/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler;

internal static class M68kStaticAnalyzer
{
	public static void Analyze(
		CompilationModule module,
		CilMethod entry,
		M68kCompilationRequest request,
		IReadOnlyList<CilExport> exports,
		IEnumerable<CilMethod>? additionalRoots = null)
	{
		var memoryManagement = M68kCompiler.GetEffectiveMemoryManagement(request);
		var visited = new HashSet<CilMethod>();
		var pending = new Queue<CilMethod>();
		var reachableDispatchLayouts = new Dictionary<CilTypeIdentity, CilTypeLayout>();
		var usedVirtualDeclarations = new Dictionary<CilMethodIdentity, CilMethod>();
		pending.Enqueue(entry);
		foreach (var export in exports)
		{
			pending.Enqueue(export.Method);
		}
		foreach (var root in additionalRoots ?? Array.Empty<CilMethod>())
		{
			pending.Enqueue(root);
		}

	ProcessPending:
		while (pending.TryDequeue(out var method))
		{
			method = module.ApplyTargetRuntimeOverride(method);
			if (!visited.Add(method) || method.IsImport)
			{
				continue;
			}

			if (request.ExceptionMode == M68kExceptionMode.Yolo &&
				method.ExceptionRegions.Count != 0)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedInstruction,
					"YOLO exception mode does not compile methods containing managed exception regions; select Full mode.",
					method.DisplayName);
			}
			ValidateExceptionRegions(module, method);
			var suppressedEphemeralSpanCalls = method.Instructions
				.Where(static instruction => instruction.OpCode == OpCodes.Call)
				.Select(instruction =>
					EphemeralParamsArrayAnalyzer.AnalyzeReadOnlySpanParams(
						module,
						method,
						instruction.Offset))
				.OfType<EphemeralSpanParams>()
				.SelectMany(static format => format.SuppressedCallOffsets)
				.ToHashSet();

