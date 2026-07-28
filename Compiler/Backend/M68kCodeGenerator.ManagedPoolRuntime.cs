/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Collections.Immutable;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private void EmitGcConfigData()
	{
		_assembler.AlignWord();
		_assembler.Mark(GcConfigLabel);
		_assembler.EmitLong((uint)_memoryManagement);
		_assembler.EmitLong((uint)M68kCompiler.GetEffectiveGcSweepStrategy(_request));
		_assembler.EmitLong(_request.Heap.StartAddress);
		_assembler.EmitLong(_request.Heap.Size);
		_assembler.EmitLong(_request.GcTelemetry.StaleBytesThreshold);
		_assembler.EmitLong(_request.GcTelemetry.StaleBlocksThreshold);
		_assembler.EmitLong(_request.GcTelemetry.IntervalTicks);
	}

	private void EmitManagedPoolRuntimeData()
	{
		_assembler.AlignWord();
		_assembler.Mark(GcHeapStartLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcHeapEndLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcFreeHeadLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcAllocHeadLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcStaleBytesLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcStaleBlocksLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcStaleBytesThresholdLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcStaleBlocksThresholdLabel);
		_assembler.EmitLong(0);
		_assembler.Mark(GcStaticRootsLabel);
		var staticRoots = _staticFields.Values
			.Where(static field => field.IsStatic && field.Type.IsReference)
			.OrderBy(static field =>
				System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(field.Handle))
			.ToArray();
		_assembler.EmitLong((uint)staticRoots.Length);
		foreach (var field in staticRoots)
		{
			_assembler.EmitAddress(StaticFieldLabel(field.Handle));
		}
	}

	private string EmitEntryAdapter(
		CilMethod entry,
		bool usesManagedRuntime,
		bool usesAmigaStartupArguments,
		bool usesExceptionRuntime)
	{
		const string label = "entry:managed";
		_assembler.AlignWord();
		_assembler.Mark(label);
		var isolatesRuntimeFrames = usesManagedRuntime || usesExceptionRuntime;
		if (isolatesRuntimeFrames)
		{
			EmitPushRegister(M68kRegister.A5);
			EmitImmediateToRegister(M68kRegister.A5, 0);
		}
		if (usesManagedRuntime && usesAmigaStartupArguments)
		{
			EmitPushD0();
			EmitPushRegister(M68kRegister.A0);
		}
		EmitInitializePlatformBases();
		if (usesManagedRuntime)
		{
			_assembler.EmitWord(0x2F3C); // MOVE.L #gc-config,-(A7)
			_assembler.EmitAddress(GcConfigLabel);
			EmitRuntimeJsr(RuntimeInitLabel, M68kRuntimeImports.GcInit);
			_loadedPlatformBase = null;
			EmitDiscardStackArguments(1);
			EmitRequireNonNull();
			if (usesAmigaStartupArguments)
			{
				EmitPopRegister(M68kRegister.A0);
				EmitPopD0();
			}
			_assembler.EmitBsr(MethodLabel(entry));
			_loadedPlatformBase = null;
			EmitPushD0();
			EmitPushRegister(M68kRegister.A0);
			EmitRuntimeJsr(RuntimeShutdownLabel, M68kRuntimeImports.GcShutdown);
			_loadedPlatformBase = null;
			EmitPopRegister(M68kRegister.A0);
			EmitPopD0();
			EmitPopRegister(M68kRegister.A5);
			_assembler.EmitWord(0x4E75); // RTS
			return label;
		}

		if (usesExceptionRuntime)
		{
			_assembler.EmitBsr(MethodLabel(entry));
			_loadedPlatformBase = null;
			EmitPopRegister(M68kRegister.A5);
			_assembler.EmitWord(0x4E75); // RTS
			return label;
		}

		return label;
	}

	private void EmitRuntimeJsr(string internalLabel, string externalLabel)
	{
		if (UsesBuiltInManagedPool)
		{
			_assembler.EmitBsr(internalLabel);
		}
		else
		{
			_assembler.EmitJsr(externalLabel, external: true);
		}
	}

	private void EmitManagedPoolRuntime()
	{
		if (!UsesBuiltInManagedPool)
		{
			return;
		}

		EmitManagedPoolInit();
		EmitManagedPoolAlloc();
		EmitManagedPoolDispose();
		EmitManagedPoolMark();
		EmitManagedPoolMarkRuntimeRoots();
		EmitManagedPoolCollect();
		EmitManagedPoolCoalesce();
		EmitManagedPoolTelemetryGetters();
		EmitManagedPoolShutdown();
	}

	private void EmitManagedPoolInit()
	{
		var fail = UniqueLabel("gc_init_fail");
		var done = UniqueLabel("gc_init_done");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeInitLabel);
		_assembler.EmitWord(0x206F); // MOVEA.L 4(A7),A0 config
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2028); // MOVE.L 8(A0),D0 heap start
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, fail);
		EmitStoreD0ToLabel(GcHeapStartLabel);
		EmitStoreD0ToLabel(GcFreeHeadLabel);
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0 first block
		_assembler.EmitWord(0x206F); // MOVEA.L 4(A7),A0 config
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2228); // MOVE.L 12(A0),D1 heap size
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.Equal, fail);
		EmitLoadA0FromLabel(GcHeapStartLabel);
		_assembler.EmitWord(0x2008); // MOVE.L A0,D0
		_assembler.EmitWord(0xD081); // ADD.L D1,D0
		EmitStoreD0ToLabel(GcHeapEndLabel);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		EmitStoreD0ToA0Displacement(4, 0); // next
		EmitStoreD0ToA0Displacement(4, 4); // prev
		_assembler.EmitWord(0x2001); // MOVE.L D1,D0
		EmitStoreD0ToA0Displacement(4, 8); // size
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		EmitStoreD0ToA0Displacement(4, 12); // flags
		EmitStoreD0ToLabel(GcAllocHeadLabel);
		EmitStoreD0ToLabel(GcStaleBytesLabel);
		EmitStoreD0ToLabel(GcStaleBlocksLabel);
		_assembler.EmitWord(0x206F); // MOVEA.L 4(A7),A0 config
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2028); // MOVE.L 16(A0),D0 stale bytes threshold
		_assembler.EmitWord(0x0010);
		EmitStoreD0ToLabel(GcStaleBytesThresholdLabel);
		_assembler.EmitWord(0x2028); // MOVE.L 20(A0),D0 stale blocks threshold
		_assembler.EmitWord(0x0014);
		EmitStoreD0ToLabel(GcStaleBlocksThresholdLabel);
		_assembler.EmitWord(0x7001); // MOVEQ #1,D0
		_assembler.EmitBranch(M68kCondition.True, done);
		_assembler.Mark(fail);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.Mark(done);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolAlloc()
	{
		var loop = UniqueLabel("gc_alloc_loop");
		var found = UniqueLabel("gc_alloc_found");
		var noSplit = UniqueLabel("gc_alloc_no_split");
		var zeroLoop = UniqueLabel("gc_alloc_zero_loop");
		var zeroDone = UniqueLabel("gc_alloc_zero_done");
		var fail = UniqueLabel("gc_alloc_fail");
		var returnLabel = UniqueLabel("gc_alloc_return");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeAllocLabel);
		_assembler.EmitWord(0x202F); // MOVE.L 4(A7),D0 requested payload size
		_assembler.EmitWord(0x0004);
		EmitPushRegisters(stackalloc[]
		{
			M68kRegister.D2,
			M68kRegister.D3,
			M68kRegister.D4,
			M68kRegister.A2
		});
		_assembler.EmitWord(0x0680); // ADDI.L #3,D0
		_assembler.EmitLong(3);
		_assembler.EmitWord(0x0280); // ANDI.L #~3,D0
		_assembler.EmitLong(0xFFFF_FFFCu);
		_assembler.EmitWord(0x0680); // ADDI.L #header,D0 total size
		_assembler.EmitLong(GcBlockHeaderSize);
		_assembler.EmitWord(0x2800); // MOVE.L D0,D4 requested total size
		EmitLoadA0FromLabel(GcFreeHeadLabel);
		_assembler.Mark(loop);
		_assembler.EmitWord(0x2208); // MOVE.L A0,D1
		_assembler.EmitBranch(M68kCondition.Equal, fail);
		_assembler.EmitWord(0x2228); // MOVE.L 8(A0),D1 block size
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarryClear, found);
		_assembler.EmitWord(0x2050); // MOVEA.L (A0),A0 next
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(found);
		_assembler.EmitWord(0x9280); // SUB.L D0,D1 remainder
		_assembler.EmitWord(0x0C81); // CMPI.L #min split,D1
		_assembler.EmitLong(GcMinimumSplitSize);
		_assembler.EmitBranch(M68kCondition.CarrySet, noSplit);
		EmitManagedPoolSplitFreeBlock();
		_assembler.EmitBranch(M68kCondition.True, zeroDone);
		_assembler.Mark(noSplit);
		EmitManagedPoolUnlinkFreeBlock();
		_assembler.EmitWord(0x2028); // MOVE.L 8(A0),D0 total size
		_assembler.EmitWord(0x0008);
		_assembler.Mark(zeroDone);
		EmitManagedPoolLinkAllocatedBlock();
		_assembler.EmitWord(0x2800); // MOVE.L D0,D4 actual total size
		EmitManagedPoolRecordAllocation();
		_assembler.EmitWord(0x2248); // MOVEA.L A0,A1
		_assembler.EmitWord(0x43E9); // LEA 16(A1),A1 payload
		_assembler.EmitWord(GcBlockHeaderSize);
		_assembler.EmitWord(0x2409); // MOVE.L A1,D2 return payload
		_assembler.EmitWord(0x2204); // MOVE.L D4,D1 total size
		_assembler.EmitWord(0x0681); // ADDI.L #-header,D1 payload bytes
		_assembler.EmitLong(unchecked((uint)-GcBlockHeaderSize));
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.Mark(zeroLoop);
		_assembler.EmitWord(0x4A81); // TST.L D1
		_assembler.EmitBranch(M68kCondition.Equal, zeroDone + ":return");
		_assembler.EmitWord(0x22C0); // MOVE.L D0,(A1)+
		_assembler.EmitWord(0x5981); // SUBQ.L #4,D1
		_assembler.EmitBranch(M68kCondition.True, zeroLoop);
		_assembler.Mark(zeroDone + ":return");
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
		_assembler.EmitBranch(M68kCondition.True, returnLabel);
		_assembler.Mark(fail);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		_assembler.Mark(returnLabel);
		EmitPopRegisters(stackalloc[]
		{
			M68kRegister.D2,
			M68kRegister.D3,
			M68kRegister.D4,
			M68kRegister.A2
		});
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolRecordAllocation()
	{
		EmitLoadD0FromLabel(GcStaleBytesLabel);
		_assembler.EmitWord(0xD084); // ADD.L D4,D0
		EmitStoreD0ToLabel(GcStaleBytesLabel);
		EmitLoadD0FromLabel(GcStaleBlocksLabel);
		_assembler.EmitWord(0x5280); // ADDQ.L #1,D0
		EmitStoreD0ToLabel(GcStaleBlocksLabel);
	}

	private void EmitManagedPoolSplitFreeBlock()
	{
		var nextDone = UniqueLabel("gc_split_next_done");
		var prevPresent = UniqueLabel("gc_split_prev_present");
		var prevDone = UniqueLabel("gc_split_prev_done");
		_assembler.EmitWord(0x2248); // MOVEA.L A0,A1 new free block base
		_assembler.EmitWord(0xD3C0); // ADDA.L D0,A1
		_assembler.EmitWord(0x2428); // MOVE.L (A0),D2 old next
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2342); // MOVE.L D2,(A1)
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2628); // MOVE.L 4(A0),D3 old prev
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2343); // MOVE.L D3,4(A1)
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2341); // MOVE.L D1,8(A1) remainder size
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0x7400); // MOVEQ #0,D2
		_assembler.EmitWord(0x2342); // MOVE.L D2,12(A1) free flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A82); // TST.L D2 old next? D2 is zero now; reload
		_assembler.EmitWord(0x2429); // MOVE.L (A1),D2
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x4A82); // TST.L D2
		_assembler.EmitBranch(M68kCondition.Equal, nextDone);
		_assembler.EmitWord(0x2442); // MOVEA.L D2,A2
		_assembler.EmitWord(0x2549); // MOVE.L A1,4(A2)
		_assembler.EmitWord(0x0004);
		_assembler.Mark(nextDone);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.NotEqual, prevPresent);
		_assembler.EmitWord(0x2009); // MOVE.L A1,D0
		EmitStoreD0ToLabel(GcFreeHeadLabel);
		_assembler.EmitBranch(M68kCondition.True, prevDone);
		_assembler.Mark(prevPresent);
		_assembler.EmitWord(0x2443); // MOVEA.L D3,A2
		_assembler.EmitWord(0x2549); // MOVE.L A1,(A2)
		_assembler.EmitWord(0x0000);
		_assembler.Mark(prevDone);
		_assembler.EmitWord(0x2004); // MOVE.L D4,D0 allocated size
		_assembler.EmitWord(0x2140); // MOVE.L D0,8(A0) allocated size
		_assembler.EmitWord(0x0008);
	}

	private void EmitManagedPoolUnlinkFreeBlock()
	{
		var prevPresent = UniqueLabel("gc_unlink_prev_present");
		var prevDone = UniqueLabel("gc_unlink_prev_done");
		var nextDone = UniqueLabel("gc_unlink_next_done");
		_assembler.EmitWord(0x2428); // MOVE.L (A0),D2 next
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2628); // MOVE.L 4(A0),D3 prev
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.NotEqual, prevPresent);
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
		EmitStoreD0ToLabel(GcFreeHeadLabel);
		_assembler.EmitBranch(M68kCondition.True, prevDone);
		_assembler.Mark(prevPresent);
		_assembler.EmitWord(0x2243); // MOVEA.L D3,A1
		_assembler.EmitWord(0x2342); // MOVE.L D2,(A1)
		_assembler.EmitWord(0x0000);
		_assembler.Mark(prevDone);
		_assembler.EmitWord(0x4A82); // TST.L D2
		_assembler.EmitBranch(M68kCondition.Equal, nextDone);
		_assembler.EmitWord(0x2242); // MOVEA.L D2,A1
		_assembler.EmitWord(0x2343); // MOVE.L D3,4(A1)
		_assembler.EmitWord(0x0004);
		_assembler.Mark(nextDone);
	}

	private void EmitManagedPoolLinkAllocatedBlock()
	{
		var oldDone = UniqueLabel("gc_link_alloc_old_done");
		EmitLoadD0FromLabel(GcAllocHeadLabel);
		EmitStoreD0ToA0Displacement(4, 0);
		_assembler.EmitWord(0x7200); // MOVEQ #0,D1
		_assembler.EmitWord(0x2141); // MOVE.L D1,4(A0)
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x7201); // MOVEQ #1,D1
		_assembler.EmitWord(0x2141); // MOVE.L D1,12(A0)
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A80); // TST.L D0 old head
		_assembler.EmitBranch(M68kCondition.Equal, oldDone);
		_assembler.EmitWord(0x2240); // MOVEA.L D0,A1
		_assembler.EmitWord(0x2348); // MOVE.L A0,4(A1)
		_assembler.EmitWord(0x0004);
		_assembler.Mark(oldDone);
		_assembler.EmitWord(0x2008); // MOVE.L A0,D0
		EmitStoreD0ToLabel(GcAllocHeadLabel);
		_assembler.EmitWord(0x2028); // MOVE.L 8(A0),D0 return total size
		_assembler.EmitWord(0x0008);
	}

	private void EmitManagedPoolDispose()
	{
		var done = UniqueLabel("gc_dispose_done");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeDisposeLabel);
		_assembler.EmitWord(0x206F); // MOVEA.L 4(A7),A0 slot
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2010); // MOVE.L (A0),D0 payload
		_assembler.EmitBranch(M68kCondition.Equal, done);
		EmitPushRegister(M68kRegister.D2);
		EmitPushRegister(M68kRegister.D3);
		_assembler.EmitWord(0x7200); // MOVEQ #0,D1
		_assembler.EmitWord(0x2081); // MOVE.L D1,(A0) clear slot
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0 payload
		_assembler.EmitWord(0x41E8); // LEA -16(A0),A0 header
		_assembler.EmitWord(unchecked((ushort)-GcBlockHeaderSize));
		EmitManagedPoolUnlinkAllocatedBlock();
		EmitManagedPoolLinkFreeBlock();
		EmitPopRegister(M68kRegister.D3);
		EmitPopRegister(M68kRegister.D2);
		_assembler.Mark(done);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolUnlinkAllocatedBlock()
	{
		var prevPresent = UniqueLabel("gc_unlink_alloc_prev_present");
		var prevDone = UniqueLabel("gc_unlink_alloc_prev_done");
		var nextDone = UniqueLabel("gc_unlink_alloc_next_done");
		_assembler.EmitWord(0x2428); // MOVE.L (A0),D2 next
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2628); // MOVE.L 4(A0),D3 prev
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.NotEqual, prevPresent);
		_assembler.EmitWord(0x2002); // MOVE.L D2,D0
		EmitStoreD0ToLabel(GcAllocHeadLabel);
		_assembler.EmitBranch(M68kCondition.True, prevDone);
		_assembler.Mark(prevPresent);
		_assembler.EmitWord(0x2243); // MOVEA.L D3,A1
		_assembler.EmitWord(0x2342); // MOVE.L D2,(A1)
		_assembler.EmitWord(0x0000);
		_assembler.Mark(prevDone);
		_assembler.EmitWord(0x4A82); // TST.L D2
		_assembler.EmitBranch(M68kCondition.Equal, nextDone);
		_assembler.EmitWord(0x2242); // MOVEA.L D2,A1
		_assembler.EmitWord(0x2343); // MOVE.L D3,4(A1)
		_assembler.EmitWord(0x0004);
		_assembler.Mark(nextDone);
	}

	private void EmitManagedPoolLinkFreeBlock()
	{
		var store = UniqueLabel("gc_link_free_store");
		EmitLoadD0FromLabel(GcFreeHeadLabel);
		EmitStoreD0ToA0Displacement(4, 0);
		_assembler.EmitWord(0x7200); // MOVEQ #0,D1
		_assembler.EmitWord(0x2141); // MOVE.L D1,4(A0)
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x2141); // MOVE.L D1,12(A0)
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A80); // TST.L D0 old free head
		_assembler.EmitBranch(M68kCondition.Equal, store);
		_assembler.EmitWord(0x2240); // MOVEA.L D0,A1
		_assembler.EmitWord(0x2348); // MOVE.L A0,4(A1)
		_assembler.EmitWord(0x0004);
		_assembler.Mark(store);
		_assembler.EmitWord(0x2008); // MOVE.L A0,D0
		EmitStoreD0ToLabel(GcFreeHeadLabel);
	}

	private void EmitManagedPoolMark()
	{
		var done = UniqueLabel("gc_mark_done");
		var alreadyMarked = UniqueLabel("gc_mark_already");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeMarkLabel);
		_assembler.EmitWord(0x202F); // MOVE.L 4(A7),D0 payload
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, done);
		_assembler.EmitWord(0x2200); // MOVE.L D0,D1 payload
		EmitLoadD0FromLabel(GcHeapStartLabel);
		_assembler.EmitWord(0x0680); // ADDI.L #header,D0 first payload
		_assembler.EmitLong(GcBlockHeaderSize);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarrySet, done);
		EmitLoadD0FromLabel(GcHeapEndLabel);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarryClear, done);
		_assembler.EmitWord(0x2001); // MOVE.L D1,D0 payload
		_assembler.EmitWord(0x2040); // MOVEA.L D0,A0 payload
		_assembler.EmitWord(0x41E8); // LEA -16(A0),A0 header
		_assembler.EmitWord(unchecked((ushort)-GcBlockHeaderSize));
		_assembler.EmitWord(0x2228); // MOVE.L 12(A0),D1 flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x2001); // MOVE.L D1,D0
		_assembler.EmitWord(0x0280); // ANDI.L #mark,D0
		_assembler.EmitLong(GcMarkFlag);
		_assembler.EmitBranch(M68kCondition.NotEqual, alreadyMarked);
		_assembler.EmitWord(0x0081); // ORI.L #mark,D1
		_assembler.EmitLong(GcMarkFlag);
		_assembler.EmitWord(0x2141); // MOVE.L D1,12(A0)
		_assembler.EmitWord(0x000C);
		_assembler.Mark(alreadyMarked);
		_assembler.Mark(done);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolCollect()
	{
		var traceRestart = UniqueLabel("gc_trace_restart");
		var traceLoop = UniqueLabel("gc_trace_loop");
		var traceNext = UniqueLabel("gc_trace_next");
		var tracePassDone = UniqueLabel("gc_trace_pass_done");
		var traceArray = UniqueLabel("gc_trace_array");
		var traceFields = UniqueLabel("gc_trace_fields");
		var traceFieldSkip = UniqueLabel("gc_trace_field_skip");
		var traceArrayLoop = UniqueLabel("gc_trace_array_loop");
		var traceScanned = UniqueLabel("gc_trace_scanned");
		var loop = UniqueLabel("gc_sweep_loop");
		var live = UniqueLabel("gc_sweep_live");
		var next = UniqueLabel("gc_sweep_next");
		var done = UniqueLabel("gc_sweep_done");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeCollectLabel);
		EmitPushRegisters(stackalloc[]
		{
			M68kRegister.D2,
			M68kRegister.D3,
			M68kRegister.D4,
			M68kRegister.A2
		});
		_assembler.Mark(traceRestart);
		_assembler.EmitWord(0x7800); // MOVEQ #0,D4 pass scanned count
		EmitLoadA0FromLabel(GcAllocHeadLabel);
		_assembler.Mark(traceLoop);
		_assembler.EmitWord(0x2208); // MOVE.L A0,D1
		_assembler.EmitBranch(M68kCondition.Equal, tracePassDone);
		_assembler.EmitWord(0x2428); // MOVE.L (A0),D2 next allocated
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0 flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x2600); // MOVE.L D0,D3
		_assembler.EmitWord(0x0283); // ANDI.L #mark,D3
		_assembler.EmitLong(GcMarkFlag);
		_assembler.EmitBranch(M68kCondition.Equal, traceNext);
		_assembler.EmitWord(0x2600); // MOVE.L D0,D3
		_assembler.EmitWord(0x0283); // ANDI.L #scanned,D3
		_assembler.EmitLong(GcScanFlag);
		_assembler.EmitBranch(M68kCondition.NotEqual, traceNext);
		_assembler.EmitWord(0x0080); // ORI.L #scanned,D0
		_assembler.EmitLong(GcScanFlag);
		EmitStoreD0ToA0Displacement(4, 12);
		_assembler.EmitWord(0x7801); // MOVEQ #1,D4 another pass may be needed
		_assembler.EmitWord(0x41E8); // LEA 16(A0),A0 payload
		_assembler.EmitWord(GcBlockHeaderSize);
		_assembler.EmitWord(0x2250); // MOVEA.L (A0),A1 descriptor
		_assembler.EmitWord(0x2629); // MOVE.L (A1),D3 descriptor object size
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.Equal, traceArray);
		_assembler.EmitWord(0x2629); // MOVE.L 4(A1),D3 reference bitmap
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x45E8); // LEA 8(A0),A2 first object field
		_assembler.EmitWord(0x0008);
		_assembler.Mark(traceFields);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.Equal, traceScanned);
		_assembler.EmitWord(0x2003); // MOVE.L D3,D0
		_assembler.EmitWord(0x0280); // ANDI.L #1,D0
		_assembler.EmitLong(1);
		_assembler.EmitBranch(M68kCondition.Equal, traceFieldSkip);
		_assembler.EmitWord(0x202A); // MOVE.L (A2),D0 field reference
		_assembler.EmitWord(0x0000);
		EmitPushD0();
		_assembler.EmitBsr(RuntimeMarkLabel);
		_loadedPlatformBase = null;
		EmitDiscardStackArguments(1);
		_assembler.Mark(traceFieldSkip);
		_assembler.EmitWord(0x588A); // ADDQ.L #4,A2
		_assembler.EmitWord(0xE28B); // LSR.L #1,D3
		_assembler.EmitBranch(M68kCondition.True, traceFields);
		_assembler.Mark(traceArray);
		_assembler.EmitWord(0x2629); // MOVE.L 4(A1),D3 reference-array flag
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.Equal, traceScanned);
		_assembler.EmitWord(0x2628); // MOVE.L 8(A0),D3 array length
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0x45E8); // LEA 12(A0),A2 first array element
		_assembler.EmitWord(0x000C);
		_assembler.Mark(traceArrayLoop);
		_assembler.EmitWord(0x4A83); // TST.L D3
		_assembler.EmitBranch(M68kCondition.Equal, traceScanned);
		_assembler.EmitWord(0x202A); // MOVE.L (A2),D0 element reference
		_assembler.EmitWord(0x0000);
		EmitPushD0();
		_assembler.EmitBsr(RuntimeMarkLabel);
		_loadedPlatformBase = null;
		EmitDiscardStackArguments(1);
		_assembler.EmitWord(0x588A); // ADDQ.L #4,A2
		_assembler.EmitWord(0x5383); // SUBQ.L #1,D3
		_assembler.EmitBranch(M68kCondition.True, traceArrayLoop);
		_assembler.Mark(traceScanned);
		_assembler.Mark(traceNext);
		_assembler.EmitWord(0x2042); // MOVEA.L D2,A0 next allocated
		_assembler.EmitBranch(M68kCondition.True, traceLoop);
		_assembler.Mark(tracePassDone);
		_assembler.EmitWord(0x4A84); // TST.L D4
		_assembler.EmitBranch(M68kCondition.NotEqual, traceRestart);
		EmitLoadA0FromLabel(GcAllocHeadLabel);
		_assembler.Mark(loop);
		_assembler.EmitWord(0x2208); // MOVE.L A0,D1
		_assembler.EmitBranch(M68kCondition.Equal, done);
		_assembler.EmitWord(0x2428); // MOVE.L (A0),D2 next allocated
		_assembler.EmitWord(0x0000);
		_assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0 flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x0280); // ANDI.L #mark,D0
		_assembler.EmitLong(GcMarkFlag);
		_assembler.EmitBranch(M68kCondition.NotEqual, live);
		EmitManagedPoolUnlinkAllocatedBlock();
		EmitManagedPoolLinkFreeBlock();
		_assembler.EmitBranch(M68kCondition.True, next);
		_assembler.Mark(live);
		_assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0 flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x0280); // ANDI.L #~(mark|scanned),D0
		_assembler.EmitLong(~(GcMarkFlag | GcScanFlag));
		EmitStoreD0ToA0Displacement(4, 12);
		_assembler.Mark(next);
		_assembler.EmitWord(0x2042); // MOVEA.L D2,A0 next allocated
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(done);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		EmitStoreD0ToLabel(GcStaleBytesLabel);
		EmitStoreD0ToLabel(GcStaleBlocksLabel);
		EmitPopRegisters(stackalloc[]
		{
			M68kRegister.D2,
			M68kRegister.D3,
			M68kRegister.D4,
			M68kRegister.A2
		});
		_assembler.EmitBranch(M68kCondition.True, RuntimeCoalesceLabel);
	}

	private void EmitManagedPoolCoalesce()
	{
		var loop = UniqueLabel("gc_collect_loop");
		var advance = UniqueLabel("gc_collect_advance");
		var done = UniqueLabel("gc_collect_done");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeCoalesceLabel);
		EmitPushRegisters(stackalloc[]
		{
			M68kRegister.D2,
			M68kRegister.D3,
			M68kRegister.D4,
			M68kRegister.A2
		});
		EmitLoadA0FromLabel(GcHeapStartLabel);
		_assembler.Mark(loop);
		_assembler.EmitWord(0x2208); // MOVE.L A0,D1
		_assembler.EmitBranch(M68kCondition.Equal, done);
		EmitLoadD0FromLabel(GcHeapEndLabel);
		_assembler.EmitWord(0xB280); // CMP.L D0,D1
		_assembler.EmitBranch(M68kCondition.CarryClear, done);
		_assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0 flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, advance);
		_assembler.EmitWord(0x2228); // MOVE.L 8(A0),D1 current size
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0x2408); // MOVE.L A0,D2
		_assembler.EmitWord(0xD481); // ADD.L D1,D2 next physical block
		EmitLoadD0FromLabel(GcHeapEndLabel);
		_assembler.EmitWord(0x2602); // MOVE.L D2,D3
		_assembler.EmitWord(0xB680); // CMP.L D0,D3
		_assembler.EmitBranch(M68kCondition.CarryClear, advance);
		_assembler.EmitWord(0x2242); // MOVEA.L D2,A1
		_assembler.EmitWord(0x2029); // MOVE.L 12(A1),D0 next flags
		_assembler.EmitWord(0x000C);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.NotEqual, advance);
		EmitMoveRegister(M68kRegister.A0, M68kRegister.A2);
		EmitMoveRegister(M68kRegister.A1, M68kRegister.D4);
		EmitMoveRegister(M68kRegister.A1, M68kRegister.A0);
		EmitManagedPoolUnlinkFreeBlock();
		EmitMoveRegister(M68kRegister.A2, M68kRegister.A0);
		EmitMoveRegister(M68kRegister.D4, M68kRegister.A1);
		_assembler.EmitWord(0x2228); // MOVE.L 8(A0),D1 current size
		_assembler.EmitWord(0x0008);
		EmitMoveRegister(M68kRegister.A1, M68kRegister.A0);
		_assembler.EmitWord(0x2428); // MOVE.L 8(A0),D2 next size
		_assembler.EmitWord(0x0008);
		EmitMoveRegister(M68kRegister.A2, M68kRegister.A0);
		_assembler.EmitWord(0x2001); // MOVE.L D1,D0
		_assembler.EmitWord(0xD082); // ADD.L D2,D0
		_assembler.EmitWord(0x2200); // MOVE.L D0,D1
		_assembler.EmitWord(0x2141); // MOVE.L D1,8(A0)
		_assembler.EmitWord(0x0008);
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(advance);
		_assembler.EmitWord(0x2228); // MOVE.L 8(A0),D1 current size
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0xD1C1); // ADDA.L D1,A0
		_assembler.EmitBranch(M68kCondition.True, loop);
		_assembler.Mark(done);
		EmitPopRegisters(stackalloc[]
		{
			M68kRegister.D2,
			M68kRegister.D3,
			M68kRegister.D4,
			M68kRegister.A2
		});
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolTelemetryGetters()
	{
		_assembler.AlignWord();
		_assembler.Mark(RuntimeGetStaleBytesLabel);
		EmitLoadD0FromLabel(GcStaleBytesLabel);
		_assembler.EmitWord(0x4E75); // RTS
		_assembler.AlignWord();
		_assembler.Mark(RuntimeGetStaleBlocksLabel);
		EmitLoadD0FromLabel(GcStaleBlocksLabel);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolShutdown()
	{
		_assembler.AlignWord();
		_assembler.Mark(RuntimeShutdownLabel);
		_assembler.EmitWord(0x7000); // MOVEQ #0,D0
		EmitStoreD0ToLabel(GcFreeHeadLabel);
		EmitStoreD0ToLabel(GcAllocHeadLabel);
		EmitStoreD0ToLabel(GcHeapStartLabel);
		EmitStoreD0ToLabel(GcHeapEndLabel);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolMarkRuntimeRoots()
	{
		var frameLoop = UniqueLabel("gc_roots_frame_loop");
		var staticStart = UniqueLabel("gc_roots_static_start");
		var rootLoop = UniqueLabel("gc_roots_root_loop");
		var nextFrame = UniqueLabel("gc_roots_next_frame");
		var staticLoop = UniqueLabel("gc_roots_static_loop");
		var done = UniqueLabel("gc_roots_done");

		_assembler.AlignWord();
		_assembler.Mark(RuntimeMarkRootsLabel);
		EmitPushRegisters(stackalloc[]
		{
			M68kRegister.D2,
			M68kRegister.D3,
			M68kRegister.A2,
			M68kRegister.A3
		});
		EmitMoveRegister(M68kRegister.A5, M68kRegister.A3);
		_assembler.Mark(frameLoop);
		EmitMoveRegister(M68kRegister.A3, M68kRegister.D0);
		_assembler.EmitWord(0x4A80); // TST.L D0
		_assembler.EmitBranch(M68kCondition.Equal, staticStart);
		_assembler.EmitWord(0x246B); // MOVEA.L 4(A3),A2 descriptor
		_assembler.EmitWord(0x0004);
		_assembler.EmitWord(0x241A); // MOVE.L (A2)+,D2 root count
		_assembler.Mark(rootLoop);
		_assembler.EmitWord(0x4A82); // TST.L D2
		_assembler.EmitBranch(M68kCondition.Equal, nextFrame);
		_assembler.EmitWord(0x261A); // MOVE.L (A2)+,D3 root offset
		_assembler.EmitWord(0x206B); // MOVEA.L 8(A3),A0 frame base
		_assembler.EmitWord(0x0008);
		_assembler.EmitWord(0xD1C3); // ADDA.L D3,A0
		_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		EmitPushD0();
		_assembler.EmitBsr(RuntimeMarkLabel);
		EmitDiscardStackArguments(1);
		_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
		_assembler.EmitBranch(M68kCondition.True, rootLoop);
		_assembler.Mark(nextFrame);
		_assembler.EmitWord(0x2653); // MOVEA.L (A3),A3 previous frame
		_assembler.EmitBranch(M68kCondition.True, frameLoop);

		_assembler.Mark(staticStart);
		_assembler.EmitWord(0x247C); // MOVEA.L #static-roots,A2
		_assembler.EmitAddress(GcStaticRootsLabel);
		_assembler.EmitWord(0x241A); // MOVE.L (A2)+,D2 root count
		_assembler.Mark(staticLoop);
		_assembler.EmitWord(0x4A82); // TST.L D2
		_assembler.EmitBranch(M68kCondition.Equal, done);
		_assembler.EmitWord(0x205A); // MOVEA.L (A2)+,A0 slot
		_assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		EmitPushD0();
		_assembler.EmitBsr(RuntimeMarkLabel);
		EmitDiscardStackArguments(1);
		_assembler.EmitWord(0x5382); // SUBQ.L #1,D2
		_assembler.EmitBranch(M68kCondition.True, staticLoop);
		_assembler.Mark(done);
		EmitPopRegisters(stackalloc[]
		{
			M68kRegister.D2,
			M68kRegister.D3,
			M68kRegister.A2,
			M68kRegister.A3
		});
		_assembler.EmitWord(0x4E75); // RTS
	}

	private const string GcConfigLabel = "runtime:gc-config";
	private const string GcHeapStartLabel = "runtime:gc-heap-start";
	private const string GcHeapEndLabel = "runtime:gc-heap-end";
	private const string GcFreeHeadLabel = "runtime:gc-free-head";
	private const string GcAllocHeadLabel = "runtime:gc-alloc-head";
	private const string GcStaleBytesLabel = "runtime:gc-stale-bytes";
	private const string GcStaleBlocksLabel = "runtime:gc-stale-blocks";
	private const string GcStaleBytesThresholdLabel = "runtime:gc-stale-bytes-threshold";
	private const string GcStaleBlocksThresholdLabel = "runtime:gc-stale-blocks-threshold";
	private const string GcStaticRootsLabel = "runtime:gc-static-roots";
	private const string RuntimeInitLabel = "__c68k_gc_init";
	private const string RuntimeAllocLabel = "__c68k_alloc";
	private const string RuntimeDisposeLabel = "__c68k_dispose";
	private const string RuntimeMarkLabel = "__c68k_gc_mark";
	private const string RuntimeMarkRootsLabel = "__c68k_gc_mark_roots";
	private const string RuntimeCollectLabel = "__c68k_gc_collect";
	private const string RuntimeCoalesceLabel = "__c68k_gc_coalesce";
	private const string RuntimeGetStaleBytesLabel = "__c68k_gc_get_stale_bytes";
	private const string RuntimeGetStaleBlocksLabel = "__c68k_gc_get_stale_blocks";
	private const string RuntimeShutdownLabel = "__c68k_gc_shutdown";
	private const int GcBlockHeaderSize = 16;
	private const int GcMinimumSplitSize = GcBlockHeaderSize + 4;
	private const uint GcMarkFlag = 2;
	private const uint GcScanFlag = 4;

}

