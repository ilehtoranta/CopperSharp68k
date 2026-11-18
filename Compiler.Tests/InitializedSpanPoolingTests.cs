/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Compiler.Tests.MultiModule;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void InitializedSpanPoolingPreservesObservableAddresses(M68kCpuTarget target, M68kCpuModel model)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		foreach (var (entry, copies) in new[] {
			(nameof(CompilerFixtures.PooledReadOnlySpanEntry), 1),
			(nameof(CompilerFixtures.ObservableReadOnlySpanEntry), 2)
		})
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = $"{typeof(CompilerFixtures).FullName}::{entry}",
				ManagedAssemblyPaths = [typeof(ExternalMethods).Assembly.Location],
				IncludedExportNames = [], Cpu = target, OutputFormat = M68kOutputFormat.Hunk,
				MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
				FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }
			});
			Assert.Equal(42u, Execute(CreateHunkBus(result), model, HunkLoadAddress + result.EntryPoint));

			AssertInitializedSpanPayloadCopies(result, Convert.FromHexString("1234A55A89ABCDEFC53B10EA"), copies);
		}
	}

	[Theory]
	[InlineData(false, 1)]
	[InlineData(true, 2)]
	public void ByteSpanPoolingRequiresTheViewToFitItsBackingField(bool oversized, int copies)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var bytes = Convert.FromHexString("156B85D02F8E277BCF");
		// Host reflection can keep the generated assembly open for the test process.
		var directory = Path.Combine(AppContext.BaseDirectory, "InitializedSpanProbes", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var assembly = InitializedSpanFixtureBuilder.CreateByteSpanPoolingProbe(directory, bytes, oversized);
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = assembly, EntryPoint = "ByteSpanPoolingProbe::Entry",
			IncludedExportNames = [], Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
			MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }
		});
		Assert.Equal(42u, Execute(CreateHunkBus(result), M68kCpuModel.M68000, HunkLoadAddress + result.EntryPoint));
		AssertInitializedSpanPayloadCopies(result, bytes, copies);
	}

	private static void AssertInitializedSpanPayloadCopies(M68kCompilationResult result, byte[] payload, int copies)
	{
		var remaining = result.Code.AsSpan();
		for (var copy = 0; copy < copies; copy++)
		{
			var offset = remaining.IndexOf(payload);
			Assert.True(offset >= 0, $"Missing copy {copy + 1} of the initialized span payload.");
			remaining = remaining[(offset + payload.Length)..];
		}
		Assert.Equal(-1, remaining.IndexOf(payload));
	}
}

public static partial class CompilerFixtures
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PooledReadOnlySpanEntry()
	{
		ReadOnlySpan<uint> data = [0x1234A55A, 0x89ABCDEF, 0xC53B10EA];
		return data.Length == 3 && data[1] == 0x89ABCDEF && ExternalMethods.ReadInitializedSpan(2) == 0xC53B10EA ? 42 : 1;
	}

	public static unsafe int ObservableReadOnlySpanEntry()
	{
		ReadOnlySpan<uint> data = [0x1234A55A, 0x89ABCDEF, 0xC53B10EA];
		var first = Unsafe.AsPointer(ref Unsafe.AsRef(in data[0]));
		var external = ExternalMethods.GetInitializedSpan();
		var second = Unsafe.AsPointer(ref Unsafe.AsRef(in external[0]));
		return first != second ? PooledReadOnlySpanEntry() : 2;
	}
}
