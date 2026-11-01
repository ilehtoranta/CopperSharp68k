using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
    private bool TryEmitScalarExportAdapter(CilExport export, bool initializesPlatformBase)
    {
        if (initializesPlatformBase || UsesResidentInvocationContext || _usesExceptionRuntime ||
            _request.ExceptionMode != M68kExceptionMode.Yolo || _memoryManagement != M68kMemoryManagement.None ||
            export.Method.Signature.Header.IsInstance || _rootOnlyMethods.Contains(export.Method.Identity) ||
            _foldedMethodAliases.ContainsKey(export.Method.Identity) ||
            Is64BitScalar(export.Method.Signature.ReturnType)) return false;
        var abi = GetInternalCallAbi(export.Method);
        if (abi.ReturnBufferStackOffset is not null || abi.Arguments.Length != export.ParameterRegisters.Count ||
            abi.Arguments.Any(a => a.SlotLongs != 1 || a.LowRegister is not null ||
                a.Register is { } r && r is not (M68kRegister.D0 or M68kRegister.D1 or M68kRegister.A0 or M68kRegister.A1)))
            return false;

        // Private bodies preserve D2-D7/A2-A6. This adapter changes only private
        // volatile argument registers, so a second callee-save frame is redundant.
        // Save overflow arguments before any register permutation can overwrite them.
        foreach (var argument in abi.Arguments.Where(a => a.IsStack).OrderByDescending(a => a.StackOffset))
            EmitPushRegister(export.ParameterRegisters[argument.Index]);
        var moves = abi.Arguments.Where(a => !a.IsStack)
            .Select(a => (Source: export.ParameterRegisters[a.Index], Destination: a.Register!.Value))
            .Where(m => m.Source != m.Destination).ToList();
        EmitParallelExportMoves(moves);
        _assembler.EmitCall(MethodLabel(export.Method));
        EmitReleaseStackBytes(abi.StackBytes);
        if (!export.Method.Signature.ReturnType.IsVoid)
        {
            if (IsInternalAddressReturn(export.Method.Signature.ReturnType))
                EmitMoveRegister(M68kRegister.A0, export.ReturnRegister);
            else EmitMoveReturnFromD0(export.ReturnRegister);
        }
        _assembler.EmitWord(0x4E75);
        return true;
    }

    private void EmitParallelExportMoves(List<(M68kRegister Source, M68kRegister Destination)> moves)
    {
        while (moves.Count != 0)
        {
            int ready = moves.FindIndex(m => !moves.Any(other => other.Source == m.Destination));
            if (ready >= 0)
            {
                var move = moves[ready]; moves.RemoveAt(ready);
                EmitMoveRegister(move.Source, move.Destination);
                continue;
            }
            // A register cycle needs one saved value. Stack staging is bounded
            // to that cycle and is removed before calling the managed body.
            var cycle = moves[0]; moves.RemoveAt(0);
            EmitPushRegister(cycle.Source);
            EmitParallelExportMoves(moves);
            EmitPopRegister(cycle.Destination);
        }
    }
}
