/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kTextFreeDecodeTests
{
	private delegate bool Decode(int offset, IReadOnlyDictionary<string, string> labels,
		IReadOnlyDictionary<int, AddressFixup> addresses, IReadOnlyDictionary<int, BranchFixup> branches,
		IReadOnlyDictionary<int, PcRelativeFixup> pcRelative, out string text, out int length, bool formatText);

	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer",
			BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(assembler)!;

	private static Decode Decoder(M68kAssembler assembler) => typeof(M68kAssembler)
		.GetMethod("TryRenderInstruction", BindingFlags.Instance | BindingFlags.NonPublic)!
		.CreateDelegate<Decode>(assembler);

	[Theory]
	[InlineData(0)]
	[InlineData(255)]
	public void EveryOpcodeRetainsFormattedRecognitionAndLength(int extensionByte)
	{
		var assembler = new M68kAssembler();
		var buffer = Buffer(assembler);
		buffer.Bytes.AddRange(Enumerable.Repeat((byte)extensionByte, 24));
		var decode = Decoder(assembler);
		var labels = new Dictionary<string, string>();
		var addresses = new Dictionary<int, AddressFixup>();
		var branches = new Dictionary<int, BranchFixup>();
		var pcRelative = new Dictionary<int, PcRelativeFixup>();
		for (var opcode = 0; opcode <= ushort.MaxValue; opcode++)
		{
			buffer.WriteWord(0, opcode);
			var formatted = decode(0, labels, addresses, branches, pcRelative, out _, out var expected, true);
			var textFree = decode(0, labels, addresses, branches, pcRelative, out _, out var actual, false);
			Assert.Equal(formatted, textFree);
			Assert.Equal(expected, actual);
		}
	}

	[Theory]
	[InlineData(0x23FC)] // Two long operands.
	[InlineData(0x203C)] // Long immediate.
	[InlineData(0x48EF)] // MOVEM mask and displacement.
	[InlineData(0x4EAE)] // Library vector displacement.
	[InlineData(0xF620)] // MOVE16 extension.
	[InlineData(0xF22F)] // FPU stack operand.
	public void TruncatedOperandsKeepTheSameFallback(int opcode)
	{
		var assembler = new M68kAssembler(); var buffer = Buffer(assembler);
		var decode = Decoder(assembler);
		for (var bytes = 0; bytes <= 12; bytes++)
		{
			buffer.Bytes.Clear(); buffer.Bytes.AddRange(new byte[bytes]);
			if (bytes >= 2) buffer.WriteWord(0, opcode);
			var expected = decode(0, new Dictionary<string, string>(), new Dictionary<int, AddressFixup>(),
				new Dictionary<int, BranchFixup>(), new Dictionary<int, PcRelativeFixup>(), out _, out var length, true);
			Assert.Equal(expected, decode(0, new Dictionary<string, string>(), new Dictionary<int, AddressFixup>(),
				new Dictionary<int, BranchFixup>(), new Dictionary<int, PcRelativeFixup>(), out _, out var actual, false));
			Assert.Equal(length, actual);
		}
	}

	[Fact]
	public void TextFreeOperandDecodingDoesNotAllocateFormattedStrings()
	{
		var assembler = new M68kAssembler(); assembler.EmitWord(0x23FC);
		assembler.EmitLong(0x12345678); assembler.EmitLong(0x87654321);
		var decode = Decoder(assembler);
		var labels = new Dictionary<string, string>(); var addresses = new Dictionary<int, AddressFixup>();
		var branches = new Dictionary<int, BranchFixup>(); var pcRelative = new Dictionary<int, PcRelativeFixup>();
		for (var i = 0; i < 64; i++) decode(0, labels, addresses, branches, pcRelative, out _, out _, false);
		var start = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 8192; i++) decode(0, labels, addresses, branches, pcRelative, out _, out _, false);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
		Assert.True(allocated < 1024, $"Text-free operands allocated {allocated} bytes.");
		Assert.True(decode(0, labels, addresses, branches, pcRelative, out var text, out var length, true));
		Assert.Equal("move.l\t#$12345678,$87654321", text); Assert.Equal(10, length);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void SymbolicTargetsAndRenderingSurviveAlternatingDecodeModes(bool external)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4EB9); assembler.EmitAddress("__c68k_exception_raise", external);
		assembler.EmitBranch(M68kCondition.NotEqual, "target:colon");
		assembler.Mark("target:colon"); assembler.EmitWord(0x203A); assembler.EmitPcRelativeWord("data");
		assembler.EmitWord(0x4E75); assembler.Mark("__c68k_exception_raise"); assembler.EmitWord(0x4AFC);
		assembler.MarkDataStart(); assembler.Mark("data"); assembler.EmitLong(0);
		var all = assembler.GetExecutableInstructionStream();
		Assert.Equal(M68kInstructionKind.Call, all[0].Kind);
		Assert.Equal(external, all[0].ExternalTarget); Assert.Equal(!external, all[0].IsNonReturning);
		Assert.Equal(external ? null : assembler.Labels["__c68k_exception_raise"], all[0].TargetOffset);
		// Rendering may relax an absolute call to a branch; compare each mode
		// on that same current layout after checking the original call flags.
		var expected = assembler.RenderAssembly(M68kCpuTarget.M68000);
		all = assembler.GetExecutableInstructionStream();
		assembler.BeginInstructionAnalysisRound();
		try
		{
			Assert.Equal(all[0], Assert.Single(assembler.GetExecutableInstructionRange(0, 1)));
			Assert.Equal(expected, assembler.RenderAssembly(M68kCpuTarget.M68000));
			Assert.Equal(all, assembler.GetExecutableInstructionStream());
		}
		finally { assembler.EndInstructionAnalysisRound(); }
		Assert.Equal(expected, assembler.RenderAssembly(M68kCpuTarget.M68000));
	}
}
