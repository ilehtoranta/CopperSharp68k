/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Runtime;
using CopperSharp.Sdk.Amiga;

namespace CopperSharp.Runtime.AmigaPal;

/// <summary>
/// Private target implementation for the admitted portable <see cref="Console"/>
/// input/output slice. Applications continue to compile against System.Console.
/// </summary>
public static class ConsolePal
{
	private const uint MinimumDosVersion = 0;
	private const uint InputBufferSize = 128;
	private static uint _cachedDosBase;
	private static uint _cachedInput;
	private static bool _applicationLifetimeActive;
	private static uint _inputBuffer;
	private static int _inputOffset;
	private static int _inputCount;
	private static bool _inputEof;
	private static bool _hasPendingInput;
	private static int _pendingInput;

	/// <summary>Enables lazy application-lifetime ownership for the DOS base.</summary>
	public static void Initialize()
	{
		_applicationLifetimeActive = true;
	}

	/// <summary>Initializes application-lifetime input state.</summary>
	public static void InitializeInput()
	{
		_cachedInput = 0;
		_inputOffset = 0;
		_inputCount = 0;
		_inputEof = false;
		_hasPendingInput = false;
	}

	/// <summary>Releases a lazily acquired DOS base. Safe to call repeatedly.</summary>
	public static void Shutdown()
	{
		var dosBase = _cachedDosBase;
		_cachedDosBase = 0;
		_applicationLifetimeActive = false;
		if (dosBase != 0)
		{
			Exec.CloseLibrary(APTR.FromPointer(dosBase));
		}
	}

	/// <summary>Releases the lazily allocated application input buffer.</summary>
	public static void ShutdownInput()
	{
		var inputBuffer = _inputBuffer;
		_inputBuffer = 0;
		if (inputBuffer != 0)
		{
			Exec.FreeMem(inputBuffer, InputBufferSize);
		}

		_cachedInput = 0;
		_inputOffset = 0;
		_inputCount = 0;
		_inputEof = false;
		_hasPendingInput = false;
	}

