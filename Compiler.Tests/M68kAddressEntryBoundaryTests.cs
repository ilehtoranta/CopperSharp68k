/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections;
using System.Reflection;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kAddressEntryBoundaryTests
{
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly FieldInfo AddressIndex = Field("_addressEntryBoundaryIndex");
	private static readonly FieldInfo RoundAddresses = Field("_addressFixupOffsets");

	[Theory]
	[InlineData(10, 0, 10, 20, false)]
	[InlineData(20, 0, 10, 20, false)]
	[InlineData(11, 0, 10, 20, true)]
	[InlineData(0, 10, 10, 20, true)]
	[InlineData(0, 10, 11, 20, false)]
	[InlineData(20, 10, 10, 20, false)]
	[InlineData(20, -10, 10, 20, true)]
	[InlineData(0, -10, -8, -2, true)]
	[InlineData(0, -10, -20, -10, false)]
	[InlineData(0, 10, 10, 10, true)]
	[InlineData(10, 0, 10, 10, false)]
	[InlineData(0, 10, 9, 1, true)]
	[InlineData(0, 0, 9, 1, false)]
	[InlineData(int.MaxValue - 2, 4, int.MaxValue - 1, int.MaxValue, true)]
	[InlineData(int.MinValue + 2, -4, int.MinValue, int.MinValue + 1, true)]
	[InlineData(int.MaxValue, int.MaxValue, int.MinValue, -1, false)]
	[InlineData(int.MinValue, int.MinValue, 0, int.MaxValue, false)]
	public void PreservesDestinationAndSignedSpanBoundaries(
		int target, int addend, int start, int end, bool blocked)
	{
		var (assembler, buffer) = CreateBuffer();
		buffer.Labels.Add("outside:target", target);
		buffer.Addresses.Add(new(0, "outside:target", false, addend));
		var optimizer = CreateOptimizer(assembler, buffer);
		var canFold = BindGuard(optimizer);
		Assert.Equal(!blocked, canFold(start, end));
		RoundAddresses.SetValue(optimizer, new HashSet<int>());
		Assert.Equal(!blocked, canFold(start, end));
		Assert.NotNull(AddressIndex.GetValue(optimizer));
	}

	[Fact]
	public void IncludesGlobalAliasesAndZeroAddendsButIgnoresExternalOrUnresolvedTargets()
	{
		var (assembler, buffer) = CreateBuffer();
		buffer.Labels.Add("outside:alias", 12);
		buffer.Labels.Add("outside:other-alias", 12);
		buffer.Addresses.Add(new(0, "outside:alias", true));
		buffer.Addresses.Add(new(4, "outside:missing", false, 12));
		var canFold = BindGuard(CreateOptimizer(assembler, buffer));
		Assert.True(canFold(10, 20));
		buffer.Addresses.Add(new(8, "outside:other-alias", false));
		Assert.False(canFold(10, 20));
		buffer.Addresses.Add(new(12, "outside:alias", false));
		Assert.False(canFold(10, 20));
	}

	[Theory]
	[InlineData("method:test:IL_000a", true)]
	[InlineData("method:test:IL_A1234", true)]
	[InlineData("method:test:IL_001", false)]
	[InlineData("method:test:IL_000G", false)]
	[InlineData("method:test:BB000A", false)]
	[InlineData("generated:IL_000A", false)]
	public void PreservesNamedLabelBoundaryRules(string name, bool canRelocate)
	{
		var (assembler, buffer) = CreateBuffer();
		buffer.Labels.Add(name, 12);
		assembler.SetAnalysisScope(null, "missing:end", [name]);
		var canFold = BindGuard(CreateOptimizer(assembler, buffer));
		Assert.Equal(canRelocate, canFold(10, 20));
		buffer.Addresses.Add(new(0, name, true));
		// An explicitly referenced source marker remains a hard entry even if
		// the address fixup itself is external and absent from the new index.
		Assert.False(BindGuard(CreateOptimizer(assembler, buffer))(10, 20));
	}

	[Fact]
	public void CachedQueriesMatchOriginalLinearPredicateAcrossRandomBuffers()
	{
		var random = new Random(68000);
		var limits = new[] { int.MinValue, int.MinValue + 1, -1, 0, 1, int.MaxValue - 1, int.MaxValue };
		for (var sample = 0; sample < 200; sample++)
		{
			var (assembler, buffer) = CreateBuffer();
			for (var index = 0; index < 80; index++)
			{
				var target = sample % 4 == 0 && index < limits.Length
					? limits[index] : random.Next(-128, 256);
				buffer.Labels.Add("outside:" + index, target);
				buffer.Labels.Add("alias:" + index, target);
				var name = index % 7 == 0 ? "unresolved:" + index : "outside:" + index;
				var addend = index % 4 == 0 ? random.Next(-64, 65) : 0;
				buffer.Addresses.Add(new(index * 4, name, index % 5 == 0, addend));
			}
			var optimizer = CreateOptimizer(assembler, buffer);
			var canFold = BindGuard(optimizer);
			RoundAddresses.SetValue(optimizer, new HashSet<int>());
			object? firstIndex = null;
			for (var query = 0; query < 64; query++)
			{
				var start = query < limits.Length ? limits[query] : random.Next(-180, 300);
				var end = query < limits.Length ? limits[limits.Length - 1 - query]
					: unchecked(start + random.Next(-2, 24));
				Assert.Equal(!LinearBlocks(buffer, start, end), canFold(start, end));
				firstIndex ??= AddressIndex.GetValue(optimizer);
				Assert.NotNull(firstIndex);
				Assert.Same(firstIndex, AddressIndex.GetValue(optimizer));
			}
		}
	}

	[Fact]
	public void OutsideRoundQueriesObserveResolutionRetargetingInsertionsAndRemovals()
	{
		var (assembler, buffer) = CreateBuffer();
		buffer.Bytes.AddRange(new byte[100]);
		buffer.Addresses.Add(new(96, "outside:target", false));
		var optimizer = CreateOptimizer(assembler, buffer);
		var canFold = BindGuard(optimizer);
		Assert.True(canFold(10, 20));
		buffer.Labels.Add("outside:target", 12);
		Assert.False(canFold(10, 20));
		buffer.InsertBytes(0, 20);
		Assert.True(canFold(10, 20));
		buffer.RemoveBytes(0, 20);
		Assert.False(canFold(10, 20));
		buffer.Addresses[0] = buffer.Addresses[0] with { External = true };
		Assert.True(canFold(10, 20));
		buffer.Addresses[0] = buffer.Addresses[0] with { External = false, Addend = -8 };
		Assert.False(canFold(10, 20));
		buffer.Labels["outside:target"] = 40;
		Assert.True(canFold(10, 20));
		buffer.Addresses.Clear();
		Assert.True(canFold(10, 20));
		Assert.Null(AddressIndex.GetValue(optimizer));
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void RefreshesGlobalAliasAfterAnEarlierBooleanRewriteMovesItsTarget(M68kCpuTarget target)
	{
		var (assembler, buffer) = CreateTwoBooleans();
		var optimizer = CreateOptimizer(assembler, buffer, target);
		optimizer.Run();
		Assert.True(optimizer.Changed);
		Assert.True(assembler.InstructionAnalysisRounds >= 2);
		Assert.Equal(3, assembler.GetExecutableInstructionStream().Count(instruction =>
			(instruction.Opcode & 0xF0F8) == 0x50C0));
		Assert.Equal(18, buffer.Labels["outside:second-entry"]);
		Assert.Equal(0x57C0, buffer.ReadWord(buffer.Labels["outside:second-entry"]));
		Assert.Null(AddressIndex.GetValue(optimizer));
		Assert.Null(RoundAddresses.GetValue(optimizer));
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void BothEntryPointsDiscardPriorRoundIndexOnCompletionOrException(bool layoutOnly, bool fail)
	{
		var (assembler, buffer) = CreateBuffer();
		assembler.EmitWord(0x4E75);
		buffer.Labels.Add("outside:target", 12);
		buffer.Addresses.Add(new(0, "outside:target", false));
		var optimizer = CreateOptimizer(assembler, buffer, rewriteBudget: 1);
		RoundAddresses.SetValue(optimizer, new HashSet<int>());
		Assert.False(BindGuard(optimizer)(10, 20));
		Assert.NotNull(AddressIndex.GetValue(optimizer));
		buffer.Labels["outside:target"] = 40;
		assembler.SetAnalysisScope(null, "missing:end", new ObservedLabels(() =>
		{
			// This is the first setup read, before either entry point analyses
			// instructions: an old round's index must already be unavailable.
			Assert.Null(AddressIndex.GetValue(optimizer));
			if (fail) throw new InvalidOperationException("injected analysis failure");
		}));
		Action run = layoutOnly ? optimizer.RunLayoutCleanup : optimizer.Run;
		if (fail)
			Assert.Equal("injected analysis failure", Assert.Throws<InvalidOperationException>(run).Message);
		else run();
		Assert.Null(AddressIndex.GetValue(optimizer));
		Assert.Null(RoundAddresses.GetValue(optimizer));
		assembler.SetAnalysisScope(null, "missing:end", []);
		Assert.True(BindGuard(optimizer)(10, 20));
		Assert.Null(AddressIndex.GetValue(optimizer));
		// EndInstructionAnalysisRound must also have run after a failure.
		var cacheStream = typeof(M68kAssembler).GetField("_cacheInstructionStream", PrivateInstance)!;
		Assert.False((bool)cacheStream.GetValue(assembler)!);
	}

	internal static (M68kAssembler Assembler, M68kAssemblyBuffer Buffer) CreateTwoBooleans()
	{
		var (assembler, buffer) = CreateBuffer();
		const string entry = "method:address-index";
		assembler.Mark(entry);
		EmitPair();
		Emit(0x2080); // MOVE.L D0,(A0): keep the first result observable.
		EmitPair();
		Emit(0x4E75);
		assembler.Mark(entry + ":end");
		buffer.Labels.Add("outside:second-entry", 26);
		buffer.Labels.Add("outside:second-entry-alias", 26);
		assembler.MarkDataStart();
		assembler.EmitAddress("outside:second-entry");
		assembler.SetAnalysisScope(entry, entry + ":end",
			buffer.Labels.Keys.Where(name => name.StartsWith(entry, StringComparison.Ordinal)).ToArray());
		return (assembler, buffer);

		void EmitPair()
		{
			foreach (var word in new ushort[] { 0x52C0, 0x4880, 0x48C0, 0x4480,
				0x57C0, 0x4880, 0x48C0, 0x4480 }) Emit(word);
		}
		void Emit(ushort word)
		{
			assembler.Mark(entry + ":IL_" + assembler.Offset.ToString("X4"));
			assembler.EmitWord(word);
		}
	}

	private static (M68kAssembler Assembler, M68kAssemblyBuffer Buffer) CreateBuffer()
	{
		var assembler = new M68kAssembler();
		var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler)
			.GetField("_buffer", PrivateInstance)!.GetValue(assembler)!;
		// Deliberately exclude aliases from the local scope. Fixups are global.
		assembler.SetAnalysisScope(null, "missing:end", []);
		return (assembler, buffer);
	}

	private static M68kPeepholeOptimizer CreateOptimizer(M68kAssembler assembler,
		M68kAssemblyBuffer buffer, M68kCpuTarget target = M68kCpuTarget.M68000,
		int rewriteBudget = int.MaxValue) => new(assembler, buffer, target, M68kClrPolicy.Auto, [], rewriteBudget);

	private static Func<int, int, bool> BindGuard(M68kPeepholeOptimizer optimizer) =>
		typeof(M68kPeepholeOptimizer).GetMethod("CanFoldAcrossUnreferencedIlLabels", PrivateInstance)!
			.CreateDelegate<Func<int, int, bool>>(optimizer);

	private static FieldInfo Field(string name) => typeof(M68kPeepholeOptimizer)
		.GetField(name, PrivateInstance) ?? throw new InvalidOperationException("Missing cache field: " + name);

	private static bool LinearBlocks(M68kAssemblyBuffer buffer, int start, int end) =>
		buffer.Addresses.Any(address =>
		{
			if (address.External || !buffer.Labels.TryGetValue(address.Target, out var target)) return false;
			var destination = (long)target + address.Addend;
			return destination > start && destination < end || address.Addend != 0 &&
				Math.Min(target, destination) < end && Math.Max(target, destination) >= start;
		});

	private sealed class ObservedLabels(Action onRead) : IReadOnlyList<string>
	{
		public int Count => 0;
		public string this[int index] => throw new ArgumentOutOfRangeException(nameof(index));
		public IEnumerator<string> GetEnumerator()
		{
			onRead();
			return Enumerable.Empty<string>().GetEnumerator();
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
