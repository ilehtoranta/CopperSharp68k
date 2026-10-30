using System.Buffers.Binary;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
	public void ApplicationMethodPoliciesHaveIndependentControls(M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		var oldPolicy = new M68kCodeSizeOptions { RemoveApplicationRegisterMoves = false, ShareReturnSequences = false };
		var request = new M68kCompilationRequest {
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "ConsoleIO.dll"), EntryPoint = "ConsoleIOExample.Program::Main",
			Cpu = target, OutputFormat = M68kOutputFormat.Hunk, RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = M68kExceptionMode.Full, PeepholeOptimization = mode,
			CodeSizeOptimizations = oldPolicy, Hunk = new() { IncludeSymbols = false }
		};
		var baseline = AmigaM68kCompiler.Compile(request);
		foreach (var policy in new[] { oldPolicy with { RemoveApplicationRegisterMoves = true },
			oldPolicy with { ShareReturnSequences = true }, new M68kCodeSizeOptions() })
		{
			var result = AmigaM68kCompiler.Compile(request with { CodeSizeOptimizations = policy });
			if (target != M68kCpuTarget.M68000 || mode == M68kPeepholeOptimizationMode.Disabled)
				Assert.Equal(baseline.Image, result.Image);
			else
			{
				Assert.True(result.Image.Length < baseline.Image.Length);
				Assert.Equal(policy.RemoveApplicationRegisterMoves,
					result.Map.Contains("GENERATED-SIZE-REWRITE pass=RemoveApplicationRegisterMoves", StringComparison.Ordinal));
				Assert.Equal(policy.ShareReturnSequences,
					!result.Map.Contains("terminal-net-bytes-saved=0 ", StringComparison.Ordinal));
				Assert.DoesNotContain("__m68k_shared_epilogue_", result.Map);
			}
		}
	}

	[Fact]
	public void ApplicationSizeRetainsExternalCollectorTableAbi()
	{
		var request = new M68kCompilationRequest {
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ExplicitCollectWithDynamicFrameEntry",
			Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application, ExceptionMode = M68kExceptionMode.Full,
			Imports = new Dictionary<string, uint> { [M68kRuntimeImports.GcCollect] = 0x2C00 },
			IncludedExportNames = [], Hunk = new() { IncludeSymbols = false }
		};
		var baseline = M68kCompiler.Compile(request);
		var compact = M68kCompiler.Compile(request with { CodeSizeOptimizations = new() {
			CompactApplicationAddresses = false, ShareApplicationRuntimeMetadata = false,
			RemoveApplicationRegisterMoves = false, ShareReturnSequences = false,
			PruneApplicationUnwindMetadata = false } });
		Assert.Equal(baseline.Image, compact.Image);
	}

	[Theory]
	[InlineData(M68kCpuModel.M68000)]
	[InlineData(M68kCpuModel.M68020)]
	[InlineData(M68kCpuModel.M68040)]
	public void ApplicationSizePreservesConsoleIoOutputAndShutdown(M68kCpuModel model)
	{
		foreach (var eof in new[] { false, true })
		{
			byte[] input = eof ? [] : [(byte)'A', 0xe4, (byte)'B', (byte)'\n'];
			var baseline = ExecuteConsoleIoSample(M68kCpuTarget.M68000, model, input, eof ? 5u : 0u, eof ? 2 : 8);
			var optimized = ExecuteConsoleIoSample(M68kCpuTarget.M68000, model, input, eof ? 5u : 0u, eof ? 2 : 8,
				sizeOptions: new());
			Assert.Equal(baseline, optimized);
		}
	}

	[Theory]
	[InlineData("TryCatchEntry", false)]
	[InlineData("FinallyEntry", false)]
	[InlineData("CrossMethodCatchEntry", false)]
	[InlineData("NestedCatchEntry", false)]
	[InlineData("RethrowEntry", false)]
	[InlineData("ExceptionalFinallyEntry", false)]
	[InlineData("CalleeSaveExceptionUnwindEntry", false)]
	[InlineData("DynamicStackallocExceptionUnwindEntry", false)]
	[InlineData("CustomExceptionCatchEntry", true)]
	[InlineData("PoolCollectTracesCallerFrameEntry", true)]
	[InlineData("ArrayInteriorByrefAcrossCollectionEntry", true)]
	[InlineData("ExceptionEdgeByrefAcrossCollectionEntry", true)]
	[InlineData("ManagedByrefFrameHomesPreservePayloadEntry", true)]
	[InlineData("NormalFinallyCollectionPreservesPayloadEntry", true)]
	[InlineData("CollectorFrameAnchorRetainsCallerArrayEntry", true)]
	[InlineData("FinalizableCollectorFrameAnchorRetainsCallerArrayEntry", true)]
	public void ApplicationSizePreservesExceptionsAndGcAtRelocatedHunkBases(string entry, bool managed)
	{
		var request = new M68kCompilationRequest {
			AssemblyPath = FixtureAssembly, EntryPoint = typeof(CompilerFixtures).FullName + "::" + entry,
			Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application, ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = managed ? M68kMemoryManagement.ManagedPoolMarkSweepGc : M68kMemoryManagement.ExternalAllocator,
			GcSweepStrategy = managed ? M68kGcSweepStrategy.EveryAllocation : null,
			Heap = new() { StartAddress = 0x60000, Size = 0x8000 },
			Imports = new Dictionary<string, uint> { [M68kRuntimeImports.Allocate] = 0x2800 },
			IncludedExportNames = [], Hunk = new() { IncludeSymbols = false }
		};
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		foreach (var policy in new M68kCodeSizeOptions?[] {
			null,
			new() { CompactApplicationAddresses = false, CompactApplicationExceptionSites = false, RemoveApplicationRegisterMoves = false, ShareReturnSequences = false },
			new() { ShareApplicationRuntimeMetadata = false, CompactApplicationExceptionSites = false, RemoveApplicationRegisterMoves = false, ShareReturnSequences = false },
			new() { CompactApplicationAddresses = false, ShareApplicationRuntimeMetadata = false, RemoveApplicationRegisterMoves = false, ShareReturnSequences = false },
			new() { RemoveApplicationRegisterMoves = false, ShareReturnSequences = false },
			new() { ShareReturnSequences = false },
			new() { RemoveApplicationRegisterMoves = false },
			new() { PruneApplicationUnwindMetadata = false, NarrowApplicationExceptionSites = false },
			new() { PruneApplicationUnwindMetadata = false },
			new() { NarrowApplicationExceptionSites = false },
			new()
		})
		{
			var result = M68kCompiler.Compile(request with { CodeSizeOptimizations = policy, PeepholeOptimization = mode });
			Assert.Equal(M68kExceptionMode.Full, result.NativeCompatibility.ExceptionMode);
			Assert.Equal(request.MemoryManagement, result.NativeCompatibility.MemoryManagement);
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (var load in new uint[] { 0x10000, 0x20000 })
			{
				// Load the serialized file with BSS deliberately separated from code.
				var bus = LoadApplicationSizeHunk(result.Image, load, 0x40000);
				var allocation = 0x50000u;
				bus.RegisterGateway(0x2800, state => {
					var bytes = state.D[0]; state.D[0] = allocation;
					Array.Clear(bus.Memory, (int)allocation, (int)bytes);
					allocation += (bytes + 3) & ~3u;
					Assert.True(allocation < 0x60000);
				});
				bus.WriteLong(0x68000, 0x5A17C0DE);
				Assert.Equal(entry == "FinallyEntry" ? 3u : 42u, Execute(bus, model, load + result.EntryPoint, maxInstructions: 5_000_000,
					afterReturn: state => Assert.Equal(StackPointer + 4, state.A[7])));
				Assert.Equal(0x5A17C0DEu, bus.ReadLong(0x68000));
			}
		}
	}

	internal static TestBus LoadApplicationSizeHunk(byte[] image, uint codeBase, uint bssBase)
	{
		var position = 0;
		uint Read() { var word = BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(position, 4)); position += 4; return word; }
		Assert.Equal(0x3f3u, Read()); Assert.Equal(0u, Read());
		var count = Read(); Assert.InRange(count, 1u, 2u);
		Assert.Equal(0u, Read()); Assert.Equal(count - 1, Read());
		var sizes = new uint[count];
		for (var i = 0; i < count; i++) sizes[i] = Read() * 4;
		var bases = new[] { codeBase, bssBase };
		var bus = new TestBus();
		for (var hunk = 0; hunk < count; hunk++)
		{
			var kind = Read(); Assert.True(kind is 0x3e9 or 0x3eb);
			var size = Read() * 4; Assert.Equal(sizes[hunk], size);
			if (kind == 0x3e9) { image.AsSpan(position, (int)size).CopyTo(bus.Memory.AsSpan((int)bases[hunk])); position += (int)size; }
			var record = Read();
			if (record == 0x3ec)
			{
				for (var relocations = Read(); relocations != 0; relocations = Read())
				{
					var target = Read(); Assert.True(target < count);
					for (var i = 0; i < relocations; i++) {
						var offset = Read(); Assert.True(offset + 4 <= size);
						var address = bases[hunk] + offset;
						bus.WriteLong(address, bus.ReadLong(address) + bases[target]);
					}
				}
				record = Read();
			}
			Assert.Equal(0x3f2u, record);
		}
		Assert.Equal(image.Length, position);
		return bus;
	}
}

