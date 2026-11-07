namespace Cilica.IR.Utils;

/// <summary> Helper for folding instructions with constant operands. </summary>
public class ConstFolding
{
    public static Value? Fold(Value inst)
    {
        return inst switch {
            BinaryInst b    => FoldBinary(b.Op, b.Left, b.Right),
            UnaryInst u     => FoldUnary(u.Op, u.Value),
            ConvertInst c   => FoldConvert(c.Value, c.ResultType, c.CheckOverflow, c.SrcUnsigned),
            CompareInst c   => FoldCompare(c.Op, c.Left, c.Right),
            CallInst c      => FoldCall(c.Method, c.Args),
            IntrinsicInst c => FoldIntrinsic(c),
            _ => null
        };
    }

    public static Value? FoldBinary(BinaryOp op, Value left, Value right)
    {
        if (op.IsCommutative() && left is Const && right is not Const) {
            (left, right) = (right, left);
        }
        switch (left, right, op) {
            case (ConstInt i1, ConstInt i2, _): {
                long v1 = i1.SignedStackValue;
                long v2 = i2.SignedStackValue;
                int width = i1.IsLong ? 64 : 32;
                int shift = (int)v2 & (width - 1);
                bool invalidDivision = v2 == 0 || (v2 == -1 && v1 == (i1.IsLong ? long.MinValue : int.MinValue));

                long? result = op switch {
                    BinaryOp.Add  => v1 + v2,
                    BinaryOp.Sub  => v1 - v2,
                    BinaryOp.Mul  => v1 * v2,
                    BinaryOp.SDiv => invalidDivision ? null : v1 / v2,
                    BinaryOp.UDiv => v2 == 0 ? null : (long)(i1.UnsignedStackValue / i2.UnsignedStackValue),
                    BinaryOp.SRem => invalidDivision ? null : v1 % v2,
                    BinaryOp.URem => v2 == 0 ? null : (long)(i1.UnsignedStackValue % i2.UnsignedStackValue),
                    BinaryOp.And  => v1 & v2,
                    BinaryOp.Or   => v1 | v2,
                    BinaryOp.Xor  => v1 ^ v2,
                    BinaryOp.Shl  => v1 << shift,
                    BinaryOp.Shra => v1 >> shift,
                    BinaryOp.Shrl => (long)(i1.UnsignedStackValue >> shift),
                    _ => null
                };
                if (result != null) {
                    var resultType = BinaryInst.GetResultType(op, i1.ResultType, i2.ResultType)!;
                    return ConstInt.Create(resultType, result.Value);
                }
                break;
            }
            case (ConstFloat f1, ConstFloat f2, _): {
                double v1 = f1.Value;
                double v2 = f2.Value;

                double? result = op switch {
                    BinaryOp.FAdd => v1 + v2,
                    BinaryOp.FSub => v1 - v2,
                    BinaryOp.FMul => v1 * v2,
                    BinaryOp.FDiv => v1 / v2,
                    BinaryOp.FRem => v1 % v2,
                    _ => null
                };
                if (result != null) {
                    var resultType = f1.IsDouble || f2.IsDouble ? PrimType.Double : PrimType.Single;
                    return ConstFloat.Create(resultType, result.Value);
                }
                break;
            }
            // x op 0, x * 1, x & ~0 = x
            case (_, ConstInt { Value: 0 }, BinaryOp.Add or BinaryOp.Sub or BinaryOp.Or or BinaryOp.Xor or BinaryOp.Shl or BinaryOp.Shra or BinaryOp.Shrl):
            case (_, ConstInt { Value: 1 }, BinaryOp.Mul):
            case (_, ConstInt { SignedStackValue: -1 }, BinaryOp.And): {
                return left;
            }
            case (_, ConstInt { SignedStackValue: -1 }, BinaryOp.Or)
                when left.ResultType.StackType == right.ResultType.StackType: {
                return right;
            }
            // x * 0, x & 0 = 0
            case (_, ConstInt { Value: 0 }, BinaryOp.Mul or BinaryOp.And)
                when left.ResultType.StackType == right.ResultType.StackType: {
                return right;
            }
            // x - x, x ^ x = 0
            case (_, _, BinaryOp.Sub or BinaryOp.Xor) when left.Equals(right): {
                // Native-sized results need an explicit conversion in the IR.
                if (left.ResultType.IsPointerLike()) return null;
                return ConstInt.Create(left.ResultType, 0);
            }
            case (_, _, BinaryOp.And or BinaryOp.Or) when left.Equals(right): {
                return left;
            }
        }
        return null;
    }

