using System.Diagnostics;
using System.Text.Json;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

// Opt-in wall-clock measurements; no work is moved out of the test or cached.
internal static class ExecutionTestProfiling
{
	private static readonly string? DirectoryPath = Environment.GetEnvironmentVariable("COPPERSHARP_EXECUTION_PHASE_PROFILE");
	private static readonly object WriteLock = new();
	[ThreadStatic] private static Identity? current;
	internal sealed record Identity(string Entry, string Cpu, string Optimization);
	internal static void WriteOptimizerSnapshot(object snapshot)
	{
		if (current is not { } identity || string.IsNullOrEmpty(DirectoryPath)) return;
		Directory.CreateDirectory(DirectoryPath);
		var path = Path.Combine(DirectoryPath, $"optimizer-{identity.Cpu}-{identity.Optimization}-{Guid.NewGuid():N}.json");
		File.WriteAllText(path, JsonSerializer.Serialize(new { identity, Snapshot = snapshot },
			new JsonSerializerOptions { WriteIndented = true }));
	}

	internal readonly struct Scenario : IDisposable
	{
		private readonly Identity? previous;
		internal Scenario(string entry, M68kCpuTarget cpu, M68kPeepholeOptimizationMode optimization)
		{
			previous = current;
			if (!string.IsNullOrEmpty(DirectoryPath)) current = new(entry, cpu.ToString(), optimization.ToString());
		}
		public void Dispose() => current = previous;
	}

	internal readonly struct Measurement : IDisposable
	{
		private readonly string phase;
		private readonly Identity? identity;
		private readonly long start;
		internal Measurement(string phase)
		{
			this.phase = phase;
			identity = current;
			start = identity is null ? 0 : Stopwatch.GetTimestamp();
		}
		public void Dispose()
		{
			if (identity is null) return;
			var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
			var json = JsonSerializer.Serialize(new { Phase = phase, identity.Entry, identity.Cpu, identity.Optimization,
				Milliseconds = elapsed, ProcessId = Environment.ProcessId,
				HostTieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default" });
			lock (WriteLock)
			{
				Directory.CreateDirectory(DirectoryPath!);
				File.AppendAllText(Path.Combine(DirectoryPath!, $"phases-{Environment.ProcessId}.jsonl"), json + "\n");
			}
		}
	}
}

public sealed partial class CompilerExecutionTests
{
	private static M68kCompilationResult CompileProfiled(M68kCompilationRequest request)
	{
		using var measurement = new ExecutionTestProfiling.Measurement("compilation");
		using var optimizer = Environment.GetEnvironmentVariable("COPPERSHARP_EXECUTION_OPTIMIZER_PROFILE") == "1"
			? M68kOptimizationProfiler.Start() : null;
		try { return M68kCompiler.Compile(request); }
		finally
		{
			if (optimizer is not null) ExecutionTestProfiling.WriteOptimizerSnapshot(optimizer.GetSnapshot());
		}
	}
}
