/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

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
	private readonly CilSignatureTypeProvider _signatureProvider = new();
	private readonly Dictionary<MethodDefinitionHandle, CilMethod> _methodCache = new();
	private readonly Dictionary<FieldDefinitionHandle, CilField> _fieldCache = new();
	private readonly Dictionary<TypeDefinitionHandle, CilTypeLayout> _layoutCache = new();
	private readonly Dictionary<TypeDefinitionHandle, CilVirtualTable> _virtualTableCache = new();
	private readonly Dictionary<TypeDefinitionHandle, CilInterfaceDefinition> _interfaceCache = new();
	private readonly Dictionary<CilInterfaceImplementationIdentity, CilInterfaceImplementation?> _interfaceImplementationCache = new();
	private readonly Dictionary<string, bool> _transparentScalarTypeCache = new(StringComparer.Ordinal);
	private readonly IReadOnlyList<IM68kExternalCallResolver> _externalCallResolvers;
	private readonly string _assemblyDirectory;
	private readonly CompilationModule _root;
	private readonly Dictionary<string, CompilationModule> _modules;
	private readonly IReadOnlyDictionary<string, string> _managedAssemblyPaths;
	private string _assemblyName = string.Empty;

	public CompilationModule(
		string assemblyPath,
		IReadOnlyList<IM68kExternalCallResolver>? externalCallResolvers = null,
		IReadOnlyList<string>? managedAssemblyPaths = null)
		: this(assemblyPath, externalCallResolvers, root: null)
	{
		_managedAssemblyPaths = (managedAssemblyPaths ?? Array.Empty<string>())
			.ToDictionary(
				path => Path.GetFileNameWithoutExtension(path),
				Path.GetFullPath,
				StringComparer.Ordinal);
	}

	private CompilationModule(
		string assemblyPath,
		IReadOnlyList<IM68kExternalCallResolver>? externalCallResolvers,
		CompilationModule? root)
	{
		_externalCallResolvers = externalCallResolvers ?? Array.Empty<IM68kExternalCallResolver>();
		_assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
		_root = root ?? this;
		_modules = root?._modules ?? new Dictionary<string, CompilationModule>(StringComparer.Ordinal);
		_managedAssemblyPaths = root?._managedAssemblyPaths ??
			new Dictionary<string, string>(StringComparer.Ordinal);
		try
		{
			_stream = File.OpenRead(assemblyPath);
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

	public CilMethod GetMethod(MethodDefinitionHandle handle)
	{
		if (_methodCache.TryGetValue(handle, out var cached))
		{
			return cached;
		}

		var definition = Reader.GetMethodDefinition(handle);
		var declaringType = Reader.GetTypeDefinition(definition.GetDeclaringType());
		var typeName = GetTypeName(declaringType);
		var methodName = Reader.GetString(definition.Name);
		var displayName = $"{typeName}::{methodName}";
		var signature = definition.DecodeSignature(_signatureProvider, CilGenericContext.Empty);
		var importName = TryGetImportName(definition.GetCustomAttributes());
		var externalConvention = ResolveExternalCall(new M68kExternalMethod(
			_assemblyName,
			displayName,
			typeName,
			methodName,
			!signature.Header.IsInstance,
			DecodeAttributes(declaringType.GetCustomAttributes()),
			DecodeAttributes(definition.GetCustomAttributes()),
			Array.Empty<IReadOnlyList<M68kMetadataAttribute>>(),
			Array.Empty<M68kMetadataAttribute>()));
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
						.DecodeLocalSignature(_signatureProvider, CilGenericContext.Empty);
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
			declaringType.Attributes);
		_methodCache.Add(handle, method);
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

		if (parameterRegisters.Contains(binding.BaseRegister))
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
			HandleKind.FieldDefinition => GetField((FieldDefinitionHandle)handle),
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
		var inheritedSize = IsValueTypeDefinition(definition) ? 0 : 8;
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

			if (!field.Type.IsSupportedScalar || field.Type.Size > 4)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					$"Field '{field.DisplayName}' has unsupported type '{field.Type.DisplayName}'.");
			}

			var fieldIndex = (size - 8) / 4;
			if (field.Type.IsReference && fieldIndex < 32)
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

	public CilTypeLayout GetTypeLayout(CilMethod method) =>
		GetModule(method.ModuleName).GetTypeLayout(method.DeclaringType);

	public CilTypeLayout GetTypeLayout(CilField field) =>
		GetModule(field.ModuleName).GetTypeLayout(field.DeclaringType);

	public CilTypeLayout GetTypeLayout(CilTypeLayout owner, TypeDefinitionHandle handle) =>
		GetModule(owner.ModuleName).GetTypeLayout(handle);

	public CilVirtualTable GetVirtualTable(CilTypeLayout layout) =>
		GetModule(layout.ModuleName).GetVirtualTable(layout.Handle);

	public int GetVirtualSlot(CilMethod method) =>
		GetModule(method.ModuleName).GetVirtualSlot(method.Handle);

	public IReadOnlyList<CilMethod> GetVirtualImplementations(CilMethod declaration) =>
		GetModule(declaration.ModuleName).GetVirtualImplementations(declaration.Handle);

	public CilInterfaceDefinition GetInterfaceDefinition(CilMethod method) =>
		GetModule(method.ModuleName).GetInterfaceDefinition(method.DeclaringType);

	public int GetInterfaceSlot(CilMethod method) =>
		GetModule(method.ModuleName).GetInterfaceSlot(method.Handle);

	public IReadOnlyList<CilMethod> GetInterfaceImplementations(CilMethod declaration) =>
		GetModule(declaration.ModuleName).GetInterfaceImplementations(declaration.Handle);

	public CilInterfaceImplementation? TryGetInterfaceImplementation(
		CilTypeLayout layout,
		CilInterfaceDefinition interfaceDefinition) =>
		GetModule(layout.ModuleName).TryGetInterfaceImplementation(
			layout.Handle,
			interfaceDefinition);

	public EntityHandle GetBaseType(CilTypeLayout layout) =>
		GetModule(layout.ModuleName).GetBaseType(layout.Handle);

	public string GetTypeDisplayName(EntityHandle handle, CilTypeLayout owner) =>
		GetModule(owner.ModuleName).GetTypeDisplayName(handle);

	private CompilationModule GetModule(string moduleName) =>
		string.IsNullOrEmpty(moduleName) || string.Equals(moduleName, _assemblyName, StringComparison.Ordinal)
			? this
			: _root._modules[moduleName];

	private CilVirtualTable GetVirtualTable(TypeDefinitionHandle handle)
	{
		if (_virtualTableCache.TryGetValue(handle, out var cached))
		{
			return cached;
		}

		var definition = Reader.GetTypeDefinition(handle);
		var slots = definition.BaseType.Kind == HandleKind.TypeDefinition
			? GetVirtualTable((TypeDefinitionHandle)definition.BaseType).Slots.ToBuilder()
			: ImmutableArray.CreateBuilder<CilMethod>();
		foreach (var methodHandle in definition.GetMethods())
		{
			var methodDefinition = Reader.GetMethodDefinition(methodHandle);
			if ((methodDefinition.Attributes & MethodAttributes.Virtual) == 0 ||
				(methodDefinition.Attributes & MethodAttributes.Static) != 0)
			{
				continue;
			}

			var method = GetMethod(methodHandle);
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

		var table = new CilVirtualTable(GetTypeLayout(handle), slots.ToImmutable());
		_virtualTableCache.Add(handle, table);
		return table;
	}

	private int GetVirtualSlot(MethodDefinitionHandle handle)
	{
		var method = GetMethod(handle);
		var table = GetVirtualTable(method.DeclaringType);
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

	private IReadOnlyList<CilMethod> GetVirtualImplementations(MethodDefinitionHandle handle)
	{
		var declaration = GetMethod(handle);
		var slot = GetVirtualSlot(handle);
		var implementations = new Dictionary<CilMethodIdentity, CilMethod>();
		foreach (var typeHandle in Reader.TypeDefinitions)
		{
			var type = Reader.GetTypeDefinition(typeHandle);
			if ((type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0 ||
				!IsDerivedFrom(typeHandle, declaration.DeclaringType))
			{
				continue;
			}

			var table = GetVirtualTable(typeHandle);
			if (slot >= table.Slots.Length || table.Slots[slot].IsAbstract)
			{
				continue;
			}
			implementations.TryAdd(table.Slots[slot].Identity, table.Slots[slot]);
		}
		return implementations.Values.ToArray();
	}

	private bool IsDerivedFrom(TypeDefinitionHandle type, TypeDefinitionHandle baseType)
	{
		var current = type;
		while (!current.IsNil)
		{
			if (current == baseType)
			{
				return true;
			}

			var parent = Reader.GetTypeDefinition(current).BaseType;
			if (parent.Kind != HandleKind.TypeDefinition)
			{
				return false;
			}
			current = (TypeDefinitionHandle)parent;
		}
		return false;
	}

	private CilInterfaceDefinition GetInterfaceDefinition(TypeDefinitionHandle handle)
	{
		if (_interfaceCache.TryGetValue(handle, out var cached))
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
			if (parent.Kind != HandleKind.TypeDefinition)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedPolymorphism,
					$"Interface '{GetTypeName(type)}' inherits an interface outside its managed module.");
			}

			foreach (var method in GetInterfaceDefinition((TypeDefinitionHandle)parent).Slots)
			{
				if (inherited.Add(method.Identity))
				{
					slots.Add(method);
				}
			}
		}

		foreach (var methodHandle in type.GetMethods())
		{
			var method = GetMethod(methodHandle);
			if (!method.Signature.Header.IsInstance || !inherited.Add(method.Identity))
			{
				continue;
			}
			slots.Add(method);
		}

		var definition = new CilInterfaceDefinition(
			new CilTypeIdentity(_assemblyName, handle),
			GetTypeName(type),
			slots.ToImmutable());
		_interfaceCache.Add(handle, definition);
		return definition;
	}

	private int GetInterfaceSlot(MethodDefinitionHandle handle)
	{
		var method = GetMethod(handle);
		var interfaceDefinition = GetInterfaceDefinition(method.DeclaringType);
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

	private IReadOnlyList<CilMethod> GetInterfaceImplementations(MethodDefinitionHandle handle)
	{
		var declaration = GetMethod(handle);
		var interfaceDefinition = GetInterfaceDefinition(declaration.DeclaringType);
		var slot = GetInterfaceSlot(handle);
		var methods = new Dictionary<CilMethodIdentity, CilMethod>();
		foreach (var typeHandle in Reader.TypeDefinitions)
		{
			var type = Reader.GetTypeDefinition(typeHandle);
			if ((type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) != 0)
			{
				continue;
			}

			var implementation = TryGetInterfaceImplementation(typeHandle, interfaceDefinition);
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

	private CilInterfaceImplementation? TryGetInterfaceImplementation(
		TypeDefinitionHandle typeHandle,
		CilInterfaceDefinition interfaceDefinition)
	{
		if (!string.Equals(interfaceDefinition.Identity.ModuleName, _assemblyName, StringComparison.Ordinal))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedPolymorphism,
				"Cross-module interface implementation maps are not supported yet.");
		}

		var identity = new CilInterfaceImplementationIdentity(
			new CilTypeIdentity(_assemblyName, typeHandle),
			interfaceDefinition.Identity);
		if (_interfaceImplementationCache.TryGetValue(identity, out var cached))
		{
			return cached;
		}

		if (!ImplementsInterface(typeHandle, interfaceDefinition.Identity.Handle))
		{
			_interfaceImplementationCache.Add(identity, null);
			return null;
		}

		var methods = ImmutableArray.CreateBuilder<CilMethod>(interfaceDefinition.Slots.Length);
		foreach (var declaration in interfaceDefinition.Slots)
		{
			var implementation =
				TryFindExplicitInterfaceImplementation(typeHandle, declaration) ??
				TryFindImplicitInterfaceImplementation(typeHandle, declaration) ??
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedPolymorphism,
					$"Concrete type '{GetTypeName(Reader.GetTypeDefinition(typeHandle))}' has no compiler-supported implementation for '{declaration.DisplayName}'. Default interface methods are not supported.");
			methods.Add(implementation);
		}

		var result = new CilInterfaceImplementation(
			GetTypeLayout(typeHandle),
			interfaceDefinition,
			methods.MoveToImmutable());
		_interfaceImplementationCache.Add(identity, result);
		return result;
	}

	private bool ImplementsInterface(
		TypeDefinitionHandle typeHandle,
		TypeDefinitionHandle interfaceHandle)
	{
		var current = typeHandle;
		while (!current.IsNil)
		{
			var type = Reader.GetTypeDefinition(current);
			foreach (var implementationHandle in type.GetInterfaceImplementations())
			{
				var implemented = Reader.GetInterfaceImplementation(implementationHandle).Interface;
				if (implemented.Kind == HandleKind.TypeDefinition &&
					InterfaceExtends((TypeDefinitionHandle)implemented, interfaceHandle))
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

	private bool InterfaceExtends(
		TypeDefinitionHandle interfaceHandle,
		TypeDefinitionHandle target)
	{
		if (interfaceHandle == target)
		{
			return true;
		}

		var type = Reader.GetTypeDefinition(interfaceHandle);
		foreach (var implementationHandle in type.GetInterfaceImplementations())
		{
			var parent = Reader.GetInterfaceImplementation(implementationHandle).Interface;
			if (parent.Kind == HandleKind.TypeDefinition &&
				InterfaceExtends((TypeDefinitionHandle)parent, target))
			{
				return true;
			}
		}
		return false;
	}

	private CilMethod? TryFindExplicitInterfaceImplementation(
		TypeDefinitionHandle typeHandle,
		CilMethod declaration)
	{
		var current = typeHandle;
		while (!current.IsNil)
		{
			var type = Reader.GetTypeDefinition(current);
			foreach (var implementationHandle in type.GetMethodImplementations())
			{
				var implementation = Reader.GetMethodImplementation(implementationHandle);
				var implementedDeclaration = TryResolveInterfaceMethodDeclaration(
					implementation.MethodDeclaration);
				if (implementedDeclaration?.Identity != declaration.Identity ||
					implementation.MethodBody.Kind != HandleKind.MethodDefinition)
				{
					continue;
				}
				return GetMethod((MethodDefinitionHandle)implementation.MethodBody);
			}

			if (type.BaseType.Kind != HandleKind.TypeDefinition)
			{
				break;
			}
			current = (TypeDefinitionHandle)type.BaseType;
		}
		return null;
	}

	private CilMethod? TryResolveInterfaceMethodDeclaration(EntityHandle handle)
	{
		if (handle.Kind == HandleKind.MethodDefinition)
		{
			return GetMethod((MethodDefinitionHandle)handle);
		}
		if (handle.Kind != HandleKind.MemberReference)
		{
			return null;
		}

		var member = Reader.GetMemberReference((MemberReferenceHandle)handle);
		if (member.Parent.Kind != HandleKind.TypeDefinition)
		{
			return null;
		}
		var name = Reader.GetString(member.Name);
		var signature = member.DecodeMethodSignature(_signatureProvider, CilGenericContext.Empty);
		var type = Reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent);
		foreach (var methodHandle in type.GetMethods())
		{
			var candidate = GetMethod(methodHandle);
			if (candidate.Name == name &&
				SignaturesMatch(candidate.Signature, signature))
			{
				return candidate;
			}
		}
		return null;
	}

	private CilMethod? TryFindImplicitInterfaceImplementation(
		TypeDefinitionHandle typeHandle,
		CilMethod declaration)
	{
		var current = typeHandle;
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

				var candidate = GetMethod(methodHandle);
				if (!candidate.IsAbstract &&
					candidate.Name == declaration.Name &&
					SignaturesMatch(candidate.Signature, declaration.Signature))
				{
					return candidate;
				}
			}

			if (type.BaseType.Kind != HandleKind.TypeDefinition)
			{
				break;
			}
			current = (TypeDefinitionHandle)type.BaseType;
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
				var reflectionType = Assembly.LoadFrom(path).GetType(type.DisplayName, throwOnError: false);
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

	public bool IsSupportedStructType(CilType type)
	{
		if (type.IsSupportedScalar ||
			IsTransparentScalarType(type))
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

			var fields = definition.GetFields()
				.Select(GetField)
				.Where(static field => !field.IsStatic)
				.ToArray();
			return fields.Length != 0;
		}

		return TryGetReflectionStructSlotLongs(type.DisplayName, out _);
	}

	private bool TryGetReflectionStructSlotLongs(string displayName, out int slotLongs)
	{
		slotLongs = 0;
		foreach (var path in Directory.EnumerateFiles(_assemblyDirectory, "*.dll"))
		{
			try
			{
				var type = Assembly.LoadFrom(path).GetType(displayName, throwOnError: false);
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
		foreach (var handle in Reader.TypeDefinitions)
		{
			var definition = Reader.GetTypeDefinition(handle);
			if (TypeNameMatches(definition, type.DisplayName))
			{
				return checked((GetTypeLayout(handle).Size + 3) / 4);
			}
		}

		if (TryGetReflectionStructSlotLongs(type.DisplayName, out var slotLongs))
		{
			return slotLongs;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedSignature,
			$"Unsupported value type '{type.DisplayName}'.");
	}

	public bool IsTransparentScalarConstructor(CilMethod method) =>
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
				var type = Assembly.LoadFrom(path).GetType(displayName, throwOnError: false);
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
				0x12),
			HandleKind.TypeReference => ResolveReferencedType((TypeReferenceHandle)handle),
			HandleKind.TypeSpecification => _signatureProvider.GetTypeFromSpecification(
				Reader,
				CilGenericContext.Empty,
				(TypeSpecificationHandle)handle,
				0x12),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Token 0x{token:X8} is not a type reference.",
				caller.DisplayName,
				ilOffset)
		};
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
				var type = Assembly.LoadFrom(path).GetType(displayName, throwOnError: false);
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

	private MethodReference ResolveMethodDefinition(MethodDefinitionHandle handle)
	{
		var method = GetMethod(handle);
		var declaringType = Reader.GetTypeDefinition(
			Reader.GetMethodDefinition(handle).GetDeclaringType());
		return TryResolveIntrinsicReference(
				GetTypeName(declaringType),
				method.Name,
				method.Signature) ??
			MethodReference.ForDefinition(method);
	}

	private MethodReference ResolveMethodSpecification(
		MethodSpecificationHandle handle,
		CilMethod caller,
		int ilOffset)
	{
		var specification = Reader.GetMethodSpecification(handle);
		var arguments = specification.DecodeSignature(_signatureProvider, CilGenericContext.Empty);
		foreach (var argument in arguments)
		{
			if (argument.IsFloatingPoint || !argument.IsSupportedScalar || argument.Size > 4)
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
		var method = GetMethod(methodHandle);
		var instantiatedSignature = Reader
			.GetMethodDefinition(methodHandle)
			.DecodeSignature(
				_signatureProvider,
				new CilGenericContext(ImmutableArray<CilType>.Empty, arguments));
		return new MethodReference(method, method.ImportName, instantiatedSignature);
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
		var candidates = type?
			.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
				BindingFlags.Static | BindingFlags.Instance)
			.Where(method =>
				method.Name == methodName &&
				method.IsStatic == isStatic &&
				ParametersMatch(method, signature))
			.ToArray() ?? Array.Empty<MethodInfo>();
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
			DecodeReflectionAttributes(declaration.ReturnParameter.CustomAttributes));
	}

	private static bool ParametersMatch(MethodInfo method, MethodSignature<CilType> signature)
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
		var signature = member.DecodeMethodSignature(_signatureProvider, CilGenericContext.Empty);

		if (member.Parent.Kind == HandleKind.TypeDefinition)
		{
			var type = Reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent);
			var typeName = GetTypeName(type);
			if (TryResolveIntrinsicReference(typeName, name, signature) is { } intrinsic)
			{
				return intrinsic;
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
			if (TryResolveIntrinsicReference(typeName, name, signature) is { } intrinsic)
			{
				return intrinsic;
			}

			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name.StartsWith("Dispose", StringComparison.Ordinal) &&
				signature.ParameterTypes.Length == 1)
			{
				return MethodReference.ForIntrinsic("intrinsic:runtime-dispose", signature);
			}

			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "Collect" &&
				signature.ParameterTypes.Length == 0)
			{
				return MethodReference.ForIntrinsic("intrinsic:runtime-gc-collect", signature);
			}

			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name is "GetGcStaleBytes" or "GetGcStaleBlocks" &&
				signature.ParameterTypes.Length == 0)
			{
				return MethodReference.ForIntrinsic($"intrinsic:runtime-{name}", signature);
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

			if (TryResolveManagedMethod(assemblyName, typeName, name, signature) is { } managedMethod)
			{
				return MethodReference.ForDefinition(managedMethod);
			}
		}

		if (member.Parent.Kind == HandleKind.TypeSpecification)
		{
			var parentType = Reader
				.GetTypeSpecification((TypeSpecificationHandle)member.Parent)
				.DecodeSignature(_signatureProvider, CilGenericContext.Empty);
			if (TryResolveNullableIntrinsicReference(parentType, name, signature) is { } nullableIntrinsic)
			{
				return nullableIntrinsic;
			}
		}

		if (name == "FromAddress" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].DisplayName == "Amiga.APTR" &&
			signature.ReturnType.IsReference)
		{
			return MethodReference.ForIntrinsic("intrinsic:address-to-ref", signature);
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"External method reference '{name}' must be represented by a local [M68kImport] declaration.",
			caller.DisplayName,
			ilOffset);
	}

	private CilMethod? TryResolveManagedMethod(
		string assemblyName,
		string typeName,
		string methodName,
		MethodSignature<CilType> signature)
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

			foreach (var methodHandle in type.GetMethods())
			{
				var candidate = module.GetMethod(methodHandle);
				if (candidate.Name == methodName &&
					SignaturesMatch(candidate.Signature, signature))
				{
					return candidate;
				}
			}
		}

		return null;
	}

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

	private static MethodReference? TryResolveIntrinsicReference(
		string typeName,
		string name,
		MethodSignature<CilType> signature)
	{
		if (name == "FromAddress" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].DisplayName == "Amiga.APTR" &&
			signature.ReturnType.IsReference)
		{
			return MethodReference.ForIntrinsic("intrinsic:address-to-ref", signature);
		}

		if ((typeName == "System.Object" || typeName == "System.Exception") &&
			name == ".ctor" &&
			signature.ParameterTypes.Length == 0)
		{
			return MethodReference.ForIntrinsic("intrinsic:object-ctor", signature);
		}

		if (typeName == "System.Runtime.CompilerServices.RuntimeHelpers" &&
			name == "InitializeArray" &&
			signature.ParameterTypes.Length == 2)
		{
			return MethodReference.ForIntrinsic("intrinsic:initialize-array", signature);
		}

		if (typeName == "System.String" && name == "get_Length" &&
			signature.ParameterTypes.Length == 0)
		{
			return MethodReference.ForIntrinsic("intrinsic:string-length", signature);
		}

		if (typeName == "Amiga.CString")
		{
			if ((name == "FromLiteral" || name == "op_Implicit") &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "string")
			{
				return MethodReference.ForIntrinsic("intrinsic:cstring-from-literal", signature);
			}

			if ((name == "FromPointer" || name == "op_Implicit") &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "uint")
			{
				return MethodReference.ForIntrinsic("intrinsic:cstring-from-pointer", signature);
			}

			if ((name == "ToUInt32" || name == "op_Implicit") &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "Amiga.CString")
			{
				return MethodReference.ForIntrinsic("intrinsic:cstring-to-uint32", signature);
			}
		}

		if (typeName == "Amiga.FileInfoBlock" &&
			name is "FileName" or "Comment" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].DisplayName == "uint")
		{
			return MethodReference.ForIntrinsic("intrinsic:file-info-block-file-name", signature);
		}

		if (typeName is
			"Amiga.APTR" or
			"Amiga.BPTR" or
			"Amiga.STRPTR" or
			"Amiga.CONST_STRPTR" or
			"Amiga.IFFHandle" or
			"CopperSharp.Compiler.M68kAddress")
		{
			if (name == "get_Null" &&
				signature.ParameterTypes.Length == 0)
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-null", signature);
			}

			if ((name is "FromPointer" or "FromRaw" or "FromUInt32" or "op_Implicit") &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "uint")
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-from-pointer", signature);
			}

			if (typeName == "Amiga.APTR" &&
				name == "ExportAddress" &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "string")
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-export-address", signature);
			}

			if (typeName is "Amiga.APTR" or "CopperSharp.Compiler.M68kAddress" &&
				name == "ReadUInt32" &&
				signature.ParameterTypes.Length == 2 &&
				signature.ParameterTypes[0].DisplayName == typeName &&
				signature.ParameterTypes[1].DisplayName == "int")
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-read-uint32", signature);
			}

			if (typeName is "Amiga.APTR" or "CopperSharp.Compiler.M68kAddress" &&
				name == "WriteUInt32" &&
				signature.ParameterTypes.Length == 3 &&
				signature.ParameterTypes[0].DisplayName == typeName &&
				signature.ParameterTypes[1].DisplayName == "int" &&
				signature.ParameterTypes[2].DisplayName == "uint")
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-write-uint32", signature);
			}

			if ((name == "ToUInt32" || name == "op_Implicit") &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == typeName)
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-to-uint32", signature);
			}

			if (name == "get_Raw" &&
				signature.ParameterTypes.Length == 0)
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-raw", signature);
			}

			if (name == "get_IsNull" &&
				signature.ParameterTypes.Length == 0)
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-is-null", signature);
			}

			if (name == "get_IsNotNull" &&
				signature.ParameterTypes.Length == 0)
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-is-not-null", signature);
			}


			if ((typeName is "Amiga.STRPTR" or "Amiga.CONST_STRPTR") &&
				(name == "get_Address" || name == "ToAddress") &&
				signature.ParameterTypes.Length <= 1)
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-to-uint32", signature);
			}

			if ((typeName is "Amiga.STRPTR" or "Amiga.CONST_STRPTR") &&
				name == "FromAddress" &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "Amiga.APTR")
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-from-pointer", signature);
			}

			if (typeName == "Amiga.CONST_STRPTR" &&
				name == "op_Implicit" &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "Amiga.STRPTR")
			{
				return MethodReference.ForIntrinsic("intrinsic:aptr-from-pointer", signature);
			}

			if (typeName == "Amiga.BPTR" &&
				(name == "get_Address" || name == "ToAddress") &&
				signature.ParameterTypes.Length <= 1)
			{
				return MethodReference.ForIntrinsic("intrinsic:bptr-address", signature);
			}

			if (typeName == "Amiga.BPTR" &&
				name == "FromAddress" &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "Amiga.APTR")
			{
				return MethodReference.ForIntrinsic("intrinsic:bptr-from-address", signature);
			}

			if (typeName == "Amiga.IFFHandle" &&
				name == "get_Stream" &&
				signature.ParameterTypes.Length == 0)
			{
				return MethodReference.ForIntrinsic("intrinsic:iff-handle-stream", signature);
			}

			if (typeName == "Amiga.IFFHandle" &&
				name == "SetStream" &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "Amiga.BPTR")
			{
				return MethodReference.ForIntrinsic("intrinsic:iff-handle-set-stream", signature);
			}
		}

		if (typeName == "Amiga.AmigaVarArg" &&
			name == "op_Implicit" &&
			signature.ParameterTypes.Length == 1)
		{
			if (signature.ParameterTypes[0].DisplayName == "string")
			{
				return MethodReference.ForIntrinsic("intrinsic:amiga-vararg-from-literal", signature);
			}

			if (signature.ParameterTypes[0].Size == 4 ||
				signature.ParameterTypes[0].Kind == CilTypeKind.ValueType)
			{
				return MethodReference.ForIntrinsic("intrinsic:amiga-vararg-from-value", signature);
			}
		}

		if (typeName == "Amiga.Hook" &&
			name == "AddressOf" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].DisplayName == "Amiga.Hook&")
		{
			return MethodReference.ForIntrinsic("intrinsic:hook-address-of", signature);
		}

		if (name == "AddressOf" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].Kind == CilTypeKind.ManagedPointer &&
			signature.ReturnType.DisplayName == "Amiga.APTR")
		{
			return MethodReference.ForIntrinsic("intrinsic:address-of-ref", signature);
		}

		if (name == "Cast" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].Kind == CilTypeKind.ManagedPointer &&
			signature.ReturnType.Kind == CilTypeKind.ManagedPointer)
		{
			return MethodReference.ForIntrinsic("intrinsic:ref-cast", signature);
		}

		if ((typeName == "Amiga.BOOPSI+Message" || typeName == "Message") &&
			name == "AddressOf" &&
			signature.ParameterTypes.Length == 1 &&
			(signature.ParameterTypes[0].DisplayName == "Amiga.BOOPSI+Message&" ||
			 signature.ParameterTypes[0].DisplayName == "Message&"))
		{
			return MethodReference.ForIntrinsic("intrinsic:boopsi-message-address-of", signature);
		}

		if (typeName == "Amiga.BOOPSI" &&
			name == "InstanceData" &&
			signature.ParameterTypes.Length == 2 &&
			signature.ParameterTypes[0].DisplayName == "Amiga.APTR" &&
			signature.ParameterTypes[1].DisplayName == "Amiga.APTR" &&
			signature.ReturnType.DisplayName == "Amiga.APTR")
		{
			return MethodReference.ForIntrinsic("intrinsic:boopsi-instance-data", signature);
		}

		if (typeName == "Amiga.BOOPSI" &&
			name == "DoMethod" &&
			signature.ParameterTypes.Length == 2 &&
			signature.ParameterTypes[1].DisplayName == "uint[]" &&
			signature.ParameterTypes[1].ElementType?.DisplayName == "uint")
		{
			return MethodReference.ForIntrinsic("intrinsic:boopsi-do-method-stack-varargs", signature);
		}

		if (typeName == "Amiga.BOOPSI" &&
			name == "DoMethod" &&
			signature.ParameterTypes.Length is >= 2 and <= 8)
		{
			return MethodReference.ForIntrinsic("intrinsic:boopsi-do-method", signature);
		}

		if (TryGetAmigaLibraryBaseIntrinsic(typeName, name, signature) is { } amigaLibraryBase)
		{
			return MethodReference.ForIntrinsic(amigaLibraryBase, signature);
		}

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

	private MethodReference? TryResolveNullableIntrinsicReference(
		CilType nullableType,
		string name,
		MethodSignature<CilType> signature)
	{
		if (!IsSupportedNullableType(nullableType) ||
			nullableType.NullableElementType is not { } element)
		{
			return null;
		}

		if (name == ".ctor" &&
			signature.ParameterTypes.Length == 1)
		{
			return MethodReference.ForIntrinsic(
				$"intrinsic:nullable-ctor:{element.DisplayName}",
				signature);
		}

		if (name == "get_HasValue" &&
			signature.ParameterTypes.Length == 0)
		{
			return MethodReference.ForIntrinsic(
				$"intrinsic:nullable-has-value:{element.DisplayName}",
				signature);
		}

		if (name is "get_Value" or "GetValueOrDefault" &&
			signature.ParameterTypes.Length == 0)
		{
			return MethodReference.ForIntrinsic(
				$"intrinsic:nullable-get-value:{element.DisplayName}",
				signature);
		}

		if (name == "GetValueOrDefault" &&
			signature.ParameterTypes.Length == 1)
		{
			return MethodReference.ForIntrinsic(
				$"intrinsic:nullable-get-value-or-default:{element.DisplayName}",
				signature);
		}

		return null;
	}

	private static string? TryGetAmigaLibraryBaseIntrinsic(
		string typeName,
		string name,
		MethodSignature<CilType> signature)
	{
		const string prefix = "Amiga.";
		if (!typeName.StartsWith(prefix, StringComparison.Ordinal) ||
			typeName == "Amiga.Exec")
		{
			return null;
		}

		var libraryTypeName = typeName[prefix.Length..];
		var propertyName = $"{libraryTypeName}LibraryBase";
		if (name == $"set_{propertyName}" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].DisplayName is "uint" or "Amiga.APTR")
		{
			return $"intrinsic:amiga-library-base-set:{libraryTypeName}";
		}

		if (name == $"get_{propertyName}" &&
			signature.ParameterTypes.Length == 0)
		{
			return $"intrinsic:amiga-library-base-get:{libraryTypeName}";
		}

		return null;
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

	private CilField ResolveFieldMemberReference(
		MemberReferenceHandle handle,
		CilMethod caller,
		int ilOffset)
	{
		var member = Reader.GetMemberReference(handle);
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
	TypeAttributes DeclaringTypeAttributes = 0)
{
	public CilMethodIdentity Identity => new(ModuleName, Handle);

	public bool IsImport => ImportName is not null || ExternalCall is not null;

	public bool IsAbstract => (Attributes & MethodAttributes.Abstract) != 0;

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
	string ModuleName = "")
{
	public CilFieldIdentity Identity => new(ModuleName, Handle);
}

