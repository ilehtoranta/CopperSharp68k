/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Metadata;

namespace CopperSharp.Compiler.Framework;

/// <summary>
/// Compiler-owned admission and substitution policy for the first pinned
/// CoreLib slice. Artifact manifests deliberately cannot change this policy.
/// </summary>
internal static class FrameworkImplementationProfile
{
	private const string ContractAssembly = "System.Runtime";
	private const string ImplementationAssembly = "System.Private.CoreLib";
	private const string StopwatchType = "System.Diagnostics.Stopwatch";
	private const string TimeSpanType = "System.TimeSpan";
	private const string ObjectType = "System.Object";
	private const string ExceptionType = "System.Exception";
	private const string ExternalExceptionType = "System.Runtime.InteropServices.ExternalException";
	private const string CultureInfoType = "System.Globalization.CultureInfo";
	private const string SystemResourceType = "System.SR";
	private static readonly FrameworkTypeId CharacterCopyArrayType = FrameworkTypeId.Named(ContractAssembly, "System.Array");
	private static readonly FrameworkMemberId CharacterCopyCaller = new(
		FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray"), "CopyCharacters",
		new FrameworkMethodSignatureId(0, 0, 3, FrameworkTypeId.Primitive("System.Void"),
			[CharacterCopyArrayType, CharacterCopyArrayType, FrameworkTypeId.Primitive("System.Int32")]));
	internal static readonly IReadOnlyList<string> StringBuilderResourceGetters = [
		"get_OutOfMemory_StringTooLong",
		"get_Arg_EmptySpan", "get_Arg_LongerThanSrcString", "get_ArgumentOutOfRange_Capacity",
		"get_ArgumentOutOfRange_IndexLength", "get_ArgumentOutOfRange_IndexMustBeLess",
		"get_ArgumentOutOfRange_IndexMustBeLessOrEqual", "get_ArgumentOutOfRange_LengthGreaterThanCapacity",
		"get_ArgumentOutOfRange_OffsetOut", "get_ArgumentOutOfRange_SmallCapacity",
		"get_ArgumentOutOfRange_StartIndexLargerThanLength", "get_Arg_ArgumentOutOfRangeException",
		"get_ArgumentOutOfRange_Generic_MustBeNonNegative", "get_ArgumentOutOfRange_Generic_MustBeNonNegativeNonZero",
		"get_ArgumentNull_Generic", "get_Arg_NotSupportedException", "get_Arg_InvalidOperationException", "get_Arg_FormatException",
		"get_Arg_IndexOutOfRangeException", "get_InvalidOperation_EnumOpCantHappen", "get_Argument_EmptyString",
		"get_Format_IndexOutOfRange", "get_Format_InvalidString", "get_Format_InvalidStringWithOffsetAndReason",
		"get_Argument_DestinationTooShort", "get_Argument_BadFormatSpecifier", "get_ArgumentOutOfRange_Generic_MustBeLessOrEqual",
		"get_Argument_InvalidGroupSize", "get_InvalidOperation_ReadOnly", "get_InvalidOperation_NoValue", "get_ArgumentOutOfRange_Range", "get_InvalidOperation_EnumFailedVersion",
		"get_InvalidOperation_EnumEnded", "get_InvalidOperation_EnumNotStarted"
	];

	private static readonly HashSet<string> StopwatchMembers = new(StringComparer.Ordinal)
	{
		".ctor",
		"Start",
		"StartNew",
		"Stop",
		"Reset",
		"Restart",
		"get_IsRunning",
		"get_ElapsedTicks"
	};

	private static readonly HashSet<string> StopwatchTargetOverrides = new(StringComparer.Ordinal)
	{
		"GetTimestamp",
		"GetElapsedTime",
		"get_Elapsed",
		"get_ElapsedMilliseconds"
	};

	private static readonly HashSet<string> TimeSpanMembers = new(StringComparer.Ordinal)
	{
		"get_Ticks",
		"op_Equality",
		"op_Inequality",
		"op_LessThan",
		"op_LessThanOrEqual",
		"op_GreaterThan",
		"op_GreaterThanOrEqual"
	};

	private static readonly HashSet<string> TimeSpanTargetOverrides = new(StringComparer.Ordinal)
	{
		".ctor",
		"FromTicks",
		"get_Days",
		"get_Hours",
		"get_Minutes",
		"get_Seconds",
		"get_Milliseconds",
		"get_TotalDays",
		"get_TotalHours",
		"get_TotalMinutes",
		"get_TotalSeconds",
		"get_TotalMilliseconds"
	};

	public static FrameworkMemberId Canonicalize(FrameworkMemberId member)
	{
		if (!string.Equals(member.AssemblyName, ImplementationAssembly, StringComparison.Ordinal))
		{
			return member;
		}
		return new FrameworkMemberId(
			CanonicalizeType(member.DeclaringType),
			member.Name,
			new FrameworkMethodSignatureId(
				member.Signature.Header,
				member.Signature.GenericParameterCount,
				member.Signature.RequiredParameterCount,
				CanonicalizeType(member.Signature.ReturnType),
				member.Signature.ParameterTypes.Select(CanonicalizeType).ToArray()),
			member.MethodTypeArguments.Select(CanonicalizeType).ToArray());
	}

