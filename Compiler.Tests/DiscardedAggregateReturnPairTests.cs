/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Text.RegularExpressions;
using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class DiscardedAggregateReturnPairTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;
	private const uint ExpectedHigh = 0x1234_5678;
	private const uint ExpectedLow = 0x9ABC_DEF0;
	private const uint ExpectedState = 0x0000_2000;
	private const uint ExpectedFrame = 0x0000_3000;
	private const uint ExpectedLibrary = 0x0000_4000;

	private static readonly Regex TopLevelMethodLabel = new(
		"^C68K_method_003A(?![^\\r\\n]*_003A(?:BB|end))[^\\r\\n]+:\\r?$",
		RegexOptions.Multiline | RegexOptions.CultureInvariant);

	[Fact]
	public void DiscardedFortyFourByteResultBalancesBeforePairReturn()
	{
		Assert.Equal(44, DiscardedAggregateReturnPairFixtures.AggregateSize);
		Assert.Equal(80, DiscardedAggregateReturnPairFixtures.ExactCallSize);
		var compilation = Compile();

		var discardedAggregate = MethodBody(compilation,
			Name(nameof(DiscardedAggregateReturnPairFixtures.DiscardedAggregateEntry)));
		var materializedAggregate = MethodBody(compilation,
			Name(nameof(DiscardedAggregateReturnPairFixtures.MaterializedAggregateEntry)));
		var discardedScalar = MethodBody(compilation,
			Name(nameof(DiscardedAggregateReturnPairFixtures.DiscardedScalarEntry)));
		var discardedPair = MethodBody(compilation,
			Name(nameof(DiscardedAggregateReturnPairFixtures.DiscardedPairEntry)));
		var exactDiscardedAggregate = MethodBody(compilation,
			Name(nameof(DiscardedAggregateReturnPairFixtures.ExactDiscardedAggregateEntry)));
		var exactMaterializedAggregate = MethodBody(compilation,
			Name(nameof(DiscardedAggregateReturnPairFixtures.ExactMaterializedAggregateEntry)));

		var aggregateTarget = MethodLabel(compilation,
			Name("DispatchAggregate"));
		var scalarTarget = MethodLabel(compilation, Name("DispatchScalar"));
		var pairTarget = MethodLabel(compilation, Name("DispatchPair"));
		var exactAggregateTarget = MethodLabel(compilation,
			Name("DispatchExactAggregate"));

		Assert.Equal(4, CleanupAfterCall(discardedAggregate, aggregateTarget));
		Assert.Equal(4, CleanupAfterCall(materializedAggregate, aggregateTarget));
		Assert.Equal(0, CleanupAfterCall(discardedScalar, scalarTarget));
		Assert.Equal(0, CleanupAfterCall(discardedPair, pairTarget));
		Assert.Equal(4, CleanupAfterCall(exactDiscardedAggregate,
			exactAggregateTarget));
		Assert.Equal(4, CleanupAfterCall(exactMaterializedAggregate,
			exactAggregateTarget));

		AssertPairAndStack(compilation,
			nameof(DiscardedAggregateReturnPairFixtures.DiscardedAggregateEntry));
		AssertPairAndStack(compilation,
			nameof(DiscardedAggregateReturnPairFixtures.MaterializedAggregateEntry));
		AssertPairAndStack(compilation,
			nameof(DiscardedAggregateReturnPairFixtures.DiscardedScalarEntry));
		AssertPairAndStack(compilation,
			nameof(DiscardedAggregateReturnPairFixtures.DiscardedPairEntry));
		AssertScalarAndStack(compilation,
			nameof(DiscardedAggregateReturnPairFixtures.TransparentConstructorStoredInFieldEntry),
			ExpectedState);
		AssertScalarAndStack(compilation,
			nameof(DiscardedAggregateReturnPairFixtures.TransparentConstructorStoredInStaticFieldEntry),
			ExpectedState);
		AssertPairAndStack(compilation,
			nameof(DiscardedAggregateReturnPairFixtures.ExactDiscardedAggregateEntry),
			exactRegisterAbi: true);
		AssertPairAndStack(compilation,
			nameof(DiscardedAggregateReturnPairFixtures.ExactMaterializedAggregateEntry),
			exactRegisterAbi: true);
	}

	private static void AssertScalarAndStack(M68kCompilationResult compilation,
		string method, uint expected)
	{
		var symbol = Assert.Single(compilation.Symbols,
			symbol => symbol.Name == Name(method));
		var bus = Load(compilation);
		bus.WriteLong(StackPointer, ReturnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(LoadAddress + unchecked((uint)symbol.Address), StackPointer);
		RunUntilReturn(cpu, method);
		Assert.Equal(expected, cpu.State.D[0]);
		Assert.Equal(StackPointer + 4, cpu.State.A[7]);
	}

	private static void AssertPairAndStack(M68kCompilationResult compilation,
		string method, bool exactRegisterAbi = false)
	{
		var symbol = Assert.Single(compilation.Symbols,
			symbol => symbol.Name == Name(method));
		var bus = Load(compilation);
		bus.WriteLong(StackPointer, ReturnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(LoadAddress + unchecked((uint)symbol.Address), StackPointer);
		if (exactRegisterAbi)
		{
			cpu.State.A[0] = ExpectedState;
			cpu.State.A[1] = ExpectedFrame;
			cpu.State.D[0] = ExpectedLibrary;
		}
		RunUntilReturn(cpu, method);
		Assert.Equal(ExpectedHigh, cpu.State.D[0]);
		Assert.Equal(ExpectedLow, cpu.State.D[1]);
		Assert.Equal(StackPointer + 4, cpu.State.A[7]);
	}

	private static TestBus Load(M68kCompilationResult compilation)
	{
		var bus = new TestBus();
		compilation.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in compilation.Relocations)
		{
			var address = LoadAddress + unchecked((uint)relocation.Offset);
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		return bus;
	}

	private static void RunUntilReturn(IM68kCore cpu, string method)
	{
		for (var instruction = 0; instruction < 200_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel) return;
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"MC68000 {method} halted at ${cpu.State.ProgramCounter:X8}, " +
					$"opcode ${cpu.State.LastOpcode:X4}.");
		}
		Assert.Fail($"MC68000 {method} did not return; " +
			$"PC=${cpu.State.ProgramCounter:X8}, A7=${cpu.State.A[7]:X8}.");
	}

	private static int CleanupAfterCall(string body, string target)
	{
		var lines = body.Split(['\r', '\n'],
			StringSplitOptions.RemoveEmptyEntries);
		var call = Array.FindIndex(lines, line =>
			(line.StartsWith("\tjsr\t", StringComparison.Ordinal) ||
				line.StartsWith("\tbsr", StringComparison.Ordinal)) &&
			line.EndsWith(target, StringComparison.Ordinal));
		Assert.True(call >= 0, $"Call to {target} was not emitted.\n{body}");
		for (var index = call + 1; index < lines.Length; index++)
		{
			var line = lines[index];
			if (line.EndsWith(':')) continue;
			if (line == "\taddq.l\t#4,a7") return 4;
			if (line == "\taddq.l\t#8,a7") return 8;
			var match = Regex.Match(line,
				"^\\tlea\\t(?<bytes>[0-9]+)\\(a7\\),a7$");
			if (match.Success) return int.Parse(match.Groups["bytes"].Value,
				System.Globalization.CultureInfo.InvariantCulture);
			return 0;
		}
		return 0;
	}

	private static string MethodLabel(M68kCompilationResult compilation,
		string symbolName)
	{
		var assembly = Assert.IsType<string>(compilation.Text);
		var labels = TopLevelMethodLabel.Matches(assembly);
		var methods = compilation.Symbols.Where(symbol =>
			symbol.Name.Contains("::", StringComparison.Ordinal)).ToArray();
		Assert.Equal(methods.Length, labels.Count);
		var methodIndex = Array.FindIndex(methods,
			symbol => symbol.Name == symbolName);
		Assert.True(methodIndex >= 0,
			$"Generated symbol was not found: {symbolName}.");
		return labels[methodIndex].Value.TrimEnd('\r', '\n', ':');
	}

	private static string MethodBody(M68kCompilationResult compilation,
		string symbolName)
	{
		var assembly = Assert.IsType<string>(compilation.Text);
		var labels = TopLevelMethodLabel.Matches(assembly);
		var methods = compilation.Symbols.Where(symbol =>
			symbol.Name.Contains("::", StringComparison.Ordinal)).ToArray();
		Assert.Equal(methods.Length, labels.Count);
		var methodIndex = Array.FindIndex(methods,
			symbol => symbol.Name == symbolName);
		Assert.True(methodIndex >= 0,
			$"Generated symbol was not found: {symbolName}.");
		var start = labels[methodIndex].Index;
		var end = methodIndex + 1 < labels.Count
			? labels[methodIndex + 1].Index : assembly.Length;
		return assembly[start..end];
	}

	private static string Name(string method) =>
		$"CopperSharp.Compiler.Tests.DiscardedAggregateReturnPairFixtures::{method}";

	private static M68kCompilationResult Compile() =>
		AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath =
				typeof(DiscardedAggregateReturnPairFixtures).Assembly.Location,
			EntryPoint = Name(nameof(
				DiscardedAggregateReturnPairFixtures.ReachabilityEntry)),
			Cpu = M68kCpuTarget.M68000,
			ClrPolicy = M68kClrPolicy.Always,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = M68kPeepholeOptimizationMode.Disabled,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			ManagedAssemblyPaths =
			[
				typeof(APTR).Assembly.Location,
			],
		});
}
