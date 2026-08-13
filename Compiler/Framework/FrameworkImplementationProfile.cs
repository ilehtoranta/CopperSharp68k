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

	public static bool TryCreatePinnedBinding(
		FrameworkMemberId referencedMember,
		FrameworkBinding? fallback,
		bool enableUnlistedManagedBodies,
		out FrameworkBinding binding)
	{
		var canonicalMember = Canonicalize(referencedMember);
		var isOriginalPinnedSlice = fallback is not null &&
			IsPinnedMember(canonicalMember) &&
			fallback.Member.Equals(canonicalMember);
		if (!IsFrameworkImplementationCandidate(referencedMember) ||
			(!isOriginalPinnedSlice && !enableUnlistedManagedBodies) ||
			(!isOriginalPinnedSlice &&
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
			fallback?.EffectSummary ?? FrameworkEffectSummary.None,
			Reason: "Reachable CIL body loaded from the verified framework implementation pack.",
			TypeInitializerPolicy: isOriginalPinnedSlice
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
		  IsPinnedMember(Canonicalize(binding.Member))));

	public static bool TryCreateTargetRuntimeOverride(
		FrameworkMemberId referencedMember,
		bool enableUnlistedManagedBodies,
		out FrameworkBinding binding)
	{
		var member = Canonicalize(referencedMember);
		if (!enableUnlistedManagedBodies ||
			!string.Equals(member.AssemblyName, ContractAssembly, StringComparison.Ordinal) ||
			member.DeclaringType.Kind != FrameworkTypeKind.Named ||
			!string.Equals(member.DeclaringType.MetadataName, ExceptionType, StringComparison.Ordinal) ||
			!string.Equals(member.Name, "ToString", StringComparison.Ordinal) ||
			!member.Signature.IsInstance ||
			member.Signature.GenericParameterCount != 0 ||
			member.Signature.ParameterTypes.Length != 0 ||
			!IsPrimitive(member.Signature.ReturnType, "System.String"))
		{
			binding = null!;
			return false;
		}

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
			binding.PreservesVirtualDispatch == expected.PreservesVirtualDispatch;
	}

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
		$"pinned:[{ImplementationAssembly}]{GetNamedDeclaringType(member.DeclaringType).MetadataName}::{member.Name}";

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

	private static bool IsNamedStopwatch(FrameworkTypeId type) =>
		type.Kind == FrameworkTypeKind.Named &&
		string.Equals(type.AssemblyName, ContractAssembly, StringComparison.Ordinal) &&
		string.Equals(type.MetadataName, StopwatchType, StringComparison.Ordinal);

	private static bool IsNamedTimeSpan(FrameworkTypeId type) =>
		type.Kind == FrameworkTypeKind.Named &&
		string.Equals(type.AssemblyName, ContractAssembly, StringComparison.Ordinal) &&
		string.Equals(type.MetadataName, TimeSpanType, StringComparison.Ordinal);

	private static FrameworkTypeId CanonicalizeType(FrameworkTypeId type) =>
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
