/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Copper68k;
using CopperSharp.Compiler.Framework;
using CopperSharp.Runtime.AmigaPal;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DateOnlyInvariantCalendarSurfaceExecutesOnTarget(M68kCpuTarget target, M68kCpuModel model)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibDateOnlyQualificationEntry",
			IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(M68kRuntime).Assembly.Location, typeof(EnvironmentPal).Assembly.Location],
			Cpu = target,
			FloatingPoint = M68kFloatingPointMode.SoftFloat,
			OutputFormat = M68kOutputFormat.Hunk,
			ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 32 * 1024 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});

		Assert.Equal(42u, Execute(CreateHunkBus(result), model,
			HunkLoadAddress + result.EntryPoint, initialStackPointer: 0x0020_0000,
			maxInstructions: 100_000_000));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DateOnlyInvariantParsingExecutesOnTarget(M68kCpuTarget target, M68kCpuModel model)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibDateOnlyParseQualificationEntry",
			IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(M68kRuntime).Assembly.Location, typeof(EnvironmentPal).Assembly.Location],
			Cpu = target,
			FloatingPoint = M68kFloatingPointMode.SoftFloat,
			OutputFormat = M68kOutputFormat.Hunk,
			ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 32 * 1024 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});

		Assert.Equal(42u, Execute(CreateHunkBus(result), model,
			HunkLoadAddress + result.EntryPoint, initialStackPointer: 0x0020_0000,
			maxInstructions: 100_000_000));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void TimeOnlyArithmeticAndInvariantFormattingExecuteOnTarget(M68kCpuTarget target, M68kCpuModel model)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibTimeOnlyQualificationEntry",
			IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(M68kRuntime).Assembly.Location, typeof(EnvironmentPal).Assembly.Location],
			Cpu = target,
			FloatingPoint = M68kFloatingPointMode.SoftFloat,
			OutputFormat = M68kOutputFormat.Hunk,
			ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 32 * 1024 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});

		Assert.Equal(42u, Execute(CreateHunkBus(result), model,
			HunkLoadAddress + result.EntryPoint, initialStackPointer: 0x0020_0000,
			maxInstructions: 100_000_000));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void TimeOnlyInvariantParsingExecutesOnTarget(M68kCpuTarget target, M68kCpuModel model)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = FixtureAssembly,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibTimeOnlyParseQualificationEntry",
			IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(M68kRuntime).Assembly.Location, typeof(EnvironmentPal).Assembly.Location],
			Cpu = target,
			FloatingPoint = M68kFloatingPointMode.SoftFloat,
			OutputFormat = M68kOutputFormat.Hunk,
			ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 32 * 1024 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});

		Assert.Equal(42u, Execute(CreateHunkBus(result), model,
			HunkLoadAddress + result.EntryPoint, initialStackPointer: 0x0020_0000,
			maxInstructions: 100_000_000));
	}
}
