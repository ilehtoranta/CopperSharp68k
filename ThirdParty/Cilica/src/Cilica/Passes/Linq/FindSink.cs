namespace Cilica.Passes.Linq;

using Cilica.IR;
using Cilica.IR.Utils;

//First{OrDefault}(pred?)
internal class FindSink : LinqSink
{
    public FindSink(CallInst call)
        : base(call) { }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var exit = loopData.Exit;
        string op = SubjectCall.Method.Name;

        // First(source, predicate)
        // FirstOrDefault(source, predicate?, defaultValue?)
        if (SubjectCall.Method.ParamSig is [_, { Type.Name: "Func`2" }, ..]) {
            // goto pred(currItem) ? NextBody : Latch
            var cond = builder.CreateLambdaInvoke(SubjectCall, 1, currItem);
            builder.ForkIf(cond, loopData.SkipBlock, negate: true);
        }
        
        var type = SubjectCall.ResultType;
        var resultSlot = Method.CreateVar(type, "lq_found", hardExposed: true);
        if (op.EndsWith("OrDefault")) {
            var defaultValue = SubjectCall.Method.ParamSig[^1] == type
                ? SubjectCall.Args[^1]
                : loopData.PreHeader.CreateDefaultOf(type);
            loopData.PreHeader.CreateStore(resultSlot, defaultValue);
        } else {
            exit.Throw(typeof(InvalidOperationException));
            exit.SetPosition(exit.Method.CreateBlock(insertAfter: exit.Block));
        }
        builder.CreateStore(resultSlot, currItem);
        Result = exit.CreateLoad(resultSlot);
        // LoopBuilder expects an edge to the latch from the last body block, 
        // create a dead cond branch here so it won't complain about it.
        builder.ForkIf(ConstInt.CreateI(1), loopData.GetExitTarget(exit.Block));
    }
}

//Any(pred), All(pred)
internal class QuantifySink : LinqSink
{
    public QuantifySink(CallInst call)
        : base(call) { }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var exit = loopData.Exit;
        int normRes = SubjectCall.Method.Name == "Any" ? 0 : 1;
        var resultSlot = Method.CreateVar(PrimType.Bool, "lq_quantified", hardExposed: true);
        loopData.PreHeader.CreateStore(resultSlot, ConstInt.CreateI(normRes));
        Result = exit.CreateLoad(resultSlot);
        var found = builder.Method.CreateBlock(insertAfter: builder.Block);
        var matched = new IRBuilder(found);
        matched.CreateStore(resultSlot, ConstInt.CreateI(1 - normRes));
        matched.SetBranch(loopData.GetExitTarget(exit.Block));
        var cond = builder.CreateLambdaInvoke(SubjectCall, 1, currItem);
        builder.ForkIf(builder.CreateNe(cond, ConstInt.CreateI(normRes)), found);
    }
}
