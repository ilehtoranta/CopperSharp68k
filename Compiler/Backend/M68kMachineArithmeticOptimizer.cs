/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Numerics;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal enum M68kIntegerArithmeticKind
{
	UnsignedPowerOfTwoDivision,
	SignedWordDivision,
	UnsignedWordDivision,
	SignedWordMultiply,
	UnsignedWordMultiply,
	FactoredWordMultiply
}

internal sealed record M68kIntegerArithmeticPlan(
	M68kIntegerArithmeticKind Kind,
	int Constant = 0,
	int SourceOperand = 0,
	int WordFactor = 0,
	int RemainingFactor = 0);

/// <summary>
/// Arithmetic selection before allocation, so both outputs of a division and
/// their lifetimes are visible to the allocator. This pass does not move memory
/// operations or speculate a division onto a path where it did not execute.
/// </summary>
internal static class M68kMachineArithmeticOptimizer
{
	public static IReadOnlyDictionary<int, M68kIntegerArithmeticPlan> Run(
		M68kMachineFunction function, M68kCpuTarget cpu, CompilationModule? module = null)
	{
		var plans = new Dictionary<int, M68kIntegerArithmeticPlan>();
		if (cpu != M68kCpuTarget.M68000) return plans;
		CombineDivisions(function, module);
		var ranges = new M68kIntegerRangeAnalysis(function);
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (instruction.Uses is not [var left, var right] ||
					instruction.Definitions.Length is < 1 or > 2 ||
					function.Values[instruction.Definitions[0]].Kind is
						CilStackValueKind.Float32 or CilStackValueKind.Float64 ||
					function.Values[instruction.Definitions[0]].Width == M68kMachineValueWidth.LongPair)
					continue;
				M68kIntegerArithmeticPlan? plan = null;
				if (instruction.Operation is M68kMachineOperation.Divide or M68kMachineOperation.Remainder &&
					ranges.TryGetConstant(right, out var divisor) && divisor != 0)
				{
					var unsigned = IsUnsigned(instruction);
					if (unsigned && BitOperations.IsPow2(unchecked((uint)divisor)))
						plan = new(M68kIntegerArithmeticKind.UnsignedPowerOfTwoDivision, divisor);
					else if (ranges.Get(left) is { } dividend)
					{
						if (!unsigned && divisor is >= short.MinValue and <= short.MaxValue &&
							FitsSignedWord(dividend.Minimum / divisor) &&
							FitsSignedWord(dividend.Maximum / divisor))
							plan = new(M68kIntegerArithmeticKind.SignedWordDivision, divisor);
						else if (unsigned && divisor is > 0 and <= ushort.MaxValue &&
							dividend.Minimum >= 0 && dividend.Maximum / divisor <= ushort.MaxValue)
							plan = new(M68kIntegerArithmeticKind.UnsignedWordDivision, divisor);
					}
				}
				else if (instruction.Operation == M68kMachineOperation.Multiply &&
					instruction.SourceInstruction?.OpCode != OpCodes.Mul_Ovf &&
					instruction.SourceInstruction?.OpCode != OpCodes.Mul_Ovf_Un)
				{
					var lhs = ranges.Get(left);
					var rhs = ranges.Get(right);
					if (lhs is { IsUnsignedWord: true } && rhs is { IsUnsignedWord: true })
						plan = new(M68kIntegerArithmeticKind.UnsignedWordMultiply);
					else if (lhs is { IsSignedWord: true } && rhs is { IsSignedWord: true })
						plan = new(M68kIntegerArithmeticKind.SignedWordMultiply);
					else
					{
						for (var operand = 0; operand < 2; operand++)
						{
							if (ranges.TryGetConstant(instruction.Uses[1 - operand], out var factor) &&
								factor > ushort.MaxValue &&
								ranges.Get(instruction.Uses[operand]) is { IsSignedWord: true } bounded &&
								TryFactorWordMultiply(factor, bounded, out var wordFactor, out var remaining))
							{
								plan = new(M68kIntegerArithmeticKind.FactoredWordMultiply, factor,
									operand, wordFactor, remaining);
								break;
							}
						}
					}
				}
				if (plan is null) continue;
				plans.Add(instruction.Id, plan);
				// The replacement emits into D0 and uses D2 only for the factored
				// product or a paired remainder-first division. Paired quotient-first
				// division explicitly defines D3 instead of making it an invisible
				// scratch clobber. The old D4-D6 saves/spills can therefore disappear.
				block.Instructions[index] = instruction with
				{
					Clobbers = plan.Kind == M68kIntegerArithmeticKind.FactoredWordMultiply
						? M68kRegisterSet.From(M68kRegister.D2)
						: M68kRegisterSet.None
				};
			}
		}
		M68kMachineIrVerifier.Verify(function);
		return plans;
	}

	private static bool FitsSignedWord(long value) => value is >= short.MinValue and <= short.MaxValue;

	private static bool TryFactorWordMultiply(int factor, M68kIntegerRangeAnalysis.Bounds bounds,
		out int wordFactor, out int remaining)
	{
		// Prefer a small remaining factor; this covers time-unit factors such as
		// 20,000,000 = 15,625 * 1,280 without hard-coding application constants.
		for (var candidate = short.MaxValue; candidate >= 2; candidate--)
		{
			if (factor % candidate != 0) continue;
			var rest = factor / candidate;
			if (BitOperations.PopCount(unchecked((uint)rest)) > 2) continue;
			if (bounds.Minimum * factor < int.MinValue || bounds.Maximum * factor > int.MaxValue)
				continue;
			wordFactor = candidate;
			remaining = rest;
			return true;
		}
		wordFactor = remaining = 0;
		return false;
	}

	private readonly record struct DivisionKey(int Left, int Right, bool Unsigned);

	private static void CombineDivisions(M68kMachineFunction function, CompilationModule? module)
	{
		var definitions = function.Blocks.SelectMany(static block => block.Instructions)
			.SelectMany(instruction => instruction.Definitions.Select(value => (value, instruction)))
			.ToDictionary(static pair => pair.value, static pair => pair.instruction);
		var constants = new Dictionary<(CilStackValueKind, M68kMachineValueWidth, ulong), int>();
		var frameMemory = module is null ? null : new M68kArithmeticFrameMemory(function, module, definitions);
		var nextIdentity = -1;
		foreach (var block in function.Blocks)
		{
			var aliases = new Dictionary<int, int>();
			var memory = new Dictionary<M68kMemoryObject, int>();
			var available = new Dictionary<DivisionKey, (M68kMachineInstruction First, int? Other)>();
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				TrackFrameValues(instruction);
				if (instruction.Operation is not (M68kMachineOperation.Divide or M68kMachineOperation.Remainder) ||
					instruction.Uses is not [var left, var right] ||
					instruction.Definitions is not [var result] ||
					function.Values[result].Kind is CilStackValueKind.Float32 or CilStackValueKind.Float64 ||
					function.Values[result].Width != M68kMachineValueWidth.Long)
					continue;
				var key = new DivisionKey(Canonical(left), Canonical(right), IsUnsigned(instruction));
				if (!available.TryGetValue(key, out var previous))
				{
					available.Add(key, (instruction, null));
					continue;
				}
				var first = previous.First;
				int reused;
				if (first.Operation == instruction.Operation)
				{
					// Its output is fixed to D0; keep an ordinary SSA copy if it must
					// survive another operation rather than extending that fixed live range.
					reused = PreserveResult(first, first.Definitions[0]);
				}
				else if (previous.Other is { } saved) reused = saved;
				else
				{
					var secondaryRegister = first.Operation == M68kMachineOperation.Divide
						? M68kRegister.D3 : M68kRegister.D2;
					var secondary = function.CreateValue(CilStackValueKind.Int32,
						M68kMachineValueWidth.Long, M68kRegisterSet.From(secondaryRegister), secondaryRegister);
					var combined = first with { Definitions = first.Definitions.Add(secondary.Id) };
					var firstIndex = block.Instructions.FindIndex(candidate => candidate.Id == first.Id);
					block.Instructions[firstIndex] = combined;
					definitions[secondary.Id] = combined;
					reused = PreserveResult(combined, secondary.Id);
					available[key] = (combined, reused);
				}
				// PreserveResult inserts before the current position, so locate the
				// consumer again. The operand-evaluation instructions stay in place.
				index = block.Instructions.FindIndex(candidate => candidate.Id == instruction.Id);
				block.Instructions[index] = instruction with
				{
					Operation = M68kMachineOperation.Copy,
					Uses = ImmutableArray.Create(reused),
					Clobbers = M68kRegisterSet.None,
					MayThrow = false,
					SourceInstruction = null,
					ProducesConditionCodes = false
				};
				definitions[result] = block.Instructions[index];
			}

			int PreserveResult(M68kMachineInstruction producer, int output)
			{
				var saved = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long,
					M68kRegisterSet.DataOrAddress);
				var copy = function.CreateInstruction(M68kMachineOperation.Copy, producer.IlOffset,
					uses: [output], definitions: [saved.Id], origin: producer.Origin);
				var producerIndex = block.Instructions.FindIndex(candidate => candidate.Id == producer.Id);
				block.Instructions.Insert(producerIndex + 1, copy);
				definitions[saved.Id] = copy;
				return saved.Id;
			}

			int Canonical(int value)
			{
				var visited = new HashSet<int>();
				while (value >= 0 && visited.Add(value))
				{
					if (aliases.TryGetValue(value, out var alias)) { value = alias; continue; }
					if (!definitions.TryGetValue(value, out var definition)) break;
					var kind = function.Values[value];
					if (definition.Operation == M68kMachineOperation.Constant && definition.ConstantValue is { } constant)
					{
						var constantKey = (kind.Kind, kind.Width, constant.Bits);
						if (!constants.TryGetValue(constantKey, out var identity))
							constants.Add(constantKey, identity = nextIdentity--);
						return identity;
					}
					if (definition is { Operation: M68kMachineOperation.Copy, Uses: [var source] } &&
						function.Values[source].Kind == kind.Kind && function.Values[source].Width == kind.Width)
					{
						value = source;
						continue;
					}
					break;
				}
				return value;
			}

			void TrackFrameValues(M68kMachineInstruction instruction)
			{
				if (instruction.Operation == M68kMachineOperation.Call || instruction.IsSafepoint ||
					(instruction.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0)
				{
					memory.Clear();
					return;
				}
				var accesses = frameMemory?.AccessesFor(instruction) ??
					(instruction.ExactMemoryAccesses.IsDefault ? [] : instruction.ExactMemoryAccesses);
				var reads = accesses.Where(static access => access.Kind == M68kExactMemoryAccessKind.Read).ToArray();
				var writes = accesses.Where(static access => access.Writes).ToArray();
				if ((instruction.MemoryEffect & M68kMachineMemoryEffect.Write) != 0 && writes.Length == 0)
				{
					memory.Clear();
					return;
				}
				var copied = new List<(M68kMemoryObject Destination, int Identity)>();
				if (reads is [var read] && writes is [var write] &&
					IsPrivateFrame(read.Object) && IsPrivateFrame(write.Object) &&
					read.Object.Size == write.Object.Size && read.Object.Size % 4 == 0 &&
					instruction.Operation is M68kMachineOperation.AggregateIndirectLoad or
						M68kMachineOperation.AggregateIndirectStore or M68kMachineOperation.AggregateIndirectCopy or
						M68kMachineOperation.LocalLoad or M68kMachineOperation.LocalStore or
						M68kMachineOperation.ArgumentLoad or M68kMachineOperation.ArgumentStore or
						M68kMachineOperation.AggregateFieldLoad)
				{
					for (var offset = 0; offset < read.Object.Size; offset += 4)
						copied.Add((write.Object with { Offset = write.Object.Offset + offset, Size = 4 },
							MemoryIdentity(read.Object with { Offset = read.Object.Offset + offset, Size = 4 })));
				}
				foreach (var written in writes)
					foreach (var old in memory.Keys.Where(written.Object.Overlaps).ToArray()) memory.Remove(old);
				foreach (var copy in copied) memory[copy.Destination] = copy.Identity;
				if (reads is [var scalarRead] && writes.Length == 0 &&
					IsPrivateFrame(scalarRead.Object) && instruction.Definitions is [var loaded] &&
					function.Values[loaded].Width == M68kMachineValueWidth.Long && scalarRead.Object.Size == 4 &&
					instruction.Operation is M68kMachineOperation.Load or M68kMachineOperation.LocalLoad or
						M68kMachineOperation.ArgumentLoad)
					aliases[loaded] = MemoryIdentity(scalarRead.Object);
				if (writes is [var scalarWrite] && reads.Length == 0 &&
					IsPrivateFrame(scalarWrite.Object) && scalarWrite.Object.Size == 4 && scalarWrite.ValueId is { } stored)
					memory[scalarWrite.Object] = Canonical(stored);
			}

			int MemoryIdentity(M68kMemoryObject location)
			{
				if (!memory.TryGetValue(location, out var identity)) memory[location] = identity = nextIdentity--;
				return identity;
			}
		}
	}

	private static bool IsPrivateFrame(M68kMemoryObject memory) =>
		memory.Kind is M68kMemoryObjectKind.FrameSlot or M68kMemoryObjectKind.ArgumentHome && memory.Size > 0;

	private static bool IsUnsigned(M68kMachineInstruction instruction) =>
		instruction.SourceInstruction?.OpCode == OpCodes.Div_Un || instruction.SourceInstruction?.OpCode == OpCodes.Rem_Un;
}
