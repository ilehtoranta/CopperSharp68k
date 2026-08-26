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
			.ThenBy(
				static field => field.ConstructedDeclaringType?.DisplayName,
				StringComparer.Ordinal)
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
		var usesManagedLifecycle = _managedLifecycles.Count != 0;
		var wrapsEntry = usesManagedRuntime || usesManagedLifecycle;
		_assembler.AlignWord();
		_assembler.Mark(label);
		if (UsesAmigaUnhandledExceptionRequester)
		{
			_assembler.EmitWord(0x23CF); // MOVE.L A7,initial-stack
			_assembler.EmitAddress(RuntimeInitialStackLabel);
		}
		var returnType = entry.Signature.ReturnType;
		var preservesScalarResult =
			!returnType.IsVoid &&
			!Is64BitScalar(returnType) &&
			!IsInternalAddressReturn(returnType);
		var preservesAddressResult = IsInternalAddressReturn(returnType);
		var preservesWideResult = Is64BitScalar(returnType);
		var needsD2 = usesAmigaStartupArguments ||
			preservesScalarResult ||
			preservesWideResult;
		var needsD3 = preservesWideResult;
		var needsA2 = usesAmigaStartupArguments || preservesAddressResult;
		if (wrapsEntry)
		{
			// D0/D1/A0/A1 are volatile in the private ABI. Only the result
			// registers that the entry method actually uses need a callee-saved
			// temporary while target and runtime shutdown hooks run.
			if (needsD2)
			{
				EmitPushRegister(M68kRegister.D2);
			}
			if (needsD3)
			{
				EmitPushRegister(M68kRegister.D3);
			}
			if (needsA2)
			{
				EmitPushRegister(M68kRegister.A2);
			}
		}
		if (wrapsEntry && usesAmigaStartupArguments)
		{
			EmitMoveRegister(M68kRegister.D0, M68kRegister.D2);
			EmitMoveRegister(M68kRegister.A0, M68kRegister.A2);
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
		}
		EmitManagedLifecycleInitialize();
		if (wrapsEntry)
		{
			if (usesAmigaStartupArguments)
			{
				EmitMoveRegister(M68kRegister.D2, M68kRegister.D0);
				EmitMoveRegister(M68kRegister.A2, M68kRegister.A0);
			}
			_assembler.EmitCall(MethodLabel(entry));
			_loadedPlatformBase = null;
			if (preservesScalarResult)
			{
				EmitMoveRegister(M68kRegister.D0, M68kRegister.D2);
			}
			else if (preservesAddressResult)
			{
				EmitMoveRegister(M68kRegister.A0, M68kRegister.A2);
			}
			else if (preservesWideResult)
			{
				EmitMoveRegister(M68kRegister.D0, M68kRegister.D2);
				EmitMoveRegister(M68kRegister.D1, M68kRegister.D3);
			}
			if (_usesFinalizers && _managedPoolRuntime is { } finalizerRuntime)
			{
				_assembler.EmitCall(MethodLabel(finalizerRuntime.PrepareShutdownFinalizers));
				_assembler.EmitCall(MethodLabel(finalizerRuntime.DrainFinalizers));
				_loadedPlatformBase = null;
			}
			EmitManagedLifecycleShutdown();
			if (usesManagedRuntime)
			{
				EmitRuntimeJsr(RuntimeShutdownTarget, M68kRuntimeImports.GcShutdown);
				_loadedPlatformBase = null;
			}
			if (preservesScalarResult)
			{
				EmitMoveRegister(M68kRegister.D2, M68kRegister.D0);
			}
			else if (preservesAddressResult)
			{
				EmitMoveRegister(M68kRegister.A2, M68kRegister.A0);
			}
			else if (preservesWideResult)
			{
				EmitMoveRegister(M68kRegister.D2, M68kRegister.D0);
				EmitMoveRegister(M68kRegister.D3, M68kRegister.D1);
			}
			if (needsA2)
			{
				EmitPopRegister(M68kRegister.A2);
			}
			if (needsD3)
			{
				EmitPopRegister(M68kRegister.D3);
			}
			if (needsD2)
			{
				EmitPopRegister(M68kRegister.D2);
			}
			_assembler.EmitWord(0x4E75); // RTS
			return label;
		}

		if (usesExceptionRuntime)
		{
			_assembler.EmitCall(MethodLabel(entry));
			_loadedPlatformBase = null;
			_assembler.EmitWord(0x4E75); // RTS
			return label;
		}

		return label;
	}

	private void EmitManagedLifecycleInitialize()
	{
		foreach (var lifecycle in _managedLifecycles)
		{
			_assembler.EmitCall(MethodLabel(lifecycle.Initialize));
			_loadedPlatformBase = null;
		}
	}

	private void EmitManagedLifecycleShutdown()
	{
		for (var index = _managedLifecycles.Count - 1; index >= 0; index--)
		{
			_assembler.EmitCall(MethodLabel(_managedLifecycles[index].Shutdown));
			_loadedPlatformBase = null;
		}
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

		if (_loadedPlatformBase?.Label != ExecBaseSlotSymbol)
		{
			EmitLoadAddressRegisterLocal(M68kRegister.A6, ExecBaseSlotSymbol);
		}
		_assembler.EmitWord(0x203C); // MOVE.L #heap-size,D0
		_assembler.EmitLong(_request.Heap.Size);
		_assembler.EmitWord(0x7200); // MOVEQ #0,D1, no MEMF_CLEAR
		_assembler.EmitWord(0x4EAE); // JSR -198(A6)
		_assembler.EmitWord(unchecked((ushort)-198));
		EmitStoreD0DirectToLabel(GcArenaBaseLabel);
		EmitStoreD0DirectToLabel(GcConfigHeapStartLabel);
		_loadedPlatformBase = null;
	}

	private void EmitManagedPoolRuntime()
	{
		if (!UsesBuiltInManagedPool)
		{
			if (_request.Imports.ContainsKey(M68kRuntimeImports.GcCollect))
			{
				EmitExternalCollectWithRootsAdapter();
			}
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
		_assembler.EmitCall(MethodLabel(runtime.Allocate));
		_assembler.EmitWord(0x4E75); // RTS

		_assembler.AlignWord();
		_assembler.Mark(RuntimeDisposeLabel);
		_assembler.EmitWord(0x2008); // MOVE.L A0,D0
		_assembler.EmitCall(MethodLabel(runtime.Dispose));
		_assembler.EmitWord(0x4E75); // RTS

		if (_usesFinalizers)
		{
			_assembler.AlignWord();
			_assembler.Mark(RuntimeRegisterFinalizerLabel);
			_assembler.EmitCall(MethodLabel(runtime.RegisterFinalizer));
			_assembler.EmitWord(0x4E75); // RTS

			_assembler.AlignWord();
			_assembler.Mark(RuntimeSuppressFinalizerLabel);
			_assembler.EmitCall(MethodLabel(runtime.SuppressFinalizer));
			_assembler.EmitWord(0x4E75); // RTS

			_assembler.AlignWord();
			_assembler.Mark(RuntimeReRegisterFinalizerLabel);
			_assembler.EmitCall(MethodLabel(runtime.ReRegisterFinalizer));
			_assembler.EmitWord(0x4E75); // RTS
		}

		_assembler.AlignWord();
		_assembler.Mark(RuntimeMarkLabel);
		_assembler.EmitWord(0x202F); // MOVE.L 4(A7),D0
		_assembler.EmitWord(0x0004);
		_assembler.EmitCall(MethodLabel(runtime.Mark));
		_assembler.EmitWord(0x4E75); // RTS

		_assembler.AlignWord();
		_assembler.Mark(RuntimeMarkRootsLabel);
		EmitRootWalkArguments();
		if (UsesExtendedUnwindMetadata)
		{
			EmitPushRegister(M68kRegister.A5);
		}
		EmitPushRegister(M68kRegister.A1);
		EmitPushRegister(M68kRegister.A0);
		_assembler.EmitCall(MethodLabel(
			UsesExtendedUnwindMetadata ? runtime.MarkRootsExtended : runtime.MarkRoots));
		EmitDiscardStackArguments(UsesExtendedUnwindMetadata ? 3 : 2);
		_assembler.EmitWord(0x4E75); // RTS

		_assembler.AlignWord();
		_assembler.Mark(RuntimeCollectWithRootsLabel);
		EmitRootWalkArguments();
		if (UsesExtendedUnwindMetadata)
		{
			EmitPushRegister(M68kRegister.A5);
		}
		EmitPushRegister(M68kRegister.A1);
		EmitPushRegister(M68kRegister.A0);
		_assembler.EmitCall(MethodLabel(
			UsesExtendedUnwindMetadata
				? _usesFinalizers
					? runtime.CollectFinalizableWithRootsExtended
					: runtime.CollectWithRootsExtended
				: _usesFinalizers
					? runtime.CollectFinalizableWithRoots
					: runtime.CollectWithRoots));
		EmitDiscardStackArguments(UsesExtendedUnwindMetadata ? 3 : 2);
		if (_usesFinalizers)
		{
			_assembler.EmitCall(MethodLabel(runtime.DrainFinalizers));
		}
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitExternalCollectWithRootsAdapter()
	{
		_assembler.AlignWord();
		_assembler.Mark(RuntimeCollectWithRootsLabel);
		EmitRootWalkArguments();
		if (UsesExtendedUnwindMetadata)
		{
			_assembler.EmitWord(0x244D); // MOVEA.L A5,A2 current frame anchor
		}
		_assembler.EmitJsr(M68kRuntimeImports.GcCollect, external: true);
		_assembler.EmitWord(0x4E75); // RTS
	}

	private void EmitRootWalkArguments()
	{
		_assembler.EmitWord(0x200F); // MOVE.L A7,D0 cursor (return-address slot)
		_assembler.EmitWord(0x2217); // MOVE.L (A7),D1 resume PC
		EmitAddressImmediateToRegister(M68kRegister.A0, MethodTableLabel);
		EmitAddressImmediateToRegister(M68kRegister.A1, GcStaticRootsLabel);
	}

	private void EmitManagedPoolShutdown(ManagedPoolRuntimeModule runtime)
	{
		var done = UniqueLabel("gc_shutdown_done");
		_assembler.AlignWord();
		_assembler.Mark(RuntimeShutdownLabel);
		_assembler.EmitCall(MethodLabel(runtime.Shutdown));
		if (UsesAmigaManagedPoolArena)
		{
			EmitLoadA0FromLabel(GcArenaBaseLabel);
			_assembler.EmitWord(0x2008); // MOVE.L A0,D0
			_assembler.EmitBranch(M68kCondition.Equal, done);
			_assembler.EmitWord(0x2248); // MOVEA.L A0,A1
			_assembler.EmitWord(0x2039); // MOVE.L heap-size,D0
			_assembler.EmitAddress(GcConfigHeapSizeLabel);
			EmitLoadAddressRegisterLocal(M68kRegister.A6, ExecBaseSlotSymbol);
			_assembler.EmitWord(0x4EAE); // JSR -210(A6)
			_assembler.EmitWord(unchecked((ushort)-210));
			_assembler.EmitWord(0x7000); // MOVEQ #0,D0
			EmitStoreD0DirectToLabel(GcArenaBaseLabel);
			EmitStoreD0DirectToLabel(GcConfigHeapStartLabel);
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
	private const string RuntimeRegisterFinalizerLabel = "__c68k_gc_register_finalizer";
	private const string RuntimeSuppressFinalizerLabel = "__c68k_gc_suppress_finalizer";
	private const string RuntimeReRegisterFinalizerLabel = "__c68k_gc_reregister_finalizer";
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
