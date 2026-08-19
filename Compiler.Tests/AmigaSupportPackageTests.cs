using Amiga;
using Amiga.MUI;

namespace CopperSharp.Compiler.Tests;

public sealed class AmigaSupportPackageTests
{
	[Fact]
	public void GuestMemoryHelpersAreSeparatedFromTheApplicationSdk()
	{
		const string supportAssembly = "CopperSharp.Sdk.Amiga.Support";

		Assert.Equal(supportAssembly, typeof(IAmigaGuestMemory).Assembly.GetName().Name);
		Assert.Equal(supportAssembly, typeof(CStringCodec).Assembly.GetName().Name);
		Assert.Equal(supportAssembly, typeof(BOOPSIGuestCodec).Assembly.GetName().Name);
		Assert.Equal(supportAssembly, typeof(DosDateStampCodec).Assembly.GetName().Name);
		Assert.Equal(supportAssembly, typeof(LayersLayerCodec).Assembly.GetName().Name);
		Assert.Equal(supportAssembly, typeof(MUI_MinMaxCodec).Assembly.GetName().Name);
		Assert.Equal(supportAssembly, typeof(UtilityTagItemCodec).Assembly.GetName().Name);

		Assert.Equal("CopperSharp.Sdk.Amiga", typeof(APTR).Assembly.GetName().Name);
		Assert.Equal("CopperSharp.Sdk.Amiga", typeof(MUI_MinMax).Assembly.GetName().Name);
	}
}