    public static Value? FoldUnary(UnaryOp op, Value value)
    {
        return (op, value) switch {
            (UnaryOp.Neg, ConstInt v)   => ConstInt.Create(UnaryInst.GetResultType(v.ResultType), -v.SignedStackValue),
            (UnaryOp.Not, ConstInt v)   => ConstInt.Create(UnaryInst.GetResultType(v.ResultType), ~v.SignedStackValue),
            (UnaryOp.FNeg, ConstFloat v) => ConstFloat.Create(v.ResultType, -v.Value),
            _ => null
        };
    }

    public static Value? FoldConvert(Value srcValue, TypeDesc dstType, bool chkOverflow, bool srcUnsigned)
    {
        if (chkOverflow) {
            return null; // TODO
        }
        if (srcValue.ResultType == dstType) {
            return srcValue;
        }
        var simpleDstType = dstType.StackType;
        if (simpleDstType == StackType.Long) {
            simpleDstType = StackType.Int;
        }
        return (srcValue, simpleDstType) switch {
            (ConstInt c, StackType.Int)     => ConstInt.Create(dstType,
                dstType.Kind == TypeKind.UInt64 ? (long)c.UnsignedStackValue : c.SignedStackValue),
            (ConstInt c, StackType.Float) when dstType == PrimType.Single
                => ConstFloat.CreateS(srcUnsigned ? (float)c.UnsignedStackValue : (float)c.SignedStackValue),
            (ConstInt c, StackType.Float)   => ConstFloat.Create(dstType, srcUnsigned ? c.UnsignedStackValue : c.SignedStackValue),
            (ConstFloat c, StackType.Int)   => FoldFloatToInteger(c.Value, dstType),
            (ConstFloat c, StackType.Float) => ConstFloat.Create(dstType, c.Value),
            _ => null
        };
    }

    private static Value? FoldFloatToInteger(double value, TypeDesc destination)
    {
        // Out-of-range unchecked conversion results depend on the target CLR.
        // Preserve the instruction instead of folding through a wider host integer.
        int bits = destination.Kind.BitSize();
        if (bits == 0 || !double.IsFinite(value)) return null;
        bool unsigned = destination.Kind.IsUnsigned();
        double truncated = Math.Truncate(value);
        double limit = Math.ScaleB(1.0, unsigned ? bits : bits - 1);
        if (truncated < (unsigned ? 0 : -limit) || truncated >= limit) return null;
        return ConstInt.Create(destination, unsigned ? unchecked((long)(ulong)truncated) : (long)truncated);
    }

