/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Big-endian guest-memory codecs for documented AHI record prefixes.</summary>
public static class AHIAudioCtrlCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => AhiGuestMemory.IsMapped(
		ref memory, address, AHIAudioCtrl.Size);

	public static AHIAudioCtrl Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
		{
			UserData = APTR.FromPointer(memory.ReadUInt32(address,
				AHILayout.AudioCtrl.UserData)),
		};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		AHIAudioCtrl value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, AHILayout.AudioCtrl.UserData, value.UserData.Raw);
}

public static class AHISoundMessageCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => AhiGuestMemory.IsMapped(
		ref memory, address, AHISoundMessage.Size);

	public static AHISoundMessage Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
		{
			Channel = memory.ReadUInt16(address, AHILayout.SoundMessage.Channel),
		};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		AHISoundMessage value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt16(address, AHILayout.SoundMessage.Channel, value.Channel);
}

public static class AHIRecordMessageCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => AhiGuestMemory.IsMapped(
		ref memory, address, AHIRecordMessage.Size);

	public static AHIRecordMessage Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
		{
			Type = memory.ReadUInt32(address, AHILayout.RecordMessage.Type),
			Buffer = APTR.FromPointer(memory.ReadUInt32(address,
				AHILayout.RecordMessage.Buffer)),
			Length = memory.ReadUInt32(address, AHILayout.RecordMessage.Length),
		};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		AHIRecordMessage value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, AHILayout.RecordMessage.Type, value.Type);
		memory.WriteUInt32(address, AHILayout.RecordMessage.Buffer, value.Buffer.Raw);
		memory.WriteUInt32(address, AHILayout.RecordMessage.Length, value.Length);
	}
}

public static class AHISampleInfoCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => AhiGuestMemory.IsMapped(
		ref memory, address, AHISampleInfo.Size);

	public static AHISampleInfo Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
		{
			Type = memory.ReadUInt32(address, AHILayout.SampleInfo.Type),
			Address = APTR.FromPointer(memory.ReadUInt32(address,
				AHILayout.SampleInfo.Address)),
			Length = memory.ReadUInt32(address, AHILayout.SampleInfo.Length),
		};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		AHISampleInfo value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, AHILayout.SampleInfo.Type, value.Type);
		memory.WriteUInt32(address, AHILayout.SampleInfo.Address, value.Address.Raw);
		memory.WriteUInt32(address, AHILayout.SampleInfo.Length, value.Length);
	}
}

public static class AHIAudioModeRequesterCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => AhiGuestMemory.IsMapped(
		ref memory, address, AHIAudioModeRequester.Size);

	public static AHIAudioModeRequester Read<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		AudioId = memory.ReadUInt32(address, AHILayout.AudioModeRequester.AudioId),
		MixFrequency = memory.ReadUInt32(address,
			AHILayout.AudioModeRequester.MixFrequency),
		LeftEdge = unchecked((short)memory.ReadUInt16(address,
			AHILayout.AudioModeRequester.LeftEdge)),
		TopEdge = unchecked((short)memory.ReadUInt16(address,
			AHILayout.AudioModeRequester.TopEdge)),
		Width = unchecked((short)memory.ReadUInt16(address,
			AHILayout.AudioModeRequester.Width)),
		Height = unchecked((short)memory.ReadUInt16(address,
			AHILayout.AudioModeRequester.Height)),
		InfoOpened = unchecked((int)memory.ReadUInt32(address,
			AHILayout.AudioModeRequester.InfoOpened)),
		InfoLeftEdge = unchecked((short)memory.ReadUInt16(address,
			AHILayout.AudioModeRequester.InfoLeftEdge)),
		InfoTopEdge = unchecked((short)memory.ReadUInt16(address,
			AHILayout.AudioModeRequester.InfoTopEdge)),
		InfoWidth = unchecked((short)memory.ReadUInt16(address,
			AHILayout.AudioModeRequester.InfoWidth)),
		InfoHeight = unchecked((short)memory.ReadUInt16(address,
			AHILayout.AudioModeRequester.InfoHeight)),
		UserData = APTR.FromPointer(memory.ReadUInt32(address,
			AHILayout.AudioModeRequester.UserData)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		AHIAudioModeRequester value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, AHILayout.AudioModeRequester.AudioId, value.AudioId);
		memory.WriteUInt32(address, AHILayout.AudioModeRequester.MixFrequency,
			value.MixFrequency);
		memory.WriteUInt16(address, AHILayout.AudioModeRequester.LeftEdge,
			unchecked((ushort)value.LeftEdge));
		memory.WriteUInt16(address, AHILayout.AudioModeRequester.TopEdge,
			unchecked((ushort)value.TopEdge));
		memory.WriteUInt16(address, AHILayout.AudioModeRequester.Width,
			unchecked((ushort)value.Width));
		memory.WriteUInt16(address, AHILayout.AudioModeRequester.Height,
			unchecked((ushort)value.Height));
		memory.WriteUInt32(address, AHILayout.AudioModeRequester.InfoOpened,
			unchecked((uint)value.InfoOpened));
		memory.WriteUInt16(address, AHILayout.AudioModeRequester.InfoLeftEdge,
			unchecked((ushort)value.InfoLeftEdge));
		memory.WriteUInt16(address, AHILayout.AudioModeRequester.InfoTopEdge,
			unchecked((ushort)value.InfoTopEdge));
		memory.WriteUInt16(address, AHILayout.AudioModeRequester.InfoWidth,
			unchecked((ushort)value.InfoWidth));
		memory.WriteUInt16(address, AHILayout.AudioModeRequester.InfoHeight,
			unchecked((ushort)value.InfoHeight));
		memory.WriteUInt32(address, AHILayout.AudioModeRequester.UserData,
			value.UserData.Raw);
	}
}

internal static class AhiGuestMemory
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address,
		uint size) where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		(address.Raw & 1) == 0 && address.Raw <= uint.MaxValue - size &&
		memory.IsMapped(address, size);
}
