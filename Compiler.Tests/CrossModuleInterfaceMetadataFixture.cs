using CopperSharp.Compiler.Tests.MultiModule;

namespace CopperSharp.Compiler.Tests;

public sealed class CrossModuleInterfaceMetadataFixture : IExternalValueSource
{
	public static int Entry() => 0;

	public int GetValue() => 42;
}
