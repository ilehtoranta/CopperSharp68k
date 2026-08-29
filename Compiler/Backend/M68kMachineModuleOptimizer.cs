/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kMachineModuleOptimizationStatistics(
	IReadOnlyDictionary<CilMethodIdentity, M68kMachineOptimizationStatistics>
		MethodStatistics,
	int StronglyConnectedComponents,
	int DevirtualizedCalls,
	int InlinedCalls,
	int RetainedMethods,
	IReadOnlySet<CilMethodIdentity> RetainedMethodIdentities,
	long EstimatedPreOptimizationCost,
	long EstimatedPostOptimizationCost)
{
	public static M68kMachineModuleOptimizationStatistics Empty { get; } = new(
		new Dictionary<CilMethodIdentity, M68kMachineOptimizationStatistics>(),
		0,
		0,
		0,
		0,
		new HashSet<CilMethodIdentity>(),
		0,
		0);
}

internal static class M68kMachineInliningPolicy
{
	private const int MaximumGuestMemoryWrapperCallerArguments = 8;

	public static bool AllowsGuestMemoryWrapper(CilMethod caller)
	{
		var parameterCount = caller.Signature.ParameterTypes.IsDefault
			? 0
			: caller.Signature.ParameterTypes.Length;
		return parameterCount + (caller.Signature.Header.IsInstance ? 1 : 0) <=
			MaximumGuestMemoryWrapperCallerArguments;
	}

	public static bool IsGuestMemoryIntrinsic(string? importName) =>
		importName is
			"intrinsic:aptr-read-uint8" or
			"intrinsic:aptr-read-uint16" or
			"intrinsic:aptr-read-uint32" or
			"intrinsic:aptr-write-uint8" or
			"intrinsic:aptr-write-uint16" or
			"intrinsic:aptr-write-uint32";
}

/// <summary>
/// Closed-world coordination for raw machine functions. This stage deliberately
/// owns call-graph facts even when a call is not transformed: later ABI lowering,
/// inlining, reachability, and effect summaries consume one deterministic graph.
/// </summary>
internal static class M68kMachineModuleOptimizer
{
	public static M68kMachineModuleOptimizationStatistics Run(
		IReadOnlyList<CilMethod> methods,
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions,
		CompilationModule module,
		M68kCpuTarget cpu,
		IReadOnlySet<CilMethodIdentity>? roots = null)
	{
		ArgumentNullException.ThrowIfNull(methods);
		ArgumentNullException.ThrowIfNull(functions);
		ArgumentNullException.ThrowIfNull(module);
		_ = cpu;
		var methodsByIdentity = methods.ToDictionary(static method => method.Identity);
		var estimatedBefore = functions.Values.Sum(function =>
			M68kTargetCostModel.Score(EstimateCost(function, cpu), cpu));
		var devirtualized = 0;
		RunEarlyFramePromotion(
			methodsByIdentity,
			functions,
			module,
			cpu);

		foreach (var (identity, function) in functions.OrderBy(static item =>
			item.Key.ModuleName, StringComparer.Ordinal).ThenBy(static item =>
			item.Value.DisplayName, StringComparer.Ordinal))
		{
			foreach (var block in function.Blocks.OrderBy(static block => block.Id))
			{
				for (var index = 0; index < block.Instructions.Count; index++)
				{
					var instruction = block.Instructions[index];
					if (instruction.LogicalCall is not { } logicalCall ||
						logicalCall.DispatchKind is not (
							M68kMachineCallDispatchKind.Virtual or
							M68kMachineCallDispatchKind.Interface))
					{
						continue;
					}
					var targets = ResolveClosedWorldTargets(
						instruction,
						logicalCall,
						module,
						functions);
					if (targets.Length == 0)
					{
						continue;
					}
					var dispatch = targets.Length == 1
						? M68kMachineCallDispatchKind.Direct
						: logicalCall.DispatchKind;
					if (dispatch == M68kMachineCallDispatchKind.Direct)
					{
						devirtualized++;
					}
					block.Instructions[index] = instruction with
					{
						LogicalCall = logicalCall with
						{
							DispatchKind = dispatch,
							ResolvedTargets = targets
						}
					};
				}
			}
			M68kMachineIrVerifier.Verify(function);
		}

		var graph = BuildCallGraph(functions, module);
		var components = FindStronglyConnectedComponents(graph);
		// Keep the ordering stable and explicitly bottom-up for the summary and
		// inlining stages that follow this closed-world discovery pass.
		var componentByMethod = components
			.SelectMany((component, index) => component.Select(method => (method, index)))
			.ToDictionary(static item => item.method, static item => item.index);
		var inlined = 0;
		foreach (var component in components)
		{
			foreach (var methodIdentity in component.OrderBy(id =>
				methodsByIdentity.TryGetValue(id, out var candidate)
					? candidate.DisplayName
					: id.ModuleName,
				StringComparer.Ordinal))
			{
				if (!functions.TryGetValue(methodIdentity, out var caller) ||
					!methodsByIdentity.TryGetValue(methodIdentity, out var callerMethod))
				{
					continue;
				}
				inlined += InlineScalarCalls(
					callerMethod,
					caller,
					methodsByIdentity,
					functions,
					componentByMethod,
					module,
					cpu);
			}
		}

		RunMemoryPromotionFixedPoint(
			methods,
			methodsByIdentity,
			functions,
			module,
			cpu);

		var perMethod = functions
			.Where(static item => item.Value.OptimizationStatistics is not null)
			.ToDictionary(
				static item => item.Key,
				static item => item.Value.OptimizationStatistics!);
		var retainedMethods = ComputeRetainedMethods(
			functions,
			BuildCallGraph(functions, module),
			module,
			roots ?? functions.Keys.ToHashSet());
		return new M68kMachineModuleOptimizationStatistics(
			perMethod,
			components.Count,
			devirtualized,
			inlined,
			retainedMethods.Count,
			retainedMethods,
			estimatedBefore,
			functions.Values.Sum(function =>
				M68kTargetCostModel.Score(EstimateCost(function, cpu), cpu)));
	}

