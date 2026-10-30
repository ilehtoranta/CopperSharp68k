/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderEffectsTests
{
	[Fact]
	public void DeclarationsMatchEveryPinnedPublicDefinitionWithoutEnablingStableAdmission()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var stream = File.OpenRead(pack.AssemblyPath);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();
		var provider = new SignatureProvider();
		var handle = reader.TypeDefinitions.Single(handle => {
			var type = reader.GetTypeDefinition(handle);
			return reader.GetString(type.Namespace) == "System.Text" && reader.GetString(type.Name) == "StringBuilder";
		});
		var actual = reader.GetTypeDefinition(handle).GetMethods().Select(reader.GetMethodDefinition)
			.Where(method => (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public &&
				(method.Attributes & MethodAttributes.Static) == 0)
			.Select(method => new FrameworkMemberId(provider.GetTypeFromDefinition(reader, handle, 0),
				reader.GetString(method.Name), FrameworkMethodSignatureId.From(method.DecodeSignature(provider, (object?)null))))
			.Select(FrameworkImplementationProfile.Canonicalize).ToHashSet();
		Assert.Equal(103, actual.Count);
		Assert.Equal(103, StringBuilderFrameworkSurface.Members.Count);
		Assert.True(actual.SetEquals(StringBuilderFrameworkSurface.Members));
		foreach (var member in actual) {
			Assert.True(StringBuilderFrameworkSurface.TryGetEffects(member, out var effects), member.DisplayName);
			Assert.Contains(FrameworkFeature.StringBuilder, effects.RequiredFeatures);
			Assert.True(effects.Effects.HasFlag(FrameworkEffects.MayThrow));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedBinding(member, null, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreatePinnedBinding(member, null, true, out var binding));
			Assert.Equal(effects.Effects, binding.EffectSummary.Effects);
		}
	}

	[Fact]
	public void HelperDeclarationsAreExactPrivatePinnedDefinitions()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var stream = File.OpenRead(pack.AssemblyPath);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();
		var provider = new SignatureProvider();
		var handle = reader.TypeDefinitions.Single(handle => {
			var type = reader.GetTypeDefinition(handle);
			return reader.GetString(type.Namespace) == "System.Text" && reader.GetString(type.Name) == "StringBuilder";
		});
		var privateDefinitions = reader.GetTypeDefinition(handle).GetMethods().Select(reader.GetMethodDefinition)
			.Where(method => (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
			.Select(method => FrameworkImplementationProfile.Canonicalize(new FrameworkMemberId(provider.GetTypeFromDefinition(reader, handle, 0),
				reader.GetString(method.Name), FrameworkMethodSignatureId.From(method.DecodeSignature(provider, (object?)null))))).ToHashSet();
		Assert.Equal(24, StringBuilderFrameworkSurface.Helpers.Count);
		Assert.Equal(24, StringBuilderFrameworkSurface.Helpers.Distinct().Count());
		foreach (var helper in StringBuilderFrameworkSurface.Helpers) {
			Assert.Contains(helper, privateDefinitions);
			Assert.DoesNotContain(helper, StringBuilderFrameworkSurface.Members);
			Assert.True(StringBuilderFrameworkSurface.TryGetEffects(helper, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedBinding(helper, null, false, out _));
			var altered = new FrameworkMemberId(helper.DeclaringType, helper.Name,
				new(helper.Signature.Header, helper.Signature.GenericParameterCount, helper.Signature.RequiredParameterCount,
					FrameworkTypeId.Primitive("System.TypedReference"), helper.Signature.ParameterTypes));
			Assert.False(StringBuilderFrameworkSurface.TryGetEffects(altered, out _));
		}
		var unlisted = privateDefinitions.Except(StringBuilderFrameworkSurface.Helpers).ToArray();
		Assert.NotEmpty(unlisted);
		foreach (var member in unlisted) Assert.False(StringBuilderFrameworkSurface.TryGetEffects(member, out _), member.DisplayName);
	}

	[Fact]
	public void NestedProtocolDeclarationsMatchThePinnedMetadataAndRejectOtherOwners()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var stream = File.OpenRead(pack.AssemblyPath);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();
		var provider = new SignatureProvider();
		var definitions = reader.TypeDefinitions.SelectMany(handle => {
			var owner = FrameworkImplementationProfile.Canonicalize(new FrameworkMemberId(provider.GetTypeFromDefinition(reader, handle, 0), "",
				new(0, 0, 0, FrameworkTypeId.Primitive("System.Void"), []))).DeclaringType;
			if (!StringBuilderFrameworkSurface.NestedMembers.Any(member => member.DeclaringType.Equals(owner))) return Enumerable.Empty<FrameworkMemberId>();
			return reader.GetTypeDefinition(handle).GetMethods().Select(reader.GetMethodDefinition).Select(method =>
				FrameworkImplementationProfile.Canonicalize(new FrameworkMemberId(provider.GetTypeFromDefinition(reader, handle, 0), reader.GetString(method.Name),
					FrameworkMethodSignatureId.From(method.DecodeSignature(provider, (object?)null)))));
		}).ToHashSet();
		Assert.Equal(21, definitions.Count);
		Assert.True(definitions.SetEquals(StringBuilderFrameworkSurface.NestedMembers));
		foreach (var member in definitions) {
			Assert.True(StringBuilderFrameworkSurface.TryGetEffects(member, out _), member.DisplayName);
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedBinding(member, null, false, out _));
			var unrelated = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", member.DeclaringType.MetadataName!), member.Name, member.Signature);
			Assert.False(StringBuilderFrameworkSurface.TryGetEffects(unrelated, out _));
			var altered = new FrameworkMemberId(member.DeclaringType, member.Name,
				new(member.Signature.Header, member.Signature.GenericParameterCount, member.Signature.RequiredParameterCount,
					FrameworkTypeId.Primitive("System.TypedReference"), member.Signature.ParameterTypes));
			Assert.False(StringBuilderFrameworkSurface.TryGetEffects(altered, out _));
		}
	}

	[Fact]
	public void ChunkEnumerationDeclaresIndexAllocationAtConstructionAndAllocationFreeAdvance()
	{
		var getChunks = Find("GetChunks");
		Assert.True(StringBuilderFrameworkSurface.TryGetEffects(getChunks, out var creation));
		Assert.True(creation.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
		Assert.Contains(FrameworkFeature.ManagedGc, creation.RequiredFeatures);
		foreach (var member in StringBuilderFrameworkSurface.NestedMembers.Where(member => member.DeclaringType.MetadataName is "ChunkEnumerator" or "ManyChunkInfo")) {
			Assert.True(StringBuilderFrameworkSurface.TryGetEffects(member, out var effects));
			Assert.Equal(member.Name == ".ctor", effects.Effects.HasFlag(FrameworkEffects.MayAllocate));
			Assert.Equal(member.Name == ".ctor", effects.Effects.HasFlag(FrameworkEffects.MayCollect));
			Assert.Equal(member.Name is ".ctor" or "MoveNext", effects.Effects.HasFlag(FrameworkEffects.WritesManagedMemory));
		}
	}

	[Fact]
	public void HelperEffectsSeparateChunkGrowthFromAllocationFreeTraversalAndCopies()
	{
		FrameworkEffectSummary Effects(string name, int parameters) {
			var helper = StringBuilderFrameworkSurface.Helpers.Single(member => member.Name == name && member.Signature.ParameterTypes.Length == parameters);
			Assert.True(StringBuilderFrameworkSurface.TryGetEffects(helper, out var effects));
			return effects;
		}
		foreach (var name in new[] { "FindChunkForIndex", "Next" }) {
			var effects = Effects(name, 1);
			Assert.Equal(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.MayThrow, effects.Effects);
		}
		foreach (var (name, count) in new[] { ("Remove", 4), ("ReplaceInPlaceAtChunk", 4), ("<AppendFormat>g__MoveNext|121_0", 2), (".ctor", 1) }) {
			var effects = Effects(name, count);
			Assert.True(effects.Effects.HasFlag(FrameworkEffects.WritesManagedMemory));
			Assert.False(effects.Effects.HasFlag(FrameworkEffects.MayAllocate));
			Assert.DoesNotContain(FrameworkFeature.ManagedGc, effects.RequiredFeatures);
		}
		foreach (var (name, count) in new[] { ("ExpandByABlock", 1), ("MakeRoom", 5), ("ReplaceAllInChunk", 4) }) {
			var effects = Effects(name, count);
			Assert.True(effects.Effects.HasFlag(FrameworkEffects.WritesManagedMemory | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
			Assert.Contains(FrameworkFeature.ManagedGc, effects.RequiredFeatures);
		}
		Assert.True(Effects("ReplaceInPlaceAtChunk", 4).Effects.HasFlag(FrameworkEffects.ReadsNativeMemory));
		Assert.Contains(FrameworkFeature.Spans, Effects("get_RemainingCurrentChunk", 0).RequiredFeatures);
	}

	[Fact]
	public void AdjacentSignaturesAndApplicationLookalikesHaveNoDeclaration()
	{
		var member = Find("Append", FrameworkTypeId.Primitive("System.String"));
		var signature = member.Signature;
		FrameworkMemberId Changed(byte? header = null, int? arity = null, int? required = null,
			FrameworkTypeId? result = null, FrameworkTypeId[]? parameters = null) => new(member.DeclaringType, member.Name,
				new(header ?? signature.Header, arity ?? 0, required ?? 1, result ?? signature.ReturnType, parameters ?? [.. signature.ParameterTypes]));
		FrameworkMemberId[] rejected = [
			new(FrameworkTypeId.Named("Application", "System.Text.StringBuilder"), member.Name, signature),
			new(member.DeclaringType, "AppendUnknown", signature), Changed(header: 0), Changed(header: 0x60),
			Changed(arity: 1), Changed(required: 0), Changed(result: FrameworkTypeId.Primitive("System.Void")),
			Changed(parameters: [FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.String"))]),
			new(member.DeclaringType, member.Name, signature, [FrameworkTypeId.Primitive("System.Int32")])
		];
		foreach (var candidate in rejected) Assert.False(StringBuilderFrameworkSurface.TryGetEffects(candidate, out _), candidate.DisplayName);
	}

	[Fact]
	public void PublicAdmissionCandidatesExcludePrivateAndNestedImplementationHelpers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var stream = File.OpenRead(pack.AssemblyPath);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();
		var provider = new SignatureProvider();
		bool PublicType(TypeDefinitionHandle handle) {
			var type = reader.GetTypeDefinition(handle);
			return type.GetDeclaringType().IsNil ? (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public :
				(type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic && PublicType(type.GetDeclaringType());
		}
		var actual = reader.TypeDefinitions.Where(PublicType).SelectMany(handle => {
			var owner = provider.GetTypeFromDefinition(reader, handle, 0);
			if (owner.FullMetadataName is not ("System.Text.StringBuilder" or "System.Text.StringBuilder+ChunkEnumerator" or
				"System.Text.StringBuilder+AppendInterpolatedStringHandler")) return Enumerable.Empty<FrameworkMemberId>();
			return reader.GetTypeDefinition(handle).GetMethods().Select(reader.GetMethodDefinition)
				.Where(method => (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public)
				.Select(method => FrameworkImplementationProfile.Canonicalize(new FrameworkMemberId(owner, reader.GetString(method.Name),
					FrameworkMethodSignatureId.From(method.DecodeSignature(provider, (object?)null)))));
		}).ToHashSet();
		Assert.Equal(118, actual.Count);
		Assert.True(actual.SetEquals(StringBuilderFrameworkSurface.PublicMembers));
		foreach (var member in actual) {
			Assert.True(StringBuilderFrameworkSurface.TryGetPublicDefinition(member, out var definition));
			Assert.Equal(member, definition);
		}
		foreach (var member in StringBuilderFrameworkSurface.Helpers.Concat(StringBuilderFrameworkSurface.NestedMembers.Except(actual)))
			Assert.False(StringBuilderFrameworkSurface.TryGetPublicDefinition(member, out _), member.DisplayName);
	}

	[Fact]
	public void GenericDeclarationsMatchDefinitionAndSpecializedObservationForms()
	{
		var definition = StringBuilderFrameworkSurface.Members.Single(member => member.Name == "AppendJoin" &&
			member.Signature.GenericParameterCount == 1 && member.Signature.ParameterTypes[0].Equals(FrameworkTypeId.Primitive("System.Char")));
		var argument = FrameworkTypeId.Primitive("System.Int32");
		var enumerable = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerable`1"), [argument]);
		var closed = new FrameworkMemberId(definition.DeclaringType, definition.Name,
			new(0x30, 1, 2, definition.Signature.ReturnType, [FrameworkTypeId.Primitive("System.Char"), enumerable]), [argument]);
		Assert.True(StringBuilderFrameworkSurface.TryGetEffects(closed, out _));
		Assert.True(StringBuilderFrameworkSurface.TryGetPublicDefinition(closed, out var canonicalDefinition));
		Assert.Equal(definition, canonicalDefinition);
		Assert.True(StringBuilderFrameworkSurface.TryGetEffects(new(definition.DeclaringType, definition.Name, definition.Signature, [argument]), out _));
		Assert.False(StringBuilderFrameworkSurface.TryGetEffects(new(closed.DeclaringType, closed.Name, closed.Signature, [argument, argument]), out _));
		Assert.False(StringBuilderFrameworkSurface.TryGetEffects(new(closed.DeclaringType, closed.Name, closed.Signature, [FrameworkTypeId.Primitive("System.Int64")]), out _));
	}

	[Fact]
	public void EffectsDistinguishReadingAllocatingMutatingAndNativeDestinations()
	{
		FrameworkEffectSummary Effects(FrameworkMemberId member) {
			Assert.True(StringBuilderFrameworkSurface.TryGetEffects(member, out var effects));
			return effects;
		}
		var getter = Effects(Find("get_Length"));
		Assert.Equal(FrameworkEffects.ReadsManagedMemory | FrameworkEffects.MayThrow, getter.Effects);
		var snapshot = Effects(Find("ToString"));
		Assert.True(snapshot.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
		Assert.False(snapshot.Effects.HasFlag(FrameworkEffects.WritesManagedMemory));
		var append = Effects(Find("Append", FrameworkTypeId.Primitive("System.Char")));
		Assert.True(append.Effects.HasFlag(FrameworkEffects.WritesManagedMemory | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
		Assert.DoesNotContain(FrameworkFeature.NativeMemory, append.RequiredFeatures);
		var pointer = Effects(Find("Append", FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Char")), FrameworkTypeId.Primitive("System.Int32")));
		Assert.True(pointer.Effects.HasFlag(FrameworkEffects.ReadsNativeMemory));
		Assert.False(pointer.Effects.HasFlag(FrameworkEffects.RetainsNativePointer));
		var copy = Effects(Find("CopyTo", FrameworkTypeId.Primitive("System.Int32"),
			FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [FrameworkTypeId.Primitive("System.Char")]), FrameworkTypeId.Primitive("System.Int32")));
		Assert.True(copy.Effects.HasFlag(FrameworkEffects.WritesNativeMemory | FrameworkEffects.WritesManagedMemory));
		Assert.False(copy.Effects.HasFlag(FrameworkEffects.MayAllocate));
		Assert.Contains(FrameworkFeature.Spans, copy.RequiredFeatures);
	}

	private static FrameworkMemberId Find(string name, params FrameworkTypeId[] parameters) =>
		StringBuilderFrameworkSurface.Members.Single(member => member.Name == name && member.Signature.ParameterTypes.SequenceEqual(parameters));

	private sealed class SignatureProvider : ISignatureTypeProvider<FrameworkTypeId, object?>
	{
		public FrameworkTypeId GetArrayType(FrameworkTypeId element, ArrayShape shape) => FrameworkTypeId.Array(element, shape);
		public FrameworkTypeId GetByReferenceType(FrameworkTypeId element) => FrameworkTypeId.ByReference(element);
		public FrameworkTypeId GetFunctionPointerType(MethodSignature<FrameworkTypeId> signature) => FrameworkTypeId.FunctionPointer(FrameworkMethodSignatureId.From(signature));
		public FrameworkTypeId GetGenericInstantiation(FrameworkTypeId type, ImmutableArray<FrameworkTypeId> arguments) => FrameworkTypeId.GenericInstantiation(type, arguments);
		public FrameworkTypeId GetGenericMethodParameter(object? context, int index) => FrameworkTypeId.GenericMethodParameter(index);
		public FrameworkTypeId GetGenericTypeParameter(object? context, int index) => FrameworkTypeId.GenericTypeParameter(index);
		public FrameworkTypeId GetModifiedType(FrameworkTypeId modifier, FrameworkTypeId type, bool required) => FrameworkTypeId.Modified(modifier, type, required);
		public FrameworkTypeId GetPinnedType(FrameworkTypeId element) => element;
		public FrameworkTypeId GetPointerType(FrameworkTypeId element) => FrameworkTypeId.Pointer(element);
		public FrameworkTypeId GetPrimitiveType(PrimitiveTypeCode code) => FrameworkTypeId.Primitive("System." + code);
		public FrameworkTypeId GetSZArrayType(FrameworkTypeId element) => FrameworkTypeId.SzArray(element);
		public FrameworkTypeId GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte kind) {
			var type = reader.GetTypeDefinition(handle);
			var parent = type.GetDeclaringType().IsNil ? null : GetTypeFromDefinition(reader, type.GetDeclaringType(), kind);
			return FrameworkTypeId.Named(reader.GetString(reader.GetAssemblyDefinition().Name),
				parent is null ? reader.GetString(type.Namespace) + "." + reader.GetString(type.Name) : reader.GetString(type.Name), parent);
		}
		public FrameworkTypeId GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte kind) {
			var type = reader.GetTypeReference(handle);
			var parent = type.ResolutionScope.Kind == HandleKind.TypeReference ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, kind) : null;
			var assembly = parent?.AssemblyName ?? (type.ResolutionScope.Kind == HandleKind.AssemblyReference
				? reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name) : reader.GetString(reader.GetAssemblyDefinition().Name));
			return FrameworkTypeId.Named(assembly, parent is null ? reader.GetString(type.Namespace) + "." + reader.GetString(type.Name) : reader.GetString(type.Name), parent);
		}
		public FrameworkTypeId GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte kind) => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
	}
}
