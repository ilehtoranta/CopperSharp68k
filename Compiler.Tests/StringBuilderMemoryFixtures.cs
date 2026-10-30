/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private sealed class BuilderCharacterMemoryManager : MemoryManager<char>
	{
		private readonly char[] _buffer;
		public int Reads;
		public bool Fail;
		public int VisibleLength;
		public BuilderCharacterMemoryManager(int length)
		{
			_buffer = new char[length]; VisibleLength = length;
			for (var index = 0; index < length; index++) _buffer[index] = "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[index % 7];
		}
		public ReadOnlyMemory<char> View(int start, int length) => CreateMemory(start, length);
		public override Span<char> GetSpan()
		{
			Reads++; System.GC.Collect();
			if (Fail) throw new FormatException("memory callback");
			return new Span<char>(_buffer, 0, VisibleLength);
		}
		public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
		public override void Unpin() { }
		protected override void Dispose(bool disposing) { }
	}

	public static int CoreLibStringBuilderArrayMemoryAppendEntry() => BuilderMemoryMatrix(false);
	public static int CoreLibStringBuilderStringMemoryAppendEntry() => BuilderMemoryMatrix(true);

	private static int BuilderMemoryMatrix(bool strings)
	{
		for (var length = 0; length <= 33; length++)
		for (var start = 0; start <= 3; start++)
		for (var capacityCase = 0; capacityCase < 3; capacityCase++)
		{
			var memory = CreateBuilderMemory(strings, length + start + 2).Slice(start).Slice(0, length);
			System.GC.Collect(); var pressure = new char[80]; pressure[0] = 'z';
			var builder = new StringBuilder(capacityCase == 0 ? 1 : capacityCase == 1 ? 4 : 80);
			builder.Append('!');
			if (builder.Append(memory) != builder || builder.Length != length + 1) return 1;
			memory = default; System.GC.Collect();
			var snapshot = builder.ToString();
			if (snapshot[0] != '!') return 2;
			for (var index = 0; index < length; index++)
				if (snapshot[index + 1] != "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[(start + index) % 7]) return 3;
			builder.Clear(); builder.Append("tail"); System.GC.Collect();
			if (builder.ToString() != "tail" || snapshot.Length != length + 1) return 4;
			System.GC.KeepAlive(pressure);
		}
		var empty = new StringBuilder(1);
		if (empty.Append(default(ReadOnlyMemory<char>)) != empty || empty.Append(CreateBuilderMemory(strings, 3).Slice(3, 0)) != empty || empty.Length != 0) return 5;
		var array = new char[3]; array[0] = 'x'; array[1] = '\u03A9'; array[2] = '\0';
		empty.Append(new ReadOnlyMemory<char>(array)); array[0] = 'y'; System.GC.Collect();
		return empty.ToString() == "x\u03A9\0" ? 42 : 6;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlyMemory<char> CreateBuilderMemory(bool strings, int length)
	{
		var array = new char[length];
		for (var index = 0; index < length; index++) array[index] = "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[index % 7];
		return strings ? new string(new ReadOnlySpan<char>(array)).AsMemory() : new ReadOnlyMemory<char>(array);
	}

	public static int CoreLibStringBuilderManagerMemoryAppendEntry()
	{
		var manager = new BuilderCharacterMemoryManager(18);
		var view = manager.View(1, 13).Slice(2, 9);
		var builder = new StringBuilder(1); builder.Append('!');
		System.GC.Collect();
		if (builder.Append(view) != builder || manager.Reads != 1 || builder.Length != 10) return 1;
		for (var index = 0; index < 9; index++) if (builder[index + 1] != "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[(index + 3) % 7]) return 2;
		var snapshot = builder.ToString();
		manager.Fail = true;
		try { builder.Append(view); return 3; }
		catch (FormatException exception) { if (exception.Message != "memory callback") return 4; }
		if (manager.Reads != 2 || builder.ToString() != snapshot) return 5;
		manager.Fail = false; manager.VisibleLength = 5;
		try { builder.Append(view); return 6; }
		catch (ArgumentOutOfRangeException) { }
		if (manager.Reads != 3 || builder.ToString() != snapshot) return 7;
		manager.VisibleLength = 18;
		builder.Clear(); builder.Append(view);
		if (manager.Reads != 4 || builder.Length != 9) return 8;
		var retained = CreateManagerOnlyMemory();
		System.GC.Collect(); var pressure = new char[80]; pressure[0] = 'z';
		builder.Clear(); builder.Append(retained); retained = default;
		System.GC.Collect(); System.GC.KeepAlive(pressure);
		return builder.ToString() == "\0\u03A9\uD83D\uDE00\uD800\uFFFFA" ? 42 : 9;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlyMemory<char> CreateManagerOnlyMemory() => new BuilderCharacterMemoryManager(12).View(1, 7);

	public static int CoreLibStringBuilderMemoryAllocationFailureEntry()
	{
		for (var kind = 0; kind < 3; kind++)
		{
			var memory = kind == 2 ? new BuilderCharacterMemoryManager(12).View(0, 12) : CreateBuilderMemory(kind == 1, 12);
			var roomy = new StringBuilder(16);
			SetStringBuilderAllocationFailure(1);
			roomy.Append(default(ReadOnlyMemory<char>)); roomy.Append(memory.Slice(12, 0)); roomy.Append(memory.Slice(0, 2));
			SetStringBuilderAllocationFailure(0);
			if (roomy.Length != 2 || roomy[0] != 'A' || roomy[1] != '\0') return 1;
			for (var failAt = 1; failAt <= 2; failAt++)
			{
				var builder = new StringBuilder(4); builder.Append("pre");
				SetStringBuilderAllocationFailure(failAt);
				try { builder.Append(memory); SetStringBuilderAllocationFailure(0); return 2; }
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
				if (builder.ToString() != "preA" || memory.Length != 12) return 3;
				builder.Clear(); builder.Append(memory);
				if (builder.Length != 12) return 4;
				for (var index = 0; index < 12; index++)
					if (builder[index] != "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[index % 7]) return 5;
			}
		}
		return 42;
	}
}
