/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Metadata;

internal sealed partial class CompilationModule
{
	private bool TryGetGuidStorageLayout(System.Reflection.Metadata.TypeDefinitionHandle handle, out CilTypeLayout layout)
	{
		layout = null!;
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true ||
			!(_assemblyName == "System.Private.CoreLib" && GetTypeName(handle) == "System.Guid" ||
			  _assemblyName == "CopperSharp.Runtime.Managed" && GetTypeName(handle) == "CopperSharp.Runtime.ShadowGuid")) return false;
		var definition = Reader.GetTypeDefinition(handle);
		if (!IsValueTypeDefinition(definition) ||
			(definition.Attributes & System.Reflection.TypeAttributes.LayoutMask) != System.Reflection.TypeAttributes.SequentialLayout ||
			definition.GetLayout().Size > 16)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Guid storage requires the verified sequential sixteen-byte value definition.");
		var fields = definition.GetFields().Select(GetField).Where(f => !f.IsStatic).ToArray();
		if (fields.Length != 11) throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Guid storage requires exactly eleven logical fields.");
		var offsets = new Dictionary<System.Reflection.Metadata.FieldDefinitionHandle, int>();
		for (var index = 0; index < fields.Length; index++)
		{
			var field = fields[index];
			if (Reader.GetString(Reader.GetFieldDefinition(field.Handle).Name) != "_" + (char)('a' + index) ||
				field.Type.DisplayName != (index == 0 ? "int" : index <= 2 ? "short" : "byte"))
				throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Guid storage field names or types do not match the pinned representation.");
			offsets.Add(field.Handle, index == 0 ? 0 : index <= 2 ? index * 2 + 2 : index + 5);
		}
		layout = new CilTypeLayout(handle, GetTypeName(handle), 16, 0, offsets, _assemblyName);
		return true;
	}

	private bool TryGetGuidLayout(CilType type, out CilTypeLayout layout)
	{
		layout = null!;
		if (type is not { Kind: CilTypeKind.ValueType, DisplayName: "System.Guid" } || !type.GenericArguments.IsDefaultOrEmpty ||
			FrameworkImplementationPack is not { IsPinnedStringBuilderInput: true } ||
			GetOrLoadImplementationModule("System.Private.CoreLib") is not { } owner ||
			!TryGetStructLayout(type, "System.Private.CoreLib", out layout) ||
			layout.ModuleName != "System.Private.CoreLib" || layout.Handle.IsNil || layout.Size != 16 || layout.ReferenceBitmap != 0) return false;
		return VerifyGuidFields(owner, layout);
	}

	private bool VerifyGuidRuntimeLayouts()
	{
		return TryGetGuidLayout(new CilType(CilTypeKind.ValueType, 0, "System.Guid"), out _) &&
			GetOrLoadModule("CopperSharp.Runtime.Managed") is { } runtime &&
			TryGetStructLayout(new CilType(CilTypeKind.ValueType, 16, "CopperSharp.Runtime.ShadowGuid"), "CopperSharp.Runtime.Managed", out var shadow) &&
			shadow.ModuleName == runtime.AssemblyName && VerifyGuidFields(runtime, shadow);
	}

	private static bool VerifyGuidFields(CompilationModule owner, CilTypeLayout layout)
	{
		if (layout.Size != 16 || layout.ReferenceBitmap != 0 || layout.Handle.IsNil ||
			!owner.IsValueTypeDefinition(owner.Reader.GetTypeDefinition(layout.Handle))) return false;
		var fields = owner.Reader.GetTypeDefinition(layout.Handle).GetFields().Select(owner.GetField).Where(f => !f.IsStatic).ToArray();
		if (fields.Length != 11 || layout.FieldOffsets.Count != 11) return false;
		for (var index = 0; index < fields.Length; index++)
		{
			var field = fields[index];
			var expectedName = "_" + (char)('a' + index);
			var expectedType = index == 0 ? "int" : index <= 2 ? "short" : "byte";
			var expectedOffset = index == 0 ? 0 : index <= 2 ? index * 2 + 2 : index + 5;
			if (owner.Reader.GetString(owner.Reader.GetFieldDefinition(field.Handle).Name) != expectedName ||
				field.Type.DisplayName != expectedType || layout.FieldOffsets[field.Handle] != expectedOffset) return false;
		}
		return true;
	}

	internal bool IsVerifiedGuidLayout(CilTypeLayout layout) =>
		TryGetGuidLayout(new CilType(CilTypeKind.ValueType, 0, "System.Guid"), out var verified) &&
		layout.Identity == verified.Identity && layout.Size == verified.Size && layout.ReferenceBitmap == 0 &&
		layout.FieldOffsets.Count == verified.FieldOffsets.Count &&
		verified.FieldOffsets.All(f => layout.FieldOffsets.TryGetValue(f.Key, out var offset) && offset == f.Value);

	internal bool TryCreatePinnedGuidBinding(FrameworkMemberId referenced, CilMethod? caller, out FrameworkBinding binding)
	{
		binding = null!;
		if (caller is null || FrameworkImplementationPack is not { IsPinnedStringBuilderInput: true, EnableUnlistedManagedBodies: false } ||
			!(GuidSurface.TryCreatePinnedBinding(referenced, GetModule(caller.ModuleName).DescribeFrameworkMethodDefinition(caller.Handle, []), out var candidate) ||
			  GuidSurface.TryCreateShadowBinding(referenced, out candidate)) ||
			!VerifyGuidRuntimeLayouts()) return false;
		binding = candidate;
		return true;
	}

	internal bool TryCreatePinnedGuidObjectBinding(FrameworkMemberId referenced, CilMethod? caller, int offset, out FrameworkBinding binding)
	{
		binding = null!;
		if (caller is null || FrameworkImplementationPack is not { IsPinnedStringBuilderInput: true, EnableUnlistedManagedBodies: false } ||
			caller.Instructions.FirstOrDefault(i => i.Offset == offset) is not
			{ OpCode: var op, ConstrainedTypeToken: { } token } || op != System.Reflection.Emit.OpCodes.Callvirt) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var result = member.Name == "ToString" ? FrameworkTypeId.Primitive("System.String") : FrameworkTypeId.Primitive("System.Int32");
		if (member.Name is not ("ToString" or "GetHashCode") || !member.Equals(new FrameworkMemberId(
			FrameworkTypeId.Named("System.Runtime", "System.Object"), member.Name, new FrameworkMethodSignatureId(0x20, 0, 0, result, [])))) return false;
		var type = ResolveTypeToken(token, caller, offset);
		if (!TryGetGuidLayout(type, out var layout) || !VerifyGuidRuntimeLayouts()) return false;
		var identity = ResolveRuntimeTypeToken(token, caller, offset);
		if (identity.ModuleName != "System.Private.CoreLib" || identity.Handle != layout.Handle) return false;
		var guidMember = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Guid"), member.Name, member.Signature);
		if (!GuidSurface.TryCreateShadowBinding(guidMember, out var candidate)) return false;
		binding = candidate with { Member = referenced, Reason = "The exact constrained Guid receiver selects its verified value override without boxing." };
		return true;
	}

	private bool TryResolveConstrainedGuidOverride(CilMethod caller, int token, int offset, CilMethod declaration, out CilMethod implementation)
	{
		implementation = null!;
		var shadowDeclaration = declaration.ModuleName == "CopperSharp.Runtime.Managed" && declaration.DisplayName is
			"CopperSharp.Runtime.ShadowObject::GetHashCode" or "CopperSharp.Runtime.ShadowGuid::GetValueHashCode" or "CopperSharp.Runtime.ShadowGuid::FormatDefault";
		if (FrameworkImplementationPack is not { IsPinnedStringBuilderInput: true, EnableUnlistedManagedBodies: false } ||
			!declaration.Signature.Header.IsInstance ||
			declaration.Signature.GenericParameterCount != 0 || declaration.Signature.ParameterTypes.Length != 0 ||
			(!shadowDeclaration && (declaration.ModuleName != "System.Private.CoreLib" ||
			 declaration.DisplayName is not ("System.Object::GetHashCode" or "System.ValueType::GetHashCode" or "System.Guid::GetHashCode" or
				"System.Object::ToString" or "System.ValueType::ToString" or "System.Guid::ToString")))) return false;
		var type = ResolveTypeToken(token, caller, offset);
		if (!TryGetGuidLayout(type, out var layout) || !VerifyGuidRuntimeLayouts()) return false;
		var identity = ResolveRuntimeTypeToken(token, caller, offset);
		if (identity.ModuleName != "System.Private.CoreLib" || identity.Handle != layout.Handle) return false;
		implementation = TryResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowGuid",
			declaration.Name is "GetHashCode" or "GetValueHashCode" ? "GetValueHashCode" : "FormatDefault", declaration.Signature)!;
		return implementation is not null;
	}
}
