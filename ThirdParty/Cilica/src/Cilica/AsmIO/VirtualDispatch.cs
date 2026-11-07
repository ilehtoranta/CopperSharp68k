namespace Cilica.AsmIO;

using System.Reflection;

/// <summary> Resolves class slots and exact interface implementations without runtime lookups. </summary>
internal static class VirtualDispatch
{
    readonly record struct MethodKey(MethodDef Definition, TypeDefOrSpec Owner);
    sealed class Slot(MethodDefOrSpec implementation)
    {
        public MethodDefOrSpec Implementation = implementation;
        public bool PreserveBaseOverrides;
    }

    public static MethodDesc? Resolve(MethodDesc method, TypeDefOrSpec actualType)
    {
        var requested = DefinitionOf(method);
        if (requested == null || !actualType.Inherits(method.DeclaringType) || !method.IsStatic && actualType.IsInterface) return null;
        var resolver = new Resolver(requested.GenericParams, actualType);
        return resolver.Resolve(requested);
    }

    // A class MethodImpl can redirect a nonvirtual call as well. Resolve only
    // in the declaring type here, not in the receiver's more-derived type.
    public static MethodDesc? ResolveDirect(MethodDefOrSpec method)
        => method.IsStatic || method.DeclaringType.IsInterface || !TypeUtils.HasClassMethodImpls(method)
            ? method : new Resolver(method.GenericParams, method.DeclaringType).Resolve(method);

    public static bool SameMethod(MethodDesc? a, MethodDesc? b)
        => a is MethodDefOrSpec x && b is MethodDefOrSpec y && Key(x) == Key(y) &&
           x.GenericParams.SequenceEqual(y.GenericParams);

    static MethodKey Key(MethodDefOrSpec method)
        => new(method.Definition, Normalize(method.DeclaringType));

    static TypeDefOrSpec Normalize(TypeDefOrSpec type)
        => type is TypeDef ? (TypeDefOrSpec)type.GetUnboundSpec() : type;

    static MethodDefOrSpec? DefinitionOf(MethodDesc method)
        => method as MethodDefOrSpec ?? (method as MethodReference)?.Target as MethodDefOrSpec;

    sealed class Resolver(IReadOnlyList<TypeDesc> methodArguments, TypeDefOrSpec actualType)
    {
        readonly List<TypeDefOrSpec> _hierarchy = BuildHierarchy(actualType);
        readonly List<Slot> _slots = new();
        readonly Dictionary<MethodKey, int> _methodSlots = new();
        readonly HashSet<MethodKey> _methodImplBodies = new();

        MethodDefOrSpec Bind(MethodDef method, TypeDefOrSpec owner)
            => method.GetSpec(new GenericContext(owner.GenericParams, methodArguments));

        public MethodDesc? Resolve(MethodDefOrSpec requested)
        {
            if (!requested.IsStatic && !BuildClassSlots()) return null;
            MethodDesc? result;
            if (requested.DeclaringType.IsInterface) {
                result = ResolveInterface(requested);
            } else {
                result = _methodSlots.TryGetValue(Key(requested), out int slot) ? _slots[slot].Implementation : null;
            }
            return result?.Attribs.HasFlag(MethodAttributes.Abstract) == false ? result : null;
        }

