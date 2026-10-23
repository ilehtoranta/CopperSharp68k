/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class Base64CodecFixtures
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Entry()
	{
		byte[] source = [0, 1, 2, 254, 255, 64, 128];
		var encoded = Convert.ToBase64String(source);
		if (encoded != "AAEC/v9AgA==") return 1;
		if (Convert.ToBase64String(source, 1, 3) != "AQL+") return 2;
		var decoded = Convert.FromBase64String(" \r\n" + encoded + "\t");
		if (decoded.Length != source.Length) return 3;
		for (var i = 0; i < source.Length; i++) if (source[i] != decoded[i]) return 4;
		var bytes = new byte[24];
		if (Base64.EncodeToUtf8(source, bytes, out var consumed, out var written) != OperationStatus.Done ||
			consumed != source.Length || written != 12) return 5;
		if (!Base64.IsValid(((ReadOnlySpan<byte>)bytes).Slice(0, written), out var length) || length != source.Length) return 6;
		var destination = new byte[16];
		if (Base64.DecodeFromUtf8(((ReadOnlySpan<byte>)bytes).Slice(0, written), destination, out consumed, out length) != OperationStatus.Done ||
			consumed != written || length != source.Length) return 7;
		for (var i = 0; i < source.Length; i++) if (source[i] != destination[i]) return 8;
		var chars = new char[14];
		if (!Convert.TryToBase64Chars(source, chars, out var charsWritten) || charsWritten != 12) return 9;
		if (!Convert.TryFromBase64Chars(((ReadOnlySpan<char>)chars).Slice(0, charsWritten), destination, out length) || length != 7) return 10;
		var inPlace = new byte[12];
		for (var i = 0; i < source.Length; i++) inPlace[i] = source[i];
		if (Base64.EncodeToUtf8InPlace(inPlace, source.Length, out written) != OperationStatus.Done || written != 12) return 11;
		var inPlaceStatus = Base64.DecodeFromUtf8InPlace(inPlace, out written);
		if (inPlaceStatus != OperationStatus.Done) return 1200 + (int)inPlaceStatus;
		if (written != 7) return 1300 + written;
		for (var i = 0; i < source.Length; i++) if (inPlace[i] != source[i]) return 13;
		var line = new byte[58];
		for (var i = 0; i < line.Length; i++) line[i] = (byte)i;
		var text = Convert.ToBase64String(line, Base64FormattingOptions.InsertLineBreaks);
		if (text.Length != 82 || text[76] != '\r' || text[77] != '\n') return 14;
		if (Convert.FromBase64String(text).Length != line.Length) return 15;
		if (Base64.GetMaxEncodedToUtf8Length(7) != 12 || Base64.GetMaxDecodedFromUtf8Length(12) != 9) return 16;
		if (Convert.ToBase64String(source, Base64FormattingOptions.None) != encoded ||
			Convert.ToBase64String(source, 0, source.Length, Base64FormattingOptions.None) != encoded ||
			Convert.ToBase64String((ReadOnlySpan<byte>)source, Base64FormattingOptions.None) != encoded) return 17;
		var charArray = new char[16];
		if (Convert.ToBase64CharArray(source, 0, source.Length, charArray, 1) != 12 ||
			Convert.ToBase64CharArray(source, 0, source.Length, charArray, 1, Base64FormattingOptions.None) != 12) return 18;
		if (Convert.FromBase64CharArray(charArray, 1, 12).Length != 7 ||
			!Convert.TryFromBase64String(encoded, destination, out length) || length != 7) return 19;
		if (!Base64.IsValid(((ReadOnlySpan<byte>)bytes).Slice(0, 12)) ||
			!Base64.IsValid(encoded.AsSpan()) || !Base64.IsValid(encoded.AsSpan(), out length) || length != 7) return 20;
		var badOptions = false;
		try { Convert.ToBase64String(source, (Base64FormattingOptions)2); }
		catch (ArgumentException error) { badOptions = error.ParamName == "options"; }
		if (!badOptions) return 21;
		var nullInput = false;
		try { Convert.ToBase64String((byte[])null!); }
		catch (ArgumentNullException error) { nullInput = error.ParamName == "inArray"; }
		if (!nullInput) return 22;
		var nullText = false;
		try { Convert.FromBase64String((string)null!); }
		catch (ArgumentNullException error) { nullText = error.ParamName == "s"; }
		if (!nullText) return 23;
		var badText = false;
		try { Convert.FromBase64String("A!AA"); }
		catch (FormatException) { badText = true; }
		if (!badText) return 24;
		var badLength = false;
		try { Convert.ToBase64String(source, 0, -1); }
		catch (ArgumentOutOfRangeException error) { badLength = error.ParamName == "length"; }
		if (!badLength) return 25;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint GeneratedEntry()
	{
		uint state = 0x64b12001;
		uint hash = 2166136261;
		for (var size = 0; size <= 75; size++)
		{
			var bytes = new byte[size];
			for (var i = 0; i < size; i++)
			{
				state = unchecked(state * 1664525 + 1013904223);
				bytes[i] = (byte)(state >> 24);
			}
			var option = size % 2 == 0 ? Base64FormattingOptions.None : Base64FormattingOptions.InsertLineBreaks;
			var text = Convert.ToBase64String(bytes, option);
			var parsed = Convert.FromBase64String(text);
			if (parsed.Length != bytes.Length) return 0xbad00001;
			for (var i = 0; i < bytes.Length; i++)
			{
				if (parsed[i] != bytes[i]) return 0xbad00002;
				hash = unchecked((hash ^ parsed[i]) * 16777619);
			}
			for (var i = 0; i < text.Length; i++) hash = unchecked((hash ^ text[i]) * 16777619);
			var encoded = new byte[Base64.GetMaxEncodedToUtf8Length(size)];
			if (Base64.EncodeToUtf8(bytes, encoded, out var consumed, out var written) != OperationStatus.Done ||
				consumed != size) return 0xbad00003;
			var decoded = new byte[size + 3];
			if (Base64.DecodeFromUtf8(((ReadOnlySpan<byte>)encoded).Slice(0, written), decoded, out consumed, out var decodedLength) != OperationStatus.Done ||
				decodedLength != size || consumed != written) return 0xbad00004;
			hash = unchecked((hash ^ (uint)written) * 16777619);
			var inPlace = new byte[encoded.Length + 2];
			for (var i = 0; i < inPlace.Length; i++) inPlace[i] = 0xa5;
			for (var i = 0; i < size; i++) inPlace[1 + i] = bytes[i];
			var inplaceStatus = Base64.EncodeToUtf8InPlace(((Span<byte>)inPlace).Slice(1, encoded.Length), size, out var inPlaceWritten);
			if (inplaceStatus != OperationStatus.Done || inPlaceWritten != encoded.Length || inPlace[0] != 0xa5 || inPlace[inPlace.Length - 1] != 0xa5)
				return 0xbad00006;
			hash = Mix(Mix(hash, (uint)inplaceStatus), (uint)inPlaceWritten);
			for (var i = 0; i < encoded.Length; i++)
			{
				if (inPlace[1 + i] != encoded[i]) return 0xbad00007;
				hash = Mix(hash, inPlace[1 + i]);
			}
			inplaceStatus = Base64.DecodeFromUtf8InPlace(((Span<byte>)inPlace).Slice(1, encoded.Length), out inPlaceWritten);
			if (inplaceStatus != OperationStatus.Done || inPlaceWritten != size || inPlace[0] != 0xa5 || inPlace[inPlace.Length - 1] != 0xa5)
				return 0xbad00008;
			hash = Mix(Mix(hash, (uint)inplaceStatus), (uint)inPlaceWritten);
			for (var i = 0; i < size; i++)
			{
				if (inPlace[1 + i] != bytes[i]) return 0xbad00009;
				hash = Mix(hash, inPlace[1 + i]);
			}
			var guard = new char[text.Length + 2];
			guard[0] = '#';
			guard[guard.Length - 1] = '#';
			if (!Convert.TryToBase64Chars(bytes, ((Span<char>)guard).Slice(1, text.Length), out var charCount, option) || charCount != text.Length ||
				guard[0] != '#' || guard[guard.Length - 1] != '#') return 0xbad00005;
		}
		return hash;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AdversarialEntry()
	{
		string[] corpus = ["", " ", "A", "AAAA", "AA==", "AAA=", "////", "A A A A", "AA== ",
			"AA==AA==", "====", "A===", "AAA", "AAAAA", "A?AA", "A\vAA", "A\0AA",
			"AA\r\n==", "AA= =", "A=AA", "AB==", "ABC=", "AA==x", "TWFu", "TWE=", "TQ=="];
		uint hash = 2166136261;
		for (var sample = 0; sample < corpus.Length; sample++)
		{
			var text = corpus[sample];
			var input = new byte[text.Length];
			for (var i = 0; i < text.Length; i++) input[i] = (byte)text[i];
			for (var flag = 0; flag < 2; flag++)
				for (var capacity = 0; capacity < 7; capacity++)
				{
					var target = new byte[capacity + 2];
					for (var i = 0; i < target.Length; i++) target[i] = 0xa5;
					var status = Base64.DecodeFromUtf8(input, ((Span<byte>)target).Slice(1, capacity),
						out var consumed, out var written, flag == 1);
					hash = Mix(hash, (uint)status);
					hash = Mix(hash, (uint)consumed);
					hash = Mix(hash, (uint)written);
					for (var i = 0; i < target.Length; i++) hash = Mix(hash, target[i]);
				}
			var inPlace = new byte[input.Length];
			for (var i = 0; i < input.Length; i++) inPlace[i] = input[i];
			var inPlaceStatus = Base64.DecodeFromUtf8InPlace(inPlace, out var inPlaceWritten);
			hash = Mix(hash, (uint)inPlaceStatus);
			hash = Mix(hash, (uint)inPlaceWritten);
			for (var i = 0; i < inPlace.Length; i++) hash = Mix(hash, inPlace[i]);
			var isValid = Base64.IsValid(input, out var decodedLength);
			hash = Mix(hash, isValid ? 1u : 0u);
			hash = Mix(hash, (uint)decodedLength);
		}
		return hash;
	}

	private static uint Mix(uint current, uint value) => unchecked((current ^ value) * 16777619);
}
