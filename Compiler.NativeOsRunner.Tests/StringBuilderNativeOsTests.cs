using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Tests.NativeOsFixture;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.NativeOsRunner.Tests;

public sealed class StringBuilderNativeOsTests
{
    // The two interpreters execute the same 68000 image; compile that input once.
    private static readonly ConcurrentDictionary<(string Entry, M68kCpuTarget Cpu, M68kPeepholeOptimizationMode Mode),
        Lazy<M68kCompilationResult>> Compilations = new();
    public static TheoryData<string, string> Scenarios => new()
    {
        { "Presized", "partpartpartpartpartpartpartpart\n" },
        { "Growing", "partpartpartpartpartpartpartpart\n" },
        { "IntegerFormat", "-00042|89ABCDEF\n" },
        { "IntegerHandler", "[0][1][2][3][4][5][6][7]\n" },
        { "CollectWithLiveBuilderAndSnapshot", "partpartpartpartpartpartpartpart\nafter collection\n" }
    };

    public static TheoryData<string, string, string> NativeCases
    {
        get
        {
            var cases = new TheoryData<string, string, string>();
            foreach (var scenario in Scenarios)
            foreach (var backend in new[] { "InterpreterM68000", "InterpreterM68040", "JitM68040" })
                cases.Add((string)scenario[0], (string)scenario[1], backend);
            return cases;
        }
    }

    [NativeOsTheory]
    [Trait("Category", "NativeOS")]
    [MemberData(nameof(NativeCases))]
    public async Task PinnedStringBuilderMatchesExactOutputInBothOptimizerModes(string entry, string expectedOutput, string backend)
    {
        using var pack = new PinnedCoreLibPack();
        var rom = await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("COPPERSHARP_KICKSTART31_ROM")!);
        var jit = backend == "JitM68040";
        var cpu = jit ? M68kCpuTarget.M68040 : M68kCpuTarget.M68000;
        var model = backend == "InterpreterM68000" ? M68kCpuModel.M68000 : M68kCpuModel.M68040;
        NativeOsResult? previous = null;
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var compilation = Compilations.GetOrAdd((entry, cpu, mode), _ => new(() => AmigaM68kCompiler.Compile(new M68kCompilationRequest
            {
                AssemblyPath = typeof(StringBuilderNativeFixture).Assembly.Location,
                EntryPoint = $"{typeof(StringBuilderNativeFixture).FullName}::{entry}",
                ManagedAssemblyPaths = [typeof(CopperSharp.Runtime.AmigaPal.EnvironmentPal).Assembly.Location],
                IncludedExportNames = [], Cpu = cpu,
                OutputFormat = M68kOutputFormat.Hunk, RuntimeProfile = M68kRuntimeProfile.Application,
                ExceptionMode = M68kExceptionMode.Full, PeepholeOptimization = mode,
                MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
                GcSweepStrategy = M68kGcSweepStrategy.OnAllocationFailure,
                Heap = new M68kHeapOptions { Size = 0x2000 },
                FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
            }))).Value;
            Assert.Contains("sha256=" + PinnedCoreLibPack.ExpectedSha256, compilation.Map, StringComparison.Ordinal);
            Assert.Contains(compilation.Symbols, symbol => symbol.Name.StartsWith("System.Text.StringBuilder::", StringComparison.Ordinal));
            var result = await NativeOsSession.RunAsync(compilation.Image, rom, "",
                model, jit, 1500, TimeSpan.FromSeconds(120));
            await Retain(entry, mode, backend, cpu, expectedOutput, compilation.Image, compilation.Map, result);
            Assert.True(result.StopReason == "ProgramReturned", JsonSerializer.Serialize(result));
            Assert.Equal(0u, result.LaunchStatus);
            Assert.Equal(42u, result.ReturnValue);
            Assert.Equal(expectedOutput, result.StandardOutput);
            Assert.Null(result.DiskSnapshotError);
            Assert.Equal(model.ToString(), result.CpuModel);
            Assert.Equal(jit ? "Jit" : "Interpreter", result.ExecutionMode);
            if (previous is not null)
            {
                Assert.NotEqual(previous.RunId, result.RunId);
                Assert.Equal(previous.ReturnValue, result.ReturnValue);
                Assert.Equal(previous.StandardOutput, result.StandardOutput);
            }
            previous = result;
        }
    }

    private static async Task Retain(string entry, M68kPeepholeOptimizationMode mode, string backend,
        M68kCpuTarget cpu, string expectedOutput, byte[] image, string map, NativeOsResult result)
    {
        var directory = Environment.GetEnvironmentVariable("COPPERSHARP_NATIVE_STRINGBUILDER_RESULTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var name = $"{entry}-{backend}-{mode}-{result.RunId}";
        await File.WriteAllBytesAsync(Path.Combine(directory, name + ".hunk"), image);
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".map"), map);
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new
        {
            Entry = entry, Backend = backend, CompilerCpu = cpu.ToString(), OptimizationMode = mode.ToString(),
            HeapBytes = 0x2000, ExpectedReturnValue = 42u, ExpectedOutput = expectedOutput,
            RetainedHunk = name + ".hunk", CoreLibSha256 = PinnedCoreLibPack.ExpectedSha256,
            CompilerSha256 = Hash(typeof(M68kCompiler).Assembly.Location),
            FixtureSha256 = Hash(typeof(StringBuilderNativeFixture).Assembly.Location),
            RunnerSha256 = Hash(typeof(NativeOsSession).Assembly.Location),
            EngineSha256 = Hash(typeof(CopperMod.Amiga.Lightweight.LightweightA500Machine).Assembly.Location),
            CpuEngineSha256 = Hash(typeof(M68kCpuModel).Assembly.Location),
            RuntimeSha256 = Hash(typeof(CopperSharp.Runtime.ManagedPool).Assembly.Location),
            Result = result
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
