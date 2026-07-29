/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed record ManagedPoolRuntimeModule(
	CilMethod Initialize,
	CilMethod GetAllocationSize,
	CilMethod Allocate,
	CilMethod Dispose,
	CilMethod Mark,
	CilMethod MarkRoots,
	CilMethod CollectWithRoots,
	CilMethod Collect,
	CilMethod Coalesce,
	CilMethod GetStaleBytes,
	CilMethod GetStaleBlocks,
	CilMethod Shutdown,
	CilField HeapStart,
	CilField HeapEnd,
	CilField FreeHead,
	CilField AllocatedHead,
	CilField StaleBytes,
	CilField StaleBlocks,
	CilField StaleBytesThreshold,
	CilField StaleBlocksThreshold)
{
	public IEnumerable<CilMethod> Methods
	{
		get
		{
			yield return Initialize;
			yield return GetAllocationSize;
			yield return Allocate;
			yield return Dispose;
			yield return Mark;
			yield return MarkRoots;
			yield return CollectWithRoots;
			yield return Collect;
			yield return Coalesce;
			yield return GetStaleBytes;
			yield return GetStaleBlocks;
			yield return Shutdown;
		}
	}

	public IEnumerable<CilField> Fields
	{
		get
		{
			yield return HeapStart;
			yield return HeapEnd;
			yield return FreeHead;
			yield return AllocatedHead;
			yield return StaleBytes;
			yield return StaleBlocks;
			yield return StaleBytesThreshold;
			yield return StaleBlocksThreshold;
		}
	}
}
