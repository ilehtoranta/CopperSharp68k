/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Framework;

/// <summary>
/// Exact public DateOnly members admitted from the pinned CoreLib pack.
/// Helpers remain closed to this implementation surface and are added only
/// when a reachable body requires them.
/// </summary>
internal static class DateOnlySurface
{
	private const string TypeName = "System.DateOnly";
	private static readonly FrameworkTypeId DateOnly = FrameworkTypeId.Named("System.Runtime", TypeName);
	private static readonly FrameworkTypeId Int32 = FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId Boolean = FrameworkTypeId.Primitive("System.Boolean");
	private static readonly FrameworkTypeId String = FrameworkTypeId.Primitive("System.String");
	private static readonly FrameworkTypeId Void = FrameworkTypeId.Primitive("System.Void");
	private static readonly FrameworkTypeId FormatProvider = FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider");

	public static bool Contains(FrameworkMemberId referenced)
	{
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		return IsExactInstance(member, ".ctor", Void, [Int32, Int32, Int32]) ||
			IsExactInstance(member, "AddDays", DateOnly, [Int32]) ||
			IsExactInstance(member, "CompareTo", Int32, [DateOnly]) ||
			IsExactInstance(member, "Equals", Boolean, [DateOnly]) ||
			IsExactInstance(member, "get_Day", Int32, []) ||
			IsExactInstance(member, "get_DayNumber", Int32, []) ||
			IsExactInstance(member, "get_Month", Int32, []) ||
			IsExactInstance(member, "get_Year", Int32, []) ||
			IsExactStatic(member, "FromDayNumber", DateOnly, [Int32]) ||
			IsExactStatic(member, "op_Equality", Boolean, [DateOnly, DateOnly]) ||
			IsExactStatic(member, "op_Inequality", Boolean, [DateOnly, DateOnly]) ||
			IsExactStatic(member, "op_LessThan", Boolean, [DateOnly, DateOnly]) ||
			IsExactStatic(member, "op_LessThanOrEqual", Boolean, [DateOnly, DateOnly]);
	}

