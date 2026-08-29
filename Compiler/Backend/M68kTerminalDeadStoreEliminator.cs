/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kTerminalDeadStoreStatistics(
	int Candidates,
	int Removed,
	IReadOnlyDictionary<string, int> Rejections,
	IReadOnlyList<M68kTerminalDeadStoreCandidate> Details)
{
	public static M68kTerminalDeadStoreStatistics Empty { get; } =
		new(
			0,
			0,
			new Dictionary<string, int>(StringComparer.Ordinal),
			Array.Empty<M68kTerminalDeadStoreCandidate>());
}

internal sealed record M68kTerminalDeadStoreCandidate(
	M68kMemoryObjectKind Kind,
	string Identity,
	int IlOffset,
	bool Removed,
	string? RejectionReason);

internal static class M68kTerminalDeadStoreEliminator
{
	public static M68kTerminalDeadStoreStatistics Run(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		bool terminatingEntry,
		IReadOnlySet<CilFieldIdentity> escapedStaticFields)
	{
		if (!terminatingEntry)
		{
			return M68kTerminalDeadStoreStatistics.Empty;
		}

		var effects = function.Blocks
			.SelectMany(static block => block.Instructions)
			.ToDictionary(
				static instruction => instruction.Id,
				instruction => M68kMemoryModel.Summarize(
					method,
					module,
					instruction));
		var candidates = FindCandidates(
			function,
			method,
			module,
			effects,
			escapedStaticFields);
		if (candidates.Count == 0)
		{
			return M68kTerminalDeadStoreStatistics.Empty;
		}

		var candidateObjects = candidates.Values
			.Select(static candidate => candidate.Object)
			.ToHashSet();
		var managedRootObjects = candidateObjects
			.Where(static item => item.IsManagedRoot)
			.ToHashSet();
		var effectiveSuccessors = BuildEffectiveSuccessors(function, method);
		var liveIn = function.Blocks.ToDictionary(
			static block => block.Id,
			static _ => new HashSet<M68kMemoryObject>());
		var liveOut = function.Blocks.ToDictionary(
			static block => block.Id,
			static _ => new HashSet<M68kMemoryObject>());

		bool changed;
		do
		{
			changed = false;
			for (var index = function.Blocks.Count - 1; index >= 0; index--)
			{
				var block = function.Blocks[index];
				var nextOut = new HashSet<M68kMemoryObject>();
				foreach (var successor in effectiveSuccessors[block.Id])
				{
					nextOut.UnionWith(liveIn[successor]);
				}
				if (!HasModeledContinuation(
						block,
					effectiveSuccessors[block.Id],
						method))
				{
					nextOut.UnionWith(candidateObjects);
				}

				var nextIn = TransferBlock(
					block,
					nextOut,
					effects,
					candidateObjects,
					managedRootObjects);
				if (!liveOut[block.Id].SetEquals(nextOut))
				{
					liveOut[block.Id] = nextOut;
					changed = true;
				}
				if (!liveIn[block.Id].SetEquals(nextIn))
				{
					liveIn[block.Id] = nextIn;
					changed = true;
				}
			}
		}
		while (changed);

		var removable = new HashSet<int>();
		foreach (var block in function.Blocks)
		{
			var live = new HashSet<M68kMemoryObject>(liveOut[block.Id]);
			for (var index = block.Instructions.Count - 1; index >= 0; index--)
			{
				var instruction = block.Instructions[index];
				var effect = effects[instruction.Id];
				if (candidates.TryGetValue(instruction.Id, out var candidate) &&
					!live.Contains(candidate.Object))
				{
					removable.Add(instruction.Id);
				}
				TransferInstruction(
					live,
					effect,
					candidateObjects,
					managedRootObjects);
			}
		}
		foreach (var block in function.Blocks)
		{
			block.Instructions.RemoveAll(instruction =>
				removable.Contains(instruction.Id));
		}
		RemoveDeadPureDefinitions(function);
		M68kMachineIrVerifier.Verify(function);

		var rejections = new Dictionary<string, int>(StringComparer.Ordinal);
		if (candidates.Count != removable.Count)
		{
			rejections.Add("value-may-be-observed", candidates.Count - removable.Count);
		}
		return new M68kTerminalDeadStoreStatistics(
			candidates.Count,
			removable.Count,
			rejections,
			candidates.Select(pair => new M68kTerminalDeadStoreCandidate(
				pair.Value.Object.Kind,
				pair.Value.Object.Identity,
				pair.Value.IlOffset,
				removable.Contains(pair.Key),
				removable.Contains(pair.Key) ? null : "value-may-be-observed"))
			.OrderBy(static detail => detail.IlOffset)
			.ToArray());
	}

