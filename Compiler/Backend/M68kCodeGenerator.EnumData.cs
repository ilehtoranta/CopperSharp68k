/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private readonly Dictionary<string, CilEnumData> _enumData = new(StringComparer.Ordinal);
	private CilTypeLayout? _enumMetadataLayout;
	private bool _usesBoxedEnumData;
	private const string BoxedEnumDataMapLabel = "runtime:boxed-enum-data-map";

	private string RegisterEnumData(CilType type, string preferredModule)
	{
		var data = CilEnumData.Read(_module, type, preferredModule);
		_enumData.TryAdd(data.Label, data);
		_enumMetadataLayout ??= _module.GetEnumMetadataLayout();
		_usedTypeLayouts.TryAdd(_enumMetadataLayout.Identity, _enumMetadataLayout);
		var values = new CilType(CilTypeKind.UnsignedInteger, 8, "ulong");
		var names = new CilType(CilTypeKind.ManagedReference, 4, "string");
		_arrayTypes.TryAdd(values.DisplayName, values); _arrayTypes.TryAdd(names.DisplayName, names);
		_arrayElementRuntimeTypes.TryAdd(names.DisplayName, _module.ResolveRuntimeTypeIdentity(names, "System.Private.CoreLib"));
		_usesDynamicStrings = true;
		return data.Label;
	}

	private void EmitEnumData()
	{
		foreach (var data in _enumData.Values.OrderBy(data => data.Label, StringComparer.Ordinal))
		{
			_assembler.AlignWord(); _assembler.Mark(data.Label);
			_assembler.EmitAddress(TypeDescriptorLabel(_enumMetadataLayout!)); _assembler.EmitLong(28);
			_assembler.EmitLong((uint)data.Type.Size); _assembler.EmitLong(data.Type.Kind == CilTypeKind.SignedInteger ? 1u : 0u);
			_assembler.EmitLong(data.Flags ? 1u : 0u);
			_assembler.EmitAddress(data.Label + ":values"); _assembler.EmitAddress(data.Label + ":names");
			_assembler.Mark(data.Label + ":values");
			_assembler.EmitAddress(ArrayDescriptorLabel(new CilType(CilTypeKind.UnsignedInteger, 8, "ulong")));
			_assembler.EmitLong(checked((uint)(12 + data.Values.Length * 8))); _assembler.EmitLong((uint)data.Values.Length);
			foreach (var value in data.Values) { _assembler.EmitLong((uint)(value >> 32)); _assembler.EmitLong((uint)value); }
			_assembler.Mark(data.Label + ":names");
			_assembler.EmitAddress(ArrayDescriptorLabel(new CilType(CilTypeKind.ManagedReference, 4, "string")));
			_assembler.EmitLong(checked((uint)(12 + data.Names.Length * 4))); _assembler.EmitLong((uint)data.Names.Length);
			for (var i = 0; i < data.Names.Length; i++) _assembler.EmitAddress(data.Label + $":name:{i}");
			for (var i = 0; i < data.Names.Length; i++)
			{
				var name = data.Names[i]; _assembler.AlignWord(); _assembler.Mark(data.Label + $":name:{i}");
				_assembler.EmitAddress("runtime:string-descriptor"); _assembler.EmitLong(checked((uint)(14 + name.Length * 2)));
				_assembler.EmitLong((uint)name.Length);
				foreach (var character in name) _assembler.EmitWord(character);
				_assembler.EmitWord(0);
			}
		}
	}

	private Dictionary<string, string> PrepareBoxedEnumData()
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		if (!_usesBoxedEnumData) return map;
		foreach (var type in _boxedTypes.Values.Where(type => type.IsEnum))
		{
			_boxedStructLayouts.TryAdd(type.DisplayName, _module.GetRuntimeTypeLayout(_module.ResolveRuntimeTypeIdentity(type, _module.AssemblyName)));
			map.Add(BoxedTypeDescriptorLabel(type), RegisterEnumData(type, _module.AssemblyName));
		}
		return map;
	}

	private void EmitBoxedEnumData(Dictionary<string, string> map, IReadOnlyList<CilMethod> methods)
	{
		if (!_usesBoxedEnumData) return;
		_assembler.Mark(BoxedEnumDataMapLabel);
		foreach (var item in map.OrderBy(item => item.Key, StringComparer.Ordinal))
		{
			_assembler.EmitAddress(item.Key); _assembler.EmitAddress(item.Value);
		}
		_assembler.EmitLong(0);
		var compiled = methods.Select(method => method.Identity).ToHashSet();
		foreach (var type in _boxedTypes.Values.Where(type => type.IsEnum).OrderBy(type => type.DisplayName, StringComparer.Ordinal))
		{
			_assembler.Mark(BoxedTypeDescriptorLabel(type) + ":virtuals");
			foreach (var slot in _module.GetVirtualTable(_boxedStructLayouts[type.DisplayName]).Slots)
			{
				// Table emission only needs identities; unused CoreLib slots must
				// not materialize bodies outside the admitted reachable graph.
				var implementation = _module.ApplyTargetRuntimeOverride(slot, materializeBody: false);
				if (compiled.Contains(implementation.Identity)) _assembler.EmitAddress(MethodLabel(implementation));
				else _assembler.EmitLong(0);
			}
		}
	}
}
