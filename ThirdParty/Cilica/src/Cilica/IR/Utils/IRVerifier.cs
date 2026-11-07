namespace Cilica.IR.Utils;

using Cilica.Analysis;

public class IRVerifier
{
    readonly MethodBody _method;
    readonly List<Diagnostic> _diags = new();
    readonly HashSet<BasicBlock> _blocks = new();
    readonly HashSet<Instruction> _instructions = new();
    readonly HashSet<BasicBlock> _reachable = new();

    private IRVerifier(MethodBody method)
    {
        _method = method;
    }

    public static List<Diagnostic> Diagnose(MethodBody method)
    {
        var v = new IRVerifier(method);

        try {
            v.VerifyStructure();

            if (!v._diags.Any(d => d.Severity == DiagnosticSeverity.Error)) {
                v.VerifyMethod();
            }
        } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or NullReferenceException) {
            v.Error(method.EntryBlock, $"Invalid IR structure: {ex.Message}");
        }

        return v._diags;
    }

    public static void Verify(MethodBody method)
    {
        var diagnostics = Diagnose(method);

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) {
            throw new IRVerificationException(diagnostics);
        }
    }

    private void VerifyStructure()
    {
        if (_method.EntryBlock == null) {
            Error(null, "Method must have an entry block");
            return;
        }

        BasicBlock? previousBlock = null;

        foreach (var block in _method) {
            if (!_blocks.Add(block)) {
                Error(block, "Block list contains a cycle");
                return;
            }

            Check(block.Method == _method && block.Prev == previousBlock, block, "Invalid block ownership or links");
            previousBlock = block;

            if (block.First == null || block.Last == null) {
                Error(block, "Block must contain a terminator");
                continue;
            }

            Instruction? previous = null;

            foreach (var inst in block) {
                if (!_instructions.Add(inst)) {
                    Error(block, "Instruction list contains a cycle or shared instruction");
                    return;
                }

                Check(inst.Block == block && inst.Prev == previous, inst, "Invalid instruction ownership or links");
                Check(!inst.IsBranch || inst.Next == null, inst, "Terminator must be last in its block");
                previous = inst;
            }

            Check(previous == block.Last && block.Last.IsBranch, block, "Block must end with a valid terminator");
        }

        Check(_blocks.Count == _method.NumBlocks, _method.EntryBlock, "Block count does not match the block list");

        foreach (var inst in _instructions) {
            foreach (var operand in inst.Operands) {
                bool owned = operand switch {
                    null => false,
                    BasicBlock block => _blocks.Contains(block),
                    Instruction definition => _instructions.Contains(definition),
                    LocalSlot slot => slot.Method == _method,
                    Argument arg => _method.Args.Contains(arg),
                    _ => true
                };
                Check(owned, inst, "Operand must belong to this method and remain attached");
            }
        }
    }

    private void VerifyMethod()
    {
        Check(_method.EntryBlock.NumPreds == 0, _method.EntryBlock, "Entry block should not have predecessors");
        _method.TraverseDepthFirst(preVisit: block => _reachable.Add(block));

        var useChecker = new UseChecker();
        var regionAnalysis = new ProtectedRegionAnalysis(_method);

        foreach (var block in _method) {
            int blockInstIdx = 0;

            foreach (var inst in block) {
                CheckInst(inst);
                useChecker.AddInst(this, inst, blockInstIdx++);
            }

            if (_reachable.Contains(block)) {
                CheckTerminator(block, regionAnalysis);
            }
        }

        useChecker.Validate(this, regionAnalysis);
    }

    private void CheckInst(Instruction inst)
    {
        switch (inst) {
            case ReturnInst ret: {
                    Check(_method.ReturnType == PrimType.Void ? ret.Value == null
                        : ret.Value != null && IsCompatible(ret.Value, _method.ReturnType), ret,
                        "Return value must match the method signature");
                    break;
                }
            case NewObjInst allocation: {
                    CheckArguments(allocation, allocation.Constructor, allocation.Args, 1);
                    break;
                }
            case CilIntrinsic.Initialization initialization: {
                    var helper = initialization.Helper;
                    Check(helper.IsStatic && !helper.IsGeneric && helper.ParamSig.Count == 0 && helper.ReturnType == PrimType.Void,
                        initialization, "Initialization helper must be a non-generic static void method without parameters");
                    break;
                }
            case CilIntrinsic.CallIndirect call: {
                    var signature = call.FunctionType.Signature;
                    bool thisCall = signature.CallConv == CallConvention.ThisCall || signature.ReturnType.Modifiers.Any(modifier =>
                        modifier.Type is { Namespace: "System.Runtime.CompilerServices", Name: "CallConvThiscall" } && modifier.Type.IsCorelibType());
                    Check(!thisCall || signature.NumCallArgs != 0, call, "Thiscall requires a receiver argument");
                    Check(call.Function.ResultType.StackType == StackType.NInt, call, "Indirect call target must be a function pointer");
                    Check(call.CallArgs.Length == signature.NumCallArgs, call, "Indirect call argument count must match its signature");
                    int offset = signature.IsInstance == true && !signature.HasExplicitThis ? 1 : 0;
                    if (offset != 0 && call.CallArgs.Length != 0) {
                        Check(call.CallArgs[0].ResultType.IsPointerOrObject(), call, "Indirect call receiver must be an object or managed reference");
                    }
                    for (int i = 0; i < signature.ParamTypes.Count && i + offset < call.CallArgs.Length; i++) {
                        Check(IsCompatible(call.CallArgs[i + offset], signature.ParamTypes[i].Type), call,
                            $"Indirect call argument {i} must match its signature");
                    }
                    Check(call.ResultType == signature.ReturnType.Type, call, "Indirect call result must match its signature");
                    if (call.IsTailCall) Check(call.Next is ReturnInst, call, "Next instruction after tail call must be a return.");
                    break;
                }
            case CilIntrinsic.RefCast cast: {
                    var source = cast.Args[0];
                    Check(source.ResultType.IsManagedObject() && cast.ResultType.IsManagedObject() ||
                        cast.ResultType.StackType == StackType.ByRef &&
                        (source.ResultType.StackType == StackType.NInt || source is ConstInt { IsInt: true, Value: 0 }), cast,
                        "Unchecked reference conversion requires object references or a native address converted to a managed byref");
                    break;
                }
            case CilIntrinsic.RefAnyType typedReference: {
                    Check(typedReference.Args[0].ResultType == PrimType.TypedRef, typedReference, "refanytype requires a typed reference");
                    break;
                }
            case CilIntrinsic.MkRefAny typedReference: {
                    Check(typedReference.Args[0].ResultType.IsPointerLike(), typedReference,
                        "mkrefany requires an address");
                    break;
                }
            case CilIntrinsic.RefAnyVal typedReference: {
                    Check(typedReference.Args[0].ResultType == PrimType.TypedRef, typedReference,
                        "refanyval requires a typed reference");
                    break;
                }
            case StoreInst store: {
                    Check(store.Address.ResultType.IsPointerLike(), store, "Store address must be a pointer or managed reference");
                    Check(IsCompatible(store.Value, store.ElemType), store, "Stored value must match the element stack type");
                    break;
                }
            case LoadInst load: {
                    Check(load.Address.ResultType.IsPointerLike(), load, "Load address must be a pointer or managed reference");
                    break;
                }
            case SelectInst select: {
                    Check(IsCondition(select.Cond) && IsCompatible(select.IfTrue, select.ResultType) && IsCompatible(select.IfFalse, select.ResultType),
                        select, "Select operands must match the condition and result types");
                    break;
                }
            case BranchInst { Cond: { } condition } branch: {
                    Check(IsCondition(condition), branch, "Branch condition must be an integer, pointer, or object reference");
                    break;
                }
            case SwitchInst sw: {
                    Check(sw.TargetIndex.ResultType.StackType == StackType.Int, sw, "Switch index must be an int32 stack value");
                    break;
                }
            case GuardInst guard: {
                Check(
                    guard.HandlerBlock.NumPreds == 1 && (!guard.HasFilter || guard.FilterBlock.NumPreds == 1), guard,
                    "Guard handler/filter block must have a single predecessor");
                Check(
                    guard.HandlerBlock.First is not GuardInst && (!guard.HasFilter || guard.FilterBlock.First is not GuardInst), guard,
                    "Handler/filter entry block should not have guards");
                Check(
                    guard.Prev is null or GuardInst, guard,
                    "Guards must come before phi and normal instructions");
                break;
            }
            case PhiInst phi: {
                Check(
                    phi.Prev is null or GuardInst or PhiInst, phi,
                    "Phis must come before normal instructions");

                var preds = new HashSet<BasicBlock>();
                bool argTypesMatch = true;

                foreach (var (pred, val) in phi) {
                    Check(preds.Add(pred), phi, "Phi should not have duplicated block arguments");

                    var type = phi.ResultType;
                    argTypesMatch &= val switch {
                        ConstNull => type.StackType == StackType.Object,
                        ConstInt ci when type.Kind.IsSmallInt() => ci.FitsInType(type),
                        _ => val.ResultType.IsAssignableTo(type) || type.IsManagedObject() && IsCompatible(val, type)
                    };
                }
                preds.SymmetricExceptWith(phi.Block.Preds.AsEnumerable());
                Check(preds.Count == 0, phi, "Phi must have one argument for each block predecessor");
                    Check(argTypesMatch, phi, "Phi arguments should be assignable to its result type");
                    break;
            }
            case CallInst call: {
                    Check(call.IsVirtual || call.Method.IsStatic && call.Constraint != null ||
                        !call.Method.Attribs.HasFlag(System.Reflection.MethodAttributes.Abstract), call,
                        "Abstract methods require virtual dispatch");
                    Check(call.Method is not MethodDefOrSpec managedTarget ||
                        !managedTarget.Definition.HasCustomAttrib("System.Runtime.InteropServices", "UnmanagedCallersOnlyAttribute"),
                        call, "UnmanagedCallersOnly methods require an unmanaged call site");
                    if (call.Method is MethodDefOrSpec { Definition.ImportInfo: { } import }) {
                        bool thisCall = (import.Attribs & System.Reflection.MethodImportAttributes.CallingConventionMask) ==
                            System.Reflection.MethodImportAttributes.CallingConventionThisCall;
                        Check(!thisCall || call.NumArgs != 0, call, "Thiscall requires a receiver argument");
                    }
                    CheckArguments(call, call.Method, call.Args, 0);
                    Check(call.Method.ReturnType.IsStackAssignableTo(call.ResultType), call, "Call result must match the method signature");

                    if (call.IsTailCall) {
                    Check(call.Next is ReturnInst, call, "Next instruction after tail call must be a return.");
                }
                break;
            }
            case { IsBranch: true, Next: not null }: {
                Error(inst, "Branch must be the last instruction in the block");
                break;
            }
        }
    }

    private void CheckArguments(Instruction inst, MethodDesc target, ReadOnlySpan<Value> args, int offset)
    {
        Check(target is not MethodReference { Parent: TypeDef { IsGeneric: true } owner } || owner == _method.Definition.DeclaringType,
            inst, "Generic member reference requires an instantiated declaring type");
        Check(args.Length + offset == target.ParamSig.Count, inst, "Argument count must match the method signature");

        for (int i = 0; i < args.Length && i + offset < target.ParamSig.Count; i++) {
            if (i == 0 && inst is CallInst { Constraint: not null, IsStatic: false }) {
                Check(args[i].ResultType.IsPointerLike(), inst, "Constrained receiver must be a managed reference");
            } else {
                Check(IsCompatible(args[i], target.ParamSig[i + offset].Type), inst, $"Argument {i} must match the parameter type");
            }
        }
    }

    private static bool IsCondition(Value value)
        => value.ResultType.StackType is StackType.Int or StackType.Long or StackType.NInt or StackType.ByRef or StackType.Object;

    private bool IsCompatible(Value value, TypeDesc target, HashSet<Value>? visiting = null)
    {
        if (value is ConstNull) {
            return target.StackType == StackType.Object;
        }

        var source = value.ResultType;

        if (source.StackType != StackType.Object || target.StackType != StackType.Object) {
            return source.IsStackAssignableTo(target);
        }

        if (value is CilIntrinsic.Box box) {
            return IsBoxAssignable(box.SourceType, target, new());
        }

        if (source is GenericParamType || target is GenericParamType || IsReferenceAssignable(source, target, new())) {
            return true;
        }

        // A stack merge can lose a common interface when its inputs have different
        // class ancestors. Check all incoming values before rejecting that use.
        if (value is PhiInst or SelectInst) {
            visiting ??= new();
            if (!visiting.Add(value)) return true;
            bool compatible = value is PhiInst phi
                ? Enumerable.Range(0, phi.NumArgs).All(i => IsCompatible(phi.GetValue(i), target, visiting))
                : value is SelectInst select && IsCompatible(select.IfTrue, target, visiting) && IsCompatible(select.IfFalse, target, visiting);
            visiting.Remove(value);
            return compatible;
        }
        return false;
    }

    private bool IsBoxAssignable(TypeDesc source, TypeDesc target, HashSet<TypeDesc> visiting)
    {
        source = source.GetSpec(new GenericContext(_method.Definition));
        if (source.IsCorelibType(typeof(Nullable<>))) return IsBoxAssignable(source.GenericParams[0], target, visiting);
        if (target == PrimType.Object || IsReferenceAssignable(source, target, new())) return true;
        if (source is PrimType primitive) {
            return IsReferenceAssignable(primitive.GetDefinition(_method.Definition.Module.Resolver), target, new());
        }
        if (!visiting.Add(source)) return false;

        // IL tokens carry unbound parameters. Recover their constraints from the
        // containing method/type, including constraints through another parameter.
        return source is GenericParamType parameter &&
            parameter.Constraints.Any(constraint => IsBoxAssignable(constraint.Sig.Type, target, visiting));
    }

    private bool IsReferenceAssignable(TypeDesc source, TypeDesc target, HashSet<(TypeDesc, TypeDesc)> visiting)
    {
        if (source.IsAssignableTo(target)) return true;
        if (!visiting.Add((source, target))) return false;

        if (source is GenericParamType parameter) {
            parameter = ResolveParameter(parameter);
            return target == PrimType.Object || parameter.Constraints.Any(c => IsReferenceAssignable(c.Sig.Type, target, visiting));
        }
        if (source is ArrayType sourceArray && target is ArrayType targetArray) {
            return IsKnownReference(sourceArray.ElemType, new()) && IsKnownReference(targetArray.ElemType, new()) &&
                IsReferenceAssignable(sourceArray.ElemType, targetArray.ElemType, visiting);
        }
        if (source is TypeDefOrSpec from && target is TypeDefOrSpec to && from.Definition == to.Definition &&
            (from.IsInterface || from.BaseType is { Name: "MulticastDelegate" } parent && parent.IsCorelibType())) {
            for (int i = 0; i < from.GenericParams.Count; i++) {
                var a = from.GenericParams[i];
                var b = to.GenericParams[i];
                if (a == b) continue;
                if (!IsKnownReference(a, new()) || !IsKnownReference(b, new())) return false;

                var variance = from.Definition.GenericParams[i];
                if (!(variance.IsCovariant ? IsReferenceAssignable(a, b, new(visiting))
                    : variance.IsContravariant && IsReferenceAssignable(b, a, new(visiting)))) return false;
            }
            return true;
        }
        return source.Interfaces.Any(itf => IsReferenceAssignable(itf, target, visiting)) ||
            source.BaseType is { } baseType && IsReferenceAssignable(baseType, target, visiting);
    }

    private GenericParamType ResolveParameter(GenericParamType parameter)
        => new GenericContext(_method.Definition).GetArgument(parameter.Index, parameter.IsMethodParam) as GenericParamType ?? parameter;

    private bool IsKnownReference(TypeDesc type, HashSet<TypeDesc> visiting)
    {
        if (type is not GenericParamType parameter) return type.IsManagedObject();
        if (!visiting.Add(type)) return false;
        parameter = ResolveParameter(parameter);
        return parameter.Attribs.HasFlag(System.Reflection.GenericParameterAttributes.ReferenceTypeConstraint) ||
            parameter.Constraints.Any(c => c.Sig.Type is GenericParamType
                ? IsKnownReference(c.Sig.Type, visiting)
                : c.Sig.Type.IsClass && c.Sig.Type.IsManagedObject() && c.Sig.Type != PrimType.Object && !c.Sig.Type.IsCorelibType(typeof(object)) &&
                    !c.Sig.Type.IsCorelibType(typeof(ValueType)) && !c.Sig.Type.IsCorelibType(typeof(Enum)));
    }

    private void CheckTerminator(BasicBlock block, ProtectedRegionAnalysis regionAnalysis)
    {
        switch (block.Last) {
            case BranchInst or SwitchInst: {
                var currRegion = regionAnalysis.GetBlockRegion(block);

                foreach (var oper in block.Last.Operands) {
                    if (oper is not BasicBlock succ) continue;

                    var succRegion = regionAnalysis.GetBlockRegion(succ);

                    if (currRegion != succRegion && !(succ == succRegion.StartBlock && succRegion.Parent == currRegion)) {
                        Error(block, $"Branch target must be within the same region");
                        break;
                    }
                }
                break;
            }
            case LeaveInst or ResumeInst: {
                var region = regionAnalysis.GetBlockRegion(block);

                if (region == regionAnalysis.Root) {
                    Error(block, "Cannot leave or resume from root region");
                } else if (block.Last is ResumeInst resume) {
                    var guard = region.GetHandlerGuard();

                    if (guard == null) {
                        Error(block, "Missing guard for filter or finally region");
                        break;
                    }

                    bool isValid = 
                        resume.IsFromFilter
                            ? guard.FilterBlock == region.StartBlock
                            : guard.Kind is GuardKind.Finally or GuardKind.Fault;
                    Check(isValid, block, "ResumeInst should not be used outside filter, finally, or fault handlers");

                    var succBlocks = regionAnalysis.GetResumeTargets(resume);
                    succBlocks.SymmetricExceptWith(resume.GetExitTargets());

                    Check(succBlocks.Count == 0, resume, "ResumeInst targets must be consistent with exits in sibling protected region");
                }
                break;
            }
            case ReturnInst: {
                if (regionAnalysis.GetBlockRegion(block) != regionAnalysis.Root) {
                    Error(block, $"ReturnInst should not be used inside a protected region");
                }
                break;
            }
            default: {
                Check(block.Last.IsBranch, block, "Block must end with a valid terminator");
                break;
            }
        }
    }

    private void Error(Value? location, string msg, DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        _diags.Add(new Diagnostic() {
            Severity = severity,
            Location = location,
            Message = msg
        });
    }

    private void Check(bool cond, Value location, string msg, DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        if (!cond) {
            Error(location, msg, severity);
        }
    }

    class UseChecker
    {
        Dictionary<TrackedValue, HashSet<UseRef>> _expectedUsers = new();
        Dictionary<Instruction, int> _blockIndices = new();
        Dictionary<BasicBlock, HashSet<BasicBlock>> _exceptionalPaths = new();

        public void AddInst(IRVerifier verifier, Instruction inst, int blockIndex)
        {
            _expectedUsers.TryAdd(inst, new());

            for (int index = 0; index < inst.Operands.Length; index++) {
                var oper = inst.Operands[index];

                if (oper is Instruction instOper && instOper.Block?.Method != verifier._method) {
                    verifier.Error(inst, "Using an instruction that was removed or is declared outside the parent method");
                } else if (oper is TrackedValue trackedOper) {
                    var expUsers = _expectedUsers.GetOrAddRef(trackedOper) ??= new();
                    expUsers.Add(inst.GetOperandRef(index));
                }
            }

            _blockIndices[inst] = blockIndex;
        }

        public void Validate(IRVerifier verifier, ProtectedRegionAnalysis regionAnalysis)
        {
            var domTree = new DominatorTree(verifier._method);

            foreach (var block in verifier._blocks) {
                _expectedUsers.TryAdd(block, new());
            }

            foreach (var arg in verifier._method.Args) {
                _expectedUsers.TryAdd(arg, new());
            }

            foreach (var slot in verifier._method.LocalVars()) {
                _expectedUsers.TryAdd(slot, new());
            }

            foreach (var (val, expUsers) in _expectedUsers) {
                // Check use list correctness
                var actUsers = new HashSet<UseRef>();
                foreach (var use in val.Uses()) {
                    if (!actUsers.Add(use) || actUsers.Count > expUsers.Count) {
                        break;
                    }
                }

                verifier.Check(val.NumUses == expUsers.Count && actUsers.Count == expUsers.Count, val, "Invalid value use count");
                verifier.Check(actUsers.SetEquals(expUsers), val, "Invalid value use chain");

                // Check users
                if (val is Instruction def && verifier._reachable.Contains(def.Block)) {
                    foreach (var use in expUsers) {
                        var user = use.Parent;

                        if (!verifier._reachable.Contains(user.Block)) {
                            continue;
                        }

                        if (!IsDominatedByDef(domTree, def, user)) {
                            verifier.Error(user, $"Using non-dominating instruction '{def}'");
                        }

                        // Phi inputs are established on incoming edges, before
                        // any protected instruction can transfer to a handler.
                        if (def is not (GuardInst or PhiInst) && def.Block != user.Block &&
                            def.Block.Guards().Any(g => g.Kind != GuardKind.Finally) &&
                            (user is PhiInst phi
                                ? Enumerable.Range(0, phi.NumArgs).Any(i => phi.GetValue(i) == def && ExceptionalPaths(def.Block).Contains(phi.GetBlock(i)))
                                : ExceptionalPaths(def.Block).Contains(user.Block))
                        ) {
                            verifier.Error(user, $"Using instruction defined inside a child region '{def}'");
                        }
                    }
                }
            }
        }

        private HashSet<BasicBlock> ExceptionalPaths(BasicBlock definitionBlock)
        {
            if (_exceptionalPaths.TryGetValue(definitionBlock, out var reachable)) return reachable;
            reachable = new();
            _exceptionalPaths.Add(definitionBlock, reachable);
            var pending = new Stack<BasicBlock>();
            foreach (var guard in definitionBlock.Guards()) {
                if (guard.Kind == GuardKind.Finally) continue;
                pending.Push(guard.HandlerBlock);
                if (guard.HasFilter) pending.Push(guard.FilterBlock);
            }
            while (pending.TryPop(out var block)) {
                // Re-entering the definition's block executes its normal code again.
                if (block == definitionBlock || !reachable.Add(block)) continue;
                foreach (var successor in block.Succs) pending.Push(successor);
            }
            return reachable;
        }

        private bool IsDominatedByDef(DominatorTree domTree, Instruction def, Instruction user)
        {
            if (user is PhiInst phi) {
                foreach (var (pred, value) in phi) {
                    if (value == def && !domTree.Dominates(def.Block, pred)) {
                        return false;
                    }
                }
                return true;
            }
            return user.Block == def.Block
                ? _blockIndices[def] < _blockIndices[user]
                : domTree.Dominates(def.Block, user.Block);
        }
    }
}

public struct Diagnostic
{
    public DiagnosticSeverity Severity { get; init; }

    /// <summary> The BasicBlock or Instruction originating this diagnostic. </summary>
    public Value? Location { get; init; }

    public string? Message { get; init; }

    public override string ToString()
    {
        var location = Location is Instruction inst ? inst.InstName : Location?.GetType().Name ?? "method";

        return $"[{Severity}] {Message} (at {location})";
    }
}

public sealed class IRVerificationException(IReadOnlyList<Diagnostic> diagnostics)
    : InvalidOperationException("IR verification failed: " + string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)))
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
}
public enum DiagnosticSeverity
{
    Info,
    Warn,
    Error,
}
