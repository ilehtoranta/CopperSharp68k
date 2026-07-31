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
	private const short RuntimeFrameActiveExceptionOffset = 0;
	private const short RuntimeFramePendingActionOffset = 4;
	private const short RuntimeFrameLeaveContinuationOffset = 8;
	private const int RuntimeFrameHeaderLongs = 0;
	private const short RuntimeFramePreviousOffset = 0;
	private const short RuntimeFrameDescriptorOffset = 0;
	private const short RuntimeFrameBaseOffset = 0;
	private const short RuntimeFrameStateOffset = 0;
	private const int UnwindSiteEntryBytes = 20;

	private sealed record UnwindMethodLayout(
		CilMethod Method,
		int FrameBytes,
		ImmutableArray<M68kRegister> CalleeSavedRegisters,
		ImmutableArray<int> RootOffsets);

	private sealed record UnwindSite(
		CilMethod Method,
		string ResumeLabel,
		int StackAdjustment,
		string? ExceptionStateLabel,
		ImmutableArray<int> RootOffsets);

	private readonly Dictionary<CilMethodIdentity, UnwindMethodLayout> _unwindMethodLayouts = new();
	private readonly List<UnwindSite> _unwindSites = new();
	private CilMethod? _emittingUnwindMethod;
	private M68kAllocatedFunction? _emittingAllocatedFunction;
	private M68kMachineInstruction? _emittingMachineInstruction;
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

	private sealed record ExceptionResumeAction(
		CilMethod Method,
		string? StateLabel,
		string Label);

	private readonly Dictionary<string, ExceptionResumeAction> _exceptionResumeActions = new(StringComparer.Ordinal);

	private bool RequiresRuntimeFrame(CilMethod method) => false;

	private bool MethodMayRaiseException(CilMethod method) =>
		MethodMayRaiseException(method, new HashSet<CilMethodIdentity>());

	private bool MethodMayRaiseException(
		CilMethod method,
		HashSet<CilMethodIdentity> visiting)
	{
		if (!visiting.Add(method.Identity))
		{
			return false;
		}

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

			if (op != OpCodes.Call && op != OpCodes.Callvirt)
			{
				continue;
			}

			var target = _module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			if (target.Definition?.ExternalCall is { } externalCall)
			{
				if (externalCall.Convention.ExceptionPolicy != M68kExternalExceptionPolicy.None)
				{
					return true;
				}
				continue;
			}

			if (op == OpCodes.Callvirt)
			{
				// The receiver check and any closed-world override can raise. Keep a
				// runtime frame so unwinding restores this method's callee-saved state.
				return true;
			}

			if (target.Definition is { IsImport: false } callee &&
				MethodMayRaiseException(callee, visiting))
			{
				return true;
			}
		}

		return false;
	}

	private void RecordRuntimeFrameLayout(CilMethod method)
	{
		// Runtime layout is recorded after register allocation by RecordUnwindLayout.
	}

	private void RecordUnwindLayout(
		CilMethod method,
		InternalCallAbi abi,
		M68kAllocatedFunction allocated)
	{
		var roots = allocated.Frame.GcHomeOffsets
			.Concat(Enumerable.Range(0, method.ParameterCount)
				.Where(index => abi.Arguments[index].IsGcReference && abi.Arguments[index].IsStack)
				.Select(index => checked(
					allocated.Frame.FrameBytes +
					(allocated.Frame.CalleeSavedRegisters.Count * 4) +
					4 +
					abi.Arguments[index].StackOffset)))
			.Distinct()
			.Order()
			.ToImmutableArray();
		_unwindMethodLayouts[method.Identity] = new UnwindMethodLayout(
			method,
			allocated.Frame.FrameBytes,
			allocated.Frame.CalleeSavedRegisters.ToImmutableArray(),
			roots);
	}

	private void RegisterCurrentUnwindSite(
		bool exception,
		bool gc,
		int additionalStackBytes = 0)
	{
		if (_emittingUnwindMethod is not { } method ||
			_emittingAllocatedFunction is not { } allocated ||
			_emittingMachineInstruction is not { } instruction ||
			(!exception && !gc))
		{
			return;
		}

		var label = UniqueLabel("unwind_site");
		_assembler.Mark(label);
		var state = exception
			? RegisterExceptionState(
				method,
				GetActiveExceptionGroups(method, instruction.IlOffset))
			: null;
		var roots = gc
			? GetSafepointRootOffsets(method, allocated, instruction)
			: ImmutableArray<int>.Empty;
		_unwindSites.Add(new UnwindSite(
			method,
			label,
			checked(4 + _allocatedOutgoingStackBytes + additionalStackBytes),
			state,
			roots));
	}

	private ImmutableArray<int> GetSafepointRootOffsets(
		CilMethod method,
		M68kAllocatedFunction allocated,
		M68kMachineInstruction instruction)
	{
		var roots = new HashSet<int>(_unwindMethodLayouts[method.Identity].RootOffsets);
		var safepoint = allocated.Safepoints.Safepoints.FirstOrDefault(
			item => item.InstructionId == instruction.Id);
		if (safepoint is not null)
		{
			foreach (var value in safepoint.LiveReferences)
			{
				if (allocated.Safepoints.RootSlotByValue.TryGetValue(value, out var slot) &&
					allocated.Frame.RootOffsets.TryGetValue(slot, out var offset))
				{
					roots.Add(offset);
				}
			}
			foreach (var slot in safepoint.LiveSpillRootSlots)
			{
				if (allocated.Frame.SpillOffsets.TryGetValue(slot, out var offset))
				{
					roots.Add(offset);
				}
			}
		}
		if (allocated.Frame.ActiveExceptionOffset is { } exceptionOffset &&
			method.ExceptionRegions.Any(region =>
				region.HandlerOffset <= instruction.IlOffset &&
				instruction.IlOffset < region.HandlerEnd))
		{
			roots.Add(exceptionOffset);
		}
		return roots.Order().ToImmutableArray();
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

	private void EmitProtectedInstructionState(
		CilMethod method,
		CilInstruction instruction,
		bool forceExceptionState,
		ref string? emittedExceptionStateLabel)
	{
		if (method.ExceptionRegions.Count != 0)
		{
			var stateLabel = RegisterExceptionState(
				method,
				GetActiveExceptionGroups(method, instruction.Offset));
			if (forceExceptionState ||
				!StringComparer.Ordinal.Equals(stateLabel, emittedExceptionStateLabel))
			{
				if (stateLabel is null)
				{
					EmitRuntimeFrameImmediate(0, RuntimeFrameStateOffset);
				}
				else
				{
					EmitRuntimeFrameAddress(stateLabel, RuntimeFrameStateOffset);
				}

				emittedExceptionStateLabel = stateLabel;
			}
		}

		if (M68kCompiler.IsManagedRuntime(_request) &&
			InstructionMayReachGcSafepoint(method, instruction))
		{
			EmitSyncRuntimeFrameRoots();
		}
	}

	private bool InstructionMayReachGcSafepoint(
		CilMethod method,
		CilInstruction instruction)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Newobj || op == OpCodes.Newarr)
		{
			return true;
		}

		if (op != OpCodes.Call && op != OpCodes.Callvirt)
		{
			return false;
		}

		var target = _module.ResolveMethodToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (target.ImportName is M68kRuntimeImports.Allocate or
			M68kRuntimeImports.GcCollect or
			"intrinsic:runtime-gc-collect")
		{
			return true;
		}

		if (target.Definition is not { IsImport: false } definition)
		{
			// Platform calls and compiler intrinsics cannot re-enter the managed
			// allocator unless they are one of the runtime operations above.
			return false;
		}

		if (definition.DeclaringTypeIsInterface)
		{
			return _module.GetInterfaceImplementations(definition)
				.Any(callee => MethodMayReachGcSafepoint(
					callee,
					new HashSet<CilMethodIdentity>()));
		}

		if (RequiresVirtualDispatch(instruction, definition))
		{
			return _module.GetVirtualImplementations(definition)
				.Any(callee => MethodMayReachGcSafepoint(
					callee,
					new HashSet<CilMethodIdentity>()));
		}

		return MethodMayReachGcSafepoint(
			definition,
			new HashSet<CilMethodIdentity>());
	}

	private bool MethodMayReachGcSafepoint(
		CilMethod method,
		HashSet<CilMethodIdentity> visiting)
	{
		if (!visiting.Add(method.Identity))
		{
			return false;
		}

		foreach (var instruction in method.Instructions)
		{
			var op = instruction.OpCode;
			if (op == OpCodes.Newobj || op == OpCodes.Newarr)
			{
				return true;
			}

			if (op != OpCodes.Call && op != OpCodes.Callvirt)
			{
				continue;
			}

			var target = _module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			if (target.ImportName is M68kRuntimeImports.Allocate or
				M68kRuntimeImports.GcCollect or
				"intrinsic:runtime-gc-collect")
			{
				return true;
			}

			if (target.Definition is not { IsImport: false } definition)
			{
				continue;
			}

			if (definition.DeclaringTypeIsInterface)
			{
				if (_module.GetInterfaceImplementations(definition)
					.Any(callee => MethodMayReachGcSafepoint(callee, visiting)))
				{
					return true;
				}
				continue;
			}

			if (RequiresVirtualDispatch(instruction, definition))
			{
				if (_module.GetVirtualImplementations(definition)
					.Any(callee => MethodMayReachGcSafepoint(callee, visiting)))
				{
					return true;
				}
				continue;
			}

			if (MethodMayReachGcSafepoint(definition, visiting))
			{
				return true;
			}
		}

		visiting.Remove(method.Identity);
		return false;
	}

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

		EmitEhFrameImmediate(0, RuntimeFrameActiveExceptionOffset);
		EmitEhFrameAddress(
			ControlFlowTargetLabel(method, targetOffset),
			RuntimeFrameLeaveContinuationOffset);
		EmitEhFrameAddress(
			NormalLeaveNextActionLabel(chain, 1),
			RuntimeFramePendingActionOffset);
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
		EmitCreateExceptionCursor(fromResumeAddress: true);
		_assembler.EmitBranch(M68kCondition.True, RuntimeExceptionDispatchLabel);

		_assembler.AlignWord();
		_assembler.Mark(RuntimeExceptionResumeLabel);
		EmitCreateExceptionCursor(fromResumeAddress: false);
		EmitLoadExceptionContextRegister(M68kRegister.A1, ExceptionContextStateOffset);
		EmitMoveRegister(M68kRegister.A1, M68kRegister.D0);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, RuntimeExceptionJumpStateLabel);
		_assembler.EmitBranch(M68kCondition.True, RuntimeExceptionUnwindFrameLabel);

		_assembler.Mark(RuntimeExceptionDispatchLabel);
		EmitLoadExceptionContextRegister(M68kRegister.A1, ExceptionContextCursorOffset);
		_assembler.EmitWord(0x2211); // MOVE.L (A1),D1 resume PC
		_assembler.EmitBsr(RuntimeFindUnwindSiteLabel);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, RuntimeExceptionUnhandledLabel);
		EmitStoreExceptionContextRegister(M68kRegister.A1, ExceptionContextSiteOffset);
		_assembler.EmitWord(0x2069); // MOVEA.L 4(A1),A0 descriptor
		_assembler.EmitWord(0x0004);
		EmitStoreExceptionContextRegister(M68kRegister.A0, ExceptionContextDescriptorOffset);
		_assembler.EmitWord(0x2029); // MOVE.L 12(A1),D0 stack adjustment
		_assembler.EmitWord(0x000C);
		EmitLoadExceptionContextRegister(M68kRegister.A0, ExceptionContextCursorOffset);
		_assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
		EmitStoreExceptionContextRegister(M68kRegister.A0, ExceptionContextFrameBaseOffset);
		_assembler.EmitWord(0x2269); // MOVEA.L 8(A1),A1 state
		_assembler.EmitWord(0x0008);
		EmitStoreExceptionContextRegister(M68kRegister.A1, ExceptionContextStateOffset);
		EmitMoveRegister(M68kRegister.A1, M68kRegister.D0);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, RuntimeExceptionJumpStateLabel);

		_assembler.Mark(RuntimeExceptionUnwindFrameLabel);
		EmitLoadExceptionContextRegister(M68kRegister.A0, ExceptionContextDescriptorOffset);
		EmitLoadExceptionContextRegister(M68kRegister.A1, ExceptionContextFrameBaseOffset);
		_assembler.EmitWord(0x2068); // MOVEA.L 8(A0),A0 unwind thunk
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0x220E); // MOVE.L A6,D1 context pointer
		_assembler.EmitWord(0x4E90); // JSR (A0)
		_assembler.EmitWord(0x2C41); // MOVEA.L D1,A6
		EmitLoadExceptionContextRegister(M68kRegister.A1, ExceptionContextFrameBaseOffset);
		EmitLoadExceptionContextRegister(M68kRegister.A0, ExceptionContextDescriptorOffset);
		_assembler.EmitWord(0x2010); // MOVE.L (A0),D0 frame bytes
		_assembler.EmitWord(0xD3C0); // ADDA.L D0,A1
		_assembler.EmitWord(0x2028); // MOVE.L 4(A0),D0 saved bytes
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0xD3C0); // ADDA.L D0,A1
		EmitStoreExceptionContextRegister(M68kRegister.A1, ExceptionContextCursorOffset);
		_assembler.EmitBranch(M68kCondition.True, RuntimeExceptionDispatchLabel);

		_assembler.Mark(RuntimeExceptionJumpStateLabel);
		_assembler.EmitWord(0x4ED1); // JMP (A1)

		EmitFindUnwindSiteRuntime();

		_assembler.Mark(RuntimeExceptionUnhandledLabel);
		EmitLoadExceptionContextRegister(M68kRegister.A0, ExceptionContextExceptionOffset);
		EmitRestoreExceptionCursorRegisters();
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

	private void EmitCreateExceptionCursor(bool fromResumeAddress)
	{
		ReadOnlySpan<M68kRegister> preserved = stackalloc[]
		{
			M68kRegister.D2,
			M68kRegister.D3,
			M68kRegister.D4,
			M68kRegister.D5,
			M68kRegister.D6,
			M68kRegister.D7,
			M68kRegister.A2,
			M68kRegister.A3,
			M68kRegister.A4,
			M68kRegister.A5,
			M68kRegister.A6
		};
		EmitPushRegisters(preserved);
		EmitAllocateFrame(ExceptionContextBytes);
		_assembler.EmitWord(0x2C4F); // MOVEA.L A7,A6 context
		EmitStoreExceptionContextRegister(M68kRegister.A0, ExceptionContextExceptionOffset);
		_assembler.EmitWord(0x43EE); // LEA saved-context-end(A6),A1
		_assembler.EmitWord(checked((ushort)(ExceptionContextBytes + (preserved.Length * 4))));
		if (fromResumeAddress)
		{
			EmitStoreExceptionContextRegister(M68kRegister.A1, ExceptionContextCursorOffset);
			return;
		}
		EmitStoreExceptionContextRegister(M68kRegister.A1, ExceptionContextFrameBaseOffset);
		EmitStoreExceptionContextRegister(M68kRegister.D0, ExceptionContextDescriptorOffset);
		EmitStoreExceptionContextRegister(M68kRegister.D1, ExceptionContextStateOffset);
	}

	private void EmitFindUnwindSiteRuntime()
	{
		var loop = UniqueLabel("unwind_find_loop");
		var found = UniqueLabel("unwind_find_found");
		var missing = UniqueLabel("unwind_find_missing");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeFindUnwindSiteLabel);
		EmitAddressImmediateToRegister(M68kRegister.A1, MethodTableLabel);
		_assembler.EmitWord(0x2019); // MOVE.L (A1)+,D0 count
		_assembler.Mark(loop);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, missing);
		_assembler.EmitWord(0xB291); // CMP.L (A1),D1
		_assembler.EmitBranch(M68kCondition.Equal, found);
		_assembler.EmitWord(0x43E9); // LEA next-entry(A1),A1
		_assembler.EmitWord(UnwindSiteEntryBytes);
		_assembler.EmitWord(0x5380); // SUBQ.L #1,D0
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(found);
		_assembler.EmitWord(0x7001); // MOVEQ #1,D0
		_assembler.EmitWord(0x4E75); // RTS
		_assembler.Mark(missing);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitStoreExceptionContextRegister(M68kRegister register, short displacement)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2D40 | (int)register));
		}
		else
		{
			_assembler.EmitWord((ushort)(
				0x2D48 | ((int)register - (int)M68kRegister.A0)));
		}
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitLoadExceptionContextRegister(M68kRegister register, short displacement)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x202E | ((int)register << 9)));
		}
		else
		{
			_assembler.EmitWord((ushort)(
				0x206E | (((int)register - (int)M68kRegister.A0) << 9)));
		}
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitRestoreExceptionCursorRegisters()
	{
		// A6 owns the exception-context address. Restoring A6 in the same MOVEM
		// that uses it as the effective-address register is not portable on 68000,
		// so use A1 as the base and load A6 separately.
		_assembler.EmitWord(0x224E); // MOVEA.L A6,A1
		_assembler.EmitWord(0x4CE9); // MOVEM.L d16(A1),D2-D7/A2-A5
		_assembler.EmitWord(0x3CFC);
		_assembler.EmitWord(ExceptionContextBytes);
		_assembler.EmitWord(0x2C69); // MOVEA.L d16(A1),A6
		_assembler.EmitWord(ExceptionContextBytes + 40);
	}

	private void EmitStoreExceptionFrameContextRegister(
		M68kRegister register,
		short displacement)
	{
		EmitLoadExceptionContextRegister(M68kRegister.A1, ExceptionContextFrameBaseOffset);
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x2340 | (int)register));
		}
		else
		{
			_assembler.EmitWord((ushort)(
				0x2348 | ((int)register - (int)M68kRegister.A0)));
		}
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitStoreExceptionFrameContextAddress(string label, short displacement)
	{
		EmitLoadExceptionContextRegister(M68kRegister.A1, ExceptionContextFrameBaseOffset);
		_assembler.EmitWord(0x237C); // MOVE.L #label,d16(A1)
		_assembler.EmitAddress(label);
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitClearExceptionFrameContextSlot(short displacement)
	{
		EmitLoadExceptionContextRegister(M68kRegister.A1, ExceptionContextFrameBaseOffset);
		_assembler.EmitWord(0x42A9); // CLR.L d16(A1)
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitEnterExceptionHandler(string target, bool pushException)
	{
		EmitLoadExceptionContextRegister(M68kRegister.D0, ExceptionContextFrameBaseOffset);
		EmitAddressImmediateToRegister(M68kRegister.D1, target);
		EmitLoadExceptionContextRegister(M68kRegister.A0, ExceptionContextExceptionOffset);
		EmitRestoreExceptionCursorRegisters();
		_assembler.EmitWord(0x2E40); // MOVEA.L D0,A7
		if (pushException)
		{
			EmitPushRegister(M68kRegister.A0);
		}
		_assembler.EmitWord(0x2241); // MOVEA.L D1,A1
		_assembler.EmitWord(0x4ED1); // JMP (A1)
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
		_assembler.EmitWord(0x2050); // MOVEA.L (A0),A0
		_assembler.Mark(loop);
		EmitMoveRegister(M68kRegister.A0, M68kRegister.D0);
		_assembler.EmitWord(0xB089); // CMP.L A1,D0
		_assembler.EmitBranch(M68kCondition.Equal, match);
		EmitMoveRegister(M68kRegister.A0, M68kRegister.D0);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, noMatch);
		_assembler.EmitWord(0x2068); // MOVEA.L 8(A0),A0
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
		EmitLoadEhFrameRegister(
			M68kRegister.A1,
			RuntimeFramePendingActionOffset);
		EmitMoveRegister(M68kRegister.A1, M68kRegister.D0);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, valid);
		_assembler.EmitWord(0x4AFC); // ILLEGAL
		_assembler.Mark(valid);
		EmitLoadEhFrameRegister(
			M68kRegister.A0,
			RuntimeFrameActiveExceptionOffset);
		_assembler.EmitWord(0x4ED1); // JMP (A1)

		_assembler.Mark(RuntimeExceptionLeaveContinueLabel);
		EmitLoadEhFrameRegister(
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

			_assembler.AlignWord();
			_assembler.Mark(state.Label);
			foreach (var entry in group.Regions.Where(static item => item.Region.IsCatch))
			{
				var nextCatch = UniqueLabel("eh_next_catch");
				if (!entry.Region.CatchType.IsNil)
				{
					EmitLoadExceptionContextRegister(
						M68kRegister.A0,
						ExceptionContextExceptionOffset);
					EmitRuntimeTypeAddress(
						M68kRegister.A1,
						entry.Region.CatchType);
					_assembler.EmitBsr(RuntimeExceptionTypeMatchLabel);
					_assembler.EmitWord(0x4A80); // TST.L D0
					_assembler.EmitBranch(M68kCondition.Equal, nextCatch);
				}

				EmitLoadExceptionContextRegister(
					M68kRegister.A0,
					ExceptionContextExceptionOffset);
				EmitStoreExceptionFrameContextRegister(
					M68kRegister.A0,
					RuntimeFrameActiveExceptionOffset);
				EmitClearExceptionFrameContextSlot(RuntimeFramePendingActionOffset);
				EmitClearExceptionFrameContextSlot(RuntimeFrameLeaveContinuationOffset);
				EmitEnterExceptionHandler(
					ControlFlowTargetLabel(state.Method, entry.Region.HandlerOffset),
					pushException: true);
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
				var resume = RegisterExceptionResumeAction(state.Method, suffix);
				EmitLoadExceptionContextRegister(
					M68kRegister.A0,
					ExceptionContextExceptionOffset);
				EmitStoreExceptionFrameContextRegister(
					M68kRegister.A0,
					RuntimeFrameActiveExceptionOffset);
				EmitStoreExceptionFrameContextAddress(
					resume,
					RuntimeFramePendingActionOffset);
				EmitEnterExceptionHandler(
					ControlFlowTargetLabel(state.Method, finallyRegion.HandlerOffset),
					pushException: false);
				continue;
			}

			_assembler.EmitJmp(
				suffix ?? RuntimeExceptionUnwindFrameLabel,
				external: false);
		}

		foreach (var action in _exceptionResumeActions.Values)
		{
			_assembler.AlignWord();
			_assembler.Mark(action.Label);
			EmitAddressImmediateToRegister(
				M68kRegister.D0,
				RuntimeMethodDescriptorLabel(action.Method));
			if (action.StateLabel is null)
			{
				EmitImmediateToRegister(M68kRegister.D1, 0);
			}
			else
			{
				EmitAddressImmediateToRegister(M68kRegister.D1, action.StateLabel);
			}
			_assembler.EmitJmp(RuntimeExceptionResumeLabel, external: false);
		}
	}

	private string RegisterExceptionResumeAction(CilMethod method, string? stateLabel)
	{
		var key = $"{method.Identity}:{stateLabel ?? "unwind"}";
		if (!_exceptionResumeActions.TryGetValue(key, out var action))
		{
			action = new ExceptionResumeAction(
				method,
				stateLabel,
				UniqueLabel("eh_resume_action"));
			_exceptionResumeActions.Add(key, action);
		}
		return action.Label;
	}

	private void EmitNormalLeaveActions()
	{
		foreach (var chain in _normalLeaveChains.Values)
		{
			for (var index = 1; index < chain.FinallyRegions.Length; index++)
			{
				_assembler.AlignWord();
				_assembler.Mark(NormalLeaveNextActionLabel(chain, index));
				EmitEhFrameAddress(
					NormalLeaveNextActionLabel(chain, index + 1),
					RuntimeFramePendingActionOffset);
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

	private void EmitEhFrameImmediate(int value, short displacement)
	{
		if (value == 0)
		{
			_assembler.EmitWord(0x42AF); // CLR.L d16(A7)
			_assembler.EmitWord(unchecked((ushort)displacement));
			return;
		}
		_assembler.EmitWord(0x2F7C); // MOVE.L #value,d16(A7)
		_assembler.EmitLong(unchecked((uint)value));
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitEhFrameAddress(string label, short displacement)
	{
		_assembler.EmitWord(0x2F7C); // MOVE.L #label,d16(A7)
		_assembler.EmitAddress(label);
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitLoadEhFrameRegister(M68kRegister register, short displacement)
	{
		if (register <= M68kRegister.D7)
		{
			_assembler.EmitWord((ushort)(0x202F | ((int)register << 9)));
		}
		else
		{
			_assembler.EmitWord((ushort)(
				0x206F | (((int)register - (int)M68kRegister.A0) << 9)));
		}
		_assembler.EmitWord(unchecked((ushort)displacement));
	}

	private void EmitRuntimeFrameImmediate(int value, short displacement)
	{
		if (value == 0)
		{
			_assembler.EmitWord(0x42AD); // CLR.L d16(A5)
			_assembler.EmitWord(unchecked((ushort)displacement));
			return;
		}

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

	private string RuntimeMethodUnwindRestoreLabel(CilMethod method) =>
		$"runtime:method-unwind-restore:{ModuleLabelPrefix(method.ModuleName)}{MetadataTokens.GetToken(method.Handle):X8}";

	private static string RuntimeRootMapLabel(int index) =>
		$"runtime:root-map:{index}";

	private static string RuntimeTypeDescriptorLabel(string typeName) =>
		$"runtime:type-descriptor:{typeName}";

	private string TypeDescriptorLabel(EntityHandle handle) =>
		handle.Kind == HandleKind.TypeDefinition
			? TypeDescriptorLabel((TypeDefinitionHandle)handle)
			: RuntimeTypeDescriptorLabel(_module.GetTypeDisplayName(handle));

	private static string RuntimeExceptionObjectLabel(string typeName) =>
		$"runtime:exception-object:{typeName}";

	private const string RuntimeExceptionRaiseLabel = "__c68k_exception_raise";
	private const string RuntimeExceptionResumeLabel = "__c68k_exception_resume";
	private const string RuntimeExceptionDispatchLabel = "__c68k_exception_dispatch";
	private const string RuntimeExceptionJumpStateLabel = "__c68k_exception_jump_state";
	private const string RuntimeExceptionUnwindFrameLabel = "__c68k_exception_unwind_frame";
	private const string RuntimeExceptionUnhandledLabel = "__c68k_exception_unhandled";
	private const string RuntimeExceptionTypeMatchLabel = "__c68k_exception_type_match";
	private const string RuntimeExceptionEndFinallyLabel = "__c68k_exception_endfinally";
	private const string RuntimeExceptionLeaveContinueLabel = "__c68k_exception_leave_continue";
	private const string RuntimeFindUnwindSiteLabel = "__c68k_find_unwind_site";
	private const string RuntimeAmigaRequesterLabel = "__c68k_amiga_unhandled_requester";

	private const short ExceptionContextExceptionOffset = 0;
	private const short ExceptionContextCursorOffset = 4;
	private const short ExceptionContextSiteOffset = 8;
	private const short ExceptionContextFrameBaseOffset = 12;
	private const short ExceptionContextDescriptorOffset = 16;
	private const short ExceptionContextStateOffset = 20;
	private const int ExceptionContextBytes = 24;
	private const string RuntimeInitialStackLabel = "runtime:amiga-initial-stack";
	private const string RuntimeIntuitionNameLabel = "runtime:amiga-intuition-name";
	private const string RuntimeRequesterBodyLabel = "runtime:amiga-requester-body";
	private const string RuntimeRequesterExitLabel = "runtime:amiga-requester-exit";
	private const string RuntimeRequesterFreezeLabel = "runtime:amiga-requester-freeze";
	private const string RuntimeRequesterBodyTextLabel = "runtime:amiga-requester-body-text";
	private const string RuntimeRequesterExitTextLabel = "runtime:amiga-requester-exit-text";
	private const string RuntimeRequesterFreezeTextLabel = "runtime:amiga-requester-freeze-text";
}
