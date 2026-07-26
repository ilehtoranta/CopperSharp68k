/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace CopperSharp.Targets.Amiga;

internal static class AmigaStaticAnalyzer
{
	private static readonly string LibraryAttributeName = typeof(AmigaLibraryAttribute).FullName!;
	private static readonly string LvoAttributeName = typeof(AmigaLvoAttribute).FullName!;
	private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue = CreateOpCodeMap();

	public static void Analyze(M68kCompilationRequest request, AmigaCompilationOptions options)
	{
		if (!MayUseAutoOpen(options))
		{
			return;
		}

		using var stream = File.OpenRead(request.AssemblyPath);
		using var peReader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
		var reader = peReader.GetMetadataReader();
		var state = new State(request, options, peReader, reader);
		var entry = ResolveEntryPoint(reader, request.EntryPoint);
		state.EnqueueTypeInitializer(reader.GetMethodDefinition(entry).GetDeclaringType());
		state.Enqueue(entry);
		state.Run();
	}

	private static bool MayUseAutoOpen(AmigaCompilationOptions options) =>
		options.DefaultLibraryBasePolicy == AmigaLibraryBasePolicy.AutoOpen ||
		options.LibraryBasePolicies.Values.Any(
			static policy => policy == AmigaLibraryBasePolicy.AutoOpen);

	private static MethodDefinitionHandle ResolveEntryPoint(
		MetadataReader reader,
		string? selector)
	{
		if (!string.IsNullOrWhiteSpace(selector))
		{
			var separator = selector.LastIndexOf("::", StringComparison.Ordinal);
			if (separator <= 0 || separator + 2 >= selector.Length)
			{
				return default;
			}

			var requestedType = selector[..separator];
			var requestedMethod = selector[(separator + 2)..];
			foreach (var typeHandle in reader.TypeDefinitions)
			{
				var type = reader.GetTypeDefinition(typeHandle);
				if (!string.Equals(GetTypeName(reader, type), requestedType, StringComparison.Ordinal))
				{
					continue;
				}

				foreach (var methodHandle in type.GetMethods())
				{
					var method = reader.GetMethodDefinition(methodHandle);
					if (string.Equals(
						reader.GetString(method.Name),
						requestedMethod,
						StringComparison.Ordinal))
					{
						return methodHandle;
					}
				}
			}
			return default;
		}

		var entryAttributeName = typeof(M68kEntryPointAttribute).FullName!;
		foreach (var handle in reader.MethodDefinitions)
		{
			var definition = reader.GetMethodDefinition(handle);
			if (HasAttribute(reader, definition.GetCustomAttributes(), entryAttributeName))
			{
				return handle;
			}
		}

		return default;
	}

	private static bool HasAttribute(
		MetadataReader reader,
		CustomAttributeHandleCollection attributes,
		string typeName) =>
		attributes.Any(handle => string.Equals(
			GetAttributeTypeName(reader, handle),
			typeName,
			StringComparison.Ordinal));

	private static LibraryDeclaration? GetLibraryDeclaration(
		MetadataReader reader,
		CustomAttributeHandleCollection attributes)
	{
		foreach (var handle in attributes)
		{
			if (!string.Equals(
				GetAttributeTypeName(reader, handle),
				LibraryAttributeName,
				StringComparison.Ordinal))
			{
				continue;
			}

			var value = reader
				.GetCustomAttribute(handle)
				.DecodeValue(new AttributeTypeProvider(reader));
			if (value.FixedArguments.Length == 0 ||
				value.FixedArguments[0].Value is not string name ||
				string.IsNullOrWhiteSpace(name))
			{
				return null;
			}

			var policy = value.FixedArguments.Length > 1 &&
				value.FixedArguments[1].Value is int policyValue &&
				Enum.IsDefined(typeof(AmigaLibraryBasePolicy), policyValue)
					? (AmigaLibraryBasePolicy?)policyValue
					: null;
			return new LibraryDeclaration(name, policy);
		}
		return null;
	}

