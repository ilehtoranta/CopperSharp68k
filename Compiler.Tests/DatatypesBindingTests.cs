/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Runtime.CompilerServices;
using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class DatatypesBindingTests
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static APTR CallNewDTObject() =>
		Datatypes.NewDTObjectA(0x0000_4300u, 0x0000_4400u);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallDrawDTObject() =>
		Datatypes.DrawDTObjectA(0x0000_4300u, 0x0000_4400u,
			10, 20, 320, 200, 0, 0, 0x0000_4500u);

	[Fact]
	public void PointerParametersAndResultsUsePointerTypes()
	{
		Assert.Equal(typeof(APTR), Method(nameof(Datatypes.ObtainDataTypeA))
			.ReturnType);
		Assert.Equal(new[] { typeof(uint), typeof(APTR), typeof(APTR) },
			Method(nameof(Datatypes.ObtainDataTypeA)).GetParameters()
				.Select(parameter => parameter.ParameterType));
		Assert.Equal(new[] { typeof(APTR), typeof(APTR), typeof(APTR), typeof(APTR) },
			Method(nameof(Datatypes.SetDTAttrsA)).GetParameters()
				.Select(parameter => parameter.ParameterType));
		Assert.Equal(typeof(STRPTR), Method(nameof(Datatypes.GetDTString)).ReturnType);
		Assert.Equal(new[] { typeof(APTR), typeof(STRPTR), typeof(APTR) },
			Method(nameof(Datatypes.LaunchToolA)).GetParameters()
				.Select(parameter => parameter.ParameterType));
		Assert.Equal(new[] { typeof(APTR), typeof(APTR), typeof(APTR), typeof(STRPTR),
			typeof(uint), typeof(int), typeof(APTR) },
			Method(nameof(Datatypes.SaveDTObjectA)).GetParameters()
				.Select(parameter => parameter.ParameterType));
	}

	[Theory]
	[InlineData(nameof(Datatypes.ObtainDataTypeA), -36)]
	[InlineData(nameof(Datatypes.NewDTObjectA), -48)]
	[InlineData(nameof(Datatypes.GetDTString), -138)]
	[InlineData(nameof(Datatypes.SaveDTObjectA), -294)]
	public void PointerTypedBindingsKeepTheirPublishedVectors(string methodName, int lvo)
	{
		Assert.Equal(lvo, Method(methodName)
			.GetCustomAttribute<AmigaLvoAttribute>()?.Offset);
		Assert.Equal(M68kRegister.D0, Method(methodName).ReturnParameter
			.GetCustomAttribute<M68kRegisterAttribute>()?.Register);
	}

	[Theory]
	[InlineData(nameof(CallNewDTObject), "-48(a6)")]
	[InlineData(nameof(CallDrawDTObject), "-126(a6)")]
	public void PointerTypedDatatypesCallsLowerThroughTheLibraryBase(
		string methodName, string vector)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(DatatypesBindingTests).FullName}::{methodName}",
			OutputFormat = M68kOutputFormat.Assembly
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				[Datatypes.Name] = 0x0000_4200
			}
		});

		Assert.Contains(vector, result.Text, StringComparison.Ordinal);
	}

	private static MethodInfo Method(string name) => typeof(Datatypes).GetMethod(name,
		BindingFlags.Public | BindingFlags.Static)!;
}
