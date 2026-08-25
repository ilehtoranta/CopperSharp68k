/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static class AHILayout
{
	public static class AudioCtrl
	{
		public const int UserData = 0;
	}

	public static class SoundMessage
	{
		public const int Channel = 0;
	}

	public static class RecordMessage
	{
		public const int Type = 0;
		public const int Buffer = 4;
		public const int Length = 8;
	}

	public static class SampleInfo
	{
		public const int Type = 0;
		public const int Address = 4;
		public const int Length = 8;
	}

	public static class AudioModeRequester
	{
		public const int AudioId = 0;
		public const int MixFrequency = 4;
		public const int LeftEdge = 8;
		public const int TopEdge = 10;
		public const int Width = 12;
		public const int Height = 14;
		public const int InfoOpened = 16;
		public const int InfoLeftEdge = 20;
		public const int InfoTopEdge = 22;
		public const int InfoWidth = 24;
		public const int InfoHeight = 26;
		public const int UserData = 28;
	}
}
