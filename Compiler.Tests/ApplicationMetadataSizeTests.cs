using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[InlineData(M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kPeepholeOptimizationMode.Disabled)]
	public void ApplicationMetadataCompactExceptionPathsExecute(M68kPeepholeOptimizationMode mode)
	{
		foreach (var flags in Enumerable.Range(0, 8))
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly, EntryPoint = typeof(ApplicationMetadataFixtures).FullName + "::ExceptionPaths",
				Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk, RuntimeProfile = M68kRuntimeProfile.Application,
				ExceptionMode = M68kExceptionMode.Full, IncludedExportNames = [], Hunk = new() { IncludeSymbols = false },
				PeepholeOptimization = mode, CodeSizeOptimizations = new() {
					PruneApplicationUnwindMetadata = (flags & 1) != 0, NarrowApplicationExceptionSites = (flags & 2) != 0,
					ShareApplicationUnwindRestores = (flags & 4) != 0 }
			});
			Assert.Equal((flags & 2) != 0, result.Map.Contains("pass=NarrowApplicationExceptionSites", StringComparison.Ordinal));
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (var load in new uint[] { 0x10000, 0x20000 })
				Assert.Equal(42u, Execute(LoadApplicationSizeHunk(result.Image, load, 0x40000), model, load + result.EntryPoint,
					maxInstructions: 5_000_000, afterReturn: state => Assert.Equal(StackPointer + 4, state.A[7])));
		}
	}

	[Fact]
	public void ApplicationMetadataPassesReduceSizeIndependently()
	{
		var previous = new M68kCodeSizeOptions { PruneApplicationUnwindMetadata = false, NarrowApplicationExceptionSites = false, ShareApplicationUnwindRestores = false };
		var request = new M68kCompilationRequest {
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "ConsoleIO.dll"), EntryPoint = "ConsoleIOExample.Program::Main",
			Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk, RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = M68kExceptionMode.Full, Hunk = new() { IncludeSymbols = false }, CodeSizeOptimizations = previous
		};
		var baseline = AmigaM68kCompiler.Compile(request);
		foreach (var policy in new[] { previous with { PruneApplicationUnwindMetadata = true },
			previous with { NarrowApplicationExceptionSites = true }, new M68kCodeSizeOptions() })
		{
			var result = AmigaM68kCompiler.Compile(request with { CodeSizeOptimizations = policy });
			Assert.True(result.Image.Length < baseline.Image.Length);
			Assert.Equal(policy.NarrowApplicationExceptionSites,
				result.Map.Contains("pass=NarrowApplicationExceptionSites", StringComparison.Ordinal));
		}
	}

	[Fact]
	public void ApplicationMetadataSharedRestoresPreserveEverySavedRegister()
	{
		var request = new M68kCompilationRequest {
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "StopwatchBenchmark.dll"), EntryPoint = "StopwatchBenchmarkExample.Program::Main",
			Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk, RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = M68kExceptionMode.Full, Hunk = new() { IncludeSymbols = false },
			CodeSizeOptimizations = new() { ShareApplicationUnwindRestores = false, RelativeApplicationUnwindRestores = false, ShareApplicationUnwindHelper = false }
		};
		var before = AmigaM68kCompiler.Compile(request);
		var after = AmigaM68kCompiler.Compile(request with { CodeSizeOptimizations = new() { RelativeApplicationUnwindRestores = false, ShareApplicationUnwindHelper = false } });
		Assert.True(after.Image.Length < before.Image.Length);
		var restores = after.Relocations.Where(relocation => relocation.Target.StartsWith("runtime:method-unwind-restore:", StringComparison.Ordinal)).ToArray();
		Assert.Contains(restores.GroupBy(relocation => Read(after, relocation.Offset)), group =>
			group.Select(relocation => Read(after, relocation.Offset - 8)).Distinct().Count() > 1);
		foreach (var relocation in restores)
		foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
		{
			var old = before.Relocations.Single(item => item.Target == relocation.Target);
			Assert.Equal(Run(before, old.Offset, model), Run(after, relocation.Offset, model));
		}
		static uint Read(M68kCompilationResult result, int offset) => BinaryPrimitives.ReadUInt32BigEndian(result.Code.AsSpan(offset, 4));
		byte[] Run(M68kCompilationResult result, int pointerOffset, M68kCpuModel model)
		{
			const uint load = 0x10000, context = 0x50000, frame = 0x52000;
			var bus = LoadApplicationSizeHunk(result.Image, load, 0x40000);
			var frameBytes = Read(result, pointerOffset - 8);
			bus.WriteLong(context + 16, load + (uint)pointerOffset - 8);
			for (uint i = 0; i < 11; i++)
			{
				bus.WriteLong(frame + frameBytes + i * 4, 0x12340000 + i);
				bus.WriteLong(context + 24 + i * 4, 0x76540000 + i);
			}
			Execute(bus, model, load + Read(result, pointerOffset), initialize: state => {
				state.D[1] = context; state.A[1] = frame;
			}, afterReturn: state => {
				Assert.Equal(context, state.D[1]); Assert.Equal(StackPointer + 4, state.A[7]);
			});
			return bus.Memory.AsSpan((int)context + 24, 44).ToArray();
		}
	}

	[Fact]
	public void ApplicationMetadataNarrowLookupRejectsTruncatedPcAliases()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest {
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "ConsoleIO.dll"), EntryPoint = "ConsoleIOExample.Program::Main",
			Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk, RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = M68kExceptionMode.Full, Hunk = new() { IncludeSymbols = false }, CodeSizeOptimizations = new()
		});
		Assert.Contains("pass=NarrowApplicationExceptionSites", result.Map);
		// Locate the private helper by its PC-relative table load and range-check
		// prologue. The test then calls the emitted helper with valid/invalid PCs.
		byte[] prologue = [0x20, 0x09, 0x92, 0x80, 0x30, 0x01, 0x48, 0xc0, 0xb2, 0x80];
		var lookup = result.Code.AsSpan().IndexOf(prologue) - 4;
		Assert.True(lookup >= 0);
		Assert.Equal(0x43fa, BinaryPrimitives.ReadUInt16BigEndian(result.Code.AsSpan(lookup, 2)));
		foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
		foreach (var load in new uint[] { 0x10000, 0x20000 })
		{
			var bus = LoadApplicationSizeHunk(result.Image, load, 0x40000);
			var table = load + result.Symbols.Single(symbol => symbol.Name == "__c68k_method_table").Address;
			var count = bus.ReadLong(table);
			for (uint index = 0; index < count; index++)
			{
				var row = table + 4 + index * 8; // ConsoleIO: four narrow fields, no cleanup column.
				var pc = unchecked(table + (uint)(short)bus.ReadWord(row));
				foreach (var delta in new[] { 0, 1, 65536, -65536 })
				{
					var found = Execute(bus, model, load + (uint)lookup, initialize: state => state.D[1] = unchecked(pc + (uint)delta));
					Assert.Equal(delta == 0 ? 1u : 0u, found);
				}
			}
		}
	}

	[Fact]
	public void ApplicationMetadataLargeDisplacementsFallBackWithoutChangingBehavior()
	{
		var request = new M68kCompilationRequest {
			AssemblyPath = FixtureAssembly, EntryPoint = typeof(ApplicationMetadataFixtures).FullName + "::LargeTable",
			Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk, RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = M68kExceptionMode.Full, IncludedExportNames = [], Hunk = new() { IncludeSymbols = false },
			CodeSizeOptimizations = new() { NarrowApplicationExceptionSites = false }
		};
		var wide = M68kCompiler.Compile(request);
		var automatic = M68kCompiler.Compile(request with { CodeSizeOptimizations = new() });
		Assert.True(wide.Code.Length > short.MaxValue);
		var table = (int)wide.Symbols.Single(symbol => symbol.Name == "__c68k_method_table").Address;
		Assert.True(BinaryPrimitives.ReadUInt32BigEndian(wide.Code.AsSpan(table, 4)) >= 8);
		Assert.True(BinaryPrimitives.ReadInt32BigEndian(wide.Code.AsSpan(table + 4, 4)) < short.MinValue);
		Assert.Equal(wide.Image, automatic.Image);
		Assert.DoesNotContain("pass=NarrowApplicationExceptionSites", automatic.Map);
		foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
		foreach (var load in new uint[] { 0x10000, 0x20000 })
			Assert.Equal(42u, Execute(LoadApplicationSizeHunk(automatic.Image, load, 0x40000), model, load + automatic.EntryPoint));
	}
}

