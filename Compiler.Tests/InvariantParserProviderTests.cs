/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Runtime;
using CopperSharp.Runtime.AmigaPal;

namespace CopperSharp.Compiler.Tests;

public sealed class InvariantParserProviderTests
{
	[Theory]
	[InlineData("DateOnly")]
	[InlineData("TimeOnly")]
	public void StackOriginsMatchTheActualProviderArgument(string type)
	{
		var inline = Analyze(type, (OpCodes.Ldnull, null), (OpCodes.Call, 1), (OpCodes.Call, 2), (OpCodes.Pop, null));
		Assert.True(inline.AdmitsGetter(1));
		Assert.True(inline.AdmitsParser(2));
		var wrongSlot = Analyze(type, (OpCodes.Call, 1), (OpCodes.Ldnull, null), (OpCodes.Call, 2), (OpCodes.Pop, null));
		Assert.False(wrongSlot.AdmitsGetter(0));
		Assert.False(wrongSlot.AdmitsParser(2));
		var discarded = Analyze(type, (OpCodes.Ldnull, null), (OpCodes.Call, 1), (OpCodes.Pop, null),
			(OpCodes.Ldnull, null), (OpCodes.Call, 2), (OpCodes.Pop, null));
		Assert.False(discarded.AdmitsGetter(1));
		Assert.False(discarded.AdmitsParser(4));
		var wrongOutSlot = Analyze(type, (OpCodes.Ldnull, null), (OpCodes.Ldnull, null),
			(OpCodes.Call, 1), (OpCodes.Call, 3), (OpCodes.Pop, null));
		Assert.False(wrongOutSlot.AdmitsGetter(2));
		Assert.False(wrongOutSlot.AdmitsParser(3));
	}

	[Theory]
	[InlineData("DateOnly")]
	[InlineData("TimeOnly")]
	public void FieldStoresAndReturnsAreObservableUses(string type)
	{
		var stored = Analyze(type, (OpCodes.Call, 1), (OpCodes.Dup, null), (OpCodes.Stloc_0, null),
			(OpCodes.Stsfld, 123), (OpCodes.Ldnull, null), (OpCodes.Ldloc_0, null), (OpCodes.Call, 2), (OpCodes.Pop, null));
		Assert.False(stored.AdmitsGetter(0));
		Assert.False(stored.AdmitsParser(6));
		var returned = Analyze(type, (OpCodes.Call, 1), (OpCodes.Stloc_0, null),
			(OpCodes.Ldnull, null), (OpCodes.Ldloc_0, null), (OpCodes.Call, 2), (OpCodes.Pop, null),
			(OpCodes.Ldloc_0, null), (OpCodes.Ret, null));
		Assert.False(returned.AdmitsGetter(0));
		Assert.False(returned.AdmitsParser(4));
	}

	[Theory]
	[InlineData("DateOnly")]
	[InlineData("TimeOnly")]
	public void CachedAliasesAndTryParseOutSlotAreTracked(string type)
	{
		var result = Analyze(type, (OpCodes.Call, 1), (OpCodes.Stloc_0, null), (OpCodes.Ldloc_0, null),
			(OpCodes.Stloc_1, null), (OpCodes.Ldnull, null), (OpCodes.Ldloc_1, null),
			(OpCodes.Ldloca_S, 2), (OpCodes.Call, 3), (OpCodes.Pop, null));
		Assert.True(result.AdmitsGetter(0));
		Assert.True(result.AdmitsParser(7));
	}

	[Fact]
	public void StackJoinsAndDuplicatedSafeParserUsesPreserveOrigins()
	{
		var result = Analyze("TimeOnly", (OpCodes.Ldc_I4_0, null), (OpCodes.Brtrue, 4),
			(OpCodes.Call, 1), (OpCodes.Br, 5), (OpCodes.Call, 1), (OpCodes.Dup, null),
			(OpCodes.Stloc_0, null), (OpCodes.Stloc_1, null),
			(OpCodes.Ldnull, null), (OpCodes.Ldloc_0, null), (OpCodes.Call, 2), (OpCodes.Pop, null),
			(OpCodes.Ldnull, null), (OpCodes.Ldloc_1, null), (OpCodes.Call, 2), (OpCodes.Pop, null));
		Assert.True(result.AdmitsGetter(2));
		Assert.True(result.AdmitsGetter(4));
		Assert.True(result.AdmitsParser(10));
		Assert.True(result.AdmitsParser(14));
	}

