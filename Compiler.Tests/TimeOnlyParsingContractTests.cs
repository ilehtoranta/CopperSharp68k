/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Globalization;
using Copper68k;
using CopperSharp.Compiler;
using CopperSharp.Compiler.Framework;
using CopperSharp.Runtime;
using CopperSharp.Runtime.AmigaPal;

namespace CopperSharp.Compiler.Tests;

public sealed class TimeOnlyParsingContractTests
{
	[Theory]
	[InlineData("00:00", "00:00:00.0000000")]
	[InlineData("09:08", "09:08:00.0000000")]
	[InlineData("12:34:56", "12:34:56.0000000")]
	[InlineData("13:24:35.1", "13:24:35.1000000")]
	[InlineData("13:24:35.12345", "13:24:35.1234500")]
	[InlineData("23:59:59.9999999", "23:59:59.9999999")]
	public void CanonicalInvariantFormsMatchHostTimeOnly(string text, string expected)
	{
		Assert.True(TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out var parsed));
		Assert.Equal(expected, parsed.ToString("O", CultureInfo.InvariantCulture));
	}

	[Theory]
	[InlineData("")]
	[InlineData("24:00")]
	[InlineData("12:60")]
	[InlineData("12:34:60")]
	[InlineData("12-34")]
	[InlineData("12:34x")]
	public void InvalidCanonicalFormsAreRejectedByHostTimeOnly(string text) =>
		Assert.False(TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out _));

	[Theory]
	[InlineData("1:02")]
	[InlineData("12:34:56.")]
	[InlineData("12:34:56.12345678")]
	[InlineData(" 12:34 ")]
	public void HostSupportsAdditionalFormsOutsideTheBoundedTargetGrammar(string text) =>
		Assert.True(TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out _));

	[Fact]
	public void NullTryParseReturnsFalseAndDefaultValue()
	{
		Assert.False(TimeOnly.TryParse(null, CultureInfo.InvariantCulture, out var parsed));
		Assert.Equal(TimeOnly.MinValue, parsed);
		Assert.Throws<ArgumentNullException>(() => TimeOnly.Parse(null!, CultureInfo.InvariantCulture));
	}

	[Fact]
	public void PinnedParserRejectsCurrentCultureProviderCallSites()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var error = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibTimeOnlyCurrentCultureParseRejectionEntry",
			IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(M68kRuntime).Assembly.Location, typeof(EnvironmentPal).Assembly.Location],
			Cpu = M68kCpuTarget.M68000,
			FloatingPoint = M68kFloatingPointMode.SoftFloat,
			OutputFormat = M68kOutputFormat.Hunk,
			ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 32 * 1024 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		}));
		Assert.Contains("System.TimeOnly::TryParse", error.Message, StringComparison.Ordinal);
	}
}
