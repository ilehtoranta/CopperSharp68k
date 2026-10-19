using System.Text.Json;
using Copper68k;
using CopperSharp.Compiler.Tests.NativeOsFixture;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.NativeOsRunner.Tests;

public sealed class NativeOsTheoryAttribute : TheoryAttribute
{
    public NativeOsTheoryAttribute()
    {
        var rom = Environment.GetEnvironmentVariable("COPPERSHARP_KICKSTART31_ROM");
        if (string.IsNullOrEmpty(rom))
            Skip = "Set COPPERSHARP_KICKSTART31_ROM to a legally supplied Kickstart 3.1 ROM.";
    }
}

public sealed class NativeOsExecutionTests
{
    [NativeOsTheory]
    [Trait("Category", "NativeOS")]
    [InlineData("Answer", 42u, "")]
    [InlineData("HighBitAnswer", 0xdeadbeefu, "")]
    [InlineData("DosProbe", 42u, "")]
    [InlineData("ArgumentProbe", 42u, "OK")]
    public async Task NativeDosLoadsExecutesAndReportsPayload(string entry, uint expected, string arguments)
    {
        var payload = AmigaM68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = typeof(NativeOsFixture).Assembly.Location,
            EntryPoint = $"{typeof(NativeOsFixture).FullName}::{entry}",
            RuntimeProfile = M68kRuntimeProfile.Application,
            ExceptionMode = M68kExceptionMode.Full,
            IncludedExportNames = []
        }).Image;
        var result = await Run(payload, arguments, 1500);
        Assert.True(result.StopReason == "ProgramReturned", JsonSerializer.Serialize(result));
        Assert.Equal(0u, result.LaunchStatus);
        Assert.Equal(expected, result.ReturnValue);
        Assert.Null(result.DiskSnapshotError);
        if (entry == "DosProbe") Assert.Equal("native DOS payload\n", result.PayloadOutput);
    }

    [NativeOsTheory]
    [Trait("Category", "NativeOS")]
    [InlineData(1, "FrameLimitReached", uint.MaxValue)]
    [InlineData(1500, "LaunchFailed", 1u)]
    public async Task IncompleteRunAndInvalidHunkCannotPass(int maxFrames, string reason, uint status)
    {
        // HUNK_HEADER alone is deliberately truncated: native LoadSeg must reject it.
        var result = await Run([0, 0, 3, 0xf3], "", maxFrames);
        Assert.True(result.StopReason == reason, JsonSerializer.Serialize(result));
        Assert.Equal(status, result.LaunchStatus);
    }

    private static async Task<NativeOsResult> Run(byte[] payload, string arguments, int frames) =>
        await NativeOsSession.RunAsync(payload,
            await File.ReadAllBytesAsync(Environment.GetEnvironmentVariable("COPPERSHARP_KICKSTART31_ROM")!),
            arguments, M68kCpuModel.M68000, false, frames, TimeSpan.FromSeconds(120));
}
