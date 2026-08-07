/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace CopperSharp.Compiler.Framework;

internal enum FrameworkTypeKind
{
	Primitive,
	Named,
	GenericInstantiation,
	SzArray,
	Array,
	ByReference,
	Pointer,
	GenericTypeParameter,
	GenericMethodParameter,
	FunctionPointer,
	Modified
}

internal readonly record struct FrameworkGenericContext(
	ImmutableArray<FrameworkTypeId> TypeArguments,
	ImmutableArray<FrameworkTypeId> MethodArguments)
{
	public static FrameworkGenericContext Empty { get; } = new([], []);
}

/// <summary>
/// Structural metadata type identity. <see cref="DisplayName"/> is diagnostic
/// output only and is never used by equality or binding lookup.
/// </summary>
internal sealed class FrameworkTypeId : IEquatable<FrameworkTypeId>
{
	private FrameworkTypeId(
		FrameworkTypeKind kind,
		string? assemblyName = null,
		string? metadataName = null,
		FrameworkTypeId? declaringType = null,
		FrameworkTypeId? elementType = null,
		IReadOnlyList<FrameworkTypeId>? genericArguments = null,
		int genericParameterIndex = -1,
		FrameworkArrayShape? arrayShape = null,
		FrameworkMethodSignatureId? functionPointerSignature = null,
		FrameworkTypeId? modifier = null,
		bool isRequiredModifier = false)
	{
		Kind = kind;
		AssemblyName = assemblyName;
		MetadataName = metadataName;
		DeclaringType = declaringType;
		ElementType = elementType;
		GenericArguments = genericArguments?.ToImmutableArray() ?? [];
		GenericParameterIndex = genericParameterIndex;
		ArrayShape = arrayShape;
		FunctionPointerSignature = functionPointerSignature;
		Modifier = modifier;
		IsRequiredModifier = isRequiredModifier;
	}

	public FrameworkTypeKind Kind { get; }

	public string? AssemblyName { get; }

	public string? MetadataName { get; }

	public FrameworkTypeId? DeclaringType { get; }

	public FrameworkTypeId? ElementType { get; }

	public ImmutableArray<FrameworkTypeId> GenericArguments { get; }

	public int GenericParameterIndex { get; }

	public FrameworkArrayShape? ArrayShape { get; }

	public FrameworkMethodSignatureId? FunctionPointerSignature { get; }

	public FrameworkTypeId? Modifier { get; }

	public bool IsRequiredModifier { get; }

	public string DisplayName => Kind switch
	{
		FrameworkTypeKind.Primitive => MetadataName!,
		FrameworkTypeKind.Named => DeclaringType is null
			? $"[{AssemblyName}]{MetadataName}"
			: $"{DeclaringType.DisplayName}+{MetadataName}",
		FrameworkTypeKind.GenericInstantiation =>
			$"{ElementType!.DisplayName}<{string.Join(",", GenericArguments.Select(static item => item.DisplayName))}>",
		FrameworkTypeKind.SzArray => $"{ElementType!.DisplayName}[]",
		FrameworkTypeKind.Array =>
			$"{ElementType!.DisplayName}[{new string(',', Math.Max(0, ArrayShape!.Rank - 1))}]",
		FrameworkTypeKind.ByReference => $"{ElementType!.DisplayName}&",
		FrameworkTypeKind.Pointer => $"{ElementType!.DisplayName}*",
		FrameworkTypeKind.GenericTypeParameter => $"!{GenericParameterIndex}",
		FrameworkTypeKind.GenericMethodParameter => $"!!{GenericParameterIndex}",
		FrameworkTypeKind.FunctionPointer => $"method*{FunctionPointerSignature!.DisplayName}",
		FrameworkTypeKind.Modified =>
			$"{(IsRequiredModifier ? "modreq" : "modopt")}({Modifier!.DisplayName}) {ElementType!.DisplayName}",
		_ => throw new InvalidOperationException($"Unknown framework type kind {Kind}.")
	};

