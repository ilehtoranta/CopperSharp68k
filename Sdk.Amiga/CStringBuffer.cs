/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>
/// Owns a temporary NUL-terminated Amiga Latin-1 string. The byref-like owner
/// cannot escape to the managed heap; dispose it after the native call returns.
/// </summary>
public ref struct CStringBuffer
{
	private uint _pointer;
	private uint _byteSize;

	public CStringBuffer(string value)
	{
		_pointer = CStringEncoding.AllocateAndWrite(value, out _byteSize);
	}

	public readonly CString Value => CString.FromPointer(_pointer);

	public readonly uint ByteSize => _byteSize;

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
