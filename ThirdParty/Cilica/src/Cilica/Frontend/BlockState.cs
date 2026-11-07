namespace Cilica.Frontend;

internal class BlockState
{
    readonly ILImporter _importer;
    readonly MethodBody _body;
    public readonly int StartOffset;

    private ModuleDef _mod => _body.Definition.Module;

    public readonly BasicBlock Block;
    // A parent block that enters this one. Currently used to handle nested exception regions.
    public BasicBlock EntryBlock;

    readonly ArrayStack<Value> _stack;
    readonly List<BlockState> _preds = new();
    PhiInst[] _entryPhis = [];
    int _mergedPredCount, _entryStackHeight;

    private InstFlags _prefixFlags = InstFlags.None;
    private TypeDesc? _callConstraint;

    private DebugSourceLocation? _currDebugLoc = null;

    public BlockState(ILImporter importer, int startOffset)
    {
        _importer = importer;
        _body = importer._body;
        this.StartOffset = startOffset;
        
        Block = _body.CreateBlock();
        EntryBlock = Block;

        var ilBody = importer._method.ILBody!;
        _stack = new ArrayStack<Value>(ilBody.MaxStack);
    }

    public void Emit(Instruction inst)
    {
        Block.InsertLast(inst);
        inst.DebugLocation = _currDebugLoc;
    }

    public void PushNoEmit(Value value)
    {
        Debug.Assert(value.HasResult);
        _stack.Push(value);
    }
    public void Push(Value value)
    {
        if (value is Instruction inst) {
            Emit(inst);
        }
        if (value.HasResult) {
            _stack.Push(value);
        }
    }
    public Value Pop()
    {
        return _stack.Pop();
    }

    private bool HasPrefix(InstFlags flag)
    {
        return (_prefixFlags & flag) == flag;
    }

    private BasicBlock AddSucc(int offset)
    {
        var succ = _importer.GetBlock(offset);
        if (!succ._preds.Contains(this)) succ._preds.Add(this);
        return _importer.GetBranchTarget(StartOffset, offset);
    }
    // Adds the last instruction in the block (a branch).
    private void TerminateBlock(Instruction branch, bool clearStack = false)
    {
        Emit(branch);

        if (clearStack) {
            _stack.Clear();
        }
    }

    private void MergePredStacks()
    {
        if (_preds.Count == 0) return;

        _mergedPredCount = _preds.Count;
        int maxDepth = _preds[0]._stack.Count;

        // We don't make any guarantees for invalid IL, this is just for good measure.
        Debug.Assert(_preds.All(b => b._stack.Count == maxDepth));

        if (maxDepth == 0) return;
        _entryPhis = new PhiInst[maxDepth];

        for (int depth = 0; depth < maxDepth; depth++) {
            var args = new PhiArg[_preds.Count];
            int argIdx = 0;
            var type = default(TypeDesc);

            foreach (var pred in _preds) {
                if (pred._stack.Count != maxDepth) throw Fail();

                var value = pred._stack[depth];
                args[argIdx++] = (pred.Block, value);

                if (value is not ConstNull) {
                    type = GetMergedStackType(type, value.ResultType) ?? throw Fail();
                }
            }
            type ??= PrimType.Object; // all args were ConstNull`s

            // Backedges may not have been imported yet. Keep a placeholder
            // even for a single incoming value, then complete it after import.
            var result = _entryPhis[depth] = EntryBlock.InsertPhi(new PhiInst(type, args));

            _stack.Push(result);
        }

        Exception Fail() => throw Error("Inconsistent evaluation stack between basic blocks.");
    }

    public void CompleteStackPhis()
    {
        foreach (var predecessor in _preds) {
            if (predecessor._stack.Count != _entryStackHeight) throw Error("Inconsistent evaluation stack on a backedge.");
        }
        if (_preds.Count == _mergedPredCount || _entryStackHeight == 0) return;
        if (_entryPhis.Length != _entryStackHeight) {
            throw new NotSupportedException("Stack-carrying branches into exceptional entries are not supported");
        }
        for (int i = _mergedPredCount; i < _preds.Count; i++) {
            var predecessor = _preds[i];
            for (int depth = 0; depth < _entryPhis.Length; depth++) {
                var phi = _entryPhis[depth];
                var value = predecessor._stack[depth];
                if (value is not ConstNull && GetMergedStackType(phi.ResultType, value.ResultType) != phi.ResultType) {
                    throw new NotSupportedException("Evaluation stack type widens on a backedge");
                }
                phi.AddArg(predecessor.Block, value);
            }
        }
    }

    private TypeDesc? GetMergedStackType(TypeDesc? currType, TypeDesc newType)
    {
        // III.1.8.1.3
        if (currType == null) {
            return newType;
        }
        if (newType.IsAssignableTo(currType)) {
            return currType;
        }
        if (currType.IsAssignableTo(newType)) {
            return newType;
        }
        if (currType.StackType is StackType.Object && newType.StackType is StackType.Object) {
            return TypeDesc.GetCommonAncestor(currType, newType);
        }
        return null;
    }

