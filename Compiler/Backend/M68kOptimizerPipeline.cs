/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal interface IM68kOptimizerPass
{
	bool Changed { get; }
	int RewriteCount { get; }

	void Run();
}

internal sealed record M68kPeepholeOptimizationStatistics(
	long AnalyzedBytes,
	int Batches,
	int Rewrites,
	int Rounds,
	int MethodRanges,
	bool Converged)
{
	internal int TerminalGroups { get; init; }
	internal int TerminalMergedCopies { get; init; }
	internal int TerminalInvertedBranches { get; init; }
	internal int TerminalTrampolines { get; init; }
	internal int TerminalGrossBytesRemoved { get; init; }
	internal int TerminalBranchBytesAdded { get; init; }
	internal int TerminalNetBytesSaved { get; init; }
	internal int ReturnConditionTargets { get; init; }
	internal int ReturnConditionTestsRemoved { get; init; }
	internal int ReturnConditionTestsInserted { get; init; }
	internal int ReturnConditionNetBytesSaved { get; init; }
	internal int IdenticalMethodGroups { get; init; }
	internal int IdenticalMethodThunks { get; init; }
	internal int IdenticalMethodGrossBytesRemoved { get; init; }
	internal int IdenticalMethodJumpBytesAdded { get; init; }
	internal int IdenticalMethodNetBytesSaved { get; init; }

	internal static M68kPeepholeOptimizationStatistics Empty { get; } =
		new(0, 0, 0, 0, 0, true);
}

internal sealed class M68kOptimizerPipeline
{
	private sealed record MethodAnalysisScope(
		string? StartLabel,
		string EndLabel,
		IReadOnlyList<string> LabelNames);

	private const int BoundedPeepholeRewriteBudget = 8;
	private const int MaximumCombinedFixedPointRounds = 32;
	private readonly M68kAssembler _assembler;
	private readonly M68kAssemblyBuffer _buffer;
	private readonly M68kPeepholeOptimizer _layoutPass;
	private readonly M68kRepeatedCallResultTestOptimizer _repeatedCallResultTestOptimizer;
	private readonly M68kTerminalEpilogueMerger _terminalEpilogueMerger;
	private readonly M68kIdenticalMethodMerger _identicalMethodMerger;
	private readonly IReadOnlyList<IM68kOptimizerPass> _passes;
	private readonly M68kPeepholeOptimizationMode _peepholeOptimization;

	public M68kOptimizerPipeline(
		M68kAssembler assembler,
		M68kAssemblyBuffer buffer,
		M68kCpuTarget cpu,
		M68kClrPolicy clrPolicy,
		IReadOnlyList<M68kLoopLayout>? sizeFirstLoops = null,
		M68kPeepholeOptimizationMode peepholeOptimization =
			M68kPeepholeOptimizationMode.FixedPoint)
	{
		_assembler = assembler;
		_buffer = buffer;
		_peepholeOptimization = peepholeOptimization;
		_layoutPass = new M68kPeepholeOptimizer(
			assembler,
			buffer,
			cpu,
			clrPolicy,
			sizeFirstLoops ?? Array.Empty<M68kLoopLayout>(),
			peepholeOptimization == M68kPeepholeOptimizationMode.Bounded
				? BoundedPeepholeRewriteBudget
				: int.MaxValue);
		_repeatedCallResultTestOptimizer =
			new M68kRepeatedCallResultTestOptimizer(assembler, buffer);
		_terminalEpilogueMerger = new M68kTerminalEpilogueMerger(
			assembler,
			buffer,
			assembler.EnableMethodLocalTerminalReuse,
			assembler.EnableMethodLocalTerminalSuffixReuse,
			assembler.EnableRegionalTerminalReuse);
		_identicalMethodMerger = new M68kIdenticalMethodMerger(assembler, buffer);
		_passes = new IM68kOptimizerPass[]
		{
			new M68kConditionCodeOptimizer(assembler, buffer),
			_layoutPass
		};
	}

