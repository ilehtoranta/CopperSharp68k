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
	private sealed record FrameLayout(
		int FrameBytes,
		short ClearStartOffset,
		int ClearLongs,
		ImmutableArray<short> LocalOffsets,
		ImmutableArray<M68kRegister?> LocalRegisters,
		ImmutableArray<short> ArgumentOffsets,
		ImmutableArray<M68kRegister?> ArgumentRegisters,
		ImmutableArray<M68kRegister> CalleeSavedRegisters,
		ImmutableArray<short> CalleeSaveOffsets,
		short VarargsScratchOffset,
		int VarargsScratchBytes,
		short DirectCallScratchOffset,
		int DirectCallScratchBytes,
		bool HasRuntimeFrame,
		ImmutableArray<short> GcScratchOffsets);


	private FrameLayout CreateFrameLayout(
		CilMethod method,
		InternalCallAbi internalAbi,
		IReadOnlySet<int> branchTargets,
		IReadOnlySet<int> reachableOffsets,
		IReadOnlyDictionary<int, ImmutableArray<CilStackValueKind>> reachableStackStates)
	{
		var hasRuntimeFrame = RequiresRuntimeFrame(method);
		var runtimeFrameLongs = hasRuntimeFrame ? RuntimeFrameHeaderLongs : 0;
		var argumentHomeCount = internalAbi.Arguments
			.Where(static argument => !argument.IsStack)
			.Sum(static argument => argument.SlotLongs);
		var argumentRegisters = SelectPromotedArgumentRegisters(
			method,
			internalAbi,
			branchTargets,
			reachableOffsets);
		for (var index = 0; index < argumentRegisters.Length; index++)
		{
			if (argumentRegisters[index] is not null)
			{
				argumentHomeCount -= internalAbi.Arguments[index].SlotLongs;
			}
		}
		var localRegisters = SelectPromotedLocalRegisters(method, branchTargets, reachableOffsets);
		var calleeSavedRegisters = GetCalleeSavedRegisters(
			method,
			localRegisters,
			reachableOffsets);
		var calleeSaveCount = calleeSavedRegisters.Length;
		var varargsScratchBytes = CalculateVarargsScratchBytes(method, branchTargets, reachableOffsets);
		var varargsScratchLongs = checked(varargsScratchBytes / 4);
		var directCallScratchBytes = CalculateDirectStackArgumentScratchBytes(
			method,
			reachableOffsets);
		var directCallScratchLongs = checked(directCallScratchBytes / 4);
		var gcScratchLongs = hasRuntimeFrame && M68kCompiler.IsManagedRuntime(_request)
			? reachableStackStates.Values.Max(static stack =>
				stack.Count(static kind => kind == CilStackValueKind.Reference))
			: 0;
		var frameLocalLongs = 0;
		for (var index = 0; index < method.Locals.Length; index++)
		{
			if (localRegisters[index] is null)
			{
				frameLocalLongs += LocalSlotLongs(method.Locals[index]);
			}
		}
		var frameBytes = checked(
			((runtimeFrameLongs + calleeSaveCount + frameLocalLongs + argumentHomeCount + gcScratchLongs) * 4) +
			varargsScratchBytes +
			directCallScratchBytes);
		if (frameBytes > short.MaxValue)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Local frame exceeds the LINK.W displacement range.",
				method.DisplayName);
		}

		var clearLocals = GetLocalsRequiringEntryClear(method, branchTargets, reachableOffsets);
		var localOffsets = new short[method.Locals.Length];
		var gcScratchOffsets = new short[gcScratchLongs];
		var gcScratchStartSlot = runtimeFrameLongs +
			calleeSaveCount +
			argumentHomeCount +
			varargsScratchLongs +
			directCallScratchLongs;
		for (var index = 0; index < gcScratchOffsets.Length; index++)
		{
			gcScratchOffsets[index] = checked((short)((gcScratchStartSlot + index) * 4));
		}

		var nextLocalSlot = gcScratchStartSlot + gcScratchLongs;
		for (var index = 0; index < method.Locals.Length; index++)
		{
			if (!clearLocals[index] || localRegisters[index] is not null)
			{
				continue;
			}

			localOffsets[index] = checked((short)(nextLocalSlot * 4));
			nextLocalSlot += LocalSlotLongs(method.Locals[index]);
		}

		var clearLongs = nextLocalSlot - gcScratchStartSlot;
		for (var index = 0; index < method.Locals.Length; index++)
		{
			if (clearLocals[index] || localRegisters[index] is not null)
			{
				continue;
			}

			localOffsets[index] = checked((short)(nextLocalSlot * 4));
			nextLocalSlot += LocalSlotLongs(method.Locals[index]);
		}

		var argumentOffsets = new short[method.ParameterCount];
		var nextArgumentHomeSlot = runtimeFrameLongs + calleeSaveCount;
		for (var index = 0; index < argumentOffsets.Length; index++)
		{
			var location = internalAbi.Arguments[index];
			argumentOffsets[index] = location.IsStack
				? checked((short)(frameBytes + 4 + location.StackOffset))
				: argumentRegisters[index] is null
					? checked((short)(nextArgumentHomeSlot * 4))
					: (short)0;
			if (!location.IsStack && argumentRegisters[index] is null)
			{
				nextArgumentHomeSlot += location.SlotLongs;
			}
		}

		return new FrameLayout(
			frameBytes,
			checked((short)(gcScratchStartSlot * 4)),
			clearLongs,
			localOffsets.ToImmutableArray(),
			localRegisters.ToImmutableArray(),
			argumentOffsets.ToImmutableArray(),
			argumentRegisters.ToImmutableArray(),
			calleeSavedRegisters,
			Enumerable.Range(0, calleeSaveCount)
				.Select(index => checked((short)((runtimeFrameLongs + index) * 4)))
				.ToImmutableArray(),
			checked((short)((runtimeFrameLongs + calleeSaveCount + argumentHomeCount) * 4)),
			varargsScratchBytes,
			checked((short)((runtimeFrameLongs + calleeSaveCount + argumentHomeCount + varargsScratchLongs) * 4)),
			directCallScratchBytes,
			hasRuntimeFrame,
			gcScratchOffsets.ToImmutableArray());
	}

	private int CalculateDirectStackArgumentScratchBytes(
		CilMethod caller,
		IReadOnlySet<int> reachableOffsets)
	{
		var result = 0;
		foreach (var instruction in caller.Instructions)
		{
			if (!reachableOffsets.Contains(instruction.Offset) ||
				(instruction.OpCode != OpCodes.Call &&
				 instruction.OpCode != OpCodes.Callvirt &&
				 instruction.OpCode != OpCodes.Newobj))
			{
				continue;
			}

			var target = _module.ResolveMethodToken(
				(int)instruction.Operand!,
				caller,
				instruction.Offset).Definition;
			if (target is null ||
				target.IsImport ||
				target.ExternalCall is not null)
			{
				continue;
			}

			var abi = GetInternalCallAbi(target);
			var dispatchSaveBytes =
				target.DeclaringTypeIsInterface || RequiresVirtualDispatch(instruction, target)
					? 12
					: 0;
			result = Math.Max(result, checked(abi.StackBytes + dispatchSaveBytes));
		}

		return result;
	}


	private bool TryEmitCompactArgumentHomeFrame(InternalCallAbi internalAbi)
	{
		if (internalAbi.StackBytes != 0 ||
			internalAbi.Arguments.Any(static argument => argument.SlotLongs != 1) ||
			CurrentFrameLayout.CalleeSavedRegisters.Length != 0 ||
			CurrentFrameLayout.HasRuntimeFrame ||
			CurrentFrameLayout.ClearLongs != 0 ||
			CurrentFrameLayout.VarargsScratchBytes != 0 ||
			CurrentFrameLayout.DirectCallScratchBytes != 0)
		{
			return false;
		}

		var homes = new List<(short Offset, M68kRegister Register)>();
		for (var index = 0; index < internalAbi.Arguments.Length; index++)
		{
			if (CurrentFrameLayout.ArgumentRegisters[index] is not null)
			{
				continue;
			}

			homes.Add((
				CurrentFrameLayout.ArgumentOffsets[index],
				internalAbi.Arguments[index].Register!.Value));
		}

		if (homes.Count == 0 ||
			CurrentFrameLayout.FrameBytes != homes.Count * 4)
		{
			return false;
		}

		foreach (var home in homes.OrderByDescending(static item => item.Offset))
		{
			EmitPushRegister(home.Register);
		}

		return true;
	}

	private M68kRegister?[] SelectPromotedArgumentRegisters(
		CilMethod method,
		InternalCallAbi internalAbi,
		IReadOnlySet<int> branchTargets,
		IReadOnlySet<int> reachableOffsets)
	{
		var result = new M68kRegister?[method.ParameterCount];
		if (RequiresRuntimeFrame(method))
		{
			return result;
		}

		for (var index = 0; index < internalAbi.Arguments.Length; index++)
		{
			if (internalAbi.Arguments[index].IsStack)
			{
				continue;
			}
			if (CanPromoteArgument(method, index, branchTargets, reachableOffsets))
			{
				result[index] = internalAbi.Arguments[index].Register;
			}
		}

		return result;
	}

	private bool CanPromoteArgument(
		CilMethod method,
		int argumentIndex,
		IReadOnlySet<int> branchTargets,
		IReadOnlySet<int> reachableOffsets)
	{
		if (!IsTransparentScalarDeclaringType(method))
		{
			return false;
		}

		if (IsReferenceParameter(method, argumentIndex) ||
			!IsSupportedScalarParameter(method, argumentIndex))
		{
			return false;
		}

		var firstAccess = -1;
		var lastAccess = -1;
		for (var index = 0; index < method.Instructions.Count; index++)
		{
			var instruction = method.Instructions[index];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if ((TryGetLoadArgumentAddressIndex(instruction, out var addressArgument) &&
				 addressArgument == argumentIndex))
			{
				if (!IsTransparentScalarArgumentRawGetterAccess(
					method,
					argumentIndex,
					index,
					reachableOffsets))
				{
					return false;
				}

				firstAccess = firstAccess < 0 ? index : firstAccess;
				lastAccess = index;
				continue;
			}

			if (instruction.OpCode is { } op &&
				(op == OpCodes.Starg || op == OpCodes.Starg_S) &&
				Convert.ToInt32(instruction.Operand) == argumentIndex)
			{
				return false;
			}

			if (TryGetArgumentIndex(instruction, out var loadedArgument) &&
				loadedArgument == argumentIndex)
			{
				firstAccess = firstAccess < 0 ? index : firstAccess;
				lastAccess = index;
			}
		}

		if (firstAccess < 0)
		{
			return true;
		}

		for (var index = firstAccess; index <= lastAccess; index++)
		{
			var instruction = method.Instructions[index];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (branchTargets.Contains(instruction.Offset) ||
				instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch ||
				instruction.OpCode == OpCodes.Call ||
				instruction.OpCode == OpCodes.Callvirt ||
				instruction.OpCode == OpCodes.Newobj ||
				instruction.OpCode == OpCodes.Newarr ||
				InstructionMayClobberPromotedLocalRegister(instruction.OpCode))
			{
				return false;
			}
		}

		return true;
	}

	private bool IsTransparentScalarDeclaringType(CilMethod method) =>
		_module.IsTransparentScalarType(new CilType(
			CilTypeKind.ValueType,
			4,
			method.DisplayName.Split("::", StringSplitOptions.None)[0]));

	private bool IsTransparentScalarArgumentRawGetterAccess(
		CilMethod method,
		int argumentIndex,
		int addressInstructionIndex,
		IReadOnlySet<int> reachableOffsets)
	{
		if (!_module.IsTransparentScalarType(TypeForArgument(method, argumentIndex)))
		{
			return false;
		}

		for (var index = addressInstructionIndex + 1; index < method.Instructions.Count; index++)
		{
			var instruction = method.Instructions[index];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (instruction.OpCode != OpCodes.Call &&
				instruction.OpCode != OpCodes.Callvirt)
			{
				return false;
			}

			var target = _module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return IsTransparentScalarRawGetter(target);
		}

		return false;
	}

	private M68kRegister?[] SelectPromotedLocalRegisters(
		CilMethod method,
		IReadOnlySet<int> branchTargets,
		IReadOnlySet<int> reachableOffsets)
	{
		var result = new M68kRegister?[method.Locals.Length];
		if (RequiresRuntimeFrame(method))
		{
			return result;
		}

		var dataRegisters = new[]
		{
			M68kRegister.D7,
			M68kRegister.D6,
			M68kRegister.D5,
			M68kRegister.D4,
			M68kRegister.D3,
			M68kRegister.D2
		};
		var addressRegisters = new[]
		{
			M68kRegister.A5,
			M68kRegister.A4,
			M68kRegister.A3,
			M68kRegister.A2,
			M68kRegister.A6
		};
		var reservedAddressRegisters = GetReservedAddressLocalRegisters(method, reachableOffsets);
		var candidates = new List<LocalLiveInterval>();
		var allocations = new List<LocalRegisterAllocation>();

		for (var index = 0; index < method.Locals.Length; index++)
		{
			if (TryGetPromotableLocalLiveInterval(
				method,
				index,
				reachableOffsets,
				out var interval))
			{
				candidates.Add(interval);
			}
		}

		foreach (var candidate in candidates
			.OrderByDescending(static item => item.AccessCount)
			.ThenBy(static item => item.Length)
			.ThenBy(static item => item.FirstInstructionIndex))
		{
			var registers = _module.IsTransparentScalarType(method.Locals[candidate.LocalIndex])
				? addressRegisters.Where(register => !reservedAddressRegisters.Contains(register))
				: dataRegisters;
			foreach (var register in registers)
			{
				if (allocations.Any(item =>
						item.Register == register &&
						item.Interval.Overlaps(candidate)) ||
					!CanKeepLocalInRegisterAcrossRange(
						method,
						candidate,
						register,
						branchTargets,
						reachableOffsets))
				{
					continue;
				}

				result[candidate.LocalIndex] = register;
				allocations.Add(new LocalRegisterAllocation(candidate, register));
				break;
			}
		}

		return result;
	}

	private ImmutableArray<M68kRegister> GetCalleeSavedRegisters(
		CilMethod method,
		IReadOnlyList<M68kRegister?> localRegisters,
		IReadOnlySet<int> reachableOffsets)
	{
		var result = new HashSet<M68kRegister>(
			localRegisters
				.OfType<M68kRegister>()
				.Where(IsInternalCalleeSavedRegister));

		foreach (var instruction in method.Instructions)
		{
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (instruction.OpCode == OpCodes.Mul && _request.Cpu == M68kCpuTarget.M68000)
			{
				result.Add(M68kRegister.D2);
				result.Add(M68kRegister.D3);
			}
			if (instruction.OpCode is var divide &&
				(divide == OpCodes.Div || divide == OpCodes.Div_Un ||
				 divide == OpCodes.Rem || divide == OpCodes.Rem_Un))
			{
				result.Add(M68kRegister.D2);
				if (_request.Cpu == M68kCpuTarget.M68000)
				{
					result.Add(M68kRegister.D3);
					result.Add(M68kRegister.D4);
					result.Add(M68kRegister.D5);
					result.Add(M68kRegister.D6);
				}
			}
			if (instruction.OpCode == OpCodes.Newarr || IsArrayAccess(instruction.OpCode))
			{
				result.Add(M68kRegister.D2);
			}

			if (instruction.OpCode != OpCodes.Call &&
				instruction.OpCode != OpCodes.Callvirt)
			{
				continue;
			}

			var target = _module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			if (target.ImportName == "intrinsic:boopsi-do-method")
			{
				var messageLongs = Math.Clamp(target.ParameterCount - 1, 0, 7);
				for (var dataIndex = 2; dataIndex < messageLongs; dataIndex++)
				{
					result.Add((M68kRegister)((int)M68kRegister.D0 + dataIndex));
				}
			}
			if (target.Definition is { } dispatchDefinition)
			{
				if (dispatchDefinition.DeclaringTypeIsInterface)
				{
					result.Add(M68kRegister.D2);
					result.Add(M68kRegister.A2);
					result.Add(M68kRegister.A3);
				}
				else if (RequiresVirtualDispatch(instruction, dispatchDefinition))
				{
					result.Add(M68kRegister.A2);
				}
			}

			var definition = target.Definition;
			if (definition?.ExternalCall is { } externalCall)
			{
				AddCalleeSavedRegister(result, externalCall.Convention.BaseRegister);
				if (externalCall.Convention.CacheRegister is { } cacheRegister)
				{
					AddCalleeSavedRegister(result, cacheRegister);
				}
				foreach (var register in externalCall.Abi.ParameterRegisters)
				{
					AddCalleeSavedRegister(result, register);
				}
				AddCalleeSavedRegister(result, externalCall.Abi.ReturnRegister);
				if (RequiresRuntimeFrame(method) &&
					externalCall.Convention.ExceptionPolicy ==
						M68kExternalExceptionPolicy.NonZeroStatus)
				{
					result.Add(M68kRegister.D5);
					result.Add(M68kRegister.D6);
					result.Add(M68kRegister.D7);
				}
			}
			else if (definition?.ImportAbi is { } importAbi)
			{
				foreach (var register in importAbi.ParameterRegisters)
				{
					AddCalleeSavedRegister(result, register);
				}
				AddCalleeSavedRegister(result, importAbi.ReturnRegister);
			}
		}

		if (RequiresRuntimeFrame(method))
		{
			result.Remove(M68kRegister.A5);
		}

		return result.OrderBy(static register => register).ToImmutableArray();
	}

	private static void AddCalleeSavedRegister(
		ISet<M68kRegister> registers,
		M68kRegister register)
	{
		if (IsInternalCalleeSavedRegister(register))
		{
			registers.Add(register);
		}
	}

	private static bool IsInternalCalleeSavedRegister(M68kRegister register) =>
		register is >= M68kRegister.D2 and <= M68kRegister.D7 or
			>= M68kRegister.A2 and <= M68kRegister.A6;

	private HashSet<M68kRegister> GetReservedAddressLocalRegisters(
		CilMethod method,
		IReadOnlySet<int> reachableOffsets)
	{
		var result = new HashSet<M68kRegister>();
		if (RequiresRuntimeFrame(method))
		{
			result.Add(M68kRegister.A5);
		}

		for (var index = 0; index < method.Instructions.Count; index++)
		{
			var instruction = method.Instructions[index];
			var op = instruction.OpCode;
			if (!reachableOffsets.Contains(instruction.Offset) ||
				(op != OpCodes.Call && op != OpCodes.Callvirt && op != OpCodes.Newobj))
			{
				continue;
			}

			var target = _module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			if (target.Definition?.ExternalCall?.Convention is not { } convention)
			{
				continue;
			}

			if (convention.BaseSource == M68kExternalBaseSource.CachedPointer &&
				convention.CacheRegister is { } cacheRegister)
			{
				result.Add(cacheRegister);
			}
		}

		return result;
	}

	private readonly record struct LocalLiveInterval(
		int LocalIndex,
		int FirstInstructionIndex,
		int LastInstructionIndex,
		int AccessCount)
	{
		public int Length => LastInstructionIndex - FirstInstructionIndex + 1;

		public bool Overlaps(LocalLiveInterval other) =>
			FirstInstructionIndex <= other.LastInstructionIndex &&
			other.FirstInstructionIndex <= LastInstructionIndex;
	}

	private readonly record struct LocalRegisterAllocation(
		LocalLiveInterval Interval,
		M68kRegister Register);

	private bool TryGetPromotableLocalLiveInterval(
		CilMethod method,
		int localIndex,
		IReadOnlySet<int> reachableOffsets,
		out LocalLiveInterval interval)
	{
		interval = default;
		if (method.Instructions.Any(static instruction =>
			instruction.OpCode == OpCodes.Conv_I1 || instruction.OpCode == OpCodes.Conv_U1 ||
			instruction.OpCode == OpCodes.Conv_I2 || instruction.OpCode == OpCodes.Conv_U2))
		{
			return false;
		}

		var localType = method.Locals[localIndex];
		var isTransparentScalar = _module.IsTransparentScalarType(localType);
		var isCompactNullable = IsCompactNullableType(localType);
		if (localType.IsNullable && !isCompactNullable ||
			localType.IsReference && !isCompactNullable ||
			(!localType.IsSupportedScalar && !isTransparentScalar && !isCompactNullable) ||
			(!isTransparentScalar && !isCompactNullable && localType.Size != 4))
		{
			return false;
		}

		var firstAccess = -1;
		var lastAccess = -1;
		var accessCount = 0;
		for (var index = 0; index < method.Instructions.Count; index++)
		{
			var instruction = method.Instructions[index];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (TryGetLoadLocalAddressIndex(instruction, out var addressLocal) &&
				addressLocal == localIndex)
			{
				if (isCompactNullable
					? !IsCompactNullableIntrinsicAccess(
						method,
						localIndex,
						index,
						reachableOffsets)
					: !IsTransparentScalarRawGetterAccess(
						method,
						localIndex,
						index,
						reachableOffsets))
				{
					return false;
				}

				firstAccess = firstAccess < 0 ? index : firstAccess;
				lastAccess = index;
				accessCount++;
				continue;
			}

			if ((TryGetLoadLocalIndex(instruction, out var loadedLocal) && loadedLocal == localIndex) ||
				(TryGetStoreLocalIndex(instruction, out var storedLocal) && storedLocal == localIndex))
			{
				firstAccess = firstAccess < 0 ? index : firstAccess;
				lastAccess = index;
				accessCount++;
			}
		}

		if (firstAccess < 0 ||
			!TryGetStoreLocalIndex(method.Instructions[firstAccess], out var firstStore) ||
			firstStore != localIndex)
		{
			return false;
		}

		for (var index = firstAccess; index <= lastAccess; index++)
		{
			var instruction = method.Instructions[index];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (InstructionMayClobberPromotedLocalRegister(instruction.OpCode))
			{
				return false;
			}
		}

		interval = new LocalLiveInterval(localIndex, firstAccess, lastAccess, accessCount);
		return true;
	}

	private bool IsTransparentScalarRawGetterAccess(
		CilMethod method,
		int localIndex,
		int addressInstructionIndex,
		IReadOnlySet<int> reachableOffsets)
	{
		if (!_module.IsTransparentScalarType(method.Locals[localIndex]))
		{
			return false;
		}

		for (var index = addressInstructionIndex + 1; index < method.Instructions.Count; index++)
		{
			var instruction = method.Instructions[index];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (instruction.OpCode != OpCodes.Call &&
				instruction.OpCode != OpCodes.Callvirt)
			{
				return false;
			}

			var target = _module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return IsTransparentScalarRawGetter(target);
		}

		return false;
	}

	private bool IsCompactNullableIntrinsicAccess(
		CilMethod method,
		int localIndex,
		int addressInstructionIndex,
		IReadOnlySet<int> reachableOffsets)
	{
		if (!IsCompactNullableType(method.Locals[localIndex]))
		{
			return false;
		}

		for (var index = addressInstructionIndex + 1; index < method.Instructions.Count; index++)
		{
			var instruction = method.Instructions[index];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (instruction.OpCode != OpCodes.Call &&
				instruction.OpCode != OpCodes.Callvirt)
			{
				return false;
			}

			var target = _module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			return IsCompactNullableIntrinsic(target);
		}

		return false;
	}

	private bool CanKeepLocalInRegisterAcrossRange(
		CilMethod method,
		LocalLiveInterval interval,
		M68kRegister register,
		IReadOnlySet<int> branchTargets,
		IReadOnlySet<int> reachableOffsets)
	{
		for (var index = interval.FirstInstructionIndex; index <= interval.LastInstructionIndex; index++)
		{
			var instruction = method.Instructions[index];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (!IsRegisterPreservedByInstruction(method, instruction, register))
			{
				if ((instruction.OpCode == OpCodes.Newarr ||
					 IsArrayAccess(instruction.OpCode)) &&
					IsInsideLoweredVarargsArray(method, index, branchTargets))
				{
					continue;
				}

				return false;
			}
		}

		return true;
	}

	private bool IsRegisterPreservedByInstruction(
		CilMethod method,
		CilInstruction instruction,
		M68kRegister register)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Newobj ||
			op == OpCodes.Switch)
		{
			return false;
		}

		if (op == OpCodes.Newarr)
		{
			return false;
		}

		if (IsArrayAccess(op))
		{
			return register != M68kRegister.D2;
		}

		if (op != OpCodes.Call && op != OpCodes.Callvirt)
		{
			return true;
		}

		var target = _module.ResolveMethodToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (IsTransparentScalarRawGetter(target))
		{
			return true;
		}

		if (target.Definition is not { } definition)
		{
			return true;
		}

		if (_module.IsTransparentScalarConstructor(definition) ||
			IsTransparentScalarToUInt32Conversion(target) ||
			IsUInt32ToTransparentScalarConversion(target) ||
			IsCompactNullableIntrinsic(target))
		{
			return true;
		}

		if (definition.ExternalCall is { } externalCall)
		{
			if (externalCall.Convention.BaseRegister == register)
			{
				return false;
			}

			return !CallAbiUsesRegister(
				externalCall.Abi,
				definition.Signature,
				register);
		}

		if (definition.IsImport)
		{
			return definition.ImportAbi is { } importAbi &&
				!CallAbiUsesRegister(
					importAbi,
					definition.Signature,
					register);
		}

		return false;
	}

	private bool IsInsideLoweredVarargsArray(
		CilMethod method,
		int instructionIndex,
		IReadOnlySet<int> branchTargets)
	{
		for (var startIndex = 0; startIndex <= instructionIndex; startIndex++)
		{
			if (!TryCollectVarargsArrayValues(
					method,
					method.Instructions,
					startIndex,
					branchTargets,
					out _,
					out var callIndex) ||
				instructionIndex > callIndex)
			{
				continue;
			}

			var target = _module.ResolveMethodToken(
				(int)method.Instructions[callIndex].Operand!,
				method,
				method.Instructions[callIndex].Offset);
			if (target.ImportName == "intrinsic:boopsi-do-method-stack-varargs" ||
				TryGetStackVarargsCallInfo(target, out _))
			{
				return true;
			}
		}

		return false;
	}

	private static bool CallAbiUsesRegister(
		CilRegisterAbi abi,
		MethodSignature<CilType> signature,
		M68kRegister register)
	{
		for (var index = 0; index < abi.ParameterRegisters.Count; index++)
		{
			var parameterRegister = abi.ParameterRegisters[index];
			if (parameterRegister == register)
			{
				return true;
			}

			if (index < signature.ParameterTypes.Length &&
				Is64BitScalar(signature.ParameterTypes[index]) &&
				NextDataRegisterOrNull(parameterRegister) == register)
			{
				return true;
			}
		}

		return abi.ReturnRegister == register;
	}

	private static M68kRegister? NextDataRegisterOrNull(M68kRegister register)
	{
		if (register < M68kRegister.D0 ||
			register >= M68kRegister.D7)
		{
			return null;
		}

		return register + 1;
	}

	private static bool InstructionMayClobberPromotedLocalRegister(OpCode op) =>
		op == OpCodes.Mul ||
		op == OpCodes.Div ||
		op == OpCodes.Div_Un ||
		op == OpCodes.Rem ||
		op == OpCodes.Rem_Un ||
		op == OpCodes.Shl ||
		op == OpCodes.Shr ||
		op == OpCodes.Shr_Un ||
		op == OpCodes.Switch;

	private bool[] GetLocalsRequiringEntryClear(
		CilMethod method,
		IReadOnlySet<int> branchTargets,
		IReadOnlySet<int> reachableOffsets)
	{
		var result = new bool[method.Locals.Length];
		if (!method.InitializeLocals)
		{
			return result;
		}

		Array.Fill(result, true);
		for (var localIndex = 0; localIndex < method.Locals.Length; localIndex++)
		{
			result[localIndex] = LocalRequiresEntryClear(
				method,
				localIndex,
				branchTargets,
				reachableOffsets);
		}

		return result;
	}

	private bool LocalRequiresEntryClear(
		CilMethod method,
		int localIndex,
		IReadOnlySet<int> branchTargets,
		IReadOnlySet<int> reachableOffsets)
	{
		if (_module.IsUninitializedStorageType(method.Locals[localIndex]))
		{
			return false;
		}

		var ambiguousBeforeFirstAccess = false;
		for (var instructionIndex = 0; instructionIndex < method.Instructions.Count; instructionIndex++)
		{
			var instruction = method.Instructions[instructionIndex];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (branchTargets.Contains(instruction.Offset) ||
				instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
			{
				ambiguousBeforeFirstAccess = true;
			}

			if (method.Locals[localIndex].IsReference &&
				!IsCompactNullableType(method.Locals[localIndex]) &&
				instruction.OpCode is { } op &&
				(op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj || op == OpCodes.Newarr))
			{
				ambiguousBeforeFirstAccess = true;
			}

			if (TryGetStoreLocalIndex(instruction, out var storedLocal) &&
				storedLocal == localIndex)
			{
				if (ambiguousBeforeFirstAccess &&
					IsImmediateBranchTempStore(
						method,
						localIndex,
						instructionIndex,
						branchTargets,
						reachableOffsets))
				{
					return false;
				}

				return ambiguousBeforeFirstAccess;
			}

			if (TryGetLoadLocalIndex(instruction, out var loadedLocal) &&
				loadedLocal == localIndex)
			{
				return true;
			}

			if (TryGetLoadLocalAddressIndex(instruction, out var addressLocal) &&
				addressLocal == localIndex)
			{
				return ambiguousBeforeFirstAccess ||
					!IsTransparentLocalConstructorInitialization(
						method,
						localIndex,
						instructionIndex,
						branchTargets,
						reachableOffsets);
			}
		}

		return false;
	}

	private static bool IsImmediateBranchTempStore(
		CilMethod method,
		int localIndex,
		int storeInstructionIndex,
		IReadOnlySet<int> branchTargets,
		IReadOnlySet<int> reachableOffsets)
	{
		var loadIndex = storeInstructionIndex + 1;
		while (loadIndex < method.Instructions.Count &&
			method.Instructions[loadIndex].OpCode == OpCodes.Nop)
		{
			if (branchTargets.Contains(method.Instructions[loadIndex].Offset))
			{
				return false;
			}

			loadIndex++;
		}

		var branchIndex = loadIndex + 1;
		while (branchIndex < method.Instructions.Count &&
			method.Instructions[branchIndex].OpCode == OpCodes.Nop)
		{
			if (branchTargets.Contains(method.Instructions[branchIndex].Offset))
			{
				return false;
			}

			branchIndex++;
		}

		if (loadIndex >= method.Instructions.Count ||
			branchIndex >= method.Instructions.Count ||
			!reachableOffsets.Contains(method.Instructions[loadIndex].Offset) ||
			!reachableOffsets.Contains(method.Instructions[branchIndex].Offset) ||
			branchTargets.Contains(method.Instructions[loadIndex].Offset) ||
			branchTargets.Contains(method.Instructions[branchIndex].Offset) ||
			!TryGetLoadLocalIndex(method.Instructions[loadIndex], out var loadedLocal) ||
			loadedLocal != localIndex)
		{
			return false;
		}

		var branchOp = method.Instructions[branchIndex].OpCode;
		if (branchOp != OpCodes.Brtrue && branchOp != OpCodes.Brtrue_S &&
			branchOp != OpCodes.Brfalse && branchOp != OpCodes.Brfalse_S)
		{
			return false;
		}

		for (var index = branchIndex + 1; index < method.Instructions.Count; index++)
		{
			if (!reachableOffsets.Contains(method.Instructions[index].Offset))
			{
				continue;
			}

			if (TryGetLoadLocalIndex(method.Instructions[index], out loadedLocal) &&
				loadedLocal == localIndex)
			{
				return false;
			}
		}

		return true;
	}

	private bool IsTransparentLocalConstructorInitialization(
		CilMethod method,
		int localIndex,
		int addressInstructionIndex,
		IReadOnlySet<int> branchTargets,
		IReadOnlySet<int> reachableOffsets)
	{
		if (!_module.IsTransparentScalarType(method.Locals[localIndex]))
		{
			return false;
		}

		for (var index = addressInstructionIndex + 1; index < method.Instructions.Count; index++)
		{
			var instruction = method.Instructions[index];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (branchTargets.Contains(instruction.Offset) ||
				instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
			{
				return false;
			}

			if ((TryGetLoadLocalIndex(instruction, out var loadedLocal) && loadedLocal == localIndex) ||
				(TryGetStoreLocalIndex(instruction, out var storedLocal) && storedLocal == localIndex) ||
				(TryGetLoadLocalAddressIndex(instruction, out var addressLocal) && addressLocal == localIndex))
			{
				return false;
			}

			if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
			{
				continue;
			}

			var target = _module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			if (target.Definition is { } definition &&
				_module.IsTransparentScalarConstructor(definition))
			{
				return true;
			}
		}

		return false;
	}


	private FrameLayout CurrentFrameLayout =>
		_currentFrameLayout ?? throw new InvalidOperationException("No active frame layout.");

	private int LocalSlotLongs(CilType type) =>
		checked(SlotLongs(type) + (_module.RequiresLongAlignedStackAddress(type) ? 1 : 0));

	private M68kRegister? LocalRegister(int index) =>
		(uint)index < (uint)CurrentFrameLayout.LocalRegisters.Length
			? CurrentFrameLayout.LocalRegisters[index]
			: null;

	private M68kRegister? ArgumentRegister(int index) =>
		(uint)index < (uint)CurrentFrameLayout.ArgumentRegisters.Length
			? CurrentFrameLayout.ArgumentRegisters[index]
			: null;

	private short LocalOffset(CilMethod method, int index)
	{
		if ((uint)index >= (uint)method.Locals.Length)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Local index {index} is outside the local signature.",
				method.DisplayName);
		}

		if (CurrentFrameLayout.LocalRegisters[index] is not null)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Promoted local index {index} has no frame slot.",
				method.DisplayName);
		}

		return CurrentFrameLayout.LocalOffsets[index];
	}

	private short ArgumentOffset(CilMethod method, int index)
	{
		if ((uint)index >= (uint)method.ParameterCount)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Argument index {index} is outside the method signature.",
				method.DisplayName);
		}

		return CurrentFrameLayout.ArgumentOffsets[index];
	}

	private short FrameDisplacement(short frameOffset, int stackDepth)
	{
		var byteDepth = stackDepth <= _currentStackTypes.Length
			? CilStackValueLayout.ByteDepth(_currentStackTypes, stackDepth)
			: checked(
				CilStackValueLayout.ByteDepth(_currentStackTypes, _currentStackTypes.Length) +
				((stackDepth - _currentStackTypes.Length) * 4));
		return checked((short)(frameOffset + byteDepth));
	}

}

