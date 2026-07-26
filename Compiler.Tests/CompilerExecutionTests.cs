using System.Buffers.Binary;
using System.Reflection;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed class CompilerExecutionTests
{
	private const uint HunkLoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;

	public static TheoryData<M68kCpuTarget, M68kCpuModel> CpuTargets =>
		new()
		{
			{ M68kCpuTarget.M68000, M68kCpuModel.M68000 },
			// The exact MC68020 profile still lacks timing entries for several
			// instruction families emitted by the compiler. MC68040 is
			// architecturally compatible with the MC68020 output.
			{ M68kCpuTarget.M68020, M68kCpuModel.M68040 },
			{ M68kCpuTarget.M68040, M68kCpuModel.M68040 }
		};

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CompilesCallsArithmeticLoopsAndBranchesForEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DefaultEntry");

		var actual = ExecuteHunk(result, model);

		Assert.Equal(106u, actual);
		Assert.Contains(result.Symbols, symbol => symbol.Name.EndsWith("::Arithmetic", StringComparison.Ordinal));
		Assert.NotEmpty(result.Relocations);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void DivisionRemainderAndArgumentsProduceExpectedValue(M68kCpuTarget target)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ArithmeticEntry");

		Assert.Equal(16u, ExecuteHunk(result, M68kCpuModel.M68040));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CompilesShiftsAndComparisonsForEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShiftAndCompare");

		Assert.Equal(24u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ResolvesAbsoluteImports(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint importAddress = 0x0000_2000;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallImport",
			Cpu = target,
			Imports = new Dictionary<string, uint>
			{
				["fixture.value"] = importAddress
			}
		});
		var bus = CreateHunkBus(result);
		bus.WriteWord(importAddress, 0x7042); // MOVEQ #$42,D0
		bus.WriteWord(importAddress + 2, 0x4E75); // RTS

		Assert.Equal(74u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MapsRegisterAbiImports(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint importAddress = 0x0000_2200;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallRegisterImport",
			Cpu = target,
			Imports = new Dictionary<string, uint>
			{
				["fixture.registerAdd"] = importAddress
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(importAddress, state =>
		{
			Assert.Equal(17u, state.D[0]);
			Assert.Equal(25u, state.D[1]);
			state.D[2] = state.D[0] + state.D[1];
		});

		Assert.Equal(42u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CallsExecLibraryVectorsAndReusesA6WithinABasicBlock(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint vectorAddress = execBase - 30;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallExecLibrary",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var calls = 0;
		bus.RegisterGateway(vectorAddress, state =>
		{
			calls++;
			Assert.Equal(execBase, state.A[6]);
			state.D[0] += state.D[1];
		});

		Assert.Equal(62u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(2, calls);
		Assert.Equal(
			2,
			result.Text!.Split(
				"\tmovea.l\t$0004.w,a5",
				StringSplitOptions.None).Length - 1);
		Assert.Equal(
			1,
			result.Text.Split(
				"\tmovea.l\ta5,a6",
				StringSplitOptions.None).Length - 1);
		Assert.Contains("\tjsr\t-30(a6)", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ManualLibraryBaseUsesCStylePublishedHunkSlot()
	{
		const uint libraryBase = 0x0000_3200;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallManualLibrary"
		});
		var baseSlot = result.Symbols.Single(symbol =>
			symbol.Name == AmigaLibraryBaseSymbols.For("dos.library"));
		var bus = CreateHunkBus(result);
		bus.WriteLong(HunkLoadAddress + baseSlot.Address, libraryBase);
		bus.RegisterGateway(libraryBase - 42, state =>
		{
			Assert.Equal(libraryBase, state.A[6]);
			state.D[2] = state.D[0] + state.D[1];
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));

		var assembly = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallManualLibrary",
			OutputFormat = M68kOutputFormat.Assembly
		});
		Assert.Contains(
			"\tmovea.l\t_DOSLibraryBase",
			assembly.Text,
			StringComparison.Ordinal);
		Assert.Contains("\tjsr\t-42(a6)", assembly.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ProvidedLibraryBaseIsLinkedAsAnImmediate()
	{
		const uint libraryBase = 0x0000_3400;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallProvidedLibrary",
			OutputFormat = M68kOutputFormat.Assembly
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				["graphics.library"] = libraryBase
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(libraryBase - 54, state => state.D[0] += state.D[1]);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Contains("\tmovea.l\t#$00003400,a6", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjsr\t-54(a6)", result.Text, StringComparison.Ordinal);

		var rom = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallProvidedLibrary",
			OutputFormat = M68kOutputFormat.KickstartRom
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				["graphics.library"] = libraryBase
			}
		});
		var romBus = new TestBus();
		rom.Image.CopyTo(romBus.Memory.AsSpan(0x00F8_0000));
		romBus.RegisterGateway(libraryBase - 54, state => state.D[0] += state.D[1]);
		Assert.Equal(42u, Execute(romBus, M68kCpuModel.M68000, rom.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void AmigaSdkProvidesExecOpenLibraryReferenceBinding(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3600;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallSdkOpenLibrary",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal(0x0000_1800u, state.A[1]);
			Assert.Equal(37u, state.D[0]);
			state.D[0] = 0x0000_4200;
		});

		Assert.Equal(0x0000_4200u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Contains("\tmove.l\t#$00001800,-(a7)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmoveq\t#37,d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjsr\t-552(a6)", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void AmigaSdkProvidesDosOpenReferenceBinding()
	{
		const uint dosBase = 0x0000_3800;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallSdkDosOpen",
			OutputFormat = M68kOutputFormat.Assembly
		});
		var slot = result.Symbols.Single(symbol =>
			symbol.Name == AmigaLibraryBaseSymbols.For("dos.library"));
		var bus = CreateHunkBus(result);
		bus.WriteLong(HunkLoadAddress + slot.Address, dosBase);
		bus.RegisterGateway(dosBase - 30, state =>
		{
			Assert.Equal(0x0000_1900u, state.D[1]);
			Assert.Equal(1005u, state.D[2]);
			state.D[0] = 0x0000_0042;
		});

		Assert.Equal(
			0x0000_0042u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Contains("\tmove.l\t#$00001900,-(a7)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\t#$000003ED,-(a7)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjsr\t-30(a6)", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void AmigaSdkLibraryBasePolicyCanBeSelectedAtCompileTime()
	{
		const uint dosBase = 0x0000_3A00;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallSdkDosOpen",
			OutputFormat = M68kOutputFormat.Assembly
		}, new AmigaCompilationOptions
		{
			LibraryBasePolicies = new Dictionary<string, AmigaLibraryBasePolicy>
			{
				["dos.library"] = AmigaLibraryBasePolicy.Provided
			},
			LibraryBases = new Dictionary<string, uint>
			{
				["dos.library"] = dosBase
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(dosBase - 30, state =>
		{
			Assert.Equal(0x0000_1900u, state.D[1]);
			Assert.Equal(1005u, state.D[2]);
			state.D[0] = 0x0000_0042;
		});

		Assert.Equal(
			0x0000_0042u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Contains("\tmovea.l\t#$00003A00,a6", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjsr\t-30(a6)", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void RejectsInvalidLibraryMetadataAndManualRomStorage()
	{
		var signature = Assert.Throws<M68kCompilationException>(() =>
			Compile(
				M68kCpuTarget.M68000,
				M68kOutputFormat.Hunk,
				"CopperSharp.Compiler.Tests.CompilerFixtures::CallInvalidLibrarySignature"));
		Assert.Equal(M68kDiagnosticIds.UnsupportedSignature, signature.DiagnosticId);
		Assert.Contains("[M68kRegister]", signature.Message, StringComparison.Ordinal);

		var lvo = Assert.Throws<M68kCompilationException>(() =>
			Compile(
				M68kCpuTarget.M68000,
				M68kOutputFormat.Hunk,
				"CopperSharp.Compiler.Tests.CompilerFixtures::CallInvalidLibraryLvo"));
		Assert.Equal(M68kDiagnosticIds.InvalidMetadata, lvo.DiagnosticId);
		Assert.Contains("signed 16-bit", lvo.Message, StringComparison.Ordinal);

		var rom = Assert.Throws<M68kCompilationException>(() =>
			Compile(
				M68kCpuTarget.M68000,
				M68kOutputFormat.KickstartRom,
				"CopperSharp.Compiler.Tests.CompilerFixtures::CallManualLibrary"));
		Assert.Equal(M68kDiagnosticIds.InvalidOutputOptions, rom.DiagnosticId);
		Assert.Contains("read-only ROM", rom.Message, StringComparison.Ordinal);

		var provided = Assert.Throws<M68kCompilationException>(() =>
			Compile(
				M68kCpuTarget.M68000,
				M68kOutputFormat.Hunk,
				"CopperSharp.Compiler.Tests.CompilerFixtures::CallProvidedLibrary"));
		Assert.Equal(M68kDiagnosticIds.UnresolvedImport, provided.DiagnosticId);
		Assert.Contains("graphics.library", provided.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AutoOpenLibraryCallsAreRejectedFromStaticInitialization()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			AmigaM68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = "CopperSharp.Compiler.Tests.StaticInitializationFixtures::Entry"
			}, new AmigaCompilationOptions
			{
				DefaultLibraryBasePolicy = AmigaLibraryBasePolicy.AutoOpen
			}));

		Assert.Equal(M68kDiagnosticIds.StaticAnalysis, exception.DiagnosticId);
		Assert.Contains("dos.library", exception.Message, StringComparison.Ordinal);
		Assert.Contains("static initialization", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void RomRuntimeProfileRejectsManagedAllocationByDefault()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry",
				OutputFormat = M68kOutputFormat.KickstartRom,
				RuntimeProfile = M68kRuntimeProfile.Rom
			}));

		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, exception.DiagnosticId);
		Assert.Contains("managed heap", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ManagedRuntimeCallsGcInitAndShutdownAroundEntry()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(26u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Contains(result.Symbols, symbol => symbol.Name.EndsWith("ManagedArrayEntry", StringComparison.Ordinal));
	}

	[Fact]
	public void TelemetryTriggeredGcStrategyEmitsRuntimeConfig()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry",
			OutputFormat = M68kOutputFormat.Assembly,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.TelemetryTriggered,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			},
			GcTelemetry = new M68kGcTelemetryOptions
			{
				StaleBytesThreshold = 4096,
				StaleBlocksThreshold = 32,
				IntervalTicks = 10
			}
		});

		Assert.Contains("\tjsr\t__c68k_gc_init", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjsr\t__c68k_gc_shutdown", result.Text, StringComparison.Ordinal);
		Assert.Contains("C68K_runtime_003Agc_002Dconfig:", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tdc.w\t$0003", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tdc.w\t$4000", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tdc.w\t$2000", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tdc.w\t$1000", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tdc.w\t$0020", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tdc.w\t$000A", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ManagedPoolEveryAllocationStrategyCollectsAtAllocationSite()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry",
			OutputFormat = M68kOutputFormat.Assembly,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Contains("__c68k_alloc:", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjsr\t__c68k_gc_mark", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjsr\t__c68k_gc_collect", result.Text, StringComparison.Ordinal);
		Assert.Contains("C68K_generated_003Agc_alloc_loop", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ManagedPoolAllocationFailureStrategyRetriesAfterCollectingAtAllocationSite()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry",
			OutputFormat = M68kOutputFormat.Assembly,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.OnAllocationFailure,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		var text = result.Text!;
		var firstAllocIndex = text.IndexOf("\tjsr\t__c68k_alloc", StringComparison.Ordinal);
		var collectIndex = text.IndexOf("\tjsr\t__c68k_gc_collect", StringComparison.Ordinal);
		var retryIndex = text.IndexOf(
			"\tjsr\t__c68k_alloc",
			firstAllocIndex + 1,
			StringComparison.Ordinal);
		Assert.True(firstAllocIndex < collectIndex);
		Assert.True(collectIndex < retryIndex);
	}

	[Fact]
	public void GcSweepStrategyRequiresGcMemoryManagement()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry",
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation
			}));

		Assert.Equal(M68kDiagnosticIds.InvalidOutputOptions, exception.DiagnosticId);
		Assert.Contains("GC sweep strategy", exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void KickstartRomContainsVectorsChecksumAndExecutableCode(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.KickstartRom,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShiftAndCompare");

		Assert.Equal(512 * 1024, result.Image.Length);
		Assert.Equal(StackPointer, BinaryPrimitives.ReadUInt32BigEndian(result.Image));
		Assert.Equal(result.EntryPoint, BinaryPrimitives.ReadUInt32BigEndian(result.Image.AsSpan(4)));
		Assert.Equal(uint.MaxValue, EndAroundCarrySum(result.Image));

		var bus = new TestBus();
		const int romBase = 0x00F8_0000;
		result.Image.CopyTo(bus.Memory.AsSpan(romBase));
		Assert.Equal(24u, Execute(bus, model, result.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CompilesStaticFieldsObjectsInstanceFieldsAndCalls(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint allocatorAddress = 0x0000_2800;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedObjectEntry",
			Cpu = target,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			}
		});
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		bus.RegisterGateway(allocatorAddress, state =>
		{
			var size = bus.ReadLong(state.A[7] + 4);
			var address = heap;
			heap += (size + 3) & ~3u;
			Array.Clear(bus.Memory, (int)address, (int)size);
			state.D[0] = address;
		});

		var addSymbol = result.Symbols.Single(symbol =>
			symbol.Name.EndsWith("ManagedBox::Add", StringComparison.Ordinal));
		var observedCall = false;
		var actual = Execute(
			bus,
			model,
			HunkLoadAddress + result.EntryPoint,
			(cpu, memory) =>
			{
				if (cpu.State.ProgramCounter != HunkLoadAddress + addSymbol.Address)
				{
					return;
				}

				observedCall = true;
				Assert.Equal(4u, cpu.State.D[0]);
				Assert.Equal(0x0000_4000u, cpu.State.A[0]);
			});

		Assert.True(observedCall);
		Assert.Equal(14u, actual);
	}

	[Fact]
	public void ManagedFieldRoundTripUsesObjectLayout()
	{
		const uint allocatorAddress = 0x0000_2800;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedFieldEntry",
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			}
		});
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		bus.RegisterGateway(allocatorAddress, state =>
		{
			var size = bus.ReadLong(state.A[7] + 4);
			state.D[0] = heap;
			heap += (size + 3) & ~3u;
		});

		Assert.Equal(10u, Execute(bus, M68kCpuModel.M68040, HunkLoadAddress + result.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PassesAndReturnsManagedReferencesThroughA0(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint allocatorAddress = 0x0000_2800;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ReferenceReturnEntry",
			Cpu = target,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(allocatorAddress, state =>
		{
			Array.Clear(bus.Memory, 0x4000, (int)bus.ReadLong(state.A[7] + 4));
			state.D[0] = 0x4000;
		});

		Assert.Equal(37u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void EmbedsManagedStringLiterals(M68kCpuTarget target, M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::StringLiteralEntry");

		Assert.Equal(9u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void EmbedsCStringLiteralsAsNullTerminatedBytes(M68kCpuTarget target, M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CStringLiteralEntry");
		var bus = CreateHunkBus(result);

		var address = checked((int)Execute(bus, model, HunkLoadAddress + result.EntryPoint));

		Assert.Equal((byte)'a', bus.Memory[address]);
		Assert.Equal((byte)'b', bus.Memory[address + 1]);
		Assert.Equal((byte)'c', bus.Memory[address + 2]);
		Assert.Equal(0, bus.Memory[address + 3]);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CompilesManagedArraysWithBoundsCheckedElementAccess(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint allocatorAddress = 0x0000_2800;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry",
			Cpu = target,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			}
		});
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		bus.RegisterGateway(allocatorAddress, state =>
		{
			var size = bus.ReadLong(state.A[7] + 4);
			var address = heap;
			heap += (size + 3) & ~3u;
			Array.Clear(bus.Memory, (int)address, (int)size);
			state.D[0] = address;
		});

		Assert.Equal(26u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CompilesByteArraysWithUnsignedElementAccess(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ByteArrayEntry");

		Assert.Equal(271u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CompilesShortArraysWithSignedElementAccess(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShortArrayEntry");

		Assert.Equal(287u, ExecuteHunkWithAllocator(result, model));
	}

	[Fact]
	public void CompilesIndirectByteAndWordLoadStore()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::IndirectMemoryEntry");

		Assert.Equal(unchecked((uint)-993), ExecuteHunkWithAllocator(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void EmitsIndirectByteAndWordLoadStoreForM68020()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::IndirectMemoryEntry",
			Cpu = M68kCpuTarget.M68020,
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});

		Assert.Contains("dc.w\t$1140", result.Text, StringComparison.Ordinal);
		Assert.Contains("dc.w\t$3140", result.Text, StringComparison.Ordinal);
		Assert.Contains("dc.w\t$1028", result.Text, StringComparison.Ordinal);
		Assert.Contains("dc.w\t$3028", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ExplicitRuntimeDisposeReceivesReferenceSlotAndCanClearIt()
	{
		const uint allocatorAddress = 0x0000_2800;
		const uint disposeAddress = 0x0000_2A00;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ExplicitDisposeEntry",
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress,
				[M68kRuntimeImports.Dispose] = disposeAddress
			}
		});
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var disposed = 0u;
		bus.RegisterGateway(allocatorAddress, state =>
		{
			var size = bus.ReadLong(state.A[7] + 4);
			var address = heap;
			heap += (size + 3) & ~3u;
			Array.Clear(bus.Memory, (int)address, (int)size);
			state.D[0] = address;
		});
		bus.RegisterGateway(disposeAddress, state =>
		{
			var slot = bus.ReadLong(state.A[7] + 4);
			disposed = bus.ReadLong(slot);
			Assert.NotEqual(0u, disposed);
			bus.WriteLong(slot, 0);
		});

		Assert.Equal(42u, Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.NotEqual(0u, disposed);
	}

	[Fact]
	public void ManagedPoolDisposeReturnsBlockToBuiltInFreeList()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolDisposeReuseEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 48
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ManagedPoolCollectCoalescesAdjacentFreeBlocks()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolCollectCoalescesEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 88
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ManagedPoolCollectReclaimsUnrootedAllocatedBlocks()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolCollectReclaimsUnrootedEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 120
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ManagedPoolAllocationFailureCollectsRootsAndRetries()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolAllocationFailureCollectsRootsEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.OnAllocationFailure,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 120
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ManagedPoolAllocationFailureDoesNotMarkIntegerStackValues()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolAllocationFailureIgnoresIntegerStackRootsEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.OnAllocationFailure,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 88
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ManagedPoolCollectTracesObjectReferenceFields()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolCollectTracesObjectFieldsEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 104
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ManagedPoolCollectTracesReferenceArrayElements()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolCollectTracesReferenceArrayEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 104
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ExplicitRuntimeCollectCallsGcCollectHook()
	{
		const uint collectAddress = 0x0000_2C00;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ExplicitCollectEntry",
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.GcCollect] = collectAddress
			}
		});
		var bus = CreateHunkBus(result);
		var calls = 0;
		bus.RegisterGateway(collectAddress, _ => calls++);

		Assert.Equal(42u, Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, calls);
	}

	[Fact]
	public void HunkOutputContainsHeaderCodeRelocationsSymbolsAndEnd()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DefaultEntry");
		var words = EnumerateLongWords(result.Image).ToArray();

		Assert.Equal(0x0000_03F3u, words[0]);
		Assert.Contains(0x0000_03E9u, words);
		Assert.Contains(0x0000_03ECu, words);
		Assert.Contains(0x0000_03F0u, words);
		Assert.Equal(0x0000_03F2u, words[^1]);
	}

	[Fact]
	public void AssemblyOutputPreservesLabelsWordsAndSymbolicImports()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallImport",
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.NotNull(result.Text);
		Assert.Equal(result.Text, System.Text.Encoding.UTF8.GetString(result.Image));
		Assert.Contains("\tmc68000", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tsection\tcode,code", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjsr\tC68K_fixture_002Evalue", result.Text, StringComparison.Ordinal);
		Assert.Contains("\txref\tC68K_fixture_002Evalue", result.Text, StringComparison.Ordinal);
		Assert.Contains("\taddq.l\t#4,a7", result.Text, StringComparison.Ordinal);
		Assert.Contains("(a7)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmovea.l\ta6,a7", result.Text, StringComparison.Ordinal);
		Assert.Contains("C68K_method_003A", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void OptimizesSmallConstantsToMoveqAddqAndSubq(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::QuickArithmeticEntry");

		Assert.Equal(41u, ExecuteHunk(result, model));
		Assert.Contains("\tmoveq\t#40,d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("\taddq.l\t#3,(a7)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tsubq.l\t#2,(a7)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tsubq.l\t#4,a7", result.Text, StringComparison.Ordinal);
		Assert.Contains("\taddq.l\t#4,a7", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void OptimizesInternalCallReturnPairsToTailJumps(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TailCallEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
		Assert.Contains("\tjmp\tC68K_method_003A", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ExportAdaptersMapRegistersAndPreserveAmigaCalleeSavedRegisters(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShiftAndCompare");
		var export = result.Symbols.Single(symbol => symbol.Name == "fixture.add");
		var bus = CreateHunkBus(result);

		var actual = Execute(
			bus,
			model,
			HunkLoadAddress + export.Address,
			initialize: state =>
			{
				state.D[0] = 17;
				state.D[1] = 25;
				state.D[2] = 0x2233_4455;
				state.A[2] = 0x0066_7788;
			},
			afterReturn: state =>
			{
				Assert.Equal(0x2233_4455u, state.D[2]);
				Assert.Equal(0x0066_7788u, state.A[2]);
			});

		Assert.Equal(42u, actual);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SharesGenericMethodBodyAcrossScalarAndReferenceInstantiations(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SharedGenericEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
		Assert.Single(result.Symbols.Where(symbol =>
			symbol.Name.EndsWith("::SharedIdentity", StringComparison.Ordinal)));
	}

	[Fact]
	public void FloatingPointSignatureFailsWithoutEmittingFpuCode()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			Compile(
				M68kCpuTarget.M68040,
				M68kOutputFormat.Hunk,
				"CopperSharp.Compiler.Tests.CompilerFixtures::UnsupportedFloat"));

		Assert.Equal(M68kDiagnosticIds.UnsupportedSignature, exception.DiagnosticId);
		Assert.Contains("Floating-point", exception.Message, StringComparison.Ordinal);
	}

	private static string FixtureAssembly => Assembly.GetExecutingAssembly().Location;

	private static M68kCompilationResult Compile(
		M68kCpuTarget cpu,
		M68kOutputFormat format,
		string entry) =>
		AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = entry,
			Cpu = cpu,
			OutputFormat = format,
			Rom = new KickstartRomOutputOptions
			{
				Size = 512 * 1024,
				InitialStackPointer = StackPointer
			}
		});

	private static M68kCompilationResult CompileWithAllocator(
		M68kCpuTarget cpu,
		string entry) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = entry,
			Cpu = cpu,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});

	private static uint ExecuteHunk(M68kCompilationResult result, M68kCpuModel model)
	{
		var bus = CreateHunkBus(result);
		return Execute(bus, model, HunkLoadAddress + result.EntryPoint);
	}

	private static uint ExecuteHunkWithAllocator(M68kCompilationResult result, M68kCpuModel model)
	{
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			var size = bus.ReadLong(state.A[7] + 4);
			var address = heap;
			heap += (size + 3) & ~3u;
			Array.Clear(bus.Memory, (int)address, (int)size);
			state.D[0] = address;
		});
		return Execute(bus, model, HunkLoadAddress + result.EntryPoint);
	}


	private static TestBus CreateHunkBus(M68kCompilationResult result)
	{
		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)HunkLoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = HunkLoadAddress + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + HunkLoadAddress);
		}

		return bus;
	}

	private static uint Execute(
		TestBus bus,
		M68kCpuModel model,
		uint entryPoint,
		Action<IM68kCore, TestBus>? beforeInstruction = null,
		Action<M68kCpuState>? initialize = null,
		Action<M68kCpuState>? afterReturn = null)
	{
		bus.WriteLong(StackPointer, ReturnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(entryPoint, StackPointer);
		initialize?.Invoke(cpu.State);
		for (var instruction = 0; instruction < 200_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				afterReturn?.Invoke(cpu.State);
				return cpu.State.D[0];
			}

			beforeInstruction?.Invoke(cpu, bus);
			cpu.ExecuteInstruction();
			if (cpu.State.Halted)
			{
				throw new Xunit.Sdk.XunitException(
					$"{model} halted at ${cpu.State.ProgramCounter:X8}, last opcode ${cpu.State.LastOpcode:X4}.");
			}
		}

		throw new Xunit.Sdk.XunitException(
			$"{model} did not return after 200000 instructions; PC=${cpu.State.ProgramCounter:X8}.");
	}

	private static IEnumerable<uint> EnumerateLongWords(byte[] data)
	{
		for (var offset = 0; offset + 4 <= data.Length; offset += 4)
		{
			yield return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
		}
	}

	private static uint EndAroundCarrySum(ReadOnlySpan<byte> data)
	{
		var sum = 0u;
		for (var offset = 0; offset < data.Length; offset += 4)
		{
			var value = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
			var previous = sum;
			sum += value;
			if (sum < previous)
			{
				sum++;
			}
		}

		return sum;
	}
}
