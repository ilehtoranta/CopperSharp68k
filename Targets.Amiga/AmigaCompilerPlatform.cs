/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.ObjectModel;
using System.Reflection;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace CopperSharp.Targets.Amiga;

public sealed record AmigaCompilationOptions
{
	public IReadOnlyDictionary<string, uint> LibraryBases { get; init; } =
		new ReadOnlyDictionary<string, uint>(new Dictionary<string, uint>());

	public AmigaLibraryBasePolicy DefaultLibraryBasePolicy { get; init; } =
		AmigaLibraryBasePolicy.Manual;

	public IReadOnlyDictionary<string, AmigaLibraryBasePolicy> LibraryBasePolicies { get; init; } =
		new ReadOnlyDictionary<string, AmigaLibraryBasePolicy>(
			new Dictionary<string, AmigaLibraryBasePolicy>());
}

public static class AmigaLibraryBaseSymbols
{
	private static readonly IReadOnlyDictionary<string, string> KnownNames =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["amisslmaster"] = "AmiSSLMaster",
			["amigaguide"] = "AmigaGuide",
			["asl"] = "ASL",
			["bsdsocket"] = "BsdSocket",
			["camd"] = "CAMD",
			["cgxvideo"] = "CgxVideo",
			["bullet"] = "Bullet",
			["commodities"] = "Commodities",
			["cybergraphics"] = "CyberGraphics",
			["datatypes"] = "Datatypes",
			["diskfont"] = "Diskfont",
			["dos"] = "DOS",
			["exec"] = "Exec",
			["expansion"] = "Expansion",
			["gadtools"] = "GadTools",
			["graphics"] = "Graphics",
			["icon"] = "Icon",
			["iffparse"] = "IFFParse",
			["intuition"] = "Intuition",
			["keymap"] = "Keymap",
			["layers"] = "Layers",
			["locale"] = "Locale",
			["lowlevel"] = "LowLevel",
			["mathffp"] = "MathFfp",
			["mathieeedoubbas"] = "MathIeeeDoubBas",
			["mathieeedoubtrans"] = "MathIeeeDoubTrans",
			["mathieeesingbas"] = "MathIeeeSingBas",
			["mathieeesingtrans"] = "MathIeeeSingTrans",
			["mathtrans"] = "MathTrans",
			["muimaster"] = "MUIMaster",
			["nonvolatile"] = "Nonvolatile",
			["realtime"] = "Realtime",
			["rexxsupport"] = "RexxSupport",
			["rexxsyslib"] = "RexxSysLib",
			["timerdevice"] = "TimerDevice",
			["ttengine"] = "TTEngine",
			["utility"] = "Utility",
			["version"] = "Version",
			["workbench"] = "Workbench",
			["xadmaster"] = "XadMaster",
			["xpkmaster"] = "XpkMaster"
		};

	public static string For(string libraryName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(libraryName);
		var root = libraryName.EndsWith(".library", StringComparison.OrdinalIgnoreCase)
			? libraryName[..^".library".Length]
			: libraryName;
		root = root.Replace(".", string.Empty, StringComparison.Ordinal);
		return $"_{(KnownNames.TryGetValue(root, out var name) ? name : ToPascalCase(root))}LibraryBase";
	}

	private static string ToPascalCase(string value)
	{
		var result = new System.Text.StringBuilder();
		var capitalize = true;
		foreach (var character in value)
		{
			if (!char.IsAsciiLetterOrDigit(character))
			{
				capitalize = true;
				continue;
			}
			result.Append(capitalize ? char.ToUpperInvariant(character) : character);
			capitalize = false;
		}
		return result.Length == 0 ? "Library" : result.ToString();
	}
}

