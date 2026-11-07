namespace Cilica.Passes;

using Cilica.Passes.Linq;

public class ExpandLinq : IMethodPass
{
    readonly TypeDefOrSpec t_Enumerable, t_IEnumerableOfT0;
    readonly bool _deferEnumeratorSources;

    public ExpandLinq(ModuleResolver resolver) : this(resolver, deferEnumeratorSources: false) { }

    internal ExpandLinq(ModuleResolver resolver, bool deferEnumeratorSources)
    {
        _deferEnumeratorSources = deferEnumeratorSources;
        t_Enumerable = resolver.Import(typeof(Enumerable));
        t_IEnumerableOfT0 = resolver.Import(typeof(IEnumerable<>)).GetSpec(GenericContext.Empty);
    }

    static IMethodPass IMethodPass.Create<TSelf>(Compilation comp)
        => new ExpandLinq(comp.Resolver);

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var queries = new List<LinqSourceNode>();

        foreach (var inst in ctx.Method.Instructions()) {
            if (inst is CallInst call && CreatePipe(call) is { } pipe) {
                queries.Add(pipe);
            }
        }

        var validated = new HashSet<CallInst>();
        foreach (var query in queries) {
            query.Emit(validated);
            ctx.Logger.Trace($"Expanded LINQ query ending in '{query.GetSink().SubjectCall.Method}' into a loop.");
        }
        return queries.Count > 0 ? MethodInvalidations.Loops : 0;
    }

    private LinqSourceNode? CreatePipe(CallInst call)
    {
        var sink = CreateSink(call);
        if (sink == null) {
            return null;
        }
        var source = CreateStage(call.GetOperandRef(0), sink);

        // A helper may expose fixed storage after inlining. Keep the original
        // sink available until then instead of committing to an enumerator loop.
        if (_deferEnumeratorSources && source is EnumeratorSource) return null;

        if (source is EnumeratorSource && !RuntimeLibrary.HasKnownSequenceDispatch(source.PhysicalSource.Operand)) {
            return null;
        }

        return IsProfitableToExpand(source, sink) ? source : null;
    }

    private static bool IsProfitableToExpand(LinqSourceNode source, LinqSink sink)
    {
        // Preserve the runtime's specialized direct Sum paths (including their
        // overflow behavior). Fuse only staged queries or selector overloads.
        if (sink is SumSink && source.Drain == sink && sink.SubjectCall.NumArgs == 1) return false;
        // Unfiltered Count()/Any() is not profitable because we always scan over
        // the entire source, and LINQ specializes over collections and such. 
        if (sink.SubjectCall is { NumArgs: 1, Method.Name: "Count" or "Any" }) {
            return false;
        }
        // Expanding enumerator sources may not be profitable because 
        // Linq can special-case source types and defer to e.g. Array.Copy().
        // Similarly, expanding an enumerator source to a loop sink is an expansive no-op.
        if (source is EnumeratorSource && source.Drain == sink) {
            return sink is not (ConcretizationSink or LoopSink);
        }
        // LINQ specializes Range().ToArray() and ToList() over contiguous output storage.
        // - https://github.com/dubiousconst282/DistIL/issues/25
        // - https://github.com/dotnet/runtime/pull/87992
        if (source is IntRangeSource && source.Drain == sink) {
            return sink is not ListOrArraySink ||
                   (source.SubjectCall.Args is [_, ConstInt count] && count.Value <= 64);
        }
        return true;
    }

    private LinqSink? CreateSink(CallInst call)
    {
        var method = call.Method;
        if (method.DeclaringType == t_Enumerable) {
#pragma warning disable format
            return method.Name switch {
                "ToList" or "ToArray"       => new ListOrArraySink(call),
                "ToHashSet"                 => new HashSetSink(call),
                "ToDictionary" when method.ParamSig is [_, { Type.Name: "Func`2" }, ..]
                                           => new DictionarySink(call),
                "Aggregate"                 => new AggregationSink(call),
                "Count"                     => new CountSink(call),
                "Sum" when SumSink.IsSupported(call) => new SumSink(call),
                "First" or "FirstOrDefault" => new FindSink(call),
                "Any" or "All"              => new QuantifySink(call),
                _ => null
            };
#pragma warning restore format
        }
        if (method.Name == "GetEnumerator") {
            var declType = (method.DeclaringType as TypeSpec)?.Definition ?? method.DeclaringType;

            if (declType == t_IEnumerableOfT0.Definition || declType.Inherits(t_IEnumerableOfT0)) {
                return LoopSink.TryCreate(call);
            }
        }
        return null;
    }

    // UseRefs allows for overlapping queries to be expanded with no specific order.
    private LinqSourceNode CreateStage(UseRef sourceRef, LinqStageNode drain)
    {
        var source = sourceRef.Operand;

        if (source is CallInst call && call.Method.DeclaringType == t_Enumerable) {
            // Indexed collection operators own their mutation and enumeration semantics.
            if (call.NumArgs > 0 && call.Method.Name != "Range" && !RuntimeLibrary.HasKnownSequenceDispatch(call.Args[0])) {
                return new EnumeratorSource(sourceRef, drain);
            }

            if (call.Method.Name == "Range") {
                return new IntRangeSource(call, drain);
            }
#pragma warning disable format
            var node = call.Method.Name switch {
                "Select"        => new SelectStage(call, drain),
                "Where"         => new WhereStage(call, drain),
                "OfType"        => new OfTypeStage(call, drain),
                "Cast"          => new CastStage(call, drain),
                "SelectMany"    => new FlattenStage(call, drain),
                "Skip"          => new SkipStage(call, drain),
                "Take" when call.Method.ParamSig[1] == PrimType.Int32
                                => new TakeStage(call, drain),
                _ => default(LinqStageNode)
            };
#pragma warning restore format
            if (node != null) {
                return CreateStage(call.GetOperandRef(0), node);
            }
        }

        var type = source.ResultType;

        if (type is ArrayType || type.Kind == TypeKind.String) {
            return new MemorySource(sourceRef, drain);
        }

        return new EnumeratorSource(sourceRef, drain);
    }
}
