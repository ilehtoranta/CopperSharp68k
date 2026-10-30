using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Fact]
	public void StaticVirtualConstrainedCallPreservesFourBytePlatformAndMixedArgumentAbi()
	{
		const string entry =
			"CopperSharp.Compiler.Tests.CompilerFixtures::StaticConstrainedAbiEntry";
		Assert.Equal(42u, CompilerFixtures.StaticConstrainedAbiEntry());

		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Hunk,
			entry);
		Assert.Equal(42u, ExecuteHunk(result, M68kCpuModel.M68000));
	}
}