using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CopperSharp.Compiler;
using CopperSharp.Targets.Amiga;
using Xunit.Sdk;

namespace CopperSharp.Compiler.Tests;

public sealed class CopperScreenHeadlessIntegrationTests
{
    private const string NativeOsRunnerEnvironmentVariable = "COPPERSHARP_NATIVE_OS_RUNNER";
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(180);

    public static TheoryData<string, uint, M68kCpuTarget, string> EmulatorCases
    {
        get
        {
            var result = new TheoryData<string, uint, M68kCpuTarget, string>();
            foreach (var (entryPoint, expected) in new[]
            {
                ("DefaultEntry", 106u),
                ("ArithmeticEntry", 16u),
                ("HunkBssEntry", 42u),
                ("TryCatchEntry", 42u)
            })
            {
                foreach (var (target, cpuBackend) in new[]
                {
                    (M68kCpuTarget.M68000, "InterpreterM68000"),
                    (M68kCpuTarget.M68000, "InterpreterM68040"),
                    (M68kCpuTarget.M68040, "JitM68040")
                })
                {
                    result.Add(entryPoint, expected, target, cpuBackend);
                }
            }

            return result;
        }
    }

    [NativeOsIntegrationTheory]
    [Trait("Category", "Emulator")]
    [MemberData(nameof(EmulatorCases))]
    public async Task CompiledHunkReturnsExpectedValueInCopperScreen(
        string entryPoint,
        uint expectedReturnValue,
        M68kCpuTarget target,
        string cpuBackend)
    {
        var cliPath = FindNativeOsRunner();
        if (cliPath is null)
        {
            throw new XunitException(
                $"Set {NativeOsRunnerEnvironmentVariable} to the native OS runner DLL to run emulator integration tests.");
        }

        var compilation = AmigaM68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
            EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entryPoint}",
            Cpu = target,
            OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Application
        });
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "coppersharp-copperscreen-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var hunkPath = Path.Combine(temporaryDirectory, $"{entryPoint}-{target}.hunk");
            await File.WriteAllBytesAsync(hunkPath, compilation.Image);

            var result = await RunNativeOsAsync(
                cliPath,
                hunkPath,
                cpuBackend,
                expectedReturnValue);
            var failureContext = FormatFailureContext(
                cliPath,
                entryPoint,
                target,
                cpuBackend,
                result);
            Assert.True(result.ExitCode == 0, failureContext);

            using var json = JsonDocument.Parse(result.StandardOutput);
            var root = json.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean(), failureContext);
            var run = root.GetProperty("result");
            Assert.Equal(expectedReturnValue, run.GetProperty("ReturnValue").GetUInt32());
            Assert.Equal("ProgramReturned", run.GetProperty("StopReason").GetString());
            Assert.Equal(0u, run.GetProperty("LaunchStatus").GetUInt32());
        }
        finally
        {
            DeleteStagingDirectory(temporaryDirectory);
        }
    }

    [NativeOsIntegrationTheory]
    [Trait("Category", "Emulator")]
    [InlineData("InterpreterM68000")]
    [InlineData("InterpreterM68040")]
    public async Task ManagedPoolCollectsAndPreservesCallerFrameRootsInCopperScreen(string cpuBackend)
    {
        const string entryPoint = "PoolCollectTracesCallerFrameEntry";
        const uint expectedReturnValue = 42;
        var cliPath = FindNativeOsRunner();
        if (cliPath is null)
        {
            throw new XunitException(
                $"Set {NativeOsRunnerEnvironmentVariable} to the native OS runner DLL to run emulator integration tests.");
        }

        var compilation = AmigaM68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
            EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entryPoint}",
            Cpu = M68kCpuTarget.M68000,
            OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Application,
            MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
            Heap = new M68kHeapOptions
            {
                Size = 88
            }
        });
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "coppersharp-copperscreen-gc-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var hunkPath = Path.Combine(temporaryDirectory, $"{entryPoint}-{cpuBackend}.hunk");
            await File.WriteAllBytesAsync(hunkPath, compilation.Image);

            var result = await RunNativeOsAsync(cliPath, hunkPath, cpuBackend, expectedReturnValue);
            var failureContext = FormatFailureContext(
                cliPath,
                entryPoint,
                M68kCpuTarget.M68000,
                cpuBackend,
                result);
            Assert.True(result.ExitCode == 0, failureContext);

            using var json = JsonDocument.Parse(result.StandardOutput);
            var root = json.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean(), failureContext);
            var run = root.GetProperty("result");
            Assert.Equal(expectedReturnValue, run.GetProperty("ReturnValue").GetUInt32());
            Assert.Equal("ProgramReturned", run.GetProperty("StopReason").GetString());
            Assert.Equal(0u, run.GetProperty("LaunchStatus").GetUInt32());
        }
        finally
        {
            DeleteStagingDirectory(temporaryDirectory);
        }
    }

    [NativeOsIntegrationTheory]
    [Trait("Category", "Emulator")]
    [InlineData("BoundsCatchEntry", "InterpreterM68000")]
    [InlineData("BoundsCatchEntry", "InterpreterM68040")]
    [InlineData("NullDereferenceCatchEntry", "InterpreterM68000")]
    [InlineData("NullDereferenceCatchEntry", "InterpreterM68040")]
    [InlineData("DivideByZeroCatchEntry", "InterpreterM68000")]
    [InlineData("DivideByZeroCatchEntry", "InterpreterM68040")]
    [InlineData("ExceptionalFinallyEntry", "InterpreterM68000")]
    [InlineData("ExceptionalFinallyEntry", "InterpreterM68040")]
    public Task ManagedExceptionsUnwindInCopperScreen(string entryPoint, string cpuBackend) =>
        RunFixtureAsync(entryPoint, 42, M68kCpuTarget.M68000, cpuBackend, managedHeapSize: 0x400);

    [NativeOsIntegrationTheory]
    [Trait("Category", "Emulator")]
    [InlineData(M68kCpuTarget.M68000, "InterpreterM68000")]
    [InlineData(M68kCpuTarget.M68000, "InterpreterM68040")]
    [InlineData(M68kCpuTarget.M68040, "JitM68040")]
    public Task Int64HybridAbiExecutesInCopperScreen(M68kCpuTarget target, string cpuBackend) =>
        RunFixtureAsync("HybridInt64ArgumentsEntry", 42, target, cpuBackend);

    [NativeOsIntegrationTheory]
    [Trait("Category", "Emulator")]
    [InlineData("VirtualDispatchEntry", "InterpreterM68000")]
    [InlineData("VirtualDispatchEntry", "InterpreterM68040")]
    [InlineData("InterfaceDispatchEntry", "InterpreterM68000")]
    [InlineData("InterfaceDispatchEntry", "InterpreterM68040")]
    public Task DynamicDispatchExecutesInCopperScreen(string entryPoint, string cpuBackend) =>
        RunFixtureAsync(entryPoint, 42, M68kCpuTarget.M68000, cpuBackend, managedHeapSize: 0x2000);

    [NativeOsIntegrationTheory]
    [Trait("Category", "Emulator")]
    [InlineData("InterpreterM68000")]
    [InlineData("InterpreterM68040")]
    public Task CapturingDelegateAndClosureSurviveForcedGcInCopperScreen(string cpuBackend) =>
        RunFixtureAsync("CapturingLambdaGcEntry", 42, M68kCpuTarget.M68000, cpuBackend, managedHeapSize: 0x2000);

    [NativeOsIntegrationTheory]
    [Trait("Category", "Emulator")]
    [InlineData("OnceOnlyEntry", 83u, "InterpreterM68000")]
    [InlineData("OnceOnlyEntry", 83u, "InterpreterM68040")]
    [InlineData("FailureEntry", 42u, "InterpreterM68000")]
    [InlineData("FailureEntry", 42u, "InterpreterM68040")]
    public Task StaticTypeInitializationExecutesInCopperScreen(
        string entryPoint,
        uint expectedReturnValue,
        string cpuBackend) =>
        RunFixtureAsync(
            entryPoint,
            expectedReturnValue,
            M68kCpuTarget.M68000,
            cpuBackend,
            declaringType: "CopperSharp.Compiler.Tests.TypeInitializationRuntimeFixtures");

    [NativeOsIntegrationTheory]
    [Trait("Category", "Emulator")]
    [InlineData("ManagedArrayEntry", 26u, "InterpreterM68000", true)]
    [InlineData("ManagedArrayEntry", 26u, "InterpreterM68040", true)]
    [InlineData("StringLiteralEntry", 9u, "InterpreterM68000", false)]
    [InlineData("StringLiteralEntry", 9u, "InterpreterM68040", false)]
    public Task ManagedArraysAndStringsExecuteInCopperScreen(
        string entryPoint,
        uint expectedReturnValue,
        string cpuBackend,
        bool needsManagedHeap) =>
        RunFixtureAsync(
            entryPoint,
            expectedReturnValue,
            M68kCpuTarget.M68000,
            cpuBackend,
            managedHeapSize: needsManagedHeap ? 0x2000u : null);

    [NativeOsIntegrationFact]
    [Trait("Category", "Emulator")]
    public Task M68040NativeFloatingPointExecutesInCopperScreen() =>
        RunFixtureAsync(
            "NativeFloatAdd",
            unchecked((uint)BitConverter.SingleToInt32Bits(3.75f)),
            M68kCpuTarget.M68040,
            "JitM68040",
            floatingPoint: M68kFloatingPointMode.M68040);

    [NativeOsIntegrationTheory]
    [Trait("Category", "Emulator")]
    [InlineData("DOS", "DOSExample.Program::Main", "missing", 20u)]
    [InlineData("FileStats", "FileStatsExample.Program::Main", "", 10u)]
    public async Task DosExamplesRunThroughAmigaApplicationAbiInCopperScreen(
        string example,
        string entryPoint,
        string arguments,
        uint expectedReturnValue)
    {
        var cliPath = FindNativeOsRunner();
        if (cliPath is null)
        {
            throw new XunitException(
                $"Set {NativeOsRunnerEnvironmentVariable} to the native OS runner DLL to run emulator integration tests.");
        }

        var assemblyPath = Path.Combine(AppContext.BaseDirectory, example + ".dll");
        Assert.True(File.Exists(assemblyPath), $"Example assembly was not built: '{assemblyPath}'.");
        var compilation = AmigaM68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = assemblyPath,
            EntryPoint = entryPoint,
            Cpu = M68kCpuTarget.M68000,
            OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Application,
            ExceptionMode = M68kExceptionMode.Yolo
        });
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "coppersharp-dos-example-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var hunkPath = Path.Combine(temporaryDirectory, example);
            await File.WriteAllBytesAsync(hunkPath, compilation.Image);
            var result = await RunNativeOsAsync(
                cliPath,
                hunkPath,
                "InterpreterM68000",
                expectedReturnValue,
                arguments);
            var failureContext = FormatFailureContext(
                cliPath,
                entryPoint,
                M68kCpuTarget.M68000,
                "InterpreterM68000",
                result);
            Assert.True(result.ExitCode == 0, failureContext);

            using var json = JsonDocument.Parse(result.StandardOutput);
            var root = json.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean(), failureContext);
            var run = root.GetProperty("result");
            Assert.Equal(expectedReturnValue, run.GetProperty("ReturnValue").GetUInt32());
            Assert.Equal("ProgramReturned", run.GetProperty("StopReason").GetString());
            Assert.Equal(0u, run.GetProperty("LaunchStatus").GetUInt32());
            Assert.Null(run.GetProperty("DiskSnapshotError").GetString());
        }
        finally
        {
            DeleteStagingDirectory(temporaryDirectory);
        }
    }

    [Fact(Skip = "Retired payload-only baseline: native boot cycles are not equivalent. Instruction-count instrumentation is required before this benchmark can run.")]
    [Trait("Category", "Benchmark")]
    [Trait("Category", "Emulator")]
    public async Task FileStatsEmptyInputHasPinnedA500PalExecutionCost()
    {
        const uint expectedReturnValue = 10;
        var cliPath = FindNativeOsRunner();
        if (cliPath is null)
        {
            throw new XunitException(
                $"Set {NativeOsRunnerEnvironmentVariable} to the native OS runner DLL to run emulator benchmarks.");
        }

        var compilation = AmigaM68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = Path.Combine(AppContext.BaseDirectory, "FileStats.dll"),
            EntryPoint = "FileStatsExample.Program::Main",
            Cpu = M68kCpuTarget.M68000,
            OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Application,
            ExceptionMode = M68kExceptionMode.Yolo
        });
        var temporaryDirectory = Path.Combine(Path.GetTempPath(),
            "coppersharp-filestats-benchmark-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var hunkPath = Path.Combine(temporaryDirectory, "FileStats");
            await File.WriteAllBytesAsync(hunkPath, compilation.Image);
            var result = await RunNativeOsAsync(
                cliPath, hunkPath, "InterpreterM68000", expectedReturnValue);
            var context = FormatFailureContext(
                cliPath, "FileStatsExample.Program::Main", M68kCpuTarget.M68000,
                "InterpreterM68000", result);
            Assert.True(result.ExitCode == 0, context);

            using var json = JsonDocument.Parse(result.StandardOutput);
            var run = json.RootElement.GetProperty("result");
            Assert.Equal(expectedReturnValue, run.GetProperty("ReturnValue").GetUInt32());
            Assert.Equal("ProgramReturned", run.GetProperty("StopReason").GetString());
            Assert.Equal(0u, run.GetProperty("LaunchStatus").GetUInt32());
            var snapshot = run.GetProperty("Snapshot");
            var cycles = snapshot.GetProperty("Cpu").GetProperty("Cycles").GetInt64();
            var instructions = snapshot.GetProperty("InstructionsExecuted").GetInt64();
            Assert.InRange(cycles, 1, 100_000);
            Assert.InRange(instructions, 1, 10_000);
        }
        finally
        {
            DeleteStagingDirectory(temporaryDirectory);
        }
    }

    [NativeOsIntegrationFact]
    [Trait("Category", "Emulator")]
    public async Task FileStatsEmptyInputCompletesWithinNativeBootBudget()
    {
        var cliPath = FindNativeOsRunner() ?? throw new XunitException("Build or configure the native OS runner.");
        var compilation = AmigaM68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = Path.Combine(AppContext.BaseDirectory, "FileStats.dll"),
            EntryPoint = "FileStatsExample.Program::Main",
            Cpu = M68kCpuTarget.M68000,
            RuntimeProfile = M68kRuntimeProfile.Application,
            ExceptionMode = M68kExceptionMode.Yolo
        });
        var directory = Path.Combine(Path.GetTempPath(), "coppersharp-native-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var hunk = Path.Combine(directory, "FileStats");
            await File.WriteAllBytesAsync(hunk, compilation.Image);
            var result = await RunNativeOsAsync(cliPath, hunk, "InterpreterM68000", 10);
            Assert.True(result.ExitCode == 0, FormatFailureContext(cliPath, "FileStats", M68kCpuTarget.M68000, "InterpreterM68000", result));
            using var json = JsonDocument.Parse(result.StandardOutput);
            var run = json.RootElement.GetProperty("result");
            Assert.Equal("ProgramReturned", run.GetProperty("StopReason").GetString());
            Assert.Equal(10u, run.GetProperty("ReturnValue").GetUInt32());
            Assert.InRange(run.GetProperty("Frames").GetInt64(), 1, 1500);
            Assert.InRange(run.GetProperty("Cycles").GetInt64(), 1, 1500L * 142_500);
            Assert.Equal("M68000", run.GetProperty("CpuModel").GetString());
            Assert.Equal("Interpreter", run.GetProperty("ExecutionMode").GetString());
        }
        finally { DeleteStagingDirectory(directory); }
    }

    private static async Task RunFixtureAsync(
        string entryPoint,
        uint expectedReturnValue,
        M68kCpuTarget target,
        string cpuBackend,
        uint? managedHeapSize = null,
        string declaringType = "CopperSharp.Compiler.Tests.CompilerFixtures",
        M68kFloatingPointMode floatingPoint = M68kFloatingPointMode.Disabled)
    {
        var cliPath = FindNativeOsRunner();
        if (cliPath is null)
        {
            throw new XunitException(
                $"Set {NativeOsRunnerEnvironmentVariable} to the native OS runner DLL to run emulator integration tests.");
        }

        var request = new M68kCompilationRequest
        {
            AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
            EntryPoint = $"{declaringType}::{entryPoint}",
            Cpu = target,
            OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Application,
            FloatingPoint = floatingPoint,
            MemoryManagement = managedHeapSize.HasValue
                ? M68kMemoryManagement.ManagedPoolMarkSweepGc
                : M68kMemoryManagement.None,
            Heap = managedHeapSize.HasValue
                ? new M68kHeapOptions
                {
                    Size = managedHeapSize.Value
                }
                : null!
        };
        var compilation = AmigaM68kCompiler.Compile(request);
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "coppersharp-copperscreen-fixture-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var hunkPath = Path.Combine(temporaryDirectory, $"{entryPoint}-{target}.hunk");
            await File.WriteAllBytesAsync(hunkPath, compilation.Image);
            var result = await RunNativeOsAsync(cliPath, hunkPath, cpuBackend, expectedReturnValue);
            var failureContext = FormatFailureContext(cliPath, entryPoint, target, cpuBackend, result);
            Assert.True(result.ExitCode == 0, failureContext);

            using var json = JsonDocument.Parse(result.StandardOutput);
            var root = json.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean(), failureContext);
            var run = root.GetProperty("result");
            Assert.Equal(expectedReturnValue, run.GetProperty("ReturnValue").GetUInt32());
            Assert.Equal("ProgramReturned", run.GetProperty("StopReason").GetString());
            Assert.Equal(0u, run.GetProperty("LaunchStatus").GetUInt32());
        }
        finally
        {
            DeleteStagingDirectory(temporaryDirectory);
        }
    }

    private static async Task<ProcessResult> RunNativeOsAsync(
        string cliPath,
        string hunkPath,
        string cpuBackend,
        uint expectedReturnValue,
        string arguments = "")
    {
        var isManagedDll = Path.GetExtension(cliPath).Equals(".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = isManagedDll ? "dotnet" : cliPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (isManagedDll)
        {
            startInfo.ArgumentList.Add(cliPath);
        }

        AddOption(startInfo, "--hunk", hunkPath);
        AddOption(startInfo, "--expect-d0", expectedReturnValue.ToString(CultureInfo.InvariantCulture));
        AddOption(startInfo, "--rom", Environment.GetEnvironmentVariable("COPPERSHARP_KICKSTART31_ROM")!);
        var model = cpuBackend switch
        {
            "InterpreterM68000" => "68000",
            "InterpreterM68040" or "JitM68040" => "68040",
            _ => throw new XunitException($"Unsupported native OS backend: {cpuBackend}")
        };
        AddOption(startInfo, "--cpu", model);
        if (cpuBackend == "JitM68040") startInfo.ArgumentList.Add("--jit");
        if (arguments.Length > 0)
        {
            AddOption(startInfo, "--arguments", arguments);
        }
        AddOption(startInfo, "--max-frames", "1500");
        AddOption(startInfo, "--timeout-seconds", "120");


        using var process = Process.Start(startInfo) ??
            throw new XunitException($"Could not start native OS runner '{cliPath}'.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new XunitException(
                $"CopperScreen did not finish within {ProcessTimeout.TotalSeconds:F0} seconds. " +
                $"stdout:{Environment.NewLine}{await standardOutput}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{await standardError}");
        }

        var result = new ProcessResult(process.ExitCode, await standardOutput, await standardError);
        var recordDirectory = Environment.GetEnvironmentVariable("COPPERSHARP_NATIVE_OS_INTEGRATION_RESULTS");
        if (!string.IsNullOrWhiteSpace(recordDirectory))
        {
            Directory.CreateDirectory(recordDirectory);
            var recordName = $"{Path.GetFileName(hunkPath)}-{cpuBackend}-{Guid.NewGuid():N}";
            var recordPath = Path.Combine(recordDirectory, recordName + ".json");
            var retainedHunk = recordName + ".hunk";
            await File.WriteAllBytesAsync(Path.Combine(recordDirectory, retainedHunk), await File.ReadAllBytesAsync(hunkPath));
            await File.WriteAllTextAsync(recordPath, JsonSerializer.Serialize(new
            {
                Entry = Path.GetFileName(hunkPath), Backend = cpuBackend, RetainedHunk = retainedHunk,
                ExpectedReturnValue = expectedReturnValue,
                HunkSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(hunkPath))),
                CompilerSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(typeof(M68kCompiler).Assembly.Location))),
                FixtureSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(typeof(CompilerFixtures).Assembly.Location))),
                RunnerSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(cliPath))),
                result.ExitCode, result.StandardOutput, result.StandardError
            }));
        }
        if (result.ExitCode == 0)
        {
            using var json = JsonDocument.Parse(result.StandardOutput);
            var run = json.RootElement.GetProperty("result");
            Assert.Equal("M" + model, run.GetProperty("CpuModel").GetString());
            Assert.Equal(cpuBackend == "JitM68040" ? "Jit" : "Interpreter", run.GetProperty("ExecutionMode").GetString());
            Assert.Null(run.GetProperty("DiskSnapshotError").GetString());
            Assert.True(Guid.TryParseExact(run.GetProperty("RunId").GetString(), "N", out _));
        }
        return result;
    }

    private static void AddOption(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private static string? FindNativeOsRunner()
    {
        var configuredPath = Environment.GetEnvironmentVariable(NativeOsRunnerEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath);
            if (!File.Exists(fullPath))
            {
                throw new XunitException(
                    $"{NativeOsRunnerEnvironmentVariable} points to a missing file: '{fullPath}'.");
            }

            return fullPath;
        }

        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return null;
        }

        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var outputDirectory = Path.Combine(
                repositoryRoot,
                "Compiler.NativeOsRunner",
                "bin",
                configuration,
                "net10.0");
            foreach (var fileName in new[] { "CopperSharp.Compiler.NativeOsRunner.dll" })
            {
                var candidate = Path.Combine(outputDirectory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CopperSharp68k.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string FormatFailureContext(
        string cliPath,
        string entryPoint,
        M68kCpuTarget target,
        string cpuBackend,
        ProcessResult result) =>
        $"CopperScreen native OS validation failed. CLI='{cliPath}', entry={entryPoint}, " +
        $"compiler={target}, backend={cpuBackend}, " +
        $"exit={result.ExitCode}.{Environment.NewLine}" +
        $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
        $"stderr:{Environment.NewLine}{result.StandardError}";

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private static void DeleteStagingDirectory(string directory)
    {
        var resolved = Path.GetFullPath(directory);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(resolved).StartsWith("coppersharp-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to remove a staging directory outside the test temporary root.");
        Directory.Delete(resolved, recursive: true);
    }
}