public sealed class ApplicationAddressRelaxationTests
{
	[Fact]
	public void RelativeDataTracksFinalLayoutWithoutRelocationOrSixteenBitLimit()
	{
		var assembler = new M68kAssembler { CompactApplicationAddresses = true };
		assembler.Mark("backward");
		assembler.EmitWord(0x207C); assembler.EmitAddress("table");
		assembler.EmitWord(0x4E75);
		assembler.MarkDataStart(); assembler.Mark("table");
		assembler.EmitRelativeDataLong("backward", "table");
		assembler.EmitRelativeDataLong("forward", "table");
		for (var index = 0; index < 20_000; index++) assembler.EmitWord(0);
		assembler.Mark("forward"); assembler.EmitLong(42);
		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Empty(linked.Relocations);
		Assert.Equal(-6, BinaryPrimitives.ReadInt32BigEndian(linked.Bytes.AsSpan(6, 4)));
		Assert.Equal(40_008, BinaryPrimitives.ReadInt32BigEndian(linked.Bytes.AsSpan(10, 4)));
		Assert.True(assembler.ReferencesTarget("forward"));
	}

	[Fact]
	public void RelativeDataRejectsSeparateBssTargets()
	{
		var assembler = new M68kAssembler();
		assembler.MarkDataStart(); assembler.Mark("table");
		assembler.EmitRelativeDataLong("bss", "table");
		assembler.MarkBssStart(); assembler.Mark("bss"); assembler.EmitLong(0);
		Assert.Throws<InvalidOperationException>(() => assembler.Link(0, new Dictionary<string, uint>()));
	}

