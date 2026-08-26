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
	CilMethod MarkRootsExtended,
	CilMethod CollectWithRoots,
	CilMethod CollectWithRootsExtended,
	CilMethod Collect,
	CilMethod RegisterFinalizer,
	CilMethod SuppressFinalizer,
	CilMethod ReRegisterFinalizer,
	CilMethod CollectFinalizableWithRoots,
	CilMethod CollectFinalizableWithRootsExtended,
	CilMethod CollectFinalizable,
	CilMethod DrainFinalizers,
	CilMethod PrepareShutdownFinalizers,
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
	CilField StaleBlocksThreshold,
	CilField FinalizerDrainActive,
	CilField ActiveFinalizerObject,
	CilField ActiveFinalizerRemaining,
	CilField FinalizersCompleted)
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
			yield return MarkRootsExtended;
			yield return CollectWithRoots;
			yield return CollectWithRootsExtended;
			yield return Collect;
			yield return Coalesce;
			yield return GetStaleBytes;
			yield return GetStaleBlocks;
			yield return Shutdown;
		}
	}

	public IEnumerable<CilMethod> CoreMethods
	{
		get
		{
			foreach (var method in Methods)
			{
				if (method != MarkRootsExtended &&
					method != CollectWithRootsExtended)
				{
					yield return method;
				}
			}
		}
	}

	public IEnumerable<CilMethod> ExtendedRootWalkMethods
	{
		get
		{
			yield return MarkRootsExtended;
			yield return CollectWithRootsExtended;
		}
	}

	public IEnumerable<CilMethod> FinalizerMethods
	{
		get
		{
			yield return RegisterFinalizer;
			yield return SuppressFinalizer;
			yield return ReRegisterFinalizer;
			yield return CollectFinalizableWithRoots;
			yield return CollectFinalizableWithRootsExtended;
			yield return CollectFinalizable;
			yield return DrainFinalizers;
			yield return PrepareShutdownFinalizers;
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

	public IEnumerable<CilField> FinalizerFields
	{
		get
		{
			yield return FinalizerDrainActive;
			yield return ActiveFinalizerObject;
			yield return ActiveFinalizerRemaining;
			yield return FinalizersCompleted;
		}
	}
}
