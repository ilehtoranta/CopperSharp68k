using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class FramePreservationEmissionTests
{
	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.FixedPoint)]
	public void EmissionPreservesRealRegistersStackArgumentsAndArgumentSideEffects(
		M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
	{
		foreach (var entry in new[]
		{
			nameof(FramePreservationFixtures.EmptyWithSideEffect),
			nameof(FramePreservationFixtures.NoInlineEmptyWithSideEffect),
			nameof(FramePreservationFixtures.StoreAndRead),
			nameof(FramePreservationFixtures.StackArithmetic)
		})
		{
			var result = Compile(entry, target, mode);
			var bus = Load(result);
			using var cpu = M68kCoreFactory.Default.Create(model, bus);
			Run(cpu, bus, LoadAddress + result.EntryPoint);
			Assert.Equal(42u, cpu.State.D[0]);
			if (entry == nameof(FramePreservationFixtures.EmptyWithSideEffect))
			{
				var empty = Assert.Single(result.Symbols, symbol => symbol.Name.Contains("::EmptyLeaf<", StringComparison.Ordinal));
				Assert.Equal(new byte[] { 0x4e, 0x75 }, result.Code.AsSpan((int)empty.Address, empty.Size).ToArray());
				Run(cpu, bus, LoadAddress + empty.Address, seedArguments: true);
				AssertPreserved(cpu);
			}
			if (entry == nameof(FramePreservationFixtures.StoreAndRead))
			{
				var store = Assert.Single(result.Symbols, symbol => symbol.Name.Contains("::StoreItem<", StringComparison.Ordinal));
				bus.Memory.AsSpan(0x61000, 256).Fill(0xa5);
				Run(cpu, bus, LoadAddress + store.Address, seedArguments: true);
				Assert.Equal(0x89ab_cdefu, bus.ReadLong(0x61000 + 48));
				Assert.All(bus.Memory.AsSpan(0x61000, 48).ToArray(), value => Assert.Equal(0xa5, value));
				Assert.All(bus.Memory.AsSpan(0x61000 + 52, 204).ToArray(), value => Assert.Equal(0xa5, value));
				AssertPreserved(cpu);
			}
			if (entry == nameof(FramePreservationFixtures.StackArithmetic))
			{
				var arithmetic = Assert.Single(result.Symbols, symbol => symbol.Name.EndsWith("::StackAndConstants", StringComparison.Ordinal));
				Run(cpu, bus, LoadAddress + arithmetic.Address, stackArguments: true);
				Assert.Equal(42u, cpu.State.D[0]);
				Assert.Equal(5u, bus.ReadLong(StackAddress + 4));
				Assert.Equal(12u, bus.ReadLong(StackAddress + 8));
				AssertPreserved(cpu);
			}
		}
	}

	[Fact]
	public void SelectedConstantMultiplyAndAddDoNotSaveUnusedD3OrD5()
	{
		var result = Compile(nameof(FramePreservationFixtures.StoreAndRead), M68kCpuTarget.M68000,
			M68kPeepholeOptimizationMode.Disabled);
		var store = Assert.Single(result.Symbols, symbol => symbol.Name.Contains("::StoreItem<", StringComparison.Ordinal));
		var bytes = result.Code.AsSpan((int)store.Address, store.Size);
		var assembler = new M68kAssembler();
		for (var offset = 0; offset < bytes.Length; offset += 2)
			assembler.EmitWord(System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]));
		var savedData = assembler.GetInstructionStream().Select(M68kInstructionDataflow.GetEffects)
			.Where(effect => effect.StackDelta < 0 && (effect.WritesMemory & M68kMemorySet.Stack) != 0)
			.Aggregate(0, (mask, effect) => mask | effect.UsesData);
		Assert.Equal(0, savedData & ((1 << 3) | (1 << 5)));
		Assert.NotEqual(0, savedData & (1 << 2)); // The selected multiply really writes D2.
	}

	[Fact]
	public void NoInliningOnAnEmptyConstrainedImplementationRetainsTheLogicalCall()
	{
		using var module = new CompilationModule(Assembly.GetExecutingAssembly().Location);
		var entry = module.ResolveEntryPoint($"{typeof(FramePreservationFixtures).FullName}::{nameof(FramePreservationFixtures.NoInlineEmptyWithSideEffect)}");
		var helperCall = entry.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call &&
			module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition?.Name == "EmptyLeaf");
		var helper = module.ResolveMethodToken((int)helperCall.Operand!, entry, helperCall.Offset).Definition!;
		var function = CilMachineIrBuilder.Build(helper, module);
		Assert.Single(function.Blocks.SelectMany(block => block.Instructions), instruction => instruction.Operation == M68kMachineOperation.Call);
	}

	[Fact]
	public void SuppressedLocationsNeverRemoveLiveArgumentsOrRealExternalClobbers()
	{
		var function = new M68kMachineFunction("preservation", 0) { HasDynamicStackAllocation = true };
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var constant = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		var incoming = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Constant, 0, definitions: [constant.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Call, 1, uses: [incoming.Id],
			clobbers: M68kRegisterSet.From(M68kRegister.D3)));
		var allocation = new M68kAllocationResult(new Dictionary<int, M68kAllocatedLocation>
		{
			[constant.Id] = new(M68kRegister.D5, false),
			[incoming.Id] = new(M68kRegister.D4, false)
		}, new HashSet<int>());
		var registers = M68kAllocatedPreservationAnalysis.RequiredRegisters(function, allocation,
			[M68kRegister.D3, M68kRegister.D4, M68kRegister.D5, M68kRegister.A5],
			new HashSet<int> { constant.Id }, new Dictionary<int, M68kRegisterSet>(), []);
		Assert.Equal([M68kRegister.D3, M68kRegister.D4, M68kRegister.A5], registers);
	}

	private const uint LoadAddress = 0x10000, StackAddress = 0x80000, Sentinel = 0x1000;

	private static M68kCompilationResult Compile(string entry, M68kCpuTarget target, M68kPeepholeOptimizationMode mode) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(FramePreservationFixtures).FullName}::{entry}", Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly, RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = mode, IncludedExportNames = []
		});

	private static TestBus Load(M68kCompilationResult result)
	{
		var bus = new TestBus();
		bus.Memory.AsSpan((int)StackAddress - 8192, 8192).Fill(0xa5);
		result.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = LoadAddress + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		return bus;
	}

	private static void Run(IM68kCore cpu, TestBus bus, uint entry, bool seedArguments = false, bool stackArguments = false)
	{
		bus.WriteLong(StackAddress, Sentinel);
		cpu.Reset(entry, StackAddress);
		for (var register = 2; register <= 7; register++) cpu.State.D[register] = (uint)(0x12340000 + register);
		for (var register = 2; register <= 6; register++) cpu.State.A[register] = (uint)(0x40000 + register * 256);
		if (seedArguments)
		{
			cpu.State.A[0] = 0x62000;
			cpu.State.A[1] = 0x61000;
			cpu.State.D[0] = 11;
			cpu.State.D[1] = 0x89ab_cdef;
		}
		if (stackArguments)
		{
			cpu.State.D[0] = 3; cpu.State.D[1] = 2;
			cpu.State.A[0] = 3; cpu.State.A[1] = 4;
			bus.WriteLong(StackAddress + 4, 5); bus.WriteLong(StackAddress + 8, 12);
		}
		for (var step = 0; step < 20_000 && cpu.State.ProgramCounter != Sentinel; step++)
		{
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted, $"Halted at {cpu.State.ProgramCounter:X8}.");
		}
		Assert.Equal(Sentinel, cpu.State.ProgramCounter);
		Assert.Equal(StackAddress + 4, cpu.State.A[7]);
	}

	private static void AssertPreserved(IM68kCore cpu)
	{
		for (var register = 2; register <= 7; register++) Assert.Equal((uint)(0x12340000 + register), cpu.State.D[register]);
		for (var register = 2; register <= 6; register++) Assert.Equal((uint)(0x40000 + register * 256), cpu.State.A[register]);
	}
}