	[Fact]
	public void AddressLoadsPreserveEveryCcrAndRegisterAtMultipleLoadAddresses()
	{
		for (var register = 0; register < 7; register++)
		foreach (var enabled in new[] { false, true })
		{
			var assembler = new M68kAssembler { CompactApplicationAddresses = enabled };
			assembler.EmitWord((ushort)(0x207C | register << 9)); assembler.EmitAddress("value");
			assembler.EmitWord(0x4E75); assembler.MarkDataStart(); assembler.Mark("value"); assembler.EmitLong(42);
			var linked = assembler.Link(0, new Dictionary<string, uint>());
			Assert.Equal(enabled ? 0 : 1, linked.Relocations.Count);
			Assert.Equal(enabled ? 10 : 12, linked.Bytes.Length);
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (uint load in new uint[] { 0x10000, 0x20000 })
			for (var flags = 0; flags < 32; flags++)
			{
				var bus = new TestBus(); linked.Bytes.CopyTo(bus.Memory.AsSpan((int)load));
				foreach (var relocation in linked.Relocations) bus.WriteLong(load + (uint)relocation.Offset, bus.ReadLong(load + (uint)relocation.Offset) + load);
				using var cpu = M68kCoreFactory.Default.Create(model, bus);
				cpu.Reset(load, 0x80000); cpu.State.StatusRegister = (ushort)(0x2700 | flags);
				for (var i = 0; i < 8; i++) { cpu.State.D[i] = 0xD0000000u + (uint)i; cpu.State.A[i] = 0xA0000000u + (uint)i; }
				cpu.ExecuteInstruction();
				Assert.False(cpu.State.Halted); Assert.Equal((ushort)(0x2700 | flags), cpu.State.StatusRegister);
				for (var i = 0; i < 8; i++) {
					Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
					Assert.Equal(i == register ? load + (uint)linked.Labels["value"] : 0xA0000000u + (uint)i, cpu.State.A[i]);
				}
				Assert.Equal(42u, bus.ReadLong(cpu.State.A[register]));
			}
		}
	}

	[Theory]
	[InlineData("far")]
	[InlineData("bss")]
	[InlineData("external")]
	[InlineData("addend")]
	public void UnsafeAddressRelaxationCandidatesRetainAbsoluteRelocations(string scenario)
	{
		var assembler = new M68kAssembler { CompactApplicationAddresses = true };
		assembler.EmitWord(0x207C); assembler.EmitAddress("value", external: scenario == "external", addend: scenario == "addend" ? 4 : 0);
		assembler.EmitWord(0x4E75); assembler.MarkDataStart();
		if (scenario == "far") for (var i = 0; i < 16384; i++) assembler.EmitWord(0);
		if (scenario == "bss") { assembler.MarkWritableDataStart(); assembler.MarkBssStart(); }
		assembler.Mark("value"); assembler.EmitLong(0); assembler.EmitLong(42);
		var linked = assembler.Link(0, new Dictionary<string, uint> { ["value"] = 0x40000 });
		Assert.Equal(0x207C, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Equal(scenario == "external" ? 0 : 1, linked.Relocations.Count);
		Assert.Equal(scenario == "external" ? 0x40000u : (uint)linked.Labels["value"] + (scenario == "addend" ? 4u : 0),
			BinaryPrimitives.ReadUInt32BigEndian(linked.Bytes.AsSpan(2)));
	}
}
