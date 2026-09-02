/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kBooleanPhiReachabilityTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void BooleanPhiThreadingRemovesItsRawCilOrphan(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var fixture = RawCilFixtureBuilder.CreateBooleanPhiReachabilityAssembly(
			Path.GetTempPath());
		try
		{
			using (var module = new CompilationModule(fixture.AssemblyPath))
			{
				var probe = module.ResolveEntryPoint(
					"RawBooleanPhiReachability::Probe");
				var function = CilMachineIrBuilder.Build(probe, module, target);

				Assert.DoesNotContain(function.Blocks, block =>
					block.StartIlOffset == fixture.DeadProbeIlOffset);
				M68kMachineIrVerifier.Verify(function);
				Assert.Equal(
					function.Blocks.Count,
					M68kControlFlowAnalysis.ComputeDominators(function).Count);
			}

			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = fixture.AssemblyPath,
				EntryPoint = "RawBooleanPhiReachability::Entry",
				Cpu = target,
				OutputFormat = M68kOutputFormat.Hunk
			});

			Assert.Equal(42u, Execute(result, model));
		}
		finally
		{
			File.Delete(fixture.AssemblyPath);
		}
	}

	[Fact]
	public void ReachabilityCleanupRepairsPhiAndKeepsExceptionEntryRoot()
	{
		var function = new M68kMachineFunction("cleanup-roots", 0);
		var entry = AddBlock(function, 0, 0);
		var join = AddBlock(function, 1, 10);
		var orphan = AddBlock(function, 2, 20);
		var exceptionEntry = AddBlock(function, 3, 30);
		var exceptionTail = AddBlock(function, 4, 40);
		exceptionEntry.IsExceptionEntry = true;
		Connect(entry, join);
		Connect(orphan, join);
		Connect(exceptionEntry, exceptionTail);

		var entryValue = CreateLong(function);
		var orphanValue = CreateLong(function);
		var merged = CreateLong(function);
		AddConstant(function, entry, entryValue.Id, 21, 0);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			1));
		AddConstant(function, orphan, orphanValue.Id, 99, 20);
		orphan.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			21));
		join.Phis.Add(new M68kMachinePhi(
			merged.Id,
			new Dictionary<int, int>
			{
				[entry.Id] = entryValue.Id,
				[orphan.Id] = orphanValue.Id
			}));
		join.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			10,
			uses: [merged.Id]));
		exceptionEntry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			30));
		exceptionTail.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			40));

		var removed = M68kControlFlowCleanup.RemoveUnreachableBlocks(function);

		Assert.Equal(1, removed);
		Assert.DoesNotContain(function.Blocks, block => block.Id == orphan.Id);
		Assert.Contains(function.Blocks, block => block.Id == exceptionEntry.Id);
		Assert.Contains(function.Blocks, block => block.Id == exceptionTail.Id);
		Assert.Equal([entry.Id], join.Predecessors);
		var phi = Assert.Single(join.Phis);
		var input = Assert.Single(phi.Inputs);
		Assert.Equal(entry.Id, input.Key);
		Assert.Equal(entryValue.Id, input.Value);
		M68kMachineIrVerifier.Verify(function);
		Assert.Equal(
			function.Blocks.Count,
			M68kControlFlowAnalysis.ComputeDominators(function).Count);
	}

	[Fact]
	public void DominatorAnalysisStillRejectsRawUnreachableBlock()
	{
		var function = new M68kMachineFunction("raw-unreachable", 0);
		var entry = AddBlock(function, 0, 0);
		var orphan = AddBlock(function, 1, 10);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			0));
		orphan.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			10));

		var exception = Assert.Throws<InvalidOperationException>(() =>
			M68kControlFlowAnalysis.ComputeDominators(function));

		Assert.Contains("raw-unreachable", exception.Message, StringComparison.Ordinal);
		Assert.Contains("unreachable blocks 1", exception.Message, StringComparison.Ordinal);
	}

	private static uint Execute(
		M68kCompilationResult result,
		M68kCpuModel model)
	{
		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = LoadAddress + (uint)relocation.Offset;
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
				return cpu.State.D[0];
			}
			cpu.ExecuteInstruction();
			Assert.False(
				cpu.State.Halted,
				$"{model} halted at ${cpu.State.ProgramCounter:X8}.");
		}
		throw new Xunit.Sdk.XunitException(
			$"{model} did not return; PC=${cpu.State.ProgramCounter:X8}.");
	}

	private static M68kMachineBlock AddBlock(
		M68kMachineFunction function,
		int id,
		int ilOffset)
	{
		var block = new M68kMachineBlock(id, ilOffset);
		function.Blocks.Add(block);
		return block;
	}

	private static void Connect(M68kMachineBlock source, M68kMachineBlock target)
	{
		source.Successors.Add(target.Id);
		target.Predecessors.Add(source.Id);
	}

	private static M68kMachineValue CreateLong(M68kMachineFunction function) =>
		function.CreateValue(
			CilStackValueKind.Int32,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Data);

	private static void AddConstant(
		M68kMachineFunction function,
		M68kMachineBlock block,
		int definition,
		int value,
		int offset) =>
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			offset,
			definitions: [definition],
			sourceInstruction: new CilInstruction(
				offset,
				OpCodes.Ldc_I4,
				value,
				offset + 1),
			constantValue: M68kMachineConstant.Int32(value)));
}
