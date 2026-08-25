/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Security.Cryptography;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Tests.MultiModule;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeCompatibilityTests
{
	[Fact]
	public void FreestandingYoloReportUsesFinalCountsAndExactReachableAssemblies()
	{
		var primaryAssembly = Assembly.GetExecutingAssembly();
		var dependencyAssembly = typeof(ExternalMethods).Assembly;
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = primaryAssembly.Location,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::MultiModuleEntry",
			ManagedAssemblyPaths = [dependencyAssembly.Location],
			Cpu = M68kCpuTarget.M68000,
			ExceptionMode = M68kExceptionMode.Yolo,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			OutputFormat = M68kOutputFormat.Hunk
		});

		var compatibility = result.NativeCompatibility;
		Assert.Equal(M68kExceptionMode.Yolo, compatibility.ExceptionMode);
		Assert.Equal(M68kMemoryManagement.None, compatibility.MemoryManagement);
		Assert.Equal(0, compatibility.ExceptionRegionCount);
		Assert.Equal(0, compatibility.FatalMachineFaultSiteCount);
		Assert.Empty(compatibility.RuntimeFeatures);
		Assert.Equal(0, compatibility.RuntimeFeatureCount);
		Assert.Empty(compatibility.RuntimeHelpers);
		Assert.Equal(0, compatibility.RuntimeHelperCount);
		Assert.Empty(compatibility.ExternalNativeTargets);
		Assert.Equal(0, compatibility.ExternalNativeTargetCount);

		Assert.Equal(
			new[]
			{
				primaryAssembly.GetName().Name,
				dependencyAssembly.GetName().Name
			},
			compatibility.ReachableAssemblies
				.Select(static identity => identity.Name)
				.ToArray());
		Assert.Equal(2, compatibility.ReachableAssemblyCount);
		AssertExactIdentity(compatibility, primaryAssembly);
		AssertExactIdentity(compatibility, dependencyAssembly);

		Assert.Contains(
			"NATIVE exceptions=Yolo memory=None exception-regions=0 " +
			"fatal-machine-fault-sites=0 runtime-features=0 runtime-helpers=0 " +
			"external-native-targets=0 " +
			"reachable-assemblies=2",
			result.Map,
			StringComparison.Ordinal);
		Assert.Contains("RUNTIME HELPERS", result.Map, StringComparison.Ordinal);
		Assert.Contains("EXTERNAL NATIVE TARGETS", result.Map,
			StringComparison.Ordinal);
		Assert.Contains("REACHABLE ASSEMBLIES", result.Map, StringComparison.Ordinal);
		foreach (var identity in compatibility.ReachableAssemblies)
		{
			var token = identity.PublicKeyToken.Length == 0
				? "-"
				: identity.PublicKeyToken;
			Assert.Contains(
				$"{identity.Name} {identity.Version} pkt={token} " +
				$"mvid={identity.Mvid:D} sha256={identity.Sha256}",
				result.Map,
				StringComparison.Ordinal);
		}
	}

	[Fact]
	public void FullExceptionReportCountsRegionsFatalSitesAndRuntimeHelpers()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::TryCatchEntry",
			Cpu = M68kCpuTarget.M68000,
			ExceptionMode = M68kExceptionMode.Full,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			OutputFormat = M68kOutputFormat.Assembly
		});

		var compatibility = result.NativeCompatibility;
		Assert.Equal(M68kExceptionMode.Full, compatibility.ExceptionMode);
		Assert.Equal(M68kMemoryManagement.None, compatibility.MemoryManagement);
		Assert.Equal(1, compatibility.ExceptionRegionCount);
		Assert.True(compatibility.FatalMachineFaultSiteCount > 0);
		Assert.Contains("__c68k_exception_raise", compatibility.RuntimeHelpers);
		Assert.Equal(
			compatibility.RuntimeHelpers.Count,
			compatibility.RuntimeHelperCount);
		Assert.Empty(compatibility.ExternalNativeTargets);
		Assert.Equal(0, compatibility.ExternalNativeTargetCount);
		Assert.Contains(
			$"exception-regions=1 fatal-machine-fault-sites=" +
			$"{compatibility.FatalMachineFaultSiteCount}",
			result.Map,
			StringComparison.Ordinal);
		Assert.Contains(
			$"runtime-helpers={compatibility.RuntimeHelperCount}",
			result.Map,
			StringComparison.Ordinal);
	}

	[Fact]
	public void ExternalNativeTargetsReportEveryExternalAssemblerReference()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CallImport",
			Cpu = M68kCpuTarget.M68000,
			ExceptionMode = M68kExceptionMode.Yolo,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			OutputFormat = M68kOutputFormat.Assembly
		});

		var compatibility = result.NativeCompatibility;
		Assert.Equal(new[] { "fixture.value" },
			compatibility.ExternalNativeTargets);
		Assert.Equal(1, compatibility.ExternalNativeTargetCount);
		Assert.Empty(compatibility.RuntimeHelpers);
		Assert.Contains("external-native-targets=1", result.Map,
			StringComparison.Ordinal);
		Assert.Contains(
			"EXTERNAL NATIVE TARGETS" + Environment.NewLine + "fixture.value",
			result.Map,
			StringComparison.Ordinal);
	}

	[Fact]
	public void ExecutableInstructionEvidenceStopsBeforeAllDataSections()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4E75); // RTS: the only executable instruction.
		assembler.MarkDataStart();
		assembler.EmitWord(0x4AFC);
		assembler.MarkWritableDataStart();
		assembler.EmitWord(0x4AFC);
		assembler.MarkBssStart();
		assembler.EmitWord(0x4AFC);

		Assert.Contains(assembler.GetInstructionStream(),
			static instruction => instruction.Opcode == 0x4AFC);
		var executable = Assert.Single(
			assembler.GetExecutableInstructionStream());
		Assert.Equal(0x4E75, executable.Opcode);
	}

	[Fact]
	public void FatalMachineFaultEvidenceIgnoresIllegalOpcodeWordInLiteralData()
	{
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint =
				"CopperSharp.Compiler.Tests.CompilerFixtures::IllegalOpcodeDataEntry",
			Cpu = M68kCpuTarget.M68000,
			ExceptionMode = M68kExceptionMode.Yolo,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			OutputFormat = M68kOutputFormat.Hunk
		});

		Assert.True(result.Code.AsSpan().IndexOf(new byte[] { 0x4A, 0xFC }) >= 0,
			"The fixture must retain the ILLEGAL opcode word in emitted literal data.");
		Assert.Equal(0,
			result.NativeCompatibility.FatalMachineFaultSiteCount);
	}

	private static void AssertExactIdentity(
		M68kNativeCompatibility compatibility,
		Assembly assembly)
	{
		var name = assembly.GetName();
		var identity = Assert.Single(
			compatibility.ReachableAssemblies,
			candidate => candidate.Name == name.Name);
		Assert.Equal(name.Version?.ToString(), identity.Version);
		Assert.Equal(
			Convert.ToHexString(name.GetPublicKeyToken() ?? []).ToLowerInvariant(),
			identity.PublicKeyToken);
		Assert.Equal(assembly.ManifestModule.ModuleVersionId, identity.Mvid);
		using var stream = File.OpenRead(assembly.Location);
		Assert.Equal(
			Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
			identity.Sha256);
	}
}
