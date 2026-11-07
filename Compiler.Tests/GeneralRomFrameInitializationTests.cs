namespace CopperSharp.Compiler.Tests;

public sealed class GeneralRomFrameInitializationTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var shape in M68kFrameClearRunsExecutionTests.Shapes)
        foreach (var pressure in M68kFrameClearRunsExecutionTests.Pressures)
            yield return [shape.Name, pressure.Name];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExplicitGeneralOptionMatchesTheExistingRomFramePolicy(string shape, string pressure)
    {
        foreach (var remainder in new uint[] { 0, 2 })
        {
            var baseline = M68kFrameClearRunsExecutionTests.Measure(shape, pressure, M68kCpuTarget.M68000,
                M68kClrPolicy.Always, M68kPeepholeOptimizationMode.FixedPoint, false, remainder,
                romProfile: true, frameOption: true);
            var general = M68kFrameClearRunsExecutionTests.Measure(shape, pressure, M68kCpuTarget.M68000,
                M68kClrPolicy.Always, M68kPeepholeOptimizationMode.FixedPoint, false, remainder,
                frameOption: true, generalFramePolicy: true);
            Assert.Equal(baseline.FrameSHA256, general.FrameSHA256);
            Assert.Equal(baseline.CodeSHA256, general.CodeSHA256);
            Assert.Equal(baseline.InitializerBytes, general.InitializerBytes);
            Assert.Equal(baseline.Cycles, general.Cycles);
        }
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, true)]
    [InlineData(M68kCpuTarget.M68020, false)]
    public void ExcludedCpuAndDynamicFramesRetainTheirExistingEmission(M68kCpuTarget target, bool dynamicFrame)
    {
        var baseline = M68kFrameClearRunsExecutionTests.Measure("two-33", "all-incoming", target,
            M68kClrPolicy.Always, M68kPeepholeOptimizationMode.FixedPoint, dynamicFrame, 0,
            generalFramePolicy: true);
        var enabled = M68kFrameClearRunsExecutionTests.Measure("two-33", "all-incoming", target,
            M68kClrPolicy.Always, M68kPeepholeOptimizationMode.FixedPoint, dynamicFrame, 0,
            frameOption: true, generalFramePolicy: true);
        Assert.Equal(baseline.FrameSHA256, enabled.FrameSHA256);
        Assert.Equal(baseline.CodeSHA256, enabled.CodeSHA256);
    }
}
