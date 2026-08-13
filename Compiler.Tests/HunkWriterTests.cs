/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using CopperSharp.Compiler.Backend;
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

	[Fact]
	public void AbsoluteFarMethodCallRelocatesEquallyAtDistinctHunkLoadBases()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("method:callee");
		assembler.EmitWord(0x4E75); // RTS
		while (assembler.Offset < 32_768)
		{
			assembler.EmitWord(0x4E71); // Unreached padding.
		}
		assembler.Mark("method:entry");
		assembler.EmitCall("method:callee");
		assembler.EmitWord(0x4E75); // RTS
		var linked = assembler.Link(0, new Dictionary<string, uint>());
		var image = HunkWriter.Write(
			linked.Bytes,
			linked.Relocations,
			[],
			linked.Labels,
			linked.BssStartOffset,
			linked.PcRelativeTargets,
			new HunkOutputOptions { IncludeSymbols = false });
		var first = LoadSingleCodeHunk(image, 0x0001_0000);
		var second = LoadSingleCodeHunk(image, 0x0004_0000);
		var entry = linked.Labels["method:entry"];

		Assert.Equal(0x4EB9,
			BinaryPrimitives.ReadUInt16BigEndian(first.AsSpan(entry)));
		Assert.Equal(0x4EB9,
			BinaryPrimitives.ReadUInt16BigEndian(second.AsSpan(entry)));
		Assert.Equal((uint)linked.Labels["method:callee"],
			BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(entry + 2)) -
			0x0001_0000u);
		Assert.Equal((uint)linked.Labels["method:callee"],
			BinaryPrimitives.ReadUInt32BigEndian(second.AsSpan(entry + 2)) -
			0x0004_0000u);
		BinaryPrimitives.WriteUInt32BigEndian(first.AsSpan(entry + 2), 0);
		BinaryPrimitives.WriteUInt32BigEndian(second.AsSpan(entry + 2), 0);
		Assert.Equal(first, second);
	}

	private static byte[] LoadSingleCodeHunk(byte[] image, uint loadBase)
	{
		var offset = 0;
		uint ReadLong()
		{
			var value = BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(offset));
			offset += 4;
			return value;
		}

		Assert.Equal(0x0000_03F3u, ReadLong()); // HUNK_HEADER
		Assert.Equal(0u, ReadLong()); // No resident name.
		Assert.Equal(1u, ReadLong());
		Assert.Equal(0u, ReadLong());
		Assert.Equal(0u, ReadLong());
		_ = ReadLong(); // Allocation size.
		Assert.Equal(0x0000_03E9u, ReadLong()); // HUNK_CODE
		var code = image.AsSpan(offset + 4,
			checked((int)ReadLong() * 4)).ToArray();
		offset += code.Length;
		Assert.Equal(0x0000_03ECu, ReadLong()); // HUNK_RELOC32
		while (true)
		{
			var count = ReadLong();
			if (count == 0) break;
			Assert.Equal(0u, ReadLong());
			for (var index = 0u; index < count; index++)
			{
				var relocation = checked((int)ReadLong());
				var value = BinaryPrimitives.ReadUInt32BigEndian(
					code.AsSpan(relocation));
				BinaryPrimitives.WriteUInt32BigEndian(
					code.AsSpan(relocation),
					checked(value + loadBase));
			}
		}
		Assert.Equal(0x0000_03F2u, ReadLong()); // HUNK_END
		return code;
	}

	private static IEnumerable<uint> EnumerateLongWords(byte[] bytes)
	{
		for (var offset = 0; offset + 3 < bytes.Length; offset += 4)
		{
			yield return BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
		}
	}
}
