/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private readonly record struct PlatformBaseState(bool Visited, string? Identity);

	private void AnalyzePlatformBaseMethodEntries(
		IReadOnlyList<CilMethod> methods,
		CilMethod entry,
		IReadOnlyList<CilExport> exports)
	{
		_platformBaseMethodEntries.Clear();
		var methodsByIdentity = methods.ToDictionary(static method => method.Identity);
		var addressTaken = new HashSet<CilMethodIdentity>();
		foreach (var caller in methods)
		{
			foreach (var instruction in caller.Instructions)
			{
				if (instruction.OpCode != OpCodes.Ldftn &&
					instruction.OpCode != OpCodes.Ldvirtftn)
				{
					continue;
				}

				var target = _module.ResolveMethodToken(
					(int)instruction.Operand!,
					caller,
					instruction.Offset);
				if (target.Definition is { IsImport: false } definition)
				{
					addressTaken.Add(definition.Identity);
				}
			}
		}

		var externallyReachable = exports
			.Select(static export => export.Method.Identity)
			.ToHashSet();
		externallyReachable.Add(entry.Identity);
		if (_managedPoolRuntime is not null)
		{
			externallyReachable.UnionWith(
				_managedPoolRuntime.Methods.Select(static method => method.Identity));
		}
		foreach (var lifecycle in _managedLifecycles)
		{
			externallyReachable.UnionWith(
				lifecycle.Methods.Select(static method => method.Identity));
		}
		externallyReachable.UnionWith(_typeInitializers.Keys);

		var inferable = methods
			.Where(method =>
				!externallyReachable.Contains(method.Identity) &&
				!addressTaken.Contains(method.Identity) &&
				(method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private &&
				(method.Attributes & MethodAttributes.Static) != 0 &&
				!method.IsVirtual &&
				!method.IsTypeInitializer)
			.Select(static method => method.Identity)
			.ToHashSet();

		var states = new Dictionary<CilMethodIdentity, PlatformBaseState>();
		var queue = new Queue<CilMethodIdentity>();
		foreach (var method in methods)
		{
			if (!inferable.Contains(method.Identity))
			{
				MergePlatformBaseMethodState(
					states,
					queue,
					method.Identity,
					new PlatformBaseState(Visited: true, Identity: null));
			}
		}

		while (queue.Count != 0)
		{
			var methodIdentity = queue.Dequeue();
			var method = methodsByIdentity[methodIdentity];
			var state = states[methodIdentity];
			var reachableOffsets = method.Instructions
				.Select(static instruction => instruction.Offset)
				.ToHashSet();
			AnalyzePlatformBaseBlockEntries(
				method,
				reachableOffsets,
				state.Identity,
				(callee, identity) =>
				{
					if (inferable.Contains(callee.Identity))
					{
						MergePlatformBaseMethodState(
							states,
							queue,
							callee.Identity,
							new PlatformBaseState(Visited: true, Identity: identity));
					}
				});
		}

		foreach (var method in methods)
		{
			_platformBaseMethodEntries[method.Identity] =
				states.TryGetValue(method.Identity, out var state) && state.Visited
					? state.Identity
					: null;
		}
	}

	private static void MergePlatformBaseMethodState(
		Dictionary<CilMethodIdentity, PlatformBaseState> states,
		Queue<CilMethodIdentity> queue,
		CilMethodIdentity method,
		PlatformBaseState incoming)
	{
		if (!states.TryGetValue(method, out var existing) || !existing.Visited)
		{
			states[method] = incoming;
			queue.Enqueue(method);
			return;
		}

		var mergedIdentity = existing.Identity == incoming.Identity
			? existing.Identity
			: null;
		if (mergedIdentity != existing.Identity)
		{
			states[method] = new PlatformBaseState(Visited: true, Identity: mergedIdentity);
			queue.Enqueue(method);
		}
	}

	private Dictionary<int, string?> AnalyzePlatformBaseBlockEntries(
		CilMethod method,
		IReadOnlySet<int> reachableOffsets,
		string? initialIdentity = null,
		Action<CilMethod, string?>? observeInternalCall = null)
	{
		var result = new Dictionary<int, string?>();
		if (method.Instructions.Count == 0)
		{
			return result;
		}

		var offsetToIndex = new Dictionary<int, int>();
		for (var index = 0; index < method.Instructions.Count; index++)
		{
			offsetToIndex.Add(method.Instructions[index].Offset, index);
		}

		var firstIndex = -1;
		for (var index = 0; index < method.Instructions.Count; index++)
		{
			if (reachableOffsets.Contains(method.Instructions[index].Offset))
			{
				firstIndex = index;
				break;
			}
		}

		if (firstIndex < 0)
		{
			return result;
		}

		var blockStarts = GetPlatformBaseBlockStarts(method.Instructions, reachableOffsets);
		foreach (var region in method.ExceptionRegions)
		{
			if (reachableOffsets.Contains(region.HandlerOffset))
			{
				blockStarts.Add(region.HandlerOffset);
			}
			if (region.FilterOffset >= 0 && reachableOffsets.Contains(region.FilterOffset))
			{
				blockStarts.Add(region.FilterOffset);
			}
		}
		var states = new Dictionary<int, PlatformBaseState>();
		var queue = new Queue<int>();
		MergePlatformBaseState(
			states,
			queue,
			method.Instructions[firstIndex].Offset,
			new PlatformBaseState(Visited: true, Identity: initialIdentity));
		foreach (var region in method.ExceptionRegions)
		{
			if (reachableOffsets.Contains(region.HandlerOffset))
			{
				MergePlatformBaseState(
					states,
					queue,
					region.HandlerOffset,
					new PlatformBaseState(Visited: true, Identity: null));
			}
			if (region.FilterOffset >= 0 && reachableOffsets.Contains(region.FilterOffset))
			{
				MergePlatformBaseState(
					states,
					queue,
					region.FilterOffset,
					new PlatformBaseState(Visited: true, Identity: null));
			}
		}

		while (queue.Count != 0)
		{
			var offset = queue.Dequeue();
			var state = states[offset];
			var index = offsetToIndex[offset];
			var instruction = method.Instructions[index];
			if (observeInternalCall is not null &&
				TryGetDirectInternalPlatformBaseCallee(method, instruction) is { } callee)
			{
				// Argument setup and scratch use cannot disturb an internal
				// callee-saved base register. Caller-saved base registers do not
				// receive an interprocedural entry guarantee.
				observeInternalCall(
					callee,
					PreservedPlatformBaseIdentity(state.Identity));
			}
			var nextState = new PlatformBaseState(
				Visited: true,
				Identity: TransferPlatformBaseIdentity(method, instruction, state.Identity));

			foreach (var successor in GetPlatformBaseSuccessors(
				method.Instructions,
				index,
				reachableOffsets))
			{
				MergePlatformBaseState(states, queue, successor, nextState);
			}
		}

		foreach (var start in blockStarts)
		{
			if (states.TryGetValue(start, out var state))
			{
				result.Add(start, state.Identity);
			}
		}

		return result;
	}

	private static HashSet<int> GetPlatformBaseBlockStarts(
		IReadOnlyList<CilInstruction> instructions,
		IReadOnlySet<int> reachableOffsets)
	{
		var result = new HashSet<int>();
		for (var index = 0; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (result.Count == 0)
			{
				result.Add(instruction.Offset);
			}

			foreach (var target in GetBranchTargetOffsets(instruction))
			{
				if (reachableOffsets.Contains(target))
				{
					result.Add(target);
				}
			}

			if (InstructionCanFallThrough(instruction.OpCode) &&
				index + 1 < instructions.Count &&
				reachableOffsets.Contains(instructions[index + 1].Offset) &&
				instruction.OpCode.FlowControl is FlowControl.Cond_Branch)
			{
				result.Add(instructions[index + 1].Offset);
			}
		}

		return result;
	}

	private static IEnumerable<int> GetPlatformBaseSuccessors(
		IReadOnlyList<CilInstruction> instructions,
		int index,
		IReadOnlySet<int> reachableOffsets)
	{
		var instruction = instructions[index];
		foreach (var target in GetBranchTargetOffsets(instruction))
		{
			if (reachableOffsets.Contains(target))
			{
				yield return target;
			}
		}

		if (InstructionCanFallThrough(instruction.OpCode) &&
			index + 1 < instructions.Count &&
			reachableOffsets.Contains(instructions[index + 1].Offset))
		{
			yield return instructions[index + 1].Offset;
		}
	}

	private static IEnumerable<int> GetBranchTargetOffsets(CilInstruction instruction)
	{
		if (instruction.OpCode == OpCodes.Switch)
		{
			foreach (var target in (int[])instruction.Operand!)
			{
				yield return target;
			}
		}
		else if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch &&
			instruction.Operand is int target)
		{
			yield return target;
		}
	}

	private static bool InstructionCanFallThrough(OpCode op) =>
		op.FlowControl is not FlowControl.Branch and not FlowControl.Return and not FlowControl.Throw;

	private string? TransferPlatformBaseIdentity(
		CilMethod method,
		CilInstruction instruction,
		string? currentIdentity)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Newarr)
		{
			return null;
		}

		if (op != OpCodes.Call &&
			op != OpCodes.Callvirt &&
			op != OpCodes.Newobj)
		{
			return currentIdentity;
		}

		var target = _module.ResolveMethodToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (target.Definition?.ExternalCall is { } externalCall)
		{
			return externalCall.Convention.BaseSource == M68kExternalBaseSource.Argument
				? null
				: externalCall.Convention.Identity;
		}

		if (op == OpCodes.Newobj &&
			target.Definition is { } constructor &&
			_module.IsTransparentScalarConstructor(constructor))
		{
			return currentIdentity;
		}

		if (target.ImportName is { } importName)
		{
			if (importName.StartsWith("intrinsic:amiga-library-base-set:", StringComparison.Ordinal))
			{
				return null;
			}
			return importName.StartsWith("intrinsic:", StringComparison.Ordinal)
				? currentIdentity
				: null;
		}

		return PreservedPlatformBaseIdentity(currentIdentity);
	}

	private CilMethod? TryGetDirectInternalPlatformBaseCallee(
		CilMethod caller,
		CilInstruction instruction)
	{
		if (!IsCallInstruction(instruction))
		{
			return null;
		}

		var target = _module.ResolveMethodToken(
			(int)instruction.Operand!,
			caller,
			instruction.Offset);
		return target.Definition is { IsImport: false } definition &&
			!definition.DeclaringTypeIsInterface &&
			!RequiresVirtualDispatch(instruction, definition) &&
			!IsAlwaysInlinedMethod(definition)
				? definition
				: null;
	}

	private string? PreservedPlatformBaseIdentity(string? identity)
	{
		if (identity is null ||
			!_usedPlatformBases.TryGetValue(identity, out var platformBase))
		{
			return null;
		}

		return IsInternalCalleeSavedRegister(platformBase.Binding.BaseRegister)
			? identity
			: null;
	}

	private void PreservePlatformBaseAcrossInternalCall()
	{
		if (_loadedPlatformBase is { } platformBase &&
			!IsInternalCalleeSavedRegister(platformBase.Binding.BaseRegister))
		{
			_loadedPlatformBase = null;
		}
	}

	private static void MergePlatformBaseState(
		Dictionary<int, PlatformBaseState> states,
		Queue<int> queue,
		int offset,
		PlatformBaseState incoming)
	{
		if (!states.TryGetValue(offset, out var existing) ||
			!existing.Visited)
		{
			states[offset] = incoming;
			queue.Enqueue(offset);
			return;
		}

		var mergedIdentity = existing.Identity == incoming.Identity
			? existing.Identity
			: null;
		if (mergedIdentity != existing.Identity)
		{
			states[offset] = new PlatformBaseState(Visited: true, Identity: mergedIdentity);
			queue.Enqueue(offset);
		}
	}

	private void ApplyPlatformBaseBlockEntry(string? identity)
	{
		_loadedPlatformBase =
			identity is not null &&
			_usedPlatformBases.TryGetValue(identity, out var platformBase)
				? platformBase
				: null;
	}
}