    public static Value? FoldCompare(CompareOp op, Value left, Value right)
    {
        bool? r = null;
        switch (left, right, op) {
            case (ConstInt i1, ConstInt i2, _): {
                int sc = i1.SignedStackValue.CompareTo(i2.SignedStackValue);
                int uc = i1.UnsignedStackValue.CompareTo(i2.UnsignedStackValue);

                r = op switch {
                    CompareOp.Eq => sc == 0,
                    CompareOp.Ne => sc != 0,
                    CompareOp.Slt => sc < 0,
                    CompareOp.Sgt => sc > 0,
                    CompareOp.Sle => sc <= 0,
                    CompareOp.Sge => sc >= 0,
                    CompareOp.Ult => uc < 0,
                    CompareOp.Ugt => uc > 0,
                    CompareOp.Ule => uc <= 0,
                    CompareOp.Uge => uc >= 0,
                    _ => null
                };
                break;
            }
            case (ConstFloat f1, ConstFloat f2, _): {
                double x = f1.Value, y = f2.Value;
                bool unordered = double.IsNaN(x) || double.IsNaN(y);

                r = op switch {
                    CompareOp.FOeq => x == y,
                    CompareOp.FOne => !unordered && x != y,
                    CompareOp.FOlt => x < y,
                    CompareOp.FOgt => x > y,
                    CompareOp.FOle => x <= y,
                    CompareOp.FOge => x >= y,
                    CompareOp.FUeq => unordered || x == y,
                    CompareOp.FUne => x != y,
                    CompareOp.FUlt => unordered || x < y,
                    CompareOp.FUgt => unordered || x > y,
                    CompareOp.FUle => unordered || x <= y,
                    CompareOp.FUge => unordered || x >= y,
                    _ => null
                };
                break;
            }
            case (Const c1, Const c2, CompareOp.Eq or CompareOp.Ne): {
                r = c1.Equals(c2) ^ (op == CompareOp.Ne);
                break;
            }
            // (x != 0) -> x  where x: bool
            // This is not valid according to ECMA, but we'll assume that all bools are 0/1. 
            case ({ ResultType.Kind: TypeKind.Bool }, ConstInt { Value: 0 }, CompareOp.Ne): {
                return left;
            }
            // x == x, x != x
            case (_, _, CompareOp.Eq or CompareOp.Ne) when left.Equals(right): {
                return ConstInt.Create(PrimType.Bool, op == CompareOp.Eq ? 1 : 0);
            }
            // (x != 0/null) when x is trivially known to be non-null
            case (_, ConstNull or ConstInt { Value: 0 }, CompareOp.Eq or CompareOp.Ne): {
                r = FoldCondition(left) ^ (op == CompareOp.Eq);
                break;
            }
        }
        return r == null ? null : ConstInt.Create(PrimType.Bool, r.Value ? 1 : 0);
    }

    public static Value? FoldSelect(Value cond, Value ifTrue, Value ifFalse)
    {
        if (ifTrue.Equals(ifFalse)) {
            return ifTrue;
        }

        return FoldCondition(cond) switch {
            true => ifTrue,
            false => ifFalse,
            _ => null
        };
    }

    /// <summary> Attempts to fold a conditional branch/switch in the block.  (goto 1 ? T : F)  ->  (goto T) </summary>
    public static bool FoldBlockBranch(BasicBlock block)
    {
        if (block.Last is BranchInst { IsConditional: true } br && FoldCondition(br.Cond) is bool cond) {
            block.ReplaceTerminator(new BranchInst(cond ? br.Then : br.Else!));

            return true;
        }

        if (block.Last is SwitchInst sw && (Fold(sw.TargetIndex) ?? sw.TargetIndex) is ConstInt caseIdx) {
            var targetBlock = sw.GetTarget((int)caseIdx.Value);

            block.ReplaceTerminator(new BranchInst(targetBlock));

            return true;
        }

        return false;
    }

    // TODO: evolve this using something like RangeAnalysis
    public static bool? FoldCondition(Value cond)
    {
        if (cond is CompareInst { Op: CompareOp.Ne, Right: ConstNull or ConstInt { Value: 0 } } cmp) {
            cond = cmp.Left;
        }
        if (cond is Instruction inst) {
            cond = Fold(inst) ?? cond;
        }

        return cond switch {
            ConstInt c => c.Value != 0,
            ConstNull => false,
            CilIntrinsic.Box { SourceType: { IsValueType: true, Name: not "Nullable`1" } } => true, // either throws or returns non-null 
            NewObjInst => true,
            _ => null,
        };
    }

    public static Value? FoldCall(MethodDesc method, ReadOnlySpan<Value> args)
    {
        try {
            return FoldCallCore(method, args);
        } catch (Exception ex) when (ex is ArgumentException or ArithmeticException ||
            ex is System.Reflection.TargetInvocationException { InnerException: ArgumentException or ArithmeticException }) {
            // A throwing operation must remain at its original execution point.
            return null;
        }
    }

