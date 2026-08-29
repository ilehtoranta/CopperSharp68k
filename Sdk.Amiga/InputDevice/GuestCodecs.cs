/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Typed big-endian guest-memory boundary for input events.</summary>
public static class InputEventCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - InputEvent.Size &&
		memory.IsMapped(address, InputEvent.Size);

	public static InputEvent Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		NextEvent = APTR.FromPointer(memory.ReadUInt32(address,
			InputEventLayout.NextEvent)),
		Class = (InputEventClass)memory.ReadUInt8(address,
			InputEventLayout.Class),
		SubClass = (InputEventSubClass)memory.ReadUInt8(address,
			InputEventLayout.SubClass),
		Code = memory.ReadUInt16(address, InputEventLayout.Code),
		Qualifier = (InputEventQualifier)memory.ReadUInt16(address,
			InputEventLayout.Qualifier),
		Position = unchecked((int)memory.ReadUInt32(address,
			InputEventLayout.Position)),
		TimeStamp = TimeValCodec.Read(ref memory, APTR.FromPointer(
			address.Raw + (uint)InputEventLayout.TimeStamp)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		InputEvent value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, InputEventLayout.NextEvent,
			value.NextEvent.Raw);
		memory.WriteUInt8(address, InputEventLayout.Class, (byte)value.Class);
		memory.WriteUInt8(address, InputEventLayout.SubClass,
			(byte)value.SubClass);
		memory.WriteUInt16(address, InputEventLayout.Code, value.Code);
		memory.WriteUInt16(address, InputEventLayout.Qualifier,
			(ushort)value.Qualifier);
		memory.WriteUInt32(address, InputEventLayout.Position,
			unchecked((uint)value.Position));
		TimeValCodec.Write(ref memory, APTR.FromPointer(
			address.Raw + (uint)InputEventLayout.TimeStamp), value.TimeStamp);
	}
}
