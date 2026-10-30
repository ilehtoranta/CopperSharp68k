/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>
/// Layout-compatible TimeOnly operations for the bounded target time-of-day surface.
/// CoreLib stores a TimeOnly value as ticks since midnight.
/// </summary>
internal unsafe struct ShadowTimeOnly
{
	private const long TicksPerMicrosecond = 10;
	private const long TicksPerMillisecond = 10_000;
	private const long TicksPerSecond = 10_000_000;
	private const long TicksPerMinute = 600_000_000;
	private const long TicksPerHour = 36_000_000_000;
	private const long TicksPerDay = 864_000_000_000;
	private long _ticks;

	public int GetHour() => (int)(_ticks / TicksPerHour);
	public int GetMinute() => (int)(_ticks / TicksPerMinute % 60);
	public int GetSecond() => (int)(_ticks / TicksPerSecond % 60);
	public int GetMillisecond() => (int)(_ticks / TicksPerMillisecond % 1000);
	public int GetMicrosecond() => (int)(_ticks / TicksPerMicrosecond % 1000);
	public long GetTicks() => _ticks;

	public int CompareTo(TimeOnly other) => _ticks < ReadTicks(other) ? -1 : _ticks > ReadTicks(other) ? 1 : 0;
	public bool Equals(TimeOnly other) => _ticks == ReadTicks(other);
	public static bool AreEqual(TimeOnly left, TimeOnly right) => ReadTicks(left) == ReadTicks(right);
	public static bool AreNotEqual(TimeOnly left, TimeOnly right) => ReadTicks(left) != ReadTicks(right);
	public static bool IsGreaterThan(TimeOnly left, TimeOnly right) => ReadTicks(left) > ReadTicks(right);
	public static bool IsGreaterThanOrEqual(TimeOnly left, TimeOnly right) => ReadTicks(left) >= ReadTicks(right);
	public static bool IsLessThan(TimeOnly left, TimeOnly right) => ReadTicks(left) < ReadTicks(right);
	public static bool IsLessThanOrEqual(TimeOnly left, TimeOnly right) => ReadTicks(left) <= ReadTicks(right);

	public TimeOnly Add(TimeSpan value) => AddWithWrappedDays(value, out _);

	public TimeOnly AddWithWrappedDays(TimeSpan value, out int wrappedDays)
	{
		var spanTicks = value.Ticks;
		var days = spanTicks / TicksPerDay;
		var remainder = spanTicks % TicksPerDay;
		var resultTicks = _ticks + remainder;
		if (resultTicks >= TicksPerDay)
		{
			resultTicks -= TicksPerDay;
			days++;
		}
		else if (resultTicks < 0)
		{
			resultTicks += TicksPerDay;
			days--;
		}
		wrappedDays = (int)days;
		return new TimeOnly(resultTicks);
	}

	public string FormatRoundTrip(string? format)
	{
		if (format is not null && format != "O" && format != "o") M68kRuntime.ThrowFormatException();
		Span<char> result = stackalloc char[16];
		var ticks = _ticks;
		var hour = (int)(ticks / TicksPerHour);
		var minute = (int)(ticks / TicksPerMinute % 60);
		var second = (int)(ticks / TicksPerSecond % 60);
		var fraction = (int)(ticks % TicksPerSecond);
		WriteTwoDigits(result, 0, hour);
		result[2] = ':';
		WriteTwoDigits(result, 3, minute);
		result[5] = ':';
		WriteTwoDigits(result, 6, second);
		result[8] = '.';
		for (var index = 15; index >= 9; index--)
		{
			result[index] = (char)('0' + fraction % 10);
			fraction /= 10;
		}
		return ShadowStringData.FromCharacters(result);
	}

	public string FormatRoundTripWithProvider(string? format, IFormatProvider? provider)
	{
		_ = provider;
		return FormatRoundTrip(format);
	}

	public ShadowTimeOnly(int hour, int minute, int second, int millisecond, int microsecond)
	{
		if ((uint)hour > 23 || (uint)minute > 59 || (uint)second > 59 ||
			(uint)millisecond > 999 || (uint)microsecond > 999)
			M68kRuntime.ThrowArgumentOutOfRangeException();
		_ticks = hour * TicksPerHour + minute * TicksPerMinute + second * TicksPerSecond +
			millisecond * TicksPerMillisecond + microsecond * TicksPerMicrosecond;
	}

	public ShadowTimeOnly(int hour, int minute) : this(hour, minute, 0, 0, 0) { }
	public ShadowTimeOnly(int hour, int minute, int second) : this(hour, minute, second, 0, 0) { }
	public ShadowTimeOnly(int hour, int minute, int second, int millisecond) : this(hour, minute, second, millisecond, 0) { }

	public ShadowTimeOnly(long ticks)
	{
		if (ticks < 0 || ticks >= TicksPerDay) M68kRuntime.ThrowArgumentOutOfRangeException();
		_ticks = ticks;
	}

	private static long ReadTicks(TimeOnly value) => *(long*)&value;

	private static void WriteTwoDigits(Span<char> destination, int offset, int value)
	{
		destination[offset] = (char)('0' + value / 10);
		destination[offset + 1] = (char)('0' + value % 10);
	}
}
