using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
	using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;
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

	public static TheoryData<M68kCpuTarget, M68kCpuModel, M68kGcSweepStrategy, bool>
		PortableConsoleReadLineCases
	{
		get
		{
			var result = new TheoryData<
				M68kCpuTarget,
				M68kCpuModel,
				M68kGcSweepStrategy,
				bool>();
			foreach (var cpu in CpuTargets)
			{
				var target = (M68kCpuTarget)cpu[0];
				var model = (M68kCpuModel)cpu[1];
				result.Add(target, model, M68kGcSweepStrategy.OnAllocationFailure, true);
				result.Add(target, model, M68kGcSweepStrategy.EveryAllocation, false);
			}
			return result;
		}
	}

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		PortableFileSystemNativeFailureCases
	{
		get
		{
			var result = new TheoryData<string, M68kCpuTarget, M68kCpuModel>();
			foreach (var scenario in new[]
			{
				"open-failure",
				"lock-failure",
				"examine-failure"
			})
			{
				foreach (var cpu in CpuTargets)
				{
					result.Add(
						scenario,
						(M68kCpuTarget)cpu[0],
						(M68kCpuModel)cpu[1]);
				}
			}
			return result;
		}
	}

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		ListIntegralEqualityCases
	{
		get
		{
			var result = new TheoryData<string, M68kCpuTarget, M68kCpuModel>();
			foreach (var entry in new[]
			{
				"ListInt32EqualityEntry",
				"ListInt64EqualityEntry",
				"ListNarrowIntegralEqualityEntry"
			})
			{
				foreach (var cpu in CpuTargets)
				{
					result.Add(
						entry,
						(M68kCpuTarget)cpu[0],
						(M68kCpuModel)cpu[1]);
				}
			}
			return result;
		}
	}

	public static TheoryData<string, M68kCpuTarget, M68kCpuModel>
		ListEnumEqualityCases
	{
		get
		{
			var result = new TheoryData<string, M68kCpuTarget, M68kCpuModel>();
			foreach (var entry in new[]
			{
				"ListByteEnumEqualityEntry",
				"ListIntEnumEqualityEntry",
				"ListLongEnumEqualityEntry"
			})
			{
				foreach (var cpu in CpuTargets)
				{
					result.Add(
						entry,
						(M68kCpuTarget)cpu[0],
						(M68kCpuModel)cpu[1]);
				}
			}
			return result;
		}
	}

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
	public void AllocatedDenseSwitchPreservesCaseDefaultAndPhiEdges(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::AllocatedDenseSwitchEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableEnvironmentReportsAmigaValuesOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableEnvironmentEntry");
		var bus = CreateHunkBus(result);
		long cycles = 0;

		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		if (target == M68kCpuTarget.M68000)
		{
			Assert.True(result.Image.Length <= 2_100);
			Assert.True(result.Code.Length <= 1_450);
			Assert.True(cycles <= 600);
			Assert.Equal(4, result.AllocationStatistics.Count);
			Assert.All(
				result.AllocationStatistics,
				statistics => Assert.Equal(0, statistics.SpillFrameBytes));
		}
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
	}

	[Fact]
	public void PortableEnvironmentMembersRemainIndependentlyPayForPlay()
	{
		var newLine = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableEnvironmentNewLineEntry");
		var processorCount = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableEnvironmentProcessorCountEntry");
		var console = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleWriteEntry");
		var fileSystem = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemMissingEntry");
		var unrelated = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DefaultEntry");

		Assert.Contains(
			newLine.Symbols,
			symbol => symbol.Name.EndsWith("EnvironmentPal::GetNewLine", StringComparison.Ordinal));
		Assert.DoesNotContain(
			newLine.Symbols,
			symbol => symbol.Name.EndsWith("EnvironmentPal::GetProcessorCount", StringComparison.Ordinal));
		Assert.DoesNotContain(
			processorCount.Symbols,
			symbol => symbol.Name.Contains("EnvironmentPal", StringComparison.Ordinal));
		Assert.Contains("\tmoveq\t#42,d0", processorCount.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			console.Symbols,
			symbol => symbol.Name.Contains("EnvironmentPal", StringComparison.Ordinal));
		Assert.DoesNotContain(
			fileSystem.Symbols,
			symbol => symbol.Name.Contains("EnvironmentPal", StringComparison.Ordinal));
		Assert.DoesNotContain(
			unrelated.Symbols,
			symbol => symbol.Name.Contains("EnvironmentPal", StringComparison.Ordinal));
	}

	[Fact]
	public void UnusedFrameworkAndPalGroupsContributeNoTargetSymbolsCodeOrData()
	{
		var unrelated = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DefaultEntry");
		var groupMarkers = new[]
		{
			"ShadowString",
			"ShadowStringFormat",
			"ShadowInt32",
			"ShadowUInt32",
			"ShadowInt64",
			"ShadowUInt64",
			"ShadowList",
			"ShadowDictionary",
			"ShadowEnumerable",
			"ManagedPool",
			"ConsolePal",
			"FileSystemPal",
			"ClockPal",
			"EnvironmentPal",
			"CStringEncoding"
		};

		Assert.Empty(unrelated.FrameworkFeatures);
		Assert.DoesNotContain(
			unrelated.Symbols,
			symbol => groupMarkers.Any(marker =>
				symbol.Name.Contains(marker, StringComparison.Ordinal)));
		// Assembly text contains both the code and data sections, so marker
		// absence covers anonymous emission as well as the public symbol table.
		Assert.All(
			groupMarkers,
			marker => Assert.DoesNotContain(
				marker,
				unrelated.Text,
				StringComparison.Ordinal));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableStopwatchUsesRawEClockOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchEntry");
		var bus = CreateHunkBus(result);
		var probe = RegisterClockGateways(
			bus,
			ticks:
			[
				0x0000_0001_ffff_fff0UL,
				0x0000_0002_0000_0010UL,
				0x0000_0002_0000_0042UL
			]);

		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		Assert.Equal(0, probe.CreatePorts);
		Assert.Equal(0, probe.CreateRequests);
		Assert.Equal(1, probe.Opens);
		Assert.Equal(3, probe.Reads);
		Assert.Equal(1, probe.Closes);
		Assert.Equal(0, probe.DeleteRequests);
		Assert.Equal(0, probe.DeletePorts);
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name ==
				AmigaLibraryBaseSymbols.For(global::Amiga.TimerDevice.Name));
		if (target == M68kCpuTarget.M68000)
		{
			Assert.True(
				result.Image.Length <= 5_500,
				$"Stopwatch EClock image grew to {result.Image.Length} bytes.");
			Assert.True(
				result.Code.Length <= 3_900,
				$"Stopwatch EClock code grew to {result.Code.Length} bytes.");
			Assert.True(
				cycles <= 4_700,
				$"Stopwatch EClock path grew to {cycles} MC68000 cycles.");
			Assert.Equal(13, result.AllocationStatistics.Count);
			Assert.Equal(
				0,
				result.AllocationStatistics.Max(static item => item.SpillFrameBytes));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableStopwatchTimestampPreservesThe64BitReturnAbi(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const ulong ticks = 0x1122_3344_5566_7788UL;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchTimestampEntry");
		var bus = CreateHunkBus(result);
		var probe = RegisterClockGateways(bus, ticks: [ticks]);
		uint low = 0;

		var high = Execute(
			bus,
			model,
			HunkLoadAddress + result.EntryPoint,
			afterReturn: state => low = state.D[1]);

		Assert.Equal((uint)(ticks >> 32), high);
		Assert.Equal((uint)(ticks & uint.MaxValue), low);
		Assert.Equal(1, probe.Reads);
		Assert.Equal(1, probe.Closes);
		Assert.Equal(0, probe.DeleteRequests);
		Assert.Equal(0, probe.DeletePorts);
	}

	[Fact]
	public void PortableStopwatchFieldsLinkAndInitializeIndependently()
	{
		const uint frequency = 709_379;
		var frequencyResult = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchFrequencyEntry");
		var frequencyBus = CreateHunkBus(frequencyResult);
		var frequencyProbe = RegisterClockGateways(
			frequencyBus,
			frequency: frequency,
			ticks: [42]);
		uint frequencyLow = 0;
		var frequencyHigh = Execute(
			frequencyBus,
			M68kCpuModel.M68000,
			HunkLoadAddress + frequencyResult.EntryPoint,
			afterReturn: state => frequencyLow = state.D[1]);

		var resolutionResult = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchHighResolutionEntry");
		Assert.Equal(42u, ExecuteHunk(resolutionResult, M68kCpuModel.M68000));

		Assert.Equal(0u, frequencyHigh);
		Assert.Equal(frequency, frequencyLow);
		Assert.Equal(1, frequencyProbe.Reads);
		Assert.Equal(1, frequencyProbe.Closes);
		Assert.Contains(
			frequencyResult.Symbols,
			symbol => symbol.Name.Contains("StopwatchFrequencyField", StringComparison.Ordinal));
		Assert.DoesNotContain(
			frequencyResult.Symbols,
			symbol => symbol.Name.Contains("StopwatchHighResolutionField", StringComparison.Ordinal));
		Assert.Contains(
			resolutionResult.Symbols,
			symbol => symbol.Name.Contains("StopwatchHighResolutionField", StringComparison.Ordinal));
		Assert.DoesNotContain(
			resolutionResult.Symbols,
			symbol => symbol.Name.Contains("ClockPal", StringComparison.Ordinal));
		Assert.DoesNotContain(
			resolutionResult.Symbols,
			symbol => symbol.Name == AmigaLibraryBaseSymbols.For(global::Amiga.TimerDevice.Name));
	}

	[Theory]
	[InlineData("open-failure", 0)]
	[InlineData("null-device", 1)]
	[InlineData("frequency-zero", 1)]
	public void PortableStopwatchFailuresCloseAnOpenedTimerDevice(
		string scenario,
		int closes)
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchInvalidOperationEntry");
		var bus = CreateHunkBus(result);
		var probe = RegisterClockGateways(bus, scenario);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(closes, probe.Closes);
		Assert.Equal(0, probe.CreatePorts);
		Assert.Equal(0, probe.CreateRequests);
		Assert.Equal(0, probe.DeleteRequests);
		Assert.Equal(0, probe.DeletePorts);
		Assert.Equal(scenario == "frequency-zero" ? 1 : 0, probe.Reads);
	}

	[Fact]
	public void FreestandingStopwatchUsesScopedTimerDeviceOwnership()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchTwoTimestampEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Freestanding
		});
		var bus = CreateHunkBus(result);
		var probe = RegisterClockGateways(bus, ticks: [41, 42]);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, probe.CreatePorts);
		Assert.Equal(0, probe.CreateRequests);
		Assert.Equal(2, probe.Opens);
		Assert.Equal(2, probe.Reads);
		Assert.Equal(2, probe.Closes);
		Assert.Equal(0, probe.DeleteRequests);
		Assert.Equal(0, probe.DeletePorts);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableStopwatchInstanceStateMachineRunsOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint allocatorAddress = 0x0000_2800;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchInstanceEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			});
		var bus = CreateHunkBus(result);
		var getAllocationCount = RegisterBumpAllocator(bus, allocatorAddress);
		var probe = RegisterClockGateways(
			bus,
			ticks:
			[
				0x0000_0001_ffff_fff0UL,
				0x0000_0002_0000_000eUL,
				0x0000_0002_0000_002cUL,
				0x0000_0005_ffff_fff0UL,
				0x0000_0006_0000_001aUL,
				0x0000_0008_ffff_fff0UL,
				0x0000_000a_ffff_fff0UL,
				0x0000_000b_0000_001aUL
			]);

		Assert.Equal(
			42u,
			Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, probe.CreatePorts);
		Assert.Equal(0, probe.CreateRequests);
		Assert.Equal(1, probe.Opens);
		Assert.Equal(8, probe.Reads);
		Assert.Equal(1, probe.Closes);
		Assert.Equal(0, probe.DeleteRequests);
		Assert.Equal(0, probe.DeletePorts);
		Assert.Equal(2, getAllocationCount());
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PinnedCoreLibStopwatchInstanceStateMachineRunsOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		const uint allocatorAddress = 0x0000_2800;
		var shadow = Measure(frameworkImplementationPack: null);
		var pinned = Measure(
			new M68kFrameworkImplementationPackOptions(pack.ManifestPath));

		Console.WriteLine(
			$"STOPWATCH-BENCHMARK cpu={target} " +
			$"shadow(image={shadow.Result.Image.Length},code={shadow.Result.Code.Length}," +
			$"cycles={shadow.Cycles},allocations={shadow.Allocations}," +
			$"methods={shadow.Result.AllocationStatistics.Count},spill={shadow.MaxSpill}) " +
			$"pinned(image={pinned.Result.Image.Length},code={pinned.Result.Code.Length}," +
			$"cycles={pinned.Cycles},allocations={pinned.Allocations}," +
			$"methods={pinned.Result.AllocationStatistics.Count},spill={pinned.MaxSpill})");

		Assert.Equal(2, shadow.Allocations);
		Assert.Equal(2, pinned.Allocations);
		Assert.True(
			pinned.Result.Image.Length <= shadow.Result.Image.Length,
			$"Pinned Stopwatch image grew beyond shadow: {pinned.Result.Image.Length} > {shadow.Result.Image.Length} bytes.");
		Assert.True(
			pinned.Result.Code.Length <= shadow.Result.Code.Length,
			$"Pinned Stopwatch code grew beyond shadow: {pinned.Result.Code.Length} > {shadow.Result.Code.Length} bytes.");
		Assert.True(
			pinned.Cycles <= shadow.Cycles,
			$"Pinned Stopwatch execution regressed beyond shadow: {pinned.Cycles} > {shadow.Cycles} cycles.");
		Assert.Contains(
			shadow.Result.Symbols,
			static symbol => symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
		Assert.DoesNotContain(
			pinned.Result.Symbols,
			static symbol => symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
		Assert.DoesNotContain(
			pinned.Result.Symbols,
			static symbol => symbol.Name == "System.Diagnostics.Stopwatch::.cctor");

		(M68kCompilationResult Result, long Cycles, int Allocations, int MaxSpill) Measure(
			M68kFrameworkImplementationPackOptions? frameworkImplementationPack)
		{
			var result = Compile(
				target,
				M68kOutputFormat.Hunk,
				"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchInstanceEntry",
				imports: new Dictionary<string, uint>
				{
					[M68kRuntimeImports.Allocate] = allocatorAddress
				},
				frameworkImplementationPack: frameworkImplementationPack);
			var bus = CreateHunkBus(result);
			var getAllocationCount = RegisterBumpAllocator(bus, allocatorAddress);
			var probe = RegisterClockGateways(
				bus,
				ticks:
				[
					0x0000_0001_ffff_fff0UL,
					0x0000_0002_0000_000eUL,
					0x0000_0002_0000_002cUL,
					0x0000_0005_ffff_fff0UL,
					0x0000_0006_0000_001aUL,
					0x0000_0008_ffff_fff0UL,
					0x0000_000a_ffff_fff0UL,
					0x0000_000b_0000_001aUL
				]);
			long cycles = 0;
			Assert.Equal(
				42u,
				Execute(
					bus,
					model,
					HunkLoadAddress + result.EntryPoint,
					afterReturn: state => cycles = state.Cycles));
			Assert.Equal(8, probe.Reads);
			return (
				result,
				cycles,
				getAllocationCount(),
				result.AllocationStatistics.Max(static item => item.SpillFrameBytes));
		}
	}

	[Fact]
	public void PortableStopwatchResetOnlyDoesNotLinkTheAmigaClock()
	{
		const uint allocatorAddress = 0x0000_2800;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchResetOnlyEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			});
		var bus = CreateHunkBus(result);
		var getAllocationCount = RegisterBumpAllocator(bus, allocatorAddress);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, getAllocationCount());
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.Contains("ClockPal", StringComparison.Ordinal));
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name ==
				AmigaLibraryBaseSymbols.For(global::Amiga.TimerDevice.Name));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PinnedCoreLibTimeSpanIntegerSliceRunsOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		const uint allocatorAddress = 0x0000_2800;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortablePinnedTimeSpanEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			},
			frameworkImplementationPack:
				new M68kFrameworkImplementationPackOptions(pack.ManifestPath));
		var bus = CreateHunkBus(result);
		_ = RegisterBumpAllocator(bus, allocatorAddress);
		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		Console.WriteLine(
			$"TIMESPAN-PINNED cpu={target} image={result.Image.Length} " +
			$"code={result.Code.Length} cycles={cycles}");
	}

	[Fact]
	public void ExperimentalCoreLibExceptionToStringCutPointRunsThroughBackend()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		const uint allocatorAddress = 0x0000_2800;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibExceptionToStringCutPointEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			},
			frameworkImplementationPack:
				new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
				{
					EnableUnlistedManagedBodies = true
				});
		var bus = CreateHunkBus(result);
		_ = RegisterBumpAllocator(bus, allocatorAddress);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Contains(result.Symbols, static symbol =>
			symbol.Name.Contains("ShadowException::ToString", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name.Contains("Exception::get_StackTrace", StringComparison.Ordinal) ||
			symbol.Name.Contains("System.Reflection.", StringComparison.Ordinal));
	}

	[Fact]
	public void CoreLibStringBuilderTraversalClearsReflectionAndReachesGlobalizationBoundary()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		const uint allocatorAddress = 0x0000_2800;
		var exception = Assert.Throws<M68kCompilationException>(() => Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderAppendIntEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			},
			frameworkImplementationPack:
				new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
				{
					EnableUnlistedManagedBodies = true
				}));

		Assert.Equal(M68kDiagnosticIds.UnsupportedSignature, exception.DiagnosticId);
		Assert.Contains("System.OperationCanceledException::_cancellationToken", exception.Message);
		Assert.Contains("System.Threading.CancellationToken", exception.Message);
		Assert.DoesNotContain("System.Reflection.CerHashtable`2", exception.Message);
	}

	[Fact]
	public void CoreLibListTraversalClearsReflectionAndReachesGlobalizationBoundary()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		const uint allocatorAddress = 0x0000_2800;
		var exception = Assert.Throws<M68kCompilationException>(() => Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibListIntEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			},
			frameworkImplementationPack:
				new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
				{
					EnableUnlistedManagedBodies = true
				}));

		Assert.Equal(M68kDiagnosticIds.UnsupportedSignature, exception.DiagnosticId);
		Assert.Contains("System.OperationCanceledException::_cancellationToken", exception.Message);
		Assert.Contains("System.Threading.CancellationToken", exception.Message);
		Assert.DoesNotContain("System.Reflection.CerHashtable`2", exception.Message);
	}

	[Fact]
	public void PinnedCoreLibResetOnlyDoesNotLinkOrOpenTheAmigaClock()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		const uint allocatorAddress = 0x0000_2800;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchResetOnlyEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocatorAddress
			},
			frameworkImplementationPack:
				new M68kFrameworkImplementationPackOptions(pack.ManifestPath));
		var bus = CreateHunkBus(result);
		var getAllocationCount = RegisterBumpAllocator(bus, allocatorAddress);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, getAllocationCount());
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name.Contains("ClockPal", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name == AmigaLibraryBaseSymbols.For(global::Amiga.TimerDevice.Name));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name == "System.Diagnostics.Stopwatch::.cctor");
	}

	private static Func<int> RegisterBumpAllocator(
		TestBus bus,
		uint allocatorAddress)
	{
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(allocatorAddress, state =>
		{
			var size = state.D[0];
			var address = heap;
			heap += (size + 3u) & ~3u;
			Array.Clear(bus.Memory, checked((int)address), checked((int)size));
			allocations++;
			state.D[0] = address;
		});
		return () => allocations;
	}

	[Fact]
	public void PortableStopwatchPalIsAbsentWhenUnreachable()
	{
		var timestamp = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchTimestampEntry");
		var environment = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableEnvironmentEntry");
		var unrelated = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DefaultEntry");

		Assert.Contains(
			timestamp.Symbols,
			symbol => symbol.Name.Contains("ClockPal", StringComparison.Ordinal));
		Assert.DoesNotContain(
			environment.Symbols,
			symbol => symbol.Name.Contains("ClockPal", StringComparison.Ordinal));
		Assert.DoesNotContain(
			unrelated.Symbols,
			symbol => symbol.Name.Contains("ClockPal", StringComparison.Ordinal));
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

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DivisionCorpusMatchesManagedBoundaryAndRandomReference(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const string entry =
			"CopperSharp.Compiler.Tests.CompilerFixtures::DivisionDifferentialCorpusEntry";
		var result = Compile(target, M68kOutputFormat.Hunk, entry);
		Assert.Equal(ReferenceDivisionDifferentialCorpus(), ExecuteHunk(result, model));

		if (target == M68kCpuTarget.M68000)
		{
			var assembly = Compile(target, M68kOutputFormat.Assembly, entry).Text!;
			Assert.Contains(
				"\tadd.l\td0,d0\r\n\taddx.l\td3,d3",
				assembly,
				StringComparison.Ordinal);
			Assert.DoesNotContain("\tbtst\td4,d0", assembly, StringComparison.Ordinal);
			Assert.DoesNotContain("\tbset\td4,d2", assembly, StringComparison.Ordinal);
			Assert.DoesNotContain("divu.w", assembly, StringComparison.Ordinal);
		}
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
	public void LoopCarriedLocalRetainsInitializerAcrossSetupCalls()
	{
		const string entry =
			"CopperSharp.Compiler.Tests.CompilerFixtures::LoopCarriedInitializerEntry";
		var result = Compile(M68kCpuTarget.M68000, M68kOutputFormat.Hunk, entry);

		Assert.Equal(49u, ExecuteHunk(result, M68kCpuModel.M68000));
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

	[Fact]
	public void LongRangeMethodCallPreservesResultWithFourByteTwoCycleCost()
	{
		var near = BuildMethodCall(callOffset: 100);
		var far = BuildMethodCall(callOffset: 32_768);
		var nearCycles = 0L;
		var farCycles = 0L;

		Assert.Equal(42u, ExecuteLinked(near, cycles => nearCycles = cycles));
		Assert.Equal(42u, ExecuteLinked(far, cycles => farCycles = cycles));
		Assert.Equal(0x6100,
			BinaryPrimitives.ReadUInt16BigEndian(near.Linked.Bytes.AsSpan(near.Entry)) &
			0xFF00);
		Assert.Equal(0x4EB9,
			BinaryPrimitives.ReadUInt16BigEndian(far.Linked.Bytes.AsSpan(far.Entry)));
		Assert.Equal(2, near.CallBytes);
		Assert.Equal(6, far.CallBytes);
		Assert.Equal(4, far.CallBytes - near.CallBytes);
		Assert.Equal(nearCycles + 2, farCycles);
		Assert.Empty(near.Linked.Relocations);
		Assert.Single(far.Linked.Relocations);

		static (LinkedCode Linked, int Entry, int CallBytes) BuildMethodCall(
			int callOffset)
		{
			var assembler = new M68kAssembler();
			assembler.Mark("method:callee");
			assembler.EmitWord(0x702A); // MOVEQ #42,D0
			assembler.EmitWord(0x4E75); // RTS
			while (assembler.Offset < callOffset)
			{
				assembler.EmitWord(0x4E71); // Unreached padding.
			}
			assembler.Mark("method:entry");
			assembler.EmitCall("method:callee");
			assembler.Mark("method:after-call");
			assembler.EmitWord(0x4E75); // RTS
			var linked = assembler.Link(
				HunkLoadAddress,
				new Dictionary<string, uint>());
			var entry = linked.Labels["method:entry"];
			return (
				linked,
				entry,
				linked.Labels["method:after-call"] - entry);
		}

		static uint ExecuteLinked(
			(LinkedCode Linked, int Entry, int CallBytes) image,
			Action<long> captureCycles)
		{
			var bus = new TestBus();
			image.Linked.Bytes.CopyTo(bus.Memory.AsSpan((int)HunkLoadAddress));
			return Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + (uint)image.Entry,
				afterReturn: state => captureCycles(state.Cycles));
		}
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

	[Fact]
	public void SingleWordStackArgumentHomePreservesCallerLiveD7OnMc68000()
	{
		const string entry =
			"CopperSharp.Compiler.Tests.CompilerFixtures::" +
			"StackArgumentHomePreservesCallerD7Entry";
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			entry);
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			entry);

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Contains("\taddq.l\t#1,d7", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\td7,-(a7)", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\t(a7)+,d7", assembly.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void SingleWordStackArgumentHomeUsesAnchoredDestinationAcrossD7Scratch()
	{
		const string entry =
			"CopperSharp.Compiler.Tests.CompilerFixtures::" +
			"AnchoredStackArgumentHomePreservesCallerD7Entry";
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			entry);
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			entry);

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Contains("\taddq.l\t#1,d7", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmovea.l\ta7,a5", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\td7,-(a7)", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\td7,36(a5)", assembly.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\td7,40(a5)", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\t(a7)+,d7", assembly.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void MultiwordStackArgumentHomeUsesAnchoredDestinationAcrossD7Scratch()
	{
		const string entry =
			"CopperSharp.Compiler.Tests.CompilerFixtures::" +
			"AnchoredMultiwordStackArgumentHomeEntry";
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			entry);
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			entry);

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Contains("\tmovea.l\ta7,a5", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\td7,-(a7)", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\td7,36(a5)", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\td7,40(a5)", assembly.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\td7,44(a5)", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\t(a7)+,d7", assembly.Text, StringComparison.Ordinal);
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

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void IncomingRegisterAndStackTransfersPreserveBothBanksAndWidePairs(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var (entry, expected) in new[]
		{
			("IncomingDataOverlapEntry", 302u),
			("HybridManagedPointerArgumentsEntry", 42u),
			("IncomingInt64OverlapEntry", 214u)
		})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");

			Assert.Equal(expected, ExecuteHunkWithAllocator(result, model));
		}
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
	public void CompilesAndExecutesGenericMethodBodyFromReferencedModule()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			ManagedAssemblyPaths =
			[
				typeof(CopperSharp.Compiler.Tests.MultiModule.ExternalMethods).Assembly.Location
			],
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::MultiModuleGenericEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name.Contains("::AddOne<", StringComparison.Ordinal));
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
	[InlineData("CrossMethodFinallyCatchEntry")]
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
	public void UnsignedDivisionByZeroRaisesDivideByZeroException()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::UnsignedDivideByZeroCatchEntry");

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

	[Theory]
	[InlineData(
		"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteUnhandledIOExceptionEntry",
		15,
		-3)]
	[InlineData(
		"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteUnhandledDirectoryNotFoundEntry",
		16,
		0)]
	[InlineData(
		"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteUnhandledUnauthorizedEntry",
		17,
		2)]
	[InlineData(
		"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileGetAttributesUnhandledFileNotFoundEntry",
		18,
		0)]
	public void ExtendedUnhandledExceptionsReportStableReasons(
		string entryPoint,
		int expectedReason,
		int entryType)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_3400;
		const uint intuitionBase = 0x0000_3800;
		const uint unhandledHook = 0x0000_2E00;
		const uint nativePath = 0x0000_6000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			entryPoint,
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.UnhandledException] = unhandledHook
			});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var observedReason = -1;
		var frees = 0;
		var unlocks = 0;
		var closedBases = new List<uint>();
		bus.RegisterGateway(execBase - 198, state => state.D[0] = nativePath);
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
		{
			state.D[0] = ReadCString(bus, state.A[1]) switch
			{
				"dos.library" => dosBase,
				"intuition.library" => intuitionBase,
				var name => throw new Xunit.Sdk.XunitException(
					$"Unexpected library '{name}'.")
			};
		});
		bus.RegisterGateway(execBase - 414, state => closedBases.Add(state.A[1]));
		bus.RegisterGateway(dosBase - 84, state =>
			state.D[0] = entryType == 0 ? 0u : 0x100u);
		bus.RegisterGateway(dosBase - 102, state =>
		{
			Assert.Equal(0x100u, state.D[1]);
			var offset = checked((int)state.D[2] + global::Amiga.FileInfoBlock.DirEntryTypeOffset);
			var rawEntryType = unchecked((uint)entryType);
			bus.Memory[offset] = (byte)(rawEntryType >> 24);
			bus.Memory[offset + 1] = (byte)(rawEntryType >> 16);
			bus.Memory[offset + 2] = (byte)(rawEntryType >> 8);
			bus.Memory[offset + 3] = (byte)rawEntryType;
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, _ => unlocks++);
		bus.RegisterGateway(dosBase - 132, state =>
			state.D[0] = (uint)global::Amiga.DOS.Error.ObjectNotFound);
		bus.RegisterGateway(unhandledHook, state =>
			observedReason = unchecked((int)state.D[0]));
		bus.RegisterGateway(intuitionBase - 348, state => state.D[0] = 1);

		var returnValue = Execute(
			bus,
			M68kCpuModel.M68000,
			HunkLoadAddress + result.EntryPoint);
		Assert.Equal(expectedReason, observedReason);
		Assert.Equal(20u, returnValue);
		Assert.Equal(1, frees);
		Assert.Equal(entryType == 0 ? 0 : 1, unlocks);
		Assert.Equal([dosBase, intuitionBase], closedBases);
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

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void VariableShiftsUseRegisterCountsAndMatchCilMasking(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var (entryPoint, expected) in new[]
		{
			("VariableShiftCorpusEntry", CompilerFixtures.VariableShiftCorpusEntry()),
			("VariableShiftDifferentialEntry", CompilerFixtures.VariableShiftDifferentialEntry())
		})
		{
			var result = Compile(
				target,
				M68kOutputFormat.Assembly,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entryPoint}");

			Assert.Equal(expected, ExecuteHunk(result, model));
			Assert.DoesNotContain("shift_loop", result.Text, StringComparison.Ordinal);
			Assert.DoesNotContain(
				"andi.l\t#$0000001F",
				result.Text,
				StringComparison.Ordinal);
			Assert.Matches(@"\t(?:lsl|lsr|asr)\.l\td[0-7],d[0-7]", result.Text);
		}
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
	public void BooleanPhiThreadingPreservesCompanionPhiDefinitions()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::BooleanPhiWithCompanionValuesEntry");

		Assert.Equal(332u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void AssignedLocalsDoNotEmitEntryClears()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ManyAssignedLocalsEntry");

		Assert.DoesNotMatch(@"\tclr\.l\t\d+\(a7\)", result.Text);
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
		Assert.Contains("\tlea\t12(a7),a7", result.Text, StringComparison.Ordinal);
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
	public void LargeStackArgumentDiscardUsesSingleLea()
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

		Assert.Matches(@"\tlea\t\d+\(a7\),a7", result.Text);
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
	public void AmigaFallthroughEntryDoesNotPreserveInternalCalleeSavedRegisters()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::PromotedAptrLocalAcrossExecCall",
			OutputFormat = M68kOutputFormat.Assembly
		});
		var assembly = result.Text!;
		var methodStart = assembly.IndexOf("\r\nC68K_method_", StringComparison.Ordinal);
		Assert.True(methodStart >= 0);
		var blockStart = assembly.IndexOf(
			"\r\nC68K_method_",
			methodStart + 2,
			StringComparison.Ordinal);
		Assert.True(blockStart > methodStart);
		var entryPrologue = assembly[methodStart..blockStart];

		Assert.DoesNotContain("movem.l", entryPrologue, StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmove\.l\t(?:d[2-7]|a[2-6]),-\(a7\)",
			entryPrologue);
	}

	[Fact]
	public void FullExceptionAmigaRootEntryDoesNotPreserveInternalCalleeSavedRegisters()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::CrossMethodFinallyCatchEntry",
			ExceptionMode = M68kExceptionMode.Full,
			OutputFormat = M68kOutputFormat.Assembly
		});
		var assembly = result.Text!;
		var methodStart = assembly.IndexOf("\r\nC68K_method_", StringComparison.Ordinal);
		Assert.True(methodStart >= 0);
		var blockStart = assembly.IndexOf(
			"\r\nC68K_method_",
			methodStart + 2,
			StringComparison.Ordinal);
		Assert.True(blockStart > methodStart);
		var entryPrologue = assembly[methodStart..blockStart];

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.DoesNotContain("movem.l", entryPrologue, StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmove\.l\t(?:d[2-7]|a[2-6]),-\(a7\)",
			entryPrologue);
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
	public void TransparentScalarInstanceReceiversMatchCalleeAbi(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::TransparentScalarInstanceReceiverEntry",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
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
			"\tmove.l\t$0004.w,_ExecBase",
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
		Assert.Contains("\tmovea.l\t_ExecBase(pc),a6", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void TransparentScalarLocalDoesNotClobberCachedPlatformBaseRegister(
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
		// The machine optimizer may now fold the transparent APTR local to the
		// final D0 constant instead of materializing it in an address register.
		// Keep the ABI invariant: A4 is the resolver's cached platform base and
		// must never receive the local pointer value.
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

	[Fact]
	public void AllocatedIncomingA6ReloadsInferredExecBase()
	{
		const uint execBase = 0x0000_3600;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::PlatformBaseIncomingA6Entry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var typeOfMemCalls = 0;
		var freeMemCalls = 0;
		bus.RegisterGateway(execBase - 534, state =>
		{
			typeOfMemCalls++;
			Assert.Equal(execBase, state.A[6]);
			Assert.Equal(0x0000_1800u, state.A[1]);
			state.D[0] = 14;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			freeMemCalls++;
			Assert.Equal(execBase, state.A[6]);
			Assert.Equal(0x0000_1800u, state.A[1]);
			Assert.Equal(4u, state.D[0]);
		});

		Assert.Equal(
			42u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, typeOfMemCalls);
		Assert.Equal(1, freeMemCalls);
		Assert.Contains(
			"\tmovea.w\t#$0005,a6",
			result.Text,
			StringComparison.Ordinal);
		Assert.Matches(
			@"\tmovea\.l\t_ExecBase(?:\(pc\))?,a6\r?\n\tjsr\t-210\(a6\)",
			result.Text!);
		Assert.Matches(
			@"\tpea\t\(a6\)[\s\S]*" +
			@"\tmovea\.l\t\(a7\)\+,a6\r?\n\tmove\.l\t\(a7\)\+,d2\r?\n\trts",
			result.Text!);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PlatformBaseEntryProofRespectsSameDifferentAndUnknownMerges(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		AssertCase("PlatformBaseSameMergeEntry", 20, expectedALoads: 3, expectedBLoads: 0);
		AssertCase("PlatformBaseDifferentMergeEntry", 30, expectedALoads: 2, expectedBLoads: 1);
		AssertCase("PlatformBaseUnknownMergeEntry", 11, expectedALoads: 2, expectedBLoads: 0);
		AssertCase("PlatformBasePreservedAcrossInternalCallEntry", 27, expectedALoads: 2, expectedBLoads: 0);
		AssertCase("PlatformBaseTailCallEntry", 10, expectedALoads: 2, expectedBLoads: 0);
		AssertCase(
			"PlatformBaseNestedFinallyEntry",
			10,
			expectedALoads: 2,
			expectedBLoads: 1,
			exceptionMode: M68kExceptionMode.Full);

		void AssertCase(
			string method,
			uint expectedResult,
			int expectedALoads,
			int expectedBLoads,
			M68kExceptionMode exceptionMode = M68kExceptionMode.Yolo)
		{
			const uint baseA = 0x0000_4000;
			const uint baseB = 0x0000_5000;
			const uint selectorBase = 0x0000_6000;
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{method}",
				Cpu = target,
				ExceptionMode = exceptionMode,
				OutputFormat = M68kOutputFormat.Assembly,
				ExternalCallResolvers = new[] { new PlatformBaseStateResolver(baseA, baseB) }
			});
			var bus = CreateHunkBus(result);
			bus.RegisterGateway(baseA - 30, state =>
			{
				Assert.Equal(baseA, state.A[6]);
				state.D[0] = 10;
			});
			bus.RegisterGateway(baseB - 30, state =>
			{
				Assert.Equal(baseB, state.A[6]);
				state.D[0] = 20;
			});
			bus.RegisterGateway(selectorBase - 30, state =>
			{
				Assert.Equal(selectorBase, state.A[6]);
				state.D[0] = 0;
			});

			Assert.Equal(expectedResult, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
			Assert.Equal(
				expectedALoads,
				Regex.Matches(
					result.Text!,
					@"\tmovea\.(?:w\t#\$4000|l\t#\$00004000),a6").Count);
			Assert.Equal(
				expectedBLoads,
				Regex.Matches(
					result.Text!,
					@"\tmovea\.(?:w\t#\$5000|l\t#\$00005000),a6").Count);
		}
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
		var sampleAssembly = Path.Combine(AppContext.BaseDirectory, "MUISunflower.dll");
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = sampleAssembly,
			EntryPoint = "MUISunflower.Program::Main",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			Imports = new Dictionary<string, uint>
			{
				["amiga.boopsi.DoMethodA"] = 0x0000_2600
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
			"\tjsr\tC68K_amiga_002Eboopsi_002EDoMethodA\r\n\tlea\t12(a7),a7\r\n\taddq.l\t#8,a7\r\n\trts",
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
			"\tmovea.l\t_IFFParseLibraryBase(pc),a1",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"\tmovea.l\t_DOSLibraryBase(pc),a1",
			result.Text,
			StringComparison.Ordinal);
		Assert.Matches(
			@"\tmovea\.l\t_IFFParseLibraryBase\(pc\),a1\r?\n" +
			@"\tmovea\.l\t_ExecBase\(pc\),a6\r?\n" +
			@"\tjsr\t-414\(a6\)",
			result.Text);
		Assert.Matches(
			@"\tmovea\.l\t_DOSLibraryBase\(pc\),a1\r?\n" +
			@"\tmovea\.l\t_ExecBase\(pc\),a6\r?\n" +
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
		Assert.Matches(
			@"\tmove\.l\t(?<field>C68K_static_003ACopperSharp_002ERuntime_002EManaged_003A[0-9A-F]+)\(pc\),d1\r?\n" +
			@"\taddq\.l\t#1,d1\r?\n" +
			@"\tmove\.l\td1,\k<field>",
			result.Text);
		Assert.Matches(
			@"\tadd\.l\td2,C68K_static_003ACopperSharp_002ERuntime_002EManaged_003A[0-9A-F]+",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t(?<field>C68K_static_[^\r\n]+)\(pc\),(?<loaded>d[0-7])\r?\n" +
			@"(?:\tmoveq\t#[^\r\n]+,(?<delta>d[0-7])\r?\n)?" +
			@"\tadd\.l\t(?:\k<loaded>,\k<delta>|\k<delta>,\k<loaded>)\r?\n" +
			@"\tmove\.l\t(?:\k<loaded>|\k<delta>),\k<field>",
			result.Text);
		Assert.Matches(
			@"\tmoveq\t#16,(?<delta>d[0-7])\r?\n" +
			@"\tadd\.l\t\k<delta>,(?<cursor>d[0-7])\r?\n" +
			@"\tmove\.l\t\k<cursor>,(?<limit>d[0-7])\r?\n" +
			@"\tsub\.l\t\k<delta>,(?<remaining>d[0-7])",
			result.Text);
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

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void IffInspectSampleExecutesSuccessAndNestedCleanup(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_3800;
		const uint iffBase = 0x0000_4000;
		const uint path = 0x0000_0800;
		const uint file = 0x0000_1234;
		const uint iff = 0x0002_0000;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(IFFInspect.Program).Assembly.Location,
			EntryPoint = "IFFInspect.Program::Main",
			Cpu = target,
			ExceptionMode = M68kExceptionMode.Full,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x000F_0000
			}
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		System.Text.Encoding.ASCII.GetBytes("fixture.iff\0").CopyTo(
			bus.Memory.AsSpan(checked((int)path)));
		var openedLibraries = new List<string>();
		var closedLibraries = new List<uint>();
		var events = new List<string>();
		var heap = 0x0003_0000u;
		bus.RegisterGateway(0x000F_0000, state =>
		{
			var size = state.D[0];
			state.D[0] = heap;
			heap += (size + 3) & ~3u;
		});
		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal(33u, state.D[0]);
			var name = ReadCString(bus, state.A[1]);
			openedLibraries.Add(name);
			state.D[0] = name switch
			{
				"dos.library" => dosBase,
				"iffparse.library" => iffBase,
				_ => 0
			};
		});
		bus.RegisterGateway(execBase - 414, state =>
			closedLibraries.Add(state.A[1]));
		bus.RegisterGateway(dosBase - 30, state =>
		{
			Assert.Equal(path, state.D[1]);
			Assert.Equal("fixture.iff", ReadCString(bus, state.D[1]));
			Assert.Equal((uint)global::Amiga.DOS.FileMode.OldFile, state.D[2]);
			events.Add("open-file");
			state.D[0] = file;
		});
		bus.RegisterGateway(dosBase - 36, state =>
		{
			Assert.Equal(file, state.D[1]);
			events.Add("close-file");
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 948, state =>
		{
			Assert.Equal("IFF stream is valid\n", ReadCString(bus, state.D[1]));
			events.Add("put-valid");
			state.D[0] = 1;
		});
		bus.RegisterGateway(iffBase - 30, state =>
		{
			events.Add("alloc-iff");
			state.D[0] = iff;
		});
		bus.RegisterGateway(iffBase - 234, state =>
		{
			Assert.Equal(iff, state.A[0]);
			Assert.Equal(file, bus.ReadLong(iff));
			events.Add("init-iff");
		});
		bus.RegisterGateway(iffBase - 36, state =>
		{
			Assert.Equal(iff, state.A[0]);
			Assert.Equal((uint)global::Amiga.IffParse.IFFF_READ, state.D[0]);
			events.Add("open-iff");
			state.D[0] = 0;
		});
		bus.RegisterGateway(iffBase - 42, state =>
		{
			Assert.Equal(iff, state.A[0]);
			Assert.Equal((uint)global::Amiga.IffParse.IFFPARSE_SCAN, state.D[0]);
			events.Add("parse-iff");
			state.D[0] = unchecked((uint)(int)global::Amiga.IffError.Eof);
		});
		bus.RegisterGateway(iffBase - 48, state =>
		{
			Assert.Equal(iff, state.A[0]);
			events.Add("close-iff");
		});
		bus.RegisterGateway(iffBase - 54, state =>
		{
			Assert.Equal(iff, state.A[0]);
			events.Add("free-iff");
		});

		Assert.Equal(
			(uint)global::Amiga.DOS.RETURN_OK,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				initialize: state =>
				{
					state.D[0] = 1;
					state.A[0] = path;
				},
				maxInstructions: 1_000_000));
		Assert.Equal(["dos.library", "iffparse.library"], openedLibraries);
		Assert.Equal([iffBase, dosBase], closedLibraries);
		Assert.Equal(
			["open-file", "alloc-iff", "init-iff", "open-iff", "parse-iff",
				"close-iff", "free-iff", "close-file", "put-valid"],
			events);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(5)]
	public void IffInspectSampleEarlyFailuresPreserveCleanupOrder(int failureStage)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_3800;
		const uint iffBase = 0x0000_4000;
		const uint path = 0x0000_0800;
		const uint file = 0x0000_1234;
		const uint iff = 0x0002_0000;
		const uint allocator = 0x000F_0000;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(IFFInspect.Program).Assembly.Location,
			EntryPoint = "IFFInspect.Program::Main",
			Cpu = M68kCpuTarget.M68000,
			ExceptionMode = M68kExceptionMode.Full,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = allocator
			}
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		System.Text.Encoding.ASCII.GetBytes("fixture.iff\0").CopyTo(
			bus.Memory.AsSpan(checked((int)path)));
		var events = new List<string>();
		var heap = 0x0003_0000u;
		bus.RegisterGateway(allocator, state =>
		{
			var size = state.D[0];
			state.D[0] = heap;
			heap += (size + 3) & ~3u;
		});
		bus.RegisterGateway(execBase - 552, state =>
		{
			var name = ReadCString(bus, state.A[1]);
			events.Add(name == "dos.library" ? "open-lib:dos" : "open-lib:iff");
			state.D[0] = name switch
			{
				"dos.library" when failureStage == 0 => 0,
				"dos.library" => dosBase,
				"iffparse.library" when failureStage == 1 => 0,
				"iffparse.library" => iffBase,
				_ => 0
			};
		});
		bus.RegisterGateway(execBase - 414, state =>
			events.Add(state.A[1] == iffBase ? "close-lib:iff" : "close-lib:dos"));
		bus.RegisterGateway(dosBase - 30, state =>
		{
			events.Add("open-file");
			state.D[0] = failureStage == 2 ? 0 : file;
		});
		bus.RegisterGateway(dosBase - 132, state =>
		{
			events.Add("ioerr");
			state.D[0] = (uint)global::Amiga.DOS.Error.ObjectNotFound;
		});
		bus.RegisterGateway(dosBase - 36, state =>
		{
			Assert.Equal(file, state.D[1]);
			events.Add("close-file");
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 948, state =>
		{
			var text = ReadCString(bus, state.D[1]);
			events.Add(text == "Cannot open iffparse.library\n" ? "put:no-iff" : "put:failed");
			state.D[0] = 1;
		});
		bus.RegisterGateway(iffBase - 30, state =>
		{
			events.Add("alloc-iff");
			state.D[0] = failureStage == 3 ? 0 : iff;
		});
		bus.RegisterGateway(iffBase - 234, state =>
		{
			Assert.Equal(file, bus.ReadLong(iff));
			events.Add("init-iff");
		});
		bus.RegisterGateway(iffBase - 36, state =>
		{
			events.Add("open-iff");
			state.D[0] = failureStage == 4 ? unchecked((uint)-99) : 0;
		});
		bus.RegisterGateway(iffBase - 42, state =>
		{
			events.Add("parse-iff");
			state.D[0] = failureStage == 5
				? unchecked((uint)-99)
				: unchecked((uint)(int)global::Amiga.IffError.Eof);
		});
		bus.RegisterGateway(iffBase - 48, _ => events.Add("close-iff"));
		bus.RegisterGateway(iffBase - 54, _ => events.Add("free-iff"));
		var actual = Execute(
			bus,
			M68kCpuModel.M68000,
			HunkLoadAddress + result.EntryPoint,
			initialize: state =>
			{
				state.D[0] = 1;
				state.A[0] = path;
			},
			maxInstructions: 1_000_000);
		var expected = failureStage switch
		{
			0 => new[] { "open-lib:dos" },
			1 => ["open-lib:dos", "open-lib:iff", "put:no-iff", "close-lib:dos"],
			2 => ["open-lib:dos", "open-lib:iff", "open-file", "ioerr", "put:failed",
				"close-lib:iff", "close-lib:dos"],
			3 => ["open-lib:dos", "open-lib:iff", "open-file", "alloc-iff", "close-file",
				"put:failed", "close-lib:iff", "close-lib:dos"],
			4 => ["open-lib:dos", "open-lib:iff", "open-file", "alloc-iff", "init-iff",
				"open-iff", "free-iff", "close-file", "put:failed", "close-lib:iff",
				"close-lib:dos"],
			_ => new[] { "open-lib:dos", "open-lib:iff", "open-file", "alloc-iff",
				"init-iff", "open-iff", "parse-iff", "close-iff", "free-iff",
				"close-file", "put:failed", "close-lib:iff", "close-lib:dos" }
		};
		Assert.Equal(expected, events);
		Assert.Equal(
			failureStage is 0 or 1 or 3
				? (uint)global::Amiga.DOS.RETURN_FAIL
				: (uint)global::Amiga.DOS.RETURN_ERROR,
			actual);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DosSampleExecutesDirectoryTraversalAndExactPrintfVector(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_3800;
		const uint directoryLock = 0x0000_1234;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "DOS.dll"),
			EntryPoint = "DOSExample.Program::Main",
			Cpu = target,
			ExceptionMode = M68kExceptionMode.Yolo,
			OutputFormat = M68kOutputFormat.Hunk
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var exNextCalls = 0;
		var ioErrCalls = 0;
		var unlocks = 0;
		var closes = 0;
		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal("dos.library", ReadCString(bus, state.A[1]));
			Assert.Equal(0u, state.D[0]);
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, state =>
		{
			Assert.Equal(dosBase, state.A[1]);
			closes++;
		});
		bus.RegisterGateway(dosBase - 84, state =>
		{
			Assert.Equal(string.Empty, ReadCString(bus, state.D[1]));
			Assert.Equal(unchecked((uint)(int)global::Amiga.DOS.LockMode.Shared), state.D[2]);
			state.D[0] = directoryLock;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			Assert.Equal(directoryLock, state.D[1]);
			Array.Clear(bus.Memory, checked((int)state.D[2]), global::Amiga.FileInfoBlock.SizeInBytes);
			bus.WriteLong(
				state.D[2] + global::Amiga.FileInfoBlock.DirEntryTypeOffset,
				2);
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 108, state =>
		{
			Assert.Equal(directoryLock, state.D[1]);
			var fib = state.D[2];
			if (exNextCalls++ == 0)
			{
				var name = System.Text.Encoding.ASCII.GetBytes("example.bin\0");
				name.CopyTo(bus.Memory.AsSpan(
					checked((int)(fib + global::Amiga.FileInfoBlock.FileNameOffset))));
				bus.WriteLong(fib + global::Amiga.FileInfoBlock.SizeOffset, 12345);
				bus.WriteLong(fib + global::Amiga.FileInfoBlock.DateDaysOffset, 77);
				bus.WriteLong(fib + global::Amiga.FileInfoBlock.DateMinuteOffset, 88);
				bus.WriteLong(fib + global::Amiga.FileInfoBlock.DateTickOffset, 99);
				state.D[0] = 1;
			}
			else
			{
				state.D[0] = 0;
			}
		});
		bus.RegisterGateway(dosBase - 954, state =>
		{
			Assert.Equal("%-30s %10ld  %ld/%ld/%ld\n", ReadCString(bus, state.D[1]));
			var arguments = state.D[2];
			Assert.Equal("example.bin", ReadCString(bus, bus.ReadLong(arguments)));
			Assert.Equal(12345u, bus.ReadLong(arguments + 4));
			Assert.Equal(77u, bus.ReadLong(arguments + 8));
			Assert.Equal(88u, bus.ReadLong(arguments + 12));
			Assert.Equal(99u, bus.ReadLong(arguments + 16));
			state.D[0] = 0;
		});
		bus.RegisterGateway(dosBase - 132, state =>
		{
			ioErrCalls++;
			state.D[0] = (uint)global::Amiga.DOS.Error.NoMoreEntries;
		});
		bus.RegisterGateway(dosBase - 90, state =>
		{
			Assert.Equal(directoryLock, state.D[1]);
			unlocks++;
		});

		Assert.Equal(
			0u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				initialize: state => state.D[0] = 0));
		Assert.Equal(2, exNextCalls);
		Assert.Equal(1, ioErrCalls);
		Assert.Equal(1, unlocks);
		Assert.Equal(1, closes);
	}

	[Fact]
	public void DosSampleUsesBoundedLoopForFileInfoBlockInitialization()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "DOS.dll"),
			EntryPoint = "DOSExample.Program::Main",
			Cpu = M68kCpuTarget.M68000,
			ExceptionMode = M68kExceptionMode.Yolo,
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Matches(
			@"\tlea\t\(a7\),a([0-6])\r?\n" +
			@"\tmoveq\t#65,d([0-7])\r?\n" +
			@"[^:]+:\r?\n" +
			@"\tmove\.l\t#\$00000000,\(a\1\)\+\r?\n" +
			@"\tdbra\td\2,",
			result.Text!);
		Assert.DoesNotMatch(
			@"(?:\tmove\.l\t#\$00000000,\d+\(a7\)\r?\n){12}",
			result.Text!);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void DosSampleFailsWithoutUsingOrLeakingUnavailableResources(
		bool failLibraryOpen)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_3800;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "DOS.dll"),
			EntryPoint = "DOSExample.Program::Main",
			Cpu = M68kCpuTarget.M68000,
			ExceptionMode = M68kExceptionMode.Yolo,
			OutputFormat = M68kOutputFormat.Hunk
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var closes = 0;
		var lockCalls = 0;
		var prints = 0;
		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal("dos.library", ReadCString(bus, state.A[1]));
			state.D[0] = failLibraryOpen ? 0 : dosBase;
		});
		bus.RegisterGateway(execBase - 414, state =>
		{
			Assert.Equal(dosBase, state.A[1]);
			closes++;
		});
		bus.RegisterGateway(dosBase - 84, state =>
		{
			lockCalls++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(dosBase - 132, state =>
			state.D[0] = (uint)global::Amiga.DOS.Error.ObjectNotFound);
		bus.RegisterGateway(dosBase - 954, state =>
		{
			Assert.Equal("Cannot lock path, IoErr %ld\n", ReadCString(bus, state.D[1]));
			Assert.Equal(
				(uint)global::Amiga.DOS.Error.ObjectNotFound,
				bus.ReadLong(state.D[2]));
			prints++;
			state.D[0] = 0;
		});

		Assert.Equal(
			(uint)global::Amiga.DOS.RETURN_FAIL,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint,
				initialize: state => state.D[0] = 0));
		Assert.Equal(failLibraryOpen ? 0 : 1, lockCalls);
		Assert.Equal(failLibraryOpen ? 0 : 1, prints);
		Assert.Equal(failLibraryOpen ? 0 : 1, closes);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PolymorphismSampleExecutesClassAndInterfaceDispatch(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var sampleAssembly = Path.Combine(AppContext.BaseDirectory, "Polymorphism.dll");
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = sampleAssembly,
			EntryPoint = "PolymorphismExample.Program::Main",
			Cpu = target,
			ExceptionMode = M68kExceptionMode.Yolo,
			OutputFormat = M68kOutputFormat.Hunk,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});

		var bus = CreateHunkBus(result);
		var heap = 0x0001_0000u;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			var size = state.D[0];
			state.D[0] = heap;
			heap += (size + 3) & ~3u;
		});
		Assert.Equal(
			21u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				initialize: state => state.D[0] = 0));

		bus = CreateHunkBus(result);
		heap = 0x0001_0000u;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			var size = state.D[0];
			state.D[0] = heap;
			heap += (size + 3) & ~3u;
		});
		Assert.Equal(
			52u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				initialize: state => state.D[0] = 1));

		var additionConstructor = Assert.Single(result.Symbols.Where(symbol =>
			symbol.Name.Contains("PolymorphismExample.Addition::.ctor", StringComparison.Ordinal)));
		var multiplicationConstructor = Assert.Single(result.Symbols.Where(symbol =>
			symbol.Name.Contains("PolymorphismExample.Multiplication::.ctor", StringComparison.Ordinal)));
		Assert.Equal(additionConstructor.Address, multiplicationConstructor.Address);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ExactDirectBodiesFoldButAddressTakenMethodsRemainDistinct(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var folded = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::IdenticalDirectBodyFoldEntry",
			exceptionMode: M68kExceptionMode.Yolo);
		var foldedFirst = Assert.Single(folded.Symbols.Where(symbol =>
			symbol.Name.EndsWith("IdenticalDirectBodyA", StringComparison.Ordinal)));
		var foldedSecond = Assert.Single(folded.Symbols.Where(symbol =>
			symbol.Name.EndsWith("IdenticalDirectBodyB", StringComparison.Ordinal)));
		Assert.Equal(foldedFirst.Address, foldedSecond.Address);
		Assert.Equal(
			44u,
			Execute(
				CreateHunkBus(folded),
				model,
				HunkLoadAddress + folded.EntryPoint));

		var addressTaken = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::IdenticalAddressTakenBodiesEntry");
		var addressTakenFirst = Assert.Single(addressTaken.Symbols.Where(symbol =>
			symbol.Name.EndsWith("IdenticalAddressTakenBodyA", StringComparison.Ordinal)));
		var addressTakenSecond = Assert.Single(addressTaken.Symbols.Where(symbol =>
			symbol.Name.EndsWith("IdenticalAddressTakenBodyB", StringComparison.Ordinal)));
		Assert.NotEqual(addressTakenFirst.Address, addressTakenSecond.Address);
		Assert.Equal(
			17u,
			ExecuteHunkWithAllocator(addressTaken, model));
	}

	[Fact]
	public void FullExceptionIdenticalBodiesKeepDistinctUnwindIdentity()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::IdenticalDirectBodyFoldEntry");
		var first = Assert.Single(result.Symbols.Where(symbol =>
			symbol.Name.EndsWith("IdenticalDirectBodyA", StringComparison.Ordinal)));
		var second = Assert.Single(result.Symbols.Where(symbol =>
			symbol.Name.EndsWith("IdenticalDirectBodyB", StringComparison.Ordinal)));

		Assert.NotEqual(first.Address, second.Address);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConsoleIoSampleEchoesLatin1AndFormatsCountAcrossCpuTargets(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var output = ExecuteConsoleIoSample(
			target,
			model,
			[(byte)'A', 0xe4, (byte)'B', (byte)'\n'],
			0,
			8);

		Assert.Equal(
			(byte[])
			[
				.. System.Text.Encoding.ASCII.GetBytes(
					"Console input/output example\nType text: A"),
				0xe4,
				.. System.Text.Encoding.ASCII.GetBytes(
					"B\nCharacters read: 3\n")
			],
			output);
	}

	[Fact]
	public void ConsoleIoSampleStopsAtCarriageReturnWithoutEchoingBufferedLineFeed()
	{
		var output = ExecuteConsoleIoSample(
			M68kCpuTarget.M68000,
			M68kCpuModel.M68000,
			[(byte)'Z', (byte)'\r', (byte)'\n'],
			0,
			6);

		Assert.Equal(
			"Console input/output example\nType text: Z\nCharacters read: 1\n",
			System.Text.Encoding.Latin1.GetString(output));
	}

	[Fact]
	public void ConsoleIoSampleReturnsFailureAtEndOfFileAndStillShutsDown()
	{
		var output = ExecuteConsoleIoSample(
			M68kCpuTarget.M68000,
			M68kCpuModel.M68000,
			[],
			5,
			2);

		Assert.Equal(
			"Console input/output example\nType text: ",
			System.Text.Encoding.Latin1.GetString(output));
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
		Assert.DoesNotContain("MUITaskList.Program::One", result.Map, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MuiTaskListExecutesInitialListTagVector(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint intuitionBase = 0x0000_3400;
		const uint muiBase = 0x0000_3800;
		const uint appClass = 0x0000_6000;
		const uint nativeClass = 0x0000_7000;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(MUITaskList.Program).Assembly.Location,
			EntryPoint = "MUITaskList.Program::Main",
			Cpu = target,
			ExceptionMode = M68kExceptionMode.Yolo,
			OutputFormat = M68kOutputFormat.Hunk,
			Imports = new Dictionary<string, uint>
			{
				["amiga.boopsi.DoMethodA"] = 0x0000_2600,
				["amiga.boopsi.DoSuperMethodA"] = 0x0000_2700
			}
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		bus.WriteLong(appClass, nativeClass);
		var opened = new List<string>();
		var closed = new List<uint>();
		var allocation = 0;
		var frees = 0;
		var newObjects = 0;
		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal(0u, state.D[0]);
			var library = ReadCString(bus, state.A[1]);
			opened.Add(library);
			state.D[0] = library switch
			{
				global::Amiga.MUIMaster.Name => muiBase,
				global::Amiga.Intuition.Name => intuitionBase,
				_ => 0
			};
		});
		bus.RegisterGateway(execBase - 414, state => closed.Add(state.A[1]));
		bus.RegisterGateway(execBase - 198, state =>
		{
			Assert.Equal(12u, state.D[0]);
			state.D[0] = 0x0000_5000u + checked((uint)allocation++ * 0x10u);
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(muiBase - 108, state => state.D[0] = appClass);
		bus.RegisterGateway(muiBase - 114, _ => { });
		bus.RegisterGateway(muiBase - 120, state =>
			state.D[0] = 0x0000_6800u + checked((uint)newObjects++ * 0x100u));
		bus.RegisterGateway(muiBase - 30, state =>
		{
			var tags = state.A[1];
			if (newObjects == 0)
			{
				Assert.Equal(global::Amiga.MUI.List.Format, bus.ReadLong(tags));
				Assert.Equal(
					"BAR,WEIGHT=50,BAR,WEIGHT=30,WEIGHT=20",
					ReadCString(bus, bus.ReadLong(tags + 4)));
				Assert.Equal(global::Amiga.MUI.List.Title, bus.ReadLong(tags + 8));
				Assert.Equal(1u, bus.ReadLong(tags + 12));
				Assert.Equal(global::Amiga.MUI.List.DisplayHook, bus.ReadLong(tags + 16));
				var hook = bus.ReadLong(tags + 20);
				Assert.NotEqual(0u, hook);
				Assert.Equal(global::Amiga.MUI.Tag.Done, bus.ReadLong(tags + 24));
				Assert.NotEqual(hook, bus.ReadLong(tags + 24));
			}
			state.D[0] = 0x0000_6100u + checked((uint)newObjects++ * 0x100u);
		});
		bus.RegisterGateway(intuitionBase - 636, state => state.D[0] = 0);

		Assert.Equal(20u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(3, allocation);
		Assert.Equal(3, frees);
		Assert.True(newObjects >= 6);
		Assert.Equal(
			[global::Amiga.MUIMaster.Name, global::Amiga.Intuition.Name],
			opened);
		Assert.Equal([intuitionBase, muiBase], closed);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	public void MuiTaskListCleansUpPartiallyOpenedLibraries(int failingOpenIndex)
	{
		const uint execBase = 0x0000_3000;
		const uint muiBase = 0x0000_3800;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(MUITaskList.Program).Assembly.Location,
			EntryPoint = "MUITaskList.Program::Main",
			Cpu = M68kCpuTarget.M68000,
			ExceptionMode = M68kExceptionMode.Yolo,
			OutputFormat = M68kOutputFormat.Hunk,
			Imports = new Dictionary<string, uint>
			{
				["amiga.boopsi.DoMethodA"] = 0x0000_2600,
				["amiga.boopsi.DoSuperMethodA"] = 0x0000_2700
			}
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opened = new List<string>();
		var closed = new List<uint>();
		bus.RegisterGateway(execBase - 552, state =>
		{
			opened.Add(ReadCString(bus, state.A[1]));
			var openIndex = opened.Count - 1;
			state.D[0] = openIndex == failingOpenIndex ? 0 : muiBase;
		});
		bus.RegisterGateway(execBase - 414, state => closed.Add(state.A[1]));

		Assert.Equal(
			20u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(
			failingOpenIndex == 0
				? new[] { global::Amiga.MUIMaster.Name }
				: new[] { global::Amiga.MUIMaster.Name, global::Amiga.Intuition.Name },
			opened);
		Assert.Equal(
			failingOpenIndex == 0 ? Array.Empty<uint>() : new[] { muiBase },
			closed);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MuiSunflowerExecutesExactVectorsAndOwnsMuiLibrary(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint muiBase = 0x0000_3800;
		const uint button = 0x0000_6100;
		const uint label = 0x0000_6200;
		const uint group = 0x0000_6300;
		const uint window = 0x0000_6400;
		const uint app = 0x0000_6500;
		const uint runResult = 0x1234_5678;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(MUISunflower.Program).Assembly.Location,
			EntryPoint = "MUISunflower.Program::Main",
			Cpu = target,
			ExceptionMode = M68kExceptionMode.Yolo,
			OutputFormat = M68kOutputFormat.Hunk,
			Imports = new Dictionary<string, uint>
			{
				["amiga.boopsi.DoMethodA"] = 0x0000_2600
			}
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var newObjects = 0;
		var messages = 0;
		var disposed = 0u;
		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal(0u, state.D[0]);
			Assert.Equal(global::Amiga.MUIMaster.Name, ReadCString(bus, state.A[1]));
			opens++;
			state.D[0] = muiBase;
		});
		bus.RegisterGateway(execBase - 414, state =>
		{
			Assert.Equal(muiBase, state.A[1]);
			closes++;
		});
		bus.RegisterGateway(muiBase - 120, state =>
		{
			Assert.Equal((uint)global::Amiga.MUI.MakeObject.Button, state.D[0]);
			Assert.Equal("Grow", ReadCString(bus, bus.ReadLong(state.A[0])));
			state.D[0] = button;
		});
		bus.RegisterGateway(muiBase - 30, state =>
		{
			var tags = state.A[1];
			switch (newObjects++)
			{
				case 0:
					Assert.Equal(global::Amiga.MUI.Text.Name, ReadCString(bus, state.A[0]));
					Assert.Equal(global::Amiga.MUI.Text.Contents, bus.ReadLong(tags));
					Assert.Equal(
						"A tiny MUI window from CopperSharp.Sdk.Amiga.",
						ReadCString(bus, bus.ReadLong(tags + 4)));
					Assert.Equal(global::Amiga.MUI.Tag.Done, bus.ReadLong(tags + 8));
					state.D[0] = label;
					break;
				case 1:
					Assert.Equal(global::Amiga.MUI.Group.Name, ReadCString(bus, state.A[0]));
					Assert.Equal(global::Amiga.MUI.Group.Child, bus.ReadLong(tags));
					Assert.Equal(label, bus.ReadLong(tags + 4));
					Assert.Equal(global::Amiga.MUI.Group.Child, bus.ReadLong(tags + 8));
					Assert.Equal(button, bus.ReadLong(tags + 12));
					Assert.Equal(global::Amiga.MUI.Tag.Done, bus.ReadLong(tags + 16));
					state.D[0] = group;
					break;
				case 2:
					Assert.Equal(global::Amiga.MUI.Window.Name, ReadCString(bus, state.A[0]));
					Assert.Equal(global::Amiga.MUI.Window.Title, bus.ReadLong(tags));
					Assert.Equal("MUI Sunflower", ReadCString(bus, bus.ReadLong(tags + 4)));
					Assert.Equal(global::Amiga.MUI.Window.RootObject, bus.ReadLong(tags + 8));
					Assert.Equal(group, bus.ReadLong(tags + 12));
					Assert.Equal(global::Amiga.MUI.Tag.Done, bus.ReadLong(tags + 16));
					state.D[0] = window;
					break;
				case 3:
					Assert.Equal(global::Amiga.MUI.Application.Name, ReadCString(bus, state.A[0]));
					var expected = new (uint Tag, string? Text, uint Value)[]
					{
						(global::Amiga.MUI.Application.Author, "CopperSharp68k", 0),
						(global::Amiga.MUI.Application.Base, "SUNFLOWER", 0),
						(global::Amiga.MUI.Application.Description, "Simple MUI window and button example.", 0),
						(global::Amiga.MUI.Application.Title, "MUI Sunflower", 0),
						(global::Amiga.MUI.Application.Version, "$VER: MUISunflower 1.0", 0),
						(global::Amiga.MUI.Application.Window, null, window)
					};
					for (var index = 0; index < expected.Length; index++)
					{
						Assert.Equal(expected[index].Tag, bus.ReadLong(tags + checked((uint)index * 8)));
						var value = bus.ReadLong(tags + checked((uint)index * 8) + 4);
						if (expected[index].Text is { } text)
						{
							Assert.Equal(text, ReadCString(bus, value));
						}
						else
						{
							Assert.Equal(expected[index].Value, value);
						}
					}
					Assert.Equal(
						global::Amiga.MUI.Tag.Done,
						bus.ReadLong(tags + checked((uint)expected.Length * 8)));
					state.D[0] = app;
					break;
				default:
					throw new InvalidOperationException("Unexpected MUI_NewObject call.");
			}
		});
		bus.RegisterGateway(0x0000_2600, state =>
		{
			var message = state.A[1];
			switch (messages++)
			{
				case 0:
					Assert.Equal(window, state.A[0]);
					Assert.Equal(global::Amiga.MUI.Notify.Method, bus.ReadLong(message));
					Assert.Equal(global::Amiga.MUI.Window.CloseRequest, bus.ReadLong(message + 4));
					Assert.Equal((uint)global::Amiga.MUI.Value.EveryTime, bus.ReadLong(message + 8));
					Assert.Equal(app, bus.ReadLong(message + 12));
					Assert.Equal(2u, bus.ReadLong(message + 16));
					Assert.Equal(global::Amiga.MUI.Application.Method.ReturnID, bus.ReadLong(message + 20));
					Assert.Equal(uint.MaxValue, bus.ReadLong(message + 24));
					break;
				case 1:
					Assert.Equal(window, state.A[0]);
					Assert.Equal(global::Amiga.MUI.Method.Set, bus.ReadLong(message));
					Assert.Equal(global::Amiga.MUI.Window.Open, bus.ReadLong(message + 4));
					Assert.Equal(1u, bus.ReadLong(message + 8));
					break;
				case 2:
					Assert.Equal(app, state.A[0]);
					Assert.Equal(global::Amiga.MUI.Application.Method.Run, bus.ReadLong(message));
					state.D[0] = runResult;
					break;
				default:
					throw new InvalidOperationException("Unexpected BOOPSI message.");
			}
		});
		bus.RegisterGateway(muiBase - 36, state => disposed = state.A[0]);

		Assert.Equal(runResult, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.Equal(4, newObjects);
		Assert.Equal(3, messages);
		Assert.Equal(app, disposed);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void CallsExecLibraryVectorsAndReloadsA6AtEachAllocatedCall(
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
			1,
			result.Text!.Split(
				"\tmovea.l\t$0004.w,a6\r\n\tmove.l\ta6,_ExecBase",
				StringSplitOptions.None).Length - 1);
		Assert.Equal(
			1,
			result.Text.Split(
				"\tmove.l\t$0004.w,_ExecBase",
				StringSplitOptions.None).Length - 1);
		Assert.Equal(
			2,
			result.Text.Split(
				"\tmovea.l\t_ExecBase(pc),a6",
				StringSplitOptions.None).Length - 1);
		Assert.Contains("\tjsr\t-30(a6)", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ReloadsA6AtBranchJoinForEachAllocatedCallSite(
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
			3,
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

	[Fact]
	public void CallerProvidedLibraryBaseIsLoadedFromTheCallArgument()
	{
		const uint deviceBase = 0x0000_3400;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::CallCallerProvidedLibrary",
			OutputFormat = M68kOutputFormat.Assembly
		});
		var bus = CreateHunkBus(result);
		bus.RegisterGateway(deviceBase - 60, state =>
		{
			Assert.Equal(deviceBase, state.A[6]);
			state.D[0] += state.D[1];
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.DoesNotContain(
			AmigaLibraryBaseSymbols.For("fixture.device"),
			result.Text,
			StringComparison.Ordinal);
		Assert.Matches(@"\tj(?:sr|mp)\t-60\(a6\)", result.Text);
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
	public void AmigaSdkRawOpenLibraryBindingAvoidsNullableRuntimeFeature()
	{
		const uint execBase = 0x0000_3600;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::CallSdkOpenLibraryRaw",
			Cpu = M68kCpuTarget.M68000,
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

		Assert.Equal(0x0000_4200u,
			Execute(bus, M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint));
		Assert.Empty(result.FrameworkFeatures);
		Assert.Empty(result.FrameworkAnalysis.Members);
		Assert.Empty(result.FrameworkAnalysis.ManagedAllocationSites);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LoadsLiteralAddressDirectlyIntoAmigaAddressArgument(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3600;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallSdkOpenLibraryLiteral",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal("fixture.library", ReadCString(bus, state.A[1]));
			Assert.Equal(37u, state.D[0]);
			state.D[0] = 0x0000_4200;
		});

		Assert.Equal(0x0000_4200u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Matches(
			@"\tmovea\.l\t#C68K_cstring_[^,\r\n]+,a1",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t#C68K_cstring_[^,\r\n]+,d([0-7])\r?\n\tmovea\.l\td\1,a1",
			result.Text);
	}

	[Fact]
	public void FileStatsDefersWordNormalizationUntilWideningBoundary()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "FileStats.dll"),
			EntryPoint = "FileStatsExample.Program::Main",
			Cpu = M68kCpuTarget.M68000,
			ExceptionMode = M68kExceptionMode.Yolo,
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Matches(
			@"\taddq\.w\t#1,d[0-7]",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmoveq\t#1,d([0-7])\r?\n\tadd\.w\td[0-7],d\1",
			result.Text);
		Assert.Matches(
			@"\taddq\.l\t#1,a[0-6]",
			result.Text);
		Assert.DoesNotMatch(
			@"\tandi\.l\t#\$0000FFFF,d([0-7])\r?\n\tmoveq\t#1,d([0-7])\r?\n\tadd\.l\td\1,d\2",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\td0,d([2-7])\r?\n\tandi\.l\t#\$0000FFFF,d\1",
			result.Text);
		Assert.Matches(
			@"\trol\.w\t#5,d([0-7])\r?\n\tadd\.w\td[0-7],d\1",
			result.Text);
		Assert.Matches(
			@"\tswap\td([0-7])\r?\n\tclr\.w\td\1",
			result.Text);
		Assert.DoesNotMatch(
			@"\tlsl\.l\t#8,d([0-7])\r?\n\tlsl\.l\t#8,d\1",
			result.Text);
		Assert.Equal(
			3,
			Regex.Matches(
				result.Text!,
				@"\tmoveq\t#0,d([0-7])\r?\n\tmove\.[bw]\td[0-7],d\1\r?\n\tmove\.l\td\1,-\(a7\)").Count);
		Assert.DoesNotMatch(
			@"\tandi\.l\t#\$0000(?:00FF|FFFF),d([0-7])\r?\n\tmove\.l\td\1,-\(a7\)",
			result.Text);
		Assert.Contains("\tlea\t20(a7),a7", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tlea\t28(a7),a7", result.Text, StringComparison.Ordinal);
		var printReportStatistics = Assert.Single(
			result.AllocationStatistics.Where(statistics =>
				statistics.Method.Contains("Program::PrintReport", StringComparison.Ordinal)));
		Assert.True(
			printReportStatistics.CodeBytes < 252,
			$"PrintReport is {printReportStatistics.CodeBytes} bytes.");
		Assert.Equal(0, printReportStatistics.SpillFrameBytes);
		Assert.Equal(1, printReportStatistics.CalleeSavedRegisters);
		var printReport = Regex.Match(
			result.Text!,
			@"C68K_method_003A06000003:\r?\n(?<body>.*?)C68K_method_003A06000003_003Aend:",
			RegexOptions.Singleline);
		Assert.True(printReport.Success, "FileStats PrintReport body was not found.");
		Assert.Equal(
			5,
			Regex.Matches(
				printReport.Groups["body"].Value,
				@"\tmove\.l\t\d+\(a7\),d0\r?\n\tmove\.l\td0,-\(a7\)").Count);
		Assert.DoesNotContain("a2", printReport.Groups["body"].Value, StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmove\.l\td[0-7],\d+\(a7\)",
			printReport.Groups["body"].Value);
		Assert.DoesNotMatch(
			@"\tmovea\.l\td([0-7]),a1\r?\n\tmove\.l\ta1,d0\r?\n\tmove\.l\td0,d1",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmovea\.l\td([0-7]),a1\r?\n\tmove\.l\ta1,d1\r?\n\tjsr\t-(?:306|36)\(a6\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmovea\.l\td[0-7],a1\r?\n\ttst\.l\td[0-7]",
			result.Text);
		Assert.Matches(
			@"\tmove\.l\td[0-7],_DOSLibraryBase",
			result.Text);
		Assert.Contains(
			"\tmovea.l\t_DOSLibraryBase(pc),a1",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmove\.l\t_DOSLibraryBase\(pc\),d([0-7])\r?\n\tmovea\.l\td\1,a1",
			result.Text);
		Assert.Matches(
			@"\tmove\.l\t#C68K_cstring_[^,\r\n]+,d1\r?\n\tmove\.l\ta7,d2\r?\n\tjsr\t-954\(a6\)",
			result.Text);
		Assert.DoesNotContain(
			"\tmovea.l\t_DOSLibraryBase(pc),a6",
			printReport.Groups["body"].Value,
			StringComparison.Ordinal);
		var reportLoop = Assert.Single(result.LoopFootprints.Where(footprint =>
			footprint.Method.Contains("Program::Report", StringComparison.Ordinal)));
		Assert.True(
			reportLoop.InstructionBytes < 128,
			$"FileStats Report loop is {reportLoop.InstructionBytes} bytes.");
		var loopMatch = Regex.Match(
			result.Text!,
			@"C68K_method_003A06000002_003ABB0008:\r?\n(?<body>.*?)C68K_method_003A06000002_003ABB0002:",
			RegexOptions.Singleline);
		Assert.True(loopMatch.Success, "FileStats loop layout was not found.");
		Assert.DoesNotContain("(a7)", loopMatch.Groups["body"].Value, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void FileStatsExecutesRealReportAndForwardsEveryPrintfArgument(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_3800;
		const uint pathAddress = 0x0000_1800;
		const uint fileHandle = 0x0000_4242;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "FileStats.dll"),
			EntryPoint = "FileStatsExample.Program::Main",
			Cpu = target,
			ExceptionMode = M68kExceptionMode.Yolo,
			OutputFormat = M68kOutputFormat.Hunk
		});
		foreach (var input in new byte[][]
		{
			[],
			[0xA5],
			[0x00, 0x0A, 0x7F, 0xFF, 0x0A, 0x31]
		})
		{
			var bus = CreateHunkBus(result);
			var calls = new List<string>();
			var nextByte = 0;
			uint[]? printed = null;
			bus.WriteLong(4, execBase);
			var path = "RAM:test.bin"u8.ToArray();
			path.CopyTo(bus.Memory.AsSpan((int)pathAddress));
			bus.Memory[(int)pathAddress + path.Length] = 0;
			bus.RegisterGateway(execBase - 552, state =>
			{
				calls.Add("OpenLibrary");
				Assert.Equal("dos.library", ReadCString(bus, state.A[1]));
				Assert.Equal(0u, state.D[0]);
				state.D[0] = dosBase;
			});
			bus.RegisterGateway(dosBase - 30, state =>
			{
				calls.Add("Open");
				Assert.Equal(pathAddress, state.D[1]);
				Assert.Equal(1005u, state.D[2]);
				state.D[0] = fileHandle;
			});
			bus.RegisterGateway(dosBase - 306, state =>
			{
				calls.Add("FGetC");
				Assert.Equal(fileHandle, state.D[1]);
				state.D[0] = nextByte < input.Length
					? input[nextByte++]
					: uint.MaxValue;
			});
			bus.RegisterGateway(dosBase - 36, state =>
			{
				calls.Add("Close");
				Assert.Equal(fileHandle, state.D[1]);
			});
			bus.RegisterGateway(dosBase - 954, state =>
			{
				calls.Add("Printf");
				Assert.Equal(
					"%s: %ld bytes, %ld lines, byte checksum %ld, word checksum %ld, average byte %ld, hash %ld\n",
					ReadCString(bus, state.D[1]));
				printed = Enumerable.Range(0, 7)
					.Select(index => bus.ReadLong(state.D[2] + checked((uint)(index * 4))))
					.ToArray();
			});
			bus.RegisterGateway(execBase - 414, state =>
			{
				calls.Add("CloseLibrary");
				Assert.Equal(dosBase, state.A[1]);
			});

			Assert.Equal(
				0u,
				Execute(
					bus,
					model,
					HunkLoadAddress + result.EntryPoint,
					initialize: state =>
					{
						state.D[0] = (uint)path.Length;
						state.A[0] = pathAddress;
					},
					maxInstructions: 500_000));

			byte byteChecksum = 0;
			ushort wordChecksum = 0;
			ushort lineCount = 0;
			uint byteSum = 0;
			uint rollingHash = 2166136261;
			foreach (var value in input)
			{
				byteChecksum = unchecked((byte)(byteChecksum + value));
				wordChecksum = unchecked((ushort)(
					((wordChecksum << 5) | (wordChecksum >> 11)) + value));
				byteSum += value;
				rollingHash = unchecked((rollingHash ^ value) * 16777619);
				if (value == 10)
				{
					lineCount++;
				}
			}
			Assert.Equal(
				[
					pathAddress,
					(uint)input.Length,
					lineCount,
					byteChecksum,
					wordChecksum,
					input.Length == 0 ? 0u : byteSum / (uint)input.Length,
					rollingHash
				],
				printed);
			Assert.Equal(input.Length + 1, calls.Count(call => call == "FGetC"));
			Assert.Equal(
				["OpenLibrary", "Open", .. Enumerable.Repeat("FGetC", input.Length + 1), "Close", "Printf", "CloseLibrary"],
				calls);
		}
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

		var callerProvided = Assert.Throws<M68kCompilationException>(() =>
			Compile(
				M68kCpuTarget.M68000,
				M68kOutputFormat.Hunk,
				"CopperSharp.Compiler.Tests.CompilerFixtures::CallInvalidCallerProvidedLibrary"));
		Assert.Equal(M68kDiagnosticIds.UnsupportedSignature, callerProvided.DiagnosticId);
		Assert.Contains("exactly one A6 argument", callerProvided.Message, StringComparison.Ordinal);

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
		Assert.Contains("__c68k_gc_coalesce:", result.Text, StringComparison.Ordinal);
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
	public void NonNullFactsMeetAcrossAllocatingBranchesAndNullableMergeStillChecks()
	{
		const uint allocatorAddress = 0x0000_2800;
		M68kCompilationResult CompileEntry(
			string entryPoint,
			M68kExceptionMode exceptionMode) =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint =
					$"CopperSharp.Compiler.Tests.CompilerFixtures::{entryPoint}",
				Cpu = M68kCpuTarget.M68000,
				ExceptionMode = exceptionMode,
				OutputFormat = M68kOutputFormat.Assembly,
				Imports = new Dictionary<string, uint>
				{
					[M68kRuntimeImports.Allocate] = allocatorAddress
				}
			});

		var yolo = CompileEntry("NonNullMergeTrueEntry", M68kExceptionMode.Yolo);
		var full = CompileEntry("NonNullMergeTrueEntry", M68kExceptionMode.Full);
		Assert.DoesNotContain(
			"allocated_nonnull",
			yolo.Text!,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"allocated_nonnull",
			full.Text!,
			StringComparison.Ordinal);

		Assert.Equal(22u, ExecutePath(yolo));
		Assert.Equal(
			26u,
			ExecutePath(CompileEntry(
				"NonNullMergeFalseEntry",
				M68kExceptionMode.Yolo)));

		var nullable = CompileEntry(
			"NullableMergeObjectEntry",
			M68kExceptionMode.Yolo);
		Assert.Contains(
			"allocated_nonnull",
			nullable.Text!,
			StringComparison.Ordinal);

		uint ExecutePath(M68kCompilationResult result)
		{
			var bus = CreateHunkBus(result);
			var heap = 0x0000_4000u;
			bus.RegisterGateway(allocatorAddress, state =>
			{
				var size = state.D[0];
				state.D[0] = heap;
				heap += (size + 3) & ~3u;
			});
			return Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint);
		}
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
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
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
			new[] { 24u, 20u, 20u },
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

	[Fact]
	public void UnrelatedCrossModuleSdkStructIsExcludedFromUserInterfaceMap()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::InterfaceDispatchWithUnrelatedSdkRectangleEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ActualCrossModuleInterfaceMapRemainsAnExplicitDiagnostic()
	{
		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location,
			managedAssemblyPaths:
			[
				typeof(CopperSharp.Compiler.Tests.MultiModule.IExternalValueSource)
					.Assembly.Location
			]);
		var typeEntry = module.ResolveEntryPoint(
			"CopperSharp.Compiler.Tests.CrossModuleInterfaceMetadataFixture::Entry");
		var layout = module.GetTypeLayout(typeEntry);
		var interfaceEntry = module.ResolveEntryPoint(
			"CopperSharp.Compiler.Tests.CompilerFixtures::ExternalInterfaceIdentityEntry");
		var target = module.ResolveRuntimeTypeIdentity(
			interfaceEntry.Signature.ReturnType,
			interfaceEntry.ModuleName);
		var definition = module.GetRuntimeInterfaceDefinition(target);

		var exception = Assert.Throws<M68kCompilationException>(() =>
			module.TryGetInterfaceImplementation(layout, definition));

		Assert.Equal(M68kDiagnosticIds.UnsupportedPolymorphism,
			exception.DiagnosticId);
		Assert.Contains("Cross-module interface implementation map",
			exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ConstrainedValueDispatchTraversesAReferencedInterfaceBase()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			ManagedAssemblyPaths =
			[
				typeof(CopperSharp.Compiler.Tests.MultiModule.IExternalValueSource)
					.Assembly.Location
			],
			EntryPoint =
				"CopperSharp.Compiler.Tests.CrossModuleConstrainedInterfaceFixture::Entry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ReferencedTransparentScalarCanBeEmbeddedInALocalValueType()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ExternalTransparentScalarFieldEntry");

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
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
			"ObjectArrayBoxedValueStoreEntry",
			"ReferenceArrayStoreTypeCheckEntry",
			"StringArrayStoreTypeCheckEntry",
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
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
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
	public void ConstrainedGenericInterfaceMethodsDispatchWithoutBoxing(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstrainedGenericInterfaceMethodDispatchEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstrainedGenericCallsUseConcreteAbiAndInitializeDefaultsAcrossFinally(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstrainedGenericMultiArgumentDefaultFinallyEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ValueTypeConstructorsCanBeStoredThroughOutParameters(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ValueTypeConstructorStoredThroughOutParameterEntry");

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
	public void MemoryViewsSliceAndExposeSpansWithoutAllocatingViews(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.MemoryArraySliceAndSpanEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryArraySliceAndSpanEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MemoryNullAndRangeSemanticsMatchDotNet(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.MemoryNullAndBoundsEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryNullAndBoundsEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MemoryReferenceOwnerRemainsRootedAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryReferenceOwnerSurvivesCollectionEntry",
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
	public void MemoryViewsUseExactScalarWidthsAndBigEndianLongs(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.MemoryScalarWidthAndEndianEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryScalarWidthAndEndianEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));

		var high = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryLongSpanEntry");
		Assert.Equal(0x11223344u, ExecuteHunkWithAllocator(high, model));
		var low = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryLongSpanLowWordEntry");
		Assert.Equal(0x55667788u, ExecuteHunkWithAllocator(low, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MemoryFloatSpanUsesExactFourByteStride(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryFloatSpanEntry",
			floatingPoint: M68kFloatingPointMode.SoftFloat);

		Assert.Equal(
			unchecked((uint)BitConverter.SingleToInt32Bits(21.5f)),
			ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MemoryCopyOperationsMatchDotNetAndPreserveOverlap(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.MemoryCopyOperationsEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryCopyOperationsEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MemoryCopyShortDestinationThrowsOrReturnsFalseWithoutWriting(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.MemoryCopyShortDestinationEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryCopyShortDestinationEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MemoryCopyUsesExactScalarWidthsAndEndianOrder(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.MemoryCopyScalarWidthsEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryCopyScalarWidthsEntry",
			floatingPoint: M68kFloatingPointMode.SoftFloat);

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));

		var high = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryCopyLongEntry");
		Assert.Equal(0x11223344u, ExecuteHunkWithAllocator(high, model));
		var low = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryCopyLongLowWordEntry");
		Assert.Equal(0x55667788u, ExecuteHunkWithAllocator(low, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void MemoryReferenceCopyRemainsRootedAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryReferenceCopySurvivesCollectionEntry",
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
	public void LinqRangeAndRepeatMaterializeLikeDotNet(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.LinqRangeToArrayEntry());
		Assert.Equal(42, CompilerFixtures.LinqRepeatByteToArrayEntry());

		var range = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeToArrayEntry");
		Assert.Equal(42u, ExecuteHunkWithAllocator(range, model));
		var repeat = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRepeatByteToArrayEntry");
		Assert.Equal(42u, ExecuteHunkWithAllocator(repeat, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqRangeValidatesArgumentsAtFactoryCall(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.LinqRangeValidatesArgumentsAtFactoryCallEntry());

		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeValidatesArgumentsAtFactoryCallEntry");
		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqRangeProvenanceSurvivesLocalsMergesAndRepeatedMaterialization(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"LinqRangeLocalRepeatedToArrayEntry",
			"LinqRangeSameFamilyMergeToArrayEntry"
		})
		{
			var host = entry == "LinqRangeLocalRepeatedToArrayEntry"
				? CompilerFixtures.LinqRangeLocalRepeatedToArrayEntry()
				: CompilerFixtures.LinqRangeSameFamilyMergeToArrayEntry();
			Assert.Equal(42, host);
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqRangeSelectIsDeferredOrderedAndRepeatable(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.LinqRangeSelectToArrayEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectToArrayEntry");
		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqRangeSelectInvokesStaticSelector(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.LinqRangeSelectStaticToArrayEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectStaticToArrayEntry");
		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqRangeSelectDefersSelectorExceptionUntilMaterialization(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.LinqRangeSelectDefersSelectorExceptionEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectDefersSelectorExceptionEntry");
		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqRangeSelectCaptureRemainsRootedAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectCaptureSurvivesCollectionEntry",
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
	public void LinqRangeSelectValidatesNullSelectorAtConstruction(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.LinqRangeSelectNullSelectorEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectNullSelectorEntry");
		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqRangeWhereIsDeferredOrderedAndHandlesCardinality(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"LinqRangeWhereToArrayEntry",
			"LinqRangeWhereAllNoneEmptyEntry",
			"LinqRangeSelectWhereToArrayEntry"
		})
		{
			var host = entry switch
			{
				"LinqRangeWhereToArrayEntry" => CompilerFixtures.LinqRangeWhereToArrayEntry(),
				"LinqRangeWhereAllNoneEmptyEntry" => CompilerFixtures.LinqRangeWhereAllNoneEmptyEntry(),
				_ => CompilerFixtures.LinqRangeSelectWhereToArrayEntry()
			};
			Assert.Equal(42, host);
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqRangeWherePreservesConstructionAndPredicateExceptionTiming(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.LinqRangeWhereNullPredicateEntry());
		Assert.Equal(42, CompilerFixtures.LinqRangeWhereDefersPredicateExceptionEntry());

		foreach (var entry in new[]
		{
			"LinqRangeWhereNullPredicateEntry",
			"LinqRangeWhereDefersPredicateExceptionEntry"
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
	public void LinqRangeWhereCaptureRemainsRootedAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeWhereCaptureSurvivesCollectionEntry",
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
	public void LinqAnyShortCircuitsEverySelectedPrivateIteratorFamily(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.LinqAnyWithoutPredicateEntry());
		Assert.Equal(42, CompilerFixtures.LinqAnyPredicateEntry());

		foreach (var entry in new[]
		{
			"LinqAnyWithoutPredicateEntry",
			"LinqAnyPredicateEntry"
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
	public void LinqAnyPreservesPredicateValidationExceptionAndShortCircuitTiming(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.LinqAnyExceptionTimingEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqAnyExceptionTimingEntry");
		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqAnyCapturedPredicatesRemainRootedAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::LinqAnyCaptureSurvivesCollectionEntry",
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
	public void LinqTakeNarrowsEverySelectedPrivateIteratorFamily(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.LinqRangeRepeatTakeEntry());
		Assert.Equal(42, CompilerFixtures.LinqSelectTakeEntry());
		Assert.Equal(42, CompilerFixtures.LinqRangeWhereTakeEntry());
		Assert.Equal(42, CompilerFixtures.LinqSelectWhereTakeEntry());
		Assert.Equal(42, CompilerFixtures.LinqTakeAnyEntry());

		foreach (var entry in new[]
		{
			"LinqRangeRepeatTakeEntry",
			"LinqSelectTakeEntry",
			"LinqRangeWhereTakeEntry",
			"LinqSelectWhereTakeEntry",
			"LinqTakeAnyEntry"
		})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
			var actual = ExecuteHunkWithAllocator(result, model);
			Assert.True(actual == 42u, $"{entry} returned {actual}, expected 42.");
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqTakePreservesValidationDeferredExceptionAndLimitTiming(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.LinqTakeExceptionTimingEntry());
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqTakeExceptionTimingEntry");
		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqTakeCapturedPredicatesRemainRootedAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::LinqTakeCaptureSurvivesCollectionEntry",
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
	public void LinqSumAggregatesEverySelectedPrivateIteratorFamily(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"LinqRangeRepeatSumEntry",
			"LinqSelectSumEntry",
			"LinqRangeWhereSumEntry",
			"LinqSelectWhereTakeSumEntry",
			"LinqRangeSelectWhereTakeSumStaticEntry",
			"LinqArrayImageBlockSumSelectorEntry"
		})
		{
			Assert.Equal(42, entry switch
			{
				"LinqRangeRepeatSumEntry" => CompilerFixtures.LinqRangeRepeatSumEntry(),
				"LinqSelectSumEntry" => CompilerFixtures.LinqSelectSumEntry(),
				"LinqRangeWhereSumEntry" => CompilerFixtures.LinqRangeWhereSumEntry(),
				"LinqSelectWhereTakeSumEntry" => CompilerFixtures.LinqSelectWhereTakeSumEntry(),
				"LinqRangeSelectWhereTakeSumStaticEntry" =>
					CompilerFixtures.LinqRangeSelectWhereTakeSumStaticEntry(),
				_ => CompilerFixtures.LinqArrayImageBlockSumSelectorEntry()
			});
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
			var actual = ExecuteHunkWithAllocator(result, model);
			Assert.True(actual == 42u, $"{entry} returned {actual}, expected 42.");
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqDictionaryValuesOrderByThenByIsStableOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		Assert.Equal(634251, CompilerFixtures.LinqDictionaryValuesOrderByThenByEntry());
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::LinqDictionaryValuesOrderByThenByEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0002_0000
			}
		});
		Assert.Equal(
			634251u,
			ExecuteHunk(result, model, maxInstructions: 1_000_000));
	}

	[Fact]
	public void LinqDictionaryValuesOrderByThenByReturnsExpectedOrderOnMc68000()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqDictionaryValuesOrderByThenByEntry");
		Assert.Equal(634251u, ExecuteHunkWithAllocator(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void LinqDictionaryOrderingRepeatsStatefulSelectorsOnMc68000()
	{
		Assert.Equal(42, CompilerFixtures.LinqDictionaryOrderingStatefulRepeatedEntry());
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqDictionaryOrderingStatefulRepeatedEntry");
		Assert.Equal(42u, ExecuteHunkWithAllocator(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void LinqDictionaryOrderingPreservesNullAndSelectorExceptionTimingOnMc68000()
	{
		Assert.Equal(42, CompilerFixtures.LinqDictionaryOrderingExceptionTimingEntry());
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqDictionaryOrderingExceptionTimingEntry");
		Assert.Equal(42u, ExecuteHunkWithAllocator(result, M68kCpuModel.M68000));
	}

	[Theory]
	[InlineData("LinqDictionaryOrderingSize0Entry")]
	[InlineData("LinqDictionaryOrderingSize1Entry")]
	public void LinqDictionaryOrderingHandlesEmptyAndSingleSourcesOnMc68000(string entry)
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
		Assert.Equal(42u, ExecuteHunkWithAllocator(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void StablePermutationSortUsesBothKeysOnMc68000()
	{
		Assert.Equal(634251, CompilerFixtures.StablePermutationSortEntry());
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::StablePermutationSortEntry");
		Assert.Equal(634251u, ExecuteHunkWithAllocator(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void LinqDictionaryOrderingLinksOnlyThePrivateStablePermutationPath()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqDictionaryValuesOrderByThenByEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			});

		Assert.Contains("ShadowEnumerable::DictionaryUInt32ValuesOrderBy", assembly.Map);
		Assert.Contains("ShadowEnumerable::DictionaryUInt32ValuesThenBy", assembly.Map);
		Assert.Contains("ShadowPrimaryOrderedEnumerable", assembly.Map);
		Assert.Contains("ShadowOrderedEnumerable", assembly.Map);
		Assert.Contains("ShadowInt32StablePermutationSort", assembly.Map);
		Assert.Contains("ShadowOrderedEnumerator", assembly.Map);
		Assert.DoesNotContain("IComparer", assembly.Map);
		Assert.DoesNotContain("CreateOrderedEnumerable", assembly.Map);
		Assert.DoesNotContain("ToArray", assembly.Map);
	}

	[Fact]
	public void LinqDictionaryOrderingHasSubquadraticMc68000Scaling()
	{
		var one = MeasureDictionaryOrdering(
			"LinqDictionaryOrderingSize1Entry",
			maxInstructions: 1_000_000);
		var sixteen = MeasureDictionaryOrdering(
			"LinqDictionaryOrderingSize16Entry",
			maxInstructions: 2_000_000);
		var oneHundredSixtyEight = MeasureDictionaryOrdering(
			"LinqDictionaryOrderingSize168Entry",
			maxInstructions: 10_000_000);

		Assert.True(
			oneHundredSixtyEight.Cycles < sixteen.Cycles * 20,
			$"Ordering scaling is not subquadratic: 16={sixteen.Cycles}, " +
			$"168={oneHundredSixtyEight.Cycles} MC68000 cycles.");
		AssertDictionaryOrderingBudget(one, 16, 32_000);
		AssertDictionaryOrderingBudget(sixteen, 28, 450_000);
		AssertDictionaryOrderingBudget(oneHundredSixtyEight, 40, 5_500_000);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqSumPreservesNullSelectorOverflowAndCallbackExceptionTiming(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"LinqSumExceptionTimingEntry",
			"LinqArrayImageBlockSumExceptionTimingEntry"
		})
		{
			Assert.Equal(
				42,
				entry == "LinqSumExceptionTimingEntry"
					? CompilerFixtures.LinqSumExceptionTimingEntry()
					: CompilerFixtures.LinqArrayImageBlockSumExceptionTimingEntry());
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
			Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqSumCapturedPipelineAndTerminalSelectorsRemainRootedAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"LinqSumCaptureSurvivesCollectionEntry",
			"LinqArrayImageBlockSumCaptureSurvivesCollectionEntry"
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

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LinqRepeatReferencesRemainRootedAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRepeatReferenceSurvivesCollectionEntry",
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
	public void LinqRangeMaterializerUsesDirectExactArrayLoop()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeToArrayEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			});

		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::Range", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::RangeToArray", assembly.Map);
		Assert.Contains("move.l\td1,12(a0,d0.l)", assembly.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("ShadowEnumerableEnumerator", assembly.Map);
	}

	[Fact]
	public void LinqRangeSelectMaterializerUsesExactPrivatePipelineWithoutEnumerator()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectStaticToArrayEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			});

		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::SelectInt32", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::SelectInt32ToArray", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowInt32SelectIterator", assembly.Map);
		Assert.DoesNotContain("ShadowEnumerableEnumerator", assembly.Map);
	}

	[Fact]
	public void LinqRangeSelectWhereMaterializerUsesExactPrivatePipelineWithoutEnumerator()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectWhereStaticToArrayEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			});

		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::SelectWhereInt32", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::SelectWhereInt32ToArray", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowSelectWhereIterator", assembly.Map);
		Assert.DoesNotContain("ShadowEnumerableEnumerator", assembly.Map);
		Assert.DoesNotContain("ShadowWhereEnumerator", assembly.Map);
	}

	[Fact]
	public void LinqAnyUsesExactPrivateShortCircuitPipelineWithoutEnumeratorOrArray()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectWhereAnyStaticEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			});

		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::SelectWhereInt32AnyPredicate", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowSelectWhereIterator::Any", assembly.Map);
		Assert.DoesNotContain("ShadowEnumerableEnumerator", assembly.Map);
		Assert.DoesNotContain("ShadowWhereEnumerator", assembly.Map);
		Assert.DoesNotContain("MaterializeWhere", assembly.Map);
		Assert.DoesNotContain("ToArray", assembly.Map);
	}

	[Fact]
	public void LinqTakeUsesNarrowedExactPrivatePipelineWithoutEnumeratorOrArray()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectWhereTakeAnyStaticEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			});

		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::SelectWhereInt32TakeInt32", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowSelectWhereIterator::Take", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::SelectWhereInt32TakeAnyPredicate", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowSelectWhereIterator::AnyTaken", assembly.Map);
		Assert.DoesNotContain("ShadowEnumerableEnumerator", assembly.Map);
		Assert.DoesNotContain("ShadowWhereEnumerator", assembly.Map);
		Assert.DoesNotContain("ShadowTakeEnumerator", assembly.Map);
		Assert.DoesNotContain("MaterializeWhere", assembly.Map);
		Assert.DoesNotContain("ToArray", assembly.Map);
	}

	[Fact]
	public void LinqSumUsesCheckedExactPrivateTerminalWithoutEnumeratorOrMaterialization()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectWhereTakeSumStaticEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			});

		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::SelectWhereInt32TakeInt32", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::SelectWhereInt32TakeSumSelector", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowSelectWhereIterator::SumTaken", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::CheckedAdd", assembly.Map);
		Assert.DoesNotContain("ShadowEnumerableEnumerator", assembly.Map);
		Assert.DoesNotContain("ShadowWhereEnumerator", assembly.Map);
		Assert.DoesNotContain("ShadowTakeEnumerator", assembly.Map);
		Assert.DoesNotContain("MaterializeWhere", assembly.Map);
		Assert.DoesNotContain("ToArray", assembly.Map);
	}

	[Fact]
	public void LinqReferenceFreeStructArraySumUsesGenericArrayLoopWithoutEnumeration()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqArrayImageBlockSumStaticEntry",
			imports: new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			});

		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::ArraySumSelector", assembly.Map);
		Assert.Contains("CopperSharp.Runtime.ShadowEnumerable::CheckedAdd", assembly.Map);
		Assert.Contains("LinqIpfDescriptorBits", assembly.Map);
		Assert.Contains("bvc", assembly.Text, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("ShadowEnumerableIterator", assembly.Map);
		Assert.DoesNotContain("Enumerator", assembly.Map);
		Assert.DoesNotContain("Materialize", assembly.Map);
		Assert.DoesNotContain("ToArray", assembly.Map);
	}

	[Fact]
	public void LinqReferenceFreeStructArraySumStaysWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqArrayImageBlockSumStaticEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 5_800,
			$"LINQ reference-free struct array Sum image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 4_200,
			$"LINQ reference-free struct array Sum code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 4_200,
			$"LINQ reference-free struct array Sum path grew to {cycles} MC68000 cycles.");
		Assert.Equal(2, allocations);
		Assert.Equal(7, result.AllocationStatistics.Count);
		Assert.Equal(
			0,
			result.AllocationStatistics.Max(static item => item.SpillFrameBytes));
	}

	[Fact]
	public void LinqRangeTakeExecutesExactPrivateMaterializerOnMc68000()
	{
		Assert.Equal(234, CompilerFixtures.LinqRangeTakeStaticToArrayEntry());
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeTakeStaticToArrayEntry");
		Assert.Equal(234u, ExecuteHunkWithAllocator(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void LinqTakeStaysWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectWhereTakeAnyStaticEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 10_500,
			$"LINQ Range.Select.Where.Take.Any image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 7_400,
			$"LINQ Range.Select.Where.Take.Any code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 15_000,
			$"LINQ Range.Select.Where.Take.Any path grew to {cycles} MC68000 cycles.");
		Assert.Equal(7, allocations);
		Assert.Equal(23, result.AllocationStatistics.Count);
		Assert.True(
			result.AllocationStatistics.Max(static item => item.SpillFrameBytes) <= 8,
			"LINQ Range.Select.Where.Take.Any exceeded two 32-bit spill slots.");
	}

	[Fact]
	public void LinqSumStaysWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectWhereTakeSumStaticEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 10_800,
			$"LINQ Range.Select.Where.Take.Sum image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 7_600,
			$"LINQ Range.Select.Where.Take.Sum code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 15_800,
			$"LINQ Range.Select.Where.Take.Sum path grew to {cycles} MC68000 cycles.");
		Assert.Equal(7, allocations);
		Assert.Equal(24, result.AllocationStatistics.Count);
		Assert.True(
			result.AllocationStatistics.Max(static item => item.SpillFrameBytes) <= 8,
			"LINQ Range.Select.Where.Take.Sum exceeded two 32-bit spill slots.");
	}

	[Fact]
	public void LinqAnyStaysWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectWhereAnyStaticEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 9_000,
			$"LINQ Range.Select.Where.Any image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 6_300,
			$"LINQ Range.Select.Where.Any code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 13_200,
			$"LINQ Range.Select.Where.Any path grew to {cycles} MC68000 cycles.");
		Assert.Equal(6, allocations);
		Assert.Equal(20, result.AllocationStatistics.Count);
		var maxSpill = result.AllocationStatistics.Max(static item => item.SpillFrameBytes);
		Assert.True(
			maxSpill <= 4,
			$"LINQ Range.Select.Where.Any exceeded one 32-bit spill slot ({maxSpill} bytes). " +
			string.Join(", ", result.AllocationStatistics
				.Where(item => item.SpillFrameBytes == maxSpill)
				.Select(item => item.Method)));
	}

	[Fact]
	public void LinqRangeSelectWhereMaterializationStaysWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectWhereStaticToArrayEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 9_800,
			$"LINQ Range.Select.Where image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 7_000,
			$"LINQ Range.Select.Where code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 14_500,
			$"LINQ Range.Select.Where path grew to {cycles} MC68000 cycles.");
		Assert.Equal(7, allocations);
		Assert.Equal(19, result.AllocationStatistics.Count);
		Assert.True(
			result.AllocationStatistics.Max(static item => item.SpillFrameBytes) <= 4,
			"LINQ Range.Select.Where exceeded one 32-bit spill slot.");
	}

	[Fact]
	public void LinqRangeSelectMaterializationStaysWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeSelectStaticToArrayEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 8_200,
			$"LINQ Range.Select image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 5_800,
			$"LINQ Range.Select code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 6_800,
			$"LINQ Range.Select path grew to {cycles} MC68000 cycles.");
		Assert.Equal(4, allocations);
		Assert.Equal(16, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void LinqRangeMaterializationStaysWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRangeToArrayEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 4_300,
			$"LINQ Range image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 3_150,
			$"LINQ Range code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 5_200,
			$"LINQ Range path grew to {cycles} MC68000 cycles.");
		Assert.Equal(4, allocations);
		Assert.Equal(7, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void LinqRepeatMaterializationStaysWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::LinqRepeatByteToArrayEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 3_900,
			$"LINQ Repeat image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 2_800,
			$"LINQ Repeat code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 3_000,
			$"LINQ Repeat path grew to {cycles} MC68000 cycles.");
		Assert.Equal(2, allocations);
		Assert.Equal(7, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void MemoryCopyAssemblyUsesSharedInlineMemmoveKernel()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryCopyOperationsEntry");

		Assert.Contains("allocated_copy_backward_loop", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("allocated_copy_forward_loop", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\t(a2),d0", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\td0,(a1)", assembly.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void MemoryCopyMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryCopyOperationsEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 7_100,
			$"Memory copy image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 6_000,
			$"Memory copy code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 9_500,
			$"Memory copy path grew to {cycles} MC68000 cycles.");
		Assert.Equal(2, allocations);
		Assert.Equal(2, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void MemoryViewMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MemoryArraySliceAndSpanEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 5_100,
			$"Memory view image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 4_200,
			$"Memory view code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 6_500,
			$"Memory view path grew to {cycles} MC68000 cycles " +
			$"({result.Image.Length} image bytes, {result.Code.Length} code bytes, " +
			$"{allocations} executed allocation, " +
			$"{result.AllocationStatistics.Count} analyzed allocation methods).");
		Assert.Equal(1, allocations);
		Assert.Equal(2, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
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
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
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
	public void IntegerFormattingFixturesMatchHostNet10() =>
		Assert.Multiple(
			() => Assert.Equal(42, CompilerFixtures.IntegerToStringEntry()),
			() => Assert.Equal(42, CompilerFixtures.IntegerToStringBoundaryEntry()),
			() => Assert.Equal(42, CompilerFixtures.Int64ToStringEntry()),
			() => Assert.Equal(42, CompilerFixtures.IntegerFormatStringEntry()),
			() => Assert.Equal(42, CompilerFixtures.IntegerFormatStringExceptionEntry()));

	[Fact]
	public void InterpolatedIntegerFixtureMatchesHostNet10() =>
		Assert.Equal(42, CompilerFixtures.InterpolatedIntegerEntry());

	[Fact]
	public void StringFormatParamsIntegerFixtureMatchesHostNet10() =>
		Assert.Multiple(
			() => Assert.Equal(42, CompilerFixtures.StringFormatParamsIntegerEntry()),
			() => Assert.Equal(42, CompilerFixtures.StringFormatSharedComputedParamsEntry()),
			() => Assert.Equal(42, CompilerFixtures.StringFormatOverflowingIndexEntry()),
			() => Assert.Equal(42, CompilerFixtures.StringFormatFixedArgumentsEntry()),
			() => Assert.Equal(42, CompilerFixtures.StringFormatSpanParamsEntry()),
			() => Assert.Equal(42, CompilerFixtures.StringFormatSpanEightParamsEntry()));

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringFormatFourIntegerParamsExecuteWithoutArrayOrBoxes(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::StringFormatParamsIntegerEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_5000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Fact]
	public void StringFormatRewritesExecuteWithForcedCollection()
	{
		foreach (var entry in new[]
		{
			"StringFormatSharedComputedParamsEntry",
			"StringFormatFixedArgumentsEntry",
			"StringFormatSpanParamsEntry",
			"StringFormatOverflowingIndexEntry"
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = M68kCpuTarget.M68000,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_5000
				}
			});

			var actual = ExecuteHunk(result, M68kCpuModel.M68000);
			Assert.True(actual == 42u, $"{entry} returned {actual}.");
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void InterpolatedIntegersExecuteWithForcedCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::InterpolatedIntegerEntry",
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
	public void IntegerFormattingUsesInvariantAllocationExactShadowRuntime(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"IntegerToStringEntry",
			"IntegerToStringBoundaryEntry",
			"IntegerFormatStringEntry",
			"IntegerFormatStringExceptionEntry"
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

			var actual = ExecuteHunk(result, model);
			Assert.True(actual == 42u, $"{entry} returned {actual}.");
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void Int64FormattingUsesLaneWiseAllocationExactShadowRuntime(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::Int64ToStringEntry",
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
	public void DirectWidenedInt64ArgumentsPreserveBothRegisterLanes(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DirectWidenedInt64ArgumentEntry");
		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SplitInt64IntrinsicPreservesBothRegisterLanes(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SplitInt64IntrinsicEntry");
		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void UInt64LaneFormatterExecutesOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::UInt64LaneFormatterEntry",
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
	[InlineData("IntegerFormatStringEntry", 10532, 8036, 44432L)]
	[InlineData("InterpolatedIntegerEntry", 10384, 7732, 34756L)]
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
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			entry switch
			{
				"IntegerToStringEntry" => 10,
				"IntegerFormatStringEntry" => 18,
				"InterpolatedIntegerEntry" => 15,
				_ => 2
			},
			result.AllocationStatistics.Count);
		Assert.Equal(
			0,
			result.AllocationStatistics.Max(item => item.SpillFrameBytes));
		if (entry == "InterpolatedIntegerEntry")
		{
			Assert.Equal(2, allocationCalls);
		}
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
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
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

	[Fact]
	public void PackedSdkRectangleOutStoreCopiesAllExpandedFields()
	{
		Assert.Equal(42, CompilerFixtures.PackedRectangleOutStoreEntry());
		var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				ManagedAssemblyPaths =
					[typeof(global::Amiga.Rectangle).Assembly.Location],
				EntryPoint =
					"CopperSharp.Compiler.Tests.CompilerFixtures::PackedRectangleOutStoreEntry",
				Cpu = M68kCpuTarget.M68000,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
					{
						StartAddress = 0x0000_4000,
						Size = 0x0000_2000
					}
			});

		var actual = ExecuteHunk(result, M68kCpuModel.M68000);
		Assert.True(
			actual == 42u,
			$"Packed Rectangle out store returned {actual} instead of 42.");
	}

	[Fact]
	public void NestedExternalValueTypeTokenPreservesAggregateOutStore()
	{
		Assert.Equal(42,
			CompilerFixtures.NestedExternalPackedRectangleOutStoreEntry());
		var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				ManagedAssemblyPaths =
					[typeof(CopperSharp.Compiler.Tests.MultiModule.ExternalValueTypes)
						.Assembly.Location],
				EntryPoint =
					"CopperSharp.Compiler.Tests.CompilerFixtures::" +
					"NestedExternalPackedRectangleOutStoreEntry",
				Cpu = M68kCpuTarget.M68000,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
					{
						StartAddress = 0x0000_4000,
						Size = 0x0000_2000
					}
			});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
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
		foreach (var entry in new[]
		{
			"StaticDelegateEntry",
			"StaticMultiwordDelegateEntry",
			"NonCapturingLambdaEntry"
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
	public void ArrayLoadedImageBlockShapeDelegatesSurviveCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ArrayImageBlockDelegateEntry",
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
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
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
	public void ArrayLoadedImageDescriptorShapeDelegatesPreserveAllFourteenWords(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ArrayDictionaryImageDescriptorDelegateEntry",
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
	public void ExecutesCompleteIntegralMathSurfaceOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShadowMathIntegralSurfaceEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ExecutesIeeeAndSoftwareRoundingMathOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var ieee = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShadowMathIeeeSurfaceEntry",
			floatingPoint: M68kFloatingPointMode.SoftFloat);
		var rounding = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShadowMathSoftwareRoundingEntry",
			floatingPoint: M68kFloatingPointMode.SoftFloat);

		Assert.Equal(42u, ExecuteHunk(ieee, model));
		Assert.Equal(42u, ExecuteHunk(rounding, model));
	}

	[Fact]
	public void FloatingSignNaNRaisesArithmeticExceptionOnTarget()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShadowMathFloatingSignNaNCatchEntry",
			floatingPoint: M68kFloatingPointMode.SoftFloat);

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
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
	public void CheckedInt32AdditionMatchesClrOverflowSemanticsOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var successful = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CheckedInt32AddEntry");
		var overflowing = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CheckedInt32AddOverflowCatchEntry");
		var converting = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CheckedUInt32ToInt32Entry");

		Assert.Equal(42u, ExecuteHunk(successful, model));
		Assert.Equal(42u, ExecuteHunk(overflowing, model));
		Assert.Equal(42u, ExecuteHunk(converting, model));
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
	public void ScopedCStringBufferEncodesAndReleasesNativeStorage(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint nativeBuffer = 0x0000_6000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CStringBufferEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var freed = false;
		bus.RegisterGateway(execBase - 198, state =>
		{
			Assert.Equal(8u, state.D[0]);
			Assert.Equal((uint)global::Amiga.Exec.MemoryFlags.Public, state.D[1]);
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.Equal(nativeBuffer, state.A[1]);
			Assert.Equal(8u, state.D[0]);
			freed = true;
		});

		Assert.Equal(
			nativeBuffer,
			Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal("Amiga", ReadCString(bus, nativeBuffer));
		Assert.True(freed);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void RetainedCStringStorageEncodesAndReleasesNativeStorage(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint nativeBuffer = 0x0000_6000;
		const uint managedAllocator = 0x0000_2800;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CStringStorageEntry",
			Cpu = target,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = managedAllocator
			}
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var managedHeap = 0x0000_4000u;
		var freed = false;
		bus.RegisterGateway(managedAllocator, state =>
		{
			var size = state.D[0];
			state.D[0] = managedHeap;
			managedHeap += (size + 3u) & ~3u;
		});
		bus.RegisterGateway(execBase - 198, state =>
		{
			Assert.Equal(12u, state.D[0]);
			Assert.Equal((uint)global::Amiga.Exec.MemoryFlags.Public, state.D[1]);
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.Equal(nativeBuffer, state.A[1]);
			Assert.Equal(12u, state.D[0]);
			freed = true;
		});

		Assert.Equal(
			nativeBuffer,
			Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal("Retained", ReadCString(bus, nativeBuffer));
		Assert.True(freed);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SealedDisposableUsesOrdinaryFrameworkInterfaceBinding(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = CompileWithAllocator(
			target,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SealedDisposableUsingEntry");

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Fact]
	public void InterfaceTypedDisposableHasStableUnsupportedDiagnostic()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::InterfaceTypedDisposableEntry"
			}));

		Assert.Equal(M68kDiagnosticIds.UnsupportedFrameworkMember, exception.DiagnosticId);
		Assert.Contains("sealed exact receiver", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ListCoreSemanticFixturesMatchHostNet10() =>
		Assert.Multiple(
			() => Assert.Equal(42, CompilerFixtures.ListInt32Entry()),
			() => Assert.Equal(0x0000_002A_5566_7788L, CompilerFixtures.ListInt64Entry()),
				() => Assert.Equal(42, CompilerFixtures.ListRangeExceptionEntry()));

	[Fact]
	public void DictionaryCoreSemanticFixturesMatchHostNet10() =>
		Assert.Multiple(
			() => Assert.Equal(42, CompilerFixtures.DictionaryInt32Entry()),
			() => Assert.Equal(42, CompilerFixtures.DictionaryStringGcEntry()),
			() => Assert.Equal(42, CompilerFixtures.DictionaryStringNullKeyEntry()),
			() => Assert.Equal(42, CompilerFixtures.DictionaryReferenceFreeStructValueEntry()));

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DictionaryCorePreservesCollisionsResizeAndReferencesAcrossForcedCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"DictionaryInt32Entry",
			"DictionaryInt32ReferenceGcEntry"
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
					Size = 0x0000_9000
				}
			});
			var actual = ExecuteHunk(result, model);
			Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DictionaryReferenceFreeStructValuesPreserveAllWordsAcrossResizeAndCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::DictionaryReferenceFreeStructValueEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0001_4000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DictionaryReferenceFreeStructValuesViewPreservesCachedIdentityAcrossCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::DictionaryReferenceFreeStructValuesIdentityEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0001_4000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	// Copper68k's exact MC68020 timing profile does not yet cover PEA (A1),
	// so execute the MC68020-compatible artifact on the MC68040 core.
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68040)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	[InlineData(M68kCpuTarget.M68060, M68kCpuModel.M68040)]
	public void DictionaryStringKeysPreserveOrdinalEqualityAcrossForcedCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::DictionaryStringGcEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_9000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Fact]
	public void DictionaryStringNullKeyThrowsArgumentNullException()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::DictionaryStringNullKeyEntry",
			Cpu = M68kCpuTarget.M68000,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void DictionaryInt32CoreExecutesWithOrdinaryAllocator()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DictionaryInt32Entry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
			var size = state.D[0];
			state.D[0] = heap;
			heap += (size + 3) & ~3u;
		});
		Assert.Equal(
			42u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint));
		Assert.Equal(10, allocations);
	}

	[Fact]
	public void DictionaryStringCoreMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DictionaryStringGcEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 14_200,
			$"String dictionary image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 11_100,
			$"String dictionary code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 62_000,
			$"String dictionary grew to {cycles} MC68000 cycles.");
		Assert.Equal(11, allocations);
		Assert.True(result.AllocationStatistics.Count <= 15);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void DictionaryReferenceFreeStructValueMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DictionaryReferenceFreeStructValueEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
			result.Image.Length <= 20_000,
			$"Reference-free struct dictionary image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 15_500,
			$"Reference-free struct dictionary code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 85_000,
			$"Reference-free struct dictionary grew to {cycles} MC68000 cycles.");
		Assert.Equal(10, allocations);
		Assert.Equal(18, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void GenericStringLookupReturnsCandidateIndexInsteadOfHash(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::GenericStringLookupResultEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ListCoreOperationsPreserveGenericValuesAcrossForcedCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var (entry, expected) in new[]
		{
			("ListInt32Entry", 42u),
			("ListInt64Entry", 42u),
			("ListRangeExceptionEntry", 42u),
			("ListReferenceGcEntry", 42u)
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
					Size = 0x0000_5000
				}
			});

			var actual = ExecuteHunk(result, model);
			Assert.True(actual == expected, $"{entry} returned {actual} instead of {expected}.");
		}
	}

	[Fact]
	public void ListCapacityAndMutationFixturesMatchHostNet10() =>
		Assert.Multiple(
			() => Assert.Equal(42, CompilerFixtures.ListCapacityMutationEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListMutationRangeExceptionEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListDirectEnumerationEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListEmptyEnumerationEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListEnumerationMutationEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListEnumerationCapacityEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListNarrowEnumerationEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListInt64EnumerationEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListNarrowMutationEntry()),
			() => Assert.Equal(
				0x0000_002A_5566_7788L,
				CompilerFixtures.ListInt64MutationEntry()));

	[Fact]
	public void ListIntegralEqualityFixturesMatchHostNet10() =>
		Assert.Multiple(
			() => Assert.Equal(42, CompilerFixtures.ListInt32EqualityEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListInt64EqualityEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListNarrowIntegralEqualityEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListIntegralEqualityMetricEntry()));

	[Fact]
	public void ListNullableIntEqualityFixtureMatchesHostNet10() =>
		Assert.Equal(42, CompilerFixtures.ListNullableIntEqualityEntry());

	[Fact]
	public void PublicIntegralEqualityComparerFixtureMatchesHostNet10() =>
		Assert.Equal(42, CompilerFixtures.PublicIntegralEqualityComparerEntry());

	[Fact]
	public void PublicFloatingEqualityComparerFixtureMatchesHostNet10() =>
		Assert.Equal(42, CompilerFixtures.PublicFloatingEqualityComparerEntry());

	[Fact]
	public void PublicStringEqualityComparerFixtureMatchesHostNet10() =>
		Assert.Equal(42, CompilerFixtures.PublicStringEqualityComparerEntry());

	[Fact]
	public void PublicNullableIntEqualityComparerFixtureMatchesHostNet10() =>
		Assert.Equal(42, CompilerFixtures.PublicNullableIntEqualityComparerEntry());

	[Fact]
	public void PublicSealedReferenceEqualityComparerFixturesMatchHostNet10() =>
		Assert.Multiple(
			() => Assert.Equal(
				42,
				CompilerFixtures.PublicSealedReferenceEqualityComparerEntry()),
			() => Assert.Equal(
				42,
				CompilerFixtures.PublicSealedEquatableEqualityComparerEntry()));

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PublicIntegralEqualityComparerPreservesSingletonAndValueSemantics(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::PublicIntegralEqualityComparerEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PublicFloatingEqualityComparerPreservesNetSemanticsWithoutFpuHelpers(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::PublicFloatingEqualityComparerEntry",
			Cpu = target,
			FloatingPoint = M68kFloatingPointMode.SoftFloat,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.Contains("softfloat", StringComparison.OrdinalIgnoreCase));
		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PublicStringEqualityComparerPreservesOrdinalNetSemantics(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::PublicStringEqualityComparerEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PublicNullableIntEqualityComparerPreservesNetSemantics(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::PublicNullableIntEqualityComparerEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PublicSealedReferenceEqualityComparersPreserveNetSemanticsUnderForcedGc(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"PublicSealedReferenceEqualityComparerEntry",
			"PublicSealedEquatableEqualityComparerEntry"
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
					Size = 0x0000_6000
				}
			});

			Assert.Equal(42u, ExecuteHunk(result, model));
		}
	}

	[Fact]
	public void PublicSealedReferenceEqualityComparersExecuteOnMc68000WithoutForcedGc()
	{
		foreach (var entry in new[]
		{
			"PublicSealedReferenceEqualityComparerEntry",
			"PublicSealedEquatableEqualityComparerEntry"
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint =
					$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
				Cpu = M68kCpuTarget.M68000,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.OnDemand,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_6000
				}
			});

			Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
		}
	}

	[Fact]
	public void PublicSealedReferenceComparerOnlyLinksSelectedEqualityPaths()
	{
		var objectComparer = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PublicSealedReferenceEqualityComparerEntry");
		var equatableComparer = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PublicSealedEquatableEqualityComparerEntry");
		var integralComparer = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PublicIntegralEqualityComparerEntry");

		Assert.Contains(
			objectComparer.Symbols,
			symbol => symbol.Name.Contains("DefaultEqualsObject", StringComparison.Ordinal));
		Assert.DoesNotContain(
			objectComparer.Symbols,
			symbol => symbol.Name.Contains("DefaultEqualsEquatable", StringComparison.Ordinal));
		Assert.Contains(
			equatableComparer.Symbols,
			symbol => symbol.Name.Contains("DefaultEqualsEquatable", StringComparison.Ordinal));
		Assert.DoesNotContain(
			equatableComparer.Symbols,
			symbol => symbol.Name.Contains("DefaultEqualsObject", StringComparison.Ordinal));
		Assert.All(new[] { objectComparer, equatableComparer }, result =>
			Assert.Contains(
				result.Symbols,
				symbol => symbol.Name.Contains(
					"DefaultHashCodeObject",
					StringComparison.Ordinal)));
		Assert.DoesNotContain(
			integralComparer.Symbols,
			symbol => symbol.Name.Contains("DefaultEqualsObject", StringComparison.Ordinal) ||
				symbol.Name.Contains("DefaultEqualsEquatable", StringComparison.Ordinal) ||
				symbol.Name.Contains("DefaultHashCodeObject", StringComparison.Ordinal));
	}

	[Fact]
	public void TypeInitializerCleanupLinksExtendedRootWalkMetadataOnDemand()
	{
		var comparer = CompileEveryAllocationAssembly(
			"CopperSharp.Compiler.Tests.CompilerFixtures::PublicSealedReferenceEqualityComparerEntry");
		var baseline = CompileEveryAllocationAssembly(
			"CopperSharp.Compiler.Tests.CompilerFixtures::DefaultEntry");

		Assert.Contains(
			comparer.Symbols,
			symbol => symbol.Name.Contains("MarkRootsExtended", StringComparison.Ordinal));
		Assert.Contains(
			comparer.Symbols,
			symbol => symbol.Name.Contains("CollectWithRootsExtended", StringComparison.Ordinal));
		Assert.DoesNotContain(
			baseline.Symbols,
			symbol => symbol.Name.Contains("MarkRootsExtended", StringComparison.Ordinal) ||
				symbol.Name.Contains("CollectWithRootsExtended", StringComparison.Ordinal));

		M68kCompilationResult CompileEveryAllocationAssembly(string entry) =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = entry,
				Cpu = M68kCpuTarget.M68000,
				OutputFormat = M68kOutputFormat.Assembly,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_6000
				}
			});
	}

	[Fact]
	public void ListFloatingEqualityFixtureMatchesHostNet10() =>
		Assert.Equal(42, CompilerFixtures.ListFloatingEqualityEntry());

	[Fact]
	public void ListEnumEqualityFixturesMatchHostNet10() =>
		Assert.Multiple(
			() => Assert.Equal(42, CompilerFixtures.ListByteEnumEqualityEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListIntEnumEqualityEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListLongEnumEqualityEntry()));

	[Fact]
	public void ListStringEqualityFixturesMatchHostNet10() =>
		Assert.Multiple(
			() => Assert.Equal(42, CompilerFixtures.ListStringEqualityEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListStringEqualityMetricEntry()));

	[Fact]
	public void ListSealedReferenceObjectEqualityFixturesMatchHostNet10() =>
		Assert.Multiple(
			() => Assert.Equal(42, CompilerFixtures.ListSealedReferenceFallbackEqualityEntry()),
			() => Assert.Equal(42, CompilerFixtures.ListSealedReferenceOverrideEqualityEntry()));

	[Fact]
	public void ListSealedEquatableEqualityFixtureMatchesHostNet10() =>
		Assert.Equal(42, CompilerFixtures.ListSealedEquatableReferenceEntry());

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ListSealedReferenceObjectEqualityPreservesNetSemanticsAcrossForcedCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"ListSealedReferenceFallbackEqualityEntry",
			"ListSealedReferenceOverrideEqualityEntry"
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
					Size = 0x0000_6000
				}
			});

			var actual = ExecuteHunk(result, model);
			Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ListNullableIntEqualityPreservesNetSemanticsAcrossCpuTargets(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ListNullableIntEqualityEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ListFloatingEqualityPreservesNetSemanticsWithoutFpuInstructions(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ListFloatingEqualityEntry",
			Cpu = target,
			FloatingPoint = M68kFloatingPointMode.SoftFloat,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.Contains("softfloat", StringComparison.OrdinalIgnoreCase));
		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ListSealedEquatableEqualityPreservesTypedPrecedenceAcrossForcedCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ListSealedEquatableReferenceEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ListStringEqualityPreservesOrdinalNetSemanticsAcrossForcedCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ListStringEqualityEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		var actual = ExecuteHunk(result, model);
		Assert.True(actual == 42u, $"ListStringEqualityEntry returned {actual} instead of 42.");
	}

	[Theory]
	[MemberData(nameof(ListEnumEqualityCases))]
	public void ListEnumEqualityPreservesUnderlyingRepresentationAcrossCpuTargets(
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
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		var actual = ExecuteHunk(result, model);
		Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
	}

	[Fact]
	public void ListEnumEqualityResolvesUnderlyingTypeFromReferencedModule()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			ManagedAssemblyPaths =
			[
				typeof(CopperSharp.Compiler.Tests.MultiModule.ExternalListState)
					.Assembly.Location
			],
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ListExternalEnumEqualityEntry",
			Cpu = M68kCpuTarget.M68000,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_4000
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Theory]
	[MemberData(nameof(ListIntegralEqualityCases))]
	public void ListIntegralEqualityPreservesNetSemanticsAcrossForcedCollection(
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
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0000_4000,
					Size = 0x0000_6000
				}
			});

		var actual = ExecuteHunk(result, model);
		Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ListEnumerationPreservesPublicSemanticsAcrossForcedCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"ListInt64EnumerationEntry",
			"ListReferenceEnumerationGcEntry",
			"ListDirectEnumerationEntry",
			"ListEmptyEnumerationEntry",
			"ListEnumerationMutationEntry",
			"ListEnumerationCapacityEntry",
			"ListNarrowEnumerationEntry"
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
					Size = 0x0000_5000
				}
			});

			var actual = ExecuteHunk(result, model);
			Assert.True(actual == 42u, $"{entry} returned {actual} instead of 42.");
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ListCapacityAndMutationPreserveValuesAcrossForcedCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var (entry, expected) in new[]
		{
			("ListCapacityMutationEntry", 42u),
			("ListMutationRangeExceptionEntry", 42u),
			("ListNarrowMutationEntry", 42u),
			("ListInt64MutationEntry", 42u),
			("ListReferenceMutationGcEntry", 42u)
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
					Size = 0x0000_5000
				}
			});

			var actual = ExecuteHunk(result, model);
			Assert.True(actual == expected, $"{entry} returned {actual} instead of {expected}.");
		}
	}

	[Theory]
	[InlineData("ListReferenceClearReclaimsEntry")]
	[InlineData("ListReferenceRemoveAtReclaimsEntry")]
	public void ListMutationClearsDeadReferencesBeforeTheNextAllocation(string entry)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
			Cpu = M68kCpuTarget.M68000,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 128
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void TypedInt64ArrayLoadAndStorePreserveRegisterPairOrder(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::TypedInt64ArrayEntry",
			Cpu = target,
			Imports = new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			}
		});

		Assert.Equal(42u, ExecuteHunkWithAllocator(result, model));
	}

	[Fact]
	public void ListDeadReferenceReclamationHeapBudgetHasRetainedControl()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ListReferenceRetentionControlEntry",
			Cpu = M68kCpuTarget.M68000,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 128
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Fact]
	public void ListEnumerationMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ListEnumerationMetricEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			result.Image.Length <= 6_100,
			$"ListEnumerationMetricEntry image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 4_600,
			$"ListEnumerationMetricEntry code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 7_800,
			$"ListEnumerationMetricEntry grew to {cycles} MC68000 cycles.");
		Assert.Equal(2, allocationCalls);
		Assert.Equal(8, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void ListCoreMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ListInt32Entry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			result.Image.Length <= 5_450,
			$"ListInt32Entry image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 4_050,
			$"ListInt32Entry code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 10_100,
			$"ListInt32Entry grew to {cycles} MC68000 cycles.");
		Assert.Equal(3, allocationCalls);
		Assert.Equal(8, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void ListMutationMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ListMutationMetricEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			result.Image.Length <= 8_100,
			$"ListMutationMetricEntry image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 6_150,
			$"ListMutationMetricEntry code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 10_700,
			$"ListMutationMetricEntry grew to {cycles} MC68000 cycles.");
		Assert.Equal(5, allocationCalls);
		Assert.Equal(11, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void ListIntegralEqualityMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ListIntegralEqualityMetricEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			result.Image.Length <= 6_600,
			$"ListIntegralEqualityMetricEntry image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 4_900,
			$"ListIntegralEqualityMetricEntry code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 6_300,
			$"ListIntegralEqualityMetricEntry grew to {cycles} MC68000 cycles.");
		Assert.Equal(2, allocationCalls);
		Assert.Equal(11, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void ListEnumEqualityMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ListIntEnumEqualityEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			result.Image.Length <= 7_050,
			$"ListIntEnumEqualityEntry image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 4_950,
			$"ListIntEnumEqualityEntry code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 8_000,
			$"ListIntEnumEqualityEntry grew to {cycles} MC68000 cycles.");
		Assert.Equal(2, allocationCalls);
		Assert.Equal(10, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void ListStringEqualityMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ListStringEqualityMetricEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			result.Image.Length <= 7_200,
			$"ListStringEqualityMetricEntry image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 5_400,
			$"ListStringEqualityMetricEntry code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 7_300,
			$"ListStringEqualityMetricEntry grew to {cycles} MC68000 cycles.");
		Assert.Equal(2, allocationCalls);
		Assert.Equal(11, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void ListSealedEquatableEqualityMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ListSealedEquatableReferenceEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			result.Image.Length <= 8_500,
			$"ListSealedEquatableReferenceEntry image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 6_000,
			$"ListSealedEquatableReferenceEntry code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 14_000,
			$"ListSealedEquatableReferenceEntry grew to {cycles} MC68000 cycles.");
		Assert.Equal(5, allocationCalls);
		Assert.Equal(13, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Theory]
	[InlineData("ListNullableIntEqualityEntry", M68kFloatingPointMode.Disabled, 8_850, 6_750, 25_500, 2, 12)]
	[InlineData("ListFloatingEqualityEntry", M68kFloatingPointMode.SoftFloat, 13_000, 9_900, 19_500, 4, 20)]
	public void Stage84cMetricsStayWithinInitialMc68000Budgets(
		string entry,
		M68kFloatingPointMode floatingPoint,
		int imageBudget,
		int codeBudget,
		long cycleBudget,
		int expectedAllocations,
		int expectedMethods)
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}",
			floatingPoint: floatingPoint);
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			$"{entry} image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= codeBudget,
			$"{entry} code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= cycleBudget,
			$"{entry} grew to {cycles} MC68000 cycles.");
		Assert.Equal(expectedAllocations, allocationCalls);
		Assert.Equal(expectedMethods, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void PublicIntegralEqualityComparerMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PublicIntegralEqualityComparerEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			result.Image.Length <= 9_200,
			$"Public comparer image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 6_100,
			$"Public comparer code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 8_000,
			$"Public comparer path grew to {cycles} MC68000 cycles.");
		Assert.Equal(5, allocationCalls);
		Assert.Equal(22, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void PublicFloatingEqualityComparerMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PublicFloatingEqualityComparerEntry",
			floatingPoint: M68kFloatingPointMode.SoftFloat);
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			result.Image.Length <= 6_300,
			$"Public floating comparer image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 4_500,
			$"Public floating comparer code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 7_000,
			$"Public floating comparer grew to {cycles} MC68000 cycles.");
		Assert.Equal(2, allocationCalls);
		Assert.Equal(12, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Fact]
	public void PublicStringEqualityComparerMetricsStayWithinInitialMc68000Budgets()
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PublicStringEqualityComparerEntry");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			result.Image.Length <= 4_800,
			$"Public string comparer image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 3_500,
			$"Public string comparer code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 8_500,
			$"Public string comparer grew to {cycles} MC68000 cycles.");
		Assert.Equal(2, allocationCalls);
		Assert.Equal(7, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Theory]
	[InlineData("PublicNullableIntEqualityComparerEntry", 4_500, 3_150, 8_300, 1, 8)]
	[InlineData("PublicSealedReferenceEqualityComparerEntry", 8_200, 5_650, 12_600, 5, 15)]
	[InlineData("PublicSealedEquatableEqualityComparerEntry", 7_200, 5_000, 9_900, 5, 13)]
	public void AdditionalPublicComparerMetricsStayWithinInitialMc68000Budgets(
		string entry,
		int imageBudget,
		int codeBudget,
		long cycleBudget,
		int expectedAllocations,
		int expectedMethods)
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocationCalls = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocationCalls++;
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
			$"{entry} image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= codeBudget,
			$"{entry} code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= cycleBudget,
			$"{entry} grew to {cycles} MC68000 cycles.");
		Assert.Equal(expectedAllocations, allocationCalls);
		Assert.Equal(expectedMethods, result.AllocationStatistics.Count);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleStringOutputUsesCachedApplicationResources(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		const uint outputHandle = 0x0000_0123;
		const uint previousDosBase = 0x0000_7000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleWriteEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var dosBaseSlot = result.Symbols.Single(symbol =>
			symbol.Name == AmigaLibraryBaseSymbols.For("dos.library"));
		bus.WriteLong(HunkLoadAddress + dosBaseSlot.Address, previousDosBase);
		var bytes = new List<byte>();
		var opens = 0;
		var closes = 0;
		var allocations = 0;
		var frees = 0;

		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal("dos.library", ReadCString(bus, state.A[1]));
			Assert.Equal(0u, state.D[0]);
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, state =>
		{
			Assert.Equal(dosBase, state.A[1]);
			closes++;
		});
		bus.RegisterGateway(execBase - 198, state =>
		{
			Assert.Equal(4u, state.D[0]);
			Assert.Equal((uint)global::Amiga.Exec.MemoryFlags.Public, state.D[1]);
			allocations++;
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.Equal(nativeBuffer, state.A[1]);
			Assert.Equal(4u, state.D[0]);
			frees++;
		});
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = outputHandle);
		bus.RegisterGateway(dosBase - 48, state =>
		{
			Assert.Equal(outputHandle, state.D[1]);
			var length = checked((int)state.D[3]);
			for (var index = 0; index < length; index++)
			{
				bytes.Add(bus.Memory[checked((int)state.D[2] + index)]);
			}
			state.D[0] = state.D[3];
		});

		Assert.Equal(
			42u,
			Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.Equal(new byte[] { (byte)'A', 0, (byte)'B', 0xe4, 10, 10 }, bytes);
		Assert.Equal(0, allocations);
		Assert.Equal(0, frees);
		Assert.Equal(
			previousDosBase,
			bus.ReadLong(HunkLoadAddress + dosBaseSlot.Address));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleIntegerOutputReusesFormattingAndCachedResources(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		const uint outputHandle = 0x0000_0123;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsolePrimitiveEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var nativeAllocationSizes = new List<uint>();
		var frees = 0;
		var opens = 0;
		var closes = 0;
		var bytes = new List<byte>();

		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(execBase - 198, state =>
		{
			nativeAllocationSizes.Add(state.D[0]);
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = outputHandle);
		bus.RegisterGateway(dosBase - 48, state =>
		{
			Assert.Equal(outputHandle, state.D[1]);
			var length = checked((int)state.D[3]);
			for (var index = 0; index < length; index++)
			{
				bytes.Add(bus.Memory[checked((int)state.D[2] + index)]);
			}
			state.D[0] = state.D[3];
		});

		Assert.Equal(
			42u,
			Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(
			"-2147483648|4294967295\n-42\n42",
			System.Text.Encoding.Latin1.GetString(bytes.ToArray()));
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
		Assert.Empty(nativeAllocationSizes);
		Assert.Equal(0, frees);
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleInt64OutputUsesPackedLaneFormatting(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		const uint outputHandle = 0x0000_0123;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleInt64Entry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var nativeAllocationSizes = new List<uint>();
		var frees = 0;
		var opens = 0;
		var closes = 0;
		var bytes = new List<byte>();

		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(execBase - 198, state =>
		{
			nativeAllocationSizes.Add(state.D[0]);
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = outputHandle);
		bus.RegisterGateway(dosBase - 48, state =>
		{
			Assert.Equal(outputHandle, state.D[1]);
			var length = checked((int)state.D[3]);
			for (var index = 0; index < length; index++)
			{
				bytes.Add(bus.Memory[checked((int)state.D[2] + index)]);
			}
			state.D[0] = state.D[3];
		});

		Assert.Equal(
			42u,
			Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(
			"-9223372036854775808|18446744073709551615\n-42\n42",
			System.Text.Encoding.Latin1.GetString(bytes.ToArray()));
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
		Assert.DoesNotContain("ShadowInt64::ToString", result.Map);
		Assert.DoesNotContain("ShadowUInt64::ToString", result.Map);
		Assert.Empty(nativeAllocationSizes);
		Assert.Equal(0, frees);
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleIntegerOutputDoesNotDependOnNativeAllocation(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"PortableConsolePrimitiveAllocationFailureEntry",
			"PortableConsoleInt64AllocationFailureEntry"
		})
		{
			const uint execBase = 0x0000_3000;
			const uint dosBase = 0x0000_5000;
			var result = Compile(
				target,
				M68kOutputFormat.Hunk,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
			var bus = CreateHunkBus(result);
			bus.WriteLong(4, execBase);
			var nativeAllocations = 0;
			var opens = 0;
			var closes = 0;
			bus.RegisterGateway(execBase - 552, state =>
			{
				opens++;
				state.D[0] = dosBase;
			});
			bus.RegisterGateway(execBase - 414, _ => closes++);
			bus.RegisterGateway(execBase - 198, state =>
			{
				nativeAllocations++;
				state.D[0] = 0;
			});
			bus.RegisterGateway(dosBase - 60, state => state.D[0] = 0x100);
			bus.RegisterGateway(dosBase - 48, state => state.D[0] = state.D[3]);

			Assert.Equal(
				1u,
				Execute(bus, model, HunkLoadAddress + result.EntryPoint));
			Assert.Equal(0, nativeAllocations);
			Assert.Equal(1, opens);
			Assert.Equal(1, closes);
			Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleBooleanOutputUsesStaticAllocationFreeBytes(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint outputHandle = 0x0000_0123;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleBooleanEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var frees = 0;
		var opens = 0;
		var closes = 0;
		var writes = 0;
		var bytes = new List<byte>();
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = 0x0000_6000;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = outputHandle);
		bus.RegisterGateway(dosBase - 48, state =>
		{
			writes++;
			Assert.Equal(outputHandle, state.D[1]);
			var length = checked((int)state.D[3]);
			for (var index = 0; index < length; index++)
			{
				bytes.Add(bus.Memory[checked((int)state.D[2] + index)]);
			}
			state.D[0] = state.D[3];
		});

		Assert.Equal(42u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(
			"TrueFalse\nTrue\nFalse",
			System.Text.Encoding.Latin1.GetString(bytes.ToArray()));
		Assert.Equal(0, allocations);
		Assert.Equal(0, frees);
		Assert.Equal(4, writes);
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleCharacterOutputUsesDeclaredLatin1Policy(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		const uint outputHandle = 0x0000_0123;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleCharacterEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocationSizes = new List<uint>();
		var frees = 0;
		var opens = 0;
		var closes = 0;
		var writes = 0;
		var bytes = new List<byte>();
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocationSizes.Add(state.D[0]);
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = outputHandle);
		bus.RegisterGateway(dosBase - 48, state =>
		{
			writes++;
			Assert.Equal(outputHandle, state.D[1]);
			var length = checked((int)state.D[3]);
			for (var index = 0; index < length; index++)
			{
				bytes.Add(bus.Memory[checked((int)state.D[2] + index)]);
			}
			state.D[0] = state.D[3];
		});

		Assert.Equal(42u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(new byte[] { 0, 0xe4, (byte)'?', 10, (byte)'A' }, bytes);
		Assert.Empty(allocationSizes);
		Assert.Equal(0, frees);
		Assert.Equal(4, writes);
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleReadUsesBufferedLatin1Input(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		const uint inputHandle = 0x0000_0123;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleReadEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var allocations = 0;
		var frees = 0;
		var inputs = 0;
		var reads = 0;

		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal("dos.library", ReadCString(bus, state.A[1]));
			Assert.Equal(0u, state.D[0]);
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, state =>
		{
			Assert.Equal(dosBase, state.A[1]);
			closes++;
		});
		bus.RegisterGateway(execBase - 198, state =>
		{
			Assert.Equal(128u, state.D[0]);
			Assert.Equal((uint)global::Amiga.Exec.MemoryFlags.Public, state.D[1]);
			allocations++;
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.Equal(nativeBuffer, state.A[1]);
			Assert.Equal(128u, state.D[0]);
			frees++;
		});
		bus.RegisterGateway(dosBase - 54, state =>
		{
			inputs++;
			state.D[0] = inputHandle;
		});
		bus.RegisterGateway(dosBase - 42, state =>
		{
			Assert.Equal(inputHandle, state.D[1]);
			Assert.Equal(nativeBuffer, state.D[2]);
			Assert.Equal(128u, state.D[3]);
			if (reads++ == 0)
			{
				bus.Memory[checked((int)state.D[2])] = (byte)'A';
				bus.Memory[checked((int)state.D[2] + 1)] = 0;
				bus.Memory[checked((int)state.D[2] + 2)] = 0xe4;
				state.D[0] = 3;
			}
			else
			{
				state.D[0] = 0;
			}
		});

		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		if (target == M68kCpuTarget.M68000)
		{
			Assert.True(
				result.Image.Length <= 4_000,
				$"Console.Read image grew to {result.Image.Length} bytes.");
			Assert.True(
				result.Code.Length <= 3_000,
				$"Console.Read code grew to {result.Code.Length} bytes.");
			Assert.True(
				cycles <= 10_000,
				$"Console.Read grew to {cycles} MC68000 cycles.");
			Assert.Equal(8, result.AllocationStatistics.Count);
			Assert.All(
				result.AllocationStatistics,
				item => Assert.Equal(0, item.SpillFrameBytes));
		}
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.Equal(1, allocations);
		Assert.Equal(1, frees);
		Assert.Equal(1, inputs);
		Assert.Equal(2, reads);
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name.EndsWith("ConsolePal::InitializeInput", StringComparison.Ordinal));
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name.EndsWith("ConsolePal::ShutdownInput", StringComparison.Ordinal));
	}

	[Fact]
	public void FreestandingConsoleReadUsesScopedNativeOwnership()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		const uint inputHandle = 0x0000_0123;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleReadEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Freestanding
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var allocations = 0;
		var frees = 0;
		var inputs = 0;
		var reads = 0;
		byte[] input = [(byte)'A', 0, 0xe4];

		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(execBase - 198, state =>
		{
			Assert.Equal(4u, state.D[0]);
			allocations++;
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.Equal(4u, state.D[0]);
			frees++;
		});
		bus.RegisterGateway(dosBase - 54, state =>
		{
			inputs++;
			state.D[0] = inputHandle;
		});
		bus.RegisterGateway(dosBase - 42, state =>
		{
			if (reads < input.Length)
			{
				bus.Memory[checked((int)state.D[2])] = input[reads];
				state.D[0] = 1;
			}
			else
			{
				state.D[0] = 0;
			}
			reads++;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(4, opens);
		Assert.Equal(4, closes);
		Assert.Equal(4, allocations);
		Assert.Equal(4, frees);
		Assert.Equal(4, inputs);
		Assert.Equal(4, reads);
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("ConsolePal::Initialize", StringComparison.Ordinal));
	}

	[Theory]
	[MemberData(nameof(PortableConsoleReadLineCases))]
	public void PortableConsoleReadLineHandlesCrLfCrAndEof(
		M68kCpuTarget target,
		M68kCpuModel model,
		M68kGcSweepStrategy sweepStrategy,
		bool assertProductionBudgets)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		const uint managedArena = 0x0020_0000;
		const uint managedArenaSize = 0x0000_2000;
		const uint inputHandle = 0x0000_0123;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleReadLineEntry",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = sweepStrategy,
			Heap = new M68kHeapOptions { Size = managedArenaSize }
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var nativeAllocations = 0;
		var nativeFrees = 0;
		var arenaAllocations = 0;
		var arenaFrees = 0;
		var inputs = 0;
		var reads = 0;

		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => { });
		bus.RegisterGateway(execBase - 198, state =>
		{
			if (state.D[0] == 128)
			{
				nativeAllocations++;
				state.D[0] = nativeBuffer;
			}
			else
			{
				Assert.Equal(managedArenaSize, state.D[0]);
				arenaAllocations++;
				state.D[0] = managedArena;
			}
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			if (state.A[1] == nativeBuffer)
			{
				nativeFrees++;
			}
			else
			{
				Assert.Equal(managedArena, state.A[1]);
				arenaFrees++;
			}
		});
		bus.RegisterGateway(dosBase - 54, state =>
		{
			inputs++;
			state.D[0] = inputHandle;
		});
		bus.RegisterGateway(dosBase - 42, state =>
		{
			Assert.Equal(inputHandle, state.D[1]);
			Assert.Equal(nativeBuffer, state.D[2]);
			Assert.Equal(128u, state.D[3]);
			if (reads++ == 0)
			{
				byte[] input = [(byte)'A', 0, 0xe4, 13, 10, (byte)'B', 10, 13, (byte)'C'];
				for (var index = 0; index < input.Length; index++)
				{
					bus.Memory[checked((int)state.D[2] + index)] = input[index];
				}
				state.D[0] = (uint)input.Length;
			}
			else
			{
				state.D[0] = 0;
			}
		});

		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		if (assertProductionBudgets && target == M68kCpuTarget.M68000)
		{
			Assert.True(
				result.Image.Length <= 10_500,
				$"Console.ReadLine image grew to {result.Image.Length} bytes.");
			Assert.True(
				result.Code.Length <= 7_600,
				$"Console.ReadLine code grew to {result.Code.Length} bytes.");
			Assert.True(
				cycles <= 50_000,
				$"Console.ReadLine grew to {cycles} MC68000 cycles.");
			Assert.Equal(27, result.AllocationStatistics.Count);
			Assert.All(
				result.AllocationStatistics,
				item => Assert.Equal(0, item.SpillFrameBytes));
		}
		Assert.Equal(1, nativeAllocations);
		Assert.Equal(1, nativeFrees);
		Assert.Equal(1, arenaAllocations);
		Assert.Equal(1, arenaFrees);
		Assert.Equal(1, inputs);
		Assert.Equal(2, reads);
		Assert.Contains("CopperSharp.Runtime.ManagedPool", result.Map);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleInputFailuresAreCatchable(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		foreach (var scenario in new[] { "missing-handle", "read-error", "allocation-failure" })
		{
			var entry = scenario == "allocation-failure"
				? "PortableConsoleInputAllocationFailureEntry"
				: "PortableConsoleInputIOExceptionEntry";
			var result = Compile(
				target,
				M68kOutputFormat.Hunk,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
			var bus = CreateHunkBus(result);
			bus.WriteLong(4, execBase);
			var closes = 0;
			var frees = 0;
			var reads = 0;
			bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
			bus.RegisterGateway(execBase - 414, _ => closes++);
			bus.RegisterGateway(execBase - 198, state =>
				state.D[0] = scenario == "allocation-failure" ? 0u : nativeBuffer);
			bus.RegisterGateway(execBase - 210, _ => frees++);
			bus.RegisterGateway(dosBase - 54, state =>
				state.D[0] = scenario == "missing-handle" ? 0u : 0x123u);
			bus.RegisterGateway(dosBase - 42, state =>
			{
				reads++;
				state.D[0] = uint.MaxValue;
			});

			var returnValue = Execute(bus, model, HunkLoadAddress + result.EntryPoint);
			Assert.True(
				returnValue == 42,
				$"{scenario} returned {returnValue} after {reads} reads instead of catching the expected failure.");
			Assert.Equal(1, closes);
			Assert.Equal(scenario == "read-error" ? 1 : 0, frees);
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleCharacterOutputDoesNotDependOnNativeAllocation(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleCharacterAllocationFailureEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var closes = 0;
		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = 0x100);
		bus.RegisterGateway(dosBase - 48, state => state.D[0] = state.D[3]);

		Assert.Equal(1u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, allocations);
		Assert.Equal(1, closes);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleBooleanShortWriteRaisesCatchableIOException(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleBooleanShortWriteEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var closes = 0;
		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = 0x100);
		bus.RegisterGateway(dosBase - 48, state => state.D[0] = state.D[3] - 1);

		Assert.Equal(42u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, closes);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleOpenFailureRaisesCatchableIOException(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleOpenFailureEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		bus.RegisterGateway(execBase - 552, state => state.D[0] = 0);

		Assert.Equal(
			42u,
			Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Fact]
	public void PortableConsoleShortWriteClosesCachedLibraryWithoutNativeBuffer()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleOpenFailureEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var closed = 0;
		var freed = 0;
		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => closed++);
		bus.RegisterGateway(execBase - 198, state => state.D[0] = nativeBuffer);
		bus.RegisterGateway(execBase - 210, _ => freed++);
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = 0x100);
		bus.RegisterGateway(dosBase - 48, state => state.D[0] = state.D[3] - 1);

		Assert.Equal(
			42u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, freed);
		Assert.Equal(1, closed);
	}

	[Fact]
	public void PortableConsoleMissingOutputRaisesIOExceptionAndClosesCachedDosLibrary()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleOpenFailureEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var closed = 0;
		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => closed++);
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = 0);

		Assert.Equal(
			42u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, closed);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableFileSystemExistenceUsesLatin1LockExamineAndCleanup(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemExistsEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var frees = 0;
		var locks = 0;
		var examines = 0;
		var unlocks = 0;
		var allocations = new List<uint>();
		var entryTypes = new Dictionary<uint, int>();

		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal("dos.library", ReadCString(bus, state.A[1]));
			Assert.Equal(0u, state.D[0]);
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, state =>
		{
			Assert.Equal(dosBase, state.A[1]);
			closes++;
		});
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations.Add(state.D[0]);
			Assert.Equal((uint)global::Amiga.Exec.MemoryFlags.Public, state.D[1]);
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.Equal(nativePath, state.A[1]);
			Assert.Equal(allocations[frees], state.D[0]);
			frees++;
		});
		bus.RegisterGateway(dosBase - 84, state =>
		{
			var path = ReadCString(bus, state.D[1]);
			Assert.Equal(unchecked((uint)-2), state.D[2]);
			var handle = (uint)(0x100 + locks);
			entryTypes[handle] = path switch
			{
				".coppersharp-portable-file" => -3,
				".coppersharp-portable-directory" => 2,
				_ => throw new Xunit.Sdk.XunitException($"Unexpected path '{path}'.")
			};
			locks++;
			state.D[0] = handle;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			Assert.True(entryTypes.TryGetValue(state.D[1], out var entryType));
			var offset = checked((int)state.D[2] + global::Amiga.FileInfoBlock.DirEntryTypeOffset);
			var rawEntryType = unchecked((uint)entryType);
			bus.Memory[offset] = (byte)(rawEntryType >> 24);
			bus.Memory[offset + 1] = (byte)(rawEntryType >> 16);
			bus.Memory[offset + 2] = (byte)(rawEntryType >> 8);
			bus.Memory[offset + 3] = (byte)rawEntryType;
			examines++;
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, state =>
		{
			Assert.True(entryTypes.Remove(state.D[1]));
			unlocks++;
		});

		long cycles = 0;
		var returnValue = Execute(
			bus,
			model,
			HunkLoadAddress + result.EntryPoint,
			afterReturn: state => cycles = state.Cycles);
		Assert.True(
			returnValue == 42,
			$"Existence probes returned {returnValue}; opens={opens}, allocations={allocations.Count}, locks={locks}, examines={examines}, unlocks={unlocks}, frees={frees}.");
		if (target == M68kCpuTarget.M68000)
		{
			Assert.True(
				result.Image.Length <= 7_000,
				$"File-system existence image grew to {result.Image.Length} bytes.");
			Assert.True(
				result.Code.Length <= 5_000,
				$"File-system existence code grew to {result.Code.Length} bytes.");
			Assert.True(
				cycles <= 200_000,
				$"File-system existence probes grew to {cycles} MC68000 cycles.");
			Assert.Equal(14, result.AllocationStatistics.Count);
			Assert.True(
				result.AllocationStatistics.Max(item => item.SpillFrameBytes) <= 4,
				"File-system existence probes exceeded the pinned four-byte spill ceiling.");
		}
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.Equal(4, allocations.Count);
		Assert.Equal(4, frees);
		Assert.Equal(4, locks);
		Assert.Equal(4, examines);
		Assert.Equal(4, unlocks);
		Assert.Empty(entryTypes);
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Initialize", StringComparison.Ordinal));
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Shutdown", StringComparison.Ordinal));
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.Contains("ConsolePal", StringComparison.Ordinal));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableFileSystemDeletionUsesLockExamineDeleteAndCleanup(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var allocations = 0;
		var frees = 0;
		var locks = 0;
		var examines = 0;
		var unlocks = 0;
		var ioErrors = 0;
		var deletedPaths = new List<string>();
		var entryTypes = new Dictionary<uint, int>();
		var nextHandle = 0x100u;

		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal("dos.library", ReadCString(bus, state.A[1]));
			Assert.Equal(0u, state.D[0]);
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.Equal(nativePath, state.A[1]);
			frees++;
		});
		bus.RegisterGateway(dosBase - 84, state =>
		{
			var path = ReadCString(bus, state.D[1]);
			Assert.Equal(unchecked((uint)-2), state.D[2]);
			locks++;
			if (path == ".coppersharp-portable-missing")
			{
				state.D[0] = 0;
				return;
			}
			var handle = nextHandle++;
			entryTypes[handle] = path switch
			{
				".coppersharp-portable-file" => -3,
				".coppersharp-portable-directory" => 2,
				_ => throw new Xunit.Sdk.XunitException($"Unexpected path '{path}'.")
			};
			state.D[0] = handle;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			Assert.True(entryTypes.TryGetValue(state.D[1], out var entryType));
			var offset = checked((int)state.D[2] + global::Amiga.FileInfoBlock.DirEntryTypeOffset);
			var rawEntryType = unchecked((uint)entryType);
			bus.Memory[offset] = (byte)(rawEntryType >> 24);
			bus.Memory[offset + 1] = (byte)(rawEntryType >> 16);
			bus.Memory[offset + 2] = (byte)(rawEntryType >> 8);
			bus.Memory[offset + 3] = (byte)rawEntryType;
			examines++;
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, state =>
		{
			Assert.True(entryTypes.Remove(state.D[1]));
			unlocks++;
		});
		bus.RegisterGateway(dosBase - 72, state =>
		{
			deletedPaths.Add(ReadCString(bus, state.D[1]));
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 132, state =>
		{
			ioErrors++;
			state.D[0] = (uint)global::Amiga.DOS.Error.ObjectNotFound;
		});

		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		if (target == M68kCpuTarget.M68000)
		{
			Assert.True(result.Image.Length <= 8_200,
				$"File-system deletion image grew to {result.Image.Length} bytes.");
			Assert.True(result.Code.Length <= 5_800,
				$"File-system deletion code grew to {result.Code.Length} bytes.");
			Assert.True(cycles <= 150_000,
				$"File-system deletion grew to {cycles} MC68000 cycles.");
			Assert.Equal(20, result.AllocationStatistics.Count);
			Assert.Equal(
				0,
				result.AllocationStatistics.Max(item => item.SpillFrameBytes));
		}
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.Equal(3, allocations);
		Assert.Equal(3, frees);
		Assert.Equal(3, locks);
		Assert.Equal(2, examines);
		Assert.Equal(2, unlocks);
		Assert.Equal(1, ioErrors);
		Assert.Equal(
			[".coppersharp-portable-file", ".coppersharp-portable-directory"],
			deletedPaths);
		Assert.Empty(entryTypes);
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableDirectoryDeletePreservesDirectoryNotFoundIOExceptionInheritance(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteDirectoryNotFoundEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var frees = 0;
		var locks = 0;
		var ioErrors = 0;
		var closes = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state =>
		{
			locks++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(dosBase - 132, state =>
		{
			ioErrors++;
			state.D[0] = (uint)global::Amiga.DOS.Error.ObjectNotFound;
		});

		Assert.Equal(42u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(2, allocations);
		Assert.Equal(2, frees);
		Assert.Equal(2, locks);
		Assert.Equal(2, ioErrors);
		Assert.Equal(1, closes);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableFileDeletePreservesUnauthorizedSystemExceptionInheritance(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteUnauthorizedEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var frees = 0;
		var locks = 0;
		var examines = 0;
		var unlocks = 0;
		var deletes = 0;
		var closes = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state =>
		{
			locks++;
			state.D[0] = (uint)(0x100 + locks);
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			var offset = checked((int)state.D[2] + global::Amiga.FileInfoBlock.DirEntryTypeOffset);
			bus.Memory[offset + 3] = 2;
			examines++;
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, _ => unlocks++);
		bus.RegisterGateway(dosBase - 72, _ => deletes++);

		Assert.Equal(42u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(2, allocations);
		Assert.Equal(2, frees);
		Assert.Equal(2, locks);
		Assert.Equal(2, examines);
		Assert.Equal(2, unlocks);
		Assert.Equal(0, deletes);
		Assert.Equal(1, closes);
	}

	[Theory]
	[MemberData(nameof(PortableFileSystemNativeFailureCases))]
	public void PortableFileSystemNativeFailuresReturnFalseAndReleaseOwnership(
		string scenario,
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		const uint lockHandle = 0x0000_0100;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemMissingEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var allocations = 0;
		var frees = 0;
		var locks = 0;
		var examines = 0;
		var unlocks = 0;

		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.Equal(nativePath, state.A[1]);
			frees++;
		});
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = scenario == "open-failure" ? 0u : dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state =>
		{
			locks++;
			state.D[0] = scenario == "lock-failure" ? 0u : lockHandle;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			Assert.Equal(lockHandle, state.D[1]);
			examines++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(dosBase - 90, state =>
		{
			Assert.Equal(lockHandle, state.D[1]);
			unlocks++;
		});

		Assert.Equal(
			42u,
			Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, allocations);
		Assert.Equal(1, frees);
		Assert.Equal(1, opens);
		Assert.Equal(scenario == "open-failure" ? 0 : 1, closes);
		Assert.Equal(scenario == "open-failure" ? 0 : 1, locks);
		Assert.Equal(scenario == "examine-failure" ? 1 : 0, examines);
		Assert.Equal(scenario == "examine-failure" ? 1 : 0, unlocks);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableFileSystemPathAllocationFailureIsCatchable(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemExistsAllocationFailureEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var opens = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = 0;
		});

		Assert.Equal(42u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, allocations);
		Assert.Equal(0, opens);
	}

	[Fact]
	public void PortableFileSystemDeletionRejectsInvalidPathsBeforeNativeCalls()
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteInvalidPathEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var opens = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = 0;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, allocations);
		Assert.Equal(0, opens);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableFileGetAttributesMapsAmigaMetadataOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileGetAttributesEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var allocations = 0;
		var frees = 0;
		var locks = 0;
		var examines = 0;
		var unlocks = 0;
		var handles = new Dictionary<uint, (int EntryType, uint Protection)>();
		var nextHandle = 0x100u;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state =>
		{
			var path = ReadCString(bus, state.D[1]);
			var handle = nextHandle;
			nextHandle += 4;
			handles[handle] = path switch
			{
				".coppersharp-portable-file" =>
					(-3, (uint)(global::Amiga.FileProtection.Write |
						global::Amiga.FileProtection.Archive)),
				".coppersharp-portable-directory" => (2, 0u),
				_ => throw new Xunit.Sdk.XunitException($"Unexpected path '{path}'.")
			};
			locks++;
			state.D[0] = handle;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			Assert.True(handles.TryGetValue(state.D[1], out var metadata));
			var entryOffset = checked(
				(int)state.D[2] + global::Amiga.FileInfoBlock.DirEntryTypeOffset);
			var entryType = unchecked((uint)metadata.EntryType);
			bus.Memory[entryOffset] = (byte)(entryType >> 24);
			bus.Memory[entryOffset + 1] = (byte)(entryType >> 16);
			bus.Memory[entryOffset + 2] = (byte)(entryType >> 8);
			bus.Memory[entryOffset + 3] = (byte)entryType;
			var protectionOffset = checked(
				(int)state.D[2] + global::Amiga.FileInfoBlock.ProtectionOffset);
			bus.Memory[protectionOffset] = (byte)(metadata.Protection >> 24);
			bus.Memory[protectionOffset + 1] = (byte)(metadata.Protection >> 16);
			bus.Memory[protectionOffset + 2] = (byte)(metadata.Protection >> 8);
			bus.Memory[protectionOffset + 3] = (byte)metadata.Protection;
			examines++;
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, state =>
		{
			Assert.True(handles.Remove(state.D[1]));
			unlocks++;
		});

		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		if (target == M68kCpuTarget.M68000)
		{
			Assert.True(result.Image.Length <= 7_200,
				$"File.GetAttributes image grew to {result.Image.Length} bytes.");
			Assert.True(result.Code.Length <= 5_300,
				$"File.GetAttributes code grew to {result.Code.Length} bytes.");
			Assert.True(cycles <= 95_000,
				$"File.GetAttributes grew to {cycles} MC68000 cycles.");
			Assert.Equal(15, result.AllocationStatistics.Count);
			Assert.Equal(
				0,
				result.AllocationStatistics.Max(static item => item.SpillFrameBytes));
		}
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.Equal(2, allocations);
		Assert.Equal(2, frees);
		Assert.Equal(2, locks);
		Assert.Equal(2, examines);
		Assert.Equal(2, unlocks);
		Assert.Empty(handles);
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
	}

	[Fact]
	public void PortableFileGetAttributesRejectsInvalidPathsBeforeNativeCalls()
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileGetAttributesInvalidPathEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var opens = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = 0;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, allocations);
		Assert.Equal(0, opens);
	}

	[Fact]
	public void PortableFileGetAttributesAllocationFailureIsCatchable()
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileGetAttributesOutOfMemoryEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		bus.RegisterGateway(execBase - 198, state => state.D[0] = 0);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = 0;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, opens);
	}

	[Theory]
	[InlineData("open-failure", "PortableFileGetAttributesIOExceptionEntry", 1)]
	[InlineData("missing-file", "PortableFileGetAttributesFileNotFoundEntry", 2)]
	[InlineData("missing-directory", "PortableFileGetAttributesDirectoryNotFoundEntry", 2)]
	[InlineData("protected", "PortableFileGetAttributesUnauthorizedEntry", 2)]
	[InlineData("examine-failure", "PortableFileGetAttributesIOExceptionEntry", 1)]
	[InlineData("native-no-free-store", "PortableFileGetAttributesOutOfMemoryEntry", 1)]
	public void PortableFileGetAttributesFailuresReleaseEveryOwnedResource(
		string scenario,
		string fixture,
		int calls)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		const uint lockHandle = 0x0000_0100;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{fixture}");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var frees = 0;
		var opens = 0;
		var closes = 0;
		var locks = 0;
		var examines = 0;
		var unlocks = 0;
		var ioErrors = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = scenario == "open-failure" ? 0u : dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state =>
		{
			locks++;
			state.D[0] = scenario == "examine-failure" ? lockHandle : 0u;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			examines++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(dosBase - 90, _ => unlocks++);
		bus.RegisterGateway(dosBase - 132, state =>
		{
			ioErrors++;
			state.D[0] = (uint)(scenario switch
			{
				"missing-file" => global::Amiga.DOS.Error.ObjectNotFound,
				"missing-directory" => global::Amiga.DOS.Error.DirectoryNotFound,
				"protected" => global::Amiga.DOS.Error.ReadProtected,
				"native-no-free-store" => global::Amiga.DOS.Error.NoFreeStore,
				_ => global::Amiga.DOS.Error.ObjectInUse
			});
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(calls, allocations);
		Assert.Equal(calls, frees);
		Assert.Equal(1, opens);
		Assert.Equal(scenario == "open-failure" ? 0 : 1, closes);
		Assert.Equal(scenario == "open-failure" ? 0 : calls, locks);
		Assert.Equal(scenario == "examine-failure" ? calls : 0, examines);
		Assert.Equal(scenario == "examine-failure" ? calls : 0, unlocks);
		Assert.Equal(scenario == "open-failure" ? 0 : calls, ioErrors);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableFileSetAttributesPreservesUnmappedProtectionOnEveryCpu(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		const uint lockHandle = 0x0000_0100;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSetAttributesEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var allocations = 0;
		var frees = 0;
		var locks = 0;
		var examines = 0;
		var unlocks = 0;
		var protections = new List<uint>();
		var currentProtection = (uint)(
			global::Amiga.FileProtection.Delete |
			global::Amiga.FileProtection.Execute |
			global::Amiga.FileProtection.Pure);
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state =>
		{
			Assert.Equal(".coppersharp-portable-file", ReadCString(bus, state.D[1]));
			locks++;
			state.D[0] = lockHandle;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			bus.WriteLong(
				state.D[2] + global::Amiga.FileInfoBlock.ProtectionOffset,
				currentProtection);
			examines++;
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, _ => unlocks++);
		bus.RegisterGateway(dosBase - 186, state =>
		{
			Assert.Equal(".coppersharp-portable-file", ReadCString(bus, state.D[1]));
			currentProtection = state.D[2];
			protections.Add(currentProtection);
			state.D[0] = 1;
		});

		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		if (target == M68kCpuTarget.M68000)
		{
			Assert.True(result.Image.Length <= 7_000,
				$"File.SetAttributes image grew to {result.Image.Length} bytes.");
			Assert.True(result.Code.Length <= 5_200,
				$"File.SetAttributes code grew to {result.Code.Length} bytes.");
			Assert.True(cycles <= 88_000,
				$"File.SetAttributes grew to {cycles} MC68000 cycles.");
			Assert.Equal(14, result.AllocationStatistics.Count);
			Assert.Equal(
				4,
				result.AllocationStatistics.Max(static item => item.SpillFrameBytes));
		}
		var preservedProtection = (uint)(
			global::Amiga.FileProtection.Delete |
			global::Amiga.FileProtection.Execute |
			global::Amiga.FileProtection.Pure);
		Assert.Equal(
			[
				preservedProtection |
					(uint)global::Amiga.FileProtection.Write |
					(uint)global::Amiga.FileProtection.Archive,
				preservedProtection
			],
			protections);
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.Equal(2, allocations);
		Assert.Equal(2, frees);
		Assert.Equal(2, locks);
		Assert.Equal(2, examines);
		Assert.Equal(2, unlocks);
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
	}

	[Fact]
	public void PortableFileSetAttributesRejectsInvalidInputBeforeNativeCalls()
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSetAttributesInvalidEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var opens = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = 0;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, allocations);
		Assert.Equal(0, opens);
	}

	[Fact]
	public void PortableFileSetAttributesIgnoresKnownUnrepresentableFlags()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		const uint lockHandle = 0x0000_0100;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSetAttributesKnownUnsupportedFlagsEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var sets = 0;
		bus.RegisterGateway(execBase - 198, state => state.D[0] = nativePath);
		bus.RegisterGateway(execBase - 210, _ => { });
		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => { });
		bus.RegisterGateway(dosBase - 84, state => state.D[0] = lockHandle);
		bus.RegisterGateway(dosBase - 102, state =>
		{
			bus.WriteLong(
				state.D[2] + global::Amiga.FileInfoBlock.ProtectionOffset,
				(uint)(global::Amiga.FileProtection.Delete |
					global::Amiga.FileProtection.Execute));
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, _ => { });
		bus.RegisterGateway(dosBase - 186, state =>
		{
			sets++;
			state.D[0] = 1;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, sets);
	}

	[Fact]
	public void PortableFileSetAttributesAllocationFailureIsCatchable()
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSetAttributesOutOfMemoryEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		bus.RegisterGateway(execBase - 198, state => state.D[0] = 0);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = 0;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, opens);
	}

	[Theory]
	[InlineData("open-failure", "PortableFileSetAttributesIOExceptionEntry", 1)]
	[InlineData("missing-file", "PortableFileSetAttributesFileNotFoundEntry", 2)]
	[InlineData("missing-directory", "PortableFileSetAttributesDirectoryNotFoundEntry", 2)]
	[InlineData("protected", "PortableFileSetAttributesUnauthorizedEntry", 2)]
	[InlineData("examine-failure", "PortableFileSetAttributesIOExceptionEntry", 1)]
	[InlineData("set-failure", "PortableFileSetAttributesIOExceptionEntry", 1)]
	[InlineData("native-no-free-store", "PortableFileSetAttributesOutOfMemoryEntry", 1)]
	public void PortableFileSetAttributesFailuresReleaseEveryOwnedResource(
		string scenario,
		string fixture,
		int calls)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		const uint lockHandle = 0x0000_0100;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{fixture}");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var frees = 0;
		var opens = 0;
		var closes = 0;
		var locks = 0;
		var examines = 0;
		var unlocks = 0;
		var sets = 0;
		var ioErrors = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = scenario == "open-failure" ? 0u : dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state =>
		{
			locks++;
			state.D[0] = scenario is "examine-failure" or "protected" or "set-failure"
				? lockHandle
				: 0u;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			examines++;
			if (scenario is "protected" or "set-failure")
			{
				bus.WriteLong(
					state.D[2] + global::Amiga.FileInfoBlock.ProtectionOffset,
					0u);
				state.D[0] = 1;
			}
			else
			{
				state.D[0] = 0;
			}
		});
		bus.RegisterGateway(dosBase - 90, _ => unlocks++);
		bus.RegisterGateway(dosBase - 186, state =>
		{
			sets++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(dosBase - 132, state =>
		{
			ioErrors++;
			state.D[0] = (uint)(scenario switch
			{
				"missing-file" => global::Amiga.DOS.Error.ObjectNotFound,
				"missing-directory" => global::Amiga.DOS.Error.DirectoryNotFound,
				"protected" => global::Amiga.DOS.Error.WriteProtected,
				"native-no-free-store" => global::Amiga.DOS.Error.NoFreeStore,
				_ => global::Amiga.DOS.Error.ObjectInUse
			});
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(calls, allocations);
		Assert.Equal(calls, frees);
		Assert.Equal(1, opens);
		Assert.Equal(scenario == "open-failure" ? 0 : 1, closes);
		Assert.Equal(scenario == "open-failure" ? 0 : calls, locks);
		var reachesExamine = scenario is "examine-failure" or "protected" or "set-failure";
		Assert.Equal(reachesExamine ? calls : 0, examines);
		Assert.Equal(reachesExamine ? calls : 0, unlocks);
		Assert.Equal(scenario is "protected" or "set-failure" ? calls : 0, sets);
		Assert.Equal(scenario == "open-failure" ? 0 : calls, ioErrors);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableDirectoryMoveUsesOneNativePairAndRename(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePaths = 0x0000_6000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableDirectoryMoveEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var allocations = 0;
		var frees = 0;
		var moves = new List<(string Source, string Destination)>();
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePaths;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.Equal(nativePaths, state.A[1]);
			frees++;
		});
		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal("dos.library", ReadCString(bus, state.A[1]));
			Assert.Equal(0u, state.D[0]);
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 78, state =>
		{
			moves.Add((ReadCString(bus, state.D[1]), ReadCString(bus, state.D[2])));
			state.D[0] = 1;
		});

		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		if (target == M68kCpuTarget.M68000)
		{
			Assert.True(result.Image.Length <= 7_400,
				$"Directory.Move image grew to {result.Image.Length} bytes.");
			Assert.True(result.Code.Length <= 5_500,
				$"Directory.Move code grew to {result.Code.Length} bytes.");
			Assert.True(cycles <= 180_000,
				$"Directory.Move grew to {cycles} MC68000 cycles.");
			Assert.Equal(15, result.AllocationStatistics.Count);
			Assert.All(
				result.AllocationStatistics,
				statistics => Assert.Equal(0, statistics.SpillFrameBytes));
		}
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.Equal(2, allocations);
		Assert.Equal(2, frees);
		Assert.Equal(
			[
				(
					".coppersharp-portable-directory-source",
					".coppersharp-portable-directory-destination"),
				(
					".coppersharp-portable-file-source",
					".coppersharp-portable-file-destination")
			],
			moves);
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
	}

	[Theory]
	[InlineData("PortableDirectoryMoveInvalidPathEntry")]
	[InlineData("PortableDirectoryMoveSamePathEntry")]
	public void PortableDirectoryMoveRejectsInvalidOrIdenticalPathsBeforeNativeCalls(
		string fixture)
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{fixture}");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var opens = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = 0;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, allocations);
		Assert.Equal(0, opens);
	}

	[Fact]
	public void PortableDirectoryMoveAllocationFailureIsCatchable()
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableDirectoryMoveOutOfMemoryEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var opens = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = 0;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, allocations);
		Assert.Equal(0, opens);
	}

	[Theory]
	[InlineData("open-failure", "PortableDirectoryMoveIOExceptionEntry", 1)]
	[InlineData("missing", "PortableDirectoryMoveDirectoryNotFoundEntry", 2)]
	[InlineData("destination-exists", "PortableDirectoryMoveIOExceptionEntry", 1)]
	[InlineData("cross-volume", "PortableDirectoryMoveIOExceptionEntry", 1)]
	[InlineData("protected", "PortableDirectoryMoveUnauthorizedEntry", 2)]
	[InlineData("native-no-free-store", "PortableDirectoryMoveOutOfMemoryEntry", 1)]
	public void PortableDirectoryMoveFailuresReleaseEveryOwnedResource(
		string scenario,
		string fixture,
		int calls)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePaths = 0x0000_6000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{fixture}");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var frees = 0;
		var opens = 0;
		var closes = 0;
		var renames = 0;
		var ioErrors = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePaths;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = scenario == "open-failure" ? 0u : dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 78, state =>
		{
			Assert.NotEmpty(ReadCString(bus, state.D[1]));
			Assert.NotEmpty(ReadCString(bus, state.D[2]));
			renames++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(dosBase - 132, state =>
		{
			ioErrors++;
			state.D[0] = (uint)(scenario switch
			{
				"missing" => global::Amiga.DOS.Error.ObjectNotFound,
				"destination-exists" => global::Amiga.DOS.Error.ObjectExists,
				"cross-volume" => global::Amiga.DOS.Error.RenameAcrossDevices,
				"protected" => global::Amiga.DOS.Error.WriteProtected,
				"native-no-free-store" => global::Amiga.DOS.Error.NoFreeStore,
				_ => global::Amiga.DOS.Error.None
			});
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(calls, allocations);
		Assert.Equal(calls, frees);
		Assert.Equal(1, opens);
		Assert.Equal(scenario == "open-failure" ? 0 : 1, closes);
		Assert.Equal(scenario == "open-failure" ? 0 : calls, renames);
		Assert.Equal(scenario == "open-failure" ? 0 : calls, ioErrors);
	}

	[Fact]
	public void PortableFileSystemDeletionPathAllocationFailureIsCatchable()
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteAllocationFailureEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var opens = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = 0;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, allocations);
		Assert.Equal(0, opens);
	}

	[Theory]
	[InlineData("open-failure")]
	[InlineData("examine-failure")]
	[InlineData("wrong-kind")]
	[InlineData("delete-failure")]
	public void PortableDirectoryDeleteIoFailuresReleaseEveryOwnedResource(string scenario)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		const uint lockHandle = 0x0000_0100;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteIOExceptionEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var allocations = 0;
		var frees = 0;
		var closes = 0;
		var locks = 0;
		var examines = 0;
		var unlocks = 0;
		var deletes = 0;
		var ioErrors = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
			state.D[0] = scenario == "open-failure" ? 0u : dosBase);
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state =>
		{
			locks++;
			state.D[0] = lockHandle;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			examines++;
			if (scenario == "examine-failure")
			{
				state.D[0] = 0;
				return;
			}
			var offset = checked((int)state.D[2] + global::Amiga.FileInfoBlock.DirEntryTypeOffset);
			var entryType = scenario == "wrong-kind" ? unchecked((uint)-3) : 2u;
			bus.Memory[offset] = (byte)(entryType >> 24);
			bus.Memory[offset + 1] = (byte)(entryType >> 16);
			bus.Memory[offset + 2] = (byte)(entryType >> 8);
			bus.Memory[offset + 3] = (byte)entryType;
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, _ => unlocks++);
		bus.RegisterGateway(dosBase - 72, state =>
		{
			deletes++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(dosBase - 132, state =>
		{
			ioErrors++;
			state.D[0] = (uint)(scenario == "examine-failure"
				? global::Amiga.DOS.Error.SeekError
				: global::Amiga.DOS.Error.DirectoryNotEmpty);
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, allocations);
		Assert.Equal(1, frees);
		Assert.Equal(scenario == "open-failure" ? 0 : 1, closes);
		Assert.Equal(scenario == "open-failure" ? 0 : 1, locks);
		Assert.Equal(scenario is "examine-failure" or "wrong-kind" or "delete-failure" ? 1 : 0, examines);
		Assert.Equal(scenario is "examine-failure" or "wrong-kind" or "delete-failure" ? 1 : 0, unlocks);
		Assert.Equal(scenario == "delete-failure" ? 1 : 0, deletes);
		Assert.Equal(scenario is "examine-failure" or "delete-failure" ? 1 : 0, ioErrors);
	}

	[Fact]
	public void PortableFileDeleteProtectionMapsToUnauthorizedAccessAfterCleanup()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		const uint lockHandle = 0x0000_0100;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteProtectedEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var frees = 0;
		var closes = 0;
		var unlocks = 0;
		bus.RegisterGateway(execBase - 198, state => state.D[0] = nativePath);
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state => state.D[0] = lockHandle);
		bus.RegisterGateway(dosBase - 102, state =>
		{
			var offset = checked((int)state.D[2] + global::Amiga.FileInfoBlock.DirEntryTypeOffset);
			bus.Memory[offset] = 0xff;
			bus.Memory[offset + 1] = 0xff;
			bus.Memory[offset + 2] = 0xff;
			bus.Memory[offset + 3] = 0xfd;
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, _ => unlocks++);
		bus.RegisterGateway(dosBase - 72, state => state.D[0] = 0);
		bus.RegisterGateway(dosBase - 132, state =>
			state.D[0] = (uint)global::Amiga.DOS.Error.DeleteProtected);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, frees);
		Assert.Equal(1, unlocks);
		Assert.Equal(1, closes);
	}

	[Fact]
	public void FreestandingFileSystemProbeUsesScopedDosOwnership()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemMissingEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Freestanding
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var frees = 0;
		bus.RegisterGateway(execBase - 198, state => state.D[0] = nativePath);
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state => state.D[0] = 0);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.Equal(1, frees);
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Initialize", StringComparison.Ordinal));
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Shutdown", StringComparison.Ordinal));
	}

	[Fact]
	public void FreestandingFileSystemDeletionUsesScopedDosOwnership()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Freestanding
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var frees = 0;
		var unlocks = 0;
		var nextHandle = 0x100u;
		var entryTypes = new Dictionary<uint, int>();
		bus.RegisterGateway(execBase - 198, state => state.D[0] = nativePath);
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state =>
		{
			var path = ReadCString(bus, state.D[1]);
			if (path == ".coppersharp-portable-missing")
			{
				state.D[0] = 0;
				return;
			}
			var handle = nextHandle++;
			entryTypes[handle] = path == ".coppersharp-portable-directory" ? 2 : -3;
			state.D[0] = handle;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			Assert.True(entryTypes.TryGetValue(state.D[1], out var entryType));
			var offset = checked((int)state.D[2] + global::Amiga.FileInfoBlock.DirEntryTypeOffset);
			var rawEntryType = unchecked((uint)entryType);
			bus.Memory[offset] = (byte)(rawEntryType >> 24);
			bus.Memory[offset + 1] = (byte)(rawEntryType >> 16);
			bus.Memory[offset + 2] = (byte)(rawEntryType >> 8);
			bus.Memory[offset + 3] = (byte)rawEntryType;
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, state =>
		{
			Assert.True(entryTypes.Remove(state.D[1]));
			unlocks++;
		});
		bus.RegisterGateway(dosBase - 72, state => state.D[0] = 1);
		bus.RegisterGateway(dosBase - 132, state =>
			state.D[0] = (uint)global::Amiga.DOS.Error.ObjectNotFound);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(3, opens);
		Assert.Equal(3, closes);
		Assert.Equal(3, frees);
		Assert.Equal(2, unlocks);
		Assert.Empty(entryTypes);
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Initialize", StringComparison.Ordinal));
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Shutdown", StringComparison.Ordinal));
	}

	[Fact]
	public void FreestandingDirectoryMoveUsesScopedDosOwnership()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePaths = 0x0000_6000;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PortableDirectoryMoveEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Freestanding
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var allocations = 0;
		var frees = 0;
		var renames = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePaths;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 78, state =>
		{
			Assert.False(string.IsNullOrEmpty(ReadCString(bus, state.D[1])));
			Assert.False(string.IsNullOrEmpty(ReadCString(bus, state.D[2])));
			renames++;
			state.D[0] = 1;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(2, opens);
		Assert.Equal(2, closes);
		Assert.Equal(2, allocations);
		Assert.Equal(2, frees);
		Assert.Equal(2, renames);
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Initialize", StringComparison.Ordinal));
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Shutdown", StringComparison.Ordinal));
	}

	[Fact]
	public void FreestandingFileGetAttributesUsesScopedDosOwnership()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		const uint lockHandle = 0x0000_0100;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileGetAttributesEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Freestanding
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var allocations = 0;
		var frees = 0;
		var locks = 0;
		var examines = 0;
		var unlocks = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state =>
		{
			locks++;
			state.D[0] = lockHandle;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			var entryType = examines == 0 ? unchecked((uint)-3) : 2u;
			var protection = examines == 0
				? (uint)(global::Amiga.FileProtection.Write | global::Amiga.FileProtection.Archive)
				: 0u;
			bus.WriteLong(
				state.D[2] + global::Amiga.FileInfoBlock.DirEntryTypeOffset,
				entryType);
			bus.WriteLong(
				state.D[2] + global::Amiga.FileInfoBlock.ProtectionOffset,
				protection);
			examines++;
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, _ => unlocks++);

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(2, opens);
		Assert.Equal(2, closes);
		Assert.Equal(2, allocations);
		Assert.Equal(2, frees);
		Assert.Equal(2, locks);
		Assert.Equal(2, examines);
		Assert.Equal(2, unlocks);
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Initialize", StringComparison.Ordinal));
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Shutdown", StringComparison.Ordinal));
	}

	[Fact]
	public void FreestandingFileSetAttributesUsesScopedDosOwnership()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativePath = 0x0000_6000;
		const uint lockHandle = 0x0000_0100;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSetAttributesEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Freestanding
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		var allocations = 0;
		var frees = 0;
		var locks = 0;
		var examines = 0;
		var unlocks = 0;
		var sets = 0;
		var currentProtection = 0u;
		bus.RegisterGateway(execBase - 198, state =>
		{
			allocations++;
			state.D[0] = nativePath;
		});
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 84, state =>
		{
			locks++;
			state.D[0] = lockHandle;
		});
		bus.RegisterGateway(dosBase - 102, state =>
		{
			bus.WriteLong(
				state.D[2] + global::Amiga.FileInfoBlock.ProtectionOffset,
				currentProtection);
			examines++;
			state.D[0] = 1;
		});
		bus.RegisterGateway(dosBase - 90, _ => unlocks++);
		bus.RegisterGateway(dosBase - 186, state =>
		{
			currentProtection = state.D[2];
			sets++;
			state.D[0] = 1;
		});

		Assert.Equal(
			42u,
			Execute(bus, M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		Assert.Equal(2, opens);
		Assert.Equal(2, closes);
		Assert.Equal(2, allocations);
		Assert.Equal(2, frees);
		Assert.Equal(2, locks);
		Assert.Equal(2, examines);
		Assert.Equal(2, unlocks);
		Assert.Equal(2, sets);
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Initialize", StringComparison.Ordinal));
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Shutdown", StringComparison.Ordinal));
	}

	[Fact]
	public void PortableFileSystemPalIsLinkedOnlyWhenReachable()
	{
		var fileSystem = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemMissingEntry");
		var deletion = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSystemDeleteEntry");
		var move = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableDirectoryMoveEntry");
		var attributes = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileGetAttributesEntry");
		var setAttributes = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableFileSetAttributesEntry");
		var console = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleWriteEntry");
		var unrelated = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DefaultEntry");

		Assert.Contains(
			fileSystem.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Initialize", StringComparison.Ordinal));
		Assert.Contains(
			fileSystem.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Shutdown", StringComparison.Ordinal));
		Assert.DoesNotContain(
			fileSystem.Symbols,
			symbol => symbol.Name.Contains("ConsolePal", StringComparison.Ordinal));
		Assert.DoesNotContain(
			fileSystem.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::DeleteFile", StringComparison.Ordinal));
		Assert.DoesNotContain(
			fileSystem.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::DeleteDirectory", StringComparison.Ordinal));
		Assert.DoesNotContain(
			fileSystem.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::MoveDirectory", StringComparison.Ordinal));
		Assert.DoesNotContain(
			fileSystem.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::GetFileAttributes", StringComparison.Ordinal));
		Assert.DoesNotContain(
			fileSystem.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::SetFileAttributes", StringComparison.Ordinal));
		Assert.Contains(
			deletion.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::DeleteFile", StringComparison.Ordinal));
		Assert.Contains(
			deletion.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::DeleteDirectory", StringComparison.Ordinal));
		Assert.DoesNotContain(
			deletion.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Exists", StringComparison.Ordinal));
		Assert.DoesNotContain(
			deletion.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::MoveDirectory", StringComparison.Ordinal));
		Assert.DoesNotContain(
			deletion.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::GetFileAttributes", StringComparison.Ordinal));
		Assert.DoesNotContain(
			deletion.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::SetFileAttributes", StringComparison.Ordinal));
		Assert.Contains(
			move.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::MoveDirectory", StringComparison.Ordinal));
		Assert.DoesNotContain(
			move.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Exists", StringComparison.Ordinal));
		Assert.DoesNotContain(
			move.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::DeleteFile", StringComparison.Ordinal));
		Assert.DoesNotContain(
			move.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::DeleteDirectory", StringComparison.Ordinal));
		Assert.DoesNotContain(
			move.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::GetFileAttributes", StringComparison.Ordinal));
		Assert.DoesNotContain(
			move.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::SetFileAttributes", StringComparison.Ordinal));
		Assert.Contains(
			attributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::GetFileAttributes", StringComparison.Ordinal));
		Assert.DoesNotContain(
			attributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Exists", StringComparison.Ordinal));
		Assert.DoesNotContain(
			attributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::DeleteFile", StringComparison.Ordinal));
		Assert.DoesNotContain(
			attributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::DeleteDirectory", StringComparison.Ordinal));
		Assert.DoesNotContain(
			attributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::MoveDirectory", StringComparison.Ordinal));
		Assert.DoesNotContain(
			attributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::SetFileAttributes", StringComparison.Ordinal));
		Assert.Contains(
			setAttributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::SetFileAttributes", StringComparison.Ordinal));
		Assert.DoesNotContain(
			setAttributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::Exists", StringComparison.Ordinal));
		Assert.DoesNotContain(
			setAttributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::DeleteFile", StringComparison.Ordinal));
		Assert.DoesNotContain(
			setAttributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::DeleteDirectory", StringComparison.Ordinal));
		Assert.DoesNotContain(
			setAttributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::MoveDirectory", StringComparison.Ordinal));
		Assert.DoesNotContain(
			setAttributes.Symbols,
			symbol => symbol.Name.EndsWith("FileSystemPal::GetFileAttributes", StringComparison.Ordinal));
		Assert.DoesNotContain(
			console.Symbols,
			symbol => symbol.Name.Contains("FileSystemPal", StringComparison.Ordinal));
		Assert.DoesNotContain(
			unrelated.Symbols,
			symbol => symbol.Name.Contains("FileSystemPal", StringComparison.Ordinal));
	}

	[Fact]
	public void PortableConsolePalIsLinkedOnlyWhenReachable()
	{
		var console = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleWriteEntry");
		var unrelated = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DefaultEntry");

		Assert.Contains(
			console.Symbols,
			symbol => symbol.Name.Contains("ConsolePal", StringComparison.Ordinal));
		Assert.Contains(
			console.Symbols,
			symbol => symbol.Name.EndsWith("ConsolePal::Initialize", StringComparison.Ordinal));
		Assert.Contains(
			console.Symbols,
			symbol => symbol.Name.EndsWith("ConsolePal::Shutdown", StringComparison.Ordinal));
		Assert.DoesNotContain(
			console.Symbols,
			symbol => symbol.Name.EndsWith("ConsolePal::InitializeInput", StringComparison.Ordinal));
		Assert.DoesNotContain(
			console.Symbols,
			symbol => symbol.Name.EndsWith("ConsolePal::ShutdownInput", StringComparison.Ordinal));
		Assert.DoesNotContain(
			unrelated.Symbols,
			symbol => symbol.Name.Contains("ConsolePal", StringComparison.Ordinal));
	}

	[Fact]
	public void PortableConsoleIntegerFormatterIsLinkedOnlyForPrimitiveOverloads()
	{
		var primitive = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsolePrimitiveEntry");
		var stringOnly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleWriteEntry");

		Assert.Contains("ShadowIntegerFormatter", primitive.Map);
		Assert.Contains("ShadowIntegerFormatter::PackInt32", primitive.Map);
		Assert.Contains("ShadowIntegerFormatter::PackUInt32", primitive.Map);
		Assert.DoesNotContain("ShadowInt32::ToString", primitive.Map);
		Assert.DoesNotContain("ShadowUInt32::ToString", primitive.Map);
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, primitive.Map);
		Assert.DoesNotContain("ShadowIntegerFormatter", stringOnly.Map);
	}

	[Fact]
	public void PortableConsoleBooleanAndCharacterPathsRemainIndependentlyLinkable()
	{
		var boolean = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleBooleanEntry");
		var character = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleCharacterEntry");
		var stringOnly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleWriteEntry");

		Assert.Contains("ConsolePal::WriteBoolean", boolean.Map);
		Assert.Contains("ConsolePal::WriteStaticBytes", boolean.Map);
		Assert.DoesNotContain("ConsolePal::WritePackedBytes", boolean.Map);
		Assert.DoesNotContain("ShadowIntegerFormatter", boolean.Map);
		Assert.DoesNotContain("ConsolePal::WriteManagedString", boolean.Map);
		Assert.Contains("ConsolePal::WriteCharacter", character.Map);
		Assert.Contains("ConsolePal::WritePackedBytes", character.Map);
		Assert.DoesNotContain("ConsolePal::WriteBoolean", character.Map);
		Assert.DoesNotContain("ShadowIntegerFormatter", character.Map);
		Assert.DoesNotContain("ConsolePal::WriteManagedString", character.Map);
		Assert.DoesNotContain("ConsolePal::WriteBoolean", stringOnly.Map);
		Assert.DoesNotContain("ConsolePal::WriteCharacter", stringOnly.Map);
	}

	[Fact]
	public void PortableConsoleFreestandingProfileRetainsScopedOwnership()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleWriteEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Freestanding
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(execBase - 198, state => state.D[0] = nativeBuffer);
		bus.RegisterGateway(execBase - 210, _ => { });
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = 0x100);
		bus.RegisterGateway(dosBase - 48, state => state.D[0] = state.D[3]);

		Assert.Equal(
			42u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint));
		Assert.Equal(3, opens);
		Assert.Equal(3, closes);
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("ConsolePal::Initialize", StringComparison.Ordinal));
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.EndsWith("ConsolePal::Shutdown", StringComparison.Ordinal));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PortableConsoleLifecyclePreservesAmigaStartupArguments(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleStartupArgsEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var opens = 0;
		var closes = 0;
		bus.RegisterGateway(execBase - 552, state =>
		{
			opens++;
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, _ => closes++);
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = 0x100);
		bus.RegisterGateway(dosBase - 48, state => state.D[0] = state.D[3]);

		Assert.Equal(
			42u,
			Execute(
				bus,
				model,
				HunkLoadAddress + result.EntryPoint,
				initialize: state =>
				{
					InitializeClassicCalleeSavedRegisters(state);
					state.D[0] = 17;
					state.A[0] = 0x0000_1800;
				},
				afterReturn: state =>
				{
					Assert.Equal(0xD000_0002u, state.D[2]);
					Assert.Equal(0x00A0_0002u, state.A[2]);
				}));
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
	}

	[Fact]
	public void PortableConsoleUnhandledFailureShutsDownBeforeRequester()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint intuitionBase = 0x0000_5800;
		const uint nativeBuffer = 0x0000_6000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleUnhandledFailureEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var events = new List<string>();
		var frees = 0;
		bus.RegisterGateway(execBase - 552, state =>
		{
			var name = ReadCString(bus, state.A[1]);
			events.Add($"open:{name}");
			state.D[0] = name == "dos.library" ? dosBase : intuitionBase;
		});
		bus.RegisterGateway(execBase - 414, state =>
			events.Add(state.A[1] == dosBase ? "close:dos" : "close:intuition"));
		bus.RegisterGateway(execBase - 198, state => state.D[0] = nativeBuffer);
		bus.RegisterGateway(execBase - 210, _ => frees++);
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = 0x100);
		bus.RegisterGateway(dosBase - 48, state => state.D[0] = state.D[3] - 1);
		bus.RegisterGateway(intuitionBase - 348, state => state.D[0] = 1);

		Assert.Equal(
			20u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint));
		Assert.Equal(0, frees);
		Assert.Equal(
			new[]
			{
				"open:dos.library",
				"close:dos",
				"open:intuition.library",
				"close:intuition"
			},
			events);
	}

	[Fact]
	public void PortableConsoleStringMetricsStayWithinMc68000Budgets()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleWriteEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var nativeAllocations = 0;
		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => { });
		bus.RegisterGateway(execBase - 198, state =>
		{
			nativeAllocations++;
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, _ => { });
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = 0x100);
		bus.RegisterGateway(dosBase - 48, state => state.D[0] = state.D[3]);
		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		var maximumSpillBytes = result.AllocationStatistics.Count == 0
			? 0
			: result.AllocationStatistics.Max(static item => item.SpillFrameBytes);

		Assert.True(
			result.Image.Length <= 4_800,
			$"Portable console image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 3_500,
			$"Portable console code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 13_000,
			$"Portable console grew to {cycles} MC68000 compiler/emulator cycles.");
		Assert.Equal(0, nativeAllocations);
		Assert.Equal(8, result.AllocationStatistics.Count);
		Assert.Equal(4, maximumSpillBytes);
	}

	[Fact]
	public void PortableConsolePrimitiveMetricsStayWithinMc68000Budgets()
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsolePrimitiveEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var nativeAllocations = 0;
		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => { });
		bus.RegisterGateway(execBase - 198, state =>
		{
			nativeAllocations++;
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, _ => { });
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = 0x100);
		bus.RegisterGateway(dosBase - 48, state => state.D[0] = state.D[3]);
		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		var maximumSpillBytes = result.AllocationStatistics.Count == 0
			? 0
			: result.AllocationStatistics.Max(static item => item.SpillFrameBytes);

		Assert.True(
			result.Image.Length <= 8_100,
			$"Primitive console image grew to {result.Image.Length} bytes.");
		Assert.True(
			result.Code.Length <= 6_000,
			$"Primitive console code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 55_000,
			$"Primitive console grew to {cycles} MC68000 compiler/emulator cycles.");
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
		Assert.Equal(0, nativeAllocations);
		Assert.Equal(17, result.AllocationStatistics.Count);
		Assert.Equal(4, maximumSpillBytes);
	}

	[Fact]
	public void PortableConsoleBooleanMetricsStayWithinMc68000Budgets()
	{
		var measurement = MeasurePortableConsolePrimitive(
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleBooleanEntry");
		Assert.True(
			measurement.Result.Image.Length <= 3_200,
			$"Boolean console image grew to {measurement.Result.Image.Length} bytes.");
		Assert.True(
			measurement.Result.Code.Length <= 2_100,
			$"Boolean console code grew to {measurement.Result.Code.Length} bytes.");
		Assert.True(
			measurement.Cycles <= 5_000,
			$"Boolean console grew to {measurement.Cycles} MC68000 cycles.");
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, measurement.Result.Map);
		Assert.Equal(0, measurement.NativeAllocations);
		Assert.Equal(9, measurement.Result.AllocationStatistics.Count);
		Assert.Equal(0, measurement.MaximumSpillBytes);
	}

	[Fact]
	public void PortableConsoleCharacterMetricsStayWithinMc68000Budgets()
	{
		var measurement = MeasurePortableConsolePrimitive(
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleCharacterEntry");
		Assert.True(
			measurement.Result.Image.Length <= 3_800,
			$"Character console image grew to {measurement.Result.Image.Length} bytes.");
		Assert.True(
			measurement.Result.Code.Length <= 2_550,
			$"Character console code grew to {measurement.Result.Code.Length} bytes.");
		Assert.True(
			measurement.Cycles <= 11_000,
			$"Character console grew to {measurement.Cycles} MC68000 cycles.");
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, measurement.Result.Map);
		Assert.Equal(0, measurement.NativeAllocations);
		Assert.Equal(10, measurement.Result.AllocationStatistics.Count);
		Assert.Equal(0, measurement.MaximumSpillBytes);
	}

	[Fact]
	public void PortableConsoleInt64MetricsStayWithinMc68000Budgets()
	{
		var measurement = MeasurePortableConsolePrimitive(
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableConsoleInt64Entry");
		Assert.True(
			measurement.Result.Image.Length <= 11_000,
			$"Int64 console image grew to {measurement.Result.Image.Length} bytes.");
		Assert.True(
			measurement.Result.Code.Length <= 8_500,
			$"Int64 console code grew to {measurement.Result.Code.Length} bytes.");
		Assert.True(
			measurement.Cycles <= 190_000,
			$"Int64 console grew to {measurement.Cycles} MC68000 cycles.");
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, measurement.Result.Map);
		Assert.Equal(0, measurement.NativeAllocations);
		Assert.Equal(21, measurement.Result.AllocationStatistics.Count);
		Assert.Equal(8, measurement.MaximumSpillBytes);
	}

	private static (
		M68kCompilationResult Result,
		long Cycles,
		int NativeAllocations,
		int MaximumSpillBytes) MeasurePortableConsolePrimitive(string entryPoint)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint nativeBuffer = 0x0000_6000;
		var result = Compile(M68kCpuTarget.M68000, M68kOutputFormat.Hunk, entryPoint);
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var nativeAllocations = 0;
		bus.RegisterGateway(execBase - 552, state => state.D[0] = dosBase);
		bus.RegisterGateway(execBase - 414, _ => { });
		bus.RegisterGateway(execBase - 198, state =>
		{
			nativeAllocations++;
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, _ => { });
		bus.RegisterGateway(dosBase - 60, state => state.D[0] = 0x100);
		bus.RegisterGateway(dosBase - 48, state => state.D[0] = state.D[3]);
		long cycles = 0;
		Assert.Equal(
			42u,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		var maximumSpillBytes = result.AllocationStatistics.Count == 0
			? 0
			: result.AllocationStatistics.Max(static item => item.SpillFrameBytes);
		return (result, cycles, nativeAllocations, maximumSpillBytes);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DynamicCStringRejectsEmbeddedNullBeforeNativeAllocation(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CStringRejectsEmbeddedNullEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DynamicCStringRejectsUnmappableLatin1Input(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CStringRejectsUnmappableEntry");

		Assert.Equal(42u, ExecuteHunk(result, model));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DynamicCStringReportsNativeAllocationFailure(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		const uint execBase = 0x0000_3000;
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CStringAllocationFailureEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		bus.RegisterGateway(execBase - 198, state => state.D[0] = 0);

		Assert.Equal(
			42u,
			Execute(bus, model, HunkLoadAddress + result.EntryPoint));
	}

	[Fact]
	public void CStringLiteralPathDoesNotLinkDynamicOwnershipHelpers()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CStringLiteralEntry");

		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.Contains("CStringEncoding", StringComparison.Ordinal));
		Assert.DoesNotContain("\tjsr\t-198(a6)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tjsr\t-210(a6)", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ScopedCStringMetricsStayWithinInitialMc68000Budgets()
	{
		const uint execBase = 0x0000_3000;
		const uint nativeBuffer = 0x0000_6000;
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::CStringBufferEntry");
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var nativeAllocations = 0;
		bus.RegisterGateway(execBase - 198, state =>
		{
			nativeAllocations++;
			state.D[0] = nativeBuffer;
		});
		bus.RegisterGateway(execBase - 210, _ => { });
		long cycles = 0;
		Assert.Equal(
			nativeBuffer,
			Execute(
				bus,
				M68kCpuModel.M68000,
				HunkLoadAddress + result.EntryPoint,
				afterReturn: state => cycles = state.Cycles));
		Assert.True(
			result.Image.Length <= 4128,
			$"CStringBufferEntry: image={result.Image.Length}, code={result.Code.Length}, cycles={cycles}.");
		Assert.True(
			result.Code.Length <= 3036,
			$"CStringBufferEntry code grew to {result.Code.Length} bytes.");
		Assert.True(
			cycles <= 8750,
			$"CStringBufferEntry grew to {cycles} MC68000 cycles.");
		var analysis = AmigaM68kCompiler.AnalyzeFramework(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CStringBufferEntry",
			Cpu = M68kCpuTarget.M68000
		});
		Assert.Empty(analysis.ManagedAllocationSites);
		Assert.All(
			result.AllocationStatistics,
			statistics => Assert.Equal(0, statistics.SpillFrameBytes));
		Assert.Equal(1, nativeAllocations);
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

	[Fact]
	public void ArrayAlgorithmFixturesMatchHostNet10()
	{
		Assert.Multiple(
			() => Assert.Equal(42, CompilerFixtures.ArrayAlgorithmsEntry()),
			() => Assert.Equal(42, CompilerFixtures.ArrayFloatingEqualityEntry()),
			() => Assert.Equal(42, CompilerFixtures.ArrayAlgorithmsExceptionEntry()));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void GenericArrayAlgorithmsPreserveNetSemanticsAcrossForcedCollection(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var entry in new[]
		{
			"ArrayAlgorithmsEntry",
			"ArrayAlgorithmsExceptionEntry"
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
					Size = 0x0000_6000
				}
			});

			Assert.Equal(42u, ExecuteHunk(result, model));
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ArrayFloatingSearchUsesDefaultEqualityWithoutFpuHelpers(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::ArrayFloatingEmptySearchEntry",
			Cpu = target,
			FloatingPoint = M68kFloatingPointMode.SoftFloat,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_6000
			}
		});

		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name.Contains("softfloat", StringComparison.OrdinalIgnoreCase));
		Assert.Equal(42u, ExecuteHunk(result, model));
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

		Assert.Matches(@"\tadd\.b\td[0-7],d[0-7]", byteResult.Text);
		Assert.Matches(@"\tadd\.w\td[0-7],d[0-7]", shortResult.Text);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DefersNarrowNormalizationAcrossCopyAndPhiChains(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var (entryPoint, expected) in new[]
		{
			("UnsignedWordNormalizationChainEntry", 1u),
			("UnsignedByteNormalizationChainEntry", 1u),
			("SignedByteNormalizationChainEntry", 127u),
			("SignedWordNormalizationChainEntry", 32767u)
		})
		{
			var hunk = Compile(
				target,
				M68kOutputFormat.Hunk,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entryPoint}");
			Assert.Equal(expected, ExecuteHunk(hunk, model));
		}

		var assembly = Compile(
			target,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::UnsignedWordNormalizationChainEntry");
		Assert.DoesNotContain(
			"andi.l\t#$0000FFFF",
			assembly.Text,
			StringComparison.Ordinal);
		Assert.Matches(@"\tmove\.l\td0,d[2-7]", assembly.Text);
		Assert.Matches(@"\tadd(?:q)?\.w\t(?:#[1-8]|d[0-7]),d[0-7]", assembly.Text);

		assembly = Compile(
			target,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::UnsignedByteNormalizationChainEntry");
		Assert.DoesNotContain(
			"andi.l\t#$000000FF",
			assembly.Text,
			StringComparison.Ordinal);
		Assert.Matches(
			@"\tmoveq\t#0,d([0-7])\r?\n\tmove\.b\td[0-7],d\1",
			assembly.Text);

		assembly = Compile(
			target,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SignedByteNormalizationChainEntry");
		if (target == M68kCpuTarget.M68000)
		{
			Assert.Matches(
				@"\tmove\.l\td[0-7],d([0-7])\r?\n\text\.w\td\1\r?\n\text\.l\td\1",
				assembly.Text);
			Assert.True(
				System.Text.RegularExpressions.Regex.Matches(
					assembly.Text!,
					@"\text\.w\td[0-7]").Count == 1);
		}
		else
		{
			Assert.Matches(
				@"\tmove\.l\td[0-7],d([0-7])\r?\n\textb\.l\td\1",
				assembly.Text);
			Assert.True(
				System.Text.RegularExpressions.Regex.Matches(
					assembly.Text!,
					@"\textb\.l\td[0-7]").Count == 1);
		}

		assembly = Compile(
			target,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SignedWordNormalizationChainEntry");
		Assert.Matches(
			@"\tmove\.l\td[0-7],d([0-7])\r?\n\text\.l\td\1",
			assembly.Text);
		Assert.True(
			System.Text.RegularExpressions.Regex.Matches(
				assembly.Text!,
				@"\text\.l\td[0-7]").Count == 1);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PreservesNarrowNormalizationAcrossMemoryAndAbiBoundaries(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		foreach (var (entryPoint, expected) in new[]
		{
			("NarrowArrayNormalizationEntry", 32896u),
			("NarrowFrameAndSpillNormalizationEntry", 36u),
			("NarrowCallBoundaryEntry", 65534u),
			("NarrowStackArgumentBoundaryEntry", 65534u),
			("NarrowReturnBoundaryEntry", 65534u),
			("NarrowCheckedConversionBoundaryEntry", 65534u),
			("NarrowLogicalNormalizationEntry", 41719u),
			("NarrowCompareNormalizationEntry", 3u)
		})
		{
			var result = CompileWithAllocator(
				target,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entryPoint}");
			Assert.Equal(expected, ExecuteHunkWithAllocator(result, model));
		}
	}

	[Fact]
	public void EmitsNarrowNormalizationOnlyAtMemoryAndAbiBoundaries()
	{
		string EntryBody(string entryPoint)
		{
			var result = Compile(
				M68kCpuTarget.M68000,
				M68kOutputFormat.Assembly,
				$"CopperSharp.Compiler.Tests.CompilerFixtures::{entryPoint}");
			var text = result.Text!;
			var end = text.IndexOf("_003Aend:", StringComparison.Ordinal);
			Assert.True(end > 0);
			return text[..end];
		}

		static int Count(string text, string pattern) =>
			System.Text.RegularExpressions.Regex.Matches(text, pattern).Count;

		var arrays = EntryBody("NarrowArrayNormalizationEntry");
		Assert.Matches(
			@"\tmove\.b\t[^\r\n]+,d([0-7])\r?\n\tadd(?:q)?\.b\t(?:#[1-8]|d[0-7]),d\1",
			arrays);
		Assert.Matches(
			@"\tmove\.w\t[^\r\n]+,d([0-7])\r?\n(?:\tmove\.w\td[0-7],d\1\r?\n)?\tadd(?:q)?\.w\t(?:#[1-8]|d[0-7]),d\1",
			arrays);
		Assert.True(Count(arrays, @"\tandi\.l\t#\$000000FF,d[0-7]") == 1);
		Assert.True(Count(arrays, @"\tandi\.l\t#\$0000FFFF,d[0-7]") == 1);
		Assert.True(Count(arrays, @"\text\.w\td[0-7]") == 1);
		Assert.True(Count(arrays, @"\text\.l\td[0-7]") == 2);

		var frame = EntryBody("NarrowFrameAndSpillNormalizationEntry");
		Assert.Matches(@"\tmove\.w\t\(a7\),d[0-7]\r?\n\tadd\.w", frame);
		Assert.DoesNotContain("andi.l\t#$0000FFFF", frame, StringComparison.Ordinal);
		Assert.Matches(
			@"\tmoveq\t#0,d([0-7])\r?\n\tmove\.w\td[0-7],d\1",
			frame);

		var call = EntryBody("NarrowCallBoundaryEntry");
		Assert.Matches(
			@"\tandi\.l\t#\$0000FFFF,d0\r?\n\t(?:bra|bsr)",
			call);
		Assert.True(Count(call, @"\tandi\.l\t#\$0000FFFF,d[0-7]") == 1);

		var stackArgument = EntryBody("NarrowStackArgumentBoundaryEntry");
		Assert.Matches(
			@"\tmoveq\t#0,d([0-7])\r?\n\tmove\.w\td[0-7],d\1\r?\n\tmove\.l\td\1,-\(a7\)",
			stackArgument);
		Assert.True(Count(stackArgument, @"\tandi\.l\t#\$0000FFFF,d[0-7]") == 0);

		var narrowReturn = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NarrowReturnBoundaryEntry")
			.Text!;
		Assert.Matches(
			@"\tbsr\.[sw]\t[^\r\n]+\r?\n[^\r\n]*:\r?\n\tandi\.l\t#\$0000FFFF,d0\r?\n\trts",
			narrowReturn);
		Assert.True(
			Count(narrowReturn, @"\tandi\.l\t#\$0000FFFF,d[0-7]") == 1);

		var checkedConversion = EntryBody("NarrowCheckedConversionBoundaryEntry");
		Assert.Contains(
			"andi.l\t#$0000FFFF,d0\r\n\ttst.l\td0",
			checkedConversion,
			StringComparison.Ordinal);
		Assert.True(
			Count(checkedConversion, @"\tandi\.l\t#\$0000FFFF,d[0-7]") == 1);

		var logical = EntryBody("NarrowLogicalNormalizationEntry");
		Assert.Contains("eor.w", logical, StringComparison.Ordinal);
		Assert.Contains("and.w", logical, StringComparison.Ordinal);
		Assert.Contains("or.w", logical, StringComparison.Ordinal);
		Assert.Contains("not.w", logical, StringComparison.Ordinal);
		Assert.Contains("mulu.w", logical, StringComparison.Ordinal);
		Assert.True(Count(logical, @"\tandi\.l\t#\$0000FFFF,d[0-7]") == 1);

		var compare = EntryBody("NarrowCompareNormalizationEntry");
		Assert.Contains("cmp.w", compare, StringComparison.Ordinal);
		Assert.Contains("cmp.b", compare, StringComparison.Ordinal);
		Assert.True(Count(compare, @"\tandi\.l\t#\$0000(?:00FF|FFFF),d[0-7]") == 0);
		Assert.True(Count(compare, @"\text(?:b)?\.[wl]\td[0-7]") == 0);
	}

	[Fact]
	public void UsesScratchRegisterForUnsignedNarrowLongStackArgument()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NarrowStackArgumentBoundaryEntry")
			.Text!;

		Assert.Matches(
			@"\tmoveq\t#0,d([0-7])\r?\n\tmove\.w\td[0-7],d\1\r?\n\tmove\.l\td\1,-\(a7\)",
			assembly);
		Assert.DoesNotMatch(
			@"\tandi\.l\t#\$0000FFFF,d([0-7])\r?\n\tmove\.l\td\1,-\(a7\)",
			assembly);
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

		Assert.Matches(@"\tsub\.w\td[0-7],d[0-7]", result.Text);
		Assert.Matches(@"\tmulu\.w\td[0-7],d[0-7]", result.Text);
		Assert.Matches(@"\tasr\.w\t#1,d[0-7]", result.Text);
		Assert.Matches(@"\tneg\.b\td[0-7]", result.Text);
	}

	[Fact]
	public void M68000SynthesizesPositiveConstantMultiplyWithoutRuntimeLoop()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstantMultiplyEntry");

		Assert.DoesNotContain("mul_loop", assembly.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("#$01000193", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("swap\td2", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("clr.w\td2", assembly.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ExecutesM68000PositiveConstantMultiplySynthesis()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstantMultiplyEntry");

		Assert.Equal(0xCD79BD48u, ExecuteHunk(result, M68kCpuModel.M68000));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstantMultiplyMatchesModulo32BitOracleForBoundaryAndRandomInputs(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(
			target,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::ConstantMultiplyDifferentialEntry");

		static uint Mix(uint checksum, uint value) =>
			unchecked(((checksum << 1) | (checksum >> 31)) ^
				(value * 0x01000193u));
		var expected = 0u;
		foreach (var boundary in new[]
		{
			0u,
			1u,
			0x7FFF_FFFFu,
			0x8000_0000u,
			uint.MaxValue
		})
		{
			expected = Mix(expected, boundary);
		}
		var random = 0x6800_C0DEu;
		for (var index = 0; index < 32; index++)
		{
			random ^= random << 13;
			random ^= random >> 17;
			random ^= random << 5;
			expected = Mix(expected, random);
		}

		Assert.Equal(expected, ExecuteHunk(result, model));
	}

	[Fact]
	public void M68000KeepsCompactLoopForDenseConstantMultiply()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::DenseConstantMultiplyEntry");

		Assert.Contains("mul_loop", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("#$55555555", assembly.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void M68000SelectsSubtractConstantMultiplyPlanByCost()
	{
		var assembly = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SubtractConstantMultiplyEntry");

		Assert.DoesNotContain("mul_loop", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("sub.l", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("swap", assembly.Text, StringComparison.Ordinal);
		Assert.Contains("clr.w", assembly.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ExecutesM68000SubtractConstantMultiplyPlan()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::SubtractConstantMultiplyEntry");

		Assert.Equal(0xEDCBA988u, ExecuteHunk(result, M68kCpuModel.M68000));
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
	public void LowersAptrByteAndWordMemoryIntrinsics()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::AptrByteWordAccessEntry",
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Matches(@"move\.b\td[0-7],3\(a[0-6]\)", result.Text);
		Assert.Matches(@"move\.w\td[0-7],6\(a[0-6]\)", result.Text);
		Assert.Matches(@"move\.b\t3\(a[0-6]\),d[0-7]", result.Text);
		Assert.Matches(@"move\.w\t6\(a[0-6]\),d[0-7]", result.Text);
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
		foreach (var offset in new[] { 4, 116, 124, 132, 136, 140 })
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

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ManagedPoolAllocationFailureCollectsRootsAndRetries(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PoolAllocationFailureCollectsRootsEntry",
			Cpu = target,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.OnAllocationFailure,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 120
			}
		});

		Assert.Equal(42u, ExecuteHunk(result, model));
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
				Size = 108
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
				Size = 112
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
	public void HunkOutputUsesBssForProfitableZeroInitializedStatics()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			"CopperSharp.Compiler.Tests.CompilerFixtures::HunkBssEntry");
		var words = EnumerateLongWords(result.Image).ToArray();

		Assert.Equal(2u, words[2]);
		Assert.Contains(0x0000_03EBu, words);
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
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
		Assert.Matches(@"\taddq\.l\t#3,d[0-7]", result.Text);
		Assert.Matches(@"\tsubq\.l\t#2,d[0-7]", result.Text);
		Assert.DoesNotContain("\tmoveq\t#3,", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmoveq\t#2,", result.Text, StringComparison.Ordinal);
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
			"\tmovem.l\td0-d2,-(a7)",
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

	[Theory]
	[InlineData(M68kCpuTarget.M68040, M68kFloatingPointMode.M68040)]
	[InlineData(M68kCpuTarget.M68060, M68kFloatingPointMode.M68040)]
	[InlineData(M68kCpuTarget.M68020, M68kFloatingPointMode.M68882)]
	public void NativeFpuModesLowerMathSqrtAndTruncateToHardware(
		M68kCpuTarget cpu,
		M68kFloatingPointMode floatingPoint)
	{
		var squareRoot = Compile(
			cpu,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NativeMathSqrtEntry",
			floatingPoint: floatingPoint);
		var truncate = Compile(
			cpu,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NativeMathTruncateEntry",
			floatingPoint: floatingPoint);

		Assert.Contains("\tfsqrt.x\t", squareRoot.Text, StringComparison.Ordinal);
		Assert.Contains("\tfintrz.x\t", truncate.Text, StringComparison.Ordinal);
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

	private sealed class ClockGatewayProbe
	{
		public HashSet<uint> OpenRequests { get; } = [];
		public int CreatePorts { get; set; }
		public int CreateRequests { get; set; }
		public int Opens { get; set; }
		public int Reads { get; set; }
		public int Closes { get; set; }
		public int DeleteRequests { get; set; }
		public int DeletePorts { get; set; }
	}

	private static ClockGatewayProbe RegisterClockGateways(
		TestBus bus,
		string scenario = "success",
		uint frequency = 709_379,
		params ulong[] ticks)
	{
		const uint execBase = 0x0000_3000;
		const uint deviceBase = 0x0000_5000;
		var probe = new ClockGatewayProbe();
		bus.WriteLong(4, execBase);
		bus.RegisterGateway(execBase - 666, state =>
		{
			probe.CreatePorts++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(execBase - 654, state =>
		{
			probe.CreateRequests++;
			state.D[0] = 0;
		});
		bus.RegisterGateway(execBase - 444, state =>
		{
			Assert.True(
				state.A[0] != 0,
				"timer.device name pointer must be relocated into the image.");
			Assert.Equal(global::Amiga.TimerDevice.Name, ReadCString(bus, state.A[0]));
			Assert.Equal(global::Amiga.TimerDevice.UnitEClock, state.D[0]);
			Assert.Equal(0u, state.D[1]);
			AssertManualClockRequest(bus, state.A[1]);
			probe.Opens++;
			if (scenario != "open-failure")
			{
				probe.OpenRequests.Add(state.A[1]);
				bus.WriteLong(
					state.A[1] + 20,
					scenario == "null-device" ? 0u : deviceBase);
			}
			state.D[0] = scenario == "open-failure" ? 1u : 0u;
		});
		bus.RegisterGateway(deviceBase - 60, state =>
		{
			Assert.Equal(deviceBase, state.A[6]);
			var value = ticks.Length == 0
				? (ulong)probe.Reads
				: ticks[Math.Min(probe.Reads, ticks.Length - 1)];
			bus.WriteLong(state.A[0], (uint)(value >> 32));
			bus.WriteLong(state.A[0] + 4, (uint)value);
			probe.Reads++;
			state.D[0] = scenario == "frequency-zero" ? 0u : frequency;
		});
		bus.RegisterGateway(execBase - 450, state =>
		{
			Assert.Contains(state.A[1], probe.OpenRequests);
			probe.Closes++;
		});
		bus.RegisterGateway(execBase - 660, state =>
		{
			probe.DeleteRequests++;
		});
		bus.RegisterGateway(execBase - 672, state =>
		{
			probe.DeletePorts++;
		});
		return probe;
	}

	private static void AssertManualClockRequest(TestBus bus, uint request)
	{
		var port = bus.ReadLong(request + 14);
		Assert.True(
			port != 0,
			$"Manual IORequest at 0x{request:X8} has no reply port; bytes: " +
			Convert.ToHexString(bus.Memory.AsSpan(checked((int)request), 40)));
		Assert.Equal(
			(byte)global::Amiga.NodeType.ReplyMessage,
			bus.Memory[checked((int)(request + 8))]);
		Assert.Equal((ushort)40, bus.ReadWord(request + 18));
		Assert.Equal(
			(byte)global::Amiga.NodeType.MessagePort,
			bus.Memory[checked((int)(port + 8))]);
		Assert.Equal(
			(byte)global::Amiga.PortFlags.Ignore,
			bus.Memory[checked((int)(port + 14))]);
		Assert.Equal(0, bus.Memory[checked((int)(port + 15))]);
		Assert.Equal(0u, bus.ReadLong(port + 16));
		Assert.Equal(port + 24, bus.ReadLong(port + 20));
		Assert.Equal(0u, bus.ReadLong(port + 24));
		Assert.Equal(port + 20, bus.ReadLong(port + 28));
	}

	private static byte[] ExecuteConsoleIoSample(
		M68kCpuTarget target,
		M68kCpuModel model,
		byte[] input,
		uint expectedReturn,
		int expectedOutputCalls)
	{
		const uint execBase = 0x0000_3000;
		const uint dosBase = 0x0000_5000;
		const uint inputHandle = 0x0000_0123;
		const uint outputHandle = 0x0000_0456;
		const uint previousDosBase = 0x0000_7000;
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "ConsoleIO.dll"),
			EntryPoint = "ConsoleIOExample.Program::Main",
			Cpu = target,
			ExceptionMode = M68kExceptionMode.Full,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application
		});
		var bus = CreateHunkBus(result);
		bus.WriteLong(4, execBase);
		var dosBaseSlot = result.Symbols.Single(symbol =>
			symbol.Name == AmigaLibraryBaseSymbols.For("dos.library"));
		bus.WriteLong(HunkLoadAddress + dosBaseSlot.Address, previousDosBase);
		var output = new List<byte>();
		var liveNativeAllocations = new Dictionary<uint, uint>();
		var events = new List<string>();
		var nextNativeBuffer = 0x0000_6000u;
		var inputBuffer = 0u;
		var opens = 0;
		var closes = 0;
		var inputs = 0;
		var outputs = 0;
		var reads = 0;

		bus.RegisterGateway(execBase - 552, state =>
		{
			Assert.Equal("dos.library", ReadCString(bus, state.A[1]));
			Assert.Equal(0u, state.D[0]);
			opens++;
			events.Add("open-library");
			state.D[0] = dosBase;
		});
		bus.RegisterGateway(execBase - 414, state =>
		{
			Assert.Equal(dosBase, state.A[1]);
			closes++;
			events.Add("close-library");
		});
		bus.RegisterGateway(execBase - 198, state =>
		{
			var size = state.D[0];
			Assert.True(size > 0);
			Assert.Equal((uint)global::Amiga.Exec.MemoryFlags.Public, state.D[1]);
			var address = nextNativeBuffer;
			nextNativeBuffer += (size + 3) & ~3u;
			liveNativeAllocations.Add(address, size);
			if (size == 128)
			{
				Assert.Equal(0u, inputBuffer);
				inputBuffer = address;
			}
			events.Add($"alloc:{size}");
			state.D[0] = address;
		});
		bus.RegisterGateway(execBase - 210, state =>
		{
			Assert.True(
				liveNativeAllocations.Remove(state.A[1], out var size),
				$"Attempted to free unknown native allocation 0x{state.A[1]:X8}.");
			Assert.Equal(size, state.D[0]);
			events.Add($"free:{size}");
		});
		bus.RegisterGateway(dosBase - 54, state =>
		{
			inputs++;
			state.D[0] = inputHandle;
		});
		bus.RegisterGateway(dosBase - 60, state =>
		{
			outputs++;
			state.D[0] = outputHandle;
		});
		bus.RegisterGateway(dosBase - 42, state =>
		{
			Assert.Equal(inputHandle, state.D[1]);
			Assert.Equal(inputBuffer, state.D[2]);
			Assert.Equal(128u, state.D[3]);
			if (reads++ == 0)
			{
				input.CopyTo(bus.Memory.AsSpan(checked((int)state.D[2])));
				state.D[0] = checked((uint)input.Length);
			}
			else
			{
				state.D[0] = 0;
			}
		});
		bus.RegisterGateway(dosBase - 48, state =>
		{
			Assert.Equal(outputHandle, state.D[1]);
			var length = checked((int)state.D[3]);
			for (var index = 0; index < length; index++)
			{
				output.Add(bus.Memory[checked((int)state.D[2] + index)]);
			}
			state.D[0] = state.D[3];
		});

		var actualReturn = Execute(
			bus,
			model,
			HunkLoadAddress + result.EntryPoint,
			maxInstructions: 1_000_000);
		Assert.Equal(expectedReturn, actualReturn);
		Assert.Equal(1, opens);
		Assert.Equal(1, closes);
		Assert.Equal(1, inputs);
		Assert.Equal(expectedOutputCalls, outputs);
		Assert.Equal(1, reads);
		Assert.Empty(liveNativeAllocations);
		Assert.Equal("open-library", events[0]);
		Assert.Equal("close-library", events[^1]);
		Assert.Equal(
			previousDosBase,
			bus.ReadLong(HunkLoadAddress + dosBaseSlot.Address));
		Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
		return output.ToArray();
	}

	private static M68kCompilationResult Compile(
		M68kCpuTarget cpu,
		M68kOutputFormat format,
		string entry,
		M68kClrPolicy clrPolicy = M68kClrPolicy.Auto,
		M68kExceptionMode exceptionMode = M68kExceptionMode.Full,
		IReadOnlyDictionary<string, uint>? imports = null,
		M68kFloatingPointMode floatingPoint = M68kFloatingPointMode.Disabled,
		M68kFrameworkImplementationPackOptions? frameworkImplementationPack = null) =>
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
			FrameworkImplementationPack = frameworkImplementationPack,
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

	private static DictionaryOrderingMeasurement MeasureDictionaryOrdering(
		string entry,
		int maxInstructions)
	{
		var result = CompileWithAllocator(
			M68kCpuTarget.M68000,
			$"CopperSharp.Compiler.Tests.CompilerFixtures::{entry}");
		var bus = CreateHunkBus(result);
		var heap = 0x0000_4000u;
		var allocations = 0;
		bus.RegisterGateway(0x0000_2800, state =>
		{
			allocations++;
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
				afterReturn: state => cycles = state.Cycles,
				maxInstructions: maxInstructions));
		return new DictionaryOrderingMeasurement(
			result.Image.Length,
			result.Code.Length,
			allocations,
			result.AllocationStatistics.Count,
			result.AllocationStatistics.Max(static item => item.SpillFrameBytes),
			cycles);
	}

	private sealed record DictionaryOrderingMeasurement(
		int ImageBytes,
		int CodeBytes,
		int Allocations,
		int AnalyzedMethods,
		int MaximumSpillBytes,
		long Cycles);

	private static void AssertDictionaryOrderingBudget(
		DictionaryOrderingMeasurement measurement,
		int expectedAllocations,
		long maximumCycles)
	{
		Assert.True(
			measurement.ImageBytes <= 27_500,
			$"Ordering image grew to {measurement.ImageBytes} bytes.");
		Assert.True(
			measurement.CodeBytes <= 20_250,
			$"Ordering code grew to {measurement.CodeBytes} bytes.");
		Assert.Equal(expectedAllocations, measurement.Allocations);
		Assert.Equal(34, measurement.AnalyzedMethods);
		Assert.True(
			measurement.MaximumSpillBytes <= 4,
			$"Ordering exceeded one spill slot: {measurement.MaximumSpillBytes} bytes.");
		Assert.True(
			measurement.Cycles <= maximumCycles,
			$"Ordering grew to {measurement.Cycles} MC68000 cycles.");
	}

	private static uint ReferenceDivisionDifferentialCorpus()
	{
		var hash = 0x811C_9DC5u;
		hash = MixUnsigned(hash, 0, 1);
		hash = MixUnsigned(hash, 1, 1);
		hash = MixUnsigned(hash, uint.MaxValue, 1);
		hash = MixUnsigned(hash, uint.MaxValue, uint.MaxValue);
		hash = MixUnsigned(hash, uint.MaxValue, 0x8000_0000u);
		hash = MixUnsigned(hash, 0x8000_0000u, uint.MaxValue);
		hash = MixUnsigned(hash, 0xFFFF_0000u, 0x0000_FFFFu);

		var state = 0xC001_D00Du;
		for (var index = 0; index < 48; index++)
		{
			state = unchecked((state * 1_664_525u) + 1_013_904_223u);
			var dividend = state ^ (state << 13) ^ (state >> 9);
			state = unchecked((state * 22_695_477u) + 1u);
			hash = MixUnsigned(hash, dividend, state | 1u);
		}

		hash = MixSigned(hash, 0, 1);
		hash = MixSigned(hash, int.MaxValue, 1);
		hash = MixSigned(hash, int.MinValue, 1);
		hash = MixSigned(hash, int.MinValue, int.MinValue);
		hash = MixSigned(hash, int.MaxValue, -1);
		hash = MixSigned(hash, -17, 5);
		hash = MixSigned(hash, 17, -5);
		hash = MixSigned(hash, -17, -5);
		for (var index = 0; index < 32; index++)
		{
			state = unchecked((state * 1_103_515_245u) + 12_345u);
			var dividend = unchecked((int)(state ^ (state >> 11)));
			state = unchecked((state * 214_013u) + 2_531_011u);
			hash = MixSigned(hash, dividend, unchecked((int)(state | 1u)));
		}

		return hash;

		static uint MixUnsigned(uint current, uint dividend, uint divisor)
		{
			var quotient = dividend / divisor;
			var remainder = dividend % divisor;
			return unchecked((current * 16_777_619u) ^ quotient ^
				((remainder << 7) | (remainder >> 25)));
		}

		static uint MixSigned(uint current, int dividend, int divisor)
		{
			var quotient = dividend / divisor;
			var remainder = unchecked((uint)(dividend % divisor));
			return unchecked((current * 16_777_619u) ^ (uint)quotient ^
				((remainder << 11) | (remainder >> 21)));
		}
	}

	private static uint ExecuteHunk(
		M68kCompilationResult result,
		M68kCpuModel model,
		Action<IM68kCore, TestBus>? beforeInstruction = null,
		int maxInstructions = 200_000)
	{
		var bus = CreateHunkBus(result);
		return Execute(
			bus,
			model,
			HunkLoadAddress + result.EntryPoint,
			beforeInstruction,
			maxInstructions: maxInstructions);
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
		Action<M68kCpuState>? afterReturn = null,
		int maxInstructions = 200_000)
	{
		bus.WriteLong(StackPointer, ReturnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(entryPoint, StackPointer);
		initialize?.Invoke(cpu.State);
		for (var instruction = 0; instruction < maxInstructions; instruction++)
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
			$"{model} did not return after {maxInstructions} instructions; " +
			$"PC=${cpu.State.ProgramCounter:X8}.");
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

	private sealed class PlatformBaseStateResolver(uint baseA, uint baseB) : IM68kExternalCallResolver
	{
		public bool TryResolve(
			M68kExternalMethod method,
			out M68kExternalCallConvention convention)
		{
			var isSelector = method.DisplayName ==
				"CopperSharp.Compiler.Tests.CompilerFixtures::PlatformBaseStateSelector";
			var isA = method.DisplayName is
				"CopperSharp.Compiler.Tests.CompilerFixtures::PlatformBaseStateA" or
				"CopperSharp.Compiler.Tests.CompilerFixtures::PlatformBaseStateAAlias";
			if (!isA && !isSelector && method.DisplayName !=
				"CopperSharp.Compiler.Tests.CompilerFixtures::PlatformBaseStateB")
			{
				convention = null!;
				return false;
			}

			convention = new M68kExternalCallConvention(
				isSelector
					? "fixture.platform-base-selector"
					: isA ? "fixture.platform-base-a" : "fixture.platform-base-b",
				M68kExternalBaseSource.Immediate,
				M68kRegister.A6,
				-30,
				InitialValue: isSelector ? 0x0000_6000u : isA ? baseA : baseB,
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
