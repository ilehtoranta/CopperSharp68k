/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Metadata;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Framework;

internal enum FrameworkDefaultEqualityKind
{
	Unsupported,
	SealedObjectEquals,
	SealedIEquatable
}

internal readonly record struct FrameworkBindingContext(
	string TypeName,
	MethodSignature<CilType> CilSignature,
	CilType? ConstructedDeclaringType,
	bool IsSupportedNullableType,
	IReadOnlyList<CilType>? MethodTypeArguments = null,
	IReadOnlyList<bool?>? MethodTypeArgumentContainsReferences = null,
	FrameworkDefaultEqualityKind DefaultEqualityKind =
		FrameworkDefaultEqualityKind.Unsupported,
	IReadOnlyList<bool?>? DeclaringTypeArgumentContainsReferences = null);

internal static class FrameworkBindingRegistry
{
	private static readonly FrameworkTypeId Void =
		FrameworkTypeId.Primitive("System.Void");
	private static readonly FrameworkTypeId Boolean =
		FrameworkTypeId.Primitive("System.Boolean");
	private static readonly FrameworkTypeId Byte =
		FrameworkTypeId.Primitive("System.Byte");
	private static readonly FrameworkTypeId SByte =
		FrameworkTypeId.Primitive("System.SByte");
	private static readonly FrameworkTypeId Int16 =
		FrameworkTypeId.Primitive("System.Int16");
	private static readonly FrameworkTypeId UInt16 =
		FrameworkTypeId.Primitive("System.UInt16");
	private static readonly FrameworkTypeId Int32 =
		FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId UInt32 =
		FrameworkTypeId.Primitive("System.UInt32");
	private static readonly FrameworkTypeId Int64 =
		FrameworkTypeId.Primitive("System.Int64");
	private static readonly FrameworkTypeId UInt64 =
		FrameworkTypeId.Primitive("System.UInt64");
	private static readonly FrameworkTypeId IntPtr =
		FrameworkTypeId.Primitive("System.IntPtr");
	private static readonly FrameworkTypeId UIntPtr =
		FrameworkTypeId.Primitive("System.UIntPtr");
	private static readonly FrameworkTypeId Single =
		FrameworkTypeId.Primitive("System.Single");
	private static readonly FrameworkTypeId Double =
		FrameworkTypeId.Primitive("System.Double");
	private static readonly FrameworkTypeId MidpointRounding =
		Named("System.Runtime", "System.MidpointRounding");
	private static readonly FrameworkTypeId Char =
		FrameworkTypeId.Primitive("System.Char");
	private static readonly FrameworkTypeId Object =
		FrameworkTypeId.Primitive("System.Object");
	private static readonly FrameworkTypeId String =
		FrameworkTypeId.Primitive("System.String");
	private static readonly FrameworkTypeId ArrayDefinition =
		Named("System.Runtime", "System.Array");
	private static readonly FrameworkTypeId FrameworkStringComparison =
		Named("System.Runtime", "System.StringComparison");
	private static readonly FrameworkTypeId FileAttributes =
		Named("System.Runtime", "System.IO.FileAttributes");
	private static readonly FrameworkTypeId Stopwatch =
		Named("System.Runtime", "System.Diagnostics.Stopwatch");
	private static readonly FrameworkTypeId TimeSpan =
		Named("System.Runtime", "System.TimeSpan");
	private static readonly FrameworkTypeId GenericType0 =
		FrameworkTypeId.GenericTypeParameter(0);
	private static readonly FrameworkTypeId GenericType1 =
		FrameworkTypeId.GenericTypeParameter(1);
	private static readonly FrameworkTypeId GenericMethod0 =
		FrameworkTypeId.GenericMethodParameter(0);
	private static readonly FrameworkTypeId GenericMethod1 =
		FrameworkTypeId.GenericMethodParameter(1);
	private static readonly FrameworkTypeId NullableDefinition =
		Named("System.Runtime", "System.Nullable`1");
	private static readonly FrameworkTypeId ListDefinition =
		Named("System.Collections", "System.Collections.Generic.List`1");
	private static readonly FrameworkTypeId DictionaryDefinition =
		Named("System.Collections", "System.Collections.Generic.Dictionary`2");
	private static readonly FrameworkTypeId DictionaryValueCollectionDefinition =
		FrameworkTypeId.Named(
			"System.Collections",
			"ValueCollection",
			DictionaryDefinition);
	private static readonly FrameworkTypeId EqualityComparerDefinition =
		Named("System.Collections", "System.Collections.Generic.EqualityComparer`1");
	private static readonly FrameworkTypeId EqualityComparerInterfaceDefinition =
		Named("System.Runtime", "System.Collections.Generic.IEqualityComparer`1");
	private static readonly FrameworkTypeId EquatableDefinition =
		Named("System.Runtime", "System.IEquatable`1");
	private static readonly FrameworkTypeId ListEnumeratorDefinition =
		FrameworkTypeId.Named(
			"System.Collections",
			"Enumerator",
			ListDefinition);
	private static readonly FrameworkTypeId SpanDefinition =
		Named("System.Runtime", "System.Span`1");
	private static readonly FrameworkTypeId SpanOfChar =
		FrameworkTypeId.GenericInstantiation(SpanDefinition, [Char]);
	private static readonly FrameworkTypeId ReadOnlySpanDefinition =
		Named("System.Runtime", "System.ReadOnlySpan`1");
	private static readonly FrameworkTypeId MemoryDefinition =
		Named("System.Runtime", "System.Memory`1");
	private static readonly FrameworkTypeId ReadOnlyMemoryDefinition =
		Named("System.Runtime", "System.ReadOnlyMemory`1");
	private static readonly FrameworkTypeId EnumerableDefinition =
		Named("System.Linq", "System.Linq.Enumerable");
	private static readonly FrameworkTypeId EnumerableInterfaceDefinition =
		Named("System.Runtime", "System.Collections.Generic.IEnumerable`1");
	private static readonly FrameworkTypeId EnumeratorInterfaceDefinition =
		Named("System.Runtime", "System.Collections.Generic.IEnumerator`1");
	private static readonly FrameworkTypeId NonGenericEnumeratorInterface =
		Named("System.Runtime", "System.Collections.IEnumerator");
	private static readonly FrameworkTypeId OrderedEnumerableInterfaceDefinition =
		Named("System.Linq", "System.Linq.IOrderedEnumerable`1");
	private static readonly FrameworkTypeId EnumerableOfInt32 =
		FrameworkTypeId.GenericInstantiation(
			Named("System.Runtime", "System.Collections.Generic.IEnumerable`1"),
			[Int32]);
	private static readonly FrameworkTypeId EnumerableOfGenericMethod0 =
		FrameworkTypeId.GenericInstantiation(
			Named("System.Runtime", "System.Collections.Generic.IEnumerable`1"),
			[GenericMethod0]);
	private static readonly FrameworkTypeId EnumerableOfGenericMethod1 =
		FrameworkTypeId.GenericInstantiation(
			Named("System.Runtime", "System.Collections.Generic.IEnumerable`1"),
			[GenericMethod1]);
	private static readonly FrameworkTypeId OrderedEnumerableOfGenericMethod0 =
		FrameworkTypeId.GenericInstantiation(
			OrderedEnumerableInterfaceDefinition,
			[GenericMethod0]);
	private static readonly FrameworkTypeId FuncOfGenericMethod0And1 =
		FrameworkTypeId.GenericInstantiation(
			Named("System.Runtime", "System.Func`2"),
			[GenericMethod0, GenericMethod1]);
	private static readonly FrameworkTypeId FuncOfGenericMethod0AndBoolean =
		FrameworkTypeId.GenericInstantiation(
			Named("System.Runtime", "System.Func`2"),
			[GenericMethod0, Boolean]);
	private static readonly FrameworkTypeId FuncOfGenericMethod0AndInt32 =
		FrameworkTypeId.GenericInstantiation(
			Named("System.Runtime", "System.Func`2"),
			[GenericMethod0, Int32]);
	private static readonly FrameworkTypeId ReadOnlySpanOfChar =
		FrameworkTypeId.GenericInstantiation(ReadOnlySpanDefinition, [Char]);
	private static readonly FrameworkTypeId ReadOnlySpanOfObject =
		FrameworkTypeId.GenericInstantiation(ReadOnlySpanDefinition, [Object]);
	private static readonly FrameworkTypeId ReadOnlySpanOfGenericMethod0 =
		FrameworkTypeId.GenericInstantiation(
			ReadOnlySpanDefinition,
			[GenericMethod0]);
	private static readonly FrameworkTypeId DefaultInterpolatedStringHandler =
		Named(
			"System.Runtime",
			"System.Runtime.CompilerServices.DefaultInterpolatedStringHandler");

	private static readonly IReadOnlyDictionary<FrameworkMemberId, FrameworkBinding>
		FrameworkBindings = CreateFrameworkBindings();

	public static FrameworkBinding? TryBind(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (FrameworkBindings.TryGetValue(member, out var exact))
		{
			return exact;
		}

		if (TryBindDefaultInterpolatedStringHandler(member, context) is { } handler)
		{
			return handler;
		}

		if (TryBindNullable(member, context) is { } nullable)
		{
			return nullable;
		}

		if (TryBindEquatable(member) is { } equatable)
		{
			return equatable;
		}

		if (TryBindEqualityComparerInterface(member, context) is { } comparerInterface)
		{
			return comparerInterface;
		}

		if (TryBindEqualityComparer(member, context) is { } equalityComparer)
		{
			return equalityComparer;
		}

		if (TryBindArray(member, context) is { } array)
		{
			return array;
		}

		if (TryBindList(member, context) is { } list)
		{
			return list;
		}

		if (TryBindDictionary(member, context) is { } dictionary)
		{
			return dictionary;
		}

		if (TryBindListEnumerator(member, context) is { } listEnumerator)
		{
			return listEnumerator;
		}

		if (TryBindSpan(member, context) is { } span)
		{
			return span;
		}

		if (TryBindReadOnlySpan(member, context) is { } readOnlySpan)
		{
			return readOnlySpan;
		}

		if (TryBindMemory(member, context) is { } memory)
		{
			return memory;
		}

		if (TryBindReadOnlyMemory(member, context) is { } readOnlyMemory)
		{
			return readOnlyMemory;
		}

		if (TryBindMemoryExtensions(member, context) is { } memoryExtensions)
		{
			return memoryExtensions;
		}

		if (TryBindEnumerable(member, context) is { } enumerable)
		{
			return enumerable;
		}

		if (TryBindOrderedEnumerationInterfaces(member, context) is { } orderedEnumeration)
		{
			return orderedEnumeration;
		}

		if (TryBindRuntimeHelpers(member, context) is { } runtimeHelpers)
		{
			return runtimeHelpers;
		}

		if (TryBindDelegate(member, context) is { } delegateBinding)
		{
			return delegateBinding;
		}

		return TryBindCompilerIntrinsic(member, context);
	}

	public static FrameworkShadowFieldBinding? TryBindReadOnlyStaticField(
		string assemblyName,
		string typeName,
		string fieldName,
		CilType fieldType)
	{
		if (!string.Equals(assemblyName, "System.Runtime", StringComparison.Ordinal) ||
			!string.Equals(typeName, "System.Diagnostics.Stopwatch", StringComparison.Ordinal))
		{
			return null;
		}

		var expectedType = fieldName switch
		{
			"Frequency" => "long",
			"IsHighResolution" => "bool",
			_ => null
		};
		if (expectedType is null ||
			!string.Equals(fieldType.DisplayName, expectedType, StringComparison.Ordinal))
		{
			return null;
		}

		return new FrameworkShadowFieldBinding(
			assemblyName,
			typeName,
			fieldName,
			expectedType,
			"CopperSharp.Runtime.AmigaPal",
			fieldName == "Frequency"
				? "CopperSharp.Runtime.AmigaPal.StopwatchFrequencyField"
				: "CopperSharp.Runtime.AmigaPal.StopwatchHighResolutionField",
			fieldName);
	}

	private static FrameworkBinding? TryBindEquatable(FrameworkMemberId member)
	{
		if (member.DeclaringType is not
			{
				Kind: FrameworkTypeKind.GenericInstantiation,
				GenericArguments: [_]
			} ||
			!member.DeclaringType.ElementType!.Equals(EquatableDefinition) ||
			member.MethodTypeArguments.Length != 0 ||
			member.Name != "Equals" ||
			!member.Signature.IsInstance ||
			!SignatureEquals(member, Boolean, GenericType0))
		{
			return null;
		}

		return new FrameworkBinding(
			member,
			FrameworkBindingKind.ManagedBody,
			"managed:closed-world-sealed-equatable-dispatch",
			Effects(
				FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.ManagedObjects,
				FrameworkFeature.ManagedGc));
	}

	private static FrameworkBinding? TryBindEqualityComparer(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (member.DeclaringType.Kind != FrameworkTypeKind.GenericInstantiation ||
			!member.DeclaringType.ElementType!.Equals(EqualityComparerDefinition) ||
			member.DeclaringType.GenericArguments.Length != 1 ||
			member.MethodTypeArguments.Length != 0 ||
			context.ConstructedDeclaringType is not
			{
				GenericArguments: [var element]
			} ||
			!IsSupportedPublicEqualityComparerElement(element, context))
		{
			return null;
		}

		var comparerType = FrameworkTypeId.GenericInstantiation(
			EqualityComparerDefinition,
			[GenericType0]);
		if (member.Name == ".ctor" && SignatureEquals(member, Void))
		{
			return Intrinsic(
				member,
				"intrinsic:object-ctor",
				Effects(
					FrameworkEffects.None,
					FrameworkFeature.ManagedObjects));
		}

		var shadow = new FrameworkShadowMethod(
			"CopperSharp.Runtime.Managed",
			"CopperSharp.Runtime.ShadowEqualityComparer`1",
			member.Name == "get_Default" ? "GetDefault" : "Equals");
		if (member.Name == "get_Default" &&
			!member.Signature.IsInstance &&
			member.Signature.GenericParameterCount == 0 &&
			member.Signature.RequiredParameterCount == 0 &&
			member.Signature.ReturnType.Equals(comparerType))
		{
			return Shadow(
				member,
				shadow,
				Effects(
					FrameworkEffects.MayAllocate |
						FrameworkEffects.MayThrow |
						FrameworkEffects.MayCollect |
						FrameworkEffects.ReadsManagedMemory |
						FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.ManagedObjects,
					FrameworkFeature.ManagedGc));
		}
		if (member.Name == "Equals" &&
			SignatureEquals(member, Boolean, GenericType0, GenericType0))
		{
			return Shadow(
				member,
				shadow,
				Effects(
					FrameworkEffects.None,
					FrameworkFeature.ManagedObjects,
					FrameworkFeature.ManagedGc),
				preservesVirtualDispatch: true);
		}
		if (member.Name == "GetHashCode" &&
			SignatureEquals(member, Int32, GenericType0))
		{
			return Shadow(
				member,
				new FrameworkShadowMethod(
					shadow.AssemblyName,
					shadow.TypeName,
					"GetHashCode"),
				Effects(
					FrameworkEffects.None,
					FrameworkFeature.ManagedObjects,
					FrameworkFeature.ManagedGc),
				preservesVirtualDispatch: true);
		}

		return null;
	}