	private sealed record Candidate(M68kMemoryObject Object, int IlOffset);

	private static Dictionary<int, Candidate> FindCandidates(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		IReadOnlyDictionary<int, M68kObjectMemoryEffect> effects,
		IReadOnlySet<CilFieldIdentity> escapedStaticFields)
	{
		var definitions = function.Blocks
			.SelectMany(static block => block.Instructions)
			.SelectMany(instruction => instruction.Definitions.Select(
				definition => (definition, instruction)))
			.ToDictionary(static item => item.definition, static item => item.instruction);
		var result = new Dictionary<int, Candidate>();
		foreach (var instruction in function.Blocks.SelectMany(
			static block => block.Instructions))
		{
			var effect = effects[instruction.Id];
			var isKnownZeroLibraryBaseSet =
				effect.WritesExact.Count == 1 &&
				effect.WritesExact.Single().Kind == M68kMemoryObjectKind.LibraryBase &&
				instruction.Uses.Length == 0 &&
				instruction.Immediate == 0;
			if (effect.WritesExact.Count != 1 ||
				effect.IsVolatile ||
				effect.MayTrap ||
				(!isKnownZeroLibraryBaseSet &&
					(instruction.Uses.Length != 1 ||
					 !IsDefaultValue(
						instruction.Uses[0],
						definitions,
						method,
						module,
						new HashSet<int>()))))
			{
				continue;
			}

			var memoryObject = effect.WritesExact.Single();
			if (memoryObject.Kind == M68kMemoryObjectKind.LibraryBase)
			{
				result.Add(
					instruction.Id,
					new Candidate(memoryObject, instruction.IlOffset));
				continue;
			}
			if (memoryObject.Kind != M68kMemoryObjectKind.StaticField ||
				instruction.SourceInstruction is not { Operand: int token } source)
			{
				continue;
			}

			var field = module.ResolveFieldToken(token, method, source.Offset);
			if (escapedStaticFields.Contains(field.Identity) ||
				!IsPrivateNonVolatileField(method, module, field))
			{
				continue;
			}
			result.Add(
				instruction.Id,
				new Candidate(memoryObject, instruction.IlOffset));
		}
		return result;
	}

	private static bool IsPrivateNonVolatileField(
		CilMethod method,
		CompilationModule module,
		CilField field)
	{
		if (field.ModuleName != method.ModuleName ||
			field.Handle.IsNil)
		{
			return false;
		}
		var definition = module.Reader.GetFieldDefinition(field.Handle);
		if ((definition.Attributes & FieldAttributes.FieldAccessMask) !=
				FieldAttributes.Private ||
			(definition.Attributes &
				(FieldAttributes.Literal | FieldAttributes.InitOnly)) != 0)
		{
			return false;
		}

		// Reject every custom-modified field. In particular this excludes the
		// required IsVolatile modifier without teaching the optimizer enough
		// signature parsing to distinguish benign modifiers.
		var signature = module.Reader.GetBlobReader(definition.Signature);
		_ = signature.ReadSignatureHeader();
		if (signature.RemainingBytes == 0)
		{
			return false;
		}
		var probe = signature;
		var element = probe.ReadByte();
		return element is not (0x1F or 0x20); // CMOD_REQD / CMOD_OPT
	}

