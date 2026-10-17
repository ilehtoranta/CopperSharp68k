using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests.MultiModule;

public struct ExternalStructReceiverRootProbe
{
	public int Value;
	public int Other;
	[MethodImpl(MethodImplOptions.NoInlining)]
	public int ReadAfterCall() { CallBoundary(Other); return Value; }
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CallBoundary(int value) { }
}
