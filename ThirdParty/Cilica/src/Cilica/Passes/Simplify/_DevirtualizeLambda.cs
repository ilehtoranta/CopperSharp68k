namespace Cilica.Passes;

using System.Runtime.CompilerServices;

using Cilica.IR.Utils;

partial class SimplifyInsts
{
    // Directize delegate invokes if target is known:
    //
    // Func = Func`2[int, bool]
    //  BB_Header:
    //    r2 = ldfld Data::LambdaCache1 -> Func
    //    r3 = cmp.ne r2, null -> bool
    //    goto r3 ? BB_Result : BB_CacheLoad
    //  BB_CacheLoad:
    //    r5 = ldfld Data::Instance -> Data
    //    r6 = funcaddr bool Data::Lambda1(Data, int) -> void*
    //    r7 = newobj Func::.ctor(object: r5, nint: r6) -> Func
    //    stfld Data::LambdaCache1, r7
    //    goto BB_Result
    //  BB_Result:
    //    r9 = phi [BB_Header: r2], [BB_CacheLoad: r7] -> Func
    //    ...
    //    r25 = callvirt Func::Invoke(this: r9, int: r24) -> bool
    // ->
    //  BB_Result:
    //    r5 = ldfld Data::Instance -> Data
    //    r25 = call Data::Lambda1(Data: r5, int: r24) -> bool
    private bool DevirtualizeLambda(CallInst call, ICompilationLogger logger)
    {
        if (call is not { Method.Name: "Invoke", IsStatic: false, Args: [var lambdaInstance, ..] }) return false;

        if (lambdaInstance is PhiInst { NumArgs: 2 } phi) {
            var phiArg1 = phi.GetValue(0);
            var phiArg2 = phi.GetValue(1);
            var allocInst = (phiArg1 as NewObjInst) ?? (phiArg2 as NewObjInst);
            var cacheLoad = (phiArg1 as LoadInst) ?? (phiArg2 as LoadInst);

            if (allocInst == null || cacheLoad == null ||
                !MatchCache(phi, allocInst, cacheLoad, out var branch, out var store) ||
                !DevirtWithCtorArgs(call, allocInst, cacheLoad)) return false;

            // Last lambda to be inlined is responsible for cleanup
            if (phi.NumUses == 0) {
                branch.Cond = ConstInt.CreateI(0);
                phi.Remove();
                store.Remove();
                allocInst.Remove();
            }
            return true;
        } else if (lambdaInstance is NewObjInst immAlloc) {
            if (!DevirtWithCtorArgs(call, immAlloc)) return false;
            
            if (immAlloc.NumUses == 0) {
                immAlloc.Remove();
            }
            return true;
        }
        return false;

        bool DevirtWithCtorArgs(CallInst call, NewObjInst alloc, LoadInst? cacheLoad = null)
        {
            if (alloc is not { Args: [var instanceObj, FuncAddrInst { Method: var method } addr] }) return false;
            if (addr.Constraint != null) return false;
            if (call.NumArgs - (method.IsStatic ? 1 : 0) != method.ParamSig.Count) return false;
            // A delegate may capture an unchecked or independently resolved
            // virtual target. Only replace it when a direct call has the same receiver.
            if (method.IsInstance && !method.DeclaringType.IsValueType &&
                !instanceObj.ResultType.IsAssignableTo(method.DeclaringType)) return false;
            if (addr.IsVirtual && addr.Object != instanceObj) return false;

            if (instanceObj is ConstNull) {
                var replacement = new CallInst(method, call.Args[1..].ToArray()) { IsTailCall = call.IsTailCall };
                OnCallReplaced?.Invoke(call, replacement);
                call.ReplaceWith(replacement, insertIfInst: true);
                logger.Trace($"Replaced delegate invocation with a direct call to '{method}'.");
                return true;
            } else if (method.IsInstance) {
                // Cached instance targets must be available on both phi edges.
                if (cacheLoad != null) {
                    if (!IRMatcher.StaticFieldLoad(instanceObj, out var field) ||
                        !field.Attribs.HasFlag(System.Reflection.FieldAttributes.InitOnly)) return false;
                    var fieldAddr = new FieldAddrInst(field);
                    var newLoad = new LoadInst(fieldAddr);
                    // Reading the cache already initialized this type. Its readonly
                    // receiver can be loaded there, outside any consuming loop.
                    // The single-entry allocation arm makes the cache block dominate
                    // the merge and every use of the delegate phi.
                    if (cacheLoad.Address is FieldAddrInst cacheAddr && cacheAddr.Field.DeclaringType == field.DeclaringType &&
                        alloc.Block.NumPreds == 1 && alloc.Block.Preds.First() == cacheLoad.Block) {
                        fieldAddr.InsertAfter(cacheLoad);
                        newLoad.InsertAfter(fieldAddr);
                    } else {
                        fieldAddr.InsertBefore(call);
                        newLoad.InsertBefore(call);
                    }
                    instanceObj = newLoad;
                }
                if (method.DeclaringType.IsValueType) {
                    var address = new CilIntrinsic.UnboxRef(method.DeclaringType, instanceObj);
                    address.InsertBefore(call);
                    instanceObj = address;
                }
                call.Method = method;
                call.SetArg(0, instanceObj);
                call.IsVirtual = addr.IsVirtual;
                logger.Trace($"Replaced delegate invocation with a call to '{method}' using its captured receiver.");
                return true;
            }
            return false;
        }
        static bool MatchCache(PhiInst phi, NewObjInst allocInst, LoadInst cacheLoad,
            out BranchInst branch, out StoreInst store)
        {
            branch = null!;
            store = null!;
            var condBlock = cacheLoad.Block;

            if (!(
                // First load block's must end with "goto cache == null ? BB_CacheLoad : BB_Result"
                condBlock.Last is
                    BranchInst { Cond: CompareInst { Op: CompareOp.Ne, Left: var condCache, Right: ConstNull } } br &&
                    br.Then == phi.Block &&
                    br.Else == allocInst.Block &&
                    condCache == cacheLoad &&
                // BB_CacheLoad must store to the cache field
                allocInst.NumUses == 2 && // phi and next store
                ((phi.GetBlock(0) == condBlock && phi.GetBlock(1) == allocInst.Block) ||
                 (phi.GetBlock(1) == condBlock && phi.GetBlock(0) == allocInst.Block)) &&
                phi.GetValue(condBlock) == cacheLoad && phi.GetValue(allocInst.Block) == allocInst &&
                IRMatcher.StaticFieldLoad(cacheLoad, out var cacheField) &&
                allocInst.Next?.Next is StoreInst cacheStore && 
                cacheStore.Value == allocInst &&
                (cacheStore.Address as FieldAddrInst)?.Field == cacheField &&
                cacheField.DeclaringType is TypeDefOrSpec declType && 
                declType.Definition.HasCustomAttrib(typeof(CompilerGeneratedAttribute))
            )) return false;

            branch = br;
            store = cacheStore;
            return true;
        }
    }
}
