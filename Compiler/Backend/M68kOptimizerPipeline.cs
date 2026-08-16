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
	private readonly M68kPeepholeOptimizer _layoutPass;
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
			_assembler.PeepholeOptimizationStatistics = new(
				_assembler.InstructionAnalysisBytes,
				batches,
				rewrites,
				rounds,
				0,
				true);
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
			_assembler.PeepholeOptimizationStatistics = new(
				_assembler.InstructionAnalysisBytes,
				batches,
				rewrites,
				rounds,
				0,
				true);
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
		_assembler.PeepholeOptimizationStatistics = new(
			_assembler.InstructionAnalysisBytes,
			batches,
			rewrites,
			rounds,
			methodEnds.Length,
			true);
	}

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