	public void Run()
	{
		_assembler.ResetInstructionAnalysisStatistics();
		if (_peepholeOptimization == M68kPeepholeOptimizationMode.Disabled)
		{
			_assembler.PeepholeOptimizationStatistics =
				M68kPeepholeOptimizationStatistics.Empty;
			return;
		}
		var batches = 0;
		var rewrites = 0;
		var rounds = 0;
		if (_peepholeOptimization == M68kPeepholeOptimizationMode.Bounded)
		{
			foreach (var pass in _passes)
			{
				pass.Run();
				rounds++;
				if (pass.Changed)
				{
					batches++;
					rewrites += pass.RewriteCount;
				}
			}
			var boundedTerminalStatistics = RunTerminalEpilogueMerger(
				ref batches, ref rewrites, ref rounds);
			var boundedIdenticalMethodStatistics = RunIdenticalMethodMerger(
				ref batches, ref rewrites, ref rounds);
			_assembler.PeepholeOptimizationStatistics = CreateStatistics(
				_assembler.InstructionAnalysisBytes,
				batches,
				rewrites,
				rounds,
				0,
				true,
				boundedTerminalStatistics,
				identicalMethods: boundedIdenticalMethodStatistics);
			return;
		}

		var methodEnds = _assembler.Labels
			.Where(static label => label.Key.EndsWith(":end", StringComparison.Ordinal))
			.OrderBy(static label => label.Value)
			.Select(static label => label.Key)
			.ToArray();
		if (methodEnds.Length == 0)
		{
			RunPassesToFixedPoint(ref batches, ref rewrites, ref rounds,
				"whole image");
			RunLayoutCleanup(ref batches, ref rewrites, ref rounds);
			var wholeImageReturnConditionStatistics = RunRepeatedCallResultTestOptimizer(
				ref batches, ref rewrites, ref rounds);
			RunLayoutCleanup(ref batches, ref rewrites, ref rounds);
			var wholeImageTerminalStatistics = RunTerminalEpilogueMerger(
				ref batches, ref rewrites, ref rounds);
			RunLayoutCleanup(ref batches, ref rewrites, ref rounds);
			var wholeImageIdenticalMethodStatistics = RunIdenticalMethodMerger(
				ref batches, ref rewrites, ref rounds);
			RunLayoutCleanup(ref batches, ref rewrites, ref rounds);
			_assembler.PeepholeOptimizationStatistics = CreateStatistics(
				_assembler.InstructionAnalysisBytes,
				batches,
				rewrites,
				rounds,
				0,
				true,
				wholeImageTerminalStatistics,
				wholeImageReturnConditionStatistics,
				wholeImageIdenticalMethodStatistics);
			return;
		}
		try
		{
			RunLayoutCleanup(ref batches, ref rewrites, ref rounds);
			var methodScopes = BuildMethodAnalysisScopes(methodEnds);
			for (var index = methodScopes.Length - 1; index >= 0; index--)
			{
				var scope = methodScopes[index];
				_assembler.SetAnalysisScope(
					scope.StartLabel,
					scope.EndLabel,
					scope.LabelNames);
				RunPassesToFixedPoint(ref batches, ref rewrites, ref rounds,
					scope.EndLabel);
			}
		}
		finally
		{
			_assembler.ClearAnalysisScope();
		}
		RunLayoutCleanup(ref batches, ref rewrites, ref rounds);
		var returnConditionStatistics = RunRepeatedCallResultTestOptimizer(
			ref batches, ref rewrites, ref rounds);
		RunLayoutCleanup(ref batches, ref rewrites, ref rounds);
		var terminalStatistics = RunTerminalEpilogueMerger(
			ref batches, ref rewrites, ref rounds);
		RunLayoutCleanup(ref batches, ref rewrites, ref rounds);
		var identicalMethodStatistics = RunIdenticalMethodMerger(
			ref batches, ref rewrites, ref rounds);
		RunLayoutCleanup(ref batches, ref rewrites, ref rounds);
		_assembler.PeepholeOptimizationStatistics = CreateStatistics(
			_assembler.InstructionAnalysisBytes,
			batches,
			rewrites,
			rounds,
			methodEnds.Length,
			true,
			terminalStatistics,
			returnConditionStatistics,
			identicalMethodStatistics);
	}

	private M68kRepeatedCallResultTestOptimizer.Statistics
		RunRepeatedCallResultTestOptimizer(
			ref int batches,
			ref int rewrites,
			ref int rounds)
	{
		if (!_assembler.EnableRepeatedCallResultTestOptimization)
			return M68kRepeatedCallResultTestOptimizer.Statistics.Empty;

		var statistics = _repeatedCallResultTestOptimizer.Run();
		rounds++;
		if (statistics.NetBytesSaved != 0)
		{
			batches++;
			rewrites += statistics.TestsRemoved;
		}
		return statistics;
	}

	private M68kTerminalEpilogueMerger.Statistics RunTerminalEpilogueMerger(
		ref int batches,
		ref int rewrites,
		ref int rounds)
	{
		if (!_assembler.EnableMethodLocalTerminalReuse &&
			!_assembler.EnableMethodLocalTerminalSuffixReuse &&
			!_assembler.EnableRegionalTerminalReuse)
		{
			return M68kTerminalEpilogueMerger.Statistics.Empty;
		}

		// Managed returns carry precise ABI liveness while the fixed-point
		// peepholes run. Once those rewrites are complete, restore the built-in
		// conservative RTS effects so analysis-only overrides do not become hard
		// boundaries that prevent identical terminal sequences from being shared.
		foreach (var offset in _buffer.InstructionEffectOverrides.Keys.ToArray())
		{
			if (offset >= 0 && offset + 1 < _buffer.Bytes.Count &&
				_buffer.ReadWord(offset) == 0x4E75)
			{
				_buffer.InstructionEffectOverrides.Remove(offset);
			}
		}
		var statistics = _terminalEpilogueMerger.Run();
		rounds++;
		if (statistics.MergedCopies != 0)
		{
			batches++;
			rewrites += statistics.MergedCopies;
		}
		return statistics;
	}

