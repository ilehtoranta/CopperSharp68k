namespace Cilica.Passes.Vectorization;

using Cilica.Analysis;

internal static class VectorizationLegality
{
    public static bool Check(CountingLoopInfo loop, MethodTransformContext context, out List<Value> boundsChecks, out string? reason)
    {
        boundsChecks = new();
        reason = null;

        if (loop.Loop.NumBlocks != 2 || !loop.IsInvariant(loop.End)) {
            reason = "requires a two-block loop with an invariant bound";

            return false;
        }

        var regions = context.GetAnalysis<ProtectedRegionAnalysis>();
        var dominators = context.GetAnalysis<DominatorTree>();
        var region = regions.GetBlockRegion(loop.PreHeader);

        if (regions.GetBlockRegion(loop.Header) != region || regions.GetBlockRegion(loop.Latch) != region ||
            loop.Header.HasGuards || loop.Latch.HasGuards ||
            loop.Header.NonPhis().Any(inst => inst != loop.ExitCondition && inst != loop.Header.Last)) {
            reason = "loop headers and exception boundaries must be uniform";

            return false;
        }

        bool pointerCounter = loop.Counter.ResultType.IsPointerLike();

        if (pointerCounter) {
            if (loop.ExitCondition.Op != CompareOp.Ult ||
                loop.UpdatedCounter is not PtrOffsetInst { Index: ConstInt { Value: 1 } } update ||
                update.BasePtr != loop.Counter || !TryGetStorage(loop.Start, loop, out var storage) ||
                update.Stride != storage.Element.Kind.Size() ||
                loop.End is not PtrOffsetInst end || end.BasePtr != loop.Start ||
                end.Stride != update.Stride || !IsBounded(end.Index, storage, loop, dominators, requireExactArrayLength: true)) {
                reason = "pointer induction requires a proven storage extent";

                return false;
            }
        } else if (!loop.IsCanonical || loop.Counter.ResultType.Kind != TypeKind.Int32 ||
                   loop.End.ResultType.StackType != StackType.Int || loop.ExitCondition.Op != CompareOp.Slt) {
            reason = "requires canonical signed int32 induction";

            return false;
        }

        foreach (var phi in loop.Header.Phis()) {
            if (phi == loop.Counter) {
                continue;
            }

            if (phi.ResultType.IsFloat() || phi.GetValue(loop.Latch) is not Instruction step ||
                step.Operands.ToArray().Count(value => value == phi) != 1) {
                reason = "reduction must be an integer recurrence";

                return false;
            }
        }

        var accesses = new List<(Storage Storage, bool Writes)>();

        foreach (var inst in loop.Latch) {
            if (inst is BinaryInst { Op: BinaryOp.Shl or BinaryOp.Shra or BinaryOp.Shrl } shift &&
                (!loop.IsInvariant(shift.Right) || shift.Right.ResultType.StackType != StackType.Int ||
                 shift.Op == BinaryOp.Shra && shift.Left.ResultType.Kind.IsUnsigned())) {
                reason = "shift counts and signedness must match the vector operation";

                return false;
            }

            if (inst is ArrayAddrInst address &&
                (!TryGetArrayStorage(loop, address, out var arrayStorage) ||
                 !ProveOrGuardBound(loop, arrayStorage, dominators, boundsChecks))) {
                reason = "array accesses require invariant storage and the loop index without casts or variance checks";

                return false;
            }

            if (inst is MemoryInst memory) {
                if (memory.IsVolatile || !TryGetAccess(loop, memory, out var storage) ||
                    !pointerCounter && !ProveOrGuardBound(loop, storage, dominators, boundsChecks)) {
                    reason = "memory accesses require nonvolatile unit-stride storage with a proven or guardable extent";

                    return false;
                }

                bool writes = memory is StoreInst;

                if (writes && storage.Source.ResultType.Kind == TypeKind.String) {
                    reason = "string storage is read-only";

                    return false;
                }

                foreach (var previous in accesses) {
                    if ((writes || previous.Writes) && previous.Storage.Source != storage.Source &&
                        !AreIndependent(previous.Storage.Source, storage.Source)) {
                        reason = "memory dependence cannot be excluded";

                        return false;
                    }
                }

                accesses.Add((storage, writes));
            } else if (inst is BinaryInst { ChecksOverflow: true } || inst is ConvertInst { CheckOverflow: true } ||
                       inst is CallInst call && !VectorTranslator.IsSafeMathCall(call)) {
                reason = "operation can throw or has unsupported effects";

                return false;
            }
        }

        return true;
    }

    private static bool TryGetAccess(CountingLoopInfo loop, MemoryInst access, out Storage storage)
    {
        storage = default;
        Value address = access.Address;

        if (address == loop.Counter && loop.Counter.ResultType.IsPointerLike()) {
            return TryGetStorage(loop.Start, loop, out storage) && access.ElemType == storage.Element;
        }

        if (address is ArrayAddrInst arrayAddress) {
            return TryGetArrayStorage(loop, arrayAddress, out storage) && access.ElemType == storage.Element;
        }

        if (address is not PtrOffsetInst offset || offset.Index != loop.Counter ||
            loop.Counter.ResultType.IsPointerLike() || !loop.IsInvariant(offset.BasePtr) ||
            !TryGetStorage(offset.BasePtr, loop, out storage)) {
            return false;
        }

        return offset.Stride == storage.Element.Kind.Size() && access.ElemType == storage.Element;
    }

