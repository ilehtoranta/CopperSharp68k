/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Text.Json;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Framework;

internal sealed class Net10FrameworkContract
{
	private const string ResourceName =
		"CopperSharp.Compiler.Framework.net10.0-10.0.9.json";

	private readonly HashSet<string> _assemblies;
	private readonly FrameworkBindingRule[] _bindings;
	private readonly string[] _deferredTypePrefixes;

	private Net10FrameworkContract(FrameworkContractManifest manifest)
	{
		Validate(manifest);
		Contract = new M68kFrameworkContract(
			manifest.TargetFramework,
			manifest.ReferencePack,
			manifest.ReferencePackVersion,
			manifest.SchemaVersion);
		_assemblies = manifest.Assemblies.ToHashSet(StringComparer.Ordinal);
		_bindings = manifest.Bindings.ToArray();
		_deferredTypePrefixes = manifest.DeferredTypePrefixes.ToArray();
	}

	public static Net10FrameworkContract Default { get; } = Load();

	public M68kFrameworkContract Contract { get; }

	public bool IsFrameworkAssembly(string assemblyName) =>
		_assemblies.Contains(assemblyName);

	internal static void ValidateManifestJson(string json)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(json);
		var manifest = JsonSerializer.Deserialize<FrameworkContractManifest>(
			json,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
			throw new InvalidOperationException("The framework contract manifest is empty.");
		Validate(manifest);
	}

	public FrameworkBindingDecision Classify(
		FrameworkMemberId exactMember,
		CilMethodReferenceIdentity member,
		MethodReference? resolved,
		M68kCompilationException? resolutionFailure)
	{
		var rules = _bindings.Where(candidate => candidate.Matches(member)).ToArray();
		if (resolved?.FrameworkBinding is { } binding)
		{
			if (!binding.Member.Equals(exactMember))
			{
				return new FrameworkBindingDecision(
					M68kFrameworkCompatibilityStatus.Unsupported,
					null,
					"The resolved framework binding identity does not equal the referenced metadata identity.",
					Array.Empty<string>(),
					Array.Empty<string>());
			}
			if (rules.Length == 0)
			{
				return new FrameworkBindingDecision(
					M68kFrameworkCompatibilityStatus.Unsupported,
					null,
					"The exact compiler binding is missing from the compatibility ledger.",
					Array.Empty<string>(),
					Array.Empty<string>());
			}

			var status = BindingStatus(binding.Kind);
			var effectNames = EffectNames(binding.EffectSummary.Effects);
			var featureNames = binding.EffectSummary.RequiredFeatures
				.Select(static feature => feature.Name)
				.Order(StringComparer.Ordinal)
				.ToArray();
			var matchedRule = rules.SingleOrDefault(candidate =>
				status == ParseStatus(candidate.Status) &&
				MatchesTarget(candidate.Target ?? string.Empty, binding.Target ?? string.Empty) &&
				candidate.Effects.Order(StringComparer.Ordinal).SequenceEqual(
					effectNames.Order(StringComparer.Ordinal)) &&
				candidate.Features.Order(StringComparer.Ordinal).SequenceEqual(featureNames));
			if (matchedRule is null)
			{
				return new FrameworkBindingDecision(
					M68kFrameworkCompatibilityStatus.Unsupported,
					null,
					$"Compiler binding '{binding.Target}' does not agree with the " +
						"ledgered dispositions for the public member.",
					Array.Empty<string>(),
					Array.Empty<string>());
			}

			return new FrameworkBindingDecision(
				status,
				binding.Target,
				binding.Reason,
				effectNames,
				featureNames);
		}

		var rule = rules.Length switch
		{
			0 => null,
			1 => rules[0],
			_ when resolved?.ImportName is { } importName =>
				rules.SingleOrDefault(candidate =>
					MatchesTarget(candidate.Target ?? string.Empty, importName)),
			_ => null
		};
		if (rules.Length > 1 && rule is null)
		{
			return new FrameworkBindingDecision(
				M68kFrameworkCompatibilityStatus.Unsupported,
				null,
				"The public member has context-specialized ledger dispositions, but " +
					"metadata resolution did not select one.",
				Array.Empty<string>(),
				Array.Empty<string>());
		}
		if (rule is not null)
		{
			var status = ParseStatus(rule.Status);
			if (status == M68kFrameworkCompatibilityStatus.Intrinsic &&
				resolved?.FrameworkBinding is null)
			{
				return new FrameworkBindingDecision(
					M68kFrameworkCompatibilityStatus.Unsupported,
					null,
					"The ledgered intrinsic did not resolve through the exact framework binding registry.",
					Array.Empty<string>(),
					Array.Empty<string>());
			}
			if (status == M68kFrameworkCompatibilityStatus.Intrinsic &&
				(resolved?.ImportName is null ||
				 !MatchesTarget(rule.Target!, resolved.ImportName)))
			{
				return new FrameworkBindingDecision(
					M68kFrameworkCompatibilityStatus.Unsupported,
					null,
					$"The compatibility ledger expects '{rule.Target}', but metadata resolution " +
					$"produced '{resolved?.ImportName ?? "no binding"}'.",
					rule.Effects,
					rule.Features);
			}

			return new FrameworkBindingDecision(
				status,
				resolved?.ImportName ?? rule.Target,
				rule.Reason,
				rule.Effects,
				rule.Features);
		}

		if (_deferredTypePrefixes.Any(prefix =>
			member.TypeName.StartsWith(prefix, StringComparison.Ordinal)))
		{
			return new FrameworkBindingDecision(
				M68kFrameworkCompatibilityStatus.Deferred,
				null,
				"The member belongs to a framework capability deferred by the initial profile.",
				Array.Empty<string>(),
				Array.Empty<string>());
		}

		if (resolved?.Definition is { IsImport: false })
		{
			return new FrameworkBindingDecision(
				M68kFrameworkCompatibilityStatus.Implemented,
				resolved.Definition.DisplayName,
				null,
				Array.Empty<string>(),
				Array.Empty<string>());
		}

		if (resolved?.Definition is { IsImport: true } platformDefinition &&
			platformDefinition.ExternalCall is not null)
		{
			return new FrameworkBindingDecision(
				M68kFrameworkCompatibilityStatus.Platform,
				platformDefinition.ExternalCall.Convention.Identity,
				null,
				Array.Empty<string>(),
				Array.Empty<string>());
		}

		var reason = resolutionFailure?.Message ??
			(resolved?.ImportName is not null
				? $"Framework binding '{resolved.ImportName}' is not present in the compatibility ledger."
				: "No compatible framework implementation is registered.");
		return new FrameworkBindingDecision(
			M68kFrameworkCompatibilityStatus.Unsupported,
			null,
			reason,
			Array.Empty<string>(),
			Array.Empty<string>());
	}

