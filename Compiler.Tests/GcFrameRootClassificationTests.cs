/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Runtime.CompilerServices;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class GcFrameRootClassificationTests
{
	[Fact]
	public void ByrefHomesTransportOwnersWithoutPublishingInteriorAddressesAsObjects()
	{
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location);
		var method = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::ManagedByrefFrameHomeProbe");
		var function = CilMachineIrBuilder.Build(method, module);
		var byrefs = function.LocalHomes.Values.Where(home => method.Locals[home.Index].Kind == CilTypeKind.ManagedPointer).ToArray();
		Assert.NotEmpty(byrefs);
		Assert.All(byrefs, home => { Assert.False(home.IsGcReference); Assert.Empty(home.GcReferenceOffsets!); });
		Assert.True(function.ArgumentHomes[1].IsGcReference);
	}
}

public static partial class CompilerFixtures
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CollectInNormalFinally(int[] owner)
	{
		try { return owner[1]; }
		finally { GC.Collect(); GC.KeepAlive(owner); }
	}

	public static int NormalFinallyCollectionPreservesPayloadEntry()
	{
		var owner = new int[2]; owner[0] = 0x00350031; owner[1] = 42;
		return CollectInNormalFinally(owner) == 42 && owner[0] == 0x00350031 ? 42 : 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static object FrameRootIdentity(object owner) => owner;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ManagedByrefFrameHomeProbe(ref int value, object owner)
	{
		ref var current = ref value;
		try
		{
			owner = FrameRootIdentity(owner);
			GC.Collect();
			return current;
		}
		finally { GC.KeepAlive(owner); }
	}

	public static int ManagedByrefFrameHomesPreservePayloadEntry()
	{
		var values = new int[2]; values[0] = 0x00350031; values[1] = 42;
		var result = ManagedByrefFrameHomeProbe(ref values[1], values);
		return result == 42 && values[0] == 0x00350031 ? 42 : 1;
	}
}
