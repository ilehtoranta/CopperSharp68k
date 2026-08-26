/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed class FinalizerExecutionTests
{
	private const uint HunkLoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;
	private const uint ShutdownSignalAddress = 0x0000_3000;
	private const string FixtureType =
		"CopperSharp.Compiler.Tests.FinalizerExecutionFixtures";
	private static readonly string FixtureAssembly =
		typeof(FinalizerExecutionFixtures).Assembly.Location;

	public static TheoryData<M68kCpuTarget, M68kCpuModel> CpuTargets =>
		new()
		{
			{ M68kCpuTarget.M68000, M68kCpuModel.M68000 },
			{ M68kCpuTarget.M68020, M68kCpuModel.M68020 },
			{ M68kCpuTarget.M68040, M68kCpuModel.M68040 }
		};

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void UnreachableObjectFinalizesOnEverySupportedCpu(
		M68kCpuTarget target,
		M68kCpuModel model) =>
		Assert.Equal(1u, Execute(Compile("FinalizesUnreachableEntry", target), model).Result);

	[Theory]
	[InlineData("PreservesFinalizerGraphEntry", 42u)]
	[InlineData("SuppressesFinalizerEntry", 0u)]
	[InlineData("CountsReregistrationEntry", 3u)]
	[InlineData("ResurrectsAndFinalizesAgainEntry", 2u)]
	[InlineData("NestedCollectionEntry", 2u)]
	[InlineData("ThrowingFinalizerDoesNotStopDrainEntry", 2u)]
	public void FinalizerSemanticsExecute(string entry, uint expected) =>
		Assert.Equal(expected, Execute(Compile(entry), M68kCpuModel.M68000).Result);

	[Fact]
	public void RootedObjectFinalizesDuringNormalExitDrain()
	{
		var execution = Execute(Compile("RootedObjectWaitsUntilExitEntry"), M68kCpuModel.M68000);

		Assert.Equal(0u, execution.Result);
		Assert.Equal(42u, execution.Bus.ReadLong(ShutdownSignalAddress));
	}

	[Fact]
	public void SuppressedRootIsSkippedDuringNormalExitDrain()
	{
		var execution = Execute(Compile("SuppressedRootDoesNotFinalizeAtExitEntry"), M68kCpuModel.M68000);

		Assert.Equal(42u, execution.Result);
		Assert.Equal(0u, execution.Bus.ReadLong(ShutdownSignalAddress));
	}

	[Fact]
	public void AllocationFailureReclaimsCompletedFinalizerOnSecondPass() =>
		Assert.Equal(
			42u,
			Execute(
				Compile(
					"AllocationPressureSecondPassEntry",
					heapSize: 0x30),
				M68kCpuModel.M68000).Result);

	private static M68kCompilationResult Compile(
		string entry,
		M68kCpuTarget target = M68kCpuTarget.M68000,
		uint heapSize = 0x0000_8000) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"{FixtureType}::{entry}",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Hunk,
			ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = heapSize
			}
		});

	private static (uint Result, TestBus Bus) Execute(
		M68kCompilationResult result,
		M68kCpuModel model)
	{
		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)HunkLoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = HunkLoadAddress + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + HunkLoadAddress);
		}

		bus.WriteLong(StackPointer, ReturnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(HunkLoadAddress + result.EntryPoint, StackPointer);
		for (var instruction = 0; instruction < 2_000_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				return (cpu.State.D[0], bus);
			}
			cpu.ExecuteInstruction();
			if (cpu.State.Halted)
			{
				throw new Xunit.Sdk.XunitException(
					$"{model} halted at ${cpu.State.ProgramCounter:X8}, " +
					$"last opcode ${cpu.State.LastOpcode:X4}.");
			}
		}

		throw new Xunit.Sdk.XunitException(
			$"{model} did not return within the instruction limit.");
	}
}
