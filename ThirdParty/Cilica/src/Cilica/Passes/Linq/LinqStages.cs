namespace Cilica.Passes.Linq;

using Cilica.IR.Utils;

internal class SelectStage : LinqStageNode
{
    public SelectStage(CallInst call, LinqStageNode drain)
        : base(call, drain) { }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var mappedItem = builder.CreateLambdaInvoke_ItemAndIndex(SubjectCall, 1, currItem, loopData.CreateAccum);
        Drain.EmitBody(builder, mappedItem, loopData);
    }
}
internal class WhereStage : LinqStageNode
{
    public WhereStage(CallInst call, LinqStageNode drain)
        : base(call, drain) { }

    public override bool IsFiltering => true;

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var cond = builder.CreateLambdaInvoke_ItemAndIndex(SubjectCall, 1, currItem, loopData.CreateAccum);
        // if (!cond) goto SkipBlock;
        builder.ForkIf(cond, loopData.SkipBlock, negate: true);
        Drain.EmitBody(builder, currItem, loopData);
    }
}
internal class OfTypeStage : LinqStageNode
{
    public OfTypeStage(CallInst call, LinqStageNode drain)
        : base(call, drain) { }

    public override bool IsFiltering => true;

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var destType = SubjectCall!.Method.GenericParams[0];

        if (currItem.ResultType.IsValueType || currItem.ResultType is GenericParamType) {
            currItem = builder.CreateBox(currItem.ResultType, currItem);
        }
        currItem = builder.CreateAsInstance(destType, currItem);
        builder.ForkIf(currItem, loopData.SkipBlock, negate: true);

        if (destType.IsValueType || destType is GenericParamType) {
            currItem = builder.CreateUnboxObj(destType, currItem);
        }
        Drain.EmitBody(builder, currItem, loopData);
    }
}
internal class CastStage : LinqStageNode
{
    public CastStage(CallInst call, LinqStageNode drain)
        : base(call, drain) { }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var destType = SubjectCall!.Method.GenericParams[0];

        if (currItem.ResultType.IsValueType || currItem.ResultType is GenericParamType) {
            currItem = builder.CreateBox(currItem.ResultType, currItem);
        }
        currItem = destType.IsValueType || destType is GenericParamType
            ? builder.CreateUnboxObj(destType, currItem) : builder.CreateCastClass(destType, currItem);

        Drain.EmitBody(builder, currItem, loopData);
    }
}
internal class SkipStage : LinqStageNode
{
    public SkipStage(CallInst call, LinqStageNode sink)
        : base(call, sink) { }

    public override bool IsFiltering => true;

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        // Behavior for `count <= 0` is nop (drain all source items)
        var count = SubjectCall.Args[1];

        loopData.CreateAccum(count, emitUpdate: curr => {
            // if (count > 0) goto DecrAndSkip;
            //  ...
            // DecrAndSkip:
            //  count--;
            //  goto Skip
            var decrAndSkip = builder.Method.CreateBlock(insertAfter: loopData.SkipBlock.Prev);
            var decr = new BinaryInst(BinaryOp.Sub, curr, ConstInt.CreateI(1));
            decrAndSkip.InsertFirst(decr);
            decrAndSkip.SetBranch(loopData.SkipBlock);

            builder.ForkIf(builder.CreateSgt(curr, ConstInt.CreateI(0)), decrAndSkip);
            Drain.EmitBody(builder, currItem, loopData);

            return decr;
        }).SetName("lq_skipRem");
    }
}
internal class TakeStage : LinqStageNode
{
    LocalSlot _remaining = null!;
    public LocalSlot Remaining => _remaining;
    public TakeStage(CallInst call, LinqStageNode sink)
        : base(call, sink) { }

    public override bool IsFiltering => true;

    public override void Prepare(BodyLoopData loopData)
    {
        _remaining = loopData.PreHeader.Method.CreateVar(PrimType.Int32, "lq_takeRemaining", hardExposed: true);
        loopData.PreHeader.CreateStore(_remaining, SubjectCall.Args[1]);
        loopData.Limits.Add(_remaining);
    }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var remaining = builder.CreateLoad(_remaining);
        builder.CreateStore(_remaining, builder.CreateSub(remaining, ConstInt.CreateI(1)));
        Drain.EmitBody(builder, currItem, loopData);
    }
}
internal class FlattenStage : LinqStageNode
{
    public FlattenStage(CallInst call, LinqStageNode drain)
        : base(call, drain) { }

    public override bool IsFiltering => true;
    public override bool IsGenerating => true;

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var innerLoop = new LoopBuilder(SubjectCall.Block, "LQ_Flatten_");

        var subCollection = builder.CreateLambdaInvoke_ItemAndIndex(SubjectCall, 1, currItem, loopData.CreateAccum);
        var enumeration = new EnumeratorLoop(innerLoop, subCollection, subCollection.ResultType, loopData.Enumeration);
        loopData.HasDisposal = true;

        // Limits before this flattening stage count outer items; only downstream
        // limits constrain advancement of its inner enumerator.
        var innerLimits = new List<LocalSlot>();
        for (LinqStageNode? node = Drain; node != null; node = node.Drain) {
            if (node is TakeStage take) innerLimits.Add(take.Remaining);
        }

        var innerLoopData = loopData with {
            Enumeration = enumeration,
            Limits = innerLimits,
            SkipBlock = innerLoop.Latch.Block,
            CreateAccum = (seed, emitUpdate, resultType) =>
                loopData.CreateAccum(seed, curr => innerLoop.CreateAccum(curr, emitUpdate, resultType), resultType)
        };

        innerLoop.Build(
            emitCond: enumeration.MoveNext,
            emitBody: body => {
                var innerItem = enumeration.Current(body);
                if (SubjectCall.NumArgs == 3) {
                    innerItem = body.CreateLambdaInvoke(SubjectCall, 2, currItem, innerItem);
                }
                Drain.EmitBody(body, innerItem, innerLoopData);
            },
            emitGuard: innerLoopData.Limits.Count == 0 ? null : innerLoopData.EmitGuard
        );
        var continuation = builder.Method.CreateBlock(insertAfter: innerLoop.Exit.Block).SetName("LQ_Flatten_Continue");
        enumeration.Finish(continuation);
        builder.SetBranch(innerLoop.PreHeader.Block);
        builder.SetPosition(continuation);
    }
}
