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
				var aggressive = (targetMethod.ImplAttributes &
					MethodImplAttributes.AggressiveInlining) != 0;
				if (!TryGetScalarInlineBody(
						target,
						logicalCall,
						module,
						aggressive,
						out var body,
						out var arguments,
						out var returnValues,
						out var guestMemoryIntrinsicWrapper))
				{
					continue;
				}
				if (logicalCall.DispatchKind == M68kMachineCallDispatchKind.Constrained &&
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
				var forceIntrinsicWrapper = guestMemoryIntrinsicWrapper;
				if (!forceIntrinsicWrapper &&
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

	private static bool TryGetScalarInlineBody(
		M68kMachineFunction target,
		M68kMachineLogicalCall logicalCall,
		CompilationModule module,
		bool allowGuestMemoryIntrinsic,
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
		if (inlineBody.Any(instruction =>
			!IsPureScalarInstruction(instruction) &&
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

		static bool IsGuestMemoryIntrinsic(
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
