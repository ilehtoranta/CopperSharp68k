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

public sealed class DateOnlyParsingContractTests
{
	[Theory]
	[InlineData("0001-01-01", 1, 1, 1)]
	[InlineData("2000-02-29", 2000, 2, 29)]
	[InlineData("2024-12-31", 2024, 12, 31)]
	[InlineData("9999-12-31", 9999, 12, 31)]
	public void CanonicalInvariantFormsMatchHostDateOnly(string text, int year, int month, int day)
	{
		Assert.True(DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var parsed));
		Assert.Equal(new DateOnly(year, month, day), parsed);
	}

	[Theory]
	[InlineData(1900, false)]
	[InlineData(2000, true)]
	[InlineData(2024, true)]
	[InlineData(2100, false)]
	public void LeapYearBoundariesMatchGregorianCalendar(int year, bool expected)
	{
		var text = $"{year:D4}-02-29";
		Assert.Equal(expected, DateOnly.TryParse(text, CultureInfo.InvariantCulture, out _));
	}

	[Theory]
	[InlineData("")]
	[InlineData("0000-01-01")]
	[InlineData("2024-00-01")]
	[InlineData("2024-13-01")]
	[InlineData("2024-04-31")]
	[InlineData("2024-02-30")]
	[InlineData("24-02-29")]
	public void InvalidCanonicalFormsAreRejectedByHostDateOnly(string text) =>
		Assert.False(DateOnly.TryParse(text, CultureInfo.InvariantCulture, out _));

	[Theory]
	[InlineData("1/2/2024")]
	[InlineData("February 29, 2024")]
	[InlineData("2024/02/29")]
	[InlineData(" 2024-02-29")]
	public void HostSupportsAdditionalFormsOutsideTheBoundedTargetGrammar(string text) =>
		Assert.True(DateOnly.TryParse(text, CultureInfo.InvariantCulture, out _));

	[Fact]
	public void NullTryParseReturnsFalseAndDefaultValue()
	{
		Assert.False(DateOnly.TryParse(null, CultureInfo.InvariantCulture, out var parsed));
		Assert.Equal(DateOnly.MinValue, parsed);
		Assert.Throws<ArgumentNullException>(() => DateOnly.Parse(null!, CultureInfo.InvariantCulture));
	}

	[Fact]
	public void PinnedParserRejectsCurrentCultureProviderCallSites()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var error = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibDateOnlyCurrentCultureParseRejectionEntry",
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
		Assert.Contains("System.DateOnly::TryParse", error.Message, StringComparison.Ordinal);
	}
}
