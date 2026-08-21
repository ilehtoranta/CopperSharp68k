/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace CopperSharp.Compiler.Metadata;

internal enum CilTypeKind
{
	Void,
	Boolean,
	Character,
	SignedInteger,
	UnsignedInteger,
	NativeInteger,
	FloatingPoint,
	ManagedReference,
	ManagedPointer,
	UnmanagedPointer,
	ValueType,
	GenericParameter,
	FunctionPointer,
	Unknown
}

internal sealed record CilType(
	CilTypeKind Kind,
	int Size,
	string DisplayName,
	CilType? ElementType = null,
	ImmutableArray<CilType> GenericArguments = default,
	bool IsReadOnly = false,
	bool IsEnum = false)
{
	public bool IsVoid => Kind == CilTypeKind.Void;

	public bool IsFloatingPoint => Kind == CilTypeKind.FloatingPoint;

	public bool IsReference =>
		Kind is CilTypeKind.ManagedReference or CilTypeKind.ManagedPointer;

	public bool IsSupportedScalar =>
		Kind is CilTypeKind.Boolean or
			CilTypeKind.Character or
			CilTypeKind.SignedInteger or
			CilTypeKind.UnsignedInteger or
			CilTypeKind.NativeInteger or
			CilTypeKind.ManagedReference or
			CilTypeKind.ManagedPointer or
			CilTypeKind.UnmanagedPointer or
			CilTypeKind.GenericParameter or
			CilTypeKind.FloatingPoint ||
		DisplayName == "Amiga.CString";

	public bool IsNullable =>
		Kind == CilTypeKind.ValueType &&
		DisplayName.StartsWith("System.Nullable<", StringComparison.Ordinal) &&
		GenericArguments.Length == 1;

	public CilType? NullableElementType =>
		IsNullable ? GenericArguments[0] : null;
}

internal readonly record struct CilGenericContext(
	ImmutableArray<CilType> TypeArguments,
	ImmutableArray<CilType> MethodArguments)
{
	public static CilGenericContext Empty { get; } =
		new(ImmutableArray<CilType>.Empty, ImmutableArray<CilType>.Empty);
}

