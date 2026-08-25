/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NestedAddressReturnChainTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;

	[Fact]
	public void NestedGenericAddressReturnMatchesManagedOnMc68000()
	{
		Assert.Equal(10u, NestedAddressReturnChainFixtures.ManagedEntry());
		Assert.Equal(10u, Execute(Compile()));
	}

	private static uint Execute(M68kCompilationResult compilation)
	{
		var bus = new TestBus();
		compilation.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in compilation.Relocations)
		{
			var address = LoadAddress + unchecked((uint)relocation.Offset);
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		bus.WriteLong(StackPointer, ReturnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(LoadAddress + compilation.EntryPoint, StackPointer);
		for (var instruction = 0; instruction < 200_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
				return cpu.State.D[0];
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"MC68000 halted at ${cpu.State.ProgramCounter:X8}, opcode " +
				$"${cpu.State.LastOpcode:X4}.");
		}
		Assert.Fail($"MC68000 did not return; PC=${cpu.State.ProgramCounter:X8}.");
		return 0;
	}

	private static M68kCompilationResult Compile() =>
		AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath =
				typeof(NestedAddressReturnChainFixtures).Assembly.Location,
			EntryPoint =
				"CopperSharp.Compiler.Tests.NestedAddressReturnChainFixtures::NativeEntry",
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
