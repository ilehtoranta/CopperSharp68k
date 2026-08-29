/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

[Flags]
internal enum M68kParameterMemoryEffect
{
	None = 0,
	Read = 1,
	Write = 2,
	Capture = 4,
	ReturnedAlias = 8
}

internal sealed record M68kMethodMemorySummary(
	ImmutableHashSet<M68kMemoryObject> ExactGlobalReads,
	ImmutableHashSet<M68kMemoryObject> ExactGlobalWrites,
	ImmutableDictionary<int, M68kParameterMemoryEffect> ParameterEffects,
	bool ReadsUnknownHeap,
	bool WritesUnknownHeap,
	bool ReadsUnknownGlobals,
	bool WritesUnknownGlobals,
	bool MayCallback,
	bool MayReenter,
	bool ObservesManagedRoots)
{
	public static M68kMethodMemorySummary Empty { get; } = new(
		ImmutableHashSet<M68kMemoryObject>.Empty,
		ImmutableHashSet<M68kMemoryObject>.Empty,
		ImmutableDictionary<int, M68kParameterMemoryEffect>.Empty,
		ReadsUnknownHeap: false,
		WritesUnknownHeap: false,
		ReadsUnknownGlobals: false,
		WritesUnknownGlobals: false,
		MayCallback: false,
		MayReenter: false,
		ObservesManagedRoots: false);

	public M68kParameterMemoryEffect EffectForParameter(int index) =>
		ParameterEffects.GetValueOrDefault(index);
}

/// <summary>
/// Closed-world method effects. The analysis deliberately has no annotation
/// surface: native or unresolved calls remain conservative, while managed SCCs
/// converge from the exact effects present in their lowered bodies.
/// </summary>
internal static class M68kMethodMemorySummaryAnalyzer
{
	private const int MaximumIterations = 64;

	private sealed class MutableSummary
	{
		public HashSet<M68kMemoryObject> GlobalReads { get; } = new();

		public HashSet<M68kMemoryObject> GlobalWrites { get; } = new();

		public Dictionary<int, M68kParameterMemoryEffect> Parameters { get; } = new();

		public bool ReadsUnknownHeap;

		public bool WritesUnknownHeap;

		public bool ReadsUnknownGlobals;

		public bool WritesUnknownGlobals;

		public bool MayCallback;

		public bool MayReenter;

		public bool ObservesManagedRoots;

		public void AddParameter(int index, M68kParameterMemoryEffect effect)
		{
			Parameters[index] = Parameters.GetValueOrDefault(index) | effect;
		}

		public void Merge(
			M68kMethodMemorySummary other,
			bool includeParameterEffects = false)
		{
			GlobalReads.UnionWith(other.ExactGlobalReads);
			GlobalWrites.UnionWith(other.ExactGlobalWrites);
			if (includeParameterEffects)
			{
				foreach (var (index, effect) in other.ParameterEffects)
				{
					AddParameter(index, effect);
				}
			}
			ReadsUnknownHeap |= other.ReadsUnknownHeap;
			WritesUnknownHeap |= other.WritesUnknownHeap;
			ReadsUnknownGlobals |= other.ReadsUnknownGlobals;
			WritesUnknownGlobals |= other.WritesUnknownGlobals;
			MayCallback |= other.MayCallback;
			MayReenter |= other.MayReenter;
			ObservesManagedRoots |= other.ObservesManagedRoots;
		}

		public M68kMethodMemorySummary Freeze() => new(
			GlobalReads.ToImmutableHashSet(),
			GlobalWrites.ToImmutableHashSet(),
			Parameters.ToImmutableDictionary(),
			ReadsUnknownHeap,
			WritesUnknownHeap,
			ReadsUnknownGlobals,
			WritesUnknownGlobals,
			MayCallback,
			MayReenter,
			ObservesManagedRoots);
	}

