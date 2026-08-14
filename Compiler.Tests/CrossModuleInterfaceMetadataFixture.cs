using CopperSharp.Compiler.Tests.MultiModule;

namespace CopperSharp.Compiler.Tests;

public sealed class CrossModuleInterfaceMetadataFixture : IExternalValueSource
{
	public static int Entry() => 0;

	public int GetValue() => 42;
}

public interface IInheritedExternalValueSource : IExternalValueSource;

public struct CrossModuleConstrainedValueSource : IInheritedExternalValueSource
{
	public int Value;

	public readonly int GetValue() => Value;
}

public static class CrossModuleConstrainedInterfaceFixture
{
	public static int Entry()
	{
		var source = new CrossModuleConstrainedValueSource { Value = 42 };
		return Read(ref source);
	}

	private static int Read<T>(ref T source)
		where T : struct, IExternalValueSource => source.GetValue();
}
