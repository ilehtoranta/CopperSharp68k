using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CopperSharp.Compiler;
using CopperSharp.Targets.Amiga;
using Xunit.Sdk;

namespace CopperSharp.Compiler.Tests;

public sealed class CopperScreenHeadlessIntegrationTests
{
    private const string HeadlessCliEnvironmentVariable = "COPPERSCREEN_HEADLESS_CLI";
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

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
                    (M68kCpuTarget.M68000, "AccurateM68000"),
                    (M68kCpuTarget.M68000, "JitM68000"),
                    (M68kCpuTarget.M68040, "JitM68040")
                })
                {
                    result.Add(entryPoint, expected, target, cpuBackend);
                }
            }

            return result;
        }
    }

    [Theory]
    [Trait("Category", "Emulator")]
    [MemberData(nameof(EmulatorCases))]
    public async Task CompiledHunkReturnsExpectedValueInCopperScreen(
        string entryPoint,
        uint expectedReturnValue,
        M68kCpuTarget target,
        string cpuBackend)
    {
        var cliPath = FindHeadlessCli();
        if (cliPath is null)
        {
            throw SkipException.ForSkip(
                $"Set {HeadlessCliEnvironmentVariable} to CopperScreen.Headless.Cli.exe or its DLL to run emulator integration tests.");
        }

        var compilation = M68kCompiler.Compile(new M68kCompilationRequest
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

            var result = await RunHeadlessAsync(
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
            Assert.Equal(0, run.GetProperty("StopReason").GetInt32());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Theory]
    [Trait("Category", "Emulator")]
    [InlineData("AccurateM68000")]
    [InlineData("JitM68000")]
    public async Task ManagedPoolCollectsAndPreservesCallerFrameRootsInCopperScreen(string cpuBackend)
    {
        const string entryPoint = "PoolCollectTracesCallerFrameEntry";
        const uint expectedReturnValue = 42;
        var cliPath = FindHeadlessCli();
        if (cliPath is null)
        {
            throw SkipException.ForSkip(
                $"Set {HeadlessCliEnvironmentVariable} to CopperScreen.Headless.Cli.exe or its DLL to run emulator integration tests.");
        }

        var compilation = M68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
            EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{entryPoint}",
            Cpu = M68kCpuTarget.M68000,
            OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Application,
            MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
            Heap = new M68kHeapOptions
            {
                StartAddress = 0x0000_4000,
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

            var result = await RunHeadlessAsync(cliPath, hunkPath, cpuBackend, expectedReturnValue);
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
            Assert.Equal(0, run.GetProperty("StopReason").GetInt32());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Theory]
    [Trait("Category", "Emulator")]
    [InlineData("BoundsCatchEntry", "AccurateM68000")]
    [InlineData("BoundsCatchEntry", "JitM68000")]
    [InlineData("NullDereferenceCatchEntry", "AccurateM68000")]
    [InlineData("NullDereferenceCatchEntry", "JitM68000")]
    [InlineData("DivideByZeroCatchEntry", "AccurateM68000")]
    [InlineData("DivideByZeroCatchEntry", "JitM68000")]
    [InlineData("ExceptionalFinallyEntry", "AccurateM68000")]
    [InlineData("ExceptionalFinallyEntry", "JitM68000")]
    public Task ManagedExceptionsUnwindInCopperScreen(string entryPoint, string cpuBackend) =>
        RunFixtureAsync(entryPoint, 42, M68kCpuTarget.M68000, cpuBackend, managedHeapSize: 0x400);

    [Theory]
    [Trait("Category", "Emulator")]
    [InlineData(M68kCpuTarget.M68000, "AccurateM68000")]
    [InlineData(M68kCpuTarget.M68000, "JitM68000")]
    [InlineData(M68kCpuTarget.M68040, "JitM68040")]
    public Task Int64HybridAbiExecutesInCopperScreen(M68kCpuTarget target, string cpuBackend) =>
        RunFixtureAsync("HybridInt64ArgumentsEntry", 42, target, cpuBackend);

    [Theory]
    [Trait("Category", "Emulator")]
    [InlineData("VirtualDispatchEntry", "AccurateM68000")]
    [InlineData("VirtualDispatchEntry", "JitM68000")]
    [InlineData("InterfaceDispatchEntry", "AccurateM68000")]
    [InlineData("InterfaceDispatchEntry", "JitM68000")]
    public Task DynamicDispatchExecutesInCopperScreen(string entryPoint, string cpuBackend) =>
        RunFixtureAsync(entryPoint, 42, M68kCpuTarget.M68000, cpuBackend, managedHeapSize: 0x2000);

    [Theory]
    [Trait("Category", "Emulator")]
    [InlineData("AccurateM68000")]
    [InlineData("JitM68000")]
    public Task CapturingDelegateAndClosureSurviveForcedGcInCopperScreen(string cpuBackend) =>
        RunFixtureAsync("CapturingLambdaGcEntry", 42, M68kCpuTarget.M68000, cpuBackend, managedHeapSize: 0x2000);

    [Theory]
    [Trait("Category", "Emulator")]
    [InlineData("OnceOnlyEntry", 83u, "AccurateM68000")]
    [InlineData("OnceOnlyEntry", 83u, "JitM68000")]
    [InlineData("FailureEntry", 42u, "AccurateM68000")]
    [InlineData("FailureEntry", 42u, "JitM68000")]
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

    [Theory]
    [Trait("Category", "Emulator")]
    [InlineData("ManagedArrayEntry", 26u, "AccurateM68000", true)]
    [InlineData("ManagedArrayEntry", 26u, "JitM68000", true)]
    [InlineData("StringLiteralEntry", 9u, "AccurateM68000", false)]
    [InlineData("StringLiteralEntry", 9u, "JitM68000", false)]
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

    [Fact]
    [Trait("Category", "Emulator")]
    public Task M68040NativeFloatingPointExecutesInCopperScreen() =>
        RunFixtureAsync(
            "NativeFloatAdd",
            unchecked((uint)BitConverter.SingleToInt32Bits(3.75f)),
            M68kCpuTarget.M68040,
            "JitM68040",
            floatingPoint: M68kFloatingPointMode.M68040);

    [Theory]
    [Trait("Category", "Emulator")]
    [InlineData("DOS", "DOSExample.Program::Main", "missing", 20u)]
    [InlineData("FileStats", "FileStatsExample.Program::Main", "", 10u)]
    public async Task DosExamplesRunThroughAmigaApplicationAbiInCopperScreen(
        string example,
        string entryPoint,
        string arguments,
        uint expectedReturnValue)
    {
        var cliPath = FindHeadlessCli();
        if (cliPath is null)
        {
            throw SkipException.ForSkip(
                $"Set {HeadlessCliEnvironmentVariable} to CopperScreen.Headless.Cli.exe or its DLL to run emulator integration tests.");
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
            var result = await RunHeadlessAsync(
                cliPath,
                hunkPath,
                "AccurateM68000",
                expectedReturnValue,
                arguments);
            var failureContext = FormatFailureContext(
                cliPath,
                entryPoint,
                M68kCpuTarget.M68000,
                "AccurateM68000",
                result);
            Assert.True(result.ExitCode == 0, failureContext);

            using var json = JsonDocument.Parse(result.StandardOutput);
            var root = json.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean(), failureContext);
            var run = root.GetProperty("result");
            Assert.Equal(expectedReturnValue, run.GetProperty("ReturnValue").GetUInt32());
            Assert.Equal(0, run.GetProperty("StopReason").GetInt32());
            var diagnostics = run.GetProperty("Snapshot").GetProperty("Diagnostics");
            Assert.Contains(
                diagnostics.EnumerateArray(),
                diagnostic => diagnostic.GetProperty("Code").GetString() == "AMIGA_BOOT_OPEN_LIBRARY");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
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
        var cliPath = FindHeadlessCli();
        if (cliPath is null)
        {
            throw SkipException.ForSkip(
                $"Set {HeadlessCliEnvironmentVariable} to CopperScreen.Headless.Cli.exe or its DLL to run emulator integration tests.");
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
                    StartAddress = 0x0000_4000,
                    Size = managedHeapSize.Value
                }
                : null!
        };
        var compilation = M68kCompiler.Compile(request);
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "coppersharp-copperscreen-fixture-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var hunkPath = Path.Combine(temporaryDirectory, $"{entryPoint}-{target}.hunk");
            await File.WriteAllBytesAsync(hunkPath, compilation.Image);
            var result = await RunHeadlessAsync(cliPath, hunkPath, cpuBackend, expectedReturnValue);
            var failureContext = FormatFailureContext(cliPath, entryPoint, target, cpuBackend, result);
            Assert.True(result.ExitCode == 0, failureContext);

            using var json = JsonDocument.Parse(result.StandardOutput);
            var root = json.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean(), failureContext);
            var run = root.GetProperty("result");
            Assert.Equal(expectedReturnValue, run.GetProperty("ReturnValue").GetUInt32());
            Assert.Equal(0, run.GetProperty("StopReason").GetInt32());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunHeadlessAsync(
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
        AddOption(startInfo, "--profile", "A500Pal512K");
        AddOption(startInfo, "--cpu", cpuBackend);
        if (arguments.Length > 0)
        {
            AddOption(startInfo, "--arguments", arguments);
        }
        AddOption(startInfo, "--max-frames", "20");
        AddOption(startInfo, "--max-instructions", "1000000");
        startInfo.ArgumentList.Add("--json");

        using var process = Process.Start(startInfo) ??
            throw new XunitException($"Could not start CopperScreen headless CLI '{cliPath}'.");
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

        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static void AddOption(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private static string? FindHeadlessCli()
    {
        var configuredPath = Environment.GetEnvironmentVariable(HeadlessCliEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath);
            if (!File.Exists(fullPath))
            {
                throw new XunitException(
                    $"{HeadlessCliEnvironmentVariable} points to a missing file: '{fullPath}'.");
            }

            return fullPath;
        }

        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return null;
        }

        var workspaceRoot = Directory.GetParent(repositoryRoot)?.FullName;
        if (workspaceRoot is null)
        {
            return null;
        }

        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var outputDirectory = Path.Combine(
                workspaceRoot,
                "MedPlayer",
                "CopperScreen.Headless.Cli",
                "bin",
                configuration,
                "net10.0");
            foreach (var fileName in new[] { "CopperScreen.Headless.Cli.exe", "CopperScreen.Headless.Cli.dll" })
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
        $"CopperScreen headless validation failed. CLI='{cliPath}', entry={entryPoint}, " +
        $"compiler={target}, backend={cpuBackend}, " +
        $"exit={result.ExitCode}.{Environment.NewLine}" +
        $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
        $"stderr:{Environment.NewLine}{result.StandardError}";

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
