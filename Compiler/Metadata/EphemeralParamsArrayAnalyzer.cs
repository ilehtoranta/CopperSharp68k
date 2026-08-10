/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;

namespace CopperSharp.Compiler.Metadata;

/// <summary>
/// Describes the canonical C# lowering of a fresh params array which is filled
/// exactly once and consumed immediately by the following call.
/// </summary>
internal sealed record EphemeralParamsArray(
	int ArrayStartOffset,
	int CallOffset,
	IReadOnlyList<CilType> ElementTypes);

internal sealed record EphemeralSpanParams(
	int CallOffset,
	int InlineArrayLocal,
	IReadOnlyList<CilType> ElementTypes,
	IReadOnlyList<(int StartIndex, int EndIndex)> SuppressedRanges,
	IReadOnlySet<int> SuppressedCallOffsets);

internal static class EphemeralParamsArrayAnalyzer
{
	public static EphemeralSpanParams? AnalyzeReadOnlySpanParams(
		CompilationModule module,
		CilMethod caller,
		int callOffset)
	{
		var instructions = caller.Instructions;
		var callIndex = IndexOfOffset(instructions, callOffset);
		if (callIndex < 4 ||
			instructions[callIndex].OpCode != OpCodes.Call ||
			module.DescribeMethodToken(
				(int)instructions[callIndex].Operand!,
				caller,
				callOffset) is not
				{
					AssemblyName: "System.Runtime",
					TypeName: "System.String",
					Name: "Format",
					ParameterTypes: var formatParameters
				} ||
			formatParameters.Length != 2 ||
			formatParameters[0] is not ("string" or "System.String") ||
			formatParameters[1] != "System.ReadOnlySpan`1<object>" ||
			instructions[callIndex - 1].OpCode != OpCodes.Call ||
			!IsCompilerInlineArrayHelper(
				module,
				caller,
				instructions[callIndex - 1],
				"InlineArrayAsReadOnlySpan") ||
			!TryGetConstant(instructions[callIndex - 2], out var length) ||
			length is < 1 or > 8 ||
			!TryGetLocalAddressIndex(
				instructions[callIndex - 3],
				out var inlineArrayLocal) ||
			(uint)inlineArrayLocal >= (uint)caller.Locals.Length ||
			!IsInlineObjectArrayLocal(caller.Locals[inlineArrayLocal], length))
		{
			return null;
		}

		var ranges = new List<(int StartIndex, int EndIndex)>
		{
			(callIndex - 3, callIndex - 1)
		};
		var suppressedCalls = new HashSet<int>
		{
			instructions[callIndex - 1].Offset
		};
		var elementTypes = new CilType[length];
		var cursor = callIndex - 3;
		for (var expected = length - 1; expected >= 0; expected--)
		{
			if (cursor < 2 ||
				instructions[cursor - 1].OpCode != OpCodes.Stind_Ref ||
				instructions[cursor - 2].OpCode != OpCodes.Box)
			{
				return null;
			}
			var boxedType = module.ResolveTypeToken(
				(int)instructions[cursor - 2].Operand!,
				caller,
				instructions[cursor - 2].Offset);
			if (!IsAdmittedInteger(boxedType))
			{
				return null;
			}
			elementTypes[expected] = boxedType;
			ranges.Add((cursor - 2, cursor - 1));

			var elementAddress = -1;
			for (var index = cursor - 3; index >= 2; index--)
			{
				if (instructions[index].OpCode == OpCodes.Call &&
					IsCompilerInlineArrayHelper(
						module,
						caller,
						instructions[index],
						"InlineArrayElementRef") &&
					TryGetConstant(instructions[index - 1], out var elementIndex) &&
					elementIndex == expected &&
					TryGetLocalAddressIndex(
						instructions[index - 2],
						out var candidateLocal) &&
					candidateLocal == inlineArrayLocal)
				{
					elementAddress = index - 2;
					ranges.Add((index - 2, index));
					suppressedCalls.Add(instructions[index].Offset);
					break;
				}
			}
			if (elementAddress < 0)
			{
				return null;
			}
			cursor = elementAddress;
		}

		if (cursor < 2 ||
			instructions[cursor - 1].OpCode != OpCodes.Initobj ||
			!TryGetLocalAddressIndex(
				instructions[cursor - 2],
				out var initializedLocal) ||
			initializedLocal != inlineArrayLocal)
		{
			return null;
		}
		ranges.Add((cursor - 2, cursor - 1));
		var admittedInstructionIndices = ranges
			.SelectMany(static range => Enumerable.Range(
				range.StartIndex,
				range.EndIndex - range.StartIndex + 1))
			.ToHashSet();
		for (var index = 0; index < instructions.Count; index++)
		{
			if (!admittedInstructionIndices.Contains(index) &&
				TryGetLocalIndex(instructions[index], out var usedLocal) &&
				usedLocal == inlineArrayLocal)
			{
				return null;
			}
		}
		var firstOffset = instructions[cursor - 2].Offset;
		if (HasControlFlowEntry(caller, firstOffset, callOffset))
		{
			return null;
		}

		return new EphemeralSpanParams(
			callOffset,
			inlineArrayLocal,
			elementTypes,
			ranges.OrderBy(static range => range.StartIndex).ToArray(),
			suppressedCalls);
	}