	public static IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary> Compute(
		IReadOnlyList<CilMethod> methods,
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions,
		CompilationModule module)
	{
		var methodsByIdentity = methods.ToDictionary(static method => method.Identity);
		var local = new Dictionary<CilMethodIdentity, M68kMethodMemorySummary>();
		foreach (var (identity, function) in functions)
		{
			if (!methodsByIdentity.TryGetValue(identity, out var method))
			{
				continue;
			}
			var aliases = BuildParameterAliases(function, summaries: null);
			local[identity] = ComputeLocal(method, function, module, aliases);
		}

		var summaries = new Dictionary<CilMethodIdentity, M68kMethodMemorySummary>(local);
		var components = FindStronglyConnectedComponents(functions);
		foreach (var component in components)
		{
			var iteration = 0;
			var componentChanged = true;
			while (componentChanged && iteration++ < MaximumIterations)
			{
				componentChanged = false;
				foreach (var identity in component)
				{
					if (!functions.TryGetValue(identity, out var function) ||
						!methodsByIdentity.TryGetValue(identity, out var method))
					{
						continue;
					}
					var aliases = BuildParameterAliases(function, summaries);
					var body = ComputeLocal(method, function, module, aliases);
					var next = ApplyCalls(body, function, aliases, summaries);
					if (Equivalent(summaries[identity], next))
					{
						continue;
					}
					summaries[identity] = next;
					componentChanged = true;
				}
			}
		}
		return summaries;
	}

	private static M68kMethodMemorySummary ComputeLocal(
		CilMethod method,
		M68kMachineFunction function,
		CompilationModule module,
		IReadOnlyDictionary<int, int> parameterAliases)
	{
		var summary = new MutableSummary();
		foreach (var instruction in function.Blocks.SelectMany(static block =>
			block.Instructions))
		{
			summary.ObservesManagedRoots |= instruction.IsSafepoint;
			if (instruction.Operation == M68kMachineOperation.Call)
			{
				// Calls are composed from logical target summaries below. Avoid
				// resolving their source token here; optimizer unit functions may use
				// synthetic module identities with no backing metadata image.
				continue;
			}
			var effect = M68kMemoryModel.Summarize(method, module, instruction);
			summary.GlobalReads.UnionWith(effect.ReadsExact.Where(static item =>
				item.IsGlobalObject));
			summary.GlobalWrites.UnionWith(effect.WritesExact.Where(static item =>
				item.IsGlobalObject));

			if (effect.ReadsUnknown)
			{
				if (MayAccessGlobals(instruction))
				{
					summary.ReadsUnknownGlobals = true;
				}
				else
				{
					summary.ReadsUnknownHeap = true;
				}
			}
			if (effect.WritesUnknown)
			{
				if (MayAccessGlobals(instruction))
				{
					summary.WritesUnknownGlobals = true;
				}
				else
				{
					summary.WritesUnknownHeap = true;
				}
			}

			if (instruction.Uses.Length != 0 &&
				parameterAliases.TryGetValue(instruction.Uses[0], out var ownerParameter))
			{
				var parameterEffect = ParameterEffectFor(instruction);
				if (parameterEffect != M68kParameterMemoryEffect.None)
				{
					summary.AddParameter(ownerParameter, parameterEffect);
				}
			}
			if (instruction.Operation == M68kMachineOperation.Return)
			{
				foreach (var use in instruction.Uses)
				{
					if (parameterAliases.TryGetValue(use, out var parameter))
					{
						summary.AddParameter(
							parameter,
							M68kParameterMemoryEffect.ReturnedAlias);
					}
				}
			}
			foreach (var access in instruction.ExactMemoryAccesses.Where(static access =>
				access.Kind == M68kExactMemoryAccessKind.Write &&
				(access.Object.IsGlobalObject || access.Object.IsHeapObject) &&
				access.ValueId is not null))
			{
				if (parameterAliases.TryGetValue(access.ValueId!.Value, out var parameter))
				{
					// Storing an incoming reference into any longer-lived exact
					// location captures it. This is especially important for
					// constructors: the caller must not promote the referenced
					// object's fields as though the constructor were transparent.
					summary.AddParameter(
						parameter,
						M68kParameterMemoryEffect.Capture);
				}
			}
			var source = instruction.Origin?.SourceInstruction ??
				instruction.SourceInstruction;
			if (instruction.Uses.Length != 0 &&
				IsCapturingStore(instruction, source) &&
				parameterAliases.TryGetValue(instruction.Uses[^1], out var captured))
			{
				// Some destinations (notably fields of finalizable or already
				// escaping objects) intentionally have no exact-memory identity.
				// Their source opcode still proves that the stored value escapes.
				summary.AddParameter(
					captured,
					M68kParameterMemoryEffect.Capture);
			}
		}
		return summary.Freeze();
	}

