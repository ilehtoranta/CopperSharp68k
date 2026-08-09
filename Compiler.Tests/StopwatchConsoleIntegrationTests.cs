/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class StopwatchConsoleIntegrationTests
{
	[Fact]
	public void PortableStopwatchAndConsoleCompileTogether()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(StopwatchConsoleIntegrationTests).Assembly.Location,
			EntryPoint =
				"CopperSharp.Compiler.Tests.StopwatchConsoleIntegrationFixture::Entry",
			Cpu = M68kCpuTarget.M68000,
			ExceptionMode = M68kExceptionMode.Full,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application
		});

		Assert.NotEmpty(result.Image);
	}
}

public static class StopwatchConsoleIntegrationFixture
{
	[M68kEntryPoint]
	public static int Entry()
	{
		var started = (uint)System.Diagnostics.Stopwatch.GetTimestamp();
		Console.WriteLine((uint)System.Diagnostics.Stopwatch.GetTimestamp() - started);
		return 0;
	}
}