public static class FramePreservationFixtures
{
	public interface IWords
	{
		void Cache(uint operation, APTR address, uint byteCount);
		void Write32(APTR address, uint offset, uint value);
	}
	public struct Words : IWords
	{
		public uint Tag;
		public void Cache(uint operation, APTR address, uint byteCount) { }
		public void Write32(APTR address, uint offset, uint value) => APTR.WriteUInt32(address, (int)offset, value);
	}
	public struct NoInlineWords : IWords
	{
		public uint Tag;
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void Cache(uint operation, APTR address, uint byteCount) { }
		public void Write32(APTR address, uint offset, uint value) => APTR.WriteUInt32(address, (int)offset, value);
	}
	private static uint _argumentEffects;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint EvaluateCount() { _argumentEffects += 42; return 11; }
	public static uint EmptyWithSideEffect()
	{
		Words words = default; _argumentEffects = 0;
		EmptyLeaf(ref words, 1, APTR.FromPointer(0x61000), EvaluateCount());
		return _argumentEffects;
	}
	public static uint NoInlineEmptyWithSideEffect()
	{
		NoInlineWords words = default; _argumentEffects = 0;
		EmptyLeaf(ref words, 1, APTR.FromPointer(0x61000), EvaluateCount());
		return _argumentEffects;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void EmptyLeaf<T>(ref T words, uint operation, APTR address, uint byteCount) where T : struct, IWords =>
		words.Cache(operation, address, byteCount);
	public static uint StoreAndRead()
	{
		Words words = default;
		StoreItem(ref words, APTR.FromPointer(0x61000), 11, APTR.FromPointer(42));
		return APTR.ReadUInt32(APTR.FromPointer(0x61000), 48);
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void StoreItem<T>(ref T words, APTR data, uint index, APTR value) where T : struct, IWords =>
		words.Write32(data, 4 + index * 4, value.Raw);
	public static uint StackArithmetic() => StackAndConstants(3, 2, 3, 4, 5, 12);
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint StackAndConstants(uint a, uint b, uint c, uint d, uint e, uint f) => a * 4 + b + c + d + e + f + 4;
}
