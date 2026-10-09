/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderReplacementAdmissionTests
{
	[Fact]
	public void ReplacementBufferBodiesRequireTheirExactIntegerConstructionAndOwnedCaller()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		var definition = StringBuilderFrameworkSurface.Members.First(member => member.Name == "Replace");
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.StringBuilder"), definition.Name, definition.Signature);
		foreach (var member in StringBuilderReplacementSurface.Members)
		{
			Assert.True(Admit(member, caller));
			Assert.False(Admit(member, null));
			Assert.False(Admit(new(member.DeclaringType, "Unlisted", member.Signature), caller));
			Assert.False(Admit(new(FrameworkTypeId.GenericInstantiation(member.DeclaringType.ElementType!, [FrameworkTypeId.Primitive("System.Int64")]), member.Name, member.Signature), caller));
			Assert.False(Admit(new(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")]), caller));
		}
		pack.Replace("packVersion", "10.0.10");
		catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		Assert.False(Admit(StringBuilderReplacementSurface.Members[0], caller));
		bool Admit(FrameworkMemberId member, FrameworkMemberId? owner) =>
			FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, owner, out _);
	}

	[Fact]
	public void ReplacementFixtureMatchesCoreLib() => Assert.Equal(42, CompilerFixtures.StringBuilderStableReplacementEntry());

	[Fact]
	public void ReplacementGraphIsCompatibleWithoutUnlistedBodies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableReplacementEntry",
			IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-replacement-analysis.json"),
			System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
		Assert.True(result.IsCompatible, string.Join("\n", result.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported).Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}
}

public static partial class CompilerFixtures
{
	public static int StringBuilderStableReplacementEntry()
	{
		var builder = new System.Text.StringBuilder(1024);
		for (var i = 0; i < 1024; i++) builder.Append('a');
		var snapshot = builder.ToString();
		GC.Collect();
		builder.Replace("a", "\u03a9\0");
		GC.Collect();
		if (builder.Length != 2048 || snapshot.Length != 1024 || snapshot[0] != 'a') return 1;
		for (var i = 0; i < 2048; i++) if (builder[i] != (i % 2 == 0 ? '\u03a9' : '\0')) return 2;
		builder.Replace("\u03a9\0".AsSpan(), "Q".AsSpan(), 0, 1024);
		GC.Collect();
		if (builder.Length != 1536 || builder[511] != 'Q' || builder[512] != '\u03a9') return 3;
		builder.Replace("Q", null);
		GC.Collect();
		if (builder.Length != 1024 || builder[0] != '\u03a9') return 4;
		var crossChunk = new System.Text.StringBuilder(1).Append("ababa");
		crossChunk.Replace("aba", "\ud800\0"); GC.Collect();
		if (crossChunk.ToString() != "\ud800\0ba") return 6;
		var failures = 0;
		try { builder.Replace((string)null!, "x"); } catch (ArgumentNullException) { failures++; }
		try { builder.Replace("", "x"); } catch (ArgumentException) { failures++; }
		try { builder.Replace("x", "y", -1, 1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { builder.Replace("x", "y", 0, 1025); } catch (ArgumentOutOfRangeException) { failures++; }
		GC.Collect();
		return failures == 4 && builder.Length == 1024 && snapshot[1023] == 'a' ? 42 : 5;
	}
}
