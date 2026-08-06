/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Framework;

internal static class FrameworkReachabilityAnalyzer
{
	public static M68kFrameworkAnalysisResult Analyze(
		CompilationModule module,
		CilMethod entry,
		IReadOnlyList<CilExport> exports,
		ManagedPoolRuntimeModule? managedPoolRuntime)
	{
		var contract = Net10FrameworkContract.Default;
		var members = new Dictionary<MemberObservationKey, MemberAccumulator>();
		var managedAllocationSites = new HashSet<M68kManagedAllocationSite>();
		var discoveredPaths = new Dictionary<CilMethodIdentity, IReadOnlyList<string>>();
		var reachableDispatchLayouts = new Dictionary<CilTypeIdentity, CilTypeLayout>();
		var usedVirtualDeclarations = new Dictionary<
			CilMethodIdentity,
			(CilMethod Method, IReadOnlyList<string> RootPath)>();
		var pending = new Queue<ReachableMethod>();
		EnqueueRoot(entry);
		foreach (var export in exports)
		{
			EnqueueRoot(export.Method);
		}
		if (managedPoolRuntime is not null)
		{
			foreach (var method in managedPoolRuntime.Methods)
			{
				EnqueueRoot(method);
			}
		}

	ProcessPending:
		while (pending.TryDequeue(out var reachable))
		{
			var method = reachable.Method;
			if (method.IsImport)
			{
				continue;
			}

			foreach (var instruction in method.Instructions)
			{
				if (instruction.OpCode == OpCodes.Ldftn ||
					instruction.OpCode == OpCodes.Ldvirtftn)
				{
					var functionTarget = module.ResolveMethodToken(
						(int)instruction.Operand!,
						method,
						instruction.Offset);
					if (instruction.OpCode == OpCodes.Ldvirtftn &&
						functionTarget.Definition is { IsImport: false, DeclaringTypeIsInterface: true }
							interfaceTarget)
					{
						foreach (var implementation in module.GetInterfaceTableImplementations(interfaceTarget))
						{
							EnqueueChild(implementation, reachable.RootPath);
						}
					}
					else if (instruction.OpCode == OpCodes.Ldvirtftn &&
						functionTarget.Definition is { IsImport: false } virtualTarget &&
						virtualTarget.IsVirtual &&
						!virtualTarget.IsFinal &&
						!virtualTarget.DeclaringTypeIsSealed)
					{
						usedVirtualDeclarations.TryAdd(
							virtualTarget.Identity,
							(virtualTarget, reachable.RootPath));
						foreach (var implementation in module.GetVirtualImplementations(virtualTarget))
						{
							EnqueueChild(implementation, reachable.RootPath);
						}
					}
					else if (functionTarget.Definition is { IsImport: false } delegateTarget)
					{
						EnqueueChild(delegateTarget, reachable.RootPath);
					}
					continue;
				}

				if (instruction.OpCode == OpCodes.Newarr)
				{
					var elementType = module.ResolveTypeToken(
						(int)instruction.Operand!,
						method,
						instruction.Offset);
					managedAllocationSites.Add(new M68kManagedAllocationSite(
						method.DisplayName,
						instruction.Offset,
						"array",
						elementType.DisplayName + "[]",
						reachable.RootPath.ToArray()));
					continue;
				}

				if (!IsCallInstruction(instruction))
				{
					continue;
				}

				var token = (int)instruction.Operand!;
				var exactIdentity = module.DescribeFrameworkMethodToken(
					token,
					method,
					instruction.Offset);
				var description = module.DescribeMethodToken(
					token,
					method,
					instruction.Offset);
				var isFrameworkReference =
					contract.IsFrameworkAssembly(exactIdentity.AssemblyName);
				MethodReference? target = null;
				M68kCompilationException? resolutionFailure = null;
				try
				{
					target = module.ResolveMethodToken(token, method, instruction.Offset);
				}
				catch (M68kCompilationException exception) when (isFrameworkReference)
				{
					resolutionFailure = exception;
				}

				if (description is not null && isFrameworkReference)
				{
					var decision = contract.Classify(
						exactIdentity,
						description,
						target,
						resolutionFailure);
					AddObservation(
						members,
						exactIdentity,
						description,
						decision,
						method.DisplayName,
						instruction.Offset,
						reachable.RootPath);
				}

				if (instruction.OpCode == OpCodes.Newobj &&
					target?.ImportName?.StartsWith(
						"intrinsic:nullable-ctor:",
						StringComparison.Ordinal) != true &&
					!IsSpanValueConstructor(target?.ImportName) &&
					(target?.Definition is null ||
					 !module.IsValueTypeConstructor(target.Definition)) &&
					(description is not null || target?.Definition is not null))
				{
					var allocatedType = description?.TypeName ??
						GetDeclaringTypeName(target!.Definition!.DisplayName);
					managedAllocationSites.Add(new M68kManagedAllocationSite(
						method.DisplayName,
						instruction.Offset,
						"object",
						allocatedType,
						reachable.RootPath.ToArray()));
				}
				if (instruction.OpCode == OpCodes.Newobj &&
					target?.Definition is { IsImport: false } constructor)
				{
					var layout = module.GetTypeLayout(constructor);
					reachableDispatchLayouts.TryAdd(layout.Identity, layout);
				}

				if (target?.ImportName is
					"intrinsic:runtime-allocate-string" or
					"intrinsic:string-concat-two" or
					"intrinsic:string-substring")
				{
					managedAllocationSites.Add(new M68kManagedAllocationSite(
						method.DisplayName,
						instruction.Offset,
						"string",
						"string",
						reachable.RootPath.ToArray()));
				}
				if (target?.ImportName == "intrinsic:string-to-char-array")
				{
					managedAllocationSites.Add(new M68kManagedAllocationSite(
						method.DisplayName,
						instruction.Offset,
						"array",
						"char[]",
						reachable.RootPath.ToArray()));
				}

				if (target is null)
				{
					continue;
				}
				if (target.Definition is { IsImport: false } virtualDefinition &&
					RequiresVirtualDispatch(instruction, virtualDefinition))
				{
					usedVirtualDeclarations.TryAdd(
						virtualDefinition.Identity,
						(virtualDefinition, reachable.RootPath));
				}

				EnqueueTargets(
					module,
					instruction,
					target,
					candidate => EnqueueChild(candidate, reachable.RootPath));
			}
		}

		var queuedClosedVirtual = false;
		foreach (var layout in reachableDispatchLayouts.Values)
		{
			foreach (var item in usedVirtualDeclarations.Values)
			{
				var implementation = module.TryGetVirtualImplementation(
					layout,
					item.Method);
				if (implementation is not null &&
					EnqueueChild(implementation, item.RootPath))
				{
					queuedClosedVirtual = true;
				}
			}
		}
		if (queuedClosedVirtual)
		{
			goto ProcessPending;
		}

		void EnqueueRoot(CilMethod method)
		{
			var path = new[] { method.DisplayName };
			if (discoveredPaths.TryAdd(method.Identity, path))
			{
				pending.Enqueue(new ReachableMethod(method, path));
			}
		}

		bool EnqueueChild(CilMethod method, IReadOnlyList<string> parentPath)
		{
			if (discoveredPaths.ContainsKey(method.Identity))
			{
				return false;
			}
			var path = parentPath.Append(method.DisplayName).ToArray();
			discoveredPaths.Add(method.Identity, path);
			pending.Enqueue(new ReachableMethod(method, path));
			return true;
		}

		return new M68kFrameworkAnalysisResult(
			contract.Contract,
			members.Values
				.OrderBy(static member => member.Member.AssemblyName, StringComparer.Ordinal)
				.ThenBy(static member => member.Member.TypeName, StringComparer.Ordinal)
				.ThenBy(static member => member.Member.Name, StringComparer.Ordinal)
				.ThenBy(static member => member.Member.Key, StringComparer.Ordinal)
				.ThenBy(static member => member.Status)
				.ThenBy(static member => member.Binding, StringComparer.Ordinal)
				.Select(static member => member.ToAnalysis())
				.ToArray(),
			managedAllocationSites
				.OrderBy(static site => site.Caller, StringComparer.Ordinal)
				.ThenBy(static site => site.IlOffset)
				.ThenBy(static site => site.Kind, StringComparer.Ordinal)
				.ThenBy(static site => site.AllocatedType, StringComparer.Ordinal)
				.ToArray());
	}

