namespace Cilica.IR.Utils;

public class IRCloner
{
    // Mapping from old to new (clonned) values
    readonly Dictionary<Value, Value> _mappings = new();
    // Values that must be remapped and replaced last (they depend on defs in an unprocessed block).
    readonly RefSet<PendingClone> _pendingClones = new();
    readonly InstCloner _instCloner;
    readonly List<BasicBlock> _blocks = new();
    protected readonly GenericContext _genericContext;
    protected readonly MethodBody _destMethod;
    readonly bool _foldConstants;

    public IRCloner(MethodBody method, GenericContext genericContext = default, bool foldConstants = true)
    {
        _destMethod = method;
        _instCloner = new(this);
        _genericContext = genericContext;
        _foldConstants = foldConstants;
    }

    public static MethodBody CloneBody(MethodBody source)
        => CloneBody(source, out _);

    internal static MethodBody CloneBody(MethodBody source, out IRCloner cloner)
    {
        var copy = new MethodBody(source.Definition) { InitLocals = source.InitLocals };
        cloner = new IRCloner(copy, foldConstants: false);

        for (int i = 0; i < source.Args.Length; i++) {
            cloner.AddMapping(source.Args[i], copy.Args[i]);
        }

        foreach (var block in source) {
            cloner.AddMapping(block, copy.CreateBlock());
        }

        cloner.Run(source.EntryBlock);

        return copy;
    }

    public void AddMapping(Value oldVal, Value newVal)
    {
        _mappings.Add(oldVal, newVal);

        if (oldVal is BasicBlock oldBlock) {
            Ensure.That(newVal is BasicBlock);
            Debug.Assert(((BasicBlock)newVal).Method == _destMethod);
            
            _blocks.Add(oldBlock);
        }
    }

    public BasicBlock GetMapping(BasicBlock originalBlock) => (BasicBlock)_mappings[originalBlock];
    public Value GetMapping(Value originalValue) => _mappings[originalValue];

    /// <summary> Clones all blocks that are reachable from <paramref name="entry"/>. </summary>
    /// <remarks> Destination blocks must be created and mapped beforehand via <see cref="AddMapping(Value, Value)"/>. </remarks>
    public void Run(BasicBlock entry)
    {
        Ensure.That(_mappings.ContainsKey(entry));

        var worklist = new DiscreteStack<BasicBlock>(entry);

        while (worklist.TryPop(out var block)) {
            var clonedBlock = GetMapping(block);
            
            foreach (var inst in block) {
                var clonedInst = Clone(inst);
                // Clone() may fold constants: `add r10, 0` -> `r10`,
                // so we can only insert a inst if it isn't already in a block.
                if (clonedInst is Instruction { Block: null } newInst) {
                    clonedBlock.InsertLast(newInst);
                }
            }

            // Resume targets describe exits taken by leaves, not independent
            // paths. Following them could keep a folded-away exit reachable.
            if (block.Last is ResumeInst) continue;

            // If the cloned branch gets folded, we should only push reachable succs.
            var newSuccs = _foldConstants && ConstFolding.FoldBlockBranch(clonedBlock)
                ? [..clonedBlock.Succs]
                : default(HashSet<BasicBlock>);

            foreach (var succ in block.Succs) {
                if (newSuccs != null && !newSuccs.Contains(GetMapping(succ))) continue;

                worklist.Push(succ);
            }
        }

        ControlFlowUtils.RemoveUnreachableBlocks(GetMapping(entry), _blocks.Select(GetMapping));

        // At this point the only pending values should be from phi uses for
        // unreachable pred blocks. Check that they're no longer used.
        foreach (var pending in _pendingClones) {
            Ensure.That(pending.NumUses == 0);
        }
    }

    /// <summary> Clones or folds the given instruction. </summary>
    public Value Clone(Instruction inst)
    {
        var clonedInst = CreateClone(inst);

        if (clonedInst.HasResult) {
            ref var mapping = ref _mappings.GetOrAddRef(inst, out bool exists);

            if (exists) {
                if (mapping is not PendingClone pending) {
                    throw new InvalidOperationException("Cloned instruction with an already existing mapping");
                }
                pending.ReplaceUses(clonedInst);
                _pendingClones.Remove(pending);
            }
            mapping = clonedInst;
        } else {
            _mappings.Add(inst, clonedInst);
        }
        return clonedInst;
    } 

    protected virtual Value CreateClone(Instruction inst)
    {
        return _instCloner.Clone(inst);
    }

