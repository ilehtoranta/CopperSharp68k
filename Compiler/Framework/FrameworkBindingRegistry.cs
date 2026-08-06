/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Metadata;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Framework;

internal readonly record struct FrameworkBindingContext(
	string TypeName,
	MethodSignature<CilType> CilSignature,
	CilType? ConstructedDeclaringType,
	bool IsSupportedNullableType,
	IReadOnlyList<CilType>? MethodTypeArguments = null,
	IReadOnlyList<bool?>? MethodTypeArgumentContainsReferences = null);

internal static class FrameworkBindingRegistry
{
	private static readonly FrameworkTypeId Void =
		FrameworkTypeId.Primitive("System.Void");
	private static readonly FrameworkTypeId Boolean =
		FrameworkTypeId.Primitive("System.Boolean");
	private static readonly FrameworkTypeId Int32 =
		FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId Char =
		FrameworkTypeId.Primitive("System.Char");
	private static readonly FrameworkTypeId Object =
		FrameworkTypeId.Primitive("System.Object");
	private static readonly FrameworkTypeId String =
		FrameworkTypeId.Primitive("System.String");
	private static readonly FrameworkTypeId FrameworkStringComparison =
		Named("System.Runtime", "System.StringComparison");
	private static readonly FrameworkTypeId GenericType0 =
		FrameworkTypeId.GenericTypeParameter(0);
	private static readonly FrameworkTypeId GenericMethod0 =
		FrameworkTypeId.GenericMethodParameter(0);
	private static readonly FrameworkTypeId NullableDefinition =
		Named("System.Runtime", "System.Nullable`1");
	private static readonly FrameworkTypeId SpanDefinition =
		Named("System.Runtime", "System.Span`1");
	private static readonly FrameworkTypeId SpanOfChar =
		FrameworkTypeId.GenericInstantiation(SpanDefinition, [Char]);
	private static readonly FrameworkTypeId ReadOnlySpanDefinition =
		Named("System.Runtime", "System.ReadOnlySpan`1");
	private static readonly FrameworkTypeId ReadOnlySpanOfChar =
		FrameworkTypeId.GenericInstantiation(ReadOnlySpanDefinition, [Char]);
	private static readonly FrameworkTypeId ReadOnlySpanOfGenericMethod0 =
		FrameworkTypeId.GenericInstantiation(
			ReadOnlySpanDefinition,
			[GenericMethod0]);

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

		if (TryBindNullable(member, context) is { } nullable)
		{
			return nullable;
		}

		if (TryBindSpan(member, context) is { } span)
		{
			return span;
		}

		if (TryBindReadOnlySpan(member, context) is { } readOnlySpan)
		{
			return readOnlySpan;
		}

		if (TryBindMemoryExtensions(member, context) is { } memoryExtensions)
		{
			return memoryExtensions;
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

		if (member.Name == "Invoke" && context.CilSignature.Header.IsInstance)
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

	private static IReadOnlyDictionary<FrameworkMemberId, FrameworkBinding>
		CreateFrameworkBindings()
	{
		var bindings = new Dictionary<FrameworkMemberId, FrameworkBinding>();
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
		AddShadow(
			bindings,
			Member(
				"System.Runtime",
				"System.Math",
				"Abs",
				isInstance: false,
				Int32,
				Int32),
			new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowMath",
				"Abs"),
			Effects(
				FrameworkEffects.MayThrow,
				FrameworkFeature.ManagedExceptions,
				FrameworkFeature.Numerics));
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

	private static FrameworkBinding? TryBindCompilerIntrinsic(
		FrameworkMemberId member,
		FrameworkBindingContext context)
	{
		var typeName = context.TypeName;
		var name = member.Name;
		var signature = context.CilSignature;

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
				(name == "get_Address" || name == "ToAddress") &&
				signature.ParameterTypes.Length <= 1)
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

	private static FrameworkBinding Intrinsic(
		FrameworkMemberId member,
		string target,
		FrameworkEffectSummary effects) =>
		new(member, FrameworkBindingKind.Intrinsic, target, effects);

	private static FrameworkEffectSummary Effects(
		FrameworkEffects effects,
		params FrameworkFeature[] features) =>
		new(effects, features);
}
