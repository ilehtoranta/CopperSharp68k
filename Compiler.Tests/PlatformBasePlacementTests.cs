using System.Buffers.Binary;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class PlatformBasePlacementTests
{
	[Theory]
	[InlineData(-32769, 0)]
	[InlineData(-32768, 1)]
	[InlineData(32767, 1)]
	[InlineData(32768, 0)]
	public void SlotReadCandidateMustFitSignedPcDisplacement(int displacement, int expected)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2C79); // MOVEA.L slot,A6
		assembler.EmitAddress("slot");
		Assert.Equal(expected, assembler.CountPcRelativeReadCandidates("slot", 2 + displacement));
	}

	[Fact]
	public void CountsOnlyExactLocalCodeReadsWithoutAddends()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2C79); assembler.EmitAddress("slot"); // MOVEA.L
		assembler.EmitWord(0x2039); assembler.EmitAddress("slot"); // MOVE.L to D0
		assembler.EmitWord(0x23C0); assembler.EmitAddress("slot"); // store
		assembler.EmitWord(0x41F9); assembler.EmitAddress("slot"); // LEA
		assembler.EmitWord(0x2C79); assembler.EmitAddress("slot-other");
		assembler.EmitWord(0x2C79); assembler.EmitAddress("slot", external: true);
		assembler.EmitWord(0x2C79); assembler.EmitAddress("slot", addend: 4);
		assembler.MarkDataStart();
		assembler.EmitWord(0x2C79); assembler.EmitAddress("slot"); // data resembling code
		Assert.Equal(2, assembler.CountPcRelativeReadCandidates("slot", 100));
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, true)]
	[InlineData(M68kCpuTarget.M68020, false)]
	public void FrequentSlotsShrinkCodeAndRelocationsOnlyForEligibleApplications(M68kCpuTarget cpu, bool eligible)
	{
		var request = new M68kCompilationRequest {
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "ConsoleIO.dll"),
			EntryPoint = "ConsoleIOExample.Program::Main", Cpu = cpu,
			OutputFormat = M68kOutputFormat.Hunk, RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = M68kExceptionMode.Full, IncludedExportNames = [],
			Hunk = new() { IncludeSymbols = false },
			CodeSizeOptimizations = new() { CompactApplicationPlatformBases = false }
		};
		var before = AmigaM68kCompiler.Compile(request);
		var after = AmigaM68kCompiler.Compile(request with { CodeSizeOptimizations = new() });
		if (!eligible) { Assert.Equal(before.Image, after.Image); return; }
		Assert.True(after.Image.Length < before.Image.Length);
		Assert.True(after.Relocations.Count < before.Relocations.Count);
		foreach (var name in new[] { "_ExecBase", "_DOSLibraryBase" })
		{
			var newSlot = after.Symbols.Single(symbol => symbol.Name == name).Address;
			// The writer can merge BSS into CODE when that container is smaller.
			// Verify the reduced absolute references independently of that choice.
			Assert.True(after.Relocations.Count(relocation => relocation.Target == name) <
				before.Relocations.Count(relocation => relocation.Target == name));
			Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(after.Code.AsSpan((int)newSlot, 4)));
		}
		var noRelative = request with { CodeSizeOptimizations = new() {
			CompactApplicationPlatformBases = false, CompactApplicationAddresses = false } };
		Assert.Equal(AmigaM68kCompiler.Compile(noRelative).Image,
			AmigaM68kCompiler.Compile(noRelative with { CodeSizeOptimizations =
				noRelative.CodeSizeOptimizations! with { CompactApplicationPlatformBases = true } }).Image);
	}
}
