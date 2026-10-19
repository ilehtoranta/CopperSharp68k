/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kLayoutLookupTests
{
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	private const string Edge = "generated:allocated-switch-edge:test";
	private delegate bool BranchQuery(int offset, out BranchFixup branch);
	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer", PrivateInstance)!.GetValue(assembler)!;
	private static M68kPeepholeOptimizer Pass(M68kAssembler assembler) =>
		new(assembler, Buffer(assembler), M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
	private static FieldInfo Field(string name) => typeof(M68kPeepholeOptimizer).GetField(name, PrivateInstance)!;
	private static BranchQuery Query(M68kPeepholeOptimizer pass) => typeof(M68kPeepholeOptimizer)
		.GetMethod("TryGetBranch", PrivateInstance)!.CreateDelegate<BranchQuery>(pass);

	[Fact]
	public void RoundBranchLookupKeepsTheFirstMatchingListEntry()
	{
		var assembler = new M68kAssembler(); var buffer = Buffer(assembler);
		buffer.Branches.Add(new(8, "later")); buffer.Branches.Add(new(2, "first"));
		buffer.Branches.Add(new(2, "alias"));
		var pass = Pass(assembler); Field("_addressFixupOffsets").SetValue(pass, assembler.AddressFixupOffsets);
		var query = Query(pass);
		Assert.True(query(2, out var first)); Assert.Equal("first", first.Target);
		Assert.True(query(8, out var later)); Assert.Equal("later", later.Target);
		Assert.False(query(4, out _));
		for (var i = 0; i < 3; i++) query(2, out _);
		var start = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 1024; i++) Assert.True(query(2, out _));
		Assert.True(GC.GetAllocatedBytesForCurrentThread() - start < 4096);
	}

	[Fact]
	public void CallsOutsideARoundSeeSameCountRetargetingAndRebasing()
	{
		var assembler = new M68kAssembler(); assembler.EmitBranch(M68kCondition.True, "first");
		var buffer = Buffer(assembler); var query = Query(Pass(assembler));
		Assert.True(query(0, out var before)); Assert.Equal("first", before.Target);
		buffer.Branches[0] = buffer.Branches[0] with { Target = "second" };
		buffer.InsertBytes(0, 2); buffer.WriteWord(0, 0x4E71);
		Assert.False(query(0, out _));
		Assert.True(query(2, out var after)); Assert.Equal("second", after.Target);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void BothEntrypointsClearLookupStateOnExceptionalExit(bool layoutOnly)
	{
		var assembler = new M68kAssembler(); assembler.EmitWord(0x4EB9); assembler.EmitAddress("callee");
		assembler.Mark("callee"); assembler.EmitWord(0x4E75);
		var buffer = Buffer(assembler); var pass = Pass(assembler);
		Field("_addressFixupOffsets").SetValue(pass, assembler.AddressFixupOffsets);
		Assert.False(Query(pass)(0, out _)); Assert.NotNull(Field("_roundBranches").GetValue(pass));
		// Duplicate private fixup records fail while preparing the next round.
		buffer.Addresses.Add(buffer.Addresses[0]);
		Assert.Throws<ArgumentException>(() => { if (layoutOnly) pass.RunLayoutCleanup(); else pass.Run(); });
		Assert.Null(Field("_roundBranches").GetValue(pass));
		Assert.Null(Field("_addressFixupOffsets").GetValue(pass));
		buffer.Addresses.RemoveAt(1);
		if (layoutOnly) pass.RunLayoutCleanup(); else pass.Run();
		Assert.Null(Field("_roundBranches").GetValue(pass));
	}

	[Theory]
	[InlineData("private")]
	[InlineData("public")]
	[InlineData("interior address")]
	[InlineData("external address")]
	public void SwitchGroupsRetainAllAliasesAndTheirEntryRestrictions(string alias)
	{
		var assembler = CreateSwitch(alias); var buffer = Buffer(assembler);
		var keys = buffer.Labels.Keys.ToArray(); var before = buffer.Bytes.ToArray();
		var pass = Pass(assembler);
		var bypass = typeof(M68kPeepholeOptimizer).GetMethod("TryBypassEmptySwitchEdges", PrivateInstance)!
			.CreateDelegate<Func<bool>>(pass);
		Assert.Equal(alias == "private", bypass());
		Assert.Equal(keys, buffer.Labels.Keys);
		if (alias == "private")
		{
			Assert.Equal(assembler.Labels["case:zero"], assembler.Labels[Edge]);
			Assert.Equal(assembler.Labels[Edge], assembler.Labels["method:entry:BB00AF"]);
			Assert.Equal(assembler.Labels[Edge], assembler.Labels["generated:alias"]);
			Assert.Equal(before.Length - 4, buffer.Bytes.Count);
		}
		else Assert.Equal(before, buffer.Bytes);
	}

	[Fact]
	public void SwitchDiscoverySeesLabelsRetargetedBetweenCalls()
	{
		var assembler = CreateSwitch("public"); var buffer = Buffer(assembler); var pass = Pass(assembler);
		var bypass = typeof(M68kPeepholeOptimizer).GetMethod("TryBypassEmptySwitchEdges", PrivateInstance)!
			.CreateDelegate<Func<bool>>(pass);
		Assert.False(bypass());
		buffer.Labels["public:alias"] = assembler.Labels["case:zero"];
		Assert.True(bypass());
		Assert.Equal(assembler.Labels["case:zero"], assembler.Labels[Edge]);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void RepeatedLayoutRoundsPreserveCompiledMc68000BranchesTablesAndFlags(bool rebase)
	{
		var original = CreateSwitch("private").Link(0x10000, new Dictionary<string, uint>());
		var assembler = CreateSwitch("private"); var pass = Pass(assembler);
		pass.RunLayoutCleanup();
		Assert.Null(Field("_roundBranches").GetValue(pass));
		if (rebase) { Buffer(assembler).InsertBytes(0, 2); Buffer(assembler).WriteWord(0, 0x4E71); }
		pass.RunLayoutCleanup();
		var linked = assembler.Link(0x10000, new Dictionary<string, uint>());
		Assert.Equal(linked.Labels["case:zero"], linked.Labels[Edge]);
		foreach (var input in new uint[] { 0, 1, 0x80000000, uint.MaxValue })
		foreach (var flags in new ushort[] { 0, 0x1F })
			Assert.Equal(Execute(original, input, flags), Execute(linked, input, flags));
	}

	private static M68kAssembler CreateSwitch(string alias)
	{
		var assembler = new M68kAssembler(); assembler.Mark("entry"); assembler.EmitWord(0x4A80);
		assembler.EmitBranch(M68kCondition.NotEqual, "case:one");
		assembler.EmitBranch(M68kCondition.True, Edge);
		assembler.Mark(Edge); assembler.Mark("method:entry:BB00AF"); assembler.Mark("generated:alias");
		if (alias == "public") assembler.Mark("public:alias");
		assembler.EmitBranch(M68kCondition.True, "case:zero");
		assembler.Mark("case:one"); assembler.EmitWord(0x700B); assembler.EmitWord(0x4E75);
		assembler.Mark("case:zero"); assembler.EmitWord(0x7016); assembler.EmitWord(0x4E75);
		assembler.Mark("method:entry:end"); assembler.MarkDataStart();
		assembler.Mark("table"); assembler.EmitAddress(Edge, alias == "external address", alias == "interior address" ? 2 : 0);
		assembler.EmitPcRelativeWord(Edge);
		return assembler;
	}

	private static (uint D0, uint D1, uint Sp, ushort Flags) Execute(LinkedCode code, uint input, ushort flags)
	{
		const uint load = 0x10000, stack = 0x80000, done = 0x20000;
		var bus = new TestBus(); code.Bytes.CopyTo(bus.Memory.AsSpan((int)load)); bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load + (uint)code.Labels["entry"], stack); cpu.State.D[0] = input; cpu.State.D[1] = 0x12345678;
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var i = 0; i < 32 && cpu.State.ProgramCounter != done; i++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		var table = code.Labels["table"];
		Assert.Equal(load + (uint)code.Labels[Edge], bus.ReadLong(load + (uint)table));
		Assert.Equal(code.Labels[Edge], table + 4 + unchecked((short)bus.ReadWord(load + (uint)table + 4)));
		return (cpu.State.D[0], cpu.State.D[1], cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 0x1F));
	}
}
