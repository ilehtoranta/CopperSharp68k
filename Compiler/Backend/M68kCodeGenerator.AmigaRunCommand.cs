/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private const string AmigaRunCommandOnStackImport =
		"intrinsic:amiga-run-command-on-stack";

	private void ValidateAmigaRunCommandOnStack(CilMethod method)
	{
		bool IsWord(CilType type) =>
			!type.IsReference && !type.IsFloatingPoint &&
			(type.IsSupportedScalar && type.Size == 4 ||
			 _module.IsTransparentScalarType(type));

		if (method.Signature.Header.IsInstance ||
			method.Signature.ParameterTypes.Length != 4 ||
			!method.Signature.ParameterTypes.All(IsWord) ||
			!IsWord(method.Signature.ReturnType) ||
			method.ImportAbi is not { } abi ||
			abi.ParameterRegisters.Count != 4 ||
			abi.ParameterRegisters[0] != M68kRegister.A0 ||
			abi.ParameterRegisters[1] != M68kRegister.A3 ||
			abi.ParameterRegisters[2] != M68kRegister.D0 ||
			abi.ParameterRegisters[3] != M68kRegister.A1 ||
			abi.ReturnRegister != M68kRegister.D0)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedSignature,
				"The Amiga command stack bridge requires four native word arguments " +
				"in A0/A3/D0/A1 and a native word result in D0.",
				method.DisplayName);
		}
	}

	private void EmitAmigaRunCommandOnStack()
	{
		ReadOnlySpan<M68kRegister> preserved = stackalloc[]
		{
			M68kRegister.D1, M68kRegister.D2, M68kRegister.D3,
			M68kRegister.D4, M68kRegister.D5, M68kRegister.D6, M68kRegister.D7,
			M68kRegister.A0, M68kRegister.A1, M68kRegister.A2,
			M68kRegister.A3, M68kRegister.A4, M68kRegister.A5, M68kRegister.A6
		};
		EmitPushRegisters(preserved);
		_assembler.EmitWord(0x2400); // MOVE.L D0,D2: length across Exec StackSwap
		_assembler.EmitWord(0x2449); // MOVEA.L A1,A2: arguments
		_assembler.EmitWord(0x284B); // MOVEA.L A3,A4: entry
		_assembler.EmitWord(0x2A48); // MOVEA.L A0,A5: StackSwapStruct

		// Keep the only post-command recovery pointer on the NEW stack. The
		// original NDK command-entry contract preserves only SP, not A5 or A6.
		_assembler.EmitWord(0x206D); // MOVEA.L 8(A5),A0
		_assembler.EmitWord(8);
		_assembler.EmitWord(0x5988); // SUBQ.L #4,A0
		_assembler.EmitWord(0x208D); // MOVE.L A5,(A0)
		_assembler.EmitWord(0x2B48); // MOVE.L A0,8(A5)
		_assembler.EmitWord(8);
		_assembler.EmitWord(0x204D); // MOVEA.L A5,A0
		EmitAmigaRunCommandStackSwap();

		// No access to the old C# frame is allowed until the second swap.
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
		_assembler.EmitWord(0x204A); // MOVEA.L A2,A0
		_assembler.EmitWord(0x264C); // MOVEA.L A4,A3
		var commandCall = _assembler.Offset;
		_assembler.EmitWord(0x4E93); // JSR (A3)
		_assembler.SetInstructionEffects(commandCall, new M68kInstructionEffects(
			0x01, 0xFF, 0x89, 0x7F,
			M68kConditionCodeSet.None, M68kConditionCodeSet.All,
			M68kMemorySet.All, M68kMemorySet.All, 0, true, false));

		// Recover through SP before trusting ANY register left by the command.
		// Popping the control word also restores the original new-stack pointer
		// that the second StackSwap writes back into the caller's descriptor.
		_assembler.EmitWord(0x245F); // MOVEA.L (A7)+,A2
		_assembler.EmitWord(0x2400); // MOVE.L D0,D2: result across Exec StackSwap
		_assembler.EmitWord(0x204A); // MOVEA.L A2,A0
		EmitAmigaRunCommandStackSwap();
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
		EmitPopRegisters(preserved);
		_loadedPlatformBase = null;
	}

	private void EmitAmigaRunCommandStackSwap()
	{
		// Reload the public Exec base. Neither command register values nor an
		// invocation-global library-base slot are needed to recover the caller.
		_assembler.EmitWord(0x2C78); // MOVEA.L $4.W,A6
		_assembler.EmitWord(4);
		var callOffset = _assembler.Offset;
		EmitBaseRelativeJsr(M68kRegister.A6, -732); // Exec StackSwap, V37
		_assembler.SetInstructionEffects(callOffset, new M68kInstructionEffects(
			0, 0x03, 0xC1, 0x83,
			M68kConditionCodeSet.None, M68kConditionCodeSet.All,
			M68kMemorySet.All, M68kMemorySet.All, null, true, false));
	}
}
