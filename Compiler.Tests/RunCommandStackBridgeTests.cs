using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class RunCommandStackBridgeTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint ReturnSentinel = 0x0000_1000;
	private const uint ExecBase = 0x0000_4000;
	private const uint StackSwapVector = ExecBase - 732;

	public static TheoryData<M68kCpuTarget, M68kCpuModel,
		M68kPeepholeOptimizationMode, M68kRuntimeProfile> ExecutionCases
	{
		get
		{
			var cases = new TheoryData<M68kCpuTarget, M68kCpuModel,
				M68kPeepholeOptimizationMode, M68kRuntimeProfile>();
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
			foreach (var profile in new[]
			{
				M68kRuntimeProfile.Freestanding, M68kRuntimeProfile.Resident
			})
				cases.Add(target, model, mode, profile);
			return cases;
		}
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void BridgeRestoresBothStacksAndOuterAbiAfterARegisterClobberingCommand(
		M68kCpuTarget target, M68kCpuModel model,
		M68kPeepholeOptimizationMode mode, M68kRuntimeProfile profile)
	{
		var result = Compile(target, mode, profile);
		Assert.Equal(0, result.NativeCompatibility.FatalMachineFaultSiteCount);
		Assert.Empty(result.NativeCompatibility.RuntimeFeatures);
		Assert.Empty(result.NativeCompatibility.RuntimeHelpers);
		Assert.Empty(result.NativeCompatibility.ExternalNativeTargets);
		var bus = new BridgeBus(result);
		var first = new Invocation(0, 0x1000, 7, 0x7654_3210);
		var second = new Invocation(1, 0x4000, 1, 0xF123_4567);
		bus.Prepare(first);
		bus.Prepare(second);
		using var firstCpu = CreateCpu(model, result, bus, first);
		using var secondCpu = CreateCpu(model, result, bus, second);

		// Interleave actual instructions, including the interval on the new stack.
		for (var instruction = 0; instruction < 20_000; instruction++)
		{
			if (firstCpu.State.ProgramCounter != ReturnSentinel)
				Step(firstCpu, bus, first);
			if (secondCpu.State.ProgramCounter != ReturnSentinel)
				Step(secondCpu, bus, second);
			if (firstCpu.State.ProgramCounter == ReturnSentinel &&
				secondCpu.State.ProgramCounter == ReturnSentinel) break;
		}
		AssertReturned(firstCpu, bus, first);
		AssertReturned(secondCpu, bus, second);
		Assert.Equal(2, first.StackSwaps);
		Assert.Equal(2, second.StackSwaps);
		Assert.True(first.CommandInstructions > 14);
		Assert.True(second.CommandInstructions > 14);

		// Reuse the same loaded image, task, StackSwapStruct and stack allocation.
		// Do not reseed the restored descriptor or task bounds between invocations.
		using var again = CreateCpu(model, result, bus, first);
		for (var instruction = 0; instruction < 10_000 &&
			again.State.ProgramCounter != ReturnSentinel; instruction++)
			Step(again, bus, first);
		AssertReturned(again, bus, first);
		Assert.Equal(4, first.StackSwaps);
		Assert.Equal(bus.InitialImage, bus.Memory.AsSpan((int)LoadAddress,
			result.Code.Length).ToArray());
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void OrdinaryCallsReadTheOldFrameRelativeToTheNewStack(
		M68kCpuTarget target, M68kCpuModel model,
		M68kPeepholeOptimizationMode mode, M68kRuntimeProfile profile)
	{
		var result = Compile(target, mode, profile,
			nameof(RunCommandStackBridgeFixture.OrdinaryEntry));
		var bus = new BridgeBus(result);
		var invocation = new Invocation(0, 0x1000, 7, 0x7654_3210);
		bus.Prepare(invocation);
		using var cpu = CreateCpu(model, result, bus, invocation);
		var failure = Assert.Throws<InvalidOperationException>(() =>
		{
			for (var instruction = 0; instruction < 10_000 &&
				cpu.State.ProgramCounter != ReturnSentinel; instruction++)
				Step(cpu, bus, invocation);
		});
		Assert.Contains($"read outside its ranges: ${invocation.NewUpper + 8:X8}+4, CpuDataRead",
			failure.Message);
		Assert.Equal(1, invocation.StackSwaps);
		Assert.Equal(0, invocation.CommandInstructions);
	}

	[Theory]
	[InlineData(nameof(RunCommandStackBridgeFixture.WrongArityEntry))]
	[InlineData(nameof(RunCommandStackBridgeFixture.WrongRegisterEntry))]
	[InlineData(nameof(RunCommandStackBridgeFixture.WrongReturnEntry))]
	[InlineData(nameof(RunCommandStackBridgeFixture.WideLengthEntry))]
	[InlineData(nameof(RunCommandStackBridgeFixture.MissingAbiEntry))]
	public void MalformedBridgeImportsAreRejected(string entry)
	{
		var error = Assert.Throws<M68kCompilationException>(() => Compile(
			M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.FixedPoint,
			M68kRuntimeProfile.Freestanding, entry));
		Assert.Contains("command stack bridge requires four native word arguments",
			error.Message);
	}

	private static M68kCompilationResult Compile(M68kCpuTarget target,
		M68kPeepholeOptimizationMode mode, M68kRuntimeProfile profile,
		string entry = nameof(RunCommandStackBridgeFixture.Entry)) =>
		AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(RunCommandStackBridgeFixture).Assembly.Location,
			EntryPoint = $"{typeof(RunCommandStackBridgeFixture).FullName}::{entry}",
			IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(APTR).Assembly.Location],
			Cpu = target,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = profile,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = mode
		});

	private static IM68kCore CreateCpu(M68kCpuModel model,
		M68kCompilationResult result, BridgeBus bus, Invocation invocation)
	{
		bus.Active = invocation;
		bus.WriteLong(invocation.OldPointer, ReturnSentinel);
		var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(LoadAddress + result.EntryPoint, invocation.OldPointer);
		for (var register = 0; register < 8; register++)
			cpu.State.D[register] = OriginalData(register, invocation.Index);
		for (var register = 0; register < 7; register++)
			cpu.State.A[register] = OriginalAddress(register, invocation.Index);
		cpu.State.D[0] = invocation.Context;
		return cpu;
	}

	private static void Step(IM68kCore cpu, BridgeBus bus, Invocation invocation)
	{
		bus.Active = invocation;
		var pc = cpu.State.ProgramCounter;
		Assert.True(bus.IsProgramCounter(pc, invocation),
			$"Command bridge escaped native code at ${pc:X8}.");
		if (pc >= invocation.Command && pc < invocation.CommandEnd)
		{
			invocation.CommandInstructions++;
			Assert.Equal(invocation.NewLower,
				bus.ReadLong(invocation.Task + ExecLayout.Task.StackLower));
			Assert.Equal(invocation.NewUpper,
				bus.ReadLong(invocation.Task + ExecLayout.Task.StackUpper));
		}
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.Halted,
			$"Command bridge halted at ${cpu.State.ProgramCounter:X8}.");
	}

	private static void AssertReturned(IM68kCore cpu, BridgeBus bus,
		Invocation invocation)
	{
		Assert.Equal(ReturnSentinel, cpu.State.ProgramCounter);
		Assert.Equal(invocation.Result, cpu.State.D[0]);
		Assert.Equal(invocation.OldPointer + 4, cpu.State.A[7]);
		for (var register = 2; register < 8; register++)
			Assert.Equal(OriginalData(register, invocation.Index), cpu.State.D[register]);
		for (var register = 2; register < 7; register++)
			Assert.Equal(OriginalAddress(register, invocation.Index), cpu.State.A[register]);
		Assert.Equal(invocation.Length, bus.ReadLong(invocation.Observed));
		Assert.Equal(invocation.Arguments, bus.ReadLong(invocation.Observed + 4));
		Assert.Equal(invocation.NewLower, bus.ReadLong(invocation.Swap));
		Assert.Equal(invocation.NewUpper, bus.ReadLong(invocation.Swap + 4));
		Assert.Equal(invocation.NewUpper, bus.ReadLong(invocation.Swap + 8));
		Assert.Equal(invocation.OldLower,
			bus.ReadLong(invocation.Task + ExecLayout.Task.StackLower));
		Assert.Equal(invocation.OldUpper,
			bus.ReadLong(invocation.Task + ExecLayout.Task.StackUpper));
		foreach (var start in new[] { invocation.OldLower - 16,
			invocation.OldUpper, invocation.NewLower - 16, invocation.NewUpper })
			Assert.All(bus.Memory.AsSpan((int)start, 16).ToArray(),
				value => Assert.Equal((byte)0xA5, value));
	}

	private static uint OriginalData(int register, int invocation) =>
		0xD000_1000u + (uint)register * 0x0010_0100u + (uint)invocation;
	private static uint OriginalAddress(int register, int invocation) =>
		0xA000_2000u + (uint)register * 0x0010_0100u + (uint)invocation;

	private sealed class Invocation(int index, uint newStackSize, uint length,
		uint result)
	{
		public int Index { get; } = index;
		public uint Context { get; } = 0x8000u + (uint)index * 0x100;
		public uint Swap => Context + 32;
		public uint Arguments => Context + 64;
		public uint Observed => Context + 96;
		public uint Task { get; } = 0xA000u + (uint)index * 0x100;
		public uint OldLower { get; } = 0x60000u + (uint)index * 0x8000;
		public uint OldUpper => OldLower + 0x4000;
		public uint OldPointer => OldUpper - 4;
		public uint NewLower { get; } = 0x90000u + (uint)index * 0x8000;
		public uint NewUpper => NewLower + newStackSize;
		public uint Command { get; } = 0x2000u + (uint)index * 0x200;
		public uint CommandEnd { get; set; }
		public uint Length { get; } = length;
		public uint Result { get; } = result;
		public int StackSwaps { get; set; }
		public int CommandInstructions { get; set; }
	}

	private sealed class BridgeBus : IM68kBus
	{
		private readonly TestBus _inner = new(0x0010_0000);
		private readonly uint _imageEnd;
		public Invocation? Active { get; set; }
		public byte[] Memory => _inner.Memory;
		public byte[] InitialImage { get; }

		public BridgeBus(M68kCompilationResult result)
		{
			Assert.Empty(result.FrameworkAnalysis.ManagedAllocationSites);
			result.Code.CopyTo(Memory.AsSpan((int)LoadAddress));
			_imageEnd = LoadAddress + (uint)result.Code.Length;
			foreach (var relocation in result.Relocations)
			{
				var address = LoadAddress + (uint)relocation.Offset;
				WriteLong(address, ReadLong(address) + LoadAddress);
			}
			InitialImage = Memory.AsSpan((int)LoadAddress, result.Code.Length).ToArray();
			WriteLong(4, ExecBase);
			_inner.RegisterGateway(StackSwapVector, SwapStack);
		}

		public void Prepare(Invocation invocation)
		{
			WriteLong(invocation.Context, invocation.Swap);
			WriteLong(invocation.Context + 4, invocation.Command);
			WriteLong(invocation.Context + 8, invocation.Length);
			WriteLong(invocation.Context + 12, invocation.Arguments);
			Memory.AsSpan((int)invocation.Arguments, 16).Fill(0x31);
			Memory[(int)(invocation.Arguments + invocation.Length - 1)] = 10;
			Memory[(int)(invocation.Arguments + invocation.Length)] = 0;
			WriteLong(invocation.Swap, invocation.NewLower);
			WriteLong(invocation.Swap + 4, invocation.NewUpper);
			WriteLong(invocation.Swap + 8, invocation.NewUpper);
			WriteLong(invocation.Task + ExecLayout.Task.StackLower, invocation.OldLower);
			WriteLong(invocation.Task + ExecLayout.Task.StackUpper, invocation.OldUpper);
			WriteLong(invocation.Task + ExecLayout.Task.StackPointer, invocation.OldPointer);
			foreach (var start in new[] { invocation.OldLower - 16,
				invocation.OldUpper, invocation.NewLower - 16, invocation.NewUpper })
				Memory.AsSpan((int)start, 16).Fill(0xA5);

			var position = invocation.Command;
			void Word(ushort value) { _inner.WriteWord(position, value); position += 2; }
			void Long(uint value) { WriteLong(position, value); position += 4; }
			Word(0x23C0); Long(invocation.Observed); // MOVE.L D0,observed
			Word(0x23C8); Long(invocation.Observed + 4); // MOVE.L A0,observed+4
			for (var register = 1; register < 8; register++)
			{
				Word((ushort)(0x203C | (register << 9))); // MOVE.L #value,Dn
				Long(0x1100_0000u + (uint)register * 0x10101);
			}
			for (var register = 0; register < 7; register++)
			{
				Word((ushort)(0x207C | (register << 9))); // MOVEA.L #value,An
				Long(0x2200_0000u + (uint)register * 0x10101);
			}
			Word(0x203C); Long(invocation.Result); // MOVE.L #result,D0
			Word(0x4E75); // RTS, with the command's original SP
			invocation.CommandEnd = position;
		}

		private void SwapStack(M68kCpuState cpu)
		{
			var invocation = Assert.IsType<Invocation>(Active);
			Assert.Equal(ExecBase, cpu.A[6]);
			Assert.Equal(invocation.Swap, cpu.A[0]);
			var newLower = ReadLong(invocation.Swap);
			var newUpper = ReadLong(invocation.Swap + 4);
			var newPointer = ReadLong(invocation.Swap + 8);
			var oldLower = ReadLong(invocation.Task + ExecLayout.Task.StackLower);
			var oldUpper = ReadLong(invocation.Task + ExecLayout.Task.StackUpper);
			Assert.InRange(cpu.A[7], oldLower, oldUpper - 4);
			Assert.InRange(newPointer, newLower + 4, newUpper);
			Assert.Equal(0u, newPointer & 1);
			var returnAddress = ReadLong(cpu.A[7]);
			WriteLong(invocation.Swap, oldLower);
			WriteLong(invocation.Swap + 4, oldUpper);
			WriteLong(invocation.Swap + 8, cpu.A[7] + 4);
			WriteLong(invocation.Task + ExecLayout.Task.StackLower, newLower);
			WriteLong(invocation.Task + ExecLayout.Task.StackUpper, newUpper);
			WriteLong(invocation.Task + ExecLayout.Task.StackPointer, newPointer);
			// Exec moves only the return address; it never copies a C# frame.
			WriteLong(newPointer - 4, returnAddress);
			cpu.A[7] = newPointer - 4;
			cpu.D[0] = 0xD0D0_D0D0;
			cpu.D[1] = 0xD1D1_D1D1;
			cpu.A[0] = 0xA0A0_A0A0;
			cpu.A[1] = 0xA1A1_A1A1;
			invocation.StackSwaps++;
		}

		public bool IsProgramCounter(uint address, Invocation invocation) =>
			Inside(address, 2, LoadAddress, _imageEnd) ||
			Inside(address, 2, invocation.Command, invocation.CommandEnd) ||
			address == StackSwapVector;

		private void Check(uint address, uint size, bool write,
			M68kBusAccessKind kind)
		{
			var invocation = Active ?? throw new InvalidOperationException("No active invocation.");
			if (Inside(address, size, invocation.OldLower, invocation.OldUpper) ||
				Inside(address, size, invocation.NewLower, invocation.NewUpper) ||
				Inside(address, size, invocation.Swap, invocation.Swap + 12) ||
				Inside(address, size, invocation.Observed, invocation.Observed + 8)) return;
			if (!write && (Inside(address, size, 4, 8) ||
				Inside(address, size, invocation.Context, invocation.Context + 128) ||
				Inside(address, size, invocation.Task, invocation.Task + 96) ||
				Inside(address, size, LoadAddress, _imageEnd + 8) ||
				Inside(address, size, invocation.Command, invocation.CommandEnd + 8) ||
				Inside(address, size, StackSwapVector, StackSwapVector + 16) ||
				Inside(address, size, ReturnSentinel, ReturnSentinel + 8))) return;
			throw new InvalidOperationException(
				$"Invocation {invocation.Index} {(write ? "write" : "read")} outside its ranges: ${address:X8}+{size}, {kind}.");
		}

		private static bool Inside(uint address, uint size, uint lower, uint upper) =>
			address >= lower && address <= upper && size <= upper - address;
		public uint ReadLong(uint address) => _inner.ReadLong(address);
		public void WriteLong(uint address, uint value) => _inner.WriteLong(address, value);
		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind)
		{ Check(address, 1, false, kind); return _inner.ReadByte(address, ref cycle, kind); }
		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind)
		{ Check(address, 2, false, kind); return _inner.ReadWord(address, ref cycle, kind); }
		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind)
		{ Check(address, 4, false, kind); return _inner.ReadLong(address, ref cycle, kind); }
		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind)
		{ Check(address, 1, true, kind); _inner.WriteByte(address, value, ref cycle, kind); }
		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind)
		{ Check(address, 2, true, kind); _inner.WriteWord(address, value, ref cycle, kind); }
		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind)
		{ Check(address, 4, true, kind); _inner.WriteLong(address, value, ref cycle, kind); }
		public bool HasHostGateway(uint address) => _inner.HasHostGateway(address);
		public bool TryInvokeHostGateway(uint pc, uint token, M68kCpuState state) =>
			_inner.TryInvokeHostGateway(pc, token, state);
		public void ResetExternalDevices(long cycle) { }
	}
}