			foreach (var instruction in method.Instructions)
			{
				if (suppressedEphemeralSpanCalls.Contains(instruction.Offset))
				{
					continue;
				}
				AnalyzeInstruction(
					module,
					method,
					instruction,
					request,
					memoryManagement,
					pending,
					reachableDispatchLayouts,
					usedVirtualDeclarations);
			}
		}
		var queuedClosedVirtual = false;
		foreach (var layout in reachableDispatchLayouts.Values)
		{
			foreach (var declaration in usedVirtualDeclarations.Values)
			{
				var implementation = module.TryGetVirtualImplementation(layout, declaration);
				if (implementation is not null && !visited.Contains(implementation))
				{
					pending.Enqueue(implementation);
					queuedClosedVirtual = true;
				}
			}
		}
		if (queuedClosedVirtual)
		{
			goto ProcessPending;
		}
	}

	private static void ValidateExceptionRegions(CompilationModule module, CilMethod method)
	{
		for (var leftIndex = 0; leftIndex < method.ExceptionRegions.Count; leftIndex++)
		{
			var left = method.ExceptionRegions[leftIndex];
			if (left.IsCatch && !left.CatchType.IsNil)
			{
				_ = module.GetTypeDisplayName(left.CatchType);
			}

			for (var rightIndex = leftIndex + 1;
				rightIndex < method.ExceptionRegions.Count;
				rightIndex++)
			{
				var right = method.ExceptionRegions[rightIndex];
				if (PartiallyOverlaps(
					left.TryOffset,
					left.TryEnd,
					right.TryOffset,
					right.TryEnd) ||
					PartiallyOverlaps(
						left.HandlerOffset,
						left.HandlerEnd,
						right.HandlerOffset,
						right.HandlerEnd))
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidMetadata,
						"Exception regions must be disjoint or properly nested.",
						method.DisplayName,
						Math.Min(left.TryOffset, right.TryOffset));
				}
			}
		}

		foreach (var instruction in method.Instructions)
		{
			if (instruction.OpCode == OpCodes.Rethrow &&
				!method.ExceptionRegions.Any(region =>
					region.IsCatch &&
					region.HandlerOffset <= instruction.Offset &&
					instruction.Offset < region.HandlerEnd))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"Rethrow is only valid inside a catch handler.",
					method.DisplayName,
					instruction.Offset);
			}

			if (instruction.OpCode == OpCodes.Endfinally &&
				!method.ExceptionRegions.Any(region =>
					region.IsFinally &&
					region.HandlerOffset <= instruction.Offset &&
					instruction.Offset < region.HandlerEnd))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"Endfinally is only valid inside a finally handler.",
					method.DisplayName,
					instruction.Offset);
			}
		}
	}

	private static bool PartiallyOverlaps(
		int leftStart,
		int leftEnd,
		int rightStart,
		int rightEnd)
	{
		var overlaps = leftStart < rightEnd && rightStart < leftEnd;
		if (!overlaps)
		{
			return false;
		}

		var leftContainsRight = leftStart <= rightStart && rightEnd <= leftEnd;
		var rightContainsLeft = rightStart <= leftStart && leftEnd <= rightEnd;
		return !leftContainsRight && !rightContainsLeft;
	}

	private static void AnalyzeInstruction(
		CompilationModule module,
		CilMethod method,
		CilInstruction instruction,
		M68kCompilationRequest request,
		M68kMemoryManagement memoryManagement,
		Queue<CilMethod> pending,
		IDictionary<CilTypeIdentity, CilTypeLayout> reachableDispatchLayouts,
		IDictionary<CilMethodIdentity, CilMethod> usedVirtualDeclarations)
	{
		var op = instruction.OpCode;
		MethodReference? target = null;
		if (op == OpCodes.Newobj)
		{
			target = module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
		}

		if ((op == OpCodes.Newobj &&
			(target is null || target.Definition is not { } constructor ||
				!module.IsTransparentScalarConstructor(constructor))) ||
			op == OpCodes.Newarr)
		{
			RequireManagedHeap(method, instruction, memoryManagement, "managed allocation");
		}

		if (op != OpCodes.Call && op != OpCodes.Callvirt && op != OpCodes.Newobj)
		{
			return;
		}

		target ??= module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
		if (op == OpCodes.Newobj &&
			target.Definition is { IsImport: false } layoutConstructor)
		{
			var layout = module.GetTypeLayout(layoutConstructor);
			reachableDispatchLayouts.TryAdd(layout.Identity, layout);
		}
		ValidateCallDispatch(method, instruction, target);
		if (target.Definition is { IsImport: false, DeclaringTypeIsInterface: true } interfaceMethod)
		{
			if (instruction.ConstrainedTypeToken is { } constrainedTypeToken)
			{
				pending.Enqueue(module.ResolveConstrainedInterfaceImplementation(
					method,
					constrainedTypeToken,
					instruction.Offset,
					interfaceMethod));
			}
			else if (!interfaceMethod.Signature.Header.IsInstance && !interfaceMethod.IsAbstract)
			{
				pending.Enqueue(interfaceMethod);
			}
			else
			{
				foreach (var implementation in module.GetInterfaceImplementations(interfaceMethod))
				{
					pending.Enqueue(implementation);
				}
			}
		}
		else if (target.Definition is { IsImport: false } definition &&
			RequiresVirtualDispatch(instruction, definition))
		{
			usedVirtualDeclarations.TryAdd(definition.Identity, definition);
			foreach (var implementation in module.GetVirtualImplementations(definition))
			{
				pending.Enqueue(implementation);
			}
		}
		else if (target.Definition is { IsImport: false } directDefinition)
		{
			pending.Enqueue(directDefinition);
		}

		if (target.ImportName is not null &&
			IsRuntimeDisposeOperation(target.ImportName))
		{
			RequireManagedHeap(method, instruction, memoryManagement, "runtime heap operation");
			RequireDisposeRuntime(method, instruction, request, target.ImportName);
		}

		if (target.ImportName is not null &&
			IsRuntimeGcOperation(target.ImportName))
		{
			RequireGcRuntime(method, instruction, request, target.ImportName);
		}
	}

	private static void ValidateCallDispatch(
		CilMethod caller,
		CilInstruction instruction,
		MethodReference target)
	{
		if (target.Definition is not { } method)
		{
			return;
		}

		if (method.DeclaringTypeIsInterface &&
			method.Signature.Header.IsInstance &&
			instruction.OpCode != OpCodes.Callvirt)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				$"Interface method '{method.DisplayName}' must be invoked through instance callvirt dispatch.",
				caller.DisplayName,
				instruction.Offset);
		}

		if (method.DeclaringTypeIsInterface &&
			!method.Signature.Header.IsInstance &&
			method.IsAbstract &&
			instruction.ConstrainedTypeToken is null)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				$"Static abstract interface method '{method.DisplayName}' requires constrained dispatch.",
				caller.DisplayName,
				instruction.Offset);
		}

		if (method.IsAbstract &&
			!method.DeclaringTypeIsInterface &&
			!RequiresVirtualDispatch(instruction, method))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				$"Abstract method '{method.DisplayName}' must be invoked through class virtual dispatch.",
				caller.DisplayName,
				instruction.Offset);
		}
	}

	private static bool RequiresVirtualDispatch(CilInstruction instruction, CilMethod method) =>
		instruction.OpCode == OpCodes.Callvirt &&
		method.IsVirtual &&
		!method.IsFinal &&
		!method.DeclaringTypeIsSealed;

	private static bool IsRuntimeDisposeOperation(string name) =>
		name is "intrinsic:runtime-dispose" or
			M68kRuntimeImports.Dispose;

	private static bool IsRuntimeGcOperation(string name) =>
		name is "intrinsic:runtime-gc-collect" or
			"intrinsic:runtime-GetGcStaleBytes" or
			"intrinsic:runtime-GetGcStaleBlocks" or
			M68kRuntimeImports.GcCollect or
			M68kRuntimeImports.GcGetStaleBytes or
			M68kRuntimeImports.GcGetStaleBlocks;

	private static void RequireDisposeRuntime(
		CilMethod method,
		CilInstruction instruction,
		M68kCompilationRequest request,
		string importName)
	{
		if (M68kCompiler.GetEffectiveMemoryManagement(request) == M68kMemoryManagement.ManagedPoolMarkSweepGc ||
			request.Imports.ContainsKey(RuntimeImportFor(importName)))
		{
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.StaticAnalysis,
			"runtime dispose operation requires ManagedPoolMarkSweepGc or an explicit runtime dispose import.",
			method.DisplayName,
			instruction.Offset);
	}

	private static void RequireGcRuntime(
		CilMethod method,
		CilInstruction instruction,
		M68kCompilationRequest request,
		string importName)
	{
		if (M68kCompiler.IsManagedRuntime(request) ||
			request.Imports.ContainsKey(RuntimeImportFor(importName)))
		{
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.StaticAnalysis,
			"runtime GC operation requires ManagedPoolMarkSweepGc, ExecPoolMarkSweepGc, or an explicit runtime GC import.",
			method.DisplayName,
			instruction.Offset);
	}

	private static string RuntimeImportFor(string name) =>
		name switch
		{
			"intrinsic:runtime-dispose" => M68kRuntimeImports.Dispose,
			"intrinsic:runtime-gc-collect" => M68kRuntimeImports.GcCollect,
			"intrinsic:runtime-GetGcStaleBytes" => M68kRuntimeImports.GcGetStaleBytes,
			"intrinsic:runtime-GetGcStaleBlocks" => M68kRuntimeImports.GcGetStaleBlocks,
			_ => name
		};

	private static void RequireManagedHeap(
		CilMethod method,
		CilInstruction instruction,
		M68kMemoryManagement memoryManagement,
		string operation)
	{
		if (memoryManagement != M68kMemoryManagement.None)
		{
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.StaticAnalysis,
			$"{operation} requires a managed heap. Select ExternalAllocator, ManagedPoolMarkSweepGc, or ExecPoolMarkSweepGc memory management.",
			method.DisplayName,
			instruction.Offset);
	}
}
