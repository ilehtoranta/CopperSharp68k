namespace Cilica.AsmIO;

using System.Reflection;

public static class TypeUtils
{
    /// <summary> Whether accessing this type can execute a type initializer. </summary>
    /// <param name="fieldAccess">Field accesses trigger initialization even for BeforeFieldInit types.</param>
    public static bool RequiresTypeInitialization(this TypeDesc type, bool fieldAccess = false)
        => fieldAccess && type.RequiresModuleInitialization() || type is TypeDefOrSpec def &&
           (fieldAccess || !def.Attribs.HasFlag(TypeAttributes.BeforeFieldInit)) &&
           def.Methods.Any(m => m.Name == ".cctor");

    /// <summary> Whether this type's module defines a module initializer. </summary>
    public static bool RequiresModuleInitialization(this TypeDesc type)
        => type is TypeDefOrSpec def && def.Module.RequiresModuleInitialization();

    /// <summary> Whether this module defines a module initializer. </summary>
    public static bool RequiresModuleInitialization(this ModuleDef module)
        => module.TypeDefs.FirstOrDefault(candidate => candidate.Name == "<Module>")
            ?.Methods.Any(method => method.Name == ".cctor") == true;

    /// <summary> Whether invoking this method can run its declaring type's initializer. </summary>
    /// <remarks> Constructors (including base constructor calls), static methods, and value-type or interface instance methods trigger precise initialization. </remarks>
    public static bool RequiresTypeInitialization(this MethodDesc method)
        => (method.IsStatic || method.IsConstructor || method.DeclaringType.IsValueType || method.DeclaringType.IsInterface) &&
           method.DeclaringType.RequiresTypeInitialization();

    /// <summary> Checks if this type is defined in <c>System.Private.CoreLib</c>. </summary>
    public static bool IsCorelibType(this TypeDesc desc)
    {
        return desc is TypeDefOrSpec def && def.Module == def.Module.Resolver.CoreLib;
    }
    
    /// <summary> Checks if this type is the definition of a type in CoreLib, <paramref name="rtType"/>. </summary>
    /// <remarks> This method compares by names, generic instantiations are not considered. </remarks>
    public static bool IsCorelibType(this TypeDesc desc, Type rtType)
    {
        return desc.IsCorelibType() &&
               desc.Name == rtType.Name &&
               desc.Namespace == rtType.Namespace;
    }

    /// <summary> Whether this type represents a primitive integer (byte, int, long, etc.). </summary>
    public static bool IsInt(this TypeDesc type)
    {
        return type.StackType is StackType.Int or StackType.Long;
    }
    /// <summary> Whether this type represents a float or double. </summary>
    public static bool IsFloat(this TypeDesc type)
    {
        return type.StackType is StackType.Float;
    }

    /// <summary> Whether this type represents a byref, pointer, or nint. </summary>
    public static bool IsPointerLike(this TypeDesc type)
    {
        return type.StackType is StackType.ByRef or StackType.NInt;
    }

    public static bool IsPointerOrObject(this TypeDesc type)
    {
        return type.StackType is StackType.ByRef or StackType.NInt or StackType.Object;
    }

    public static bool IsManagedObject(this TypeDesc type)
    {
        return type.StackType is StackType.Object;
    }

    /// <summary> If this type is generic, returns an <see cref="TypeSpec"/> with all unbound parameters. Otherwise, returns the unchanged instance. </summary>
    public static TypeDesc GetUnboundSpec(this TypeDesc type)
    {
        return type is TypeDefOrSpec { Definition: var def } 
            ? def.GetSpec(GenericContext.Empty)
            : type;
    }

    /// <summary> Checks if the given value has a concrete result object type (it's statically known). </summary>
    public static bool HasConcreteType(Value obj)
    {
        // TODO: could probably build a more sophisticated analysis for this, with propagation / use chain scan
        return obj is NewObjInst { ResultType: not MDArrayType } && !IsComImportType(obj.ResultType) || IsExactType(obj.ResultType);
    }

    internal static bool IsExactType(TypeDesc type)
    {
        if (type is not TypeDefOrSpec def || !def.Attribs.HasFlag(TypeAttributes.Sealed) || IsComImportType(type)) return false;

        // Variance applies only to reference-type arguments. A sealed delegate
        // such as Func<int, int> still has an exact runtime type.
        for (int i = 0; i < def.Definition.GenericParams.Length; i++) {
            var parameter = def.Definition.GenericParams[i];
            if ((parameter.IsCovariant || parameter.IsContravariant) && !def.GenericParams[i].IsValueType) return false;
        }
        return true;
    }

    // Class MethodImpl records can redirect even nonvirtual calls to virtual
    // methods. The CLR owns those entry-point mappings until we model them.
    internal static bool HasClassMethodImpls(MethodDesc method)
    {
        if (!method.Attribs.HasFlag(MethodAttributes.Virtual)) return false;
        for (var type = method.DeclaringType as TypeDefOrSpec; type != null; type = type.BaseType) {
            if (type.Definition.MethodImpls.Keys.Any(declaration => !declaration.DeclaringType.IsInterface)) return true;
        }
        return false;
    }

