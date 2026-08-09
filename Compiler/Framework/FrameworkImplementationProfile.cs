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
	private const string ObjectType = "System.Object";

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
		out FrameworkBinding binding)
	{
		var member = Canonicalize(referencedMember);
		if (fallback is null ||
			!IsPinnedStopwatchMember(member) ||
			!fallback.Member.Equals(member))
		{
			binding = null!;
			return false;
		}

		binding = new FrameworkBinding(
			member,
			FrameworkBindingKind.PinnedManagedBody,
			$"pinned:[{ImplementationAssembly}]{StopwatchType}::{member.Name}",
			fallback.EffectSummary,
			ShadowMethod: fallback.ShadowMethod,
			TypeInitializerPolicy: FrameworkTypeInitializerPolicy.TargetOwned);
		return true;
	}

	public static bool IsPinnedBinding(FrameworkBinding binding) =>
		binding.Kind == FrameworkBindingKind.PinnedManagedBody &&
		IsPinnedStopwatchMember(binding.Member) &&
		string.Equals(
			binding.Target,
			$"pinned:[{ImplementationAssembly}]{StopwatchType}::{binding.Member.Name}",
			StringComparison.Ordinal) &&
		binding.TypeInitializerPolicy == FrameworkTypeInitializerPolicy.TargetOwned;

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
			 string.Equals(member.Name, "GetTimestamp", StringComparison.Ordinal) &&
			 binding.Kind == FrameworkBindingKind.PlatformOperation);
	}

	public static bool IsPinnedTypeBoundary(FrameworkMemberId referencedMember)
	{
		var member = Canonicalize(referencedMember);
		return string.Equals(member.AssemblyName, ContractAssembly, StringComparison.Ordinal) &&
			member.DeclaringType.Kind == FrameworkTypeKind.Named &&
			string.Equals(
				member.DeclaringType.MetadataName,
				StopwatchType,
				StringComparison.Ordinal);
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

	private static bool IsPrimitive(FrameworkTypeId type, string metadataName) =>
		type.Kind == FrameworkTypeKind.Primitive &&
		string.Equals(type.MetadataName, metadataName, StringComparison.Ordinal);

	private static bool IsNamedStopwatch(FrameworkTypeId type) =>
		type.Kind == FrameworkTypeKind.Named &&
		string.Equals(type.AssemblyName, ContractAssembly, StringComparison.Ordinal) &&
		string.Equals(type.MetadataName, StopwatchType, StringComparison.Ordinal);

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
