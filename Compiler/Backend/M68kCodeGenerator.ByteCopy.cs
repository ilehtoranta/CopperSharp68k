/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
    private void EmitAllocatedForwardByteBlocks(string scalar, string success)
    {
        // Ordinary Span/Buffer memory, not volatile device accesses. A1/A2
        // are destination/source and D2 is the positive byte count. The
        // existing direction check has already selected a forward copy.
        _assembler.EmitWord(0x0C82); // CMPI.L #64,D2
        _assembler.EmitLong(64);
        _assembler.EmitBranch(M68kCondition.CarrySet, scalar);
        _assembler.EmitWord(0x0C82); // At most 65536 groups for DBRA.
        _assembler.EmitLong(64 * 65536);
        _assembler.EmitBranch(M68kCondition.Higher, scalar);
        EmitAllocatedMove(M68kRegister.A1, M68kRegister.D0, M68kMachineValueWidth.Long);
        EmitAllocatedMove(M68kRegister.A2, M68kRegister.D1, M68kMachineValueWidth.Long);
        _assembler.EmitWord(0x8081); // OR.L D1,D0
        _assembler.EmitWord(0x0800); // BTST #0,D0; longwords require even RAM.
        _assembler.EmitWord(0);
        _assembler.EmitBranch(M68kCondition.NotEqual, scalar);
        EmitAllocatedMove(M68kRegister.D2, M68kRegister.D0, M68kMachineValueWidth.Long);
        _assembler.EmitWord(0x0280); // ANDI.L #63,D0 (tail byte count)
        _assembler.EmitLong(63);
        EmitAllocatedMove(M68kRegister.D2, M68kRegister.D3, M68kMachineValueWidth.Long);
        _assembler.EmitWord(0xEC8B); // LSR.L #6,D3
        _assembler.EmitWord(0x5343); // SUBQ.W #1,D3
        var loop = UniqueLabel("allocated_byte_copy_block");
        _assembler.Mark(loop);
        for (var word = 0; word < 16; word++)
            _assembler.EmitWord(0x22DA); // MOVE.L (A2)+,(A1)+
        _assembler.EmitDbra(3, loop);
        EmitAllocatedMove(M68kRegister.D0, M68kRegister.D2, M68kMachineValueWidth.Long);
        EmitAllocatedTest(M68kRegister.D2, M68kMachineValueWidth.Long);
        _assembler.EmitBranch(M68kCondition.Equal, success);
        // Copy the exact remaining 1..63 bytes with the original scalar path.
        // No extra registers, stack saves, source reads or safepoints.
    }
}
