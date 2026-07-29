/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

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
		_assembler.Mark(GcConfigHeapStartLabel);
		_assembler.EmitLong(UsesAmigaManagedPoolArena ? 0 : _request.Heap.StartAddress);
		_assembler.Mark(GcConfigHeapSizeLabel);
		_assembler.EmitLong(_request.Heap.Size);
		_assembler.EmitLong(_request.GcTelemetry.StaleBytesThreshold);
		_assembler.EmitLong(_request.GcTelemetry.StaleBlocksThreshold);
		_assembler.EmitLong(_request.GcTelemetry.IntervalTicks);
	}

	private void EmitManagedPoolRuntimeData()
	{
		_assembler.AlignWord();
		if (UsesAmigaManagedPoolArena)
		{
			_assembler.Mark(GcArenaBaseLabel);
			_assembler.EmitLong(0);
		}

		_assembler.Mark(GcStaticRootsLabel);
		var staticRoots = _staticFields.Values
			.Where(static field => field.IsStatic && field.Type.IsReference)
			.OrderBy(static field =>
				System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(field.Handle))
			.ToArray();
		_assembler.EmitLong((uint)staticRoots.Length);
		foreach (var field in staticRoots)
		{
			_assembler.EmitAddress(StaticFieldLabel(field));
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
		if (UsesAmigaUnhandledExceptionRequester)
		{
			_assembler.EmitWord(0x23CF); // MOVE.L A7,initial-stack
			_assembler.EmitAddress(RuntimeInitialStackLabel);
		}
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
			EmitAllocateAmigaManagedPoolArena();
			if (UsesBuiltInManagedPool)
			{
				EmitAddressImmediateToRegister(M68kRegister.D0, GcConfigLabel);
				_assembler.EmitBsr(RuntimeInitLabel);
			}
			else
			{
				_assembler.EmitWord(0x2F3C); // MOVE.L #gc-config,-(A7)
				_assembler.EmitAddress(GcConfigLabel);
				EmitRuntimeJsr(RuntimeInitLabel, M68kRuntimeImports.GcInit);
				EmitDiscardStackArguments(1);
			}
			_loadedPlatformBase = null;
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
			EmitRuntimeJsr(RuntimeShutdownTarget, M68kRuntimeImports.GcShutdown);
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

	private void EmitAllocateAmigaManagedPoolArena()
	{
		if (!UsesAmigaManagedPoolArena)
		{
			return;
		}

		EmitLoadAddressRegisterPcRelative(M68kRegister.A6, ExecBaseSlotSymbol);
		_assembler.EmitWord(0x203C); // MOVE.L #heap-size,D0
		_assembler.EmitLong(_request.Heap.Size);
		_assembler.EmitWord(0x7200); // MOVEQ #0,D1, no MEMF_CLEAR
		_assembler.EmitWord(0x4EAE); // JSR -198(A6)
		_assembler.EmitWord(unchecked((ushort)-198));
		EmitStoreD0ToLabel(GcArenaBaseLabel);
		EmitStoreD0ToLabel(GcConfigHeapStartLabel);
		_loadedPlatformBase = null;
	}

	private void EmitManagedPoolRuntime()
	{
		if (!UsesBuiltInManagedPool)
		{
			return;
		}

		if (_managedPoolRuntime is not { } runtime)
		{
			throw new InvalidOperationException(
				"ManagedPoolMarkSweepGc requires CopperSharp.Runtime.Managed.");
		}

		EmitManagedPoolAllocationAdapters(runtime);
		if (UsesAmigaManagedPoolArena)
		{
			EmitManagedPoolShutdown(runtime);
		}
	}

	private void EmitManagedPoolAllocationAdapters(ManagedPoolRuntimeModule runtime)
	{
		_assembler.AlignWord();
		_assembler.Mark(RuntimeAllocLabel);
		_assembler.EmitBsr(MethodLabel(runtime.Allocate));
		_assembler.EmitWord(0x4E75); // RTS

		_assembler.AlignWord();
		_assembler.Mark(RuntimeDisposeLabel);
		_assembler.EmitWord(0x2008); // MOVE.L A0,D0
		_assembler.EmitBsr(MethodLabel(runtime.Dispose));
		_assembler.EmitWord(0x4E75); // RTS

		_assembler.AlignWord();
		_assembler.Mark(RuntimeMarkLabel);
		_assembler.EmitWord(0x202F); // MOVE.L 4(A7),D0
		_assembler.EmitWord(0x0004);
		_assembler.EmitBsr(MethodLabel(runtime.Mark));
		_assembler.EmitWord(0x4E75); // RTS

		_assembler.AlignWord();
		_assembler.Mark(RuntimeMarkRootsLabel);
		_assembler.EmitWord(0x200D); // MOVE.L A5,D0
		EmitAddressImmediateToRegister(M68kRegister.D1, GcStaticRootsLabel);
		_assembler.EmitBsr(MethodLabel(runtime.MarkRoots));
		_assembler.EmitWord(0x4E75); // RTS

		_assembler.AlignWord();
		_assembler.Mark(RuntimeCollectWithRootsLabel);
		_assembler.EmitWord(0x200D); // MOVE.L A5,D0
		EmitAddressImmediateToRegister(M68kRegister.D1, GcStaticRootsLabel);
		_assembler.EmitBsr(MethodLabel(runtime.CollectWithRoots));
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitManagedPoolShutdown(ManagedPoolRuntimeModule runtime)
	{
		var done = UniqueLabel("gc_shutdown_done");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeShutdownLabel);
		_assembler.EmitBsr(MethodLabel(runtime.Shutdown));
		if (UsesAmigaManagedPoolArena)
		{
			EmitLoadA0FromLabel(GcArenaBaseLabel);
			_assembler.EmitWord(0x2008); // MOVE.L A0,D0
			_assembler.EmitBranch(M68kCondition.Equal, done);
			_assembler.EmitWord(0x2248); // MOVEA.L A0,A1
			_assembler.EmitWord(0x2039); // MOVE.L heap-size,D0
			_assembler.EmitAddress(GcConfigHeapSizeLabel);
			EmitLoadAddressRegisterPcRelative(M68kRegister.A6, ExecBaseSlotSymbol);
			_assembler.EmitWord(0x4EAE); // JSR -210(A6)
			_assembler.EmitWord(unchecked((ushort)-210));
			_assembler.EmitWord(0x7000); // MOVEQ #0,D0
			EmitStoreD0ToLabel(GcArenaBaseLabel);
			EmitStoreD0ToLabel(GcConfigHeapStartLabel);
			_assembler.Mark(done);
		}
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EnsureManagedPoolExecBase()
	{
		if (!UsesAmigaManagedPoolArena)
		{
			return;
		}

		GetOrAddPlatformBase(
			new M68kExternalCallConvention(
				"exec.library",
				M68kExternalBaseSource.WritableSlot,
				M68kRegister.A6,
				0,
				SourceAddress: 4,
				SlotSymbol: ExecBaseSlotSymbol),
			_managedPoolRuntime?.Initialize ??
				throw new InvalidOperationException(
					"ManagedPoolMarkSweepGc requires CopperSharp.Runtime.Managed."));
	}

	private const string GcConfigLabel = "runtime:gc-config";
	private const string ExecBaseSlotSymbol = "_ExecBase";
	private const string GcConfigHeapStartLabel = "runtime:gc-config-heap-start";
	private const string GcConfigHeapSizeLabel = "runtime:gc-config-heap-size";
	private const string GcArenaBaseLabel = "runtime:gc-arena-base";
	private string GcStaleBytesLabel => ManagedPoolStateLabel(
		_managedPoolRuntime?.StaleBytes,
		"runtime:gc-stale-bytes");
	private string GcStaleBlocksLabel => ManagedPoolStateLabel(
		_managedPoolRuntime?.StaleBlocks,
		"runtime:gc-stale-blocks");
	private string GcStaleBytesThresholdLabel => ManagedPoolStateLabel(
		_managedPoolRuntime?.StaleBytesThreshold,
		"runtime:gc-stale-bytes-threshold");
	private string GcStaleBlocksThresholdLabel => ManagedPoolStateLabel(
		_managedPoolRuntime?.StaleBlocksThreshold,
		"runtime:gc-stale-blocks-threshold");
	private const string GcStaticRootsLabel = "runtime:gc-static-roots";
	private const string RuntimeInitLabel = "__c68k_gc_init";
	private const string RuntimeAllocLabel = "__c68k_alloc";
	private const string RuntimeDisposeLabel = "__c68k_dispose";
	private const string RuntimeMarkLabel = "__c68k_gc_mark";
	private const string RuntimeMarkRootsLabel = "__c68k_gc_mark_roots";
	private const string RuntimeCollectWithRootsLabel = "__c68k_gc_collect_with_roots";
	private const string RuntimeCollectLabel = "__c68k_gc_collect";
	private const string RuntimeCoalesceLabel = "__c68k_gc_coalesce";
	private const string RuntimeGetStaleBytesLabel = "__c68k_gc_get_stale_bytes";
	private const string RuntimeGetStaleBlocksLabel = "__c68k_gc_get_stale_blocks";
	private const string RuntimeShutdownLabel = "__c68k_gc_shutdown";

	private string RuntimeShutdownTarget => RuntimeShutdownLabel;

	private string RuntimeGetStaleBytesTarget => RuntimeGetStaleBytesLabel;

	private string RuntimeGetStaleBlocksTarget => RuntimeGetStaleBlocksLabel;

	private string? ManagedRuntimeAlias(CilMethod method)
	{
		if (_managedPoolRuntime is not { } runtime)
		{
			return null;
		}
		if (method.Identity == runtime.Initialize.Identity)
		{
			return RuntimeInitLabel;
		}
		if (method.Identity == runtime.GetStaleBytes.Identity)
		{
			return RuntimeGetStaleBytesLabel;
		}
		if (method.Identity == runtime.GetStaleBlocks.Identity)
		{
			return RuntimeGetStaleBlocksLabel;
		}
		if (method.Identity == runtime.Shutdown.Identity && !UsesAmigaManagedPoolArena)
		{
			return RuntimeShutdownLabel;
		}
		if (method.Identity == runtime.Collect.Identity)
		{
			return RuntimeCollectLabel;
		}
		if (method.Identity == runtime.Coalesce.Identity)
		{
			return RuntimeCoalesceLabel;
		}
		return null;
	}

	private string ManagedPoolStateLabel(CilField? field, string fallback) =>
		field is null ? fallback : StaticFieldLabel(field);
}
