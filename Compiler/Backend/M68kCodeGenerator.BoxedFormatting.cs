/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private readonly HashSet<string> _boxedFormattingVirtualTables = new(StringComparer.Ordinal);

	private bool CanAdaptBoxedCharacterSpanFormatter(CilInterfaceImplementation implementation, CilMethod method)
	{
		var pinnedDecimal = _module.FrameworkImplementationPack?.IsPinnedStringBuilderInput == true &&
			_module.TryGetReferenceFreeStructLayout(_module.GetMethodDeclaringType(method), method.ModuleName, out var layout) &&
			_module.IsExperimentalDecimalFormattingLayout(layout);
		if ((_module.FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !pinnedDecimal) ||
			implementation.Interface.Identity.ModuleName != "System.Private.CoreLib" || implementation.Interface.DisplayName != "System.ISpanFormattable" ||
			method.Signature.GenericParameterCount != 0 || method.Signature.ReturnType is not { Kind: CilTypeKind.Boolean, Size: 1 } ||
			method.Signature.ParameterTypes is not [var destination, { Kind: CilTypeKind.ManagedPointer, ElementType: { Kind: CilTypeKind.SignedInteger, Size: 4 } },
				var format, { Kind: CilTypeKind.ManagedReference, DisplayName: "System.IFormatProvider" }]) return false;
		return IsCharacterSpan(destination, "System.Span`1<char>") && IsCharacterSpan(format, "System.ReadOnlySpan`1<char>");

		bool IsCharacterSpan(CilType type, string name) => type.Kind == CilTypeKind.ValueType && type.DisplayName == name &&
			type.GenericArguments is [{ Kind: CilTypeKind.Character, Size: 2 }] &&
			_module.ResolveRuntimeTypeIdentity(type, method.ModuleName).ModuleName == "System.Private.CoreLib" &&
			_module.TryGetReferenceFreeStructLayout(type, "System.Private.CoreLib", out var layout) &&
			layout.ModuleName == "System.Private.CoreLib" && layout.Size == 12 && layout.ReferenceBitmap == 4;
	}

	private void EmitBoxedFormattingVirtuals(IReadOnlyList<CilMethod> methods)
	{
		if (_module.FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && _module.FrameworkImplementationPack?.IsPinnedStringBuilderInput != true) return;
		var compiled = methods.Select(method => method.Identity).ToHashSet();
		foreach (var (name, layout) in _boxedStructLayouts.OrderBy(item => item.Key, StringComparer.Ordinal))
		{
			if (_module.FrameworkImplementationPack?.EnableUnlistedManagedBodies != true && !_module.IsExperimentalDecimalFormattingLayout(layout)) continue;
			var type = _boxedTypes[name];
			if (type.Kind != CilTypeKind.ValueType || type.IsEnum || !_allocatedBoxedTypes.Contains(name) ||
				Net10FrameworkContract.Default.IsFrameworkAssembly(layout.ModuleName) && !_module.IsExperimentalDecimalFormattingLayout(layout)) continue;
			var slots = _module.GetVirtualTable(layout).Slots;
			var target = slots.FirstOrDefault(method => (method.ModuleName == layout.ModuleName ||
				method.ModuleName == "System.Private.CoreLib" && method.DisplayName == "System.ValueType::ToString") && method.Name == "ToString" &&
				!method.IsAbstract && !method.IsNewSlot && method.Signature.Header.IsInstance &&
				method.Signature.ParameterTypes.Length == 0 && method.Signature.ReturnType.DisplayName == "string" && compiled.Contains(method.Identity));
			if (target is null) continue;
			_boxedFormattingVirtualTables.Add(name);
			_assembler.AlignWord();
			_assembler.Mark(BoxedTypeDescriptorLabel(type) + ":formatting-to-string");
			if (target.ModuleName == layout.ModuleName)
			{
				_assembler.EmitWord(0x41E8); // LEA 8(A0),A0: boxed receiver to value payload.
				_assembler.EmitWord(8);
				if (_module.IsTransparentScalarType(type))
					EmitMoveRegister(M68kRegister.A0, M68kRegister.D0);
			}
			_assembler.EmitJmp(MethodLabel(target), external: false);
			_assembler.AlignWord();
			_assembler.Mark(BoxedTypeDescriptorLabel(type) + ":formatting-virtuals");
			foreach (var slot in slots)
			{
				if (slot.Identity == target.Identity) _assembler.EmitAddress(BoxedTypeDescriptorLabel(type) + ":formatting-to-string");
				else _assembler.EmitLong(0);
			}
		}
	}
}
