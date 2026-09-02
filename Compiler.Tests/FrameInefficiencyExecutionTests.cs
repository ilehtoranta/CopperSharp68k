using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class FrameInefficiencyExecutionTests
{
	public static TheoryData<string, M68kCpuTarget, M68kCpuModel,
		M68kPeepholeOptimizationMode> ExecutionCases
	{
		get
		{
			var cases = new TheoryData<string, M68kCpuTarget, M68kCpuModel,
				M68kPeepholeOptimizationMode>();
			foreach (var entry in new[]
			{
				nameof(FrameInefficiencyFixtures.ExplicitInitialization),
				nameof(FrameInefficiencyFixtures.IncomingAggregateCopy),
				nameof(FrameInefficiencyFixtures.IncomingAggregateDynamicCopy),
				nameof(FrameInefficiencyFixtures.LocalAggregateCopy),
				nameof(FrameInefficiencyFixtures.ConstrainedEmptyCall),
				nameof(FrameInefficiencyFixtures.ReadOnlyArgument),
				nameof(FrameInefficiencyFixtures.MutableWrapperArgument)
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
	public void FrameChangesPreserveAggregateValuesIncomingArgumentsAndCalleeSavedRegisters(
		string entry, M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
	{
		var result = Compile(entry, target, mode);
		const uint load = 0x10000, stack = 0x80000, sentinel = 0x1000;
		var bus = new TestBus();
		// A nonzero stack makes missing initialization visible rather than relying
		// on the test bus's initial zero contents.
		bus.Memory.AsSpan((int)stack - 8192, 8192).Fill(0xa5);
		result.Code.CopyTo(bus.Memory.AsSpan((int)load));
		foreach (var relocation in result.Relocations)
		{
			var address = load + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + load);
		}
		bus.WriteLong(stack, sentinel);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(load + result.EntryPoint, stack);
		for (var register = 2; register <= 7; register++)
			cpu.State.D[register] = (uint)(0x12340000 + register);
		for (var register = 2; register <= 6; register++)
			cpu.State.A[register] = (uint)(0x40000 + register * 256);
		for (var step = 0; step < 20_000 && cpu.State.ProgramCounter != sentinel; step++)
		{
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted, $"{entry}/{model}/{mode}: halted at {cpu.State.ProgramCounter:X8}.");
		}
		Assert.Equal(sentinel, cpu.State.ProgramCounter);
		Assert.Equal(42u, cpu.State.D[0]);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		// A sole process entry deliberately has no caller-owned callee saves.
		// Exercise the retained aggregate callee separately with a real incoming
		// stack argument to validate its own ABI boundary and copy displacements.
		var helperName = entry switch
		{
			nameof(FrameInefficiencyFixtures.IncomingAggregateCopy) => "::CopyIncoming",
			nameof(FrameInefficiencyFixtures.IncomingAggregateDynamicCopy) => "::CopyIncomingDynamic",
			_ => null
		};
		if (helperName is null)
			return;
		var helper = Assert.Single(result.Symbols, symbol => symbol.Name.EndsWith(helperName, StringComparison.Ordinal));
		bus.WriteLong(stack, sentinel);
		for (uint offset = 0; offset < 88; offset += 4)
			bus.WriteLong(stack + 4 + offset, offset == 0 ? 40u : 0);
		cpu.Reset(load + helper.Address, stack);
		for (var register = 2; register <= 7; register++)
			cpu.State.D[register] = (uint)(0x12340000 + register);
		for (var register = 2; register <= 6; register++)
			cpu.State.A[register] = (uint)(0x40000 + register * 256);
		for (var step = 0; step < 20_000 && cpu.State.ProgramCounter != sentinel; step++)
		{
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted);
		}
		Assert.Equal(sentinel, cpu.State.ProgramCounter);
		Assert.Equal(42u, cpu.State.D[0]);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		Assert.Equal(40u, bus.ReadLong(stack + 4));
		for (var register = 2; register <= 7; register++)
			Assert.Equal((uint)(0x12340000 + register), cpu.State.D[register]);
		for (var register = 2; register <= 6; register++)
			Assert.Equal((uint)(0x40000 + register * 256), cpu.State.A[register]);
	}

	[Fact]
	public void EmptyConstrainedValueCallDoesNotCreateAbiScratchRegisters()
	{
		using var module = new CompilationModule(Assembly.GetExecutingAssembly().Location);
		var entry = module.ResolveEntryPoint(
			$"{typeof(FrameInefficiencyFixtures).FullName}::{nameof(FrameInefficiencyFixtures.ConstrainedEmptyCall)}");
		var call = Assert.Single(entry.Instructions, instruction => instruction.OpCode == OpCodes.Call);
		var target = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset).Definition!;
		var function = CilMachineIrBuilder.Build(target, module);
		Assert.DoesNotContain(function.Blocks.SelectMany(block => block.Instructions),
			instruction => instruction.Operation == M68kMachineOperation.Call);
	}

	[Fact]
	public void LargeIncomingAggregateCopyDoesNotSaveD7AsATemporary()
	{
		var result = Compile(nameof(FrameInefficiencyFixtures.IncomingAggregateCopy),
			M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.Disabled);
		var method = Assert.Single(result.Symbols, symbol => symbol.Name.EndsWith("::CopyIncoming", StringComparison.Ordinal));
		var bytes = result.Code.AsSpan((int)method.Address, method.Size);
		var words = new List<ushort>();
		for (var index = 0; index < bytes.Length; index += 2)
			words.Add(System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes[index..]));

		Assert.DoesNotContain((ushort)0x2f07, words); // MOVE.L D7,-(SP).
		Assert.DoesNotContain((ushort)0x2e1f, words); // MOVE.L (SP)+,D7.
	}

	[Fact]
	public void ReadOnlyPointerWrapperArgumentNeedsNoStackHome()
	{
		var result = Compile(nameof(FrameInefficiencyFixtures.ReadOnlyArgument),
			M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.Disabled);
		var method = Assert.Single(result.Symbols, symbol => symbol.Name.EndsWith("::ReadRaw", StringComparison.Ordinal));
		// The native ABI receives APTR in A0 and returns uint in D0. There is no
		// address escape, so the entire helper is MOVE.L A0,D0; RTS.
		Assert.Equal(new byte[] { 0x20, 0x08, 0x4e, 0x75 },
			result.Code.AsSpan((int)method.Address, method.Size).ToArray());
	}

	[Fact]
	public void AggregateInitializationDoesNotAlsoEmitAPrologueClear()
	{
		var result = Compile(nameof(FrameInefficiencyFixtures.ExplicitInitialization),
			M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.Disabled);
		Assert.Contains("allocated_aggregate_zero_loop", result.Text!);
		Assert.DoesNotContain("allocated_frame_zero_loop", result.Text!);
	}

	private static M68kCompilationResult Compile(string entry, M68kCpuTarget target,
		M68kPeepholeOptimizationMode mode) => M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(FrameInefficiencyFixtures).FullName}::{entry}",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = mode,
			IncludedExportNames = []
		});
}

