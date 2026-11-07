namespace Cilica.Frontend;

using ExceptionRegionKind = System.Reflection.Metadata.ExceptionRegionKind;

public class ILImporter
{
    internal readonly MethodDef _method;
    internal readonly MethodBody _body;
    internal readonly RegionNode? _regionTree;

    internal readonly ILMethodBody _ilBody;
    internal readonly VarFlags[] _varFlags;
    internal readonly Value?[] _varSlots;
    internal readonly MethodDebugSymbols? _debugSyms;

    readonly Dictionary<int, BlockState> _blocks = new();
    internal List<BlockState> _blocksEndingWithEhResume = new();

    private ILImporter(MethodDef method)
    {
        Ensure.That(method.ILBody != null);

        _ilBody = method.ILBody;
        _method = method;
        _body = new MethodBody(method) { InitLocals = _ilBody.InitLocals };

        _regionTree = RegionNode.BuildTree(_ilBody.ExceptionClauses);
        _debugSyms = method.GetDebugSymbols();

        int numVars = _body.Args.Length + _ilBody.Locals.Length;
        _varFlags = new VarFlags[numVars];
        _varSlots = new Value[numVars];
    }

    public static MethodBody ParseCode(MethodDef method)
    {
        if (method.IsRuntimeAsync) {
            throw new NotSupportedException("Runtime-async method bodies require a distinct return and suspension model");
        }
        return new ILImporter(method).ImportCode();
    }

    private MethodBody ImportCode()
    {
        var code = _ilBody.Instructions.AsSpan();
        var ehRegions = _ilBody.ExceptionClauses;
        var leaders = FindLeaders(code, ehRegions);

        AnalyseVars(code, leaders);
        CreateBlocks(leaders);
        CreateGuards(ehRegions);
        ImportBlocks(code, leaders);
        foreach (var state in _blocks.Values) state.CompleteStackPhis();
        foreach (var phi in _body.Instructions().OfType<PhiInst>().ToArray()) {
            if (phi.Block != null) Passes.DeadCodeElim.RemoveTrivialPhi(phi, peel: true);
        }

        NormalizeReferenceTypes();
        NormalizeHandlerEntries();

        AddFinallyResumeEdges(ehRegions);

        return _body;
    }

    private void NormalizeHandlerEntries()
    {
        // A loop may branch back to the first instruction of its handler.
        // Keep that ordinary entry separate from the exceptional entry, which
        // must have only its guard as predecessor in IR.
        foreach (var guard in _body.Instructions().OfType<GuardInst>().ToArray()) {
            guard.HandlerBlock = SplitEntry(guard.HandlerBlock);
            if (guard.HasFilter) guard.FilterBlock = SplitEntry(guard.FilterBlock);
        }

        BasicBlock SplitEntry(BasicBlock block)
        {
            if (block.NumPreds == 1) return block;
            var entry = _body.CreateBlock(insertAfter: block.Prev);
            entry.SetBranch(block);
            return entry;
        }
    }

    private void NormalizeReferenceTypes()
    {
        // Generated CIL can pass a broadly typed reference directly to
        // a more specific reference parameter or store, without castclass. Make
        // that unchecked typing assumption explicit in IR, preserving the IL.
        foreach (var inst in _body.Instructions().ToArray()) {
            switch (inst) {
                case CallInst call:
                    for (int i = 0; i < call.NumArgs; i++) ConvertOperand(i, call.Method.ParamSig[i].Type);
                    break;
                case NewObjInst allocation:
                    for (int i = 0; i < allocation.NumArgs; i++) ConvertOperand(i, allocation.Constructor.ParamSig[i + 1].Type);
                    break;
                case StoreInst store:
                    ConvertOperand(1, store.ElemType);
                    break;
                case ReturnInst { HasValue: true }:
                    ConvertOperand(0, _body.ReturnType);
                    break;
            }

            void ConvertOperand(int index, TypeDesc target)
            {
                var value = inst.Operands[index];
                var source = value.ResultType;
                if (!source.IsManagedObject() || value is ConstNull or CilIntrinsic.Box || source is GenericParamType ||
                    !target.IsManagedObject() || target is GenericParamType || source.IsAssignableTo(target)) return;
                var cast = new CilIntrinsic.RefCast(target, value) { DebugLocation = inst.DebugLocation };
                cast.InsertBefore(inst);
                inst.ReplaceOperand(index, cast);
            }
        }
    }