        bool BuildClassSlots()
        {
            var methodsByName = _hierarchy.Select(type => type.Definition.Methods.ToLookup(method => method.Name)).ToArray();
            for (int depth = 0; depth < _hierarchy.Count; depth++) {
                var owner = _hierarchy[depth];
                var previous = _slots.Select(slot => slot.Implementation).ToArray();
                var changed = new HashSet<int>();
                foreach (var definition in owner.Definition.Methods) {
                    if (definition.IsStatic || !definition.Attribs.HasFlag(MethodAttributes.Virtual) ||
                        definition.GenericParams.Length != methodArguments.Count) continue;
                    var member = Bind(definition, owner);
                    int slot = -1;
                    if (!definition.Attribs.HasFlag(MethodAttributes.NewSlot)) {
                        for (int parent = depth - 1; parent >= 0 && slot < 0; parent--) {
                            foreach (var candidate in methodsByName[parent][definition.Name]) {
                                if (candidate.IsStatic || !candidate.Attribs.HasFlag(MethodAttributes.Virtual) ||
                                    candidate.GenericParams.Length != methodArguments.Count) continue;
                                var bound = Bind(candidate, _hierarchy[parent]);
                                if (!SameSignature(member, bound)) continue;
                                int match = _methodSlots[Key(bound)];
                                if (slot >= 0 && slot != match) return false;
                                slot = match;
                            }
                        }
                    }
                    if (slot < 0) { slot = _slots.Count; _slots.Add(new Slot(member)); }
                    else { _slots[slot].Implementation = member; changed.Add(slot); }
                    _methodSlots.Add(Key(member), slot);
                }

                var context = new GenericContext(owner.GenericParams, methodArguments);
                foreach (var (declaration, body) in owner.Definition.MethodImpls) {
                    if (body.IsStatic || body.GenericParams.Length != methodArguments.Count) continue;
                    var implementation = Bind(body, owner);
                    _methodImplBodies.Add(Key(implementation));
                    if (declaration.DeclaringType.IsInterface) continue;
                    var target = ResolveDeclaration(declaration.GetSpec(context));
                    if (target == null || !_methodSlots.TryGetValue(Key(target), out int slot)) return false;
                    _slots[slot].Implementation = implementation;
                    changed.Add(slot);
                }

                // Inherited aliases follow overrides of their implementation's
                // own slot, unless this class explicitly replaced the alias.
                for (int slot = 0; slot < previous.Length; slot++) {
                    if (!changed.Contains(slot) && _methodSlots.TryGetValue(Key(previous[slot]), out int primary) && primary != slot)
                        _slots[slot].Implementation = _slots[primary].Implementation;
                }

                // Match ClassLoader::PropagateCovariantReturnMethodImplSlots:
                // the attribute belongs to a MethodImpl slot and is inherited.
                for (int slot = 0; slot < _slots.Count; slot++) {
                    var implementation = _slots[slot].Implementation;
                    bool methodImpl = _methodImplBodies.Contains(Key(implementation));
                    _slots[slot].PreserveBaseOverrides |= methodImpl && implementation.Definition.HasCustomAttrib(
                        "System.Runtime.CompilerServices", "PreserveBaseOverridesAttribute");
                    if (slot >= previous.Length || !methodImpl || !_slots[slot].PreserveBaseOverrides ||
                        SameMethod(implementation, previous[slot])) continue;
                    for (int alias = slot + 1; alias < previous.Length; alias++) {
                        if (SameMethod(_slots[alias].Implementation, previous[slot]))
                            _slots[alias].Implementation = implementation;
                    }
                }
            }
            return true;
        }

        MethodDefOrSpec? ResolveDeclaration(MethodDesc method)
        {
            if (DefinitionOf(method) is { } definition) return definition;
            // MemberRefs on an intermediate generic base use that type's
            // parameter context. Search instantiated signatures, retaining
            // ambiguity rather than choosing a declaration by metadata order.
            for (var owner = method.DeclaringType as TypeDefOrSpec; owner != null; owner = owner.BaseType) {
                var matches = owner.Definition.Methods.Where(candidate => candidate.GenericParams.Length == methodArguments.Count)
                    .Select(candidate => Bind(candidate, owner)).Where(candidate => SameSignature(candidate, method)).ToArray();
                if (matches.Length != 0) return matches.Length == 1 ? matches[0] : null;
            }
            return null;
        }