	public static FrameworkTypeId Primitive(string metadataName) =>
		new(FrameworkTypeKind.Primitive, metadataName: metadataName);

	public static FrameworkTypeId Named(
		string assemblyName,
		string metadataName,
		FrameworkTypeId? declaringType = null) =>
		new(
			FrameworkTypeKind.Named,
			assemblyName,
			metadataName,
			declaringType);

	public static FrameworkTypeId GenericInstantiation(
		FrameworkTypeId genericType,
		IReadOnlyList<FrameworkTypeId> arguments) =>
		new(
			FrameworkTypeKind.GenericInstantiation,
			elementType: genericType,
			genericArguments: arguments);

	public static FrameworkTypeId SzArray(FrameworkTypeId elementType) =>
		new(FrameworkTypeKind.SzArray, elementType: elementType);

	public static FrameworkTypeId Array(
		FrameworkTypeId elementType,
		ArrayShape shape) =>
		new(
			FrameworkTypeKind.Array,
			elementType: elementType,
			arrayShape: new FrameworkArrayShape(
				shape.Rank,
				shape.Sizes,
				shape.LowerBounds));

	public static FrameworkTypeId ByReference(FrameworkTypeId elementType) =>
		new(FrameworkTypeKind.ByReference, elementType: elementType);

	public static FrameworkTypeId Pointer(FrameworkTypeId elementType) =>
		new(FrameworkTypeKind.Pointer, elementType: elementType);

	public static FrameworkTypeId GenericTypeParameter(int index) =>
		new(FrameworkTypeKind.GenericTypeParameter, genericParameterIndex: index);

	public static FrameworkTypeId GenericMethodParameter(int index) =>
		new(FrameworkTypeKind.GenericMethodParameter, genericParameterIndex: index);

	public static FrameworkTypeId FunctionPointer(FrameworkMethodSignatureId signature) =>
		new(FrameworkTypeKind.FunctionPointer, functionPointerSignature: signature);

	public static FrameworkTypeId Modified(
		FrameworkTypeId modifier,
		FrameworkTypeId unmodifiedType,
		bool isRequired) =>
		new(
			FrameworkTypeKind.Modified,
			elementType: unmodifiedType,
			modifier: modifier,
			isRequiredModifier: isRequired);

	public bool Equals(FrameworkTypeId? other)
	{
		if (ReferenceEquals(this, other))
		{
			return true;
		}
		return other is not null &&
			Kind == other.Kind &&
			string.Equals(AssemblyName, other.AssemblyName, StringComparison.Ordinal) &&
			string.Equals(MetadataName, other.MetadataName, StringComparison.Ordinal) &&
			Equals(DeclaringType, other.DeclaringType) &&
			Equals(ElementType, other.ElementType) &&
			GenericParameterIndex == other.GenericParameterIndex &&
			Equals(ArrayShape, other.ArrayShape) &&
			Equals(FunctionPointerSignature, other.FunctionPointerSignature) &&
			Equals(Modifier, other.Modifier) &&
			IsRequiredModifier == other.IsRequiredModifier &&
			GenericArguments.SequenceEqual(other.GenericArguments);
	}

	public override bool Equals(object? obj) => Equals(obj as FrameworkTypeId);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Kind);
		hash.Add(AssemblyName, StringComparer.Ordinal);
		hash.Add(MetadataName, StringComparer.Ordinal);
		hash.Add(DeclaringType);
		hash.Add(ElementType);
		hash.Add(GenericParameterIndex);
		hash.Add(ArrayShape);
		hash.Add(FunctionPointerSignature);
		hash.Add(Modifier);
		hash.Add(IsRequiredModifier);
		foreach (var argument in GenericArguments)
		{
			hash.Add(argument);
		}
		return hash.ToHashCode();
	}
}

