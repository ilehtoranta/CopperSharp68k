using Copper68k;
using CopperSharp.Compiler.Backend;
using System.Buffers.Binary;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kLiteralAddressOffsetTests
{
	private const uint Stack = 0x80000, Return = 0x1000, ExternalData = 0x50000;
	private const string Entry = "method:entry", Data = "literal:bytes";

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void FoldedOffsetsKeepRelocationDataRegistersAndObservedFlags(M68kCpuTarget target, M68kCpuModel model)
	{
		foreach (var storage in new[] { "emitted", "deferred", "external" })
		foreach (var subtract in new[] { false, true })
		foreach (var offset in new[] { -244, 4, 244 })
		foreach (var hazard in new[] { "none", "value", "flags", "entry" })
		{
			var external = storage == "external";
			var deferred = storage == "deferred";
			if (deferred && hazard == "entry") continue; // Interior data references are covered by the emitted-data fixture.
			var original = Create(offset, subtract, external, hazard, deferred: deferred);
			var optimized = Create(offset, subtract, external, hazard, deferred: deferred);
			optimized.OptimizeForCpu(target);
			if (deferred) { AppendData(original, hazard); AppendData(optimized, hazard); }
			// Data targets and their offsets move together during compaction.
			// A live offset register keeps its load. Live flags and interior
			// entry points forbid folding the arithmetic into the relocation.
			if (hazard is "none" or "value") Assert.True(optimized.Offset < original.Offset,
				$"offset={offset}, subtract={subtract}, external={external}");
			foreach (var origin in new[] { 0x10000u, 0x20000u })
			{
				var expectedCode = original.Link(origin, new Dictionary<string, uint> { [Data] = ExternalData });
				var actualCode = optimized.Link(origin, new Dictionary<string, uint> { [Data] = ExternalData });
				var baseAddress = external ? ExternalData : origin + (uint)actualCode.Labels[Data];
				var foldedOffset = hazard is "none" or "value" ? (subtract ? -offset : offset) : 0;
				Assert.Equal(unchecked(baseAddress + 256 + (uint)foldedOffset),
					BinaryPrimitives.ReadUInt32BigEndian(actualCode.Bytes.AsSpan(2, 4)));
				for (ushort flags = 0; flags < 32; flags++)
				{
					var expected = Execute(expectedCode, origin, external, model, flags);
					var actual = Execute(actualCode, origin, external, model, flags);
					Assert.Equal(expected.Data, actual.Data);
					Assert.Equal(expected.Address, actual.Address);
					Assert.Equal(expected.Flags, actual.Flags);
				}
			}
		}
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public void InternalCodeOrAnOffsetOutsideTheDataAreaKeepsArithmetic(bool codeTarget, bool deferred)
	{
		var assembler = Create(2048, false, false, "none", codeTarget, deferred);
		assembler.OptimizeForM68000();
		if (deferred) AppendData(assembler, "none");
		var linked = assembler.Link(0x10000, new Dictionary<string, uint>());
		Assert.Equal(0x10000u + (uint)linked.Labels[codeTarget ? Entry : Data] + 256,
			BinaryPrimitives.ReadUInt32BigEndian(linked.Bytes.AsSpan(2, 4)));
	}

	private static M68kAssembler Create(int offset, bool subtract, bool external, string hazard,
		bool codeTarget = false, bool deferred = false)
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitWord(0x223C); // MOVE.L #data+256,D1
		assembler.EmitAddress(codeTarget ? Entry : Data, external, 256);
		assembler.Mark(Entry + ":IL_0010");
		assembler.EmitWord(0x203C); // MOVE.L #offset,D0
		assembler.EmitLong(unchecked((uint)offset));
		assembler.EmitWord(subtract ? (ushort)0x9280 : (ushort)0xD280);
		if (hazard == "value") assembler.EmitWord(0x2400); // MOVE.L D0,D2
		if (hazard == "flags") assembler.EmitWord(0x40C3); // MOVE.W SR,D3 (including X)
		assembler.EmitWord(0x2041); // MOVEA.L D1,A0
		assembler.EmitWord(0x7200); // Clear the address temporary.
		assembler.EmitWord(0xD281); // ADD.L D1,D1: kill every arithmetic flag, including X.
		assembler.EmitWord(0x2010); // Read the derived address, rather than comparing layout-dependent pointers.
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		assembler.MarkDataStart();
		if (deferred) assembler.DeclareDeferredDataSymbol(Data, 2048);
		else AppendData(assembler, hazard);
		return assembler;
	}

	private static void AppendData(M68kAssembler assembler, string hazard)
	{
		assembler.Mark(Data);
		for (var n = 0; n < 1024; n++) assembler.EmitWord((ushort)(n * 257));
		if (hazard == "entry") assembler.EmitAddress(Entry + ":IL_0010");
	}

	private static (uint[] Data, uint[] Address, ushort Flags) Execute(LinkedCode code, uint origin,
		bool external, M68kCpuModel model, ushort flags)
	{
		var bus = new TestBus(0x100000);
		code.Bytes.CopyTo(bus.Memory.AsSpan((int)origin));
		if (external)
			for (var n = 0; n < 1024; n++) bus.WriteWord(ExternalData + (uint)n * 2, (ushort)(n * 257));
		bus.WriteLong(Stack, Return);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(origin, Stack);
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var r = 0; r < 8; r++) cpu.State.D[r] = 0xD0000000u + (uint)r;
		for (var r = 0; r < 7; r++) cpu.State.A[r] = 0xA0000000u + (uint)r;
		for (var n = 0; n < 100 && cpu.State.ProgramCounter != Return; n++) cpu.ExecuteInstruction();
		Assert.Equal(Return, cpu.State.ProgramCounter);
		var addresses = cpu.State.A.ToArray();
		addresses[0] -= external ? ExternalData : origin + (uint)code.Labels[Data];
		return (cpu.State.D.ToArray(), addresses, (ushort)(cpu.State.StatusRegister & 31));
	}
}
