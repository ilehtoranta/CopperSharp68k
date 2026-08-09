/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed record ManagedLifecycleModule(
	string RequiredFrameworkFeature,
	CilMethod Initialize,
	CilMethod Shutdown)
{
	public IEnumerable<CilMethod> Methods
	{
		get
		{
			yield return Initialize;
			yield return Shutdown;
		}
	}
}
