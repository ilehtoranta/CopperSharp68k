/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler;

internal static class M68kStaticAnalyzer
{
	public static void Analyze(CompilationModule module, CilMethod entry, M68kCompilationRequest request)
	{
		var memoryManagement = M68kCompiler.GetEffectiveMemoryManagement(request);
		var visited = new HashSet<CilMethod>();
		var pending = new Queue<CilMethod>();
		pending.Enqueue(entry);
		foreach (var export in module.GetExports())
		{
			pending.Enqueue(export.Method);
		}

		while (pending.TryDequeue(out var method))
		{
			if (!visited.Add(method) || method.IsImport)
			{
				continue;
			}

			foreach (var instruction in method.Instructions)
			{
				AnalyzeInstruction(module, method, instruction, request, memoryManagement, pending);
			}
		}
	}

	private static void AnalyzeInstruction(
		CompilationModule module,
		CilMethod method,
		CilInstruction instruction,
		M68kCompilationRequest request,
		M68kMemoryManagement memoryManagement,
		Queue<CilMethod> pending)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Newobj || op == OpCodes.Newarr)
		{
			RequireManagedHeap(method, instruction, memoryManagement, "managed allocation");
		}

		if (op != OpCodes.Call && op != OpCodes.Callvirt && op != OpCodes.Newobj)
		{
			return;
		}

		var target = module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
		if (target.Definition is { IsImport: false } definition)
		{
			pending.Enqueue(definition);
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