	private static FrameworkBinding? TryBindArray(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (!member.DeclaringType.Equals(ArrayDefinition) ||
			member.MethodTypeArguments is not [_] ||
			context.MethodTypeArguments is not [var element] ||
			!IsSupportedArrayElement(element, context))
		{
			return null;
		}

		var array = FrameworkTypeId.SzArray(GenericMethod0);
		var shadowName = member.Name switch
		{
			"Empty" when GenericStaticSignatureEquals(member, array) => "Empty",
			"Fill" when GenericStaticSignatureEquals(
				member, Void, array, GenericMethod0) => "Fill",
			"Fill" when GenericStaticSignatureEquals(
				member, Void, array, GenericMethod0, Int32, Int32) => "Fill",
			"IndexOf" when IsSupportedListEqualityElement(element, context) &&
				GenericStaticSignatureEquals(
					member, Int32, array, GenericMethod0) => "IndexOf",
			"IndexOf" when IsSupportedListEqualityElement(element, context) &&
				GenericStaticSignatureEquals(
					member, Int32, array, GenericMethod0, Int32) => "IndexOf",
			"IndexOf" when IsSupportedListEqualityElement(element, context) &&
				GenericStaticSignatureEquals(
					member, Int32, array, GenericMethod0, Int32, Int32) => "IndexOf",
			"LastIndexOf" when IsSupportedListEqualityElement(element, context) &&
				GenericStaticSignatureEquals(
					member, Int32, array, GenericMethod0) => "LastIndexOf",
			"LastIndexOf" when IsSupportedListEqualityElement(element, context) &&
				GenericStaticSignatureEquals(
					member, Int32, array, GenericMethod0, Int32) => "LastIndexOf",
			"LastIndexOf" when IsSupportedListEqualityElement(element, context) &&
				GenericStaticSignatureEquals(
					member, Int32, array, GenericMethod0, Int32, Int32) => "LastIndexOf",
			"Reverse" when GenericStaticSignatureEquals(
				member, Void, array) => "Reverse",
			"Reverse" when GenericStaticSignatureEquals(
				member, Void, array, Int32, Int32) => "Reverse",
			_ => null
		};
		if (shadowName is null)
		{
			return null;
		}

		var effects = member.Name == "Empty"
			? FrameworkEffects.MayAllocate |
				FrameworkEffects.MayThrow |
				FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory
			: member.Name is "Fill" or "Reverse"
				? FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory
				: FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsManagedMemory;
		return Shadow(
			member,
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowArray",
				shadowName),
			Effects(
				effects,
				FrameworkFeature.ManagedArrays,
				FrameworkFeature.ManagedGc));
	}

	private static bool IsSupportedArrayElement(
		CilType element,
		FrameworkBindingContext context) =>
		(element.IsSupportedScalar &&
		 element.Kind is not (CilTypeKind.ManagedPointer or
			CilTypeKind.UnmanagedPointer or CilTypeKind.GenericParameter)) ||
		IsSupportedNullableListEqualityElement(element) ||
		(element.Kind == CilTypeKind.ValueType &&
		 context.MethodTypeArgumentContainsReferences is [false]);

	private static FrameworkBinding? TryBindEqualityComparerInterface(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (member.DeclaringType.Kind != FrameworkTypeKind.GenericInstantiation ||
			!member.DeclaringType.ElementType!.Equals(
				EqualityComparerInterfaceDefinition) ||
			member.DeclaringType.GenericArguments.Length != 1 ||
			member.MethodTypeArguments.Length != 0 ||
			context.ConstructedDeclaringType is not
			{
				GenericArguments: [var element]
			} ||
			!IsSupportedPublicEqualityComparerElement(element, context))
		{
			return null;
		}

		var methodName = member.Name switch
		{
			"Equals" when SignatureEquals(
				member,
				Boolean,
				GenericType0,
				GenericType0) => "Equals",
			"GetHashCode" when SignatureEquals(
				member,
				Int32,
				GenericType0) => "GetHashCode",
			_ => null
		};
		return methodName is null
			? null
			: Shadow(
				member,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.Managed",
					"CopperSharp.Runtime.IShadowEqualityComparer`1",
					methodName),
				Effects(
					FrameworkEffects.None,
					FrameworkFeature.ManagedObjects,
					FrameworkFeature.ManagedGc));
	}

	private static FrameworkBinding? TryBindDefaultInterpolatedStringHandler(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (!member.DeclaringType.Equals(DefaultInterpolatedStringHandler))
		{
			return null;
		}

		string? shadowName = null;
		if (member.MethodTypeArguments.Length == 0)
		{
			if (member.Name == ".ctor" && SignatureEquals(member, Void, Int32, Int32))
			{
				shadowName = ".ctor";
			}
			else if (member.Name == "AppendLiteral" &&
				SignatureEquals(member, Void, String))
			{
				shadowName = "AppendLiteral";
			}
			else if (member.Name == "ToStringAndClear" &&
				SignatureEquals(member, String))
			{
				shadowName = "ToStringAndClear";
			}
		}
		else if (member.Name == "AppendFormatted" &&
			context.MethodTypeArguments is [{ DisplayName: var argumentType }])
		{
			if (argumentType == "int" &&
				context.CilSignature.ParameterTypes is [{ DisplayName: "int" }])
			{
				shadowName = "AppendFormattedInt32";
			}
			else if (argumentType == "uint" &&
				context.CilSignature.ParameterTypes is
					[{ DisplayName: "uint" }, { DisplayName: "string" }])
			{
				shadowName = "AppendFormattedUInt32";
			}
		}

		if (shadowName is null)
		{
			return null;
		}

		return Shadow(
			member,
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowDefaultInterpolatedStringHandler",
				shadowName),
			Effects(
				FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.IntegerFormatting,
				FrameworkFeature.ManagedArrays,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.StringInterpolation));
	}

	private static FrameworkBinding? TryBindMemoryExtensions(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (!member.DeclaringType.Equals(
				Named("System.Memory", "System.MemoryExtensions")) ||
			member.Name != "SequenceEqual" ||
			member.MethodTypeArguments is not [var element] ||
			!element.Equals(Char) ||
			member.Signature.Header != 0x10 ||
			member.Signature.GenericParameterCount != 1 ||
			member.Signature.RequiredParameterCount != 2 ||
			!member.Signature.ReturnType.Equals(Boolean) ||
			member.Signature.ParameterTypes is not [var first, var second] ||
			!first.Equals(ReadOnlySpanOfGenericMethod0) ||
			!second.Equals(ReadOnlySpanOfGenericMethod0) ||
			context.MethodTypeArguments is not
				[{ DisplayName: "char" }] ||
			context.CilSignature is not
			{
				Header.IsInstance: false,
				ReturnType.DisplayName: "bool"
			})
		{
			return null;
		}

		return Intrinsic(
			member,
			"intrinsic:readonly-span-sequence-equal:char",
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.Spans));
	}

	private static FrameworkBinding? TryBindEnumerable(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (!member.DeclaringType.Equals(EnumerableDefinition))
		{
			return null;
		}

		string? shadowName = null;
		if (member.Name == "Range" &&
			member.MethodTypeArguments.Length == 0 &&
			member.Signature.Header == 0x00 &&
			member.Signature.GenericParameterCount == 0 &&
			member.Signature.RequiredParameterCount == 2 &&
			member.Signature.ReturnType.Equals(EnumerableOfInt32) &&
			member.Signature.ParameterTypes.SequenceEqual([Int32, Int32]))
		{
			shadowName = "Range";
		}
		else if (member.Name == "Repeat" &&
			member.MethodTypeArguments is [_] &&
			context.MethodTypeArguments is [var repeatElement] &&
			IsSupportedEnumerableElement(repeatElement) &&
			GenericStaticSignatureEquals(
				member,
				EnumerableOfGenericMethod0,
				GenericMethod0,
				Int32))
		{
			shadowName = "Repeat";
		}
		else if (member.Name == "Select" &&
			member.MethodTypeArguments is [_, _] &&
			context.MethodTypeArguments is [var sourceElement, var resultElement] &&
			sourceElement.DisplayName == "int" &&
			resultElement.DisplayName == "int" &&
			member.Signature.Header == 0x10 &&
			member.Signature.GenericParameterCount == 2 &&
			member.Signature.RequiredParameterCount == 2 &&
			member.Signature.ReturnType.Equals(EnumerableOfGenericMethod1) &&
			member.Signature.ParameterTypes.SequenceEqual(
				[EnumerableOfGenericMethod0, FuncOfGenericMethod0And1]))
		{
			shadowName = "Select";
		}
		else if (member.Name == "Where" &&
			member.MethodTypeArguments is [_] &&
			context.MethodTypeArguments is [{ DisplayName: "int" }] &&
			member.Signature.Header == 0x10 &&
			member.Signature.GenericParameterCount == 1 &&
			member.Signature.RequiredParameterCount == 2 &&
			member.Signature.ReturnType.Equals(EnumerableOfGenericMethod0) &&
			member.Signature.ParameterTypes.SequenceEqual(
				[EnumerableOfGenericMethod0, FuncOfGenericMethod0AndBoolean]))
		{
			shadowName = "Where";
		}
		else if (member.Name == "Take" &&
			member.MethodTypeArguments is [_] &&
			context.MethodTypeArguments is [{ DisplayName: "int" }] &&
			member.Signature.Header == 0x10 &&
			member.Signature.GenericParameterCount == 1 &&
			member.Signature.RequiredParameterCount == 2 &&
			member.Signature.ReturnType.Equals(EnumerableOfGenericMethod0) &&
			member.Signature.ParameterTypes.SequenceEqual(
				[EnumerableOfGenericMethod0, Int32]))
		{
			shadowName = "Take";
		}
		else if (member.Name == "Any" &&
			member.MethodTypeArguments is [_] &&
			context.MethodTypeArguments is [{ DisplayName: "int" }] &&
			GenericStaticSignatureEquals(
				member,
				Boolean,
				EnumerableOfGenericMethod0))
		{
			shadowName = "Any";
		}
		else if (member.Name == "Any" &&
			member.MethodTypeArguments is [_] &&
			context.MethodTypeArguments is [{ DisplayName: "int" }] &&
			member.Signature.Header == 0x10 &&
			member.Signature.GenericParameterCount == 1 &&
			member.Signature.RequiredParameterCount == 2 &&
			member.Signature.ReturnType.Equals(Boolean) &&
			member.Signature.ParameterTypes.SequenceEqual(
				[EnumerableOfGenericMethod0, FuncOfGenericMethod0AndBoolean]))
		{
			shadowName = "AnyPredicate";
		}
		else if (member.Name == "Sum" &&
			member.MethodTypeArguments.Length == 0 &&
			member.Signature.Header == 0x00 &&
			member.Signature.GenericParameterCount == 0 &&
			member.Signature.RequiredParameterCount == 1 &&
			member.Signature.ReturnType.Equals(Int32) &&
			member.Signature.ParameterTypes.SequenceEqual([EnumerableOfInt32]))
		{
			shadowName = "Sum";
		}
		else if (member.Name == "Sum" &&
			member.MethodTypeArguments is [_] &&
			context.MethodTypeArguments is [var sumElement] &&
			(sumElement.DisplayName == "int" ||
			 (sumElement.Kind == CilTypeKind.ValueType &&
			  context.MethodTypeArgumentContainsReferences is [false])) &&
			member.Signature.Header == 0x10 &&
			member.Signature.GenericParameterCount == 1 &&
			member.Signature.RequiredParameterCount == 2 &&
			member.Signature.ReturnType.Equals(Int32) &&
			member.Signature.ParameterTypes.SequenceEqual(
				[EnumerableOfGenericMethod0, FuncOfGenericMethod0AndInt32]))
		{
			shadowName = "SumSelector";
		}
		else if (member.Name == "OrderBy" &&
			member.MethodTypeArguments is [_, _] &&
			context.MethodTypeArguments is
			[
				{ Kind: CilTypeKind.ValueType },
				{ DisplayName: "int" }
			] &&
			context.MethodTypeArgumentContainsReferences is [false, false] &&
			member.Signature.Header == 0x10 &&
			member.Signature.GenericParameterCount == 2 &&
			member.Signature.RequiredParameterCount == 2 &&
			member.Signature.ReturnType.Equals(OrderedEnumerableOfGenericMethod0) &&
			member.Signature.ParameterTypes.SequenceEqual(
				[EnumerableOfGenericMethod0, FuncOfGenericMethod0And1]))
		{
			shadowName = "OrderBy";
		}
		else if (member.Name == "ThenBy" &&
			member.MethodTypeArguments is [_, _] &&
			context.MethodTypeArguments is
			[
				{ Kind: CilTypeKind.ValueType },
				{ DisplayName: "int" }
			] &&
			context.MethodTypeArgumentContainsReferences is [false, false] &&
			member.Signature.Header == 0x10 &&
			member.Signature.GenericParameterCount == 2 &&
			member.Signature.RequiredParameterCount == 2 &&
			member.Signature.ReturnType.Equals(OrderedEnumerableOfGenericMethod0) &&
			member.Signature.ParameterTypes.SequenceEqual(
				[OrderedEnumerableOfGenericMethod0, FuncOfGenericMethod0And1]))
		{
			shadowName = "ThenBy";
		}
		else if (member.Name == "ToArray" &&
			member.MethodTypeArguments is [_] &&
			context.MethodTypeArguments is [var arrayElement] &&
			IsSupportedEnumerableElement(arrayElement) &&
			GenericStaticSignatureEquals(
				member,
				FrameworkTypeId.SzArray(GenericMethod0),
				EnumerableOfGenericMethod0))
		{
			shadowName = "ToArray";
		}

		if (shadowName is null)
		{
			return null;
		}

		var effects = FrameworkEffects.MayAllocate |
			FrameworkEffects.MayThrow |
			FrameworkEffects.MayCollect |
			FrameworkEffects.ReadsManagedMemory |
			FrameworkEffects.WritesManagedMemory;
		var features = shadowName is "Select" or "Where" or "AnyPredicate" or
			"SumSelector" or "OrderBy" or "ThenBy"
			? new[]
			{
				FrameworkFeature.Linq,
				FrameworkFeature.ManagedArrays,
				FrameworkFeature.ManagedGc,
				FrameworkFeature.Delegates
			}
			: new[]
			{
				FrameworkFeature.Linq,
				FrameworkFeature.ManagedArrays,
				FrameworkFeature.ManagedGc
			};
		return Shadow(
			member,
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowEnumerable",
				shadowName),
			Effects(effects, features));
	}

	private static FrameworkBinding? TryBindOrderedEnumerationInterfaces(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		FrameworkShadowMethod? target = null;
		var effects = FrameworkEffects.MayThrow |
			FrameworkEffects.ReadsManagedMemory;
		if (member.DeclaringType.Kind == FrameworkTypeKind.GenericInstantiation &&
			member.DeclaringType.ElementType!.Equals(EnumerableInterfaceDefinition) &&
			member.DeclaringType.GenericArguments.Length == 1 &&
			member.MethodTypeArguments.Length == 0 &&
			context.ConstructedDeclaringType is
			{
				GenericArguments: [{ Kind: CilTypeKind.ValueType }]
			} &&
			context.DeclaringTypeArgumentContainsReferences is [false] &&
			member.Name == "GetEnumerator" &&
			SignatureEquals(
				member,
				FrameworkTypeId.GenericInstantiation(
					EnumeratorInterfaceDefinition,
					[GenericType0])))
		{
			target = new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowOrderedEnumerable`1",
				"GetEnumerator");
			effects |= FrameworkEffects.MayAllocate |
				FrameworkEffects.MayCollect |
				FrameworkEffects.WritesManagedMemory;
		}
		else if (member.DeclaringType.Equals(NonGenericEnumeratorInterface) &&
			member.MethodTypeArguments.Length == 0 &&
			member.Name == "MoveNext" &&
			SignatureEquals(member, Boolean))
		{
			target = new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowOrderedEnumeratorBase",
				"MoveNext");
			effects |= FrameworkEffects.WritesManagedMemory;
		}
		else if (member.DeclaringType.Kind == FrameworkTypeKind.GenericInstantiation &&
			member.DeclaringType.ElementType!.Equals(EnumeratorInterfaceDefinition) &&
			member.DeclaringType.GenericArguments.Length == 1 &&
			member.MethodTypeArguments.Length == 0 &&
			context.ConstructedDeclaringType is
			{
				GenericArguments: [{ Kind: CilTypeKind.ValueType }]
			} &&
			context.DeclaringTypeArgumentContainsReferences is [false] &&
			member.Name == "get_Current" &&
			SignatureEquals(member, GenericType0))
		{
			target = new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowOrderedEnumerator`1",
				"get_Current");
		}

		return target is null
			? null
			: Shadow(
				member,
				target,
				Effects(
					effects,
					FrameworkFeature.Linq,
					FrameworkFeature.ManagedArrays,
					FrameworkFeature.ManagedGc));
	}

	public static FrameworkBinding BindOrderedEnumeratorDispose(
		FrameworkMemberId member) =>
		Shadow(
			member,
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowOrderedEnumeratorBase",
				"Dispose"),
			Effects(
				FrameworkEffects.None,
				FrameworkFeature.Linq,
				FrameworkFeature.ManagedGc));

	private static bool IsSupportedEnumerableElement(CilType element) =>
		element.IsSupportedScalar &&
		element.Kind is not CilTypeKind.ManagedPointer and
			not CilTypeKind.GenericParameter;

	private static bool GenericStaticSignatureEquals(
		FrameworkMemberId member,
		FrameworkTypeId returnType,
		params FrameworkTypeId[] parameterTypes) =>
		member.Signature.Header == 0x10 &&
		member.Signature.GenericParameterCount == 1 &&
		member.Signature.RequiredParameterCount == parameterTypes.Length &&
		member.Signature.ReturnType.Equals(returnType) &&
		member.Signature.ParameterTypes.SequenceEqual(parameterTypes);

	private static FrameworkBinding? TryBindRuntimeHelpers(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (member.DeclaringType is not
			{
				Kind: FrameworkTypeKind.Named,
				AssemblyName: "System.Runtime",
				MetadataName: "System.Runtime.CompilerServices.RuntimeHelpers"
			} ||
			member.Name != "IsReferenceOrContainsReferences" ||
			member.Signature.Header != 0x10 ||
			member.Signature.GenericParameterCount != 1 ||
			member.Signature.RequiredParameterCount != 0 ||
			!member.Signature.ReturnType.Equals(Boolean) ||
			member.Signature.ParameterTypes.Length != 0 ||
			member.MethodTypeArguments.Length != 1 ||
			context.MethodTypeArguments is not [_] ||
			context.MethodTypeArgumentContainsReferences is not [var containsReferences] ||
			containsReferences is null)
		{
			return null;
		}

		return Intrinsic(
			member,
			$"intrinsic:runtimehelpers-is-reference-or-contains-references:" +
				(containsReferences.Value ? "true" : "false"),
			FrameworkEffectSummary.None);
	}

	public static FrameworkBinding BindProvenDelegateObjectEquals(
		FrameworkMemberId member) =>
		Intrinsic(
			member,
			"intrinsic:delegate-equality",
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.Delegates));

	public static FrameworkBinding BindListEnumeratorDispose(
		FrameworkMemberId member) =>
		Intrinsic(
			member,
			"intrinsic:list-enumerator-dispose",
			Effects(
				FrameworkEffects.None,
				FrameworkFeature.ManagedCollections));

	private static FrameworkBinding? TryBindDelegate(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		var definition = member.DeclaringType.Kind == FrameworkTypeKind.GenericInstantiation
			? member.DeclaringType.ElementType
			: member.DeclaringType;
		if (definition is not { Kind: FrameworkTypeKind.Named } ||
			!string.Equals(definition.AssemblyName, "System.Runtime", StringComparison.Ordinal) ||
			definition.MetadataName is not { } metadataName ||
			(!metadataName.StartsWith("System.Func`", StringComparison.Ordinal) &&
			 !metadataName.StartsWith("System.Action`", StringComparison.Ordinal) &&
			 metadataName != "System.Action"))
		{
			return null;
		}

		if (member.Name == ".ctor" &&
			context.CilSignature.Header.IsInstance &&
			context.CilSignature.ReturnType.IsVoid &&
			context.CilSignature.ParameterTypes is
			[
				{ DisplayName: "object" },
				{ DisplayName: "nint" }
			])
		{
			return Intrinsic(
				member,
				"intrinsic:delegate-ctor",
				Effects(
					FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.MayCollect |
					FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.Delegates,
					FrameworkFeature.ManagedGc));
		}

		if (member.Name == "Invoke" &&
			context.CilSignature.Header.IsInstance &&
			IsSupportedDelegateInvokeShape(metadataName, context))
		{
			return Intrinsic(
				member,
				"intrinsic:delegate-invoke",
				Effects(
					FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory,
					FrameworkFeature.Delegates));
		}

		return null;
	}

	private static bool IsSupportedDelegateInvokeShape(
		string metadataName,
		FrameworkBindingContext context)
	{
		var arguments = context.ConstructedDeclaringType?.GenericArguments ?? [];
		if (metadataName == "System.Func`1" &&
			arguments is [{ DisplayName: "int" }])
		{
			return true;
		}
		if (metadataName == "System.Func`2" &&
			arguments is [{ DisplayName: "int" }, { DisplayName: "int" or "bool" }])
		{
			return true;
		}
		if (metadataName == "System.Action`1" &&
			arguments is [{ DisplayName: "int" }])
		{
			return true;
		}

		return metadataName == "System.Func`2" &&
			arguments is [{ Kind: CilTypeKind.ValueType }, { DisplayName: "int" }] &&
			context.DeclaringTypeArgumentContainsReferences is [false, false];
	}

	private static IReadOnlyDictionary<FrameworkMemberId, FrameworkBinding>
		CreateFrameworkBindings()
	{
		var bindings = new Dictionary<FrameworkMemberId, FrameworkBinding>();
		AddPlatform(
			bindings,
			Member(
				"System.Console",
				"System.Console",
				"Write",
				isInstance: false,
				Void,
				String),
			"platform:amiga-console-write",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ConsolePal",
				"Write"),
			Effects(
				FrameworkEffects.MayAllocate |
				FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesNativeMemory,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.NativeMemory,
				FrameworkFeature.AmigaInterop,
				FrameworkFeature.AmigaConsole,
				FrameworkFeature.ManagedExceptions));
		AddPlatform(
			bindings,
			Member(
				"System.Console",
				"System.Console",
				"WriteLine",
				isInstance: false,
				Void,
				String),
			"platform:amiga-console-write-line",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ConsolePal",
				"WriteLine"),
			Effects(
				FrameworkEffects.MayAllocate |
				FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesNativeMemory,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.NativeMemory,
				FrameworkFeature.AmigaInterop,
				FrameworkFeature.AmigaConsole,
				FrameworkFeature.ManagedExceptions));
		foreach (var (memberName, parameterType, target) in new[]
		{
			("Write", Int32, "platform:amiga-console-write-int32"),
			("Write", UInt32, "platform:amiga-console-write-uint32"),
			("Write", Int64, "platform:amiga-console-write-int64"),
			("Write", UInt64, "platform:amiga-console-write-uint64"),
			("WriteLine", Int32, "platform:amiga-console-write-line-int32"),
			("WriteLine", UInt32, "platform:amiga-console-write-line-uint32"),
			("WriteLine", Int64, "platform:amiga-console-write-line-int64"),
			("WriteLine", UInt64, "platform:amiga-console-write-line-uint64")
		})
		{
			AddPlatform(
				bindings,
				Member(
					"System.Console",
					"System.Console",
					memberName,
					isInstance: false,
					Void,
					parameterType),
				target,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.AmigaPal",
					"CopperSharp.Runtime.AmigaPal.ConsolePal",
					memberName),
				Effects(
					FrameworkEffects.MayAllocate |
						FrameworkEffects.MayThrow |
						FrameworkEffects.WritesNativeMemory,
					FrameworkFeature.IntegerFormatting,
					FrameworkFeature.Numerics,
					FrameworkFeature.NativeMemory,
					FrameworkFeature.AmigaInterop,
					FrameworkFeature.AmigaConsole,
					FrameworkFeature.ManagedExceptions));
		}
		foreach (var (memberName, target) in new[]
		{
			("Write", "platform:amiga-console-write-boolean"),
			("WriteLine", "platform:amiga-console-write-line-boolean")
		})
		{
			AddPlatform(
				bindings,
				Member(
					"System.Console",
					"System.Console",
					memberName,
					isInstance: false,
					Void,
					Boolean),
				target,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.AmigaPal",
					"CopperSharp.Runtime.AmigaPal.ConsolePal",
					memberName),
				Effects(
					FrameworkEffects.MayThrow |
						FrameworkEffects.WritesNativeMemory,
					FrameworkFeature.NativeMemory,
					FrameworkFeature.AmigaInterop,
					FrameworkFeature.AmigaConsole,
					FrameworkFeature.ManagedExceptions));
		}
		foreach (var (memberName, target) in new[]
		{
			("Write", "platform:amiga-console-write-char"),
			("WriteLine", "platform:amiga-console-write-line-char")
		})
		{
			AddPlatform(
				bindings,
				Member(
					"System.Console",
					"System.Console",
					memberName,
					isInstance: false,
					Void,
					Char),
				target,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.AmigaPal",
					"CopperSharp.Runtime.AmigaPal.ConsolePal",
					memberName),
				Effects(
					FrameworkEffects.MayAllocate |
						FrameworkEffects.MayThrow |
						FrameworkEffects.WritesNativeMemory,
					FrameworkFeature.NativeMemory,
					FrameworkFeature.AmigaInterop,
					FrameworkFeature.AmigaConsole,
					FrameworkFeature.ManagedExceptions));
		}
		AddPlatform(
			bindings,
			Member(
				"System.Console",
				"System.Console",
				"Read",
				isInstance: false,
				Int32),
			"platform:amiga-console-read",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ConsolePal",
				"Read"),
			Effects(
				FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsNativeMemory |
					FrameworkEffects.WritesNativeMemory,
				FrameworkFeature.NativeMemory,
				FrameworkFeature.AmigaInterop,
				FrameworkFeature.AmigaConsole,
				FrameworkFeature.AmigaConsoleInput,
				FrameworkFeature.ManagedExceptions));
		AddPlatform(
			bindings,
			Member(
				"System.Console",
				"System.Console",
				"ReadLine",
				isInstance: false,
				String),
			"platform:amiga-console-read-line",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ConsolePal",
				"ReadLine"),
			Effects(
				FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory |
					FrameworkEffects.ReadsNativeMemory |
					FrameworkEffects.WritesNativeMemory,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.ManagedArrays,
				FrameworkFeature.ManagedGc,
				FrameworkFeature.NativeMemory,
				FrameworkFeature.AmigaInterop,
				FrameworkFeature.AmigaConsole,
				FrameworkFeature.AmigaConsoleInput,
				FrameworkFeature.ManagedExceptions));
		foreach (var (typeName, memberName, target, shadowMethod) in new[]
		{
			("System.IO.File", "Exists", "platform:amiga-file-exists", "FileExists"),
			("System.IO.Directory", "Exists", "platform:amiga-directory-exists", "DirectoryExists")
		})
		{
			AddPlatform(
				bindings,
				Member(
					"System.Runtime",
					typeName,
					memberName,
					isInstance: false,
					Boolean,
					String),
				target,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.AmigaPal",
					"CopperSharp.Runtime.AmigaPal.FileSystemPal",
					shadowMethod),
				Effects(
					FrameworkEffects.MayAllocate |
						FrameworkEffects.MayThrow |
						FrameworkEffects.ReadsManagedMemory |
						FrameworkEffects.ReadsNativeMemory |
						FrameworkEffects.WritesNativeMemory,
					FrameworkFeature.ManagedStrings,
					FrameworkFeature.NativeCStrings,
					FrameworkFeature.NativeMemory,
					FrameworkFeature.AmigaInterop,
					FrameworkFeature.AmigaFileSystem,
					FrameworkFeature.ManagedExceptions));
		}
		foreach (var (typeName, target, shadowMethod) in new[]
		{
			("System.IO.File", "platform:amiga-file-delete", "DeleteFile"),
			("System.IO.Directory", "platform:amiga-directory-delete", "DeleteDirectory")
		})
		{
			AddPlatform(
				bindings,
				Member(
					"System.Runtime",
					typeName,
					"Delete",
					isInstance: false,
					Void,
					String),
				target,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.AmigaPal",
					"CopperSharp.Runtime.AmigaPal.FileSystemPal",
					shadowMethod),
				Effects(
					FrameworkEffects.MayAllocate |
						FrameworkEffects.MayThrow |
						FrameworkEffects.ReadsManagedMemory |
						FrameworkEffects.ReadsNativeMemory |
						FrameworkEffects.WritesNativeMemory,
					FrameworkFeature.ManagedStrings,
					FrameworkFeature.NativeCStrings,
					FrameworkFeature.NativeMemory,
					FrameworkFeature.AmigaInterop,
					FrameworkFeature.AmigaFileSystem,
					FrameworkFeature.ManagedExceptions));
		}
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.IO.Directory",
				"Move",
				isInstance: false,
				Void,
				String,
				String),
			"platform:amiga-directory-move",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.FileSystemPal",
				"MoveDirectory"),
			Effects(
				FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.ReadsNativeMemory |
					FrameworkEffects.WritesNativeMemory,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.NativeCStrings,
				FrameworkFeature.NativeMemory,
				FrameworkFeature.AmigaInterop,
				FrameworkFeature.AmigaFileSystem,
				FrameworkFeature.ManagedExceptions));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.IO.File",
				"GetAttributes",
				isInstance: false,
				FileAttributes,
				String),
			"platform:amiga-file-get-attributes",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.FileSystemPal",
				"GetFileAttributes"),
			Effects(
				FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.ReadsNativeMemory |
					FrameworkEffects.WritesNativeMemory,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.NativeCStrings,
				FrameworkFeature.NativeMemory,
				FrameworkFeature.AmigaInterop,
				FrameworkFeature.AmigaFileSystem,
				FrameworkFeature.ManagedExceptions));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.IO.File",
				"SetAttributes",
				isInstance: false,
				Void,
				String,
				FileAttributes),
			"platform:amiga-file-set-attributes",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.FileSystemPal",
				"SetFileAttributes"),
			Effects(
				FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.ReadsNativeMemory |
					FrameworkEffects.WritesNativeMemory,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.NativeCStrings,
				FrameworkFeature.NativeMemory,
				FrameworkFeature.AmigaInterop,
				FrameworkFeature.AmigaFileSystem,
				FrameworkFeature.ManagedExceptions));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.Environment",
				"get_NewLine",
				isInstance: false,
				String),
			"platform:amiga-environment-new-line",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.EnvironmentPal",
				"GetNewLine"),
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.AmigaEnvironment));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.Environment",
				"get_ProcessorCount",
				isInstance: false,
				Int32),
			"platform:amiga-environment-processor-count",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.EnvironmentPal",
				"GetProcessorCount"),
			Effects(
				FrameworkEffects.None,
				FrameworkFeature.AmigaEnvironment));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.Diagnostics.Stopwatch",
				"GetTimestamp",
				isInstance: false,
				Int64),
			"platform:amiga-stopwatch-get-timestamp",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ClockPal",
				"GetTimestamp"),
			Effects(
				FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsNativeMemory |
					FrameworkEffects.WritesNativeMemory,
				FrameworkFeature.NativeMemory,
				FrameworkFeature.AmigaInterop,
				FrameworkFeature.AmigaClock,
				FrameworkFeature.ManagedExceptions));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.Diagnostics.Stopwatch",
				".ctor",
				isInstance: true,
				Void),
			"platform:amiga-stopwatch-ctor",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ShadowStopwatch",
				"Initialize"),
			Effects(
				FrameworkEffects.None,
				FrameworkFeature.ManagedObjects));
		foreach (var (memberName, targetName, shadowName) in new[]
		{
			("Start", "platform:amiga-stopwatch-start", "Start"),
			("Stop", "platform:amiga-stopwatch-stop", "Stop"),
			("Restart", "platform:amiga-stopwatch-restart", "Restart")
		})
		{
			AddPlatform(
				bindings,
				Member(
					"System.Runtime",
					"System.Diagnostics.Stopwatch",
					memberName,
					isInstance: true,
					Void),
				targetName,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.AmigaPal",
					"CopperSharp.Runtime.AmigaPal.ShadowStopwatch",
					shadowName),
				Effects(
					FrameworkEffects.MayThrow |
						FrameworkEffects.ReadsManagedMemory |
						FrameworkEffects.WritesManagedMemory |
						FrameworkEffects.ReadsNativeMemory |
						FrameworkEffects.WritesNativeMemory,
					FrameworkFeature.ManagedObjects,
					FrameworkFeature.NativeMemory,
					FrameworkFeature.AmigaInterop,
					FrameworkFeature.AmigaClock,
					FrameworkFeature.ManagedExceptions));
		}
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.Diagnostics.Stopwatch",
				"Reset",
				isInstance: true,
				Void),
			"platform:amiga-stopwatch-reset",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ShadowStopwatch",
				"Reset"),
			Effects(
				FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.ManagedObjects));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.Diagnostics.Stopwatch",
				"StartNew",
				isInstance: false,
				Stopwatch),
			"platform:amiga-stopwatch-start-new",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ShadowStopwatch",
				"StartNew"),
			Effects(
				FrameworkEffects.MayAllocate |
					FrameworkEffects.MayCollect |
					FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory |
					FrameworkEffects.ReadsNativeMemory |
					FrameworkEffects.WritesNativeMemory,
				FrameworkFeature.ManagedObjects,
				FrameworkFeature.ManagedGc,
				FrameworkFeature.NativeMemory,
				FrameworkFeature.AmigaInterop,
				FrameworkFeature.AmigaClock,
				FrameworkFeature.ManagedExceptions));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.Diagnostics.Stopwatch",
				"get_IsRunning",
				isInstance: true,
				Boolean),
			"platform:amiga-stopwatch-is-running",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ShadowStopwatch",
				"GetIsRunning"),
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.ManagedObjects));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.Diagnostics.Stopwatch",
				"get_ElapsedTicks",
				isInstance: true,
				Int64),
			"platform:amiga-stopwatch-elapsed-ticks",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ShadowStopwatch",
				"GetElapsedTicks"),
			Effects(
				FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.ReadsNativeMemory |
					FrameworkEffects.WritesNativeMemory,
				FrameworkFeature.ManagedObjects,
				FrameworkFeature.NativeMemory,
				FrameworkFeature.AmigaInterop,
				FrameworkFeature.AmigaClock,
				FrameworkFeature.ManagedExceptions));
		foreach (var (memberName, targetName, shadowName, returnType) in new[]
		{
			("get_ElapsedMilliseconds", "platform:amiga-stopwatch-elapsed-milliseconds", "GetElapsedMilliseconds", Int64),
			("get_Elapsed", "platform:amiga-stopwatch-elapsed", "GetElapsed", TimeSpan)
		})
		{
			AddPlatform(
				bindings,
				Member(
					"System.Runtime",
					"System.Diagnostics.Stopwatch",
					memberName,
					isInstance: true,
					returnType),
				targetName,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.AmigaPal",
					"CopperSharp.Runtime.AmigaPal.ShadowStopwatch",
					shadowName),
				Effects(
					FrameworkEffects.MayThrow |
						FrameworkEffects.ReadsManagedMemory |
						FrameworkEffects.ReadsNativeMemory |
						FrameworkEffects.WritesNativeMemory,
					FrameworkFeature.ManagedObjects,
					FrameworkFeature.NativeMemory,
					FrameworkFeature.AmigaInterop,
					FrameworkFeature.AmigaClock,
					FrameworkFeature.ManagedExceptions));
		}
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.Diagnostics.Stopwatch",
				"GetElapsedTime",
				isInstance: false,
				TimeSpan,
				Int64),
			"platform:amiga-stopwatch-get-elapsed-time",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ShadowStopwatch",
				"GetElapsedTime"),
			Effects(
				FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsNativeMemory |
					FrameworkEffects.WritesNativeMemory,
				FrameworkFeature.NativeMemory,
				FrameworkFeature.AmigaInterop,
				FrameworkFeature.AmigaClock,
				FrameworkFeature.ManagedExceptions));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.Diagnostics.Stopwatch",
				"GetElapsedTime",
				isInstance: false,
				TimeSpan,
				Int64,
				Int64),
			"platform:amiga-stopwatch-get-elapsed-time",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ShadowStopwatch",
				"GetElapsedTime"),
			Effects(
				FrameworkEffects.MayThrow,
				FrameworkFeature.ManagedExceptions));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.TimeSpan",
				".ctor",
				isInstance: true,
				Void,
				Int64),
			"platform:amiga-timespan-ctor",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ShadowTimeSpan",
				"Initialize"),
			Effects(FrameworkEffects.WritesManagedMemory));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.TimeSpan",
				"FromTicks",
				isInstance: false,
				TimeSpan,
				Int64),
			"platform:amiga-timespan-from-ticks",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ShadowTimeSpan",
				"FromTicks"),
			Effects(
				FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.NativeMemory));
		AddPlatform(
			bindings,
			Member(
				"System.Runtime",
				"System.TimeSpan",
				"get_Ticks",
				isInstance: true,
				Int64),
			"platform:amiga-timespan-ticks",
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.AmigaPal",
				"CopperSharp.Runtime.AmigaPal.ShadowTimeSpan",
				"GetTicks"),
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.NativeMemory));
		foreach (var (memberName, targetName, shadowName) in new[]
		{
			("get_Days", "platform:amiga-timespan-days", "GetDays"),
			("get_Hours", "platform:amiga-timespan-hours", "GetHours"),
			("get_Minutes", "platform:amiga-timespan-minutes", "GetMinutes"),
			("get_Seconds", "platform:amiga-timespan-seconds", "GetSeconds"),
			("get_Milliseconds", "platform:amiga-timespan-milliseconds", "GetMilliseconds")
		})
		{
			AddPlatform(
				bindings,
				Member(
					"System.Runtime",
					"System.TimeSpan",
					memberName,
					isInstance: true,
					Int32),
				targetName,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.AmigaPal",
					"CopperSharp.Runtime.AmigaPal.ShadowTimeSpan",
					shadowName),
				Effects(FrameworkEffects.ReadsManagedMemory));
		}
		foreach (var (memberName, targetName, shadowName) in new[]
		{
			("get_TotalDays", "platform:amiga-timespan-total-days", "GetTotalDays"),
			("get_TotalHours", "platform:amiga-timespan-total-hours", "GetTotalHours"),
			("get_TotalMinutes", "platform:amiga-timespan-total-minutes", "GetTotalMinutes"),
			("get_TotalSeconds", "platform:amiga-timespan-total-seconds", "GetTotalSeconds"),
			("get_TotalMilliseconds", "platform:amiga-timespan-total-milliseconds", "GetTotalMilliseconds")
		})
		{
			AddPlatform(
				bindings,
				Member(
					"System.Runtime",
					"System.TimeSpan",
					memberName,
					isInstance: true,
					Double),
				targetName,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.AmigaPal",
					"CopperSharp.Runtime.AmigaPal.ShadowTimeSpan",
					shadowName),
				Effects(FrameworkEffects.ReadsManagedMemory));
		}
		foreach (var (memberName, targetName, shadowName) in new[]
		{
			("op_Equality", "platform:amiga-timespan-equality", "Equal"),
			("op_Inequality", "platform:amiga-timespan-inequality", "NotEqual"),
			("op_LessThan", "platform:amiga-timespan-less-than", "LessThan"),
			("op_LessThanOrEqual", "platform:amiga-timespan-less-than-or-equal", "LessThanOrEqual"),
			("op_GreaterThan", "platform:amiga-timespan-greater-than", "GreaterThan"),
			("op_GreaterThanOrEqual", "platform:amiga-timespan-greater-than-or-equal", "GreaterThanOrEqual")
		})
		{
			AddPlatform(
				bindings,
				Member(
					"System.Runtime",
					"System.TimeSpan",
					memberName,
					isInstance: false,
					Boolean,
					TimeSpan,
					TimeSpan),
				targetName,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.AmigaPal",
					"CopperSharp.Runtime.AmigaPal.ShadowTimeSpan",
					shadowName),
				Effects(
					FrameworkEffects.ReadsManagedMemory,
					FrameworkFeature.NativeMemory));
		}
		AddManagedBody(
			bindings,
			Member(
				"System.Runtime",
				"System.IDisposable",
				"Dispose",
				isInstance: true,
				Void),
			"managed:closed-world-sealed-interface-dispatch",
			Effects(
				FrameworkEffects.MayThrow,
				FrameworkFeature.ManagedObjects));
		AddShadow(
			bindings,
			Member(
				"System.Runtime",
				"System.Object",
				"Equals",
				isInstance: true,
				Boolean,
				Object),
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowObject",
				"Equals"),
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.ManagedObjects),
			preservesVirtualDispatch: true);
		AddShadow(
			bindings,
			Member(
				"System.Runtime",
				"System.Object",
				"Equals",
				isInstance: false,
				Boolean,
				Object,
				Object),
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowObject",
				"EqualsObjects"),
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.ManagedObjects));
		AddShadow(
			bindings,
			Member(
				"System.Runtime",
				"System.Object",
				"GetHashCode",
				isInstance: true,
				Int32),
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowObject",
				"GetHashCode"),
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.ManagedObjects),
			preservesVirtualDispatch: true);
		Add(
			bindings,
			Member("System.Runtime", "System.Object", ".ctor", isInstance: true, Void),
			"intrinsic:object-ctor",
			FrameworkEffectSummary.None);
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.Delegate",
				"Equals",
				isInstance: true,
				Boolean,
				Object),
			"intrinsic:delegate-equality",
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.Delegates));
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.Object",
				"ReferenceEquals",
				isInstance: false,
				Boolean,
				Object,
				Object),
			"intrinsic:object-reference-equals",
			Effects(
				FrameworkEffects.None,
				FrameworkFeature.ManagedObjects));
		Add(
			bindings,
			Member("System.Runtime", "System.Exception", ".ctor", isInstance: true, Void),
			"intrinsic:object-ctor",
			FrameworkEffectSummary.None);
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.Delegate",
				"op_Equality",
				isInstance: false,
				Boolean,
				Named("System.Runtime", "System.Delegate"),
				Named("System.Runtime", "System.Delegate")),
			"intrinsic:delegate-equality",
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.Delegates));
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.Delegate",
				"Combine",
				isInstance: false,
				Named("System.Runtime", "System.Delegate"),
				Named("System.Runtime", "System.Delegate"),
				Named("System.Runtime", "System.Delegate")),
			"intrinsic:delegate-combine",
			Effects(
				FrameworkEffects.MayAllocate |
				FrameworkEffects.MayThrow |
				FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.Delegates,
				FrameworkFeature.ManagedGc));
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.Delegate",
				"Remove",
				isInstance: false,
				Named("System.Runtime", "System.Delegate"),
				Named("System.Runtime", "System.Delegate"),
				Named("System.Runtime", "System.Delegate")),
			"intrinsic:delegate-remove",
			Effects(
				FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.Delegates,
				FrameworkFeature.ManagedGc));
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.Delegate",
				"op_Inequality",
				isInstance: false,
				Boolean,
				Named("System.Runtime", "System.Delegate"),
				Named("System.Runtime", "System.Delegate")),
			"intrinsic:delegate-inequality",
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.Delegates));
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.Runtime.CompilerServices.RuntimeHelpers",
				"InitializeArray",
				isInstance: false,
				Void,
				Named("System.Runtime", "System.Array"),
				Named("System.Runtime", "System.RuntimeFieldHandle")),
			"intrinsic:initialize-array",
			Effects(
				FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.ManagedArrays));
		AddShadow(
			bindings,
			Member(
				"System.Runtime",
				"System.String",
				"Format",
				isInstance: false,
				String,
				String,
				FrameworkTypeId.SzArray(Object)),
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowStringFormat",
				"FormatParams"),
			Effects(
				FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.IntegerFormatting,
				FrameworkFeature.ManagedArrays,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.StringInterpolation));
		foreach (var formatArguments in new[]
		{
			new[] { Object },
			new[] { Object, Object },
			new[] { Object, Object, Object }
		})
		{
			AddShadow(
				bindings,
				Member(
					"System.Runtime",
					"System.String",
					"Format",
					isInstance: false,
					String,
					[String, .. formatArguments]),
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.Managed",
					"CopperSharp.Runtime.ShadowStringFormat",
					"FormatArguments"),
				Effects(
					FrameworkEffects.MayAllocate |
						FrameworkEffects.MayThrow |
						FrameworkEffects.MayCollect |
						FrameworkEffects.ReadsManagedMemory |
						FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.IntegerFormatting,
					FrameworkFeature.ManagedStrings,
					FrameworkFeature.StringInterpolation));
		}
		AddShadow(
			bindings,
			Member(
				"System.Runtime",
				"System.String",
				"Format",
				isInstance: false,
				String,
				String,
				ReadOnlySpanOfObject),
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowStringFormat",
				"FormatSpanParams"),
			Effects(
				FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.IntegerFormatting,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.Spans,
				FrameworkFeature.StringInterpolation));
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.String",
				"Concat",
				isInstance: false,
				String,
				String,
				String),
			"intrinsic:string-concat-two",
			Effects(
				FrameworkEffects.MayAllocate |
				FrameworkEffects.MayThrow |
				FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.ManagedStrings));
		foreach (var parameters in new[]
		{
			new[] { Int32 },
			new[] { Int32, Int32 }
		})
		{
			Add(
				bindings,
				Member(
					"System.Runtime",
					"System.String",
					"Substring",
					isInstance: true,
					String,
					parameters),
				"intrinsic:string-substring",
				Effects(
					FrameworkEffects.MayAllocate |
						FrameworkEffects.MayThrow |
						FrameworkEffects.MayCollect |
						FrameworkEffects.ReadsManagedMemory |
						FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.ManagedStrings));
		}
		var charArray = FrameworkTypeId.SzArray(Char);
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.String",
				"CopyTo",
				isInstance: true,
				Void,
				Int32,
				charArray,
				Int32,
				Int32),
			"intrinsic:string-copy-to-char-array",
			Effects(
				FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.ManagedArrays));
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.String",
				"CopyTo",
				isInstance: true,
				Void,
				SpanOfChar),
			"intrinsic:string-copy-to-span-char",
			Effects(
				FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.Spans));
		foreach (var parameters in new[]
		{
			Array.Empty<FrameworkTypeId>(),
			new[] { Int32, Int32 }
		})
		{
			Add(
				bindings,
				Member(
					"System.Runtime",
					"System.String",
					"ToCharArray",
					isInstance: true,
					charArray,
					parameters),
				"intrinsic:string-to-char-array",
				Effects(
					FrameworkEffects.MayAllocate |
						FrameworkEffects.MayThrow |
						FrameworkEffects.MayCollect |
						FrameworkEffects.ReadsManagedMemory |
						FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.ManagedStrings,
					FrameworkFeature.ManagedArrays));
		}
		foreach (var (name, returnType, target) in new[]
		{
			("StartsWith", Boolean, "intrinsic:string-starts-with-ordinal"),
			("EndsWith", Boolean, "intrinsic:string-ends-with-ordinal"),
			("Contains", Boolean, "intrinsic:string-contains-ordinal"),
			("IndexOf", Int32, "intrinsic:string-index-of-ordinal")
		})
		{
			Add(
				bindings,
				Member(
					"System.Runtime",
					"System.String",
					name,
					isInstance: true,
					returnType,
					String,
					FrameworkStringComparison),
				target,
				Effects(
					FrameworkEffects.MayThrow |
						FrameworkEffects.ReadsManagedMemory,
					FrameworkFeature.ManagedStrings));
		}
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.String",
				"Contains",
				isInstance: true,
				Boolean,
				String),
			"intrinsic:string-contains-ordinal",
			Effects(
				FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.ManagedStrings));
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.String",
				"get_Chars",
				isInstance: true,
				Char,
				Int32),
			"intrinsic:string-char",
			Effects(
				FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.ManagedStrings));
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.String",
				"get_Length",
				isInstance: true,
				Int32),
			"intrinsic:string-length",
			Effects(
				FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.ManagedStrings));
		foreach (var (name, target) in new[]
		{
			("Equals", "intrinsic:string-equality"),
			("op_Equality", "intrinsic:string-equality"),
			("op_Inequality", "intrinsic:string-inequality")
		})
		{
			Add(
				bindings,
				Member(
					"System.Runtime",
					"System.String",
					name,
					isInstance: false,
					Boolean,
					String,
					String),
				target,
				Effects(
					FrameworkEffects.ReadsManagedMemory,
					FrameworkFeature.ManagedStrings));
		}
		Add(
			bindings,
			Member(
				"System.Runtime",
				"System.String",
				"op_Implicit",
				isInstance: false,
				ReadOnlySpanOfChar,
				String),
			"intrinsic:readonly-span-from-string",
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.Spans));
		Add(
			bindings,
			Member(
				"System.Memory",
				"System.MemoryExtensions",
				"AsSpan",
				isInstance: false,
				ReadOnlySpanOfChar,
				String),
			"intrinsic:readonly-span-from-string",
			Effects(
				FrameworkEffects.ReadsManagedMemory,
				FrameworkFeature.ManagedStrings,
				FrameworkFeature.Spans));
		var mathShadow = new FrameworkShadowMethod(
			"CopperSharp.Runtime.Managed",
			"CopperSharp.Runtime.ShadowMath",
			string.Empty);
		foreach (var type in new[] { SByte, Int16, Int32, Int64, IntPtr })
		{
			AddShadow(
				bindings,
				Member("System.Runtime", "System.Math", "Abs", false, type, type),
				mathShadow with { MethodName = "Abs" },
				Effects(
					FrameworkEffects.MayThrow,
					FrameworkFeature.ManagedExceptions,
					FrameworkFeature.Numerics));
			AddShadow(
				bindings,
				Member("System.Runtime", "System.Math", "Sign", false, Int32, type),
				mathShadow with { MethodName = "Sign" },
				Effects(FrameworkEffects.None, FrameworkFeature.Numerics));
		}
		foreach (var type in new[]
		{
			Byte, SByte, Int16, UInt16, Int32, UInt32, Int64, UInt64, IntPtr, UIntPtr
		})
		{
			foreach (var name in new[] { "Min", "Max" })
			{
				AddShadow(
					bindings,
					Member("System.Runtime", "System.Math", name, false, type, type, type),
					mathShadow with { MethodName = name },
					Effects(FrameworkEffects.None, FrameworkFeature.Numerics));
			}
			AddShadow(
				bindings,
				Member("System.Runtime", "System.Math", "Clamp", false, type, type, type, type),
				mathShadow with { MethodName = "Clamp" },
				Effects(
					FrameworkEffects.MayThrow,
					FrameworkFeature.ManagedExceptions,
					FrameworkFeature.Numerics));
		}
		AddShadow(
			bindings,
			Member("System.Runtime", "System.Math", "BigMul", false, Int64, Int32, Int32),
			mathShadow with { MethodName = "BigMul" },
			Effects(FrameworkEffects.None, FrameworkFeature.Numerics));
		AddShadow(
			bindings,
			Member("System.Runtime", "System.Math", "BigMul", false, UInt64, UInt32, UInt32),
			mathShadow with { MethodName = "BigMul" },
			Effects(FrameworkEffects.None, FrameworkFeature.Numerics));
		foreach (var type in new[] { Single, Double })
		{
			foreach (var name in new[] { "Abs", "Min", "Max", "Clamp", "Sign" })
			{
				var parameters = name switch
				{
					"Abs" or "Sign" => new[] { type },
					"Min" or "Max" => new[] { type, type },
					_ => new[] { type, type, type }
				};
				AddShadow(
					bindings,
					Member(
						"System.Runtime",
						"System.Math",
						name,
						false,
						name == "Sign" ? Int32 : type,
						parameters),
					mathShadow with { MethodName = name },
					name is "Clamp" or "Sign"
						? Effects(
							FrameworkEffects.MayThrow,
							FrameworkFeature.ManagedExceptions,
							FrameworkFeature.Numerics)
						: Effects(FrameworkEffects.None, FrameworkFeature.Numerics));
			}
		}
		AddShadow(
			bindings,
			Member("System.Runtime", "System.Math", "CopySign", false, Double, Double, Double),
			mathShadow with { MethodName = "CopySign" },
			Effects(FrameworkEffects.None, FrameworkFeature.Numerics));
		foreach (var name in new[] { "Floor", "Ceiling", "Truncate", "Sqrt" })
		{
			AddShadow(
				bindings,
				Member("System.Runtime", "System.Math", name, false, Double, Double),
				mathShadow with { MethodName = name },
				Effects(FrameworkEffects.None, FrameworkFeature.Numerics));
		}
		AddShadow(
			bindings,
			Member("System.Runtime", "System.Math", "Round", false, Double, Double),
			mathShadow with { MethodName = "Round" },
			Effects(FrameworkEffects.None, FrameworkFeature.Numerics));
		AddShadow(
			bindings,
			Member("System.Runtime", "System.Math", "Round", false, Double, Double, MidpointRounding),
			mathShadow with { MethodName = "Round" },
			Effects(
				FrameworkEffects.MayThrow,
				FrameworkFeature.ManagedExceptions,
				FrameworkFeature.Numerics));
		foreach (var (typeName, type, shadowType) in new[]
		{
			("System.Double", Double, "CopperSharp.Runtime.ShadowDouble"),
			("System.Single", Single, "CopperSharp.Runtime.ShadowSingle")
		})
		{
			foreach (var name in new[]
			{
				"IsFinite", "IsInfinity", "IsNaN", "IsNegative",
				"IsNegativeInfinity", "IsPositiveInfinity", "IsNormal", "IsSubnormal"
			})
			{
				AddShadow(
					bindings,
					Member("System.Runtime", typeName, name, false, Boolean, type),
					new FrameworkShadowMethod(
						"CopperSharp.Runtime.Managed",
						shadowType,
						name),
					Effects(FrameworkEffects.None, FrameworkFeature.Numerics));
			}
		}
		foreach (var (typeName, shadowType) in new[]
		{
			("System.Int32", "CopperSharp.Runtime.ShadowInt32"),
			("System.UInt32", "CopperSharp.Runtime.ShadowUInt32"),
			("System.Int64", "CopperSharp.Runtime.ShadowInt64"),
			("System.UInt64", "CopperSharp.Runtime.ShadowUInt64")
		})
		{
			AddShadow(
				bindings,
				Member(
					"System.Runtime",
					typeName,
					"ToString",
					isInstance: true,
					String),
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.Managed",
					shadowType,
					"ToString"),
				Effects(
					FrameworkEffects.MayAllocate |
						FrameworkEffects.MayThrow |
						FrameworkEffects.MayCollect |
						FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.IntegerFormatting,
					FrameworkFeature.ManagedStrings,
					FrameworkFeature.Numerics));
		}
		foreach (var (typeName, shadowType) in new[]
		{
			("System.Int32", "CopperSharp.Runtime.ShadowInt32"),
			("System.UInt32", "CopperSharp.Runtime.ShadowUInt32")
		})
		{
			AddShadow(
				bindings,
				Member(
					"System.Runtime",
					typeName,
					"ToString",
					isInstance: true,
					String,
					String),
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.Managed",
					shadowType,
					"ToString"),
				Effects(
					FrameworkEffects.MayAllocate |
						FrameworkEffects.MayThrow |
						FrameworkEffects.MayCollect |
						FrameworkEffects.ReadsManagedMemory |
						FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.IntegerFormatting,
					FrameworkFeature.ManagedExceptions,
					FrameworkFeature.ManagedStrings,
					FrameworkFeature.Numerics));
		}
		AddShadow(
			bindings,
			Member(
				"System.Runtime",
				"System.BitConverter",
				"GetBytes",
				isInstance: false,
				FrameworkTypeId.SzArray(
					FrameworkTypeId.Primitive("System.Byte")),
				Int32),
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowBitConverter",
				"GetBytes"),
			Effects(
				FrameworkEffects.MayAllocate |
				FrameworkEffects.MayThrow |
				FrameworkEffects.MayCollect |
				FrameworkEffects.WritesManagedMemory,
				FrameworkFeature.BinaryPrimitives,
				FrameworkFeature.ManagedArrays));
		return bindings;
	}

	private static FrameworkBinding? TryBindNullable(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (!context.IsSupportedNullableType ||
			context.ConstructedDeclaringType?.NullableElementType is not { } element ||
			member.DeclaringType.Kind != FrameworkTypeKind.GenericInstantiation ||
			!member.DeclaringType.ElementType!.Equals(NullableDefinition) ||
			member.DeclaringType.GenericArguments.Length != 1 ||
			member.MethodTypeArguments.Length != 0)
		{
			return null;
		}

		var target = member.Name switch
		{
			".ctor" when SignatureEquals(member, Void, GenericType0) =>
				$"intrinsic:nullable-ctor:{element.DisplayName}",
			"get_HasValue" when SignatureEquals(member, Boolean) =>
				$"intrinsic:nullable-has-value:{element.DisplayName}",
			"get_Value" when SignatureEquals(member, GenericType0) =>
				$"intrinsic:nullable-get-value:{element.DisplayName}",
			"GetValueOrDefault" when SignatureEquals(member, GenericType0) =>
				$"intrinsic:nullable-get-value-or-default-no-argument:{element.DisplayName}",
			"GetValueOrDefault" when SignatureEquals(member, GenericType0, GenericType0) =>
				$"intrinsic:nullable-get-value-or-default:{element.DisplayName}",
			_ => null
		};
		if (target is null)
		{
			return null;
		}

		var effects = member.Name == "get_Value"
			? FrameworkEffects.MayThrow
			: FrameworkEffects.None;
		return Intrinsic(
			member,
			target,
			Effects(effects, FrameworkFeature.NullableValues));
	}

	private static FrameworkBinding? TryBindList(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (member.DeclaringType.Kind != FrameworkTypeKind.GenericInstantiation ||
			!member.DeclaringType.ElementType!.Equals(ListDefinition) ||
			member.DeclaringType.GenericArguments.Length != 1 ||
			member.MethodTypeArguments.Length != 0 ||
			context.ConstructedDeclaringType is not
			{
				GenericArguments: [var element]
			} ||
			(!element.IsSupportedScalar &&
			 !IsSupportedNullableListEqualityElement(element)) ||
			element.Kind is CilTypeKind.ManagedPointer or CilTypeKind.GenericParameter)
		{
			return null;
		}

		var shadowName = member.Name switch
		{
			".ctor" when SignatureEquals(member, Void) => ".ctor",
			".ctor" when SignatureEquals(member, Void, Int32) => ".ctor",
			"Add" when SignatureEquals(member, Void, GenericType0) => "Add",
			"Clear" when SignatureEquals(member, Void) => "Clear",
			"Contains" when IsSupportedListEqualityElement(element, context) &&
				SignatureEquals(member, Boolean, GenericType0) => "Contains",
			"get_Capacity" when SignatureEquals(member, Int32) => "get_Capacity",
			"get_Count" when SignatureEquals(member, Int32) => "get_Count",
			"get_Item" when SignatureEquals(member, GenericType0, Int32) => "get_Item",
			"IndexOf" when IsSupportedListEqualityElement(element, context) &&
				SignatureEquals(member, Int32, GenericType0) => "IndexOf",
			"Remove" when IsSupportedListEqualityElement(element, context) &&
				SignatureEquals(member, Boolean, GenericType0) => "Remove",
			"RemoveAt" when SignatureEquals(member, Void, Int32) => "RemoveAt",
			"set_Capacity" when SignatureEquals(member, Void, Int32) => "set_Capacity",
			"set_Item" when SignatureEquals(member, Void, Int32, GenericType0) => "set_Item",
			"GetEnumerator" when SignatureEquals(
				member,
				FrameworkTypeId.GenericInstantiation(
					ListEnumeratorDefinition,
					[GenericType0])) => "GetEnumerator",
			"ToArray" when SignatureEquals(
				member,
				FrameworkTypeId.SzArray(GenericType0)) => "ToArray",
			_ => null
		};
		if (shadowName is null)
		{
			return null;
		}

		var effects = member.Name switch
		{
			".ctor" when member.Signature.ParameterTypes.Length == 0 =>
				FrameworkEffects.WritesManagedMemory,
			".ctor" => FrameworkEffects.MayAllocate |
				FrameworkEffects.MayThrow |
				FrameworkEffects.MayCollect |
				FrameworkEffects.WritesManagedMemory,
			"Add" => FrameworkEffects.MayAllocate |
				FrameworkEffects.MayThrow |
				FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory,
			"Clear" => FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory,
			"get_Capacity" or "get_Count" =>
				FrameworkEffects.ReadsManagedMemory,
			"Contains" or "IndexOf" => FrameworkEffects.ReadsManagedMemory,
			"Remove" => FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory,
			"GetEnumerator" => FrameworkEffects.ReadsManagedMemory,
			"get_Item" => FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory,
			"set_Capacity" or "ToArray" => FrameworkEffects.MayAllocate |
				FrameworkEffects.MayThrow |
				FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory,
			_ => FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory
		};
		return Shadow(
			member,
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowList`1",
				shadowName),
			Effects(
				effects,
				FrameworkFeature.ManagedCollections,
				FrameworkFeature.ManagedArrays,
				FrameworkFeature.ManagedGc));
	}

	private static FrameworkBinding? TryBindDictionary(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (member.DeclaringType.Kind != FrameworkTypeKind.GenericInstantiation ||
			!member.DeclaringType.ElementType!.Equals(DictionaryDefinition) ||
			member.DeclaringType.GenericArguments.Length != 2 ||
			member.MethodTypeArguments.Length != 0 ||
			context.ConstructedDeclaringType is not
			{
				GenericArguments: [var key, var value]
			} ||
			!IsSupportedDictionaryKey(key) ||
			!IsSupportedDictionaryValue(value, context))
		{
			return null;
		}

		var shadowName = member.Name switch
		{
			".ctor" when SignatureEquals(member, Void) => ".ctor",
			"Add" when SignatureEquals(member, Void, GenericType0, GenericType1) => "Add",
			"get_Count" when SignatureEquals(member, Int32) => "get_Count",
			"get_Values" when SignatureEquals(
				member,
				FrameworkTypeId.GenericInstantiation(
					DictionaryValueCollectionDefinition,
					[GenericType0, GenericType1])) => "get_Values",
			"get_Item" when SignatureEquals(member, GenericType1, GenericType0) => "get_Item",
			"set_Item" when SignatureEquals(
				member,
				Void,
				GenericType0,
				GenericType1) => "set_Item",
			"TryGetValue" when SignatureEquals(
				member,
				Boolean,
				GenericType0,
				FrameworkTypeId.ByReference(GenericType1)) => "TryGetValue",
			_ => null
		};
		if (shadowName is null)
		{
			return null;
		}

		var effects = member.Name switch
		{
			".ctor" => FrameworkEffects.WritesManagedMemory,
			"get_Count" => FrameworkEffects.ReadsManagedMemory,
			"get_Values" => FrameworkEffects.MayAllocate |
				FrameworkEffects.MayThrow |
				FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory,
			"TryGetValue" => FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory,
			"get_Item" => FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory,
			_ => FrameworkEffects.MayAllocate |
				FrameworkEffects.MayThrow |
				FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory
		};
		return Shadow(
			member,
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowDictionary`2",
				shadowName),
			Effects(
				effects,
				FrameworkFeature.ManagedCollections,
				FrameworkFeature.ManagedArrays,
				FrameworkFeature.ManagedGc));
	}

	private static bool IsSupportedDictionaryKey(CilType key) =>
		(IsIntegralListEqualityElement(key) || IsStringListEqualityElement(key)) &&
		key.Kind is not (CilTypeKind.ManagedPointer or CilTypeKind.GenericParameter);

	private static bool IsSupportedDictionaryValue(
		CilType value,
		FrameworkBindingContext context) =>
		((IsIntegralListEqualityElement(value) || IsStringListEqualityElement(value)) &&
		 value.Kind is not (CilTypeKind.ManagedPointer or CilTypeKind.GenericParameter)) ||
		(value.Kind == CilTypeKind.ValueType &&
		 context.DeclaringTypeArgumentContainsReferences is [_, false]);

	private static bool IsIntegralListEqualityElement(CilType element) =>
		element.Kind is
			CilTypeKind.Boolean or
			CilTypeKind.Character or
			CilTypeKind.SignedInteger or
			CilTypeKind.UnsignedInteger or
			CilTypeKind.NativeInteger;

	private static bool IsStringListEqualityElement(CilType element) =>
		element.Kind == CilTypeKind.ManagedReference &&
		element.DisplayName == "string";

	private static bool IsFloatingPointListEqualityElement(CilType element) =>
		element.IsFloatingPoint && element.Size is 4 or 8;

	private static bool IsSupportedPublicEqualityComparerElement(
		CilType element,
		FrameworkBindingContext context) =>
		IsIntegralListEqualityElement(element) ||
		IsFloatingPointListEqualityElement(element) ||
		IsStringListEqualityElement(element) ||
		IsSupportedNullableListEqualityElement(element) ||
		context.DefaultEqualityKind is
			FrameworkDefaultEqualityKind.SealedObjectEquals or
			FrameworkDefaultEqualityKind.SealedIEquatable;

	private static bool IsSupportedNullableListEqualityElement(CilType element) =>
		element.NullableElementType is { } nullableElement &&
		IsIntegralListEqualityElement(nullableElement) &&
		nullableElement.Size == 4;

	private static bool IsSupportedListEqualityElement(
		CilType element,
		FrameworkBindingContext context) =>
		IsIntegralListEqualityElement(element) ||
		IsFloatingPointListEqualityElement(element) ||
		IsStringListEqualityElement(element) ||
		IsSupportedNullableListEqualityElement(element) ||
		context.DefaultEqualityKind is
			FrameworkDefaultEqualityKind.SealedObjectEquals or
			FrameworkDefaultEqualityKind.SealedIEquatable;

	private static FrameworkBinding? TryBindListEnumerator(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (member.DeclaringType.Kind != FrameworkTypeKind.GenericInstantiation ||
			!member.DeclaringType.ElementType!.Equals(ListEnumeratorDefinition) ||
			member.DeclaringType.GenericArguments.Length != 1 ||
			member.MethodTypeArguments.Length != 0 ||
			context.ConstructedDeclaringType is not
			{
				GenericArguments: [var element]
			} ||
			!element.IsSupportedScalar ||
			element.Kind is CilTypeKind.ManagedPointer or CilTypeKind.GenericParameter)
		{
			return null;
		}

		var shadowName = member.Name switch
		{
			"MoveNext" when SignatureEquals(member, Boolean) => "MoveNext",
			"get_Current" when SignatureEquals(member, GenericType0) => "get_Current",
			"Dispose" when SignatureEquals(member, Void) => "Dispose",
			_ => null
		};
		if (shadowName is null)
		{
			return null;
		}

		var effects = member.Name == "MoveNext"
			? FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory
			: member.Name == "get_Current"
				? FrameworkEffects.ReadsManagedMemory
				: FrameworkEffects.None;
		return Shadow(
			member,
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowListEnumerator`1",
				shadowName),
			Effects(
				effects,
				FrameworkFeature.ManagedCollections,
				FrameworkFeature.ManagedArrays,
				FrameworkFeature.ManagedGc));
	}

	private static FrameworkBinding? TryBindSpan(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (member.DeclaringType.Kind != FrameworkTypeKind.GenericInstantiation ||
			!member.DeclaringType.ElementType!.Equals(SpanDefinition) ||
			member.DeclaringType.GenericArguments.Length != 1 ||
			member.MethodTypeArguments.Length != 0 ||
			context.ConstructedDeclaringType is not
			{
				GenericArguments: [var element]
			})
		{
			return null;
		}

		var signature = context.CilSignature;
		var target = member.Name switch
		{
			".ctor" when
				signature.Header.IsInstance &&
				signature.ParameterTypes is
				[
					{ Kind: CilTypeKind.UnmanagedPointer },
					{ DisplayName: "int" }
				] =>
					$"intrinsic:span-from-pointer:{element.DisplayName}",
			".ctor" when
				signature.Header.IsInstance &&
				signature.ParameterTypes is
				[
					{
						Kind: CilTypeKind.ManagedPointer,
						ElementType: { } referencedElement
					}
				] &&
				referencedElement.Equals(element) =>
					$"intrinsic:span-from-ref:{element.DisplayName}",
			"op_Implicit" when
				!signature.Header.IsInstance &&
				signature.ParameterTypes is
				[
					{
						Kind: CilTypeKind.ManagedReference,
						ElementType: { } arrayElement
					}
				] &&
				arrayElement.Equals(element) =>
					$"intrinsic:span-from-array:{element.DisplayName}",
			"op_Implicit" when
				!signature.Header.IsInstance &&
				signature.ParameterTypes is
				[
					{
						Kind: CilTypeKind.ValueType,
						GenericArguments: [var sourceElement]
					} source
				] &&
				source.DisplayName.StartsWith(
					"System.Span`1<",
					StringComparison.Ordinal) &&
				sourceElement.Equals(element) &&
				signature.ReturnType.DisplayName.StartsWith(
					"System.ReadOnlySpan`1<",
					StringComparison.Ordinal) =>
					$"intrinsic:readonly-span-from-span:{element.DisplayName}",
			"get_Length" when
				signature.Header.IsInstance &&
				signature.ParameterTypes.Length == 0 &&
				signature.ReturnType.DisplayName == "int" =>
					$"intrinsic:span-length:{element.DisplayName}",
			"get_IsEmpty" when
				signature.Header.IsInstance &&
				signature.ParameterTypes.Length == 0 &&
				signature.ReturnType.DisplayName == "bool" =>
					$"intrinsic:span-is-empty:{element.DisplayName}",
			"Slice" when
				signature.Header.IsInstance &&
				signature.ParameterTypes is [{ DisplayName: "int" }] &&
				signature.ReturnType.DisplayName ==
					context.ConstructedDeclaringType.DisplayName =>
					$"intrinsic:span-slice-start:{element.DisplayName}",
			"Slice" when
				signature.Header.IsInstance &&
				signature.ParameterTypes is
					[{ DisplayName: "int" }, { DisplayName: "int" }] &&
				signature.ReturnType.DisplayName ==
					context.ConstructedDeclaringType.DisplayName =>
					$"intrinsic:span-slice-range:{element.DisplayName}",
			"get_Item" when
				signature.Header.IsInstance &&
				signature.ParameterTypes is [{ DisplayName: "int" }] &&
				signature.ReturnType.Kind == CilTypeKind.ManagedPointer &&
				signature.ReturnType.ElementType?.Equals(element) == true =>
					$"intrinsic:span-get-item:{element.DisplayName}",
			"CopyTo" when
				signature.Header.IsInstance &&
				signature.ParameterTypes is
				[
					{
						Kind: CilTypeKind.ValueType,
						GenericArguments: [var destinationElement]
					} destination
				] &&
				destination.DisplayName.StartsWith(
					"System.Span`1<",
					StringComparison.Ordinal) &&
				destinationElement.Equals(element) &&
				signature.ReturnType.DisplayName == "void" =>
					$"intrinsic:span-copy-to:{element.DisplayName}",
			_ => null
		};
		if (target is null)
		{
			return null;
		}

		var effects = member.Name == "CopyTo"
			? FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory
			: member.Name is "get_Item" or "Slice"
			? FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory
			: target.StartsWith("intrinsic:span-from-pointer:", StringComparison.Ordinal)
				? FrameworkEffects.MayThrow
				: member.Name == ".ctor"
					? FrameworkEffects.None
					: FrameworkEffects.ReadsManagedMemory;
		return Intrinsic(
			member,
			target,
			member.Name is ".ctor" or "CopyTo"
				? Effects(effects, FrameworkFeature.Spans)
				: Effects(effects, FrameworkFeature.Spans, FrameworkFeature.ManagedArrays));
	}

	private static FrameworkBinding? TryBindReadOnlySpan(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (member.DeclaringType.Kind != FrameworkTypeKind.GenericInstantiation ||
			!member.DeclaringType.ElementType!.Equals(ReadOnlySpanDefinition) ||
			member.DeclaringType.GenericArguments.Length != 1 ||
			member.MethodTypeArguments.Length != 0 ||
			context.ConstructedDeclaringType is not
			{
				GenericArguments: [var element]
			})
		{
			return null;
		}

		var signature = context.CilSignature;
		var target = member.Name switch
		{
			".ctor" when
				signature.Header.IsInstance &&
				signature.ParameterTypes is
				[
					{
						Kind: CilTypeKind.ManagedPointer,
						ElementType: { } referencedElement
					}
				] &&
				referencedElement.Equals(element) =>
					$"intrinsic:readonly-span-from-ref:{element.DisplayName}",
			"op_Implicit" when
				!signature.Header.IsInstance &&
				signature.ParameterTypes is
				[
					{
						Kind: CilTypeKind.ManagedReference,
						ElementType: { } arrayElement
					}
				] &&
				arrayElement.Equals(element) =>
					$"intrinsic:readonly-span-from-array:{element.DisplayName}",
			"get_Length" when
				signature.Header.IsInstance &&
				signature.ParameterTypes.Length == 0 &&
				signature.ReturnType.DisplayName == "int" =>
					$"intrinsic:readonly-span-length:{element.DisplayName}",
			"get_IsEmpty" when
				signature.Header.IsInstance &&
				signature.ParameterTypes.Length == 0 &&
				signature.ReturnType.DisplayName == "bool" =>
					$"intrinsic:readonly-span-is-empty:{element.DisplayName}",
			"Slice" when
				signature.Header.IsInstance &&
				signature.ParameterTypes is [{ DisplayName: "int" }] &&
				signature.ReturnType.DisplayName ==
					context.ConstructedDeclaringType.DisplayName =>
					$"intrinsic:readonly-span-slice-start:{element.DisplayName}",
			"Slice" when
				signature.Header.IsInstance &&
				signature.ParameterTypes is
					[{ DisplayName: "int" }, { DisplayName: "int" }] &&
				signature.ReturnType.DisplayName ==
					context.ConstructedDeclaringType.DisplayName =>
					$"intrinsic:readonly-span-slice-range:{element.DisplayName}",
			"get_Item" when
				signature.Header.IsInstance &&
				signature.ParameterTypes is [{ DisplayName: "int" }] &&
				signature.ReturnType.Kind == CilTypeKind.ManagedPointer &&
				signature.ReturnType.ElementType?.Equals(element) == true =>
					$"intrinsic:readonly-span-get-item:{element.DisplayName}",
			"CopyTo" when
				signature.Header.IsInstance &&
				signature.ParameterTypes is
				[
					{
						Kind: CilTypeKind.ValueType,
						GenericArguments: [var destinationElement]
					} destination
				] &&
				destination.DisplayName.StartsWith(
					"System.Span`1<",
					StringComparison.Ordinal) &&
				destinationElement.Equals(element) &&
				signature.ReturnType.DisplayName == "void" =>
					$"intrinsic:readonly-span-copy-to:{element.DisplayName}",
			_ => null
		};
		if (target is null)
		{
			return null;
		}

		var effects = member.Name == "CopyTo"
			? FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory
			: member.Name is "get_Item" or "Slice"
			? FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory
			: member.Name == ".ctor"
				? FrameworkEffects.None
				: FrameworkEffects.ReadsManagedMemory;
		return Intrinsic(
			member,
			target,
			member.Name is ".ctor" or "CopyTo"
				? Effects(effects, FrameworkFeature.Spans)
				: Effects(effects, FrameworkFeature.Spans, FrameworkFeature.ManagedArrays));
	}

	private static FrameworkBinding? TryBindMemory(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (!TryGetMemoryElement(
				member,
				context,
				MemoryDefinition,
				out var element))
		{
			return null;
		}

		var signature = context.CilSignature;
		var target = member.Name switch
		{
			".ctor" when
				signature.Header.IsInstance &&
				IsElementArrayParameter(signature, element) =>
					$"intrinsic:memory-from-array:{element.DisplayName}",
			".ctor" when
				signature.Header.IsInstance &&
				IsElementArrayRangeParameters(signature, element) =>
					$"intrinsic:memory-from-array-range:{element.DisplayName}",
			"op_Implicit" when
				!signature.Header.IsInstance &&
				IsElementArrayParameter(signature, element) &&
				signature.ReturnType.DisplayName ==
					context.ConstructedDeclaringType!.DisplayName =>
					$"intrinsic:memory-from-array:{element.DisplayName}",
			"op_Implicit" when
				!signature.Header.IsInstance &&
				signature.ParameterTypes is
				[
					{
						Kind: CilTypeKind.ValueType,
						GenericArguments: [var sourceElement]
					} source
				] &&
				source.DisplayName.StartsWith(
					"System.Memory`1<",
					StringComparison.Ordinal) &&
				sourceElement.Equals(element) &&
				signature.ReturnType.DisplayName.StartsWith(
					"System.ReadOnlyMemory`1<",
					StringComparison.Ordinal) =>
					$"intrinsic:readonly-memory-from-memory:{element.DisplayName}",
			"get_Length" when IsMemoryScalarGetter(signature, "int") =>
				$"intrinsic:memory-length:{element.DisplayName}",
			"get_IsEmpty" when IsMemoryScalarGetter(signature, "bool") =>
				$"intrinsic:memory-is-empty:{element.DisplayName}",
			"Slice" when IsMemorySlice(signature, context, withLength: false) =>
				$"intrinsic:memory-slice-start:{element.DisplayName}",
			"Slice" when IsMemorySlice(signature, context, withLength: true) =>
				$"intrinsic:memory-slice-range:{element.DisplayName}",
			"get_Span" when
				IsMemorySpanGetter(signature, element, readOnly: false) =>
					$"intrinsic:span-from-memory:{element.DisplayName}",
			"CopyTo" when IsMemoryCopy(signature, element, returnsBoolean: false) =>
				$"intrinsic:memory-copy-to:{element.DisplayName}",
			"TryCopyTo" when IsMemoryCopy(signature, element, returnsBoolean: true) =>
				$"intrinsic:memory-try-copy-to:{element.DisplayName}",
			_ => null
		};
		return CreateMemoryBinding(member, target);
	}

	private static FrameworkBinding? TryBindReadOnlyMemory(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		if (!TryGetMemoryElement(
				member,
				context,
				ReadOnlyMemoryDefinition,
				out var element))
		{
			return null;
		}

		var signature = context.CilSignature;
		var target = member.Name switch
		{
			".ctor" when
				signature.Header.IsInstance &&
				IsElementArrayParameter(signature, element) =>
					$"intrinsic:readonly-memory-from-array:{element.DisplayName}",
			".ctor" when
				signature.Header.IsInstance &&
				IsElementArrayRangeParameters(signature, element) =>
					$"intrinsic:readonly-memory-from-array-range:{element.DisplayName}",
			"op_Implicit" when
				!signature.Header.IsInstance &&
				IsElementArrayParameter(signature, element) &&
				signature.ReturnType.DisplayName ==
					context.ConstructedDeclaringType!.DisplayName =>
					$"intrinsic:readonly-memory-from-array:{element.DisplayName}",
			"get_Length" when IsMemoryScalarGetter(signature, "int") =>
				$"intrinsic:readonly-memory-length:{element.DisplayName}",
			"get_IsEmpty" when IsMemoryScalarGetter(signature, "bool") =>
				$"intrinsic:readonly-memory-is-empty:{element.DisplayName}",
			"Slice" when IsMemorySlice(signature, context, withLength: false) =>
				$"intrinsic:readonly-memory-slice-start:{element.DisplayName}",
			"Slice" when IsMemorySlice(signature, context, withLength: true) =>
				$"intrinsic:readonly-memory-slice-range:{element.DisplayName}",
			"get_Span" when
				IsMemorySpanGetter(signature, element, readOnly: true) =>
					$"intrinsic:readonly-span-from-memory:{element.DisplayName}",
			"CopyTo" when IsMemoryCopy(signature, element, returnsBoolean: false) =>
				$"intrinsic:readonly-memory-copy-to:{element.DisplayName}",
			"TryCopyTo" when IsMemoryCopy(signature, element, returnsBoolean: true) =>
				$"intrinsic:readonly-memory-try-copy-to:{element.DisplayName}",
			_ => null
		};
		return CreateMemoryBinding(member, target);
	}

	private static bool TryGetMemoryElement(
		FrameworkMemberId member,
		FrameworkBindingContext context,
		FrameworkTypeId definition,
		out CilType element)
	{
		element = null!;
		if (member.DeclaringType.Kind != FrameworkTypeKind.GenericInstantiation ||
			!member.DeclaringType.ElementType!.Equals(definition) ||
			member.DeclaringType.GenericArguments.Length != 1 ||
			member.MethodTypeArguments.Length != 0 ||
			context.ConstructedDeclaringType is not
			{
				GenericArguments: [var candidate]
			} ||
			!candidate.IsSupportedScalar ||
			candidate.Kind is CilTypeKind.ManagedPointer or CilTypeKind.GenericParameter)
		{
			return false;
		}

		element = candidate;
		return true;
	}

	private static bool IsElementArrayParameter(
		MethodSignature<CilType> signature,
		CilType element) =>
		signature.ParameterTypes is
		[
			{
				Kind: CilTypeKind.ManagedReference,
				ElementType: { } arrayElement
			}
		] &&
		arrayElement.Equals(element);

	private static bool IsElementArrayRangeParameters(
		MethodSignature<CilType> signature,
		CilType element) =>
		signature.ParameterTypes is
		[
			{
				Kind: CilTypeKind.ManagedReference,
				ElementType: { } arrayElement
			},
			{ DisplayName: "int" },
			{ DisplayName: "int" }
		] &&
		arrayElement.Equals(element);

	private static bool IsMemoryScalarGetter(
		MethodSignature<CilType> signature,
		string returnType) =>
		signature.Header.IsInstance &&
		signature.ParameterTypes.Length == 0 &&
		signature.ReturnType.DisplayName == returnType;

	private static bool IsMemorySlice(
		MethodSignature<CilType> signature,
		FrameworkBindingContext context,
		bool withLength) =>
		signature.Header.IsInstance &&
		signature.ReturnType.DisplayName ==
			context.ConstructedDeclaringType!.DisplayName &&
		(withLength
			? signature.ParameterTypes is
				[{ DisplayName: "int" }, { DisplayName: "int" }]
			: signature.ParameterTypes is [{ DisplayName: "int" }]);

	private static bool IsMemorySpanGetter(
		MethodSignature<CilType> signature,
		CilType element,
		bool readOnly) =>
		signature.Header.IsInstance &&
		signature.ParameterTypes.Length == 0 &&
		signature.ReturnType.Kind == CilTypeKind.ValueType &&
		signature.ReturnType.GenericArguments is [var resultElement] &&
		resultElement.Equals(element) &&
		signature.ReturnType.DisplayName.StartsWith(
			readOnly ? "System.ReadOnlySpan`1<" : "System.Span`1<",
			StringComparison.Ordinal);

	private static bool IsMemoryCopy(
		MethodSignature<CilType> signature,
		CilType element,
		bool returnsBoolean) =>
		signature.Header.IsInstance &&
		signature.ParameterTypes is
		[
			{
				Kind: CilTypeKind.ValueType,
				GenericArguments: [var destinationElement]
			} destination
		] &&
		destination.DisplayName.StartsWith(
			"System.Memory`1<",
			StringComparison.Ordinal) &&
		destinationElement.Equals(element) &&
		signature.ReturnType.DisplayName == (returnsBoolean ? "bool" : "void");

	private static FrameworkBinding? CreateMemoryBinding(
		FrameworkMemberId member,
		string? target)
	{
		if (target is null)
		{
			return null;
		}

		var isCopy = target.Contains("memory-copy-to:", StringComparison.Ordinal) ||
			target.Contains("memory-try-copy-to:", StringComparison.Ordinal);
		var effects = isCopy
			? FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory |
				(target.Contains("try-copy", StringComparison.Ordinal)
					? FrameworkEffects.None
					: FrameworkEffects.MayThrow)
			: target.Contains("-range:", StringComparison.Ordinal) ||
				target.Contains("-slice-", StringComparison.Ordinal)
					? FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory
					: FrameworkEffects.ReadsManagedMemory;
		var features = target.Contains("span-from-memory:", StringComparison.Ordinal) || isCopy
			? Effects(
				effects,
				FrameworkFeature.ManagedMemory,
				FrameworkFeature.ManagedArrays,
				FrameworkFeature.ManagedGc,
				FrameworkFeature.Spans)
			: Effects(
				effects,
				FrameworkFeature.ManagedMemory,
				FrameworkFeature.ManagedArrays,
				FrameworkFeature.ManagedGc);
		return Intrinsic(member, target, features);
	}

	private static FrameworkBinding? TryBindCompilerIntrinsic(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		var typeName = context.TypeName;
		var name = member.Name;
		var signature = context.CilSignature;

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "DefaultEquals" &&
			context.MethodTypeArguments is [var element] &&
			signature is
			{
				Header.IsInstance: false,
				ReturnType.DisplayName: "bool",
				ParameterTypes: [var left, var right]
			} &&
			left.DisplayName == element.DisplayName &&
			right.DisplayName == element.DisplayName)
		{
			if (IsIntegralListEqualityElement(element))
			{
				return Intrinsic(
					member,
					$"intrinsic:runtime-integral-equals:{element.Size * 8}",
					FrameworkEffectSummary.None);
			}
			if (IsFloatingPointListEqualityElement(element))
			{
				return Intrinsic(
					member,
					$"intrinsic:runtime-floating-equals:{element.Size * 8}",
					FrameworkEffectSummary.None);
			}
			if (IsStringListEqualityElement(element))
			{
				return Intrinsic(
					member,
					"intrinsic:string-equality",
					Effects(
						FrameworkEffects.ReadsManagedMemory,
						FrameworkFeature.ManagedStrings));
			}
			if (IsSupportedNullableListEqualityElement(element))
			{
				return Shadow(
					member,
					new FrameworkShadowMethod(
						"CopperSharp.Runtime.Managed",
						"CopperSharp.Runtime.ShadowObject",
						"DefaultEqualsNullable"),
					Effects(
						FrameworkEffects.None,
						FrameworkFeature.NullableValues));
			}
			if (context.DefaultEqualityKind == FrameworkDefaultEqualityKind.SealedObjectEquals)
			{
				return Shadow(
					member,
					new FrameworkShadowMethod(
						"CopperSharp.Runtime.Managed",
						"CopperSharp.Runtime.ShadowObject",
						"DefaultEqualsObject"),
					Effects(
						FrameworkEffects.MayAllocate |
							FrameworkEffects.MayThrow |
							FrameworkEffects.MayCollect |
							FrameworkEffects.ReadsManagedMemory |
							FrameworkEffects.WritesManagedMemory,
						FrameworkFeature.ManagedObjects,
						FrameworkFeature.ManagedGc));
			}
			if (context.DefaultEqualityKind == FrameworkDefaultEqualityKind.SealedIEquatable)
			{
				return Shadow(
					member,
					new FrameworkShadowMethod(
						"CopperSharp.Runtime.Managed",
						"CopperSharp.Runtime.ShadowObject",
						"DefaultEqualsEquatable"),
					Effects(
						FrameworkEffects.MayAllocate |
							FrameworkEffects.MayThrow |
							FrameworkEffects.MayCollect |
							FrameworkEffects.ReadsManagedMemory |
							FrameworkEffects.WritesManagedMemory,
						FrameworkFeature.ManagedObjects,
						FrameworkFeature.ManagedGc));
			}
		}
		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "DefaultHashCode" &&
			context.MethodTypeArguments is [var referenceHashElement] &&
			signature is
			{
				Header.IsInstance: false,
				ReturnType.DisplayName: "int",
				ParameterTypes: [var referenceHashValue]
			} &&
			referenceHashValue.DisplayName == referenceHashElement.DisplayName &&
			context.DefaultEqualityKind is
				FrameworkDefaultEqualityKind.SealedObjectEquals or
				FrameworkDefaultEqualityKind.SealedIEquatable)
		{
			return Shadow(
				member,
				new FrameworkShadowMethod(
					"CopperSharp.Runtime.Managed",
					"CopperSharp.Runtime.ShadowObject",
					"DefaultHashCodeObject"),
				Effects(
					FrameworkEffects.MayAllocate |
						FrameworkEffects.MayThrow |
						FrameworkEffects.MayCollect |
						FrameworkEffects.ReadsManagedMemory |
						FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.ManagedObjects,
					FrameworkFeature.ManagedGc));
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "DefaultHashCode" &&
			context.MethodTypeArguments is [var nullableHashElement] &&
			signature is
			{
				Header.IsInstance: false,
				ReturnType.DisplayName: "int",
				ParameterTypes: [var nullableHashValue]
			} &&
			nullableHashValue.DisplayName == nullableHashElement.DisplayName &&
			IsSupportedNullableListEqualityElement(nullableHashElement))
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-nullable-integral-hash:32",
				Effects(
					FrameworkEffects.None,
					FrameworkFeature.NullableValues));
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "DefaultHashCode" &&
			context.MethodTypeArguments is [var stringHashElement] &&
			signature is
			{
				Header.IsInstance: false,
				ReturnType.DisplayName: "int",
				ParameterTypes: [var stringHashValue]
			} &&
			stringHashValue.DisplayName == stringHashElement.DisplayName &&
			IsStringListEqualityElement(stringHashElement))
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-string-hash",
				Effects(
					FrameworkEffects.ReadsManagedMemory,
					FrameworkFeature.ManagedStrings));
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "DefaultHashCode" &&
			context.MethodTypeArguments is [var floatingHashElement] &&
			signature is
			{
				Header.IsInstance: false,
				ReturnType.DisplayName: "int",
				ParameterTypes: [var floatingHashValue]
			} &&
			floatingHashValue.DisplayName == floatingHashElement.DisplayName &&
			IsFloatingPointListEqualityElement(floatingHashElement))
		{
			return Intrinsic(
				member,
				$"intrinsic:runtime-floating-hash:{floatingHashElement.Size * 8}",
				FrameworkEffectSummary.None);
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "DefaultHashCode" &&
			context.MethodTypeArguments is [var hashElement] &&
			signature is
			{
				Header.IsInstance: false,
				ReturnType.DisplayName: "int",
				ParameterTypes: [var hashValue]
			} &&
			hashValue.DisplayName == hashElement.DisplayName &&
			IsIntegralListEqualityElement(hashElement))
		{
			return Intrinsic(
				member,
				$"intrinsic:runtime-integral-hash:{hashElement.Size * 8}",
				FrameworkEffectSummary.None);
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "DictionaryKeyIsNull" &&
			context.MethodTypeArguments is [var dictionaryKey] &&
			signature is
			{
				Header.IsInstance: false,
				ReturnType.DisplayName: "bool",
				ParameterTypes: [var dictionaryKeyValue]
			} &&
			dictionaryKeyValue.DisplayName == dictionaryKey.DisplayName &&
			IsSupportedDictionaryKey(dictionaryKey))
		{
			return Intrinsic(
				member,
				IsStringListEqualityElement(dictionaryKey)
					? "intrinsic:dictionary-key-is-null:reference"
					: "intrinsic:dictionary-key-is-null:false",
				FrameworkEffectSummary.None);
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "ReferenceAsObject" &&
			context.MethodTypeArguments is [var referenceType] &&
			referenceType.Kind == CilTypeKind.ManagedReference &&
			signature is
			{
				Header.IsInstance: false,
				ReturnType.DisplayName: "object",
				ParameterTypes: [var value]
			} &&
			value.DisplayName == referenceType.DisplayName)
		{
			return Intrinsic(
				member,
				"intrinsic:ref-cast",
				Effects(FrameworkEffects.None, FrameworkFeature.ManagedObjects));
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "ReferenceAsEquatable" &&
			context.MethodTypeArguments is [var equatableReferenceType] &&
			equatableReferenceType.Kind == CilTypeKind.ManagedReference &&
			signature is
			{
				Header.IsInstance: false,
				ReturnType:
				{
					Kind: CilTypeKind.ManagedReference,
					GenericArguments: [var equatableElement]
				},
				ParameterTypes: [var equatableValue]
			} &&
			signature.ReturnType.DisplayName.StartsWith(
				"System.IEquatable`1<",
				StringComparison.Ordinal) &&
			equatableValue.DisplayName == equatableReferenceType.DisplayName &&
			equatableElement.DisplayName == equatableReferenceType.DisplayName)
		{
			return Intrinsic(
				member,
				"intrinsic:ref-cast",
				Effects(FrameworkEffects.None, FrameworkFeature.ManagedObjects));
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name is "SplitInt64" or "SplitUInt64" or "SplitDouble" &&
			signature is
			{
				Header.IsInstance: false,
				ReturnType.DisplayName: "uint",
				ParameterTypes:
				[
					{ Size: 8, IsSupportedScalar: true },
					{ DisplayName: "uint&" }
				]
			})
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-int64-split",
				Effects(
					FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.Numerics));
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name is "CombineInt64" or "CombineDouble" &&
			signature is
			{
				Header.IsInstance: false,
				ReturnType: { Size: 8, IsSupportedScalar: true },
				ParameterTypes:
				[
					{ DisplayName: "uint" },
					{ DisplayName: "uint" }
				]
			})
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-int64-combine",
				Effects(FrameworkEffects.None, FrameworkFeature.Numerics));
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name is "SingleToUInt32Bits" or "UInt32BitsToSingle" &&
			signature is
			{
				Header.IsInstance: false,
				ParameterTypes: [{ Size: 4, IsSupportedScalar: true }]
			})
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-bitcast-32",
				Effects(FrameworkEffects.None, FrameworkFeature.Numerics));
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "AllocateString" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].DisplayName == "int")
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-allocate-string",
				Effects(
					FrameworkEffects.MayAllocate |
					FrameworkEffects.MayThrow |
					FrameworkEffects.MayCollect,
					FrameworkFeature.ManagedStrings));
		}
		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "SetStringChar" &&
			signature.ParameterTypes is
				[
					{ DisplayName: "string" },
					{ DisplayName: "int" },
					{ DisplayName: "char" }
				])
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-set-string-char",
				Effects(
					FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.ManagedStrings));
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "ThrowOverflowException" &&
			signature.ParameterTypes.Length == 0)
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-throw-overflow",
				Effects(
					FrameworkEffects.MayThrow,
					FrameworkFeature.ManagedExceptions));
		}
		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "ThrowArithmeticException" &&
			signature.ParameterTypes.Length == 0)
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-throw-arithmetic",
				Effects(
					FrameworkEffects.MayThrow,
					FrameworkFeature.ManagedExceptions));
		}
			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "ThrowFormatException" &&
			signature.ParameterTypes.Length == 0)
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-throw-format",
				Effects(
					FrameworkEffects.MayThrow,
						FrameworkFeature.ManagedExceptions));
			}
			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "ThrowArgumentException" &&
				signature.ParameterTypes.Length == 0)
			{
				return Intrinsic(
					member,
					"intrinsic:runtime-throw-argument",
					Effects(
						FrameworkEffects.MayThrow,
						FrameworkFeature.ManagedExceptions));
			}
			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "ThrowArgumentNullException" &&
				signature.ParameterTypes.Length == 0)
			{
				return Intrinsic(
					member,
					"intrinsic:runtime-throw-argument-null",
					Effects(
						FrameworkEffects.MayThrow,
						FrameworkFeature.ManagedExceptions));
			}
			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "ThrowArgumentOutOfRangeException" &&
				signature.ParameterTypes.Length == 0)
			{
				return Intrinsic(
					member,
					"intrinsic:runtime-throw-argument-out-of-range",
					Effects(
						FrameworkEffects.MayThrow,
						FrameworkFeature.ManagedExceptions));
			}
			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "ThrowInvalidOperationException" &&
				signature.ParameterTypes.Length == 0)
			{
				return Intrinsic(
					member,
					"intrinsic:runtime-throw-invalid-operation",
					Effects(
						FrameworkEffects.MayThrow,
						FrameworkFeature.ManagedExceptions));
			}
			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "ThrowIOException" &&
				signature.ParameterTypes.Length == 0)
			{
				return Intrinsic(
					member,
					"intrinsic:runtime-throw-io",
					Effects(
						FrameworkEffects.MayThrow,
						FrameworkFeature.ManagedExceptions));
			}
			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "ThrowDirectoryNotFoundException" &&
				signature.ParameterTypes.Length == 0)
			{
				return Intrinsic(
					member,
					"intrinsic:runtime-throw-directory-not-found",
					Effects(
						FrameworkEffects.MayThrow,
						FrameworkFeature.ManagedExceptions));
			}
			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "ThrowFileNotFoundException" &&
				signature.ParameterTypes.Length == 0)
			{
				return Intrinsic(
					member,
					"intrinsic:runtime-throw-file-not-found",
					Effects(
						FrameworkEffects.MayThrow,
						FrameworkFeature.ManagedExceptions));
			}
			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "ThrowUnauthorizedAccessException" &&
				signature.ParameterTypes.Length == 0)
			{
				return Intrinsic(
					member,
					"intrinsic:runtime-throw-unauthorized-access",
					Effects(
						FrameworkEffects.MayThrow,
						FrameworkFeature.ManagedExceptions));
			}
			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "ThrowKeyNotFoundException" &&
				signature.ParameterTypes.Length == 0)
			{
				return Intrinsic(
					member,
					"intrinsic:runtime-throw-key-not-found",
					Effects(
						FrameworkEffects.MayThrow,
						FrameworkFeature.ManagedExceptions));
			}
			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
				name == "ThrowOutOfMemoryException" &&
				signature.ParameterTypes.Length == 0)
			{
				return Intrinsic(
					member,
					"intrinsic:runtime-throw-out-of-memory",
					Effects(
						FrameworkEffects.MayThrow,
						FrameworkFeature.ManagedExceptions));
			}

			if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name.StartsWith("Dispose", StringComparison.Ordinal) &&
			signature.ParameterTypes.Length == 1)
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-dispose",
				Effects(
					FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.ManagedGc));
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name == "Collect" &&
			signature.ParameterTypes.Length == 0)
		{
			return Intrinsic(
				member,
				"intrinsic:runtime-gc-collect",
				Effects(
					FrameworkEffects.MayCollect | FrameworkEffects.WritesManagedMemory,
					FrameworkFeature.ManagedGc));
		}

		if (typeName == "CopperSharp.Compiler.M68kRuntime" &&
			name is "GetGcStaleBytes" or "GetGcStaleBlocks" &&
			signature.ParameterTypes.Length == 0)
		{
			return Intrinsic(
				member,
				$"intrinsic:runtime-{name}",
				Effects(
					FrameworkEffects.ReadsManagedMemory,
					FrameworkFeature.ManagedGc));
		}

		if (name == "FromAddress" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].DisplayName == "Amiga.APTR" &&
			signature.ReturnType.IsReference)
		{
			return Intrinsic(
				member,
				"intrinsic:address-to-ref",
				Effects(FrameworkEffects.None, FrameworkFeature.NativeMemory));
		}

		if (typeName == "Amiga.CString")
		{
			if ((name == "FromLiteral" || name == "op_Implicit") &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "string")
			{
				return Intrinsic(
					member,
					"intrinsic:cstring-from-literal",
					Effects(
						FrameworkEffects.ReadsManagedMemory,
						FrameworkFeature.NativeCStrings,
						FrameworkFeature.AmigaInterop));
			}

			if ((name == "FromPointer" || name == "op_Implicit") &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "uint")
			{
				return Intrinsic(
					member,
					"intrinsic:cstring-from-pointer",
					Effects(
						FrameworkEffects.None,
						FrameworkFeature.NativeCStrings,
						FrameworkFeature.AmigaInterop));
			}

			if ((name == "ToUInt32" || name == "op_Implicit") &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "Amiga.CString")
			{
				return Intrinsic(
					member,
					"intrinsic:cstring-to-uint32",
					Effects(
						FrameworkEffects.None,
						FrameworkFeature.NativeCStrings,
						FrameworkFeature.AmigaInterop));
			}
		}

		if (typeName == "Amiga.FileInfoBlock" &&
			name is "FileName" or "Comment" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].DisplayName == "uint")
		{
			return Intrinsic(
				member,
				"intrinsic:file-info-block-file-name",
				Effects(
					FrameworkEffects.ReadsNativeMemory,
					FrameworkFeature.NativeCStrings,
					FrameworkFeature.AmigaInterop));
		}

		if (typeName == "Amiga.FileInfoBlock" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].DisplayName == "uint")
		{
			var offset = name switch
			{
				"GetDirEntryType" => 4,
				"GetProtection" => 116,
				"GetSize" => 124,
				"GetDateDays" => 132,
				"GetDateMinute" => 136,
				"GetDateTick" => 140,
				_ => -1
			};
			if (offset >= 0)
			{
				return Intrinsic(
					member,
					$"intrinsic:file-info-block-read-int32:{offset}",
					Effects(
						FrameworkEffects.ReadsNativeMemory,
						FrameworkFeature.NativeMemory,
						FrameworkFeature.AmigaInterop));
			}
		}
		if (typeName is
			"Amiga.APTR" or
			"Amiga.BPTR" or
			"Amiga.STRPTR" or
			"Amiga.CONST_STRPTR" or
			"Amiga.IFFHandle" or
			"CopperSharp.Compiler.M68kAddress")
		{
			if (name == "get_Null" && signature.ParameterTypes.Length == 0)
			{
				return NativeMemoryIntrinsic(member, "intrinsic:aptr-null");
			}

			if ((name is "FromPointer" or "FromRaw" or "FromUInt32" or "op_Implicit") &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "uint")
			{
				return NativeMemoryIntrinsic(member, "intrinsic:aptr-from-pointer");
			}

			if (typeName == "Amiga.APTR" && name == "ExportAddress" &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "string")
			{
				return Intrinsic(
					member,
					"intrinsic:aptr-export-address",
					Effects(
						FrameworkEffects.ReadsManagedMemory,
						FrameworkFeature.NativeMemory));
			}

			if (typeName is "Amiga.APTR" or "CopperSharp.Compiler.M68kAddress" &&
				name == "ReadUInt8" &&
				signature.ParameterTypes.Length == 2 &&
				signature.ParameterTypes[0].DisplayName == typeName &&
				signature.ParameterTypes[1].DisplayName == "int")
			{
				return Intrinsic(
					member,
					"intrinsic:aptr-read-uint8",
					Effects(
						FrameworkEffects.ReadsNativeMemory,
						FrameworkFeature.NativeMemory));
			}

			if (typeName is "Amiga.APTR" or "CopperSharp.Compiler.M68kAddress" &&
				name == "ReadUInt16" &&
				signature.ParameterTypes.Length == 2 &&
				signature.ParameterTypes[0].DisplayName == typeName &&
				signature.ParameterTypes[1].DisplayName == "int")
			{
				return Intrinsic(
					member,
					"intrinsic:aptr-read-uint16",
					Effects(
						FrameworkEffects.ReadsNativeMemory,
						FrameworkFeature.NativeMemory));
			}

			if (typeName is "Amiga.APTR" or "CopperSharp.Compiler.M68kAddress" &&
				name == "ReadUInt32" &&
				signature.ParameterTypes.Length == 2 &&
				signature.ParameterTypes[0].DisplayName == typeName &&
				signature.ParameterTypes[1].DisplayName == "int")
			{
				return Intrinsic(
					member,
					"intrinsic:aptr-read-uint32",
					Effects(
						FrameworkEffects.ReadsNativeMemory,
						FrameworkFeature.NativeMemory));
			}

			if (typeName is "Amiga.APTR" or "CopperSharp.Compiler.M68kAddress" &&
				name == "WriteUInt8" &&
				signature.ParameterTypes.Length == 3 &&
				signature.ParameterTypes[0].DisplayName == typeName &&
				signature.ParameterTypes[1].DisplayName == "int" &&
				signature.ParameterTypes[2].DisplayName == "byte")
			{
				return Intrinsic(
					member,
					"intrinsic:aptr-write-uint8",
					Effects(
						FrameworkEffects.WritesNativeMemory,
						FrameworkFeature.NativeMemory));
			}

			if (typeName is "Amiga.APTR" or "CopperSharp.Compiler.M68kAddress" &&
				name == "WriteUInt16" &&
				signature.ParameterTypes.Length == 3 &&
				signature.ParameterTypes[0].DisplayName == typeName &&
				signature.ParameterTypes[1].DisplayName == "int" &&
				signature.ParameterTypes[2].DisplayName == "ushort")
			{
				return Intrinsic(
					member,
					"intrinsic:aptr-write-uint16",
					Effects(
						FrameworkEffects.WritesNativeMemory,
						FrameworkFeature.NativeMemory));
			}

			if (typeName is "Amiga.APTR" or "CopperSharp.Compiler.M68kAddress" &&
				name == "WriteUInt32" &&
				signature.ParameterTypes.Length == 3 &&
				signature.ParameterTypes[0].DisplayName == typeName &&
				signature.ParameterTypes[1].DisplayName == "int" &&
				signature.ParameterTypes[2].DisplayName == "uint")
			{
				return Intrinsic(
					member,
					"intrinsic:aptr-write-uint32",
					Effects(
						FrameworkEffects.WritesNativeMemory,
						FrameworkFeature.NativeMemory));
			}

			if ((name == "ToUInt32" || name == "op_Implicit") &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == typeName)
			{
				return NativeMemoryIntrinsic(member, "intrinsic:aptr-to-uint32");
			}

			if (name == "get_Raw" && signature.ParameterTypes.Length == 0)
			{
				return NativeMemoryIntrinsic(member, "intrinsic:aptr-raw");
			}

			if (name == "get_IsNull" && signature.ParameterTypes.Length == 0)
			{
				return NativeMemoryIntrinsic(member, "intrinsic:aptr-is-null");
			}

			if (name == "get_IsNotNull" && signature.ParameterTypes.Length == 0)
			{
				return NativeMemoryIntrinsic(member, "intrinsic:aptr-is-not-null");
			}

			if ((typeName is "Amiga.STRPTR" or "Amiga.CONST_STRPTR") &&
				name == "get_Address" &&
				signature.ParameterTypes.Length == 0)
			{
				// An instance value-type receiver is a managed address. Load the
				// transparent scalar stored there before projecting it as APTR;
				// treating this as the static identity conversion leaks the address
				// of an embedded STRPTR field instead of its pointer payload.
				return NativeMemoryIntrinsic(member, "intrinsic:aptr-raw");
			}

			if ((typeName is "Amiga.STRPTR" or "Amiga.CONST_STRPTR") &&
				name == "ToAddress" &&
				signature.ParameterTypes.Length == 1)
			{
				return NativeMemoryIntrinsic(member, "intrinsic:aptr-to-uint32");
			}

			if ((typeName is "Amiga.STRPTR" or "Amiga.CONST_STRPTR") &&
				name == "FromAddress" &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "Amiga.APTR")
			{
				return NativeMemoryIntrinsic(member, "intrinsic:aptr-from-pointer");
			}

			if (typeName == "Amiga.CONST_STRPTR" && name == "op_Implicit" &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "Amiga.STRPTR")
			{
				return NativeMemoryIntrinsic(member, "intrinsic:aptr-from-pointer");
			}

			if (typeName == "Amiga.BPTR" &&
				(name == "get_Address" || name == "ToAddress") &&
				signature.ParameterTypes.Length <= 1)
			{
				return NativeMemoryIntrinsic(member, "intrinsic:bptr-address");
			}

			if (typeName == "Amiga.BPTR" && name == "FromAddress" &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "Amiga.APTR")
			{
				return NativeMemoryIntrinsic(member, "intrinsic:bptr-from-address");
			}

			if (typeName == "Amiga.IFFHandle" && name == "get_Stream" &&
				signature.ParameterTypes.Length == 0)
			{
				return Intrinsic(
					member,
					"intrinsic:iff-handle-stream",
					Effects(
						FrameworkEffects.ReadsNativeMemory,
						FrameworkFeature.AmigaInterop));
			}

			if (typeName == "Amiga.IFFHandle" && name == "SetStream" &&
				signature.ParameterTypes.Length == 1 &&
				signature.ParameterTypes[0].DisplayName == "Amiga.BPTR")
			{
				return Intrinsic(
					member,
					"intrinsic:iff-handle-set-stream",
					Effects(
						FrameworkEffects.WritesNativeMemory,
						FrameworkFeature.AmigaInterop));
			}
		}

		if (typeName == "Amiga.AmigaVarArg" && name == "op_Implicit" &&
			signature.ParameterTypes.Length == 1)
		{
			if (signature.ParameterTypes[0].DisplayName == "string")
			{
				return Intrinsic(
					member,
					"intrinsic:amiga-vararg-from-literal",
					Effects(
						FrameworkEffects.ReadsManagedMemory,
						FrameworkFeature.NativeCStrings,
						FrameworkFeature.AmigaInterop));
			}

			if (signature.ParameterTypes[0].Size == 4 ||
				signature.ParameterTypes[0].Kind == CilTypeKind.ValueType)
			{
				return Intrinsic(
					member,
					"intrinsic:amiga-vararg-from-value",
					Effects(FrameworkEffects.None, FrameworkFeature.AmigaInterop));
			}
		}

		if (typeName == "Amiga.Hook" && name == "AddressOf" &&
			signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].DisplayName == "Amiga.Hook&")
		{
			return NativeMemoryIntrinsic(member, "intrinsic:hook-address-of");
		}

		if (name == "AddressOf" && signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].Kind == CilTypeKind.ManagedPointer &&
			signature.ReturnType.DisplayName == "Amiga.APTR")
		{
			return NativeMemoryIntrinsic(member, "intrinsic:address-of-ref");
		}

		if (name == "Cast" && signature.ParameterTypes.Length == 1 &&
			signature.ParameterTypes[0].Kind == CilTypeKind.ManagedPointer &&
			signature.ReturnType.Kind == CilTypeKind.ManagedPointer)
		{
			return NativeMemoryIntrinsic(member, "intrinsic:ref-cast");
		}

		if ((typeName == "Amiga.BOOPSI+Message" || typeName == "Message") &&
			name == "AddressOf" && signature.ParameterTypes.Length == 1 &&
			(signature.ParameterTypes[0].DisplayName == "Amiga.BOOPSI+Message&" ||
			 signature.ParameterTypes[0].DisplayName == "Message&"))
		{
			return NativeMemoryIntrinsic(member, "intrinsic:boopsi-message-address-of");
		}

		if (typeName == "Amiga.BOOPSI" && name == "InstanceData" &&
			signature.ParameterTypes.Length == 2 &&
			signature.ParameterTypes[0].DisplayName == "Amiga.APTR" &&
			signature.ParameterTypes[1].DisplayName == "Amiga.APTR" &&
			signature.ReturnType.DisplayName == "Amiga.APTR")
		{
			return Intrinsic(
				member,
				"intrinsic:boopsi-instance-data",
				Effects(
					FrameworkEffects.ReadsNativeMemory,
					FrameworkFeature.AmigaInterop));
		}

		if (typeName == "Amiga.BOOPSI" && name == "DoMethod" &&
			signature.ParameterTypes.Length == 2 &&
			signature.ParameterTypes[1].DisplayName == "uint[]" &&
			signature.ParameterTypes[1].ElementType?.DisplayName == "uint")
		{
			return Intrinsic(
				member,
				"intrinsic:boopsi-do-method-stack-varargs",
				Effects(
					FrameworkEffects.ReadsNativeMemory | FrameworkEffects.WritesNativeMemory,
					FrameworkFeature.AmigaInterop));
		}

		if (typeName == "Amiga.BOOPSI" && name == "DoMethod" &&
			signature.ParameterTypes.Length is >= 2 and <= 8)
		{
			return Intrinsic(
				member,
				"intrinsic:boopsi-do-method",
				Effects(
					FrameworkEffects.ReadsNativeMemory | FrameworkEffects.WritesNativeMemory,
					FrameworkFeature.AmigaInterop));
		}

		if (TryGetAmigaLibraryBaseIntrinsic(typeName, name, signature) is { } libraryBase)
		{
			return Intrinsic(
				member,
				libraryBase,
				Effects(
					libraryBase.Contains("-set:", StringComparison.Ordinal)
						? FrameworkEffects.WritesNativeMemory
						: FrameworkEffects.ReadsNativeMemory,
					FrameworkFeature.AmigaInterop));
		}

		return null;
	}

	private static FrameworkBinding NativeMemoryIntrinsic(
		FrameworkMemberId member,
		string target) =>
		Intrinsic(
			member,
			target,
			Effects(FrameworkEffects.None, FrameworkFeature.NativeMemory));

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

	private static bool SignatureEquals(
		FrameworkMemberId member,
		FrameworkTypeId returnType,
		params FrameworkTypeId[] parameterTypes) =>
		member.Signature.Header == 0x20 &&
		member.Signature.GenericParameterCount == 0 &&
		member.Signature.RequiredParameterCount == parameterTypes.Length &&
		member.Signature.ReturnType.Equals(returnType) &&
		member.Signature.ParameterTypes.SequenceEqual(parameterTypes);

	private static FrameworkMemberId Member(
		string assembly,
		string type,
		string name,
		bool isInstance,
		FrameworkTypeId returnType,
		params FrameworkTypeId[] parameterTypes) =>
		new(
			Named(assembly, type),
			name,
			new FrameworkMethodSignatureId(
				isInstance ? (byte)0x20 : (byte)0x00,
				0,
				parameterTypes.Length,
				returnType,
				parameterTypes));

	private static FrameworkTypeId Named(string assembly, string type) =>
		FrameworkTypeId.Named(assembly, type);

	private static void Add(
		IDictionary<FrameworkMemberId, FrameworkBinding> bindings,
		FrameworkMemberId member,
		string target,
		FrameworkEffectSummary effects)
	{
		if (!bindings.TryAdd(member, Intrinsic(member, target, effects)))
		{
			throw new InvalidOperationException(
				$"Duplicate framework binding identity '{member.DisplayName}'.");
		}
	}

	private static void AddShadow(
		IDictionary<FrameworkMemberId, FrameworkBinding> bindings,
		FrameworkMemberId member,
		FrameworkShadowMethod shadowMethod,
		FrameworkEffectSummary effects,
		bool preservesVirtualDispatch = false)
	{
		var target =
			$"shadow:{shadowMethod.AssemblyName}:{shadowMethod.TypeName}::{shadowMethod.MethodName}";
		if (!bindings.TryAdd(
			member,
			new FrameworkBinding(
				member,
				FrameworkBindingKind.ShadowMethod,
				target,
				effects,
				ShadowMethod: shadowMethod,
				PreservesVirtualDispatch: preservesVirtualDispatch)))
		{
			throw new InvalidOperationException(
				$"Duplicate framework binding identity '{member.DisplayName}'.");
		}
	}

	private static void AddManagedBody(
		IDictionary<FrameworkMemberId, FrameworkBinding> bindings,
		FrameworkMemberId member,
		string target,
		FrameworkEffectSummary effects)
	{
		if (!bindings.TryAdd(
				member,
				new FrameworkBinding(
					member,
					FrameworkBindingKind.ManagedBody,
					target,
					effects)))
		{
			throw new InvalidOperationException(
				$"Duplicate framework binding identity '{member.DisplayName}'.");
		}
	}

	private static void AddPlatform(
		IDictionary<FrameworkMemberId, FrameworkBinding> bindings,
		FrameworkMemberId member,
		string target,
		FrameworkShadowMethod shadowMethod,
		FrameworkEffectSummary effects)
	{
		if (!bindings.TryAdd(
				member,
				new FrameworkBinding(
					member,
					FrameworkBindingKind.PlatformOperation,
					target,
					effects,
					ShadowMethod: shadowMethod)))
		{
			throw new InvalidOperationException(
				$"Duplicate framework binding identity '{member.DisplayName}'.");
		}
	}

	private static FrameworkBinding Intrinsic(
		FrameworkMemberId member,
		string target,
		FrameworkEffectSummary effects) =>
		new(member, FrameworkBindingKind.Intrinsic, target, effects);

	private static FrameworkBinding Shadow(
		FrameworkMemberId member,
		FrameworkShadowMethod shadowMethod,
		FrameworkEffectSummary effects,
		bool preservesVirtualDispatch = false) =>
		new(
			member,
			FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadowMethod.AssemblyName}:{shadowMethod.TypeName}::{shadowMethod.MethodName}",
			effects,
			ShadowMethod: shadowMethod,
			PreservesVirtualDispatch: preservesVirtualDispatch);

	private static FrameworkEffectSummary Effects(
		FrameworkEffects effects,
		params FrameworkFeature[] features) =>
		new(effects, features);
}
