/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kBranchRelaxationTests
{
	[Fact]
	public void RelaxesNearbyWordBranchesToShortForm()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x4E71); // NOP
		assembler.EmitWord(0x4E71); // NOP
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Contains("beq.s\tC68K_done", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("beq.w", assembly, StringComparison.Ordinal);
		Assert.Equal(0x6704, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Equal(6, linked.Labels["done"]);
	}

	[Fact]
	public void KeepsOutOfRangeWordBranches()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "done");
		for (var index = 0; index < 128; index++)
		{
			assembler.EmitWord(0x4E71); // NOP
		}
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("beq.w\tC68K_done", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesBranchToImmediatelyFollowingInstruction()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.DoesNotContain("beq.", assembly, StringComparison.Ordinal);
		Assert.Contains("C68K_done:", assembly, StringComparison.Ordinal);
		Assert.Contains("\trts", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RelaxesNearbyBsrToShortForm()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBsr("callee");
		assembler.EmitWord(0x4E71); // NOP
		assembler.EmitWord(0x4E71); // NOP
		assembler.Mark("callee");
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("bsr.s\tC68K_callee", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("bsr.w", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RepeatsRelaxationWhenOneShortBranchEnablesAnother()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "far");
		assembler.EmitBranch(M68kCondition.Equal, "near");
		assembler.EmitWord(0x4E71); // NOP
		assembler.EmitWord(0x4E71); // NOP
		assembler.Mark("near");
		for (var index = 0; index < 59; index++)
		{
			assembler.EmitWord(0x4E71); // NOP
		}
		assembler.Mark("far");
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Equal(2, assembly.Split("beq.s", StringSplitOptions.None).Length - 1);
		Assert.DoesNotContain("beq.w", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RelaxesNearbyLocalAbsoluteJumpToBraWord()
	{
		var assembler = new M68kAssembler();
		assembler.EmitJmp("done", external: false);
		assembler.EmitWord(0x4E71); // NOP
		assembler.EmitWord(0x4E71); // NOP
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Contains("bra.w\tC68K_done", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("jmp\tC68K_done", assembly, StringComparison.Ordinal);
		Assert.Equal(0x6000, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Equal(6, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(2)));
		Assert.Equal(8, linked.Labels["done"]);
	}
}