	public static bool TryCreatePinnedStringBuilderBinding(
		FrameworkMemberId referencedMember,
		FrameworkBinding? fallback,
		FrameworkImplementationPackCatalog catalog,
		FrameworkMemberId? implementationCaller,
		out FrameworkBinding binding)
	{
		binding = null!;
		if (!catalog.IsPinnedStringBuilderInput) return false;
		if (IsExactStaticMethod(Canonicalize(referencedMember), "System.String", "CreateFromChar", FrameworkTypeId.Primitive("System.String"), [FrameworkTypeId.Primitive("System.Char")]) &&
			implementationCaller?.AssemblyName == ImplementationAssembly &&
			IsExactStaticMethod(Canonicalize(implementationCaller), "System.Char", "ToString", FrameworkTypeId.Primitive("System.String"), [FrameworkTypeId.Primitive("System.Char")]))
		{
			if (!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			binding = binding with { EffectSummary = new FrameworkEffectSummary(
				FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]) };
			return true;
		}
		if (IsExactStaticMethod(Canonicalize(referencedMember), "System.Char", "ToString", FrameworkTypeId.Primitive("System.String"), [FrameworkTypeId.Primitive("System.Char")]) &&
			implementationCaller?.AssemblyName == ImplementationAssembly &&
			IsExactInstanceMethod(Canonicalize(implementationCaller), "System.Char", "ToString", FrameworkTypeId.Primitive("System.String"), []))
		{
			if (!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			binding = binding with { EffectSummary = new FrameworkEffectSummary(
				FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]) };
			return true;
		}
		if (StringBuilderConcatenationSurface.Contains(referencedMember) ||
			StringBuilderConcatenationSurface.ContainsHelper(referencedMember) && StringBuilderConcatenationSurface.IsOwnedCaller(implementationCaller))
		{
			if (!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			binding = binding with { EffectSummary = new FrameworkEffectSummary(
				FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]) };
			return true;
		}
		if (StringBuilderFloatingSurface.ContainsScalarPredicate(referencedMember))
			return StringBuilderFloatingSurface.TryCreateScalarPredicateBinding(referencedMember, out binding);
		if (StringBuilderFloatingSurface.ContainsInterface(referencedMember) && StringBuilderFloatingSurface.IsOwnedCaller(implementationCaller))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderFloatingSurface.IsPublic(referencedMember) ||
			(StringBuilderFloatingSurface.ContainsFormatter(referencedMember) || StringBuilderFloatingSurface.ContainsHelper(referencedMember)) && StringBuilderFloatingSurface.IsOwnedCaller(implementationCaller))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderFormattingInterfaces.Contains(referencedMember) || StringBuilderEnumerationInterfaces.Contains(referencedMember))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderExceptionSurface.IsPublicCallbackConstructor(referencedMember))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderExceptionSurface.IsPublicValidationProperty(referencedMember))
		{
			if (!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			binding = binding with { EffectSummary = new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory,
				[FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedStrings]) };
			return true;
		}
		// Native virtual dispatch retains the default slot as well as reachable overrides.
		if (implementationCaller?.Equals(new FrameworkMemberId(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinDispatch"),
			"ToString", new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []))) == true &&
			IsExactInstanceMethod(Canonicalize(referencedMember), "System.NotSupportedException", ".ctor", FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.String")]))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (IsCharacterArrayMemoryProjection(referencedMember))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		var ownedCaller = IsOwnedStringBuilderCaller(implementationCaller);
		if (implementationCaller?.Equals(new FrameworkMemberId(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray"), "ClearDecimals",
			new FrameworkMethodSignatureId(0, 0, 3, FrameworkTypeId.Primitive("System.Void"),
				[CharacterCopyArrayType, FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]))) == true &&
			(IsExactInstanceMethod(Canonicalize(referencedMember), "System.ArgumentNullException", ".ctor", FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.String")]) ||
			 IsExactInstanceMethod(Canonicalize(referencedMember), "System.NotSupportedException", ".ctor", FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.String")]) ||
			 IsExactInstanceMethod(Canonicalize(referencedMember), "System.IndexOutOfRangeException", ".ctor", FrameworkTypeId.Primitive("System.Void"), [])))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderBoxedEnumSurface.Contains(referencedMember) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var enumBinding) && enumBinding.ShadowMethod?.TypeName == "CopperSharp.Runtime.ShadowBoxedEnum")
		{
			binding = enumBinding;
			return true;
		}
		var decimalListCaller = StringBuilderDecimalListSurface.IsOwnedCaller(implementationCaller);
		if (StringBuilderBoxedEnumSurface.IsRuntimeCaller(implementationCaller) &&
			(StringBuilderExceptionSurface.Contains(referencedMember) || IsStringTryCopyTo(referencedMember)))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderDecimalListSurface.IsPublic(referencedMember) || decimalListCaller && StringBuilderDecimalListSurface.IsHelper(referencedMember))
		{
			if (!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			binding = binding with { EffectSummary = StringBuilderDecimalListSurface.Effects };
			return true;
		}
		if ((decimalListCaller || StringBuilderDecimalListSurface.IsExceptionCaller(implementationCaller)) &&
			(StringBuilderExceptionSurface.Contains(referencedMember) || StringBuilderDecimalListSurface.IsExceptionHelper(referencedMember) || IsStringBuilderFormattingPrimitive(Canonicalize(referencedMember))))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderDecimalSurface.IsPublic(referencedMember) ||
			StringBuilderDecimalSurface.Contains(referencedMember) && StringBuilderDecimalSurface.IsOwnedCaller(implementationCaller))
		{
			if (!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			binding = binding with { EffectSummary = StringBuilderDecimalSurface.Effects };
			return true;
		}
		if ((StringBuilderDecimalSurface.IsOwnedCaller(implementationCaller) || StringBuilderDecimalSurface.IsValidationCaller(implementationCaller)) &&
			(StringBuilderDecimalSurface.IsValidationDependency(referencedMember) || StringBuilderDecimalSurface.IsComparisonDependency(referencedMember) || StringBuilderExceptionSurface.Contains(referencedMember)))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		var compositeCaller = StringBuilderCompositeSurface.IsOwnedCaller(implementationCaller) || StringBuilderCompositeSurface.IsHelperCaller(implementationCaller);
		if (compositeCaller && StringBuilderCompositeSurface.IsHelper(referencedMember))
		{
			if (!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			binding = binding with { EffectSummary = StringBuilderCompositeSurface.Effects };
			return true;
		}
		if (compositeCaller && (StringBuilderExceptionSurface.Contains(referencedMember) || IsStringBuilderFormattingPrimitive(Canonicalize(referencedMember))))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderCompositeSurface.Contains(referencedMember) &&
			(StringBuilderCompositeSurface.IsPublic(referencedMember) || ownedCaller || StringBuilderCompositeSurface.IsOwnedCaller(implementationCaller)))
		{
			if (!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			binding = binding with { EffectSummary = StringBuilderCompositeSurface.Effects };
			return true;
		}
		if (IsExactInstanceMethod(Canonicalize(referencedMember), "System.Globalization.NumberFormatInfo", ".ctor", FrameworkTypeId.Primitive("System.Void"), []))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (implementationCaller?.Equals(new FrameworkMemberId(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowValueStringBuilder"),
			"Grow", new FrameworkMethodSignatureId(0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32")]))) == true &&
			IsExactInstanceMethod(Canonicalize(referencedMember), "System.ArgumentOutOfRangeException", ".ctor", FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.String")]))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderInterpolatedHandlerSurface.IsOwnedCaller(implementationCaller) && implementationCaller!.Name == "AppendLiteral" &&
			IsStringTryCopyTo(referencedMember)) return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderNumericSurface.IsOwnedCaller(implementationCaller) && IsStringTryCopyTo(referencedMember))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderInterpolatedHandlerSurface.Contains(referencedMember) && (ownedCaller || StringBuilderInterpolatedHandlerSurface.IsOwnedCaller(implementationCaller)))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (ownedCaller && StringBuilderObjectBufferSurface.IsConstructor(referencedMember))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderReplacementSurface.Contains(referencedMember) && (ownedCaller || StringBuilderReplacementSurface.IsOwnedCaller(implementationCaller)))
		{
			if (fallback?.Kind == FrameworkBindingKind.Intrinsic) { binding = fallback; return true; }
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		}
		var stringSpanCaller = implementationCaller?.AssemblyName == ImplementationAssembly &&
			(IsStringSpanProjection(implementationCaller) || IsStringSpanThrowHelper(implementationCaller));
		if (IsStringSpanProjection(referencedMember))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (stringSpanCaller && IsStringSpanThrowHelper(referencedMember))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (stringSpanCaller && StringBuilderExceptionSurface.Contains(referencedMember))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderNumericSurface.Contains(referencedMember) && StringBuilderNumericSurface.IsOwnedCaller(implementationCaller))
		{
			if (fallback?.Kind == FrameworkBindingKind.Intrinsic) { binding = fallback; return true; }
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		}
		if (StringBuilderNumericSurface.IsOwnedCaller(implementationCaller) && StringBuilderExceptionSurface.Contains(referencedMember))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderNumericSurface.IsOwnedCaller(implementationCaller) && Canonicalize(referencedMember).Equals(new FrameworkMemberId(
			FrameworkTypeId.Named(ContractAssembly, "System.ThrowHelper"), "ThrowFormatException_BadFormatSpecifier",
			new FrameworkMethodSignatureId(0, 0, 0, FrameworkTypeId.Primitive("System.Void"), []))))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (IsCultureStorageCaller(implementationCaller) && StringBuilderExceptionSurface.Contains(referencedMember) &&
			referencedMember.Name == ".ctor" && Canonicalize(referencedMember).DeclaringType.FullMetadataName is
			"System.ArgumentNullException" or "System.NotSupportedException" or "System.InvalidOperationException")
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		var canonical = Canonicalize(referencedMember);
		if (StringBuilderNumberSettingsSurface.IsPublic(canonical) ||
			StringBuilderNumberSettingsSurface.IsHelper(canonical) && StringBuilderNumberSettingsSurface.IsOwnedCaller(implementationCaller))
		{
			if (!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			binding = binding with { EffectSummary = StringBuilderNumberSettingsSurface.Effects };
			return true;
		}
		if (StringBuilderNumberSettingsSurface.IsOwnedCaller(implementationCaller) && StringBuilderExceptionSurface.Contains(referencedMember))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderNumberSettingsSurface.IsOwnedCaller(implementationCaller) && StringBuilderNumberSettingsSurface.IsRangeThrow(referencedMember))
		{
			if (!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			binding = binding with { EffectSummary = StringBuilderNumberSettingsSurface.Effects };
			return true;
		}
		if (IsNumberFormatProviderEntry(canonical) || IsNumberFormatGetFormat(canonical) || IsNumberFormatSettingGetter(canonical))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (IsNumberFormatProviderHelper(canonical) && implementationCaller?.AssemblyName == ImplementationAssembly &&
			IsNumberFormatProviderEntry(implementationCaller))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (IsCultureStorageCaller(implementationCaller) && implementationCaller!.Name == "GetNumberFormat" && IsExactInstanceMethod(canonical,
			"System.Globalization.NumberFormatInfo", ".ctor", FrameworkTypeId.Primitive("System.Void"), []))
			return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (IsOwnedExceptionCaller(implementationCaller) && implementationCaller!.Name == "GetArgumentOutOfRangeException" &&
			IsExactStaticMethod(canonical, "System.ThrowHelper", "GetArgumentName", FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Named(ContractAssembly, "System.ExceptionArgument")]))
		{
			if (!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			binding = binding with { EffectSummary = StringBuilderCompositeSurface.Effects };
			return true;
		}
		if (IsStringBuilderFormattingPrimitive(Canonicalize(referencedMember)))
			return (ownedCaller || StringBuilderNumericSurface.IsOwnedCaller(implementationCaller)) && TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (IsAsciiDigitRangePredicate(Canonicalize(referencedMember)))
			return implementationCaller?.AssemblyName == ImplementationAssembly &&
				IsStringBuilderFormattingPrimitive(Canonicalize(implementationCaller)) && implementationCaller.Name == "IsAsciiDigit" &&
				TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (IsCharacterSpanFill(referencedMember, constructed: true))
			return ownedCaller && TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
		if (StringBuilderExceptionSurface.Contains(referencedMember) ||
			(IsOwnedExceptionCaller(implementationCaller) && StringBuilderExceptionSurface.IsIntegerPredicate(referencedMember)))
		{
			var copyConstructor = (IsCharacterCopyCaller(implementationCaller) || implementationCaller?.Equals(new FrameworkMemberId(
				FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray"), "CloneInt32",
				new FrameworkMethodSignatureId(0, 0, 1, FrameworkTypeId.Primitive("System.Object"), [CharacterCopyArrayType]))) == true || implementationCaller?.Equals(new FrameworkMemberId(
				FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray"), "CopyCompositeSegments",
				new FrameworkMethodSignatureId(0, 0, 3, FrameworkTypeId.Primitive("System.Void"),
					[CharacterCopyArrayType, CharacterCopyArrayType, FrameworkTypeId.Primitive("System.Int32")]))) == true || implementationCaller?.Equals(new FrameworkMemberId(
				FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray"), "CopyDecimals",
				new FrameworkMethodSignatureId(0, 0, 3, FrameworkTypeId.Primitive("System.Void"),
					[CharacterCopyArrayType, CharacterCopyArrayType, FrameworkTypeId.Primitive("System.Int32")]))) == true) && referencedMember.Name == ".ctor" &&
				(Canonicalize(referencedMember).DeclaringType.FullMetadataName is "System.ArgumentException" or "System.ArgumentNullException" or
					"System.ArgumentOutOfRangeException" or "System.NotSupportedException") &&
				referencedMember.Signature.ParameterTypes.SequenceEqual(new[] { FrameworkTypeId.Primitive("System.String") });
			if ((!ownedCaller && !IsOwnedExceptionCaller(implementationCaller) && !copyConstructor && !StringBuilderReplacementSurface.IsOwnedCaller(implementationCaller)) ||
				!TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			return true;
		}
		if (IsStringBuilderResourceGetter(referencedMember))
		{
			if ((!ownedCaller && !IsOwnedExceptionCaller(implementationCaller) && !StringBuilderNumberSettingsSurface.IsOwnedCaller(implementationCaller) && !decimalListCaller) || !TryCreatePinnedBinding(referencedMember, fallback, true, out binding)) return false;
			return true;
		}
		if (!StringBuilderFrameworkSurface.TryGetEffects(referencedMember, out _)) return false;
		if (!StringBuilderFrameworkSurface.TryGetPublicDefinition(referencedMember, out _) &&
			!ownedCaller) return false;
		// The exact surface gate above bounds this use of the body materializer.
		// Dependencies still go through their own independent admission policy.
		return TryCreatePinnedBinding(referencedMember, fallback, true, out binding);
	}

	public static bool TryCreatePinnedStringBuilderDependencyBinding(
		FrameworkMemberId referencedMember,
		FrameworkImplementationPackCatalog catalog,
		FrameworkMemberId? implementationCaller,
		out FrameworkBinding binding)
	{
		binding = null!;
		// These two exact public wrappers must use the target newline even when
		// reached through the released interpolated-handler consumer bodies.
		if (catalog.IsPinnedStringBuilderInput &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var newLine) &&
			newLine.ShadowMethod is { TypeName: "CopperSharp.Runtime.ShadowStringBuilder", MethodName: "AppendLine" })
		{
			binding = newLine;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && implementationCaller?.AssemblyName == ImplementationAssembly &&
			IsExactStaticMethod(Canonicalize(implementationCaller), "System.String", "CreateFromChar", FrameworkTypeId.Primitive("System.String"), [FrameworkTypeId.Primitive("System.Char")]) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var characterTextLeaf) &&
			characterTextLeaf.Target == "intrinsic:runtime-allocate-string")
		{
			binding = characterTextLeaf;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && StringBuilderConcatenationSurface.IsMemoryCaller(implementationCaller) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var concatenationLeaf) &&
			concatenationLeaf.Target is "intrinsic:runtime-allocate-string" or "intrinsic:corelib-memmove-char" or "intrinsic:corelib-add-char-ref")
		{
			binding = concatenationLeaf;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && StringBuilderFloatingSurface.ContainsScalarPredicate(referencedMember))
			return StringBuilderFloatingSurface.TryCreateScalarPredicateBinding(referencedMember, out binding);
		if (catalog.IsPinnedStringBuilderInput && StringBuilderFloatingSurface.IsBitProjectionCall(referencedMember, implementationCaller))
			return StringBuilderFloatingSurface.TryCreateBitProjectionBinding(referencedMember, out binding);
		if (catalog.IsPinnedStringBuilderInput && StringBuilderFloatingSurface.IsCachedDataCall(referencedMember, implementationCaller))
			return TryCreateTargetRuntimeOverride(referencedMember, true, out binding);
		if (catalog.IsPinnedStringBuilderInput && StringBuilderFloatingSurface.IsByteTablePointerCall(referencedMember, implementationCaller))
			return TryCreateTargetRuntimeOverride(referencedMember, true, out binding);
		if (catalog.IsPinnedStringBuilderInput &&
			(StringBuilderFloatingSurface.IsBigIntegerCaller(implementationCaller, "SetValue") || StringBuilderFloatingSurface.IsBigIntegerCaller(implementationCaller, "Clear")) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var bigIntegerMemory) &&
			(StringBuilderFloatingSurface.IsBigIntegerCaller(implementationCaller, "SetValue") && bigIntegerMemory.Target == "intrinsic:corelib-memmove-uint32" ||
			 StringBuilderFloatingSurface.IsBigIntegerCaller(implementationCaller, "Clear") && bigIntegerMemory.ShadowMethod is { TypeName: "CopperSharp.Runtime.ShadowBuffer", MethodName: "ZeroMemoryInternal" }))
		{
			binding = bigIntegerMemory;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && StringBuilderBoxedEnumSurface.Contains(referencedMember) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var enumBinding) && enumBinding.ShadowMethod?.TypeName == "CopperSharp.Runtime.ShadowBoxedEnum")
		{
			binding = enumBinding;
			return true;
		}
		var compositeReference = referencedMember;
		if (implementationCaller is not null && StringBuilderCompositeSurface.IsHelperCaller(implementationCaller) &&
			(Canonicalize(implementationCaller).DeclaringType.Equals(StringBuilderCompositeSurface.SpanToString.DeclaringType) ||
			 Canonicalize(implementationCaller).DeclaringType.Equals(StringBuilderCompositeSurface.MutableSpanToString.DeclaringType)) &&
			(referencedMember.MethodTypeArguments.SequenceEqual([FrameworkTypeId.GenericTypeParameter(0), FrameworkTypeId.Primitive("System.Char")]) ||
			 referencedMember.MethodTypeArguments.SequenceEqual([FrameworkTypeId.GenericTypeParameter(0)])))
			compositeReference = new FrameworkMemberId(referencedMember.DeclaringType, referencedMember.Name, referencedMember.Signature,
				referencedMember.MethodTypeArguments.Select(argument => argument.Equals(FrameworkTypeId.GenericTypeParameter(0)) ? FrameworkTypeId.Primitive("System.Char") : argument).ToArray());
		if (catalog.IsPinnedStringBuilderInput &&
			(StringBuilderCompositeSurface.IsOwnedCaller(implementationCaller) || StringBuilderCompositeSurface.IsHelperCaller(implementationCaller)) &&
			TryCreateTargetRuntimeOverride(compositeReference, true, out var compositeLeaf) &&
			compositeLeaf.Target is "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCharacterSpans::IndexOfAny" or
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowValueStringBuilder::Grow" or
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowValueStringBuilder::Dispose" or
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowStringData::FromCharacters" or "intrinsic:corelib-memmove-char" or
				"intrinsic:readonly-span-from-ref-length:char" or "intrinsic:ref-cast")
		{
			binding = compositeLeaf with { Member = referencedMember };
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && implementationCaller?.Equals(new FrameworkMemberId(
			FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCustomNumberFormatting"), "FormatMagnitude",
			new FrameworkMethodSignatureId(0, 0, 4, FrameworkTypeId.Primitive("System.String"), [
				FrameworkTypeId.Primitive("System.UInt64"), FrameworkTypeId.Primitive("System.Boolean"),
				FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")]),
				FrameworkTypeId.Named(ContractAssembly, "System.Globalization.NumberFormatInfo") ]))) == true &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var customText) &&
			customText.Target == "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowStringData::FromCharacters")
		{
			binding = customText;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && (StringBuilderDecimalSurface.IsRuntimeCaller(implementationCaller) || StringBuilderBoxedEnumSurface.IsRuntimeCaller(implementationCaller)) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var decimalText) &&
			decimalText.Target == "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowStringData::FromCharacters")
		{
			binding = decimalText;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && IsHandlerReflectionCaller(implementationCaller) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var handlerReflection) &&
			handlerReflection.Target is "intrinsic:runtime-type-from-handle" or "intrinsic:runtime-object-get-type" or "intrinsic:runtime-type-not-equals")
		{
			binding = handlerReflection;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && implementationCaller?.AssemblyName == ImplementationAssembly && IsStringTryCopyTo(implementationCaller) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var stringCopy) &&
			stringCopy.Target is "intrinsic:corelib-memmove-char" or "intrinsic:corelib-string-data" or "intrinsic:corelib-char-span-data")
		{
			binding = stringCopy;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && (IsOwnedStringBuilderCaller(implementationCaller) || StringBuilderInterpolatedHandlerSurface.IsOwnedCaller(implementationCaller)) &&
			StringBuilderInterpolatedHandlerSurface.Contains(referencedMember) && Canonicalize(referencedMember).Name is "Clear" or "GrowCore")
			return TryCreateTargetRuntimeOverride(referencedMember, true, out binding);
		if ((catalog.IsPinnedStringBuilderInput || catalog.EnableUnlistedManagedBodies) && (implementationCaller?.Equals(new FrameworkMemberId(
			FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinDispatch"), "ToString",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []))) == true ||
			implementationCaller?.Equals(new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.ValueType"), "ToString",
				new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []))) == true))
		{
			var canonical = Canonicalize(referencedMember);
			if (IsExactInstanceMethod(canonical, "System.Object", "GetType", FrameworkTypeId.Named(ContractAssembly, "System.Type"), []))
				return TryCreateTargetRuntimeOverride(referencedMember, true, out binding);
			if (IsExactInstanceMethod(canonical, "System.Object", "ToString", FrameworkTypeId.Primitive("System.String"), []))
			{
				var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowRuntimeType", "GetObjectFallbackName");
				binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod, $"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
					new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.ReadsManagedMemory,
						[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]),
					Reason: "The exact object fallback formats its canonical emitted type object.", ShadowMethod: shadow);
				return true;
			}
		}
		if (catalog.IsPinnedStringBuilderInput && IsOwnedExceptionCaller(implementationCaller) &&
			implementationCaller!.Name is "ThrowFormatInvalidString" or "GetArgumentOutOfRangeException" && implementationCaller.Signature.ParameterTypes.Length == 2 &&
			IsExactStaticMethod(Canonicalize(referencedMember), "System.ThrowHelper", "GetResourceString", FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Named(ContractAssembly, "System.ExceptionResource")]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowSystemResources", "GetFormattingResourceString");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}", new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect,
					[FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedGc]),
				Reason: "Verified formatting resource identifiers map to deterministic target resource keys.", ShadowMethod: shadow);
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && (IsOwnedStringBuilderCaller(implementationCaller) || StringBuilderObjectBufferSurface.IsOwnedCaller(implementationCaller)) &&
			StringBuilderObjectBufferSurface.IsHelper(referencedMember))
			return TryCreateTargetRuntimeOverride(referencedMember, true, out binding);
		if (catalog.IsPinnedStringBuilderInput && IsReferenceArraySpanConstructor(referencedMember))
			return TryCreateTargetRuntimeOverride(referencedMember, true, out binding);
		if (catalog.IsPinnedStringBuilderInput && IsOwnedStringBuilderCaller(implementationCaller) &&
			Canonicalize(implementationCaller!).Name == "AppendFormat" && TryCreateTargetRuntimeOverride(referencedMember, true, out var formattingType) &&
			formattingType.Target == "intrinsic:runtime-type-from-handle")
		{
			binding = formattingType;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && (IsOwnedStringBuilderCaller(implementationCaller) || StringBuilderReplacementSurface.IsOwnedCaller(implementationCaller)) &&
			StringBuilderReplacementSurface.Contains(referencedMember) && TryCreateTargetRuntimeOverride(referencedMember, true, out var replacementLeaf) &&
			replacementLeaf.ShadowMethod?.TypeName == "CopperSharp.Runtime.ShadowValueListBuilder`1")
		{
			binding = replacementLeaf;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && IsCharacterArraySpanProjection(referencedMember))
			return TryCreateTargetRuntimeOverride(referencedMember, true, out binding);
		if (catalog.IsPinnedStringBuilderInput && implementationCaller?.AssemblyName == ImplementationAssembly &&
			IsStringSpanProjection(implementationCaller) && TryCreateTargetRuntimeOverride(referencedMember, true, out var stringSpanLeaf) &&
			stringSpanLeaf.Target is "intrinsic:corelib-string-data" or "intrinsic:corelib-add-char-ref" or "intrinsic:readonly-span-from-ref-length:char")
		{
			binding = stringSpanLeaf;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && StringBuilderNumericSurface.IsOwnedCaller(implementationCaller) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var numericLeaf) &&
			(numericLeaf.Target?.StartsWith("shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowNumberFormatCharacters::", StringComparison.Ordinal) == true ||
			 numericLeaf.Target is "intrinsic:corelib-char-span-data" or "intrinsic:corelib-byte-span-data" or "intrinsic:address-of-ref" or
				"intrinsic:corelib-memmove-char" or "intrinsic:span-from-pointer:char" or "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCustomNumberFormatting::WriteTwoDigits" ||
			 StringBuilderNumericSurface.Contains(referencedMember) && numericLeaf.Target is
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowValueListBuilder`1::Grow" or
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowValueListBuilder`1::Dispose"))
		{
			binding = numericLeaf;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && StringBuilderDecimalSurface.IsOwnedCaller(implementationCaller) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var decimalLeaf) &&
			decimalLeaf.ShadowMethod is { TypeName: "CopperSharp.Runtime.ShadowDecimalFormatting", MethodName: "FormatDecimal" or "TryFormatDecimal" })
		{
			binding = decimalLeaf;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && IsIntegralTryFormatCaller(implementationCaller) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var integral) &&
			integral.Target is "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowNumberFormatting::TryFormatInt32" or
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowNumberFormatting::TryFormatUInt32" or
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowNumberFormatting::TryFormatInt64" or
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowNumberFormatting::TryFormatUInt64")
		{
			binding = integral;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && IsIntegralToStringCaller(implementationCaller) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var integralText) &&
			integralText.Target is "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowNumberFormatting::Int32ToDecStr" or
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowNumberFormatting::UInt32ToDecStr" or
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowNumberFormatting::Int64ToDecStr" or
				"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowNumberFormatting::UInt64ToDecStr")
		{
			binding = integralText;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && IsIntegralFormattedToStringCaller(implementationCaller) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var formattedInteger) &&
			formattedInteger.ShadowMethod is { TypeName: "CopperSharp.Runtime.ShadowNumberFormatting", MethodName: "FormatInt32" or "FormatUInt32" or "FormatInt64" or "FormatUInt64" })
		{
			binding = formattedInteger;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && IsNumberFormatProviderCaller(implementationCaller) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var providerLeaf) &&
			(providerLeaf.Target is "intrinsic:runtime-object-get-type" or "intrinsic:runtime-type-from-handle" ||
			 providerLeaf.Target == "intrinsic:object-reference-equals" && Canonicalize(referencedMember).DeclaringType.FullMetadataName == "System.Type" ||
			 implementationCaller!.DeclaringType.FullMetadataName == CultureInfoType && implementationCaller.Name == "GetFormat" &&
			 providerLeaf.Target == "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::GetDateTimeFormat"))
		{
			binding = providerLeaf;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && TryCreateTargetRuntimeOverride(referencedMember, true, out var culture) &&
			(culture.Target is "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::GetCurrentCulture" or
			 "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::SetCurrentCulture" or
			 "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::GetDefaultThreadCurrentCulture" or
			 "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::SetDefaultThreadCurrentCulture" or
			 "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::GetInvariantCulture" or
			 "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::InitializeName" or
			 "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::InitializeNameWithOverrides" or
			 "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::GetNumberFormat" or
			 "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::GetDateTimeFormat" or
			 "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::SetNumberFormat" ||
			 IsCultureStorageCaller(implementationCaller) && implementationCaller!.Name == "InitializeName" && culture.Target is "intrinsic:runtime-object-get-type" or
			 "intrinsic:runtime-type-from-handle" or "intrinsic:runtime-type-not-equals"))
		{
			binding = culture;
			return true;
		}
		var resourceCaller = implementationCaller?.AssemblyName == ImplementationAssembly && IsStringBuilderResourceGetter(implementationCaller);
		if (catalog.IsPinnedStringBuilderInput && implementationCaller?.AssemblyName == ImplementationAssembly &&
			IsCharacterSpanFill(implementationCaller, constructed: false) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var fillLeaf) && fillLeaf.Target == "intrinsic:corelib-fill-char")
		{
			binding = fillLeaf;
			return true;
		}
		if (catalog.IsPinnedStringBuilderInput && IsOwnedExceptionCaller(implementationCaller) &&
			TryCreateTargetRuntimeOverride(referencedMember, true, out var exceptionLeaf) &&
			exceptionLeaf.Target is "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowSystemResources::GetOutOfMemoryMessage" or
			 "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowSystemResources::FormatResourceString")
		{
			binding = exceptionLeaf;
			return true;
		}
		if (!catalog.IsPinnedStringBuilderInput || (!IsOwnedStringBuilderCaller(implementationCaller) && !resourceCaller) ||
			!TryCreateTargetRuntimeOverride(referencedMember, true, out var candidate)) return false;
		if (resourceCaller)
		{
			if (candidate.Target != "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowSystemResources::GetResourceString") return false;
			binding = candidate;
			return true;
		}
		// Reuse exact, independently tested character-memory substitutions, not
		// the broader experimental set of target overrides.
		if (candidate.Target is not ("intrinsic:corelib-memmove-char" or "intrinsic:corelib-char-array-data" or
			"intrinsic:corelib-add-char-ref" or "intrinsic:corelib-string-data" or
			"intrinsic:runtime-allocate-string" or
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowStringData::FromCharacters" or
			"intrinsic:span-from-ref-length:char" or "intrinsic:span-from-array-range:char" or
			"intrinsic:span-from-array-range-value:char" or
			"intrinsic:span-from-array-ctor:char" or "intrinsic:readonly-span-from-array-ctor:char" or
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCharacterSpans::Replace" or
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCharacterSpans::IndexOf" or
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCharacterSpans::IndexOfAny" or
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCharacterSpans::EqualsOrdinal" or
			"intrinsic:corelib-char-span-data" or
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowArray::CopyCharacters" or
			"intrinsic:readonly-span-from-ref-length:char" or "intrinsic:span-from-array-start:char" or
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowArray::AllocateUninitializedArray")) return false;
		binding = candidate;
		return true;
	}

	private static bool IsOwnedExceptionCaller(FrameworkMemberId? caller) =>
		caller?.AssemblyName == ImplementationAssembly && (StringBuilderExceptionSurface.Contains(caller) || StringBuilderConcatenationSurface.ContainsHelper(caller) || StringBuilderDecimalSurface.IsValidationCaller(caller) || StringBuilderNumberSettingsSurface.IsRangeCaller(caller) || StringBuilderDecimalListSurface.IsExceptionCaller(caller) ||
			Canonicalize(caller).Equals(new FrameworkMemberId(FrameworkTypeId.Named(ContractAssembly, "System.ThrowHelper"), "ThrowFormatException_BadFormatSpecifier",
				new FrameworkMethodSignatureId(0, 0, 0, FrameworkTypeId.Primitive("System.Void"), []))));

	internal static bool IsIntegralToStringCaller(FrameworkMemberId? member)
	{
		if (member?.AssemblyName != ImplementationAssembly) return false;
		var canonical = Canonicalize(member);
		var owner = canonical.DeclaringType.FullMetadataName;
		return owner is "System.SByte" or "System.Byte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" &&
			IsExactInstanceMethod(canonical, owner, "ToString", FrameworkTypeId.Primitive("System.String"), []);
	}

	internal static bool IsIntegralTryFormatCaller(FrameworkMemberId? member)
	{
		if (member?.AssemblyName != ImplementationAssembly) return false;
		var canonical = Canonicalize(member);
		var owner = canonical.DeclaringType.FullMetadataName;
		if (owner is not ("System.SByte" or "System.Byte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64")) return false;
		var character = FrameworkTypeId.Primitive("System.Char");
		return IsExactInstanceMethod(canonical, owner, "TryFormat", FrameworkTypeId.Primitive("System.Boolean"), [
			FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [character]),
			FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Int32")),
			FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [character]),
			FrameworkTypeId.Named(ContractAssembly, "System.IFormatProvider")]);
	}

	private static bool IsIntegralFormattedToStringCaller(FrameworkMemberId? member)
	{
		if (member?.AssemblyName != ImplementationAssembly) return false;
		var canonical = Canonicalize(member);
		var owner = canonical.DeclaringType.FullMetadataName;
		return owner is "System.SByte" or "System.Byte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" &&
			IsExactInstanceMethod(canonical, owner, "ToString", FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Named(ContractAssembly, "System.IFormatProvider")]);
	}

	internal static bool IsNumberFormatSettingGetter(FrameworkMemberId member)
	{
		var canonical = Canonicalize(member);
		var text = canonical.Name is "get_NegativeSign" or "get_PositiveSign" or "get_NumberDecimalSeparator" or "get_NumberGroupSeparator" or
			"get_CurrencyDecimalSeparator" or "get_CurrencyGroupSeparator" or "get_CurrencySymbol" or "get_PercentDecimalSeparator" or
			"get_PercentGroupSeparator" or "get_PercentSymbol" or "get_PerMilleSymbol" or "get_NaNSymbol" or "get_NegativeInfinitySymbol" or "get_PositiveInfinitySymbol";
		var integer = canonical.Name is "get_NumberDecimalDigits" or "get_NumberNegativePattern" or "get_CurrencyDecimalDigits" or
			"get_CurrencyNegativePattern" or "get_CurrencyPositivePattern" or "get_PercentDecimalDigits" or "get_PercentNegativePattern" or "get_PercentPositivePattern";
		return (text || integer) && IsExactInstanceMethod(canonical, "System.Globalization.NumberFormatInfo", canonical.Name,
			FrameworkTypeId.Primitive(text ? "System.String" : "System.Int32"), []);
	}

	internal static bool IsNumberFormatProviderEntry(FrameworkMemberId member) =>
		IsExactStaticMethod(Canonicalize(member), "System.Globalization.NumberFormatInfo", "GetInstance",
			FrameworkTypeId.Named(ContractAssembly, "System.Globalization.NumberFormatInfo"),
			[FrameworkTypeId.Named(ContractAssembly, "System.IFormatProvider")]) ||
		IsExactStaticMethod(Canonicalize(member), "System.Globalization.NumberFormatInfo", "get_CurrentInfo",
			FrameworkTypeId.Named(ContractAssembly, "System.Globalization.NumberFormatInfo"), []) ||
		IsExactStaticMethod(Canonicalize(member), "System.Globalization.NumberFormatInfo", "get_InvariantInfo",
			FrameworkTypeId.Named(ContractAssembly, "System.Globalization.NumberFormatInfo"), []);

	internal static bool IsNumberFormatProviderHelper(FrameworkMemberId member) =>
		IsExactStaticMethod(Canonicalize(member), "System.Globalization.NumberFormatInfo", "<GetInstance>g__GetProviderNonNull|58_0",
			FrameworkTypeId.Named(ContractAssembly, "System.Globalization.NumberFormatInfo"),
			[FrameworkTypeId.Named(ContractAssembly, "System.IFormatProvider")]);

	internal static bool IsNumberFormatGetFormat(FrameworkMemberId member) =>
		IsExactInstanceMethod(Canonicalize(member), CultureInfoType, "GetFormat", FrameworkTypeId.Primitive("System.Object"),
			[FrameworkTypeId.Named(ContractAssembly, "System.Type")]) ||
		IsExactInstanceMethod(Canonicalize(member), "System.Globalization.NumberFormatInfo", "GetFormat", FrameworkTypeId.Primitive("System.Object"),
			[FrameworkTypeId.Named(ContractAssembly, "System.Type")]);

	internal static bool IsNumberFormatProviderCaller(FrameworkMemberId? member) =>
		member?.AssemblyName == ImplementationAssembly &&
		(IsNumberFormatProviderEntry(member) || IsNumberFormatProviderHelper(member) || IsNumberFormatGetFormat(member));

	internal static bool IsCultureStorageCaller(FrameworkMemberId? caller)
	{
		if (caller is null || !caller.DeclaringType.Equals(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCultureInfo"))) return false;
		var type = FrameworkTypeId.Named(ContractAssembly, CultureInfoType);
		var number = FrameworkTypeId.Named(ContractAssembly, "System.Globalization.NumberFormatInfo");
		var none = FrameworkTypeId.Primitive("System.Void");
		var text = FrameworkTypeId.Primitive("System.String");
		var expected = caller.Name switch {
			"GetCurrentCulture" or "GetDefaultThreadCurrentCulture" or "GetInvariantCulture" => new FrameworkMethodSignatureId(0, 0, 0, type, []),
			"SetCurrentCulture" or "SetDefaultThreadCurrentCulture" or "FreezeCulture" => new FrameworkMethodSignatureId(0, 0, 1, none, [type]),
			"FreezeNumberFormat" => new FrameworkMethodSignatureId(0, 0, 1, none, [number]),
			"InitializeName" => new FrameworkMethodSignatureId(0x20, 0, 1, none, [text]),
			"InitializeNameWithOverrides" => new FrameworkMethodSignatureId(0x20, 0, 2, none, [text, FrameworkTypeId.Primitive("System.Boolean")]),
			"GetNumberFormat" => new FrameworkMethodSignatureId(0x20, 0, 0, number, []),
			"SetNumberFormat" => new FrameworkMethodSignatureId(0x20, 0, 1, none, [number]),
			_ => null
		};
		return expected is not null && caller.Signature.Equals(expected) && caller.MethodTypeArguments.Length == 0;
	}

	private static bool IsStringBuilderFormattingPrimitive(FrameworkMemberId member) =>
		IsExactInstanceMethod(member, "System.Boolean", "ToString", FrameworkTypeId.Primitive("System.String"), []) ||
		IsExactStaticMethod(member, "System.Char", "IsAsciiDigit", FrameworkTypeId.Primitive("System.Boolean"), [FrameworkTypeId.Primitive("System.Char")]);

	private static bool IsAsciiDigitRangePredicate(FrameworkMemberId member) =>
		IsExactStaticMethod(member, "System.Char", "IsBetween", FrameworkTypeId.Primitive("System.Boolean"),
			[FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.Primitive("System.Char")]);

	private static bool IsCharacterCopyCaller(FrameworkMemberId? caller) =>
		caller is not null && Canonicalize(caller).Equals(CharacterCopyCaller);

	private static bool IsStringTryCopyTo(FrameworkMemberId member) =>
		IsExactInstanceMethod(Canonicalize(member), "System.String", "TryCopyTo", FrameworkTypeId.Primitive("System.Boolean"),
			[FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [FrameworkTypeId.Primitive("System.Char")])]);

	internal static bool IsHandlerReflectionCaller(FrameworkMemberId? caller) => caller is not null &&
		((IsOwnedStringBuilderCaller(caller) && caller.Name == "AppendCustomFormatter") ||
		 (StringBuilderInterpolatedHandlerSurface.IsOwnedCaller(caller) && caller.Name is "HasCustomFormatter" or "AppendCustomFormatter"));

	private static bool IsCharacterSpanFill(FrameworkMemberId referenced, bool constructed)
	{
		var member = Canonicalize(referenced);
		var definition = FrameworkTypeId.Named(ContractAssembly, "System.Span`1");
		var character = FrameworkTypeId.Primitive("System.Char");
		return member.DeclaringType.Equals(constructed ? FrameworkTypeId.GenericInstantiation(definition, [character]) : definition) &&
			member.Name == "Fill" && member.MethodTypeArguments.Length == 0 && member.Signature.Header == 0x20 &&
			member.Signature.GenericParameterCount == 0 && member.Signature.RequiredParameterCount == 1 &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) && member.Signature.ParameterTypes.Length == 1 &&
			(member.Signature.ParameterTypes[0].Equals(FrameworkTypeId.GenericTypeParameter(0)) || constructed && member.Signature.ParameterTypes[0].Equals(character));
	}

	private static bool IsStringSpanThrowHelper(FrameworkMemberId referenced)
	{
		var member = Canonicalize(referenced);
		var argument = FrameworkTypeId.Named(ContractAssembly, "System.ExceptionArgument");
		return IsExactStaticMethod(member, "System.ThrowHelper", "ThrowArgumentNullException", FrameworkTypeId.Primitive("System.Void"), [argument]) ||
			IsExactStaticMethod(member, "System.ThrowHelper", "ThrowArgumentOutOfRangeException", FrameworkTypeId.Primitive("System.Void"), [argument]) ||
			IsExactStaticMethod(member, "System.ThrowHelper", "GetArgumentName", FrameworkTypeId.Primitive("System.String"), [argument]);
	}

	internal static bool IsStringSpanProjection(FrameworkMemberId referenced)
	{
		var member = Canonicalize(referenced);
		if (member.DeclaringType.Equals(FrameworkTypeId.Named("System.Memory", "System.MemoryExtensions")))
			member = new FrameworkMemberId(FrameworkTypeId.Named(ContractAssembly, "System.MemoryExtensions"),
				member.Name, member.Signature, member.MethodTypeArguments);
		var text = FrameworkTypeId.Primitive("System.String");
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var span = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")]);
		return IsExactStaticMethod(member, "System.MemoryExtensions", "AsSpan", span, [text]) ||
			IsExactStaticMethod(member, "System.MemoryExtensions", "AsSpan", span, [text, integer]) ||
			IsExactStaticMethod(member, "System.MemoryExtensions", "AsSpan", span, [text, integer, integer]);
	}

	internal static bool IsReferenceArraySpanConstructor(FrameworkMemberId referenced)
	{
		var member = Canonicalize(referenced);
		var owner = member.DeclaringType;
		if (owner.Kind != FrameworkTypeKind.GenericInstantiation ||
			!owner.ElementType!.Equals(FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1")) ||
			owner.GenericArguments is not [var element] ||
			!(element.Equals(FrameworkTypeId.Primitive("System.String")) || element.Equals(FrameworkTypeId.Primitive("System.Object")))) return false;
		var array = FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0));
		var integer = FrameworkTypeId.Primitive("System.Int32");
		return member.Name == ".ctor" && member.MethodTypeArguments.Length == 0 && member.Signature.Header == 0x20 &&
			member.Signature.GenericParameterCount == 0 && member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) &&
			(member.Signature.RequiredParameterCount == 1 && member.Signature.ParameterTypes.SequenceEqual(new[] { array }) ||
			 member.Signature.RequiredParameterCount == 3 && member.Signature.ParameterTypes.SequenceEqual(new[] { array, integer, integer }));
	}

	internal static bool IsCharacterArraySpanProjection(FrameworkMemberId referenced)
	{
		var member = Canonicalize(referenced);
		if (member.DeclaringType.Equals(FrameworkTypeId.Named("System.Memory", "System.MemoryExtensions")))
			member = new FrameworkMemberId(FrameworkTypeId.Named(ContractAssembly, "System.MemoryExtensions"),
				member.Name, member.Signature, member.MethodTypeArguments);
		var element = FrameworkTypeId.GenericMethodParameter(0);
		var array = FrameworkTypeId.SzArray(element);
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var span = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [element]);
		return IsExactCharacterMethod(member, "System.MemoryExtensions", "AsSpan", span, [array]) ||
			IsExactCharacterMethod(member, "System.MemoryExtensions", "AsSpan", span, [array, integer]) ||
			IsExactCharacterMethod(member, "System.MemoryExtensions", "AsSpan", span, [array, integer, integer]);
	}

	internal static bool IsCharacterArrayMemoryProjection(FrameworkMemberId referenced)
	{
		var member = Canonicalize(referenced);
		if (member.DeclaringType.Equals(FrameworkTypeId.Named("System.Memory", "System.MemoryExtensions")))
			member = new FrameworkMemberId(FrameworkTypeId.Named(ContractAssembly, "System.MemoryExtensions"),
				member.Name, member.Signature, member.MethodTypeArguments);
		var element = FrameworkTypeId.GenericMethodParameter(0);
		var array = FrameworkTypeId.SzArray(element);
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var memory = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Memory`1"), [element]);
		return IsExactCharacterMethod(member, "System.MemoryExtensions", "AsMemory", memory, [array]) ||
			IsExactCharacterMethod(member, "System.MemoryExtensions", "AsMemory", memory, [array, integer]) ||
			IsExactCharacterMethod(member, "System.MemoryExtensions", "AsMemory", memory, [array, integer, integer]);
	}

	private static bool IsOwnedStringBuilderCaller(FrameworkMemberId? caller) =>
		caller?.AssemblyName == ImplementationAssembly && StringBuilderFrameworkSurface.TryGetEffects(caller, out _);

	private static bool IsStringBuilderResourceGetter(FrameworkMemberId member) =>
		StringBuilderResourceGetters.Contains(member.Name) && IsExactStaticMethod(Canonicalize(member), SystemResourceType,
			member.Name, FrameworkTypeId.Primitive("System.String"), []);

	public static bool TryCreatePinnedBinding(
		FrameworkMemberId referencedMember,
		FrameworkBinding? fallback,
		bool enableUnlistedManagedBodies,
		out FrameworkBinding binding)
	{
		var canonicalMember = Canonicalize(referencedMember);
		var characterMemorySpan = enableUnlistedManagedBodies && IsCharacterMemorySpanGetter(canonicalMember);
		var isOriginalPinnedSlice = fallback is not null &&
			IsPinnedMember(canonicalMember) &&
			fallback.Member.Equals(canonicalMember);
		if (!IsFrameworkImplementationCandidate(referencedMember) ||
			(!isOriginalPinnedSlice && !enableUnlistedManagedBodies) ||
			(!isOriginalPinnedSlice && !characterMemorySpan && !IsStringSpanProjection(canonicalMember) &&
			 fallback?.Kind is FrameworkBindingKind.Intrinsic or
				 FrameworkBindingKind.PlatformOperation))
		{
			binding = null!;
			return false;
		}

		binding = new FrameworkBinding(
			referencedMember,
			FrameworkBindingKind.PinnedManagedBody,
			PinnedTarget(referencedMember),
			characterMemorySpan
				? new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayCollect | FrameworkEffects.ReadsManagedMemory,
					[FrameworkFeature.ManagedMemory, FrameworkFeature.Spans, FrameworkFeature.ManagedGc])
					: IsCharacterArrayMemoryProjection(canonicalMember)
						? new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.ReadsManagedMemory,
							[FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedMemory, FrameworkFeature.ManagedGc, FrameworkFeature.ManagedExceptions])
					: IsStringSpanProjection(canonicalMember) || IsStringSpanThrowHelper(canonicalMember)
						? new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.ReadsManagedMemory,
							[FrameworkFeature.ManagedStrings, FrameworkFeature.Spans, FrameworkFeature.ManagedGc, FrameworkFeature.ManagedExceptions])
					: StringBuilderObjectBufferSurface.IsConstructor(canonicalMember)
						? new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
							[FrameworkFeature.ManagedObjects, FrameworkFeature.Spans])
					: StringBuilderNumericSurface.Contains(canonicalMember) && !IsStringBuilderFormattingPrimitive(canonicalMember) || StringBuilderReplacementSurface.Contains(canonicalMember)
						? new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
							FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
							[FrameworkFeature.Spans, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc])
					: IsNumberFormatSettingGetter(canonicalMember)
						? new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory, [FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedStrings])
					: StringBuilderFrameworkSurface.TryGetEffects(canonicalMember, out var stringBuilderEffects)
						? stringBuilderEffects : StringBuilderFormattingInterfaces.Contains(canonicalMember) || StringBuilderEnumerationInterfaces.Contains(canonicalMember) || IsNumberFormatProviderEntry(canonicalMember) || IsNumberFormatProviderHelper(canonicalMember) || IsNumberFormatGetFormat(canonicalMember)
							? new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
								FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
								[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedStrings, FrameworkFeature.Spans, FrameworkFeature.ManagedGc])
							: IsExactInstanceMethod(canonicalMember, "System.Globalization.NumberFormatInfo", ".ctor", FrameworkTypeId.Primitive("System.Void"), [])
							? new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
								FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
								[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc])
							: IsStringBuilderFormattingPrimitive(canonicalMember)
							? new FrameworkEffectSummary(FrameworkEffects.None,
								canonicalMember.Name == "ToString" ? [FrameworkFeature.ManagedStrings] : [])
							: IsCharacterSpanFill(canonicalMember, constructed: true)
							? new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
								[FrameworkFeature.Spans, FrameworkFeature.ManagedMemory]) : IsStringBuilderResourceGetter(canonicalMember)
						? new FrameworkEffectSummary(FrameworkEffects.None, [FrameworkFeature.ManagedStrings])
						: StringBuilderExceptionSurface.Contains(canonicalMember)
							? new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory |
								(canonicalMember.Name.StartsWith("Throw", StringComparison.Ordinal) ? FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect : FrameworkEffects.None),
								[FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedGc])
							: fallback?.EffectSummary ?? FrameworkEffectSummary.None,
			Reason: "Reachable CIL body loaded from the verified framework implementation pack.",
			TypeInitializerPolicy: isOriginalPinnedSlice ||
				IsSystemResourceMember(canonicalMember)
				? FrameworkTypeInitializerPolicy.TargetOwned
				: FrameworkTypeInitializerPolicy.Implementation);
		return true;
	}

	public static bool IsPinnedBinding(FrameworkBinding binding) =>
		binding.Kind == FrameworkBindingKind.PinnedManagedBody &&
		IsFrameworkImplementationCandidate(binding.Member) &&
		string.Equals(
			binding.Target,
			PinnedTarget(binding.Member),
			StringComparison.Ordinal) &&
		(binding.TypeInitializerPolicy == FrameworkTypeInitializerPolicy.Implementation ||
		 (binding.TypeInitializerPolicy == FrameworkTypeInitializerPolicy.TargetOwned &&
		  (IsPinnedMember(Canonicalize(binding.Member)) ||
			   IsSystemResourceMember(Canonicalize(binding.Member)))));

	private static bool IsCharacterMemorySpanGetter(FrameworkMemberId member)
	{
		var definition = member.DeclaringType.ElementType;
		if (member.DeclaringType.Kind != FrameworkTypeKind.GenericInstantiation ||
			definition is not { Kind: FrameworkTypeKind.Named, AssemblyName: ContractAssembly } ||
			definition.MetadataName is not ("System.Memory`1" or "System.ReadOnlyMemory`1") ||
			!member.DeclaringType.GenericArguments.SequenceEqual([FrameworkTypeId.Primitive("System.Char")]) ||
			member.Name != "get_Span" || member.MethodTypeArguments.Length != 0 ||
			member.Signature.Header != 0x20 || member.Signature.GenericParameterCount != 0 ||
			member.Signature.RequiredParameterCount != 0 || member.Signature.ParameterTypes.Length != 0) return false;
		var span = FrameworkTypeId.Named(ContractAssembly, definition.MetadataName == "System.Memory`1" ? "System.Span`1" : "System.ReadOnlySpan`1");
		return member.Signature.ReturnType.Equals(FrameworkTypeId.GenericInstantiation(span, [FrameworkTypeId.GenericTypeParameter(0)])) ||
			member.Signature.ReturnType.Equals(FrameworkTypeId.GenericInstantiation(span, [FrameworkTypeId.Primitive("System.Char")]));
	}

	public static bool IsUnsupportedHardwareSupportGetter(FrameworkMemberId referencedMember)
	{
		var member = Canonicalize(referencedMember);
		// These verified foreign-architecture gates select BitOperations' scalar
		// software fallback. Intrinsic-only operation bodies are not m68k code.
		if (member.AssemblyName is not (ContractAssembly or "System.Runtime.Intrinsics") ||
			member.DeclaringType.Kind != FrameworkTypeKind.Named ||
			member.Name != "get_IsSupported" || member.MethodTypeArguments.Length != 0 ||
			member.Signature.Header != 0 || member.Signature.GenericParameterCount != 0 ||
			member.Signature.RequiredParameterCount != 0 || member.Signature.ParameterTypes.Length != 0 ||
			!member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Boolean"))) return false;
		var type = member.DeclaringType;
		if (type.DeclaringType is null)
			return type.MetadataName is "System.Runtime.Intrinsics.X86.Lzcnt" or "System.Runtime.Intrinsics.X86.X86Base" or
				"System.Runtime.Intrinsics.Arm.ArmBase";
		var parent = type.DeclaringType;
		return parent.Kind == FrameworkTypeKind.Named && parent.DeclaringType is null && parent.AssemblyName == member.AssemblyName &&
			(parent.MetadataName, type.MetadataName) is
				("System.Runtime.Intrinsics.X86.Lzcnt", "X64") or ("System.Runtime.Intrinsics.X86.X86Base", "X64") or
				("System.Runtime.Intrinsics.Arm.ArmBase", "Arm64");
	}

	public static bool TryCreateTargetRuntimeOverride(
		FrameworkMemberId referencedMember,
		bool enableUnlistedManagedBodies,
		out FrameworkBinding binding)
	{
		if (!enableUnlistedManagedBodies)
		{
			binding = null!;
			return false;
		}
		var member = Canonicalize(referencedMember);
		if (StringBuilderFloatingSurface.TryCreateBitProjectionBinding(referencedMember, out binding)) return true;
		if (member.Equals(StringBuilderFloatingSurface.BytePointerConstructor))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:span-from-pointer:byte",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow, [FrameworkFeature.Spans]),
				Reason: "The exact byte pointer span validates its length and retains no managed owner.");
			return true;
		}
		if (IsExactStaticMethod(member, "System.Runtime.InteropServices.NativeMemory", "Clear", FrameworkTypeId.Primitive("System.Void"),
			[FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Void")), FrameworkTypeId.Primitive("System.UIntPtr")]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowBuffer", "ZeroMemoryInternal");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod, $"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.WritesNativeMemory, [FrameworkFeature.NativeMemory]),
				Reason: "Target scalar byte clearing preserves the native pointer and byte count without allocation.", ShadowMethod: shadow);
			return true;
		}
		if (IsExactStaticMethod(member, "System.Runtime.CompilerServices.RuntimeHelpers", "ObjectHasComponentSize",
			FrameworkTypeId.Primitive("System.Boolean"), [FrameworkTypeId.Primitive("System.Object")]))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:runtime-object-has-component-size",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory,
					[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedStrings]),
				Reason: "Variable-sized target descriptors identify array and string component storage.");
			return true;
		}
		if (referencedMember.AssemblyName == ImplementationAssembly &&
			IsExactStaticMethod(member, "System.Environment", "GetProcessorCount", FrameworkTypeId.Primitive("System.Int32"), []))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.PlatformOperation,
				"platform:amiga-environment-processor-count",
				new FrameworkEffectSummary(FrameworkEffects.None, [FrameworkFeature.AmigaEnvironment]),
				Reason: "The private CoreLib processor-count leaf uses the existing target platform implementation.",
				ShadowMethod: new FrameworkShadowMethod("CopperSharp.Runtime.AmigaPal", "CopperSharp.Runtime.AmigaPal.EnvironmentPal", "GetProcessorCount"),
				TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		if (IsExactInstanceMethod(member, ObjectType, "MemberwiseClone", FrameworkTypeId.Primitive("System.Object"), []))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:runtime-object-memberwise-clone",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
					[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc]),
				Reason: "Target memberwise cloning copies the managed allocation header and payload while retaining the runtime descriptor.",
				TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		var enumParameter = FrameworkTypeId.GenericMethodParameter(0);
		var enumCharacters = FrameworkTypeId.Primitive("System.Char");
		if (member.MethodTypeArguments.Length == 1 && member.Name is "TryFormatUnconstrained" or "TryFormat" &&
			IsExactElementMethod(member, "System.Enum", member.Name, FrameworkTypeId.Primitive("System.Boolean"),
				[enumParameter, FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [enumCharacters]),
				 FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Int32")),
				 FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [enumCharacters])], member.MethodTypeArguments[0]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowEnumFormatting", "TryFormat");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
					[FrameworkFeature.ManagedStrings, FrameworkFeature.Spans, FrameworkFeature.ManagedGc]),
				Reason: "Target enum formatting uses verified integral constants and names without host reflection.", ShadowMethod: shadow,
				TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		var enumString = FrameworkTypeId.Primitive("System.String");
		var enumProvider = FrameworkTypeId.Named(ContractAssembly, "System.IFormatProvider");
		if (IsExactInstanceMethod(member, "System.Enum", "ToString", enumString, []) ||
			IsExactInstanceMethod(member, "System.Enum", "ToString", enumString, [enumString]) ||
			IsExactInstanceMethod(member, "System.Enum", "ToString", enumString, [enumProvider]) ||
			IsExactInstanceMethod(member, "System.Enum", "ToString", enumString, [enumString, enumProvider]) ||
			IsExactInstanceMethod(member, "System.Enum", "System.ISpanFormattable.TryFormat", FrameworkTypeId.Primitive("System.Boolean"),
				[FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [enumCharacters]),
				 FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Int32")),
				 FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [enumCharacters]), enumProvider]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowBoxedEnum",
				member.Name == "System.ISpanFormattable.TryFormat" ? "TryFormat" : "ToString");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
					[FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedObjects, FrameworkFeature.Spans, FrameworkFeature.ManagedGc]),
				Reason: "Boxed integral enum formatting uses target metadata and payloads without host reflection.",
				ShadowMethod: shadow, TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		var runtimeType = FrameworkTypeId.Named(ContractAssembly, "System.Type");
		if (IsExactInstanceMethod(member, "System.RuntimeType", "ToString", enumString, []))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowRuntimeType", "ToString");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory, [FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedStrings]),
				Reason: "Canonical target type objects carry names rendered from verified metadata.", ShadowMethod: shadow,
				TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		if (IsExactInstanceMethod(member, "System.Object", "GetType", runtimeType, []))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:runtime-object-get-type",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory, [FrameworkFeature.ManagedObjects]),
				Reason: "Emitted object descriptors identify canonical target type objects without host reflection.",
				TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		if (IsExactInstanceMethod(member, "System.Type", "get_IsEnum", FrameworkTypeId.Primitive("System.Boolean"), []))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:runtime-type-is-enum",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory, [FrameworkFeature.ManagedObjects]),
				Reason: "Canonical target type objects retain enum classification from verified compilation metadata.",
				TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		if (IsExactStaticMethod(member, "System.Type", "GetTypeFromHandle", runtimeType,
				[FrameworkTypeId.Named(ContractAssembly, "System.RuntimeTypeHandle")]))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:runtime-type-from-handle", FrameworkEffectSummary.None,
				Reason: "Target type handles identify canonical immortal type objects without host reflection initialization.",
				TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		if (IsExactStaticMethod(member, "System.Type", "op_Equality", FrameworkTypeId.Primitive("System.Boolean"), [runtimeType, runtimeType]) ||
			IsExactStaticMethod(member, "System.Type", "op_Inequality", FrameworkTypeId.Primitive("System.Boolean"), [runtimeType, runtimeType]))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				member.Name == "op_Equality" ? "intrinsic:object-reference-equals" : "intrinsic:runtime-type-not-equals",
				FrameworkEffectSummary.None, Reason: "Canonical target type objects compare by identity.",
				TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		var genericElement = FrameworkTypeId.GenericMethodParameter(0);
		var objectElement = FrameworkTypeId.Primitive("System.Object");
		var readonlyMethodObjectSpan = FrameworkTypeId.GenericInstantiation(
			FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [genericElement]);
		if (IsExactElementMethod(member, "System.Runtime.InteropServices.MemoryMarshal", "CreateReadOnlySpan",
				readonlyMethodObjectSpan, [FrameworkTypeId.ByReference(genericElement), FrameworkTypeId.Primitive("System.Int32")], objectElement))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:readonly-span-from-ref-length:object",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow, [FrameworkFeature.Spans]),
				Reason: "Target readonly object spans preserve the source byref owner.");
			return true;
		}
		if (member.DeclaringType.Equals(FrameworkTypeId.Named(ContractAssembly, "<PrivateImplementationDetails>")) &&
			member.MethodTypeArguments.Length == 2 && member.MethodTypeArguments[1].Equals(objectElement) &&
			(member.MethodTypeArguments[0].Equals(FrameworkTypeId.Named(ContractAssembly, "System.TwoObjects")) ||
			 member.MethodTypeArguments[0].Equals(FrameworkTypeId.Named(ContractAssembly, "System.ThreeObjects"))) &&
			member.Signature.Header == 0x10 && member.Signature.GenericParameterCount == 2 &&
			(member.Signature.RequiredParameterCount == 2 &&
			 member.Signature.ParameterTypes.SequenceEqual(new[] { FrameworkTypeId.ByReference(genericElement), FrameworkTypeId.Primitive("System.Int32") }) ||
			 member.Signature.RequiredParameterCount == 1 &&
			 member.Signature.ParameterTypes.SequenceEqual(new[] { FrameworkTypeId.ByReference(genericElement) })))
		{
			var second = FrameworkTypeId.GenericMethodParameter(1);
			var createsSpan = member.Name == "InlineArrayAsReadOnlySpan" && member.Signature.RequiredParameterCount == 2 && member.Signature.ReturnType.Equals(
				FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [second]));
			var elementRef = member.Name == "InlineArrayElementRef" && member.Signature.RequiredParameterCount == 2 && member.Signature.ReturnType.Equals(FrameworkTypeId.ByReference(second));
			var firstRef = member.Name == "InlineArrayFirstElementRef" && member.Signature.RequiredParameterCount == 1 && member.Signature.ReturnType.Equals(FrameworkTypeId.ByReference(second));
			if (createsSpan || elementRef || firstRef)
			{
				binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
					createsSpan ? "intrinsic:readonly-span-from-ref-length:object" : firstRef ? "intrinsic:ref-cast" : "intrinsic:corelib-add-int32-ref",
					new FrameworkEffectSummary(createsSpan ? FrameworkEffects.MayThrow : FrameworkEffects.None, [FrameworkFeature.Spans]),
					Reason: "Pinned object inline-array helpers lower at their caller so frame roots and byref owners remain precise.");
				return true;
			}
		}
		var character = FrameworkTypeId.Primitive("System.Char");
		if (StringBuilderNumericSurface.Contains(member) && member.Name == ".ctor" &&
			member.DeclaringType is { Kind: FrameworkTypeKind.GenericInstantiation, ElementType.FullMetadataName: "System.ReadOnlySpan`1" })
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:span-from-pointer:char",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow, [FrameworkFeature.Spans]),
				Reason: "Audited UTF-16 pointer spans use the target span constructor with length validation and no managed owner.");
			return true;
		}
		if (IsExactCharacterMethod(member, "System.Number", "WriteTwoDigits", FrameworkTypeId.Primitive("System.Void"),
			[FrameworkTypeId.Primitive("System.UInt32"), FrameworkTypeId.Pointer(genericElement)]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCustomNumberFormatting", "WriteTwoDigits");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedMemory]),
				Reason: "Target UTF-16 digit pairs are written directly without a host-endian lookup table.", ShadowMethod: shadow);
			return true;
		}
		if (member.DeclaringType.Equals(FrameworkTypeId.Named(ContractAssembly, "System.Globalization.NumberFormatInfo")) &&
			member.Name is "NegativeSignTChar" or "PositiveSignTChar" or "NumberDecimalSeparatorTChar" or "NumberGroupSeparatorTChar" or "PercentSymbolTChar" or "PerMilleSymbolTChar" or
				"CurrencySymbolTChar" or "CurrencyDecimalSeparatorTChar" or "CurrencyGroupSeparatorTChar" or "PercentDecimalSeparatorTChar" or "PercentGroupSeparatorTChar" &&
			member.Signature.Header == 0x30 && member.Signature.GenericParameterCount == 1 && member.Signature.RequiredParameterCount == 0 &&
			member.Signature.ParameterTypes.Length == 0 && member.MethodTypeArguments.SequenceEqual(new[] { character }) &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [genericElement])))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowNumberFormatCharacters", member.Name);
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory, [FrameworkFeature.Spans, FrameworkFeature.ManagedStrings]),
				Reason: "UTF-16 numeric settings expose their existing strings as owner-retaining character spans.", ShadowMethod: shadow);
			return true;
		}
		var integer = FrameworkTypeId.Primitive("System.Int32");
		if (IsExactStaticMethod(member, "System.Math", "Ceiling", FrameworkTypeId.Primitive("System.Double"),
			[FrameworkTypeId.Primitive("System.Double")]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowMath", "Ceiling");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}", FrameworkEffectSummary.None,
				Reason: "CoreLib numeric algorithms share the target's bit-preserving ceiling implementation.", ShadowMethod: shadow);
			return true;
		}
		if (member.MethodTypeArguments is [var initializedElement] && initializedElement.Kind == FrameworkTypeKind.Primitive &&
			initializedElement.MetadataName is ("System.Byte" or "System.SByte" or "System.Char" or "System.Int16" or "System.UInt16" or
				"System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double") &&
			IsExactElementMethod(member, "System.Runtime.CompilerServices.RuntimeHelpers", "CreateSpan",
				FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [genericElement]),
				[FrameworkTypeId.Named(ContractAssembly, "System.RuntimeFieldHandle")], initializedElement))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:initialized-data-span",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory, [FrameworkFeature.Spans]),
				Reason: "An immediate initialized field becomes an immutable span with elements encoded in target byte order.");
			return true;
		}
		var provider = FrameworkTypeId.Named(ContractAssembly, "System.IFormatProvider");
		var characterFormat = FrameworkTypeId.GenericInstantiation(
			FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [character]);
		var spanOfMethodElement = FrameworkTypeId.GenericInstantiation(
			FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [genericElement]);
		var readOnlySpanOfMethodElement = FrameworkTypeId.GenericInstantiation(
			FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [genericElement]);
		var characterSpanMember = member.DeclaringType.Equals(FrameworkTypeId.Named("System.Memory", "System.MemoryExtensions"))
			? new FrameworkMemberId(FrameworkTypeId.Named(ContractAssembly, "System.MemoryExtensions"),
				member.Name, member.Signature, member.MethodTypeArguments) : member;
		if (IsExactCharacterMethod(characterSpanMember, "System.MemoryExtensions", "Replace",
				FrameworkTypeId.Primitive("System.Void"), [spanOfMethodElement, genericElement, genericElement]) ||
			IsExactCharacterMethod(characterSpanMember, "System.MemoryExtensions", "IndexOf",
				integer, [readOnlySpanOfMethodElement, readOnlySpanOfMethodElement]) ||
			IsExactStaticMethod(characterSpanMember, "System.MemoryExtensions", "EqualsOrdinal",
				FrameworkTypeId.Primitive("System.Boolean"), [characterFormat, characterFormat]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowCharacterSpans", member.Name);
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory |
					(member.Name == "Replace" ? FrameworkEffects.WritesManagedMemory : FrameworkEffects.None),
					[FrameworkFeature.Spans]),
				Reason: "Target runtime performs ordinal UTF-16 span operations without host SIMD discovery.",
				ShadowMethod: shadow);
			return true;
		}
		var integerList = FrameworkTypeId.GenericInstantiation(
			FrameworkTypeId.Named(ContractAssembly, "System.Collections.Generic.ValueListBuilder`1"), [integer]);
		var characterList = FrameworkTypeId.GenericInstantiation(
			FrameworkTypeId.Named(ContractAssembly, "System.Collections.Generic.ValueListBuilder`1"), [character]);
		if (referencedMember.AssemblyName == ImplementationAssembly &&
			(IsExactInstanceMethod(member, "System.Text.ValueStringBuilder", "Grow", FrameworkTypeId.Primitive("System.Void"), [integer]) ||
			 IsExactInstanceMethod(member, "System.Text.ValueStringBuilder", "Dispose", FrameworkTypeId.Primitive("System.Void"), [])))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowValueStringBuilder", member.Name);
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory |
					(member.Name == "Grow" ? FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect : FrameworkEffects.None),
					[FrameworkFeature.Spans, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc]),
				Reason: "Target character buffers grow and release ordinary GC-owned arrays.", ShadowMethod: shadow);
			return true;
		}
		if (IsExactInstanceMethod(member, "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler", "GrowCore",
				FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.UInt32")]) ||
			IsExactInstanceMethod(member, "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler", "Clear",
				FrameworkTypeId.Primitive("System.Void"), []))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowInterpolatedStringHandlerBuffer", member.Name);
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory |
					(member.Name == "GrowCore" ? FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect : FrameworkEffects.None),
					[FrameworkFeature.Spans, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc]),
				Reason: "Target interpolated handlers grow and release temporary character buffers under ordinary GC ownership.", ShadowMethod: shadow);
			return true;
		}
		if ((member.DeclaringType.Equals(integerList) || member.DeclaringType.Equals(characterList)) && member.MethodTypeArguments.Length == 0 &&
			member.Signature.Header == 0x20 && member.Signature.GenericParameterCount == 0 &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) &&
			((member.Name == "Grow" && member.Signature.RequiredParameterCount == 1 &&
			  member.Signature.ParameterTypes.SequenceEqual(new[] { integer })) ||
			 (member.Name == "Dispose" && member.Signature.RequiredParameterCount == 0 &&
			  member.Signature.ParameterTypes.Length == 0)))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowValueListBuilder`1", member.Name);
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory |
					(member.Name == "Grow" ? FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect : FrameworkEffects.None),
					[FrameworkFeature.Spans, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc]),
				Reason: "Target runtime grows and releases integer and character lists with ordinary managed arrays instead of host pooling.",
				ShadowMethod: shadow);
			return true;
		}
		if (IsExactStaticMethod(member, "System.Array", "Copy", FrameworkTypeId.Primitive("System.Void"),
				[FrameworkTypeId.Named(ContractAssembly, "System.Array"), FrameworkTypeId.Named(ContractAssembly, "System.Array"), integer]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowArray", "CopyCharacters");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedArrays, FrameworkFeature.Spans]),
				Reason: "Target runtime copies character-array prefixes for StringBuilder chunk rebuilding; other array kinds are unsupported.",
				ShadowMethod: shadow);
			return true;
		}
		if (IsExactStaticMethod(member, "System.Array", "Copy", FrameworkTypeId.Primitive("System.Void"),
				[FrameworkTypeId.Named(ContractAssembly, "System.Array"), integer,
				 FrameworkTypeId.Named(ContractAssembly, "System.Array"), integer, integer]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray", "CopyInt32");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory |
					FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedArrays]),
				Reason: "Target runtime copies indexed Int32 array ranges with overlap and bounds validation; other array kinds are unsupported.", ShadowMethod: shadow);
			return true;
		}
		if (IsExactStaticMethod(member, "System.Array", "Clear", FrameworkTypeId.Primitive("System.Void"),
				[FrameworkTypeId.Named(ContractAssembly, "System.Array"), integer, integer]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowArray", "ClearPrimitive");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedArrays]),
				Reason: "Target runtime clears validated byte, character, Int32 and reference array ranges using its private array layout.", ShadowMethod: shadow);
			return true;
		}
		if (IsExactStaticMethod(member, "System.Buffer", "ZeroMemoryInternal", FrameworkTypeId.Primitive("System.Void"),
				[FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Void")), FrameworkTypeId.Primitive("System.UIntPtr")]) ||
			IsExactStaticMethod(member, "System.SpanHelpers", "ClearWithoutReferences", FrameworkTypeId.Primitive("System.Void"),
				[FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Byte")), FrameworkTypeId.Primitive("System.UIntPtr")]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowBuffer", member.Name);
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedMemory]),
				Reason: "Target runtime clears bytes without host native memory helpers or allocation.", ShadowMethod: shadow);
			return true;
		}
		if (IsExactInstanceMethod(member, "System.Text.StringBuilder", "AppendLine",
				FrameworkTypeId.Named(ContractAssembly, "System.Text.StringBuilder"), []) ||
			IsExactInstanceMethod(member, "System.Text.StringBuilder", "AppendLine",
				FrameworkTypeId.Named(ContractAssembly, "System.Text.StringBuilder"), [FrameworkTypeId.Primitive("System.String")]))
		{
			// Windows implementation packs embed CRLF in these IL bodies.
			// Route the two wrappers through the target's Environment.NewLine;
			// their string appends still use the verified CoreLib implementation.
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowStringBuilder", "AppendLine");
			if (!StringBuilderFrameworkSurface.TryGetEffects(member, out var appendLineEffects))
				throw new InvalidOperationException("StringBuilder.AppendLine must have an exact effect declaration.");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				appendLineEffects,
				Reason: "Target runtime appends its platform newline through the official CoreLib string append body.",
				ShadowMethod: shadow);
			return true;
		}
		// Public array extensions are also referenced through the System.Memory facade.
		var arraySpanMember = member.DeclaringType.Equals(FrameworkTypeId.Named("System.Memory", "System.MemoryExtensions"))
			? new FrameworkMemberId(FrameworkTypeId.Named(ContractAssembly, "System.MemoryExtensions"),
				member.Name, member.Signature, member.MethodTypeArguments)
			: member;
		var writableCharacterSpan = FrameworkTypeId.GenericInstantiation(
			FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [character]);
		var readonlyStringSpan = FrameworkTypeId.GenericInstantiation(
			FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.String")]);
		var readonlyObjectSpan = FrameworkTypeId.GenericInstantiation(
			FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Object")]);
		var readonlyReferenceElement = member.DeclaringType.Equals(readonlyStringSpan) ? "string"
			: member.DeclaringType.Equals(readonlyObjectSpan) ? "object" : null;
		var mutableReferenceElement = member.DeclaringType.Equals(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [FrameworkTypeId.Primitive("System.String")])) ? "string" :
			member.DeclaringType.Equals(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [FrameworkTypeId.Primitive("System.Object")])) ? "object" : null;
		if (mutableReferenceElement is not null && member.Name == ".ctor" && member.MethodTypeArguments.Length == 0 &&
			member.Signature.Header == 0x20 && member.Signature.GenericParameterCount == 0 && member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) &&
			(member.Signature.RequiredParameterCount == 1 && member.Signature.ParameterTypes.SequenceEqual(new[] { FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0)) }) ||
			 member.Signature.RequiredParameterCount == 3 && member.Signature.ParameterTypes.SequenceEqual(new[] { FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0)), integer, integer })))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				member.Signature.ParameterTypes.Length == 3 ? $"intrinsic:span-from-array-range-mutable:{mutableReferenceElement}" : $"intrinsic:span-from-array-ctor:{mutableReferenceElement}",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow, [FrameworkFeature.Spans, FrameworkFeature.ManagedArrays]),
				Reason: "Writable reference spans require exact array descriptors, retain their owner and preserve bounds/null validation.");
			return true;
		}
		if (readonlyReferenceElement is not null && member.Name == ".ctor" &&
			member.MethodTypeArguments.Length == 0 && member.Signature.Header == 0x20 &&
			member.Signature.GenericParameterCount == 0 &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) &&
			((member.Signature.RequiredParameterCount == 1 && member.Signature.ParameterTypes.SequenceEqual(new[]
				{ FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0)) })) ||
			 (member.Signature.RequiredParameterCount == 3 && member.Signature.ParameterTypes.SequenceEqual(new[]
				{ FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0)), integer, integer }))))
		{
			var ranged = member.Signature.ParameterTypes.Length == 3;
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				ranged ? $"intrinsic:span-from-array-range:{readonlyReferenceElement}" : $"intrinsic:readonly-span-from-array-ctor:{readonlyReferenceElement}",
				new FrameworkEffectSummary(ranged ? FrameworkEffects.MayThrow : FrameworkEffects.None,
					[FrameworkFeature.Spans, FrameworkFeature.ManagedArrays]),
				Reason: "Target runtime constructs an exact readonly reference-array span with bounds checks and a retained owner.");
			return true;
		}
		if ((member.DeclaringType.Equals(characterFormat) || member.DeclaringType.Equals(writableCharacterSpan)) &&
			member.Name == ".ctor" && member.MethodTypeArguments.Length == 0 &&
			member.Signature.Header == 0x20 && member.Signature.GenericParameterCount == 0 &&
			member.Signature.RequiredParameterCount == 1 &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) &&
			member.Signature.ParameterTypes.SequenceEqual(new[] { FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0)) }))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				member.DeclaringType.Equals(characterFormat)
					? "intrinsic:readonly-span-from-array-ctor:char" : "intrinsic:span-from-array-ctor:char",
				new FrameworkEffectSummary(FrameworkEffects.None, [FrameworkFeature.Spans, FrameworkFeature.ManagedArrays]),
				Reason: "Target runtime constructs a character-array span with a retained owner and null-array semantics.");
			return true;
		}
		if ((member.DeclaringType.Equals(characterFormat) || member.DeclaringType.Equals(writableCharacterSpan)) &&
			member.Name == ".ctor" &&
			member.MethodTypeArguments.Length == 0 && member.Signature.Header == 0x20 &&
			member.Signature.GenericParameterCount == 0 && member.Signature.RequiredParameterCount == 2 &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) &&
			member.Signature.ParameterTypes.SequenceEqual(new[]
			{
				FrameworkTypeId.ByReference(FrameworkTypeId.GenericTypeParameter(0)), integer
			}))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				member.DeclaringType.Equals(characterFormat)
					? "intrinsic:readonly-span-from-ref-length:char" : "intrinsic:span-from-ref-length:char",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow, [FrameworkFeature.Spans]),
				Reason: "Target runtime creates a length-checked character span and retains known byref owners.");
			return true;
		}
		if (IsCharacterArraySpanProjection(member))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				member.Signature.ParameterTypes.Length == 1
					? "intrinsic:span-from-array:char" : member.Signature.ParameterTypes.Length == 2
					? "intrinsic:span-from-array-start:char" : "intrinsic:span-from-array-range-value:char",
				new FrameworkEffectSummary(member.Signature.ParameterTypes.Length == 1
					? FrameworkEffects.None : FrameworkEffects.MayThrow, [FrameworkFeature.Spans, FrameworkFeature.ManagedArrays]),
				Reason: "Target runtime exposes a character-array span using its private header and retained owner.");
			return true;
		}
		if ((member.DeclaringType.Equals(writableCharacterSpan) || member.DeclaringType.Equals(characterFormat)) &&
			member.Name == ".ctor" && member.MethodTypeArguments.Length == 0 &&
			member.Signature.Header == 0x20 && member.Signature.GenericParameterCount == 0 &&
			member.Signature.RequiredParameterCount == 3 &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) &&
			member.Signature.ParameterTypes.SequenceEqual(new[]
			{
				FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0)), integer, integer
			}))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:span-from-array-range:char",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow, [FrameworkFeature.Spans, FrameworkFeature.ManagedArrays]),
				Reason: "Target runtime creates a bounds-checked writable or readonly character span and retains its array owner.");
			return true;
		}
		var decimalType = FrameworkTypeId.Named(ContractAssembly, "System.Decimal");
		var numberInfo = FrameworkTypeId.Named(ContractAssembly, "System.Globalization.NumberFormatInfo");
		if (IsExactStaticMethod(member, "System.Number", "FormatDecimal", FrameworkTypeId.Primitive("System.String"),
				[decimalType, characterFormat, numberInfo]) ||
			IsExactCharacterMethod(member, "System.Number", "TryFormatDecimal", FrameworkTypeId.Primitive("System.Boolean"),
				[decimalType, characterFormat, numberInfo, spanOfMethodElement, FrameworkTypeId.ByReference(integer)]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowDecimalFormatting", member.Name);
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
					[FrameworkFeature.ManagedStrings, FrameworkFeature.Spans, FrameworkFeature.ManagedGc]),
				Reason: "Target decimal adapters extract logical significand words and use the verified UTF-16 CoreLib renderers.",
				ShadowMethod: shadow, TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		if (IsExactCharacterMethod(member, "System.Number", "TryFormatInt32",
				FrameworkTypeId.Primitive("System.Boolean"),
				[integer, integer, characterFormat, provider,
				 FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [genericElement]),
				 FrameworkTypeId.ByReference(integer)]) ||
			IsExactStaticMethod(member, "System.Number", "FormatInt32",
				FrameworkTypeId.Primitive("System.String"),
				[integer, integer, FrameworkTypeId.Primitive("System.String"), provider]) ||
			IsExactStaticMethod(member, "System.Number", "Int32ToDecStr",
				FrameworkTypeId.Primitive("System.String"), [integer]) ||
			IsExactCharacterMethod(member, "System.Number", "TryFormatUInt32",
				FrameworkTypeId.Primitive("System.Boolean"),
				[FrameworkTypeId.Primitive("System.UInt32"), characterFormat, provider,
				 FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [genericElement]),
				 FrameworkTypeId.ByReference(integer)]) ||
			IsExactStaticMethod(member, "System.Number", "FormatUInt32",
				FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.UInt32"), FrameworkTypeId.Primitive("System.String"), provider]) ||
			IsExactStaticMethod(member, "System.Number", "UInt32ToDecStr",
				FrameworkTypeId.Primitive("System.String"), [FrameworkTypeId.Primitive("System.UInt32")]) ||
			IsExactCharacterMethod(member, "System.Number", "TryFormatInt64", FrameworkTypeId.Primitive("System.Boolean"),
				[FrameworkTypeId.Primitive("System.Int64"), characterFormat, provider,
				 FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [genericElement]),
				 FrameworkTypeId.ByReference(integer)]) ||
			IsExactCharacterMethod(member, "System.Number", "TryFormatUInt64", FrameworkTypeId.Primitive("System.Boolean"),
				[FrameworkTypeId.Primitive("System.UInt64"), characterFormat, provider,
				 FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Span`1"), [genericElement]),
				 FrameworkTypeId.ByReference(integer)]) ||
			IsExactStaticMethod(member, "System.Number", "FormatInt64", FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.Int64"), FrameworkTypeId.Primitive("System.String"), provider]) ||
			IsExactStaticMethod(member, "System.Number", "FormatUInt64", FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.UInt64"), FrameworkTypeId.Primitive("System.String"), provider]) ||
			IsExactStaticMethod(member, "System.Number", "Int64ToDecStr", FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.Int64")]) ||
			IsExactStaticMethod(member, "System.Number", "UInt64ToDecStr", FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.UInt64")]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowNumberFormatting", member.Name);
			var effects = FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory |
				FrameworkEffects.WritesManagedMemory | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect;
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(effects, [FrameworkFeature.ManagedStrings, FrameworkFeature.Spans, FrameworkFeature.ManagedGc]),
				Reason: "Target runtime formats standard integral text and routes custom integral patterns through the verified UTF-16 CoreLib renderer with resolved explicit or ambient numeric settings.",
				ShadowMethod: shadow);
			return true;
		}
		var genericElementRef = FrameworkTypeId.ByReference(genericElement);
		if (IsExactCharacterMethod(arraySpanMember, "System.MemoryExtensions", "IndexOfAny", integer,
				[readOnlySpanOfMethodElement, genericElement, genericElement]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCharacterSpans", "IndexOfAny");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory, [FrameworkFeature.Spans]),
				Reason: "Target UTF-16 search finds either character without host SIMD helpers.", ShadowMethod: shadow);
			return true;
		}
		if (member.MethodTypeArguments.Length == 1 && IsExactElementMethod(member, "System.Runtime.CompilerServices.Unsafe", "AsRef",
			genericElementRef, [genericElementRef], member.MethodTypeArguments[0]))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:ref-cast", FrameworkEffectSummary.None,
				Reason: "Unsafe byref identity retains its source owner without importing host intrinsic stubs.");
			return true;
		}
		if (IsExactElementMethod(member, "System.Runtime.CompilerServices.Unsafe", "AsPointer",
			FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Void")), [genericElementRef], FrameworkTypeId.Primitive("System.Byte")) ||
			IsExactElementMethod(member, "System.Runtime.CompilerServices.Unsafe", "AsPointer",
				FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Void")), [genericElementRef], FrameworkTypeId.Primitive("System.UInt32")))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:address-of-ref", FrameworkEffectSummary.None,
				Reason: "Unsafe byte and UInt32 references expose their address while retaining the tracked source lifetime.");
			return true;
		}
		if (member.MethodTypeArguments is [var referenceTarget] &&
			(referenceTarget.Equals(FrameworkTypeId.Primitive("System.String")) ||
			 referenceTarget.Equals(FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Char"))) ||
			 referenceTarget.Equals(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.Buffers.MemoryManager`1"), [FrameworkTypeId.Primitive("System.Char")]))) &&
			IsExactElementMethod(member, "System.Runtime.CompilerServices.Unsafe", "As", genericElement,
				[FrameworkTypeId.Primitive("System.Object")], referenceTarget))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:ref-cast", FrameworkEffectSummary.None,
				Reason: "Unsafe character-memory reference casts preserve the managed object address.");
			return true;
		}
		if (member.DeclaringType.Equals(FrameworkTypeId.Named(ContractAssembly, "System.Runtime.CompilerServices.Unsafe")) &&
			member.Name == "As" && member.Signature.Header == 0x10 && member.Signature.GenericParameterCount == 2 &&
			member.Signature.RequiredParameterCount == 1 && member.Signature.ParameterTypes.SequenceEqual(new[] { genericElementRef }) &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.ByReference(FrameworkTypeId.GenericMethodParameter(1))) && member.MethodTypeArguments.Length == 2)
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:ref-cast", FrameworkEffectSummary.None,
				Reason: "Unsafe reference reinterpretation retains the original owner using the target managed-byref ABI.");
			return true;
		}
		if (IsExactCharacterMethod(member, "System.Runtime.CompilerServices.Unsafe", "NullRef", genericElementRef, []))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-null-char-ref", FrameworkEffectSummary.None,
				Reason: "The null character reference has no managed owner.");
			return true;
		}
		if (new[] { integer, FrameworkTypeId.Primitive("System.IntPtr"), FrameworkTypeId.Primitive("System.UIntPtr") }
			.Any(offset => IsExactElementMethod(member, "System.Runtime.CompilerServices.Unsafe", "Add",
				genericElementRef, [genericElementRef, offset], FrameworkTypeId.Primitive("System.Byte")) ||
				!offset.Equals(integer) && IsExactElementMethod(member, "System.Runtime.CompilerServices.Unsafe", "AddByteOffset",
					genericElementRef, [genericElementRef, offset], FrameworkTypeId.Primitive("System.Byte"))))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-add-byte-ref", FrameworkEffectSummary.None,
				Reason: "Target runtime advances a managed byte reference while retaining its owner.");
			return true;
		}
		if (IsExactElementMethod(member, "System.Runtime.CompilerServices.Unsafe", "Add",
				genericElementRef, [genericElementRef, integer], integer) ||
			new[] { integer, FrameworkTypeId.Primitive("System.IntPtr"), FrameworkTypeId.Primitive("System.UIntPtr") }
				.Any(offset => IsExactElementMethod(member, "System.Runtime.CompilerServices.Unsafe", "Add",
					genericElementRef, [genericElementRef, offset], FrameworkTypeId.Primitive("System.Object"))) ||
			new[] { integer, FrameworkTypeId.Primitive("System.IntPtr"), FrameworkTypeId.Primitive("System.UIntPtr") }
				.Any(offset => IsExactElementMethod(member, "System.Runtime.CompilerServices.Unsafe", "Add",
					genericElementRef, [genericElementRef, offset], FrameworkTypeId.Primitive("System.IntPtr"))))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-add-int32-ref", FrameworkEffectSummary.None,
				Reason: "Target runtime advances a managed reference by four-byte Int32 or target-native signed integer elements while retaining its owner.");
			return true;
		}
		if (IsExactCharacterMethod(member, "System.SpanHelpers", "Fill", FrameworkTypeId.Primitive("System.Void"),
				[genericElementRef, FrameworkTypeId.Primitive("System.UIntPtr"), genericElement]))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-fill-char",
				new FrameworkEffectSummary(FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedMemory]),
				Reason: "Target runtime fills UTF-16 code units without host SIMD vector locals.");
			return true;
		}
		var spanReferenceMember = member.DeclaringType.Equals(FrameworkTypeId.Named("System.Memory", "System.Runtime.InteropServices.MemoryMarshal"))
			? new FrameworkMemberId(FrameworkTypeId.Named(ContractAssembly, "System.Runtime.InteropServices.MemoryMarshal"),
				member.Name, member.Signature, member.MethodTypeArguments)
			: member;
		if (IsExactCharacterMethod(spanReferenceMember, "System.Runtime.InteropServices.MemoryMarshal", "GetReference",
				genericElementRef, [spanOfMethodElement]) ||
			IsExactCharacterMethod(spanReferenceMember, "System.Runtime.InteropServices.MemoryMarshal", "GetReference",
				genericElementRef, [FrameworkTypeId.GenericInstantiation(
					FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [genericElement])]))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-char-span-data",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory, [FrameworkFeature.Spans]),
				Reason: "Target runtime projects span data together with its retained owner, including empty spans.");
			return true;
		}
		if (IsExactElementMethod(spanReferenceMember, "System.Runtime.InteropServices.MemoryMarshal", "GetReference",
			genericElementRef, [spanOfMethodElement], FrameworkTypeId.Primitive("System.Byte")) ||
			IsExactElementMethod(spanReferenceMember, "System.Runtime.InteropServices.MemoryMarshal", "GetReference",
			genericElementRef, [FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named(ContractAssembly, "System.ReadOnlySpan`1"), [genericElement])],
			FrameworkTypeId.Primitive("System.Byte")))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:corelib-byte-span-data",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory, [FrameworkFeature.Spans]),
				Reason: "Target byte span projections retain their owner, including empty spans.");
			return true;
		}
		if (IsExactCharacterMethod(member, "System.Buffer", "Memmove",
				FrameworkTypeId.Primitive("System.Void"),
				[genericElementRef, genericElementRef, FrameworkTypeId.Primitive("System.UIntPtr")]))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-memmove-char",
				new FrameworkEffectSummary(
					FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
					[FrameworkFeature.ManagedMemory]),
				Reason: "Target runtime copies UTF-16 code units with its overlap-safe memory kernel.");
			return true;
		}
		var bytePointer = FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Byte"));
		if (IsExactStaticMethod(member, "System.Buffer", "MemmoveInternal",
			FrameworkTypeId.Primitive("System.Void"),
			[bytePointer, bytePointer, FrameworkTypeId.Primitive("System.UIntPtr")]))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-memmove-bytes",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory |
					FrameworkEffects.ReadsNativeMemory | FrameworkEffects.WritesNativeMemory,
					[FrameworkFeature.ManagedMemory, FrameworkFeature.NativeMemory]),
				Reason: "The target copies byte-counted native or managed storage with its overlap-safe kernel without safepoints.");
			return true;
		}
		var byteReference = FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Byte"));
		if ((member.Name is "BulkMoveWithWriteBarrier" or "BulkMoveWithWriteBarrierInternal") &&
			IsExactStaticMethod(member, "System.Buffer", member.Name,
				FrameworkTypeId.Primitive("System.Void"),
				[byteReference, byteReference, FrameworkTypeId.Primitive("System.UIntPtr")]))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-memmove-bytes",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
					[FrameworkFeature.ManagedMemory]),
				Reason: "The target collector has no generational write barrier; reference-bearing memory uses the overlap-safe byte kernel without safepoints.");
			return true;
		}
		if (new[] { integer, FrameworkTypeId.Primitive("System.IntPtr"), FrameworkTypeId.Primitive("System.UIntPtr") }
			.Any(offset => IsExactCharacterMethod(member, "System.Runtime.CompilerServices.Unsafe", "Add",
				genericElementRef, [genericElementRef, offset])))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-add-char-ref", FrameworkEffectSummary.None,
				Reason: "Target runtime advances a managed reference by UTF-16 code units.");
			return true;
		}
		if (new[] { FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Object") }
			.Any(element => IsExactElementMethod(member, "System.Runtime.InteropServices.MemoryMarshal", "GetArrayDataReference",
				genericElementRef, [FrameworkTypeId.SzArray(genericElement)], element)))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-reference-array-data",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow, [FrameworkFeature.ManagedArrays]),
				Reason: "Reference-array projections retain the exact array owner while exposing the target data header.");
			return true;
		}
		if (IsExactCharacterMethod(member, "System.Runtime.InteropServices.MemoryMarshal", "GetArrayDataReference",
				genericElementRef, [FrameworkTypeId.SzArray(genericElement)]))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-char-array-data",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow, [FrameworkFeature.ManagedArrays]),
				Reason: "Target runtime exposes character-array data using its private array header.");
			return true;
		}
		var nullTerminatedCharacters = IsExactInstanceMethod(member, "System.String", ".ctor", FrameworkTypeId.Primitive("System.Void"),
			[FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Char"))]);
		if (nullTerminatedCharacters || IsExactInstanceMethod(member, "System.String", ".ctor", FrameworkTypeId.Primitive("System.Void"),
				[characterFormat]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowStringData",
				nullTerminatedCharacters ? "FromNullTerminatedCharacters" : "FromCharacters");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow |
					FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory |
					(nullTerminatedCharacters ? FrameworkEffects.ReadsNativeMemory : FrameworkEffects.None),
					[FrameworkFeature.ManagedStrings, nullTerminatedCharacters ? FrameworkFeature.NativeMemory : FrameworkFeature.Spans, FrameworkFeature.ManagedGc]),
				Reason: nullTerminatedCharacters
					? "Target string construction copies null-terminated UTF-16 pointer data into independent target storage."
					: "Target string construction copies UTF-16 from an owner-retaining span into variable-sized target storage.", ShadowMethod: shadow);
			return true;
		}
		if (IsExactInstanceMethod(member, "System.String", "GetRawStringData",
				FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Char")), []))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:corelib-string-data",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow, [FrameworkFeature.ManagedStrings]),
				Reason: "Target runtime exposes UTF-16 string data using its private string header.");
			return true;
		}
		if (IsExactStaticMethod(member, "System.String", "FastAllocateString",
				FrameworkTypeId.Primitive("System.String"), [FrameworkTypeId.Primitive("System.IntPtr")]))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic,
				"intrinsic:runtime-allocate-string",
				new FrameworkEffectSummary(
					FrameworkEffects.MayAllocate | FrameworkEffects.MayThrow |
					FrameworkEffects.MayCollect | FrameworkEffects.WritesManagedMemory,
					[FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]),
				Reason: "Target runtime allocates UTF-16 strings using its private object header.");
			return true;
		}
		if (IsExactCharacterMethod(member, "System.GC", "AllocateUninitializedArray",
				FrameworkTypeId.SzArray(genericElement),
				[FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Boolean")]))
		{
			var shadow = new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowArray",
				"AllocateUninitializedArray");
			binding = new FrameworkBinding(
				referencedMember,
				FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(
					FrameworkEffects.MayAllocate | FrameworkEffects.MayThrow |
					FrameworkEffects.MayCollect | FrameworkEffects.WritesManagedMemory,
					[FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc]),
				Reason: "Target runtime allocates zeroed character arrays; its nonmoving heap also satisfies pinned allocation requests.",
				ShadowMethod: shadow);
			return true;
		}

		if (IsExactStaticMethod(
				member,
				"System.OutOfMemoryException",
				"GetDefaultMessage",
				FrameworkTypeId.Primitive("System.String"),
				[]))
		{
			var shadow = new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowSystemResources",
				"GetOutOfMemoryMessage");
			binding = new FrameworkBinding(
				referencedMember,
				FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(
					FrameworkEffects.None,
					[FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedStrings]),
				Reason: "Target runtime uses a deterministic out-of-memory message instead of native resource lookup.",
				ShadowMethod: shadow);
			return true;
		}

		if (IsExactInstanceMethod(
				member,
				ExceptionType,
				"ToString",
				FrameworkTypeId.Primitive("System.String"),
				[]))
		{
			var shadow = new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowException",
				"ToString");
			binding = new FrameworkBinding(
				referencedMember,
				FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(
					FrameworkEffects.None,
					[FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedStrings]),
				Reason: "Target runtime omits stack-trace and reflection expansion from Exception.ToString().",
				ShadowMethod: shadow,
				PreservesVirtualDispatch: true);
			return true;
		}

		if (IsExactInstanceMethod(
				member,
				ExternalExceptionType,
				"ToString",
				FrameworkTypeId.Primitive("System.String"),
				[]))
		{
			var shadow = new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowExternalException",
				"ToString");
			binding = new FrameworkBinding(
				referencedMember,
				FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(
					FrameworkEffects.None,
					[FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedStrings]),
				Reason: "Target runtime omits stack traces, reflection, and HRESULT formatting from ExternalException.ToString().",
				ShadowMethod: shadow,
				PreservesVirtualDispatch: true);
			return true;
		}

		var numberFormatInfo = FrameworkTypeId.Named(ContractAssembly, "System.Globalization.NumberFormatInfo");
		var cultureString = FrameworkTypeId.Primitive("System.String");
		var cultureVoid = FrameworkTypeId.Primitive("System.Void");
		var cultureType = FrameworkTypeId.Named(ContractAssembly, CultureInfoType);
		if (IsExactStaticMethod(member, CultureInfoType, "get_CurrentCulture", cultureType, []) ||
			IsExactStaticMethod(member, CultureInfoType, "set_CurrentCulture", cultureVoid, [cultureType]) ||
			IsExactStaticMethod(member, CultureInfoType, "get_DefaultThreadCurrentCulture", cultureType, []) ||
			IsExactStaticMethod(member, CultureInfoType, "set_DefaultThreadCurrentCulture", cultureVoid, [cultureType]))
		{
			var shadowName = member.Name switch
			{
				"get_CurrentCulture" => "GetCurrentCulture",
				"set_CurrentCulture" => "SetCurrentCulture",
				"get_DefaultThreadCurrentCulture" => "GetDefaultThreadCurrentCulture",
				_ => "SetDefaultThreadCurrentCulture"
			};
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCultureInfo", shadowName);
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
					[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedGc]),
				Reason: "Target execution culture supplies ambient numeric settings and defaults to the invariant culture.",
				ShadowMethod: shadow, TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		if (IsExactElementMethod(member, "System.Buffer", "Memmove", FrameworkTypeId.Primitive("System.Void"),
			[genericElementRef, genericElementRef, FrameworkTypeId.Primitive("System.UIntPtr")], FrameworkTypeId.Primitive("System.UInt32")))
		{
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:corelib-memmove-uint32",
				new FrameworkEffectSummary(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedMemory]),
				Reason: "Reference-free UInt32 blocks use the target's overlap-safe memory kernel.");
			return true;
		}
		if (member.DeclaringType.Equals(FrameworkTypeId.Named(ContractAssembly, "System.Runtime.CompilerServices.Unsafe")) &&
			member.Name == "BitCast" && member.Signature.Header == 0x10 && member.Signature.GenericParameterCount == 2 &&
			member.Signature.RequiredParameterCount == 1 && member.Signature.ParameterTypes.SequenceEqual(new[] { genericElement }) &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.GenericMethodParameter(1)) &&
			member.MethodTypeArguments is [var bitSource, var bitTarget])
		{
			var single = FrameworkTypeId.Primitive("System.Single");
			var wide = FrameworkTypeId.Primitive("System.Double");
			var uint32 = FrameworkTypeId.Primitive("System.UInt32");
			var int64 = FrameworkTypeId.Primitive("System.Int64");
			var uint64 = FrameworkTypeId.Primitive("System.UInt64");
			if (bitSource.Equals(single) && (bitTarget.Equals(integer) || bitTarget.Equals(uint32)) ||
				bitTarget.Equals(single) && (bitSource.Equals(integer) || bitSource.Equals(uint32)))
			{
				binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.Intrinsic, "intrinsic:runtime-bitcast-32",
					FrameworkEffectSummary.None, Reason: "Single bit projections preserve the target's raw IEEE word.");
				return true;
			}
			var method = bitSource.Equals(wide) && bitTarget.Equals(uint64) ? "DoubleToUInt64" :
				bitSource.Equals(wide) && bitTarget.Equals(int64) ? "DoubleToInt64" :
				bitTarget.Equals(wide) && bitSource.Equals(uint64) ? "UInt64ToDouble" :
				bitTarget.Equals(wide) && bitSource.Equals(int64) ? "Int64ToDouble" : null;
			if (method is not null)
			{
				var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowFloatingPointBits", method);
				binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
					$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}", FrameworkEffectSummary.None,
					Reason: "Double bit projections preserve both target IEEE words without a numeric conversion.", ShadowMethod: shadow);
				return true;
			}
		}
		if (member.DeclaringType.Equals(FrameworkTypeId.Named(ContractAssembly, "System.Runtime.CompilerServices.Unsafe")) &&
			member.Name == "BitCast" && member.Signature.Header == 0x10 && member.Signature.GenericParameterCount == 2 &&
			member.Signature.RequiredParameterCount == 1 && member.Signature.ParameterTypes.SequenceEqual(new[] { genericElement }) &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.GenericMethodParameter(1)) &&
			member.MethodTypeArguments.SequenceEqual(new[] { writableCharacterSpan, writableCharacterSpan }))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCharacterSpans", "Identity");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}", FrameworkEffectSummary.None,
				Reason: "An exact same-type character-span cast preserves the complete target span and its GC owner.", ShadowMethod: shadow);
			return true;
		}
		if (IsExactStaticMethod(member, CultureInfoType, "get_InvariantCulture",
			FrameworkTypeId.Named(ContractAssembly, CultureInfoType), []))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCultureInfo", "GetInvariantCulture");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
					[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedGc]),
				Reason: "Target-owned invariant culture retains shared read-only numeric settings without host locale initialization.",
				ShadowMethod: shadow, TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		if (IsExactInstanceMethod(member, CultureInfoType, ".ctor", cultureVoid, [cultureString]) ||
			IsExactInstanceMethod(member, CultureInfoType, ".ctor", cultureVoid, [cultureString, FrameworkTypeId.Primitive("System.Boolean")]) ||
			IsExactInstanceMethod(member, CultureInfoType, "get_NumberFormat", numberFormatInfo, []) ||
			IsExactInstanceMethod(member, CultureInfoType, "set_NumberFormat", cultureVoid, [numberFormatInfo]))
		{
			var shadowName = member.Name switch
			{
				".ctor" => member.Signature.ParameterTypes.Length == 1 ? "InitializeName" : "InitializeNameWithOverrides",
				"get_NumberFormat" => "GetNumberFormat",
				_ => "SetNumberFormat"
			};
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCultureInfo", shadowName);
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
					FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
					[FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedGc]),
				Reason: "Target CultureInfo storage supplies invariant numeric settings and explicitly rejects unavailable named locale data.",
				ShadowMethod: shadow, PreservesVirtualDispatch: member.Name != ".ctor",
				TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}

		if (IsExactInstanceMethod(
				member,
				CultureInfoType,
				"get_DateTimeFormat",
				FrameworkTypeId.Named(
					ContractAssembly,
					"System.Globalization.DateTimeFormatInfo"),
				[]))
		{
			var shadow = new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowCultureInfo",
				"GetDateTimeFormat");
			binding = new FrameworkBinding(
				referencedMember,
				FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(
					FrameworkEffects.None,
					[FrameworkFeature.ManagedObjects]),
				Reason: "Target runtime omits date/time globalization and host calendar/locale discovery.",
				ShadowMethod: shadow,
				PreservesVirtualDispatch: true);
			return true;
		}

		if (IsExactStaticMethod(
				member,
				SystemResourceType,
				"GetResourceString",
				FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.String")]))
		{
			var shadow = new FrameworkShadowMethod(
				"CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowSystemResources",
				"GetResourceString");
			binding = new FrameworkBinding(
				referencedMember,
				FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(
					FrameworkEffects.None,
					[FrameworkFeature.ManagedStrings]),
				Reason: "Target runtime uses deterministic CoreLib resource keys instead of ResourceManager lookup.",
				ShadowMethod: shadow,
				TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}
		if (IsExactStaticMethod(member, SystemResourceType, "Format",
				FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Object")]) ||
			IsExactStaticMethod(member, SystemResourceType, "Format",
				FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Object"),
				 FrameworkTypeId.Primitive("System.Object")]) ||
			IsExactStaticMethod(member, SystemResourceType, "Format", FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Primitive("System.Object")]) ||
			IsExactStaticMethod(member, SystemResourceType, "Format", FrameworkTypeId.Primitive("System.String"),
				[FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Primitive("System.Object")]))
		{
			var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed",
				"CopperSharp.Runtime.ShadowSystemResources", "FormatResourceString");
			binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
				$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
				new FrameworkEffectSummary(FrameworkEffects.None, [FrameworkFeature.ManagedStrings]),
				Reason: "Target runtime reports the resource key without formatting exception-message arguments.",
				ShadowMethod: shadow, TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
			return true;
		}

		binding = null!;
		return false;
	}

	public static bool IsTargetRuntimeOverride(FrameworkBinding binding)
	{
		if (!TryCreateTargetRuntimeOverride(
				binding.Member,
				enableUnlistedManagedBodies: true,
				out var expected))
		{
			return false;
		}

		return binding.Kind == expected.Kind &&
			string.Equals(binding.Target, expected.Target, StringComparison.Ordinal) &&
			binding.ShadowMethod == expected.ShadowMethod &&
			binding.PreservesVirtualDispatch == expected.PreservesVirtualDispatch &&
			binding.TypeInitializerPolicy == expected.TypeInitializerPolicy;
	}

	internal static bool IsExperimentalNumberGroupSizesReader(FrameworkMemberId member, bool enabled) =>
		enabled && member.DeclaringType.Equals(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowGroupedIntegerFormatting")) &&
		member.Name is "NumberGroupSizes" or "CurrencyGroupSizes" or "PercentGroupSizes" && member.MethodTypeArguments.Length == 0 &&
		member.Signature.Header == 0 && member.Signature.GenericParameterCount == 0 && member.Signature.RequiredParameterCount == 1 &&
		member.Signature.ReturnType.Equals(FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Int32"))) &&
		member.Signature.ParameterTypes is [var info] &&
		(info.Equals(FrameworkTypeId.Named(ContractAssembly, "System.Globalization.NumberFormatInfo")) ||
		 info.Equals(FrameworkTypeId.Named(ImplementationAssembly, "System.Globalization.NumberFormatInfo")));

	public static FrameworkShadowFieldBinding? TryCreateTargetRuntimeFieldOverride(
		string assemblyName, string typeName, string fieldName, string fieldType,
		bool enableUnlistedManagedBodies) =>
		enableUnlistedManagedBodies &&
		assemblyName is ContractAssembly or ImplementationAssembly &&
		typeName == "System.String" && fieldName == "Empty" && fieldType == "string"
			? new FrameworkShadowFieldBinding(assemblyName, typeName, fieldName, fieldType,
				"CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowStringData", "Empty")
			: null;

	public static bool IsRequiredCoreLibOverride(
		FrameworkMemberId referencedMember,
		FrameworkBinding? binding)
	{
		if (binding is null)
		{
			return false;
		}
		var member = Canonicalize(referencedMember);
		var typeName = member.DeclaringType.MetadataName;
		return (string.Equals(typeName, ObjectType, StringComparison.Ordinal) &&
			string.Equals(member.Name, ".ctor", StringComparison.Ordinal) &&
			binding.Kind == FrameworkBindingKind.Intrinsic) ||
			(string.Equals(typeName, StopwatchType, StringComparison.Ordinal) &&
			 StopwatchTargetOverrides.Contains(member.Name) &&
			 binding.Kind == FrameworkBindingKind.PlatformOperation) ||
			(string.Equals(typeName, TimeSpanType, StringComparison.Ordinal) &&
			 TimeSpanTargetOverrides.Contains(member.Name) &&
			 binding.Kind == FrameworkBindingKind.PlatformOperation);
	}

	public static bool IsPinnedTypeBoundary(FrameworkMemberId referencedMember)
	{
		var member = Canonicalize(referencedMember);
		return string.Equals(member.AssemblyName, ContractAssembly, StringComparison.Ordinal) &&
			member.DeclaringType.Kind == FrameworkTypeKind.Named &&
			(member.DeclaringType.MetadataName is StopwatchType or TimeSpanType);
	}

	private static bool IsPinnedStopwatchMember(FrameworkMemberId member)
	{
		if (!string.Equals(member.AssemblyName, ContractAssembly, StringComparison.Ordinal) ||
			member.DeclaringType.Kind != FrameworkTypeKind.Named ||
			!string.Equals(member.DeclaringType.MetadataName, StopwatchType, StringComparison.Ordinal) ||
			!StopwatchMembers.Contains(member.Name) ||
			member.MethodTypeArguments.Length != 0 ||
			member.Signature.GenericParameterCount != 0 ||
			member.Signature.ParameterTypes.Length != 0)
		{
			return false;
		}

		return member.Name switch
		{
			".ctor" or "Start" or "Stop" or "Reset" or "Restart" =>
				member.Signature.IsInstance && IsPrimitive(member.Signature.ReturnType, "System.Void"),
			"StartNew" =>
				!member.Signature.IsInstance && IsNamedStopwatch(member.Signature.ReturnType),
			"get_IsRunning" =>
				member.Signature.IsInstance && IsPrimitive(member.Signature.ReturnType, "System.Boolean"),
			"get_ElapsedTicks" =>
				member.Signature.IsInstance && IsPrimitive(member.Signature.ReturnType, "System.Int64"),
			_ => false
		};
	}

	private static bool IsPinnedMember(FrameworkMemberId member) =>
		IsPinnedStopwatchMember(member) || IsPinnedTimeSpanMember(member);

	public static bool IsFrameworkImplementationCandidate(FrameworkMemberId member) =>
		string.Equals(member.AssemblyName, ImplementationAssembly, StringComparison.Ordinal) ||
		Net10FrameworkContract.Default.IsFrameworkAssembly(member.AssemblyName);

	private static string PinnedTarget(FrameworkMemberId member) =>
		$"pinned:[{ImplementationAssembly}]{GetNamedDeclaringType(member.DeclaringType).FullMetadataName}::{member.Name}";

	private static FrameworkTypeId GetNamedDeclaringType(FrameworkTypeId type) =>
		type.Kind == FrameworkTypeKind.GenericInstantiation
			? GetNamedDeclaringType(type.ElementType!)
			: type.Kind == FrameworkTypeKind.Named
				? type
				: throw new InvalidOperationException(
					$"Declaring type '{type.DisplayName}' is not a named metadata type.");

	private static bool IsPinnedTimeSpanMember(FrameworkMemberId member)
	{
		if (!string.Equals(member.AssemblyName, ContractAssembly, StringComparison.Ordinal) ||
			member.DeclaringType.Kind != FrameworkTypeKind.Named ||
			!string.Equals(member.DeclaringType.MetadataName, TimeSpanType, StringComparison.Ordinal) ||
			!TimeSpanMembers.Contains(member.Name) ||
			member.MethodTypeArguments.Length != 0 ||
			member.Signature.GenericParameterCount != 0)
		{
			return false;
		}

		return member.Name switch
		{
			"get_Ticks" =>
				member.Signature.IsInstance &&
				member.Signature.ParameterTypes.Length == 0 &&
				IsPrimitive(member.Signature.ReturnType, "System.Int64"),
			"get_Days" or "get_Hours" or "get_Minutes" or "get_Seconds" or
				"get_Milliseconds" =>
				member.Signature.IsInstance &&
				member.Signature.ParameterTypes.Length == 0 &&
				IsPrimitive(member.Signature.ReturnType, "System.Int32"),
			"get_TotalDays" or "get_TotalHours" or "get_TotalMinutes" or
				"get_TotalSeconds" or "get_TotalMilliseconds" =>
				member.Signature.IsInstance &&
				member.Signature.ParameterTypes.Length == 0 &&
				IsPrimitive(member.Signature.ReturnType, "System.Double"),
			"op_Equality" or "op_Inequality" or "op_LessThan" or
				"op_LessThanOrEqual" or "op_GreaterThan" or
				"op_GreaterThanOrEqual" =>
				!member.Signature.IsInstance &&
				IsPrimitive(member.Signature.ReturnType, "System.Boolean") &&
				member.Signature.ParameterTypes.Length == 2 &&
				IsNamedTimeSpan(member.Signature.ParameterTypes[0]) &&
				IsNamedTimeSpan(member.Signature.ParameterTypes[1]),
			_ => false
		};
	}

	private static bool IsPrimitive(FrameworkTypeId type, string metadataName) =>
		type.Kind == FrameworkTypeKind.Primitive &&
		string.Equals(type.MetadataName, metadataName, StringComparison.Ordinal);

	private static bool IsExactInstanceMethod(
		FrameworkMemberId member,
		string declaringType,
		string name,
		FrameworkTypeId returnType,
		IReadOnlyList<FrameworkTypeId> parameterTypes) =>
		string.Equals(member.AssemblyName, ContractAssembly, StringComparison.Ordinal) &&
		member.DeclaringType.Kind == FrameworkTypeKind.Named &&
		string.Equals(member.DeclaringType.MetadataName, declaringType, StringComparison.Ordinal) &&
		string.Equals(member.Name, name, StringComparison.Ordinal) &&
		member.MethodTypeArguments.Length == 0 && member.Signature.Header == 0x20 &&
		member.Signature.GenericParameterCount == 0 &&
		member.Signature.RequiredParameterCount == parameterTypes.Count &&
		member.Signature.ReturnType.Equals(returnType) &&
		member.Signature.ParameterTypes.SequenceEqual(parameterTypes);

	private static bool IsExactStaticMethod(
		FrameworkMemberId member,
		string declaringType,
		string name,
		FrameworkTypeId returnType,
		IReadOnlyList<FrameworkTypeId> parameterTypes) =>
		string.Equals(member.AssemblyName, ContractAssembly, StringComparison.Ordinal) &&
		member.DeclaringType.Kind == FrameworkTypeKind.Named &&
		string.Equals(member.DeclaringType.MetadataName, declaringType, StringComparison.Ordinal) &&
		string.Equals(member.Name, name, StringComparison.Ordinal) &&
		member.MethodTypeArguments.Length == 0 && member.Signature.Header == 0 &&
		member.Signature.GenericParameterCount == 0 &&
		member.Signature.RequiredParameterCount == parameterTypes.Count &&
		member.Signature.ReturnType.Equals(returnType) &&
		member.Signature.ParameterTypes.SequenceEqual(parameterTypes);

	private static bool IsExactCharacterMethod(
		FrameworkMemberId member, string declaringType, string name,
		FrameworkTypeId returnType, IReadOnlyList<FrameworkTypeId> parameterTypes) =>
		IsExactElementMethod(member, declaringType, name, returnType, parameterTypes, FrameworkTypeId.Primitive("System.Char"));

	private static bool IsExactElementMethod(
		FrameworkMemberId member, string declaringType, string name,
		FrameworkTypeId returnType, IReadOnlyList<FrameworkTypeId> parameterTypes, FrameworkTypeId element) =>
		member.DeclaringType.Equals(FrameworkTypeId.Named(ContractAssembly, declaringType)) &&
		member.Name == name && member.Signature.Header == 0x10 &&
		member.Signature.GenericParameterCount == 1 &&
		member.Signature.RequiredParameterCount == parameterTypes.Count &&
		member.Signature.ReturnType.Equals(returnType) &&
		member.Signature.ParameterTypes.SequenceEqual(parameterTypes) &&
		member.MethodTypeArguments.SequenceEqual(new[] { element });

	private static bool IsNamedStopwatch(FrameworkTypeId type) =>
		type.Kind == FrameworkTypeKind.Named &&
		string.Equals(type.AssemblyName, ContractAssembly, StringComparison.Ordinal) &&
		string.Equals(type.MetadataName, StopwatchType, StringComparison.Ordinal);

	private static bool IsSystemResourceMember(FrameworkMemberId member) =>
		string.Equals(member.AssemblyName, ContractAssembly, StringComparison.Ordinal) &&
		member.DeclaringType.Kind == FrameworkTypeKind.Named &&
		string.Equals(
			member.DeclaringType.MetadataName,
			SystemResourceType,
			StringComparison.Ordinal);

	private static bool IsNamedTimeSpan(FrameworkTypeId type) =>
		type.Kind == FrameworkTypeKind.Named &&
		string.Equals(type.AssemblyName, ContractAssembly, StringComparison.Ordinal) &&
		string.Equals(type.MetadataName, TimeSpanType, StringComparison.Ordinal);

	internal static FrameworkTypeId CanonicalizeType(FrameworkTypeId type) =>
		type.Kind switch
		{
			FrameworkTypeKind.Primitive => FrameworkTypeId.Primitive(type.MetadataName!),
			FrameworkTypeKind.Named => FrameworkTypeId.Named(
				string.Equals(type.AssemblyName, ImplementationAssembly, StringComparison.Ordinal)
					? ContractAssembly
					: type.AssemblyName!,
				type.MetadataName!,
				type.DeclaringType is null ? null : CanonicalizeType(type.DeclaringType)),
			FrameworkTypeKind.GenericInstantiation => FrameworkTypeId.GenericInstantiation(
				CanonicalizeType(type.ElementType!),
				type.GenericArguments.Select(CanonicalizeType).ToArray()),
			FrameworkTypeKind.SzArray => FrameworkTypeId.SzArray(CanonicalizeType(type.ElementType!)),
			FrameworkTypeKind.Array => FrameworkTypeId.Array(
				CanonicalizeType(type.ElementType!),
				new ArrayShape(
					type.ArrayShape!.Rank,
					type.ArrayShape.Sizes,
					type.ArrayShape.LowerBounds)),
			FrameworkTypeKind.ByReference => FrameworkTypeId.ByReference(CanonicalizeType(type.ElementType!)),
			FrameworkTypeKind.Pointer => FrameworkTypeId.Pointer(CanonicalizeType(type.ElementType!)),
			FrameworkTypeKind.GenericTypeParameter => FrameworkTypeId.GenericTypeParameter(type.GenericParameterIndex),
			FrameworkTypeKind.GenericMethodParameter => FrameworkTypeId.GenericMethodParameter(type.GenericParameterIndex),
			FrameworkTypeKind.FunctionPointer => FrameworkTypeId.FunctionPointer(type.FunctionPointerSignature!),
			FrameworkTypeKind.Modified => FrameworkTypeId.Modified(
				CanonicalizeType(type.Modifier!),
				CanonicalizeType(type.ElementType!),
				type.IsRequiredModifier),
			_ => throw new InvalidOperationException($"Unknown framework type kind {type.Kind}.")
		};
}
