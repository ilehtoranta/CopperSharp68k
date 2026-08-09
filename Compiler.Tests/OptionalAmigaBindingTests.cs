using System.Reflection;
using System.Runtime.CompilerServices;
using Amiga;
using Amiga.MUI;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class OptionalAmigaBindingTests
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallAmiSSLMaster() => AmiSSLMaster.InitAmiSSLMaster(1, 0);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallXadMaster() => unchecked((int)XadMaster.XadGetSystemInfo());

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallXpkMaster() => XpkMaster.XpkQuery(0);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallCAMD() => CAMD.Rethink();

	[Theory]
	[InlineData(typeof(AmiSSLMaster), "amisslmaster.library", "_AmiSSLMasterLibraryBase")]
	[InlineData(typeof(XadMaster), "xadmaster.library", "_XadMasterLibraryBase")]
	[InlineData(typeof(XpkMaster), "xpkmaster.library", "_XpkMasterLibraryBase")]
	[InlineData(typeof(CAMD), "camd.library", "_CAMDLibraryBase")]
	[InlineData(typeof(TimerDevice), "timer.device", "_TimerDeviceLibraryBase")]
	public void OptionalLibrariesAreManual(Type libraryType, string name, string baseSymbol)
	{
		var attribute = libraryType.GetCustomAttribute<AmigaLibraryAttribute>();

		Assert.NotNull(attribute);
		Assert.Equal(name, attribute.Name);
		Assert.Equal(AmigaLibraryBasePolicy.Manual, attribute.BasePolicy);
		Assert.Equal(baseSymbol, AmigaLibraryBaseSymbols.For(name));
	}

	[Theory]
	[InlineData(typeof(AmiSSLMaster), nameof(AmiSSLMaster.OpenAmiSSLTagList), -60)]
	[InlineData(typeof(XadMaster), nameof(XadMaster.XadGetSystemInfo), -186)]
	[InlineData(typeof(XpkMaster), nameof(XpkMaster.XpkPassRequest), -114)]
	[InlineData(typeof(CAMD), nameof(CAMD.Midi2Driver), -240)]
	[InlineData(typeof(TimerDevice), nameof(TimerDevice.ReadEClock), -60)]
	public void OptionalLibraryVectorsUseExpectedOffsets(Type libraryType, string methodName, int offset)
	{
		var method = libraryType.GetMethod(methodName);

		Assert.NotNull(method);
		Assert.Equal(offset, method.GetCustomAttribute<AmigaLvoAttribute>()?.Offset);
	}

	[Theory]
	[InlineData(nameof(CallAmiSSLMaster), "amisslmaster.library", "_AmiSSLMasterLibraryBase", "-30(a6)")]
	[InlineData(nameof(CallXadMaster), "xadmaster.library", "_XadMasterLibraryBase", "-186(a6)")]
	[InlineData(nameof(CallXpkMaster), "xpkmaster.library", "_XpkMasterLibraryBase", "-84(a6)")]
	[InlineData(nameof(CallCAMD), "camd.library", "_CAMDLibraryBase", "-216(a6)")]
	public void OptionalLibraryCallsUseManualBaseSlots(
		string methodName,
		string libraryName,
		string baseSymbol,
		string vector)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(OptionalAmigaBindingTests).FullName}::{methodName}",
			OutputFormat = M68kOutputFormat.Assembly
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				[libraryName] = 0x0000_4200
			}
		});

		Assert.Contains(baseSymbol, result.Text, StringComparison.Ordinal);
		Assert.Contains(vector, result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ThirdPartyMUIClassesExposeCanonicalNamesAndTags()
	{
		Assert.Equal("TextEditor.mcc", TextEditor.Name);
		Assert.Equal(0xad000002u, TextEditor.Contents);
		Assert.Equal(0xad000024u, TextEditor.Method.ClearText);

		Assert.Equal("TheBar.mcc", TheBar.Name);
		Assert.Equal(0xf76b0251u, TheBar.RemoveSpacers);
		Assert.Equal(0xf76b0231u, TheBar.Method.GetObject);

		Assert.Equal("BetterString.mcc", BetterString.Name);
		Assert.Equal(0xad00100eu, BetterString.NoNotify);
		Assert.Equal(0xad00100bu, BetterString.Method.DoAction);
		Assert.Equal(0xfffffffeu, BetterString.Value.InsertEndOfString);
	}
}