	public static IReadOnlyList<CilType>? AnalyzeImmediateBoxedArguments(
		CompilationModule module,
		CilMethod caller,
		int callOffset,
		int argumentCount)
	{
		if (argumentCount is < 1 or > 8 || caller.Instructions.Count == 0)
		{
			return null;
		}

		var instructions = caller.Instructions.ToDictionary(
			static instruction => instruction.Offset);
		if (!instructions.ContainsKey(callOffset))
		{
			return null;
		}

		var states = new Dictionary<int, IReadOnlyList<BoxedValue?>>();
		var work = new Queue<int>();
		Enqueue(caller.Instructions[0].Offset, []);
		foreach (var region in caller.ExceptionRegions)
		{
			Enqueue(
				region.HandlerOffset,
				region.IsCatch ? [null] : []);
			if (region.FilterOffset >= 0)
			{
				Enqueue(region.FilterOffset, [null]);
			}
		}

		while (work.Count != 0)
		{
			var offset = work.Dequeue();
			if (offset == callOffset)
			{
				continue;
			}
			var instruction = instructions[offset];
			var next = TransferBoxedValues(
				module,
				caller,
				instruction,
				states[offset]);
			foreach (var successor in Successors(instructions, instruction))
			{
				Enqueue(successor, next);
			}
		}

		if (!states.TryGetValue(callOffset, out var callState) ||
			callState.Count < argumentCount)
		{
			return null;
		}
		var boxed = callState.Skip(callState.Count - argumentCount).ToArray();
		if (boxed.Any(static value => value is null))
		{
			return null;
		}
		var firstBoxOffset = boxed.Min(static value => value!.BoxOffset);
		if (HasControlFlowEntry(caller, firstBoxOffset, callOffset))
		{
			return null;
		}
		return boxed.Select(static value => value!.Type).ToArray();

		void Enqueue(int offset, IReadOnlyList<BoxedValue?> incoming)
		{
			if (!instructions.ContainsKey(offset))
			{
				return;
			}
			if (!states.TryGetValue(offset, out var current))
			{
				states.Add(offset, incoming.ToArray());
				work.Enqueue(offset);
				return;
			}
			var joined = Join(current, incoming);
			if (!joined.SequenceEqual(current))
			{
				states[offset] = joined;
				work.Enqueue(offset);
			}
		}
	}