	private static void RunEarlyFramePromotion(
		IReadOnlyDictionary<CilMethodIdentity, CilMethod> methodsByIdentity,
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions,
		CompilationModule module,
		M68kCpuTarget cpu)
	{
		var emptySummaries = new Dictionary<
			CilMethodIdentity,
			M68kMethodMemorySummary>();
		var emptyGlobals = new HashSet<M68kMemoryObject>();
		var emptyOwners = new Dictionary<int, M68kHeapOwnerFacts>();
		foreach (var (identity, function) in functions)
		{
			if (!methodsByIdentity.TryGetValue(identity, out var method))
			{
				continue;
			}
			M68kExactMemoryAnnotator.AnnotateFrameAndArgumentAccesses(function);
			var promotion = M68kMemoryPromotionPass.Run(
				function,
				new M68kMemoryPromotionContext(
					method,
					module,
					emptySummaries,
					emptyGlobals,
					emptyOwners,
					function,
					FrameAndArgumentOnly: true));
			if (promotion.Changed)
			{
				function.OptimizationStatistics =
					M68kMachineOptimizer.Run(function, cpu);
			}
		}
	}

	private static void RunMemoryPromotionFixedPoint(
		IReadOnlyList<CilMethod> methods,
		IReadOnlyDictionary<CilMethodIdentity, CilMethod> methodsByIdentity,
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions,
		CompilationModule module,
		M68kCpuTarget cpu)
	{
		const int maximumIterations = 8;
		for (var iteration = 0; iteration < maximumIterations; iteration++)
		{
			var summaries = M68kMethodMemorySummaryAnalyzer.Compute(
				methods,
				functions,
				module);
			var annotations = new Dictionary<
				CilMethodIdentity,
				M68kExactMemoryAnnotation>();
			foreach (var (identity, function) in functions)
			{
				if (!methodsByIdentity.TryGetValue(identity, out var method))
				{
					continue;
				}
				annotations[identity] = M68kExactMemoryAnnotator.Annotate(
					function,
					method,
					module,
					summaries);
				var trace = Environment.GetEnvironmentVariable(
					"COPPERSHARP_DIAGNOSTIC_TRACE_MEMORY_PROMOTION");
				if (!string.IsNullOrEmpty(trace) &&
					function.DisplayName.Contains(trace, StringComparison.Ordinal))
				{
					Console.Error.WriteLine(
						$"PROMOTION {iteration} {function.DisplayName}");
					foreach (var call in function.Blocks
						.SelectMany(static block => block.Instructions)
						.Where(static instruction =>
							instruction.Operation == M68kMachineOperation.Call))
					{
						Console.Error.WriteLine(
							$"CALL I{call.Id} uses=[{string.Join(',', call.Uses)}] " +
							$"defs=[{string.Join(',', call.Definitions)}] " +
							$"logicalArgs=[{string.Join(',', call.LogicalCall?.ArgumentValueIds ?? [])}]");
						foreach (var target in call.LogicalCall?.ResolvedTargets ?? [])
						{
							var targetSummary = summaries.GetValueOrDefault(target);
							Console.Error.WriteLine(
								$"  TARGET {target} parameters=" +
								string.Join(',', targetSummary?.ParameterEffects ?? []));
						}
					}
					foreach (var facts in annotations[identity].HeapOwners.Values)
					{
						Console.Error.WriteLine(
							$"OWNER v{facts.OwnerValueId} array={facts.IsArray} " +
							$"promotable={facts.IsPromotable} length={facts.ConstantLength} " +
							$"ctor={facts.ConstructorMayWrite} finalizer={facts.HasFinalizer}");
					}
					foreach (var block in function.Blocks)
					{
						foreach (var instruction in block.Instructions.Where(static item =>
							!item.ExactMemoryAccesses.IsDefaultOrEmpty))
						{
							Console.Error.WriteLine(
								$"  B{block.Id} I{instruction.Id} {instruction.Operation} " +
								$"{string.Join(';', instruction.ExactMemoryAccesses)}");
						}
					}
				}
			}
			summaries = M68kMethodMemorySummaryAnalyzer.Compute(
				methods,
				functions,
				module);
			var localGlobals = FindLocallyOwnedGlobals(functions);
			var changed = false;
			foreach (var (identity, function) in functions)
			{
				if (!methodsByIdentity.TryGetValue(identity, out var method) ||
					!annotations.TryGetValue(identity, out var annotation))
				{
					continue;
				}
				if (string.Equals(
						method.ModuleName,
						"CopperSharp.Runtime.Managed",
						StringComparison.Ordinal))
				{
					// The collector manipulates allocator metadata through raw native
					// addresses that deliberately sit outside managed owner identity.
					// Treat it as the memory-model boundary: promoting its implementation
					// fields can hide state from a collection performed by another method.
					continue;
				}
				var promotion = M68kMemoryPromotionPass.Run(
					function,
					new M68kMemoryPromotionContext(
						method,
						module,
						summaries,
						localGlobals,
						annotation.HeapOwners,
						function));
				if (!promotion.Changed)
				{
					continue;
				}
				changed = true;
				function.OptimizationStatistics =
					M68kMachineOptimizer.Run(function, cpu);
			}
			if (!changed)
			{
				break;
			}
		}
	}

	private static IReadOnlySet<M68kMemoryObject> FindLocallyOwnedGlobals(
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions)
	{
		var accesses = functions.SelectMany(item =>
			item.Value.Blocks
				.SelectMany(static block => block.Instructions)
				.SelectMany(instruction => instruction.ExactMemoryAccesses.Select(
					access => (Method: item.Key, Access: access))))
			.Where(static item => item.Access.Object.IsGlobalObject)
			.GroupBy(static item => item.Access.Object);
		var result = new HashSet<M68kMemoryObject>();
		foreach (var group in accesses)
		{
			var materialized = group.ToArray();
			if (group.Key.IsManagedRoot ||
				materialized.Select(static item => item.Method).Distinct().Count() != 1 ||
				!materialized.Any(static item => item.Access.Kind ==
					M68kExactMemoryAccessKind.Read) ||
				!materialized.Any(static item => item.Access.Kind ==
					M68kExactMemoryAccessKind.Write) ||
				materialized.Any(static item => item.Access.Kind is
					M68kExactMemoryAccessKind.Address or
					M68kExactMemoryAccessKind.Escape))
			{
				continue;
			}
			result.Add(group.Key);
		}
		return result;
	}

