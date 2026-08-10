/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using System.Text;

namespace CopperSharp.Compiler.Output;

internal static class HunkWriter
{
	private const uint HunkCode = 0x0000_03E9;
	private const uint HunkBss = 0x0000_03EB;
	private const uint HunkReloc32 = 0x0000_03EC;
	private const uint HunkSymbol = 0x0000_03F0;
	private const uint HunkEnd = 0x0000_03F2;
	private const uint HunkHeader = 0x0000_03F3;

	public static byte[] Write(
		ReadOnlySpan<byte> code,
		IReadOnlyList<M68kRelocation> relocations,
		IReadOnlyList<M68kSymbol> symbols,
		IReadOnlyDictionary<string, int> labels,
		int? bssStartOffset,
		IReadOnlySet<string> pcRelativeTargets,
		HunkOutputOptions options)
	{
		var singleHunk = WriteSingleCodeHunk(code, relocations, symbols, options);
		if (!TryWriteCodeAndBssHunks(
			code,
			relocations,
			symbols,
			labels,
			bssStartOffset,
			pcRelativeTargets,
			options,
			out var codeAndBss) ||
			codeAndBss.Length >= singleHunk.Length)
		{
			return singleHunk;
		}

		return codeAndBss;
	}

	private static byte[] WriteSingleCodeHunk(
		ReadOnlySpan<byte> code,
		IReadOnlyList<M68kRelocation> relocations,
		IReadOnlyList<M68kSymbol> symbols,
		HunkOutputOptions options)
	{
		var writer = new BigEndianWriter();
		var sizeInLongs = checked((uint)((code.Length + 3) / 4));

		writer.WriteUInt32(HunkHeader);
		writer.WriteUInt32(0); // Resident-library name terminator.
		writer.WriteUInt32(1); // Hunk table size.
		writer.WriteUInt32(0); // First hunk.
		writer.WriteUInt32(0); // Last hunk.
		writer.WriteUInt32(sizeInLongs);

		writer.WriteUInt32(HunkCode);
		writer.WriteUInt32(sizeInLongs);
		writer.WriteBytes(code);
		writer.PadToLong();

		if (relocations.Count != 0)
		{
			WriteRelocations(writer, [(0, relocations)]);
		}

		if (options.IncludeSymbols && symbols.Count != 0)
		{
			writer.WriteUInt32(HunkSymbol);
			foreach (var symbol in symbols.OrderBy(item => item.Address))
			{
				WriteSymbol(writer, symbol);
			}

			writer.WriteUInt32(0);
		}

		writer.WriteUInt32(HunkEnd);
		return writer.ToArray();
	}

	private static bool TryWriteCodeAndBssHunks(
		ReadOnlySpan<byte> code,
		IReadOnlyList<M68kRelocation> relocations,
		IReadOnlyList<M68kSymbol> symbols,
		IReadOnlyDictionary<string, int> labels,
		int? bssStartOffset,
		IReadOnlySet<string> pcRelativeTargets,
		HunkOutputOptions options,
		out byte[] result)
	{
		result = [];
		if (bssStartOffset is not { } bssStart ||
			bssStart <= 0 ||
			bssStart >= code.Length ||
			(bssStart & 3) != 0 ||
			((code.Length - bssStart) & 3) != 0 ||
			code[bssStart..].ContainsAnyExcept((byte)0) ||
			symbols.Any(symbol => symbol.Address >= (uint)bssStart) ||
			pcRelativeTargets.Any(target =>
				!labels.TryGetValue(target, out var targetOffset) ||
				targetOffset >= bssStart))
		{
			return false;
		}

		var initializedCode = code[..bssStart].ToArray();
		var codeRelocations = new List<M68kRelocation>();
		var bssRelocations = new List<M68kRelocation>();
		foreach (var relocation in relocations)
		{
			if (relocation.Offset < 0 ||
				relocation.Offset + sizeof(uint) > initializedCode.Length ||
				!labels.TryGetValue(relocation.Target, out var targetOffset) ||
				targetOffset < 0 ||
				targetOffset >= code.Length)
			{
				return false;
			}

			if (targetOffset < bssStart)
			{
				codeRelocations.Add(relocation);
				continue;
			}

			var addend = BinaryPrimitives.ReadUInt32BigEndian(
				initializedCode.AsSpan(relocation.Offset, sizeof(uint)));
			if (addend != (uint)targetOffset)
			{
				return false;
			}
			BinaryPrimitives.WriteUInt32BigEndian(
				initializedCode.AsSpan(relocation.Offset, sizeof(uint)),
				addend - (uint)bssStart);
			bssRelocations.Add(relocation);
		}

		var codeSizeInLongs = checked((uint)(initializedCode.Length / 4));
		var bssSizeInLongs = checked((uint)((code.Length - bssStart) / 4));
		var writer = new BigEndianWriter();
		writer.WriteUInt32(HunkHeader);
		writer.WriteUInt32(0); // Resident-library name terminator.
		writer.WriteUInt32(2); // Hunk table size.
		writer.WriteUInt32(0); // First hunk.
		writer.WriteUInt32(1); // Last hunk.
		writer.WriteUInt32(codeSizeInLongs);
		writer.WriteUInt32(bssSizeInLongs);

		writer.WriteUInt32(HunkCode);
		writer.WriteUInt32(codeSizeInLongs);
		writer.WriteBytes(initializedCode);
		if (codeRelocations.Count != 0 || bssRelocations.Count != 0)
		{
			var groups = new List<(uint TargetHunk, IReadOnlyList<M68kRelocation> Relocations)>(2);
			if (codeRelocations.Count != 0)
			{
				groups.Add((0, codeRelocations));
			}
			if (bssRelocations.Count != 0)
			{
				groups.Add((1, bssRelocations));
			}
			WriteRelocations(writer, groups);
		}
		if (options.IncludeSymbols && symbols.Count != 0)
		{
			writer.WriteUInt32(HunkSymbol);
			foreach (var symbol in symbols.OrderBy(item => item.Address))
			{
				WriteSymbol(writer, symbol);
			}
			writer.WriteUInt32(0);
		}
		writer.WriteUInt32(HunkEnd);

		writer.WriteUInt32(HunkBss);
		writer.WriteUInt32(bssSizeInLongs);
		writer.WriteUInt32(HunkEnd);
		result = writer.ToArray();
		return true;
	}

	private static void WriteRelocations(
		BigEndianWriter writer,
		IReadOnlyList<(uint TargetHunk, IReadOnlyList<M68kRelocation> Relocations)> groups)
	{
		writer.WriteUInt32(HunkReloc32);
		foreach (var group in groups)
		{
			writer.WriteUInt32(checked((uint)group.Relocations.Count));
			writer.WriteUInt32(group.TargetHunk);
			foreach (var relocation in group.Relocations.OrderBy(item => item.Offset))
			{
				writer.WriteUInt32(checked((uint)relocation.Offset));
			}
		}
		writer.WriteUInt32(0);
	}

	private static void WriteSymbol(BigEndianWriter writer, M68kSymbol symbol)
	{
		var name = Encoding.ASCII.GetBytes(symbol.Name);
		var wordLength = checked((uint)((name.Length + 3) / 4));
		writer.WriteUInt32(wordLength);
		writer.WriteBytes(name);
		for (var index = name.Length; index < checked(wordLength * 4); index++)
		{
			writer.WriteByte(0);
		}

		writer.WriteUInt32(symbol.Address);
	}
}