internal sealed class FrameworkMethodSignatureId : IEquatable<FrameworkMethodSignatureId>
{
	public FrameworkMethodSignatureId(
		byte header,
		int genericParameterCount,
		int requiredParameterCount,
		FrameworkTypeId returnType,
		IReadOnlyList<FrameworkTypeId> parameterTypes)
	{
		Header = header;
		GenericParameterCount = genericParameterCount;
		RequiredParameterCount = requiredParameterCount;
		ReturnType = returnType;
		ParameterTypes = parameterTypes.ToImmutableArray();
	}

	public byte Header { get; }

	public int GenericParameterCount { get; }

	public int RequiredParameterCount { get; }

	public FrameworkTypeId ReturnType { get; }

	public ImmutableArray<FrameworkTypeId> ParameterTypes { get; }

	public bool IsInstance => (Header & 0x20) != 0;

	public string DisplayName =>
		$"{ReturnType.DisplayName}({string.Join(",", ParameterTypes.Select(static item => item.DisplayName))})";

	public static FrameworkMethodSignatureId From(MethodSignature<FrameworkTypeId> signature) =>
		new(
			signature.Header.RawValue,
			signature.GenericParameterCount,
			signature.RequiredParameterCount,
			signature.ReturnType,
			signature.ParameterTypes);

	public bool Equals(FrameworkMethodSignatureId? other) =>
		other is not null &&
		Header == other.Header &&
		GenericParameterCount == other.GenericParameterCount &&
		RequiredParameterCount == other.RequiredParameterCount &&
		ReturnType.Equals(other.ReturnType) &&
		ParameterTypes.SequenceEqual(other.ParameterTypes);

	public override bool Equals(object? obj) => Equals(obj as FrameworkMethodSignatureId);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Header);
		hash.Add(GenericParameterCount);
		hash.Add(RequiredParameterCount);
		hash.Add(ReturnType);
		foreach (var parameter in ParameterTypes)
		{
			hash.Add(parameter);
		}
		return hash.ToHashCode();
	}
}

internal sealed class FrameworkMemberId : IEquatable<FrameworkMemberId>
{
	public FrameworkMemberId(
		FrameworkTypeId declaringType,
		string name,
		FrameworkMethodSignatureId signature,
		IReadOnlyList<FrameworkTypeId>? methodTypeArguments = null)
	{
		DeclaringType = declaringType;
		Name = name;
		Signature = signature;
		MethodTypeArguments = methodTypeArguments?.ToImmutableArray() ?? [];
	}

	public FrameworkTypeId DeclaringType { get; }

	public string Name { get; }

	public FrameworkMethodSignatureId Signature { get; }

	public ImmutableArray<FrameworkTypeId> MethodTypeArguments { get; }

	public string AssemblyName => GetNamedType(DeclaringType).AssemblyName!;

	public string DisplayName =>
		$"{DeclaringType.DisplayName}::{Name}" +
		(MethodTypeArguments.Length == 0
			? string.Empty
			: $"<{string.Join(",", MethodTypeArguments.Select(static item => item.DisplayName))}>") +
		$" {Signature.DisplayName}";

	public bool Equals(FrameworkMemberId? other) =>
		other is not null &&
		DeclaringType.Equals(other.DeclaringType) &&
		string.Equals(Name, other.Name, StringComparison.Ordinal) &&
		Signature.Equals(other.Signature) &&
		MethodTypeArguments.SequenceEqual(other.MethodTypeArguments);

	public override bool Equals(object? obj) => Equals(obj as FrameworkMemberId);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(DeclaringType);
		hash.Add(Name, StringComparer.Ordinal);
		hash.Add(Signature);
		foreach (var argument in MethodTypeArguments)
		{
			hash.Add(argument);
		}
		return hash.ToHashCode();
	}

	private static FrameworkTypeId GetNamedType(FrameworkTypeId type) =>
		type.Kind == FrameworkTypeKind.GenericInstantiation
			? GetNamedType(type.ElementType!)
			: type.Kind == FrameworkTypeKind.Named
				? type
				: throw new InvalidOperationException(
					$"Declaring type '{type.DisplayName}' is not a named metadata type.");
}

