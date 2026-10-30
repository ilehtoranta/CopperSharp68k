/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Framework;

/// <summary>
/// Exact TimeOnly members admitted for the bounded pinned-CoreLib time-of-day surface.
/// </summary>
internal static class TimeOnlySurface
{
	private const string TypeName = "System.TimeOnly";
	private static readonly FrameworkTypeId TimeOnly = FrameworkTypeId.Named("System.Runtime", TypeName);
	private static readonly FrameworkTypeId Int32 = FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId Int64 = FrameworkTypeId.Primitive("System.Int64");
	private static readonly FrameworkTypeId Boolean = FrameworkTypeId.Primitive("System.Boolean");
	private static readonly FrameworkTypeId String = FrameworkTypeId.Primitive("System.String");
	private static readonly FrameworkTypeId Void = FrameworkTypeId.Primitive("System.Void");
	private static readonly FrameworkTypeId FormatProvider = FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider");
	private static readonly FrameworkTypeId TimeSpan = FrameworkTypeId.Named("System.Runtime", "System.TimeSpan");

	public static bool Contains(FrameworkMemberId referenced)
	{
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		return IsInstance(member, ".ctor", Void, [Int32, Int32]) ||
			IsInstance(member, ".ctor", Void, [Int32, Int32, Int32]) ||
			IsInstance(member, ".ctor", Void, [Int32, Int32, Int32, Int32]) ||
			IsInstance(member, ".ctor", Void, [Int32, Int32, Int32, Int32, Int32]) ||
			IsInstance(member, ".ctor", Void, [Int64]) ||
			IsInstance(member, "Add", TimeOnly, [TimeSpan, FrameworkTypeId.ByReference(Int32)]) ||
			IsInstance(member, "Add", TimeOnly, [TimeSpan]) ||
			IsInstance(member, "CompareTo", Int32, [TimeOnly]) ||
			IsInstance(member, "Equals", Boolean, [TimeOnly]) ||
			IsInstance(member, "get_Hour", Int32, []) ||
			IsInstance(member, "get_Microsecond", Int32, []) ||
			IsInstance(member, "get_Millisecond", Int32, []) ||
			IsInstance(member, "get_Minute", Int32, []) ||
			IsInstance(member, "get_Second", Int32, []) ||
			IsInstance(member, "get_Ticks", Int64, []) ||
			IsStatic(member, "op_Equality", Boolean, [TimeOnly, TimeOnly]) ||
			IsStatic(member, "op_GreaterThan", Boolean, [TimeOnly, TimeOnly]) ||
			IsStatic(member, "op_GreaterThanOrEqual", Boolean, [TimeOnly, TimeOnly]) ||
			IsStatic(member, "op_Inequality", Boolean, [TimeOnly, TimeOnly]) ||
			IsStatic(member, "op_LessThan", Boolean, [TimeOnly, TimeOnly]) ||
			IsStatic(member, "op_LessThanOrEqual", Boolean, [TimeOnly, TimeOnly]) ||
			IsInstance(member, "ToString", String, [String]) ||
			IsInstance(member, "ToString", String, [String, FormatProvider]);
	}

	public static bool TryCreateShadowBinding(FrameworkMemberId referenced, out FrameworkBinding binding)
	{
		binding = null!;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		if (!Contains(member)) return false;
		var method = member.Name switch
		{
			".ctor" => ".ctor",
			"Add" when member.Signature.ParameterTypes.Length == 2 => "AddWithWrappedDays",
			"Add" => "Add",
			"CompareTo" => "CompareTo",
			"Equals" => "Equals",
			"get_Hour" => "GetHour",
			"get_Microsecond" => "GetMicrosecond",
			"get_Millisecond" => "GetMillisecond",
			"get_Minute" => "GetMinute",
			"get_Second" => "GetSecond",
			"get_Ticks" => "GetTicks",
			"op_Equality" => "AreEqual",
			"op_GreaterThan" => "IsGreaterThan",
			"op_GreaterThanOrEqual" => "IsGreaterThanOrEqual",
			"op_Inequality" => "AreNotEqual",
			"op_LessThan" => "IsLessThan",
			"op_LessThanOrEqual" => "IsLessThanOrEqual",
			"ToString" => member.Signature.ParameterTypes.Length == 2 ? "FormatRoundTripWithProvider" : "FormatRoundTrip",
			_ => null
		};
		if (method is null) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowTimeOnly", method);
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}", Effects,
			Reason: "The bounded TimeOnly surface uses the target's single-tick value representation for time-of-day arithmetic and fixed invariant round-trip formatting.",
			ShadowMethod: shadow);
		return true;
	}

	public static bool IsInvariantParseMember(FrameworkMemberId referenced)
	{
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		return IsStatic(member, "Parse", TimeOnly, [String, FormatProvider]) ||
			IsStatic(member, "TryParse", Boolean, [String, FormatProvider, FrameworkTypeId.ByReference(TimeOnly)]);
	}

	public static bool TryCreateInvariantParsingShadowBinding(FrameworkMemberId referenced, out FrameworkBinding binding)
	{
		binding = null!;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var method = IsStatic(member, "Parse", TimeOnly, [String, FormatProvider]) ? "ParseInvariant" :
			IsStatic(member, "TryParse", Boolean, [String, FormatProvider, FrameworkTypeId.ByReference(TimeOnly)]) ? "TryParseInvariant" : null;
		if (method is null) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowTimeOnlyParsing", method);
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedGc, FrameworkFeature.ManagedStrings]),
			Reason: "Invariant TimeOnly parsing requires proven invariant-provider Parse/TryParse arguments and a documented canonical time grammar.",
			ShadowMethod: shadow);
		return true;
	}

	public static FrameworkEffectSummary Effects { get; } = new(
		FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
		FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
		[FrameworkFeature.ManagedExceptions, FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedGc]);

	private static bool IsInstance(FrameworkMemberId member, string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		member.DeclaringType.Equals(TimeOnly) && member.Name == name && member.Signature.IsInstance &&
		member.Signature.GenericParameterCount == 0 && member.Signature.ParameterTypes.SequenceEqual(parameters) &&
		member.Signature.ReturnType.Equals(result);

	private static bool IsStatic(FrameworkMemberId member, string name, FrameworkTypeId result, FrameworkTypeId[] parameters) =>
		member.DeclaringType.Equals(TimeOnly) && member.Name == name && !member.Signature.IsInstance &&
		member.Signature.GenericParameterCount == 0 && member.Signature.ParameterTypes.SequenceEqual(parameters) &&
		member.Signature.ReturnType.Equals(result);
}
