using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.NativeOsRunner.Tests;

public sealed class FileStatsNativeOsTests
{
    [NativeOsTheory]
    [Trait("Category", "NativeOS")]
    [InlineData("empty")]
    [InlineData("high-bit")]
    [InlineData("binary-lines")]
    [InlineData("quoted")]
    [InlineData("missing")]
    [InlineData("extra-argument")]
    [InlineData("usage")]
    public async Task RealDosReportMatchesInBothOptimizerModes(string scenario)
    {
        byte[] contents = scenario switch
        {
            "empty" => [],
            "high-bit" => [0xA5],
            _ => [0x00, 0x0A, 0x7F, 0xFF, 0x0A, 0x31]
        };
        var name = scenario == "quoted" ? "input data.bin" : "INPUT.BIN";
        var arguments = scenario switch
        {
            "quoted" => "\"input data.bin\"",
            "missing" => "MISSING.BIN",
            "extra-argument" => "INPUT.BIN OTHER.BIN",
            "usage" => "",
            _ => name
        };
        var expectedReturn = scenario == "missing" ? 20u : scenario is "extra-argument" or "usage" ? 10u : 0u;
        var expectedOutput = scenario switch
        {
            "missing" => "Cannot open file\n",
            "extra-argument" or "usage" => "Usage: filestats <file>\n",
            _ => ExpectedReport(name, contents)
        };
        NativeOsResult? previous = null;
        var rom = await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("COPPERSHARP_KICKSTART31_ROM")!);
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var compilation = AmigaM68kCompiler.Compile(new M68kCompilationRequest
            {
                AssemblyPath = Path.Combine(AppContext.BaseDirectory, "FileStats.dll"),
                EntryPoint = "FileStatsExample.Program::Main",
                Cpu = M68kCpuTarget.M68000,
                OutputFormat = M68kOutputFormat.Hunk,
                ExceptionMode = M68kExceptionMode.Yolo,
                PeepholeOptimization = mode
            });
            var result = await NativeOsSession.RunAsync(compilation.Image, rom, arguments,
                M68kCpuModel.M68000, false, 1500, TimeSpan.FromSeconds(120),
                inputFiles: new Dictionary<string, byte[]> { [name] = contents });
            await RetainRecord(scenario, mode, arguments, name, contents, compilation.Image,
                expectedReturn, expectedOutput, result);
            Assert.True(result.StopReason == "ProgramReturned", JsonSerializer.Serialize(result));
            Assert.Equal(0u, result.LaunchStatus);
            Assert.Equal(expectedReturn, result.ReturnValue);
            Assert.Equal(expectedOutput, result.StandardOutput);
            Assert.Null(result.DiskSnapshotError);
            Assert.Equal("M68000", result.CpuModel);
            Assert.Equal("Interpreter", result.ExecutionMode);
            if (previous is not null)
            {
                Assert.NotEqual(previous.RunId, result.RunId);
                Assert.NotEqual(previous.HunkSha256, result.HunkSha256);
                Assert.Equal(previous.StandardOutput, result.StandardOutput);
                Assert.Equal(previous.ReturnValue, result.ReturnValue);
            }
            previous = result;
        }
    }

    private static string ExpectedReport(string name, byte[] contents)
    {
        byte byteChecksum = 0;
        ushort wordChecksum = 0, lines = 0;
        uint sum = 0, hash = 2166136261;
        foreach (var value in contents)
        {
            byteChecksum = unchecked((byte)(byteChecksum + value));
            wordChecksum = unchecked((ushort)(((wordChecksum << 5) | (wordChecksum >> 11)) + value));
            if (value == 10) lines++;
            sum += value;
            hash = unchecked((hash ^ value) * 16777619);
        }
        var average = contents.Length == 0 ? 0u : sum / (uint)contents.Length;
        // AmigaDOS %ld prints signed LONG, including the uint hash's bit pattern.
        return string.Create(CultureInfo.InvariantCulture,
            $"{name}: {contents.Length} bytes, {lines} lines, byte checksum {byteChecksum}, word checksum {wordChecksum}, average byte {average}, hash {unchecked((int)hash)}\n");
    }

    private static async Task RetainRecord(string scenario, M68kPeepholeOptimizationMode mode,
        string arguments, string name, byte[] contents, byte[] image,
        uint expectedReturn, string expectedOutput, NativeOsResult result)
    {
        var directory = Environment.GetEnvironmentVariable("COPPERSHARP_NATIVE_FILESTATS_RESULTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, $"{scenario}-{mode}.hunk"), image);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{scenario}-{mode}-{result.RunId}.json"),
            JsonSerializer.Serialize(new
            {
                Scenario = scenario, OptimizationMode = mode.ToString(), Arguments = arguments,
                InputFileName = name, InputSha256 = Convert.ToHexString(SHA256.HashData(contents)),
                HunkSha256 = Convert.ToHexString(SHA256.HashData(image)),
                CompilerSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(typeof(M68kCompiler).Assembly.Location))),
                FixtureSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "FileStats.dll")))),
                ExpectedReturnValue = expectedReturn, ExpectedOutput = expectedOutput, Result = result
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
