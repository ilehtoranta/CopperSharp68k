using System.Reflection;
using System.Runtime.CompilerServices;
using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class BsdSocketBindingTests
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallSocket() => BsdSocket.Socket(2, 1, 0);

	[Fact]
	public void BsdSocketIsExplicitlyManual()
	{
		var attribute = typeof(BsdSocket).GetCustomAttribute<AmigaLibraryAttribute>();

		Assert.NotNull(attribute);
		Assert.Equal("bsdsocket.library", attribute.Name);
		Assert.Equal(AmigaLibraryBasePolicy.Manual, attribute.BasePolicy);
		Assert.Equal("_BsdSocketLibraryBase", AmigaLibraryBaseSymbols.For(BsdSocket.Name));
	}

	[Theory]
	[InlineData(nameof(BsdSocket.Socket), -30)]
	[InlineData(nameof(BsdSocket.Bind), -36)]
	[InlineData(nameof(BsdSocket.CloseSocket), -120)]
	[InlineData(nameof(BsdSocket.SocketBaseTagList), -294)]
	[InlineData(nameof(BsdSocket.ObtainServerSocket), -696)]
	public void BsdSocketVectorsUseExpectedOffsets(string methodName, int offset)
	{
		var method = typeof(BsdSocket).GetMethod(methodName);

		Assert.NotNull(method);
		Assert.Equal(offset, method.GetCustomAttribute<AmigaLvoAttribute>()?.Offset);
	}

	[Fact]
	public void BsdSocketCallsUseTheManualBaseSlot()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(BsdSocketBindingTests).FullName}::CallSocket",
			OutputFormat = M68kOutputFormat.Assembly
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				[BsdSocket.Name] = 0x0000_4200
			}
		});

		Assert.Contains("_BsdSocketLibraryBase", result.Text, StringComparison.Ordinal);
		Assert.Contains("-30(a6)", result.Text, StringComparison.Ordinal);
	}
}
