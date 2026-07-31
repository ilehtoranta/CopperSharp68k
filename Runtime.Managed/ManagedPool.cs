/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

public static class ManagedPool
{
	private const uint BlockHeaderSize = 16;
	private const uint AlignmentMask = 3;
	private const uint MinimumSplitSize = BlockHeaderSize + 4;
	private const int NextOffset = 0;
	private const int PreviousOffset = 4;
	private const int SizeOffset = 8;
	private const int FlagsOffset = 12;
	private const uint AllocatedFlag = 1;
	private const uint MarkFlag = 2;
	private const uint ScanFlag = 4;

	public static uint HeapStart;
	public static uint HeapEnd;
	public static uint FreeHead;
	public static uint AllocatedHead;
	public static uint StaleBytes;
	public static uint StaleBlocks;
	public static uint StaleBytesThreshold;
	public static uint StaleBlocksThreshold;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Initialize(M68kAddress config)
	{
		var heapStart = M68kAddress.ReadUInt32(config, 8);
		var heapSize = M68kAddress.ReadUInt32(config, 12);
		if (heapStart == 0 || heapSize == 0)
		{
			return 0;
		}

		HeapStart = heapStart;
		HeapEnd = heapStart + heapSize;
		FreeHead = heapStart;
		AllocatedHead = 0;
		StaleBytes = 0;
		StaleBlocks = 0;
		StaleBytesThreshold = M68kAddress.ReadUInt32(config, 16);
		StaleBlocksThreshold = M68kAddress.ReadUInt32(config, 20);

		var first = M68kAddress.FromUInt32(heapStart);
		M68kAddress.WriteUInt32(first, 0, 0);
		M68kAddress.WriteUInt32(first, 4, 0);
		M68kAddress.WriteUInt32(first, 8, heapSize);
		M68kAddress.WriteUInt32(first, 12, 0);
		return 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint GetAllocationSize(uint payloadSize) =>
		(payloadSize + AlignmentMask & ~AlignmentMask) + BlockHeaderSize;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Allocate(uint payloadSize)
	{
		var requiredSize = GetAllocationSize(payloadSize);
		var current = FreeHead;
		while (current != 0)
		{
			var block = M68kAddress.FromUInt32(current);
			var blockSize = M68kAddress.ReadUInt32(block, SizeOffset);
			if (blockSize >= requiredSize)
			{
				var remainder = blockSize - requiredSize;
				if (remainder >= MinimumSplitSize)
				{
					SplitFreeBlock(current, requiredSize, remainder);
					blockSize = requiredSize;
				}
				else
				{
					UnlinkFreeBlock(current);
				}

				LinkAllocatedBlock(current);
				StaleBytes += blockSize;
				StaleBlocks++;

				var payload = current + BlockHeaderSize;
				var clearAddress = payload;
				var clearBytes = blockSize - BlockHeaderSize;
				while (clearBytes != 0)
				{
					M68kAddress.WriteUInt32(
						M68kAddress.FromUInt32(clearAddress),
						0,
						0);
					clearAddress += 4;
					clearBytes -= 4;
				}
				return payload;
			}

			current = M68kAddress.ReadUInt32(block, NextOffset);
		}

		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Dispose(uint slotAddress)
	{
		var slot = M68kAddress.FromUInt32(slotAddress);
		var payload = M68kAddress.ReadUInt32(slot, 0);
		if (payload == 0)
		{
			return;
		}

		M68kAddress.WriteUInt32(slot, 0, 0);
		var block = payload - BlockHeaderSize;
		UnlinkAllocatedBlock(block);
		LinkFreeBlock(block);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Mark(uint payloadAddress)
	{
		if (payloadAddress < HeapStart + BlockHeaderSize ||
			payloadAddress >= HeapEnd)
		{
			return;
		}

		var block = M68kAddress.FromUInt32(payloadAddress - BlockHeaderSize);
		var flags = M68kAddress.ReadUInt32(block, FlagsOffset);
		if ((flags & AllocatedFlag) == 0 || (flags & MarkFlag) != 0)
		{
			return;
		}
		M68kAddress.WriteUInt32(block, FlagsOffset, flags | MarkFlag);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Collect()
	{
		uint scanned;
		do
		{
			scanned = 0;
			var current = AllocatedHead;
			while (current != 0)
			{
				var block = M68kAddress.FromUInt32(current);
				var next = M68kAddress.ReadUInt32(block, NextOffset);
				var flags = M68kAddress.ReadUInt32(block, FlagsOffset);
				if ((flags & MarkFlag) != 0 && (flags & ScanFlag) == 0)
				{
					M68kAddress.WriteUInt32(block, FlagsOffset, flags | ScanFlag);
					TraceObject(current + BlockHeaderSize);
					scanned = 1;
				}
				current = next;
			}
		}
		while (scanned != 0);

		var sweep = AllocatedHead;
		while (sweep != 0)
		{
			var block = M68kAddress.FromUInt32(sweep);
			var next = M68kAddress.ReadUInt32(block, NextOffset);
			var flags = M68kAddress.ReadUInt32(block, FlagsOffset);
			if ((flags & MarkFlag) == 0)
			{
				UnlinkAllocatedBlock(sweep);
				LinkFreeBlock(sweep);
			}
			else
			{
				M68kAddress.WriteUInt32(
					block,
					FlagsOffset,
					flags & ~(MarkFlag | ScanFlag));
			}
			sweep = next;
		}

		StaleBytes = 0;
		StaleBlocks = 0;
		Coalesce();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void MarkRoots(
		uint cursorAddress,
		uint resumePc,
		M68kAddress methodTable,
		M68kAddress staticRoots)
	{
		var methodTableAddress = M68kAddress.ToUInt32(methodTable);
		var cursor = cursorAddress;
		var currentPc = resumePc;
		while (cursor != 0)
		{
			var siteCount = M68kAddress.ReadUInt32(methodTable, 0);
			var siteAddress = methodTableAddress + 4;
			while (siteCount != 0 &&
				M68kAddress.ReadUInt32(M68kAddress.FromUInt32(siteAddress), 0) != currentPc)
			{
				siteAddress += 20;
				siteCount--;
			}
			if (siteCount == 0)
			{
				break;
			}

			var site = M68kAddress.FromUInt32(siteAddress);
			var descriptorAddress = M68kAddress.ReadUInt32(site, 4);
			var frameBase = cursor + M68kAddress.ReadUInt32(site, 12);
			var rootMapAddress = M68kAddress.ReadUInt32(site, 16);
			var rootCount = rootMapAddress == 0
				? 0
				: M68kAddress.ReadUInt32(M68kAddress.FromUInt32(rootMapAddress), 0);
			var rootOffsets = rootMapAddress + 4;
			while (rootCount != 0)
			{
				var offset = M68kAddress.ReadUInt32(
					M68kAddress.FromUInt32(rootOffsets),
					0);
				Mark(M68kAddress.ReadUInt32(
					M68kAddress.FromUInt32(unchecked(frameBase + offset)),
					0));
				rootOffsets += 4;
				rootCount--;
			}

			var descriptor = M68kAddress.FromUInt32(descriptorAddress);
			cursor = frameBase +
				M68kAddress.ReadUInt32(descriptor, 0) +
				M68kAddress.ReadUInt32(descriptor, 4);
			currentPc = M68kAddress.ReadUInt32(M68kAddress.FromUInt32(cursor), 0);
		}

		var staticRootsAddress = M68kAddress.ToUInt32(staticRoots);
		var staticRootCount = M68kAddress.ReadUInt32(staticRoots, 0);
		var rootAddress = staticRootsAddress + 4;
		while (staticRootCount != 0)
		{
			var slotAddress = M68kAddress.ReadUInt32(
				M68kAddress.FromUInt32(rootAddress),
				0);
			Mark(M68kAddress.ReadUInt32(
				M68kAddress.FromUInt32(slotAddress),
				0));
			rootAddress += 4;
			staticRootCount--;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void CollectWithRoots(
		uint cursorAddress,
		uint resumePc,
		M68kAddress methodTable,
		M68kAddress staticRoots)
	{
		MarkRoots(cursorAddress, resumePc, methodTable, staticRoots);
		Collect();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Coalesce()
	{
		var current = HeapStart;
		while (current != 0 && current < HeapEnd)
		{
			var block = M68kAddress.FromUInt32(current);
			var size = M68kAddress.ReadUInt32(block, SizeOffset);
			if (size == 0)
			{
				return;
			}

			if (M68kAddress.ReadUInt32(block, FlagsOffset) == 0)
			{
				var nextAddress = current + size;
				if (nextAddress < HeapEnd)
				{
					var next = M68kAddress.FromUInt32(nextAddress);
					if (M68kAddress.ReadUInt32(next, FlagsOffset) == 0)
					{
						var nextSize = M68kAddress.ReadUInt32(next, SizeOffset);
						UnlinkFreeBlock(nextAddress);
						M68kAddress.WriteUInt32(
							block,
							SizeOffset,
							size + nextSize);
						continue;
					}
				}
			}

			current += size;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint GetStaleBytes() => StaleBytes;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint GetStaleBlocks() => StaleBlocks;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Shutdown()
	{
		FreeHead = 0;
		AllocatedHead = 0;
		HeapStart = 0;
		HeapEnd = 0;
	}

	private static void SplitFreeBlock(uint blockAddress, uint requiredSize, uint remainder)
	{
		var block = M68kAddress.FromUInt32(blockAddress);
		var next = M68kAddress.ReadUInt32(block, NextOffset);
		var previous = M68kAddress.ReadUInt32(block, PreviousOffset);
		var splitAddress = blockAddress + requiredSize;
		var split = M68kAddress.FromUInt32(splitAddress);
		M68kAddress.WriteUInt32(split, NextOffset, next);
		M68kAddress.WriteUInt32(split, PreviousOffset, previous);
		M68kAddress.WriteUInt32(split, SizeOffset, remainder);
		M68kAddress.WriteUInt32(split, FlagsOffset, 0);
		if (next != 0)
		{
			M68kAddress.WriteUInt32(
				M68kAddress.FromUInt32(next),
				PreviousOffset,
				splitAddress);
		}
		if (previous == 0)
		{
			FreeHead = splitAddress;
		}
		else
		{
			M68kAddress.WriteUInt32(
				M68kAddress.FromUInt32(previous),
				NextOffset,
				splitAddress);
		}
		M68kAddress.WriteUInt32(block, SizeOffset, requiredSize);
	}

	private static void TraceObject(uint payloadAddress)
	{
		var payload = M68kAddress.FromUInt32(payloadAddress);
		var descriptorAddress = M68kAddress.ReadUInt32(payload, 0);
		if (descriptorAddress == 0)
		{
			return;
		}

		var descriptor = M68kAddress.FromUInt32(descriptorAddress);
		var fixedSize = M68kAddress.ReadUInt32(descriptor, 0);
		if (fixedSize == 0)
		{
			if (M68kAddress.ReadUInt32(descriptor, 4) == 0)
			{
				return;
			}

			var length = M68kAddress.ReadUInt32(payload, 8);
			var elementAddress = payloadAddress + 12;
			while (length != 0)
			{
				Mark(M68kAddress.ReadUInt32(
					M68kAddress.FromUInt32(elementAddress),
					0));
				elementAddress += 4;
				length--;
			}
			return;
		}

		var bitmap = M68kAddress.ReadUInt32(descriptor, 4);
		var fieldAddress = payloadAddress + 8;
		while (bitmap != 0)
		{
			if ((bitmap & 1) != 0)
			{
				Mark(M68kAddress.ReadUInt32(
					M68kAddress.FromUInt32(fieldAddress),
					0));
			}
			fieldAddress += 4;
			bitmap >>= 1;
		}
	}

	private static void UnlinkFreeBlock(uint blockAddress)
	{
		var block = M68kAddress.FromUInt32(blockAddress);
		var next = M68kAddress.ReadUInt32(block, NextOffset);
		var previous = M68kAddress.ReadUInt32(block, PreviousOffset);
		if (previous == 0)
		{
			FreeHead = next;
		}
		else
		{
			M68kAddress.WriteUInt32(
				M68kAddress.FromUInt32(previous),
				NextOffset,
				next);
		}
		if (next != 0)
		{
			M68kAddress.WriteUInt32(
				M68kAddress.FromUInt32(next),
				PreviousOffset,
				previous);
		}
	}

	private static void LinkAllocatedBlock(uint blockAddress)
	{
		var block = M68kAddress.FromUInt32(blockAddress);
		var previousHead = AllocatedHead;
		M68kAddress.WriteUInt32(block, NextOffset, previousHead);
		M68kAddress.WriteUInt32(block, PreviousOffset, 0);
		M68kAddress.WriteUInt32(block, FlagsOffset, AllocatedFlag);
		if (previousHead != 0)
		{
			M68kAddress.WriteUInt32(
				M68kAddress.FromUInt32(previousHead),
				PreviousOffset,
				blockAddress);
		}
		AllocatedHead = blockAddress;
	}

	private static void UnlinkAllocatedBlock(uint blockAddress)
	{
		var block = M68kAddress.FromUInt32(blockAddress);
		var next = M68kAddress.ReadUInt32(block, NextOffset);
		var previous = M68kAddress.ReadUInt32(block, PreviousOffset);
		if (previous == 0)
		{
			AllocatedHead = next;
		}
		else
		{
			M68kAddress.WriteUInt32(
				M68kAddress.FromUInt32(previous),
				NextOffset,
				next);
		}
		if (next != 0)
		{
			M68kAddress.WriteUInt32(
				M68kAddress.FromUInt32(next),
				PreviousOffset,
				previous);
		}
	}

	private static void LinkFreeBlock(uint blockAddress)
	{
		var block = M68kAddress.FromUInt32(blockAddress);
		var previousHead = FreeHead;
		M68kAddress.WriteUInt32(block, NextOffset, previousHead);
		M68kAddress.WriteUInt32(block, PreviousOffset, 0);
		M68kAddress.WriteUInt32(block, FlagsOffset, 0);
		if (previousHead != 0)
		{
			M68kAddress.WriteUInt32(
				M68kAddress.FromUInt32(previousHead),
				PreviousOffset,
				blockAddress);
		}
		FreeHead = blockAddress;
	}
}
