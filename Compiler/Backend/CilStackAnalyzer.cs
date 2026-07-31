/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using System.Collections.Immutable;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal enum CilStackValueKind
{
	BooleanByte,
	UnsignedByte,
	SignedByte,
	UnsignedWord,
	SignedWord,
	Int32,
	Int64,
	Reference,
	ManagedPointer
}

internal static class CilStackAnalyzer
{
	public static IReadOnlyDictionary<int, int> Analyze(
		CilMethod method,
		CompilationModule module)
	{
		if (method.IsImport)
		{
			return new Dictionary<int, int>();
		}

		if (method.Instructions.Count == 0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"Method body is empty.",
				method.DisplayName);
		}

		var instructions = method.Instructions.ToDictionary(item => item.Offset);
		var depths = new Dictionary<int, int>();
		var work = new Queue<(int Offset, int Depth)>();
		work.Enqueue((method.Instructions[0].Offset, 0));
		foreach (var region in method.ExceptionRegions)
		{
			work.Enqueue((region.HandlerOffset, region.IsCatch ? 1 : 0));
		}

		while (work.Count != 0)
		{
			var (offset, depth) = work.Dequeue();
			if (depths.TryGetValue(offset, out var priorDepth))
			{
				if (priorDepth != depth)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidEvaluationStack,
						$"Control-flow merge has stack depths {priorDepth} and {depth}.",
						method.DisplayName,
						offset);
				}

