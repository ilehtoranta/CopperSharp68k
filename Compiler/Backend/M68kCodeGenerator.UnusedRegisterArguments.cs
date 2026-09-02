using System.Collections.Immutable;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
    // Formal ABI locations, stack arguments, return
    // buffers, call effects/clobbers, and argument-expression effects stay intact.
    private void ElideUnusedRegisterArguments(
        IReadOnlyList<CilMethod> methods,
        IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions)
    {
        if (_request.RomSizeOptimizations?.ElideUnusedRegisterArguments != true ||
            _request.Cpu != M68kCpuTarget.M68000 ||
            _request.RuntimeProfile != M68kRuntimeProfile.Rom ||
            _request.ExceptionMode != M68kExceptionMode.Yolo ||
            _memoryManagement != M68kMemoryManagement.None || _managedPoolRuntime is not null ||
            _managedLifecycles.Count != 0 || _usesExceptionRuntime)
            return;
        var rounds = 0;
        for (; rounds < 12; rounds++)
        {
            var summaries = new Dictionary<CilMethodIdentity, HashSet<M68kRegister>>();
            foreach (var method in methods)
            {
                if (!functions.TryGetValue(method.Identity, out var function) ||
                    !EligibleUnusedArgumentFunction(function) || method.IsImport ||
                    method.ImportName is not null || method.ExternalCall is not null ||
                    method.DeclaringTypeIsInterface || IsAlwaysInlinedMethod(method) ||
                    IsNativeShadowMathLeaf(method)) continue;
                var abi = GetInternalCallAbi(method);
                // Allocated emitter substitutions can index Uses directly.
                // Only ordinary native calls are admitted by this pass.
                if (abi.RegisterOnlyLocations is { } registers &&
                    TryGetInlineCandidate(method, registers, out _)) continue;
                var used = function.Blocks.SelectMany(b => b.Instructions)
                    .Where(i => i.ArgumentIndex is not null && i.Operation is
                        (M68kMachineOperation.Argument or M68kMachineOperation.ArgumentLoad or
                         M68kMachineOperation.ArgumentStore or M68kMachineOperation.ArgumentAddress))
                    .Select(i => i.ArgumentIndex!.Value).ToHashSet();
                var unused = abi.Arguments.Where(a => a.Register is not null &&
                    a.LowRegister is null && a.SlotLongs == 1 && !used.Contains(a.Index) &&
                    !function.ArgumentHomes.ContainsKey(a.Index))
                    .Select(a => a.Register!.Value).ToHashSet();
                if (unused.Count != 0) summaries.Add(method.Identity, unused);
            }
            var roundChanged = new HashSet<CilMethodIdentity>();
            foreach (var callerMethod in methods)
            {
                if (!functions.TryGetValue(callerMethod.Identity, out var function) ||
                    !EligibleUnusedArgumentFunction(function)) continue;
                var definitions = function.Blocks.SelectMany(b => b.Instructions)
                    .SelectMany(i => i.Definitions.Select(v => (Value: v, Instruction: i)))
                    .GroupBy(v => v.Value).ToDictionary(g => g.Key, g => g.Single().Instruction);
                foreach (var block in function.Blocks)
                for (var callIndex = 0; callIndex < block.Instructions.Count; callIndex++)
                {
                    var call = block.Instructions[callIndex];
                    if (call.Operation != M68kMachineOperation.Call || call.HasExplicitPlatformBase ||
                        call.StackVarargsRegister is not null || call.SourceInstruction?.OpCode != OpCodes.Call ||
                        call.LogicalCall is not {
                            DispatchKind: M68kMachineCallDispatchKind.Direct,
                            RequiresNullCheck: false, ResolvedTargets.Length: 1
                        } logical) continue;
                    var identity = logical.ResolvedTargets[0];
                    if (_foldedMethodAliases.TryGetValue(identity, out var folded)) identity = folded.Identity;
                    if (!summaries.TryGetValue(identity, out var unused)) continue;
                    var removable = call.Uses.Where(id =>
                        function.Values[id].PrecoloredRegister is { } register && unused.Contains(register) &&
                        definitions.TryGetValue(id, out var producer) &&
                        producer.Operation == M68kMachineOperation.Copy && producer.Uses.Length == 1 &&
                        producer.Definitions.Length == 1 && producer.IlOffset == call.IlOffset &&
                        producer.MemoryEffect == M68kMachineMemoryEffect.None && !producer.MayThrow &&
                        !producer.IsSafepoint && !producer.ProducesConditionCodes && !producer.ConsumesConditionCodes &&
                        !producer.TransportsManagedByrefOwner && !producer.RequiresLiveCallerFrame)
                        .ToHashSet();
                    if (removable.Count == 0) continue;
                    block.Instructions[callIndex] = call with {
                        Uses = call.Uses.Where(id => !removable.Contains(id)).ToImmutableArray()
                    };
                    // Logical operands retain their original positions and identities.
                    // Existing DCE may remove only unused pure producers; effectful
                    // argument evaluation, all pushes, and every cleanup remain.
                    roundChanged.Add(callerMethod.Identity);
                }
            }
            if (roundChanged.Count == 0) break;
            foreach (var identity in roundChanged)
            {
                M68kMachineIrVerifier.Verify(functions[identity]);
                functions[identity].OptimizationStatistics = M68kMachineOptimizer.Run(functions[identity], _request.Cpu);
                M68kMachineIrVerifier.Verify(functions[identity]);
            }
        }

    }

    private static bool EligibleUnusedArgumentFunction(M68kMachineFunction function) =>
        !function.HasExceptionHandlers && function.ExceptionRegions.Count == 0 &&
        !function.HasDynamicStackAllocation && !function.Values.Values.Any(v => v.IsGcReference) &&
        !function.LocalHomes.Values.Any(h => h.HasGcReferences) &&
        !function.ArgumentHomes.Values.Any(h => h.HasGcReferences);
}
