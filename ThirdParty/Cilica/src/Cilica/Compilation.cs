namespace Cilica;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

using Cilica.Analysis;
using Cilica.CodeGen.Cil;
using MethodBody = IR.MethodBody;

public partial class Compilation
{
    public ModuleDef Module { get; }
    public ICompilationLogger Logger { get; }
    public CompilationSettings Settings { get; }

    public RuntimeLibrary Runtime { get; }

    public ModuleResolver Resolver => Module.Resolver;

    private TypeDef? _auxType;
    bool _ownsAuxType;
    readonly HashSet<MemberDesc> _initialAuxMembers = new();
    readonly Dictionary<ModuleDef, bool> _exceptionWrapping = new();

    readonly AnalysisCache<IGlobalAnalysis> _analyses = new();
    readonly HashSet<MethodDef> _failedMethods = new();

    public Compilation(ModuleDef module, ICompilationLogger logger, CompilationSettings settings)
    {
        Module = module;
        Logger = logger;
        Settings = settings;
        Runtime = new RuntimeLibrary(module.Resolver);
    }

    public A GetAnalysis<A>() where A : IGlobalAnalysis
        => (A)_analyses.Get(typeof(A), () => A.Create(this));

    public void InvalidateAnalyses(MethodDef method)
    {
        foreach (var analysis in _analyses.Values) {
            analysis.Invalidate(method);
        }
    }

    public bool HasFailed(MethodDef method) => _failedMethods.Contains(method);

    internal void BeginMethod(MethodDef method) => _failedMethods.Remove(method);

    internal void FailMethod(MethodDef method)
    {
        _failedMethods.Add(method);
        InvalidateAnalyses(method);
    }

    internal void EnsureReferencedMembersAccessible(MethodBody body)
    {
        // The runtime parses these attributes using CoreLib's own assembly-name
        // parser. Adding one to CoreLib can recursively enter that parser's JIT
        // compilation during startup. Inlining and constructor expansion use
        // CanInlineBody to avoid introducing references needing suppression.
        if (Module == Resolver.CoreLib) return;

        var visitedTypes = new HashSet<TypeDesc>();
        foreach (var inst in body.Instructions()) {
            VisitType(inst.ResultType);
            foreach (var operand in inst.Operands) VisitType(operand.ResultType);
            MemberDesc? member = inst switch {
                CallInst call => call.Method,
                NewObjInst allocation => allocation.Constructor,
                FuncAddrInst address => address.Method,
                FieldAddrInst field => field.Field,
                FieldExtractInst field => field.Field,
                FieldInsertInst field => field.Field,
                _ => null
            };
            if (member != null) VisitMember(member);

            if (inst is IntrinsicInst intrinsic) {
                foreach (var entity in intrinsic.StaticArgs) {
                    if (entity is TypeDesc type) VisitType(type);
                    else if (entity is MemberDesc referenced) VisitMember(referenced);
                }
            }
            if (inst is CallInst { Constraint: { } constraint }) VisitType(constraint);
            if (inst is FuncAddrInst { Constraint: { } addressConstraint }) VisitType(addressConstraint);
            if (inst is GuardInst { CatchType: { } catchType }) VisitType(catchType);
            if (inst is MemoryInst memory) VisitType(memory.ElemType);
        }

        void VisitMember(MemberDesc member)
        {
            VisitType(member.DeclaringType);
            if (member is MethodDesc method) {
                VisitSignature(method.ReturnSig);
                foreach (var parameter in method.ParamSig) VisitSignature(parameter);
                foreach (var argument in method.GenericParams) VisitType(argument);
            } else if (member is FieldDesc field) {
                VisitSignature(field.Sig);
            }
            if (member.DeclaringType is not TypeDefOrSpec owner) return;
            bool publicMember = member switch {
                MethodDesc m => m.IsPublic,
                FieldDesc f => (f.Attribs & FieldAttributes.FieldAccessMask) == FieldAttributes.Public,
                _ => false
            };
            if (!publicMember && owner != body.Definition.DeclaringType) EnsureMembersAccessible(owner.Module);
        }

        void VisitSignature(TypeSig signature)
        {
            VisitType(signature.Type);
            foreach (var modifier in signature.Modifiers) VisitType(modifier.Type);
        }

        void VisitType(TypeDesc type)
        {
            if (type is PrimType or GenericParamType || !visitedTypes.Add(type)) return;
            if (type is CompoundType compound) {
                VisitType(compound.ElemType);
                foreach (var modifier in compound.ElementModifiers) VisitType(modifier.Type);
            } else if (type is FuncPtrType pointer) {
                VisitSignature(pointer.Signature.ReturnType);
                foreach (var parameter in pointer.Signature.ParamTypes) VisitSignature(parameter);
            } else if (type is TypeDefOrSpec nominal) {
                foreach (var argument in nominal.GenericParams) VisitType(argument);
                for (var definition = nominal.Definition; definition != null; definition = definition.DeclaringType) {
                    var visibility = definition.Attribs & TypeAttributes.VisibilityMask;
                    if (visibility is TypeAttributes.Public or TypeAttributes.NestedPublic) continue;
                    bool assemblyAccess = visibility is TypeAttributes.NotPublic or TypeAttributes.NestedAssembly or TypeAttributes.NestedFamORAssem;
                    if (definition.Module != Module || !assemblyAccess && definition != body.Definition.DeclaringType) {
                        EnsureMembersAccessible(definition.Module);
                    }
                }
            }
        }
    }

