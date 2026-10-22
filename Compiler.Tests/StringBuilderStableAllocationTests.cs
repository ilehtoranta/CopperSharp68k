/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	internal static readonly (string Entry, bool ShortLeaf, int Groups)[] StringBuilderAllocationFamilies = [
		("CoreLibFloatingAllocationLeafContractsEntry", true, 12),
		("CoreLibFloatingLargeAllocationContractsEntry", false, 12),
		("CoreLibStringBuilderFloatingAllocationContractsEntry", false, 64),
		("CoreLibFloatingAdapterAllocationCleanupEntry", false, 2),
		("CoreLibFloatingCustomEnumerableAllocationEntry", false, 8),
		("CoreLibStringAdapterAllocationCleanupEntry", false, 2),
		("CoreLibStringCustomEnumerableAllocationEntry", false, 4),
		("CoreLibObjectAdapterAllocationCleanupEntry", false, 2),
		("CoreLibObjectCustomEnumerableAllocationEntry", false, 4),
		("CoreLibInt32AdapterAllocationCleanupEntry", false, 2),
		("CoreLibInt32CustomEnumerableAllocationEntry", false, 4),
		("CoreLibDecimalAdapterAllocationCleanupEntry", false, 2),
		("CoreLibDecimalCustomEnumerableAllocationEntry", false, 4),
		("CoreLibFloatingHandlerConsumerAllocationContractsEntry", false, 64),
		("CoreLibStringBuilderFloatingLeftAllocationContractsEntry", false, 64),
		("CoreLibFloatingCrossChunkInsertAllocationContractsEntry", false, 8),
		("CoreLibFloatingFirstUseAllocationFailureEntry", false, 2),
		("CoreLibFloatingIndexedAllocationContractsEntry", false, 32),
		("CoreLibFloatingSymbolAllocationContractsEntry", false, 12),
		("CoreLibStringBuilderFloatingDefaultAllocationContractsEntry", false, 32),
		("CoreLibGenericJoinAdapterAllocationCleanupEntry", false, 2),
		("CoreLibGenericJoinEnumerableAllocationEntry", false, 4),
		("CoreLibStringBuilderObjectEnumAllocationEntry", false, 40),
		("CoreLibGenericBooleanCharAllocationEntry", false, 16),
		("CoreLibGenericApplicationValueAllocationEntry", false, 16),
		("CoreLibGenericNullableAllocationEntry", false, 17),
		("CoreLibGenericDecimalNullableAllocationEntry", false, 16),
		("CoreLibGenericFloatingNullableAllocationEntry", false, 16),
		("CoreLibGenericWideNullableAllocationEntry", false, 16),
		("CoreLibGenericSmallNullableAllocationEntry", false, 16),
		("CoreLibStringBuilderProviderArrayFormatAllocationEntry", false, 4),
		("CoreLibGenericApplicationReferenceAllocationEntry", false, 8),
		("CoreLibGenericApplicationNullableAllocationEntry", false, 8),
	];

	public static IEnumerable<object[]> StableStringBuilderAllocationCpuCases()
	{
		foreach (var cpu in CpuTargets)
		foreach (var family in StringBuilderAllocationFamilies)
			yield return [cpu[0], cpu[1], family.Entry, family.ShortLeaf, family.Groups];
	}

	[Theory]
	[MemberData(nameof(StableStringBuilderAllocationCpuCases))]
	public void StringBuilderStableAllocationCorpusExecutesWithoutUnlistedBodies(
		M68kCpuTarget target, Copper68k.M68kCpuModel model, string entry, bool shortLeaf, int groups)
		=> RunFloatingAllocationContracts(target, model, entry, shortLeaf, groups, enableUnlistedManagedBodies: false);
}

public sealed class StringBuilderStableAllocationGraphTests
{
	public static IEnumerable<object[]> Families => CompilerExecutionTests.StringBuilderAllocationFamilies.Select(family => new object[] { family.Entry });