public sealed class AmigaExternalCallResolver : IM68kExternalCallResolver
{
	private static readonly string LibraryAttributeName = typeof(AmigaLibraryAttribute).FullName!;
	private static readonly string LvoAttributeName = typeof(AmigaLvoAttribute).FullName!;
	private static readonly string ImportAttributeName = typeof(M68kImportAttribute).FullName!;
	private static readonly string RegisterAttributeName = typeof(M68kRegisterAttribute).FullName!;
	private static readonly string IndirectCallAttributeName =
		typeof(AmigaIndirectCallAttribute).FullName!;
	private readonly AmigaCompilationOptions _options;

	public AmigaExternalCallResolver(AmigaCompilationOptions? options = null)
	{
		_options = options ?? new AmigaCompilationOptions();
	}

	public bool TryResolve(
		M68kExternalMethod method,
		out M68kExternalCallConvention convention)
	{
		var indirectCall = Find(method.MethodAttributes, IndirectCallAttributeName);
		if (indirectCall is not null)
		{
			if (!method.IsStatic)
				throw Unsupported(method, "Amiga indirect calls must be static.");
			if (indirectCall.FixedArguments.Count != 1 ||
				indirectCall.FixedArguments[0] is not int registerValue ||
				registerValue < (int)M68kRegister.A0 ||
				registerValue > (int)M68kRegister.A6)
			{
				throw Invalid(method,
					"[AmigaIndirectCall] requires one A0-A6 target register.");
			}
			var targetRegister = (M68kRegister)registerValue;
			convention = new M68kExternalCallConvention(
				$"amiga-indirect:{targetRegister}",
				M68kExternalBaseSource.Argument,
				targetRegister,
				0,
				ParameterRegisters: DecodeParameterRegisters(method),
				ReturnRegister: DecodeReturnRegister(method));
			return true;
		}
		if (Find(method.MethodAttributes, ImportAttributeName) is not null)
		{
			convention = null!;
			return false;
		}

		var methodLibrary = Find(method.MethodAttributes, LibraryAttributeName);
		var typeLibrary = Find(method.DeclaringTypeAttributes, LibraryAttributeName);
		var lvo = Find(method.MethodAttributes, LvoAttributeName);
		if (methodLibrary is null && typeLibrary is null && lvo is null)
		{
			convention = null!;
			return false;
		}
		if (lvo is null)
		{
			throw Invalid(method, "[AmigaLibrary] requires [AmigaLvo] on an external method.");
		}
		var libraryAttribute = methodLibrary ?? typeLibrary ??
			throw Invalid(method, "[AmigaLvo] requires [AmigaLibrary] on the method or declaring type.");
		if (!method.IsStatic)
		{
			throw Unsupported(method, "Amiga library vector declarations must be static.");
		}

		var (name, policy) = DecodeLibrary(method, libraryAttribute);
		var offset = DecodeLvo(method, lvo);
		var parameterRegisters = DecodeParameterRegisters(method);
		var returnRegister = DecodeReturnRegister(method);
		convention = CreateConvention(
			method,
			name,
			ResolvePolicy(name, policy),
			offset,
			parameterRegisters,
			returnRegister);
		return true;
	}

	private AmigaLibraryBasePolicy ResolvePolicy(
		string name,
		AmigaLibraryBasePolicy? explicitPolicy)
	{
		if (explicitPolicy is not null)
		{
			return explicitPolicy.Value;
		}
		return _options.LibraryBasePolicies.TryGetValue(name, out var policy)
			? policy
			: _options.DefaultLibraryBasePolicy;
	}

