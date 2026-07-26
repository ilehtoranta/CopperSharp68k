/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;

namespace CopperSharp.Compiler.Output;

internal static class KickstartRomWriter
{
	private const int ChecksumDistanceFromEnd = 20;

	public static byte[] Write(
		ReadOnlySpan<byte> code,
		uint entryPoint,
		KickstartRomOutputOptions options)
	{
		ValidateOptions(options);
		var checksumOffset = options.Size - ChecksumDistanceFromEnd;
		if (code.Length > checksumOffset - 8)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.ImageOverflow,
				$"Linked code requires {code.Length} bytes but ROM has only {checksumOffset - 8} code bytes.");
		}

		var image = GC.AllocateUninitializedArray<byte>(options.Size);
		image.AsSpan().Fill(options.FillByte);
		BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0, 4), options.InitialStackPointer);
		BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(4, 4), entryPoint);
		code.CopyTo(image.AsSpan(8));
		BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(checksumOffset, 4), 0);
		var checksum = ~ComputeEndAroundCarrySum(image);
		BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(checksumOffset, 4), checksum);

		if (ComputeEndAroundCarrySum(image) != uint.MaxValue)
		{
			throw new InvalidOperationException("Kickstart checksum construction failed.");
		}

		return image;
	}

	public static uint GetBaseAddress(KickstartRomOutputOptions options)
	{
		ValidateOptions(options);
		return options.BaseAddress != 0
			? options.BaseAddress
			: checked(0x0100_0000u - (uint)options.Size);
	}

	internal static uint ComputeEndAroundCarrySum(ReadOnlySpan<byte> image)
	{
		if ((image.Length & 3) != 0)
		{
			throw new ArgumentException("ROM length must be a multiple of four.", nameof(image));
		}

		var sum = 0u;
		for (var offset = 0; offset < image.Length; offset += 4)
		{
			var value = BinaryPrimitives.ReadUInt32BigEndian(image[offset..]);
			var previous = sum;
			sum += value;
			if (sum < previous)
			{
				sum++;
			}
		}

		return sum;
	}

	private static void ValidateOptions(KickstartRomOutputOptions options)
	{
		if (options.Size is not (256 * 1024) and not (512 * 1024))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				"Kickstart ROM size must be 256 KiB or 512 KiB.");
		}

		var baseAddress = options.BaseAddress != 0
			? options.BaseAddress
			: checked(0x0100_0000u - (uint)options.Size);
		if ((baseAddress & 1) != 0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				"Kickstart ROM base address must be word aligned.");
		}

		if ((options.InitialStackPointer & 1) != 0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				"Initial stack pointer must be word aligned.");
		}

		if ((ulong)baseAddress + (uint)options.Size > uint.MaxValue + 1ul)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				"Kickstart ROM address range wraps the 32-bit address space.");
		}
	}
}
