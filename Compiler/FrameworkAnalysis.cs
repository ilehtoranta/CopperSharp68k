/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler;

/// <summary>The pinned framework contract used for compatibility analysis.</summary>
public sealed record M68kFrameworkContract(
	string TargetFramework,
	string ReferencePack,
	string ReferencePackVersion,
	int ManifestSchemaVersion);

/// <summary>Exact identity of one verified framework implementation assembly.</summary>
public sealed record M68kFrameworkImplementationAssemblyProvenance(
	string Name,
	string Version,
	string PublicKeyToken,
	Guid Mvid,
	string Sha256);

/// <summary>Verified framework implementation input used by one analysis.</summary>
public sealed record M68kFrameworkImplementationPackProvenance(
	int ManifestSchemaVersion,
	string PackId,
	string PackVersion,
	string RuntimeIdentifier,
	string TargetFramework,
	string ReferencePack,
	string ReferencePackVersion,
	string ImplementationProfile,
	IReadOnlyList<M68kFrameworkImplementationAssemblyProvenance> Assemblies);

/// <summary>Compatibility disposition for one reachable framework member.</summary>
public enum M68kFrameworkCompatibilityStatus
{
	Implemented,
	Intrinsic,
	Platform,
	Deferred,
	Unsupported
}

/// <summary>An exact framework member and, when applicable, method instantiation.</summary>
public sealed record M68kFrameworkMember(
	string AssemblyName,
	string TypeName,
	string Name,
	bool IsStatic,
	int GenericArity,
	string ReturnType,
	IReadOnlyList<string> ParameterTypes,
	IReadOnlyList<string> MethodTypeArguments)
{
	/// <summary>Stable human-readable identity used in reports and diagnostics.</summary>
	public string DisplayName
	{
		get
		{
			var instantiation = MethodTypeArguments.Count == 0
				? string.Empty
				: $"<{string.Join(",", MethodTypeArguments)}>";
			return $"[{AssemblyName}]{TypeName}::{Name}{instantiation} " +
				$"{(IsStatic ? "static" : "instance")} " +
				$"{ReturnType}({string.Join(",", ParameterTypes)})";
		}
	}
}

/// <summary>A reachable IL call site that refers to a framework member.</summary>
public sealed record M68kFrameworkCallSite(
	string Caller,
	int IlOffset,
	IReadOnlyList<string> RootPath);

/// <summary>A statically reachable managed heap-allocation instruction.</summary>
public sealed record M68kManagedAllocationSite(
	string Caller,
	int IlOffset,
	string Kind,
	string AllocatedType,
	IReadOnlyList<string> RootPath);

/// <summary>Compatibility analysis for one distinct reachable framework member.</summary>
public sealed record M68kFrameworkMemberAnalysis(
	M68kFrameworkMember Member,
	M68kFrameworkCompatibilityStatus Status,
	string? Binding,
	string? Reason,
	IReadOnlyList<string> Effects,
	IReadOnlyList<string> RequiredFeatures,
	IReadOnlyList<M68kFrameworkCallSite> CallSites);

/// <summary>Reachable framework inventory for one closed-world entry graph.</summary>
public sealed class M68kFrameworkAnalysisResult
{
	internal M68kFrameworkAnalysisResult(
		M68kFrameworkContract contract,
		IReadOnlyList<M68kFrameworkMemberAnalysis> members,
		IReadOnlyList<M68kManagedAllocationSite> managedAllocationSites,
		M68kFrameworkImplementationPackProvenance? implementationPack = null)
	{
		Contract = contract;
		Members = members;
		ManagedAllocationSites = managedAllocationSites;
		ImplementationPack = implementationPack;
	}

	public M68kFrameworkContract Contract { get; }

	public IReadOnlyList<M68kFrameworkMemberAnalysis> Members { get; }

	/// <summary>
	/// Verified implementation input used by this analysis, or <see langword="null"/>
	/// when framework bodies came only from compiler-owned intrinsics, PALs, and shadows.
	/// </summary>
	public M68kFrameworkImplementationPackProvenance? ImplementationPack { get; }

	/// <summary>
	/// Reachable <c>newobj</c> and <c>newarr</c> instructions that allocate on
	/// the managed heap. Value-type construction is excluded.
	/// </summary>
	public IReadOnlyList<M68kManagedAllocationSite> ManagedAllocationSites { get; }

	public bool IsCompatible => Members.All(static member =>
		member.Status is
			M68kFrameworkCompatibilityStatus.Implemented or
			M68kFrameworkCompatibilityStatus.Intrinsic or
			M68kFrameworkCompatibilityStatus.Platform);
}