	private static bool IsDefaultValue(
		int value,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions,
		CilMethod method,
		CompilationModule module,
		HashSet<int> visited)
	{
		if (!visited.Add(value) ||
			!definitions.TryGetValue(value, out var definition))
		{
			return false;
		}
		if (definition.Operation == M68kMachineOperation.Copy &&
			definition.Uses is [var source])
		{
			return IsDefaultValue(source, definitions, method, module, visited);
		}
		if (definition.Operation == M68kMachineOperation.Constant)
		{
			if (definition.ConstantValue is { } constantValue &&
				constantValue.TryGetIntegral(out var integral))
			{
				return integral == 0;
			}
			return definition.SourceInstruction is { } constant &&
				IsZeroConstant(constant);
		}
		if (definition.Operation == M68kMachineOperation.Call &&
			definition.SourceInstruction is { Operand: int token } call)
		{
			var name = module.ResolveMethodToken(token, method, call.Offset).ImportName;
			if (name == "intrinsic:aptr-null")
			{
				return true;
			}
			if (name is
				"intrinsic:cstring-from-pointer" or
				"intrinsic:cstring-to-uint32" or
				"intrinsic:aptr-from-pointer" or
				"intrinsic:aptr-to-uint32" or
				"intrinsic:address-of-ref" or
				"intrinsic:address-to-ref" or
				"intrinsic:ref-cast" &&
				definition.Uses is [var identitySource])
			{
				return IsDefaultValue(
					identitySource,
					definitions,
					method,
					module,
					visited);
			}
		}
		return false;
	}

