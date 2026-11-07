namespace Cilica.IR.Utils;

/// <summary> Helper for building a for-style loop. </summary>
public class LoopBuilder
{
    public readonly IRBuilder PreHeader, Header, Latch, Exit;
    public readonly IRBuilder Body;

    public readonly BasicBlock EntryBlock; // PreHeader may be forked and so we need to keep track of the actual entry block
    readonly List<(PhiInst HeadPhi, Instruction Next)> _pendingAccums = new();

    public LoopBuilder(BasicBlock blockInsertPos, string blockNamePrefix = "l_")
    {
        PreHeader = CreateBlock("PreHeader");
        Header = CreateBlock("Header");
        Body = CreateBlock("Body");
        Latch = CreateBlock("Latch");
        Exit = CreateBlock("Exit");

        EntryBlock = PreHeader.Block;

        IRBuilder CreateBlock(string name)
        {
            blockInsertPos = blockInsertPos.Method.CreateBlock(insertAfter: blockInsertPos);
            return new IRBuilder(blockInsertPos.SetName(blockNamePrefix + name));
        }
    }

    // PreHeader:
    //  ...
    //  goto Header
    // Header:
    //  var accumN = phi [PreHeader -> accumN_Seed, Latch -> accumN_Next]
    //  bool hasNext = ${emitBound()}
    //  goto hasNext ? Body : Exit
    // Body:
    //  ${emitBody()}
    //  goto Latch
    // Latch:
    //  var accumN_Next = update(accumN)
    //  goto Header
    // Exit:
    //  ...
    public void Build(
        Func<IRBuilder, Value> emitCond,
        Action<IRBuilder> emitBody,
        Func<IRBuilder, Value>? emitGuard = null)
    {
        var condition = new IRBuilder(Header.Block, InsertionDir.After);
        if (emitGuard != null) {
            var next = Header.Method.CreateBlock(insertAfter: Header.Block);
            Header.SetBranch(emitGuard(Header), next, Exit.Block);
            condition.SetPosition(next);
        }
        var hasNext = emitCond(condition);
        condition.SetBranch(hasNext, Body.Block, Exit.Block);

        emitBody(Body);

        if (Body.Block.Last is not { IsBranch: true }) {
            Body.SetBranch(Latch.Block);
        }
        PreHeader.SetBranch(Header.Block);

        foreach (var (headPhi, next) in _pendingAccums) {
            headPhi.ReplaceOperand(next, InsertAccumPhis(Latch.Block, next, headPhi, new()));
        }
        Latch.SetBranch(Header.Block);
    }

    // Publish provisional phis before visiting predecessors so nested backedges
    // can refer to the value being constructed without recursively revisiting it.
    private Value InsertAccumPhis(BasicBlock block, Instruction def, Instruction dom, Dictionary<BasicBlock, Value> values)
    {
        if (block == def.Block) {
            return def;
        }
        if (block == dom.Block) {
            return dom;
        }
        if (values.TryGetValue(block, out var value)) return value;
        Debug.Assert(block.NumPreds > 0);

        var phi = block.InsertPhi(dom.ResultType);
        values.Add(block, phi);
        foreach (var pred in block.Preds) {
            phi.AddArg(pred, InsertAccumPhis(pred, def, dom, values));
        }
        return phi;
    }

    /// <summary> Creates a loop accumulator/induction variable. </summary>
    public PhiInst CreateAccum(Value seed, Func<Value, Value> emitUpdate)
        => CreateAccum(seed, emitUpdate, seed.ResultType);

    /// <summary> Creates a loop accumulator with the given type, or the seed type when null. </summary>
    public PhiInst CreateAccum(Value seed, Func<Value, Value> emitUpdate, TypeDesc? resultType)
    {
        var phi = Header.CreatePhi(resultType ?? seed.ResultType);
        var next = emitUpdate(phi);

        if (next is Instruction nextI && nextI.Block != Latch.Block) {
            _pendingAccums.Add((phi, nextI));
        }
        phi.AddArg((PreHeader.Block, seed), (Latch.Block, next));
        return phi;
    }

    /// <summary> Creates an <see cref="int"/> index var which increments on every iteration. </summary>
    public PhiInst CreateInductor()
    {
        return CreateAccum(
            seed: ConstInt.CreateI(0),
            emitUpdate: curr => Latch.CreateAdd(curr, ConstInt.CreateI(1))
        ).SetName("index");
    }

    public void InsertBefore(Instruction inst)
    {
        var newBlock = inst.Block.Split(inst, branchTo: EntryBlock);
        Exit.SetBranch(newBlock);
    }
}