				continue;
			}

			if (!instructions.TryGetValue(offset, out var instruction))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Control flow targets invalid IL offset 0x{offset:X}.",
					method.DisplayName,
					offset);
			}

			depths.Add(offset, depth);
			var nextDepth = instruction.OpCode == OpCodes.Leave || instruction.OpCode == OpCodes.Leave_S
				? 0
				: checked(depth + GetStackDelta(method, module, instruction));
			if (nextDepth < 0)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidEvaluationStack,
					"Instruction pops more values than are present.",
					method.DisplayName,
					instruction.Offset);
			}

			if (instruction.OpCode == OpCodes.Throw ||
				instruction.OpCode == OpCodes.Rethrow ||
				instruction.OpCode == OpCodes.Endfinally)
			{
				continue;
			}

			if (instruction.OpCode == OpCodes.Ret)
			{
				if (nextDepth != 0)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidEvaluationStack,
						$"Return leaves {nextDepth} values on the evaluation stack.",
						method.DisplayName,
						instruction.Offset);
				}

				continue;
			}

			if (IsUnconditionalBranch(instruction.OpCode))
			{
				EnqueueBranchTarget(work, instruction, nextDepth);
				continue;
			}

			if (IsConditionalBranch(instruction.OpCode))
			{
				EnqueueBranchTarget(work, instruction, nextDepth);
				EnqueueFallthrough(work, instructions, instruction, nextDepth, method);
				continue;
			}

			if (instruction.OpCode == OpCodes.Switch)
			{
				foreach (var target in (int[])instruction.Operand!)
				{
					work.Enqueue((target, nextDepth));
				}

				EnqueueFallthrough(work, instructions, instruction, nextDepth, method);
				continue;
			}

			EnqueueFallthrough(work, instructions, instruction, nextDepth, method);
		}

		return depths;
	}

	public static IReadOnlyDictionary<int, ImmutableArray<CilStackValueKind>> AnalyzeTypes(
		CilMethod method,
		CompilationModule module)
	{
		if (method.IsImport)
		{
			return new Dictionary<int, ImmutableArray<CilStackValueKind>>();
		}

		if (method.Instructions.Count == 0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"Method body is empty.",
				method.DisplayName);
		}

		var instructions = method.Instructions.ToDictionary(item => item.Offset);
		var states = new Dictionary<int, ImmutableArray<CilStackValueKind>>();
		var work = new Queue<(int Offset, ImmutableArray<CilStackValueKind> Stack)>();
		work.Enqueue((method.Instructions[0].Offset, ImmutableArray<CilStackValueKind>.Empty));
		foreach (var region in method.ExceptionRegions)
		{
			work.Enqueue((
				region.HandlerOffset,
				region.IsCatch
					? ImmutableArray.Create(CilStackValueKind.Reference)
					: ImmutableArray<CilStackValueKind>.Empty));
		}

		while (work.Count != 0)
		{
			var (offset, stack) = work.Dequeue();
			if (states.TryGetValue(offset, out var priorStack))
			{
				if (!priorStack.SequenceEqual(stack))
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidEvaluationStack,
						$"Control-flow merge has incompatible evaluation-stack types: prior [{string.Join(",", priorStack)}], incoming [{string.Join(",", stack)}].",
						method.DisplayName,
						offset);
				}

				continue;
			}

			if (!instructions.TryGetValue(offset, out var instruction))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Control flow targets invalid IL offset 0x{offset:X}.",
					method.DisplayName,
					offset);
			}

			states.Add(offset, stack);
			var nextStack = ApplyStackEffect(method, module, instruction, stack);
			if (instruction.OpCode == OpCodes.Throw ||
				instruction.OpCode == OpCodes.Rethrow ||
				instruction.OpCode == OpCodes.Endfinally)
			{
				continue;
			}

			if (instruction.OpCode == OpCodes.Ret)
			{
				if (nextStack.Length != 0)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidEvaluationStack,
						$"Return leaves {nextStack.Length} values on the evaluation stack.",
						method.DisplayName,
						instruction.Offset);
				}

				continue;
			}

			if (IsUnconditionalBranch(instruction.OpCode))
			{
				EnqueueBranchTarget(work, instruction, nextStack);
				continue;
			}

			if (IsConditionalBranch(instruction.OpCode))
			{
				EnqueueBranchTarget(work, instruction, nextStack);
				EnqueueFallthrough(work, instructions, instruction, nextStack, method);
				continue;
			}

			if (instruction.OpCode == OpCodes.Switch)
			{
				foreach (var target in (int[])instruction.Operand!)
				{
					work.Enqueue((target, nextStack));
				}

				EnqueueFallthrough(work, instructions, instruction, nextStack, method);
				continue;
			}

			EnqueueFallthrough(work, instructions, instruction, nextStack, method);
		}

		return states;
	}

	private static int GetStackDelta(
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Ldc_I8)
		{
			return 2;
		}

		if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
		{
			var target = module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
			var count = ParameterSlotCount(target.Signature.ParameterTypes) +
				(target.Signature.Header.IsInstance &&
					(op != OpCodes.Newobj ||
					 target.ImportName?.StartsWith("intrinsic:nullable-ctor:", StringComparison.Ordinal) == true)
					? 1
					: 0);
			var pushes = op == OpCodes.Newobj
				? 1
				: SlotCount(target.Signature.ReturnType);
			return pushes - count;
		}

		if (op == OpCodes.Initobj)
		{
			return -1;
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			return SlotCount(TypeForParameter(method, argumentIndex));
		}

		if (TryGetLoadLocalIndex(instruction, out var loadLocal))
		{
			return SlotCount(method.Locals[loadLocal]);
		}

		if (TryGetStoreLocalIndex(instruction, out var storeLocal))
		{
			return -SlotCount(method.Locals[storeLocal]);
		}

		if (op == OpCodes.Starg || op == OpCodes.Starg_S)
		{
			return -SlotCount(TypeForParameter(method, Convert.ToInt32(instruction.Operand)));
		}

		if (op == OpCodes.Ret)
		{
			return -SlotCount(method.Signature.ReturnType);
		}

		if (op == OpCodes.Throw)
		{
			return -1;
		}

		if (op == OpCodes.Rethrow || op == OpCodes.Endfinally)
		{
			return 0;
		}

		return op.StackBehaviourPop switch
		{
			StackBehaviour.Pop0 => PushCount(op.StackBehaviourPush),
			StackBehaviour.Pop1 or
				StackBehaviour.Popi or
				StackBehaviour.Popref => -1 + PushCount(op.StackBehaviourPush),
			StackBehaviour.Pop1_pop1 or
				StackBehaviour.Popi_pop1 or
				StackBehaviour.Popi_popi or
				StackBehaviour.Popi_popi8 or
				StackBehaviour.Popi_popr4 or
				StackBehaviour.Popi_popr8 or
				StackBehaviour.Popref_pop1 or
				StackBehaviour.Popref_popi => -2 + PushCount(op.StackBehaviourPush),
			StackBehaviour.Popi_popi_popi or
				StackBehaviour.Popref_popi_pop1 or
				StackBehaviour.Popref_popi_popi or
				StackBehaviour.Popref_popi_popi8 or
				StackBehaviour.Popref_popi_popr4 or
				StackBehaviour.Popref_popi_popr8 or
				StackBehaviour.Popref_popi_popref => -3 + PushCount(op.StackBehaviourPush),
			StackBehaviour.Varpop => throw Unsupported(method, instruction, "variable stack effect"),
			_ => throw Unsupported(method, instruction, $"stack behavior {op.StackBehaviourPop}")
		};
	}

	private static int PushCount(StackBehaviour behavior) =>
		behavior switch
		{
			StackBehaviour.Push0 => 0,
			StackBehaviour.Push1 or
				StackBehaviour.Pushi or
				StackBehaviour.Pushi8 or
				StackBehaviour.Pushr4 or
				StackBehaviour.Pushr8 or
				StackBehaviour.Pushref => 1,
			StackBehaviour.Push1_push1 => 2,
			StackBehaviour.Varpush => throw new InvalidOperationException("Variable pushes must be handled by the opcode."),
			_ => throw new InvalidOperationException($"Unknown CIL push behavior {behavior}.")
		};

	private static bool IsUnconditionalBranch(OpCode op) =>
		op == OpCodes.Br || op == OpCodes.Br_S || op == OpCodes.Leave || op == OpCodes.Leave_S;

	private static bool IsConditionalBranch(OpCode op) =>
		op.FlowControl == FlowControl.Cond_Branch && op != OpCodes.Switch;

	private static bool IsRelationalBranch(OpCode op) =>
		op == OpCodes.Beq || op == OpCodes.Beq_S ||
		op == OpCodes.Bne_Un || op == OpCodes.Bne_Un_S ||
		op == OpCodes.Bge || op == OpCodes.Bge_S ||
		op == OpCodes.Bgt || op == OpCodes.Bgt_S ||
		op == OpCodes.Ble || op == OpCodes.Ble_S ||
		op == OpCodes.Blt || op == OpCodes.Blt_S ||
		op == OpCodes.Bge_Un || op == OpCodes.Bge_Un_S ||
		op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S ||
		op == OpCodes.Ble_Un || op == OpCodes.Ble_Un_S ||
		op == OpCodes.Blt_Un || op == OpCodes.Blt_Un_S;

	private static bool IsIntConstant(OpCode op) =>
		op == OpCodes.Ldc_I4_M1 ||
		(op.Value >= OpCodes.Ldc_I4_0.Value && op.Value <= OpCodes.Ldc_I4_8.Value) ||
		op == OpCodes.Ldc_I4_S ||
		op == OpCodes.Ldc_I4;

	private static bool TryGetArgumentIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldarg_0.Value && op.Value <= OpCodes.Ldarg_3.Value)
		{
			index = op.Value - OpCodes.Ldarg_0.Value;
			return true;
		}

		if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = default;
		return false;
	}

	private static bool TryGetLoadLocalIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldloc_0.Value && op.Value <= OpCodes.Ldloc_3.Value)
		{
			index = op.Value - OpCodes.Ldloc_0.Value;
			return true;
		}

		if (op == OpCodes.Ldloc || op == OpCodes.Ldloc_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = default;
		return false;
	}

	private static bool TryGetLoadLocalAddressIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Ldloca || op == OpCodes.Ldloca_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = default;
		return false;
	}

	private static bool TryGetLoadArgumentAddressIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Ldarga || op == OpCodes.Ldarga_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = default;
		return false;
	}

	private static bool TryGetStoreLocalIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Stloc_0.Value && op.Value <= OpCodes.Stloc_3.Value)
		{
			index = op.Value - OpCodes.Stloc_0.Value;
			return true;
		}

		if (op == OpCodes.Stloc || op == OpCodes.Stloc_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = default;
		return false;
	}

	private static bool IsArrayLoad(OpCode op) =>
		op == OpCodes.Ldelem_I1 ||
		op == OpCodes.Ldelem_U1 ||
		op == OpCodes.Ldelem_I2 ||
		op == OpCodes.Ldelem_U2 ||
		op == OpCodes.Ldelem_I4 ||
		op == OpCodes.Ldelem_U4 ||
		op == OpCodes.Ldelem_I ||
		op == OpCodes.Ldelem_Ref ||
		op == OpCodes.Ldelema;

	private static bool IsArrayStore(OpCode op) =>
		op == OpCodes.Stelem ||
		op == OpCodes.Stelem_I1 ||
		op == OpCodes.Stelem_I2 ||
		op == OpCodes.Stelem_I4 ||
		op == OpCodes.Stelem_I ||
		op == OpCodes.Stelem_Ref;

	private static bool IsIndirectLoad(OpCode op) =>
		op == OpCodes.Ldind_I1 ||
		op == OpCodes.Ldind_U1 ||
		op == OpCodes.Ldind_I2 ||
		op == OpCodes.Ldind_U2 ||
		op == OpCodes.Ldind_I4 ||
		op == OpCodes.Ldind_U4 ||
		op == OpCodes.Ldind_I ||
		op == OpCodes.Ldind_Ref;

	private static bool IsIndirectStore(OpCode op) =>
		op == OpCodes.Stind_I1 ||
		op == OpCodes.Stind_I2 ||
		op == OpCodes.Stind_I4 ||
		op == OpCodes.Stind_I ||
		op == OpCodes.Stind_Ref;

	private static bool IsConversion(OpCode op) =>
		op == OpCodes.Conv_I ||
		op == OpCodes.Conv_U ||
		op == OpCodes.Conv_I4 ||
		op == OpCodes.Conv_U4 ||
		op == OpCodes.Conv_I1 ||
		op == OpCodes.Conv_U1 ||
		op == OpCodes.Conv_I2 ||
		op == OpCodes.Conv_U2;

	private static void EnqueueBranchTarget(
		Queue<(int Offset, int Depth)> work,
		CilInstruction instruction,
		int depth) =>
		work.Enqueue(((int)instruction.Operand!, depth));

	private static void EnqueueBranchTarget(
		Queue<(int Offset, ImmutableArray<CilStackValueKind> Stack)> work,
		CilInstruction instruction,
		ImmutableArray<CilStackValueKind> stack) =>
		work.Enqueue(((int)instruction.Operand!, stack));

	private static void EnqueueFallthrough(
		Queue<(int Offset, int Depth)> work,
		IReadOnlyDictionary<int, CilInstruction> instructions,
		CilInstruction instruction,
		int depth,
		CilMethod method)
	{
		if (!instructions.ContainsKey(instruction.NextOffset))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"Reachable control flow falls past the end of the method.",
				method.DisplayName,
				instruction.Offset);
		}

		work.Enqueue((instruction.NextOffset, depth));
	}

	private static void EnqueueFallthrough(
		Queue<(int Offset, ImmutableArray<CilStackValueKind> Stack)> work,
		IReadOnlyDictionary<int, CilInstruction> instructions,
		CilInstruction instruction,
		ImmutableArray<CilStackValueKind> stack,
		CilMethod method)
	{
		if (!instructions.ContainsKey(instruction.NextOffset))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"Reachable control flow falls past the end of the method.",
				method.DisplayName,
				instruction.Offset);
		}

		work.Enqueue((instruction.NextOffset, stack));
	}

	private static M68kCompilationException Unsupported(
		CilMethod method,
		CilInstruction instruction,
		string detail) =>
		new(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"Opcode '{instruction.OpCode.Name}' has unsupported {detail}.",
			method.DisplayName,
			instruction.Offset);

	internal static ImmutableArray<CilStackValueKind> ApplyStackEffect(
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		ImmutableArray<CilStackValueKind> stack)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Nop)
		{
			return stack;
		}

		if (IsIntConstant(op))
		{
			return Push(stack, CilStackValueKind.Int32);
		}

		if (op == OpCodes.Ldc_I8)
		{
			return Push(Push(stack, CilStackValueKind.Int64), CilStackValueKind.Int64);
		}

		if (op == OpCodes.Ldnull || op == OpCodes.Ldstr)
		{
			return Push(stack, CilStackValueKind.Reference);
		}

		if (op == OpCodes.Ldtoken)
		{
			return Push(stack, CilStackValueKind.Int32);
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			return PushValue(stack, StackKindForParameter(module, method, argumentIndex));
		}

		if (TryGetLoadLocalIndex(instruction, out var loadLocal))
		{
			return PushValue(stack, StackKindForType(method.Locals[loadLocal]));
		}

		if (TryGetLoadLocalAddressIndex(instruction, out var loadLocalAddress))
		{
			return Push(stack, CilStackValueKind.ManagedPointer);
		}

		if (TryGetLoadArgumentAddressIndex(instruction, out var loadArgumentAddress))
		{
			return Push(stack, CilStackValueKind.ManagedPointer);
		}

		if (TryGetStoreLocalIndex(instruction, out var storeLocal))
		{
			return Pop(method, instruction, stack, SlotCount(method.Locals[storeLocal]));
		}

		if (op == OpCodes.Starg || op == OpCodes.Starg_S)
		{
			var index = Convert.ToInt32(instruction.Operand);
			if (method.Signature.Header.IsInstance)
			{
				if (index == 0)
				{
					return Pop(method, instruction, stack, 1);
				}
				index--;
			}
			return Pop(method, instruction, stack, SlotCount(method.Signature.ParameterTypes[index]));
		}

		if (op == OpCodes.Dup)
		{
			EnsureDepth(method, instruction, stack, 1);
			return stack.Add(stack[^1]);
		}

		if (op == OpCodes.Pop)
		{
			return Pop(method, instruction, stack, 1);
		}

		if (op == OpCodes.Ceq || op == OpCodes.Cgt || op == OpCodes.Cgt_Un ||
			op == OpCodes.Clt || op == OpCodes.Clt_Un)
		{
			if (stack.TakeLast(Math.Min(4, stack.Length)).Contains(CilStackValueKind.Int64))
			{
				throw Unsupported(method, instruction, "64-bit comparisons");
			}

			// Comparison results are observable managed booleans. Keep their
			// evaluation-stack representation 32-bit so merges with integer
			// constants remain ABI-compatible; compact nullable predicates use
			// BooleanByte independently until a consumer widens them.
			return Push(Pop(method, instruction, stack, 2), CilStackValueKind.Int32);
		}

		if (op == OpCodes.Add || op == OpCodes.Sub || op == OpCodes.And ||
			op == OpCodes.Or || op == OpCodes.Xor || op == OpCodes.Mul ||
			op == OpCodes.Div || op == OpCodes.Div_Un || op == OpCodes.Rem ||
			op == OpCodes.Rem_Un || op == OpCodes.Shl || op == OpCodes.Shr ||
			op == OpCodes.Shr_Un)
		{
			if (stack.TakeLast(Math.Min(4, stack.Length)).Contains(CilStackValueKind.Int64))
			{
				throw Unsupported(method, instruction, "64-bit arithmetic and comparisons");
			}

			return Push(
				Pop(method, instruction, stack, 2),
				TryGetNarrowArithmeticResult(method, instruction, stack, op, out var narrowResult)
					? narrowResult
					: CilStackValueKind.Int32);
		}

		if (op == OpCodes.Neg || op == OpCodes.Not)
		{
			if (stack.Length != 0 && stack[^1] == CilStackValueKind.Int64)
			{
				throw Unsupported(method, instruction, "64-bit arithmetic");
			}

			return Push(
				Pop(method, instruction, stack, 1),
				TryGetNarrowArithmeticResult(method, instruction, stack, op, out var narrowResult)
					? narrowResult
					: CilStackValueKind.Int32);
		}

		if (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S ||
			op == OpCodes.Brfalse || op == OpCodes.Brfalse_S)
		{
			return Pop(method, instruction, stack, 1);
		}

		if (IsUnconditionalBranch(op))
		{
			return op == OpCodes.Leave || op == OpCodes.Leave_S
				? ImmutableArray<CilStackValueKind>.Empty
				: stack;
		}

		if (op == OpCodes.Throw)
		{
			var result = Pop(method, instruction, stack, 1);
			if (stack.Length == 0 || stack[^1] != CilStackValueKind.Reference)
			{
				throw Unsupported(method, instruction, "throw requires an exception reference");
			}

			return result;
		}

		if (op == OpCodes.Rethrow || op == OpCodes.Endfinally)
		{
			return ImmutableArray<CilStackValueKind>.Empty;
		}

		if (IsRelationalBranch(op))
		{
			return Pop(method, instruction, stack, 2);
		}

		if (op == OpCodes.Switch)
		{
			return Pop(method, instruction, stack, 1);
		}

		if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
		{
			var target = module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
			var count = ParameterSlotCount(target.Signature.ParameterTypes) +
				(target.Signature.Header.IsInstance &&
					(op != OpCodes.Newobj ||
					 target.ImportName?.StartsWith("intrinsic:nullable-ctor:", StringComparison.Ordinal) == true)
					? 1
					: 0);
			var result = Pop(method, instruction, stack, count);
			if (op == OpCodes.Newobj)
			{
				return Push(result, CilStackValueKind.Reference);
			}
			return target.Signature.ReturnType.IsVoid
				? result
				: PushValue(result, StackKindForType(target.Signature.ReturnType));
		}

		if (op == OpCodes.Initobj)
		{
			return Pop(method, instruction, stack, 1);
		}

		if (op == OpCodes.Newarr)
		{
			return Push(Pop(method, instruction, stack, 1), CilStackValueKind.Reference);
		}

		if (op == OpCodes.Ldlen)
		{
			return Push(Pop(method, instruction, stack, 1), CilStackValueKind.Int32);
		}

		if (IsArrayLoad(op))
		{
			var result = Pop(method, instruction, stack, 2);
			return Push(result, op == OpCodes.Ldelema
				? CilStackValueKind.ManagedPointer
				: op == OpCodes.Ldelem_Ref
					? CilStackValueKind.Reference
					: op == OpCodes.Ldelem_I1
						? CilStackValueKind.SignedByte
						: op == OpCodes.Ldelem_U1
							? CilStackValueKind.UnsignedByte
							: op == OpCodes.Ldelem_I2
								? CilStackValueKind.SignedWord
								: op == OpCodes.Ldelem_U2
									? CilStackValueKind.UnsignedWord
					: CilStackValueKind.Int32);
		}

		if (IsArrayStore(op))
		{
			return Pop(method, instruction, stack, 3);
		}

		if (IsIndirectLoad(op))
		{
			return Push(Pop(method, instruction, stack, 1),
				op == OpCodes.Ldind_Ref
					? CilStackValueKind.Reference
					: op == OpCodes.Ldind_I1
						? CilStackValueKind.SignedByte
						: op == OpCodes.Ldind_U1
							? CilStackValueKind.UnsignedByte
							: op == OpCodes.Ldind_I2
								? CilStackValueKind.SignedWord
								: op == OpCodes.Ldind_U2
									? CilStackValueKind.UnsignedWord
							: CilStackValueKind.Int32);
		}

		if (IsIndirectStore(op))
		{
			return Pop(method, instruction, stack, 2);
		}

		if (op == OpCodes.Ldfld || op == OpCodes.Ldflda ||
			op == OpCodes.Stfld || op == OpCodes.Ldsfld ||
			op == OpCodes.Ldsflda || op == OpCodes.Stsfld)
		{
			var field = module.ResolveFieldToken((int)instruction.Operand!, method, instruction.Offset);
			if (op == OpCodes.Ldsfld)
			{
				return PushValue(stack, StackKindForType(field.Type));
			}
			if (op == OpCodes.Ldsflda)
			{
				return Push(stack, CilStackValueKind.ManagedPointer);
			}
			if (op == OpCodes.Stsfld)
			{
				return Pop(method, instruction, stack, 1);
			}
			if (op == OpCodes.Ldfld)
			{
				return PushValue(Pop(method, instruction, stack, 1), StackKindForType(field.Type));
			}
			if (op == OpCodes.Ldflda)
			{
				return Push(Pop(method, instruction, stack, 1), CilStackValueKind.ManagedPointer);
			}
			return Pop(method, instruction, stack, 2);
		}

		if (op == OpCodes.Ret)
		{
			return Pop(method, instruction, stack, SlotCount(method.Signature.ReturnType));
		}

		if (IsConversion(op))
		{
			return Push(Pop(method, instruction, stack, 1), StackKindForConversion(op));
		}

		throw Unsupported(method, instruction, $"typed stack effect for opcode '{op.Name}'");
	}

	internal static int GetPopSlotCount(
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		int currentDepth)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
		{
			var target = module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return ParameterSlotCount(target.Signature.ParameterTypes) +
				(target.Signature.Header.IsInstance &&
					(op != OpCodes.Newobj ||
					 target.ImportName?.StartsWith(
						"intrinsic:nullable-ctor:",
						StringComparison.Ordinal) == true)
					? 1
					: 0);
		}

		if (op == OpCodes.Initobj)
		{
			return 1;
		}
		if (TryGetArgumentIndex(instruction, out _) ||
			TryGetLoadLocalIndex(instruction, out _) ||
			TryGetLoadLocalAddressIndex(instruction, out _) ||
			TryGetLoadArgumentAddressIndex(instruction, out _))
		{
			return 0;
		}
		if (TryGetStoreLocalIndex(instruction, out var storeLocal))
		{
			return SlotCount(method.Locals[storeLocal]);
		}
		if (op == OpCodes.Starg || op == OpCodes.Starg_S)
		{
			return SlotCount(TypeForParameter(
				method,
				Convert.ToInt32(instruction.Operand)));
		}
		if (op == OpCodes.Ret)
		{
			return SlotCount(method.Signature.ReturnType);
		}
		if (op == OpCodes.Throw)
		{
			return 1;
		}
		if (op == OpCodes.Rethrow || op == OpCodes.Endfinally)
		{
			return 0;
		}
		if (op == OpCodes.Leave || op == OpCodes.Leave_S)
		{
			return currentDepth;
		}

		return op.StackBehaviourPop switch
		{
			StackBehaviour.Pop0 => 0,
			StackBehaviour.Pop1 or
				StackBehaviour.Popi or
				StackBehaviour.Popref => 1,
			StackBehaviour.Pop1_pop1 or
				StackBehaviour.Popi_pop1 or
				StackBehaviour.Popi_popi or
				StackBehaviour.Popi_popi8 or
				StackBehaviour.Popi_popr4 or
				StackBehaviour.Popi_popr8 or
				StackBehaviour.Popref_pop1 or
				StackBehaviour.Popref_popi => 2,
			StackBehaviour.Popi_popi_popi or
				StackBehaviour.Popref_popi_pop1 or
				StackBehaviour.Popref_popi_popi or
				StackBehaviour.Popref_popi_popi8 or
				StackBehaviour.Popref_popi_popr4 or
				StackBehaviour.Popref_popi_popr8 or
				StackBehaviour.Popref_popi_popref => 3,
			StackBehaviour.Varpop => throw Unsupported(
				method,
				instruction,
				"variable stack effect"),
			_ => throw Unsupported(
				method,
				instruction,
				$"stack behavior {op.StackBehaviourPop}")
		};
	}

	private static ImmutableArray<CilStackValueKind> Push(
		ImmutableArray<CilStackValueKind> stack,
		CilStackValueKind kind) =>
		stack.Add(kind);

	private static ImmutableArray<CilStackValueKind> PushValue(
		ImmutableArray<CilStackValueKind> stack,
		CilStackValueKind kind) =>
		kind == CilStackValueKind.Int64
			? stack.Add(CilStackValueKind.Int64).Add(CilStackValueKind.Int64)
			: stack.Add(kind);

	private static ImmutableArray<CilStackValueKind> Pop(
		CilMethod method,
		CilInstruction instruction,
		ImmutableArray<CilStackValueKind> stack,
		int count)
	{
		EnsureDepth(method, instruction, stack, count);
		return stack.RemoveRange(stack.Length - count, count);
	}

	private static void EnsureDepth(
		CilMethod method,
		CilInstruction instruction,
		ImmutableArray<CilStackValueKind> stack,
		int count)
	{
		if (stack.Length < count)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidEvaluationStack,
				"Instruction pops more values than are present.",
				method.DisplayName,
				instruction.Offset);
		}
	}

	private static CilStackValueKind StackKindForParameter(
		CompilationModule module,
		CilMethod method,
		int index)
	{
		if (method.Signature.Header.IsInstance)
		{
			if (index == 0)
			{
				var declaringType = new CilType(
					CilTypeKind.ValueType,
					4,
					method.DisplayName.Split("::", StringSplitOptions.None)[0]);
				return module.IsTransparentScalarType(declaringType)
					? CilStackValueKind.Int32
					: CilStackValueKind.Reference;
			}

			index--;
		}

		return StackKindForType(method.Signature.ParameterTypes[index]);
	}

	private static CilType TypeForParameter(CilMethod method, int index)
	{
		if (method.Signature.Header.IsInstance)
		{
			if (index == 0)
			{
				return new CilType(
					CilTypeKind.ManagedReference,
					4,
					method.DisplayName.Split("::", StringSplitOptions.None)[0]);
			}

			index--;
		}

		return method.Signature.ParameterTypes[index];
	}

	private static bool TryGetNarrowArithmeticResult(
		CilMethod method,
		CilInstruction instruction,
		ImmutableArray<CilStackValueKind> stack,
		OpCode op,
		out CilStackValueKind result)
	{
		result = default;
		if (!TryGetFallthroughInstruction(method, instruction, out var next) ||
			!IsNarrowConversion(next.OpCode) ||
			!TryGetNarrowConversionKind(next.OpCode, out result))
		{
			return false;
		}

		if (op == OpCodes.Shl || op == OpCodes.Shr || op == OpCodes.Shr_Un)
		{
			return stack.Length >= 2 && CilStackValueLayout.IsSmall(stack[^2]);
		}

		if (op == OpCodes.Neg || op == OpCodes.Not)
		{
			return stack.Length != 0 && CilStackValueLayout.IsSmall(stack[^1]);
		}

		return stack.Length >= 2 &&
			CilStackValueLayout.IsSmall(stack[^2]) &&
			CilStackValueLayout.IsSmall(stack[^1]);
	}

	private static bool TryGetFallthroughInstruction(
		CilMethod method,
		CilInstruction instruction,
		out CilInstruction next)
	{
		foreach (var candidate in method.Instructions)
		{
			if (candidate.Offset == instruction.NextOffset)
			{
				next = candidate;
				return true;
			}
		}

		next = null!;
		return false;
	}

	private static bool IsNarrowConversion(OpCode op) =>
		op == OpCodes.Conv_I1 || op == OpCodes.Conv_U1 ||
		op == OpCodes.Conv_I2 || op == OpCodes.Conv_U2;

	private static bool TryGetNarrowConversionKind(
		OpCode op,
		out CilStackValueKind kind)
	{
		kind = op == OpCodes.Conv_I1
			? CilStackValueKind.SignedByte
			: op == OpCodes.Conv_U1
				? CilStackValueKind.UnsignedByte
				: op == OpCodes.Conv_I2
					? CilStackValueKind.SignedWord
					: op == OpCodes.Conv_U2
						? CilStackValueKind.UnsignedWord
						: default;
		return IsNarrowConversion(op);
	}

	private static CilStackValueKind StackKindForConversion(OpCode op) =>
		TryGetNarrowConversionKind(op, out var kind)
			? kind
			: CilStackValueKind.Int32;

	internal static CilStackValueKind StackKindForType(CilType type) =>
		type.Size == 8 && type.IsSupportedScalar
			? CilStackValueKind.Int64
			: type.Size == 1 && type.Kind == CilTypeKind.Boolean
				? CilStackValueKind.BooleanByte
			: type.Size == 1 && type.Kind == CilTypeKind.SignedInteger
				? CilStackValueKind.SignedByte
			: type.Size == 1 && type.Kind == CilTypeKind.UnsignedInteger
				? CilStackValueKind.UnsignedByte
			: type.Size == 2 && (type.Kind == CilTypeKind.Character ||
				type.Kind == CilTypeKind.UnsignedInteger)
				? CilStackValueKind.UnsignedWord
			: type.Size == 2 && type.Kind == CilTypeKind.SignedInteger
				? CilStackValueKind.SignedWord
			: type.Kind switch
		{
			CilTypeKind.ManagedReference => CilStackValueKind.Reference,
			CilTypeKind.ManagedPointer => CilStackValueKind.ManagedPointer,
			_ => CilStackValueKind.Int32
		};

	private static int SlotCount(CilType type) =>
		type.IsVoid ? 0 : type.Size == 8 && type.IsSupportedScalar ? 2 : 1;

	private static int ParameterSlotCount(ImmutableArray<CilType> parameterTypes)
	{
		var result = 0;
		foreach (var parameter in parameterTypes)
		{
			result += SlotCount(parameter);
		}
		return result;
	}
}

