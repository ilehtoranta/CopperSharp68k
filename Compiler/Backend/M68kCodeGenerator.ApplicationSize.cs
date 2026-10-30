/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
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
	private int UnwindSiteFieldBytes => _narrowApplicationExceptionSites ? 2 : 4;
	private int UnwindStackAdjustmentOffset => (_omitApplicationUnwindState ? 2 : 3) * UnwindSiteFieldBytes;
	private int UnwindCleanupOffset => _compactApplicationExceptionSites ? UnwindStackAdjustmentOffset + UnwindSiteFieldBytes : 20;

	private IEnumerable<UnwindMethodLayout> ReferencedApplicationUnwindLayouts() =>
		_unwindMethodLayouts.Values.Where(layout => !UsesApplicationSizeOptimizations ||
			!_request.CodeSizeOptimizations!.PruneApplicationUnwindMetadata ||
			_assembler.ReferencesTarget(RuntimeMethodDescriptorLabel(layout.Method)) ||
			_assembler.ReferencesTarget(RuntimeMethodUnwindRestoreLabel(layout.Method)));

	private void PrepareApplicationExceptionLayout()
	{
		// Both built-in and external collectors consume the public 20/24-byte
		// root-walking ABI. Only the compiler-owned exception reader may specialize.
		_compactApplicationExceptionSites = UsesApplicationSizeOptimizations &&
			_request.CodeSizeOptimizations!.CompactApplicationExceptionSites &&
			!M68kCompiler.IsManagedRuntime(_request) &&
			!_request.Imports.ContainsKey(M68kRuntimeImports.GcCollect) &&
			_unwindSites.All(site => site.RootOffsets.IsEmpty);
		_omitApplicationUnwindState = _compactApplicationExceptionSites &&
			_unwindSites.All(site => site.ExceptionStateLabel is null);
		// Small tables would not amortize the extra sign extensions/range check.
		// Pointer displacements are checked after all layout changes by the linker;
		// an overflow recompiles with this pass disabled, never truncates an offset.
		_narrowApplicationExceptionSites = _compactApplicationExceptionSites &&
			_request.CodeSizeOptimizations!.NarrowApplicationExceptionSites && _unwindSites.Count >= 8 &&
			_unwindSites.All(site => site.StackAdjustment is >= short.MinValue and <= short.MaxValue);
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
