namespace CopperSharp.Compiler.Tests.PackageDependency;

public static class PortableDependency
{
	public static int SupportedAnswer() => 42;

	public static int UnsupportedAnswer() =>
		string.Concat("a", "b", "c", "d").Length;
}
