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
	Float32,
	Float64,
	Reference,
	ManagedPointer,
	// Logical multiword aggregate value represented by the address of stable
	// storage. The analyzer tracks its exact CilType separately so unrelated
	// value types cannot merge merely because they share this storage shape.
	AggregateAddress
}

internal readonly record struct CilAggregateStackType(
	string ModuleName,
	CilType Type);

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
		CompilationModule module,
		CilOptimizationPlan? optimizations = null)
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
		var aggregateStates = new Dictionary<int, ImmutableArray<CilAggregateStackType?>>();
		var work = new Queue<(
			int Offset,
			ImmutableArray<CilStackValueKind> Stack,
			ImmutableArray<CilAggregateStackType?> AggregateTypes)>();
		work.Enqueue((
			method.Instructions[0].Offset,
			ImmutableArray<CilStackValueKind>.Empty,
			ImmutableArray<CilAggregateStackType?>.Empty));
		foreach (var region in method.ExceptionRegions)
		{
			var handlerStack = region.IsCatch
				? ImmutableArray.Create(CilStackValueKind.Reference)
				: ImmutableArray<CilStackValueKind>.Empty;
			work.Enqueue((
				region.HandlerOffset,
				handlerStack,
				ImmutableArray.CreateRange<CilAggregateStackType?>(
					Enumerable.Repeat<CilAggregateStackType?>(null, handlerStack.Length))));
		}

		while (work.Count != 0)
		{
			var (offset, stack, aggregateTypes) = work.Dequeue();
			if (states.TryGetValue(offset, out var priorStack))
			{
				var priorAggregateTypes = aggregateStates[offset];
				if (!TryMergeTypedStacks(
						priorStack,
						priorAggregateTypes,
						stack,
						aggregateTypes,
						out var mergedStack,
						out var mergedAggregateTypes))
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidEvaluationStack,
						$"Control-flow merge has incompatible evaluation-stack types: " +
						$"prior [{FormatTypedStack(priorStack, priorAggregateTypes)}], " +
						$"incoming [{FormatTypedStack(stack, aggregateTypes)}].",
						method.DisplayName,
						offset);
				}

				if (priorStack.SequenceEqual(mergedStack) &&
					priorAggregateTypes.SequenceEqual(mergedAggregateTypes))
				{
					continue;
				}

				states[offset] = mergedStack;
				aggregateStates[offset] = mergedAggregateTypes;
				stack = mergedStack;
				aggregateTypes = mergedAggregateTypes;
			}
			else
			{
				states.Add(offset, stack);
				aggregateStates.Add(offset, aggregateTypes);
			}

			if (!instructions.TryGetValue(offset, out var instruction))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Control flow targets invalid IL offset 0x{offset:X}.",
					method.DisplayName,
					offset);
			}
			if (optimizations is not null &&
				optimizations.TryGet(offset, out var suppressed) &&
				suppressed.Kind == CilOptimizationKind.EphemeralSpanScaffolding)
			{
				var nextOffset = method.Instructions[suppressed.EndIndex].NextOffset;
				if (instructions.ContainsKey(nextOffset))
				{
					work.Enqueue((nextOffset, stack, aggregateTypes));
				}
				continue;
			}

			ImmutableArray<CilStackValueKind> nextStack;
			ImmutableArray<CilAggregateStackType?> nextAggregateTypes;
			if (optimizations is not null &&
				optimizations.TryGetSpanFormat(offset, out var spanFormat))
			{
				var popSlots = spanFormat.ElementTypes.Count + 1;
				if (stack.Length < popSlots)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidEvaluationStack,
						"Ephemeral string.Format span lowering has insufficient stack values.",
						method.DisplayName,
						instruction.Offset);
				}
				nextStack = stack.RemoveRange(stack.Length - popSlots, popSlots)
					.Add(CilStackValueKind.Reference);
				nextAggregateTypes = aggregateTypes
					.RemoveRange(aggregateTypes.Length - popSlots, popSlots)
					.Add(null);
			}
			else
			{
				nextStack = ApplyStackEffect(method, module, instruction, stack);
				nextAggregateTypes = ApplyAggregateTypeEffect(
					method,
					module,
					instruction,
					stack,
					aggregateTypes,
					nextStack);
			}
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
						$"Return leaves {nextStack.Length} values on the evaluation stack " +
						$"({string.Join(", ", nextStack)}).",
						method.DisplayName,
						instruction.Offset);
				}

				continue;
			}

			if (IsUnconditionalBranch(instruction.OpCode))
			{
				EnqueueBranchTarget(work, instruction, nextStack, nextAggregateTypes);
				continue;
			}

			if (IsConditionalBranch(instruction.OpCode))
			{
				EnqueueBranchTarget(work, instruction, nextStack, nextAggregateTypes);
				EnqueueFallthrough(
					work,
					instructions,
					instruction,
					nextStack,
					nextAggregateTypes,
					method);
				continue;
			}

			if (instruction.OpCode == OpCodes.Switch)
			{
				foreach (var target in (int[])instruction.Operand!)
				{
					work.Enqueue((target, nextStack, nextAggregateTypes));
				}

				EnqueueFallthrough(
					work,
					instructions,
					instruction,
					nextStack,
					nextAggregateTypes,
					method);
				continue;
			}

			EnqueueFallthrough(
				work,
				instructions,
				instruction,
				nextStack,
				nextAggregateTypes,
				method);
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

		if (op == OpCodes.Ldftn)
		{
			module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
			return 1;
		}

		if (op == OpCodes.Ldvirtftn)
		{
			module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
			return 0;
		}

		if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
		{
			var target = module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
			var count = ParameterSlotCount(target.Signature.ParameterTypes) +
				(target.Signature.Header.IsInstance && op != OpCodes.Newobj
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
		if (op == OpCodes.Ldobj)
		{
			var type = module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return SlotCount(type) - 1;
		}
		if (op == OpCodes.Stobj || op == OpCodes.Cpobj)
		{
			module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return -2;
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
		op == OpCodes.Ldelem_I8 ||
		op == OpCodes.Ldelem_I ||
		op == OpCodes.Ldelem_Ref ||
		op == OpCodes.Ldelem ||
		op == OpCodes.Ldelema;

	private static bool IsArrayStore(OpCode op) =>
		op == OpCodes.Stelem ||
		op == OpCodes.Stelem_I1 ||
		op == OpCodes.Stelem_I2 ||
		op == OpCodes.Stelem_I4 ||
		op == OpCodes.Stelem_I8 ||
		op == OpCodes.Stelem_I ||
		op == OpCodes.Stelem_R4 ||
		op == OpCodes.Stelem_R8 ||
		op == OpCodes.Stelem_Ref;

	private static bool IsIndirectLoad(OpCode op) =>
		op == OpCodes.Ldind_I1 ||
		op == OpCodes.Ldind_U1 ||
		op == OpCodes.Ldind_I2 ||
		op == OpCodes.Ldind_U2 ||
		op == OpCodes.Ldind_I4 ||
		op == OpCodes.Ldind_U4 ||
		op == OpCodes.Ldind_I ||
		op == OpCodes.Ldind_I8 ||
		op == OpCodes.Ldind_R4 ||
		op == OpCodes.Ldind_Ref;

	private static bool IsIndirectStore(OpCode op) =>
		op == OpCodes.Stind_I1 ||
		op == OpCodes.Stind_I2 ||
		op == OpCodes.Stind_I4 ||
		op == OpCodes.Stind_I ||
		op == OpCodes.Stind_I8 ||
		op == OpCodes.Stind_R4 ||
		op == OpCodes.Stind_Ref;

	private static bool IsConversion(OpCode op) =>
		op == OpCodes.Conv_I ||
		op == OpCodes.Conv_U ||
		op == OpCodes.Conv_I4 ||
		op == OpCodes.Conv_Ovf_I4_Un ||
		op == OpCodes.Conv_U4 ||
		op == OpCodes.Conv_I8 ||
		op == OpCodes.Conv_U8 ||
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

	private static void EnqueueBranchTarget(
		Queue<(
			int Offset,
			ImmutableArray<CilStackValueKind> Stack,
			ImmutableArray<CilAggregateStackType?> AggregateTypes)> work,
		CilInstruction instruction,
		ImmutableArray<CilStackValueKind> stack,
		ImmutableArray<CilAggregateStackType?> aggregateTypes) =>
		work.Enqueue(((int)instruction.Operand!, stack, aggregateTypes));

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

	private static void EnqueueFallthrough(
		Queue<(
			int Offset,
			ImmutableArray<CilStackValueKind> Stack,
			ImmutableArray<CilAggregateStackType?> AggregateTypes)> work,
		IReadOnlyDictionary<int, CilInstruction> instructions,
		CilInstruction instruction,
		ImmutableArray<CilStackValueKind> stack,
		ImmutableArray<CilAggregateStackType?> aggregateTypes,
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

		work.Enqueue((instruction.NextOffset, stack, aggregateTypes));
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

		if (op == OpCodes.Ldc_R4)
		{
			return Push(stack, CilStackValueKind.Float32);
		}

		if (op == OpCodes.Ldc_R8)
		{
			return PushValue(stack, CilStackValueKind.Float64);
		}

		if (op == OpCodes.Ldnull || op == OpCodes.Ldstr)
		{
			return Push(stack, CilStackValueKind.Reference);
		}

		if (op == OpCodes.Ldtoken)
		{
			return Push(stack, CilStackValueKind.Int32);
		}

		if (op == OpCodes.Ldftn)
		{
			module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
			return Push(stack, CilStackValueKind.Int32);
		}

		if (op == OpCodes.Ldvirtftn)
		{
			module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
			return Push(Pop(method, instruction, stack, 1), CilStackValueKind.Int32);
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			return PushValue(stack, StackKindForParameter(module, method, argumentIndex));
		}

		if (TryGetLoadLocalIndex(instruction, out var loadLocal))
		{
			return PushValue(
				stack,
				StackKindForType(module, method.Locals[loadLocal], method.ModuleName));
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
			var popSlots = stack.Length != 0 && stack[^1] is
				CilStackValueKind.Int64 or CilStackValueKind.Float64
					? 2
					: 1;
			return Pop(method, instruction, stack, popSlots);
		}

		if (op == OpCodes.Ceq || op == OpCodes.Cgt || op == OpCodes.Cgt_Un ||
			op == OpCodes.Clt || op == OpCodes.Clt_Un)
		{
			var comparisonKind = stack.Length == 0 ? CilStackValueKind.Int32 : stack[^1];

			// Comparison results are observable managed booleans. Keep their
			// evaluation-stack representation 32-bit so merges with integer
			// constants remain ABI-compatible; compact nullable predicates use
			// BooleanByte independently until a consumer widens them.
			return Push(Pop(method, instruction, stack,
				comparisonKind is CilStackValueKind.Int64 or CilStackValueKind.Float64 ? 4 : 2), CilStackValueKind.Int32);
		}

		if (op == OpCodes.Add || op == OpCodes.Add_Ovf || op == OpCodes.Sub || op == OpCodes.And ||
			op == OpCodes.Or || op == OpCodes.Xor || op == OpCodes.Mul ||
			op == OpCodes.Mul_Ovf || op == OpCodes.Mul_Ovf_Un ||
			op == OpCodes.Div || op == OpCodes.Div_Un || op == OpCodes.Rem ||
			op == OpCodes.Rem_Un || op == OpCodes.Shl || op == OpCodes.Shr ||
			op == OpCodes.Shr_Un)
		{
			var arithmeticKind = stack.Length == 0 ? CilStackValueKind.Int32 : stack[^1];
			if (arithmeticKind == CilStackValueKind.Int64)
			{
				if (op != OpCodes.Add && op != OpCodes.Sub)
				{
					throw Unsupported(method, instruction, "64-bit arithmetic other than addition or subtraction");
				}
				return PushValue(
					Pop(method, instruction, stack, 4),
					CilStackValueKind.Int64);
			}
			if (op == OpCodes.Add_Ovf && arithmeticKind != CilStackValueKind.Int32)
			{
				throw Unsupported(method, instruction, "checked addition other than signed 32-bit integers");
			}
			if (arithmeticKind is CilStackValueKind.Float32 or CilStackValueKind.Float64 &&
				(op == OpCodes.And || op == OpCodes.Or || op == OpCodes.Xor ||
				 op == OpCodes.Shl || op == OpCodes.Shr || op == OpCodes.Shr_Un))
			{
				throw Unsupported(method, instruction, "floating-point bitwise arithmetic");
			}

			return PushValue(
				Pop(method, instruction, stack,
					arithmeticKind == CilStackValueKind.Float64 ? 4 : 2),
				TryGetNarrowArithmeticResult(method, instruction, stack, op, out var narrowResult)
					? narrowResult
					: arithmeticKind is CilStackValueKind.Float32 or CilStackValueKind.Float64
						? arithmeticKind
						: CilStackValueKind.Int32);
		}

		if (op == OpCodes.Neg || op == OpCodes.Not)
		{
			var unaryKind = stack.Length == 0 ? CilStackValueKind.Int32 : stack[^1];
			if (unaryKind == CilStackValueKind.Int64)
			{
				throw Unsupported(method, instruction, "64-bit arithmetic");
			}
			if (unaryKind is CilStackValueKind.Float32 or CilStackValueKind.Float64 && op == OpCodes.Not)
			{
				throw Unsupported(method, instruction, "floating-point bitwise complement");
			}

			return PushValue(
				Pop(method, instruction, stack, unaryKind == CilStackValueKind.Float64 ? 2 : 1),
				TryGetNarrowArithmeticResult(method, instruction, stack, op, out var narrowResult)
					? narrowResult
					: unaryKind);
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
			return Pop(
				method,
				instruction,
				stack,
				stack.Length != 0 && stack[^1] is
					CilStackValueKind.Int64 or CilStackValueKind.Float64
						? 4
						: 2);
		}

		if (op == OpCodes.Switch)
		{
			return Pop(method, instruction, stack, 1);
		}

		if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
		{
			var target = module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
				var count = ParameterSlotCount(target.Signature.ParameterTypes) +
					(target.Signature.Header.IsInstance && op != OpCodes.Newobj
						? 1
						: 0);
			var result = Pop(method, instruction, stack, count);
			if (op == OpCodes.Newobj)
			{
				var constructedValueType = target.ConstructedDeclaringType;
				if (constructedValueType is null && target.Definition is { Name: ".ctor" } definition)
				{
					var declaringType = module.GetMethodDeclaringType(definition);
					if (declaringType.Kind == CilTypeKind.ValueType)
						constructedValueType = declaringType;
				}
				if (constructedValueType is { Kind: CilTypeKind.ValueType })
					return PushValue(
						result,
						StackKindForType(module, constructedValueType, method.ModuleName));
				return Push(result, CilStackValueKind.Reference);
			}
			return target.Signature.ReturnType.IsVoid
				? result
				: PushValue(
					result,
					StackKindForType(
						module,
						target.Signature.ReturnType,
						target.Definition?.ModuleName ?? method.ModuleName));
		}

		if (op == OpCodes.Initobj)
		{
			return Pop(method, instruction, stack, 1);
		}
		if (op == OpCodes.Ldobj)
		{
			var type = module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return PushValue(
				Pop(method, instruction, stack, 1),
				StackKindForType(module, type, method.ModuleName));
		}
		if (op == OpCodes.Stobj || op == OpCodes.Cpobj)
		{
			module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return Pop(method, instruction, stack, 2);
		}

		if (op == OpCodes.Newarr)
		{
			return Push(Pop(method, instruction, stack, 1), CilStackValueKind.Reference);
		}

		if (op == OpCodes.Isinst || op == OpCodes.Castclass)
		{
			module.ResolveRuntimeTypeToken((int)instruction.Operand!, method, instruction.Offset);
			return Push(Pop(method, instruction, stack, 1), CilStackValueKind.Reference);
		}

		if (op == OpCodes.Box)
		{
			var type = module.ResolveTypeToken((int)instruction.Operand!, method, instruction.Offset);
			return Push(Pop(method, instruction, stack, SlotCount(type)), CilStackValueKind.Reference);
		}

		if (op == OpCodes.Unbox || op == OpCodes.Unbox_Any)
		{
			var type = module.ResolveTypeToken((int)instruction.Operand!, method, instruction.Offset);
			var result = Pop(method, instruction, stack, 1);
			return op == OpCodes.Unbox
				? Push(result, CilStackValueKind.ManagedPointer)
				: PushValue(result, StackKindForType(module, type, method.ModuleName));
		}

		if (op == OpCodes.Ldlen)
		{
			return Push(Pop(method, instruction, stack, 1), CilStackValueKind.Int32);
		}

		if (IsArrayLoad(op))
		{
			var result = Pop(method, instruction, stack, 2);
			return op == OpCodes.Ldelem
				? PushValue(
					result,
					StackKindForType(
						module,
						module.ResolveTypeToken(
							(int)instruction.Operand!,
							method,
							instruction.Offset),
						method.ModuleName))
				: PushValue(result, op == OpCodes.Ldelema
				? CilStackValueKind.ManagedPointer
				: op == OpCodes.Ldelem_Ref
					? CilStackValueKind.Reference
					: op == OpCodes.Ldelem_I8
						? CilStackValueKind.Int64
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
			var valueSlots = op == OpCodes.Stelem
				? SlotCount(module.ResolveTypeToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset))
				: op is var arrayStore &&
					(arrayStore == OpCodes.Stelem_I8 || arrayStore == OpCodes.Stelem_R8)
						? 2
						: 1;
			return Pop(method, instruction, stack, 2 + valueSlots);
		}

		if (IsIndirectLoad(op))
		{
			return PushValue(Pop(method, instruction, stack, 1),
				op == OpCodes.Ldind_Ref
					? CilStackValueKind.Reference
					: op == OpCodes.Ldind_R4
						? CilStackValueKind.Float32
					: op == OpCodes.Ldind_I8
						? CilStackValueKind.Int64
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
			return Pop(
				method,
				instruction,
				stack,
				op == OpCodes.Stind_I8 ? 3 : 2);
		}

		if (op == OpCodes.Ldfld || op == OpCodes.Ldflda ||
			op == OpCodes.Stfld || op == OpCodes.Ldsfld ||
			op == OpCodes.Ldsflda || op == OpCodes.Stsfld)
		{
			var field = module.ResolveFieldToken((int)instruction.Operand!, method, instruction.Offset);
			if (op == OpCodes.Ldsfld)
			{
				return PushValue(
					stack,
					StackKindForType(module, field.Type, field.ModuleName));
			}
			if (op == OpCodes.Ldsflda)
			{
				return Push(stack, CilStackValueKind.ManagedPointer);
			}
			if (op == OpCodes.Stsfld)
			{
				return Pop(
					method,
					instruction,
					stack,
					SlotCount(field.Type));
			}
			if (op == OpCodes.Ldfld)
			{
				return PushValue(
					Pop(method, instruction, stack, 1),
					StackKindForType(module, field.Type, field.ModuleName));
			}
			if (op == OpCodes.Ldflda)
			{
				return Push(Pop(method, instruction, stack, 1), CilStackValueKind.ManagedPointer);
			}
			return Pop(
				method,
				instruction,
				stack,
				1 + SlotCount(field.Type));
		}

		if (op == OpCodes.Ret)
		{
			return Pop(method, instruction, stack, SlotCount(method.Signature.ReturnType));
		}

		if (op == OpCodes.Localloc)
		{
			return Push(
				Pop(method, instruction, stack, 1),
				CilStackValueKind.ManagedPointer);
		}

		if (IsConversion(op))
		{
			var inputSlots = stack.Length != 0 && stack[^1] is
				CilStackValueKind.Int64 or CilStackValueKind.Float64
					? 2
					: 1;
			return PushValue(
				Pop(method, instruction, stack, inputSlots),
				StackKindForConversion(op));
		}

		throw Unsupported(method, instruction, $"typed stack effect for opcode '{op.Name}'");
	}

	private static ImmutableArray<CilAggregateStackType?> ApplyAggregateTypeEffect(
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		ImmutableArray<CilStackValueKind> currentStack,
		ImmutableArray<CilAggregateStackType?> currentAggregateTypes,
		ImmutableArray<CilStackValueKind> nextStack)
	{
		if (currentStack.Length != currentAggregateTypes.Length)
		{
			throw new InvalidOperationException(
				"Aggregate type state must stay aligned with evaluation-stack kinds.");
		}

		if (instruction.OpCode == OpCodes.Dup)
		{
			EnsureDepth(method, instruction, currentStack, 1);
			return currentAggregateTypes.Add(currentAggregateTypes[^1]);
		}

		if (instruction.OpCode == OpCodes.Stobj)
		{
			var type = module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			if (StackKindForType(module, type, method.ModuleName) ==
					CilStackValueKind.AggregateAddress)
			{
				EnsureDepth(method, instruction, currentStack, 2);
				var expected = CreateAggregateStackType(
					module,
					type,
					module.ResolveTypeTokenModuleName(
						(int)instruction.Operand!,
						method,
						instruction.Offset));
				if (currentStack[^1] != CilStackValueKind.AggregateAddress ||
					currentAggregateTypes[^1] != expected)
				{
					throw Unsupported(
						method,
						instruction,
						$"stobj source does not match '{type.DisplayName}' " +
						$"(expected module '{expected.ModuleName}', actual " +
						$"'{currentAggregateTypes[^1]?.ModuleName ?? "<none>"}', " +
						$"actual type '{currentAggregateTypes[^1]?.Type.DisplayName ?? "<none>"}')");
				}
			}
		}

		var popCount = GetPopSlotCount(method, module, instruction, currentStack);
		EnsureDepth(method, instruction, currentStack, popCount);
		var result = currentAggregateTypes.RemoveRange(
			currentAggregateTypes.Length - popCount,
			popCount);
		var pushCount = nextStack.Length - result.Length;
		if (pushCount < 0)
		{
			throw new InvalidOperationException(
				"Typed stack effect removed more aggregate type entries than stack kinds.");
		}

		if (pushCount != 0)
		{
			result = result.AddRange(
				Enumerable.Repeat<CilAggregateStackType?>(null, pushCount));
		}

		if (pushCount == 1 &&
			nextStack[^1] == CilStackValueKind.AggregateAddress)
		{
			if (!TryGetPushedAggregateType(
				method,
				module,
				instruction,
				out var aggregateType))
			{
				throw Unsupported(
					method,
					instruction,
					"aggregate value without an exact value-type identity");
			}

			result = result.SetItem(result.Length - 1, aggregateType);
		}

		return result;
	}

	private static bool TryGetPushedAggregateType(
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		out CilAggregateStackType aggregateType)
	{
		CilType? type = null;
		var moduleName = method.ModuleName;
		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			type = TypeForParameter(method, argumentIndex);
		}
		else if (TryGetLoadLocalIndex(instruction, out var localIndex))
		{
			type = method.Locals[localIndex];
		}
		else if (instruction.OpCode == OpCodes.Call ||
			instruction.OpCode == OpCodes.Callvirt)
		{
			var target = module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			type = target.Signature.ReturnType;
			moduleName = target.Definition?.ModuleName ?? method.ModuleName;
		}
		else if (instruction.OpCode == OpCodes.Newobj)
		{
			var target = module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			if (target.ConstructedDeclaringType is { Kind: CilTypeKind.ValueType } valueType)
			{
				type = valueType;
				moduleName = target.Definition?.ModuleName ?? method.ModuleName;
			}
			else if (target.Definition is { Name: ".ctor" } definition)
			{
				var declaringType = module.GetMethodDeclaringType(definition);
				if (declaringType.Kind == CilTypeKind.ValueType)
				{
					type = declaringType;
					moduleName = definition.ModuleName;
				}
			}
			else if (IsSpanValueConstructor(target.ImportName) ||
				IsMemoryValueConstructor(target.ImportName) ||
				target.ConstructedDeclaringType?.IsNullable == true)
			{
				type = target.ConstructedDeclaringType;
			}
			if (type is null)
				throw Unsupported(method, instruction,
					$"unclassified newobj target definition='{target.Definition?.DisplayName ?? "<none>"}', " +
					$"import='{target.ImportName ?? "<none>"}', constructed=" +
					$"'{target.ConstructedDeclaringType?.DisplayName ?? "<none>"}'");
		}
		else if (instruction.OpCode == OpCodes.Unbox_Any)
		{
			type = module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			moduleName = module.ResolveTypeTokenModuleName(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
		}
		else if (instruction.OpCode == OpCodes.Ldfld ||
			instruction.OpCode == OpCodes.Ldsfld)
		{
			var field = module.ResolveFieldToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			type = field.Type;
			moduleName = field.ModuleName;
		}
		else if (instruction.OpCode == OpCodes.Ldelem)
		{
			type = module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			moduleName = module.ResolveTypeTokenModuleName(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
		}
		else if (instruction.OpCode == OpCodes.Ldobj)
		{
			type = module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			moduleName = module.ResolveTypeTokenModuleName(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
		}

		if (type is not null &&
			StackKindForType(module, type, moduleName) ==
				CilStackValueKind.AggregateAddress)
		{
			aggregateType = CreateAggregateStackType(module, type, moduleName);
			return true;
		}

		aggregateType = default;
		return false;
	}

	private static CilAggregateStackType CreateAggregateStackType(
		CompilationModule module,
		CilType type,
		string preferredModuleName)
	{
		var identity = module.ResolveRuntimeTypeIdentity(type, preferredModuleName);
		return new CilAggregateStackType(
			identity.Handle.IsNil ? preferredModuleName : identity.ModuleName,
			type);
	}

	private static bool IsSpanByrefConstructor(string? importName) =>
		importName?.StartsWith(
			"intrinsic:span-from-ref:",
			StringComparison.Ordinal) == true ||
		importName?.StartsWith(
			"intrinsic:readonly-span-from-ref:",
			StringComparison.Ordinal) == true;

	private static bool IsSpanValueConstructor(string? importName) =>
		IsSpanByrefConstructor(importName) ||
		importName?.StartsWith(
			"intrinsic:span-from-pointer:",
			StringComparison.Ordinal) == true;

	private static bool IsMemoryValueConstructor(string? importName) =>
		importName?.StartsWith(
			"intrinsic:memory-from-array",
			StringComparison.Ordinal) == true ||
		importName?.StartsWith(
			"intrinsic:readonly-memory-from-array",
			StringComparison.Ordinal) == true;

	private static string FormatTypedStack(
		ImmutableArray<CilStackValueKind> stack,
		ImmutableArray<CilAggregateStackType?> aggregateTypes)
	{
		var entries = new string[stack.Length];
		for (var index = 0; index < stack.Length; index++)
		{
			entries[index] = aggregateTypes[index] is { } aggregateType
				? $"{stack[index]}<{aggregateType.ModuleName}:{aggregateType.Type.DisplayName}>"
				: stack[index].ToString();
		}

		return string.Join(",", entries);
	}

	private static bool TryMergeTypedStacks(
		ImmutableArray<CilStackValueKind> first,
		ImmutableArray<CilAggregateStackType?> firstAggregateTypes,
		ImmutableArray<CilStackValueKind> second,
		ImmutableArray<CilAggregateStackType?> secondAggregateTypes,
		out ImmutableArray<CilStackValueKind> merged,
		out ImmutableArray<CilAggregateStackType?> mergedAggregateTypes)
	{
		merged = default;
		mergedAggregateTypes = default;
		if (first.Length != second.Length ||
			firstAggregateTypes.Length != first.Length ||
			secondAggregateTypes.Length != second.Length)
		{
			return false;
		}

		var kinds = first.ToBuilder();
		var aggregates = firstAggregateTypes.ToBuilder();
		for (var index = 0; index < first.Length; index++)
		{
			if (first[index] == second[index] &&
				firstAggregateTypes[index] == secondAggregateTypes[index])
			{
				continue;
			}
			if (firstAggregateTypes[index] is null &&
				secondAggregateTypes[index] is null &&
				IsInt32StackKind(first[index]) &&
				IsInt32StackKind(second[index]))
			{
				kinds[index] = CilStackValueKind.Int32;
				aggregates[index] = null;
				continue;
			}
			return false;
		}

		merged = kinds.ToImmutable();
		mergedAggregateTypes = aggregates.ToImmutable();
		return true;
	}

	private static bool IsInt32StackKind(CilStackValueKind kind) =>
		kind is CilStackValueKind.Int32 or
			CilStackValueKind.BooleanByte or
			CilStackValueKind.SignedByte or
			CilStackValueKind.UnsignedByte or
			CilStackValueKind.SignedWord or
			CilStackValueKind.UnsignedWord;

	internal static int GetPopSlotCount(
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		ImmutableArray<CilStackValueKind> currentStack)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
		{
			var target = module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
				return ParameterSlotCount(target.Signature.ParameterTypes) +
					(target.Signature.Header.IsInstance && op != OpCodes.Newobj
						? 1
						: 0);
		}

		if (op == OpCodes.Initobj)
		{
			return 1;
		}
		if (op == OpCodes.Ldobj)
		{
			module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return 1;
		}
		if (op == OpCodes.Stobj || op == OpCodes.Cpobj)
		{
			module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return 2;
		}
		if (op == OpCodes.Box)
		{
			return SlotCount(module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset));
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
		if (op == OpCodes.Pop &&
			currentStack.Length != 0 &&
			currentStack[^1] is CilStackValueKind.Int64 or CilStackValueKind.Float64)
		{
			return 2;
		}
		if (op == OpCodes.Stind_I8)
		{
			return 3;
		}
		if (op == OpCodes.Stfld || op == OpCodes.Stsfld)
		{
			var field = module.ResolveFieldToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return (op == OpCodes.Stfld ? 1 : 0) +
				SlotCount(field.Type);
		}
		if (IsArrayStore(op))
		{
			var valueSlots = op == OpCodes.Stelem
				? SlotCount(module.ResolveTypeToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset))
				: op == OpCodes.Stelem_I8 || op == OpCodes.Stelem_R8
					? 2
					: 1;
			return 2 + valueSlots;
		}
		if (op == OpCodes.Rethrow || op == OpCodes.Endfinally)
		{
			return 0;
		}
		if (op == OpCodes.Leave || op == OpCodes.Leave_S)
		{
			return currentStack.Length;
		}
		if (IsConversion(op) &&
			currentStack.Length != 0 &&
			currentStack[^1] is CilStackValueKind.Int64 or CilStackValueKind.Float64)
		{
			return 2;
		}
		if (IsRelationalBranch(op) &&
			currentStack.Length != 0 &&
			currentStack[^1] is CilStackValueKind.Int64 or CilStackValueKind.Float64)
		{
			return 4;
		}
		if ((op == OpCodes.Add || op == OpCodes.Sub || op == OpCodes.Mul ||
			op == OpCodes.Div || op == OpCodes.Div_Un || op == OpCodes.Rem ||
			op == OpCodes.Rem_Un || op == OpCodes.Ceq || op == OpCodes.Cgt ||
			op == OpCodes.Cgt_Un || op == OpCodes.Clt || op == OpCodes.Clt_Un) &&
			currentStack.Length != 0 && currentStack[^1] is
				CilStackValueKind.Int64 or CilStackValueKind.Float64)
		{
			return 4;
		}
		if ((op == OpCodes.Neg || op == OpCodes.Not) &&
			currentStack.Length != 0 && currentStack[^1] == CilStackValueKind.Float64)
		{
			return 2;
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
		kind is CilStackValueKind.Int64 or CilStackValueKind.Float64
			? stack.Add(kind).Add(kind)
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

		var parameter = method.Signature.ParameterTypes[index];
		return module.IsTransparentScalarType(parameter)
			? CilStackValueKind.ManagedPointer
			: StackKindForType(module, parameter, method.ModuleName);
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
		op == OpCodes.Conv_I8 || op == OpCodes.Conv_U8
			? CilStackValueKind.Int64
			: TryGetNarrowConversionKind(op, out var kind)
			? kind
			: CilStackValueKind.Int32;

	internal static CilStackValueKind StackKindForType(CilType type) =>
		type.IsFloatingPoint
			? type.Size == 8 ? CilStackValueKind.Float64 : CilStackValueKind.Float32
		: type.Size == 8 && type.IsSupportedScalar
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

	private static CilStackValueKind StackKindForType(
		CompilationModule module,
		CilType type,
		string moduleName) =>
		!type.IsSupportedScalar &&
		!module.IsTransparentScalarType(type) &&
		(module.TryGetReferenceFreeStructLayout(type, moduleName, out var layout) ||
		 module.TryGetStructLayout(type, moduleName, out layout)) &&
		layout.Size > 4
			? CilStackValueKind.AggregateAddress
			: StackKindForType(type);

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