internal sealed record CilTypeLayout(
	TypeDefinitionHandle Handle,
	string DisplayName,
	int Size,
	uint ReferenceBitmap,
	IReadOnlyDictionary<FieldDefinitionHandle, int> FieldOffsets,
	string ModuleName = "")
{
	public CilTypeIdentity Identity => new(ModuleName, Handle);
}

internal sealed record CilVirtualTable(
	CilTypeLayout Type,
	ImmutableArray<CilMethod> Slots);

internal sealed record CilInterfaceDefinition(
	CilTypeIdentity Identity,
	string DisplayName,
	ImmutableArray<CilMethod> Slots);

internal sealed record CilInterfaceImplementation(
	CilTypeLayout Type,
	CilInterfaceDefinition Interface,
	ImmutableArray<CilMethod> Methods);

internal readonly record struct CilInterfaceImplementationIdentity(
	CilTypeIdentity Type,
	CilTypeIdentity Interface);

internal readonly record struct CilMethodIdentity(
	string ModuleName,
	MethodDefinitionHandle Handle);

internal readonly record struct CilFieldIdentity(
	string ModuleName,
	FieldDefinitionHandle Handle);

internal readonly record struct CilTypeIdentity(
	string ModuleName,
	TypeDefinitionHandle Handle);

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

internal sealed record MethodReference(
	CilMethod? Definition,
	string? ImportName,
	MethodSignature<CilType> Signature)
{
	public static MethodReference ForDefinition(CilMethod method) =>
		new(method, method.ImportName, method.Signature);

	public static MethodReference ForIntrinsic(
		string name,
		MethodSignature<CilType> signature) =>
		new(null, name, signature);

	public int ParameterCount =>
		Signature.ParameterTypes.Length + (Signature.Header.IsInstance ? 1 : 0);
}
