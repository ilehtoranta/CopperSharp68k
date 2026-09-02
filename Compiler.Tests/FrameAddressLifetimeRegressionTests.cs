using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class FrameAddressLifetimeRegressionTests
{
	private static readonly string FixtureName = typeof(FrameAddressLifetimeFixtures).FullName!;
	private static readonly string FixtureAssembly = typeof(FrameAddressLifetimeFixtures).Assembly.Location;

	[Fact]
	public void StackArgumentReturnedAsRawPointerKeepsTheOriginalFrameDependency()
	{
		using var module = new CompilationModule(FixtureAssembly);
		var method = module.ResolveEntryPoint($"{FixtureName}::Scenario");
		var carrier = module.ResolveEntryPoint($"{FixtureName}::CarryStackPointer");
		var checker = module.ResolveEntryPoint($"{FixtureName}::CheckAfterStackWrite");
		var function = CilMachineIrBuilder.Build(method, module);
		var call = Assert.Single(function.Blocks.SelectMany(static block => block.Instructions),
			instruction => instruction.LogicalCall?.ResolvedTargets.Contains(carrier.Identity) == true);
		var checkerCall = Assert.Single(function.Blocks.SelectMany(static block => block.Instructions),
			instruction => instruction.LogicalCall?.ResolvedTargets.Contains(checker.Identity) == true);
		var frameValues = M68kFrameAddressLifetime.FindDependentValues(function);

		// Four scalar arguments consume D0/D1/A0/A1. The only frame pointer is
		// the fifth, stack-passed argument: it is absent from physical Call.Uses.
		Assert.Equal(4, call.Uses.Length);
		Assert.Equal(5, call.LogicalCall!.ArgumentValueIds.Length);
		Assert.DoesNotContain(call.Uses, frameValues.Contains);
		Assert.Contains(call.LogicalCall.ArgumentValueIds, frameValues.Contains);
		Assert.Contains(function.Blocks.SelectMany(static block => block.Instructions),
			instruction => instruction.Operation == M68kMachineOperation.OutgoingArgumentPush &&
				instruction.ArgumentIndex == 4 && instruction.Uses.Any(frameValues.Contains));

		M68kCallAbiLowering.FinalizeLogicalCalls(function);
		var allocated = M68kRegisterAllocatorPipeline.Run(function, allowUntrackedManagedByrefs: true);
		var finalCall = Assert.Single(allocated.Function.Blocks.SelectMany(static block => block.Instructions),
			instruction => instruction.Id == checkerCall.Id);
		Assert.Null(finalCall.LogicalCall);
		Assert.True(finalCall.RequiresLiveCallerFrame);
	}

	[Theory]
	[InlineData(M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kPeepholeOptimizationMode.Disabled)]
	public void RawPointerReturnedThroughStackArgumentRemainsLiveAtTailPositionChecker(
		M68kPeepholeOptimizationMode mode)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"{FixtureName}::Entry",
			IncludedExportNames = [],
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = mode
		});
		var scenario = Assert.Single(result.Symbols, symbol => symbol.Name == $"{FixtureName}::Scenario");
		var carrier = Assert.Single(result.Symbols, symbol => symbol.Name == $"{FixtureName}::CarryStackPointer");
		var checker = Assert.Single(result.Symbols, symbol => symbol.Name == $"{FixtureName}::CheckAfterStackWrite");
		const uint load = 0x10000, stackTop = 0x80000, sentinel = 0x1000;
		foreach (var remainder in new uint[] { 0, 2 })
		{
			var stack = stackTop + remainder;
			var bus = new TestBus();
			bus.Memory.AsSpan((int)stack - 4096, 4096).Fill(0xa5);
			result.Code.CopyTo(bus.Memory.AsSpan((int)load));
			foreach (var relocation in result.Relocations)
			{
				var address = load + (uint)relocation.Offset;
				bus.WriteLong(address, bus.ReadLong(address) + load);
			}
			bus.WriteLong(stack, sentinel);
			using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
			cpu.Reset(load + scenario.Address, stack);
			cpu.State.D[0] = 42;
			for (var register = 2; register <= 7; register++)
				cpu.State.D[register] = (uint)(0x12340000 + register);
			for (var register = 2; register <= 6; register++)
				cpu.State.A[register] = (uint)(0x40000 + register * 256);

			uint? originalPointer = null;
			var checkedPointer = false;
			for (var step = 0; step < 20_000 && cpu.State.ProgramCounter != sentinel; step++)
			{
				if (cpu.State.ProgramCounter == load + carrier.Address)
				{
					Assert.Null(originalPointer);
					originalPointer = bus.ReadLong(cpu.State.A[7] + 4);
					Assert.InRange(originalPointer.Value, cpu.State.A[7] + 4, stack - 4);
					Assert.Equal(42u, bus.ReadLong(originalPointer.Value));
				}
				if (cpu.State.ProgramCounter == load + checker.Address)
				{
					Assert.NotNull(originalPointer);
					Assert.Equal(originalPointer.GetValueOrDefault(), cpu.State.A[0]);
					// On an invalid tail jump, the caller frame was released and the
					// pointer lies below SP. The checker's own stack write can reuse it.
					Assert.InRange(cpu.State.A[0], cpu.State.A[7] + 4, stack - 4);
					checkedPointer = true;
				}
				cpu.ExecuteInstruction();
				Assert.False(cpu.State.Halted,
					$"{mode}/SP+{remainder}: halted at {cpu.State.ProgramCounter:X8}.");
			}
			Assert.True(checkedPointer);
			Assert.Equal(sentinel, cpu.State.ProgramCounter);
			Assert.Equal(42u, cpu.State.D[0]);
			Assert.Equal(stack + 4, cpu.State.A[7]);
			for (var register = 2; register <= 7; register++)
				Assert.Equal((uint)(0x12340000 + register), cpu.State.D[register]);
			for (var register = 2; register <= 6; register++)
				Assert.Equal((uint)(0x40000 + register * 256), cpu.State.A[register]);
		}
	}
}

public static class FrameAddressLifetimeFixtures
{
	public static uint Entry() => Scenario(42);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint Scenario(uint seed)
	{
		var value = seed;
		var pointer = CarryStackPointer(seed, seed + 1, seed + 2, seed + 3, &value);
		return CheckAfterStackWrite(pointer);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint* CarryStackPointer(uint first, uint second, uint third, uint fourth, uint* pointer)
	{
		if ((first ^ second ^ third ^ fourth) == uint.MaxValue) return pointer + 1;
		return pointer;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint CheckAfterStackWrite(uint* pointer)
	{
		var guard = 0xdeadbeefu;
		Mutate(&guard);
		return *pointer ^ guard ^ 0xdeadbeecu;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe void Mutate(uint* pointer) => *pointer ^= 3;
}
