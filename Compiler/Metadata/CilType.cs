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
	CilType? ElementType = null)
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
			CilTypeKind.GenericParameter ||
		DisplayName == "Amiga.CString";
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
	public CilType GetArrayType(CilType elementType, ArrayShape shape) =>
		new(CilTypeKind.ManagedReference, 4, $"{elementType.DisplayName}[{new string(',', Math.Max(0, shape.Rank - 1))}]", elementType);

	public CilType GetByReferenceType(CilType elementType) =>
		new(CilTypeKind.ManagedPointer, 4, $"{elementType.DisplayName}&", elementType);

	public CilType GetFunctionPointerType(MethodSignature<CilType> signature) =>
		new(CilTypeKind.FunctionPointer, 4, "method*");

	public CilType GetGenericInstantiation(CilType genericType, ImmutableArray<CilType> typeArguments) =>
		new(
			genericType.Kind,
			genericType.Size,
			$"{genericType.DisplayName}<{string.Join(",", typeArguments.Select(static item => item.DisplayName))}>");

	public CilType GetGenericMethodParameter(CilGenericContext genericContext, int index) =>
		index >= 0 && index < genericContext.MethodArguments.Length
			? genericContext.MethodArguments[index]
			: new(CilTypeKind.GenericParameter, 4, $"!!{index}");

	public CilType GetGenericTypeParameter(CilGenericContext genericContext, int index) =>
		index >= 0 && index < genericContext.TypeArguments.Length
			? genericContext.TypeArguments[index]
			: new(CilTypeKind.GenericParameter, 4, $"!{index}");

	public CilType GetModifiedType(CilType modifier, CilType unmodifiedType, bool isRequired) =>
		unmodifiedType;

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
		var name = QualifiedName(reader, definition.Namespace, definition.Name);
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
		var name = QualifiedName(reader, reference.Namespace, reference.Name);
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

	private static string QualifiedName(
		MetadataReader reader,
		StringHandle namespaceHandle,
		StringHandle nameHandle)
	{
		var typeNamespace = reader.GetString(namespaceHandle);
		var name = reader.GetString(nameHandle);
		return string.IsNullOrEmpty(typeNamespace) ? name : $"{typeNamespace}.{name}";
	}
}
