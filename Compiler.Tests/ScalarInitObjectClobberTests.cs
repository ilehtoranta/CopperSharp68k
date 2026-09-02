using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class ScalarInitObjectClobberTests
{
	private static readonly string FixtureName = typeof(ScalarInitObjectClobberFixtures).FullName!;
	private static readonly string FixtureAssembly = typeof(ScalarInitObjectClobberFixtures).Assembly.Location;

	[Theory]
	[InlineData(nameof(ScalarInitObjectClobberFixtures.Initialize1), 1)]
	[InlineData(nameof(ScalarInitObjectClobberFixtures.Initialize2), 2)]
	[InlineData(nameof(ScalarInitObjectClobberFixtures.Initialize4), 4)]
	[InlineData(nameof(ScalarInitObjectClobberFixtures.Initialize8), 8)]
	public void RealInitobjDeclaresItsZeroMaterializationScratchRegister(string name, int width)
	{
		using var module = new CompilationModule(FixtureAssembly);
		var method = module.ResolveEntryPoint($"{FixtureName}::{name}");
		var source = Assert.Single(method.Instructions, instruction => instruction.OpCode == OpCodes.Initobj);
		var type = module.ResolveTypeToken((int)source.Operand!, method, source.Offset);
		Assert.True(module.TryGetIndirectInitializeLayout(type, method.ModuleName, out var layout));
		Assert.Equal(width, layout.Size);

		var function = CilMachineIrBuilder.Build(method, module);
		var initialization = Assert.Single(function.Blocks.SelectMany(static block => block.Instructions),
			instruction => instruction.SourceInstruction?.OpCode == OpCodes.Initobj &&
				(instruction.MemoryEffect & M68kMachineMemoryEffect.Write) != 0);
		Assert.True(initialization.Clobbers.Contains(M68kRegister.D0));
		if (width <= 4)
			Assert.NotEqual(M68kMachineOperation.AggregateIndirectInitialize, initialization.Operation);
		else
			Assert.Equal(M68kMachineOperation.AggregateIndirectInitialize, initialization.Operation);
		Assert.Contains(function.Blocks.SelectMany(static block => block.Instructions),
			instruction => instruction.Operation == M68kMachineOperation.Call &&
				instruction.IlOffset > initialization.IlOffset);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68020, M68kPeepholeOptimizationMode.Disabled)]
	public void InitializationPreservesLiveCountAndPointersUntilTheirLaterCall(
		M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"{FixtureName}::Entry",
			IncludedExportNames = [],
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = mode
		});
		var copy = Assert.Single(result.Symbols,
			symbol => symbol.Name == $"{FixtureName}::CopyWithInitializedState");
		const uint load = 0x10000, stackTop = 0x80000, sentinel = 0x1000;
		const uint sourceBase = 0x30000, destinationBase = 0x31000, lowStackGuard = 0x78000;
		foreach (var width in new[] { 1, 2, 4, 8 })
		foreach (var remainder in new uint[] { 0, 2 })
		foreach (var count in new uint[] { 0, 1, 17, 64 })
		{
			var wrapper = Assert.Single(result.Symbols,
				symbol => symbol.Name == $"{FixtureName}::Initialize{width}");
			var stack = stackTop + remainder;
			// Odd byte addresses also expose accidental pointer rounding or a
			// count/pointer register mix-up; the initialized local stays aligned.
			var source = sourceBase + remainder + 1;
			var destination = destinationBase + remainder + 3;
			var bus = new TestBus();
			bus.Memory.AsSpan((int)lowStackGuard, (int)(stack + 36 - lowStackGuard)).Fill(0xa5);
			bus.Memory.AsSpan((int)source - 8, 80).Fill(0xb7);
			bus.Memory.AsSpan((int)destination - 8, 80).Fill(0xc9);
			for (var index = 0; index < 64; index++)
				bus.Memory[(int)source + index] = (byte)(0x23 + index * 13);
			var expectedSource = bus.Memory.AsSpan((int)source - 8, 80).ToArray();
			var expectedDestination = bus.Memory.AsSpan((int)destination - 8, 80).ToArray();
			bus.Memory.AsSpan((int)source, (int)count).CopyTo(expectedDestination.AsSpan(8));
			result.Code.CopyTo(bus.Memory.AsSpan((int)load));
			foreach (var relocation in result.Relocations)
			{
				var address = load + (uint)relocation.Offset;
				bus.WriteLong(address, bus.ReadLong(address) + load);
			}
			var expectedCode = bus.Memory.AsSpan((int)load, result.Code.Length).ToArray();
			bus.WriteLong(stack, sentinel);
			using var cpu = M68kCoreFactory.Default.Create(
				target == M68kCpuTarget.M68000 ? M68kCpuModel.M68000 : M68kCpuModel.M68020, bus);
			cpu.Reset(load + wrapper.Address, stack);
			cpu.State.A[0] = source;
			cpu.State.A[1] = destination;
			cpu.State.D[0] = count;
			for (var register = 2; register <= 7; register++)
				cpu.State.D[register] = (uint)(0x12340000 + register);
			for (var register = 2; register <= 6; register++)
				cpu.State.A[register] = (uint)(0x40000 + register * 256);

			var checkedCall = false;
			for (var step = 0; step < 50_000 && cpu.State.ProgramCounter != sentinel; step++)
			{
				if (cpu.State.ProgramCounter == load + copy.Address)
				{
					Assert.False(checkedCall);
					checkedCall = true;
					// The noinline callee receives state in A0, source in A1,
					// destination in D0, count in D1, and width on the stack.
					Assert.Equal(source, cpu.State.A[1]);
					Assert.Equal(destination, cpu.State.D[0]);
					Assert.Equal(count, cpu.State.D[1]);
					Assert.Equal((uint)width, bus.ReadLong(cpu.State.A[7] + 4));
					Assert.InRange(cpu.State.A[0], cpu.State.A[7] + 8, stack - (uint)width);
					Assert.All(bus.Memory.AsSpan((int)cpu.State.A[0], width).ToArray(),
						value => Assert.Equal((byte)0, value));
				}
				cpu.ExecuteInstruction();
				Assert.False(cpu.State.Halted,
					$"{target}/{mode}/width={width}/count={count}/SP+{remainder}: halted at {cpu.State.ProgramCounter:X8}.");
			}

			Assert.True(checkedCall);
			Assert.Equal(sentinel, cpu.State.ProgramCounter);
			Assert.Equal(count ^ (uint)width ^ source ^ destination, cpu.State.D[0]);
			Assert.Equal(stack + 4, cpu.State.A[7]);
			for (var register = 2; register <= 7; register++)
				Assert.Equal((uint)(0x12340000 + register), cpu.State.D[register]);
			for (var register = 2; register <= 6; register++)
				Assert.Equal((uint)(0x40000 + register * 256), cpu.State.A[register]);
			Assert.Equal(expectedSource, bus.Memory.AsSpan((int)source - 8, 80).ToArray());
			Assert.Equal(expectedDestination, bus.Memory.AsSpan((int)destination - 8, 80).ToArray());
			Assert.Equal(expectedCode, bus.Memory.AsSpan((int)load, result.Code.Length).ToArray());
			Assert.Equal(sentinel, bus.ReadLong(stack));
			Assert.All(bus.Memory.AsSpan((int)stack + 4, 32).ToArray(), value => Assert.Equal((byte)0xa5, value));
			Assert.All(bus.Memory.AsSpan((int)lowStackGuard, 32).ToArray(), value => Assert.Equal((byte)0xa5, value));
		}
	}
}

