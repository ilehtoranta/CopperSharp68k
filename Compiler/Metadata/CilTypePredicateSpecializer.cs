/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Metadata;

internal static class CilTypePredicateSpecializer
{
	public static CilMethod Specialize(CilMethod method, CompilationModule module)
	{
		if ((module.FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !module.IsPinnedNumericSpecialization(method) && !module.IsPinnedHandlerFormattingCaller(method) && !module.IsPinnedCompositeSpanToString(method)) ||
			method.Instructions.Count == 0 || method.ExceptionRegions.Count != 0) return method;
		var instructions = method.Instructions.ToArray();
		var targets = instructions.SelectMany(instruction => instruction.Operand switch
		{
			int target when instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch => [target],
			int[] branches when instruction.OpCode == OpCodes.Switch => branches,
			_ => Array.Empty<int>()
		}).ToHashSet();
		var changed = false;
		for (var i = 0; i + 3 < instructions.Length; i++)
		{
			if (instructions[i].OpCode == OpCodes.Sizeof &&
				!targets.Contains(instructions[i + 1].Offset) && !targets.Contains(instructions[i + 2].Offset) &&
				TryFoldSizeComparison(instructions.AsSpan(i, 3), method, module, out var sizeBranch))
			{
				instructions[i] = instructions[i] with { OpCode = OpCodes.Nop, Operand = null };
				instructions[i + 1] = instructions[i + 1] with { OpCode = OpCodes.Nop, Operand = null };
				instructions[i + 2] = sizeBranch;
				changed = true;
				continue;
			}
			if (i + 5 < instructions.Length && instructions[i].OpCode == OpCodes.Ldtoken &&
				instructions[i + 1].OpCode == OpCodes.Call && instructions[i + 2].OpCode == OpCodes.Ldtoken &&
				instructions[i + 3].OpCode == OpCodes.Call && instructions[i + 4].OpCode == OpCodes.Call &&
				instructions[i + 5].OpCode.FlowControl == FlowControl.Cond_Branch &&
				!instructions.Skip(i + 1).Take(5).Any(instruction => targets.Contains(instruction.Offset)) &&
				TryFoldTypeEquality(instructions.AsSpan(i, 6), method, module, out var equalityBranch))
			{
				for (var j = i; j < i + 5; j++) instructions[j] = instructions[j] with { OpCode = OpCodes.Nop, Operand = null };
				instructions[i + 5] = equalityBranch;
				changed = true;
				continue;
			}
			var token = instructions[i];
			var handle = instructions[i + 1];
			var predicate = instructions[i + 2];
			var branch = instructions[i + 3];
			if (handle.OpCode == OpCodes.Box && predicate.OpCode == OpCodes.Isinst &&
				(token.OpCode == OpCodes.Ldarg_0 || token.OpCode == OpCodes.Ldarg_1 || token.OpCode == OpCodes.Ldarg_2 ||
				 token.OpCode == OpCodes.Ldarg_3 || token.OpCode == OpCodes.Ldarg || token.OpCode == OpCodes.Ldarg_S) &&
				(branch.OpCode == OpCodes.Brtrue || branch.OpCode == OpCodes.Brtrue_S || branch.OpCode == OpCodes.Brfalse || branch.OpCode == OpCodes.Brfalse_S) &&
				!targets.Contains(handle.Offset) && !targets.Contains(predicate.Offset) && !targets.Contains(branch.Offset))
			{
				var boxed = module.ResolveTypeToken((int)handle.Operand!, method, handle.Offset);
				var tested = module.ResolveTypeToken((int)predicate.Operand!, method, predicate.Offset);
				var argument = ArgumentType(token, method);
				if (argument == boxed && boxed.IsEnum && (boxed.Kind is CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger) &&
					(boxed.Size is 1 or 2 or 4 or 8) && (tested.DisplayName is "System.IFormattable" or "System.ISpanFormattable") &&
					module.ResolveRuntimeTypeIdentity(tested, method.ModuleName).ModuleName == "System.Private.CoreLib")
				{
					// Every verified integral enum implements these interfaces. The
					// test only consumes an argument load and a temporary box; retain
					// neither allocation nor an unreachable virtual ToString fallback.
					for (var j = i; j < i + 3; j++) instructions[j] = instructions[j] with { OpCode = OpCodes.Nop, Operand = null };
					instructions[i + 3] = branch.OpCode == OpCodes.Brtrue || branch.OpCode == OpCodes.Brtrue_S
						? branch with { OpCode = OpCodes.Br } : branch with { OpCode = OpCodes.Nop, Operand = null };
					changed = true;
					continue;
				}
			}
			if (token.OpCode != OpCodes.Ldtoken || handle.OpCode != OpCodes.Call ||
				(predicate.OpCode != OpCodes.Callvirt && predicate.OpCode != OpCodes.Call) ||
				(branch.OpCode != OpCodes.Brtrue && branch.OpCode != OpCodes.Brtrue_S &&
				 branch.OpCode != OpCodes.Brfalse && branch.OpCode != OpCodes.Brfalse_S) ||
				targets.Contains(handle.Offset) || targets.Contains(predicate.Offset) || targets.Contains(branch.Offset)) continue;
			// These exact predicates are side-effect-free for a verified non-null
			// type token; arbitrary reflection getters are never folded.
			var predicateMember = FrameworkImplementationProfile.Canonicalize(module.DescribeFrameworkMethodToken((int)predicate.Operand!, method, predicate.Offset));
			var isValueType = predicateMember.DeclaringType.Equals(FrameworkTypeId.Named("System.Runtime", "System.Type")) &&
				predicateMember.Name == "get_IsValueType" && predicateMember.MethodTypeArguments.Length == 0 &&
				predicateMember.Signature.Header == 0x20 && predicateMember.Signature.GenericParameterCount == 0 &&
				predicateMember.Signature.RequiredParameterCount == 0 && predicateMember.Signature.ParameterTypes.Length == 0 &&
				predicateMember.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Boolean"));
			if (!FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
					module.DescribeFrameworkMethodToken((int)handle.Operand!, method, handle.Offset), true, out var fromHandle) ||
				fromHandle.Target != "intrinsic:runtime-type-from-handle" ||
				!isValueType && (!FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
					module.DescribeFrameworkMethodToken((int)predicate.Operand!, method, predicate.Offset), true, out var isEnum) ||
				isEnum.Target != "intrinsic:runtime-type-is-enum")) continue;
			var type = module.ResolveTypeToken((int)token.Operand!, method, token.Offset);
			if (type.Kind is CilTypeKind.GenericParameter or CilTypeKind.Unknown) continue;
			var identity = module.ResolveRuntimeTypeIdentity(type, method.ModuleName);
			var branchOnTrue = branch.OpCode == OpCodes.Brtrue || branch.OpCode == OpCodes.Brtrue_S;
			for (var j = i; j < i + 3; j++) instructions[j] = instructions[j] with { OpCode = OpCodes.Nop, Operand = null };
			var result = isValueType ? identity.Type.Kind is CilTypeKind.ValueType or CilTypeKind.Boolean or CilTypeKind.Character or
				CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger or CilTypeKind.NativeInteger or CilTypeKind.FloatingPoint or CilTypeKind.Void : identity.Type.IsEnum;
			instructions[i + 3] = branchOnTrue == result
				? branch with { OpCode = OpCodes.Br }
				: branch with { OpCode = OpCodes.Nop, Operand = null };
			changed = true;
		}
		if (!changed) return method;
		var byOffset = instructions.ToDictionary(static instruction => instruction.Offset);
		var reachable = new HashSet<int>();
		var pending = new Stack<int>();
		pending.Push(instructions[0].Offset);
		while (pending.TryPop(out var offset))
		{
			if (!reachable.Add(offset) || !byOffset.TryGetValue(offset, out var instruction)) continue;
			if (instruction.OpCode.FlowControl is FlowControl.Return or FlowControl.Throw) continue;
			if (instruction.OpCode == OpCodes.Switch)
				foreach (var target in (int[])instruction.Operand!) pending.Push(target);
			else if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
				pending.Push((int)instruction.Operand!);
			if (instruction.OpCode.FlowControl != FlowControl.Branch) pending.Push(instruction.NextOffset);
		}
		return method with { Instructions = instructions.Where(instruction => reachable.Contains(instruction.Offset)).ToArray() };
	}

	private static bool TryFoldSizeComparison(ReadOnlySpan<CilInstruction> sequence, CilMethod method,
		CompilationModule module, out CilInstruction branch)
	{
		branch = sequence[2];
		var equalBranch = branch.OpCode == OpCodes.Beq || branch.OpCode == OpCodes.Beq_S;
		if (!equalBranch && branch.OpCode != OpCodes.Bne_Un && branch.OpCode != OpCodes.Bne_Un_S) return false;
		var type = module.ResolveTypeToken((int)sequence[0].Operand!, method, sequence[0].Offset);
		if (type.Kind is not (CilTypeKind.Boolean or CilTypeKind.Character or CilTypeKind.SignedInteger or
			CilTypeKind.UnsignedInteger or CilTypeKind.NativeInteger or CilTypeKind.FloatingPoint or
			CilTypeKind.UnmanagedPointer or CilTypeKind.FunctionPointer)) return false;
		var constant = sequence[1];
		int expected;
		if (constant.OpCode.Value >= OpCodes.Ldc_I4_M1.Value && constant.OpCode.Value <= OpCodes.Ldc_I4_8.Value)
			expected = constant.OpCode.Value - OpCodes.Ldc_I4_0.Value;
		else if (constant.OpCode == OpCodes.Ldc_I4 || constant.OpCode == OpCodes.Ldc_I4_S)
			expected = Convert.ToInt32(constant.Operand);
		else return false;
		branch = equalBranch == (type.Size == expected)
			? branch with { OpCode = OpCodes.Br } : branch with { OpCode = OpCodes.Nop, Operand = null };
		return true;
	}

	private static bool TryFoldTypeEquality(ReadOnlySpan<CilInstruction> sequence, CilMethod method,
		CompilationModule module, out CilInstruction branch)
	{
		branch = sequence[5];
		var branchOnTrue = branch.OpCode == OpCodes.Brtrue || branch.OpCode == OpCodes.Brtrue_S;
		if (!branchOnTrue && branch.OpCode != OpCodes.Brfalse && branch.OpCode != OpCodes.Brfalse_S) return false;
		foreach (var index in new[] { 1, 3 })
			if (!FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
				module.DescribeFrameworkMethodToken((int)sequence[index].Operand!, method, sequence[index].Offset), true, out var handle) ||
				handle.Target != "intrinsic:runtime-type-from-handle") return false;
		var comparison = module.DescribeFrameworkMethodToken((int)sequence[4].Operand!, method, sequence[4].Offset);
		if (!FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(comparison, true, out var equality) ||
			equality.Target is not ("intrinsic:object-reference-equals" or "intrinsic:runtime-type-not-equals") ||
			comparison.DeclaringType.MetadataName != "System.Type") return false;
		var left = module.ResolveTypeToken((int)sequence[0].Operand!, method, sequence[0].Offset);
		var right = module.ResolveTypeToken((int)sequence[2].Operand!, method, sequence[2].Offset);
		if (left.Kind is CilTypeKind.GenericParameter or CilTypeKind.Unknown || right.Kind is CilTypeKind.GenericParameter or CilTypeKind.Unknown) return false;
		// Closed non-generic definitions have an exact module/token identity.
		// Arrays, pointers and nested generic constructions need a fuller type
		// identity proof and retain their runtime comparison here.
		if (!left.GenericArguments.IsDefaultOrEmpty || !right.GenericArguments.IsDefaultOrEmpty ||
			left.ElementType is not null || right.ElementType is not null) return false;
		var leftTarget = module.ResolveRuntimeTypeIdentity(left, method.ModuleName);
		var rightTarget = module.ResolveRuntimeTypeIdentity(right, method.ModuleName);
		if (leftTarget.Handle.IsNil || rightTarget.Handle.IsNil) return false;
		var equal = leftTarget.ModuleName == rightTarget.ModuleName && leftTarget.Handle == rightTarget.Handle;
		var result = equality.Target == "intrinsic:runtime-type-not-equals" ? !equal : equal;
		branch = branchOnTrue == result ? branch with { OpCode = OpCodes.Br } : branch with { OpCode = OpCodes.Nop, Operand = null };
		return true;
	}

	private static CilType? ArgumentType(CilInstruction load, CilMethod method)
	{
		var index = load.OpCode == OpCodes.Ldarg_0 ? 0 : load.OpCode == OpCodes.Ldarg_1 ? 1 :
			load.OpCode == OpCodes.Ldarg_2 ? 2 : load.OpCode == OpCodes.Ldarg_3 ? 3 :
			load.OpCode == OpCodes.Ldarg || load.OpCode == OpCodes.Ldarg_S ? Convert.ToInt32(load.Operand) : -1;
		if (method.Signature.Header.IsInstance) index--;
		return index >= 0 && index < method.Signature.ParameterTypes.Length ? method.Signature.ParameterTypes[index] : null;
	}
}
