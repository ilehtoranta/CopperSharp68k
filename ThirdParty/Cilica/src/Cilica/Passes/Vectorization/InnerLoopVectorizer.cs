namespace Cilica.Passes.Vectorization;

using Cilica.Analysis;
using Cilica.IR.Utils;

internal sealed class InnerLoopVectorizer
{
    readonly CountingLoopInfo _loop;
    readonly VectorTranslator _trans;
    readonly Dictionary<Instruction, InstMapping> _mappings = new();
    readonly Dictionary<PhiInst, (Value Seed, Instruction IterOut, PhiInst NewPhi, VectorOp Op)> _reductionPhis = new();

    readonly BasicBlock _bodyBlock;
    Value _newCounter = null!;
    IRBuilder _vectorPreHeader = null!;
    readonly Dictionary<(VectorShape Shape, Value Scalar), Value> _invariants = new();
    readonly Dictionary<VectorShape, Value> _counterOffsets = new();
    readonly Dictionary<Value, Value> _arrayBases = new();
    int _elemBitWidth;
    int _width;

    private InnerLoopVectorizer(CountingLoopInfo loop, VectorTranslator translator)
    {
        _loop = loop;
        _trans = translator;
        _bodyBlock = loop.Latch;
    }

    public static bool Run(CountingLoopInfo loop, VectorTranslator trans, IReadOnlyList<Value> boundsChecks, ICompilationLogger? reportLogger)
    {
        // Only consider loops with a single body block (the latch).
        if (loop.Loop.NumBlocks != 2) {
            return false;
        }

        // Supported induction forms:
        //  for (int i = ...; i < bound; i++)
        //  for (ptr p = ...; p < bound; p = lea p + 1)
        if (!IsSequentialLoop()) {
            return false;
        }

        var vectorizer = new InnerLoopVectorizer(loop, trans);

        if (vectorizer.BuildMappings(reportLogger) && vectorizer.CheckElementWidth(reportLogger)) {
            vectorizer.EmitVectorizedLoop(boundsChecks);
            reportLogger?.Trace($"Vectorized loop {loop.Header}: {vectorizer._elemBitWidth} bits element width, {vectorizer._reductionPhis.Count} reductions, {boundsChecks.Count} guarded arrays.");

            return true;
        }

        return false;

        bool IsSequentialLoop()
        {
            return loop.ExitCondition.Op is CompareOp.Slt or CompareOp.Ult &&
                   loop.IsInvariant(loop.End) &&
                   (loop.UpdatedCounter is BinaryInst { Op: BinaryOp.Add, Right: ConstInt { Value: 1 } } ||
                   (loop.UpdatedCounter is PtrOffsetInst { BasePtr: var basePtr, Index: ConstInt { Value: 1 }, KnownStride: true } && basePtr == loop.Counter));
        }
    }

