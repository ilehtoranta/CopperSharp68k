/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private readonly HashSet<CilMethodIdentity> _applicationLeafMethods = new();
	private int _applicationLeafSitesRemoved;

	private void AnalyzeApplicationLeafSites(IReadOnlyDictionary<CilMethodIdentity, M68kMachineFunction> functions)
	{
		if (!UsesApplicationSizeOptimizations || !_request.CodeSizeOptimizations!.PruneApplicationLeafSites ||
			_request.Imports.ContainsKey(M68kRuntimeImports.GcCollect) ||
			M68kCompiler.IsManagedRuntime(_request) && !UsesBuiltInManagedPool) return;

		// This affects only return-site metadata, never liveness, root spills or
		// instructions. Keep every opaque call and every potentially throwing or
		// collecting operation. Recursion is safe only if its entire component
		// and every outgoing edge meet the same proof.
		// Collector entry points can observe the stack or reclaim objects even
		// though their implementation consists of raw memory intrinsics.
		var collectorEntries = _managedPoolRuntime is { } runtime
			? runtime.Methods.Concat(runtime.FinalizerMethods).Where(method => method != runtime.Mark)
				.Select(method => method.Identity).ToHashSet()
			: new HashSet<CilMethodIdentity>();
		var candidates = functions.Where(item => !collectorEntries.Contains(item.Key) &&
			!IsCollectorEntry(item.Value.DisplayName) && !item.Value.HasExceptionHandlers &&
			!item.Value.HasDynamicStackAllocation && item.Value.Blocks.SelectMany(block => block.Instructions)
				.All(IsLocallySafe)).ToDictionary(item => item.Key, item => item.Value);
		bool changed;
		do
		{
			changed = false;
			foreach (var (identity, function) in candidates.ToArray())
			{
				if (function.Blocks.SelectMany(block => block.Instructions).Any(instruction =>
					instruction.LogicalCall is { DispatchKind: M68kMachineCallDispatchKind.Direct } call &&
					call.ResolvedTargets.Any(target => !candidates.ContainsKey(target))))
				{
					candidates.Remove(identity);
					changed = true;
				}
			}
		} while (changed);
		_applicationLeafMethods.UnionWith(candidates.Keys);

		static bool IsCollectorEntry(string name)
		{
			// Include legacy readers called explicitly by user code, even when
			// the compiler's own adapters selected the compact reader variants.
			const string prefix = "CopperSharp.Runtime.ManagedPool::";
			if (!name.StartsWith(prefix, StringComparison.Ordinal)) return false;
			var member = name[prefix.Length..];
			return member.StartsWith("Collect", StringComparison.Ordinal) ||
				member.StartsWith("MarkRoots", StringComparison.Ordinal) ||
				member is "Allocate" or "Dispose" or "Shutdown" or "DrainFinalizers" or "PrepareShutdownFinalizers";
		}

		bool IsLocallySafe(M68kMachineInstruction instruction)
		{
			if (instruction.RequiresLiveCallerFrame) return false;
			// Static field accesses have no null receiver. Do not discard the
			// conservative throw flag unless no type initializer can be triggered.
			if (!instruction.IsSafepoint && instruction.LogicalCall is null &&
				instruction.Operation is M68kMachineOperation.Load or M68kMachineOperation.Store &&
				instruction.Origin is { } fieldOrigin &&
				(fieldOrigin.SourceInstruction.OpCode == OpCodes.Ldsfld ||
				 fieldOrigin.SourceInstruction.OpCode == OpCodes.Stsfld) &&
				_module.GetTriggeredTypeInitializer(fieldOrigin.SourceMethod, fieldOrigin.SourceInstruction) is null)
				return true;
			if (instruction.Operation == M68kMachineOperation.Call)
			{
				// These exact intrinsics lower to a single native memory access,
				// never a managed call or a callback into user/native code.
				if (instruction.Origin is { SourceInstruction.Operand: int token } origin &&
					M68kMachineInliningPolicy.IsGuestMemoryIntrinsic(_module.ResolveMethodToken(
						token, origin.SourceMethod, origin.SourceInstruction.Offset).ImportName))
					return true;
				return instruction.LogicalCall is {
					DispatchKind: M68kMachineCallDispatchKind.Direct, RequiresNullCheck: false,
					ResolvedTargets.Length: > 0
				};
			}
			return !instruction.MayThrow && !instruction.IsSafepoint && instruction.LogicalCall is null &&
				instruction.Operation is not (M68kMachineOperation.Other or M68kMachineOperation.Throw or
					M68kMachineOperation.TypeInitialize or M68kMachineOperation.ObjectAllocate or
					M68kMachineOperation.ArrayAllocate or M68kMachineOperation.Box or
					M68kMachineOperation.DelegateCreate or M68kMachineOperation.Divide or
					M68kMachineOperation.Remainder or M68kMachineOperation.BulkCopy or
					M68kMachineOperation.PlatformBaseLoad or M68kMachineOperation.PlatformBaseStore);
		}
	}

	internal static bool IsApplicationMethodSizeEligible(M68kMachineFunction function, bool collectorCanRun) =>
		!function.HasExceptionHandlers && !function.HasDynamicStackAllocation &&
		(!collectorCanRun || function.ManagedByrefTypes.Count == 0 && function.GcSpillSlots.Count == 0 &&
		!function.Values.Values.Any(value => value.IsGcReference) &&
		!function.LocalHomes.Values.Any(home => home.HasGcReferences) &&
		!function.ArgumentHomes.Values.Any(home => home.HasGcReferences));

	private bool UsesApplicationSizeOptimizations =>
		_request.CodeSizeOptimizations is not null &&
		_request.RuntimeProfile == M68kRuntimeProfile.Application &&
		_request.OutputFormat == M68kOutputFormat.Hunk &&
		_request.Cpu == M68kCpuTarget.M68000;

	private bool _compactApplicationExceptionSites;
	private bool _omitApplicationUnwindState;
	private bool _narrowApplicationExceptionSites;
	private bool UsesCompactApplicationGcSites => M68kCompiler.UsesCompactApplicationGcSites(_request);
	private int UnwindSiteFieldBytes => _narrowApplicationExceptionSites ? 2 : 4;
	private int UnwindStackAdjustmentOffset => (_omitApplicationUnwindState ? 2 : 3) * UnwindSiteFieldBytes;
	private int UnwindCleanupOffset => _compactApplicationExceptionSites
		? UnwindStackAdjustmentOffset + UnwindSiteFieldBytes * (UsesCompactApplicationGcSites ? 2 : 1) : 20;

	private IEnumerable<UnwindMethodLayout> ReferencedApplicationUnwindLayouts() =>
		_unwindMethodLayouts.Values.Where(layout => !UsesApplicationSizeOptimizations ||
			!_request.CodeSizeOptimizations!.PruneApplicationUnwindMetadata ||
			_assembler.ReferencesTarget(RuntimeMethodDescriptorLabel(layout.Method)) ||
			_assembler.ReferencesTarget(RuntimeMethodUnwindRestoreLabel(layout.Method)));

	private void PrepareApplicationExceptionLayout()
	{
		// Select the collector reader before reachability; both readers and the
		// emitter must agree even when exception-state columns happen to be empty.
		_compactApplicationExceptionSites = UsesCompactApplicationGcSites || UsesApplicationSizeOptimizations &&
			_request.CodeSizeOptimizations!.CompactApplicationExceptionSites &&
			!M68kCompiler.IsManagedRuntime(_request) &&
			!_request.Imports.ContainsKey(M68kRuntimeImports.GcCollect) &&
			_unwindSites.All(site => site.RootOffsets.IsEmpty);
		_omitApplicationUnwindState = _compactApplicationExceptionSites && !UsesCompactApplicationGcSites &&
			_unwindSites.All(site => site.ExceptionStateLabel is null);
		// Small tables would not amortize the extra sign extensions/range check.
		// Pointer displacements are checked after all layout changes by the linker;
		// an overflow recompiles with this pass disabled, never truncates an offset.
		_narrowApplicationExceptionSites = UsesCompactApplicationGcSites || _compactApplicationExceptionSites &&
			_request.CodeSizeOptimizations!.NarrowApplicationExceptionSites && _unwindSites.Count >= 8 &&
			_unwindSites.All(site => site.StackAdjustment is >= short.MinValue and <= short.MaxValue);
		if (UsesCompactApplicationGcSites &&
			_unwindSites.Any(site => site.StackAdjustment is < short.MinValue or > short.MaxValue))
			throw new M68kNarrowDataOverflowException();
		if (_applicationLeafSitesRemoved != 0)
			_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.PruneApplicationLeafSites),
				_applicationLeafSitesRemoved * UnwindSiteEntryBytes);
	}

	private void EmitSharedApplicationUnwindMetadata()
	{
		// These are private immutable records. Their addresses carry no method
		// identity: each site still keeps its own resume PC, exception state,
		// stack adjustment and cleanup. Keep the reader ABI and all offsets intact.
		var start = _assembler.Offset;
		var originalBytes = 0;
		var rootGroups = _unwindSites.Select((site, index) => (site.RootOffsets, Index: index))
			.Where(item => !item.RootOffsets.IsEmpty)
			.GroupBy(item => string.Join(",", item.RootOffsets));
		foreach (var group in rootGroups)
		{
			var roots = group.First().RootOffsets;
			foreach (var item in group)
			{
				_assembler.Mark(RuntimeRootMapLabel(item.Index));
				originalBytes += 4 + roots.Length * 4;
			}
			_assembler.EmitLong((uint)roots.Length);
			foreach (var offset in roots) _assembler.EmitLong(unchecked((uint)offset));
		}

		var layouts = ReferencedApplicationUnwindLayouts().ToArray();
		foreach (var group in layouts.GroupBy(layout => (
			layout.FrameBytes, Registers: string.Join(",", layout.CalleeSavedRegisters),
			Dynamic: UsesExtendedUnwindMetadata && layout.HasDynamicStackAllocation,
			Anchor: UsesExtendedUnwindMetadata ? layout.SavedFrameAnchorOffset : null)))
		{
			var layout = group.First();
			_assembler.AlignWord();
			foreach (var member in group)
			{
				_assembler.Mark(RuntimeMethodDescriptorLabel(member.Method));
				originalBytes += UsesExtendedUnwindMetadata ? 20 : 12;
			}
			_assembler.EmitLong((uint)layout.FrameBytes);
			_assembler.EmitLong((uint)(layout.CalleeSavedRegisters.Length * 4));
			_assembler.EmitAddress(RuntimeMethodUnwindRestoreLabel(layout.Method));
			if (UsesExtendedUnwindMetadata)
			{
				_assembler.EmitLong(layout.HasDynamicStackAllocation ? 1u : 0u);
				_assembler.EmitLong(layout.SavedFrameAnchorOffset is { } anchorOffset
					? checked((uint)anchorOffset + 1) : 0u);
			}
		}

		// Save order remains exact. Different frame sizes can share a restore by
		// reading FrameBytes from the existing descriptor in the exception context.
		// The extra ten bytes are amortized by at least two nonempty routines.
		var restoreGroups = layouts.GroupBy(layout => string.Join(",", layout.CalleeSavedRegisters))
			.SelectMany(group => _request.CodeSizeOptimizations!.ShareApplicationUnwindRestores || group.First().CalleeSavedRegisters.IsEmpty
				? new[] { group.ToArray() }
				: group.GroupBy(layout => layout.FrameBytes).Select(frames => frames.ToArray()))
			.OrderBy(group => Array.IndexOf(layouts, group[0]));
		foreach (var group in restoreGroups)
		{
			var layout = group.First();
			var variableFrame = !layout.CalleeSavedRegisters.IsEmpty && group.Any(member => member.FrameBytes != layout.FrameBytes);
			foreach (var member in group)
			{
				_assembler.Mark(RuntimeMethodUnwindRestoreLabel(member.Method));
				originalBytes += 4 + layout.CalleeSavedRegisters.Length * 8;
			}
			_assembler.EmitWord(0x2041); // MOVEA.L D1,A0 exception context
			if (variableFrame)
			{
				_assembler.EmitWord(0x2068); // MOVEA.L descriptor(A0),A0
				_assembler.EmitWord(checked((ushort)ExceptionContextDescriptorOffset));
				_assembler.EmitWord(0x2010); // MOVE.L (A0),D0 frame bytes
				_assembler.EmitWord(0xD3C0); // ADDA.L D0,A1 saved-register base
				_assembler.EmitWord(0x2041); // MOVEA.L D1,A0 exception context
				layout = layout with { FrameBytes = 0 };
			}
			if (!TryEmitCompactApplicationUnwindRestore(layout))
			{
				for (var index = 0; index < layout.CalleeSavedRegisters.Length; index++)
				{
					var register = layout.CalleeSavedRegisters[index];
					var savedIndex = layout.CalleeSavedRegisters.Length < 3
						? layout.CalleeSavedRegisters.Length - 1 - index : index;
					EmitLoadUnwindRegister(register, checked((short)(layout.FrameBytes + savedIndex * 4)));
					EmitStoreUnwindRegisterSnapshot(register);
				}
			}
			_assembler.EmitWord(0x4E75); // RTS
		}
		_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.ShareApplicationRuntimeMetadata),
			originalBytes - (_assembler.Offset - start));
	}

	private bool TryEmitCompactApplicationUnwindRestore(UnwindMethodLayout layout)
	{
		var registers = layout.CalleeSavedRegisters;
		// Three or more saves use ascending MOVEM stack order. A0/A1 own the
		// context/frame pointers and must not be restored by this routine.
		if (registers.Length < 3 || layout.FrameBytes is < 0 or > short.MaxValue ||
			!registers.SequenceEqual(registers.Order()) ||
			registers.Distinct().Count() != registers.Length || registers.Any(register =>
				register is < M68kRegister.D2 or > M68kRegister.A6 ||
				register is M68kRegister.A0 or M68kRegister.A1)) return false;
		_assembler.EmitWord(0x4CE9); // MOVEM.L d16(A1),registers
		_assembler.EmitWord(Mask(0, registers.Length));
		_assembler.EmitWord(checked((ushort)layout.FrameBytes));
		for (var first = 0; first < registers.Length;)
		{
			var end = first + 1;
			while (end < registers.Length && SnapshotIndex(registers[end]) == SnapshotIndex(registers[end - 1]) + 1) end++;
			if (end == first + 1) EmitStoreUnwindRegisterSnapshot(registers[first]);
			else
			{
				_assembler.EmitWord(0x48E8); // MOVEM.L registers,d16(A0)
				_assembler.EmitWord(Mask(first, end));
				_assembler.EmitWord(checked((ushort)(ExceptionContextBytes + SnapshotIndex(registers[first]) * 4)));
			}
			first = end;
		}
		// Exception dispatch overwrites NZVC by loading descriptor.FrameBytes
		// immediately after this private call. Neither form changes X.
		return true;

		ushort Mask(int first, int end)
		{
			var mask = 0;
			for (var index = first; index < end; index++) mask |= 1 << (int)registers[index];
			return checked((ushort)mask);
		}
		static int SnapshotIndex(M68kRegister register) => register <= M68kRegister.D7
			? (int)register - (int)M68kRegister.D2 : 6 + (int)register - (int)M68kRegister.A2;
	}
}
