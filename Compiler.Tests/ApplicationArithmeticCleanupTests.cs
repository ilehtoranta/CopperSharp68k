using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed partial class M68kHotLoopTransportTests
{
	[Theory]
	[InlineData("shift", true)]
	[InlineData("mask", true)]
	[InlineData("extend", true)]
	[InlineData("address", true)]
	[InlineData("live-edge", false)]
	[InlineData("live-flags", false)]
	[InlineData("live-x", false)]
	[InlineData("divide", false)]
	[InlineData("memory", false)]
	[InlineData("return-address", false)]
	[InlineData("unowned", false)]
	[InlineData("cpu", false)]
	public void ApplicationDeadArithmeticPreservesObservableState(string shape, bool smaller)
	{
		CheckApplicationCleanup(Build(false), Build(true), smaller);
		LinkedCode Build(bool enabled)
		{
			var a = ArithmeticAssembler(enabled, "dead", shape);
			a.Mark("start"); a.EmitWord(0x5286); // Keep candidate away from referenced entry.
			if (shape == "return-address") a.MarkReturnAddress("resume");
			a.EmitWord(shape switch { "mask" => 0x0280, "extend" => 0x48C0,
				"address" => 0xD1C3, "divide" => 0x80C3, "memory" => 0xD090, _ => 0xE188 });
			if (shape == "mask") a.EmitLong(0xFFFF);
			if (shape is "live-flags" or "live-x") SnapshotFlags(a);
			a.EmitWord(0x4A84); a.EmitBranch(M68kCondition.Equal, "other");
			a.EmitWord(0x5285); a.EmitBranch(M68kCondition.True, "join");
			a.Mark("other");
			if (shape == "live-edge") a.EmitWord(0x2600);
			a.EmitWord(0x5385); a.Mark("join");
			a.EmitWord(shape == "address" ? (ushort)0x204B : (ushort)0x7000);
			// Clear X as well when it is not part of this fixture's observation.
			if (shape != "live-x") a.EmitWord(0xD484); // ADD.L D4,D2
			Return(a); a.Mark("end");
			return Finish(a, shape == "cpu" ? M68kCpuTarget.M68020 : M68kCpuTarget.M68000);
		}
	}

	[Theory]
	[InlineData("byte", true)]
	[InlineData("word", true)]
	[InlineData("extend-byte", true)]
	[InlineData("extend-word", true)]
	[InlineData("partial", true)]
	[InlineData("loop", true)]
	[InlineData("long-add", true)]
	[InlineData("long-add-flags", false)]
	[InlineData("shift-down", false)]
	[InlineData("zero-byte", true)]
	[InlineData("compare-unsigned", true)]
	[InlineData("compare-immediate", true)]
	[InlineData("compare-signed", false)]
	[InlineData("live-upper", false)]
	[InlineData("live-flags", false)]
	[InlineData("upper-through-swap", false)]
	[InlineData("call", false)]
	[InlineData("return-address", false)]
	[InlineData("unowned", false)]
	[InlineData("cpu", false)]
	public void ApplicationNarrowTrackingFollowsEverySuccessor(string shape, bool smaller)
	{
		CheckApplicationCleanup(Build(false), Build(true), smaller);
		LinkedCode Build(bool enabled)
		{
			var a = ArithmeticAssembler(enabled, "narrow", shape);
			a.Mark("start"); a.EmitWord(0x5285); // D0 is an incoming value, including nonzero upper bits.
			if (shape.StartsWith("compare-")) { a.EmitWord(0x0283); a.EmitLong(0xFF); }
			if (shape == "return-address") a.MarkReturnAddress("resume");
			var word = shape is "word" or "extend-word";
			if (shape == "zero-byte") { a.EmitWord(0x7000); a.EmitWord(0x1007); }
			else if (shape.StartsWith("extend-")) a.EmitWord(word ? (ushort)0x48C0 : (ushort)0x4880);
			else { a.EmitWord(0x0280); a.EmitLong(word ? 0xFFFFu : 0xFFu); }
			if (shape == "live-flags") SnapshotFlags(a);
			if (shape == "call") a.EmitBsr("callee");
			if (shape.StartsWith("long-add")) {
				a.EmitWord(0xD087); // ADD.L D7,D0; only the low byte will be consumed.
				if (shape == "long-add-flags") SnapshotFlags(a);
				else a.EmitWord(0xD484); // Overwrite arithmetic flags, including X.
			}
			if (shape == "shift-down") a.EmitWord(0xE088); // LSR.L #8,D0 exposes formerly masked bits.
			a.EmitWord(0x4A84); a.EmitBranch(M68kCondition.Equal, "other");
			if (shape == "partial") a.EmitWord(0x1003); // MOVE.B D3,D0 preserves dirty upper bits.
			if (shape == "compare-immediate") { a.EmitWord(0x0C80); a.EmitLong(128); }
			else a.EmitWord(shape.StartsWith("compare-") ? (ushort)0xB680 : word ? (ushort)0xB640 : (ushort)0xB600);
			if (shape.StartsWith("compare-")) a.EmitWord(shape == "compare-signed" ? (ushort)0x5DC6 : (ushort)0x55C6); // SLT/SCS D6
			else SnapshotFlags(a);
			a.EmitBranch(M68kCondition.True, "join");
			a.Mark("other");
			if (shape == "live-upper") a.EmitWord(0x2600);
			else if (shape == "upper-through-swap") {
				// Observe an upper bit via SWAP, without dereferencing an invalid pointer.
				a.EmitWord(0x2600); a.EmitWord(0x4843);
			}
			if (shape == "loop") {
				a.EmitWord(0x7202); a.Mark("loop");
				a.EmitWord(0x1080); a.EmitWord(0x5341); a.EmitBranch(M68kCondition.NotEqual, "loop");
			}
			a.EmitWord(word ? (ushort)0x3080 : (ushort)0x1080); // Same-width store.
			a.Mark("join"); a.EmitWord(0x7000); a.EmitWord(0xD484);
			Return(a); a.Mark("end");
			if (shape == "call") { a.Mark("callee"); a.EmitWord(0x2600); a.EmitWord(0x4E75); }
			return Finish(a, shape == "cpu" ? M68kCpuTarget.M68020 : M68kCpuTarget.M68000);
		}
	}

	[Theory]
	[InlineData(0x8000, 0)] [InlineData(0x8000, 1)] [InlineData(0x8000, 2)]
	[InlineData(0xC000, 0)] [InlineData(0xC000, 1)] [InlineData(0xC000, 2)]
	[InlineData(0x9000, 0)] [InlineData(0x9000, 1)] [InlineData(0x9000, 2)]
	[InlineData(0xD000, 0)] [InlineData(0xD000, 1)] [InlineData(0xD000, 2)]
	[InlineData(0xB000, 0)] [InlineData(0xB000, 1)] [InlineData(0xB000, 2)]
	public void ApplicationMemoryOperandsPreserveWidthsOrderAndCcr(int operation, int size)
	{
		// A register-only gap exercises the new nonadjacent path even for ADD/SUB/CMP.L.
		foreach (var mode in new[] { "indirect", "displacement", "indexed" })
			// Copper68k 1.5.1 lacks exact 020 timings for OR.B/W memory sources.
			// These forms still execute on 000 and 040; every other form runs on all three.
			CheckApplicationCleanup(MemoryProgram(false, mode, operation, size), MemoryProgram(true, mode, operation, size), true,
				operation == 0x8000 && size < 2 ? [M68kCpuModel.M68000, M68kCpuModel.M68040] : Models);
	}

	[Theory]
	[InlineData("live-temporary")]
	[InlineData("live-flags")]
	[InlineData("gap-reads-flags")]
	[InlineData("gap-destination")]
	[InlineData("memory-gap")]
	[InlineData("entry-consumer")]
	[InlineData("address-addend")]
	[InlineData("return-address")]
	[InlineData("unowned")]
	[InlineData("cpu")]
	public void ApplicationMemoryOperandsRejectObservableInterference(string shape) =>
		CheckApplicationCleanup(MemoryProgram(false, shape, 0xC000, 2), MemoryProgram(true, shape, 0xC000, 2), false);

	private static LinkedCode MemoryProgram(bool enabled, string shape, int operation, int size)
	{
		var a = ArithmeticAssembler(enabled, "memory", shape);
		a.Mark("start"); a.EmitWord(0x5285);
		if (shape == "entry-consumer") { a.EmitWord(0x4A84); a.EmitBranch(M68kCondition.Equal, "use"); }
		if (shape == "return-address") a.MarkReturnAddress("resume");
		a.Mark("load");
		var family = size == 0 ? 0x1000 : size == 1 ? 0x3000 : 0x2000;
		a.EmitWord((ushort)(family | 1 << 9 | (shape == "displacement" ? 0x28 : shape == "indexed" ? 0x30 : 0x10)));
		if (shape == "displacement") a.EmitWord(2);
		if (shape == "indexed") a.EmitWord(0x2800); // D2.L, initialized to zero.
		a.EmitWord(shape switch { "gap-destination" => 0x5280, "memory-gap" => 0x2610,
			"gap-reads-flags" => 0x57C3, "live-flags" => 0x7601, _ => 0x224B }); // MOVEA.L A3,A1
		a.Mark("use"); a.EmitWord((ushort)(operation | size << 6 | 1));
		SnapshotFlags(a);
		if (shape == "live-temporary") a.EmitWord(0x2601);
		a.EmitWord(0x7200); Return(a); a.Mark("end");
		if (shape == "address-addend") { a.MarkDataStart(); a.EmitAddress("load", addend: 4); }
		return Finish(a, shape == "cpu" ? M68kCpuTarget.M68020 : M68kCpuTarget.M68000);
	}

	private static M68kAssembler ArithmeticAssembler(bool enabled, string pass, string shape) => new() {
		GeneratedTransportRegistersOnly = true,
		GeneratedCodeSizeOptions = new() {
			RemoveApplicationDeadArithmetic = enabled && pass == "dead",
			TrackApplicationNarrowValues = enabled && pass == "narrow",
			FoldApplicationMemoryOperands = enabled && pass == "memory" },
		GeneratedCodeSizeRanges = shape == "unowned" ? [] : [("start", "end")]
	};

	private static void CheckApplicationCleanup(LinkedCode before, LinkedCode after, bool smaller, M68kCpuModel[]? models = null)
	{
		Assert.True(smaller == (after.Bytes.Length < before.Bytes.Length),
			$"Expected smaller={smaller}: {Convert.ToHexString(before.Bytes)} -> {Convert.ToHexString(after.Bytes)}");
		foreach (var model in models ?? Models)
		foreach (var value in new uint[] { 0, 1, 0x7F, 0x80, 0xFFFF, 0x80000000, 0xFFFFFFFF })
		foreach (var flags in Enumerable.Range(0, 32))
			Equal(Run(before, model, value, flags, arithmeticInputs: true), Run(after, model, value, flags, arithmeticInputs: true));
	}
}
