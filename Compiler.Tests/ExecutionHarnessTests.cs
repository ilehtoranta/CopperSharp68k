using System.Text.Json;
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[InlineData(M68kCpuModel.M68000)]
	[InlineData(M68kCpuModel.M68020)]
	[InlineData(M68kCpuModel.M68040)]
	public void CountedExecutionPreservesObservedStateAndInstructionBudget(M68kCpuModel model)
	{
		TestBus Program()
		{
			var bus = new TestBus(checked((int)StackPointer + 16));
			bus.WriteWord(HunkLoadAddress, 0x7028); // MOVEQ #40,D0
			bus.WriteWord(HunkLoadAddress + 2, 0x5280); // ADDQ.L #1,D0
			bus.WriteWord(HunkLoadAddress + 4, 0x5280);
			bus.WriteWord(HunkLoadAddress + 6, 0x4E75); // RTS
			return bus;
		}
		string Snapshot(M68kCpuState state) => JsonSerializer.Serialize(new {
			state.D, state.A, state.ProgramCounter, state.StatusRegister, state.Cycles, state.NativeCycles,
			state.Halted, state.Stopped, state.LastOpcode, state.LastInstructionProgramCounter
		});
		var countedBus = Program();
		var observedBus = Program();
		string? countedState = null, observedState = null;
		var observations = 0;
		Assert.Equal(42u, ExecuteCounted(countedBus, model, HunkLoadAddress, out var counted,
			afterReturn: state => countedState = Snapshot(state), maxInstructions: 5));
		Assert.Equal(42u, ExecuteCounted(observedBus, model, HunkLoadAddress, out var observed,
			beforeInstruction: (_, _) => observations++,
			afterReturn: state => observedState = Snapshot(state), maxInstructions: 5));
		Assert.Equal(4, counted);
		Assert.Equal(counted, observed);
		Assert.Equal(counted, observations);
		Assert.Equal(countedState, observedState);
		Assert.Equal(countedBus.Memory, observedBus.Memory);
		foreach (var observe in new[] { false, true })
		{
			var error = Assert.Throws<Xunit.Sdk.XunitException>(() => ExecuteCounted(Program(), model,
				HunkLoadAddress, out _, beforeInstruction: observe ? (_, _) => { } : null, maxInstructions: 4));
			Assert.Contains("did not return after 4 instructions", error.Message);
		}
	}
}

public sealed class ExecutionBusTests
{
	[Fact]
	public void GatewayRangeRetainsEndpointsGapsAndCallbacks()
	{
		var bus = new TestBus(0x1000);
		var state = new M68kCpuState();
		Assert.False(bus.HasHostGateway(0));
		Assert.False(bus.HasHostGateway(uint.MaxValue));
		Assert.False(bus.TryInvokeHostGateway(0, 1, state));
		bus.RegisterGateway(0x400, cpu => cpu.D[0]++);
		bus.RegisterGateway(0x200, cpu => cpu.D[1]++);
		foreach (var address in new uint[] { 0, 0x1FF, 0x300, 0x401, uint.MaxValue })
		{
			Assert.False(bus.HasHostGateway(address));
			Assert.False(bus.TryInvokeHostGateway(address, 1, state));
		}
		foreach (var address in new uint[] { 0x200, 0x400 })
		{
			Assert.True(bus.HasHostGateway(address));
			Assert.True(bus.TryInvokeHostGateway(address, 1, state));
			Assert.Equal(0xFF00, bus.ReadWord(address));
			Assert.Equal(1u, bus.ReadLong(address + 2));
		}
		Assert.Equal(1u, state.D[0]);
		Assert.Equal(1u, state.D[1]);
		Assert.Throws<ArgumentException>(() => bus.RegisterGateway(0x200, _ => throw new Exception()));
		Assert.True(bus.TryInvokeHostGateway(0x200, 1, state));
		Assert.Equal(2u, state.D[1]);
	}
}
