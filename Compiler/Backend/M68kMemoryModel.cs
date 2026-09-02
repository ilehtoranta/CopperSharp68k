/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Stable identities for memory that the machine backend can distinguish without
/// relying on physical registers. Heap objects use their canonical allocation SSA
/// owner and a byte range; frame and global objects use a compiler-owned identity.
/// </summary>
internal enum M68kMemoryObjectKind
{
	StaticField,
	LibraryBase,
	RuntimeSlot,
	FrameSlot,
	ArgumentHome,
	ManagedRoot,
	ObjectField,
	ArrayElement,
	AggregateLane,
	UnknownHeap
}

internal readonly record struct M68kMemoryObject(
	M68kMemoryObjectKind Kind,
	string Identity,
	bool IsManagedRoot = false,
	int? OwnerValueId = null,
	int Offset = 0,
	int Size = 0)
{
	public bool IsHeapObject => Kind is
		M68kMemoryObjectKind.ObjectField or
		M68kMemoryObjectKind.ArrayElement or
		M68kMemoryObjectKind.AggregateLane;

	public bool IsGlobalObject => Kind is
		M68kMemoryObjectKind.StaticField or
		M68kMemoryObjectKind.LibraryBase or
		M68kMemoryObjectKind.RuntimeSlot or
		M68kMemoryObjectKind.ManagedRoot;

	public bool Overlaps(M68kMemoryObject other)
	{
		var sameStorageKind = Kind == other.Kind ||
			IsHeapObject && other.IsHeapObject;
		if (!sameStorageKind ||
			!string.Equals(Identity, other.Identity, StringComparison.Ordinal) ||
			OwnerValueId != other.OwnerValueId)
		{
			return false;
		}
		if (Size <= 0 || other.Size <= 0)
		{
			return true;
		}
		return (long)Offset < (long)other.Offset + other.Size &&
			(long)other.Offset < (long)Offset + Size;
	}
}

internal enum M68kExactMemoryAccessKind
{
	Read,
	Write,
	Address,
	Escape
}

internal readonly record struct M68kExactMemoryAccess(
	M68kMemoryObject Object,
	M68kExactMemoryAccessKind Kind,
	int? ValueId = null)
{
	public bool Reads => Kind is
		M68kExactMemoryAccessKind.Read or
		M68kExactMemoryAccessKind.Address or
		M68kExactMemoryAccessKind.Escape;

	public bool Writes => Kind == M68kExactMemoryAccessKind.Write;

	public bool Escapes => Kind is
		M68kExactMemoryAccessKind.Address or
		M68kExactMemoryAccessKind.Escape;
}