	private M68kExternalCallConvention CreateConvention(
		M68kExternalMethod method,
		string name,
		AmigaLibraryBasePolicy policy,
		short offset,
		IReadOnlyList<M68kRegister>? parameterRegisters = null,
		M68kRegister returnRegister = M68kRegister.D0) =>
		policy switch
		{
			AmigaLibraryBasePolicy.ExecBase => new M68kExternalCallConvention(
				name,
				M68kExternalBaseSource.WritableSlot,
				M68kRegister.A6,
				offset,
				SourceAddress: 4,
				SlotSymbol: "_ExecBase",
				ParameterRegisters: parameterRegisters,
				ReturnRegister: returnRegister),
			AmigaLibraryBasePolicy.Manual => new M68kExternalCallConvention(
				name,
				M68kExternalBaseSource.WritableSlot,
				M68kRegister.A6,
				offset,
				InitialValue: _options.LibraryBases.TryGetValue(name, out var cached) ? cached : 0,
				SlotSymbol: AmigaLibraryBaseSymbols.For(name),
				ParameterRegisters: parameterRegisters,
				ReturnRegister: returnRegister),
			AmigaLibraryBasePolicy.AutoOpen => throw Unsupported(
				method,
				"Automatic Amiga library opening is a linker/startup mode and is not implemented yet."),
			AmigaLibraryBasePolicy.Provided => new M68kExternalCallConvention(
				name,
				M68kExternalBaseSource.Immediate,
				M68kRegister.A6,
				offset,
				InitialValue: GetProvidedBase(method, name),
				ParameterRegisters: parameterRegisters,
				ReturnRegister: returnRegister),
			AmigaLibraryBasePolicy.CallerProvided =>
				CreateCallerProvidedConvention(
					name,
					offset,
					parameterRegisters,
					returnRegister),
			_ => throw Invalid(method, $"Unknown Amiga library base policy {policy}.")
		};

	private static M68kExternalCallConvention CreateCallerProvidedConvention(
		string name,
		short offset,
		IReadOnlyList<M68kRegister>? parameterRegisters,
		M68kRegister returnRegister)
		=> new(
			name,
			M68kExternalBaseSource.Argument,
			M68kRegister.A6,
			offset,
			ParameterRegisters: parameterRegisters,
			ReturnRegister: returnRegister);

	private uint GetProvidedBase(M68kExternalMethod method, string name) =>
		_options.LibraryBases.TryGetValue(name, out var address)
			? address
			: throw new M68kCompilationException(
				M68kDiagnosticIds.UnresolvedImport,
				$"No base address was supplied for Amiga library '{name}'.",
				method.DisplayName);

	private static (string Name, AmigaLibraryBasePolicy? Policy) DecodeLibrary(
		M68kExternalMethod method,
		M68kMetadataAttribute attribute)
	{
		if (attribute.FixedArguments.Count is < 1 or > 2 ||
			attribute.FixedArguments[0] is not string name ||
			string.IsNullOrWhiteSpace(name))
		{
			throw Invalid(method, "[AmigaLibrary] must contain a non-empty library name.");
		}
		if (attribute.FixedArguments.Count == 1)
		{
			return (name, null);
		}

		var value = attribute.FixedArguments[1] as int?;
		if (value is null || !Enum.IsDefined(typeof(AmigaLibraryBasePolicy), value.Value))
		{
			throw Invalid(method, "[AmigaLibrary] contains an invalid base policy.");
		}
		return (name, (AmigaLibraryBasePolicy)value.Value);
	}

	private static short DecodeLvo(
		M68kExternalMethod method,
		M68kMetadataAttribute attribute)
	{
		if (attribute.FixedArguments.Count != 1 || attribute.FixedArguments[0] is not int offset)
		{
			throw Invalid(method, "[AmigaLvo] must contain one signed byte offset.");
		}
		if (offset >= 0 || offset < short.MinValue || (offset & 1) != 0)
		{
			throw Invalid(method, $"[AmigaLvo] offset {offset} must be a negative, word-aligned signed 16-bit displacement.");
		}
		return (short)offset;
	}

	private static M68kMetadataAttribute? Find(
		IReadOnlyList<M68kMetadataAttribute> attributes,
		string typeName) =>
		attributes.FirstOrDefault(attribute =>
			string.Equals(attribute.TypeName, typeName, StringComparison.Ordinal));

