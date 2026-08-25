/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>Library-owned grayscale pixmap returned by <see cref="TTEngine.TT_GetPixmapA"/>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TTPixmap
{
	public const uint Size = 16;

	public uint StructureSize;
	public uint Width;
	public uint Height;
	public APTR Data;
}

public static class TTEngineLayout
{
	public static class Pixmap
	{
		public const int Size = 16;
		public const int StructureSize = 0;
		public const int Width = 4;
		public const int Height = 8;
		public const int Data = 12;
	}
}
