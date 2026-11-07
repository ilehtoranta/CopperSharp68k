namespace Cilica.Analysis;

/// <summary> Indexes potential writes between memory accesses and calls in a snapshot of a method. </summary>
/// <remarks> Removal-only traversals may reuse the snapshot while ignoring detached instructions. </remarks>
public sealed class MemoryAccessAnalysis : IMethodAnalysis
{
    readonly Dictionary<BasicBlock, Instruction[]> _writes = new();
    readonly Dictionary<Instruction, (int Before, int After)> _positions = new();

    public MemoryAccessAnalysis(MethodBody method)
    {
        foreach (var block in method) {
            List<Instruction>? writes = null;

            foreach (var instruction in block) {
                int before = writes?.Count ?? 0;

                // Exceptional edges leave from guards rather than from every instruction
                // in the protected region, including nested handlers that can mutate memory.
                if (instruction is GuardInst || instruction.MayWriteToMemory || instruction.IsMemoryBarrier) {
                    (writes ??= new()).Add(instruction);
                }

                if (instruction is MemoryInst or CallInst) {
                    _positions.Add(instruction, (before, writes?.Count ?? 0));
                }
            }

            _writes.Add(block, writes?.ToArray() ?? []);
        }
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager manager) => new MemoryAccessAnalysis(manager.Method);

    /// <summary> Gets potential writes strictly after and before the given memory accesses or calls. Null boundaries include the whole block. </summary>
    public ReadOnlySpan<Instruction> GetPotentialWrites(BasicBlock block, Instruction? after = null, Instruction? before = null)
    {
        var writes = _writes[block];
        int start = after == null ? 0 : _positions[after].After;
        int end = before == null ? writes.Length : _positions[before].Before;

        return writes.AsSpan(start, end - start);
    }
}
