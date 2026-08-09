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

internal sealed class CompilationModule : IDisposable
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
	private readonly Dictionary<(TypeDefinitionHandle Handle, string Construction), CilMethod?> _typeInitializerCache = new();
	private readonly Dictionary<FieldDefinitionHandle, CilField> _fieldCache = new();
	private readonly Dictionary<TypeDefinitionHandle, CilTypeLayout> _layoutCache = new();
	private readonly Dictionary<(TypeDefinitionHandle Handle, string Construction), CilTypeLayout>
		_constructedLayoutCache = new();
	private readonly Dictionary<(TypeDefinitionHandle Handle, string Construction), CilVirtualTable>
		_virtualTableCache = new();
	private readonly Dictionary<(TypeDefinitionHandle Handle, string Construction), CilInterfaceDefinition>
		_interfaceCache = new();
	private readonly Dictionary<CilInterfaceImplementationIdentity, CilInterfaceImplementation?> _interfaceImplementationCache = new();
	private readonly Dictionary<string, bool> _transparentScalarTypeCache = new(StringComparer.Ordinal);
	private readonly IReadOnlyList<IM68kExternalCallResolver> _externalCallResolvers;
	private readonly string _assemblyPath;
	private readonly string _assemblyDirectory;
	private readonly CompilationModule _root;
	private readonly Dictionary<string, CompilationModule> _modules;
	private readonly Dictionary<CilMethodIdentity, FrameworkVirtualFallback>
		_frameworkVirtualFallbacks;
	private readonly IReadOnlyDictionary<string, string> _managedAssemblyPaths;
	private readonly FrameworkImplementationPackCatalog? _frameworkImplementationPack;
	private string _assemblyName = string.Empty;

	public CompilationModule(
		string assemblyPath,
		IReadOnlyList<IM68kExternalCallResolver>? externalCallResolvers = null,
		IReadOnlyList<string>? managedAssemblyPaths = null,
		FrameworkImplementationPackCatalog? frameworkImplementationPack = null)
		: this(assemblyPath, externalCallResolvers, root: null)
	{
		_managedAssemblyPaths = CreateManagedAssemblyPathMap(
			managedAssemblyPaths ?? Array.Empty<string>());
		_frameworkImplementationPack = frameworkImplementationPack;
		if (_frameworkImplementationPack is not null)
		{
			// Layout can be needed before the first pinned method body is bound.
			// Load the verified CoreLib now so identity resolution never records a
			// synthetic nil-handle layout for an implementation-owned type.
			_ = GetOrLoadImplementationModule("System.Private.CoreLib");
		}
	}

	internal FrameworkImplementationPackCatalog? FrameworkImplementationPack =>
		_root._frameworkImplementationPack;

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
		_frameworkVirtualFallbacks = root?._frameworkVirtualFallbacks ??
			new Dictionary<CilMethodIdentity, FrameworkVirtualFallback>();
		_managedAssemblyPaths = root?._managedAssemblyPaths ??
			new Dictionary<string, string>(StringComparer.Ordinal);
		_frameworkImplementationPack = root?._frameworkImplementationPack;
		_signatureProvider = new CilSignatureTypeProvider(ResolveReferencedEnumType);
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
		ImmutableArray<CilType> methodTypeArguments)
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

		if (importName is null && externalCall is null)
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
				methodTypeArguments);
		_methodCache.Add(cacheKey, method);
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
		return handle.Kind switch
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
		if (!definition.BaseType.IsNil &&
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
				aggregateLayout.Size > 4)
			{
				fieldOffsets.Add(fieldHandle, size);
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
				if (field.Type.IsReference && fieldIndex is >= 0 and < 32)
			{
				bitmap |= 1u << fieldIndex;
			}

			fieldOffsets.Add(fieldHandle, size);
			size += 4;
		}

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

	public CilTypeLayout GetTypeLayout(CilField field)
	{
		var module = GetModule(field.ModuleName);
		return field.ConstructedDeclaringType is { } constructedType
			? module.GetConstructedTypeLayout(field.DeclaringType, constructedType)
			: module.GetTypeLayout(field.DeclaringType);
	}

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

	public int GetVirtualSlot(CilMethod method) =>
		GetModule(method.ModuleName).GetVirtualSlotCore(method);

	public IReadOnlyList<CilMethod> GetVirtualImplementations(CilMethod declaration) =>
		_root._frameworkVirtualFallbacks.ContainsKey(declaration.Identity)
			? GetFrameworkVirtualImplementations(declaration)
			: GetModule(declaration.ModuleName).GetVirtualImplementationsCore(declaration);

	public CilMethod? TryGetVirtualImplementation(
		CilTypeLayout layout,
		CilMethod declaration)
	{
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

	public CilInterfaceDefinition GetInterfaceDefinition(CilMethod method) =>
		GetModule(method.ModuleName).GetInterfaceDefinition(
			method.DeclaringType,
			method.ConstructedDeclaringType);

	public int GetInterfaceSlot(CilMethod method) =>
		GetModule(method.ModuleName).GetInterfaceSlotCore(method);

	public IReadOnlyList<CilMethod> GetInterfaceImplementations(CilMethod declaration) =>
		GetModule(declaration.ModuleName).GetInterfaceImplementationsCore(declaration);

	public IReadOnlyList<CilMethod> GetInterfaceTableImplementations(CilMethod declaration) =>
		GetInterfaceDefinition(declaration).Slots
			.SelectMany(GetInterfaceImplementations)
			.DistinctBy(static method => method.Identity)
			.ToArray();

	public CilInterfaceImplementation? TryGetInterfaceImplementation(
		CilTypeLayout layout,
		CilInterfaceDefinition interfaceDefinition)
	{
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
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				"Phase 5D constrained dispatch currently supports interface methods on closed value types; constrained object and non-interface virtual methods are not supported yet.",
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
				out var constrainedLayout))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				$"Constrained receiver '{constrainedType.DisplayName}' is not a compiler-supported closed value type.",
				caller.DisplayName,
				ilOffset);
		}
		var interfaceDefinition = GetInterfaceDefinition(declaration);
		var implementation = TryGetInterfaceImplementation(
			constrainedLayout,
			interfaceDefinition) ??
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				$"Constrained receiver '{constrainedType.DisplayName}' does not implement '{interfaceDefinition.DisplayName}'.",
				caller.DisplayName,
				ilOffset);
		return implementation.Methods[GetInterfaceSlot(declaration)];
	}

	public EntityHandle GetBaseType(CilTypeLayout layout) =>
		GetModule(layout.ModuleName).GetBaseType(layout.Handle);

	public string GetTypeDisplayName(EntityHandle handle, CilTypeLayout owner) =>
		GetModule(owner.ModuleName).GetTypeDisplayName(handle);

	private CompilationModule GetModule(string moduleName) =>
		string.IsNullOrEmpty(moduleName) || string.Equals(moduleName, _assemblyName, StringComparison.Ordinal)
			? this
			: _root._modules[moduleName];

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
				ImmutableArray<CilType>.Empty);
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

	private IReadOnlyList<CilMethod> GetFrameworkVirtualImplementations(
		CilMethod declaration)
	{
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
			if (!TryResolveImplementedInterface(
					parent,
					constructedType,
					out var parentHandle,
					out var parentConstruction))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedPolymorphism,
					$"Interface '{constructedType?.DisplayName ?? GetTypeName(type)}' inherits an interface whose exact construction cannot be resolved in its managed module.");
			}

			foreach (var method in GetInterfaceDefinition(
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
			if (interfaceDefinition.Slots[index].Identity == method.Identity)
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
		if (!string.Equals(interfaceDefinition.Identity.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				$"Cross-module interface implementation map from " +
				$"'{_assemblyName}:{GetTypeName(Reader.GetTypeDefinition(typeHandle))}' " +
				$"to '{interfaceDefinition.DisplayName}' in " +
				$"'{interfaceDefinition.Identity.ModuleName}' is not supported yet.");
		}

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

		if (!ImplementsInterface(typeHandle, constructedType, interfaceDefinition))
		{
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
		return TryResolveImplementedInterface(
				implemented,
				constructedType,
				out var interfaceHandle,
				out var interfaceConstruction) &&
			InterfaceExtends(
				interfaceHandle,
				interfaceConstruction,
				interfaceDefinition,
				new HashSet<CilTypeIdentity>());
	}

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
			if (TryResolveImplementedInterface(
					parent,
					interfaceConstruction,
					out var parentHandle,
					out var parentConstruction) &&
				InterfaceExtends(
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
					ImmutableArray<CilType>.Empty);
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
				ImmutableArray<CilType>.Empty));
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
				if ((definition.Attributes & MethodAttributes.Static) != 0 ||
					(definition.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
				{
					continue;
				}

				var candidate = GetConstructedMethod(
					methodHandle,
					currentConstruction,
					ImmutableArray<CilType>.Empty);
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
		}

		return false;
	}

	public bool IsTransparentScalarType(CilType type)
	{
		if (type.Kind != CilTypeKind.ValueType)
		{
			return false;
		}

		if (_transparentScalarTypeCache.TryGetValue(type.DisplayName, out var cached))
		{
			return cached;
		}

		var result = IsTransparentScalarType(type.DisplayName);
		_transparentScalarTypeCache.Add(type.DisplayName, result);
		return result;
	}

	public bool IsSupportedNullableType(CilType type) =>
		type.NullableElementType is { } element &&
		(element.IsSupportedScalar && element.Size == 4 ||
		 IsTransparentScalarType(element));

	public bool IsValueTypeConstructor(CilMethod method)
	{
		if (!string.IsNullOrEmpty(method.ModuleName) &&
			!string.Equals(method.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(method, 0).IsValueTypeConstructor(method);
		}

		return method.Name == ".ctor" &&
			!method.DeclaringType.IsNil &&
			IsValueTypeDefinition(Reader.GetTypeDefinition(method.DeclaringType));
	}

	public bool IsSupportedStructType(CilType type)
	{
		if (IsSupportedSpanLikeType(type) ||
			IsSupportedMemoryLikeType(type) ||
			IsDefaultInterpolatedStringHandler(type) ||
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
			return fields.Length != 0;
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
			if (IsSupportedNullableType(type))
		{
			layout = new CilTypeLayout(
				default,
				type.DisplayName,
				type.NullableElementType is { } element &&
					IsTransparentScalarType(element)
						? 4
						: 8,
				0,
				new Dictionary<FieldDefinitionHandle, int>(),
				preferredModuleName);
			return true;
		}
			if (!TryGetStructLayout(type, preferredModuleName, out layout))
			{
				return false;
			}
			return layout.ReferenceBitmap == 0;
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
			type.GenericArguments is not [var element] ||
			!element.IsSupportedScalar ||
			element.Kind is CilTypeKind.ManagedPointer or CilTypeKind.GenericParameter)
		{
			return false;
		}

		var currentBytes = Math.Max(4, element.Size);
		var referenceBitmap = 1u;
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

	public bool IsTransparentScalarField(CilField field) =>
		IsTransparentScalarType(new CilType(
			CilTypeKind.ValueType,
			4,
			field.DisplayName.Split("::", StringSplitOptions.None)[0])) &&
		field.Type.IsSupportedScalar &&
		field.Type.Size == 4;

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
			fields[0].Type.IsSupportedScalar &&
			fields[0].Type.Size == 4;
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
		definition.BaseType.Kind == HandleKind.TypeReference &&
		GetTypeName(Reader.GetTypeReference((TypeReferenceHandle)definition.BaseType)) == "System.ValueType";

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
			HandleKind.TypeSpecification when type.GenericArguments.Length != 0 &&
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

		var definition = Reader.GetTypeDefinition(handle);
		var isValueType = IsValueTypeDefinition(definition);
		var inheritedSize = isValueType ? 0 : 8;
		var inheritedBitmap = 0u;
		var fieldOffsets = new Dictionary<FieldDefinitionHandle, int>();
		if (definition.BaseType.Kind == HandleKind.TypeDefinition)
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
				aggregateLayout.Size > 4)
			{
				fieldOffsets.Add(fieldHandle, size);
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
			if (fieldType.Kind == CilTypeKind.ManagedReference &&
				fieldIndex is >= 0 and < 32)
			{
				bitmap |= 1u << fieldIndex;
			}
			fieldOffsets.Add(fieldHandle, size);
			size = checked(size + fieldStorageSize);
		}

		var layout = new CilTypeLayout(
			handle,
			constructedType.DisplayName,
			size,
			bitmap,
			fieldOffsets,
			_assemblyName,
			constructedType);
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
		foreach (var handle in Reader.TypeDefinitions)
		{
			var definition = Reader.GetTypeDefinition(handle);
			var signatureName = _signatureProvider
				.GetTypeFromDefinition(Reader, handle, 0x12)
				.DisplayName;
			if (!StringComparer.Ordinal.Equals(signatureName, type.DisplayName))
			{
				continue;
			}
			target = new CilRuntimeTypeTarget(
				type,
				_assemblyName,
				handle,
				(definition.Attributes & TypeAttributes.Interface) != 0,
				IsArray: false);
			return true;
		}
		target = null!;
		return false;
	}

	public bool IsUninitializedStorageType(CilType type)
	{
		foreach (var handle in Reader.TypeDefinitions)
		{
			var definition = Reader.GetTypeDefinition(handle);
			if (!TypeNameMatches(definition, type.DisplayName))
			{
				continue;
			}

			return HasAttribute(definition.GetCustomAttributes(), UninitializedStorageAttributeName);
		}

		return HasReflectionAttribute(type.DisplayName, UninitializedStorageAttributeName);
	}

	public bool RequiresLongAlignedStackAddress(CilType type)
	{
		if (!IsUninitializedStorageType(type))
		{
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
				return value.FixedArguments.Length == 1 &&
					value.FixedArguments[0].Value is int alignment &&
					alignment >= 4;
			}
		}

		return HasReflectionAttribute(type.DisplayName, StackAlignmentAttributeName);
	}

	private bool HasReflectionAttribute(string displayName, string attributeName)
	{
		foreach (var path in Directory.EnumerateFiles(_assemblyDirectory, "*.dll"))
		{
			try
			{
				var type = Assembly.LoadFrom(path).GetType(displayName.Replace('/', '+'), throwOnError: false);
				if (type is null)
				{
					continue;
				}

				return type.CustomAttributes.Any(attribute =>
					string.Equals(attribute.AttributeType.FullName, attributeName, StringComparison.Ordinal));
			}
			catch (BadImageFormatException)
			{
			}
		}

		return false;
	}

	public MethodReference ResolveMethodToken(int token, CilMethod caller, int ilOffset)
	{
		if (!string.IsNullOrEmpty(caller.ModuleName) &&
			!string.Equals(caller.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			return GetCallerModule(caller, ilOffset).ResolveMethodToken(token, caller, ilOffset);
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
			HandleKind.MethodDefinition => ResolveMethodDefinition(
				(MethodDefinitionHandle)handle),
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
	}

	public CilMethodReferenceIdentity? DescribeMethodToken(
		int token,
		CilMethod caller,
		int ilOffset)
	{
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
			HandleKind.MethodDefinition => null,
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Token 0x{token:X8} is not a method reference.",
				caller.DisplayName,
				ilOffset)
		};
	}

	public FrameworkMemberId DescribeFrameworkMethodToken(
		int token,
		CilMethod caller,
		int ilOffset)
	{
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
				[]),
			HandleKind.MethodSpecification => DescribeFrameworkMethodSpecification(
				(MethodSpecificationHandle)handle),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Token 0x{token:X8} is not a method reference.",
				caller.DisplayName,
				ilOffset)
		};
	}

	private FrameworkMemberId DescribeFrameworkMethodSpecification(
		MethodSpecificationHandle handle)
	{
		var provider = new FrameworkSignatureTypeProvider(this);
		var specification = Reader.GetMethodSpecification(handle);
		var arguments = specification.DecodeSignature(
			provider,
			FrameworkGenericContext.Empty);
		return specification.Method.Kind switch
		{
			HandleKind.MethodDefinition => DescribeFrameworkMethodDefinition(
				(MethodDefinitionHandle)specification.Method,
				arguments),
			HandleKind.MemberReference => DescribeFrameworkMemberReference(
				(MemberReferenceHandle)specification.Method,
				arguments),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"A method specification must refer to a method definition or member reference.")
		};
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
		IReadOnlyList<FrameworkTypeId> methodTypeArguments)
	{
		var provider = new FrameworkSignatureTypeProvider(this);
		var member = Reader.GetMemberReference(handle);
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
				FrameworkGenericContext.Empty,
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
		if (specification.Method.Kind != HandleKind.MemberReference)
		{
			return null;
		}

		var arguments = specification
			.DecodeSignature(_signatureProvider, CilGenericContext.Empty)
			.Select(static argument => argument.DisplayName)
			.ToImmutableArray();
		return DescribeMemberReference(
			(MemberReferenceHandle)specification.Method,
			arguments);
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

	private MethodReference ResolveMethodDefinition(MethodDefinitionHandle handle)
	{
		var method = GetMethod(handle);
		var declaringType = Reader.GetTypeDefinition(
			Reader.GetMethodDefinition(handle).GetDeclaringType());
		return TryResolveRegisteredBinding(
				handle,
				GetTypeName(declaringType),
				method.Signature,
				constructedDeclaringType: null) ??
			MethodReference.ForDefinition(method);
	}

	private MethodReference ResolveMethodSpecification(
		MethodSpecificationHandle handle,
		CilMethod caller,
		int ilOffset)
	{
		var specification = Reader.GetMethodSpecification(handle);
		var arguments = specification.DecodeSignature(
			_signatureProvider,
			caller.GenericContext);
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

			var exactMember = DescribeFrameworkMethodSpecification(handle);
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
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Generic methods declared outside the input assembly are not supported.",
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
			"System.IntPtr" => "nint",
			"System.UIntPtr" => "nuint",
			"System.String" => "string",
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

	private static bool ConstructedSignaturesMatch(
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
			!left.GenericArguments.SequenceEqual(right.GenericArguments))
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

	private static CilType SubstituteTypeArguments(
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
			ElementType = elementType,
			GenericArguments = genericArguments
		};
	}

	private MethodReference? TryResolveRegisteredBinding(
		MethodDefinitionHandle handle,
		string typeName,
		MethodSignature<CilType> signature,
		CilType? constructedDeclaringType)
	{
		var member = DescribeFrameworkMethodDefinition(handle, []);
		return TryResolveRegisteredBinding(
			member,
			typeName,
			signature,
			constructedDeclaringType);
	}

	private MethodReference? TryResolveRegisteredBinding(
		MemberReferenceHandle handle,
		string typeName,
		MethodSignature<CilType> signature,
		CilType? constructedDeclaringType,
		CilMethod? caller = null,
		int ilOffset = -1)
	{
		var member = DescribeFrameworkMemberReference(handle, []);
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
		var context = new FrameworkBindingContext(
				typeName,
				signature,
				constructedDeclaringType,
				constructedDeclaringType is not null &&
					IsSupportedNullableType(constructedDeclaringType),
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
		var binding = FrameworkBindingRegistry.TryBind(lookupMember, context);
		if (FrameworkImplementationPack is not null &&
			FrameworkImplementationProfile.TryCreatePinnedBinding(
				member,
				binding,
				out var pinnedBinding))
		{
			var implementation = ResolvePinnedImplementationMethod(
				pinnedBinding,
				signature);
			return MethodReference.ForManagedBinding(
				pinnedBinding,
				implementation,
				signature);
		}
		if (FrameworkImplementationPack is not null &&
			(string.Equals(
					member.AssemblyName,
					"System.Private.CoreLib",
					StringComparison.Ordinal) ||
			 FrameworkImplementationProfile.IsPinnedTypeBoundary(member)) &&
			!FrameworkImplementationProfile.IsRequiredCoreLibOverride(member, binding))
		{
			binding = null;
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
			if (shadowTarget is
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
			IReadOnlyList<CilType>? shadowMethodTypeArguments = null;
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
					signature,
					constructedDeclaringType,
					shadowMethodTypeArguments) ??
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidInput,
					$"Managed framework binding target '{binding.Target}' could not be resolved from the supplied target runtime assemblies.");
			if (binding.PreservesVirtualDispatch)
			{
				RegisterFrameworkVirtualFallback(binding, shadowMethod);
			}
			return MethodReference.ForShadowBinding(binding, shadowMethod, signature);
		}
		var boundSignature = binding.Target is
			"intrinsic:delegate-ctor" or "intrinsic:delegate-invoke" &&
			constructedDeclaringType is { GenericArguments.Length: > 0 } delegateType
				? SubstituteTypeArguments(signature, delegateType.GenericArguments)
				: signature;
		return MethodReference.ForBinding(
			binding,
			boundSignature,
			constructedDeclaringType);
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

	private static MethodSignature<CilType> SubstituteTypeArguments(
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
			if (!string.Equals(module.GetTypeName(type), typeName, StringComparison.Ordinal))
			{
				continue;
			}

			foreach (var methodHandle in type.GetMethods())
			{
				var definition = module.Reader.GetMethodDefinition(methodHandle);
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
								0x12);
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

	private CilMethod ResolvePinnedImplementationMethod(
		FrameworkBinding binding,
		MethodSignature<CilType> publicSignature)
	{
		const string assemblyName = "System.Private.CoreLib";
		var typeName = binding.Member.DeclaringType.MetadataName ??
			throw UnsupportedPinnedBody(binding, "declaring type identity is unavailable");
		var module = GetOrLoadImplementationModule(assemblyName) ??
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidInput,
				$"Verified framework implementation assembly '{assemblyName}' could not be loaded.");
		CilMethod? match = null;
		foreach (var typeHandle in module.Reader.TypeDefinitions)
		{
			var type = module.Reader.GetTypeDefinition(typeHandle);
			if (!string.Equals(module.GetTypeName(type), typeName, StringComparison.Ordinal))
			{
				continue;
			}
			if (type.GetGenericParameters().Count != 0 ||
				(type.Attributes & TypeAttributes.LayoutMask) == TypeAttributes.ExplicitLayout)
			{
				throw UnsupportedPinnedBody(
					binding,
					$"implementation type '{typeName}' has unsupported generic or explicit layout metadata");
			}
			ValidatePinnedTypeLayout(module, typeHandle, binding);

			foreach (var methodHandle in type.GetMethods())
			{
				var definition = module.Reader.GetMethodDefinition(methodHandle);
				if (!string.Equals(
						module.Reader.GetString(definition.Name),
						binding.Member.Name,
						StringComparison.Ordinal) ||
					definition.GetGenericParameters().Count != 0)
				{
					continue;
				}
				var candidateSignature = definition.DecodeSignature(
					module._signatureProvider,
					CilGenericContext.Empty);
				if (!SignaturesMatch(candidateSignature, publicSignature))
				{
					continue;
				}
				ValidatePinnedMethodDefinition(module, definition, binding);
				if (match is not null)
				{
					throw UnsupportedPinnedBody(
						binding,
						"implementation member identity is ambiguous");
				}
				match = module.GetMethod(methodHandle);
			}
		}
		return match ?? throw UnsupportedPinnedBody(
			binding,
			"exact implementation member was not found");
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
		// GetTypeLayout uses managed-object coordinates for value-type definitions:
		// the payload follows the eight-byte object header. Transport strips that
		// header, leaving the expected eight-byte TimeSpan value.
		if (typeName == "System.TimeSpan" && layout.Size == 16 &&
			layout.ReferenceBitmap == 0 &&
			offsets.Count == 1 &&
			offsets.TryGetValue("_ticks", out var ticks) && ticks == 8)
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
		if (definition.RelativeVirtualAddress == 0 ||
			(definition.Attributes & (MethodAttributes.Abstract | MethodAttributes.PinvokeImpl)) != 0 ||
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
			"CopperSharp.Runtime.ShadowEqualityComparer`1" or
			"CopperSharp.Runtime.IShadowEqualityComparer`1" or
			"CopperSharp.Runtime.ShadowDictionary`2" or
			"CopperSharp.Runtime.ShadowPrimaryOrderedEnumerable`1" or
			"CopperSharp.Runtime.ShadowOrderedEnumerable`1" or
			"CopperSharp.Runtime.ShadowOrderedEnumerator`1";

	private CompilationModule GetCallerModule(CilMethod caller, int ilOffset)
	{
		if (_root._modules.TryGetValue(caller.ModuleName, out var module))
		{
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
			return module;
		}

		if (!_root._managedAssemblyPaths.TryGetValue(assemblyName, out var path))
		{
			return null;
		}

		return File.Exists(path)
			? new CompilationModule(path, _externalCallResolvers, _root)
			: null;
	}

	private CompilationModule? GetOrLoadImplementationModule(string assemblyName)
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
			return loaded;
		}
		return new CompilationModule(path, _externalCallResolvers, _root);
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
		var module = GetOrLoadModule(reader.GetString(assembly.Name));
		return module is not null &&
			module._signatureProvider.TryGetDefinedEnumType(
				module.Reader,
				displayName,
				out var enumType)
			? enumType
			: null;
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
			if (parentType.GenericArguments.Length != 0 &&
				TryFindConstructedGenericDefinition(parentType, out var constructedTarget))
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
			if (!string.Equals(module.GetTypeName(type), typeName, StringComparison.Ordinal))
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
		var reference = Reader.GetTypeReference(handle);
		var name = GetTypeName(reference);
		return name switch
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
			_ => _signatureProvider.GetTypeFromReference(Reader, handle, 0x12)
		};
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
			left.ReturnType.DisplayName != right.ReturnType.DisplayName)
		{
			return false;
		}

		for (var i = 0; i < left.ParameterTypes.Length; i++)
		{
			if (left.ParameterTypes[i].DisplayName != right.ParameterTypes[i].DisplayName)
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
	ImmutableArray<CilType> MethodTypeArguments = default)
{
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
	CilType? ConstructedDeclaringType = null)
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
		new(managedMethod, managedMethod.ImportName, publicSignature, binding);

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
