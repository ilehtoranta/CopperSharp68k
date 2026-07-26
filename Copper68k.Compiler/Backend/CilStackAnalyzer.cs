using System.Reflection.Emit;
using Copper68k.Compiler.Metadata;

namespace Copper68k.Compiler.Backend;

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
			var nextDepth = checked(depth + GetStackDelta(method, module, instruction));
			if (nextDepth < 0)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidEvaluationStack,
					"Instruction pops more values than are present.",
					method.DisplayName,
					instruction.Offset);
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

	private static int GetStackDelta(
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
		{
			var target = module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
			var count = target.Signature.ParameterTypes.Length +
				(target.Signature.Header.IsInstance && op != OpCodes.Newobj ? 1 : 0);
			var pushes = op == OpCodes.Newobj || !target.Signature.ReturnType.IsVoid ? 1 : 0;
			return pushes - count;
		}

		if (op == OpCodes.Ret)
		{
			return method.Signature.ReturnType.IsVoid ? 0 : -1;
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

	private static void EnqueueBranchTarget(
		Queue<(int Offset, int Depth)> work,
		CilInstruction instruction,
		int depth) =>
		work.Enqueue(((int)instruction.Operand!, depth));

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

	private static M68kCompilationException Unsupported(
		CilMethod method,
		CilInstruction instruction,
		string detail) =>
		new(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"Opcode '{instruction.OpCode.Name}' has unsupported {detail}.",
			method.DisplayName,
			instruction.Offset);
}
