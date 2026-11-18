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

	public static string CreateByteSpanPoolingProbe(string directory, byte[] bytes, bool oversized)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("ByteSpanPoolingProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("ByteSpanPoolingProbe");
		var type = module.DefineType("ByteSpanPoolingProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var first = type.DefineInitializedData("First", bytes, FieldAttributes.Static | FieldAttributes.Private);
		var second = type.DefineInitializedData("Second", bytes, FieldAttributes.Static | FieldAttributes.Private);
		var entry = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator();
		var spanType = typeof(ReadOnlySpan<byte>);
		var local = il.DeclareLocal(spanType);
		var constructor = spanType.GetConstructor([typeof(void).MakePointerType(), typeof(int)])!;
		var indexer = spanType.GetProperty("Item")!.GetMethod!;
		foreach (var (field, length) in new[] { (first, bytes.Length), (second, bytes.Length + (oversized ? 1 : 0)) })
		{
			il.Emit(OpCodes.Ldsflda, field);
			il.Emit(OpCodes.Ldc_I4, length);
			il.Emit(OpCodes.Newobj, constructor);
			il.Emit(OpCodes.Stloc, local);
			il.Emit(OpCodes.Ldloca, local);
			il.Emit(OpCodes.Ldc_I4_0);
			il.Emit(OpCodes.Call, indexer);
			il.Emit(OpCodes.Ldind_U1);
		}
		il.Emit(OpCodes.Add);
		il.Emit(OpCodes.Ret);
		type.CreateType();
		var path = Path.Combine(directory, "ByteSpanPoolingProbe.dll");
		assembly.Save(path);
		return path;
	}
}
