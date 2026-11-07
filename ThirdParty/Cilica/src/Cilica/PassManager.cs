namespace Cilica;

using Cilica.CodeGen.Cil;
using Cilica.Frontend;
using Cilica.Passes;
using Cilica.IR.Utils;

public class PassManager
{
    public required Compilation Compilation { get; init; }
    public List<PipelineSegment> Pipeline { get; } = new();
    public List<IPassInspector> Inspectors { get; } = new();

    readonly Dictionary<MethodDef, MethodBody?> _originalBodies = new();
    readonly Dictionary<MethodDef, ILMethodBody?> _originalIL = new();
    readonly Dictionary<MethodDef, long> _initialRevisions = new();
    readonly Dictionary<MethodDef, MethodCompilationResult> _results = new();
    readonly HashSet<MethodDef> _preservedMethods = new();
    bool _running;

    /// <summary> Adds a new segment to the pass pipeline. </summary>
    /// <param name="applyIndependently"> Whether to apply the segment on all methods independently from others. </param>
    public PipelineSegment AddPasses(bool applyIndependently = false)
    {
        var pipe = new PipelineSegment(this, applyIndependently);
        Pipeline.Add(pipe);
        return pipe;
    }

    /// <summary> Imports, transforms, verifies, and emits the selected methods. Failed methods retain their input bodies. </summary>
    public CompilationResult Run(IReadOnlyCollection<MethodDef> methods) => Execute(methods, importAndEmit: true);

    /// <summary> Transforms and verifies working copies of the supplied IR bodies. </summary>
    public CompilationResult ApplyPasses(IReadOnlyCollection<MethodDef> methods) => Execute(methods, importAndEmit: false);

    private CompilationResult Execute(IReadOnlyCollection<MethodDef> methods, bool importAndEmit)
    {
        Ensure.That(!_running, "Pass managers cannot be entered recursively");

        _running = true;
        _originalBodies.Clear();
        _originalIL.Clear();
        _initialRevisions.Clear();
        _results.Clear();
        _preservedMethods.Clear();

        try {
            foreach (var inspector in Inspectors) {
                inspector.OnBegin(Compilation);
            }

            Compilation.ExportModuleInitializationHelper();
            PrepareMethods(methods, importAndEmit);
            ApplyPipeline(methods);

            foreach (var method in methods) {
                if (method.Body == null || Compilation.HasFailed(method) || _preservedMethods.Contains(method)) {
                    continue;
                }

                try {
                    IRVerifier.Verify(method.Body);
                    bool changed = method.Body.Revision != _initialRevisions[method];

                    if (importAndEmit) {
                        GenerateIL(method);
                    }

                    _results[method].Status = changed ? MethodCompilationStatus.Optimized : MethodCompilationStatus.Unchanged;
                } catch (Exception error) {
                    Fail(method, importAndEmit ? "emission" : "verification", error);
                }
            }

            Compilation.RemoveUnusedArtifacts();

            return new CompilationResult(_results.Values.ToArray());
        } finally {
            _running = false;

            foreach (var inspector in Inspectors) {
                inspector.OnFinish(Compilation);
            }
        }
    }

