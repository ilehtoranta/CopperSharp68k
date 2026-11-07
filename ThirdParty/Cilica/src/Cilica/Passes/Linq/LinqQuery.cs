namespace Cilica.Passes.Linq;

using Cilica.IR.Utils;

internal abstract class LinqSink : LinqStageNode
{
    public MethodBody Method { get; }
    public Value? Result { get; protected set; }

    public LinqSink(CallInst call)
        : base(call, null!)
    {
        Method = call.Block.Method;
    }

    public virtual void EmitHead(IRBuilder builder, EstimatedSourceLen estimLen) { }
    public virtual void EmitTail(IRBuilder builder) { }
    public abstract override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData);
}

internal abstract class LinqStageNode
{
    public CallInst SubjectCall { get; }
    public LinqStageNode Drain { get; }

    /// <summary> Whether this stage may discard source items. </summary>
    public virtual bool IsFiltering => false;
    /// <summary> Whether this stage may drain more items than given by the source. </summary>
    public virtual bool IsGenerating => false;

    protected LinqStageNode(CallInst call, LinqStageNode drain)
        => (SubjectCall, Drain) = (call, drain);

    // Queries are expanded from front-to-back, for example:
    //  Source()                // Front
    //    .Select(MapFn)        // Drain #1
    //    .Where(FilterFn)      // Drain #2
    //    .ToArray();           // Sink
    //
    // Will be rewritten in this way:
    //  Head();
    //  foreach (var item in Source()) { // Loop created by Source(), item propagated down through the chain
    //    var item2 = MapFn(item);
    //    if (!FilterFn(item2)) goto SkipBlock;
    //    Body(item2);
    // SkipBlock:
    //  }
    // Exit:
    //  Tail();
    public virtual void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
        => Drain.EmitBody(builder, currItem, loopData);

    public virtual void Prepare(BodyLoopData loopData) { }

    public void ValidateArguments(HashSet<CallInst> validated)
    {
        if (SubjectCall is not { Method: MethodDefOrSpec method } call ||
            method.DeclaringType.Namespace != "System.Linq" || method.DeclaringType.Name != "Enumerable" ||
            !validated.Add(call)) return;

        var builder = new IRBuilder(call, InsertionDir.Before);
        if (method.Name == "Range") {
            var exception = builder.Resolver.Import(typeof(ArgumentOutOfRangeException));
            var generic = GenericParamType.GetUnboundM(0);
            var negative = exception.FindMethod("ThrowIfNegative", new MethodSig(PrimType.Void, [generic, PrimType.String], numGenPars: 1)).GetSpec([PrimType.Int32]);
            var greater = exception.FindMethod("ThrowIfGreaterThan", new MethodSig(PrimType.Void, [generic, generic, PrimType.String], numGenPars: 1)).GetSpec([PrimType.Int64]);
            var name = ConstString.Create("count");
            builder.CreateCall(negative, call.Args[1], name);
            var last = builder.CreateSub(builder.CreateAdd(builder.CreateConvert(call.Args[0], PrimType.Int64),
                builder.CreateConvert(call.Args[1], PrimType.Int64)), ConstInt.CreateL(1));
            builder.CreateCall(greater, last, ConstInt.CreateL(int.MaxValue), name);
            return;
        }

        var throwIfNull = builder.Resolver.Import(typeof(ArgumentNullException)).FindMethod("ThrowIfNull",
            new MethodSig(PrimType.Void, [PrimType.Object, PrimType.String]));
        for (int i = 0; i < call.NumArgs; i++) {
            var parameter = method.Definition.Params[i];
            if (i != 0 && !parameter.Type.Name.StartsWith("Func`")) continue;
            var arg = call.Args[i];
            if (arg is ConstString || ConstFolding.FoldCondition(arg) == true) continue;
            // Enumerable operators return a non-null sequence after validating their inputs.
            if (i == 0 && arg is CallInst { Method.DeclaringType.Namespace: "System.Linq", Method.DeclaringType.Name: "Enumerable" }) continue;
            builder.CreateCall(throwIfNull, arg, ConstString.Create(parameter.Name ?? (i == 0 ? "source" : "selector")));
        }
    }

    protected virtual void DeleteSubject()
    {
        Drain?.DeleteSubject();
        if (SubjectCall is { NumUses: 0 }) {
            SubjectCall.Remove();
        }
    }
    public LinqSink GetSink()
    {
        var node = Drain;
        while (node is not LinqSink) {
            node = node.Drain;
        }
        return (LinqSink)node;
    }
}
internal abstract class LinqSourceNode : LinqStageNode
{
    public UseRef PhysicalSource { get; }

    protected LinqSourceNode(LinqStageNode drain, UseRef physicalSource, CallInst? subjectCall = null)
        : base(subjectCall!, drain)
    {
        PhysicalSource = physicalSource;
    }

