/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text;
using System.Reflection;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Compiler.Output;

namespace CopperSharp.Compiler;

/// <summary>Compiles a closed set of CIL methods into a linked 68k image.</summary>
public static class M68kCompiler
{
	private static readonly string CompilerPackageVersion = GetPackageVersion(
		typeof(M68kCompiler).Assembly);

	/// <summary>
	/// Analyzes the reachable framework surface without generating or linking target code.
	/// </summary>
	public static M68kFrameworkAnalysisResult AnalyzeFramework(M68kCompilationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.AssemblyPath);
		ValidateRuntimeOptions(request);
		var implementationPack = FrameworkImplementationPackLoader.Load(
			request.FrameworkImplementationPack);

		using var module = new CompilationModule(
			request.AssemblyPath,
			request.ExternalCallResolvers,
			GetManagedAssemblyPaths(request),
			implementationPack);
		var entry = module.ResolveEntryPoint(request.EntryPoint);
		var exports = SelectExports(module, request.IncludedExportNames);
		var managedPoolRuntime = GetEffectiveMemoryManagement(request) ==
				M68kMemoryManagement.ManagedPoolMarkSweepGc
				? ResolveManagedPoolRuntime(module)
				: null;

		var (analysis, _) = AnalyzeFrameworkAndLifecycles(
			module,
			entry,
			exports,
			managedPoolRuntime,
			request);
		return analysis;
	}

	/// <summary>Compiles and links one assembly.</summary>
	public static M68kCompilationResult Compile(M68kCompilationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.AssemblyPath);
		ValidateRuntimeOptions(request);

		var managedAssemblyPaths = GetManagedAssemblyPaths(request);
		var implementationPack = FrameworkImplementationPackLoader.Load(
			request.FrameworkImplementationPack);

		using var module = new CompilationModule(
			request.AssemblyPath,
			request.ExternalCallResolvers,
			managedAssemblyPaths,
			implementationPack);
		var entry = module.ResolveEntryPoint(request.EntryPoint);
		var exports = SelectExports(module, request.IncludedExportNames);
		var managedPoolRuntime = GetEffectiveMemoryManagement(request) ==
				M68kMemoryManagement.ManagedPoolMarkSweepGc
				? ResolveManagedPoolRuntime(module)
				: null;
		var (frameworkAnalysis, managedLifecycles) = AnalyzeFrameworkAndLifecycles(
			module,
			entry,
			exports,
			managedPoolRuntime,
			request);
		ThrowIfFrameworkIncompatible(frameworkAnalysis);
		var frameworkFeatures = frameworkAnalysis.Members
			.SelectMany(static member => member.RequiredFeatures)
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();
		M68kStaticAnalyzer.Analyze(
			module,
			entry,
			request,
			exports,
			managedLifecycles.SelectMany(static lifecycle => lifecycle.Methods));
		var generated = new M68kCodeGenerator(
			module,
			request,
			exports,
			managedPoolRuntime,
			managedLifecycles).Generate(entry);

		return request.OutputFormat switch
		{
			M68kOutputFormat.Hunk => LinkHunk(generated, request, frameworkFeatures, frameworkAnalysis),
			M68kOutputFormat.KickstartRom => LinkRom(generated, request, frameworkFeatures, frameworkAnalysis),
			M68kOutputFormat.Assembly => WriteAssembly(generated, request, frameworkFeatures, frameworkAnalysis),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				$"Unknown output format {request.OutputFormat}.")
		};
	}

	private static IReadOnlyList<CilExport> SelectExports(
		CompilationModule module,
		IReadOnlyList<string>? includedExportNames)
	{
		var exports = module.GetExports();
		if (includedExportNames is null)
		{
			return exports;
		}

		var requested = new HashSet<string>(StringComparer.Ordinal);
		foreach (var name in includedExportNames)
		{
			if (string.IsNullOrWhiteSpace(name) || !requested.Add(name))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"Included export names must be non-empty and unique.");
			}
		}

		var selected = exports
			.Where(export => requested.Contains(export.Name))
			.ToArray();
		if (selected.Length != requested.Count)
		{
			var declared = exports.Select(static export => export.Name)
				.ToHashSet(StringComparer.Ordinal);
			var missing = requested.Where(name => !declared.Contains(name))
				.Order(StringComparer.Ordinal);
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Included export name(s) are not declared: {string.Join(", ", missing)}.");
		}
		return selected;
	}

	private static (
		M68kFrameworkAnalysisResult Analysis,
		IReadOnlyList<ManagedLifecycleModule> Lifecycles)
		AnalyzeFrameworkAndLifecycles(
			CompilationModule module,
			CilMethod entry,
			IReadOnlyList<CilExport> exports,
			ManagedPoolRuntimeModule? managedPoolRuntime,
			M68kCompilationRequest request)
	{
		var baseline = FrameworkReachabilityAnalyzer.Analyze(
			module,
			entry,
			exports,
			managedPoolRuntime,
			request.FloatingPoint);
		if (HasIncompatibleFrameworkMember(baseline))
		{
			return (baseline, Array.Empty<ManagedLifecycleModule>());
		}

		var baselineFeatures = baseline.Members
			.SelectMany(static member => member.RequiredFeatures)
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		var lifecycles = ResolveManagedLifecycles(
			module,
			request,
			baselineFeatures);
		if (lifecycles.Count == 0)
		{
			return (baseline, lifecycles);
		}

		var augmented = FrameworkReachabilityAnalyzer.Analyze(
			module,
			entry,
			exports,
			managedPoolRuntime,
			request.FloatingPoint,
			lifecycles.SelectMany(static lifecycle => lifecycle.Methods));
		return (augmented, lifecycles);
	}

	private static bool HasIncompatibleFrameworkMember(
		M68kFrameworkAnalysisResult analysis) =>
		analysis.Members.Any(static member => member.Status is
			M68kFrameworkCompatibilityStatus.Deferred or
			M68kFrameworkCompatibilityStatus.Unsupported);

	private static void ThrowIfFrameworkIncompatible(
		M68kFrameworkAnalysisResult analysis)
	{
		var unsupported = analysis.Members.FirstOrDefault(static member =>
			member.Status is
				M68kFrameworkCompatibilityStatus.Deferred or
				M68kFrameworkCompatibilityStatus.Unsupported);
		if (unsupported is null)
		{
			return;
		}

		var callSite = unsupported.CallSites.FirstOrDefault();
		var path = callSite is null
			? "<unknown>"
			: string.Join(" -> ", callSite.RootPath.Append(unsupported.Member.DisplayName));
		var reason = string.IsNullOrWhiteSpace(unsupported.Reason)
			? string.Empty
			: $" {unsupported.Reason}";
		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedFrameworkMember,
			$"Reachable .NET framework member '{unsupported.Member.DisplayName}' is " +
				$"{unsupported.Status.ToString().ToLowerInvariant()}.{reason} " +
				$"Root path: {path}",
			callSite?.Caller,
			callSite?.IlOffset);
	}

	private static List<string> GetManagedAssemblyPaths(M68kCompilationRequest request)
	{
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

		return managedAssemblyPaths;
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
			Method("MarkRootsExtended"),
			Method("CollectWithRoots"),
			Method("CollectWithRootsExtended"),
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

	private static IReadOnlyList<ManagedLifecycleModule> ResolveManagedLifecycles(
		CompilationModule module,
		M68kCompilationRequest request,
		IReadOnlyCollection<string> frameworkFeatures)
	{
		if (request.RuntimeProfile != M68kRuntimeProfile.Application ||
			request.ManagedLifecycleHooks.Count == 0)
		{
			return Array.Empty<ManagedLifecycleModule>();
		}

		var reachableFeatures = frameworkFeatures.ToHashSet(StringComparer.Ordinal);
		var result = new List<ManagedLifecycleModule>();
		foreach (var hook in request.ManagedLifecycleHooks)
		{
			if (!reachableFeatures.Contains(hook.RequiredFrameworkFeature))
			{
				continue;
			}

			var initialize = module.ResolveManagedMethod(
				hook.AssemblyName,
				hook.InitializeMethod);
			var shutdown = module.ResolveManagedMethod(
				hook.AssemblyName,
				hook.ShutdownMethod);
			ValidateLifecycleMethod(initialize, "initialize");
			ValidateLifecycleMethod(shutdown, "shutdown");
			result.Add(new ManagedLifecycleModule(
				hook.RequiredFrameworkFeature,
				initialize,
				shutdown));
		}
		return result;
	}

	private static void ValidateLifecycleMethod(CilMethod method, string role)
	{
		if (!method.IsImport &&
			!method.Signature.Header.IsInstance &&
			method.ParameterCount == 0 &&
			method.Signature.ReturnType.IsVoid)
		{
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.InvalidMetadata,
			$"Managed lifecycle {role} method must be a managed static void method with no parameters.",
			method.DisplayName);
	}

	internal static M68kMemoryManagement GetEffectiveMemoryManagement(
		M68kCompilationRequest request) =>
		request.MemoryManagement ??
		(request.RuntimeProfile == M68kRuntimeProfile.Rom
			? M68kMemoryManagement.None
			: M68kMemoryManagement.ExternalAllocator);

	private static void ValidateRuntimeOptions(M68kCompilationRequest request)
	{
		if (request.TargetContract is { } target &&
			(!IsSingleLineValue(target.RuntimeIdentifier) ||
			 !IsSingleLineValue(target.PackageId) ||
			 !IsSingleLineValue(target.PackageVersion)))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				"Target provenance requires non-empty, single-line runtime, package, and version values.");
		}

		var lifecycleHooks = new HashSet<M68kManagedLifecycleHook>();
		foreach (var hook in request.ManagedLifecycleHooks)
		{
			if (string.IsNullOrWhiteSpace(hook.RequiredFrameworkFeature) ||
				string.IsNullOrWhiteSpace(hook.AssemblyName) ||
				string.IsNullOrWhiteSpace(hook.InitializeMethod) ||
				string.IsNullOrWhiteSpace(hook.ShutdownMethod))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidOutputOptions,
					"Managed lifecycle hooks require non-empty feature, assembly, initialize, and shutdown identities.");
			}
			if (!lifecycleHooks.Add(hook))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidOutputOptions,
					$"Duplicate managed lifecycle hook for '{hook.RequiredFrameworkFeature}'.");
			}
		}

		if (request.FloatingPoint == M68kFloatingPointMode.M68040 &&
			request.Cpu is not M68kCpuTarget.M68040 and not M68kCpuTarget.M68060)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				"M68040 floating-point mode requires an M68040 or M68060 CPU target.");
		}

		if (request.FloatingPoint == M68kFloatingPointMode.M68882 &&
			request.Cpu != M68kCpuTarget.M68020)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				"M68882 floating-point mode requires the M68020 CPU target.");
		}

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

	private static bool IsSingleLineValue(string? value) =>
		!string.IsNullOrWhiteSpace(value) &&
		value.IndexOfAny(['\r', '\n']) < 0;

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
		M68kCompilationRequest request,
		IReadOnlyList<string> frameworkFeatures,
		M68kFrameworkAnalysisResult frameworkAnalysis)
	{
		var imports = new Dictionary<string, uint>(request.Imports, StringComparer.Ordinal);
		foreach (var import in program.Assembler.ExternalTargets)
		{
			imports.TryAdd(import, 0);
		}

		var linked = program.Assembler.Link(0, imports);
		var symbols = CreateSymbols(program, linked, 0);
		var loopFootprints = M68kLoopFootprintAnalysis.Measure(
			program.LoopLayouts,
			linked.Labels,
			linked.AnalysisAnchors,
			0);
		var entryOffset = checked((uint)linked.Labels[program.EntryLabel]);
		var text = program.Assembler.RenderAssembly(request.Cpu);
		var image = Encoding.UTF8.GetBytes(text);
		return new M68kCompilationResult(
			image,
			linked.Bytes,
			entryOffset,
			symbols,
			linked.Relocations,
			CreateMap(
				request,
				frameworkAnalysis,
				entryOffset,
				image.Length,
				linked.Bytes.Length,
				symbols,
				linked.Relocations,
				loopFootprints,
				frameworkFeatures),
			text,
			program.AllocationStatistics.Values.ToArray(),
			program.TerminalDeadStoreStatistics.Values.ToArray(),
			loopFootprints,
			frameworkFeatures,
			frameworkAnalysis);
	}

	private static M68kCompilationResult LinkHunk(
		GeneratedProgram program,
		M68kCompilationRequest request,
		IReadOnlyList<string> frameworkFeatures,
		M68kFrameworkAnalysisResult frameworkAnalysis)
	{
		var linked = program.Assembler.Link(0, request.Imports);
		var symbols = CreateSymbols(program, linked, 0);
		var loopFootprints = M68kLoopFootprintAnalysis.Measure(
			program.LoopLayouts,
			linked.Labels,
			linked.AnalysisAnchors,
			0);
		var entryOffset = checked((uint)linked.Labels[program.EntryLabel]);
		var image = HunkWriter.Write(
			linked.Bytes,
			linked.Relocations,
			symbols,
			linked.Labels,
			linked.BssStartOffset,
			linked.PcRelativeTargets,
			request.Hunk);
		return new M68kCompilationResult(
			image,
			linked.Bytes,
			entryOffset,
			symbols,
			linked.Relocations,
			CreateMap(
				request,
				frameworkAnalysis,
				entryOffset,
				image.Length,
				linked.Bytes.Length,
				symbols,
				linked.Relocations,
				loopFootprints,
				frameworkFeatures),
			null,
			program.AllocationStatistics.Values.ToArray(),
			program.TerminalDeadStoreStatistics.Values.ToArray(),
			loopFootprints,
			frameworkFeatures,
			frameworkAnalysis);
	}

	private static M68kCompilationResult LinkRom(
		GeneratedProgram program,
		M68kCompilationRequest request,
		IReadOnlyList<string> frameworkFeatures,
		M68kFrameworkAnalysisResult frameworkAnalysis)
	{
		var romBase = KickstartRomWriter.GetBaseAddress(request.Rom);
		var codeOrigin = checked(romBase + 8);
		var linked = program.Assembler.Link(codeOrigin, request.Imports);
		var symbols = CreateSymbols(program, linked, codeOrigin);
		var loopFootprints = M68kLoopFootprintAnalysis.Measure(
			program.LoopLayouts,
			linked.Labels,
			linked.AnalysisAnchors,
			codeOrigin);
		var entryPoint = checked(codeOrigin + (uint)linked.Labels[program.EntryLabel]);
		var image = KickstartRomWriter.Write(linked.Bytes, entryPoint, request.Rom);
		return new M68kCompilationResult(
			image,
			linked.Bytes,
			entryPoint,
			symbols,
			linked.Relocations,
			CreateMap(
				request,
				frameworkAnalysis,
				entryPoint,
				image.Length,
				linked.Bytes.Length,
				symbols,
				linked.Relocations,
				loopFootprints,
				frameworkFeatures),
			null,
			program.AllocationStatistics.Values.ToArray(),
			program.TerminalDeadStoreStatistics.Values.ToArray(),
			loopFootprints,
			frameworkFeatures,
			frameworkAnalysis);
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
		if (linked.Labels.TryGetValue("runtime:method-table", out var methodTableOffset))
		{
			result.Add(new M68kSymbol(
				"__c68k_method_table",
				checked(origin + (uint)methodTableOffset),
				0));
		}

		return result
			.OrderBy(symbol => symbol.Address)
			.ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
			.ToArray();
	}

	private static string CreateMap(
		M68kCompilationRequest request,
		M68kFrameworkAnalysisResult frameworkAnalysis,
		uint entryPoint,
		int artifactBytes,
		int codeBytes,
		IReadOnlyList<M68kSymbol> symbols,
		IReadOnlyList<M68kRelocation> relocations,
		IReadOnlyList<M68kLoopFootprint> loopFootprints,
		IReadOnlyList<string> frameworkFeatures)
	{
		var target = request.TargetContract ?? new M68kTargetContract(
			"m68k",
			"CopperSharp.Compiler",
			CompilerPackageVersion);
		var contract = frameworkAnalysis.Contract;
		var map = new StringBuilder();
		map.AppendLine($"COMPILER CopperSharp.Compiler {CompilerPackageVersion}");
		map.AppendLine(
			$"CONTRACT {contract.TargetFramework}-{contract.ReferencePackVersion} " +
			$"{contract.ReferencePack} {contract.ReferencePackVersion}");
		map.AppendLine(
			$"TARGET {target.RuntimeIdentifier} {target.PackageId} {target.PackageVersion}");
		if (frameworkAnalysis.ImplementationPack is { } implementationPack)
		{
			map.AppendLine(
				$"IMPLEMENTATION PACK {implementationPack.PackId} {implementationPack.PackVersion} " +
				$"{implementationPack.RuntimeIdentifier} {implementationPack.ImplementationProfile}");
			foreach (var assembly in implementationPack.Assemblies)
			{
				map.AppendLine(
					$"IMPLEMENTATION ASSEMBLY {assembly.Name} {assembly.Version} " +
					$"pkt={assembly.PublicKeyToken} mvid={assembly.Mvid:D} sha256={assembly.Sha256}");
			}
		}
		map.AppendLine($"PROFILE {request.RuntimeProfile}");
		map.AppendLine($"CPU {request.Cpu}");
		map.AppendLine($"FORMAT {request.OutputFormat}");
		map.AppendLine($"ENTRY {entryPoint:X8}");
		map.AppendLine(
			$"METRICS artifact-bytes={artifactBytes} code-bytes={codeBytes} " +
			$"symbols={symbols.Count} relocations={relocations.Count} " +
			$"loops={loopFootprints.Count} framework-features={frameworkFeatures.Count} " +
			$"managed-allocation-sites={frameworkAnalysis.ManagedAllocationSites.Count}");
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

		map.AppendLine("LOOPS");
		foreach (var loop in loopFootprints)
		{
			map.AppendLine(
				$"{loop.HeaderAddress:X8} IL_{loop.HeaderIlOffset:X4} " +
				$"bytes={loop.InstructionBytes} span={loop.SpanBytes} " +
				$"lines={loop.CacheLineCount} " +
				$"fits256={(loop.FitsIn256ByteInstructionCache ? "yes" : "no")} " +
				loop.Method);
		}

		map.AppendLine("FRAMEWORK FEATURES");
		foreach (var feature in frameworkFeatures)
		{
			map.AppendLine(feature);
		}

		return map.ToString();
	}

	private static string GetPackageVersion(Assembly assembly)
	{
		var informationalVersion = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
			.InformationalVersion;
		if (!string.IsNullOrWhiteSpace(informationalVersion))
		{
			var metadataSeparator = informationalVersion.IndexOf('+');
			return metadataSeparator < 0
				? informationalVersion
				: informationalVersion[..metadataSeparator];
		}

		return assembly.GetName().Version?.ToString() ?? "unknown";
	}
}
