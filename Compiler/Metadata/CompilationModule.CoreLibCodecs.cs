/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Metadata;

internal sealed partial class CompilationModule
{
	internal bool TryCreatePinnedCodecBinding(FrameworkMemberId member, out FrameworkBinding binding)
	{
		binding = null!;
		return FrameworkImplementationPack is { } catalog && CoreLibCodecSurface.TryCreateBinding(member, catalog, out binding);
	}
}