	private static IReadOnlyList<M68kRegister>? DecodeParameterRegisters(
		M68kExternalMethod method)
	{
		if (method.ParameterAttributes.Count == 0)
		{
			return null;
		}
		var result = new M68kRegister[method.ParameterAttributes.Count];
		for (var index = 0; index < result.Length; index++)
		{
			result[index] = DecodeRegister(
				method,
				method.ParameterAttributes[index],
				$"parameter {index}") ??
				throw Unsupported(method, $"Amiga call parameter {index} requires [M68kRegister].");
		}
		return result;
	}

	private static M68kRegister DecodeReturnRegister(M68kExternalMethod method) =>
		DecodeRegister(method, method.ReturnAttributes, "return value") ?? M68kRegister.D0;

	private static M68kRegister? DecodeRegister(
		M68kExternalMethod method,
		IReadOnlyList<M68kMetadataAttribute> attributes,
		string role)
	{
		var attribute = Find(attributes, RegisterAttributeName);
		if (attribute is null)
		{
			return null;
		}
		if (attribute.FixedArguments.Count != 1 ||
			attribute.FixedArguments[0] is not int value ||
			!Enum.IsDefined(typeof(M68kRegister), value))
		{
			throw Invalid(method, $"Invalid [M68kRegister] on {role}.");
		}
		return (M68kRegister)value;
	}

	private static M68kCompilationException Invalid(M68kExternalMethod method, string message) =>
		new(M68kDiagnosticIds.InvalidMetadata, message, method.DisplayName);

	private static M68kCompilationException Unsupported(M68kExternalMethod method, string message) =>
		new(M68kDiagnosticIds.UnsupportedSignature, message, method.DisplayName);
}

public static class AmigaM68kCompiler
{
	private static readonly M68kTargetContract TargetContract = new(
		"amiga-m68k",
		"CopperSharp.Targets.Amiga",
		GetPackageVersion(typeof(AmigaM68kCompiler).Assembly));
	private static readonly M68kManagedLifecycleHook ConsoleLifecycle = new(
		"amiga-console",
		"CopperSharp.Runtime.AmigaPal",
		"CopperSharp.Runtime.AmigaPal.ConsolePal::Initialize",
		"CopperSharp.Runtime.AmigaPal.ConsolePal::Shutdown");
	private static readonly M68kManagedLifecycleHook ConsoleInputLifecycle = new(
		"amiga-console-input",
		"CopperSharp.Runtime.AmigaPal",
		"CopperSharp.Runtime.AmigaPal.ConsolePal::InitializeInput",
		"CopperSharp.Runtime.AmigaPal.ConsolePal::ShutdownInput");
	private static readonly M68kManagedLifecycleHook FileSystemLifecycle = new(
		"amiga-filesystem",
		"CopperSharp.Runtime.AmigaPal",
		"CopperSharp.Runtime.AmigaPal.FileSystemPal::Initialize",
		"CopperSharp.Runtime.AmigaPal.FileSystemPal::Shutdown");
	private static readonly M68kManagedLifecycleHook ClockLifecycle = new(
		"amiga-clock",
		"CopperSharp.Runtime.AmigaPal",
		"CopperSharp.Runtime.AmigaPal.ClockPal::Initialize",
		"CopperSharp.Runtime.AmigaPal.ClockPal::Shutdown");

	/// <summary>
	/// Analyzes reachable framework members using the Amiga platform resolver
	/// without generating target code.
	/// </summary>
	public static M68kFrameworkAnalysisResult AnalyzeFramework(
		M68kCompilationRequest request,
		AmigaCompilationOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(request);
		var resolvedOptions = options ?? new AmigaCompilationOptions();
		request = IncludeAmigaManagedBodies(request);
		return M68kCompiler.AnalyzeFramework(request with
		{
			ExternalCallResolvers =
			[
				new AmigaExternalCallResolver(resolvedOptions)
			]
		});
	}

