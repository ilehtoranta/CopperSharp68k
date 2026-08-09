/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;

namespace CopperSharp.Compiler.Metadata;

internal enum EnumerableSourceProvenance
{
	Unknown,
	Null,
	Array,
	DictionaryValues,
	DictionaryUInt32Values,
	OrderedPrimary,
	OrderedPrimarySecondary,
	OrderedEnumerator,
	Range,
	Repeat,
	RangeSelect,
	RangeWhere,
	RangeSelectWhere,
	RangeWhereTake,
	RangeSelectWhereTake
}

/// <summary>
/// Closed-world must analysis for the private LINQ iterator families admitted by
/// the compact profile. Unknown input poisons a merge, while matching factory
/// families survive evaluation-stack, local, and control-flow propagation.
/// </summary>
internal static class EnumerableSourceProvenanceAnalyzer
{
	public static EnumerableSourceProvenance Analyze(
		CompilationModule module,
		CilMethod method,
		int callOffset,
		int argumentFromTop = 0)
	{
		var instructions = method.Instructions.ToDictionary(
			static instruction => instruction.Offset);
		if (!instructions.ContainsKey(callOffset) || method.Instructions.Count == 0)
		{
			return EnumerableSourceProvenance.Unknown;
		}

		var states = new Dictionary<int, State>();
		var work = new Queue<int>();
		Enqueue(
			method.Instructions[0].Offset,
			new State([], new EnumerableSourceProvenance[method.Locals.Length]));
		foreach (var region in method.ExceptionRegions)
		{
			Enqueue(
				region.HandlerOffset,
				new State(
					region.IsCatch ? [EnumerableSourceProvenance.Unknown] : [],
					new EnumerableSourceProvenance[method.Locals.Length]));
			if (region.FilterOffset >= 0)
			{
				Enqueue(
					region.FilterOffset,
					new State(
						[EnumerableSourceProvenance.Unknown],
						new EnumerableSourceProvenance[method.Locals.Length]));
			}
		}

		while (work.Count != 0)
		{
			var offset = work.Dequeue();
			var instruction = instructions[offset];
			var next = Transfer(module, method, instruction, states[offset]);
			foreach (var successor in Successors(method, instructions, instruction))
			{
				Enqueue(successor, next);
			}
		}

		return states.TryGetValue(callOffset, out var callState) &&
			callState.Stack.Count > argumentFromTop
				? callState.Stack[^(argumentFromTop + 1)]
				: EnumerableSourceProvenance.Unknown;

		void Enqueue(int offset, State incoming)
		{
			if (!instructions.ContainsKey(offset))
			{
				return;
			}
			if (!states.TryGetValue(offset, out var current))
			{
				states.Add(offset, incoming.Clone());
				work.Enqueue(offset);
				return;
			}

			var joined = Join(current, incoming);
			if (!joined.Equals(current))
			{
				states[offset] = joined;
				work.Enqueue(offset);
			}
		}
	}

	private static State Transfer(
		CompilationModule module,
		CilMethod method,
		CilInstruction instruction,
		State incoming)
	{
		var state = incoming.Clone();
		if (TryGetLoadLocalIndex(instruction, out var loadLocal))
		{
			state.Stack.Add(state.Locals[loadLocal]);
			return state;
		}
		if (TryGetStoreLocalIndex(instruction, out var storeLocal))
		{
			state.Locals[storeLocal] = Pop(state.Stack);
			return state;
		}
		if (instruction.OpCode == OpCodes.Dup)
		{
			state.Stack.Add(
				state.Stack.Count == 0
					? EnumerableSourceProvenance.Unknown
					: state.Stack[^1]);
			return state;
		}
		if (instruction.OpCode == OpCodes.Ldnull)
		{
			state.Stack.Add(EnumerableSourceProvenance.Null);
			return state;
		}

		var callProvenance = FactoryProvenance(
			module,
			method,
			instruction,
			state.Stack);
		var popCount = PopCount(module, method, instruction);
		for (var index = 0; index < popCount; index++)
		{
			Pop(state.Stack);
		}
		if (instruction.OpCode is { } op &&
			(op == OpCodes.Leave || op == OpCodes.Leave_S))
		{
			state.Stack.Clear();
		}

		var pushed = PushCount(module, method, instruction);
		for (var index = 0; index < pushed; index++)
		{
			state.Stack.Add(
				index == pushed - 1
					? callProvenance
					: EnumerableSourceProvenance.Unknown);
		}
		return state;
	}

