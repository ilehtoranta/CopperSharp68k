/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Moves repeated low-byte result tests from internal call sites to all RTS
/// exits of the called routine. Calls do not define the 68000 condition codes,
/// so a single test at each return is equivalent to testing D0 after every
/// call. The rewrite is retained only when it removes more tests than it adds.
/// </summary>
internal sealed class M68kRepeatedCallResultTestOptimizer
{
	internal sealed record Statistics(
		int Targets,
		int TestsRemoved,
		int TestsInserted)
	{
		internal int NetBytesSaved =>
			checked((TestsRemoved - TestsInserted) * 2);

		internal static Statistics Empty { get; } = new(0, 0, 0);
	}

	private sealed record Candidate(
		int TargetOffset,
		IReadOnlyList<int> TestOffsets,
		IReadOnlySet<int> ReturnOffsets);

	private readonly M68kAssembler _assembler;
	private readonly M68kAssemblyBuffer _buffer;

	internal M68kRepeatedCallResultTestOptimizer(
		M68kAssembler assembler,
		M68kAssemblyBuffer buffer)
	{
		_assembler = assembler;
		_buffer = buffer;
	}

	internal Statistics Run()
	{
		IReadOnlyList<M68kEmittedInstruction> instructions;
		_assembler.BeginInstructionAnalysisRound();
		try
		{
			instructions = _assembler.GetExecutableInstructionStream();
		}
		finally
		{
			_assembler.EndInstructionAnalysisRound();
		}
		if (instructions.Count < 3)
			return Statistics.Empty;

		var indexByOffset = instructions
			.Select((instruction, index) => (instruction.Offset, Index: index))
			.ToDictionary(static item => item.Offset, static item => item.Index);
		// The code generator places bookkeeping labels at many fall-through
		// instruction boundaries. Only a referenced label makes the test an
		// independent entry that cannot be removed safely.
		var entryOffsets = instructions
			.Where(static instruction => instruction.TargetOffset.HasValue)
			.Select(static instruction => instruction.TargetOffset!.Value)
			.ToHashSet();
		foreach (var address in _buffer.Addresses)
		{
			if (!address.External &&
				_buffer.Labels.TryGetValue(address.Target, out var targetOffset))
			{
				entryOffsets.Add(targetOffset);
			}
		}
		foreach (var pcRelative in _buffer.PcRelative)
		{
			if (_buffer.Labels.TryGetValue(pcRelative.Target, out var targetOffset))
				entryOffsets.Add(targetOffset);
		}
		var testsByTarget = new Dictionary<int, List<int>>();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var call = instructions[index];
			var test = instructions[index + 1];
			if (call.Kind != M68kInstructionKind.Call ||
				call.ExternalTarget ||
				call.IsNonReturning ||
				call.TargetOffset is not { } targetOffset ||
				!indexByOffset.ContainsKey(targetOffset) ||
				call.Offset + call.Length != test.Offset ||
				test.Opcode != 0x4A00 || // TST.B D0
				test.Length != 2 ||
				entryOffsets.Contains(test.Offset))
			{
				continue;
			}

			if (!testsByTarget.TryGetValue(targetOffset, out var tests))
			{
				tests = new List<int>();
				testsByTarget.Add(targetOffset, tests);
			}
			tests.Add(test.Offset);
		}
		if (testsByTarget.Count == 0)
			return Statistics.Empty;

		var candidates = new List<Candidate>();
		foreach (var (targetOffset, testOffsets) in testsByTarget)
		{
			if (TryFindReturnOffsets(
					targetOffset,
					instructions,
					indexByOffset,
					out var returnOffsets))
			{
				candidates.Add(new Candidate(
					targetOffset,
					testOffsets,
					returnOffsets));
			}
		}

		var selected = new List<Candidate>();
		var insertedReturns = new HashSet<int>();
		while (true)
		{
			Candidate? best = null;
			var bestSaving = 0;
			foreach (var candidate in candidates)
			{
				if (selected.Contains(candidate))
					continue;
				var addedReturns = candidate.ReturnOffsets.Count(offset =>
					!insertedReturns.Contains(offset));
				var saving = candidate.TestOffsets.Count - addedReturns;
				if (saving > bestSaving)
				{
					best = candidate;
					bestSaving = saving;
				}
			}
			if (best is null)
				break;

			selected.Add(best);
			insertedReturns.UnionWith(best.ReturnOffsets);
		}
		if (selected.Count == 0)
			return Statistics.Empty;

		var removedTests = selected
			.SelectMany(static candidate => candidate.TestOffsets)
			.ToHashSet();
		var edits = removedTests
			.Select(static offset => (Offset: offset, Insert: false))
			.Concat(insertedReturns.Select(static offset =>
				(Offset: offset, Insert: true)))
			.OrderByDescending(static edit => edit.Offset)
			.ToArray();
		foreach (var edit in edits)
		{
			if (edit.Insert)
			{
				// Labels at an RTS must continue to name the new test so every
				// branch into a shared epilogue observes the forwarded condition.
				_buffer.InsertBytes(edit.Offset, 2);
				_buffer.WriteWord(edit.Offset, 0x4A00); // TST.B D0
			}
			else
			{
				_buffer.RemoveBytes(edit.Offset, 2);
			}
		}

		return new Statistics(
			selected.Count,
			removedTests.Count,
			insertedReturns.Count);
	}

	private static bool TryFindReturnOffsets(
		int targetOffset,
		IReadOnlyList<M68kEmittedInstruction> instructions,
		IReadOnlyDictionary<int, int> indexByOffset,
		out IReadOnlySet<int> returnOffsets)
	{
		var returns = new HashSet<int>();
		var visited = new HashSet<int>();
		var pending = new Stack<int>();
		pending.Push(targetOffset);
		while (pending.Count != 0)
		{
			var offset = pending.Pop();
			if (!visited.Add(offset))
				continue;
			if (!indexByOffset.TryGetValue(offset, out var index))
			{
				returnOffsets = returns;
				return false;
			}

			var instruction = instructions[index];
			if (!instruction.IsDecoded)
			{
				returnOffsets = returns;
				return false;
			}
			if (instruction.IsNonReturning ||
				instruction.Opcode is 0x4E72 or 0x4E73 or 0x4E74 or 0x4E77)
			{
				continue;
			}
			if (instruction.Kind == M68kInstructionKind.Return)
			{
				returns.Add(instruction.Offset);
				continue;
			}

			var nextOffset = instruction.Offset + instruction.Length;
			switch (instruction.Kind)
			{
				case M68kInstructionKind.UnconditionalBranch:
					if (instruction.ExternalTarget ||
						instruction.TargetOffset is not { } unconditionalTarget)
					{
						returnOffsets = returns;
						return false;
					}
					pending.Push(unconditionalTarget);
					break;

				case M68kInstructionKind.ConditionalBranch:
				case M68kInstructionKind.Dbcc:
					if (instruction.ExternalTarget ||
						instruction.TargetOffset is not { } conditionalTarget)
					{
						returnOffsets = returns;
						return false;
					}
					pending.Push(conditionalTarget);
					pending.Push(nextOffset);
					break;

				default:
					pending.Push(nextOffset);
					break;
			}
		}

		returnOffsets = returns;
		return returns.Count != 0;
	}
}