	private static void EnqueueTargets(
		CompilationModule module,
		CilInstruction instruction,
		MethodReference target,
		Action<CilMethod> enqueue)
	{
		if (target.Definition is { IsImport: false, DeclaringTypeIsInterface: true } interfaceMethod)
		{
			foreach (var implementation in module.GetInterfaceTableImplementations(interfaceMethod))
			{
				enqueue(implementation);
			}
			return;
		}

		if (target.Definition is { IsImport: false } definition &&
			RequiresVirtualDispatch(instruction, definition))
		{
			foreach (var implementation in module.GetVirtualImplementations(definition))
			{
				enqueue(implementation);
			}
			return;
		}

		if (target.Definition is { IsImport: false } directDefinition)
		{
			enqueue(directDefinition);
		}
	}

	private static void AddObservation(
		IDictionary<MemberObservationKey, MemberAccumulator> members,
		FrameworkMemberId exactIdentity,
		CilMethodReferenceIdentity identity,
		FrameworkBindingDecision decision,
		string caller,
		int ilOffset,
		IReadOnlyList<string> rootPath)
	{
		var key = MemberObservationKey.Create(exactIdentity, decision);
		if (!members.TryGetValue(key, out var accumulator))
		{
			accumulator = new MemberAccumulator(exactIdentity, identity, decision);
			members.Add(key, accumulator);
		}
		accumulator.AddCallSite(caller, ilOffset, rootPath);
	}

