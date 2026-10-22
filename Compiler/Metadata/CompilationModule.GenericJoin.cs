/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Metadata;

internal sealed partial class CompilationModule
{
	internal bool IsPinnedJoinListTypeToken(CilMethod caller, CilType type) =>
		IsPinnedRuntimeJoinFactory(caller) && type is { Kind: CilTypeKind.ManagedReference, Size: 4, ElementType: null, GenericArguments.Length: 1 } &&
		type.DisplayName == $"System.Collections.Generic.List`1<{type.GenericArguments[0].DisplayName}>" &&
		caller.Signature.ParameterTypes[0].GenericArguments is [var element] && AreSameClosedJoinType(type.GenericArguments[0], element) &&
		ResolveRuntimeTypeIdentity(type, "System.Private.CoreLib").ModuleName == "System.Private.CoreLib";

	private bool IsPinnedRuntimeJoinFactory(CilMethod caller)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || caller.ModuleName != "CopperSharp.Runtime.Managed" ||
			caller.Handle.IsNil || caller.Name != "GetEnumerator" || caller.Signature.Header.IsInstance || caller.Signature.ParameterTypes.Length != 1) return false;
		var owner = GetModule(caller.ModuleName);
		if (MetadataTokens.GetRowNumber(caller.Handle) > owner.Reader.MethodDefinitions.Count) return false;
		var actual = owner.GetConstructedMethod(caller.Handle, caller.ConstructedDeclaringType, caller.MethodTypeArguments);
		if (actual.DisplayName != caller.DisplayName || actual.Name != caller.Name || actual.DeclaringType != caller.DeclaringType ||
			!AreSameClosedJoinType(actual.Signature.ReturnType, caller.Signature.ReturnType) ||
			!AreSameClosedJoinType(actual.Signature.ParameterTypes[0], caller.Signature.ParameterTypes[0])) return false;
		return caller.MethodTypeArguments is [var element] && IsPinnedGenericJoinElement(element) && IsExperimentalGenericJoinFactory(caller, element) ||
			caller.MethodTypeArguments.IsDefaultOrEmpty && caller.DisplayName is
				"CopperSharp.Runtime.ShadowStringJoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowObjectJoinEnumeration::GetEnumerator" or
				"CopperSharp.Runtime.ShadowInt32JoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowDecimalJoinEnumeration::GetEnumerator" or
				"CopperSharp.Runtime.ShadowSingleJoinEnumeration::GetEnumerator" or "CopperSharp.Runtime.ShadowDoubleJoinEnumeration::GetEnumerator";
	}

	private bool TryCreatePinnedJoinTypeIdentityBinding(FrameworkMemberId referenced, CilMethod? caller, int ilOffset, out FrameworkBinding binding)
	{
		binding = null!;
		if (caller is null || !IsPinnedRuntimeJoinFactory(caller) ||
			caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset) is not { OpCode: var opcode, Operand: int token } ||
			!(opcode == System.Reflection.Emit.OpCodes.Call || opcode == System.Reflection.Emit.OpCodes.Callvirt) ||
			!FrameworkImplementationProfile.Canonicalize(referenced).Equals(FrameworkImplementationProfile.Canonicalize(DescribeFrameworkMethodToken(token, caller, ilOffset)))) return false;
		if (!FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(referenced, true, out var candidate) ||
			candidate.Target is not ("intrinsic:runtime-object-get-type" or "intrinsic:runtime-type-from-handle" or "intrinsic:object-reference-equals")) return false;
		binding = candidate;
		return true;
	}

	private bool TryGetExperimentalNullableJoinLayout(TypeDefinitionHandle handle, CilType type, out CilTypeLayout layout)
	{
		layout = null!;
		if (_assemblyName != "System.Private.CoreLib" || !IsExperimentalNullableJoinValue(type) ||
			GetTypeName(handle) != "System.Nullable`1") return false;
		var fields = Reader.GetTypeDefinition(handle).GetFields().Select(GetField).Where(field => !field.IsStatic).ToArray();
		if (fields.Length != 2) throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Nullable join layout requires the verified two-field CoreLib definition.");
		var offsets = new Dictionary<FieldDefinitionHandle, int>();
		foreach (var field in fields)
		{
			var name = Reader.GetString(Reader.GetFieldDefinition(field.Handle).Name);
			var fieldType = SubstituteTypeArguments(field.Type, type.GenericArguments);
			if (name == "hasValue" && fieldType is { Kind: CilTypeKind.Boolean, Size: 1 })
				// Existing scalar intrinsics use (value, flag) longwords. Wide payloads
				// retain both value words, with the Boolean in the final low byte.
				offsets.Add(field.Handle, type.Size - 1);
			else if (name == "value" && fieldType == type.NullableElementType)
				offsets.Add(field.Handle, IsExperimentalDecimalNullableElement(fieldType) || IsExperimentalApplicationNullableElement(fieldType) ? 0 : Math.Max(4, fieldType.Size) - fieldType.Size);
			else throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Nullable join fields differ from the verified CoreLib representation.");
		}
		var references = IsExperimentalApplicationNullableElement(type.NullableElementType!) &&
			TryGetStructLayout(type.NullableElementType!, _root.AssemblyName, out var payload) ? payload.ReferenceBitmap : 0u;
		layout = new CilTypeLayout(handle, type.DisplayName, type.Size, references, offsets, _assemblyName, type);
		return true;
	}

	private bool TryResolveExperimentalConstrainedNullableToString(CilMethod caller, int typeToken, int ilOffset,
		CilMethod declaration, out CilMethod implementation)
	{
		implementation = null!;
		if (declaration.ModuleName != "System.Private.CoreLib" || declaration.Name != "ToString" ||
			!declaration.Signature.Header.IsInstance || declaration.Signature.GenericParameterCount != 0 ||
			declaration.Signature.ParameterTypes.Length != 0 || declaration.Signature.ReturnType.DisplayName != "string") return false;
		var type = ResolveTypeToken(typeToken, caller, ilOffset);
		if (!IsExperimentalNullableJoinValue(type)) return false;
		if (declaration.DisplayName != "System.Object::ToString" && !AreSameClosedJoinType(declaration.ConstructedDeclaringType, type)) return false;
		var target = ResolveRuntimeTypeIdentity(type, caller.ModuleName);
		if (target.ModuleName != "System.Private.CoreLib" || target.Handle.IsNil || target.Handle.Kind != HandleKind.TypeDefinition) return false;
		var owner = GetModule(target.ModuleName);
		foreach (var handle in owner.Reader.GetTypeDefinition((TypeDefinitionHandle)target.Handle).GetMethods())
		{
			var definition = owner.Reader.GetMethodDefinition(handle);
			if (!owner.Reader.StringComparer.Equals(definition.Name, "ToString") ||
				(definition.Attributes & MethodAttributes.Virtual) == 0 ||
				(definition.Attributes & MethodAttributes.NewSlot) != 0) continue;
			var method = owner.GetConstructedMethod(handle, type, ImmutableArray<CilType>.Empty);
			if (!method.Signature.Header.IsInstance || method.Signature.GenericParameterCount != 0 ||
				method.Signature.ParameterTypes.Length != 0 || method.Signature.ReturnType.DisplayName != "string") continue;
			implementation = method;
			return true;
		}
		return false;
	}

	private bool TryResolveExperimentalConstrainedEnumToString(CilMethod caller, int typeToken, int ilOffset, CilMethod declaration, out CilMethod implementation)
	{
		implementation = null!;
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !IsPinnedPrimitiveJoinCaller(caller) && !IsPinnedNullableToStringCaller(caller) && !IsPinnedApplicationScalarFormattingCaller(caller)) ||
			!(declaration.ModuleName == "System.Private.CoreLib" && declaration.DisplayName is "System.Object::ToString" or "System.Enum::ToString" ||
			  declaration.ModuleName == "CopperSharp.Runtime.Managed" && declaration.DisplayName == "CopperSharp.Runtime.ShadowBoxedEnum::ToString") ||
			!declaration.Signature.Header.IsInstance || declaration.Signature.GenericParameterCount != 0 ||
			declaration.Signature.ParameterTypes.Length != 0 || declaration.Signature.ReturnType.DisplayName != "string") return false;
		var type = ResolveTypeToken(typeToken, caller, ilOffset);
		if (!type.IsEnum || !IsExperimentalGenericJoinElement(type)) return false;
		if (FrameworkImplementationPack?.EnableUnlistedManagedBodies != true &&
			!type.Equals(IsPinnedNullableToStringCaller(caller) ? caller.ConstructedDeclaringType!.NullableElementType : caller.MethodTypeArguments[0])) return false;
		var target = ResolveRuntimeTypeIdentity(type, caller.ModuleName);
		_ = ReadEnumData(target);
		RegisterBoxedDispatchLayout(type, caller.ModuleName);
		var method = TryResolveManagedMethod("System.Private.CoreLib", "System.Enum", "ToString", declaration.Signature);
		if (method == null) return false;
		implementation = ApplyTargetRuntimeOverride(method);
		return implementation.ModuleName == "CopperSharp.Runtime.Managed" && implementation.DisplayName == "CopperSharp.Runtime.ShadowBoxedEnum::ToString";
	}

	private bool IsExperimentalGenericJoinElement(CilType type) =>
		IsPinnedGenericJoinElement(type) ||
		FrameworkImplementationPack?.EnableUnlistedManagedBodies == true &&
		(type.IsEnum ? type.Kind is CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger && type.Size is 1 or 2 or 4 or 8 :
		 type is { Kind: CilTypeKind.Boolean, Size: 1, DisplayName: "bool" } or
			{ Kind: CilTypeKind.Character, Size: 2, DisplayName: "char" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 1, DisplayName: "sbyte" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 1, DisplayName: "byte" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 2, DisplayName: "short" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 2, DisplayName: "ushort" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 4, DisplayName: "uint" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 8, DisplayName: "long" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 8, DisplayName: "ulong" } || IsExperimentalApplicationJoinValue(type) || IsExperimentalNullableJoinValue(type) || IsExperimentalApplicationJoinReference(type));

	internal bool IsExperimentalNullableJoinValue(CilType type) =>
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true) &&
		type.NullableElementType is { } element && type.Size == GetNullableStorageSize(element) &&
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || !type.IsEnum && type.DisplayName == $"System.Nullable<{element.DisplayName}>") &&
		(IsExperimentalSmallNullableElement(element) || IsExperimentalWideNullableElement(element) || IsExperimentalFloatingNullableElement(element) || IsExperimentalDecimalNullableElement(element) || IsExperimentalApplicationNullableElement(element));

	private bool IsExperimentalApplicationNullableElement(CilType element) =>
		!HasOpenJoinTypeArgument(element) && IsExperimentalApplicationJoinValue(element);

	private static bool HasOpenJoinTypeArgument(CilType type) =>
		type.Kind == CilTypeKind.GenericParameter || type.ElementType is { } item && HasOpenJoinTypeArgument(item) ||
		!type.GenericArguments.IsDefaultOrEmpty && type.GenericArguments.Any(HasOpenJoinTypeArgument);

	private int GetNullableStorageSize(CilType element) =>
		IsExperimentalApplicationNullableElement(element) && TryGetStructLayout(element, _root.AssemblyName, out var layout)
			? checked(layout.Size + 4) : CilType.NullableStorageSize(element);

	public bool IsCompactNullableType(CilType type) =>
		type.NullableElementType is { } element && IsTransparentScalarType(element) && !IsExperimentalNullableJoinValue(type);

	private bool IsExperimentalDecimalNullableElement(CilType element) =>
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || IsPinnedNullableJoinElement(element)) &&
		element is { IsEnum: false, Kind: CilTypeKind.ValueType, Size: 0, DisplayName: "System.Decimal" } &&
		TryGetDecimalLayout(element, out _);

	private bool IsExperimentalFloatingNullableElement(CilType element) =>
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || IsPinnedNullableJoinElement(element)) &&
		element is { IsEnum: false, Kind: CilTypeKind.FloatingPoint, Size: 4, DisplayName: "float" } or
			{ IsEnum: false, Kind: CilTypeKind.FloatingPoint, Size: 8, DisplayName: "double" };

	private bool IsExperimentalWideNullableElement(CilType element) =>
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || IsPinnedNullableJoinElement(element)) && element.Size == 8 &&
		(element.IsEnum ? element.Kind is CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger :
		 element is { Kind: CilTypeKind.SignedInteger, DisplayName: "long" } or
			{ Kind: CilTypeKind.UnsignedInteger, DisplayName: "ulong" });

	private bool IsExperimentalSmallNullableElement(CilType element) =>
		(FrameworkImplementationPack?.EnableUnlistedManagedBodies == true || IsPinnedNullableJoinElement(element)) &&
		(element.IsEnum ? element.Kind is CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger && element.Size is 1 or 2 or 4 :
		 element is { Kind: CilTypeKind.Boolean, Size: 1, DisplayName: "bool" } or
			{ Kind: CilTypeKind.Character, Size: 2, DisplayName: "char" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 1, DisplayName: "sbyte" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 1, DisplayName: "byte" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 2, DisplayName: "short" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 2, DisplayName: "ushort" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 4, DisplayName: "uint" });

	private bool IsExperimentalApplicationJoinValue(CilType type)
	{
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) ||
			type.Kind != CilTypeKind.ValueType || type.IsNullable || HasOpenJoinTypeArgument(type))
			return false;
		var target = ResolveRuntimeTypeIdentity(type, _root.AssemblyName);
		if (Net10FrameworkContract.Default.IsFrameworkAssembly(target.ModuleName) ||
			target.ModuleName is "System.Private.CoreLib" or "CopperSharp.Runtime.Managed" or "CopperSharp.Compiler") return false;
		if (!TryGetStructLayout(type, target.ModuleName, out var layout)) return false;
		return IsExperimentalManagedStructLayout(layout);
	}

	private bool IsExperimentalApplicationJoinReference(CilType type)
	{
		if ((FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) ||
			type is not { Kind: CilTypeKind.ManagedReference, Size: 4, ElementType: null } || HasOpenJoinTypeArgument(type)) return false;
		var target = ResolveRuntimeTypeIdentity(type, _root.AssemblyName);
		return !target.Handle.IsNil && target.Handle.Kind == HandleKind.TypeDefinition &&
			!Net10FrameworkContract.Default.IsFrameworkAssembly(target.ModuleName) &&
			target.ModuleName is not ("System.Private.CoreLib" or "CopperSharp.Runtime.Managed" or "CopperSharp.Compiler") &&
			!GetModule(target.ModuleName).IsValueTypeDefinition(GetModule(target.ModuleName).Reader.GetTypeDefinition((TypeDefinitionHandle)target.Handle));
	}

	private bool IsExperimentalGenericJoinFactory(CilMethod caller, CilType element) =>
		caller.ModuleName == "CopperSharp.Runtime.Managed" &&
		caller.MethodTypeArguments is [var argument] && argument == element && IsExperimentalGenericJoinElement(argument) &&
		caller.DisplayName == $"CopperSharp.Runtime.ShadowGenericJoinEnumeration::GetEnumerator<{argument.DisplayName}>";

	private bool TryCreateExperimentalGenericJoinEnumerationBinding(FrameworkMemberId referencedMember, CilMethod? caller, out FrameworkBinding binding)
	{
		binding = null!;
		if (caller is not { ModuleName: "System.Private.CoreLib", MethodTypeArguments: [var argument] } ||
			(FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !IsPinnedPrimitiveJoinCaller(caller)) ||
			!IsExperimentalGenericJoinElement(argument) || caller.DisplayName != $"System.Text.StringBuilder::AppendJoinCore<{argument.DisplayName}>" ||
			!caller.Signature.Header.IsInstance || caller.Signature.GenericParameterCount != 1 || caller.Signature.RequiredParameterCount != 3 ||
			caller.Signature.ReturnType.DisplayName != "System.Text.StringBuilder" ||
			caller.Signature.ParameterTypes is not [{ Kind: CilTypeKind.ManagedPointer, ElementType.DisplayName: "char" },
				{ Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" },
				{ Kind: CilTypeKind.ManagedReference, GenericArguments: [var sourceArgument] } source] ||
			sourceArgument != argument || source.DisplayName != $"System.Collections.Generic.IEnumerable`1<{argument.DisplayName}>") return false;
		FrameworkTypeId element;
		if (argument.IsEnum || IsExperimentalApplicationJoinValue(argument) || IsExperimentalNullableJoinValue(argument) || IsExperimentalApplicationJoinReference(argument))
		{
			var target = _root.ResolveRuntimeTypeIdentity(argument, _root.AssemblyName);
			if (argument.IsEnum) _ = ReadEnumData(target);
			element = GetApplicationJoinFrameworkType(argument);
		}
		else element = FrameworkTypeId.Primitive(argument.DisplayName switch {
			"bool" => "System.Boolean", "char" => "System.Char", "sbyte" => "System.SByte", "byte" => "System.Byte", "short" => "System.Int16", "ushort" => "System.UInt16",
			"uint" => "System.UInt32", "long" => "System.Int64", _ => "System.UInt64" });
		var member = FrameworkImplementationProfile.Canonicalize(referencedMember);
		if (member.MethodTypeArguments.Length != 0 || member.Signature.Header != 0x20 || member.Signature.GenericParameterCount != 0 ||
			member.Signature.RequiredParameterCount != 0 || member.Signature.ParameterTypes.Length != 0) return false;
		var enumerableDefinition = FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerable`1");
		var enumeratorDefinition = FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerator`1");
		var enumerable = FrameworkTypeId.GenericInstantiation(enumerableDefinition, [element]);
		var enumerator = FrameworkTypeId.GenericInstantiation(enumeratorDefinition, [element]);
		var methodEnumerable = FrameworkTypeId.GenericInstantiation(enumerableDefinition, [FrameworkTypeId.GenericMethodParameter(0)]);
		var methodEnumerator = FrameworkTypeId.GenericInstantiation(enumeratorDefinition, [FrameworkTypeId.GenericMethodParameter(0)]);
		var openEnumerator = FrameworkTypeId.GenericInstantiation(enumeratorDefinition, [FrameworkTypeId.GenericTypeParameter(0)]);
		string? name = null;
		if ((member.DeclaringType.Equals(enumerable) || member.DeclaringType.Equals(methodEnumerable)) && member.Name == "GetEnumerator" &&
			member.Signature.ReturnType.Equals(openEnumerator)) name = "GetEnumerator";
		else if ((member.DeclaringType.Equals(enumerator) || member.DeclaringType.Equals(methodEnumerator)) && member.Name == "get_Current" &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.GenericTypeParameter(0))) name = "get_Current";
		else if (member.DeclaringType.Equals(FrameworkTypeId.Named("System.Runtime", "System.Collections.IEnumerator")) && member.Name == "MoveNext" &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Boolean"))) name = "MoveNext";
		else if (member.DeclaringType.Equals(FrameworkTypeId.Named("System.Runtime", "System.IDisposable")) && member.Name == "Dispose" &&
			member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void"))) name = "Dispose";
		if (name == null) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", name == "GetEnumerator" ?
			"CopperSharp.Runtime.ShadowGenericJoinEnumeration" : "CopperSharp.Runtime.ShadowGenericJoinEnumerator`1", name);
		binding = new FrameworkBinding(referencedMember, FrameworkBindingKind.ShadowMethod,
			$"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{name}",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory, [FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc]),
			Reason: "Exact closed admitted CoreLib join element enumeration over arrays, lists and delegated custom enumerators.", ShadowMethod: shadow);
		return true;
	}

	private bool IsPinnedBooleanCharJoinElement(CilType type) =>
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && type is
			{ IsEnum: false, Kind: CilTypeKind.Boolean, Size: 1, DisplayName: "bool" } or
			{ IsEnum: false, Kind: CilTypeKind.Character, Size: 2, DisplayName: "char" };

	internal static bool AreSameClosedJoinType(CilType? left, CilType? right)
	{
		if (left is null || right is null) return left is null && right is null;
		if (left.Kind != right.Kind || left.Size != right.Size || left.DisplayName != right.DisplayName ||
			left.IsEnum != right.IsEnum || left.IsReadOnly != right.IsReadOnly || !AreSameClosedJoinType(left.ElementType, right.ElementType)) return false;
		if (left.GenericArguments.IsDefaultOrEmpty || right.GenericArguments.IsDefaultOrEmpty)
			return left.GenericArguments.IsDefaultOrEmpty && right.GenericArguments.IsDefaultOrEmpty;
		return left.GenericArguments.Length == right.GenericArguments.Length && left.GenericArguments.Zip(right.GenericArguments)
			.All(pair => AreSameClosedJoinType(pair.First, pair.Second));
	}

	private bool IsPinnedGenericJoinElement(CilType type) =>
		IsPinnedBooleanCharJoinElement(type) || FrameworkImplementationPack?.IsPinnedStringBuilderInput == true &&
		(IsExperimentalNullableJoinValue(type) || IsExperimentalApplicationJoinValue(type) || IsExperimentalApplicationJoinReference(type) ||
		(type.IsEnum ? IsPinnedBoxedEnumType(type, _root.AssemblyName) : type is
			{ Kind: CilTypeKind.SignedInteger, Size: 1, DisplayName: "sbyte" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 1, DisplayName: "byte" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 2, DisplayName: "short" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 2, DisplayName: "ushort" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 4, DisplayName: "uint" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 8, DisplayName: "long" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 8, DisplayName: "ulong" }));

	private bool IsPinnedNullableJoinElement(CilType element) =>
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && !element.IsNullable &&
		(element.IsEnum ? IsPinnedBoxedEnumType(element, _root.AssemblyName) : element is
			{ Kind: CilTypeKind.Boolean, Size: 1, DisplayName: "bool" } or
			{ Kind: CilTypeKind.Character, Size: 2, DisplayName: "char" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 1, DisplayName: "sbyte" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 1, DisplayName: "byte" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 2, DisplayName: "short" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 2, DisplayName: "ushort" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 4, DisplayName: "int" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 4, DisplayName: "uint" } or
			{ Kind: CilTypeKind.SignedInteger, Size: 8, DisplayName: "long" } or
			{ Kind: CilTypeKind.UnsignedInteger, Size: 8, DisplayName: "ulong" } or
			{ Kind: CilTypeKind.FloatingPoint, Size: 4, DisplayName: "float" } or
			{ Kind: CilTypeKind.FloatingPoint, Size: 8, DisplayName: "double" } ||
		 element is { Kind: CilTypeKind.ValueType, Size: 0, DisplayName: "System.Decimal" } && TryGetDecimalLayout(element, out _));

	internal bool IsPinnedGenericJoinInterface(CilType? type) =>
		type is { Kind: CilTypeKind.ManagedReference, Size: 4 } && !type.GenericArguments.IsDefault && type.GenericArguments is [var element] &&
		(type.DisplayName == $"System.Collections.Generic.IEnumerable`1<{element.DisplayName}>" ||
		 type.DisplayName == $"System.Collections.Generic.IEnumerator`1<{element.DisplayName}>") && IsPinnedGenericJoinElement(element);

	private bool IsPinnedNullableToStringCaller(CilMethod caller) =>
		FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && caller.ModuleName == "System.Private.CoreLib" &&
		caller.ConstructedDeclaringType is { } type && IsExperimentalNullableJoinValue(type) &&
		caller.Name == "ToString" && caller.DisplayName == $"{type.DisplayName}::ToString" &&
		caller.Signature.Header.IsInstance && caller.Signature.GenericParameterCount == 0 &&
		caller.Signature.ParameterTypes.Length == 0 && caller.Signature.ReturnType.DisplayName == "string" &&
		GetModule(caller.ModuleName).GetTypeName(caller.DeclaringType) == "System.Nullable`1";

	private bool IsPinnedApplicationScalarFormattingCaller(CilMethod caller)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || caller.MethodTypeArguments.IsDefault ||
			caller.MethodTypeArguments is not [var element] || !IsPinnedNullableJoinElement(element) ||
			Net10FrameworkContract.Default.IsFrameworkAssembly(caller.ModuleName) || caller.ModuleName is "System.Private.CoreLib" or "CopperSharp.Runtime.Managed" or "CopperSharp.Compiler" ||
			!_root._modules.TryGetValue(caller.ModuleName, out var owner) || caller.Handle.IsNil ||
			System.Reflection.Metadata.Ecma335.MetadataTokens.GetRowNumber(caller.Handle) > owner.Reader.GetTableRowCount(System.Reflection.Metadata.Ecma335.TableIndex.MethodDef)) return false;
		var definition = owner.Reader.GetMethodDefinition(caller.Handle);
		return definition.GetDeclaringType() == caller.DeclaringType && owner.Reader.StringComparer.Equals(definition.Name, caller.Name) &&
			caller.ConstructedDeclaringType is null && caller.DisplayName == $"{owner.GetTypeName(caller.DeclaringType)}::{caller.Name}<{element.DisplayName}>";
	}

	private bool IsPinnedApplicationValueFormattingCaller(CilMethod caller)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || !caller.MethodTypeArguments.IsDefaultOrEmpty ||
			caller.ConstructedDeclaringType is not null || Net10FrameworkContract.Default.IsFrameworkAssembly(caller.ModuleName) ||
			caller.ModuleName is "System.Private.CoreLib" or "CopperSharp.Runtime.Managed" or "CopperSharp.Compiler" ||
			!_root._modules.TryGetValue(caller.ModuleName, out var owner) || caller.Handle.IsNil ||
			MetadataTokens.GetRowNumber(caller.Handle) > owner.Reader.GetTableRowCount(TableIndex.MethodDef)) return false;
		var definition = owner.Reader.GetMethodDefinition(caller.Handle);
		return definition.GetGenericParameters().Count == 0 && definition.GetDeclaringType() == caller.DeclaringType &&
			owner.Reader.StringComparer.Equals(definition.Name, caller.Name) &&
			caller.DisplayName == $"{owner.GetTypeName(caller.DeclaringType)}::{caller.Name}";
	}

	internal bool TryCreatePinnedNullableToStringBinding(FrameworkMemberId referenced, CilMethod? caller, int ilOffset,
		out FrameworkBinding binding, out CilMethod implementation)
	{
		binding = null!; implementation = null!;
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || caller is null) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var signature = new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []);
		if (!member.Equals(new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Object"), "ToString", signature)) ||
			caller.Instructions.FirstOrDefault(instruction => instruction.Offset == ilOffset) is not
			{ OpCode: var opcode, ConstrainedTypeToken: { } token } || opcode != System.Reflection.Emit.OpCodes.Callvirt) return false;
		var declaration = GetModule("System.Private.CoreLib").TryResolveManagedMethod("System.Private.CoreLib", "System.Object", "ToString",
			new MethodSignature<CilType>(new SignatureHeader(0x20), new CilType(CilTypeKind.ManagedReference, 4, "string"), 0, 0, []));
		if (declaration is null || !TryResolveExperimentalConstrainedNullableToString(caller, token, ilOffset, declaration, out implementation)) return false;
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ManagedBody, "managed:constrained-nullable-tostring",
			new FrameworkEffectSummary(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
				[FrameworkFeature.NullableValues, FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]),
			Reason: "A verified closed nullable value selects its exact released public ToString override.");
		return true;
	}

	private bool TryCreatePinnedNullableListBinding(FrameworkMemberId referenced, CilType? construction, out FrameworkBinding binding, CilMethod? caller = null)
	{
		binding = null!;
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput == true && construction is { GenericArguments: [var decimalElement] } &&
			construction.DisplayName == "System.Collections.Generic.List`1<System.Decimal>" && decimalElement.DisplayName == "System.Decimal" &&
			TryGetDecimalLayout(decimalElement, out _) && caller?.ConstructedDeclaringType is { GenericArguments: [var argument] } &&
			AreSameClosedJoinType(argument, decimalElement) && IsActualClosedApplicationCaller(caller))
		{
			var decimalMember = FrameworkImplementationProfile.Canonicalize(referenced);
			var closedOwner = FrameworkTypeId.GenericInstantiation(decimalMember.DeclaringType.Kind == FrameworkTypeKind.GenericInstantiation
				? decimalMember.DeclaringType.ElementType! : FrameworkTypeId.Named("System.Collections", "System.Collections.Generic.List`1"), [StringBuilderDecimalListSurface.Decimal]);
			var closed = new FrameworkMemberId(closedOwner, decimalMember.Name, decimalMember.Signature, decimalMember.MethodTypeArguments);
			if (decimalMember.Name is ".ctor" or "Add" && decimalMember.DeclaringType.Kind == FrameworkTypeKind.GenericInstantiation &&
				decimalMember.DeclaringType.ElementType!.FullMetadataName == "System.Collections.Generic.List`1" &&
				decimalMember.DeclaringType.ElementType.AssemblyName is "System.Collections" or "System.Runtime" or "System.Private.CoreLib" &&
				decimalMember.DeclaringType.GenericArguments.SequenceEqual([FrameworkTypeId.GenericTypeParameter(0)]) &&
				StringBuilderDecimalListSurface.IsPublic(closed) &&
				FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(closed, null, FrameworkImplementationPack, null, out binding))
			{
				binding = binding with { Member = referenced };
				return true;
			}
		}
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || construction is not { GenericArguments.IsDefault: false } ||
			construction.GenericArguments is not [var element] ||
			!(IsExperimentalNullableJoinValue(element) || IsExperimentalApplicationJoinValue(element) || IsExperimentalApplicationJoinReference(element))) return false;
		var list = FrameworkTypeId.Named("System.Collections", "System.Collections.Generic.List`1");
		var enumerator = FrameworkTypeId.Named("System.Collections", "Enumerator", list);
		var isEnumerator = construction.DisplayName == $"System.Collections.Generic.List`1/Enumerator<{element.DisplayName}>";
		if (!isEnumerator && construction.DisplayName != $"System.Collections.Generic.List`1<{element.DisplayName}>") return false;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var expectedOwner = FrameworkTypeId.GenericInstantiation(isEnumerator ? enumerator : list, [GetApplicationJoinFrameworkType(element)]);
		if (!member.DeclaringType.Equals(expectedOwner)) return false;
		var signature = member.Name switch {
			".ctor" => new FrameworkMethodSignatureId(0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32")]),
			"Add" => new FrameworkMethodSignatureId(0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.GenericTypeParameter(0)]),
			"set_Item" => new FrameworkMethodSignatureId(0x20, 0, 2, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.GenericTypeParameter(0)]),
			"get_Item" => new FrameworkMethodSignatureId(0x20, 0, 1, FrameworkTypeId.GenericTypeParameter(0), [FrameworkTypeId.Primitive("System.Int32")]),
			"GetEnumerator" => new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.GenericInstantiation(enumerator, [FrameworkTypeId.GenericTypeParameter(0)]), []),
			"MoveNext" => new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.Boolean"), []),
			"get_Current" => new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.GenericTypeParameter(0), []),
			"Dispose" => new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.Void"), []),
			_ => null };
		if (signature is null || !member.Signature.Equals(signature) ||
			(isEnumerator ? member.Name is not ("MoveNext" or "get_Current" or "Dispose") : member.Name is "MoveNext" or "get_Current" or "Dispose")) return false;
		var shadow = new FrameworkShadowMethod("CopperSharp.Runtime.Managed", isEnumerator ? "CopperSharp.Runtime.ShadowListEnumerator`1" : "CopperSharp.Runtime.ShadowList`1", member.Name);
		var effects = member.Name switch {
			"Dispose" => FrameworkEffects.None,
			"GetEnumerator" or "get_Current" => FrameworkEffects.ReadsManagedMemory,
			"get_Item" => FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory,
			"set_Item" or "MoveNext" => FrameworkEffects.MayThrow | FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
			_ => FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.WritesManagedMemory |
				(member.Name == "Add" ? FrameworkEffects.ReadsManagedMemory : FrameworkEffects.None) };
		binding = new FrameworkBinding(referenced, FrameworkBindingKind.ShadowMethod, $"shadow:{shadow.AssemblyName}:{shadow.TypeName}::{shadow.MethodName}",
			new FrameworkEffectSummary(effects,
				[FrameworkFeature.ManagedCollections, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc]), ShadowMethod: shadow);
		return true;
	}

	private bool IsActualClosedApplicationCaller(CilMethod caller)
	{
		if (caller.ModuleName is "System.Private.CoreLib" or "CopperSharp.Runtime.Managed" or "CopperSharp.Compiler" ||
			Net10FrameworkContract.Default.IsFrameworkAssembly(caller.ModuleName) || caller.Handle.IsNil ||
			!_root._modules.TryGetValue(caller.ModuleName, out var owner) || MetadataTokens.GetRowNumber(caller.Handle) > owner.Reader.MethodDefinitions.Count ||
			caller.ConstructedDeclaringType is not { } construction) return false;
		var definition = owner.Reader.GetMethodDefinition(caller.Handle);
		if (definition.GetDeclaringType() != caller.DeclaringType ||
			!owner.TypeNameMatches(owner.Reader.GetTypeDefinition(caller.DeclaringType), construction.DisplayName.Split('<')[0]) ||
			owner.Reader.GetTypeDefinition(caller.DeclaringType).GetGenericParameters().Count != construction.GenericArguments.Length) return false;
		var actual = owner.GetConstructedMethod(caller.Handle, construction, caller.MethodTypeArguments);
		return actual.Name == caller.Name && actual.DisplayName == caller.DisplayName &&
			actual.Signature.Header.RawValue == caller.Signature.Header.RawValue &&
			actual.Signature.ParameterTypes.Length == caller.Signature.ParameterTypes.Length &&
			AreSameClosedJoinType(actual.Signature.ReturnType, caller.Signature.ReturnType) &&
			actual.Signature.ParameterTypes.Zip(caller.Signature.ParameterTypes).All(pair => AreSameClosedJoinType(pair.First, pair.Second));
	}

	private bool TryCreatePinnedNullableDependencyBinding(FrameworkMemberId referenced, CilMethod? caller, FrameworkBinding? fallback, out FrameworkBinding binding)
	{
		binding = null!;
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var noValue = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.ThrowHelper"),
			"ThrowInvalidOperationException_InvalidOperation_NoValue", new FrameworkMethodSignatureId(0, 0, 0, FrameworkTypeId.Primitive("System.Void"), []));
		if (member.Equals(noValue) && caller is { ModuleName: "System.Private.CoreLib", Name: "get_Value", ConstructedDeclaringType: { } nullable } &&
			IsExperimentalNullableJoinValue(nullable) && GetModule(caller.ModuleName).GetTypeName(caller.DeclaringType) == "System.Nullable`1")
			return FrameworkImplementationProfile.TryCreatePinnedBinding(referenced, fallback, true, out binding);
		if (member.Name == "BitCast" && member.DeclaringType.Equals(FrameworkTypeId.Named("System.Runtime", "System.Runtime.CompilerServices.Unsafe")) &&
			FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(referenced, true, out var bits) &&
			(bits.Target == "intrinsic:runtime-bitcast-32" || bits.ShadowMethod is { TypeName: "CopperSharp.Runtime.ShadowFloatingPointBits", MethodName:
				"DoubleToUInt64" or "DoubleToInt64" or "UInt64ToDouble" or "Int64ToDouble" }))
		{
			binding = bits;
			return true;
		}
		return false;
	}

	internal bool TryCreatePinnedNullableMemberBinding(FrameworkMemberId referenced, CilType? construction,
		CilMethod? caller, FrameworkBinding? fallback, out FrameworkBinding binding)
	{
		binding = null!;
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true || construction is null || !IsExperimentalNullableJoinValue(construction)) return false;
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var definition = FrameworkTypeId.Named("System.Runtime", "System.Nullable`1");
		var parameter = FrameworkTypeId.GenericTypeParameter(0);
		if (member.DeclaringType.Kind != FrameworkTypeKind.GenericInstantiation || !member.DeclaringType.ElementType!.Equals(definition) ||
			member.DeclaringType.GenericArguments.Length != 1 || member.MethodTypeArguments.Length != 0) return false;
		var payload = construction.NullableElementType!;
		var closed = FrameworkImplementationProfile.Canonicalize(new FrameworkMemberId(
			FrameworkTypeId.GenericInstantiation(definition, [GetApplicationJoinFrameworkType(payload)]), member.Name, member.Signature)).DeclaringType;
		var argument = member.DeclaringType.GenericArguments[0];
		if (!member.DeclaringType.Equals(closed) &&
			!(argument.Equals(FrameworkTypeId.GenericMethodParameter(0)) && caller is { MethodTypeArguments.IsDefault: false } &&
				caller.MethodTypeArguments is [var methodPayload] && methodPayload == payload) &&
			!(argument.Equals(parameter) && caller?.ConstructedDeclaringType is { GenericArguments.IsDefault: false } owner &&
				owner.GenericArguments is [var typePayload] && typePayload == payload)) return false;
		var signature = member.Name switch {
			".ctor" => new FrameworkMethodSignatureId(0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [parameter]),
			"get_HasValue" => new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.Boolean"), []),
			"get_Value" => new FrameworkMethodSignatureId(0x20, 0, 0, parameter, []),
			"GetValueOrDefault" when member.Signature.ParameterTypes.Length == 0 => new FrameworkMethodSignatureId(0x20, 0, 0, parameter, []),
			"GetValueOrDefault" => new FrameworkMethodSignatureId(0x20, 0, 1, parameter, [parameter]),
			"ToString" => new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []),
			_ => null };
		if (signature is null || !member.Signature.Equals(signature) ||
			!FrameworkImplementationProfile.TryCreatePinnedBinding(referenced, fallback, true, out binding)) return false;
		binding = binding with { EffectSummary = new FrameworkEffectSummary(
			FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
			FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
			[FrameworkFeature.NullableValues, FrameworkFeature.ManagedStrings, FrameworkFeature.ManagedGc]) };
		return true;
	}

	internal bool TryCreatePinnedGenericJoinInterfaceBinding(FrameworkMemberId referenced, CilType? construction,
		CilMethod? caller, FrameworkBinding? fallback, out FrameworkBinding binding)
	{
		binding = null!;
		if (!IsPinnedGenericJoinInterface(construction) && !IsPinnedDerivedListJoinInterface(construction, caller)) return false;
		var element = construction!.GenericArguments[0];
		var member = FrameworkImplementationProfile.Canonicalize(referenced);
		var enumerable = FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerable`1");
		var enumerator = FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerator`1");
		var closedElement = GetApplicationJoinFrameworkType(element);
		var openElement = FrameworkTypeId.GenericTypeParameter(0);
		var factory = construction.DisplayName.StartsWith("System.Collections.Generic.IEnumerable`1<", StringComparison.Ordinal);
		var declaring = FrameworkTypeId.GenericInstantiation(factory ? enumerable : enumerator, [closedElement]);
		// Open metadata identities must agree with the caller's actual closed
		// arguments. Application generic code uses the same public slots.
		if (!member.DeclaringType.Equals(declaring))
		{
			var methodParameter = FrameworkTypeId.GenericMethodParameter(0);
			var methodMatches = caller is { MethodTypeArguments.IsDefault: false } && caller.MethodTypeArguments is [var methodArgument] && methodArgument == element &&
				member.DeclaringType.Equals(FrameworkTypeId.GenericInstantiation(factory ? enumerable : enumerator, [methodParameter]));
			var typeMatches = caller?.ConstructedDeclaringType is { GenericArguments.IsDefault: false } owner && owner.GenericArguments is [var payload] && payload == element &&
				member.DeclaringType.Equals(FrameworkTypeId.GenericInstantiation(factory ? enumerable : enumerator, [openElement]));
			if (!methodMatches && !typeMatches) return false;
		}
		var expected = new FrameworkMethodSignatureId(0x20, 0, 0,
			factory ? FrameworkTypeId.GenericInstantiation(enumerator, [openElement]) : openElement, []);
		if (member.Name != (factory ? "GetEnumerator" : "get_Current") || !member.Signature.Equals(expected) ||
			!FrameworkImplementationProfile.TryCreatePinnedBinding(referenced, fallback, true, out binding)) return false;
		binding = binding with { EffectSummary = new FrameworkEffectSummary(
			FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
			FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory,
			[FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedGc]) };
		return true;
	}

	private bool IsPinnedDerivedListJoinInterface(CilType? construction, CilMethod? caller)
	{
		if (FrameworkImplementationPack?.IsPinnedStringBuilderInput != true ||
			construction is not { Kind: CilTypeKind.ManagedReference, Size: 4, GenericArguments: [var element] } ||
			caller?.ConstructedDeclaringType is not { GenericArguments: [var payload] } ||
			!AreSameClosedJoinType(element, payload) || !IsActualClosedApplicationCaller(caller) ||
			(construction.DisplayName != $"System.Collections.Generic.IEnumerable`1<{element.DisplayName}>" &&
			 construction.DisplayName != $"System.Collections.Generic.IEnumerator`1<{element.DisplayName}>")) return false;
		if (!IsPinnedNullableJoinElement(element) && element is not
			{ Kind: CilTypeKind.ManagedReference, Size: 4, DisplayName: "string" or "object" }) return false;
		return GetExperimentalListBase(GetTypeLayout(caller))?.ConstructedType is { GenericArguments: [var baseElement] } &&
			AreSameClosedJoinType(element, baseElement);
	}

	private FrameworkTypeId GetApplicationJoinFrameworkType(CilType type)
	{
		if (FrameworkPrimitiveArgument(type) is { } primitive) return primitive;
		if (type is { Kind: CilTypeKind.ValueType, Size: 0, DisplayName: "System.Decimal" } && TryGetDecimalLayout(type, out _))
			return FrameworkTypeId.Named("System.Runtime", "System.Decimal");
		if (type.ElementType is { } item && type.DisplayName.EndsWith("[]", StringComparison.Ordinal))
			return FrameworkTypeId.SzArray(GetApplicationJoinFrameworkType(item));
		var target = ResolveRuntimeTypeIdentity(type, _root.AssemblyName);
		var owner = GetModule(target.ModuleName);
		var definition = FrameworkImplementationProfile.CanonicalizeType(new FrameworkSignatureTypeProvider(owner).GetTypeFromDefinition(owner.Reader, (TypeDefinitionHandle)target.Handle, 0));
		return type.GenericArguments.IsDefaultOrEmpty ? definition :
			FrameworkTypeId.GenericInstantiation(definition, type.GenericArguments.Select(GetApplicationJoinFrameworkType).ToArray());
	}
}