public static unsafe class ScalarInitObjectClobberFixtures
{
	// Fixed buffers give exact one/two-byte layouts; ordinary narrow managed
	// fields occupy four bytes in this compiler and would not cover those stores.
	public struct State1 { public fixed byte Bytes[1]; }
	public struct State2 { public fixed byte Bytes[2]; }
	public struct State4 { public uint Value; }
	public struct State8 { public uint First; public uint Second; }

	public static uint Entry() =>
		Initialize1((byte*)0x30001, (byte*)0x31003, 17) ^
		Initialize2((byte*)0x30001, (byte*)0x31003, 17) ^
		Initialize4((byte*)0x30001, (byte*)0x31003, 17) ^
		Initialize8((byte*)0x30001, (byte*)0x31003, 17);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Initialize1(byte* source, byte* destination, uint count)
	{
		State1 state = default;
		return CopyWithInitializedState((byte*)&state, source, destination, count, 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Initialize2(byte* source, byte* destination, uint count)
	{
		State2 state = default;
		return CopyWithInitializedState((byte*)&state, source, destination, count, 2);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Initialize4(byte* source, byte* destination, uint count)
	{
		State4 state = default;
		return CopyWithInitializedState((byte*)&state, source, destination, count, 4);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Initialize8(byte* source, byte* destination, uint count)
	{
		State8 state = default;
		return CopyWithInitializedState((byte*)&state, source, destination, count, 8);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CopyWithInitializedState(
		byte* state, byte* source, byte* destination, uint count, uint width)
	{
		for (uint index = 0; index < width; index++)
			if (state[index] != 0) return 0xbad00000 | index;
		for (uint index = 0; index < count; index++)
			destination[index] = source[index];
		return count ^ width ^ (uint)source ^ (uint)destination;
	}
}
