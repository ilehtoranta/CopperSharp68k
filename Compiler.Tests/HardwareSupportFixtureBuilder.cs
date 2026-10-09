/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

internal static class HardwareSupportFixtureBuilder
{
	internal static Type[] GateTypes() => [typeof(System.Runtime.Intrinsics.X86.Lzcnt), typeof(System.Runtime.Intrinsics.X86.Lzcnt.X64),
		typeof(System.Runtime.Intrinsics.X86.X86Base), typeof(System.Runtime.Intrinsics.X86.X86Base.X64),
		typeof(System.Runtime.Intrinsics.Arm.ArmBase), typeof(System.Runtime.Intrinsics.Arm.ArmBase.Arm64)];

	public static string Create(string directory)
	{
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("HardwareSupportProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("HardwareSupportProbe");
		var type = module.DefineType("HardwareSupportProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var gates = type.DefineMethod("Gates", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var gateBody = gates.GetILGenerator(); var gateFailure = 100;
		foreach (var gate in GateTypes())
		{
			var getter = gate.GetProperty("IsSupported", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.GetMethod!;
			var valid = gateBody.DefineLabel(); gateBody.Emit(OpCodes.Call, getter); gateBody.Emit(OpCodes.Brfalse, valid);
			gateBody.Emit(OpCodes.Ldc_I4, gateFailure++); gateBody.Emit(OpCodes.Ret); gateBody.MarkLabel(valid);
		}
		gateBody.Emit(OpCodes.Ldc_I4, 42); gateBody.Emit(OpCodes.Ret);
		var protectedGate = type.DefineMethod("ProtectedGate", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var protectedBody = protectedGate.GetILGenerator(); var result = protectedBody.DeclareLocal(typeof(int));
		protectedBody.BeginExceptionBlock(); protectedBody.Emit(OpCodes.Call, GateTypes()[1].GetProperty("IsSupported", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.GetMethod!);
		protectedBody.Emit(OpCodes.Stloc, result); protectedBody.BeginCatchBlock(typeof(Exception)); protectedBody.Emit(OpCodes.Pop);
		protectedBody.Emit(OpCodes.Ldc_I4, 99); protectedBody.Emit(OpCodes.Stloc, result); protectedBody.EndExceptionBlock();
		protectedBody.Emit(OpCodes.Ldloc, result); protectedBody.Emit(OpCodes.Ret);
		var fake = module.DefineType("System.Runtime.Intrinsics.X86.Lzcnt", TypeAttributes.Public);
		var fakeGetter = fake.DefineMethod("get_IsSupported", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), Type.EmptyTypes);
		var fakeBody = fakeGetter.GetILGenerator(); fakeBody.Emit(OpCodes.Ldc_I4_1); fakeBody.Emit(OpCodes.Ret);
		var fakeEntry = type.DefineMethod("ApplicationGate", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var fakeEntryBody = fakeEntry.GetILGenerator(); fakeEntryBody.Emit(OpCodes.Call, fakeGetter); fakeEntryBody.Emit(OpCodes.Ret);
		fake.CreateType();
		var entry = type.DefineMethod("Log2Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var root = entry.GetILGenerator(); var failure = 1000;
		foreach (var precision in new[] { typeof(uint), typeof(ulong) })
		{
			var helper = type.DefineMethod(precision == typeof(uint) ? "Log2UInt32" : "Log2UInt64", MethodAttributes.Public | MethodAttributes.Static, typeof(int), [precision]);
			helper.SetImplementationFlags(MethodImplAttributes.NoInlining);
			var helperBody = helper.GetILGenerator(); helperBody.Emit(OpCodes.Ldarg_0); helperBody.Emit(OpCodes.Call, typeof(BitOperations).GetMethod("Log2", [precision])!); helperBody.Emit(OpCodes.Ret);
			foreach (var value in Values())
			{
				var expected = precision == typeof(uint) ? BitOperations.Log2((uint)value) : BitOperations.Log2(value);
				if (precision == typeof(uint)) root.Emit(OpCodes.Ldc_I4, unchecked((int)value)); else root.Emit(OpCodes.Ldc_I8, unchecked((long)value));
				root.Emit(OpCodes.Call, helper); root.Emit(OpCodes.Ldc_I4, expected); var valid = root.DefineLabel(); root.Emit(OpCodes.Beq, valid);
				root.Emit(OpCodes.Ldc_I4, failure++); root.Emit(OpCodes.Ret); root.MarkLabel(valid);
			}
		}
		root.Emit(OpCodes.Ldc_I4, 42); root.Emit(OpCodes.Ret);
		type.CreateType(); var path = Path.Combine(directory, "HardwareSupportProbe.dll"); assembly.Save(path); return path;
	}

	private static IEnumerable<ulong> Values()
	{
		yield return 0; yield return ulong.MaxValue; yield return 0x89abcdef01234567;
		for (var bit = 0; bit < 64; bit++)
		{
			var power = 1UL << bit; yield return power; yield return power - 1; yield return unchecked(power + 1);
		}
	}
}
