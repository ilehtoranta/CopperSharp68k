/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Collections.Immutable;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private readonly record struct ArgumentValue(
		CilInstruction? Instruction,
		bool IsCStringLiteral = false,
		bool IsExportAddressLiteral = false,
		bool IsTransparentScalarRawGetter = false,
		bool IsCompactNullableValueGetter = false,
		bool AllowsInternalBranchTargets = false);

	private static bool IsArgumentValueInstruction(CilInstruction instruction) =>
		TryGetConstant(instruction, out _) ||
		instruction.OpCode == OpCodes.Ldnull ||
		instruction.OpCode == OpCodes.Ldsfld ||
		instruction.OpCode == OpCodes.Ldsflda ||
		TryGetLoadLocalIndex(instruction, out _) ||
		TryGetArgumentIndex(instruction, out _) ||
		TryGetLoadLocalAddressIndex(instruction, out _) ||
		TryGetLoadArgumentAddressIndex(instruction, out _);

	private bool TryGetArgumentValueExpression(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int valueIndex,
		out ArgumentValue value,
		out int consumed)
	{
		value = default;
		consumed = 0;
		if (valueIndex >= instructions.Count)
		{
			return false;
		}

		if (instructions[valueIndex].OpCode == OpCodes.Call)
		{
			var callTarget = _module.ResolveMethodToken(
				(int)instructions[valueIndex].Operand!,
				caller,
				instructions[valueIndex].Offset);
			if (callTarget.Definition is { } definition &&
				TryGetConstantReturnValue(definition, out var constant))
			{
				value = new ArgumentValue(new CilInstruction(
					instructions[valueIndex].Offset,
					OpCodes.Ldc_I4,
					constant,
					instructions[valueIndex].NextOffset));
				consumed = 1;
				return true;
			}
		}

		if (TryGetBooleanToUInt32Expression(
			instructions,
			valueIndex,
			out value,
			out consumed))
		{
			return true;
		}

		if (valueIndex + 1 < instructions.Count &&
			(instructions[valueIndex + 1].OpCode == OpCodes.Call ||
			 instructions[valueIndex + 1].OpCode == OpCodes.Callvirt) &&
			IsTransparentScalarRawGetterBaseInstruction(instructions[valueIndex]))
		{
			var getter = _module.ResolveMethodToken(
				(int)instructions[valueIndex + 1].Operand!,
				caller,
				instructions[valueIndex + 1].Offset);
			if (IsTransparentScalarRawGetter(getter))
			{
				if (valueIndex + 2 < instructions.Count &&
					(instructions[valueIndex + 2].OpCode == OpCodes.Call ||
					 instructions[valueIndex + 2].OpCode == OpCodes.Callvirt))
				{
					var wrapper = _module.ResolveMethodToken(
						(int)instructions[valueIndex + 2].Operand!,
						caller,
						instructions[valueIndex + 2].Offset);
					if (wrapper.ImportName == "intrinsic:cstring-from-pointer")
					{
						value = new ArgumentValue(
							instructions[valueIndex],
							IsTransparentScalarRawGetter: true);
						consumed = 3;
						return true;
					}
				}

				value = new ArgumentValue(
					instructions[valueIndex],
					IsTransparentScalarRawGetter: true);
				consumed = 2;
				return true;
			}
		}

		if (instructions[valueIndex].OpCode == OpCodes.Ldstr &&
			valueIndex + 2 < instructions.Count &&
			instructions[valueIndex + 1].OpCode is { } fromLiteralOp &&
			(fromLiteralOp == OpCodes.Call || fromLiteralOp == OpCodes.Callvirt) &&
			instructions[valueIndex + 2].OpCode is { } toUInt32Op &&
			(toUInt32Op == OpCodes.Call || toUInt32Op == OpCodes.Callvirt))
		{
			var fromLiteral = _module.ResolveMethodToken(
				(int)instructions[valueIndex + 1].Operand!,
				caller,
				instructions[valueIndex + 1].Offset);
			var toUInt32 = _module.ResolveMethodToken(
				(int)instructions[valueIndex + 2].Operand!,
				caller,
				instructions[valueIndex + 2].Offset);
			if (fromLiteral.ImportName == "intrinsic:cstring-from-literal" &&
				toUInt32.ImportName == "intrinsic:cstring-to-uint32")
			{
				value = new ArgumentValue(instructions[valueIndex], IsCStringLiteral: true);
				consumed = 3;
				return true;
			}
		}

		if (instructions[valueIndex].OpCode == OpCodes.Ldstr &&
			valueIndex + 1 < instructions.Count &&
			(instructions[valueIndex + 1].OpCode == OpCodes.Call ||
				instructions[valueIndex + 1].OpCode == OpCodes.Callvirt))
		{
			var exportAddress = _module.ResolveMethodToken(
				(int)instructions[valueIndex + 1].Operand!,
				caller,
				instructions[valueIndex + 1].Offset);
			if (exportAddress.ImportName == "intrinsic:aptr-export-address")
			{
				value = new ArgumentValue(
					instructions[valueIndex],
					IsExportAddressLiteral: true);
				consumed = 2;
				return true;
			}
		}

		if (valueIndex + 1 < instructions.Count &&
			(instructions[valueIndex + 1].OpCode == OpCodes.Call ||
			 instructions[valueIndex + 1].OpCode == OpCodes.Callvirt) &&
			TryGetLoadLocalAddressIndex(instructions[valueIndex], out var nullableLocalIndex) &&
			(uint)nullableLocalIndex < (uint)caller.Locals.Length &&
			IsCompactNullableType(caller.Locals[nullableLocalIndex]))
		{
			var getter = _module.ResolveMethodToken(
				(int)instructions[valueIndex + 1].Operand!,
				caller,
				instructions[valueIndex + 1].Offset);
			if (getter.ImportName?.StartsWith("intrinsic:nullable-get-value:", StringComparison.Ordinal) == true)
			{
				value = new ArgumentValue(
					instructions[valueIndex],
					IsCompactNullableValueGetter: true);
				consumed = 2;
				return true;
			}
		}

		if (instructions[valueIndex].OpCode == OpCodes.Ldstr &&
			valueIndex + 1 < instructions.Count &&
			instructions[valueIndex + 1].OpCode is { } cstringFromLiteralOp &&
			(cstringFromLiteralOp == OpCodes.Call || cstringFromLiteralOp == OpCodes.Callvirt))
		{
			var fromLiteral = _module.ResolveMethodToken(
				(int)instructions[valueIndex + 1].Operand!,
				caller,
				instructions[valueIndex + 1].Offset);
			if (IsCStringLiteralIntrinsic(fromLiteral.ImportName))
			{
				value = new ArgumentValue(instructions[valueIndex], IsCStringLiteral: true);
				consumed = 2;
				return true;
			}
		}

		if (!IsArgumentValueInstruction(instructions[valueIndex]))
		{
			return false;
		}

		value = new ArgumentValue(instructions[valueIndex]);
		consumed = 1;
		if (valueIndex + 1 >= instructions.Count)
		{
			return true;
		}

		var op = instructions[valueIndex + 1].OpCode;
		if (op == OpCodes.Newobj)
		{
			var constructor = _module.ResolveMethodToken(
				(int)instructions[valueIndex + 1].Operand!,
				caller,
				instructions[valueIndex + 1].Offset).Definition;
			if (constructor is not null &&
				_module.IsTransparentScalarConstructor(constructor))
			{
				consumed = 2;
			}

			return true;
		}

		if (valueIndex + 2 < instructions.Count &&
			IsArgumentValueInstruction(instructions[valueIndex]) &&
			instructions[valueIndex + 1].OpCode == OpCodes.Call &&
			instructions[valueIndex + 2].OpCode == OpCodes.Call)
		{
			var addressOf = _module.ResolveMethodToken(
				(int)instructions[valueIndex + 1].Operand!,
				caller,
				instructions[valueIndex + 1].Offset);
			var conversion = _module.ResolveMethodToken(
				(int)instructions[valueIndex + 2].Operand!,
				caller,
				instructions[valueIndex + 2].Offset);
			if (addressOf.ImportName is
					"intrinsic:hook-address-of" or
					"intrinsic:boopsi-message-address-of" or
					"intrinsic:address-of-ref" &&
				(conversion.ImportName == "intrinsic:aptr-to-uint32" ||
				 IsTransparentScalarToUInt32Conversion(conversion)))
			{
				value = new ArgumentValue(instructions[valueIndex]);
				consumed = 3;
				return true;
			}
		}

		if (op != OpCodes.Call && op != OpCodes.Callvirt)
		{
			return true;
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[valueIndex + 1].Operand!,
			caller,
			instructions[valueIndex + 1].Offset);
		if (target.ImportName is
				"intrinsic:cstring-from-pointer" or
				"intrinsic:cstring-to-uint32" or
				"intrinsic:hook-address-of" or
				"intrinsic:boopsi-message-address-of" or
				"intrinsic:address-of-ref" ||
			IsTransparentScalarToUInt32Conversion(target) ||
			IsUInt32ToTransparentScalarConversion(target) ||
			IsInt32ToTransparentScalarConversion(target) ||
			IsTransparentScalarToTransparentScalarConversion(target))
		{
			consumed = 2;
		}

		return true;
	}

	private static bool TryGetConstantReturnValue(CilMethod method, out int value)
	{
		value = 0;
		if (method.Signature.Header.IsInstance ||
			method.Signature.ParameterTypes.Length != 0 ||
			method.Instructions.Count != 2 ||
			method.Instructions[1].OpCode != OpCodes.Ret)
		{
			return false;
		}

		return TryGetConstant(method.Instructions[0], out value);
	}

	private static bool TryGetBooleanToUInt32Expression(
		IReadOnlyList<CilInstruction> instructions,
		int valueIndex,
		out ArgumentValue value,
		out int consumed)
	{
		value = default;
		consumed = 0;
		if (valueIndex + 4 >= instructions.Count)
		{
			return false;
		}

		var branchOp = instructions[valueIndex + 1].OpCode;
		if (!IsArgumentValueInstruction(instructions[valueIndex]) ||
			(branchOp != OpCodes.Brtrue && branchOp != OpCodes.Brtrue_S) ||
			instructions[valueIndex + 1].Operand is not int trueTarget)
		{
			return false;
		}

		var falseIndex = SkipNops(instructions, valueIndex + 2);
		if (falseIndex >= instructions.Count ||
			!TryGetConstant(instructions[falseIndex], out var falseValue) ||
			falseValue != 0)
		{
			return false;
		}

		var skipIndex = SkipNops(instructions, falseIndex + 1);
		if (skipIndex >= instructions.Count ||
			(instructions[skipIndex].OpCode != OpCodes.Br &&
			 instructions[skipIndex].OpCode != OpCodes.Br_S) ||
			instructions[skipIndex].Operand is not int doneTarget)
		{
			return false;
		}

		if (!TryGetInstructionIndexByOffset(instructions, trueTarget, out var trueIndex))
		{
			return false;
		}

		trueIndex = SkipNops(instructions, trueIndex);
		if (trueIndex >= instructions.Count ||
			!TryGetConstant(instructions[trueIndex], out var trueValue) ||
			trueValue != 1)
		{
			return false;
		}

		if (!TryGetInstructionIndexByOffset(instructions, doneTarget, out var doneIndex) ||
			doneIndex <= trueIndex)
		{
			return false;
		}

		value = new ArgumentValue(
			instructions[valueIndex],
			AllowsInternalBranchTargets: true);
		consumed = doneIndex - valueIndex;
		return true;
	}

	private static int SkipNops(IReadOnlyList<CilInstruction> instructions, int index)
	{
		while (index < instructions.Count && instructions[index].OpCode == OpCodes.Nop)
		{
			index++;
		}

		return index;
	}

	private static bool TryGetInstructionIndexByOffset(
		IReadOnlyList<CilInstruction> instructions,
		int offset,
		out int index)
	{
		for (index = 0; index < instructions.Count; index++)
		{
			if (instructions[index].Offset == offset)
			{
				return true;
			}
		}

		index = -1;
		return false;
	}

	private bool IsTransparentScalarToUInt32Conversion(MethodReference target) =>
		target.Signature.ReturnType.DisplayName == "uint" &&
		target.Signature.ParameterTypes.Length == 1 &&
		(target.Definition?.Name is "op_Implicit" or "ToUInt32" ||
		 target.ImportName?.EndsWith("-to-uint32", StringComparison.Ordinal) == true) &&
		_module.IsTransparentScalarType(target.Signature.ParameterTypes[0]);

	private bool IsUInt32ToTransparentScalarConversion(MethodReference target) =>
		target.Signature.ReturnType is { DisplayName: var returnType } &&
		target.Signature.ParameterTypes.Length == 1 &&
		target.Signature.ParameterTypes[0].DisplayName == "uint" &&
		(target.Definition?.Name is "op_Implicit" or "FromPointer" or "FromRaw" ||
			target.ImportName?.EndsWith("-from-pointer", StringComparison.Ordinal) == true ||
			target.ImportName?.EndsWith("-from-raw", StringComparison.Ordinal) == true ||
			target.ImportName?.EndsWith("-from-uint", StringComparison.Ordinal) == true ||
			target.ImportName?.EndsWith("-from-value", StringComparison.Ordinal) == true) &&
		_module.IsTransparentScalarType(new CilType(
			CilTypeKind.ValueType,
			4,
			returnType));

	private bool IsInt32ToTransparentScalarConversion(MethodReference target) =>
		target.Signature.ReturnType is { DisplayName: var returnType } &&
		target.Signature.ParameterTypes.Length == 1 &&
		target.Signature.ParameterTypes[0].DisplayName == "int" &&
		(target.Definition?.Name == "op_Implicit" ||
			target.ImportName?.EndsWith("-from-int", StringComparison.Ordinal) == true ||
			target.ImportName?.EndsWith("-from-value", StringComparison.Ordinal) == true) &&
		_module.IsTransparentScalarType(new CilType(
			CilTypeKind.ValueType,
			4,
			returnType));

	private bool IsTransparentScalarToTransparentScalarConversion(MethodReference target) =>
		target.Signature.ReturnType is { Kind: CilTypeKind.ValueType } returnType &&
		target.Signature.ParameterTypes is [var parameterType] &&
		(target.Definition?.Name == "op_Implicit" ||
			target.ImportName?.EndsWith("-from-value", StringComparison.Ordinal) == true) &&
		_module.IsTransparentScalarType(returnType) &&
		(parameterType.IsSupportedScalar && parameterType.Size == 4 ||
			_module.IsTransparentScalarType(parameterType));

	private static bool IsTransparentScalarRawGetterBaseInstruction(CilInstruction instruction) =>
		TryGetArgumentIndex(instruction, out _) ||
		TryGetLoadArgumentAddressIndex(instruction, out _) ||
		TryGetLoadLocalAddressIndex(instruction, out _);

	private bool IsTransparentScalarRawGetter(MethodReference target) =>
		target.Signature.ParameterTypes.Length == 0 &&
		target.Signature.ReturnType.DisplayName == "uint" &&
		(target.ImportName is { } importName &&
		 importName.StartsWith("intrinsic:", StringComparison.Ordinal) &&
		 importName.EndsWith("-raw", StringComparison.Ordinal) ||
		 target.Definition is { } definition &&
		 definition.Signature.Header.IsInstance &&
		 definition.Name == "get_Raw" &&
		 _module.IsTransparentScalarType(new CilType(
			 CilTypeKind.ValueType,
			 4,
			 definition.DisplayName.Split("::", StringSplitOptions.None)[0])));

	private void EmitArgumentValue(
		CilMethod caller,
		ArgumentValue value,
		int stackDepth)
	{
		if (value.Instruction is not { } instruction)
		{
			EmitPushConstant(0);
			return;
		}

		if (value.IsCStringLiteral)
		{
			var token = (int)instruction.Operand!;
			_cStringLiterals.TryAdd(token, _module.GetUserString(token));
			_assembler.EmitWord(0x2F3C); // MOVE.L #cstring,-(A7)
			_assembler.EmitAddress(CStringLabel(token));
			return;
		}

		if (value.IsExportAddressLiteral)
		{
			_assembler.EmitWord(0x2F3C); // MOVE.L #export,-(A7)
			_assembler.EmitAddress(ResolveExportAddressLabel(caller, instruction));
			return;
		}

		if (value.IsTransparentScalarRawGetter)
		{
			EmitTransparentScalarRawGetterValue(caller, instruction, stackDepth);
			return;
		}

		if (value.IsCompactNullableValueGetter)
		{
			EmitCompactNullableValueGetterValue(caller, instruction, stackDepth);
			return;
		}

		if (TryGetConstant(instruction, out var constant))
		{
			EmitPushConstant(constant);
			return;
		}

		if (instruction.OpCode == OpCodes.Ldnull)
		{
			EmitPushConstant(0);
			return;
		}

		if (instruction.OpCode == OpCodes.Ldsflda)
		{
			var field = _module.ResolveFieldToken(
				(int)instruction.Operand!,
				caller,
				instruction.Offset);
			if (!field.IsStatic)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Field '{field.DisplayName}' must be static when loaded by address.",
					caller.DisplayName,
					instruction.Offset);
			}

			_staticFields.TryAdd(field.Handle, field);
			_assembler.EmitWord(0x4879); // PEA abs.l
			_assembler.EmitAddress(StaticFieldLabel(field.Handle));
			return;
		}

		if (instruction.OpCode == OpCodes.Ldsfld)
		{
			EmitLoadStaticFieldToRegister(caller, instruction, M68kRegister.D0);
			EmitPushD0();
			return;
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			if (ArgumentRegister(argumentIndex) is { } register)
			{
				EmitPushRegister(register);
				return;
			}

			EmitPushFrameSlot(FrameDisplacement(
				ArgumentOffset(caller, argumentIndex),
				stackDepth));
			return;
		}

		if (TryGetLoadLocalIndex(instruction, out var localIndex))
		{
			ValidateLocal(caller, instruction, localIndex);
			if (LocalRegister(localIndex) is { } register)
			{
				EmitPushRegister(register);
				return;
			}

			EmitPushFrameSlot(FrameDisplacement(
				LocalOffset(caller, localIndex),
				stackDepth));
			return;
		}

		if (TryGetLoadLocalAddressIndex(instruction, out var localAddressIndex))
		{
			ValidateLocal(caller, instruction, localAddressIndex);
			_assembler.EmitWord(0x486F); // PEA d16(A7)
			_assembler.EmitWord(unchecked((ushort)FrameDisplacement(
				LocalOffset(caller, localAddressIndex),
				stackDepth)));
			return;
		}

		if (TryGetLoadArgumentAddressIndex(instruction, out var argumentAddressIndex))
		{
			ValidateArgument(caller, instruction, argumentAddressIndex);
			_assembler.EmitWord(0x486F); // PEA d16(A7)
			_assembler.EmitWord(unchecked((ushort)FrameDisplacement(
				ArgumentOffset(caller, argumentAddressIndex),
				stackDepth)));
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"Opcode '{instruction.OpCode.Name}' is not supported as a direct argument value.",
			caller.DisplayName,
			instruction.Offset);
	}

	private void EmitArgumentValueToRegister(
		CilMethod caller,
		ArgumentValue value,
		int stackDepth,
		M68kRegister register)
	{
		if (value.Instruction is not { } instruction)
		{
			EmitImmediateToRegister(register, 0);
			return;
		}

		if (value.IsTransparentScalarRawGetter)
		{
			EmitTransparentScalarRawGetterValueToRegister(caller, instruction, stackDepth, register);
			return;
		}

		if (value.IsCompactNullableValueGetter)
		{
			EmitCompactNullableValueGetterValueToRegister(caller, instruction, stackDepth, register);
			return;
		}

		if (value.IsCStringLiteral)
		{
			var token = (int)instruction.Operand!;
			_cStringLiterals.TryAdd(token, _module.GetUserString(token));
			EmitAddressImmediateToRegister(register, CStringLabel(token));
			return;
		}

		if (value.IsExportAddressLiteral)
		{
			EmitAddressImmediateToRegister(
				register,
				ResolveExportAddressLabel(caller, instruction));
			return;
		}

		if (TryGetConstant(instruction, out var constant))
		{
			EmitImmediateToRegister(register, constant);
			return;
		}

		if (instruction.OpCode == OpCodes.Ldnull)
		{
			EmitImmediateToRegister(register, 0);
			return;
		}

		if (instruction.OpCode == OpCodes.Ldsflda)
		{
			var field = _module.ResolveFieldToken(
				(int)instruction.Operand!,
				caller,
				instruction.Offset);
			if (!field.IsStatic)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Field '{field.DisplayName}' must be static when loaded by address.",
					caller.DisplayName,
					instruction.Offset);
			}

			_staticFields.TryAdd(field.Handle, field);
			EmitAddressImmediateToRegister(register, StaticFieldLabel(field.Handle));
			return;
		}

		if (instruction.OpCode == OpCodes.Ldsfld)
		{
			EmitLoadStaticFieldToRegister(caller, instruction, register);
			return;
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			if (ArgumentRegister(argumentIndex) is { } argumentRegister)
			{
				EmitMoveRegisterToRegister(argumentRegister, register);
				return;
			}

			EmitLoadRegisterFromStack(
				register,
				FrameDisplacement(
					ArgumentOffset(caller, argumentIndex),
					stackDepth));
			return;
		}

		if (TryGetLoadLocalIndex(instruction, out var localIndex))
		{
			ValidateLocal(caller, instruction, localIndex);
			if (LocalRegister(localIndex) is { } localRegister)
			{
				EmitMoveRegisterToRegister(localRegister, register);
				return;
			}

			EmitLoadRegisterFromStack(
				register,
				FrameDisplacement(
					LocalOffset(caller, localIndex),
					stackDepth));
			return;
		}

		if (TryGetLoadLocalAddressIndex(instruction, out var localAddressIndex))
		{
			ValidateLocal(caller, instruction, localAddressIndex);
			EmitFrameAddressValueToRegister(
				register,
				FrameDisplacement(
					LocalOffset(caller, localAddressIndex),
					stackDepth));
			return;
		}

		if (TryGetLoadArgumentAddressIndex(instruction, out var argumentAddressIndex))
		{
			ValidateArgument(caller, instruction, argumentAddressIndex);
			EmitFrameAddressValueToRegister(
				register,
				FrameDisplacement(
					ArgumentOffset(caller, argumentAddressIndex),
					stackDepth));
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"Opcode '{instruction.OpCode.Name}' is not supported as a register argument value.",
			caller.DisplayName,
			instruction.Offset);
	}

	private void EmitFrameAddressValueToRegister(M68kRegister register, short displacement)
	{
		if (register >= M68kRegister.A0)
		{
			EmitLoadFrameAddress(register, displacement);
			return;
		}

		EmitLoadFrameAddress(M68kRegister.A0, displacement);
		EmitMoveRegisterToRegister(M68kRegister.A0, register);
	}

	private bool TryEmitArgumentValueToFrame(
		CilMethod caller,
		ArgumentValue value,
		int stackDepth,
		short destination)
	{
		if (value.Instruction is not { } instruction)
		{
			EmitStoreZeroToFrame(destination);
			return true;
		}

		if (value.IsCStringLiteral)
		{
			var token = (int)instruction.Operand!;
			_cStringLiterals.TryAdd(token, _module.GetUserString(token));
			EmitAddressImmediateToFrame(CStringLabel(token), destination);
			return true;
		}

		if (value.IsExportAddressLiteral)
		{
			EmitAddressImmediateToFrame(
				ResolveExportAddressLabel(caller, instruction),
				destination);
			return true;
		}

		if (value.IsTransparentScalarRawGetter)
		{
			return TryEmitTransparentScalarRawGetterValueToFrame(
				caller,
				instruction,
				stackDepth,
				destination);
		}

		if (value.IsCompactNullableValueGetter)
		{
			return TryEmitCompactNullableValueGetterValueToFrame(caller, instruction, stackDepth, destination);
		}

		if (instruction.OpCode == OpCodes.Ldnull)
		{
			EmitStoreZeroToFrame(destination);
			return true;
		}

		if (instruction.OpCode == OpCodes.Ldsfld)
		{
			EmitLoadStaticFieldToRegister(caller, instruction, M68kRegister.D0);
			EmitStoreRegisterToFrame(M68kRegister.D0, destination);
			return true;
		}

		if (instruction.OpCode == OpCodes.Ldsflda)
		{
			var field = _module.ResolveFieldToken(
				(int)instruction.Operand!,
				caller,
				instruction.Offset);
			if (!field.IsStatic)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Field '{field.DisplayName}' must be static when loaded by address.",
					caller.DisplayName,
					instruction.Offset);
			}

			_staticFields.TryAdd(field.Handle, field);
			EmitAddressImmediateToFrame(StaticFieldLabel(field.Handle), destination);
			return true;
		}

		if (TryGetConstant(instruction, out var constant))
		{
			if (constant == 0)
			{
				EmitStoreZeroToFrame(destination);
			}
			else
			{
				EmitImmediateToFrame(constant, destination);
			}
			return true;
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			if (ArgumentRegister(argumentIndex) is { } promotedArgumentRegister)
			{
				EmitStoreRegisterToFrame(promotedArgumentRegister, destination);
				return true;
			}

			EmitMoveFrameSlotToFrameSlot(
				FrameDisplacement(
					ArgumentOffset(caller, argumentIndex),
					stackDepth),
				destination);
			return true;
		}

		if (TryGetLoadLocalIndex(instruction, out var localIndex))
		{
			ValidateLocal(caller, instruction, localIndex);
			if (LocalRegister(localIndex) is { } localRegister)
			{
				EmitStoreRegisterToFrame(localRegister, destination);
				return true;
			}

			EmitMoveFrameSlotToFrameSlot(
				FrameDisplacement(
					LocalOffset(caller, localIndex),
					stackDepth),
				destination);
			return true;
		}

		return false;
	}

	private static bool IsCStringLiteralIntrinsic(string? importName) =>
		importName is
			"intrinsic:cstring-from-literal" or
			"intrinsic:amiga-vararg-from-literal";

	private string ResolveExportAddressLabel(
		CilMethod caller,
		CilInstruction instruction)
	{
		var exportName = _module.GetUserString((int)instruction.Operand!);
		if (!_module.GetExports().Any(export => export.Name == exportName))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnresolvedImport,
				$"No [M68kExport] method named '{exportName}' exists.",
				caller.DisplayName,
				instruction.Offset);
		}

		return ExportLabel(exportName);
	}

	private void EmitLoadStaticFieldToRegister(
		CilMethod caller,
		CilInstruction instruction,
		M68kRegister register)
	{
		var field = _module.ResolveFieldToken(
			(int)instruction.Operand!,
			caller,
			instruction.Offset);
		if (!field.IsStatic)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Field '{field.DisplayName}' must be static when loaded by value.",
				caller.DisplayName,
				instruction.Offset);
		}

		_staticFields.TryAdd(field.Handle, field);
		var label = StaticFieldLabel(field.Handle);
		if (register <= M68kRegister.D7)
		{
			InvalidatePlatformBaseIfWritingRegister(register);
			_assembler.EmitWord((ushort)(0x2039 | ((int)register << 9)));
			_assembler.EmitAddress(label);
			return;
		}

		InvalidatePlatformBaseIfWritingRegister(register);
		var addressRegister = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x2079 | (addressRegister << 9)));
		_assembler.EmitAddress(label);
	}

	private bool TryEmitTransparentScalarRawGetterValueToFrame(
		CilMethod caller,
		CilInstruction instruction,
		int stackDepth,
		short destination)
	{
		if (TryGetLoadLocalAddressIndex(instruction, out var localIndex))
		{
			ValidateLocal(caller, instruction, localIndex);
			if (LocalRegister(localIndex) is { } localRegister)
			{
				EmitStoreRegisterToFrame(localRegister, destination);
				return true;
			}

			EmitMoveFrameSlotToFrameSlot(
				FrameDisplacement(
					LocalOffset(caller, localIndex),
					stackDepth),
				destination);
			return true;
		}

		if (TryGetLoadArgumentAddressIndex(instruction, out var argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			if (ArgumentRegister(argumentIndex) is { } promotedAddressArgument)
			{
				EmitStoreRegisterToFrame(promotedAddressArgument, destination);
				return true;
			}

			EmitMoveFrameSlotToFrameSlot(
				FrameDisplacement(
					ArgumentOffset(caller, argumentIndex),
					stackDepth),
				destination);
			return true;
		}

		if (!TryGetArgumentIndex(instruction, out argumentIndex))
		{
			return false;
		}

		ValidateArgument(caller, instruction, argumentIndex);
		if (caller.Signature.Header.IsInstance && argumentIndex == 0)
		{
			var sourceRegister = ArgumentRegister(argumentIndex);
			if (sourceRegister is null)
			{
				EmitLoadRegisterFromStack(
					M68kRegister.A0,
					FrameDisplacement(
						ArgumentOffset(caller, argumentIndex),
						stackDepth));
				sourceRegister = M68kRegister.A0;
			}

			EmitStoreAddressIndirectLongToFrame(sourceRegister.Value, destination);
			return true;
		}

		if (ArgumentRegister(argumentIndex) is { } argumentRegister)
		{
			EmitStoreRegisterToFrame(argumentRegister, destination);
			return true;
		}

		EmitMoveFrameSlotToFrameSlot(
			FrameDisplacement(
				ArgumentOffset(caller, argumentIndex),
				stackDepth),
			destination);
		return true;
	}

	private void EmitCompactNullableValueGetterValue(
		CilMethod caller,
		CilInstruction instruction,
		int stackDepth)
	{
		if (!TryGetLoadLocalAddressIndex(instruction, out var localIndex))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Opcode '{instruction.OpCode.Name}' is not supported for compact nullable value getters.",
				caller.DisplayName,
				instruction.Offset);
		}

		ValidateLocal(caller, instruction, localIndex);
		if (LocalRegister(localIndex) is { } localRegister)
		{
			EmitPushRegister(localRegister);
			return;
		}

		EmitPushFrameSlot(FrameDisplacement(
			LocalOffset(caller, localIndex),
			stackDepth));
	}

	private void EmitCompactNullableValueGetterValueToRegister(
		CilMethod caller,
		CilInstruction instruction,
		int stackDepth,
		M68kRegister register)
	{
		if (!TryGetLoadLocalAddressIndex(instruction, out var localIndex))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Opcode '{instruction.OpCode.Name}' is not supported as a compact nullable register getter.",
				caller.DisplayName,
				instruction.Offset);
		}

		ValidateLocal(caller, instruction, localIndex);
		if (LocalRegister(localIndex) is { } localRegister)
		{
			EmitMoveRegisterToRegister(localRegister, register);
			return;
		}

		EmitLoadRegisterFromStack(
			register,
			FrameDisplacement(
				LocalOffset(caller, localIndex),
				stackDepth));
	}

	private bool TryEmitCompactNullableValueGetterValueToFrame(
		CilMethod caller,
		CilInstruction instruction,
		int stackDepth,
		short destination)
	{
		if (!TryGetLoadLocalAddressIndex(instruction, out var localIndex))
		{
			return false;
		}

		ValidateLocal(caller, instruction, localIndex);
		if (LocalRegister(localIndex) is { } localRegister)
		{
			EmitStoreRegisterToFrame(localRegister, destination);
			return true;
		}

		EmitMoveFrameSlotToFrameSlot(
			FrameDisplacement(
				LocalOffset(caller, localIndex),
				stackDepth),
			destination);
		return true;
	}

	private void EmitTransparentScalarRawGetterValue(
		CilMethod caller,
		CilInstruction instruction,
		int stackDepth)
	{
		if (TryGetLoadLocalAddressIndex(instruction, out var localIndex))
		{
			ValidateLocal(caller, instruction, localIndex);
			if (LocalRegister(localIndex) is { } localRegister)
			{
				EmitPushRegister(localRegister);
				return;
			}

			EmitPushFrameSlot(FrameDisplacement(
				LocalOffset(caller, localIndex),
				stackDepth));
			return;
		}

		if (TryGetLoadArgumentAddressIndex(instruction, out var argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			if (ArgumentRegister(argumentIndex) is { } argumentRegister)
			{
				EmitPushRegister(argumentRegister);
				return;
			}

			EmitPushFrameSlot(FrameDisplacement(
				ArgumentOffset(caller, argumentIndex),
				stackDepth));
			return;
		}

		if (TryGetArgumentIndex(instruction, out argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			if (caller.Signature.Header.IsInstance && argumentIndex == 0)
			{
				if (IsTransparentScalarArgument(caller, argumentIndex))
				{
					if (ArgumentRegister(argumentIndex) is { } transparentArgumentRegister)
					{
						EmitPushRegister(transparentArgumentRegister);
						return;
					}

					EmitPushFrameSlot(FrameDisplacement(
						ArgumentOffset(caller, argumentIndex),
						stackDepth));
					return;
				}

				if (ArgumentRegister(argumentIndex) is not { } register)
				{
					EmitLoadRegisterFromStack(
						M68kRegister.A0,
						FrameDisplacement(
							ArgumentOffset(caller, argumentIndex),
							stackDepth));
					register = M68kRegister.A0;
				}
				else
				{
					EmitMoveRegisterToRegister(register, M68kRegister.A0);
					register = M68kRegister.A0;
				}

				_assembler.EmitWord(0x2F10); // MOVE.L (A0),-(A7)
				return;
			}

			if (ArgumentRegister(argumentIndex) is { } argumentRegister)
			{
				EmitPushRegister(argumentRegister);
				return;
			}

			EmitPushFrameSlot(FrameDisplacement(
				ArgumentOffset(caller, argumentIndex),
				stackDepth));
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"Opcode '{instruction.OpCode.Name}' is not supported for transparent scalar getters.",
			caller.DisplayName,
			instruction.Offset);
	}

	private void EmitTransparentScalarRawGetterValueToRegister(
		CilMethod caller,
		CilInstruction instruction,
		int stackDepth,
		M68kRegister register)
	{
		if (TryGetLoadLocalAddressIndex(instruction, out var localIndex))
		{
			ValidateLocal(caller, instruction, localIndex);
			if (LocalRegister(localIndex) is { } localRegister)
			{
				EmitMoveRegisterToRegister(localRegister, register);
				return;
			}

			EmitLoadRegisterFromStack(
				register,
				FrameDisplacement(
					LocalOffset(caller, localIndex),
					stackDepth));
			return;
		}

		if (TryGetLoadArgumentAddressIndex(instruction, out var argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			if (ArgumentRegister(argumentIndex) is { } argumentRegister)
			{
				EmitMoveRegisterToRegister(argumentRegister, register);
				return;
			}

			EmitLoadRegisterFromStack(
				register,
				FrameDisplacement(
					ArgumentOffset(caller, argumentIndex),
					stackDepth));
			return;
		}

		if (TryGetArgumentIndex(instruction, out argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			if (caller.Signature.Header.IsInstance && argumentIndex == 0)
			{
				if (ArgumentRegister(argumentIndex) is { } argumentRegister)
				{
					if (IsTransparentScalarArgument(caller, argumentIndex))
					{
						EmitMoveRegisterToRegister(argumentRegister, register);
						return;
					}

					EmitLoadRegisterFromAddressRegister(register, argumentRegister);
					return;
				}

				EmitLoadRegisterFromStack(
					M68kRegister.A0,
					FrameDisplacement(
						ArgumentOffset(caller, argumentIndex),
						stackDepth));
				EmitLoadRegisterFromAddressRegister(register, M68kRegister.A0);
				return;
			}

			if (ArgumentRegister(argumentIndex) is { } promotedArgument)
			{
				EmitMoveRegisterToRegister(promotedArgument, register);
				return;
			}

			EmitLoadRegisterFromStack(
				register,
				FrameDisplacement(
					ArgumentOffset(caller, argumentIndex),
					stackDepth));
			return;
		}

		if (TryGetLoadLocalAddressIndex(instruction, out var localAddressIndex))
		{
			ValidateLocal(caller, instruction, localAddressIndex);
			EmitLoadFrameAddress(
				register,
				FrameDisplacement(
					LocalOffset(caller, localAddressIndex),
					stackDepth));
			return;
		}

		if (TryGetLoadArgumentAddressIndex(instruction, out var argumentAddressIndex))
		{
			ValidateArgument(caller, instruction, argumentAddressIndex);
			EmitLoadFrameAddress(
				register,
				FrameDisplacement(
					ArgumentOffset(caller, argumentAddressIndex),
					stackDepth));
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"Opcode '{instruction.OpCode.Name}' is not supported as a transparent scalar register getter.",
			caller.DisplayName,
			instruction.Offset);
	}


}

