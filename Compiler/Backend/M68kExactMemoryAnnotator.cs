/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kHeapOwnerFacts(
	int OwnerValueId,
	bool IsArray,
	bool IsPromotable,
	bool HasFinalizer,
	bool ConstructorMayWrite,
	int? ConstantLength,
	int ElementSize,
	string StorageIdentity);

internal sealed record M68kExactMemoryAnnotation(
	IReadOnlyDictionary<int, M68kHeapOwnerFacts> HeapOwners,
	IReadOnlyDictionary<int, int> CanonicalOwners,
	bool Changed);

/// <summary>
/// Adds precise memory identities to lowered instructions. This intentionally
/// runs after scalar inlining: copied references and same-owner phis have then
/// settled, so one canonical allocation value can name every exact heap access.
/// </summary>
internal static class M68kExactMemoryAnnotator
{
	private sealed record AllocationCandidate(
		int Owner,
		M68kMachineInstruction Instruction,
		bool IsArray,
		CilTypeLayout? Layout,
		CilType? ArrayElementType,
		int? ConstantLength,
		int ElementSize,
		string Identity,
		bool HasFinalizer)
	{
		public bool Escapes { get; set; }

		public bool HasInvalidArrayAccess { get; set; }

		public bool ConstructorMayWrite { get; set; }
	}

