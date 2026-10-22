/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using System.Reflection.Emit;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

internal static class FloatingPointComparisonFixtureBuilder
{
	internal static IEnumerable<(string Name, OpCode Op)> Operations()
	{
		foreach (var op in new[] { OpCodes.Ceq, OpCodes.Cgt, OpCodes.Cgt_Un, OpCodes.Clt, OpCodes.Clt_Un,
			OpCodes.Beq, OpCodes.Bne_Un, OpCodes.Bgt, OpCodes.Bgt_Un, OpCodes.Blt, OpCodes.Blt_Un, OpCodes.Bge, OpCodes.Bge_Un, OpCodes.Ble, OpCodes.Ble_Un,
			OpCodes.Beq_S, OpCodes.Bne_Un_S, OpCodes.Bgt_S, OpCodes.Bgt_Un_S, OpCodes.Blt_S, OpCodes.Blt_Un_S, OpCodes.Bge_S, OpCodes.Bge_Un_S, OpCodes.Ble_S, OpCodes.Ble_Un_S })
			yield return (op.Name!.Replace(".", "_"), op);
	}

	internal static void EmitComparison(ILGenerator body, OpCode op)
	{
		body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Ldarg_1);
		if (op.FlowControl != FlowControl.Cond_Branch) { body.Emit(op); body.Emit(OpCodes.Ret); return; }
		var yes = body.DefineLabel(); body.Emit(op, yes);
		body.Emit(OpCodes.Ldc_I4_0); body.Emit(OpCodes.Ret); body.MarkLabel(yes);
		body.Emit(OpCodes.Ldc_I4_1); body.Emit(OpCodes.Ret);
	}

	public static string Create(string directory)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("FloatingPointComparisonProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("FloatingPointComparisonProbe");
		var type = module.DefineType("FloatingPointComparisonProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var entry = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var scenario = type.DefineMethod("Scenario", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		scenario.SetImplementationFlags(MethodImplAttributes.NoInlining);
		var entryBody = entry.GetILGenerator(); entryBody.Emit(OpCodes.Call, scenario); entryBody.Emit(OpCodes.Ret);
		var root = scenario.GetILGenerator(); var failure = 5000;
		foreach (var (name, op) in Operations())
		{
			var check = type.DefineMethod(name + "Checks", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
			check.SetImplementationFlags(MethodImplAttributes.NoInlining);
			var il = check.GetILGenerator();
			foreach (var precision in new[] { typeof(float), typeof(double) })
			{
				var single = precision == typeof(float);
				var helper = type.DefineMethod(name + (single ? "Single" : "Double"), MethodAttributes.Public | MethodAttributes.Static, typeof(int), [precision, precision]);
				helper.SetImplementationFlags(MethodImplAttributes.NoInlining); EmitComparison(helper.GetILGenerator(), op);
				var oracleMethod = new DynamicMethod("compare", typeof(int), [precision, precision]); EmitComparison(oracleMethod.GetILGenerator(), op);
				var oracle = oracleMethod.CreateDelegate(single ? typeof(Func<float, float, int>) : typeof(Func<double, double, int>));
				foreach (var (leftBits, rightBits) in Pairs(single))
				{
					EmitInput(leftBits); EmitInput(rightBits); il.Emit(OpCodes.Call, helper);
					var left = single ? (object)BitConverter.UInt32BitsToSingle((uint)leftBits) : BitConverter.UInt64BitsToDouble(leftBits);
					var right = single ? (object)BitConverter.UInt32BitsToSingle((uint)rightBits) : BitConverter.UInt64BitsToDouble(rightBits);
					var expected = (int)oracle.DynamicInvoke(left, right)!;
					il.Emit(OpCodes.Ldc_I4, expected); var valid = il.DefineLabel(); il.Emit(OpCodes.Beq, valid);
					il.Emit(OpCodes.Ldc_I4, failure++); il.Emit(OpCodes.Ret); il.MarkLabel(valid);
				}
				void EmitInput(ulong bits)
				{
					if (single) { il.Emit(OpCodes.Ldc_I4, unchecked((int)bits)); il.Emit(OpCodes.Call, typeof(M68kRuntime).GetMethod(nameof(M68kRuntime.UInt32BitsToSingle))!); }
					else { il.Emit(OpCodes.Ldc_I8, unchecked((long)bits)); il.Emit(OpCodes.Call, typeof(ShadowFloatingPointBits).GetMethod(nameof(ShadowFloatingPointBits.UInt64ToDouble))!); }
				}
			}
			il.Emit(OpCodes.Ldc_I4, 42); il.Emit(OpCodes.Ret);
			root.Emit(OpCodes.Call, check); root.Emit(OpCodes.Dup); root.Emit(OpCodes.Ldc_I4, 42);
			var passed = root.DefineLabel(); root.Emit(OpCodes.Beq, passed); root.Emit(OpCodes.Ret); root.MarkLabel(passed); root.Emit(OpCodes.Pop);
		}
		root.Emit(OpCodes.Ldc_I4, 42); root.Emit(OpCodes.Ret);
		var protectedMethod = type.DefineMethod("ProtectedComparison", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [typeof(double), typeof(double)]);
		protectedMethod.SetImplementationFlags(MethodImplAttributes.NoInlining);
		var protectedBody = protectedMethod.GetILGenerator(); var result = protectedBody.DeclareLocal(typeof(int));
		var exit = protectedBody.BeginExceptionBlock(); var greater = protectedBody.DefineLabel();
		protectedBody.Emit(OpCodes.Ldarg_0); protectedBody.Emit(OpCodes.Ldarg_1); protectedBody.Emit(OpCodes.Bgt_S, greater);
		protectedBody.Emit(OpCodes.Ldc_I4_0); protectedBody.Emit(OpCodes.Stloc, result); protectedBody.Emit(OpCodes.Leave, exit);
		protectedBody.MarkLabel(greater); protectedBody.Emit(OpCodes.Ldc_I4_1); protectedBody.Emit(OpCodes.Stloc, result);
		protectedBody.BeginCatchBlock(typeof(Exception)); protectedBody.Emit(OpCodes.Pop); protectedBody.Emit(OpCodes.Ldc_I4, 99); protectedBody.Emit(OpCodes.Stloc, result);
		protectedBody.EndExceptionBlock(); protectedBody.Emit(OpCodes.Ldloc, result); protectedBody.Emit(OpCodes.Ret);
		var protectedEntry = type.DefineMethod("ProtectedEntry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var protectedEntryBody = protectedEntry.GetILGenerator();
		var protectedFailure = 7000;
		foreach (var (leftBits, rightBits) in Pairs(false))
		{
			foreach (var input in new[] { leftBits, rightBits })
			{
				protectedEntryBody.Emit(OpCodes.Ldc_I8, unchecked((long)input));
				protectedEntryBody.Emit(OpCodes.Call, typeof(ShadowFloatingPointBits).GetMethod(nameof(ShadowFloatingPointBits.UInt64ToDouble))!);
			}
			protectedEntryBody.Emit(OpCodes.Call, protectedMethod);
			var expected = BitConverter.UInt64BitsToDouble(leftBits) > BitConverter.UInt64BitsToDouble(rightBits) ? 1 : 0;
			protectedEntryBody.Emit(OpCodes.Ldc_I4, expected); var valid = protectedEntryBody.DefineLabel();
			protectedEntryBody.Emit(OpCodes.Beq, valid); protectedEntryBody.Emit(OpCodes.Ldc_I4, protectedFailure++); protectedEntryBody.Emit(OpCodes.Ret); protectedEntryBody.MarkLabel(valid);
		}
		protectedEntryBody.Emit(OpCodes.Ldc_I4, 42); protectedEntryBody.Emit(OpCodes.Ret);
		type.CreateType(); var path = Path.Combine(directory, "FloatingPointComparisonProbe.dll"); assembly.Save(path); return path;
	}

	internal static IEnumerable<(ulong Left, ulong Right)> Pairs(bool single)
	{
		var values = single ? new ulong[] { 0, 0x80000000, 0x3f800000, 0xbf800000, 0xc0000000, 0x7f800000, 0xff800000, 0x7f7fffff,
			0x7fc12345, 0xffa12345, 1, 0x00800000, 0x80000001, 0x80800000 }
			: new ulong[] { 0, 0x8000000000000000, 0x3ff0000000000000, 0xbff0000000000000, 0xc000000000000000, 0x7ff0000000000000,
			0xfff0000000000000, 0x7fefffffffffffff, 0x7ff8000012345678, 0xfff0000012345678, 1, 0x0010000000000000, 0x8000000000000001, 0x8010000000000000 };
		foreach (var (left, right) in new (int, int)[] { (0,1), (1,0), (2,3), (3,2), (3,4), (4,3), (5,7), (7,5), (6,4), (4,6),
			(8,0), (0,8), (9,6), (6,9), (8,9), (2,2), (10,11), (11,10), (12,13), (13,12), (0,10), (10,0), (1,12), (12,1) })
			yield return (values[left], values[right]);
	}
}
