/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum MidiLinkFlags : byte
{
	Sender = 1,
	ParticipantChange = 2,
	Private = 4,
	Device = 8,
}

[System.Flags]
public enum MidiClientType : uint
{
	Sequencer = 1u << 0,
	SampleEditor = 1u << 1,
	PatchEditor = 1u << 2,
	Notator = 1u << 3,
	EventProcessor = 1u << 4,
	EventFilter = 1u << 5,
	EventRouter = 1u << 6,
	ToneGenerator = 1u << 7,
	EventGenerator = 1u << 8,
	GraphicAnimator = 1u << 9,
}

[System.Flags]
public enum MidiEventMask : uint
{
	Note = 1u << 0,
	Program = 1u << 1,
	PitchBend = 1u << 2,
	Control = 0x000001f8,
	Mode = 1u << 9,
	ChannelPressure = 1u << 10,
	PolyPressure = 1u << 11,
	Realtime = 1u << 12,
	SystemCommon = 1u << 13,
	SystemExclusive = 1u << 14,
	Channel = 0x00000fff,
	All = 0x00007fff,
}

public enum MidiMessageMethod : uint
{
	Receive = 0,
	Link = 1,
}

public enum MidiLinkAction : uint
{
	Link = 0,
	Unlink = 1,
}

public enum MidiError : int
{
	NoMemory = 801,
	NoSignals = 802,
	NoTimer = 803,
	BadPreferences = 804,
}

public enum MidiStatus : byte
{
	NoteOff = 0x80,
	NoteOn = 0x90,
	PolyPressure = 0xa0,
	Control = 0xb0,
	Program = 0xc0,
	ChannelPressure = 0xd0,
	PitchBend = 0xe0,
	System = 0xf0,
	Realtime = 0xf8,
}
