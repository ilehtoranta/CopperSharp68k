/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class StaticAddressRelaxationTests
{
	[Theory]
	[InlineData(M68kRegister.D2, M68kRegister.A0)]
	[InlineData(M68kRegister.A1, M68kRegister.A0)]
	[InlineData(M68kRegister.A0, M68kRegister.A1)]
	public void TaggedStaticStorePreservesValueRegistersAndConditionCodes(
		M68kRegister source,
		M68kRegister scratch)
	{
		var assembler = new M68kAssembler {
			CompactApplicationAddresses = true,
			CompactApplicationStatics = true
		};
		assembler.EmitWord((ushort)(0x23C0 | (int)source));
		assembler.EmitAddress("value", storeAddressScratchRegister: scratch);
		assembler.EmitWord(0x4E75);
		assembler.MarkDataStart();
		assembler.Mark("value");
		assembler.EmitLong(0xDEAD_BEEF);

		Assert.True(assembler.TryGetInstructionEffects(0, out var originalEffects));
		Assert.NotEqual(0, originalEffects.DefinesAddress & (1 << ((int)scratch - (int)M68kRegister.A0)));
		Assert.Equal(4, assembler.EstimateStaticAddressSavings("value", assembler.Offset - 4));
		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Empty(linked.Relocations);
		Assert.Equal(0x41FA | (((int)scratch - (int)M68kRegister.A0) << 9),
			BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Equal(0x2080 | (((int)scratch - (int)M68kRegister.A0) << 9) | (int)source,
			BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(4)));
		Assert.Equal(linked.Labels["value"] - 2,
			BinaryPrimitives.ReadInt16BigEndian(linked.Bytes.AsSpan(2)));
		Assert.False(assembler.TryGetInstructionEffects(0, out _));
		var instructions = assembler.GetExecutableInstructionStream();
		Assert.Equal(4, instructions[0].Length);
		Assert.Equal(2, instructions[1].Length);
		Assert.NotEqual(0, M68kInstructionDataflow.GetEffects(instructions[0]).DefinesAddress &
			(1 << ((int)scratch - (int)M68kRegister.A0)));
		Assert.NotEqual(M68kMemorySet.None,
			M68kInstructionDataflow.GetEffects(instructions[1]).WritesMemory);

		foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
		foreach (uint load in new uint[] { 0x10000, 0x20000 })
		foreach (uint value in new uint[] { 0, 0x8000_0000, 0x1234_5678 })
		foreach (var flags in new[] { 0, 0x10, 0x1F })
		{
			var bus = new TestBus();
			linked.Bytes.CopyTo(bus.Memory.AsSpan((int)load));
			using var cpu = M68kCoreFactory.Default.Create(model, bus);
			cpu.Reset(load, 0x80000);
			cpu.State.StatusRegister = (ushort)(0x2700 | flags);
			var expectedData = new uint[8];
			var expectedAddress = new uint[7];
			for (var register = 0; register < 8; register++)
			{
				expectedData[register] = cpu.State.D[register] = 0xD000_0000u + (uint)register;
				if (register < 7)
					expectedAddress[register] = cpu.State.A[register] = 0xA000_0000u + (uint)register;
			}
			if (source <= M68kRegister.D7)
				expectedData[(int)source] = cpu.State.D[(int)source] = value;
			else
				expectedAddress[(int)source - (int)M68kRegister.A0] =
				cpu.State.A[(int)source - (int)M68kRegister.A0] = value;
			cpu.ExecuteInstruction(); // LEA must preserve the incoming CCR.
			Assert.Equal((ushort)(0x2700 | flags), cpu.State.StatusRegister);
			cpu.ExecuteInstruction(); // MOVE.L publishes N/Z and clears V/C, preserving X.
			Assert.False(cpu.State.Halted);
			var expectedFlags = (flags & 0x10) |
				(value == 0 ? 4 : 0) |
				((value & 0x8000_0000) != 0 ? 8 : 0);
			Assert.Equal((ushort)(0x2700 | expectedFlags), cpu.State.StatusRegister);
			expectedAddress[(int)scratch - (int)M68kRegister.A0] = load + (uint)linked.Labels["value"];
			for (var register = 0; register < 8; register++)
			{
				Assert.Equal(expectedData[register], cpu.State.D[register]);
				if (register < 7) Assert.Equal(expectedAddress[register], cpu.State.A[register]);
			}
			Assert.Equal(value, bus.ReadLong(load + (uint)linked.Labels["value"]));
		}
	}

	[Theory]
	[InlineData("bss")]
	[InlineData("far")]
	[InlineData("addend")]
	[InlineData("no-hint")]
	[InlineData("static-disabled")]
	[InlineData("address-disabled")]
	public void UnrelaxableStaticStoresKeepAbsoluteRelocation(string scenario)
	{
		var assembler = new M68kAssembler {
			CompactApplicationAddresses = scenario != "address-disabled",
			CompactApplicationStatics = scenario != "static-disabled"
		};
		assembler.EmitWord(0x23C2); // MOVE.L D2,value
		assembler.EmitAddress("value", addend: scenario == "addend" ? 4 : 0,
			storeAddressScratchRegister: scenario == "no-hint" ? null : M68kRegister.A0);
		assembler.EmitWord(0x4E75);
		assembler.MarkDataStart();
		if (scenario == "far")
			for (var index = 0; index < 16_384; index++) assembler.EmitWord(0);
		if (scenario == "bss") { assembler.MarkWritableDataStart(); assembler.MarkBssStart(); }
		assembler.Mark("value");
		assembler.EmitLong(0);
		assembler.EmitLong(0);

		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Equal(0x23C2, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Single(linked.Relocations);
		Assert.Equal((uint)linked.Labels["value"] + (scenario == "addend" ? 4u : 0u),
			BinaryPrimitives.ReadUInt32BigEndian(linked.Bytes.AsSpan(2)));
	}

	[Fact]
	public void StaticCompareCanReadNearbyInitializedDataPcRelatively()
	{
		var absolute = EmitStaticCompare(compactStatics: false);
		var compact = EmitStaticCompare(compactStatics: true);
		Assert.Single(absolute.Relocations);
		Assert.Empty(compact.Relocations);
		Assert.Equal(0xB6B9, BinaryPrimitives.ReadUInt16BigEndian(absolute.Bytes));
		Assert.Equal(0xB6BA, BinaryPrimitives.ReadUInt16BigEndian(compact.Bytes));
		Assert.Equal(absolute.Bytes.Length - 2, compact.Bytes.Length);
		Assert.Equal(compact.Labels["value"] - 2,
			BinaryPrimitives.ReadInt16BigEndian(compact.Bytes.AsSpan(2)));

		foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
		foreach (uint load in new uint[] { 0x10000, 0x20000 })
		foreach (var flags in new[] { 0, 0x10, 0x1F })
		foreach (uint value in new uint[] { 0, 0x1234_5678, 0x8000_0000 })
		{
			var absoluteState = ExecuteCompare(absolute, model, load, flags, value);
			var compactState = ExecuteCompare(compact, model, load, flags, value);
			Assert.Equal(absoluteState.Status, compactState.Status);
			Assert.Equal(absoluteState.Data, compactState.Data);
			Assert.Equal(absoluteState.Address, compactState.Address);
		}
	}

	[Fact]
	public void ImmediateWordResemblingCompareIsNotAnInstructionBoundary()
	{
		var assembler = new M68kAssembler {
			CompactApplicationAddresses = true,
			CompactApplicationStatics = true
		};
		assembler.EmitWord(0x23FC); // MOVE.L #$0000B0B9,value
		assembler.EmitLong(0x0000_B0B9); // Low half resembles CMP.L abs.l,D0.
		assembler.EmitAddress("value");
		assembler.EmitWord(0x4E75);
		assembler.MarkDataStart();
		assembler.Mark("value");
		assembler.EmitLong(0);

		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Equal(0x23FC, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Equal(0x0000_B0B9u, BinaryPrimitives.ReadUInt32BigEndian(linked.Bytes.AsSpan(2)));
		Assert.Equal((uint)linked.Labels["value"], BinaryPrimitives.ReadUInt32BigEndian(linked.Bytes.AsSpan(6)));
		Assert.Single(linked.Relocations);
		Assert.Equal(10, assembler.GetExecutableInstructionStream()[0].Length);
	}

	[Theory]
	[InlineData(M68kRegister.A0, M68kRegister.A0)]
	[InlineData(M68kRegister.D2, M68kRegister.A2)]
	public void StaticStoreRejectsSourceOrUnsupportedScratch(
		M68kRegister source,
		M68kRegister scratch)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord((ushort)(0x23C0 | (int)source));
		Assert.Throws<InvalidOperationException>(() =>
			assembler.EmitAddress("value", storeAddressScratchRegister: scratch));
	}

	[Fact]
	public void ScratchEffectTracksCurrentStoreSourceAndStopsAfterArithmeticFusion()
	{
		var assembler = TaggedStore(M68kRegister.D2, M68kRegister.A0);
		var buffer = Buffer(assembler);
		buffer.WriteWord(0, 0x23C3); // A peephole forwards D3 into the tagged store.
		var changedStore = assembler.GetInstructionStream()[0];
		var ordinaryEffects = M68kInstructionDataflow.GetEffects(changedStore);
		Assert.True(assembler.TryGetInstructionEffects(0, out var taggedEffects));
		Assert.Equal(ordinaryEffects with { DefinesAddress =
			(ushort)(ordinaryEffects.DefinesAddress | 1) }, taggedEffects);

		buffer.WriteWord(0, 0xD5B9); // A read/modify/write fold makes it ADD.L D2,value.
		Assert.False(assembler.TryGetInstructionEffects(0, out _));
		var arithmetic = assembler.GetInstructionStream()[0];
		Assert.NotEqual(M68kMemorySet.None,
			M68kInstructionDataflow.GetEffects(arithmetic).WritesMemory);
		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Equal(0xD5B9, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Single(linked.Relocations);
	}

	[Fact]
	public void ScratchEffectFollowsFixupMovedByAddressTransferFold()
	{
		var assembler = new M68kAssembler {
			CompactApplicationAddresses = true,
			CompactApplicationStatics = true
		};
		assembler.EmitWord(0x2242); // MOVEA.L D2,A1
		assembler.EmitWord(0x23C9); // MOVE.L A1,value
		assembler.EmitAddress("value", storeAddressScratchRegister: M68kRegister.A0);
		assembler.EmitWord(0x4E75);
		assembler.MarkDataStart();
		assembler.Mark("value");
		assembler.EmitLong(0);
		var buffer = Buffer(assembler);
		var optimizer = new M68kPeepholeOptimizer(assembler, buffer,
			M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
		var fold = typeof(M68kPeepholeOptimizer).GetMethod(
			"TryFoldAddressRegisterMemoryTransfer",
			BindingFlags.Instance | BindingFlags.NonPublic)!;
		Assert.True(fold.CreateDelegate<Func<M68kInstructionDataflow, bool>>(optimizer)(
			M68kInstructionDataflow.Analyze(assembler)));
		Assert.Equal(2, Assert.Single(buffer.Addresses).Offset);
		Assert.Equal(0x23C2, buffer.ReadWord(0));
		Assert.True(assembler.TryGetInstructionEffects(0, out var effects));
		Assert.NotEqual(0, effects.DefinesAddress & 1);
		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Equal(0x41FA, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Equal(0x2082, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(4)));
		Assert.Empty(linked.Relocations);
	}

	[Fact]
	public void ExplicitStoreEffectKeepsOriginalInstructionAndRelocation()
	{
		var assembler = TaggedStore(M68kRegister.D2, M68kRegister.A0);
		var original = M68kInstructionDataflow.GetEffects(assembler.GetInstructionStream()[0]);
		assembler.SetInstructionEffects(0, original with { IsBarrier = true });
		Assert.True(assembler.TryGetInstructionEffects(0, out var annotated));
		Assert.True(annotated.IsBarrier);
		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Equal(0x23C2, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Single(linked.Relocations);
		Assert.True(assembler.TryGetInstructionEffects(0, out annotated));
		Assert.True(annotated.IsBarrier);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68020, M68kOutputFormat.Hunk, M68kRuntimeProfile.Application, true)]
	[InlineData(M68kCpuTarget.M68040, M68kOutputFormat.Hunk, M68kRuntimeProfile.Application, true)]
	[InlineData(M68kCpuTarget.M68000, M68kOutputFormat.Assembly, M68kRuntimeProfile.Application, true)]
	[InlineData(M68kCpuTarget.M68000, M68kOutputFormat.Hunk, M68kRuntimeProfile.Freestanding, true)]
	[InlineData(M68kCpuTarget.M68000, M68kOutputFormat.Hunk, M68kRuntimeProfile.Application, false)]
	public void StaticCompactionOptionIsInertOutsideEligibleOutputAndCpu(
		M68kCpuTarget cpu,
		M68kOutputFormat output,
		M68kRuntimeProfile profile,
		bool compactAddresses)
	{
		var request = StaticPolicyRequest(cpu, output, profile, compactAddresses);
		var baseline = M68kCompiler.Compile(request);
		var compact = M68kCompiler.Compile(request with {
			CodeSizeOptimizations = request.CodeSizeOptimizations! with { CompactApplicationStatics = true }
		});
		Assert.Equal(baseline.Image, compact.Image);
		Assert.Equal(baseline.Code, compact.Code);
		Assert.Equal(baseline.Relocations, compact.Relocations);
	}

	[Fact]
	public void EligibleApplicationStaticStoresRemoveArtifactBytesAndRelocations()
	{
		var request = StaticPolicyRequest(M68kCpuTarget.M68000, M68kOutputFormat.Hunk,
			M68kRuntimeProfile.Application, compactAddresses: true);
		var baseline = M68kCompiler.Compile(request);
		var compact = M68kCompiler.Compile(request with {
			CodeSizeOptimizations = request.CodeSizeOptimizations! with { CompactApplicationStatics = true }
		});
		Assert.True(compact.Image.Length < baseline.Image.Length);
		Assert.True(compact.Relocations.Count < baseline.Relocations.Count);
	}

	private static M68kCompilationRequest StaticPolicyRequest(
		M68kCpuTarget cpu,
		M68kOutputFormat output,
		M68kRuntimeProfile profile,
		bool compactAddresses) => new() {
		AssemblyPath = typeof(StaticAddressPolicyFixtures).Assembly.Location,
		EntryPoint = typeof(StaticAddressPolicyFixtures).FullName + "::Entry",
		Cpu = cpu,
		OutputFormat = output,
		RuntimeProfile = profile,
		ExceptionMode = M68kExceptionMode.Full,
		IncludedExportNames = [],
		Hunk = new() { IncludeSymbols = false },
		CodeSizeOptimizations = new() {
			CompactApplicationAddresses = compactAddresses,
			CompactApplicationStatics = false
		}
	};

	private static M68kAssembler TaggedStore(M68kRegister source, M68kRegister scratch)
	{
		var assembler = new M68kAssembler {
			CompactApplicationAddresses = true,
			CompactApplicationStatics = true
		};
		assembler.EmitWord((ushort)(0x23C0 | (int)source));
		assembler.EmitAddress("value", storeAddressScratchRegister: scratch);
		assembler.EmitWord(0x4E75);
		assembler.MarkDataStart();
		assembler.Mark("value");
		assembler.EmitLong(0);
		return assembler;
	}

	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler)
			.GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(assembler)!;

	private static LinkedCode EmitStaticCompare(bool compactStatics)
	{
		var assembler = new M68kAssembler {
			CompactApplicationAddresses = true,
			CompactApplicationStatics = compactStatics
		};
		assembler.EmitWord(0xB6B9); // CMP.L value,D3
		assembler.EmitAddress("value");
		assembler.EmitWord(0x4E75);
		assembler.MarkDataStart();
		assembler.Mark("value");
		assembler.EmitLong(0x1234_5678);
		return assembler.Link(0, new Dictionary<string, uint>());
	}

	private static (ushort Status, uint[] Data, uint[] Address) ExecuteCompare(
		LinkedCode linked, M68kCpuModel model, uint load, int flags, uint value)
	{
		var bus = new TestBus();
		linked.Bytes.CopyTo(bus.Memory.AsSpan((int)load));
		foreach (var relocation in linked.Relocations)
			bus.WriteLong(load + (uint)relocation.Offset,
				bus.ReadLong(load + (uint)relocation.Offset) + load);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(load, 0x80000);
		cpu.State.StatusRegister = (ushort)(0x2700 | flags);
		for (var register = 0; register < 8; register++)
		{
			cpu.State.D[register] = 0xD000_0000u + (uint)register;
			if (register < 7) cpu.State.A[register] = 0xA000_0000u + (uint)register;
		}
		cpu.State.D[3] = value;
		cpu.ExecuteInstruction();
		Assert.False(cpu.State.Halted);
		return (cpu.State.StatusRegister, cpu.State.D.ToArray(), cpu.State.A.Take(7).ToArray());
	}
}

public static class StaticAddressPolicyFixtures
{
	private static int _hot;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Entry()
	{
		Set(3);
		var first = _hot;
		Set(first + 4);
		var second = _hot;
		Set(second + 5);
		return unchecked((uint)(_hot + first + second));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Set(int value) => _hot = value;
}
