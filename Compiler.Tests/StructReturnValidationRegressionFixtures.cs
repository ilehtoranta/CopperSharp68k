/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

/// <summary>
/// Minimal form of CopperStart.Dos.DosVariableCore.Valid: a generic guest-memory
/// reader returns a packed five-longword record which the caller validates from
/// a local. The managed and native entries deliberately exercise the same core.
/// </summary>
public static class StructReturnValidationRegressionFixtures
{
	private const uint AllocationAddress = 0x0000_3000;
	private const uint StateAddress = 0x0000_4000;
	private const uint Magic = 0x4C56_4152; // LVAR
	private const uint AllocationSize = 66;
	private const uint Generation = 7;
	private const uint VariableOffset = 24;
	private const uint NameOffset = 48;
	private const uint AptrPayload = 0x0000_1234;
	private const uint BptrPayload = 0x0000_0456;
	private const uint StrptrPayload = 0x0000_6789;
	private const uint ConstStrptrPayload = 0x0000_89AB;

	private interface IMemory : IAmigaGuestMemory
	{
		new byte ReadUInt8(APTR address, int offset = 0);
		new ushort ReadUInt16(APTR address, int offset = 0);
		new uint ReadUInt32(APTR address, int offset = 0);
		new void WriteUInt8(APTR address, int offset, byte value);
		new void WriteUInt16(APTR address, int offset, ushort value);
		new void WriteUInt32(APTR address, int offset, uint value);
		new void Clear(APTR address, uint byteCount);
		new void Copy(APTR source, APTR destination, uint byteCount);
		new bool IsMapped(APTR address, uint byteSize);
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 4)]
	private readonly struct NativeMemory : IMemory
	{
		private readonly uint _reserved;

		public NativeMemory(uint reserved) => _reserved = reserved;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsMapped(APTR address, uint size) => address.IsNotNull &&
			address.Raw <= 0x0000_FFFFu - size;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint ReadUInt32(APTR address, int offset) =>
			APTR.ReadUInt32(address, offset);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ushort ReadUInt16(APTR address, int offset = 0) =>
			APTR.ReadUInt16(address, offset);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public byte ReadUInt8(APTR address, int offset = 0) =>
			APTR.ReadUInt8(address, offset);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteUInt32(APTR address, int offset, uint value) =>
			APTR.WriteUInt32(address, offset, value);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteUInt16(APTR address, int offset, ushort value) =>
			APTR.WriteUInt16(address, offset, value);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteUInt8(APTR address, int offset, byte value) =>
			APTR.WriteUInt8(address, offset, value);

		public void Clear(APTR address, uint byteCount)
		{
			for (var offset = 0u; offset < byteCount; offset++)
				APTR.WriteUInt8(address, unchecked((int)offset), 0);
		}

		public void Copy(APTR source, APTR destination, uint byteCount)
		{
			for (var offset = 0u; offset < byteCount; offset++)
				APTR.WriteUInt8(destination, unchecked((int)offset),
					APTR.ReadUInt8(source, unchecked((int)offset)));
		}
	}

	private struct ManagedMemory : IMemory
	{
		private readonly byte[] _bytes;

		public ManagedMemory(int size) => _bytes = new byte[size];

		public readonly bool IsMapped(APTR address, uint size) =>
			address.Raw <= (uint)_bytes.Length - size;

		public readonly uint ReadUInt32(APTR address, int offset)
		{
			var index = checked((int)address.Raw + offset);
			return ((uint)_bytes[index] << 24) |
				((uint)_bytes[index + 1] << 16) |
				((uint)_bytes[index + 2] << 8) |
				_bytes[index + 3];
		}

		public readonly ushort ReadUInt16(APTR address, int offset = 0)
		{
			var index = checked((int)address.Raw + offset);
			return unchecked((ushort)((_bytes[index] << 8) | _bytes[index + 1]));
		}

		public readonly byte ReadUInt8(APTR address, int offset = 0) =>
			_bytes[checked((int)address.Raw + offset)];

		public readonly void WriteUInt32(APTR address, int offset, uint value)
		{
			var index = checked((int)address.Raw + offset);
			_bytes[index] = unchecked((byte)(value >> 24));
			_bytes[index + 1] = unchecked((byte)(value >> 16));
			_bytes[index + 2] = unchecked((byte)(value >> 8));
			_bytes[index + 3] = unchecked((byte)value);
		}

		public readonly void WriteUInt16(APTR address, int offset, ushort value)
		{
			var index = checked((int)address.Raw + offset);
			_bytes[index] = unchecked((byte)(value >> 8));
			_bytes[index + 1] = unchecked((byte)value);
		}

		public readonly void WriteUInt8(APTR address, int offset, byte value) =>
			_bytes[checked((int)address.Raw + offset)] = value;

		public readonly void Clear(APTR address, uint byteCount) =>
			Array.Clear(_bytes, checked((int)address.Raw), checked((int)byteCount));

		public readonly void Copy(APTR source, APTR destination, uint byteCount) =>
			Array.Copy(_bytes, checked((int)source.Raw), _bytes,
				checked((int)destination.Raw), checked((int)byteCount));
	}

	[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 20)]
	private struct Header
	{
		public const uint Size = 20;

		public uint Magic;
		public APTR Owner;
		public uint AllocationSize;
		public uint Generation;
		public APTR Next;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 16)]
	private struct PointerFamily
	{
		public APTR Aptr;
		public BPTR Bptr;
		public STRPTR Strptr;
		public CONST_STRPTR ConstStrptr;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NativeEntry()
	{
		var memory = new NativeMemory(0);
		Initialize(ref memory);
		return Valid(ref memory, APTR.FromPointer(StateAddress),
			APTR.FromPointer(AllocationAddress)) ? 42u : 0u;
	}

	public static uint ManagedEntry()
	{
		var memory = new ManagedMemory(0x1_0000);
		Initialize(ref memory);
		return Valid(ref memory, APTR.FromPointer(StateAddress),
			APTR.FromPointer(AllocationAddress)) ? 42u : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NativeNestedPointerFamilyEntry() =>
		ValidatePointerFamily(CreatePointerFamily());

	public static uint ManagedNestedPointerFamilyEntry() =>
		ValidatePointerFamily(CreatePointerFamily());

	private static PointerFamily CreatePointerFamily() => new()
	{
		Aptr = APTR.FromPointer(AptrPayload),
		Bptr = BPTR.FromRaw(BptrPayload),
		Strptr = STRPTR.FromPointer(StrptrPayload),
		ConstStrptr = CONST_STRPTR.FromPointer(ConstStrptrPayload),
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ValidatePointerFamily(PointerFamily pointers)
	{
		var result = 0u;
		if (pointers.Aptr.Raw == AptrPayload) result |= 1u;
		if (pointers.Bptr.Address.Raw == BptrPayload << 2) result |= 2u;
		if (pointers.Strptr.Address.Raw == StrptrPayload) result |= 4u;
		if (pointers.ConstStrptr.Address.Raw == ConstStrptrPayload) result |= 8u;
		if (APTR.ToUInt32(pointers.Aptr) == AptrPayload) result |= 16u;
		if (BPTR.ToAddress(pointers.Bptr).Raw == BptrPayload << 2) result |= 32u;
		if (STRPTR.ToAddress(pointers.Strptr).Raw == StrptrPayload) result |= 64u;
		if (CONST_STRPTR.ToAddress(pointers.ConstStrptr).Raw ==
			ConstStrptrPayload) result |= 128u;
		return result;
	}

	private static void Initialize<TMemory>(ref TMemory memory)
		where TMemory : struct, IMemory
	{
		var allocation = APTR.FromPointer(AllocationAddress);
		memory.WriteUInt32(allocation, 0, Magic);
		memory.WriteUInt32(allocation, 4, StateAddress);
		memory.WriteUInt32(allocation, 8, AllocationSize);
		memory.WriteUInt32(allocation, 12, Generation);
		memory.WriteUInt32(allocation, 16, 0);
		var name = APTR.FromPointer(AllocationAddress + NameOffset);
		DosLocalVarCodec.Write(ref memory,
			APTR.FromPointer(AllocationAddress + VariableOffset), new LocalVar
			{
				Node = new Node
				{
					Name = STRPTR.FromPointer(name.Raw),
				},
			});
	}

	private static Header Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IMemory => new()
	{
		Magic = memory.ReadUInt32(address, 0),
		Owner = APTR.FromPointer(memory.ReadUInt32(address, 4)),
		AllocationSize = memory.ReadUInt32(address, 8),
		Generation = memory.ReadUInt32(address, 12),
		Next = APTR.FromPointer(memory.ReadUInt32(address, 16)),
	};

	// Regression anchor (the offsets are in this method's CIL):
	//   IL_00C3 ldloca variable; IL_00C5 ldflda LocalVar.Node;
	//   IL_00CA ldflda Node.Name; IL_00CF call STRPTR.get_Address.
	// DosVariableCore.Valid has the same sequence at IL_00B7..IL_00C3.
	// Before the compiler fix the M68000 tail was:
	//   lea 20(a7),a0; lea (a0),a0; lea 16(a0),a0; move.l a0,d0
	// It returned the address of the embedded STRPTR slot. The instance getter
	// must load that slot once; the static ToAddress conversion remains identity.
	private static bool Valid<TMemory>(ref TMemory memory, APTR state,
		APTR allocation) where TMemory : struct, IMemory
	{
		if ((allocation.Raw & 3u) != 0 ||
			!memory.IsMapped(allocation, Header.Size)) return false;
		var header = Read(ref memory, allocation);
		if (header.Magic != Magic || header.Owner != state ||
			header.Generation != Generation ||
			header.AllocationSize < Header.Size + 2u ||
			allocation.Raw > uint.MaxValue - header.AllocationSize ||
			!memory.IsMapped(allocation, header.AllocationSize)) return false;
		var variable = DosLocalVarCodec.Read(ref memory,
			APTR.FromPointer(allocation.Raw + VariableOffset));
		var name = APTR.FromPointer(allocation.Raw + NameOffset);
		return variable.Node.Name.Address == name;
	}
}