    internal bool CanInlineBody(MethodDef caller, MethodDefOrSpec callee, bool scalarReplacement = false)
    {
        var body = Ensure.NotNull(callee.Definition.Body);
        if (TypeUtils.HasClassMethodImpls(callee) && !VirtualDispatch.SameMethod(VirtualDispatch.ResolveDirect(callee), callee)) return false;
        if (body.LocalVars().Any(slot => slot.Type is TypeDefOrSpec { IsValueType: false } type &&
            Passes.ScalarReplacement.HasFinalizer(type))) return false;
        bool differentExceptionPolicy = caller.Module != callee.Module && WrapsNonExceptions(caller.Module) != WrapsNonExceptions(callee.Module);
        // Tail calls return from their enclosing method, not an inlined
        // continuation (including constructor expansion).
        foreach (var inst in body.Instructions()) {
            if (inst is CallInst { IsTailCall: true } or CilIntrinsic.CallIndirect { IsTailCall: true }) return false;
            if (differentExceptionPolicy && inst is GuardInst or ThrowInst) return false;
            // An unmanaged calli can fail during JIT-time stub validation.
            // Keep that validation in its original callable frame.
            if (inst is CilIntrinsic.CallIndirect { FunctionType.Signature.CallConv: not CallConvention.Managed }) return false;
            // Finalizable allocations depend on their enclosing frame's lifetime,
            // including hidden temporaries introduced by the runtime JIT.
            if (inst is NewObjInst allocation && Passes.ScalarReplacement.HasFinalizer(allocation.ResultType)) return false;
            var called = inst switch { CallInst call => call.Method, NewObjInst obj => obj.Constructor, FuncAddrInst address => address.Method, _ => null };
            if (called != null && IsFrameSensitive(called)) return false;
        }
        return new InliningAccess(caller.DeclaringType, ignoreVisibility: Module != Resolver.CoreLib).CheckBody(body, scalarReplacement);
    }

    private bool WrapsNonExceptions(ModuleDef module)
    {
        if (!_exceptionWrapping.TryGetValue(module, out bool wraps)) {
            wraps = module.GetCustomAttribs(forAssembly: true).Find(typeof(RuntimeCompatibilityAttribute))
                ?.GetProperty(nameof(RuntimeCompatibilityAttribute.WrapNonExceptionThrows))?.Value is true;
            _exceptionWrapping.Add(module, wraps);
        }
        return wraps;
    }

    private static bool IsFrameSensitive(MethodDesc method)
    {
        if (method.Attribs.HasFlag(MethodAttributes.RequireSecObject)) return true;
        var type = method.DeclaringType;
        if (!type.IsCorelibType()) return false;
        return (type.Namespace, type.Name, method.Name) switch {
            ("System.Diagnostics", "StackTrace" or "StackFrame", ".ctor") => true,
            ("System.Reflection", "MethodBase", "GetCurrentMethod") => true,
            ("System.Reflection", "Assembly", "GetCallingAssembly" or "GetExecutingAssembly") => true,
            ("System", "Environment", "get_StackTrace") => true,
            _ => false
        };
    }

