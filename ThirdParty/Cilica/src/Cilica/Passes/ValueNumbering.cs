namespace Cilica.Passes;

using Cilica.Analysis;

public class ValueNumbering : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var table = new ValueTable() {
            Context = ctx,
            DomTree = ctx.GetAnalysis<DominatorTree>(),
            AliasAnalysis = ctx.GetAnalysis<AliasAnalysis>(),
            FuncInfo = ctx.Compilation.GetAnalysis<GlobalFunctionEffects>(),
            RegionAnalysis = ctx.GetAnalysis<ProtectedRegionAnalysis>(),
        };
        int removed = 0;

        // Generate list of blocks in reverse post order, so that we visit most predecessors first.
        // (index will be > 0 at the end if there are unreachable blocks.)
        var blocks = new BasicBlock[ctx.Method.NumBlocks];
        int index = blocks.Length;
        ctx.Method.TraverseDepthFirst(postVisit: block => blocks[--index] = block);

        // Perform GVN
        foreach (var block in blocks.AsSpan(index)) {
            foreach (var inst in block) {
                removed += table.Process(inst) ? 1 : 0;
            }
        }

        return removed != 0 ? MethodInvalidations.DataFlow : 0;
    }

    class ValueTable
    {
        readonly Dictionary<Instruction, List<Instruction>> _availMap = new(VNComparer.Instance);
        MemoryAccessAnalysis? _memory;
        public required MethodTransformContext Context;
        public required DominatorTree DomTree;
        public required AliasAnalysis AliasAnalysis;
        public required GlobalFunctionEffects FuncInfo;
        public required ProtectedRegionAnalysis RegionAnalysis;

        public bool Process(Instruction inst)
        {
            if (!s_Taggers.ContainsKey(inst.GetType())) return false;
            if (inst is MemoryInst { IsVolatile: true } || inst is StoreInst { IsCasting: true }) return false;

            var availDefs = _availMap.GetOrAddRef(inst) ??= new();

            // Check if there's another available def for this instruction
            if (inst is not StoreInst) {
                foreach (var otherDef in availDefs) {
                    if (!CheckAvail(otherDef, inst)) continue;
                    Context.Logger.Trace($"Eliminated redundant '{inst}' in '{inst.Block}' by replacing with '{otherDef}' from '{otherDef.Block}'.");
                    var repl = otherDef is StoreInst store ? store.Value : otherDef;
                    Debug.Assert(repl.ResultType.IsStackAssignableTo(inst.ResultType));
                    inst.ReplaceWith(repl);
                    return true;
                }
            }

            // Add inst to the available set
            if (availDefs.Count > 0 && availDefs[^1] is Instruction prev && prev.Block == inst.Block) {
                availDefs.RemoveAt(availDefs.Count - 1);
            }
            availDefs.Add(inst);
            return false;
        }

        // Checks if `def` is available at `user` by walking backwards over the CFG until its definition.
        public bool CheckAvail(Instruction def, Instruction user)
        {
            // Trivial reject
            if (!DomTree.Dominates(def.Block, user.Block)) return false;

            // Don't propagate values across different regions. #33
            // This check is a bit conservative, there are cases where
            // it is safe to propagate values into child regions.
            var defRegion = RegionAnalysis.GetBlockRegion(def.Block);
            var useRegion = RegionAnalysis.GetBlockRegion(user.Block);
            if (defRegion != useRegion) return false;

            // Registered intrinsics, including initialization checks, are stable
            // after normal completion. Only memory accesses and ordinary calls
            // need clobber checks; writes cannot undo type/module initialization.
            if (!(def is MemoryInst or CallInst)) return true;

            if (def is CallInst call) {
                var effect = FuncInfo.GetEffects(call);

                if (effect.MayWriteMem || effect.HasMemoryBarrier || effect.MayDiverge || effect.MayAllocate) {
                    return false;
                }

                if (!effect.MayReadMem) return true;
                // if effect == MayRead, check for clobbers
            }

            _memory ??= Context.GetAnalysis<MemoryAccessAnalysis>();

            if (def.Block == user.Block) {
                return !HasClobber(user.Block, def, user);
            }

            // Backedges contribute writes from the entire revisited block.
            var worklist = new DiscreteStack<BasicBlock>(user.Block);

            // Make the worklist forget the user block was pushed so the loop can check
            // loop backedges when visiting predecessors.
            worklist.UnmarkPushed(user.Block);
            bool firstBlock = true;

            while (worklist.TryPop(out var block)) {
                if (HasClobber(block, block == def.Block ? def : null, firstBlock ? user : null)) {
                    return false;
                }

                firstBlock = false;

                if (block == def.Block) continue; // stop when reaching def's block

                foreach (var pred in block.Preds) {
                    worklist.Push(pred);
                }
            }

            return true;

            bool HasClobber(BasicBlock block, Instruction? after, Instruction? before)
            {
                foreach (var instruction in _memory.GetPotentialWrites(block, after, before)) {
                    if (instruction.Block != null && MayClobberDef(def, instruction)) {
                        return true;
                    }
                }

                return false;
            }
        }

        // Checks if `inst` may clobber the result value of `def`.
        private bool MayClobberDef(Instruction def, Instruction inst)
        {
            if (inst is GuardInst || inst.IsMemoryBarrier) return true;
            if (inst is StoreInst store) {
                if (def is MemoryInst access) {
                    return AliasAnalysis.MayAlias(store.Address, access.Address);
                }
                return true;
            }
            if (inst is CallInst call) {
                var effects = FuncInfo.GetEffects(call);
                return effects.MayWriteMem || effects.HasMemoryBarrier;
            }
            return inst.MayWriteToMemory;
        }
    }


    #region Instruction Hashing/Equality
    // Note: methods of this class will only be called for instructions with a registered tagger.
    class VNComparer : IEqualityComparer<Instruction>
    {
        public static readonly VNComparer Instance = new();

        public bool Equals(Instruction? x, Instruction? y)
        {
            var type = x!.GetType();

            // Load/store instructions can have matching tags.
            if (y!.GetType() != type) {
                if (!(x is MemoryInst && y is MemoryInst)) {
                    return false;
                }
                type = typeof(MemoryInst);
            }
            return s_Taggers[type].CompareFn(x, y);
        }
        public int GetHashCode(Instruction inst)
        {
            return s_Taggers[inst.GetType()].HashFn(inst);
        }
    }

    static readonly Dictionary<Type, InstTagger> s_Taggers = new();

    static ValueNumbering()
    {
        // TODO: this looks overly complicated, maybe just use a switch expr?

        // NOTE: value.Equals() are used in some places becase Const values may have different instances.
        Reg<BinaryInst>(
            comp: (a, b) => a.Op == b.Op && a.Left.Equals(b.Left) && a.Right.Equals(b.Right),
            hash: (inst) => HashCode.Combine((int)inst.Op | 0x10000, inst.Left, inst.Right)
        );
        Reg<UnaryInst>(
            comp: (a, b) => a.Op == b.Op && a.Value.Equals(b.Value),
            hash: (inst) => HashCode.Combine((int)inst.Op | 0x20000, inst.Value)
        );
        Reg<CompareInst>(
            comp: (a, b) => a.Op == b.Op && a.Left.Equals(b.Left) && a.Right.Equals(b.Right),
            hash: (inst) => HashCode.Combine((int)inst.Op | 0x30000, inst.Left, inst.Right)
        );
        Reg<ConvertInst>(
            comp: (a, b) => a.ResultType == b.ResultType && a.Value.Equals(b.Value) && 
                            a.CheckOverflow == b.CheckOverflow && a.SrcUnsigned == b.SrcUnsigned,
            hash: (inst) => HashCode.Combine(inst.ResultType, inst.Value)
        );
        Reg<FieldExtractInst>(
            comp: (a, b) => a.Field == b.Field && a.Obj == b.Obj,
            hash: (inst) => HashCode.Combine(inst.Field, inst.Obj, 2345)
        );

        Reg<FieldAddrInst>(
            comp: (a, b) => a.Field == b.Field && a.Obj == b.Obj && a.InBounds == b.InBounds,
            hash: (inst) => HashCode.Combine(inst.Field, inst.Obj, 1234)
        );
        Reg<ArrayAddrInst>(
            comp: (a, b) => a.Array.Equals(b.Array) && a.Index.Equals(b.Index) && a.ElemType == b.ElemType && 
                            a.InBounds == b.InBounds && a.IsReadOnly == b.IsReadOnly,
            hash: (inst) => HashCode.Combine(inst.Array, inst.Index, 1234)
        );
        Reg<PtrOffsetInst>(
            comp: (a, b) => a.BasePtr == b.BasePtr && a.Index.Equals(b.Index) && 
                            (a.KnownStride ? a.Stride == b.Stride : a.ElemType == b.ElemType),
            hash: (inst) => HashCode.Combine(inst.BasePtr, inst.Index, inst.Stride)
        );

        Reg<CilIntrinsic.ArrayLen>(
            comp: (a, b) => a.Args[0] == b.Args[0],
            hash: (inst) => HashCode.Combine(inst.Args[0])
        );
        Reg<CilIntrinsic.CastClass>(
            comp: (a, b) => a.Args[0] == b.Args[0] && a.DestType == b.DestType,
            hash: (inst) => HashCode.Combine(inst.DestType, inst.Args[0])
        );
        Reg<CilIntrinsic.AsInstance>(
            comp: (a, b) => a.Args[0] == b.Args[0] && a.DestType == b.DestType,
            hash: (inst) => HashCode.Combine(inst.DestType, inst.Args[0])
        );
        Reg<CilIntrinsic.Initialization>(
            comp: (a, b) => a.Helper.Definition == b.Helper.Definition && a.Helper.DeclaringType == b.Helper.DeclaringType,
            hash: (inst) => HashCode.Combine(inst.Helper.Definition, inst.Helper.DeclaringType)
        );

        // Volatile accesses are excluded by the value table.
        Reg<MemoryInst>(
            comp: (a, b) => a.Address == b.Address && a.ElemType == b.ElemType,
            hash: (inst) => HashCode.Combine(inst.Address)
        );

        s_Taggers[typeof(LoadInst)] = s_Taggers[typeof(MemoryInst)];
        s_Taggers[typeof(StoreInst)] = s_Taggers[typeof(MemoryInst)];

        // FIXME: proper equality for MethodSpec (maybe implement Equals() and GetHashCode()?)
        //       For now, reference comparation will work in most cases,
        //       since we don't always create new instances.
        Reg<CallInst>(
            comp: (a, b) => a.Method == b.Method && a.IsVirtual == b.IsVirtual && a.Constraint == b.Constraint &&
                a.IsReadOnly == b.IsReadOnly && a.IsTailCall == b.IsTailCall && a.Args.SequenceEqual(b.Args),
            hash: (inst) => HashCode.Combine(inst.Method, inst.NumArgs > 0 ? inst.Args[0] : null)
        );

        static void Reg<TInst>(Func<TInst, TInst, bool> comp, Func<TInst, int> hash)
            where TInst : Instruction
        {
            s_Taggers.Add(typeof(TInst), new() {
                CompareFn = (a, b) => comp((TInst)a, (TInst)b),
                HashFn = (inst) => hash((TInst)inst)
            });
        }
    }

    class InstTagger
    {
        public required Func<Instruction, Instruction, bool> CompareFn { get; init; }
        public required Func<Instruction, int> HashFn { get; init; }
    }
#endregion
}