internal sealed record M68kObjectMemoryEffect(
	ImmutableHashSet<M68kMemoryObject> ReadsExact,
	ImmutableHashSet<M68kMemoryObject> WritesExact,
	ImmutableHashSet<M68kMemoryObject> EscapesExact,
	bool ReadsUnknown,
	bool WritesUnknown,
	bool ObservesRoots,
	bool IsVolatile,
	bool MayTrap)
{
	public static M68kObjectMemoryEffect None { get; } = new(
		ImmutableHashSet<M68kMemoryObject>.Empty,
		ImmutableHashSet<M68kMemoryObject>.Empty,
		ImmutableHashSet<M68kMemoryObject>.Empty,
		ReadsUnknown: false,
		WritesUnknown: false,
		ObservesRoots: false,
		IsVolatile: false,
		MayTrap: false);

	public static M68kObjectMemoryEffect Unknown(
		M68kMachineInstruction instruction) =>
		None with
		{
			ReadsUnknown =
				(instruction.MemoryEffect & M68kMachineMemoryEffect.Read) != 0,
			WritesUnknown =
				(instruction.MemoryEffect & M68kMachineMemoryEffect.Write) != 0,
			ObservesRoots = instruction.IsSafepoint,
			IsVolatile =
				(instruction.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0,
			MayTrap = instruction.MayThrow
		};
}

/// <summary>Shared exact-memory decoding used by optimization and liveness.</summary>
internal static class M68kMemoryModel
{
	internal const string LibraryBaseGetPrefix =
		"intrinsic:amiga-library-base-get:";
	internal const string LibraryBaseSetPrefix =
		"intrinsic:amiga-library-base-set:";

	public static M68kMemoryObject StaticFieldObject(CilField field)
	{
		var size = field.Type.IsSupportedScalar
			? Math.Max(1, field.Type.Size)
			: 0;
		return new M68kMemoryObject(
			M68kMemoryObjectKind.StaticField,
			$"{field.ModuleName}:0x{MetadataTokens.GetToken(field.Handle):X8}",
			field.Type.IsReference,
			Offset: 0,
			Size: size);
	}

	public static M68kMemoryObject LibraryBaseObject(string identity) =>
		new(
			M68kMemoryObjectKind.LibraryBase,
			identity.StartsWith('_')
				? identity
				: $"_{(identity == "IffParse" ? "IFFParse" : identity)}LibraryBase",
			Offset: 0,
			Size: sizeof(uint));

	public static M68kMemoryObject FrameObject(
		M68kMemoryObjectKind kind,
		int index,
		M68kFrameHome? home = null,
		int offset = 0,
		int? size = null) =>
		new(
			kind,
			index.ToString(System.Globalization.CultureInfo.InvariantCulture),
			home?.IsGcReference == true,
			Offset: offset,
			Size: size ?? home?.Size ?? 0);

	public static M68kObjectMemoryEffect Summarize(
		CilMethod method,
		CompilationModule module,
		M68kMachineInstruction instruction)
	{
		if (instruction.BulkCopy is { } copy)
		{
			// Exact accesses are complete only when both ranges are known. In
			// particular, a known frame read must not hide an unknown ABI write.
			return M68kObjectMemoryEffect.None with
			{
				ReadsExact = copy.Source is { } copySource
					? ImmutableHashSet.Create(copySource) : ImmutableHashSet<M68kMemoryObject>.Empty,
				WritesExact = copy.Destination is { } destination
					? ImmutableHashSet.Create(destination) : ImmutableHashSet<M68kMemoryObject>.Empty,
				ReadsUnknown = copy.Source is null,
				WritesUnknown = copy.Destination is null
			};
		}
		if (!instruction.ExactMemoryAccesses.IsDefaultOrEmpty)
		{
			return FromExactAccesses(instruction);
		}

		var sourceMethod = instruction.Origin?.SourceMethod ?? method;
		var source = instruction.Origin?.SourceInstruction ??
			instruction.SourceInstruction;
		if (source is not null &&
			source.OpCode is var fieldOp &&
			(fieldOp == OpCodes.Ldsfld || fieldOp == OpCodes.Ldsflda ||
			 fieldOp == OpCodes.Stsfld) &&
			source.Operand is int fieldToken)
		{
			var field = module.ResolveFieldToken(
				fieldToken,
				sourceMethod,
				source.Offset);
			var memoryObject = StaticFieldObject(field);
			if (fieldOp == OpCodes.Ldsflda)
			{
				return NoneWith(
					reads: [memoryObject],
					escapes: [memoryObject]);
			}
			return fieldOp == OpCodes.Stsfld
				? NoneWith(writes: [memoryObject])
				: NoneWith(reads: [memoryObject]);
		}

		if (instruction.Operation == M68kMachineOperation.PlatformBaseLoad &&
			instruction.PlatformBaseConvention?.SlotSymbol is { } loadSlot)
		{
			return NoneWith(reads: [LibraryBaseObject(loadSlot)]);
		}
		if (instruction.Operation == M68kMachineOperation.PlatformBaseStore &&
			instruction.PlatformBaseConvention?.SlotSymbol is { } storeSlot)
		{
			return NoneWith(writes: [LibraryBaseObject(storeSlot)]);
		}

		if (instruction.Operation == M68kMachineOperation.Call &&
			source is { Operand: int token } call)
		{
			var target = module.ResolveMethodToken(
				token,
				sourceMethod,
				call.Offset);
			var name = target.ImportName;
			if (name?.StartsWith(LibraryBaseGetPrefix, StringComparison.Ordinal) == true)
			{
				return NoneWith(reads:
					[LibraryBaseObject(name[LibraryBaseGetPrefix.Length..])]);
			}
			if (name?.StartsWith(LibraryBaseSetPrefix, StringComparison.Ordinal) == true)
			{
				return NoneWith(writes:
					[LibraryBaseObject(name[LibraryBaseSetPrefix.Length..])]);
			}
			if (target.Definition?.ExternalCall is { } externalCall)
			{
				return M68kObjectMemoryEffect.None with
				{
					ReadsExact = externalCall.Convention.BaseSource ==
							M68kExternalBaseSource.WritableSlot &&
						externalCall.Convention.SlotSymbol is { } slotSymbol
							? ImmutableHashSet.Create(LibraryBaseObject(slotSymbol))
							: ImmutableHashSet<M68kMemoryObject>.Empty,
					WritesUnknown = true,
					ObservesRoots = instruction.IsSafepoint,
					MayTrap = instruction.MayThrow
				};
			}
			if (IsPureIdentityIntrinsic(name))
			{
				return M68kObjectMemoryEffect.None;
			}
		}

		if (TryGetFrameObject(instruction, out var frameObject, out var accessKind))
		{
			return accessKind switch
			{
				M68kExactMemoryAccessKind.Read => NoneWith(reads: [frameObject]),
				M68kExactMemoryAccessKind.Write => NoneWith(writes: [frameObject]),
				_ => NoneWith(reads: [frameObject], escapes: [frameObject])
			};
		}

		if (instruction.Operation is
			M68kMachineOperation.SpillLoad or
			M68kMachineOperation.SpillStore or
			M68kMachineOperation.SpillClear or
			M68kMachineOperation.RootStore or
			M68kMachineOperation.RootClear or
			M68kMachineOperation.ByrefOwnerKeepAlive or
			M68kMachineOperation.GcKeepAlive or
			M68kMachineOperation.OutgoingArgumentPush or
			M68kMachineOperation.IncomingArgumentPush or
			M68kMachineOperation.OutgoingArgumentCleanup)
		{
			return M68kObjectMemoryEffect.None;
		}

		return M68kObjectMemoryEffect.Unknown(instruction);
	}

	private static M68kObjectMemoryEffect FromExactAccesses(
		M68kMachineInstruction instruction)
	{
		var reads = instruction.ExactMemoryAccesses
			.Where(static access => access.Reads)
			.Select(static access => access.Object)
			.ToImmutableHashSet();
		var writes = instruction.ExactMemoryAccesses
			.Where(static access => access.Writes)
			.Select(static access => access.Object)
			.ToImmutableHashSet();
		var escapes = instruction.ExactMemoryAccesses
			.Where(static access => access.Escapes)
			.Select(static access => access.Object)
			.ToImmutableHashSet();
		return new M68kObjectMemoryEffect(
			reads,
			writes,
			escapes,
			ReadsUnknown: false,
			WritesUnknown: false,
			ObservesRoots: instruction.IsSafepoint,
			IsVolatile:
				(instruction.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0,
			MayTrap: instruction.MayThrow);
	}

	private static M68kObjectMemoryEffect NoneWith(
		IEnumerable<M68kMemoryObject>? reads = null,
		IEnumerable<M68kMemoryObject>? writes = null,
		IEnumerable<M68kMemoryObject>? escapes = null) =>
		M68kObjectMemoryEffect.None with
		{
			ReadsExact = reads?.ToImmutableHashSet() ??
				ImmutableHashSet<M68kMemoryObject>.Empty,
			WritesExact = writes?.ToImmutableHashSet() ??
				ImmutableHashSet<M68kMemoryObject>.Empty,
			EscapesExact = escapes?.ToImmutableHashSet() ??
				ImmutableHashSet<M68kMemoryObject>.Empty
		};

	private static bool TryGetFrameObject(
		M68kMachineInstruction instruction,
		out M68kMemoryObject memoryObject,
		out M68kExactMemoryAccessKind accessKind)
	{
		if (instruction.ArgumentIndex is not { } index)
		{
			memoryObject = default;
			accessKind = default;
			return false;
		}
		var kind = instruction.Operation switch
		{
			M68kMachineOperation.LocalLoad or
				M68kMachineOperation.LocalStore or
				M68kMachineOperation.LocalAddress => M68kMemoryObjectKind.FrameSlot,
			M68kMachineOperation.ArgumentLoad or
				M68kMachineOperation.ArgumentStore or
				M68kMachineOperation.ArgumentAddress => M68kMemoryObjectKind.ArgumentHome,
			_ => (M68kMemoryObjectKind?)null
		};
		if (kind is null)
		{
			memoryObject = default;
			accessKind = default;
			return false;
		}
		accessKind = instruction.Operation switch
		{
			M68kMachineOperation.LocalLoad or
				M68kMachineOperation.ArgumentLoad => M68kExactMemoryAccessKind.Read,
			M68kMachineOperation.LocalStore or
				M68kMachineOperation.ArgumentStore => M68kExactMemoryAccessKind.Write,
			_ => M68kExactMemoryAccessKind.Address
		};
		memoryObject = FrameObject(
			kind.Value,
			index,
			offset: accessKind == M68kExactMemoryAccessKind.Address
				? 0
				: instruction.MemoryOffset,
			size: accessKind == M68kExactMemoryAccessKind.Address
				? null
				: instruction.MemorySize > 0
					? instruction.MemorySize
					: null);
		return true;
	}

	private static bool IsPureIdentityIntrinsic(string? name) =>
		name is
			"intrinsic:object-ctor" or
			"intrinsic:cstring-from-pointer" or
			"intrinsic:cstring-to-uint32" or
			"intrinsic:aptr-from-pointer" or
			"intrinsic:aptr-to-uint32" or
			"intrinsic:amiga-vararg-from-value" or
			"intrinsic:address-of-ref" or
			"intrinsic:address-to-ref" or
			"intrinsic:ref-cast" or
			"intrinsic:hook-address-of" or
			"intrinsic:boopsi-message-address-of" or
			"intrinsic:aptr-null" or
			"intrinsic:cstring-from-literal" or
			"intrinsic:amiga-vararg-from-literal" or
			"intrinsic:aptr-export-address";
}
