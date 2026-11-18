/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private sealed record InitializedData(string Label, byte[] Bytes)
	{
		public bool RequiresDistinctAddress { get; set; }
	}

	private readonly Dictionary<string, InitializedData> _initializedDataBySource = new(StringComparer.Ordinal);
	private readonly HashSet<CilMethodIdentity> _privateInitializedSpanHelpers = [];
	private static readonly IEqualityComparer<byte[]> InitializedPayloadComparer = EqualityComparer<byte[]>.Create(
		static (left, right) => left.AsSpan().SequenceEqual(right),
		static bytes =>
		{
			var hash = new HashCode();
			hash.AddBytes(bytes);
			return hash.ToHashCode();
		});

	private (InitializedData Data, int Length) RegisterInitializedSpanData(CilMethod caller, CilInstruction fieldToken, CilInstruction call)
	{
		var target = _module.ResolveMethodToken((int)call.Operand!, caller, call.Offset);
		if (target.ImportName != "intrinsic:initialized-data-span" || fieldToken.OpCode != OpCodes.Ldtoken ||
			fieldToken.NextOffset != call.Offset || target.Signature.ReturnType.GenericArguments is not [var element])
			throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedInstruction,
				"Initialized spans require an immediately preceding field token and the exact RuntimeHelpers.CreateSpan call.", caller.DisplayName, call.Offset);
		var size = element.Size;
		if (size is not (1 or 2 or 4 or 8) || element.Kind is not (CilTypeKind.Character or CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger or CilTypeKind.FloatingPoint))
			throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, "Initialized spans require fixed-size primitive elements.", caller.DisplayName, call.Offset);
		var token = (int)fieldToken.Operand!;
		var key = $"{caller.ModuleName}:{token:X8}:{size}";
		if (!_initializedDataBySource.TryGetValue(key, out var data))
		{
			var source = _module.ReadInitializedFieldRva(token, caller, fieldToken.Offset);
			if (source.Length % size != 0)
				throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Initialized field size is not a multiple of the span element size.", caller.DisplayName, call.Offset);
			var bytes = new byte[source.Length];
			for (var offset = 0; offset < bytes.Length; offset += size)
			for (var lane = 0; lane < size; lane++) bytes[offset + lane] = source[offset + size - 1 - lane];
			data = new InitializedData($"runtime:initialized-span:{_initializedDataBySource.Count:X4}", bytes);
			_initializedDataBySource.Add(key, data);
		}
		data.RequiresDistinctAddress = data.RequiresDistinctAddress ||
			!HasPrivateInitializedSpanUses(caller, fieldToken, call, element);
		return (data, data.Bytes.Length / size);
	}

	private bool TryEmitInitializedFieldAddress(CilMethod caller, CilInstruction source, M68kRegister destination)
	{
		if (source.OpCode != OpCodes.Ldtoken || MetadataTokens.EntityHandle((int)source.Operand!).Kind != HandleKind.FieldDefinition)
			return false;
		var next = caller.Instructions.FirstOrDefault(instruction => instruction.Offset == source.NextOffset);
		if (next is null || next.OpCode != OpCodes.Call)
			throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedInstruction, "Initialized field handles may only feed an immediate span factory.", caller.DisplayName, source.Offset);
		var target = _module.ResolveMethodToken((int)next.Operand!, caller, next.Offset);
		var data = target.ImportName == "intrinsic:initialize-array"
			? RegisterInitializedArrayData(caller, next) : RegisterInitializedSpanData(caller, source, next).Data;
		EmitAllocatedAddress(data.Label, destination);
		return true;
	}

	private InitializedData RegisterInitializedArrayData(CilMethod caller, CilInstruction call)
	{
		var index = caller.Instructions.ToList().FindIndex(instruction => instruction.Offset == call.Offset);
		if (index < 4 || caller.Instructions[index - 1].OpCode != OpCodes.Ldtoken ||
			caller.Instructions[index - 2].OpCode != OpCodes.Dup || caller.Instructions[index - 3].OpCode != OpCodes.Newarr ||
			!TryGetConstant(caller.Instructions[index - 4], out var count) || count < 0)
			throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedInstruction,
				"Initialized arrays require a constant-length newarr, dup, field token and InitializeArray sequence.", caller.DisplayName, call.Offset);
		var suffixOffsets = caller.Instructions.Skip(index - 3).Take(4).Select(instruction => instruction.Offset).ToHashSet();
		if (caller.Instructions.Any(instruction => instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch &&
			(instruction.Operand is int target && suffixOffsets.Contains(target) || instruction.Operand is int[] targets && targets.Any(suffixOffsets.Contains))) ||
			caller.ExceptionRegions.Any(region => suffixOffsets.Contains(region.HandlerOffset) || suffixOffsets.Contains(region.FilterOffset)))
			throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedInstruction, "Initialized array sequences cannot have an interior control-flow entry.", caller.DisplayName, call.Offset);
		var allocation = caller.Instructions[index - 3];
		var element = _module.ResolveTypeToken((int)allocation.Operand!, caller, allocation.Offset);
		var size = element.Size;
		if (size is not (1 or 2 or 4 or 8) || element.Kind is not (CilTypeKind.Boolean or CilTypeKind.Character or CilTypeKind.SignedInteger or CilTypeKind.UnsignedInteger or CilTypeKind.FloatingPoint))
			throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedSignature, "Initialized arrays require fixed-size primitive elements.", caller.DisplayName, call.Offset);
		var field = caller.Instructions[index - 1];
		var key = $"array:{caller.ModuleName}:{field.Operand}:{size}:{count}";
		if (_initializedDataBySource.TryGetValue(key, out var existing)) return existing;
		var source = _module.ReadInitializedFieldRva((int)field.Operand!, caller, field.Offset);
		if ((long)count * size != source.Length)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Initialized field size does not match the array's primitive storage size.", caller.DisplayName, call.Offset);
		var bytes = new byte[source.Length];
		for (var offset = 0; offset < bytes.Length; offset += size)
		for (var lane = 0; lane < size; lane++) bytes[offset + lane] = source[offset + size - 1 - lane];
		// The array initializer only copies target-order bytes into a new array.
		var data = new InitializedData($"runtime:initialized-array:{_initializedDataBySource.Count:X4}", bytes);
		_initializedDataBySource.Add(key, data);
		return data;
	}

	private bool TryEmitInitializedByteFieldAddress(CilMethod caller, CilInstruction source, M68kRegister destination)
	{
		if ((_module.FrameworkImplementationPack?.EnableUnlistedManagedBodies != true &&
			!(caller.Name == "get_Log2DeBruijn" && _module.IsPinnedFloatingHardwareMethod(caller))) || source.OpCode != OpCodes.Ldsflda ||
			!_module.HasInitializedFieldRva((int)source.Operand!, caller, source.Offset)) return false;
		var length = caller.Instructions.FirstOrDefault(instruction => instruction.Offset == source.NextOffset);
		if (length is null || !TryGetConstant(length, out var byteCount)) return false;
		var constructor = caller.Instructions.FirstOrDefault(instruction => instruction.Offset == length.NextOffset);
		if (constructor is null || constructor.OpCode != OpCodes.Newobj) return false;
		var member = FrameworkImplementationProfile.Canonicalize(_module.DescribeFrameworkMethodToken((int)constructor.Operand!, caller, constructor.Offset));
		var element = FrameworkTypeId.Primitive("System.Byte");
		if (!member.DeclaringType.Equals(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [element])) ||
			member.Name != ".ctor" || member.MethodTypeArguments.Length != 0 || member.Signature.Header != 0x20 ||
			member.Signature.GenericParameterCount != 0 || member.Signature.RequiredParameterCount != 2 ||
			!member.Signature.ReturnType.Equals(FrameworkTypeId.Primitive("System.Void")) ||
			!member.Signature.ParameterTypes.SequenceEqual(new[] { FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Void")), FrameworkTypeId.Primitive("System.Int32") })) return false;
		// A byte view preserves PE byte order. No typed multi-byte reinterpretation
		// or ordinary zero-initialized field receives this ROM address substitution.
		var key = $"{caller.ModuleName}:{(int)source.Operand!:X8}:1";
		if (!_initializedDataBySource.TryGetValue(key, out var data))
		{
			var bytes = _module.ReadInitializedFieldRva((int)source.Operand!, caller, source.Offset).ToArray();
			data = new InitializedData($"runtime:initialized-span:{_initializedDataBySource.Count:X4}", bytes);
			_initializedDataBySource.Add(key, data);
		}
		data.RequiresDistinctAddress = data.RequiresDistinctAddress ||
			(uint)byteCount > (uint)data.Bytes.Length ||
			!HasPrivateInitializedSpanUses(caller, source, constructor, new CilType(CilTypeKind.UnsignedInteger, 1, "byte"));
		EmitAllocatedAddress(data.Label, destination); return true;
	}

	private bool HasPrivateInitializedSpanUses(CilMethod caller, CilInstruction source, CilInstruction factory, CilType element)
	{
		return (_privateInitializedSpanHelpers.Contains(caller.Identity) &&
			caller.Signature.ReturnType.GenericArguments[0].Equals(element)) ||
			HasPrivateInitializedSpanLocalUses(caller, source, factory, element);
	}

	private void AnalyzeInitializedSpanHelperCallers(
		IReadOnlyList<CilMethod> methods, CilMethod entry, IReadOnlyList<CilExport> exports)
	{
		var candidates = methods.Where(static method =>
			(method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private &&
			!method.IsVirtual && !method.IsImport && !method.Signature.Header.IsInstance &&
			method.Signature.GenericParameterCount == 0 && method.Signature.ParameterTypes.Length == 0 &&
			method.ExceptionRegions.Count == 0 &&
			method.Signature.ReturnType.DisplayName.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) &&
			method.Signature.ReturnType.GenericArguments.Length == 1 &&
			(method.Instructions is [var token, var factory, var ret] && token.OpCode == OpCodes.Ldtoken &&
			 factory.OpCode == OpCodes.Call && ret.OpCode == OpCodes.Ret ||
			 method.Instructions is [var address, var length, var constructor, var returnSpan] &&
			 address.OpCode == OpCodes.Ldsflda && TryGetConstant(length, out _) &&
			 constructor.OpCode == OpCodes.Newobj && returnSpan.OpCode == OpCodes.Ret))
			.ToDictionary(static method => method.Identity, static method => method.Signature.ReturnType.GenericArguments[0]);
		if (candidates.Count == 0) return;

		candidates.Remove(entry.Identity);
		if (_bulkCopyMethod is not null) candidates.Remove(_bulkCopyMethod.Identity);
		foreach (var export in exports) candidates.Remove(export.Method.Identity);
		foreach (var method in ReachableManagedPoolMethods) candidates.Remove(method.Identity);
		foreach (var lifecycle in _managedLifecycles)
			foreach (var method in lifecycle.Methods) candidates.Remove(method.Identity);
		foreach (var (alias, canonical) in _foldedMethodAliases)
		{
			candidates.Remove(alias);
			candidates.Remove(canonical.Identity);
		}

		// Use original reachable IL so pruning or inlining cannot hide an escape.
		foreach (var caller in methods)
		{
			if (candidates.Count == 0) break;
			foreach (var instruction in caller.Instructions)
			{
				if (!IsCallInstruction(instruction) && instruction.OpCode != OpCodes.Ldftn && instruction.OpCode != OpCodes.Ldvirtftn)
					continue;
				var target = _module.ResolveMethodToken((int)instruction.Operand!, caller, instruction.Offset);
				if (target.Definition is not { } helper || !candidates.TryGetValue(helper.Identity, out var element)) continue;
				if (instruction.OpCode == OpCodes.Call && HasPrivateInitializedSpanLocalUses(caller, instruction, instruction, element))
					_privateInitializedSpanHelpers.Add(helper.Identity);
				else
				{
					candidates.Remove(helper.Identity);
					_privateInitializedSpanHelpers.Remove(helper.Identity);
				}
			}
		}
	}

	private bool HasPrivateInitializedSpanLocalUses(CilMethod caller, CilInstruction source, CilInstruction factory, CilType element)
	{
		// Keep the proof local: the factory result goes straight into a local, and
		// every use of that local reads its length or one primitive value.
		var instructions = caller.Instructions;
		var store = instructions.FirstOrDefault(instruction => instruction.Offset == factory.NextOffset);
		if (caller.ExceptionRegions.Count != 0 || store is null || !TryGetStoreLocalIndex(store, out var localIndex) ||
			(uint)localIndex >= (uint)caller.Locals.Length ||
			!caller.Locals[localIndex].DisplayName.StartsWith("System.ReadOnlySpan`1<", StringComparison.Ordinal) ||
			caller.Locals[localIndex].GenericArguments is not [var localElement] || !localElement.Equals(element))
			return false;

		var branchTargets = GetBranchTargets(instructions);
		if (branchTargets.Any(offset => offset > source.Offset && offset <= store.Offset)) return false;

		for (var index = 0; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			if (TryGetLoadLocalIndex(instruction, out var loadedLocal) && loadedLocal == localIndex) return false;
			if (TryGetLoadLocalAddressIndex(instruction, out var addressedLocal) && addressedLocal == localIndex &&
				!IsInitializedSpanValueRead(caller, index, element, branchTargets)) return false;
		}
		return true;
	}

	private bool IsInitializedSpanValueRead(CilMethod caller, int addressIndex, CilType element, HashSet<int> branchTargets)
	{
		var instructions = caller.Instructions;
		if (addressIndex + 1 >= instructions.Count) return false;
		var next = instructions[addressIndex + 1];
		if (branchTargets.Contains(next.Offset)) return false;
		if (next.OpCode == OpCodes.Call || next.OpCode == OpCodes.Callvirt)
		{
			var accessor = _module.ResolveMethodToken((int)next.Operand!, caller, next.Offset);
			return accessor.ImportName == $"intrinsic:readonly-span-length:{element.DisplayName}" ||
				accessor.ImportName == $"intrinsic:readonly-span-is-empty:{element.DisplayName}";
		}

		if (addressIndex + 3 >= instructions.Count ||
			!(TryGetConstant(next, out _) || TryGetLoadLocalIndex(next, out _) || TryGetArgumentIndex(next, out _))) return false;
		var call = instructions[addressIndex + 2];
		var load = instructions[addressIndex + 3];
		if ((call.OpCode != OpCodes.Call && call.OpCode != OpCodes.Callvirt) || branchTargets.Contains(call.Offset) ||
			branchTargets.Contains(load.Offset) || !IsIndirectLoad(load.OpCode) ||
			load.OpCode == OpCodes.Ldobj || load.OpCode == OpCodes.Ldind_Ref || load.OpCode == OpCodes.Ldind_I ||
			GetIndirectAccess(load.OpCode).Size != element.Size) return false;
		var indexer = _module.ResolveMethodToken((int)call.Operand!, caller, call.Offset);
		return indexer.ImportName == $"intrinsic:readonly-span-get-item:{element.DisplayName}";
	}

	private void EmitInitializedData()
	{
		// By now all emitted references have been seen. An escaping use of any
		// view of the same backing data keeps the source at its own address.
		var pooledLabels = _initializedDataBySource.Values.Where(static data => !data.RequiresDistinctAddress)
			.ToLookup(static data => data.Bytes, static data => data.Label, InitializedPayloadComparer);
		foreach (var data in _initializedDataBySource.Values)
		{
			IEnumerable<string> labels = data.RequiresDistinctAddress ? [data.Label] : pooledLabels[data.Bytes];
			if (labels.First() != data.Label) continue;
			_assembler.AlignWord();
			foreach (var label in labels) _assembler.Mark(label);
			foreach (var value in data.Bytes) _assembler.EmitByte(value);
		}
	}
}
