/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal static class CilManagedByrefSummary
{
	public static bool TryGetBorrowedParameterReturn(
		CilMethod method,
		out int argumentIndex)
	{
		argumentIndex = default;
		if (method.Signature.Header.IsInstance ||
			method.Signature.ReturnType.Kind != CilTypeKind.ManagedPointer)
		{
			return false;
		}

		var instructions = method.Instructions
			.Where(static instruction => instruction.OpCode != OpCodes.Nop)
			.ToArray();
		if (instructions.Length != 2 ||
			instructions[1].OpCode != OpCodes.Ret ||
			!TryGetLoadArgumentIndex(instructions[0], out argumentIndex) ||
			argumentIndex < 0 ||
			argumentIndex >= method.Signature.ParameterTypes.Length ||
			method.Signature.ParameterTypes[argumentIndex].Kind !=
				CilTypeKind.ManagedPointer)
		{
			argumentIndex = default;
			return false;
		}
		return true;
	}

	private static bool TryGetLoadArgumentIndex(
		CilInstruction instruction,
		out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldarg_0.Value &&
			op.Value <= OpCodes.Ldarg_3.Value)
		{
			index = op.Value - OpCodes.Ldarg_0.Value;
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

internal enum M68kByrefProvenanceKind
{
	Unknown,
	Frame,
	Static,
	Ownerless,
	CallerBorrowed,
	ObjectInterior,
	ArrayInterior,
	BoxInterior
}

internal readonly record struct M68kByrefProvenance(
	M68kByrefProvenanceKind Kind,
	int? OwnerValue = null)
{
	public bool IsSafeWithoutOwnerRoot => Kind is
		M68kByrefProvenanceKind.Frame or
		M68kByrefProvenanceKind.Static or
		M68kByrefProvenanceKind.Ownerless or
		M68kByrefProvenanceKind.CallerBorrowed;
}

internal static class M68kManagedByrefEscapeValidator
{
	public static void Validate(
		M68kMachineFunction function,
		bool allowUntrackedManagedByrefs)
	{
		if (allowUntrackedManagedByrefs)
		{
			return;
		}

		foreach (var instruction in function.Blocks
			.SelectMany(static block => block.Instructions))
		{
			if (instruction.SourceInstruction is not { } source ||
				(instruction.MemoryEffect & M68kMachineMemoryEffect.Write) == 0 ||
				!StoresValueOutsideManagedByrefSsa(source.OpCode) ||
				instruction.Uses.Length == 0)
			{
				continue;
			}

			var storedValue = instruction.Uses[^1];
			if (function.Values[storedValue].Kind !=
				CilStackValueKind.ManagedPointer)
			{
				continue;
			}

			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Managed byref v{storedValue} cannot escape through '{source.OpCode.Name}'. Phase 5C does not permit managed byref values in heap, static, array, or indirect storage; keep the byref in SSA/ref locals or pass it to a scoped managed callee.",
				function.DisplayName,
				instruction.IlOffset);
		}
	}

	private static bool StoresValueOutsideManagedByrefSsa(OpCode op) =>
		op == OpCodes.Stfld ||
		op == OpCodes.Stsfld ||
		op == OpCodes.Stelem ||
		op == OpCodes.Stelem_I ||
		op == OpCodes.Stelem_I1 ||
		op == OpCodes.Stelem_I2 ||
		op == OpCodes.Stelem_I4 ||
		op == OpCodes.Stelem_I8 ||
		op == OpCodes.Stelem_R4 ||
		op == OpCodes.Stelem_R8 ||
		op == OpCodes.Stelem_Ref ||
		op == OpCodes.Stind_I ||
		op == OpCodes.Stind_I1 ||
		op == OpCodes.Stind_I2 ||
		op == OpCodes.Stind_I4 ||
		op == OpCodes.Stind_I8 ||
		op == OpCodes.Stind_R4 ||
		op == OpCodes.Stind_R8 ||
		op == OpCodes.Stind_Ref ||
		op == OpCodes.Stobj;
}

internal static class M68kManagedByrefTypeTracker
{
	public static void TrackAndValidate(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module)
	{
		var tracked = function.Values.Values
			.Where(static value =>
				value.Kind == CilStackValueKind.ManagedPointer)
			.ToDictionary(
				static value => value.Id,
				static _ => (M68kManagedByrefType?)null);
		if (tracked.Count == 0)
		{
			return;
		}

		var readonlyTargets = method.Instructions
			.Select((instruction, index) =>
				instruction.OpCode == OpCodes.Readonly &&
				index + 1 < method.Instructions.Count
					? (int?)method.Instructions[index + 1].Offset
					: null)
			.OfType<int>()
			.ToHashSet();
		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var block in function.Blocks)
			{
				foreach (var phi in block.Phis)
				{
					if (!tracked.ContainsKey(phi.Definition) ||
						!TryMerge(
							phi.Inputs.Values,
							tracked,
							function,
							block.StartIlOffset,
							out var merged))
					{
						continue;
					}
					changed |= Assign(tracked, phi.Definition, merged);
				}

				foreach (var instruction in block.Instructions)
				{
					foreach (var definition in instruction.Definitions)
					{
						if (!tracked.ContainsKey(definition) ||
							!TryDescribe(
								instruction,
								method,
								module,
								tracked,
								readonlyTargets,
								out var description))
						{
							continue;
						}
						changed |= Assign(tracked, definition, description);
					}
				}
			}
		}

		function.ManagedByrefTypes.Clear();
		foreach (var (value, description) in tracked)
		{
			if (description is not null)
				function.ManagedByrefTypes.Add(value, description.Value);
		}
		ValidateUses(function, method, module);
	}

	private static bool TryDescribe(
		M68kMachineInstruction instruction,
		CilMethod method,
		CompilationModule module,
		IReadOnlyDictionary<int, M68kManagedByrefType?> tracked,
		IReadOnlySet<int> readonlyTargets,
		out M68kManagedByrefType description)
	{
		if (instruction.Operation == M68kMachineOperation.Copy &&
			instruction.Uses.Length == 1 &&
			tracked.TryGetValue(instruction.Uses[0], out var copied) &&
			copied is not null)
		{
			description = copied.Value;
			return true;
		}
		if (instruction.Operation == M68kMachineOperation.Argument &&
			instruction.ArgumentIndex is { } argumentIndex &&
			TryGetArgumentType(method, argumentIndex, out var argumentType) &&
			argumentType.Kind == CilTypeKind.ManagedPointer &&
			argumentType.ElementType is { } parameterReferent)
		{
			description = new M68kManagedByrefType(
				parameterReferent,
				argumentType.IsReadOnly ||
				parameterReferent.IsReadOnly ||
				IsReadOnlyArgument(method, argumentIndex));
			return true;
		}
		if (instruction.Operation == M68kMachineOperation.LocalAddress &&
			instruction.ArgumentIndex is { } localIndex &&
			instruction.SourceInstruction?.OpCode is var localOp &&
			(localOp == OpCodes.Ldloca || localOp == OpCodes.Ldloca_S) &&
			localIndex >= 0 && localIndex < method.Locals.Length)
		{
			description = new M68kManagedByrefType(method.Locals[localIndex], false);
			return true;
		}
		if (instruction.Operation == M68kMachineOperation.ArgumentAddress &&
			instruction.ArgumentIndex is { } addressedArgument &&
			TryGetArgumentType(method, addressedArgument, out var addressedType))
		{
			description = new M68kManagedByrefType(addressedType, false);
			return true;
		}
		if (instruction.SourceInstruction is { } source &&
			source.OpCode is var fieldOp &&
			(fieldOp == OpCodes.Ldflda || fieldOp == OpCodes.Ldsflda))
		{
			var field = module.ResolveFieldToken(
				(int)source.Operand!, method, source.Offset);
			description = new M68kManagedByrefType(field.Type, false);
			return true;
		}
		if (instruction.Operation == M68kMachineOperation.ArrayAddress &&
			instruction.SourceInstruction is { } arraySource)
		{
			var element = module.ResolveTypeToken(
				(int)arraySource.Operand!, method, arraySource.Offset);
			description = new M68kManagedByrefType(
				element,
				readonlyTargets.Contains(arraySource.Offset));
			return true;
		}
		if (instruction.Operation == M68kMachineOperation.Unbox &&
			instruction.SourceInstruction is { } unboxSource &&
			unboxSource.OpCode == OpCodes.Unbox)
		{
			description = new M68kManagedByrefType(
				module.ResolveTypeToken(
					(int)unboxSource.Operand!, method, unboxSource.Offset),
				false);
			return true;
		}
		if (instruction.Operation == M68kMachineOperation.Call &&
			instruction.SourceInstruction is { } callSource)
		{
			var returnType = module.ResolveMethodToken(
				(int)callSource.Operand!, method, callSource.Offset)
				.Signature.ReturnType;
			if (returnType.Kind == CilTypeKind.ManagedPointer &&
				returnType.ElementType is { } returnReferent)
			{
				description = new M68kManagedByrefType(
					returnReferent,
					returnType.IsReadOnly || returnReferent.IsReadOnly);
				return true;
			}
		}

		description = default;
		return false;
	}

	private static void ValidateUses(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module)
	{
		foreach (var instruction in function.Blocks
			.SelectMany(static block => block.Instructions))
		{
			if (instruction.Uses.Length == 0 ||
				!function.ManagedByrefTypes.TryGetValue(
					instruction.Uses[0], out var destination))
			{
				continue;
			}

			var op = instruction.SourceInstruction?.OpCode;
			if (destination.IsReadOnly && op is { } writeOp &&
				WritesThroughManagedByref(writeOp))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedInstruction,
					$"Write through readonly managed byref v{instruction.Uses[0]} using '{writeOp.Name}' is not permitted.",
					function.DisplayName,
					instruction.IlOffset);
			}

			if (op is not { } typedOp ||
				typedOp != OpCodes.Ldobj &&
				typedOp != OpCodes.Stobj &&
				typedOp != OpCodes.Initobj &&
				typedOp != OpCodes.Cpobj)
			{
				continue;
			}
			var accessedType = module.ResolveTypeToken(
				(int)instruction.SourceInstruction!.Operand!,
				method,
				instruction.IlOffset);
			if (!SameReferentType(destination.ReferentType, accessedType))
			{
				throw Incompatible(
					function,
					instruction.IlOffset,
					destination.ReferentType,
					accessedType,
					$"'{typedOp.Name}' access");
			}
		}
	}

	private static bool TryMerge(
		IEnumerable<int> values,
		IReadOnlyDictionary<int, M68kManagedByrefType?> tracked,
		M68kMachineFunction function,
		int ilOffset,
		out M68kManagedByrefType merged)
	{
		var inputs = new List<M68kManagedByrefType>();
		foreach (var value in values)
		{
			if (!tracked.TryGetValue(value, out var input) || input is null)
			{
				merged = default;
				return false;
			}
			inputs.Add(input.Value);
		}
		if (inputs.Count == 0)
		{
			merged = default;
			return false;
		}

		var referent = inputs[0].ReferentType;
		var incompatible = inputs
			.Select(static input => input.ReferentType)
			.FirstOrDefault(candidate => !SameReferentType(candidate, referent));
		if (incompatible is not null)
		{
			throw Incompatible(
				function,
				ilOffset,
				referent,
				incompatible,
				"control-flow merge");
		}
		merged = new M68kManagedByrefType(
			referent,
			inputs.Any(static input => input.IsReadOnly));
		return true;
	}

	private static bool SameReferentType(CilType first, CilType second)
	{
		if (first == second)
		{
			return true;
		}
		if (StringComparer.Ordinal.Equals(first.DisplayName, second.DisplayName) &&
			first.GenericArguments.IsDefaultOrEmpty &&
			second.GenericArguments.IsDefaultOrEmpty)
		{
			return true;
		}
		var isAdmittedConstructedValue =
			CompilationModule.IsSupportedSpanLikeType(first) &&
				CompilationModule.IsSupportedSpanLikeType(second) ||
			CompilationModule.IsSupportedMemoryLikeType(first) &&
				CompilationModule.IsSupportedMemoryLikeType(second) ||
			CompilationModule.IsListEnumeratorType(first) &&
				CompilationModule.IsListEnumeratorType(second) ||
			first.IsNullable && second.IsNullable;
		return isAdmittedConstructedValue &&
			first.DisplayName.Split('<', 2)[0] ==
				second.DisplayName.Split('<', 2)[0] &&
			first.GenericArguments.SequenceEqual(second.GenericArguments);
	}

	private static bool Assign(
		IDictionary<int, M68kManagedByrefType?> tracked,
		int value,
		M68kManagedByrefType description)
	{
		if (tracked[value] == description)
			return false;
		tracked[value] = description;
		return true;
	}

	private static bool TryGetArgumentType(
		CilMethod method,
		int argumentIndex,
		out CilType type)
	{
		if (method.Signature.Header.IsInstance)
		{
			if (argumentIndex == 0)
			{
				type = new CilType(
					CilTypeKind.ValueType,
					4,
					method.DisplayName.Split("::", StringSplitOptions.None)[0]);
				return true;
			}
			argumentIndex--;
		}
		if (argumentIndex < 0 ||
			argumentIndex >= method.Signature.ParameterTypes.Length)
		{
			type = null!;
			return false;
		}
		type = method.Signature.ParameterTypes[argumentIndex];
		return true;
	}

	private static bool IsReadOnlyArgument(CilMethod method, int argumentIndex)
	{
		if (method.Signature.Header.IsInstance)
		{
			if (argumentIndex == 0)
				return false;
			argumentIndex--;
		}
		return !method.ParameterFlags.IsDefault &&
			argumentIndex >= 0 &&
			argumentIndex < method.ParameterFlags.Length &&
			(method.ParameterFlags[argumentIndex] & ParameterAttributes.In) != 0;
	}

	private static bool WritesThroughManagedByref(OpCode op) =>
		op == OpCodes.Stfld ||
		op == OpCodes.Stobj ||
		op == OpCodes.Initobj ||
		op == OpCodes.Cpobj ||
		op == OpCodes.Stind_I ||
		op == OpCodes.Stind_I1 ||
		op == OpCodes.Stind_I2 ||
		op == OpCodes.Stind_I4 ||
		op == OpCodes.Stind_I8 ||
		op == OpCodes.Stind_R4 ||
		op == OpCodes.Stind_R8 ||
		op == OpCodes.Stind_Ref;

	private static M68kCompilationException Incompatible(
		M68kMachineFunction function,
		int ilOffset,
		CilType first,
		CilType second,
		string context) =>
		new(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"Managed byref {context} has incompatible referent types '{first.DisplayName}' and '{second.DisplayName}'. Exact referent identity is required.",
			function.DisplayName,
			ilOffset);
}

