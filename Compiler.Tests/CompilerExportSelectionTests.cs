using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class CompilerExportSelectionTests
{
	private static readonly string FixtureAssembly =
		typeof(CompilerFixtures).Assembly.Location;

	[Fact]
	public void EmptyAllowlistExcludesOtherwiseGlobalExportRoot()
	{
		var request = CreateRequest(
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShiftAndCompare") with
		{
			IncludedExportNames = Array.Empty<string>()
		};

		var analysis = AmigaM68kCompiler.AnalyzeFramework(request);
		var result = AmigaM68kCompiler.Compile(request);

		Assert.Equal(1, analysis.RootMethodCount);
		Assert.DoesNotContain(result.Symbols,
			symbol => symbol.Name == "fixture.add");
	}

	[Fact]
	public void NamedAllowlistEmitsAndRootsOnlySelectedExport()
	{
		var request = CreateRequest(
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShiftAndCompare") with
		{
			IncludedExportNames = ["fixture.add"]
		};

		var analysis = AmigaM68kCompiler.AnalyzeFramework(request);
		var result = AmigaM68kCompiler.Compile(request);

		Assert.Equal(2, analysis.RootMethodCount);
		Assert.Single(result.Symbols,
			symbol => symbol.Name == "fixture.add");
	}

	[Fact]
	public void NullAllowlistPreservesAllExportBehavior()
	{
		var result = AmigaM68kCompiler.Compile(CreateRequest(
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShiftAndCompare"));

		Assert.Contains(result.Symbols,
			symbol => symbol.Name == "fixture.add");
	}

	[Fact]
	public void ExportAddressCannotResolveExcludedDeclaration()
	{
		var request = CreateRequest(
			"CopperSharp.Compiler.Tests.CompilerFixtures::ExportAddressEntry") with
		{
			IncludedExportNames = Array.Empty<string>()
		};

		var error = Assert.Throws<M68kCompilationException>(() =>
			AmigaM68kCompiler.Compile(request));

		Assert.Equal(M68kDiagnosticIds.UnresolvedImport, error.DiagnosticId);
		Assert.Contains("fixture.add", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void UnknownAllowlistNameFailsBeforeCodeGeneration()
	{
		var request = CreateRequest(
			"CopperSharp.Compiler.Tests.CompilerFixtures::ShiftAndCompare") with
		{
			IncludedExportNames = ["fixture.missing"]
		};

		var error = Assert.Throws<M68kCompilationException>(() =>
			AmigaM68kCompiler.AnalyzeFramework(request));

		Assert.Equal(M68kDiagnosticIds.InvalidMetadata, error.DiagnosticId);
		Assert.Contains("fixture.missing", error.Message, StringComparison.Ordinal);
	}

	private static M68kCompilationRequest CreateRequest(string entry) => new()
	{
		AssemblyPath = FixtureAssembly,
		EntryPoint = entry,
		Cpu = M68kCpuTarget.M68000,
		ClrPolicy = M68kClrPolicy.Always,
		OutputFormat = M68kOutputFormat.Hunk,
		RuntimeProfile = M68kRuntimeProfile.Freestanding,
		MemoryManagement = M68kMemoryManagement.None
	};
}
