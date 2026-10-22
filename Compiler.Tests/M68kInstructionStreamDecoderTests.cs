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
	public void AnalysisPreservesSymbolicOperandPrecedenceForEveryOpcode(int extensionWords)
	{
		var renderer = typeof(M68kAssembler).GetMethod("TryRenderInstruction", BindingFlags.Instance | BindingFlags.NonPublic)!;
		foreach (var fixupOffset in new[] { -1, 0, 2, 4, 6 })
		{
			for (var opcode = 0; opcode <= ushort.MaxValue; opcode++)
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
					$"Opcode ${opcode:X4}, fixup {fixupOffset}, {extensionWords} extension words.");
			}
		}
	}

	[Theory]
	[InlineData(0, 0xFFFF)]
	[InlineData(1, 0xFFFF)]
	[InlineData(5, 0xFFFF)]
	[InlineData(1, 0x0000)]
	[InlineData(1, 0x0800)]
	[InlineData(1, 0x0002)]
	[InlineData(1, 0x4000)]
	[InlineData(2, 0x6000)]
	[InlineData(5, 0x1234)]
	[InlineData(5, 0x8000)]
	public void AnalysisMatchesTextRendererForEveryOpcodeAtBufferBoundaries(int extensionWords, int extension)
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
			for (var index = 0; index < extensionWords; index++) assembler.EmitWord((ushort)extension);
			var decoded = (bool)renderer.Invoke(assembler, arguments)!;
			var first = assembler.GetInstructionStream()[0];
			Assert.True(decoded == first.IsDecoded && (int)arguments[6]! == first.Length,
				$"Opcode ${opcode:X4}, {extensionWords} extension words: renderer ({decoded}, {arguments[6]}), analysis ({first.IsDecoded}, {first.Length}).");
		}
	}

	[Theory]
	[InlineData(0xF200)]
	[InlineData(0xF217)]
	[InlineData(0xF22F)]
	[InlineData(0xF620)]
	[InlineData(0x4C01)]
	[InlineData(0x4C41)]
	[InlineData(0x48E7)]
	[InlineData(0x4CDF)]
	public void AnalysisMatchesRendererForEveryExtensionWord(int opcode)
	{
		var renderer = typeof(M68kAssembler).GetMethod("TryRenderInstruction", BindingFlags.Instance | BindingFlags.NonPublic)!;
		object?[] arguments = [0, new Dictionary<string, string>(), new Dictionary<int, AddressFixup>(),
			new Dictionary<int, BranchFixup>(), new Dictionary<int, PcRelativeFixup>(), null, 0];
		for (var extension = 0; extension <= ushort.MaxValue; extension++)
		{
			var assembler = new M68kAssembler();
			assembler.EmitWord((ushort)opcode);
			assembler.EmitWord((ushort)extension);
			assembler.EmitLong(0xFFFFFFFF);
			var decoded = (bool)renderer.Invoke(assembler, arguments)!;
			var first = assembler.GetInstructionStream()[0];
			Assert.True(decoded == first.IsDecoded && (int)arguments[6]! == first.Length,
				$"Opcode ${opcode:X4}, extension ${extension:X4}.");
		}
	}

	[Fact]
	public void AnalysisFallbackDoesNotAllocateAssemblyTextAfterProfilingWarmup()
	{
		var assembler = new M68kAssembler();
		var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler)
			.GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(assembler)!;
		void Emit(params ushort[] words) { foreach (var word in words) assembler.EmitWord(word); }
		buffer.Labels.Add("target", 0);
		buffer.Branches.Add(new(0, "target")); Emit(0x6600, 0);
		buffer.Addresses.Add(new(6, "target", false)); Emit(0x4EB9, 0, 0);
		buffer.PcRelative.Add(new(12, "target")); Emit(0x203A, 0);
		Emit(0x48E7, 0xFFFF); // MOVEM
		Emit(0x43EF, 8); // LEA
		Emit(0x0280, 0, 0); // Immediate arithmetic
		Emit(0xF22F, 0x4400, 8); // FPU stack transfer
		Emit(0xF620, 0x1000); // MOVE16
		Emit(0x0800, 31); // Bit operation
		Emit(0xE188); // Shift
		Emit(0xD0AF, 4); // Memory source arithmetic
		Emit(0xD1AF, 4); // Memory destination arithmetic
		Emit(0x4EAE, 8); // Displacement control flow
		using var session = M68kOptimizationProfiler.Start();
		assembler.GetInstructionStream(); assembler.GetInstructionStream();
		var before = session.GetSnapshot().Measurements.Where(item => item.Name.StartsWith("analysis.decode.")).ToArray();
		Assert.NotEmpty(before);
		for (var repeat = 0; repeat < 20; repeat++) assembler.GetInstructionStream();
		var after = session.GetSnapshot().Measurements.Where(item => item.Name.StartsWith("analysis.decode.")).ToArray();
		Assert.Equal(before.Sum(item => item.AllocatedBytes), after.Sum(item => item.AllocatedBytes));
		Assert.True(after.Sum(item => item.Calls) > before.Sum(item => item.Calls));
	}
}
