using System.Reflection.Emit;
using System.Reflection.Metadata;
using Copper68k.Compiler.Metadata;

namespace Copper68k.Compiler.Backend;

internal sealed class M68kCodeGenerator
{
	private readonly CompilationModule _module;
	private readonly M68kCompilationRequest _request;
	private readonly M68kAssembler _assembler = new();
	private readonly HashSet<TypeDefinitionHandle> _usedTypeLayouts = new();
	private readonly Dictionary<FieldDefinitionHandle, CilField> _staticFields = new();
	private readonly Dictionary<int, string> _stringLiterals = new();
	private readonly Dictionary<string, CilType> _arrayTypes = new(StringComparer.Ordinal);
	private readonly Dictionary<string, GeneratedPlatformBase> _usedPlatformBases = new(StringComparer.Ordinal);
	private int _uniqueLabel;
	private int _currentStackDepth;
	private GeneratedPlatformBase? _loadedPlatformBase;

	public M68kCodeGenerator(CompilationModule module, M68kCompilationRequest request)
	{
		_module = module;
		_request = request;
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
		foreach (var export in exports)
		{
			EmitExportAdapter(export, cachesPlatformBase);
		}
		var entryLabel = MethodLabel(entry);
		if (cachesPlatformBase)
		{
			entryLabel = EmitEntryAdapter(entry);
		}
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
		var reachableDepths = CilStackAnalyzer.Analyze(method, _module);
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
			if (!reachableDepths.ContainsKey(instruction.Offset))
			{
				continue;
			}

			_currentStackDepth = reachableDepths[instruction.Offset];
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

		if (op == OpCodes.Ldelem_I4 || op == OpCodes.Ldelem_U4 ||
			op == OpCodes.Ldelem_Ref || op == OpCodes.Ldelema ||
			op == OpCodes.Stelem_I4 || op == OpCodes.Stelem_Ref)
		{
			EmitArrayAccess(method, instruction);
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
				? $"platform-base:{binding.Identity}"
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

		if (!constructor.Signature.Header.IsInstance ||
			constructor.Signature.ParameterTypes.Length != 0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				"Object construction currently requires a parameterless instance constructor.",
				caller.DisplayName,
				instruction.Offset);
		}

		var layout = _module.GetTypeLayout(constructor.DeclaringType);
		_usedTypeLayouts.Add(layout.Handle);
		EmitPushConstant(layout.Size);
		_assembler.EmitJsr(M68kRuntimeImports.Allocate, external: true);
		_loadedPlatformBase = null;
		EmitDiscardStackArguments(1);
		EmitRequireNonNull();

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

		EmitPushD0(); // Object retained as newobj result.
		EmitPushD0(); // Object passed as constructor this.
		var constructorAbi = GetInternalRegisterAbi(constructor);
		if (constructorAbi is not null)
		{
			EmitLoadInternalArguments(constructorAbi);
			EmitDiscardStackArguments(constructorAbi.Count);
		}
		_assembler.EmitJsr(MethodLabel(constructor), external: false);
		_loadedPlatformBase = null;
		if (constructorAbi is null)
		{
			EmitDiscardStackArguments(1);
		}
	}

	private void EmitNewArray(CilMethod method, CilInstruction instruction)
	{
		var elementType = _module.ResolveTypeToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (elementType.Size != 4 ||
			(!elementType.IsSupportedScalar && !elementType.IsReference))
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Arrays of '{elementType.DisplayName}' are not implemented; v1 array elements must occupy four bytes.",
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
		_assembler.EmitWord(0xE388); // LSL.L #1,D0
		_assembler.EmitWord(0xE388); // LSL.L #1,D0
		_assembler.EmitWord(0x0680); // ADDI.L #12,D0
		_assembler.EmitLong(12);
		EmitPushD0();
		_assembler.EmitJsr(M68kRuntimeImports.Allocate, external: true);
		_loadedPlatformBase = null;
		EmitDiscardStackArguments(1);
		EmitRequireNonNull();
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		_assembler.EmitWord(0x20BC); // MOVE.L #descriptor,(A0)
		_assembler.EmitAddress(ArrayDescriptorLabel(elementType));
		_assembler.EmitWord(0x2202); // MOVE.L D2,D1
		_assembler.EmitWord(0xE389); // LSL.L #1,D1
		_assembler.EmitWord(0xE389); // LSL.L #1,D1
		_assembler.EmitWord(0x0681); // ADDI.L #12,D1
		_assembler.EmitLong(12);
		_assembler.EmitWord(0x2141); // MOVE.L D1,4(A0)
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2142); // MOVE.L D2,8(A0)
		_assembler.EmitWord(0x0008);
		EmitPushD0();
	}

	private void EmitArrayAccess(CilMethod method, CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Stelem_I4 || op == OpCodes.Stelem_Ref)
		{
			EmitPopD0(); // value
			_assembler.EmitWord(0x221F); // MOVE.L (A7)+,D1 index
			_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0 array
			EmitArrayBoundsCheck();
			_assembler.EmitWord(0xE389); // LSL.L #1,D1
			_assembler.EmitWord(0xE389); // LSL.L #1,D1
			_assembler.EmitWord(0xD1C1); // ADDA.L D1,A0
			_assembler.EmitWord(0x2140); // MOVE.L D0,12(A0)
			_assembler.EmitWord(0x000C);
			return;
		}

		_assembler.EmitWord(0x221F); // MOVE.L (A7)+,D1 index
		_assembler.EmitWord(0x205F); // MOVEA.L (A7)+,A0 array
		EmitArrayBoundsCheck();
		_assembler.EmitWord(0xE389); // LSL.L #1,D1
		_assembler.EmitWord(0xE389); // LSL.L #1,D1
		_assembler.EmitWord(0xD1C1); // ADDA.L D1,A0
		if (op == OpCodes.Ldelema)
		{
			_assembler.EmitWord(0x41E8); // LEA 12(A0),A0
			_assembler.EmitWord(0x000C);
			_assembler.EmitWord(0x2F08); // MOVE.L A0,-(A7)
			return;
		}

		_assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0
		_assembler.EmitWord(0x000C);
		EmitPushD0();
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

		foreach (var type in _arrayTypes.Values.OrderBy(item => item.DisplayName, StringComparer.Ordinal))
		{
			_assembler.AlignWord();
			_assembler.Mark(ArrayDescriptorLabel(type));
			_assembler.EmitLong(0); // Variable size.
			_assembler.EmitLong(type.IsReference ? 1u : 0u);
		}
	}

	private string EmitEntryAdapter(CilMethod entry)
	{
		const string label = "entry:managed";
		_assembler.AlignWord();
		_assembler.Mark(label);
		EmitCachePlatformBases();
		_assembler.EmitJmp(MethodLabel(entry), external: false);
		return label;
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
