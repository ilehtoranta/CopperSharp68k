/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private sealed record InitializedSpanData(string Label, byte[] Bytes, int ElementSize);
	private readonly Dictionary<string, InitializedSpanData> _initializedSpanData = new(StringComparer.Ordinal);

	private InitializedSpanData RegisterInitializedSpanData(CilMethod caller, CilInstruction fieldToken, CilInstruction call)
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
		if (_initializedSpanData.TryGetValue(key, out var existing)) return existing;
		var source = _module.ReadInitializedFieldRva(token, caller, fieldToken.Offset);
		if (source.Length % size != 0)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Initialized field size is not a multiple of the span element size.", caller.DisplayName, call.Offset);
		var bytes = new byte[source.Length];
		for (var offset = 0; offset < bytes.Length; offset += size)
		for (var lane = 0; lane < size; lane++) bytes[offset + lane] = source[offset + size - 1 - lane];
		var data = new InitializedSpanData($"runtime:initialized-span:{_initializedSpanData.Count:X4}", bytes, size);
		_initializedSpanData.Add(key, data);
		return data;
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
			? RegisterInitializedArrayData(caller, next) : RegisterInitializedSpanData(caller, source, next);
		EmitAllocatedAddress(data.Label, destination);
		return true;
	}

	private InitializedSpanData RegisterInitializedArrayData(CilMethod caller, CilInstruction call)
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
		if (_initializedSpanData.TryGetValue(key, out var existing)) return existing;
		var source = _module.ReadInitializedFieldRva((int)field.Operand!, caller, field.Offset);
		if ((long)count * size != source.Length)
			throw new M68kCompilationException(M68kDiagnosticIds.InvalidMetadata, "Initialized field size does not match the array's primitive storage size.", caller.DisplayName, call.Offset);
		var bytes = new byte[source.Length];
		for (var offset = 0; offset < bytes.Length; offset += size)
		for (var lane = 0; lane < size; lane++) bytes[offset + lane] = source[offset + size - 1 - lane];
		var data = new InitializedSpanData($"runtime:initialized-array:{_initializedSpanData.Count:X4}", bytes, size);
		_initializedSpanData.Add(key, data);
		return data;
	}

	private bool TryEmitInitializedByteFieldAddress(CilMethod caller, CilInstruction source, M68kRegister destination)
	{
		if ((_module.FrameworkImplementationPack?.EnableUnlistedManagedBodies != true &&
			!(caller.Name == "get_Log2DeBruijn" && _module.IsPinnedFloatingHardwareMethod(caller))) || source.OpCode != OpCodes.Ldsflda ||
			!_module.HasInitializedFieldRva((int)source.Operand!, caller, source.Offset)) return false;
		var length = caller.Instructions.FirstOrDefault(instruction => instruction.Offset == source.NextOffset);
		if (length is null || !length.OpCode.Name!.StartsWith("ldc.i4", StringComparison.Ordinal)) return false;
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
		if (!_initializedSpanData.TryGetValue(key, out var data))
		{
			var bytes = _module.ReadInitializedFieldRva((int)source.Operand!, caller, source.Offset).ToArray();
			data = new InitializedSpanData($"runtime:initialized-span:{_initializedSpanData.Count:X4}", bytes, 1);
			_initializedSpanData.Add(key, data);
		}
		EmitAllocatedAddress(data.Label, destination); return true;
	}

	private void EmitInitializedSpanData()
	{
		foreach (var data in _initializedSpanData.Values)
		{
			_assembler.AlignWord();
			_assembler.Mark(data.Label);
			foreach (var value in data.Bytes) _assembler.EmitByte(value);
		}
	}
}
