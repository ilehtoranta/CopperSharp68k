/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Tests;

public sealed class FinalizerDiagnosticsTests
{
	private const string FixtureType =
		"CopperSharp.Compiler.Tests.FinalizerDiagnosticsFixtures";
	private static readonly string FixtureAssembly =
		typeof(FinalizerDiagnosticsFixtures).Assembly.Location;

	[Theory]
	[InlineData(M68kMemoryManagement.None)]
	[InlineData(M68kMemoryManagement.ExternalAllocator)]
	[InlineData(M68kMemoryManagement.ExecPoolMarkSweepGc)]
	public void ReachableFinalizerAllocationIsRejected(
		M68kMemoryManagement memoryManagement)
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			Compile("DirectFinalizerEntry", memoryManagement));

		Assert.Equal(M68kDiagnosticIds.StaticAnalysis, exception.DiagnosticId);
		Assert.Contains("finalizable type", exception.Message, StringComparison.Ordinal);
		Assert.Contains("DirectFinalizableFixture::Finalize", exception.Message,
			StringComparison.Ordinal);
		Assert.Contains("Dispose/try-finally", exception.Message, StringComparison.Ordinal);
		Assert.Equal($"{FixtureType}::DirectFinalizerEntry", exception.Method);
		Assert.NotNull(exception.IlOffset);
	}

	[Fact]
	public void BumpAllocatorStillRejectsFinalizerProgram() =>
		Assert.Throws<M68kCompilationException>(() =>
			Compile("DirectFinalizerEntry", M68kMemoryManagement.BumpAllocator));

	[Fact]
	public void ManagedPoolWithFullExceptionsAcceptsFinalizerAllocation()
	{
		var result = Compile(
			"DirectFinalizerEntry",
			M68kMemoryManagement.ManagedPoolMarkSweepGc);

		Assert.NotEmpty(result.Image);
	}

	[Fact]
	public void ManagedPoolWithFullExceptionsCompilesInheritedEffectiveFinalizer()
	{
		var result = Compile(
			"InheritedFinalizerEntry",
			M68kMemoryManagement.ManagedPoolMarkSweepGc);

		Assert.Contains(
			result.Symbols,
			symbol => symbol.Name.Contains(
				"BaseFinalizableFixture::Finalize",
				StringComparison.Ordinal));
	}

	[Fact]
	public void ManagedPoolWithYoloExceptionsReportsFullRequirement()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			Compile(
				"DirectFinalizerEntry",
				M68kMemoryManagement.ManagedPoolMarkSweepGc,
				M68kExceptionMode.Yolo));

		Assert.Equal(M68kDiagnosticIds.StaticAnalysis, exception.DiagnosticId);
		Assert.Contains("require Full exception mode", exception.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void InheritedFinalizerAllocationNamesEffectiveBaseFinalizer()
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			Compile("InheritedFinalizerEntry"));

		Assert.Equal(M68kDiagnosticIds.StaticAnalysis, exception.DiagnosticId);
		Assert.Contains("DerivedFinalizableFixture", exception.Message,
			StringComparison.Ordinal);
		Assert.Contains("BaseFinalizableFixture::Finalize", exception.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void UnreachableFinalizerAllocationDoesNotFailCompilation()
	{
		var result = Compile("UnreachableFinalizerEntry");

		Assert.NotEmpty(result.Image);
	}

	[Fact]
	public void UnreachableFinalizerAllocationDoesNotLinkFinalizerRuntime()
	{
		var result = Compile(
			"UnreachableFinalizerEntry",
			M68kMemoryManagement.ManagedPoolMarkSweepGc);

		Assert.DoesNotContain(
			result.Symbols,
			symbol =>
				symbol.Name.StartsWith("CopperSharp.Runtime.ManagedPool::", StringComparison.Ordinal) &&
				symbol.Name.Contains("Finaliz", StringComparison.Ordinal));
	}

	private static M68kCompilationResult Compile(
		string method,
		M68kMemoryManagement memoryManagement =
			M68kMemoryManagement.ExternalAllocator,
		M68kExceptionMode exceptionMode = M68kExceptionMode.Full) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = $"{FixtureType}::{method}",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			MemoryManagement = memoryManagement,
			ExceptionMode = exceptionMode,
			Heap = new M68kHeapOptions
			{
				StartAddress = 0x0000_4000,
				Size = 0x0000_2000
			}
		});
}