internal static class CilStackValueLayout
{
	internal static bool IsByte(CilStackValueKind kind) =>
		kind is CilStackValueKind.BooleanByte or
			CilStackValueKind.UnsignedByte or
			CilStackValueKind.SignedByte;

	internal static bool IsWord(CilStackValueKind kind) =>
		kind is CilStackValueKind.UnsignedWord or
			CilStackValueKind.SignedWord;

	internal static bool IsSmall(CilStackValueKind kind) =>
		IsByte(kind) || IsWord(kind);

	internal static int ArithmeticWidth(CilStackValueKind kind) =>
		IsByte(kind) ? 1 : IsWord(kind) ? 2 : 4;

	internal static int SlotBytes(CilStackValueKind kind) =>
		IsByte(kind) ? 2 : 4;

	internal static int ByteDepth(
		IReadOnlyList<CilStackValueKind> stack,
		int entryCount)
	{
		if ((uint)entryCount > (uint)stack.Count)
		{
			throw new ArgumentOutOfRangeException(nameof(entryCount));
		}

		var bytes = 0;
		for (var index = 0; index < entryCount; index++)
		{
			bytes += SlotBytes(stack[index]);
		}

		return bytes;
	}

	internal static bool IsSignedByte(CilStackValueKind kind) =>
		kind == CilStackValueKind.SignedByte;

	internal static bool IsSignedWord(CilStackValueKind kind) =>
		kind == CilStackValueKind.SignedWord;
}
