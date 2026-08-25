/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text.RegularExpressions;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kSymbolAttributionTests
{
	private static readonly Regex MetricsPattern = new(
		@"METRICS .*?code-bytes=(?<code>\d+) .*?rom-bytes=(?<rom>\d+) " +
		@"rom-code-bytes=(?<romCode>\d+) rom-rodata-bytes=(?<rodata>\d+) " +
		@"initialized-ram-bytes=(?<ram>\d+) bss-bytes=(?<bss>\d+)",
		RegexOptions.CultureInvariant);

	[Fact]
	public void FinalMethodSymbolExcludesExportAdapterAndSwitchReadOnlyData()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::AllocatedLargeDenseSwitchEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			ExceptionMode = M68kExceptionMode.Yolo,
			MemoryManagement = M68kMemoryManagement.None,
			PeepholeOptimization = M68kPeepholeOptimizationMode.FixedPoint
		});

		var finalMethod = Assert.Single(result.Symbols, static symbol =>
			symbol.Name.EndsWith("::LargeDenseSwitch", StringComparison.Ordinal));
		var export = Assert.Single(result.Symbols, static symbol =>
			symbol.Name == "fixture.add");
		var finalMethodEnd = checked(finalMethod.Address + (uint)finalMethod.Size);
		var lastManagedAddress = result.Symbols
			.Where(static symbol => symbol.Name.Contains("::", StringComparison.Ordinal))
			.Max(static symbol => symbol.Address);

		Assert.Equal(lastManagedAddress, finalMethod.Address);
		Assert.True(finalMethod.Address < export.Address);
		Assert.True(
			finalMethodEnd <= export.Address,
			$"Final method ended at ${finalMethodEnd:X8}, after export adapter " +
			$"start ${export.Address:X8}.");

		var metrics = MetricsPattern.Match(result.Map);
		Assert.True(metrics.Success, "Map did not contain the expected METRICS fields.");
		var codeBytes = Metric(metrics, "code");
		var romBytes = Metric(metrics, "rom");
		var romCodeBytes = Metric(metrics, "romCode");
		var readOnlyDataBytes = Metric(metrics, "rodata");
		var initializedRamBytes = Metric(metrics, "ram");
		var bssBytes = Metric(metrics, "bss");

		Assert.True(readOnlyDataBytes > 0, "Dense switch emitted no ROM read-only table.");
		Assert.True(export.Address < (uint)romCodeBytes);
		Assert.True(finalMethodEnd <= (uint)romCodeBytes);
		Assert.Equal(result.Code.Length, codeBytes);
		Assert.Equal(
			codeBytes,
			checked(romCodeBytes + readOnlyDataBytes + initializedRamBytes + bssBytes));
		Assert.Equal(codeBytes - bssBytes, romBytes);
		Assert.Contains(
			$"{finalMethod.Address:X8} {finalMethod.Size,6} {finalMethod.Name}",
			result.Map,
			StringComparison.Ordinal);
	}

	private static int Metric(Match metrics, string name) =>
		int.Parse(metrics.Groups[name].Value, System.Globalization.CultureInfo.InvariantCulture);
}
