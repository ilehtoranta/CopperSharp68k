/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using System.Reflection.Emit;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

internal static class FloatingPointArithmeticFixtureBuilder
{
	public static string Create(string directory)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("FloatingPointArithmeticProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("FloatingPointArithmeticProbe");
		var type = module.DefineType("FloatingPointArithmeticProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var entry = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var rootBody = entry.GetILGenerator();
		var scenario = 3000;
		MethodBuilder singleSubtract = null!;
		foreach (var (operation, opcode) in new[] { ("Add", OpCodes.Add), ("Subtract", OpCodes.Sub), ("Multiply", OpCodes.Mul), ("Divide", OpCodes.Div), ("Remainder", OpCodes.Rem) })
		{
			var operationEntry = type.DefineMethod(operation + "Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
			operationEntry.SetImplementationFlags(MethodImplAttributes.NoInlining);
			var operationBody = operationEntry.GetILGenerator();
			var scenarioMethod = type.DefineMethod(operation + "Scenario", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
			scenarioMethod.SetImplementationFlags(MethodImplAttributes.NoInlining);
			operationBody.Emit(OpCodes.Call, scenarioMethod); operationBody.Emit(OpCodes.Ret);
			rootBody.Emit(OpCodes.Call, operationEntry); rootBody.Emit(OpCodes.Dup); rootBody.Emit(OpCodes.Ldc_I4, 42);
			var passed = rootBody.DefineLabel(); rootBody.Emit(OpCodes.Beq, passed); rootBody.Emit(OpCodes.Ret); rootBody.MarkLabel(passed); rootBody.Emit(OpCodes.Pop);
			var il = scenarioMethod.GetILGenerator();
			var bits = il.DeclareLocal(typeof(ulong));
			foreach (var precision in new[] { typeof(float), typeof(double) })
			{
				var single = precision == typeof(float);
				var helper = type.DefineMethod(operation + (single ? "Single" : "Double"), MethodAttributes.Public | MethodAttributes.Static, precision, [precision, precision]);
				helper.SetImplementationFlags(MethodImplAttributes.NoInlining);
				if (operation == "Subtract" && single) singleSubtract = helper;
				var body = helper.GetILGenerator();
				body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Ldarg_1); body.Emit(opcode); body.Emit(OpCodes.Ret);
				if (operation == "Subtract" && !single)
				{
					var diagnostic = type.DefineMethod("SubnormalSubtractBits", MethodAttributes.Public | MethodAttributes.Static, typeof(ulong), Type.EmptyTypes);
					var diagnosticBody = diagnostic.GetILGenerator();
					// Keep both precision routes reachable in the same native image.
					foreach (var input in new int[] { 0x3f800000, 0x3f000000 })
					{
						diagnosticBody.Emit(OpCodes.Ldc_I4, input);
						diagnosticBody.Emit(OpCodes.Call, typeof(M68kRuntime).GetMethod(nameof(M68kRuntime.UInt32BitsToSingle))!);
					}
					diagnosticBody.Emit(OpCodes.Call, singleSubtract);
					diagnosticBody.Emit(OpCodes.Call, typeof(M68kRuntime).GetMethod(nameof(M68kRuntime.SingleToUInt32Bits))!);
					diagnosticBody.Emit(OpCodes.Ldc_I4, 0x3f000000);
					var singleValid = diagnosticBody.DefineLabel(); diagnosticBody.Emit(OpCodes.Beq, singleValid);
					diagnosticBody.Emit(OpCodes.Ldc_I8, -1L); diagnosticBody.Emit(OpCodes.Ret); diagnosticBody.MarkLabel(singleValid);
					foreach (var input in new long[] { 0x3f800000, 0x33800000 })
					{
						diagnosticBody.Emit(OpCodes.Ldc_I8, input);
						diagnosticBody.Emit(OpCodes.Call, typeof(ShadowFloatingPointBits).GetMethod(nameof(ShadowFloatingPointBits.UInt64ToDouble))!);
					}
					diagnosticBody.Emit(OpCodes.Call, helper);
					diagnosticBody.Emit(OpCodes.Call, typeof(ShadowFloatingPointBits).GetMethod(nameof(ShadowFloatingPointBits.DoubleToUInt64))!);
					diagnosticBody.Emit(OpCodes.Ret);
				}

				foreach (var (leftBits, rightBits) in FloatingPointArithmeticTests.NativeCases())
				{
					EmitInput(leftBits); EmitInput(rightBits);
					il.Emit(OpCodes.Call, helper);
					ulong expected;
					bool expectedNaN;
					if (single)
					{
						var value = FloatingPointArithmeticTests.Evaluate(operation, BitConverter.UInt32BitsToSingle((uint)leftBits), BitConverter.UInt32BitsToSingle((uint)rightBits));
						expected = BitConverter.SingleToUInt32Bits(value); expectedNaN = float.IsNaN(value);
						il.Emit(OpCodes.Call, typeof(M68kRuntime).GetMethod(nameof(M68kRuntime.SingleToUInt32Bits))!);
						il.Emit(OpCodes.Conv_U8);
					}
					else
					{
						var value = FloatingPointArithmeticTests.Evaluate(operation, BitConverter.UInt64BitsToDouble(leftBits), BitConverter.UInt64BitsToDouble(rightBits));
						expected = BitConverter.DoubleToUInt64Bits(value); expectedNaN = double.IsNaN(value);
						il.Emit(OpCodes.Call, typeof(ShadowFloatingPointBits).GetMethod(nameof(ShadowFloatingPointBits.DoubleToUInt64))!);
					}
					var valid = il.DefineLabel();
					if (expectedNaN)
					{
						il.Emit(OpCodes.Stloc, bits);
						var invalid = il.DefineLabel();
						var exponentMask = single ? 0x7f800000UL : 0x7ff0000000000000UL;
						var fractionMask = single ? 0x007fffffUL : 0x000fffffffffffffUL;
						il.Emit(OpCodes.Ldloc, bits); il.Emit(OpCodes.Ldc_I8, unchecked((long)exponentMask)); il.Emit(OpCodes.And);
						il.Emit(OpCodes.Ldc_I8, unchecked((long)exponentMask)); il.Emit(OpCodes.Bne_Un, invalid);
						il.Emit(OpCodes.Ldloc, bits); il.Emit(OpCodes.Ldc_I8, unchecked((long)fractionMask)); il.Emit(OpCodes.And);
						il.Emit(OpCodes.Ldc_I8, 0L); il.Emit(OpCodes.Bne_Un, valid); il.MarkLabel(invalid);
					}
					else { il.Emit(OpCodes.Ldc_I8, unchecked((long)expected)); il.Emit(OpCodes.Beq, valid); }
					il.Emit(OpCodes.Ldc_I4, scenario++); il.Emit(OpCodes.Ret); il.MarkLabel(valid);
				}
				void EmitInput(ulong input)
				{
					if (single)
					{
						il.Emit(OpCodes.Ldc_I4, unchecked((int)input));
						il.Emit(OpCodes.Call, typeof(M68kRuntime).GetMethod(nameof(M68kRuntime.UInt32BitsToSingle))!);
					}
					else
					{
						il.Emit(OpCodes.Ldc_I8, unchecked((long)input));
						il.Emit(OpCodes.Call, typeof(ShadowFloatingPointBits).GetMethod(nameof(ShadowFloatingPointBits.UInt64ToDouble))!);
					}
				}
			}
			il.Emit(OpCodes.Ldc_I4, 42); il.Emit(OpCodes.Ret);
		}
		rootBody.Emit(OpCodes.Ldc_I4, 42); rootBody.Emit(OpCodes.Ret);
		type.CreateType();
		var path = Path.Combine(directory, "FloatingPointArithmeticProbe.dll"); assembly.Save(path); return path;
	}
}
