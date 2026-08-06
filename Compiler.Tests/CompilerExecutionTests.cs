using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
	using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using CopperSharp.Compiler.Backend;
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
			{ M68kCpuTarget.M68040, M68kCpuModel.M68040 },
			// The emulator has no MC68060 model yet. The compiler intentionally
			// limits MC68060 output to the integer subset available on MC68040.
			{ M68kCpuTarget.M68060, M68kCpuModel.M68040 }
		};

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		MultiwordReturnCases
	{
		get
		{
			var result = new TheoryData<string, M68kCpuTarget, M68kCpuModel>();
			foreach (var entry in new[]
			{
				"MultiwordReturnEntry",
				"ConstructedMultiwordReturnEntry",
				"ThreeWordReturnEntry",
				"ConstructedThreeWordReturnEntry",
				"MixedMultiwordReturnEntry",
				"MultiwordLocalCopyEntry",
				"MultiwordReturnExceptionEntry",
				"BoxedMultiwordReturnEntry",
				"NestedMultiwordReturnEntry",
				"MultiwordPhiReturnEntry"
			})
			{
				foreach (var (target, model) in new[]
				{
					(M68kCpuTarget.M68000, M68kCpuModel.M68000),
					(M68kCpuTarget.M68020, M68kCpuModel.M68040),
					(M68kCpuTarget.M68040, M68kCpuModel.M68040),
					(M68kCpuTarget.M68060, M68kCpuModel.M68040)
				})
				{
					result.Add(entry, target, model);
				}
			}
			return result;
		}
	}

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		MultiwordArgumentCases => CreateMultiwordCpuCases(
			"BoxedMultiwordArgumentEntry",
			"ForwardedMultiwordArgumentEntry",
			"MultiwordArgumentStoreEntry",
			"ThreeWordArgumentEntry",
			"MixedScalarMultiwordArgumentEntry",
			"MixedReferenceMultiwordArgumentEntry",
			"TwoMultiwordArgumentsEntry",
			"MultiwordArgumentExceptionEntry",
			"MultiwordExpressionArgumentEntry",
			"BoxedMultiwordExpressionEntry");

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		MultiwordUnboxAnyCases => CreateMultiwordCpuCases(
			"BoxedMultiwordUnboxAnyEntry",
			"BoxedThreeWordUnboxAnyEntry",
			"BoxedMultiwordUnboxAnyIdentityEntry",
			"BoxedMultiwordUnboxAnyNullEntry");

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		MultiwordFieldCases => CreateMultiwordCpuCases(
			"MultiwordInstanceFieldEntry",
			"MultiwordInstanceFieldExpressionEntry",
			"MultiwordStaticFieldEntry",
			"MultiwordStaticFieldExpressionEntry");

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		MultiwordArrayCases => CreateMultiwordCpuCases(
			"MultiwordArrayEntry",
			"MultiwordArrayExpressionEntry",
			"ThreeWordArrayEntry",
			"MultiwordArrayZeroInitializationEntry",
			"MultiwordArrayCollectionEntry",
			"MultiwordArrayLoadBoundsEntry",
			"MultiwordArrayStoreBoundsEntry",
			"MultiwordArrayNegativeLengthEntry",
			"MultiwordArraySizeOverflowEntry");

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		MultiwordIndirectCases => CreateMultiwordCpuCases(
			"MultiwordIndirectLoadEntry",
			"ThreeWordIndirectLoadEntry",
			"MultiwordIndirectStoreEntry",
			"MultiwordIndirectInitializeEntry",
			"MultiwordIndirectCopyEntry");

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		ManagedByrefSafepointCases => CreateMultiwordCpuCases(
			"FrameByrefAcrossCollectionEntry",
			"StaticByrefAcrossCollectionEntry",
			"ArrayInteriorByrefAcrossCollectionEntry",
			"ObjectInteriorByrefAcrossCollectionEntry",
			"BorrowedFrameByrefAcrossCollectionEntry",
			"BorrowedArrayByrefAcrossCollectionEntry",
			"BorrowedObjectByrefAcrossCollectionEntry",
			"BorrowedByrefReturnAcrossCollectionEntry",
			"CompatibleOwnerByrefPhiEntry",
			"ExceptionEdgeByrefAcrossCollectionEntry");

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		SpanByrefConstructorCases => CreateMultiwordCpuCases(
			"SpanFromFrameRefAcrossCollectionEntry",
			"SpanFromStaticRefAcrossCollectionEntry",
			"SpanFromArrayRefAcrossCollectionEntry",
			"SpanFromObjectRefAcrossCollectionEntry",
			"ReadOnlySpanFromArrayRefAcrossCollectionEntry");

	private static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		CreateMultiwordCpuCases(params string[] entries)
	{
		var result = new TheoryData<string, M68kCpuTarget, M68kCpuModel>();
		foreach (var entry in entries)
		{
			foreach (var (target, model) in new[]
			{
				(M68kCpuTarget.M68000, M68kCpuModel.M68000),
				(M68kCpuTarget.M68020, M68kCpuModel.M68040),
				(M68kCpuTarget.M68040, M68kCpuModel.M68040),
				(M68kCpuTarget.M68060, M68kCpuModel.M68040)
			})
			{
				result.Add(entry, target, model);
			}
		}
		return result;
	}

	public static TheoryData<string, uint, M68kCpuTarget, M68kCpuModel> NarrowOperationCases
	{
		get
		{
			var result = new TheoryData<string, uint, M68kCpuTarget, M68kCpuModel>();
			foreach (var (entryPoint, expected) in new[]
			{
				("NarrowUnsignedSubtractionEntry", 64000u),
				("NarrowByteMultiplyEntry", 255u),
				("NarrowShortShiftEntry", 0xFFFF_FFCEu),
				("NarrowSignedNegateEntry", 120u)
			})
			{
				foreach (var (target, model) in new[]
				{
					(M68kCpuTarget.M68000, M68kCpuModel.M68000),
					(M68kCpuTarget.M68020, M68kCpuModel.M68040),
					(M68kCpuTarget.M68040, M68kCpuModel.M68040)
				})
				{
					result.Add(entryPoint, expected, target, model);
				}
			}

			return result;
		}
	}

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
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoundaryQuickConstantUsesCpuTimedMaterialization(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const string entry =
			"CopperSharp.Compiler.Tests.CompilerFixtures::BoundaryQuickConstantEntry";
		var executable = Compile(target, M68kOutputFormat.Hunk, entry);
		Assert.Equal(135u, ExecuteHunk(executable, model));

		var assembly = Compile(target, M68kOutputFormat.Assembly, entry).Text;
		if (target >= M68kCpuTarget.M68040)
		{
			Assert.Contains("\tmove.l\t#$00000087,d0", assembly, StringComparison.Ordinal);
			Assert.DoesNotContain("\tmoveq\t#127,d0", assembly, StringComparison.Ordinal);
		}
		else
		{
			Assert.Contains(
				"\tmoveq\t#127,d0\r\n\taddq.w\t#8,d0",
				assembly,
				StringComparison.Ordinal);
			Assert.DoesNotContain("\tmove.l\t#$00000087,d0", assembly, StringComparison.Ordinal);
		}
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	[InlineData(M68kCpuTarget.M68060)]
	public void DivisionRemainderAndArgumentsProduceExpectedValue(M68kCpuTarget target)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ArithmeticEntry");

		Assert.Equal(16u, ExecuteHunk(result, M68kCpuModel.M68040));
	}

	[Fact]
	public void RegisterAllocationCycleCorpusMeetsMc68000Target()
	{
		var corpus = new[]
		{
			(
				Entry: "CopperSharp.Compiler.Tests.CompilerFixtures::ArithmeticEntry",
				BaselineCycles: 6_012L),
			(
				Entry: "CopperSharp.Compiler.Tests.CompilerFixtures::QuickArithmeticEntry",
				BaselineCycles: 278L),
			(
				Entry: "CopperSharp.Compiler.Tests.CompilerFixtures::BranchAssignedLocalsEntry",
				BaselineCycles: 296L),
			(
				Entry: "CopperSharp.Compiler.Tests.CompilerFixtures::NarrowByteArithmeticEntry",
				BaselineCycles: 196L),
			(
				Entry: "CopperSharp.Compiler.Tests.CompilerFixtures::NarrowShortArithmeticEntry",
				BaselineCycles: 168L)
		};
		var measurements = corpus
			.Select(item => (
				item.Entry,
				item.BaselineCycles,
				Cycles: MeasureCycles(item.Entry)))
			.ToArray();
		foreach (var measurement in measurements)
		{
			Assert.True(
				measurement.Cycles * 100 <=
					measurement.BaselineCycles * 102,
				$"{measurement.Entry} regressed from " +
				$"{measurement.BaselineCycles} to {measurement.Cycles} MC68000 cycles.");
		}
		var baselineTotal = measurements.Sum(static item => item.BaselineCycles);
		var currentTotal = measurements.Sum(static item => item.Cycles);
		Assert.True(
			currentTotal * 10 <= baselineTotal * 9,
			$"Cycle corpus improved from {baselineTotal} to {currentTotal} " +
			"MC68000 cycles, less than the required 10%.");
	}

	[Fact]
	public void CompilationReportsPerMethodAllocationStatistics()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ArithmeticEntry");

		Assert.NotEmpty(result.AllocationStatistics);
		Assert.All(
			result.AllocationStatistics,
			statistics =>
			{
				Assert.True(statistics.VirtualValues > 0);
				Assert.True(statistics.RegisterValues > 0);
				Assert.True(statistics.CodeBytes > 0);
				Assert.True(statistics.StackMemoryInstructions >= 0);
				Assert.True(statistics.Reloads >= 0);
			});
	}

	[Theory]
	[InlineData(M68kCpuModel.M68000)]
	[InlineData(M68kCpuModel.M68040)]
	public void ExecutesCompactSignedWordImmediateForms(M68kCpuModel model)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x307C); // MOVEA.W #40,A0
		assembler.EmitWord(40);
		assembler.EmitWord(0xD0FC); // ADDA.W #4,A0
		assembler.EmitWord(4);
		assembler.EmitWord(0x90FC); // SUBA.W #2,A0
		assembler.EmitWord(2);
		assembler.EmitWord(0xB0FC); // CMPA.W #42,A0
		assembler.EmitWord(42);
		assembler.EmitBranch(M68kCondition.NotEqual, "failure");
		assembler.EmitWord(0x4878); // PEA (42).W
		assembler.EmitWord(42);
		assembler.EmitWord(0x221F); // MOVE.L (A7)+,D1
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x303C); // MOVE.W #42,D0
		assembler.EmitWord(42);
		assembler.EmitWord(0xB081); // CMP.L D1,D0
		assembler.EmitBranch(M68kCondition.NotEqual, "failure");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("failure");
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x4E75); // RTS

		var linked = assembler.Link(HunkLoadAddress, new Dictionary<string, uint>());
		var bus = new TestBus();
		linked.Bytes.CopyTo(bus.Memory.AsSpan((int)HunkLoadAddress));

		Assert.Contains("pea\t$002A.w", assembler.RenderAssembly(M68kCpuTarget.M68020));
		Assert.Equal(42u, Execute(bus, model, HunkLoadAddress));
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void ExecutesCanonicalizedZeroDisplacementEffectiveAddresses(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x307C); // MOVEA.W #$0200,A0
		assembler.EmitWord(0x0200);
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0x2140); // MOVE.L D0,0(A0)
		assembler.EmitWord(0);
		assembler.EmitWord(0x43E8); // LEA 0(A0),A1
		assembler.EmitWord(0);
		assembler.EmitWord(0x42A9); // CLR.L 0(A1)
		assembler.EmitWord(0);
		assembler.EmitWord(0x52A9); // ADDQ.L #1,0(A1)
		assembler.EmitWord(0);
		assembler.EmitWord(0x2028); // MOVE.L 0(A0),D0
		assembler.EmitWord(0);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(target);
		var assembly = assembler.RenderAssembly(target);
		var linked = assembler.Link(HunkLoadAddress, new Dictionary<string, uint>());
		var bus = new TestBus();
		linked.Bytes.CopyTo(bus.Memory.AsSpan((int)HunkLoadAddress));

		Assert.DoesNotContain("0(a", assembly, StringComparison.Ordinal);
		Assert.Equal(1u, Execute(bus, model, HunkLoadAddress));
	}

	[Fact]
	public void CompilationReportsFinalLinkedLoopFootprints()
	{
		var result = Compile(
			M68kCpuTarget.M68020,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DefaultEntry");

		var matchingLoops = result.LoopFootprints.Where(footprint =>
			footprint.Method.Contains("LoopAndBranch", StringComparison.Ordinal)).ToArray();
		Assert.True(
			matchingLoops.Length == 1,
			"Reported loops: " + string.Join(", ", result.LoopFootprints.Select(
				static footprint => footprint.Method)));
		var loop = matchingLoops[0];
		Assert.True(loop.HeaderIlOffset >= 0);
		Assert.True(loop.InstructionBytes > 0);
		Assert.True(loop.SpanBytes >= loop.InstructionBytes);
		Assert.True(loop.CacheLineCount > 0);
		Assert.True(loop.FitsIn256ByteInstructionCache);
		Assert.Contains("LOOPS", result.Map, StringComparison.Ordinal);
		Assert.Contains(
			$"{loop.HeaderAddress:X8} IL_{loop.HeaderIlOffset:X4}",
			result.Map,
			StringComparison.Ordinal);
		Assert.Contains("fits256=yes", result.Map, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("CacheBoundaryLoop240Entry", "CacheBoundaryLoop240", 240, 16, true)]
	[InlineData("CacheBoundaryLoop256Entry", "CacheBoundaryLoop256", 256, 8, true)]
	[InlineData("CacheBoundaryLoop280Entry", "CacheBoundaryLoop280", 280, 16, false)]
	public void Mc68020LoopCacheBoundaryCorpusReportsFinalFootprints(
		string entry,
		string loopMethod,
		int targetBytes,
		int tolerance,
		bool expectedToFit)
	{
		var result = Compile(
			M68kCpuTarget.M68020,
			M68kOutputFormat.Hunk,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
		var loop = Assert.Single(result.LoopFootprints.Where(footprint =>
			footprint.Method.Contains(loopMethod, StringComparison.Ordinal)));

		Assert.InRange(
			loop.InstructionBytes,
			targetBytes - tolerance,
			targetBytes + tolerance);
		Assert.Equal(0u, loop.HeaderAddress & 3);
		Assert.Equal(expectedToFit, loop.FitsIn256ByteInstructionCache);
		var expected = entry switch
		{
			"CacheBoundaryLoop240Entry" => CompilerFixtures.CacheBoundaryLoop240Entry(),
			"CacheBoundaryLoop256Entry" => CompilerFixtures.CacheBoundaryLoop256Entry(),
			"CacheBoundaryLoop280Entry" => CompilerFixtures.CacheBoundaryLoop280Entry(),
			_ => throw new ArgumentOutOfRangeException(nameof(entry))
		};
		Assert.Equal(expected, ExecuteHunk(result, M68kCpuModel.M68040));
	}

	[Fact]
	public void ExceptionCycleCorpusStaysWithinMc68000Budget()
	{
		var corpus = new[]
		{
			(Entry: "AddressNullBranchInTryEntry", BaselineCycles: 224L),
			(Entry: "FinallyEntry", BaselineCycles: 410L),
			(Entry: "TryCatchEntry", BaselineCycles: 1_308L),
			(Entry: "CrossMethodCatchEntry", BaselineCycles: 1_694L),
			(Entry: "NestedCatchEntry", BaselineCycles: 1_512L),
			(Entry: "RethrowEntry", BaselineCycles: 2_346L),
			(Entry: "ExceptionalFinallyEntry", BaselineCycles: 1_812L)
		};
		foreach (var item in corpus)
		{
			var cycles = MeasureCycles(
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{item.Entry}");
			Assert.True(
				cycles * 100 <= item.BaselineCycles * 105,
				$"{item.Entry} regressed from {item.BaselineCycles} to " +
				$"{cycles} MC68000 cycles.");
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void HybridDirectCallsUseRegisterBanksAndForwardOverflowOrder(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ForwardStackArgumentEntry");

		Assert.Equal(21_234u, ExecuteHunk(result, model));
	}

	[Fact]
	public void HybridDirectCallAssemblyUsesRegistersWithoutShadowSlots()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ForwardStackArgumentEntry");

		Assert.Contains("\tmove.l\td0,-(a7)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\td1,-(a7)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tbsr.w\tC68K_method_", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\t12(a7),d0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\t8(a7),d1", result.Text, StringComparison.Ordinal);
		Assert.Contains("\taddq.l\t#8,a7", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void HybridAbiUsesD0D1PairOrWholeStackForInt64(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::HybridInt64ArgumentsEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Fact]
	public void CompilesAndExecutesMethodBodyFromReferencedModule()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			ManagedAssemblyPaths =
			[
				typeof(CopperSharp.Compiler.Tests.MultiModule.ExternalMethods).Assembly.Location
			],
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::MultiModuleEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name.EndsWith("::AddAndDouble", StringComparison.Ordinal));
	}

	[Fact]
	public void FullExceptionModeEmitsBuiltInCatchRuntime()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TryCatchEntry",
			exceptionMode: M68kExceptionMode.Full);

		Assert.Contains("__c68k_exception_raise:", result.Text, StringComparison.Ordinal);
		Assert.Contains("C68K_runtime_003Aexception_002Dtable", result.Text, StringComparison.Ordinal);
		Assert.Contains("C68K_runtime_003Amethod_002Dtable", result.Text, StringComparison.Ordinal);
		Assert.Contains("__c68k_find_unwind_site:", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmovea.l\ta7,a5", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("12(a5)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("8(a5)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjmp\t(a1)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tdc.w\t$2E6D", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tdc.w\t$4ED1", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("__c68k_eh_", result.Text, StringComparison.Ordinal);
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name == "__c68k_exception_table");
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name == "__c68k_method_table");
	}

	[Fact]
	public void ExceptionStateUsesTableEntriesWithoutMutableFrameWrites()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TryCatchEntry",
			exceptionMode: M68kExceptionMode.Full);
		var text = result.Text!;
		var methodStart = text.IndexOf(
			"\nC68K_method_003A",
			StringComparison.Ordinal) + 1;
		Assert.True(methodStart > 0);
		var methodLabelEnd = text.IndexOf(':', methodStart);
		var methodLabel = text[methodStart..methodLabelEnd];
		var methodEnd = text.IndexOf(
			$"\n{methodLabel}_003Aend:",
			methodLabelEnd,
			StringComparison.Ordinal);
		Assert.True(methodEnd > methodLabelEnd);
		var methodBody = text[methodLabelEnd..methodEnd];

		Assert.DoesNotContain("12(a5)", methodBody, StringComparison.Ordinal);
		Assert.Contains("C68K_generated_003Aunwind_site", text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmovea.l\t12(a6),a1\r\n" +
			"\tmove.l\ta0,(a1)\r\n" +
			"\tmovea.l\t12(a6),a1\r\n" +
			"\tclr.l\t4(a1)\r\n" +
			"\tmovea.l\t12(a6),a1",
			text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void ExceptionStatesShareRestoreAndJumpTail()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NestedCatchEntry",
			exceptionMode: M68kExceptionMode.Full);

		const string tail = "\tmovea.l\td1,a1\r\n\tjmp\t(a1)";
		Assert.Equal(
			1,
			result.Text!.Split(tail, StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void FullExceptionModeExecutesCatchInGeneratedRuntime()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TryCatchEntry");

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void AddressNullBranchInProtectedMethodUsesConditionCodesDirectly()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::AddressNullBranchInTryEntry");

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Matches(
			@"\tb(?:eq|ne)\.[sw]\t",
			BeforeExceptionRuntime(result));
		Assert.DoesNotContain(
			"\tseq\td0\r\n\tneg.b\td0",
			BeforeExceptionRuntime(result),
			StringComparison.Ordinal);
	}

	[Fact]
	public void ProtectedMethodDoesNotMaterializeDiscardedCallResult()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DiscardCallResultInTryEntry");

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.DoesNotContain(
			"\tmove.l\td0,-(a7)\r\n\taddq.l\t#4,a7",
			BeforeExceptionRuntime(result),
			StringComparison.Ordinal);
	}

	[Fact]
	public void ProtectedMethodBranchesDirectlyOnScalarComparison()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ComparisonStoreBranchInTryEntry");
		var methodText = BeforeExceptionRuntime(result);

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Matches(@"\tb(?:eq|ne)\.[sw]\t", methodText);
		Assert.DoesNotContain("\tseq\td0", methodText, StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmove\.b\td0,\d+\(a7\)\r?\n\tmove\.b\t\d+\(a7\),d0",
			methodText);
	}

	[Fact]
	public void FullExceptionModeEmitsFinallyRuntimeContract()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::FinallyEntry",
			exceptionMode: M68kExceptionMode.Full);

		Assert.Contains("__c68k_exception_endfinally:", result.Text, StringComparison.Ordinal);
		Assert.Contains("C68K_runtime_003Aexception_002Dtable", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void FullExceptionModeRecordsTypedCatchClauses()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TypedCatchEntry",
			exceptionMode: M68kExceptionMode.Full);

		Assert.Contains("C68K_runtime_003Aexception_002Dtable", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("__c68k_eh_", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void FullExceptionModeExecutesTypedCatchInGeneratedRuntime()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TypedCatchEntry");

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void FullExceptionModeMatchesInputAssemblyExceptionDescriptor()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CustomExceptionCatchEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::GetAllocationSize");
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::Allocate");
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::Dispose");
	}

	[Fact]
	public void FullExceptionModeRunsFinallyOnNormalLeave()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::FinallyEntry");

		Assert.Equal(3u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Theory]
	[InlineData("CrossMethodCatchEntry")]
	[InlineData("NestedCatchEntry")]
	[InlineData("RethrowEntry")]
	[InlineData("ExceptionalFinallyEntry")]
	[InlineData("A5PromotionThroughFramelessEntry")]
	[InlineData("DivideByZeroCatchEntry")]
	[InlineData("NullDereferenceCatchEntry")]
	public void FullExceptionModeHandlesNestedAndCrossMethodControlFlow(string method)
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{method}");

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ExceptionalUnwindRestoresClassicCalleeSavedRegisters()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CalleeSaveExceptionUnwindEntry");
		var bus = CreateHunkBus(result);
		var expectedData = new uint[]
		{
			0xD200_0002,
			0xD300_0003,
			0xD400_0004,
			0xD500_0005,
			0xD600_0006,
			0xD700_0007
		};
		var expectedAddress = new uint[]
		{
			0x00A2_0002,
			0x00A3_0003,
			0x00A4_0004,
			0x00A5_0005,
			0x00A6_0006
		};

		var actual = Execute(
			bus,
			M68kCpuModel.M68000,
			HunkLoadAddress + result.EntryPoint,
			initialize: state =>
			{
				expectedData.CopyTo(state.D, 2);
				expectedAddress.CopyTo(state.A, 2);
			},
			afterReturn: state =>
			{
				Assert.Equal(expectedData, state.D[2..8]);
				Assert.Equal(expectedAddress, state.A[2..7]);
			});

		Assert.Equal(42u, actual);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, 1, false)]
	[InlineData(M68kCpuTarget.M68000, 2, true)]
	[InlineData(M68kCpuTarget.M68000, 6, true)]
	[InlineData(M68kCpuTarget.M68020, 2, false)]
	[InlineData(M68kCpuTarget.M68040, 2, false)]
	[InlineData(M68kCpuTarget.M68060, 2, false)]
	public void FrameCalleeSaveMovemPolicyIsCycleFirst(
		M68kCpuTarget target,
		int registerCount,
		bool expected)
	{
		Assert.Equal(
			expected,
			M68kCodeGenerator.ShouldUseMovemForFrameCalleeSaves(
				target,
				registerCount));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void InternalCallKindsExecuteWithRootRegisterAllocation(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var entries = new[]
		{
			"HybridMixedArgumentsEntry",
			"WideConstructorArgumentsEntry",
			"WideVirtualDispatchEntry",
			"WideInterfaceDispatchEntry",
			"RecursiveCalleeSaveEntry"
		};

		foreach (var entry in entries)
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
			var bus = CreateHunkBus(result);
			var heap = 0x0000_4000u;
			bus.RegisterGateway(0x0000_2800, state =>
			{
				var size = state.D[0];
				state.D[0] = heap;
				heap += (size + 3) & ~3u;
			});

			Assert.Equal(
				42u,
				Execute(
					bus,
					model,
					HunkLoadAddress + result.EntryPoint));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinkedManagedRuntimePreservesClassicCalleeSavedRegisters(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolCollectTracesCallerFrameEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 88
			}
		});
		var bus = CreateHunkBus(result);

		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				initialize: InitializeClassicCalleeSavedRegisters,
				afterReturn: state => AssertClassicCalleeSavedRegisters(state, "managed runtime")));
	}

	[Theory]
	[InlineData("BoundsCatchEntry")]
	[InlineData("OutOfMemoryCatchEntry")]
	public void FullExceptionModeHandlesManagedRuntimeFaults(string entry)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_0400
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Theory]
	[InlineData("A5ImportInsideCatchEntry")]
	[InlineData("A5ImportThroughFramelessEntry")]
	public void ProtectedImportUsingA5PreservesRuntimeFrameChain(string entry)
	{
		const uint importAddress = 0x0000_2E00;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
			imports: new Dictionary<string, uint>
			{
				["fixture.a5Value"] = importAddress
			});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(importAddress, state => state.D[0] = state.A[5]);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
	}

	[Fact]
	public void ManagedImportUsingA5PreservesRuntimeFrameChain()
	{
		const uint importAddress = 0x0000_2E00;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedA5ImportEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			},
			Imports = new Dictionary<string, uint>
			{
				["fixture.a5Value"] = importAddress
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(importAddress, state => state.D[0] = state.A[5]);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
	}

	[Fact]
	public void ExternalNonzeroStatusRaisesCatchableManagedException()
	{
		const uint callAddress = 0x0000_3000;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ExternalFailureCatchEntry",
			ExternalCallResolvers =
			[
				new ExceptionStatusResolver(callAddress + 30)
			]
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(callAddress, state =>
		{
			state.D[0] = 1;
			state.D[1] = 99;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
	}

	[Fact]
	public void ExternalZeroStatusPreservesD0ReturnValue()
	{
		const uint callAddress = 0x0000_3100;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ExternalSuccessEntry",
			ExternalCallResolvers =
			[
				new ExceptionStatusResolver(callAddress + 30)
			]
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(callAddress, state =>
		{
			state.D[0] = 42;
			state.D[1] = 0;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
	}

	[Fact]
	public void AmigaUnhandledExceptionUsesRequesterAfterOptionalHook()
	{
		var withoutHook = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::UnhandledExceptionEntry");
		var withHook = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::UnhandledExceptionEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.UnhandledException] = 0x0000_2E00
			});

		Assert.DoesNotContain(
			$"\tjsr\t{M68kRuntimeImports.UnhandledException}",
			withoutHook.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"__c68k_amiga_unhandled_requester:",
			withoutHook.Text,
			StringComparison.Ordinal);
		Assert.Contains("\tjsr\t-348(a6)", withoutHook.Text, StringComparison.Ordinal);
		Assert.Contains("\tmoveq\t#20,d0", withoutHook.Text, StringComparison.Ordinal);
		Assert.Contains(
			$"\tjsr\t{M68kRuntimeImports.UnhandledException}",
			withHook.Text,
			StringComparison.Ordinal);
		Assert.Matches(
			@"\t(?:jmp|bra\.[sw])\t__c68k_amiga_unhandled_requester",
			withHook.Text);
		Assert.Contains("\tillegal", withHook.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void AmigaUnhandledExceptionRequesterCanExitThroughOriginalStack()
	{
		const uint execBase = 0x0000_3000;
		const uint intuitionBase = 0x0000_3800;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::UnhandledExceptionEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opened = false;
		var requested = false;
		var closed = false;
		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal("intuition.library", ReadCString(bus, state.A[1]));
			Assert.Equal(0u, state.D[0]);
			opened = true;
			state.D[0] = intuitionBase;
		});
		bus.RegisterGateway(intuitionBase - 348, state =>
		{
			Assert.Equal(0u, state.A[0]);
			Assert.Equal(
				"Unhandled managed exception.",
				ReadCString(bus, bus.ReadLong(state.A[1] + 12)));
			Assert.Equal("Exit", ReadCString(bus, bus.ReadLong(state.A[2] + 12)));
			Assert.Equal("Freeze", ReadCString(bus, bus.ReadLong(state.A[3] + 12)));
			Assert.Equal(320u, state.D[2]);
			Assert.Equal(72u, state.D[3]);
			requested = true;
			state.D[0] = 1;
		});
		bus.RegisterGateway(execBase - 414, state =>
		{
			Assert.Equal(intuitionBase, state.A[1]);
			closed = true;
		});

		Assert.Equal(
			20u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.True(opened);
		Assert.True(requested);
		Assert.True(closed);
	}

	[Fact]
	public void AmigaUnhandledExceptionRequesterFreezeClosesIntuitionThenCrashes()
	{
		const uint execBase = 0x0000_3000;
		const uint intuitionBase = 0x0000_3800;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::UnhandledExceptionEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var closed = false;
		bus.RegisterGateway(execBase - 552, state => state.D[0] = intuitionBase);
		bus.RegisterGateway(intuitionBase - 348, state => state.D[0] = 0);
		bus.RegisterGateway(execBase - 414, _ => closed = true);

		Assert.Throws<InvalidOperationException>(() =>
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint,
				beforeInstruction: (cpu, memory) =>
				{
					if (memory.ReadWord(cpu.State.ProgramCounter) == 0x4AFC)
					{
						throw new InvalidOperationException("Reached ILLEGAL crash fallback.");
					}
				}));
		Assert.True(closed);
	}

	[Fact]
	public void AmigaUnhandledExceptionRequesterOpenFailureCrashes()
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::UnhandledExceptionEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		bus.RegisterGateway(execBase - 552, state => state.D[0] = 0);

		Assert.Throws<InvalidOperationException>(() =>
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint,
				beforeInstruction: (cpu, memory) =>
				{
					if (memory.ReadWord(cpu.State.ProgramCounter) == 0x4AFC)
					{
						throw new InvalidOperationException("Reached ILLEGAL crash fallback.");
					}
				}));
	}

	[Fact]
	public void YoloExceptionModeRejectsManagedExceptionRegions()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			Compile(
				M68kCpuTarget.M68000,
				M68kOutputFormat.Assembly,
				"CopperSharp.Compiler.Tests.CompilerFixtures::TryCatchEntry",
				exceptionMode: M68kExceptionMode.Yolo));

		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, exception.DiagnosticId);
		Assert.Contains("YOLO exception mode", exception.Message, StringComparison.Ordinal);

		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DefaultEntry",
			exceptionMode: M68kExceptionMode.Yolo);
		Assert.DoesNotContain(
			"__c68k_amiga_unhandled_requester",
			result.Text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void DiscardedCallResultDoesNotRoundTripThroughEvaluationStack()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DiscardCallResultEntry");

		Assert.Contains("\tbsr.w\tC68K_method_", result.Text, StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tbsr\.w\tC68K_method_[A-Za-z0-9_]+\r?\n\tmove\.l\td0,-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\taddq\.l\t#4,a7",
			result.Text);
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
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
	public void LowersConstantShiftCountToImmediateShift(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstantUnsignedShiftEntry");

		Assert.Equal(21u, ExecuteHunk(result, model));
		Assert.Contains("\tlsr.l\t#1,d0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmoveq\t#1,d1", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tandi.l\t#$0000001F,d1", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void SplitsLargeConstantShiftCountsIntoImmediateChunks()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstantUnsignedShiftNineEntry");

		Assert.Equal(2u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Contains(
			"\tlsr.l\t#8,d0\r\n\tlsr.l\t#1,d0",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain("\tmoveq\t#9,d1", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tandi.l\t#$0000001F,d1", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void MaterializedComparisonStoresResultWithoutStackRoundTrip()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MaterializedEqualityEntry");

		Assert.Equal(21u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Matches(
			@"\t(?:cmp\.l\td[0-7],d[0-7]|cmpi\.l\t#[^,]+,d[0-7])",
			result.Text);
		Assert.DoesNotMatch(@"\tcmp\.l\t\d+\(a7\),d0", result.Text);
		Assert.DoesNotContain("\tmove.l\t(a7)+,d7", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmoveq\t#1,d7", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmoveq\t#1,d0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\t(a7)+,d1\r\n\tmove.l\t(a7)+,d0\r\n\tcmp.l\td1,d0",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tseq\td0\r\n\text.w\td0\r\n\text.l\td0\r\n\tneg.l\td0\r\n\tmove.l\td0,-(a7)\r\n",
			result.Text,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("BooleanOrControlFlowEntry")]
	[InlineData("BooleanAndControlFlowEntry")]
	public void BooleanControlFlowBranchesDirectlyFromComparisons(string entry)
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.DoesNotMatch(
			@"\ts(?:eq|ne|lt|le|gt|ge|cs|cc|hi|ls)\td[0-7]\r?\n\text\.w\td[0-7]\r?\n\text\.l\td[0-7]\r?\n\tneg\.l\td[0-7]",
			result.Text);
		Assert.DoesNotMatch(
			@"(?m)^\tb(?!ra)[a-z]+\.w\t[^\r\n]+\r?\n\tbra\.w\t",
			result.Text);
	}

	[Fact]
	public void AssignedLocalsDoNotEmitEntryClears()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ManyAssignedLocalsEntry");

		Assert.DoesNotMatch(@"\tclr\.l\t\d+\(a7\)", result.Text);
		Assert.Contains("\tmoveq\t#7,d2", result.Text, StringComparison.Ordinal);
		Assert.Equal(36u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void AmbiguousFirstAccessLocalsUseGroupedEntryClear()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::BranchAssignedLocalsEntry");

		Assert.DoesNotMatch(
			@"\tmove\.l\t\(a7\)\+,\d+\(a7\)",
			result.Text);
		Assert.Equal(10u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ZeroConstantPushAvoidsMoveqBounce()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DefaultEntry");

		Assert.DoesNotContain(
			"\tmoveq\t#0,d0\r\n\tmove.l\td0,-(a7)",
			result.Text,
			StringComparison.Ordinal);
		Assert.Equal(106u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void FrameClearsAreTargetAwareAndSupportGlobalOptIn()
	{
		var m68000FrameClear = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TryCatchEntry");
		Assert.Matches(
			@"\tmoveq\t#0,(d[0-7])\r\n\tmove\.l\t\1,\d+\(a7\)",
			m68000FrameClear.Text);
		Assert.DoesNotMatch(@"\tclr\.l\t\d+\(a5\)", m68000FrameClear.Text);

		var m68020 = Compile(
			M68kCpuTarget.M68020,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TryCatchEntry");
		Assert.Matches(
			@"\tmoveq\t#0,(d[0-7])\r\n\tmove\.l\t\1,\d+\(a7\)",
			m68020.Text);

		var optIn = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TryCatchEntry",
			M68kClrPolicy.Always);
		Assert.Matches(@"\tclr\.l\t\d+\(a7\)", optIn.Text);
	}

	[Fact]
	public void CompilerOwnedLibrarySlotsUseClrOnM68000()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ClearDosLibraryBaseWithNull");

		Assert.Contains("\tclr.l\t_DOSLibraryBase", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\t#$00000000,_DOSLibraryBase",
			result.Text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void TerminalEntryRemovesUnobservablePrivateDefaultStores()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TerminalPrivateDefaultStoresEntry");

		foreach (var fieldName in new[]
			{
				"_terminalScalar",
				"_terminalReference",
				"_terminalAddress"
			})
		{
			var label = StaticFieldLabel(fieldName);
			Assert.DoesNotMatch(
				$@"\t(?:clr\.l\t{label}|move\.l\t[^\r\n]+,{label})",
				result.Text);
		}
		var statistics = Assert.Single(
			result.TerminalDeadStoreStatistics,
			statistics => statistics.Candidates != 0);
		Assert.Equal(3, statistics.Candidates);
		Assert.Equal(3, statistics.Removed);
		Assert.All(statistics.Details, detail =>
		{
			Assert.True(detail.Removed);
			Assert.Null(detail.RejectionReason);
			Assert.True(detail.IlOffset >= 0);
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void TerminalManagedReferenceStoreRemainsLiveAcrossGcObservation()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::TerminalObservedReferenceStoreEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Application,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		AssertStaticStore(result.Text!, "_terminalReference");
		var statistics = Assert.Single(
			result.TerminalDeadStoreStatistics,
			statistics => statistics.Candidates != 0);
		var candidate = Assert.Single(statistics.Details);
		Assert.False(candidate.Removed);
		Assert.Equal("value-may-be-observed", candidate.RejectionReason);
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void TerminalStoreRemainsBeforeUnknownManagedCall()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TerminalStoreBeforeUnknownCallEntry");

		AssertStaticStore(result.Text!, "_terminalScalar");
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void NonTerminalMethodKeepsPrivateDefaultStore()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NonTerminalPrivateStoreEntry");

		AssertStaticStore(result.Text!, "_terminalAddress");
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void PersistentRomProfileKeepsPrivateDefaultStore()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::TerminalPrivateDefaultStoresEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.KickstartRom,
			RuntimeProfile = M68kRuntimeProfile.Rom
		});

		Assert.Contains(
			result.Relocations,
			relocation => relocation.Target == StaticFieldRelocation("_terminalScalar"));
	}

	[Fact]
	public void FreestandingAssemblyKeepsPrivateDefaultStore()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::TerminalPrivateDefaultStoresEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding
		});

		AssertStaticStore(result.Text!, "_terminalScalar");
	}

	[Fact]
	public void TerminalFinalOverwriteRemovesOnlyTheUnobservableDefaultStore()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TerminalOverwriteEntry");
		var label = StaticFieldLabel("_terminalScalar");

		Assert.Matches($@"\tmove\.l\t[^\r\n]+,{label}", result.Text);
		Assert.DoesNotContain($"\tclr.l\t{label}", result.Text, StringComparison.Ordinal);
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ConditionalReadOnOnePathKeepsTerminalStore()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TerminalConditionalReadEntry");

		AssertStaticStore(result.Text!, "_terminalScalar");
	}

	[Fact]
	public void EscapedStaticAddressKeepsTerminalStore()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TerminalEscapedStaticAddressEntry");

		AssertStaticStore(result.Text!, "_terminalScalar");
	}

	[Fact]
	public void ExceptionalHandlerReadKeepsTerminalStore()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TerminalExceptionalReadEntry");

		AssertStaticStore(result.Text!, "_terminalScalar");
		Assert.Equal(0u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void MultipleTerminalReturnsRemoveIndependentDefaultStores()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TerminalMultipleReturnsEntry");
		var label = StaticFieldLabel("_terminalScalar");

		Assert.DoesNotMatch(
			$@"\t(?:clr\.l\t{label}|move\.l\t[^\r\n]+,{label})",
			result.Text);
	}

	[Fact]
	public void OutermostTerminalFinallyRemovesPrivateDefaultStore()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TerminalFinallyStoreEntry");
		var label = StaticFieldLabel("_terminalAddress");

		Assert.DoesNotMatch(
			$@"\t(?:clr\.l\t{label}|move\.l\t[^\r\n]+,{label})",
			result.Text);
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ManagedPoolShutdownDoesNotObserveFinalPrivateReferenceClear()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::TerminalPrivateDefaultStoresEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Application,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});
		var label = StaticFieldLabel("_terminalReference");

		Assert.DoesNotMatch(
			$@"\t(?:clr\.l\t{label}|move\.l\t[^\r\n]+,{label})",
			result.Text);
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void UnknownExternalGcShutdownKeepsPrivateDefaultStore()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::TerminalPrivateDefaultStoresEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Application,
			MemoryManagement = M68kMemoryManagement.ExecPoolMarkSweepGc
		});

		AssertStaticStore(result.Text!, "_terminalScalar");
		Assert.DoesNotContain(
			result.TerminalDeadStoreStatistics,
			statistics => statistics.Candidates != 0);
	}

	[Fact]
	public void TerminalArrayStoreIsOutsideExactObjectScope()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::TerminalArrayStoreEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Application,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.DoesNotContain(
			result.TerminalDeadStoreStatistics,
			statistics => statistics.Candidates != 0);
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void TerminalLivenessConvergesAcrossLoop()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::TerminalLoopEntry");
		var label = StaticFieldLabel("_terminalScalar");

		Assert.DoesNotMatch(
			$@"\t(?:clr\.l\t{label}|move\.l\t[^\r\n]+,{label})",
			result.Text);
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void LibraryVectorBaseReadKeepsTerminalClear()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ClearDosLibraryBaseBeforeVectorCall");

		Assert.Matches(
			@"\t(?:clr\.l\t_DOSLibraryBase|move\.l\t[^\r\n]+,_DOSLibraryBase)",
			result.Text);
		Assert.Contains("\tmovea.l\t_DOSLibraryBase(pc),a6", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ManagedObjectHeadersKeepDescriptorRelocations()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Contains(
			"\tmove.l\t#C68K_array_003Aint,(a0)",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\t#$00000000,(a0)",
			result.Text,
			StringComparison.Ordinal);
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
			OutputFormat = M68kOutputFormat.Assembly,
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
		Assert.Contains("\tmoveq\t#17,d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmoveq\t#25,d1", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjsr\tC68K_fixture_002EregisterAdd", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmoveq\t#17,d0\r\n\tmove.l\td0,-(a7)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmoveq\t#25,d0\r\n\tmove.l\td0,-(a7)", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PromotesLoopCounterAcrossRegisterAbiCall(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint importAddress = 0x0000_2300;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::RegisterPromotedLoopCounterAcrossRegisterCall",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				["fixture.registerAdd"] = importAddress
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(importAddress, state =>
		{
			state.D[2] = state.D[0] + state.D[1];
		});

		Assert.Equal(6u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Matches(
			@"\tsub(?:\.l\td[0-7],d[0-7]|q\.l\t#[1-8],d[0-7])",
			result.Text);
		Assert.Matches(
			@"\t(?:cmp\.l\td[0-7],d[0-7]|cmpi\.l\t#[^,]+,d[0-7])",
			result.Text);
		Assert.DoesNotContain("\tmove.l\t(a7)+", result.Text, StringComparison.Ordinal);
		Assert.DoesNotMatch(@"\tsubq\.l\t#1,\d+\(a7\)", result.Text);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DirectCallResultStoreUsesRegisterAbiForInternalCalls(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StoreInternalRegisterCallResult",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
		Assert.Contains("\tmoveq\t#17,d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmoveq\t#25,d1", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmoveq\t#17,d0\r\n\tmove.l\td0,-(a7)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmoveq\t#25,d0\r\n\tmove.l\td0,-(a7)", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LowersBoopsiDoMethodConvenienceToDoMethodA(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint doMethodAddress = 0x0000_2400;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallBoopsiDoMethod",
			Cpu = target,
			Imports = new Dictionary<string, uint>
			{
				["amiga.boopsi.DoMethodA"] = doMethodAddress
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(doMethodAddress, state =>
		{
			Assert.Equal(0x0000_1234u, state.A[0]);
			var message = state.A[1];
			Assert.Equal(0x8042_3BA6u, bus.ReadLong(message));
			Assert.Equal(7u, bus.ReadLong(message + 4));
			Assert.Equal(9u, bus.ReadLong(message + 8));
			state.D[0] = 42;
		});

		Assert.Equal(42u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Fact]
	public void FixedBoopsiDoMethodEmitsDirectMessageStack()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallBoopsiDoMethod",
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				["amiga.boopsi.DoMethodA"] = 0x0000_2600
			}
		});

		Assert.Matches(@"\t(?:movea\.l\ta7|lea\t\d+\(a7\)),a1", result.Text);
		Assert.Contains("\tadda.w\t#12,a7", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tlea\t0(a7),a1", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\t(a7)+,d3\r\n\tmove.l\t(a7)+,d2\r\n\tmove.l\t(a7)+,d1\r\n\tmove.l\t(a7)+,d0",
			result.Text,
			StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LowersBoopsiDoMethodParamsToDoMethodA(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint doMethodAddress = 0x0000_2600;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallBoopsiDoMethodStackVarargs",
			Cpu = target,
			Imports = new Dictionary<string, uint>
			{
				["amiga.boopsi.DoMethodA"] = doMethodAddress
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(doMethodAddress, state =>
		{
			Assert.Equal(0x0000_1234u, state.A[0]);
			var message = state.A[1];
			Assert.Equal(0x8042_C9CBu, bus.ReadLong(message));
			Assert.Equal(0x8042_E86Eu, bus.ReadLong(message + 4));
			Assert.Equal(0x4987_9DB1u, bus.ReadLong(message + 8));
			Assert.Equal(0x0000_5678u, bus.ReadLong(message + 12));
			Assert.Equal(2u, bus.ReadLong(message + 16));
			Assert.Equal(0x8042_76EFu, bus.ReadLong(message + 20));
			Assert.Equal(0xffff_ffffu, bus.ReadLong(message + 24));
			state.D[0] = 43;
		});

		Assert.Equal(43u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Fact]
	public void LargeStackArgumentDiscardUsesSingleAdda()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallBoopsiDoMethodStackVarargs",
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				["amiga.boopsi.DoMethodA"] = 0x0000_2600
			}
		});

		Assert.Contains("\tadda.w\t#", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tlea\t0(a7),a1", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\td2,4(a7)\r\n\tmove.l\td3,8(a7)\r\n\tmove.l\td4,12(a7)",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\taddq.l\t#8,a7\r\n\taddq.l\t#8,a7\r\n\taddq.l\t#8,a7\r\n\taddq.l\t#4,a7",
			result.Text,
			StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LowersMuiNewObjectParamsTagsToStackTagList(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint libraryBase = 0x0000_3800;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallMuiNewObjectStackTags",
			Cpu = target
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				["muimaster.library"] = libraryBase
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(libraryBase - 30, state =>
		{
			Assert.Equal("Window.mui", ReadCString(bus, state.A[0]));
			var tags = state.A[1];
			Assert.Equal(global::Amiga.MUI.Window.Title, bus.ReadLong(tags));
			Assert.Equal("Fixture Window", ReadCString(bus, bus.ReadLong(tags + 4)));
			Assert.Equal(global::Amiga.MUI.Tag.Done, bus.ReadLong(tags + 8));
			state.D[0] = 0x0000_4242;
		});

		Assert.Equal(0x0000_4242u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LowersMuiMakeObjectParamsToStackParameterList(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint libraryBase = 0x0000_3A00;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallMuiMakeObjectStackParameters",
			Cpu = target
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				["muimaster.library"] = libraryBase
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(libraryBase - 120, state =>
		{
			Assert.Equal((uint)global::Amiga.MUI.MakeObject.Button, state.D[0]);
			Assert.Equal("Fixture Button", ReadCString(bus, bus.ReadLong(state.A[0])));
			state.D[0] = 0x0000_4343;
		});

		Assert.Equal(0x0000_4343u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LowersAnyExternalStackVarargsParamsToDeclaredPointerRegister(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint libraryBase = 0x0000_3B00;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallIntuitionNewObjectStackTags",
			Cpu = target
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				["intuition.library"] = libraryBase
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(libraryBase - 636, state =>
		{
			Assert.Equal(0x0000_1111u, state.A[0]);
			Assert.Equal(0x0000_2222u, state.A[1]);
			var tags = state.A[2];
			Assert.Equal(global::Amiga.MUI.Window.Title, bus.ReadLong(tags));
			Assert.Equal("Fixture Custom Object", ReadCString(bus, bus.ReadLong(tags + 4)));
			Assert.Equal(global::Amiga.MUI.Tag.Done, bus.ReadLong(tags + 8));
			state.D[0] = 0x0000_4545;
		});

		Assert.Equal(0x0000_4545u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LowersDosPrintfParamsToStackArgumentList(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint libraryBase = 0x0000_3C00;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallDosPrintfStackArguments",
			Cpu = target
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(libraryBase - 954, state =>
		{
			Assert.Equal("value: %ld %s\n", ReadCString(bus, state.D[1]));
			Assert.Equal(10u, bus.ReadLong(state.D[2]));
			Assert.Equal("items", ReadCString(bus, bus.ReadLong(state.D[2] + 4)));
			state.D[0] = 12;
		});

		Assert.Equal(12u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LowersImplicitCStringLiteralForAmigaApi(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint libraryBase = 0x0000_3C00;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallDosPutStrImplicitLiteral",
			Cpu = target
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(libraryBase - 948, state =>
		{
			Assert.Equal("implicit CString\n", ReadCString(bus, state.D[1]));
			state.D[0] = 17;
		});

		Assert.Equal(17u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LowersDosLibraryBasePropertyToManualBaseSlot(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ReadDosLibraryBaseAfterSet",
			Cpu = target
		});

		Assert.Equal(0x0000_3C00u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StoresNullableValueToLibraryBaseSlotWithoutStackRoundTrip(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::SetDosLibraryBaseFromNullableValue",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Equal(0x0000_3C00u, ExecuteHunk(result, model));
		Assert.Matches(@"\tmove\.l\t(?:#\$[0-9A-F]{8}|\d+\(a7\)|[da]\d),_DOSLibraryBase", result.Text);
		Assert.DoesNotMatch(@"\tmove\.l\t\d+\(a7\),d0\r?\n\tmove\.l\td0,_DOSLibraryBase", result.Text);
		Assert.DoesNotContain("\tmove.l\t(a7)+,_DOSLibraryBase", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\td0,-(a7)\r\n\tmove.l\t(a7)+,_DOSLibraryBase",
			result.Text,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(
		"CopperSharp.Compiler.Tests.CompilerFixtures::ReadGraphicsLibraryBaseAfterSet",
		0x0000_3E00u,
		"_GraphicsLibraryBase")]
	[InlineData(
		"CopperSharp.Compiler.Tests.CompilerFixtures::ReadIffParseLibraryBaseAfterSet",
		0x0000_4000u,
		"_IFFParseLibraryBase")]
	public void LowersAllLibraryBasePropertiesToManualBaseSlots(
		string entryPoint,
		uint expected,
		string slotSymbol)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = entryPoint,
			Cpu = M68kCpuTarget.M68000
		});

		Assert.Equal(expected, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Contains(result.Symbols, symbol => symbol.Name == slotSymbol);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LowersAptrNullForLibraryBaseProperties(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ClearDosLibraryBaseWithNull",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
		Assert.Contains("\tclr.l\t_DOSLibraryBase", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\t(a7)+,_DOSLibraryBase", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SupportsNullableTransparentScalarNull(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::NullableAptrNullEntry",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
		var applicationText = BeforeExceptionRuntime(result);
		Assert.DoesNotContain("\ttst.l\td0", applicationText, StringComparison.Ordinal);
		Assert.DoesNotContain("\tsne\td1", applicationText, StringComparison.Ordinal);
		Assert.DoesNotContain("\tneg.b\td1", applicationText, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SupportsNullableTransparentScalarValue(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::NullableAptrValueEntry",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Equal(0x0000_4400u, ExecuteHunk(result, model));
		var applicationText = BeforeExceptionRuntime(result);
		Assert.DoesNotContain("\ttst.l\td0", applicationText, StringComparison.Ordinal);
		Assert.DoesNotContain("\tsne\td1", applicationText, StringComparison.Ordinal);
		Assert.DoesNotContain("\tneg.b\td1", applicationText, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("StrPtrValueEntry", 0x0000_4500u)]
	[InlineData("ConstStrPtrValueEntry", 0x0000_4600u)]
	[InlineData("ConstStrPtrFromStrPtrEntry", 0x0000_4700u)]
	public void SupportsAmigaStringPointerTransparentScalars(
		string entry,
		uint expected)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Equal(expected, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.DoesNotContain("NotSupportedException", result.Map, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PassesAmigaStartupArgumentsToEntryPoint(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::AmigaStartupArgsEntry",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});
		var bus = CreateHunkBus(result);

		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				initialize: state =>
				{
					state.D[0] = 10;
					state.A[0] = 32;
				}));
		Assert.Equal(0u, result.EntryPoint);
		Assert.DoesNotContain("\r\nC68K_entry_003Amanaged:\r\n\tmove.l\ta0,d1\r\n", result.Text, StringComparison.Ordinal);
		Assert.Contains("\r\nC68K_method_", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void PreservesAmigaStartupArgumentsAcrossManagedRuntimeInit()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::AmigaStartupArgsEntry",
			OutputFormat = M68kOutputFormat.Assembly,
			MemoryManagement = M68kMemoryManagement.ExecPoolMarkSweepGc,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.GcInit] = 0x0000_2400,
				[M68kRuntimeImports.GcShutdown] = 0x0000_2600
			}
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(0x0000_2400, state =>
		{
			state.D[0] = 1;
			state.A[0] = 2;
		});
		bus.RegisterGateway(0x0000_2600, state =>
		{
			state.D[0] = 3;
			state.A[0] = 4;
		});

		Assert.Equal(
			42u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint,
				initialize: state =>
				{
					state.D[0] = 10;
					state.A[0] = 32;
				}));
		Assert.Contains("\tmove.l\td0,d2\r\n\tmovea.l\ta0,a2", result.Text, StringComparison.Ordinal);
		Assert.Matches(
			@"\tmove\.l\td2,d0\r\n\tmovea\.l\ta2,a0\r\n\tbsr\.[sw]\tC68K_method_",
			result.Text);
		Assert.Contains(
			"\tmove.l\td2,d0\r\n\tmovea.l\t(a7)+,a2\r\n\tmove.l\t(a7)+,d2",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmovea.l\ta2,a0\r\n\tmove.l\td2,d0",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\td0,-(a7)\r\n\tpea\t(a0)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmovea.l\t(a7)+,a0\r\n\tmove.l\t(a7)+,d0", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PromotesTransparentScalarLocalsToAddressRegisters(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3600;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PromotedAptrLocalAcrossExecCall",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		bus.RegisterGateway(execBase - 552, state =>
		{
			state.D[0] = 0x0000_4200;
		});

		Assert.Equal(0x0000_4400u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0u, result.EntryPoint);
		var assembly = result.Text!;
		var entryIndex = assembly.IndexOf(
			"\r\nC68K_entry_003Amanaged:",
			StringComparison.Ordinal);
		Assert.True(entryIndex >= 0);
		var execBaseIndex = assembly.IndexOf(
			"\tmovea.l\t$0004.w,a6\r\n\tmove.l\ta6,_ExecBase",
			entryIndex,
			StringComparison.Ordinal);
		Assert.True(execBaseIndex > entryIndex);
		var methodIndex = assembly.IndexOf(
			"\r\nC68K_method_",
			execBaseIndex,
			StringComparison.Ordinal);
		Assert.True(methodIndex > execBaseIndex);
		Assert.DoesNotContain("\tjmp\tC68K_method_", result.Text, StringComparison.Ordinal);

		Assert.DoesNotContain("\tmove.l\t$0004.w,d0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\t(a7)+,_ExecBase", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\t$0004.w,_ExecBase", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmovea.l\t_ExecBase(pc),a6", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PromotedTransparentScalarLocalSkipsCachedPlatformBaseRegister(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint baseSourceAddress = 0x0000_0300;
		const uint platformBase = 0x0000_3E00;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PromotedAptrLocalAvoidsCachedPlatformBaseRegister",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly,
			ExternalCallResolvers = new[] { new CachedPlatformBaseResolver(baseSourceAddress) }
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(baseSourceAddress, platformBase);
		bus.RegisterGateway(platformBase - 30, state =>
		{
			Assert.Equal(platformBase, state.A[6]);
			Assert.Equal(platformBase, state.A[4]);
			state.D[0] = 7;
		});

		Assert.Equal(0x0000_4400u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Matches(
			@"\tmovea\.w\t#\$4400,a(?:[0-3]|[5-6])",
			result.Text);
		Assert.Contains("\tmovea.l\ta4,a6", result.Text, StringComparison.Ordinal);
		Assert.DoesNotMatch(@"\tmovea\.[wl]\t#\$(?:0000)?4400,a4", result.Text);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void TransparentScalarLocalsRemainRegisterAllocatedUnderPressure(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PromotedAptrLocalsCanUseA6",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});
		Assert.Equal(0x0000_0F00u, ExecuteHunk(result, model));
		Assert.NotNull(result.Text);
		var entryBody = result.Text[..result.Text.IndexOf(
			"\trts",
			StringComparison.Ordinal)];
		Assert.DoesNotContain("(a7)", entryBody, StringComparison.Ordinal);
		Assert.Matches(
			@"\t(?:move\.w\t#\$0100,d[0-7]|movea\.w\t#\$0100,a[0-6])",
			entryBody);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void A6LocalPromotionInvalidatesLoadedPlatformBase(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3600;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::A6PromotionBetweenExecCallsReloadsBase",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var calls = 0;
		bus.RegisterGateway(execBase - 552, state =>
		{
			calls++;
			Assert.Equal(execBase, state.A[6]);
			state.D[0] = 0x0000_4200 + (uint)calls;
		});

		Assert.Equal(0x0000_0F00u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(2, calls);
		Assert.True(
			result.Text!.Split(
				"\tmovea.l\t_ExecBase(pc),a6",
				StringSplitOptions.None).Length - 1 >= 1);
		Assert.Matches(
			@"\tmovea\.w\t#\$0[1-5]00,a[0-6]",
			result.Text);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SupportsNullableUIntValue(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::NullableUIntValueEntry",
			Cpu = target
		});

		Assert.Equal(37u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SupportsNullableIntNull(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::NullableIntNullEntry",
			Cpu = target
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SupportsNullableGetValueOrDefaultArgument(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::NullableUIntDefaultEntry",
			Cpu = target
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Fact]
	public void CompilesMuiSunflowerSampleEventLoop()
	{
		var sdkAssembly = typeof(global::Amiga.MUIMaster).Assembly.Location;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = sdkAssembly,
			EntryPoint = "MUISunflower.Program::Main",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				["amiga.boopsi.DoMethodA"] = 0x0000_2600
			}
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				["muimaster.library"] = 0x0000_3800
			}
		});
		Assert.NotEmpty(result.Code);
		Assert.DoesNotContain("Amiga.MUI.WindowObject::op_Implicit", result.Map, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\t#$00000000,16(a7)",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmoveq\t#0,d0\r?\n\tmove\.l\td0,\d+\(a7\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t\d+\(a7\),d0\r?\n\tmove\.l\td0,\d+\(a7\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t\(a0\),d0\r?\n\tmove\.l\td0,\d+\(a7\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t\(a0\),\d+\(a7\)",
			result.Text);
		Assert.DoesNotContain(
			"\tmove.l\td0,(a7)\r\n\tmovea.l\t(a7),a0",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\t56(a7),12(a7)",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\t(a7)+,d3\r\n\tmove.l\t(a7)+,d2\r\n\tmove.l\t(a7)+,d1\r\n\tmove.l\t(a7)+,d0",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmove\.l\t\d+\(a7\),-\(a7\)\r?\n[a-zA-Z0-9_:\r\n]*\tmove\.l\t\(a7\)\+,d0\r?\n\ttst\.l\td0",
			result.Text);
		Assert.DoesNotContain(
			"\tmovea.l\t12(a7),a0\r\n\tmove.l\t8(a7),d0\r\n\tmove.l\t4(a7),d1",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\t(a0),-(a7)\r\n\tmovea.l\t(a7)+,a0",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tjsr\tC68K_amiga_002Eboopsi_002EDoMethodA\r?\n\tadda\.w\t#12,a7\r?\n\tmove\.l\td0,-\(a7\)\r?\n(?:[A-Za-z0-9_]+:\r?\n)+\tmove\.l\t\(a7\)\+,d0",
			result.Text);
		Assert.DoesNotContain(
			"\tjsr\tC68K_amiga_002Eboopsi_002EDoMethodA\r\n\tadda.w\t#12,a7\r\n\taddq.l\t#8,a7\r\n\trts",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tsubq.l\t#4,a7\r\n\tmove.l\ta0,(a7)",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"C68K_method_003A06000607:\r?\n\tsubq\.l",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\ta0,-\(a7\)\r?\n[A-Za-z0-9_:]+:\r?\n\tmovea\.l\t\(a7\)\+,a0",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\ta0,-\(a7\)\r?\n[A-Za-z0-9_:]+:\r?\n\tmovea\.l\t0\(a7\),a0\r?\n\taddq\.l\t#4,a7",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmovea\.l\t\(a7\)\+,a[0-7]\r?\n\tmove\.l\td0,\(a[0-7]\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmovea\.l\td0,a0\r?\n(?:\t.*\r?\n){0,8}\tmove\.l\t\(a0\),12\(a7\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t#\$80424842,d[2-7]\r?\n\tmove\.l\t#\$8042E07A,d[2-7]\r?\n\tmove\.l\t#\$80421FC6,d[2-7]",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\td0,-\(a7\)\r?\n[A-Za-z0-9_:]+:\r?\n\tmove\.l\t\(a7\)\+,\d+\(a7\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\td0,-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tbra\.w\t[A-Za-z0-9_:]+\r?\n[A-Za-z0-9_:]+:\r?\n\tmoveq\t#1,d0\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmove\.l\td0,-\(a7\)\r?\n[A-Za-z0-9_:]+:\r?\n\tmove\.l\t\(a7\)\+,\d+\(a7\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tlea\t\d+\(a7\),a0\r?\n\tbsr\.w\tC68K_method_003A0600067[14]",
			result.Text);
		Assert.DoesNotContain(
			"\tmove.l\ta7,d2",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmove\.l\t#(\$[0-9A-F]{8}|C68K_cstring_[A-Za-z0-9_]+),-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmove\.l\t\(a7\)\+,\d+\(a7\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t#C68K_cstring_[A-Za-z0-9_]+,-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmovea\.l\t\(a7\)\+,a0",
			result.Text);
		Assert.Matches(
			@"\tmovea?\.l\t#C68K_cstring_[A-Za-z0-9_]+,[ad][0-6]",
			result.Text);
		Assert.DoesNotMatch(
			@"\tjsr\t-30\(a6\)\r?\n\tmove\.l\td0,-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmove\.l\t\(a7\)\+,\d+\(a7\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tjsr\t-30\(a6\)\r?\n\tmove\.l\td0,-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmovea\.l\t4\(a7\),a0\r?\n\tmove\.l\t0\(a7\),d0\r?\n\taddq\.l\t#8,a7\r?\n\tbsr\.w\tC68K_method_",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmoveq\t#1,d0\r?\n\tmove\.l\td0,-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmovea\.l\t4\(a7\),a0\r?\n\tmove\.l\t0\(a7\),d0\r?\n\taddq\.l\t#8,a7\r?\n\tbsr\.w\tC68K_method_",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\ta0,-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmove\.l\td0,-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmove\.l\t\(a7\)\+,d0\r?\n\tmovea\.l\t\(a7\)\+,a0\r?\n\tmove\.l\td0,\(a0\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\td0,-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmove\.l\t0\(a7\),d0\r?\n\taddq\.l\t#4,a7\r?\n\trts",
			result.Text);
		Assert.DoesNotMatch(
			@"\tbsr\.w\tC68K_method_[A-Za-z0-9_]+(?:\r?\n(?:[A-Za-z0-9_]+:\r?\n)*)?\trts",
			result.Text);
		Assert.DoesNotMatch(
			@"\tbsr\.w\tC68K_method_[A-Za-z0-9_]+(?:\r?\n(?:[A-Za-z0-9_]+:\r?\n)*)?\tmove\.l\td0,\(a0\)\r?\n\trts",
			result.Text);
		Assert.Matches(@"\tmove\.l\t#\$804226E6,d[0-4]", result.Text);
		Assert.Matches(
			@"\t(?:movea\.l\ta7|lea\t[1-9][0-9]*\(a7\)),a1\r?\n\tjsr\tC68K_amiga_002Eboopsi_002EDoMethodA",
			result.Text);
		Assert.DoesNotContain(
			"\tlea\t0(a7),a1",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"\tbsr.w\tC68K_method_",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tjsr\tC68K_method_[A-Za-z0-9_]+",
			result.Text);
		Assert.Contains("\tmove.l\t(a0),d0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tbra\.w\t([A-Za-z0-9_]+)\r?\n\1:",
			result.Text);
		Assert.DoesNotMatch(
			@"(?m)^C68K_method_[A-Za-z0-9_]+_003AIL_[0-9A-F]{4}:\r?\nC68K_method_[A-Za-z0-9_]+_003AIL_[0-9A-F]{4}:",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t\d+\(a7\),-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmove\.l\t\(a7\)\+,d0",
			result.Text);
		Assert.Contains(
			"\tmove.l\td0,(a0)",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tdc.w\t$486F",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tdc.w\t$2080",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tdc.w\t$4298",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"\tmovea.l\t_MUIMasterLibraryBase(pc),a6",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmovea.l\t_MUIMasterLibraryBase,a6",
			result.Text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void CompilesIffInspectSampleWithTypedExceptionsAndCleanup()
	{
		var iffInspectAssembly = typeof(IFFInspect.Program).Assembly.Location;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = iffInspectAssembly,
			EntryPoint = "IFFInspect.Program::Main",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Application,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0001_0000,
				Size = 0x0000_8000
			}
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				["exec.library"] = 0x0000_0400
			}
		});
		Assert.NotEmpty(result.Code);
		Assert.Contains("IFFInspect.Program::Main", result.Map, StringComparison.Ordinal);
		Assert.Contains("IFFInspect.Program::Inspect", result.Map, StringComparison.Ordinal);
		Assert.Contains("__c68k_exception_raise:", result.Text, StringComparison.Ordinal);
		Assert.Contains("__c68k_exception_unwind_frame:", result.Text, StringComparison.Ordinal);
		Assert.Contains("__c68k_gc_mark_roots:", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("__c68k_eh_", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"C68K_string_003A70000019",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"C68K_cstring_003A70000019",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tclr.l\t_DOSLibraryBase",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tclr.l\t_IFFParseLibraryBase",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"\tmove.l\t_IFFParseLibraryBase(pc),d0",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"\tmove.l\t_DOSLibraryBase(pc),d0",
			result.Text,
			StringComparison.Ordinal);
		Assert.Matches(
			@"\tmove\.l\t_IFFParseLibraryBase\(pc\),d0\r?\n" +
			@"\tmovea\.l\td0,a1\r?\n" +
			@"\tmovea\.l\t_ExecBase\(pc\),a6\r?\n" +
			@"\tjsr\t-414\(a6\)",
			result.Text);
		Assert.Matches(
			@"\tmove\.l\t_DOSLibraryBase\(pc\),d0\r?\n" +
			@"\tmovea\.l\td0,a1\r?\n" +
			@"\tjsr\t-414\(a6\)",
			result.Text);
		Assert.Equal(
			2,
			result.TerminalDeadStoreStatistics.Sum(
				static statistics => statistics.Removed));
		Assert.DoesNotContain(
			"\tmove.l\t(a7),36(a7)",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmovea.l\t(a7),a0\r\n\tmove.l\ta0,36(a7)",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"\tmove.l\t#C68K_cstring_003A700000A1,d1",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\t#C68K_cstring_003A700000A1,d0\r\n\tmove.l\td0,d1",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tjsr\t-36\(a6\)\r?\n" +
			@"\tmove\.l\td0,(?<slot>\d+\(a7\))\r?\n" +
			@"\tmove\.l\t\k<slot>,(?<register>d[0-7])\r?\n" +
			@"\tcmp\.l\td0,\k<register>",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t\(a0\),d0\r?\n\tseq\td0",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.b\td0,\d+\(a7\)\r?\n\tmove\.b\t\d+\(a7\),d0",
			result.Text);
		Assert.DoesNotMatch(
			@"\ts(?:eq|ne|cc|cs|hi|ls|ge|lt|gt|le)\t(?<register>d[0-7])\r?\n" +
			@"\text\.w\t\k<register>\r?\n" +
			@"\text\.l\t\k<register>\r?\n" +
			@"\tneg\.l\t\k<register>",
			result.Text);
		Assert.DoesNotMatch(
			@"(?:\tandi\.l\t#\$000000FF,(?<register>d[0-7])\r?\n){2}" +
			@"\ttst\.b\t\k<register>",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmoveq\t#16,d0\r?\n\tmove\.l\td2,-\(a7\)\r?\n\tmove\.l\td0,d2",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmoveq\t#(?<offset>4|8|12),d0\r?\n" +
			@"(?:\tmovea\.l\t[ad][0-6],a0\r?\n)?" +
			@"\tmove\.l\t\k<offset>\(a0\),d0",
			result.Text);
		Assert.Matches(
			@"\tmovea\.l\td0,a0\r?\n" +
			@"(?:C68K_[^\r\n]+:\r?\n)*" +
			@"\tmove\.l\t8\(a0\),d[0-7]\r?\n" +
			@"\tmove\.l\t12\(a0\),d[0-7]",
			result.Text);
		Assert.DoesNotContain(
			"\tmove.l\td0,d4\r\n\tmovea.l\td4,a0",
			result.Text,
			StringComparison.Ordinal);
		Assert.Matches(
			@"\tmove\.l\t4\(a6\),d0\r?\n" +
			@"\tadd\.l\t12\(a1\),d0\r?\n" +
			@"\tmove\.l\td0,12\(a6\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t12\(a1\),d0\r?\n" +
			@"\tmovea\.l\td0,a0",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmovea\.l\ta[1-6],a0\r?\n" +
			@"\tmove\.[bwl]\t[^\r\n]*\(a0\)",
			result.Text);
		Assert.DoesNotMatch(
			"\\tlea\\t\\d+\\(a7\\),a[0-6]\\r?\\n" +
			"\\tmove\\.[bwl]\\t\\(a[0-6]\\),d[0-7]",
			result.Text);
		Assert.Contains("\tmove.l\t12(a7),d0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmove\.l\t[^\r\n]+,(?<register>d[0-7])\r?\n" +
			@"\ttst\.l\t\k<register>",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\td[0-7],d1\r?\n" +
			@"\tmove\.l\td1,\d+\(a[0-6]\)",
			result.Text);
		Assert.Contains(
			"\taddq.l\t#1,C68K_static_003ACopperSharp_002ERuntime_002EManaged_003A04000010",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"\tadd.l\td2,C68K_static_003ACopperSharp_002ERuntime_002EManaged_003A0400000F",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmove\.l\t(?<field>C68K_static_[^\r\n]+)\(pc\),(?<loaded>d[0-7])\r?\n" +
			@"(?:\tmoveq\t#[^\r\n]+,(?<delta>d[0-7])\r?\n)?" +
			@"\tadd\.l\t(?:\k<loaded>,\k<delta>|\k<delta>,\k<loaded>)\r?\n" +
			@"\tmove\.l\t(?:\k<loaded>|\k<delta>),\k<field>",
			result.Text);
		Assert.Contains(
			"\tmoveq\t#16,d0\r\n" +
			"\tadd.l\td0,d4\r\n" +
			"\tmove.l\td4,d3\r\n" +
			"\tsub.l\td0,d2",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\td4,d3\r\n\tadd.l\td0,d3\r\n\tmove.l\td3,d4",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmoveq\t#20,d0\r?\n" +
			@"\tbra\.s\tC68K_method_003A06000004_003AIL_[^\r\n]+\r?\n" +
			@"C68K_method_003A06000004_003ABB000D:\r?\n" +
			@"\tmove\.l\t20\(a7\),d0",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmoveq\t#20,d0\r?\n" +
			@"\tmove\.l\td0,20\(a7\)\r?\n" +
			@"C68K_method_003A06000004_003ABB000D:",
			result.Text);
		Assert.Contains(
			"\tmoveq\t#0,d1\r\n" +
			"\tmove.l\td1,12(a7)\r\n" +
			"\tmove.l\td1,16(a7)\r\n" +
			"\tmove.l\td1,20(a7)",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tclr.l\t12(a7)\r\n\tclr.l\t16(a7)",
			result.Text,
			StringComparison.Ordinal);

		Assert.DoesNotContain(
			"\tmove.l\td0,48(a7)\r\n\tmove.l\td0,44(a7)",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmovea.l\ta2,a0\r\n\tmove.l\td2,d0",
			result.Text,
			StringComparison.Ordinal);

		Assert.DoesNotContain(
			"\tmove.l\td0,d1\r\n\tmoveq\t#3,d0\r\n\tcmp.l\td0,d1",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"(?m)^\tb(?!ra)[a-z]+\.w\t[^\r\n]+\r?\n\tbra\.w\t",
			result.Text);
		Assert.Equal(0, CountBranchesToForwardingBlocks(result.Text!));
		var unconditionalBranches =
			result.Text!.Split("\tbra.w\t", StringSplitOptions.None).Length - 1;
		Assert.True(
			unconditionalBranches < 59,
			$"IFFInspect final-destination propagation emitted {unconditionalBranches} unconditional branches.");
	}

	[Fact]
	public void DosErrorsExposeCompleteTypedSdkSurface()
	{
		Assert.Equal(49, Enum.GetValues<global::Amiga.DOS.Error>().Length);
		Assert.Equal(3, Enum.GetValues<global::Amiga.DOS.FileMode>().Length);
		Assert.Equal(4, Enum.GetValues<global::Amiga.DOS.LockMode>().Length);
		Assert.Equal(103, (int)global::Amiga.DOS.Error.NoFreeStore);
		Assert.Equal(232, (int)global::Amiga.DOS.Error.NoMoreEntries);
		Assert.Equal(305, (int)global::Amiga.DOS.Error.NotExecutable);
		Assert.Equal(1004, (int)global::Amiga.DOS.FileMode.ReadWrite);
		Assert.Equal(1005, (int)global::Amiga.DOS.FileMode.OldFile);
		Assert.Equal(1006, (int)global::Amiga.DOS.FileMode.NewFile);
		Assert.Equal(-2, (int)global::Amiga.DOS.LockMode.Shared);
		Assert.Equal(-2, (int)global::Amiga.DOS.LockMode.Read);
		Assert.Equal(-1, (int)global::Amiga.DOS.LockMode.Exclusive);
		Assert.Equal(-1, (int)global::Amiga.DOS.LockMode.Write);

		var ioErr = typeof(global::Amiga.DOS).GetMethod(nameof(global::Amiga.DOS.IoErr));
		var setIoErr = typeof(global::Amiga.DOS).GetMethod(nameof(global::Amiga.DOS.SetIoErr));
		var open = typeof(global::Amiga.DOS).GetMethod(nameof(global::Amiga.DOS.Open));
		var lock_ = typeof(global::Amiga.DOS).GetMethod(nameof(global::Amiga.DOS.Lock));
		Assert.NotNull(ioErr);
		Assert.NotNull(setIoErr);
		Assert.NotNull(open);
		Assert.NotNull(lock_);
		Assert.Equal(typeof(global::Amiga.DOS.Error), ioErr.ReturnType);
		Assert.Equal(typeof(global::Amiga.DOS.Error), setIoErr.ReturnType);
		Assert.Equal(
			typeof(global::Amiga.DOS.Error),
			Assert.Single(setIoErr.GetParameters()).ParameterType);
		Assert.Equal(
			typeof(global::Amiga.DOS.FileMode),
			open.GetParameters()[1].ParameterType);
		Assert.Equal(
			typeof(global::Amiga.DOS.LockMode),
			lock_.GetParameters()[1].ParameterType);
	}

	[Fact]
	public void CompilesMuiTaskListSampleWithSubclassAndHooks()
	{
		var taskListAssembly = typeof(MUITaskList.Program).Assembly.Location;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = taskListAssembly,
			EntryPoint = "MUITaskList.Program::Main",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				["amiga.boopsi.DoMethodA"] = 0x0000_2600,
				["amiga.boopsi.DoSuperMethodA"] = 0x0000_2700
			}
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				["exec.library"] = 0x0000_0400,
				["intuition.library"] = 0x0000_3000,
				["muimaster.library"] = 0x0000_3800
			}
		});

		Assert.NotEmpty(result.Code);
		Assert.Contains("muitasklist.app.dispatcher", result.Map, StringComparison.Ordinal);
		Assert.Contains("muitasklist.list.display", result.Map, StringComparison.Ordinal);
		Assert.Matches(
			@"\tmovea?\.l\t#C68K_export_003Amuitasklist_002Eapp_002Edispatcher,[ad][0-6]",
			result.Text);
		Assert.Matches(
			@"\tmovea?\.l\t#C68K_export_003Amuitasklist_002Elist_002Edisplay,[ad][0-6]",
			result.Text);
		Assert.Contains(
			"\tjsr\tC68K_amiga_002Eboopsi_002EDoSuperMethodA",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains("\tmove.w\t32(a1),d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tadda.l\td0,a0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmovea.l\t#$00000000,a0",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"\tsuba.l\ta0,a0",
			result.Text,
			StringComparison.Ordinal);
		Assert.Matches(
			@"\tmovea?\.l\tC68K_static_003A04000003(?:\(pc\))?,[ad][0-7]\r\n(?:[^\r\n]*\r\n){0,2}\tbeq\.[sw]",
			result.Text);
		Assert.DoesNotContain(
			"\tmove.l\tC68K_static_003A04000003,-(a7)\r\n\tmove.l\t(a7)+,d0\r\n\ttst.l\td0",
			result.Text,
			StringComparison.Ordinal);
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
				"\tmovea.l\t$0004.w,a6\r\n\tmove.l\ta6,_ExecBase",
				StringSplitOptions.None).Length - 1);
		Assert.Equal(
			1,
			result.Text.Split(
				"\tmovea.l\t_ExecBase(pc),a6",
				StringSplitOptions.None).Length - 1);
		Assert.Contains("\tjsr\t-30(a6)", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ReusesA6AtBranchJoinWhenPredecessorsAgree(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint vectorAddress = execBase - 30;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallExecLibraryAfterMergedPaths",
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

		Assert.Equal(14u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(2, calls);
		Assert.Equal(
			2,
			result.Text!.Split(
				"\tmovea.l\t_ExecBase(pc),a6",
				StringSplitOptions.None).Length - 1);
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
			"\tmovea.l\t_DOSLibraryBase(pc),a6",
			assembly.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmovea.l\t_DOSLibraryBase,a6",
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
		Assert.Matches(@"\tj(?:sr|mp)\t-54\(a6\)", result.Text);

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
		Assert.Contains("\tmoveq\t#37,d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjsr\t-552(a6)", result.Text, StringComparison.Ordinal);
		Assert.Matches(@"\tjsr\t-552\(a6\)\r?\n\tmove\.l\td0,d[2-7]", result.Text);
		Assert.DoesNotMatch(@"\tclr\.l\t\d+\(a7\)", result.Text);
		Assert.DoesNotMatch(@"\tjsr\t-552\(a6\)\r?\n\tmove\.l\td0,\d+\(a7\)", result.Text);
		Assert.DoesNotContain("\tmove.l\t#$00001800,-(a7)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmovea.l\t4(a7),a1", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\td1,12(a7)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\ttst.l\td0\r\n\tsne\td0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tdc.w\t$56C0", result.Text, StringComparison.Ordinal);
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
		Assert.Contains("\tmove.l\t#$00001900,d1", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\t#$000003ED,d2", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\t#$00001900,-(a7)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\t#$000003ED,-(a7)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmovea.l\t_DOSLibraryBase(pc),a6", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjsr\t-30(a6)", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DecodesBptrToByteAddress(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::DecodeBptrAddress",
			Cpu = target
		});

		Assert.Equal(0x0000_0108u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LowersDosSeek64RegisterPairAbi(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint dosBase = 0x0000_3800;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallSdkDosSeek64",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});
		var slot = result.Symbols.Single(symbol =>
			symbol.Name == AmigaLibraryBaseSymbols.For("dos.library"));
		var bus = CreateHunkBus(result);
		bus.WriteLong(HunkLoadAddress + slot.Address, dosBase);
		bus.RegisterGateway(dosBase - 1066, state =>
		{
			Assert.Equal(0x0000_0042u, state.D[1]);
			Assert.Equal(0x1122_3344u, state.D[2]);
			Assert.Equal(0x5566_7788u, state.D[3]);
			Assert.Equal(0xFFFF_FFFFu, state.D[4]);
			state.D[0] = 0x99AA_BBCC;
			state.D[1] = 0xDDEE_F001;
		});

		var high = Execute(
			bus,
			model,
			HunkLoadAddress + result.EntryPoint,
			afterReturn: state => Assert.Equal(0xDDEE_F001u, state.D[1]));
		Assert.Equal(0x99AA_BBCCu, high);
		Assert.True(
			result.Text!.Contains("\tjsr\t-1066(a6)", StringComparison.Ordinal) ||
			result.Text!.Contains("\tjmp\t-1066(a6)", StringComparison.Ordinal));
	}

	[Fact]
	public void LowersDosLockRecord64RegisterPairArguments()
	{
		const uint dosBase = 0x0000_3800;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallSdkDosLockRecord64",
			OutputFormat = M68kOutputFormat.Assembly
		});
		var slot = result.Symbols.Single(symbol =>
			symbol.Name == AmigaLibraryBaseSymbols.For("dos.library"));
		var bus = CreateHunkBus(result);
		bus.WriteLong(HunkLoadAddress + slot.Address, dosBase);
		bus.RegisterGateway(dosBase - 1078, state =>
		{
			Assert.Equal(0x0000_0042u, state.D[1]);
			Assert.Equal(0x1122_3344u, state.D[2]);
			Assert.Equal(0x5566_7788u, state.D[3]);
			Assert.Equal(0x99AA_BBCCu, state.D[4]);
			Assert.Equal(0xDDEE_F001u, state.D[5]);
			Assert.Equal(3u, state.D[6]);
			Assert.Equal(4u, state.D[7]);
			state.D[0] = 1;
		});

		Assert.Equal(1u, Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.True(
			result.Text!.Contains("\tjsr\t-1078(a6)", StringComparison.Ordinal) ||
			result.Text!.Contains("\tjmp\t-1078(a6)", StringComparison.Ordinal));
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

		Assert.Equal(M68kDiagnosticIds.StaticAnalysis, exception.DiagnosticId);
		Assert.Contains("managed heap", exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StaticTypeInitializerRunsExactlyOnce(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.TypeInitializationRuntimeFixtures::OnceOnlyEntry");

		Assert.Equal(83u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void RecursiveStaticTypeInitializationObservesInProgressValues(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.TypeInitializationRuntimeFixtures::RecursiveEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void FailedStaticTypeInitializationIsCachedAndRethrown(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.TypeInitializationRuntimeFixtures::FailureEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Fact]
	public void StaticAnalyzerRejectsRuntimeGcCallsWithoutManagedRuntime()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ExplicitCollectEntry"
			}));

		Assert.Equal(M68kDiagnosticIds.StaticAnalysis, exception.DiagnosticId);
		Assert.Contains("runtime GC operation", exception.Message, StringComparison.Ordinal);
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
	public void AmigaManagedPoolOwnsExecAllocatedArenaWithoutMemfClear()
	{
		const uint execBase = 0x0000_3000;
		const uint arena = 0x0000_4000;
		const uint arenaSize = 0x0000_2000;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0xDEAD_0000,
				Size = arenaSize
			}
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocated = false;
		var freed = false;
		bus.RegisterGateway(execBase - 198, state =>
		{
			Assert.Equal(arenaSize, state.D[0]);
			Assert.Equal(0u, state.D[1]);
			allocated = true;
			state.D[0] = arena;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.Equal(arena, state.A[1]);
			Assert.Equal(arenaSize, state.D[0]);
			freed = true;
		});

		Assert.Equal(
			26u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.True(allocated);
		Assert.True(freed);
		var assembly = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry",
			OutputFormat = M68kOutputFormat.Assembly,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0xDEAD_0000,
				Size = arenaSize
			}
		});
		Assert.Equal(
			2,
			assembly.Text!.Split(
				"\tmovea.l\t$0004.w,a6\r\n\tmove.l\ta6,_ExecBase",
				StringSplitOptions.None).Length - 1);
		Assert.Equal(
			1,
			assembly.Text.Split(
				"\tmovea.l\t_ExecBase(pc),a6",
				StringSplitOptions.None).Length - 1);
		Assert.Contains(
			"\tmove.l\td0,C68K_runtime_003Agc_002Darena_002Dbase",
			assembly.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\td0,-(a7)\r\n\tmove.l\t(a7)+,C68K_runtime_003Agc_002Darena_002Dbase",
			assembly.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmovea.l\t$0004.w,a6\r\n\tjsr\t-210(a6)",
			assembly.Text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void AmigaRequesterExitReleasesManagedPoolArena()
	{
		const uint execBase = 0x0000_3000;
		const uint intuitionBase = 0x0000_3800;
		const uint arena = 0x0000_4000;
		const uint arenaSize = 0x0000_2000;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::UnhandledExceptionEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions { Size = arenaSize }
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var freed = false;
		bus.RegisterGateway(execBase - 198, state => state.D[0] = arena);
		bus.RegisterGateway(execBase - 552, state => state.D[0] = intuitionBase);
		bus.RegisterGateway(intuitionBase - 348, state => state.D[0] = 1);
		bus.RegisterGateway(execBase - 414, _ => { });
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.Equal(arena, state.A[1]);
			Assert.Equal(arenaSize, state.D[0]);
			freed = true;
		});

		Assert.Equal(
			20u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.True(freed);
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

		Assert.Contains("\tbsr.w\t__c68k_gc_init", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tbsr.w\t__c68k_gc_shutdown", result.Text, StringComparison.Ordinal);
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
		Assert.Contains("__c68k_gc_collect_with_roots:", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tbsr.w\t__c68k_gc_collect_with_roots", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tbsr.w\t__c68k_gc_collect", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tbra.w\t__c68k_gc_coalesce", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tbsr.w\t__c68k_gc_coalesce\r\n\trts",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::Allocate");
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::Mark");
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::Collect");
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::Coalesce");
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::CollectWithRoots");
		Assert.DoesNotMatch(
			@"\tclr\.l\t(?:\d+\(a[0-6]\)|\(a[0-6]\)\+?|\-\(a[0-6]\))",
			result.Text);
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
		var firstAllocIndex = text.IndexOf("\tbsr.w\t__c68k_alloc", StringComparison.Ordinal);
		var collectIndex = text.IndexOf("\tbsr.w\t__c68k_gc_collect", StringComparison.Ordinal);
		var retryIndex = text.IndexOf(
			"\tbsr.w\t__c68k_alloc",
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

	[Fact]
	public void ManagedPoolRequiresManagedRuntimeAssembly()
	{
		var missingRuntime = Path.Combine(
			Path.GetTempPath(),
			Guid.NewGuid().ToString("N"),
			"CopperSharp.Runtime.Managed.dll");
		var exception = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry",
				ManagedAssemblyPaths = new[] { missingRuntime },
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			}));

		Assert.Equal(M68kDiagnosticIds.InvalidInput, exception.DiagnosticId);
		Assert.Contains("requires", exception.Message, StringComparison.Ordinal);
		Assert.Contains("CopperSharp.Runtime.Managed.dll", exception.Message, StringComparison.Ordinal);
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
			var size = state.D[0];
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
			var size = state.D[0];
			state.D[0] = heap;
			heap += (size + 3) & ~3u;
		});

		Assert.Equal(10u, Execute(bus, M68kCpuModel.M68040, HunkLoadAddress + result.EntryPoint));
	}

	[Fact]
	public void ZeroManagedFieldsReuseZeroRegistersOnM68000()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ZeroManagedStoresEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});

		Assert.Matches(@"\tmove\.l\td[0-7],12\(a[0-6]\)", result.Text);
		Assert.DoesNotMatch(@"\tclr\.l\t12\(a[0-6]\)", result.Text);
		Assert.Matches(@"\tmove\.l\td[0-7],C68K_static_", result.Text);
		Assert.DoesNotMatch(@"\tclr\.l\tC68K_static_", result.Text);

		var m68020 = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ZeroManagedStoresEntry",
			Cpu = M68kCpuTarget.M68020,
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});
		var m68020Always = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ZeroManagedStoresEntry",
			Cpu = M68kCpuTarget.M68020,
			ClrPolicy = M68kClrPolicy.Always,
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});
		Assert.Matches(@"\tclr\.l\t\d+\(a[0-6]\)", m68020.Text);
		Assert.Matches(@"\tclr\.l\tC68K_static_", m68020.Text);
		Assert.Matches(@"\tclr\.l\t\d+\(a[0-6]\)", m68020Always.Text);
		Assert.Matches(@"\tclr\.l\tC68K_static_", m68020Always.Text);
	}

	[Fact]
	public void ZeroManagedStoreClrPolicyHasMeasuredM68000Cost()
	{
		const string entry =
			"CopperSharp.Compiler.Tests.CompilerFixtures::ZeroManagedStoresEntry";
		var moveResult = CompileWithAllocator(
			M68kCpuTarget.M68000,
			entry,
			M68kClrPolicy.Auto);
		var clrResult = CompileWithAllocator(
			M68kCpuTarget.M68000,
			entry,
			M68kClrPolicy.Always);

		var moveCycles = MeasureCyclesWithAllocator(moveResult);
		var clrCycles = MeasureCyclesWithAllocator(clrResult);

		Assert.True(
			moveCycles + 8 <= clrCycles,
			$"MOVE-based zero stores took {moveCycles} cycles; " +
			$"CLR-based stores took {clrCycles} cycles.");
	}

	[Fact]
	public void CompilesObjectConstructionWithArguments()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ConstructorArgumentsEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_1000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void HybridAbiSupportsWideConstructorsAndInterleavedOverflow(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
			{
				"WideConstructorArgumentsEntry",
				"HybridMixedArgumentsEntry",
				"HybridManagedPointerArgumentsEntry"
			})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");

			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(ManagedByrefSafepointCases))]
	public void ManagedByrefProvenanceKeepsRequiredOwnersAlive(
		string entry,
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		var actual = ExecuteHunk(result, model);
		Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
	}

	[Fact]
	public void IncompatibleOwnerByrefAtSafepointHasStableDiagnostic()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint =
					"CopperSharp.Compiler.Tests.CompilerFixtures::IncompatibleOwnerByrefMergeEntry",
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			}));

		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, exception.DiagnosticId);
		Assert.Contains("transported owner root", exception.Message, StringComparison.Ordinal);
		Assert.Contains("Unknown", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ManagedByrefReturnRequiresStableLifetimeSummaryDiagnostic()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint =
					"CopperSharp.Compiler.Tests.CompilerFixtures::UnsupportedBorrowedByrefReturnEntry",
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			}));

		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, exception.DiagnosticId);
		Assert.True(
			exception.Message.Contains("Managed byref return", StringComparison.Ordinal),
			exception.Message);
		Assert.True(
			exception.Message.Contains("return-owner/lifetime summary", StringComparison.Ordinal),
			exception.Message);
	}

	[Theory]
	[InlineData("ManagedByrefStaticEscapeTemplateEntry", "IgnoreIntReference", "_managedByrefStaticEscapeSink", true, "stsfld")]
	[InlineData("ManagedByrefHeapEscapeTemplateEntry", "IgnoreObjectAndIntReference", "ByrefEscapeSink", false, "stfld")]
	public void ManagedByrefStorageEscapeHasStableDiagnostic(
		string entry,
		string placeholderMethod,
		string targetField,
		bool isStatic,
		string expectedOpcode)
	{
		var assemblyPath = CreateManagedByrefEscapeFixtureAssembly(
			entry,
			placeholderMethod,
			targetField,
			isStatic);
		try
		{
			var exception = Assert.Throws<M68kCompilationException>(() =>
				M68kCompiler.Compile(new M68kCompilationRequest
				{
					AssemblyPath = assemblyPath,
					EntryPoint =
						$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
					MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
					Heap = new M68kHeapOptions
					{
						StartAddress = 0x0000_4000,
						Size = 0x0000_2000
					}
				}));

			Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, exception.DiagnosticId);
			Assert.Contains("Managed byref", exception.Message, StringComparison.Ordinal);
			Assert.Contains("cannot escape", exception.Message, StringComparison.Ordinal);
			Assert.Contains(expectedOpcode, exception.Message, StringComparison.Ordinal);
		}
		finally
		{
			File.Delete(assemblyPath);
		}
	}

	[Fact]
	public void ReadonlyManagedByrefWriteHasStableDiagnostic()
	{
		var assemblyPath = CreateReadonlyByrefWriteFixtureAssembly();
		try
		{
			var exception = Assert.Throws<M68kCompilationException>(() =>
				M68kCompiler.Compile(new M68kCompilationRequest
				{
					AssemblyPath = assemblyPath,
					EntryPoint =
						"CopperSharp.Compiler.Tests.CompilerFixtures::ReadonlyByrefWriteTemplateEntry"
				}));

			Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, exception.DiagnosticId);
			Assert.Contains("readonly managed byref", exception.Message, StringComparison.Ordinal);
			Assert.Contains("stind.i4", exception.Message, StringComparison.Ordinal);
		}
		finally
		{
			File.Delete(assemblyPath);
		}
	}

	[Fact]
	public void IncompatibleManagedByrefReferentMergeHasStableDiagnostic()
	{
		var assemblyPath = CreateIncompatibleByrefTypeFixtureAssembly();
		try
		{
			var exception = Assert.Throws<M68kCompilationException>(() =>
				M68kCompiler.Compile(new M68kCompilationRequest
				{
					AssemblyPath = assemblyPath,
					EntryPoint =
						"CopperSharp.Compiler.Tests.CompilerFixtures::IncompatibleByrefTypeTemplateEntry"
				}));

			Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, exception.DiagnosticId);
			Assert.Contains("incompatible referent types", exception.Message, StringComparison.Ordinal);
			Assert.Contains("int", exception.Message, StringComparison.Ordinal);
			Assert.Contains("uint", exception.Message, StringComparison.Ordinal);
		}
		finally
		{
			File.Delete(assemblyPath);
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ReferenceBearingAggregateHomeReportsPreciseRootWords(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ReferenceBearingAggregateHomeAcrossCollectionEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxInteriorByrefRetainsBoxAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var assemblyPath = CreateBoxInteriorByrefFixtureAssembly();
		try
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = assemblyPath,
				EntryPoint =
					"CopperSharp.Compiler.Tests.CompilerFixtures::BoxInteriorByrefTemplateEntry",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			});

			Assert.Equal(42u, ExecuteHunk(result, model));
		}
		finally
		{
			File.Delete(assemblyPath);
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void InheritedObjectLayoutPreservesBaseAndReferenceFields(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint allocatorAddress = 0x0000_2800;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::InheritedObjectLayoutEntry",
			Cpu = target,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			}
		});
		var bus = CreateHunkBus(result);
		var allocations = new List<(uint Address, uint Size)>();
		var heap = 0x0000_4000u;
		bus.RegisterGateway(allocatorAddress, state =>
		{
			var size = state.D[0];
			var address = heap;
			heap += (size + 3) & ~3u;
			Array.Clear(bus.Memory, (int)address, (int)size);
			allocations.Add((address, size));
			state.D[0] = address;
		});

		Assert.Equal(42u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(
			new[] { 24u, 12u, 12u },
			allocations.Select(static allocation => allocation.Size));

		var descriptor = bus.ReadLong(allocations[0].Address);
		Assert.Equal(24u, bus.ReadLong(descriptor));
		Assert.Equal(0x0000_0006u, bus.ReadLong(descriptor + 4));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SealedClassInstanceMethodIsCompiledAsADirectCall(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SealedDirectCallEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ExplicitBaseMethodCallIsCompiledDirectly(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ExplicitBaseCallEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void VirtualDispatchUsesRuntimeObjectType(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::VirtualDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[InlineData("VirtualBaseDispatchEntry")]
	[InlineData("VirtualArgumentDispatchEntry")]
	[InlineData("MultiSlotVirtualDispatchEntry")]
	[InlineData("AbstractVirtualDispatchEntry")]
	public void VirtualDispatchPreservesArgumentsSlotsAndAbstractContracts(string entryPoint)
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{entryPoint}");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void VirtualDispatchEmitsIndirectJsrThroughDescriptorVtable()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::VirtualDispatchEntry",
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});

		Assert.Contains("\tjsr\t(a2)", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void NullVirtualReceiverRaisesNullReferenceBeforeVtableLoad()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NullVirtualDispatchEntry");

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void InterfaceDispatchUsesRuntimeInterfaceMap(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::InterfaceDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void RuntimeClassAndInterfaceTypeTestsUseObjectDescriptors(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"RuntimeClassTypeTestEntry",
			"RuntimeInterfaceTypeTestEntry",
			"RuntimeArrayTypeTestEntry"
		})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CastClassReturnsCompatibleObjectAndRaisesInvalidCast(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::RuntimeCastClassEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ReferenceArrayStoresValidateRuntimeElementIdentity(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"ReferenceArrayStoreTypeCheckEntry",
			"InterfaceArrayStoreTypeCheckEntry"
		})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ClosedGenericConstructionsHaveDistinctRuntimeIdentity(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericTypeIdentityEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstructedGenericOwnersExposeTypeIndependentInstanceFields(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericInstanceFieldEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstructedGenericDependentFieldsUseSpecializedLayouts(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericDependentFieldTemplateEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstructedGenericStaticFieldsHaveDistinctStorageAndRoots(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericStaticFieldEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstructedGenericStaticInitializersHaveDistinctMethodState(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericStaticInitializerTemplateEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void GenericMethodsAreSpecializedPerConstruction(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericMethodSpecializationEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstructedGenericFieldsSubstituteCompoundTypes(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericCompoundFieldEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void GenericMethodsOnConstructedOwnersUseBothConstructionKeys(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedOwnerGenericMethodEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ClosedGenericInterfacesKeepDistinctInterfaceMaps(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericInterfaceDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstructedGenericImplementersSpecializeInterfaceMapsAndMethods(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericImplementerDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ExplicitConstructedGenericInterfaceMethodsUseExactBodies(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ExplicitConstructedGenericInterfaceDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void InheritedConstructedGenericInterfacesKeepBaseConstruction(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::InheritedConstructedGenericInterfaceDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstructedGenericInterfaceInheritancePreservesParentConstruction(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericInterfaceInheritanceEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CovariantGenericInterfacesReuseClosedImplementerMethodTables(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CovariantGenericInterfaceDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void RuntimeCastsHonorCovariantGenericInterfaceConversions(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CovariantGenericInterfaceCastEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ContravariantGenericInterfacesReuseClosedImplementerMethodTables(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ContravariantGenericInterfaceDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MixedVarianceGenericInterfacesApplyEachParameterDirection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MixedVarianceGenericInterfaceDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CovarianceComposesWithConstructedInterfaceInheritance(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::InheritedCovariantGenericInterfaceDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CovariantGenericInterfacesRejectTheReverseDirection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::InvalidCovariantDirectionTypeTestEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void VariantGenericParametersRemainInvariantForValueTypes(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ValueTypeVarianceRemainsInvariantEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstructedGenericVirtualDeclarationsKeepDistinctVtables(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericVirtualDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstructedGenericOverridesSpecializeVtablesAndMethods(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericVirtualOverrideEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MultiHopPermutedGenericVirtualOverridesRemainReachable(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MultiHopPermutedGenericVirtualOverrideEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ClosedLeavesRetainOverridesThroughConstructedGenericBases(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ClosedMultiHopGenericVirtualOverrideEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MultiHopPermutedGenericInterfacesResolveReachableConstructions(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MultiHopPermutedGenericInterfaceEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstructedGenericBaseLayoutsInheritSpecializedFieldsAndRoots(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ConstructedGenericBaseLayoutEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstrainedGenericValueTypesDispatchWithoutBoxing(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstrainedGenericValueTypeDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StatefulConstrainedGenericValueTypesPreserveReceiverPayload(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::StatefulConstrainedGenericValueTypeDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstrainedGenericReferencesUseDynamicInterfaceDispatch(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstrainedGenericReferenceInterfaceDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstrainedGenericReferencesUseDynamicVirtualDispatch(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstrainedGenericReferenceVirtualDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstrainedGenericObjectVirtualsUseOverrides(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstrainedGenericObjectVirtualDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstrainedGenericObjectVirtualsUseShadowFallbacks(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstrainedGenericObjectVirtualFallbackEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ObjectGetHashCodeUsesShadowFallbacks(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ObjectGetHashCodeFallbackEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ObjectGetHashCodeUsesRuntimeOverrides(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"ObjectGetHashCodeOverrideEntry",
			"ObjectGetHashCodeBaseTypedOverrideEntry"
		})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");

			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ObjectEqualsUsesReferenceEqualityFallback(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ObjectEqualsFallbackEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ObjectEqualsUsesRuntimeOverrides(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"ObjectEqualsOverrideEntry",
			"ObjectEqualsBaseTypedOverrideEntry",
			"ConstrainedGenericObjectEqualsOverrideEntry"
		})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");

			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StaticObjectEqualsHandlesNullsOverridesAndDelegates(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"StaticObjectEqualsEntry",
			"StaticObjectEqualsDelegateEntry"
		})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");

			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ObjectReferenceEqualsUsesRawManagedIdentity(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"ObjectReferenceEqualsEntry",
			"DelegateReferenceEqualsEntry"
		})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");

			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ExplicitCilObjectReferenceEqualsUsesIntrinsicComparison(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var assemblyPath = RawCilFixtureBuilder.CreateObjectReferenceEqualsAssembly(
			Path.GetDirectoryName(FixtureAssembly)!);
		try
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = assemblyPath,
				EntryPoint = "RawReferenceEquals::Entry",
				Cpu = target
			});

			Assert.Equal(1u, ExecuteHunk(result, model));
		}
		finally
		{
			File.Delete(assemblyPath);
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstrainedObjectEqualsUsesReferenceEqualityFallback(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstrainedGenericObjectEqualsFallbackEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void NullConstrainedObjectEqualsThrowsNullReferenceException(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NullConstrainedGenericObjectEqualsEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void NullConstrainedGenericObjectVirtualsThrowNullReferenceException(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NullConstrainedGenericObjectVirtualDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void NullConstrainedGenericReferencesThrowNullReferenceException(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NullConstrainedGenericReferenceDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanArrayLengthAndIndexerUseAllocationFreeByrefLikePair(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SpanArrayLengthAndIndexerEntry");

		Assert.Equal(44u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanArrayOwnerRemainsRootedAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::SpanArrayOwnerSurvivesCollectionEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(44u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(SpanByrefConstructorCases))]
	public void SpanByrefConstructorsTransportExactOwnerWithoutAllocation(
		string entry,
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Fact]
	public void SpanFromCallerBorrowedRefHasStableOwnerTransportDiagnostic()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			Compile(
				M68kCpuTarget.M68000,
				M68kOutputFormat.Hunk,
				"CopperSharp.Compiler.Tests.CompilerFixtures::UnsupportedSpanFromBorrowedRefEntry"));

		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, exception.DiagnosticId);
		Assert.Contains("CallerBorrowed", exception.Message, StringComparison.Ordinal);
		Assert.Contains("GC owner", exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanIsEmptyUsesLengthWithoutAllocationOrHelper(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SpanIsEmptyEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DefaultSpanClearsPayloadAndOwnerBeforeCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::SpanDefaultAcrossCollectionEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanSlicePreservesOwnerAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::SpanSliceOwnerSurvivesCollectionEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(89u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void WideSpanElementsUseExactLayoutScaling(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::WideSpanExactLayoutEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanSliceBoundsUseArgumentOutOfRangeException(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SpanSliceBoundsEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ReadOnlySpanArrayAndSlicePreserveOwnerAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ReadOnlySpanArraySliceOwnerSurvivesCollectionEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(89u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanToReadOnlySpanConversionPreservesOwnerAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ReadOnlySpanFromSpanOwnerSurvivesCollectionEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(44u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringToReadOnlySpanUsesUtf16PayloadAndNullSemantics(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ReadOnlySpanFromStringEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringCharIndexerUsesCheckedUtf16CodeUnits(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"StringCharIndexerEntry",
			"StringCharIndexerExceptionEntry"
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_3000
				}
			});

			var actual = ExecuteHunk(result, model);
			Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringOrdinalEqualityUsesAllocationFreeUtf16Comparison(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::StringOrdinalEqualityEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringConcatUsesCheckedUtf16AllocationAndPreservesGcRoots(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"StringConcatEntry",
			"StringConcatAllocatedEntry",
			"StringConcatNullFastPathsEntry",
			"StringConcatSurvivesCollectionEntry"
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_3000
				}
			});

			var actual = ExecuteHunk(result, model);
			Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
		}
	}

	[Fact]
	public void StringSubstringSemanticFixturesMatchHostNet10()
	{
		Assert.Equal(42, CompilerFixtures.StringSubstringEntry());
		Assert.Equal(42, CompilerFixtures.StringSubstringAllocatedEntry());
		Assert.Equal(42, CompilerFixtures.StringSubstringExceptionEntry());
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringSubstringUsesOneCheckedUtf16AllocationAndPreservesGcRoots(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"StringSubstringEntry",
			"StringSubstringAllocatedEntry",
			"StringSubstringExceptionEntry",
			"StringSubstringSurvivesCollectionEntry"
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_3000
				}
			});

			var actual = ExecuteHunk(result, model);
			Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
		}
	}

	[Fact]
	public void StringCopyAndEnumerationFixturesMatchHostNet10()
	{
		Assert.Equal(42, CompilerFixtures.StringCopyToEntry());
		Assert.Equal(42, CompilerFixtures.StringCopyToExceptionEntry());
		Assert.Equal(42, CompilerFixtures.StringCopyToSpanEntry());
		Assert.Equal(42, CompilerFixtures.StringCopyToSpanExceptionEntry());
		Assert.Equal(42, CompilerFixtures.StringToCharArrayEntry());
		Assert.Equal(42, CompilerFixtures.StringToCharArrayAllocatedEntry());
		Assert.Equal(42, CompilerFixtures.StringToCharArrayExceptionEntry());
		Assert.Equal(42, CompilerFixtures.StringEnumerationEntry());
		Assert.Equal(42, CompilerFixtures.StringEnumerationNullEntry());
	}

	[Fact]
	public void ParameterlessIntegerFormattingFixtureMatchesHostNet10() =>
		Assert.Multiple(
			() => Assert.Equal(42, CompilerFixtures.IntegerToStringEntry()),
			() => Assert.Equal(42, CompilerFixtures.IntegerToStringBoundaryEntry()));

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ParameterlessIntegerFormattingUsesInvariantDecimalShadowRuntime(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"IntegerToStringEntry",
			"IntegerToStringBoundaryEntry"
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint =
					$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_3000
				}
			});

			Assert.Equal(42u, ExecuteHunk(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringCopyAndEnumerationUseUtf16ContractsAndPreserveGcRoots(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"StringCopyToEntry",
			"StringCopyToExceptionEntry",
			"StringCopyToSpanEntry",
			"StringCopyToSpanExceptionEntry",
			"StringToCharArrayEntry",
			"StringToCharArrayAllocatedEntry",
			"StringToCharArrayExceptionEntry",
			"StringToCharArraySurvivesCollectionEntry",
			"StringEnumerationEntry",
			"StringEnumerationNullEntry"
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_3000
				}
			});

			var actual = ExecuteHunk(result, model);
			Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringOrdinalSearchUsesAllocationFreeUtf16Loops(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"StringOrdinalSearchEntry",
			"StringOrdinalSearchNullEntry",
			"StringNonOrdinalComparisonRejectedEntry"
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_3000
				}
			});

			var actual = ExecuteHunk(result, model);
			Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
		}
	}

	[Theory]
	[InlineData("StringCharIndexerEntry", 2156, 1588, 1004L)]
	[InlineData("StringCharIndexerExceptionEntry", 1992, 1440, 4094L)]
	[InlineData("StringOrdinalEqualityEntry", 1584, 1302, 3058L)]
	[InlineData("StringConcatAllocatedEntry", 2260, 1706, 1990L)]
	[InlineData("StringSubstringAllocatedEntry", 2344, 1756, 1520L)]
	[InlineData("StringCopyToEntry", 3180, 2450, 1908L)]
	[InlineData("StringCopyToSpanEntry", 2408, 1814, 1528L)]
	[InlineData("IntegerToStringEntry", 5468, 4138, 44388L)]
	[InlineData("StringToCharArrayAllocatedEntry", 2272, 1680, 1180L)]
	[InlineData("StringEnumerationEntry", 1596, 1156, 2558L)]
	[InlineData("StringOrdinalSearchEntry", 2776, 2380, 10378L)]
	public void StringPrimitiveMetricsStayWithinInitialMc68000Budgets(
		string entry,
		int imageBudget,
		int codeBudget,
		long cycleBudget)
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			var size = state.D[0];
			state.D[0] = heap;
			heap += (size + 3) & ~3u;
		});
		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		Assert.True(
			result.Image.Length <= imageBudget,
			$"{entry}: image={result.Image.Length}, code={result.Code.Length}, cycles={cycles}.");
		Assert.True(
			result.Code.Length <= codeBudget,
			$"{entry} code grew from {codeBudget} to {result.Code.Length} bytes.");
		Assert.Equal(
			entry == "IntegerToStringEntry" ? 10 : 2,
			result.AllocationStatistics.Count);
		Assert.Equal(
			entry == "IntegerToStringEntry" ? 8 : 0,
			result.AllocationStatistics.Max(item => item.SpillFrameBytes));
		Assert.True(
			cycles <= cycleBudget,
			$"{entry} grew from {cycleBudget} to {cycles} MC68000 cycles.");
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ReadOnlySpanCharSequenceEqualUsesDirectUtf16Comparison(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ReadOnlySpanCharSequenceEqualEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DynamicStringReadOnlySpanPreservesOwnerAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::DynamicStringReadOnlySpanOwnerSurvivesCollectionEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DynamicStringLengthValidationUsesStablePublicExceptions(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::DynamicStringLengthValidationEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ReadOnlySpanSliceBoundsUseArgumentOutOfRangeException(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ReadOnlySpanSliceBoundsEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[InlineData("UnsupportedSpanEntryPoint", "return type")]
	[InlineData("UnsupportedReadOnlySpanEntryPoint", "return type")]
	[InlineData("UnsupportedImportedReadOnlySpanParameterEntry", "parameter")]
	public void SpanLikeSignatureBoundaryHasStableDiagnostic(string entry, string role)
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			Compile(
				M68kCpuTarget.M68000,
				M68kOutputFormat.Hunk,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}"));

		Assert.Equal(M68kDiagnosticIds.UnsupportedSignature, exception.DiagnosticId);
		Assert.Contains($"Unsupported {role}", exception.Message, StringComparison.Ordinal);
		Assert.Contains("Span`1<int>", exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanLikeReturnsTransportOwnerThroughHiddenBuffer(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"SpanReturnOwnerSurvivesCollectionEntry",
			"ReadOnlySpanReturnOwnerSurvivesCollectionEntry",
			"SpanParameterReturnOwnerSurvivesCollectionEntry"
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_3000
				}
			});

			Assert.Equal(42u, ExecuteHunk(result, model));
		}
	}

	[Fact]
	public void SpanLikeReturnHiddenBufferInitializesAndCopiesOwnerWord()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SpanReturnOwnerSurvivesCollectionEntry");

		Assert.Matches(@"\tclr\.l\t\d+\(a7\)", assembly.Text);
		Assert.Matches(
			@"\tmove\.l\t8\(a[0-6]\),d0\r?\n\tmove\.l\td0,8\(a[0-6]\)",
			assembly.Text);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ReadOnlySpanParameterPreservesOwnerAcrossNestedManagedCalls(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ReadOnlySpanParameterOwnerSurvivesCollectionEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanParameterPreservesOwnerAndWritesAcrossNestedManagedCalls(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::SpanParameterOwnerSurvivesCollectionEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstantStackallocSpanUsesFrameStorageAcrossNestedManagedCalls(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ConstantStackallocSpanEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MultipleConstantStackallocSpansPreserveAlignmentAndZeroLength(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MultipleConstantStackallocSpanEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanCopyToUsesOverlapSafeNativeWidthLoops(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"SpanByteCopyToEntry",
			"ReadOnlySpanIntCopyToEntry"
		})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");

			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanCopyToShortDestinationRaisesArgumentException(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SpanCopyToShortDestinationEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Fact]
	public void SpanFloatCopyToUsesNativeLongWidthLoop()
	{
		var assembly = Compile(
			M68kCpuTarget.M68040,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SpanFloatCopyToEntry");
		Assert.Contains("\tmove.l\t(a2),d0", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\td0,(a1)", assembly.Text, StringComparison.Ordinal);

		var result = CompileWithAllocator(
			M68kCpuTarget.M68040,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SpanFloatCopyToEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, M68kCpuModel.M68040));
	}

	[Fact]
	public void SpanFloatIndexersRoundTripNativeSingleValues()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68040,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SpanFloatElementAccessEntry",
			floatingPoint: M68kFloatingPointMode.M68040);

		Assert.Equal(
			unchecked((uint)BitConverter.SingleToInt32Bits(3.75f)),
			ExecuteHunkWithAllocator(result, M68kCpuModel.M68040));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanLongIndexersRoundTripHighNativeWord(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SpanLongElementAccessEntry");

		Assert.Equal(0x11223344u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpanLongIndexersRoundTripLowNativeWord(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SpanLongLowWordElementAccessEntry");

		Assert.Equal(0x55667788u, ExecuteHunkWithAllocator(result, model));
	}

	[Fact]
	public void SpanLongIndexersExpandToAdjacentNativeLongTransfers()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SpanLongElementAccessEntry");

		Assert.Matches(
			@"\tmove\.l\td[0-7],\(a([0-6])\)\r?\n\tmove\.l\td[0-7],4\(a\1\)",
			assembly.Text);
		Assert.Matches(
			@"\tmove\.l\t\(a([0-6])\),d[0-7]\r?\n\tmove\.l\t4\(a\1\),d[0-7]",
			assembly.Text);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DynamicStackallocUsesRuntimeSizedAnchoredFrame(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DynamicStackallocSpanCallerEntry");

		Assert.Equal(3u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DynamicStackallocSurvivesNestedCallsAndRestoresTheStack(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DynamicStackallocNestedCallEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DynamicStackallocNegativeCountRaisesOverflowException(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DynamicStackallocNegativeCountEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DynamicStackallocExceptionHandlerRetainsAnchoredLocals(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DynamicStackallocExceptionUnwindEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DynamicStackallocFrameRemainsGcWalkable(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::DynamicStackallocGcEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_3000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Fact]
	public void DynamicStackallocPaysForFrameAnchoringOnlyInDynamicMethods()
	{
		var dynamicAssembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DynamicStackallocSpanCallerEntry");
		var constantAssembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MultipleConstantStackallocSpanEntry");

		Assert.Contains("movea.l\ta7,a5", dynamicAssembly.Text, StringComparison.Ordinal);
		Assert.Contains("movea.l\ta5,a7", dynamicAssembly.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\ta7,a5", constantAssembly.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxedScalarsHaveDistinctDescriptorsAndCheckedUnboxing(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::BoxedScalarTypeIdentityEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxedInt64ValuesPreserveBothWordsAndExactTypeIdentity(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::BoxedInt64TypeIdentityEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxedInt64ValuesSurviveCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::BoxedInt64GcEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxedSingleWordStructsCopyValuesAndPreserveExactIdentity(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::BoxedSingleWordStructEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxedSingleWordStructsSurviveCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::BoxedSingleWordStructGcEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxedSingleWordStructInterfacesDispatchThroughUnboxingThunks(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::BoxedSingleWordStructInterfaceEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxedSingleWordStructInterfaceThunksAdaptScalarArguments(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::BoxedSingleWordStructInterfaceArgumentEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxedSingleWordStructInterfaceThunksAdaptReferenceArguments(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::BoxedSingleWordStructInterfaceReferenceArgumentEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxedSingleWordStructInterfaceThunksAdaptTwoRegisterArguments(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"BoxedSingleWordStructInterfaceTwoDataArgumentsEntry",
			"BoxedSingleWordStructInterfaceTwoReferenceArgumentsEntry",
			"BoxedSingleWordStructInterfaceMixedArgumentsEntry",
			"BoxedSingleWordStructInterfaceTwoDataExceptionEntry"
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_3000
				}
			});

			Assert.Equal(42u, ExecuteHunk(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxedSingleWordStructInterfaceThunksAdaptLongPairArguments(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"BoxedSingleWordStructInterfaceLongArgumentEntry",
			"BoxedSingleWordStructInterfaceLongExceptionEntry"
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			});

			Assert.Equal(42u, ExecuteHunk(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void BoxedMultiwordStructsCopyCompleteLocalPayloads(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::BoxedMultiwordStructLocalEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(MultiwordArgumentCases))]
	public void MultiwordStructArgumentsPreserveCompletePayloads(
		string entry,
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			});

		var actual = ExecuteHunk(result, model);
		Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
	}

	[Theory]
	[MemberData(nameof(MultiwordReturnCases))]
	public void MultiwordStructReturnsUseHiddenBuffers(
		string entry,
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			});

		var actual = ExecuteHunk(result, model);
		Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
	}

	[Theory]
	[MemberData(nameof(MultiwordFieldCases))]
	public void MultiwordStructFieldsPreserveSnapshotValueSemantics(
		string entry,
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			});

		var actual = ExecuteHunk(result, model);
		Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
	}

	[Theory]
	[MemberData(nameof(MultiwordArrayCases))]
	public void MultiwordStructArraysPreserveInlineValueSemantics(
		string entry,
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			});

		var actual = ExecuteHunk(result, model);
		Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
	}

	[Theory]
	[MemberData(nameof(MultiwordIndirectCases))]
	public void MultiwordStructIndirectOperationsPreserveValueSemantics(
		string entry,
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			});

		var actual = ExecuteHunk(result, model);
		Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CpobjCopiesReferenceFreeMultiwordStructsThroughPrivateSnapshot(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var assemblyPath = CreateCpobjFixtureAssembly();
		try
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = assemblyPath,
				EntryPoint =
					"CopperSharp.Compiler.Tests.CompilerFixtures::MultiwordIndirectCopyEntry",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			});

			Assert.Equal(42u, ExecuteHunk(result, model));
		}
		finally
		{
			File.Delete(assemblyPath);
		}
	}

	[Theory]
	[MemberData(nameof(MultiwordUnboxAnyCases))]
	public void MultiwordStructUnboxAnyCopiesCompletePayloadToDirectLocals(
		string entry,
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = target,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_2000
				}
			});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StaticSingleCastDelegatesInvokeReachableTargets(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[] { "StaticDelegateEntry", "NonCapturingLambdaEntry" })
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");

			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ClosedInstanceAndCapturingDelegatesUseTracedTargets(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"ClosedInstanceDelegateEntry",
			"CapturingLambdaEntry",
			"VirtualDelegateEntry",
			"InterfaceDelegateEntry",
			"CapturingActionEntry"
		})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");

			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CapturingDelegateAndClosureSurviveForcedCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CapturingLambdaGcEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SingleCastDelegateEqualityUsesTypeTargetAndMethodIdentity(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DelegateEqualityEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DelegateEqualsUsesLogicalDelegateIdentity(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DelegateEqualsEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MulticastDelegatesInvokeInOrderAndReturnTheFinalResult(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MulticastDelegateEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MulticastDelegateEqualityComparesInvocationSequences(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MulticastDelegateEqualityEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MulticastInvocationStopsAtTheFirstException(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MulticastDelegateExceptionEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CombiningDifferentDelegateTypesRaisesArgumentException(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::IncompatibleDelegateCombineEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MulticastInvocationTailAndCapturedTargetsSurviveCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::MulticastDelegateGcEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DelegateRemoveUsesTheLastSequenceAndCollapsesSmallResults(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MulticastDelegateRemoveEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DelegateRemoveResultSurvivesCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::MulticastDelegateRemoveGcEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[InlineData("InterfaceArgumentDispatchEntry")]
	[InlineData("InterfaceTwoDataArgumentDispatchEntry")]
	[InlineData("InterfaceLongArgumentDispatchEntry")]
	[InlineData("MultipleInterfaceDispatchEntry")]
	[InlineData("InheritedInterfaceDispatchEntry")]
	[InlineData("ExplicitInterfaceDispatchEntry")]
	[InlineData("InheritedClassInterfaceDispatchEntry")]
	public void InterfaceMapsSupportArgumentsInheritanceAndExplicitImplementations(
		string entryPoint)
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{entryPoint}");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void NullInterfaceReceiverRaisesNullReferenceBeforeMapLookup()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NullInterfaceDispatchEntry");

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void InterfaceDispatchSupportsHybridOverflowArguments(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::WideInterfaceDispatchEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			});

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Fact]
	public void DefaultInterfaceMethodHasStableUnsupportedDiagnostic()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::UnsupportedDefaultInterfaceDispatchEntry"
			}));

		Assert.Equal(M68kDiagnosticIds.UnsupportedPolymorphism, exception.DiagnosticId);
		Assert.Contains("Default interface methods", exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void VirtualDispatchSupportsHybridOverflowArguments(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::WideVirtualDispatchEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			});

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[InlineData("NullComparisonEntry")]
	[InlineData("ReferenceEqualityEntry")]
	public void CompilesNullAndReferenceComparisons(string entry)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
			OutputFormat = M68kOutputFormat.Assembly,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_1000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		var applicationText = BeforeExceptionRuntime(result);
		Assert.DoesNotContain(
			"\tmove.l\t(a7)+,d1\r\n\tmove.l\t(a7)+,d0\r\n\tcmp.l\td1,d0",
			applicationText,
			StringComparison.Ordinal);
		if (entry == "ReferenceEqualityEntry")
		{
			Assert.Contains("\tcmp.l\t", applicationText, StringComparison.Ordinal);
		}
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
			Array.Clear(bus.Memory, 0x4000, (int)state.D[0]);
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
	public void ExecutesShadowMathOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShadowMathAbsEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ShadowMethodExceptionUsesManagedUnwinding(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShadowMathOverflowCatchEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void AllocatingBigEndianShadowMethodExecutesOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShadowBitConverterEntry");

		Assert.Equal(0x01020304u, ExecuteHunkWithAllocator(result, model));
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
			var size = state.D[0];
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

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CompilesSignedByteArraysWithSignedElementAccess(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SignedByteArrayEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Fact]
	public void M68000SignExtendsBytesWithTwoInstructions()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SignedByteArrayEntry");

		Assert.Contains(
			"\text.w\td0\r\n\text.l\td0",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain("\textb.l", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	[InlineData(M68kCpuTarget.M68060)]
	public void M68020AndLaterUseExtbLong(M68kCpuTarget target)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SignedByteArrayEntry");

		Assert.Contains("\textb.l\td0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\text.w\td0\r\n\text.l\td0",
			result.Text,
			StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CompilesUnsignedShortArraysWithUnsignedElementAccess(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::UnsignedShortArrayEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CompilesExplicitByteArithmeticWithByteOpcode(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NarrowByteArithmeticEntry");

		Assert.Equal(4u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CompilesExplicitShortArithmeticWithWordOpcode(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NarrowShortArithmeticEntry");

		Assert.Equal(unchecked((uint)(short)-25536), ExecuteHunk(result, model));
	}

	[Fact]
	public void EmitsByteAndWordArithmeticOpcodes()
	{
		var byteResult = Compile(
			M68kCpuTarget.M68020,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NarrowByteArithmeticEntry");
		var shortResult = Compile(
			M68kCpuTarget.M68020,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NarrowShortArithmeticEntry");

		Assert.Contains("add.b\td1,d0", byteResult.Text, StringComparison.Ordinal);
		Assert.Contains("add.w\td1,d0", shortResult.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CompilesNarrowArithmeticOperations(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NarrowArithmeticOperationsEntry");

		Assert.Equal(64215u, ExecuteHunk(result, model));
	}

	[Fact]
	public void EmitsNarrowSubtractionMultiplyShiftAndUnaryOpcodes()
	{
		var result = Compile(
			M68kCpuTarget.M68020,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NarrowArithmeticOperationsEntry");

		Assert.Contains("sub.w\td1,d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("mulu.w\td1,d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("asr.w\t#1,d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("neg.b\td0", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(NarrowOperationCases))]
	public void ExecutesIndividualNarrowOperations(
		string entryPoint,
		uint expected,
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{entryPoint}");

		Assert.Equal(expected, ExecuteHunk(result, model));
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
		Assert.Matches(@"move\.b\td[0-7],(?:12)?\(a[0-6]\)", result.Text);
		Assert.Matches(@"move\.w\td[0-7],(?:12)?\(a[0-6]\)", result.Text);
		Assert.Matches(@"move\.b\t(?:12)?\(a[0-6]\),d[0-7]", result.Text);
		Assert.Matches(@"move\.w\t(?:12)?\(a[0-6]\),d[0-7]", result.Text);
		Assert.Contains("ext.l\td0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("btst\t#15,d0", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void EmitsCpuSpecificIndexedArrayEffectiveAddresses()
	{
		var m68000 = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShortArrayEntry");
		var m68020 = Compile(
			M68kCpuTarget.M68020,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShortArrayEntry");
		var m68040 = Compile(
			M68kCpuTarget.M68040,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ManagedArrayEntry");

		Assert.Matches(@"move\.w\t12\(a[0-6],d[0-7]\.l\),d[0-7]", m68000.Text);
		Assert.Contains("lsl.l\t#1", m68000.Text, StringComparison.Ordinal);
		Assert.Matches(@"move\.w\t12\(a[0-6],d[0-7]\.l\*2\),d[0-7]", m68020.Text);
		Assert.Matches(@"(?:move|add)\.l\t12\(a[0-6],d[0-7]\.l\*4\),d[0-7]", m68040.Text);
	}

	[Fact]
	public void FusesConstantM68kAddressOffsetsIntoDirectMemoryAccess()
	{
		var read = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::AddressReadConstantEntry",
			OutputFormat = M68kOutputFormat.Assembly
		});
		var write = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::AddressWriteConstantEntry",
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Contains("move.l\t8(a0),d0", read.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("adda.l\td0,a0", read.Text, StringComparison.Ordinal);
		Assert.Matches(@"move\.l\td[0-7],8\(a0\)", write.Text);
		Assert.DoesNotContain("adda.l\td1,a0", write.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ConstantAddressDisplacementUsesDirectFormOnlyWhenLegal()
	{
		static string CompileAddressMethod(string method) =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{method}",
				OutputFormat = M68kOutputFormat.Assembly
			}).Text!;

		var negative = CompileAddressMethod("AddressReadNegativeEntry");
		var large = CompileAddressMethod("AddressReadLargeEntry");
		var dynamic = CompileAddressMethod("AddressReadDynamicEntry");

		Assert.Matches(@"move\.l\t-8\(a[0-6]\),d[0-7]", negative);
		Assert.DoesNotMatch(@"move\.l\t40000\(a[0-6]\),d[0-7]", large);
		Assert.Matches(@"(?:add|adda)\.l\t", large);
		Assert.DoesNotMatch(@"move\.l\t-?\d+\(a[0-6]\),d[0-7]", dynamic);
		Assert.Matches(@"(?:add|adda)\.l\t", dynamic);
	}

	[Fact]
	public void FileInfoBlockFieldsUseAbiFixedDisplacementLoads()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			ManagedAssemblyPaths = [typeof(global::Amiga.FileInfoBlock).Assembly.Location],
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::FileInfoBlockFixedFieldsEntry",
			OutputFormat = M68kOutputFormat.Assembly
		});

		foreach (var offset in new[] { 4, 124, 132, 136, 140 })
		{
			Assert.Matches($@"(?:move|add)\.l\t{offset}\(a[0-6]\),d[0-7]", result.Text!);
		}
		Assert.DoesNotContain("::GetSize", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("adda.l", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7)+", result.Text, StringComparison.Ordinal);
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
			var size = state.D[0];
			var address = heap;
			heap += (size + 3) & ~3u;
			Array.Clear(bus.Memory, (int)address, (int)size);
			state.D[0] = address;
		});
		bus.RegisterGateway(disposeAddress, state =>
		{
			var slot = state.A[0];
			disposed = bus.ReadLong(slot);
			Assert.NotEqual(0u, disposed);
			bus.WriteLong(slot, 0);
		});

		Assert.Equal(42u, Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.NotEqual(0u, disposed);
	}

	[Fact]
	public void StaticAnalyzerRejectsRuntimeDisposeWithoutDisposeRuntime()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ExplicitDisposeEntry",
				Imports = new Dictionary<string, uint>
				{
					[M68kRuntimeImports.Allocate] = 0x0000_2800
				}
			}));

		Assert.Equal(M68kDiagnosticIds.StaticAnalysis, exception.DiagnosticId);
		Assert.Contains("runtime dispose operation", exception.Message, StringComparison.Ordinal);
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
	public void ManagedPoolCollectTracesRootsInCallerFrames()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolCollectTracesCallerFrameEntry",
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
	public void ManagedPoolSkipsRootSyncForTransitivelyNonAllocatingCall()
	{
		var request = new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::NonAllocatingCallWithLiveReferenceEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 120
			}
		};
		var executable = M68kCompiler.Compile(request);
		Assert.Equal(42u, ExecuteHunk(executable, M68kCpuModel.M68000));

		var assembly = M68kCompiler.Compile(request with
		{
			OutputFormat = M68kOutputFormat.Assembly
		});
		var text = assembly.Text!;
		var methodStart = text.IndexOf(
			"\nC68K_method_003A",
			StringComparison.Ordinal) + 1;
		var methodLabelEnd = text.IndexOf(':', methodStart);
		var methodLabel = text[methodStart..methodLabelEnd];
		var methodEnd = text.IndexOf(
			$"\n{methodLabel}_003Aend:",
			methodLabelEnd,
			StringComparison.Ordinal);
		var methodBody = text[methodLabelEnd..methodEnd];

		Assert.DoesNotContain(
			"\tmove.l\t(a7),d0\r\n\tmove.l\td0,",
			methodBody,
			StringComparison.Ordinal);
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
	public void ManagedPoolTelemetryTriggeredStrategyCollectsWhenThresholdIsReached()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolAllocationFailureCollectsRootsEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.TelemetryTriggered,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 120
			},
			GcTelemetry = new M68kGcTelemetryOptions
			{
				StaleBytesThreshold = 1
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ManagedPoolTelemetryCountersAreReadable()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolTelemetryCountersEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 120
			}
		});

		Assert.Equal(1u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ManagedPoolTelemetryCountersResetAfterCollect()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolTelemetryCountersResetAfterCollectEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 120
			}
		});

		Assert.Equal(0u, ExecuteHunk(result, M68kCpuModel.M68000));
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

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ManagedPoolCollectTracesRegisterAndOverflowReferenceArguments(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::GcHybridReferenceArgumentsEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_1000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
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
	public void ManagedPoolCollectTracesDeepObjectGraph()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolCollectTracesDeepObjectGraphEntry",
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 144
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
		var methodTable = result.Symbols.Single(
			symbol => symbol.Name == "__c68k_method_table").Address;
		bus.RegisterGateway(collectAddress, state =>
		{
			Assert.Equal(state.A[7] + 4, state.D[0]);
			Assert.NotEqual(0u, state.D[1]);
			Assert.Equal(HunkLoadAddress + methodTable, state.A[0]);
			Assert.NotEqual(0u, state.A[1]);
			calls++;
		});

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
		Assert.Contains("\tmoveq\t#3,d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tadd.l\td1,d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmoveq\t#2,d1", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tsub.l\td1,d0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\taddq.l\t#3,(a7)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tsubq.l\t#2,(a7)", result.Text, StringComparison.Ordinal);
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
		Assert.Matches(
			@"\t(?:jmp|bra\.[sw])\tC68K_method_003A[0-9A-F]+",
			result.Text);
		Assert.DoesNotMatch(
			@"\taddq\.l\t#4,a7\r\n\taddq\.l\t#4,a7\r\n\t(?:jmp|bra\.[sw])\t",
			result.Text);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void FallsBackToNormalCallWhenTailOverflowCannotBeInstalledSafely(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::OverflowTailCallEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
		Assert.Matches(@"\tbsr\.[sw]\tC68K_method_", result.Text);
		Assert.DoesNotMatch(
			@"\t(?:jmp|bra\.[sw])\tC68K_method_[A-Za-z0-9_]+",
			result.Text);
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

		var assembly = Compile(
			target,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShiftAndCompare");
		Assert.Contains("\tmovem.l\td2-d7/a2-a6,-(a7)", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmovem.l\t(a7)+,d2-d7/a2-a6", assembly.Text, StringComparison.Ordinal);
		Assert.Contains(
			"\tmove.l\td2,-(a7)\r\n" +
			"\tmove.l\td1,-(a7)\r\n" +
			"\tmove.l\td0,-(a7)",
			assembly.Text,
			StringComparison.Ordinal);
		Assert.Contains("\tmove.l\t(a7),d0", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\t4(a7),d1", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\taddq.l\t#4,a7", assembly.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ExportAddressIntrinsicReturnsExportAdapterPointerAsAptr()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ExportAddressEntry");
		var export = result.Symbols.Single(symbol => symbol.Name == "fixture.add");

		Assert.Equal(HunkLoadAddress + export.Address, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Contains(result.Relocations, relocation => relocation.Target == "export:fixture.add");

		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ExportAddressEntry");
		Assert.Matches(
			@"\tmovea?\.l\t#C68K_export_003Afixture_002Eadd,[ad][0-7]",
			assembly.Text);
		Assert.DoesNotContain(
			"\tmove.l\t#C68K_export_003Afixture_002Eadd,-(a7)",
			assembly.Text,
			StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpecializesGenericMethodBodyAcrossScalarAndReferenceInstantiations(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SharedGenericEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
		var specializations = result.Symbols.Where(symbol =>
			symbol.Name.Contains("::SharedIdentity<", StringComparison.Ordinal)).ToArray();
		Assert.NotEmpty(specializations);
		Assert.DoesNotContain(result.Symbols, symbol =>
			symbol.Name.EndsWith("::SharedIdentity", StringComparison.Ordinal));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SpecializesExternalFrameworkGenericMethodSpecsByRepresentation(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::FrameworkGenericSpecializationEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
		Assert.DoesNotContain(
			"RuntimeHelpers",
			result.Text,
			StringComparison.Ordinal);
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

	[Fact]
	public void M68040FloatingPointModeEmitsAndExecutesSingleArithmetic()
	{
		var result = Compile(
			M68kCpuTarget.M68040,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NativeFloatAdd",
			floatingPoint: M68kFloatingPointMode.M68040);

		Assert.Contains(result.Code, static value => value == 0xF2);
		Assert.Equal(
			unchecked((uint)BitConverter.SingleToInt32Bits(3.75f)),
			ExecuteHunk(result, M68kCpuModel.M68040));
	}

	[Fact]
	public void RejectsNativeFpuModeForIncompatibleCpu()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			Compile(
				M68kCpuTarget.M68020,
				M68kOutputFormat.Hunk,
				"CopperSharp.Compiler.Tests.CompilerFixtures::NativeFloatAdd",
				floatingPoint: M68kFloatingPointMode.M68040));

		Assert.Equal(M68kDiagnosticIds.InvalidOutputOptions, exception.DiagnosticId);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68040, M68kFloatingPointMode.M68040)]
	[InlineData(M68kCpuTarget.M68060, M68kFloatingPointMode.M68040)]
	[InlineData(M68kCpuTarget.M68020, M68kFloatingPointMode.M68882)]
	public void NativeFpuModesRenderFloatingPointInstructions(
		M68kCpuTarget cpu,
		M68kFloatingPointMode floatingPoint)
	{
		var result = Compile(
			cpu,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NativeDoubleMultiply",
			floatingPoint: floatingPoint);

		Assert.Contains("\tfmove.d\t", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tfmul.x\t", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void M68060DoubleArithmeticUsesOneSharedScratchWindow()
	{
		var result = Compile(
			M68kCpuTarget.M68060,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NativeDoubleMultiply",
			floatingPoint: M68kFloatingPointMode.M68040);

		Assert.Contains("\tfmove.d\t(a7),fp0", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tfmove.d\t8(a7),fp1", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tfmove.d\tfp0,(a7)", result.Text, StringComparison.Ordinal);
		Assert.Equal(
			unchecked((uint)(BitConverter.DoubleToInt64Bits(6.0d) >> 32)),
			ExecuteHunk(
				Compile(
					M68kCpuTarget.M68060,
					M68kOutputFormat.Hunk,
					"CopperSharp.Compiler.Tests.CompilerFixtures::NativeDoubleMultiply",
					floatingPoint: M68kFloatingPointMode.M68040),
				M68kCpuModel.M68040));
	}

	[Fact]
	public void M68040DoubleArithmeticReusesOneEightByteScratchSlot()
	{
		var result = Compile(
			M68kCpuTarget.M68040,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NativeDoubleMultiply",
			floatingPoint: M68kFloatingPointMode.M68040);

		Assert.Contains("\tfmove.d\t(a7),fp0", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tfmove.d\t(a7),fp1", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tfmove.d\tfp0,(a7)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tfmove.d\t8(a7)", result.Text, StringComparison.Ordinal);
		Assert.Equal(
			unchecked((uint)(BitConverter.DoubleToInt64Bits(6.0d) >> 32)),
			ExecuteHunk(
				Compile(
					M68kCpuTarget.M68040,
					M68kOutputFormat.Hunk,
					"CopperSharp.Compiler.Tests.CompilerFixtures::NativeDoubleMultiply",
					floatingPoint: M68kFloatingPointMode.M68040),
				M68kCpuModel.M68040));
	}

	private static string FixtureAssembly => Assembly.GetExecutingAssembly().Location;

	private static string CreateCpobjFixtureAssembly()
	{
		var image = File.ReadAllBytes(FixtureAssembly);
		using var peReader = new PEReader(new MemoryStream(image, writable: false));
		var metadata = peReader.GetMetadataReader();
		var copyMethod = metadata.MethodDefinitions
			.Select(metadata.GetMethodDefinition)
			.Single(method => metadata.GetString(method.Name) == "CopyIndirect");
		var rva = copyMethod.RelativeVirtualAddress;
		var section = peReader.PEHeaders.SectionHeaders.Single(candidate =>
			rva >= candidate.VirtualAddress &&
			rva < candidate.VirtualAddress +
				Math.Max(candidate.VirtualSize, candidate.SizeOfRawData));
		var bodyOffset = checked(
			section.PointerToRawData + rva - section.VirtualAddress);
		var firstHeaderByte = image[bodyOffset];
		var headerSize = (firstHeaderByte & 3) == 2
			? 1
			: checked((BinaryPrimitives.ReadUInt16LittleEndian(
				image.AsSpan(bodyOffset, 2)) >> 12) * 4);
		var codeOffset = checked(bodyOffset + headerSize);
		var code = image.AsSpan(codeOffset, 13);
		Assert.Equal((byte)0x02, code[0]); // ldarg.0
		Assert.Equal((byte)0x03, code[1]); // ldarg.1
		Assert.Equal((byte)0x71, code[2]); // ldobj
		Assert.Equal((byte)0x81, code[7]); // stobj
		Assert.Equal(code.Slice(3, 4).ToArray(), code.Slice(8, 4).ToArray());
		Assert.Equal((byte)0x2A, code[12]); // ret

		code[2] = 0x70; // cpobj; the existing ldobj type token stays in place
		code[7] = 0x2A; // ret
		code.Slice(8, 5).Clear(); // unreachable nops preserve body size

		var path = Path.Combine(
			Path.GetDirectoryName(FixtureAssembly)!,
			$"CopperSharp-cpobj-{Guid.NewGuid():N}.dll");
		File.WriteAllBytes(path, image);
		return path;
	}

	private static string CreateBoxInteriorByrefFixtureAssembly()
	{
		var image = File.ReadAllBytes(FixtureAssembly);
		using var peReader = new PEReader(new MemoryStream(image, writable: false));
		var metadata = peReader.GetMetadataReader();
		var template = metadata.MethodDefinitions
			.Select(metadata.GetMethodDefinition)
			.Single(method => metadata.GetString(method.Name) ==
				"BoxInteriorByrefTemplateEntry");
		var rva = template.RelativeVirtualAddress;
		var section = peReader.PEHeaders.SectionHeaders.Single(candidate =>
			rva >= candidate.VirtualAddress &&
			rva < candidate.VirtualAddress +
				Math.Max(candidate.VirtualSize, candidate.SizeOfRawData));
		var bodyOffset = checked(
			section.PointerToRawData + rva - section.VirtualAddress);
		var firstHeaderByte = image[bodyOffset];
		var headerSize = (firstHeaderByte & 3) == 2
			? 1
			: checked((BinaryPrimitives.ReadUInt16LittleEndian(
				image.AsSpan(bodyOffset, 2)) >> 12) * 4);
		var codeOffset = checked(bodyOffset + headerSize);
		var templateIl = peReader.GetMethodBody(rva).GetILBytes() ??
			throw new InvalidOperationException("Template method has no IL body.");
		var codeSize = templateIl.Length;
		var code = image.AsSpan(codeOffset, codeSize);

		var unboxAnyOffset = code.IndexOf((byte)0xA5);
		Assert.True(unboxAnyOffset >= 0, "Template must contain unbox.any.");
		Assert.Equal((byte)0x0C, code[unboxAnyOffset + 5]); // stloc.2
		var reloadOffset = code.IndexOf(new byte[] { 0x12, 0x02, 0x28 });
		Assert.True(reloadOffset >= 0, "Template must reload local 2 for Sum().");

		code[unboxAnyOffset] = 0x79; // unbox; the type token is unchanged
		code[unboxAnyOffset + 5] = 0x00; // keep the managed pointer on-stack
		code[reloadOffset] = 0x00; // pointer is already live across Collect
		code[reloadOffset + 1] = 0x00;

		var path = Path.Combine(
			Path.GetDirectoryName(FixtureAssembly)!,
			$"CopperSharp-unbox-byref-{Guid.NewGuid():N}.dll");
		File.WriteAllBytes(path, image);
		return path;
	}

	private static string CreateManagedByrefEscapeFixtureAssembly(
		string entry,
		string placeholderMethod,
		string targetField,
		bool isStatic)
	{
		var image = File.ReadAllBytes(FixtureAssembly);
		using var peReader = new PEReader(new MemoryStream(image, writable: false));
		var metadata = peReader.GetMetadataReader();
		var methods = metadata.MethodDefinitions
			.Select(handle => (Handle: handle, Definition: metadata.GetMethodDefinition(handle)))
			.ToArray();
		var template = methods.Single(item =>
			metadata.GetString(item.Definition.Name) == entry);
		var placeholder = methods.Single(item =>
			metadata.GetString(item.Definition.Name) == placeholderMethod);
		var fields = metadata.FieldDefinitions
			.Select(handle => (Handle: handle, Definition: metadata.GetFieldDefinition(handle)))
			.ToArray();
		var field = fields.Single(item =>
			metadata.GetString(item.Definition.Name) == targetField);

		var rva = template.Definition.RelativeVirtualAddress;
		var section = peReader.PEHeaders.SectionHeaders.Single(candidate =>
			rva >= candidate.VirtualAddress &&
			rva < candidate.VirtualAddress +
				Math.Max(candidate.VirtualSize, candidate.SizeOfRawData));
		var bodyOffset = checked(
			section.PointerToRawData + rva - section.VirtualAddress);
		var firstHeaderByte = image[bodyOffset];
		var headerSize = (firstHeaderByte & 3) == 2
			? 1
			: checked((BinaryPrimitives.ReadUInt16LittleEndian(
				image.AsSpan(bodyOffset, 2)) >> 12) * 4);
		var codeOffset = checked(bodyOffset + headerSize);
		var codeSize = peReader.GetMethodBody(rva).GetILBytes()?.Length ??
			throw new InvalidOperationException("Template method has no IL body.");
		var code = image.AsSpan(codeOffset, codeSize);
		var placeholderToken = MetadataTokens.GetToken(placeholder.Handle);
		var callOffset = -1;
		for (var index = 0; index <= code.Length - 5; index++)
		{
			if (code[index] == 0x28 &&
				BinaryPrimitives.ReadInt32LittleEndian(code.Slice(index + 1, 4)) ==
					placeholderToken)
			{
				callOffset = index;
				break;
			}
		}
		Assert.True(callOffset >= 0, "Template must call the placeholder method.");

		code[callOffset] = isStatic ? (byte)0x80 : (byte)0x7D; // stsfld / stfld
		BinaryPrimitives.WriteInt32LittleEndian(
			code.Slice(callOffset + 1, 4),
			MetadataTokens.GetToken(field.Handle));

		var path = Path.Combine(
			Path.GetDirectoryName(FixtureAssembly)!,
			$"CopperSharp-byref-escape-{Guid.NewGuid():N}.dll");
		File.WriteAllBytes(path, image);
		return path;
	}

	private static string CreateReadonlyByrefWriteFixtureAssembly()
	{
		var image = File.ReadAllBytes(FixtureAssembly);
		using var peReader = new PEReader(new MemoryStream(image, writable: false));
		var metadata = peReader.GetMetadataReader();
		var methods = metadata.MethodDefinitions
			.Select(handle => (Handle: handle, Definition: metadata.GetMethodDefinition(handle)))
			.ToArray();
		var template = methods.Single(item =>
			metadata.GetString(item.Definition.Name) == "ReadonlyByrefWriteTemplate");
		var placeholder = methods.Single(item =>
			metadata.GetString(item.Definition.Name) == "IgnoreReadonlyReference");
		var rva = template.Definition.RelativeVirtualAddress;
		var section = peReader.PEHeaders.SectionHeaders.Single(candidate =>
			rva >= candidate.VirtualAddress &&
			rva < candidate.VirtualAddress +
				Math.Max(candidate.VirtualSize, candidate.SizeOfRawData));
		var bodyOffset = checked(
			section.PointerToRawData + rva - section.VirtualAddress);
		var headerSize = (image[bodyOffset] & 3) == 2
			? 1
			: checked((BinaryPrimitives.ReadUInt16LittleEndian(
				image.AsSpan(bodyOffset, 2)) >> 12) * 4);
		var codeOffset = checked(bodyOffset + headerSize);
		var codeSize = peReader.GetMethodBody(rva).GetILBytes()?.Length ??
			throw new InvalidOperationException("Template method has no IL body.");
		var code = image.AsSpan(codeOffset, codeSize);
		var placeholderToken = MetadataTokens.GetToken(placeholder.Handle);
		var callOffset = -1;
		for (var index = 0; index <= code.Length - 5; index++)
		{
			if (code[index] == 0x28 &&
				BinaryPrimitives.ReadInt32LittleEndian(code.Slice(index + 1, 4)) ==
					placeholderToken)
			{
				callOffset = index;
				break;
			}
		}
		Assert.True(callOffset >= 0, "Template must call the readonly placeholder.");
		code[callOffset] = 0x54; // stind.i4
		code.Slice(callOffset + 1, 4).Clear();

		var path = Path.Combine(
			Path.GetDirectoryName(FixtureAssembly)!,
			$"CopperSharp-readonly-byref-{Guid.NewGuid():N}.dll");
		File.WriteAllBytes(path, image);
		return path;
	}

	private static string CreateIncompatibleByrefTypeFixtureAssembly()
	{
		var image = File.ReadAllBytes(FixtureAssembly);
		using var peReader = new PEReader(new MemoryStream(image, writable: false));
		var metadata = peReader.GetMetadataReader();
		var methods = metadata.MethodDefinitions
			.Select(handle => (Handle: handle, Definition: metadata.GetMethodDefinition(handle)))
			.ToArray();
		var template = methods.Single(item =>
			metadata.GetString(item.Definition.Name) == "IncompatibleByrefTypeTemplate");
		var fields = metadata.FieldDefinitions
			.Select(handle => (Handle: handle, Definition: metadata.GetFieldDefinition(handle)))
			.ToArray();
		var sourceField = fields.Single(item =>
			metadata.GetString(item.Definition.Name) == "OtherValue");
		var targetField = fields.Single(item =>
			metadata.GetString(item.Definition.Name) == "ByrefEscapeSink");
		var rva = template.Definition.RelativeVirtualAddress;
		var section = peReader.PEHeaders.SectionHeaders.Single(candidate =>
			rva >= candidate.VirtualAddress &&
			rva < candidate.VirtualAddress +
				Math.Max(candidate.VirtualSize, candidate.SizeOfRawData));
		var bodyOffset = checked(
			section.PointerToRawData + rva - section.VirtualAddress);
		var headerSize = (image[bodyOffset] & 3) == 2
			? 1
			: checked((BinaryPrimitives.ReadUInt16LittleEndian(
				image.AsSpan(bodyOffset, 2)) >> 12) * 4);
		var codeOffset = checked(bodyOffset + headerSize);
		var codeSize = peReader.GetMethodBody(rva).GetILBytes()?.Length ??
			throw new InvalidOperationException("Template method has no IL body.");
		var code = image.AsSpan(codeOffset, codeSize);
		var sourceToken = MetadataTokens.GetToken(sourceField.Handle);
		var fieldOffset = -1;
		for (var index = 0; index <= code.Length - 5; index++)
		{
			if (code[index] == 0x7C &&
				BinaryPrimitives.ReadInt32LittleEndian(code.Slice(index + 1, 4)) ==
					sourceToken)
			{
				fieldOffset = index;
				break;
			}
		}
		Assert.True(fieldOffset >= 0, "Template must take OtherValue's address.");
		BinaryPrimitives.WriteInt32LittleEndian(
			code.Slice(fieldOffset + 1, 4),
			MetadataTokens.GetToken(targetField.Handle));

		var path = Path.Combine(
			Path.GetDirectoryName(FixtureAssembly)!,
			$"CopperSharp-byref-type-{Guid.NewGuid():N}.dll");
		File.WriteAllBytes(path, image);
		return path;
	}

	private static string BeforeExceptionRuntime(M68kCompilationResult result)
	{
		var text = result.Text ?? string.Empty;
		var runtimeOffset = text.IndexOf(
			"__c68k_exception_raise:",
			StringComparison.Ordinal);
		return runtimeOffset < 0 ? text : text[..runtimeOffset];
	}

	private static string StaticFieldLabel(string fieldName)
	{
		return $"C68K_static_003A{StaticFieldToken(fieldName):X8}";
	}

	private static string StaticFieldRelocation(string fieldName) =>
		$"static:{StaticFieldToken(fieldName):X8}";

	private static int StaticFieldToken(string fieldName)
	{
		var field = typeof(CompilerFixtures).GetField(
			fieldName,
			BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(field);
		return field.MetadataToken;
	}

	private static void AssertStaticStore(string assembly, string fieldName)
	{
		var label = StaticFieldLabel(fieldName);
		Assert.Matches(
			$@"\t(?:clr\.l\t{label}|move\.l\t[^\r\n]+,{label})",
			assembly);
	}

	private static M68kCompilationResult Compile(
		M68kCpuTarget cpu,
		M68kOutputFormat format,
		string entry,
		M68kClrPolicy clrPolicy = M68kClrPolicy.Auto,
		M68kExceptionMode exceptionMode = M68kExceptionMode.Full,
		IReadOnlyDictionary<string, uint>? imports = null,
		M68kFloatingPointMode floatingPoint = M68kFloatingPointMode.Disabled) =>
		AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = entry,
			Cpu = cpu,
			FloatingPoint = floatingPoint,
			ClrPolicy = clrPolicy,
			ExceptionMode = exceptionMode,
			Imports = imports ?? new Dictionary<string, uint>(),
			OutputFormat = format,
			RuntimeProfile = format == M68kOutputFormat.KickstartRom
				? M68kRuntimeProfile.Freestanding
				: M68kRuntimeProfile.Application,
			Rom = new KickstartRomOutputOptions
			{
				Size = 512 * 1024,
				InitialStackPointer = StackPointer
			}
		});

	private static M68kCompilationResult CompileWithAllocator(
		M68kCpuTarget cpu,
		string entry,
		M68kClrPolicy clrPolicy = M68kClrPolicy.Auto,
		M68kFloatingPointMode floatingPoint = M68kFloatingPointMode.Disabled) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = entry,
			Cpu = cpu,
			FloatingPoint = floatingPoint,
			ClrPolicy = clrPolicy,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});

	private static long MeasureCycles(string entry)
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			entry);
		var bus = CreateHunkBus(result);
		long cycles = 0;
		Execute(
			bus,
			M68kCpuModel.M68000,
			HunkLoadAddress + result.EntryPoint,
			afterReturn: state => cycles = state.Cycles);
		return cycles;
	}

	private static uint ExecuteHunk(
		M68kCompilationResult result,
		M68kCpuModel model,
		Action<IM68kCore, TestBus>? beforeInstruction = null)
	{
		var bus = CreateHunkBus(result);
		return Execute(bus, model, HunkLoadAddress + result.EntryPoint, beforeInstruction);
	}

	private static uint ExecuteHunkWithAllocator(M68kCompilationResult result, M68kCpuModel model)
	{
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			var size = state.D[0];
			var address = heap;
			heap += (size + 3) & ~3u;
			Array.Clear(bus.Memory, (int)address, (int)size);
			state.D[0] = address;
		});
		return Execute(bus, model, HunkLoadAddress + result.EntryPoint);
	}

	private static long MeasureCyclesWithAllocator(M68kCompilationResult result)
	{
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			var size = state.D[0];
			state.D[0] = heap;
			heap += (size + 3) & ~3u;
		});
		long cycles = 0;
		Assert.Equal(
			0u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		return cycles;
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

	private static void InitializeClassicCalleeSavedRegisters(M68kCpuState state)
	{
		for (var index = 2; index <= 7; index++)
		{
			state.D[index] = 0xD000_0000u | (uint)index;
		}
		for (var index = 2; index <= 6; index++)
		{
			state.A[index] = 0x00A0_0000u | (uint)index;
		}
	}

	private static void AssertClassicCalleeSavedRegisters(
		M68kCpuState state,
		string callKind)
	{
		for (var index = 2; index <= 7; index++)
		{
			var expected = 0xD000_0000u | (uint)index;
			Assert.True(
				expected == state.D[index],
				$"{callKind} did not preserve D{index}: expected ${expected:X8}, actual ${state.D[index]:X8}.");
		}
		for (var index = 2; index <= 6; index++)
		{
			var expected = 0x00A0_0000u | (uint)index;
			Assert.True(
				expected == state.A[index],
				$"{callKind} did not preserve A{index}: expected ${expected:X8}, actual ${state.A[index]:X8}.");
		}
	}

	private static string ReadCString(TestBus bus, uint address)
	{
		var chars = new List<char>();
		for (var offset = 0u; ; offset++)
		{
			var value = bus.Memory[(int)(address + offset)];
			if (value == 0)
			{
				return new string(chars.ToArray());
			}

			chars.Add((char)value);
		}
	}

	private static int CountBranchesToForwardingBlocks(string assembly)
	{
		var lines = assembly.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		var labelIndexes = lines
			.Select((line, index) => (line, index))
			.Where(static item =>
				item.line.Length > 1 &&
				item.line[^1] == ':' &&
				!char.IsWhiteSpace(item.line[0]))
			.ToArray();
		var forwardingBlocks = new HashSet<string>(StringComparer.Ordinal);
		for (var labelIndex = 0; labelIndex < labelIndexes.Length; labelIndex++)
		{
			var (labelLine, lineIndex) = labelIndexes[labelIndex];
			if (!labelLine.StartsWith("C68K_method_", StringComparison.Ordinal) ||
				!labelLine.Contains("_003ABB", StringComparison.Ordinal))
			{
				continue;
			}
			var end = labelIndex + 1 < labelIndexes.Length
				? labelIndexes[labelIndex + 1].index
				: lines.Length;
			var instructions = lines[(lineIndex + 1)..end]
				.Where(static line => line.StartsWith('\t'))
				.ToArray();
			if (instructions is [var instruction] &&
				instruction.StartsWith("\tbra.w\t", StringComparison.Ordinal))
			{
				forwardingBlocks.Add(labelLine[..^1]);
			}
		}

		return lines.Count(line =>
		{
			if (!line.StartsWith("\tb", StringComparison.Ordinal) ||
				!line.Contains(".w\t", StringComparison.Ordinal))
			{
				return false;
			}
			var separator = line.IndexOf('\t', 1);
			return separator >= 0 &&
				forwardingBlocks.Contains(line[(separator + 1)..]);
		});
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

	private sealed class CachedPlatformBaseResolver(uint sourceAddress) : IM68kExternalCallResolver
	{
		public bool TryResolve(
			M68kExternalMethod method,
			out M68kExternalCallConvention convention)
		{
			if (method.DisplayName != "CopperSharp.Compiler.Tests.CompilerFixtures::CachedPlatformBaseCall")
			{
				convention = null!;
				return false;
			}

			convention = new M68kExternalCallConvention(
				"fixture.cached",
				M68kExternalBaseSource.CachedPointer,
				M68kRegister.A6,
				-30,
				CacheRegister: M68kRegister.A4,
				SourceAddress: sourceAddress,
				ParameterRegisters: Array.Empty<M68kRegister>(),
				ReturnRegister: M68kRegister.D0);
			return true;
		}
	}

	private sealed class ExceptionStatusResolver(uint baseAddress) : IM68kExternalCallResolver
	{
		public bool TryResolve(
			M68kExternalMethod method,
			out M68kExternalCallConvention convention)
		{
			var success = method.DisplayName ==
				"CopperSharp.Compiler.Tests.CompilerFixtures::ExternalSuccess";
			if (!success &&
				method.DisplayName !=
					"CopperSharp.Compiler.Tests.CompilerFixtures::ExternalFailure")
			{
				convention = null!;
				return false;
			}

			convention = new M68kExternalCallConvention(
				"fixture.exception-status",
				M68kExternalBaseSource.Immediate,
				M68kRegister.A6,
				-30,
				InitialValue: baseAddress,
				ParameterRegisters: Array.Empty<M68kRegister>(),
				ReturnRegister: success ? M68kRegister.D0 : M68kRegister.D1,
				ExceptionPolicy: M68kExternalExceptionPolicy.NonZeroStatus,
				ExceptionStatusRegister: success ? M68kRegister.D1 : M68kRegister.D0);
			return true;
		}
	}
}