internal static class M68kByrefProvenanceAnalyzer
{
	public static IReadOnlyDictionary<int, M68kByrefProvenance> Analyze(
		M68kMachineFunction function,
		bool allowCallerBorrowedByrefs,
		out IReadOnlyDictionary<int, int> canonicalOwners)
	{
		var managedPointers = function.Values.Values
			.Where(static value =>
				value.Kind == CilStackValueKind.ManagedPointer)
			.Select(static value => value.Id)
			.ToHashSet();
		var inferred = managedPointers.ToDictionary(
			static value => value,
			static _ => (M68kByrefProvenance?)null);
		canonicalOwners = BuildCanonicalGcOwners(function);

		var changed = true;
		while (changed)
		{
			changed = false;
			var spillProvenance = InferSpillProvenance(function, inferred);
			foreach (var block in function.Blocks)
			{
				foreach (var phi in block.Phis)
				{
					if (!managedPointers.Contains(phi.Definition) ||
						!TryMerge(
							phi.Inputs.Values,
							inferred,
							out var provenance))
					{
						continue;
					}
					changed |= Assign(inferred, phi.Definition, provenance);
				}

				foreach (var instruction in block.Instructions)
				{
					foreach (var definition in instruction.Definitions)
					{
						if (!managedPointers.Contains(definition) ||
							!TryInferInstruction(
								instruction,
								inferred,
								spillProvenance,
								canonicalOwners,
								allowCallerBorrowedByrefs,
								out var provenance))
						{
							continue;
						}
						changed |= Assign(inferred, definition, provenance);
					}
				}
			}
		}

		return inferred.ToDictionary(
			static item => item.Key,
			static item => item.Value ??
				new M68kByrefProvenance(M68kByrefProvenanceKind.Unknown));
	}

