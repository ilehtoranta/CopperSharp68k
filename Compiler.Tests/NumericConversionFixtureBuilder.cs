/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

internal static class NumericConversionFixtureBuilder
{
	public static string Create(string directory)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("NumericConversionProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("NumericConversionProbe");
		var type = module.DefineType("NumericConversionProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var disabled = type.DefineMethod("DisabledEntry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var disabledBody = disabled.GetILGenerator();
		disabledBody.Emit(OpCodes.Ldc_R8, 1.5);
		disabledBody.Emit(OpCodes.Conv_I4);
		disabledBody.Emit(OpCodes.Ret);
		var branched = type.DefineMethod("BranchedUInt64ToSingle", MethodAttributes.Public | MethodAttributes.Static,
			typeof(float), [typeof(bool), typeof(ulong), typeof(double)]);
		var branchBody = branched.GetILGenerator();
		var integerInput = branchBody.DefineLabel();
		var narrow = branchBody.DefineLabel();
		branchBody.Emit(OpCodes.Ldarg_0);
		branchBody.Emit(OpCodes.Brfalse, integerInput);
		branchBody.Emit(OpCodes.Ldarg_2);
		branchBody.Emit(OpCodes.Br, narrow);
		branchBody.MarkLabel(integerInput);
		branchBody.Emit(OpCodes.Ldarg_1);
		branchBody.Emit(OpCodes.Conv_R_Un);
		branchBody.MarkLabel(narrow);
		branchBody.Emit(OpCodes.Conv_R4);
		branchBody.Emit(OpCodes.Ret);
		CreateNegationProbe(type);
		var entry = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator();
		var scenario = 0;
		foreach (var source in new[] { typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double) })
		foreach (var target in new[] { typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double) })
		{
			if (source == target || (source != typeof(float) && source != typeof(double) && target != typeof(float) && target != typeof(double))) continue;
			var helper = type.DefineMethod(source.Name + "To" + target.Name, MethodAttributes.Public | MethodAttributes.Static, target, [source]);
			helper.SetImplementationFlags(MethodImplAttributes.NoInlining);
			var body = helper.GetILGenerator();
			body.Emit(OpCodes.Ldarg_0);
			if (source == typeof(uint) || source == typeof(ulong)) body.Emit(OpCodes.Conv_R_Un);
			body.Emit(target == typeof(float) ? OpCodes.Conv_R4 : target == typeof(double) ? OpCodes.Conv_R8 :
				target == typeof(int) ? OpCodes.Conv_I4 : target == typeof(uint) ? OpCodes.Conv_U4 : target == typeof(long) ? OpCodes.Conv_I8 : OpCodes.Conv_U8);
			body.Emit(OpCodes.Ret);
			var input = Expression.Parameter(source);
			var oracle = Expression.Lambda(Expression.Convert(input, target), input).Compile();
			foreach (var value in Cases(source))
			{
				EmitLiteral(il, value);
				il.Emit(OpCodes.Call, helper);
				var expected = oracle.DynamicInvoke(value)!;
				if (target == typeof(double))
				{
					il.Emit(OpCodes.Call, typeof(ShadowFloatingPointBits).GetMethod(nameof(ShadowFloatingPointBits.DoubleToUInt64))!);
					expected = BitConverter.DoubleToUInt64Bits((double)expected);
				}
				else if (target == typeof(float))
				{
					il.Emit(OpCodes.Call, typeof(M68kRuntime).GetMethod(nameof(M68kRuntime.SingleToUInt32Bits))!);
					expected = BitConverter.SingleToUInt32Bits((float)expected);
				}
				EmitLiteral(il, expected);
				var valid = il.DefineLabel();
				il.Emit(OpCodes.Beq, valid);
				il.Emit(OpCodes.Ldc_I4, 1000 + scenario++);
				il.Emit(OpCodes.Ret);
				il.MarkLabel(valid);
			}
		}
		il.Emit(OpCodes.Ldc_I4, 42);
		il.Emit(OpCodes.Ret);
		type.CreateType();
		var path = Path.Combine(directory, "NumericConversionProbe.dll");
		assembly.Save(path);
		return path;
	}

	private static void CreateNegationProbe(TypeBuilder type)
	{
		var entry = type.DefineMethod("NegationEntry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator();
		var scenario = 2000;
		foreach (var precision in new[] { typeof(float), typeof(double) })
		{
			var helper = type.DefineMethod(precision == typeof(float) ? "NegateSingle" : "NegateDouble",
				MethodAttributes.Public | MethodAttributes.Static, precision, [precision]);
			helper.SetImplementationFlags(MethodImplAttributes.NoInlining);
			var body = helper.GetILGenerator();
			body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Neg); body.Emit(OpCodes.Ret);
			foreach (var value in Cases(precision))
			{
				// Construct the input through integer bits: host ldc.r4 handling can
				// quiet a signalling NaN before the negation method receives it.
				if (precision == typeof(float))
				{
					EmitLiteral(il, BitConverter.SingleToUInt32Bits((float)value));
					il.Emit(OpCodes.Call, typeof(M68kRuntime).GetMethod(nameof(M68kRuntime.UInt32BitsToSingle))!);
				}
				else
				{
					EmitLiteral(il, BitConverter.DoubleToUInt64Bits((double)value));
					il.Emit(OpCodes.Call, typeof(ShadowFloatingPointBits).GetMethod(nameof(ShadowFloatingPointBits.UInt64ToDouble))!);
				}
				il.Emit(OpCodes.Call, helper);
				if (precision == typeof(float))
				{
					il.Emit(OpCodes.Call, typeof(M68kRuntime).GetMethod(nameof(M68kRuntime.SingleToUInt32Bits))!);
					EmitLiteral(il, BitConverter.SingleToUInt32Bits((float)value) ^ 0x80000000u);
				}
				else
				{
					il.Emit(OpCodes.Call, typeof(ShadowFloatingPointBits).GetMethod(nameof(ShadowFloatingPointBits.DoubleToUInt64))!);
					EmitLiteral(il, BitConverter.DoubleToUInt64Bits((double)value) ^ 0x8000000000000000UL);
				}
				var valid = il.DefineLabel(); il.Emit(OpCodes.Beq, valid);
				il.Emit(OpCodes.Ldc_I4, scenario++); il.Emit(OpCodes.Ret); il.MarkLabel(valid);
			}
		}
		il.Emit(OpCodes.Ldc_I4, 42); il.Emit(OpCodes.Ret);
		var integer = type.DefineMethod("NegateInt64", MethodAttributes.Public | MethodAttributes.Static, typeof(long), [typeof(long)]);
		var integerBody = integer.GetILGenerator();
		integerBody.Emit(OpCodes.Ldarg_0); integerBody.Emit(OpCodes.Neg); integerBody.Emit(OpCodes.Ret);
	}

	private static IEnumerable<object> Cases(Type type)
	{
		if (type == typeof(float))
		{
			foreach (var bits in new uint[] { 0, 0x80000000, 1, 0x007fffff, 0x00800000, 0x3f800000,
				0xbf800000, 0x3ff00000, 0xc0600000, 0x4effffff, 0x4f000000, 0x4f800000,
				0x5effffff, 0x5f000000, 0x5f800000, 0x7f7fffff, 0x7f800000, 0xff800000, 0x7fc12345, 0xffa12345 })
				yield return BitConverter.UInt32BitsToSingle(bits);
		}
		else if (type == typeof(double))
		{
			foreach (var bits in new ulong[] { 0, 0x8000000000000000, 1, 0x000fffffffffffff, 0x0010000000000000,
				0x3ff0000000000000, 0xbff0000000000000, 0x3ff8000000000000, 0xc00c000000000000,
				0x3ff0000010000000, 0x3ff0000010000001, 0x3690000000000000, 0x36a0000000000000,
				0x380fffffc0000000, 0x3810000000000000, 0x41dfffffffffffff, 0x41e0000000000000,
				0x41efffffffffffff, 0x41f0000000000000, 0x43dfffffffffffff, 0x43e0000000000000,
				0xc3e0000000000000, 0x43efffffffffffff, 0x43f0000000000000, 0x47effffff0000000,
				0x7fefffffffffffff, 0x7ff0000000000000, 0xfff0000000000000, 0x7ff8000012345678, 0xfff0000012345678 })
				yield return BitConverter.UInt64BitsToDouble(bits);
		}
		else
		{
			foreach (var bits in new ulong[] { 0, 1, ulong.MaxValue, 0x7fffffff, 0x80000000, 0xffffffff, 0x100000001,
				0x1000001, 0x20000000000001, 0x7fffffffffffffff, 0x8000000000000000,
				(1UL << 63) + (1UL << 39) - 1, (1UL << 63) + (1UL << 39) + 1 })
			{
				if (type == typeof(int)) yield return unchecked((int)bits);
				else if (type == typeof(uint)) yield return (uint)bits;
				else if (type == typeof(long)) yield return unchecked((long)bits);
				else yield return bits;
			}
		}
	}

	private static void EmitLiteral(ILGenerator il, object value)
	{
		switch (value)
		{
			case int number: il.Emit(OpCodes.Ldc_I4, number); break;
			case uint number: il.Emit(OpCodes.Ldc_I4, unchecked((int)number)); break;
			case long number: il.Emit(OpCodes.Ldc_I8, number); break;
			case ulong number: il.Emit(OpCodes.Ldc_I8, unchecked((long)number)); break;
			case float number: il.Emit(OpCodes.Ldc_R4, number); break;
			case double number: il.Emit(OpCodes.Ldc_R8, number); break;
			default: throw new ArgumentException("Unexpected numeric probe type.", nameof(value));
		}
	}
}