	private static EnumerableSourceProvenance FactoryProvenance(
		CompilationModule module,
		CilMethod method,
		CilInstruction instruction,
		IReadOnlyList<EnumerableSourceProvenance> stack)
	{
		if (instruction.OpCode == OpCodes.Newarr)
		{
			return EnumerableSourceProvenance.Array;
		}
		var call = instruction.OpCode;
		if ((call != OpCodes.Call && call != OpCodes.Callvirt) ||
			instruction.Operand is not int token)
		{
			return EnumerableSourceProvenance.Unknown;
		}
		var identity = module.DescribeMethodToken(token, method, instruction.Offset);
		var returnsArray = identity?.ReturnType.EndsWith("[]", StringComparison.Ordinal) == true ||
			identity is null &&
			module.ResolveMethodToken(token, method, instruction.Offset).Signature.ReturnType is
				{ ElementType: not null, DisplayName: var returnTypeName } &&
			returnTypeName.EndsWith("[]", StringComparison.Ordinal);
		return returnsArray
			? EnumerableSourceProvenance.Array
			: identity is
				{
					TypeName: var otherDictionaryType,
					Name: "get_Values"
				} && otherDictionaryType.StartsWith(
					"System.Collections.Generic.Dictionary`2<uint,",
					StringComparison.Ordinal)
				? EnumerableSourceProvenance.DictionaryUInt32Values
			: identity is
				{
					TypeName: var dictionaryType,
					Name: "get_Values"
				} && dictionaryType.StartsWith(
					"System.Collections.Generic.Dictionary`2<",
					StringComparison.Ordinal)
				? EnumerableSourceProvenance.DictionaryValues
			: identity is { TypeName: "System.Linq.Enumerable", Name: "OrderBy" } &&
			  stack.Count >= 2 &&
			  stack[^2] == EnumerableSourceProvenance.DictionaryUInt32Values
				? EnumerableSourceProvenance.OrderedPrimary
			: identity is { TypeName: "System.Linq.Enumerable", Name: "ThenBy" } &&
			  stack.Count >= 2 &&
			  stack[^2] == EnumerableSourceProvenance.OrderedPrimary
				? EnumerableSourceProvenance.OrderedPrimarySecondary
			: identity is
				{
					TypeName: var enumerableType,
					Name: "GetEnumerator"
				} && enumerableType.StartsWith(
					"System.Collections.Generic.IEnumerable`1<",
					StringComparison.Ordinal) &&
			  stack.Count >= 1 &&
			  stack[^1] == EnumerableSourceProvenance.OrderedPrimarySecondary
				? EnumerableSourceProvenance.OrderedEnumerator
			: identity is { TypeName: "System.Linq.Enumerable", Name: "Range" }
			? EnumerableSourceProvenance.Range
			: identity is { TypeName: "System.Linq.Enumerable", Name: "Repeat" }
				? EnumerableSourceProvenance.Repeat
				: identity is { TypeName: "System.Linq.Enumerable", Name: "Select" } &&
				  stack.Count >= 2 && stack[^2] == EnumerableSourceProvenance.Range
					? EnumerableSourceProvenance.RangeSelect
				: identity is { TypeName: "System.Linq.Enumerable", Name: "Where" } &&
				  stack.Count >= 2 && stack[^2] == EnumerableSourceProvenance.Range
					? EnumerableSourceProvenance.RangeWhere
				: identity is { TypeName: "System.Linq.Enumerable", Name: "Where" } &&
				  stack.Count >= 2 && stack[^2] == EnumerableSourceProvenance.RangeSelect
					? EnumerableSourceProvenance.RangeSelectWhere
					: identity is { TypeName: "System.Linq.Enumerable", Name: "Take" } &&
					  stack.Count >= 2 && IsSelectedIterator(stack[^2])
						? Taken(stack[^2])
					: EnumerableSourceProvenance.Unknown;
	}

	private static EnumerableSourceProvenance Taken(
		EnumerableSourceProvenance provenance) => provenance switch
		{
			EnumerableSourceProvenance.RangeWhere =>
				EnumerableSourceProvenance.RangeWhereTake,
			EnumerableSourceProvenance.RangeSelectWhere =>
				EnumerableSourceProvenance.RangeSelectWhereTake,
			_ => provenance
		};

	private static bool IsSelectedIterator(EnumerableSourceProvenance provenance) =>
		provenance is EnumerableSourceProvenance.Range or
			EnumerableSourceProvenance.Repeat or
			EnumerableSourceProvenance.RangeSelect or
			EnumerableSourceProvenance.RangeWhere or
			EnumerableSourceProvenance.RangeSelectWhere or
			EnumerableSourceProvenance.RangeWhereTake or
			EnumerableSourceProvenance.RangeSelectWhereTake;