    private void PrepareMethods(IReadOnlyCollection<MethodDef> methods, bool import)
    {
        long start = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        int count = 0;

        foreach (var method in methods) {
            _results.Add(method, new(method));
            _originalBodies.Add(method, method.Body);
            Compilation.BeginMethod(method);

            try {
                if (import) {
                    _originalIL[method] = method.ILBody;
                    if (method.ILBody != null && (method.IsRuntimeAsync ||
                        method.ImplAttribs.HasFlag(System.Reflection.MethodImplAttributes.NoOptimization))) {
                        _preservedMethods.Add(method);
                        _results[method].Status = MethodCompilationStatus.Unchanged;
                        continue;
                    }
                }

                if (method.Body != null) {
                    IRVerifier.Verify(method.Body);
                    method.Body = IRCloner.CloneBody(method.Body);
                } else if (import && method.ILBody != null) {
                    method.Body = ILImporter.ParseCode(method);
                    count++;
                }

                if (method.Body != null) {
                    DeadCodeElim.RemoveUnreachableBlocks(method.Body);
                    IRVerifier.Verify(method.Body);
                    _initialRevisions[method] = method.Body.Revision;
                }
            } catch (Exception error) {
                Fail(method, "import", error);
            }
        }

        Inspectors.OfType<PassTimingInspector>().FirstOrDefault()?.IncrementStat(typeof(ILImporter), start, count, methods.Count,
            GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    private void GenerateIL(MethodDef method)
    {
        long start = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var symbols = method.GetDebugSymbols() != null ? new SequencePointBuilder(method) : null;
        var generated = ILGenerator.GenerateCode(method.Body!, symbols);
        var generatedSymbols = symbols?.BuildSymbols(generated.Instructions);
        Compilation.EnsureReferencedMembersAccessible(method.Body!);
        method.ILBody = generated;

        if (symbols != null) {
            method.Module.GetDebugSymbols(create: true)!.SetMethodSymbols(method, generatedSymbols);
        }

        Inspectors.OfType<PassTimingInspector>().FirstOrDefault()?.IncrementStat(typeof(ILGenerator), start, 1,
            allocatedBytes: GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    private void Fail(MethodDef method, string phase, Exception error)
    {
        method.Body = _originalBodies[method];

        if (_originalIL.TryGetValue(method, out var original)) {
            method.ILBody = original;
        }

        Compilation.FailMethod(method);
        var result = _results[method];
        result.Status = error is NotSupportedException or NotImplementedException
            ? MethodCompilationStatus.Unsupported : MethodCompilationStatus.Failed;
        result.FailedPhase = phase;
        result.Error = error;
        Compilation.Logger.Error($"Failed during {phase} for '{method}'; input body retained", error);
    }

    private void ApplyPipeline(IReadOnlyCollection<MethodDef> candidates)
    {
        for (int i = 0; i < Pipeline.Count;) {
            int end = i + 1;

            if (!Pipeline[i]._applyIndependently) {
                while (end < Pipeline.Count && !Pipeline[end]._applyIndependently) {
                    end++;
                }
            }

            var segments = Pipeline.AsSpan()[i..end];

            foreach (var method in candidates) {
                if (method.Body == null || Compilation.HasFailed(method) || _preservedMethods.Contains(method)) {
                    continue;
                }

                using var scope = Compilation.Logger.Push(s_ApplyScope, $"Apply passes to '{method}'");
                var context = new MethodTransformContext(Compilation, method.Body);

                try {
                    foreach (var segment in segments) {
                        segment.Run(context, Inspectors.AsSpan());
                    }
                } catch (Exception error) {
                    Fail(method, "transformation", error);
                }
            }

            i = end;
        }
    }

    static readonly LoggerScopeInfo s_ApplyScope = new("Cilica.PassManager.Apply");

    /// <summary>
    /// Searches for candidate transform methods and their dependencies through a depth-first IL scan. <br/>
    /// The returned list is ordered such that called methods appear before callers, if statically known.
    /// </summary>
    public static List<MethodDef> GetCandidateMethodsFromIL(ModuleDef module, CandidateMethodFilter? filter = null)
    {
        var candidates = new List<MethodDef>();

        var worklist = new ArrayStack<(MethodDef Method, bool Entered)>();
        var visited = new RefSet<MethodDef>();

        // Perform DFS on each defined method
        foreach (var seedMethod in module.MethodDefs()) {
            Push(null, seedMethod);

            while (!worklist.IsEmpty) {
                var (method, entered) = worklist.Top;

                if (!entered) {
                    worklist.Top.Entered = true;

                    // Enqueue called methods
                    foreach (ref var inst in method.ILBody!.Instructions.AsSpan()) {
                        if (inst.Operand is MethodDefOrSpec { Definition: var target }) {
                            Push(method, target);
                        }
                    }
                } else {
                    candidates.Add(method);
                    worklist.Pop();
                }
            }
        }
        return candidates;

        void Push(MethodDef? caller, MethodDef target)
        {
            if (target.ILBody != null && !visited.Contains(target)) {
                if (IsIntrinsic(target) || IsIntrinsic(target.DeclaringType)) return;
                
                // Invoke the filter late as it may be expansive.
                if (filter != null && !filter.Invoke(caller, target)) return;

                visited.Add(target);
                worklist.Push((target, false));
            }
        }

        static bool IsIntrinsic(ModuleEntity entity)
        {
            return entity.HasCustomAttrib("System.Runtime.CompilerServices", "IntrinsicAttribute");
        }
    }

    public delegate bool CandidateMethodFilter(MethodDef? caller, MethodDef method);

    public class PipelineSegment
    {
        readonly PassManager _manager = null!;
        internal readonly bool _applyIndependently;

        readonly List<IMethodPass> _passes = new();
        PipelineSegment? _contIfChanged;
        int _maxIters = 1;

        internal PipelineSegment(PassManager manager, bool applyIndependently)
            => (_manager, _applyIndependently) = (manager, applyIndependently);

        public void Run(MethodTransformContext ctx, ReadOnlySpan<IPassInspector> inspectors)
        {
            for (int i = 0; i < _maxIters; i++) {
                if (!RunOnce(ctx, inspectors)) break;

                _contIfChanged?.Run(ctx, inspectors);
            }
        }

        private bool RunOnce(MethodTransformContext ctx, ReadOnlySpan<IPassInspector> inspectors)
        {
            bool changed = false;

            foreach (var pass in _passes) {
                changed |= PassRunner.Run(pass, ctx, inspectors).Changes != MethodInvalidations.None;
            }

            return changed;
        }

        public PipelineSegment Apply<TPass>() where TPass : IMethodPass
        {
            return Apply(TPass.Create<TPass>(_manager.Compilation));
        }
        public PipelineSegment Apply(IMethodPass pass)
        {
            _passes.Add(pass);
            return this;
        }

        public PipelineSegment IfChanged(Action<PipelineSegment> buildChildPipe)
        {
            Ensure.That(_contIfChanged == null);

            _contIfChanged = new PipelineSegment(_manager, false);
            buildChildPipe(_contIfChanged);

            return this;
        }
        public PipelineSegment IfChanged(PipelineSegment segmentToApply)
        {
            Ensure.That(_contIfChanged == null);
            _contIfChanged = segmentToApply;
            return this;
        }
        public PipelineSegment RepeatUntilFixedPoint(int maxIters)
        {
            _maxIters = maxIters;

            return this;
        }
    }
}