	public static EphemeralParamsArray? AnalyzeBoxedValueArray(
		CompilationModule module,
		CilMethod caller,
		int callOffset,
		int minimumLength,
		int maximumLength)
	{
		var instructions = caller.Instructions;
		var callIndex = IndexOfOffset(instructions, callOffset);
		if (callIndex < 0 || minimumLength < 0 || maximumLength < minimumLength)
		{
			return null;
		}

		var branchTargets = GetBranchTargets(instructions);
		for (var start = callIndex - 2; start >= 1; start--)
		{
			if (!TryGetConstant(instructions[start], out var length) ||
				length < minimumLength || length > maximumLength ||
				start + 1 >= callIndex ||
				instructions[start + 1].OpCode != OpCodes.Newarr ||
				module.ResolveTypeToken(
					(int)instructions[start + 1].Operand!,
					caller,
					instructions[start + 1].Offset).DisplayName != "object" ||
				branchTargets.Any(target =>
					target >= instructions[start].Offset && target <= callOffset))
			{
				continue;
			}

			var elementTypes = new List<CilType>(length);
			var index = start + 2;
			var matched = true;
			for (var expected = 0; expected < length; expected++)
			{
				if (index + 3 >= callIndex ||
					instructions[index].OpCode != OpCodes.Dup ||
					!TryGetConstant(instructions[index + 1], out var actual) ||
					actual != expected)
				{
					matched = false;
					break;
				}

				var store = index + 2;
				while (store < callIndex &&
					instructions[store].OpCode != OpCodes.Stelem_Ref)
				{
					if (IsControlFlow(instructions[store].OpCode) ||
						instructions[store].OpCode is var op &&
						(op == OpCodes.Newarr || op == OpCodes.Stelem ||
						 op == OpCodes.Stelem_Ref))
					{
						matched = false;
						break;
					}
					store++;
				}
				if (!matched || store >= callIndex || store == index + 2 ||
					instructions[store - 1].OpCode != OpCodes.Box)
				{
					matched = false;
					break;
				}

				var elementType = module.ResolveTypeToken(
					(int)instructions[store - 1].Operand!,
					caller,
					instructions[store - 1].Offset);
				if (!IsAdmittedInteger(elementType))
				{
					matched = false;
					break;
				}
				elementTypes.Add(elementType);
				index = store + 1;
			}

			if (matched && index == callIndex)
			{
				return new EphemeralParamsArray(
					instructions[start].Offset,
					callOffset,
					elementTypes);
			}
		}

		return null;
	}

	private static bool IsAdmittedInteger(CilType type) =>
		type.DisplayName is "int" or "System.Int32";

	private static bool IsInlineObjectArrayLocal(CilType type, int length) =>
		type.DisplayName.StartsWith(
			$"System.Runtime.CompilerServices.InlineArray{length}`1<",
			StringComparison.Ordinal) &&
		type.DisplayName.EndsWith("object>", StringComparison.Ordinal);

	private static bool TryGetLocalAddressIndex(
		CilInstruction instruction,
		out int index)
	{
		if (instruction.OpCode == OpCodes.Ldloca ||
			instruction.OpCode == OpCodes.Ldloca_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}
		index = default;
		return false;
	}

