namespace Cilica.Analysis;

using Cilica.Frontend;
using Cilica.Passes;
using Cilica.IR.Utils;

using MethodAttributes = System.Reflection.MethodAttributes;
using ParameterAttributes = System.Reflection.ParameterAttributes;
using MethodImplAttributes = System.Reflection.MethodImplAttributes;

public class InliningAdvisor : IGlobalAnalysis
{
    readonly Dictionary<MethodDef, Entry> _cache = new();
    readonly Compilation _comp;
    readonly HashSet<MethodDef> _importing = new();

    sealed record Entry(InlineRejectReason Reason, FunctionMetrics? Metrics, MethodBody Body, long Revision);

    private InliningAdvisor(Compilation comp) => _comp = comp;

    void IGlobalAnalysis.Invalidate(MethodDef method) => _cache.Remove(method);

    static IGlobalAnalysis IGlobalAnalysis.Create(Compilation comp)
        => new InliningAdvisor(comp);

    public int EvaluateInliningCost(MethodBody targetBody, ReadOnlySpan<Value> args)
    {
        var (rejection, metrics) = GetMetrics(targetBody);

        if (rejection != InlineRejectReason.Accepted) {
            return int.MaxValue;
        }

        int cost = metrics!.BaseCost;

        for (int i = 0; i < targetBody.Args.Length; i++) {
            var argMetrics = metrics.Args[i];
            cost -= argMetrics.BenefitIfInlined;

            if (args[i] is Const) {
                cost -= argMetrics.BenefitIfKnownConst;
            } else if (TypeUtils.HasConcreteType(args[i])) {
                cost -= argMetrics.BenefitIfKnownType;
            }
        }

        // AggressiveInlining discounts expansion costs without reducing folding benefits.
        return cost > 0 && targetBody.Definition.ImplAttribs.HasFlag(MethodImplAttributes.AggressiveInlining) ? cost / 5 : cost;
    }

    /// <summary> Gets or attempts to import a method body for IPO purposes. </summary>
    public MethodBody? GetMethodBodyForIPO(MethodDef method)
    {
        if (_comp.HasFailed(method) || _importing.Contains(method)) {
            return null;
        }

        if (method.Module != _comp.Module && !_comp.Settings.AllowCrossAssemblyIPO) {
            return null;
        }

        if (method.Body == null && method.ILBody != null) {
            _importing.Add(method);

            try {
                _comp.Logger.Debug($"Importing method for IPO: {method}");
                var body = ILImporter.ParseCode(method);
                CompilationPipeline.Normalize(new MethodTransformContext(_comp, body), inlineConstructors: method.IsConstructor);
                method.Body = body;
                _comp.InvalidateAnalyses(method);
            } catch (Exception ex) {
                method.Body = null;
                _comp.FailMethod(method);
                _comp.Logger.Error($"Failed to import method for IPO: {method}", ex);
            } finally {
                _importing.Remove(method);
            }
        }

        return method.Body;
    }

    public (InlineRejectReason Rejection, FunctionMetrics? Metrics) GetMetrics(MethodBody method)
    {
        if (!_cache.TryGetValue(method.Definition, out var entry) || entry.Body != method || entry.Revision != method.Revision) {
            var (reason, metrics) = ComputeMetrics(method);
            entry = new Entry(reason, metrics, method, method.Revision);
            _cache[method.Definition] = entry;
        }

        return (entry.Reason, entry.Metrics);
    }

    public void InvalidateMetrics(MethodBody method)
    {
        _cache.Remove(method.Definition);
    }

    public InlineRejectReason EarlyCheck(MethodDef method)
    {
        if (method.CallConv == CallConvention.VarArgs || _comp.Runtime.IsOptimizationIntrinsic(method) ||
            method.Attribs.HasFlag(MethodAttributes.RequireSecObject)) return InlineRejectReason.Ineligible;
        int maxInstrs = method.ImplAttribs.HasFlag(MethodImplAttributes.AggressiveInlining)
            ? 4096
            : 256;

        if (method.ILBody != null && method.ILBody.Instructions.Count > maxInstrs) {
            return InlineRejectReason.Overbudget;
        }

        if ((method.ImplAttribs & (MethodImplAttributes.NoInlining | MethodImplAttributes.NoOptimization | MethodImplAttributes.Synchronized)) != 0) {
            return InlineRejectReason.Ineligible;
        }

        if (method.Attribs.HasFlag(MethodAttributes.Abstract) || 
            method.Attribs.HasFlag(MethodAttributes.PinvokeImpl) ||
            method.HasCustomAttrib("System.Runtime.InteropServices", "UnmanagedCallersOnlyAttribute") ||
            method.HasCustomAttrib("System.Runtime.CompilerServices", "IntrinsicAttribute")
        ) {
            return InlineRejectReason.UnknownTarget;
        }

        // Don't bother inlining async methods because we don't gain anything
        // from doing so yet, and it might mess up stack traces.
        if (method.IsRuntimeAsync || method.HasCustomAttrib("System.Runtime.CompilerServices", "AsyncStateMachineAttribute")) {
            return InlineRejectReason.Ineligible;
        }

        return InlineRejectReason.Accepted;
    }