    private void CreateBlocks(BitSet leaders)
    {
        // Remove 0th label to avoid creating 2 blocks
        bool firstHasPred = leaders.Remove(0);
        var entryBlock = firstHasPred ? _body.CreateBlock() : null!;

        int startOffset = 0;
        foreach (int endOffset in leaders) {
            _blocks[startOffset] = new BlockState(this, startOffset);
            startOffset = endOffset;
        }
        // Ensure that the entry block don't have predecessors
        if (firstHasPred) {
            var firstBlock = GetBlock(0).Block;
            entryBlock.SetBranch(firstBlock);
        }
    }

    private void CreateGuards(ExceptionClause[] clauses)
    {
        var mappings = new Dictionary<GuardInst, ExceptionClause>(clauses.Length);

        // I.12.4.2.5 Overview of exception handling
        foreach (var clause in clauses) {
            var kind = clause.Kind switch {
                ExceptionRegionKind.Catch or
                ExceptionRegionKind.Filter  => GuardKind.Catch,
                ExceptionRegionKind.Finally => GuardKind.Finally,
                ExceptionRegionKind.Fault   => GuardKind.Fault,
                _ => throw new InvalidOperationException()
            };
            bool hasFilter = clause.Kind == ExceptionRegionKind.Filter;

            var startBlock = GetOrSplitStartBlock(clause);
            var handlerBlock = GetBlock(clause.HandlerStart);
            var filterBlock = hasFilter ? GetBlock(clause.FilterStart) : null;

            var guard = new GuardInst(kind, handlerBlock.Block, clause.CatchType, filterBlock?.Block);
            startBlock.InsertAnteLast(guard);
            _regionTree!.FindEnclosing(clause.TryStart, clause.TryEnd).EntryBlock = startBlock;

            // Push exception on handler/filter entry stack
            if (kind == GuardKind.Catch) {
                handlerBlock.PushNoEmit(guard);
            }
            if (hasFilter) {
                filterBlock!.PushNoEmit(guard);
            }
            mappings[guard] = clause;
        }

        // A handler/filter can begin at the same IL offset as a nested try.
        // Give the exceptional entry its own block, outside that nested region.
        // Resolve these entries after all protected entry blocks have been built.
        foreach (var (guard, clause) in mappings) {
            guard.HandlerBlock = GetHandlerEntry(clause.HandlerStart);
            if (guard.HasFilter) guard.FilterBlock = GetHandlerEntry(clause.FilterStart);
        }

        BasicBlock GetHandlerEntry(int offset)
        {
            var state = GetBlock(offset);
            if (state.EntryBlock.HasGuards) {
                var entry = _body.CreateBlock(insertAfter: state.EntryBlock.Prev);
                entry.SetBranch(state.EntryBlock);
                state.EntryBlock = entry;
            }
            return state.EntryBlock;
        }

        BasicBlock GetOrSplitStartBlock(ExceptionClause region)
        {
            var state = GetBlock(region.TryStart);

            // Create a new dominating block for this region if it nests any other in the current block.
            // Note that this code relies on the region table to be correctly ordered, as required by ECMA335:
            //  "If handlers are nested, the most deeply nested try blocks shall come
            //  before the try blocks that enclose them."
            // TODO: consider using the region tree for this
            if (IsBlockNestedBy(region, state.EntryBlock)) {
                var newBlock = _body.CreateBlock(insertAfter: state.EntryBlock.Prev);

                foreach (var pred in state.EntryBlock.Preds) {
                    Debug.Assert(pred.NumSuccs == 1);
                    pred.SetBranch(newBlock);
                }
                newBlock.SetBranch(state.EntryBlock);
                state.EntryBlock = newBlock;
            }
            return state.EntryBlock;
        }
        bool IsBlockNestedBy(ExceptionClause region, BasicBlock block)
        {
            foreach (var guard in block.Guards()) {
                var currRegion = mappings[guard];
                if (currRegion.TryStart >= region.TryStart && currRegion.TryEnd < region.TryEnd) {
                    return true;
                }
            }
            return false;
        }
    }

