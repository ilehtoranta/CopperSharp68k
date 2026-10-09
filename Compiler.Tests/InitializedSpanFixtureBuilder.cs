/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

internal static class InitializedSpanFixtureBuilder
{
	public static string CreateApplicationFieldHandle(string directory)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("ApplicationFieldHandleProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("ApplicationFieldHandleProbe");
		var type = module.DefineType("System.RuntimeFieldHandle", TypeAttributes.Public | TypeAttributes.Sealed, typeof(ValueType));
		type.DefineField("First", typeof(uint), FieldAttributes.Public);
		type.DefineField("Second", typeof(uint), FieldAttributes.Public);
		var identity = type.DefineMethod("Identity", MethodAttributes.Public | MethodAttributes.Static, type, [type]);
		var il = identity.GetILGenerator();
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Ret);
		type.CreateType();
		var path = Path.Combine(directory, "ApplicationFieldHandleProbe.dll");
		assembly.Save(path);
		return path;
	}

	public static string Create(string directory, bool rvaField, int size, bool storeHandle)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("InitializedSpanProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("InitializedSpanProbe");
		var type = module.DefineType("InitializedSpanProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var field = rvaField
			? type.DefineInitializedData("Data", Enumerable.Range(1, size).Select(value => (byte)value).ToArray(), FieldAttributes.Static | FieldAttributes.Public)
			: type.DefineField("Data", typeof(uint), FieldAttributes.Static | FieldAttributes.Public);
		var entry = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator();
		il.Emit(OpCodes.Ldtoken, field);
		if (storeHandle)
		{
			var handle = il.DeclareLocal(typeof(RuntimeFieldHandle));
			il.Emit(OpCodes.Stloc, handle);
			il.Emit(OpCodes.Ldloc, handle);
		}
		il.Emit(OpCodes.Call, typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.CreateSpan))!.MakeGenericMethod(typeof(uint)));
		il.Emit(OpCodes.Pop);
		il.Emit(OpCodes.Ldc_I4, 42);
		il.Emit(OpCodes.Ret);
		type.CreateType();
		var path = Path.Combine(directory, "InitializedSpanProbe.dll");
		assembly.Save(path);
		return path;
	}
}