    private bool BuildMappings(ICompilationLogger? logger)
    {
        foreach (var phi in _loop.Header.Phis()) {
            if (phi == _loop.Counter) {
                continue;
            }

            if (phi.GetValue(_bodyBlock) is not Instruction iterOut || iterOut.Block != _bodyBlock) {
                logger?.Trace($"Loop {_loop.Header} vectorization declined: reduction {phi} is not updated in the body block.");
                return false;
            }

            if (phi.Users().Any(u => _loop.Contains(u.Block) && u != iterOut) ||
                iterOut.Users().Any(u => _loop.Contains(u.Block) && u != phi)) {
                logger?.Trace($"Loop {_loop.Header} vectorization declined: intermediate reduction values are used in the loop.");
                return false;
            }

            var op = _trans.GetVectorOp(iterOut).Op;

            if (!IsSupportedReductionOp(op)) {
                logger?.Trace($"Loop {_loop.Header} vectorization declined: unsupported reduction operation {iterOut}.");
                return false;
            }

            _reductionPhis.Add(phi, new() {
                Seed = phi.GetValue(_loop.PreHeader),
                IterOut = iterOut,
                Op = op
            });
        }

        foreach (var inst in _bodyBlock) {
            switch (inst) {
                case ArrayAddrInst: {
                    // Legality has proved the full range or requested a runtime guard.
                    _mappings.Add(inst, new());
                    break;
                }
                case PtrOffsetInst addr: {
                    if ((!_loop.IsInvariant(addr.BasePtr) || addr.Index != _loop.Counter) && addr.BasePtr is not PhiInst) {
                        logger?.Trace($"Loop {_loop.Header} vectorization declined: unsupported pointer addressing '{addr}'.");

                        return false;
                    }

                    _mappings.Add(inst, new());
                    break;
                }
                case MemoryInst acc: {
                    if (!VectorShape.IsSupportedElemType(acc.ElemType)) {
                        logger?.Trace($"Loop {_loop.Header} vectorization declined: unsupported memory access type '{acc.ElemType}'.");

                        return false;
                    }

                    _mappings.Add(inst, new InstMapping() {
                        TargetOp = inst is LoadInst ? VectorOp.Load : VectorOp.Store,
                        ElemType = acc.ElemType.Kind,
                    });
                    break;
                }
                default: {
                    // Ignore branch or loop counter update
                    if (inst is BranchInst || inst == _loop.UpdatedCounter) {
                        break;
                    }

                    var (op, elemType) = _trans.GetVectorOp(inst);

                    if (op == VectorOp.Invalid) {
                        logger?.Trace($"Loop {_loop.Header} vectorization declined: unsupported instruction '{inst}'.");

                        return false;
                    }

                    if (inst.ResultType == PrimType.Bool) {
                        elemType = GetConditionalElemType(inst);

                        if (elemType == TypeKind.Void) {
                            logger?.Trace($"Loop {_loop.Header} vectorization declined: conditional operands have incompatible element widths at {inst}.");

                            return false;
                        }

                        if (!inst.Users().All(u => IsSupportedConditionalUse(u, elemType))) {
                            logger?.Trace($"Loop {_loop.Header} vectorization declined: unsupported use of boolean result {inst}.");

                            return false;
                        }
                    }

                    if (IsPredicatedCountStep(inst)) {
                        var rhs = (Instruction)inst.Operands[1];
                        elemType = _mappings.GetRef(rhs).ElemType;
                        op = VectorOp._PredicatedCount;
                    }

                    _mappings.Add(inst, new InstMapping() {
                        TargetOp = op,
                        ElemType = elemType
                    });
                    break;
                }
            }
        }

        return true;
    }

    private bool CheckElementWidth(ICompilationLogger? logger)
    {
        // Check if all mappings have the same scalar width
        int commonElemSize = 0;

        foreach (var inst in _mappings.Keys) {
            ref var mapping = ref _mappings.GetRef(inst);

            if (mapping.ElemType == TypeKind.Void) {
                continue;
            }

            int size = mapping.ElemType.BitSize();

            if (commonElemSize == 0) {
                commonElemSize = size;
            } else if (commonElemSize != size) {
                logger?.Trace($"Loop {_loop.Header} vectorization declined: mixed element widths ({commonElemSize} and {size} bits).");
                return false;
            }
        }

        if (commonElemSize == 0) {
            return false;
        }

        _elemBitWidth = commonElemSize;

        return true;
    }

    private void EmitVectorizedLoop(IReadOnlyList<Value> boundsChecks)
    {
        var wideLoop = new LoopBuilder(_loop.PreHeader, "Vec256_");
        var narrowLoop = new LoopBuilder(wideLoop.Exit.Block, "Vec128_");
        var scalarEntry = narrowLoop.Exit;
        BasicBlock? fallback = null;

        if (boundsChecks.Count != 0) {
            scalarEntry = new IRBuilder(_loop.Header.Method.CreateBlock(insertAfter: narrowLoop.Exit.Block).SetName("Vec_Scalar"));
            fallback = _loop.Header.Method.CreateBlock(insertAfter: scalarEntry.Block).SetName("Vec_Fallback");
            fallback.SetBranch(scalarEntry.Block);

            var guard = new IRBuilder(_loop.PreHeader);
            // Short and empty loops need neither bounds checks nor vector setup.
            guard.ForkIf(guard.CreateSlt(_loop.End, ConstInt.CreateI(128 / _elemBitWidth)), fallback);

            foreach (var array in boundsChecks) {
                // Keep length reads behind null checks. A failed check must run the
                // original loop so earlier scalar stores and exceptions stay ordered.
                guard.ForkIf(guard.CreateEq(array, ConstNull.Create()), fallback);
                var length = guard.CreateConvert(guard.CreateArrayLen(array), PrimType.Int32);
                guard.ForkIf(guard.CreateUgt(_loop.End, length), fallback);
            }
            guard.SetBranch(wideLoop.EntryBlock);
        } else {
            _loop.PreHeader.SetBranch(wideLoop.EntryBlock);
        }

        // Each width consumes the remaining iterations and passes scalar reductions onward.
        var reductions = _reductionPhis.ToDictionary(pair => pair.Key, pair => pair.Value.Seed);
        var counter = EmitVectorLoop(wideLoop, 256, _loop.Start, reductions);
        wideLoop.Exit.SetBranch(narrowLoop.EntryBlock);
        counter = EmitVectorLoop(narrowLoop, 128, counter, reductions);

        foreach (var (phi, data) in _reductionPhis) {
            phi.RedirectArg(_loop.PreHeader, scalarEntry.Block, MergeScalarInput(data.Seed, reductions[phi]));
        }
        _loop.Counter.RedirectArg(_loop.PreHeader, scalarEntry.Block, MergeScalarInput(_loop.Start, counter));

        if (fallback != null) narrowLoop.Exit.SetBranch(scalarEntry.Block);
        scalarEntry.SetBranch(_loop.Header);

        Value MergeScalarInput(Value seed, Value vectorResult)
            => fallback == null
                ? vectorResult
                : scalarEntry.CreatePhi(seed.ResultType, (fallback, seed), (narrowLoop.Exit.Block, vectorResult));
    }