	public static M68kExactMemoryAnnotation Annotate(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>?
			methodSummaries = null)
	{
		ArgumentNullException.ThrowIfNull(function);
		ArgumentNullException.ThrowIfNull(method);
		ArgumentNullException.ThrowIfNull(module);

		var definitions = BuildDefinitions(function);
		var canonicalOwners = BuildCanonicalOwners(
			function,
			methodSummaries);
		var allocations = DiscoverAllocations(
			function,
			method,
			module,
			canonicalOwners,
			definitions);
		DiscoverEscapesAndInvalidArrays(
			function,
			method,
			module,
			canonicalOwners,
			definitions,
			allocations,
			methodSummaries);
		var changed = DecomposeAggregateHeapAccesses(
			function,
			method,
			module,
			canonicalOwners,
			allocations);
		if (changed)
		{
			definitions = BuildDefinitions(function);
			canonicalOwners = BuildCanonicalOwners(
				function,
				methodSummaries);
			allocations = DiscoverAllocations(
				function,
				method,
				module,
				canonicalOwners,
				definitions);
			DiscoverEscapesAndInvalidArrays(
				function,
				method,
				module,
				canonicalOwners,
				definitions,
				allocations,
				methodSummaries);
		}

		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (TryFoldArrayLength(
						instruction,
						canonicalOwners,
						allocations,
						out var foldedLength))
				{
					block.Instructions[index] = foldedLength;
					changed = true;
					continue;
				}
				var accesses = ExactAccessesFor(
					function,
					method,
					module,
					instruction,
					canonicalOwners,
					definitions,
					allocations);
				if (!accesses.SequenceEqual(instruction.ExactMemoryAccesses))
				{
					block.Instructions[index] = instruction with
					{
						ExactMemoryAccesses = accesses
					};
					changed = true;
				}
			}
		}

		return new M68kExactMemoryAnnotation(
			allocations.Values.ToDictionary(
				static candidate => candidate.Owner,
				static candidate => new M68kHeapOwnerFacts(
					candidate.Owner,
					candidate.IsArray,
					!candidate.Escapes &&
					!candidate.HasInvalidArrayAccess &&
					!candidate.HasFinalizer,
					candidate.HasFinalizer,
					candidate.ConstructorMayWrite,
					candidate.ConstantLength,
					candidate.ElementSize,
					candidate.Identity)),
			canonicalOwners,
			changed);
	}

	public static bool AnnotateFrameAndArgumentAccesses(
		M68kMachineFunction function)
	{
		ArgumentNullException.ThrowIfNull(function);
		var changed = false;
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (!TryGetFrameAccess(function, instruction, out var access))
				{
					continue;
				}
				ImmutableArray<M68kExactMemoryAccess> accesses = [access];
				if (accesses.SequenceEqual(instruction.ExactMemoryAccesses))
				{
					continue;
				}
				block.Instructions[index] = instruction with
				{
					ExactMemoryAccesses = accesses
				};
				changed = true;
			}
		}
		return changed;
	}

	/// <summary>
	/// Turns supported reference-free aggregate field and array copies into
	/// independent longword lanes. The compiler-owned aggregate homes remain as
	/// snapshots for the existing value-type representation, while heap traffic
	/// becomes ordinary scalar memory SSA that can be promoted independently.
	/// </summary>
	private static bool DecomposeAggregateHeapAccesses(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		IReadOnlyDictionary<int, int> canonicalOwners,
		IReadOnlyDictionary<int, AllocationCandidate> allocations)
	{
		var changed = false;
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (instruction.Uses.Length == 0 ||
					!canonicalOwners.TryGetValue(instruction.Uses[0], out var owner) ||
					!allocations.TryGetValue(owner, out var allocation) ||
					allocation.Escapes || allocation.HasInvalidArrayAccess ||
					allocation.HasFinalizer)
				{
					continue;
				}
				var sourceMethod = instruction.Origin?.SourceMethod ?? method;
				var source = instruction.SourceInstruction;
				if (source is null)
				{
					continue;
				}

				CilTypeLayout? layout = null;
				var isFieldLoad = instruction.Operation ==
						M68kMachineOperation.AggregateFieldLoad &&
					source.OpCode == OpCodes.Ldfld;
				var isFieldStore = instruction.Operation ==
						M68kMachineOperation.Store &&
					source.OpCode == OpCodes.Stfld;
				if (isFieldLoad || isFieldStore)
				{
					var field = module.ResolveFieldToken(
						(int)source.Operand!,
						sourceMethod,
						source.Offset);
					if (!field.IsStatic &&
						module.TryGetReferenceFreeStructLayout(
							field.Type,
							field.ModuleName,
							out var fieldLayout))
					{
						layout = fieldLayout;
					}
				}
				var isArrayLoad = instruction.Operation ==
					M68kMachineOperation.AggregateArrayLoad;
				var isArrayStore = instruction.Operation ==
					M68kMachineOperation.AggregateArrayStore;
				if (isArrayLoad || isArrayStore)
				{
					var elementType = module.ResolveTypeToken(
						(int)source.Operand!,
						sourceMethod,
						source.Offset);
					if (module.TryGetReferenceFreeStructLayout(
							elementType,
							sourceMethod.ModuleName,
							out var elementLayout))
					{
						layout = elementLayout;
					}
				}

				if (layout is null || layout.Size <= 4 ||
					(layout.Size & 3) != 0 || layout.ReferenceBitmap != 0)
				{
					continue;
				}
				if ((isFieldLoad || isArrayLoad) &&
					instruction.ArgumentIndex is null)
				{
					continue;
				}
				if (isFieldLoad && instruction.Uses.Length != 1 ||
					isFieldStore && instruction.Uses.Length != 2 ||
					isArrayLoad && instruction.Uses.Length != 2 ||
					isArrayStore && instruction.Uses.Length != 3)
				{
					continue;
				}

				var replacement = new List<M68kMachineInstruction>();
				for (var laneOffset = 0;
					laneOffset < layout.Size;
					laneOffset += sizeof(uint))
				{
					var lane = function.CreateValue(
						CilStackValueKind.Int32,
						M68kMachineValueWidth.Long,
						isArrayStore
							? M68kRegisterSet.Data.Remove(M68kRegister.D1)
							: M68kRegisterSet.Data);
					if (isFieldStore || isArrayStore)
					{
						var aggregateAddress = instruction.Uses[^1];
						replacement.Add(function.CreateInstruction(
							M68kMachineOperation.Load,
							instruction.IlOffset,
							uses: [aggregateAddress],
							definitions: [lane.Id],
							// AggregateAddress values always name compiler-owned
							// snapshots. This read cannot trap or be externally
							// observed, so an unused superseded lane may die.
							memoryEffect: M68kMachineMemoryEffect.None,
							sourceInstruction: new CilInstruction(
								instruction.IlOffset,
								OpCodes.Ldind_I4,
								null,
								instruction.IlOffset),
							origin: instruction.Origin,
							memoryOffset: laneOffset,
							memorySize: sizeof(uint)));
					}

					if (isFieldLoad)
					{
						replacement.Add(function.CreateInstruction(
							M68kMachineOperation.Load,
							instruction.IlOffset,
							uses: instruction.Uses,
							definitions: [lane.Id],
							memoryEffect: M68kMachineMemoryEffect.Read,
							mayThrow: instruction.MayThrow,
							sourceInstruction: source,
							origin: instruction.Origin,
							memoryOffset: laneOffset,
							memorySize: sizeof(uint)));
					}
					else if (isFieldStore)
					{
						replacement.Add(function.CreateInstruction(
							M68kMachineOperation.Store,
							instruction.IlOffset,
							uses: [instruction.Uses[0], lane.Id],
							memoryEffect: M68kMachineMemoryEffect.Write,
							mayThrow: instruction.MayThrow,
							sourceInstruction: source,
							origin: instruction.Origin,
							memoryOffset: laneOffset,
							memorySize: sizeof(uint)));
					}
					else if (isArrayLoad)
					{
						replacement.Add(function.CreateInstruction(
							M68kMachineOperation.ArrayLoad,
							instruction.IlOffset,
							uses: instruction.Uses,
							definitions: [lane.Id],
							clobbers: M68kRegisterSet.From(M68kRegister.D1),
							memoryEffect: M68kMachineMemoryEffect.Read,
							mayThrow: instruction.MayThrow,
							sourceInstruction: source,
							origin: instruction.Origin,
							memoryOffset: laneOffset,
							memorySize: sizeof(uint)));
					}
					else
					{
						replacement.Add(function.CreateInstruction(
							M68kMachineOperation.ArrayStore,
							instruction.IlOffset,
							uses:
							[
								instruction.Uses[0],
								instruction.Uses[1],
								lane.Id
							],
							clobbers: M68kRegisterSet.From(M68kRegister.D1),
							memoryEffect: M68kMachineMemoryEffect.Write,
							mayThrow: instruction.MayThrow,
							sourceInstruction: source,
							origin: instruction.Origin,
							memoryOffset: laneOffset,
							memorySize: sizeof(uint)));
					}

					if (isFieldLoad || isArrayLoad)
					{
						replacement.Add(function.CreateInstruction(
							M68kMachineOperation.LocalStore,
							instruction.IlOffset,
							uses: [lane.Id],
							memoryEffect: M68kMachineMemoryEffect.Write,
							argumentIndex: instruction.ArgumentIndex,
							sourceInstruction: source,
							origin: instruction.Origin,
							memoryOffset: laneOffset,
							memorySize: sizeof(uint)));
					}
				}

				if ((isFieldLoad || isArrayLoad) &&
					instruction.Definitions is [var aggregateAddressDefinition])
				{
					replacement.Add(function.CreateInstruction(
						M68kMachineOperation.LocalAddress,
						instruction.IlOffset,
						definitions: [aggregateAddressDefinition],
						argumentIndex: instruction.ArgumentIndex,
						sourceInstruction: source,
						origin: instruction.Origin));
				}

				block.Instructions.RemoveAt(index);
				block.Instructions.InsertRange(index, replacement);
				index += replacement.Count - 1;
				changed = true;
			}
		}
		return changed;
	}

	private static Dictionary<int, M68kMachineInstruction> BuildDefinitions(
		M68kMachineFunction function) =>
		function.Blocks
			.SelectMany(static block => block.Instructions)
			.SelectMany(static instruction => instruction.Definitions.Select(
				definition => (definition, instruction)))
			.GroupBy(static item => item.definition)
			.ToDictionary(
				static group => group.Key,
				static group => group.Single().instruction);

	private static IReadOnlyDictionary<int, int> BuildCanonicalOwners(
		M68kMachineFunction function,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>?
			methodSummaries)
	{
		var result = new Dictionary<int, int>(
			M68kByrefProvenanceAnalyzer.BuildCanonicalGcOwners(function));
		if (methodSummaries is null)
		{
			return result;
		}

		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var instruction in function.Blocks
				.SelectMany(static block => block.Instructions))
			{
				if (instruction.Operation == M68kMachineOperation.Copy &&
					instruction.Uses is [var source] &&
					instruction.Definitions is [var definition] &&
					result.TryGetValue(source, out var owner) &&
					AssignCanonicalOwner(result, definition, owner))
				{
					changed = true;
				}
				if (instruction.Operation == M68kMachineOperation.Call &&
					TryGetReturnedOwner(
						instruction,
						result,
						methodSummaries,
						out var returnedOwner))
				{
					// Logical call results and their ABI-fixed definitions initially form
					// a provisional GC-owner class. Rebind the logical result first so
					// the physical result and following transport copies can resolve the
					// class to the returned parameter's allocation identity.
					foreach (var resultValue in
						(instruction.LogicalCall?.ResultValueIds ?? [])
						.Concat(instruction.Definitions))
					{
						changed |= AssignCanonicalOwner(
							result,
							resultValue,
							returnedOwner);
					}
				}
			}
			foreach (var phi in function.Blocks.SelectMany(static block => block.Phis))
			{
				var inputs = phi.Inputs.Values
					.Where(value => value != phi.Definition)
					.ToArray();
				var owners = inputs
					.Where(result.ContainsKey)
					.Select(value => result[value])
					.Distinct()
					.ToArray();
				if (owners is [var owner] &&
					inputs.Length != 0 &&
					inputs.All(result.ContainsKey) &&
					AssignCanonicalOwner(result, phi.Definition, owner))
				{
					changed = true;
				}
			}
		}
		return result;
	}

	private static bool AssignCanonicalOwner(
		IDictionary<int, int> owners,
		int value,
		int owner)
	{
		owner = ResolveCanonicalOwner(owners, owner);
		if (owners.TryGetValue(value, out var existing))
		{
			var resolvedExisting = ResolveCanonicalOwner(owners, existing);
			if (resolvedExisting == owner)
			{
				if (existing == owner)
				{
					return false;
				}
				owners[value] = owner;
				return true;
			}
			// BuildCanonicalGcOwners initially gives unresolved call/phi
			// definitions their own identity. Replace only that provisional
			// identity; never merge two already distinct allocations.
			if (existing != value)
			{
				return false;
			}
		}
		owners[value] = owner;
		return true;
	}

	private static int ResolveCanonicalOwner(
		IDictionary<int, int> owners,
		int value)
	{
		var visited = new HashSet<int>();
		while (visited.Add(value) &&
			owners.TryGetValue(value, out var owner) &&
			owner != value)
		{
			value = owner;
		}
		return value;
	}

	private static bool TryGetReturnedOwner(
		M68kMachineInstruction call,
		IReadOnlyDictionary<int, int> canonicalOwners,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>
			methodSummaries,
		out int owner)
	{
		owner = default;
		if (call.LogicalCall is not
			{
				ResolvedTargets.Length: > 0,
				ArgumentValueIds: var arguments
			} logicalCall)
		{
			return false;
		}
		int? commonOwner = null;
		foreach (var target in logicalCall.ResolvedTargets)
		{
			if (!methodSummaries.TryGetValue(target, out var summary))
			{
				return false;
			}
			var returnedOwners = Enumerable.Range(0, arguments.Length)
				.Where(index => (summary.EffectForParameter(index) &
					M68kParameterMemoryEffect.ReturnedAlias) != 0)
				.Where(index => canonicalOwners.ContainsKey(arguments[index]))
				.Select(index => canonicalOwners[arguments[index]])
				.Distinct()
				.ToArray();
			if (returnedOwners is not [var targetOwner] ||
				commonOwner is { } knownOwner && knownOwner != targetOwner)
			{
				return false;
			}
			commonOwner = targetOwner;
		}
		if (commonOwner is not { } resolvedOwner)
		{
			return false;
		}
		owner = resolvedOwner;
		return true;
	}

	private static Dictionary<int, AllocationCandidate> DiscoverAllocations(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		IReadOnlyDictionary<int, int> canonicalOwners,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions)
	{
		var result = new Dictionary<int, AllocationCandidate>();
		foreach (var instruction in function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction => instruction.Operation is
				M68kMachineOperation.ObjectAllocate or
				M68kMachineOperation.ArrayAllocate))
		{
			if (instruction.Definitions is not [var definition] ||
				!canonicalOwners.TryGetValue(definition, out var owner))
			{
				continue;
			}
			var sourceMethod = instruction.Origin?.SourceMethod ?? method;
			var source = instruction.Origin?.SourceInstruction ??
				instruction.SourceInstruction;
			if (instruction.Operation == M68kMachineOperation.ArrayAllocate)
			{
				if (source is not { Operand: int typeToken } ||
					source.OpCode != OpCodes.Newarr)
				{
					continue;
				}
				var elementType = module.ResolveTypeToken(
					typeToken,
					sourceMethod,
					source.Offset);
				var elementSize = TryGetElementSize(
					module,
					elementType,
					sourceMethod.ModuleName,
					out var size)
						? size
						: 0;
				int? length = null;
				if (instruction.Uses is [var lengthValue] &&
					TryGetIntegralConstant(
						lengthValue,
						definitions,
						out var constantLength) &&
					constantLength is >= 0 and <= int.MaxValue)
				{
					length = (int)constantLength;
				}
				result[owner] = new AllocationCandidate(
					owner,
					instruction,
					IsArray: true,
					Layout: null,
					ArrayElementType: elementType,
					length,
					elementSize,
					$"array:{elementType.DisplayName}",
					HasFinalizer: false);
				continue;
			}

			if (source is not { Operand: int constructorToken })
			{
				continue;
			}
			var constructor = module.ResolveMethodToken(
				constructorToken,
				sourceMethod,
				source.Offset).Definition;
			if (constructor is null)
			{
				continue;
			}
			var layout = module.GetTypeLayout(constructor);
			result[owner] = new AllocationCandidate(
				owner,
				instruction,
				IsArray: false,
				layout,
				ArrayElementType: null,
				ConstantLength: null,
				ElementSize: 0,
				$"object:{layout.ModuleName}:{layout.Handle}:" +
					(layout.ConstructedType?.DisplayName ?? string.Empty),
				module.TryGetEffectiveFinalizer(layout) is not null);
		}
		return result;
	}

	private static void DiscoverEscapesAndInvalidArrays(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		IReadOnlyDictionary<int, int> canonicalOwners,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions,
		IReadOnlyDictionary<int, AllocationCandidate> allocations,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>?
			methodSummaries)
	{
		foreach (var phi in function.Blocks.SelectMany(static block => block.Phis))
		{
			var inputs = phi.Inputs.Values.Distinct().ToArray();
			var allocationOwners = inputs
				.Where(canonicalOwners.ContainsKey)
				.Select(value => canonicalOwners[value])
				.Where(allocations.ContainsKey)
				.Distinct()
				.ToArray();
			if (allocationOwners.Length == 0)
			{
				continue;
			}
			var preservesOneOwner = allocationOwners is [var exactOwner] &&
				inputs.All(value =>
					canonicalOwners.TryGetValue(value, out var inputOwner) &&
					inputOwner == exactOwner);
			if (preservesOneOwner)
			{
				continue;
			}
			foreach (var owner in allocationOwners)
			{
				allocations[owner].Escapes = true;
			}
		}
		foreach (var block in function.Blocks)
		{
			foreach (var instruction in block.Instructions)
			{
				for (var useIndex = 0; useIndex < instruction.Uses.Length; useIndex++)
				{
					var use = instruction.Uses[useIndex];
					if (!canonicalOwners.TryGetValue(use, out var owner) ||
						!allocations.TryGetValue(owner, out var allocation))
					{
						continue;
					}
					if (IsTransparentOwnerUse(
							instruction,
							useIndex,
							allocation,
							method,
							module,
							canonicalOwners,
							methodSummaries))
					{
						continue;
					}
					allocation.Escapes = true;
				}

				if (instruction.Operation == M68kMachineOperation.Call &&
					instruction.LogicalCall is { ArgumentValueIds: [var receiver, ..] } &&
					canonicalOwners.TryGetValue(receiver, out var callOwner) &&
					allocations.TryGetValue(callOwner, out var constructed) &&
					IsAllocationConstructorCall(instruction, constructed))
				{
					constructed.ConstructorMayWrite = true;
				}

				if (instruction.Operation is not
					M68kMachineOperation.ArrayLoad and not
					M68kMachineOperation.ArrayStore and not
					M68kMachineOperation.ArrayAddress and not
					M68kMachineOperation.AggregateArrayLoad and not
					M68kMachineOperation.AggregateArrayStore ||
					instruction.Uses.Length == 0 ||
					!canonicalOwners.TryGetValue(instruction.Uses[0], out var arrayOwner) ||
					!allocations.TryGetValue(arrayOwner, out var array) ||
					!array.IsArray)
				{
					continue;
				}
				if (SourceInstructionFor(instruction)?.OpCode == OpCodes.Ldlen)
				{
					continue;
				}
				var isAggregateArrayAccess = instruction.Operation is
					M68kMachineOperation.AggregateArrayLoad or
					M68kMachineOperation.AggregateArrayStore;
				var unsupportedAggregateAccess = isAggregateArrayAccess &&
					!IsSupportedAggregateArrayAccess(instruction, method, module);
				if (instruction.Operation == M68kMachineOperation.ArrayAddress ||
					unsupportedAggregateAccess ||
					array.ConstantLength is null ||
					array.ElementSize <= 0 ||
					instruction.Uses.Length < 2 ||
					!TryGetIntegralConstant(
						instruction.Uses[1],
						definitions,
						out var elementIndex) ||
					elementIndex < 0 ||
					elementIndex >= array.ConstantLength ||
					SourceInstructionFor(instruction)?.OpCode == OpCodes.Stelem_Ref &&
					!IsStaticallySafeReferenceStore(
						function,
						instruction,
						array,
						canonicalOwners,
						allocations,
						definitions))
				{
					array.HasInvalidArrayAccess = true;
				}
			}
		}
	}

	private static bool IsSupportedAggregateArrayAccess(
		M68kMachineInstruction instruction,
		CilMethod method,
		CompilationModule module)
	{
		var isLoad = instruction.Operation ==
			M68kMachineOperation.AggregateArrayLoad;
		var isStore = instruction.Operation ==
			M68kMachineOperation.AggregateArrayStore;
		if (!isLoad && !isStore ||
			isLoad && (instruction.Uses.Length != 2 ||
				instruction.ArgumentIndex is null) ||
			isStore && instruction.Uses.Length != 3)
		{
			return false;
		}

		var sourceMethod = instruction.Origin?.SourceMethod ?? method;
		var source = instruction.SourceInstruction;
		if (source?.Operand is not int typeToken)
		{
			return false;
		}
		var elementType = module.ResolveTypeToken(
			typeToken,
			sourceMethod,
			source.Offset);
		return module.TryGetReferenceFreeStructLayout(
				elementType,
				sourceMethod.ModuleName,
				out var layout) &&
			layout.Size > 4 &&
			(layout.Size & 3) == 0 &&
			layout.ReferenceBitmap == 0;
	}

	private static bool TryFoldArrayLength(
		M68kMachineInstruction instruction,
		IReadOnlyDictionary<int, int> canonicalOwners,
		IReadOnlyDictionary<int, AllocationCandidate> allocations,
		out M68kMachineInstruction replacement)
	{
		if (instruction.Operation != M68kMachineOperation.ArrayLoad ||
			SourceInstructionFor(instruction)?.OpCode != OpCodes.Ldlen ||
			instruction.Uses is not [var arrayValue] ||
			instruction.Definitions.Length != 1 ||
			!canonicalOwners.TryGetValue(arrayValue, out var owner) ||
			!allocations.TryGetValue(owner, out var array) ||
			array.ConstantLength is not { } length)
		{
			replacement = instruction;
			return false;
		}
		replacement = instruction with
		{
			Operation = M68kMachineOperation.Constant,
			Uses = [],
			Clobbers = M68kRegisterSet.None,
			MemoryEffect = M68kMachineMemoryEffect.None,
			IsSafepoint = false,
			MayThrow = false,
			ConstantValue = M68kMachineConstant.Int32(length),
			ExactMemoryAccesses = []
		};
		return true;
	}

	private static bool IsTransparentOwnerUse(
		M68kMachineInstruction instruction,
		int useIndex,
		AllocationCandidate allocation,
		CilMethod method,
		CompilationModule module,
		IReadOnlyDictionary<int, int> canonicalOwners,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>?
			methodSummaries)
	{
		_ = method;
		_ = module;
		if (instruction.Operation is
			M68kMachineOperation.Copy or
			M68kMachineOperation.ByrefOwnerKeepAlive or
			M68kMachineOperation.GcKeepAlive)
		{
			return true;
		}
		if (useIndex == 0 && SourceInstructionFor(instruction) is { } source &&
			(source.OpCode == OpCodes.Ldfld || source.OpCode == OpCodes.Stfld))
		{
			return !allocation.IsArray;
		}
		if (useIndex == 0 && instruction.Operation is
			M68kMachineOperation.ArrayLoad or
			M68kMachineOperation.ArrayStore or
			M68kMachineOperation.ArrayAddress or
			M68kMachineOperation.AggregateArrayLoad or
			M68kMachineOperation.AggregateArrayStore)
		{
			return allocation.IsArray;
		}
		if (instruction.Operation != M68kMachineOperation.Call)
		{
			return false;
		}
		if (useIndex == 0 && IsAllocationConstructorCall(instruction, allocation))
		{
			return true;
		}
		return CallIsTransparentForOwner(
			instruction,
			allocation.Owner,
			canonicalOwners,
			methodSummaries);
	}

	private static bool CallIsTransparentForOwner(
		M68kMachineInstruction call,
		int owner,
		IReadOnlyDictionary<int, int> canonicalOwners,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>?
			methodSummaries)
	{
		if (methodSummaries is null ||
			call.LogicalCall is not
			{
				DispatchKind: M68kMachineCallDispatchKind.Direct,
				RequiresNullCheck: false,
				ResolvedTargets.Length: > 0,
				ArgumentValueIds: var arguments
			} logicalCall)
		{
			return false;
		}
		var matched = arguments.Any(argument =>
			canonicalOwners.TryGetValue(argument, out var argumentOwner) &&
			argumentOwner == owner);
		if (!matched)
		{
			return false;
		}
		foreach (var target in logicalCall.ResolvedTargets)
		{
			if (!methodSummaries.TryGetValue(target, out var summary))
			{
				return false;
			}
			for (var index = 0; index < arguments.Length; index++)
			{
				if (!canonicalOwners.TryGetValue(arguments[index], out var argumentOwner) ||
					argumentOwner != owner)
				{
					continue;
				}
				if ((summary.EffectForParameter(index) &
					(M68kParameterMemoryEffect.Read |
					 M68kParameterMemoryEffect.Write |
					 M68kParameterMemoryEffect.Capture)) != 0)
				{
					return false;
				}
			}
		}
		return true;
	}

	private static bool IsAllocationConstructorCall(
		M68kMachineInstruction instruction,
		AllocationCandidate allocation)
	{
		if (allocation.IsArray ||
			instruction.Operation != M68kMachineOperation.Call ||
			SourceInstructionFor(instruction) is not { } call ||
			SourceInstructionFor(allocation.Instruction) is not { } allocationSource ||
			call.OpCode != OpCodes.Call)
		{
			return false;
		}
		return Equals(call.Operand, allocationSource.Operand) &&
			call.Offset == allocationSource.Offset;
	}

	private static CilInstruction? SourceInstructionFor(
		M68kMachineInstruction instruction) =>
		instruction.Origin?.SourceInstruction ?? instruction.SourceInstruction;

	private static ImmutableArray<M68kExactMemoryAccess> ExactAccessesFor(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		M68kMachineInstruction instruction,
		IReadOnlyDictionary<int, int> canonicalOwners,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions,
		IReadOnlyDictionary<int, AllocationCandidate> allocations)
	{
		if (instruction.Operation is
			M68kMachineOperation.PlatformBaseLoad or
			M68kMachineOperation.PlatformBaseStore &&
			!instruction.ExactMemoryAccesses.IsDefaultOrEmpty)
		{
			return instruction.ExactMemoryAccesses;
		}

		if (TryGetFrameAccess(function, instruction, out var frameAccess))
		{
			return [frameAccess];
		}

		var sourceMethod = instruction.Origin?.SourceMethod ?? method;
		var source = instruction.Origin?.SourceInstruction ??
			instruction.SourceInstruction;
		if (source is null)
		{
			return ImmutableArray<M68kExactMemoryAccess>.Empty;
		}
		if (source.OpCode is var fieldOp &&
			(fieldOp == OpCodes.Ldsfld || fieldOp == OpCodes.Ldsflda ||
			 fieldOp == OpCodes.Stsfld) &&
			IsConcreteFieldOperation(instruction.Operation, fieldOp) &&
			source.Operand is int staticToken)
		{
			var field = module.ResolveFieldToken(
				staticToken,
				sourceMethod,
				source.Offset);
			var memoryObject = M68kMemoryModel.StaticFieldObject(field);
			return [new M68kExactMemoryAccess(
				memoryObject,
				fieldOp == OpCodes.Ldsflda
					? M68kExactMemoryAccessKind.Address
					: fieldOp == OpCodes.Stsfld
						? M68kExactMemoryAccessKind.Write
						: M68kExactMemoryAccessKind.Read,
				fieldOp == OpCodes.Stsfld
					? instruction.Uses.LastOrDefault()
					: instruction.Definitions.SingleOrDefault())];
		}

		if (source.OpCode is var instanceOp &&
			(instanceOp == OpCodes.Ldfld || instanceOp == OpCodes.Ldflda ||
			 instanceOp == OpCodes.Stfld) &&
			IsConcreteFieldOperation(instruction.Operation, instanceOp) &&
			source.Operand is int instanceToken &&
			instruction.Uses.Length != 0 &&
			canonicalOwners.TryGetValue(instruction.Uses[0], out var fieldOwner) &&
			allocations.TryGetValue(fieldOwner, out var objectAllocation) &&
			!objectAllocation.IsArray &&
			!objectAllocation.Escapes &&
			!objectAllocation.HasFinalizer &&
			objectAllocation.Layout is { } layout)
		{
			var field = module.ResolveFieldToken(
				instanceToken,
				sourceMethod,
				source.Offset);
			var isAggregateField = module.TryGetReferenceFreeStructLayout(
					field.Type,
					field.ModuleName,
					out var aggregateLayout) &&
				aggregateLayout.Size > 4;
			var isAggregateLane = isAggregateField &&
				instruction.MemorySize == sizeof(uint);
			if (isAggregateField && !isAggregateLane)
			{
				return ImmutableArray<M68kExactMemoryAccess>.Empty;
			}
			if (isAggregateLane &&
				(instruction.MemoryOffset < 0 ||
				 instruction.MemoryOffset > aggregateLayout!.Size - sizeof(uint) ||
				 (instruction.MemoryOffset & 3) != 0))
			{
				return ImmutableArray<M68kExactMemoryAccess>.Empty;
			}
			if (!layout.FieldOffsets.TryGetValue(field.Handle, out var fieldOffset))
			{
				return ImmutableArray<M68kExactMemoryAccess>.Empty;
			}
			var size = isAggregateLane
				? sizeof(uint)
				: TryGetElementSize(
					module,
					field.Type,
					field.ModuleName,
					out var fieldSize)
						? fieldSize
						: 0;
			var memoryObject = new M68kMemoryObject(
				isAggregateLane
					? M68kMemoryObjectKind.AggregateLane
					: M68kMemoryObjectKind.ObjectField,
				objectAllocation.Identity,
				field.Type.IsReference,
				fieldOwner,
				checked(fieldOffset + instruction.MemoryOffset),
				size);
			return [new M68kExactMemoryAccess(
				memoryObject,
				instanceOp == OpCodes.Ldflda
					? M68kExactMemoryAccessKind.Address
					: instanceOp == OpCodes.Stfld
						? M68kExactMemoryAccessKind.Write
						: M68kExactMemoryAccessKind.Read,
				instanceOp == OpCodes.Stfld
					? instruction.Uses[^1]
					: instruction.Definitions.SingleOrDefault())];
		}

		if (instruction.Operation is
			M68kMachineOperation.ArrayLoad or
			M68kMachineOperation.ArrayStore &&
			instruction.Uses.Length != 0 &&
			canonicalOwners.TryGetValue(instruction.Uses[0], out var arrayOwner) &&
			allocations.TryGetValue(arrayOwner, out var array) &&
			array.IsArray &&
			!array.Escapes &&
			!array.HasInvalidArrayAccess &&
			array.ConstantLength is not null &&
			instruction.Uses.Length >= 2 &&
			TryGetIntegralConstant(
				instruction.Uses[1],
				definitions,
				out var index) &&
			index is >= 0 and <= int.MaxValue)
		{
			var valueId = instruction.Operation == M68kMachineOperation.ArrayStore
				? instruction.Uses[^1]
				: instruction.Definitions.SingleOrDefault();
			var isRoot = function.Values.TryGetValue(valueId, out var value) &&
				value.IsGcReference;
			var isAggregateLane = instruction.MemorySize == sizeof(uint) &&
				source.OpCode is var aggregateArrayOp &&
				(aggregateArrayOp == OpCodes.Ldelem ||
				 aggregateArrayOp == OpCodes.Stelem) &&
				source.Operand is int aggregateElementToken &&
				module.TryGetReferenceFreeStructLayout(
					module.ResolveTypeToken(
						aggregateElementToken,
						sourceMethod,
						source.Offset),
					sourceMethod.ModuleName,
					out var aggregateElementLayout) &&
				aggregateElementLayout.Size > 4;
			if (isAggregateLane &&
				(instruction.MemoryOffset < 0 ||
				 instruction.MemoryOffset > array.ElementSize - sizeof(uint) ||
				 (instruction.MemoryOffset & 3) != 0))
			{
				return ImmutableArray<M68kExactMemoryAccess>.Empty;
			}
			var accessSize = isAggregateLane
				? sizeof(uint)
				: array.ElementSize;
			var memoryObject = new M68kMemoryObject(
				isAggregateLane
					? M68kMemoryObjectKind.AggregateLane
					: M68kMemoryObjectKind.ArrayElement,
				array.Identity,
				isRoot,
				arrayOwner,
				checked((int)index * array.ElementSize + instruction.MemoryOffset),
				accessSize);
			return [new M68kExactMemoryAccess(
				memoryObject,
				instruction.Operation == M68kMachineOperation.ArrayStore
					? M68kExactMemoryAccessKind.Write
					: M68kExactMemoryAccessKind.Read,
				valueId)];
		}

		return ImmutableArray<M68kExactMemoryAccess>.Empty;
	}

	private static bool IsConcreteFieldOperation(
		M68kMachineOperation operation,
		OpCode fieldOp) =>
		fieldOp == OpCodes.Stfld || fieldOp == OpCodes.Stsfld
			? operation == M68kMachineOperation.Store
			: fieldOp == OpCodes.Ldflda || fieldOp == OpCodes.Ldsflda
				? operation == M68kMachineOperation.Address
				: operation == M68kMachineOperation.Load;

	private static bool TryGetFrameAccess(
		M68kMachineFunction function,
		M68kMachineInstruction instruction,
		out M68kExactMemoryAccess access)
	{
		if (instruction.ArgumentIndex is not { } index)
		{
			access = default;
			return false;
		}
		var objectKind = instruction.Operation switch
		{
			M68kMachineOperation.LocalLoad or
				M68kMachineOperation.LocalStore or
				M68kMachineOperation.LocalAddress => M68kMemoryObjectKind.FrameSlot,
			M68kMachineOperation.ArgumentLoad or
				M68kMachineOperation.ArgumentStore or
				M68kMachineOperation.ArgumentAddress => M68kMemoryObjectKind.ArgumentHome,
			_ => (M68kMemoryObjectKind?)null
		};
		if (objectKind is null)
		{
			access = default;
			return false;
		}
		var home = objectKind == M68kMemoryObjectKind.FrameSlot
			? function.LocalHomes.GetValueOrDefault(index)
			: function.ArgumentHomes.GetValueOrDefault(index);
		var accessKind = instruction.Operation switch
		{
			M68kMachineOperation.LocalLoad or
				M68kMachineOperation.ArgumentLoad => M68kExactMemoryAccessKind.Read,
			M68kMachineOperation.LocalStore or
				M68kMachineOperation.ArgumentStore => M68kExactMemoryAccessKind.Write,
			_ => M68kExactMemoryAccessKind.Address
		};
		var memoryOffset = accessKind == M68kExactMemoryAccessKind.Address
			? 0
			: instruction.MemoryOffset;
		var accessSize = accessKind == M68kExactMemoryAccessKind.Address
			? home?.Size ?? 0
			: instruction.MemorySize > 0
				? instruction.MemorySize
				: home?.Size ?? 0;
		access = new M68kExactMemoryAccess(
			M68kMemoryModel.FrameObject(
				objectKind.Value,
				index,
				home,
				memoryOffset,
				accessSize),
			accessKind,
			accessKind == M68kExactMemoryAccessKind.Read
				? instruction.Definitions.SingleOrDefault()
				: accessKind == M68kExactMemoryAccessKind.Write
					? instruction.Uses.SingleOrDefault()
					: null);
		return true;
	}

	private static bool TryGetElementSize(
		CompilationModule module,
		CilType type,
		string moduleName,
		out int size)
	{
		if (type.IsReference || type.Kind is
			CilTypeKind.UnmanagedPointer or
			CilTypeKind.FunctionPointer)
		{
			size = sizeof(uint);
			return true;
		}
		if (type.IsSupportedScalar)
		{
			size = Math.Max(1, type.Size);
			return true;
		}
		if (module.TryGetReferenceFreeStructLayout(
				type,
				moduleName,
				out var layout))
		{
			size = layout.Size;
			return true;
		}
		size = 0;
		return false;
	}

	private static bool IsKnownNullStore(
		M68kMachineInstruction instruction,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions) =>
		instruction.Uses.Length >= 3 &&
		TryGetConstant(instruction.Uses[^1], definitions, out var constant) &&
		constant.Kind == M68kMachineConstantKind.Null;

	private static bool IsStaticallySafeReferenceStore(
		M68kMachineFunction function,
		M68kMachineInstruction instruction,
		AllocationCandidate array,
		IReadOnlyDictionary<int, int> canonicalOwners,
		IReadOnlyDictionary<int, AllocationCandidate> allocations,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions)
	{
		if (IsKnownNullStore(instruction, definitions))
		{
			return true;
		}
		if (instruction.Uses.Length < 3 ||
			array.ArrayElementType is not { } elementType)
		{
			return false;
		}
		var storedValue = instruction.Uses[^1];
		if (!function.Values.TryGetValue(storedValue, out var value) ||
			!value.IsGcReference)
		{
			return false;
		}
		// A freshly allocated object[] has no narrower runtime element type.
		if (elementType.DisplayName == "object")
		{
			return true;
		}
		if (!canonicalOwners.TryGetValue(storedValue, out var storedOwner) ||
			!allocations.TryGetValue(storedOwner, out var storedAllocation))
		{
			return false;
		}
		var exactType = storedAllocation.IsArray
			? storedAllocation.ArrayElementType is { } storedElement
				? $"{storedElement.DisplayName}[]"
				: string.Empty
			: storedAllocation.Layout?.ConstructedType?.DisplayName ??
				storedAllocation.Layout?.DisplayName ?? string.Empty;
		return string.Equals(
			elementType.DisplayName,
			exactType,
			StringComparison.Ordinal);
	}

	private static bool TryGetIntegralConstant(
		int value,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions,
		out long integral)
	{
		if (TryGetConstant(value, definitions, out var constant) &&
			constant.TryGetIntegral(out integral))
		{
			return true;
		}
		integral = 0;
		return false;
	}

	private static bool TryGetConstant(
		int value,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions,
		out M68kMachineConstant constant)
	{
		var visited = new HashSet<int>();
		while (visited.Add(value) &&
			definitions.TryGetValue(value, out var definition))
		{
			if (definition.Operation == M68kMachineOperation.Constant &&
				definition.ConstantValue is { } known)
			{
				constant = known;
				return true;
			}
			if (definition.Operation is
				M68kMachineOperation.Copy or
				M68kMachineOperation.Convert &&
				definition.Uses is [var source])
			{
				value = source;
				continue;
			}
			break;
		}
		constant = default;
		return false;
	}
}
