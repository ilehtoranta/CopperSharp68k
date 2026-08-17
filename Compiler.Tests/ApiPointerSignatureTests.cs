using System.Reflection;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class ApiPointerSignatureTests
{
	[Fact]
	public void CoreLibraryPointerSlotsUseSemanticAbiTypes()
	{
		Assert.Equal(typeof(APTR), typeof(Exec).GetMethod(nameof(Exec.AllocMem))!.ReturnType);
		Assert.Equal(typeof(APTR), Parameter(typeof(Exec), nameof(Exec.FreeMem), 0));
		Assert.Equal(typeof(APTR), Parameter(typeof(DOS), nameof(DOS.Read), 1));
		Assert.Equal(typeof(APTR), Parameter(typeof(DOS), nameof(DOS.Examine64), 1));
		Assert.Equal(typeof(APTR), Parameter(typeof(DOS), nameof(DOS.Examine64), 2));
		Assert.Equal(typeof(APTR), Parameter(typeof(Expansion), nameof(Expansion.ConfigChain), 0));
		Assert.Equal(typeof(APTR), Parameter(typeof(BOOPSI), nameof(BOOPSI.DoMethodA), 0));
		Assert.Equal(typeof(APTR), Parameter(typeof(BOOPSI), nameof(BOOPSI.DoMethodA), 1));
	}

	[Fact]
	public void MorphOsPointerSlotsRetainFourByteGuestWidth()
	{
		Assert.Equal(4, System.Runtime.InteropServices.Marshal.SizeOf<WSTRPTR>());
		Assert.Equal(4, System.Runtime.InteropServices.Marshal.SizeOf<CONST_WSTRPTR>());
		Assert.Equal(typeof(APTR), typeof(Exec).GetMethod(nameof(Exec.NewCreateLibrary))!.ReturnType);
		Assert.Equal(typeof(APTR), typeof(Exec).GetMethod(nameof(Exec.AllocVecDMA))!.ReturnType);
		Assert.Equal(typeof(APTR), Parameter(typeof(Exec), nameof(Exec.NewGetTaskAttrsA), 0));
		Assert.Equal(typeof(APTR), Parameter(typeof(Exec), nameof(Exec.NewGetTaskAttrsA), 1));
		Assert.Equal(typeof(APTR), Parameter(typeof(Exec), nameof(Exec.NewGetTaskAttrsA), 4));
		Assert.Equal(4, System.Runtime.InteropServices.Marshal.SizeOf<APTR>());
	}

	[Fact]
	public void Ucs4KeymapCallsUseWideStringPointerTypes()
	{
		Assert.Equal(typeof(WSTRPTR), Parameter(typeof(Keymap), nameof(Keymap.MapRawKeyUCS4), 1));
		Assert.Equal(typeof(CONST_WSTRPTR), Parameter(typeof(Keymap), nameof(Keymap.MapUCS4), 0));
		Assert.Equal(typeof(STRPTR), Parameter(typeof(Keymap), nameof(Keymap.MapUCS4), 2));
		Assert.Equal(typeof(int), Parameter(typeof(Keymap), nameof(Keymap.ToANSI), 0));
	}

	[Fact]
	public void ScalarResultsRemainFixedWidthValues()
	{
		Assert.Equal(typeof(uint), typeof(Exec).GetMethod(nameof(Exec.MakeFunctions))!.ReturnType);
		Assert.Equal(typeof(uint), typeof(BOOPSI).GetMethod(nameof(BOOPSI.DoMethodA))!.ReturnType);
		Assert.Equal(typeof(uint), typeof(Expansion).GetMethod(nameof(Expansion.FindConfigDev))!.ReturnType);
	}

	private static Type Parameter(Type owner, string methodName, int index) =>
		owner.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Single(method => method.Name == methodName &&
				method.GetParameters().Length > index)
			.GetParameters()[index].ParameterType;
}
