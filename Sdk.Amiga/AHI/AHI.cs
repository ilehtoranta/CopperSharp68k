/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

/// <summary>AHI audio.device M68k vectors. The caller supplies the device base.</summary>
[AmigaLibrary(Name, AmigaLibraryBasePolicy.CallerProvided)]
public static class AHI
{
	public const string Name = "ahi.device";
	public const short AllocAudioALvo = -42;
	public const short FreeAudioLvo = -48;
	public const short KillAudioLvo = -54;
	public const short ControlAudioALvo = -60;
	public const short SetVolLvo = -66;
	public const short SetFreqLvo = -72;
	public const short SetSoundLvo = -78;
	public const short SetEffectLvo = -84;
	public const short LoadSoundLvo = -90;
	public const short UnloadSoundLvo = -96;
	public const short NextAudioIdLvo = -102;
	public const short GetAudioAttrsALvo = -108;
	public const short BestAudioIdALvo = -114;
	public const short AllocAudioRequestALvo = -120;
	public const short AudioRequestALvo = -126;
	public const short FreeAudioRequestLvo = -132;
	public const short PlayALvo = -138;
	public const short SampleFrameSizeLvo = -144;
	public const short AddAudioModeLvo = -150;
	public const short RemoveAudioModeLvo = -156;
	public const short LoadModeFileLvo = -162;

	[AmigaLvo(AllocAudioALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocAudioA(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A1)] APTR tagList);

	[AmigaLvo(FreeAudioLvo)]
	public static extern void FreeAudio(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A2)] APTR audioCtrl);

	[AmigaLvo(KillAudioLvo)]
	public static extern void KillAudio(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase);

	[AmigaLvo(ControlAudioALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ControlAudioA(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A2)] APTR audioCtrl,
		[M68kRegister(M68kRegister.A1)] APTR tagList);

	[AmigaLvo(SetVolLvo)]
	public static extern void SetVol(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.D0)] ushort channel,
		[M68kRegister(M68kRegister.D1)] int volume,
		[M68kRegister(M68kRegister.D2)] int position,
		[M68kRegister(M68kRegister.A2)] APTR audioCtrl,
		[M68kRegister(M68kRegister.D3)] uint flags);

	[AmigaLvo(SetFreqLvo)]
	public static extern void SetFreq(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.D0)] ushort channel,
		[M68kRegister(M68kRegister.D1)] uint frequency,
		[M68kRegister(M68kRegister.A2)] APTR audioCtrl,
		[M68kRegister(M68kRegister.D2)] uint flags);

	[AmigaLvo(SetSoundLvo)]
	public static extern void SetSound(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.D0)] ushort channel,
		[M68kRegister(M68kRegister.D1)] ushort sound,
		[M68kRegister(M68kRegister.D2)] uint offset,
		[M68kRegister(M68kRegister.D3)] int length,
		[M68kRegister(M68kRegister.A2)] APTR audioCtrl,
		[M68kRegister(M68kRegister.D4)] uint flags);

	[AmigaLvo(SetEffectLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetEffect(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A0)] APTR effect,
		[M68kRegister(M68kRegister.A2)] APTR audioCtrl);

	[AmigaLvo(LoadSoundLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LoadSound(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.D0)] ushort sound,
		[M68kRegister(M68kRegister.D1)] uint type,
		[M68kRegister(M68kRegister.A0)] APTR info,
		[M68kRegister(M68kRegister.A2)] APTR audioCtrl);

	[AmigaLvo(UnloadSoundLvo)]
	public static extern void UnloadSound(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.D0)] ushort sound,
		[M68kRegister(M68kRegister.A2)] APTR audioCtrl);

	[AmigaLvo(NextAudioIdLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextAudioId(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.D0)] uint lastId);

	[AmigaLvo(GetAudioAttrsALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetAudioAttrsA(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.D0)] uint audioId,
		[M68kRegister(M68kRegister.A2)] APTR audioCtrl,
		[M68kRegister(M68kRegister.A1)] APTR tagList);

	[AmigaLvo(BestAudioIdALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint BestAudioIdA(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A1)] APTR tagList);

	[AmigaLvo(AllocAudioRequestALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocAudioRequestA(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A0)] APTR tagList);

	[AmigaLvo(AudioRequestALvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AudioRequestA(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A0)] APTR requester,
		[M68kRegister(M68kRegister.A1)] APTR tagList);

	[AmigaLvo(FreeAudioRequestLvo)]
	public static extern void FreeAudioRequest(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A0)] APTR requester);

	/// <remarks>Requires AHI version 4 or newer.</remarks>
	[AmigaLvo(PlayALvo)]
	public static extern void PlayA(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A2)] APTR audioCtrl,
		[M68kRegister(M68kRegister.A1)] APTR tagList);

	/// <remarks>Requires AHI version 4 or newer.</remarks>
	[AmigaLvo(SampleFrameSizeLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SampleFrameSize(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.D0)] uint sampleType);

	/// <remarks>Requires AHI version 4 or newer.</remarks>
	[AmigaLvo(AddAudioModeLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AddAudioMode(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A0)] APTR tagList);

	/// <remarks>Requires AHI version 4 or newer.</remarks>
	[AmigaLvo(RemoveAudioModeLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint RemoveAudioMode(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.D0)] uint audioId);

	/// <remarks>Requires AHI version 4 or newer.</remarks>
	[AmigaLvo(LoadModeFileLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LoadModeFile(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A0)] CString name);
}
