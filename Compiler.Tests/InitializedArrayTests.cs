/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public sealed class InitializedArrayTests
{
	[Theory]
	[InlineData(7, false, "Initialized field size does not match")]
	[InlineData(8, true, "interior control-flow entry")]
	public void InvalidArrayInitializerIsRejected(int bytes, bool interiorEntry, string message)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var directory = Path.Combine(AppContext.BaseDirectory, "InitializedArrayProbes", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("InitializedArrayProbe"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("InitializedArrayProbe");
		var type = module.DefineType("InitializedArrayProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var field = type.DefineInitializedData("Data", new byte[bytes], FieldAttributes.Public | FieldAttributes.Static);
		var method = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = method.GetILGenerator();
		il.Emit(OpCodes.Ldc_I4_2);
		if (interiorEntry)
		{
			var allocation = il.DefineLabel();
			il.Emit(OpCodes.Br_S, allocation);
			il.Emit(OpCodes.Ldc_I4_2);
			il.MarkLabel(allocation);
		}
		il.Emit(OpCodes.Newarr, typeof(int));
		il.Emit(OpCodes.Dup);
		il.Emit(OpCodes.Ldtoken, field);
		il.Emit(OpCodes.Call, typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.InitializeArray))!);
		il.Emit(OpCodes.Pop);
		il.Emit(OpCodes.Ldc_I4, 42);
		il.Emit(OpCodes.Ret);
		type.CreateType();
		var path = Path.Combine(directory, "InitializedArrayProbe.dll");
		assembly.Save(path);
		var error = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(new M68kCompilationRequest {
			AssemblyPath = path, EntryPoint = "InitializedArrayProbe::Entry", Cpu = M68kCpuTarget.M68020,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		}));
		Assert.True(error.Message.Contains(message, StringComparison.Ordinal), error.Message);
	}
}

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void InitializedPrimitiveArraysPreserveByteOrder(M68kCpuTarget target, Copper68k.M68kCpuModel model)
	{
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::InitializedPrimitiveArraysEntry",
				IncludedExportNames = [], Cpu = target, OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 0x8000 }
			});
			Assert.Equal(42u, Execute(CreateHunkBus(result), model, HunkLoadAddress + result.EntryPoint, initialStackPointer: 0x0020_0000));
		}
	}
}

public static partial class CompilerFixtures
{
	public static int InitializedPrimitiveArraysEntry()
	{
		var bytes = new byte[] { 1, 0x80, 0xFE, 0xFF };
		var shorts = new short[] { 0x1234, -2, short.MinValue, short.MaxValue };
		var integers = new int[] { 0x12345678, -2, int.MinValue, int.MaxValue };
		var longs = new long[] { 0x123456789ABCDEF, -2, long.MinValue, long.MaxValue };
		return bytes[0] == 1 && bytes[1] == 0x80 && bytes[2] == 0xFE && bytes[3] == 0xFF &&
			shorts[0] == 0x1234 && shorts[1] == -2 && shorts[2] == short.MinValue && shorts[3] == short.MaxValue &&
			integers[0] == 0x12345678 && integers[1] == -2 && integers[2] == int.MinValue && integers[3] == int.MaxValue &&
			longs[0] == 0x123456789ABCDEF && longs[1] == -2 && longs[2] == long.MinValue && longs[3] == long.MaxValue ? 42 : 1;
	}
}