	private static bool IsZeroConstant(CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Ldnull || op == OpCodes.Ldc_I4_0)
		{
			return true;
		}
		return op == OpCodes.Ldc_I4_S
			? Convert.ToSByte(instruction.Operand) == 0
			: op == OpCodes.Ldc_I4
				? Convert.ToInt32(instruction.Operand) == 0
				: op == OpCodes.Ldc_I8 && Convert.ToInt64(instruction.Operand) == 0;
	}

	private static Dictionary<int, HashSet<int>> BuildEffectiveSuccessors(
		M68kMachineFunction function,
		CilMethod method)
	{
		var result = function.Blocks.ToDictionary(
			static block => block.Id,
			block => block.ControlFlowSuccessors.ToHashSet());
		var blocksByOffset = function.Blocks.ToDictionary(
			static block => block.StartIlOffset);
		var endFinallyBlocks = function.Blocks
			.Where(static block => block.Instructions.LastOrDefault()?.SourceInstruction?.OpCode ==
				OpCodes.Endfinally)
			.ToArray();

		// The machine CFG intentionally contains normal IL control flow only.
		// Terminal memory liveness must also see handler-only reads, so add every
		// enclosing exceptional continuation for instructions which may throw.
		foreach (var block in function.Blocks)
		{
			foreach (var instruction in block.Instructions.Where(
				static instruction => instruction.MayThrow))
			{
				foreach (var region in method.ExceptionRegions.Where(region =>
					region.TryOffset <= instruction.IlOffset &&
					instruction.IlOffset < region.TryEnd))
				{
					var continuationOffset = region.FilterOffset >= 0
						? region.FilterOffset
						: region.HandlerOffset;
					if (blocksByOffset.TryGetValue(
							continuationOffset,
							out var exceptionalContinuation))
					{
						result[block.Id].Add(exceptionalContinuation.Id);
					}
				}
			}
		}

		foreach (var leave in method.Instructions.Where(static instruction =>
			instruction.OpCode == OpCodes.Leave ||
			instruction.OpCode == OpCodes.Leave_S))
		{
			var target = (int)leave.Operand!;
			var finalies = method.ExceptionRegions
				.Where(region =>
					region.IsFinally &&
					region.TryOffset <= leave.Offset &&
					leave.Offset < region.TryEnd &&
					!(region.TryOffset <= target && target < region.TryEnd))
				.OrderBy(static region => region.TryLength)
				.ToArray();
			for (var index = 0; index < finalies.Length; index++)
			{
				var region = finalies[index];
				var regionEndBlocks = endFinallyBlocks.Where(block =>
					block.Instructions[^1].IlOffset >= region.HandlerOffset &&
					block.Instructions[^1].IlOffset < region.HandlerEnd);
				var continuationOffset = index + 1 < finalies.Length
					? finalies[index + 1].HandlerOffset
					: target;
				if (blocksByOffset.TryGetValue(continuationOffset, out var continuation))
				{
					foreach (var endBlock in regionEndBlocks)
					{
						result[endBlock.Id].Add(continuation.Id);
					}
				}
			}
		}
		return result;
	}

	private static bool HasModeledContinuation(
		M68kMachineBlock block,
		IReadOnlySet<int> successors,
		CilMethod method)
	{
		if (successors.Count != 0)
		{
			return true;
		}
		var last = block.Instructions.LastOrDefault();
		if (last?.Operation is M68kMachineOperation.Return or
			M68kMachineOperation.Throw)
		{
			return true;
		}
		if (last?.SourceInstruction?.OpCode != OpCodes.Endfinally)
		{
			return false;
		}
		var region = method.ExceptionRegions
			.Where(candidate =>
				candidate.IsFinally &&
				candidate.HandlerOffset <= last.IlOffset &&
				last.IlOffset < candidate.HandlerEnd)
			.OrderBy(static candidate => candidate.HandlerLength)
			.FirstOrDefault();
		return region is not null &&
			!method.ExceptionRegions.Any(candidate =>
				candidate != region &&
				candidate.TryOffset <= region.TryOffset &&
				region.TryEnd <= candidate.TryEnd);
	}

	private static HashSet<M68kMemoryObject> TransferBlock(
		M68kMachineBlock block,
		IReadOnlySet<M68kMemoryObject> liveOut,
		IReadOnlyDictionary<int, M68kObjectMemoryEffect> effects,
		IReadOnlySet<M68kMemoryObject> candidateObjects,
		IReadOnlySet<M68kMemoryObject> managedRootObjects)
	{
		var live = new HashSet<M68kMemoryObject>(liveOut);
		for (var index = block.Instructions.Count - 1; index >= 0; index--)
		{
			TransferInstruction(
				live,
				effects[block.Instructions[index].Id],
				candidateObjects,
				managedRootObjects);
		}
		return live;
	}

	private static void TransferInstruction(
		HashSet<M68kMemoryObject> live,
		M68kObjectMemoryEffect effect,
		IReadOnlySet<M68kMemoryObject> candidateObjects,
		IReadOnlySet<M68kMemoryObject> managedRootObjects)
	{
		foreach (var written in effect.WritesExact)
		{
			live.Remove(written);
		}
		live.UnionWith(effect.ReadsExact);
		live.UnionWith(effect.EscapesExact);
		if (effect.ObservesRoots)
		{
			live.UnionWith(managedRootObjects);
		}
		if (effect.ReadsUnknown || effect.IsVolatile)
		{
			live.UnionWith(candidateObjects);
		}
	}

	private static void RemoveDeadPureDefinitions(M68kMachineFunction function)
	{
		bool changed;
		do
		{
			var used = function.Blocks
				.SelectMany(static block =>
					block.Instructions.SelectMany(static instruction =>
						instruction.Uses
							.Concat(instruction.LogicalCall?.ArgumentValueIds ?? [])
							.Concat(instruction.LogicalCall?.ResultValueIds ?? []))
						.Concat(block.Phis.SelectMany(static phi => phi.Inputs.Values)))
				.ToHashSet();
			changed = false;
			foreach (var block in function.Blocks)
			{
				changed |= block.Instructions.RemoveAll(instruction =>
					instruction.Definitions.Length != 0 &&
					instruction.Definitions.All(definition => !used.Contains(definition)) &&
					instruction.MemoryEffect == M68kMachineMemoryEffect.None &&
					!instruction.IsSafepoint &&
					!instruction.MayThrow &&
					instruction.Operation is
						M68kMachineOperation.Copy or
						M68kMachineOperation.Constant or
						M68kMachineOperation.Address or
						M68kMachineOperation.Other) != 0;
			}
		}
		while (changed);

		var referenced = function.Blocks
			.SelectMany(static block =>
				block.Instructions.SelectMany(static instruction =>
					instruction.Uses.Concat(instruction.Definitions)
						.Concat(instruction.LogicalCall?.ArgumentValueIds ?? [])
						.Concat(instruction.LogicalCall?.ResultValueIds ?? []))
				.Concat(block.Phis.SelectMany(static phi =>
					phi.Inputs.Values.Append(phi.Definition))))
			.ToHashSet();
		foreach (var value in function.Values.Keys
			.Where(value => !referenced.Contains(value))
			.ToArray())
		{
			function.Values.Remove(value);
		}
	}
}