	private static bool TryGetLocalIndex(
		CilInstruction instruction,
		out int index)
	{
		if (TryGetLocalAddressIndex(instruction, out index))
		{
			return true;
		}
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldloc_0.Value && op.Value <= OpCodes.Ldloc_3.Value)
		{
			index = op.Value - OpCodes.Ldloc_0.Value;
			return true;
		}
		if (op.Value >= OpCodes.Stloc_0.Value && op.Value <= OpCodes.Stloc_3.Value)
		{
			index = op.Value - OpCodes.Stloc_0.Value;
			return true;
		}
		if (op == OpCodes.Ldloc || op == OpCodes.Ldloc_S ||
			op == OpCodes.Stloc || op == OpCodes.Stloc_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}
		index = default;
		return false;
	}

	private static bool IsCompilerInlineArrayHelper(
		CompilationModule module,
		CilMethod caller,
		CilInstruction instruction,
		string name) =>
		instruction.Operand is int token &&
		module.DescribeMethodTokenName(
			token,
			caller,
			instruction.Offset) == name;

	private static IReadOnlyList<BoxedValue?> TransferBoxedValues(
		CompilationModule module,
		CilMethod caller,
		CilInstruction instruction,
		IReadOnlyList<BoxedValue?> incoming)
	{
		var stack = incoming.ToList();
		if (instruction.OpCode == OpCodes.Dup)
		{
			_ = Pop(stack);
			// A duplicated boxed reference is observably aliased and therefore
			// cannot be removed by the immediate-call optimization.
			stack.Add(null);
			stack.Add(null);
			return stack;
		}
		if (instruction.OpCode == OpCodes.Box)
		{
			_ = Pop(stack);
			var type = module.ResolveTypeToken(
				(int)instruction.Operand!,
				caller,
				instruction.Offset);
			stack.Add(IsAdmittedInteger(type)
				? new BoxedValue(type, instruction.Offset)
				: null);
			return stack;
		}

		var popCount = PopCount(module, caller, instruction);
		for (var index = 0; index < popCount; index++)
		{
			_ = Pop(stack);
		}
		if (instruction.OpCode is var op &&
			(op == OpCodes.Leave || op == OpCodes.Leave_S))
		{
			stack.Clear();
		}
		var pushCount = PushCount(module, caller, instruction);
		for (var index = 0; index < pushCount; index++)
		{
			stack.Add(null);
		}
		return stack;
	}

	private static int PopCount(
		CompilationModule module,
		CilMethod caller,
		CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
		{
			var target = module.ResolveMethodToken(
				(int)instruction.Operand!,
				caller,
				instruction.Offset);
			return target.Signature.ParameterTypes.Length +
				(target.Signature.Header.IsInstance && op != OpCodes.Newobj ? 1 : 0);
		}
		if (op == OpCodes.Ret)
		{
			return caller.Signature.ReturnType.IsVoid ? 0 : 1;
		}
		if (op == OpCodes.Leave || op == OpCodes.Leave_S ||
			op == OpCodes.Rethrow || op == OpCodes.Endfinally)
		{
			return 0;
		}
		return op.StackBehaviourPop switch
		{
			StackBehaviour.Pop0 => 0,
			StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
			StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or
				StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8 or
				StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or
				StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
			StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_pop1 or
				StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or
				StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or
				StackBehaviour.Popref_popi_popref => 3,
			_ => 0
		};
	}

	private static int PushCount(
		CompilationModule module,
		CilMethod caller,
		CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
		{
			if (op == OpCodes.Newobj)
			{
				return 1;
			}
			var target = module.ResolveMethodToken(
				(int)instruction.Operand!,
				caller,
				instruction.Offset);
			return target.Signature.ReturnType.IsVoid ? 0 : 1;
		}
		return op.StackBehaviourPush switch
		{
			StackBehaviour.Push0 => 0,
			StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or
				StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
			StackBehaviour.Push1_push1 => 2,
			_ => 0
		};
	}

	private static IEnumerable<int> Successors(
		IReadOnlyDictionary<int, CilInstruction> instructions,
		CilInstruction instruction)
	{
		if (instruction.OpCode == OpCodes.Switch)
		{
			foreach (var target in (int[])instruction.Operand!)
			{
				yield return target;
			}
			if (instructions.ContainsKey(instruction.NextOffset))
			{
				yield return instruction.NextOffset;
			}
			yield break;
		}
		if (instruction.OpCode.FlowControl == FlowControl.Branch)
		{
			if (instruction.Operand is int target)
			{
				yield return target;
			}
			yield break;
		}
		if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
		{
			if (instruction.Operand is int target)
			{
				yield return target;
			}
			if (instructions.ContainsKey(instruction.NextOffset))
			{
				yield return instruction.NextOffset;
			}
			yield break;
		}
		if (instruction.OpCode.FlowControl is FlowControl.Return or FlowControl.Throw ||
			instruction.OpCode == OpCodes.Endfinally)
		{
			yield break;
		}
		if (instructions.ContainsKey(instruction.NextOffset))
		{
			yield return instruction.NextOffset;
		}
	}

	private static IReadOnlyList<BoxedValue?> Join(
		IReadOnlyList<BoxedValue?> left,
		IReadOnlyList<BoxedValue?> right)
	{
		if (left.Count != right.Count)
		{
			return Enumerable.Repeat<BoxedValue?>(
				null,
				Math.Min(left.Count, right.Count)).ToArray();
		}
		return left.Zip(
			right,
			static (first, second) => first == second ? first : null).ToArray();
	}

	private static BoxedValue? Pop(List<BoxedValue?> stack)
	{
		if (stack.Count == 0)
		{
			return null;
		}
		var value = stack[^1];
		stack.RemoveAt(stack.Count - 1);
		return value;
	}

	private static bool HasControlFlowEntry(
		CilMethod caller,
		int firstBoxOffset,
		int callOffset)
	{
		var suffixOffsets = caller.Instructions
			.Where(instruction => instruction.Offset > firstBoxOffset &&
				instruction.Offset <= callOffset)
			.Select(static instruction => instruction.Offset)
			.ToHashSet();
		foreach (var instruction in caller.Instructions)
		{
			if (instruction.Operand is int target &&
				instruction.OpCode.OperandType is
					OperandType.InlineBrTarget or OperandType.ShortInlineBrTarget &&
				suffixOffsets.Contains(target) ||
				instruction.Operand is int[] targets && targets.Any(suffixOffsets.Contains))
			{
				return true;
			}
		}
		return caller.ExceptionRegions.Any(region =>
			suffixOffsets.Contains(region.HandlerOffset) ||
			(region.FilterOffset >= 0 && suffixOffsets.Contains(region.FilterOffset)));
	}

	private sealed record BoxedValue(CilType Type, int BoxOffset);

	private static int IndexOfOffset(
		IReadOnlyList<CilInstruction> instructions,
		int offset)
	{
		for (var index = 0; index < instructions.Count; index++)
		{
			if (instructions[index].Offset == offset)
			{
				return index;
			}
		}
		return -1;
	}

	private static HashSet<int> GetBranchTargets(
		IReadOnlyList<CilInstruction> instructions)
	{
		var result = new HashSet<int>();
		foreach (var instruction in instructions)
		{
			if (instruction.Operand is int target &&
				instruction.OpCode.OperandType is OperandType.InlineBrTarget or
					OperandType.ShortInlineBrTarget)
			{
				result.Add(target);
			}
			else if (instruction.Operand is int[] targets)
			{
				result.UnionWith(targets);
			}
		}
		return result;
	}

	private static bool IsControlFlow(OpCode opCode) =>
		opCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or
			FlowControl.Return or FlowControl.Throw;

	private static bool TryGetConstant(CilInstruction instruction, out int value)
	{
		value = instruction.OpCode.Value switch
		{
			var op when op == OpCodes.Ldc_I4_M1.Value => -1,
			var op when op == OpCodes.Ldc_I4_0.Value => 0,
			var op when op == OpCodes.Ldc_I4_1.Value => 1,
			var op when op == OpCodes.Ldc_I4_2.Value => 2,
			var op when op == OpCodes.Ldc_I4_3.Value => 3,
			var op when op == OpCodes.Ldc_I4_4.Value => 4,
			var op when op == OpCodes.Ldc_I4_5.Value => 5,
			var op when op == OpCodes.Ldc_I4_6.Value => 6,
			var op when op == OpCodes.Ldc_I4_7.Value => 7,
			var op when op == OpCodes.Ldc_I4_8.Value => 8,
			var op when op == OpCodes.Ldc_I4_S.Value => (sbyte)instruction.Operand!,
			var op when op == OpCodes.Ldc_I4.Value => (int)instruction.Operand!,
			_ => int.MinValue
		};
		return value != int.MinValue;
	}
}