	public static M68kCompilationResult Compile(
		M68kCompilationRequest request,
		AmigaCompilationOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(request);
		var resolvedOptions = options ?? new AmigaCompilationOptions();
		request = IncludeAmigaManagedBodies(request);
		AmigaStaticAnalyzer.Analyze(request, resolvedOptions);
		var imports = request.Imports;
		if (request.ExceptionMode == M68kExceptionMode.Full &&
			request.OutputFormat != M68kOutputFormat.KickstartRom)
		{
			var configured = new Dictionary<string, uint>(request.Imports, StringComparer.Ordinal)
			{
				[M68kRuntimeImports.AmigaUnhandledExceptionRequester] = 1
			};
			imports = new ReadOnlyDictionary<string, uint>(configured);
		}
		if (request.MemoryManagement == M68kMemoryManagement.ManagedPoolMarkSweepGc &&
			request.OutputFormat != M68kOutputFormat.KickstartRom)
		{
			var configured = new Dictionary<string, uint>(imports, StringComparer.Ordinal)
			{
				[M68kRuntimeImports.AmigaManagedPoolArena] = 1
			};
			imports = new ReadOnlyDictionary<string, uint>(configured);
		}
		return M68kCompiler.Compile(request with
		{
			Imports = imports,
			ExternalCallResolvers =
			[
				new AmigaExternalCallResolver(resolvedOptions)
			]
			});
	}

	private static M68kCompilationRequest IncludeAmigaManagedBodies(
		M68kCompilationRequest request)
	{
		var paths = request.ManagedAssemblyPaths.ToList();
		var lifecycleHooks = request.ManagedLifecycleHooks.ToList();
		if (!lifecycleHooks.Contains(ConsoleLifecycle))
		{
			lifecycleHooks.Add(ConsoleLifecycle);
		}
		if (!lifecycleHooks.Contains(ConsoleInputLifecycle))
		{
			lifecycleHooks.Add(ConsoleInputLifecycle);
		}
		if (!lifecycleHooks.Contains(FileSystemLifecycle))
		{
			lifecycleHooks.Add(FileSystemLifecycle);
		}
		if (!lifecycleHooks.Contains(ClockLifecycle))
		{
			lifecycleHooks.Add(ClockLifecycle);
		}
		var inputDirectory = Path.GetDirectoryName(
			Path.GetFullPath(request.AssemblyPath));
		if (inputDirectory is not null)
		{
			AddManagedAssembly(
				paths,
				Path.Combine(inputDirectory, "CopperSharp.Sdk.Amiga.dll"));
			AddManagedAssembly(
				paths,
				Path.Combine(inputDirectory, "CopperSharp.Runtime.AmigaPal.dll"));
		}

		var targetDirectory = Path.GetDirectoryName(
			typeof(AmigaM68kCompiler).Assembly.Location);
		if (targetDirectory is not null)
		{
			// Portable applications such as ConsoleIO do not reference the SDK
			// directly. The runtime bodies still expose SDK value types (APTR,
			// BPTR, and friends), so make the target's SDK available when the
			// input directory does not carry a copy.
			AddManagedAssembly(
				paths,
				Path.Combine(targetDirectory, "CopperSharp.Sdk.Amiga.dll"));
			AddManagedAssembly(
				paths,
				Path.Combine(targetDirectory, "CopperSharp.Runtime.AmigaPal.dll"));
		}
		if (paths.Count == request.ManagedAssemblyPaths.Count &&
			lifecycleHooks.Count == request.ManagedLifecycleHooks.Count &&
			request.TargetContract == TargetContract)
		{
			return request;
		}

		return request with
		{
			ManagedAssemblyPaths = paths,
			ManagedLifecycleHooks = lifecycleHooks,
			TargetContract = TargetContract
		};
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

	private static void AddManagedAssembly(ICollection<string> paths, string path)
	{
		if (!File.Exists(path) || paths.Any(existing =>
				string.Equals(
					Path.GetFileName(existing),
					Path.GetFileName(path),
					StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}
		paths.Add(path);
	}
}