    private void ImportBlocks(Span<ILInstruction> code, BitSet leaders)
    {
        var entryBlock = _body.EntryBlock ?? GetBlock(0).Block;

        // Copy stored/address taken arguments to local variables
        for (int i = 0; i < _body.Args.Length; i++) {
            if (!Has(_varFlags[i], VarFlags.AddrTaken | VarFlags.Stored)) continue;

            var arg = _body.Args[i];
            var slot = _varSlots[i] = _body.CreateVar(arg.ResultType, $"a_{arg.Name}");
            entryBlock.InsertAnteLast(new StoreInst(slot, arg));
        }

        // Import code
        int startIndex = 0;
        foreach (int endOffset in leaders) {
            var block = GetBlock(code[startIndex].Offset);
            int endIndex = FindIndex(code, endOffset);
            block.ImportCode(code[startIndex..endIndex]);
            startIndex = endIndex;
        }
    }

    private void AddFinallyResumeEdges(ExceptionClause[] clauses)
    {
        foreach (var block in _blocksEndingWithEhResume) {
            var parentClause = clauses.First(c => {
                return c.Kind == ExceptionRegionKind.Filter 
                    ? block.StartOffset >= c.FilterStart && block.StartOffset < c.FilterEnd 
                    : block.StartOffset >= c.HandlerStart && block.StartOffset < c.HandlerEnd;
            });
            var protectedNode = _regionTree!.FindEnclosing(parentClause.TryStart, parentClause.TryEnd);

            var resume = (ResumeInst)block.Block.Last;
            resume.SetExitTargets(protectedNode.ExitTargets);
        }
    }

    private void AnalyseVars(Span<ILInstruction> code, BitSet leaders)
    {
        int blockStartIdx = 0;
        var lastBlocks = new int[_ilBody.Locals.Length];

        foreach (int endOffset in leaders) {
            int blockEndIdx = FindIndex(code, endOffset);

            foreach (ref var inst in code[blockStartIdx..blockEndIdx]) {
                var (op, varIdx) = GetVarInstOp(inst.OpCode, inst.Operand);
                if (op == VarFlags.None) continue;

                if (Has(op, VarFlags.IsLocal)) {
                    ref int lastBlock = ref lastBlocks[varIdx];
                    if (lastBlock != 0 && lastBlock != blockStartIdx + 1) {
                        op |= VarFlags.CrossesBlock;
                    }
                    lastBlock = blockStartIdx + 1;
                    varIdx += _body.Args.Length;
                }
                _varFlags[varIdx] |= op;
            }
            blockStartIdx = blockEndIdx;
        }
    }

    // Returns a bitset containing all instruction offsets where a block starts (branch targets).
    private static BitSet FindLeaders(Span<ILInstruction> code, ExceptionClause[] ehRegions)
    {
        int codeSize = code[^1].GetEndOffset();
        var leaders = new BitSet(codeSize);
        leaders.Add(codeSize);

        foreach (ref var inst in code) {
            if (!inst.OpCode.IsTerminator()) continue;

            if (inst.Operand is int targetOffset) {
                leaders.Add(targetOffset);
            }
            // switch
            else if (inst.Operand is int[] targetOffsets) {
                foreach (int offset in targetOffsets) {
                    leaders.Add(offset);
                }
            }
            leaders.Add(inst.GetEndOffset()); // fallthrough
        }

        foreach (var region in ehRegions) {
            // Note: end offsets must have already been marked by leave/endfinally
            leaders.Add(region.TryStart);

            if (region.HandlerStart >= 0) {
                leaders.Add(region.HandlerStart);
            }
            if (region.FilterStart >= 0) {
                leaders.Add(region.FilterStart);
            }
        }
        return leaders;
    }

