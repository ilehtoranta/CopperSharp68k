/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderIndexAndChunkFailureTests
{
	[Fact]
	public void IndexAndChunkFailureFixtureMatchesCoreLib()
		=> Assert.Equal(42, CompilerFixtures.StringBuilderStableIndexAndChunkFailuresEntry());

	[Fact]
	public void IndexAndChunkFailureGraphIsCompatibleWithoutUnlistedBodies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var analysis = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableIndexAndChunkFailuresEntry", IncludedExportNames = [],
			ExceptionMode = M68kExceptionMode.Full, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 0x8000 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-index-chunk-failures-analysis.json"),
			System.Text.Json.JsonSerializer.Serialize(analysis, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
		Assert.True(analysis.IsCompatible, string.Join("\n", analysis.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
			.Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}
}

public static partial class CompilerFixtures
{
	public static int StringBuilderStableIndexAndChunkFailuresEntry()
	{
		var builder = new System.Text.StringBuilder(1).Append("A\0\u03a9\ud800");
		var snapshot = builder.ToString();
		var failures = 0;
		try { _ = builder[-1]; } catch (IndexOutOfRangeException) { failures++; }
		try { _ = builder[builder.Length]; } catch (IndexOutOfRangeException) { failures++; }
		try { builder[-1] = 'x'; } catch (ArgumentOutOfRangeException) { failures++; }
		try { builder[builder.Length] = 'x'; } catch (ArgumentOutOfRangeException) { failures++; }
		System.Text.StringBuilder.ChunkEnumerator invalid = default;
		try { _ = invalid.Current; } catch (InvalidOperationException) { failures++; }
		GC.Collect();
		if (failures != 5 || builder.ToString() != snapshot) return 1;
		builder[2] = '\u03bb';
		var chunks = builder.GetChunks().GetEnumerator();
		var length = 0;
		while (chunks.MoveNext()) { length += chunks.Current.Length; GC.Collect(); }
		builder.Append('!'); GC.Collect();
		return length == 4 && snapshot == "A\0\u03a9\ud800" && builder.ToString() == "A\0\u03bb\ud800!" ? 42 : 2;
	}
}
