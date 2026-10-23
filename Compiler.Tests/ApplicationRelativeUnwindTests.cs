using System.Buffers.Binary;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Fact]
	public void ApplicationRelativeUnwindRestoresRemovePrivateRelocationsWithoutChangingDescriptorTargets()
	{
		var request = new M68kCompilationRequest {
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "ConsoleIO.dll"),
			EntryPoint = "ConsoleIOExample.Program::Main", Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk, RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = M68kExceptionMode.Full, Hunk = new() { IncludeSymbols = false },
			CodeSizeOptimizations = new() { RelativeApplicationUnwindRestores = false,
				ShareApplicationUnwindHelper = false }
		};
		var absolute = AmigaM68kCompiler.Compile(request);
		var relative = AmigaM68kCompiler.Compile(request with {
			CodeSizeOptimizations = new() { RelativeApplicationUnwindRestores = true,
				ShareApplicationUnwindHelper = false }
		});
		var pointers = absolute.Relocations.Where(relocation =>
			relocation.Target.StartsWith("runtime:method-unwind-restore:", StringComparison.Ordinal)).ToArray();
		Assert.NotEmpty(pointers);
		Assert.Equal(absolute.Code.Length, relative.Code.Length);
		Assert.True(absolute.Image.Length - relative.Image.Length >= pointers.Length * 4);
		Assert.DoesNotContain(relative.Relocations, relocation =>
			relocation.Target.StartsWith("runtime:method-unwind-restore:", StringComparison.Ordinal));
		foreach (var pointer in pointers)
		{
			var absoluteTarget = BinaryPrimitives.ReadUInt32BigEndian(absolute.Code.AsSpan(pointer.Offset, 4));
			var relativeOffset = BinaryPrimitives.ReadInt32BigEndian(relative.Code.AsSpan(pointer.Offset, 4));
			Assert.Equal(absoluteTarget, unchecked((uint)(pointer.Offset - 8 + relativeOffset)));
		}
		Assert.True(relative.Code.AsSpan().IndexOf(new byte[] { 0xD1, 0xE8, 0x00, 0x08 }) >= 0);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ApplicationRelativeUnwindRestoresPreserveExceptionAndCollectorExecution(bool managedPool)
	{
		var request = managedPool
			? CompactGcRequest(typeof(TypeInitializationRuntimeFixtures).FullName + "::FailureEntry")
			: new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly,
				EntryPoint = typeof(ApplicationMetadataFixtures).FullName + "::ExceptionPaths",
				Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
				RuntimeProfile = M68kRuntimeProfile.Application, ExceptionMode = M68kExceptionMode.Full,
				IncludedExportNames = [], Hunk = new() { IncludeSymbols = false }, CodeSizeOptimizations = new()
			};
		foreach (var enabled in new[] { false, true })
		{
			var result = M68kCompiler.Compile(request with {
				CodeSizeOptimizations = new() { RelativeApplicationUnwindRestores = enabled,
					ShareApplicationUnwindHelper = false }
			});
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (uint load in new uint[] { 0x10000, 0x20000 })
				Assert.Equal(42u, Execute(LoadApplicationSizeHunk(result.Image, load, 0x40000),
					model, load + result.EntryPoint, maxInstructions: 5_000_000,
					afterReturn: state => Assert.Equal(StackPointer + 4, state.A[7])));
		}
	}

	[Fact]
	public void ApplicationRelativeUnwindRestoresRespectExternalCollectorAndPolicyGates()
	{
		var request = new M68kCompilationRequest {
			AssemblyPath = FixtureAssembly,
			EntryPoint = typeof(ApplicationMetadataFixtures).FullName + "::ExceptionPaths",
			Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application, ExceptionMode = M68kExceptionMode.Full,
			IncludedExportNames = [], Hunk = new() { IncludeSymbols = false }
		};
		foreach (var gated in new[] {
			request with { Imports = new Dictionary<string, uint> { [M68kRuntimeImports.GcCollect] = 0x2C00 } },
			request with { Cpu = M68kCpuTarget.M68020 }
		})
		{
			var absolute = M68kCompiler.Compile(gated with {
				CodeSizeOptimizations = new() { RelativeApplicationUnwindRestores = false,
					ShareApplicationUnwindHelper = false }
			});
			var relative = M68kCompiler.Compile(gated with {
				CodeSizeOptimizations = new() { RelativeApplicationUnwindRestores = true,
					ShareApplicationUnwindHelper = false }
			});
			Assert.Equal(absolute.Image, relative.Image);
		}
		var unshared = M68kCompiler.Compile(request with {
			CodeSizeOptimizations = new() {
				ShareApplicationRuntimeMetadata = false, RelativeApplicationUnwindRestores = false,
				ShareApplicationUnwindHelper = false }
		});
		var unsharedRelative = M68kCompiler.Compile(request with {
			CodeSizeOptimizations = new() {
				ShareApplicationRuntimeMetadata = false, RelativeApplicationUnwindRestores = true,
				ShareApplicationUnwindHelper = false }
		});
		Assert.Equal(unshared.Image, unsharedRelative.Image);
	}
}
