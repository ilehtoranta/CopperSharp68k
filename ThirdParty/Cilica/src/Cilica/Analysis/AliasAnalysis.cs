namespace Cilica.Analysis;

public class AliasAnalysis : IMethodAnalysis
{
    public AliasAnalysis(MethodBody method) { }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new AliasAnalysis(mgr.Method);

    /// <summary> Checks if two given symbolic values may have the same value at runtime. </summary>
    public bool MayAlias(Value v1, Value v2)
    {
        if (object.ReferenceEquals(v1, v2)) {
            return true;
        }
        if (!MayTypesAlias(v1.ResultType, v2.ResultType)) {
            return false;
        }

        static int AssocRank(Value val) => val switch {
            LocalSlot => 10,
            FieldAddrInst => 20,
            ArrayAddrInst => 30,
            PtrOffsetInst => 40,
            _ => 100
        };

        if (AssocRank(v1) > AssocRank(v2)) {
            (v1, v2) = (v2, v1);
        }

        switch (v1, v2) {
            case (FieldAddrInst f1, FieldAddrInst f2): {
                var field1 = ((FieldDefOrSpec)f1.Field).Definition;
                var field2 = ((FieldDefOrSpec)f2.Field).Definition;
                if (field1 == field2 && f1.Field.DeclaringType == f2.Field.DeclaringType) {
                    return f1.IsStatic || MayAlias(f1.Obj, f2.Obj!);
                }
                if (f1.IsStatic && f2.IsStatic) {
                    if (field1.MappedData != null && field2.MappedData != null) return true;
                    return field1 == field2 && (f1.Field.DeclaringType.IsUnboundGeneric || f2.Field.DeclaringType.IsUnboundGeneric);
                }
                // Nested union fields and retyped byrefs can overlap even when
                // their immediate declaring types and field names differ.
                if (field1.DeclaringType.IsValueType || field2.DeclaringType.IsValueType) {
                    bool sameStorage = f1.IsInstance && f2.IsInstance && f1.Obj == f2.Obj &&
                        f1.Field.DeclaringType == f2.Field.DeclaringType;
                    return !sameStorage || field1.HasLayoutOffset || field2.HasLayoutOffset;
                }
                return field1.HasLayoutOffset || field2.HasLayoutOffset;
            }
            case (FieldAddrInst f1, ArrayAddrInst a1): {
                return f1.IsInstance && f1.Field.DeclaringType.IsValueType && 
                       a1.ElemType.IsValueType && MayAlias(f1.Obj, a1);
            }
            case (LocalSlot l1, FieldAddrInst f1): {
                return f1.IsInstance && f1.Field.DeclaringType.IsValueType && MayAlias(l1, f1.Obj);
            }
            case (ArrayAddrInst a1, ArrayAddrInst a2) : {
                return MayAlias(a1.Array, a2.Array) && MayAlias(a1.Index, a2.Index);
            }
            case (LocalSlot l1, LocalSlot l2): {
                return l1 == l2;
            }
            case (LocalSlot, ArrayAddrInst or Argument or Const): {
                return false;
            }
            case (Const, Const): {
                return v1.Equals(v2);
            }
        }
        return true;
    }

    /// Checks if two objects/pointers of the given types can be aliased.
    private bool MayTypesAlias(TypeDesc t1, TypeDesc t2)
    {
        if (!object.ReferenceEquals(t1, t2)) {
            // Unsafe.As and raw CIL can reinterpret the same storage.
            if (t1 is PointerType && t2 is PointerType) {
                return true;
            }
            if (t1 is ArrayType && t2 is ArrayType) {
                var e1 = t1.ElemType!;
                var e2 = t2.ElemType!;

                // U[] and S[] may alias (e.g. int[] and uint[])
                if (e1.Kind.IsInt() || e2.Kind.IsInt()) {
                    return e1.Kind.GetStorageType() == e2.Kind.GetStorageType();
                }
                // A[] and B[] may alias if both are classes and one inherits from the other.
                // otherwise, T[] may alias with T[]
                return MayObjTypesAlias(e1, e2) ?? (e1 == e2);
            }
            return MayObjTypesAlias(t1, t2) ?? true;
        }
        return true;
    }

    // Checks if two objects of the given types may alias. If both aren't object types, returns null.
    private bool? MayObjTypesAlias(TypeDesc t1, TypeDesc t2)
    {
        bool isObj1 = t1.IsManagedObject();
        bool isObj2 = t2.IsManagedObject();

        if (isObj1 || isObj2) {
            if (isObj1 && isObj2) {
                if (t1.IsUnboundGeneric || t2.IsUnboundGeneric) return true;
                if (t1.Inherits(t2) || t2.Inherits(t1)) return true;
                // An unsealed class can have subclasses implementing an interface;
                // unrelated interfaces can share the same concrete implementation.
                if (t1.IsInterface || t2.IsInterface) {
                    var other = t1.IsInterface ? t2 : t1;
                    return other is not TypeDefOrSpec def || !def.Attribs.HasFlag(System.Reflection.TypeAttributes.Sealed) ||
                        TypeUtils.CanHaveDynamicInterfaces(other);
                }
                return false;
            }
            return false;
        }
        return null;
    }
}
