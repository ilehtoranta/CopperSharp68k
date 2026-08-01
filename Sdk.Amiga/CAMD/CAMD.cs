/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

// camd.library is an optional MIDI framework. It is not a Kickstart library
// and must be opened explicitly when MIDI support is available.
[AmigaLibrary(Name, AmigaLibraryBasePolicy.Manual)]
public static class CAMD
{
	public const string Name = "camd.library";

	public static APTR CAMDLibraryBase
	{
		get => throw new System.NotSupportedException(
			"CAMDLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"CAMDLibraryBase is lowered by CopperSharp.");
	}

	public const uint CDLinkages = 0;
	public const uint MidiLinkReceiver = 0;
	public const uint MidiLinkSender = 1;

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint Lock(
		[M68kRegister(M68kRegister.D0)] uint lockType);

	[AmigaLvo(-36)]
	public static extern void Unlock(
		[M68kRegister(M68kRegister.A0)] uint lockToken);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateMidiA(
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-48)]
	public static extern void DeleteMidi(
		[M68kRegister(M68kRegister.A0)] uint midiNode);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetMidiAttrsA(
		[M68kRegister(M68kRegister.A0)] uint midiNode,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetMidiAttrsA(
		[M68kRegister(M68kRegister.A0)] uint midiNode,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextMidi(
		[M68kRegister(M68kRegister.A0)] uint midiNode);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindMidi(
		[M68kRegister(M68kRegister.A1)] CString name);

	[AmigaLvo(-78)]
	public static extern void FlushMidi(
		[M68kRegister(M68kRegister.A0)] uint midiNode);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AddMidiLinkA(
		[M68kRegister(M68kRegister.A0)] uint midiNode,
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-90)]
	public static extern void RemoveMidiLink(
		[M68kRegister(M68kRegister.A0)] uint midiLink);

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetMidiLinkAttrsA(
		[M68kRegister(M68kRegister.A0)] uint midiLink,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetMidiLinkAttrsA(
		[M68kRegister(M68kRegister.A0)] uint midiLink,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextClusterLink(
		[M68kRegister(M68kRegister.A0)] uint cluster,
		[M68kRegister(M68kRegister.A1)] uint midiLink,
		[M68kRegister(M68kRegister.D0)] int type);

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextMidiLink(
		[M68kRegister(M68kRegister.A0)] uint midiNode,
		[M68kRegister(M68kRegister.A1)] uint midiLink,
		[M68kRegister(M68kRegister.D0)] int type);

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MidiLinkConnected(
		[M68kRegister(M68kRegister.A0)] uint midiLink);

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextCluster(
		[M68kRegister(M68kRegister.A0)] uint lastCluster);

	[AmigaLvo(-132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint FindCluster(
		[M68kRegister(M68kRegister.A0)] CString name);

	[AmigaLvo(-138)]
	public static extern void PutMidi(
		[M68kRegister(M68kRegister.A0)] uint midiLink,
		[M68kRegister(M68kRegister.D0)] uint message);

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetMidi(
		[M68kRegister(M68kRegister.A0)] uint midiNode,
		[M68kRegister(M68kRegister.A1)] uint message);

	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WaitMidi(
		[M68kRegister(M68kRegister.A0)] uint midiNode,
		[M68kRegister(M68kRegister.A1)] uint message);

	[AmigaLvo(-156)]
	public static extern void PutSysEx(
		[M68kRegister(M68kRegister.A0)] uint midiLink,
		[M68kRegister(M68kRegister.A1)] uint buffer);

	[AmigaLvo(-162)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetSysEx(
		[M68kRegister(M68kRegister.A0)] uint midiNode,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D0)] uint length);

	[AmigaLvo(-168)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint QuerySysEx(
		[M68kRegister(M68kRegister.A0)] uint midiNode);

	[AmigaLvo(-174)]
	public static extern void SkipSysEx(
		[M68kRegister(M68kRegister.A0)] uint midiNode);

	[AmigaLvo(-180)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern byte GetMidiErr(
		[M68kRegister(M68kRegister.A0)] uint midiNode);

	[AmigaLvo(-186)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern short MidiMsgType(
		[M68kRegister(M68kRegister.A0)] uint message);

	[AmigaLvo(-192)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern short MidiMsgLen(
		[M68kRegister(M68kRegister.D0)] uint message);

	[AmigaLvo(-198)]
	public static extern void ParseMidi(
		[M68kRegister(M68kRegister.A0)] uint midiLink,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D0)] uint length);

	[AmigaLvo(-204)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenMidiDevice(
		[M68kRegister(M68kRegister.A0)] CString name);

	[AmigaLvo(-210)]
	public static extern void CloseMidiDevice(
		[M68kRegister(M68kRegister.A0)] uint deviceData);

	[AmigaLvo(-216)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Rethink();

	[AmigaLvo(-222)]
	public static extern void StartClusterNotify(
		[M68kRegister(M68kRegister.A0)] uint notifyNode);

	[AmigaLvo(-228)]
	public static extern void EndClusterNotify(
		[M68kRegister(M68kRegister.A0)] uint notifyNode);

	[AmigaLvo(-234)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GoodPutMidi(
		[M68kRegister(M68kRegister.A0)] uint midiLink,
		[M68kRegister(M68kRegister.D0)] uint message,
		[M68kRegister(M68kRegister.D1)] uint maximumBuffer);

	[AmigaLvo(-240)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Midi2Driver(
		[M68kRegister(M68kRegister.A0)] uint driverData,
		[M68kRegister(M68kRegister.D0)] uint message,
		[M68kRegister(M68kRegister.D1)] uint maximumBuffer);
}