    private (InlineRejectReason Reason, FunctionMetrics? Metrics) ComputeMetrics(MethodBody method)
    {
        // Early bail on methods with too many blocks
        if (method.NumBlocks >= 200) {
            return (InlineRejectReason.Overbudget, null);
        }

        var metrics = new FunctionMetrics {
            Args = new ArgumentMetrics[method.Args.Length]
        };
        int i = 0;
        int numReturns = 0;

        foreach (var arg in method.Args) {
            ref var argMetrics = ref metrics.Args[i++];

            foreach (var user in arg.Users()) {
                UpdateArgumentUseInfo(ref argMetrics, user);
            }

            // Larger structs are more expansive to pass by value
            if (arg.ResultType is TypeDefOrSpec { IsValueType: true } type) {
                argMetrics.BenefitIfInlined += Math.Min(type.Definition.Fields.Count * 5, 50);
            }
            // Inlining functions with `out var` may enable promotion of exposed local vars
            else if (arg.Param.Attribs.HasFlag(ParameterAttributes.Out)) {
                argMetrics.BenefitIfInlined += 25;
            }
            // First few arguments are passed in registers, rest on stack
            else {
                argMetrics.BenefitIfInlined += (i > 4 ? 5 : 1);
            }
        }

        foreach (var block in method) {
            // Penalize exception regions because they're complicated
            foreach (var guard in block.Guards()) {
                metrics.BaseCost += guard.Kind == GuardKind.Finally ? 30 : 150;
            }

            foreach (var inst in block.NonPhis()) {
                // Cannot inline method with stackallocs
                if (inst is CilIntrinsic.Alloca) {
                    return (InlineRejectReason.HasStackAllocs, metrics);
                }
                if (inst is CallInst { Method: MethodDefOrSpec target }) {
                    // Cannot inline self-recursing method
                    if (target.Definition == method.Definition) {
                        return (InlineRejectReason.SelfRecursion, metrics);
                    }
                    metrics.BaseCost += Math.Min(target.ParamSig.Count * 2, 20);
                }
                metrics.BaseCost += 3;
            }

            // Penalize methods with more than one block
            if (block != method.EntryBlock) {
                metrics.BaseCost += 15;
            }

            // Throws suggest cold blocks.
            // This is probably a bad heuristic for methods that are too eager to validate arguments...
            // Would be better to penalize non-returning methods instead. 
            if (block.Last is ThrowInst) {
                metrics.BaseCost += 20;
            } else if (block.Last is ReturnInst) {
                numReturns++;
                metrics.BaseCost -= 3;
            }

            if (metrics.BaseCost >= 3000) { // bail if too long / expansive
                return (InlineRejectReason.Overbudget, metrics);
            }
        }

        if (numReturns == 0) {
            return (InlineRejectReason.DoesNotReturn, metrics);
        }
        return (InlineRejectReason.Accepted, metrics);
    }

    private static void UpdateArgumentUseInfo(ref ArgumentMetrics metrics, Instruction inst, int chainLen = 0)
    {
        bool checkDeps = false;

        // Devirt
        if (inst is CallInst { IsVirtual: true, Method: var callTarget }) {
            metrics.BenefitIfKnownType += callTarget.Attribs.HasFlag(MethodAttributes.Virtual) ? 130 : 80;
        }
        // Cast folding
        if (inst is CilIntrinsic.CastClass or CilIntrinsic.AsInstance) {
            metrics.BenefitIfKnownType += 30;
            checkDeps = true;
        }

        // Op folding
        if (inst is BinaryInst or CompareInst or UnaryInst or ConvertInst or SelectInst) {
            metrics.BenefitIfKnownConst += 10;
            checkDeps = true;
        }

        // Branch folding
        if (inst is BranchInst or SwitchInst) {
            // TODO: consider computing total number of succ blocks
            // (might be able to guesstimate by `PostOrderNo - PreOrderNo`?)
            metrics.BenefitIfKnownConst += 60;
        }

        // Traverse dependency chain
        if (checkDeps && chainLen <= 8) {
            foreach (var user in inst.Users()) {
                UpdateArgumentUseInfo(ref metrics, user, chainLen + 1);
            }
        }
    }

    public class FunctionMetrics
    {
        public ArgumentMetrics[] Args = [];
        public int BaseCost;
    }
    public struct ArgumentMetrics
    {
        public int BenefitIfInlined;      // some arguments may be more expansive to pass than others, e.g. large structs.
        public int BenefitIfKnownConst;
        public int BenefitIfKnownType;
    }
}

public enum InlineRejectReason
{
    Accepted,
    Ineligible,     // some other arbitrary reason
    UnknownTarget,  // method target is not known statically (e.g. abstract / pinvoke)
    Overbudget,
    SelfRecursion,
    HasStackAllocs,
    DoesNotReturn
}
