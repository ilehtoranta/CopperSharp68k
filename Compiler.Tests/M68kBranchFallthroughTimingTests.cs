using System.Buffers.Binary;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;
using Xunit.Abstractions;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kBranchFallthroughTimingTests(ITestOutputHelper output)
{
	[Theory]
	[InlineData(20)]
	[InlineData(160)]
	public void LoopExitUsesTheFasterAvailableFallthrough(int paddingBytes)
	{
		var baseline = Build(paddingBytes, false);
		var optimized = Build(paddingBytes, true);
		var beforeHot = Measure(baseline, false);
		var afterHot = Measure(optimized, false);
		var beforeExit = Measure(baseline, true);
		var afterExit = Measure(optimized, true);
		output.WriteLine($"padding={paddingBytes}, bytes={baseline.Bytes.Length}/{optimized.Bytes.Length}, " +
			$"continuation={beforeHot}/{afterHot}, exit={beforeExit}/{afterExit} MC68000 cycles");

		if (paddingBytes < 128)
		{
			Assert.Equal(baseline.Bytes, optimized.Bytes);
			Assert.Equal(beforeHot, afterHot);
			Assert.Equal(beforeExit, afterExit);
		}
		else
		{
			Assert.Equal(0x6704, BinaryPrimitives.ReadUInt16BigEndian(optimized.Bytes));
			Assert.Equal(0x6000, BinaryPrimitives.ReadUInt16BigEndian(optimized.Bytes.AsSpan(2)));
			Assert.Equal(baseline.Bytes.Length + 2, optimized.Bytes.Length);
			Assert.Equal(beforeHot - 2, afterHot);
			Assert.Equal(beforeExit + 8, afterExit);
		}
	}

	[Fact]
	public void WordBranchExpansionDoesNotOverflowAnExistingShortBranch()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "near");
		assembler.EmitBranch(M68kCondition.NotEqual, "far", preferFastFallthrough: true);
		for (var i = 0; i < 122; i += 2) assembler.EmitWord(0x4E71);
		assembler.Mark("near");
		for (var i = 0; i < 100; i += 2) assembler.EmitWord(0x4E71);
		assembler.Mark("far");
		assembler.EmitWord(0x4E75);

		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Equal(0x677E, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Equal(0x6600, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(2)));
		Assert.Equal(linked.Bytes, assembler.Link(0, new Dictionary<string, uint>()).Bytes);
	}

	[Fact]
	public void WordBranchExpansionPreservesRequestedAlignment()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.NotEqual, "far", preferFastFallthrough: true);
		assembler.Mark("aligned");
		assembler.RequestLongAlignment("aligned");
		for (var i = 0; i < 160; i += 2) assembler.EmitWord(0x4E71);
		assembler.Mark("far");
		assembler.EmitWord(0x4E75);
		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Equal(0, linked.Labels["aligned"] % 4);
		Assert.Equal(0x6600, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
	}

	[Fact]
	public void ExpandedBranchHasStableFixupsAndAssemblyOutput()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.NotEqual, "far", preferFastFallthrough: true);
		for (var i = 0; i < 160; i += 2) assembler.EmitWord(0x4E71);
		assembler.Mark("far");
		assembler.EmitWord(0x4E75);
		var first = assembler.Link(0, new Dictionary<string, uint>());
		var text = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("beq.s", text);
		Assert.Contains("bra.w", text);
		Assert.Equal(first.Bytes, assembler.Link(0, new Dictionary<string, uint>()).Bytes);
	}

	[Fact]
	public void GeneratedLargeLoopCycleBenchmark()
	{
		var request = new M68kCompilationRequest
		{
			AssemblyPath = typeof(BranchTimingFixture).Assembly.Location,
			EntryPoint = typeof(BranchTimingFixture).FullName + "::Entry",
			Cpu = M68kCpuTarget.M68000,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			OutputFormat = M68kOutputFormat.Hunk,
			ExceptionMode = M68kExceptionMode.Yolo,
			MemoryManagement = M68kMemoryManagement.None,
			IncludedExportNames = [],
			Hunk = new() { IncludeSymbols = false }
		};
		var baseline = AmigaM68kCompiler.Compile(request with { OptimizeWordBranchFallthrough = false });
		var optimized = AmigaM68kCompiler.Compile(request);
		var before = Execute(baseline);
		var after = Execute(optimized);
		output.WriteLine($"generated loop: code bytes {baseline.Code.Length}/{optimized.Code.Length}; " +
			$"cycles {before.Cycles}/{after.Cycles}; result {before.Result:X8}/{after.Result:X8}");
		Assert.Equal(BranchTimingFixture.Entry(), before.Result);
		Assert.Equal(before.Result, after.Result);
		Assert.True(after.Cycles < before.Cycles, $"Expected a loop improvement: {before.Cycles}/{after.Cycles}");
		var otherCpu = request with { Cpu = M68kCpuTarget.M68020 };
		Assert.Equal(
			AmigaM68kCompiler.Compile(otherCpu with { OptimizeWordBranchFallthrough = false }).Code,
			AmigaM68kCompiler.Compile(otherCpu).Code);
	}

	private static (uint Result, long Cycles) Execute(M68kCompilationResult code)
	{
		var bus = new TestBus();
		ResidentCodeSizeExecutionTests.LoadImage(code.Image, 0x10000).CopyTo(bus.Memory, 0x10000);
		bus.WriteLong(0x80000, 0x1000);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(0x10000 + code.EntryPoint, 0x80000);
		for (var i = 0; i < 200000 && cpu.State.ProgramCounter != 0x1000; i++) cpu.ExecuteInstruction();
		Assert.Equal(0x1000u, cpu.State.ProgramCounter);
		return (cpu.State.D[0], cpu.State.Cycles);
	}

	private static LinkedCode Build(int paddingBytes, bool optimize)
	{
		var assembler = new M68kAssembler();
		assembler.Mark("entry");
		assembler.EmitBranch(M68kCondition.NotEqual, "exit", preferFastFallthrough: optimize);
		assembler.Mark("continue");
		assembler.EmitWord(0x4E75);
		for (var i = 0; i < paddingBytes; i += 2) assembler.EmitWord(0x4E71);
		assembler.Mark("exit");
		assembler.EmitWord(0x4E75);
		return assembler.Link(0x10000, new Dictionary<string, uint>());
	}

	private static long Measure(LinkedCode code, bool exit)
	{
		var bus = new TestBus();
		code.Bytes.CopyTo(bus.Memory, 0x10000);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(0x10000, 0x80000);
		cpu.State.StatusRegister = (ushort)(0x2700 | (exit ? 0 : 4));
		var target = 0x10000u + (uint)code.Labels[exit ? "exit" : "continue"];
		var before = cpu.State.Cycles;
		for (var i = 0; i < 2 && cpu.State.ProgramCounter != target; i++) cpu.ExecuteInstruction();
		Assert.Equal(target, cpu.State.ProgramCounter);
		return cpu.State.Cycles - before;
	}
}

public static class BranchTimingFixture
{
	public static uint Entry()
	{
		uint value = 0x12345678;
		for (var i = 0; i < 100; i++)
		{
			value = (value << 3) ^ (value >> 5) ^ 1;
			value = (value << 3) ^ (value >> 5) ^ 2;
			value = (value << 3) ^ (value >> 5) ^ 3;
			value = (value << 3) ^ (value >> 5) ^ 4;
			value = (value << 3) ^ (value >> 5) ^ 5;
			value = (value << 3) ^ (value >> 5) ^ 6;
			value = (value << 3) ^ (value >> 5) ^ 7;
			value = (value << 3) ^ (value >> 5) ^ 8;
			value = (value << 3) ^ (value >> 5) ^ 9;
			value = (value << 3) ^ (value >> 5) ^ 10;
			value = (value << 3) ^ (value >> 5) ^ 11;
			value = (value << 3) ^ (value >> 5) ^ 12;
			value = (value << 3) ^ (value >> 5) ^ 13;
			value = (value << 3) ^ (value >> 5) ^ 14;
			value = (value << 3) ^ (value >> 5) ^ 15;
			value = (value << 3) ^ (value >> 5) ^ 16;
		}
		return value;
	}
}