	private static IReadOnlyDictionary<int, M68kByrefProvenance>
		InferSpillProvenance(
			M68kMachineFunction function,
			IReadOnlyDictionary<int, M68kByrefProvenance?> inferred)
	{
		var stores = function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction =>
				instruction.Operation == M68kMachineOperation.SpillStore &&
				instruction.SpillSlotIndex is not null &&
				instruction.Uses.Length == 1)
			.GroupBy(static instruction => instruction.SpillSlotIndex!.Value);
		var result = new Dictionary<int, M68kByrefProvenance>();
		foreach (var group in stores)
		{
			if (TryMerge(
					group.Select(static instruction => instruction.Uses[0]),
					inferred,
					out var provenance))
			{
				result.Add(group.Key, provenance);
			}
		}
		return result;
	}

	private static bool TryInferInstruction(
		M68kMachineInstruction instruction,
		IReadOnlyDictionary<int, M68kByrefProvenance?> inferred,
		IReadOnlyDictionary<int, M68kByrefProvenance> spillProvenance,
		IReadOnlyDictionary<int, int> canonicalOwners,
		bool allowCallerBorrowedByrefs,
		out M68kByrefProvenance provenance)
	{
		if (instruction.Operation == M68kMachineOperation.Argument)
		{
			provenance = new M68kByrefProvenance(
				allowCallerBorrowedByrefs
					? M68kByrefProvenanceKind.CallerBorrowed
					: M68kByrefProvenanceKind.Unknown);
			return true;
		}
		if (instruction.Operation is
			M68kMachineOperation.LocalAddress or
			M68kMachineOperation.ArgumentAddress or
			M68kMachineOperation.AggregateArrayLoad or
			M68kMachineOperation.AggregateIndirectLoad)
		{
			provenance = new M68kByrefProvenance(
				M68kByrefProvenanceKind.Frame);
			return true;
		}
		if (instruction.SourceInstruction?.OpCode == OpCodes.Ldsflda)
		{
			provenance = new M68kByrefProvenance(
				M68kByrefProvenanceKind.Static);
			return true;
		}
		if (instruction.SourceInstruction?.OpCode == OpCodes.Ldflda &&
			instruction.Uses.Length != 0 &&
			canonicalOwners.TryGetValue(instruction.Uses[0], out var objectOwner))
		{
			provenance = new M68kByrefProvenance(
				M68kByrefProvenanceKind.ObjectInterior,
				objectOwner);
			return true;
		}
		if (instruction.Operation == M68kMachineOperation.ArrayAddress &&
			instruction.Uses.Length != 0 &&
			canonicalOwners.TryGetValue(instruction.Uses[0], out var arrayOwner))
		{
			provenance = new M68kByrefProvenance(
				M68kByrefProvenanceKind.ArrayInterior,
				arrayOwner);
			return true;
		}
		if (instruction.Operation == M68kMachineOperation.Unbox &&
			instruction.SourceInstruction?.OpCode == OpCodes.Unbox &&
			instruction.Uses.Length != 0 &&
			canonicalOwners.TryGetValue(instruction.Uses[0], out var boxOwner))
		{
			provenance = new M68kByrefProvenance(
				M68kByrefProvenanceKind.BoxInterior,
				boxOwner);
			return true;
		}
		if (instruction.Operation == M68kMachineOperation.Copy &&
			instruction.Uses.Length == 1 &&
			inferred.TryGetValue(instruction.Uses[0], out var copied) &&
			copied is not null)
		{
			provenance = copied.Value;
			return true;
		}
		if (instruction.Operation == M68kMachineOperation.SpillLoad &&
			instruction.SpillSlotIndex is { } slot &&
			spillProvenance.TryGetValue(slot, out provenance))
		{
			return true;
		}

		provenance = new M68kByrefProvenance(
			M68kByrefProvenanceKind.Unknown);
		return instruction.Operation is not
			M68kMachineOperation.Copy and not
			M68kMachineOperation.SpillLoad;
	}

	private static IReadOnlyDictionary<int, int> BuildCanonicalGcOwners(
		M68kMachineFunction function)
	{
		var gcValues = function.Values.Values
			.Where(static value => value.IsGcReference)
			.Select(static value => value.Id)
			.ToHashSet();
		var parent = gcValues.ToDictionary(static value => value);

		int Find(int value)
		{
			var root = value;
			while (parent[root] != root)
				root = parent[root];
			while (parent[value] != value)
			{
				var next = parent[value];
				parent[value] = root;
				value = next;
			}
			return root;
		}

		void Union(int first, int second)
		{
			var firstRoot = Find(first);
			var secondRoot = Find(second);
			if (firstRoot == secondRoot)
				return;
			var root = Math.Min(firstRoot, secondRoot);
			parent[firstRoot] = root;
			parent[secondRoot] = root;
		}

		foreach (var instruction in function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction =>
				instruction.Operation == M68kMachineOperation.Copy &&
				instruction.Uses.Length == 1 &&
				instruction.Definitions.Length == 1))
		{
			if (gcValues.Contains(instruction.Uses[0]) &&
				gcValues.Contains(instruction.Definitions[0]))
			{
				Union(instruction.Uses[0], instruction.Definitions[0]);
			}
		}

		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var phi in function.Blocks.SelectMany(static block => block.Phis))
			{
				if (!gcValues.Contains(phi.Definition) || phi.Inputs.Count == 0)
					continue;
				var roots = phi.Inputs.Values
					.Where(gcValues.Contains)
					.Select(Find)
					.Distinct()
					.ToArray();
				if (roots.Length != 1)
					continue;
				var prior = Find(phi.Definition);
				Union(phi.Definition, roots[0]);
				changed |= Find(phi.Definition) != prior;
			}
		}

		return gcValues.ToDictionary(static value => value, Find);
	}

	private static bool TryMerge(
		IEnumerable<int> values,
		IReadOnlyDictionary<int, M68kByrefProvenance?> inferred,
		out M68kByrefProvenance provenance)
	{
		var inputs = new List<M68kByrefProvenance>();
		foreach (var value in values)
		{
			if (!inferred.TryGetValue(value, out var input) || input is null)
			{
				provenance = default;
				return false;
			}
			inputs.Add(input.Value);
		}
		if (inputs.Count == 0)
		{
			provenance = default;
			return false;
		}

		var first = inputs[0];
		provenance = first;
		if (inputs.All(input => input == first))
		{
			return true;
		}
		if (inputs.All(static input => input.IsSafeWithoutOwnerRoot))
		{
			provenance = new M68kByrefProvenance(
				M68kByrefProvenanceKind.Ownerless);
			return true;
		}
		provenance = new M68kByrefProvenance(
			M68kByrefProvenanceKind.Unknown);
		return true;
	}

	private static bool Assign(
		IDictionary<int, M68kByrefProvenance?> inferred,
		int value,
		M68kByrefProvenance provenance)
	{
		if (inferred[value] == provenance)
		{
			return false;
		}
		inferred[value] = provenance;
		return true;
	}
}

