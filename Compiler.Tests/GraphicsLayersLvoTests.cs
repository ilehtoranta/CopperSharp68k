using System.Reflection;
using Amiga;
using CopperSharp.Sdk.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class GraphicsLayersLvoTests
{
	public static IEnumerable<object[]> Vectors()
	{
		foreach (var field in typeof(GraphicsLayersLvo).GetFields(
			BindingFlags.Public | BindingFlags.Static))
			yield return new object[] { field.Name, (short)field.GetRawConstantValue()! };
	}

	[Theory]
	[MemberData(nameof(Vectors))]
	public void EveryLayersGraphicsConstantMatchesTheSdkDeclaration(string name,
		short expected)
	{
		var method = typeof(Graphics).GetMethod(name, BindingFlags.Public |
			BindingFlags.Static);
		Assert.NotNull(method);
		Assert.Equal(expected,
			method!.GetCustomAttribute<AmigaLvoAttribute>()?.Offset);
	}

	[Fact]
	public void SurfaceContainsTwentyNineRasterAndFiveCompanionVectors()
	{
		var fields = typeof(GraphicsLayersLvo).GetFields(BindingFlags.Public |
			BindingFlags.Static);
		Assert.Equal(34, fields.Length);
		Assert.Equal(34, fields.Select(field => (short)field.GetRawConstantValue()!)
			.Distinct().Count());
	}

	[Fact]
	public void BitMapAttributeSelectorsMatchGraphicsV39Abi()
	{
		Assert.Equal(0u, (uint)BitMapAttribute.Height);
		Assert.Equal(4u, (uint)BitMapAttribute.Depth);
		Assert.Equal(8u, (uint)BitMapAttribute.Width);
		Assert.Equal(12u, (uint)BitMapAttribute.Flags);
		Assert.Equal(0x08, (byte)BitMapFlags.Standard);
	}
}
