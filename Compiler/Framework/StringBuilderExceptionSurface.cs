/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderExceptionSurface
{
	private static readonly FrameworkTypeId Void = FrameworkTypeId.Primitive("System.Void");
	private static readonly FrameworkTypeId Text = FrameworkTypeId.Primitive("System.String");
	private static readonly FrameworkTypeId Integer = FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId Parameter = FrameworkTypeId.GenericMethodParameter(0);
	internal static readonly IReadOnlyList<FrameworkMemberId> Members = [
		Constructor("System.ArgumentOutOfRangeException", [Text, Text]),
		Constructor("System.ArgumentOutOfRangeException", [Text]),
		Constructor("System.ArgumentOutOfRangeException", [Text, FrameworkTypeId.Primitive("System.Object"), Text]),
		Constructor("System.ArgumentException", [Text, Text]),
		Constructor("System.ArgumentException", [Text]),
		Constructor("System.ArgumentNullException", [Text]),
		Constructor("System.NotSupportedException", [Text]),
		Constructor("System.InvalidOperationException", [Text]),
		Constructor("System.InvalidOperationException", []),
		Constructor("System.IndexOutOfRangeException", []),
		Method("System.ThrowHelper", "ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen", 0, 0, Void, []),
		Constructor("System.FormatException", []),
		Constructor("System.FormatException", [Text]),
		Method("System.ArgumentNullException", "Throw", 0, 0, Void, [Text]),
		Method("System.ArgumentNullException", "ThrowIfNull", 0, 0, Void, [FrameworkTypeId.Primitive("System.Object"), Text]),
		Constructor("System.SystemException", [Text]), Constructor("System.Exception", [Text]),
		Constructor("System.OutOfMemoryException", []),
		Method("System.Exception", "set_HResult", 0x20, 0, Void, [Integer]),
		Method("System.ArgumentOutOfRangeException", "ThrowIfNegative", 0x10, 1, Void, [Parameter, Text]),
		Method("System.ArgumentOutOfRangeException", "ThrowIfNegativeOrZero", 0x10, 1, Void, [Parameter, Text]),
		Method("System.ArgumentOutOfRangeException", "ThrowNegative", 0x10, 1, Void, [Parameter, Text]),
		Method("System.ArgumentOutOfRangeException", "ThrowNegativeOrZero", 0x10, 1, Void, [Parameter, Text]),
		Method("System.ArgumentException", "ThrowIfNullOrEmpty", 0, 0, Void, [Text, Text]),
		Method("System.ArgumentException", "ThrowNullOrEmptyException", 0, 0, Void, [Text, Text]),
		Method("System.String", "IsNullOrEmpty", 0, 0, FrameworkTypeId.Primitive("System.Boolean"), [Text]),
		Method("System.ThrowHelper", "ThrowArgumentNullException", 0, 0, Void, [FrameworkTypeId.Named("System.Runtime", "System.ExceptionArgument")]),
		Constructor("System.NotSupportedException", []),
		Method("System.ThrowHelper", "ThrowFormatIndexOutOfRange", 0, 0, Void, []),
		Method("System.ThrowHelper", "ThrowInvalidOperationException_InvalidOperation_NoValue", 0, 0, Void, []),
		Method("System.ThrowHelper", "ThrowFormatInvalidString", 0, 0, Void, []),
		Method("System.ThrowHelper", "ThrowFormatInvalidString", 0, 0, Void, [Integer, FrameworkTypeId.Named("System.Runtime", "System.ExceptionResource")]),
		Method("System.ThrowHelper", "ThrowArgumentOutOfRangeException", 0, 0, Void, [FrameworkTypeId.Named("System.Runtime", "System.ExceptionArgument"), FrameworkTypeId.Named("System.Runtime", "System.ExceptionResource")]),
		Method("System.ThrowHelper", "GetArgumentOutOfRangeException", 0, 0, FrameworkTypeId.Named("System.Runtime", "System.ArgumentOutOfRangeException"),
			[FrameworkTypeId.Named("System.Runtime", "System.ExceptionArgument"), FrameworkTypeId.Named("System.Runtime", "System.ExceptionResource")])
	];

	internal static bool IsPublicCallbackConstructor(FrameworkMemberId referenced)
	{
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		return member.Equals(Constructor("System.InvalidOperationException", [Text])) ||
			member.Equals(Constructor("System.Exception", [Text])) ||
			member.Equals(Constructor("System.InvalidOperationException", [])) ||
			member.Equals(Constructor("System.NotSupportedException", []));
	}

	internal static bool IsPublicValidationProperty(FrameworkMemberId referenced) => FrameworkImplementationProfile.Canonicalize(referenced).Equals(
		Method("System.ArgumentException", "get_ParamName", 0x20, 0, Text, [])) || FrameworkImplementationProfile.Canonicalize(referenced).Equals(
		Method("System.ArgumentOutOfRangeException", "get_ActualValue", 0x20, 0, FrameworkTypeId.Primitive("System.Object"), []));

	internal static bool Contains(FrameworkMemberId referenced)
	{
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		if (member.MethodTypeArguments.Length != 0 &&
			!(member.MethodTypeArguments is [var argument] && (argument.Equals(Integer) || argument.Equals(Parameter)))) return false;
		return Members.Any(definition => definition.DeclaringType.Equals(member.DeclaringType) && definition.Name == member.Name &&
			definition.Signature.Equals(member.Signature) &&
			(member.Signature.GenericParameterCount == 1 || member.MethodTypeArguments.Length == 0));
	}

	internal static bool IsIntegerPredicate(FrameworkMemberId referenced)
	{
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		return member.DeclaringType.Kind == FrameworkTypeKind.GenericInstantiation &&
			member.DeclaringType.ElementType!.Equals(FrameworkTypeId.Named("System.Runtime", "System.Numerics.INumberBase`1")) &&
			member.DeclaringType.GenericArguments is [var argument] && (argument.Equals(Integer) || argument.Equals(Parameter)) &&
			member.Name is "IsNegative" or "IsZero" && member.MethodTypeArguments.Length == 0 &&
			member.Signature.Header == 0 && member.Signature.GenericParameterCount == 0 && member.Signature.RequiredParameterCount == 1 &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Boolean")) &&
			member.Signature.ParameterTypes.Length == 1 &&
			(member.Signature.ParameterTypes[0].Equals(FrameworkTypeId.GenericTypeParameter(0)) || member.Signature.ParameterTypes[0].Equals(Integer));
	}

	private static FrameworkMemberId Constructor(string owner, FrameworkTypeId[] parameters) => Method(owner, ".ctor", 0x20, 0, Void, parameters);
	private static FrameworkMemberId Method(string owner, string name, byte header, int arity, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		new(FrameworkTypeId.Named("System.Runtime", owner), name, new FrameworkMethodSignatureId(header, arity, parameters.Length, result, parameters));
}
