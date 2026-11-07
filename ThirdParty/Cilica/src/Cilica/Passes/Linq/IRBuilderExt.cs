namespace Cilica.Passes.Linq;

using Cilica.IR.Utils;

internal static class IRBuilderExt
{
    public static Value CreateLambdaInvoke(this IRBuilder ib, CallInst call, int lambdaIndex, params Value[] args)
    {
        var invoker = call.Method.ParamSig[lambdaIndex].Type.FindMethod("Invoke");
        return ib.CreateCallVirt(invoker, args.Prepend(call.Args[lambdaIndex]).ToArray());
    }
    public static Value CreateLambdaInvoke_ItemAndIndex(this IRBuilder ib, CallInst call, int lambdaIndex, Value currItem, LoopAccumVarFactory createAccum)
    {
        var invoker = call.Method.ParamSig[lambdaIndex].Type.FindMethod("Invoke");
        var lambda = call.Args[lambdaIndex];

        if (invoker.ParamSig.Count == 3) {
            CallInst invocation = null!;
            createAccum(ConstInt.CreateI(-1), previousIndex => {
                var index = ib.CreateBin(BinaryOp.AddOvf, previousIndex, ConstInt.CreateI(1));
                invocation = ib.CreateCallVirt(invoker, lambda, currItem, index);
                return index;
            });
            return invocation;
        }
        return ib.CreateCallVirt(invoker, lambda, currItem);
    }

    public static void Throw(this IRBuilder ib, Type exceptionType, Value? cond = null)
    {
        var modResolver = ib.Method.Definition.Module.Resolver;
        var exceptCtor = modResolver.Import(exceptionType)
            .FindMethod(".ctor", new MethodSig(PrimType.Void, [], isInstance: true));

        var throwHelper = ib.Method.CreateBlock().SetName("LQ_ThrowHelper");
        var exceptObj = new NewObjInst(exceptCtor, []);
        throwHelper.InsertLast(exceptObj);
        throwHelper.InsertLast(new ThrowInst(exceptObj));

        if (cond == null) {
            ib.SetBranch(throwHelper);
        } else {
            ib.ForkIf(ib.CreateNe(cond, Const.CreateZero(cond.ResultType)), throwHelper);
        }
    }
}
