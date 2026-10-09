/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

internal static class PrivateEnvironmentFixtureBuilder
{
	public static string Create(string directory)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("PrivateEnvironmentProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("PrivateEnvironmentProbe");
		var type = module.DefineType("PrivateEnvironmentProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var entry = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator();
		var failed = il.DefineLabel();
		il.Emit(OpCodes.Call, typeof(Environment).GetMethod("GetProcessorCount", BindingFlags.NonPublic | BindingFlags.Static)!);
		il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Bne_Un, failed);
		il.Emit(OpCodes.Call, typeof(Environment).GetProperty(nameof(Environment.ProcessorCount))!.GetMethod!);
		il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Bne_Un, failed);
		il.Emit(OpCodes.Ldc_I4, 42); il.Emit(OpCodes.Ret);
		il.MarkLabel(failed); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
		type.CreateType();
		var path = Path.Combine(directory, "PrivateEnvironmentProbe.dll");
		assembly.Save(path);
		return path;
	}
}