	private static IReadOnlySet<CilMethodIdentity> ComputeRetainedMethods(
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions,
		IReadOnlyDictionary<CilMethodIdentity, HashSet<CilMethodIdentity>> graph,
		CompilationModule module,
		IReadOnlySet<CilMethodIdentity> roots)
	{
		var retained = new HashSet<CilMethodIdentity>();
		var pending = new Stack<CilMethodIdentity>(roots.Where(functions.ContainsKey));
		while (pending.TryPop(out var identity))
		{
			if (!retained.Add(identity))
			{
				continue;
			}
			foreach (var target in graph[identity])
			{
				pending.Push(target);
			}
			if (functions[identity].Blocks
				.SelectMany(static block => block.Instructions)
				.Any(static instruction => instruction.LogicalCall is
					{
						DispatchKind: M68kMachineCallDispatchKind.Virtual or
							M68kMachineCallDispatchKind.Interface,
						ResolvedTargets.Length: <= 1
					}))
			{
				// No complete target set means the compiler's existing dispatch-table
				// construction remains authoritative. Keep its discovered functions.
				foreach (var possibleTarget in functions.Keys)
				{
					pending.Push(possibleTarget);
				}
			}
			foreach (var instruction in functions[identity].Blocks
				.SelectMany(static block => block.Instructions)
				.Where(static instruction =>
					instruction.Operation == M68kMachineOperation.FunctionAddress &&
					instruction.Origin?.SourceInstruction.Operand is int))
			{
				var origin = instruction.Origin!;
				var target = module.ResolveMethodToken(
					(int)origin.SourceInstruction.Operand!,
					origin.SourceMethod,
					origin.SourceInstruction.Offset).Definition;
				if (target is not null && functions.ContainsKey(target.Identity))
				{
					pending.Push(target.Identity);
				}
			}
		}
		return retained;
	}

