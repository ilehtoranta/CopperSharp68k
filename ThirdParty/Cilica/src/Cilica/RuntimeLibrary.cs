namespace Cilica;

using Cilica.Analysis;
using Cilica.IR.Utils;

/// <summary> Semantic contracts for recognized methods in the resolved runtime library. </summary>
public sealed class RuntimeLibrary(ModuleResolver resolver)
{
    public ModuleResolver Resolver { get; } = resolver;

    // Retain calls whose contracts are consumed by simplification, LINQ expansion,
    // list presizing, or vectorization instead of exposing their runtime bodies.
    internal bool IsOptimizationIntrinsic(MethodDef method)
    {
        var type = method.DeclaringType;
        if (type.Namespace == "System.Linq" && type.Name == "Enumerable" &&
            method.Module == Resolver.Resolve("System.Linq")) {
            return method.Name switch {
                "Range" or "Where" or "Select" or "SelectMany" or "Skip" or "Cast" or "OfType" or
                "ToList" or "ToArray" or "ToHashSet" or "Aggregate" or "First" or "FirstOrDefault" or "All" => true,
                "Count" or "Any" => method.ParamSig.Count == 2,
                "Take" => method.ParamSig[1] == PrimType.Int32,
                "ToDictionary" => method.ParamSig is [_, { Type.Name: "Func`2" }, ..],
                "Sum" => method.ParamSig.Count is 1 or 2 && method.ReturnType.Kind is TypeKind.Int32 or TypeKind.Int64,
                _ => false
            };
        }
        if (!type.IsCorelibType()) return false;

        return (type.Namespace, type.Name, method.Name) switch {
            ("System", "Math" or "MathF", _) => ConstFolding.IsMathIntrinsic(method),
            ("System", "String", "GetPinnableReference") => true,
            ("System", "String", _) => ConstFolding.IsStringIntrinsic(method),
            ("System", "Type", "GetTypeFromHandle" or "op_Equality") => true,
            ("System", "Span`1" or "ReadOnlySpan`1", "get_Length" or "get_Item" or "GetPinnableReference") => true,
            ("System.Collections.Generic", "List`1", "Add" or "ToArray" or "get_Count" or "get_Item") => true,
            ("System.Collections.Generic", "Dictionary`2", "ContainsKey" or "get_Item") => true,
            ("System.Runtime.InteropServices", "MemoryMarshal", "GetArrayDataReference") => true,
            ("System.Runtime.CompilerServices", "RuntimeHelpers", "IsReferenceOrContainsReferences") => true,
            _ => false
        };
    }

    /// <summary> Whether the sequence uses mutable indexed storage with library-specific iteration rules. </summary>
    public static bool IsMutableIndexedSequence(TypeDesc type)
    {
        var pending = new Stack<TypeDesc>();
        var visited = new HashSet<TypeDesc>();
        pending.Push(type);

        while (pending.TryPop(out var current)) {
            if (!visited.Add(current)) {
                continue;
            }

            if (current.IsCorelibType(typeof(List<>)) || current.IsCorelibType(typeof(IList<>))) {
                return true;
            }

            foreach (var implemented in current.Interfaces) {
                pending.Push(implemented);
            }

            if (current.BaseType is { } parent) {
                pending.Push(parent);
            }
        }

        return false;
    }

    /// <summary> Whether source dispatch is known to use ordinary enumeration or fixed-length storage. </summary>
    public static bool HasKnownSequenceDispatch(Value source)
    {
        var type = source is CilIntrinsic.Box box ? box.SourceType : source.ResultType;
        if (type is GenericParamType) return false;

        if (type is ArrayType || type.Kind == TypeKind.String || type.IsCorelibType(typeof(ArraySegment<>))) {
            return true;
        }

        if (IsMutableIndexedSequence(type)) {
            return false;
        }

        if (source is CallInst { Method: MethodDefOrSpec method } call) {
            if (method.DeclaringType.Namespace == "System.Linq" && method.DeclaringType.Name == "Enumerable" &&
                method.Module == method.Module.Resolver.Resolve("System.Linq")) {
                return method.Name == "AsEnumerable" ? HasKnownSequenceDispatch(call.Args[0])
                    : method.Name is "Range" or "Where" or "Select" or "SelectMany" or "Skip" or "Take" or "Cast" or "OfType";
            }

            if (method.Definition.HasCustomAttrib("System.Runtime.CompilerServices", "IteratorStateMachineAttribute")) {
                return true;
            }
        }

        return source is NewObjInst or CilIntrinsic.Box ||
            type is TypeDefOrSpec definition && definition.Attribs.HasFlag(System.Reflection.TypeAttributes.Sealed);
    }