internal static class M68kByrefOwnerRooting
{
	private sealed record OwnerDominanceInfo(
		IReadOnlyDictionary<int, HashSet<int>> Dominators,
		IReadOnlyDictionary<int, (int BlockId, int Index)> Definitions,
		IReadOnlyDictionary<int, int> InstructionIndices);

	public static void Insert(
		M68kMachineFunction function,
		bool allowUntrackedManagedByrefs = false,
		bool allowCallerBorrowedByrefs = false,
		bool rejectManagedByrefReturn = false)
	{
		M68kManagedByrefEscapeValidator.Validate(
			function,
			allowUntrackedManagedByrefs);
		var provenance = M68kByrefProvenanceAnalyzer.Analyze(
			function,
			allowCallerBorrowedByrefs,
			out var canonicalOwners);
		MarkFrameDependentCalls(function, provenance);
		var ownerDominance = BuildOwnerDominance(function);
		AttachTransportedOwners(
			function,
			provenance,
			canonicalOwners,
			ownerDominance);
		var blockLiveness = M68kLivenessAnalysis.Analyze(function);
		var liveness = M68kLivenessAnalysis.AnalyzeInstructions(
			function,
			blockLiveness);
		if (rejectManagedByrefReturn)
		{
			var returning = function.Blocks
				.SelectMany(static block => block.Instructions)
				.FirstOrDefault(instruction =>
					instruction.Operation == M68kMachineOperation.Return &&
					instruction.Uses.Length == 1 &&
					function.Values[instruction.Uses[0]].Kind ==
						CilStackValueKind.ManagedPointer);
			if (returning is not null)
			{
				var valueId = returning.Uses[0];
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedInstruction,
					$"Managed byref return v{valueId} with {provenance[valueId].Kind} provenance requires a Phase 5C return-owner/lifetime summary.",
					function.DisplayName,
					returning.IlOffset);
			}
		}
		foreach (var block in function.Blocks)
		{
			var rewritten = new List<M68kMachineInstruction>();
			foreach (var instruction in block.Instructions)
			{
				rewritten.Add(instruction);
				if (!instruction.IsSafepoint)
				{
					continue;
				}

				var owners = new HashSet<int>();
				foreach (var valueId in liveness.LiveBefore[instruction.Id]
					.Where(value =>
						function.Values[value].Kind ==
							CilStackValueKind.ManagedPointer))
				{
					var byref = provenance[valueId];
					if (byref.IsSafeWithoutOwnerRoot)
					{
						continue;
					}
					// ManagedPoolRuntimeModule is a compiler-resolved, closed set of
					// allocator methods. Those methods intentionally construct raw
					// M68kAddress values and cannot collect while their own helper
					// calls are in progress. Never infer this trust from a namespace
					// or method name; the caller supplies exact resolved type identity.
					if (allowUntrackedManagedByrefs &&
						byref.Kind == M68kByrefProvenanceKind.Unknown)
					{
						continue;
					}
					if (byref.OwnerValue is not { } owner)
					{
						throw Unsupported(function, instruction, valueId, byref);
					}
					owner = FindDominatingOwner(
						function,
						block,
						instruction,
						owner,
						canonicalOwners,
						ownerDominance);
					if (!function.Values.TryGetValue(owner, out var ownerValue) ||
						!ownerValue.IsGcReference)
					{
						throw new InvalidOperationException(
							$"Managed byref v{valueId} names non-GC owner v{owner}.");
					}
					owners.Add(owner);
				}

				if (owners.Count != 0)
				{
					rewritten.Add(function.CreateInstruction(
						M68kMachineOperation.ByrefOwnerKeepAlive,
						instruction.IlOffset,
						uses: owners.Order()));
				}
			}
			block.Instructions.Clear();
			block.Instructions.AddRange(rewritten);
		}
		M68kMachineIrVerifier.Verify(function);
	}

	private static void MarkFrameDependentCalls(
		M68kMachineFunction function,
		IReadOnlyDictionary<int, M68kByrefProvenance> provenance)
	{
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (instruction.Operation != M68kMachineOperation.Call ||
					!instruction.Uses.Any(value =>
						provenance.TryGetValue(value, out var byref) &&
						byref.Kind == M68kByrefProvenanceKind.Frame))
				{
					continue;
				}

				block.Instructions[index] = instruction with
				{
					RequiresLiveCallerFrame = true
				};
			}
		}
	}

	private static void AttachTransportedOwners(
		M68kMachineFunction function,
		IReadOnlyDictionary<int, M68kByrefProvenance> provenance,
		IReadOnlyDictionary<int, int> canonicalOwners,
		OwnerDominanceInfo dominance)
	{
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (!instruction.TransportsManagedByrefOwner)
				{
					continue;
				}
				if (instruction.Uses.Length != 1)
				{
					throw new InvalidOperationException(
						$"Managed-byref owner transport {instruction.Id} has {instruction.Uses.Length} source operands before owner attachment.");
				}

				var valueId = instruction.Uses[0];
				var byref = provenance[valueId];
				if (byref.Kind is M68kByrefProvenanceKind.Frame or
					M68kByrefProvenanceKind.Static or
					M68kByrefProvenanceKind.Ownerless)
				{
					continue;
				}
				if (byref.Kind == M68kByrefProvenanceKind.CallerBorrowed)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedInstruction,
						$"Managed byref v{valueId} with CallerBorrowed provenance cannot initialize Span-like storage because the current managed-byref ABI does not transport the caller's GC owner.",
						function.DisplayName,
						instruction.IlOffset);
				}
				if (byref.OwnerValue is not { } owner)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedInstruction,
						$"Managed byref v{valueId} with {byref.Kind} provenance cannot initialize Span-like storage without an exact transported GC owner.",
						function.DisplayName,
						instruction.IlOffset);
				}

				owner = FindDominatingOwner(
					function,
					block,
					instruction,
					owner,
					canonicalOwners,
					dominance);
				block.Instructions[index] = instruction with
				{
					Uses = instruction.Uses.Add(owner)
				};
			}
		}
	}

	private static int FindDominatingOwner(
		M68kMachineFunction function,
		M68kMachineBlock useBlock,
		M68kMachineInstruction useInstruction,
		int canonicalOwner,
		IReadOnlyDictionary<int, int> canonicalOwners,
		OwnerDominanceInfo dominance)
	{
		var useIndex = dominance.InstructionIndices[useInstruction.Id];
		var candidates = canonicalOwners
			.Where(item => item.Value == canonicalOwner &&
				function.Values[item.Key].IsGcReference &&
				dominance.Definitions.ContainsKey(item.Key))
			.Select(item => (
				Value: item.Key,
				Definition: dominance.Definitions[item.Key]))
			.Where(item =>
				dominance.Dominators[useBlock.Id].Contains(
					item.Definition.BlockId) &&
				(item.Definition.BlockId != useBlock.Id ||
				 item.Definition.Index < useIndex))
			.OrderByDescending(item =>
				dominance.Dominators[item.Definition.BlockId].Count)
			.ThenByDescending(item => item.Definition.Index)
			.ThenBy(item => item.Value)
			.ToArray();
		if (candidates.Length == 0)
		{
			throw new InvalidOperationException(
				$"No equivalent GC owner for canonical v{canonicalOwner} dominates instruction {useInstruction.Id}.");
		}
		return candidates[0].Value;
	}

	private static OwnerDominanceInfo BuildOwnerDominance(
		M68kMachineFunction function)
	{
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		var allBlockIds = blocks.Keys.ToHashSet();
		var dominators = blocks.Keys.ToDictionary(
			static blockId => blockId,
			blockId => blockId == function.EntryBlockId
				? new HashSet<int> { blockId }
				: new HashSet<int>(allBlockIds));
		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var block in function.Blocks.Where(block =>
				block.Id != function.EntryBlockId))
			{
				var next = block.Predecessors.Count == 0
					? new HashSet<int>()
					: new HashSet<int>(dominators[block.Predecessors[0]]);
				foreach (var predecessor in block.Predecessors.Skip(1))
					next.IntersectWith(dominators[predecessor]);
				next.Add(block.Id);
				if (!dominators[block.Id].SetEquals(next))
				{
					dominators[block.Id] = next;
					changed = true;
				}
			}
		}

		var definitions = new Dictionary<int, (int BlockId, int Index)>();
		var instructionIndices = new Dictionary<int, int>();
		foreach (var block in function.Blocks)
		{
			foreach (var phi in block.Phis)
				definitions[phi.Definition] = (block.Id, -1);
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				instructionIndices[block.Instructions[index].Id] = index;
				foreach (var definition in block.Instructions[index].Definitions)
					definitions[definition] = (block.Id, index);
			}
		}
		return new OwnerDominanceInfo(
			dominators,
			definitions,
			instructionIndices);
	}

	private static M68kCompilationException Unsupported(
		M68kMachineFunction function,
		M68kMachineInstruction instruction,
		int valueId,
		M68kByrefProvenance provenance) =>
		new(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"Managed byref v{valueId} with {provenance.Kind} provenance is live at a GC safepoint. Phase 5C requires a transported owner root; frame, static, directly-owned interior, and caller-borrowed byrefs are supported.",
			function.DisplayName,
			instruction.IlOffset);
}
