/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Tests;

public sealed class AggregateInterfaceReturnTests
{
	[Fact]
	public void ClassBoxedAndConstrainedReturnContractsMatchHost() =>
		Assert.Equal(42, CompilerFixtures.AggregateInterfaceReturnsPreserveWordsAndExceptionsEntry());
	[Fact]
	public void ReferenceBearingReturnContractsMatchHost() =>
		Assert.Equal(42, CompilerFixtures.AggregateInterfaceReferenceReturnsPreserveOwnersEntry());

	[Fact]
	public void ExplicitInterfaceArgumentsKeepTheirUnimplementedAdapterBoundary()
	{
		var exception = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::ParameterizedAggregateInterfaceReturnEntry"
		}));
		Assert.Contains("interface returns with explicit arguments", exception.Message, StringComparison.Ordinal);
	}
}
