using System.Buffers.Binary;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeCallSiteTests
{
    [Theory]
    [InlineData(M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kPeepholeOptimizationMode.FixedPoint)]
    public void CaptureTracksFinalOffsetsWithoutChangingGuestCode(M68kPeepholeOptimizationMode mode)
    {
        var request = new M68kCompilationRequest {
            AssemblyPath = typeof(AmigaExternalCallClobberFixture).Assembly.Location,
            EntryPoint = typeof(AmigaExternalCallClobberFixture).FullName + "::MixedDosBases",
            IncludedExportNames = [], Cpu = M68kCpuTarget.M68000,
            RuntimeProfile = M68kRuntimeProfile.Freestanding,
            OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode
        };
        var plain = AmigaM68kCompiler.Compile(request);
        var captured = AmigaM68kCompiler.Compile(request with { CaptureNativeCallSites = true });
        Assert.Empty(plain.NativeCallSites);
        Assert.Equal(plain.Code, captured.Code);
        Assert.Equal(plain.Relocations, captured.Relocations);
        Assert.Equal(3, captured.NativeCallSites.Count(c => c.Displacement == -54));
        foreach (var call in captured.NativeCallSites) {
            Assert.Equal(M68kRegister.A6, call.BaseRegister);
            Assert.Equal(call.TailCall ? 0x4EEE : 0x4EAE,
                BinaryPrimitives.ReadUInt16BigEndian(captured.Code.AsSpan(call.Offset)));
            Assert.Equal(call.Displacement,
                BinaryPrimitives.ReadInt16BigEndian(captured.Code.AsSpan(call.Offset + 2)));
        }
    }
}
