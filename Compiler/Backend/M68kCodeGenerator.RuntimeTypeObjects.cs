/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private const string RuntimeObjectTypeMapLabel = "runtime:object-type-map";
	private bool _usesRuntimeObjectGetType;

	private string RegisterRuntimeTypeObject(CilRuntimeTypeTarget target)
	{
		var identity = $"runtime:type-object:{target.ModuleName}:{target.Type.DisplayName}";
		_runtimeTypeObjects.TryAdd(identity, target);
		return identity;
	}

	private Dictionary<string, string> PrepareObjectTypeMap()
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		if (!_usesRuntimeObjectGetType) return map;
		RegisterRuntimeTypeDescriptor("System.RuntimeType");
		void Add(string descriptor, CilType type, string moduleName) =>
			map.Add(descriptor, RegisterRuntimeTypeObject(_module.ResolveRuntimeTypeIdentity(type, moduleName)));

		var constructed = _constructedTypeDescriptors.Values.Select(static item => item.Layout.Identity).ToHashSet();
		foreach (var layout in _usedTypeLayouts.Values.Where(layout => !constructed.Contains(layout.Identity)))
			Add(TypeDescriptorLabel(layout), _module.GetRuntimeTypeSignature(layout), layout.ModuleName);
		foreach (var item in _constructedTypeDescriptors.Values)
		{
			// A verified shadow can expose a public constructed type name. Keep
			// its actual layout identity, as the constructor and ldtoken do;
			// resolving that name again can instead select the CoreLib definition.
			var target = _module.ResolveRuntimeTypeIdentity(item.Type, item.Layout.ModuleName) with {
				ModuleName = item.Layout.ModuleName, Handle = item.Layout.Handle
			};
			map.Add(ConstructedTypeDescriptorLabel(item.Layout, item.Type), RegisterRuntimeTypeObject(target));
		}
		foreach (var name in _runtimeTypeDescriptors)
		{
			if (_usedTypeLayouts.Values.Any(layout => layout.DisplayName == name && IsCoreLibRuntimeDescriptorAlias(layout))) continue;
			Add(RuntimeTypeDescriptorLabel(name),
				new CilType(CilTypeKind.ManagedReference, 4, name == "System.Object" ? "object" : name), "System.Private.CoreLib");
		}
		if (_stringLiterals.Count != 0 || _usesDynamicStrings || _usesRuntimeEmptyString)
			Add("runtime:string-descriptor", new CilType(CilTypeKind.ManagedReference, 4, "string"), "System.Private.CoreLib");
		foreach (var element in _arrayTypes.Values)
			Add(ArrayDescriptorLabel(element), new CilType(CilTypeKind.ManagedReference, 4, $"{element.DisplayName}[]", element), _module.AssemblyName);
		foreach (var type in _boxedTypes.Values)
			Add(BoxedTypeDescriptorLabel(type), type, _module.AssemblyName);
		foreach (var type in _delegateTypes.Values)
			Add(DelegateTypeDescriptorLabel(type), type, _module.AssemblyName);
		return map;
	}

	private void EmitAllocatedObjectGetType(int value, M68kRegister receiver, M68kRegister destination)
	{
		_usesRuntimeObjectGetType = true;
		EmitAllocatedDescriptorLookup(value, receiver, destination, RuntimeObjectTypeMapLabel);
	}

	private void EmitAllocatedDescriptorLookup(int value, M68kRegister receiver, M68kRegister destination, string table)
	{
		EmitAllocatedRequireNonNull(value, receiver);
		EmitAllocatedMove(receiver, M68kRegister.A0, M68kMachineValueWidth.Long);
		EmitAllocatedBaseLoad(M68kRegister.A0, M68kRegister.D0, M68kMachineValueWidth.Long, 0);
		EmitAllocatedAddress(table, M68kRegister.A0);
		var loop = UniqueLabel("object-type-lookup");
		var found = UniqueLabel("object-type-found");
		var missing = UniqueLabel("object-type-missing");
		var done = UniqueLabel("object-type-done");
		_assembler.Mark(loop);
		EmitAllocatedBaseLoad(M68kRegister.A0, M68kRegister.D1, M68kMachineValueWidth.Long, 0);
		EmitAllocatedTest(M68kRegister.D1, M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.Equal, missing);
		EmitAllocatedCompare(M68kRegister.D0, M68kRegister.D1, M68kMachineValueWidth.Long);
		_assembler.EmitBranch(M68kCondition.Equal, found);
		EmitAllocatedAddImmediate(M68kRegister.A0, 8);
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(missing);
		EmitExceptionRaise(reason: 13, hasException: false);
		_assembler.EmitBranch(M68kCondition.True, done);
		_assembler.Mark(found);
		EmitAllocatedBaseLoad(M68kRegister.A0, destination, M68kMachineValueWidth.Long, 4);
		_assembler.Mark(done);
	}
}