	private static bool IsCapturingStore(
		M68kMachineInstruction instruction,
		CilInstruction? source)
	{
		if (instruction.Operation is
			M68kMachineOperation.ArrayStore or
			M68kMachineOperation.AggregateArrayStore)
		{
			return true;
		}
		if (source is null)
		{
			return false;
		}
		var op = source.OpCode;
		return op == System.Reflection.Emit.OpCodes.Stfld ||
			op == System.Reflection.Emit.OpCodes.Stsfld ||
			op == System.Reflection.Emit.OpCodes.Stobj ||
			op == System.Reflection.Emit.OpCodes.Stind_Ref;
	}

	private static M68kMethodMemorySummary ApplyCalls(
		M68kMethodMemorySummary body,
		M68kMachineFunction function,
		IReadOnlyDictionary<int, int> parameterAliases,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary> summaries)
	{
		var result = new MutableSummary();
		result.Merge(body, includeParameterEffects: true);
		foreach (var call in function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction => instruction.Operation ==
				M68kMachineOperation.Call))
		{
			var targets = call.LogicalCall?.ResolvedTargets ?? [];
			var allKnown = targets.Length != 0 &&
				targets.All(summaries.ContainsKey);
			if (!allKnown)
			{
				var reads = (call.MemoryEffect & M68kMachineMemoryEffect.Read) != 0;
				var writes = (call.MemoryEffect & M68kMachineMemoryEffect.Write) != 0;
				result.ReadsUnknownHeap |= reads;
				result.WritesUnknownHeap |= writes;
				result.ReadsUnknownGlobals |= reads;
				result.WritesUnknownGlobals |= writes;
				var callback = HasCallbackArgument(function, call);
				result.MayCallback |= callback;
				result.MayReenter |= callback;
				foreach (var argument in call.LogicalCall?.ArgumentValueIds ?? call.Uses)
				{
					if (parameterAliases.TryGetValue(argument, out var parameter))
					{
						result.AddParameter(
							parameter,
							M68kParameterMemoryEffect.Read |
							M68kParameterMemoryEffect.Write |
							M68kParameterMemoryEffect.Capture);
					}
				}
				continue;
			}

			foreach (var target in targets)
			{
				var targetSummary = summaries[target];
				result.Merge(targetSummary);
				var arguments = call.LogicalCall?.ArgumentValueIds ?? [];
				for (var index = 0; index < arguments.Length; index++)
				{
					if (!parameterAliases.TryGetValue(arguments[index], out var callerParameter))
					{
						continue;
					}
					result.AddParameter(
						callerParameter,
						targetSummary.EffectForParameter(index) &
						~M68kParameterMemoryEffect.ReturnedAlias);
				}
			}
		}
		return result.Freeze();
	}

	private static IReadOnlyDictionary<int, int> BuildParameterAliases(
		M68kMachineFunction function,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>?
			summaries)
	{
		var result = new Dictionary<int, int>();
		foreach (var argument in function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction => instruction.Operation ==
				M68kMachineOperation.Argument))
		{
			if (argument.ArgumentIndex is { } index &&
				argument.Definitions is [var definition])
			{
				result[definition] = index;
			}
		}

		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var copy in function.Blocks
				.SelectMany(static block => block.Instructions)
				.Where(static instruction =>
					instruction.Operation == M68kMachineOperation.Copy &&
					instruction.Uses.Length == 1 &&
					instruction.Definitions.Length == 1))
			{
				if (result.TryGetValue(copy.Uses[0], out var parameter) &&
					result.TryAdd(copy.Definitions[0], parameter))
				{
					changed = true;
				}
			}
			foreach (var phi in function.Blocks.SelectMany(static block => block.Phis))
			{
				var inputs = phi.Inputs.Values
					.Where(value => value != phi.Definition)
					.ToArray();
				var parameters = inputs
					.Where(result.ContainsKey)
					.Select(value => result[value])
					.Distinct()
					.ToArray();
				if (parameters is [var parameter] &&
					inputs.Length != 0 &&
					inputs.All(result.ContainsKey) &&
					result.TryAdd(phi.Definition, parameter))
				{
					changed = true;
				}
			}
			if (summaries is null)
			{
				continue;
			}
			foreach (var call in function.Blocks
				.SelectMany(static block => block.Instructions)
				.Where(static instruction =>
					instruction.Operation == M68kMachineOperation.Call &&
					instruction.Definitions.Length == 1))
			{
				if (TryGetReturnedParameterAlias(
						call,
						result,
						summaries,
						out var parameter) &&
					result.TryAdd(call.Definitions[0], parameter))
				{
					changed = true;
				}
			}
		}
		return result;
	}

	private static bool TryGetReturnedParameterAlias(
		M68kMachineInstruction call,
		IReadOnlyDictionary<int, int> parameterAliases,
		IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary> summaries,
		out int parameter)
	{
		parameter = default;
		if (call.LogicalCall is not
			{
				ResolvedTargets.Length: > 0,
				ArgumentValueIds: var arguments
			} logicalCall)
		{
			return false;
		}
		int? commonParameter = null;
		foreach (var target in logicalCall.ResolvedTargets)
		{
			if (!summaries.TryGetValue(target, out var summary))
			{
				return false;
			}
			var aliases = Enumerable.Range(0, arguments.Length)
				.Where(index => (summary.EffectForParameter(index) &
					M68kParameterMemoryEffect.ReturnedAlias) != 0)
				.Where(index => parameterAliases.ContainsKey(arguments[index]))
				.Select(index => parameterAliases[arguments[index]])
				.Distinct()
				.ToArray();
			if (aliases is not [var targetParameter] ||
				commonParameter is { } known && known != targetParameter)
			{
				return false;
			}
			commonParameter = targetParameter;
		}
		if (commonParameter is not { } resolved)
		{
			return false;
		}
		parameter = resolved;
		return true;
	}

	private static M68kParameterMemoryEffect ParameterEffectFor(
		M68kMachineInstruction instruction)
	{
		if (instruction.Operation is
			M68kMachineOperation.ArrayLoad or
			M68kMachineOperation.AggregateArrayLoad ||
			instruction.SourceInstruction is { OpCode: var load } &&
			(load.Name?.StartsWith("ld", StringComparison.Ordinal) == true))
		{
			return M68kParameterMemoryEffect.Read;
		}
		if (instruction.Operation is
			M68kMachineOperation.ArrayStore or
			M68kMachineOperation.AggregateArrayStore ||
			instruction.SourceInstruction is { OpCode: var store } &&
			(store.Name?.StartsWith("st", StringComparison.Ordinal) == true))
		{
			return M68kParameterMemoryEffect.Write;
		}
		if (instruction.Operation is
			M68kMachineOperation.ArrayAddress or
			M68kMachineOperation.Address)
		{
			return M68kParameterMemoryEffect.Read |
				M68kParameterMemoryEffect.Write |
				M68kParameterMemoryEffect.Capture;
		}
		return M68kParameterMemoryEffect.None;
	}

	private static bool MayAccessGlobals(M68kMachineInstruction instruction) =>
		instruction.Operation is
			M68kMachineOperation.TypeInitialize or
			M68kMachineOperation.FunctionAddress;

	private static bool HasCallbackArgument(
		M68kMachineFunction function,
		M68kMachineInstruction call)
	{
		var definitions = function.Blocks
			.SelectMany(static block => block.Instructions)
			.SelectMany(static instruction => instruction.Definitions.Select(
				definition => (definition, instruction)))
			.ToDictionary(static item => item.definition, static item => item.instruction);
		foreach (var argument in call.LogicalCall?.ArgumentValueIds ?? call.Uses)
		{
			var value = argument;
			var visited = new HashSet<int>();
			while (visited.Add(value) && definitions.TryGetValue(value, out var definition))
			{
				if (definition.Operation is
					M68kMachineOperation.FunctionAddress or
					M68kMachineOperation.DelegateCreate)
				{
					return true;
				}
				if (definition.Operation == M68kMachineOperation.Copy &&
					definition.Uses is [var source])
				{
					value = source;
					continue;
				}
				break;
			}
		}
		return false;
	}

	private static IReadOnlyList<IReadOnlyList<CilMethodIdentity>>
		FindStronglyConnectedComponents(
			IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions)
	{
		var graph = functions.ToDictionary(
			static item => item.Key,
			item => item.Value.Blocks
				.SelectMany(static block => block.Instructions)
				.SelectMany(static instruction =>
					instruction.LogicalCall?.ResolvedTargets ?? [])
				.Where(functions.ContainsKey)
				.ToHashSet());
		var index = 0;
		var stack = new Stack<CilMethodIdentity>();
		var onStack = new HashSet<CilMethodIdentity>();
		var indices = new Dictionary<CilMethodIdentity, int>();
		var lowLinks = new Dictionary<CilMethodIdentity, int>();
		var components = new List<IReadOnlyList<CilMethodIdentity>>();

		void Visit(CilMethodIdentity identity)
		{
			indices[identity] = index;
			lowLinks[identity] = index++;
			stack.Push(identity);
			onStack.Add(identity);
			foreach (var target in graph[identity])
			{
				if (!indices.ContainsKey(target))
				{
					Visit(target);
					lowLinks[identity] = Math.Min(
						lowLinks[identity],
						lowLinks[target]);
				}
				else if (onStack.Contains(target))
				{
					lowLinks[identity] = Math.Min(
						lowLinks[identity],
						indices[target]);
				}
			}
			if (lowLinks[identity] != indices[identity])
			{
				return;
			}
			var component = new List<CilMethodIdentity>();
			CilMethodIdentity member;
			do
			{
				member = stack.Pop();
				onStack.Remove(member);
				component.Add(member);
			}
			while (!member.Equals(identity));
			components.Add(component);
		}

		foreach (var identity in graph.Keys)
		{
			if (!indices.ContainsKey(identity))
			{
				Visit(identity);
			}
		}
		return components;
	}

	private static bool Equivalent(
		M68kMethodMemorySummary first,
		M68kMethodMemorySummary second) =>
		first.ExactGlobalReads.SetEquals(second.ExactGlobalReads) &&
		first.ExactGlobalWrites.SetEquals(second.ExactGlobalWrites) &&
		first.ParameterEffects.Count == second.ParameterEffects.Count &&
		first.ParameterEffects.All(item =>
			second.ParameterEffects.GetValueOrDefault(item.Key) == item.Value) &&
		first.ReadsUnknownHeap == second.ReadsUnknownHeap &&
		first.WritesUnknownHeap == second.WritesUnknownHeap &&
		first.ReadsUnknownGlobals == second.ReadsUnknownGlobals &&
		first.WritesUnknownGlobals == second.WritesUnknownGlobals &&
		first.MayCallback == second.MayCallback &&
		first.MayReenter == second.MayReenter &&
		first.ObservesManagedRoots == second.ObservesManagedRoots;
}