	private static int InlineScalarCalls(
		CilMethod callerMethod,
		M68kMachineFunction caller,
		IReadOnlyDictionary<CilMethodIdentity, CilMethod> methods,
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions,
		IReadOnlyDictionary<CilMethodIdentity, int> componentByMethod,
		CompilationModule module,
		M68kCpuTarget cpu)
	{
		var count = 0;
		foreach (var block in caller.Blocks.OrderBy(static block => block.Id))
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var call = block.Instructions[index];
				if (call.Operation != M68kMachineOperation.Call ||
					call.LogicalCall is not
					{
						DispatchKind: M68kMachineCallDispatchKind.Direct or
							M68kMachineCallDispatchKind.Constrained,
						ResolvedTargets: [var targetIdentity]
					} logicalCall ||
					!methods.TryGetValue(targetIdentity, out var targetMethod) ||
					!functions.TryGetValue(targetIdentity, out var target))
				{
					continue;
				}
				var constrainedValueTypeCall =
					logicalCall.DispatchKind == M68kMachineCallDispatchKind.Constrained &&
					module.GetMethodDeclaringType(targetMethod).Kind ==
						CilTypeKind.ValueType;
				if (logicalCall.RequiresNullCheck && !constrainedValueTypeCall ||
					componentByMethod[callerMethod.Identity] == componentByMethod[targetIdentity] ||
					(targetMethod.ImplAttributes & MethodImplAttributes.NoInlining) != 0)
				{
					continue;
				}
				if (TryRewriteExactGuestMemoryWrapperCall(
						callerMethod,
						caller,
						block,
						index,
						targetMethod,
						target,
						call,
						logicalCall,
						module))
				{
					count++;
					continue;
				}
				var aggressive = (targetMethod.ImplAttributes &
					MethodImplAttributes.AggressiveInlining) != 0;
				var trivialValueTypeConstructor =
					IsTrivialValueTypeConstructor(
						targetMethod,
						logicalCall,
						module);
				if (!TryGetScalarInlineBody(
						target,
						logicalCall,
						module,
						aggressive,
						trivialValueTypeConstructor,
						out var body,
						out var arguments,
						out var returnValues,
						out var guestMemoryIntrinsicWrapper))
				{
					continue;
				}
				if (logicalCall.DispatchKind == M68kMachineCallDispatchKind.Constrained &&
					!trivialValueTypeConstructor &&
					(!guestMemoryIntrinsicWrapper ||
					 !M68kMachineInliningPolicy.AllowsGuestMemoryWrapper(callerMethod)))
				{
					continue;
				}

				var candidateLimit = aggressive ? 300 : 120;
				if (body.Count > candidateLimit)
				{
					continue;
				}
				var first = index;
				while (first > 0 && IsCallAbiPrefix(
					block.Instructions[first - 1],
					call,
					logicalCall.ArgumentValueIds))
				{
					first--;
				}
				var last = index;
				while (last + 1 < block.Instructions.Count &&
					IsCallAbiSuffix(
						block.Instructions[last + 1],
						logicalCall.ResultValueIds))
				{
					last++;
				}
				var removedCount = last - first + 1;
				var insertedCount = body.Count +
					(logicalCall.ResultValueIds.Length == 0 ? 0 : 1);
				var delta = insertedCount - removedCount;
				var beforeCost = M68kTargetCostModel.Estimate(
					block.Instructions.Skip(first).Take(removedCount),
					cpu,
					block.LoopDepth);
				var afterCost = M68kTargetCostModel.Estimate(
					body,
					cpu,
					block.LoopDepth) +
					(logicalCall.ResultValueIds.Length == 0
						? new M68kTargetCost()
						: M68kTargetCostModel.Estimate(
							[call with
								{
									Operation = M68kMachineOperation.Copy,
									Uses = [],
									Definitions = []
								}],
							cpu,
							block.LoopDepth));
				// The raw constructor body still carries incoming-ABI copies. Once
				// cloned, the local optimizer coalesces those copies around the one
				// field store. Counting them as lasting growth would retain a BSR/RTS
				// pair for a strictly smaller, faster local store.
				var forceInline = guestMemoryIntrinsicWrapper ||
					trivialValueTypeConstructor;
				if (!forceInline &&
					(delta > 0 ||
					 !M68kTargetCostModel.Accept(beforeCost, afterCost, cpu)))
				{
					continue;
				}

				var replacements = CloneScalarInlineBody(
					callerMethod,
					caller,
					call,
					logicalCall,
					body,
					arguments,
					returnValues,
					target);
				var removedInstructions = block.Instructions
					.Skip(first)
					.Take(removedCount)
					.ToArray();
				block.Instructions.RemoveRange(first, removedCount);
				block.Instructions.InsertRange(first, replacements);
				try
				{
					M68kMachineIrVerifier.Verify(caller);
				}
				catch (InvalidOperationException)
				{
					// Inlining is optional. Restore the exact ABI sequence if this
					// candidate exposes an unmodelled shared staging/result value.
					block.Instructions.RemoveRange(first, replacements.Count);
					block.Instructions.InsertRange(first, removedInstructions);
					continue;
				}
				index = first + replacements.Count - 1;
				count++;
			}
		}
		if (count != 0)
		{
			caller.OptimizationStatistics = M68kMachineOptimizer.Run(caller, cpu);
		}
		return count;
	}

	private static bool TryRewriteExactGuestMemoryWrapperCall(
		CilMethod callerMethod,
		M68kMachineFunction caller,
		M68kMachineBlock block,
		int callIndex,
		CilMethod targetMethod,
		M68kMachineFunction target,
		M68kMachineInstruction call,
		M68kMachineLogicalCall wrapperCall,
		CompilationModule module)
	{
		if (target.ExceptionRegions.Count != 0 || target.HasDynamicStackAllocation ||
			!TryGetStraightLineInstructions(target, out var instructions))
		{
			return false;
		}

		if (instructions.LastOrDefault() is not
			{
				Operation: M68kMachineOperation.Return
			} returnInstruction ||
			instructions.Count(static instruction =>
				instruction.Operation == M68kMachineOperation.Return) != 1)
		{
			return false;
		}

		var intrinsicCandidates = instructions.Where(instruction =>
			IsGuestMemoryIntrinsic(instruction, module)).ToArray();
		if (intrinsicCandidates is not [var intrinsic] ||
			intrinsic.LogicalCall is not
			{
				DispatchKind: M68kMachineCallDispatchKind.Import,
				ResolvedTargets.Length: 0,
				RequiresNullCheck: false
			} intrinsicCall ||
			intrinsic.Immediate is not null ||
			intrinsic.Uses.Length != intrinsicCall.ArgumentValueIds.Length ||
			intrinsic.Definitions.Length != intrinsicCall.ResultValueIds.Length ||
			!intrinsic.Definitions.SequenceEqual(intrinsicCall.ResultValueIds) ||
			!returnInstruction.Uses.SequenceEqual(intrinsicCall.ResultValueIds))
		{
			return false;
		}
		var instanceArgumentCount = targetMethod.Signature.Header.IsInstance ? 1 : 0;
		var parameterCount = targetMethod.Signature.ParameterTypes.IsDefault
			? 0
			: targetMethod.Signature.ParameterTypes.Length;
		if (intrinsicCall.ArgumentValueIds.Length != parameterCount ||
			wrapperCall.ArgumentValueIds.Length != parameterCount + instanceArgumentCount ||
			call.Uses.Length != wrapperCall.ArgumentValueIds.Length ||
			wrapperCall.ResultValueIds.Length != intrinsicCall.ResultValueIds.Length ||
			call.Definitions.Length != intrinsic.Definitions.Length)
		{
			return false;
		}

		var argumentValues = new Dictionary<int, int>();
		foreach (var argument in instructions.Where(static instruction =>
			instruction.Operation == M68kMachineOperation.Argument))
		{
			if (argument.ArgumentIndex is not { } argumentIndex ||
				argument.Uses.Length != 0 ||
				argument.Definitions is not [var definition] ||
				!argumentValues.TryAdd(argumentIndex, definition))
			{
				return false;
			}
		}

		var admittedInstructions = new HashSet<int> { intrinsic.Id };
		var operandPlans = new List<(
			int WrapperArgumentIndex,
			IReadOnlyList<M68kMachineInstruction> ArgumentCopies,
			int PhysicalUse,
			M68kMachineInstruction? StagingCopy)>();
		for (var parameterIndex = 0; parameterIndex < parameterCount; parameterIndex++)
		{
			var wrapperArgumentIndex = instanceArgumentCount + parameterIndex;
			var argumentValue = intrinsicCall.ArgumentValueIds[parameterIndex];
			if (!TryTraceExactArgument(
					argumentValue,
					wrapperArgumentIndex,
					argumentValues,
					instructions,
					admittedInstructions,
					out var argumentDefinition,
					out var argumentCopies))
			{
				return false;
			}

			var physicalUse = intrinsic.Uses[parameterIndex];
			M68kMachineInstruction? stagingCopy = null;
			if (physicalUse != argumentValue)
			{
				var stagingCopies = instructions.Where(instruction =>
					instruction.Definitions.Contains(physicalUse)).ToArray();
				if (stagingCopies is not [var candidateStagingCopy] ||
					!IsExactCopy(candidateStagingCopy) ||
					candidateStagingCopy.Uses is not [var source] ||
					source != argumentValue ||
					candidateStagingCopy.Definitions is not [var definition] ||
					definition != physicalUse)
				{
					return false;
				}
				stagingCopy = candidateStagingCopy;
				if (!admittedInstructions.Add(stagingCopy.Id))
				{
					return false;
				}
			}

			if (!HaveCompatibleRepresentation(
					caller.Values[wrapperCall.ArgumentValueIds[wrapperArgumentIndex]],
					target.Values[argumentDefinition]))
			{
				return false;
			}
			operandPlans.Add((
				wrapperArgumentIndex,
				argumentCopies,
				physicalUse,
				stagingCopy));
		}
		for (var resultIndex = 0; resultIndex < intrinsic.Definitions.Length;
			resultIndex++)
		{
			if (!CanSubstituteMachineValue(
					caller.Values[call.Definitions[resultIndex]],
					target.Values[intrinsic.Definitions[resultIndex]]))
			{
				return false;
			}
		}

		if (instructions.Any(instruction =>
			instruction.Operation is not M68kMachineOperation.Argument and not
				M68kMachineOperation.Return &&
			!admittedInstructions.Contains(instruction.Id)))
		{
			return false;
		}
		var intrinsicOrigin = (intrinsic.Origin ??
			target.OriginAt(intrinsic.IlOffset, intrinsic.SourceInstruction))?
			.AtInlineSite(callerMethod, call.Origin!.SourceInstruction);
		if (intrinsicOrigin is null)
		{
			return false;
		}
		if (operandPlans.Any(operand =>
			operand.StagingCopy is null && operand.ArgumentCopies.Count == 0 &&
			!CanSubstituteMachineValue(
				caller.Values[wrapperCall.ArgumentValueIds[
					operand.WrapperArgumentIndex]],
				target.Values[operand.PhysicalUse]) &&
			!CanSubstituteMachineValue(
				caller.Values[call.Uses[operand.WrapperArgumentIndex]],
				target.Values[operand.PhysicalUse])))
		{
			return false;
		}
		var mappedPhysicalUses = ImmutableArray.CreateBuilder<int>(parameterCount);
		var mappedArguments = ImmutableArray.CreateBuilder<int>(parameterCount);
		var insertedCopies = new List<M68kMachineInstruction>();
		var insertedValues = new List<int>();
		var copyOrigins = new Dictionary<int, M68kMachineInstructionOrigin>();
		foreach (var sourceCopy in operandPlans
			.SelectMany(static operand => operand.ArgumentCopies.Concat(
				operand.StagingCopy is null ? [] : [operand.StagingCopy]))
			.DistinctBy(static sourceCopy => sourceCopy.Id))
		{
			var copyOrigin = (sourceCopy.Origin ?? target.OriginAt(
				sourceCopy.IlOffset,
					sourceCopy.SourceInstruction))?.AtInlineSite(
					callerMethod,
					call.Origin!.SourceInstruction);
			if (copyOrigin is null)
			{
				return false;
			}
			copyOrigins.Add(sourceCopy.Id, copyOrigin);
		}
		foreach (var operand in operandPlans)
		{
			var mappedArgument = wrapperCall.ArgumentValueIds[
				operand.WrapperArgumentIndex];
			foreach (var argumentCopy in operand.ArgumentCopies)
			{
				mappedArgument = CloneCopy(argumentCopy, mappedArgument);
			}
			mappedArguments.Add(mappedArgument);

			var existingPhysicalUse = call.Uses[operand.WrapperArgumentIndex];
			var targetPhysicalValue = target.Values[operand.PhysicalUse];
			if (operand.StagingCopy is not { } sourceStaging)
			{
				if (CanSubstituteMachineValue(
					caller.Values[mappedArgument],
					targetPhysicalValue))
				{
					mappedPhysicalUses.Add(mappedArgument);
					continue;
				}
				if (operand.ArgumentCopies.Count == 0 &&
					CanSubstituteMachineValue(
						caller.Values[existingPhysicalUse],
						targetPhysicalValue))
				{
					mappedPhysicalUses.Add(existingPhysicalUse);
					continue;
				}
				// A non-empty exact copy chain ends in ArgumentValue, which is
				// also PhysicalUse when the intrinsic has no separate staging
				// copy. The cloned value therefore has the target constraint.
				mappedPhysicalUses.Add(mappedArgument);
				continue;
			}
			mappedPhysicalUses.Add(CloneCopy(sourceStaging, mappedArgument));
		}
		var mappedCall = intrinsicCall with
		{
			ArgumentValueIds = mappedArguments.MoveToImmutable(),
			ResultValueIds = wrapperCall.ResultValueIds,
			Origin = intrinsicOrigin
		};
		var replacement = call with
		{
			Operation = intrinsic.Operation,
			Uses = mappedPhysicalUses.MoveToImmutable(),
			Clobbers = intrinsic.Clobbers,
			MemoryEffect = intrinsic.MemoryEffect,
			IsSafepoint = intrinsic.IsSafepoint,
			MayThrow = intrinsic.MayThrow,
			ProducesConditionCodes = intrinsic.ProducesConditionCodes,
			ConsumesConditionCodes = intrinsic.ConsumesConditionCodes,
			SourceInstruction = intrinsic.SourceInstruction,
			SpillSlotIndex = intrinsic.SpillSlotIndex,
			ArgumentIndex = intrinsic.ArgumentIndex,
			StackVarargsRegister = intrinsic.StackVarargsRegister,
			Immediate = intrinsic.Immediate,
			AllowCopyCoalescing = intrinsic.AllowCopyCoalescing,
			TransportsManagedByrefOwner = intrinsic.TransportsManagedByrefOwner,
			BranchCondition = intrinsic.BranchCondition,
			RequiresLiveCallerFrame = intrinsic.RequiresLiveCallerFrame,
			ConstantValue = intrinsic.ConstantValue,
			Origin = intrinsicOrigin,
			LogicalCall = mappedCall
		};
		block.Instructions.InsertRange(callIndex, insertedCopies);
		block.Instructions[callIndex + insertedCopies.Count] = replacement;
		try
		{
			M68kMachineIrVerifier.Verify(caller);
		}
		catch (InvalidOperationException)
		{
			block.Instructions.RemoveRange(callIndex, insertedCopies.Count);
			block.Instructions[callIndex] = call;
			foreach (var value in insertedValues)
			{
				caller.ManagedByrefTypes.Remove(value);
				caller.Values.Remove(value);
			}
			return false;
		}
		return true;

		int CloneCopy(M68kMachineInstruction sourceCopy, int sourceValue)
		{
			var targetValue = target.Values[sourceCopy.Definitions[0]];
			var clonedValue = caller.CreateValue(
				targetValue.Kind,
				targetValue.Width,
				targetValue.AllowedRegisters,
				targetValue.PrecoloredRegister,
				targetValue.IsGcReference,
				targetValue.IsRematerializable,
				targetValue.SpillWeight,
				targetValue.IsSpillTemporary);
			insertedValues.Add(clonedValue.Id);
			if (target.ManagedByrefTypes.TryGetValue(
				targetValue.Id,
				out var managedByrefType))
			{
				caller.ManagedByrefTypes.Add(clonedValue.Id, managedByrefType);
			}
			insertedCopies.Add(caller.CreateInstruction(
				sourceCopy.Operation,
				call.IlOffset,
				uses: [sourceValue],
				definitions: [clonedValue.Id],
				clobbers: sourceCopy.Clobbers,
				memoryEffect: sourceCopy.MemoryEffect,
				isSafepoint: sourceCopy.IsSafepoint,
				mayThrow: sourceCopy.MayThrow,
				producesConditionCodes: sourceCopy.ProducesConditionCodes,
				consumesConditionCodes: sourceCopy.ConsumesConditionCodes,
				sourceInstruction: sourceCopy.SourceInstruction,
				spillSlotIndex: sourceCopy.SpillSlotIndex,
				argumentIndex: sourceCopy.ArgumentIndex,
				stackVarargsRegister: sourceCopy.StackVarargsRegister,
				immediate: sourceCopy.Immediate,
				allowCopyCoalescing: sourceCopy.AllowCopyCoalescing,
				transportsManagedByrefOwner:
					sourceCopy.TransportsManagedByrefOwner,
				branchCondition: sourceCopy.BranchCondition,
				requiresLiveCallerFrame: sourceCopy.RequiresLiveCallerFrame,
				constantValue: sourceCopy.ConstantValue,
				origin: copyOrigins[sourceCopy.Id]));
			return clonedValue.Id;
		}

		static bool CanSubstituteMachineValue(
			M68kMachineValue replacement,
			M68kMachineValue original) =>
			HaveCompatibleRepresentation(replacement, original) &&
			replacement.AllowedRegisters.Except(original.AllowedRegisters).IsEmpty;

		static bool HaveCompatibleRepresentation(
			M68kMachineValue replacement,
			M68kMachineValue original) =>
			replacement.Kind == original.Kind &&
			replacement.Width == original.Width &&
			replacement.IsGcReference == original.IsGcReference;

		static bool TryTraceExactArgument(
			int value,
			int expectedArgumentIndex,
			IReadOnlyDictionary<int, int> argumentValues,
			IReadOnlyList<M68kMachineInstruction> instructions,
			ISet<int> admittedInstructions,
			out int argumentDefinition,
			out IReadOnlyList<M68kMachineInstruction> argumentCopies)
		{
			argumentDefinition = -1;
			argumentCopies = [];
			var copies = new List<M68kMachineInstruction>();
			var visited = new HashSet<int>();
			while (visited.Add(value))
			{
				if (argumentValues.TryGetValue(expectedArgumentIndex, out var argument) &&
					argument == value)
				{
					argumentDefinition = argument;
					copies.Reverse();
					argumentCopies = copies;
					return true;
				}
				var definitions = instructions.Where(instruction =>
					instruction.Definitions.Contains(value)).ToArray();
				if (definitions is not [var copy] ||
					!IsExactCopy(copy) ||
					copy.Uses is not [var source])
				{
					return false;
				}
				admittedInstructions.Add(copy.Id);
				copies.Add(copy);
				value = source;
			}
			return false;
		}

		static bool IsExactCopy(M68kMachineInstruction instruction) =>
			instruction.Operation == M68kMachineOperation.Copy &&
			instruction.Uses.Length == 1 &&
			instruction.Definitions.Length == 1 &&
			instruction.MemoryEffect == M68kMachineMemoryEffect.None &&
			!instruction.IsSafepoint && !instruction.MayThrow &&
			!instruction.ProducesConditionCodes &&
			!instruction.ConsumesConditionCodes &&
			!instruction.RequiresLiveCallerFrame &&
			instruction.LogicalCall is null;

		static bool TryGetStraightLineInstructions(
			M68kMachineFunction function,
			out IReadOnlyList<M68kMachineInstruction> instructions)
		{
			instructions = [];
			if (function.Blocks.Count == 0 ||
				function.Blocks.Any(static candidate =>
					candidate.Phis.Count != 0 || candidate.IsExceptionEntry ||
					candidate.PredecessorEdges.Any(static edge =>
						edge.Kind != M68kMachineEdgeKind.Normal) ||
					candidate.SuccessorEdges.Any(static edge =>
						edge.Kind != M68kMachineEdgeKind.Normal) ||
					candidate.Successors.Count > 1))
			{
				return false;
			}

			var blocks = function.Blocks.ToDictionary(static candidate => candidate.Id);
			if (!blocks.TryGetValue(function.EntryBlockId, out var current))
			{
				return false;
			}
			var visited = new HashSet<int>();
			var ordered = new List<M68kMachineInstruction>();
			int? predecessor = null;
			while (visited.Add(current.Id))
			{
				if (predecessor is null
					? current.Predecessors.Count != 0
					: current.Predecessors is not [var source] ||
					  source != predecessor.Value)
				{
					return false;
				}
				ordered.AddRange(current.Instructions);
				if (current.Successors.Count == 0)
				{
					instructions = ordered;
					return visited.Count == function.Blocks.Count;
				}
				predecessor = current.Id;
				if (!blocks.TryGetValue(current.Successors[0], out current))
				{
					return false;
				}
			}
			return false;
		}
	}

	private static bool IsTrivialValueTypeConstructor(
		CilMethod constructor,
		M68kMachineLogicalCall logicalCall,
		CompilationModule module)
	{
		if (constructor.Name != ".ctor" ||
			!constructor.Signature.Header.IsInstance ||
			!constructor.Signature.ReturnType.IsVoid ||
			constructor.Signature.ParameterTypes is not [var parameter] ||
			parameter.Kind != CilTypeKind.UnsignedInteger ||
			parameter.Size != 4 ||
			constructor.Locals.Length != 0 ||
			constructor.ExceptionRegions.Count != 0 ||
			module.GetMethodDeclaringType(constructor).Kind != CilTypeKind.ValueType ||
			logicalCall.ArgumentValueIds.Length != 2 ||
			logicalCall.ResultValueIds.Length != 0)
		{
			return false;
		}

		var body = constructor.Instructions
			.Where(static instruction => instruction.OpCode != OpCodes.Nop)
			.ToArray();
		if (body is not [var loadThis, var loadValue, var store, var returnInstruction] ||
			!TryGetArgumentIndex(loadThis, out var thisIndex) ||
			thisIndex != 0 ||
			!TryGetArgumentIndex(loadValue, out var valueIndex) ||
			valueIndex != 1 ||
			store.OpCode != OpCodes.Stfld ||
			returnInstruction.OpCode != OpCodes.Ret)
		{
			return false;
		}

		var field = module.ResolveFieldToken(
			(int)store.Operand!,
			constructor,
			store.Offset);
		return !field.IsStatic &&
			field.DeclaringType == constructor.DeclaringType &&
			string.Equals(
				field.ModuleName,
				constructor.ModuleName,
				StringComparison.Ordinal) &&
			field.Type.Kind == CilTypeKind.UnsignedInteger &&
			field.Type.Size == 4;

		static bool TryGetArgumentIndex(
			CilInstruction instruction,
			out int index)
		{
			var op = instruction.OpCode;
			if (op == OpCodes.Ldarg_0)
			{
				index = 0;
				return true;
			}
			if (op == OpCodes.Ldarg_1)
			{
				index = 1;
				return true;
			}
			if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_S)
			{
				index = Convert.ToInt32(instruction.Operand);
				return true;
			}
			index = default;
			return false;
		}
	}

	private static bool TryGetScalarInlineBody(
		M68kMachineFunction target,
		M68kMachineLogicalCall logicalCall,
		CompilationModule module,
		bool allowGuestMemoryIntrinsic,
		bool allowTrivialValueTypeConstructor,
		out IReadOnlyList<M68kMachineInstruction> body,
		out IReadOnlyDictionary<int, int> arguments,
		out ImmutableArray<int> returnValues,
		out bool guestMemoryIntrinsicWrapper)
	{
		body = [];
		arguments = new Dictionary<int, int>();
		returnValues = [];
		guestMemoryIntrinsicWrapper = false;
		if (target.Blocks.Count != 1 || target.ExceptionRegions.Count != 0 ||
			target.HasDynamicStackAllocation || target.Blocks[0].Phis.Count != 0)
		{
			return false;
		}
		var instructions = target.Blocks[0].Instructions;
		if (instructions.LastOrDefault() is not
			{
				Operation: M68kMachineOperation.Return
			} returnInstruction ||
			returnInstruction.Uses.Length != logicalCall.ResultValueIds.Length)
		{
			return false;
		}
		var argumentMap = new Dictionary<int, int>();
		foreach (var argument in instructions.Where(static instruction =>
			instruction.Operation == M68kMachineOperation.Argument))
		{
			if (argument.ArgumentIndex is not { } argumentIndex ||
				argument.Definitions is not [var definition] ||
				argumentIndex < 0 || argumentIndex >= logicalCall.ArgumentValueIds.Length ||
				!argumentMap.TryAdd(definition, logicalCall.ArgumentValueIds[argumentIndex]))
			{
				return false;
			}
		}
		var inlineBody = instructions
			.Where(static instruction => instruction.Operation is not
				M68kMachineOperation.Argument and not M68kMachineOperation.Return)
			.ToArray();
		var guestMemoryIntrinsicCount = inlineBody.Count(instruction =>
			IsGuestMemoryIntrinsic(instruction, module));
		var trivialConstructorStoreCount = inlineBody.Count(
			IsTrivialValueTypeConstructorStore);
		var isTrivialValueTypeConstructorBody =
			allowTrivialValueTypeConstructor &&
			trivialConstructorStoreCount == 1 &&
			inlineBody.All(instruction =>
				IsPureScalarInstruction(instruction) ||
				IsTrivialValueTypeConstructorStore(instruction));
		if (inlineBody.Any(instruction =>
			!IsPureScalarInstruction(instruction) &&
			!(isTrivialValueTypeConstructorBody &&
			  IsTrivialValueTypeConstructorStore(instruction)) &&
			!(allowGuestMemoryIntrinsic &&
			  IsGuestMemoryIntrinsic(instruction, module))) ||
			guestMemoryIntrinsicCount > 1 ||
			guestMemoryIntrinsicCount != 0 && inlineBody.Length > 8)
		{
			return false;
		}
		guestMemoryIntrinsicWrapper = guestMemoryIntrinsicCount == 1;
		body = inlineBody;
		arguments = argumentMap;
		returnValues = returnInstruction.Uses;
		return true;

		static bool IsPureScalarInstruction(M68kMachineInstruction instruction) =>
			instruction.Operation is (
				M68kMachineOperation.Copy or
				M68kMachineOperation.Constant or
				M68kMachineOperation.Add or
				M68kMachineOperation.Subtract or
				M68kMachineOperation.Multiply or
				M68kMachineOperation.And or
				M68kMachineOperation.Or or
				M68kMachineOperation.Xor or
				M68kMachineOperation.Negate or
				M68kMachineOperation.Not or
				M68kMachineOperation.Shift or
				M68kMachineOperation.Compare or
				M68kMachineOperation.Convert) &&
			instruction.MemoryEffect == M68kMachineMemoryEffect.None &&
			!instruction.MayThrow && !instruction.IsSafepoint &&
			!instruction.RequiresLiveCallerFrame &&
			instruction.LogicalCall is null;

		static bool IsTrivialValueTypeConstructorStore(
			M68kMachineInstruction instruction) =>
			instruction.Operation == M68kMachineOperation.Store &&
			instruction.SourceInstruction?.OpCode == OpCodes.Stfld &&
			instruction.Uses.Length == 2 &&
			instruction.Definitions.Length == 0 &&
			instruction.MemoryEffect == M68kMachineMemoryEffect.Write &&
			!instruction.IsSafepoint &&
			instruction.MayThrow &&
			!instruction.RequiresLiveCallerFrame &&
			instruction.LogicalCall is null;

	}

	private static bool IsGuestMemoryIntrinsic(
		M68kMachineInstruction instruction,
		CompilationModule module)
	{
		if (instruction.Operation != M68kMachineOperation.Call ||
			instruction.LogicalCall is null ||
			instruction.RequiresLiveCallerFrame ||
			instruction.Origin is not { SourceInstruction.Operand: int token } origin ||
			origin.SourceInstruction.OpCode != OpCodes.Call &&
			origin.SourceInstruction.OpCode != OpCodes.Callvirt)
		{
			return false;
		}
		var reference = module.ResolveMethodToken(
			token, origin.SourceMethod, origin.SourceInstruction.Offset);
		return M68kMachineInliningPolicy.IsGuestMemoryIntrinsic(
			reference.ImportName);
	}

	private static List<M68kMachineInstruction> CloneScalarInlineBody(
		CilMethod callerMethod,
		M68kMachineFunction caller,
		M68kMachineInstruction call,
		M68kMachineLogicalCall logicalCall,
		IReadOnlyList<M68kMachineInstruction> body,
		IReadOnlyDictionary<int, int> arguments,
		ImmutableArray<int> returnValues,
		M68kMachineFunction target)
	{
		var values = new Dictionary<int, int>(arguments);
		var result = new List<M68kMachineInstruction>();
		foreach (var instruction in body)
		{
			foreach (var definition in instruction.Definitions)
			{
				var source = target.Values[definition];
				var clone = caller.CreateValue(
					source.Kind,
					source.Width,
					source.AllowedRegisters,
					isGcReference: source.IsGcReference,
					isRematerializable: source.IsRematerializable,
					spillWeight: source.SpillWeight);
				values.Add(definition, clone.Id);
			}
		}
		foreach (var instruction in body)
		{
			var origin = (instruction.Origin ??
				target.OriginAt(instruction.IlOffset, instruction.SourceInstruction))?
				.AtInlineSite(callerMethod, call.Origin!.SourceInstruction);
			var nestedLogicalCall = instruction.LogicalCall is { } nested
				? nested with
				{
					ArgumentValueIds = nested.ArgumentValueIds
						.Select(value => values[value]).ToImmutableArray(),
					ResultValueIds = nested.ResultValueIds
						.Select(value => values[value]).ToImmutableArray(),
					Origin = origin!
				}
				: null;
			result.Add(caller.CreateInstruction(
				instruction.Operation,
				call.IlOffset,
				instruction.Uses.Select(use => values[use]),
				instruction.Definitions.Select(definition => values[definition]),
				instruction.Clobbers,
				instruction.MemoryEffect,
				instruction.IsSafepoint,
				instruction.MayThrow,
				instruction.ProducesConditionCodes,
				instruction.ConsumesConditionCodes,
				instruction.SourceInstruction,
				instruction.SpillSlotIndex,
				instruction.ArgumentIndex,
				instruction.StackVarargsRegister,
				instruction.Immediate,
				instruction.AllowCopyCoalescing,
				instruction.TransportsManagedByrefOwner,
				instruction.BranchCondition,
				instruction.RequiresLiveCallerFrame,
				instruction.ConstantValue,
				origin,
				nestedLogicalCall));
		}
		for (var index = 0; index < logicalCall.ResultValueIds.Length; index++)
		{
			var mappedReturn = values.GetValueOrDefault(
				returnValues[index],
				arguments.GetValueOrDefault(returnValues[index]));
			result.Add(caller.CreateInstruction(
				M68kMachineOperation.Copy,
				call.IlOffset,
				uses: [mappedReturn],
				definitions: [logicalCall.ResultValueIds[index]],
				origin: call.Origin));
		}
		return result;
	}

	private static bool IsCallAbiPrefix(
		M68kMachineInstruction instruction,
		M68kMachineInstruction call,
		ImmutableArray<int> logicalArguments) =>
		instruction.IlOffset == call.IlOffset &&
		(instruction.Operation == M68kMachineOperation.OutgoingArgumentPush ||
		 instruction.Operation == M68kMachineOperation.Copy &&
		 instruction.SourceInstruction?.Offset == call.SourceInstruction?.Offset &&
		 instruction.Definitions.Any(call.Uses.Contains) &&
		 instruction.Definitions.All(definition =>
			!logicalArguments.Contains(definition)));

	private static bool IsCallAbiSuffix(
		M68kMachineInstruction instruction,
		ImmutableArray<int> logicalResults) =>
		instruction.Operation == M68kMachineOperation.OutgoingArgumentCleanup ||
		instruction.Operation == M68kMachineOperation.Copy &&
			instruction.Definitions.Any(logicalResults.Contains);

	private static int InstructionCount(M68kMachineFunction function) =>
		function.Blocks.Sum(static block => block.Instructions.Count);

	private static ImmutableArray<CilMethodIdentity> ResolveClosedWorldTargets(
		M68kMachineInstruction instruction,
		M68kMachineLogicalCall logicalCall,
		CompilationModule module,
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions)
	{
		if (instruction.Origin is not { } origin ||
			origin.SourceInstruction is not { Operand: int token } source ||
			source.OpCode != OpCodes.Callvirt)
		{
			return logicalCall.ResolvedTargets;
		}
		var declaration = module.ResolveMethodToken(
			token,
			origin.SourceMethod,
			source.Offset).Definition;
		if (declaration is null)
		{
			return logicalCall.ResolvedTargets;
		}
		IEnumerable<CilMethod> candidates;
		try
		{
			candidates = declaration.DeclaringTypeIsInterface
				? module.GetInterfaceImplementations(declaration)
				: module.GetVirtualImplementations(declaration);
		}
		catch (M68kCompilationException)
		{
			// Some framework-private helper methods are emitted with virtual-shaped
			// metadata but deliberately have no runtime dispatch slot. Preserve the
			// existing call and reachability behavior for that admitted boundary.
			return logicalCall.ResolvedTargets;
		}
		if (!declaration.IsAbstract)
		{
			candidates = candidates.Prepend(declaration);
		}
		return candidates
			.Select(static candidate => candidate.Identity)
			.Where(functions.ContainsKey)
			.Distinct()
			.OrderBy(static identity => identity.ModuleName, StringComparer.Ordinal)
			.ThenBy(static identity => identity.Handle.GetHashCode())
			.ThenBy(static identity => identity.Construction, StringComparer.Ordinal)
			.ToImmutableArray();
	}

	private static Dictionary<CilMethodIdentity, HashSet<CilMethodIdentity>>
		BuildCallGraph(
			IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions,
			CompilationModule module)
	{
		var graph = functions.Keys.ToDictionary(
			static identity => identity,
			static _ => new HashSet<CilMethodIdentity>());
		foreach (var (caller, function) in functions)
		{
			foreach (var target in function.Blocks
				.SelectMany(static block => block.Instructions)
				.SelectMany(static instruction =>
					instruction.LogicalCall?.ResolvedTargets ?? []))
			{
				if (functions.ContainsKey(target))
				{
					graph[caller].Add(target);
				}
			}
			foreach (var instruction in function.Blocks
				.SelectMany(static block => block.Instructions)
				.Where(static instruction =>
					instruction.Operation == M68kMachineOperation.TypeInitialize &&
					instruction.Origin?.SourceInstruction.Operand is int))
			{
				var origin = instruction.Origin!;
				var initializer = module.GetTriggeredTypeInitializer(
					origin.SourceMethod,
					origin.SourceInstruction);
				if (initializer is not null && functions.ContainsKey(initializer.Identity))
				{
					graph[caller].Add(initializer.Identity);
				}
			}
		}
		return graph;
	}

	private static List<List<CilMethodIdentity>> FindStronglyConnectedComponents(
		IReadOnlyDictionary<CilMethodIdentity, HashSet<CilMethodIdentity>> graph)
	{
		var nextIndex = 0;
		var indices = new Dictionary<CilMethodIdentity, int>();
		var lowLinks = new Dictionary<CilMethodIdentity, int>();
		var stack = new Stack<CilMethodIdentity>();
		var onStack = new HashSet<CilMethodIdentity>();
		var result = new List<List<CilMethodIdentity>>();
		foreach (var vertex in graph.Keys.OrderBy(static identity =>
			identity.ModuleName, StringComparer.Ordinal).ThenBy(static identity =>
			identity.Handle.GetHashCode()))
		{
			if (!indices.ContainsKey(vertex))
			{
				Visit(vertex);
			}
		}
		return result;

		void Visit(CilMethodIdentity vertex)
		{
			indices[vertex] = nextIndex;
			lowLinks[vertex] = nextIndex++;
			stack.Push(vertex);
			onStack.Add(vertex);
			foreach (var target in graph[vertex].OrderBy(static identity =>
				identity.ModuleName, StringComparer.Ordinal).ThenBy(static identity =>
				identity.Handle.GetHashCode()))
			{
				if (!indices.ContainsKey(target))
				{
					Visit(target);
					lowLinks[vertex] = Math.Min(lowLinks[vertex], lowLinks[target]);
				}
				else if (onStack.Contains(target))
				{
					lowLinks[vertex] = Math.Min(lowLinks[vertex], indices[target]);
				}
			}
			if (lowLinks[vertex] != indices[vertex])
			{
				return;
			}
			var component = new List<CilMethodIdentity>();
			CilMethodIdentity member;
			do
			{
				member = stack.Pop();
				onStack.Remove(member);
				component.Add(member);
			}
			while (member != vertex);
			result.Add(component);
		}
	}

	private static M68kTargetCost EstimateCost(
		M68kMachineFunction function,
		M68kCpuTarget cpu) =>
		function.Blocks.Aggregate(
			new M68kTargetCost(),
			(cost, block) => cost + M68kTargetCostModel.Estimate(
				block.Instructions,
				cpu,
				block.LoopDepth));
}
