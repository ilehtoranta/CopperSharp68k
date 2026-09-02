/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kAssemblyBufferMutationTests
{
	[Fact]
	public void RemoveBytesPreservesBoundaryLabelsAnchorsSectionsAndFixups()
	{
		var buffer = new M68kAssemblyBuffer();
		buffer.Bytes.AddRange(Enumerable.Range(0, 32).Select(value => (byte)value));
		buffer.MarkDataStart();
		buffer.Bytes.AddRange(Enumerable.Range(32, 8).Select(value => (byte)value));
		buffer.MarkWritableDataStart();
		buffer.Bytes.AddRange(Enumerable.Range(40, 8).Select(value => (byte)value));
		buffer.MarkBssStart();
		buffer.Bytes.AddRange([48, 49]);
		var offsets = new Dictionary<string, int>(StringComparer.Ordinal)
		{
			["before"] = 0,
			["start"] = 10,
			["inside"] = 14,
			["end"] = 20,
			["alias"] = 20,
			["after"] = 30,
			["image-end"] = 50
		};
		foreach (var item in offsets)
		{
			buffer.Labels.Add(item.Key, item.Value);
			buffer.AnalysisAnchors.Add(item.Key, item.Value);
		}
		buffer.Branches.AddRange([
			new(31, "after", false), new(0, "before"),
			new(10, "start"), new(19, "inside"), new(20, "end")
		]);
		buffer.Addresses.AddRange([
			new(31, "external", true, 7), new(0, "before", false, -3),
			new(10, "start", false), new(19, "inside", false),
			new(20, "end", false, 9)
		]);
		buffer.PcRelative.AddRange([
			new(31, "after"), new(0, "before"), new(10, "start"),
			new(19, "inside"), new(20, "end")
		]);
		buffer.InstructionEffectOverrides.Add(40, Effect(1));
		buffer.InstructionEffectOverrides.Add(5, Effect(2));
		buffer.InstructionEffectOverrides.Add(20, Effect(3));
		buffer.InstructionEffectOverrides.Add(10, Effect(4));
		buffer.InstructionEffectOverrides.Add(19, Effect(5));

		buffer.RemoveBytes(10, 10);

		Assert.Equal(Enumerable.Range(0, 10).Concat(Enumerable.Range(20, 30))
			.Select(value => (byte)value), buffer.Bytes);
		Assert.Equal(22, buffer.DataStartOffset);
		Assert.Equal(30, buffer.WritableDataStartOffset);
		Assert.Equal(38, buffer.BssStartOffset);
		Assert.Equal(offsets.Keys, buffer.Labels.Keys);
		Assert.Equal(new[] { 0, 10, 14, 10, 10, 20, 40 }, buffer.Labels.Values);
		Assert.Equal(offsets.Keys, buffer.AnalysisAnchors.Keys);
		Assert.Equal(new[] { 0, 10, 10, 10, 10, 20, 40 }, buffer.AnalysisAnchors.Values);
		Assert.Equal(new BranchFixup[] {
			new(21, "after", false), new(0, "before"), new(10, "end")
		}, buffer.Branches);
		Assert.Equal(new AddressFixup[] {
			new(21, "external", true, 7), new(0, "before", false, -3),
			new(10, "end", false, 9)
		}, buffer.Addresses);
		Assert.Equal(new PcRelativeFixup[] {
			new(21, "after"), new(0, "before"), new(10, "end")
		}, buffer.PcRelative);
		Assert.Equal(new[] { 5, 10, 30 }, buffer.InstructionEffectOverrides.Keys.Order());
		Assert.Equal(Effect(2), buffer.InstructionEffectOverrides[5]);
		Assert.Equal(Effect(3), buffer.InstructionEffectOverrides[10]);
		Assert.Equal(Effect(1), buffer.InstructionEffectOverrides[30]);
	}

	[Fact]
	public void RemoveAllBytesRetainsLabelsButClampsAnalysisAnchors()
	{
		var buffer = new M68kAssemblyBuffer();
		buffer.Bytes.AddRange([1, 2, 3, 4]);
		buffer.Labels.Add("inside", 2);
		buffer.Labels.Add("end", 4);
		buffer.AnalysisAnchors.Add("inside", 2);
		buffer.AnalysisAnchors.Add("end", 4);
		buffer.MarkDataStart();

		buffer.RemoveBytes(0, 4);

		Assert.Empty(buffer.Bytes);
		Assert.Equal(2, buffer.Labels["inside"]);
		Assert.Equal(0, buffer.Labels["end"]);
		Assert.Equal(0, buffer.AnalysisAnchors["inside"]);
		Assert.Equal(0, buffer.AnalysisAnchors["end"]);
		Assert.Equal(0, buffer.DataStartOffset);
		Assert.Null(buffer.WritableDataStartOffset);
		Assert.Null(buffer.BssStartOffset);
	}

	[Fact]
	public void MoveLabelsUpdatesEveryAliasWithinHalfOpenInterval()
	{
		var buffer = new M68kAssemblyBuffer();
		buffer.Labels.Add("before", 8);
		buffer.Labels.Add("start", 10);
		buffer.Labels.Add("middle", 14);
		buffer.Labels.Add("alias", 14);
		buffer.Labels.Add("target", 18);
		buffer.Labels.Add("end", 20);
		buffer.Labels.Add("after", 22);
		var names = buffer.Labels.Keys.ToArray();
		var optimizer = new M68kPeepholeOptimizer(
			new M68kAssembler(), buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
		var method = typeof(M68kPeepholeOptimizer).GetMethod("MoveLabelsToOffset",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		var moveLabels = method!.CreateDelegate<Action<int, int, int>>(optimizer);

		moveLabels(10, 20, 18);

		Assert.Equal(names, buffer.Labels.Keys);
		Assert.Equal(new[] { 8, 18, 18, 18, 18, 20, 22 }, buffer.Labels.Values);
	}

	private static M68kInstructionEffects Effect(ushort identity) =>
		new(identity, 0, 0, 0, M68kConditionCodeSet.None,
			M68kConditionCodeSet.None, M68kMemorySet.None,
			M68kMemorySet.None, 0, false, true);
}