internal sealed class FrameworkArrayShape : IEquatable<FrameworkArrayShape>
{
	public FrameworkArrayShape(
		int rank,
		ImmutableArray<int> sizes,
		ImmutableArray<int> lowerBounds)
	{
		Rank = rank;
		Sizes = sizes;
		LowerBounds = lowerBounds;
	}

	public int Rank { get; }

	public ImmutableArray<int> Sizes { get; }

	public ImmutableArray<int> LowerBounds { get; }

	public bool Equals(FrameworkArrayShape? other) =>
		other is not null &&
		Rank == other.Rank &&
		Sizes.SequenceEqual(other.Sizes) &&
		LowerBounds.SequenceEqual(other.LowerBounds);

	public override bool Equals(object? obj) => Equals(obj as FrameworkArrayShape);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Rank);
		foreach (var size in Sizes)
		{
			hash.Add(size);
		}
		foreach (var lowerBound in LowerBounds)
		{
			hash.Add(lowerBound);
		}
		return hash.ToHashCode();
	}
}

internal enum FrameworkBindingKind
{
	ManagedBody,
	ShadowMethod,
	Intrinsic,
	PlatformOperation,
	Unsupported
}

[Flags]
internal enum FrameworkEffects
{
	None = 0,
	MayAllocate = 1 << 0,
	MayThrow = 1 << 1,
	MayCollect = 1 << 2,
	ReadsManagedMemory = 1 << 3,
	WritesManagedMemory = 1 << 4,
	ReadsNativeMemory = 1 << 5,
	WritesNativeMemory = 1 << 6,
	RetainsNativePointer = 1 << 7,
	RequiresReflectionMetadata = 1 << 8,
	RequiresTypeInitialization = 1 << 9
}

internal readonly record struct FrameworkFeature(string Name)
{
	public static FrameworkFeature ManagedStrings { get; } = new("managed-strings");

	public static FrameworkFeature NativeCStrings { get; } = new("native-cstrings");

	public static FrameworkFeature NullableValues { get; } = new("nullable-values");

	public static FrameworkFeature Spans { get; } = new("spans");

	public static FrameworkFeature ManagedArrays { get; } = new("managed-arrays");

	public static FrameworkFeature ManagedCollections { get; } =
		new("managed-collections");

	public static FrameworkFeature ManagedGc { get; } = new("managed-gc");

	public static FrameworkFeature NativeMemory { get; } = new("native-memory");

	public static FrameworkFeature AmigaInterop { get; } = new("amiga-interop");

	public static FrameworkFeature ManagedExceptions { get; } = new("managed-exceptions");

	public static FrameworkFeature Numerics { get; } = new("numerics");

	public static FrameworkFeature IntegerFormatting { get; } =
		new("integer-formatting");

	public static FrameworkFeature StringInterpolation { get; } =
		new("string-interpolation");

	public static FrameworkFeature BinaryPrimitives { get; } = new("binary-primitives");

	public static FrameworkFeature Delegates { get; } = new("delegates");

	public static FrameworkFeature ManagedObjects { get; } = new("managed-objects");
}

internal sealed record FrameworkEffectSummary(
	FrameworkEffects Effects,
	IReadOnlyList<FrameworkFeature> RequiredFeatures)
{
	public static FrameworkEffectSummary None { get; } =
		new(FrameworkEffects.None, Array.Empty<FrameworkFeature>());
}

internal sealed record FrameworkBinding(
	FrameworkMemberId Member,
	FrameworkBindingKind Kind,
	string? Target,
	FrameworkEffectSummary EffectSummary,
	string? Reason = null,
	string? SuggestedAlternative = null,
	FrameworkShadowMethod? ShadowMethod = null,
	bool PreservesVirtualDispatch = false)
{
	public bool IsAccepted => Kind != FrameworkBindingKind.Unsupported;
}

internal sealed record FrameworkShadowMethod(
	string AssemblyName,
	string TypeName,
	string MethodName);