    private static bool TryGetArrayStorage(CountingLoopInfo loop, ArrayAddrInst address, out Storage storage)
    {
        storage = default;

        if (address.Array.ResultType is not ArrayType array || !loop.IsInvariant(address.Array) ||
            address.Index != loop.Counter || loop.Counter.ResultType.IsPointerLike() ||
            address.IsCasting || address.RequiresVarianceCheck) {
            return false;
        }

        storage = new(address.Array, array.ElemType);
        return true;
    }

    private static bool ProveOrGuardBound(CountingLoopInfo loop, Storage storage, DominatorTree dominators, List<Value> boundsChecks)
    {
        if (IsBounded(loop.End, storage, loop, dominators)) return true;
        if (storage.Source.ResultType is not ArrayType) return false;

        // Array identity and extent cannot change. Check each unknown extent once
        // before entering the vector loop, retaining checked scalar accesses on failure.
        if (!boundsChecks.Contains(storage.Source)) boundsChecks.Add(storage.Source);
        return true;
    }

    private static bool TryGetStorage(Value pointer, CountingLoopInfo loop, out Storage storage)
    {
        storage = default;

        if (pointer is not CallInst { NumArgs: 1 } call) {
            return false;
        }

        var source = call.Args[0];

        if (call.Method.Name == "GetArrayDataReference" && source.ResultType is ArrayType array &&
            call.Method.DeclaringType.IsCorelibType(typeof(System.Runtime.InteropServices.MemoryMarshal))) {
            storage = new(source, array.ElemType);

            return true;
        }

        if (call.Method.Name == "GetPinnableReference" && source.ResultType.Kind == TypeKind.String &&
            call.Method.DeclaringType.IsCorelibType(typeof(string))) {
            storage = new(source, PrimType.Char);

            return true;
        }

        if (call.Method.Name == "GetPinnableReference" && source is LocalSlot slot &&
            slot.Type.IsCorelibType() && slot.Type.Name is "Span`1" or "ReadOnlySpan`1") {
            int initializations = 0;

            foreach (var use in slot.Uses()) {
                if (use.Parent is StoreInst store && use.OperIndex == 0 && store.Value is Argument argument &&
                    argument.ResultType == slot.Type && !loop.Contains(store.Block)) {
                    initializations++;
                } else if (use.OperIndex != 0 || use.Parent is not CallInst accessor || accessor.Method.DeclaringType != slot.Type ||
                    accessor.Method.Name is not ("get_Length" or "get_Item" or "GetPinnableReference")) {
                    return false;
                }
            }

            if (initializations == 1) {
                storage = new(source, slot.Type.GenericParams[0]);

                return true;
            }
        }

        return false;
    }

    private static bool IsBounded(Value bound, Storage storage, CountingLoopInfo loop, DominatorTree dominators, bool requireExactArrayLength = false)
    {
        // Signed upper bounds alone may be negative. Integer induction then skips
        // the loop, but pointer induction needs a nonnegative storage extent.
        if (storage.Source.ResultType is ArrayType &&
            ArrayBoundsFacts.IsLengthBound(bound, storage.Source, loop.PreHeader, dominators, exact: requireExactArrayLength)) return true;
        while (bound is ConvertInst { CheckOverflow: false } conversion) {
            bound = conversion.Value;
        }

        if (bound is CilIntrinsic.ArrayLen length && length.Args[0] == storage.Source) {
            return true;
        }

        if (bound is CallInst { Method.Name: "get_Length", Args: [var source] } && source == storage.Source &&
            (storage.Source.ResultType.Kind == TypeKind.String || storage.Source is LocalSlot)) {
            return true;
        }

        if (storage.Source is CilIntrinsic.NewArray allocation && bound.Equals(allocation.Args[0])) {
            return true;
        }

        long? count = storage.Source switch {
            CilIntrinsic.NewArray { Args: [ConstInt size] } => size.Value,
            ConstString text => text.Value.Length,
            _ => null
        };

        return count.HasValue && bound is ConstInt constant && constant.Value >= 0 && constant.Value <= count.Value;
    }

    private static bool AreIndependent(Value left, Value right)
        // Array storage either does not overlap or is the same array. All accepted
        // accesses use the same index and element stride, so identical arrays have
        // no cross-iteration dependence. This does not apply to overlapping spans.
        => left.ResultType is ArrayType a && right.ResultType is ArrayType b && a.ElemType == b.ElemType ||
           left is CilIntrinsic.NewArray && right is CilIntrinsic.NewArray && left != right ||
           left.ResultType.Kind == TypeKind.String || right.ResultType.Kind == TypeKind.String;

    readonly record struct Storage(Value Source, TypeDesc Element);
}