public static class FrameInefficiencyFixtures
{
	public struct LargeValue
	{
		public uint F00, F01, F02, F03, F04, F05, F06, F07, F08, F09, F10;
		public uint F11, F12, F13, F14, F15, F16, F17, F18, F19, F20, F21;
	}

	public static uint ExplicitInitialization()
	{
		LargeValue value = default;
		value.F00 = 42;
		return Read(ref value);
	}

	public static uint IncomingAggregateCopy()
	{
		LargeValue value = default;
		value.F00 = 40;
		return CopyIncoming(value) == 42 && value.F00 == 40 ? 42u : 0;
	}

	public static uint LocalAggregateCopy()
	{
		LargeValue value = default;
		value.F00 = 40;
		return CopyLocal(ref value) == 42 && value.F00 == 40 ? 42u : 0;
	}

	public static uint IncomingAggregateDynamicCopy()
	{
		LargeValue value = default;
		value.F00 = 40;
		return CopyIncomingDynamic(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint CopyIncomingDynamic(LargeValue value)
	{
		uint* temporary = stackalloc uint[2];
		temporary[0] = value.F00;
		temporary[1] = 2;
		value.F00 = temporary[0] + temporary[1];
		return Read(ref value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CopyIncoming(LargeValue value)
	{
		value.F00 += 2;
		return Read(ref value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CopyLocal(ref LargeValue source)
	{
		LargeValue local = source;
		local.F00 += 2;
		return Read(ref local);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint Read(ref LargeValue value) =>
		value.F00 + value.F01 + value.F02 + value.F03 + value.F04 + value.F05 +
		value.F06 + value.F07 + value.F08 + value.F09 + value.F10 + value.F11 +
		value.F12 + value.F13 + value.F14 + value.F15 + value.F16 + value.F17 +
		value.F18 + value.F19 + value.F20 + value.F21;

	public interface IByteSink { void Put(byte value); }
	public struct EmptySink : IByteSink
	{
		public uint Tag;
		public void Put(byte value) { }
	}

	public static uint ConstrainedEmptyCall()
	{
		EmptySink sink = default;
		InvokeConstrained(ref sink, 9);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void InvokeConstrained<T>(ref T sink, byte value) where T : struct, IByteSink =>
		sink.Put(value);

	public static uint ReadOnlyArgument() => ReadRaw(APTR.FromPointer(42));

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadRaw(APTR value) => value.Raw;

	public static uint MutableWrapperArgument() => SnapshotAndReplace(APTR.FromPointer(6));

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint SnapshotAndReplace(APTR value)
	{
		var snapshot = value.Raw;
		ReplaceWrapper(ref value);
		return snapshot + value.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReplaceWrapper(ref APTR value) => value = APTR.FromPointer(36);
}