	[Fact]
	public void CorpusRetainsAll33OriginalAllocationFamilies()
	{
		Assert.Equal(33, CompilerExecutionTests.StringBuilderAllocationFamilies.Length);
		Assert.Equal(33, CompilerExecutionTests.StringBuilderAllocationFamilies.Select(family => family.Entry).Distinct(StringComparer.Ordinal).Count());
		Assert.All(CompilerExecutionTests.StringBuilderAllocationFamilies, family => Assert.NotNull(typeof(CompilerFixtures).GetMethod(family.Entry)));
		// Compare the new matrix with the compiled original test calls, including
		// leaf limits and failure-region groups, rather than trusting its row count.
		var helper = typeof(CompilerExecutionTests).GetMethod("RunFloatingAllocationContracts", BindingFlags.NonPublic | BindingFlags.Static)!;
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location);
		var type = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == nameof(CompilerExecutionTests));
		var original = new List<(string Entry, bool ShortLeaf, int Groups)>();
		foreach (var handle in module.Reader.GetTypeDefinition(type).GetMethods())
		{
			if (module.Reader.GetMethodDefinition(handle).RelativeVirtualAddress == 0) continue;
			var method = module.GetMethod(handle);
			for (var index = 4; index < method.Instructions.Count; index++)
			{
				var call = method.Instructions[index];
				if (call.OpCode != OpCodes.Call || call.Operand is not int token || token != helper.MetadataToken || method.Instructions[index - 4].OpCode != OpCodes.Ldstr) continue;
				var entry = module.Reader.GetUserString(System.Reflection.Metadata.Ecma335.MetadataTokens.UserStringHandle((int)method.Instructions[index - 4].Operand! & 0x00FFFFFF));
				original.Add((entry, Integer(method.Instructions[index - 3]) != 0, Integer(method.Instructions[index - 2])));
			}
		}
		Assert.Equal(CompilerExecutionTests.StringBuilderAllocationFamilies.OrderBy(family => family.Entry, StringComparer.Ordinal), original.OrderBy(family => family.Entry, StringComparer.Ordinal));
		static int Integer(CilInstruction instruction) => instruction.OpCode == OpCodes.Ldc_I4 || instruction.OpCode == OpCodes.Ldc_I4_S
			? Convert.ToInt32(instruction.Operand) : instruction.OpCode.Value >= OpCodes.Ldc_I4_M1.Value && instruction.OpCode.Value <= OpCodes.Ldc_I4_8.Value
				? instruction.OpCode.Value - OpCodes.Ldc_I4_0.Value : throw new InvalidOperationException("The original allocation call must retain literal options.");
	}

	[Theory]
	[MemberData(nameof(Families))]
	public void ReleasedAllocationGraphsRequireDisabledUnlistedBodies(string entry)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var request = new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::" + entry, IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(CopperSharp.Runtime.AmigaPal.EnvironmentPal).Assembly.Location],
			FloatingPoint = M68kFloatingPointMode.SoftFloat, ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ExternalAllocator,
			Imports = new Dictionary<string, uint> { [M68kRuntimeImports.Allocate] = 0x2800,
				["fixture.string-builder-allocation-failure"] = 0x2A00, ["fixture.floating-allocation-case"] = 0x2B00, [M68kRuntimeImports.GcCollect] = 0x2C00 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		};
		var artifact = Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-allocation-graph-" + entry + ".json");
		M68kFrameworkAnalysisResult result;
		try { result = M68kCompiler.AnalyzeFramework(request); }
		catch (M68kCompilationException error)
		{
			File.WriteAllText(artifact, System.Text.Json.JsonSerializer.Serialize(new {
				EntryPoint = entry, IsCompatible = false, CoreLibSha256 = pack.Sha256, EnableUnlistedManagedBodies = false,
				error.DiagnosticId, error.Method, error.IlOffset, Error = error.Message
			}));
			throw;
		}
		Assert.Equal(pack.Sha256, Assert.Single(result.ImplementationPack!.Assemblies).Sha256);
		File.WriteAllText(artifact, System.Text.Json.JsonSerializer.Serialize(result));
		Assert.True(result.IsCompatible, string.Join("\n", result.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
			.Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}
}
