namespace Cilica.Passes;

using Cilica.Analysis;
using Cilica.AsmIO;
using Cilica.IR.Utils;

public class PresizeLists : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var loopAnalysis = ctx.GetAnalysis<LoopAnalysis>();
        var domTree = ctx.GetAnalysis<DominatorTree>();
        var regions = ctx.GetAnalysis<ProtectedRegionAnalysis>();

        var candidateLists = new Dictionary<TrackedValue, (bool HasConditionalAdd, int AddCallCount)>();
        int numChanges = 0;
        int arrayBuilders = 0;

        foreach (var loop in loopAnalysis.GetShapedLoops(innermostOnly: true)) {
            // Must be able to calculate loop trip count before entering it
            if (!loop.HasKnownTripCount(domTree, loop.PreHeader.Last)) continue;

            // Find Add() calls inside loop
            foreach (var block in loop.Blocks) {
                foreach (var inst in block) {
                    if (!(inst is CallInst call && IsListAdd(call.Method))) continue;

                    // List must have been defined outside loop
                    if (call.Args is not [TrackedValue list, ..] || !loop.IsInvariant(list)) continue;

                    if (list is not NewObjInst { NumArgs: 0 } || !IsConfined(list)) {
                        continue;
                    }

                    ref var info = ref candidateLists.GetOrAddRef(list);

                    // The call is executed unconditionally on every loop iteration iff
                    // the block it is defined in dominates the loop latch.
                    // We could probably speculate a reasonable initial capacity given
                    // PGO data, but that's thinking way ahead :')
                    if (domTree.Dominates(inst.Block, loop.Latch)) {
                        info.AddCallCount++;
                    } else {
                        info.HasConditionalAdd = true;
                    }
                }
            }

            // Pre-size lists
            foreach (var (list, info) in candidateLists) {
                if (info.AddCallCount == 0) continue; // nothing we can do

                var listAlloc = (NewObjInst)list;

                if (!CanSinkAlloc(listAlloc, loop.PreHeader.Last, domTree, regions)) {
                    continue;
                }

                var builder = new IRBuilder(loop.PreHeader);
                var tripCount = loop.GetTripCount(builder)!;
                bool fitsCapacity = info.AddCallCount == 1 || tripCount is ConstInt constant &&
                    constant.Value <= int.MaxValue / info.AddCallCount;
                Value numAddedItems;

                if (fitsCapacity) {
                    numAddedItems = builder.CreateMul(tripCount, ConstInt.CreateI(info.AddCallCount));
                } else {
                    var wide = builder.CreateMul(builder.CreateConvert(tripCount, PrimType.Int64), ConstInt.CreateL(info.AddCallCount));
                    numAddedItems = builder.CreateSelect(builder.CreateSle(wide, ConstInt.CreateL(int.MaxValue)),
                        builder.CreateConvert(wide, PrimType.Int32), ConstInt.CreateI(0));
                }

                var ctorWithCap = list.ResultType.FindMethod(".ctor", new MethodSig(PrimType.Void, [PrimType.Int32]));
                var newList = builder.CreateNewObj(ctorWithCap, numAddedItems);
                listAlloc.ReplaceWith(newList);

                if (!info.HasConditionalAdd && fitsCapacity) {
                    arrayBuilders += TryReplaceArrayBuilder(loop, builder, newList, domTree) ? 1 : 0;
                }

                numChanges++;
            }

            candidateLists.Clear();
        }

        if (numChanges > 0) ctx.Logger.Trace($"Presized {numChanges} lists; replaced {arrayBuilders} with direct array construction.");
        return numChanges > 0 ? MethodInvalidations.DataFlow : MethodInvalidations.None;
    }

    private static bool IsConfined(TrackedValue list)
        => list.Uses().All(use => use.OperIndex == 0 && use.Parent is CallInst call && IsListMethod(call.Method) &&
            call.Method.Name is "Add" or "ToArray" or "get_Count" or "get_Item");

    // A confined list construction can write directly to its result array.
    // Observable lists retain Add's versioning and incremental mutation semantics.
    private static bool TryReplaceArrayBuilder(ShapedLoopInfo loop, IRBuilder builder, TrackedValue list, DominatorTree domTree)
    {
        var addCalls = new List<CallInst>();
        var toArrayCall = default(CallInst);
        int numToArrayCalls = 0, numOtherUses = 0;

        foreach (var user in list.Users()) {
            if (loop.Contains(user.Block)) {
                // Loop uses must be only from Add() calls
                if (user is CallInst call && IsListMethod(call.Method) && call.Method.Name == "Add") {
                    addCalls.Add(call);
                } else {
                    return false;
                }
            } else {
                // Uses from elsewhere can do whatever, we just need to track ToArray() calls
                if (user is CallInst call && IsListMethod(call.Method) && call.Method.Name == "ToArray") {
                    toArrayCall = call;
                    numToArrayCalls++;
                } else {
                    numOtherUses++;
                }
            }
        }

        var continuation = loop.Exit.Last is LeaveInst leave ? leave.Target : loop.Exit;
        if (toArrayCall == null || numToArrayCalls != 1 || numOtherUses != 0 ||
            !domTree.Dominates(continuation, toArrayCall.Block) ||
            list is not NewObjInst { Args: [{ ResultType.Kind: TypeKind.Int32 }] } listAlloc) return false;

        var elemType = list.ResultType.GenericParams[0];
        var array = builder.CreateNewArray(elemType, listAlloc.Args[0]).SetName("arrbuilder_items");
        toArrayCall.ReplaceWith(array);
        listAlloc.Remove();
        Debug.Assert(listAlloc.NumUses == addCalls.Count);

        // Canonical counting loops use their induction variable as the destination index.
        if (addCalls.Count == 1 && loop is CountingLoopInfo { IsCanonical: true } forLoop) {
            var basePtr = LoopStrengthReduction.CreateGetDataPtrRange(builder, array, getCount: false).BasePtr;

            foreach (var call in addCalls) {
                builder.SetPosition(call, InsertionDir.After);

                var ptr = builder.CreatePtrOffset(basePtr, forLoop.Counter);
                builder.CreateStore(ptr, call.Args[1]);
                call.Remove();
            }
        } else {
            var idxVar = builder.Method.CreateVar(PrimType.Int32, "arrbuilder_idx");
            builder.CreateStore(idxVar, ConstInt.CreateI(0));

            foreach (var call in addCalls) {
                builder.SetPosition(call, InsertionDir.After);

                builder.CreateArrayStore(array, builder.CreateLoad(idxVar), call.Args[1]); // array[idx] = value
                builder.CreateStore(idxVar, builder.CreateAdd(builder.CreateLoad(idxVar), ConstInt.CreateI(1))); // idx++
                call.Remove();
            }
        }
        return true;
    }

    // Checks if `alloc` can be moved to after `inst`
    private static bool CanSinkAlloc(NewObjInst alloc, Instruction inst, DominatorTree domTree, ProtectedRegionAnalysis regions)
    {
        var sourceRegion = regions.GetBlockRegion(alloc.Block);
        for (var region = regions.GetBlockRegion(inst.Block); region != sourceRegion; region = region.Parent) {
            if (region == null || region.GetHandlerGuard() != null ||
                region.StartBlock.Guards().Any(g => g.Kind != GuardKind.Finally)) return false;
        }
        foreach (var user in alloc.Users()) {
            if (!domTree.Dominates(inst, user)) {
                return false;
            }
        }
        return true;
    }

    private static bool IsListAdd(MethodDesc method)
    {
        // TODO: support for ImmutableArray builders
        return method.Name == "Add" && IsListMethod(method);
    }
    private static bool IsListMethod(MethodDesc method)
    {
        return method.DeclaringType.IsCorelibType(typeof(List<>));
    }
}