    /// <summary> Whether cardinality can be read without invoking a user implementation. </summary>
    public static bool HasKnownCollectionCount(Value source)
        => source.ResultType is ArrayType || source.ResultType.Kind == TypeKind.String ||
           source is NewObjInst allocation && allocation.Constructor.DeclaringType.IsCorelibType(typeof(List<>));

    public static Value EmitCollectionCount(IRBuilder builder, Value source)
    {
        Ensure.That(HasKnownCollectionCount(source));

        return source.ResultType is ArrayType ? builder.CreateConvert(builder.CreateArrayLen(source), PrimType.Int32)
            : builder.CreateCallVirt(source.ResultType.Kind == TypeKind.String ? "get_Length" : "get_Count", source);
    }

    public FunctionEffects? GetKnownEffects(MethodDef method)
    {
        var type = method.DeclaringType;

        if (!type.IsCorelibType()) {
            return null;
        }

        bool Matches(string name, TypeDesc result, bool instance, params TypeSig[] parameters)
            => method.Name == name && new MethodSig(result, parameters, instance).Matches(method);

        if (type.Namespace == "System.Collections.Generic" && type.Name is "Dictionary`2" or "List`1" or "HashSet`1") {
            if (Matches("get_Count", PrimType.Int32, true)) {
                return new(MemoryEffects.Read, FunctionTraits.MayThrow);
            }

            if (type.Name == "List`1" && Matches("get_Item", GenericParamType.GetUnboundT(0), true, PrimType.Int32)) {
                return new(MemoryEffects.Read, FunctionTraits.MayThrow);
            }

            return null;
        }

        if (type.Namespace == "System" && type.Name == "String") {
            if (Matches("op_Equality", PrimType.Bool, false, PrimType.String, PrimType.String) ||
                Matches("op_Inequality", PrimType.Bool, false, PrimType.String, PrimType.String)) {
                return new(MemoryEffects.None, FunctionTraits.None);
            }

            if (Matches("get_Length", PrimType.Int32, true)) {
                return new(MemoryEffects.None, FunctionTraits.MayThrow);
            }

            if (Matches("ToUpperInvariant", PrimType.String, true) || Matches("ToLowerInvariant", PrimType.String, true)) {
                return new(MemoryEffects.None, FunctionTraits.MayThrow | FunctionTraits.Allocates);
            }
        }

        if (type.Namespace == "System" && type.Name is "Math" or "MathF" && method.IsStatic) {
            var scalar = type.Name == "MathF" ? PrimType.Single : PrimType.Double;
            int arity = method.Name switch {
                "Floor" or "Ceiling" or "Round" or "Truncate" or "Abs" or "Sqrt" or "Cbrt" or
                "Log" or "Log2" or "Log10" or "Exp" or "Sin" or "Cos" or "Tan" or "Sinh" or "Cosh" or "Tanh" or
                "Asin" or "Acos" or "Atan" or "Asinh" or "Acosh" or "Atanh" or
                "ReciprocalEstimate" or "ReciprocalSqrtEstimate" => 1,
                "Pow" or "Atan2" or "Min" or "Max" or "MaxMagnitude" or "MinMagnitude" or "CopySign" => 2,
                "FusedMultiplyAdd" => 3,
                _ => -1
            };

            if (method.GenericParams.Length == 0 && method.ReturnType == scalar && method.ParamSig.Count == arity &&
                method.ParamSig.All(p => p.Type == scalar)) {
                return new(MemoryEffects.None, FunctionTraits.None);
            }
        }

        return null;
    }
}