    private static Value? FoldCallCore(MethodDesc method, ReadOnlySpan<Value> args)
    {
        if (IsCoreLibMethod(method, out var methodDef)) {
            var declType = methodDef.DeclaringType;

            object? result = (declType.Namespace, declType.Name, method.Name, AllConsts(args)) switch {
                ("System", "Math" or "MathF", _, true)
                    => FoldMathCall(methodDef, args.ToArray()),

                ("System", "String", _, true) when IsStringIntrinsic(methodDef)
                    => FoldStringCall(methodDef, args.ToArray()),

                ("System", "Type", "op_Equality", false)
                    => FoldTypeEquality(args[0], args[1]),

                ("System.Runtime.CompilerServices", "RuntimeHelpers", "IsReferenceOrContainsReferences", _)
                    => method.GenericParams[0].IsRefOrContainsRefs(),

                _ => null
            };
            return result switch {
                float r => ConstFloat.Create(method.ReturnType, r),
                double r => ConstFloat.Create(method.ReturnType, r),
                ulong r => ConstInt.Create(method.ReturnType, unchecked((long)r)),
                sbyte or byte or short or ushort or int or uint or long
                    => ConstInt.Create(method.ReturnType, System.Convert.ToInt64(result)),
                bool r   => ConstInt.Create(method.ReturnType, r ? 1 : 0),
                string r => ConstString.Create(r),
                _ => null
            };
        }
        return null;
    }

    // bool Type.op_Equality(Type a, Type b)
    private static bool? FoldTypeEquality(Value arg1, Value arg2)
    {
        var typeA = UnwrapType(arg1);
        var typeB = UnwrapType(arg2);

        if (typeA == null || typeB == null) return null;
        if ((typeA.IsUnboundGeneric || typeB.IsUnboundGeneric) && typeA != typeB) return null;

        return TypeUtils.GetRuntimeType(typeA) == TypeUtils.GetRuntimeType(typeB);

        static TypeDesc? UnwrapType(Value obj)
        {
            if (obj is not CallInst call) return null;

            if (call.Method.Name == "GetTypeFromHandle" && call.Method.DeclaringType.IsCorelibType(typeof(Type))) {
                return (call.Args[0] as CilIntrinsic.LoadHandle)?.StaticArgs[0] as TypeDesc;
            }
            if (call.Method.Name == "GetType" && call.Method.DeclaringType == PrimType.Object) {
                return TypeUtils.HasConcreteType(call.Args[0]) ? call.Args[0].ResultType : null;
            }
            return null;
        }
    }

    public static Value? FoldIntrinsic(IntrinsicInst intrin)
    {
        if (intrin is CilIntrinsic.DefaultValue) {
            var defaultType = intrin.ResultType;
            if (defaultType is GenericParamType || defaultType.StackType == StackType.Struct) return null;
            if (defaultType.StackType == StackType.ByRef) return new CilIntrinsic.RefCast(defaultType, ConstInt.CreateI(0));
            return defaultType.IsPointerLike()
                ? new ConvertInst(ConstInt.CreateI(0), PrimType.IntPtr)
                : Const.CreateZero(defaultType);
        }
        if (intrin is CilIntrinsic.RefCast reference &&
            reference.Args[0].ResultType.StackType == reference.ResultType.StackType &&
            (reference.Args[0] is ConstNull || reference.Args[0].ResultType.IsAssignableTo(reference.ResultType))) {
            return reference.Args[0];
        }
        if (intrin is CilIntrinsic.SizeOf { ObjType.Kind: >= TypeKind.Bool and <= TypeKind.Double and var type } && type.BitSize() != 0) {
            return ConstInt.CreateI(type.BitSize() / 8);
        }
        if (intrin is CilIntrinsic.AsInstance asi) {
            return TypeUtils.CheckCast(asi.Args[0], asi.DestType) switch {
                true => asi.Args[0],
                false => ConstNull.Create(),
                _ => null,
            };
        }
        if (intrin is CilIntrinsic.CastClass cast) {
            return cast.Args[0] is ConstNull || TypeUtils.CheckCast(cast.Args[0], cast.DestType) == true
                ? cast.Args[0] : null;
        }
        return null;
    }

