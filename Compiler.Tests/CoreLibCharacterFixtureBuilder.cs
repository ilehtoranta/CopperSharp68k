/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CopperSharp.Compiler.Tests;

internal static class CoreLibCharacterFixtureBuilder
{
	public static string CreateObjectInlineArrayHelper(string directory, bool extraInstruction)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("ObjectInlineArrayProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("ObjectInlineArrayProbe");
		var type = module.DefineType("<PrivateImplementationDetails>", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
		type.SetCustomAttribute(new CustomAttributeBuilder(typeof(CompilerGeneratedAttribute).GetConstructor(Type.EmptyTypes)!, []));
		var marker = type.DefineField("Marker", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
		var helper = type.DefineMethod("InlineArrayAsReadOnlySpan", MethodAttributes.Public | MethodAttributes.Static);
		var arguments = helper.DefineGenericParameters("TBuffer", "TElement");
		helper.SetReturnType(typeof(ReadOnlySpan<>).MakeGenericType(arguments[1]));
		helper.SetParameters(arguments[0].MakeByRefType(), typeof(int));
		var asRef = typeof(Unsafe).GetMethods().Single(method => method.Name == "AsRef" && method.IsGenericMethodDefinition && method.GetParameters()[0].ParameterType.IsByRef);
		var asType = typeof(Unsafe).GetMethods().Single(method => method.Name == "As" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 2);
		var createSpan = typeof(MemoryMarshal).GetMethod(nameof(MemoryMarshal.CreateReadOnlySpan))!;
		var il = helper.GetILGenerator();
		if (extraInstruction)
		{
			il.Emit(OpCodes.Ldc_I4_1);
			il.Emit(OpCodes.Stsfld, marker);
		}
		il.Emit(OpCodes.Ldarg_0);
		il.Emit(OpCodes.Call, asRef.MakeGenericMethod(arguments[0]));
		il.Emit(OpCodes.Call, asType.MakeGenericMethod(arguments));
		il.Emit(OpCodes.Ldarg_1);
		il.Emit(OpCodes.Call, createSpan.MakeGenericMethod(arguments[1]));
		il.Emit(OpCodes.Ret);
		var probe = module.DefineType("ObjectInlineArrayProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var entry = probe.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var bufferType = typeof(object).Assembly.GetType("System.Runtime.CompilerServices.InlineArray4`1", throwOnError: true)!.MakeGenericType(typeof(object));
		var entryIl = entry.GetILGenerator();
		var buffer = entryIl.DeclareLocal(bufferType);
		entryIl.Emit(OpCodes.Ldloca, buffer);
		entryIl.Emit(OpCodes.Initobj, bufferType);
		entryIl.Emit(OpCodes.Ldloca, buffer);
		entryIl.Emit(OpCodes.Ldc_I4_4);
		entryIl.Emit(OpCodes.Call, helper.MakeGenericMethod(bufferType, typeof(object)));
		entryIl.Emit(OpCodes.Pop);
		entryIl.Emit(OpCodes.Ldc_I4, 42);
		entryIl.Emit(OpCodes.Ret);
		type.CreateType();
		probe.CreateType();
		var path = Path.Combine(directory, "ObjectInlineArrayProbe.dll");
		assembly.Save(path);
		return path;
	}

	// Reference the implementation assembly directly to exercise its private ABI helpers.
	public static string Create(string directory)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("CoreLibCharacterProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("CoreLibCharacterProbe");
		var type = module.DefineType("CoreLibCharacterProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var entry = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator();
		var data = typeof(MemoryMarshal).GetMethods().Single(method =>
			method.Name == "GetArrayDataReference" && method.IsGenericMethodDefinition &&
			method.GetParameters()[0].ParameterType.IsArray).MakeGenericMethod(typeof(char));
		var add = typeof(Unsafe).GetMethods().Single(method =>
			method.Name == "Add" && method.IsGenericMethodDefinition &&
			method.GetParameters()[0].ParameterType.IsByRef &&
			method.GetParameters()[1].ParameterType == typeof(int)).MakeGenericMethod(typeof(char));
		var memmove = typeof(Buffer).GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Single(method =>
			method.Name == "Memmove" && method.IsGenericMethodDefinition).MakeGenericMethod(typeof(char));
		var genericMove = type.DefineMethod("GenericMove", MethodAttributes.Private | MethodAttributes.Static);
		var moveElement = genericMove.DefineGenericParameters("T")[0];
		genericMove.SetReturnType(typeof(void));
		genericMove.SetParameters(moveElement.MakeByRefType(), moveElement.MakeByRefType(), typeof(nuint));
		genericMove.SetImplementationFlags(MethodImplAttributes.NoInlining);
		var genericMoveIl = genericMove.GetILGenerator();
		genericMoveIl.Emit(OpCodes.Ldarg_0);
		genericMoveIl.Emit(OpCodes.Ldarg_1);
		genericMoveIl.Emit(OpCodes.Ldarg_2);
		genericMoveIl.Emit(OpCodes.Call, memmove.GetGenericMethodDefinition().MakeGenericMethod(moveElement));
		genericMoveIl.Emit(OpCodes.Ret);
		var stringData = typeof(string).GetMethod("GetRawStringData", BindingFlags.NonPublic | BindingFlags.Instance)!;
		var allocateString = typeof(string).GetMethod("FastAllocateString", BindingFlags.NonPublic | BindingFlags.Static,
			[typeof(nint)])!;
		var collect = typeof(M68kRuntime).GetMethod(nameof(M68kRuntime.Collect))!;
		var array = il.DeclareLocal(typeof(char[]));
		var text = il.DeclareLocal(typeof(string));
		var retained = il.DeclareLocal(typeof(char).MakeByRefType());
		var failure = il.DefineLabel();

		AllocateArray(5);
		int[] original = [0, 0x03A9, 0xD83D, 0xDE00, 0xFFFF];
		for (var index = 0; index < original.Length; index++) StoreArray(index, original[index]);
		Move(destination: 1, source: 0, count: 4);
		CheckArray([0, 0, 0x03A9, 0xD83D, 0xDE00]);
		Move(destination: 0, source: 1, count: 4);
		CheckArray([0, 0x03A9, 0xD83D, 0xDE00, 0xDE00]);
		Move(destination: 0, source: 0, count: 5);
		Move(destination: 1, source: 0, count: 0);
		CheckArray([0, 0x03A9, 0xD83D, 0xDE00, 0xDE00]);
		ArrayReference(2);
		il.Emit(OpCodes.Ldc_I4_M1);
		il.Emit(OpCodes.Call, add);
		il.Emit(OpCodes.Ldind_U2);
		il.Emit(OpCodes.Ldc_I4, 0x03A9);
		il.Emit(OpCodes.Bne_Un, failure);

		// Keep only a projected byref alive. Collection followed by allocation must
		// retain its owner, even though the array/string SSA value is otherwise dead.
		ArrayReference(2);
		il.Emit(OpCodes.Stloc, retained);
		il.Emit(OpCodes.Ldnull);
		il.Emit(OpCodes.Stloc, array);
		il.Emit(OpCodes.Call, collect);
		AllocateArray(5);
		StoreArray(2, 1234);
		CheckRetained(0xD83D);

		il.Emit(OpCodes.Ldc_I4_3);
		il.Emit(OpCodes.Conv_I);
		il.Emit(OpCodes.Call, allocateString);
		il.Emit(OpCodes.Stloc, text);
		il.Emit(OpCodes.Ldloc, text);
		il.Emit(OpCodes.Call, stringData);
		il.Emit(OpCodes.Ldc_I4_2);
		il.Emit(OpCodes.Call, add);
		il.Emit(OpCodes.Stloc, retained);
		il.Emit(OpCodes.Ldloc, retained);
		il.Emit(OpCodes.Ldc_I4, 0xDE00);
		il.Emit(OpCodes.Stind_I2);
		il.Emit(OpCodes.Ldnull);
		il.Emit(OpCodes.Stloc, text);
		il.Emit(OpCodes.Call, collect);
		il.Emit(OpCodes.Ldc_I4_3);
		il.Emit(OpCodes.Conv_I);
		il.Emit(OpCodes.Call, allocateString);
		il.Emit(OpCodes.Call, stringData);
		il.Emit(OpCodes.Ldc_I4_2);
		il.Emit(OpCodes.Call, add);
		il.Emit(OpCodes.Ldc_I4, 1234);
		il.Emit(OpCodes.Stind_I2);
		CheckRetained(0xDE00);
		il.Emit(OpCodes.Ldc_I4, 42);
		il.Emit(OpCodes.Ret);
		BuildOwnedSpanProbe(readOnly: true);
		BuildOwnedSpanProbe(readOnly: false);
		BuildFillProbe();
		BuildValueListProbe();
		BuildConstrainedStringProbe();
		BuildZeroMemoryProbe(pointer: true);
		BuildZeroMemoryProbe(pointer: false);
		BuildNarrowBoxProbe();
		il.MarkLabel(failure);
		il.Emit(OpCodes.Ldc_I4_0);
		il.Emit(OpCodes.Ret);
		var overflow = type.DefineMethod("OverflowEntry", MethodAttributes.Public | MethodAttributes.Static,
			typeof(int), Type.EmptyTypes).GetILGenerator();
		var overflowResult = overflow.DeclareLocal(typeof(int));
		var end = overflow.BeginExceptionBlock();
		overflow.Emit(OpCodes.Ldc_I4, 0x4000_0000);
		overflow.Emit(OpCodes.Conv_I);
		overflow.Emit(OpCodes.Call, allocateString);
		overflow.Emit(OpCodes.Pop);
		overflow.Emit(OpCodes.Ldc_I4_0);
		overflow.Emit(OpCodes.Stloc, overflowResult);
		overflow.Emit(OpCodes.Leave, end);
		overflow.BeginCatchBlock(typeof(OutOfMemoryException));
		overflow.Emit(OpCodes.Pop);
		overflow.Emit(OpCodes.Ldc_I4, 42);
		overflow.Emit(OpCodes.Stloc, overflowResult);
		overflow.Emit(OpCodes.Leave, end);
		overflow.EndExceptionBlock();
		overflow.Emit(OpCodes.Ldloc, overflowResult);
		overflow.Emit(OpCodes.Ret);
		var empty = typeof(string).GetField(nameof(string.Empty))!;
		foreach (var operation in new[] { OpCodes.Ldsflda, OpCodes.Stsfld })
		{
			var fieldProbe = type.DefineMethod(operation == OpCodes.Ldsflda ? "EmptyAddressEntry" : "EmptyStoreEntry",
				MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes).GetILGenerator();
			if (operation == OpCodes.Stsfld) fieldProbe.Emit(OpCodes.Ldstr, "unexpected");
			fieldProbe.Emit(operation, empty);
			if (operation == OpCodes.Ldsflda) fieldProbe.Emit(OpCodes.Pop);
			fieldProbe.Emit(OpCodes.Ldc_I4_0);
			fieldProbe.Emit(OpCodes.Ret);
		}
		type.CreateType();
		var path = Path.Combine(directory, "CoreLibCharacterProbe.dll");
		assembly.Save(path);
		return path;

		void BuildNarrowBoxProbe()
		{
			var probe = type.DefineMethod("NarrowBoxReferences", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
			var code = probe.GetILGenerator();
			var box = code.DeclareLocal(typeof(object));
			var failed = code.DefineLabel();
			foreach (var (scalar, initial, changed, load, store) in new[]
			{
				(typeof(bool), 1, 0, OpCodes.Ldind_U1, OpCodes.Stind_I1),
				(typeof(byte), 255, 128, OpCodes.Ldind_U1, OpCodes.Stind_I1),
				(typeof(sbyte), -128, -1, OpCodes.Ldind_I1, OpCodes.Stind_I1),
				(typeof(char), 0x03A9, 0xD800, OpCodes.Ldind_U2, OpCodes.Stind_I2),
				(typeof(short), -32768, -123, OpCodes.Ldind_I2, OpCodes.Stind_I2),
				(typeof(ushort), 65535, 32768, OpCodes.Ldind_U2, OpCodes.Stind_I2)
			})
			{
				var retained = code.DeclareLocal(scalar.MakeByRefType());
				code.Emit(OpCodes.Ldc_I4, initial);
				code.Emit(OpCodes.Box, scalar);
				code.Emit(OpCodes.Stloc, box);
				code.Emit(OpCodes.Ldloc, box);
				code.Emit(OpCodes.Unbox_Any, scalar);
				code.Emit(OpCodes.Ldc_I4, initial);
				code.Emit(OpCodes.Bne_Un, failed);
				code.Emit(OpCodes.Ldloc, box);
				code.Emit(OpCodes.Unbox, scalar);
				code.Emit(OpCodes.Stloc, retained);
				code.Emit(OpCodes.Ldloc, retained);
				code.Emit(load);
				code.Emit(OpCodes.Ldc_I4, initial);
				code.Emit(OpCodes.Bne_Un, failed);
				code.Emit(OpCodes.Ldloc, retained);
				code.Emit(OpCodes.Ldc_I4, changed);
				code.Emit(store);
				code.Emit(OpCodes.Ldloc, box);
				code.Emit(OpCodes.Unbox_Any, scalar);
				code.Emit(OpCodes.Ldc_I4, changed);
				code.Emit(OpCodes.Bne_Un, failed);
				code.Emit(OpCodes.Ldnull);
				code.Emit(OpCodes.Stloc, box);
				code.Emit(OpCodes.Call, collect);
				code.Emit(OpCodes.Ldc_I4, 32);
				code.Emit(OpCodes.Newarr, typeof(byte));
				code.Emit(OpCodes.Pop);
				code.Emit(OpCodes.Ldloc, retained);
				code.Emit(load);
				code.Emit(OpCodes.Ldc_I4, changed);
				code.Emit(OpCodes.Bne_Un, failed);
			}
			code.Emit(OpCodes.Ldc_I4, 42);
			code.Emit(OpCodes.Ret);
			code.MarkLabel(failed);
			code.Emit(OpCodes.Ldc_I4_1);
			code.Emit(OpCodes.Ret);
		}

		void BuildZeroMemoryProbe(bool pointer)
		{
			var owner = pointer ? typeof(Buffer) : typeof(object).Assembly.GetType("System.SpanHelpers")!;
			var name = pointer ? "ZeroMemoryInternal" : "ClearWithoutReferences";
			var kernel = owner.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
				[pointer ? typeof(void).MakePointerType() : typeof(byte).MakeByRefType(), typeof(nuint)])!;
			var probe = type.DefineMethod(pointer ? "ZeroMemoryEntry" : "ClearBytesEntry", MethodAttributes.Public | MethodAttributes.Static,
				typeof(int), Type.EmptyTypes).GetILGenerator();
			var values = probe.DeclareLocal(typeof(byte[])); var failed = probe.DefineLabel();
			probe.Emit(OpCodes.Ldc_I4, 72); probe.Emit(OpCodes.Newarr, typeof(byte)); probe.Emit(OpCodes.Stloc, values);
			for (var index = 0; index < 72; index++)
			{
				probe.Emit(OpCodes.Ldloc, values); probe.Emit(OpCodes.Ldc_I4, index); probe.Emit(OpCodes.Ldc_I4, 0xA5); probe.Emit(OpCodes.Stelem_I1);
			}
			foreach (var range in new[] { (Start: 1, Length: 65), (Start: 71, Length: 0) })
			{
				probe.Emit(OpCodes.Ldloc, values); probe.Emit(OpCodes.Ldc_I4, range.Start); probe.Emit(OpCodes.Ldelema, typeof(byte));
				if (pointer) probe.Emit(OpCodes.Conv_U);
				probe.Emit(OpCodes.Ldc_I4, range.Length); probe.Emit(OpCodes.Conv_U); probe.Emit(OpCodes.Call, kernel);
			}
			probe.Emit(OpCodes.Call, collect);
			for (var index = 0; index < 72; index++)
			{
				probe.Emit(OpCodes.Ldloc, values); probe.Emit(OpCodes.Ldc_I4, index); probe.Emit(OpCodes.Ldelem_U1);
				probe.Emit(OpCodes.Ldc_I4, index >= 1 && index < 66 ? 0 : 0xA5); probe.Emit(OpCodes.Bne_Un, failed);
			}
			probe.Emit(OpCodes.Ldc_I4, 42); probe.Emit(OpCodes.Ret);
			probe.MarkLabel(failed); probe.Emit(OpCodes.Ldc_I4_0); probe.Emit(OpCodes.Ret);
		}

		void BuildConstrainedStringProbe()
		{
			var helper = type.DefineMethod("ConstrainedStringToString", MethodAttributes.Public | MethodAttributes.Static,
				typeof(string), [typeof(string).MakeByRefType()]);
			helper.SetImplementationFlags(MethodImplAttributes.NoInlining);
			var helperIl = helper.GetILGenerator();
			helperIl.Emit(OpCodes.Ldarg_0);
			helperIl.Emit(OpCodes.Constrained, typeof(string));
			helperIl.Emit(OpCodes.Callvirt, typeof(object).GetMethod(nameof(ToString), Type.EmptyTypes)!);
			helperIl.Emit(OpCodes.Ret);
			foreach (var receiver in new[] { typeof(object), typeof(string[]) })
			{
				var other = type.DefineMethod(receiver == typeof(object) ? "ConstrainedObjectToString" : "ConstrainedArrayToString",
					MethodAttributes.Public | MethodAttributes.Static, typeof(string), [receiver.MakeByRefType()]).GetILGenerator();
				other.Emit(OpCodes.Ldarg_0);
				other.Emit(OpCodes.Constrained, receiver);
				other.Emit(OpCodes.Callvirt, typeof(object).GetMethod(nameof(ToString), Type.EmptyTypes)!);
				other.Emit(OpCodes.Ret);
			}
			var probe = type.DefineMethod("ConstrainedStringEntry", MethodAttributes.Public | MethodAttributes.Static,
				typeof(int), Type.EmptyTypes).GetILGenerator();
			var textLocal = probe.DeclareLocal(typeof(string));
			var resultLocal = probe.DeclareLocal(typeof(int));
			var bad = probe.DefineLabel();
			probe.Emit(OpCodes.Ldstr, "\u03A9\0text");
			probe.Emit(OpCodes.Stloc, textLocal);
			probe.Emit(OpCodes.Ldloca, textLocal);
			probe.Emit(OpCodes.Call, helper);
			probe.Emit(OpCodes.Ldloc, textLocal);
			probe.Emit(OpCodes.Bne_Un, bad);
			probe.Emit(OpCodes.Ldnull);
			probe.Emit(OpCodes.Stloc, textLocal);
			var complete = probe.BeginExceptionBlock();
			probe.Emit(OpCodes.Ldloca, textLocal);
			probe.Emit(OpCodes.Call, helper);
			probe.Emit(OpCodes.Pop);
			probe.Emit(OpCodes.Leave, complete);
			probe.BeginCatchBlock(typeof(NullReferenceException));
			probe.Emit(OpCodes.Pop);
			probe.Emit(OpCodes.Ldc_I4, 42);
			probe.Emit(OpCodes.Stloc, resultLocal);
			probe.Emit(OpCodes.Leave, complete);
			probe.EndExceptionBlock();
			probe.Emit(OpCodes.Ldloc, resultLocal);
			probe.Emit(OpCodes.Ret);
			probe.MarkLabel(bad);
			probe.Emit(OpCodes.Ldc_I4_1);
			probe.Emit(OpCodes.Ret);
		}

		void AllocateArray(int length)
		{
			il.Emit(OpCodes.Ldc_I4, length);
			il.Emit(OpCodes.Newarr, typeof(char));
			il.Emit(OpCodes.Stloc, array);
		}

		void StoreArray(int index, int value)
		{
			il.Emit(OpCodes.Ldloc, array);
			il.Emit(OpCodes.Ldc_I4, index);
			il.Emit(OpCodes.Ldc_I4, value);
			il.Emit(OpCodes.Stelem_I2);
		}

		void ArrayReference(int index)
		{
			il.Emit(OpCodes.Ldloc, array);
			il.Emit(OpCodes.Call, data);
			il.Emit(OpCodes.Ldc_I4, index);
			il.Emit(OpCodes.Call, add);
		}

		void Move(int destination, int source, int count)
		{
			ArrayReference(destination);
			ArrayReference(source);
			il.Emit(OpCodes.Ldc_I4, count);
			il.Emit(OpCodes.Conv_U);
			il.Emit(OpCodes.Call, genericMove.MakeGenericMethod(typeof(char)));
		}

		void CheckArray(int[] expected)
		{
			for (var index = 0; index < expected.Length; index++)
			{
				il.Emit(OpCodes.Ldloc, array);
				il.Emit(OpCodes.Ldc_I4, index);
				il.Emit(OpCodes.Ldelem_U2);
				il.Emit(OpCodes.Ldc_I4, expected[index]);
				il.Emit(OpCodes.Bne_Un, failure);
			}
		}

		void CheckRetained(int expected)
		{
			il.Emit(OpCodes.Ldloc, retained);
			il.Emit(OpCodes.Ldind_U2);
			il.Emit(OpCodes.Ldc_I4, expected);
			il.Emit(OpCodes.Bne_Un, failure);
		}

		void BuildValueListProbe()
		{
			var listType = typeof(object).Assembly.GetType("System.Collections.Generic.ValueListBuilder`1")!.MakeGenericType(typeof(int));
			var probe = type.DefineMethod("ValueListEntry", MethodAttributes.Public | MethodAttributes.Static,
				typeof(int), Type.EmptyTypes).GetILGenerator();
			var list = probe.DeclareLocal(listType);
			var span = probe.DeclareLocal(typeof(ReadOnlySpan<int>));
			var index = probe.DeclareLocal(typeof(int));
			var loop = probe.DefineLabel();
			var verify = probe.DefineLabel();
			var failed = probe.DefineLabel();
			probe.Emit(OpCodes.Ldloca, list);
			probe.Emit(OpCodes.Ldc_I4, 16);
			probe.Emit(OpCodes.Conv_U);
			probe.Emit(OpCodes.Localloc);
			probe.Emit(OpCodes.Ldc_I4_4);
			probe.Emit(OpCodes.Newobj, typeof(Span<int>).GetConstructor([typeof(void*), typeof(int)])!);
			probe.Emit(OpCodes.Call, listType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null, [typeof(Span<int>)], null)!);
			probe.MarkLabel(loop);
			probe.Emit(OpCodes.Ldloca, list);
			probe.Emit(OpCodes.Ldloc, index);
			probe.Emit(OpCodes.Ldc_I4, 17);
			probe.Emit(OpCodes.Mul);
			probe.Emit(OpCodes.Call, listType.GetMethod("Append", [typeof(int)])!);
			Increment();
			probe.Emit(OpCodes.Blt, loop);
			probe.Emit(OpCodes.Ldloca, list);
			probe.Emit(OpCodes.Call, listType.GetMethod("AsSpan")!);
			probe.Emit(OpCodes.Stloc, span);
			probe.Emit(OpCodes.Ldloca, list);
			probe.Emit(OpCodes.Call, listType.GetMethod("Dispose")!);
			probe.Emit(OpCodes.Ldloca, list);
			probe.Emit(OpCodes.Initobj, listType);
			probe.Emit(OpCodes.Call, collect);
			probe.Emit(OpCodes.Ldloca, span);
			probe.Emit(OpCodes.Call, typeof(ReadOnlySpan<int>).GetProperty("Length")!.GetMethod!);
			probe.Emit(OpCodes.Ldc_I4, 300);
			probe.Emit(OpCodes.Bne_Un, failed);
			probe.Emit(OpCodes.Ldc_I4_0);
			probe.Emit(OpCodes.Stloc, index);
			probe.MarkLabel(verify);
			probe.Emit(OpCodes.Ldloca, span);
			probe.Emit(OpCodes.Ldloc, index);
			probe.Emit(OpCodes.Call, typeof(ReadOnlySpan<int>).GetProperty("Item")!.GetMethod!);
			probe.Emit(OpCodes.Ldind_I4);
			probe.Emit(OpCodes.Ldloc, index);
			probe.Emit(OpCodes.Ldc_I4, 17);
			probe.Emit(OpCodes.Mul);
			probe.Emit(OpCodes.Bne_Un, failed);
			Increment();
			probe.Emit(OpCodes.Blt, verify);
			probe.Emit(OpCodes.Ldc_I4, 42);
			probe.Emit(OpCodes.Ret);
			probe.MarkLabel(failed);
			probe.Emit(OpCodes.Ldc_I4_0);
			probe.Emit(OpCodes.Ret);

			void Increment()
			{
				probe.Emit(OpCodes.Ldloc, index);
				probe.Emit(OpCodes.Ldc_I4_1);
				probe.Emit(OpCodes.Add);
				probe.Emit(OpCodes.Stloc, index);
				probe.Emit(OpCodes.Ldloc, index);
				probe.Emit(OpCodes.Ldc_I4, 300);
			}
		}

		void BuildFillProbe()
		{
			var fill = typeof(object).Assembly.GetType("System.SpanHelpers")!.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
				.Single(method => method.Name == "Fill" && method.IsGenericMethodDefinition).MakeGenericMethod(typeof(char));
			var fillIl = type.DefineMethod("FillEntry", MethodAttributes.Public | MethodAttributes.Static,
				typeof(int), Type.EmptyTypes).GetILGenerator();
			var fillArray = fillIl.DeclareLocal(typeof(char[]));
			var fillReference = fillIl.DeclareLocal(typeof(char).MakeByRefType());
			var count = fillIl.DeclareLocal(typeof(nuint));
			var value = fillIl.DeclareLocal(typeof(char));
			var stackCharacter = fillIl.DeclareLocal(typeof(char));
			var fillFailure = fillIl.DefineLabel();
			fillIl.Emit(OpCodes.Ldc_I4, 65_540);
			fillIl.Emit(OpCodes.Newarr, typeof(char));
			fillIl.Emit(OpCodes.Stloc, fillArray);
			foreach (var index in new[] { 0, 65_538, 65_539 })
			{
				fillIl.Emit(OpCodes.Ldloc, fillArray);
				fillIl.Emit(OpCodes.Ldc_I4, index);
				fillIl.Emit(OpCodes.Ldc_I4, 0x03A9);
				fillIl.Emit(OpCodes.Stelem_I2);
			}
			fillIl.Emit(OpCodes.Ldloc, fillArray);
			fillIl.Emit(OpCodes.Call, data);
			fillIl.Emit(OpCodes.Ldc_I4_1);
			fillIl.Emit(OpCodes.Call, add);
			fillIl.Emit(OpCodes.Stloc, fillReference);
			fillIl.Emit(OpCodes.Ldc_I4, 65_537);
			fillIl.Emit(OpCodes.Conv_U);
			fillIl.Emit(OpCodes.Stloc, count);
			fillIl.Emit(OpCodes.Ldc_I4, 0xFFFF);
			fillIl.Emit(OpCodes.Stloc, value);
			fillIl.Emit(OpCodes.Ldloc, fillReference);
			fillIl.Emit(OpCodes.Ldloc, count);
			fillIl.Emit(OpCodes.Ldloc, value);
			fillIl.Emit(OpCodes.Call, fill);
			// Preserve both inputs after the destructive native loop, and verify
			// a count above 16 bits plus guard words immediately outside the range.
			fillIl.Emit(OpCodes.Ldloc, count);
			fillIl.Emit(OpCodes.Ldc_I4, 65_537);
			fillIl.Emit(OpCodes.Bne_Un, fillFailure);
			fillIl.Emit(OpCodes.Ldloc, value);
			fillIl.Emit(OpCodes.Ldc_I4, 0xFFFF);
			fillIl.Emit(OpCodes.Bne_Un, fillFailure);
			foreach (var index in new[] { 0, 1, 32_768, 65_536, 65_537, 65_538, 65_539 })
			{
				fillIl.Emit(OpCodes.Ldloc, fillArray);
				fillIl.Emit(OpCodes.Ldc_I4, index);
				fillIl.Emit(OpCodes.Ldelem_U2);
				fillIl.Emit(OpCodes.Ldc_I4, index is 0 or 65_538 or 65_539 ? 0x03A9 : 0xFFFF);
				fillIl.Emit(OpCodes.Bne_Un, fillFailure);
			}
			fillIl.Emit(OpCodes.Ldnull);
			fillIl.Emit(OpCodes.Stloc, fillArray);
			fillIl.Emit(OpCodes.Call, collect);
			fillIl.Emit(OpCodes.Ldloc, fillReference);
			fillIl.Emit(OpCodes.Ldind_U2);
			fillIl.Emit(OpCodes.Ldc_I4, 0xFFFF);
			fillIl.Emit(OpCodes.Bne_Un, fillFailure);
			fillIl.Emit(OpCodes.Ldc_I4, 0xD83D);
			fillIl.Emit(OpCodes.Stloc, stackCharacter);
			fillIl.Emit(OpCodes.Ldloca, stackCharacter);
			fillIl.Emit(OpCodes.Ldc_I4_0);
			fillIl.Emit(OpCodes.Conv_U);
			fillIl.Emit(OpCodes.Ldc_I4_0);
			fillIl.Emit(OpCodes.Call, fill);
			fillIl.Emit(OpCodes.Ldloc, stackCharacter);
			fillIl.Emit(OpCodes.Ldc_I4, 0xD83D);
			fillIl.Emit(OpCodes.Bne_Un, fillFailure);
			fillIl.Emit(OpCodes.Ldc_I4, 42);
			fillIl.Emit(OpCodes.Ret);
			fillIl.MarkLabel(fillFailure);
			fillIl.Emit(OpCodes.Ldc_I4_0);
			fillIl.Emit(OpCodes.Ret);
		}

		void BuildOwnedSpanProbe(bool readOnly)
		{
			var spanType = readOnly ? typeof(ReadOnlySpan<char>) : typeof(Span<char>);
			var spanIl = type.DefineMethod(readOnly ? "OwnedSpanEntry" : "WritableOwnedSpanEntry", MethodAttributes.Public | MethodAttributes.Static,
				typeof(int), Type.EmptyTypes).GetILGenerator();
			var spanArray = spanIl.DeclareLocal(typeof(char[]));
			var span = spanIl.DeclareLocal(spanType);
			var stackCharacter = spanIl.DeclareLocal(typeof(char));
			var result = spanIl.DeclareLocal(typeof(int));
			var spanFailure = spanIl.DefineLabel();
			var constructor = spanType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
				null, [typeof(char).MakeByRefType(), typeof(int)], null)!;
			var item = spanType.GetProperty("Item")!.GetMethod!;
			var borrowed = type.DefineMethod(readOnly ? "BorrowedReadOnlySpan" : "BorrowedWritableSpan",
				MethodAttributes.Private | MethodAttributes.Static, typeof(int), [typeof(char).MakeByRefType()]);
			borrowed.SetImplementationFlags(MethodImplAttributes.NoInlining);
			var borrowedIl = borrowed.GetILGenerator();
			borrowedIl.Emit(OpCodes.Ldarg_0);
			borrowedIl.Emit(OpCodes.Ldc_I4_1);
			borrowedIl.Emit(OpCodes.Newobj, constructor);
			borrowedIl.Emit(OpCodes.Pop);
			borrowedIl.Emit(OpCodes.Ldc_I4, 42);
			borrowedIl.Emit(OpCodes.Ret);
			var borrowedEntryIl = type.DefineMethod(readOnly ? "BorrowedReadOnlySpanEntry" : "BorrowedWritableSpanEntry",
				MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes).GetILGenerator();
			var borrowedCharacter = borrowedEntryIl.DeclareLocal(typeof(char));
			borrowedEntryIl.Emit(OpCodes.Ldloca, borrowedCharacter);
			borrowedEntryIl.Emit(OpCodes.Call, borrowed);
			borrowedEntryIl.Emit(OpCodes.Ret);
			spanIl.Emit(OpCodes.Ldc_I4_3);
			spanIl.Emit(OpCodes.Newarr, typeof(char));
			spanIl.Emit(OpCodes.Stloc, spanArray);
			spanIl.Emit(OpCodes.Ldloc, spanArray);
			spanIl.Emit(OpCodes.Ldc_I4_1);
			spanIl.Emit(OpCodes.Ldc_I4, 0x03A9);
			spanIl.Emit(OpCodes.Stelem_I2);
			spanIl.Emit(OpCodes.Ldloc, spanArray);
			spanIl.Emit(OpCodes.Ldc_I4_1);
			spanIl.Emit(OpCodes.Ldelema, typeof(char));
			spanIl.Emit(OpCodes.Ldc_I4_2);
			spanIl.Emit(OpCodes.Newobj, constructor);
			spanIl.Emit(OpCodes.Stloc, span);
			if (!readOnly)
			{
				spanIl.Emit(OpCodes.Ldloca, span);
				spanIl.Emit(OpCodes.Ldc_I4_0);
				spanIl.Emit(OpCodes.Call, item);
				spanIl.Emit(OpCodes.Ldc_I4, 0xFFFF);
				spanIl.Emit(OpCodes.Stind_I2);
			}
			spanIl.Emit(OpCodes.Ldnull);
			spanIl.Emit(OpCodes.Stloc, spanArray);
			spanIl.Emit(OpCodes.Call, collect);
			spanIl.Emit(OpCodes.Ldc_I4_3);
			spanIl.Emit(OpCodes.Newarr, typeof(char));
			spanIl.Emit(OpCodes.Pop);
			spanIl.Emit(OpCodes.Ldloca, span);
			spanIl.Emit(OpCodes.Ldc_I4_0);
			spanIl.Emit(OpCodes.Call, item);
			spanIl.Emit(OpCodes.Ldind_U2);
			spanIl.Emit(OpCodes.Ldc_I4, readOnly ? 0x03A9 : 0xFFFF);
			spanIl.Emit(OpCodes.Bne_Un, spanFailure);
			// Exercise the in-place constructor with stack data as well as the
			// owner-retaining value constructor above.
			spanIl.Emit(OpCodes.Ldc_I4, 0xD83D);
			spanIl.Emit(OpCodes.Stloc, stackCharacter);
			spanIl.Emit(OpCodes.Ldloca, span);
			spanIl.Emit(OpCodes.Ldloca, stackCharacter);
			spanIl.Emit(OpCodes.Ldc_I4_1);
			spanIl.Emit(OpCodes.Call, constructor);
			spanIl.Emit(OpCodes.Call, collect);
			spanIl.Emit(OpCodes.Ldloca, span);
			spanIl.Emit(OpCodes.Ldc_I4_0);
			spanIl.Emit(OpCodes.Call, item);
			spanIl.Emit(OpCodes.Ldind_U2);
			spanIl.Emit(OpCodes.Ldc_I4, 0xD83D);
			spanIl.Emit(OpCodes.Bne_Un, spanFailure);
			var end = spanIl.BeginExceptionBlock();
			spanIl.Emit(OpCodes.Ldloca, stackCharacter);
			spanIl.Emit(OpCodes.Ldc_I4_M1);
			spanIl.Emit(OpCodes.Newobj, constructor);
			spanIl.Emit(OpCodes.Pop);
			spanIl.Emit(OpCodes.Leave, end);
			spanIl.BeginCatchBlock(typeof(ArgumentOutOfRangeException));
			spanIl.Emit(OpCodes.Pop);
			spanIl.Emit(OpCodes.Ldc_I4, 42);
			spanIl.Emit(OpCodes.Stloc, result);
			spanIl.Emit(OpCodes.Leave, end);
			spanIl.EndExceptionBlock();
			spanIl.Emit(OpCodes.Ldloc, result);
			spanIl.Emit(OpCodes.Ret);
			spanIl.MarkLabel(spanFailure);
			spanIl.Emit(OpCodes.Ldc_I4_1);
			spanIl.Emit(OpCodes.Ret);
		}
	}
}
