using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed class CallArgumentPushEncodingTests
{
	[Fact]
	public void ApplicationLargeConstantArgumentUsesDirectImmediatePush()
	{
		var old = CompileArgumentEntry(nameof(CallArgumentPushFixtures.EntryLarge),
			optimize: false, M68kClrPolicy.Auto);
		var compact = CompileArgumentEntry(nameof(CallArgumentPushFixtures.EntryLarge),
			optimize: true, M68kClrPolicy.Auto);
		ReadOnlySpan<byte> directPush = [0x2f, 0x3c, 0x12, 0x34, 0x56, 0x78];
		Assert.True(EntryCode(compact, nameof(CallArgumentPushFixtures.EntryLarge))
			.IndexOf(directPush) >= 0);
		Assert.True(EntryCode(old, nameof(CallArgumentPushFixtures.EntryLarge))
			.IndexOf(directPush) < 0);
		Assert.True(compact.Code.Length < old.Code.Length);
	}

	[Fact]
	public void ApplicationZeroArgumentRespectsClrPolicy()
	{
		var automatic = CompileArgumentEntry(nameof(CallArgumentPushFixtures.EntryZero),
			optimize: true, M68kClrPolicy.Auto);
		var forced = CompileArgumentEntry(nameof(CallArgumentPushFixtures.EntryZero),
			optimize: true, M68kClrPolicy.Always);
		ReadOnlySpan<byte> clearPush = [0x42, 0xa7];
		Assert.True(EntryCode(automatic, nameof(CallArgumentPushFixtures.EntryZero))
			.IndexOf(clearPush) < 0);
		Assert.True(EntryCode(forced, nameof(CallArgumentPushFixtures.EntryZero))
			.IndexOf(clearPush) >= 0);
	}

	private static M68kCompilationResult CompileArgumentEntry(
		string entry,
		bool optimize,
		M68kClrPolicy clrPolicy) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(CallArgumentPushFixtures).Assembly.Location,
			EntryPoint = typeof(CallArgumentPushFixtures).FullName + "::" + entry,
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = M68kExceptionMode.Full,
			PeepholeOptimization = M68kPeepholeOptimizationMode.Disabled,
			IncludedExportNames = [],
			ClrPolicy = clrPolicy,
			CodeSizeOptimizations = new()
			{
				OptimizeApplicationCallArguments = optimize
			}
		});

	private static ReadOnlySpan<byte> EntryCode(
		M68kCompilationResult compilation,
		string entry)
	{
		var symbol = Assert.Single(compilation.Symbols, symbol =>
			symbol.Name == typeof(CallArgumentPushFixtures).FullName + "::" + entry);
		return compilation.Code.AsSpan((int)symbol.Address, symbol.Size);
	}

	[Theory]
	[InlineData(M68kCpuModel.M68000)]
	[InlineData(M68kCpuModel.M68020)]
	[InlineData(M68kCpuModel.M68040)]
	public void DirectImmediatePushPreservesStackValueAndFlags(M68kCpuModel model)
	{
		foreach (var value in new[] { 0, 128, -129, int.MinValue, int.MaxValue })
		{
			var old = ExecuteImmediate(model, value, direct: false);
			var compact = ExecuteImmediate(model, value, direct: true);
			Assert.Equal(old.StackPointer, compact.StackPointer);
			Assert.Equal(old.Pushed, compact.Pushed);
			Assert.Equal(unchecked((uint)value), compact.Pushed);
			Assert.Equal(old.StatusRegister, compact.StatusRegister);
			Assert.Equal(0x89abcdefu, compact.D0);
		}
	}

	[Theory]
	[InlineData(M68kCpuModel.M68000)]
	[InlineData(M68kCpuModel.M68020)]
	[InlineData(M68kCpuModel.M68040)]
	public void DirectStackArgumentPushReadsOriginalStackBeforeDecrement(M68kCpuModel model)
	{
		foreach (var value in new uint[] { 0, 1, 0x7fffffff, 0x80000000, 0xffffffff })
		foreach (var displacement in new ushort[] { 4, 8, 0x7ffe })
		{
			var old = Execute(model, value, displacement, direct: false);
			var compact = Execute(model, value, displacement, direct: true);
			Assert.Equal(old.StackPointer, compact.StackPointer);
			Assert.Equal(old.Pushed, compact.Pushed);
			Assert.Equal(value, compact.Pushed);
			Assert.Equal(old.Source, compact.Source);
			Assert.Equal(old.StatusRegister, compact.StatusRegister);
			Assert.Equal(0x89abcdefu, compact.D0);
		}
	}

	private static Observation Execute(
		M68kCpuModel model,
		uint value,
		ushort displacement,
		bool direct)
	{
		const uint entry = 0x1000;
		const uint stack = 0x10000;
		var bus = new TestBus(0x20000);
		if (direct)
		{
			bus.WriteWord(entry, 0x2f2f); // MOVE.L d16(A7),-(A7)
			bus.WriteWord(entry + 2, displacement);
		}
		else
		{
			bus.WriteWord(entry, 0x202f); // MOVE.L d16(A7),D0
			bus.WriteWord(entry + 2, displacement);
			bus.WriteWord(entry + 4, 0x2f00); // MOVE.L D0,-(A7)
		}
		bus.WriteLong(stack + displacement, value);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(entry, stack);
		cpu.State.D[0] = 0x89abcdef;
		cpu.State.StatusRegister = 0x2700;
		cpu.ExecuteInstruction();
		if (!direct)
			cpu.ExecuteInstruction();
		Assert.Equal(entry + (direct ? 4u : 6u), cpu.State.ProgramCounter);
		return new Observation(
			cpu.State.A[7],
			bus.ReadLong(cpu.State.A[7]),
			bus.ReadLong(stack + displacement),
			cpu.State.D[0],
			cpu.State.StatusRegister);
	}

	private static Observation ExecuteImmediate(
		M68kCpuModel model,
		int value,
		bool direct)
	{
		const uint entry = 0x1000;
		const uint stack = 0x10000;
		var bus = new TestBus(0x20000);
		var immediate = unchecked((uint)value);
		var useMoveq = value is >= -128 and <= 127;
		if (direct)
		{
			bus.WriteWord(entry, value == 0 ? (ushort)0x42a7 : (ushort)0x2f3c);
			if (value != 0)
				bus.WriteLong(entry + 2, immediate);
		}
		else
		{
			bus.WriteWord(entry, useMoveq
				? (ushort)(0x7000 | (byte)(sbyte)value)
				: (ushort)0x203c);
			if (!useMoveq)
				bus.WriteLong(entry + 2, immediate);
			bus.WriteWord(entry + (useMoveq ? 2u : 6u), 0x2f00);
		}
		bus.WriteLong(stack - 4, 0x12345678);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(entry, stack);
		cpu.State.D[0] = 0x89abcdef;
		cpu.State.StatusRegister = 0x2700;
		cpu.ExecuteInstruction();
		if (!direct)
			cpu.ExecuteInstruction();
		var length = direct ? value == 0 ? 2u : 6u : useMoveq ? 4u : 8u;
		Assert.Equal(entry + length, cpu.State.ProgramCounter);
		return new Observation(
			cpu.State.A[7],
			bus.ReadLong(cpu.State.A[7]),
			bus.ReadLong(stack + 4),
			cpu.State.D[0],
			cpu.State.StatusRegister);
	}

	private sealed record Observation(
		uint StackPointer,
		uint Pushed,
		uint Source,
		uint D0,
		ushort StatusRegister);
}

public static class CallArgumentPushFixtures
{
	public static uint EntryLarge() => Mix(1, 2, 3, 4, 5, 6, 0x12345678);
	public static uint EntryZero() => Mix(1, 2, 3, 4, 5, 6, 0);

	private static uint Mix(uint a, uint b, uint c, uint d,
		uint e, uint f, uint g) => a + b + c + d + e + f + g;
}
