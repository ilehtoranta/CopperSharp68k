/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using Copper68k;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderStableDispatchTests
{
	[Fact]
	public void SnapshotBindingRequiresTheExactSealedReceiverAndReleasedInput()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);

		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
			foreach (var name in new[] { "StringBuilderStableSnapshot", "StringBuilderUnknownSnapshot", "StringBuilderProducedSnapshot", "StringBuilderConstructedSnapshot", "StringBuilderUnknownProducedSnapshot" })
			{
				var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::" + name);
				var call = caller.Instructions.Last(instruction => instruction.OpCode == OpCodes.Callvirt);
				var member = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
				Assert.Equal(admitted && name is "StringBuilderStableSnapshot" or "StringBuilderProducedSnapshot" or "StringBuilderConstructedSnapshot", module.TryCreatePinnedStringBuilderToStringBinding(member, caller, call.Offset, out var binding));
				Assert.False(module.TryCreatePinnedStringBuilderToStringBinding(member, null, call.Offset, out _));
				var direct = caller with { Instructions = caller.Instructions.Select(instruction => instruction.Offset == call.Offset ? instruction with { OpCode = OpCodes.Call } : instruction).ToArray() };
				Assert.False(module.TryCreatePinnedStringBuilderToStringBinding(member, direct, call.Offset, out _));
				Assert.False(module.TryCreatePinnedStringBuilderToStringBinding(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature), caller, call.Offset, out _));
				if (name is "StringBuilderProducedSnapshot" or "StringBuilderConstructedSnapshot")
				{
					var ingress = caller with { Instructions = caller.Instructions.Select((instruction, index) => index == 0
						? instruction with { OpCode = OpCodes.Br, Operand = call.Offset } : instruction).ToArray() };
					Assert.False(module.TryCreatePinnedStringBuilderToStringBinding(member, ingress, call.Offset, out _));
				}
				if (binding is not null)
				{
					Assert.Contains(FrameworkFeature.StringBuilder, binding.EffectSummary.RequiredFeatures);
					var implementation = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
					Assert.Equal("System.Text.StringBuilder::ToString", implementation.DisplayName);
					Assert.True(implementation.DeclaringTypeIsSealed);
				}
			}
		}
	}
}

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableTextExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderBenchmarkPresizedTextEntry", "StringBuilderBenchmarkGrowingTextEntry",
			"StringBuilderStableNullSnapshotEntry", "StringBuilderStableArgumentSnapshotEntry", "StringBuilderStableConstructorValidationEntry", "StringBuilderStableEditingEntry",
			"StringBuilderStableEditingValidationEntry", "StringBuilderStablePrimitiveTextEntry", "StringBuilderStableCultureEntry", "StringBuilderStableFormattingCallbacksEntry", "StringBuilderStableNumericProviderEntry", "StringBuilderStableIntegralEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableStringConstructorsExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableStringConstructorEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableIndexAndChunkFailuresExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableIndexAndChunkFailuresEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableArraySpansExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableArraySpanEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableArrayMemoryExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableArrayMemoryEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableApplicationStringSpansExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableApplicationStringSpanEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableReplacementExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableReplacementEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableEnumerationExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibStringCustomCallbackContractsEntry", "CoreLibInt32CustomCallbackContractsEntry",
			"CoreLibStringCustomIteratorOwnershipEntry", "CoreLibInt32CustomIteratorOwnershipEntry", "StringBuilderStableArrayJoinEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableReferenceSpansExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableReferenceSpanEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableObjectEnumerationExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibObjectCustomCallbackContractsEntry", "CoreLibObjectCustomIteratorOwnershipEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableObjectTextExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableObjectTextEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableFixedObjectFormattingExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableFixedObjectFormattingEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableGeneralObjectsExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableGeneralObjectsEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableIntegralInsertionExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableIntegralInsertionEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableHandlersExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableHandlersEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableCompositeFormatsExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibStringBuilderParsedCompositeFormatEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableCompositeProvidersExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibParsedCompositeFormatProviderContractsEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableCompositeValidationExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibParsedCompositeFormatValidationEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableDecimalSmokeExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibStringBuilderDecimalSmokeEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableDecimalMatrixExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibStringBuilderDecimalMatrixEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableDecimalProvidersExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibDecimalValueProviderContractsEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableDecimalArrayJoinExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableDecimalArrayJoinEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableDecimalCallbacksExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibDecimalCustomCallbackContractsEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableDecimalIteratorOwnershipExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibDecimalCustomIteratorOwnershipEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableDecimalListsExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableDecimalListGrowthEntry", "CoreLibDecimalListIteratorOwnershipEntry",
			"CoreLibStringBuilderDecimalEnumerableJoinEntry", "CoreLibStringBuilderBoxedDecimalEntry", "CoreLibStringBuilderDecimalJoinLifetimeEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableDecimalCapacityExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibStringBuilderDecimalCapacityEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableBoxedDecimalExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibStringBuilderBoxedDecimalEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableDecimalConstantsExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibDecimalPublicConstantsEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableNumberSettingsValidationExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableNumberSettingsValidationEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableNumberSettingsFallbackExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["StringBuilderStableNumberSettingsFallbackEntry"]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableBoxedEnumsExecuteWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, ["CoreLibStringBuilderObjectEnumsEntry", "CoreLibStringBuilderObjectEnumOwnershipEntry", "CoreLibStringBuilderObjectEnumCapacityEntry"]);

	public static IEnumerable<object[]> StableFloatingCases() => CpuTargets.SelectMany(cpu => new[] {
		"CoreLibStringBuilderFloatingPointSmokeEntry", "CoreLibFloatingBoxedFormatProbeEntry",
		"CoreLibFloatingHandlerFormatProbeEntry", "CoreLibFloatingParsedFormatProbeEntry"
	}.Select(entry => new object[] { cpu[0], cpu[1], entry }));

	[Theory]
	[MemberData(nameof(StableFloatingCases))]
	public void StringBuilderStableFloatingExecutesWithoutUnlistedBodies(M68kCpuTarget target, M68kCpuModel model, string entry)
		=> VerifyStableStringBuilderEntries(target, model, [entry], M68kFloatingPointMode.SoftFloat);

	private void VerifyStableStringBuilderEntries(M68kCpuTarget target, M68kCpuModel model, string[] entries,
		M68kFloatingPointMode floatingPoint = M68kFloatingPointMode.Disabled)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		foreach (var entry in entries)
		{
			if (entry == "CoreLibStringBuilderDecimalJoinLifetimeEntry")
				StringBuilderStableDecimalContractGraphTests.VerifyDecimalJoinLifetimeHostReference();
			else Assert.Equal(42, (int)typeof(CompilerFixtures).GetMethod(entry)!.Invoke(null, null)!);
			var request = new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::" + entry,
				ManagedAssemblyPaths = [typeof(CopperSharp.Runtime.AmigaPal.EnvironmentPal).Assembly.Location],
				IncludedExportNames = [], Cpu = target, OutputFormat = M68kOutputFormat.Hunk, ExceptionMode = M68kExceptionMode.Full,
				FloatingPoint = floatingPoint,
				PeepholeOptimization = mode, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 0x8000 },
				FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
			};
			var result = M68kCompiler.Compile(request);
			Assert.True(result.FrameworkAnalysis.IsCompatible);
			Assert.Contains("sha256=" + pack.Sha256, result.Map, StringComparison.Ordinal);
			Assert.True(HunkLoadAddress + result.Code.Length < 0x0010_0000);
			Assert.DoesNotContain(result.Symbols, symbol => symbol.Name == "System.Object::ToString" || symbol.Name.StartsWith("System.Reflection.", StringComparison.Ordinal));
			var artifact = Path.Combine(AppContext.BaseDirectory, $"stringbuilder-stable-text-{entry}-{target}-{mode}");
			File.WriteAllText(artifact + ".map", result.Map);
			var replacementGrow = entry == "StringBuilderStableReplacementEntry"
				? result.Symbols.Single(symbol => symbol.Name == "CopperSharp.Runtime.ShadowValueListBuilder`1<int>::Grow") : default;
			var replacementGrowths = 0;
			var instructions = 0;
			// These callback and parsed-format validation matrices use the same ceiling
			// as the broader formatting runner, with the same native result oracle.
			var instructionLimit = entry is "CoreLibStringCustomCallbackContractsEntry" or "CoreLibInt32CustomCallbackContractsEntry" or "CoreLibObjectCustomCallbackContractsEntry"
				or "CoreLibParsedCompositeFormatProviderContractsEntry"
				or "CoreLibParsedCompositeFormatValidationEntry"
				or "CoreLibStringBuilderDecimalMatrixEntry"
				or "CoreLibDecimalValueProviderContractsEntry"
				or "CoreLibDecimalCustomCallbackContractsEntry"
				or "CoreLibStringBuilderDecimalCapacityEntry"
					or "CoreLibStringBuilderObjectEnumsEntry"
					or "CoreLibStringBuilderGenericBooleanCharJoinsEntry"
					or "CoreLibGenericBooleanCharCallbackContractsEntry"
					or "CoreLibGenericBooleanCharOwnershipEntry"
					or "CoreLibGenericBooleanCharCapacityEntry"
					or "CoreLibStringBuilderGenericIntegralJoinsEntry"
					or "CoreLibStringBuilderGenericEnumJoinsEntry"
					or "CoreLibGenericJoinCallbackContractsEntry"
					or "CoreLibGenericJoinIteratorOwnershipEntry"
					or "CoreLibGenericJoinBoxedIteratorContractsEntry"
					or "CoreLibGenericEnumJoinCapacityContractsEntry"
					|| StringBuilderNullableJoinAdmissionTests.BehaviorEntries.Contains(entry, StringComparer.Ordinal)
					|| StringBuilderApplicationJoinAdmissionTests.BehaviorEntries.Contains(entry, StringComparer.Ordinal)
				? 500_000_000 : 20_000_000;
			if (entry == "CoreLibStringBuilderFloatingPointSmokeEntry") instructionLimit = 500_000_000;
			else if (entry is "CoreLibFloatingBoxedFormatProbeEntry" or "CoreLibFloatingHandlerFormatProbeEntry" or "CoreLibFloatingParsedFormatProbeEntry") instructionLimit = 5_000_000;
			var nativeBus = CreateHunkBus(result);
			var actual = Execute(nativeBus, model, HunkLoadAddress + result.EntryPoint,
				beforeInstruction: (cpu, memory) => {
					instructions++; if (replacementGrow.Size > 0 && cpu.State.ProgramCounter == HunkLoadAddress + replacementGrow.Address) replacementGrowths++;
				},
				initialStackPointer: 0x0020_0000, maxInstructions: instructionLimit);
			Assert.True(actual == 42u, $"{entry} on {target} ({mode}) returned {actual}; expected 42.");
			Assert.True(entry != "StringBuilderStableReplacementEntry" || replacementGrowths > 0, "The replacement fixture must execute integer-buffer growth under collection.");
			File.WriteAllText(artifact + ".json", System.Text.Json.JsonSerializer.Serialize(new {
				Entry = entry, Cpu = target.ToString(), Optimization = mode.ToString(), Result = 42,
				CoreLibSha256 = pack.Sha256, EnableUnlistedManagedBodies = false, Collection = "EveryAllocation", ReplacementBufferGrowths = replacementGrowths,
				Instructions = instructions, InstructionLimit = instructionLimit, FloatingPoint = floatingPoint.ToString()
			}));
		}
	}
}
