/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Compile-time byte offsets for guest-resident Graphics structures.</summary>
public static class GraphicsLayout
{
	/// <summary>
	/// Classic graphics.library RastPort facts needed to follow a Layer's
	/// public RastPort and BitMap pointers without duplicating guest offsets.
	/// </summary>
	public static class RastPort
	{
		public const int Size = 100;
		public const int Layer = 0;
		public const int BitMap = 4;
		public const int AreaPattern = 8;
		public const int TemporaryRaster = 12;
		public const int AreaInfo = 16;
		public const int GelsInfo = 20;
		public const int Mask = 24;
		public const int ForegroundPen = 25;
		public const int BackgroundPen = 26;
		public const int OutlinePen = 27;
		public const int DrawMode = 28;
		public const int AreaPatternSize = 29;
		public const int LinePatternCount = 30;
		public const int Flags = 32;
		public const int LinePattern = 34;
		public const int CurrentX = 36;
		public const int CurrentY = 38;
		public const int Minterm0 = 40;
		public const int PenWidth = 48;
		public const int PenHeight = 50;
		public const int Font = 52;
		public const int AlgorithmicStyle = 56;
		public const int TextFlags = 57;
		public const int TextHeight = 58;
		public const int TextWidth = 60;
		public const int TextBaseline = 62;
		public const int TextSpacing = 64;
	}

	/// <summary>Classic graphics.library BitMap public layout.</summary>
	public static class BitMap
	{
		public const int Size = 40;
		public const int BytesPerRow = 0;
		public const int Rows = 2;
		public const int Flags = 4;
		public const int Depth = 5;
		public const int Plane0 = 8;
		public const int Plane1 = 12;
		public const int Plane2 = 16;
		public const int Plane3 = 20;
		public const int Plane4 = 24;
		public const int Plane5 = 28;
		public const int Plane6 = 32;
		public const int Plane7 = 36;
	}

	public static class Rectangle
	{
		public const int Size = 8;
		public const int MinX = 0;
		public const int MinY = 2;
		public const int MaxX = 4;
		public const int MaxY = 6;
	}

	public static class RegionRectangle
	{
		public const int Size = 16;
		public const int Successor = 0;
		public const int Predecessor = 4;
		public const int Bounds = 8;
	}

	public static class Region
	{
		public const int Size = 12;
		public const int Bounds = 0;
		public const int RegionRectangle = 8;
	}
}