	private static M68kFrameworkCompatibilityStatus BindingStatus(
		FrameworkBindingKind kind) => kind switch
		{
			FrameworkBindingKind.ManagedBody or FrameworkBindingKind.ShadowMethod =>
				M68kFrameworkCompatibilityStatus.Implemented,
			FrameworkBindingKind.Intrinsic => M68kFrameworkCompatibilityStatus.Intrinsic,
			FrameworkBindingKind.PlatformOperation => M68kFrameworkCompatibilityStatus.Platform,
			FrameworkBindingKind.Unsupported => M68kFrameworkCompatibilityStatus.Unsupported,
			_ => throw new InvalidOperationException($"Unknown framework binding kind {kind}.")
		};

	private static string[] EffectNames(FrameworkEffects effects) =>
		Enum.GetValues<FrameworkEffects>()
			.Where(effect => effect != FrameworkEffects.None && effects.HasFlag(effect))
			.Select(static effect => effect.ToString())
			.ToArray();

	private static bool MatchesTarget(string pattern, string actual) =>
		pattern.EndsWith('*')
			? actual.StartsWith(pattern[..^1], StringComparison.Ordinal)
			: string.Equals(pattern, actual, StringComparison.Ordinal);

	private static Net10FrameworkContract Load()
	{
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName) ??
			throw new InvalidOperationException(
				$"Embedded framework contract '{ResourceName}' was not found.");
		var manifest = JsonSerializer.Deserialize<FrameworkContractManifest>(
			stream,
			new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			}) ?? throw new InvalidOperationException(
				$"Embedded framework contract '{ResourceName}' is empty.");
		return new Net10FrameworkContract(manifest);
	}

	private static void Validate(FrameworkContractManifest manifest)
	{
		if (manifest.SchemaVersion != 1 ||
			!string.Equals(manifest.TargetFramework, "net10.0", StringComparison.Ordinal) ||
			!string.Equals(
				manifest.ReferencePack,
				"Microsoft.NETCore.App.Ref",
				StringComparison.Ordinal) ||
			string.IsNullOrWhiteSpace(manifest.ReferencePackVersion))
		{
			throw new InvalidOperationException(
				"The embedded .NET framework contract descriptor is invalid or unsupported.");
		}

		if (manifest.Assemblies.Count == 0 ||
			manifest.Assemblies.Any(string.IsNullOrWhiteSpace) ||
			manifest.Assemblies.Distinct(StringComparer.Ordinal).Count() !=
				manifest.Assemblies.Count)
		{
			throw new InvalidOperationException(
				"The embedded framework assembly inventory must be non-empty and unique.");
		}

		var keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var binding in manifest.Bindings)
		{
			_ = ParseStatus(binding.Status);
			if (string.IsNullOrWhiteSpace(binding.Assembly) ||
				string.IsNullOrWhiteSpace(binding.Type) ||
				string.IsNullOrWhiteSpace(binding.Member) ||
				string.IsNullOrWhiteSpace(binding.ReturnType) ||
				binding.ParameterCount < 0 ||
				binding.ParameterTypes.Count != binding.ParameterCount ||
				!keys.Add(binding.Key))
			{
				throw new InvalidOperationException(
					"The embedded framework binding inventory contains an invalid or duplicate rule.");
			}
			if (ParseStatus(binding.Status) == M68kFrameworkCompatibilityStatus.Intrinsic &&
				string.IsNullOrWhiteSpace(binding.Target))
			{
				throw new InvalidOperationException(
					"Every intrinsic framework binding must name its compiler target.");
			}
		}
	}

	private static M68kFrameworkCompatibilityStatus ParseStatus(string value) =>
		Enum.TryParse<M68kFrameworkCompatibilityStatus>(value, ignoreCase: true, out var status)
			? status
			: throw new InvalidOperationException(
				$"Unknown framework compatibility status '{value}'.");

	private sealed class FrameworkContractManifest
	{
		public int SchemaVersion { get; init; }

		public string TargetFramework { get; init; } = string.Empty;

		public string ReferencePack { get; init; } = string.Empty;

		public string ReferencePackVersion { get; init; } = string.Empty;

		public List<string> Assemblies { get; init; } = [];

		public List<FrameworkBindingRule> Bindings { get; init; } = [];

		public List<string> DeferredTypePrefixes { get; init; } = [];
	}

	private sealed class FrameworkBindingRule
	{
		public string Assembly { get; init; } = string.Empty;

		public string Type { get; init; } = string.Empty;

		public string Member { get; init; } = string.Empty;

		public bool IsStatic { get; init; }

		public int GenericArity { get; init; }

		public int ParameterCount { get; init; }

		public string ReturnType { get; init; } = string.Empty;

		public List<string> ParameterTypes { get; init; } = [];

		public string Status { get; init; } = string.Empty;

		public string? Target { get; init; }

		public string? Reason { get; init; }

		public List<string> Effects { get; init; } = [];

		public List<string> Features { get; init; } = [];

		public string Key => string.Join(
			'\u001f',
			Assembly,
			Type,
			Member,
			IsStatic,
			GenericArity,
			ReturnType,
			string.Join('\u001e', ParameterTypes),
			Status,
			Target ?? string.Empty,
			string.Join('\u001e', Effects.Order(StringComparer.Ordinal)),
			string.Join('\u001e', Features.Order(StringComparer.Ordinal)));

		public bool Matches(CilMethodReferenceIdentity member) =>
			(Assembly == "*" || string.Equals(Assembly, member.AssemblyName, StringComparison.Ordinal)) &&
			MatchesType(Type, member.TypeName) &&
			string.Equals(Member, member.Name, StringComparison.Ordinal) &&
			IsStatic == member.IsStatic &&
			GenericArity == member.GenericArity &&
			string.Equals(ReturnType, member.ReturnType, StringComparison.Ordinal) &&
			ParameterTypes.SequenceEqual(member.ParameterTypes);

		private static bool MatchesType(string pattern, string typeName)
		{
			const string genericWildcard = "<*>";
			return pattern.EndsWith(genericWildcard, StringComparison.Ordinal)
				? typeName.StartsWith(
					pattern[..^genericWildcard.Length] + "<",
					StringComparison.Ordinal) && typeName.EndsWith('>')
				: string.Equals(pattern, typeName, StringComparison.Ordinal);
		}
	}
}

internal sealed record FrameworkBindingDecision(
	M68kFrameworkCompatibilityStatus Status,
	string? Binding,
	string? Reason,
	IReadOnlyList<string> Effects,
	IReadOnlyList<string> RequiredFeatures);
