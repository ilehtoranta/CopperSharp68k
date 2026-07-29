/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Compiler.Output;

namespace CopperSharp.Compiler;

/// <summary>Compiles a closed set of CIL methods into a linked 68k image.</summary>
public static class M68kCompiler
{
	/// <summary>Compiles and links one assembly.</summary>
	public static M68kCompilationResult Compile(M68kCompilationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.AssemblyPath);
		ValidateRuntimeOptions(request);

		var managedAssemblyPaths = request.ManagedAssemblyPaths.ToList();
		var configuredManagedRuntimePath = request.ManagedAssemblyPaths.FirstOrDefault(path =>
			string.Equals(
				Path.GetFileName(path),
				"CopperSharp.Runtime.Managed.dll",
				StringComparison.OrdinalIgnoreCase));
		var managedRuntimePath = configuredManagedRuntimePath ??
			Path.Combine(AppContext.BaseDirectory, "CopperSharp.Runtime.Managed.dll");
		if (GetEffectiveMemoryManagement(request) == M68kMemoryManagement.ManagedPoolMarkSweepGc &&
			!File.Exists(managedRuntimePath))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidInput,
				$"ManagedPoolMarkSweepGc requires '{managedRuntimePath}'. " +
				"Add CopperSharp.Runtime.Managed.dll to ManagedAssemblyPaths or the compiler output directory.");
		}
		if (File.Exists(managedRuntimePath))
		{
			if (!managedAssemblyPaths.Contains(managedRuntimePath, StringComparer.OrdinalIgnoreCase))
			{
				managedAssemblyPaths.Add(managedRuntimePath);
			}
		}

		using var module = new CompilationModule(
			request.AssemblyPath,
			request.ExternalCallResolvers,
			managedAssemblyPaths);
		var entry = module.ResolveEntryPoint(request.EntryPoint);
		var managedPoolRuntime = GetEffectiveMemoryManagement(request) ==
				M68kMemoryManagement.ManagedPoolMarkSweepGc
				? ResolveManagedPoolRuntime(module)
				: null;
		M68kStaticAnalyzer.Analyze(module, entry, request);
		var generated = new M68kCodeGenerator(
			module,
			request,
			managedPoolRuntime).Generate(entry);

		return request.OutputFormat switch
		{
			M68kOutputFormat.Hunk => LinkHunk(generated, request),
			M68kOutputFormat.KickstartRom => LinkRom(generated, request),
			M68kOutputFormat.Assembly => WriteAssembly(generated, request),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				$"Unknown output format {request.OutputFormat}.")
		};
	}

	private static ManagedPoolRuntimeModule ResolveManagedPoolRuntime(CompilationModule module)
	{
		const string assembly = "CopperSharp.Runtime.Managed";
		const string type = "CopperSharp.Runtime.ManagedPool";
		CilMethod Method(string name) =>
			module.ResolveManagedMethod(assembly, $"{type}::{name}");
		CilField Field(string name) =>
			module.ResolveManagedField(assembly, type, name);

		return new ManagedPoolRuntimeModule(
			Method("Initialize"),
			Method("GetAllocationSize"),
			Method("Allocate"),
			Method("Dispose"),
			Method("Mark"),
			Method("MarkRoots"),
			Method("CollectWithRoots"),
			Method("Collect"),
			Method("Coalesce"),
			Method("GetStaleBytes"),
			Method("GetStaleBlocks"),
			Method("Shutdown"),
			Field("HeapStart"),
			Field("HeapEnd"),
			Field("FreeHead"),
			Field("AllocatedHead"),
			Field("StaleBytes"),
			Field("StaleBlocks"),
			Field("StaleBytesThreshold"),
			Field("StaleBlocksThreshold"));
	}

	internal static M68kMemoryManagement GetEffectiveMemoryManagement(
		M68kCompilationRequest request) =>
		request.MemoryManagement ??
		(request.RuntimeProfile == M68kRuntimeProfile.Rom
			? M68kMemoryManagement.None
			: M68kMemoryManagement.ExternalAllocator);

	private static void ValidateRuntimeOptions(M68kCompilationRequest request)
	{
		if (request.RuntimeProfile == M68kRuntimeProfile.Rom &&
			request.OutputFormat != M68kOutputFormat.KickstartRom)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				"ROM runtime profile requires Kickstart ROM output.");
		}

		if (GetEffectiveMemoryManagement(request) == M68kMemoryManagement.BumpAllocator)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				"Built-in bump allocator startup code is not implemented yet; use ExternalAllocator for now.");
		}

		if (request.GcSweepStrategy is not null && !IsManagedRuntime(request))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				"GC sweep strategy requires ManagedPoolMarkSweepGc or ExecPoolMarkSweepGc memory management.");
		}
	}

	internal static M68kGcSweepStrategy GetEffectiveGcSweepStrategy(
		M68kCompilationRequest request) =>
		request.GcSweepStrategy ?? M68kGcSweepStrategy.OnAllocationFailure;

	internal static bool IsManagedRuntime(M68kCompilationRequest request)
	{
		var memoryManagement = GetEffectiveMemoryManagement(request);
		return memoryManagement is
			M68kMemoryManagement.ManagedPoolMarkSweepGc or
			M68kMemoryManagement.ExecPoolMarkSweepGc;
	}

	private static M68kCompilationResult WriteAssembly(
		GeneratedProgram program,
		M68kCompilationRequest request)
	{
		var imports = new Dictionary<string, uint>(request.Imports, StringComparer.Ordinal);
		foreach (var import in program.Assembler.ExternalTargets)
		{
			imports.TryAdd(import, 0);
		}

		var linked = program.Assembler.Link(0, imports);
		var symbols = CreateSymbols(program, linked, 0);
		var entryOffset = checked((uint)linked.Labels[program.EntryLabel]);
		var text = program.Assembler.RenderAssembly(request.Cpu);
		return new M68kCompilationResult(
			Encoding.UTF8.GetBytes(text),
			linked.Bytes,
			entryOffset,
			symbols,
			linked.Relocations,
			CreateMap(request, entryOffset, symbols, linked.Relocations),
			text);
	}

	private static M68kCompilationResult LinkHunk(
		GeneratedProgram program,
		M68kCompilationRequest request)
	{
		var linked = program.Assembler.Link(0, request.Imports);
		var symbols = CreateSymbols(program, linked, 0);
		var entryOffset = checked((uint)linked.Labels[program.EntryLabel]);
		var image = HunkWriter.Write(
			linked.Bytes,
			linked.Relocations,
			symbols,
			request.Hunk);
		return new M68kCompilationResult(
			image,
			linked.Bytes,
			entryOffset,
			symbols,
			linked.Relocations,
			CreateMap(request, entryOffset, symbols, linked.Relocations));
	}

	private static M68kCompilationResult LinkRom(
		GeneratedProgram program,
		M68kCompilationRequest request)
	{
		var romBase = KickstartRomWriter.GetBaseAddress(request.Rom);
		var codeOrigin = checked(romBase + 8);
		var linked = program.Assembler.Link(codeOrigin, request.Imports);
		var symbols = CreateSymbols(program, linked, codeOrigin);
		var entryPoint = checked(codeOrigin + (uint)linked.Labels[program.EntryLabel]);
		var image = KickstartRomWriter.Write(linked.Bytes, entryPoint, request.Rom);
		return new M68kCompilationResult(
			image,
			linked.Bytes,
			entryPoint,
			symbols,
			linked.Relocations,
			CreateMap(request, entryPoint, symbols, linked.Relocations));
	}

	private static IReadOnlyList<M68kSymbol> CreateSymbols(
		GeneratedProgram program,
		LinkedCode linked,
		uint origin)
	{
		var methodOffsets = new List<(string Name, int Offset)>();
		foreach (var method in program.Methods)
		{
			var label = program.MethodLabels[method.Identity];
			methodOffsets.Add((method.DisplayName, linked.Labels[label]));
		}

		methodOffsets.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
		var result = new List<M68kSymbol>(methodOffsets.Count);
		for (var index = 0; index < methodOffsets.Count; index++)
		{
			var current = methodOffsets[index];
			var end = index + 1 < methodOffsets.Count
				? methodOffsets[index + 1].Offset
				: linked.Bytes.Length;
			result.Add(new M68kSymbol(
				current.Name,
				checked(origin + (uint)current.Offset),
				end - current.Offset));
		}

		foreach (var export in program.Exports)
		{
			var label = M68kCodeGenerator.ExportLabel(export.Name);
			result.Add(new M68kSymbol(
				export.Name,
				checked(origin + (uint)linked.Labels[label]),
				0));
		}

		foreach (var platformBase in program.PlatformBases.Where(
			item => item.Binding.BaseSource == M68kExternalBaseSource.WritableSlot))
		{
			result.Add(new M68kSymbol(
				platformBase.Binding.SlotSymbol!,
				checked(origin + (uint)linked.Labels[platformBase.Label!]),
				4));
		}

		if (linked.Labels.TryGetValue("runtime:exception-table", out var exceptionTableOffset))
		{
			result.Add(new M68kSymbol(
				"__c68k_exception_table",
				checked(origin + (uint)exceptionTableOffset),
				0));
		}

		return result
			.OrderBy(symbol => symbol.Address)
			.ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
			.ToArray();
	}

	private static string CreateMap(
		M68kCompilationRequest request,
		uint entryPoint,
		IReadOnlyList<M68kSymbol> symbols,
		IReadOnlyList<M68kRelocation> relocations)
	{
		var map = new StringBuilder();
		map.AppendLine($"CPU {request.Cpu}");
		map.AppendLine($"FORMAT {request.OutputFormat}");
		map.AppendLine($"ENTRY {entryPoint:X8}");
		map.AppendLine("SYMBOLS");
		foreach (var symbol in symbols)
		{
			map.AppendLine($"{symbol.Address:X8} {symbol.Size,6} {symbol.Name}");
		}

		map.AppendLine("RELOCATIONS");
		foreach (var relocation in relocations)
		{
			map.AppendLine($"{relocation.Offset:X8} {relocation.Target}");
		}

		return map.ToString();
	}
}
