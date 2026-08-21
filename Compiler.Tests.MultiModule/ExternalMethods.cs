using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests.MultiModule;

public enum ExternalListState
{
	First = 19,
	Second = 23,
	Missing = 42
}

public interface IExternalValueSource
{
	int GetValue();
}

public static class ExternalValueTypes
{
	[StructLayout(LayoutKind.Sequential, Pack = 2)]
	public struct NestedRectangle
	{
		public short MinX;
		public short MinY;
		public short MaxX;
		public short MaxY;
	}
}

public static class ExternalMethods
{
	private static int _lastResult;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int AddAndDouble(int left, int right)
	{
		_lastResult = (left + right) * 2;
		return _lastResult;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int AddOne<T>(int value) where T : struct => value + 1;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static uint ReadGuestUInt32(APTR address, int offset) =>
		APTR.ReadUInt32(address, offset);
}
