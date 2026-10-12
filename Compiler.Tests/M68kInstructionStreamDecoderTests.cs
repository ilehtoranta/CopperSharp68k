/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kInstructionStreamDecoderTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(5)]
	public void AnalysisMatchesTextRendererForEveryOpcodeAtBufferBoundaries(int extensionWords)
	{
		// The text path retains the original family precedence and operand checks.
		// Compare against it independently of the analysis lookup table so future
		// renderer additions cannot silently change instruction boundaries.
		var renderer = typeof(M68kAssembler).GetMethod("TryRenderInstruction", BindingFlags.Instance | BindingFlags.NonPublic)!;
		var parameters = renderer.GetParameters();
		object?[] arguments = [0, null, null, null, null, null, 0];
		for (var index = 1; index <= 4; index++)
			arguments[index] = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(parameters[index].ParameterType.GetGenericArguments()));
		for (var opcode = 0; opcode <= ushort.MaxValue; opcode++)
		{
			var assembler = new M68kAssembler();
			assembler.EmitWord((ushort)opcode);
			for (var index = 0; index < extensionWords; index++) assembler.EmitWord(0xFFFF);
			var decoded = (bool)renderer.Invoke(assembler, arguments)!;
			var first = assembler.GetInstructionStream()[0];
			Assert.True(decoded == first.IsDecoded && (int)arguments[6]! == first.Length,
				$"Opcode ${opcode:X4}, {extensionWords} extension words: renderer ({decoded}, {arguments[6]}), analysis ({first.IsDecoded}, {first.Length}).");
		}
	}
}
