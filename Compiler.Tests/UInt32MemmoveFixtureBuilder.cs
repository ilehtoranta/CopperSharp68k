/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

internal static class UInt32MemmoveFixtureBuilder
{
	public static string Create(string directory)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("UInt32MemmoveProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("UInt32MemmoveProbe");
		var type = module.DefineType("UInt32MemmoveProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var entry = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator();
		var array = il.DeclareLocal(typeof(uint[]));
		var memmove = typeof(Buffer).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
			.Single(method => method.Name == "Memmove" && method.IsGenericMethodDefinition).MakeGenericMethod(typeof(uint));
		uint[] initial = [0xAABBCCDD, 0x89ABCDEF, 0x01234567, 0xFEDCBA98, 0x13579BDF, 0x2468ACE0, 0x76543210, 0xDDCCBBAA];
		(int Destination, int Source, int Count)[] cases = [(2, 1, 0), (2, 2, 4), (2, 1, 4), (1, 2, 4), (4, 1, 2)];
		for (var scenario = 0; scenario < cases.Length; scenario++)
		{
			il.Emit(OpCodes.Ldc_I4, initial.Length);
			il.Emit(OpCodes.Newarr, typeof(uint));
			il.Emit(OpCodes.Stloc, array);
			for (var index = 0; index < initial.Length; index++)
			{
				il.Emit(OpCodes.Ldloc, array);
				il.Emit(OpCodes.Ldc_I4, index);
				il.Emit(OpCodes.Ldc_I4, unchecked((int)initial[index]));
				il.Emit(OpCodes.Stelem_I4);
			}
			var (destination, source, count) = cases[scenario];
			il.Emit(OpCodes.Ldloc, array);
			il.Emit(OpCodes.Ldc_I4, destination);
			il.Emit(OpCodes.Ldelema, typeof(uint));
			il.Emit(OpCodes.Ldloc, array);
			il.Emit(OpCodes.Ldc_I4, source);
			il.Emit(OpCodes.Ldelema, typeof(uint));
			il.Emit(OpCodes.Ldc_I4, count);
			il.Emit(OpCodes.Conv_U);
			il.Emit(OpCodes.Call, memmove);
			il.Emit(OpCodes.Call, typeof(GC).GetMethod(nameof(GC.Collect), Type.EmptyTypes)!);
			var expected = (uint[])initial.Clone();
			Array.Copy(initial, source, expected, destination, count);
			for (var index = 0; index < expected.Length; index++)
			{
				var valid = il.DefineLabel();
				il.Emit(OpCodes.Ldloc, array);
				il.Emit(OpCodes.Ldc_I4, index);
				il.Emit(OpCodes.Ldelem_U4);
				il.Emit(OpCodes.Ldc_I4, unchecked((int)expected[index]));
				il.Emit(OpCodes.Beq, valid);
				il.Emit(OpCodes.Ldc_I4, 100 + scenario * expected.Length + index);
				il.Emit(OpCodes.Ret);
				il.MarkLabel(valid);
			}
		}
		il.Emit(OpCodes.Ldc_I4, 42);
		il.Emit(OpCodes.Ret);
		type.CreateType();
		var path = Path.Combine(directory, "UInt32MemmoveProbe.dll");
		assembly.Save(path);
		return path;
	}
}