    internal static bool IsMathIntrinsic(MethodDef method)
        => method.Name is ("Sqrt" or "Cbrt" or "Log" or "Log2" or "Log10" or "Exp" or
            "Floor" or "Ceiling" or "Truncate" or "Abs" or "Sin" or "Cos" or "Tan" or
            "Asin" or "Acos" or "Atan" or "Asinh" or "Acosh" or "Atanh" or "Atan2" or
            "Pow" or "Min" or "Max" or "CopySign" or "Round") &&
           method.ParamSig.All(parameter => parameter.Type.Kind is >= TypeKind.SByte and <= TypeKind.Double ||
               parameter.Type.IsCorelibType(typeof(MidpointRounding)));

    internal static bool IsStringIntrinsic(MethodDef method)
        => method.Name switch {
            "get_Length" or "get_Chars" => true,
            "Concat" => method.ParamSig.All(parameter => parameter.Type == PrimType.String || parameter.Type == PrimType.Object),
            "Replace" => method.ParamSig is [_, { Type: var oldType }, { Type: var newType }] &&
                oldType == newType && (oldType == PrimType.String || oldType == PrimType.Char),
            _ => false
        };

    private static object? FoldMathCall(MethodDef method, Value[] args)
    {
        // Only known operations on representable constants may run in the compiler.
        if (!IsMathIntrinsic(method)) return null;

        // Match the resolved signature exactly. Argument stack types alone cannot
        // distinguish signed/unsigned overloads, enum parameters, or MathF precision.
        var runtimeArgs = new object[args.Length];
        for (int i = 0; i < args.Length; i++) {
            var type = method.ParamSig[i].Type;
            object? arg = null;
            if (args[i] is ConstFloat f) {
                arg = type.Kind switch {
                    TypeKind.Single => (object)(float)f.Value,
                    TypeKind.Double => f.Value,
                    _ => null
                };
            } else if (args[i] is ConstInt c) {
                arg = type.IsCorelibType(typeof(MidpointRounding)) ? (MidpointRounding)c.Value : type.Kind switch {
                    TypeKind.SByte => (object)(sbyte)c.Value,
                    TypeKind.Byte => (byte)c.Value,
                    TypeKind.Int16 => (short)c.Value,
                    TypeKind.UInt16 => (ushort)c.Value,
                    TypeKind.Int32 => (int)c.Value,
                    TypeKind.UInt32 => (uint)c.Value,
                    TypeKind.Int64 => c.Value,
                    TypeKind.UInt64 => (ulong)c.Value,
                    _ => null
                };
            }
            if (arg == null) return null;
            runtimeArgs[i] = arg;
        }
        var runtimeType = method.DeclaringType.Name == "MathF" ? typeof(MathF) : typeof(Math);
        var target = runtimeType.GetMethod(method.Name, runtimeArgs.Select(a => a.GetType()).ToArray());
        return target?.Invoke(null, runtimeArgs);
    }
    private static object? FoldStringCall(MethodDef methodDef, Value[] args)
    {
        return (methodDef.Name, args) switch {
            ("get_Length", [ConstString str]) 
                => (long)str.Value.Length,
            
            ("get_Chars", [ConstString str, ConstInt idx]) 
                when idx.Value >= 0 && idx.Value < str.Value.Length
                => (long)str.Value[(int)idx.Value],

            ("Concat", _)
                when args.All(a => a is ConstString)
                => string.Concat(args.Select(a => ((ConstString)a).Value)),

            ("Replace", [ConstString str, ConstString oldStr, ConstString newStr])
                => str.Value.Replace(oldStr.Value, newStr.Value),

            ("Replace", [ConstString str, ConstInt oldChar, ConstInt newChar])
                => str.Value.Replace((char)oldChar.Value, (char)newChar.Value),

            _ => null
        };
    }

    private static bool IsCoreLibMethod(MethodDesc method, out MethodDef def)
    {
        def = (method as MethodDefOrSpec)?.Definition!;
        return def != null && def.DeclaringType.IsCorelibType();
    }
    private static bool AllConsts(ReadOnlySpan<Value> args)
    {
        foreach (var value in args) {
            if (value is not Const) {
                return false;
            }
        }
        return true;
    }
}
