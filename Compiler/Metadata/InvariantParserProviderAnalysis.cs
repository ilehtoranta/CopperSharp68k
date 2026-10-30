/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Metadata;

/// <summary>
/// Method-local must analysis of invariant parser arguments, paired with a may
/// analysis of getter uses. Origin sets survive unknown joins so an escaping
/// value cannot disappear and incorrectly authorize a null provider placeholder.
/// This analysis reads metadata signatures and never resolves executable calls.
/// </summary>
internal sealed class InvariantParserProviderAnalysis
{
	private readonly HashSet<int> _parsers = [];
	private readonly HashSet<int> _getters = [];
	public bool AdmitsParser(int offset) => _parsers.Contains(offset);
	public bool AdmitsGetter(int offset) => _getters.Contains(offset);

	internal static bool IsInvariantGetter(FrameworkMemberId referenced)
	{
		var culture = FrameworkTypeId.Named("System.Runtime", "System.Globalization.CultureInfo");
		return FrameworkImplementationProfile.Canonicalize(referenced).Equals(new FrameworkMemberId(
			culture, "get_InvariantCulture", new FrameworkMethodSignatureId(0, 0, 0, culture, [])));
	}

	public static InvariantParserProviderAnalysis Analyze(CilMethod method, Func<CilInstruction, FrameworkMemberId> describe)
	{
		var result = new InvariantParserProviderAnalysis();
		if (method.Instructions.Count == 0 || method.ExceptionRegions.Any(r => !r.IsCatch)) return result;
		var instructions = method.Instructions.ToDictionary(i => i.Offset);
		var calls = new Dictionary<int, FrameworkMemberId>();
		var addressedLocals = new HashSet<int>();
		foreach (var instruction in method.Instructions)
		{
			var op = instruction.OpCode;
			if (op == OpCodes.Calli || op == OpCodes.Jmp) return result;
			if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
				calls.Add(instruction.Offset, FrameworkImplementationProfile.Canonicalize(describe(instruction)));
			if (op == OpCodes.Ldloca || op == OpCodes.Ldloca_S)
				addressedLocals.Add(Convert.ToInt32(instruction.Operand));
		}

		var states = new Dictionary<int, State>();
		var work = new Queue<int>();
		var malformed = false;
		Enqueue(method.Instructions[0].Offset, new State([], Enumerable.Repeat(Value.Unknown, method.Locals.Length).ToArray()));
		while (work.TryDequeue(out var offset) && !malformed)
		{
			var instruction = instructions[offset];
			var incoming = states[offset];
			var outgoing = Transfer(instruction, incoming, null);
			foreach (var target in Successors(instruction)) Enqueue(target, outgoing);
			// Exceptions may observe locals before or after an instruction. Catch
			// stacks hold only the exception; references in locals retain origins.
			foreach (var region in method.ExceptionRegions)
			{
				if (offset < region.TryOffset || offset >= region.TryOffset + region.TryLength) continue;
				Enqueue(region.HandlerOffset, new State([Value.Unknown], incoming.Locals));
				Enqueue(region.HandlerOffset, new State([Value.Unknown], outgoing.Locals));
			}
		}
		if (malformed) return result;

		var unsafeOrigins = new HashSet<int>();
		var candidates = new Dictionary<int, Value>();
		foreach (var (offset, state) in states)
		{
			Transfer(instructions[offset], state, unsafeOrigins);
			if (!calls.TryGetValue(offset, out var target) || !IsParserCall(instructions[offset], target)) continue;
			var provider = state.Stack[^(target.Name == "TryParse" ? 2 : 1)];
			if (provider.IsInvariant) candidates.Add(offset, provider);
			else unsafeOrigins.UnionWith(provider.Origins);
		}
		// One unsafe origin invalidates the parser at a join, which in turn
		// invalidates every getter feeding that parser. Iterate the use closure.
		bool changed;
		do
		{
			var count = unsafeOrigins.Count;
			foreach (var provider in candidates.Values)
				if (provider.Origins.Overlaps(unsafeOrigins)) unsafeOrigins.UnionWith(provider.Origins);
			changed = count != unsafeOrigins.Count;
		} while (changed);
		foreach (var (offset, provider) in candidates)
		{
			if (provider.Origins.Overlaps(unsafeOrigins)) continue;
			result._parsers.Add(offset);
			result._getters.UnionWith(provider.Origins);
		}
		return result;

		void Enqueue(int offset, State incoming)
		{
			if (!instructions.ContainsKey(offset)) { malformed = true; return; }
			if (!states.TryGetValue(offset, out var current))
			{
				states.Add(offset, incoming.Copy());
				work.Enqueue(offset);
				return;
			}
			if (current.Stack.Count != incoming.Stack.Count) { malformed = true; return; }
			var merged = new State(current.Stack.Zip(incoming.Stack, Value.Join).ToList(),
				current.Locals.Zip(incoming.Locals, Value.Join).ToArray());
			if (current.SameAs(merged)) return;
			states[offset] = merged;
			work.Enqueue(offset);
		}

		State Transfer(CilInstruction instruction, State incoming, HashSet<int>? unsafeUses)
		{
			var state = incoming.Copy();
			var op = instruction.OpCode;
			if (LocalIndex(instruction, true) is int load)
			{
				if ((uint)load >= state.Locals.Length) { malformed = true; return state; }
				state.Stack.Add(state.Locals[load]);
				return state;
			}
			if (LocalIndex(instruction, false) is int store)
			{
				var value = Pop();
				if ((uint)store >= state.Locals.Length) { malformed = true; return state; }
				if (addressedLocals.Contains(store)) { Unsafe(value); value = Value.Unknown; }
				state.Locals[store] = value;
				return state;
			}
			if (op == OpCodes.Dup)
			{
				var value = Pop();
				state.Stack.Add(value);
				state.Stack.Add(value);
				return state;
			}
			if (calls.TryGetValue(instruction.Offset, out var call))
			{
				var popCount = call.Signature.ParameterTypes.Length +
					(op != OpCodes.Newobj && call.Signature.IsInstance ? 1 : 0);
				var providerSlot = IsParserCall(instruction, call) ? call.Name == "TryParse" ? 1 : 0 : -1;
				for (var index = 0; index < popCount; index++)
				{
					var value = Pop();
					if (index != providerSlot) Unsafe(value);
				}
				if (op == OpCodes.Newobj || !call.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")))
					state.Stack.Add(op == OpCodes.Call && IsInvariantGetter(call) ? Value.FromGetter(instruction.Offset) : Value.Unknown);
				return state;
			}
			if (op == OpCodes.Leave || op == OpCodes.Leave_S)
			{
				foreach (var value in state.Stack) Unsafe(value);
				state.Stack.Clear();
				return state;
			}
			var pops = op == OpCodes.Ret ? method.Signature.ReturnType.IsVoid ? 0 : 1 : PopCount(op.StackBehaviourPop);
			var pushes = PushCount(op.StackBehaviourPush);
			if (pops < 0 || pushes < 0) { malformed = true; return state; }
			for (var index = 0; index < pops; index++) Unsafe(Pop());
			for (var index = 0; index < pushes; index++) state.Stack.Add(Value.Unknown);
			return state;

			Value Pop()
			{
				if (state.Stack.Count == 0) { malformed = true; return Value.Unknown; }
				var value = state.Stack[^1];
				state.Stack.RemoveAt(state.Stack.Count - 1);
				return value;
			}
			void Unsafe(Value value) => unsafeUses?.UnionWith(value.Origins);
		}

		IEnumerable<int> Successors(CilInstruction instruction)
		{
			var op = instruction.OpCode;
			if (op == OpCodes.Switch)
				foreach (var target in (int[])instruction.Operand!) yield return target;
			else if (op.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch && instruction.Operand is int target)
				yield return target;
			if (op.FlowControl is FlowControl.Branch or FlowControl.Return or FlowControl.Throw || op == OpCodes.Endfinally) yield break;
			if (instructions.ContainsKey(instruction.NextOffset)) yield return instruction.NextOffset;
		}
	}

	private static bool IsParserCall(CilInstruction instruction, FrameworkMemberId member) =>
		instruction.OpCode == OpCodes.Call &&
		(DateOnlySurface.IsInvariantParseMember(member) || TimeOnlySurface.IsInvariantParseMember(member));

	private static int? LocalIndex(CilInstruction instruction, bool load)
	{
		var op = instruction.OpCode;
		var first = load ? OpCodes.Ldloc_0 : OpCodes.Stloc_0;
		if (op.Value >= first.Value && op.Value <= first.Value + 3) return op.Value - first.Value;
		return op == (load ? OpCodes.Ldloc : OpCodes.Stloc) || op == (load ? OpCodes.Ldloc_S : OpCodes.Stloc_S)
			? Convert.ToInt32(instruction.Operand) : null;
	}

	private static int PopCount(StackBehaviour pop) => pop switch
	{
		StackBehaviour.Pop0 => 0,
		StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
		StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8 or
			StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
		StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_pop1 or StackBehaviour.Popref_popi_popi or
			StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or
			StackBehaviour.Popref_popi_popref => 3,
		_ => -1
	};

	private static int PushCount(StackBehaviour push) => push switch
	{
		StackBehaviour.Push0 => 0,
		StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4 or
			StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
		StackBehaviour.Push1_push1 => 2,
		_ => -1
	};

	private sealed record Value(bool MayBeUnknown, HashSet<int> Origins)
	{
		public static Value Unknown { get; } = new(true, []);
		public bool IsInvariant => !MayBeUnknown && Origins.Count != 0;
		public static Value FromGetter(int offset) => new(false, [offset]);
		public static Value Join(Value left, Value right) => new(left.MayBeUnknown || right.MayBeUnknown, [.. left.Origins, .. right.Origins]);
		public bool SameAs(Value other) => MayBeUnknown == other.MayBeUnknown && Origins.SetEquals(other.Origins);
	}

	private sealed record State(List<Value> Stack, Value[] Locals)
	{
		public State Copy() => new([.. Stack], [.. Locals]);
		public bool SameAs(State other) => Stack.Zip(other.Stack).All(p => p.First.SameAs(p.Second)) &&
			Locals.Zip(other.Locals).All(p => p.First.SameAs(p.Second));
	}
}
