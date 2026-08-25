using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class AmigaTargetManagedAssemblyTests
{
	[Fact]
	public void ResolvesSdkFromTargetDirectoryWhenInputDirectoryDoesNotContainIt()
	{
		CompileConsoleIoFromIsolatedDirectory(copySdk: false);
	}

	[Fact]
	public void PrefersInputDirectorySdkOverTargetDirectoryFallback()
	{
		CompileConsoleIoFromIsolatedDirectory(copySdk: true);
	}

	private static void CompileConsoleIoFromIsolatedDirectory(bool copySdk)
	{
		var temporaryDirectory = Path.Combine(
			Path.GetTempPath(),
			"CopperSharpAmigaTargetTests",
			Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(temporaryDirectory);
		try
		{
			var assemblyPath = Path.Combine(temporaryDirectory, "ConsoleIO.dll");
			File.Copy(Path.Combine(AppContext.BaseDirectory, "ConsoleIO.dll"), assemblyPath);
			if (copySdk)
			{
				File.Copy(
					Path.Combine(AppContext.BaseDirectory, "CopperSharp.Sdk.Amiga.dll"),
					Path.Combine(temporaryDirectory, "CopperSharp.Sdk.Amiga.dll"));
			}

			var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = assemblyPath,
				EntryPoint = "ConsoleIOExample.Program::Main",
				Cpu = M68kCpuTarget.M68000,
				ExceptionMode = M68kExceptionMode.Full,
				OutputFormat = M68kOutputFormat.Assembly,
				RuntimeProfile = M68kRuntimeProfile.Application
			});

			Assert.NotNull(result.Text);
			Assert.NotEmpty(result.Text!);
		}
		finally
		{
			Directory.Delete(temporaryDirectory, recursive: true);
		}
	}
}