	[Theory]
	[InlineData("DateOnly")]
	[InlineData("TimeOnly")]
	public void ObservingAliasesAndWrongCallOpcodesCannotAuthorizeAPlaceholder(string type)
	{
		var observed = Analyze(type, (OpCodes.Call, 1), (OpCodes.Dup, null), (OpCodes.Stloc_0, null),
			(OpCodes.Call, 4), (OpCodes.Ldnull, null), (OpCodes.Ldloc_0, null), (OpCodes.Call, 2), (OpCodes.Pop, null));
		Assert.False(observed.AdmitsGetter(0));
		Assert.False(observed.AdmitsParser(6));
		var wrongOpcode = Analyze(type, (OpCodes.Ldnull, null), (OpCodes.Call, 1), (OpCodes.Newobj, 2), (OpCodes.Pop, null));
		Assert.False(wrongOpcode.AdmitsGetter(1));
		Assert.False(wrongOpcode.AdmitsParser(2));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void BranchJoinRequiresInvariantProvidersOnEveryPath(bool mixed)
	{
		var result = Analyze("DateOnly", (OpCodes.Ldc_I4_0, null), (OpCodes.Brtrue, 5),
			(OpCodes.Call, 1), (OpCodes.Stloc_0, null), (OpCodes.Br, 7),
			(OpCodes.Call, mixed ? 5 : 1), (OpCodes.Stloc_0, null),
			(OpCodes.Ldnull, null), (OpCodes.Ldloc_0, null), (OpCodes.Call, 2), (OpCodes.Pop, null));
		Assert.Equal(!mixed, result.AdmitsGetter(2));
		Assert.Equal(!mixed, result.AdmitsParser(9));
		Assert.Equal(!mixed, result.AdmitsGetter(5));
	}

	[Fact]
	public void ReassignmentAndAddressTakenLocalsCannotRetainAStaleProof()
	{
		var overwritten = Analyze("DateOnly", (OpCodes.Call, 1), (OpCodes.Stloc_0, null),
			(OpCodes.Call, 5), (OpCodes.Stloc_0, null), (OpCodes.Ldnull, null),
			(OpCodes.Ldloc_0, null), (OpCodes.Call, 2), (OpCodes.Pop, null));
		Assert.False(overwritten.AdmitsGetter(0));
		Assert.False(overwritten.AdmitsParser(6));
		var addressed = Analyze("TimeOnly", (OpCodes.Ldloca_S, 0), (OpCodes.Pop, null),
			(OpCodes.Call, 1), (OpCodes.Stloc_0, null), (OpCodes.Ldnull, null),
			(OpCodes.Ldloc_0, null), (OpCodes.Call, 2), (OpCodes.Pop, null));
		Assert.False(addressed.AdmitsGetter(2));
		Assert.False(addressed.AdmitsParser(6));
	}

	[Fact]
	public void EscapingUseInvalidatesEarlierParserProofEvenAfterUnknownJoin()
	{
		var result = Analyze("TimeOnly", (OpCodes.Call, 1), (OpCodes.Stloc_0, null),
			(OpCodes.Ldnull, null), (OpCodes.Ldloc_0, null), (OpCodes.Call, 2), (OpCodes.Pop, null),
			(OpCodes.Ldc_I4_0, null), (OpCodes.Brtrue, 11), (OpCodes.Ldnull, null),
			(OpCodes.Stloc_0, null), (OpCodes.Br, 11), (OpCodes.Ldloc_0, null), (OpCodes.Call, 4));
		Assert.False(result.AdmitsGetter(0));
		Assert.False(result.AdmitsParser(4));
	}

	[Fact]
	public void UnsafeGetterAtJoinInvalidatesAllConnectedGetterPlaceholders()
	{
		var result = Analyze("DateOnly", (OpCodes.Call, 1), (OpCodes.Stloc_0, null),
			(OpCodes.Call, 1), (OpCodes.Stloc_1, null), (OpCodes.Ldc_I4_0, null), (OpCodes.Brtrue, 8),
			(OpCodes.Ldloc_0, null), (OpCodes.Br, 9), (OpCodes.Ldloc_1, null), (OpCodes.Stloc_2, null),
			(OpCodes.Ldnull, null), (OpCodes.Ldloc_2, null), (OpCodes.Call, 2), (OpCodes.Pop, null),
			(OpCodes.Ldnull, null), (OpCodes.Ldloc_1, null), (OpCodes.Call, 2), (OpCodes.Pop, null),
			(OpCodes.Ldloc_0, null), (OpCodes.Call, 4));
		Assert.False(result.AdmitsGetter(0));
		Assert.False(result.AdmitsGetter(2));
		Assert.False(result.AdmitsParser(12));
		Assert.False(result.AdmitsParser(16));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void LoopBackEdgesParticipateInTheMustProof(bool replace)
	{
		var result = Analyze("DateOnly", (OpCodes.Call, 1), (OpCodes.Stloc_0, null),
			(OpCodes.Ldnull, null), (OpCodes.Ldloc_0, null), (OpCodes.Call, 2), (OpCodes.Pop, null),
			(OpCodes.Call, replace ? 5 : 1), (OpCodes.Stloc_0, null), (OpCodes.Ldc_I4_0, null), (OpCodes.Brtrue, 2));
		Assert.Equal(!replace, result.AdmitsGetter(0));
		Assert.Equal(!replace, result.AdmitsParser(4));
	}

	[Fact]
	public void CatchPathsCannotHideAnEscapingProvider()
	{
		var program = new (OpCode, object?)[] {
			(OpCodes.Call, 1), (OpCodes.Stloc_0, null), (OpCodes.Ldnull, null), (OpCodes.Ldloc_0, null),
			(OpCodes.Call, 2), (OpCodes.Pop, null), (OpCodes.Leave, 11),
			(OpCodes.Pop, null), (OpCodes.Ldloc_0, null), (OpCodes.Call, 4), (OpCodes.Leave, 11) };
		var result = Analyze("DateOnly", program, [new CilExceptionRegion(ExceptionRegionKind.Catch, 2, 5, 7, 4, default, -1)]);
		Assert.False(result.AdmitsGetter(0));
		Assert.False(result.AdmitsParser(4));
	}

	[Fact]
	public void FinallyAndMalformedStackFlowsFailClosed()
	{
		var code = new (OpCode, object?)[] { (OpCodes.Ldnull, null), (OpCodes.Call, 1), (OpCodes.Call, 2), (OpCodes.Pop, null) };
		var result = Analyze("DateOnly", code, [new CilExceptionRegion(ExceptionRegionKind.Finally, 0, 4, 4, 1, default, -1)]);
		Assert.False(result.AdmitsGetter(1));
		Assert.False(result.AdmitsParser(2));
		result = Analyze("TimeOnly", (OpCodes.Call, 1), (OpCodes.Call, 2), (OpCodes.Pop, null));
		Assert.False(result.AdmitsGetter(0));
		Assert.False(result.AdmitsParser(1));
	}

	[Fact]
	public void ActualMethodAnalysisIsCachedAndSharedByBothParserBindings()
	{
		using var module = new CompilationModule(typeof(InvariantParserProviderFixtures).Assembly.Location);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.InvariantParserProviderFixtures::CachedEntry");
		var call = entry.Instructions.First(i => i.OpCode == OpCodes.Call);
		var method = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset).Definition!;
		var result = module.GetInvariantParserProviders(method);
		Assert.Same(result, module.GetInvariantParserProviders(method));
		Assert.Equal(4, method.Instructions.Count(i => result.AdmitsParser(i.Offset)));
		Assert.Equal(2, method.Instructions.Count(i => result.AdmitsGetter(i.Offset)));
		Assert.Equal(42, InvariantParserProviderFixtures.CachedEntry());
	}

	[Theory]
	[InlineData("OverwrittenEntry")]
	[InlineData("EscapingEntry")]
	[InlineData("MixedEntry")]
	[InlineData("NullEntry")]
	[InlineData("AliasedEntry")]
	public void UnsafeProviderFlowsAreRejectedByPinnedCompilation(string entry)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var error = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(InvariantParserProviderFixtures).Assembly.Location,
			EntryPoint = $"CopperSharp.Compiler.Tests.InvariantParserProviderFixtures::{entry}",
			IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(M68kRuntime).Assembly.Location, typeof(EnvironmentPal).Assembly.Location],
			Cpu = M68kCpuTarget.M68000,
			FloatingPoint = M68kFloatingPointMode.SoftFloat,
			OutputFormat = M68kOutputFormat.Hunk,
			ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		}));
		Assert.Contains("C68K0012", error.Message, StringComparison.Ordinal);
	}

	private static InvariantParserProviderAnalysis Analyze(string type, params (OpCode, object?)[] program) => Analyze(type, program, []);

	private static InvariantParserProviderAnalysis Analyze(string type, (OpCode, object?)[] program, IReadOnlyList<CilExceptionRegion> regions)
	{
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location);
		var method = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibTimeOnlyParseQualificationEntry");
		var instructions = program.Concat([(OpCodes.Ldc_I4_0, (object?)null), (OpCodes.Ret, null)])
			.Select((i, index) => new CilInstruction(index, i.Item1, i.Item2, index + 1)).ToArray();
		method = method with { Instructions = instructions, ExceptionRegions = regions,
			Locals = Enumerable.Repeat(new CilType(CilTypeKind.ManagedReference, 4, "object"), 3).ToImmutableArray() };
		var value = FrameworkTypeId.Named("System.Runtime", $"System.{type}");
		var provider = FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider");
		var culture = FrameworkTypeId.Named("System.Runtime", "System.Globalization.CultureInfo");
		var text = FrameworkTypeId.Primitive("System.String");
		return InvariantParserProviderAnalysis.Analyze(method, i => (int)i.Operand! switch
		{
			1 => new(culture, "get_InvariantCulture", new(0, 0, 0, culture, [])),
			2 => new(value, "Parse", new(0, 0, 0, value, [text, provider])),
			3 => new(value, "TryParse", new(0, 0, 0, FrameworkTypeId.Primitive("System.Boolean"), [text, provider, FrameworkTypeId.ByReference(value)])),
			4 => new(FrameworkTypeId.Named("Example", "Consumer"), "Observe", new(0, 0, 0, FrameworkTypeId.Primitive("System.Void"), [provider])),
			5 => new(culture, "get_CurrentCulture", new(0, 0, 0, culture, [])),
			_ => throw new InvalidOperationException()
		});
	}
}
