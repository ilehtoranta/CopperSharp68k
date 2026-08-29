/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Typed big-endian guest-memory boundary for timer values.</summary>
public static class TimeValCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - TimeVal.Size &&
		memory.IsMapped(address, TimeVal.Size);

	public static TimeVal Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Seconds = memory.ReadUInt32(address,
			TimerDeviceLayout.TimeVal.Seconds),
		Microseconds = memory.ReadUInt32(address,
			TimerDeviceLayout.TimeVal.Microseconds),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		TimeVal value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, TimerDeviceLayout.TimeVal.Seconds,
			value.Seconds);
		memory.WriteUInt32(address, TimerDeviceLayout.TimeVal.Microseconds,
			value.Microseconds);
	}
}

/// <summary>
/// Typed big-endian guest-memory boundary for timer.device requests. Algorithms
/// use <see cref="TimerRequest"/> members; numeric ABI offsets remain here.
/// </summary>
public static class TimerRequestCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - TimerRequest.Size &&
		memory.IsMapped(address, TimerRequest.Size);

	public static TimerRequest Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Request = new IORequest
		{
			Message = new Message
			{
				Node = new Node
				{
					Successor = APTR.FromPointer(memory.ReadUInt32(address,
						ExecLayout.Node.Successor)),
					Predecessor = APTR.FromPointer(memory.ReadUInt32(address,
						ExecLayout.Node.Predecessor)),
					Type = memory.ReadUInt8(address, ExecLayout.Node.Type),
					Priority = unchecked((sbyte)memory.ReadUInt8(address,
						ExecLayout.Node.Priority)),
					Name = STRPTR.FromPointer(memory.ReadUInt32(address,
						ExecLayout.Node.Name)),
				},
				ReplyPort = APTR.FromPointer(memory.ReadUInt32(address,
					ExecLayout.Message.ReplyPort)),
				Length = memory.ReadUInt16(address, ExecLayout.Message.Length),
			},
			Device = APTR.FromPointer(memory.ReadUInt32(address,
				ExecLayout.IORequest.Device)),
			Unit = APTR.FromPointer(memory.ReadUInt32(address,
				ExecLayout.IORequest.Unit)),
			Command = (DeviceCommand)memory.ReadUInt16(address,
				ExecLayout.IORequest.Command),
			Flags = (IOFlags)memory.ReadUInt8(address, ExecLayout.IORequest.Flags),
			Error = unchecked((sbyte)memory.ReadUInt8(address,
				ExecLayout.IORequest.Error)),
		},
		Time = TimeValCodec.Read(ref memory, APTR.FromPointer(address.Raw +
			TimerDeviceLayout.TimerRequest.Time)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		TimerRequest value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, ExecLayout.Node.Successor,
			value.Request.Message.Node.Successor.Raw);
		memory.WriteUInt32(address, ExecLayout.Node.Predecessor,
			value.Request.Message.Node.Predecessor.Raw);
		memory.WriteUInt8(address, ExecLayout.Node.Type,
			value.Request.Message.Node.Type);
		memory.WriteUInt8(address, ExecLayout.Node.Priority,
			unchecked((byte)value.Request.Message.Node.Priority));
		memory.WriteUInt32(address, ExecLayout.Node.Name,
			value.Request.Message.Node.Name.Raw);
		memory.WriteUInt32(address, ExecLayout.Message.ReplyPort,
			value.Request.Message.ReplyPort.Raw);
		memory.WriteUInt16(address, ExecLayout.Message.Length,
			value.Request.Message.Length);
		memory.WriteUInt32(address, ExecLayout.IORequest.Device,
			value.Request.Device.Raw);
		memory.WriteUInt32(address, ExecLayout.IORequest.Unit,
			value.Request.Unit.Raw);
		memory.WriteUInt16(address, ExecLayout.IORequest.Command,
			(ushort)value.Request.Command);
		memory.WriteUInt8(address, ExecLayout.IORequest.Flags,
			(byte)value.Request.Flags);
		memory.WriteUInt8(address, ExecLayout.IORequest.Error,
			unchecked((byte)value.Request.Error));
		TimeValCodec.Write(ref memory, APTR.FromPointer(address.Raw +
			TimerDeviceLayout.TimerRequest.Time), value.Time);
	}

	public static APTR ReadDevice<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.IORequest.Device));
}
