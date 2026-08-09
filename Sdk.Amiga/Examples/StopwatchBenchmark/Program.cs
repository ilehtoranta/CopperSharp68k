/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Diagnostics;
using CopperSharp.Compiler;

namespace StopwatchBenchmarkExample;

public static class Program
{
	private const int PrimeLimit = 10000;

	[M68kEntryPoint]
	public static int Main()
	{
		var started = (uint)Stopwatch.GetTimestamp();
		var primeCount = CountPrimes();
		var elapsedTicks = (uint)Stopwatch.GetTimestamp() - started;

		Console.WriteLine("Prime search benchmark");
		Console.Write("Primes up to 10000: ");
		Console.WriteLine(primeCount);
		Console.Write("Elapsed ticks: ");
		Console.WriteLine(elapsedTicks);
		Console.Write("Ticks per second: ");
		Console.WriteLine(Stopwatch.Frequency);
		Console.Write("High-resolution timer: ");
		Console.WriteLine(Stopwatch.IsHighResolution);

		return primeCount == 1229 ? 0 : 5;
	}

	private static int CountPrimes()
	{
		var count = 1;
		for (var candidate = 3; candidate <= PrimeLimit; candidate += 2)
		{
			if (IsPrime(candidate))
			{
				count++;
			}
		}

		return count;
	}

	private static bool IsPrime(int candidate)
	{
		for (var divisor = 3; divisor <= candidate / divisor; divisor += 2)
		{
			if (candidate % divisor == 0)
			{
				return false;
			}
		}

		return true;
	}
}