        MethodDesc? ResolveInterface(MethodDefOrSpec requested)
        {
            var interfaces = _hierarchy.SelectMany(ImplementedInterfaces)
                .Concat(actualType.IsInterface ? [actualType] : Array.Empty<TypeDefOrSpec>()).Distinct().ToArray();
            var alternatives = interfaces.Where(itf => itf.Definition == requested.DeclaringType.Definition).ToArray();
            // Distinct open interfaces can collapse after substitution. An
            // otherwise unique exact map is stable across generic instantiations.
            if (actualType.IsUnboundGeneric && alternatives.Any(itf => itf != requested.DeclaringType &&
                (itf.IsUnboundGeneric || requested.DeclaringType.IsUnboundGeneric))) return null;
            if (!interfaces.Contains(requested.DeclaringType)) {
                var compatible = alternatives.Where(itf => itf.IsAssignableTo(requested.DeclaringType)).ToArray();
                if (compatible.Length != 1) return null;
                requested = requested.Definition.GetSpec(new GenericContext(compatible[0].GenericParams, methodArguments));
            }

            MethodDefOrSpec? implementation = null;
            int interfaceOwner = -1;
            for (int depth = 0; depth < _hierarchy.Count; depth++) {
                var owner = _hierarchy[depth];
                var context = new GenericContext(owner.GenericParams, methodArguments);
                var explicitMatches = owner.Definition.MethodImpls.Where(pair => SameMethod(
                    ResolveDeclaration(pair.Key.GetSpec(context)), requested)).Select(pair => Bind(pair.Value, owner)).ToArray();
                if (explicitMatches.Length > 1) return null;
                if (explicitMatches.Length == 1) {
                    implementation = explicitMatches[0]; interfaceOwner = depth;
                } else if (!requested.IsStatic && (ImplementedInterfaces(owner).Contains(requested.DeclaringType) ||
                    implementation == null && depth > 0 && _hierarchy[depth - 1].Attribs.HasFlag(TypeAttributes.Abstract) &&
                    _hierarchy.Take(depth).SelectMany(ImplementedInterfaces).Contains(requested.DeclaringType))) {
                    for (int parent = depth; parent > interfaceOwner; parent--) {
                        var matches = _hierarchy[parent].Definition.Methods.Where(candidate => candidate.IsPublic && !candidate.IsStatic &&
                            candidate.Attribs.HasFlag(MethodAttributes.Virtual) &&
                            candidate.GenericParams.Length == methodArguments.Count)
                            .Select(candidate => Bind(candidate, _hierarchy[parent])).Where(candidate => SameSignature(candidate, requested)).ToArray();
                        if (matches.Length > 1) return null;
                        if (matches.Length == 1) { implementation = matches[0]; interfaceOwner = depth; break; }
                    }
                }
            }
            if (implementation != null) {
                return _methodSlots.TryGetValue(Key(implementation), out int slot) ? _slots[slot].Implementation : implementation;
            }
            return ResolveDefaultInterface(requested, interfaces);
        }

        MethodDesc? ResolveDefaultInterface(MethodDefOrSpec requested, TypeDefOrSpec[] interfaces)
        {
            var candidates = new List<(TypeDefOrSpec Owner, MethodDefOrSpec Method)>();
            if (!requested.Attribs.HasFlag(MethodAttributes.Abstract)) candidates.Add((requested.DeclaringType, requested));
            foreach (var itf in interfaces) {
                var context = new GenericContext(itf.GenericParams, methodArguments);
                foreach (var (declaration, body) in itf.Definition.MethodImpls) {
                    if (SameMethod(ResolveDeclaration(declaration.GetSpec(context)), requested)) candidates.Add((itf, Bind(body, itf)));
                }
            }
            var mostSpecific = candidates.Where(candidate => !candidates.Any(other => other.Owner != candidate.Owner &&
                ImplementedInterfaces(other.Owner).Contains(candidate.Owner))).ToArray();
            return mostSpecific.Length == 1 ? mostSpecific[0].Method : null;
        }
    }

    static List<TypeDefOrSpec> BuildHierarchy(TypeDefOrSpec type)
    {
        var result = new List<TypeDefOrSpec>();
        for (var current = type; current != null; current = current.BaseType)
            result.Add(Normalize(current));
        result.Reverse();
        return result;
    }

    static IEnumerable<TypeDefOrSpec> ImplementedInterfaces(TypeDefOrSpec type)
    {
        var pending = new Stack<TypeDefOrSpec>(type.Interfaces.OfType<TypeDefOrSpec>());
        var visited = new HashSet<TypeDefOrSpec>();
        while (pending.TryPop(out var current)) {
            if (!visited.Add(current)) continue;
            yield return current;
            foreach (var parent in current.Interfaces.OfType<TypeDefOrSpec>()) pending.Push(parent);
        }
    }

    static bool SameSignature(MethodDesc a, MethodDesc b)
        => a.Name == b.Name && a.IsStatic == b.IsStatic && a.ReturnSig == b.ReturnSig &&
           a.GenericParams.Count == b.GenericParams.Count &&
           a.ParamSig.Skip(a.IsInstance ? 1 : 0).SequenceEqual(b.ParamSig.Skip(b.IsInstance ? 1 : 0));
}
