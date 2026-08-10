/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using CopperSharp.Compiler.Output;

namespace CopperSharp.Compiler.Tests;

public sealed class HunkWriterTests
{
	private const uint HunkBss = 0x0000_03EB;

	[Theory]
	[InlineData(16, false)]
	[InlineData(20, true)]
	public void UsesBssOnlyWhenSerializedImageIsStrictlySmaller(
		int bssBytes,
		bool expectedBss)
	{
		var code = new byte[4 + bssBytes];
		BinaryPrimitives.WriteUInt32BigEndian(code, 0x4E75_4E75u);

		var image = HunkWriter.Write(
			code,
			[],
			[],
			new Dictionary<string, int>(),
			4,
			new HashSet<string>(),
			new HunkOutputOptions { IncludeSymbols = false });
		var words = EnumerateLongWords(image).ToArray();

		Assert.Equal(expectedBss ? 2u : 1u, words[2]);
		Assert.Equal(expectedBss, words.Contains(HunkBss));
	}

	[Fact]
	public void RebaseBssTargetsAndEmitRelocationsToSecondHunk()
	{
		var code = new byte[24];
		BinaryPrimitives.WriteUInt32BigEndian(code, 4u);
		var image = HunkWriter.Write(
			code,
			[new M68kRelocation(0, "bss:value")],
			[],
			new Dictionary<string, int> { ["bss:value"] = 4 },
			4,
			new HashSet<string>(),
			new HunkOutputOptions { IncludeSymbols = false });
		var words = EnumerateLongWords(image).ToArray();

		Assert.Equal(2u, words[2]);
		Assert.Equal(0u, words[9]); // BSS-relative addend.
		Assert.Equal(0x0000_03ECu, words[10]);
		Assert.Equal(1u, words[11]);
		Assert.Equal(1u, words[12]); // Relocation target is hunk one.
		Assert.Equal(0u, words[13]);
		Assert.Equal(HunkBss, words[16]);
	}

	[Fact]
	public void KeepsSingleCodeHunkWhenCandidateSuffixIsNotAllZero()
	{
		var code = new byte[32];
		BinaryPrimitives.WriteUInt32BigEndian(code, 0x4E75_4E75u);
		code[^1] = 1;

		var image = HunkWriter.Write(
			code,
			[],
			[],
			new Dictionary<string, int>(),
			4,
			new HashSet<string>(),
			new HunkOutputOptions { IncludeSymbols = false });
		var words = EnumerateLongWords(image).ToArray();

		Assert.Equal(1u, words[2]);
		Assert.DoesNotContain(HunkBss, words);
	}

	[Fact]
	public void KeepsSingleCodeHunkWhenPcRelativeReferenceTargetsCandidateBss()
	{
		var code = new byte[32];
		BinaryPrimitives.WriteUInt32BigEndian(code, 0x4E75_4E75u);

		var image = HunkWriter.Write(
			code,
			[],
			[],
			new Dictionary<string, int> { ["bss:value"] = 4 },
			4,
			new HashSet<string> { "bss:value" },
			new HunkOutputOptions { IncludeSymbols = false });
		var words = EnumerateLongWords(image).ToArray();

		Assert.Equal(1u, words[2]);
		Assert.DoesNotContain(HunkBss, words);
	}

	private static IEnumerable<uint> EnumerateLongWords(byte[] bytes)
	{
		for (var offset = 0; offset + 3 < bytes.Length; offset += 4)
		{
			yield return BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
		}
	}
}
