/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kDeferredAnalysisTests
{
	private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer", Private)!.GetValue(assembler)!;
	private static M68kValueRangeAnalysis? Values(M68kInstructionDataflow flow) =>
		(M68kValueRangeAnalysis?)typeof(M68kInstructionDataflow).GetField("_values", Private)!.GetValue(flow)!;
	private static M68kConditionProvenanceAnalysis? Conditions(M68kInstructionDataflow flow) =>
		(M68kConditionProvenanceAnalysis?)typeof(M68kInstructionDataflow).GetField("_conditions", Private)!.GetValue(flow)!;

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void OnlyTheQueriedAnalysisIsConstructedAndThenReused(bool conditionFirst)
	{
		var assembler = Create(); var flow = M68kInstructionDataflow.Analyze(assembler);
		Assert.Null(Values(flow)); Assert.Null(Conditions(flow));
		Assert.True(flow.TryGetFacts(8, out _));
		Assert.Null(Values(flow)); Assert.Null(Conditions(flow));
		var comparison = flow.Instructions.Single(i => i.Offset == 8);
		if (conditionFirst) Assert.True(flow.IsConditionInstructionRedundant(comparison, M68kConditionCodeSet.Zero));
		else Assert.True(flow.GetDataValueBefore(6, 0).IsExact(out _));
		Assert.Equal(!conditionFirst, (Values(flow) is not null));
		Assert.Equal(conditionFirst, (Conditions(flow) is not null));
		Assert.True(flow.GetDataValueBefore(6, 0).IsExact(out var value)); Assert.Equal(7u, value);
		Assert.True(flow.IsConditionInstructionRedundant(comparison, M68kConditionCodeSet.Zero));
		var values = Values(flow); var conditions = Conditions(flow);
		flow.GetAddressAliasBefore(6, 7); flow.TryGetKnownZeroTest(comparison, out _);
		Assert.Same(values, Values(flow)); Assert.Same(conditions, Conditions(flow));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void FirstDeferredQueriesRetainBytesEffectsAndCallerOffsetMembership(bool symbolic)
	{
		var assembler = Create(); var buffer = Buffer(assembler);
		var offsets = symbolic ? new HashSet<int> { 2, 10 } : new HashSet<int>();
		var flow = M68kInstructionDataflow.Analyze(assembler, offsets);
		var comparison = flow.Instructions.Single(i => i.Offset == 8);
		Assert.Null(Values(flow)); Assert.Null(Conditions(flow));
		offsets.Clear(); if (!symbolic) offsets.UnionWith(new[] { 2, 10 });
		buffer.WriteWord(4, 99); buffer.WriteWord(12, 1);
		assembler.SetInstructionEffects(0, M68kInstructionDataflow.GetEffects(flow.Instructions[0]) with { IsBarrier = true });
		buffer.InsertBytes(0, 2); buffer.WriteWord(0, 0x4E71);
		Assert.Equal(!symbolic, flow.GetDataValueBefore(6, 0).IsExact(out var value));
		if (!symbolic) Assert.Equal(7u, value);
		Assert.Equal(!symbolic, flow.IsConditionInstructionRedundant(comparison, M68kConditionCodeSet.Zero));
		Assert.True(flow.TryGetFacts(0, out var old)); Assert.False(old.Effects.IsBarrier);
		var fresh = M68kInstructionDataflow.Analyze(assembler);
		Assert.True(fresh.TryGetFacts(2, out var current)); Assert.True(current.Effects.IsBarrier);
	}

	[Fact]
	public void TruncatedImmediateRetainsSymbolicMembershipBeyondItsDecodedLength()
	{
		var assembler = new M68kAssembler(); assembler.EmitWord(0x203C); assembler.EmitWord(0x4E75);
		var offsets = new HashSet<int> { 2 };
		var flow = M68kInstructionDataflow.Analyze(assembler, offsets);
		Assert.False(flow.Instructions[0].IsDecoded); Assert.Equal(2, flow.Instructions[0].Length);
		offsets.Clear();
		Assert.False(flow.GetDataValueBefore(2, 0).IsExact(out _));
		Assert.True(M68kInstructionDataflow.Analyze(assembler).GetDataValueBefore(2, 0).IsExact(out var literal));
		Assert.Equal(0u, literal);
		Assert.Null(Conditions(flow));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void DeferredKnownZeroFactsKeepOriginalBranchEdges(bool taken)
	{
		var assembler = new M68kAssembler(); assembler.EmitWord(0x4A82);
		assembler.EmitBranch(M68kCondition.NotEqual, "nonzero");
		var zero = assembler.Offset; assembler.EmitWord(0x4A82); assembler.EmitWord(0x4E75);
		assembler.Mark("nonzero"); var nonzero = assembler.Offset;
		assembler.EmitWord(0x4A82); assembler.EmitWord(0x4E75);
		var flow = M68kInstructionDataflow.Analyze(assembler);
		var test = flow.Instructions.Single(i => i.Offset == (taken ? nonzero : zero));
		var buffer = Buffer(assembler); buffer.Labels["nonzero"] = zero;
		buffer.WriteWord(0, 0x4A83); // Current image tests another register.
		Assert.True(flow.TryGetKnownZeroTest(test, out var nonZero)); Assert.Equal(taken, nonZero);
		Assert.Null(Values(flow));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void BranchJoinsAndLoopBackEdgesRetainExactValues(bool loop)
	{
		var assembler = new M68kAssembler(); assembler.EmitWord(0x7007);
		assembler.Mark("loop"); assembler.EmitWord(0x4A82);
		assembler.EmitBranch(M68kCondition.NotEqual, "other");
		assembler.EmitWord(0x7007); assembler.EmitBranch(M68kCondition.True, "join");
		assembler.Mark("other"); assembler.EmitWord(0x7007); assembler.Mark("join");
		var test = assembler.Offset; assembler.EmitWord(0x4A80); assembler.EmitWord(0x4A80);
		if (loop) assembler.EmitBranch(M68kCondition.NotEqual, "loop");
		assembler.EmitWord(0x4E75);
		var flow = M68kInstructionDataflow.Analyze(assembler);
		Buffer(assembler).WriteWord(0, 0x7009);
		Assert.True(flow.GetDataValueBefore(test, 0).IsExact(out var value)); Assert.Equal(7u, value);
		// Equal numeric values from distinct definitions retain the existing
		// conservative physical-value provenance at the join.
		Assert.False(flow.IsConditionInstructionRedundant(flow.Instructions.Single(i => i.Offset == test + 2), M68kConditionCodeSet.Zero));
	}

	private static M68kAssembler Create()
	{
		var assembler = new M68kAssembler(); assembler.EmitWord(0x203C); assembler.EmitLong(7);
		assembler.EmitWord(0x4A82); assembler.EmitWord(0x0C82); assembler.EmitLong(0); assembler.EmitWord(0x4E75);
		return assembler;
	}
}