    /// <summary> Returns the compiler's auxiliary type for the current module. </summary>
    public TypeDef GetAuxType()
    {
        if (_auxType != null) {
            return _auxType;
        }
        const string name = "<>_Cilica_Aux";
        _auxType = Module.FindType(null, name);

        if (_auxType == null) {
            _ownsAuxType = true;
            _auxType = Module.CreateType(null, name, TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

            var attribCtor = Module.Resolver.Import(typeof(CompilerGeneratedAttribute)).FindMethod(".ctor");
            var typeAttribs = _auxType.GetCustomAttribs(readOnly: false);
            typeAttribs.Add(new CustomAttrib(attribCtor));
        } else {
            _initialAuxMembers.UnionWith(_auxType.Fields);
            _initialAuxMembers.UnionWith(_auxType.Methods);
        }

        return _auxType;
    }

    internal void RemoveUnusedArtifacts()
    {
        if (_auxType == null && _generatedInitializationHelpers.Count == 0) {
            return;
        }

        var live = new HashSet<MemberDesc>();
        var pending = new Queue<MethodDef>(Module.MethodDefs().Where(method =>
            method.DeclaringType != _auxType && !_generatedInitializationHelpers.Contains(method)));
        Mark(_moduleInitializationHelper);

        while (pending.TryDequeue(out var method)) {
            if (method.ILBody is { } il) {
                foreach (var inst in il.Instructions) {
                    Mark(inst.Operand as MemberDesc);
                }
            }

            if (method.Body is { } body)
            foreach (var inst in body.Instructions()) {
                Mark(inst switch {
                    CallInst call => call.Method,
                    NewObjInst allocation => allocation.Constructor,
                    FuncAddrInst address => address.Method,
                    FieldAddrInst field => field.Field,
                    FieldExtractInst field => field.Field,
                    FieldInsertInst field => field.Field,
                    _ => null
                });

                if (inst is IntrinsicInst intrinsic) {
                    foreach (var entity in intrinsic.StaticArgs) {
                        Mark(entity as MemberDesc);
                    }
                }
            }
        }

        foreach (var helper in _generatedInitializationHelpers.ToArray()) {
            if (live.Contains(helper)) continue;
            helper.DeclaringType.Methods.Remove(helper);
            _generatedInitializationHelpers.Remove(helper);
            _typeInitializationHelpers.Remove(helper.DeclaringType);
        }

        _auxType?.Fields.RemoveAll(field => !_initialAuxMembers.Contains(field) && !live.Contains(field));
        _auxType?.Methods.RemoveAll(method => !_initialAuxMembers.Contains(method) && !live.Contains(method));

        if (_ownsAuxType && _auxType!.Fields.Count == 0 && _auxType.Methods.Count == 0) {
            Module.RemoveType(_auxType);
            _auxType = null;
            _ownsAuxType = false;
        }

        void Mark(MemberDesc? member)
        {
            MemberDesc? definition = member switch {
                MethodDefOrSpec method => method.Definition,
                FieldDefOrSpec field => field.Definition,
                _ => null
            };

            if (definition == null ||
                definition.DeclaringType != _auxType && (definition is not MethodDef generated || !_generatedInitializationHelpers.Contains(generated)) ||
                !live.Add(definition)) {
                return;
            }

            if (definition is MethodDef helper) {
                pending.Enqueue(helper);
            }
        }
    }

    /// <summary> Copies <paramref name="data"/> into a static field RVA. </summary>
    public FieldDef CreateStaticRva(ReadOnlySpan<byte> data)
    {
        Ensure.That(data.Length < 1024 * 1024 * 4, "Cannot allocate RVA bigger than 4MB");

        string fieldName = "Data_" + Convert.ToBase64String(SHA256.HashData(data)).Replace('+', '_').Replace('/', '_').TrimEnd('=');
        var auxType = GetAuxType();

        if (auxType.FindField(fieldName, throwIfNotFound: false) is FieldDef field) {
            Ensure.That(
                data.SequenceEqual(field.MappedData.AsSpan(0, data.Length)),
                "Congratulations, you just found a hash collision! (or a serious bug)");
            return field;
        }
        var (blockType, alignedSize) = CreateRvaBlock(auxType, data.Length);

        var alignedData = new byte[alignedSize];
        data.CopyTo(alignedData);

        var attrs = FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly | FieldAttributes.HasFieldRVA;
        return auxType.CreateField(fieldName, blockType, attrs, mappedData: alignedData);
    }

    private (TypeDesc Type, int Size) CreateRvaBlock(TypeDef parentType, int size)
    {
        if (size <= 8) return (PrimType.UInt64, 8);

        size = (size + 15) & ~15; // align to multiples of 16 bytes (for no particular reason).
        string name = "Block" + size;

        var blockType = parentType.FindNestedType(name);
        Ensure.That(blockType == null || blockType.LayoutSize == size);

        if (blockType == null) {
            var attrs = TypeAttributes.NestedAssembly | TypeAttributes.ExplicitLayout;
            blockType = parentType.CreateNestedType(name, attrs, baseType: Resolver.SysTypes.ValueType);
            blockType.LayoutSize = size;
        }
        return (blockType, size);
    }

    HashSet<string> _assembliesToIgnoreAccessChecksFor = new();

    /// <summary> Adds an IgnoresAccessChecksToAttribute for the given module if there's not already one. </summary>
    /// <remarks> This ensures that <see cref="Module"/> can access any private member declared in the given module.  </remarks>
    public void EnsureMembersAccessible(ModuleDef module)
    {
        if (Module == Resolver.CoreLib) {
            throw new InvalidOperationException("CoreLib cannot use access-check suppression during runtime initialization");
        }
        string name = module.AsmName.Name!;

        if (_assembliesToIgnoreAccessChecksFor.Add(name)) {
            MarkIgnoreAccessChecksToAssembly(name);
        }
    }

    private MethodDesc? m_IgnoreAccessChecksToAttributeCtor;

    private void MarkIgnoreAccessChecksToAssembly(string assemblyName)
    {
        var asmAttribs = Module.GetCustomAttribs(forAssembly: true);

        if (m_IgnoreAccessChecksToAttributeCtor == null) {
            var attribType = Module.FindType("System.Runtime.CompilerServices", "IgnoresAccessChecksToAttribute");

            if (attribType == null) {
                attribType = Module.CreateType(
                    "System.Runtime.CompilerServices", "IgnoresAccessChecksToAttribute",
                    TypeAttributes.BeforeFieldInit,
                    Module.Resolver.CoreLib.FindType("System", "Attribute")
                );
                var attribCtor = attribType.CreateMethod(
                    ".ctor", PrimType.Void,
                    [new ParamDef(attribType, "this"), new ParamDef(PrimType.String, "assemblyName")],
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName
                );
                var assembler = new ILAssembler();
                assembler.Emit(ILCode.Ldarg_0);
                assembler.Emit(ILCode.Call, attribType.BaseType!.FindMethod(".ctor", new MethodSig(PrimType.Void, [])));
                assembler.Emit(ILCode.Ret);
                attribCtor.ILBody = assembler.Assemble(null);

                m_IgnoreAccessChecksToAttributeCtor = attribCtor;
            } else {
                m_IgnoreAccessChecksToAttributeCtor = attribType.FindMethod(".ctor", new MethodSig(PrimType.Void, [PrimType.String]));

                foreach (var attrib in asmAttribs) {
                    if (attrib.Constructor == m_IgnoreAccessChecksToAttributeCtor) {
                        _assembliesToIgnoreAccessChecksFor.Add((string)attrib.Args[0]);

                        if ((string)attrib.Args[0] == assemblyName) {
                            return;
                        }
                    }
                }
            }
        }

        asmAttribs.Add(new CustomAttrib(m_IgnoreAccessChecksToAttributeCtor, [assemblyName]));
    }
}

public class CompilationSettings
{
    public bool VerifyEachPass { get; init; }

    /// <summary> If set to true, the resulting module will have a fixed dependency on a little-endianess. </summary>
    public bool AssumeLittleEndian { get; init; } = true; // TODO: add module cctor check

    /// <summary> Whether to allow optimizations to inline or make assumptions about methods defined in other assemblies. </summary>
    public bool AllowCrossAssemblyIPO { get; init; } = true;

    /// <summary> Whether to inline method bodies and expand constructors, including scalar replacement. </summary>
    public bool EnableInlining { get; init; } = true;

    /// <summary> Caller IL size budget in bytes; larger callers may only inline without growing. </summary>
    public int MaxInlinedMethodILSize { get; init; } = 32 * 1024;
}
