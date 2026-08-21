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
	public CrossModuleConstrainedValueSource(int value) => Value = value;

	public readonly int GetValue() => Value;
}

public static class CrossModuleConstrainedInterfaceFixture
{
	public static int Entry()
	{
		// The explicit value-type constructor makes this layout reachable by the
		// dispatch-table scan. Constrained calls still require no runtime interface
		// map, including when the interface is declared in another assembly.
		var source = new CrossModuleConstrainedValueSource(42);
		return Read(ref source);
	}

	private static int Read<T>(ref T source)
		where T : struct, IExternalValueSource => source.GetValue();
}
