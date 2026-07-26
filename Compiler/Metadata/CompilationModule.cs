/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace CopperSharp.Compiler.Metadata;

internal sealed class CompilationModule : IDisposable
{
	private readonly FileStream _stream;
	private readonly PEReader _peReader;
	private readonly CilSignatureTypeProvider _signatureProvider = new();
	private readonly Dictionary<MethodDefinitionHandle, CilMethod> _methodCache = new();
	private readonly Dictionary<FieldDefinitionHandle, CilField> _fieldCache = new();
	private readonly Dictionary<TypeDefinitionHandle, CilTypeLayout> _layoutCache = new();
	private readonly Dictionary<string, bool> _transparentScalarTypeCache = new(StringComparer.Ordinal);
	private readonly IReadOnlyList<IM68kExternalCallResolver> _externalCallResolvers;
	private readonly string _assemblyDirectory;
	private string _assemblyName = string.Empty;

	public CompilationModule(
		string assemblyPath,
		IReadOnlyList<IM68kExternalCallResolver>? externalCallResolvers = null)
	{
		_externalCallResolvers = externalCallResolvers ?? Array.Empty<IM68kExternalCallResolver>();
		_assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
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

	public IReadOnlyList<CilExport> GetExports()
	{
		var exports = new List<CilExport>();
		foreach (var handle in Reader.MethodDefinitions)
		{
			var definition = Reader.GetMethodDefinition(handle);
			var exportName = TryGetExportName(definition.GetCustomAttributes());
			if (exportName is null)
			{
				continue;
			}

			var method = GetMethod(handle);
			if (method.Signature.Header.IsInstance)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedSignature,
					"Exported methods must be static.",
					method.DisplayName);
			}

			var parameterRegisters = new M68kRegister[method.Signature.ParameterTypes.Length];
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
					"Every exported parameter must carry [M68kRegister].",
					method.DisplayName);
			}

			exports.Add(new CilExport(
				method,
				exportName.Length == 0 ? method.DisplayName : exportName,
				parameterRegisters,
				returnRegister ?? M68kRegister.D0));
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

		if (importName is null && externalCall is null)
		{
			if (definition.RelativeVirtualAddress == 0)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					"Reachable method has no CIL body and is not marked [M68kImport] or resolved by the target platform.",
					displayName);
			}

			body = _peReader.GetMethodBody(definition.RelativeVirtualAddress);
			if (!body.LocalSignature.IsNil)
			{
				locals = Reader
					.GetStandaloneSignature(body.LocalSignature)
					.DecodeLocalSignature(_signatureProvider, CilGenericContext.Empty);
			}

			instructions = CilInstructionDecoder.Decode(body.GetILBytes(), displayName);
		}

		var method = new CilMethod(
			handle,
			definition.GetDeclaringType(),
			displayName,
			Reader.GetString(definition.Name),
			signature,
			locals,
			instructions,
			body?.LocalVariablesInitialized ?? false,
			importName,
			importAbi,
			externalCall);
		_methodCache.Add(handle, method);
		return method;
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
		if (abi.ParameterRegisters.Contains(binding.BaseRegister) ||
			(!signature.ReturnType.IsVoid && abi.ReturnRegister == binding.BaseRegister))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				$"{binding.BaseRegister} holds the platform call base and cannot be an argument or return register.",
				displayName);
		}
		if (abi.ParameterRegisters.Count != abi.ParameterRegisters.Distinct().Count())
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				"Platform call parameter registers must be unique.",
				displayName);
		}
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

	public CilTypeLayout GetTypeLayout(TypeDefinitionHandle handle)
	{
		if (_layoutCache.TryGetValue(handle, out var cached))
		{
			return cached;
		}

		var definition = Reader.GetTypeDefinition(handle);
		var inheritedSize = 8;
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
			fieldOffsets);
		_layoutCache.Add(handle, layout);
		return layout;
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
			if (GetTypeName(definition) == displayName)
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

	public string GetUserString(int token)
	{
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

	public MethodReference ResolveMethodToken(int token, CilMethod caller, int ilOffset)
	{
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

	private static string ReflectionDisplayName(Type type) =>
		type.FullName switch
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
			var convention = ResolveExternalCall(externalMethod);
			if (convention is not null)
			{
				if (convention.ParameterRegisters is null)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedSignature,
						"Referenced external call conventions must provide their register ABI.",
						displayName);
				}
				var abi = new CilRegisterAbi(
					convention.ParameterRegisters,
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
					false,
					null,
					null,
					new CilExternalCall(convention, abi)));
			}
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"External method reference '{name}' must be represented by a local [M68kImport] declaration.",
			caller.DisplayName,
			ilOffset);
	}

	private static MethodReference? TryResolveIntrinsicReference(
		string typeName,
		string name,
		MethodSignature<CilType> signature)
	{
		if (typeName == "System.Object" && name == ".ctor" &&
			signature.ParameterTypes.Length == 0)
		{
			return MethodReference.ForIntrinsic("intrinsic:object-ctor", signature);
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
			(definition.Attributes & FieldAttributes.Static) != 0);
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

	private string GetTypeName(TypeDefinition definition) =>
		QualifiedName(definition.Namespace, definition.Name);

	private string GetTypeName(TypeReference reference) =>
		QualifiedName(reference.Namespace, reference.Name);

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
	bool InitializeLocals,
	string? ImportName,
	CilRegisterAbi? ImportAbi,
	CilExternalCall? ExternalCall)
{
	public bool IsImport => ImportName is not null || ExternalCall is not null;

	public int ParameterCount =>
		Signature.ParameterTypes.Length + (Signature.Header.IsInstance ? 1 : 0);
}

internal sealed record CilField(
	FieldDefinitionHandle Handle,
	TypeDefinitionHandle DeclaringType,
	string DisplayName,
	CilType Type,
	bool IsStatic);

internal sealed record CilTypeLayout(
	TypeDefinitionHandle Handle,
	string DisplayName,
	int Size,
	uint ReferenceBitmap,
	IReadOnlyDictionary<FieldDefinitionHandle, int> FieldOffsets);

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
