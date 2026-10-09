/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Metadata;

internal sealed partial class CompilationModule : IDisposable
{
	public string AssemblyName => _assemblyName;

	private const string BoopsiDispatcherAttributeName = "Amiga.BOOPSI+DispatcherAttribute";
	private const string MuiListDisplayCallbackAttributeName = "Amiga.MUI.List+DisplayCallbackAttribute";
	private const string UninitializedStorageAttributeName = "CopperSharp.Compiler.M68kUninitializedStorageAttribute";
	private const string StackAlignmentAttributeName = "CopperSharp.Compiler.M68kStackAlignmentAttribute";

	private readonly FileStream _stream;
	private readonly PEReader _peReader;
	private readonly CilSignatureTypeProvider _signatureProvider;
	private readonly Dictionary<(MethodDefinitionHandle Handle, string Construction), CilMethod> _methodCache = new();
	private readonly Dictionary<(int Token, CilMethodIdentity Caller, int IlOffset),
		MethodReference> _methodReferenceCache = new();
	private readonly Dictionary<(TypeDefinitionHandle Handle, string Construction), CilMethod?> _typeInitializerCache = new();
	private readonly Dictionary<FieldDefinitionHandle, CilField> _fieldCache = new();
	private readonly Dictionary<TypeReferenceHandle, CilType> _referencedTypeCache = new();
	private readonly Dictionary<TypeDefinitionHandle, CilTypeLayout> _layoutCache = new();
	private readonly Dictionary<(TypeDefinitionHandle Handle, string Construction), CilTypeLayout>
		_constructedLayoutCache = new();
	private readonly Dictionary<(TypeDefinitionHandle Handle, string Construction), CilVirtualTable>
		_virtualTableCache = new();
	private readonly Dictionary<(TypeDefinitionHandle Handle, string Construction), CilInterfaceDefinition>
		_interfaceCache = new();
	private readonly Dictionary<CilInterfaceImplementationIdentity, CilInterfaceImplementation?> _interfaceImplementationCache = new();
	private readonly Dictionary<string, bool> _transparentScalarTypeCache = new(StringComparer.Ordinal);
	private readonly Dictionary<string, bool> _uninitializedStorageTypeCache = new(StringComparer.Ordinal);
	private readonly Dictionary<string, bool> _longAlignedStackTypeCache = new(StringComparer.Ordinal);
	private readonly Dictionary<(string DisplayName, string AttributeName), bool>
		_reflectionAttributeCache = new();
	private Dictionary<string, (TypeDefinitionHandle Handle, bool IsInterface)>?
		_runtimeTypeDefinitionIndex;
	private readonly IReadOnlyList<IM68kExternalCallResolver> _externalCallResolvers;
	private readonly string _assemblyPath;
	private readonly string _assemblyDirectory;
	private readonly CompilationModule _root;
	private readonly Dictionary<string, CompilationModule> _modules;
	private readonly HashSet<string> _reachableAssemblyNames;
	private readonly Dictionary<CilMethodIdentity, FrameworkVirtualFallback>
		_frameworkVirtualFallbacks;
	private readonly Dictionary<CilTypeIdentity, CilTypeLayout> _reachableDispatchLayouts = new();
	private readonly IReadOnlyDictionary<string, string> _managedAssemblyPaths;
	private readonly FrameworkImplementationPackCatalog? _frameworkImplementationPack;
	private string _assemblyName = string.Empty;

	public CompilationModule(
		string assemblyPath,
		IReadOnlyList<IM68kExternalCallResolver>? externalCallResolvers = null,
		IReadOnlyList<string>? managedAssemblyPaths = null,
		FrameworkImplementationPackCatalog? frameworkImplementationPack = null,
		M68kFloatingPointMode floatingPoint = M68kFloatingPointMode.Disabled)
		: this(assemblyPath, externalCallResolvers, root: null)
	{
		_managedAssemblyPaths = CreateManagedAssemblyPathMap(
			managedAssemblyPaths ?? Array.Empty<string>());
		_frameworkImplementationPack = frameworkImplementationPack;
		_floatingPointMode = floatingPoint;
		if (_frameworkImplementationPack is not null)
		{
			// Layout can be needed before the first pinned method body is bound.
			// Load the verified CoreLib now so identity resolution never records a
			// synthetic nil-handle layout for an implementation-owned type.
			_ = GetOrLoadImplementationModule(
				"System.Private.CoreLib",
				markReachable: false);
		}
	}

	internal FrameworkImplementationPackCatalog? FrameworkImplementationPack =>
		_root._frameworkImplementationPack;

	internal bool IsPinnedCultureStorageCaller(CilMethod caller) =>
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && caller.ModuleName == "CopperSharp.Runtime.Managed" &&
		FrameworkImplementationProfile.IsCultureStorageCaller(GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, []));

	private bool CanDescribeCoreLibDefinitions => _assemblyName == "System.Private.CoreLib" &&
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true);

	internal bool IsPinnedNumericSpecialization(CilMethod method) =>
		IsPinnedFloatingFormatter(method) ||
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && method.ModuleName == "System.Private.CoreLib" &&
		StringBuilderNumericSurface.IsDefinition(GetModule(method.ModuleName).DescribeFrameworkMethodDefinition(method.Handle, [])) &&
		(method.MethodTypeArguments is [{ Kind: CilTypeKind.Character, Size: 2, DisplayName: "char" }] ||
		 method.ConstructedDeclaringType is { } type && IsCharacterValueListBuilder(type));

	internal bool IsPinnedFloatingHardwareMethod(CilMethod method) =>
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && method.ModuleName == "System.Private.CoreLib" &&
		GetModule(method.ModuleName).GetTypeName(method.DeclaringType) == "System.Numerics.BitOperations" &&
		StringBuilderFloatingSurface.ContainsHelper(GetModule(method.ModuleName).DescribeFrameworkMethodDefinition(method.Handle, []));

	internal bool IsPinnedFloatingFormatter(CilMethod method)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || method.ModuleName != "System.Private.CoreLib") return false;
		var arguments = new List<FrameworkTypeId>();
		foreach (var argument in method.MethodTypeArguments)
		{
			var name = argument switch {
				{ IsEnum: false, Kind: CilTypeKind.FloatingPoint, Size: 4, DisplayName: "float" } => "System.Single",
				{ IsEnum: false, Kind: CilTypeKind.FloatingPoint, Size: 8, DisplayName: "double" } => "System.Double",
				{ Kind: CilTypeKind.Character, Size: 2, DisplayName: "char" } => "System.Char", _ => null
			};
			if (name is null) return false;
			arguments.Add(FrameworkTypeId.Primitive(name));
		}
		return StringBuilderFloatingSurface.ContainsFormatter(GetModule(method.ModuleName).DescribeFrameworkMethodDefinition(method.Handle, arguments));
	}

	internal bool IsPinnedNumberFormatTypeToken(CilMethod caller, CilType type) =>
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && caller.ModuleName == "System.Private.CoreLib" &&
		caller.DisplayName != "System.Globalization.NumberFormatInfo::GetInstance" &&
		FrameworkImplementationProfile.IsNumberFormatProviderCaller(GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, [])) &&
		(type.DisplayName == "System.Globalization.NumberFormatInfo" ||
		 caller.DisplayName == "System.Globalization.NumberFormatInfo::<GetInstance>g__GetProviderNonNull|58_0" && type.DisplayName == "System.Globalization.CultureInfo" ||
		 caller.DisplayName == "System.Globalization.CultureInfo::GetFormat" && type.DisplayName == "System.Globalization.DateTimeFormatInfo");

	private bool IsExperimentalCoreLibModule =>
		FrameworkImplementationPack?.EnableUnlistedManagedBodies == true &&
		string.Equals(_assemblyName, "System.Private.CoreLib", StringComparison.Ordinal);

	internal bool IsPinnedStringBuilderFormattingTypeToken(CilMethod caller, CilType type) =>
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true &&
		((type.DisplayName is "System.ICustomFormatter" or "System.Globalization.NumberFormatInfo" && IsPinnedApplicationFormatProviderCaller(caller)) ||
		 caller.ModuleName == "System.Private.CoreLib" && ((caller.DisplayName == "System.Text.StringBuilder::AppendFormat" && type.DisplayName == "System.ICustomFormatter" &&
		  StringBuilderFrameworkSurface.TryGetEffects(GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, []), out _)) ||
		 (FrameworkImplementationProfile.IsHandlerReflectionCaller(GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, [])) &&
		  (type.DisplayName == "System.ICustomFormatter" || type.DisplayName == "System.Globalization.CultureInfo" && caller.Name == "HasCustomFormatter"))));

	private static IReadOnlyDictionary<string, string> CreateManagedAssemblyPathMap(
		IReadOnlyList<string> paths)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var candidate in paths)
		{
			var path = Path.GetFullPath(candidate);
			var name = Path.GetFileNameWithoutExtension(path);
			if (!result.TryGetValue(name, out var previousPath))
			{
				result.Add(name, path);
				continue;
			}
			if (FilesHaveEqualContent(previousPath, path))
			{
				continue;
			}

			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidInput,
				$"Managed assembly identity '{name}' is supplied by different files '{previousPath}' and '{path}'.");
		}
		return result;
	}

	private static bool FilesHaveEqualContent(string leftPath, string rightPath)
	{
		if (string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		var leftInfo = new FileInfo(leftPath);
		var rightInfo = new FileInfo(rightPath);
		if (leftInfo.Length != rightInfo.Length)
		{
			return false;
		}
		using var left = File.OpenRead(leftPath);
		using var right = File.OpenRead(rightPath);
		return SHA256.HashData(left).AsSpan().SequenceEqual(SHA256.HashData(right));
	}

	private CompilationModule(
		string assemblyPath,
		IReadOnlyList<IM68kExternalCallResolver>? externalCallResolvers,
		CompilationModule? root)
	{
		_externalCallResolvers = externalCallResolvers ?? Array.Empty<IM68kExternalCallResolver>();
		_assemblyPath = Path.GetFullPath(assemblyPath);
		_assemblyDirectory = Path.GetDirectoryName(_assemblyPath)!;
		_root = root ?? this;
		_modules = root?._modules ?? new Dictionary<string, CompilationModule>(StringComparer.Ordinal);
		_reachableAssemblyNames = root?._reachableAssemblyNames ??
			new HashSet<string>(StringComparer.Ordinal);
		_frameworkVirtualFallbacks = root?._frameworkVirtualFallbacks ??
			new Dictionary<CilMethodIdentity, FrameworkVirtualFallback>();
		_managedAssemblyPaths = root?._managedAssemblyPaths ??
			new Dictionary<string, string>(StringComparer.Ordinal);
		_frameworkImplementationPack = root?._frameworkImplementationPack;
		_signatureProvider = new CilSignatureTypeProvider(ResolveReferencedEnumType,
			useCoreLibPrimitiveDefinitions: () => CanDescribeCoreLibDefinitions,
			nullableStorageSizeResolver: GetNullableStorageSize);
		try
		{
			_stream = File.OpenRead(_assemblyPath);
			_peReader = new PEReader(_stream, PEStreamOptions.PrefetchEntireImage);
			if (!_peReader.HasMetadata)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidInput,
					$"'{assemblyPath}' is not a managed PE image.");
			}

			Reader = _peReader.GetMetadataReader();
			_assemblyName = Reader.GetString(Reader.GetAssemblyDefinition().Name);
			_modules.TryAdd(_assemblyName, this);
			if (root is null)
			{
				_reachableAssemblyNames.Add(_assemblyName);
			}
		}
		catch (M68kCompilationException)
		{
			throw;
		}
		catch (Exception exception) when (
			exception is IOException or
			UnauthorizedAccessException or
			BadImageFormatException)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidInput,
				$"Could not open managed assembly '{assemblyPath}': {exception.Message}",
				innerException: exception);
		}
	}

	public MetadataReader Reader { get; }

	internal IReadOnlyList<M68kReachableAssemblyIdentity>
		GetReachableAssemblyIdentities() =>
		_root._reachableAssemblyNames
			.Select(name => _root._modules[name].GetAssemblyIdentity())
			.OrderBy(static identity => identity.Name, StringComparer.Ordinal)
			.ThenBy(static identity => identity.Version, StringComparer.Ordinal)
			.ThenBy(static identity => identity.Mvid)
			.ToArray();

	private M68kReachableAssemblyIdentity GetAssemblyIdentity()
	{
		var definition = Reader.GetAssemblyDefinition();
		var module = Reader.GetModuleDefinition();
		var publicKey = Reader.GetBlobBytes(definition.PublicKey);
		var publicKeyToken = string.Empty;
		if (publicKey.Length != 0)
		{
			var hash = SHA1.HashData(publicKey);
			Span<byte> token = stackalloc byte[8];
			for (var index = 0; index < token.Length; index++)
			{
				token[index] = hash[hash.Length - 1 - index];
			}
			publicKeyToken = Convert.ToHexString(token).ToLowerInvariant();
		}

		using var stream = File.OpenRead(_assemblyPath);
		var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
		return new M68kReachableAssemblyIdentity(
			_assemblyName,
			definition.Version.ToString(),
			publicKeyToken,
			Reader.GetGuid(module.Mvid),
			sha256);
	}

	public CilMethod ResolveEntryPoint(string? selector)
	{
		if (!string.IsNullOrWhiteSpace(selector))
		{
			return ResolveSelector(selector);
		}

		var candidates = new List<CilMethod>();
		foreach (var handle in Reader.MethodDefinitions)
		{
			var definition = Reader.GetMethodDefinition(handle);
			if (HasAttribute(definition.GetCustomAttributes(), typeof(M68kEntryPointAttribute).FullName!))
			{
				candidates.Add(GetMethod(handle));
			}
		}

		if (candidates.Count != 1)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.EntryPointNotFound,
				$"Expected exactly one [M68kEntryPoint] method but found {candidates.Count}.");
		}

		return candidates[0];
	}

	public CilMethod ResolveManagedMethod(string assemblyName, string selector)
	{
		var module = GetOrLoadModule(assemblyName) ??
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidInput,
				$"Configured managed assembly '{assemblyName}' could not be loaded.");
		return module.ResolveEntryPoint(selector);
	}

	public CilField ResolveManagedField(
		string assemblyName,
		string typeName,
		string fieldName)
	{
		var module = GetOrLoadModule(assemblyName) ??
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidInput,
				$"Configured managed assembly '{assemblyName}' could not be loaded.");
		foreach (var handle in module.Reader.TypeDefinitions)
		{
			var type = module.Reader.GetTypeDefinition(handle);
			if (!string.Equals(module.GetTypeName(type), typeName, StringComparison.Ordinal))
			{
				continue;
			}

			foreach (var fieldHandle in type.GetFields())
			{
				var field = module.GetField(fieldHandle);
				if (field.DisplayName.EndsWith($"::{fieldName}", StringComparison.Ordinal))
				{
					return field;
				}
			}
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.InvalidMetadata,
			$"Could not resolve managed runtime field '{typeName}::{fieldName}'.");
	}

	public IReadOnlyList<CilExport> GetExports()
	{
		var exports = new List<CilExport>();
		foreach (var handle in Reader.MethodDefinitions)
		{
			var definition = Reader.GetMethodDefinition(handle);
			var exportName = TryGetExportName(definition.GetCustomAttributes());
			var boopsiDispatcherName = TryGetBoopsiDispatcherName(definition.GetCustomAttributes());
			var muiListDisplayCallbackName = TryGetMuiListDisplayCallbackName(definition.GetCustomAttributes());
			if (exportName is null && boopsiDispatcherName is null && muiListDisplayCallbackName is null)
			{
				continue;
			}

			if (exportName is not null && (boopsiDispatcherName is not null || muiListDisplayCallbackName is not null) ||
				boopsiDispatcherName is not null && muiListDisplayCallbackName is not null)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"A method cannot combine [M68kExport], [BOOPSI.Dispatcher], and [MUI.List.DisplayCallback].");
			}

			var method = GetMethod(handle);
			if (method.Signature.Header.IsInstance)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					"Exported methods must be static.",
					method.DisplayName);
			}

			M68kRegister[] parameterRegisters;
			M68kRegister returnRegister;
			if (boopsiDispatcherName is not null)
			{
				if (method.Signature.ParameterTypes.Length != 3)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedSignature,
						"[BOOPSI.Dispatcher] methods must have exactly three parameters (cl, obj, message).",
						method.DisplayName);
				}

				parameterRegisters = new[]
				{
					M68kRegister.A0,
					M68kRegister.A2,
					M68kRegister.A1
				};
				returnRegister = M68kRegister.D0;
			}
			else if (muiListDisplayCallbackName is not null)
			{
				if (method.Signature.ParameterTypes.Length != 2)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedSignature,
						"[MUI.List.DisplayCallback] methods must have exactly two parameters (entry, columns).",
						method.DisplayName);
				}

				parameterRegisters = new[]
				{
					M68kRegister.A1,
					M68kRegister.A2
				};
				returnRegister = M68kRegister.D0;
			}
			else
			{
				parameterRegisters = new M68kRegister[method.Signature.ParameterTypes.Length];
				var hasRegister = new bool[parameterRegisters.Length];
				M68kRegister? explicitReturnRegister = null;
				foreach (var parameterHandle in definition.GetParameters())
				{
					var parameter = Reader.GetParameter(parameterHandle);
					var register = TryGetRegister(parameter.GetCustomAttributes());
					if (parameter.SequenceNumber == 0)
					{
						explicitReturnRegister = register;
						continue;
					}

					var index = parameter.SequenceNumber - 1;
					if ((uint)index < (uint)parameterRegisters.Length && register is { } value)
					{
						parameterRegisters[index] = value;
						hasRegister[index] = true;
					}
				}

				if (hasRegister.Any(static present => !present))
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedSignature,
						"Every exported parameter must carry [M68kRegister].",
						method.DisplayName);
				}

				returnRegister = explicitReturnRegister ?? M68kRegister.D0;
			}

			exports.Add(new CilExport(
				method,
				(exportName ?? boopsiDispatcherName ?? muiListDisplayCallbackName)!.Length == 0
					? method.DisplayName
					: (exportName ?? boopsiDispatcherName ?? muiListDisplayCallbackName)!,
				parameterRegisters,
				returnRegister));
		}

		return exports;
	}

	public CilMethod GetMethod(MethodDefinitionHandle handle) =>
		GetConstructedMethod(
			handle,
			constructedDeclaringType: null,
			ImmutableArray<CilType>.Empty);

	private CilMethod GetConstructedMethod(
		MethodDefinitionHandle handle,
		CilType? constructedDeclaringType,
		ImmutableArray<CilType> methodTypeArguments,
		bool deferBody = false)
	{
		var construction = CilMethod.FormatConstruction(
			constructedDeclaringType,
			methodTypeArguments);
		var cacheKey = (handle, construction);
		if (_methodCache.TryGetValue(cacheKey, out var cached))
		{
			return cached;
		}

		var definition = Reader.GetMethodDefinition(handle);
		var declaringType = Reader.GetTypeDefinition(definition.GetDeclaringType());
		var typeName = GetTypeName(declaringType);
		var methodName = Reader.GetString(definition.Name);
		var displayName = $"{constructedDeclaringType?.DisplayName ?? typeName}::{methodName}" +
			(methodTypeArguments.Length == 0
				? string.Empty
				: $"<{string.Join(",", methodTypeArguments.Select(static type => type.DisplayName))}>");
		var genericContext = new CilGenericContext(
			constructedDeclaringType?.GenericArguments ?? ImmutableArray<CilType>.Empty,
			methodTypeArguments);
			var signature = definition.DecodeSignature(_signatureProvider, genericContext);
			var parameterFlags = new ParameterAttributes[signature.ParameterTypes.Length];
			foreach (var parameterHandle in definition.GetParameters())
			{
				var parameter = Reader.GetParameter(parameterHandle);
				if (parameter.SequenceNumber > 0 &&
					parameter.SequenceNumber <= parameterFlags.Length)
				{
					parameterFlags[parameter.SequenceNumber - 1] = parameter.Attributes;
				}
			}
		var importName = TryGetImportName(definition.GetCustomAttributes());
			var externalConvention = definition.RelativeVirtualAddress == 0
				? ResolveExternalCall(new M68kExternalMethod(
				_assemblyName,
			displayName,
			typeName,
			methodName,
			!signature.Header.IsInstance,
			DecodeAttributes(declaringType.GetCustomAttributes()),
			DecodeAttributes(definition.GetCustomAttributes()),
				Array.Empty<IReadOnlyList<M68kMetadataAttribute>>(),
				Array.Empty<M68kMetadataAttribute>()))
				: null;
		if (importName is not null && externalConvention is not null)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"A method cannot be both [M68kImport] and a platform call.",
				displayName);
		}

		var externalCall = externalConvention is null
			? null
			: GetExternalCall(
				definition,
				signature,
				displayName,
				externalConvention);
		var importAbi = importName is null
			? null
			: GetImportAbi(definition, signature, displayName);
		MethodBodyBlock? body = null;
		ImmutableArray<CilType> locals = ImmutableArray<CilType>.Empty;
		IReadOnlyList<CilInstruction> instructions = Array.Empty<CilInstruction>();
		IReadOnlyList<CilExceptionRegion> exceptionRegions = Array.Empty<CilExceptionRegion>();

		if (!deferBody && importName is null && externalCall is null)
		{
			if (definition.RelativeVirtualAddress == 0)
			{
				if ((definition.Attributes & MethodAttributes.Abstract) == 0)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidMetadata,
						"Reachable method has no CIL body and is not abstract, marked [M68kImport], or resolved by the target platform.",
						displayName);
				}
			}
			else
			{
				body = _peReader.GetMethodBody(definition.RelativeVirtualAddress);
				if (!body.LocalSignature.IsNil)
				{
					locals = Reader
						.GetStandaloneSignature(body.LocalSignature)
						.DecodeLocalSignature(_signatureProvider, genericContext);
				}

				instructions = CilInstructionDecoder.Decode(body.GetILBytes(), displayName);
				exceptionRegions = DecodeExceptionRegions(body, instructions, displayName);
			}
		}

		var method = new CilMethod(
			handle,
			definition.GetDeclaringType(),
			displayName,
			Reader.GetString(definition.Name),
			signature,
			locals,
			instructions,
			exceptionRegions,
			body?.LocalVariablesInitialized ?? false,
			importName,
			importAbi,
			externalCall,
				_assemblyName,
				definition.Attributes,
				declaringType.Attributes,
				parameterFlags.ToImmutableArray(),
				constructedDeclaringType,
				methodTypeArguments,
				definition.ImplAttributes);
		if (deferBody) return method with { HasDeferredBody = true };
		_methodCache.Add(cacheKey, method);
		method = CilHardwareSupportSpecializer.Specialize(method, this);
		method = CilTypePredicateSpecializer.Specialize(method, this);
		_methodCache[cacheKey] = method;
		return method;
	}

	private IReadOnlyList<CilExceptionRegion> DecodeExceptionRegions(
		MethodBodyBlock body,
		IReadOnlyList<CilInstruction> instructions,
		string methodName)
	{
		if (body!.ExceptionRegions.Length == 0)
		{
			return Array.Empty<CilExceptionRegion>();
		}

		var ilSize = body.GetILBytes()!.Length;
		var instructionOffsets = instructions
			.Select(instruction => instruction.Offset)
			.ToHashSet();
		instructionOffsets.Add(ilSize);
		var result = new List<CilExceptionRegion>(body.ExceptionRegions.Length);
		foreach (var region in body.ExceptionRegions)
		{
			if (region.Kind is ExceptionRegionKind.Filter or ExceptionRegionKind.Fault)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedInstruction,
					$"Exception region kind '{region.Kind}' is not supported; only catch and finally are available.",
					methodName,
					region.TryOffset);
			}

			var tryEnd = checked(region.TryOffset + region.TryLength);
			var handlerEnd = checked(region.HandlerOffset + region.HandlerLength);
			if (region.TryOffset < 0 || region.TryLength <= 0 || tryEnd > ilSize ||
				region.HandlerOffset < 0 || region.HandlerLength <= 0 || handlerEnd > ilSize ||
				!instructionOffsets.Contains(region.TryOffset) ||
				!instructionOffsets.Contains(tryEnd) ||
				!instructionOffsets.Contains(region.HandlerOffset) ||
				!instructionOffsets.Contains(handlerEnd))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"Exception region boundaries must align with CIL instruction boundaries.",
					methodName,
					region.TryOffset);
			}

			result.Add(new CilExceptionRegion(
				region.Kind,
				region.TryOffset,
				region.TryLength,
				region.HandlerOffset,
				region.HandlerLength,
				region.CatchType,
				region.FilterOffset));
		}

		return result;
	}

	private CilExternalCall GetExternalCall(
		MethodDefinition definition,
		MethodSignature<CilType> signature,
		string displayName,
		M68kExternalCallConvention binding)
	{
		if (signature.Header.IsInstance)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Platform call declarations must be static.",
				displayName);
		}
		if (string.IsNullOrWhiteSpace(binding.Identity))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"Platform call identity cannot be empty.",
				displayName);
		}
		if (binding.CacheRegister == binding.BaseRegister)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"Platform base and cache registers must be distinct.",
				displayName);
		}
		if (binding.ExceptionPolicy == M68kExternalExceptionPolicy.NonZeroStatus &&
			binding.ExceptionStatusRegister is null)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"A nonzero-status external call must specify an exception status register.",
				displayName);
		}
		if (binding.ExceptionStatusRegister > M68kRegister.D7)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"External exception status must use a data register.",
				displayName);
		}
		if (binding.ClobberedRegisters?.Any(static register =>
			register < M68kRegister.D0 || register > M68kRegister.A6) == true)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"External clobbered registers must use D0-D7 or A0-A6.",
				displayName);
		}

		var abi = binding.ParameterRegisters is null
			? GetRequiredRegisterAbi(
				definition,
				signature,
				displayName,
				"platform call")
			: new CilRegisterAbi(binding.ParameterRegisters, binding.ReturnRegister);
		ValidateExternalCallAbi(signature, displayName, binding, abi);
		return new CilExternalCall(binding, abi);
	}

	private static void ValidateExternalCallAbi(
		MethodSignature<CilType> signature,
		string displayName,
		M68kExternalCallConvention binding,
		CilRegisterAbi abi)
	{
		if (abi.ParameterRegisters.Count != signature.ParameterTypes.Length)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Platform call register count does not match its parameter count.",
				displayName);
		}
		var parameterRegisters = new List<M68kRegister>();
		for (var index = 0; index < abi.ParameterRegisters.Count; index++)
		{
			parameterRegisters.Add(abi.ParameterRegisters[index]);
			if (Is64BitScalar(signature.ParameterTypes[index]))
			{
				parameterRegisters.Add(NextDataRegister(abi.ParameterRegisters[index], displayName));
			}
		}

		var baseArgumentCount = parameterRegisters.Count(
			register => register == binding.BaseRegister);
		if (binding.BaseSource == M68kExternalBaseSource.Argument)
		{
			if (baseArgumentCount != 1)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					$"Argument-sourced platform calls require exactly one {binding.BaseRegister} argument.",
					displayName);
			}
		}
		else if (baseArgumentCount != 0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				$"{binding.BaseRegister} holds the platform call base and cannot be an argument register.",
				displayName);
		}
		if (parameterRegisters.Count != parameterRegisters.Distinct().Count())
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"Platform call parameter registers must be unique.",
				displayName);
		}

		if (!signature.ReturnType.IsVoid)
		{
			if (abi.ReturnRegister == binding.BaseRegister)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					$"{binding.BaseRegister} holds the platform call base and cannot be the return register.",
					displayName);
			}
			if (Is64BitScalar(signature.ReturnType))
			{
				var lowReturnRegister = NextDataRegister(abi.ReturnRegister, displayName);
				if (lowReturnRegister == binding.BaseRegister)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedSignature,
						$"{binding.BaseRegister} holds the platform call base and cannot be the return register.",
						displayName);
				}
			}
		}
	}

	private static bool Is64BitScalar(CilType type) =>
		type.IsSupportedScalar && type.Size == 8;

	private static M68kRegister NextDataRegister(M68kRegister register, string displayName)
	{
		if (register < M68kRegister.D0 || register >= M68kRegister.D7)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"64-bit register-pair values must start in D0-D6.",
				displayName);
		}

		return register + 1;
	}

	private CilRegisterAbi? GetImportAbi(
		MethodDefinition definition,
		MethodSignature<CilType> signature,
		string displayName)
	{
		if (signature.Header.IsInstance)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Imported methods must be static.",
				displayName);
		}

		var parameterRegisters = new M68kRegister[signature.ParameterTypes.Length];
		var hasRegister = new bool[parameterRegisters.Length];
		M68kRegister? returnRegister = null;
		var hasAnyRegister = false;
		foreach (var parameterHandle in definition.GetParameters())
		{
			var parameter = Reader.GetParameter(parameterHandle);
			var register = TryGetRegister(parameter.GetCustomAttributes());
			if (register is null)
			{
				continue;
			}

			hasAnyRegister = true;
			if (parameter.SequenceNumber == 0)
			{
				returnRegister = register;
				continue;
			}

			var index = parameter.SequenceNumber - 1;
			if ((uint)index < (uint)parameterRegisters.Length)
			{
				parameterRegisters[index] = register.Value;
				hasRegister[index] = true;
			}
		}

		if (!hasAnyRegister)
		{
			return null;
		}

		if (hasRegister.Any(static present => !present))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Every register-ABI import parameter must carry [M68kRegister].",
				displayName);
		}

		return new CilRegisterAbi(parameterRegisters, returnRegister ?? M68kRegister.D0);
	}

	private CilRegisterAbi GetRequiredRegisterAbi(
		MethodDefinition definition,
		MethodSignature<CilType> signature,
		string displayName,
		string role)
	{
		var parameterRegisters = new M68kRegister[signature.ParameterTypes.Length];
		var hasRegister = new bool[parameterRegisters.Length];
		M68kRegister? returnRegister = null;
		foreach (var parameterHandle in definition.GetParameters())
		{
			var parameter = Reader.GetParameter(parameterHandle);
			var register = TryGetRegister(parameter.GetCustomAttributes());
			if (parameter.SequenceNumber == 0)
			{
				returnRegister = register;
				continue;
			}

			var index = parameter.SequenceNumber - 1;
			if ((uint)index < (uint)parameterRegisters.Length && register is { } value)
			{
				parameterRegisters[index] = value;
				hasRegister[index] = true;
			}
		}

		if (hasRegister.Any(static present => !present))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				$"Every {role} parameter must carry [M68kRegister].",
				displayName);
		}

		return new CilRegisterAbi(parameterRegisters, returnRegister ?? M68kRegister.D0);
	}

	public CilField ResolveFieldToken(int token, CilMethod caller, int ilOffset)
	{
		if (!string.IsNullOrEmpty(caller.ModuleName) &&
			!string.Equals(caller.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(caller, ilOffset).ResolveFieldToken(token, caller, ilOffset);
		}

		var handle = MetadataTokens.EntityHandle(token);
		var field = handle.Kind switch
		{
			HandleKind.FieldDefinition => GetFieldForCaller(
				(FieldDefinitionHandle)handle,
				caller),
			HandleKind.MemberReference => ResolveFieldMemberReference(
				(MemberReferenceHandle)handle,
				caller,
				ilOffset),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Token 0x{token:X8} is not a field reference.",
				caller.DisplayName,
				ilOffset)
		};
		var separator = field.DisplayName.LastIndexOf("::", StringComparison.Ordinal);
		return field.IsStatic && separator >= 0
			? TryResolveTargetRuntimeField(field.ModuleName, field.DisplayName[..separator],
				field.DisplayName[(separator + 2)..], field.Type, caller, ilOffset) ?? field
			: field;
	}

	public CilMethod? GetTriggeredTypeInitializer(
		CilMethod caller,
		CilInstruction instruction)
	{
		if (instruction.OpCode == System.Reflection.Emit.OpCodes.Ldsfld ||
			instruction.OpCode == System.Reflection.Emit.OpCodes.Ldsflda ||
			instruction.OpCode == System.Reflection.Emit.OpCodes.Stsfld)
		{
			var field = ResolveFieldToken(
				(int)instruction.Operand!,
				caller,
				instruction.Offset);
			if (!field.IsStatic)
			{
				return null;
			}
			var initializer = GetModule(field.ModuleName)
				.GetTypeInitializerForField(field, caller);
			return initializer;
		}

		if (instruction.OpCode != System.Reflection.Emit.OpCodes.Call &&
			instruction.OpCode != System.Reflection.Emit.OpCodes.Callvirt &&
			instruction.OpCode != System.Reflection.Emit.OpCodes.Newobj)
		{
			return null;
		}

		var reference = ResolveMethodToken(
			(int)instruction.Operand!,
			caller,
			instruction.Offset);
		if (reference.FrameworkBinding?.TypeInitializerPolicy ==
			FrameworkTypeInitializerPolicy.TargetOwned)
		{
			return null;
		}
		var target = reference.Definition;
		if (target is null || target.IsTypeInitializer)
		{
			return null;
		}
		var triggersInitialization = instruction.OpCode == System.Reflection.Emit.OpCodes.Newobj ||
			!target.Signature.Header.IsInstance;
		return triggersInitialization
			? GetModule(target.ModuleName).GetTypeInitializerForMethod(target, caller)
			: null;
	}

	private CilMethod? GetTypeInitializerForField(CilField field, CilMethod caller) =>
		GetTypeInitializer(
			Reader.GetFieldDefinition(field.Handle).GetDeclaringType(),
			caller,
			field.ConstructedDeclaringType);

	public bool HasTypeInitializer(CilMethod method)
	{
		if (!string.IsNullOrEmpty(method.ModuleName) &&
			!string.Equals(method.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(method, 0).HasTypeInitializer(method);
		}
		if (method.DeclaringType.IsNil) return false;
		return Reader.GetTypeDefinition(method.DeclaringType).GetMethods().Any(handle =>
			Reader.StringComparer.Equals(Reader.GetMethodDefinition(handle).Name, ".cctor"));
	}

	private CilMethod? GetTypeInitializerForMethod(CilMethod method, CilMethod caller) =>
		GetTypeInitializer(
			Reader.GetMethodDefinition(method.Handle).GetDeclaringType(),
			caller,
			method.ConstructedDeclaringType);

	private CilMethod? GetTypeInitializer(
		TypeDefinitionHandle declaringType,
		CilMethod caller,
		CilType? constructedDeclaringType)
	{
		var row = MetadataTokens.GetRowNumber(declaringType);
		if (row <= 0 || row > Reader.TypeDefinitions.Count)
		{
			// Constructed cross-module references can retain the public contract's
			// metadata handle while targeting a private shadow method. Such a handle
			// is not a type definition in this module and cannot own a target cctor.
			return null;
		}
		var cacheKey = (
			declaringType,
			constructedDeclaringType?.DisplayName ?? string.Empty);
		if (!_typeInitializerCache.TryGetValue(cacheKey, out var initializer))
		{
			var type = Reader.GetTypeDefinition(declaringType);
			initializer = null;
			foreach (var methodHandle in type.GetMethods())
			{
				var definition = Reader.GetMethodDefinition(methodHandle);
				if (Reader.StringComparer.Equals(definition.Name, ".cctor"))
				{
					initializer = GetConstructedMethod(
						methodHandle,
						constructedDeclaringType,
						ImmutableArray<CilType>.Empty);
					break;
				}
			}
			_typeInitializerCache.Add(cacheKey, initializer);
		}
		return initializer is not null &&
			StringComparer.Ordinal.Equals(initializer.ModuleName, caller.ModuleName) &&
			initializer.DeclaringType == caller.DeclaringType &&
			StringComparer.Ordinal.Equals(initializer.Construction, caller.Construction)
			? null
			: initializer;
	}

	public ImmutableArray<uint> ReadUInt32FieldRva(
		int token,
		int count,
		CilMethod caller,
		int ilOffset)
	{
		if (!string.IsNullOrEmpty(caller.ModuleName) &&
			!string.Equals(caller.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(caller, ilOffset).ReadUInt32FieldRva(token, count, caller, ilOffset);
		}

		var handle = MetadataTokens.EntityHandle(token);
		if (handle.Kind != HandleKind.FieldDefinition || count < 0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Token 0x{token:X8} is not a valid initialized-data field.",
				caller.DisplayName,
				ilOffset);
		}

		var definition = Reader.GetFieldDefinition((FieldDefinitionHandle)handle);
		var rva = definition.GetRelativeVirtualAddress();
		var byteCount = checked(count * sizeof(uint));
		var section = _peReader.GetSectionData(rva);
		if (rva == 0 || section.Length < byteCount)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"Initialized-data field is missing or shorter than the target array.",
				caller.DisplayName,
				ilOffset);
		}

		var bytes = section.GetContent(0, byteCount);
		var result = ImmutableArray.CreateBuilder<uint>(count);
		for (var offset = 0; offset < byteCount; offset += sizeof(uint))
		{
			result.Add(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint))));
		}
		return result.MoveToImmutable();
	}

	public bool HasInitializedFieldRva(int token, CilMethod caller, int ilOffset)
	{
		if (!string.IsNullOrEmpty(caller.ModuleName) && caller.ModuleName != _assemblyName)
			return GetCallerModule(caller, ilOffset).HasInitializedFieldRva(token, caller, ilOffset);
		var handle = MetadataTokens.EntityHandle(token);
		if (handle.Kind != HandleKind.FieldDefinition) return false;
		var field = Reader.GetFieldDefinition((FieldDefinitionHandle)handle);
		return (field.Attributes & (FieldAttributes.Static | FieldAttributes.HasFieldRVA)) == (FieldAttributes.Static | FieldAttributes.HasFieldRVA);
	}

	public ImmutableArray<byte> ReadInitializedFieldRva(int token, CilMethod caller, int ilOffset)
	{
		if (!string.IsNullOrEmpty(caller.ModuleName) && caller.ModuleName != _assemblyName)
			return GetCallerModule(caller, ilOffset).ReadInitializedFieldRva(token, caller, ilOffset);
		var handle = MetadataTokens.EntityHandle(token);
		if (handle.Kind != HandleKind.FieldDefinition)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Initialized spans require an immediate field definition token.", caller.DisplayName, ilOffset);
		var field = Reader.GetFieldDefinition((FieldDefinitionHandle)handle);
		var type = field.DecodeSignature(_signatureProvider, caller.GenericContext);
		if ((field.Attributes & (FieldAttributes.Static | FieldAttributes.HasFieldRVA)) != (FieldAttributes.Static | FieldAttributes.HasFieldRVA))
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Initialized spans require a static RVA field with an exact value-type storage declaration.", caller.DisplayName, ilOffset);
		int size;
		if (type.Kind is CilTypeKind.Boolean or CilTypeKind.Character or CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger or CilTypeKind.FloatingPoint &&
			type.Size is 1 or 2 or 4 or 8)
		{
			// Roslyn uses primitive RVA fields for small array initializer blobs.
			size = type.Size;
		}
		else
		{
			var identity = ResolveRuntimeTypeIdentity(type, caller.ModuleName);
			if (type.Kind != CilTypeKind.ValueType || identity.ModuleName != _assemblyName || identity.Handle.Kind != HandleKind.TypeDefinition)
				throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Initialized spans require a static RVA field with an exact value-type storage declaration.", caller.DisplayName, ilOffset);
			var storage = Reader.GetTypeDefinition((TypeDefinitionHandle)identity.Handle);
			size = storage.GetLayout().Size;
			if (size <= 0 || storage.GetFields().Count != 0)
				throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Initialized span storage must declare a positive fixed byte size and contain no managed fields.", caller.DisplayName, ilOffset);
		}
		var rva = field.GetRelativeVirtualAddress();
		if (rva == 0)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Initialized span data has no RVA.", caller.DisplayName, ilOffset);
		var section = _peReader.GetSectionData(rva);
		if (section.Length < size)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Initialized span data is missing or shorter than its storage declaration.", caller.DisplayName, ilOffset);
		return section.GetContent(0, size);
	}

	public CilTypeLayout GetTypeLayout(TypeDefinitionHandle handle)
	{
		if (_layoutCache.TryGetValue(handle, out var cached))
		{
			return cached;
		}

		var definition = Reader.GetTypeDefinition(handle);
		var isValueType = IsValueTypeDefinition(definition);
		var inheritedSize = isValueType ? 0 : 8;
		var inheritedBitmap = 0u;
		var fieldOffsets = new Dictionary<FieldDefinitionHandle, int>();
		if (!isValueType && TryGetExperimentalListBase(handle) is { } listBase)
		{
			inheritedSize = listBase.Size;
			inheritedBitmap = listBase.ReferenceBitmap;
		}
		else if (!isValueType && !definition.BaseType.IsNil &&
			definition.BaseType.Kind == HandleKind.TypeDefinition)
		{
			var baseLayout = GetTypeLayout((TypeDefinitionHandle)definition.BaseType);
			inheritedSize = baseLayout.Size;
			inheritedBitmap = baseLayout.ReferenceBitmap;
			foreach (var item in baseLayout.FieldOffsets)
			{
				fieldOffsets.Add(item.Key, item.Value);
			}
		}
		else if (!isValueType && TryGetExperimentalCultureBase(handle) is { } cultureBase)
		{
			inheritedSize = cultureBase.Size;
			inheritedBitmap = cultureBase.ReferenceBitmap;
			// Field handles belong to their module. Copying external handles
			// into this map could collide with this class's own field rows.
		}
		else if (!isValueType && TryGetExperimentalCharacterMemoryManagerBase(handle) is { } memoryBase)
		{
			inheritedSize = memoryBase.Size;
			inheritedBitmap = memoryBase.ReferenceBitmap;
		}
		var size = inheritedSize;
		var bitmap = inheritedBitmap;
		foreach (var fieldHandle in definition.GetFields())
		{
			var field = GetField(fieldHandle);
			if (field.IsStatic)
			{
				continue;
			}

			if (TryGetFixedBufferSize(field.Type, out var fixedBufferSize))
			{
				fieldOffsets.Add(fieldHandle, size);
				size += fixedBufferSize;
				continue;
			}

			if (TryGetReferenceFreeStructLayout(
					field.Type,
					field.ModuleName,
					out var aggregateLayout) &&
				aggregateLayout.UsesAggregateTransport)
			{
				fieldOffsets.Add(fieldHandle, size);
				if (IsInterpolatedHandlerStorageType(GetTypeName(definition)) || FrameworkImplementationPack?.EnableUnlistedManagedBodies == true ||
					FrameworkImplementationPack?.IsPinnedStringBuilderInput == true &&
					(IsExperimentalManagedStructLayout(aggregateLayout) || IsExperimentalNullableJoinValue(field.Type) ||
					 _assemblyName == "System.Private.CoreLib" && GetTypeName(handle) == "System.Number/NumberBuffer" ||
					 _assemblyName == "CopperSharp.Runtime.Managed" && GetTypeName(definition) == "CopperSharp.Runtime.ShadowNumberBuffer"))
				{
					var word = (size - (isValueType ? 0 : 8)) / 4;
					if (aggregateLayout.ReferenceBitmap != 0 && (word >= 32 || (word > 0 && (aggregateLayout.ReferenceBitmap >> (32 - word)) != 0)))
						throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, $"Reference fields of '{field.DisplayName}' exceed the descriptor bitmap.");
					bitmap |= aggregateLayout.ReferenceBitmap << word;
				}
				size = checked(size + aggregateLayout.Size);
				continue;
			}

			if (field.Type.IsSupportedScalar && field.Type.Size == 8)
			{
				fieldOffsets.Add(fieldHandle, size);
				size = checked(size + 8);
				continue;
			}

			if ((!field.Type.IsSupportedScalar && !IsTransparentScalarType(field.Type)) || field.Type.Size > 4)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					$"Field '{field.DisplayName}' has unsupported type '{field.Type.DisplayName}'.");
			}

				var fieldIndex = (size - (isValueType ? 0 : 8)) / 4;
				if (field.Type.IsReference)
			{
				if (fieldIndex is < 0 or >= 32)
					throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, $"Reference fields of '{field.DisplayName}' exceed the descriptor bitmap.");
				bitmap |= 1u << fieldIndex;
			}

			fieldOffsets.Add(fieldHandle, size);
			size += 4;
		}

		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && _assemblyName == "System.Private.CoreLib" &&
			GetTypeName(definition) is "System.TwoObjects" or "System.ThreeObjects")
		{
			var expectedLength = GetTypeName(definition) == "System.TwoObjects" ? 2 : 3;
			var attributes = definition.GetCustomAttributes().Where(attribute =>
				GetAttributeTypeName(attribute) == "System.Runtime.CompilerServices.InlineArrayAttribute").ToArray();
			if (!isValueType || size != 4 || bitmap != 1 || fieldOffsets.Count != 1 || attributes.Length != 1 ||
				Reader.GetCustomAttribute(attributes[0]).DecodeValue(new AttributeTypeProvider(Reader)).FixedArguments is not [{ Value: int length }] ||
				length != expectedLength)
				throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Pinned object argument buffer has an unexpected inline-array layout.");
			size = expectedLength * 4;
			bitmap = (1u << expectedLength) - 1;
		}
		if (isValueType && size == 0 && FrameworkImplementationPack?.EnableUnlistedManagedBodies == true &&
			!Net10FrameworkContract.Default.IsFrameworkAssembly(_assemblyName)) size = 4;
		var layout = new CilTypeLayout(
			handle,
			GetTypeName(definition),
			size,
			bitmap,
			fieldOffsets,
			_assemblyName);
		_layoutCache.Add(handle, layout);
		return layout;
	}

	public CilTypeLayout GetTypeLayout(CilMethod method)
	{
		var module = GetModule(method.ModuleName);
		return method.ConstructedDeclaringType is { } constructedType
			? module.GetConstructedTypeLayout(method.DeclaringType, constructedType)
			: module.GetTypeLayout(method.DeclaringType);
	}

	internal CilTypeLayout GetAllocationLayout(MethodReference constructor) =>
		constructor.AllocationLayout ?? GetTypeLayout(constructor.Definition ??
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Object allocation requires a managed constructor."));

	public CilTypeLayout GetTypeLayout(CilField field)
	{
		var module = GetModule(field.ModuleName);
		return field.ConstructedDeclaringType is { } constructedType
			? module.GetConstructedTypeLayout(field.DeclaringType, constructedType)
			: module.GetTypeLayout(field.DeclaringType);
	}

	public CilTypeLayout GetTypeLayout(CilMethod owner, TypeDefinitionHandle handle) =>
		GetModule(owner.ModuleName).GetTypeLayout(handle);

	public CilTypeLayout GetTypeLayout(CilTypeLayout owner, TypeDefinitionHandle handle)
	{
		var module = GetModule(owner.ModuleName);
		var row = MetadataTokens.GetRowNumber(handle);
		if (row <= 0 || row > module.Reader.TypeDefinitions.Count)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Base type handle row {row} for runtime layout '{owner.DisplayName}' is outside module '{module.AssemblyName}' ({module.Reader.TypeDefinitions.Count} type definitions).");
		}
		return module.GetTypeLayout(handle);
	}

	public CilVirtualTable GetVirtualTable(CilTypeLayout layout) =>
		GetModule(layout.ModuleName).GetVirtualTable(
			layout.Handle,
			layout.ConstructedType);

	public CilType GetRuntimeTypeSignature(CilTypeLayout layout)
	{
		if (layout.ConstructedType is { } constructed) return constructed;
		var module = GetModule(layout.ModuleName);
		return module._signatureProvider.GetTypeFromDefinition(module.Reader, layout.Handle,
			module.IsValueTypeDefinition(module.Reader.GetTypeDefinition(layout.Handle)) ? (byte)0x11 : (byte)0x12);
	}

	private bool IsInterpolatedHandlerStorageType(string name) =>
		_assemblyName == "System.Private.CoreLib" &&
			(IsExperimentalCoreLibModule && name is "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler" or "System.Text.ValueStringBuilder" ||
			 FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && name is "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler" or "System.Text.ValueStringBuilder") ||
		_assemblyName == "CopperSharp.Runtime.Managed" && name is "CopperSharp.Runtime.ShadowInterpolatedStringHandlerBuffer" or "CopperSharp.Runtime.ShadowValueStringBuilder";

	public bool IsInterpolatedHandlerStorageMethod(CilMethod method)
	{
		var module = GetModule(method.ModuleName);
		return module.IsInterpolatedHandlerStorageType(module.GetTypeName(module.Reader.GetTypeDefinition(method.DeclaringType)));
	}

	public CilMethod? TryGetEffectiveFinalizer(CilTypeLayout layout) =>
		GetVirtualTable(layout).Slots.FirstOrDefault(
			static candidate =>
				candidate.Name == "Finalize" &&
				candidate.Signature.Header.IsInstance &&
				candidate.Signature.ParameterTypes.Length == 0 &&
				candidate.Signature.ReturnType.IsVoid &&
				candidate.IsVirtual &&
				!candidate.IsNewSlot);

	public int GetVirtualSlot(CilMethod method)
	{
		var declaration = GetExperimentalCultureFallbackDeclaration(method) ?? method;
		return GetModule(declaration.ModuleName).GetVirtualSlotCore(declaration);
	}

	private CilMethod? GetExperimentalCultureFallbackDeclaration(CilMethod method) =>
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) &&
		_root._frameworkVirtualFallbacks.TryGetValue(method.Identity, out var fallback) &&
		fallback.Binding.Member.DeclaringType.FullMetadataName == "System.Globalization.CultureInfo"
			? TryResolveManagedMethod("System.Private.CoreLib", "System.Globalization.CultureInfo",
				fallback.Binding.Member.Name, method.Signature)
			: null;

	public IReadOnlyList<CilMethod> GetVirtualImplementations(CilMethod declaration) =>
		IsObjectJoinDispatch(declaration)
			? GetObjectJoinDispatchEntries(declaration).Select(static entry => entry.Method).Prepend(declaration)
				.DistinctBy(static method => method.Identity).ToArray()
			: _root._frameworkVirtualFallbacks.ContainsKey(declaration.Identity)
			? GetFrameworkVirtualImplementations(declaration)
			: GetModule(declaration.ModuleName).GetVirtualImplementationsCore(declaration);

	public void RegisterReachableDispatchLayout(CilTypeLayout layout) =>
		_root._reachableDispatchLayouts.TryAdd(layout.Identity, layout);

	public CilTypeLayout? RegisterStringDispatchLayout()
	{
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true) return null;
		var target = ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, "string"), "System.Private.CoreLib");
		var layout = GetRuntimeTypeLayout(target);
		RegisterReachableDispatchLayout(layout);
		return layout;
	}

	public CilTypeLayout? RegisterStringDispatchLayout(CilMethod method)
	{
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true) return null;
		// Strings can arrive through parameters/fields as well as literals and
		// intrinsic factories. They use a runtime allocation header, but still
		// supply ordinary CoreLib virtual and interface implementations.
		if (IsString(method.Signature.ReturnType) || method.Signature.ParameterTypes.Any(IsString) ||
			method.Locals.Any(IsString) || method.Instructions.Any(instruction =>
				instruction.OpCode == OpCodes.Ldstr ||
				((instruction.OpCode == OpCodes.Ldfld || instruction.OpCode == OpCodes.Ldsfld) &&
				 IsString(ResolveFieldToken((int)instruction.Operand!, method, instruction.Offset).Type))))
			return RegisterStringDispatchLayout();
		return null;
	}

	internal static bool IsString(CilType type) => type.DisplayName is "string" or "System.String";

	internal CilEnumData ReadEnumData(CilRuntimeTypeTarget target)
	{
		var owner = GetModule(target.ModuleName);
		var definition = owner.Reader.GetTypeDefinition((TypeDefinitionHandle)target.Handle);
		var verifiedBase = definition.BaseType.Kind == HandleKind.TypeDefinition && target.ModuleName == "System.Private.CoreLib" &&
			owner.GetTypeName(owner.Reader.GetTypeDefinition((TypeDefinitionHandle)definition.BaseType)) == "System.Enum" ||
			definition.BaseType.Kind == HandleKind.TypeReference &&
			owner.GetTypeName(owner.Reader.GetTypeReference((TypeReferenceHandle)definition.BaseType)) == "System.Enum" &&
			owner.GetReferencedAssemblyName(owner.Reader.GetTypeReference((TypeReferenceHandle)definition.BaseType).ResolutionScope) is "System.Runtime" or "System.Private.CoreLib";
		var payload = definition.GetFields().Select(owner.GetField).Where(field => !field.IsStatic).ToArray();
		if (!verifiedBase || payload is not [var underlying] || underlying.Type.IsEnum || underlying.Type.Kind != target.Type.Kind || underlying.Type.Size != target.Type.Size ||
			owner.Reader.GetString(owner.Reader.GetFieldDefinition(underlying.Handle).Name) != "value__")
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Enum metadata requires a verified Enum base and its single integral value field.");
		var values = new List<ulong>(); var names = new List<string>();
		foreach (var handle in definition.GetFields())
		{
			var field = owner.Reader.GetFieldDefinition(handle);
			if ((field.Attributes & FieldAttributes.Literal) == 0) continue;
			var constantHandle = field.GetDefaultValue();
			if (constantHandle.IsNil) throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Enum literal has no constant value.");
			var constant = owner.Reader.GetConstant(constantHandle);
			var blob = owner.Reader.GetBlobReader(constant.Value);
			var expected = (target.Type.Kind, target.Type.Size) switch
			{
				(CilTypeKind.SignedInteger, 1) => ConstantTypeCode.SByte, (CilTypeKind.UnsignedInteger, 1) => ConstantTypeCode.Byte,
				(CilTypeKind.SignedInteger, 2) => ConstantTypeCode.Int16, (CilTypeKind.UnsignedInteger, 2) => ConstantTypeCode.UInt16,
				(CilTypeKind.SignedInteger, 4) => ConstantTypeCode.Int32, (CilTypeKind.UnsignedInteger, 4) => ConstantTypeCode.UInt32,
				(CilTypeKind.SignedInteger, 8) => ConstantTypeCode.Int64, (CilTypeKind.UnsignedInteger, 8) => ConstantTypeCode.UInt64,
				_ => throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Enum underlying type is not integral.")
			};
			if (constant.TypeCode != expected || blob.Length != target.Type.Size)
				throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Enum literal differs from its declared underlying representation.");
			var value = constant.TypeCode switch
			{
				ConstantTypeCode.SByte => (ulong)(byte)blob.ReadSByte(), ConstantTypeCode.Byte => blob.ReadByte(),
				ConstantTypeCode.Int16 => (ulong)(ushort)blob.ReadInt16(), ConstantTypeCode.UInt16 => blob.ReadUInt16(),
				ConstantTypeCode.Int32 => (ulong)(uint)blob.ReadInt32(), ConstantTypeCode.UInt32 => blob.ReadUInt32(),
				ConstantTypeCode.Int64 => (ulong)blob.ReadInt64(), ConstantTypeCode.UInt64 => blob.ReadUInt64(),
				_ => throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Enum constant has a non-integral representation.")
			};
			values.Add(value); names.Add(owner.Reader.GetString(field.Name));
		}
		var valueArray = values.ToArray(); var nameArray = names.ToArray();
		if (!valueArray.Zip(valueArray.Skip(1), (left, right) => left <= right).All(sorted => sorted))
			Array.Sort(valueArray, nameArray);
		return new CilEnumData(target.Type, target.ModuleName, MetadataTokens.GetToken(target.Handle),
			owner.HasFrameworkFlagsAttribute(definition.GetCustomAttributes()), valueArray, nameArray);
	}

	private bool HasFrameworkFlagsAttribute(CustomAttributeHandleCollection attributes)
	{
		foreach (var handle in attributes)
		{
			var constructor = Reader.GetCustomAttribute(handle).Constructor;
			if (constructor.Kind == HandleKind.MethodDefinition && _assemblyName == "System.Private.CoreLib" &&
				GetTypeName(Reader.GetTypeDefinition(Reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType())) == "System.FlagsAttribute") return true;
			if (constructor.Kind != HandleKind.MemberReference) continue;
			var member = Reader.GetMemberReference((MemberReferenceHandle)constructor);
			if (member.Parent.Kind != HandleKind.TypeReference) continue;
			var type = Reader.GetTypeReference((TypeReferenceHandle)member.Parent);
			if (GetTypeName(type) == "System.FlagsAttribute" && GetReferencedAssemblyName(type.ResolutionScope) is "System.Runtime" or "System.Private.CoreLib") return true;
		}
		return false;
	}

	internal CilTypeLayout GetEnumMetadataLayout()
	{
		if (GetOrLoadModule("CopperSharp.Runtime.Managed") is null)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidInput, "Enum formatting requires the supplied target runtime assembly.");
		var target = ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, "CopperSharp.Runtime.ShadowEnumMetadata"), "CopperSharp.Runtime.Managed");
		var layout = GetRuntimeTypeLayout(target);
		var owner = GetModule(layout.ModuleName);
		var fields = owner.Reader.GetTypeDefinition(layout.Handle).GetFields().Select(owner.GetField).Where(field => !field.IsStatic).ToArray();
		var names = new[] { "Width", "Signed", "Flags", "Values", "Names" };
		var types = new[] { "int", "int", "int", "ulong[]", "string[]" };
		var offsets = new[] { 8, 12, 16, 20, 24 };
		if (layout.ModuleName != "CopperSharp.Runtime.Managed" || layout.Size != 28 || layout.ReferenceBitmap != 24 || fields.Length != 5 ||
			fields.Where((field, index) => owner.Reader.GetString(owner.Reader.GetFieldDefinition(field.Handle).Name) != names[index] ||
				field.Type.DisplayName != types[index] || layout.FieldOffsets[field.Handle] != offsets[index]).Any())
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Enum metadata helper differs from its immutable target representation.");
		return layout;
	}

	internal bool IsPinnedBoxedEnumType(CilType type, string moduleName)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || !type.IsEnum ||
			type.Kind is not (CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger) || type.Size is not (1 or 2 or 4 or 8)) return false;
		var target = ResolveRuntimeTypeIdentity(type, moduleName);
		if (target.Handle.Kind != HandleKind.TypeDefinition) return false;
		if (target.Type.Kind != type.Kind || target.Type.Size != type.Size)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Boxed enum signature differs from its metadata representation.");
		_ = ReadEnumData(target); // Verify the Enum base, payload, constants and flags.
		return true;
	}

	public CilTypeLayout? RegisterBoxedDispatchLayout(CilType type, string moduleName)
	{
		type = type.NullableElementType ?? type;
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && IsListEnumeratorType(type) &&
			(type.DisplayName.StartsWith("CopperSharp.Runtime.ShadowListEnumerator`1<", StringComparison.Ordinal) ||
			 type.DisplayName == "System.Collections.Generic.List`1/Enumerator<System.Decimal>" &&
			 type.GenericArguments is [{ DisplayName: "System.Decimal" }]) &&
			TryGetListEnumeratorLayout(type, moduleName, out var storage) && GetOrLoadModule(moduleName) is not null)
		{
			var enumeratorTarget = ResolveRuntimeTypeIdentity(type, moduleName);
			var expectedModule = type.DisplayName.StartsWith("CopperSharp.Runtime.ShadowListEnumerator`1<", StringComparison.Ordinal)
				? "CopperSharp.Runtime.Managed" : "System.Private.CoreLib";
			if (enumeratorTarget.ModuleName == expectedModule && !enumeratorTarget.Handle.IsNil)
			{
				var enumeratorLayout = GetRuntimeTypeLayout(enumeratorTarget);
				if (enumeratorLayout.Size != storage.Size || enumeratorLayout.ReferenceBitmap != storage.ReferenceBitmap)
					throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata,
						$"Boxed join List enumerator '{type.DisplayName}' has layout {enumeratorLayout.Size}/{enumeratorLayout.ReferenceBitmap:X8}; expected {storage.Size}/{storage.ReferenceBitmap:X8}.");
				RegisterReachableDispatchLayout(enumeratorLayout);
				return enumeratorLayout;
			}
		}
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && type is { Kind: CilTypeKind.ValueType, IsEnum: false } &&
			TryGetStructLayout(type, moduleName, out var applicationLayout) && IsExperimentalManagedStructLayout(applicationLayout))
		{
			// Boxed application iterators must enter the same dispatch fixed point
			// as reference allocations, including their later IDisposable calls.
			RegisterReachableDispatchLayout(applicationLayout);
			return applicationLayout;
		}
		var pinnedEnum = IsPinnedBoxedEnumType(type, moduleName);
		var pinnedFloating = FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && !type.IsEnum &&
			type is { Kind: CilTypeKind.FloatingPoint, Size: 4, DisplayName: "float" } or { Kind: CilTypeKind.FloatingPoint, Size: 8, DisplayName: "double" };
		var pinnedDecimal = FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && !type.IsEnum &&
			type is { Kind: CilTypeKind.ValueType, DisplayName: "System.Decimal" } && TryGetDecimalLayout(type, out _);
		if (type.IsReference || (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true &&
			!(pinnedEnum || pinnedFloating || pinnedDecimal || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && !type.IsEnum &&
			  type is { Kind: CilTypeKind.SignedInteger, Size: 1, DisplayName: "sbyte" } or
				{ Kind: CilTypeKind.UnsignedInteger, Size: 1, DisplayName: "byte" } or
				{ Kind: CilTypeKind.SignedInteger, Size: 2, DisplayName: "short" } or
				{ Kind: CilTypeKind.UnsignedInteger, Size: 2, DisplayName: "ushort" } or
				{ Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" } or
				{ Kind: CilTypeKind.UnsignedInteger, Size: 4, DisplayName: "uint" } or
				{ Kind: CilTypeKind.SignedInteger, Size: 8, DisplayName: "long" } or
				{ Kind: CilTypeKind.UnsignedInteger, Size: 8, DisplayName: "ulong" }))) return null;
		var target = ResolveRuntimeTypeIdentity(type, moduleName);
		if (target.Handle.IsNil || target.Handle.Kind != HandleKind.TypeDefinition ||
			FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !pinnedEnum && target.ModuleName != "System.Private.CoreLib") return null;
		var layout = GetRuntimeTypeLayout(target);
		if (pinnedFloating && !TryGetExperimentalFloatingFormattingType(layout, out _))
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Boxed floating-point type requires its exact CoreLib payload layout.");
		RegisterReachableDispatchLayout(layout);
		return layout;
	}

	public CilTypeLayout? RegisterRuntimeTypeDispatchLayout()
	{
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true) return null;
		var type = new CilType(CilTypeKind.ManagedReference, 4, "System.RuntimeType");
		var target = ResolveRuntimeTypeIdentity(type, "System.Private.CoreLib");
		var layout = GetRuntimeTypeLayout(target);
		RegisterReachableDispatchLayout(layout);
		return layout;
	}

	internal CilTypeLayout GetRuntimeTypeNameLayout()
	{
		if (GetOrLoadModule("CopperSharp.Runtime.Managed") is null)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidInput, "Runtime type names require the supplied target runtime assembly.");
		var target = ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4,
			"CopperSharp.Runtime.ShadowRuntimeType"), "CopperSharp.Runtime.Managed");
		var layout = GetRuntimeTypeLayout(target);
		var owner = GetModule(layout.ModuleName);
		var fields = owner.Reader.GetTypeDefinition(layout.Handle).GetFields().Select(owner.GetField).Where(field => !field.IsStatic).ToArray();
		var names = new[] { "_classification", "_name", "_defaultObjectString" };
		var types = new[] { "int", "string", "int" };
		if (layout.ModuleName != "CopperSharp.Runtime.Managed" || layout.Size != 20 || layout.ReferenceBitmap != 2 || fields.Length != 3 ||
			fields.Where((field, index) => owner.Reader.GetString(owner.Reader.GetFieldDefinition(field.Handle).Name) != names[index] ||
				field.Type.DisplayName != types[index] || layout.FieldOffsets[field.Handle] != 8 + index * 4).Any())
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Runtime type name helper differs from its immutable target representation.");
		return layout;
	}

	public bool HasDefaultObjectToString(CilRuntimeTypeTarget target)
	{
		if (target.Type.ElementType is not null && target.Type.DisplayName.EndsWith("[]", StringComparison.Ordinal)) return true;
		// Type objects also describe types that never have an allocated instance.
		// Inspect metadata without constructing their layouts or virtual tables.
		var visited = new HashSet<(string, EntityHandle)>();
		while (!target.Handle.IsNil && target.Handle.Kind == HandleKind.TypeDefinition && visited.Add((target.ModuleName, target.Handle)))
		{
			var owner = GetModule(target.ModuleName);
			var definition = owner.Reader.GetTypeDefinition((TypeDefinitionHandle)target.Handle);
			if (target.ModuleName == "System.Private.CoreLib" && owner.GetTypeName(definition) == "System.Object") return true;
			foreach (var handle in definition.GetMethods())
			{
				var metadata = owner.Reader.GetMethodDefinition(handle);
				if ((metadata.Attributes & MethodAttributes.Virtual) == 0 || (metadata.Attributes & MethodAttributes.NewSlot) != 0 ||
					owner.Reader.GetString(metadata.Name) != "ToString") continue;
				var method = owner.GetMethod(handle);
				if (method.Signature.Header.IsInstance && method.Signature.GenericParameterCount == 0 &&
					method.Signature.ParameterTypes.Length == 0 && method.Signature.ReturnType.DisplayName == "string")
					return target.ModuleName == "CopperSharp.Runtime.Managed" && owner.GetTypeName(definition) == "CopperSharp.Runtime.ShadowObjectJoinDispatch" ||
						target.ModuleName == "System.Private.CoreLib" && owner.GetTypeName(definition) == "System.ValueType";
			}
			// Explicit MethodImpl declarations require slot resolution; do not guess.
			if (definition.GetMethodImplementations().Count != 0) return false;
			var baseType = definition.BaseType;
			if (baseType.IsNil) return false;
			if (baseType.Kind == HandleKind.TypeDefinition)
			{
				target = target with { Handle = baseType };
				continue;
			}
			var type = baseType.Kind switch {
				HandleKind.TypeReference => owner.ResolveReferencedType((TypeReferenceHandle)baseType),
				HandleKind.TypeSpecification => owner.Reader.GetTypeSpecification((TypeSpecificationHandle)baseType).DecodeSignature(
					owner._signatureProvider, new CilGenericContext(target.Type.GenericArguments, ImmutableArray<CilType>.Empty)),
				_ => null
			};
			if (type is null) return false;
			target = ResolveRuntimeTypeIdentity(type, owner._assemblyName);
		}
		return false;
	}

	public string FormatRuntimeTypeName(CilRuntimeTypeTarget target)
	{
		var type = target.Type;
		if (type.ElementType is { } element)
			return FormatRuntimeTypeName(ResolveRuntimeTypeIdentity(element, target.ModuleName)) +
				type.DisplayName[element.DisplayName.Length..];
		if (type.Kind is CilTypeKind.GenericParameter or CilTypeKind.FunctionPointer)
			throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, $"Runtime type name '{type.DisplayName}' needs a resolved metadata identity.");
		if (target.Handle.Kind != HandleKind.TypeDefinition)
			target = ResolveRuntimeTypeIdentity(type, target.ModuleName);
		if (target.Handle.Kind != HandleKind.TypeDefinition)
			throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, $"Runtime type name '{type.DisplayName}' needs a resolved metadata identity.");
		var owner = GetModule(target.ModuleName);
		var definition = owner.Reader.GetTypeDefinition((TypeDefinitionHandle)target.Handle);
		var name = owner.GetTypeName((TypeDefinitionHandle)target.Handle).Replace('/', '+');
		if (type.GenericArguments.IsDefaultOrEmpty && definition.GetGenericParameters().Count != 0)
			return name + "[" + string.Join(',', definition.GetGenericParameters().Select(handle =>
				owner.Reader.GetString(owner.Reader.GetGenericParameter(handle).Name))) + "]";
		return type.GenericArguments.IsDefaultOrEmpty ? name : name + "[" + string.Join(',', type.GenericArguments.Select(argument =>
			FormatRuntimeTypeName(ResolveRuntimeTypeIdentity(argument, target.ModuleName)))) + "]";
	}

	public CilMethod ApplyTargetRuntimeOverride(CilMethod method, bool materializeBody = true)
	{
		if (method.HasDeferredBody)
		{
			var owner = GetModule(method.ModuleName);
			var target = owner.ApplyTargetRuntimeOverrideCore(method);
			var resolved = target.Identity != method.Identity || !materializeBody ? target : owner.GetConstructedMethod(method.Handle,
				method.ConstructedDeclaringType, method.MethodTypeArguments);
			return materializeBody ? LowerNumericOperations(resolved) : resolved;
		}
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) || method.IsImport)
		{
			return materializeBody ? LowerNumericOperations(method) : method;
		}

		var implementation = GetModule(method.ModuleName).ApplyTargetRuntimeOverrideCore(method);
		return materializeBody ? LowerNumericOperations(implementation) : implementation;
	}

	private CilMethod ApplyTargetRuntimeOverrideCore(CilMethod method)
	{
		var member = DescribeFrameworkMethodDefinition(
			method.Handle,
			ImmutableArray<FrameworkTypeId>.Empty);
		if (!(FrameworkImplementationPack is not null && FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(
			member, FrameworkImplementationPack, null, out _)) && !FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
				member,
				enableUnlistedManagedBodies: FrameworkImplementationPack?.EnableUnlistedManagedBodies == true,
				out _))
		{
			return method;
		}

		var definition = Reader.GetTypeDefinition(method.DeclaringType);
		return TryResolveRegisteredBinding(
				member,
				GetTypeName(definition),
				method.Signature,
				method.ConstructedDeclaringType,
				method.MethodTypeArguments.IsDefault
					? null
					: method.MethodTypeArguments)?.Definition ?? method;
	}

	public CilMethod? TryGetVirtualImplementation(
		CilTypeLayout layout,
		CilMethod declaration)
	{
		if (GetExperimentalCultureFallbackDeclaration(declaration) is { } cultureDeclaration)
		{
			return TryGetVirtualImplementation(layout, cultureDeclaration);
		}
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || IsPinnedBoxedEnumType(GetRuntimeTypeSignature(layout), layout.ModuleName)) && GetRuntimeTypeSignature(layout).IsEnum &&
			declaration.ModuleName == "System.Private.CoreLib" && GetTypeDisplayName(declaration.DeclaringType, declaration) is "System.Object" or "System.Enum")
		{
			var slot = GetVirtualSlot(declaration);
			var table = GetVirtualTable(layout);
			return slot < table.Slots.Length && !table.Slots[slot].IsAbstract ? table.Slots[slot] : null;
		}
		if (IsObjectJoinDispatch(declaration))
		{
			return TryGetObjectJoinOverride(layout, declaration);
		}
		if (_root._frameworkVirtualFallbacks.ContainsKey(declaration.Identity))
		{
			var slot = GetModule(declaration.ModuleName).GetVirtualSlotCore(declaration);
			var table = GetModule(layout.ModuleName).GetVirtualTable(
				layout.Handle,
				layout.ConstructedType);
			return slot < table.Slots.Length && !table.Slots[slot].IsAbstract
				? table.Slots[slot]
				: null;
		}
		return GetModule(layout.ModuleName).TryGetVirtualImplementationCore(
			layout,
			declaration);
	}

	internal bool IsObjectJoinDispatch(CilMethod declaration) =>
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) &&
		declaration.ModuleName == "CopperSharp.Runtime.Managed" &&
		declaration.DisplayName == "CopperSharp.Runtime.ShadowObjectJoinDispatch::ToString" &&
		_root._frameworkVirtualFallbacks.ContainsKey(declaration.Identity);

	internal IReadOnlyList<(CilTypeLayout Layout, CilMethod Method)> GetObjectJoinDispatchEntries(CilMethod declaration) =>
		_root._reachableDispatchLayouts.Values
			.OrderBy(static layout => layout.ModuleName, StringComparer.Ordinal)
			.ThenBy(static layout => MetadataTokens.GetToken(layout.Handle))
			.ThenBy(static layout => layout.Identity.Construction, StringComparer.Ordinal)
			.Select(layout => (Layout: layout, Method: TryGetObjectJoinOverride(layout, declaration)))
			.Where(static entry => entry.Method is not null)
			.Select(static entry => (entry.Layout, entry.Method!)).ToArray();

	private CilMethod? TryGetObjectJoinOverride(CilTypeLayout layout, CilMethod declaration)
	{
		var type = GetRuntimeTypeSignature(layout);
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && layout.ModuleName == "System.Private.CoreLib" && layout.DisplayName == "System.Text.StringBuilder")
			return TryResolveManagedMethod("System.Private.CoreLib", "System.Text.StringBuilder", "ToString", declaration.Signature);
		if (type.IsEnum && (IsExperimentalGenericJoinElement(type) || IsPinnedBoxedEnumType(type, layout.ModuleName)))
		{
			_ = ReadEnumData(ResolveRuntimeTypeIdentity(type, layout.ModuleName));
			var enumToString = TryResolveManagedMethod("System.Private.CoreLib", "System.Enum", "ToString", declaration.Signature);
			return enumToString is null ? null : ApplyTargetRuntimeOverride(enumToString);
		}
		// Verified numeric boxes use the CoreLib override for their payload.
		if (IsExperimentalDecimalFormattingLayout(layout) || TryGetExperimentalFloatingFormattingType(layout, out _))
			return GetVirtualTable(layout).Slots.FirstOrDefault(method =>
				method.ModuleName == "System.Private.CoreLib" && method.DisplayName == layout.DisplayName + "::ToString" &&
				!method.IsAbstract && !method.IsNewSlot && SignaturesMatch(method.Signature, declaration.Signature));
		// Other CoreLib and runtime-private objects have independent or synthetic
		// dispatch layouts. Application reference layouts use their own overrides.
		if (layout.ModuleName is "System.Private.CoreLib" or "CopperSharp.Runtime.Managed" or "CopperSharp.Compiler")
		{
			return null;
		}
		var module = GetModule(layout.ModuleName);
		if (module.IsValueTypeDefinition(module.Reader.GetTypeDefinition(layout.Handle)) ||
			!module.HasLocalFrameworkObjectHierarchy(layout))
		{
			return null;
		}
		var slot = GetVirtualSlot(declaration);
		var table = module.GetVirtualTable(layout.Handle, layout.ConstructedType);
		if (slot >= table.Slots.Length)
		{
			return null;
		}
		var implementation = table.Slots[slot];
		// Preserve the original Object slot for classes with no override, even
		// when they introduce a separate hidden ToString slot. Its managed body
		// uses the existing descriptor-to-runtime-type name mapping.
		if (implementation.ModuleName == "System.Private.CoreLib" && implementation.DisplayName == "System.Object::ToString" &&
			!implementation.IsAbstract && SignaturesMatch(implementation.Signature, declaration.Signature))
			return implementation;
		return implementation.Identity != declaration.Identity && !implementation.IsAbstract && !implementation.IsNewSlot &&
			implementation.Name == "ToString" && SignaturesMatch(implementation.Signature, declaration.Signature)
			? implementation : null;
	}

	private bool IsFrameworkObjectReference(EntityHandle handle)
	{
		if (handle.Kind != HandleKind.TypeReference) return false;
		var reference = Reader.GetTypeReference((TypeReferenceHandle)handle);
		if (GetTypeName(reference) != "System.Object" || reference.ResolutionScope.Kind != HandleKind.AssemblyReference)
			return false;
		var assembly = Reader.GetString(Reader.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name);
		return assembly is "System.Runtime" or "System.Private.CoreLib";
	}

	private bool HasLocalFrameworkObjectHierarchy(CilTypeLayout layout)
	{
		var current = layout.Handle;
		var construction = layout.ConstructedType;
		for (var depth = 0; depth < Reader.TypeDefinitions.Count; depth++)
		{
			var parent = Reader.GetTypeDefinition(current).BaseType;
			if (parent.Kind == HandleKind.TypeDefinition)
			{
				current = (TypeDefinitionHandle)parent;
				continue;
			}
			if (parent.Kind == HandleKind.TypeReference)
			{
				return IsFrameworkObjectReference(parent);
			}
			if (parent.Kind != HandleKind.TypeSpecification)
			{
				return false;
			}
			var baseType = Reader.GetTypeSpecification((TypeSpecificationHandle)parent).DecodeSignature(
				_signatureProvider,
				new CilGenericContext(construction?.GenericArguments ?? ImmutableArray<CilType>.Empty, ImmutableArray<CilType>.Empty));
			if (!TryFindConstructedGenericDefinition(baseType, out var target) ||
				target.ModuleName != _assemblyName || target.Handle.Kind != HandleKind.TypeDefinition)
			{
				return false;
			}
			current = (TypeDefinitionHandle)target.Handle;
			construction = baseType;
		}
		return false;
	}

	public CilInterfaceDefinition GetInterfaceDefinition(CilMethod method) =>
		GetModule(method.ModuleName).GetInterfaceDefinition(
			method.DeclaringType,
			method.ConstructedDeclaringType);

	public int GetInterfaceSlot(CilMethod method) =>
		GetModule(method.ModuleName).GetInterfaceSlotCore(method);

	public IReadOnlyList<CilMethod> GetInterfaceImplementations(CilMethod declaration)
	{
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true &&
			(IsPinnedCallbackInterfaceName(GetInterfaceDefinition(declaration).DisplayName) || IsPinnedGenericJoinInterface(declaration.ConstructedDeclaringType))) && declaration.ModuleName == "System.Private.CoreLib")
		{
			// Framework interfaces participate in the same allocation-driven fixed
			// point as virtual dispatch. Scanning the entire host CoreLib imports
			// unrelated formatters and platform dependencies into every call.
			var definition = GetInterfaceDefinition(declaration);
			var slot = GetInterfaceSlot(declaration);
			return _root._reachableDispatchLayouts.Values
				.Select(layout => TryGetInterfaceImplementation(layout, definition))
				.OfType<CilInterfaceImplementation>().Select(implementation => implementation.Methods[slot])
				.DistinctBy(static method => method.Identity).ToArray();
		}
		return GetModule(declaration.ModuleName).GetInterfaceImplementationsCore(declaration);
	}

	public IReadOnlyList<CilMethod> GetInterfaceTableImplementations(CilMethod declaration) =>
		GetInterfaceDefinition(declaration).Slots
			.SelectMany(GetInterfaceImplementations)
			.DistinctBy(static method => method.Identity)
			.ToArray();

	public CilInterfaceImplementation? TryGetInterfaceImplementation(
		CilTypeLayout layout,
		CilInterfaceDefinition interfaceDefinition)
	{
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || IsPinnedBoxedEnumType(GetRuntimeTypeSignature(layout), layout.ModuleName)) && layout.ModuleName != "System.Private.CoreLib" &&
			GetRuntimeTypeSignature(layout).IsEnum && interfaceDefinition.Identity.ModuleName == "System.Private.CoreLib" &&
			interfaceDefinition.DisplayName is "System.IFormattable" or "System.ISpanFormattable")
		{
			var target = ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, "System.Enum"), "System.Private.CoreLib");
			return TryGetInterfaceImplementation(GetRuntimeTypeLayout(target), interfaceDefinition);
		}
		// Private shadow interfaces have a single compiler-owned implementation in
		// Runtime.Managed. Other reachable user/framework layouts cannot implement
		// them and must not trigger the deliberately unsupported general
		// cross-module interface-map path.
		if (!string.Equals(
				layout.ModuleName,
				interfaceDefinition.Identity.ModuleName,
				StringComparison.Ordinal) &&
			IsPrivateShadowInterface(interfaceDefinition))
		{
			return null;
		}

		return GetModule(layout.ModuleName).TryGetInterfaceImplementation(
			layout.Handle,
			layout.ConstructedType,
			interfaceDefinition);
	}

	private static bool IsPrivateShadowInterface(
		CilInterfaceDefinition interfaceDefinition) =>
		interfaceDefinition.Identity.ModuleName == "CopperSharp.Runtime.Managed" &&
		interfaceDefinition.DisplayName.StartsWith(
			"CopperSharp.Runtime.IShadowEqualityComparer`1<",
			StringComparison.Ordinal);

	public CilMethod ResolveConstrainedInterfaceImplementation(
		CilMethod caller,
		int constrainedTypeToken,
		int ilOffset,
		CilMethod declaration)
	{
		if (!declaration.DeclaringTypeIsInterface)
		{
			if (FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && StringBuilderDecimalSurface.IsValidationCaller(DescribeImplementationCaller(caller)) &&
				TryCreatePinnedDecimalScaleComparisonBinding(GetModule(declaration.ModuleName).DescribeFrameworkMethodDefinition(declaration.Handle, []),
					caller, ilOffset, out _, out var scaleComparison)) return scaleComparison;
			if (TryResolveExperimentalCharacterBufferToString(caller, constrainedTypeToken, ilOffset, declaration, out var builderToString))
				return builderToString;
			if (TryResolveExperimentalConstrainedPrimitiveToString(caller, constrainedTypeToken,
					ilOffset, declaration, out var integerToString))
				return integerToString;
			if (TryResolveExperimentalConstrainedEnumToString(caller, constrainedTypeToken, ilOffset, declaration, out var enumToString))
				return enumToString;
			if (TryResolveExperimentalConstrainedNullableToString(caller, constrainedTypeToken, ilOffset, declaration, out var nullableToString))
				return nullableToString;
			if (TryResolveExperimentalConstrainedStructToString(caller, constrainedTypeToken, ilOffset, declaration, out var structToString))
				return structToString;
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				"Constrained dispatch currently supports interface methods on closed value types; constrained object and non-interface virtual methods are not supported yet.",
				caller.DisplayName,
				ilOffset);
		}

		var constrainedType = ResolveTypeToken(
			constrainedTypeToken,
			caller,
			ilOffset);
		if (!TryGetStructLayout(
				constrainedType,
				caller.ModuleName,
				out var constrainedLayout) &&
			!TryGetPrimitiveFrameworkLayout(
				constrainedType,
				declaration,
				out constrainedLayout) &&
			!(
				!declaration.Signature.Header.IsInstance &&
				TryGetConstrainedMetadataLayout(
					constrainedType,
					caller.ModuleName,
					out constrainedLayout)))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				$"Constrained receiver '{constrainedType.DisplayName}' is not a compiler-supported closed value type.",
				caller.DisplayName,
				ilOffset);
		}
		if (constrainedLayout.Handle.IsNil &&
			!TryGetConstrainedMetadataLayout(
				constrainedType,
				caller.ModuleName,
				out constrainedLayout))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				$"Constrained receiver '{constrainedType.DisplayName}' has no exact managed type identity.",
				caller.DisplayName,
				ilOffset);
		}
		var interfaceDefinition = GetInterfaceDefinition(declaration);
		var implementationMethod = GetModule(constrainedLayout.ModuleName)
			.TryResolveConstrainedInterfaceMethod(
				constrainedLayout,
				interfaceDefinition,
				declaration) ??
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				$"Constrained receiver '{constrainedType.DisplayName}' has no compiler-supported implementation for '{declaration.DisplayName}'.",
				caller.DisplayName,
				ilOffset);
		var implementation = declaration.MethodTypeArguments.IsDefaultOrEmpty
			? implementationMethod
			: GetModule(implementationMethod.ModuleName).GetConstructedMethod(
				implementationMethod.Handle,
				implementationMethod.ConstructedDeclaringType,
				declaration.MethodTypeArguments);
		// Reachability already applies target overrides when it queues methods.
		// Static constrained calls must select that same body for their call label.
		return declaration.Signature.Header.IsInstance ? implementation : ApplyTargetRuntimeOverride(implementation);
	}

	public bool TryResolveConstrainedValueInterfaceImplementation(
		CilMethod caller,
		int constrainedTypeToken,
		int ilOffset,
		CilMethod declaration,
		out CilMethod implementation)
	{
		var constrainedType = ResolveTypeToken(
			constrainedTypeToken,
			caller,
			ilOffset);
		if (constrainedType.IsReference)
		{
			implementation = null!;
			return false;
		}
		implementation = ResolveConstrainedInterfaceImplementation(
			caller,
			constrainedTypeToken,
			ilOffset,
			declaration);
		return true;
	}

	private bool TryResolveExperimentalCharacterBufferToString(CilMethod caller, int constrainedTypeToken, int ilOffset,
		CilMethod declaration, out CilMethod implementation)
	{
		implementation = null!;
		var stableParser = IsPinnedCompositeParserCaller(caller) || IsPinnedFloatingBufferCaller(caller);
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !stableParser) || declaration.ModuleName != "System.Private.CoreLib" ||
			declaration.Name != "ToString" || !declaration.Signature.Header.IsInstance ||
			declaration.Signature.ParameterTypes.Length != 0 || declaration.Signature.ReturnType.DisplayName != "string") return false;
		var type = ResolveTypeToken(constrainedTypeToken, caller, ilOffset);
		if (IsPinnedFloatingBufferCaller(caller) && type.DisplayName != "System.ReadOnlySpan`1<char>") return false;
		if (stableParser && type.DisplayName is not ("System.Text.ValueStringBuilder" or "System.ReadOnlySpan`1<char>" or "System.Span`1<char>")) return false;
		var span = IsSupportedSpanLikeType(type) && type.GenericArguments is [{ Kind: CilTypeKind.Character, Size: 2 }];
		if (type.Kind != CilTypeKind.ValueType || (!span && type.DisplayName != "System.Text.ValueStringBuilder") ||
			!TryGetReferenceFreeStructLayout(type, caller.ModuleName, out var layout) || (!span && layout.ModuleName != "System.Private.CoreLib")) return false;
		var target = ResolveRuntimeTypeIdentity(type, caller.ModuleName);
		if (target.ModuleName != "System.Private.CoreLib" || target.Handle.Kind != HandleKind.TypeDefinition) return false;
		if (declaration.DisplayName != "System.Object::ToString" && declaration.DeclaringType != target.Handle) return false;
		var owner = GetModule(target.ModuleName);
		foreach (var handle in owner.Reader.GetTypeDefinition((TypeDefinitionHandle)target.Handle).GetMethods())
		{
			var method = owner.GetConstructedMethod(handle, target.IsConstructedGeneric ? type : null, ImmutableArray<CilType>.Empty);
			var attributes = owner.Reader.GetMethodDefinition(handle).Attributes;
			if (method.Name == "ToString" && method.Signature.Header.IsInstance && method.Signature.ParameterTypes.Length == 0 &&
				method.Signature.ReturnType.DisplayName == "string" && (attributes & MethodAttributes.Virtual) != 0 &&
				(attributes & MethodAttributes.NewSlot) == 0)
			{
				implementation = method;
				return true;
			}
		}
		return false;
	}

	private bool TryResolveExperimentalConstrainedStructToString(CilMethod caller, int constrainedTypeToken, int ilOffset,
		CilMethod declaration, out CilMethod implementation)
	{
		implementation = null!;
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !IsPinnedDecimalFormattingCaller(caller) && !IsPinnedPrimitiveJoinCaller(caller) && !IsPinnedNullableToStringCaller(caller) && !IsPinnedApplicationScalarFormattingCaller(caller) && !IsPinnedApplicationValueFormattingCaller(caller)) ||
			declaration.Name != "ToString" || !declaration.Signature.Header.IsInstance ||
			declaration.Signature.GenericParameterCount != 0 || declaration.Signature.ParameterTypes.Length != 0 ||
			declaration.Signature.ReturnType.DisplayName != "string") return false;
		var type = ResolveTypeToken(constrainedTypeToken, caller, ilOffset);
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && IsPinnedApplicationValueFormattingCaller(caller) &&
			!IsExperimentalApplicationJoinValue(type)) return false;
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && IsPinnedPrimitiveJoinCaller(caller) &&
			!AreSameClosedJoinType(type, caller.MethodTypeArguments[0])) return false;
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && IsPinnedNullableToStringCaller(caller) &&
			!AreSameClosedJoinType(type, caller.ConstructedDeclaringType!.NullableElementType)) return false;
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && IsPinnedApplicationScalarFormattingCaller(caller) &&
			type != caller.MethodTypeArguments[0]) return false;
		if (type.Kind != CilTypeKind.ValueType || !TryGetReferenceFreeStructLayout(type, caller.ModuleName, out var layout) || layout.Handle.IsNil ||
			Net10FrameworkContract.Default.IsFrameworkAssembly(layout.ModuleName) && !IsExperimentalDecimalFormattingLayout(layout)) return false;
		var owner = GetModule(layout.ModuleName);
		var originalSlot = declaration.ModuleName == "System.Private.CoreLib" &&
			declaration.DisplayName is "System.Object::ToString" or "System.Decimal::ToString" or "System.ValueType::ToString";
		foreach (var handle in owner.Reader.GetTypeDefinition(layout.Handle).GetMethods())
		{
			var definition = owner.Reader.GetMethodDefinition(handle);
			if (!owner.Reader.StringComparer.Equals(definition.Name, "ToString") ||
				(definition.Attributes & MethodAttributes.Virtual) == 0 || (definition.Attributes & MethodAttributes.NewSlot) != 0) continue;
			var method = owner.GetConstructedMethod(handle, layout.ConstructedType, ImmutableArray<CilType>.Empty);
			if (method.Signature.Header.IsInstance && method.Signature.GenericParameterCount == 0 &&
				method.Signature.ParameterTypes.Length == 0 && method.Signature.ReturnType.DisplayName == "string")
			{
				if (!originalSlot && (declaration.ModuleName != method.ModuleName || declaration.Handle != method.Handle ||
					!AreSameClosedJoinType(declaration.ConstructedDeclaringType, method.ConstructedDeclaringType))) return false;
				implementation = method; return true;
			}
		}
		// With no application override, constrained Object.ToString boxes the
		// value and invokes ValueType's actual managed implementation.
		if (!originalSlot || !IsExperimentalManagedStructLayout(layout)) return false;
		var declarationSlot = GetVirtualSlot(declaration);
		var table = owner.GetVirtualTable(layout.Handle, layout.ConstructedType);
		if (declarationSlot < table.Slots.Length && table.Slots[declarationSlot] is
			{ ModuleName: "System.Private.CoreLib", DisplayName: "System.ValueType::ToString", IsAbstract: false } inherited)
		{
			implementation = inherited;
			RegisterBoxedDispatchLayout(type, caller.ModuleName);
			return true;
		}
		return false;
	}

	private bool TryResolveExperimentalConstrainedPrimitiveToString(
		CilMethod caller, int constrainedTypeToken, int ilOffset,
		CilMethod declaration, out CilMethod implementation)
	{
		implementation = null!;
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !IsPinnedIntegralFormattingCaller(caller) && !IsPinnedFloatingFormattingCaller(caller) && !IsPinnedPrimitiveJoinCaller(caller) && !IsPinnedNullableToStringCaller(caller) && !IsPinnedApplicationScalarFormattingCaller(caller)) ||
			declaration.ModuleName != "System.Private.CoreLib" ||
			declaration.DisplayName is not ("System.Object::ToString" or "System.Int32::ToString" or "System.UInt32::ToString" or
				"System.Int64::ToString" or "System.UInt64::ToString" or "System.SByte::ToString" or "System.Byte::ToString" or
				"System.Int16::ToString" or "System.UInt16::ToString" or "System.Single::ToString" or "System.Double::ToString" or
				"System.Boolean::ToString" or "System.Char::ToString") ||
			!declaration.Signature.Header.IsInstance || declaration.Signature.ParameterTypes.Length != 0 ||
			declaration.Signature.ReturnType.DisplayName != "string") return false;
		var type = ResolveTypeToken(constrainedTypeToken, caller, ilOffset);
		var eligibleInteger = type.Kind is CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger &&
			type.DisplayName is ("sbyte" or "byte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong") &&
			type.Size is 1 or 2 or 4 or 8;
		var eligibleFloatingPoint = type.Kind == CilTypeKind.FloatingPoint &&
			(type.DisplayName == "float" && type.Size == 4 || type.DisplayName == "double" && type.Size == 8);
		var eligibleBooleanOrChar = type is { Kind: CilTypeKind.Boolean, Size: 1, DisplayName: "bool" } or
			{ Kind: CilTypeKind.Character, Size: 2, DisplayName: "char" };
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true &&
			(!(eligibleInteger || eligibleFloatingPoint && (IsPinnedFloatingFormattingCaller(caller) || IsPinnedPrimitiveJoinCaller(caller) || IsPinnedNullableToStringCaller(caller) || IsPinnedApplicationScalarFormattingCaller(caller)) ||
				eligibleBooleanOrChar && (IsPinnedPrimitiveJoinCaller(caller) || IsPinnedNullableToStringCaller(caller) || IsPinnedApplicationScalarFormattingCaller(caller))) ||
			 !type.Equals(IsPinnedNullableToStringCaller(caller) ? caller.ConstructedDeclaringType!.NullableElementType : caller.MethodTypeArguments[0]))) return false;
		if ((!eligibleInteger && !eligibleFloatingPoint && !eligibleBooleanOrChar) || type.IsEnum ||
			!TryGetPrimitiveFrameworkLayout(type, declaration, out var layout) ||
			(declaration.DisplayName != "System.Object::ToString" &&
			 declaration.DisplayName != $"{layout.DisplayName}::ToString")) return false;
		var module = GetModule(layout.ModuleName);
		foreach (var handle in module.Reader.GetTypeDefinition(layout.Handle).GetMethods())
		{
			if (!module.Reader.StringComparer.Equals(module.Reader.GetMethodDefinition(handle).Name, "ToString")) continue;
			var candidate = module.GetMethod(handle);
			if (candidate.Name == "ToString" && candidate.Signature.Header.IsInstance &&
				candidate.Signature.ParameterTypes.Length == 0 && candidate.Signature.ReturnType.DisplayName == "string")
			{
				implementation = candidate;
				return true;
			}
		}
		return false;
	}

	internal bool TryResolveExperimentalConstrainedStringToString(
		CilMethod caller, int constrainedTypeToken, int ilOffset,
		CilMethod declaration, out CilMethod implementation)
	{
		implementation = null!;
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !IsPinnedPrimitiveJoinCaller(caller)) ||
			declaration.ModuleName != "System.Private.CoreLib" ||
			declaration.DisplayName != "System.Object::ToString" ||
			!declaration.Signature.Header.IsInstance || declaration.Signature.ParameterTypes.Length != 0 ||
			declaration.Signature.GenericParameterCount != 0 ||
			declaration.Signature.ReturnType.DisplayName != "string") return false;
		var type = ResolveTypeToken(constrainedTypeToken, caller, ilOffset);
		if (type.Kind != CilTypeKind.ManagedReference || type.DisplayName != "string" ||
			type.ElementType is not null || !type.GenericArguments.IsDefaultOrEmpty) return false;
		var module = GetModule(declaration.ModuleName);
		foreach (var typeHandle in module.Reader.TypeDefinitions)
		{
			if (module.GetTypeName(typeHandle) != "System.String") continue;
			foreach (var methodHandle in module.Reader.GetTypeDefinition(typeHandle).GetMethods())
			{
				if (!module.Reader.StringComparer.Equals(module.Reader.GetMethodDefinition(methodHandle).Name, "ToString")) continue;
				var candidate = module.GetMethod(methodHandle);
				if (candidate.Signature.Header.IsInstance && candidate.Signature.ParameterTypes.Length == 0 &&
					candidate.Signature.GenericParameterCount == 0 && candidate.Signature.ReturnType.DisplayName == "string" && candidate.DeclaringTypeIsSealed)
				{
					implementation = candidate;
					return true;
				}
			}
		}
		return false;
	}

	private bool TryGetPrimitiveFrameworkLayout(
		CilType type,
		CilMethod declaration,
		out CilTypeLayout layout)
	{
		var metadataName = type.DisplayName switch
		{
			"bool" => "System.Boolean",
			"char" => "System.Char",
			"sbyte" => "System.SByte",
			"byte" => "System.Byte",
			"short" => "System.Int16",
			"ushort" => "System.UInt16",
			"int" => "System.Int32",
			"uint" => "System.UInt32",
			"long" => "System.Int64",
			"ulong" => "System.UInt64",
			"nint" => "System.IntPtr",
			"nuint" => "System.UIntPtr",
			"float" => "System.Single",
			"double" => "System.Double",
			_ => null
		};
		if (metadataName is null ||
			string.IsNullOrEmpty(declaration.ModuleName) ||
			!_root._modules.TryGetValue(declaration.ModuleName, out var module))
		{
			layout = null!;
			return false;
		}

		foreach (var handle in module.Reader.TypeDefinitions)
		{
			var definition = module.Reader.GetTypeDefinition(handle);
			if (string.Equals(module.GetTypeName(definition), metadataName, StringComparison.Ordinal))
			{
				layout = module.GetTypeLayout(handle);
				return true;
			}
		}

		layout = null!;
		return false;
	}

	private bool TryGetConstrainedMetadataLayout(
		CilType type,
		string preferredModuleName,
		out CilTypeLayout layout)
	{
		var target = ResolveRuntimeTypeIdentity(type, preferredModuleName);
		if (target.Handle.Kind != HandleKind.TypeDefinition)
		{
			layout = null!;
			return false;
		}

		layout = GetRuntimeTypeLayout(target);
		return true;
	}

	private CilMethod? TryResolveConstrainedInterfaceMethod(
		CilTypeLayout layout,
		CilInterfaceDefinition interfaceDefinition,
		CilMethod declaration)
	{
		if (!ImplementsInterface(layout.Handle, layout.ConstructedType, interfaceDefinition))
		{
			return null;
		}

		return TryFindExplicitInterfaceImplementation(
				layout.Handle,
				layout.ConstructedType,
				declaration) ??
			TryFindImplicitInterfaceImplementation(
				layout.Handle,
				layout.ConstructedType,
				declaration) ??
			(!declaration.IsAbstract ? declaration : null);
	}

	public EntityHandle GetBaseType(CilTypeLayout layout) =>
		GetModule(layout.ModuleName).GetBaseType(layout.Handle);

	internal CilTypeLayout? GetExperimentalCultureBase(CilTypeLayout layout) =>
		GetModule(layout.ModuleName).TryGetExperimentalCultureBase(layout.Handle);

	internal CilTypeLayout? GetExperimentalListBase(CilTypeLayout layout) =>
		GetModule(layout.ModuleName).TryGetExperimentalListBase(layout.Handle, layout.ConstructedType);

	private CilTypeLayout? TryGetExperimentalListBase(TypeDefinitionHandle handle, CilType? construction = null)
	{
		if (handle.IsNil) return null;
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) return null;
		var baseHandle = Reader.GetTypeDefinition(handle).BaseType;
		if (baseHandle.Kind != HandleKind.TypeSpecification) return null;
		var type = Reader.GetTypeSpecification((TypeSpecificationHandle)baseHandle).DecodeSignature(_signatureProvider,
			new CilGenericContext(construction?.GenericArguments ?? ImmutableArray<CilType>.Empty, ImmutableArray<CilType>.Empty));
		if (type is not { Kind: CilTypeKind.ManagedReference, GenericArguments.Length: 1 } ||
			type.DisplayName != $"System.Collections.Generic.List`1<{type.GenericArguments[0].DisplayName}>" ||
			ResolveRuntimeTypeIdentity(type, _assemblyName).ModuleName != "System.Private.CoreLib") return null;
		var element = type.GenericArguments[0];
		if (!(IsPinnedGenericJoinElement(element) || element.DisplayName is "string" or "object" or "int" or "float" or "double" ||
			element.DisplayName == "System.Decimal" && TryGetDecimalLayout(element, out _))) return null;
		var usesReleasedList = FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || element.DisplayName == "System.Decimal";
		if (!usesReleasedList && GetOrLoadModule("CopperSharp.Runtime.Managed") is null)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidInput, "Inherited join List storage requires CopperSharp.Runtime.Managed in ManagedAssemblyPaths.");
		var target = usesReleasedList
			? ResolveRuntimeTypeIdentity(type, "System.Private.CoreLib")
			: ResolveRuntimeTypeIdentity(type with { DisplayName = $"CopperSharp.Runtime.ShadowList`1<{element.DisplayName}>" }, "CopperSharp.Runtime.Managed") with { Type = type };
		var layout = GetRuntimeTypeLayout(target);
		var owner = GetModule(layout.ModuleName);
		var fields = layout.FieldOffsets.ToDictionary(field => owner.Reader.GetString(owner.Reader.GetFieldDefinition(field.Key).Name), field => field.Value);
		if (layout.Size != 20 || layout.ReferenceBitmap != 1 || fields.Count != 3 ||
			!fields.TryGetValue("_items", out var items) || items != 8 || !fields.TryGetValue("_size", out var size) || size != 12 ||
			!fields.TryGetValue("_version", out var version) || version != 16)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Inherited join List storage differs from its verified target layout.");
		return layout;
	}

	internal CilTypeLayout? GetExperimentalCharacterMemoryManagerBase(CilTypeLayout layout) =>
		GetModule(layout.ModuleName).TryGetExperimentalCharacterMemoryManagerBase(layout.Handle, layout.ConstructedType);

	private CilTypeLayout? TryGetExperimentalCharacterMemoryManagerBase(TypeDefinitionHandle handle, CilType? construction = null)
	{
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true) return null;
		var baseType = Reader.GetTypeDefinition(handle).BaseType;
		if (baseType.Kind != HandleKind.TypeSpecification) return null;
		var specification = Reader.GetTypeSpecification((TypeSpecificationHandle)baseType);
		var arguments = construction?.GenericArguments ?? ImmutableArray<CilType>.Empty;
		var frameworkType = specification.DecodeSignature(new FrameworkSignatureTypeProvider(this),
			new FrameworkGenericContext(arguments.Select((type, index) => FrameworkPrimitiveArgument(type) ?? FrameworkTypeId.GenericTypeParameter(index)).ToImmutableArray(), ImmutableArray<FrameworkTypeId>.Empty));
		var canonical = FrameworkImplementationProfile.Canonicalize(new FrameworkMemberId(frameworkType, ".base",
			new FrameworkMethodSignatureId(0, 0, 0, FrameworkTypeId.Primitive("System.Void"), []), [])).DeclaringType;
		if (!canonical.Equals(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Buffers.MemoryManager`1"), [FrameworkTypeId.Primitive("System.Char")]))) return null;
		var type = specification.DecodeSignature(_signatureProvider, new CilGenericContext(arguments, ImmutableArray<CilType>.Empty));
		var target = ResolveRuntimeTypeIdentity(type, "System.Private.CoreLib");
		return target.ModuleName == "System.Private.CoreLib" && target.Handle.Kind == HandleKind.TypeDefinition ? GetRuntimeTypeLayout(target) : null;
	}

	private CilTypeLayout? FindExperimentalCharacterMemoryManagerBase(CilTypeLayout layout)
	{
		var owner = GetModule(layout.ModuleName);
		var current = layout.Handle;
		for (var depth = 0; depth < owner.Reader.TypeDefinitions.Count && !current.IsNil; depth++)
		{
			if (owner.TryGetExperimentalCharacterMemoryManagerBase(current, layout.ConstructedType) is { } memoryBase) return memoryBase;
			var baseType = owner.Reader.GetTypeDefinition(current).BaseType;
			if (baseType.Kind != HandleKind.TypeDefinition) return null;
			current = (TypeDefinitionHandle)baseType;
		}
		return null;
	}

	internal bool IsExperimentalCharacterMemoryManagerSpanMethod(CilMethod method)
	{
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true || !method.IsVirtual || method.DeclaringTypeIsInterface ||
			method.Name != "GetSpan" || method.Signature.ParameterTypes.Length != 0 ||
			method.Signature.ReturnType.DisplayName != "System.Span`1<char>") return false;
		var layout = GetTypeLayout(method);
		if (FindExperimentalCharacterMemoryManagerBase(layout) is not { } memoryBase) return false;
		var declaration = GetVirtualTable(memoryBase).Slots.Single(candidate => candidate.Name == "GetSpan");
		return TryGetVirtualImplementation(layout, declaration)?.Identity == method.Identity;
	}

	private CilTypeLayout? TryGetExperimentalCultureBase(TypeDefinitionHandle handle)
	{
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) return null;
		var baseType = Reader.GetTypeDefinition(handle).BaseType;
		if (baseType.Kind != HandleKind.TypeReference) return null;
		var reference = Reader.GetTypeReference((TypeReferenceHandle)baseType);
		if (GetTypeName(reference) != "System.Globalization.CultureInfo" ||
			GetReferencedAssemblyName(reference.ResolutionScope) is not ("System.Runtime" or "System.Private.CoreLib")) return null;
		var target = ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, "System.Globalization.CultureInfo"), "System.Private.CoreLib");
		return target.ModuleName == "System.Private.CoreLib" && target.Handle.Kind == HandleKind.TypeDefinition ? GetRuntimeTypeLayout(target) : null;
	}

	private CilTypeLayout? FindExperimentalCultureBase(CilTypeLayout layout)
		=> GetModule(layout.ModuleName).FindExperimentalCultureBase(layout.Handle);

	private CilTypeLayout? FindExperimentalCultureBase(TypeDefinitionHandle handle)
	{
		var current = handle;
		for (var depth = 0; depth < Reader.TypeDefinitions.Count && !current.IsNil; depth++)
		{
			if (TryGetExperimentalCultureBase(current) is { } cultureBase) return cultureBase;
			var baseType = Reader.GetTypeDefinition(current).BaseType;
			if (baseType.Kind != HandleKind.TypeDefinition) return null;
			current = (TypeDefinitionHandle)baseType;
		}
		return null;
	}

	private CilInterfaceImplementation? GetInheritedCultureInterfaceImplementation(
		CilTypeLayout layout, CilInterfaceDefinition definition)
	{
		if (!IsExperimentalFormattingInterface(definition) || FindExperimentalCultureBase(layout) is not { } cultureBase)
			return null;
		var inherited = TryGetInterfaceImplementation(cultureBase, definition);
		return inherited is null ? null : new CilInterfaceImplementation(layout, definition,
			inherited.Methods.Select(method => method.IsVirtual
				? TryGetVirtualImplementation(layout, method) ?? method : method).ToImmutableArray());
	}

	public string GetTypeDisplayName(EntityHandle handle, CilTypeLayout owner) =>
		GetModule(owner.ModuleName).GetTypeDisplayName(handle);

	public string GetTypeDisplayName(EntityHandle handle, CilMethod owner) =>
		GetModule(owner.ModuleName).GetTypeDisplayName(handle);

	private CompilationModule GetModule(string moduleName)
	{
		var module = string.IsNullOrEmpty(moduleName) ||
			string.Equals(moduleName, _assemblyName, StringComparison.Ordinal)
			? this
			: _root._modules[moduleName];
		_root._reachableAssemblyNames.Add(module._assemblyName);
		return module;
	}

	private CilVirtualTable GetVirtualTable(
		TypeDefinitionHandle handle,
		CilType? constructedType = null)
	{
		var construction = constructedType?.DisplayName ?? string.Empty;
		var cacheKey = (handle, construction);
		if (_virtualTableCache.TryGetValue(cacheKey, out var cached))
		{
			return cached;
		}

		var definition = Reader.GetTypeDefinition(handle);
		var slots = ImmutableArray.CreateBuilder<CilMethod>();
		if (!definition.BaseType.IsNil &&
			definition.BaseType.Kind == HandleKind.TypeDefinition)
		{
			slots.AddRange(GetVirtualTable((TypeDefinitionHandle)definition.BaseType).Slots);
		}
		else if (!definition.BaseType.IsNil &&
			definition.BaseType.Kind == HandleKind.TypeSpecification)
		{
			if (TryGetExperimentalCharacterMemoryManagerBase(handle, constructedType) is { } memoryBase)
			{
				slots.AddRange(GetVirtualTable(memoryBase).Slots);
			}
			else if (TryGetExperimentalListBase(handle, constructedType) is { } listBase)
			{
				slots.AddRange(GetVirtualTable(listBase).Slots);
			}
			else
			{
				var baseType = Reader
					.GetTypeSpecification((TypeSpecificationHandle)definition.BaseType)
					.DecodeSignature(
						_signatureProvider,
						new CilGenericContext(
							constructedType?.GenericArguments ?? ImmutableArray<CilType>.Empty,
							ImmutableArray<CilType>.Empty));
				if (TryFindConstructedGenericDefinition(baseType, out var baseTarget) &&
					baseTarget.Handle.Kind == HandleKind.TypeDefinition)
				{
					slots.AddRange(GetVirtualTable(
						(TypeDefinitionHandle)baseTarget.Handle,
						baseType).Slots);
				}
			}
		}
		else if (TryGetExperimentalCultureBase(handle) is { } cultureBase)
		{
			slots.AddRange(GetVirtualTable(cultureBase).Slots);
		}
		else if (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true &&
			definition.BaseType.Kind == HandleKind.TypeReference && GetTypeName(Reader.GetTypeReference((TypeReferenceHandle)definition.BaseType)) == "System.Enum" &&
			GetReferencedAssemblyName(Reader.GetTypeReference((TypeReferenceHandle)definition.BaseType).ResolutionScope) is "System.Runtime" or "System.Private.CoreLib")
		{
			var corelibEnum = ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, "System.Enum"), "System.Private.CoreLib");
			slots.AddRange(GetVirtualTable(GetRuntimeTypeLayout(corelibEnum)).Slots);
		}
		else if (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true &&
			IsFrameworkObjectReference(definition.BaseType) &&
			GetOrLoadImplementationModule("System.Private.CoreLib") is { } corelib)
		{
			// The opt-in managed bodies dispatch using the actual Object slot
			// order. Application overrides must inherit that same order, rather
			// than the smaller stable-profile fallback table.
			var target = corelib.ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, "object"), corelib.AssemblyName);
			slots.AddRange(corelib.GetVirtualTable((TypeDefinitionHandle)target.Handle).Slots);
		}
		else if (TryGetExperimentalValueTypeBase(handle) is { } valueTypeBase)
		{
			slots.AddRange(GetVirtualTable(valueTypeBase).Slots);
		}
		else if (ShouldSeedFrameworkObjectSlots(definition))
		{
			slots.AddRange(_root._frameworkVirtualFallbacks.Values
				.OrderBy(static item => item.Binding.Member.DisplayName, StringComparer.Ordinal)
				.Select(static item => item.Method));
		}
		foreach (var methodHandle in definition.GetMethods())
		{
			var methodDefinition = Reader.GetMethodDefinition(methodHandle);
			if ((methodDefinition.Attributes & MethodAttributes.Virtual) == 0 ||
				(methodDefinition.Attributes & MethodAttributes.Static) != 0)
			{
				continue;
			}

			var method = GetConstructedMethod(
				methodHandle,
				constructedType,
				ImmutableArray<CilType>.Empty,
				deferBody: IsExperimentalCoreLibModule && GetTypeName(definition) == "System.RuntimeType");
			var slot = -1;
			if (!method.IsNewSlot)
			{
				for (var index = slots.Count - 1; index >= 0; index--)
				{
					if (slots[index].Name == method.Name &&
						SignaturesMatch(slots[index].Signature, method.Signature))
					{
						slot = index;
						break;
					}
				}
			}

			if (slot < 0)
			{
				slots.Add(method);
			}
			else
			{
				slots[slot] = method;
			}
		}

		var table = new CilVirtualTable(
			constructedType is null
				? GetTypeLayout(handle)
				: GetConstructedTypeLayout(handle, constructedType),
			slots.ToImmutable());
		_virtualTableCache.Add(cacheKey, table);
		return table;
	}

	private bool ShouldSeedFrameworkObjectSlots(TypeDefinition definition)
	{
		if (definition.BaseType.IsNil ||
			definition.BaseType.Kind != HandleKind.TypeReference ||
			(definition.Attributes & TypeAttributes.Interface) != 0)
		{
			return false;
		}
		var baseName = GetTypeName(
			Reader.GetTypeReference((TypeReferenceHandle)definition.BaseType));
		return baseName is not "System.ValueType" and not "System.Enum";
	}

	private CilTypeLayout? TryGetExperimentalValueTypeBase(TypeDefinitionHandle handle)
	{
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) ||
			_assemblyName is "System.Private.CoreLib" or "CopperSharp.Compiler" || Net10FrameworkContract.Default.IsFrameworkAssembly(_assemblyName)) return null;
		var definition = Reader.GetTypeDefinition(handle);
		if (definition.BaseType.Kind != HandleKind.TypeReference) return null;
		var reference = Reader.GetTypeReference((TypeReferenceHandle)definition.BaseType);
		if (GetTypeName(reference) != "System.ValueType" || reference.ResolutionScope.Kind != HandleKind.AssemblyReference ||
			GetReferencedAssemblyName(reference.ResolutionScope) is not ("System.Runtime" or "System.Private.CoreLib")) return null;
		return GetRuntimeTypeLayout(ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, "System.ValueType"), "System.Private.CoreLib"));
	}

	private IReadOnlyList<CilMethod> GetFrameworkVirtualImplementations(
		CilMethod declaration)
	{
		if (GetExperimentalCultureFallbackDeclaration(declaration) is { } cultureDeclaration)
		{
			return GetModule(cultureDeclaration.ModuleName).GetVirtualImplementationsCore(cultureDeclaration)
				.Append(declaration).DistinctBy(static method => method.Identity).ToArray();
		}
		if (_root._frameworkVirtualFallbacks.TryGetValue(
				declaration.Identity,
				out var fallback))
		{
			return [fallback.Method];
		}
		return [];
	}

	private void RegisterFrameworkVirtualFallback(
		FrameworkBinding binding,
		CilMethod method)
	{
		if (!method.Signature.Header.IsInstance ||
			!method.IsVirtual ||
			method.IsFinal)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidInput,
				$"Framework virtual fallback '{method.DisplayName}' must be an overridable instance method.");
		}
		if (!_root._frameworkVirtualFallbacks.TryAdd(
				method.Identity,
				new FrameworkVirtualFallback(binding, method)))
		{
			return;
		}
		foreach (var module in _root._modules.Values)
		{
			module._virtualTableCache.Clear();
		}
	}

	private int GetVirtualSlotCore(CilMethod method)
	{
		var table = GetVirtualTable(
			method.DeclaringType,
			method.ConstructedDeclaringType);
		for (var index = 0; index < table.Slots.Length; index++)
		{
			if (table.Slots[index].Identity == method.Identity)
			{
				return index;
			}
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.InvalidMetadata,
			$"Virtual method '{method.DisplayName}' has no vtable slot.",
			method.DisplayName);
	}

	private IReadOnlyList<CilMethod> GetVirtualImplementationsCore(CilMethod declaration)
	{
		if (IsExperimentalCoreLibModule)
		{
			// A framework module contains host-only classes and native delegate
			// shapes. Only allocated layouts can supply a target virtual receiver.
			// The analysis fixed points revisit declarations when more allocations
			// become reachable; backend effect queries use the same completed set.
			return _root._reachableDispatchLayouts.Values
				.Select(layout => TryGetVirtualImplementation(layout, declaration))
				.OfType<CilMethod>()
				.DistinctBy(static method => method.Identity)
				.ToArray();
		}
		var slot = GetVirtualSlotCore(declaration);
		var implementations = new Dictionary<CilMethodIdentity, CilMethod>();
		foreach (var typeHandle in Reader.TypeDefinitions)
		{
			var type = Reader.GetTypeDefinition(typeHandle);
			if ((type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0)
			{
				continue;
			}
			CilType? constructedType = null;
			if (type.GetGenericParameters().Count > 0)
			{
				if (!TryInferVirtualImplementerConstruction(
						typeHandle,
						declaration,
						out constructedType))
				{
					continue;
				}
			}
			else if (!IsDerivedFromDeclaration(typeHandle, declaration))
			{
				continue;
			}

			var table = GetVirtualTable(typeHandle, constructedType);
			if (slot >= table.Slots.Length || table.Slots[slot].IsAbstract)
			{
				continue;
			}
			implementations.TryAdd(table.Slots[slot].Identity, table.Slots[slot]);
		}
		return implementations.Values.ToArray();
	}

	private CilMethod? TryGetVirtualImplementationCore(
		CilTypeLayout layout,
		CilMethod declaration)
	{
		if (declaration.ModuleName == "System.Private.CoreLib" && declaration.DisplayName == "System.Object::ToString" &&
			TryGetExperimentalValueTypeBase(layout.Handle) is not null)
		{
			var valueSlot = GetVirtualSlot(declaration);
			var valueTable = GetVirtualTable(layout.Handle, layout.ConstructedType);
			if (valueSlot < valueTable.Slots.Length && valueTable.Slots[valueSlot] is { IsAbstract: false, IsNewSlot: false } implementation &&
				(implementation.ModuleName == layout.ModuleName ||
				 implementation.ModuleName == "System.Private.CoreLib" && implementation.DisplayName == "System.ValueType::ToString" &&
				 TryGetReferenceFreeStructLayout(GetRuntimeTypeSignature(layout), layout.ModuleName, out _))) return implementation;
			return null;
		}
		if (declaration.ModuleName == "System.Private.CoreLib" && FindExperimentalCharacterMemoryManagerBase(layout) is { } memoryBase &&
			(GetMethodDeclaringType(declaration).DisplayName == memoryBase.ConstructedType!.DisplayName ||
			 GetTypeDisplayName(declaration.DeclaringType, declaration) == "System.Object"))
		{
			var memorySlot = GetVirtualSlot(declaration);
			var memoryTable = GetVirtualTable(layout.Handle, layout.ConstructedType);
			return memorySlot < memoryTable.Slots.Length && !memoryTable.Slots[memorySlot].IsAbstract ? memoryTable.Slots[memorySlot] : null;
		}
		if (declaration.ModuleName == "System.Private.CoreLib" && FindExperimentalCultureBase(layout) is not null &&
			GetTypeDisplayName(declaration.DeclaringType, declaration) is "System.Object" or "System.Globalization.CultureInfo")
		{
			var cultureSlot = GetVirtualSlot(declaration);
			var cultureTable = GetVirtualTable(layout.Handle, layout.ConstructedType);
			return cultureSlot < cultureTable.Slots.Length && !cultureTable.Slots[cultureSlot].IsAbstract
				? cultureTable.Slots[cultureSlot] : null;
		}
		if (!string.Equals(declaration.ModuleName, _assemblyName, StringComparison.Ordinal) &&
			FrameworkImplementationPack?.EnableUnlistedManagedBodies == true &&
			declaration.ModuleName == "System.Private.CoreLib" &&
			GetModule(declaration.ModuleName).GetTypeName(GetModule(declaration.ModuleName).Reader.GetTypeDefinition(declaration.DeclaringType)) == "System.Object" &&
			HasLocalFrameworkObjectHierarchy(layout))
		{
			var objectSlot = GetVirtualSlot(declaration);
			var objectTable = GetVirtualTable(layout.Handle, layout.ConstructedType);
			return objectSlot < objectTable.Slots.Length && !objectTable.Slots[objectSlot].IsAbstract
				? objectTable.Slots[objectSlot] : null;
		}
		if (!string.Equals(
				declaration.ModuleName,
				_assemblyName,
				StringComparison.Ordinal) ||
			!IsDerivedFromDeclaration(
				layout.Handle,
				layout.ConstructedType,
				declaration))
		{
			return null;
		}

		var slot = GetVirtualSlotCore(declaration);
		var table = GetVirtualTable(layout.Handle, layout.ConstructedType);
		return slot < table.Slots.Length && !table.Slots[slot].IsAbstract
			? table.Slots[slot]
			: null;
	}

	private bool TryInferVirtualImplementerConstruction(
		TypeDefinitionHandle typeHandle,
		CilMethod declaration,
		out CilType? constructedType)
	{
		constructedType = null;
		if (declaration.ConstructedDeclaringType is not { } closedBase)
		{
			return false;
		}

		var type = Reader.GetTypeDefinition(typeHandle);
		var parent = type.BaseType;
		if (parent.Kind != HandleKind.TypeSpecification)
		{
			return false;
		}
		var pattern = Reader
			.GetTypeSpecification((TypeSpecificationHandle)parent)
			.DecodeSignature(_signatureProvider, CilGenericContext.Empty);
		if (!TryFindConstructedGenericDefinition(pattern, out var target) ||
			target.Handle != declaration.DeclaringType ||
			pattern.GenericArguments.Length != closedBase.GenericArguments.Length)
		{
			return false;
		}

		var arguments = new CilType?[type.GetGenericParameters().Count];
		for (var index = 0; index < pattern.GenericArguments.Length; index++)
		{
			var parameter = pattern.GenericArguments[index];
			if (parameter.Kind != CilTypeKind.GenericParameter ||
				!parameter.DisplayName.StartsWith('!') ||
				parameter.DisplayName.StartsWith("!!", StringComparison.Ordinal) ||
				!int.TryParse(parameter.DisplayName.AsSpan(1), out var parameterIndex) ||
				(uint)parameterIndex >= (uint)arguments.Length)
			{
				return false;
			}
			var argument = closedBase.GenericArguments[index];
			if (arguments[parameterIndex] is { } existing && existing != argument)
			{
				return false;
			}
			arguments[parameterIndex] = argument;
		}
		if (arguments.Any(static argument => argument is null))
		{
			return false;
		}

		var openType = _signatureProvider.GetTypeFromDefinition(
			Reader,
			typeHandle,
			0x12);
		constructedType = _signatureProvider.GetGenericInstantiation(
			openType,
			arguments.Select(static argument => argument!).ToImmutableArray());
		return true;
	}

	private bool IsDerivedFromDeclaration(
		TypeDefinitionHandle type,
		CilMethod declaration) =>
		IsDerivedFromDeclaration(type, constructedType: null, declaration);

	private bool IsDerivedFromDeclaration(
		TypeDefinitionHandle type,
		CilType? constructedType,
		CilMethod declaration)
	{
		var current = type;
		var currentConstruction = constructedType;
		var targetConstruction =
			declaration.ConstructedDeclaringType?.DisplayName ?? string.Empty;
		while (!current.IsNil)
		{
			if (current == declaration.DeclaringType &&
				StringComparer.Ordinal.Equals(
					currentConstruction?.DisplayName ?? string.Empty,
					targetConstruction))
			{
				return true;
			}
			if (!TryGetBaseDefinition(
					current,
					currentConstruction,
					out current,
					out currentConstruction))
			{
				break;
			}
		}
		return false;
	}

	private CilInterfaceDefinition GetInterfaceDefinition(
		TypeDefinitionHandle handle,
		CilType? constructedType = null)
	{
		var construction = constructedType?.DisplayName ?? string.Empty;
		var cacheKey = (handle, construction);
		if (_interfaceCache.TryGetValue(cacheKey, out var cached))
		{
			return cached;
		}

		var type = Reader.GetTypeDefinition(handle);
		if ((type.Attributes & TypeAttributes.Interface) == 0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Type '{GetTypeName(type)}' is not an interface.");
		}

		var slots = ImmutableArray.CreateBuilder<CilMethod>();
		var inherited = new HashSet<CilMethodIdentity>();
		foreach (var implementationHandle in type.GetInterfaceImplementations())
		{
			var parent = Reader.GetInterfaceImplementation(implementationHandle).Interface;
			if (!TryResolveImplementedInterfaceTarget(
					parent,
					constructedType,
					out var parentModule,
					out var parentHandle,
					out var parentConstruction))
			{
				// Interface-map discovery scans unrelated managed types too. An
				// unconfigured referenced assembly cannot contribute reachable slots;
				// direct use will still fail at the unresolved method reference.
				continue;
			}

			foreach (var method in parentModule.GetInterfaceDefinition(
				parentHandle,
				parentConstruction).Slots)
			{
				if (inherited.Add(method.Identity))
				{
					slots.Add(method);
				}
			}
		}

		foreach (var methodHandle in type.GetMethods())
		{
			var method = GetConstructedMethod(
				methodHandle,
				constructedType,
				ImmutableArray<CilType>.Empty);
			if (!method.Signature.Header.IsInstance || !inherited.Add(method.Identity))
			{
				continue;
			}
			slots.Add(method);
		}

		var definition = new CilInterfaceDefinition(
			new CilTypeIdentity(_assemblyName, handle, construction),
			constructedType?.DisplayName ?? GetTypeName(type),
			slots.ToImmutable(),
			constructedType);
		_interfaceCache.Add(cacheKey, definition);
		return definition;
	}

	private int GetInterfaceSlotCore(CilMethod method)
	{
		var interfaceDefinition = GetInterfaceDefinition(
			method.DeclaringType,
			method.ConstructedDeclaringType);
		for (var index = 0; index < interfaceDefinition.Slots.Length; index++)
		{
			var slot = interfaceDefinition.Slots[index];
			if (slot.Handle == method.Handle &&
				string.Equals(slot.ModuleName, method.ModuleName, StringComparison.Ordinal) &&
				string.Equals(
					slot.ConstructedDeclaringType?.DisplayName,
					method.ConstructedDeclaringType?.DisplayName,
					StringComparison.Ordinal))
			{
				return index;
			}
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.InvalidMetadata,
			$"Interface method '{method.DisplayName}' has no interface slot.",
			method.DisplayName);
	}

	private IReadOnlyList<CilMethod> GetInterfaceImplementationsCore(CilMethod declaration)
	{
		var interfaceDefinition = GetInterfaceDefinition(
			declaration.DeclaringType,
			declaration.ConstructedDeclaringType);
		var slot = GetInterfaceSlotCore(declaration);
		var methods = new Dictionary<CilMethodIdentity, CilMethod>();
		foreach (var typeHandle in Reader.TypeDefinitions)
		{
			var type = Reader.GetTypeDefinition(typeHandle);
			if ((type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0)
			{
				continue;
			}

			CilType? constructedType = null;
			if (type.GetGenericParameters().Count > 0 &&
				!TryInferInterfaceImplementerConstruction(
					typeHandle,
					interfaceDefinition,
					out constructedType))
			{
				continue;
			}

			var implementation = TryGetInterfaceImplementation(
				typeHandle,
				constructedType,
				interfaceDefinition);
			if (implementation is null)
			{
				continue;
			}
			methods.TryAdd(
				implementation.Methods[slot].Identity,
				implementation.Methods[slot]);
		}
		return methods.Values.ToArray();
	}

	private bool TryInferInterfaceImplementerConstruction(
		TypeDefinitionHandle typeHandle,
		CilInterfaceDefinition interfaceDefinition,
		out CilType? constructedType)
	{
		constructedType = null;
		if (interfaceDefinition.ConstructedType is not { } closedInterface)
		{
			return false;
		}

		var type = Reader.GetTypeDefinition(typeHandle);
		var parameterCount = type.GetGenericParameters().Count;
		foreach (var implementationHandle in type.GetInterfaceImplementations())
		{
			var implemented = Reader.GetInterfaceImplementation(implementationHandle).Interface;
			if (implemented.Kind != HandleKind.TypeSpecification)
			{
				continue;
			}
			var pattern = Reader
				.GetTypeSpecification((TypeSpecificationHandle)implemented)
				.DecodeSignature(_signatureProvider, CilGenericContext.Empty);
			if (!TryFindConstructedGenericDefinition(pattern, out var target) ||
				target.Handle != interfaceDefinition.Identity.Handle ||
				pattern.GenericArguments.Length != closedInterface.GenericArguments.Length)
			{
				continue;
			}

			var arguments = new CilType?[parameterCount];
			var valid = true;
			for (var index = 0; index < pattern.GenericArguments.Length; index++)
			{
				var parameter = pattern.GenericArguments[index];
				if (parameter.Kind != CilTypeKind.GenericParameter ||
					!parameter.DisplayName.StartsWith('!') ||
					parameter.DisplayName.StartsWith("!!", StringComparison.Ordinal) ||
					!int.TryParse(parameter.DisplayName.AsSpan(1), out var parameterIndex) ||
					(uint)parameterIndex >= (uint)arguments.Length)
				{
					valid = false;
					break;
				}
				var argument = closedInterface.GenericArguments[index];
				if (arguments[parameterIndex] is { } existing && existing != argument)
				{
					valid = false;
					break;
				}
				arguments[parameterIndex] = argument;
			}
			if (!valid || arguments.Any(static argument => argument is null))
			{
				continue;
			}

			var openType = _signatureProvider.GetTypeFromDefinition(
				Reader,
				typeHandle,
				0x12);
			constructedType = _signatureProvider.GetGenericInstantiation(
				openType,
				arguments.Select(static argument => argument!).ToImmutableArray());
			return true;
		}
		return false;
	}

	private CilInterfaceImplementation? TryGetInterfaceImplementation(
		TypeDefinitionHandle typeHandle,
		CilType? constructedType,
		CilInterfaceDefinition interfaceDefinition)
	{
		var identity = new CilInterfaceImplementationIdentity(
			new CilTypeIdentity(
				_assemblyName,
				typeHandle,
				constructedType?.DisplayName ?? string.Empty),
			interfaceDefinition.Identity);
		if (_interfaceImplementationCache.TryGetValue(identity, out var cached))
		{
			return cached;
		}

		if (!string.Equals(interfaceDefinition.Identity.ModuleName, _assemblyName,
			StringComparison.Ordinal) && !IsExperimentalFormattingInterface(interfaceDefinition) &&
			!IsExperimentalJoinEnumerationInterface(interfaceDefinition))
		{
			if (!ReferencesCrossModuleInterface(typeHandle, constructedType,
				interfaceDefinition))
			{
				_interfaceImplementationCache.Add(identity, null);
				return null;
			}
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				$"Cross-module interface implementation map from " +
				$"'{_assemblyName}:{GetTypeName(Reader.GetTypeDefinition(typeHandle))}' " +
				$"to '{interfaceDefinition.DisplayName}' in " +
				$"'{interfaceDefinition.Identity.ModuleName}' is not supported yet.");
		}

		CilInterfaceImplementation? inheritedBase = null;
		if (FindExperimentalCultureBase(typeHandle) is not null)
		{
			var cultureLayout = constructedType is null ? GetTypeLayout(typeHandle) : GetConstructedTypeLayout(typeHandle, constructedType);
			inheritedBase = GetInheritedCultureInterfaceImplementation(cultureLayout, interfaceDefinition);
		}
		if (inheritedBase is null && IsExperimentalJoinEnumerationInterface(interfaceDefinition) &&
			TryGetExperimentalListBase(typeHandle, constructedType) is { } listBase)
		{
			var listLayout = constructedType is null ? GetTypeLayout(typeHandle) : GetConstructedTypeLayout(typeHandle, constructedType);
			if (TryGetInterfaceImplementation(listBase, interfaceDefinition) is { } inheritedList)
				inheritedBase = new CilInterfaceImplementation(listLayout, interfaceDefinition, inheritedList.Methods);
		}
		if (!ImplementsInterface(typeHandle, constructedType, interfaceDefinition))
		{
			if (inheritedBase is not null)
			{
				_interfaceImplementationCache.Add(identity, inheritedBase);
				return inheritedBase;
			}
			if (!TryFindVariantImplementedInterface(
					typeHandle,
					constructedType,
					interfaceDefinition,
					out var sourceInterface))
			{
				_interfaceImplementationCache.Add(identity, null);
				return null;
			}

			var sourceImplementation = TryGetInterfaceImplementation(
				typeHandle,
				constructedType,
				sourceInterface) ??
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Variant source interface '{sourceInterface.DisplayName}' has no implementation on '{GetTypeName(Reader.GetTypeDefinition(typeHandle))}'.");
			if (sourceImplementation.Methods.Length != interfaceDefinition.Slots.Length)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Variant interfaces '{sourceInterface.DisplayName}' and '{interfaceDefinition.DisplayName}' do not have matching method-table shapes.");
			}

			var variantResult = new CilInterfaceImplementation(
				sourceImplementation.Type,
				interfaceDefinition,
				sourceImplementation.Methods);
			_interfaceImplementationCache.Add(identity, variantResult);
			return variantResult;
		}

		var methods = ImmutableArray.CreateBuilder<CilMethod>(interfaceDefinition.Slots.Length);
		foreach (var declaration in interfaceDefinition.Slots)
		{
			var implementation =
				TryFindExplicitInterfaceImplementation(
					typeHandle,
					constructedType,
					declaration) ??
				TryFindImplicitInterfaceImplementation(
					typeHandle,
					constructedType,
					declaration) ??
				inheritedBase?.Methods[methods.Count] ??
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedPolymorphism,
					$"Concrete type '{GetTypeName(Reader.GetTypeDefinition(typeHandle))}' has no compiler-supported implementation for '{declaration.DisplayName}'. Default interface methods are not supported.");
			methods.Add(implementation);
		}

		var result = new CilInterfaceImplementation(
			constructedType is null
				? GetTypeLayout(typeHandle)
				: GetConstructedTypeLayout(typeHandle, constructedType),
			interfaceDefinition,
			methods.MoveToImmutable());
		_interfaceImplementationCache.Add(identity, result);
		return result;
	}

	private bool ReferencesCrossModuleInterface(TypeDefinitionHandle typeHandle,
		CilType? constructedType, CilInterfaceDefinition interfaceDefinition)
	{
		var expectedName = interfaceDefinition.ConstructedType?.DisplayName ??
			interfaceDefinition.DisplayName;
		var current = typeHandle;
		var currentConstruction = constructedType;
		while (!current.IsNil)
		{
			var type = Reader.GetTypeDefinition(current);
			foreach (var implementationHandle in type.GetInterfaceImplementations())
			{
				var implemented = Reader.GetInterfaceImplementation(
					implementationHandle).Interface;
				if (CrossModuleInterfaceReferenceMatches(implemented,
					currentConstruction, interfaceDefinition.Identity.ModuleName,
					expectedName)) return true;
			}
			if (!TryGetBaseDefinition(current, currentConstruction, out current,
				out currentConstruction)) break;
		}
		return false;
	}

	private bool CrossModuleInterfaceReferenceMatches(EntityHandle implemented,
		CilType? ownerConstruction, string expectedModule, string expectedName)
	{
		if (implemented.Kind == HandleKind.TypeReference)
		{
			var reference = Reader.GetTypeReference(
				(TypeReferenceHandle)implemented);
			return string.Equals(GetReferencedAssemblyName(
					reference.ResolutionScope), expectedModule,
					StringComparison.Ordinal) &&
				string.Equals(GetTypeName(reference), expectedName,
					StringComparison.Ordinal);
		}
		if (implemented.Kind != HandleKind.TypeSpecification) return false;
		var context = new CilGenericContext(
			ownerConstruction?.GenericArguments ?? ImmutableArray<CilType>.Empty,
			ImmutableArray<CilType>.Empty);
		var specification = Reader.GetTypeSpecification(
			(TypeSpecificationHandle)implemented);
		var candidate = specification.DecodeSignature(_signatureProvider, context);
		var module = specification.DecodeSignature(
			new DeclaringAssemblyTypeProvider(this), context);
		return string.Equals(module, expectedModule, StringComparison.Ordinal) &&
			string.Equals(candidate.DisplayName, expectedName,
				StringComparison.Ordinal);
	}

	private bool TryFindVariantImplementedInterface(
		TypeDefinitionHandle typeHandle,
		CilType? constructedType,
		CilInterfaceDefinition target,
		out CilInterfaceDefinition source)
	{
		var current = typeHandle;
		var currentConstruction = constructedType;
		while (!current.IsNil)
		{
			var type = Reader.GetTypeDefinition(current);
			foreach (var implementationHandle in type.GetInterfaceImplementations())
			{
				var implemented = Reader.GetInterfaceImplementation(implementationHandle).Interface;
				if (TryResolveImplementedInterface(
						implemented,
						currentConstruction,
						out var interfaceHandle,
						out var interfaceConstruction) &&
					TryFindVariantInterface(
						interfaceHandle,
						interfaceConstruction,
						target,
						new HashSet<CilTypeIdentity>(),
						out source))
				{
					return true;
				}
			}

			if (!TryGetBaseDefinition(
					current,
					currentConstruction,
					out current,
					out currentConstruction))
			{
				break;
			}
		}

		source = null!;
		return false;
	}

	private bool TryFindVariantInterface(
		TypeDefinitionHandle interfaceHandle,
		CilType? interfaceConstruction,
		CilInterfaceDefinition target,
		HashSet<CilTypeIdentity> visited,
		out CilInterfaceDefinition source)
	{
		var identity = new CilTypeIdentity(
			_assemblyName,
			interfaceHandle,
			interfaceConstruction?.DisplayName ?? string.Empty);
		if (!visited.Add(identity))
		{
			source = null!;
			return false;
		}

		var candidate = GetInterfaceDefinition(interfaceHandle, interfaceConstruction);
		if (IsVariantInterfaceConversion(candidate, target))
		{
			source = candidate;
			return true;
		}

		var type = Reader.GetTypeDefinition(interfaceHandle);
		foreach (var implementationHandle in type.GetInterfaceImplementations())
		{
			var parent = Reader.GetInterfaceImplementation(implementationHandle).Interface;
			if (TryResolveImplementedInterface(
					parent,
					interfaceConstruction,
					out var parentHandle,
					out var parentConstruction) &&
				TryFindVariantInterface(
					parentHandle,
					parentConstruction,
					target,
					visited,
					out source))
			{
				return true;
			}
		}

		source = null!;
		return false;
	}

	private bool IsVariantInterfaceConversion(
		CilInterfaceDefinition source,
		CilInterfaceDefinition target)
	{
		if (!string.Equals(
				source.Identity.ModuleName,
				target.Identity.ModuleName,
				StringComparison.Ordinal) ||
			source.Identity.Handle != target.Identity.Handle ||
			source.ConstructedType is not { } sourceType ||
			target.ConstructedType is not { } targetType ||
			sourceType.GenericArguments.Length != targetType.GenericArguments.Length)
		{
			return false;
		}

		var parameters = Reader
			.GetTypeDefinition((TypeDefinitionHandle)source.Identity.Handle)
			.GetGenericParameters()
			.Select(Reader.GetGenericParameter)
			.OrderBy(static parameter => parameter.Index)
			.ToArray();
		if (parameters.Length != sourceType.GenericArguments.Length)
		{
			return false;
		}

		for (var index = 0; index < parameters.Length; index++)
		{
			var sourceArgument = sourceType.GenericArguments[index];
			var targetArgument = targetType.GenericArguments[index];
			var variance = parameters[index].Attributes &
				GenericParameterAttributes.VarianceMask;
			switch (variance)
			{
				case GenericParameterAttributes.None:
					if (sourceArgument != targetArgument)
					{
						return false;
					}
					break;
				case GenericParameterAttributes.Covariant:
					if (!sourceArgument.IsReference ||
						!targetArgument.IsReference ||
						!HasImplicitReferenceConversion(
							sourceArgument,
							targetArgument,
							new HashSet<(string Source, string Target)>()))
					{
						return false;
					}
					break;
				case GenericParameterAttributes.Contravariant:
					if (!sourceArgument.IsReference ||
						!targetArgument.IsReference ||
						!HasImplicitReferenceConversion(
							targetArgument,
							sourceArgument,
							new HashSet<(string Source, string Target)>()))
					{
						return false;
					}
					break;
				default:
					return false;
			}
		}
		return true;
	}

	private bool HasImplicitReferenceConversion(
		CilType source,
		CilType target,
		HashSet<(string Source, string Target)> visited)
	{
		if (source == target)
		{
			return true;
		}
		if (!source.IsReference || !target.IsReference)
		{
			return false;
		}
		if (StringComparer.Ordinal.Equals(target.DisplayName, "System.Object"))
		{
			return true;
		}
		if (!visited.Add((source.DisplayName, target.DisplayName)))
		{
			return false;
		}
		if (source.ElementType is { } sourceElement &&
			target.ElementType is { } targetElement)
		{
			return sourceElement.IsReference &&
				targetElement.IsReference &&
				HasImplicitReferenceConversion(sourceElement, targetElement, visited);
		}
		if (!TryResolveLocalTypeDefinition(
				source,
				out var sourceHandle,
				out var sourceConstruction) ||
			!TryResolveLocalTypeDefinition(
				target,
				out var targetHandle,
				out var targetConstruction))
		{
			return false;
		}

		var targetDefinition = Reader.GetTypeDefinition(targetHandle);
		if ((targetDefinition.Attributes & TypeAttributes.Interface) != 0)
		{
			var targetInterface = GetInterfaceDefinition(
				targetHandle,
				targetConstruction);
			if ((Reader.GetTypeDefinition(sourceHandle).Attributes &
					TypeAttributes.Interface) != 0)
			{
				return InterfaceExtends(
						sourceHandle,
						sourceConstruction,
						targetInterface,
						new HashSet<CilTypeIdentity>()) ||
					TryFindVariantInterface(
						sourceHandle,
						sourceConstruction,
						targetInterface,
						new HashSet<CilTypeIdentity>(),
						out _);
			}
			return ImplementsInterface(
					sourceHandle,
					sourceConstruction,
					targetInterface) ||
				TryFindVariantImplementedInterface(
					sourceHandle,
					sourceConstruction,
					targetInterface,
					out _);
		}

		var targetIdentity = new CilTypeIdentity(
			_assemblyName,
			targetHandle,
			targetConstruction?.DisplayName ?? string.Empty);
		var current = sourceHandle;
		var currentConstruction = sourceConstruction;
		while (!current.IsNil)
		{
			if (new CilTypeIdentity(
					_assemblyName,
					current,
					currentConstruction?.DisplayName ?? string.Empty) == targetIdentity)
			{
				return true;
			}
			if (!TryGetBaseDefinition(
					current,
					currentConstruction,
					out current,
					out currentConstruction))
			{
				break;
			}
		}
		return false;
	}

	private bool TryResolveLocalTypeDefinition(
		CilType type,
		out TypeDefinitionHandle handle,
		out CilType? construction)
	{
		if (!type.GenericArguments.IsDefaultOrEmpty &&
			TryFindConstructedGenericDefinition(type, out var constructed) &&
			constructed.Handle.Kind == HandleKind.TypeDefinition)
		{
			handle = (TypeDefinitionHandle)constructed.Handle;
			construction = type;
			return true;
		}
		if (TryFindRuntimeTypeDefinition(type, out var target) &&
			target.Handle.Kind == HandleKind.TypeDefinition)
		{
			handle = (TypeDefinitionHandle)target.Handle;
			construction = null;
			return true;
		}

		handle = default;
		construction = null;
		return false;
	}

	private bool ImplementsInterface(
		TypeDefinitionHandle typeHandle,
		CilType? constructedType,
		CilInterfaceDefinition interfaceDefinition)
	{
		var current = typeHandle;
		var currentConstruction = constructedType;
		while (!current.IsNil)
		{
			var type = Reader.GetTypeDefinition(current);
			foreach (var implementationHandle in type.GetInterfaceImplementations())
			{
				var implemented = Reader.GetInterfaceImplementation(implementationHandle).Interface;
				if (ImplementedInterfaceMatches(
						implemented,
						currentConstruction,
						interfaceDefinition))
				{
					return true;
				}
			}

			if (!TryGetBaseDefinition(
					current,
					currentConstruction,
					out current,
					out currentConstruction))
			{
				break;
			}
		}
		return false;
	}

	private bool TryGetBaseDefinition(
		TypeDefinitionHandle typeHandle,
		CilType? constructedType,
		out TypeDefinitionHandle baseHandle,
		out CilType? constructedBaseType)
	{
		var baseType = Reader.GetTypeDefinition(typeHandle).BaseType;
		if (baseType.Kind == HandleKind.TypeDefinition)
		{
			baseHandle = (TypeDefinitionHandle)baseType;
			constructedBaseType = null;
			return true;
		}
		if (baseType.Kind == HandleKind.TypeSpecification)
		{
			constructedBaseType = Reader
				.GetTypeSpecification((TypeSpecificationHandle)baseType)
				.DecodeSignature(
					_signatureProvider,
					new CilGenericContext(
						constructedType?.GenericArguments ?? ImmutableArray<CilType>.Empty,
						ImmutableArray<CilType>.Empty));
			if (TryFindConstructedGenericDefinition(
					constructedBaseType,
					out var target) &&
				target.Handle.Kind == HandleKind.TypeDefinition)
			{
				baseHandle = (TypeDefinitionHandle)target.Handle;
				return true;
			}
		}

		baseHandle = default;
		constructedBaseType = null;
		return false;
	}

	private bool ImplementedInterfaceMatches(
		EntityHandle implemented,
		CilType? constructedType,
		CilInterfaceDefinition interfaceDefinition)
	{
		return TryResolveImplementedInterfaceTarget(
				implemented,
				constructedType,
				out var module,
				out var interfaceHandle,
				out var interfaceConstruction) &&
			module.InterfaceExtends(
				interfaceHandle,
				interfaceConstruction,
				interfaceDefinition,
				new HashSet<CilTypeIdentity>());
	}

	private bool TryResolveImplementedInterfaceTarget(
		EntityHandle implemented,
		CilType? ownerConstruction,
		out CompilationModule module,
		out TypeDefinitionHandle interfaceHandle,
		out CilType? interfaceConstruction)
	{
		if (implemented.Kind == HandleKind.TypeDefinition)
		{
			module = this;
			interfaceHandle = (TypeDefinitionHandle)implemented;
			interfaceConstruction = null;
			return true;
		}
		if (implemented.Kind == HandleKind.TypeReference)
		{
			var reference = Reader.GetTypeReference((TypeReferenceHandle)implemented);
			var name = GetTypeName(reference);
			var assembly = GetReferencedAssemblyName(reference.ResolutionScope);
			if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && IsPinnedCallbackInterfaceName(name)) &&
				assembly is "System.Runtime" or "System.Private.CoreLib" &&
				(IsFormattingInterfaceName(name) || name is "System.Collections.IEnumerable" or "System.Collections.IEnumerator" or "System.IDisposable") &&
				GetOrLoadImplementationModule("System.Private.CoreLib") is { } corelib &&
				corelib.TryFindRuntimeTypeDefinition(new CilType(CilTypeKind.ManagedReference, 4, name), out var interfaceTarget) &&
				interfaceTarget.IsInterface && interfaceTarget.Handle.Kind == HandleKind.TypeDefinition)
			{
				module = corelib;
				interfaceHandle = (TypeDefinitionHandle)interfaceTarget.Handle;
				interfaceConstruction = null;
				return true;
			}
			var referencedModule = GetOrLoadModule(
				GetReferencedAssemblyName(reference.ResolutionScope));
			if (referencedModule is not null)
			{
				var referencedName = GetTypeName(reference).Replace('/', '+');
				foreach (var handle in referencedModule.Reader.TypeDefinitions)
				{
					if (string.Equals(
						referencedModule.GetTypeName(handle).Replace('/', '+'),
						referencedName,
						StringComparison.Ordinal))
					{
						module = referencedModule;
						interfaceHandle = handle;
						interfaceConstruction = null;
						return true;
					}
				}
			}
		}
		if (implemented.Kind == HandleKind.TypeSpecification)
		{
			interfaceConstruction = Reader
				.GetTypeSpecification((TypeSpecificationHandle)implemented)
				.DecodeSignature(
					_signatureProvider,
					new CilGenericContext(
						ownerConstruction?.GenericArguments ?? ImmutableArray<CilType>.Empty,
						ImmutableArray<CilType>.Empty));
			var target = ResolveRuntimeTypeIdentity(interfaceConstruction, _assemblyName);
			if (target.Handle.Kind == HandleKind.TypeDefinition)
			{
				module = GetModule(target.ModuleName);
				interfaceHandle = (TypeDefinitionHandle)target.Handle;
				return true;
			}
		}

		module = null!;
		interfaceHandle = default;
		interfaceConstruction = null;
		return false;
	}

	private bool IsExperimentalFormattingInterface(CilInterfaceDefinition definition) =>
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) &&
		definition.Identity.ModuleName == "System.Private.CoreLib" && IsFormattingInterfaceName(definition.DisplayName);

	private static bool IsFormattingInterfaceName(string name) => name is
		"System.IFormatProvider" or "System.ICustomFormatter" or "System.IFormattable" or "System.ISpanFormattable";

	private static bool IsPinnedJoinInterfaceName(string name) => name is
		"System.Collections.IEnumerable" or "System.Collections.IEnumerator" or "System.IDisposable" or
		"System.Collections.Generic.IEnumerable`1<string>" or "System.Collections.Generic.IEnumerator`1<string>" or
		"System.Collections.Generic.IEnumerable`1<int>" or "System.Collections.Generic.IEnumerator`1<int>" or
		"System.Collections.Generic.IEnumerable`1<object>" or "System.Collections.Generic.IEnumerator`1<object>" or
		"System.Collections.Generic.IEnumerable`1<System.Decimal>" or "System.Collections.Generic.IEnumerator`1<System.Decimal>" or
		"System.Collections.Generic.IEnumerable`1<float>" or "System.Collections.Generic.IEnumerator`1<float>" or
		"System.Collections.Generic.IEnumerable`1<double>" or "System.Collections.Generic.IEnumerator`1<double>" or
		"System.Collections.Generic.IEnumerable`1<bool>" or "System.Collections.Generic.IEnumerator`1<bool>" or
		"System.Collections.Generic.IEnumerable`1<char>" or "System.Collections.Generic.IEnumerator`1<char>" or
		"System.Collections.Generic.IEnumerable`1<System.ValueTuple`4<string,int,int,string>>" or "System.Collections.Generic.IEnumerator`1<System.ValueTuple`4<string,int,int,string>>";

	private static bool IsPinnedCallbackInterfaceName(string name) => IsFormattingInterfaceName(name) || IsPinnedJoinInterfaceName(name);

	private bool IsExperimentalJoinEnumerationInterface(CilInterfaceDefinition definition) =>
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true ||
		 FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && (IsPinnedJoinInterfaceName(definition.DisplayName) || IsPinnedGenericJoinInterface(definition.ConstructedType))) &&
		definition.Identity.ModuleName == "System.Private.CoreLib" &&
		// Generic enumeration tables include the inherited nongeneric and
		// IDisposable slots. Admit these identities with the admitted join
		// interfaces, without opening arbitrary cross-module interface maps.
		(definition.DisplayName is "System.Collections.IEnumerable" or "System.Collections.IEnumerator" or "System.IDisposable" ||
		 definition.ConstructedType is { GenericArguments: [var element] } &&
		 (element is { Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" } or
			{ Kind: CilTypeKind.FloatingPoint, Size: 4, DisplayName: "float" } or
			{ Kind: CilTypeKind.FloatingPoint, Size: 8, DisplayName: "double" } or
			{ Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "string" or "object" } ||
		  element is { Kind: CilTypeKind.ValueType, DisplayName: "System.Decimal" } && TryGetDecimalLayout(element, out _) ||
		  IsCompositeFormatSegment(element) && TryGetReferenceFreeStructLayout(element, "System.Private.CoreLib", out _) ||
		  IsExperimentalGenericJoinElement(element)) &&
		 (definition.DisplayName.StartsWith("System.Collections.Generic.IEnumerable`1<", StringComparison.Ordinal) ||
		  definition.DisplayName.StartsWith("System.Collections.Generic.IEnumerator`1<", StringComparison.Ordinal)));

	private bool TryResolveImplementedInterface(
		EntityHandle implemented,
		CilType? ownerConstruction,
		out TypeDefinitionHandle interfaceHandle,
		out CilType? interfaceConstruction)
	{
		if (implemented.Kind == HandleKind.TypeDefinition)
		{
			interfaceHandle = (TypeDefinitionHandle)implemented;
			interfaceConstruction = null;
			return true;
		}
		if (implemented.Kind == HandleKind.TypeSpecification)
		{
			interfaceConstruction = Reader
				.GetTypeSpecification((TypeSpecificationHandle)implemented)
				.DecodeSignature(
					_signatureProvider,
					new CilGenericContext(
						ownerConstruction?.GenericArguments ?? ImmutableArray<CilType>.Empty,
						ImmutableArray<CilType>.Empty));
			if (TryFindConstructedGenericDefinition(
					interfaceConstruction,
					out var target) &&
				target.Handle.Kind == HandleKind.TypeDefinition)
			{
				interfaceHandle = (TypeDefinitionHandle)target.Handle;
				return true;
			}
		}

		interfaceHandle = default;
		interfaceConstruction = null;
		return false;
	}

	private bool InterfaceExtends(
		TypeDefinitionHandle interfaceHandle,
		CilType? interfaceConstruction,
		CilInterfaceDefinition target,
		HashSet<CilTypeIdentity> visited)
	{
		var identity = new CilTypeIdentity(
			_assemblyName,
			interfaceHandle,
			interfaceConstruction?.DisplayName ?? string.Empty);
		if (identity == target.Identity)
		{
			return true;
		}
		if (!visited.Add(identity))
		{
			return false;
		}

		var type = Reader.GetTypeDefinition(interfaceHandle);
		foreach (var implementationHandle in type.GetInterfaceImplementations())
		{
			var parent = Reader.GetInterfaceImplementation(implementationHandle).Interface;
			if (TryResolveImplementedInterfaceTarget(
					parent,
					interfaceConstruction,
					out var parentModule,
					out var parentHandle,
					out var parentConstruction) &&
				parentModule.InterfaceExtends(
					parentHandle,
					parentConstruction,
					target,
					visited))
			{
				return true;
			}
		}
		return false;
	}

	private CilMethod? TryFindExplicitInterfaceImplementation(
		TypeDefinitionHandle typeHandle,
		CilType? constructedType,
		CilMethod declaration)
	{
		var current = typeHandle;
		var currentConstruction = constructedType;
		while (!current.IsNil)
		{
			var type = Reader.GetTypeDefinition(current);
			foreach (var implementationHandle in type.GetMethodImplementations())
			{
				var implementation = Reader.GetMethodImplementation(implementationHandle);
				if (!ExplicitInterfaceDeclarationMatches(
						implementation.MethodDeclaration,
						currentConstruction,
						declaration) ||
					implementation.MethodBody.Kind != HandleKind.MethodDefinition)
				{
					continue;
				}
				return GetConstructedMethod(
					(MethodDefinitionHandle)implementation.MethodBody,
					currentConstruction,
					declaration.MethodTypeArguments.IsDefault
						? ImmutableArray<CilType>.Empty
						: declaration.MethodTypeArguments);
			}

			if (!TryGetBaseDefinition(
					current,
					currentConstruction,
					out current,
					out currentConstruction))
			{
				break;
			}
		}
		return null;
	}

	private bool ExplicitInterfaceDeclarationMatches(
		EntityHandle handle,
		CilType? implementerConstruction,
		CilMethod declaration)
	{
		if (handle.Kind == HandleKind.MethodDefinition)
		{
			var candidate = GetMethod((MethodDefinitionHandle)handle);
			return candidate.DeclaringType == declaration.DeclaringType &&
				candidate.Handle == declaration.Handle;
		}
		if (handle.Kind != HandleKind.MemberReference)
		{
			return false;
		}

		var member = Reader.GetMemberReference((MemberReferenceHandle)handle);
		if (declaration.ModuleName != _assemblyName)
		{
			if (!TryResolveImplementedInterfaceTarget(member.Parent, implementerConstruction,
					out var targetModule, out var targetHandle, out var targetConstruction) ||
				targetModule._assemblyName != declaration.ModuleName || targetHandle != declaration.DeclaringType ||
				(targetConstruction?.DisplayName ?? string.Empty) != (declaration.ConstructedDeclaringType?.DisplayName ?? string.Empty))
				return false;
			var targetSignature = member.DecodeMethodSignature(_signatureProvider, new CilGenericContext(
				targetConstruction?.GenericArguments ?? ImmutableArray<CilType>.Empty,
				declaration.MethodTypeArguments.IsDefault ? ImmutableArray<CilType>.Empty : declaration.MethodTypeArguments));
			return declaration.Name == Reader.GetString(member.Name) && SignaturesMatch(declaration.Signature, targetSignature);
		}
		TypeDefinitionHandle interfaceHandle;
		CilType? interfaceConstruction = null;
		if (member.Parent.Kind == HandleKind.TypeDefinition)
		{
			interfaceHandle = (TypeDefinitionHandle)member.Parent;
		}
		else if (member.Parent.Kind == HandleKind.TypeSpecification)
		{
			interfaceConstruction = Reader
				.GetTypeSpecification((TypeSpecificationHandle)member.Parent)
				.DecodeSignature(
					_signatureProvider,
					new CilGenericContext(
						implementerConstruction?.GenericArguments ?? ImmutableArray<CilType>.Empty,
						ImmutableArray<CilType>.Empty));
			if (!TryFindConstructedGenericDefinition(
					interfaceConstruction,
					out var interfaceTarget) ||
				interfaceTarget.Handle.Kind != HandleKind.TypeDefinition)
			{
				return false;
			}
			interfaceHandle = (TypeDefinitionHandle)interfaceTarget.Handle;
		}
		else
		{
			return false;
		}
		if (interfaceHandle != declaration.DeclaringType ||
			!StringComparer.Ordinal.Equals(
				interfaceConstruction?.DisplayName ?? string.Empty,
				declaration.ConstructedDeclaringType?.DisplayName ?? string.Empty))
		{
			return false;
		}

		var name = Reader.GetString(member.Name);
		var signature = member.DecodeMethodSignature(
			_signatureProvider,
			new CilGenericContext(
				interfaceConstruction?.GenericArguments ?? ImmutableArray<CilType>.Empty,
				declaration.MethodTypeArguments.IsDefault
					? ImmutableArray<CilType>.Empty
					: declaration.MethodTypeArguments));
		return declaration.Name == name &&
			SignaturesMatch(declaration.Signature, signature);
	}

	private CilMethod? TryFindImplicitInterfaceImplementation(
		TypeDefinitionHandle typeHandle,
		CilType? constructedType,
		CilMethod declaration)
	{
		var current = typeHandle;
		var currentConstruction = constructedType;
		while (!current.IsNil)
		{
			var type = Reader.GetTypeDefinition(current);
			foreach (var methodHandle in type.GetMethods())
			{
				var definition = Reader.GetMethodDefinition(methodHandle);
				if ((definition.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
				{
					continue;
				}

				var candidate = GetConstructedMethod(
					methodHandle,
					currentConstruction,
					declaration.MethodTypeArguments.IsDefault
						? ImmutableArray<CilType>.Empty
						: declaration.MethodTypeArguments);
				if (!candidate.IsAbstract &&
					candidate.Name == declaration.Name &&
					SignaturesMatch(candidate.Signature, declaration.Signature))
				{
					return candidate;
				}
			}

			if (!TryGetBaseDefinition(
					current,
					currentConstruction,
					out current,
					out currentConstruction))
			{
				break;
			}
		}
		return null;
	}

	private bool TryGetFixedBufferSize(CilType type, out int size)
	{
		size = 0;
		if (type.Kind != CilTypeKind.ValueType ||
			!type.DisplayName.Contains("e__FixedBuffer", StringComparison.Ordinal))
		{
			return false;
		}

		foreach (var handle in Reader.TypeDefinitions)
		{
			var definition = Reader.GetTypeDefinition(handle);
			if (!string.Equals(
					GetTypeName(handle).Replace('+', '/'),
					type.DisplayName.Replace('+', '/'),
					StringComparison.Ordinal))
			{
				continue;
			}
			size = definition.GetLayout().Size;
			if (size > 0)
			{
				return true;
			}
			break;
		}

		foreach (var path in Directory.EnumerateFiles(_assemblyDirectory, "*.dll"))
		{
			try
			{
				var reflectionName = type.DisplayName.Replace('/', '+');
				var reflectionType = Assembly.LoadFrom(path).GetType(reflectionName, throwOnError: false);
				if (reflectionType is null)
				{
					continue;
				}

				size = Marshal.SizeOf(reflectionType);
				return size > 0;
			}
			catch (BadImageFormatException)
			{
			}
			catch (FileNotFoundException)
			{
			}
			catch (FileLoadException)
			{
			}
		}

		return false;
	}

	public bool IsTransparentScalarType(CilType type)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && type.Kind == CilTypeKind.ValueType &&
			type.DisplayName == "System.RuntimeTypeHandle")
		{
			var identity = ResolveRuntimeTypeIdentity(type, _assemblyName);
			if (identity.ModuleName == "System.Private.CoreLib" && identity.Handle.Kind == HandleKind.TypeDefinition) return true;
		}
		// Target handles are opaque immortal addresses: a type object or an
		// immediate RVA field consumed by the initialized-span intrinsic.
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true &&
			type.Kind == CilTypeKind.ValueType)
		{
			if (type.DisplayName == "System.RuntimeTypeHandle") return true;
			if (type.DisplayName == "System.RuntimeFieldHandle")
			{
				var identity = ResolveRuntimeTypeIdentity(type, _assemblyName);
				if (identity.ModuleName == "System.Private.CoreLib" && identity.Handle.Kind == HandleKind.TypeDefinition)
					return true;
			}
		}
		if (type.Kind != CilTypeKind.ValueType)
		{
			return false;
		}

		if (_transparentScalarTypeCache.TryGetValue(type.DisplayName, out var cached))
		{
			return cached;
		}

		// Reject recursive value-layout probes while classifying nested
		// single-word wrappers (for example a record containing APTR).
		_transparentScalarTypeCache[type.DisplayName] = false;
		var result = IsTransparentScalarType(type.DisplayName);
		_transparentScalarTypeCache[type.DisplayName] = result;
		return result;
	}

	public bool IsSupportedNullableType(CilType type) =>
		type.NullableElementType is { } element &&
		(element.IsSupportedScalar && element.Size == 4 ||
		 IsExperimentalSmallNullableElement(element) || IsExperimentalWideNullableElement(element) || IsExperimentalFloatingNullableElement(element) || IsExperimentalDecimalNullableElement(element) || IsExperimentalApplicationNullableElement(element) ||
		 IsTransparentScalarType(element));

	public bool IsValueTypeConstructor(CilMethod method) =>
		method.Name == ".ctor" && IsValueTypeMethod(method);

	public bool IsValueTypeMethod(CilMethod method)
	{
		if (!string.IsNullOrEmpty(method.ModuleName) &&
			!string.Equals(method.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(method, 0).IsValueTypeMethod(method);
		}

		return !method.DeclaringType.IsNil &&
			IsValueTypeDefinition(Reader.GetTypeDefinition(method.DeclaringType));
	}

	public CilType GetMethodDeclaringType(CilMethod method)
	{
		if (!string.IsNullOrEmpty(method.ModuleName) &&
			!string.Equals(method.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(method, 0).GetMethodDeclaringType(method);
		}
		if (method.ConstructedDeclaringType is { } construction) return construction;
		if (method.DeclaringType.IsNil)
			throw new InvalidOperationException($"Method '{method.DisplayName}' has no declaring type.");
		return _signatureProvider.GetTypeFromDefinition(
			Reader,
			method.DeclaringType,
			IsValueTypeDefinition(Reader.GetTypeDefinition(method.DeclaringType)) ? (byte)0x11 : (byte)0x12);
	}

	public bool IsSupportedStructType(CilType type)
	{
		if (type.Kind == CilTypeKind.ValueType && type.DisplayName == "System.Decimal")
			return (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && TryGetDecimalLayout(type, out _);
		if (IsCharacterValueListBuilder(type) || IsNumberBuffer(type))
			return (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && TryGetReferenceFreeStructLayout(type, _assemblyName, out _);
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && type.Kind == CilTypeKind.ValueType &&
			type.DisplayName == "System.ValueTuple`2<uint,uint>" && !type.GenericArguments.IsDefault &&
			type.GenericArguments is [{ Kind: CilTypeKind.UnsignedInteger, Size: 4 }, { Kind: CilTypeKind.UnsignedInteger, Size: 4 }])
			return TryGetReferenceFreeStructLayout(type, "System.Private.CoreLib", out var tuple) &&
				tuple.ModuleName == "System.Private.CoreLib" && tuple.Size == 8 && tuple.ReferenceBitmap == 0 &&
				tuple.FieldOffsets.Count == 2 && tuple.FieldOffsets.Values.Order().SequenceEqual([0, 4]);
		if (IsExperimentalApplicationJoinValue(type)) return true;
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true && type.Kind == CilTypeKind.ValueType &&
			!type.GenericArguments.IsDefaultOrEmpty && TryFindConstructedGenericDefinition(type, out var applicationType) &&
			!Net10FrameworkContract.Default.IsFrameworkAssembly(applicationType.ModuleName))
			return TryGetReferenceFreeStructLayout(type, _assemblyName, out _);
		if (IsCompositeFormatSegment(type) ||
			type.Kind == CilTypeKind.ValueType && type.DisplayName is "System.Text.ValueStringBuilder" or "CopperSharp.Runtime.ShadowValueStringBuilder")
			return TryGetReferenceFreeStructLayout(type, _assemblyName, out _);
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true && type.Kind == CilTypeKind.ValueType &&
			type.DisplayName.StartsWith("System.Runtime.CompilerServices.InlineArray", StringComparison.Ordinal))
			return TryGetReferenceFreeStructLayout(type, _assemblyName, out _);
		if (IsSupportedSpanLikeType(type) ||
			IsSupportedMemoryLikeType(type) ||
			IsDefaultInterpolatedStringHandler(type) ||
			IsIntegerValueListBuilder(type) ||
			IsListEnumeratorType(type))
		{
			return true;
		}
		if (type.IsSupportedScalar ||
			IsTransparentScalarType(type))
		{
			return false;
		}

		foreach (var module in _root._modules.Values)
		{
			if (module.HasSupportedStructDefinition(type))
			{
				return true;
			}
		}

		return TryGetReflectionStructSlotLongs(type.DisplayName, out _);
	}

	private bool HasSupportedStructDefinition(CilType type)
	{
		foreach (var handle in Reader.TypeDefinitions)
		{
			var definition = Reader.GetTypeDefinition(handle);
			if (!TypeNameMatches(definition, type.DisplayName))
			{
				continue;
			}

			var fields = definition.GetFields()
				.Select(GetField)
				.Where(static field => !field.IsStatic)
				.ToArray();
			return fields.Length != 0 || FrameworkImplementationPack?.EnableUnlistedManagedBodies == true &&
				!Net10FrameworkContract.Default.IsFrameworkAssembly(_assemblyName) &&
				IsExperimentalManagedStructLayout(GetTypeLayout(handle));
		}

		return false;
	}

	public static bool IsSupportedSpanLikeType(CilType type) =>
		type.Kind == CilTypeKind.ValueType &&
		(type.DisplayName.StartsWith("System.Span`1<", StringComparison.Ordinal) ||
		 type.DisplayName.StartsWith(
			"System.ReadOnlySpan`1<",
			StringComparison.Ordinal)) &&
		type.GenericArguments.Length == 1;

	public static bool IsSupportedMemoryLikeType(CilType type) =>
		type.Kind == CilTypeKind.ValueType &&
		(type.DisplayName.StartsWith("System.Memory`1<", StringComparison.Ordinal) ||
		 type.DisplayName.StartsWith(
			"System.ReadOnlyMemory`1<",
			StringComparison.Ordinal)) &&
		type.GenericArguments.Length == 1;

	public static bool IsDefaultInterpolatedStringHandler(CilType type) =>
		type.Kind == CilTypeKind.ValueType &&
		type.DisplayName ==
			"System.Runtime.CompilerServices.DefaultInterpolatedStringHandler";

	public static bool IsListEnumeratorType(CilType type) =>
		type.Kind == CilTypeKind.ValueType &&
		!type.GenericArguments.IsDefault &&
		type.GenericArguments.Length == 1 &&
		(type.DisplayName.StartsWith(
			"System.Collections.Generic.List`1/Enumerator<",
			StringComparison.Ordinal) ||
		 type.DisplayName.StartsWith(
			"CopperSharp.Runtime.ShadowListEnumerator`1<",
			StringComparison.Ordinal));

	public bool TryGetReferenceFreeStructLayout(
		CilType type,
		string preferredModuleName,
		out CilTypeLayout layout)
		{
			layout = null!;
			if (type.Kind == CilTypeKind.ValueType && type.DisplayName == "System.Decimal")
				return (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && TryGetDecimalLayout(type, out layout);
			if (type.Kind == CilTypeKind.ValueType && !type.GenericArguments.IsDefault && type.GenericArguments is [{ DisplayName: "object", Kind: CilTypeKind.ManagedReference }] &&
				type.DisplayName.StartsWith("System.Runtime.CompilerServices.InlineArray", StringComparison.Ordinal) &&
				FrameworkImplementationPack?.EnableUnlistedManagedBodies == true)
				return TryGetStructLayout(type, preferredModuleName, out layout) && layout.ModuleName == "System.Private.CoreLib" &&
					layout.Size is >= 4 and <= 128 && layout.ReferenceBitmap == (layout.Size == 128 ? uint.MaxValue : (1u << (layout.Size / 4)) - 1);
			if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && type.Kind == CilTypeKind.ValueType &&
				type.DisplayName is "System.TwoObjects" or "System.ThreeObjects")
				return TryGetStructLayout(type, preferredModuleName, out layout) && layout.ModuleName == "System.Private.CoreLib" &&
					(type.DisplayName == "System.TwoObjects" ? layout.Size == 8 && layout.ReferenceBitmap == 3 : layout.Size == 12 && layout.ReferenceBitmap == 7);
			if (IsIntegerValueListBuilder(type))
				return TryGetStructLayout(type, preferredModuleName, out layout);
			if (IsCharacterValueListBuilder(type) || IsNumberBuffer(type))
				return (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && TryGetNumericBufferLayout(type, out layout);
			if (IsCompositeFormatSegment(type))
				return (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) &&
					TryGetStructLayout(type, preferredModuleName, out layout) && layout.ModuleName == "System.Private.CoreLib" &&
					layout.Size == 16 && layout.ReferenceBitmap == 9 && layout.FieldOffsets.Count == 4 &&
					layout.FieldOffsets.Values.Order().SequenceEqual([0, 4, 8, 12]);
			if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && type.Kind == CilTypeKind.ValueType &&
				type.DisplayName is "System.Text.ValueStringBuilder" or "CopperSharp.Runtime.ShadowValueStringBuilder")
				return TryGetStructLayout(type, preferredModuleName, out layout) &&
					layout.ModuleName == (type.DisplayName == "System.Text.ValueStringBuilder" ? "System.Private.CoreLib" : "CopperSharp.Runtime.Managed") &&
					layout.Size == 20 && layout.ReferenceBitmap == 9 && layout.FieldOffsets.Count == 3 &&
					layout.FieldOffsets.Values.Order().SequenceEqual([0, 4, 16]);
			if (type.Kind == CilTypeKind.ValueType &&
				type.DisplayName.Replace('/', '+') == "System.Text.StringBuilder+AppendInterpolatedStringHandler" &&
				type.GenericArguments.IsDefaultOrEmpty &&
				FrameworkImplementationPack?.SupportsStringBuilderProtocolAbi == true)
			{
				// Both the captured builder and provider must stay rooted when a
				// handler is returned by value or copied across collecting calls.
				return TryGetStructLayout(type, preferredModuleName, out layout) &&
					layout.ModuleName == "System.Private.CoreLib" && layout.Size == 12 && layout.ReferenceBitmap == 3 &&
					layout.FieldOffsets.Count == 3 && layout.FieldOffsets.Values.Order().SequenceEqual([0, 4, 8]);
			}
			if (type.Kind == CilTypeKind.ValueType &&
				type.DisplayName.Replace('/', '+') == "System.Text.StringBuilder+ChunkEnumerator" &&
				type.GenericArguments.IsDefaultOrEmpty &&
				FrameworkImplementationPack?.SupportsStringBuilderProtocolAbi == true)
			{
				// The pinned enumerator contains three object references. Admit its
				// existing aggregate ABI only with the verified CoreLib layout so
				// locals and hidden return buffers retain all three precise roots.
				return TryGetStructLayout(type, preferredModuleName, out layout) &&
					layout.ModuleName == "System.Private.CoreLib" &&
					layout.Size == 12 && layout.ReferenceBitmap == 7 &&
					layout.FieldOffsets.Count == 3 && layout.FieldOffsets.Values.Order().SequenceEqual([0, 4, 8]);
			}
			if (IsListEnumeratorType(type))
			{
				// Direct List<T> enumeration is a deliberately admitted
				// reference-bearing aggregate transport. Its hidden return buffer
				// is a precisely described frame root and the shadow/public layouts
				// are identical, so no general reference-bearing copy surface is
				// opened here.
				return TryGetListEnumeratorLayout(
					type,
					preferredModuleName,
					out layout);
			}
			if (IsSupportedSpanLikeType(type))
			{
				// The language-visible payload is the allocation-free (data, length)
				// pair. A private third word retains the owning array for precise GC
				// rooting; it is copied with the value but never exposed as Span state.
				layout = new CilTypeLayout(
					default,
					type.DisplayName,
					12,
					1u << 2,
					new Dictionary<FieldDefinitionHandle, int>(),
					preferredModuleName,
					type);
				return true;
			}
			if (IsSupportedMemoryLikeType(type))
			{
				// The admitted array-backed value is (owner, start, length). Word
				// zero is a precise GC root; no data pointer is retained across a move.
				layout = new CilTypeLayout(
					default,
					type.DisplayName,
					12,
					1u,
					new Dictionary<FieldDefinitionHandle, int>(),
					preferredModuleName,
					type);
				return true;
			}
			if (IsExperimentalNullableJoinValue(type)) return TryGetStructLayout(type, preferredModuleName, out layout);
			if (IsSupportedNullableType(type))
		{
			layout = new CilTypeLayout(
				default,
				type.DisplayName,
				type.NullableElementType is { } element &&
					IsTransparentScalarType(element)
						? 4
						: type.Size,
				0,
				new Dictionary<FieldDefinitionHandle, int>(),
				preferredModuleName);
			return true;
		}
			if (!TryGetStructLayout(type, preferredModuleName, out layout))
			{
				return false;
			}
			return layout.ReferenceBitmap == 0 || IsExperimentalManagedStructLayout(layout);
		}

	private bool IsExperimentalManagedStructLayout(CilTypeLayout layout)
	{
		// CoreLib is supplied as an implementation assembly rather than a public
		// contract assembly. Its layouts still require their exact stable gates.
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && layout.ModuleName == "System.Private.CoreLib") return false;
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) ||
			Net10FrameworkContract.Default.IsFrameworkAssembly(layout.ModuleName) ||
			layout.Size is <= 0 or > 128 || (layout.Size & 3) != 0 || layout.Handle.IsNil) return false;
		var owner = GetModule(layout.ModuleName);
		var definition = owner.Reader.GetTypeDefinition(layout.Handle);
		return owner.IsValueTypeDefinition(definition) && !definition.GetCustomAttributes().Any(attribute =>
			owner.GetAttributeTypeName(attribute) == "System.Runtime.CompilerServices.IsByRefLikeAttribute");
	}

			public bool TryGetStructLayout(
				CilType type,
				string preferredModuleName,
				out CilTypeLayout layout)
			{
				layout = null!;
				if (IsListEnumeratorType(type))
				{
					return TryGetListEnumeratorLayout(
						type,
						preferredModuleName,
						out layout);
				}
				if (IsSupportedMemoryLikeType(type))
				{
					layout = new CilTypeLayout(
						default,
						type.DisplayName,
						12,
						1u,
						new Dictionary<FieldDefinitionHandle, int>(),
						preferredModuleName,
						type);
					return true;
				}
				if (IsDefaultInterpolatedStringHandler(type))
				{
					// Pinned .NET 10 field order: provider, pooled array, Span<char>,
					// position, custom-formatter flag. The private target shadow uses
					// the same 7-word shape and reference slots.
					layout = new CilTypeLayout(
						default,
						type.DisplayName,
						28,
						(1u << 0) | (1u << 1) | (1u << 4),
						new Dictionary<FieldDefinitionHandle, int>(),
						preferredModuleName,
						type);
					return true;
				}
				if (type.Kind != CilTypeKind.ValueType || type.IsSupportedScalar)
			{
				return false;
			}

			var target = ResolveRuntimeTypeIdentity(type, preferredModuleName);
		if (target.Handle.IsNil || target.Handle.Kind != HandleKind.TypeDefinition)
		{
			return false;
		}

		layout = GetRuntimeTypeLayout(target);
			return layout.Size > 0;
		}

	public bool TryGetIndirectInitializeLayout(
		CilType type,
		string preferredModuleName,
		out CilTypeLayout layout) =>
		IsSupportedSpanLikeType(type) ||
		IsSupportedMemoryLikeType(type) ||
		IsSupportedNullableType(type)
			? TryGetReferenceFreeStructLayout(
				type,
				preferredModuleName,
				out layout)
			: TryGetStructLayout(type, preferredModuleName, out layout);

	private bool TryGetReflectionStructSlotLongs(string displayName, out int slotLongs)
	{
		slotLongs = 0;
		foreach (var path in Directory.EnumerateFiles(_assemblyDirectory, "*.dll"))
		{
			try
			{
				var type = Assembly.LoadFrom(path).GetType(displayName.Replace('/', '+'), throwOnError: false);
				if (type is null ||
					!type.IsValueType)
				{
					continue;
				}

				var fields = type
					.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
					.Where(static field => !field.IsStatic)
					.ToArray();
				if (fields.Length == 0 ||
					fields.Any(field =>
						!IsReflectionStructField(field.FieldType) &&
						!IsReflectionFixedBufferField(field.FieldType)))
				{
					continue;
				}

				var size = ReflectionStructSize(type);
				if (size == 0)
				{
					continue;
				}

				slotLongs = checked((size + 3) / 4);
				return true;
			}
			catch (BadImageFormatException)
			{
			}
			catch (FileNotFoundException)
			{
			}
			catch (FileLoadException)
			{
			}
		}

		return false;
	}

	public int GetStructSlotLongs(CilType type)
	{
		if (IsSupportedSpanLikeType(type))
		{
			return 3;
		}
		if (IsSupportedMemoryLikeType(type))
		{
			return 3;
		}
		if (IsDefaultInterpolatedStringHandler(type))
		{
			return 7;
		}
		if (IsListEnumeratorType(type) &&
			TryGetListEnumeratorLayout(type, _assemblyName, out var enumeratorLayout))
		{
			return checked((enumeratorLayout.Size + 3) / 4);
		}
		if (TryGetStructLayout(type, _assemblyName, out var layout))
		{
			return checked((layout.Size + 3) / 4);
		}

		if (TryGetReflectionStructSlotLongs(type.DisplayName, out var slotLongs))
		{
			return slotLongs;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedSignature,
			$"Unsupported value type '{type.DisplayName}'.");
	}

	private bool TryGetListEnumeratorLayout(
		CilType type,
		string preferredModuleName,
		out CilTypeLayout layout)
	{
		layout = null!;
		if (!IsListEnumeratorType(type) ||
			type.GenericArguments is not [var element])
		{
			return false;
		}

		int currentBytes;
		var currentReferences = 0u;
		if (element.IsSupportedScalar && element.Kind is not (CilTypeKind.ManagedPointer or CilTypeKind.GenericParameter))
			currentBytes = Math.Max(4, element.Size);
		else if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) &&
			element is { Kind: CilTypeKind.ValueType, DisplayName: "System.Decimal" } && TryGetDecimalLayout(element, out var decimalLayout))
			currentBytes = decimalLayout.Size;
		else if (IsExperimentalApplicationJoinValue(element) && TryGetStructLayout(element, preferredModuleName, out var applicationLayout))
		{
			currentBytes = applicationLayout.Size;
			currentReferences = applicationLayout.ReferenceBitmap;
		}
		else if (IsExperimentalNullableJoinValue(element) && TryGetStructLayout(element, preferredModuleName, out var nullableLayout))
		{
			currentBytes = nullableLayout.Size;
			currentReferences = nullableLayout.ReferenceBitmap;
		}
		else if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && IsCompositeFormatSegment(element) &&
			TryGetReferenceFreeStructLayout(element, "System.Private.CoreLib", out var segmentLayout) &&
			segmentLayout.ModuleName == "System.Private.CoreLib" && segmentLayout.Size == 16 && segmentLayout.ReferenceBitmap == 9)
		{
			currentBytes = segmentLayout.Size;
			currentReferences = segmentLayout.ReferenceBitmap;
		}
		else
			return false;
		var referenceBitmap = 1u | (currentReferences << 3);
		if (element.IsReference)
		{
			referenceBitmap |= 1u << 3;
		}
		layout = new CilTypeLayout(
			default,
			type.DisplayName,
			12 + currentBytes,
			referenceBitmap,
			new Dictionary<FieldDefinitionHandle, int>(),
			preferredModuleName,
			type);
		return true;
	}

	internal static bool IsCompositeFormatSegment(CilType type) =>
		type.Kind == CilTypeKind.ValueType && type.DisplayName == "System.ValueTuple`4<string,int,int,string>" &&
		type.GenericArguments is [{ Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "string" },
			{ Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" }, { Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" },
			{ Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "string" }];

	public static bool IsIntegerValueListBuilder(CilType type) =>
		type.Kind == CilTypeKind.ValueType &&
		!type.GenericArguments.IsDefault &&
		type.GenericArguments is [{ Kind: CilTypeKind.SignedInteger, Size: 4 }] &&
		(type.DisplayName.StartsWith("System.Collections.Generic.ValueListBuilder`1<", StringComparison.Ordinal) ||
		 type.DisplayName.StartsWith("CopperSharp.Runtime.ShadowValueListBuilder`1<", StringComparison.Ordinal));

	public static bool IsCharacterValueListBuilder(CilType type) =>
		type.Kind == CilTypeKind.ValueType && !type.GenericArguments.IsDefault && type.GenericArguments is [{ Kind: CilTypeKind.Character, Size: 2 }] &&
		(type.DisplayName == "System.Collections.Generic.ValueListBuilder`1<char>" ||
		 type.DisplayName == "CopperSharp.Runtime.ShadowValueListBuilder`1<char>");

	public static bool IsNumberBuffer(CilType type) => type.Kind == CilTypeKind.ValueType &&
		type.DisplayName is "System.Number/NumberBuffer" or "CopperSharp.Runtime.ShadowNumberBuffer";

	internal bool TryGetExperimentalFloatingFormattingType(CilTypeLayout layout, out CilType type)
	{
		type = null!;
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) || layout.ModuleName != "System.Private.CoreLib" ||
			layout.Handle.IsNil || layout.ReferenceBitmap != 0 || layout.FieldOffsets.Count != 1) return false;
		var size = layout.DisplayName switch { "System.Single" => 4, "System.Double" => 8, _ => 0 };
		if (size == 0 || layout.Size != size) return false;
		var owner = GetModule(layout.ModuleName);
		if (owner.GetTypeName(layout.Handle) != layout.DisplayName) return false;
		var fields = owner.Reader.GetTypeDefinition(layout.Handle).GetFields().Select(owner.GetField).Where(field => !field.IsStatic).ToArray();
		var name = size == 4 ? "float" : "double";
		if (fields.Length != 1 || fields[0].Type.Kind != CilTypeKind.FloatingPoint || fields[0].Type.Size != size ||
			fields[0].Type.DisplayName != name || !layout.FieldOffsets.TryGetValue(fields[0].Handle, out var offset) || offset != 0) return false;
		type = fields[0].Type;
		return true;
	}

	internal bool IsExperimentalDecimalFormattingLayout(CilTypeLayout layout) =>
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && layout.ModuleName == "System.Private.CoreLib" &&
		layout.DisplayName == "System.Decimal" && TryGetDecimalLayout(new CilType(CilTypeKind.ValueType, 0, "System.Decimal"), out var verified) &&
		layout.Identity == verified.Identity && layout.Size == verified.Size && layout.ReferenceBitmap == verified.ReferenceBitmap &&
		layout.FieldOffsets.Count == verified.FieldOffsets.Count && verified.FieldOffsets.All(field => layout.FieldOffsets.TryGetValue(field.Key, out var offset) && offset == field.Value);

	private bool TryGetDecimalLayout(CilType type, out CilTypeLayout layout)
	{
		layout = null!;
		if (!type.GenericArguments.IsDefaultOrEmpty || GetOrLoadImplementationModule("System.Private.CoreLib") is not { } owner ||
			!TryGetStructLayout(type, "System.Private.CoreLib", out layout) || layout.ModuleName != "System.Private.CoreLib" || layout.Handle.IsNil) return false;
		var fields = owner.Reader.GetTypeDefinition(layout.Handle).GetFields().Select(owner.GetField).Where(field => !field.IsStatic).ToArray();
		var names = new[] { "_flags", "_hi32", "_lo64" };
		var types = new[] { "int", "uint", "ulong" };
		var offsets = new[] { 0, 4, 8 };
		var verified = layout;
		return layout.Size == 16 && layout.ReferenceBitmap == 0 && fields.Length == 3 &&
			!fields.Where((field, index) => owner.Reader.GetString(owner.Reader.GetFieldDefinition(field.Handle).Name) != names[index] ||
				field.Type.DisplayName != types[index] || verified.FieldOffsets[field.Handle] != offsets[index]).Any();
	}

	private bool TryGetNumericBufferLayout(CilType type, out CilTypeLayout layout)
	{
		var shadow = type.DisplayName.StartsWith("CopperSharp.Runtime.", StringComparison.Ordinal);
		var expectedModule = shadow ? "CopperSharp.Runtime.Managed" : "System.Private.CoreLib";
		layout = null!;
		if ((shadow ? GetOrLoadModule(expectedModule) : GetOrLoadImplementationModule(expectedModule)) is null ||
			!TryGetStructLayout(type, expectedModule, out layout) || layout.ModuleName != expectedModule || layout.Handle.IsNil) return false;
		var owner = GetModule(layout.ModuleName);
		var definition = owner.Reader.GetTypeDefinition(layout.Handle);
		if (!owner.IsValueTypeDefinition(definition) || !owner.HasAttribute(definition.GetCustomAttributes(), "System.Runtime.CompilerServices.IsByRefLikeAttribute")) return false;
		var fields = definition.GetFields().Select(owner.GetField).Where(field => !field.IsStatic).ToArray();
		var list = IsCharacterValueListBuilder(type);
		var names = list ? new[] { "_span", "_arrayFromPool", "_pos" } : new[] { "DigitsCount", "Scale", "IsNegative", "HasNonZeroTail", "Kind", "Digits" };
		var types = list ? new[] { "System.Span`1<char>", "char[]", "int" } : new[] { "int", "int", "bool", "bool", shadow ? "CopperSharp.Runtime.ShadowNumberBufferKind" : "System.Number/NumberBufferKind", "System.Span`1<byte>" };
		var offsets = list ? new[] { 0, 12, 16 } : new[] { 0, 4, 8, 12, 16, 20 };
		var verified = layout;
		return layout.Size == (list ? 20 : 32) && layout.ReferenceBitmap == (list ? 12u : 128u) && fields.Length == names.Length &&
			!fields.Where((field, index) => owner.Reader.GetString(owner.Reader.GetFieldDefinition(field.Handle).Name) != names[index] ||
				(type.GenericArguments.IsDefaultOrEmpty ? field.Type : SubstituteTypeArguments(field.Type, type.GenericArguments)).DisplayName != types[index] ||
				verified.FieldOffsets[field.Handle] != offsets[index]).Any();
	}

	public bool IsNumericBufferSpanField(CilField field)
	{
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) || field.IsStatic ||
			!IsSupportedSpanLikeType(field.Type) || field.ModuleName is not ("System.Private.CoreLib" or "CopperSharp.Runtime.Managed")) return false;
		var owner = GetModule(field.ModuleName);
		var type = field.ConstructedDeclaringType ?? new CilType(CilTypeKind.ValueType, 0, owner.GetTypeName(field.DeclaringType));
		return (IsNumberBuffer(type) || IsCharacterValueListBuilder(type)) && TryGetReferenceFreeStructLayout(type, field.ModuleName, out _);
	}

	public bool IsTransparentScalarConstructor(CilMethod method) =>
		GetModule(method.ModuleName).IsTransparentScalarConstructorCore(method);

	private bool IsTransparentScalarConstructorCore(CilMethod method) =>
		method.Signature.Header.IsInstance &&
		method.Name == ".ctor" &&
		method.Signature.ParameterTypes.Length == 1 &&
		IsTransparentScalarType(new CilType(
			CilTypeKind.ValueType,
			4,
			Reader.GetTypeDefinition(method.DeclaringType) is { } type
				? GetTypeName(type)
				: method.DisplayName.Split("::", StringSplitOptions.None)[0]));

	public bool IsTransparentScalarField(CilField field)
	{
		if (!IsTransparentScalarPayload(field.Type))
			return false;
		if (!field.DeclaringType.IsNil && field.ConstructedDeclaringType is null)
		{
			// Nested types may share a short display name. The resolved field
			// already carries its exact owner; do not rebind it by that name.
			var module = GetModule(field.ModuleName);
			return module.IsTransparentScalarType(
				module.Reader.GetTypeDefinition(field.DeclaringType));
		}
		return IsTransparentScalarType(field.ConstructedDeclaringType ?? new CilType(
			CilTypeKind.ValueType, 4,
			field.DisplayName.Split("::", StringSplitOptions.None)[0]));
	}

	private bool IsTransparentScalarPayload(CilType type) =>
		(type.IsSupportedScalar && type.Size == 4 && !type.IsReference) || IsTransparentScalarType(type);

	private bool IsTransparentScalarType(string displayName)
	{
		foreach (var handle in Reader.TypeDefinitions)
		{
			var definition = Reader.GetTypeDefinition(handle);
			if (TypeNameMatches(definition, displayName))
			{
				return IsTransparentScalarType(definition);
			}
		}

		foreach (var module in _root._modules.Values)
		{
			if (ReferenceEquals(module, this)) continue;
			foreach (var handle in module.Reader.TypeDefinitions)
			{
				var definition = module.Reader.GetTypeDefinition(handle);
				if (module.TypeNameMatches(definition, displayName))
					return module.IsTransparentScalarType(definition);
			}
		}

		foreach (var path in Directory.EnumerateFiles(_assemblyDirectory, "*.dll"))
		{
			try
			{
				var type = Assembly.LoadFrom(path).GetType(displayName.Replace('/', '+'), throwOnError: false);
				if (type is not null)
				{
					var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					return type.IsValueType &&
						fields.Length == 1 &&
						IsReflectionTransparentScalarField(fields[0].FieldType);
				}
			}
			catch (BadImageFormatException)
			{
			}
			catch (FileNotFoundException)
			{
			}
			catch (FileLoadException)
			{
			}
		}

		return false;
	}

	private bool IsTransparentScalarType(TypeDefinition definition)
	{
		if (definition.BaseType.Kind != HandleKind.TypeReference ||
			GetTypeName(Reader.GetTypeReference((TypeReferenceHandle)definition.BaseType)) != "System.ValueType")
		{
			return false;
		}

		var fields = definition.GetFields()
			.Select(GetField)
			.Where(static field => !field.IsStatic)
			.ToArray();
		return fields.Length == 1 &&
			IsTransparentScalarPayload(fields[0].Type);
	}

	private bool TypeNameMatches(TypeDefinition definition, string displayName)
	{
		var typeName = GetTypeName(definition);
		return typeName == displayName ||
			typeName.EndsWith($".{displayName}", StringComparison.Ordinal) ||
			typeName.EndsWith($"/{displayName}", StringComparison.Ordinal) ||
			displayName.EndsWith($"/{typeName}", StringComparison.Ordinal) ||
			displayName.EndsWith($"+{typeName}", StringComparison.Ordinal) ||
			Reader.GetString(definition.Name) == displayName;
	}

	private bool IsValueTypeDefinition(TypeDefinition definition) =>
		!definition.BaseType.IsNil && definition.BaseType.Kind switch
		{
			HandleKind.TypeReference => GetTypeName(Reader.GetTypeReference(
				(TypeReferenceHandle)definition.BaseType)) is "System.ValueType" or "System.Enum",
			HandleKind.TypeDefinition => GetTypeName(Reader.GetTypeDefinition(
				(TypeDefinitionHandle)definition.BaseType)) is "System.ValueType" or "System.Enum",
			_ => false
		};

	private static bool IsReflectionTransparentScalarField(Type type) =>
		type == typeof(bool) ||
		type == typeof(char) ||
		type == typeof(sbyte) ||
		type == typeof(byte) ||
		type == typeof(short) ||
		type == typeof(ushort) ||
		type == typeof(int) ||
		type == typeof(uint) ||
		type == typeof(IntPtr) ||
		type == typeof(UIntPtr);

	private static bool IsReflectionScalarField(Type type) =>
		type == typeof(bool) ||
		type == typeof(char) ||
		type == typeof(sbyte) ||
		type == typeof(byte) ||
		type == typeof(short) ||
		type == typeof(ushort) ||
		type == typeof(int) ||
		type == typeof(uint) ||
		type == typeof(IntPtr) ||
		type == typeof(UIntPtr) ||
		type.FullName is "Amiga.APTR" or "Amiga.BPTR" or "Amiga.STRPTR" or "Amiga.CONST_STRPTR" or "Amiga.CString";

	private static bool IsReflectionStructField(Type type)
	{
		if (IsReflectionScalarField(type))
		{
			return true;
		}

		if (!type.IsValueType || type.Namespace != "Amiga")
		{
			return false;
		}

		var fields = type
			.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.Where(static field => !field.IsStatic)
			.ToArray();
		return fields.Length != 0 &&
			fields.All(field =>
				IsReflectionStructField(field.FieldType) ||
				IsReflectionFixedBufferField(field.FieldType));
	}

	private static bool IsReflectionFixedBufferField(Type type) =>
		type.IsValueType &&
		type.GetField("FixedElementField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;

	private static int ReflectionFieldSize(Type type)
	{
		if (IsReflectionFixedBufferField(type))
		{
			return Marshal.SizeOf(type);
		}

		if (type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte))
		{
			return 1;
		}

		if (type == typeof(char) || type == typeof(short) || type == typeof(ushort))
		{
			return 2;
		}

		if (IsReflectionScalarField(type))
		{
			return 4;
		}

		return IsReflectionStructField(type) ? ReflectionStructSize(type) : 0;
	}

	private static int ReflectionStructSize(Type type)
	{
		var size = 0;
		var fields = type
			.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.Where(static field => !field.IsStatic)
			.ToArray();
		foreach (var field in fields)
		{
			var fieldSize = ReflectionFieldSize(field.FieldType);
			if (fieldSize == 0)
			{
				return 0;
			}

			size = Align(size, fieldSize >= 2 ? 2 : 1);
			size += fieldSize;
		}

		return Align(size, 2);
	}

	private static int Align(int value, int alignment) =>
		(value + alignment - 1) / alignment * alignment;

	public string GetUserString(int token, CilMethod caller, int ilOffset)
	{
		if (!string.IsNullOrEmpty(caller.ModuleName) &&
			!string.Equals(caller.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(caller, ilOffset).GetUserString(token, caller, ilOffset);
		}

		var handle = MetadataTokens.Handle(token);
		if (handle.Kind != HandleKind.UserString)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Token 0x{token:X8} is not a user string.");
		}

		return Reader.GetUserString((UserStringHandle)handle);
	}

	public CilType ResolveTypeToken(int token, CilMethod caller, int ilOffset)
	{
		if (!string.IsNullOrEmpty(caller.ModuleName) &&
			!string.Equals(caller.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(caller, ilOffset).ResolveTypeToken(token, caller, ilOffset);
		}

		var handle = MetadataTokens.EntityHandle(token);
		return handle.Kind switch
		{
			HandleKind.TypeDefinition => _signatureProvider.GetTypeFromDefinition(
				Reader,
				(TypeDefinitionHandle)handle,
				IsValueTypeDefinition(Reader.GetTypeDefinition((TypeDefinitionHandle)handle))
					? (byte)0x11
					: (byte)0x12),
			HandleKind.TypeReference => ResolveReferencedType((TypeReferenceHandle)handle),
			HandleKind.TypeSpecification => _signatureProvider.GetTypeFromSpecification(
				Reader,
				caller.GenericContext,
				(TypeSpecificationHandle)handle,
				0x12),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Token 0x{token:X8} is not a type reference.",
				caller.DisplayName,
				ilOffset)
		};
	}

	public string ResolveTypeTokenModuleName(
		int token,
		CilMethod caller,
		int ilOffset)
	{
		if (!string.IsNullOrEmpty(caller.ModuleName) &&
			!string.Equals(caller.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(caller, ilOffset)
				.ResolveTypeTokenModuleName(token, caller, ilOffset);
		}

		var handle = MetadataTokens.EntityHandle(token);
		return handle.Kind switch
		{
			HandleKind.TypeDefinition => _assemblyName,
			HandleKind.TypeReference => GetReferencedAssemblyName(
				Reader.GetTypeReference((TypeReferenceHandle)handle).ResolutionScope),
			HandleKind.TypeSpecification => Reader
				.GetTypeSpecification((TypeSpecificationHandle)handle)
				.DecodeSignature(
					new DeclaringAssemblyTypeProvider(this),
					caller.GenericContext) ?? caller.ModuleName,
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Token 0x{token:X8} is not a type reference.",
				caller.DisplayName,
				ilOffset)
		};
	}

	private bool CanResolveImplementationRuntimeType(CilType type) =>
		FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true &&
		(IsPinnedCallbackInterfaceName(type.DisplayName) || IsPinnedGenericJoinInterface(type) || IsExperimentalNullableJoinValue(type) || IsCompositeFormatSegment(type) || type.DisplayName == "System.Globalization.NumberFormatInfo" ||
		 type is { Kind: CilTypeKind.ValueType, IsEnum: false, DisplayName: "System.Decimal" } && TryGetDecimalLayout(type, out _));

	public CilRuntimeTypeTarget ResolveRuntimeTypeToken(
		int token,
		CilMethod caller,
		int ilOffset)
	{
		if (!string.IsNullOrEmpty(caller.ModuleName) &&
			!string.Equals(caller.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(caller, ilOffset)
				.ResolveRuntimeTypeToken(token, caller, ilOffset);
		}

		var handle = MetadataTokens.EntityHandle(token);
		var type = ResolveTypeToken(token, caller, ilOffset);
		if (handle.Kind == HandleKind.TypeSpecification && (IsExperimentalSmallNullableElement(type) || IsExperimentalWideNullableElement(type) || IsExperimentalFloatingNullableElement(type)))
		{
			// Fully specialized !0/!!0 type tests must name the concrete scalar
			// or enum descriptor rather than treating it as a constructed type.
			var identity = ResolveRuntimeTypeIdentity(type, caller.ModuleName);
			if (!identity.Handle.IsNil && identity.Handle.Kind == HandleKind.TypeDefinition) return identity;
		}
		if (CanResolveImplementationRuntimeType(type) &&
			handle.Kind == HandleKind.TypeReference)
		{
			var reference = Reader.GetTypeReference((TypeReferenceHandle)handle);
			var assembly = GetReferencedAssemblyName(reference.ResolutionScope);
			if (assembly is not null && Net10FrameworkContract.Default.IsFrameworkAssembly(assembly))
			{
				var identity = ResolveRuntimeTypeIdentity(type, assembly);
				// A TypeRef does not encode the interface bit. Resolve it through
				// the verified implementation before selecting runtime type tests.
				if (identity.ModuleName == "System.Private.CoreLib" && identity.Handle.Kind == HandleKind.TypeDefinition &&
					(identity.IsInterface || type is { Kind: CilTypeKind.ValueType, IsEnum: false, DisplayName: "System.Decimal" } && TryGetDecimalLayout(type, out _)))
					return identity;
			}
		}
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true ||
			FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && caller.DisplayName is
			"CopperSharp.Runtime.ShadowStringJoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowInt32JoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowObjectJoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowDecimalJoinEnumeration::GetEnumerator" or
			"CopperSharp.Runtime.ShadowSingleJoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowDoubleJoinEnumeration::GetEnumerator" ||
			caller.MethodTypeArguments is [var stableJoinElement] && IsPinnedGenericJoinElement(stableJoinElement) && IsExperimentalGenericJoinFactory(caller, stableJoinElement)) &&
			caller.ModuleName == "CopperSharp.Runtime.Managed" &&
			(caller.DisplayName is "CopperSharp.Runtime.ShadowStringJoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowObjectJoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowInt32JoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowDecimalJoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowSingleJoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowDoubleJoinEnumeration::GetEnumerator" ||
			 caller.MethodTypeArguments is [var genericJoinElement] && IsExperimentalGenericJoinFactory(caller, genericJoinElement)) &&
			!type.GenericArguments.IsDefault &&
			type is { Kind: CilTypeKind.ManagedReference, ElementType: null, GenericArguments.Length: 1 } &&
			((caller.DisplayName == "CopperSharp.Runtime.ShadowStringJoinEnumeration::GetEnumerator" && type.GenericArguments[0].DisplayName == "string") ||
			 (caller.DisplayName == "CopperSharp.Runtime.ShadowObjectJoinEnumeration::GetEnumerator" && type.GenericArguments[0].DisplayName == "object") ||
			 (caller.DisplayName == "CopperSharp.Runtime.ShadowInt32JoinEnumeration::GetEnumerator" && type.GenericArguments[0] is { Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" }) ||
			 (caller.DisplayName == "CopperSharp.Runtime.ShadowDecimalJoinEnumeration::GetEnumerator" && type.GenericArguments[0] is { Kind: CilTypeKind.ValueType, DisplayName: "System.Decimal" } decimalElement && TryGetDecimalLayout(decimalElement, out _)) ||
			 (caller.DisplayName == "CopperSharp.Runtime.ShadowSingleJoinEnumeration::GetEnumerator" && type.GenericArguments[0] is { Kind: CilTypeKind.FloatingPoint, Size: 4, DisplayName: "float" }) ||
			 (caller.DisplayName == "CopperSharp.Runtime.ShadowDoubleJoinEnumeration::GetEnumerator" && type.GenericArguments[0] is { Kind: CilTypeKind.FloatingPoint, Size: 8, DisplayName: "double" }) ||
			 IsExperimentalGenericJoinFactory(caller, type.GenericArguments[0])) &&
			(type.DisplayName == $"System.Collections.Generic.List`1<{type.GenericArguments[0].DisplayName}>" ||
			 type.DisplayName == $"CopperSharp.Runtime.ShadowList`1<{type.GenericArguments[0].DisplayName}>"))
		{
			// Stable scalar lists are allocated by ShadowList constructors. Decimal
			// and experimental lists use CoreLib constructors. Both bridge casts
			// must name the descriptor selected by the corresponding constructor.
			var publicList = type with { DisplayName = $"System.Collections.Generic.List`1<{type.GenericArguments[0].DisplayName}>" };
			var usesShadowConstructor = FrameworkImplementationPack?.EnableUnlistedManagedBodies != true &&
				caller.DisplayName is "CopperSharp.Runtime.ShadowStringJoinEnumeration::GetEnumerator" or
				"CopperSharp.Runtime.ShadowInt32JoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowObjectJoinEnumeration::GetEnumerator" or
				"CopperSharp.Runtime.ShadowSingleJoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowDoubleJoinEnumeration::GetEnumerator" ||
				FrameworkImplementationPack?.EnableUnlistedManagedBodies != true &&
				IsPinnedGenericJoinElement(type.GenericArguments[0]) && IsExperimentalGenericJoinFactory(caller, type.GenericArguments[0]);
			var expectedModule = usesShadowConstructor ? "CopperSharp.Runtime.Managed" : "System.Private.CoreLib";
			var identity = usesShadowConstructor
				? ResolveRuntimeTypeIdentity(type with { DisplayName = $"CopperSharp.Runtime.ShadowList`1<{type.GenericArguments[0].DisplayName}>" }, expectedModule) with { Type = publicList }
				: ResolveRuntimeTypeIdentity(publicList, expectedModule);
			var layout = GetRuntimeTypeLayout(identity);
			var owner = GetModule(layout.ModuleName);
			var offsets = layout.FieldOffsets.ToDictionary(pair => owner.Reader.GetString(owner.Reader.GetFieldDefinition(pair.Key).Name), pair => pair.Value);
			if (layout.ModuleName != expectedModule || layout.Size != 20 || layout.ReferenceBitmap != 1 || offsets.Count != 3 ||
				!offsets.TryGetValue("_items", out var items) || items != 8 ||
				!offsets.TryGetValue("_size", out var size) || size != 12 ||
				!offsets.TryGetValue("_version", out var version) || version != 16)
				throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature,
					$"Join enumeration requires the verified {expectedModule} List<{type.GenericArguments[0].DisplayName}> layout.", caller.DisplayName, ilOffset);
			return identity;
		}
		return handle.Kind switch
		{
			HandleKind.TypeDefinition => new CilRuntimeTypeTarget(
				type,
				_assemblyName,
				handle,
				(Reader.GetTypeDefinition((TypeDefinitionHandle)handle).Attributes &
					TypeAttributes.Interface) != 0,
				IsArray: false),
			HandleKind.TypeReference => new CilRuntimeTypeTarget(
				type,
				GetReferencedAssemblyName(
					Reader.GetTypeReference((TypeReferenceHandle)handle).ResolutionScope) ??
					string.Empty,
				handle,
				IsInterface: false,
				IsArray: false),
			HandleKind.TypeSpecification when type.ElementType is not null =>
				new CilRuntimeTypeTarget(
					type,
					_assemblyName,
					handle,
					IsInterface: false,
					IsArray: type.Kind == CilTypeKind.ManagedReference &&
						type.DisplayName.EndsWith("]", StringComparison.Ordinal)),
			HandleKind.TypeSpecification when IsFrameworkDelegateType(type) =>
				new CilRuntimeTypeTarget(
					type,
					"System.Private.CoreLib",
					handle,
					IsInterface: false,
					IsArray: false),
			HandleKind.TypeSpecification when !type.GenericArguments.IsDefaultOrEmpty &&
				TryFindConstructedGenericDefinition(type, out var constructedTarget) =>
				constructedTarget,
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Runtime type identity for '{type.DisplayName}' is not implemented.",
				caller.DisplayName,
				ilOffset)
		};
	}

	private static bool IsFrameworkDelegateType(CilType type) =>
		type.DisplayName.StartsWith("System.Func`", StringComparison.Ordinal) ||
		type.DisplayName.StartsWith("System.Action`", StringComparison.Ordinal) ||
		StringComparer.Ordinal.Equals(type.DisplayName, "System.Action") ||
		StringComparer.Ordinal.Equals(type.DisplayName, "System.Delegate") ||
		StringComparer.Ordinal.Equals(type.DisplayName, "System.MulticastDelegate");

	private bool TryFindConstructedGenericDefinition(
		CilType constructedType,
		out CilRuntimeTypeTarget target)
	{
		var separator = constructedType.DisplayName.IndexOf('<');
		var definitionName = separator < 0
			? constructedType.DisplayName
			: constructedType.DisplayName[..separator];
		if (constructedType.IsNullable) definitionName = "System.Nullable`1";
		foreach (var handle in Reader.TypeDefinitions)
		{
			var definition = Reader.GetTypeDefinition(handle);
			var signatureName = _signatureProvider
				.GetTypeFromDefinition(Reader, handle, 0x12)
				.DisplayName;
			if (!StringComparer.Ordinal.Equals(signatureName, definitionName))
			{
				continue;
			}
			target = new CilRuntimeTypeTarget(
				constructedType,
				_assemblyName,
				handle,
				(definition.Attributes & TypeAttributes.Interface) != 0,
				IsArray: false,
				IsConstructedGeneric: true);
			return true;
		}
		target = null!;
		return false;
	}

	public CilTypeLayout GetRuntimeTypeLayout(CilRuntimeTypeTarget target)
	{
		if (target.Handle.Kind != HandleKind.TypeDefinition)
		{
			throw new InvalidOperationException(
				$"Runtime type '{target.Type.DisplayName}' is not a type definition.");
		}
		var module = GetModule(target.ModuleName);
		return target.IsConstructedGeneric
			? module.GetConstructedTypeLayout(
				(TypeDefinitionHandle)target.Handle,
				target.Type)
			: module.GetTypeLayout((TypeDefinitionHandle)target.Handle);
	}

	private CilTypeLayout GetConstructedTypeLayout(
		TypeDefinitionHandle handle,
		CilType constructedType)
	{
		var key = (handle, constructedType.DisplayName);
		if (_constructedLayoutCache.TryGetValue(key, out var cached))
		{
			return cached;
		}
		if (TryGetExperimentalNullableJoinLayout(handle, constructedType, out var nullableLayout))
		{
			_constructedLayoutCache.Add(key, nullableLayout);
			return nullableLayout;
		}

		var definition = Reader.GetTypeDefinition(handle);
		var isValueType = IsValueTypeDefinition(definition);
		var inheritedSize = isValueType ? 0 : 8;
		var inheritedBitmap = 0u;
		var fieldOffsets = new Dictionary<FieldDefinitionHandle, int>();
		if (!isValueType && TryGetExperimentalListBase(handle, constructedType) is { } listBase)
		{
			inheritedSize = listBase.Size;
			inheritedBitmap = listBase.ReferenceBitmap;
		}
		else if (!isValueType && definition.BaseType.Kind == HandleKind.TypeDefinition)
		{
			var baseLayout = GetTypeLayout((TypeDefinitionHandle)definition.BaseType);
			inheritedSize = baseLayout.Size;
			inheritedBitmap = baseLayout.ReferenceBitmap;
			foreach (var item in baseLayout.FieldOffsets)
			{
				fieldOffsets.Add(item.Key, item.Value);
			}
		}
		else if (definition.BaseType.Kind == HandleKind.TypeSpecification)
		{
			var baseType = Reader
				.GetTypeSpecification((TypeSpecificationHandle)definition.BaseType)
				.DecodeSignature(
					_signatureProvider,
					new CilGenericContext(
						constructedType.GenericArguments,
						ImmutableArray<CilType>.Empty));
			if ((!TryFindConstructedGenericDefinition(baseType, out var baseTarget) ||
				baseTarget.Handle.Kind != HandleKind.TypeDefinition) &&
				!IsShadowEqualityComparerFrameworkBase(constructedType, baseType))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					$"Constructed base layout '{baseType.DisplayName}' for '{constructedType.DisplayName}' cannot be resolved to an in-module generic definition.");
			}
			if (baseTarget is not null &&
				baseTarget.Handle.Kind == HandleKind.TypeDefinition)
			{
				var baseLayout = GetConstructedTypeLayout(
					(TypeDefinitionHandle)baseTarget.Handle,
					baseType);
				inheritedSize = baseLayout.Size;
				inheritedBitmap = baseLayout.ReferenceBitmap;
				foreach (var item in baseLayout.FieldOffsets)
				{
					fieldOffsets.Add(item.Key, item.Value);
				}
			}
		}

		var size = inheritedSize;
		var bitmap = inheritedBitmap;
		foreach (var fieldHandle in definition.GetFields())
		{
			var field = GetField(fieldHandle);
			if (field.IsStatic)
			{
				continue;
			}
			var fieldType = SubstituteTypeArguments(
				field.Type,
				constructedType.GenericArguments);
			if (TryGetFixedBufferSize(fieldType, out var fixedBufferSize))
			{
				fieldOffsets.Add(fieldHandle, size);
				size = checked(size + fixedBufferSize);
				continue;
			}
			if (TryGetReferenceFreeStructLayout(
					fieldType,
					field.ModuleName,
					out var aggregateLayout) &&
				aggregateLayout.UsesAggregateTransport)
			{
				fieldOffsets.Add(fieldHandle, size);
				if (IsIntegerValueListBuilder(constructedType) || FrameworkImplementationPack?.EnableUnlistedManagedBodies == true ||
					FrameworkImplementationPack?.IsPinnedStringBuilderInput == true &&
					(IsCharacterValueListBuilder(constructedType) || IsExperimentalManagedStructLayout(aggregateLayout) || IsExperimentalNullableJoinValue(fieldType)))
				{
					var word = (size - (isValueType ? 0 : 8)) / 4;
					if (aggregateLayout.ReferenceBitmap != 0 && (word >= 32 || (word > 0 && (aggregateLayout.ReferenceBitmap >> (32 - word)) != 0)))
						throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, $"Reference fields of '{constructedType.DisplayName}' exceed the descriptor bitmap.");
					bitmap |= aggregateLayout.ReferenceBitmap << word;
				}
				size = checked(size + aggregateLayout.Size);
				continue;
			}
			if ((!fieldType.IsSupportedScalar && !IsTransparentScalarType(fieldType)) ||
				fieldType.Size > 8)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					$"Constructed field '{constructedType.DisplayName}::{Reader.GetString(Reader.GetFieldDefinition(fieldHandle).Name)}' has unsupported type '{fieldType.DisplayName}'.");
			}

			var fieldStorageSize =
				fieldType.IsSupportedScalar && fieldType.Size == 8
					? 8
					: 4;
			var fieldIndex = (size - (isValueType ? 0 : 8)) / 4;
			if (fieldType.Kind == CilTypeKind.ManagedReference)
			{
				if (fieldIndex is < 0 or >= 32)
					throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, $"Reference fields of '{constructedType.DisplayName}' exceed the descriptor bitmap.");
				bitmap |= 1u << fieldIndex;
			}
			fieldOffsets.Add(fieldHandle, size);
			size = checked(size + fieldStorageSize);
		}

		if (isValueType && size == 0 && FrameworkImplementationPack?.EnableUnlistedManagedBodies == true &&
			!Net10FrameworkContract.Default.IsFrameworkAssembly(_assemblyName)) size = 4;
		var layout = new CilTypeLayout(
			handle,
			constructedType.DisplayName,
			size,
			bitmap,
			fieldOffsets,
			_assemblyName,
			constructedType);
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true && _assemblyName == "System.Private.CoreLib" &&
			constructedType.GenericArguments is [{ Kind: CilTypeKind.ManagedReference, DisplayName: "object" }] &&
			GetTypeName(definition).StartsWith("System.Runtime.CompilerServices.InlineArray", StringComparison.Ordinal))
		{
			var attributes = definition.GetCustomAttributes().Where(attribute =>
				GetAttributeTypeName(attribute) == "System.Runtime.CompilerServices.InlineArrayAttribute").ToArray();
			if (!isValueType || size != 4 || bitmap != 1 || fieldOffsets.Count != 1 || attributes.Length != 1 ||
				Reader.GetCustomAttribute(attributes[0]).DecodeValue(new AttributeTypeProvider(Reader)).FixedArguments is not [{ Value: int length }] ||
				length is < 1 or > 32)
				throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, "Object inline-array argument buffers require one object field and one to 32 precise reference slots.");
			layout = layout with { Size = length * 4, ReferenceBitmap = length == 32 ? uint.MaxValue : (1u << length) - 1 };
		}
		_constructedLayoutCache.Add(key, layout);
		return layout;
	}

	private static bool IsShadowEqualityComparerFrameworkBase(
		CilType constructedType,
		CilType baseType) =>
		constructedType.DisplayName.StartsWith(
			"CopperSharp.Runtime.ShadowEqualityComparer`1<",
			StringComparison.Ordinal) &&
		baseType.DisplayName.StartsWith(
			"System.Collections.Generic.EqualityComparer`1<",
			StringComparison.Ordinal) &&
		constructedType.GenericArguments.SequenceEqual(
			baseType.GenericArguments);

	public CilInterfaceDefinition GetRuntimeInterfaceDefinition(
		CilRuntimeTypeTarget target)
	{
		if (!target.IsInterface || target.Handle.Kind != HandleKind.TypeDefinition)
		{
			throw new InvalidOperationException(
				$"Runtime type '{target.Type.DisplayName}' is not a local interface definition.");
		}
		return GetModule(target.ModuleName)
			.GetInterfaceDefinition(
				(TypeDefinitionHandle)target.Handle,
				target.IsConstructedGeneric ? target.Type : null);
	}

	public CilRuntimeTypeTarget ResolveRuntimeTypeIdentity(
		CilType type,
		string preferredModuleName)
	{
		// A value copied through a contract TypeRef may name a facade that has
		// no implementation module. Resolve its identity in the verified CoreLib
		// before examining other loaded definitions, as for ordinary forwarding.
		if (!_root._modules.ContainsKey(preferredModuleName) &&
			Net10FrameworkContract.Default.IsFrameworkAssembly(preferredModuleName) &&
			CanResolveImplementationRuntimeType(type) &&
			GetOrLoadImplementationModule("System.Private.CoreLib") is { } forwardedImplementation)
			preferredModuleName = forwardedImplementation._assemblyName;
		var preferred = GetModule(preferredModuleName);
		if (preferred.TryFindRuntimeTypeDefinition(type, out var target))
		{
			return target;
		}
		foreach (var module in _root._modules.Values)
		{
			if (!ReferenceEquals(module, preferred) &&
				module.TryFindRuntimeTypeDefinition(type, out target))
			{
				return target;
			}
		}
		if (GetOrLoadImplementationModule("System.Private.CoreLib") is { } implementation &&
			!ReferenceEquals(implementation, preferred) &&
			implementation.TryFindRuntimeTypeDefinition(type, out target))
		{
			return target;
		}
		return new CilRuntimeTypeTarget(
			type,
			"System.Private.CoreLib",
			default,
			IsInterface: false,
			IsArray: type.ElementType is not null &&
				type.DisplayName.EndsWith("]", StringComparison.Ordinal));
	}

	private bool TryFindRuntimeTypeDefinition(
		CilType type,
		out CilRuntimeTypeTarget target)
	{
		if (!type.GenericArguments.IsDefaultOrEmpty &&
			TryFindConstructedGenericDefinition(type, out target))
		{
			return true;
		}

		if (_runtimeTypeDefinitionIndex is null)
		{
			var index = new Dictionary<string, (TypeDefinitionHandle, bool)>(
				StringComparer.Ordinal);
			foreach (var handle in Reader.TypeDefinitions)
			{
				var definition = Reader.GetTypeDefinition(handle);
				var signatureName = _signatureProvider
					.GetTypeFromDefinition(Reader, handle, 0x12)
					.DisplayName;
				index.TryAdd(
					signatureName,
					(handle, (definition.Attributes & TypeAttributes.Interface) != 0));
				if (_assemblyName == "System.Private.CoreLib" && FrameworkImplementationPack?.EnableUnlistedManagedBodies == true)
				{
					// TypeRef tokens spell primitives as System.Int32/System.Object,
					// whereas signatures and boxed descriptors use int/object.
					var metadataName = Reader.GetString(definition.Name);
					var metadataNamespace = Reader.GetString(definition.Namespace);
					if (metadataNamespace.Length != 0)
						index.TryAdd($"{metadataNamespace}.{metadataName}",
							(handle, (definition.Attributes & TypeAttributes.Interface) != 0));
				}
			}
			_runtimeTypeDefinitionIndex = index;
		}

		if (_runtimeTypeDefinitionIndex.TryGetValue(type.DisplayName, out var definitionTarget))
		{
			target = new CilRuntimeTypeTarget(
				_assemblyName == "System.Private.CoreLib" && FrameworkImplementationPack?.EnableUnlistedManagedBodies == true
					? _signatureProvider.GetTypeFromDefinition(Reader, definitionTarget.Handle,
						IsValueTypeDefinition(Reader.GetTypeDefinition(definitionTarget.Handle)) ? (byte)0x11 : (byte)0x12)
					: type,
				_assemblyName,
				definitionTarget.Handle,
				definitionTarget.IsInterface,
				IsArray: false);
			return true;
		}
		target = null!;
		return false;
	}

	public bool IsUninitializedStorageType(CilType type)
	{
		if (_uninitializedStorageTypeCache.TryGetValue(type.DisplayName, out var cached))
		{
			return cached;
		}

		foreach (var handle in Reader.TypeDefinitions)
		{
			var definition = Reader.GetTypeDefinition(handle);
			if (!TypeNameMatches(definition, type.DisplayName))
			{
				continue;
			}

			var localResult = HasAttribute(
				definition.GetCustomAttributes(),
				UninitializedStorageAttributeName);
			_uninitializedStorageTypeCache.Add(type.DisplayName, localResult);
			return localResult;
		}

		var result = HasReflectionAttribute(type.DisplayName, UninitializedStorageAttributeName);
		_uninitializedStorageTypeCache.Add(type.DisplayName, result);
		return result;
	}

	public bool RequiresLongAlignedStackAddress(CilType type)
	{
		if (_longAlignedStackTypeCache.TryGetValue(type.DisplayName, out var cached))
		{
			return cached;
		}

		if (!IsUninitializedStorageType(type))
		{
			_longAlignedStackTypeCache.Add(type.DisplayName, false);
			return false;
		}

		foreach (var handle in Reader.TypeDefinitions)
		{
			var definition = Reader.GetTypeDefinition(handle);
			if (!TypeNameMatches(definition, type.DisplayName))
			{
				continue;
			}

			foreach (var attributeHandle in definition.GetCustomAttributes())
			{
				if (!string.Equals(GetAttributeTypeName(attributeHandle), StackAlignmentAttributeName, StringComparison.Ordinal))
				{
					continue;
				}

				var value = Reader.GetCustomAttribute(attributeHandle)
					.DecodeValue(new AttributeTypeProvider(Reader));
				var localResult = value.FixedArguments.Length == 1 &&
					value.FixedArguments[0].Value is int alignment &&
					alignment >= 4;
				_longAlignedStackTypeCache.Add(type.DisplayName, localResult);
				return localResult;
			}
		}

		var result = HasReflectionAttribute(type.DisplayName, StackAlignmentAttributeName);
		_longAlignedStackTypeCache.Add(type.DisplayName, result);
		return result;
	}

	private bool HasReflectionAttribute(string displayName, string attributeName)
	{
		var cacheKey = (displayName, attributeName);
		if (_reflectionAttributeCache.TryGetValue(cacheKey, out var cached))
		{
			return cached;
		}

		foreach (var path in Directory.EnumerateFiles(_assemblyDirectory, "*.dll"))
		{
			try
			{
				var type = Assembly.LoadFrom(path).GetType(displayName.Replace('/', '+'), throwOnError: false);
				if (type is null)
				{
					continue;
				}

				var result = type.CustomAttributes.Any(attribute =>
					string.Equals(attribute.AttributeType.FullName, attributeName, StringComparison.Ordinal));
				_reflectionAttributeCache.Add(cacheKey, result);
				return result;
			}
			catch (BadImageFormatException)
			{
			}
			catch (FileNotFoundException)
			{
			}
			catch (FileLoadException)
			{
			}
		}

		_reflectionAttributeCache.Add(cacheKey, false);
		return false;
	}

	public MethodReference ResolveMethodToken(int token, CilMethod caller, int ilOffset)
	{
		if (TryGetNumericCall(token, caller, ilOffset, out var numericCall)) return numericCall;
		if (!string.IsNullOrEmpty(caller.ModuleName) &&
			!string.Equals(caller.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(caller, ilOffset).ResolveMethodToken(token, caller, ilOffset);
		}

		var cacheKey = (token, caller.Identity, ilOffset);
		if (_methodReferenceCache.TryGetValue(cacheKey, out var cached))
		{
			return cached;
		}

		EntityHandle handle;
		try
		{
			handle = MetadataTokens.EntityHandle(token);
		}
		catch (ArgumentException exception)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Invalid method metadata token 0x{token:X8}.",
				caller.DisplayName,
				ilOffset,
				exception);
		}

		var result = handle.Kind switch
		{
			HandleKind.MethodDefinition => ResolveMethodDefinitionForCaller(
				(MethodDefinitionHandle)handle, caller, ilOffset),
			HandleKind.MemberReference => ResolveMemberReference(
				(MemberReferenceHandle)handle,
				caller,
				ilOffset),
			HandleKind.MethodSpecification => ResolveMethodSpecification(
				(MethodSpecificationHandle)handle,
				caller,
				ilOffset),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Token 0x{token:X8} is not a method reference.",
				caller.DisplayName,
				ilOffset)
		};
		if (result.Definition is { } declaration &&
			caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset)?.ConstrainedTypeToken is { } constrainedTypeToken &&
			(TryResolveExperimentalConstrainedPrimitiveToString(caller, constrainedTypeToken, ilOffset, declaration, out var constrainedToString) ||
			 TryResolveExperimentalConstrainedStringToString(caller, constrainedTypeToken, ilOffset, declaration, out constrainedToString)))
			result = result with { Definition = constrainedToString };
		_methodReferenceCache.Add(cacheKey, result);
		return result;
	}

	private MethodReference ResolveMethodDefinitionForCaller(MethodDefinitionHandle handle, CilMethod caller, int ilOffset)
	{
		var member = DescribeFrameworkMethodDefinition(handle, []);
		if (!TryCreatePinnedApplicationFormattingLeafBinding(member, caller, ilOffset, out _) && !TryCreatePinnedCompositeBufferToStringBinding(member, caller, ilOffset, out _, out _) && !TryCreatePinnedHandlerToStringBinding(member, caller, ilOffset, out _) && !TryCreatePinnedDecimalScaleComparisonBinding(member, caller, ilOffset, out _, out _) && !TryCreatePinnedIntegralToStringBinding(member, caller, ilOffset, out _, out _) &&
			!TryCreatePinnedNullableToStringBinding(member, caller, ilOffset, out _, out _) &&
			!TryCreateExperimentalJoinEnumerationBinding(member, caller, out _) &&
			!TryCreateExperimentalListPrefixCopyBinding(member, caller, out _) &&
			!TryCreateExperimentalCompositeSegmentCopyBinding(member, caller, out _) &&
			!TryCreateExperimentalObjectJoinTextBinding(member, caller, out _) &&
			!TryCreateExperimentalObjectJoinDispatchBinding(member, caller, out _) &&
			!TryCreateExperimentalNumberGroupCloneBinding(member, caller, out _)) return ResolveMethodDefinition(handle, caller, ilOffset);
		var declaration = GetMethod(handle);
		return TryResolveRegisteredBinding(member, GetTypeName(Reader.GetTypeDefinition(declaration.DeclaringType)),
			declaration.Signature, declaration.ConstructedDeclaringType, caller: caller, ilOffset: ilOffset)!;
	}

	public CilMethodReferenceIdentity? DescribeMethodToken(
		int token,
		CilMethod caller,
		int ilOffset)
	{
		if (TryGetNumericCall(token, caller, ilOffset, out var numericCall))
			return GetModule(numericCall.Definition!.ModuleName).DescribeMethodDefinition(numericCall.Definition.Handle, []);
		if (!string.IsNullOrEmpty(caller.ModuleName) &&
			!string.Equals(caller.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(caller, ilOffset).DescribeMethodToken(token, caller, ilOffset);
		}

		EntityHandle handle;
		try
		{
			handle = MetadataTokens.EntityHandle(token);
		}
		catch (ArgumentException exception)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Invalid method metadata token 0x{token:X8}.",
				caller.DisplayName,
				ilOffset,
				exception);
		}

		return handle.Kind switch
		{
			HandleKind.MemberReference => DescribeMemberReference(
				(MemberReferenceHandle)handle,
				ImmutableArray<string>.Empty),
			HandleKind.MethodSpecification => DescribeMethodSpecification(
				(MethodSpecificationHandle)handle),
			HandleKind.MethodDefinition when CanDescribeCoreLibDefinitions =>
				DescribeMethodDefinition((MethodDefinitionHandle)handle, ImmutableArray<string>.Empty),
			HandleKind.MethodDefinition => null,
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Token 0x{token:X8} is not a method reference.",
				caller.DisplayName,
				ilOffset)
		};
	}

	public string? DescribeMethodTokenName(
		int token,
		CilMethod caller,
		int ilOffset)
	{
		if (!string.IsNullOrEmpty(caller.ModuleName) &&
			!string.Equals(caller.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(caller, ilOffset)
				.DescribeMethodTokenName(token, caller, ilOffset);
		}
		var handle = MetadataTokens.EntityHandle(token);
		return MethodName(handle);

		string? MethodName(EntityHandle methodHandle) => methodHandle.Kind switch
		{
			HandleKind.MethodDefinition => Reader.GetString(
				Reader.GetMethodDefinition(
					(MethodDefinitionHandle)methodHandle).Name),
			HandleKind.MemberReference => Reader.GetString(
				Reader.GetMemberReference(
					(MemberReferenceHandle)methodHandle).Name),
			HandleKind.MethodSpecification => MethodName(
				Reader.GetMethodSpecification(
					(MethodSpecificationHandle)methodHandle).Method),
			_ => null
		};
	}

	public FrameworkMemberId DescribeFrameworkMethodToken(
		int token,
		CilMethod caller,
		int ilOffset)
	{
		if (TryGetNumericCall(token, caller, ilOffset, out var numericCall))
			return GetModule(numericCall.Definition!.ModuleName).DescribeFrameworkMethodDefinition(numericCall.Definition.Handle, []);
		if (!string.IsNullOrEmpty(caller.ModuleName) &&
			!string.Equals(caller.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(caller, ilOffset).DescribeFrameworkMethodToken(
				token,
				caller,
				ilOffset);
		}

		EntityHandle handle;
		try
		{
			handle = MetadataTokens.EntityHandle(token);
		}
		catch (ArgumentException exception)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Invalid method metadata token 0x{token:X8}.",
				caller.DisplayName,
				ilOffset,
				exception);
		}

		return handle.Kind switch
		{
			HandleKind.MethodDefinition => DescribeFrameworkMethodDefinition(
				(MethodDefinitionHandle)handle,
				[]),
			HandleKind.MemberReference => DescribeFrameworkMemberReference(
				(MemberReferenceHandle)handle,
				[], caller),
			HandleKind.MethodSpecification => DescribeFrameworkMethodSpecification(
				(MethodSpecificationHandle)handle, caller),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Token 0x{token:X8} is not a method reference.",
				caller.DisplayName,
				ilOffset)
		};
	}

	private FrameworkMemberId DescribeFrameworkMethodSpecification(
		MethodSpecificationHandle handle, CilMethod? caller = null)
	{
		var provider = new FrameworkSignatureTypeProvider(this);
		var specification = Reader.GetMethodSpecification(handle);
		var context = caller is null ? FrameworkGenericContext.Empty : new FrameworkGenericContext(
			caller.GenericContext.TypeArguments.Select((argument, index) =>
				FrameworkPrimitiveArgument(argument) ?? (IsPinnedGenericJoinElement(argument) ? GetApplicationJoinFrameworkType(argument) : null) ?? FrameworkTypeId.GenericTypeParameter(index)).ToImmutableArray(),
			caller.GenericContext.MethodArguments.Select((argument, index) =>
				FrameworkPrimitiveArgument(argument) ?? (IsPinnedGenericJoinElement(argument) ? GetApplicationJoinFrameworkType(argument) : null) ?? FrameworkTypeId.GenericMethodParameter(index)).ToImmutableArray());
		var arguments = specification.DecodeSignature(
			provider,
			context);
		return specification.Method.Kind switch
		{
			HandleKind.MethodDefinition => DescribeFrameworkMethodDefinition(
				(MethodDefinitionHandle)specification.Method,
				arguments),
			HandleKind.MemberReference => DescribeFrameworkMemberReference(
				(MemberReferenceHandle)specification.Method,
				arguments, caller),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"A method specification must refer to a method definition or member reference.")
		};
	}

	private static FrameworkTypeId? FrameworkPrimitiveArgument(CilType type)
	{
		if (type.IsEnum) return null;
		if (type is { Kind: CilTypeKind.ManagedReference, ElementType: null } && type.DisplayName is "object" or "string")
			return FrameworkTypeId.Primitive(type.DisplayName == "object" ? "System.Object" : "System.String");
		if (type.DisplayName is "nint" or "nuint")
			return FrameworkTypeId.Primitive(type.DisplayName == "nint" ? "System.IntPtr" : "System.UIntPtr");
		var name = (type.Kind, type.Size) switch
		{
			(CilTypeKind.Boolean, 1) => "System.Boolean",
			(CilTypeKind.Character, 2) => "System.Char",
			(CilTypeKind.SignedInteger, 1) => "System.SByte",
			(CilTypeKind.UnsignedInteger, 1) => "System.Byte",
			(CilTypeKind.SignedInteger, 2) => "System.Int16",
			(CilTypeKind.UnsignedInteger, 2) => "System.UInt16",
			(CilTypeKind.SignedInteger, 4) => "System.Int32",
			(CilTypeKind.UnsignedInteger, 4) => "System.UInt32",
			(CilTypeKind.SignedInteger, 8) => "System.Int64",
			(CilTypeKind.UnsignedInteger, 8) => "System.UInt64",
			(CilTypeKind.FloatingPoint, 4) => "System.Single",
			(CilTypeKind.FloatingPoint, 8) => "System.Double",
			_ => null
		};
		return name is null ? null : FrameworkTypeId.Primitive(name);
	}

	private FrameworkMemberId DescribeFrameworkMethodDefinition(
		MethodDefinitionHandle handle,
		IReadOnlyList<FrameworkTypeId> methodTypeArguments)
	{
		var provider = new FrameworkSignatureTypeProvider(this);
		var definition = Reader.GetMethodDefinition(handle);
		var declaringType = provider.GetTypeFromDefinition(
			Reader,
			definition.GetDeclaringType(),
			0x12);
		var signature = definition.DecodeSignature(
			provider,
			FrameworkGenericContext.Empty);
		return new FrameworkMemberId(
			declaringType,
			Reader.GetString(definition.Name),
			FrameworkMethodSignatureId.From(signature),
			methodTypeArguments);
	}

	private FrameworkMemberId DescribeFrameworkMemberReference(
		MemberReferenceHandle handle,
		IReadOnlyList<FrameworkTypeId> methodTypeArguments,
		CilMethod? caller = null)
	{
		var provider = new FrameworkSignatureTypeProvider(this);
		var member = Reader.GetMemberReference(handle);
		var declaringContext = caller is null ? FrameworkGenericContext.Empty : new FrameworkGenericContext(
			caller.GenericContext.TypeArguments.Select((argument, index) =>
				FrameworkPrimitiveArgument(argument) ?? (IsPinnedGenericJoinElement(argument) ? GetApplicationJoinFrameworkType(argument) : null) ?? FrameworkTypeId.GenericTypeParameter(index)).ToImmutableArray(),
			caller.GenericContext.MethodArguments.Select((argument, index) =>
				FrameworkPrimitiveArgument(argument) ?? (IsPinnedGenericJoinElement(argument) ? GetApplicationJoinFrameworkType(argument) : null) ?? FrameworkTypeId.GenericMethodParameter(index)).ToImmutableArray());
		var declaringType = member.Parent.Kind switch
		{
			HandleKind.TypeDefinition => provider.GetTypeFromDefinition(
				Reader,
				(TypeDefinitionHandle)member.Parent,
				0x12),
			HandleKind.TypeReference => provider.GetTypeFromReference(
				Reader,
				(TypeReferenceHandle)member.Parent,
				0x12),
			HandleKind.TypeSpecification => provider.GetTypeFromSpecification(
				Reader,
				declaringContext,
				(TypeSpecificationHandle)member.Parent,
				0x12),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"A method member reference must have a type parent.")
		};
		var signature = member.DecodeMethodSignature(
			provider,
			FrameworkGenericContext.Empty);
		return new FrameworkMemberId(
			declaringType,
			Reader.GetString(member.Name),
			FrameworkMethodSignatureId.From(signature),
			methodTypeArguments);
	}

	private CilMethodReferenceIdentity? DescribeMethodSpecification(
		MethodSpecificationHandle handle)
	{
		var specification = Reader.GetMethodSpecification(handle);
		if (specification.Method.Kind != HandleKind.MemberReference &&
			(specification.Method.Kind != HandleKind.MethodDefinition || !CanDescribeCoreLibDefinitions))
		{
			return null;
		}

		var arguments = specification
			.DecodeSignature(_signatureProvider, CilGenericContext.Empty)
			.Select(static argument => argument.DisplayName)
			.ToImmutableArray();
		return specification.Method.Kind == HandleKind.MemberReference
			? DescribeMemberReference((MemberReferenceHandle)specification.Method, arguments)
			: DescribeMethodDefinition((MethodDefinitionHandle)specification.Method, arguments);
	}

	private CilMethodReferenceIdentity DescribeMethodDefinition(
		MethodDefinitionHandle handle,
		ImmutableArray<string> methodTypeArguments)
	{
		var definition = Reader.GetMethodDefinition(handle);
		var signature = definition.DecodeSignature(_signatureProvider, CilGenericContext.Empty);
		return new CilMethodReferenceIdentity(
			_assemblyName,
			GetTypeName(definition.GetDeclaringType()),
			Reader.GetString(definition.Name),
			!signature.Header.IsInstance,
			signature.GenericParameterCount,
			signature.ReturnType.DisplayName,
			signature.ParameterTypes.Select(static type => type.DisplayName).ToImmutableArray(),
			methodTypeArguments);
	}

	private CilMethodReferenceIdentity? DescribeMemberReference(
		MemberReferenceHandle handle,
		ImmutableArray<string> methodTypeArguments)
	{
		var member = Reader.GetMemberReference(handle);
		string assemblyName;
		string typeName;
		switch (member.Parent.Kind)
		{
			case HandleKind.TypeReference:
			{
				var type = Reader.GetTypeReference((TypeReferenceHandle)member.Parent);
				assemblyName = GetReferencedAssemblyName(type.ResolutionScope);
				typeName = GetTypeName(type);
				break;
			}
			case HandleKind.TypeSpecification:
			{
				var specification = Reader.GetTypeSpecification(
					(TypeSpecificationHandle)member.Parent);
				typeName = specification
					.DecodeSignature(_signatureProvider, CilGenericContext.Empty)
					.DisplayName;
				assemblyName = specification.DecodeSignature(
					new DeclaringAssemblyTypeProvider(this),
					CilGenericContext.Empty) ?? string.Empty;
				break;
			}
			default:
				return null;
		}

		if (string.IsNullOrEmpty(assemblyName))
		{
			return null;
		}

		var signature = member.DecodeMethodSignature(
			_signatureProvider,
			CilGenericContext.Empty);
		return new CilMethodReferenceIdentity(
			assemblyName,
			typeName,
			Reader.GetString(member.Name),
			!signature.Header.IsInstance,
			signature.GenericParameterCount,
			signature.ReturnType.DisplayName,
			signature.ParameterTypes
				.Select(static parameter => parameter.DisplayName)
				.ToImmutableArray(),
			methodTypeArguments);
	}

	private MethodReference ResolveMethodDefinition(MethodDefinitionHandle handle, CilMethod caller, int ilOffset)
	{
		var definition = Reader.GetMethodDefinition(handle);
		var declaringType = Reader.GetTypeDefinition(definition.GetDeclaringType());
		var signature = definition.DecodeSignature(_signatureProvider, CilGenericContext.Empty);
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && _assemblyName == "CopperSharp.Runtime.Managed" &&
			GetTypeName(declaringType) == "CopperSharp.Runtime.ShadowCultureInfo" &&
			(definition.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private && signature.Header.RawValue == 0 &&
			signature.GenericParameterCount == 0 && signature.RequiredParameterCount == 1 && signature.ReturnType.IsVoid &&
			signature.ParameterTypes is [{ Kind: CilTypeKind.ManagedReference, Size: 4 } receiver] &&
			((Reader.GetString(definition.Name) == "FreezeCulture" && receiver.DisplayName == "System.Globalization.CultureInfo") ||
			 (Reader.GetString(definition.Name) == "FreezeNumberFormat" && receiver.DisplayName == "System.Globalization.NumberFormatInfo")))
		{
			var field = GetExperimentalReadOnlyField(receiver.DisplayName);
			return MethodReference.ForIntrinsic($"intrinsic:runtime-freeze-number-provider:{GetTypeLayout(field).FieldOffsets[field.Handle]}", signature);
		}
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) && _assemblyName == "CopperSharp.Runtime.Managed" &&
			GetTypeName(declaringType) == "CopperSharp.Runtime.ShadowBoxedEnum" && Reader.GetString(definition.Name) == "Data" &&
			(definition.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private && signature.Header.RawValue == 0 &&
			signature.RequiredParameterCount == 1 &&
			signature.GenericParameterCount == 0 && signature.ParameterTypes is [{ DisplayName: "object", Kind: CilTypeKind.ManagedReference }] &&
			signature.ReturnType is { Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "CopperSharp.Runtime.ShadowEnumMetadata" })
			return MethodReference.ForIntrinsic("intrinsic:runtime-boxed-enum-data", signature);
		return TryResolveRegisteredBinding(
				handle,
				GetTypeName(declaringType),
				signature,
				constructedDeclaringType: null, caller: caller, ilOffset: ilOffset) ??
			MethodReference.ForDefinition(GetMethod(handle));
	}

	private bool TryResolveObjectInlineArrayHelper(EntityHandle handle, ImmutableArray<CilType> arguments, out MethodReference result)
	{
		result = null!;
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true || handle.Kind != HandleKind.MethodDefinition ||
			arguments is not [var buffer, { Kind: CilTypeKind.ManagedReference, DisplayName: "object" }] ||
			!TryGetReferenceFreeStructLayout(buffer, _assemblyName, out var layout) || layout.ModuleName != "System.Private.CoreLib" ||
			!buffer.DisplayName.StartsWith("System.Runtime.CompilerServices.InlineArray", StringComparison.Ordinal)) return false;
		var definition = Reader.GetMethodDefinition((MethodDefinitionHandle)handle);
		var declaringType = Reader.GetTypeDefinition(definition.GetDeclaringType());
		if (GetTypeName(declaringType) != "<PrivateImplementationDetails>" ||
			!HasAttribute(declaringType.GetCustomAttributes(), "System.Runtime.CompilerServices.CompilerGeneratedAttribute")) return false;
		var method = GetConstructedMethod((MethodDefinitionHandle)handle, null, arguments);
		if (method.Signature.Header.IsInstance || method.Signature.GenericParameterCount != 2 ||
			method.Signature.ParameterTypes is not [{ Kind: CilTypeKind.ManagedPointer, ElementType: { } parameterBuffer }, var index] ||
			parameterBuffer.DisplayName != buffer.DisplayName || index.DisplayName != "int") return false;
		var ops = method.Instructions;
		var span = method.Name == "InlineArrayAsReadOnlySpan" && method.Signature.ReturnType.DisplayName == "System.ReadOnlySpan`1<object>";
		var element = method.Name == "InlineArrayElementRef" && method.Signature.ReturnType is { Kind: CilTypeKind.ManagedPointer, ElementType.DisplayName: "object" };
		if (span && ops.Count == 6 && ops[0].OpCode == OpCodes.Ldarg_0 && ops[3].OpCode == OpCodes.Ldarg_1 && ops[5].OpCode == OpCodes.Ret &&
			IsCall(ops[1], "System.Runtime.CompilerServices.Unsafe", "AsRef", [buffer]) &&
			IsCall(ops[2], "System.Runtime.CompilerServices.Unsafe", "As", arguments) &&
			IsCall(ops[4], "System.Runtime.InteropServices.MemoryMarshal", "CreateReadOnlySpan", [arguments[1]]))
		{
			result = MethodReference.ForIntrinsic("intrinsic:readonly-span-from-ref-length:object", method.Signature) with { ConstructedDeclaringType = method.Signature.ReturnType };
			return true;
		}
		if (element && ops.Count == 5 && ops[0].OpCode == OpCodes.Ldarg_0 && ops[2].OpCode == OpCodes.Ldarg_1 && ops[4].OpCode == OpCodes.Ret &&
			IsCall(ops[1], "System.Runtime.CompilerServices.Unsafe", "As", arguments) &&
			IsCall(ops[3], "System.Runtime.CompilerServices.Unsafe", "Add", [arguments[1]]))
		{
			result = MethodReference.ForIntrinsic("intrinsic:corelib-add-int32-ref", method.Signature);
			return true;
		}
		return false;

		bool IsCall(CilInstruction instruction, string type, string name, ImmutableArray<CilType> expectedArguments)
		{
			if (instruction.OpCode != OpCodes.Call || instruction.Operand is not int token ||
				MetadataTokens.EntityHandle(token).Kind != HandleKind.MethodSpecification) return false;
			var spec = Reader.GetMethodSpecification((MethodSpecificationHandle)MetadataTokens.EntityHandle(token));
			if (!spec.DecodeSignature(_signatureProvider, method.GenericContext).Select(argument => (argument.Kind, argument.DisplayName))
				.SequenceEqual(expectedArguments.Select(argument => (argument.Kind, argument.DisplayName))) || spec.Method.Kind != HandleKind.MemberReference) return false;
			var member = DescribeFrameworkMethodSpecification((MethodSpecificationHandle)MetadataTokens.EntityHandle(token), method);
			var expectedTarget = name == "CreateReadOnlySpan" ? "intrinsic:readonly-span-from-ref-length:object"
				: name == "Add" ? "intrinsic:corelib-add-int32-ref" : "intrinsic:ref-cast";
			return member.AssemblyName is "System.Runtime" or "System.Private.CoreLib" &&
				member.DeclaringType.MetadataName == type && member.Name == name &&
				(name != "Add" || member.Signature.ParameterTypes is [_, var offset] && offset.Equals(FrameworkTypeId.Primitive("System.Int32"))) &&
				FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding) && binding.Target == expectedTarget;
		}
	}

	private MethodReference ResolveMethodSpecification(
		MethodSpecificationHandle handle,
		CilMethod caller,
		int ilOffset)
	{
		var specification = Reader.GetMethodSpecification(handle);
		if (specification.Method.Kind == HandleKind.MethodDefinition &&
			Reader.GetString(Reader.GetMethodDefinition(
				(MethodDefinitionHandle)specification.Method).Name) is
				"InlineArrayElementRef" or "InlineArrayAsReadOnlySpan" &&
			caller.Instructions.Any(instruction =>
				instruction.OpCode == OpCodes.Call &&
				EphemeralParamsArrayAnalyzer.AnalyzeReadOnlySpanParams(
					this,
					caller,
					instruction.Offset)?.SuppressedCallOffsets.Contains(ilOffset) == true))
		{
			var scaffold = GetMethod((MethodDefinitionHandle)specification.Method);
			return MethodReference.ForIntrinsic(
				"intrinsic:ephemeral-inline-array-scaffolding",
				scaffold.Signature);
		}
		var arguments = specification.DecodeSignature(
			_signatureProvider,
			caller.GenericContext);
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true && _assemblyName == "CopperSharp.Runtime.Managed" &&
			specification.Method.Kind == HandleKind.MethodDefinition && arguments is [var enumType] && enumType.IsEnum &&
			enumType.Kind is CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger && enumType.Size is 1 or 2 or 4 or 8 &&
			caller.Name == "TryFormat" && caller.MethodTypeArguments is [var callerEnum] && callerEnum == enumType &&
			GetTypeName(Reader.GetTypeDefinition(caller.DeclaringType)) == "CopperSharp.Runtime.ShadowEnumFormatting")
		{
			var enumDefinition = Reader.GetMethodDefinition((MethodDefinitionHandle)specification.Method);
			if (GetTypeName(Reader.GetTypeDefinition(enumDefinition.GetDeclaringType())) == "CopperSharp.Runtime.ShadowEnumFormatting")
			{
				var enumSignature = enumDefinition.DecodeSignature(_signatureProvider, new CilGenericContext([], arguments));
				var enumName = Reader.GetString(enumDefinition.Name);
				if ((enumDefinition.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private &&
					enumSignature.Header.RawValue == 0x10 && enumSignature.GenericParameterCount == 1 &&
					enumSignature.RequiredParameterCount == enumSignature.ParameterTypes.Length &&
					((enumName is "Low" or "High" && enumSignature.ReturnType.DisplayName == "uint" && enumSignature.ParameterTypes is [var input] && input == enumType) ||
					 (enumName == "Data" && enumSignature.ReturnType.DisplayName == "CopperSharp.Runtime.ShadowEnumMetadata" && enumSignature.ParameterTypes.Length == 0)))
					return MethodReference.ForIntrinsic($"intrinsic:runtime-enum-{enumName.ToLowerInvariant()}", enumSignature) with { ConstructedDeclaringType = enumType };
			}
		}
		if (TryResolveObjectInlineArrayHelper(specification.Method, arguments, out var inlineArrayHelper))
			return inlineArrayHelper;
		if (specification.Method.Kind == HandleKind.MethodDefinition)
		{
			var definition = Reader.GetMethodDefinition((MethodDefinitionHandle)specification.Method);
			var signature = definition.DecodeSignature(
				_signatureProvider,
				new CilGenericContext([], arguments));
			if (TryResolveRegisteredBinding(
					DescribeFrameworkMethodSpecification(handle, caller),
					GetTypeName(Reader.GetTypeDefinition(definition.GetDeclaringType())),
					signature,
					constructedDeclaringType: null,
					arguments,
					caller,
					ilOffset) is { } registered)
			{
				return registered;
			}
		}
		if (specification.Method.Kind == HandleKind.MemberReference)
		{
			var memberHandle = (MemberReferenceHandle)specification.Method;
			var member = Reader.GetMemberReference(memberHandle);
			string typeName;
			CilType? constructedDeclaringType = null;
			TypeDefinitionHandle localDeclaringType = default;
			switch (member.Parent.Kind)
			{
				case HandleKind.TypeDefinition:
					localDeclaringType = (TypeDefinitionHandle)member.Parent;
					typeName = GetTypeName(Reader.GetTypeDefinition(localDeclaringType));
					if (caller.DeclaringType == localDeclaringType)
					{
						constructedDeclaringType = caller.ConstructedDeclaringType;
					}
					break;
				case HandleKind.TypeReference:
					typeName = GetTypeName(Reader.GetTypeReference(
						(TypeReferenceHandle)member.Parent));
					break;
				case HandleKind.TypeSpecification:
					constructedDeclaringType = Reader
						.GetTypeSpecification((TypeSpecificationHandle)member.Parent)
						.DecodeSignature(_signatureProvider, caller.GenericContext);
					typeName = constructedDeclaringType.DisplayName;
					if (TryFindConstructedGenericDefinition(
							constructedDeclaringType,
							out var constructedTarget) &&
						constructedTarget.Handle.Kind == HandleKind.TypeDefinition)
					{
						localDeclaringType = (TypeDefinitionHandle)constructedTarget.Handle;
					}
					break;
				default:
					throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidMetadata,
						"A generic method member reference must have a type parent.",
						caller.DisplayName,
						ilOffset);
			}
			var memberSignature = member.DecodeMethodSignature(
				_signatureProvider,
				new CilGenericContext(
					constructedDeclaringType?.GenericArguments ?? ImmutableArray<CilType>.Empty,
					arguments));

			var exactMember = DescribeFrameworkMethodSpecification(handle, caller);
			if (TryResolveRegisteredBinding(
					exactMember,
					typeName,
					memberSignature,
					constructedDeclaringType,
					arguments,
					caller,
					ilOffset) is { } registered)
			{
				return registered;
			}

			var managedIdentity = DescribeMemberReference(
				memberHandle,
				arguments.Select(static argument => argument.DisplayName).ToImmutableArray());
			if (managedIdentity is not null &&
				TryResolveManagedMethod(
					managedIdentity.AssemblyName,
					typeName,
					Reader.GetString(member.Name),
					memberSignature,
					constructedDeclaringType,
					arguments) is { } managedMethod)
			{
				return MethodReference.ForDefinition(
					managedMethod,
					constructedDeclaringType);
			}

			if (!localDeclaringType.IsNil)
			{
				var definition = Reader.GetTypeDefinition(localDeclaringType);
				var name = Reader.GetString(member.Name);
				var context = new CilGenericContext(
					constructedDeclaringType?.GenericArguments ?? ImmutableArray<CilType>.Empty,
					arguments);
				foreach (var specializedMethodHandle in definition.GetMethods())
				{
					var methodDefinition = Reader.GetMethodDefinition(specializedMethodHandle);
					if (!Reader.StringComparer.Equals(methodDefinition.Name, name))
					{
						continue;
					}
					var specializedSignature = methodDefinition.DecodeSignature(
						_signatureProvider,
						context);
					if (!SignaturesMatch(specializedSignature, memberSignature))
					{
						continue;
					}
					return MethodReference.ForDefinition(
						GetConstructedMethod(
							specializedMethodHandle,
							constructedDeclaringType,
							arguments),
						constructedDeclaringType);
				}
			}
		}

		foreach (var argument in arguments)
		{
			// These method definitions are fully specialized below. Integral
			// pairs retain their closed eight-byte ABI rather than sharing !0.
			if (IsExperimentalFloatingNullableElement(argument) || IsExperimentalDecimalNullableElement(argument) || IsExperimentalApplicationNullableElement(argument) ||
				IsExperimentalGenericJoinElement(argument) && (argument.Size == 8 || IsExperimentalApplicationJoinValue(argument) || IsExperimentalNullableJoinValue(argument)))
				continue;
			var usesSingleSlotStructRepresentation =
				TryGetReferenceFreeStructLayout(
					argument,
					caller.ModuleName,
					out var argumentLayout) &&
				argumentLayout.Size <= 4;
			if (argument.IsFloatingPoint ||
				(!argument.IsSupportedScalar && !usesSingleSlotStructRepresentation) ||
				(argument.IsSupportedScalar && argument.Size > 4))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					$"Generic argument '{argument.DisplayName}' cannot use the shared four-byte representation.",
					caller.DisplayName,
					ilOffset);
			}
		}

		if (specification.Method.Kind != HandleKind.MethodDefinition)
		{
			var unresolved = specification.Method.Kind == HandleKind.MemberReference
				? DescribeMemberReference(
					(MemberReferenceHandle)specification.Method,
					arguments.Select(static argument => argument.DisplayName)
						.ToImmutableArray())
				: null;
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				unresolved is null
					? "Generic methods declared outside the input assembly are not supported."
					: $"Managed generic method '{unresolved.AssemblyName}:{unresolved.TypeName}::{unresolved.Name}<{string.Join(",", arguments.Select(static argument => argument.DisplayName))}>' could not be resolved from ManagedAssemblyPaths.",
				caller.DisplayName,
				ilOffset);
		}

		var methodHandle = (MethodDefinitionHandle)specification.Method;
		var method = GetConstructedMethod(
			methodHandle,
			constructedDeclaringType: null,
			arguments);
		return MethodReference.ForDefinition(method);
	}

	public void Dispose()
	{
		if (ReferenceEquals(this, _root))
		{
			foreach (var module in _modules.Values.Where(module => !ReferenceEquals(module, this)).ToArray())
			{
				module.DisposeFiles();
			}
			_modules.Clear();
		}
		DisposeFiles();
	}

	private void DisposeFiles()
	{
		_peReader.Dispose();
		_stream.Dispose();
	}

	private CilMethod ResolveSelector(string selector)
	{
		var separator = selector.LastIndexOf("::", StringComparison.Ordinal);
		if (separator <= 0 || separator + 2 >= selector.Length)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.EntryPointNotFound,
				$"Entry point '{selector}' must use Namespace.Type::Method syntax.");
		}

		var requestedType = selector[..separator];
		var requestedMethod = selector[(separator + 2)..];
		var candidates = new List<MethodDefinitionHandle>();
		foreach (var typeHandle in Reader.TypeDefinitions)
		{
			var type = Reader.GetTypeDefinition(typeHandle);
			if (!string.Equals(GetTypeName(type), requestedType, StringComparison.Ordinal))
			{
				continue;
			}

			foreach (var methodHandle in type.GetMethods())
			{
				var definition = Reader.GetMethodDefinition(methodHandle);
				if (string.Equals(
					Reader.GetString(definition.Name),
					requestedMethod,
					StringComparison.Ordinal))
				{
					candidates.Add(methodHandle);
				}
			}
		}

		if (candidates.Count != 1)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.EntryPointNotFound,
				$"Entry selector '{selector}' matched {candidates.Count} methods; overloads must be unambiguous.");
		}

		return GetMethod(candidates[0]);
	}

	private M68kExternalCallConvention? ResolveExternalCall(M68kExternalMethod method)
	{
		M68kExternalCallConvention? result = null;
		foreach (var resolver in _externalCallResolvers)
		{
			if (!resolver.TryResolve(method, out var candidate))
			{
				continue;
			}
			if (result is not null)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Multiple external-call resolvers claimed '{method.DisplayName}'.",
					method.DisplayName);
			}
			result = candidate;
		}
		return result;
	}

	private string GetReferencedAssemblyName(EntityHandle scope)
	{
		if (scope.Kind == HandleKind.TypeReference)
		{
			return GetReferencedAssemblyName(
				Reader.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope);
		}

		if (scope.Kind != HandleKind.AssemblyReference)
		{
			return string.Empty;
		}
		var reference = Reader.GetAssemblyReference((AssemblyReferenceHandle)scope);
		return Reader.GetString(reference.Name);
	}

	private M68kExternalMethod LoadExternalMethod(
		string assemblyName,
		string typeName,
		string methodName,
		MethodSignature<CilType> signature,
		bool isStatic)
	{
		if (string.IsNullOrEmpty(assemblyName))
		{
			return new M68kExternalMethod(
				string.Empty,
				$"{typeName}::{methodName}",
				typeName,
				methodName,
				isStatic,
				Array.Empty<M68kMetadataAttribute>(),
				Array.Empty<M68kMetadataAttribute>(),
				Array.Empty<IReadOnlyList<M68kMetadataAttribute>>(),
				Array.Empty<M68kMetadataAttribute>());
		}

		var path = Path.Combine(_assemblyDirectory, assemblyName + ".dll");
		if (!File.Exists(path))
		{
			return new M68kExternalMethod(
				assemblyName,
				$"{typeName}::{methodName}",
				typeName,
				methodName,
				isStatic,
				Array.Empty<M68kMetadataAttribute>(),
				Array.Empty<M68kMetadataAttribute>(),
				Array.Empty<IReadOnlyList<M68kMetadataAttribute>>(),
				Array.Empty<M68kMetadataAttribute>());
		}

		var assembly = Assembly.LoadFrom(path);
		LoadDeclaredReflectionDependencies(assembly, new HashSet<string>(StringComparer.Ordinal));
		var type = assembly.GetType(typeName, throwOnError: false);
		var flags = BindingFlags.Public | BindingFlags.NonPublic |
			BindingFlags.Static | BindingFlags.Instance;
		var candidates = methodName == ".ctor"
			? type?
				.GetConstructors(flags)
				.Where(constructor =>
					!isStatic && ParametersMatch(constructor, signature))
				.Cast<MethodBase>()
				.ToArray() ?? Array.Empty<MethodBase>()
			: type?
				.GetMethods(flags)
				.Where(method =>
					method.Name == methodName &&
					method.IsStatic == isStatic &&
					ParametersMatch(method, signature))
				.Cast<MethodBase>()
				.ToArray() ?? Array.Empty<MethodBase>();
		if (candidates.Length != 1)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Referenced method '{typeName}::{methodName}' matched {candidates.Length} declarations in '{assemblyName}'.");
		}

		var declaration = candidates[0];
		return new M68kExternalMethod(
			assemblyName,
			$"{typeName}::{methodName}",
			typeName,
			methodName,
			isStatic,
			DecodeReflectionAttributes(type!.CustomAttributes),
			DecodeReflectionAttributes(declaration.CustomAttributes),
			declaration.GetParameters()
				.Select(parameter => (IReadOnlyList<M68kMetadataAttribute>)
					DecodeReflectionAttributes(parameter.CustomAttributes))
				.ToArray(),
			declaration is MethodInfo methodInfo
				? DecodeReflectionAttributes(methodInfo.ReturnParameter.CustomAttributes)
				: Array.Empty<M68kMetadataAttribute>());
	}

	private void LoadDeclaredReflectionDependencies(Assembly assembly, HashSet<string> visited)
	{
		if (!visited.Add(assembly.FullName!)) return;
		// A reflected declaration can implement an interface from a dependency
		// outside its output directory. GetType(false) may otherwise return null,
		// disguising that missing dependency as a missing method or constructor.
		// Use only explicitly supplied managed inputs; do not broaden probing.
		foreach (var reference in assembly.GetReferencedAssemblies())
		{
			if (reference.Name is not null &&
				_root._managedAssemblyPaths.TryGetValue(reference.Name, out var dependencyPath))
				LoadDeclaredReflectionDependencies(Assembly.LoadFrom(dependencyPath), visited);
		}
	}

	private static bool ParametersMatch(MethodBase method, MethodSignature<CilType> signature)
	{
		var parameters = method.GetParameters();
		if (parameters.Length != signature.ParameterTypes.Length)
		{
			return false;
		}

		for (var index = 0; index < parameters.Length; index++)
		{
			if (!ParameterMatches(parameters[index].ParameterType, signature.ParameterTypes[index]))
			{
				return false;
			}
		}

		return true;
	}

	private static bool ParameterMatches(Type reflectionType, CilType cilType)
	{
		if (reflectionType.IsPointer)
		{
			return cilType.Kind == CilTypeKind.UnmanagedPointer &&
				cilType.ElementType is not null &&
				ParameterMatches(reflectionType.GetElementType()!, cilType.ElementType);
		}
		if (reflectionType.IsByRef)
		{
			return cilType.Kind == CilTypeKind.ManagedPointer &&
				cilType.ElementType is not null &&
				ParameterMatches(reflectionType.GetElementType()!, cilType.ElementType);
		}

		if (reflectionType.IsArray)
		{
			return reflectionType.GetArrayRank() == 1 &&
				cilType.ElementType is not null &&
				ParameterMatches(reflectionType.GetElementType()!, cilType.ElementType);
		}
		if (reflectionType.IsConstructedGenericType)
		{
			var arguments = reflectionType.GetGenericArguments();
			var definitionName = ReflectionDisplayName(reflectionType.GetGenericTypeDefinition());
			if (definitionName == "System.Nullable`1") definitionName = "System.Nullable";
			return !cilType.GenericArguments.IsDefault &&
				arguments.Length == cilType.GenericArguments.Length &&
				cilType.DisplayName.StartsWith(definitionName + "<", StringComparison.Ordinal) &&
				arguments.Zip(cilType.GenericArguments).All(pair => ParameterMatches(pair.First, pair.Second));
		}

		return ReflectionDisplayName(reflectionType) == cilType.DisplayName;
	}

	private static string ReflectionDisplayName(Type type)
	{
		var displayName = type.FullName switch
		{
			"System.Void" => "void",
			"System.Boolean" => "bool",
			"System.Char" => "char",
			"System.SByte" => "sbyte",
			"System.Byte" => "byte",
			"System.Int16" => "short",
			"System.UInt16" => "ushort",
			"System.Int32" => "int",
			"System.UInt32" => "uint",
			"System.Int64" => "long",
			"System.UInt64" => "ulong",
			"System.Single" => "float",
			"System.Double" => "double",
			"System.IntPtr" => "nint",
			"System.UIntPtr" => "nuint",
			"System.String" => "string",
			"System.Object" => "object",
			_ => type.FullName ?? type.Name
		};
		return displayName.Replace('+', '/');
	}

	private static IReadOnlyList<M68kMetadataAttribute> DecodeReflectionAttributes(
		IEnumerable<CustomAttributeData> attributes) =>
		attributes.Select(attribute => new M68kMetadataAttribute(
			attribute.AttributeType.FullName!,
			attribute.ConstructorArguments
				.Select(argument => argument.ArgumentType.IsEnum
					? Convert.ToInt32(argument.Value)
					: argument.Value)
				.ToArray())).ToArray();

	private MethodReference ResolveMemberReference(
		MemberReferenceHandle handle,
		CilMethod caller,
		int ilOffset)
	{
		var member = Reader.GetMemberReference(handle);
		var name = Reader.GetString(member.Name);
		var signature = member.DecodeMethodSignature(
			_signatureProvider,
			caller.GenericContext);

		if (member.Parent.Kind == HandleKind.TypeDefinition)
		{
			var type = Reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent);
			var typeName = GetTypeName(type);
				if (TryResolveRegisteredBinding(
						handle,
						typeName,
						signature,
					constructedDeclaringType: null,
					caller,
					ilOffset) is { } registered)
			{
				return registered;
			}
			foreach (var methodHandle in type.GetMethods())
			{
				var candidate = GetMethod(methodHandle);
				if (candidate.Name == name &&
					SignaturesMatch(candidate.Signature, signature))
				{
					return MethodReference.ForDefinition(candidate);
				}
			}
		}

		if (member.Parent.Kind == HandleKind.TypeReference)
		{
			var parent = Reader.GetTypeReference((TypeReferenceHandle)member.Parent);
			var typeName = GetTypeName(parent);
			if (StringComparer.Ordinal.Equals(typeName, "System.Object") &&
				StringComparer.Ordinal.Equals(name, "Equals") &&
				signature.Header.IsInstance &&
				StringComparer.Ordinal.Equals(signature.ReturnType.DisplayName, "bool") &&
				signature.ParameterTypes.Length == 1 &&
				StringComparer.Ordinal.Equals(
					signature.ParameterTypes[0].DisplayName,
					"object") &&
				HasProvableDelegateReceiver(caller, ilOffset))
			{
				var exactMember = DescribeFrameworkMemberReference(handle, []);
				var binding = FrameworkBindingRegistry
					.BindProvenDelegateObjectEquals(exactMember);
				return MethodReference.ForBinding(binding, signature);
			}
			if (TryResolveRegisteredBinding(
					handle,
					typeName,
					signature,
					constructedDeclaringType: null,
					caller: caller,
					ilOffset: ilOffset) is { } registered)
			{
				return registered;
			}
				var displayName = $"{typeName}::{name}";
				var assemblyName = GetReferencedAssemblyName(parent.ResolutionScope);
				var externalMethod = LoadExternalMethod(
				assemblyName,
				typeName,
				name,
				signature,
				!signature.Header.IsInstance);
			var importName = TryGetExternalImportName(externalMethod.MethodAttributes);
			var convention = ResolveExternalCall(externalMethod);
			if (convention is null &&
				TryResolveManagedMethod(assemblyName, typeName, name, signature) is { } managedMethod)
			{
				return MethodReference.ForDefinition(managedMethod);
			}
			if (importName is not null)
			{
				if (convention is not null)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidMetadata,
						"A referenced method cannot be both [M68kImport] and a platform call.",
						displayName);
				}

				return MethodReference.ForDefinition(new CilMethod(
					default,
					default,
					displayName,
					name,
					signature,
					ImmutableArray<CilType>.Empty,
					Array.Empty<CilInstruction>(),
					Array.Empty<CilExceptionRegion>(),
					false,
					importName,
					GetExternalImportAbi(externalMethod, signature, displayName),
					null));
			}

			if (convention is not null)
			{
				var parameterRegisters = convention.ParameterRegisters;
				if (parameterRegisters is null && signature.ParameterTypes.Length == 0)
				{
					parameterRegisters = Array.Empty<M68kRegister>();
				}
				if (parameterRegisters is null)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedSignature,
						"Referenced external call conventions must provide their register ABI.",
						displayName);
				}
				var abi = new CilRegisterAbi(
					parameterRegisters,
					convention.ReturnRegister);
				ValidateExternalCallAbi(signature, displayName, convention, abi);
				return MethodReference.ForDefinition(new CilMethod(
					default,
					default,
					displayName,
					name,
					signature,
					ImmutableArray<CilType>.Empty,
					Array.Empty<CilInstruction>(),
					Array.Empty<CilExceptionRegion>(),
					false,
					null,
					null,
					new CilExternalCall(convention, abi)));
			}

			}

		if (member.Parent.Kind == HandleKind.TypeSpecification)
		{
			var parentType = Reader
				.GetTypeSpecification((TypeSpecificationHandle)member.Parent)
				.DecodeSignature(_signatureProvider, caller.GenericContext);
			var constructedSignature = member.DecodeMethodSignature(
				_signatureProvider,
				new CilGenericContext(
					parentType.GenericArguments,
					caller.GenericContext.MethodArguments));
			if (TryResolveRegisteredBinding(
					handle,
					parentType.DisplayName,
					constructedSignature,
					parentType,
					caller: caller,
					ilOffset: ilOffset) is { } registered)
			{
				return registered;
			}
			if (parentType.GenericArguments.Length != 0 &&
				TryFindConstructedGenericDefinition(parentType, out var constructedTarget))
			{
				var definition = Reader.GetTypeDefinition(
					(TypeDefinitionHandle)constructedTarget.Handle);
				foreach (var methodHandle in definition.GetMethods())
				{
					var candidate = GetMethod(methodHandle);
					if (candidate.Name == name &&
						ConstructedSignaturesMatch(
							candidate.Signature,
							constructedSignature,
							parentType.GenericArguments))
					{
						return MethodReference.ForDefinition(
							GetConstructedMethod(
								methodHandle,
								parentType,
								ImmutableArray<CilType>.Empty),
							parentType);
					}
				}
			}
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"External method reference '{name}' must be represented by a local [M68kImport] declaration.",
			caller.DisplayName,
			ilOffset);
	}

	private static bool HasProvableDelegateReceiver(CilMethod caller, int callOffset)
	{
		var callIndex = -1;
		for (var index = 0; index < caller.Instructions.Count; index++)
		{
			if (caller.Instructions[index].Offset == callOffset)
			{
				callIndex = index;
				break;
			}
		}

		if (callIndex < 0)
		{
			return false;
		}

		CilInstruction? argumentProducer = null;
		CilInstruction? receiverProducer = null;
		var receiverIndex = -1;
		for (var index = callIndex - 1; index >= 0; index--)
		{
			var instruction = caller.Instructions[index];
			if (instruction.OpCode == OpCodes.Nop)
			{
				continue;
			}

			if (argumentProducer is null)
			{
				argumentProducer = instruction;
				continue;
			}

			receiverProducer = instruction;
			receiverIndex = index;
			break;
		}

		if (argumentProducer is null || receiverProducer is null ||
			HasControlFlowEntryIntoDelegateEqualsSuffix(
				caller,
				receiverIndex,
				callIndex) ||
			!IsSimpleObjectValueProducer(caller, argumentProducer) ||
			!TryGetDirectLoadedType(caller, receiverProducer, out var receiverType))
		{
			return false;
		}

		return IsFrameworkDelegateType(receiverType);
	}

	private static bool HasControlFlowEntryIntoDelegateEqualsSuffix(
		CilMethod caller,
		int receiverIndex,
		int callIndex)
	{
		var unsafeEntryOffsets = caller.Instructions
			.Skip(receiverIndex + 1)
			.Take(callIndex - receiverIndex)
			.Select(static instruction => instruction.Offset)
			.ToHashSet();

		foreach (var instruction in caller.Instructions)
		{
			if (instruction.Operand is int target &&
				instruction.OpCode.FlowControl is
					FlowControl.Branch or FlowControl.Cond_Branch &&
				unsafeEntryOffsets.Contains(target))
			{
				return true;
			}
			if (instruction.Operand is int[] targets &&
				targets.Any(unsafeEntryOffsets.Contains))
			{
				return true;
			}
		}

		return caller.ExceptionRegions.Any(region =>
			unsafeEntryOffsets.Contains(region.HandlerOffset) ||
			(region.FilterOffset >= 0 &&
				unsafeEntryOffsets.Contains(region.FilterOffset)));
	}

	private static bool IsSimpleObjectValueProducer(
		CilMethod caller,
		CilInstruction instruction) =>
		instruction.OpCode == OpCodes.Ldnull ||
		TryGetDirectLoadedType(caller, instruction, out _);

	private static bool TryGetDirectLoadedType(
		CilMethod caller,
		CilInstruction instruction,
		out CilType type)
	{
		if (TryGetDirectLocalIndex(instruction, out var localIndex) &&
			(uint)localIndex < (uint)caller.Locals.Length)
		{
			type = caller.Locals[localIndex];
			return true;
		}

		if (TryGetDirectArgumentIndex(instruction, out var argumentIndex))
		{
			if (caller.Signature.Header.IsInstance)
			{
				if (argumentIndex == 0)
				{
					type = default!;
					return false;
				}
				argumentIndex--;
			}

			if ((uint)argumentIndex < (uint)caller.Signature.ParameterTypes.Length)
			{
				type = caller.Signature.ParameterTypes[argumentIndex];
				return true;
			}
		}

		type = default!;
		return false;
	}

	private static bool TryGetDirectLocalIndex(
		CilInstruction instruction,
		out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldloc_0.Value && op.Value <= OpCodes.Ldloc_3.Value)
		{
			index = op.Value - OpCodes.Ldloc_0.Value;
			return true;
		}
		if (op == OpCodes.Ldloc || op == OpCodes.Ldloc_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = default;
		return false;
	}

	private static bool TryGetDirectArgumentIndex(
		CilInstruction instruction,
		out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldarg_0.Value && op.Value <= OpCodes.Ldarg_3.Value)
		{
			index = op.Value - OpCodes.Ldarg_0.Value;
			return true;
		}
		if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = default;
		return false;
	}

	private bool ConstructedSignaturesMatch(
		MethodSignature<CilType> definition,
		MethodSignature<CilType> reference,
		ImmutableArray<CilType> typeArguments)
	{
		if (definition.Header.IsInstance != reference.Header.IsInstance ||
			definition.ParameterTypes.Length != reference.ParameterTypes.Length)
		{
			return false;
		}
		var substitutedReturn =
			SubstituteTypeArguments(definition.ReturnType, typeArguments);
		var substitutedReferenceReturn =
			SubstituteTypeArguments(reference.ReturnType, typeArguments);
		if (substitutedReturn.DisplayName != substitutedReferenceReturn.DisplayName &&
			!AreListEnumeratorShadowTypes(
				substitutedReturn,
				substitutedReferenceReturn) &&
			!AreDictionaryValueCollectionShadowTypes(
				substitutedReturn,
				substitutedReferenceReturn))
		{
			return false;
		}
		for (var index = 0; index < definition.ParameterTypes.Length; index++)
		{
			if (SubstituteTypeArguments(
					definition.ParameterTypes[index],
					typeArguments).DisplayName !=
				SubstituteTypeArguments(
					reference.ParameterTypes[index],
					typeArguments).DisplayName)
			{
				return false;
			}
		}
		return true;
	}

	private static bool AreListEnumeratorShadowTypes(CilType left, CilType right)
	{
		if (!IsListEnumeratorType(left) ||
			!IsListEnumeratorType(right) ||
			left.GenericArguments.Length != right.GenericArguments.Length ||
			!left.GenericArguments.Zip(right.GenericArguments).All(pair => AreSameClosedJoinType(pair.First, pair.Second)))
		{
			return false;
		}

		var leftIsShadow = left.DisplayName.StartsWith(
			"CopperSharp.Runtime.ShadowListEnumerator`1<",
			StringComparison.Ordinal);
		var rightIsShadow = right.DisplayName.StartsWith(
			"CopperSharp.Runtime.ShadowListEnumerator`1<",
			StringComparison.Ordinal);
		return leftIsShadow != rightIsShadow;
	}

	private static bool AreDictionaryValueCollectionShadowTypes(
		CilType left,
		CilType right)
	{
		if (left.Kind != CilTypeKind.ManagedReference ||
			right.Kind != CilTypeKind.ManagedReference ||
			left.GenericArguments.Length != 2 ||
			!left.GenericArguments.SequenceEqual(right.GenericArguments))
		{
			return false;
		}

		var leftIsPublic = left.DisplayName.StartsWith(
			"System.Collections.Generic.Dictionary`2/ValueCollection<",
			StringComparison.Ordinal);
		var rightIsPublic = right.DisplayName.StartsWith(
			"System.Collections.Generic.Dictionary`2/ValueCollection<",
			StringComparison.Ordinal);
		var leftIsShadow = left.DisplayName.StartsWith(
			"CopperSharp.Runtime.ShadowDictionaryValueCollection`2<",
			StringComparison.Ordinal);
		var rightIsShadow = right.DisplayName.StartsWith(
			"CopperSharp.Runtime.ShadowDictionaryValueCollection`2<",
			StringComparison.Ordinal);
		return (leftIsPublic && rightIsShadow) ||
			(leftIsShadow && rightIsPublic);
	}

	private CilType SubstituteTypeArguments(
		CilType type,
		ImmutableArray<CilType> typeArguments)
	{
		if (type.Kind == CilTypeKind.GenericParameter &&
			type.DisplayName.StartsWith('!') &&
			!type.DisplayName.StartsWith("!!", StringComparison.Ordinal) &&
			int.TryParse(type.DisplayName.AsSpan(1), out var index) &&
			index >= 0 && index < typeArguments.Length)
		{
			var substituted = typeArguments[index];
			return type.IsReadOnly && !substituted.IsReadOnly
				? substituted with { IsReadOnly = true }
				: substituted;
		}

		var elementType = type.ElementType is null
			? null
			: SubstituteTypeArguments(type.ElementType, typeArguments);
		var genericArguments = type.GenericArguments.IsDefaultOrEmpty
			? type.GenericArguments
			: type.GenericArguments
				.Select(argument => SubstituteTypeArguments(argument, typeArguments))
				.ToImmutableArray();
		if (ReferenceEquals(elementType, type.ElementType) &&
			(genericArguments.IsDefaultOrEmpty || genericArguments == type.GenericArguments))
		{
			return type;
		}

		string displayName;
		if (!genericArguments.IsDefaultOrEmpty)
		{
			var separator = type.DisplayName.IndexOf('<');
			var definitionName = separator < 0
				? type.DisplayName
				: type.DisplayName[..separator];
			displayName =
				$"{definitionName}<{string.Join(",", genericArguments.Select(static argument => argument.DisplayName))}>";
		}
		else if (elementType is not null && type.ElementType is not null &&
			type.DisplayName.StartsWith(type.ElementType.DisplayName, StringComparison.Ordinal))
		{
			displayName = elementType.DisplayName +
				type.DisplayName[type.ElementType.DisplayName.Length..];
		}
		else
		{
			displayName = type.DisplayName;
		}

		return type with
		{
			DisplayName = displayName,
			Size = type.IsNullable ? GetNullableStorageSize(genericArguments[0]) : type.Size,
			ElementType = elementType,
			GenericArguments = genericArguments
		};
	}

	private MethodReference? TryResolveRegisteredBinding(
		MethodDefinitionHandle handle,
		string typeName,
		MethodSignature<CilType> signature,
		CilType? constructedDeclaringType,
		CilMethod? caller = null,
		int ilOffset = -1)
	{
		var member = DescribeFrameworkMethodDefinition(handle, []);
		return TryResolveRegisteredBinding(
			member,
			typeName,
			signature,
			constructedDeclaringType, caller: caller, ilOffset: ilOffset);
	}

	private MethodReference? TryResolveRegisteredBinding(
		MemberReferenceHandle handle,
		string typeName,
		MethodSignature<CilType> signature,
		CilType? constructedDeclaringType,
		CilMethod? caller = null,
		int ilOffset = -1)
	{
		var member = DescribeFrameworkMemberReference(handle, [], caller);
		return TryResolveRegisteredBinding(
			member,
			typeName,
			signature,
			constructedDeclaringType,
			caller: caller,
			ilOffset: ilOffset);
	}

	private MethodReference? TryResolveRegisteredBinding(
		FrameworkMemberId member,
		string typeName,
		MethodSignature<CilType> signature,
		CilType? constructedDeclaringType,
		IReadOnlyList<CilType>? methodTypeArguments = null,
		CilMethod? caller = null,
		int ilOffset = -1)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && caller is
			{ ModuleName: "System.Private.CoreLib", Name: "System.Collections.IEnumerable.GetEnumerator", ConstructedDeclaringType.GenericArguments: [var listElement] } &&
			caller.ConstructedDeclaringType.DisplayName == $"System.Collections.Generic.List`1<{listElement.DisplayName}>" &&
			(listElement is { Kind: CilTypeKind.ValueType, DisplayName: "System.Decimal" } && TryGetDecimalLayout(listElement, out _) ||
			 IsCompositeFormatSegment(listElement) && TryGetReferenceFreeStructLayout(listElement, "System.Private.CoreLib", out _)) &&
			caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset) is { OpCode: var listOpcode } && listOpcode == OpCodes.Callvirt &&
			caller.Instructions.TakeWhile(instruction => instruction.Offset < ilOffset).LastOrDefault()?.OpCode == OpCodes.Ldarg_0)
		{
			var canonical = FrameworkImplementationProfile.Canonicalize(member);
			var variable = FrameworkTypeId.GenericTypeParameter(0);
			var enumerable = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerable`1"), [variable]);
			var enumerator = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerator`1"), [variable]);
			if (canonical.Equals(new FrameworkMemberId(enumerable, "GetEnumerator", new FrameworkMethodSignatureId(0x20, 0, 0, enumerator, []))) &&
				constructedDeclaringType?.DisplayName == $"System.Collections.Generic.IEnumerable`1<{listElement.DisplayName}>")
			{
				var owner = GetModule("System.Private.CoreLib");
				var actualCaller = owner.Reader.GetMethodDefinition(caller.Handle);
				if (owner.Reader.GetString(actualCaller.Name) == caller.Name && actualCaller.GetDeclaringType() == caller.DeclaringType &&
					owner.GetTypeName(caller.DeclaringType) == "System.Collections.Generic.List`1")
				{
					var interfaceIdentity = ResolveRuntimeTypeIdentity(constructedDeclaringType, "System.Private.CoreLib");
					var implementationHandle = owner.Reader.GetTypeDefinition((TypeDefinitionHandle)interfaceIdentity.Handle).GetMethods().Single(handle =>
						owner.Reader.GetString(owner.Reader.GetMethodDefinition(handle).Name) == "GetEnumerator");
					var implementation = owner.GetConstructedMethod(implementationHandle, constructedDeclaringType, []);
					var forwarding = new FrameworkBinding(member, FrameworkBindingKind.ManagedBody, "managed:pinned-list-enumerable-dispatch",
						StringBuilderDecimalListSurface.Effects, Reason: "The actual released List nongeneric enumerable retains dispatch through its closed typed interface.", PreservesVirtualDispatch: true);
					return MethodReference.ForManagedBinding(forwarding, implementation, signature);
				}
			}
		}
		if (FrameworkImplementationProfile.IsExperimentalNumberGroupSizesReader(member,
				FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true))
			return ResolveExperimentalNumberGroupSizesReader(member, signature);
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) &&
			member.DeclaringType.Equals(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCustomNumberFormatting")) && member.Name == "Render")
			return ResolveExperimentalCustomNumberFormatting(member, signature);
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true ||
			FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && caller is not null && StringBuilderDecimalSurface.IsRuntimeCaller(DescribeImplementationCaller(caller))) &&
			member.DeclaringType.Equals(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowDecimalFormatting")))
		{
			if (member.Name is "RenderCustom" or "RenderStandard")
				return ResolveExperimentalCustomNumberFormatting(member, signature, member.Name == "RenderStandard");
			if (member.Name == "ParseFormatSpecifier")
			{
				var parser = GetOrLoadImplementationModule("System.Private.CoreLib")!
					.TryResolveManagedMethod("System.Private.CoreLib", "System.Number", "ParseFormatSpecifier", signature);
				if (parser is null) throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Decimal formatting requires the exact CoreLib format parser signature.");
				var shadow = new FrameworkShadowMethod("System.Private.CoreLib", "System.Number", parser.Name);
				var parserBinding = new FrameworkBinding(member, FrameworkBindingKind.ShadowMethod,
					$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
					new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
						FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory, [FrameworkFeature.Spans, FrameworkFeature.ManagedGc]),
					Reason: "Decimal formatting uses the pinned CoreLib format parser.", ShadowMethod: shadow,
					TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
				return MethodReference.ForShadowBinding(parserBinding, parser, signature);
			}
		}
		var context = new FrameworkBindingContext(
				typeName,
				signature,
				constructedDeclaringType,
				constructedDeclaringType is not null &&
					IsSupportedNullableType(constructedDeclaringType) && !IsExperimentalApplicationNullableElement(constructedDeclaringType.NullableElementType!),
				methodTypeArguments,
					methodTypeArguments?
						.Select(TypeContainsManagedReferences)
						.ToArray(),
					ResolveDefaultEqualityKind(
						member,
						constructedDeclaringType,
						methodTypeArguments),
				constructedDeclaringType?.GenericArguments
						.Select(TypeContainsManagedReferences)
						.ToArray());
		var lookupMember = FrameworkImplementationPack is null
			? member
			: FrameworkImplementationProfile.Canonicalize(member);
		if (TryCreatePinnedIntegralToStringBinding(member, caller, ilOffset, out var integralToStringBinding, out var integralToString))
			return MethodReference.ForManagedBinding(integralToStringBinding, integralToString, signature);
		if (TryCreatePinnedNullableToStringBinding(member, caller, ilOffset, out var nullableToStringBinding, out var nullableToString))
			return MethodReference.ForManagedBinding(nullableToStringBinding, nullableToString, signature);
		if (TryCreatePinnedDecimalScaleComparisonBinding(member, caller, ilOffset, out var scaleComparisonBinding, out var scaleComparison))
			return MethodReference.ForManagedBinding(scaleComparisonBinding, scaleComparison, signature);
		if (TryCreatePinnedCompositeBufferToStringBinding(member, caller, ilOffset, out var compositeToStringBinding, out var compositeToString))
			return MethodReference.ForManagedBinding(compositeToStringBinding, compositeToString, signature);
		if (TryCreatePinnedApplicationToStringBinding(member, caller, ilOffset, out var applicationToStringBinding, out var applicationToString))
			return MethodReference.ForManagedBinding(applicationToStringBinding, applicationToString, signature);
		if (TryCreatePinnedStringBuilderToStringBinding(member, caller, ilOffset, out var builderToStringBinding))
		{
			var implementation = TryResolveManagedMethod("System.Private.CoreLib", "System.Text.StringBuilder", "ToString", signature) ??
				throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "The verified StringBuilder ToString override is missing.");
			return MethodReference.ForManagedBinding(builderToStringBinding, implementation, signature);
		}
		var binding = FrameworkBindingRegistry.TryBind(lookupMember, context);
		if (TryCreatePinnedNumericSpanIdentityBinding(member, caller, ilOffset, out var enumerationBinding) || TryCreatePinnedApplicationFormattingLeafBinding(member, caller, ilOffset, out enumerationBinding) || TryCreatePinnedHandlerToStringBinding(member, caller, ilOffset, out enumerationBinding) || TryCreatePinnedGeneralObjectToStringBinding(member, caller, ilOffset, out enumerationBinding) ||
			TryCreateExperimentalJoinEnumerationBinding(member, caller, out enumerationBinding) ||
			TryCreatePinnedNullableListBinding(member, constructedDeclaringType, out enumerationBinding, caller) ||
			TryCreatePinnedNullableDependencyBinding(member, caller, binding, out enumerationBinding) ||
			TryCreateExperimentalListPrefixCopyBinding(member, caller, out enumerationBinding) ||
			TryCreateExperimentalCompositeSegmentCopyBinding(member, caller, out enumerationBinding) ||
			TryCreateExperimentalObjectJoinTextBinding(member, caller, out enumerationBinding) ||
			TryCreateExperimentalObjectJoinDispatchBinding(member, caller, out enumerationBinding) ||
			TryCreateExperimentalNumberGroupCloneBinding(member, caller, out enumerationBinding) ||
			TryCreatePinnedArrayMemoryStartConstructorBinding(member, caller, out enumerationBinding))
		{
			binding = enumerationBinding;
		}
		else if (FrameworkImplementationPack is not null &&
			(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(
				member, FrameworkImplementationPack,
				caller is { ModuleName: "System.Private.CoreLib" or "CopperSharp.Runtime.Managed" }
					? DescribeImplementationCaller(caller) : null,
				out var targetRuntimeOverride) ||
			FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
				member,
				FrameworkImplementationPack.EnableUnlistedManagedBodies,
				out targetRuntimeOverride)))
		{
			binding = targetRuntimeOverride;
		}
		else if (FrameworkImplementationPack is not null &&
			(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(
				member, binding, FrameworkImplementationPack,
				caller is { ModuleName: "System.Private.CoreLib" or "CopperSharp.Runtime.Managed" }
					? DescribeImplementationCaller(caller) : null,
				out var pinnedBinding) ||
			TryCreatePinnedGenericJoinInterfaceBinding(member, constructedDeclaringType, caller, binding, out pinnedBinding) ||
			TryCreatePinnedNullableMemberBinding(member, constructedDeclaringType, caller, binding, out pinnedBinding) ||
			FrameworkImplementationProfile.TryCreatePinnedBinding(
				member,
				binding,
				FrameworkImplementationPack.EnableUnlistedManagedBodies,
				out pinnedBinding)))
		{
			binding = pinnedBinding;
		}
		// A call-site override may also select a pinned body. Resolve it through
		// the same verified implementation path as profile-selected bodies.
		if (binding?.Kind == FrameworkBindingKind.PinnedManagedBody)
		{
			var implementation = ResolvePinnedImplementationMethod(
				binding,
				signature,
				constructedDeclaringType,
				methodTypeArguments);
			if (implementation is not null)
			{
				return MethodReference.ForManagedBinding(
					binding,
					implementation,
					signature);
			}
		}
		if (binding is null)
		{
			return null;
		}
		if (caller is not null &&
			ilOffset >= 0 &&
			IsConstrainedListEnumeratorDispose(binding, caller, ilOffset))
		{
			var disposeBinding =
				FrameworkBindingRegistry.BindListEnumeratorDispose(binding.Member);
			return MethodReference.ForBinding(disposeBinding, signature);
		}
		if (caller is not null &&
			ilOffset >= 0 &&
			IsOrderedEnumeratorDispose(binding, caller, ilOffset))
		{
			binding = FrameworkBindingRegistry.BindOrderedEnumeratorDispose(
				binding.Member);
		}
		if (binding.Kind == FrameworkBindingKind.ManagedBody)
		{
			if (caller is null || ilOffset < 0)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedPolymorphism,
					$"Framework managed binding '{binding.Member.DisplayName}' requires call-site receiver context.");
			}
			var implementation = ResolveClosedWorldSealedInterfaceCall(
				binding,
				caller,
				ilOffset,
				signature);
			return MethodReference.ForManagedBinding(binding, implementation, signature);
		}
		if (binding.Kind is
			FrameworkBindingKind.ShadowMethod or
			FrameworkBindingKind.PlatformOperation)
		{
			var shadowTarget = binding.ShadowMethod ??
				throw new InvalidOperationException(
					$"Managed framework binding '{binding.Member.DisplayName}' has no target.");
			var shadowMethodName = shadowTarget.MethodName;
			IReadOnlyList<CilType>? shadowMethodTypeArguments = null;
			MethodSignature<CilType>? shadowResolutionSignature = null;
			var genericJoinEnumeration = shadowTarget.TypeName is "CopperSharp.Runtime.ShadowGenericJoinEnumeration" or "CopperSharp.Runtime.ShadowGenericJoinEnumerator`1";
			CilType? genericJoinConstruction = null;
			if (genericJoinEnumeration)
			{
				if (caller?.MethodTypeArguments is not [var element] || !IsExperimentalGenericJoinElement(element))
					throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, "Generic join adapters require an admitted exact closed element type.");
				genericJoinConstruction = new CilType(CilTypeKind.ManagedReference, 4,
					$"CopperSharp.Runtime.ShadowGenericJoinEnumerator`1<{element.DisplayName}>", GenericArguments: [element]);
				if (shadowTarget.TypeName == "CopperSharp.Runtime.ShadowGenericJoinEnumeration")
				{
					shadowMethodTypeArguments = [element];
					shadowResolutionSignature = new MethodSignature<CilType>(new SignatureHeader(0), genericJoinConstruction, 1, 1, [constructedDeclaringType!]);
				}
			}
			if (shadowTarget is { TypeName: "CopperSharp.Runtime.ShadowCustomNumberFormatting", MethodName: "WriteTwoDigits" })
				shadowResolutionSignature = new MethodSignature<CilType>(new SignatureHeader(0), signature.ReturnType,
					signature.RequiredParameterCount, 0, signature.ParameterTypes);
			if (shadowTarget is { TypeName: "CopperSharp.Runtime.ShadowCharacterSpans", MethodName: "Identity" })
				shadowResolutionSignature = new MethodSignature<CilType>(new SignatureHeader(0), signature.ReturnType,
					signature.RequiredParameterCount, 0, signature.ParameterTypes);
			if (shadowTarget.TypeName == "CopperSharp.Runtime.ShadowFloatingPointBits")
				shadowResolutionSignature = new MethodSignature<CilType>(new SignatureHeader(0), signature.ReturnType,
					signature.RequiredParameterCount, 0, signature.ParameterTypes);
			if (shadowTarget.TypeName == "CopperSharp.Runtime.ShadowNumberFormatCharacters")
				shadowResolutionSignature = new MethodSignature<CilType>(new SignatureHeader(0), signature.ReturnType, 1, 0,
					[new CilType(CilTypeKind.ManagedReference, 4, "System.Globalization.NumberFormatInfo")]);
			var joinEnumeration = shadowTarget.TypeName is
				"CopperSharp.Runtime.ShadowStringJoinEnumeration" or "CopperSharp.Runtime.ShadowStringJoinEnumerator" or
				"CopperSharp.Runtime.ShadowObjectJoinEnumeration" or "CopperSharp.Runtime.ShadowObjectJoinEnumerator" or
				"CopperSharp.Runtime.ShadowInt32JoinEnumeration" or "CopperSharp.Runtime.ShadowInt32JoinEnumerator" or
				"CopperSharp.Runtime.ShadowDecimalJoinEnumeration" or "CopperSharp.Runtime.ShadowDecimalJoinEnumerator" or
				"CopperSharp.Runtime.ShadowSingleJoinEnumeration" or "CopperSharp.Runtime.ShadowSingleJoinEnumerator" or
				"CopperSharp.Runtime.ShadowDoubleJoinEnumeration" or "CopperSharp.Runtime.ShadowDoubleJoinEnumerator";
			var constructorFactory = shadowTarget is { TypeName: "CopperSharp.Runtime.ShadowStringData",
				MethodName: "FromCharacters" or "FromNullTerminatedCharacters" };
			if (shadowTarget is { TypeName: "CopperSharp.Runtime.ShadowArray", MethodName: "CloneInt32" })
				shadowResolutionSignature = new MethodSignature<CilType>(new SignatureHeader(0), signature.ReturnType, 1, 0,
					[new CilType(CilTypeKind.ManagedReference, 4, "System.Array")]);
			if (constructorFactory)
			{
				if (caller is not null && ilOffset >= 0 && caller.Instructions.Single(instruction => instruction.Offset == ilOffset).OpCode != System.Reflection.Emit.OpCodes.Newobj)
					throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedInstruction, "Target string constructor factories require newobj.", caller?.DisplayName, ilOffset);
				shadowResolutionSignature = new MethodSignature<CilType>(new SignatureHeader(0),
					new CilType(CilTypeKind.ManagedReference, 4, "string"), signature.RequiredParameterCount, 0, signature.ParameterTypes);
			}
			if (shadowTarget.TypeName is "CopperSharp.Runtime.ShadowStringJoinEnumeration" or "CopperSharp.Runtime.ShadowObjectJoinEnumeration" or "CopperSharp.Runtime.ShadowInt32JoinEnumeration" or "CopperSharp.Runtime.ShadowDecimalJoinEnumeration" or "CopperSharp.Runtime.ShadowSingleJoinEnumeration" or "CopperSharp.Runtime.ShadowDoubleJoinEnumeration")
			{
				// The private enumerator and the public interface use the same
				// reference ABI; the public call retains its original signature.
				shadowResolutionSignature = new MethodSignature<CilType>(new SignatureHeader(0),
					new CilType(CilTypeKind.ManagedReference, 4, shadowTarget.TypeName.Replace("Enumeration", "Enumerator", StringComparison.Ordinal)),
					1, 0, [constructedDeclaringType!]);
			}
			if (shadowTarget.TypeName == "CopperSharp.Runtime.ShadowStringBuilder" &&
				shadowTarget.MethodName == "AppendLine" && signature.Header.IsInstance)
			{
				// The newline adapter receives the official builder explicitly;
				// it owns no replacement StringBuilder layout or instance state.
				shadowResolutionSignature = new MethodSignature<CilType>(new SignatureHeader(0),
					signature.ReturnType, signature.RequiredParameterCount + 1, 0,
					[signature.ReturnType, .. signature.ParameterTypes]);
			}
			if (shadowTarget.TypeName ==
					"CopperSharp.Runtime.ShadowStringFormat" &&
				shadowTarget.MethodName is
					"FormatParams" or "FormatArguments" or "FormatSpanParams")
			{
				var elementTypes = caller is not null && ilOffset >= 0
					? shadowTarget.MethodName == "FormatParams"
						? EphemeralParamsArrayAnalyzer.AnalyzeBoxedValueArray(
							this,
							caller,
							ilOffset,
							minimumLength: 1,
							maximumLength: 8)?.ElementTypes
						: shadowTarget.MethodName == "FormatSpanParams"
							? EphemeralParamsArrayAnalyzer.AnalyzeReadOnlySpanParams(
								this,
								caller,
								ilOffset)?.ElementTypes
						: EphemeralParamsArrayAnalyzer.AnalyzeImmediateBoxedArguments(
							this,
							caller,
							ilOffset,
							signature.ParameterTypes.Length - 1)
					: null;
				if (elementTypes is null)
				{
					var provenanceRequirement = shadowTarget.MethodName switch
					{
						"FormatParams" =>
							"an immediate fresh object array containing one to eight unaliased boxed Int32 values",
						"FormatSpanParams" =>
							"an immediate compiler-generated ReadOnlySpan containing one to eight unaliased boxed Int32 values",
						_ =>
							"one to eight immediate, unaliased boxed Int32 values"
					};
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedInstruction,
						$"The admitted string.Format slice requires {provenanceRequirement} and no control-flow entries.",
						caller?.DisplayName,
						ilOffset >= 0 ? ilOffset : null);
				}
				shadowMethodName = $"Format{elementTypes.Count}";
				shadowResolutionSignature = new MethodSignature<CilType>(
					signature.Header,
					signature.ReturnType,
					requiredParameterCount: 1 + elementTypes.Count,
					genericParameterCount: 0,
					[signature.ParameterTypes[0], .. elementTypes]);
			}
			else if (shadowTarget is
			{
				TypeName: "CopperSharp.Runtime.ShadowEnumerable",
				MethodName: "ToArray"
				})
			{
				shadowMethodName = ResolveShadowEnumerableMaterializer(
					binding,
					caller,
					ilOffset);
			}
			else if (shadowTarget is
				{
					TypeName: "CopperSharp.Runtime.ShadowEnumerable",
					MethodName: "Select"
				})
			{
				if (caller is null || ilOffset < 0 ||
					EnumerableSourceProvenanceAnalyzer.Analyze(
						this,
						caller,
						ilOffset,
						argumentFromTop: 1) != EnumerableSourceProvenance.Range)
				{
					throw EnumerableSourceDiagnostic(
						binding,
						caller,
						ilOffset,
						"the selected Select slice requires exact Range source provenance");
				}
				shadowMethodName = "SelectInt32";
			}
			else if (shadowTarget is
				{
					TypeName: "CopperSharp.Runtime.ShadowEnumerable",
					MethodName: "Where"
				})
			{
				if (caller is null || ilOffset < 0)
				{
					throw EnumerableSourceDiagnostic(
						binding,
						caller,
						ilOffset,
						"call-site context is unavailable");
				}
				shadowMethodName = EnumerableSourceProvenanceAnalyzer.Analyze(
					this,
					caller,
					ilOffset,
					argumentFromTop: 1) switch
				{
					EnumerableSourceProvenance.Range => "RangeWhereInt32",
					EnumerableSourceProvenance.RangeSelect => "SelectWhereInt32",
					_ => throw EnumerableSourceDiagnostic(
						binding,
						caller,
						ilOffset,
						"the selected Where slice requires exact Range or Range.Select source provenance")
				};
			}
			else if (shadowTarget is
				{
					TypeName: "CopperSharp.Runtime.ShadowEnumerable",
					MethodName: "Any" or "AnyPredicate"
				})
			{
				shadowMethodName = ResolveShadowEnumerableAny(
					binding,
					caller,
					ilOffset,
					withPredicate: shadowTarget.MethodName == "AnyPredicate");
			}
		else if (shadowTarget is
			{
				TypeName: "CopperSharp.Runtime.ShadowEnumerable",
				MethodName: "Take"
				})
			{
				shadowMethodName = ResolveShadowEnumerableTake(
					binding,
				caller,
				ilOffset);
		}
		else if (shadowTarget is
		{
			TypeName: "CopperSharp.Runtime.ShadowEnumerable",
			MethodName: "Sum" or "SumSelector"
			})
		{
			shadowMethodName = ResolveShadowEnumerableSum(
				binding,
				caller,
				ilOffset,
				withSelector: shadowTarget.MethodName == "SumSelector",
				methodTypeArguments);
		}
		else if (shadowTarget is
		{
			TypeName: "CopperSharp.Runtime.ShadowEnumerable",
			MethodName: "OrderBy" or "ThenBy"
		})
		{
			shadowMethodName = ResolveShadowEnumerableOrdering(
				binding,
				caller,
				ilOffset,
				isThenBy: shadowTarget.MethodName == "ThenBy");
		}
		else if (shadowTarget is
		{
			TypeName: "CopperSharp.Runtime.ShadowOrderedEnumerable`1",
			MethodName: "GetEnumerator"
		})
		{
			RequireEnumerableProvenance(
				binding,
				caller,
				ilOffset,
				EnumerableSourceProvenance.OrderedPrimarySecondary,
				"the selected ordered foreach slice requires an exact OrderBy-ThenBy receiver");
		}
		else if (shadowTarget is
		{
			TypeName: "CopperSharp.Runtime.ShadowOrderedEnumerator`1",
			MethodName: "get_Current"
		} or
		{
			TypeName: "CopperSharp.Runtime.ShadowOrderedEnumeratorBase",
			MethodName: "MoveNext" or "Dispose"
		})
		{
			if (shadowTarget.MethodName != "Dispose" || caller is null ||
				!HasExactOrderedEnumeratorLocalReceiver(caller, ilOffset))
			{
				RequireEnumerableProvenance(
					binding,
					caller,
					ilOffset,
					EnumerableSourceProvenance.OrderedEnumerator,
					"the selected ordered foreach slice requires an exact private ordered enumerator");
			}
		}
			if (shadowTarget is { TypeName: "CopperSharp.Runtime.ShadowSystemResources", MethodName: "GetFormattingResourceString" })
				shadowResolutionSignature = new MethodSignature<CilType>(signature.Header, signature.ReturnType, 1, 0, [new CilType(CilTypeKind.SignedInteger, 4, "int")]);
			if (shadowTarget.TypeName == "CopperSharp.Runtime.ShadowEnumFormatting")
			{
				if (methodTypeArguments is not [var enumArgument] || !enumArgument.IsEnum ||
					enumArgument.Kind is not (CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger) || enumArgument.Size is not (1 or 2 or 4 or 8))
					throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, "Target enum formatting requires a constructed integral enum argument.");
				shadowMethodTypeArguments = methodTypeArguments;
			}
			if (shadowTarget.TypeName == "CopperSharp.Runtime.ShadowArray" ||
				(shadowTarget.TypeName == "CopperSharp.Runtime.ShadowEnumerable" &&
				 shadowMethodName is "Repeat" or "RepeatToArray" or "ArraySumSelector" or
					 "DictionaryUInt32ValuesOrderBy" or "DictionaryUInt32ValuesThenBy"))
			{
				shadowMethodTypeArguments = methodTypeArguments;
			}
			if (shadowTarget.TypeName == "CopperSharp.Runtime.ShadowObject" &&
				(shadowTarget.MethodName.StartsWith(
					"DefaultEquals",
					StringComparison.Ordinal) ||
				 shadowTarget.MethodName == "DefaultHashCodeObject"))
			{
				shadowMethodTypeArguments =
					shadowTarget.MethodName == "DefaultEqualsNullable" &&
					methodTypeArguments is
					[
						{ NullableElementType: { } nullableElement }
					]
						? [nullableElement]
						: methodTypeArguments;
			}
			var shadowMethod = TryResolveManagedMethod(
				shadowTarget.AssemblyName,
				shadowTarget.TypeName,
				shadowMethodName,
				shadowResolutionSignature ?? signature,
				genericJoinEnumeration ? (shadowTarget.TypeName == "CopperSharp.Runtime.ShadowGenericJoinEnumeration" ? null : genericJoinConstruction) :
					joinEnumeration ? null : constructedDeclaringType,
				shadowMethodTypeArguments) ??
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidInput,
					$"Managed framework binding target '{binding.Target}' could not be resolved from the supplied target runtime assemblies.");
			if (shadowTarget.TypeName == "CopperSharp.Runtime.ShadowInterpolatedStringHandlerBuffer")
				ValidateInterpolatedHandlerStorageLayout(shadowMethod);
			CilTypeLayout? allocationLayout = null;
			if (shadowTarget.TypeName == "CopperSharp.Runtime.ShadowCultureInfo")
			{
				var cultureLayout = ValidateCultureStorageLayout(shadowMethod);
				if (binding.Member.Name == ".ctor") allocationLayout = cultureLayout;
			}
			if (shadowTarget.TypeName == "CopperSharp.Runtime.ShadowBoxedEnum")
			{
				var layout = GetTypeLayout(shadowMethod);
				var owner = GetModule(layout.ModuleName);
				var fields = owner.Reader.GetTypeDefinition(layout.Handle).GetFields().Select(owner.GetField).Where(field => !field.IsStatic).ToArray();
				if (layout.Size != 16 || layout.ReferenceBitmap != 0 || fields.Length != 2 ||
					fields.Where((field, index) => field.Type is not { Kind: CilTypeKind.UnsignedInteger, Size: 4, DisplayName: "uint" } ||
						owner.Reader.GetString(owner.Reader.GetFieldDefinition(field.Handle).Name) != (index == 0 ? "_first" : "_second") ||
						layout.FieldOffsets[field.Handle] != 8 + index * 4).Any())
					throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Boxed enum adapter requires the target two-lane payload layout.");
			}
			if (binding.PreservesVirtualDispatch)
			{
				RegisterFrameworkVirtualFallback(binding, shadowMethod);
			}
			return MethodReference.ForShadowBinding(binding, shadowMethod, signature) with
			{
				IsConstructorFactory = constructorFactory,
				AllocationLayout = allocationLayout
			};
		}
		var boundSignature = binding.Target is
			"intrinsic:delegate-ctor" or "intrinsic:delegate-invoke" &&
			constructedDeclaringType is { GenericArguments.Length: > 0 } delegateType
				? SubstituteTypeArguments(signature, delegateType.GenericArguments)
				: signature;
		return MethodReference.ForBinding(
			binding,
			boundSignature,
			binding.Target == "intrinsic:readonly-span-from-ref-length:object" ? boundSignature.ReturnType : constructedDeclaringType);
	}

	internal CilField GetExperimentalReadOnlyField(string typeName)
	{
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) ||
			typeName is not ("System.Globalization.CultureInfo" or "System.Globalization.NumberFormatInfo"))
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Read-only provider access requires the experimental verified framework identity.");
		var field = ResolveManagedField("System.Private.CoreLib", typeName, "_isReadOnly");
		var layout = GetTypeLayout(field);
		if (field.ModuleName != "System.Private.CoreLib" || field.IsStatic || field.Type.DisplayName != "bool" || field.Type.Size != 1 ||
			!layout.FieldOffsets.TryGetValue(field.Handle, out var offset) || offset < 8 || offset > short.MaxValue || offset + 4 > layout.Size)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Read-only provider field has an unexpected target layout.");
		return field;
	}

	private CilTypeLayout ValidateCultureStorageLayout(CilMethod shadowMethod)
	{
		var target = ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, "System.Globalization.CultureInfo"), "System.Private.CoreLib");
		var official = GetRuntimeTypeLayout(target);
		var shadow = GetTypeLayout(shadowMethod);
		var names = new[] { "_isReadOnly", "_compareInfo", "_textInfo", "_numInfo", "_dateTimeInfo", "_calendar", "_cultureData",
			"_isInherited", "_consoleFallbackCulture", "_name", "_nonSortName", "_sortName", "_parent" };
		var types = new[] { "bool", "System.Globalization.CompareInfo", "System.Globalization.TextInfo", "System.Globalization.NumberFormatInfo",
			"System.Globalization.DateTimeFormatInfo", "System.Globalization.Calendar", "System.Globalization.CultureData", "bool",
			"System.Globalization.CultureInfo", "string", "string", "string", "System.Globalization.CultureInfo" };
		foreach (var layout in new[] { official, shadow })
		{
			var owner = GetModule(layout.ModuleName);
			var fields = owner.Reader.GetTypeDefinition(layout.Handle).GetFields().Select(owner.GetField).Where(field => !field.IsStatic).ToArray();
			if (layout.Size != 60 || layout.ReferenceBitmap != 0x1F7E || fields.Length != names.Length ||
				fields.Where((field, index) => owner.Reader.GetString(owner.Reader.GetFieldDefinition(field.Handle).Name) != names[index] ||
					layout.FieldOffsets[field.Handle] != 8 + index * 4 ||
					field.Type.DisplayName != (layout.Identity == shadow.Identity && index == 6 ? "object" : types[index]) ||
					(index is 0 or 7 ? field.Type.DisplayName != "bool" : !field.Type.IsReference)).Any())
				throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "CultureInfo adapter requires the verified thirteen-field target layout.");
		}
		return official;
	}

	private void ValidateInterpolatedHandlerStorageLayout(CilMethod shadowMethod)
	{
		var corelib = GetOrLoadImplementationModule("System.Private.CoreLib")!;
		var type = new CilType(CilTypeKind.ValueType, 0, "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler");
		var identity = corelib.ResolveRuntimeTypeIdentity(type, corelib.AssemblyName);
		var official = corelib.GetRuntimeTypeLayout(identity);
		var shadow = GetTypeLayout(shadowMethod);
		foreach (var layout in new[] { official, shadow })
		{
			var module = GetModule(layout.ModuleName);
			var fields = module.Reader.GetTypeDefinition(layout.Handle).GetFields().Select(module.GetField)
				.Where(static field => !field.IsStatic).ToArray();
			var names = new[] { "_provider", "_arrayToReturnToPool", "_chars", "_pos", "_hasCustomFormatter" };
			var types = new[] { "System.IFormatProvider", "char[]", "System.Span`1<char>", "int", "bool" };
			var offsets = new[] { 0, 4, 8, 20, 24 };
			if (layout.Size != 28 || layout.ReferenceBitmap != 19 || fields.Length != names.Length ||
				fields.Where((field, index) => module.Reader.GetString(module.Reader.GetFieldDefinition(field.Handle).Name) != names[index] || field.Type.DisplayName != types[index] ||
					layout.FieldOffsets[field.Handle] != offsets[index]).Any())
				throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Interpolated handler temporary-storage binding requires the verified seven-word field layout.");
		}
	}

	internal bool TryCreateExperimentalNumberGroupCloneBinding(FrameworkMemberId member, CilMethod? caller, out FrameworkBinding binding)
	{
		binding = null!;
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) || caller is null ||
			caller.ModuleName != "System.Private.CoreLib" || caller.Signature.Header.RawValue != 0x20 ||
			!caller.MethodTypeArguments.IsDefaultOrEmpty || caller.ConstructedDeclaringType is not null ||
			caller.Signature.GenericParameterCount != 0) return false;
		var getter = caller.DisplayName is "System.Globalization.NumberFormatInfo::get_NumberGroupSizes" or "System.Globalization.NumberFormatInfo::get_CurrencyGroupSizes" or "System.Globalization.NumberFormatInfo::get_PercentGroupSizes" &&
			caller.Signature.RequiredParameterCount == 0 && caller.Signature.ParameterTypes.Length == 0 &&
			caller.Signature.ReturnType is { Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "int[]", ElementType: { Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" } };
		var setter = caller.DisplayName is "System.Globalization.NumberFormatInfo::set_NumberGroupSizes" or "System.Globalization.NumberFormatInfo::set_CurrencyGroupSizes" or "System.Globalization.NumberFormatInfo::set_PercentGroupSizes" &&
			caller.Signature.RequiredParameterCount == 1 && caller.Signature.ParameterTypes is
			[{ Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "int[]", ElementType: { Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" } }] && caller.Signature.ReturnType.IsVoid;
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true &&
			!StringBuilderNumberSettingsSurface.IsPublic(DescribeImplementationCaller(caller))) return false;
		member = FrameworkImplementationProfile.Canonicalize(member);
		if (!(getter || setter) || !member.DeclaringType.Equals(FrameworkTypeId.Named("System.Runtime", "System.Array")) ||
			member.Name != "Clone" || member.MethodTypeArguments.Length != 0 || member.Signature.Header != 0x20 ||
			member.Signature.GenericParameterCount != 0 || member.Signature.RequiredParameterCount != 0 ||
			member.Signature.ParameterTypes.Length != 0 || !member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Object"))) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray", "CloneInt32");
		binding = new FrameworkBinding(member, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory |
				FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect,
				[FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc]),
			Reason: "The verified NumberFormatInfo group-size accessors clone Int32 arrays using the target heap.", ShadowMethod: shadow);
		return true;
	}

	internal bool TryCreateExperimentalJoinEnumerationBinding(
		FrameworkMemberId referencedMember, CilMethod? caller, out FrameworkBinding binding)
	{
		binding = null!;
		var elementName = caller?.DisplayName switch
		{
			"System.Text.StringBuilder::AppendJoinCore<string>" => "string",
			"System.Text.StringBuilder::AppendJoinCore<object>" => "object",
			"System.Text.StringBuilder::AppendJoinCore<int>" => "int",
			"System.Text.StringBuilder::AppendJoinCore<System.Decimal>" => "System.Decimal",
			"System.Text.StringBuilder::AppendJoinCore<float>" => "float",
			"System.Text.StringBuilder::AppendJoinCore<double>" => "double",
			_ => null
		};
		if (elementName is null) return TryCreateExperimentalGenericJoinEnumerationBinding(referencedMember, caller, out binding);
		if (!(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true ||
			FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && elementName is "string" or "int" or "object" or "System.Decimal" or "float" or "double") ||
			caller is not { ModuleName: "System.Private.CoreLib" } ||
			caller.MethodTypeArguments is not [var argument] || argument.DisplayName != elementName ||
			(elementName == "int" ? argument is not { Kind: CilTypeKind.SignedInteger, Size: 4 } :
			 elementName == "float" ? argument is not { Kind: CilTypeKind.FloatingPoint, Size: 4 } :
			 elementName == "double" ? argument is not { Kind: CilTypeKind.FloatingPoint, Size: 8 } :
			 elementName == "System.Decimal" ? argument is not { Kind: CilTypeKind.ValueType, Size: 0 } || !TryGetDecimalLayout(argument, out _) :
			 argument is not { Kind: CilTypeKind.ManagedReference, Size: 4 }) ||
			!caller.Signature.Header.IsInstance || caller.Signature.GenericParameterCount != 1 || caller.Signature.RequiredParameterCount != 3 ||
			caller.Signature.ReturnType.DisplayName != "System.Text.StringBuilder" ||
			caller.Signature.ParameterTypes is not
			[
				{ Kind: CilTypeKind.ManagedPointer, ElementType.DisplayName: "char" },
				{ DisplayName: "int" },
				{ Kind: CilTypeKind.ManagedReference } source
			] || source.DisplayName != $"System.Collections.Generic.IEnumerable`1<{elementName}>") return false;
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput == true &&
			!StringBuilderFrameworkSurface.Helpers.Any(helper => helper.Name == "AppendJoinCore" && helper.Signature.GenericParameterCount == 1 &&
				helper.Equals(FrameworkImplementationProfile.Canonicalize(GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, []))))) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referencedMember);
		if (member.MethodTypeArguments.Length != 0 || member.Signature.Header != 0x20 ||
			member.Signature.GenericParameterCount != 0 || member.Signature.RequiredParameterCount != 0 ||
			member.Signature.ParameterTypes.Length != 0) return false;
		var text = elementName == "System.Decimal" ? FrameworkTypeId.Named("System.Runtime", "System.Decimal") :
			FrameworkTypeId.Primitive(elementName switch { "string" => "System.String", "int" => "System.Int32", "float" => "System.Single", "double" => "System.Double", _ => "System.Object" });
		var genericElement = FrameworkTypeId.GenericTypeParameter(0);
		var enumerable = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerable`1"), [text]);
		var enumerator = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerator`1"), [text]);
		var methodEnumerable = FrameworkTypeId.GenericInstantiation(enumerable.ElementType!, [FrameworkTypeId.GenericMethodParameter(0)]);
		var methodEnumerator = FrameworkTypeId.GenericInstantiation(enumerator.ElementType!, [FrameworkTypeId.GenericMethodParameter(0)]);
		var openEnumerator = FrameworkTypeId.GenericInstantiation(enumerator.ElementType!, [genericElement]);
		string? name = null;
		var factory = false;
		if ((member.DeclaringType.Equals(enumerable) || member.DeclaringType.Equals(methodEnumerable)) && member.Name == "GetEnumerator" && member.Signature.ReturnType.Equals(openEnumerator))
		{
			name = "GetEnumerator"; factory = true;
		}
		else if ((member.DeclaringType.Equals(enumerator) || member.DeclaringType.Equals(methodEnumerator)) && member.Name == "get_Current" && member.Signature.ReturnType.Equals(genericElement)) name = "get_Current";
		else if (member.DeclaringType.Equals(FrameworkTypeId.Named("System.Runtime", "System.Collections.IEnumerator")) &&
			member.Name == "MoveNext" && member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Boolean"))) name = "MoveNext";
		else if (member.DeclaringType.Equals(FrameworkTypeId.Named("System.Runtime", "System.IDisposable")) &&
			member.Name == "Dispose" && member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void"))) name = "Dispose";
		if (name is null) return false;
		var prefix = "CopperSharp.Runtime.Shadow" + (elementName switch { "string" => "String", "int" => "Int32", "System.Decimal" => "Decimal", "float" => "Single", "double" => "Double", _ => "Object" }) + "Join";
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", prefix + (factory ? "Enumeration" : "Enumerator"), name);
		var effects = factory ? FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow |
			FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory
			: name == "MoveNext" ? FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory
			: name == "Dispose" ? FrameworkEffects.WritesManagedMemory : FrameworkEffects.ReadsManagedMemory;
		if (elementName is "string" or "object" or "int" or "System.Decimal" or "float" or "double")
			effects = FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory;
		binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{name}",
			new FrameworkEffectSummary(effects, [FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc]),
			Reason: elementName is "string" or "object" or "int" or "System.Decimal" or "float" or "double" ? $"Exact CoreLib {elementName} join enumeration over arrays, lists and delegated custom enumerators." :
				$"Exact CoreLib {elementName} join enumeration over target arrays and lists.", ShadowMethod: shadow);
		return true;
	}

	internal bool IsPinnedIntegralTryFormatMethod(CilMethod method) =>
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && method.ModuleName == "System.Private.CoreLib" &&
		FrameworkImplementationProfile.IsIntegralTryFormatCaller(GetModule(method.ModuleName).DescribeFrameworkMethodDefinition(method.Handle, []));

	internal bool IsPinnedDecimalTryFormatMethod(CilMethod method)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || method.ModuleName != "System.Private.CoreLib" ||
			method.Name != "TryFormat" || !method.MethodTypeArguments.IsDefaultOrEmpty || method.ConstructedDeclaringType is not null) return false;
		var member = GetModule(method.ModuleName).DescribeFrameworkMethodDefinition(method.Handle, []);
		return member.Name == "TryFormat" && StringBuilderDecimalSurface.IsPublic(member) &&
			TryGetDecimalLayout(new CilType(CilTypeKind.ValueType, 0, "System.Decimal"), out _);
	}

	internal bool IsPinnedFloatingTryFormatMethod(CilMethod method) =>
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && method.ModuleName == "System.Private.CoreLib" &&
		method.Name == "TryFormat" && method.MethodTypeArguments.IsDefaultOrEmpty && method.ConstructedDeclaringType is null &&
		StringBuilderFloatingSurface.IsPublic(GetModule(method.ModuleName).DescribeFrameworkMethodDefinition(method.Handle, []));

	private bool IsPinnedFloatingFormattingCaller(CilMethod caller)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || caller.ModuleName != "System.Private.CoreLib" ||
			caller.MethodTypeArguments is not [{ IsEnum: false } argument] ||
			argument is not ({ Kind: CilTypeKind.FloatingPoint, Size: 4, DisplayName: "float" } or { Kind: CilTypeKind.FloatingPoint, Size: 8, DisplayName: "double" })) return false;
		var definition = FrameworkImplementationProfile.Canonicalize(GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, []));
		return StringBuilderFrameworkSurface.Helpers.Any(member => member.Name is "AppendSpanFormattable" or "InsertSpanFormattable" && definition.Equals(member)) || IsPinnedHandlerFormattingCaller(caller);
	}

	private bool IsPinnedIntegralFormattingCaller(CilMethod caller)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || caller.ModuleName != "System.Private.CoreLib" ||
			caller.MethodTypeArguments is not [var argument] ||
			argument.Kind is not (CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger) ||
			argument.DisplayName is not ("sbyte" or "byte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong") ||
			argument.Size is not (1 or 2 or 4 or 8)) return false;
		var definition = FrameworkImplementationProfile.Canonicalize(GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, []));
		return StringBuilderFrameworkSurface.Helpers.Any(member => member.Name is "AppendSpanFormattable" or "InsertSpanFormattable" &&
			definition.Equals(member)) || IsPinnedHandlerFormattingCaller(caller);
	}

	internal bool IsPinnedHandlerFormattingCaller(CilMethod caller)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || caller.ModuleName != "System.Private.CoreLib" ||
			caller.MethodTypeArguments.Length != 1) return false;
		var member = GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, []);
		return member.Name == "AppendFormatted" && member.Signature.GenericParameterCount == 1 &&
			(StringBuilderFrameworkSurface.TryGetEffects(member, out _) || StringBuilderInterpolatedHandlerSurface.Contains(member));
	}

	private bool IsPinnedDecimalFormattingCaller(CilMethod caller)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || caller.ModuleName != "System.Private.CoreLib" ||
			caller.MethodTypeArguments is not [{ Kind: CilTypeKind.ValueType, DisplayName: "System.Decimal" } type] || !TryGetDecimalLayout(type, out _)) return false;
		var definition = FrameworkImplementationProfile.Canonicalize(GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, []));
		return StringBuilderFrameworkSurface.Helpers.Any(member => member.Name is "AppendSpanFormattable" or "InsertSpanFormattable" or "AppendJoinCore" && definition.Equals(member)) || IsPinnedHandlerFormattingCaller(caller);
	}

	internal bool TryCreatePinnedHandlerToStringBinding(FrameworkMemberId referenced, CilMethod? caller, int ilOffset, out FrameworkBinding binding)
	{
		binding = null!;
		if (caller is null || FrameworkImplementationPack?.EnableUnlistedManagedBodies != false || !IsPinnedHandlerFormattingCaller(caller) ||
			caller.MethodTypeArguments is not [{ Kind: CilTypeKind.ManagedReference, Size: 4 } type] ||
			caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset) is not { OpCode: var opcode, ConstrainedTypeToken: { } token } ||
			opcode != OpCodes.Callvirt || !ResolveTypeToken(token, caller, ilOffset).Equals(type)) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		if (!member.Equals(new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Object"), "ToString",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), [])))) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinText", "ToString");
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod, $"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]),
			Reason: "The exact reference-type handler fallback dereferences its constrained receiver and preserves object formatting dispatch.", ShadowMethod: shadow);
		return true;
	}

	internal bool TryCreatePinnedFormattingResourceBinding(FrameworkMemberId referenced, CilMethod caller, out FrameworkBinding binding)
	{
		binding = null!;
		if (FrameworkImplementationPack is not { } catalog || !(catalog.IsPinnedStringBuilderInput || catalog.EnableUnlistedManagedBodies) ||
			!FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(referenced, catalog,
				GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, []), out var candidate) ||
			candidate.ShadowMethod is not ({ TypeName: "CopperSharp.Runtime.ShadowSystemResources", MethodName: "GetFormattingResourceString" } or
				{ TypeName: "CopperSharp.Runtime.ShadowRuntimeType", MethodName: "GetObjectFallbackName" })) return false;
		binding = candidate;
		return true;
	}

	internal bool TryCreatePinnedArrayMemoryStartConstructorBinding(FrameworkMemberId referenced, CilMethod? caller, out FrameworkBinding binding)
	{
		binding = null!;
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || caller?.ModuleName != "System.Private.CoreLib" ||
			caller.MethodTypeArguments is not [{ Kind: CilTypeKind.Character, Size: 2, DisplayName: "char" }]) return false;
		var definition = GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, [FrameworkTypeId.Primitive("System.Char")]);
		if (!FrameworkImplementationProfile.IsCharacterArrayMemoryProjection(definition)) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var owner = member.DeclaringType;
		if (owner.Kind != FrameworkTypeKind.GenericInstantiation || !owner.ElementType!.Equals(FrameworkTypeId.Named("System.Runtime", "System.Memory`1")) ||
			owner.GenericArguments.Length != 1 || !(owner.GenericArguments[0].Equals(FrameworkTypeId.GenericMethodParameter(0)) || owner.GenericArguments[0].Equals(FrameworkTypeId.Primitive("System.Char"))) ||
			member.Name != ".ctor" || member.MethodTypeArguments.Length != 0 || member.Signature.Header != 0x20 || member.Signature.GenericParameterCount != 0 ||
			member.Signature.RequiredParameterCount != 2 || !member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) ||
			!member.Signature.ParameterTypes.SequenceEqual([FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0)), FrameworkTypeId.Primitive("System.Int32")])) return false;
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.Intrinsic, "intrinsic:memory-from-array-start:char",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory,
				[FrameworkFeature.ManagedMemory, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc]),
			Reason: "The verified character-array AsMemory wrapper uses its exact internal start constructor with retained ownership and bounds checks.");
		return true;
	}

	private bool IsPinnedPrimitiveJoinCaller(CilMethod caller) =>
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && caller.ModuleName == "System.Private.CoreLib" &&
		caller.MethodTypeArguments is [var argument] && (IsPinnedGenericJoinElement(argument) || argument is
			{ Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "string" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" } or
			{ IsEnum: false, Kind: CilTypeKind.FloatingPoint, Size: 4, DisplayName: "float" } or
			{ IsEnum: false, Kind: CilTypeKind.FloatingPoint, Size: 8, DisplayName: "double" } or
			{ IsEnum: false, Kind: CilTypeKind.Boolean, Size: 1, DisplayName: "bool" } or
			{ IsEnum: false, Kind: CilTypeKind.Character, Size: 2, DisplayName: "char" }) &&
		StringBuilderFrameworkSurface.Helpers.Any(member => member.Name == "AppendJoinCore" && member.Signature.GenericParameterCount == 1 &&
			member.Equals(FrameworkImplementationProfile.Canonicalize(GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, []))));

	internal bool TryCreatePinnedDecimalScaleComparisonBinding(
		FrameworkMemberId referenced, CilMethod? caller, int ilOffset, out FrameworkBinding binding, out CilMethod implementation)
	{
		binding = null!; implementation = null!;
		if (FrameworkImplementationPack is { IsPinnedStringBuilderInput: true, EnableUnlistedManagedBodies: false } && caller is not null &&
			caller.MethodTypeArguments is [{ Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" }] &&
			StringBuilderDecimalSurface.IsValidationCaller(DescribeImplementationCaller(caller)) &&
			StringBuilderDecimalSurface.IsComparisonDependency(referenced) &&
			caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset) is { OpCode: var comparisonOpcode, ConstrainedTypeToken: { } comparisonToken } &&
			comparisonOpcode == OpCodes.Callvirt && ResolveTypeToken(comparisonToken, caller, ilOffset) is { Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" })
		{
			implementation = TryResolveManagedMethod("System.Private.CoreLib", "System.Int32", "CompareTo",
				new MethodSignature<CilType>(new SignatureHeader(0x20), new CilType(CilTypeKind.SignedInteger, 4, "int"), 1, 0, [new CilType(CilTypeKind.SignedInteger, 4, "int")]))!;
			if (implementation is null) return false;
			binding = new FrameworkBinding(referenced, FrameworkBindingKind.ManagedBody, "managed:decimal-scale-comparison",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory, []), Reason: "The released decimal scale validator calls the exact closed Int32 comparison.");
			return true;
		}
		return false;
	}

	internal bool TryCreatePinnedIntegralToStringBinding(
		FrameworkMemberId referenced, CilMethod? caller, int ilOffset, out FrameworkBinding binding, out CilMethod implementation)
	{
		binding = null!; implementation = null!;
		if (caller is null || (!IsPinnedIntegralFormattingCaller(caller) && !IsPinnedFloatingFormattingCaller(caller) && !IsPinnedDecimalFormattingCaller(caller) && !IsPinnedPrimitiveJoinCaller(caller) && !IsPinnedNullableToStringCaller(caller) && !IsPinnedApplicationScalarFormattingCaller(caller) && !IsPinnedApplicationValueFormattingCaller(caller)) ||
			FrameworkImplementationPack?.EnableUnlistedManagedBodies != false) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var slot = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Object"), "ToString",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []));
		if (!member.Equals(slot) || caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset) is not
			{ OpCode: var opcode, ConstrainedTypeToken: { } token } || opcode != OpCodes.Callvirt) return false;
		var owner = GetModule("System.Private.CoreLib");
		var declaration = owner.TryResolveManagedMethod("System.Private.CoreLib", "System.Object", "ToString",
			new MethodSignature<CilType>(new SignatureHeader(0x20), new CilType(CilTypeKind.ManagedReference, 4, "string"), 0, 0, []));
		if (declaration is null || !(TryResolveExperimentalConstrainedPrimitiveToString(caller, token, ilOffset, declaration, out implementation) ||
			(IsPinnedPrimitiveJoinCaller(caller) || IsPinnedNullableToStringCaller(caller) || IsPinnedApplicationScalarFormattingCaller(caller)) && TryResolveExperimentalConstrainedEnumToString(caller, token, ilOffset, declaration, out implementation) ||
			IsPinnedPrimitiveJoinCaller(caller) && TryResolveExperimentalConstrainedNullableToString(caller, token, ilOffset, declaration, out implementation) ||
			TryResolveExperimentalConstrainedStringToString(caller, token, ilOffset, declaration, out implementation) ||
			(IsPinnedDecimalFormattingCaller(caller) || IsPinnedPrimitiveJoinCaller(caller) || IsPinnedNullableToStringCaller(caller) || IsPinnedApplicationScalarFormattingCaller(caller) || IsPinnedApplicationValueFormattingCaller(caller)) && TryResolveExperimentalConstrainedStructToString(caller, token, ilOffset, declaration, out implementation))) return false;
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ManagedBody, IsPinnedPrimitiveJoinCaller(caller) ? "managed:constrained-join-tostring" : "managed:constrained-integral-tostring",
			new FrameworkEffectSummary(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]),
			Reason: "The audited scalar formatting or join fallback selects its exact CoreLib ToString override.");
		return true;
	}

	internal bool TryCreatePinnedGeneralObjectToStringBinding(FrameworkMemberId referenced, CilMethod? caller, int ilOffset, out FrameworkBinding binding)
	{
		binding = null!;
		if (FrameworkImplementationPack is not { IsPinnedStringBuilderInput: true, EnableUnlistedManagedBodies: false } || caller is null ||
			caller.ModuleName is "System.Private.CoreLib" or "CopperSharp.Runtime.Managed" or "CopperSharp.Compiler" || Net10FrameworkContract.Default.IsFrameworkAssembly(caller.ModuleName) ||
			caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset) is not { OpCode: var opcode, ConstrainedTypeToken: null } || opcode != OpCodes.Callvirt) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		if (!member.Equals(new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Object"), "ToString",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), [])))) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinText", "ToString");
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod, $"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]),
			Reason: "Object calls use primitive conversion, reachable application overrides and canonical type-name fallback.", ShadowMethod: shadow);
		return true;
	}

	internal bool TryCreatePinnedApplicationFormattingLeafBinding(FrameworkMemberId referenced, CilMethod? caller, int ilOffset, out FrameworkBinding binding)
	{
		binding = null!;
		if (TryCreatePinnedJoinTypeIdentityBinding(referenced, caller, ilOffset, out binding)) return true;
		if (FrameworkImplementationPack is not { IsPinnedStringBuilderInput: true, EnableUnlistedManagedBodies: false } || caller is null ||
			caller.ModuleName is "System.Private.CoreLib" or "CopperSharp.Runtime.Managed" or "CopperSharp.Compiler" || Net10FrameworkContract.Default.IsFrameworkAssembly(caller.ModuleName) ||
			caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset) is not { OpCode: var opcode } ||
			!FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(referenced, true, out var candidate)) return false;
		if (opcode == OpCodes.Newobj && candidate.ShadowMethod is { TypeName: "CopperSharp.Runtime.ShadowStringData", MethodName: "FromCharacters" })
		{
			binding = candidate;
			return true;
		}
		if (opcode != OpCodes.Call || candidate.Target is not ("intrinsic:runtime-type-from-handle" or "intrinsic:runtime-type-equals" or "intrinsic:runtime-type-not-equals" or "intrinsic:object-reference-equals") ||
			FrameworkImplementationProfile.Canonicalize(referenced).DeclaringType.FullMetadataName != "System.Type" || !IsPinnedApplicationFormatProviderCaller(caller)) return false;
		binding = candidate;
		return true;
	}

	private bool IsPinnedApplicationFormatProviderCaller(CilMethod caller)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || caller.ModuleName is "System.Private.CoreLib" or "CopperSharp.Runtime.Managed" or "CopperSharp.Compiler" ||
			Net10FrameworkContract.Default.IsFrameworkAssembly(caller.ModuleName) ||
			caller.Signature.Header.RawValue != 0x20 || caller.Signature.GenericParameterCount != 0 || !caller.MethodTypeArguments.IsDefaultOrEmpty ||
			caller.Signature.ReturnType is not { Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "object" } ||
			caller.Signature.ParameterTypes is not [{ Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "System.Type" }]) return false;
		var contract = GetModule("System.Private.CoreLib").TryResolveManagedMethod("System.Private.CoreLib", "System.IFormatProvider", "GetFormat", caller.Signature);
		if (contract is null || TryGetInterfaceImplementation(GetTypeLayout(caller), GetInterfaceDefinition(contract)) is not { } implementation ||
			!implementation.Methods.Any(method => method.ModuleName == caller.ModuleName && method.Handle == caller.Handle && method.ConstructedDeclaringType == caller.ConstructedDeclaringType)) return false;
		return true;
	}

	private FrameworkMemberId DescribeImplementationCaller(CilMethod caller)
	{
		var definition = GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, []);
		if (caller.ModuleName == "System.Private.CoreLib" && caller.ConstructedDeclaringType is { GenericArguments: [var element] } owner)
		{
			FrameworkTypeId? closed = null;
			if (definition.DeclaringType.FullMetadataName == "System.Collections.Generic.List`1" && IsCompositeFormatSegment(element) &&
				owner.DisplayName == $"System.Collections.Generic.List`1<{element.DisplayName}>")
				closed = FrameworkTypeId.GenericInstantiation(definition.DeclaringType, [StringBuilderCompositeSurface.Segment]);
			else if (definition.DeclaringType.FullMetadataName is "System.Collections.Generic.List`1" or "System.Collections.Generic.List`1+Enumerator" &&
				element is { Kind: CilTypeKind.ValueType, DisplayName: "System.Decimal" } && TryGetDecimalLayout(element, out _) &&
				owner.DisplayName == $"{definition.DeclaringType.FullMetadataName.Replace('+', '/')}<System.Decimal>")
				closed = FrameworkTypeId.GenericInstantiation(definition.DeclaringType, [StringBuilderDecimalListSurface.Decimal]);
			else if ((definition.DeclaringType.FullMetadataName == "System.ReadOnlySpan`1" && definition.Name is "ToString" or "TryCopyTo" ||
				definition.DeclaringType.FullMetadataName == "System.Span`1" && definition.Name == "ToString") &&
				element is { Kind: CilTypeKind.Character, Size: 2, DisplayName: "char" } && owner.DisplayName == definition.DeclaringType.FullMetadataName + "<char>")
				closed = FrameworkTypeId.GenericInstantiation(definition.DeclaringType, [FrameworkTypeId.Primitive("System.Char")]);
			if (closed is not null) return new FrameworkMemberId(closed, definition.Name, definition.Signature, definition.MethodTypeArguments);
		}
		return definition;
	}

	internal bool IsPinnedCompositeSpanToString(CilMethod caller) => FrameworkImplementationPack?.IsPinnedStringBuilderInput == true &&
		caller.ModuleName == "System.Private.CoreLib" &&
		(StringBuilderCompositeSurface.SpanToString.Equals(FrameworkImplementationProfile.Canonicalize(DescribeImplementationCaller(caller))) ||
		 StringBuilderCompositeSurface.MutableSpanToString.Equals(FrameworkImplementationProfile.Canonicalize(DescribeImplementationCaller(caller))));

	private bool IsPinnedCompositeParserCaller(CilMethod caller) =>
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && caller.ModuleName == "System.Private.CoreLib" &&
		caller.MethodTypeArguments.IsDefaultOrEmpty &&
		(caller.Name == "TryParseLiterals" && StringBuilderCompositeSurface.IsOwnedCaller(GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, [])) ||
		 caller.Name == "ToString" && StringBuilderCompositeSurface.BufferMembers.Contains(FrameworkImplementationProfile.Canonicalize(DescribeImplementationCaller(caller))));

	private bool IsPinnedFloatingBufferCaller(CilMethod caller) => caller.Name == "FormatFloat" &&
		caller.MethodTypeArguments.Length == 1 && IsPinnedFloatingFormatter(caller);

	internal bool TryCreatePinnedNumericSpanIdentityBinding(FrameworkMemberId referenced, CilMethod? caller, int ilOffset, out FrameworkBinding binding)
	{
		binding = null!;
		if (caller is null || caller.Name != "TryCopyTo" || !IsPinnedNumericSpecialization(caller) ||
			caller.MethodTypeArguments is not [{ Kind: CilTypeKind.Character, Size: 2, DisplayName: "char" }] ||
			GetModule(caller.ModuleName).GetTypeName(caller.DeclaringType) != "System.Number" ||
			caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset)?.OpCode != OpCodes.Call) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var variable = FrameworkTypeId.GenericMethodParameter(0);
		var character = FrameworkTypeId.Primitive("System.Char");
		var span = FrameworkTypeId.Named("System.Runtime", "System.Span`1");
		var closedSpan = FrameworkTypeId.GenericInstantiation(span, [character]);
		var signature = new FrameworkMethodSignatureId(0x10, 2, 1, FrameworkTypeId.GenericMethodParameter(1), [variable]);
		var castOwner = FrameworkTypeId.Named("System.Runtime", "System.Runtime.CompilerServices.Unsafe");
		if (!member.Equals(new FrameworkMemberId(castOwner, "BitCast", signature, [FrameworkTypeId.GenericInstantiation(span, [variable]), closedSpan])) &&
			!member.Equals(new FrameworkMemberId(castOwner, "BitCast", signature, [closedSpan, closedSpan]))) return false;
		if (!FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new(member.DeclaringType, member.Name, member.Signature, [closedSpan, closedSpan]), true, out var identity)) return false;
		binding = identity with { Member = referenced };
		return true;
	}

	internal bool TryCreatePinnedCompositeBufferToStringBinding(FrameworkMemberId referenced, CilMethod? caller, int ilOffset,
		out FrameworkBinding binding, out CilMethod implementation)
	{
		binding = null!; implementation = null!;
		if (caller is null || FrameworkImplementationPack?.EnableUnlistedManagedBodies != false || (!IsPinnedCompositeParserCaller(caller) && !IsPinnedFloatingBufferCaller(caller)) ||
			caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset) is not { OpCode: var opcode, ConstrainedTypeToken: { } token } || opcode != OpCodes.Callvirt) return false;
		var slot = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Object"), "ToString",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []));
		if (!FrameworkImplementationProfile.Canonicalize(referenced).Equals(slot)) return false;
		var declaration = GetModule("System.Private.CoreLib").TryResolveManagedMethod("System.Private.CoreLib", "System.Object", "ToString",
			new MethodSignature<CilType>(new SignatureHeader(0x20), new CilType(CilTypeKind.ManagedReference, 4, "string"), 0, 0, []));
		if (declaration is null || !TryResolveExperimentalCharacterBufferToString(caller, token, ilOffset, declaration, out implementation)) return false;
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ManagedBody, "managed:composite-buffer-tostring", StringBuilderCompositeSurface.Effects,
			Reason: "The exact released parser or floating formatter snapshots its validated character buffer through its CoreLib override.");
		return true;
	}

	internal bool TryCreatePinnedApplicationToStringBinding(FrameworkMemberId referenced, CilMethod? caller, int ilOffset,
		out FrameworkBinding binding, out CilMethod implementation)
	{
		binding = null!; implementation = null!;
		if (FrameworkImplementationPack is not { IsPinnedStringBuilderInput: true, EnableUnlistedManagedBodies: false } || caller is null) return false;
		var slot = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Object"), "ToString",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []));
		if (!FrameworkImplementationProfile.Canonicalize(referenced).Equals(slot)) return false;
		var index = caller.Instructions.ToList().FindIndex(instruction => instruction.Offset == ilOffset);
		if (index < 1 || caller.Instructions[index].OpCode != OpCodes.Callvirt || caller.Instructions[index].ConstrainedTypeToken is not null) return false;
		CilType receiver;
		if (!TryGetDirectCallReceiver(caller, ilOffset, out receiver))
		{
			var producer = index - 1;
			while (producer >= 0 && caller.Instructions[producer].OpCode == OpCodes.Nop) producer--;
			if (producer < 0 || caller.Instructions[producer].OpCode != OpCodes.Castclass || HasControlFlowEntryIntoDelegateEqualsSuffix(caller, producer, index)) return false;
			receiver = ResolveTypeToken((int)caller.Instructions[producer].Operand!, caller, caller.Instructions[producer].Offset);
		}
		if (receiver.Kind != CilTypeKind.ManagedReference || !receiver.GenericArguments.IsDefaultOrEmpty) return false;
		var identity = ResolveRuntimeTypeIdentity(receiver, caller.ModuleName);
		if (identity.Handle.Kind != HandleKind.TypeDefinition || identity.ModuleName is "System.Private.CoreLib" or "CopperSharp.Runtime.Managed" or "CopperSharp.Compiler" ||
			Net10FrameworkContract.Default.IsFrameworkAssembly(identity.ModuleName)) return false;
		var owner = GetModule(identity.ModuleName);
		var definition = owner.Reader.GetTypeDefinition((TypeDefinitionHandle)identity.Handle);
		if ((definition.Attributes & TypeAttributes.Sealed) == 0 || owner.IsValueTypeDefinition(definition) || !owner.IsFrameworkObjectReference(definition.BaseType)) return false;
		var signature = new MethodSignature<CilType>(new SignatureHeader(0x20), new CilType(CilTypeKind.ManagedReference, 4, "string"), 0, 0, []);
		var selected = definition.GetMethods().Select(owner.GetMethod).SingleOrDefault(method => method.Name == "ToString" &&
			method.IsVirtual && !method.IsNewSlot && !method.IsAbstract && method.MethodTypeArguments.IsDefaultOrEmpty && SignaturesMatch(method.Signature, signature));
		if (selected is null) return false;
		implementation = selected;
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ManagedBody, "managed:sealed-application-tostring",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]),
			Reason: "A proven sealed application receiver selects its own Object.ToString override.");
		return true;
	}

	internal bool TryCreatePinnedStringBuilderToStringBinding(
		FrameworkMemberId referenced, CilMethod? caller, int ilOffset, out FrameworkBinding binding)
	{
		binding = null!;
		if (FrameworkImplementationPack is not { IsPinnedStringBuilderInput: true, EnableUnlistedManagedBodies: false } ||
			caller is null || ilOffset < 0 ||
			!caller.Instructions.Any(instruction => instruction.Offset == ilOffset && instruction.OpCode == OpCodes.Callvirt)) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var declaration = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Object"), "ToString",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []));
		if (!member.Equals(declaration)) return false;
		if ((!TryGetDirectCallReceiver(caller, ilOffset, out var receiver) && !TryGetProducedStringBuilderReceiver(caller, ilOffset, out receiver)) ||
			receiver is not { Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "System.Text.StringBuilder" } ||
			!receiver.GenericArguments.IsDefaultOrEmpty) return false;
		var identity = ResolveRuntimeTypeIdentity(receiver, caller.ModuleName);
		if (identity.ModuleName != "System.Private.CoreLib" || identity.Handle.Kind != HandleKind.TypeDefinition) return false;
		var owner = GetModule(identity.ModuleName);
		var definition = owner.Reader.GetTypeDefinition((TypeDefinitionHandle)identity.Handle);
		if (owner.GetTypeName(definition) != "System.Text.StringBuilder" || (definition.Attributes & TypeAttributes.Sealed) == 0) return false;
		var target = StringBuilderFrameworkSurface.Members.Single(candidate => candidate.Name == "ToString" && candidate.Signature.ParameterTypes.Length == 0);
		if (!StringBuilderFrameworkSurface.TryGetEffects(target, out var effects)) return false;
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ManagedBody,
			"managed:sealed-stringbuilder-tostring", effects,
			Reason: "The exact sealed StringBuilder receiver selects its verified CoreLib ToString override.");
		return true;
	}

	private bool TryGetProducedStringBuilderReceiver(CilMethod caller, int callOffset, out CilType receiver)
	{
		receiver = null!;
		var index = caller.Instructions.ToList().FindIndex(instruction => instruction.Offset == callOffset);
		var producerIndex = index - 1;
		while (producerIndex >= 0 && caller.Instructions[producerIndex].OpCode == OpCodes.Nop) producerIndex--;
		if (producerIndex < 0 || HasControlFlowEntryIntoDelegateEqualsSuffix(caller, producerIndex, index)) return false;
		var producer = caller.Instructions[producerIndex];
		if (producer.OpCode != OpCodes.Call && producer.OpCode != OpCodes.Callvirt && producer.OpCode != OpCodes.Newobj) return false;
		var target = ResolveMethodToken((int)producer.Operand!, caller, producer.Offset);
		receiver = producer.OpCode == OpCodes.Newobj
			? target.ConstructedDeclaringType ?? (target.Definition is not null ? GetMethodDeclaringType(target.Definition) : null!)
			: target.Signature.ReturnType;
		return receiver is { Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "System.Text.StringBuilder" };
	}

	internal bool TryCreateExperimentalObjectJoinTextBinding(
		FrameworkMemberId referencedMember, CilMethod? caller, out FrameworkBinding binding)
	{
		binding = null!;
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) ||
			caller is not { ModuleName: "System.Private.CoreLib" } ||
			!caller.Signature.Header.IsInstance || caller.Signature.ReturnType.DisplayName != "System.Text.StringBuilder") return false;
		var append = caller.DisplayName == "System.Text.StringBuilder::Append" &&
			caller.MethodTypeArguments.IsDefaultOrEmpty && caller.Signature.GenericParameterCount == 0 &&
			caller.Signature.RequiredParameterCount == 1 &&
			caller.Signature.ParameterTypes is [{ Kind: CilTypeKind.ManagedReference, DisplayName: "object" }];
		var insert = caller.DisplayName == "System.Text.StringBuilder::Insert" &&
			caller.MethodTypeArguments.IsDefaultOrEmpty && caller.Signature.GenericParameterCount == 0 &&
			caller.Signature.RequiredParameterCount == 2 &&
			caller.Signature.ParameterTypes is [{ Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" }, { Kind: CilTypeKind.ManagedReference, DisplayName: "object" }];
		var applicationReferenceJoin = IsPinnedPrimitiveJoinCaller(caller) && IsExperimentalApplicationJoinReference(caller.MethodTypeArguments[0]);
		var join = applicationReferenceJoin || caller.DisplayName == "System.Text.StringBuilder::AppendJoinCore<object>" &&
			caller.MethodTypeArguments is [{ Kind: CilTypeKind.ManagedReference, DisplayName: "object" }] &&
			caller.Signature.GenericParameterCount == 1 && caller.Signature.RequiredParameterCount == 3 &&
			caller.Signature.ParameterTypes is
			[
				{ Kind: CilTypeKind.ManagedPointer, ElementType.DisplayName: "char" },
				{ DisplayName: "int" },
				{ Kind: CilTypeKind.ValueType, DisplayName: "System.ReadOnlySpan`1<object>" } or
				{ Kind: CilTypeKind.ManagedReference, DisplayName: "System.Collections.Generic.IEnumerable`1<object>" }
			];
		var format = caller.DisplayName == "System.Text.StringBuilder::AppendFormat" &&
			caller.MethodTypeArguments.IsDefaultOrEmpty && caller.Signature.GenericParameterCount == 0 &&
			caller.Signature.RequiredParameterCount == 3 && caller.Signature.ParameterTypes is
			[
				{ Kind: CilTypeKind.ManagedReference, DisplayName: "System.IFormatProvider" },
				{ Kind: CilTypeKind.ManagedReference, DisplayName: "string" },
				{ Kind: CilTypeKind.ValueType, DisplayName: "System.ReadOnlySpan`1<object>" }
			];
		if (!append && !insert && !join && !format) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referencedMember);
		if (!member.DeclaringType.Equals(FrameworkTypeId.Named("System.Runtime", "System.Object")) ||
			member.Name != "ToString" || member.MethodTypeArguments.Length != 0 ||
			member.Signature.Header != 0x20 || member.Signature.GenericParameterCount != 0 ||
			member.Signature.RequiredParameterCount != 0 || member.Signature.ParameterTypes.Length != 0 ||
			!member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.String"))) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinText", "ToString");
		binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]),
			Reason: "Exact CoreLib object append/insert/join conversion for strings, Boolean, Char, integer boxes and application overrides.", ShadowMethod: shadow);
		return true;
	}

	internal bool TryCreateExperimentalListPrefixCopyBinding(FrameworkMemberId referencedMember, CilMethod? caller, out FrameworkBinding binding)
	{
		binding = null!;
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput == true &&
			caller is { ModuleName: "System.Private.CoreLib", Name: "Clear", ConstructedDeclaringType.GenericArguments: [{ Kind: CilTypeKind.ValueType, DisplayName: "System.Decimal" } clearElement] } &&
			TryGetDecimalLayout(clearElement, out _) && StringBuilderDecimalListSurface.IsOwnedCaller(DescribeImplementationCaller(caller)) &&
			DescribeImplementationCaller(caller).Name == "Clear")
		{
			var clear = FrameworkImplementationProfile.Canonicalize(referencedMember);
			if (clear.Equals(new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Array"), "Clear",
				new FrameworkMethodSignatureId(0, 0, 3, FrameworkTypeId.Primitive("System.Void"),
					[FrameworkTypeId.Named("System.Runtime", "System.Array"), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]))))
			{
				var shadowClear = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray", "ClearDecimals");
				binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
					$"shadow:{shadowClear.AssemblyName}:{shadowClear.TypeName}::{shadowClear.MethodName}", StringBuilderDecimalListSurface.Effects,
					Reason: "The exact released Decimal List clear zeros validated whole Decimal elements.", ShadowMethod: shadowClear);
				return true;
			}
		}
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) ||
			caller is not { ModuleName: "System.Private.CoreLib", Name: "set_Capacity" or "ToArray", ConstructedDeclaringType: { GenericArguments: [var element] } owner } ||
			owner.DisplayName != $"System.Collections.Generic.List`1<{element.DisplayName}>" ||
			!(element.Kind == CilTypeKind.FloatingPoint && (element.DisplayName == "float" && element.Size == 4 || element.DisplayName == "double" && element.Size == 8) ||
			  element is { Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "object" } ||
			  element is { Kind: CilTypeKind.ValueType, DisplayName: "System.Decimal" } && TryGetDecimalLayout(element, out _)) ||
			!caller.Signature.Header.IsInstance || caller.Signature.GenericParameterCount != 0 ||
			!(caller.Name == "set_Capacity" && caller.Signature.RequiredParameterCount == 1 && caller.Signature.ReturnType.DisplayName == "void" && caller.Signature.ParameterTypes is [{ DisplayName: "int", Size: 4, Kind: CilTypeKind.SignedInteger }] ||
			  caller.Name == "ToArray" && caller.Signature.RequiredParameterCount == 0 && caller.Signature.ParameterTypes.Length == 0 &&
			  caller.Signature.ReturnType.Kind == CilTypeKind.ManagedReference && caller.Signature.ReturnType.ElementType == element && caller.Signature.ReturnType.DisplayName == element.DisplayName + "[]")) return false;
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true &&
			(element is not { Kind: CilTypeKind.ValueType, DisplayName: "System.Decimal" } || !StringBuilderDecimalListSurface.IsOwnedCaller(DescribeImplementationCaller(caller)))) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referencedMember);
		var array = FrameworkTypeId.Named("System.Runtime", "System.Array");
		if (!member.DeclaringType.Equals(array) || member.Name != "Copy" || member.MethodTypeArguments.Length != 0 ||
			member.Signature.Header != 0 || member.Signature.GenericParameterCount != 0 || member.Signature.RequiredParameterCount != 3 ||
			!member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) ||
			!member.Signature.ParameterTypes.SequenceEqual([array, array, FrameworkTypeId.Primitive("System.Int32")])) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray", element.DisplayName == "System.Decimal" ? "CopyDecimals" : element.Kind == CilTypeKind.ManagedReference ? "CopyObjects" : element.Size == 4 ? "CopySingles" : "CopyDoubles");
		binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedArrays]),
			Reason: "Exact CoreLib numeric and object List growth and ToArray copy array prefixes with their verified element transport.", ShadowMethod: shadow);
		return true;
	}

	internal bool TryCreateExperimentalCompositeSegmentCopyBinding(FrameworkMemberId referencedMember, CilMethod? caller, out FrameworkBinding binding)
	{
		binding = null!;
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) ||
			caller is not { ModuleName: "System.Private.CoreLib", Name: "set_Capacity" or "ToArray", ConstructedDeclaringType: { GenericArguments: [var element] } owner } ||
			owner.DisplayName != $"System.Collections.Generic.List`1<{element.DisplayName}>" ||
			!IsCompositeFormatSegment(element) || !TryGetReferenceFreeStructLayout(element, caller.ModuleName, out _)) return false;
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !StringBuilderCompositeSurface.IsHelperCaller(DescribeImplementationCaller(caller))) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referencedMember);
		var array = FrameworkTypeId.Named("System.Runtime", "System.Array");
		if (!member.DeclaringType.Equals(array) || member.Name != "Copy" || member.MethodTypeArguments.Length != 0 ||
			member.Signature.Header != 0 || member.Signature.GenericParameterCount != 0 || member.Signature.RequiredParameterCount != 3 ||
			!member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) ||
			!member.Signature.ParameterTypes.SequenceEqual([array, array, FrameworkTypeId.Primitive("System.Int32")])) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray", "CopyCompositeSegments");
		binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.ManagedArrays]),
			Reason: "Verified parsed-segment lists copy same-type tuple-array prefixes.", ShadowMethod: shadow);
		return true;
	}

	internal bool TryCreateExperimentalObjectJoinDispatchBinding(
		FrameworkMemberId referencedMember, CilMethod? caller, out FrameworkBinding binding)
	{
		binding = null!;
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) ||
			caller is not { ModuleName: "CopperSharp.Runtime.Managed", DisplayName: "CopperSharp.Runtime.ShadowObjectJoinText::ToString" } ||
			!caller.Signature.Header.IsInstance || caller.Signature.GenericParameterCount != 0 ||
			caller.Signature.RequiredParameterCount != 0 || caller.Signature.ParameterTypes.Length != 0 ||
			caller.Signature.ReturnType.DisplayName != "string" || !caller.MethodTypeArguments.IsDefaultOrEmpty) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referencedMember);
		if (!member.DeclaringType.Equals(FrameworkTypeId.Named("System.Runtime", "System.Object")) ||
			member.Name != "ToString" || member.MethodTypeArguments.Length != 0 || member.Signature.Header != 0x20 ||
			member.Signature.GenericParameterCount != 0 || member.Signature.RequiredParameterCount != 0 || member.Signature.ParameterTypes.Length != 0 ||
			!member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.String"))) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinDispatch", "ToString");
		binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]),
			Reason: "Exact object join dispatch to reachable application overrides and verified Decimal, Single and Double ToString payloads.", ShadowMethod: shadow, PreservesVirtualDispatch: true);
		return true;
	}

	private string ResolveShadowEnumerableMaterializer(
		FrameworkBinding binding,
		CilMethod? caller,
		int callOffset)
	{
		if (caller is null || callOffset < 0)
		{
			throw EnumerableSourceDiagnostic(
				binding,
				caller,
				callOffset,
				"call-site context is unavailable");
		}

		return EnumerableSourceProvenanceAnalyzer.Analyze(this, caller, callOffset) switch
		{
			EnumerableSourceProvenance.Range => "RangeToArray",
			EnumerableSourceProvenance.Repeat => "RepeatToArray",
			EnumerableSourceProvenance.RangeSelect => "SelectInt32ToArray",
			EnumerableSourceProvenance.RangeWhere => "RangeWhereInt32ToArray",
			EnumerableSourceProvenance.RangeSelectWhere => "SelectWhereInt32ToArray",
			EnumerableSourceProvenance.RangeWhereTake => "RangeWhereInt32ToArray",
			EnumerableSourceProvenance.RangeSelectWhereTake => "SelectWhereInt32ToArray",
			_ => throw EnumerableSourceDiagnostic(
				binding,
				caller,
				callOffset,
				"the source provenance is unknown or merges different iterator families")
		};
	}

	private string ResolveShadowEnumerableAny(
		FrameworkBinding binding,
		CilMethod? caller,
		int callOffset,
		bool withPredicate)
	{
		if (caller is null || callOffset < 0)
		{
			throw EnumerableSourceDiagnostic(
				binding,
				caller,
				callOffset,
				"call-site context is unavailable");
		}

		var source = EnumerableSourceProvenanceAnalyzer.Analyze(
			this,
			caller,
			callOffset,
			argumentFromTop: withPredicate ? 1 : 0);
		var suffix = withPredicate ? "AnyPredicate" : "Any";
		return source switch
		{
			EnumerableSourceProvenance.Range => "Range" + suffix,
			EnumerableSourceProvenance.Repeat => "RepeatInt32" + suffix,
			EnumerableSourceProvenance.RangeSelect => "SelectInt32" + suffix,
			EnumerableSourceProvenance.RangeWhere => "RangeWhereInt32" + suffix,
			EnumerableSourceProvenance.RangeSelectWhere => "SelectWhereInt32" + suffix,
			EnumerableSourceProvenance.RangeWhereTake => withPredicate
				? "RangeWhereInt32TakeAnyPredicate"
				: "RangeWhereInt32Any",
			EnumerableSourceProvenance.RangeSelectWhereTake => withPredicate
				? "SelectWhereInt32TakeAnyPredicate"
				: "SelectWhereInt32Any",
			_ => throw EnumerableSourceDiagnostic(
				binding,
				caller,
				callOffset,
				"the selected Any slice requires exact private int iterator provenance")
		};
	}

	private string ResolveShadowEnumerableTake(
		FrameworkBinding binding,
		CilMethod? caller,
		int callOffset)
	{
		if (caller is null || callOffset < 0)
		{
			throw EnumerableSourceDiagnostic(
				binding,
				caller,
				callOffset,
				"call-site context is unavailable");
		}

		return EnumerableSourceProvenanceAnalyzer.Analyze(
			this,
			caller,
			callOffset,
			argumentFromTop: 1) switch
		{
			EnumerableSourceProvenance.Null => "RangeTakeInt32",
			EnumerableSourceProvenance.Range => "RangeTakeInt32",
			EnumerableSourceProvenance.Repeat => "RepeatInt32TakeInt32",
			EnumerableSourceProvenance.RangeSelect => "SelectInt32TakeInt32",
			EnumerableSourceProvenance.RangeWhere => "RangeWhereInt32TakeInt32",
			EnumerableSourceProvenance.RangeSelectWhere => "SelectWhereInt32TakeInt32",
			EnumerableSourceProvenance.RangeWhereTake => "RangeWhereInt32TakeInt32",
			EnumerableSourceProvenance.RangeSelectWhereTake => "SelectWhereInt32TakeInt32",
			_ => throw EnumerableSourceDiagnostic(
				binding,
				caller,
				callOffset,
				"the selected Take slice requires exact private int iterator provenance")
		};
	}

	private string ResolveShadowEnumerableSum(
		FrameworkBinding binding,
		CilMethod? caller,
		int callOffset,
		bool withSelector,
		IReadOnlyList<CilType>? methodTypeArguments)
	{
		if (caller is null || callOffset < 0)
		{
			throw EnumerableSourceDiagnostic(
				binding,
				caller,
				callOffset,
				"call-site context is unavailable");
		}

		var source = EnumerableSourceProvenanceAnalyzer.Analyze(
			this,
			caller,
			callOffset,
			argumentFromTop: withSelector ? 1 : 0);
		var isReferenceFreeStructSelector =
			withSelector &&
			methodTypeArguments is [{ Kind: CilTypeKind.ValueType } sourceElement] &&
			TypeContainsManagedReferences(sourceElement) == false;
		if (isReferenceFreeStructSelector &&
			source is EnumerableSourceProvenance.Null or EnumerableSourceProvenance.Array)
		{
			return "ArraySumSelector";
		}
		var suffix = withSelector ? "SumSelector" : "Sum";
		return source switch
		{
			EnumerableSourceProvenance.Null => "Range" + suffix,
			EnumerableSourceProvenance.Range => "Range" + suffix,
			EnumerableSourceProvenance.Repeat => "RepeatInt32" + suffix,
			EnumerableSourceProvenance.RangeSelect => "SelectInt32" + suffix,
			EnumerableSourceProvenance.RangeWhere => "RangeWhereInt32" + suffix,
			EnumerableSourceProvenance.RangeSelectWhere => "SelectWhereInt32" + suffix,
			EnumerableSourceProvenance.RangeWhereTake =>
				"RangeWhereInt32Take" + suffix,
			EnumerableSourceProvenance.RangeSelectWhereTake =>
				"SelectWhereInt32Take" + suffix,
			_ => throw EnumerableSourceDiagnostic(
				binding,
				caller,
				callOffset,
				"the selected Sum slice requires exact private int iterator provenance or a one-dimensional array of reference-free structs")
		};
	}

	private string ResolveShadowEnumerableOrdering(
		FrameworkBinding binding,
		CilMethod? caller,
		int callOffset,
		bool isThenBy)
	{
		if (caller is null || callOffset < 0)
		{
			throw EnumerableSourceDiagnostic(
				binding,
				caller,
				callOffset,
				"call-site context is unavailable");
		}

		var source = EnumerableSourceProvenanceAnalyzer.Analyze(
			this,
			caller,
			callOffset,
			argumentFromTop: 1);
		if (!isThenBy && source is (
			EnumerableSourceProvenance.DictionaryUInt32Values or
			EnumerableSourceProvenance.Null))
		{
			return "DictionaryUInt32ValuesOrderBy";
		}
		if (isThenBy && source is (
			EnumerableSourceProvenance.OrderedPrimary or
			EnumerableSourceProvenance.Null))
		{
			return "DictionaryUInt32ValuesThenBy";
		}
		throw EnumerableSourceDiagnostic(
			binding,
			caller,
			callOffset,
			isThenBy
				? "the selected ThenBy slice requires one exact Dictionary<uint,T>.Values OrderBy source and rejects additional ThenBy stages"
				: "the selected OrderBy slice requires exact Dictionary<uint,T>.Values source provenance");
	}

	private void RequireEnumerableProvenance(
		FrameworkBinding binding,
		CilMethod? caller,
		int callOffset,
		EnumerableSourceProvenance required,
		string detail)
	{
		if (caller is null || callOffset < 0 ||
			EnumerableSourceProvenanceAnalyzer.Analyze(
				this,
				caller,
				callOffset) != required)
		{
			throw EnumerableSourceDiagnostic(binding, caller, callOffset, detail);
		}
	}

	private static M68kCompilationException EnumerableSourceDiagnostic(
		FrameworkBinding binding,
		CilMethod? caller,
		int ilOffset,
		string detail) =>
		new(
			M68kDiagnosticIds.UnsupportedPolymorphism,
			$"Framework member '{binding.Member.DisplayName}' is admitted only when " +
			"closed-world analysis proves an Enumerable.Range, Enumerable.Repeat, or selected Select/Where source; " +
			$"{detail}.",
			caller?.DisplayName,
			ilOffset >= 0 ? ilOffset : null);

	private FrameworkDefaultEqualityKind ResolveDefaultEqualityKind(
		FrameworkMemberId member,
		CilType? constructedDeclaringType,
		IReadOnlyList<CilType>? methodTypeArguments)
	{
		CilType? element = null;
		if (member.Name is "DefaultEquals" or "DefaultHashCode" &&
			methodTypeArguments is [var methodElement])
		{
			element = methodElement;
		}
		else if (member.DeclaringType is
			{
				Kind: FrameworkTypeKind.GenericInstantiation,
				ElementType: { } declaringDefinition
			} &&
			IsDefaultEqualityDeclaringType(declaringDefinition) &&
			constructedDeclaringType is { GenericArguments: [var declaringElement] })
		{
			element = declaringElement;
		}

		if (element is not
			{
				Kind: CilTypeKind.ManagedReference,
				GenericArguments.IsDefaultOrEmpty: true,
				ElementType: null
			} ||
			string.Equals(element.DisplayName, "string", StringComparison.Ordinal))
		{
			return FrameworkDefaultEqualityKind.Unsupported;
		}

		var matches = new List<(CompilationModule Module, TypeDefinitionHandle Handle)>();
		var modules = new List<CompilationModule> { _root };
		foreach (var assemblyName in _root._managedAssemblyPaths.Keys
			.OrderBy(static name => name, StringComparer.Ordinal))
		{
			var module = GetOrLoadModule(assemblyName);
			if (module is not null && !modules.Contains(module))
			{
				modules.Add(module);
			}
		}
		foreach (var module in modules)
		{
			foreach (var handle in module.Reader.TypeDefinitions)
			{
				if (string.Equals(
						module._signatureProvider
							.GetTypeFromDefinition(module.Reader, handle, 0x12)
							.DisplayName,
						element.DisplayName,
						StringComparison.Ordinal))
				{
					matches.Add((module, handle));
				}
			}
		}
		if (matches.Count != 1)
		{
			return FrameworkDefaultEqualityKind.Unsupported;
		}
		return matches[0].Module.ClassifySealedReferenceEquality(matches[0].Handle);
	}

	private static bool IsDefaultEqualityDeclaringType(
		FrameworkTypeId declaringDefinition) =>
		(declaringDefinition.AssemblyName == "System.Collections" &&
		 declaringDefinition.MetadataName is
			"System.Collections.Generic.List`1" or
			"System.Collections.Generic.EqualityComparer`1") ||
		(declaringDefinition.AssemblyName == "System.Runtime" &&
		 declaringDefinition.MetadataName ==
			"System.Collections.Generic.IEqualityComparer`1");

	private FrameworkDefaultEqualityKind ClassifySealedReferenceEquality(
		TypeDefinitionHandle receiverHandle)
	{
		var receiverDefinition = Reader.GetTypeDefinition(receiverHandle);
		if ((receiverDefinition.Attributes & TypeAttributes.Sealed) == 0 ||
			(receiverDefinition.Attributes & TypeAttributes.Interface) != 0 ||
			receiverDefinition.GetGenericParameters().Count != 0)
		{
			return FrameworkDefaultEqualityKind.Unsupported;
		}

		var provider = new FrameworkSignatureTypeProvider(this);
		var receiverType = provider.GetTypeFromDefinition(Reader, receiverHandle, 0x12);
		var current = receiverHandle;
		while (!current.IsNil)
		{
			var definition = Reader.GetTypeDefinition(current);
			foreach (var implementationHandle in definition.GetInterfaceImplementations())
			{
				var implemented = Reader
					.GetInterfaceImplementation(implementationHandle)
					.Interface;
				var implementedType = implemented.Kind switch
				{
					HandleKind.TypeDefinition => provider.GetTypeFromDefinition(
						Reader,
						(TypeDefinitionHandle)implemented,
						0x12),
					HandleKind.TypeReference => provider.GetTypeFromReference(
						Reader,
						(TypeReferenceHandle)implemented,
						0x12),
					HandleKind.TypeSpecification => Reader
						.GetTypeSpecification((TypeSpecificationHandle)implemented)
						.DecodeSignature(provider, FrameworkGenericContext.Empty),
					_ => null
				};
				if (implementedType is
					{
						Kind: FrameworkTypeKind.GenericInstantiation,
						ElementType:
						{
							MetadataName: "System.IEquatable`1",
							AssemblyName: "System.Runtime" or "System.Private.CoreLib"
						},
						GenericArguments: [var implementedElement]
					} &&
					implementedElement.Equals(receiverType))
				{
					return FrameworkDefaultEqualityKind.SealedIEquatable;
				}
			}

			if (definition.BaseType.Kind == HandleKind.TypeDefinition)
			{
				current = (TypeDefinitionHandle)definition.BaseType;
				continue;
			}
			if (definition.BaseType.Kind == HandleKind.TypeReference)
			{
				var baseType = provider.GetTypeFromReference(
					Reader,
					(TypeReferenceHandle)definition.BaseType,
					0x12);
				return baseType is
				{
					MetadataName: "System.Object",
					AssemblyName: "System.Runtime" or "System.Private.CoreLib"
				}
					? FrameworkDefaultEqualityKind.SealedObjectEquals
					: FrameworkDefaultEqualityKind.Unsupported;
			}
			return FrameworkDefaultEqualityKind.Unsupported;
		}
		return FrameworkDefaultEqualityKind.Unsupported;
	}
	private bool IsConstrainedListEnumeratorDispose(
		FrameworkBinding binding,
		CilMethod caller,
		int ilOffset)
	{
		if (binding.Member.DeclaringType.Kind != FrameworkTypeKind.Named ||
			!string.Equals(
				binding.Member.DeclaringType.AssemblyName,
				"System.Runtime",
				StringComparison.Ordinal) ||
			!string.Equals(
				binding.Member.DeclaringType.MetadataName,
				"System.IDisposable",
				StringComparison.Ordinal) ||
			!string.Equals(binding.Member.Name, "Dispose", StringComparison.Ordinal))
		{
			return false;
		}

		foreach (var instruction in caller.Instructions)
		{
			if (instruction.Offset != ilOffset ||
				instruction.ConstrainedTypeToken is not { } constrainedTypeToken)
			{
				continue;
			}
			return IsListEnumeratorType(
				ResolveTypeToken(
					constrainedTypeToken,
					caller,
					ilOffset));
		}
		return false;
	}

	private bool IsOrderedEnumeratorDispose(
		FrameworkBinding binding,
		CilMethod caller,
		int ilOffset) =>
		binding.Member.DeclaringType.Kind == FrameworkTypeKind.Named &&
		string.Equals(
			binding.Member.DeclaringType.AssemblyName,
			"System.Runtime",
			StringComparison.Ordinal) &&
		string.Equals(
			binding.Member.DeclaringType.MetadataName,
			"System.IDisposable",
			StringComparison.Ordinal) &&
		string.Equals(binding.Member.Name, "Dispose", StringComparison.Ordinal) &&
		(EnumerableSourceProvenanceAnalyzer.Analyze(this, caller, ilOffset) ==
			EnumerableSourceProvenance.OrderedEnumerator ||
		 HasExactOrderedEnumeratorLocalReceiver(caller, ilOffset));

	private bool HasExactOrderedEnumeratorLocalReceiver(
		CilMethod caller,
		int callOffset)
	{
		var callIndex = -1;
		for (var index = 0; index < caller.Instructions.Count; index++)
		{
			if (caller.Instructions[index].Offset == callOffset)
			{
				callIndex = index;
				break;
			}
		}
		var receiverIndex = callIndex - 1;
		while (receiverIndex >= 0 &&
			caller.Instructions[receiverIndex].OpCode == OpCodes.Nop)
		{
			receiverIndex--;
		}
		if (receiverIndex < 0 ||
			!TryGetDirectLocalIndex(
				caller.Instructions[receiverIndex],
				out var receiverLocal))
		{
			return false;
		}

		var foundAssignment = false;
		for (var index = 0; index < caller.Instructions.Count; index++)
		{
			if (!TryGetDirectStoreLocalIndex(
					caller.Instructions[index],
					out var storedLocal) ||
				storedLocal != receiverLocal)
			{
				continue;
			}
			var producerIndex = index - 1;
			while (producerIndex >= 0 &&
				caller.Instructions[producerIndex].OpCode == OpCodes.Nop)
			{
				producerIndex--;
			}
			if (producerIndex < 0)
			{
				return false;
			}
			var producer = caller.Instructions[producerIndex];
			if ((producer.OpCode != OpCodes.Call &&
				 producer.OpCode != OpCodes.Callvirt) ||
				producer.Operand is not int token ||
				DescribeMethodToken(token, caller, producer.Offset) is not
				{
					TypeName: var typeName,
					Name: "GetEnumerator"
				} ||
				!typeName.StartsWith(
					"System.Collections.Generic.IEnumerable`1<",
					StringComparison.Ordinal) ||
				EnumerableSourceProvenanceAnalyzer.Analyze(
					this,
					caller,
					producer.Offset) !=
					EnumerableSourceProvenance.OrderedPrimarySecondary)
			{
				return false;
			}
			foundAssignment = true;
		}
		return foundAssignment;
	}

	private static bool TryGetDirectStoreLocalIndex(
		CilInstruction instruction,
		out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Stloc_0.Value && op.Value <= OpCodes.Stloc_3.Value)
		{
			index = op.Value - OpCodes.Stloc_0.Value;
			return true;
		}
		if (op == OpCodes.Stloc || op == OpCodes.Stloc_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}
		index = default;
		return false;
	}

	private CilMethod ResolveClosedWorldSealedInterfaceCall(
		FrameworkBinding binding,
		CilMethod caller,
		int ilOffset,
		MethodSignature<CilType> signature)
	{
		var isEquatable = string.Equals(
			binding.Target,
			"managed:closed-world-sealed-equatable-dispatch",
			StringComparison.Ordinal);
		var receiverResolved = isEquatable
			? TryGetConstrainedCallReceiver(caller, ilOffset, out var receiverType)
			: TryGetDirectCallReceiver(caller, ilOffset, out receiverType);
		if ((!isEquatable && signature.ParameterTypes.Length != 0) ||
			(isEquatable && signature.ParameterTypes.Length != 1) ||
			!receiverResolved)
		{
			throw ClosedWorldInterfaceDiagnostic(
				binding,
				caller,
				ilOffset,
				"the receiver is not a directly loaded exact managed type");
		}

		var candidates = new List<(CompilationModule Module, TypeDefinitionHandle Type)>();
		var modules = new List<CompilationModule> { _root };
		foreach (var assemblyName in _root._managedAssemblyPaths.Keys
			.OrderBy(static name => name, StringComparer.Ordinal))
		{
			var module = GetOrLoadModule(assemblyName);
			if (module is not null && !modules.Contains(module))
			{
				modules.Add(module);
			}
		}

		foreach (var module in modules)
		{
			foreach (var typeHandle in module.Reader.TypeDefinitions)
			{
				if (string.Equals(
						module.GetTypeName(typeHandle),
						receiverType.DisplayName,
						StringComparison.Ordinal))
				{
					candidates.Add((module, typeHandle));
				}
			}
		}

		if (candidates.Count != 1)
		{
			throw ClosedWorldInterfaceDiagnostic(
				binding,
				caller,
				ilOffset,
				$"the exact receiver type matched {candidates.Count} managed definitions");
		}

		var (receiverModule, receiverHandle) = candidates[0];
		var receiverDefinition = receiverModule.Reader.GetTypeDefinition(receiverHandle);
		if ((receiverDefinition.Attributes & TypeAttributes.Sealed) == 0)
		{
			throw ClosedWorldInterfaceDiagnostic(
				binding,
				caller,
				ilOffset,
				$"receiver '{receiverType.DisplayName}' is not sealed");
		}
		var implementsInterface = isEquatable
			? receiverModule.ImplementsExactEquatableInterface(
				receiverHandle,
				binding.Member.DeclaringType)
			: receiverModule.ImplementsFrameworkInterface(
				receiverHandle,
				binding.Member.DeclaringType);
		if (!implementsInterface)
		{
			throw ClosedWorldInterfaceDiagnostic(
				binding,
				caller,
				ilOffset,
				$"receiver '{receiverType.DisplayName}' does not implement the public interface");
		}

		var implementations = receiverDefinition.GetMethods()
			.Select(receiverModule.GetMethod)
			.Where(method =>
				method.Signature.Header.IsInstance &&
				(method.Name == binding.Member.Name ||
				 method.Name.EndsWith('.' + binding.Member.Name, StringComparison.Ordinal)) &&
				SignaturesMatch(method.Signature, signature))
			.ToArray();
		if (implementations.Length != 1)
		{
			throw ClosedWorldInterfaceDiagnostic(
				binding,
				caller,
				ilOffset,
				$"receiver '{receiverType.DisplayName}' matched {implementations.Length} managed implementations");
		}
		return implementations[0];
	}

	private bool ImplementsExactEquatableInterface(
		TypeDefinitionHandle receiverHandle,
		FrameworkTypeId frameworkInterface)
	{
		return frameworkInterface is
			{
				Kind: FrameworkTypeKind.GenericInstantiation,
				ElementType:
				{
					AssemblyName: "System.Runtime" or "System.Private.CoreLib",
					MetadataName: "System.IEquatable`1"
				}
			} &&
			ClassifySealedReferenceEquality(receiverHandle) ==
				FrameworkDefaultEqualityKind.SealedIEquatable;
	}

	private bool TryGetConstrainedCallReceiver(
		CilMethod caller,
		int callOffset,
		out CilType receiverType)
	{
		foreach (var instruction in caller.Instructions)
		{
			if (instruction.Offset != callOffset ||
				instruction.ConstrainedTypeToken is not { } constrainedTypeToken)
			{
				continue;
			}
			receiverType = ResolveTypeToken(constrainedTypeToken, caller, callOffset);
			return receiverType.Kind == CilTypeKind.ManagedReference;
		}
		if (caller.GenericContext.MethodArguments is [var methodTypeArgument])
		{
			receiverType = methodTypeArgument;
			return receiverType.Kind == CilTypeKind.ManagedReference;
		}
		receiverType = default!;
		return false;
	}

	private bool ImplementsFrameworkInterface(
		TypeDefinitionHandle typeHandle,
		FrameworkTypeId frameworkInterface)
	{
		if (frameworkInterface is not
			{
				Kind: FrameworkTypeKind.Named,
				AssemblyName: { } assemblyName,
				MetadataName: { } metadataName
			})
		{
			return false;
		}

		var current = typeHandle;
		while (!current.IsNil)
		{
			var type = Reader.GetTypeDefinition(current);
			foreach (var implementationHandle in type.GetInterfaceImplementations())
			{
				var implemented = Reader
					.GetInterfaceImplementation(implementationHandle)
					.Interface;
				if (implemented.Kind != HandleKind.TypeReference)
				{
					continue;
				}
				var reference = Reader.GetTypeReference((TypeReferenceHandle)implemented);
				if (string.Equals(GetTypeName(reference), metadataName, StringComparison.Ordinal) &&
					string.Equals(
						GetReferencedAssemblyName(reference.ResolutionScope),
						assemblyName,
						StringComparison.Ordinal))
				{
					return true;
				}
			}

			if (type.BaseType.Kind != HandleKind.TypeDefinition)
			{
				break;
			}
			current = (TypeDefinitionHandle)type.BaseType;
		}
		return false;
	}

	private static bool TryGetDirectCallReceiver(
		CilMethod caller,
		int callOffset,
		out CilType receiverType)
	{
		var callIndex = -1;
		for (var index = 0; index < caller.Instructions.Count; index++)
		{
			if (caller.Instructions[index].Offset == callOffset)
			{
				callIndex = index;
				break;
			}
		}
		for (var index = callIndex - 1; index >= 0; index--)
		{
			var producer = caller.Instructions[index];
			if (producer.OpCode == OpCodes.Nop)
			{
				continue;
			}
			if (HasControlFlowEntryIntoDelegateEqualsSuffix(caller, index, callIndex))
			{
				break;
			}
			return TryGetDirectLoadedType(caller, producer, out receiverType) &&
				receiverType.Kind == CilTypeKind.ManagedReference;
		}
		receiverType = default!;
		return false;
	}

	private static M68kCompilationException ClosedWorldInterfaceDiagnostic(
		FrameworkBinding binding,
		CilMethod caller,
		int ilOffset,
		string detail) =>
		new(
			M68kDiagnosticIds.UnsupportedPolymorphism,
			$"Framework interface member '{binding.Member.DisplayName}' is admitted only when closed-world analysis proves a sealed exact receiver; {detail}.",
			caller.DisplayName,
			ilOffset);

	private bool? TypeContainsManagedReferences(CilType type)
	{
		if (type.Kind == CilTypeKind.ManagedReference)
		{
			return true;
		}
		if (type.IsSupportedScalar &&
			type.Kind is not
				CilTypeKind.ManagedPointer and not CilTypeKind.GenericParameter)
		{
			return false;
		}
		return TryGetStructLayout(type, _assemblyName, out var layout)
			? layout.ReferenceBitmap != 0
			: null;
	}

	private MethodSignature<CilType> SubstituteTypeArguments(
		MethodSignature<CilType> signature,
		ImmutableArray<CilType> typeArguments) =>
		new(
			signature.Header,
			SubstituteTypeArguments(signature.ReturnType, typeArguments),
			signature.RequiredParameterCount,
			signature.GenericParameterCount,
			signature.ParameterTypes
				.Select(parameter => SubstituteTypeArguments(parameter, typeArguments))
				.ToImmutableArray());

	private MethodReference ResolveExperimentalNumberGroupSizesReader(FrameworkMemberId member, MethodSignature<CilType> signature)
	{
		var fieldName = member.Name switch { "CurrencyGroupSizes" => "_currencyGroupSizes", "PercentGroupSizes" => "_percentGroupSizes", _ => "_numberGroupSizes" };
		// Execute the implementation pack's own field reader. No host field
		// offsets or alternate NumberFormatInfo layout are introduced here.
		var module = GetOrLoadImplementationModule("System.Private.CoreLib");
		if (module is not null)
		foreach (var typeHandle in module.Reader.TypeDefinitions)
		{
			if (module.GetTypeName(typeHandle) != "System.Number") continue;
			var type = module.Reader.GetTypeDefinition(typeHandle);
			foreach (var handle in type.GetMethods())
			{
				var definition = module.Reader.GetMethodDefinition(handle);
				if (module.Reader.GetString(definition.Name) != member.Name || definition.GetGenericParameters().Count != 0) continue;
				var method = module.GetMethod(handle);
				if (!SignaturesMatch(method.Signature, signature)) continue;
				var body = method.Instructions;
				if (body.Count == 3 && body[0].OpCode == OpCodes.Ldarg_0 && body[1].OpCode == OpCodes.Ldfld &&
					body[1].Operand is int token && body[2].OpCode == OpCodes.Ret)
				{
					var field = module.ResolveFieldToken(token, method, body[1].Offset);
					if (!field.IsStatic && field.ModuleName == "System.Private.CoreLib" &&
						field.DisplayName == $"System.Globalization.NumberFormatInfo::{fieldName}" && field.Type.DisplayName == "int[]")
					{
						// This verified leaf reads no System.Number static state. The
						// runtime reader must not trigger its allocating cache initializer.
						var shadow = new FrameworkShadowMethod("System.Private.CoreLib", "System.Number", member.Name);
						var binding = new FrameworkBinding(member, FrameworkBindingKind.ShadowMethod,
							$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
							new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory, [FrameworkFeature.ManagedObjects]),
							Reason: "The verified instance-field reader has no dependency on Number's formatting caches.",
							ShadowMethod: shadow, TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
						return MethodReference.ForShadowBinding(binding, method, signature);
					}
				}
			}
		}
		throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata,
			$"The experimental {member.Name} reader requires the verified CoreLib int[] field-reader body.");
	}

	private MethodReference ResolveExperimentalCustomNumberFormatting(FrameworkMemberId member, MethodSignature<CilType> signature, bool standard = false)
	{
		var shim = TryResolveManagedMethod("CopperSharp.Runtime.Managed", member.DeclaringType.FullMetadataName!, member.Name, signature);
		if (shim is null || signature.Header.IsInstance || signature.ParameterTypes.Length != (standard ? 5 : 4) ||
			signature.ReturnType.Kind != CilTypeKind.Void ||
			signature.ParameterTypes[0].DisplayName != "CopperSharp.Runtime.ShadowValueListBuilder`1<char>&" ||
			signature.ParameterTypes[1].DisplayName != "CopperSharp.Runtime.ShadowNumberBuffer&" ||
			(standard ? signature.ParameterTypes[2].DisplayName != "char" || signature.ParameterTypes[3].DisplayName != "int" :
				signature.ParameterTypes[2].DisplayName != "System.ReadOnlySpan`1<char>") ||
			signature.ParameterTypes[^1].DisplayName != "System.Globalization.NumberFormatInfo")
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Custom number formatting requires the exact runtime renderer declaration.");
		var owner = GetOrLoadImplementationModule("System.Private.CoreLib")!;
		foreach (var typeHandle in owner.Reader.TypeDefinitions)
		{
			if (owner.GetTypeName(typeHandle) != "System.Number") continue;
			foreach (var handle in owner.Reader.GetTypeDefinition(typeHandle).GetMethods())
			{
				var definition = owner.Reader.GetMethodDefinition(handle);
				if (owner.Reader.GetString(definition.Name) != (standard ? "NumberToString" : "NumberToStringFormat") || definition.GetGenericParameters().Count != 1) continue;
				var method = owner.GetConstructedMethod(handle, null, [new CilType(CilTypeKind.Character, 2, "char")]);
				if (method.Signature.ParameterTypes.Length != signature.ParameterTypes.Length ||
					method.Signature.ParameterTypes[0].DisplayName != "System.Collections.Generic.ValueListBuilder`1<char>&" ||
					method.Signature.ParameterTypes[1].DisplayName != "System.Number/NumberBuffer&") continue;
				var adapted = new MethodSignature<CilType>(method.Signature.Header, method.Signature.ReturnType,
					method.Signature.RequiredParameterCount, 0, method.Signature.ParameterTypes.SetItem(0, signature.ParameterTypes[0]).SetItem(1, signature.ParameterTypes[1]));
				if (!SignaturesMatch(adapted, signature)) continue;
				foreach (var type in new[] { signature.ParameterTypes[0].ElementType!, signature.ParameterTypes[1].ElementType!, method.Signature.ParameterTypes[0].ElementType!, method.Signature.ParameterTypes[1].ElementType! })
					if (!TryGetReferenceFreeStructLayout(type, type.DisplayName.StartsWith("CopperSharp.", StringComparison.Ordinal) ? "CopperSharp.Runtime.Managed" : "System.Private.CoreLib", out _))
						throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, $"Custom number formatting requires a verified digit or character buffer layout for '{type.DisplayName}'.");
				var shadow = new FrameworkShadowMethod("System.Private.CoreLib", "System.Number", method.Name);
				var binding = new FrameworkBinding(member, FrameworkBindingKind.ShadowMethod,
					$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
					new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
						FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
						[FrameworkFeature.ManagedStrings, FrameworkFeature.Spans, FrameworkFeature.ManagedGc]),
					Reason: "Numeric formatting executes the verified CoreLib renderer.", ShadowMethod: shadow,
					TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
				return MethodReference.ForShadowBinding(binding, method, signature);
			}
		}
		throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Custom number formatting needs the verified CoreLib renderer signature.");
	}

	private CilMethod? TryResolveManagedMethod(
		string assemblyName,
		string typeName,
		string methodName,
		MethodSignature<CilType> signature,
		CilType? constructedDeclaringType = null,
		IReadOnlyList<CilType>? methodTypeArguments = null)
	{
		var module = GetOrLoadModule(assemblyName);
		if (module is null)
		{
			return null;
		}

		var methodArguments = methodTypeArguments?.ToImmutableArray() ??
			ImmutableArray<CilType>.Empty;
		foreach (var typeHandle in module.Reader.TypeDefinitions)
		{
			var type = module.Reader.GetTypeDefinition(typeHandle);
			if (!string.Equals(
					module.GetTypeName(typeHandle).Replace('/', '+'),
					typeName.Replace('/', '+'),
					StringComparison.Ordinal))
			{
				continue;
			}

			foreach (var methodHandle in type.GetMethods())
			{
				var definition = module.Reader.GetMethodDefinition(methodHandle);
				if (module.Reader.GetString(definition.Name) != methodName) continue;
				if (definition.GetGenericParameters().Count != methodArguments.Length)
				{
					continue;
				}
				var candidate = methodArguments.Length == 0
					? module.GetMethod(methodHandle)
					: module.GetConstructedMethod(
						methodHandle,
						constructedDeclaringType: null,
						methodArguments);
				var signatureMatches = constructedDeclaringType is null
					? SignaturesMatch(candidate.Signature, signature)
					: ConstructedSignaturesMatch(
						candidate.Signature,
						signature,
						constructedDeclaringType.GenericArguments);
				if (candidate.Name == methodName && signatureMatches)
				{
					if (constructedDeclaringType is not null &&
						methodArguments.Length == 0 &&
							UsesPrivateShadowConstruction(typeName))
					{
						var definitionType = module._signatureProvider
							.GetTypeFromDefinition(
								module.Reader,
								typeHandle,
								module.IsValueTypeDefinition(type) ? (byte)0x11 : (byte)0x12);
						var shadowConstruction = definitionType with
						{
							DisplayName =
								$"{definitionType.DisplayName}<" +
								$"{string.Join(",", constructedDeclaringType.GenericArguments.Select(static argument => argument.DisplayName))}>",
							GenericArguments =
								constructedDeclaringType.GenericArguments
						};
						return module.GetConstructedMethod(
							methodHandle,
							shadowConstruction,
							ImmutableArray<CilType>.Empty);
					}
					return constructedDeclaringType is null || methodArguments.Length != 0
						? candidate
						: module.GetConstructedMethod(
							methodHandle,
							constructedDeclaringType,
							ImmutableArray<CilType>.Empty);
				}
			}
		}

		return null;
	}

	private CilMethod? ResolvePinnedImplementationMethod(
		FrameworkBinding binding,
		MethodSignature<CilType> publicSignature,
		CilType? constructedDeclaringType,
		IReadOnlyList<CilType>? methodTypeArguments)
	{
		const string assemblyName = "System.Private.CoreLib";
		var declaringTypeIdentity = binding.Member.DeclaringType.Kind ==
				FrameworkTypeKind.GenericInstantiation
			? binding.Member.DeclaringType.ElementType!
			: binding.Member.DeclaringType;
		var typeName = declaringTypeIdentity.FullMetadataName ??
			throw UnsupportedPinnedBody(binding, "declaring type identity is unavailable");
		var module = GetOrLoadImplementationModule(assemblyName) ??
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidInput,
				$"Verified framework implementation assembly '{assemblyName}' could not be loaded.");
		var methodArguments = methodTypeArguments?.ToImmutableArray() ??
			ImmutableArray<CilType>.Empty;
		CilMethod? match = null;
		foreach (var typeHandle in module.Reader.TypeDefinitions)
		{
			var type = module.Reader.GetTypeDefinition(typeHandle);
			if (!string.Equals(
					module.GetTypeName(typeHandle).Replace('/', '+'),
					typeName.Replace('/', '+'),
					StringComparison.Ordinal))
			{
				continue;
			}
			var genericTypeParameterCount = type.GetGenericParameters().Count;
			if (genericTypeParameterCount !=
					(constructedDeclaringType?.GenericArguments.Length ?? 0))
			{
				return null;
			}
			if (binding.TypeInitializerPolicy == FrameworkTypeInitializerPolicy.TargetOwned)
			{
				ValidatePinnedTypeLayout(module, typeHandle, binding);
			}
			CilType? implementationConstruction = null;
			if (genericTypeParameterCount != 0)
			{
				var definitionType = module._signatureProvider.GetTypeFromDefinition(
					module.Reader,
					typeHandle,
					module.IsValueTypeDefinition(type) ? (byte)0x11 : (byte)0x12);
				implementationConstruction = definitionType with
				{
					DisplayName = constructedDeclaringType!.DisplayName,
					Size = constructedDeclaringType.IsNullable ? constructedDeclaringType.Size : definitionType.Size,
					GenericArguments = constructedDeclaringType.GenericArguments
				};
			}

			foreach (var methodHandle in type.GetMethods())
			{
				var definition = module.Reader.GetMethodDefinition(methodHandle);
				if (!string.Equals(
						module.Reader.GetString(definition.Name),
						binding.Member.Name,
						StringComparison.Ordinal) ||
					definition.GetGenericParameters().Count != methodArguments.Length)
				{
					continue;
				}
				var candidateSignature = definition.DecodeSignature(
					module._signatureProvider,
					new CilGenericContext(
						implementationConstruction?.GenericArguments ??
							ImmutableArray<CilType>.Empty,
						methodArguments));
				if (!SignaturesMatch(candidateSignature, publicSignature))
				{
					continue;
				}
				ValidatePinnedMethodDefinition(module, definition, binding);
				var candidate = module.GetConstructedMethod(
					methodHandle,
					implementationConstruction,
					methodArguments);
				if (match is not null)
				{
					throw UnsupportedPinnedBody(
						binding,
						"implementation member identity is ambiguous");
				}
				match = candidate;
			}
		}
		return match;
	}

	private static void ValidatePinnedTypeLayout(
		CompilationModule module,
		TypeDefinitionHandle typeHandle,
		FrameworkBinding binding)
	{
		var layout = module.GetTypeLayout(typeHandle);
		var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var pair in layout.FieldOffsets)
		{
			var field = module.Reader.GetFieldDefinition(pair.Key);
			offsets.Add(module.Reader.GetString(field.Name), pair.Value);
		}
		var typeName = binding.Member.DeclaringType.MetadataName;
		if (typeName == "System.Diagnostics.Stopwatch" && layout.Size == 28 &&
			layout.ReferenceBitmap == 0 &&
			offsets.Count == 3 &&
			offsets.TryGetValue("_elapsed", out var elapsed) && elapsed == 8 &&
			offsets.TryGetValue("_startTimeStamp", out var started) && started == 16 &&
			offsets.TryGetValue("_isRunning", out var running) && running == 24)
		{
			return;
		}
		// Value-type layouts use payload coordinates without an object header.
		if (typeName == "System.TimeSpan" && layout.Size == 8 &&
			layout.ReferenceBitmap == 0 &&
			offsets.Count == 1 &&
			offsets.TryGetValue("_ticks", out var ticks) && ticks == 0)
		{
			return;
		}
		// System.SR is a static helper. Resource-key mode owns its initialization,
		// and its implementation type has no instance state to transport.
		if (typeName == "System.SR" && layout.Size == 8 &&
			layout.ReferenceBitmap == 0 && offsets.Count == 0)
		{
			return;
		}

		throw UnsupportedPinnedBody(
			binding,
			$"implementation type '{typeName}' has layout " +
			$"size={layout.Size}, references=0x{layout.ReferenceBitmap:X8}, fields=" +
			string.Join(",", offsets.OrderBy(static item => item.Key, StringComparer.Ordinal)
				.Select(static item => $"{item.Key}@{item.Value}")));
	}

	private static void ValidatePinnedMethodDefinition(
		CompilationModule module,
		MethodDefinition definition,
		FrameworkBinding binding)
	{
		var implementation = definition.ImplAttributes;
		var isAbstract = (definition.Attributes & MethodAttributes.Abstract) != 0;
		if ((!isAbstract && definition.RelativeVirtualAddress == 0) ||
			(definition.Attributes & MethodAttributes.PinvokeImpl) != 0 ||
			(implementation & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL ||
			(implementation & MethodImplAttributes.InternalCall) != 0)
		{
			throw UnsupportedPinnedBody(
				binding,
				$"implementation method '{module.Reader.GetString(definition.Name)}' is native, runtime-provided, abstract, or has no CIL body");
		}
	}

	private static M68kCompilationException UnsupportedPinnedBody(
		FrameworkBinding binding,
		string reason) =>
		new(
			M68kDiagnosticIds.UnsupportedFrameworkMember,
			$"Pinned framework binding '{binding.Member.DisplayName}' cannot be used because {reason}.");

	private static bool UsesPrivateShadowConstruction(string typeName) =>
		typeName is
			"CopperSharp.Runtime.ShadowGenericJoinEnumerator`1" or
			"CopperSharp.Runtime.ShadowEqualityComparer`1" or
			"CopperSharp.Runtime.ShadowValueListBuilder`1" or
			"CopperSharp.Runtime.IShadowEqualityComparer`1" or
			"CopperSharp.Runtime.ShadowDictionary`2" or
			"CopperSharp.Runtime.ShadowPrimaryOrderedEnumerable`1" or
			"CopperSharp.Runtime.ShadowOrderedEnumerable`1" or
			"CopperSharp.Runtime.ShadowOrderedEnumerator`1";

	private CompilationModule GetCallerModule(CilMethod caller, int ilOffset)
	{
		if (_root._modules.TryGetValue(caller.ModuleName, out var module))
		{
			_root._reachableAssemblyNames.Add(module._assemblyName);
			return module;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.InvalidMetadata,
			$"Metadata module '{caller.ModuleName}' is not loaded.",
			caller.DisplayName,
			ilOffset);
	}

	private CompilationModule? GetOrLoadModule(string assemblyName)
	{
		if (string.IsNullOrEmpty(assemblyName))
		{
			return null;
		}

		if (_root._modules.TryGetValue(assemblyName, out var module))
		{
			_root._reachableAssemblyNames.Add(module._assemblyName);
			return module;
		}

		if (!_root._managedAssemblyPaths.TryGetValue(assemblyName, out var path))
		{
			return null;
		}

		if (!File.Exists(path))
		{
			return null;
		}

		module = new CompilationModule(path, _externalCallResolvers, _root);
		_root._reachableAssemblyNames.Add(module._assemblyName);
		return module;
	}

	private CompilationModule? GetOrLoadImplementationModule(
		string assemblyName,
		bool markReachable = true)
	{
		if (FrameworkImplementationPack is null ||
			!FrameworkImplementationPack.TryGetAssemblyPath(assemblyName, out var path))
		{
			return null;
		}
		if (_root._modules.TryGetValue(assemblyName, out var loaded))
		{
			if (!string.Equals(loaded._assemblyPath, path, StringComparison.OrdinalIgnoreCase))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidInput,
					$"Framework implementation assembly identity '{assemblyName}' collides with separately loaded managed assembly '{loaded._assemblyPath}'.");
			}
			if (markReachable)
			{
				_root._reachableAssemblyNames.Add(loaded._assemblyName);
			}
			return loaded;
		}
		var module = new CompilationModule(path, _externalCallResolvers, _root);
		if (markReachable)
		{
			_root._reachableAssemblyNames.Add(module._assemblyName);
		}
		return module;
	}

	private CilType? ResolveReferencedEnumType(
		MetadataReader reader,
		TypeReference reference,
		string displayName)
	{
		EntityHandle scope = reference.ResolutionScope;
		while (scope.Kind == HandleKind.TypeReference)
		{
			scope = reader.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
		}
		if (scope.Kind != HandleKind.AssemblyReference)
		{
			return null;
		}

		var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)scope);
		var assemblyName = reader.GetString(assembly.Name);
		var module = GetOrLoadModule(assemblyName);
		if (module is not null &&
			module._signatureProvider.TryGetDefinedEnumType(
				module.Reader,
				displayName,
				out var enumType)) return enumType;
		// Framework contracts forward enum definitions to the verified CoreLib.
		// Consult the released metadata for stable enum identity as well;
		// matching names from unrelated assemblies must retain their own identity.
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) &&
			Net10FrameworkContract.Default.IsFrameworkAssembly(assemblyName) &&
			GetOrLoadImplementationModule("System.Private.CoreLib") is { } implementation &&
			implementation._signatureProvider.TryGetDefinedEnumType(implementation.Reader, displayName, out enumType))
			return enumType;
		return null;
	}


	private static string? TryGetExternalImportName(
		IReadOnlyList<M68kMetadataAttribute> attributes)
	{
		var attribute = attributes.FirstOrDefault(static attribute =>
			string.Equals(
				attribute.TypeName,
				typeof(M68kImportAttribute).FullName,
				StringComparison.Ordinal));
		if (attribute is null)
		{
			return null;
		}

		if (attribute.FixedArguments.Count != 1 ||
			attribute.FixedArguments[0] is not string name ||
			string.IsNullOrWhiteSpace(name))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"[M68kImport] must contain one non-empty symbol name.");
		}

		return name;
	}

	private CilRegisterAbi? GetExternalImportAbi(
		M68kExternalMethod method,
		MethodSignature<CilType> signature,
		string displayName)
	{
		if (!method.IsStatic)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Imported methods must be static.",
				displayName);
		}

		var parameterRegisters = new M68kRegister[signature.ParameterTypes.Length];
		var hasRegister = new bool[parameterRegisters.Length];
		M68kRegister? returnRegister = TryGetRegister(method.ReturnAttributes);
		var hasAnyRegister = returnRegister is not null;
		if (method.ParameterAttributes.Count != parameterRegisters.Length)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"Imported method metadata parameter count does not match its signature.",
				displayName);
		}

		for (var index = 0; index < parameterRegisters.Length; index++)
		{
			var register = TryGetRegister(method.ParameterAttributes[index]);
			if (register is null)
			{
				continue;
			}

			hasAnyRegister = true;
			parameterRegisters[index] = register.Value;
			hasRegister[index] = true;
		}

		if (!hasAnyRegister)
		{
			return null;
		}

		if (hasRegister.Any(static present => !present))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Every register-ABI import parameter must carry [M68kRegister].",
				displayName);
		}

		return new CilRegisterAbi(parameterRegisters, returnRegister ?? M68kRegister.D0);
	}


	private CilField GetField(FieldDefinitionHandle handle)
	{
		if (_fieldCache.TryGetValue(handle, out var cached))
		{
			return cached;
		}

		var definition = Reader.GetFieldDefinition(handle);
		var declaringType = definition.GetDeclaringType();
		var typeDefinition = Reader.GetTypeDefinition(declaringType);
		var name = Reader.GetString(definition.Name);
		var field = new CilField(
			handle,
			declaringType,
			$"{GetTypeName(typeDefinition)}::{name}",
			definition.DecodeSignature(_signatureProvider, CilGenericContext.Empty),
			(definition.Attributes & FieldAttributes.Static) != 0,
			ModuleName: _assemblyName);
		_fieldCache.Add(handle, field);
		return field;
	}

	private CilField GetFieldForCaller(
		FieldDefinitionHandle handle,
		CilMethod caller)
	{
		var field = GetField(handle);
		if (caller.ConstructedDeclaringType is not { } constructedType ||
			field.DeclaringType != caller.DeclaringType)
		{
			return field;
		}

		var specializedType = SubstituteTypeArguments(
			field.Type,
			constructedType.GenericArguments);
		return field with
		{
			DisplayName =
				$"{constructedType.DisplayName}::{Reader.GetString(Reader.GetFieldDefinition(handle).Name)}",
			Type = specializedType,
			ConstructedDeclaringType = constructedType
		};
	}

	private CilField ResolveFieldMemberReference(
		MemberReferenceHandle handle,
		CilMethod caller,
		int ilOffset)
	{
		var member = Reader.GetMemberReference(handle);
		if (member.Parent.Kind == HandleKind.TypeSpecification)
		{
			var parentType = Reader
				.GetTypeSpecification((TypeSpecificationHandle)member.Parent)
				.DecodeSignature(_signatureProvider, caller.GenericContext);
			var hasConstructedTarget = TryFindConstructedGenericDefinition(parentType, out var constructedTarget);
			if (!hasConstructedTarget && IsCompositeFormatSegment(parentType) &&
				TryGetReferenceFreeStructLayout(parentType, caller.ModuleName, out _))
			{
				constructedTarget = ResolveRuntimeTypeIdentity(parentType, caller.ModuleName);
				hasConstructedTarget = true;
			}
			if (parentType.GenericArguments.Length != 0 && hasConstructedTarget)
			{
				var targetModule = GetModule(constructedTarget.ModuleName);
				var definition = targetModule.Reader.GetTypeDefinition(
					(TypeDefinitionHandle)constructedTarget.Handle);
				var constructedName = Reader.GetString(member.Name);
				var constructedFieldType = member.DecodeFieldSignature(
					_signatureProvider,
					new CilGenericContext(parentType.GenericArguments, []));
				foreach (var fieldHandle in definition.GetFields())
				{
					var field = targetModule.GetField(fieldHandle);
					var specializedType = SubstituteTypeArguments(
						field.Type,
						parentType.GenericArguments);
					if (!field.DisplayName.EndsWith($"::{constructedName}", StringComparison.Ordinal) ||
						!StringComparer.Ordinal.Equals(
							specializedType.DisplayName,
							constructedFieldType.DisplayName))
					{
						continue;
					}
					return field with
					{
						DisplayName =
							$"{parentType.DisplayName}::{constructedName}",
						Type = specializedType,
						ConstructedDeclaringType = parentType
					};
				}
			}
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Could not resolve constructed field '{parentType.DisplayName}::{Reader.GetString(member.Name)}' with type '{member.DecodeFieldSignature(_signatureProvider, new CilGenericContext(parentType.GenericArguments, [])).DisplayName}'.",
				caller.DisplayName,
				ilOffset);
		}
		if (member.Parent.Kind != HandleKind.TypeDefinition)
		{
			if (member.Parent.Kind == HandleKind.TypeReference)
			{
				return ResolveExternalFieldMemberReference(member, caller, ilOffset);
			}

			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"External fields are not supported.",
				caller.DisplayName,
				ilOffset);
		}

		var name = Reader.GetString(member.Name);
		var fieldType = member.DecodeFieldSignature(_signatureProvider, CilGenericContext.Empty);
		var type = Reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent);
		foreach (var fieldHandle in type.GetFields())
		{
			var field = GetField(fieldHandle);
			if (field.DisplayName.EndsWith($"::{name}", StringComparison.Ordinal) &&
				field.Type.DisplayName == fieldType.DisplayName)
			{
				return field;
			}
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.InvalidMetadata,
			$"Could not resolve field '{name}'.",
			caller.DisplayName,
			ilOffset);
	}

	private CilField ResolveExternalFieldMemberReference(
		MemberReference member,
		CilMethod caller,
		int ilOffset)
	{
		var reference = Reader.GetTypeReference((TypeReferenceHandle)member.Parent);
		var assemblyName = GetReferencedAssemblyName(reference.ResolutionScope);
		var typeName = GetTypeName(reference);
		var fieldName = Reader.GetString(member.Name);
		var fieldType = member.DecodeFieldSignature(_signatureProvider, CilGenericContext.Empty);
		if (TryResolveTargetRuntimeField(assemblyName, typeName, fieldName, fieldType, caller, ilOffset) is { } targetField)
			return targetField;
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) &&
			Net10FrameworkContract.Default.IsFrameworkAssembly(assemblyName) && typeName == "System.Decimal" &&
			fieldName is "Zero" or "One" or "MinusOne" &&
			fieldType is { Kind: CilTypeKind.ValueType, Size: 0, DisplayName: "System.Decimal" } &&
			TryGetDecimalLayout(fieldType, out _) &&
			TryResolveManagedField("System.Private.CoreLib", typeName, fieldName, fieldType) is { IsStatic: true } decimalConstant)
		{
			var owner = GetModule(decimalConstant.ModuleName);
			var attributes = owner.Reader.GetFieldDefinition(decimalConstant.Handle).Attributes;
			if ((attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public ||
				(attributes & (FieldAttributes.Static | FieldAttributes.InitOnly)) != (FieldAttributes.Static | FieldAttributes.InitOnly))
				throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Decimal constant requires the verified public static readonly field.", caller.DisplayName, ilOffset);
			if (caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset)?.OpCode != OpCodes.Ldsfld)
				throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedInstruction, $"Framework field '{typeName}::{fieldName}' is admitted as a read-only static field.", caller.DisplayName, ilOffset);
			return TryResolveManagedField("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowDecimalConstants", fieldName, fieldType)
				?? throw new M68kCompilationException(M68kDiagnosticIds.InvalidInput, "The target Decimal constants are missing from the supplied managed runtime.", caller.DisplayName, ilOffset);
		}
		if (FrameworkBindingRegistry.TryBindReadOnlyStaticField(
				assemblyName,
				typeName,
				fieldName,
				fieldType) is { } fieldBinding &&
			GetOrLoadModule(fieldBinding.ShadowAssemblyName) is not null)
		{
			var instruction = caller.Instructions.FirstOrDefault(
				candidate => candidate.Offset == ilOffset);
			if (instruction?.OpCode != System.Reflection.Emit.OpCodes.Ldsfld)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedInstruction,
					$"Framework field '{typeName}::{fieldName}' is admitted as a read-only static field.",
					caller.DisplayName,
					ilOffset);
			}

			var shadowField = TryResolveManagedField(
				fieldBinding.ShadowAssemblyName,
				fieldBinding.ShadowTypeName,
				fieldBinding.ShadowFieldName,
				fieldType);
			if (shadowField is null || !shadowField.IsStatic)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidInput,
					$"Managed framework field target '{fieldBinding.ShadowTypeName}::{fieldBinding.ShadowFieldName}' could not be resolved from the supplied target runtime assemblies.",
					caller.DisplayName,
					ilOffset);
			}
			return shadowField;
		}
		if (TryResolveManagedField(assemblyName, typeName, fieldName, fieldType) is { } managedField)
		{
			return managedField;
		}
		var path = Path.Combine(_assemblyDirectory, assemblyName + ".dll");
		if (!File.Exists(path))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"External fields are not supported.",
				caller.DisplayName,
				ilOffset);
		}

		var type = Assembly.LoadFrom(path).GetType(typeName, throwOnError: false);
		var fields = type?
			.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			.Where(static field => !field.IsStatic)
			.OrderBy(static field => field.MetadataToken)
			.ToArray() ?? Array.Empty<FieldInfo>();
		var offset = 0;
		foreach (var field in fields)
		{
			if (!IsReflectionStructField(field.FieldType) &&
				!IsReflectionFixedBufferField(field.FieldType))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					$"Field '{typeName}::{field.Name}' has unsupported type '{ReflectionDisplayName(field.FieldType)}'.",
					caller.DisplayName,
					ilOffset);
			}

			offset = Align(offset, ReflectionFieldSize(field.FieldType) >= 2 ? 2 : 1);

			if (field.Name == fieldName)
			{
				return new CilField(
					default,
					default,
					$"{typeName}::{fieldName}",
					fieldType,
					IsStatic: false,
					ExternalOffset: offset);
			}

			offset += ReflectionFieldSize(field.FieldType);
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.InvalidMetadata,
			$"Could not resolve field '{typeName}::{fieldName}'.",
			caller.DisplayName,
			ilOffset);
	}

	private CilField? TryResolveTargetRuntimeField(
		string assemblyName, string typeName, string fieldName, CilType fieldType,
		CilMethod caller, int ilOffset)
	{
		var binding = FrameworkImplementationProfile.TryCreateTargetRuntimeFieldOverride(
			assemblyName, typeName, fieldName, fieldType.DisplayName,
			FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true);
		if (binding is null) return null;
		if (caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset)?.OpCode != OpCodes.Ldsfld)
			throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedInstruction,
				$"Framework field '{typeName}::{fieldName}' is admitted as a read-only static field.",
				caller.DisplayName, ilOffset);
		return TryResolveManagedField(binding.ShadowAssemblyName, binding.ShadowTypeName, binding.ShadowFieldName, fieldType) ??
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidInput,
				$"Target runtime field '{binding.ShadowTypeName}::{binding.ShadowFieldName}' could not be resolved.");
	}

	private CilField? TryResolveManagedField(
		string assemblyName,
		string typeName,
		string fieldName,
		CilType fieldType)
	{
		var module = GetOrLoadModule(assemblyName);
		if (module is null)
		{
			return null;
		}

		foreach (var typeHandle in module.Reader.TypeDefinitions)
		{
			var type = module.Reader.GetTypeDefinition(typeHandle);
			if (!string.Equals(
					module.GetTypeName(typeHandle).Replace('/', '+'),
					typeName.Replace('/', '+'),
					StringComparison.Ordinal))
			{
				continue;
			}

			foreach (var fieldHandle in type.GetFields())
			{
				var field = module.GetField(fieldHandle);
				if (field.DisplayName.EndsWith($"::{fieldName}", StringComparison.Ordinal) &&
					field.Type.DisplayName == fieldType.DisplayName)
				{
					return field;
				}
			}
		}

		return null;
	}

	private CilType ResolveReferencedType(TypeReferenceHandle handle)
	{
		if (_referencedTypeCache.TryGetValue(handle, out var cached))
		{
			return cached;
		}

		var reference = Reader.GetTypeReference(handle);
		var name = GetTypeName(reference);
		var result = name switch
		{
			"System.Boolean" => new(CilTypeKind.Boolean, 1, "bool"),
			"System.Char" => new(CilTypeKind.Character, 2, "char"),
			"System.SByte" => new(CilTypeKind.SignedInteger, 1, "sbyte"),
			"System.Byte" => new(CilTypeKind.UnsignedInteger, 1, "byte"),
			"System.Int16" => new(CilTypeKind.SignedInteger, 2, "short"),
			"System.UInt16" => new(CilTypeKind.UnsignedInteger, 2, "ushort"),
			"System.Int32" => new(CilTypeKind.SignedInteger, 4, "int"),
			"System.UInt32" => new(CilTypeKind.UnsignedInteger, 4, "uint"),
			"System.Int64" => new(CilTypeKind.SignedInteger, 8, "long"),
			"System.UInt64" => new(CilTypeKind.UnsignedInteger, 8, "ulong"),
			"System.IntPtr" => new(CilTypeKind.NativeInteger, 4, "nint"),
			"System.UIntPtr" => new(CilTypeKind.NativeInteger, 4, "nuint"),
			"System.Single" => new(CilTypeKind.FloatingPoint, 4, "float"),
			"System.Double" => new(CilTypeKind.FloatingPoint, 8, "double"),
			"System.String" => new(CilTypeKind.ManagedReference, 4, "string"),
			"System.Object" => new(CilTypeKind.ManagedReference, 4, "object"),
			_ => ResolveNonPrimitiveReferencedType(handle, reference, name)
		};
		_referencedTypeCache.Add(handle, result);
		return result;
	}

	private CilType ResolveNonPrimitiveReferencedType(
		TypeReferenceHandle handle,
		TypeReference reference,
		string name)
	{
		if (ResolveReferencedEnumType(Reader, reference, name) is { } enumType) return enumType;
		var assemblyName = GetReferencedAssemblyName(reference.ResolutionScope);
		var module = GetOrLoadModule(assemblyName);
		// Contract facades contain forwarders rather than definitions. Resolve
		// their token kind from pinned metadata, just as signature decoding does.
		var implementation = (FrameworkImplementationPack?.EnableUnlistedManagedBodies == true ||
			FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && name == "System.Decimal" &&
			(assemblyName is "System.Runtime" or "System.Private.CoreLib") &&
			TryGetDecimalLayout(new CilType(CilTypeKind.ValueType, 0, "System.Decimal"), out _)) &&
			Net10FrameworkContract.Default.IsFrameworkAssembly(assemblyName)
			? GetOrLoadImplementationModule("System.Private.CoreLib") : null;
		foreach (var definitionModule in new[] { module, implementation }.Distinct())
		{
			if (definitionModule is null) continue;
			foreach (var definitionHandle in definitionModule.Reader.TypeDefinitions)
			{
				var definition = definitionModule.Reader.GetTypeDefinition(definitionHandle);
				if (!string.Equals(
						definitionModule.GetTypeName(definitionHandle).Replace('/', '+'),
						name.Replace('/', '+'),
						StringComparison.Ordinal))
				{
					continue;
				}

				var rawTypeKind = definitionModule.IsValueTypeDefinition(definition)
					? (byte)0x11
					: (byte)0x12;
				return _signatureProvider.GetTypeFromReference(Reader, handle, rawTypeKind);
			}
		}

		return _signatureProvider.GetTypeFromReference(Reader, handle, 0x12);
	}

	private bool HasAttribute(CustomAttributeHandleCollection handles, string fullName) =>
		handles.Any(handle => string.Equals(GetAttributeTypeName(handle), fullName, StringComparison.Ordinal));

	private IReadOnlyList<M68kMetadataAttribute> DecodeAttributes(CustomAttributeHandleCollection handles)
	{
		var result = new List<M68kMetadataAttribute>();
		foreach (var handle in handles)
		{
			var attribute = Reader.GetCustomAttribute(handle);
			var value = attribute.DecodeValue(new AttributeTypeProvider(Reader));
			result.Add(new M68kMetadataAttribute(
				GetAttributeTypeName(handle),
				value.FixedArguments.Select(argument => argument.Value).ToArray()));
		}
		return result;
	}

	private string? TryGetImportName(CustomAttributeHandleCollection handles)
	{
		foreach (var handle in handles)
		{
			if (!string.Equals(
				GetAttributeTypeName(handle),
				typeof(M68kImportAttribute).FullName,
				StringComparison.Ordinal))
			{
				continue;
			}

			var attribute = Reader.GetCustomAttribute(handle);
			var value = attribute.DecodeValue(new AttributeTypeProvider(Reader));
			if (value.FixedArguments.Length != 1 ||
				value.FixedArguments[0].Value is not string name ||
				string.IsNullOrWhiteSpace(name))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"[M68kImport] must contain one non-empty symbol name.");
			}

			return name;
		}

		return null;
	}

	private string? TryGetExportName(CustomAttributeHandleCollection handles)
	{
		foreach (var handle in handles)
		{
			if (!string.Equals(
				GetAttributeTypeName(handle),
				typeof(M68kExportAttribute).FullName,
				StringComparison.Ordinal))
			{
				continue;
			}

			var attribute = Reader.GetCustomAttribute(handle);
			var value = attribute.DecodeValue(new AttributeTypeProvider(Reader));
			return value.FixedArguments.Length == 0
				? string.Empty
				: value.FixedArguments[0].Value as string ?? string.Empty;
		}

		return null;
	}

	private string? TryGetBoopsiDispatcherName(CustomAttributeHandleCollection handles)
	{
		foreach (var handle in handles)
		{
			if (!string.Equals(
				GetAttributeTypeName(handle),
				BoopsiDispatcherAttributeName,
				StringComparison.Ordinal))
			{
				continue;
			}

			var attribute = Reader.GetCustomAttribute(handle);
			var value = attribute.DecodeValue(new AttributeTypeProvider(Reader));
			return value.FixedArguments.Length == 0 || value.FixedArguments[0].Value is null
				? string.Empty
				: value.FixedArguments[0].Value as string ?? throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"[BOOPSI.Dispatcher] name must be a string.");
		}

		return null;
	}

	private string? TryGetMuiListDisplayCallbackName(CustomAttributeHandleCollection handles)
	{
		foreach (var handle in handles)
		{
			if (!string.Equals(
				GetAttributeTypeName(handle),
				MuiListDisplayCallbackAttributeName,
				StringComparison.Ordinal))
			{
				continue;
			}

			var attribute = Reader.GetCustomAttribute(handle);
			var value = attribute.DecodeValue(new AttributeTypeProvider(Reader));
			return value.FixedArguments.Length == 0 || value.FixedArguments[0].Value is null
				? string.Empty
				: value.FixedArguments[0].Value as string ?? throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"[MUI.List.DisplayCallback] name must be a string.");
		}

		return null;
	}

	private M68kRegister? TryGetRegister(CustomAttributeHandleCollection handles)
	{
		foreach (var handle in handles)
		{
			if (!string.Equals(
				GetAttributeTypeName(handle),
				typeof(M68kRegisterAttribute).FullName,
				StringComparison.Ordinal))
			{
				continue;
			}

			var attribute = Reader.GetCustomAttribute(handle);
			var value = attribute.DecodeValue(new AttributeTypeProvider(Reader));
			if (value.FixedArguments.Length != 1 ||
				value.FixedArguments[0].Value is not int register ||
				!Enum.IsDefined(typeof(M68kRegister), register))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"[M68kRegister] contains an invalid register value.");
			}

			return (M68kRegister)register;
		}

		return null;
	}

	private static M68kRegister? TryGetRegister(
		IReadOnlyList<M68kMetadataAttribute> attributes)
	{
		foreach (var attribute in attributes)
		{
			if (!string.Equals(
				attribute.TypeName,
				typeof(M68kRegisterAttribute).FullName,
				StringComparison.Ordinal))
			{
				continue;
			}

			if (attribute.FixedArguments.Count != 1 ||
			attribute.FixedArguments[0] is not int register ||
				!Enum.IsDefined(typeof(M68kRegister), register))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"[M68kRegister] contains an invalid register value.");
			}

			return (M68kRegister)register;
		}

		return null;
	}

	private string GetAttributeTypeName(CustomAttributeHandle handle)
	{
		var attribute = Reader.GetCustomAttribute(handle);
		var constructor = attribute.Constructor;
		EntityHandle parent = constructor.Kind switch
		{
			HandleKind.MethodDefinition =>
				Reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
			HandleKind.MemberReference =>
				Reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
			_ => default
		};

		return parent.Kind switch
		{
			HandleKind.TypeDefinition => GetTypeName(Reader.GetTypeDefinition((TypeDefinitionHandle)parent)),
			HandleKind.TypeReference => GetTypeName(Reader.GetTypeReference((TypeReferenceHandle)parent)),
			_ => string.Empty
		};
	}

	private string GetTypeName(TypeDefinition definition)
		=> QualifiedName(definition.Namespace, definition.Name);

	private string GetTypeName(TypeDefinitionHandle handle)
	{
		var definition = Reader.GetTypeDefinition(handle);
		var declaringType = definition.GetDeclaringType();
		return declaringType.IsNil
			? GetTypeName(definition)
			: $"{GetTypeName(declaringType)}/{Reader.GetString(definition.Name)}";
	}

	private string GetTypeName(TypeReference reference)
	{
		var name = QualifiedName(reference.Namespace, reference.Name);
		if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
		{
			var declaringType = Reader.GetTypeReference(
				(TypeReferenceHandle)reference.ResolutionScope);
			return $"{GetTypeName(declaringType)}+{name}";
		}

		return name;
	}

	internal string GetTypeDisplayName(EntityHandle handle) =>
		handle.Kind switch
		{
			HandleKind.TypeDefinition =>
				GetTypeName(Reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
			HandleKind.TypeReference =>
				GetTypeName(Reader.GetTypeReference((TypeReferenceHandle)handle)),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Metadata handle '{handle.Kind}' is not a named type.")
		};

	internal EntityHandle GetBaseType(TypeDefinitionHandle handle) =>
		Reader.GetTypeDefinition(handle).BaseType;

	private string QualifiedName(StringHandle namespaceHandle, StringHandle nameHandle)
	{
		var typeNamespace = Reader.GetString(namespaceHandle);
		var name = Reader.GetString(nameHandle);
		return string.IsNullOrEmpty(typeNamespace) ? name : $"{typeNamespace}.{name}";
	}

	private static bool SignaturesMatch(
		MethodSignature<CilType> left,
		MethodSignature<CilType> right)
	{
		if (left.Header.IsInstance != right.Header.IsInstance ||
			left.ParameterTypes.Length != right.ParameterTypes.Length ||
			left.ReturnType.DisplayName.Replace('/', '+') != right.ReturnType.DisplayName.Replace('/', '+'))
		{
			return false;
		}

		for (var i = 0; i < left.ParameterTypes.Length; i++)
		{
			if (left.ParameterTypes[i].DisplayName.Replace('/', '+') != right.ParameterTypes[i].DisplayName.Replace('/', '+'))
			{
				return false;
			}
		}

		return true;
	}

	private sealed class AttributeTypeProvider : ICustomAttributeTypeProvider<string>
	{
		private readonly MetadataReader _reader;

		public AttributeTypeProvider(MetadataReader reader)
		{
			_reader = reader;
		}

		public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

		public string GetSystemType() => "System.Type";

		public bool IsSystemType(string type) => type == "System.Type";

		public string GetSZArrayType(string elementType) => $"{elementType}[]";

		public string GetTypeFromDefinition(
			MetadataReader reader,
			TypeDefinitionHandle handle,
			byte rawTypeKind)
		{
			var definition = reader.GetTypeDefinition(handle);
			return GetName(definition.Namespace, definition.Name);
		}

		public string GetTypeFromReference(
			MetadataReader reader,
			TypeReferenceHandle handle,
			byte rawTypeKind)
		{
			var reference = reader.GetTypeReference(handle);
			return GetName(reference.Namespace, reference.Name);
		}

		public string GetTypeFromSerializedName(string name) => name;

		public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

		private string GetName(StringHandle namespaceHandle, StringHandle nameHandle)
		{
			var typeNamespace = _reader.GetString(namespaceHandle);
			var name = _reader.GetString(nameHandle);
			return string.IsNullOrEmpty(typeNamespace) ? name : $"{typeNamespace}.{name}";
		}
	}

	private sealed class FrameworkSignatureTypeProvider(
		CompilationModule module) :
		ISignatureTypeProvider<FrameworkTypeId, FrameworkGenericContext>
	{
		public FrameworkTypeId GetArrayType(FrameworkTypeId elementType, ArrayShape shape) =>
			FrameworkTypeId.Array(elementType, shape);

		public FrameworkTypeId GetByReferenceType(FrameworkTypeId elementType) =>
			FrameworkTypeId.ByReference(elementType);

		public FrameworkTypeId GetFunctionPointerType(
			MethodSignature<FrameworkTypeId> signature) =>
			FrameworkTypeId.FunctionPointer(FrameworkMethodSignatureId.From(signature));

		public FrameworkTypeId GetGenericInstantiation(
			FrameworkTypeId genericType,
			ImmutableArray<FrameworkTypeId> typeArguments) =>
			FrameworkTypeId.GenericInstantiation(genericType, typeArguments);

		public FrameworkTypeId GetGenericMethodParameter(
			FrameworkGenericContext genericContext,
			int index) =>
			index >= 0 && index < genericContext.MethodArguments.Length
				? genericContext.MethodArguments[index]
				: FrameworkTypeId.GenericMethodParameter(index);

		public FrameworkTypeId GetGenericTypeParameter(
			FrameworkGenericContext genericContext,
			int index) =>
			index >= 0 && index < genericContext.TypeArguments.Length
				? genericContext.TypeArguments[index]
				: FrameworkTypeId.GenericTypeParameter(index);

		public FrameworkTypeId GetModifiedType(
			FrameworkTypeId modifier,
			FrameworkTypeId unmodifiedType,
			bool isRequired) =>
			FrameworkTypeId.Modified(modifier, unmodifiedType, isRequired);

		public FrameworkTypeId GetPinnedType(FrameworkTypeId elementType) => elementType;

		public FrameworkTypeId GetPointerType(FrameworkTypeId elementType) =>
			FrameworkTypeId.Pointer(elementType);

		public FrameworkTypeId GetPrimitiveType(PrimitiveTypeCode typeCode) =>
			FrameworkTypeId.Primitive(typeCode switch
			{
				PrimitiveTypeCode.Void => "System.Void",
				PrimitiveTypeCode.Boolean => "System.Boolean",
				PrimitiveTypeCode.Char => "System.Char",
				PrimitiveTypeCode.SByte => "System.SByte",
				PrimitiveTypeCode.Byte => "System.Byte",
				PrimitiveTypeCode.Int16 => "System.Int16",
				PrimitiveTypeCode.UInt16 => "System.UInt16",
				PrimitiveTypeCode.Int32 => "System.Int32",
				PrimitiveTypeCode.UInt32 => "System.UInt32",
				PrimitiveTypeCode.Int64 => "System.Int64",
				PrimitiveTypeCode.UInt64 => "System.UInt64",
				PrimitiveTypeCode.IntPtr => "System.IntPtr",
				PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
				PrimitiveTypeCode.Single => "System.Single",
				PrimitiveTypeCode.Double => "System.Double",
				PrimitiveTypeCode.Object => "System.Object",
				PrimitiveTypeCode.String => "System.String",
				PrimitiveTypeCode.TypedReference => "System.TypedReference",
				_ => throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					$"Primitive signature type '{typeCode}' is not supported.")
			});

		public FrameworkTypeId GetSZArrayType(FrameworkTypeId elementType) =>
			FrameworkTypeId.SzArray(elementType);

		public FrameworkTypeId GetTypeFromDefinition(
			MetadataReader reader,
			TypeDefinitionHandle handle,
			byte rawTypeKind)
		{
			var definition = reader.GetTypeDefinition(handle);
			var declaringHandle = definition.GetDeclaringType();
			var declaringType = declaringHandle.IsNil
				? null
				: GetTypeFromDefinition(reader, declaringHandle, rawTypeKind);
			return FrameworkTypeId.Named(
				module._assemblyName,
				MetadataTypeName(
					reader,
					definition.Namespace,
					definition.Name,
					declaringType is not null),
				declaringType);
		}

		public FrameworkTypeId GetTypeFromReference(
			MetadataReader reader,
			TypeReferenceHandle handle,
			byte rawTypeKind)
		{
			var reference = reader.GetTypeReference(handle);
			var declaringType = reference.ResolutionScope.Kind == HandleKind.TypeReference
				? GetTypeFromReference(
					reader,
					(TypeReferenceHandle)reference.ResolutionScope,
					rawTypeKind)
				: null;
			var assemblyName = declaringType?.AssemblyName ??
				module.GetReferencedAssemblyName(reference.ResolutionScope);
			if (string.IsNullOrEmpty(assemblyName))
			{
				assemblyName = module._assemblyName;
			}
			return FrameworkTypeId.Named(
				assemblyName,
				MetadataTypeName(
					reader,
					reference.Namespace,
					reference.Name,
					declaringType is not null),
				declaringType);
		}

		public FrameworkTypeId GetTypeFromSpecification(
			MetadataReader reader,
			FrameworkGenericContext genericContext,
			TypeSpecificationHandle handle,
			byte rawTypeKind) => reader
			.GetTypeSpecification(handle)
			.DecodeSignature(this, genericContext);

		private static string MetadataTypeName(
			MetadataReader reader,
			StringHandle namespaceHandle,
			StringHandle nameHandle,
			bool isNested)
		{
			var name = reader.GetString(nameHandle);
			if (isNested)
			{
				return name;
			}
			var typeNamespace = reader.GetString(namespaceHandle);
			return string.IsNullOrEmpty(typeNamespace)
				? name
				: $"{typeNamespace}.{name}";
		}
	}

	private sealed class DeclaringAssemblyTypeProvider(
		CompilationModule module) :
		ISignatureTypeProvider<string?, CilGenericContext>
	{
		public string? GetArrayType(string? elementType, ArrayShape shape) => elementType;

		public string? GetByReferenceType(string? elementType) => elementType;

		public string? GetFunctionPointerType(MethodSignature<string?> signature) => null;

		public string? GetGenericInstantiation(
			string? genericType,
			ImmutableArray<string?> typeArguments) => genericType;

		public string? GetGenericMethodParameter(CilGenericContext genericContext, int index) => null;

		public string? GetGenericTypeParameter(CilGenericContext genericContext, int index) => null;

		public string? GetModifiedType(string? modifier, string? unmodifiedType, bool isRequired) =>
			unmodifiedType;

		public string? GetPinnedType(string? elementType) => elementType;

		public string? GetPointerType(string? elementType) => elementType;

		public string? GetPrimitiveType(PrimitiveTypeCode typeCode) => null;

		public string? GetSZArrayType(string? elementType) => elementType;

		public string? GetTypeFromDefinition(
			MetadataReader reader,
			TypeDefinitionHandle handle,
			byte rawTypeKind) => module._assemblyName;

		public string? GetTypeFromReference(
			MetadataReader reader,
			TypeReferenceHandle handle,
			byte rawTypeKind)
		{
			var reference = reader.GetTypeReference(handle);
			return module.GetReferencedAssemblyName(reference.ResolutionScope);
		}

		public string? GetTypeFromSpecification(
			MetadataReader reader,
			CilGenericContext genericContext,
			TypeSpecificationHandle handle,
			byte rawTypeKind) => reader
			.GetTypeSpecification(handle)
			.DecodeSignature(this, genericContext);
	}
}

internal sealed record CilMethod(
	MethodDefinitionHandle Handle,
	TypeDefinitionHandle DeclaringType,
	string DisplayName,
	string Name,
	MethodSignature<CilType> Signature,
	ImmutableArray<CilType> Locals,
	IReadOnlyList<CilInstruction> Instructions,
	IReadOnlyList<CilExceptionRegion> ExceptionRegions,
	bool InitializeLocals,
	string? ImportName,
	CilRegisterAbi? ImportAbi,
	CilExternalCall? ExternalCall,
	string ModuleName = "",
	MethodAttributes Attributes = 0,
	TypeAttributes DeclaringTypeAttributes = 0,
	ImmutableArray<ParameterAttributes> ParameterFlags = default,
	CilType? ConstructedDeclaringType = null,
	ImmutableArray<CilType> MethodTypeArguments = default,
	MethodImplAttributes ImplAttributes = 0)
{
	// Slot discovery needs signatures, while executable bodies are materialized
	// only when reached. An unsupported unused reflection body cannot poison a
	// canonical RuntimeType's ToString slot.
	public bool HasDeferredBody { get; init; }

	public string Construction => FormatConstruction(
		ConstructedDeclaringType,
		MethodTypeArguments.IsDefault
			? ImmutableArray<CilType>.Empty
			: MethodTypeArguments);

	public CilMethodIdentity Identity => new(ModuleName, Handle, Construction);

	public CilGenericContext GenericContext => new(
		ConstructedDeclaringType?.GenericArguments ?? ImmutableArray<CilType>.Empty,
		MethodTypeArguments.IsDefault
			? ImmutableArray<CilType>.Empty
			: MethodTypeArguments);

	public static string FormatConstruction(
		CilType? constructedDeclaringType,
		ImmutableArray<CilType> methodTypeArguments) =>
		constructedDeclaringType is null && methodTypeArguments.Length == 0
			? string.Empty
			: $"type={constructedDeclaringType?.DisplayName ?? string.Empty};method=" +
				string.Join(",", methodTypeArguments.Select(static type => type.DisplayName));

	public bool IsImport => ImportName is not null || ExternalCall is not null;

	public bool IsAbstract => (Attributes & MethodAttributes.Abstract) != 0;

	public bool IsTypeInitializer =>
		Name == ".cctor" &&
		(Attributes & (MethodAttributes.Static | MethodAttributes.SpecialName)) ==
			(MethodAttributes.Static | MethodAttributes.SpecialName);

	public bool IsVirtual => (Attributes & MethodAttributes.Virtual) != 0;

	public bool IsFinal => (Attributes & MethodAttributes.Final) != 0;

	public bool IsNewSlot => (Attributes & MethodAttributes.NewSlot) != 0;

	public bool DeclaringTypeIsInterface =>
		(DeclaringTypeAttributes & TypeAttributes.Interface) != 0;

	public bool DeclaringTypeIsSealed =>
		(DeclaringTypeAttributes & TypeAttributes.Sealed) != 0;

	public int ParameterCount =>
		Signature.ParameterTypes.Length + (Signature.Header.IsInstance ? 1 : 0);
}

internal sealed record CilExceptionRegion(
	ExceptionRegionKind Kind,
	int TryOffset,
	int TryLength,
	int HandlerOffset,
	int HandlerLength,
	EntityHandle CatchType,
	int FilterOffset)
{
	public int TryEnd => checked(TryOffset + TryLength);

	public int HandlerEnd => checked(HandlerOffset + HandlerLength);

	public bool IsCatch => Kind == ExceptionRegionKind.Catch;

	public bool IsFinally => Kind == ExceptionRegionKind.Finally;
}

internal sealed record CilField(
	FieldDefinitionHandle Handle,
	TypeDefinitionHandle DeclaringType,
	string DisplayName,
	CilType Type,
	bool IsStatic,
	int? ExternalOffset = null,
	string ModuleName = "",
	CilType? ConstructedDeclaringType = null)
{
	public CilFieldIdentity Identity => new(
		ModuleName,
		Handle,
		ConstructedDeclaringType?.DisplayName ?? string.Empty);
}

internal sealed record CilTypeLayout(
	TypeDefinitionHandle Handle,
	string DisplayName,
	int Size,
	uint ReferenceBitmap,
	IReadOnlyDictionary<FieldDefinitionHandle, int> FieldOffsets,
	string ModuleName = "",
	CilType? ConstructedType = null)
{
	// Reference-bearing single-word structs use the same address-based value
	// representation and stack ABI as larger aggregates, including return buffers.
	public bool UsesAggregateTransport => Size > 4 || ReferenceBitmap != 0;

	public CilTypeIdentity Identity => new(
		ModuleName,
		Handle,
		ConstructedType?.DisplayName ?? string.Empty);
}

internal sealed record CilRuntimeTypeTarget(
	CilType Type,
	string ModuleName,
	EntityHandle Handle,
	bool IsInterface,
	bool IsArray,
	bool IsConstructedGeneric = false);

internal sealed record CilVirtualTable(
	CilTypeLayout Type,
	ImmutableArray<CilMethod> Slots);

internal sealed record CilInterfaceDefinition(
	CilTypeIdentity Identity,
	string DisplayName,
	ImmutableArray<CilMethod> Slots,
	CilType? ConstructedType = null);

internal sealed record CilInterfaceImplementation(
	CilTypeLayout Type,
	CilInterfaceDefinition Interface,
	ImmutableArray<CilMethod> Methods);

internal readonly record struct CilInterfaceImplementationIdentity(
	CilTypeIdentity Type,
	CilTypeIdentity Interface);

internal readonly record struct CilMethodIdentity(
	string ModuleName,
	MethodDefinitionHandle Handle,
	string Construction = "");

internal readonly record struct CilFieldIdentity(
	string ModuleName,
	FieldDefinitionHandle Handle,
	string Construction = "");

internal readonly record struct CilTypeIdentity(
	string ModuleName,
	TypeDefinitionHandle Handle,
	string Construction = "");

internal readonly record struct CilUserStringIdentity(
	string ModuleName,
	int Token);

internal sealed record CilExport(
	CilMethod Method,
	string Name,
	IReadOnlyList<M68kRegister> ParameterRegisters,
	M68kRegister ReturnRegister);

internal sealed record CilRegisterAbi(
	IReadOnlyList<M68kRegister> ParameterRegisters,
	M68kRegister ReturnRegister);

internal sealed record CilExternalCall(
	M68kExternalCallConvention Convention,
	CilRegisterAbi Abi);

internal sealed record FrameworkVirtualFallback(
	FrameworkBinding Binding,
	CilMethod Method);

internal sealed record MethodReference(
	CilMethod? Definition,
	string? ImportName,
	MethodSignature<CilType> Signature,
	FrameworkBinding? FrameworkBinding = null,
	CilType? ConstructedDeclaringType = null,
	bool IsConstructorFactory = false,
	CilTypeLayout? AllocationLayout = null)
{
	public static MethodReference ForDefinition(
		CilMethod method,
		CilType? constructedDeclaringType = null) =>
		new(
			method,
			method.ImportName,
			method.Signature,
			ConstructedDeclaringType: constructedDeclaringType);

	public static MethodReference ForIntrinsic(
		string name,
		MethodSignature<CilType> signature) =>
		new(null, name, signature);

	public static MethodReference ForBinding(
		FrameworkBinding binding,
		MethodSignature<CilType> signature,
		CilType? constructedDeclaringType = null) =>
		new(
			null,
			binding.Target,
			signature,
			binding,
			constructedDeclaringType);

	public static MethodReference ForShadowBinding(
		FrameworkBinding binding,
		CilMethod shadowMethod,
		MethodSignature<CilType> publicSignature) =>
		new(shadowMethod, shadowMethod.ImportName, publicSignature, binding);

	public static MethodReference ForManagedBinding(
		FrameworkBinding binding,
		CilMethod managedMethod,
		MethodSignature<CilType> publicSignature) =>
		new(managedMethod, managedMethod.ImportName, publicSignature, binding, managedMethod.ConstructedDeclaringType);

	public int ParameterCount =>
		Signature.ParameterTypes.Length + (Signature.Header.IsInstance ? 1 : 0);
}

internal sealed record CilMethodReferenceIdentity(
	string AssemblyName,
	string TypeName,
	string Name,
	bool IsStatic,
	int GenericArity,
	string ReturnType,
	ImmutableArray<string> ParameterTypes,
	ImmutableArray<string> MethodTypeArguments)
{
	public string Key => string.Join(
		'\u001f',
		AssemblyName,
		TypeName,
		Name,
		IsStatic,
		GenericArity,
		ReturnType,
		string.Join('\u001e', ParameterTypes),
		string.Join('\u001e', MethodTypeArguments));
}
