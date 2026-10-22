/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kValueRangeStorageTests
{
	[Fact]
	public void LaterTransfersPreserveEarlierRegisterStackAndAliasSnapshots()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x700A); // MOVEQ #10,D0
		var push = assembler.Offset;
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		var firstLoad = assembler.Offset;
		assembler.EmitWord(0x2217); // MOVE.L (A7),D1
		assembler.EmitWord(0x7014); // MOVEQ #20,D0
		assembler.EmitWord(0x2E80); // MOVE.L D0,(A7)
		var secondLoad = assembler.Offset;
		assembler.EmitWord(0x2417); // MOVE.L (A7),D2
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x701E); // MOVEQ #30,D0
		var done = assembler.Offset;
		assembler.EmitWord(0x4E75);

		var flow = M68kInstructionDataflow.Analyze(assembler);
		Assert.Equal(M68kValueRange.Exact(10), flow.GetDataValueBefore(firstLoad, 0));
		Assert.False(flow.GetDataValueBefore(firstLoad, 1).IsKnown);
		Assert.Equal(M68kValueRange.Exact(10), flow.GetDataValueBefore(secondLoad, 1));
		Assert.Equal(M68kValueRange.Exact(20), flow.GetDataValueBefore(secondLoad, 0));
		Assert.Equal(M68kValueRange.Exact(20), flow.GetDataValueBefore(done, 2));
		Assert.Equal(M68kValueRange.Exact(30), flow.GetDataValueBefore(done, 0));
		Assert.Equal(M68kAddressAlias.Stack(0), flow.GetAddressAliasBefore(push, 7));
		Assert.Equal(M68kAddressAlias.Stack(-4), flow.GetAddressAliasBefore(firstLoad, 7));
		Assert.Equal(M68kAddressAlias.Stack(0), flow.GetAddressAliasBefore(done, 7));
	}
}
