/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Compiler.Tests;

public sealed class RomAssemblySectionTests
{
	private static uint _value;
	private struct Pair
	{
		public int Left;
		public int Right;
	}

	public static uint IncrementValue()
	{
		_value++;
		return _value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int SumPair(ref Pair value) => value.Left + value.Right;

	public static int ByRefEntry()
	{
		var value = new Pair { Left = 17, Right = 25 };
		return SumPair(ref value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int DivideByTen(int value) => value / 10;

	public static int ConstantDivisionEntry() => DivideByTen(420);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int GuardedDivisionCore(int value, int divisor) =>
		divisor == 0 ? 0 : value / divisor;

	public static int GuardedDivisionEntry() => GuardedDivisionCore(420, 10);

	[Fact]
	public void AssemblyPlacesZeroInitializedStaticsInBss()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(RomAssemblySectionTests).Assembly.Location,
			EntryPoint = $"{typeof(RomAssemblySectionTests).FullName}::{nameof(IncrementValue)}",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			ExceptionMode = M68kExceptionMode.Yolo
		});

		var assembly = Assert.IsType<string>(result.Text);
		var bssSection = assembly.IndexOf("section\tram_bss,bss", StringComparison.Ordinal);
		var staticLabel = assembly.LastIndexOf("C68K_static_003A", StringComparison.Ordinal);
		Assert.True(bssSection >= 0);
		Assert.True(staticLabel > bssSection);
		Assert.Contains("\tds.b\t4", assembly, StringComparison.Ordinal);

		var metrics = Regex.Match(
			result.Map,
			@"code-bytes=(?<code>\d+).*rom-bytes=(?<rom>\d+).*initialized-ram-bytes=(?<ram>\d+) bss-bytes=(?<bss>\d+)");
		Assert.True(metrics.Success);
		var codeBytes = int.Parse(metrics.Groups["code"].Value);
		var romBytes = int.Parse(metrics.Groups["rom"].Value);
		Assert.Equal(0, int.Parse(metrics.Groups["ram"].Value));
		Assert.Equal(4, int.Parse(metrics.Groups["bss"].Value));
		Assert.Equal(codeBytes, romBytes + 4);
	}

	[Fact]
	public void ManagedByRefArgumentsAndConstantDivisorsNeedNoFatalChecks()
	{
		var byRef = CompileAssembly(nameof(ByRefEntry));
		Assert.DoesNotContain("allocated_nonnull", byRef.Text!, StringComparison.Ordinal);

		var division = CompileAssembly(nameof(ConstantDivisionEntry));
		Assert.DoesNotContain("div_nonzero", division.Text!, StringComparison.Ordinal);
		Assert.DoesNotContain("\tillegal", division.Text!, StringComparison.Ordinal);

		var guardedDivision = CompileAssembly(nameof(GuardedDivisionEntry));
		Assert.DoesNotContain("div_nonzero", guardedDivision.Text!, StringComparison.Ordinal);
		Assert.DoesNotContain("\tillegal", guardedDivision.Text!, StringComparison.Ordinal);
	}

	private static M68kCompilationResult CompileAssembly(string entryPoint) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(RomAssemblySectionTests).Assembly.Location,
			EntryPoint = $"{typeof(RomAssemblySectionTests).FullName}::{entryPoint}",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			ExceptionMode = M68kExceptionMode.Yolo
		});
}
