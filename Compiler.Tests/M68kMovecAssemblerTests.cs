using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kMovecAssemblerTests
{
	[Fact]
	public void EmitsFixedBigEndianMovecEncodings()
	{
		var assembler = new M68kAssembler();
		assembler.EmitMovecControlToData(0, 0x801); // MOVEC VBR,D0
		assembler.EmitMovecControlToData(1, 0x002); // MOVEC CACR,D1
		assembler.EmitMovecDataToControl(2, 0x002); // MOVEC D2,CACR

		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Equal(
			new byte[]
			{
				0x4E, 0x7A, 0x08, 0x01,
				0x4E, 0x7A, 0x10, 0x02,
				0x4E, 0x7B, 0x20, 0x02
			},
			linked.Bytes);
	}

	[Fact]
	public void EmitsMove16AsOneFourByteInstruction()
	{
		var assembler = new M68kAssembler();
		assembler.EmitMove16PostIncrement(2, 2);

		var linked = assembler.Link(0, new Dictionary<string, uint>());
		var instructions = assembler.GetInstructionStream();

		Assert.Equal(new byte[] { 0xF6, 0x22, 0x20, 0x00 }, linked.Bytes);
		Assert.Single(instructions);
		Assert.Equal(4, instructions[0].Length);
	}
}
