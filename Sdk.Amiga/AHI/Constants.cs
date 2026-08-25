/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static class AhiConstants
{
	public const uint TagBase = 0x8000_0000;
	public const uint TagBaseR = TagBase | 0x0000_8000;
	public const uint InvalidAudioId = uint.MaxValue;
	public const uint DefaultAudioId = 0;
	public const uint LoopbackAudioId = 1;
	public const uint DefaultFrequency = 0;
	public const uint MixFrequency = uint.MaxValue;
	public const ushort NoSound = ushort.MaxValue;
	public const uint DefaultUnit = 0;
	public const uint NoUnit = 255;
	public const ushort Version4 = 4;
}

public enum AhiAudioId : uint
{
	Default = AhiConstants.DefaultAudioId,
	Loopback = AhiConstants.LoopbackAudioId,
	Invalid = AhiConstants.InvalidAudioId,
}

public enum AhiAllocationTag : uint
{
	AudioId = AhiConstants.TagBase + 1, MixFrequency, Channels, Sounds,
	SoundFunction, PlayerFunction, PlayerFrequency, MinPlayerFrequency,
	MaxPlayerFrequency, RecordFunction, UserData = AhiConstants.TagBase + 11,
}

public enum AhiPlayTag : uint
{
	BeginChannel = AhiConstants.TagBase + 40,
	EndChannel,
	Frequency = AhiConstants.TagBase + 50, Volume, Pan, Sound, Offset, Length,
	LoopFrequency = AhiConstants.TagBase + 60, LoopVolume, LoopPan, LoopSound,
	LoopOffset, LoopLength,
}

public enum AhiControlTag : uint
{
	Play = AhiConstants.TagBase + 80, Record, MonitorVolume,
	MonitorVolumeQuery, MixFrequencyQuery, InputGain, InputGainQuery,
	OutputVolume, OutputVolumeQuery, Input, InputQuery, Output, OutputQuery,
}

public enum AhiAudioAttributeTag : uint
{
	AudioId = AhiConstants.TagBase + 100,
	Driver = AhiConstants.TagBaseR + 101,
	Volume = AhiConstants.TagBase + 103, Panning, Stereo, HiFi, PingPong,
	Name = AhiConstants.TagBaseR + 109,
	Bits = AhiConstants.TagBase + 110, MaxChannels, MinMixFrequency,
	MaxMixFrequency, Record, Frequencies, FrequencyArgument, Frequency,
	Author, Copyright, Version, Annotation, BufferLength, IndexArgument, Index,
	Realtime, MaxPlaySamples, MaxRecordSamples = AhiConstants.TagBase + 127,
	FullDuplex = AhiConstants.TagBase + 129, MinMonitorVolume,
	MaxMonitorVolume, MinInputGain, MaxInputGain, MinOutputVolume,
	MaxOutputVolume, Inputs, InputArgument, Input, Outputs, OutputArgument,
	Output,
}

public enum AhiBestAudioIdTag : uint
{
	Dizzy = AhiConstants.TagBase + 190,
}

public enum AhiRequesterTag : uint
{
	Window = AhiConstants.TagBase + 200, Screen, PublicScreenName,
	PrivateIdcmp, IntuitionMessageFunction, SleepWindow, UserData,
	TextAttribute = AhiConstants.TagBase + 220, Locale, TitleText,
	PositiveText, NegativeText,
	InitialLeftEdge = AhiConstants.TagBase + 240, InitialTopEdge,
	InitialWidth, InitialHeight, InitialAudioId, InitialMixFrequency,
	InitialInfoOpened, InitialInfoLeftEdge, InitialInfoTopEdge,
	InitialInfoWidth, InitialInfoHeight,
	DoMixFrequency = AhiConstants.TagBase + 260, DoDefaultMode,
	FilterTags = AhiConstants.TagBase + 270, FilterFunction,
}

[System.Flags]
public enum AhiSetFlags : uint
{
	None = 0,
	Immediate = 1,
	NoDelay = 2,
}

[System.Flags]
public enum AhiEffectType : uint
{
	MasterVolume = 1,
	OutputBuffer = 2,
	DspMask = 3,
	DspEcho = 4,
	ChannelInfo = 5,
	Cancel = 0x8000_0000,
}

public enum AhiDspMaskValue : byte
{
	Wet = 0,
	Dry = 1,
}

public enum AhiSoundType : uint
{
	Sample = 0,
	DynamicSample = 1,
	Input = 1u << 29,
}

public enum AhiSampleType : uint
{
	Mono8Signed = 0,
	Mono16Signed = 1,
	Stereo8Signed = 2,
	Stereo16Signed = 3,
	Mono8UnsignedObsolete = 4,
	Mono32Signed = 8,
	Stereo32Signed = 10,
}

public enum AhiError : uint
{
	Ok = 0,
	NoMemory = 1,
	BadSoundType = 2,
	BadSampleType = 3,
	Aborted = 4,
	Unknown = 5,
	HalfDuplex = 6,
}

[System.Flags]
public enum AhiOpenFlags : uint
{
	NoModeScan = 1,
}
