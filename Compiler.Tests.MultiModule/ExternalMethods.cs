using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests.MultiModule;

public static class ExternalMethods
{
	private static int _lastResult;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int AddAndDouble(int left, int right)
	{
		_lastResult = (left + right) * 2;
		return _lastResult;
	}
}
