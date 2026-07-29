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
	private readonly record struct StackVarargsCallInfo(
		int FixedParameterCount,
		M68kRegister VarargsRegister,
		IReadOnlyList<M68kRegister> FixedRegisters);

	private int CalculateVarargsScratchBytes(
		CilMethod method,
		IReadOnlySet<int> branchTargets,
		IReadOnlySet<int> reachableOffsets)
	{
		var result = 0;
		for (var index = 0; index < method.Instructions.Count; index++)
		{
			if (!reachableOffsets.Contains(method.Instructions[index].Offset))
			{
				continue;
			}

			if (TryGetBoopsiDoMethodFixedScratchBytes(
				method,
				method.Instructions,
				index,
				branchTargets,
				out var fixedBytes))
			{
				result = Math.Max(result, fixedBytes);
				continue;
			}

			if (TryGetVarargsArrayScratchBytes(
				method,
				method.Instructions,
				index,
				branchTargets,
				out var stackBytes))
			{
				result = Math.Max(result, stackBytes);
			}
		}

		return result;
	}

	private bool TryGetBoopsiDoMethodFixedScratchBytes(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int bytes)
	{
		bytes = 0;
		if (!TryCollectBoopsiDoMethodFixedValues(
			caller,
			instructions,
			startIndex,
			branchTargets,
			out var values,
			out _))
		{
			return false;
		}

		bytes = checked((values.Length - 1) * 4);
		return true;
	}

	private bool TryGetVarargsArrayScratchBytes(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int bytes)
	{
		bytes = 0;
		if (!TryCollectStackVarargsArrayCall(
			caller,
			instructions,
			startIndex,
			branchTargets,
			out _,
			out var values,
			out var callIndex,
			out _,
			out _))
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[callIndex].Operand!,
			caller,
			instructions[callIndex].Offset);
		var isBoopsiStackVarargs =
			target.ImportName == "intrinsic:boopsi-do-method-stack-varargs";
		if (!isBoopsiStackVarargs && !TryGetStackVarargsCallInfo(target, out _))
		{
			return false;
		}

		if (!isBoopsiStackVarargs &&
			TryGetForwardStackArgumentListStart(caller, values, out _))
		{
			return true;
		}

		bytes = checked(values.Length * 4);
		return true;
	}

	private bool TryGetForwardStackArgumentListStart(
		CilMethod caller,
		IReadOnlyList<ArgumentValue> values,
		out int firstArgumentIndex)
	{
		firstArgumentIndex = 0;
		if (values.Count == 0 || GetInternalRegisterAbi(caller) is not null)
		{
			return false;
		}

		if (values[0].Instruction is not { } firstInstruction ||
			!TryGetArgumentIndex(firstInstruction, out firstArgumentIndex))
		{
			return false;
		}

		for (var index = 0; index < values.Count; index++)
		{
			if (values[index].Instruction is not { } instruction ||
				!TryGetArgumentIndex(instruction, out var argumentIndex) ||
				argumentIndex != firstArgumentIndex + index ||
				ArgumentSlotLongs(caller, argumentIndex) != 1)
			{
				return false;
			}
		}

		return true;
	}


	private bool TryEmitBoopsiDoMethodFixedCall(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		if (!TryCollectBoopsiDoMethodFixedValues(
			caller,
			instructions,
			startIndex,
			branchTargets,
			out var values,
			out var callIndex))
		{
			return false;
		}

		var returnsDirectly = TryGetDirectReturnIndex(
			instructions,
			callIndex,
			branchTargets,
			out var returnIndex);
		var stackBytesToRelease = EmitBoopsiDoMethodFixedSimple(
			caller,
			values,
			callInstructionIndex: callIndex,
			pushResult: !returnsDirectly);
		if (returnsDirectly)
		{
			EmitReleaseStackBytes(checked(stackBytesToRelease + CurrentFrameLayout.FrameBytes));
			_assembler.EmitWord(0x4E75); // RTS
		}
		_loadedPlatformBase = null;
		consumed = (returnsDirectly ? returnIndex : callIndex) - startIndex + 1;
		return true;
	}

	private bool TryCollectBoopsiDoMethodFixedValues(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out ArgumentValue[] values,
		out int callIndex)
	{
		var builder = new List<ArgumentValue>();
		var index = startIndex;
		var callMayBeBranchTarget = false;
		values = [];
		callIndex = 0;
		while (index < instructions.Count)
		{
			var instruction = instructions[index];
			if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
			{
				break;
			}

			if (index != startIndex && branchTargets.Contains(instruction.Offset))
			{
				return false;
			}

			if (!TryGetArgumentValueExpression(
				caller,
				instructions,
				index,
				out var value,
				out var valueConsumed))
			{
				return false;
			}

			if (!value.AllowsInternalBranchTargets &&
				HasBranchTarget(
					branchTargets,
					instructions,
					startIndex,
					index + 1,
					index + valueConsumed - 1))
			{
				return false;
			}

			callMayBeBranchTarget = value.AllowsInternalBranchTargets;
			builder.Add(value);
			index += valueConsumed;
		}

		if (index >= instructions.Count ||
			builder.Count < 2 ||
			(branchTargets.Contains(instructions[index].Offset) && !callMayBeBranchTarget))
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[index].Operand!,
			caller,
			instructions[index].Offset);
		if (target.ImportName != "intrinsic:boopsi-do-method" ||
			target.ParameterCount != builder.Count)
		{
			return false;
		}

		values = builder.ToArray();
		callIndex = index;
		return true;
	}


	private int EmitBoopsiDoMethodFixedSimple(
		CilMethod caller,
		IReadOnlyList<ArgumentValue> values,
		int callInstructionIndex,
		bool pushResult)
	{
		var stackBytesToRelease = checked((values.Count - 1) * 4);
		if (TryEmitArgumentValuesToVarargsScratch(
			caller,
			values,
			startIndex: 1,
			stackDepth: _currentStackDepth,
			callInstructionIndex: callInstructionIndex,
			temporaryRegister: M68kRegister.D0,
			reservedRegisters: null,
			out var scratchDisplacement))
		{
			EmitArgumentValueToRegister(caller, values[0], _currentStackDepth, M68kRegister.A0);
			EmitLoadFrameAddress(M68kRegister.A1, scratchDisplacement);
			stackBytesToRelease = 0;
		}
		else
		{
			EmitArgumentValueToRegister(caller, values[0], _currentStackDepth, M68kRegister.A0);
			EmitArgumentValuesToStack(caller, values, startIndex: 1, stackDepth: _currentStackDepth);
			_assembler.EmitWord(0x224F); // MOVEA.L A7,A1
		}
		_assembler.EmitJsr("amiga.boopsi.DoMethodA", external: true);
		if (!pushResult)
		{
			return stackBytesToRelease;
		}

		EmitReleaseStackBytes(stackBytesToRelease);
		if (pushResult)
		{
			EmitPushD0();
		}

		return 0;
	}

	private bool TryEmitVarargsArrayCall(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		IReadOnlyDictionary<int, ImmutableArray<CilStackValueKind>> reachableStackStates,
		out int consumed)
	{
		consumed = 0;
		if (!TryCollectStackVarargsArrayCall(
			caller,
			instructions,
			startIndex,
			branchTargets,
			out var fixedValues,
			out var values,
			out var index,
			out var stackInfo,
			out var fixedValuesAlreadyOnStack))
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[index].Operand!,
			caller,
			instructions[index].Offset);
		if (target.ImportName == "intrinsic:boopsi-do-method-stack-varargs")
		{
			var returnsDirectly = TryGetDirectReturnIndex(
				instructions,
				index,
				branchTargets,
				out var returnIndex);
			var stackBytesToRelease = EmitBoopsiDoMethodVarargsArray(
				caller,
				values,
				callInstructionIndex: index,
				pushResult: !returnsDirectly);
			if (returnsDirectly)
			{
				EmitReleaseStackBytes(checked(stackBytesToRelease + CurrentFrameLayout.FrameBytes));
				_assembler.EmitWord(0x4E75); // RTS
			}
			_loadedPlatformBase = null;
			consumed = (returnsDirectly ? returnIndex : index) - startIndex + 1;
			return true;
		}

		if (!TryGetStackVarargsCallInfo(target, out stackInfo) ||
			target.Definition?.ExternalCall is not { } externalCall)
		{
			return false;
		}

		var amigaReturnsDirectly = TryGetDirectReturnIndex(
			instructions,
			index,
			branchTargets,
			out var amigaReturnIndex);
		var amigaDiscardIndex = 0;
		var amigaDiscardsResult = false;
		if (!amigaReturnsDirectly &&
			!target.Signature.ReturnType.IsVoid &&
			TryGetDiscardedResultIndex(
				instructions,
				index,
				branchTargets,
				out amigaDiscardIndex))
		{
			amigaDiscardsResult = true;
		}
		var amigaStoreIndex = 0;
		var amigaStoreDestination = default(StoreDestination);
		var amigaStoreStackTypes = default(ImmutableArray<CilStackValueKind>);
		var amigaStoresResult = false;
		if (!amigaReturnsDirectly &&
			!amigaDiscardsResult &&
			!target.Signature.ReturnType.IsVoid &&
			TryGetNextStoreIndex(
				instructions,
				index,
				branchTargets,
				out amigaStoreIndex,
				out amigaStoreDestination) &&
			reachableStackStates.TryGetValue(
				instructions[amigaStoreIndex].Offset,
				out amigaStoreStackTypes) &&
			amigaStoreStackTypes.Length != 0)
		{
			amigaStoresResult = true;
		}
		var amigaConstructorIndex = 0;
		var amigaConstructsWrapper = false;
		if (!amigaReturnsDirectly &&
			!amigaDiscardsResult &&
			!amigaStoresResult &&
			!target.Signature.ReturnType.IsVoid &&
			TryGetNextSimpleWrapperConstructorIndex(
				caller,
				instructions,
				index,
				branchTargets,
				out amigaConstructorIndex,
				out _))
		{
			amigaConstructsWrapper = true;
		}
		var amigaStackBytesToRelease = EmitStackVarargsArray(
			caller,
			target.Definition,
			externalCall,
			stackInfo,
			fixedValues,
			fixedValuesAlreadyOnStack,
			values,
			callInstructionIndex: index,
			pushResult: !amigaReturnsDirectly &&
				!amigaDiscardsResult &&
				!amigaStoresResult &&
				!amigaConstructsWrapper);
		if (amigaReturnsDirectly)
		{
			EmitReleaseStackBytes(checked(amigaStackBytesToRelease + CurrentFrameLayout.FrameBytes));
			_assembler.EmitWord(0x4E75); // RTS
		}
		else if (amigaDiscardsResult)
		{
			EmitReleaseStackBytes(amigaStackBytesToRelease);
		}
		else if (amigaStoresResult)
		{
			EmitReleaseStackBytes(amigaStackBytesToRelease);
			EmitStoreReturnToDestination(
				caller,
				target.Definition,
				amigaStoreDestination,
				stackDepth: amigaStoreStackTypes.Length - EvaluationSlotLongs(target.Signature.ReturnType));
		}
		else if (amigaConstructsWrapper)
		{
			EmitReleaseStackBytes(amigaStackBytesToRelease);
			EmitPopRegister(M68kRegister.A0);
			_assembler.EmitWord(0x2080); // MOVE.L D0,(A0)
		}
		consumed = (amigaReturnsDirectly
				? amigaReturnIndex
				: amigaDiscardsResult
					? amigaDiscardIndex
					: amigaStoresResult
					? amigaStoreIndex
					: amigaConstructsWrapper
						? amigaConstructorIndex
						: index) -
			startIndex + 1;
		return true;
	}

	private bool TryEmitVarargsArrayWrapperConstruction(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed,
		IReadOnlyDictionary<int, ArgumentValue>? localSubstitutions = null)
	{
		consumed = 0;
		if (!TryGetAddressStoreDestination(
				instructions[startIndex],
				out var destination) ||
			startIndex + 1 >= instructions.Count)
		{
			return false;
		}

		if (branchTargets.Contains(instructions[startIndex].Offset))
		{
			return false;
		}

		if (!TryGetArgumentValueExpression(
				caller,
				instructions,
				startIndex + 1,
				out var fixedArgument,
				out var fixedArgumentConsumed))
		{
			return false;
		}

		var collectStartIndex = startIndex + 1 + fixedArgumentConsumed;
		if (HasBranchTarget(
				branchTargets,
				instructions,
				startIndex,
				startIndex + 1,
				collectStartIndex - 1) ||
			!TryCollectVarargsArrayValues(
				caller,
				instructions,
				collectStartIndex,
				branchTargets,
				out var values,
				out var callIndex))
		{
			return false;
		}

		fixedArgument = SubstituteLocalVarargsValue(fixedArgument, localSubstitutions);
		for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
		{
			values[valueIndex] = SubstituteLocalVarargsValue(values[valueIndex], localSubstitutions);
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[callIndex].Operand!,
			caller,
			instructions[callIndex].Offset);
		if (!TryGetStackVarargsCallInfo(target, out var stackInfo) ||
			stackInfo.FixedParameterCount != 1 ||
			target.Definition?.ExternalCall is not { } externalCall ||
			target.Signature.ReturnType.IsVoid ||
			target.Signature.ReturnType.IsNullable ||
			target.Signature.ReturnType.Size == 8 ||
			target.Signature.ParameterTypes.Any(static type => type.Size == 8) ||
			!TryGetNextSimpleWrapperConstructorIndex(
				caller,
				instructions,
				callIndex,
				branchTargets,
				out var constructorIndex,
				out _))
		{
			return false;
		}

		var stackBytesToRelease = EmitStackVarargsArray(
			caller,
			target.Definition,
			externalCall,
			stackInfo,
			[fixedArgument],
			fixedValuesAlreadyOnStack: false,
			values,
			callInstructionIndex: callIndex,
			pushResult: false);
		EmitReleaseStackBytes(stackBytesToRelease);
		EmitStoreD0ToDestination(caller, destination, _currentStackDepth);
		consumed = constructorIndex - startIndex + 1;
		return true;
	}

	private bool TryEmitVarargsArrayWrapperConstructionWithSubstitutedLocals(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		var localSubstitutions = new Dictionary<int, ArgumentValue>();
		var index = startIndex;
		while (index < instructions.Count)
		{
			if (!TryGetArgumentValueExpression(
					caller,
					instructions,
					index,
					out var value,
					out var valueConsumed) ||
				!IsSubstitutableVarargsLocalValue(value))
			{
				break;
			}

			var storeIndex = index + valueConsumed;
			if (storeIndex >= instructions.Count ||
				HasBranchTarget(
					branchTargets,
					instructions,
					startIndex,
					index + 1,
					storeIndex) ||
				!TryGetStoreLocalIndex(instructions[storeIndex], out var localIndex) ||
				localSubstitutions.ContainsKey(localIndex))
			{
				break;
			}

			localSubstitutions.Add(localIndex, value);
			index = storeIndex + 1;
			if (!TrySkipNonTargetNops(instructions, branchTargets, ref index))
			{
				return false;
			}
		}

		if (localSubstitutions.Count == 0 ||
			!TryEmitVarargsArrayWrapperConstruction(
				caller,
				instructions,
				index,
				branchTargets,
				out var wrapperConsumed,
				localSubstitutions))
		{
			return false;
		}

		var lastConsumedIndex = index + wrapperConsumed - 1;
		if (localSubstitutions.Keys.Any(localIndex =>
				IsLocalAccessAfter(instructions, lastConsumedIndex, localIndex)))
		{
			return false;
		}

		consumed = lastConsumedIndex - startIndex + 1;
		return true;
	}

	private static bool IsSubstitutableVarargsLocalValue(ArgumentValue value)
	{
		if (value.Instruction is not { } instruction)
		{
			return true;
		}

		return value.IsCStringLiteral ||
			instruction.OpCode == OpCodes.Ldnull ||
			TryGetConstant(instruction, out _);
	}

	private static ArgumentValue SubstituteLocalVarargsValue(
		ArgumentValue value,
		IReadOnlyDictionary<int, ArgumentValue>? localSubstitutions)
	{
		if (localSubstitutions is null ||
			value.Instruction is not { } instruction ||
			value.IsCStringLiteral ||
			value.IsTransparentScalarRawGetter ||
			value.IsCompactNullableValueGetter ||
			!TryGetLoadLocalIndex(instruction, out var localIndex) ||
			!localSubstitutions.TryGetValue(localIndex, out var substitution))
		{
			return value;
		}

		return substitution;
	}

	private bool TryCollectVarargsArrayValues(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out ArgumentValue[] values,
		out int callIndex)
	{
		values = [];
		callIndex = 0;
		if (!TryGetConstant(instructions[startIndex], out var tagCount) ||
			tagCount < 0 ||
			startIndex + 2 >= instructions.Count ||
			instructions[startIndex + 1].OpCode != OpCodes.Newarr ||
			!IsStackVarargsNewArray(caller, instructions[startIndex + 1]))
		{
			return false;
		}

		values = new ArgumentValue[tagCount];
		var index = startIndex + 2;
		var expectedIndex = 0;
		if (index + 2 < instructions.Count &&
			instructions[index].OpCode == OpCodes.Dup &&
			instructions[index + 1].OpCode == OpCodes.Ldtoken &&
			instructions[index + 2].OpCode == OpCodes.Call &&
			!HasBranchTarget(branchTargets, instructions, startIndex, index, index + 2))
		{
			var initializer = _module.ResolveMethodToken(
				(int)instructions[index + 2].Operand!,
				caller,
				instructions[index + 2].Offset);
			if (initializer.ImportName == "intrinsic:initialize-array" &&
				instructions[index + 1].Operand is int fieldToken)
			{
				var constants = _module.ReadUInt32FieldRva(
					fieldToken,
					tagCount,
					caller,
					instructions[index + 1].Offset);
				for (var valueIndex = 0; valueIndex < constants.Length; valueIndex++)
				{
					values[valueIndex] = new ArgumentValue(new CilInstruction(
						instructions[index + 1].Offset,
						OpCodes.Ldc_I4,
						unchecked((int)constants[valueIndex]),
						instructions[index + 1].NextOffset));
				}
				index += 3;
			}
		}

		while (index < instructions.Count &&
			instructions[index].OpCode == OpCodes.Dup)
		{
			if (index + 2 >= instructions.Count ||
				HasBranchTarget(branchTargets, instructions, startIndex, index, index + 2) ||
				!TryGetConstant(instructions[index + 1], out var actualIndex) ||
				actualIndex < expectedIndex ||
				actualIndex >= tagCount ||
				!TryGetArgumentValueExpression(
					caller,
					instructions,
					index + 2,
					out var value,
				out var valueConsumed))
			{
				return false;
			}

			var conversionIndex = index + 2 + valueConsumed;
			if (conversionIndex < instructions.Count &&
				(instructions[conversionIndex].OpCode == OpCodes.Call ||
				 instructions[conversionIndex].OpCode == OpCodes.Callvirt))
			{
				var conversion = _module.ResolveMethodToken(
					(int)instructions[conversionIndex].Operand!,
					caller,
					instructions[conversionIndex].Offset);
				if (conversion.ImportName == "intrinsic:amiga-vararg-from-value")
				{
					valueConsumed++;
				}
			}

			var storeIndex = index + 2 + valueConsumed;
			var storeOp = storeIndex < instructions.Count
				? instructions[storeIndex].OpCode
				: default;
			if (storeIndex >= instructions.Count ||
				HasBranchTarget(branchTargets, instructions, startIndex, index + 3, storeIndex) ||
				(storeOp != OpCodes.Stelem_I4 &&
					storeOp != OpCodes.Stelem_I &&
					storeOp != OpCodes.Stelem))
			{
				return false;
			}

			values[actualIndex] = value;
			expectedIndex = actualIndex + 1;
			index = storeIndex + 1;
		}

		if (index >= instructions.Count ||
			HasBranchTarget(branchTargets, instructions, startIndex, index, index) ||
			(instructions[index].OpCode != OpCodes.Call &&
			 instructions[index].OpCode != OpCodes.Callvirt))
		{
			return false;
		}

		callIndex = index;
		return true;
	}

	private bool TryCollectStackVarargsArrayCall(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out ArgumentValue[] fixedValues,
		out ArgumentValue[] values,
		out int callIndex,
		out StackVarargsCallInfo info,
		out bool fixedValuesAlreadyOnStack)
	{
		fixedValues = [];
		values = [];
		callIndex = 0;
		info = default;
		fixedValuesAlreadyOnStack = false;

		var builder = new List<ArgumentValue>();
		var index = startIndex;
		while (index < instructions.Count)
		{
			if (TryCollectVarargsArrayValues(
					caller,
					instructions,
					index,
					branchTargets,
					out values,
					out callIndex))
			{
				var target = _module.ResolveMethodToken(
					(int)instructions[callIndex].Operand!,
					caller,
					instructions[callIndex].Offset);
				if (target.ImportName == "intrinsic:boopsi-do-method-stack-varargs")
				{
					return builder.Count == 0;
				}

				if (!TryGetStackVarargsCallInfo(target, out info))
				{
					return false;
				}

				if (builder.Count == info.FixedParameterCount)
				{
					fixedValues = builder.ToArray();
					return true;
				}

				if (builder.Count == 0 && info.FixedParameterCount != 0)
				{
					fixedValuesAlreadyOnStack = true;
					return true;
				}

				return false;
			}
			if (index != startIndex && branchTargets.Contains(instructions[index].Offset))
			{
				return false;
			}

			if (!TryGetArgumentValueExpression(
					caller,
					instructions,
					index,
					out var value,
					out var valueConsumed))
			{
				return false;
			}

			if (!value.AllowsInternalBranchTargets &&
				HasBranchTarget(
					branchTargets,
					instructions,
					startIndex,
					index + 1,
					index + valueConsumed - 1))
			{
				return false;
			}

			builder.Add(value);
			if (builder.Count > 16)
			{
				return false;
			}

			index += valueConsumed;
		}

		return false;
	}

	private bool TryGetStackVarargsCallInfo(
		MethodReference target,
		out StackVarargsCallInfo info)
	{
		info = default;
		if (target.Definition?.ExternalCall is not { } externalCall ||
			target.Signature.ParameterTypes.Length == 0 ||
			target.Signature.ParameterTypes[^1].ElementType is not { } elementType ||
			(elementType.DisplayName != "uint" &&
				!_module.IsTransparentScalarType(elementType)) ||
			externalCall.Abi.ParameterRegisters.Count == 0)
		{
			return false;
		}

		var fixedParameterCount = target.Signature.ParameterTypes.Length - 1;
		info = new StackVarargsCallInfo(
			fixedParameterCount,
			externalCall.Abi.ParameterRegisters[^1],
			externalCall.Abi.ParameterRegisters.Take(fixedParameterCount).ToArray());
		return true;
	}

	private int EmitStackVarargsArray(
		CilMethod caller,
		CilMethod target,
		CilExternalCall externalCall,
		StackVarargsCallInfo info,
		IReadOnlyList<ArgumentValue> fixedValues,
		bool fixedValuesAlreadyOnStack,
		IReadOnlyList<ArgumentValue> values,
		int callInstructionIndex,
		bool pushResult)
	{
		if (fixedValuesAlreadyOnStack)
		{
			EmitLoadRegistersFromEvaluationStack(info.FixedRegisters);
			EmitReleaseStackBytes(checked(info.FixedParameterCount * 4));
		}
		else
		{
			for (var index = 0; index < fixedValues.Count; index++)
			{
				EmitArgumentValueToRegister(
					caller,
					fixedValues[index],
					_currentStackDepth,
					info.FixedRegisters[index]);
			}
		}

		var valueStackDepth = fixedValuesAlreadyOnStack
			? checked(_currentStackDepth - info.FixedParameterCount)
			: _currentStackDepth;
		var stackBytesToRelease = checked(values.Count * 4);
		if (TryGetForwardStackArgumentListStart(caller, values, out var firstArgumentIndex))
		{
			EmitLoadVarargsFramePointer(
				info,
				FrameDisplacement(ArgumentOffset(caller, firstArgumentIndex), valueStackDepth));
			stackBytesToRelease = 0;
		}
		else if (TryEmitArgumentValuesToVarargsScratch(
			caller,
			values,
			startIndex: 0,
			stackDepth: valueStackDepth,
			callInstructionIndex: callInstructionIndex,
			temporaryRegister: SelectVarargsTemporaryDataRegister(info, externalCall),
			reservedRegisters: info.FixedRegisters
				.Append(info.VarargsRegister)
				.Append(externalCall.Convention.BaseRegister)
				.ToHashSet(),
			out var scratchDisplacement))
		{
			EmitLoadVarargsFramePointer(info, scratchDisplacement);
			stackBytesToRelease = 0;
		}
		else
		{
			EmitArgumentValuesToStack(caller, values, startIndex: 0, stackDepth: valueStackDepth);
			EmitMoveStackPointerToRegister(info.VarargsRegister);
		}
		EmitEnsurePlatformBase(externalCall.Convention, target);
		EmitBaseRelativeJsr(
			externalCall.Convention.BaseRegister,
			externalCall.Convention.Displacement);
		if (!target.Signature.ReturnType.IsVoid)
		{
			EmitMoveRegisterToD0(externalCall.Abi.ReturnRegister);
		}
		if (!pushResult)
		{
			return stackBytesToRelease;
		}

		EmitReleaseStackBytes(stackBytesToRelease);
		if (pushResult && !target.Signature.ReturnType.IsVoid)
		{
			EmitPushD0();
		}

		return 0;
	}

	private void EmitLoadVarargsFramePointer(
		StackVarargsCallInfo info,
		short displacement)
	{
		if (info.VarargsRegister <= M68kRegister.D7 &&
			displacement is > 0 and <= 8)
		{
			EmitMoveStackPointerToRegister(info.VarargsRegister);
			EmitQuickRegisterUpdate(info.VarargsRegister, displacement, subtract: false);
			return;
		}

		if (info.VarargsRegister >= M68kRegister.A0)
		{
			EmitLoadFrameAddress(info.VarargsRegister, displacement);
			return;
		}

		var temporaryAddressRegister = SelectVarargsTemporaryAddressRegister(info);
		EmitLoadFrameAddress(temporaryAddressRegister, displacement);
		EmitMoveRegister(temporaryAddressRegister, info.VarargsRegister);
	}

	private static M68kRegister SelectVarargsTemporaryDataRegister(
		StackVarargsCallInfo info,
		CilExternalCall externalCall)
	{
		for (var register = M68kRegister.D0; register <= M68kRegister.D7; register++)
		{
			if (register != info.VarargsRegister &&
				register != externalCall.Convention.BaseRegister &&
				!info.FixedRegisters.Contains(register))
			{
				return register;
			}
		}

		return M68kRegister.D0;
	}

	private static M68kRegister SelectVarargsTemporaryAddressRegister(
		StackVarargsCallInfo info)
	{
		for (var register = M68kRegister.A0; register <= M68kRegister.A6; register++)
		{
			if (register != info.VarargsRegister &&
				!info.FixedRegisters.Contains(register))
			{
				return register;
			}
		}

		return M68kRegister.A0;
	}

	private void EmitMoveStackPointerToRegister(M68kRegister register)
	{
		InvalidatePlatformBaseIfWritingRegister(register);
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x200F | ((int)register << 9))); // MOVE.L A7,Dn
			return;
		}

		var addressRegister = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x204F | (addressRegister << 9))); // MOVEA.L A7,An
	}

	private int EmitBoopsiDoMethodVarargsArray(
		CilMethod caller,
		IReadOnlyList<ArgumentValue> values,
		int callInstructionIndex,
		bool pushResult)
	{
		EmitPopRegister(M68kRegister.A0);
		var valueStackDepth = checked(_currentStackDepth - 1);
		var stackBytesToRelease = checked(values.Count * 4);
		if (TryEmitArgumentValuesToVarargsScratch(
			caller,
			values,
			startIndex: 0,
			stackDepth: valueStackDepth,
			callInstructionIndex: callInstructionIndex,
			temporaryRegister: M68kRegister.D0,
			reservedRegisters: null,
			out var scratchDisplacement))
		{
			EmitLoadFrameAddress(M68kRegister.A1, scratchDisplacement);
			stackBytesToRelease = 0;
		}
		else
		{
			EmitArgumentValuesToStack(caller, values, startIndex: 0, stackDepth: valueStackDepth);
			_assembler.EmitWord(0x224F); // MOVEA.L A7,A1
		}

		_assembler.EmitJsr("amiga.boopsi.DoMethodA", external: true);
		if (!pushResult)
		{
			return stackBytesToRelease;
		}

		EmitReleaseStackBytes(stackBytesToRelease);
		if (pushResult)
		{
			EmitPushD0();
		}

		return 0;
	}

	private bool TryEmitArgumentValuesToVarargsScratch(
		CilMethod caller,
		IReadOnlyList<ArgumentValue> values,
		int startIndex,
		int stackDepth,
		int callInstructionIndex,
		M68kRegister temporaryRegister,
		IReadOnlySet<M68kRegister>? reservedRegisters,
		out short scratchDisplacement)
	{
		var bytes = checked((values.Count - startIndex) * 4);
		scratchDisplacement = 0;
		if (bytes == 0 || CurrentFrameLayout.VarargsScratchBytes < bytes)
		{
			return false;
		}

		scratchDisplacement = FrameDisplacement(
			CurrentFrameLayout.VarargsScratchOffset,
			stackDepth);
		if (TryEmitArgumentValuesToContiguousDataRegisters(
			caller,
			values,
			startIndex,
			stackDepth,
			callInstructionIndex,
			reservedRegisters,
			scratchDisplacement))
		{
			return true;
		}

		for (var index = startIndex; index < values.Count;)
		{
			var destination = checked((short)(scratchDisplacement + ((index - startIndex) * 4)));
			if (TryGetMovemRegisterRun(
					caller,
					values,
					index,
					values.Count - 1,
					minCount: 2,
					out var runEnd,
					out var registers))
			{
				EmitStoreRegistersToFrame(registers, destination);
				index = runEnd + 1;
				continue;
			}

			if (!TryEmitArgumentValueToFrame(caller, values[index], stackDepth, destination))
			{
				EmitArgumentValueToRegister(caller, values[index], stackDepth, temporaryRegister);
				EmitStoreRegisterToFrame(
					temporaryRegister,
					destination);
			}

			index++;
		}

		return true;
	}

	private bool TryEmitArgumentValuesToContiguousDataRegisters(
		CilMethod caller,
		IReadOnlyList<ArgumentValue> values,
		int startIndex,
		int stackDepth,
		int callInstructionIndex,
		IReadOnlySet<M68kRegister>? reservedRegisters,
		short scratchDisplacement)
	{
		var count = values.Count - startIndex;
		if (count < 4 || count > 8 ||
			values.Skip(startIndex).Any(value => !CanMaterializeVarargsRegisterValue(value)))
		{
			return false;
		}

		var sourceLastUses = new Dictionary<M68kRegister, int>();
		for (var index = startIndex; index < values.Count; index++)
		{
			if (TryGetArgumentValueSourceRegister(caller, values[index], out var sourceRegister))
			{
				sourceLastUses[sourceRegister] = index;
			}
		}

		for (var first = (int)M68kRegister.D0;
			first + count <= (int)M68kRegister.D7 + 1;
			first++)
		{
			var registers = Enumerable.Range(first, count)
				.Select(static value => (M68kRegister)value)
				.ToArray();
			if ((reservedRegisters is not null && registers.Any(reservedRegisters.Contains)) ||
				!registers.All(register => IsAvailableVarargsStagingRegister(
					caller,
					register,
					sourceLastUses.Keys,
					callInstructionIndex)) ||
				!IsSafeVarargsRegisterMoveOrder(registers, sourceLastUses, startIndex))
			{
				continue;
			}

			for (var index = startIndex; index < values.Count; index++)
			{
				EmitArgumentValueToRegister(
					caller,
					values[index],
					stackDepth,
					registers[index - startIndex]);
			}

			EmitStoreRegistersToFrame(registers, scratchDisplacement);
			return true;
		}

		return false;
	}

	private bool IsAvailableVarargsStagingRegister(
		CilMethod caller,
		M68kRegister register,
		IEnumerable<M68kRegister> sourceRegisters,
		int callInstructionIndex)
	{
		if (register is M68kRegister.D0 or M68kRegister.D1)
		{
			return true;
		}

		if (sourceRegisters.Contains(register))
		{
			return !IsPromotedRegisterLiveAfterCall(caller, register, callInstructionIndex);
		}

		var registerIsMapped = CurrentFrameLayout.ArgumentRegisters.Contains(register) ||
			CurrentFrameLayout.LocalRegisters.Contains(register);
		return !registerIsMapped ||
			!IsPromotedRegisterLiveAfterCall(caller, register, callInstructionIndex);
	}

	private bool IsPromotedRegisterLiveAfterCall(
		CilMethod caller,
		M68kRegister register,
		int callInstructionIndex)
	{
		for (var index = 0; index < CurrentFrameLayout.ArgumentRegisters.Length; index++)
		{
			if (CurrentFrameLayout.ArgumentRegisters[index] == register &&
				IsArgumentValueLoadedAfter(caller, callInstructionIndex, index))
			{
				return true;
			}
		}

		for (var index = 0; index < CurrentFrameLayout.LocalRegisters.Length; index++)
		{
			if (CurrentFrameLayout.LocalRegisters[index] == register &&
				IsLocalValueLoadedAfter(caller, callInstructionIndex, index))
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsArgumentValueLoadedAfter(
		CilMethod caller,
		int callInstructionIndex,
		int argumentIndex)
	{
		var controlFlowSeen = false;
		for (var index = callInstructionIndex + 1; index < caller.Instructions.Count; index++)
		{
			var instruction = caller.Instructions[index];
			if (instruction.OpCode.FlowControl is
				FlowControl.Branch or FlowControl.Cond_Branch)
			{
				controlFlowSeen = true;
				continue;
			}

			if ((instruction.OpCode == OpCodes.Starg ||
				instruction.OpCode == OpCodes.Starg_S) &&
				Convert.ToInt32(instruction.Operand) == argumentIndex)
			{
				return controlFlowSeen;
			}

			if ((TryGetArgumentIndex(instruction, out var loadedIndex) &&
				loadedIndex == argumentIndex) ||
				(TryGetLoadArgumentAddressIndex(instruction, out loadedIndex) &&
				loadedIndex == argumentIndex))
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsLocalValueLoadedAfter(
		CilMethod caller,
		int callInstructionIndex,
		int localIndex)
	{
		var controlFlowSeen = false;
		for (var index = callInstructionIndex + 1; index < caller.Instructions.Count; index++)
		{
			var instruction = caller.Instructions[index];
			if (instruction.OpCode.FlowControl is
				FlowControl.Branch or FlowControl.Cond_Branch)
			{
				controlFlowSeen = true;
				continue;
			}

			if (TryGetStoreLocalIndex(instruction, out var storedIndex) &&
				storedIndex == localIndex)
			{
				return controlFlowSeen;
			}

			if ((TryGetLoadLocalIndex(instruction, out var loadedIndex) &&
				loadedIndex == localIndex) ||
				(TryGetLoadLocalAddressIndex(instruction, out loadedIndex) &&
				loadedIndex == localIndex))
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsSafeVarargsRegisterMoveOrder(
		IReadOnlyList<M68kRegister> registers,
		IReadOnlyDictionary<M68kRegister, int> sourceLastUses,
		int startIndex)
	{
		for (var index = 0; index < registers.Count; index++)
		{
			if (sourceLastUses.TryGetValue(registers[index], out var lastUse) &&
				lastUse > startIndex + index)
			{
				return false;
			}
		}

		return true;
	}

	private static bool CanMaterializeVarargsRegisterValue(ArgumentValue value)
	{
		if (value.Instruction is not { } instruction ||
			value.IsCStringLiteral ||
			value.IsExportAddressLiteral ||
			TryGetConstant(instruction, out _) ||
			(instruction.OpCode == OpCodes.Ldnull ||
				instruction.OpCode == OpCodes.Ldsfld ||
				instruction.OpCode == OpCodes.Ldsflda))
		{
			return true;
		}

		return TryGetArgumentIndex(instruction, out _) ||
			TryGetLoadLocalIndex(instruction, out _);
	}

	private void EmitArgumentValuesToStack(
		CilMethod caller,
		IReadOnlyList<ArgumentValue> values,
		int startIndex,
		int stackDepth)
	{
		var valueStackDepth = stackDepth;
		for (var index = values.Count - 1; index >= startIndex; index--)
		{
			if (TryGetMovemRegisterRunEndingAt(
					caller,
					values,
					startIndex,
					index,
					out var runStart,
					out var registers))
			{
				EmitPushRegisters(registers);
				valueStackDepth += registers.Length;
				index = runStart;
				continue;
			}

			EmitArgumentValue(caller, values[index], valueStackDepth);
			valueStackDepth++;
		}
	}

	private bool TryGetMovemRegisterRunEndingAt(
		CilMethod caller,
		IReadOnlyList<ArgumentValue> values,
		int startIndex,
		int endIndex,
		out int runStart,
		out M68kRegister[] registers)
	{
		registers = [];
		runStart = endIndex;
		var blockStart = endIndex;
		while (blockStart >= startIndex &&
			TryGetArgumentValueSourceRegister(caller, values[blockStart], out _))
		{
			blockStart--;
		}

		blockStart++;
		for (var candidateStart = blockStart; candidateStart <= endIndex - 2; candidateStart++)
		{
			if (!TryGetMovemRegisterRun(
					caller,
					values,
					candidateStart,
					endIndex,
					minCount: 3,
					out var candidateEnd,
					out registers))
			{
				continue;
			}

			if (candidateEnd != endIndex)
			{
				continue;
			}

			runStart = candidateStart;
			return true;
		}

		return false;
	}

	private bool TryGetMovemRegisterRun(
		CilMethod caller,
		IReadOnlyList<ArgumentValue> values,
		int startIndex,
		int maxEndIndex,
		int minCount,
		out int runEnd,
		out M68kRegister[] registers)
	{
		var collected = new List<M68kRegister>();
		runEnd = startIndex - 1;
		for (var index = startIndex; index <= maxEndIndex; index++)
		{
			if (!TryGetArgumentValueSourceRegister(caller, values[index], out var register))
			{
				break;
			}

			if (collected.Count != 0 &&
				MovemRegisterBit(register) <= MovemRegisterBit(collected[^1]))
			{
				break;
			}

			collected.Add(register);
			runEnd = index;
		}

		if (collected.Count < minCount)
		{
			registers = [];
			return false;
		}

		registers = collected.ToArray();
		return true;
	}

	private bool TryGetArgumentValueSourceRegister(
		CilMethod caller,
		ArgumentValue value,
		out M68kRegister register)
	{
		register = default;
		if (value.Instruction is not { } instruction ||
			value.IsCStringLiteral ||
			value.IsTransparentScalarRawGetter ||
			TryGetConstant(instruction, out _) ||
			instruction.OpCode == OpCodes.Ldnull ||
			TryGetLoadLocalAddressIndex(instruction, out _) ||
			TryGetLoadArgumentAddressIndex(instruction, out _))
		{
			return false;
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			ValidateArgument(caller, instruction, argumentIndex);
			if (ArgumentRegister(argumentIndex) is { } argumentRegister)
			{
				register = argumentRegister;
				return true;
			}
		}

		if (TryGetLoadLocalIndex(instruction, out var localIndex))
		{
			ValidateLocal(caller, instruction, localIndex);
			if (LocalRegister(localIndex) is { } localRegister)
			{
				register = localRegister;
				return true;
			}
		}

		return false;
	}

}

