using System.Runtime.CompilerServices;
using CopperSharp.Compiler.Metadata;
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed class LargeClassGcLayoutTests
{
	[Fact]
	public void InheritedAndConstructedClassesKeepReferencesAcrossBitmapWords()
	{
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location);
		var factory = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CreateLargeGcOwner");
		var target = module.ResolveRuntimeTypeIdentity(factory.Signature.ReturnType, module.AssemblyName);
		var layout = module.GetRuntimeTypeLayout(target);
		Assert.Equal(272, layout.Size); // header and 66 payload words
		Assert.Equal(0u, layout.ReferenceBitmap);
		Assert.Equal(new uint[] { 0x8000_0001, 3 }, layout.AdditionalReferenceBitmaps);
	}

	[Fact]
	public void LargeReferenceBearingStructsStillRequireTransportSupport()
	{
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location);
		var method = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::LargeReferenceStructIdentity");
		var target = module.ResolveRuntimeTypeIdentity(method.Signature.ReturnType, module.AssemblyName);
		var error = Assert.Throws<M68kCompilationException>(() => module.GetRuntimeTypeLayout(target));
		Assert.Contains("exceed the descriptor bitmap", error.Message);
	}
}

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void LargeClassReferencesSurviveCollection(M68kCpuTarget target, M68kCpuModel model)
	{
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::LargeClassCollectionEntry",
				Cpu = target, OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions { StartAddress = 0x0004_0000, Size = 0x2000 }
			});
			Assert.Equal(42u, Execute(CreateHunkBus(result), model, HunkLoadAddress + result.EntryPoint, maxInstructions: 20_000_000));
		}
	}
}

public static partial class CompilerFixtures
{
#pragma warning disable CS0649 // Scalar holes deliberately separate reference words.
	public class LargeGcBase
	{
		public int Low;
		public int P00, P01, P02, P03, P04, P05, P06, P07, P08, P09, P10, P11, P12, P13, P14, P15,
			P16, P17, P18, P19, P20, P21, P22, P23, P24, P25, P26, P27, P28, P29, P30;
		public int[]? At32;
		public int Q00, Q01, Q02, Q03, Q04, Q05, Q06, Q07, Q08, Q09, Q10, Q11, Q12, Q13, Q14, Q15,
			Q16, Q17, Q18, Q19, Q20, Q21, Q22, Q23, Q24, Q25, Q26, Q27, Q28, Q29;
		public int[]? At63;
	}
	public class LargeGcDerived<T> : LargeGcBase
	{
		public int[]? At64;
		public T? At65;
	}
	public struct LargeReferenceStruct
	{
		public int P00, P01, P02, P03, P04, P05, P06, P07, P08, P09, P10, P11, P12, P13, P14, P15,
			P16, P17, P18, P19, P20, P21, P22, P23, P24, P25, P26, P27, P28, P29, P30, P31;
		public object? BeyondInlineBitmap;
	}
#pragma warning restore CS0649
	public static LargeReferenceStruct LargeReferenceStructIdentity(LargeReferenceStruct value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static LargeGcDerived<int[]> CreateLargeGcOwner() => new()
	{
		Low = 3, At32 = new[] { 7 }, At63 = new[] { 11 }, At64 = new[] { 13 }, At65 = new[] { 8 }
	};

	public static int LargeClassCollectionEntry()
	{
		var owner = CreateLargeGcOwner();
		for (var i = 0; i < 100; i++)
		{
			var discarded = new int[32];
			discarded[0] = i + 100;
		}
		return owner.Low + owner.At32![0] + owner.At63![0] + owner.At64![0] + owner.At65![0];
	}
}
