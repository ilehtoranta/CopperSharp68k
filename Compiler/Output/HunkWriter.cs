/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text;

namespace CopperSharp.Compiler.Output;

internal static class HunkWriter
{
	private const uint HunkCode = 0x0000_03E9;
	private const uint HunkReloc32 = 0x0000_03EC;
	private const uint HunkSymbol = 0x0000_03F0;
	private const uint HunkEnd = 0x0000_03F2;
	private const uint HunkHeader = 0x0000_03F3;

	public static byte[] Write(
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
			writer.WriteUInt32(HunkReloc32);
			writer.WriteUInt32(checked((uint)relocations.Count));
			writer.WriteUInt32(0); // Every compiled method is in code hunk zero.
			foreach (var relocation in relocations.OrderBy(item => item.Offset))
			{
				writer.WriteUInt32(checked((uint)relocation.Offset));
			}

			writer.WriteUInt32(0);
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
