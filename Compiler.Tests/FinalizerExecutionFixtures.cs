/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class FinalizerExecutionFixtures
{
	private const uint ShutdownSignalAddress = 0x0000_3000;
	private static uint _count;
	private static uint _observed;
	private static ResurrectingFinalizable? _resurrected;
	private static CountingFinalizable? _root;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint FinalizesUnreachableEntry()
	{
		_count = 0;
		AllocateCounting();
		GC.Collect();
		GC.WaitForPendingFinalizers();
		return _count;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PreservesFinalizerGraphEntry()
	{
		_observed = 0;
		AllocateGraph();
		GC.Collect();
		return _observed;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SuppressesFinalizerEntry()
	{
		_count = 0;
		AllocateSuppressed();
		GC.Collect();
		return _count;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CountsReregistrationEntry()
	{
		_count = 0;
		AllocateReregistered();
		GC.Collect();
		return _count;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ResurrectsAndFinalizesAgainEntry()
	{
		_count = 0;
		_resurrected = null;
		AllocateResurrecting();
		GC.Collect();
		if (_count != 1 || _resurrected is null)
		{
			return 0;
		}
		_resurrected = null;
		GC.Collect();
		return _count;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NestedCollectionEntry()
	{
		_count = 0;
		AllocateNestedPair();
		GC.Collect();
		return _count;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ThrowingFinalizerDoesNotStopDrainEntry()
	{
		_count = 0;
		AllocateThrowingPair();
		GC.Collect();
		return _count;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AllocationPressureSecondPassEntry()
	{
		_count = 0;
		AllocateCounting();
		var result = new uint[1];
		result[0] = 41;
		return _count + result[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint RepeatedSuppressionConsumesOneRegistrationEntry()
	{
		_count = 0;
		var value = new CountingFinalizable(1);
		GC.ReRegisterForFinalize(value);
		GC.SuppressFinalize(value);
		GC.SuppressFinalize(value);
		value = null;
		GC.Collect();
		return _count;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NullFinalizerControlsThrowEntry()
	{
		uint result = 0;
		try
		{
			GC.SuppressFinalize(null!);
		}
		catch (ArgumentNullException)
		{
			result++;
		}
		try
		{
			GC.ReRegisterForFinalize(null!);
		}
		catch (ArgumentNullException)
		{
			result++;
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NonFinalizableControlsAreNoOpsEntry()
	{
		var value = new FinalizerChild();
		GC.SuppressFinalize(value);
		GC.ReRegisterForFinalize(value);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ExplicitFreeSkipsFinalizerEntry()
	{
		_count = 0;
		object? value = new CountingFinalizable(1);
		M68kRuntime.DisposeObject(ref value);
		GC.Collect();
		return value is null ? 42 + _count : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint RootedObjectWaitsUntilExitEntry()
	{
		M68kAddress.WriteUInt32(M68kAddress.FromUInt32(ShutdownSignalAddress), 0, 0);
		_root = new CountingFinalizable(0, writeShutdownSignal: true);
		GC.Collect();
		return _count;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SuppressedRootDoesNotFinalizeAtExitEntry()
	{
		M68kAddress.WriteUInt32(M68kAddress.FromUInt32(ShutdownSignalAddress), 0, 0);
		_root = new CountingFinalizable(0, writeShutdownSignal: true);
		GC.SuppressFinalize(_root);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AllocateCounting() =>
		_ = new CountingFinalizable(1);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AllocateGraph() =>
		_ = new GraphFinalizable(new FinalizerChild { Value = 42 });

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AllocateSuppressed()
	{
		var value = new CountingFinalizable(1);
		GC.SuppressFinalize(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AllocateReregistered()
	{
		var value = new CountingFinalizable(1);
		GC.ReRegisterForFinalize(value);
		GC.ReRegisterForFinalize(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AllocateResurrecting() =>
		_ = new ResurrectingFinalizable();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AllocateNestedPair()
	{
		_ = new CountingFinalizable(1);
		_ = new NestedCollectFinalizable();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AllocateThrowingPair()
	{
		_ = new CountingFinalizable(1);
		_ = new ThrowingFinalizable();
	}

	private sealed class CountingFinalizable
	{
		private readonly uint _increment;
		private readonly bool _writeShutdownSignal;

		public CountingFinalizable(uint increment, bool writeShutdownSignal = false)
		{
			_increment = increment;
			_writeShutdownSignal = writeShutdownSignal;
		}

		~CountingFinalizable()
		{
			_count += _increment;
			if (_writeShutdownSignal)
			{
				M68kAddress.WriteUInt32(
					M68kAddress.FromUInt32(ShutdownSignalAddress),
					0,
					42);
			}
		}
	}

	private sealed class FinalizerChild
	{
		public uint Value;
	}

	private sealed class GraphFinalizable(FinalizerChild child)
	{
		private readonly FinalizerChild _child = child;

		~GraphFinalizable()
		{
			_observed = _child.Value;
		}
	}

	private sealed class ResurrectingFinalizable
	{
		~ResurrectingFinalizable()
		{
			_count++;
			if (_count == 1)
			{
				_resurrected = this;
				GC.ReRegisterForFinalize(this);
			}
		}
	}

	private sealed class NestedCollectFinalizable
	{
		~NestedCollectFinalizable()
		{
			_count++;
			_ = new uint[1];
			GC.Collect();
		}
	}

	private sealed class ThrowingFinalizable
	{
		~ThrowingFinalizable()
		{
			_count++;
			M68kRuntime.ThrowInvalidOperationException();
		}
	}
}
