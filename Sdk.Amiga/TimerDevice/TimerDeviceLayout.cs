/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static class TimerDeviceLayout
{
	public static class TimeVal
	{
		public const int Seconds = 0;
		public const int Microseconds = 4;
	}

	public static class EClockVal
	{
		public const int High = 0;
		public const int Low = 4;
	}

	public static class TimerRequest
	{
		public const int Request = 0;
		public const int Time = 32;
		public const int Seconds = Time + TimeVal.Seconds;
		public const int Microseconds = Time + TimeVal.Microseconds;
	}
}
