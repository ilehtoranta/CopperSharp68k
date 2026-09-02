/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private bool TryEmitAllocatedFrameClearRuns(
		InternalCallAbi abi,
		M68kAllocatedFunction allocated,
		int[] clearDisplacements)
	{
		if (clearDisplacements.Length < 8 || _request.Cpu != M68kCpuTarget.M68000 ||
			_request.ClrPolicy == M68kClrPolicy.Always || UsesAllocatedFrameAnchor ||
			clearDisplacements.Any(static displacement => displacement is < 0 or > short.MaxValue)) return false;
		// Existing prologue scratch selection predates explicit reservations.
		// Keep cached-pointer and other reserved-register functions unchanged.
		if (!allocated.Function.ReservedRegisters.IsEmpty) return false;
		var hasCounter = TrySelectAllocatedFrameScratchRegister(abi, allocated,
			address: false, excluded: null, out var counter);
		var hasAddress = TrySelectAllocatedFrameScratchRegister(abi, allocated,
			address: true, excluded: null, out var address);
		var hasZero = TrySelectAllocatedFrameScratchRegister(abi, allocated,
			address: false, excluded: counter, out var zero);
		var loopKind = hasCounter && hasAddress && hasZero ? M68kFrameClearLoopKind.Scratch
			: hasAddress ? M68kFrameClearLoopKind.PreserveData : M68kFrameClearLoopKind.PreserveDataAndAddress;
		var unrolledZeroAvailable = TrySelectAllocatedFrameZeroRegister(abi, allocated, out var unrolledZero);
		var plan = M68kFrameClearRunPlanner.Create(clearDisplacements, unrolledZeroAvailable, loopKind);
		if (plan is null) return false;
		foreach (var run in plan.Runs)
		{
			var displacements = new ArraySegment<int>(clearDisplacements, run.Start, run.Count);
			if (run.Loop)
			{
				if (loopKind == M68kFrameClearLoopKind.Scratch)
					EmitAllocatedScratchFrameClear(displacements, counter, address, zero);
				else if (!TryEmitAllocatedPreservedScratchFrameClear(abi, allocated, displacements))
					throw new InvalidOperationException("A planned frame clear lost its scratch-register contract.");
			}
			else if (unrolledZeroAvailable)
			{
				_assembler.EmitWord((ushort)(0x7000 | ((int)unrolledZero << 9)));
				foreach (var displacement in displacements)
					EmitAllocatedFrameStore(unrolledZero, M68kMachineValueWidth.Long, displacement);
			}
			else
				foreach (var displacement in displacements) EmitAllocatedFrameClear(displacement);
		}
		return true;
	}

	private void EmitAllocatedScratchFrameClear(
		IReadOnlyList<int> clearDisplacements,
		M68kRegister counterRegister,
		M68kRegister addressRegister,
		M68kRegister zeroRegister)
	{
		EmitAllocatedFrameAddress(addressRegister, clearDisplacements[0], trackNonNull: false);
		_assembler.EmitWord((ushort)(0x7000 | ((int)zeroRegister << 9)));
		var remainder = clearDisplacements.Count % 4;
		for (var index = 0; index < remainder; index++)
			_assembler.EmitWord((ushort)(0x20C0 |
				(((int)addressRegister - (int)M68kRegister.A0) << 9) | (int)zeroRegister));
		EmitAllocatedImmediate((clearDisplacements.Count / 4) - 1, counterRegister);
		var loop = UniqueLabel("allocated-frame-zero-loop");
		_assembler.Mark(loop);
		for (var index = 0; index < 4; index++)
			_assembler.EmitWord((ushort)(0x20C0 |
				(((int)addressRegister - (int)M68kRegister.A0) << 9) | (int)zeroRegister));
		_assembler.EmitDbra((int)counterRegister, loop);
	}
}