    private Value EmitVectorLoop(LoopBuilder newLoop, int bitWidth, Value start, Dictionary<PhiInst, Value> reductions)
    {
        _width = bitWidth / _elemBitWidth;
        _vectorPreHeader = newLoop.PreHeader;
        int stepSize = _loop.UpdatedCounter is PtrOffsetInst lea ? _width * lea.Stride : _width;

        _newCounter = newLoop.CreateAccum(
            seed: start,
            emitUpdate: currVal => {
                var step = ConstInt.CreateI(_width);

                return _loop.UpdatedCounter is PtrOffsetInst lea
                    ? newLoop.Latch.CreatePtrOffset(currVal, step, lea.ElemType)
                    : newLoop.Latch.CreateAdd(currVal, step);
            }
        ).SetName("v_idx");

        // Create reduction accumulator phis
        foreach (var phi in _reductionPhis.Keys) {
            CreateReductionPhi(newLoop, phi, reductions[phi]);
        }

        // Emit loop and widen instructions
        newLoop.Build(
            emitCond: builder => {
                if (_loop.UpdatedCounter is PtrOffsetInst) {
                    var remBytes = builder.CreateSub(_loop.End, _newCounter);

                    return builder.CreateSge(remBytes, ConstInt.CreateI(stepSize));
                }

                var remaining = builder.CreateSub(builder.CreateConvert(_loop.End, PrimType.Int64),
                    builder.CreateConvert(_newCounter, PrimType.Int64));

                return builder.CreateSge(remaining, ConstInt.CreateL(_width));
            },
            emitBody: builder => {
                foreach (var inst in _mappings.Keys) {
                    ref var mapping = ref _mappings.GetRef(inst);
                    mapping.ClonedDef = VectorizeInst(newLoop, inst, ref mapping);
                }
            },
            // Keep the hardware intrinsic in its branch expression for JIT folding.
            emitGuard: builder => _trans.EmitHardwareCheck(builder, bitWidth)
        );

        // Finalize reduction phis
        foreach (var (phi, data) in _reductionPhis) {
            Value exitValue;

            if (_trans.IsVectorShape(data.NewPhi.ResultType)) {
                var vecType = new VectorShape(phi.ResultType, _width);
                exitValue = _trans.EmitReduction(newLoop.Exit, vecType, data.Op, data.NewPhi);
            } else {
                exitValue = data.NewPhi;
            }

            var next = _mappings.GetRef(data.IterOut).ClonedDef!;
            if (_trans.IsVectorShape(next.ResultType) && next.ResultType != data.NewPhi.ResultType) {
                next = _trans.EmitBitcast(newLoop.Latch, new VectorShape(next.ResultType.GenericParams[0], _width), phi.ResultType.Kind, next);
            }
            data.NewPhi.SetValue(newLoop.Latch.Block, next);
            reductions[phi] = exitValue;
        }

        return _newCounter;
    }

