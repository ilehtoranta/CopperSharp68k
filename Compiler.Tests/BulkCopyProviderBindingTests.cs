using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class BulkCopyProviderBindingTests
{
	private static readonly string FixtureAssembly =
		typeof(BulkCopyBindingFixture).Assembly.Location;
	private static readonly string FixtureAssemblyName =
		typeof(BulkCopyBindingFixture).Assembly.GetName().Name!;

	[Fact]
	public void NullProviderPreservesInlineCopiesAndDefaultReachability()
	{
		var request = Request(nameof(BulkCopyBindingFixture.AggregateEntry));
		var defaultResult = M68kCompiler.Compile(request);
		var explicitNullResult = M68kCompiler.Compile(request with { BulkCopy = null });

		Assert.Equal(defaultResult.Code, explicitNullResult.Code);
		Assert.Equal(defaultResult.Symbols, explicitNullResult.Symbols);
		Assert.Equal(1, defaultResult.FrameworkAnalysis.RootMethodCount);
		Assert.DoesNotContain(defaultResult.Symbols,
			symbol => symbol.Name == Selector(nameof(BulkCopyBindingFixture.CopyPointers)));
		Assert.Contains(defaultResult.Symbols,
			symbol => symbol.Name == Selector(nameof(BulkCopyBindingFixture.MakeBlock)));
	}

	[Theory]
	[InlineData(nameof(BulkCopyBindingFixture.CopyPointers), 1)]
	[InlineData(nameof(BulkCopyBindingFixture.CopyPointers), 64)]
	[InlineData(nameof(BulkCopyBindingFixture.CopyApointers), 64)]
	public void UnusedPointerAndApointerProvidersAreAnalyzedButNotEmitted(
		string provider,
		int minimumBytes)
	{
		var disabled = M68kCompiler.Compile(Request());
		var request = Request() with
		{
			BulkCopy = Managed(provider) with { MinimumBytes = minimumBytes }
		};

		var analysis = M68kCompiler.AnalyzeFramework(request);
		var result = M68kCompiler.Compile(request);

		Assert.True(analysis.IsCompatible);
		Assert.Equal(2, analysis.RootMethodCount);
		Assert.Empty(analysis.ManagedAllocationSites);
		Assert.DoesNotContain(result.Symbols, symbol => symbol.Name == Selector(provider));
		Assert.Equal(disabled.Code, result.Code);
	}

	[Fact]
	public void ThresholdAboveAllCopiesPrunesProviderAndKeepsInlineOutput()
	{
		var request = Request(nameof(BulkCopyBindingFixture.AggregateEntry));
		var disabled = M68kCompiler.Compile(request);
		var result = M68kCompiler.Compile(request with
		{
			BulkCopy = Managed(nameof(BulkCopyBindingFixture.CopyViaDependency)) with
			{
				MinimumBytes = int.MaxValue
			}
		});

		Assert.Equal(2, result.FrameworkAnalysis.RootMethodCount);
		Assert.Equal(disabled.Code, result.Code);
		Assert.DoesNotContain(result.Symbols,
			symbol => symbol.Name == Selector(nameof(BulkCopyBindingFixture.CopyViaDependency)) ||
				symbol.Name == Selector(nameof(BulkCopyBindingFixture.CopyPointers)));
	}

	[Fact]
	public void EligibleCopyRetainsManagedProviderAndItsCopyDependency()
	{
		var result = M68kCompiler.Compile(Request(nameof(BulkCopyBindingFixture.AggregateEntry)) with
		{
			BulkCopy = Managed(nameof(BulkCopyBindingFixture.CopyViaDependency))
		});

		Assert.Contains(result.Symbols,
			symbol => symbol.Name == Selector(nameof(BulkCopyBindingFixture.CopyViaDependency)));
		Assert.Contains(result.Symbols,
			symbol => symbol.Name == Selector(nameof(BulkCopyBindingFixture.CopyPointers)));
		Assert.Empty(result.FrameworkAnalysis.ManagedAllocationSites);
		Assert.Equal(M68kMemoryManagement.None, result.NativeCompatibility.MemoryManagement);
	}

	public static TheoryData<M68kBulkCopyOptions> InvalidProviderSelections => new()
	{
		new M68kBulkCopyOptions(),
		new M68kBulkCopyOptions { ManagedAssemblyName = FixtureAssemblyName },
		new M68kBulkCopyOptions { ManagedMethod = Selector(nameof(BulkCopyBindingFixture.CopyPointers)) },
		Managed(nameof(BulkCopyBindingFixture.CopyPointers)) with { ManagedMethod = " " },
		Managed(nameof(BulkCopyBindingFixture.CopyPointers)) with { ExternalCall = External() }
	};

	[Theory]
	[MemberData(nameof(InvalidProviderSelections))]
	public void InvalidOrMixedProviderSelectionFailsBeforeReadingInput(M68kBulkCopyOptions options)
	{
		AssertInvalidOptions(options);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	public void NonpositiveThresholdFailsBeforeReadingInput(int minimumBytes)
	{
		AssertInvalidOptions(Managed(nameof(BulkCopyBindingFixture.CopyPointers)) with
		{
			MinimumBytes = minimumBytes
		}, "positive");
	}

	[Theory]
	[InlineData(nameof(BulkCopyBindingFixture.ReturningProvider))]
	[InlineData(nameof(BulkCopyBindingFixture.GenericProvider))]
	[InlineData(nameof(BulkCopyBindingFixture.ReferenceProvider))]
	[InlineData(nameof(BulkCopyBindingFixture.ReferenceWrapperProvider))]
	[InlineData(nameof(BulkCopyBindingFixture.NarrowCountProvider))]
	[InlineData(nameof(BulkCopyBindingFixture.WideCountProvider))]
	[InlineData(nameof(BulkCopyBindingFixture.FloatCountProvider))]
	[InlineData(nameof(BulkCopyBindingFixture.TwoArgumentProvider))]
	[InlineData(nameof(BulkCopyBindingFixture.FourArgumentProvider))]
	[InlineData(nameof(BulkCopyBindingFixture.ImportProvider))]
	public void IncompatibleManagedSignatureReportsProviderLocation(string provider)
	{
		var request = Request() with { BulkCopy = Managed(provider) };
		var error = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.AnalyzeFramework(request));

		Assert.Equal(M68kDiagnosticIds.UnsupportedSignature, error.DiagnosticId);
		Assert.Equal(Selector(provider), error.Method);
		Assert.Contains("source, destination, byte-count", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("CopperSharp.Compiler.Tests.BulkCopyInstanceFixture::Copy")]
	[InlineData("CopperSharp.Compiler.Tests.BulkCopyGenericFixture`1::Copy")]
	public void InstanceAndOpenGenericProvidersCannotBeCompilerHelpers(string selector)
	{
		var error = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.AnalyzeFramework(Request() with
			{
				BulkCopy = Managed(nameof(BulkCopyBindingFixture.CopyPointers)) with
				{
					ManagedMethod = selector
				}
			}));

		Assert.Equal(M68kDiagnosticIds.UnsupportedSignature, error.DiagnosticId);
		Assert.Equal(selector, error.Method);
	}

	[Fact]
	public void ProviderOwnTypeInitializerIsRejectedBeforeAnalysisOrEmission()
	{
		var selector = $"{typeof(BulkCopyInitializedProviderFixture).FullName}::Copy";
		var request = Request() with
		{
			BulkCopy = Managed(nameof(BulkCopyBindingFixture.CopyPointers)) with
			{
				ManagedMethod = selector
			}
		};
		// The provider body does not access static fields. Its own initializer
		// must still be rejected: a synthesized call has no CIL trigger for it.
		var analysisError = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.AnalyzeFramework(request));
		var compileError = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(request));
		foreach (var error in new[] { analysisError, compileError })
		{
			Assert.Equal(M68kDiagnosticIds.StaticAnalysis, error.DiagnosticId);
			Assert.Equal(selector, error.Method);
			Assert.Contains("initializ", error.Message, StringComparison.OrdinalIgnoreCase);
		}
	}

	[Theory]
	[InlineData(nameof(BulkCopyBindingFixture.InitializingCallProvider))]
	[InlineData(nameof(BulkCopyBindingFixture.InitializingFieldProvider))]
	public void UnusedProviderCannotHideTypeInitializationInItsDependencyGraph(string provider)
	{
		var request = Request() with { BulkCopy = Managed(provider) };
		var analysis = M68kCompiler.AnalyzeFramework(request);
		Assert.True(analysis.IsCompatible);
		Assert.Equal(2, analysis.RootMethodCount);
		Assert.Empty(analysis.ManagedAllocationSites);

		// Both dependencies are heap-free and framework-compatible. A static
		// field access has a TypeInitialize edge but no logical managed call;
		// the backend must reject it before pruning this unused provider.
		var error = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(request));
		Assert.Equal(M68kDiagnosticIds.StaticAnalysis, error.DiagnosticId);
		Assert.Contains("initializ", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void MissingProviderAssemblyReportsConfiguredAssemblyIdentity()
	{
		const string missingAssembly = "CopperSharp.BulkCopyFixture.DoesNotExist";
		var error = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.AnalyzeFramework(Request() with
			{
				BulkCopy = Managed(nameof(BulkCopyBindingFixture.CopyPointers)) with
				{
					ManagedAssemblyName = missingAssembly
				}
			}));

		Assert.Equal(M68kDiagnosticIds.InvalidInput, error.DiagnosticId);
		Assert.Contains(missingAssembly, error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void MissingProviderMethodReportsConfiguredSelector()
	{
		var missingSelector = Selector("MissingProvider");
		var error = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.AnalyzeFramework(Request() with
			{
				BulkCopy = Managed(nameof(BulkCopyBindingFixture.CopyPointers)) with
				{
					ManagedMethod = missingSelector
				}
			}));

		Assert.Equal(M68kDiagnosticIds.EntryPointNotFound, error.DiagnosticId);
		Assert.Contains(missingSelector, error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void UnusedProviderStillRootsFrameworkDependencyDiagnostics()
	{
		var request = Request() with
		{
			BulkCopy = Managed(nameof(BulkCopyBindingFixture.FrameworkDependentProvider))
		};
		var analysis = M68kCompiler.AnalyzeFramework(request);
		var member = Assert.Single(analysis.Members,
			item => item.Member.TypeName == "System.Console");
		var callSite = Assert.Single(member.CallSites);

		Assert.Equal(2, analysis.RootMethodCount);
		Assert.False(analysis.IsCompatible);
		Assert.Equal(Selector(nameof(BulkCopyBindingFixture.FrameworkDependency)), callSite.Caller);
		Assert.Equal(Selector(nameof(BulkCopyBindingFixture.FrameworkDependentProvider)),
			callSite.RootPath[0]);
		var error = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(request));
		Assert.Equal(M68kDiagnosticIds.UnsupportedFrameworkMember, error.DiagnosticId);
		Assert.Contains(nameof(BulkCopyBindingFixture.FrameworkDependentProvider),
			error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void UnusedProviderStillRootsTransitiveNoHeapAnalysis()
	{
		var request = Request() with
		{
			BulkCopy = Managed(nameof(BulkCopyBindingFixture.AllocatingProvider))
		};
		var analysis = M68kCompiler.AnalyzeFramework(request);
		var allocation = Assert.Single(analysis.ManagedAllocationSites);

		Assert.True(analysis.IsCompatible);
		Assert.Equal(2, analysis.RootMethodCount);
		Assert.Equal(Selector(nameof(BulkCopyBindingFixture.AllocatingDependency)), allocation.Caller);
		Assert.Equal(Selector(nameof(BulkCopyBindingFixture.AllocatingProvider)), allocation.RootPath[0]);
		var error = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(request));
		Assert.Equal(M68kDiagnosticIds.StaticAnalysis, error.DiagnosticId);
		Assert.Equal(allocation.Caller, error.Method);
		Assert.NotNull(error.IlOffset);
		Assert.Contains("managed heap", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void CollectingIntrinsicCannotHideBehindReferenceFreeProviderSignature()
	{
		var request = Request() with
		{
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions { StartAddress = 0x4000, Size = 0x2000 },
			BulkCopy = Managed(nameof(BulkCopyBindingFixture.CollectingProvider))
		};
		var analysis = M68kCompiler.AnalyzeFramework(request);
		Assert.True(analysis.IsCompatible);
		Assert.Empty(analysis.ManagedAllocationSites);

		// GC.Collect has no object operands or managed allocation instruction.
		// With a valid collector configured, rejection must come from the
		// provider's nonsafepoint contract, not from a missing runtime or heap.
		var error = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(request));
		Assert.Equal(M68kDiagnosticIds.StaticAnalysis, error.DiagnosticId);
		Assert.Equal(Selector(nameof(BulkCopyBindingFixture.CollectingProvider)), error.Method);
		Assert.NotNull(error.IlOffset);
		Assert.Contains("collect", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(M68kExternalBaseSource.CachedPointer)]
	[InlineData(M68kExternalBaseSource.WritableSlot)]
	[InlineData(M68kExternalBaseSource.Immediate)]
	public void UnusedExternalProviderDoesNotIntroduceStartupOrManagedCode(
		M68kExternalBaseSource baseSource)
	{
		var convention = External() with
		{
			BaseSource = baseSource,
			CacheRegister = baseSource == M68kExternalBaseSource.CachedPointer ? M68kRegister.A5 : null,
			SourceAddress = 4,
			SlotSymbol = baseSource == M68kExternalBaseSource.WritableSlot ? "test_bulk_copy_base" : null,
			ClobberedRegisters = [M68kRegister.D2, M68kRegister.A2]
		};
		var disabled = M68kCompiler.Compile(Request());
		var result = M68kCompiler.Compile(Request() with
		{
			BulkCopy = new M68kBulkCopyOptions { ExternalCall = convention }
		});

		Assert.Equal(1, result.FrameworkAnalysis.RootMethodCount);
		Assert.Equal(disabled.Code, result.Code);
		Assert.Equal(disabled.Symbols, result.Symbols);
		Assert.Empty(result.NativeCompatibility.ExternalNativeTargets);
	}

	public static TheoryData<M68kExternalCallConvention, string> InvalidExternalConventions => new()
	{
		{ External() with { Identity = " " }, "identity" },
		{ External() with { BaseSource = M68kExternalBaseSource.Argument }, "dynamic base" },
		{ External() with { BaseRegister = M68kRegister.D3 }, "address registers" },
		{ External() with { CacheRegister = M68kRegister.A6 }, "distinct" },
		{ External() with { BaseSource = M68kExternalBaseSource.CachedPointer }, "cache register" },
		{ External() with { BaseSource = M68kExternalBaseSource.WritableSlot }, "slot symbol" },
		{ External() with { ParameterRegisters = [M68kRegister.A0, M68kRegister.A0, M68kRegister.D0] }, "three distinct" },
		{ External() with { ParameterRegisters = [M68kRegister.A0, M68kRegister.A1] }, "three distinct" },
		{ External() with { ParameterRegisters = [M68kRegister.A0, M68kRegister.A6, M68kRegister.D0] }, "three distinct" },
		{ External() with { ParameterRegisters = [M68kRegister.A0, M68kRegister.A1, (M68kRegister)99] }, "three distinct" },
		{ External() with { ClobberedRegisters = [(M68kRegister)(-1)] }, "clobbers" },
		{ External() with { ExceptionPolicy = M68kExternalExceptionPolicy.NonZeroStatus, ExceptionStatusRegister = M68kRegister.D0 }, "exceptions" },
		{ External() with { ExceptionStatusRegister = M68kRegister.D0 }, "exceptions" }
	};

	[Theory]
	[MemberData(nameof(InvalidExternalConventions))]
	public void InvalidExternalAbiFailsBeforeReadingInput(
		M68kExternalCallConvention convention,
		string expectedDiagnostic)
	{
		AssertInvalidOptions(new M68kBulkCopyOptions { ExternalCall = convention }, expectedDiagnostic);
	}

	[Theory]
	[InlineData(M68kRegister.A0)]
	[InlineData(M68kRegister.A1)]
	[InlineData(M68kRegister.A2)]
	[InlineData(M68kRegister.A5)]
	public void CachedExternalBaseMustSurviveTheProviderCall(M68kRegister cache)
	{
		var convention = External() with
		{
			BaseSource = M68kExternalBaseSource.CachedPointer,
			CacheRegister = cache,
			SourceAddress = 4,
			// Keep the cache distinct from argument/base registers so the failing
			// contract is its lifetime, not duplicate ABI register assignment.
			ParameterRegisters = [M68kRegister.D0, M68kRegister.D1, M68kRegister.D2],
			ClobberedRegisters = cache < M68kRegister.A2 ? [] : [cache]
		};
		AssertInvalidOptions(new M68kBulkCopyOptions { ExternalCall = convention }, "preserved");
	}

	[Theory]
	[InlineData(M68kRegister.A2)]
	[InlineData(M68kRegister.A3)]
	[InlineData(M68kRegister.A4)]
	[InlineData(M68kRegister.A5)]
	[InlineData(M68kRegister.A6)]
	public void PreservedExternalCacheRegistersKeepUnusedProviderPayForPlay(M68kRegister cache)
	{
		var convention = External() with
		{
			BaseSource = M68kExternalBaseSource.CachedPointer,
			BaseRegister = cache == M68kRegister.A6 ? M68kRegister.A5 : M68kRegister.A6,
			CacheRegister = cache,
			SourceAddress = 4,
			ClobberedRegisters = [M68kRegister.D0, M68kRegister.D1, M68kRegister.A0, M68kRegister.A1]
		};
		var disabled = M68kCompiler.Compile(Request());
		var result = M68kCompiler.Compile(Request() with
		{
			BulkCopy = new M68kBulkCopyOptions { ExternalCall = convention }
		});

		Assert.Equal(disabled.Code, result.Code);
		Assert.Equal(disabled.Symbols, result.Symbols);
		Assert.Empty(result.NativeCompatibility.ExternalNativeTargets);
	}

	private static void AssertInvalidOptions(M68kBulkCopyOptions options, string? expectedDiagnostic = null)
	{
		// A missing PE makes it observable that public option validation runs
		// before module loading in both analysis and compilation entry points.
		var request = Request() with
		{
			AssemblyPath = FixtureAssembly + ".not-an-assembly",
			BulkCopy = options
		};
		var analysisError = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.AnalyzeFramework(request));
		var compileError = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(request));
		foreach (var error in new[] { analysisError, compileError })
		{
			Assert.Equal(M68kDiagnosticIds.InvalidOutputOptions, error.DiagnosticId);
			Assert.Null(error.Method);
			if (expectedDiagnostic is not null)
			{
				Assert.Contains(expectedDiagnostic, error.Message, StringComparison.OrdinalIgnoreCase);
			}
		}
	}

	private static M68kCompilationRequest Request(string entry = nameof(BulkCopyBindingFixture.ScalarEntry)) => new()
	{
		AssemblyPath = FixtureAssembly,
		EntryPoint = Selector(entry),
		IncludedExportNames = [],
		Cpu = M68kCpuTarget.M68000,
		OutputFormat = M68kOutputFormat.Assembly,
		RuntimeProfile = M68kRuntimeProfile.Freestanding,
		MemoryManagement = M68kMemoryManagement.None,
		ExceptionMode = M68kExceptionMode.Yolo
	};

	private static string Selector(string method) => $"{typeof(BulkCopyBindingFixture).FullName}::{method}";

	private static M68kBulkCopyOptions Managed(string method) => new()
	{
		ManagedAssemblyName = FixtureAssemblyName,
		ManagedMethod = Selector(method)
	};

	private static M68kExternalCallConvention External() => new(
		"test.bulk-copy",
		M68kExternalBaseSource.Immediate,
		M68kRegister.A6,
		-624,
		InitialValue: 0x0000_8000,
		ParameterRegisters: [M68kRegister.A0, M68kRegister.A1, M68kRegister.D0]);
}

public static class BulkCopyBindingFixture
{
	public static int ScalarEntry() => 42;

	public static uint AggregateEntry() => ConsumeBlock(MakeBlock());

	[StructLayout(LayoutKind.Sequential)]
	public struct Block
	{
		public uint First;
		public uint Word01;
		public uint Word02;
		public uint Word03;
		public uint Word04;
		public uint Word05;
		public uint Word06;
		public uint Word07;
		public uint Word08;
		public uint Word09;
		public uint Word10;
		public uint Word11;
		public uint Word12;
		public uint Word13;
		public uint Word14;
		public uint Word15;
		public uint Word16;
		public uint Word17;
		public uint Word18;
		public uint Last;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Block MakeBlock() => new() { First = 19, Last = 23 };

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ConsumeBlock(Block value) => value.First + value.Last;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe void CopyPointers(byte* source, byte* destination, uint count)
	{
		for (uint index = 0; index < count; index++)
		{
			destination[index] = source[index];
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void CopyApointers(APTR source, APTR destination, uint count)
	{
		for (uint index = 0; index < count; index++)
		{
			APTR.WriteUInt8(destination, unchecked((int)index),
				APTR.ReadUInt8(source, unchecked((int)index)));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe void CopyViaDependency(byte* source, byte* destination, uint count) =>
		CopyPointers(source, destination, count);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe void InitializingCallProvider(byte* source, byte* destination, uint count) =>
		BulkCopyInitializedDependencyFixture.Copy(source, destination, count);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe void InitializingFieldProvider(byte* source, byte* destination, uint count)
	{
		if (BulkCopyInitializedDependencyFixture.Ready != 0)
		{
			CopyPointers(source, destination, count);
		}
	}

	public static void FrameworkDependentProvider(uint source, uint destination, uint count) =>
		FrameworkDependency(count);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void FrameworkDependency(uint count) => Console.WriteLine((int)count);

	public static unsafe void AllocatingProvider(byte* source, byte* destination, uint count) =>
		AllocatingDependency(destination, count);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe void CollectingProvider(byte* source, byte* destination, uint count)
	{
		GC.Collect();
		CopyPointers(source, destination, count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe void AllocatingDependency(byte* destination, uint count)
	{
		var bytes = new byte[count];
		*destination = bytes[0];
	}

	public static uint ReturningProvider(uint source, uint destination, uint count) => count;
	public static void GenericProvider<T>(uint source, uint destination, uint count) { }
	public static void ReferenceProvider(ref byte source, uint destination, uint count) { }
	public static void ReferenceWrapperProvider(ReferenceWord source, uint destination, uint count) { }
	public static void NarrowCountProvider(uint source, uint destination, byte count) { }
	public static void WideCountProvider(uint source, uint destination, ulong count) { }
	public static void FloatCountProvider(uint source, uint destination, float count) { }
	public static void TwoArgumentProvider(uint source, uint destination) { }
	public static void FourArgumentProvider(uint source, uint destination, uint count, uint extra) { }

	[M68kImport("test.bulk-copy.import")]
	public static void ImportProvider(uint source, uint destination, uint count) { }

	public struct ReferenceWord
	{
		public object? Value;
	}
}

public sealed class BulkCopyInstanceFixture
{
	public void Copy(uint source, uint destination, uint count) { }
}

public static class BulkCopyGenericFixture<T>
{
	public static void Copy(uint source, uint destination, uint count) { }
}

public static class BulkCopyInitializedProviderFixture
{
	static BulkCopyInitializedProviderFixture() { Ready = 1; }
	public static uint Ready;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe void Copy(byte* source, byte* destination, uint count) =>
		BulkCopyBindingFixture.CopyPointers(source, destination, count);
}

public static class BulkCopyInitializedDependencyFixture
{
	static BulkCopyInitializedDependencyFixture() { Ready = 1; }
	public static uint Ready;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe void Copy(byte* source, byte* destination, uint count) =>
		BulkCopyBindingFixture.CopyPointers(source, destination, count);
}
