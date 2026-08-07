/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;

namespace Amiga;

internal static class CStringEncoding
{
	public static uint GetAllocationSize(string value)
	{
		if (value is null)
		{
			M68kRuntime.ThrowArgumentNullException();
		}

		for (var index = 0; index < value.Length; index++)
		{
			var character = value[index];
			if (character == '\0' || character > '\u00ff')
			{
				M68kRuntime.ThrowArgumentException();
			}
		}

		var byteSize = (uint)value.Length + 1u;
		return (byteSize + 3u) & ~3u;
	}

	public static uint AllocateAndWrite(string value, out uint byteSize)
	{
		byteSize = GetAllocationSize(value);
		var pointer = Exec.AllocMem(byteSize, Exec.MemoryFlags.Public);
		if (pointer == 0)
		{
			M68kRuntime.ThrowOutOfMemoryException();
		}

		Write(value, pointer, byteSize);
		return pointer;
	}

	private static void Write(string value, uint pointer, uint byteSize)
	{
		var address = APTR.FromPointer(pointer);
		var characterIndex = 0;
		for (var offset = 0; (uint)offset < byteSize; offset += 4)
		{
			uint packed = 0;
			for (var shift = 24; shift >= 0; shift -= 8)
			{
				if (characterIndex < value.Length)
				{
					packed |= (uint)value[characterIndex++] << shift;
				}
			}

			APTR.WriteUInt32(address, offset, packed);
		}
	}
}
