/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static unsafe int CoreLibStringBuilderPointerAppendEntry()
	{
		char* buffer = stackalloc char[70];
		for (var index = 0; index < 70; index++) buffer[index] = "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[index % 7];
		for (var size = 0; size <= 65; size++)
		for (var offset = 0; offset <= 3; offset++)
		for (var capacityCase = 0; capacityCase < 3; capacityCase++)
		{
			var builder = new System.Text.StringBuilder(capacityCase == 0 ? 1 : capacityCase == 1 ? 4 : 128);
			builder.Append('!');
			if (builder.Append(buffer + offset, size) != builder || builder.Length != size + 1 || builder[0] != '!') return 1;
			System.GC.Collect();
			var snapshot = builder.ToString();
			for (var index = 0; index < size; index++)
				if (snapshot[index + 1] != "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[(index + offset) % 7]) return 2;
		}
		var validation = new System.Text.StringBuilder(1, 4);
		if (validation.Append((char*)null, 0) != validation || validation.Length != 0) return 3;
		try { validation.Append((char*)null, -1); return 4; }
		catch (ArgumentOutOfRangeException exception) { if (exception.ParamName != "valueCount") return 5; }
		try { validation.Append(buffer, 5); return 6; }
		catch (ArgumentOutOfRangeException) { }
		if (validation.Length != 0) return 7;
		validation.Append(buffer, 4);
		buffer[0] = 'Z';
		System.GC.Collect();
		return validation.ToString() == "A\0\u03A9\uD83D" ? 42 : 8;
	}

	public static unsafe int CoreLibPointerInputAllocationFailureEntry()
	{
		char* buffer = stackalloc char[6];
		buffer[0] = 'A'; buffer[1] = '\0'; buffer[2] = '\u03A9';
		buffer[3] = 'B'; buffer[4] = 'C'; buffer[5] = '\0';
		_ = new string((char*)null); // Initialize the singleton before arming the allocator.
		SetStringBuilderAllocationFailure(1);
		if (new string((char*)null) != string.Empty || new string(buffer + 1) != string.Empty) { SetStringBuilderAllocationFailure(0); return 1; }
		SetStringBuilderAllocationFailure(0);
		SetStringBuilderAllocationFailure(1);
		try { _ = new string(buffer); SetStringBuilderAllocationFailure(0); return 2; }
		catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		if (buffer[0] != 'A' || new string(buffer) != "A") return 3;
		for (var failAt = 1; failAt <= 2; failAt++)
		{
			var builder = new System.Text.StringBuilder(4);
			builder.Append("pre");
			SetStringBuilderAllocationFailure(failAt);
			try { builder.Append(buffer, 5); SetStringBuilderAllocationFailure(0); return 4; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (builder.ToString() != "preA" || buffer[0] != 'A' || buffer[1] != '\0' || buffer[2] != '\u03A9') return 5;
			builder.Clear(); builder.Append(buffer, 5);
			if (builder.ToString() != "A\0\u03A9BC") return 6;
		}
		return 42;
	}

	public static unsafe int CoreLibPointerStringConstructionEntry()
	{
		if (new string((char*)null) != string.Empty) return 1;
		char* buffer = stackalloc char[7];
		buffer[0] = '\0';
		if (new string(buffer) != string.Empty) return 2;
		buffer[0] = 'A'; buffer[1] = '\u03A9'; buffer[2] = '\uD83D';
		buffer[3] = '\uDE00'; buffer[4] = '\uD800'; buffer[5] = '\0'; buffer[6] = 'Z';
		var snapshot = new string(buffer);
		var interior = new string(buffer + 1);
		if (snapshot != "A\u03A9\uD83D\uDE00\uD800" || interior != "\u03A9\uD83D\uDE00\uD800") return 3;
		buffer[0] = 'X'; buffer[1] = '\0';
		System.GC.Collect();
		if (snapshot.Length != 5 || snapshot[0] != 'A' || interior[0] != '\u03A9') return 4;
		var builder = new System.Text.StringBuilder(1);
		builder.Append(snapshot); builder.Append(interior);
		if (builder.ToString() != "A\u03A9\uD83D\uDE00\uD800\u03A9\uD83D\uDE00\uD800") return 5;
		char* matrix = stackalloc char[70];
		for (var length = 0; length <= 65; length++)
		for (var offset = 0; offset <= 3; offset++)
		{
			for (var index = 0; index < 70; index++) matrix[index] = "A\u03A9\uD83D\uDE00\uD800\uFFFF"[index % 6];
			matrix[offset + length] = '\0';
			var text = new string(matrix + offset);
			matrix[offset] = 'z';
			System.GC.Collect();
			if (text.Length != length || length == 0 && !ReferenceEquals(text, string.Empty)) return 6;
			for (var index = 0; index < length; index++)
				if (text[index] != "A\u03A9\uD83D\uDE00\uD800\uFFFF"[(index + offset) % 6]) return 7;
		}
		var source = new char[4]; source[0] = 'x'; source[1] = '\u03A9'; source[2] = 'y'; source[3] = '\0';
		fixed (char* pinned = source)
		{
			var text = new string(pinned + 1);
			source[1] = 'z'; System.GC.Collect();
			if (text != "\u03A9y") return 8;
		}
		System.GC.KeepAlive(source);
		return 42;
	}
}
