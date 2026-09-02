/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kBulkCopyTarget(
	CilMethod? ManagedMethod,
	M68kExternalCallConvention? ExternalCall,
	ImmutableArray<M68kRegister> ParameterRegisters,
	M68kRegisterSet Clobbers);

/// <summary>
/// An ordinary-memory, reference-free copy between proven disjoint ranges.
/// Frame objects carry complete read/write facts; an absent object denotes the
/// ABI-owned outgoing argument or return buffer, never an arbitrary guest pointer.
/// </summary>
internal sealed record M68kMachineBulkCopy(
	M68kBulkCopyTarget Target,
	int ByteCount,
	int SourceAlignment,
	int DestinationAlignment,
	M68kMemoryObject? Source,
	M68kMemoryObject? Destination);

/// <summary>
/// Selects surviving large frame copies after memory promotion. All helper ABI
/// operands and clobbers are materialized here, before physical allocation.
/// The original lowering remains available for unknown, overlapping, GC, EH,
/// volatile, and externally observable memory operations.
/// </summary>
internal static class M68kBulkCopyLowering
{
	public static int Run(
		M68kMachineFunction function,
		CompilationModule module,
		M68kBulkCopyTarget target,
		int minimumBytes)
	{
		if (function.SourceMethod is not { } method || function.HasExceptionHandlers || function.HasDynamicStackAllocation)
		{
			return 0;
		}
		var definitions = function.Blocks.SelectMany(static block => block.Instructions)
			.SelectMany(instruction => instruction.Definitions.Select(value => (value, instruction)))
			.ToDictionary(static item => item.value, static item => item.instruction);
		var count = 0;
		foreach (var block in function.Blocks)
		{
			var rewritten = new List<M68kMachineInstruction>();
			foreach (var instruction in block.Instructions)
			{
				if (instruction.Uses is not [var source] ||
					function.Values[source].Kind != CilStackValueKind.AggregateAddress ||
					(instruction.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0 ||
					!TryFrameObject(function, definitions, source, out var sourceObject))
				{
					rewritten.Add(instruction);
					continue;
				}

				int bytes;
				M68kMemoryObject? destinationObject = null;
				M68kMachineOperation addressOperation;
				int? homeIndex = null;
				switch (instruction.Operation)
				{
					case M68kMachineOperation.LocalStore when
						instruction.ArgumentIndex is { } localIndex &&
						localIndex < method.Locals.Length &&
						module.TryGetReferenceFreeStructLayout(method.Locals[localIndex],
							method.ModuleName, out var layout) && layout.Size > 4 &&
						function.LocalHomes.TryGetValue(localIndex, out var home) && !home.HasGcReferences:
						bytes = layout.Size;
						homeIndex = localIndex;
						addressOperation = M68kMachineOperation.LocalAddress;
						destinationObject = M68kMemoryModel.FrameObject(
							M68kMemoryObjectKind.FrameSlot, localIndex, home, size: bytes);
						break;
					case M68kMachineOperation.OutgoingArgumentPush when instruction.ArgumentIndex is > 4 and var pushedBytes:
						bytes = pushedBytes;
						addressOperation = M68kMachineOperation.OutgoingArgumentReserve;
						break;
					case M68kMachineOperation.Return when !instruction.ReturnBufferWritten &&
						module.TryGetReferenceFreeStructLayout(method.Signature.ReturnType,
							method.ModuleName, out var returnLayout) && returnLayout.Size > 4:
						bytes = returnLayout.Size;
						addressOperation = M68kMachineOperation.ReturnBufferAddress;
						break;
					default:
						rewritten.Add(instruction);
						continue;
				}
				if (bytes < minimumBytes || bytes > sourceObject.Size ||
					(bytes & 3) != 0 ||
					destinationObject is { } destinationRange && sourceObject.Overlaps(destinationRange))
				{
					rewritten.Add(instruction);
					continue;
				}

				var destination = function.CreateValue(CilStackValueKind.AggregateAddress,
					M68kMachineValueWidth.Long, M68kRegisterSet.Address);
				rewritten.Add(function.CreateInstruction(addressOperation, instruction.IlOffset,
					definitions: [destination.Id],
					argumentIndex: addressOperation == M68kMachineOperation.OutgoingArgumentReserve ? bytes : homeIndex,
					memoryEffect: addressOperation switch
					{
						M68kMachineOperation.OutgoingArgumentReserve => M68kMachineMemoryEffect.Write,
						M68kMachineOperation.ReturnBufferAddress => M68kMachineMemoryEffect.Read,
						_ => M68kMachineMemoryEffect.None
					}, origin: instruction.Origin));
				var length = function.CreateValue(CilStackValueKind.Int32,
					M68kMachineValueWidth.Long, M68kRegisterSet.Data,
					isRematerializable: true);
				rewritten.Add(function.CreateInstruction(M68kMachineOperation.Constant, instruction.IlOffset,
					definitions: [length.Id], immediate: bytes,
					constantValue: M68kMachineConstant.Int32(bytes), origin: instruction.Origin));
				var operands = new List<int>();
				var arguments = new[] { source, destination.Id, length.Id };
				for (var index = 0; index < arguments.Length; index++)
				{
					var register = target.ParameterRegisters[index];
					// A provider is allowed to carry pointer bits in a data register.
					var staged = function.CreateValue(CilStackValueKind.Int32,
						M68kMachineValueWidth.Long, M68kRegisterSet.From(register),
						precoloredRegister: register);
					rewritten.Add(function.CreateInstruction(M68kMachineOperation.Copy, instruction.IlOffset,
						uses: [arguments[index]], definitions: [staged.Id], origin: instruction.Origin));
					operands.Add(staged.Id);
				}
				if (target.ExternalCall is { } external)
				{
					if (external.BaseSource == M68kExternalBaseSource.CachedPointer && external.CacheRegister is { } cache)
					{
						function.ReservedRegisters = function.ReservedRegisters.Add(cache);
					}
					var platformBase = function.CreateValue(CilStackValueKind.Int32,
						M68kMachineValueWidth.Long, M68kRegisterSet.From(external.BaseRegister),
						precoloredRegister: external.BaseRegister);
					rewritten.Add(function.CreateInstruction(M68kMachineOperation.PlatformBaseLoad,
						instruction.IlOffset, definitions: [platformBase.Id],
						memoryEffect: external.BaseSource == M68kExternalBaseSource.WritableSlot
							? M68kMachineMemoryEffect.Read : M68kMachineMemoryEffect.None,
						platformBaseConvention: external, origin: instruction.Origin));
					operands.Add(platformBase.Id);
				}
				rewritten.Add(function.CreateInstruction(M68kMachineOperation.BulkCopy, instruction.IlOffset,
					uses: operands, clobbers: target.Clobbers,
					memoryEffect: M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write,
					memorySize: bytes, origin: instruction.Origin) with
				{
					BulkCopy = new M68kMachineBulkCopy(target, bytes, 2, 2,
						sourceObject with { Size = bytes }, destinationObject)
				});
				if (instruction.Operation == M68kMachineOperation.Return)
				{
					rewritten.Add(instruction with { Uses = [], ReturnBufferWritten = true });
				}
				count++;
			}
			block.Instructions.Clear();
			block.Instructions.AddRange(rewritten);
		}
		M68kMachineIrVerifier.Verify(function);
		return count;
	}

	private static bool TryFrameObject(
		M68kMachineFunction function,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions,
		int value,
		out M68kMemoryObject result)
	{
		var visited = new HashSet<int>();
		while (visited.Add(value) && definitions.TryGetValue(value, out var producer))
		{
			if (producer.Operation == M68kMachineOperation.Copy && producer.Uses is [var source])
			{
				value = source;
				continue;
			}
			if (producer.ArgumentIndex is { } index)
			{
				var argument = producer.Operation == M68kMachineOperation.ArgumentAddress;
				var local = producer.Operation is M68kMachineOperation.LocalAddress or
					M68kMachineOperation.AggregateFieldLoad or M68kMachineOperation.AggregateArrayLoad or
					M68kMachineOperation.AggregateIndirectLoad;
				var homes = argument ? function.ArgumentHomes : function.LocalHomes;
				if ((argument || local) && homes.TryGetValue(index, out var home) && !home.HasGcReferences)
				{
					result = M68kMemoryModel.FrameObject(argument ? M68kMemoryObjectKind.ArgumentHome :
						M68kMemoryObjectKind.FrameSlot, index, home);
					return true;
				}
			}
			break;
		}
		result = default;
		return false;
	}
}
