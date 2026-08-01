/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

// AmiSSL is selected through amisslmaster.library. The process must open this
// optional library explicitly and call OpenAmiSSLTagList before using AmiSSL.
[AmigaLibrary(Name, AmigaLibraryBasePolicy.Manual)]
public static class AmiSSLMaster
{
	public const string Name = "amisslmaster.library";

	public static APTR AmiSSLMasterLibraryBase
	{
		get => throw new System.NotSupportedException(
			"AmiSSLMasterLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"AmiSSLMasterLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int InitAmiSSLMaster(
		[M68kRegister(M68kRegister.D0)] int apiVersion,
		[M68kRegister(M68kRegister.D1)] int usesOpenSslStructs);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenAmiSSL();

	[AmigaLvo(-42)]
	public static extern void CloseAmiSSL();

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenAmiSSLCipher(
		[M68kRegister(M68kRegister.D0)] int cipher);

	[AmigaLvo(-54)]
	public static extern void CloseAmiSSLCipher(
		[M68kRegister(M68kRegister.A0)] uint cipherBase);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int OpenAmiSSLTagList(
		[M68kRegister(M68kRegister.D0)] int apiVersion,
		[M68kRegister(M68kRegister.A0)] uint tags);
}