	public static bool TryCreateShadowBinding(FrameworkMemberId referenced, out FrameworkBinding binding)
	{
		binding = null!;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var isCalendarConstructor = IsExactInstance(member, ".ctor", Void, [Int32, Int32, Int32]);
		var isAddDays = IsExactInstance(member, "AddDays", DateOnly, [Int32]);
		var isValueOperation = isCalendarConstructor || isAddDays ||
			IsExactInstance(member, "CompareTo", Int32, [DateOnly]) ||
			IsExactInstance(member, "Equals", Boolean, [DateOnly]) ||
			IsExactStatic(member, "op_Equality", Boolean, [DateOnly, DateOnly]) ||
			IsExactStatic(member, "op_Inequality", Boolean, [DateOnly, DateOnly]) ||
			IsExactStatic(member, "op_LessThan", Boolean, [DateOnly, DateOnly]) ||
			IsExactStatic(member, "op_LessThanOrEqual", Boolean, [DateOnly, DateOnly]);
		var shadowName = isCalendarConstructor ? ".ctor" :
			isAddDays ? "AddDays" :
			IsExactInstance(member, "CompareTo", Int32, [DateOnly]) ? "CompareTo" :
			IsExactInstance(member, "Equals", Boolean, [DateOnly]) ? "Equals" :
			IsExactStatic(member, "op_Equality", Boolean, [DateOnly, DateOnly]) ? "AreEqual" :
			IsExactStatic(member, "op_Inequality", Boolean, [DateOnly, DateOnly]) ? "AreNotEqual" :
			IsExactStatic(member, "op_LessThan", Boolean, [DateOnly, DateOnly]) ? "IsLessThan" :
			IsExactStatic(member, "op_LessThanOrEqual", Boolean, [DateOnly, DateOnly]) ? "IsLessThanOrEqual" :
			IsExactInstance(member, "get_Day", Int32, []) ? "GetDay" :
			IsExactInstance(member, "get_Month", Int32, []) ? "GetMonth" :
			IsExactInstance(member, "get_Year", Int32, []) ? "GetYear" :
			IsExactStatic(member, "FromDayNumber", DateOnly, [Int32]) ? "FromDayNumber" : null;
		if (shadowName is null) return false;
		var shadowType = isValueOperation
			? "CopperSharp.Runtime.ShadowDateOnlyConstructor"
			: "CopperSharp.Runtime.ShadowDateOnlyCalendar";
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", shadowType, shadowName);
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}", Effects,
			Reason: "The target's bounded DateOnly Gregorian calendar operations use the official day-number representation without pulling in DateTime globalization or static tables.",
			ShadowMethod: shadow);
		return true;
	}

	public static bool IsIsoFormattingMethod(FrameworkMemberId referenced)
	{
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		return IsExactInstance(member, "ToString", String, [String, FormatProvider]);
	}

	public static bool IsInvariantParseMember(FrameworkMemberId referenced)
	{
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		return IsExactStatic(member, "Parse", DateOnly, [String, FormatProvider]) ||
			IsExactStatic(member, "TryParse", Boolean, [String, FormatProvider, FrameworkTypeId.ByReference(DateOnly)]);
	}

	public static bool TryCreateInvariantParsingShadowBinding(FrameworkMemberId referenced, out FrameworkBinding binding)
	{
		binding = null!;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var method = IsExactStatic(member, "Parse", DateOnly, [String, FormatProvider]) ? "ParseInvariant" :
			IsExactStatic(member, "TryParse", Boolean, [String, FormatProvider, FrameworkTypeId.ByReference(DateOnly)]) ? "TryParseInvariant" : null;
		if (method is null) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowDateOnlyParsing", method);
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedGc, FrameworkFeature.ManagedStrings]),
			Reason: "Invariant DateOnly parsing requires proven invariant-provider Parse/TryParse arguments and the canonical yyyy-MM-dd grammar.",
			ShadowMethod: shadow);
		return true;
	}

	public static bool IsOwnedCaller(FrameworkMemberId? caller) =>
		caller is { AssemblyName: "System.Private.CoreLib" } &&
		(caller.DeclaringType.FullMetadataName == TypeName || caller.DeclaringType.FullMetadataName switch
		{
			"System.DateTime" => caller.Name is ".ctor" or "CreateUnchecked" or "DateToTicks" or "DaysToYear" or "GetYear" or "IsLeapYear" or
				"get_Day" or "get_DaysToMonth365" or "get_DaysToMonth366" or "get_Month" or "get_Ticks" or "get_UTicks" or "get_Year",
			"System.DateTimeFormat" => caller.Name is "Format" or "IsValidCustomDateOnlyFormat" or "TryFormatDateOnlyO" or "TryFormatDateOnlyR",
			"System.String" => caller.Name is "Create" or "IsNullOrEmpty",
			"System.SR" => caller.Name is "get_Format_InvalidString" or "get_ArgumentOutOfRange_AddValue" or
				"get_ArgumentOutOfRange_DayNumber" or "get_ArgumentOutOfRange_BadYearMonthDay" or "get_ArgumentOutOfRange_Year" or "GetResourceString",
			"System.ThrowHelper" => caller.Name is "ThrowArgumentOutOfRange_DayNumber" or "ThrowArgumentOutOfRange_BadYearMonthDay" or
				"ThrowArgumentOutOfRange_Year" or "ThrowArgumentOutOfRangeException" or "ThrowArgumentNullException",
			"System.FormatException" => caller.Name == ".ctor",
			"System.UInt32" => caller.Name == "CompareTo",
			"System.Math" => caller.Name == "DivRem",
			"System.Runtime.CompilerServices.RuntimeHelpers" => caller.Name is "IsKnownConstant" or "CreateSpan",
			_ => false
		});

	public static bool IsRuntimeCalendarCaller(FrameworkMemberId? caller) =>
		caller is { AssemblyName: "CopperSharp.Runtime.Managed" } &&
		caller.DeclaringType.FullMetadataName == "CopperSharp.Runtime.ShadowDateOnlyCalendar" &&
		caller.Name is "Initialize" or "AddDays";

	public static bool TryCreateExceptionResourceBinding(
		FrameworkMemberId referenced, FrameworkMemberId? caller, out FrameworkBinding binding)
	{
		binding = null!;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		_ = caller;
		if (member.DeclaringType.FullMetadataName != "System.SR" || member.Signature.IsInstance ||
			!member.Signature.ReturnType.Equals(String) || member.Signature.ParameterTypes.Length != 0) return false;
		var shadowName = member.Name switch
		{
			"get_Arg_ArgumentOutOfRangeException" => "GetArgumentOutOfRangeExceptionMessage",
			"get_Arg_NotSupportedException" => "GetNotSupportedExceptionMessage",
			"get_ArgumentNull_Generic" => "GetArgumentNullGenericMessage",
			"get_ArgumentOutOfRange_DayNumber" => "GetArgumentOutOfRangeDayNumberMessage",
			_ => null
		};
		if (shadowName is null) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowSystemResources", shadowName);
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}", FrameworkEffectSummary.None,
			Reason: "Exact CoreLib exception resource getters use target-owned keys without compiling ResourceManager access.",
			ShadowMethod: shadow);
		return true;
	}

	public static bool TryCreateDayNumberThrowBinding(
		FrameworkMemberId referenced, FrameworkMemberId? caller, out FrameworkBinding binding)
	{
		binding = null!;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		if (!IsOwnedCaller(caller) || member.DeclaringType.FullMetadataName != "System.ThrowHelper" ||
			member.Name != "ThrowArgumentOutOfRange_DayNumber" || member.Signature.IsInstance ||
			!member.Signature.ReturnType.Equals(Void) || !member.Signature.ParameterTypes.SequenceEqual([Int32])) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowDateOnlyCalendar", "ThrowDayNumberOutOfRange");
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}", FrameworkEffectSummary.None,
			Reason: "DateOnly day-number validation keeps the target exception path without compiling CoreLib ThrowHelper state transitions.",
			ShadowMethod: shadow);
		return true;
	}

	public static bool IsImplementationDependency(FrameworkMemberId referenced)
	{
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		return member.DeclaringType.FullMetadataName switch
		{
			"System.DateOnly" => member.Name is ".ctor" or "<AddDays>g__ThrowOutOfRange|25_0" or
				"DayNumberFromDateTime" or "GetEquivalentDateTime",
			"System.DateTime" => member.Name is ".ctor" or "CreateUnchecked" or "DateToTicks" or "DaysToYear" or "GetYear" or "IsLeapYear" or
				"get_Day" or "get_DaysToMonth365" or "get_DaysToMonth366" or "get_Month" or "get_Ticks" or "get_UTicks" or "get_Year",
			"System.DateTimeFormat" => member.Name is "Format" or "IsValidCustomDateOnlyFormat" or
				"TryFormatDateOnlyO" or "TryFormatDateOnlyR",
			"System.ArgumentOutOfRangeException" => member.Name == ".ctor",
			"System.FormatException" => member.Name == ".ctor",
			"System.SR" => member.Name is "get_Format_InvalidString" or "get_ArgumentOutOfRange_AddValue" or
				"get_ArgumentOutOfRange_DayNumber" or "get_ArgumentOutOfRange_BadYearMonthDay" or "get_ArgumentOutOfRange_Year" or "GetResourceString",
			"System.String" => member.Name is "Create" or "IsNullOrEmpty",
			"System.ThrowHelper" => member.Name is "ThrowArgumentOutOfRange_DayNumber" or "ThrowArgumentOutOfRange_BadYearMonthDay" or
				"ThrowArgumentOutOfRange_Year" or "ThrowArgumentOutOfRangeException" or "ThrowArgumentNullException" or "GetArgumentOutOfRangeException",
			"System.Math" => member.Name == "DivRem",
			"System.Runtime.CompilerServices.RuntimeHelpers" => member.Name is "IsKnownConstant" or "CreateSpan",
			"System.UInt32" => member.Name == "CompareTo",
			_ => false
		};
	}

	public static FrameworkEffectSummary Effects { get; } = new(
		FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
		FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
		[FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedGc]);

	private static bool IsExactInstance(FrameworkMemberId member, string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		member.DeclaringType.Equals(DateOnly) && member.Name == name && member.Signature.IsInstance &&
		member.Signature.GenericParameterCount == 0 && member.Signature.ParameterTypes.SequenceEqual(parameters) &&
		member.Signature.ReturnType.Equals(result);

	private static bool IsExactStatic(FrameworkMemberId member, string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		member.DeclaringType.Equals(DateOnly) && member.Name == name && !member.Signature.IsInstance &&
		member.Signature.GenericParameterCount == 0 && member.Signature.ParameterTypes.SequenceEqual(parameters) &&
		member.Signature.ReturnType.Equals(result);
}
