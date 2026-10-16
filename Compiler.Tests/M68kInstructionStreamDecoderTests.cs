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
	public void MoveLengthFastPathPreservesSymbolicOperandPrecedence(int extensionWords)
	{
		var renderer = typeof(M68kAssembler).GetMethod("TryRenderInstruction", BindingFlags.Instance | BindingFlags.NonPublic)!;
		foreach (var fixupOffset in new[] { -1, 0, 2, 4, 6 })
		{
			for (var opcode = 0x1000; opcode <= 0x3FFF; opcode++)
			{
				var assembler = new M68kAssembler();
				var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler)
					.GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(assembler)!;
				assembler.EmitWord((ushort)opcode);
				for (var index = 0; index < extensionWords; index++) assembler.EmitWord(0xFFFF);
				buffer.Labels.Add("target", 64);
				if (fixupOffset == -1)
					buffer.PcRelative.Add(new(2, "target"));
				else if (fixupOffset == 0)
					buffer.Branches.Add(new(0, "target"));
				else
					buffer.Addresses.Add(new(fixupOffset, "target", false));
				object?[] arguments = [0, new Dictionary<string, string>(),
					buffer.Addresses.ToDictionary(item => item.Offset),
					buffer.Branches.ToDictionary(item => item.OpcodeOffset),
					buffer.PcRelative.ToDictionary(item => item.DisplacementOffset), null, 0];
				bool decoded;
				try
				{
					decoded = (bool)renderer.Invoke(assembler, arguments)!;
				}
				catch (TargetInvocationException exception) when (exception.InnerException is ArgumentOutOfRangeException)
				{
					// A malformed branch fixup can request an unsupported condition.
					// The analysis path must retain the renderer's rejection too.
					Assert.Throws<ArgumentOutOfRangeException>(() => assembler.GetInstructionStream());
					continue;
				}
				var first = assembler.GetInstructionStream()[0];
				Assert.True(decoded == first.IsDecoded && (int)arguments[6]! == first.Length,
					$"MOVE ${opcode:X4}, fixup {fixupOffset}, {extensionWords} extension words.");
			}
		}
	}

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
