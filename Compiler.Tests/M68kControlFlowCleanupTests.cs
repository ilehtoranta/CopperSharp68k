/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kControlFlowCleanupTests
{
	private const uint Origin = 0x0001_0000;
	private const uint Stack = 0x0008_0000;
	private const uint Return = 0x0000_1000;
	private const string Entry = "method:entry";
	private const string Edge = "generated:allocated-switch-edge:0";
	private const string Case = "method:entry:BB000A";

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void InvertedBooleanPreservesEveryConditionAndExtendBit(
		M68kCpuTarget target, M68kCpuModel model)
	{
		for (var condition = 0; condition < 16; condition++)
		{
			var original = CreateBoolean(condition);
			var expectedCode = original.Link(Origin, new Dictionary<string, uint>());
			var optimized = CreateBoolean(condition);
			optimized.OptimizeForCpu(target);
			var actualCode = optimized.Link(Origin, new Dictionary<string, uint>());
			Assert.True(actualCode.Bytes.Length < expectedCode.Bytes.Length);
			for (ushort flags = 0; flags < 32; flags++)
			{
				AssertEquivalent(Execute(expectedCode, model, flags: flags),
					Execute(actualCode, model, flags: flags));
			}
		}
	}

	[Fact]
	public void KeepsFirstBooleanWhenAnotherRegisterStillObservesIt()
	{
		var assembler = CreateBoolean((int)M68kCondition.Higher, firstRegister: 2);
		assembler.OptimizeForM68000();
		Assert.Equal(2, assembler.GetInstructionStream().Count(instruction =>
			(instruction.Opcode & 0xF0F8) == 0x50C0));
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		var original = CreateBoolean((int)M68kCondition.Higher, firstRegister: 2)
			.Link(Origin, new Dictionary<string, uint>());
		for (ushort flags = 0; flags < 32; flags++)
		{
			AssertEquivalent(Execute(original, M68kCpuModel.M68000, flags: flags),
				Execute(optimized, M68kCpuModel.M68000, flags: flags));
		}
	}

	[Fact]
	public void KeepsAlternateEntryInsideBooleanNormalization()
	{
		var assembler = CreateBoolean((int)M68kCondition.Equal, interiorLabel: true);
		assembler.OptimizeForM68000();
		Assert.Equal(2, assembler.GetInstructionStream().Count(instruction =>
			(instruction.Opcode & 0xF0F8) == 0x50C0));
		Assert.Contains("boolean:second", assembler.Labels.Keys);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, true)]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, false)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, true)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, false)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, true)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, false)]
	public void AuditedBooleanShapesFoldAcrossUnreferencedSourceMarkers(
		M68kCpuTarget target, M68kCpuModel model, bool isMapped)
	{
		var original = CreateAuditedBoolean(isMapped).Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateAuditedBoolean(isMapped);
		assembler.OptimizeForCpu(target);
		var set = Assert.Single(assembler.GetExecutableInstructionStream(), instruction =>
			(instruction.Opcode & 0xF0F8) == 0x50C0);
		Assert.Equal(isMapped ? 0x53C0 : 0x56C0, set.Opcode); // SLS / SNE D0.
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		Assert.True(optimized.Bytes.Length < original.Bytes.Length);
		Assert.All(assembler.Labels.Where(label => label.Key.Contains(":IL_", StringComparison.Ordinal)),
			label => Assert.Contains(assembler.GetExecutableInstructionStream(),
				instruction => instruction.Offset == label.Value));
		foreach (var input in new uint[] { 0, 1, 0xFFFF_CFFE, 0xFFFF_CFFF, 0xFFFF_D000,
			0x0000_FEDA, 0xFFFF_FEDA, 0xFFFF_FEDB })
		{
			for (ushort flags = 0; flags < 32; flags++)
				AssertEquivalent(Execute(original, model, d1: input, flags: flags),
					Execute(optimized, model, d1: input, flags: flags));
		}
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public void BooleanSourceMarkerRemainsAnEntryWhenReferenced(int referenceKind)
	{
		var assembler = CreateAuditedBoolean(isMapped: true, referenceKind);
		assembler.OptimizeForM68000();
		Assert.Equal(2, assembler.GetExecutableInstructionStream().Count(instruction =>
			(instruction.Opcode & 0xF0F8) == 0x50C0));
		var code = assembler.Link(Origin, new Dictionary<string, uint>());
		var target = code.Labels[Entry + (referenceKind == 3 ? ":IL_0009" : ":IL_0005")];
		Assert.Equal(referenceKind == 3 ? 0x241F : 0x57C0, ReadWord(code.Bytes, target));
		var table = code.Labels["referenced:marker"];
		if (referenceKind == 2)
			Assert.Equal(target, table + unchecked((short)ReadWord(code.Bytes, table)));
		else
			Assert.Equal(Origin + (uint)target, ReadLong(code.Bytes, table));
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void ReplacesWholeBareReturnChainWithoutChangingResults(
		M68kCpuTarget target, M68kCpuModel model)
	{
		var original = CreateReturnChain().Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateReturnChain();
		assembler.OptimizeForCpu(target);
		Assert.DoesNotContain(assembler.GetInstructionStream(), instruction =>
			instruction.Kind == M68kInstructionKind.UnconditionalBranch);
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		foreach (var input in new uint[] { 0, 1, 0xFFFF_FFFF })
		{
			var expected = Execute(original, model, input);
			var actual = Execute(optimized, model, input);
			AssertEquivalent(expected, actual);
			Assert.True(actual.Instructions <= expected.Instructions);
			// Linking already removes fallthrough branches on the zero path.
			// The nonzero path still traverses a return branch in the original.
			if (input != 0) Assert.True(actual.Instructions < expected.Instructions);
		}
		Assert.Contains("tail:b", optimized.Labels.Keys);
		Assert.Contains("tail:c", optimized.Labels.Keys);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void ConditionalTailCallRetainsSharedReturnEntry(
		M68kCpuTarget target, M68kCpuModel model)
	{
		var original = CreateConditionalCall().Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateConditionalCall();
		assembler.OptimizeForCpu(target);
		Assert.DoesNotContain(assembler.GetInstructionStream(), instruction =>
			instruction.Kind == M68kInstructionKind.Call);
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		Assert.Equal(0x4E75, ReadWord(optimized.Bytes, optimized.Labels["shared:return"]));
		foreach (var input in new uint[] { 0, 5 })
		{
			AssertEquivalent(Execute(original, model, input), Execute(optimized, model, input));
		}
	}

	[Fact]
	public void DoesNotTurnBranchCycleIntoReturn()
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.Mark("cycle:a");
		assembler.EmitBranch(M68kCondition.True, "cycle:b");
		assembler.Mark("cycle:b");
		assembler.EmitBranch(M68kCondition.True, "cycle:a");
		assembler.Mark(Entry + ":end");
		assembler.OptimizeForM68000();
		Assert.DoesNotContain(assembler.GetInstructionStream(), instruction => instruction.Opcode == 0x4E75);
		Assert.Contains(assembler.GetInstructionStream(), instruction =>
			instruction.Kind == M68kInstructionKind.UnconditionalBranch);
	}

	[Fact]
	public void KeepsSharedReturnCallWhoseCalleeReadsIncomingStack()
	{
		var original = CreateConditionalCall(readStack: true).Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateConditionalCall(readStack: true);
		assembler.OptimizeForM68000();
		Assert.Contains(assembler.GetInstructionStream(), instruction =>
			instruction.Kind == M68kInstructionKind.Call);
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		foreach (var input in new uint[] { 0, 5 })
		{
			AssertEquivalent(Execute(original, M68kCpuModel.M68000, input),
				Execute(optimized, M68kCpuModel.M68000, input));
		}
	}

	[Fact]
	public void SwitchEdgesKeepNamedAddressAndPcRelativeRelocations()
	{
		var original = CreateSwitch().Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateSwitch();
		assembler.OptimizeForM68000();
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		Assert.Equal(optimized.Labels[Case], optimized.Labels[Edge]);
		var table = optimized.Labels["switch:table"];
		Assert.Equal(Origin + (uint)optimized.Labels[Case], ReadLong(optimized.Bytes, table));
		Assert.Equal(optimized.Labels[Case] - (table + 4),
			unchecked((short)ReadWord(optimized.Bytes, table + 4)));
		foreach (var input in new uint[] { 0, 1, 0xFFFF_FFFF })
		{
			AssertEquivalent(Execute(original, M68kCpuModel.M68000, input),
				Execute(optimized, M68kCpuModel.M68000, input));
		}
	}

	[Theory]
	[InlineData(true, 0)]
	[InlineData(false, 2)]
	public void KeepsSwitchEdgeWithPhiCopyOrInteriorAddress(bool copy, int addend)
	{
		var original = CreateSwitch(copy, addend).Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateSwitch(copy, addend);
		assembler.OptimizeForM68000();
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		Assert.NotEqual(optimized.Labels[Case], optimized.Labels[Edge]);
		AssertEquivalent(Execute(original, M68kCpuModel.M68000, 0),
			Execute(optimized, M68kCpuModel.M68000, 0));
	}

	[Fact]
	public void DoesNotRedirectAlreadyShortBranchBeyondItsRange()
	{
		var assembler = CreateSwitch(padding: 96);
		assembler.RelaxBranches();
		assembler.OptimizeForM68000();
		var linked = assembler.Link(Origin, new Dictionary<string, uint>());
		Assert.NotEqual(linked.Labels[Edge], linked.Labels[Case]);
		Assert.Equal(40u, Execute(linked, M68kCpuModel.M68000, 0).D0);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void ConstantEdgeBypassesTestButUnknownPredecessorStillTests(
		M68kCpuTarget target, M68kCpuModel model)
	{
		var original = CreateConstantEdge().Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateConstantEdge();
		assembler.OptimizeForCpu(target);
		var known = assembler.GetInstructionStream().Where(instruction =>
			instruction.Offset >= assembler.Labels["known:zero"]).Take(2).ToArray();
		Assert.Equal(assembler.Labels["result:zero"], known[1].TargetOffset);
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		foreach (var selector in new uint[] { 0, 1 })
		foreach (var value in new uint[] { 0, 1, 0x80, 0xFFFF_FF00 })
		{
			AssertEquivalent(Execute(original, model, d1: selector, d2: value, flags: 0x10),
				Execute(optimized, model, d1: selector, d2: value, flags: 0x10));
		}
	}

	[Fact]
	public void ScopedConstantEdgeKeepsAnUnlabelledFallthroughEntry()
	{
		var original = CreateConstantEdge(labelFallthrough: false)
			.Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateConstantEdge(labelFallthrough: false);
		assembler.OptimizeForM68000();
		var known = assembler.GetInstructionStream().Where(instruction =>
			instruction.Offset >= assembler.Labels["known:zero"]).Take(2).ToArray();
		Assert.Equal(assembler.Labels["boolean:test"], known[1].TargetOffset);
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		foreach (var selector in new uint[] { 0, 1 })
		foreach (var value in new uint[] { 0, 1, 0x80, 0xFFFF_FF00 })
		{
			AssertEquivalent(Execute(original, M68kCpuModel.M68000, d1: selector, d2: value, flags: 0x10),
				Execute(optimized, M68kCpuModel.M68000, d1: selector, d2: value, flags: 0x10));
		}
	}

	[Theory]
	[InlineData(0x0C64, false)]
	[InlineData(-32768, false)]
	[InlineData(32767, false)]
	[InlineData(0x1234, true)]
	public void UsesLeaForDeadConstantAddressTemporary(int constant, bool subtract)
	{
		var original = CreateAddressDisplacement(constant, subtract)
			.Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateAddressDisplacement(constant, subtract);
		assembler.OptimizeForM68000();
		Assert.Contains(assembler.GetInstructionStream(), instruction =>
			(instruction.Opcode & 0xF1F8) == 0x41E8);
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		Assert.True(optimized.Bytes.Length < original.Bytes.Length);
		AssertEquivalent(Execute(original, M68kCpuModel.M68000, flags: 0x10),
			Execute(optimized, M68kCpuModel.M68000, flags: 0x10));
	}

	[Theory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public void KeepsAddressTemporaryWhoseValueOrFlagsAreObserved(bool observeValue, bool observeFlags)
	{
		var original = CreateAddressDisplacement(0x0C64, false, observeValue, observeFlags)
			.Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateAddressDisplacement(0x0C64, false, observeValue, observeFlags);
		assembler.OptimizeForM68000();
		Assert.DoesNotContain(assembler.GetInstructionStream(), instruction =>
			(instruction.Opcode & 0xF1F8) == 0x41E8);
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		AssertEquivalent(Execute(original, M68kCpuModel.M68000), Execute(optimized, M68kCpuModel.M68000));
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void AuditedLocalFirstBlockUsesLeaWithCallsAndSourceMarkers(
		M68kCpuTarget target, M68kCpuModel model)
	{
		var original = CreateAuditedAddressDisplacement().Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateAuditedAddressDisplacement();
		assembler.OptimizeForCpu(target);
		Assert.Contains(assembler.GetExecutableInstructionStream(), instruction =>
			instruction.Opcode == 0x41E8 && instruction.ExtensionWord == 0x0C64);
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		Assert.True(optimized.Bytes.Length < original.Bytes.Length);
		for (ushort flags = 0; flags < 32; flags++)
		{
			var expected = Execute(original, model, flags: flags);
			var actual = Execute(optimized, model, flags: flags);
			AssertEquivalent(expected, actual);
			Assert.Equal(0x3C80u, actual.D0);
		}
	}

	[Fact]
	public void ConstantAddressSourceMarkerCannotMoveWhenReferenced()
	{
		var assembler = CreateAuditedAddressDisplacement(referencedAdd: true);
		assembler.OptimizeForM68000();
		Assert.DoesNotContain(assembler.GetExecutableInstructionStream(), instruction =>
			instruction.Opcode == 0x41E8 && instruction.ExtensionWord == 0x0C64);
		var code = assembler.Link(Origin, new Dictionary<string, uint>());
		var target = code.Labels[Entry + ":IL_0011"];
		Assert.Equal(Origin + (uint)target, ReadLong(code.Bytes, code.Labels["referenced:marker"]));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void AddressCopyUsesCalleeOverwriteProofAndKeepsObservedArgument(bool calleeReadsArgument)
	{
		var original = CreateAddressCopy(calleeReadsArgument).Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateAddressCopy(calleeReadsArgument);
		assembler.OptimizeForM68000();
		Assert.Equal(calleeReadsArgument, assembler.GetInstructionStream().Any(instruction =>
			instruction.Opcode == 0x2008)); // MOVE.L A0,D0
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		AssertEquivalent(Execute(original, M68kCpuModel.M68000), Execute(optimized, M68kCpuModel.M68000));
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.Bounded)]
	[InlineData(M68kCpuTarget.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68020, M68kPeepholeOptimizationMode.Bounded)]
	[InlineData(M68kCpuTarget.M68040, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68040, M68kPeepholeOptimizationMode.Bounded)]
	public void ControlPatternsInDataRemainByteExact(M68kCpuTarget target,
		M68kPeepholeOptimizationMode mode)
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitWord(0x702A);
		assembler.EmitWord(0x4E75);
		assembler.MarkDataStart();
		assembler.Mark("literal:control-patterns");
		ushort[] payload =
		[
			0x56C0, 0x4880, 0x48C0, 0x4480, 0x57C0, 0x4880, 0x48C0, 0x4480, 0x4E75,
			0x7000, 0xB681, 0x56C0, 0x4880, 0x48C0, 0x4480, 0x4E75,
			0xE188, 0xE188, 0x4E75,
			0x7003, 0xD1C0, 0x2008, 0x4E75,
			0x2008, 0x2C40, 0x7005, 0x4E75,
			0x4EA8, 0x0004, 0x4E75
		];
		foreach (var word in payload) assembler.EmitWord(word);
		var original = assembler.Link(Origin, new Dictionary<string, uint>());

		assembler.OptimizeForCpu(target, peepholeOptimization: mode);
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());

		Assert.Equal(original.Bytes, optimized.Bytes);
		Assert.Equal(original.DataStartOffset, optimized.DataStartOffset);
		Assert.Equal(original.Labels["literal:control-patterns"], optimized.Labels["literal:control-patterns"]);
		// Full-image inspection outside an optimization round still includes data.
		Assert.Contains(assembler.GetInstructionStream(), instruction =>
			instruction.Offset >= assembler.Labels["literal:control-patterns"]);
	}

	[Fact]
	public void SharedReturnCallDoesNotTrustAnAbsoluteJumpLabelWithoutItsAddend()
	{
		var original = CreateConditionalCallWithJumpAddend().Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateConditionalCallWithJumpAddend();
		assembler.OptimizeForM68000();
		Assert.Contains(assembler.GetExecutableInstructionStream(), instruction =>
			instruction.Kind == M68kInstructionKind.Call);
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		foreach (var input in new uint[] { 0, 5 })
		{
			AssertEquivalent(Execute(original, M68kCpuModel.M68000, input),
				Execute(optimized, M68kCpuModel.M68000, input));
		}
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void AddressCopyKeepsConditionCodesReadByTheKnownCallee(
		M68kCpuTarget target, M68kCpuModel model)
	{
		var original = CreateAddressCopyWithFlagReadingCallee().Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateAddressCopyWithFlagReadingCallee();
		assembler.OptimizeForCpu(target);
		Assert.Contains(assembler.GetExecutableInstructionStream(), instruction => instruction.Opcode == 0x2008);
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		for (ushort flags = 0; flags < 32; flags++)
		{
			AssertEquivalent(Execute(original, model, flags: flags), Execute(optimized, model, flags: flags));
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void RepeatedNullAssertionReturnsWithTheSameStoreAndStack(bool exceptionCall)
	{
		var original = CreateRepeatedNullAssertion(exceptionCall).Link(Origin, new Dictionary<string, uint>());
		var assembler = CreateRepeatedNullAssertion(exceptionCall);
		assembler.OptimizeForM68000();
		var optimized = assembler.Link(Origin, new Dictionary<string, uint>());
		var expected = Execute(original, M68kCpuModel.M68000);
		var actual = Execute(optimized, M68kCpuModel.M68000);
		// Return flags are not part of the null-check ABI; compare all visible
		// data/address results, the completed store, and the restored stack.
		AssertEquivalent(expected with { Ccr = actual.Ccr }, actual);
		Assert.Equal(0xFFFF_FFFFu, actual.Stored);
	}

	private static M68kAssembler CreateBoolean(int condition, int firstRegister = 0, bool interiorLabel = false)
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		EmitBoolean(assembler, condition, firstRegister);
		assembler.EmitWord((ushort)(0x4A80 | firstRegister));
		if (interiorLabel) assembler.Mark("boolean:second");
		EmitBoolean(assembler, (int)M68kCondition.Equal, 0);
		assembler.EmitWord(0x40C1); // Observe all CCR bits, including X, in D1.
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		return assembler;
	}

	private static void EmitBoolean(M68kAssembler assembler, int condition, int register)
	{
		assembler.EmitWord((ushort)(0x50C0 | (condition << 8) | register));
		assembler.EmitWord((ushort)(0x4880 | register));
		assembler.EmitWord((ushort)(0x48C0 | register));
		assembler.EmitWord((ushort)(0x4480 | register));
	}

	private static M68kAssembler CreateAuditedBoolean(bool isMapped, int referenceKind = -1)
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		if (isMapped)
		{
			// IsMapped's final allocated body: preserve D2 and compare the pointer
			// against uint.MaxValue - length before inverting the comparison.
			assembler.EmitWord(0x2F02);
			assembler.EmitWord(0x2408);
			assembler.EmitWord(0x70FF);
			assembler.EmitWord(0x9081);
			assembler.EmitWord(0xB480);
		}
		else
		{
			// WritesD0's last comparison against the negative vector offset -294.
			assembler.EmitWord(0x203C);
			assembler.EmitLong(0xFFFF_FEDA);
			assembler.EmitWord(0xB240);
		}
		assembler.Mark("normalization:start");
		EmitMarkedWord(isMapped ? (ushort)0x52C0 : (ushort)0x57C0, "0000");
		EmitMarkedWord(0x4880, "0001");
		EmitMarkedWord(0x48C0, "0002");
		EmitMarkedWord(0x4480, "0003");
		// An addend entry must stay fixed even without an explicit TST between
		// the normalizations; NEG already supplies the zero flag for SEQ.
		if (referenceKind != 1) EmitMarkedWord(0x4A80, "0004");
		var secondOffset = assembler.Offset - assembler.Labels["normalization:start"];
		EmitMarkedWord(0x57C0, "0005");
		EmitMarkedWord(0x4880, "0006");
		EmitMarkedWord(0x48C0, "0007");
		EmitMarkedWord(0x4480, "0008");
		var afterOffset = assembler.Offset - assembler.Labels["normalization:start"];
		assembler.Mark(Entry + ":IL_0009");
		if (isMapped) assembler.EmitWord(0x241F);
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		if (referenceKind >= 0)
		{
			assembler.MarkDataStart();
			assembler.Mark("referenced:marker");
			if (referenceKind == 0) assembler.EmitAddress(Entry + ":IL_0005");
			else if (referenceKind == 1) assembler.EmitAddress("normalization:start", addend: secondOffset);
			else if (referenceKind == 2) assembler.EmitPcRelativeWord(Entry + ":IL_0005");
			else assembler.EmitAddress("normalization:start", addend: afterOffset);
		}
		return assembler;

		void EmitMarkedWord(ushort word, string suffix)
		{
			assembler.Mark(Entry + ":IL_" + suffix);
			assembler.EmitWord(word);
		}
	}

	private static M68kAssembler CreateReturnChain()
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitWord(0x4A80);
		assembler.EmitBranch(M68kCondition.Equal, "result:nine");
		assembler.EmitWord(0x7006);
		assembler.EmitBranch(M68kCondition.True, "tail:b");
		assembler.Mark("result:nine");
		assembler.EmitWord(0x7009);
		assembler.EmitBranch(M68kCondition.True, "tail:c");
		assembler.Mark("tail:b");
		assembler.EmitBranch(M68kCondition.True, "tail:c");
		assembler.Mark("tail:c");
		assembler.EmitBranch(M68kCondition.True, "tail:d");
		assembler.Mark("tail:d");
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		return assembler;
	}

	private static M68kAssembler CreateConditionalCall(bool readStack = false)
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitWord(0x4A80);
		assembler.EmitBranch(M68kCondition.Equal, "shared:return");
		assembler.EmitWord(0x2001); // MOVE.L D1,D0
		assembler.EmitCall("method:callee");
		assembler.Mark("shared:return");
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		assembler.Mark("method:callee");
		if (readStack)
		{
			assembler.EmitWord(0x202F); // MOVE.L 4(A7),D0 observes the caller's return address.
			assembler.EmitWord(4);
		}
		else
		{
			assembler.EmitWord(0x2200); // MOVE.L D0,D1
			assembler.EmitWord(0x7008); // MOVEQ #8,D0
		}
		assembler.EmitWord(0x4E75);
		assembler.Mark("method:callee:end");
		return assembler;
	}

	private static M68kAssembler CreateConditionalCallWithJumpAddend()
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitWord(0x4A80);
		assembler.EmitBranch(M68kCondition.Equal, "shared:return");
		assembler.EmitWord(0x2001);
		assembler.EmitCall("method:callee");
		assembler.Mark("shared:return");
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		assembler.Mark("method:callee");
		assembler.EmitWord(0x4EF9);
		assembler.EmitAddress("method:callee:landing", addend: 2);
		// Keep this jump absolute: branch relaxation is a separate concern.
		for (var index = 0; index < 16_384; index++) assembler.EmitWord(0x4E71);
		assembler.Mark("method:callee:landing");
		assembler.EmitWord(0x4E75); // The label alone appears stack-independent.
		assembler.EmitWord(0x202F); // Actual target: MOVE.L 4(A7),D0.
		assembler.EmitWord(4);
		assembler.EmitWord(0x4E75);
		assembler.Mark("method:callee:end");
		return assembler;
	}

	private static M68kAssembler CreateAddressCopyWithFlagReadingCallee()
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitWord(0x2008); // MOVE.L A0,D0 publishes Z=0.
		assembler.EmitWord(0x2C40);
		assembler.EmitCall("method:callee");
		assembler.EmitWord(0x2001);
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		assembler.Mark("method:callee");
		assembler.EmitWord(0x57C1); // SEQ D1 observes the caller's Z first.
		assembler.EmitWord(0x7005); // Incoming D0 is otherwise dead.
		assembler.EmitWord(0x4E75);
		assembler.Mark("method:callee:end");
		return assembler;
	}

	private static M68kAssembler CreateRepeatedNullAssertion(bool exceptionCall)
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitWord(0xBBFC); // CMPA.L #0,A5.
		assembler.EmitLong(0);
		assembler.EmitBranch(M68kCondition.NotEqual, "first:nonnull");
		EmitFailure();
		assembler.Mark("first:nonnull");
		assembler.EmitWord(0x2B42); // MOVE.L D2,8(A5).
		assembler.EmitWord(8);
		assembler.EmitWord(0xBBFC);
		assembler.EmitLong(0);
		assembler.EmitBranch(M68kCondition.NotEqual, "second:nonnull");
		EmitFailure();
		assembler.Mark("second:nonnull");
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		if (exceptionCall)
		{
			assembler.Mark("__c68k_exception_raise");
			assembler.EmitWord(0x4AFC);
			assembler.Mark("__c68k_exception_raise:end");
		}
		return assembler;

		void EmitFailure()
		{
			if (exceptionCall) assembler.EmitJsr("__c68k_exception_raise", external: false);
			else assembler.EmitWord(0x4AFC);
		}
	}

	private static M68kAssembler CreateSwitch(bool phiCopy = false, int addend = 0, int padding = 0)
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitWord(0x4A80);
		assembler.EmitBranch(M68kCondition.Equal, Edge);
		assembler.EmitBranch(M68kCondition.True, "switch:default");
		assembler.Mark(Edge);
		if (phiCopy) assembler.EmitWord(0x722A); // Actual edge work must remain.
		assembler.EmitBranch(M68kCondition.True, Case);
		assembler.Mark("switch:default");
		assembler.EmitWord(0x7008);
		assembler.EmitWord(0x4E75);
		for (var index = 0; index < padding; index++) assembler.EmitWord(0x4E71);
		assembler.Mark(Case);
		assembler.EmitWord(0x7028);
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		assembler.MarkDataStart();
		assembler.Mark("switch:table");
		assembler.EmitAddress(Edge, addend: addend);
		assembler.EmitPcRelativeWord(Edge);
		return assembler;
	}

	private static M68kAssembler CreateConstantEdge(bool labelFallthrough = true)
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitWord(0x4A81); // TST.L D1
		assembler.EmitBranch(M68kCondition.NotEqual, "unknown:value");
		assembler.Mark("known:zero");
		assembler.EmitWord(0x7000);
		assembler.EmitBranch(M68kCondition.True, "boolean:test");
		assembler.Mark("unknown:value");
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.Mark("boolean:test");
		assembler.EmitWord(0x4A00);
		assembler.EmitBranch(M68kCondition.NotEqual, "result:nonzero");
		if (labelFallthrough) assembler.Mark("result:zero");
		assembler.EmitWord(0x40C1);
		assembler.EmitWord(0x700B);
		assembler.EmitWord(0x4E75);
		assembler.Mark("result:nonzero");
		assembler.EmitWord(0x40C1);
		assembler.EmitWord(0x7016);
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		return assembler;
	}

	private static M68kAssembler CreateAddressDisplacement(int constant, bool subtract,
		bool observeValue = false, bool observeFlags = false)
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitWord(0x203C);
		assembler.EmitLong(unchecked((uint)constant));
		assembler.EmitWord(subtract ? (ushort)0x91C0 : (ushort)0xD1C0);
		if (observeValue) assembler.EmitWord(0x2200);
		if (observeFlags) assembler.EmitWord(0x40C1);
		assembler.EmitWord(0x2008); // Overwrite the temporary and flags with A0.
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		return assembler;
	}

	private static M68kAssembler CreateAuditedAddressDisplacement(bool referencedAdd = false)
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitCall("method:get_header");
		assembler.EmitWord(0x2040);
		assembler.Mark(Entry + ":IL_0010");
		assembler.EmitWord(0x203C);
		assembler.EmitLong(0x0C64);
		assembler.Mark(Entry + ":IL_0011");
		assembler.EmitWord(0xD1C0);
		assembler.Mark(Entry + ":IL_0012");
		assembler.EmitWord(0x2008);
		assembler.EmitWord(0x7210);
		assembler.EmitCall("method:align");
		assembler.EmitWord(0x2040);
		assembler.EmitWord(0x5088); // ADDQ.L #8,A0.
		assembler.EmitWord(0x2008);
		assembler.EmitWord(0x7210);
		assembler.EmitWord(0x4EF9);
		assembler.EmitAddress("method:align");
		assembler.Mark(Entry + ":end");
		assembler.Mark("method:get_header");
		assembler.EmitWord(0x2008);
		assembler.EmitWord(0x4E75);
		assembler.Mark("method:get_header:end");
		assembler.Mark("method:align");
		assembler.EmitWord(0x0680); // ADDI.L #15,D0.
		assembler.EmitLong(15);
		assembler.EmitWord(0x0280); // ANDI.L #$FFFFFFF0,D0.
		assembler.EmitLong(0xFFFF_FFF0);
		assembler.EmitWord(0x4E75);
		assembler.Mark("method:align:end");
		if (referencedAdd)
		{
			assembler.MarkDataStart();
			assembler.Mark("referenced:marker");
			assembler.EmitAddress(Entry + ":IL_0011");
		}
		return assembler;
	}

	private static M68kAssembler CreateAddressCopy(bool calleeReadsArgument)
	{
		var assembler = new M68kAssembler();
		assembler.Mark(Entry);
		assembler.EmitWord(0x2008);
		assembler.EmitWord(0x2C40); // MOVEA.L D0,A6
		assembler.EmitWord(0x204C); // MOVEA.L A4,A0
		assembler.EmitWord(0x224D); // MOVEA.L A5,A1
		assembler.EmitCall("method:callee");
		assembler.EmitWord(0x2200); // Use the callee's output, preventing a tail call.
		assembler.EmitWord(0x4E75);
		assembler.Mark(Entry + ":end");
		assembler.Mark("method:callee");
		if (calleeReadsArgument)
		{
			assembler.EmitWord(0x2340); // MOVE.L D0,8(A1)
			assembler.EmitWord(8);
		}
		assembler.EmitWord(0x2029); // MOVE.L 4(A1),D0
		assembler.EmitWord(4);
		assembler.EmitWord(0x4E75);
		assembler.Mark("method:callee:end");
		return assembler;
	}

	private sealed record Snapshot(uint D0, uint D1, uint D2, uint A0, uint A6,
		uint Stack, ushort Ccr, uint Stored, int Instructions);

	private static Snapshot Execute(LinkedCode code, M68kCpuModel model,
		uint d0 = 0xA5A5_5A5A, uint d1 = 0x1234, uint d2 = 0xFFFF_FFFF, ushort flags = 0)
	{
		var bus = new TestBus(0x0010_0000);
		code.Bytes.CopyTo(bus.Memory.AsSpan((int)Origin));
		bus.WriteLong(Stack, Return);
		bus.WriteLong(0x5004, 0x1234_5678);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(Origin + (uint)code.Labels[Entry], Stack);
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		cpu.State.D[0] = d0;
		cpu.State.D[1] = d1;
		cpu.State.D[2] = d2;
		cpu.State.A[0] = 0x3000;
		cpu.State.A[4] = 0x4000;
		cpu.State.A[5] = 0x5000;
		cpu.State.A[6] = 0x6000;
		for (var count = 0; count < 512; count++)
		{
			if (cpu.State.ProgramCounter == Return)
			{
				return new(cpu.State.D[0], cpu.State.D[1], cpu.State.D[2], cpu.State.A[0],
					cpu.State.A[6], cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 31),
					bus.ReadLong(0x5008), count);
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted, $"{model} halted at ${cpu.State.ProgramCounter:X8}.");
		}
		throw new Xunit.Sdk.XunitException("Control-flow fixture did not return.");
	}

	private static void AssertEquivalent(Snapshot expected, Snapshot actual) =>
		Assert.Equal(expected with { Instructions = 0 }, actual with { Instructions = 0 });

	private static ushort ReadWord(byte[] bytes, int offset) =>
		(ushort)((bytes[offset] << 8) | bytes[offset + 1]);

	private static uint ReadLong(byte[] bytes, int offset) =>
		((uint)ReadWord(bytes, offset) << 16) | ReadWord(bytes, offset + 2);
}
