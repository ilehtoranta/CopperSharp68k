/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Collections.Immutable;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed class M68kCodeGenerator
{
	private readonly CompilationModule _module;
	private readonly M68kCompilationRequest _request;
	private readonly M68kAssembler _assembler = new();
	private readonly HashSet<TypeDefinitionHandle> _usedTypeLayouts = new();
	private readonly Dictionary<FieldDefinitionHandle, CilField> _staticFields = new();
	private readonly Dictionary<int, string> _stringLiterals = new();
	private readonly Dictionary<int, string> _cStringLiterals = new();
	private readonly Dictionary<string, CilType> _arrayTypes = new(StringComparer.Ordinal);
	private readonly Dictionary<string, GeneratedPlatformBase> _usedPlatformBases = new(StringComparer.Ordinal);
	private readonly M68kMemoryManagement _memoryManagement;
	private int _uniqueLabel;
	private int _currentStackDepth;
	private ImmutableArray<CilStackValueKind> _currentStackTypes = ImmutableArray<CilStackValueKind>.Empty;
	private GeneratedPlatformBase? _loadedPlatformBase;

	private bool UsesBuiltInManagedPool =>
		_memoryManagement == M68kMemoryManagement.ManagedPoolMarkSweepGc;

	public M68kCodeGenerator(CompilationModule module, M68kCompilationRequest request)
	{
		_module = module;
		_request = request;
		_memoryManagement = M68kCompiler.GetEffectiveMemoryManagement(request);
	}

	public GeneratedProgram Generate(CilMethod entry)
	{
		ValidateMethodSignature(entry, isEntry: true);
		var exports = _module.GetExports();
		var methods = DiscoverReachableMethods(entry, exports);
		foreach (var method in methods)
		{
			CompileMethod(method);
		}
		var cachesPlatformBase = _usedPlatformBases.Values.Any(
			item => item.Binding.BaseSource == M68kExternalBaseSource.CachedPointer);
		var usesManagedRuntime = M68kCompiler.IsManagedRuntime(_request);
		foreach (var export in exports)
		{
			EmitExportAdapter(export, cachesPlatformBase);
		}
		var entryLabel = MethodLabel(entry);
		if (cachesPlatformBase || usesManagedRuntime)
		{
			entryLabel = EmitEntryAdapter(entry, usesManagedRuntime);
		}
		EmitManagedPoolRuntime();
		EmitData();

		return new GeneratedProgram(
			_assembler,
			methods,
			exports,
			_usedPlatformBases.Values.OrderBy(item => item.Binding.Identity, StringComparer.Ordinal).ToArray(),
			entryLabel);
	}

	private IReadOnlyList<CilMethod> DiscoverReachableMethods(
		CilMethod entry,
		IReadOnlyList<CilExport> exports)
	{
		var result = new List<CilMethod>();
		var visited = new HashSet<System.Reflection.Metadata.MethodDefinitionHandle>();
		var queue = new Queue<CilMethod>();
		queue.Enqueue(entry);
		foreach (var export in exports)
		{
			queue.Enqueue(export.Method);
		}

		while (queue.Count != 0)
		{
			var method = queue.Dequeue();
			if (!visited.Add(method.Handle))
			{
				continue;
			}

			ValidateMethodSignature(method, isEntry: method == entry);
			if (method.IsImport)
			{
				continue;
			}

			result.Add(method);
			foreach (var instruction in method.Instructions)
			{
				if (instruction.OpCode != OpCodes.Call &&
					instruction.OpCode != OpCodes.Callvirt &&
					instruction.OpCode != OpCodes.Newobj)
				{
					continue;
				}

				var target = _module.ResolveMethodToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset);
				if (target.Definition is { IsImport: false } definition)
				{
					queue.Enqueue(definition);
				}
			}
		}

		return result;
	}

	private void ValidateMethodSignature(CilMethod method, bool isEntry)
	{
		if (isEntry && method.Signature.Header.IsInstance)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"The image entry point must be static.",
				method.DisplayName);
		}

		if (isEntry && method.ParameterCount != 0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"The image entry point must not have parameters.",
				method.DisplayName);
		}

		ValidateType(method.Signature.ReturnType, method, "return type");
		foreach (var parameter in method.Signature.ParameterTypes)
		{
			ValidateType(parameter, method, "parameter");
		}

		foreach (var local in method.Locals)
		{
			ValidateType(local, method, "local");
		}
	}

	private static void ValidateType(CilType type, CilMethod method, string role)
	{
		if (type.IsVoid)
		{
			if (role == "return type")
			{
				return;
			}

			throw UnsupportedType(type, method, role);
		}

		if (type.IsFloatingPoint)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				$"Floating-point {role} '{type.DisplayName}' is disabled; no FPU code is emitted.",
				method.DisplayName);
		}

		if (!type.IsSupportedScalar || type.Size > 4)
		{
			throw UnsupportedType(type, method, role);
		}
	}

	private static M68kCompilationException UnsupportedType(
		CilType type,
		CilMethod method,
		string role) =>
		new(
			M68kDiagnosticIds.UnsupportedSignature,
			$"Unsupported {role} '{type.DisplayName}'. This compiler version accepts 32-bit scalar values.",
			method.DisplayName);

	private void CompileMethod(CilMethod method)
	{
		_loadedPlatformBase = null;
		var reachableStackStates = CilStackAnalyzer.AnalyzeTypes(method, _module);
		var registerAbi = GetInternalRegisterAbi(method);
		var argumentHomeCount = registerAbi?.Count ?? 0;
		_assembler.AlignWord();
		_assembler.Mark(MethodLabel(method));

		var frameBytes = checked((method.Locals.Length + argumentHomeCount) * 4);
		if (frameBytes > short.MaxValue)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Local frame exceeds the LINK.W displacement range.",
				method.DisplayName);
		}

		EmitAllocateFrame(frameBytes);
		if (registerAbi is not null)
		{
			for (var index = 0; index < registerAbi.Count; index++)
			{
				EmitStoreRegisterToFrame(registerAbi[index], ArgumentHomeOffset(index));
			}
		}
		if (method.InitializeLocals)
		{
			for (var index = 0; index < method.Locals.Length; index++)
			{
				EmitClearFrameSlot(LocalOffset(method, index));
			}
		}

		var branchTargets = GetBranchTargets(method.Instructions);
		for (var instructionIndex = 0; instructionIndex < method.Instructions.Count; instructionIndex++)
		{
			var instruction = method.Instructions[instructionIndex];
			if (!reachableStackStates.TryGetValue(instruction.Offset, out var stackTypes))
			{
				continue;
			}

			_currentStackTypes = stackTypes;
			_currentStackDepth = stackTypes.Length;
			if (branchTargets.Contains(instruction.Offset))
			{
				_loadedPlatformBase = null;
			}
			_assembler.Mark(IlLabel(method, instruction.Offset));
			if (TryEmitDirectExternalCall(
				method,
				method.Instructions,
				instructionIndex,
				branchTargets,
				out var directCallConsumed))
			{
				for (var skipped = 1; skipped < directCallConsumed; skipped++)
				{
					_assembler.Mark(IlLabel(
						method,
						method.Instructions[instructionIndex + skipped].Offset));
				}
				instructionIndex += directCallConsumed - 1;
				continue;
			}
			if (instructionIndex + 1 < method.Instructions.Count &&
				(instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
				method.Instructions[instructionIndex + 1] is { OpCode: var nextOp } returnInstruction &&
				nextOp == OpCodes.Ret &&
				!branchTargets.Contains(returnInstruction.Offset) &&
				TryEmitTailCall(method, instruction))
			{
				_assembler.Mark(IlLabel(method, returnInstruction.Offset));
				instructionIndex++;
				continue;
			}
			if (instructionIndex + 1 < method.Instructions.Count &&
				TryGetConstant(instruction, out var quickConstant) &&
				method.Instructions[instructionIndex + 1] is { } quickInstruction &&
				!branchTargets.Contains(quickInstruction.Offset) &&
				TryEmitQuickBinary(quickConstant, quickInstruction.OpCode))
			{
				_assembler.Mark(IlLabel(method, quickInstruction.Offset));
				instructionIndex++;
				continue;
			}
			EmitInstruction(method, instruction);
		}
	}

	private bool TryEmitDirectExternalCall(
		CilMethod caller,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out int consumed)
	{
		consumed = 0;
		var constants = new List<int>();
		var callIndex = startIndex;
		while (callIndex < instructions.Count &&
			TryGetConstant(instructions[callIndex], out var constant))
		{
			if (callIndex != startIndex &&
				branchTargets.Contains(instructions[callIndex].Offset))
			{
				return false;
			}
			constants.Add(constant);
			callIndex++;
		}
		if (constants.Count == 0 ||
			callIndex >= instructions.Count ||
			(instructions[callIndex].OpCode != OpCodes.Call &&
			 instructions[callIndex].OpCode != OpCodes.Callvirt) ||
			branchTargets.Contains(instructions[callIndex].Offset))
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instructions[callIndex].Operand!,
			caller,
			instructions[callIndex].Offset);
		if (target.Definition?.ExternalCall is not { } externalCall ||
			constants.Count != externalCall.Abi.ParameterRegisters.Count)
		{
			return false;
		}

		EmitEnsurePlatformBase(externalCall.Convention, target.Definition);
		var cacheRegister = externalCall.Convention.CacheRegister;
		var preservePlatformCache = cacheRegister is not null &&
			(externalCall.Abi.ReturnRegister == cacheRegister ||
			 externalCall.Abi.ParameterRegisters.Contains(cacheRegister.Value));
		if (preservePlatformCache)
		{
			EmitPushRegister(cacheRegister!.Value);
		}
		for (var index = 0; index < constants.Count; index++)
		{
			EmitImmediateToRegister(
				externalCall.Abi.ParameterRegisters[index],
				constants[index]);
		}
		EmitBaseRelativeJsr(
			externalCall.Convention.BaseRegister,
			externalCall.Convention.Displacement);
		if (!target.Signature.ReturnType.IsVoid)
		{
			EmitMoveRegisterToD0(externalCall.Abi.ReturnRegister);
		}
		if (preservePlatformCache)
		{
			EmitPopRegister(cacheRegister!.Value);
		}

		var returnIndex = callIndex + 1;
		var returnsDirectly =
			returnIndex < instructions.Count &&
			instructions[returnIndex].OpCode == OpCodes.Ret &&
			!branchTargets.Contains(instructions[returnIndex].Offset);
		if (returnsDirectly)
		{
			EmitFrameTeardown(caller);
			_assembler.EmitWord(0x4E75); // RTS
			consumed = returnIndex - startIndex + 1;
			return true;
		}
		if (!target.Signature.ReturnType.IsVoid)
		{
			EmitPushD0();
		}
		consumed = callIndex - startIndex + 1;
		return true;
	}

	private bool TryEmitQuickBinary(int constant, OpCode operation)
	{
		if (operation != OpCodes.Add && operation != OpCodes.Sub)
		{
			return false;
		}

		var subtract = operation == OpCodes.Sub;
		if (constant < 0)
		{
			if (constant < -8)
			{
				return false;
			}
			constant = -constant;
			subtract = !subtract;
		}
		else if (constant > 8)
		{
			return false;
		}

		if (constant == 0)
		{
			return true;
		}

		var encodedCount = constant == 8 ? 0 : constant;
		var opcode = 0x5080 | (encodedCount << 9) | 0x0017;
		if (subtract)
		{
			opcode |= 0x0100;
		}
		_assembler.EmitWord((ushort)opcode);
		return true;
	}

	private static HashSet<int> GetBranchTargets(IReadOnlyList<CilInstruction> instructions)
	{
		var result = new HashSet<int>();
		foreach (var instruction in instructions)
		{
			if (instruction.OpCode == OpCodes.Switch)
			{
				result.UnionWith((int[])instruction.Operand!);
			}
			else if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch &&
				instruction.Operand is int target)
			{
				result.Add(target);
			}
		}
		return result;
	}

	private void EmitInstruction(CilMethod method, CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Nop)
		{
			_assembler.EmitWord(0x4E71);
			return;
		}

		if (TryGetConstant(instruction, out var constant))
		{
			EmitPushConstant(constant);
			return;
		}

		if (op == OpCodes.Ldnull)
		{
			EmitPushConstant(0);
			return;
		}

		if (op == OpCodes.Ldstr)
		{
			var token = (int)instruction.Operand!;
			_stringLiterals.TryAdd(token, _module.GetUserString(token));
			_assembler.EmitWord(0x2F3C); // MOVE.L #string,-(A7)
			_assembler.EmitAddress(StringLabel(token));
			return;
		}

		if (TryGetArgumentIndex(instruction, out var argumentIndex))
		{
			EmitPushFrameSlot(FrameDisplacement(
				ArgumentOffset(method, argumentIndex),
				_currentStackDepth));
			return;
		}

		if (TryGetLoadLocalIndex(instruction, out var loadLocal))
		{
			ValidateLocal(method, instruction, loadLocal);
			EmitPushFrameSlot(FrameDisplacement(
				LocalOffset(method, loadLocal),
				_currentStackDepth));
			return;
		}

		if (TryGetStoreLocalIndex(instruction, out var storeLocal))
		{
			ValidateLocal(method, instruction, storeLocal);
			EmitPopFrameSlot(FrameDisplacement(
				LocalOffset(method, storeLocal),
				_currentStackDepth - 1));
			return;
		}

		if (op == OpCodes.Starg || op == OpCodes.Starg_S)
		{
			var index = Convert.ToInt32(instruction.Operand);
			ValidateArgument(method, instruction, index);
			EmitPopFrameSlot(FrameDisplacement(
				ArgumentOffset(method, index),
				_currentStackDepth - 1));
			return;
		}

		if (op == OpCodes.Dup)
		{
			_assembler.EmitWord(0x2017); // MOVE.L (A7),D0
			_assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
			return;
		}

		if (op == OpCodes.Pop)
		{
			_assembler.EmitWord(0x588F); // ADDQ.L #4,A7
			return;
		}

		if (op == OpCodes.Add || op == OpCodes.Sub ||
			op == OpCodes.And || op == OpCodes.Or || op == OpCodes.Xor)
		{
			EmitBinary(op);
			return;
		}

		if (op == OpCodes.Mul)
		{
			EmitPopBinaryOperands();
			EmitMultiply();
			EmitPushD0();
			return;
		}

		if (op == OpCodes.Div || op == OpCodes.Div_Un ||
			op == OpCodes.Rem || op == OpCodes.Rem_Un)
		{
			EmitPopBinaryOperands();
			EmitDivide(
				signed: op == OpCodes.Div || op == OpCodes.Rem,
				remainder: op == OpCodes.Rem || op == OpCodes.Rem_Un);
			EmitPushD0();
			return;
		}

		if (op == OpCodes.Shl || op == OpCodes.Shr || op == OpCodes.Shr_Un)
		{
			EmitPopBinaryOperands();
			EmitShift(op);
			EmitPushD0();
			return;
		}

		if (op == OpCodes.Neg || op == OpCodes.Not)
		{
			EmitPopD0();
			_assembler.EmitWord(op == OpCodes.Neg ? (ushort)0x4480 : (ushort)0x4680);
			EmitPushD0();
			return;
		}

		if (op == OpCodes.Ceq || op == OpCodes.Cgt || op == OpCodes.Cgt_Un ||
			op == OpCodes.Clt || op == OpCodes.Clt_Un)
		{
			EmitComparison(op);
			return;
		}

		if (IsUnconditionalBranch(op))
		{
			_assembler.EmitBranch(M68kCondition.True, IlLabel(method, (int)instruction.Operand!));
			return;
		}

		if (op == OpCodes.Brtrue || op == OpCodes.Brtrue_S ||
			op == OpCodes.Brfalse || op == OpCodes.Brfalse_S)
		{
			EmitPopD0();
			_assembler.EmitWord(0x4A80); // TST.L D0
			_assembler.EmitBranch(
				op == OpCodes.Brtrue || op == OpCodes.Brtrue_S
					? M68kCondition.NotEqual
					: M68kCondition.Equal,
				IlLabel(method, (int)instruction.Operand!));
			return;
		}

		if (TryGetRelationalBranch(op, out var branchCondition))
		{
			EmitPopBinaryOperands();
			_assembler.EmitWord(0xB081); // CMP.L D1,D0
			_assembler.EmitBranch(branchCondition, IlLabel(method, (int)instruction.Operand!));
			return;
		}

		if (op == OpCodes.Switch)
		{
			EmitSwitch(method, instruction);
			return;
		}

		if (op == OpCodes.Call || op == OpCodes.Callvirt)
		{
			EmitCall(method, instruction);
			return;
		}

		if (op == OpCodes.Newobj)
		{
			EmitNewObject(method, instruction);
			return;
		}

		if (op == OpCodes.Newarr)
		{
			EmitNewArray(method, instruction);
			return;
		}

		if (op == OpCodes.Ldlen)
		{
			EmitPopD0();
			EmitRequireNonNull();
			_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
			_assembler.EmitWord(0x2028); // MOVE.L 8(A0),D0
			_assembler.EmitWord(0x0008);
			EmitPushD0();
			return;
		}

		if (IsArrayAccess(op))
		{
			EmitArrayAccess(method, instruction);
			return;
		}

		if (IsIndirectLoad(op))
		{
			EmitIndirectLoad(op);
			return;
		}

		if (IsIndirectStore(op))
		{
			EmitIndirectStore(op);
			return;
		}

		if (op == OpCodes.Ldfld || op == OpCodes.Ldflda ||
			op == OpCodes.Stfld || op == OpCodes.Ldsfld ||
			op == OpCodes.Ldsflda || op == OpCodes.Stsfld)
		{
			EmitFieldAccess(method, instruction);
			return;
		}

		if (op == OpCodes.Ret)
		{
			if (!method.Signature.ReturnType.IsVoid)
			{
				if (method.Signature.ReturnType.IsReference)
				{
					_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0
				}
				else
				{
					EmitPopD0();
				}
			}

			EmitFrameTeardown(method);
			_assembler.EmitWord(0x4E75); // RTS
			return;
		}

		if (TryEmitConversion(op))
		{
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"CIL opcode '{op.Name}' is not implemented.",
			method.DisplayName,
			instruction.Offset);
	}

	private void EmitBinary(OpCode op)
	{
		EmitPopBinaryOperands();
		ushort opcode = op.Value switch
		{
			var value when value == OpCodes.Add.Value => 0xD081, // ADD.L D1,D0
			var value when value == OpCodes.Sub.Value => 0x9081, // SUB.L D1,D0
			var value when value == OpCodes.And.Value => 0xC081, // AND.L D1,D0
			var value when value == OpCodes.Or.Value => 0x8081, // OR.L D1,D0
			var value when value == OpCodes.Xor.Value => 0xB380, // EOR.L D1,D0
			_ => throw new InvalidOperationException()
		};
		_assembler.EmitWord(opcode);
		EmitPushD0();
	}

	private void EmitMultiply()
	{
		if (_request.Cpu != M68kCpuTarget.M68000)
		{
			_assembler.EmitWord(0x4C01); // MULS.L D1,D0
			_assembler.EmitWord(0x0800);
			return;
		}

		var loop = UniqueLabel("mul_loop");
		var skip = UniqueLabel("mul_skip");
		_assembler.EmitWord(0x7400); // MOVEQ #0,D2
		_assembler.EmitWord(0x761F); // MOVEQ #31,D3
		_assembler.Mark(loop);
		_assembler.EmitWord(0xE289); // LSR.L #1,D1
		_assembler.EmitBranch(M68kCondition.CarryClear, skip);
		_assembler.EmitWord(0xD480); // ADD.L D0,D2
		_assembler.Mark(skip);
		_assembler.EmitWord(0xD080); // ADD.L D0,D0
		_assembler.EmitDbra(3, loop);
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
	}

	private void EmitDivide(bool signed, bool remainder)
	{
		if (_request.Cpu != M68kCpuTarget.M68000)
		{
			_assembler.EmitWord(0x4C41); // DIV[SU].L D1,D2:D0
			_assembler.EmitWord((ushort)((signed ? 0x0800 : 0) | 0x0002));
			if (remainder)
			{
				_assembler.EmitWord(0x2002); // MOVE.L D2,D0
			}

			return;
		}

		var divisorReady = UniqueLabel("div_nonzero");
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.NotEqual, divisorReady);
		_assembler.EmitWord(0x4AFC); // ILLEGAL
		_assembler.Mark(divisorReady);

		string? dividendPositive = null;
		string? divisorPositive = null;
		if (signed)
		{
			dividendPositive = UniqueLabel("dividend_positive");
			divisorPositive = UniqueLabel("divisor_positive");
			_assembler.EmitWord(0x2C00); // MOVE.L D0,D6 (original dividend)
			_assembler.EmitWord(0x7A00); // MOVEQ #0,D5 (quotient sign)
			_assembler.EmitWord(0x4A80); // TST.L D0
			_assembler.EmitBranch(M68kCondition.Plus, dividendPositive);
			_assembler.EmitWord(0x4480); // NEG.L D0
			_assembler.EmitWord(0x08C5); // BSET #0,D5
			_assembler.EmitWord(0x0000);
			_assembler.Mark(dividendPositive);
			_assembler.EmitWord(0x4A81); // TST.L D1
			_assembler.EmitBranch(M68kCondition.Plus, divisorPositive);
			_assembler.EmitWord(0x4481); // NEG.L D1
			_assembler.EmitWord(0x0845); // BCHG #0,D5
			_assembler.EmitWord(0x0000);
			_assembler.Mark(divisorPositive);
		}

		var loop = UniqueLabel("div_loop");
		var noIncomingBit = UniqueLabel("div_no_bit");
		var noSubtract = UniqueLabel("div_no_sub");
		_assembler.EmitWord(0x7400); // MOVEQ #0,D2 quotient
		_assembler.EmitWord(0x7600); // MOVEQ #0,D3 remainder
		_assembler.EmitWord(0x781F); // MOVEQ #31,D4 counter/bit
		_assembler.Mark(loop);
		_assembler.EmitWord(0xE38B); // LSL.L #1,D3
		_assembler.EmitWord(0x0900); // BTST D4,D0
		_assembler.EmitBranch(M68kCondition.Equal, noIncomingBit);
		_assembler.EmitWord(0x0003); // ORI.B #1,D3
		_assembler.EmitWord(0x0001);
		_assembler.Mark(noIncomingBit);
		_assembler.EmitWord(0xB681); // CMP.L D1,D3
		_assembler.EmitBranch(M68kCondition.CarrySet, noSubtract);
		_assembler.EmitWord(0x9681); // SUB.L D1,D3
		_assembler.EmitWord(0x09C2); // BSET D4,D2
		_assembler.Mark(noSubtract);
		_assembler.EmitDbra(4, loop);

		if (signed)
		{
			var quotientPositive = UniqueLabel("quotient_positive");
			var remainderPositive = UniqueLabel("remainder_positive");
			_assembler.EmitWord(0x0805); // BTST #0,D5
			_assembler.EmitWord(0x0000);
			_assembler.EmitBranch(M68kCondition.Equal, quotientPositive);
			_assembler.EmitWord(0x4482); // NEG.L D2
			_assembler.Mark(quotientPositive);
			_assembler.EmitWord(0x4A86); // TST.L D6
			_assembler.EmitBranch(M68kCondition.Plus, remainderPositive);
			_assembler.EmitWord(0x4483); // NEG.L D3
			_assembler.Mark(remainderPositive);
		}

		_assembler.EmitWord(remainder ? (ushort)0x2003 : (ushort)0x2002);
	}

	private void EmitShift(OpCode op)
	{
		var done = UniqueLabel("shift_done");
		var loop = UniqueLabel("shift_loop");
		_assembler.EmitWord(0x0281); // ANDI.L #31,D1
		_assembler.EmitLong(31);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.Equal, done);
		_assembler.Mark(loop);
		_assembler.EmitWord(
			op == OpCodes.Shl
				? (ushort)0xE388 // LSL.L #1,D0
				: op == OpCodes.Shr
					? (ushort)0xE280 // ASR.L #1,D0
					: (ushort)0xE288); // LSR.L #1,D0
		_assembler.EmitWord(0x5381); // SUBQ.L #1,D1
		_assembler.EmitBranch(M68kCondition.NotEqual, loop);
		_assembler.Mark(done);
	}

	private void EmitComparison(OpCode op)
	{
		EmitPopBinaryOperands();
		_assembler.EmitWord(0xB081); // CMP.L D1,D0
		var condition = op == OpCodes.Ceq
			? M68kCondition.Equal
			: op == OpCodes.Cgt
				? M68kCondition.GreaterThan
				: op == OpCodes.Cgt_Un
					? M68kCondition.Higher
					: op == OpCodes.Clt
						? M68kCondition.LessThan
						: M68kCondition.CarrySet;
		_assembler.EmitWord((ushort)(0x50C0 | ((int)condition << 8))); // Scc D0
		_assembler.EmitWord(0x4880); // EXT.W D0
		_assembler.EmitWord(0x48C0); // EXT.L D0
		_assembler.EmitWord(0x4480); // NEG.L D0, FFFFFFFF -> 1
		EmitPushD0();
	}

	private void EmitSwitch(CilMethod method, CilInstruction instruction)
	{
		EmitPopD0();
		var targets = (int[])instruction.Operand!;
		for (var index = 0; index < targets.Length; index++)
		{
			_assembler.EmitWord(0x0C80); // CMPI.L #index,D0
			_assembler.EmitLong((uint)index);
			_assembler.EmitBranch(M68kCondition.Equal, IlLabel(method, targets[index]));
		}
	}

	private void EmitCall(CilMethod caller, CilInstruction instruction)
	{
		var target = _module.ResolveMethodToken((int)instruction.Operand!, caller, instruction.Offset);
		if (target.Definition is null)
		{
			if (target.ImportName == "intrinsic:object-ctor")
			{
				EmitDiscardStackArguments(target.ParameterCount);
				return;
			}

			if (target.ImportName == "intrinsic:string-length")
			{
				EmitPopD0();
				EmitRequireNonNull();
				_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
				_assembler.EmitWord(0x2028); // MOVE.L 8(A0),D0
				_assembler.EmitWord(0x0008);
				EmitPushD0();
				return;
			}

			if (target.ImportName == "intrinsic:cstring-from-literal")
			{
				EmitCStringFromLiteral(caller, instruction);
				return;
			}

			if (target.ImportName is "intrinsic:cstring-from-pointer" or "intrinsic:cstring-to-uint32")
			{
				return;
			}

			if (target.ImportName == "intrinsic:runtime-dispose")
			{
				EmitRuntimeJsr(RuntimeDisposeLabel, M68kRuntimeImports.Dispose);
				_loadedPlatformBase = null;
				EmitDiscardStackArguments(target.ParameterCount);
				return;
			}

			if (target.ImportName == "intrinsic:runtime-gc-collect")
			{
				if (UsesBuiltInManagedPool)
				{
					EmitMarkManagedRoots(caller);
				}
				EmitRuntimeJsr(RuntimeCollectLabel, M68kRuntimeImports.GcCollect);
				_loadedPlatformBase = null;
				return;
			}

			if (target.ImportName == "intrinsic:runtime-GetGcStaleBytes")
			{
				EmitRuntimeJsr(RuntimeGetStaleBytesLabel, M68kRuntimeImports.GcGetStaleBytes);
				_loadedPlatformBase = null;
				EmitPushD0();
				return;
			}

			if (target.ImportName == "intrinsic:runtime-GetGcStaleBlocks")
			{
				EmitRuntimeJsr(RuntimeGetStaleBlocksLabel, M68kRuntimeImports.GcGetStaleBlocks);
				_loadedPlatformBase = null;
				EmitPushD0();
				return;
			}

			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Unresolved call target.",
				caller.DisplayName,
				instruction.Offset);
		}

		ValidateMethodSignature(target.Definition, isEntry: false);
		if (target.Definition.ExternalCall is { } externalCall)
		{
			EmitExternalCall(target.Definition, externalCall);
		}
		else if (target.Definition.IsImport)
		{
			if (target.Definition.ImportAbi is { } importAbi)
			{
				for (var index = 0; index < importAbi.ParameterRegisters.Count; index++)
				{
					var stackOffset = checked(
						(importAbi.ParameterRegisters.Count - 1 - index) * 4);
					EmitLoadRegisterFromStack(
						importAbi.ParameterRegisters[index],
						stackOffset);
				}
			}

			_assembler.EmitJsr(target.Definition.ImportName!, external: true);
			_loadedPlatformBase = null;
			if (target.Definition.ImportAbi is { } registerAbi &&
				!target.Signature.ReturnType.IsVoid)
			{
				EmitMoveRegisterToD0(registerAbi.ReturnRegister);
			}
		}
		else
		{
			var internalAbi = GetInternalRegisterAbi(target.Definition);
			if (internalAbi is not null)
			{
				EmitLoadInternalArguments(internalAbi);
				EmitDiscardStackArguments(internalAbi.Count);
			}
			_assembler.EmitJsr(MethodLabel(target.Definition), external: false);
			_loadedPlatformBase = null;
		}

		if (target.Definition.IsImport || GetInternalRegisterAbi(target.Definition) is null)
		{
			EmitDiscardStackArguments(target.Definition.ParameterCount);
		}
		if (!target.Signature.ReturnType.IsVoid)
		{
			if (!target.Definition.IsImport &&
				target.Definition.Signature.ReturnType.IsReference)
			{
				_assembler.EmitWord(0x2F08); // MOVE.L A0,-(A7)
			}
			else
			{
				EmitPushD0();
			}
		}
	}

	private void EmitCStringFromLiteral(CilMethod caller, CilInstruction instruction)
	{
		var index = -1;
		for (var candidate = 0; candidate < caller.Instructions.Count; candidate++)
		{
			if (caller.Instructions[candidate].Offset == instruction.Offset)
			{
				index = candidate;
				break;
			}
		}

		if (index <= 0 ||
			caller.Instructions[index - 1] is not { OpCode: var previousOp, Operand: int token } ||
			previousOp != OpCodes.Ldstr)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"CString.FromLiteral requires a string literal argument.",
				caller.DisplayName,
				instruction.Offset);
		}

		_cStringLiterals.TryAdd(token, _module.GetUserString(token));
		EmitDiscardStackArguments(1);
		_assembler.EmitWord(0x2F3C); // MOVE.L #cstring,-(A7)
		_assembler.EmitAddress(CStringLabel(token));
	}

	private void EmitMarkManagedRoots(CilMethod method)
	{
		for (var index = 0; index < _currentStackTypes.Length; index++)
		{
			if (_currentStackTypes[index] != CilStackValueKind.Reference)
			{
				continue;
			}

			var offsetFromTop = checked((_currentStackTypes.Length - 1 - index) * 4);
			EmitPushFrameSlot(checked((short)offsetFromTop));
			_assembler.EmitJsr(RuntimeMarkLabel, external: false);
			_loadedPlatformBase = null;
			EmitDiscardStackArguments(1);
		}

		for (var index = 0; index < method.ParameterCount; index++)
		{
			if (!IsReferenceParameter(method, index))
			{
				continue;
			}

			EmitPushFrameSlot(FrameDisplacement(
				ArgumentOffset(method, index),
				_currentStackDepth));
			_assembler.EmitJsr(RuntimeMarkLabel, external: false);
			_loadedPlatformBase = null;
			EmitDiscardStackArguments(1);
		}

		for (var index = 0; index < method.Locals.Length; index++)
		{
			if (!method.Locals[index].IsReference)
			{
				continue;
			}

			EmitPushFrameSlot(FrameDisplacement(
				LocalOffset(method, index),
				_currentStackDepth));
			_assembler.EmitJsr(RuntimeMarkLabel, external: false);
			_loadedPlatformBase = null;
			EmitDiscardStackArguments(1);
		}

		foreach (var field in _staticFields.Values
			.Where(static field => field.IsStatic && field.Type.IsReference)
			.OrderBy(static field =>
				System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(field.Handle)))
		{
			EmitLoadD0FromLabel(StaticFieldLabel(field.Handle));
			EmitPushD0();
			_assembler.EmitJsr(RuntimeMarkLabel, external: false);
			_loadedPlatformBase = null;
			EmitDiscardStackArguments(1);
		}
	}

	private static bool IsReferenceParameter(CilMethod method, int index)
	{
		if (method.Signature.Header.IsInstance)
		{
			if (index == 0)
			{
				return true;
			}

			index--;
		}

		return method.Signature.ParameterTypes[index].IsReference;
	}

	private void EmitExternalCall(CilMethod method, CilExternalCall call)
	{
		var binding = call.Convention;
		EmitEnsurePlatformBase(binding, method);
		var cacheRegister = binding.CacheRegister;
		var preservePlatformCache = cacheRegister is not null &&
			(call.Abi.ReturnRegister == cacheRegister ||
			 call.Abi.ParameterRegisters.Contains(cacheRegister.Value));
		if (preservePlatformCache)
		{
			EmitPushRegister(cacheRegister!.Value);
		}
		for (var index = 0; index < call.Abi.ParameterRegisters.Count; index++)
		{
			var stackOffset = checked(
				(call.Abi.ParameterRegisters.Count - 1 - index) * 4 +
				(preservePlatformCache ? 4 : 0));
			EmitLoadRegisterFromStack(call.Abi.ParameterRegisters[index], stackOffset);
		}

		EmitBaseRelativeJsr(binding.BaseRegister, binding.Displacement);
		if (!method.Signature.ReturnType.IsVoid)
		{
			EmitMoveRegisterToD0(call.Abi.ReturnRegister);
		}
		if (preservePlatformCache)
		{
			EmitPopRegister(cacheRegister!.Value);
		}
	}

	private void EmitEnsurePlatformBase(
		M68kExternalCallConvention binding,
		CilMethod method)
	{
		var platformBase = GetOrAddPlatformBase(binding, method);
		if (_loadedPlatformBase != platformBase)
		{
			switch (binding.BaseSource)
			{
				case M68kExternalBaseSource.CachedPointer:
					EmitMoveRegister(
						binding.CacheRegister ??
							throw new M68kCompilationException(
								M68kDiagnosticIds.InvalidMetadata,
								"Cached platform bases require a cache register.",
								method.DisplayName),
						binding.BaseRegister);
					break;
				case M68kExternalBaseSource.WritableSlot:
					EmitLoadAddressRegisterAbsolute(binding.BaseRegister, platformBase.Label!);
					break;
				case M68kExternalBaseSource.Immediate:
					EmitLoadAddressRegisterImmediate(binding.BaseRegister, binding.InitialValue);
					break;
				default:
					throw new InvalidOperationException(
						$"Unknown platform base source {binding.BaseSource}.");
			}
			_loadedPlatformBase = platformBase;
		}
	}

	private GeneratedPlatformBase GetOrAddPlatformBase(
		M68kExternalCallConvention binding,
		CilMethod method)
	{
		if (_usedPlatformBases.TryGetValue(binding.Identity, out var existing))
		{
			if (existing.Binding != binding)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.InvalidMetadata,
					$"Platform base '{binding.Identity}' has conflicting declarations.",
					method.DisplayName);
			}
			return existing;
		}

		if (binding.BaseSource == M68kExternalBaseSource.WritableSlot &&
			_request.OutputFormat == M68kOutputFormat.KickstartRom)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				$"Writable platform base storage for '{binding.Identity}' would be placed in read-only ROM.",
				method.DisplayName);
		}

		if (binding.BaseRegister < M68kRegister.A0 ||
			binding.CacheRegister is { } cache && cache < M68kRegister.A0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Platform base and cache registers must be address registers.",
				method.DisplayName);
		}

		var generated = new GeneratedPlatformBase(
			binding,
			binding.BaseSource == M68kExternalBaseSource.WritableSlot
				? binding.SlotSymbol
				: null);
		if (binding.BaseSource == M68kExternalBaseSource.WritableSlot &&
			string.IsNullOrWhiteSpace(binding.SlotSymbol))
		{
			throw new M68kCompilationException(
						M68kDiagnosticIds.InvalidMetadata,
						"Writable platform bases require a slot symbol.",
						method.DisplayName);
		}
		_usedPlatformBases.Add(binding.Identity, generated);
		return generated;
	}

	private bool TryEmitTailCall(CilMethod caller, CilInstruction instruction)
	{
		var target = _module.ResolveMethodToken((int)instruction.Operand!, caller, instruction.Offset);
		if (target.Definition is not { IsImport: false } callee ||
			GetInternalRegisterAbi(callee) is not { } registerAbi ||
			callee.Signature.ReturnType.Kind == CilTypeKind.GenericParameter ||
			caller.Signature.ReturnType.IsVoid != target.Signature.ReturnType.IsVoid ||
			(!caller.Signature.ReturnType.IsVoid &&
			 caller.Signature.ReturnType.IsReference != target.Signature.ReturnType.IsReference))
		{
			return false;
		}

		ValidateMethodSignature(callee, isEntry: false);
		EmitLoadInternalArguments(registerAbi);
		EmitDiscardStackArguments(registerAbi.Count);
		EmitFrameTeardown(caller);
		_assembler.EmitJmp(MethodLabel(callee), external: false);
		return true;
	}

	private void EmitFrameTeardown(CilMethod method)
	{
		var frameBytes = checked(
			(method.Locals.Length + (GetInternalRegisterAbi(method)?.Count ?? 0)) * 4);
		EmitReleaseFrame(frameBytes);
	}

	private void EmitLoadRegisterFromStack(M68kRegister register, int offset)
	{
		if (offset > short.MaxValue)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Register-ABI import arguments exceed the indexed stack displacement range.");
		}

		if (register <= M68kRegister.D7)
		{
			// MOVE.L d16(A7),Dn
			_assembler.EmitWord((ushort)(0x202F | ((int)register << 9)));
		}
		else
		{
			// MOVEA.L d16(A7),An
			var addressRegister = (int)register - (int)M68kRegister.A0;
			_assembler.EmitWord((ushort)(0x206F | (addressRegister << 9)));
		}

		_assembler.EmitWord((ushort)offset);
	}

	private void EmitLoadInternalArguments(IReadOnlyList<M68kRegister> registers)
	{
		for (var index = 0; index < registers.Count; index++)
		{
			EmitLoadRegisterFromStack(
				registers[index],
				checked((registers.Count - 1 - index) * 4));
		}
	}

	private void EmitStoreRegisterToFrame(M68kRegister register, short displacement)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2F40 | (int)register));
		}
		else
		{
			var addressRegister = (int)register - (int)M68kRegister.A0;
			_assembler.EmitWord((ushort)(0x2F48 | addressRegister));
		}
		_assembler.EmitWord((ushort)displacement);
	}

	private static IReadOnlyList<M68kRegister>? GetInternalRegisterAbi(CilMethod method)
	{
		var registers = new List<M68kRegister>(method.ParameterCount);
		var nextData = 0;
		var nextAddress = 0;
		if (method.Signature.Header.IsInstance)
		{
			registers.Add(M68kRegister.A0);
			nextAddress = 1;
		}

		foreach (var parameter in method.Signature.ParameterTypes)
		{
			if (parameter.Kind == CilTypeKind.GenericParameter)
			{
				return null;
			}

			if (parameter.IsReference)
			{
				if (nextAddress >= 2)
				{
					return null;
				}
				registers.Add((M68kRegister)((int)M68kRegister.A0 + nextAddress++));
			}
			else
			{
				if (nextData >= 2)
				{
					return null;
				}
				registers.Add((M68kRegister)((int)M68kRegister.D0 + nextData++));
			}
		}
		return registers;
	}

	private void EmitMoveRegisterToD0(M68kRegister register)
	{
		if (register == M68kRegister.D0)
		{
			return;
		}

		if (register <= M68kRegister.D7)
		{
			// MOVE.L Dn,D0
			_assembler.EmitWord((ushort)(0x2000 | (int)register));
			return;
		}

		// MOVE.L An,D0
		var addressRegister = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x2008 | addressRegister));
	}

	private void EmitNewObject(CilMethod caller, CilInstruction instruction)
	{
		EnsureManagedAllocationAllowed(caller, instruction, "object construction");
		var constructor = _module.ResolveMethodToken(
			(int)instruction.Operand!,
			caller,
			instruction.Offset).Definition;
		if (constructor is null)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Could not resolve object constructor.",
				caller.DisplayName,
				instruction.Offset);
		}

		if (!constructor.Signature.Header.IsInstance)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Object construction requires an instance constructor.",
				caller.DisplayName,
				instruction.Offset);
		}

		var constructorAbi = GetInternalRegisterAbi(constructor);
		if (constructorAbi is null)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"Object constructors with this signature exceed the private register ABI.",
				caller.DisplayName,
				instruction.Offset);
		}

		var layout = _module.GetTypeLayout(constructor.DeclaringType);
		_usedTypeLayouts.Add(layout.Handle);
		EmitPushConstant(layout.Size);
		EmitManagedAllocation(caller);

		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x20BC); // MOVE.L #descriptor,(A0)
		_assembler.EmitAddress(TypeDescriptorLabel(layout.Handle));
		if (layout.Size <= sbyte.MaxValue)
		{
			_assembler.EmitWord((ushort)(0x7200 | (byte)layout.Size)); // MOVEQ #size,D1
		}
		else
		{
			_assembler.EmitWord(0x223C); // MOVE.L #size,D1
			_assembler.EmitLong((uint)layout.Size);
		}
		_assembler.EmitWord(0x2141); // MOVE.L D1,4(A0)
		_assembler.EmitWord(0x0004);

		EmitMoveRegister(M68kRegister.D0, M68kRegister.A0);
		for (var index = 1; index < constructorAbi.Count; index++)
		{
			EmitLoadRegisterFromStack(
				constructorAbi[index],
				checked((constructorAbi.Count - 1 - index) * 4));
		}
		_assembler.EmitJsr(MethodLabel(constructor), external: false);
		_loadedPlatformBase = null;
		EmitDiscardStackArguments(constructor.Signature.ParameterTypes.Length);
		EmitPushRegister(M68kRegister.A0);
	}

	private void EmitNewArray(CilMethod method, CilInstruction instruction)
	{
		EnsureManagedAllocationAllowed(method, instruction, "array allocation");
		var elementType = _module.ResolveTypeToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (elementType.Size is not (1 or 2 or 4) ||
			(!elementType.IsSupportedScalar && !elementType.IsReference))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Arrays of '{elementType.DisplayName}' are not implemented; array elements must occupy one, two, or four bytes.",
				method.DisplayName,
				instruction.Offset);
		}

		_arrayTypes.TryAdd(elementType.DisplayName, elementType);
		_assembler.EmitWord(0x241F); // MOVE.L (A7)+,D2 length
		var lengthValid = UniqueLabel("array_length_valid");
		_assembler.EmitWord(0x4A82); // TST.L D2
		_assembler.EmitBranch(M68kCondition.Plus, lengthValid);
		_assembler.EmitWord(0x4AFC); // ILLEGAL
		_assembler.Mark(lengthValid);
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
		EmitScaleD0(elementType.Size);
		_assembler.EmitWord(0x0680); // ADDI.L #12,D0
		_assembler.EmitLong(12);
		EmitPushD0();
		EmitManagedAllocation(method);
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x20BC); // MOVE.L #descriptor,(A0)
		_assembler.EmitAddress(ArrayDescriptorLabel(elementType));
		_assembler.EmitWord(0x2202); // MOVE.L D2,D1
		EmitScaleD1(elementType.Size);
		_assembler.EmitWord(0x0681); // ADDI.L #12,D1
		_assembler.EmitLong(12);
		_assembler.EmitWord(0x2141); // MOVE.L D1,4(A0)
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2142); // MOVE.L D2,8(A0)
		_assembler.EmitWord(0x0008);
		EmitPushD0();
	}

	private void EnsureManagedAllocationAllowed(
		CilMethod method,
		CilInstruction instruction,
		string operation)
	{
		if (_memoryManagement != M68kMemoryManagement.None)
		{
			return;
		}

		throw new M68kCompilationException(
			M68kDiagnosticIds.UnsupportedInstruction,
			$"Managed {operation} requires a managed heap. Select ExternalAllocator, BumpAllocator, ManagedPoolMarkSweepGc, or ExecPoolMarkSweepGc memory management.",
			method.DisplayName,
			instruction.Offset);
	}

	private void EmitManagedAllocation(CilMethod method)
	{
		if (!UsesBuiltInManagedPool)
		{
			EmitRuntimeJsr(RuntimeAllocLabel, M68kRuntimeImports.Allocate);
			_loadedPlatformBase = null;
			EmitDiscardStackArguments(1);
			EmitRequireNonNull();
			return;
		}

		var strategy = M68kCompiler.GetEffectiveGcSweepStrategy(_request);
		if (strategy == M68kGcSweepStrategy.EveryAllocation)
		{
			EmitMarkManagedRoots(method);
			_assembler.EmitJsr(RuntimeCollectLabel, external: false);
			_loadedPlatformBase = null;
		}
		else if (strategy == M68kGcSweepStrategy.TelemetryTriggered)
		{
			EmitTelemetryTriggeredCollection(method);
		}

		_assembler.EmitJsr(RuntimeAllocLabel, external: false);
		_loadedPlatformBase = null;
		if (strategy == M68kGcSweepStrategy.OnAllocationFailure)
		{
			var done = UniqueLabel("gc_alloc_call_done");
			_assembler.EmitWord(0x4A80); // TST.L D0
			_assembler.EmitBranch(M68kCondition.NotEqual, done);
			EmitMarkManagedRoots(method);
			_assembler.EmitJsr(RuntimeCollectLabel, external: false);
			_loadedPlatformBase = null;
			_assembler.EmitJsr(RuntimeAllocLabel, external: false);
			_loadedPlatformBase = null;
			_assembler.Mark(done);
		}

		EmitDiscardStackArguments(1);
		EmitRequireNonNull();
	}

	private void EmitTelemetryTriggeredCollection(CilMethod method)
	{
		var checkBlocks = UniqueLabel("gc_telemetry_check_blocks");
		var collect = UniqueLabel("gc_telemetry_collect");
		var done = UniqueLabel("gc_telemetry_done");
		EmitLoadD0FromLabel(GcStaleBytesThresholdLabel);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, checkBlocks);
		EmitLoadD1FromLabel(GcStaleBytesLabel);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarryClear, collect);
		_assembler.Mark(checkBlocks);
		EmitLoadD0FromLabel(GcStaleBlocksThresholdLabel);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, done);
		EmitLoadD1FromLabel(GcStaleBlocksLabel);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarrySet, done);
		_assembler.Mark(collect);
		EmitMarkManagedRoots(method);
		_assembler.EmitJsr(RuntimeCollectLabel, external: false);
		_loadedPlatformBase = null;
		_assembler.Mark(done);
	}

	private void EmitArrayAccess(CilMethod method, CilInstruction instruction)
	{
		var op = instruction.OpCode;
		var access = GetArrayAccess(op);
		if (access.IsStore)
		{
			EmitPopD0(); // value
			_assembler.EmitWord(0x221F); // MOVE.L (A7)+,D1 index
			_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0 array
			EmitArrayBoundsCheck();
			EmitScaleD1(access.Size);
			_assembler.EmitWord(0xD1C1); // ADDA.L D1,A0
			EmitStoreD0ToA0Displacement(access.Size, 12);
			return;
		}

		_assembler.EmitWord(0x221F); // MOVE.L (A7)+,D1 index
		_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0 array
		EmitArrayBoundsCheck();
		EmitScaleD1(access.Size);
		_assembler.EmitWord(0xD1C1); // ADDA.L D1,A0
		if (op == OpCodes.Ldelema)
		{
			_assembler.EmitWord(0x41E8); // LEA 12(A0),A0
			_assembler.EmitWord(0x000C);
			_assembler.EmitWord(0x2F08); // MOVE.L A0,-(A7)
			return;
		}

		EmitLoadD0FromA0Displacement(access.Size, access.SignExtend, 12);
		EmitPushD0();
	}

	private void EmitIndirectLoad(OpCode op)
	{
		var access = GetIndirectAccess(op);
		_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0
		EmitNormalizeIndirectPointer();
		EmitLoadD0FromA0Displacement(access.Size, access.SignExtend, 12);
		EmitPushD0();
	}

	private void EmitIndirectStore(OpCode op)
	{
		var size = GetIndirectAccess(op).Size;
		EmitPopD0();
		_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0
		EmitNormalizeIndirectPointer();
		EmitStoreD0ToA0Displacement(size, 12);
	}

	private void EmitArrayBoundsCheck()
	{
		var arrayValid = UniqueLabel("array_nonnull");
		var indexNonNegative = UniqueLabel("array_index_nonnegative");
		var indexValid = UniqueLabel("array_index_valid");
		_assembler.EmitWord(0x2408); // MOVE.L A0,D2
		_assembler.EmitBranch(M68kCondition.NotEqual, arrayValid);
		_assembler.EmitWord(0x4AFC); // ILLEGAL
		_assembler.Mark(arrayValid);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.Plus, indexNonNegative);
		_assembler.EmitWord(0x4AFC); // ILLEGAL
		_assembler.Mark(indexNonNegative);
		_assembler.EmitWord(0x2428); // MOVE.L 8(A0),D2
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0xB282); // CMP.L D2,D1
		_assembler.EmitBranch(M68kCondition.CarrySet, indexValid);
		_assembler.EmitWord(0x4AFC); // ILLEGAL
		_assembler.Mark(indexValid);
	}

	private void EmitFieldAccess(CilMethod method, CilInstruction instruction)
	{
		var field = _module.ResolveFieldToken((int)instruction.Operand!, method, instruction.Offset);
		ValidateType(field.Type, method, "field");
		var op = instruction.OpCode;
		if (field.IsStatic)
		{
			_staticFields.TryAdd(field.Handle, field);
			var label = StaticFieldLabel(field.Handle);
			if (op == OpCodes.Ldsfld)
			{
				_assembler.EmitWord(0x2F39); // MOVE.L abs.l,-(A7)
				_assembler.EmitAddress(label);
				return;
			}

			if (op == OpCodes.Ldsflda)
			{
				_assembler.EmitWord(0x4879); // PEA abs.l
				_assembler.EmitAddress(label);
				return;
			}

			if (op == OpCodes.Stsfld)
			{
				_assembler.EmitWord(0x23DF); // MOVE.L (A7)+,abs.l
				_assembler.EmitAddress(label);
				return;
			}

			throw FieldMismatch(method, instruction, field);
		}

		var layout = _module.GetTypeLayout(field.DeclaringType);
		_usedTypeLayouts.Add(layout.Handle);
		var displacement = checked((short)layout.FieldOffsets[field.Handle]);
		if (op == OpCodes.Ldfld)
		{
			EmitPopD0();
			EmitRequireNonNull();
			_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
			_assembler.EmitWord(0x2028); // MOVE.L d16(A0),D0
			_assembler.EmitWord((ushort)displacement);
			EmitPushD0();
			return;
		}

		if (op == OpCodes.Ldflda)
		{
			EmitPopD0();
			EmitRequireNonNull();
			_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
			_assembler.EmitWord(0x41E8); // LEA d16(A0),A0
			_assembler.EmitWord((ushort)displacement);
			_assembler.EmitWord(0x2F08); // MOVE.L A0,-(A7)
			return;
		}

		if (op == OpCodes.Stfld)
		{
			EmitPopD0();
			_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0
			var valid = UniqueLabel("field_object_valid");
			_assembler.EmitWord(0x2208); // MOVE.L A0,D1 (and set condition codes)
			_assembler.EmitBranch(M68kCondition.NotEqual, valid);
			_assembler.EmitWord(0x4AFC); // ILLEGAL
			_assembler.Mark(valid);
			_assembler.EmitWord(0x2140); // MOVE.L D0,d16(A0)
			_assembler.EmitWord((ushort)displacement);
			return;
		}

		throw FieldMismatch(method, instruction, field);
	}

	private void EmitRequireNonNull()
	{
		var valid = UniqueLabel("nonnull");
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, valid);
		_assembler.EmitWord(0x4AFC); // ILLEGAL
		_assembler.Mark(valid);
	}

	private void EmitData()
	{
		_assembler.AlignWord();
		foreach (var platformBase in _usedPlatformBases.Values
			.Where(item => item.Binding.BaseSource == M68kExternalBaseSource.WritableSlot)
			.OrderBy(item => item.Binding.Identity, StringComparer.Ordinal))
		{
			_assembler.Mark(platformBase.Label!);
			_assembler.EmitLong(platformBase.Binding.InitialValue);
		}

		foreach (var field in _staticFields.Values.OrderBy(item =>
			System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(item.Handle)))
		{
			_assembler.Mark(StaticFieldLabel(field.Handle));
			_assembler.EmitLong(0);
		}

		foreach (var handle in _usedTypeLayouts.OrderBy(handle =>
			System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle)))
		{
			var layout = _module.GetTypeLayout(handle);
			_assembler.Mark(TypeDescriptorLabel(handle));
			_assembler.EmitLong((uint)layout.Size);
			_assembler.EmitLong(layout.ReferenceBitmap);
		}

		if (_stringLiterals.Count != 0)
		{
			_assembler.Mark("runtime:string-descriptor");
			_assembler.EmitLong(0); // Variable-size object.
			_assembler.EmitLong(0);
		}

		foreach (var item in _stringLiterals.OrderBy(item => item.Key))
		{
			_assembler.AlignWord();
			_assembler.Mark(StringLabel(item.Key));
			_assembler.EmitAddress("runtime:string-descriptor");
			var size = checked(12 + ((item.Value.Length + 1) * 2));
			_assembler.EmitLong((uint)size);
			_assembler.EmitLong((uint)item.Value.Length);
			foreach (var character in item.Value)
			{
				_assembler.EmitWord(character);
			}
			_assembler.EmitWord(0);
		}

		foreach (var item in _cStringLiterals.OrderBy(item => item.Key))
		{
			_assembler.AlignWord();
			_assembler.Mark(CStringLabel(item.Key));
			foreach (var character in item.Value)
			{
				if (character > byte.MaxValue)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedInstruction,
						$"CString literal '{item.Value}' contains a non-8-bit character.");
				}
				_assembler.EmitByte((byte)character);
			}
			_assembler.EmitByte(0);
		}

		foreach (var type in _arrayTypes.Values.OrderBy(item => item.DisplayName, StringComparer.Ordinal))
		{
			_assembler.AlignWord();
			_assembler.Mark(ArrayDescriptorLabel(type));
			_assembler.EmitLong(0); // Variable size.
			_assembler.EmitLong(type.IsReference ? 1u : 0u);
		}

		if (M68kCompiler.IsManagedRuntime(_request))
		{
			EmitGcConfigData();
		}
		if (UsesBuiltInManagedPool)
		{
			EmitManagedPoolRuntimeData();
		}
	}

	private void EmitGcConfigData()
	{
		_assembler.AlignWord();
		_assembler.Mark(GcConfigLabel);
		_assembler.EmitLong((uint)_memoryManagement);
		_assembler.EmitLong((uint)M68kCompiler.GetEffectiveGcSweepStrategy(_request));
		_assembler.EmitLong(_request.Heap.StartAddress);
		_assembler.EmitLong(_request.Heap.Size);
		_assembler.EmitLong(_request.GcTelemetry.StaleBytesThreshold);
		_assembler.EmitLong(_request.GcTelemetry.StaleBlocksThreshold);
		_assembler.EmitLong(_request.GcTelemetry.IntervalTicks);
	}

	private void EmitManagedPoolRuntimeData()
	{
		_assembler.AlignWord();
		_assembler.Mark(GcHeapStartLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcHeapEndLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcFreeHeadLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcAllocHeadLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcStaleBytesLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcStaleBlocksLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcStaleBytesThresholdLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcStaleBlocksThresholdLabel);
		_assembler.EmitLong(0);
	}

	private string EmitEntryAdapter(CilMethod entry, bool usesManagedRuntime)
	{
		const string label = "entry:managed";
		_assembler.AlignWord();
		_assembler.Mark(label);
		EmitCachePlatformBases();
		if (usesManagedRuntime)
		{
			_assembler.EmitWord(0x2F3C); // MOVE.L #gc-config,-(A7)
			_assembler.EmitAddress(GcConfigLabel);
			EmitRuntimeJsr(RuntimeInitLabel, M68kRuntimeImports.GcInit);
			_loadedPlatformBase = null;
			EmitDiscardStackArguments(1);
			EmitRequireNonNull();
			_assembler.EmitJsr(MethodLabel(entry), external: false);
			_loadedPlatformBase = null;
			EmitPushD0();
			EmitPushRegister(M68kRegister.A0);
			EmitRuntimeJsr(RuntimeShutdownLabel, M68kRuntimeImports.GcShutdown);
			_loadedPlatformBase = null;
			EmitPopRegister(M68kRegister.A0);
			EmitPopD0();
			_assembler.EmitWord(0x4E75); // RTS
			return label;
		}

		_assembler.EmitJmp(MethodLabel(entry), external: false);
		return label;
	}

	private void EmitRuntimeJsr(string internalLabel, string externalLabel)
	{
		_assembler.EmitJsr(
			UsesBuiltInManagedPool ? internalLabel : externalLabel,
			external: !UsesBuiltInManagedPool);
	}

	private void EmitManagedPoolRuntime()
	{
		if (!UsesBuiltInManagedPool)
		{
			return;
		}

		EmitManagedPoolInit();
		EmitManagedPoolAlloc();
		EmitManagedPoolDispose();
		EmitManagedPoolMark();
		EmitManagedPoolCollect();
		EmitManagedPoolCoalesce();
		EmitManagedPoolTelemetryGetters();
		EmitManagedPoolShutdown();
	}

	private void EmitManagedPoolInit()
	{
		var fail = UniqueLabel("gc_init_fail");
		var done = UniqueLabel("gc_init_done");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeInitLabel);
		_assembler.EmitWord(0x206F); // MOVEA.L 4(A7),A0 config
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2028); // MOVE.L 8(A0),D0 heap start
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, fail);
		EmitStoreD0ToLabel(GcHeapStartLabel);
		EmitStoreD0ToLabel(GcFreeHeadLabel);
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0 first block
		_assembler.EmitWord(0x206F); // MOVEA.L 4(A7),A0 config
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2228); // MOVE.L 12(A0),D1 heap size
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.Equal, fail);
		EmitLoadA0FromLabel(GcHeapStartLabel);
		_assembler.EmitWord(0x2008); // MOVE.L A0,D0
		_assembler.EmitWord(0xD081); // ADD.L D1,D0
		EmitStoreD0ToLabel(GcHeapEndLabel);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		EmitStoreD0ToA0Displacement(4, 0); // next
		EmitStoreD0ToA0Displacement(4, 4); // prev
		_assembler.EmitWord(0x2001); // MOVE.L D1,D0
		EmitStoreD0ToA0Displacement(4, 8); // size
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		EmitStoreD0ToA0Displacement(4, 12); // flags
		EmitStoreD0ToLabel(GcAllocHeadLabel);
		EmitStoreD0ToLabel(GcStaleBytesLabel);
		EmitStoreD0ToLabel(GcStaleBlocksLabel);
		_assembler.EmitWord(0x206F); // MOVEA.L 4(A7),A0 config
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2028); // MOVE.L 16(A0),D0 stale bytes threshold
		_assembler.EmitWord(0x0010);
		EmitStoreD0ToLabel(GcStaleBytesThresholdLabel);
		_assembler.EmitWord(0x2028); // MOVE.L 20(A0),D0 stale blocks threshold
		_assembler.EmitWord(0x0014);
		EmitStoreD0ToLabel(GcStaleBlocksThresholdLabel);
		_assembler.EmitWord(0x7001); // MOVEQ #1,D0
		_assembler.EmitBranch(M68kCondition.True, done);
		_assembler.Mark(fail);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.Mark(done);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolAlloc()
	{
		var loop = UniqueLabel("gc_alloc_loop");
		var found = UniqueLabel("gc_alloc_found");
		var noSplit = UniqueLabel("gc_alloc_no_split");
		var zeroLoop = UniqueLabel("gc_alloc_zero_loop");
		var zeroDone = UniqueLabel("gc_alloc_zero_done");
		var fail = UniqueLabel("gc_alloc_fail");
		var returnLabel = UniqueLabel("gc_alloc_return");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeAllocLabel);
		_assembler.EmitWord(0x202F); // MOVE.L 4(A7),D0 requested payload size
		_assembler.EmitWord(0x0004);
		EmitPushRegister(M68kRegister.D2);
		EmitPushRegister(M68kRegister.D3);
		EmitPushRegister(M68kRegister.D4);
		EmitPushRegister(M68kRegister.A2);
		_assembler.EmitWord(0x0680); // ADDI.L #3,D0
		_assembler.EmitLong(3);
		_assembler.EmitWord(0x0280); // ANDI.L #~3,D0
		_assembler.EmitLong(0xFFFF_FFFCu);
		_assembler.EmitWord(0x0680); // ADDI.L #header,D0 total size
		_assembler.EmitLong(GcBlockHeaderSize);
		_assembler.EmitWord(0x2800); // MOVE.L D0,D4 requested total size
		EmitLoadA0FromLabel(GcFreeHeadLabel);
		_assembler.Mark(loop);
		_assembler.EmitWord(0x2208); // MOVE.L A0,D1
		_assembler.EmitBranch(M68kCondition.Equal, fail);
		_assembler.EmitWord(0x2228); // MOVE.L 8(A0),D1 block size
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarryClear, found);
		_assembler.EmitWord(0x2050); // MOVEA.L (A0),A0 next
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(found);
		_assembler.EmitWord(0x9280); // SUB.L D0,D1 remainder
		_assembler.EmitWord(0x0C81); // CMPI.L #min split,D1
		_assembler.EmitLong(GcMinimumSplitSize);
		_assembler.EmitBranch(M68kCondition.CarrySet, noSplit);
		EmitManagedPoolSplitFreeBlock();
		_assembler.EmitBranch(M68kCondition.True, zeroDone);
		_assembler.Mark(noSplit);
		EmitManagedPoolUnlinkFreeBlock();
		_assembler.EmitWord(0x2028); // MOVE.L 8(A0),D0 total size
		_assembler.EmitWord(0x0008);
		_assembler.Mark(zeroDone);
		EmitManagedPoolLinkAllocatedBlock();
		_assembler.EmitWord(0x2800); // MOVE.L D0,D4 actual total size
		EmitManagedPoolRecordAllocation();
		_assembler.EmitWord(0x2248); // MOVEA.L A0,A1
		_assembler.EmitWord(0x43E9); // LEA 16(A1),A1 payload
		_assembler.EmitWord(GcBlockHeaderSize);
		_assembler.EmitWord(0x2409); // MOVE.L A1,D2 return payload
		_assembler.EmitWord(0x2204); // MOVE.L D4,D1 total size
		_assembler.EmitWord(0x0681); // ADDI.L #-header,D1 payload bytes
		_assembler.EmitLong(unchecked((uint)-GcBlockHeaderSize));
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.Mark(zeroLoop);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.Equal, zeroDone + ":return");
		_assembler.EmitWord(0x22C0); // MOVE.L D0,(A1)+
		_assembler.EmitWord(0x5981); // SUBQ.L #4,D1
		_assembler.EmitBranch(M68kCondition.True, zeroLoop);
		_assembler.Mark(zeroDone + ":return");
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
		_assembler.EmitBranch(M68kCondition.True, returnLabel);
		_assembler.Mark(fail);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.Mark(returnLabel);
		EmitPopRegister(M68kRegister.A2);
		EmitPopRegister(M68kRegister.D4);
		EmitPopRegister(M68kRegister.D3);
		EmitPopRegister(M68kRegister.D2);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolRecordAllocation()
	{
		EmitLoadD0FromLabel(GcStaleBytesLabel);
		_assembler.EmitWord(0xD084); // ADD.L D4,D0
		EmitStoreD0ToLabel(GcStaleBytesLabel);
		EmitLoadD0FromLabel(GcStaleBlocksLabel);
		_assembler.EmitWord(0x5280); // ADDQ.L #1,D0
		EmitStoreD0ToLabel(GcStaleBlocksLabel);
	}

	private void EmitManagedPoolSplitFreeBlock()
	{
		var nextDone = UniqueLabel("gc_split_next_done");
		var prevPresent = UniqueLabel("gc_split_prev_present");
		var prevDone = UniqueLabel("gc_split_prev_done");
		_assembler.EmitWord(0x2248); // MOVEA.L A0,A1 new free block base
		_assembler.EmitWord(0xD3C0); // ADDA.L D0,A1
		_assembler.EmitWord(0x2428); // MOVE.L (A0),D2 old next
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2342); // MOVE.L D2,(A1)
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2628); // MOVE.L 4(A0),D3 old prev
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2343); // MOVE.L D3,4(A1)
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2341); // MOVE.L D1,8(A1) remainder size
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0x7400); // MOVEQ #0,D2
		_assembler.EmitWord(0x2342); // MOVE.L D2,12(A1) free flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A82); // TST.L D2 old next? D2 is zero now; reload
		_assembler.EmitWord(0x2429); // MOVE.L (A1),D2
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x4A82); // TST.L D2
		_assembler.EmitBranch(M68kCondition.Equal, nextDone);
		_assembler.EmitWord(0x2442); // MOVEA.L D2,A2
		_assembler.EmitWord(0x2549); // MOVE.L A1,4(A2)
		_assembler.EmitWord(0x0004);
		_assembler.Mark(nextDone);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.NotEqual, prevPresent);
		_assembler.EmitWord(0x2009); // MOVE.L A1,D0
		EmitStoreD0ToLabel(GcFreeHeadLabel);
		_assembler.EmitBranch(M68kCondition.True, prevDone);
		_assembler.Mark(prevPresent);
		_assembler.EmitWord(0x2443); // MOVEA.L D3,A2
		_assembler.EmitWord(0x2549); // MOVE.L A1,(A2)
		_assembler.EmitWord(0x0000);
		_assembler.Mark(prevDone);
		_assembler.EmitWord(0x2004); // MOVE.L D4,D0 allocated size
		_assembler.EmitWord(0x2140); // MOVE.L D0,8(A0) allocated size
		_assembler.EmitWord(0x0008);
	}

	private void EmitManagedPoolUnlinkFreeBlock()
	{
		var prevPresent = UniqueLabel("gc_unlink_prev_present");
		var prevDone = UniqueLabel("gc_unlink_prev_done");
		var nextDone = UniqueLabel("gc_unlink_next_done");
		_assembler.EmitWord(0x2428); // MOVE.L (A0),D2 next
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2628); // MOVE.L 4(A0),D3 prev
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.NotEqual, prevPresent);
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
		EmitStoreD0ToLabel(GcFreeHeadLabel);
		_assembler.EmitBranch(M68kCondition.True, prevDone);
		_assembler.Mark(prevPresent);
		_assembler.EmitWord(0x2243); // MOVEA.L D3,A1
		_assembler.EmitWord(0x2342); // MOVE.L D2,(A1)
		_assembler.EmitWord(0x0000);
		_assembler.Mark(prevDone);
		_assembler.EmitWord(0x4A82); // TST.L D2
		_assembler.EmitBranch(M68kCondition.Equal, nextDone);
		_assembler.EmitWord(0x2242); // MOVEA.L D2,A1
		_assembler.EmitWord(0x2343); // MOVE.L D3,4(A1)
		_assembler.EmitWord(0x0004);
		_assembler.Mark(nextDone);
	}

	private void EmitManagedPoolLinkAllocatedBlock()
	{
		var oldDone = UniqueLabel("gc_link_alloc_old_done");
		EmitLoadD0FromLabel(GcAllocHeadLabel);
		EmitStoreD0ToA0Displacement(4, 0);
		_assembler.EmitWord(0x7200); // MOVEQ #0,D1
		_assembler.EmitWord(0x2141); // MOVE.L D1,4(A0)
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x7201); // MOVEQ #1,D1
		_assembler.EmitWord(0x2141); // MOVE.L D1,12(A0)
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A80); // TST.L D0 old head
		_assembler.EmitBranch(M68kCondition.Equal, oldDone);
		_assembler.EmitWord(0x2240); // MOVEA.L D0,A1
		_assembler.EmitWord(0x2348); // MOVE.L A0,4(A1)
		_assembler.EmitWord(0x0004);
		_assembler.Mark(oldDone);
		_assembler.EmitWord(0x2008); // MOVE.L A0,D0
		EmitStoreD0ToLabel(GcAllocHeadLabel);
		_assembler.EmitWord(0x2028); // MOVE.L 8(A0),D0 return total size
		_assembler.EmitWord(0x0008);
	}

	private void EmitManagedPoolDispose()
	{
		var done = UniqueLabel("gc_dispose_done");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeDisposeLabel);
		_assembler.EmitWord(0x206F); // MOVEA.L 4(A7),A0 slot
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2010); // MOVE.L (A0),D0 payload
		_assembler.EmitBranch(M68kCondition.Equal, done);
		EmitPushRegister(M68kRegister.D2);
		EmitPushRegister(M68kRegister.D3);
		_assembler.EmitWord(0x7200); // MOVEQ #0,D1
		_assembler.EmitWord(0x2081); // MOVE.L D1,(A0) clear slot
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0 payload
		_assembler.EmitWord(0x41E8); // LEA -16(A0),A0 header
		_assembler.EmitWord(unchecked((ushort)-GcBlockHeaderSize));
		EmitManagedPoolUnlinkAllocatedBlock();
		EmitManagedPoolLinkFreeBlock();
		EmitPopRegister(M68kRegister.D3);
		EmitPopRegister(M68kRegister.D2);
		_assembler.Mark(done);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolUnlinkAllocatedBlock()
	{
		var prevPresent = UniqueLabel("gc_unlink_alloc_prev_present");
		var prevDone = UniqueLabel("gc_unlink_alloc_prev_done");
		var nextDone = UniqueLabel("gc_unlink_alloc_next_done");
		_assembler.EmitWord(0x2428); // MOVE.L (A0),D2 next
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2628); // MOVE.L 4(A0),D3 prev
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.NotEqual, prevPresent);
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
		EmitStoreD0ToLabel(GcAllocHeadLabel);
		_assembler.EmitBranch(M68kCondition.True, prevDone);
		_assembler.Mark(prevPresent);
		_assembler.EmitWord(0x2243); // MOVEA.L D3,A1
		_assembler.EmitWord(0x2342); // MOVE.L D2,(A1)
		_assembler.EmitWord(0x0000);
		_assembler.Mark(prevDone);
		_assembler.EmitWord(0x4A82); // TST.L D2
		_assembler.EmitBranch(M68kCondition.Equal, nextDone);
		_assembler.EmitWord(0x2242); // MOVEA.L D2,A1
		_assembler.EmitWord(0x2343); // MOVE.L D3,4(A1)
		_assembler.EmitWord(0x0004);
		_assembler.Mark(nextDone);
	}

	private void EmitManagedPoolLinkFreeBlock()
	{
		var store = UniqueLabel("gc_link_free_store");
		EmitLoadD0FromLabel(GcFreeHeadLabel);
		EmitStoreD0ToA0Displacement(4, 0);
		_assembler.EmitWord(0x7200); // MOVEQ #0,D1
		_assembler.EmitWord(0x2141); // MOVE.L D1,4(A0)
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2141); // MOVE.L D1,12(A0)
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A80); // TST.L D0 old free head
		_assembler.EmitBranch(M68kCondition.Equal, store);
		_assembler.EmitWord(0x2240); // MOVEA.L D0,A1
		_assembler.EmitWord(0x2348); // MOVE.L A0,4(A1)
		_assembler.EmitWord(0x0004);
		_assembler.Mark(store);
		_assembler.EmitWord(0x2008); // MOVE.L A0,D0
		EmitStoreD0ToLabel(GcFreeHeadLabel);
	}

	private void EmitManagedPoolMark()
	{
		var done = UniqueLabel("gc_mark_done");
		var alreadyMarked = UniqueLabel("gc_mark_already");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeMarkLabel);
		_assembler.EmitWord(0x202F); // MOVE.L 4(A7),D0 payload
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, done);
		_assembler.EmitWord(0x2200); // MOVE.L D0,D1 payload
		EmitLoadD0FromLabel(GcHeapStartLabel);
		_assembler.EmitWord(0x0680); // ADDI.L #header,D0 first payload
		_assembler.EmitLong(GcBlockHeaderSize);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarrySet, done);
		EmitLoadD0FromLabel(GcHeapEndLabel);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarryClear, done);
		_assembler.EmitWord(0x2001); // MOVE.L D1,D0 payload
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0 payload
		_assembler.EmitWord(0x41E8); // LEA -16(A0),A0 header
		_assembler.EmitWord(unchecked((ushort)-GcBlockHeaderSize));
		_assembler.EmitWord(0x2228); // MOVE.L 12(A0),D1 flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x2001); // MOVE.L D1,D0
		_assembler.EmitWord(0x0280); // ANDI.L #mark,D0
		_assembler.EmitLong(GcMarkFlag);
		_assembler.EmitBranch(M68kCondition.NotEqual, alreadyMarked);
		_assembler.EmitWord(0x0081); // ORI.L #mark,D1
		_assembler.EmitLong(GcMarkFlag);
		_assembler.EmitWord(0x2141); // MOVE.L D1,12(A0)
		_assembler.EmitWord(0x000C);
		_assembler.Mark(alreadyMarked);
		_assembler.Mark(done);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolCollect()
	{
		var traceRestart = UniqueLabel("gc_trace_restart");
		var traceLoop = UniqueLabel("gc_trace_loop");
		var traceNext = UniqueLabel("gc_trace_next");
		var tracePassDone = UniqueLabel("gc_trace_pass_done");
		var traceArray = UniqueLabel("gc_trace_array");
		var traceFields = UniqueLabel("gc_trace_fields");
		var traceFieldSkip = UniqueLabel("gc_trace_field_skip");
		var traceArrayLoop = UniqueLabel("gc_trace_array_loop");
		var traceScanned = UniqueLabel("gc_trace_scanned");
		var loop = UniqueLabel("gc_sweep_loop");
		var live = UniqueLabel("gc_sweep_live");
		var next = UniqueLabel("gc_sweep_next");
		var done = UniqueLabel("gc_sweep_done");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeCollectLabel);
		EmitPushRegister(M68kRegister.D2);
		EmitPushRegister(M68kRegister.D3);
		EmitPushRegister(M68kRegister.D4);
		EmitPushRegister(M68kRegister.A2);
		_assembler.Mark(traceRestart);
		_assembler.EmitWord(0x7800); // MOVEQ #0,D4 pass scanned count
		EmitLoadA0FromLabel(GcAllocHeadLabel);
		_assembler.Mark(traceLoop);
		_assembler.EmitWord(0x2208); // MOVE.L A0,D1
		_assembler.EmitBranch(M68kCondition.Equal, tracePassDone);
		_assembler.EmitWord(0x2428); // MOVE.L (A0),D2 next allocated
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0 flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x2600); // MOVE.L D0,D3
		_assembler.EmitWord(0x0283); // ANDI.L #mark,D3
		_assembler.EmitLong(GcMarkFlag);
		_assembler.EmitBranch(M68kCondition.Equal, traceNext);
		_assembler.EmitWord(0x2600); // MOVE.L D0,D3
		_assembler.EmitWord(0x0283); // ANDI.L #scanned,D3
		_assembler.EmitLong(GcScanFlag);
		_assembler.EmitBranch(M68kCondition.NotEqual, traceNext);
		_assembler.EmitWord(0x0080); // ORI.L #scanned,D0
		_assembler.EmitLong(GcScanFlag);
		EmitStoreD0ToA0Displacement(4, 12);
		_assembler.EmitWord(0x7801); // MOVEQ #1,D4 another pass may be needed
		_assembler.EmitWord(0x41E8); // LEA 16(A0),A0 payload
		_assembler.EmitWord(GcBlockHeaderSize);
		_assembler.EmitWord(0x2250); // MOVEA.L (A0),A1 descriptor
		_assembler.EmitWord(0x2629); // MOVE.L (A1),D3 descriptor object size
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.Equal, traceArray);
		_assembler.EmitWord(0x2629); // MOVE.L 4(A1),D3 reference bitmap
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x45E8); // LEA 8(A0),A2 first object field
		_assembler.EmitWord(0x0008);
		_assembler.Mark(traceFields);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.Equal, traceScanned);
		_assembler.EmitWord(0x2003); // MOVE.L D3,D0
		_assembler.EmitWord(0x0280); // ANDI.L #1,D0
		_assembler.EmitLong(1);
		_assembler.EmitBranch(M68kCondition.Equal, traceFieldSkip);
		_assembler.EmitWord(0x202A); // MOVE.L (A2),D0 field reference
		_assembler.EmitWord(0x0000);
		EmitPushD0();
		_assembler.EmitJsr(RuntimeMarkLabel, external: false);
		_loadedPlatformBase = null;
		EmitDiscardStackArguments(1);
		_assembler.Mark(traceFieldSkip);
		_assembler.EmitWord(0x588A); // ADDQ.L #4,A2
		_assembler.EmitWord(0xE28B); // LSR.L #1,D3
		_assembler.EmitBranch(M68kCondition.True, traceFields);
		_assembler.Mark(traceArray);
		_assembler.EmitWord(0x2629); // MOVE.L 4(A1),D3 reference-array flag
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.Equal, traceScanned);
		_assembler.EmitWord(0x2628); // MOVE.L 8(A0),D3 array length
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0x45E8); // LEA 12(A0),A2 first array element
		_assembler.EmitWord(0x000C);
		_assembler.Mark(traceArrayLoop);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.Equal, traceScanned);
		_assembler.EmitWord(0x202A); // MOVE.L (A2),D0 element reference
		_assembler.EmitWord(0x0000);
		EmitPushD0();
		_assembler.EmitJsr(RuntimeMarkLabel, external: false);
		_loadedPlatformBase = null;
		EmitDiscardStackArguments(1);
		_assembler.EmitWord(0x588A); // ADDQ.L #4,A2
		_assembler.EmitWord(0x5383); // SUBQ.L #1,D3
		_assembler.EmitBranch(M68kCondition.True, traceArrayLoop);
		_assembler.Mark(traceScanned);
		_assembler.Mark(traceNext);
		_assembler.EmitWord(0x2042); // MOVEA.L D2,A0 next allocated
		_assembler.EmitBranch(M68kCondition.True, traceLoop);
		_assembler.Mark(tracePassDone);
		_assembler.EmitWord(0x4A84); // TST.L D4
		_assembler.EmitBranch(M68kCondition.NotEqual, traceRestart);
		EmitLoadA0FromLabel(GcAllocHeadLabel);
		_assembler.Mark(loop);
		_assembler.EmitWord(0x2208); // MOVE.L A0,D1
		_assembler.EmitBranch(M68kCondition.Equal, done);
		_assembler.EmitWord(0x2428); // MOVE.L (A0),D2 next allocated
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0 flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x0280); // ANDI.L #mark,D0
		_assembler.EmitLong(GcMarkFlag);
		_assembler.EmitBranch(M68kCondition.NotEqual, live);
		EmitManagedPoolUnlinkAllocatedBlock();
		EmitManagedPoolLinkFreeBlock();
		_assembler.EmitBranch(M68kCondition.True, next);
		_assembler.Mark(live);
		_assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0 flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x0280); // ANDI.L #~(mark|scanned),D0
		_assembler.EmitLong(~(GcMarkFlag | GcScanFlag));
		EmitStoreD0ToA0Displacement(4, 12);
		_assembler.Mark(next);
		_assembler.EmitWord(0x2042); // MOVEA.L D2,A0 next allocated
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(done);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		EmitStoreD0ToLabel(GcStaleBytesLabel);
		EmitStoreD0ToLabel(GcStaleBlocksLabel);
		EmitPopRegister(M68kRegister.A2);
		EmitPopRegister(M68kRegister.D4);
		EmitPopRegister(M68kRegister.D3);
		EmitPopRegister(M68kRegister.D2);
		_assembler.EmitJsr(RuntimeCoalesceLabel, external: false);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolCoalesce()
	{
		var loop = UniqueLabel("gc_collect_loop");
		var advance = UniqueLabel("gc_collect_advance");
		var done = UniqueLabel("gc_collect_done");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeCoalesceLabel);
		EmitPushRegister(M68kRegister.D2);
		EmitPushRegister(M68kRegister.D3);
		EmitPushRegister(M68kRegister.D4);
		EmitPushRegister(M68kRegister.A2);
		EmitLoadA0FromLabel(GcHeapStartLabel);
		_assembler.Mark(loop);
		_assembler.EmitWord(0x2208); // MOVE.L A0,D1
		_assembler.EmitBranch(M68kCondition.Equal, done);
		EmitLoadD0FromLabel(GcHeapEndLabel);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarryClear, done);
		_assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0 flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, advance);
		_assembler.EmitWord(0x2228); // MOVE.L 8(A0),D1 current size
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0x2408); // MOVE.L A0,D2
		_assembler.EmitWord(0xD481); // ADD.L D1,D2 next physical block
		EmitLoadD0FromLabel(GcHeapEndLabel);
		_assembler.EmitWord(0x2602); // MOVE.L D2,D3
		_assembler.EmitWord(0xB680); // CMP.L D0,D3
		_assembler.EmitBranch(M68kCondition.CarryClear, advance);
		_assembler.EmitWord(0x2242); // MOVEA.L D2,A1
		_assembler.EmitWord(0x2029); // MOVE.L 12(A1),D0 next flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, advance);
		EmitMoveRegister(M68kRegister.A0, M68kRegister.A2);
		EmitMoveRegister(M68kRegister.A1, M68kRegister.D4);
		EmitMoveRegister(M68kRegister.A1, M68kRegister.A0);
		EmitManagedPoolUnlinkFreeBlock();
		EmitMoveRegister(M68kRegister.A2, M68kRegister.A0);
		EmitMoveRegister(M68kRegister.D4, M68kRegister.A1);
		_assembler.EmitWord(0x2228); // MOVE.L 8(A0),D1 current size
		_assembler.EmitWord(0x0008);
		EmitMoveRegister(M68kRegister.A1, M68kRegister.A0);
		_assembler.EmitWord(0x2428); // MOVE.L 8(A0),D2 next size
		_assembler.EmitWord(0x0008);
		EmitMoveRegister(M68kRegister.A2, M68kRegister.A0);
		_assembler.EmitWord(0x2001); // MOVE.L D1,D0
		_assembler.EmitWord(0xD082); // ADD.L D2,D0
		_assembler.EmitWord(0x2200); // MOVE.L D0,D1
		_assembler.EmitWord(0x2141); // MOVE.L D1,8(A0)
		_assembler.EmitWord(0x0008);
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(advance);
		_assembler.EmitWord(0x2228); // MOVE.L 8(A0),D1 current size
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0xD1C1); // ADDA.L D1,A0
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(done);
		EmitPopRegister(M68kRegister.A2);
		EmitPopRegister(M68kRegister.D4);
		EmitPopRegister(M68kRegister.D3);
		EmitPopRegister(M68kRegister.D2);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolTelemetryGetters()
	{
		_assembler.AlignWord();
		_assembler.Mark(RuntimeGetStaleBytesLabel);
		EmitLoadD0FromLabel(GcStaleBytesLabel);
		_assembler.EmitWord(0x4E75); // RTS
		_assembler.AlignWord();
		_assembler.Mark(RuntimeGetStaleBlocksLabel);
		EmitLoadD0FromLabel(GcStaleBlocksLabel);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolShutdown()
	{
		_assembler.AlignWord();
		_assembler.Mark(RuntimeShutdownLabel);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		EmitStoreD0ToLabel(GcFreeHeadLabel);
		EmitStoreD0ToLabel(GcAllocHeadLabel);
		EmitStoreD0ToLabel(GcHeapStartLabel);
		EmitStoreD0ToLabel(GcHeapEndLabel);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitExportAdapter(CilExport export, bool cachesPlatformBase)
	{
		_assembler.AlignWord();
		_assembler.Mark(ExportLabel(export.Name));
		for (var register = M68kRegister.D2; register <= M68kRegister.D7; register++)
		{
			EmitPushRegister(register);
		}
		for (var register = M68kRegister.A2; register <= M68kRegister.A6; register++)
		{
			EmitPushRegister(register);
		}

		foreach (var register in export.ParameterRegisters)
		{
			EmitPushRegister(register);
		}

		var internalAbi = GetInternalRegisterAbi(export.Method);
		if (internalAbi is not null)
		{
			EmitLoadInternalArguments(internalAbi);
			EmitDiscardStackArguments(internalAbi.Count);
		}
		if (cachesPlatformBase)
		{
			EmitCachePlatformBases();
		}
		_assembler.EmitJsr(MethodLabel(export.Method), external: false);
		if (internalAbi is null)
		{
			EmitDiscardStackArguments(export.ParameterRegisters.Count);
		}

		for (var register = M68kRegister.A6; register >= M68kRegister.A2; register--)
		{
			EmitPopRegister(register);
		}
		for (var register = M68kRegister.D7; register >= M68kRegister.D2; register--)
		{
			EmitPopRegister(register);
		}

		if (export.Method.Signature.ReturnType.IsReference)
		{
			EmitMoveRegister(M68kRegister.A0, export.ReturnRegister);
		}
		else
		{
			EmitMoveReturnFromD0(export.ReturnRegister);
		}
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitCachePlatformBases()
	{
		foreach (var platformBase in _usedPlatformBases.Values
			.Where(item => item.Binding.BaseSource == M68kExternalBaseSource.CachedPointer)
			.DistinctBy(item => (item.Binding.CacheRegister, item.Binding.SourceAddress)))
		{
			EmitLoadAddressRegisterFromMemory(
				platformBase.Binding.CacheRegister!.Value,
				platformBase.Binding.SourceAddress);
		}
	}

	private void EmitPushRegister(M68kRegister register)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2F00 | (int)register));
			return;
		}

		var addressRegister = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x2F08 | addressRegister));
	}

	private void EmitPopRegister(M68kRegister register)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x201F | ((int)register << 9)));
			return;
		}

		var addressRegister = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x205F | (addressRegister << 9)));
	}

	private void EmitMoveReturnFromD0(M68kRegister register)
	{
		if (register == M68kRegister.D0)
		{
			return;
		}

		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2000 | ((int)register << 9)));
			return;
		}

		var addressRegister = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x2040 | (addressRegister << 9)));
	}

	private void EmitMoveRegister(M68kRegister source, M68kRegister destination)
	{
		if (source == destination)
		{
			return;
		}
		var sourceIsAddress = source >= M68kRegister.A0;
		var sourceIndex = sourceIsAddress
			? (int)source - (int)M68kRegister.A0
			: (int)source;
		if (destination <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(
				0x2000 |
				((int)destination << 9) |
				(sourceIsAddress ? 0x0008 : 0) |
				sourceIndex));
		}
		else
		{
			var destinationIndex = (int)destination - (int)M68kRegister.A0;
			_assembler.EmitWord((ushort)(
				0x2040 |
				(destinationIndex << 9) |
				(sourceIsAddress ? 0x0008 : 0) |
				sourceIndex));
		}
	}

	private void EmitLoadAddressRegisterAbsolute(M68kRegister register, string label)
	{
		var index = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x2079 | (index << 9)));
		_assembler.EmitAddress(label);
	}

	private void EmitLoadD0FromLabel(string label)
	{
		_assembler.EmitWord(0x2039); // MOVE.L abs.l,D0
		_assembler.EmitAddress(label);
	}

	private void EmitLoadD1FromLabel(string label)
	{
		_assembler.EmitWord(0x2239); // MOVE.L abs.l,D1
		_assembler.EmitAddress(label);
	}

	private void EmitLoadA0FromLabel(string label)
	{
		_assembler.EmitWord(0x2079); // MOVEA.L abs.l,A0
		_assembler.EmitAddress(label);
	}

	private void EmitStoreD0ToLabel(string label)
	{
		EmitPushD0();
		_assembler.EmitWord(0x23DF); // MOVE.L (A7)+,abs.l
		_assembler.EmitAddress(label);
	}

	private void EmitLoadAddressRegisterImmediate(M68kRegister register, uint value)
	{
		var index = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x207C | (index << 9)));
		_assembler.EmitLong(value);
	}

	private void EmitImmediateToRegister(M68kRegister register, int value)
	{
		if (register <= M68kRegister.D7)
		{
			if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
			{
				_assembler.EmitWord((ushort)(
					0x7000 |
					((int)register << 9) |
					(byte)value));
				return;
			}
			_assembler.EmitWord((ushort)(0x203C | ((int)register << 9)));
			_assembler.EmitLong(unchecked((uint)value));
			return;
		}

		EmitLoadAddressRegisterImmediate(register, unchecked((uint)value));
	}

	private void EmitLoadAddressRegisterFromMemory(M68kRegister register, uint address)
	{
		var index = (int)register - (int)M68kRegister.A0;
		if (address <= short.MaxValue)
		{
			_assembler.EmitWord((ushort)(0x2078 | (index << 9)));
			_assembler.EmitWord((ushort)address);
			return;
		}
		_assembler.EmitWord((ushort)(0x2079 | (index << 9)));
		_assembler.EmitLong(address);
	}

	private void EmitBaseRelativeJsr(M68kRegister register, short displacement)
	{
		var index = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x4EA8 | index));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private static M68kCompilationException FieldMismatch(
		CilMethod method,
		CilInstruction instruction,
		CilField field) =>
		new(
			M68kDiagnosticIds.InvalidMetadata,
			$"Opcode '{instruction.OpCode.Name}' does not match field '{field.DisplayName}'.",
			method.DisplayName,
			instruction.Offset);

	private static bool IsArrayAccess(OpCode op) =>
		op == OpCodes.Ldelem_I1 ||
		op == OpCodes.Ldelem_U1 ||
		op == OpCodes.Ldelem_I2 ||
		op == OpCodes.Ldelem_U2 ||
		op == OpCodes.Ldelem_I4 ||
		op == OpCodes.Ldelem_U4 ||
		op == OpCodes.Ldelem_I ||
		op == OpCodes.Ldelem_Ref ||
		op == OpCodes.Ldelema ||
		op == OpCodes.Stelem_I1 ||
		op == OpCodes.Stelem_I2 ||
		op == OpCodes.Stelem_I4 ||
		op == OpCodes.Stelem_I ||
		op == OpCodes.Stelem_Ref;

	private static bool IsIndirectLoad(OpCode op) =>
		op == OpCodes.Ldind_I1 ||
		op == OpCodes.Ldind_U1 ||
		op == OpCodes.Ldind_I2 ||
		op == OpCodes.Ldind_U2 ||
		op == OpCodes.Ldind_I4 ||
		op == OpCodes.Ldind_U4 ||
		op == OpCodes.Ldind_I ||
		op == OpCodes.Ldind_Ref;

	private static bool IsIndirectStore(OpCode op) =>
		op == OpCodes.Stind_I1 ||
		op == OpCodes.Stind_I2 ||
		op == OpCodes.Stind_I4 ||
		op == OpCodes.Stind_I ||
		op == OpCodes.Stind_Ref;

	private static MemoryAccess GetArrayAccess(OpCode op) =>
		op.Value switch
		{
			var value when value == OpCodes.Ldelem_I1.Value => new(1, SignExtend: true, IsStore: false),
			var value when value == OpCodes.Ldelem_U1.Value => new(1, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Ldelem_I2.Value => new(2, SignExtend: true, IsStore: false),
			var value when value == OpCodes.Ldelem_U2.Value => new(2, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Ldelem_I4.Value ||
				value == OpCodes.Ldelem_U4.Value ||
				value == OpCodes.Ldelem_I.Value ||
				value == OpCodes.Ldelem_Ref.Value ||
				value == OpCodes.Ldelema.Value => new(4, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Stelem_I1.Value => new(1, SignExtend: false, IsStore: true),
			var value when value == OpCodes.Stelem_I2.Value => new(2, SignExtend: false, IsStore: true),
			var value when value == OpCodes.Stelem_I4.Value ||
				value == OpCodes.Stelem_I.Value ||
				value == OpCodes.Stelem_Ref.Value => new(4, SignExtend: false, IsStore: true),
			_ => throw new InvalidOperationException($"Unsupported array access opcode '{op.Name}'.")
		};

	private static MemoryAccess GetIndirectAccess(OpCode op) =>
		op.Value switch
		{
			var value when value == OpCodes.Ldind_I1.Value => new(1, SignExtend: true, IsStore: false),
			var value when value == OpCodes.Ldind_U1.Value => new(1, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Ldind_I2.Value => new(2, SignExtend: true, IsStore: false),
			var value when value == OpCodes.Ldind_U2.Value => new(2, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Ldind_I4.Value ||
				value == OpCodes.Ldind_U4.Value ||
				value == OpCodes.Ldind_I.Value ||
				value == OpCodes.Ldind_Ref.Value => new(4, SignExtend: false, IsStore: false),
			var value when value == OpCodes.Stind_I1.Value => new(1, SignExtend: false, IsStore: true),
			var value when value == OpCodes.Stind_I2.Value => new(2, SignExtend: false, IsStore: true),
			var value when value == OpCodes.Stind_I4.Value ||
				value == OpCodes.Stind_I.Value ||
				value == OpCodes.Stind_Ref.Value => new(4, SignExtend: false, IsStore: true),
			_ => throw new InvalidOperationException($"Unsupported indirect access opcode '{op.Name}'.")
		};

	private void EmitLoadD0FromA0Displacement(
		int size,
		bool signExtend,
		short displacement)
	{
		if (size is 1 or 2)
		{
			_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		}

		_assembler.EmitWord(size switch
		{
			1 => displacement == 0 ? (ushort)0x1010 : (ushort)0x1028, // MOVE.B (d16,A0),D0
			2 => displacement == 0 ? (ushort)0x3010 : (ushort)0x3028, // MOVE.W (d16,A0),D0
			4 => displacement == 0 ? (ushort)0x2010 : (ushort)0x2028, // MOVE.L (d16,A0),D0
			_ => throw new ArgumentOutOfRangeException(nameof(size))
		});
		if (displacement != 0)
		{
			_assembler.EmitWord(unchecked((ushort)displacement));
		}

		if (signExtend && size == 1)
		{
			EmitSignExtendD0FromBit(7, 0xFFFF_FF00);
		}
		else if (signExtend && size == 2)
		{
			EmitSignExtendD0FromBit(15, 0xFFFF_0000);
		}
	}

	private void EmitSignExtendD0FromBit(int bit, uint mask)
	{
		var done = UniqueLabel("sign_extend_done");
		_assembler.EmitWord(0x0800); // BTST #bit,D0
		_assembler.EmitWord((ushort)bit);
		_assembler.EmitBranch(M68kCondition.Equal, done);
		_assembler.EmitWord(0x0080); // ORI.L #mask,D0
		_assembler.EmitLong(mask);
		_assembler.Mark(done);
	}

	private void EmitNormalizeIndirectPointer()
	{
		_assembler.EmitWord(0x41E8); // LEA -12(A0),A0
		_assembler.EmitWord(unchecked((ushort)-12));
	}

	private void EmitStoreD0ToA0Displacement(int size, short displacement)
	{
		_assembler.EmitWord(size switch
		{
			1 => displacement == 0 ? (ushort)0x1080 : (ushort)0x1140, // MOVE.B D0,(d16,A0)
			2 => displacement == 0 ? (ushort)0x3080 : (ushort)0x3140, // MOVE.W D0,(d16,A0)
			4 => displacement == 0 ? (ushort)0x2080 : (ushort)0x2140, // MOVE.L D0,(d16,A0)
			_ => throw new ArgumentOutOfRangeException(nameof(size))
		});
		if (displacement != 0)
		{
			_assembler.EmitWord(unchecked((ushort)displacement));
		}
	}

	private void EmitScaleD0(int size)
	{
		for (var index = 1; index < size; index <<= 1)
		{
			_assembler.EmitWord(0xE388); // LSL.L #1,D0
		}
	}

	private void EmitScaleD1(int size)
	{
		for (var index = 1; index < size; index <<= 1)
		{
			_assembler.EmitWord(0xE389); // LSL.L #1,D1
		}
	}

	private bool TryEmitConversion(OpCode op)
	{
		if (op == OpCodes.Conv_I || op == OpCodes.Conv_U ||
			op == OpCodes.Conv_I4 || op == OpCodes.Conv_U4)
		{
			return true;
		}

		if (op != OpCodes.Conv_I1 && op != OpCodes.Conv_U1 &&
			op != OpCodes.Conv_I2 && op != OpCodes.Conv_U2)
		{
			return false;
		}

		EmitPopD0();
		if (op == OpCodes.Conv_I1)
		{
			_assembler.EmitWord(0x4880); // EXT.W D0
			_assembler.EmitWord(0x48C0); // EXT.L D0
		}
		else if (op == OpCodes.Conv_I2)
		{
			_assembler.EmitWord(0x48C0); // EXT.L D0
		}
		else
		{
			_assembler.EmitWord(0x0280); // ANDI.L #mask,D0
			_assembler.EmitLong(op == OpCodes.Conv_U1 ? 0xFFu : 0xFFFFu);
		}

		EmitPushD0();
		return true;
	}

	private void EmitPushConstant(int value)
	{
		if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
		{
			_assembler.EmitWord((ushort)(0x7000 | (byte)value)); // MOVEQ #value,D0
			EmitPushD0();
			return;
		}

		_assembler.EmitWord(0x2F3C); // MOVE.L #value,-(A7)
		_assembler.EmitLong(unchecked((uint)value));
	}

	private void EmitPushFrameSlot(short displacement)
	{
		_assembler.EmitWord(0x2F2F); // MOVE.L d16(A7),-(A7)
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitPopFrameSlot(short displacement)
	{
		_assembler.EmitWord(0x2F5F); // MOVE.L (A7)+,d16(A7)
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitClearFrameSlot(short displacement)
	{
		_assembler.EmitWord(0x42AF); // CLR.L d16(A7)
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitAllocateFrame(int bytes)
	{
		if (bytes == 0)
		{
			return;
		}
		if (bytes <= 8)
		{
			var encodedCount = bytes == 8 ? 0 : bytes;
			_assembler.EmitWord((ushort)(0x518F | (encodedCount << 9))); // SUBQ.L #bytes,A7
			return;
		}

		_assembler.EmitWord(0x4FEF); // LEA -frame(A7),A7
		_assembler.EmitWord(unchecked((ushort)(short)-bytes));
	}

	private void EmitReleaseFrame(int bytes)
	{
		if (bytes == 0)
		{
			return;
		}
		if (bytes <= 8)
		{
			var encodedCount = bytes == 8 ? 0 : bytes;
			_assembler.EmitWord((ushort)(0x508F | (encodedCount << 9))); // ADDQ.L #bytes,A7
			return;
		}

		_assembler.EmitWord(0x4FEF); // LEA frame(A7),A7
		_assembler.EmitWord((ushort)bytes);
	}

	private void EmitPopBinaryOperands()
	{
		_assembler.EmitWord(0x221F); // MOVE.L (A7)+,D1 (right)
		_assembler.EmitWord(0x201F); // MOVE.L (A7)+,D0 (left)
	}

	private void EmitPopD0() => _assembler.EmitWord(0x201F);

	private void EmitPushD0() => _assembler.EmitWord(0x2F00);

	private void EmitDiscardStackArguments(int count)
	{
		var bytes = checked(count * 4);
		while (bytes >= 8)
		{
			_assembler.EmitWord(0x508F); // ADDQ.L #8,A7 (encoded quick zero)
			bytes -= 8;
		}

		if (bytes == 4)
		{
			_assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		}
	}

	private static bool TryGetConstant(CilInstruction instruction, out int value)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Ldc_I4_M1)
		{
			value = -1;
			return true;
		}

		if (op.Value >= OpCodes.Ldc_I4_0.Value && op.Value <= OpCodes.Ldc_I4_8.Value)
		{
			value = op.Value - OpCodes.Ldc_I4_0.Value;
			return true;
		}

		if (op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_I4)
		{
			value = Convert.ToInt32(instruction.Operand);
			return true;
		}

		value = 0;
		return false;
	}

	private static bool TryGetArgumentIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldarg_0.Value && op.Value <= OpCodes.Ldarg_3.Value)
		{
			index = op.Value - OpCodes.Ldarg_0.Value;
			return true;
		}

		if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = 0;
		return false;
	}

	private static bool TryGetLoadLocalIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldloc_0.Value && op.Value <= OpCodes.Ldloc_3.Value)
		{
			index = op.Value - OpCodes.Ldloc_0.Value;
			return true;
		}

		if (op == OpCodes.Ldloc || op == OpCodes.Ldloc_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = 0;
		return false;
	}

	private static bool TryGetStoreLocalIndex(CilInstruction instruction, out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Stloc_0.Value && op.Value <= OpCodes.Stloc_3.Value)
		{
			index = op.Value - OpCodes.Stloc_0.Value;
			return true;
		}

		if (op == OpCodes.Stloc || op == OpCodes.Stloc_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		index = 0;
		return false;
	}

	private static bool IsUnconditionalBranch(OpCode op) =>
		op == OpCodes.Br || op == OpCodes.Br_S;

	private static bool TryGetRelationalBranch(OpCode op, out M68kCondition condition)
	{
		if (op == OpCodes.Beq || op == OpCodes.Beq_S)
		{
			condition = M68kCondition.Equal;
			return true;
		}

		if (op == OpCodes.Bne_Un || op == OpCodes.Bne_Un_S)
		{
			condition = M68kCondition.NotEqual;
			return true;
		}

		if (op == OpCodes.Bge || op == OpCodes.Bge_S)
		{
			condition = M68kCondition.GreaterOrEqual;
			return true;
		}

		if (op == OpCodes.Bgt || op == OpCodes.Bgt_S)
		{
			condition = M68kCondition.GreaterThan;
			return true;
		}

		if (op == OpCodes.Ble || op == OpCodes.Ble_S)
		{
			condition = M68kCondition.LessOrEqual;
			return true;
		}

		if (op == OpCodes.Blt || op == OpCodes.Blt_S)
		{
			condition = M68kCondition.LessThan;
			return true;
		}

		if (op == OpCodes.Bge_Un || op == OpCodes.Bge_Un_S)
		{
			condition = M68kCondition.CarryClear;
			return true;
		}

		if (op == OpCodes.Bgt_Un || op == OpCodes.Bgt_Un_S)
		{
			condition = M68kCondition.Higher;
			return true;
		}

		if (op == OpCodes.Ble_Un || op == OpCodes.Ble_Un_S)
		{
			condition = M68kCondition.LowerOrSame;
			return true;
		}

		if (op == OpCodes.Blt_Un || op == OpCodes.Blt_Un_S)
		{
			condition = M68kCondition.CarrySet;
			return true;
		}

		condition = default;
		return false;
	}

	private static void ValidateLocal(CilMethod method, CilInstruction instruction, int index)
	{
		if ((uint)index >= (uint)method.Locals.Length)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Local index {index} is outside the local signature.",
				method.DisplayName,
				instruction.Offset);
		}
	}

	private static void ValidateArgument(CilMethod method, CilInstruction instruction, int index)
	{
		if ((uint)index >= (uint)method.ParameterCount)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Argument index {index} is outside the method signature.",
				method.DisplayName,
				instruction.Offset);
		}
	}

	private static short LocalOffset(CilMethod method, int index) =>
		checked((short)(4 * ((GetInternalRegisterAbi(method)?.Count ?? 0) + index)));

	private static short ArgumentHomeOffset(int index) =>
		checked((short)(4 * index));

	private static short ArgumentOffset(CilMethod method, int index)
	{
		if ((uint)index >= (uint)method.ParameterCount)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidMetadata,
				$"Argument index {index} is outside the method signature.",
				method.DisplayName);
		}

		var frameBytes = checked(
			(method.Locals.Length + (GetInternalRegisterAbi(method)?.Count ?? 0)) * 4);
		return GetInternalRegisterAbi(method) is not null
			? ArgumentHomeOffset(index)
			: checked((short)(frameBytes + 4 + ((method.ParameterCount - 1 - index) * 4)));
	}

	private static short FrameDisplacement(short frameOffset, int stackDepth) =>
		checked((short)(frameOffset + (stackDepth * 4)));

	private static string MethodLabel(CilMethod method) =>
		$"method:{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(method.Handle):X8}";

	private static string IlLabel(CilMethod method, int offset) =>
		$"{MethodLabel(method)}:IL_{offset:X4}";

	private static string TypeDescriptorLabel(TypeDefinitionHandle handle) =>
		$"type:{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle):X8}";

	private static string StaticFieldLabel(FieldDefinitionHandle handle) =>
		$"static:{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle):X8}";

	private static string StringLabel(int token) => $"string:{token:X8}";

	private static string CStringLabel(int token) => $"cstring:{token:X8}";

	private const string GcConfigLabel = "runtime:gc-config";
	private const string GcHeapStartLabel = "runtime:gc-heap-start";
	private const string GcHeapEndLabel = "runtime:gc-heap-end";
	private const string GcFreeHeadLabel = "runtime:gc-free-head";
	private const string GcAllocHeadLabel = "runtime:gc-alloc-head";
	private const string GcStaleBytesLabel = "runtime:gc-stale-bytes";
	private const string GcStaleBlocksLabel = "runtime:gc-stale-blocks";
	private const string GcStaleBytesThresholdLabel = "runtime:gc-stale-bytes-threshold";
	private const string GcStaleBlocksThresholdLabel = "runtime:gc-stale-blocks-threshold";
	private const string RuntimeInitLabel = "__c68k_gc_init";
	private const string RuntimeAllocLabel = "__c68k_alloc";
	private const string RuntimeDisposeLabel = "__c68k_dispose";
	private const string RuntimeMarkLabel = "__c68k_gc_mark";
	private const string RuntimeCollectLabel = "__c68k_gc_collect";
	private const string RuntimeCoalesceLabel = "__c68k_gc_coalesce";
	private const string RuntimeGetStaleBytesLabel = "__c68k_gc_get_stale_bytes";
	private const string RuntimeGetStaleBlocksLabel = "__c68k_gc_get_stale_blocks";
	private const string RuntimeShutdownLabel = "__c68k_gc_shutdown";
	private const int GcBlockHeaderSize = 16;
	private const int GcMinimumSplitSize = GcBlockHeaderSize + 4;
	private const uint GcMarkFlag = 2;
	private const uint GcScanFlag = 4;

	private static string ArrayDescriptorLabel(CilType elementType) =>
		$"array:{elementType.DisplayName}";

	internal static string ExportLabel(string name) => $"export:{name}";

	private string UniqueLabel(string prefix) => $"generated:{prefix}:{_uniqueLabel++}";
}

internal sealed record GeneratedProgram(
	M68kAssembler Assembler,
	IReadOnlyList<CilMethod> Methods,
	IReadOnlyList<CilExport> Exports,
	IReadOnlyList<GeneratedPlatformBase> PlatformBases,
	string EntryLabel);

internal sealed record GeneratedPlatformBase(
	M68kExternalCallConvention Binding,
	string? Label);

internal readonly record struct MemoryAccess(
	int Size,
	bool SignExtend,
	bool IsStore);