internal sealed class CilSignatureTypeProvider :
	ISignatureTypeProvider<CilType, CilGenericContext>
{
	private readonly Func<MetadataReader, TypeReference, string, CilType?>?
		_referencedEnumResolver;
	private Dictionary<string, CilType>? _definedEnumTypes;

	public CilSignatureTypeProvider(
		Func<MetadataReader, TypeReference, string, CilType?>? referencedEnumResolver = null) =>
		_referencedEnumResolver = referencedEnumResolver;

	public CilType GetArrayType(CilType elementType, ArrayShape shape) =>
		new(CilTypeKind.ManagedReference, 4, $"{elementType.DisplayName}[{new string(',', Math.Max(0, shape.Rank - 1))}]", elementType);

	public CilType GetByReferenceType(CilType elementType) =>
		new(
			CilTypeKind.ManagedPointer,
			4,
			$"{elementType.DisplayName}&",
			elementType,
			IsReadOnly: elementType.IsReadOnly);

	public CilType GetFunctionPointerType(MethodSignature<CilType> signature) =>
		new(CilTypeKind.FunctionPointer, 4, "method*");

	public CilType GetGenericInstantiation(CilType genericType, ImmutableArray<CilType> typeArguments) =>
		genericType.DisplayName == "System.Nullable`1" && typeArguments.Length == 1
			? new(
				CilTypeKind.ValueType,
				8,
				$"System.Nullable<{typeArguments[0].DisplayName}>",
				GenericArguments: typeArguments)
			: new(
				genericType.Kind,
				genericType.Size,
				$"{genericType.DisplayName}<{string.Join(",", typeArguments.Select(static item => item.DisplayName))}>",
				GenericArguments: typeArguments);

	public CilType GetGenericMethodParameter(CilGenericContext genericContext, int index) =>
		index >= 0 && index < genericContext.MethodArguments.Length
			? genericContext.MethodArguments[index]
			: new(CilTypeKind.GenericParameter, 4, $"!!{index}");

	public CilType GetGenericTypeParameter(CilGenericContext genericContext, int index) =>
		index >= 0 && index < genericContext.TypeArguments.Length
			? genericContext.TypeArguments[index]
			: new(CilTypeKind.GenericParameter, 4, $"!{index}");

	public CilType GetModifiedType(CilType modifier, CilType unmodifiedType, bool isRequired) =>
		isRequired && modifier.DisplayName ==
			"System.Runtime.CompilerServices.IsReadOnlyAttribute"
				? unmodifiedType with { IsReadOnly = true }
				: unmodifiedType;

	public CilType GetPinnedType(CilType elementType) => elementType;

	public CilType GetPointerType(CilType elementType) =>
		new(CilTypeKind.UnmanagedPointer, 4, $"{elementType.DisplayName}*", elementType);

	public CilType GetPrimitiveType(PrimitiveTypeCode typeCode) =>
		typeCode switch
		{
			PrimitiveTypeCode.Void => new(CilTypeKind.Void, 0, "void"),
			PrimitiveTypeCode.Boolean => new(CilTypeKind.Boolean, 1, "bool"),
			PrimitiveTypeCode.Char => new(CilTypeKind.Character, 2, "char"),
			PrimitiveTypeCode.SByte => new(CilTypeKind.SignedInteger, 1, "sbyte"),
			PrimitiveTypeCode.Byte => new(CilTypeKind.UnsignedInteger, 1, "byte"),
			PrimitiveTypeCode.Int16 => new(CilTypeKind.SignedInteger, 2, "short"),
			PrimitiveTypeCode.UInt16 => new(CilTypeKind.UnsignedInteger, 2, "ushort"),
			PrimitiveTypeCode.Int32 => new(CilTypeKind.SignedInteger, 4, "int"),
			PrimitiveTypeCode.UInt32 => new(CilTypeKind.UnsignedInteger, 4, "uint"),
			PrimitiveTypeCode.Int64 => new(CilTypeKind.SignedInteger, 8, "long"),
			PrimitiveTypeCode.UInt64 => new(CilTypeKind.UnsignedInteger, 8, "ulong"),
			PrimitiveTypeCode.IntPtr => new(CilTypeKind.NativeInteger, 4, "nint"),
			PrimitiveTypeCode.UIntPtr => new(CilTypeKind.NativeInteger, 4, "nuint"),
			PrimitiveTypeCode.Single => new(CilTypeKind.FloatingPoint, 4, "float"),
			PrimitiveTypeCode.Double => new(CilTypeKind.FloatingPoint, 8, "double"),
			PrimitiveTypeCode.Object => new(CilTypeKind.ManagedReference, 4, "object"),
			PrimitiveTypeCode.String => new(CilTypeKind.ManagedReference, 4, "string"),
			PrimitiveTypeCode.TypedReference => new(CilTypeKind.Unknown, 8, "typedref"),
			_ => new(CilTypeKind.Unknown, 0, typeCode.ToString())
		};

	public CilType GetSZArrayType(CilType elementType) =>
		new(CilTypeKind.ManagedReference, 4, $"{elementType.DisplayName}[]", elementType);

	public CilType GetTypeFromDefinition(
		MetadataReader reader,
		TypeDefinitionHandle handle,
		byte rawTypeKind)
	{
		var definition = reader.GetTypeDefinition(handle);
		var name = QualifiedName(reader, handle, definition);
		if (TryGetEnumUnderlyingType(reader, definition, out var underlying))
		{
			return underlying with
			{
				DisplayName = name,
				IsEnum = true
			};
		}
		return rawTypeKind == 0x11
			? new(CilTypeKind.ValueType, name == "Amiga.CString" ? 4 : 0, name)
			: new(CilTypeKind.ManagedReference, 4, name);
	}

	public CilType GetTypeFromReference(
		MetadataReader reader,
		TypeReferenceHandle handle,
		byte rawTypeKind)
	{
		var reference = reader.GetTypeReference(handle);
		var name = QualifiedName(reader, reference);
		if (TryGetDefinedEnumType(reader, name, out var definedEnum))
		{
			return definedEnum;
		}
		if (TryGetKnownFrameworkEnumType(name, out var frameworkEnum))
		{
			return frameworkEnum;
		}
		if (_referencedEnumResolver?.Invoke(reader, reference, name) is { } referencedEnum)
		{
			return referencedEnum;
		}
		return rawTypeKind == 0x11
			? new(CilTypeKind.ValueType, name == "Amiga.CString" ? 4 : 0, name)
			: new(CilTypeKind.ManagedReference, 4, name);
	}

	public CilType GetTypeFromSpecification(
		MetadataReader reader,
		CilGenericContext genericContext,
		TypeSpecificationHandle handle,
		byte rawTypeKind) =>
		reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

	public bool TryGetDefinedEnumType(
		MetadataReader reader,
		string displayName,
		out CilType enumType)
	{
		if (_definedEnumTypes is null)
		{
			var definedEnumTypes = new Dictionary<string, CilType>(StringComparer.Ordinal);
			foreach (var definitionHandle in reader.TypeDefinitions)
			{
				var definition = reader.GetTypeDefinition(definitionHandle);
				if (!TryGetEnumUnderlyingType(reader, definition, out var underlying))
				{
					continue;
				}

				var name = QualifiedName(reader, definitionHandle, definition);
				definedEnumTypes.TryAdd(
					name,
					underlying with
					{
						DisplayName = name,
						IsEnum = true
					});
			}
			_definedEnumTypes = definedEnumTypes;
		}

		return _definedEnumTypes.TryGetValue(displayName, out enumType!);
	}

	private static bool TryGetKnownFrameworkEnumType(
		string displayName,
		out CilType enumType)
	{
		if (displayName is
			"System.StringComparison" or
			"System.IO.FileAttributes" or
			"System.MidpointRounding")
		{
			enumType = new CilType(
				CilTypeKind.SignedInteger,
				4,
				displayName,
				IsEnum: true);
			return true;
		}

		enumType = null!;
		return false;
	}

	private bool TryGetEnumUnderlyingType(
		MetadataReader reader,
		TypeDefinition definition,
		out CilType underlying)
	{
		underlying = null!;
		if (definition.BaseType.IsNil)
		{
			return false;
		}
		// Application enums reference System.Enum from CoreLib. CoreLib's own
		// internal enums instead point to a definition in the same metadata module.
		var baseTypeName = definition.BaseType.Kind switch
		{
			HandleKind.TypeReference => QualifiedName(
				reader,
				reader.GetTypeReference((TypeReferenceHandle)definition.BaseType)),
			HandleKind.TypeDefinition => QualifiedName(
				reader,
				(TypeDefinitionHandle)definition.BaseType,
				reader.GetTypeDefinition((TypeDefinitionHandle)definition.BaseType)),
			_ => null
		};
		if (!string.Equals(baseTypeName, "System.Enum", StringComparison.Ordinal))
		{
			return false;
		}

		foreach (var fieldHandle in definition.GetFields())
		{
			var field = reader.GetFieldDefinition(fieldHandle);
			if ((field.Attributes & System.Reflection.FieldAttributes.Static) != 0 ||
				reader.GetString(field.Name) != "value__")
			{
				continue;
			}

			var candidate = field.DecodeSignature(this, CilGenericContext.Empty);
			if (candidate.Kind is
				CilTypeKind.SignedInteger or
				CilTypeKind.UnsignedInteger)
			{
				underlying = candidate;
				return true;
			}
		}
		return false;
	}

	private static string QualifiedName(
		MetadataReader reader,
		TypeDefinitionHandle handle,
		TypeDefinition definition)
	{
		var name = reader.GetString(definition.Name);
		var declaringType = definition.GetDeclaringType();
		if (!declaringType.IsNil)
		{
			return $"{QualifiedName(reader, declaringType, reader.GetTypeDefinition(declaringType))}/{name}";
		}

		var typeNamespace = reader.GetString(definition.Namespace);
		return string.IsNullOrEmpty(typeNamespace) ? name : $"{typeNamespace}.{name}";
	}

	private static string QualifiedName(
		MetadataReader reader,
		TypeReference reference)
	{
		var name = reader.GetString(reference.Name);
		if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
		{
			return $"{QualifiedName(
				reader,
				reader.GetTypeReference((TypeReferenceHandle)reference.ResolutionScope))}/{name}";
		}

		var typeNamespace = reader.GetString(reference.Namespace);
		return string.IsNullOrEmpty(typeNamespace) ? name : $"{typeNamespace}.{name}";
	}
}
