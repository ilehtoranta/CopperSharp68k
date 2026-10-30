using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kHotLoopTransportTests
{
	[Theory]
	[InlineData("dead-before-branch", true)]
	[InlineData("address-move", true)]
	[InlineData("live-on-one-edge", false)]
	[InlineData("live-flags", false)]
	[InlineData("partial-overwrite", false)]
	[InlineData("unowned-range", false)]
	[InlineData("return-address", false)]
	public void ApplicationRegisterMovesRespectCfgLivenessAndMetadata(string shape, bool smaller)
	{
		var before = Build(false);
		var after = Build(true);
		Assert.Equal(smaller, after.Bytes.Length < before.Bytes.Length);
		foreach (var model in Models)
		foreach (var value in new uint[] { 0, 1, 0x7fffffff, 0x80000000, 0xffffffff })
		foreach (var flags in Enumerable.Range(0, 32))
			Equal(Run(before, model, value, flags), Run(after, model, value, flags));

		LinkedCode Build(bool enabled)
		{
			var assembler = NewAssembler(enabled);
			assembler.GeneratedTransportRegistersOnly = true;
			if (shape == "unowned-range") assembler.GeneratedCodeSizeRanges = [];
			assembler.Mark("start");
			assembler.EmitWord(0x5286); // ADDQ.L #1,D6: retain a distinct referenced method entry
			if (shape == "return-address") assembler.MarkReturnAddress("resume");
			assembler.EmitWord(shape == "address-move" ? (ushort)0x204A : (ushort)0x2007);
			if (shape != "live-flags") assembler.EmitWord(0x4A84); // TST.L D4
			assembler.EmitBranch(M68kCondition.Equal, "alternate");
			assembler.EmitWord(0x5285); // ADDQ.L #1,D5: keep the two successors distinct
			assembler.EmitBranch(M68kCondition.True, "join");
			assembler.Mark("alternate");
			if (shape == "live-on-one-edge") assembler.EmitWord(0x2600); // MOVE.L D0,D3
			else assembler.EmitWord(0x5385); // SUBQ.L #1,D5
			assembler.Mark("join");
			assembler.EmitWord(shape == "address-move" ? (ushort)0x204B :
				shape == "partial-overwrite" ? (ushort)0x1003 : (ushort)0x7000);
			Return(assembler); assembler.Mark("end");
			return Finish(assembler, M68kCpuTarget.M68000);
		}
	}

	[Theory]
	[InlineData("compare-source", true)]
	[InlineData("compare-destination", true)]
	[InlineData("compare-both", true)]
	[InlineData("subtract-gap", true)]
	[InlineData("compare-gap", true)]
	[InlineData("entry-copy", true)]
	[InlineData("diagnostic-label", true)]
	[InlineData("byte-through", true)]
	[InlineData("word-through", true)]
	[InlineData("byte-leaf-through", true)]
	[InlineData("byte-leaf-return", true)]
	[InlineData("byte-leaf-upper", false)]
	[InlineData("byte-leaf-clobber", false)]
	[InlineData("loop-byte-through", true)]
	[InlineData("byte-live-upper", false)]
	[InlineData("branch-live-upper", false)]
	[InlineData("changed-source", false)]
	[InlineData("partial-write", false)]
	[InlineData("flags-read", false)]
	[InlineData("live-temporary", false)]
	[InlineData("entry-consumer", false)]
	[InlineData("address-addend", false)]
	[InlineData("memory-gap", false)]
	[InlineData("effect-override", false)]
	[InlineData("policy-off", false)]
	[InlineData("unsupported-cpu", false)]
	public void RegisterForwardingPreservesRegistersFlagsStackAndEntries(string shape, bool smaller)
	{
		var before = CopyProgram(shape, false);
		var after = CopyProgram(shape, true);
		Assert.True(smaller == (after.Bytes.Length < before.Bytes.Length), $"{shape}: before {Convert.ToHexString(before.Bytes)}; after {Convert.ToHexString(after.Bytes)}");
		foreach (var model in Models)
		foreach (var value in new uint[] { 0, 1, 0x7fffffff, 0x80000000, 0xffffffff })
		foreach (var flags in Enumerable.Range(0, 32))
			Equal(Run(before, model, value, flags), Run(after, model, value, flags));
	}

	[Theory]
	[InlineData("indirect")]
	[InlineData("indexed")]
	[InlineData("displacement")]
	[InlineData("postincrement")]
	[InlineData("predecrement")]
	[InlineData("register")]
	[InlineData("immediate")]
	public void ByteLoadsRetainKnownUpperBytesWithoutChangingMemoryAccesses(string mode)
	{
		foreach (var value in new byte[] { 0, 0x7f, 0x80, 0xff })
		{
			var before = ByteProgram(mode, false, value);
			var after = ByteProgram(mode, true, value);
			Assert.True(after.Bytes.Length < before.Bytes.Length);
			foreach (var model in Models)
			foreach (var flags in Enumerable.Range(0, 32))
				Equal(Run(before, model, value, flags), Run(after, model, value, flags));
		}
	}

	[Theory]
	[InlineData("unknown-upper")]
	[InlineData("changed-upper")]
	[InlineData("join-upper")]
	[InlineData("clobber-call")]
	public void ByteBooleanKeepsExtensionsWhenUpperBytesAreNotProvenZero(string mode)
	{
		var code = ByteProgram(mode, true, 0x80);
		Assert.Contains((byte)0x48, code.Bytes); // The EXT.W/EXT.L sequence remains.
		foreach (var model in Models)
		foreach (var flags in Enumerable.Range(0, 32))
			Equal(Run(ByteProgram(mode, false, 0x80), model, 0x80, flags), Run(code, model, 0x80, flags));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ZeroOperandCleanupPreservesAllConditionCodesAndTheExtendBit(bool liveFlags)
	{
		var before = ZeroProgram(false, liveFlags);
		var after = ZeroProgram(true, liveFlags);
		Assert.True(after.Bytes.Length < before.Bytes.Length);
		foreach (var model in Models)
		foreach (var value in new uint[] { 0, 1, 0x80000000, 0xffffffff })
		foreach (var flags in Enumerable.Range(0, 32))
			Equal(Run(before, model, value, flags), Run(after, model, value, flags));
	}

	[Fact]
	public void AnnotatedCallInvalidatesItsDeclaredNonstandardRegisterClobber()
	{
		var assembler = NewAssembler(false);
		assembler.EmitWord(0x7400); // MOVEQ #0,D2
		var call = assembler.Offset;
		assembler.EmitWord(0x4EB9); assembler.EmitAddress("callee");
		assembler.SetInstructionEffects(call, new(0, 1 << 2, 0, 0,
			M68kConditionCodeSet.None, M68kConditionCodeSet.All, M68kMemorySet.All,
			M68kMemorySet.All, 0, true, false));
		var use = assembler.Offset;
		assembler.EmitWord(0x4A82); Return(assembler);
		assembler.Mark("callee"); assembler.EmitWord(0x2403); Return(assembler);
		var flow = M68kInstructionDataflow.Analyze(assembler);
		Assert.Equal(M68kValueRange.Unknown, flow.GetDataValueBefore(use, 2));
	}

	private static readonly M68kCpuModel[] Models = [M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040];
	private static M68kAssembler NewAssembler(bool enabled) => new()
	{
		GeneratedCodeSizeOptions = enabled ? new() { RemoveRedundantTransport = true } : null,
		GeneratedCodeSizeRanges = [("start", "end")]
	};

	private static LinkedCode CopyProgram(string shape, bool enabled)
	{
		var assembler = NewAssembler(enabled && shape != "policy-off");
		assembler.Mark("start");
		if (shape == "entry-consumer")
		{
			assembler.EmitWord(0x4A84); assembler.EmitBranch(M68kCondition.Equal, "consumer");
		}
		if (shape == "entry-copy")
		{
			assembler.EmitWord(0x4A84); assembler.EmitBranch(M68kCondition.Equal, "copy");
			assembler.EmitWord(0x7601);
		}
		assembler.Mark("copy");
		var temporary = shape is "compare-source" or "compare-both" or "subtract-gap" ? 1 :
			shape == "compare-gap" ? 2 : 0;
		var copy = assembler.Offset;
		assembler.EmitWord(shape == "subtract-gap" ? (ushort)0x2204 :
			shape == "compare-gap" ? (ushort)0x2406 : (ushort)(0x2007 | temporary << 9));
		if (shape == "effect-override")
			assembler.SetInstructionEffects(copy, new(0x80, 1, 0, 0,
				M68kConditionCodeSet.None, M68kConditionCodeSet.All, M68kMemorySet.None,
				M68kMemorySet.None, 0, false, false));
		if (shape == "changed-source") assembler.EmitWord(0x5287);
		if (shape == "partial-write") assembler.EmitWord(0x1003);
		if (shape == "flags-read") assembler.EmitWord(0x57C3);
		if (shape == "memory-gap") assembler.EmitWord(0x2610);
		if (shape == "diagnostic-label") assembler.Mark("unreferenced-block-marker");
		if (shape == "subtract-gap") assembler.EmitWord(0x2005);
		if (shape == "compare-gap") { assembler.EmitWord(0x2204); assembler.EmitWord(0x2005); assembler.EmitWord(0x9081); }
		if (shape == "entry-consumer") assembler.Mark("consumer");
		assembler.EmitWord(shape switch {
			"compare-source" => 0xB881, "compare-both" => 0xB281,
			"subtract-gap" => 0x9081, "compare-gap" => 0xB480, _ => 0xB084
		});
		if (shape == "live-temporary") assembler.EmitWord(0x2600);
		if (shape is "byte-through" or "word-through" or "loop-byte-through" or "byte-live-upper" or "branch-live-upper" || shape.StartsWith("byte-leaf-", StringComparison.Ordinal))
		{
			SnapshotFlags(assembler); assembler.EmitWord(0x2A06); // Retain the first CMP's CCR in D5.
			if (shape == "loop-byte-through") assembler.EmitWord(0x7201);
			assembler.Mark("narrow-loop");
			if (shape.StartsWith("byte-leaf-", StringComparison.Ordinal)) { assembler.EmitWord(0x4EB9); assembler.EmitAddress("callee"); }
			assembler.EmitWord(shape == "word-through" ? (ushort)0x3003 : (ushort)0x1003);
			if (shape == "branch-live-upper") { assembler.EmitWord(0x4A84); assembler.EmitBranch(M68kCondition.Equal, "wide-use"); }
			assembler.EmitWord(shape == "word-through" ? (ushort)0xB044 : (ushort)0xB004);
			if (shape == "loop-byte-through") { assembler.EmitWord(0x5381); assembler.EmitBranch(M68kCondition.Plus, "narrow-loop"); }
			if (shape is "byte-live-upper" or "branch-live-upper") { assembler.Mark("wide-use"); assembler.EmitWord(0xB084); }
		}
		SnapshotFlags(assembler); // MOVE.W SR,D6 observes NZVC and X.
		assembler.EmitWord((ushort)(0x7000 | temporary << 9));
		assembler.EmitWord(0x7200); // Every dead temporary is physically overwritten.
		Return(assembler); assembler.Mark("end");
		if (shape.StartsWith("byte-leaf-", StringComparison.Ordinal))
		{
			assembler.Mark("callee");
			assembler.EmitWord(shape == "byte-leaf-upper" ? (ushort)0x2600 : shape == "byte-leaf-clobber" ? (ushort)0x7000 : (ushort)0x7200);
			if (shape == "byte-leaf-return") Return(assembler); else assembler.EmitWord(0x4E75);
		}
		if (shape == "address-addend") { assembler.MarkDataStart(); assembler.EmitAddress("copy", addend: 2); }
		return Finish(assembler, shape == "unsupported-cpu" ? M68kCpuTarget.M68020 : M68kCpuTarget.M68000);
	}

	private static LinkedCode ByteProgram(string mode, bool optimize, byte value)
	{
		var assembler = NewAssembler(true); assembler.Mark("start");
		var register = mode == "clobber-call" ? 2 : 1;
		if (mode == "join-upper")
		{
			assembler.EmitWord(0x4A84); assembler.EmitBranch(M68kCondition.Equal, "load");
		}
		if (mode != "unknown-upper") assembler.EmitWord((ushort)(0x7000 | register << 9));
		if (mode == "changed-upper") assembler.EmitWord(0x2203);
		if (mode == "clobber-call")
		{
			var call = assembler.Offset;
			assembler.EmitWord(0x4EB9); assembler.EmitAddress("callee");
			assembler.SetInstructionEffects(call, new(0, 1 << 2, 0, 0,
				M68kConditionCodeSet.None, M68kConditionCodeSet.All, M68kMemorySet.All,
				M68kMemorySet.All, 0, true, false));
		}
		assembler.Mark("load");
		if (mode == "predecrement") { assembler.EmitWord(0x5288); }
		var ea = mode switch { "indexed" => 0x30, "displacement" => 0x28,
			"postincrement" => 0x18, "predecrement" => 0x20, "register" => 3, "immediate" => 0x3C, _ => 0x10 };
		assembler.EmitWord((ushort)(0x1000 | register << 9 | ea));
		if (mode == "indexed") assembler.EmitWord(0x2800);
		if (mode == "displacement") assembler.EmitWord(3);
		if (mode == "immediate") assembler.EmitWord(value);
		var byteUse = assembler.Offset;
		assembler.EmitWord((ushort)(0x4A00 | register));
		if (optimize && mode is "indirect" or "indexed" or "displacement" or "postincrement" or "predecrement" or "register" or "immediate")
		{
			var fact = M68kInstructionDataflow.Analyze(assembler).GetDataValueBefore(byteUse, register);
			Assert.Equal(0xFFFFFF00u, fact.KnownZeroMask & 0xFFFFFF00u);
		}
		assembler.EmitWord((ushort)(0x57C0 | register));
		assembler.EmitWord((ushort)(0x4880 | register)); assembler.EmitWord((ushort)(0x48C0 | register));
		assembler.EmitWord((ushort)(0x4480 | register)); assembler.EmitWord((ushort)(0x2000 | register));
		SnapshotFlags(assembler); Return(assembler); assembler.Mark("end");
		if (mode == "clobber-call") { assembler.Mark("callee"); assembler.EmitWord(0x2403); Return(assembler); }
		return Finish(assembler, M68kCpuTarget.M68000, optimize);
	}

	private static LinkedCode ZeroProgram(bool enabled, bool liveFlags)
	{
		var assembler = NewAssembler(enabled); assembler.Mark("start");
		assembler.EmitWord(0x7400); assembler.EmitWord(0x7000); assembler.EmitWord(0x1002);
		assembler.EmitWord(0x8280); // OR.L D0,D1
		if (liveFlags) SnapshotFlags(assembler);
		assembler.EmitWord(0x7000); assembler.EmitWord(0x7400);
		Return(assembler); assembler.Mark("end");
		return Finish(assembler, M68kCpuTarget.M68000);
	}

	// MOVE from SR is deliberately opaque to the ordinary decoder. Declare its
	// exact effects so the observation does not make every register live.
	private static void SnapshotFlags(M68kAssembler assembler)
	{
		var offset = assembler.Offset; assembler.EmitWord(0x40C6);
		assembler.SetInstructionEffects(offset, new(0, 1 << 6, 0, 0,
			M68kConditionCodeSet.All, M68kConditionCodeSet.None, M68kMemorySet.None,
			M68kMemorySet.None, 0, false, false));
	}
	private static void Return(M68kAssembler assembler)
	{
		var offset = assembler.Offset; assembler.EmitWord(0x4E75);
		assembler.SetInstructionEffects(offset, new(0xFF, 0, 0xFF, 0x80,
			M68kConditionCodeSet.All, M68kConditionCodeSet.None, M68kMemorySet.Stack,
			M68kMemorySet.None, 4, true, false));
	}
	private static LinkedCode Finish(M68kAssembler assembler, M68kCpuTarget cpu, bool optimize = true)
	{
		assembler.MarkDataStart(); assembler.EmitAddress("start");
		if (optimize) assembler.OptimizeForCpu(cpu);
		return assembler.Link(0x10000, new Dictionary<string,uint>());
	}
	private sealed record Observation(uint[] Data, uint[] Address, ushort Flags, byte[] Guest, uint[] Accesses);
	private static Observation Run(LinkedCode code, M68kCpuModel model, uint value, int flags)
	{
		var bus = new CountedBus(); code.Bytes.CopyTo(bus.Memory,0x10000);
		bus.Inner.WriteLong(0x80000,0x1000); bus.Memory.AsSpan(0x40000,32).Fill((byte)value);
		using var cpu = M68kCoreFactory.Default.Create(model,bus); cpu.Reset(0x10000,0x80000);
		cpu.State.StatusRegister = (ushort)(0x2700 | flags);
		for (var i=0; i<8; i++) { cpu.State.D[i]=0xaabbcc00u+(uint)i; if (i!=7) cpu.State.A[i]=0x40000; }
		cpu.State.D[2]=0; cpu.State.D[3]=0xaabbcc00u|(byte)value; cpu.State.D[4]=value;
		cpu.State.D[5]=value^0x80000000u; cpu.State.D[7]=value;
		for (var i=0; i<200 && cpu.State.ProgramCounter!=0x1000; i++) cpu.ExecuteInstruction();
		Assert.Equal(0x1000u,cpu.State.ProgramCounter);
		return new(Enumerable.Range(0,8).Select(i=>cpu.State.D[i]).ToArray(),
			Enumerable.Range(0,8).Select(i=>cpu.State.A[i]).ToArray(),cpu.State.StatusRegister,
			bus.Memory.AsSpan(0x40000,32).ToArray(),bus.Accesses.ToArray());
	}
	private static void Equal(Observation a, Observation b)
	{
		Assert.Equal(a.Data,b.Data); Assert.Equal(a.Address,b.Address); Assert.Equal(a.Flags,b.Flags);
		Assert.Equal(a.Guest,b.Guest); Assert.Equal(a.Accesses,b.Accesses);
	}
	private sealed class CountedBus : IM68kBus
	{
		internal TestBus Inner { get; } = new(0x100000);
		internal byte[] Memory => Inner.Memory;
		internal List<uint> Accesses { get; } = [];
		private void Record(uint address,int width,bool write) { if (address>=0x40000 && address<0x40020) Accesses.Add(address | (uint)width<<24 | (write?0x80000000u:0)); }
		public byte ReadByte(uint a,ref long c,M68kBusAccessKind k) { Record(a,1,false); return Inner.ReadByte(a,ref c,k); }
		public ushort ReadWord(uint a,ref long c,M68kBusAccessKind k) { Record(a,2,false); return Inner.ReadWord(a,ref c,k); }
		public uint ReadLong(uint a,ref long c,M68kBusAccessKind k) { Record(a,4,false); return Inner.ReadLong(a,ref c,k); }
		public void WriteByte(uint a,byte v,ref long c,M68kBusAccessKind k) { Record(a,1,true); Inner.WriteByte(a,v,ref c,k); }
		public void WriteWord(uint a,ushort v,ref long c,M68kBusAccessKind k) { Record(a,2,true); Inner.WriteWord(a,v,ref c,k); }
		public void WriteLong(uint a,uint v,ref long c,M68kBusAccessKind k) { Record(a,4,true); Inner.WriteLong(a,v,ref c,k); }
		public void ResetExternalDevices(long c) => Inner.ResetExternalDevices(c);
	}
}