    /// <summary> Checks if the given cloned block is unreachable and was removed. </summary>
    public bool IsDead(BasicBlock clonedBlock)
    {
        return clonedBlock.Method == null;
    }

    protected Value Remap(Value value)
    {
        if (_mappings.TryGetValue(value, out var newValue)) {
            return newValue;
        }
        if (value is LocalSlot var) {
            var newType = (TypeDesc)Remap(var.Type);
            newValue = _destMethod.CreateVar(newType, pinned: var.IsPinned, hardExposed: var.IsHardExposed);
            _mappings.Add(value, newValue);
            return newValue;
        }
        if (value is Const or Undef) {
            return value;
        }
        var inst = (Instruction)value;
        var pending = new PendingClone(inst, GetRemappedType(inst));
        _mappings.Add(value, pending);
        _pendingClones.Add(pending);
        return pending;
    }
    protected EntityDesc Remap(EntityDesc entity)
    {
        if (_genericContext.IsNull) {
            return entity;
        }
        return entity switch {
            TypeDesc c => c.GetSpec(_genericContext),
            MethodDesc c => c.GetSpec(_genericContext),
            FieldDesc c => c.GetSpec(_genericContext)
        };
    }

    /// <summary> This method must be overriden if the derived cloner may change result types of cloned instructions, as some refs cannot be cloned ahead of time. </summary>
    protected virtual TypeDesc GetRemappedType(Instruction inst)
    {
        return (TypeDesc)Remap(inst.ResultType);
    }

    protected sealed class PendingClone : TrackedValue
    {
        public readonly Instruction Source;
        public PendingClone(Instruction source, TypeDesc type) => (Source, ResultType) = (source, type);
        public override void Print(PrintContext ctx) => ctx.Print($"PendingClone({Source.ToString()})");
    }

    sealed class InstCloner : InstVisitor
    {
        readonly IRCloner _ctx;
        Value _result = null!;

        public InstCloner(IRCloner ctx) => _ctx = ctx;

        public Value Clone(Instruction inst)
        {
            inst.Accept(this);
            
            if (_result is Instruction clonedInst) {
                clonedInst.DebugLocation = inst.DebugLocation;
            }
            return _result;
        }

        private void Out(Value val) => _result = val;

        private V Remap<V>(V val) where V : Value
            => (V)(_ctx.Remap(val) ?? val);

        private TypeDesc Remap(TypeDesc val) => (TypeDesc)_ctx.Remap(val);
        private MethodDesc Remap(MethodDesc val) => (MethodDesc)_ctx.Remap(val);
        private FieldDesc Remap(FieldDesc val) => (FieldDesc)_ctx.Remap(val);

        private Value[] RemapArgs(ReadOnlySpan<Value> args)
        {
            var newArgs = new Value[args.Length];
            for (int i = 0; i < args.Length; i++) {
                newArgs[i] = Remap(args[i]);
            }
            return newArgs;
        }
        private EntityDesc[] RemapEntities(ReadOnlySpan<EntityDesc> args)
        {
            var newArgs = new EntityDesc[args.Length];
            for (int i = 0; i < args.Length; i++) {
                newArgs[i] = _ctx.Remap(args[i]);
            }
            return newArgs;
        }

        public void Visit(BinaryInst inst)
        {
            var left = Remap(inst.Left);
            var right = Remap(inst.Right);
            Out((_ctx._foldConstants ? ConstFolding.FoldBinary(inst.Op, left, right) : null)
                ?? new BinaryInst(inst.Op, left, right, Remap(inst.ResultType)));
        }

        public void Visit(UnaryInst inst)
        {
            var value = Remap(inst.Value);
            Out((_ctx._foldConstants ? ConstFolding.FoldUnary(inst.Op, value) : null)
                ?? new UnaryInst(inst.Op, value));
        }

        public void Visit(CompareInst inst)
        {
            var left = Remap(inst.Left);
            var right = Remap(inst.Right);
            Out((_ctx._foldConstants ? ConstFolding.FoldCompare(inst.Op, left, right) : null)
                ?? new CompareInst(inst.Op, left, right));
        }

        public void Visit(ConvertInst inst)
        {
            var value = Remap(inst.Value);
            Out((_ctx._foldConstants ? ConstFolding.FoldConvert(value, inst.ResultType, inst.CheckOverflow, inst.SrcUnsigned) : null)
                ?? new ConvertInst(value, inst.ResultType, inst.CheckOverflow, inst.SrcUnsigned));
        }