	private static string GetAttributeTypeName(MetadataReader reader, CustomAttributeHandle handle)
	{
		var attribute = reader.GetCustomAttribute(handle);
		var constructor = attribute.Constructor;
		EntityHandle parent = constructor.Kind switch
		{
			HandleKind.MethodDefinition =>
				reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
			HandleKind.MemberReference =>
				reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
			_ => default
		};

		return parent.Kind switch
		{
			HandleKind.TypeDefinition => GetTypeName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)parent)),
			HandleKind.TypeReference => GetTypeName(reader, reader.GetTypeReference((TypeReferenceHandle)parent)),
			_ => string.Empty
		};
	}

	private static string GetTypeName(MetadataReader reader, TypeDefinition definition) =>
		QualifiedName(reader, definition.Namespace, definition.Name);

	private static string GetTypeName(MetadataReader reader, TypeReference reference) =>
		QualifiedName(reader, reference.Namespace, reference.Name);

	private static string QualifiedName(
		MetadataReader reader,
		StringHandle namespaceHandle,
		StringHandle nameHandle)
	{
		var typeNamespace = reader.GetString(namespaceHandle);
		var name = reader.GetString(nameHandle);
		return string.IsNullOrEmpty(typeNamespace) ? name : $"{typeNamespace}.{name}";
	}

	private static IReadOnlyDictionary<ushort, OpCode> CreateOpCodeMap()
	{
		var result = new Dictionary<ushort, OpCode>();
		foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (field.GetValue(null) is OpCode opCode)
			{
				result[unchecked((ushort)opCode.Value)] = opCode;
			}
		}
		return result;
	}

	private sealed class State
	{
		private readonly M68kCompilationRequest _request;
		private readonly AmigaCompilationOptions _options;
		private readonly PEReader _peReader;
		private readonly MetadataReader _reader;
		private readonly string _assemblyDirectory;
		private readonly Queue<MethodDefinitionHandle> _pending = new();
		private readonly HashSet<MethodDefinitionHandle> _visited = new();

		public State(
			M68kCompilationRequest request,
			AmigaCompilationOptions options,
			PEReader peReader,
			MetadataReader reader)
		{
			_request = request;
			_options = options;
			_peReader = peReader;
			_reader = reader;
			_assemblyDirectory = Path.GetDirectoryName(Path.GetFullPath(request.AssemblyPath))!;
		}

		public void Enqueue(MethodDefinitionHandle handle)
		{
			if (!handle.IsNil && _visited.Add(handle))
			{
				_pending.Enqueue(handle);
			}
		}

		public void EnqueueTypeInitializer(TypeDefinitionHandle typeHandle)
		{
			if (typeHandle.IsNil)
			{
				return;
			}

			var type = _reader.GetTypeDefinition(typeHandle);
			foreach (var methodHandle in type.GetMethods())
			{
				var method = _reader.GetMethodDefinition(methodHandle);
				if (_reader.GetString(method.Name) == ".cctor")
				{
					Enqueue(methodHandle);
					return;
				}
			}
		}

		public void Run()
		{
			while (_pending.TryDequeue(out var handle))
			{
				AnalyzeMethod(handle);
			}
		}

		private void AnalyzeMethod(MethodDefinitionHandle handle)
		{
			var method = _reader.GetMethodDefinition(handle);
			if (IsAutoOpenLibraryMethod(method, out var library))
			{
				ThrowAutoOpenStaticCall(handle, 0, library);
			}
			if (method.RelativeVirtualAddress == 0)
			{
				return;
			}

			var body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
			foreach (var instruction in Decode(body.GetILBytes(), DisplayName(handle)))
			{
				if (instruction.OpCode == OpCodes.Call ||
					instruction.OpCode == OpCodes.Callvirt ||
					instruction.OpCode == OpCodes.Newobj)
				{
					AnalyzeMethodReference((int)instruction.Operand!, handle, instruction.Offset);
				}
				else if (
					instruction.OpCode == OpCodes.Ldsfld ||
					instruction.OpCode == OpCodes.Ldsflda ||
					instruction.OpCode == OpCodes.Stsfld)
				{
					AnalyzeFieldReference((int)instruction.Operand!);
				}
			}
		}

		private void AnalyzeMethodReference(
			int token,
			MethodDefinitionHandle caller,
			int offset)
		{
			var handle = MetadataTokens.EntityHandle(token);
			if (handle.Kind == HandleKind.MethodDefinition)
			{
				var methodHandle = (MethodDefinitionHandle)handle;
				var method = _reader.GetMethodDefinition(methodHandle);
				if (IsAutoOpenLibraryMethod(method, out var localLibrary))
				{
					ThrowAutoOpenStaticCall(caller, offset, localLibrary);
				}

				EnqueueTypeInitializer(method.GetDeclaringType());
				Enqueue(methodHandle);
				return;
			}

			if (handle.Kind != HandleKind.MemberReference)
			{
				return;
			}

			var member = _reader.GetMemberReference((MemberReferenceHandle)handle);
			if (member.Parent.Kind == HandleKind.TypeDefinition)
			{
				EnqueueTypeInitializer((TypeDefinitionHandle)member.Parent);
				return;
			}

			if (member.Parent.Kind != HandleKind.TypeReference)
			{
				return;
			}

			var type = _reader.GetTypeReference((TypeReferenceHandle)member.Parent);
			var typeName = GetTypeName(_reader, type);
			var assemblyName = GetReferencedAssemblyName(type.ResolutionScope);
			var methodName = _reader.GetString(member.Name);
			if (IsAutoOpenLibraryReflectionMethod(assemblyName, typeName, methodName, out var library))
			{
				ThrowAutoOpenStaticCall(caller, offset, library);
			}
		}

		private void AnalyzeFieldReference(int token)
		{
			var handle = MetadataTokens.EntityHandle(token);
			if (handle.Kind == HandleKind.FieldDefinition)
			{
				var field = _reader.GetFieldDefinition((FieldDefinitionHandle)handle);
				EnqueueTypeInitializer(field.GetDeclaringType());
			}
		}

		private bool IsAutoOpenLibraryMethod(
			MethodDefinition method,
			out string library)
		{
			if (!HasAttribute(_reader, method.GetCustomAttributes(), LvoAttributeName))
			{
				library = string.Empty;
				return false;
			}

			var type = _reader.GetTypeDefinition(method.GetDeclaringType());
			var declaration =
				GetLibraryDeclaration(_reader, method.GetCustomAttributes()) ??
				GetLibraryDeclaration(_reader, type.GetCustomAttributes());
			return IsAutoOpen(declaration, out library);
		}

		private bool IsAutoOpenLibraryReflectionMethod(
			string assemblyName,
			string typeName,
			string methodName,
			out string library)
		{
			library = string.Empty;
			if (string.IsNullOrEmpty(assemblyName))
			{
				return false;
			}

			var path = Path.Combine(_assemblyDirectory, assemblyName + ".dll");
			if (!File.Exists(path))
			{
				return false;
			}

			var assembly = Assembly.LoadFrom(path);
			var type = assembly.GetType(typeName, throwOnError: false);
			if (type is null)
			{
				return false;
			}

			foreach (var method in type.GetMethods(
				BindingFlags.Public |
				BindingFlags.NonPublic |
				BindingFlags.Static |
				BindingFlags.Instance))
			{
				if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
					!method.CustomAttributes.Any(static attribute =>
						attribute.AttributeType.FullName == LibraryAttributeName ||
						attribute.AttributeType.FullName == LvoAttributeName) &&
					!type.CustomAttributes.Any(static attribute =>
						attribute.AttributeType.FullName == LibraryAttributeName))
				{
					continue;
				}

				if (!method.CustomAttributes.Any(static attribute =>
					attribute.AttributeType.FullName == LvoAttributeName))
				{
					continue;
				}

				var declaration =
					GetLibraryDeclaration(method.CustomAttributes) ??
					GetLibraryDeclaration(type.CustomAttributes);
				if (IsAutoOpen(declaration, out library))
				{
					return true;
				}
			}
			return false;
		}

		private bool IsAutoOpen(LibraryDeclaration? declaration, out string library)
		{
			if (declaration is null)
			{
				library = string.Empty;
				return false;
			}

			library = declaration.Value.Name;
			var policy = declaration.Value.Policy ??
				(_options.LibraryBasePolicies.TryGetValue(library, out var configured)
					? configured
					: _options.DefaultLibraryBasePolicy);
			return policy == AmigaLibraryBasePolicy.AutoOpen;
		}

		private string GetReferencedAssemblyName(EntityHandle scope)
		{
			if (scope.Kind != HandleKind.AssemblyReference)
			{
				return string.Empty;
			}

			var reference = _reader.GetAssemblyReference((AssemblyReferenceHandle)scope);
			return _reader.GetString(reference.Name);
		}

		private string DisplayName(MethodDefinitionHandle handle)
		{
			var method = _reader.GetMethodDefinition(handle);
			var type = _reader.GetTypeDefinition(method.GetDeclaringType());
			return $"{GetTypeName(_reader, type)}::{_reader.GetString(method.Name)}";
		}

		private void ThrowAutoOpenStaticCall(
			MethodDefinitionHandle caller,
			int offset,
			string library) =>
			throw new M68kCompilationException(
				M68kDiagnosticIds.StaticAnalysis,
				$"Auto-open library '{library}' is not available during static initialization. Move this call into code that runs after startup has entered Main, or use Manual/Provided policy for that library.",
				DisplayName(caller),
				offset);
	}

	private static LibraryDeclaration? GetLibraryDeclaration(
		IEnumerable<CustomAttributeData> attributes)
	{
		foreach (var attribute in attributes)
		{
			if (attribute.AttributeType.FullName != LibraryAttributeName ||
				attribute.ConstructorArguments.Count == 0 ||
				attribute.ConstructorArguments[0].Value is not string name ||
				string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			var policy = attribute.ConstructorArguments.Count > 1 &&
				attribute.ConstructorArguments[1].Value is int policyValue &&
				Enum.IsDefined(typeof(AmigaLibraryBasePolicy), policyValue)
					? (AmigaLibraryBasePolicy?)policyValue
					: null;
			return new LibraryDeclaration(name, policy);
		}
		return null;
	}

	private static IReadOnlyList<CilInstruction> Decode(
		ReadOnlySpan<byte> il,
		string methodName)
	{
		var result = new List<CilInstruction>();
		var offset = 0;
		while (offset < il.Length)
		{
			var instructionOffset = offset;
			ushort value = il[offset++];
			if (value == 0xFE)
			{
				EnsureAvailable(il, offset, 1, methodName, instructionOffset);
				value = (ushort)(0xFE00 | il[offset++]);
			}

			if (!OpCodesByValue.TryGetValue(value, out var opCode))
			{
				throw InvalidIl(methodName, instructionOffset, $"Unknown CIL opcode 0x{value:X4}.");
			}

			object? operand = opCode.OperandType switch
			{
				OperandType.InlineNone => null,
				OperandType.ShortInlineI => ReadByte(il, ref offset, methodName, instructionOffset),
				OperandType.InlineI => ReadInt32(il, ref offset, methodName, instructionOffset),
				OperandType.InlineI8 => ReadInt64(il, ref offset, methodName, instructionOffset),
				OperandType.ShortInlineR => ReadInt32(il, ref offset, methodName, instructionOffset),
				OperandType.InlineR => ReadInt64(il, ref offset, methodName, instructionOffset),
				OperandType.ShortInlineVar => ReadByte(il, ref offset, methodName, instructionOffset),
				OperandType.InlineVar => ReadUInt16(il, ref offset, methodName, instructionOffset),
				OperandType.ShortInlineBrTarget => ReadByte(il, ref offset, methodName, instructionOffset),
				OperandType.InlineBrTarget => ReadInt32(il, ref offset, methodName, instructionOffset),
				OperandType.InlineSwitch => ReadSwitch(il, ref offset, methodName, instructionOffset),
				OperandType.InlineField or
					OperandType.InlineMethod or
					OperandType.InlineSig or
					OperandType.InlineString or
					OperandType.InlineTok or
					OperandType.InlineType =>
					ReadInt32(il, ref offset, methodName, instructionOffset),
				_ => throw InvalidIl(methodName, instructionOffset, $"Unsupported operand encoding {opCode.OperandType}.")
			};

			result.Add(new CilInstruction(instructionOffset, opCode, operand));
		}
		return result;
	}

	private static byte ReadByte(ReadOnlySpan<byte> source, ref int offset, string method, int instructionOffset)
	{
		EnsureAvailable(source, offset, 1, method, instructionOffset);
		return source[offset++];
	}

	private static ushort ReadUInt16(ReadOnlySpan<byte> source, ref int offset, string method, int instructionOffset)
	{
		EnsureAvailable(source, offset, 2, method, instructionOffset);
		var value = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
		offset += 2;
		return value;
	}

	private static int ReadInt32(ReadOnlySpan<byte> source, ref int offset, string method, int instructionOffset)
	{
		EnsureAvailable(source, offset, 4, method, instructionOffset);
		var value = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
		offset += 4;
		return value;
	}

	private static long ReadInt64(ReadOnlySpan<byte> source, ref int offset, string method, int instructionOffset)
	{
		EnsureAvailable(source, offset, 8, method, instructionOffset);
		var value = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(source[offset..]);
		offset += 8;
		return value;
	}

	private static int[] ReadSwitch(ReadOnlySpan<byte> source, ref int offset, string method, int instructionOffset)
	{
		var count = ReadInt32(source, ref offset, method, instructionOffset);
		if (count < 0 || count > (source.Length - offset) / 4)
		{
			throw InvalidIl(method, instructionOffset, "Invalid switch target count.");
		}

		var values = new int[count];
		for (var index = 0; index < values.Length; index++)
		{
			values[index] = ReadInt32(source, ref offset, method, instructionOffset);
		}
		return values;
	}

	private static void EnsureAvailable(
		ReadOnlySpan<byte> source,
		int offset,
		int count,
		string method,
		int instructionOffset)
	{
		if (offset < 0 || count < 0 || offset > source.Length - count)
		{
			throw InvalidIl(method, instructionOffset, "CIL instruction operand extends past the method body.");
		}
	}

	private static M68kCompilationException InvalidIl(string method, int offset, string message) =>
		new(M68kDiagnosticIds.InvalidMetadata, message, method, offset);

	private readonly record struct CilInstruction(int Offset, OpCode OpCode, object? Operand);

	private readonly record struct LibraryDeclaration(
		string Name,
		AmigaLibraryBasePolicy? Policy);

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

		public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
		{
			var definition = reader.GetTypeDefinition(handle);
			return GetName(definition.Namespace, definition.Name);
		}

		public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
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
