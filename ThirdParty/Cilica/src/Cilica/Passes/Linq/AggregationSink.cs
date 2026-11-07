namespace Cilica.Passes.Linq;

using Cilica.IR;
using Cilica.IR.Utils;

internal class AggregationSink : LinqSink
{
    public AggregationSink(CallInst call)
        : base(call) { }

    Value? _accumulator, _seed, _hasData;

    public override void EmitHead(IRBuilder builder, EstimatedSourceLen sourceLen)
    {
        _seed = GetSeed(builder, sourceLen);
    }
    public override void EmitTail(IRBuilder builder)
    {
        if (_hasData != null) {
            // goto hasData ? Exit : ThrowHelper
            builder.Throw(typeof(InvalidOperationException), builder.CreateEq(_hasData, ConstInt.CreateI(0)));
        }
        Result = MapResult(builder, _accumulator!);
    }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var skipBlock = loopData.SkipBlock;

        if (_seed is not Undef) {
            var type = SubjectCall.NumArgs >= 3 ? SubjectCall.Method.ParamSig[1].Type : SubjectCall.ResultType;
            _accumulator = loopData.CreateAccum(_seed!, curr => Accumulate(builder, curr, currItem, skipBlock), type);
            return;
        }
        Debug.Assert(currItem.ResultType.IsAssignableTo(SubjectCall.ResultType));

        _hasData = loopData.CreateAccum(ConstInt.Create(PrimType.Bool, 0), emitUpdate: hasData => {
            var gotData = default(Value);

            _accumulator = loopData.CreateAccum(_seed, emitUpdate: curr => {
                // nextAccum = hasData ? Accum(currItem) : currItem
                var emptyCheckBlock = builder.Block;
                var mergeBlock = builder.Method.CreateBlock(insertAfter: emptyCheckBlock).SetName("LQ_MergeAccum");
                builder.ForkIf(hasData, mergeBlock, negate: true);

                var accumulated = Accumulate(builder, curr, currItem, skipBlock);
                var accumBlock = builder.Block;
                builder.SetBranch(mergeBlock);

                builder.SetPosition(mergeBlock);
                gotData = builder.CreatePhi(PrimType.Bool, (emptyCheckBlock, ConstInt.CreateI(1)), (accumBlock, hasData));
                return builder.CreatePhi(SubjectCall.ResultType, (emptyCheckBlock, currItem), (accumBlock, accumulated));
            });
            return gotData!;
        }).SetName("lq_has_data");
    }

    protected virtual Value GetSeed(IRBuilder builder, EstimatedSourceLen sourceLen)
    {
        if (SubjectCall.NumArgs >= 3) {
            return SubjectCall.Args[1];
        }
        return new Undef(SubjectCall.ResultType);
    }
    protected virtual Value Accumulate(IRBuilder builder, Value currAccum, Value currItem, BasicBlock skipBlock)
    {
        int lambdaIdx = SubjectCall.NumArgs >= 3 ? 2 : 1;
        return builder.CreateLambdaInvoke(SubjectCall, lambdaIdx, currAccum, currItem);
    }
    protected virtual Value MapResult(IRBuilder builder, Value accum)
    {
        if (SubjectCall.NumArgs >= 4) {
            return builder.CreateLambdaInvoke(SubjectCall, 3, accum);
        }
        return accum;
    }
}
internal class CountSink : AggregationSink
{
    public CountSink(CallInst call)
        : base(call) { }

    bool _mayBeLongSource;

    protected override Value GetSeed(IRBuilder builder, EstimatedSourceLen sourceLen)
    {
        // If `estimCount != null`, the source size is guaranteed to fit in an int32.
        _mayBeLongSource = sourceLen.Length == null || sourceLen.IsUnderEstimation;

        return ConstInt.CreateI(0);
    }
    protected override Value Accumulate(IRBuilder builder, Value currAccum, Value currItem, BasicBlock skipBlock)
    {
        // Assume that bools are always normalized to 0/1.
        var inc = SubjectCall.Args.Length >= 2
            ? builder.CreateLambdaInvoke(SubjectCall, 1, currItem)
            : ConstInt.CreateI(1);

        var op = _mayBeLongSource ? BinaryOp.AddOvf : BinaryOp.Add;
        return builder.CreateBin(op, currAccum, inc);
    }
    protected override Value MapResult(IRBuilder builder, Value accum)
    {
        return accum;
    }
}

internal sealed class SumSink(CallInst call) : AggregationSink(call)
{
    public static bool IsSupported(CallInst call)
        => call.NumArgs is 1 or 2 && call.ResultType.Kind is TypeKind.Int32 or TypeKind.Int64;

    protected override Value GetSeed(IRBuilder builder, EstimatedSourceLen sourceLen)
        => ConstInt.Create(SubjectCall.ResultType, 0);

    protected override Value Accumulate(IRBuilder builder, Value currAccum, Value currItem, BasicBlock skipBlock)
    {
        if (SubjectCall.NumArgs == 2) currItem = builder.CreateLambdaInvoke(SubjectCall, 1, currItem);
        return builder.CreateBin(BinaryOp.AddOvf, currAccum, currItem);
    }
}
