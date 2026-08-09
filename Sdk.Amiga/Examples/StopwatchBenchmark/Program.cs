/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Diagnostics;
using CopperSharp.Compiler;

namespace StopwatchBenchmarkExample;

public static class Program
{
	private const int BufferLength = 512;
	private const uint ExpectedCrc32 = 0x003A981D;

	[M68kEntryPoint]
	public static int Main()
	{
		Console.WriteLine("CRC-32 benchmark");
		var started = (uint)Stopwatch.GetTimestamp();
		var checksum = ComputeCrc32();
		var elapsedTicks = (uint)Stopwatch.GetTimestamp() - started;

		Console.Write("Bytes processed: ");
		Console.WriteLine(BufferLength);
		Console.Write("CRC-32: ");
		Console.WriteLine(checksum);
		Console.Write("Elapsed ticks: ");
		Console.WriteLine(elapsedTicks);
		Console.Write("Ticks per second: ");
		Console.WriteLine(Stopwatch.Frequency);
		Console.Write("High-resolution timer: ");
		Console.WriteLine(Stopwatch.IsHighResolution);

		return checksum == ExpectedCrc32 ? 0 : 20;
	}

	private static uint ComputeCrc32()
	{
		var crc = uint.MaxValue;
		for (var index = 0; index < BufferLength; index++)
		{
			crc ^= (uint)(index ^ (index >> 3)) & 0xff;
			for (var bit = 0; bit < 8; bit++)
			{
				crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
			}
		}

		return ~crc;
	}
}
