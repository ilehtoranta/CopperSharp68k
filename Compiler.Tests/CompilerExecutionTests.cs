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
		Assert.Contains("\tmove.l\ta7,8(a7)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmove.l\t#$00000000,12(a5)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmovea.l\t8(a5),a7", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tjmp\t(a1)", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tdc.w\t$2E6D", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tdc.w\t$4ED1", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("__c68k_eh_", result.Text, StringComparison.Ordinal);
		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name == "__c68k_exception_table");
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
	public void UnhandledExceptionHookIsOptionalAndReturnsToIllegal()
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
			$"\tjsr\t{M68kRuntimeImports.UnhandledException}\r\n\tillegal",
			withHook.Text,
			StringComparison.Ordinal);
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

	[Fact]
	public void MaterializedComparisonStoresResultWithoutStackRoundTrip()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::MaterializedEqualityEntry");

		Assert.Equal(21u, ExecuteHunk(result, M68kCpuModel.M68000));
		Assert.Contains("\tcmp.l\t4(a7),d0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\t(a7)+,d7", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmoveq\t#1,d7", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tmove.l\t(a7)+,d1\r\n\tmove.l\t(a7)+,d0\r\n\tcmp.l\td1,d0",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tseq\td0\r\n\text.w\td0\r\n\text.l\td0\r\n\tneg.l\td0\r\n\tmove.l\td0,-(a7)\r\n",
			result.Text,
			StringComparison.Ordinal);
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
	public void ClrIsTargetGatedAndOptInForM68000()
	{
		var m68000 = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NullableAptrNullEntry");
		Assert.DoesNotContain("\tclr.l\t-(a7)", m68000.Text, StringComparison.Ordinal);

		var m68020 = Compile(
			M68kCpuTarget.M68020,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NullableAptrNullEntry");
		Assert.Contains("\tclr.l\t-(a7)", m68020.Text, StringComparison.Ordinal);

		var disabled = Compile(
			M68kCpuTarget.M68020,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NullableAptrNullEntry",
			M68kClrPolicy.Never);
		Assert.DoesNotContain("\tclr.l\t-(a7)", disabled.Text, StringComparison.Ordinal);

		var optIn = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"CopperSharp.Compiler.Tests.CompilerFixtures::NullableAptrNullEntry",
			M68kClrPolicy.Always);
		Assert.Contains("\tclr.l\t-(a7)", optIn.Text, StringComparison.Ordinal);
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
		Assert.Contains("\tsubq.l\t#1,d7", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tcmpi.w\t#1,d7", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tcmpi.l\t#$00000001,d7", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\td7,d0\r\n\tcmpi.l\t#$00000001,d0", result.Text, StringComparison.Ordinal);
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

		Assert.Contains("\tmovea.l\ta7,a1", result.Text, StringComparison.Ordinal);
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

		Assert.Contains("\tmovea.l\ta7,a1", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmovem.l\td0-d6,(a7)", result.Text, StringComparison.Ordinal);
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
			Assert.Equal("value: %ld\n", ReadCString(bus, state.D[1]));
			Assert.Equal(10u, bus.ReadLong(state.D[2]));
			state.D[0] = 12;
		});

		Assert.Equal(12u, Execute(bus, model, HunkLoadAddress + result.EntryPoint));
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
		if (target is M68kCpuTarget.M68020 or M68kCpuTarget.M68040)
		{
			Assert.Contains("\tclr.l\t_DOSLibraryBase", result.Text, StringComparison.Ordinal);
		}
		else
		{
			Assert.Contains("\tmove.l\t#$00000000,_DOSLibraryBase", result.Text, StringComparison.Ordinal);
		}
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
		bus.RegisterGateway(0x0000_2600, _ => { });

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
		Assert.Contains("\tmove.l\td0,-(a7)\r\n\tpea\t(a0)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmovea.l\t(a7)+,a0\r\n\tmove.l\t(a7)+,d0\r\n\tbsr.w\tC68K_method_", result.Text, StringComparison.Ordinal);
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
		Assert.Contains("\r\nC68K_entry_003Amanaged:\r\n\tmove.l\t$0004.w,_ExecBase", result.Text, StringComparison.Ordinal);
		Assert.Contains("\r\nC68K_entry_003Amanaged:\r\n\tmove.l\t$0004.w,_ExecBase\r\nC68K_method_", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tjmp\tC68K_method_", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmovea.l\t#$00004400,a5", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\t$0004.w,d0", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmove.l\t(a7)+,_ExecBase", result.Text, StringComparison.Ordinal);
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
		Assert.Contains("\tmovea.l\t#$00004400,a5", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmovea.l\ta4,a6", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tmovea.l\t#$00004400,a4", result.Text, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void PromotedTransparentScalarLocalsUseA6WhenOtherAddressRegistersAreBusy(
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
		Assert.Matches(@"\tmovea\.l\t#\$00000[1-5]00,a6", result.Text);
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
				StringSplitOptions.None).Length - 1 >= 2);
		Assert.Matches(@"\tmovea\.l\t#\$00000[1-5]00,a6", result.Text);
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
		Assert.Contains(
			"\tmoveq\t#0,d4\r\n\tmovem.l\td0-d4,(a7)",
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
			"\tsubq.l\t#4,a7\r\n\tmove.l\ta0,0(a7)",
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
		Assert.DoesNotContain(
			"\tmovea.l\ta7,a0",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tlea\t\d+\(a7\),a0\r?\n\tbsr\.w\tC68K_method_003A0600067[14]",
			result.Text);
		Assert.DoesNotContain(
			"\tmove.l\ta7,d2",
			result.Text,
			StringComparison.Ordinal);
		Assert.DoesNotMatch(
			@"\tmove\.l\t#C68K_cstring_[A-Za-z0-9_]+,d0\r?\n\tmove\.l\td0,\d+\(a7\)\r?\n",
			result.Text);
		Assert.Matches(
			@"\tmove\.l\t#C68K_cstring_[A-Za-z0-9_]+,\d+\(a7\)\r?\n",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t#\$[0-9A-F]{8},d0\r?\n\tmove\.l\td0,\d+\(a7\)\r?\n",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t#(\$[0-9A-F]{8}|C68K_cstring_[A-Za-z0-9_]+),-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmove\.l\t\(a7\)\+,\d+\(a7\)",
			result.Text);
		Assert.DoesNotMatch(
			@"\tmove\.l\t#C68K_cstring_[A-Za-z0-9_]+,-\(a7\)\r?\n(?:[A-Za-z0-9_:]+:\r?\n)*\tmovea\.l\t\(a7\)\+,a0",
			result.Text);
		Assert.Matches(
			@"\tmovea\.l\t#C68K_cstring_[A-Za-z0-9_]+,a0",
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
		Assert.Contains(
			"\tmovea.l\ta7,a1\r\n\tjsr\tC68K_amiga_002Eboopsi_002EDoMethodA",
			result.Text,
			StringComparison.Ordinal);
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
		Assert.Contains(
			"\tmove.l\t(a0),d0\r\n\trts",
			result.Text,
			StringComparison.Ordinal);
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
		Assert.Contains(
			"\tmovea.l\t#C68K_export_003Amuitasklist_002Eapp_002Edispatcher,a3",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"\tmove.l\t#C68K_export_003Amuitasklist_002Elist_002Edisplay,d0",
			result.Text,
			StringComparison.Ordinal);
		Assert.Contains(
			"\tmovem.l\td0-d4,(a7)",
			result.Text,
			StringComparison.Ordinal);
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
		Assert.Contains(
			"\tmove.l\tC68K_static_003A04000003,d0\r\n\tbeq.w",
			result.Text,
			StringComparison.Ordinal);
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
				"\tmove.l\t$0004.w,_ExecBase",
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
		Assert.Contains("\tmovea.l\t#$00001800,a1", result.Text, StringComparison.Ordinal);
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
		Assert.Contains("\tjsr\t-1066(a6)", result.Text, StringComparison.Ordinal);
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
		Assert.Contains("\tjsr\t-1078(a6)", result.Text, StringComparison.Ordinal);
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

		Assert.Equal(M68kDiagnosticIds.StaticAnalysis, exception.DiagnosticId);
		Assert.Contains("managed heap", exception.Message, StringComparison.Ordinal);
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
		Assert.Contains("\tbsr.w\t__c68k_gc_mark", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tbsr.w\t__c68k_gc_collect", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain("\tbra.w\t__c68k_gc_coalesce", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmovem.l\td2-d4/a2,-(a7)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tmovem.l\t(a7)+,d2-d4/a2", result.Text, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"\tbsr.w\t__c68k_gc_coalesce\r\n\trts",
			result.Text,
			StringComparison.Ordinal);
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
		Assert.Contains("move.b\td0,12(a0)", result.Text, StringComparison.Ordinal);
		Assert.Contains("move.w\td0,12(a0)", result.Text, StringComparison.Ordinal);
		Assert.Contains("move.b\t12(a0),d0", result.Text, StringComparison.Ordinal);
		Assert.Contains("move.w\t12(a0),d0", result.Text, StringComparison.Ordinal);
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
		Assert.Contains("\taddq.l\t#3,(a7)", result.Text, StringComparison.Ordinal);
		Assert.Contains("\tsubq.l\t#2,(a7)", result.Text, StringComparison.Ordinal);
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
		Assert.DoesNotContain(
			"\taddq.l\t#4,a7\r\n\taddq.l\t#4,a7\r\n\tjmp\t",
			result.Text,
			StringComparison.Ordinal);
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
		Assert.DoesNotContain(
			"\tmove.l\td2,-(a7)\r\n\tmove.l\td3,-(a7)\r\n\tmove.l\td4,-(a7)",
			assembly.Text,
			StringComparison.Ordinal);
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
		Assert.Contains("\tmove.l\t#C68K_export_003Afixture_002Eadd,-(a7)", assembly.Text, StringComparison.Ordinal);
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

	private static string BeforeExceptionRuntime(M68kCompilationResult result)
	{
		var text = result.Text ?? string.Empty;
		var runtimeOffset = text.IndexOf(
			"__c68k_exception_raise:",
			StringComparison.Ordinal);
		return runtimeOffset < 0 ? text : text[..runtimeOffset];
	}

	private static M68kCompilationResult Compile(
		M68kCpuTarget cpu,
		M68kOutputFormat format,
		string entry,
		M68kClrPolicy clrPolicy = M68kClrPolicy.Auto,
		M68kExceptionMode exceptionMode = M68kExceptionMode.Full,
		IReadOnlyDictionary<string, uint>? imports = null) =>
		AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = entry,
			Cpu = cpu,
			ClrPolicy = clrPolicy,
			ExceptionMode = exceptionMode,
			Imports = imports ?? new Dictionary<string, uint>(),
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