    public static MethodDesc? ResolveVirtualMethod(MethodDesc method, Value instanceObj)
    {
        return HasConcreteType(instanceObj) ? ResolveVirtualMethod(method, instanceObj.ResultType) : null;
    }

    /// <summary> Resolves the implementation of the given virtual method defined by a concrete instance type. </summary>
    public static MethodDesc? ResolveVirtualMethod(MethodDesc method, TypeDesc actualType)
    {
        Ensure.That(method.Attribs.HasFlag(MethodAttributes.Virtual));

        return actualType is TypeDefOrSpec type ? VirtualDispatch.Resolve(method, type) : null;
    }

    /// <summary> Checks if the given object can be cast to <paramref name="destType"/>, or null if unknown. </summary>
    public static bool? CheckCast(Value obj, TypeDesc destType)
    {
        var srcType = GetRuntimeType(obj.ResultType);
        destType = GetRuntimeType(destType);
        bool castable = srcType.IsAssignableTo(destType);
        if (!castable && (srcType.IsUnboundGeneric || destType.IsUnboundGeneric)) return null;
        // COM activation can produce a runtime wrapper outside the declared class hierarchy.
        if (!castable && (IsComImportType(srcType) || IsComImportType(destType))) return null;
        if (!castable && destType.IsInterface && CanHaveDynamicInterfaces(srcType)) return null;

        // If the object type is known or already assignable to `destType`, we don't need to look further.
        if (castable || HasConcreteType(obj)) {
            return castable;
        }

        // Impossible cast: Both `srcType` and `destType` are classes, but `destType` doesn't inherit from srcType
        if (srcType.IsClass && destType.IsClass && !destType.Inherits(srcType)) {
            return false;
        }

        // Impossible cast: `destType` is an array but `srcType` is some other def
        if (destType is ArrayType && srcType is not (ArrayType or MDArrayType) &&
            !srcType.IsInterface && srcType != PrimType.Object && srcType != PrimType.Array) {
            return false;
        }

        // No idea
        return null;
    }

    private static bool IsComImportType(TypeDesc type)
    {
        for (var current = type as TypeDefOrSpec; current != null; current = current.BaseType) {
            if (current.Attribs.HasFlag(TypeAttributes.Import)) return true;
        }
        return false;
    }

    /// <summary> Removes element modifiers that do not participate in runtime type identity. </summary>
    internal static TypeDesc GetRuntimeType(TypeDesc type)
    {
        if (type is CompoundType compound) {
            var element = GetRuntimeType(compound.ElemType);
            if (element == compound.ElemType && compound.ElementModifiers.IsEmpty) return type;
            return compound switch {
                ArrayType => element.CreateArray(),
                MDArrayType array => new MDArrayType(element, array.Rank, array.LowerBounds, array.Sizes),
                ByrefType => element.CreateByref(),
                PointerType => element.CreatePointer(),
                _ => throw new UnreachableException()
            };
        }
        if (type is TypeSpec nominal) {
            var arguments = nominal.GenericParams.Select(GetRuntimeType).ToImmutableArray();
            if (!arguments.SequenceEqual(nominal.GenericParams)) return nominal.Definition.GetSpec(arguments);
        }
        return type;
    }

    internal static bool CanHaveDynamicInterfaces(TypeDesc type)
    {
        var pending = new Stack<TypeDesc>();
        var visited = new HashSet<TypeDesc>();
        pending.Push(type);
        while (pending.TryPop(out var current)) {
            if (!visited.Add(current)) continue;
            if (current is TypeDefOrSpec nominal && nominal.Attribs.HasFlag(TypeAttributes.Import) ||
                current.IsCorelibType() && current.Name is "__ComObject" or "IDynamicInterfaceCastable" or "ICastable") return true;
            foreach (var itf in current.Interfaces) pending.Push(itf);
            if (current.BaseType != null) pending.Push(current.BaseType);
        }
        return false;
    }

    /// <summary> Checks if this type is or contains managed references. May return null if the type is an open generic. </summary>
    /// <remarks> See <see cref="System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences{T}"/> </remarks>
    public static bool? IsRefOrContainsRefs(this TypeDesc type)
    {
        if (type is GenericParamType) return null;
        if (type is TypeDefOrSpec { IsValueType: true }) {
            bool unknown = false;
            foreach (var field in type.Fields) {
                if (!field.IsInstance) continue;
                var containsRefs = field.Type.IsRefOrContainsRefs();
                if (containsRefs == true) return true;
                unknown |= containsRefs == null;
            }
            return unknown ? null : false;
        }
        return type.StackType is StackType.Object or StackType.ByRef;
    }
}