public sealed class ApplicationNarrowDataTests
{
	[Theory]
	[InlineData(-32769, false)]
	[InlineData(-32768, true)]
	[InlineData(-1, true)]
	[InlineData(0, true)]
	[InlineData(32767, true)]
	[InlineData(32768, false)]
	public void SignedOffsetsAreCheckedAfterLayout(int displacement, bool fits)
	{
		var assembler = new M68kAssembler();
		assembler.MarkDataStart();
		assembler.Mark(displacement < 0 ? "target" : "basis");
		for (var i = 0; i < Math.Abs(displacement); i++) assembler.EmitByte(0);
		assembler.Mark(displacement < 0 ? "basis" : "target");
		assembler.AlignWord(); assembler.Mark("value");
		assembler.EmitRelativeDataWord("target", "basis");
		if (!fits) Assert.Throws<M68kNarrowDataOverflowException>(() => assembler.Link(0, new Dictionary<string, uint>()));
		else foreach (var origin in new uint[] { 0, 0x10000, 0x20000 })
		{
			var linked = assembler.Link(origin, new Dictionary<string, uint>());
			Assert.Empty(linked.Relocations);
			Assert.Equal(displacement, BinaryPrimitives.ReadInt16BigEndian(linked.Bytes.AsSpan(assembler.Labels["value"], 2)));
		}
	}
}

public static class ApplicationMetadataFixtures
{
	public static int ExceptionPaths()
	{
		var total = CompilerFixtures.TryCatchEntry() + CompilerFixtures.FinallyEntry() +
			CompilerFixtures.CrossMethodCatchEntry() + CompilerFixtures.NestedCatchEntry() +
			CompilerFixtures.RethrowEntry() + CompilerFixtures.ExceptionalFinallyEntry() +
			CompilerFixtures.CalleeSaveExceptionUnwindEntry() + CompilerFixtures.DynamicStackallocExceptionUnwindEntry();
		return total == 7 * 42 + 3 ? 42 : 0;
	}

	private const string P64 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
	private const string P256 = P64 + P64 + P64 + P64;
	private const string P1024 = P256 + P256 + P256 + P256;
	private const string P4096 = P1024 + P1024 + P1024 + P1024;
	private const string P16384 = P4096 + P4096 + P4096 + P4096;
	public static int LargeTable()
	{
		var length = Length(P16384) + Length(P16384) + Length(P16384) + Length(P16384) +
			Length(P16384) + Length(P16384) + Length(P16384) + Length(P16384) + Length(P16384);
		return length == 9 * 16384 ? 42 : 0;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Length(string text) => text.Length;
}