        public void Visit(LoadInst inst) => Out(new LoadInst(Remap(inst.Address), Remap(inst.ElemType), inst.Flags));
        public void Visit(StoreInst inst) => Out(new StoreInst(Remap(inst.Address), Remap(inst.Value), Remap(inst.ElemType), inst.Flags));
        public void Visit(FieldExtractInst inst) => Out(new FieldExtractInst(Remap(inst.Field), Remap(inst.Obj)));
        public void Visit(FieldInsertInst inst) => Out(new FieldInsertInst(Remap(inst.Field), Remap(inst.Obj), Remap(inst.NewValue)));

        public void Visit(ArrayAddrInst inst) => Out(new ArrayAddrInst(Remap(inst.Array), Remap(inst.Index), Remap(inst.ElemType), inst.InBounds, inst.IsReadOnly));

        public void Visit(FieldAddrInst inst) => Out(new FieldAddrInst(Remap(inst.Field), inst.IsStatic ? null : Remap(inst.Obj), inst.InBounds));

        public void Visit(PtrOffsetInst inst) => Out(new PtrOffsetInst(Remap(inst.BasePtr), Remap(inst.Index), Remap(inst.ResultType), inst.Stride, 0));

        public void Visit(CallInst inst)
        {
            var method = Remap(inst.Method);
            var args = RemapArgs(inst.Args);

            Out((_ctx._foldConstants ? ConstFolding.FoldCall(method, args) : null)
                ?? new CallInst(method, args, inst.IsVirtual, inst.Constraint == null ? null : Remap(inst.Constraint)) {
                    InBounds = inst.InBounds,
                    IsTailCall = inst.IsTailCall,
                    IsReadOnly = inst.IsReadOnly
                });
        }

        public void Visit(NewObjInst inst) => Out(new NewObjInst(Remap(inst.Constructor), RemapArgs(inst.Args)));
        public void Visit(FuncAddrInst inst) => Out(new FuncAddrInst(Remap(inst.Method), inst.IsVirtual ? Remap(inst.Object) : null,
            inst.Constraint == null ? null : Remap(inst.Constraint)));

        public void Visit(IntrinsicInst inst)
        {
            // ldtoken of a generic definition means typeof(G<>), even when
            // cloning into a generic context. Only constructed tokens substitute.
            var staticArgs = inst is CilIntrinsic.LoadHandle { StaticArgs: [TypeDef] }
                ? inst.StaticArgs.ToArray() : RemapEntities(inst.StaticArgs);
            var cloned = inst.CloneWith(Remap(inst.ResultType), staticArgs, RemapArgs(inst.Args));
            var folded = _ctx._foldConstants ? ConstFolding.FoldIntrinsic(cloned) : null;

            if (folded != null) {
                Out(folded);
                cloned.Remove(); // delete uses
            } else {
                Out(cloned);
            }
        }

        public void Visit(SelectInst inst)
        {
            var cond = Remap(inst.Cond);
            var ifTrue = Remap(inst.IfTrue);
            var ifFalse = Remap(inst.IfFalse);

            Out((_ctx._foldConstants ? ConstFolding.FoldSelect(cond, ifTrue, ifFalse) : null)
                ?? new SelectInst(cond, ifTrue, ifFalse, Remap(inst.ResultType)));
        }

        public void Visit(ReturnInst inst) => Out(new ReturnInst(inst.HasValue ? Remap(inst.Value) : null));
        public void Visit(BranchInst inst) => Out(inst.IsJump ? new BranchInst(Remap(inst.Then)) : new BranchInst(Remap(inst.Cond), Remap(inst.Then), Remap(inst.Else)));
        public void Visit(SwitchInst inst) => Out(new SwitchInst(RemapArgs(inst.Operands), inst.TargetMappings.AsSpan().ToArray()));
        public void Visit(PhiInst inst) => Out(new PhiInst(Remap(inst.ResultType), RemapArgs(inst.Operands)));

        public void Visit(GuardInst inst) => Out(new GuardInst(inst.Kind, Remap(inst.HandlerBlock), inst.CatchType == null ? null : Remap(inst.CatchType), inst.HasFilter ? Remap(inst.FilterBlock) : null));
        public void Visit(ThrowInst inst) => Out(new ThrowInst(inst.IsRethrow ? null : Remap(inst.Exception)));
        public void Visit(LeaveInst inst) => Out(new LeaveInst(Remap(inst.Target)));
        public void Visit(ResumeInst inst) => Out(new ResumeInst(0, RemapArgs(inst.Operands)));
    }
}
