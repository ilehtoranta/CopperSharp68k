namespace CopperSharp.Compiler.Tests;

public sealed class FrameHomeRomOptInTests
{
	public static IEnumerable<object[]> Cases()
	{
		foreach (var shape in M68kFrameClearRunsExecutionTests.Shapes)
		foreach (var pressure in M68kFrameClearRunsExecutionTests.Pressures)
		foreach (var remainder in new uint[] { 0, 2 })
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled,
			M68kPeepholeOptimizationMode.FixedPoint })
			yield return [shape.Name, pressure.Name, remainder, mode];
	}

	[Theory, MemberData(nameof(Cases))]
	public void FrameHomeOptionPreservesInitializedBytesRegistersStackAndCycles(
		string shape, string pressure, uint remainder, M68kPeepholeOptimizationMode mode)
	{
		var baseline = M68kFrameClearRunsExecutionTests.Measure(shape, pressure,
			M68kCpuTarget.M68000, M68kClrPolicy.Always, mode, false, remainder,
			romProfile: true);
		var compact = M68kFrameClearRunsExecutionTests.Measure(shape, pressure,
			M68kCpuTarget.M68000, M68kClrPolicy.Always, mode, false, remainder,
			romProfile: true, frameOption: true);
		Assert.Equal(baseline.FrameSHA256, compact.FrameSHA256);
		Assert.Equal(baseline.MethodCilSHA256, compact.MethodCilSHA256);
		Assert.True(compact.InitializerBytes <= baseline.InitializerBytes,
			$"{shape}/{pressure}/{mode}/SP+{remainder}: {compact.InitializerBytes} > {baseline.InitializerBytes}");
		Assert.True(compact.Cycles <= baseline.Cycles,
			$"{shape}/{pressure}/{mode}/SP+{remainder}: {compact.Cycles} > {baseline.Cycles}");
	}

	[Fact]
	public void LargeContiguousFrameGetsSmallerInitializer()
	{
		var baseline = M68kFrameClearRunsExecutionTests.Measure("single-contiguous", "free",
			M68kCpuTarget.M68000, M68kClrPolicy.Always,
			M68kPeepholeOptimizationMode.FixedPoint, false, 0, romProfile: true);
		var compact = M68kFrameClearRunsExecutionTests.Measure("single-contiguous", "free",
			M68kCpuTarget.M68000, M68kClrPolicy.Always,
			M68kPeepholeOptimizationMode.FixedPoint, false, 0, romProfile: true,
			frameOption: true);
		Assert.True(compact.InitializerBytes < baseline.InitializerBytes);
	}
}