    private Value VectorizeInst(LoopBuilder newLoop, Instruction inst, ref InstMapping mapping)
    {
        if (inst is ArrayAddrInst arrayAddress) {
            if (!_arrayBases.TryGetValue(arrayAddress.Array, out var basePtr)) {
                basePtr = LoopStrengthReduction.CreateGetDataPtrRange(_vectorPreHeader, arrayAddress.Array, getCount: false).BasePtr;
                _arrayBases.Add(arrayAddress.Array, basePtr);
            }

            return newLoop.Body.CreatePtrOffset(basePtr, _newCounter, arrayAddress.ElemType);
        }

        if (inst is PtrOffsetInst addr) {
            Debug.Assert(!addr.KnownStride || addr.Stride == addr.ElemType.Kind.Size());

            return newLoop.Body.CreatePtrOffset(
                GetScalarMapping(addr.BasePtr),
                GetScalarMapping(addr.Index),
                addr.ElemType
            );
        }

        Debug.Assert(mapping.TargetOp != VectorOp.Invalid);

        var vecType = new VectorShape(mapping.ElemType, _width);
        var args = new Value[inst.Operands.Length];

        for (int i = 0; i < args.Length; i++) {
            var arg = inst.Operands[i];
            var argumentType = mapping.TargetOp == VectorOp.I2F
                ? new VectorShape(arg.ResultType, _width)
                : vecType;
            args[i] = i == 1 && mapping.TargetOp is VectorOp.Shl or VectorOp.Shra or VectorOp.Shrl
                ? GetScalarMapping(arg)
                : GetVectorMapping(newLoop.Body, argumentType, arg);
        }

        return _trans.EmitOp(newLoop.Body, vecType, mapping.TargetOp, args);
    }

    private Value GetVectorMapping(IRBuilder builder, VectorShape vecType, Value scalar)
    {
        if (scalar == _loop.UpdatedCounter && _loop.Counter.ResultType.IsInt()) {
            var current = GetVectorMapping(builder, vecType, _loop.Counter);

            return _trans.EmitOp(builder, vecType, VectorOp.Add, current,
                _trans.EmitOp(builder, vecType, VectorOp.Splat, ConstInt.Create(vecType.ElemType, 1)));
        }

        if (scalar is Instruction inst && inst.Block == _bodyBlock) {
            var value = _mappings.GetRef(inst).ClonedDef ?? throw new InvalidOperationException();

            if (_trans.IsVectorShape(value.ResultType)) {
                var actual = new VectorShape(value.ResultType.GenericParams[0], _width);

                return _trans.EmitBitcast(builder, actual, vecType.ElemKind, value);
            }

            return value;
        }

        if (scalar is PhiInst phi && _reductionPhis.ContainsKey(phi)) {
            var value = _reductionPhis.GetRef(phi).NewPhi;
            return _trans.IsVectorShape(value.ResultType)
                ? _trans.EmitBitcast(builder, new VectorShape(phi.ResultType, _width), vecType.ElemKind, value)
                : value;
        }

        if (scalar == _loop.Counter && scalar.ResultType.IsInt()) {
            if (!_counterOffsets.TryGetValue(vecType, out var offsets)) {
                var seqOffsets = Enumerable.Range(0, vecType.Count)
                    .Select(i => ConstInt.Create(vecType.ElemType, i)).ToArray();
                offsets = _trans.EmitOp(_vectorPreHeader, vecType, VectorOp.Pack, seqOffsets);
                _counterOffsets.Add(vecType, offsets);
            }

            // v_idx = [idx, idx, ...] + [0, 1, ...]
            return _trans.EmitOp(builder, vecType, VectorOp.Add,
                _trans.EmitOp(builder, vecType, VectorOp.Splat, _newCounter),
                offsets
            );
        }

        if (scalar == _loop.Counter) {
            Debug.Assert(_loop.Counter.ResultType.IsPointerLike());

            return _newCounter;
        }

        if (_loop.IsInvariant(scalar)) {
            if (_invariants.TryGetValue((vecType, scalar), out var invariant)) {
                return invariant;
            }

            if (scalar.ResultType.Kind == TypeKind.Bool) {
                var maskType = vecType.ElemType.IsFloat()
                    ? new VectorShape(vecType.ElemKind == TypeKind.Single ? TypeKind.Int32 : TypeKind.Int64, vecType.Count)
                    : vecType;
                var bits = _vectorPreHeader.CreateSelect(scalar, ConstInt.Create(maskType.ElemType, -1), ConstInt.Create(maskType.ElemType, 0));
                var mask = _trans.EmitOp(_vectorPreHeader, maskType, VectorOp.Splat, bits);
                invariant = _trans.EmitBitcast(_vectorPreHeader, maskType, vecType.ElemKind, mask);
            } else {
                invariant = _trans.EmitOp(_vectorPreHeader, vecType, VectorOp.Splat, scalar);
            }

            _invariants.Add((vecType, scalar), invariant);

            return invariant;
        }

        throw new UnreachableException();
    }

