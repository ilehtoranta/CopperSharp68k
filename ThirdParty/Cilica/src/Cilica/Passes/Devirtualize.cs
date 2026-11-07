namespace Cilica.Passes;

using System.Reflection;
using Cilica.Analysis;
using Cilica.IR.Utils;

/// <summary> Turns proven virtual targets into direct calls, independently of inlining. </summary>
public class Devirtualize : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        long revision = ctx.Method.Revision;
        foreach (var instruction in ctx.Method.Instructions()) {
            if (instruction is CallInst call) TryDevirtualize(ctx, call);
            else if (instruction is FuncAddrInst address) TryDevirtualizeAddress(ctx, address);
        }
        return ctx.Method.Revision == revision ? MethodInvalidations.None : MethodInvalidations.DataFlow;
    }

    internal static MethodDefOrSpec? ResolveTarget(MethodTransformContext ctx, CallInst call)
    {
        if (call.Method is not MethodDefOrSpec method || !CanInspect(ctx, method.DeclaringType)) return null;
        if (!method.Attribs.HasFlag(MethodAttributes.Virtual)) return method;
        if (!call.IsVirtual && call.Constraint == null) return VirtualDispatch.ResolveDirect(method) as MethodDefOrSpec;
        var type = call.Constraint;
        if (type != null && !call.IsStatic && !type.IsValueType && !TypeUtils.IsExactType(type)) type = null;
        type ??= call.Constraint == null && !call.IsStatic ? ExactType(call.Args[0]) : null;
        if (type is PrimType primitive) type = primitive.GetDefinition(ctx.Compilation.Resolver);
        if (type is TypeDefOrSpec nominal && CanInspect(ctx, nominal)) {
            if (method.DeclaringType.IsInterface && TypeUtils.CanHaveDynamicInterfaces(nominal)) return null;
            return VirtualDispatch.Resolve(method, nominal) as MethodDefOrSpec;
        }
        return method.Attribs.HasFlag(MethodAttributes.Final) && !method.DeclaringType.IsInterface
            ? VirtualDispatch.ResolveDirect(method) as MethodDefOrSpec : null;
    }

    internal static bool TryDevirtualize(MethodTransformContext ctx, CallInst call)
    {
        if (call.IsReadOnly || call.IsTailCall) return false;
        var target = ResolveTarget(ctx, call);
        if (target == null || target.Attribs.HasFlag(MethodAttributes.Abstract) ||
            !VirtualDispatch.SameMethod(VirtualDispatch.ResolveDirect(target), target) || !CanCall(ctx, target)) return false;

        Value? receiver = call.IsStatic ? null : call.Args[0];
        bool loadReceiver = call.Constraint is TypeDefOrSpec { IsValueType: false } or PrimType { StackType: StackType.Object };
        bool unboxReceiver = call.Constraint == null && receiver != null && target.DeclaringType.IsValueType;
        if (call.Constraint != null && !call.IsStatic && !loadReceiver &&
            (!call.Constraint.IsValueType || target.DeclaringType != call.Constraint)) return false;
        if (unboxReceiver && ExactType(receiver!) != target.DeclaringType &&
            !(ExactType(receiver!) is PrimType primitive && primitive.GetDefinition(ctx.Compilation.Resolver) == target.DeclaringType)) return false;
        if (!call.IsVirtual && call.Constraint == null && VirtualDispatch.SameMethod(call.Method, target)) return false;

        var builder = new IRBuilder(call, InsertionDir.Before);
        if (receiver != null) {
            if (loadReceiver) receiver = builder.CreateLoad(receiver, call.Constraint);
            if ((call.IsVirtual || call.Constraint != null) && receiver.ResultType.IsManagedObject() && !call.InBounds &&
                !IsNonNull(receiver))
                builder.CreateNullCheck(receiver);
            if (unboxReceiver) receiver = builder.Emit(new CilIntrinsic.UnboxRef(target.DeclaringType, receiver));
            if (receiver.ResultType != target.ParamSig[0].Type && receiver.ResultType.IsManagedObject())
                receiver = builder.Emit(new CilIntrinsic.RefCast(target.ParamSig[0].Type, receiver));
            call.SetArg(0, receiver);
        }
        var originalTarget = call.Method;
        var originalOpcode = call.IsVirtual ? "callvirt" : "call";
        var originalConstraint = call.Constraint;
        call.Method = target;
        call.Constraint = null;
        call.IsVirtual = false;
        ctx.Logger.Trace($"Devirtualized {(originalConstraint != null ? $"constrained({originalConstraint}) " : "")}{originalOpcode} '{originalTarget}'{(VirtualDispatch.SameMethod(originalTarget, target) ? "" : $" to '{target}'")}.");
        return true;
    }

    static void TryDevirtualizeAddress(MethodTransformContext ctx, FuncAddrInst address)
    {
        if (address.Method is not MethodDefOrSpec method || !CanInspect(ctx, method.DeclaringType)) return;
        var type = address.Constraint ?? (address.IsVirtual ? ExactType(address.Object!) : null);
        // ldvirtftn on a boxed value can return an unboxing stub. An ordinary
        // ldftn of its value-type method has a different receiver convention.
        if (address.IsVirtual && type?.IsValueType == true) return;
        if (type is PrimType primitive) type = primitive.GetDefinition(ctx.Compilation.Resolver);
        if (type is not TypeDefOrSpec nominal || !CanInspect(ctx, nominal) ||
            method.DeclaringType.IsInterface && TypeUtils.CanHaveDynamicInterfaces(nominal)) return;
        if (VirtualDispatch.Resolve(method, nominal) is not MethodDefOrSpec target || !CanCall(ctx, target) ||
            !VirtualDispatch.SameMethod(VirtualDispatch.ResolveDirect(target), target)) return;
        if (address.IsVirtual && address.Object is not NewObjInst) new IRBuilder(address, InsertionDir.Before).CreateNullCheck(address.Object!);
        address.ReplaceWith(new FuncAddrInst(target), insertIfInst: true);
        ctx.Logger.Trace($"Devirtualized {(address.Constraint != null ? $"constrained({address.Constraint}) " : "")}{(address.IsVirtual ? "ldvirtftn" : "ldftn")} '{address.Method}'{(VirtualDispatch.SameMethod(address.Method, target) ? "" : $" to '{target}'")}.");
    }

    static bool CanCall(MethodTransformContext ctx, MethodDefOrSpec target)
        => CanInspect(ctx, target.DeclaringType) && (ctx.Compilation.Module != ctx.Compilation.Resolver.CoreLib ||
            new InliningAccess(ctx.Definition.DeclaringType).CanUseMember(target));

    static bool CanInspect(MethodTransformContext ctx, TypeDefOrSpec type)
    {
        if (ctx.Compilation.Settings.AllowCrossAssemblyIPO) return true;
        if (type.Module != ctx.Compilation.Module) return false;
        for (var parent = type.BaseType; parent != null; parent = parent.BaseType)
            if (parent.Module != ctx.Compilation.Module && !parent.IsCorelibType()) return false;
        return true;
    }

    internal static TypeDesc? ExactType(Value value) => ExactType(value, new HashSet<Value>());

    static TypeDesc? ExactType(Value value, HashSet<Value> active)
    {
        if (!active.Add(value)) return null;
        try {
            if (value is CilIntrinsic.RefCast cast) return ExactType(cast.Args[0], active);
            if (value is CilIntrinsic.Box box)
                return box.SourceType.IsValueType && !box.SourceType.IsCorelibType(typeof(Nullable<>)) ? box.SourceType : null;
            if (TypeUtils.HasConcreteType(value)) return value.ResultType;
            if (value is PhiInst phi) return CommonType(Enumerable.Range(0, phi.NumArgs).Select(phi.GetValue));
            if (value is SelectInst select) return CommonType([select.IfTrue, select.IfFalse]);
            return null;
        } finally { active.Remove(value); }

        TypeDesc? CommonType(IEnumerable<Value> values)
        {
            TypeDesc? result = null;
            foreach (var input in values) {
                if (input == value || input is ConstNull) continue;
                var type = ExactType(input, active);
                if (type == null || result != null && type != result) return null;
                result = type;
            }
            return result;
        }
    }

    static bool IsNonNull(Value value)
        => value is NewObjInst || value is CilIntrinsic.Box box && box.SourceType.IsValueType &&
            !box.SourceType.IsCorelibType(typeof(Nullable<>));
}
