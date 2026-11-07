namespace Cilica.Passes;

using Cilica.IR.Utils;

partial class SimplifyInsts
{
    // Optimize double dictionary lookups:
    //
    //  if (dict.ContainsKey(key))
    //    return dict[key];
    // ->
    //  if (dict.TryGetValue(key, out long val))
    //    return val;
    //
    //  BB_01:
    //    bool r2 = callvirt Dictionary`2[string, long]::ContainsKey(this: #dict, string: #key)
    //    goto r2 ? BB_04 : BB_07
    //  BB_04:
    //    long r5 = callvirt Dictionary`2[string, long]::get_Item(this: #dict, string: #key)
    //    ret r5
    // ->
    //  BB_01:
    //    long& r2 = varaddr $loc1
    //    bool r3 = callvirt Dictionary`2[string, long]::TryGetValue(this: #dict, string: #key, long&: r2)
    //    goto r3 ? BB_05 : BB_08
    //  BB_05: // preds: BB_01
    //    long r6 = ldvar $loc1
    //    ret r6
    private bool SimplifyDictLookup(CallInst call)
    {
        Debug.Assert(call.Method.Name == "ContainsKey");

        if (!(call.Next is BranchInst br && br.Cond == call && call.Args is [TrackedValue instance, var key])) return false;

        var declType = (TypeSpec)call.Method.DeclaringType;
        // Coalescing lookups requires both the same control-flow path and a
        // comparer whose hash/equality operations have no user-visible effects.
        if (br.Then.NumPreds != 1 || !HasPureDefaultComparer(instance, declType)) return false;
        // Only check for the get_Item() call once, because guaranteeing that the dictionary
        // won't change in between calls is tricky. (VN may handle this in the future.)
        if (br.Then.First is not CallInst { Method.Name: "get_Item" } getCall ||
            getCall.Method.DeclaringType != declType ||
            getCall.Args[0] != instance || getCall.Args[1] != key) return false;


        var builder = new IRBuilder(call, InsertionDir.After);
        
        var tempVar = builder.Method.CreateVar(declType.GenericParams[1], "dictLookupOut");
        var replacement = builder.CreateCallVirt("TryGetValue", instance, key, tempVar);
        OnCallReplaced?.Invoke(call, replacement);
        call.ReplaceWith(replacement);

        builder.SetPosition(getCall, InsertionDir.After);
        getCall.ReplaceWith(builder.CreateLoad(tempVar));

        return true;
    }

    private static bool HasPureDefaultComparer(Value instance, TypeSpec dictionaryType)
    {
        var keyType = dictionaryType.GenericParams[0];
        if (keyType.Kind is not (>= TypeKind.Bool and <= TypeKind.Double or TypeKind.String)) return false;
        return instance is NewObjInst alloc && alloc.ResultType == dictionaryType && alloc.Args switch {
            [] or [ConstInt] or [ConstNull] or [ConstInt, ConstNull] => true,
            _ => false
        };
    }
}
