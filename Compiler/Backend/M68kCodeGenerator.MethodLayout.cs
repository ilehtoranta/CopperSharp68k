/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private IReadOnlyList<CilMethod> OrderMethodsForRomSize(
		IReadOnlyList<CilMethod> methods,
		IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions)
	{
		if (_request.RomSizeOptimizations?.ClusterInternalCalls != true ||
			_request.Cpu != M68kCpuTarget.M68000 ||
			_request.RuntimeProfile != M68kRuntimeProfile.Rom ||
			_request.ExceptionMode != M68kExceptionMode.Yolo ||
			_memoryManagement != M68kMemoryManagement.None ||
			_managedPoolRuntime is not null || _managedLifecycles.Count != 0 ||
			_usesExceptionRuntime)
			return methods;

		var physicalMethods = methods
			.Where(method => !_foldedMethodAliases.ContainsKey(method.Identity))
			.ToArray();
		if (physicalMethods.Length < 2) return methods;
		var indexByIdentity = physicalMethods
			.Select((method, index) => (method.Identity, Index: index))
			.ToDictionary(static item => item.Identity, static item => item.Index);
		var edges = new List<M68kCallGraphLayoutEdge>();
		foreach (var (callerIdentity, function) in functions)
		{
			var physicalCaller = PhysicalMethodIdentity(callerIdentity);
			if (!indexByIdentity.TryGetValue(physicalCaller, out var caller)) continue;
			foreach (var instruction in function.Blocks.SelectMany(
				static block => block.Instructions))
			{
				if (instruction.BulkCopy?.Target.ManagedMethod is { } provider)
					AddEdge(provider.Identity);
				if (instruction.Operation == M68kMachineOperation.Call &&
					instruction.LogicalCall is { ResolvedTargets.Length: 1 } logicalCall)
					AddEdge(logicalCall.ResolvedTargets[0]);
				if (instruction.Operation == M68kMachineOperation.TypeInitialize &&
					instruction.Origin?.SourceInstruction is { Operand: int } source)
				{
					var initializer = _module.GetTriggeredTypeInitializer(
						instruction.Origin.SourceMethod,
						source);
					if (initializer is not null) AddEdge(initializer.Identity);
				}

				void AddEdge(CilMethodIdentity targetIdentity)
				{
					var physicalTarget = PhysicalMethodIdentity(targetIdentity);
					if (indexByIdentity.TryGetValue(physicalTarget, out var target) &&
						target != caller)
						edges.Add(new(caller, target));
				}
			}
		}

		var estimatedSizes = physicalMethods.Select(method =>
		{
			if (!functions.TryGetValue(method.Identity, out var function)) return 2;
			var instructions = function.Blocks
				.SelectMany(static block => block.Instructions)
				.ToArray();
			var estimated = M68kTargetCostModel.Estimate(
				instructions,
				_request.Cpu).Bytes;
			return checked((int)Math.Clamp(estimated, 2, int.MaxValue));
		}).ToArray();
		var order = M68kCallGraphLayout.Plan(
			estimatedSizes,
			edges,
			clusterCapacity: 12_000);
		return order
			.Select(index => physicalMethods[index])
			.Concat(methods.Where(method =>
				_foldedMethodAliases.ContainsKey(method.Identity)))
			.ToArray();

		CilMethodIdentity PhysicalMethodIdentity(CilMethodIdentity identity) =>
			_foldedMethodAliases.TryGetValue(identity, out var canonical)
				? canonical.Identity
				: identity;
	}
}
