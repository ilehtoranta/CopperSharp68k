/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>
/// Owns a retained NUL-terminated Amiga Latin-1 string. Keep this object alive
/// for as long as native code may retain <see cref="Value"/>, then dispose it.
/// </summary>
public sealed class CStringStorage : IDisposable
{
	private uint _pointer;
	private uint _byteSize;

	public CStringStorage(string value)
	{
		_pointer = CStringEncoding.AllocateAndWrite(value, out _byteSize);
	}

	public CString Value => CString.FromPointer(_pointer);

	public uint ByteSize => _byteSize;

	public void Dispose()
	{
		if (_pointer == 0)
		{
			return;
		}

		Exec.FreeMem(_pointer, _byteSize);
		_pointer = 0;
		_byteSize = 0;
	}
}