	private M68kIdenticalMethodMerger.Statistics RunIdenticalMethodMerger(
		ref int batches,
		ref int rewrites,
		ref int rounds)
	{
		if (!_assembler.EnableIdenticalMethodThunks)
			return M68kIdenticalMethodMerger.Statistics.Empty;

		var statistics = _identicalMethodMerger.Run();
		rounds++;
		if (statistics.Thunks != 0)
		{
			batches++;
			rewrites += statistics.Thunks;
		}
		return statistics;
	}

	private static M68kPeepholeOptimizationStatistics CreateStatistics(
		long analyzedBytes,
		int batches,
		int rewrites,
		int rounds,
		int methodRanges,
		bool converged,
		M68kTerminalEpilogueMerger.Statistics terminal,
		M68kRepeatedCallResultTestOptimizer.Statistics? returnConditions = null,
		M68kIdenticalMethodMerger.Statistics? identicalMethods = null) =>
		new(analyzedBytes, batches, rewrites, rounds, methodRanges, converged)
		{
			TerminalGroups = terminal.Groups,
			TerminalMergedCopies = terminal.MergedCopies,
			TerminalInvertedBranches = terminal.InvertedBranches,
			TerminalTrampolines = terminal.Trampolines,
			TerminalGrossBytesRemoved = terminal.GrossBytesRemoved,
			TerminalBranchBytesAdded = terminal.BranchBytesAdded,
			TerminalNetBytesSaved = terminal.NetBytesSaved,
			ReturnConditionTargets = returnConditions?.Targets ?? 0,
			ReturnConditionTestsRemoved = returnConditions?.TestsRemoved ?? 0,
			ReturnConditionTestsInserted = returnConditions?.TestsInserted ?? 0,
			ReturnConditionNetBytesSaved = returnConditions?.NetBytesSaved ?? 0,
			IdenticalMethodGroups = identicalMethods?.Groups ?? 0,
			IdenticalMethodThunks = identicalMethods?.Thunks ?? 0,
			IdenticalMethodGrossBytesRemoved = identicalMethods?.GrossBytesRemoved ?? 0,
			IdenticalMethodJumpBytesAdded = identicalMethods?.JumpBytesAdded ?? 0,
			IdenticalMethodNetBytesSaved = identicalMethods?.NetBytesSaved ?? 0
		};

	private MethodAnalysisScope[] BuildMethodAnalysisScopes(
		IReadOnlyList<string> methodEnds)
	{
		var result = new MethodAnalysisScope[methodEnds.Count];
		for (var index = 0; index < methodEnds.Count; index++)
		{
			var startLabel = index == 0 ? null : methodEnds[index - 1];
			var startOffset = startLabel is null ? 0 : _assembler.Labels[startLabel];
			var endLabel = methodEnds[index];
			var endOffset = _assembler.Labels[endLabel];
			var labelNames = _assembler.Labels
				.Where(label => label.Value >= startOffset && label.Value <= endOffset)
				.Select(static label => label.Key)
				.ToArray();
			result[index] = new(startLabel, endLabel, labelNames);
		}
		return result;
	}

	private void RunPassesToFixedPoint(
		ref int batches,
		ref int rewrites,
		ref int rounds,
		string scope)
	{
		bool changed;
		var combinedRounds = 0;
		do
		{
			if (++combinedRounds > MaximumCombinedFixedPointRounds)
			{
				throw new InvalidOperationException(
					$"Peephole passes did not converge for {scope} after " +
					$"{MaximumCombinedFixedPointRounds} combined rounds.");
			}
			foreach (var pass in _passes)
			{
				pass.Run();
				rounds++;
				if (pass.Changed)
				{
					batches++;
					rewrites += pass.RewriteCount;
				}
			}
			changed = _passes.Any(static pass => pass.Changed);
		}
		while (changed);
	}

	private void RunLayoutCleanup(
		ref int batches,
		ref int rewrites,
		ref int rounds)
	{
		_layoutPass.RunLayoutCleanup();
		rounds++;
		if (_layoutPass.Changed)
		{
			batches++;
			rewrites += _layoutPass.RewriteCount;
		}
	}
}