public static class RunCommandStackBridgeFixture
{
	public static int Entry(int context, CONST_STRPTR unusedArguments)
	{
		var address = APTR.FromPointer((uint)context);
		return Invoke(new Request
		{
			StackSwap = APTR.ReadUInt32(address, 0),
			Entry = APTR.ReadUInt32(address, 4),
			Length = APTR.ReadUInt32(address, 8),
			Arguments = APTR.ReadUInt32(address, 12)
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Invoke(Request request)
	{
		return DosRunCommandCallbacks.ExecuteOnStack(
			APTR.FromPointer(request.StackSwap), APTR.FromPointer(request.Entry),
			request.Length, APTR.FromPointer(request.Arguments));
	}

	public static int OrdinaryEntry(int context, CONST_STRPTR unusedArguments)
	{
		var address = APTR.FromPointer((uint)context);
		return InvokeOrdinary(new Request
		{
			StackSwap = APTR.ReadUInt32(address, 0),
			Entry = APTR.ReadUInt32(address, 4),
			Length = APTR.ReadUInt32(address, 8),
			Arguments = APTR.ReadUInt32(address, 12)
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int InvokeOrdinary(Request request)
	{
		// Before-fix control: this reproduces the ordinary-call shape in the
		// existing native DOS callback without depending on CopperStart builds.
		var stack = APTR.FromPointer(request.StackSwap);
		var entry = APTR.FromPointer(request.Entry);
		StackSwap(stack, APTR.FromPointer(0x4000));
		var result = DosRunCommandCallbacks.Execute(entry, request.Length,
			APTR.FromPointer(request.Arguments));
		StackSwap(stack, APTR.FromPointer(0x4000));
		return result;
	}

	[AmigaLibrary("exec.library", AmigaLibraryBasePolicy.CallerProvided)]
	[AmigaLvo(ExecLvo.StackSwap)]
	private static extern void StackSwap(
		[M68kRegister(M68kRegister.A0)] APTR stack,
		[M68kRegister(M68kRegister.A6)] APTR execBase);

	public static int WrongArityEntry() => WrongArity(0, 0, 0);
	public static int WrongRegisterEntry() => WrongRegister(0, 0, 0, 0);
	public static int WrongReturnEntry() => WrongReturn(0, 0, 0, 0);
	public static int WideLengthEntry() => WideLength(0, 0, 1, 0);
	public static int MissingAbiEntry() => MissingAbi(0, 0, 0, 0);

	[M68kImport("intrinsic:amiga-run-command-on-stack")]
	[return: M68kRegister(M68kRegister.D0)]
	private static extern int WrongArity(
		[M68kRegister(M68kRegister.A0)] uint stack,
		[M68kRegister(M68kRegister.A3)] uint entry,
		[M68kRegister(M68kRegister.D0)] uint length);

	[M68kImport("intrinsic:amiga-run-command-on-stack")]
	[return: M68kRegister(M68kRegister.D0)]
	private static extern int WrongRegister(
		[M68kRegister(M68kRegister.A0)] uint stack,
		[M68kRegister(M68kRegister.D1)] uint entry,
		[M68kRegister(M68kRegister.D0)] uint length,
		[M68kRegister(M68kRegister.A1)] uint arguments);

	[M68kImport("intrinsic:amiga-run-command-on-stack")]
	[return: M68kRegister(M68kRegister.D1)]
	private static extern int WrongReturn(
		[M68kRegister(M68kRegister.A0)] uint stack,
		[M68kRegister(M68kRegister.A3)] uint entry,
		[M68kRegister(M68kRegister.D0)] uint length,
		[M68kRegister(M68kRegister.A1)] uint arguments);

	[M68kImport("intrinsic:amiga-run-command-on-stack")]
	[return: M68kRegister(M68kRegister.D0)]
	private static extern int WideLength(
		[M68kRegister(M68kRegister.A0)] uint stack,
		[M68kRegister(M68kRegister.A3)] uint entry,
		[M68kRegister(M68kRegister.D0)] ulong length,
		[M68kRegister(M68kRegister.A1)] uint arguments);

	[M68kImport("intrinsic:amiga-run-command-on-stack")]
	private static extern int MissingAbi(uint stack, uint entry, uint length,
		uint arguments);

	private struct Request
	{
		public uint StackSwap;
		public uint Entry;
		public uint Length;
		public uint Arguments;
	}
}