    private Value GetScalarMapping(Value val)
    {
        if (val == _loop.Counter) {
            return _newCounter;
        }

        Debug.Assert(_loop.IsInvariant(val));

        return val;
    }

    private void CreateReductionPhi(LoopBuilder newLoop, PhiInst phi, Value scalarSeed)
    {
        ref var data = ref _reductionPhis.GetRef(phi);
        var vecType = new VectorShape(phi.ResultType, _width);

        var seed = data.Op switch {
            // For bool sums, it's easier to use scalar phi + movemask
            VectorOp.Add when IsPredicatedCountStep(data.IterOut) => scalarSeed,
            // Additive: [seed, 0, 0, ...]
            VectorOp.Add or VectorOp.Or or VectorOp.Xor => EmitAccumOpSeed(scalarSeed, 0),
            // Multiplicative: [seed, 1, 1, ...]
            VectorOp.Mul => EmitAccumOpSeed(scalarSeed, 1),
            // Exclusive: [seed, seed, ...]
            VectorOp.And or VectorOp.Min or VectorOp.Max => _trans.EmitOp(newLoop.PreHeader, vecType, VectorOp.Splat, scalarSeed),
        };
        data.NewPhi = newLoop.CreateAccum(seed, currVal => currVal);

        Value EmitAccumOpSeed(Value seed, int identity)
        {
            var identityConst = seed.ResultType.IsFloat()
                ? ConstFloat.Create(seed.ResultType, identity)
                : ConstInt.Create(seed.ResultType, identity) as Const;

            var args = new Value[_width];
            args[0] = seed;
            args.AsSpan(1..).Fill(identityConst);

            return _trans.EmitOp(newLoop.PreHeader, vecType, VectorOp.Pack, args);
        }
    }

    // Checks if we know how to vectorize a reduction with the given accum op
    private static bool IsSupportedReductionOp(VectorOp op)
    {
        return op is
            VectorOp.Add or VectorOp.Mul or
            VectorOp.And or VectorOp.Or or VectorOp.Xor or
            VectorOp.Min or VectorOp.Max;
    }

    // Returns the nominal element type for some conditional (e.g. `cmp i32, i32` -> i32)
    private TypeKind GetConditionalElemType(Instruction inst)
    {
        if (inst is CompareInst) {
            return _trans.GetVectorOp(inst).ElemType;
        }

        // Widen conditionals like ((cmp x, y) | (cmp z, w)) to their comparand scalar widths
        if (inst is BinaryInst) {
            var type = TypeKind.Void;

            if (inst.Operands[0] is Instruction lhs && _mappings.ContainsKey(lhs)) {
                type = _mappings.GetRef(lhs).ElemType;
            }

            if (inst.Operands[1] is Instruction rhs && _mappings.ContainsKey(rhs)) {
                var otherType = _mappings.GetRef(rhs).ElemType;

                if (type == TypeKind.Void) {
                    type = otherType;
                } else if (type.Size() != otherType.Size()) {
                    return TypeKind.Void;
                }
            }

            return type;
        }

        return TypeKind.Void;
    }

    // Checks if we know how to vectorize a conditional (inst returning bool) with the given user
    private static bool IsSupportedConditionalUse(Instruction user, TypeKind nominalElemType)
    {
        return
            (user is SelectInst sel && sel.ResultType.Kind.Size() == nominalElemType.Size()) ||
            (user is BinaryInst { Op: BinaryOp.And or BinaryOp.Or or BinaryOp.Xor, ResultType.Kind: TypeKind.Bool }) ||
            IsPredicatedCountStep(user);
    }

    // Checks if `inst` is an `add (int32)phi, (bool)cond`.
    private static bool IsPredicatedCountStep(Instruction inst)
    {
        return inst is BinaryInst {
            Op: BinaryOp.Add,
            Left: PhiInst phi,
            Right.ResultType.Kind: TypeKind.Bool,
            NumUses: 1
        } bin && inst.Users().First() == phi;
    }

    private struct InstMapping
    {
        public Value? ClonedDef;
        public VectorOp TargetOp;
        public TypeKind ElemType;
    }
}