	public static void Write(string? value)
	{
		if (value is null || value.Length == 0)
		{
			return;
		}

		var dosBase = _cachedDosBase;
		if (dosBase == 0)
		{
			dosBase = OpenDosLibrary(DOS.Name, MinimumDosVersion);
			if (dosBase == 0)
			{
				M68kRuntime.ThrowIOException();
			}
			if (_applicationLifetimeActive)
			{
				_cachedDosBase = dosBase;
			}
		}

		var previousDosBase = APTR.ToUInt32(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.FromPointer(dosBase);
		try
		{
			var output = BPTR.ToUInt32(DOS.Output());
			if (output == 0)
			{
				M68kRuntime.ThrowIOException();
			}
			RequireCompleteWrite(
				WriteManagedString(output, value),
				value.Length);
		}
		finally
		{
			DOS.DOSLibraryBase = APTR.FromPointer(previousDosBase);
			if (!_applicationLifetimeActive)
			{
				Exec.CloseLibrary(APTR.FromPointer(dosBase));
			}
		}
	}

	public static void Write(int value)
	{
		var length = ShadowIntegerFormatter.PackInt32(
			value,
			out var word0,
			out var word1,
			out var word2);
		WritePackedBytes(word0, word1, word2, length, appendNewLine: false);
	}

	public static void Write(uint value)
	{
		var length = ShadowIntegerFormatter.PackUInt32(
			value,
			out var word0,
			out var word1,
			out var word2);
		WritePackedBytes(word0, word1, word2, length, appendNewLine: false);
	}

	public static void Write(long value)
	{
		var low = M68kRuntime.SplitInt64(value, out var high);
		var length = ShadowIntegerFormatter.PackInt64(
			high,
			low,
			out var word0,
			out var word1,
			out var word2,
			out var word3,
			out var word4);
		WritePackedInt64Bytes(
			word0,
			word1,
			word2,
			word3,
			word4,
			length,
			appendNewLine: false);
	}

	public static void Write(ulong value)
	{
		var low = M68kRuntime.SplitUInt64(value, out var high);
		var length = ShadowIntegerFormatter.PackUInt64(
			high,
			low,
			out var word0,
			out var word1,
			out var word2,
			out var word3,
			out var word4);
		WritePackedInt64Bytes(
			word0,
			word1,
			word2,
			word3,
			word4,
			length,
			appendNewLine: false);
	}

	public static void Write(bool value) =>
		WriteBoolean(value, appendNewLine: false);

	public static void Write(char value) =>
		WriteCharacter(value, appendNewLine: false);

	public static void WriteLine(string? value)
	{
		var valueLength = value is null ? 0 : value.Length;
		var dosBase = _cachedDosBase;
		if (dosBase == 0)
		{
			dosBase = OpenDosLibrary(DOS.Name, MinimumDosVersion);
			if (dosBase == 0)
			{
				M68kRuntime.ThrowIOException();
			}
			if (_applicationLifetimeActive)
			{
				_cachedDosBase = dosBase;
			}
		}

		var previousDosBase = APTR.ToUInt32(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.FromPointer(dosBase);
		try
		{
			var output = BPTR.ToUInt32(DOS.Output());
			if (output == 0)
			{
				M68kRuntime.ThrowIOException();
			}

			if (valueLength != 0)
			{
				RequireCompleteWrite(
					WriteManagedString(output, value!),
					valueLength);
			}
			var newLine = CString.ToUInt32(CString.FromLiteral("\n"));
			RequireCompleteWrite(DOS.Write(BPTR.FromRaw(output), newLine, 1), 1);
		}
		finally
		{
			DOS.DOSLibraryBase = APTR.FromPointer(previousDosBase);
			if (!_applicationLifetimeActive)
			{
				Exec.CloseLibrary(APTR.FromPointer(dosBase));
			}
		}
	}

	public static void WriteLine(int value)
	{
		var length = ShadowIntegerFormatter.PackInt32(
			value,
			out var word0,
			out var word1,
			out var word2);
		WritePackedBytes(word0, word1, word2, length, appendNewLine: true);
	}

	public static void WriteLine(uint value)
	{
		var length = ShadowIntegerFormatter.PackUInt32(
			value,
			out var word0,
			out var word1,
			out var word2);
		WritePackedBytes(word0, word1, word2, length, appendNewLine: true);
	}

	public static void WriteLine(long value)
	{
		var low = M68kRuntime.SplitInt64(value, out var high);
		var length = ShadowIntegerFormatter.PackInt64(
			high,
			low,
			out var word0,
			out var word1,
			out var word2,
			out var word3,
			out var word4);
		WritePackedInt64Bytes(
			word0,
			word1,
			word2,
			word3,
			word4,
			length,
			appendNewLine: true);
	}

	public static void WriteLine(ulong value)
	{
		var low = M68kRuntime.SplitUInt64(value, out var high);
		var length = ShadowIntegerFormatter.PackUInt64(
			high,
			low,
			out var word0,
			out var word1,
			out var word2,
			out var word3,
			out var word4);
		WritePackedInt64Bytes(
			word0,
			word1,
			word2,
			word3,
			word4,
			length,
			appendNewLine: true);
	}

	public static void WriteLine(bool value) =>
		WriteBoolean(value, appendNewLine: true);

	public static void WriteLine(char value) =>
		WriteCharacter(value, appendNewLine: true);

	public static int Read()
	{
		if (_hasPendingInput)
		{
			_hasPendingInput = false;
			return _pendingInput;
		}
		if (_inputEof)
		{
			return -1;
		}

		var dosBase = _cachedDosBase;
		if (dosBase == 0)
		{
			dosBase = OpenDosLibrary(DOS.Name, MinimumDosVersion);
			if (dosBase == 0)
			{
				M68kRuntime.ThrowIOException();
			}
			if (_applicationLifetimeActive)
			{
				_cachedDosBase = dosBase;
			}
		}

		var previousDosBase = APTR.ToUInt32(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.FromPointer(dosBase);
		try
		{
			var input = _cachedInput;
			if (input == 0)
			{
				input = BPTR.ToUInt32(DOS.Input());
				if (input != 0)
				{
					if (_applicationLifetimeActive)
					{
						_cachedInput = input;
					}
				}
			}
			if (input == 0)
			{
				M68kRuntime.ThrowIOException();
			}
			if (_applicationLifetimeActive)
			{
				var pointer = _inputBuffer;
				if (pointer == 0)
				{
					pointer = Exec.AllocMem(
						InputBufferSize,
						Exec.MemoryFlags.Public);
					if (pointer == 0)
					{
						M68kRuntime.ThrowOutOfMemoryException();
					}
					_inputBuffer = pointer;
					_inputOffset = 0;
					_inputCount = 0;
				}

				if (_inputOffset >= _inputCount)
				{
					var actual = DOS.Read(
						BPTR.FromRaw(input),
						pointer,
						(int)InputBufferSize);
					// Preserve the native -1 sentinel as a direct switch branch in
					// the bounded CIL profile instead of materializing a relation.
					switch (actual)
					{
						case -1:
							M68kRuntime.ThrowIOException();
							break;
						case 0:
							_inputEof = true;
							return -1;
					}
					_inputOffset = 0;
					_inputCount = actual;
				}

				return ReadNativeByte(pointer, _inputOffset++);
			}

			var scopedPointer = Exec.AllocMem(4, Exec.MemoryFlags.Public);
			if (scopedPointer == 0)
			{
				M68kRuntime.ThrowOutOfMemoryException();
			}
			try
			{
				var actual = DOS.Read(BPTR.FromRaw(input), scopedPointer, 1);
				switch (actual)
				{
					case -1:
						M68kRuntime.ThrowIOException();
						break;
					case 0:
						return -1;
					default:
						return ReadNativeByte(scopedPointer, 0);
				}
				return -1;
			}
			finally
			{
				Exec.FreeMem(scopedPointer, 4);
			}
		}
		finally
		{
			DOS.DOSLibraryBase = APTR.FromPointer(previousDosBase);
			if (!_applicationLifetimeActive)
			{
				Exec.CloseLibrary(APTR.FromPointer(dosBase));
			}
		}
	}

	public static string? ReadLine()
	{
		char[]? buffer = null;
		var count = 0;
		while (true)
		{
			var value = Read();
			if (value < 0)
			{
				if (count == 0)
				{
					return null;
				}
				break;
			}
			if (value == '\n')
			{
				break;
			}
			if (value == '\r')
			{
				var next = Read();
				if (next >= 0 && next != '\n')
				{
					_pendingInput = next;
					_hasPendingInput = true;
				}
				break;
			}

			if (buffer is null)
			{
				buffer = new char[64];
			}
			else if (count == buffer.Length)
			{
				if (buffer.Length >= 0x3fff_ffff)
				{
					M68kRuntime.ThrowOutOfMemoryException();
				}
				var expanded = new char[buffer.Length * 2];
				for (var index = 0; index < count; index++)
				{
					expanded[index] = buffer[index];
				}
				buffer = expanded;
			}
			buffer[count++] = (char)value;
		}

		if (count == 0)
		{
			return "";
		}
		var result = M68kRuntime.AllocateString(count);
		for (var index = 0; index < count; index++)
		{
			M68kRuntime.SetStringChar(result, index, buffer![index]);
		}
		return result;
	}

	private static int ReadNativeByte(uint pointer, int offset)
	{
		var alignedOffset = offset & ~3;
		var packed = APTR.ReadUInt32(APTR.FromPointer(pointer), alignedOffset);
		var shift = (3 - (offset & 3)) * 8;
		return (int)((packed >> shift) & 0xffu);
	}

	private static void WriteBoolean(bool value, bool appendNewLine)
	{
		uint pointer;
		int length;
		if (value)
		{
			pointer = CString.ToUInt32(CString.FromLiteral("True\n"));
			length = appendNewLine ? 5 : 4;
		}
		else
		{
			pointer = CString.ToUInt32(CString.FromLiteral("False\n"));
			length = appendNewLine ? 6 : 5;
		}
		WriteStaticBytes(pointer, length);
	}

	private static void WriteCharacter(char value, bool appendNewLine)
	{
		var character = (uint)value & 0xffffu;
		var encoded = (character & 0xff00u) == 0 ? character : (uint)'?';
		var word0 = encoded << 24;
		var length = 1;
		if (appendNewLine)
		{
			word0 |= (uint)'\n' << 16;
			length = 2;
		}
		WritePackedBytes(word0, 0, 0, length, appendNewLine: false);
	}

	private static void WriteStaticBytes(uint pointer, int length)
	{
		var dosBase = _cachedDosBase;
		if (dosBase == 0)
		{
			dosBase = OpenDosLibrary(DOS.Name, MinimumDosVersion);
			if (dosBase == 0)
			{
				M68kRuntime.ThrowIOException();
			}
			if (_applicationLifetimeActive)
			{
				_cachedDosBase = dosBase;
			}
		}

		var previousDosBase = APTR.ToUInt32(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.FromPointer(dosBase);
		try
		{
			var output = BPTR.ToUInt32(DOS.Output());
			if (output == 0)
			{
				M68kRuntime.ThrowIOException();
			}
			RequireCompleteWrite(DOS.Write(BPTR.FromRaw(output), pointer, length), length);
		}
		finally
		{
			DOS.DOSLibraryBase = APTR.FromPointer(previousDosBase);
			if (!_applicationLifetimeActive)
			{
				Exec.CloseLibrary(APTR.FromPointer(dosBase));
			}
		}
	}

	private static void WritePackedBytes(
		uint word0,
		uint word1,
		uint word2,
		int length,
		bool appendNewLine)
	{
		var dosBase = _cachedDosBase;
		if (dosBase == 0)
		{
			dosBase = OpenDosLibrary(DOS.Name, MinimumDosVersion);
			if (dosBase == 0)
			{
				M68kRuntime.ThrowIOException();
			}
			if (_applicationLifetimeActive)
			{
				_cachedDosBase = dosBase;
			}
		}

		var previousDosBase = APTR.ToUInt32(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.FromPointer(dosBase);
		try
		{
			var output = BPTR.ToUInt32(DOS.Output());
			if (output == 0)
			{
				M68kRuntime.ThrowIOException();
			}
			RequireCompleteWrite(
				WritePackedBytes(output, word0, word1, word2, length),
				length);
			if (appendNewLine)
			{
				var newLine = CString.ToUInt32(CString.FromLiteral("\n"));
				RequireCompleteWrite(DOS.Write(BPTR.FromRaw(output), newLine, 1), 1);
			}
		}
		finally
		{
			DOS.DOSLibraryBase = APTR.FromPointer(previousDosBase);
			if (!_applicationLifetimeActive)
			{
				Exec.CloseLibrary(APTR.FromPointer(dosBase));
			}
		}
	}

	private static int WritePackedBytes(
		uint output,
		uint word0,
		uint word1,
		uint word2,
		int length)
	{
		ConsoleWriteBuffer buffer = default;
		var pointer = APTR.ToUInt32(AddressOf(ref buffer));
		APTR.WriteUInt32(APTR.FromPointer(pointer), 0, word0);
		if (length > 4)
		{
			APTR.WriteUInt32(APTR.FromPointer(pointer), 4, word1);
		}
		if (length > 8)
		{
			APTR.WriteUInt32(APTR.FromPointer(pointer), 8, word2);
		}
		return DOS.Write(BPTR.FromRaw(output), pointer, length);
	}

	private static void WritePackedInt64Bytes(
		uint word0,
		uint word1,
		uint word2,
		uint word3,
		uint word4,
		int length,
		bool appendNewLine)
	{
		var dosBase = _cachedDosBase;
		if (dosBase == 0)
		{
			dosBase = OpenDosLibrary(DOS.Name, MinimumDosVersion);
			if (dosBase == 0)
			{
				M68kRuntime.ThrowIOException();
			}
			if (_applicationLifetimeActive)
			{
				_cachedDosBase = dosBase;
			}
		}

		var previousDosBase = APTR.ToUInt32(DOS.DOSLibraryBase);
		DOS.DOSLibraryBase = APTR.FromPointer(dosBase);
		try
		{
			var output = BPTR.ToUInt32(DOS.Output());
			if (output == 0)
			{
				M68kRuntime.ThrowIOException();
			}
			RequireCompleteWrite(
				WritePackedInt64Bytes(
					output,
					word0,
					word1,
					word2,
					word3,
					word4,
					length),
				length);
			if (appendNewLine)
			{
				var newLine = CString.ToUInt32(CString.FromLiteral("\n"));
				RequireCompleteWrite(DOS.Write(BPTR.FromRaw(output), newLine, 1), 1);
			}
		}
		finally
		{
			DOS.DOSLibraryBase = APTR.FromPointer(previousDosBase);
			if (!_applicationLifetimeActive)
			{
				Exec.CloseLibrary(APTR.FromPointer(dosBase));
			}
		}
	}

	private static int WritePackedInt64Bytes(
		uint output,
		uint word0,
		uint word1,
		uint word2,
		uint word3,
		uint word4,
		int length)
	{
		ConsoleWriteBuffer buffer = default;
		var pointer = APTR.ToUInt32(AddressOf(ref buffer));
		APTR.WriteUInt32(APTR.FromPointer(pointer), 0, word0);
		if (length > 4) APTR.WriteUInt32(APTR.FromPointer(pointer), 4, word1);
		if (length > 8) APTR.WriteUInt32(APTR.FromPointer(pointer), 8, word2);
		if (length > 12) APTR.WriteUInt32(APTR.FromPointer(pointer), 12, word3);
		if (length > 16) APTR.WriteUInt32(APTR.FromPointer(pointer), 16, word4);
		return DOS.Write(BPTR.FromRaw(output), pointer, length);
	}

	private static int WriteManagedString(uint output, string value)
	{
		ConsoleWriteBuffer buffer = default;
		var pointer = APTR.ToUInt32(AddressOf(ref buffer));
		var characterIndex = 0;
		while (characterIndex < value.Length)
		{
			var remaining = value.Length - characterIndex;
			var chunkLength = remaining < 20 ? remaining : 20;
			var chunkIndex = 0;
			for (var offset = 0; offset < chunkLength; offset += 4)
			{
				uint packed = 0;
				for (var shift = 24; shift >= 0; shift -= 8)
				{
					if (chunkIndex < chunkLength)
					{
						var character = (uint)value[characterIndex++] & 0xffffu;
						chunkIndex++;
						var encoded = character & 0xffu;
						if ((character & 0xff00u) != 0)
						{
							encoded = '?';
						}
						packed |= encoded << shift;
					}
				}
				APTR.WriteUInt32(APTR.FromPointer(pointer), offset, packed);
			}
			var actual = DOS.Write(BPTR.FromRaw(output), pointer, chunkLength);
			if (actual != chunkLength)
			{
				return actual;
			}
		}
		return value.Length;
	}

	private static APTR AddressOf(ref ConsoleWriteBuffer buffer) =>
		throw new System.NotSupportedException(
			"ConsolePal.AddressOf is lowered by CopperSharp.");

	[System.Runtime.InteropServices.StructLayout(
		System.Runtime.InteropServices.LayoutKind.Sequential,
		Pack = 2)]
	private struct ConsoleWriteBuffer
	{
		public uint Word0;
		public uint Word1;
		public uint Word2;
		public uint Word3;
		public uint Word4;
	}

	private static void RequireCompleteWrite(int actual, int expected)
	{
		if (actual != expected)
		{
			M68kRuntime.ThrowIOException();
		}
	}

	[AmigaLibrary(Exec.Name, AmigaLibraryBasePolicy.ExecBase)]
	[AmigaLvo(-552)]
	[return: M68kRegister(M68kRegister.D0)]
	private static extern uint OpenDosLibrary(
		[M68kRegister(M68kRegister.A1)] CString name,
		[M68kRegister(M68kRegister.D0)] uint minimumVersion);
}
