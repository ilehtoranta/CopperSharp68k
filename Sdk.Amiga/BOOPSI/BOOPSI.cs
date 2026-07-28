/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

public static partial class BOOPSI
{
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
	public sealed class DispatcherAttribute : Attribute
	{
		public DispatcherAttribute(string? name = null) => Name = name;

		public string? Name { get; }
	}

	public static APTR InstanceData(APTR cl, APTR obj) =>
		throw new System.NotSupportedException(
			"BOOPSI.InstanceData is lowered by CopperSharp.");

	public static uint DoMethod(uint obj, uint methodId) =>
		throw new System.NotSupportedException("BOOPSI.DoMethod is lowered by CopperSharp.");

	public static uint DoMethod(uint obj, uint methodId, uint arg1) =>
		throw new System.NotSupportedException("BOOPSI.DoMethod is lowered by CopperSharp.");

	public static uint DoMethod(uint obj, uint methodId, uint arg1, uint arg2) =>
		throw new System.NotSupportedException("BOOPSI.DoMethod is lowered by CopperSharp.");

	public static uint DoMethod(uint obj, uint methodId, uint arg1, uint arg2, uint arg3) =>
		throw new System.NotSupportedException("BOOPSI.DoMethod is lowered by CopperSharp.");

	public static uint DoMethod(uint obj, uint methodId, uint arg1, uint arg2, uint arg3, uint arg4) =>
		throw new System.NotSupportedException("BOOPSI.DoMethod is lowered by CopperSharp.");

	public static uint DoMethod(uint obj, uint methodId, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5) =>
		throw new System.NotSupportedException("BOOPSI.DoMethod is lowered by CopperSharp.");

	public static uint DoMethod(uint obj, uint methodId, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, uint arg6) =>
		throw new System.NotSupportedException("BOOPSI.DoMethod is lowered by CopperSharp.");

	public static uint DoMethod(
		uint obj,
		[AmigaStackVarargs] params AmigaVarArg[] message) =>
		throw new System.NotSupportedException("BOOPSI.DoMethod is lowered by CopperSharp.");

	[M68kImport("amiga.boopsi.DoMethodA")]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DoMethodA(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint message);

	[M68kImport("amiga.boopsi.DoSuperMethodA")]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DoSuperMethodA(
		[M68kRegister(M68kRegister.A0)] uint cl,
		[M68kRegister(M68kRegister.A2)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint message);

	[M68kImport("amiga.boopsi.CoerceMethodA")]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CoerceMethodA(
		[M68kRegister(M68kRegister.A0)] uint cl,
		[M68kRegister(M68kRegister.A2)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint message);
}
