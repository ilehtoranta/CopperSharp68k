namespace Cilica.Passes.Linq;

using Cilica.IR.Utils;

/// <summary> Owns acquisition, iteration, and disposal for one enumeration scope. </summary>
internal sealed class EnumeratorLoop
{
    readonly LoopBuilder _loop;
    readonly EnumeratorLoop? _parent;
    readonly bool _isRoot;
    readonly Value _source;
    readonly MethodDesc _getEnumerator;
    readonly TypeDesc? _unboxType;
    readonly LocalSlot _enumerator, _initialized;
    readonly HashSet<BasicBlock> _exitTargets = new();

    public EnumeratorLoop(LoopBuilder loop, Value source, TypeDesc enumerableType, EnumeratorLoop? parent = null, bool isRoot = false)
    {
        _loop = loop;
        _parent = parent;
        _isRoot = isRoot;
        _source = source;
        _getEnumerator = enumerableType.FindMethod("GetEnumerator", searchBaseAndItfs: true);

        if (source is CilIntrinsic.Box box && enumerableType.IsGeneric &&
            box.SourceType.IsValueType && !box.SourceType.IsCorelibType(typeof(Nullable<>))) {
            _unboxType = box.SourceType;
            var custom = box.SourceType.IsCorelibType(typeof(ArraySegment<>))
                ? box.SourceType.Methods.FirstOrDefault(m => m is { Name: "GetEnumerator", IsPublic: true, IsInstance: true, ParamSig.Count: 1 }) : null;

            if (custom != null && IsEnumerator(custom.ReturnType, enumerableType.GenericParams[0])) {
                _getEnumerator = custom;
            }
        }

        var method = loop.Header.Method;
        _enumerator = method.CreateVar(_getEnumerator.ReturnType, "lq_enumerator", hardExposed: true);
        _initialized = method.CreateVar(PrimType.Bool, "lq_initialized", hardExposed: true);
        loop.PreHeader.CreateInitObj(_enumerator);
        loop.PreHeader.CreateStore(_initialized, ConstInt.CreateI(0));
    }

    public Value MoveNext(IRBuilder builder)
    {
        // Acquisition is lazy so a loop guard can stop before touching the source.
        builder.ForkIf(builder.CreateEq(builder.CreateLoad(_initialized), ConstInt.CreateI(0)), (init, _) => {
            var receiver = _unboxType == null ? _source : init.CreateUnboxRef(_unboxType, _source);
            var acquisition = _unboxType != null && _getEnumerator.DeclaringType.IsInterface
                ? init.Emit(new CallInst(_getEnumerator, [receiver], isVirtual: true, constraint: _unboxType))
                : init.CreateCallVirt(_getEnumerator, receiver);
            init.CreateStore(_enumerator, acquisition);
            init.CreateStore(_initialized, ConstInt.CreateI(1));
        });

        return builder.CreateCallVirt("MoveNext", Receiver(builder));
    }

    public Value Current(IRBuilder builder) => builder.CreateCallVirt("get_Current", Receiver(builder));

    private Value Receiver(IRBuilder builder)
        => _enumerator.Type.IsValueType ? _enumerator : builder.CreateLoad(_enumerator);

    public BasicBlock GetExitTarget(BasicBlock target, bool exitRoot = false)
    {
        if (_isRoot && !exitRoot) return target;
        var destination = _parent?.GetExitTarget(target, exitRoot) ?? target;
        var bridge = _loop.Header.Method.CreateBlock(insertAfter: _loop.Exit.Block).SetName("LQ_Leave");
        bridge.SetBranch(new LeaveInst(destination));
        _exitTargets.Add(destination);
        return bridge;
    }

    public void Finish(BasicBlock continuation)
    {
        _loop.Exit.Block.SetBranch(new LeaveInst(continuation));
        _exitTargets.Add(continuation);
        var handler = _loop.Header.Method.CreateBlock(insertAfter: _loop.Exit.Block).SetName("LQ_Finally");
        var builder = new IRBuilder(handler);
        var disposable = builder.Resolver.Import(typeof(IDisposable));
        var dispose = disposable.FindMethod("Dispose");
        builder.ForkIf(builder.CreateLoad(_initialized), (body, _) => {
            if (_enumerator.Type.IsValueType) {
                if (_enumerator.Type.Inherits(disposable)) {
                    body.Emit(new CallInst(dispose, [_enumerator], isVirtual: true, constraint: _enumerator.Type));
                }
            } else {
                var instance = body.CreateAsInstance(disposable, Receiver(body));
                body.ForkIf(body.CreateNe(instance, ConstNull.Create()), (disposeBody, _) => disposeBody.CreateCallVirt(dispose, instance));
            }
        });
        builder.Emit(new ResumeInst(_exitTargets));
        _loop.Header.Block.InsertFirst(new GuardInst(GuardKind.Finally, handler));
    }

    private static bool IsEnumerator(TypeDesc type, TypeDesc elementType)
        => type.Methods.Count(m => m is { Name: "MoveNext", IsPublic: true, IsInstance: true, ParamSig.Count: 1 } && m.ReturnType == PrimType.Bool) == 1 &&
           type.Methods.Count(m => m is { Name: "get_Current", IsPublic: true, IsInstance: true, ParamSig.Count: 1 } && m.ReturnType == elementType) == 1;
}