    /// <summary> Translates the IL code into IR instructions. </summary>
    public void ImportCode(Span<ILInstruction> code)
    {
        MergePredStacks();
        _entryStackHeight = _stack.Count;
        var seqPoints = _importer._debugSyms?.SequencePoints;
        int seqIndex = _importer._debugSyms?.IndexOfSequencePoint(code[0].Offset) ?? -1;

        for (int instructionIndex = 0; instructionIndex < code.Length; instructionIndex++) {
            ref var inst = ref code[instructionIndex];
            // Update debug location info
            if (seqPoints != null) {
                while (seqIndex + 1 < seqPoints.Count &&  inst.Offset >= seqPoints[seqIndex + 1].Offset) {
                    seqIndex++;
                }
                if (seqIndex < 0 || seqPoints[seqIndex].IsHidden) {
                    _currDebugLoc = null;
                } else {
                    var sp = seqPoints[seqIndex];
                    if (_currDebugLoc == null || !SequencePoint.Create(_currDebugLoc, 0).IsSameSourceRange(sp)) {
                        _currDebugLoc = new DebugSourceLocation(sp);
                    }
                }
            }
            var prefix = InstFlags.None;
            var opcode = inst.OpCode;
            if (inst.Operand is TypeDef { IsGeneric: true } && opcode != ILCode.Ldtoken) {
                throw Error("Type instruction requires an instantiated generic type.");
            }
            if (inst.Operand is FieldDesc fieldOperand && fieldOperand.Attribs.HasFlag(System.Reflection.FieldAttributes.Literal)) {
                throw Error("Literal fields have no runtime storage.");
            }

            #pragma warning disable format
            const bool T = true, F = false;

            switch (opcode) {
                #region Load Const
                case >= ILCode.Ldc_I4_M1 and <= ILCode.Ldc_I4_8:
                    ImportConst(ConstInt.CreateI((int)opcode - (int)ILCode.Ldc_I4_0));
                    break;
                case ILCode.Ldc_I4_S or ILCode.Ldc_I4:
                    ImportConst(ConstInt.CreateI((int)inst.Operand!));
                    break;
                case ILCode.Ldc_I8: ImportConst(ConstInt.CreateL((long)inst.Operand!)); break;
                case ILCode.Ldc_R4: ImportConst(ConstFloat.CreateS((float)inst.Operand!)); break;
                case ILCode.Ldc_R8: ImportConst(ConstFloat.CreateD((double)inst.Operand!)); break;
                case ILCode.Ldnull: ImportConst(ConstNull.Create()); break;
                case ILCode.Ldstr:  ImportConst(ConstString.Create((string)inst.Operand!)); break;
                #endregion

                #region Load/Store Local/Argument
                case >= ILCode.Ldarg_0 and <= ILCode.Ldarg_3:
                case >= ILCode.Ldloc_0 and <= ILCode.Ldloc_3:
                case >= ILCode.Stloc_0 and <= ILCode.Stloc_3:
                case ILCode.Ldarg_S or ILCode.Ldarg:
                case ILCode.Ldloc_S or ILCode.Ldloc:
                case ILCode.Starg_S or ILCode.Starg:
                case ILCode.Stloc_S or ILCode.Stloc:
                case ILCode.Ldarga_S or ILCode.Ldarga:
                case ILCode.Ldloca_S or ILCode.Ldloca:
                    ImportVarInst(ref inst);
                    break;
                #endregion

                #region Arithmetic/Bitwise
                case ILCode.Add:        ImportBinary(BinaryOp.Add, BinaryOp.FAdd); break;
                case ILCode.Sub:        ImportBinary(BinaryOp.Sub, BinaryOp.FSub); break;
                case ILCode.Mul:        ImportBinary(BinaryOp.Mul, BinaryOp.FMul); break;
                case ILCode.Div:        ImportBinary(BinaryOp.SDiv, BinaryOp.FDiv); break;
                case ILCode.Rem:        ImportBinary(BinaryOp.SRem, BinaryOp.FRem); break;
                case ILCode.Div_Un:     ImportBinary(BinaryOp.UDiv); break;
                case ILCode.Rem_Un:     ImportBinary(BinaryOp.URem); break;

                case ILCode.And:        ImportBinary(BinaryOp.And); break;
                case ILCode.Or:         ImportBinary(BinaryOp.Or); break;
                case ILCode.Xor:        ImportBinary(BinaryOp.Xor); break;
                case ILCode.Shl:        ImportBinary(BinaryOp.Shl); break;
                case ILCode.Shr:        ImportBinary(BinaryOp.Shra); break;
                case ILCode.Shr_Un:     ImportBinary(BinaryOp.Shrl); break;

                case ILCode.Add_Ovf:    ImportBinary(BinaryOp.AddOvf); break;
                case ILCode.Add_Ovf_Un: ImportBinary(BinaryOp.UAddOvf); break;
                case ILCode.Sub_Ovf:    ImportBinary(BinaryOp.SubOvf); break;
                case ILCode.Sub_Ovf_Un: ImportBinary(BinaryOp.USubOvf); break;
                case ILCode.Mul_Ovf:    ImportBinary(BinaryOp.MulOvf); break;
                case ILCode.Mul_Ovf_Un: ImportBinary(BinaryOp.UMulOvf); break;

                case ILCode.Neg:
                case ILCode.Not:
                    ImportUnary(opcode);
                    break;
                #endregion

                #region Convert From/To Primitive
                case ILCode.Conv_I1: ImportConv(PrimType.SByte,   F); break;
                case ILCode.Conv_I2: ImportConv(PrimType.Int16,   F); break;
                case ILCode.Conv_I4: ImportConv(PrimType.Int32,   F); break;
                case ILCode.Conv_I8: ImportConv(PrimType.Int64,   F); break;
                case ILCode.Conv_U1: ImportConv(PrimType.Byte,    F); break;
                case ILCode.Conv_U2: ImportConv(PrimType.UInt16,  F); break;
                case ILCode.Conv_U4: ImportConv(PrimType.UInt32,  F); break;
                case ILCode.Conv_U8: ImportConv(PrimType.UInt64,  F); break;
                case ILCode.Conv_R4: ImportConv(PrimType.Single,  F); break;
                case ILCode.Conv_R8: ImportConv(PrimType.Double,  F); break;
                case ILCode.Conv_I:  ImportConv(PrimType.IntPtr,  F); break;
                case ILCode.Conv_U:  ImportConv(PrimType.UIntPtr, F); break;
                case ILCode.Conv_R_Un: {
                    if (instructionIndex + 1 >= code.Length) {
                        throw new NotSupportedException("Unsigned floating conversion crosses a block without an explicit precision");
                    }
                    ref var next = ref code[instructionIndex + 1];
                    var precision = GetFloatingSinkType(next);
                    if (precision == null || !precision.IsFloat()) {
                        throw new NotSupportedException("Unsigned floating conversion requires a known floating-point sink");
                    }
                    ImportConv(precision, F, T);
                    if (next.OpCode is ILCode.Conv_R4 or ILCode.Conv_R8) instructionIndex++;
                    break;
                }

                case ILCode.Conv_Ovf_I1: ImportConv(PrimType.SByte,   T); break;
                case ILCode.Conv_Ovf_I2: ImportConv(PrimType.Int16,   T); break;
                case ILCode.Conv_Ovf_I4: ImportConv(PrimType.Int32,   T); break;
                case ILCode.Conv_Ovf_I8: ImportConv(PrimType.Int64,   T); break;
                case ILCode.Conv_Ovf_U1: ImportConv(PrimType.Byte,    T); break;
                case ILCode.Conv_Ovf_U2: ImportConv(PrimType.UInt16,  T); break;
                case ILCode.Conv_Ovf_U4: ImportConv(PrimType.UInt32,  T); break;
                case ILCode.Conv_Ovf_U8: ImportConv(PrimType.UInt64,  T); break;
                case ILCode.Conv_Ovf_I:  ImportConv(PrimType.IntPtr,  T); break;
                case ILCode.Conv_Ovf_U:  ImportConv(PrimType.UIntPtr, T); break;

                case ILCode.Conv_Ovf_I1_Un: ImportConv(PrimType.SByte,   T, T); break;
                case ILCode.Conv_Ovf_I2_Un: ImportConv(PrimType.Int16,   T, T); break;
                case ILCode.Conv_Ovf_I4_Un: ImportConv(PrimType.Int32,   T, T); break;
                case ILCode.Conv_Ovf_I8_Un: ImportConv(PrimType.Int64,   T, T); break;
                case ILCode.Conv_Ovf_U1_Un: ImportConv(PrimType.Byte,    T, T); break;
                case ILCode.Conv_Ovf_U2_Un: ImportConv(PrimType.UInt16,  T, T); break;
                case ILCode.Conv_Ovf_U4_Un: ImportConv(PrimType.UInt32,  T, T); break;
                case ILCode.Conv_Ovf_U8_Un: ImportConv(PrimType.UInt64,  T, T); break;
                case ILCode.Conv_Ovf_I_Un:  ImportConv(PrimType.IntPtr,  T, T); break;
                case ILCode.Conv_Ovf_U_Un:  ImportConv(PrimType.UIntPtr, T, T); break;
                #endregion

                #region Branching
                case ILCode.Br:
                case ILCode.Br_S:
                    ImportBranch(ref inst);
                    break;
                case ILCode.Brfalse:
                case ILCode.Brfalse_S:
                    ImportUnaryBranch(ref inst, CompareOp.Eq, CompareOp.FOeq);
                    break;
                case ILCode.Brtrue:
                case ILCode.Brtrue_S:
                    ImportUnaryBranch(ref inst, CompareOp.Ne, CompareOp.FUne);
                    break;
                case ILCode.Beq_S:
                case ILCode.Beq:
                    ImportBinaryBranch(ref inst, CompareOp.Eq, CompareOp.FOeq);
                    break;
                case ILCode.Bge_S:
                case ILCode.Bge:
                    ImportBinaryBranch(ref inst, CompareOp.Sge, CompareOp.FOge);
                    break;
                case ILCode.Bgt_S:
                case ILCode.Bgt:
                    ImportBinaryBranch(ref inst, CompareOp.Sgt, CompareOp.FOgt);
                    break;
                case ILCode.Ble_S:
                case ILCode.Ble:
                    ImportBinaryBranch(ref inst, CompareOp.Sle, CompareOp.FOle);
                    break;
                case ILCode.Blt_S:
                case ILCode.Blt:
                    ImportBinaryBranch(ref inst, CompareOp.Slt, CompareOp.FOlt);
                    break;
                case ILCode.Bne_Un_S:
                case ILCode.Bne_Un:
                    ImportBinaryBranch(ref inst, CompareOp.Ne, CompareOp.FUne);
                    break;
                case ILCode.Bge_Un_S:
                case ILCode.Bge_Un:
                    ImportBinaryBranch(ref inst, CompareOp.Uge, CompareOp.FUge);
                    break;
                case ILCode.Bgt_Un_S:
                case ILCode.Bgt_Un:
                    ImportBinaryBranch(ref inst, CompareOp.Ugt, CompareOp.FUgt);
                    break;
                case ILCode.Ble_Un_S:
                case ILCode.Ble_Un:
                    ImportBinaryBranch(ref inst, CompareOp.Ule, CompareOp.FUle);
                    break;
                case ILCode.Blt_Un_S:
                case ILCode.Blt_Un:
                    ImportBinaryBranch(ref inst, CompareOp.Ult, CompareOp.FUlt);
                    break;

                case ILCode.Switch: ImportSwitch(ref inst); break;
                #endregion

                #region Comparison
                case ILCode.Ceq:    ImportCompare(CompareOp.Eq, CompareOp.FOeq); break;
                case ILCode.Cgt:    ImportCompare(CompareOp.Sgt, CompareOp.FOgt); break;
                case ILCode.Clt:    ImportCompare(CompareOp.Slt, CompareOp.FOlt); break;
                case ILCode.Cgt_Un: ImportCompare(CompareOp.Ugt, CompareOp.FUgt); break;
                case ILCode.Clt_Un: ImportCompare(CompareOp.Ult, CompareOp.FUlt); break;
                #endregion

                #region Load/Store Array Element
                case ILCode.Ldlen: ImportLoadLen(); break;
                case ILCode.Ldelema: ImportLoadElemAddr((TypeDesc)inst.Operand!); break;

                case ILCode.Ldelem_I1:  ImportLoadElem(PrimType.SByte); break;
                case ILCode.Ldelem_I2:  ImportLoadElem(PrimType.Int16); break;
                case ILCode.Ldelem_I4:  ImportLoadElem(PrimType.Int32); break;
                case ILCode.Ldelem_I8:  ImportLoadElem(PrimType.Int64); break;
                case ILCode.Ldelem_U1:  ImportLoadElem(PrimType.Byte); break;
                case ILCode.Ldelem_U2:  ImportLoadElem(PrimType.UInt16); break;
                case ILCode.Ldelem_U4:  ImportLoadElem(PrimType.UInt32); break;
                case ILCode.Ldelem_R4:  ImportLoadElem(PrimType.Single); break;
                case ILCode.Ldelem_R8:  ImportLoadElem(PrimType.Double); break;
                case ILCode.Ldelem_I:   ImportLoadElem(PrimType.IntPtr); break;
                case ILCode.Ldelem_Ref: ImportLoadElem(null); break;
                case ILCode.Ldelem:     ImportLoadElem((TypeDesc)inst.Operand!); break;

                case ILCode.Stelem_I1:  ImportStoreElem(PrimType.SByte); break;
                case ILCode.Stelem_I2:  ImportStoreElem(PrimType.Int16); break;
                case ILCode.Stelem_I4:  ImportStoreElem(PrimType.Int32); break;
                case ILCode.Stelem_I8:  ImportStoreElem(PrimType.Int64); break;
                case ILCode.Stelem_R4:  ImportStoreElem(PrimType.Single); break;
                case ILCode.Stelem_R8:  ImportStoreElem(PrimType.Double); break;
                case ILCode.Stelem_I:   ImportStoreElem(PrimType.IntPtr); break;
                case ILCode.Stelem_Ref: ImportStoreElem(null); break;
                case ILCode.Stelem:     ImportStoreElem((TypeDesc)inst.Operand!); break;
                #endregion

                #region Load/Store Indirect
                case ILCode.Ldind_I1: ImportLoadInd(PrimType.SByte); break;
                case ILCode.Ldind_I2: ImportLoadInd(PrimType.Int16); break;
                case ILCode.Ldind_I4: ImportLoadInd(PrimType.Int32); break;
                case ILCode.Ldind_I8: ImportLoadInd(PrimType.Int64); break;
                case ILCode.Ldind_U1: ImportLoadInd(PrimType.Byte); break;
                case ILCode.Ldind_U2: ImportLoadInd(PrimType.UInt16); break;
                case ILCode.Ldind_U4: ImportLoadInd(PrimType.UInt32); break;
                case ILCode.Ldind_R4: ImportLoadInd(PrimType.Single); break;
                case ILCode.Ldind_R8: ImportLoadInd(PrimType.Double); break;
                case ILCode.Ldind_I:  ImportLoadInd(PrimType.IntPtr); break;
                case ILCode.Ldind_Ref: ImportLoadInd(null); break;

                case ILCode.Stind_I1: ImportStoreInd(PrimType.SByte); break;
                case ILCode.Stind_I2: ImportStoreInd(PrimType.Int16); break;
                case ILCode.Stind_I4: ImportStoreInd(PrimType.Int32); break;
                case ILCode.Stind_I8: ImportStoreInd(PrimType.Int64); break;
                case ILCode.Stind_R4: ImportStoreInd(PrimType.Single); break;
                case ILCode.Stind_R8: ImportStoreInd(PrimType.Double); break;
                case ILCode.Stind_I:  ImportStoreInd(PrimType.IntPtr); break;
                case ILCode.Stind_Ref: ImportStoreInd(null); break;

                case ILCode.Ldobj: ImportLoadInd((TypeDesc)inst.Operand!); break;
                case ILCode.Stobj: ImportStoreInd((TypeDesc)inst.Operand!); break;
                #endregion

                #region Load/Store Field
                case ILCode.Ldfld:
                case ILCode.Ldsfld:
                    ImportLoadField((FieldDesc)inst.Operand!, opcode == ILCode.Ldsfld);
                    break;

                case ILCode.Stfld:
                case ILCode.Stsfld:
                    ImportStoreField((FieldDesc)inst.Operand!, opcode == ILCode.Stsfld);
                    break;
                
                case ILCode.Ldflda:
                case ILCode.Ldsflda:
                    ImportFieldAddr((FieldDesc)inst.Operand!, opcode == ILCode.Ldsflda);
                    break;
                #endregion

                #region Prefixes
                case ILCode.Unaligned_:     prefix = InstFlags.Unaligned; break;
                case ILCode.Volatile_:      prefix = InstFlags.Volatile; break;
                case ILCode.Tail_:          prefix = InstFlags.Tailcall; break;
                case ILCode.Constrained_:   prefix = InstFlags.Constrained; _callConstraint = (TypeDesc)inst.Operand!; break;
                case ILCode.Readonly_:      prefix = InstFlags.Readonly; break;
                case ILCode.No_: {
                    int flags = (int)inst.Operand!;
                    prefix = (InstFlags)(flags << (int)InstFlags.NoPrefixShift_);
                    break;
                }
                #endregion

                #region Exception Leave/End/Throw
                case ILCode.Leave:
                case ILCode.Leave_S:
                    ImportLeave((int)inst.Operand!);
                    break;
                case ILCode.Throw:
                case ILCode.Rethrow:
                    ImportThrow(opcode == ILCode.Rethrow);
                    break;
                case ILCode.Endfinally:
                case ILCode.Endfilter:
                    ImportResume(opcode == ILCode.Endfilter);
                    break;
                #endregion

                #region Call
                case ILCode.Call:
                case ILCode.Callvirt:
                    ImportCall((MethodDesc)inst.Operand!, opcode == ILCode.Callvirt);
                    break;
                case ILCode.Calli:
                    ImportCallIndirect((FuncPtrType)inst.Operand!);
                    break;
                case ILCode.Ldftn:
                case ILCode.Ldvirtftn:
                    ImportLoadFuncPtr((MethodDesc)inst.Operand!, opcode == ILCode.Ldvirtftn);
                    break;

                case ILCode.Newobj: ImportNewObj((MethodDesc)inst.Operand!); break;
                #endregion

                #region Intrinsics
                case ILCode.Arglist:
                    if (_body.Definition.CallConv != CallConvention.VarArgs) throw Error("arglist requires a vararg method.");
                    Push(new CilIntrinsic.ArgList(_mod.Resolver));
                    break;
                case ILCode.Newarr:
                    Push(new CilIntrinsic.NewArray((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Castclass:
                    Push(new CilIntrinsic.CastClass((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Isinst:
                    Push(new CilIntrinsic.AsInstance((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Box:
                    Push(new CilIntrinsic.Box((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Unbox:
                    Push(new CilIntrinsic.UnboxRef((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Unbox_Any:
                    Push(new CilIntrinsic.UnboxObj((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Ldtoken:
                    Push(new CilIntrinsic.LoadHandle(_mod.Resolver, (EntityDesc)inst.Operand!));
                    break;
                case ILCode.Refanytype:
                    Push(new CilIntrinsic.RefAnyType(_mod.Resolver, Pop()));
                    break;
                case ILCode.Mkrefany:
                    Push(new CilIntrinsic.MkRefAny((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Refanyval:
                    Push(new CilIntrinsic.RefAnyVal((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Initobj:
                    new IR.Utils.IRBuilder(Block, IR.Utils.InsertionDir.After).SetNextDebugLocation(_currDebugLoc)
                        .CreateInitObj(Pop(), (TypeDesc)inst.Operand!, PopPointerFlags());
                    break;
                case ILCode.Initblk:
                    Emit(new CilIntrinsic.MemSet(numBytes: Pop(), value: Pop(), destPtr: Pop(), flags: PopPointerFlags()));
                    break;
                case ILCode.Cpobj:
                    Emit(new CilIntrinsic.MemCopy(srcPtr: Pop(), destPtr: Pop(), type: (TypeDesc)inst.Operand!, flags: PopPointerFlags()));
                    break;
                case ILCode.Cpblk:
                    Emit(new CilIntrinsic.MemCopy(numBytes: Pop(), srcPtr: Pop(), destPtr: Pop(), flags: PopPointerFlags()));
                    break;
                case ILCode.Sizeof:
                    Push(new CilIntrinsic.SizeOf((TypeDesc)inst.Operand!));
                    break;
                case ILCode.Localloc: {
                    var size = Pop();
                    if (_stack.Count != 0) throw Error("localloc requires an otherwise empty evaluation stack.");
                    Push(new CilIntrinsic.Alloca(size));
                    break;
                }
                case ILCode.Ckfinite:
                    Push(new CilIntrinsic.CheckFinite(value: Pop()));
                    break;
                #endregion

                case ILCode.Ret: ImportRet(); break;
                case ILCode.Dup: ImportDup(); break;
                case ILCode.Pop: ImportPop(); break;

                case ILCode.Nop:
                case ILCode.Break:
                    break;
                    
                default: throw new NotImplementedException("Opcode " + opcode);
            }
            #pragma warning restore format
            // Update prefix
            if (prefix != InstFlags.None) {
                _prefixFlags |= prefix;
            } else {
                _prefixFlags = InstFlags.None;
                _callConstraint = null;
            }
        }
        // Fallthrough the next block
        if (!code[^1].OpCode.IsTerminator()) {
            var succ = AddSucc(code[^1].GetEndOffset());
            TerminateBlock(new BranchInst(succ));
        }
    }

    private void ImportPop()
    {
        Pop();
    }
    private void ImportDup()
    {
        var val = Pop();
        _stack.Push(val);
        _stack.Push(val);
    }

    private void ImportConst(Const cons)
    {
        Push(cons);
    }

    private void ImportVarInst(ref ILInstruction inst)
    {
        var (op, index) = ILImporter.GetVarInstOp(inst.OpCode, inst.Operand);
        var (slot, flags, isBlockLocal) = _importer.GetVarSlot(op, index);

        // Arguments that are only ever loaded don't need variables
        if (!isBlockLocal && slot is Argument) {
            Debug.Assert((flags | op) == (VarFlags.IsArg | VarFlags.Loaded));
            Push(slot);
            return;
        }
        Debug.Assert(isBlockLocal || slot is LocalSlot);

        switch (op & VarFlags.OpMask) {
            case VarFlags.Loaded: {
                if (isBlockLocal) {
                    PushNoEmit(slot);
                } else if (Block.Last is LoadInst prevLoad && prevLoad.Address == slot) {
                    PushNoEmit(prevLoad);
                } else {
                    Push(new LoadInst(slot));
                }
                break;
            }
            case VarFlags.Stored: {
                if (isBlockLocal) {
                    var value = Pop();
                    var type = _importer._ilBody.Locals[index].Type;
                    if (StoreInst.CreateCoercion(type, value) is { } conversion) {
                        Emit(conversion);
                        value = conversion;
                    }
                    _importer.SetBlockLocalVarSlot(index, value);
                } else {
                    var store = new StoreInst(slot, Pop());
                    Emit(store);
                    store.Value = StoreInst.Coerce(store.ElemType, store.Value, store);
                }

                break;
            }
            case VarFlags.AddrTaken: {
                Push(slot);
                break;
            }
            default: throw new UnreachableException();
        }
    }

    private void ImportBinary(BinaryOp op, BinaryOp opFlt = (BinaryOp)(-1))
    {
        var right = Pop();
        var left = Pop();

        if (left.ResultType.StackType == StackType.Float || right.ResultType.StackType == StackType.Float) {
            op = opFlt >= 0 ? opFlt : throw new InvalidProgramException();
        }
        Push(new BinaryInst(op, left, right));
    }

    private void ImportUnary(ILCode code)
    {
        var value = Pop();
        var type = value.ResultType.StackType;

        var op = code switch {
            ILCode.Not => UnaryOp.Not,
            ILCode.Neg => type == StackType.Float ? UnaryOp.FNeg : UnaryOp.Neg
        };
        Push(new UnaryInst(op, value));
    }

    private void ImportConv(TypeDesc dstType, bool checkOverflow = false, bool srcUnsigned = false)
    {
        var value = Pop();
        Push(new ConvertInst(value, dstType, checkOverflow, srcUnsigned));
    }

    private TypeDesc? GetFloatingSinkType(ILInstruction next)
    {
        // conv.r.un produces the CIL evaluation-stack F type. Its immediate
        // conversion/storage determines precision; forcing double first can
        // double-round a uint64-to-single conversion.
        var (operation, index) = ILImporter.GetVarInstOp(next.OpCode, next.Operand);
        if ((operation & VarFlags.Stored) != 0) {
            return (operation & VarFlags.IsLocal) != 0 ? _importer._ilBody.Locals[index].Type : _body.Args[index].ResultType;
        }
        return next.OpCode switch {
            ILCode.Conv_R4 or ILCode.Stind_R4 => PrimType.Single,
            ILCode.Conv_R8 or ILCode.Stind_R8 => PrimType.Double,
            ILCode.Ret => _body.ReturnType,
            ILCode.Stfld or ILCode.Stsfld => ((FieldDesc)next.Operand!).Type,
            ILCode.Stobj => (TypeDesc)next.Operand!,
            ILCode.Call or ILCode.Callvirt or ILCode.Newobj when next.Operand is MethodDesc { ParamSig.Count: > 0 } method
                => method.ParamSig[^1].Type,
            _ => null
        };
    }

    private void ImportCompare(CompareOp op, CompareOp fltOp)
    {
        var right = Pop();
        var left = Pop();
        Push(CreateCompare(op, fltOp, left, right));
    }
    private CompareInst CreateCompare(CompareOp op, CompareOp fltOp, Value left, Value right, bool unsignedNativeExtension = false)
    {
        bool isFloat = left.ResultType.Kind.IsFloat() || right.ResultType.Kind.IsFloat();
        if (!isFloat) {
            // CIL's signed and unsigned comparisons extend mixed int32/native
            // operands differently. Make the extension explicit before a later
            // pass negates or fuses the comparison into a branch.
            var nativeType = unsignedNativeExtension ? PrimType.UIntPtr : PrimType.IntPtr;
            if (left.ResultType.IsPointerLike() && right.ResultType.StackType == StackType.Int) {
                var conversion = new ConvertInst(right, nativeType);
                Emit(conversion);
                right = conversion;
            } else if (right.ResultType.IsPointerLike() && left.ResultType.StackType == StackType.Int) {
                var conversion = new ConvertInst(left, nativeType);
                Emit(conversion);
                left = conversion;
            }
        }
        return new CompareInst(isFloat ? fltOp : op, left, right);
    }

    private void ImportBranch(ref ILInstruction inst)
    {
        AddBranch(ref inst, null);
    }
    private void ImportUnaryBranch(ref ILInstruction inst, CompareOp op, CompareOp fltOp)
    {
        var val = Pop();
        var zero = Const.CreateZero(val.ResultType);
        AddBranch(ref inst, CreateCompare(op, fltOp, val, zero));
    }
    private void ImportBinaryBranch(ref ILInstruction inst, CompareOp op, CompareOp fltOp)
    {
        var right = Pop();
        var left = Pop();
        AddBranch(ref inst, CreateCompare(op, fltOp, left, right,
            unsignedNativeExtension: op is CompareOp.Ne or CompareOp.Ult or CompareOp.Ule or CompareOp.Ugt or CompareOp.Uge));
    }
    private void AddBranch(ref ILInstruction inst, CompareInst? cond = null)
    {
        var thenBlock = AddSucc((int)inst.Operand!);

        if (cond == null) {
            TerminateBlock(new BranchInst(thenBlock));
        } else {
            var elseBlock = AddSucc(inst.GetEndOffset());

            Emit(cond);
            TerminateBlock(new BranchInst(cond, thenBlock, elseBlock));
        }
    }

    private void ImportSwitch(ref ILInstruction inst)
    {
        var value = Pop();
        var defaultTarget = AddSucc(inst.GetEndOffset());

        var targetOffsets = (int[])inst.Operand!;
        var targets = new BasicBlock[targetOffsets.Length];

        for (int i = 0; i < targets.Length; i++) {
            targets[i] = AddSucc(targetOffsets[i]);
        }
        TerminateBlock(new SwitchInst(value, defaultTarget, targets));
    }

    private void ImportLoadLen()
    {
        var array = Pop();
        Push(new CilIntrinsic.ArrayLen(array));
    }
    private void ImportLoadElem(TypeDesc? elemType)
    {
        var addr = EmitArrayAddress(elemType, preserveType: true, readOnly: true);
        Push(new LoadInst(addr));
    }
    private void ImportStoreElem(TypeDesc? elemType)
    {
        var value = Pop();
        var addr = EmitArrayAddress(elemType);

        Emit(new StoreInst(addr, value));
    }
    private void ImportLoadElemAddr(TypeDesc elemType)
    {
        PushNoEmit(EmitArrayAddress(elemType, preserveType: true));
    }
    private AddressInst EmitArrayAddress(TypeDesc? elemType, bool preserveType = false, bool readOnly = false)
    {
        var index = Pop();
        var array = Pop();

        var inst = new ArrayAddrInst(
            array, index, NormalizeStorageType(elemType, array.ResultType, inferUnsigned: !preserveType),
            inBounds: HasPrefix(InstFlags.NoRangeCheck | InstFlags.NoNullCheck),
            readOnly: readOnly || HasPrefix(InstFlags.Readonly));
        Emit(inst);
        return inst;
    }

    private void ImportLoadInd(TypeDesc? type)
    {
        var addr = Pop();

        Push(new LoadInst(addr, NormalizeStorageType(type, addr.ResultType, inferUnsigned: false), PopPointerFlags()));
    }
    private void ImportStoreInd(TypeDesc? type)
    {
        var value = Pop();
        var addr = Pop();

        Emit(new StoreInst(addr, value, NormalizeStorageType(type, addr.ResultType), PopPointerFlags()));
    }
    private PointerFlags PopPointerFlags()
    {
        var flags = PointerFlags.None;
        if (HasPrefix(InstFlags.Unaligned)) flags |= PointerFlags.Unaligned;
        if (HasPrefix(InstFlags.Volatile)) flags |= PointerFlags.Volatile;
        return flags;
    }
    private static TypeDesc NormalizeStorageType(TypeDesc? type, TypeDesc actualArrayOrPtrType, bool inferUnsigned = true)
    {
        if (type == null) {
            // ld/st .ref variants get null type. Infer from storage type or fallback to `object`.
            type = actualArrayOrPtrType.ElemType is { StackType: StackType.Object } element ? element : PrimType.Object;
        }
        // Primitive type definitions (from CoreLib) and aliases (PrimTypes) can be
        // used interchangeably. The only difference between them is that the
        // definitions provide actual metadata such as methods and interfaces.
        // We'll normalize them to the aliases here for consistency.
        if (type is TypeDef def && PrimType.GetFromDefinition(def) is { } alias) {
            type = alias;
        }
        // Stores have no unsigned variants. Loads must retain their opcode's
        // sign/zero extension, even when the underlying storage has another type.
        var kind = type.Kind;
        var actualKind = actualArrayOrPtrType.ElemType?.Kind ?? TypeKind.Void;

        if (inferUnsigned && kind != actualKind && actualKind.IsUnsigned() && kind.GetUnsigned() == actualKind) {
            type = PrimType.GetFromKind(kind.GetUnsigned());
        }
        return type;
    }

    private void ImportLoadField(FieldDesc field, bool isStatic)
    {
        var flags = PopPointerFlags();
        var obj = isStatic ? null : Pop();
        // ldfld may legally name a static field; its receiver is consumed but ignored.
        if (!field.IsInstance) obj = null;

        // A native integer loaded from memory is an address, not a struct value.
        // Removing that load would access the pointer's storage instead of its target.
        if (obj != null && obj.ResultType.IsValueType && !obj.ResultType.IsPointerLike()) {
            // Loaded structs are snapshots, even when their original storage
            // is subsequently modified. Do not recover that storage's address.
            if (flags == 0) {
                Push(new FieldExtractInst(field, obj));
                return;
            }
            var copy = _body.CreateVar(obj.ResultType, "fieldReceiver");
            Emit(new StoreInst(copy, obj));
            obj = copy;
        }
        var addr = new FieldAddrInst(field, obj);
        Emit(addr);
        Push(new LoadInst(addr, flags: flags));
    }
    private void ImportStoreField(FieldDesc field, bool isStatic)
    {
        var value = Pop();
        var addr = EmitFieldAddr(field, isStatic);
        Emit(new StoreInst(addr, value, flags: PopPointerFlags()));
    }
    private void ImportFieldAddr(FieldDesc field, bool isStatic)
    {
        PushNoEmit(EmitFieldAddr(field, isStatic));
    }
    private FieldAddrInst EmitFieldAddr(FieldDesc field, bool isStatic)
    {
        var obj = isStatic ? null : Pop();
        if (!field.IsInstance) obj = null;
        
        if (obj != null && obj.ResultType.IsValueType && !obj.ResultType.IsPointerLike()) {
            throw Error("Field address or store requires an addressable receiver.");
        }
        var inst = new FieldAddrInst(field, obj);
        Emit(inst);
        return inst;
    }

    private void ImportCall(MethodDesc method, bool isVirt)
    {
        var args = PopCallArgs(method);
        var constraint = HasPrefix(InstFlags.Constrained) ? _callConstraint : null;
        var inst = new CallInst(method, args, isVirt, constraint);
        inst.IsTailCall = HasPrefix(InstFlags.Tailcall);
        inst.IsReadOnly = HasPrefix(InstFlags.Readonly);
        Push(inst);
    }
    private void ImportLoadFuncPtr(MethodDesc method, bool isVirt)
    {
        var obj = isVirt ? Pop() : null;
        Push(new FuncAddrInst(method, obj, HasPrefix(InstFlags.Constrained) ? _callConstraint : null));
    }

    private void ImportCallIndirect(FuncPtrType type)
    {
        if (type.Signature.CallConv == CallConvention.VarArgs || type.Signature.IsGeneric) {
            throw new NotSupportedException("Vararg and generic calli signatures are not supported");
        }
        var function = Pop();
        var args = new Value[type.Signature.NumCallArgs];
        for (int i = args.Length - 1; i >= 0; i--) args[i] = Pop();
        Push(new CilIntrinsic.CallIndirect(type, function, args) { IsTailCall = HasPrefix(InstFlags.Tailcall) });
    }

    private void ImportNewObj(MethodDesc ctor)
    {
        var args = PopCallArgs(ctor, true);
        Push(new NewObjInst(ctor, args));
    }

    private Value[] PopCallArgs(MethodDesc method, bool ctor = false)
    {
        var args = new Value[method.ParamSig.Count - (ctor ? 1 : 0)];
        for (int i = args.Length - 1; i >= 0; i--) {
            args[i] = Pop();
        }
        return args;
    }

    private void ImportRet()
    {
        bool isVoid = _body.ReturnType.Kind == TypeKind.Void;
        var value = isVoid ? null : Pop();
        if (_stack.Count != 0) throw Error("Return requires an otherwise empty evaluation stack.");

        Emit(new ReturnInst(value));
    }

    private void ImportLeave(int targetOffset)
    {
        var currRegion = _importer._regionTree?.FindEnclosing(StartOffset);
        var destRegion = _importer._regionTree?.FindEnclosing(targetOffset);
        var targetBlock = _importer.GetBranchTarget(StartOffset, targetOffset);

        // CIL leave may also branch within a region or enter the beginning of
        // a disjoint try. Only the regions outside the common ancestor unwind.
        var ancestors = new HashSet<RegionNode>();
        for (var region = currRegion; region != null; region = region.Parent) ancestors.Add(region);
        while (destRegion != null && !ancestors.Contains(destRegion)) {
            if (destRegion.Kind != RegionKind.Protected || destRegion.StartOffset != targetOffset) {
                throw Error("Leave target is not an enclosing region or a protected entry.");
            }
            destRegion = destRegion.Parent;
        }
        if (currRegion == destRegion) {
            TerminateBlock(new BranchInst(targetBlock), clearStack: true);
            return;
        }

        // Build the continuation from the outermost region inward, so each leave
        // exits exactly one region. Finally resumes must name these immediate
        // continuations, including those introduced for enclosing regions.
        var parents = new List<RegionNode>();
        for (var parent = currRegion!.Parent; parent != destRegion; parent = parent.Parent) {
            if (parent == null) throw Error("Leave target is not in an enclosing region.");
            parents.Add(parent);
        }
        for (int i = parents.Count - 1; i >= 0; i--) {
            var parent = parents[i];
            parent.LeavingBlocks ??= new();

            if (!parent.LeavingBlocks.TryGetValue(targetBlock, out var exitBlock)) {
                exitBlock = _body.CreateBlock(insertAfter: Block);
                exitBlock.InsertLast(new LeaveInst(targetBlock));
                parent.LeavingBlocks.Add(targetBlock, exitBlock);
                parent.ExitTargets.Add(targetBlock);
            }
            targetBlock = exitBlock;
        }
        currRegion.ExitTargets.Add(targetBlock);
        TerminateBlock(new LeaveInst(targetBlock), clearStack: true);
    }
    private void ImportResume(bool isFromFilter)
    {
        var filterResult = isFromFilter ? Pop() : null;
        if (_stack.Count != 0) throw Error("Exception handler termination requires an otherwise empty evaluation stack.");
        TerminateBlock(new ResumeInst([], filterResult), clearStack: true);
        
        _importer._blocksEndingWithEhResume.Add(this);
    }

    private void ImportThrow(bool isRethrow)
    {
        var exception = isRethrow ? null : Pop();
        TerminateBlock(new ThrowInst(exception), clearStack: true);
    }

    private Exception Error(string? msg = null)
    {
        return new InvalidProgramException(msg);
    }
}

[Flags]
internal enum InstFlags
{
    None            = 0,

    Unaligned       = 1 << 0,
    Volatile        = 1 << 1,
    Tailcall        = 1 << 2,
    Constrained     = 1 << 3,
    Readonly        = 1 << 4,

    // Bits [16..23] are reserved for `no.` prefix
    NoPrefixShift_  = 16,
    NoTypeCheck     = 1 << 16,
    NoRangeCheck    = 1 << 17,
    NoNullCheck     = 1 << 18,
}
[Flags]
internal enum VarFlags
{

    None        = 0,

    // Should not be combined
    IsArg       = 1 << 0,

    IsLocal     = 1 << 1,


    Loaded      = 1 << 2,

    Stored      = 1 << 3,

    AddrTaken   = 1 << 4,

    OpMask = Loaded | Stored | AddrTaken,


    CrossesBlock    = 1 << 5

}