	private sealed record MemberObservationKey(
		FrameworkMemberId ExactIdentity,
		M68kFrameworkCompatibilityStatus Status,
		string? Binding,
		string? Reason,
		string Effects,
		string RequiredFeatures)
	{
		public static MemberObservationKey Create(
			FrameworkMemberId exactIdentity,
			FrameworkBindingDecision decision) =>
			new(
				exactIdentity,
				decision.Status,
				decision.Binding,
				decision.Reason,
				string.Join('\n', decision.Effects),
				string.Join('\n', decision.RequiredFeatures));
	}

	private static bool IsCallInstruction(CilInstruction instruction) =>
		instruction.OpCode == OpCodes.Call ||
		instruction.OpCode == OpCodes.Callvirt ||
		instruction.OpCode == OpCodes.Newobj;

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

	private static bool RequiresVirtualDispatch(CilInstruction instruction, CilMethod method) =>
		instruction.OpCode == OpCodes.Callvirt &&
		method.IsVirtual &&
		!method.IsFinal &&
		!method.DeclaringTypeIsSealed;

	private static string GetDeclaringTypeName(string methodDisplayName)
	{
		var separator = methodDisplayName.LastIndexOf("::", StringComparison.Ordinal);
		return separator < 0 ? methodDisplayName : methodDisplayName[..separator];
	}

	private sealed class MemberAccumulator
	{
		private readonly FrameworkBindingDecision _decision;
		private readonly HashSet<M68kFrameworkCallSite> _callSites = [];

		public MemberAccumulator(
			FrameworkMemberId exactIdentity,
			CilMethodReferenceIdentity identity,
			FrameworkBindingDecision decision)
		{
			ExactIdentity = exactIdentity;
			Member = identity;
			_decision = decision;
		}

		public CilMethodReferenceIdentity Member { get; }

		public FrameworkMemberId ExactIdentity { get; }

		public M68kFrameworkCompatibilityStatus Status => _decision.Status;

		public string? Binding => _decision.Binding;

		public void VerifyDecision(FrameworkBindingDecision decision)
		{
			if (_decision.Status != decision.Status ||
				!string.Equals(_decision.Binding, decision.Binding, StringComparison.Ordinal) ||
				!string.Equals(_decision.Reason, decision.Reason, StringComparison.Ordinal) ||
				!_decision.Effects.SequenceEqual(decision.Effects) ||
				!_decision.RequiredFeatures.SequenceEqual(decision.RequiredFeatures))
			{
				throw new InvalidOperationException(
					$"Framework member '{Member.Key}' received inconsistent compatibility decisions.");
			}
		}

		public void AddCallSite(
			string caller,
			int ilOffset,
			IReadOnlyList<string> rootPath) =>
			_callSites.Add(new M68kFrameworkCallSite(
				caller,
				ilOffset,
				rootPath.ToArray()));

		public M68kFrameworkMemberAnalysis ToAnalysis() =>
			new(
				new M68kFrameworkMember(
					Member.AssemblyName,
					Member.TypeName,
					Member.Name,
					Member.IsStatic,
					Member.GenericArity,
					Member.ReturnType,
					Member.ParameterTypes.ToArray(),
					Member.MethodTypeArguments.ToArray()),
				_decision.Status,
				_decision.Binding,
				_decision.Reason,
				_decision.Effects.ToArray(),
				_decision.RequiredFeatures.ToArray(),
				_callSites
					.OrderBy(static callSite => callSite.Caller, StringComparer.Ordinal)
					.ThenBy(static callSite => callSite.IlOffset)
					.ToArray());
	}

	private sealed record ReachableMethod(
		CilMethod Method,
		IReadOnlyList<string> RootPath);
}
