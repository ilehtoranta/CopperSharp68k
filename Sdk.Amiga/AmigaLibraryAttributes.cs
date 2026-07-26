/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Sdk.Amiga;

public enum AmigaLibraryBasePolicy
{
	ExecBase,
	Manual,
	AutoOpen,
	Provided
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
