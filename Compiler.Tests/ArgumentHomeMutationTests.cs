using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class ArgumentHomeMutationTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel,
		M68kPeepholeOptimizationMode> ExecutionCases
	{
		get
		{
			var cases = new TheoryData<string, M68kCpuTarget, M68kCpuModel,
				M68kPeepholeOptimizationMode>();
			foreach (var entry in new[]
			{
				nameof(ArgumentHomeMutationFixture.ReadAfterMutation),
				nameof(ArgumentHomeMutationFixture.LoopAfterMutation),
				nameof(ArgumentHomeMutationFixture.StackArgumentAfterMutation)
			})
			foreach (var (target, model) in new[]
			{
				(M68kCpuTarget.M68000, M68kCpuModel.M68000),
				(M68kCpuTarget.M68020, M68kCpuModel.M68020),
				(M68kCpuTarget.M68040, M68kCpuModel.M68040)
			})
			foreach (var mode in new[]
			{
				M68kPeepholeOptimizationMode.FixedPoint,
				M68kPeepholeOptimizationMode.Disabled
			})
				cases.Add(entry, target, model, mode);
			return cases;
		}
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void AddressExposedScalarParametersObserveWritesThroughTheirHomes(
		string entry, M68kCpuTarget target, M68kCpuModel model,
		M68kPeepholeOptimizationMode mode)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(ArgumentHomeMutationFixture).Assembly.Location,
			EntryPoint = $"{typeof(ArgumentHomeMutationFixture).FullName}::{entry}",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Resident,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = mode,
			IncludedExportNames = []
		});
		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = LoadAddress + unchecked((uint)relocation.Offset);
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		bus.WriteLong(StackPointer, ReturnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(LoadAddress + result.EntryPoint, StackPointer);
		for (var instruction = 0; instruction < 20_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				Assert.Equal(StackPointer + 4, cpu.State.A[7]);
				Assert.Equal(42u, cpu.State.D[0]);
				return;
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"{entry}/{model}/{mode} halted at ${cpu.State.ProgramCounter:X8}.");
		}
		Assert.Fail($"{entry}/{model}/{mode} did not return.");
	}
}

public static class ArgumentHomeMutationFixture
{
	public static uint ReadAfterMutation() => SnapshotThenReplace(4);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint SnapshotThenReplace(uint value)
	{
		uint snapshot = value;
		Replace(ref value, 38);
		return snapshot + value;
	}

	public static uint LoopAfterMutation() =>
		CountUntilCleared(0, 1) == 1 && CountUntilCleared(1, 0) == 1 &&
		CountUntilCleared(1, 1) == 1 && CountUntilCleared(0, 0) == 0 ? 42u : 0u;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint CountUntilCleared(uint high, uint low)
	{
		uint count = 0;
		while (high != 0 || low != 0)
		{
			Clear(ref high, ref low);
			if (++count > 2)
				return 99;
		}
		return count;
	}

	public static uint StackArgumentAfterMutation() =>
		ReplaceStackArgument(1, 2, 3, 4, 5, 6);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReplaceStackArgument(uint a, uint b, uint c, uint d,
		uint e, uint value)
	{
		Replace(ref value, 27);
		return a + b + c + d + e + value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Replace(ref uint value, uint replacement) => value = replacement;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Clear(ref uint high, ref uint low)
	{
		high = 0;
		low = 0;
	}
}