	private static int PopCount(
		CompilationModule module,
		CilMethod method,
		CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
		{
			if (TryGetEnumerableCallShape(
					module,
					method,
					instruction,
					out var knownPopCount,
					out _))
			{
				return knownPopCount;
			}
			var target = module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return target.Signature.ParameterTypes.Length +
				(target.Signature.Header.IsInstance && op != OpCodes.Newobj ? 1 : 0);
		}
		if (op == OpCodes.Ret)
		{
			return method.Signature.ReturnType.IsVoid ? 0 : 1;
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
		CilMethod method,
		CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
		{
			if (op == OpCodes.Newobj)
			{
				return 1;
			}
			if (TryGetEnumerableCallShape(
					module,
					method,
					instruction,
					out _,
					out var knownPushCount))
			{
				return knownPushCount;
			}
			var target = module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
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

	private static bool TryGetEnumerableCallShape(
		CompilationModule module,
		CilMethod method,
		CilInstruction instruction,
		out int popCount,
		out int pushCount)
	{
		popCount = default;
		pushCount = default;
		if ((instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) ||
			instruction.Operand is not int token ||
			module.DescribeMethodToken(token, method, instruction.Offset) is not { } identity)
		{
			return false;
		}
		if (identity.TypeName == "System.Linq.Enumerable")
		{
			(popCount, pushCount) = identity.Name switch
			{
				"Range" or "Repeat" or "Select" or "Where" or "Take" or
					"OrderBy" or "ThenBy" => (2, 1),
				"ToArray" => (1, 1),
				"Any" or "Sum" => (identity.ParameterTypes.Length, 1),
				_ => (0, 0)
			};
			return popCount != 0;
		}
		if ((identity.Name == "GetEnumerator" &&
			 identity.TypeName.StartsWith(
				 "System.Collections.Generic.IEnumerable`1<",
				 StringComparison.Ordinal)) ||
			(identity.Name is "MoveNext" or "get_Current" &&
			 (identity.TypeName == "System.Collections.IEnumerator" ||
			  identity.TypeName.StartsWith(
				  "System.Collections.Generic.IEnumerator`1<",
				  StringComparison.Ordinal))))
		{
			(popCount, pushCount) = (1, 1);
			return true;
		}
		if (identity is { TypeName: "System.IDisposable", Name: "Dispose" })
		{
			(popCount, pushCount) = (1, 0);
			return true;
		}
		return false;
	}

	private static IEnumerable<int> Successors(
		CilMethod method,
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

	private static State Join(State left, State right)
	{
		if (left.Stack.Count != right.Stack.Count ||
			left.Locals.Length != right.Locals.Length)
		{
			return new State(
				Enumerable.Repeat(
					EnumerableSourceProvenance.Unknown,
					Math.Min(left.Stack.Count, right.Stack.Count)).ToList(),
				new EnumerableSourceProvenance[left.Locals.Length]);
		}
		var stack = left.Stack
			.Zip(right.Stack, Join)
			.ToList();
		var locals = new EnumerableSourceProvenance[left.Locals.Length];
		for (var index = 0; index < locals.Length; index++)
		{
			locals[index] = Join(left.Locals[index], right.Locals[index]);
		}
		return new State(stack, locals);
	}

	private static EnumerableSourceProvenance Join(
		EnumerableSourceProvenance left,
		EnumerableSourceProvenance right) =>
		left == right ? left : EnumerableSourceProvenance.Unknown;

	private static EnumerableSourceProvenance Pop(
		List<EnumerableSourceProvenance> stack)
	{
		if (stack.Count == 0)
		{
			return EnumerableSourceProvenance.Unknown;
		}
		var value = stack[^1];
		stack.RemoveAt(stack.Count - 1);
		return value;
	}

	private static bool TryGetLoadLocalIndex(
		CilInstruction instruction,
		out int index)
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

	private static bool TryGetStoreLocalIndex(
		CilInstruction instruction,
		out int index)
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

	private sealed class State(
		List<EnumerableSourceProvenance> stack,
		EnumerableSourceProvenance[] locals) : IEquatable<State>
	{
		public List<EnumerableSourceProvenance> Stack { get; } = stack;

		public EnumerableSourceProvenance[] Locals { get; } = locals;

		public State Clone() => new([.. Stack], [.. Locals]);

		public bool Equals(State? other) =>
			other is not null &&
			Stack.SequenceEqual(other.Stack) &&
			Locals.SequenceEqual(other.Locals);

		public override bool Equals(object? obj) => Equals(obj as State);

		public override int GetHashCode() => 0;
	}
}
