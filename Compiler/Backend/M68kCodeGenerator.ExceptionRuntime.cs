/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private const int RuntimeFrameHeaderLongs = 7;
	private const short RuntimeFramePreviousOffset = 0;
	private const short RuntimeFrameDescriptorOffset = 4;
	private const short RuntimeFrameBaseOffset = 8;
	private const short RuntimeFrameStateOffset = 12;
	private const short RuntimeFrameActiveExceptionOffset = 16;
	private const short RuntimeFramePendingActionOffset = 20;
	private const short RuntimeFrameLeaveContinuationOffset = 24;

	private readonly Dictionary<CilMethodIdentity, FrameLayout> _runtimeFrameLayouts = new();
	private readonly Dictionary<CilMethodIdentity, ImmutableArray<ExceptionRegionGroup>> _exceptionGroups = new();
	private readonly Dictionary<string, ExceptionState> _exceptionStates = new(StringComparer.Ordinal);
	private readonly Dictionary<string, NormalLeaveChain> _normalLeaveChains = new(StringComparer.Ordinal);
	private readonly HashSet<string> _runtimeTypeDescriptors = new(StringComparer.Ordinal);

	private bool UsesAmigaUnhandledExceptionRequester =>
		_usesExceptionRuntime &&
		_request.Imports.ContainsKey(M68kRuntimeImports.AmigaUnhandledExceptionRequester);

	private sealed record ExceptionRegionEntry(int Index, CilExceptionRegion Region);

	private sealed record ExceptionRegionGroup(
		int Id,
		int TryOffset,
		int TryEnd,
		ImmutableArray<ExceptionRegionEntry> Regions)
	{
		public int TryLength => TryEnd - TryOffset;
	}

	private sealed record ExceptionState(
		string Label,
		CilMethod Method,
		ImmutableArray<ExceptionRegionGroup> Groups);

	private sealed record NormalLeaveChain(
		string Key,
		CilMethod Method,
		int TargetOffset,
		ImmutableArray<CilExceptionRegion> FinallyRegions);

	private bool RequiresRuntimeFrame(CilMethod method) =>
		M68kCompiler.IsManagedRuntime(_request) ||
		(_usesExceptionRuntime && method.ExceptionRegions.Count != 0);

	private bool MethodMayRaiseException(CilMethod method)
	{
		if (method.ExceptionRegions.Count != 0)
		{
			return true;
		}

		foreach (var instruction in method.Instructions)
		{
			var op = instruction.OpCode;
			if (op == OpCodes.Throw ||
				op == OpCodes.Rethrow ||
				op == OpCodes.Div ||
				op == OpCodes.Div_Un ||
				op == OpCodes.Rem ||
				op == OpCodes.Rem_Un ||
				op == OpCodes.Newobj ||
				op == OpCodes.Newarr ||
				op == OpCodes.Ldlen ||
				op == OpCodes.Ldfld ||
				op == OpCodes.Ldflda ||
				op == OpCodes.Stfld ||
				IsArrayAccess(op) ||
				IsIndirectLoad(op) ||
				IsIndirectStore(op))
			{
				return true;
			}

			if ((op == OpCodes.Call || op == OpCodes.Callvirt) &&
				_module.ResolveMethodToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset).Definition?.ExternalCall?.Convention.ExceptionPolicy !=
					M68kExternalExceptionPolicy.None)
			{
				return true;
			}
		}

		return false;
	}

	private void RecordRuntimeFrameLayout(CilMethod method)
	{
		if (CurrentFrameLayout.HasRuntimeFrame)
		{
			_runtimeFrameLayouts[method.Identity] = CurrentFrameLayout;
		}
	}

	private void EmitLinkRuntimeFrame(CilMethod method)
	{
		if (!CurrentFrameLayout.HasRuntimeFrame)
		{
			return;
		}

		EmitStoreRegisterToFrame(M68kRegister.A5, RuntimeFramePreviousOffset);
		EmitAddressImmediateToFrame(
			RuntimeMethodDescriptorLabel(method),
			RuntimeFrameDescriptorOffset);
		_assembler.EmitWord(0x2F4F); // MOVE.L A7,d16(A7)
		_assembler.EmitWord(unchecked((ushort)RuntimeFrameBaseOffset));
		EmitImmediateToFrame(0, RuntimeFrameStateOffset);
		EmitImmediateToFrame(0, RuntimeFrameActiveExceptionOffset);
		EmitImmediateToFrame(0, RuntimeFramePendingActionOffset);
		EmitImmediateToFrame(0, RuntimeFrameLeaveContinuationOffset);
		_assembler.EmitWord(0x2A4F); // MOVEA.L A7,A5
	}

	private void EmitUnlinkRuntimeFrame()
	{
		if (!CurrentFrameLayout.HasRuntimeFrame)
		{
			return;
		}

		EmitLoadRuntimeFrameRegister(
			M68kRegister.A5,
			RuntimeFramePreviousOffset);
	}

	private void EmitProtectedInstructionState(CilMethod method, CilInstruction instruction)
	{
		if (method.ExceptionRegions.Count != 0)
		{
			var stateLabel = RegisterExceptionState(
				method,
				GetActiveExceptionGroups(method, instruction.Offset));
			if (stateLabel is null)
			{
				EmitRuntimeFrameImmediate(0, RuntimeFrameStateOffset);
			}
			else
			{
				EmitRuntimeFrameAddress(stateLabel, RuntimeFrameStateOffset);
			}
		}

		if (M68kCompiler.IsManagedRuntime(_request) &&
			InstructionMayReachGcSafepoint(instruction.OpCode))
		{
			EmitSyncRuntimeFrameRoots();
		}
	}

	private static bool InstructionMayReachGcSafepoint(OpCode op) =>
		op == OpCodes.Call ||
		op == OpCodes.Callvirt ||
		op == OpCodes.Newobj ||
		op == OpCodes.Newarr;

	private void EmitSyncRuntimeFrameRoots()
	{
		if (!CurrentFrameLayout.HasRuntimeFrame ||
			CurrentFrameLayout.GcScratchOffsets.Length == 0)
		{
			return;
		}

		var scratchIndex = 0;
		for (var stackIndex = 0; stackIndex < _currentStackTypes.Length; stackIndex++)
		{
			if (_currentStackTypes[stackIndex] != CilStackValueKind.Reference)
			{
				continue;
			}

			var offsetFromTop = 0;
			for (var index = stackIndex + 1; index < _currentStackTypes.Length; index++)
			{
				offsetFromTop += CilStackValueLayout.SlotBytes(_currentStackTypes[index]);
			}

			EmitLoadRegisterFromStack(M68kRegister.D0, offsetFromTop);
			EmitStoreRegisterToFrame(
				M68kRegister.D0,
				FrameDisplacement(
					CurrentFrameLayout.GcScratchOffsets[scratchIndex++],
					_currentStackDepth));
		}

		while (scratchIndex < CurrentFrameLayout.GcScratchOffsets.Length)
		{
			EmitStoreZeroToFrame(FrameDisplacement(
				CurrentFrameLayout.GcScratchOffsets[scratchIndex++],
				_currentStackDepth));
		}
	}

	private ImmutableArray<ExceptionRegionGroup> GetExceptionGroups(CilMethod method)
	{
		if (_exceptionGroups.TryGetValue(method.Identity, out var cached))
		{
			return cached;
		}

		var groups = method.ExceptionRegions
			.Select((region, index) => new ExceptionRegionEntry(index, region))
			.GroupBy(static entry => (entry.Region.TryOffset, entry.Region.TryEnd))
			.Select(group => new ExceptionRegionGroup(
				group.Min(static entry => entry.Index),
				group.Key.TryOffset,
				group.Key.TryEnd,
				group.OrderBy(static entry => entry.Index).ToImmutableArray()))
			.OrderBy(static group => group.Id)
			.ToImmutableArray();
		_exceptionGroups.Add(method.Identity, groups);
		return groups;
	}

	private ImmutableArray<ExceptionRegionGroup> GetActiveExceptionGroups(
		CilMethod method,
		int ilOffset) =>
		GetExceptionGroups(method)
			.Where(group => group.TryOffset <= ilOffset && ilOffset < group.TryEnd)
			.OrderBy(static group => group.TryLength)
			.ThenByDescending(static group => group.TryOffset)
			.ThenBy(static group => group.Id)
			.ToImmutableArray();

	private string? RegisterExceptionState(
		CilMethod method,
		ImmutableArray<ExceptionRegionGroup> groups)
	{
		if (groups.IsDefaultOrEmpty)
		{
			return null;
		}

		var token = MetadataTokens.GetToken(method.Handle);
		var key = $"{ModuleLabelPrefix(method.ModuleName)}{token:X8}:{string.Join(",", groups.Select(static group => group.Id))}";
		if (!_exceptionStates.TryGetValue(key, out var state))
		{
			state = new ExceptionState(
				$"generated:eh-state:{key}",
				method,
				groups);
			_exceptionStates.Add(key, state);
			RegisterExceptionState(method, groups.RemoveAt(0));
			foreach (var region in groups[0].Regions.Where(static entry => entry.Region.IsCatch))
			{
				RegisterRuntimeTypeDescriptor(region.Region.CatchType);
			}
		}

		return state.Label;
	}

	private bool TryEmitNormalLeave(CilMethod method, int leaveOffset, int targetOffset)
	{
		var finallyRegions = GetActiveExceptionGroups(method, leaveOffset)
			.Where(group =>
				!(group.TryOffset <= targetOffset && targetOffset < group.TryEnd))
			.SelectMany(static group => group.Regions)
			.Where(static entry => entry.Region.IsFinally)
			.Select(static entry => entry.Region)
			.ToImmutableArray();
		if (finallyRegions.IsDefaultOrEmpty)
		{
			return false;
		}

		var token = MetadataTokens.GetToken(method.Handle);
		var key = $"{ModuleLabelPrefix(method.ModuleName)}{token:X8}:{leaveOffset:X4}:{targetOffset:X4}";
		if (!_normalLeaveChains.TryGetValue(key, out var chain))
		{
			chain = new NormalLeaveChain(key, method, targetOffset, finallyRegions);
			_normalLeaveChains.Add(key, chain);
		}

		EmitRuntimeFrameImmediate(0, RuntimeFrameActiveExceptionOffset);
		EmitRuntimeFrameAddress(
			ControlFlowTargetLabel(method, targetOffset),
			RuntimeFrameLeaveContinuationOffset);
		EmitRuntimeFrameAddress(
			NormalLeaveNextActionLabel(chain, 1),
			RuntimeFramePendingActionOffset);
		EmitRestoreRuntimeFrameStack();
		_assembler.EmitJmp(
			ControlFlowTargetLabel(method, finallyRegions[0].HandlerOffset),
			external: false);
		return true;
	}

	private string NormalLeaveNextActionLabel(NormalLeaveChain chain, int index) =>
		index < chain.FinallyRegions.Length
			? $"generated:eh-leave:{chain.Key}:{index}"
			: RuntimeExceptionLeaveContinueLabel;

	private void EmitExceptionRuntime()
	{
		if (!_usesExceptionRuntime)
		{
			return;
		}

		EmitExceptionRaiseRuntime();
		EmitExceptionTypeMatchRuntime();
		EmitExceptionEndFinallyRuntime();
		EmitExceptionStateActions();
		EmitNormalLeaveActions();
		EmitAmigaUnhandledExceptionRequester();
	}

	private void EmitExceptionRaiseRuntime()
	{
		var haveException = UniqueLabel("eh_have_exception");
		var nullFault = UniqueLabel("eh_null_fault");
		var boundsFault = UniqueLabel("eh_bounds_fault");
		var divideFault = UniqueLabel("eh_divide_fault");
		var overflowFault = UniqueLabel("eh_overflow_fault");
		var outOfMemoryFault = UniqueLabel("eh_oom_fault");
		var systemFault = UniqueLabel("eh_system_fault");

		_assembler.AlignWord();
		_assembler.Mark(RuntimeExceptionRaiseLabel);
		EmitMoveRegister(M68kRegister.A0, M68kRegister.D1);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.NotEqual, haveException);
		EmitCompareImmediateLong(M68kRegister.D0, 0);
		_assembler.EmitBranch(M68kCondition.Equal, nullFault);
		EmitCompareImmediateLong(M68kRegister.D0, 1);
		_assembler.EmitBranch(M68kCondition.Equal, nullFault);
		EmitCompareImmediateLong(M68kRegister.D0, 2);
		_assembler.EmitBranch(M68kCondition.Equal, boundsFault);
		EmitCompareImmediateLong(M68kRegister.D0, 3);
		_assembler.EmitBranch(M68kCondition.Equal, divideFault);
		EmitCompareImmediateLong(M68kRegister.D0, 4);
		_assembler.EmitBranch(M68kCondition.Equal, overflowFault);
		EmitCompareImmediateLong(M68kRegister.D0, 6);
		_assembler.EmitBranch(M68kCondition.Equal, outOfMemoryFault);
		_assembler.EmitBranch(M68kCondition.True, systemFault);

		_assembler.Mark(nullFault);
		EmitRuntimeObjectAddress(M68kRegister.A0, "System.NullReferenceException");
		_assembler.EmitBranch(M68kCondition.True, haveException);
		_assembler.Mark(boundsFault);
		EmitRuntimeObjectAddress(M68kRegister.A0, "System.IndexOutOfRangeException");
		_assembler.EmitBranch(M68kCondition.True, haveException);
		_assembler.Mark(divideFault);
		EmitRuntimeObjectAddress(M68kRegister.A0, "System.DivideByZeroException");
		_assembler.EmitBranch(M68kCondition.True, haveException);
		_assembler.Mark(overflowFault);
		EmitRuntimeObjectAddress(M68kRegister.A0, "System.OverflowException");
		_assembler.EmitBranch(M68kCondition.True, haveException);
		_assembler.Mark(outOfMemoryFault);
		EmitRuntimeObjectAddress(M68kRegister.A0, "System.OutOfMemoryException");
		_assembler.EmitBranch(M68kCondition.True, haveException);
		_assembler.Mark(systemFault);
		EmitRuntimeObjectAddress(M68kRegister.A0, "System.Exception");

		_assembler.Mark(haveException);
		_assembler.Mark(RuntimeExceptionDispatchLabel);
		EmitMoveRegister(M68kRegister.A5, M68kRegister.D1);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.Equal, RuntimeExceptionUnhandledLabel);
		EmitStoreRuntimeFrameRegister(
			M68kRegister.A0,
			RuntimeFrameActiveExceptionOffset);
		EmitLoadRuntimeFrameRegister(
			M68kRegister.A1,
			RuntimeFrameStateOffset);
		EmitMoveRegister(M68kRegister.A1, M68kRegister.D1);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.NotEqual, RuntimeExceptionJumpStateLabel);

		_assembler.Mark(RuntimeExceptionUnwindFrameLabel);
		EmitRestoreRuntimeFrameStack();
		EmitLoadRuntimeFrameRegister(
			M68kRegister.A5,
			RuntimeFramePreviousOffset);
		_assembler.EmitBranch(M68kCondition.True, RuntimeExceptionDispatchLabel);

		_assembler.Mark(RuntimeExceptionJumpStateLabel);
		_assembler.EmitWord(0x4ED1); // JMP (A1)

		_assembler.Mark(RuntimeExceptionUnhandledLabel);
		EmitDetermineExceptionReason();
		if (_request.Imports.ContainsKey(M68kRuntimeImports.UnhandledException))
		{
			_assembler.EmitJsr(M68kRuntimeImports.UnhandledException, external: true);
		}
		if (UsesAmigaUnhandledExceptionRequester)
		{
			_assembler.EmitJmp(RuntimeAmigaRequesterLabel, external: false);
			return;
		}
		_assembler.EmitWord(0x4AFC); // ILLEGAL
	}

	private void EmitAmigaUnhandledExceptionRequester()
	{
		if (!UsesAmigaUnhandledExceptionRequester)
		{
			return;
		}

		var crash = UniqueLabel("amiga_unhandled_crash");

		_assembler.AlignWord();
		_assembler.Mark(RuntimeAmigaRequesterLabel);
		_assembler.EmitWord(0x2C78); // MOVEA.L 4.W,A6
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x43FA); // LEA intuition.library(PC),A1
		_assembler.EmitPcRelativeWord(RuntimeIntuitionNameLabel);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.EmitWord(0x4EAE); // JSR -552(A6)
		_assembler.EmitWord(unchecked((ushort)-552));
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, crash);
		_assembler.EmitWord(0x2C40); // MOVEA.L D0,A6

		_assembler.EmitWord(0x91C8); // SUBA.L A0,A0
		_assembler.EmitWord(0x43FA); // LEA body(PC),A1
		_assembler.EmitPcRelativeWord(RuntimeRequesterBodyLabel);
		_assembler.EmitWord(0x45FA); // LEA exit(PC),A2
		_assembler.EmitPcRelativeWord(RuntimeRequesterExitLabel);
		_assembler.EmitWord(0x47FA); // LEA freeze(PC),A3
		_assembler.EmitPcRelativeWord(RuntimeRequesterFreezeLabel);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.EmitWord(0x7200); // MOVEQ #0,D1
		_assembler.EmitWord(0x243C); // MOVE.L #320,D2
		_assembler.EmitLong(320);
		_assembler.EmitWord(0x7648); // MOVEQ #72,D3
		_assembler.EmitWord(0x4EAE); // JSR -348(A6)
		_assembler.EmitWord(unchecked((ushort)-348));

		_assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		_assembler.EmitWord(0x224E); // MOVEA.L A6,A1
		_assembler.EmitWord(0x2C78); // MOVEA.L 4.W,A6
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x4EAE); // JSR -414(A6)
		_assembler.EmitWord(unchecked((ushort)-414));
		_assembler.EmitWord(0x201F); // MOVE.L (A7)+,D0
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, crash);

		if (M68kCompiler.IsManagedRuntime(_request))
		{
			EmitRuntimeJsr(RuntimeShutdownTarget, M68kRuntimeImports.GcShutdown);
		}
		_assembler.EmitWord(0x2E79); // MOVEA.L initial-stack,A7
		_assembler.EmitAddress(RuntimeInitialStackLabel);
		_assembler.EmitWord(0x7014); // MOVEQ #20,D0
		_assembler.EmitWord(0x4E75); // RTS

		_assembler.Mark(crash);
		_assembler.EmitWord(0x4AFC); // ILLEGAL
	}

	private void EmitAmigaUnhandledExceptionRequesterData()
	{
		if (!UsesAmigaUnhandledExceptionRequester)
		{
			return;
		}

		_assembler.AlignWord();
		_assembler.Mark(RuntimeInitialStackLabel);
		_assembler.EmitLong(0);
		EmitIntuiText(
			RuntimeRequesterBodyLabel,
			RuntimeRequesterBodyTextLabel,
			left: 6,
			top: 4);
		EmitIntuiText(
			RuntimeRequesterExitLabel,
			RuntimeRequesterExitTextLabel,
			left: 0,
			top: 0);
		EmitIntuiText(
			RuntimeRequesterFreezeLabel,
			RuntimeRequesterFreezeTextLabel,
			left: 0,
			top: 0);
		EmitRuntimeCString(
			RuntimeIntuitionNameLabel,
			"intuition.library");
		EmitRuntimeCString(
			RuntimeRequesterBodyTextLabel,
			"Unhandled managed exception.");
		EmitRuntimeCString(RuntimeRequesterExitTextLabel, "Exit");
		EmitRuntimeCString(RuntimeRequesterFreezeTextLabel, "Freeze");
	}

	private void EmitIntuiText(string label, string textLabel, short left, short top)
	{
		_assembler.AlignWord();
		_assembler.Mark(label);
		_assembler.EmitWord(0x0100); // FrontPen=1, BackPen=0
		_assembler.EmitWord(0); // DrawMode=JAM1, alignment padding
		_assembler.EmitWord(unchecked((ushort)left));
		_assembler.EmitWord(unchecked((ushort)top));
		_assembler.EmitLong(0); // Default font.
		_assembler.EmitAddress(textLabel);
		_assembler.EmitLong(0); // No next IntuiText.
	}

	private void EmitRuntimeCString(string label, string value)
	{
		_assembler.AlignWord();
		_assembler.Mark(label);
		foreach (var character in value)
		{
			_assembler.EmitByte(checked((byte)character));
		}
		_assembler.EmitByte(0);
	}

	private void EmitExceptionTypeMatchRuntime()
	{
		var loop = UniqueLabel("eh_type_match_loop");
		var match = UniqueLabel("eh_type_match_yes");
		var noMatch = UniqueLabel("eh_type_match_no");

		_assembler.AlignWord();
		_assembler.Mark(RuntimeExceptionTypeMatchLabel);
		_assembler.EmitWord(0x2450); // MOVEA.L (A0),A2
		_assembler.Mark(loop);
		EmitMoveRegister(M68kRegister.A2, M68kRegister.D0);
		_assembler.EmitWord(0xB089); // CMP.L A1,D0
		_assembler.EmitBranch(M68kCondition.Equal, match);
		EmitMoveRegister(M68kRegister.A2, M68kRegister.D0);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, noMatch);
		_assembler.EmitWord(0x246A); // MOVEA.L 8(A2),A2
		_assembler.EmitWord(0x0008);
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(match);
		_assembler.EmitWord(0x7001); // MOVEQ #1,D0
		_assembler.EmitWord(0x4E75); // RTS
		_assembler.Mark(noMatch);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitExceptionEndFinallyRuntime()
	{
		var valid = UniqueLabel("eh_endfinally_valid");

		_assembler.AlignWord();
		_assembler.Mark(RuntimeExceptionEndFinallyLabel);
		EmitLoadRuntimeFrameRegister(
			M68kRegister.A1,
			RuntimeFramePendingActionOffset);
		EmitMoveRegister(M68kRegister.A1, M68kRegister.D0);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, valid);
		_assembler.EmitWord(0x4AFC); // ILLEGAL
		_assembler.Mark(valid);
		EmitLoadRuntimeFrameRegister(
			M68kRegister.A0,
			RuntimeFrameActiveExceptionOffset);
		_assembler.EmitWord(0x4ED1); // JMP (A1)

		_assembler.Mark(RuntimeExceptionLeaveContinueLabel);
		EmitLoadRuntimeFrameRegister(
			M68kRegister.A1,
			RuntimeFrameLeaveContinuationOffset);
		_assembler.EmitWord(0x4ED1); // JMP (A1)
	}

	private void EmitExceptionStateActions()
	{
		foreach (var state in _exceptionStates.Values.ToArray())
		{
			var group = state.Groups[0];
			var suffix = RegisterExceptionState(
				state.Method,
				state.Groups.RemoveAt(0));
			var nextAction = suffix ?? RuntimeExceptionUnwindFrameLabel;

			_assembler.AlignWord();
			_assembler.Mark(state.Label);
			foreach (var entry in group.Regions.Where(static item => item.Region.IsCatch))
			{
				var nextCatch = UniqueLabel("eh_next_catch");
				if (!entry.Region.CatchType.IsNil)
				{
					EmitRuntimeTypeAddress(
						M68kRegister.A1,
						entry.Region.CatchType);
					_assembler.EmitBsr(RuntimeExceptionTypeMatchLabel);
					_assembler.EmitWord(0x4A80); // TST.L D0
					_assembler.EmitBranch(M68kCondition.Equal, nextCatch);
				}

				EmitRuntimeFrameStateAddress(suffix);
				EmitRuntimeFrameImmediate(0, RuntimeFramePendingActionOffset);
				EmitRuntimeFrameImmediate(0, RuntimeFrameLeaveContinuationOffset);
				EmitRestoreRuntimeFrameStack();
				EmitLoadRuntimeFrameRegister(
					M68kRegister.A0,
					RuntimeFrameActiveExceptionOffset);
				EmitPushRegister(M68kRegister.A0);
				_assembler.EmitJmp(
					ControlFlowTargetLabel(state.Method, entry.Region.HandlerOffset),
					external: false);
				if (!entry.Region.CatchType.IsNil)
				{
					_assembler.Mark(nextCatch);
				}
			}

			var finallyRegion = group.Regions
				.Select(static entry => entry.Region)
				.FirstOrDefault(static region => region.IsFinally);
			if (finallyRegion is not null)
			{
				EmitRuntimeFrameStateAddress(suffix);
				EmitRuntimeFrameAddress(
					nextAction,
					RuntimeFramePendingActionOffset);
				EmitRestoreRuntimeFrameStack();
				_assembler.EmitJmp(
					ControlFlowTargetLabel(state.Method, finallyRegion.HandlerOffset),
					external: false);
				continue;
			}

			_assembler.EmitJmp(nextAction, external: false);
		}
	}

	private void EmitNormalLeaveActions()
	{
		foreach (var chain in _normalLeaveChains.Values)
		{
			for (var index = 1; index < chain.FinallyRegions.Length; index++)
			{
				_assembler.AlignWord();
				_assembler.Mark(NormalLeaveNextActionLabel(chain, index));
				EmitRuntimeFrameAddress(
					NormalLeaveNextActionLabel(chain, index + 1),
					RuntimeFramePendingActionOffset);
				EmitRestoreRuntimeFrameStack();
				_assembler.EmitJmp(
					ControlFlowTargetLabel(
						chain.Method,
						chain.FinallyRegions[index].HandlerOffset),
					external: false);
			}
		}
	}

	private void EmitRuntimeFrameStateAddress(string? label)
	{
		if (label is null)
		{
			EmitRuntimeFrameImmediate(0, RuntimeFrameStateOffset);
		}
		else
		{
			EmitRuntimeFrameAddress(label, RuntimeFrameStateOffset);
		}
	}

	private void EmitRestoreRuntimeFrameStack()
	{
		_assembler.EmitWord(0x2E6D); // MOVEA.L d16(A5),A7
		_assembler.EmitWord(unchecked((ushort)RuntimeFrameBaseOffset));
	}

	private void EmitRuntimeFrameImmediate(int value, short displacement)
	{
		_assembler.EmitWord(0x2B7C); // MOVE.L #value,d16(A5)
		_assembler.EmitLong(unchecked((uint)value));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitRuntimeFrameAddress(string label, short displacement)
	{
		_assembler.EmitWord(0x2B7C); // MOVE.L #label,d16(A5)
		_assembler.EmitAddress(label);
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitStoreRuntimeFrameRegister(
		M68kRegister register,
		short displacement)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2B40 | (int)register));
		}
		else
		{
			var index = (int)register - (int)M68kRegister.A0;
			_assembler.EmitWord((ushort)(0x2B48 | index));
		}
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitLoadRuntimeFrameRegister(
		M68kRegister register,
		short displacement)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x202D | ((int)register << 9)));
		}
		else
		{
			var index = (int)register - (int)M68kRegister.A0;
			_assembler.EmitWord((ushort)(0x206D | (index << 9)));
		}
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitRuntimeTypeAddress(M68kRegister register, string typeName)
	{
		RegisterRuntimeTypeDescriptor(typeName);
		var index = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x207C | (index << 9)));
		_assembler.EmitAddress(RuntimeTypeDescriptorLabel(typeName));
	}

	private void EmitRuntimeTypeAddress(M68kRegister register, EntityHandle handle)
	{
		RegisterRuntimeTypeDescriptor(handle);
		var index = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x207C | (index << 9)));
		_assembler.EmitAddress(TypeDescriptorLabel(handle));
	}

	private void EmitRuntimeObjectAddress(M68kRegister register, string typeName)
	{
		RegisterRuntimeTypeDescriptor(typeName);
		var index = (int)register - (int)M68kRegister.A0;
		_assembler.EmitWord((ushort)(0x207C | (index << 9)));
		_assembler.EmitAddress(RuntimeExceptionObjectLabel(typeName));
	}

	private void EmitCompareImmediateLong(M68kRegister register, int value)
	{
		if (register > M68kRegister.D7)
		{
			throw new ArgumentOutOfRangeException(nameof(register));
		}

		_assembler.EmitWord((ushort)(0x0C80 | (int)register)); // CMPI.L #value,Dn
		_assembler.EmitLong(unchecked((uint)value));
	}

	private void EmitDetermineExceptionReason()
	{
		var done = UniqueLabel("eh_reason_done");
		var mappings = new[]
		{
			("System.NullReferenceException", 1),
			("System.IndexOutOfRangeException", 2),
			("System.DivideByZeroException", 3),
			("System.OverflowException", 4),
			("System.Exception", 5),
			("System.OutOfMemoryException", 6)
		};

		foreach (var (typeName, reason) in mappings)
		{
			var next = UniqueLabel("eh_reason_next");
			EmitMoveRegister(M68kRegister.A0, M68kRegister.D0);
			_assembler.EmitWord(0xB0BC); // CMP.L #object,D0
			_assembler.EmitAddress(RuntimeExceptionObjectLabel(typeName));
			_assembler.EmitBranch(M68kCondition.NotEqual, next);
			EmitImmediateToRegister(M68kRegister.D0, reason);
			_assembler.EmitBranch(M68kCondition.True, done);
			_assembler.Mark(next);
		}
		EmitImmediateToRegister(M68kRegister.D0, 0);
		_assembler.Mark(done);
	}

	private void RegisterRuntimeTypeDescriptor(EntityHandle handle)
	{
		if (handle.IsNil)
		{
			return;
		}

		if (handle.Kind == HandleKind.TypeDefinition)
		{
			var layout = _module.GetTypeLayout((TypeDefinitionHandle)handle);
			_usedTypeLayouts.TryAdd(layout.Identity, layout);
			return;
		}

		RegisterRuntimeTypeDescriptor(_module.GetTypeDisplayName(handle));
	}

	private void RegisterRuntimeTypeDescriptor(string typeName)
	{
		_runtimeTypeDescriptors.Add(typeName);
		var baseType = RuntimeBaseTypeName(typeName);
		if (baseType is not null)
		{
			RegisterRuntimeTypeDescriptor(baseType);
		}
	}

	private void PrepareRuntimeTypeDescriptors(IReadOnlyList<CilMethod> methods)
	{
		if (_stringLiterals.Count != 0 || _arrayTypes.Count != 0)
		{
			RegisterRuntimeTypeDescriptor("System.Object");
		}

		if (_usesExceptionRuntime)
		{
			foreach (var typeName in new[]
			{
				"System.Exception",
				"System.SystemException",
				"System.ArithmeticException",
				"System.DivideByZeroException",
				"System.NullReferenceException",
				"System.IndexOutOfRangeException",
				"System.OverflowException",
				"System.OutOfMemoryException"
			})
			{
				RegisterRuntimeTypeDescriptor(typeName);
			}
		}

		foreach (var method in methods)
		{
			foreach (var region in method.ExceptionRegions.Where(static region => region.IsCatch))
			{
				RegisterRuntimeTypeDescriptor(region.CatchType);
			}
		}

		var pending = new Queue<CilTypeLayout>(_usedTypeLayouts.Values);
		while (pending.TryDequeue(out var layout))
		{
			var baseType = _module.GetBaseType(layout);
			if (baseType.Kind == HandleKind.TypeDefinition)
			{
				var baseHandle = (TypeDefinitionHandle)baseType;
				var baseLayout = _module.GetTypeLayout(layout, baseHandle);
				if (_usedTypeLayouts.TryAdd(baseLayout.Identity, baseLayout))
				{
					pending.Enqueue(baseLayout);
				}
			}
			else if (baseType.Kind == HandleKind.TypeReference)
			{
				RegisterRuntimeTypeDescriptor(_module.GetTypeDisplayName(baseType, layout));
			}
		}
	}

	private void EmitTypeDescriptorBase(CilTypeLayout layout)
	{
		var baseType = _module.GetBaseType(layout);
		if (baseType.IsNil)
		{
			_assembler.EmitLong(0);
		}
		else if (baseType.Kind == HandleKind.TypeDefinition)
		{
			_assembler.EmitAddress(TypeDescriptorLabel(
				_module.GetTypeLayout(layout, (TypeDefinitionHandle)baseType)));
		}
		else if (baseType.Kind == HandleKind.TypeReference)
		{
			_assembler.EmitAddress(RuntimeTypeDescriptorLabel(
				_module.GetTypeDisplayName(baseType, layout)));
		}
		else
		{
			_assembler.EmitLong(0);
		}
	}

	private void EmitRuntimeTypeDescriptorData()
	{
		foreach (var typeName in _runtimeTypeDescriptors.Order(StringComparer.Ordinal))
		{
			_assembler.AlignWord();
			_assembler.Mark(RuntimeTypeDescriptorLabel(typeName));
			_assembler.EmitLong(8);
			_assembler.EmitLong(0);
			var baseType = RuntimeBaseTypeName(typeName);
			if (baseType is null)
			{
				_assembler.EmitLong(0);
			}
			else
			{
				_assembler.EmitAddress(RuntimeTypeDescriptorLabel(baseType));
			}
			_assembler.EmitLong(0); // No compiler-managed vtable.
			_assembler.EmitLong(0); // No compiler-managed interface map.
		}

		if (!_usesExceptionRuntime)
		{
			return;
		}

		foreach (var typeName in new[]
		{
			"System.Exception",
			"System.DivideByZeroException",
			"System.NullReferenceException",
			"System.IndexOutOfRangeException",
			"System.OverflowException",
			"System.OutOfMemoryException"
		})
		{
			_assembler.AlignWord();
			_assembler.Mark(RuntimeExceptionObjectLabel(typeName));
			_assembler.EmitAddress(RuntimeTypeDescriptorLabel(typeName));
			_assembler.EmitLong(8);
		}
	}

	private static string? RuntimeBaseTypeName(string typeName) =>
		typeName switch
		{
			"System.Object" => null,
			"System.Exception" => "System.Object",
			"System.SystemException" => "System.Exception",
			"System.ArithmeticException" => "System.SystemException",
			"System.DivideByZeroException" => "System.ArithmeticException",
			"System.NullReferenceException" or
			"System.IndexOutOfRangeException" or
			"System.OverflowException" => "System.SystemException",
			"System.OutOfMemoryException" or
			"System.InvalidOperationException" => "System.SystemException",
			_ => "System.Exception"
		};

	private string RuntimeMethodDescriptorLabel(CilMethod method) =>
		$"runtime:method-descriptor:{ModuleLabelPrefix(method.ModuleName)}{MetadataTokens.GetToken(method.Handle):X8}";

	private static string RuntimeTypeDescriptorLabel(string typeName) =>
		$"runtime:type-descriptor:{typeName}";

	private string TypeDescriptorLabel(EntityHandle handle) =>
		handle.Kind == HandleKind.TypeDefinition
			? TypeDescriptorLabel((TypeDefinitionHandle)handle)
			: RuntimeTypeDescriptorLabel(_module.GetTypeDisplayName(handle));

	private static string RuntimeExceptionObjectLabel(string typeName) =>
		$"runtime:exception-object:{typeName}";

	private const string RuntimeExceptionRaiseLabel = "__c68k_exception_raise";
	private const string RuntimeExceptionDispatchLabel = "__c68k_exception_dispatch";
	private const string RuntimeExceptionJumpStateLabel = "__c68k_exception_jump_state";
	private const string RuntimeExceptionUnwindFrameLabel = "__c68k_exception_unwind_frame";
	private const string RuntimeExceptionUnhandledLabel = "__c68k_exception_unhandled";
	private const string RuntimeExceptionTypeMatchLabel = "__c68k_exception_type_match";
	private const string RuntimeExceptionEndFinallyLabel = "__c68k_exception_endfinally";
	private const string RuntimeExceptionLeaveContinueLabel = "__c68k_exception_leave_continue";
	private const string RuntimeAmigaRequesterLabel = "__c68k_amiga_unhandled_requester";
	private const string RuntimeInitialStackLabel = "runtime:amiga-initial-stack";
	private const string RuntimeIntuitionNameLabel = "runtime:amiga-intuition-name";
	private const string RuntimeRequesterBodyLabel = "runtime:amiga-requester-body";
	private const string RuntimeRequesterExitLabel = "runtime:amiga-requester-exit";
	private const string RuntimeRequesterFreezeLabel = "runtime:amiga-requester-freeze";
	private const string RuntimeRequesterBodyTextLabel = "runtime:amiga-requester-body-text";
	private const string RuntimeRequesterExitTextLabel = "runtime:amiga-requester-exit-text";
	private const string RuntimeRequesterFreezeTextLabel = "runtime:amiga-requester-freeze-text";
}
