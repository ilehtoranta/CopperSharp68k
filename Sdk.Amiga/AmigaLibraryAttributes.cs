/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Sdk.Amiga;

using CopperSharp.Compiler;

public enum AmigaLibraryBasePolicy
{
	ExecBase,
	Manual,
	AutoOpen,
	Provided,
	/// <summary>The call receives its library or device base in A6.</summary>
	CallerProvided
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method)]
public sealed class AmigaLibraryAttribute : Attribute
{
	public AmigaLibraryAttribute(string name)
		: this(name, AmigaLibraryBasePolicy.Manual)
	{
	}

	public AmigaLibraryAttribute(string name, AmigaLibraryBasePolicy basePolicy)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
		BasePolicy = basePolicy;
	}

	public string Name { get; }

	public AmigaLibraryBasePolicy BasePolicy { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class AmigaLvoAttribute : Attribute
{
	public AmigaLvoAttribute(int offset)
	{
		Offset = offset;
	}

	public int Offset { get; }
}

/// <summary>
/// Declares an external m68k subroutine whose absolute address is supplied in
/// one annotated address-register argument. The call is emitted as
/// <c>JSR (An)</c>; all arguments and the result still use their explicit
/// <see cref="M68kRegisterAttribute"/> mappings.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AmigaIndirectCallAttribute : Attribute
{
	public AmigaIndirectCallAttribute(M68kRegister targetRegister)
	{
		TargetRegister = targetRegister;
	}

	public M68kRegister TargetRegister { get; }
}
