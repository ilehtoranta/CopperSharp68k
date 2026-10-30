/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

internal static class CoreLibBigIntegerFixtureBuilder
{
	public static string Create(string directory)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("CoreLibBigIntegerProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("CoreLibBigIntegerProbe");
		var type = module.DefineType("CoreLibBigIntegerProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		CreateThreeBufferAddProbe(type);
		CreateSubnormalDragon4Probe(type);
		CreateSubnormalGrisuProbe(type);
		CreateOwnedPointerProbe(type, true);
		CreateOwnedPointerProbe(type, false);
		var entry = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator();
		var big = typeof(object).Assembly.GetType("System.Number+BigInteger", throwOnError: true)!;
		var source = il.DeclareLocal(big);
		var copy = il.DeclareLocal(big);
		var guard = il.DeclareLocal(typeof(uint));
		var set = big.GetMethod("SetUInt64", BindingFlags.Public | BindingFlags.Static)!;
		var add = big.GetMethod("Add", [typeof(uint)])!;
		var multiply = big.GetMethod("MultiplyPow10", [typeof(uint)])!;
		var shift = big.GetMethod("ShiftLeft", [typeof(uint)])!;
		var duplicate = big.GetMethod("SetValue", BindingFlags.Public | BindingFlags.Static)!;
		var getLength = big.GetMethod("GetLength")!;
		var getBlock = big.GetMethod("GetBlock")!;
		var collect = typeof(M68kRuntime).GetMethod(nameof(M68kRuntime.Collect))!;
		var failure = 100;
		foreach (var (value, addition, exponent, shiftBits) in new (ulong, uint, uint, uint)[] {
			(0, 0, 1000, 0), (ulong.MaxValue, uint.MaxValue, 0, 1),
			(0x89abcdef01234567, 0, 8, 35), (1, 1, 128, 31),
			(1, 0, 1000, 17), (1, 0, 0, 3650) })
		{
			il.Emit(OpCodes.Ldc_I4, unchecked((int)0xaabbccdd));
			il.Emit(OpCodes.Stloc, guard);
			il.Emit(OpCodes.Ldloca, source);
			il.Emit(OpCodes.Initobj, big);
			il.Emit(OpCodes.Ldloca, copy);
			il.Emit(OpCodes.Initobj, big);
			il.Emit(OpCodes.Ldloca, source);
			il.Emit(OpCodes.Ldc_I8, unchecked((long)value));
			il.Emit(OpCodes.Call, set);
			il.Emit(OpCodes.Ldloca, source);
			il.Emit(OpCodes.Ldc_I4, unchecked((int)addition));
			il.Emit(OpCodes.Call, add);
			il.Emit(OpCodes.Ldloca, source);
			il.Emit(OpCodes.Ldc_I4, unchecked((int)exponent));
			il.Emit(OpCodes.Call, multiply);
			il.Emit(OpCodes.Ldloca, source);
			il.Emit(OpCodes.Ldc_I4, unchecked((int)shiftBits));
			il.Emit(OpCodes.Call, shift);
			il.Emit(OpCodes.Ldloca, copy);
			il.Emit(OpCodes.Ldloca, source);
			il.Emit(OpCodes.Call, duplicate);
			il.Emit(OpCodes.Call, collect);
			var expected = (((BigInteger)value + addition) * BigInteger.Pow(10, (int)exponent)) << (int)shiftBits;
			var words = new List<uint>();
			while (expected != 0)
			{
				words.Add((uint)(expected & uint.MaxValue));
				expected >>= 32;
			}
			foreach (var local in new[] { source, copy })
			{
				il.Emit(OpCodes.Ldloca, local);
				il.Emit(OpCodes.Call, getLength);
				Check(words.Count);
				for (var index = 0; index < words.Count; index++)
				{
					il.Emit(OpCodes.Ldloca, local);
					il.Emit(OpCodes.Ldc_I4, index);
					il.Emit(OpCodes.Call, getBlock);
					Check(unchecked((int)words[index]));
				}
			}
			il.Emit(OpCodes.Ldloc, guard);
			Check(unchecked((int)0xaabbccdd));
		}
		il.Emit(OpCodes.Ldc_I4, 42);
		il.Emit(OpCodes.Ret);
		type.CreateType();
		var path = Path.Combine(directory, "CoreLibBigIntegerProbe.dll");
		assembly.Save(path);
		return path;

		void Check(int expected)
		{
			var valid = il.DefineLabel();
			il.Emit(OpCodes.Ldc_I4, expected);
			il.Emit(OpCodes.Beq, valid);
			il.Emit(OpCodes.Ldc_I4, failure++);
			il.Emit(OpCodes.Ret);
			il.MarkLabel(valid);
		}
	}

	private static void CreateSubnormalGrisuProbe(TypeBuilder type)
	{
		var number = typeof(object).Assembly.GetType("System.Number", throwOnError: true)!;
		var grisu = number.GetNestedType("Grisu3", BindingFlags.NonPublic)!;
		var diy = number.GetNestedType("DiyFp", BindingFlags.NonPublic)!;
		var run = grisu.GetMethod("TryRunShortest", BindingFlags.NonPublic | BindingFlags.Static)!;
		var constructor = diy.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(ulong), typeof(int)], null)!;
		var entry = type.DefineMethod("SubnormalGrisuEntry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator(); var minus = il.DeclareLocal(diy); var value = il.DeclareLocal(diy); var plus = il.DeclareLocal(diy);
		var buffer = il.DeclareLocal(typeof(byte[])); var length = il.DeclareLocal(typeof(int)); var exponent = il.DeclareLocal(typeof(int)); var failure = 4000;
		foreach (var (binaryExponent, decimalExponent, digit) in new[] { (-212, -45, (int)'1'), (-1137, -324, (int)'5') })
		{
			foreach (var (local, fraction) in new[] { (minus, 0x4000000000000000UL), (value, 0x8000000000000000UL), (plus, 0xc000000000000000UL) }) {
				il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Ldc_I8, unchecked((long)fraction)); il.Emit(OpCodes.Ldc_I4, binaryExponent); il.Emit(OpCodes.Call, constructor);
			}
			il.Emit(OpCodes.Ldc_I4, 32); il.Emit(OpCodes.Newarr, typeof(byte)); il.Emit(OpCodes.Stloc, buffer);
			il.Emit(OpCodes.Ldloca, minus); il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Ldloca, plus);
			il.Emit(OpCodes.Ldloc, buffer); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelema, typeof(byte)); il.Emit(OpCodes.Conv_U);
			il.Emit(OpCodes.Ldc_I4, 32); il.Emit(OpCodes.Newobj, typeof(Span<byte>).GetConstructor([typeof(void).MakePointerType(), typeof(int)])!);
			il.Emit(OpCodes.Ldloca, length); il.Emit(OpCodes.Ldloca, exponent); il.Emit(OpCodes.Call, run); Check(1);
			il.Emit(OpCodes.Ldloc, length); Check(1); il.Emit(OpCodes.Ldloc, exponent); Check(decimalExponent);
			il.Emit(OpCodes.Ldloc, buffer); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_U1); Check(digit);
		}
		il.Emit(OpCodes.Ldc_I4, 42); il.Emit(OpCodes.Ret);
		void Check(int expected) { il.Emit(OpCodes.Ldc_I4, expected); var valid = il.DefineLabel(); il.Emit(OpCodes.Beq, valid); il.Emit(OpCodes.Ldc_I4, failure++); il.Emit(OpCodes.Ret); il.MarkLabel(valid); }
	}

	private static void CreateSubnormalDragon4Probe(TypeBuilder type)
	{
		var number = typeof(object).Assembly.GetType("System.Number", throwOnError: true)!;
		var dragon = number.GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Single(method => method.Name == "Dragon4" && !method.IsGenericMethod);
		var entry = type.DefineMethod("SubnormalDragon4Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator();
		var buffer = il.DeclareLocal(typeof(byte[])); var exponent = il.DeclareLocal(typeof(int)); var failure = 3000;
		foreach (var (binaryExponent, decimalExponent, digit) in new[] { (-149, -45, (int)'1'), (-1074, -324, (int)'5') })
		{
			il.Emit(OpCodes.Ldc_I4, 32); il.Emit(OpCodes.Newarr, typeof(byte)); il.Emit(OpCodes.Stloc, buffer);
			il.Emit(OpCodes.Ldc_I8, 1L); il.Emit(OpCodes.Ldc_I4, binaryExponent); il.Emit(OpCodes.Ldc_I4_0);
			il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldc_I4_M1); il.Emit(OpCodes.Ldc_I4_1);
			il.Emit(OpCodes.Ldloc, buffer); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelema, typeof(byte)); il.Emit(OpCodes.Conv_U);
			il.Emit(OpCodes.Ldc_I4, 32); il.Emit(OpCodes.Newobj, typeof(Span<byte>).GetConstructor([typeof(void).MakePointerType(), typeof(int)])!);
			il.Emit(OpCodes.Ldloca, exponent); il.Emit(OpCodes.Call, dragon); Check(1);
			il.Emit(OpCodes.Ldloc, exponent); Check(decimalExponent);
			il.Emit(OpCodes.Ldloc, buffer); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_U1); Check(digit);
		}
		il.Emit(OpCodes.Ldc_I4, 42); il.Emit(OpCodes.Ret);
		void Check(int value) {
			il.Emit(OpCodes.Ldc_I4, value); var valid = il.DefineLabel(); il.Emit(OpCodes.Beq, valid);
			il.Emit(OpCodes.Ldc_I4, failure++); il.Emit(OpCodes.Ret); il.MarkLabel(valid);
		}
	}

	private static void CreateThreeBufferAddProbe(TypeBuilder type)
	{
		var big = typeof(object).Assembly.GetType("System.Number+BigInteger", throwOnError: true)!;
		var entry = type.DefineMethod("ThreeBufferAddEntry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator(); var left = il.DeclareLocal(big); var right = il.DeclareLocal(big); var sum = il.DeclareLocal(big);
		var set = big.GetMethod("SetUInt64", BindingFlags.Public | BindingFlags.Static)!;
		var multiply = big.GetMethod("MultiplyPow10", [typeof(uint)])!;
		var add = big.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(method => method.Name == "Add" && method.GetParameters().Length == 3);
		var getLength = big.GetMethod("GetLength")!; var getBlock = big.GetMethod("GetBlock")!;
		var failure = 2000;
		foreach (var exponent in new uint[] { 0, 45, 324 })
		{
			foreach (var (local, value) in new[] { (left, 2UL), (right, 1UL), (sum, 0x89abcdef01234567UL) })
			{
				il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Initobj, big);
				il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Ldc_I8, unchecked((long)value)); il.Emit(OpCodes.Call, set);
				if (local != sum) { il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Ldc_I4, unchecked((int)exponent)); il.Emit(OpCodes.Call, multiply); }
			}
			il.Emit(OpCodes.Ldloca, left); il.Emit(OpCodes.Ldloca, right); il.Emit(OpCodes.Ldloca, sum); il.Emit(OpCodes.Call, add);
			foreach (var (local, value) in new[] { (left, 2UL), (right, 1UL), (sum, 3UL) })
			{
				var expected = value * BigInteger.Pow(10, (int)exponent); var words = new List<uint>();
				while (expected != 0) { words.Add((uint)(expected & uint.MaxValue)); expected >>= 32; }
				il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Call, getLength); Check(words.Count);
				for (var index = 0; index < words.Count; index++)
				{
					il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Call, getBlock); Check(unchecked((int)words[index]));
				}
			}
		}
		il.Emit(OpCodes.Ldc_I4, 42); il.Emit(OpCodes.Ret);
		void Check(int value)
		{
			il.Emit(OpCodes.Ldc_I4, value); var valid = il.DefineLabel(); il.Emit(OpCodes.Beq, valid);
			il.Emit(OpCodes.Ldc_I4, failure++); il.Emit(OpCodes.Ret); il.MarkLabel(valid);
		}
	}

	private static void CreateOwnedPointerProbe(TypeBuilder type, bool useAsPointer)
	{
		var makeSpan = type.DefineMethod(useAsPointer ? "MakeSpan" : "MakeConvertedSpan", MethodAttributes.Public | MethodAttributes.Static, typeof(Span<uint>), [typeof(uint[])]);
		makeSpan.SetImplementationFlags(MethodImplAttributes.NoInlining);
		var makeSpanBody = makeSpan.GetILGenerator();
		makeSpanBody.Emit(OpCodes.Ldarg_0);
		makeSpanBody.Emit(OpCodes.Call, typeof(Span<uint>).GetMethod("op_Implicit", [typeof(uint[])])!);
		makeSpanBody.Emit(OpCodes.Ret);
		var method = type.DefineMethod(useAsPointer ? "OwnedPointerEntry" : "ConvertedPointerEntry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = method.GetILGenerator();
		var array = il.DeclareLocal(typeof(uint[]));
		var span = il.DeclareLocal(typeof(Span<uint>));
		var native = il.DeclareLocal(typeof(nuint));
		il.Emit(OpCodes.Ldc_I4, 5);
		il.Emit(OpCodes.Newarr, typeof(uint));
		il.Emit(OpCodes.Stloc, array);
		il.Emit(OpCodes.Ldloc, array);
		il.Emit(OpCodes.Ldc_I4, 2);
		il.Emit(OpCodes.Ldc_I4, unchecked((int)0x89abcdef));
		il.Emit(OpCodes.Stelem_I4);
		il.Emit(OpCodes.Ldloc, array);
		il.Emit(OpCodes.Call, makeSpan);
		il.Emit(OpCodes.Stloc, span);
		il.Emit(OpCodes.Ldloca, span);
		il.Emit(OpCodes.Ldc_I4, 2);
		il.Emit(OpCodes.Call, typeof(Span<uint>).GetProperty("Item")!.GetMethod!);
		if (useAsPointer)
			il.Emit(OpCodes.Call, typeof(System.Runtime.CompilerServices.Unsafe).GetMethod("AsPointer")!.MakeGenericMethod(typeof(uint)));
		il.Emit(OpCodes.Conv_U);
		il.Emit(OpCodes.Stloc, native);
		il.Emit(OpCodes.Ldloca, span);
		il.Emit(OpCodes.Initobj, typeof(Span<uint>));
		il.Emit(OpCodes.Ldnull);
		il.Emit(OpCodes.Stloc, array);
		il.Emit(OpCodes.Call, typeof(M68kRuntime).GetMethod(nameof(M68kRuntime.Collect))!);
		// Match the released array's size and overwrite the probed element. If the
		// native pointer loses its owner, first-fit reuse exposes that immediately.
		il.Emit(OpCodes.Ldc_I4, 5);
		il.Emit(OpCodes.Newarr, typeof(uint));
		il.Emit(OpCodes.Ldc_I4, 2);
		il.Emit(OpCodes.Ldc_I4, unchecked((int)0x76543210));
		il.Emit(OpCodes.Stelem_I4);
		il.Emit(OpCodes.Ldloc, native);
		il.Emit(OpCodes.Ldind_U4);
		il.Emit(OpCodes.Ldc_I4, unchecked((int)0x89abcdef));
		var valid = il.DefineLabel();
		il.Emit(OpCodes.Beq, valid);
		il.Emit(OpCodes.Ldc_I4, 1);
		il.Emit(OpCodes.Ret);
		il.MarkLabel(valid);
		il.Emit(OpCodes.Ldc_I4, 42);
		il.Emit(OpCodes.Ret);
	}
}
