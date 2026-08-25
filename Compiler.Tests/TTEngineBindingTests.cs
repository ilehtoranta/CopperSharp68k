/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class TTEngineBindingTests
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static APTR CallOpenFont() => TTEngine.TT_OpenFontA(0x0000_4300u);

	[Fact]
	public void TTEngineIsExplicitlyManual()
	{
		var attribute = typeof(TTEngine).GetCustomAttribute<AmigaLibraryAttribute>();

		Assert.NotNull(attribute);
		Assert.Equal(TTEngine.Name, attribute.Name);
		Assert.Equal(AmigaLibraryBasePolicy.Manual, attribute.BasePolicy);
		Assert.Equal("_TTEngineLibraryBase", AmigaLibraryBaseSymbols.For(TTEngine.Name));
	}

	public static IEnumerable<object[]> PublicVectors =>
	[
		Vector(nameof(TTEngine.TT_OpenFontA), -30, [M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(TTEngine.TT_SetFont), -36, [M68kRegister.A1, M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(TTEngine.TT_CloseFont), -42, [M68kRegister.A0]),
		Vector(nameof(TTEngine.TT_Text), -48, [M68kRegister.A1, M68kRegister.A0, M68kRegister.D0]),
		Vector(nameof(TTEngine.TT_SetAttrsA), -54, [M68kRegister.A1, M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(TTEngine.TT_GetAttrsA), -60, [M68kRegister.A1, M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(TTEngine.TT_TextLength), -66, [M68kRegister.A1, M68kRegister.A0, M68kRegister.D0], M68kRegister.D0),
		Vector(nameof(TTEngine.TT_TextExtent), -72, [M68kRegister.A1, M68kRegister.A0, M68kRegister.D0, M68kRegister.A2]),
		Vector(nameof(TTEngine.TT_TextFit), -78, [M68kRegister.A1, M68kRegister.A0, M68kRegister.D0, M68kRegister.A2, M68kRegister.A3, M68kRegister.D1, M68kRegister.D2, M68kRegister.D3], M68kRegister.D0),
		Vector(nameof(TTEngine.TT_GetPixmapA), -84, [M68kRegister.A1, M68kRegister.A2, M68kRegister.D0, M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(TTEngine.TT_FreePixmap), -90, [M68kRegister.A0]),
		Vector(nameof(TTEngine.TT_DoneRastPort), -96, [M68kRegister.A1]),
		Vector(nameof(TTEngine.TT_AllocRequest), -102, [], M68kRegister.D0),
		Vector(nameof(TTEngine.TT_RequestA), -108, [M68kRegister.A0, M68kRegister.A1], M68kRegister.D0),
		Vector(nameof(TTEngine.TT_FreeRequest), -114, [M68kRegister.A0]),
		Vector(nameof(TTEngine.TT_ObtainFamilyListA), -120, [M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(TTEngine.TT_FreeFamilyList), -126, [M68kRegister.A0]),
		Vector(nameof(TTEngine.TT_CharPositions), -132, [M68kRegister.A1, M68kRegister.A0, M68kRegister.D0, M68kRegister.A2], M68kRegister.D0),
	];

	[Theory]
	[MemberData(nameof(PublicVectors))]
	public void TTEngineVectorsUsePublishedM68kAbi(string methodName, int lvo,
		M68kRegister[] parameters, M68kRegister? result)
	{
		var method = typeof(TTEngine).GetMethod(methodName,
			BindingFlags.Public | BindingFlags.Static)!;

		Assert.Equal(lvo, method.GetCustomAttribute<AmigaLvoAttribute>()?.Offset);
		Assert.Equal(parameters, method.GetParameters().Select(parameter =>
			parameter.GetCustomAttribute<M68kRegisterAttribute>()!.Register));
		Assert.Equal(result,
			method.ReturnParameter.GetCustomAttribute<M68kRegisterAttribute>()?.Register);
	}

	[Fact]
	public void TTEngineCallUsesTheManualBaseSlot()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(TTEngineBindingTests).FullName}::CallOpenFont",
			OutputFormat = M68kOutputFormat.Assembly
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				[TTEngine.Name] = 0x0000_4200
			}
		});

		Assert.Contains("_TTEngineLibraryBase", result.Text, StringComparison.Ordinal);
		Assert.Contains("-30(a6)", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void TTPixmapAndConstantsMatchThePublicAbi()
	{
		Assert.Equal(16, Marshal.SizeOf<TTPixmap>());
		Assert.Equal(0, Marshal.OffsetOf<TTPixmap>(nameof(TTPixmap.StructureSize)).ToInt32());
		Assert.Equal(4, Marshal.OffsetOf<TTPixmap>(nameof(TTPixmap.Width)).ToInt32());
		Assert.Equal(8, Marshal.OffsetOf<TTPixmap>(nameof(TTPixmap.Height)).ToInt32());
		Assert.Equal(12, Marshal.OffsetOf<TTPixmap>(nameof(TTPixmap.Data)).ToInt32());
		Assert.Equal(10, TTEngine.MinimumVersion);
		Assert.Equal(0x6EDA000Fu, (uint)TTEngineTag.Antialias);
		Assert.Equal(0x6EDA2013u, (uint)TTEngineRequesterTag.FixedWidthOnly);
		Assert.Equal(-1, (int)TTEngineEncoding.SystemUtf8);
	}

	private static object[] Vector(string methodName, int lvo,
		M68kRegister[] parameters, M68kRegister? result = null) =>
		[methodName, lvo, parameters, result!];
}