    // Binary search to find instruction index using offset
    private static int FindIndex(Span<ILInstruction> code, int offset)
    {
        int start = 0;
        int end = code.Length - 1;
        while (start <= end) {
            int mid = (start + end) / 2;
            int c = offset - code[mid].Offset;
            if (c < 0) {
                end = mid - 1;
            } else if (c > 0) {
                start = mid + 1;
            } else {
                return mid;
            }
        }
        // Special case last instruction
        if (offset >= code[^1].Offset) {
            return code.Length;
        }
        throw new InvalidProgramException("Invalid instruction offset");
    }

    /// <summary> Gets the block for the leader instruction at the specified offset. </summary>
    internal BlockState GetBlock(int offset) => _blocks[offset];

    internal BasicBlock GetBranchTarget(int sourceOffset, int targetOffset)
    {
        var target = GetBlock(targetOffset).Block;
        // Multiple nested tries can share an IL offset. Enter each new region,
        // but do not re-enter an enclosing guard on a branch within its body.
        for (var region = _regionTree?.FindEnclosing(targetOffset);
             region is { Kind: RegionKind.Protected } && region.StartOffset == targetOffset && !region.Contains(sourceOffset);
             region = region.Parent) {
            target = region.EntryBlock;
        }
        return target;
    }

    internal (Value Slot, VarFlags CombinedFlags, bool IsBlockLocal) GetVarSlot(VarFlags op, int index)
    {
        Debug.Assert(op != VarFlags.None);

        if (Has(op, VarFlags.IsArg)) {
            return (_varSlots[index] ?? _body.Args[index], _varFlags[index], false);
        }
        const VarFlags kBlockLocalFlags = (VarFlags.IsLocal | VarFlags.Loaded | VarFlags.Stored) & ~VarFlags.CrossesBlock;

        var localVar = _ilBody.Locals[index];
        index += _body.Args.Length;

        ref var slot = ref _varSlots[index];
        var flags = _varFlags[index];
        bool isBlockLocal = flags == kBlockLocalFlags && !localVar.IsPinned;

        slot ??= isBlockLocal
            ? (Has(op, VarFlags.Loaded) ? (_body.InitLocals
                ? new IR.Utils.IRBuilder(_body.EntryBlock, IR.Utils.InsertionDir.Before).CreateDefaultOf(localVar.Type)
                : new Undef(localVar.Type)) : null)
            : _body.CreateVar(localVar.Type, pinned: localVar.IsPinned);

        return (slot!, flags, isBlockLocal);
    }

    internal void SetBlockLocalVarSlot(int index, Value value)
    {
        _varSlots[index + _body.Args.Length] = value;
    }

    internal static (VarFlags Op, int Index) GetVarInstOp(ILCode code, object? operand)
    {
        var op = code switch {
            >= ILCode.Ldarg_0 and <= ILCode.Ldarg_3 => VarFlags.Loaded | VarFlags.IsArg,
            >= ILCode.Ldloc_0 and <= ILCode.Ldloc_3 => VarFlags.Loaded | VarFlags.IsLocal,
            >= ILCode.Stloc_0 and <= ILCode.Stloc_3 => VarFlags.Stored | VarFlags.IsLocal,
            ILCode.Ldarg_S or ILCode.Ldarg          => VarFlags.Loaded | VarFlags.IsArg,
            ILCode.Ldloc_S or ILCode.Ldloc          => VarFlags.Loaded | VarFlags.IsLocal,
            ILCode.Starg_S or ILCode.Starg          => VarFlags.Stored | VarFlags.IsArg,
            ILCode.Stloc_S or ILCode.Stloc          => VarFlags.Stored | VarFlags.IsLocal,
            ILCode.Ldarga_S or ILCode.Ldarga        => VarFlags.AddrTaken | VarFlags.IsArg,
            ILCode.Ldloca_S or ILCode.Ldloca        => VarFlags.AddrTaken | VarFlags.IsLocal,
            _ => VarFlags.None
        };
        int index = op == 0 ? 0 :
            code is >= ILCode.Ldarg_0 and <= ILCode.Stloc_3
                ? (code - ILCode.Ldarg_0) & 3
                : (int)operand!;
        return (op, index);
    }
    private static bool Has(VarFlags x, VarFlags y) => (x & y) != 0;
}
