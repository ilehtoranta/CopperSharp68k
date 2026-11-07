namespace Cilica.Passes.Linq;

using Cilica.IR.Utils;

/// <summary> Source with immutable length backed by an array or string. </summary>
internal class MemorySource : LinqSourceNode
{
    public MemorySource(UseRef source, LinqStageNode drain)
        : base(drain, source) { }

    Value? _currPtr, _endPtr;

    protected override void EmitHead(LoopBuilder loop, out Value? length, ref LinqStageNode firstStage, BodyLoopData loopData)
    {
        var builder = loop.PreHeader;
        // T& startPtr = call MemoryMarshal.GetArrayDataReference<T>(T[]: source)  // or akin.
        (_currPtr, length) = LoopStrengthReduction.CreateGetDataPtrRange(builder, PhysicalSource.Operand);

        IntegrateSkipTakeRanges(builder, ref firstStage, out var offset, ref length);
        if (offset != null) {
            _currPtr = builder.CreatePtrOffset(_currPtr, offset);
        }

        // T& endPtr = lea startPtr + (nint)length * sizeof(T)
        _endPtr = builder.CreatePtrOffset(_currPtr, length);

        // T& currPtr = phi [PreHeader: startPtr], [Latch: {currPtr + sizeof(T)}]
        _currPtr = loop.CreateAccum(_currPtr, currPtr => loop.Latch.CreatePtrIncrement(currPtr)).SetName("lq_currPtr");
    }

    protected override Value EmitMoveNext(IRBuilder builder)
        => builder.CreateUlt(_currPtr!, _endPtr!); // ptr < endPtr

    protected override Value EmitCurrent(IRBuilder builder)
        => builder.CreateLoad(_currPtr!); // *ptr
}
internal class EnumeratorSource : LinqSourceNode
{
    public EnumeratorSource(UseRef enumerable, LinqStageNode drain)
        : base(drain, enumerable) { }

    EnumeratorLoop _enumeration = null!;

    protected override void EmitHead(LoopBuilder loop, out Value? length, ref LinqStageNode firstStage, BodyLoopData loopData)
    {
        var sourceCall = (CallInst)PhysicalSource.Parent;
        var enumerableType = sourceCall.Method.ParamSig[PhysicalSource.OperIndex].Type; // IEnumerable<T> | IEnumerable
        _enumeration = new EnumeratorLoop(loop, PhysicalSource.Operand, enumerableType, isRoot: true);
        loopData.Enumeration = _enumeration;
        loopData.HasDisposal = true;
        length = null;
    }

    protected override Value EmitMoveNext(IRBuilder builder)
        => _enumeration.MoveNext(builder);

    protected override Value EmitCurrent(IRBuilder builder)
        => _enumeration.Current(builder);

    protected override void EmitEnd(LoopBuilder loop)
    {
        _enumeration.Finish(((BranchInst)loop.Exit.Block.Last).Then);
    }
}
internal class IntRangeSource : LinqSourceNode
{
    public IntRangeSource(CallInst subjectCall, LinqStageNode drain)
        : base(drain, default, subjectCall) { }

    Value? _index, _start, _count;

    protected override void EmitHead(LoopBuilder loop, out Value? count, ref LinqStageNode firstStage, BodyLoopData loopData)
    {
        var start = SubjectCall.Args[0];
        count = SubjectCall.Args[1];

        _start = start;
        _count = count;
        _index = loop.CreateInductor().SetName("lq_rangeidx");
    }

    protected override Value EmitMoveNext(IRBuilder builder)
        => builder.CreateSlt(_index!, _count!);

    protected override Value EmitCurrent(IRBuilder builder)
        => builder.CreateAdd(_start!, _index!);
}