    public virtual void Emit(HashSet<CallInst> validated)
    {
        for (LinqStageNode? node = this; node != null; node = node.Drain) node.ValidateArguments(validated);
        var sink = GetSink();

        var loop = new LoopBuilder(sink.SubjectCall.Block, "LQ_");
        var firstStage = Drain;
        var loopData = new BodyLoopData(loop);
        for (LinqStageNode? node = firstStage; node != null; node = node.Drain) node.Prepare(loopData);

        EmitHead(loop, out var length, ref firstStage, loopData);
        sink.EmitHead(loop.PreHeader, new EstimatedSourceLen(length, firstStage));

        loop.Build(
            emitCond: EmitMoveNext,
            emitBody: body => firstStage.EmitBody(body, EmitCurrent(body), loopData),
            emitGuard: loopData.Limits.Count == 0 ? null : loopData.EmitGuard
        );
        sink.EmitTail(loop.Exit);

        if (sink is not LoopSink) {
            loop.InsertBefore(sink.SubjectCall);
            if (sink.Result is { } result) {
                if (loopData.HasDisposal) {
                    var slot = sink.Method.CreateVar(result.ResultType, "lq_result", hardExposed: true);
                    loop.Exit.SetPosition(loop.Exit.Block);
                    loop.Exit.CreateStore(slot, result);
                    result = new IRBuilder(sink.SubjectCall, InsertionDir.Before).CreateLoad(slot);
                }
                sink.SubjectCall.ReplaceUses(result);
            }
        }
        DeleteSubject();
        EmitEnd(loop);
    }

    protected static void IntegrateSkipTakeRanges(IRBuilder builder, ref LinqStageNode firstStage, out Value? offset, ref Value length)
    {
        offset = null;

        if (firstStage is SkipStage { SubjectCall.Args: [_, var skipCount] }) {
            // It's important to clamp skipCount both ways to avoid creating GC tracking holes.
            //  offset = clamp(skipCount, 0, (int)count)
            //  startPtr += offset
            //  count -= offset
            offset = builder.CreateMin(
                builder.CreateMax(skipCount, ConstInt.CreateI(0)),
                builder.CreateConvert(length, PrimType.Int32));
            length = builder.CreateSub(length, offset);
            firstStage = firstStage.Drain;
        }
        if (firstStage is TakeStage { SubjectCall.Args: [_, var takeCount] }) {
            // A non-positive Take count produces an empty sequence.
            length = builder.CreateMin(
                builder.CreateConvert(length, PrimType.Int32),
                builder.CreateMax(takeCount, ConstInt.CreateI(0)));
            firstStage = firstStage.Drain;
        }
    }

    protected abstract void EmitHead(LoopBuilder loop, out Value? length, ref LinqStageNode firstStage, BodyLoopData loopData);
    protected abstract Value EmitMoveNext(IRBuilder builder);
    protected abstract Value EmitCurrent(IRBuilder builder);
    protected virtual void EmitEnd(LoopBuilder loop) { }
}

internal record BodyLoopData
{
    public LoopBuilder SourceLoop;
    public BasicBlock SkipBlock;
    public LoopAccumVarFactory CreateAccum;
    public List<LocalSlot> Limits = new();
    public EnumeratorLoop? Enumeration;
    public bool HasDisposal;

    public IRBuilder PreHeader => SourceLoop.PreHeader;
    public IRBuilder Header => SourceLoop.Header;
    public IRBuilder Exit => SourceLoop.Exit;

    public BodyLoopData(LoopBuilder loop)
    {
        SourceLoop = loop;
        SkipBlock = loop.Latch.Block;
        CreateAccum = loop.CreateAccum;
    }

    public Value EmitGuard(IRBuilder builder)
    {
        Value? result = null;
        foreach (var limit in Limits) {
            var condition = builder.CreateSgt(builder.CreateLoad(limit), ConstInt.CreateI(0));
            result = result == null ? condition : builder.CreateAnd(result, condition);
        }
        return result ?? ConstInt.CreateI(1);
    }

    public BasicBlock GetExitTarget(BasicBlock target) => Enumeration?.GetExitTarget(target) ?? target;
}
internal delegate Value LoopAccumVarFactory(Value seed, Func<Value, Value> emitUpdate, TypeDesc? resultType = null);

internal struct EstimatedSourceLen
{
    public Value? Length { get; }
    public bool IsUnderEstimation { get; }
    public bool IsOverEstimation { get; }

    [MemberNotNullWhen(true, nameof(Length))]
    public bool IsExact => Length != null && !IsUnderEstimation && !IsOverEstimation;

    public EstimatedSourceLen(Value? length, LinqStageNode entryStage)
    {
        Length = length;

        if (length == null) return;

        for (var node = entryStage; node != null; node = node.Drain) {
            IsOverEstimation |= node.IsFiltering;
            IsUnderEstimation |= node.IsGenerating;
        }
    }
}
