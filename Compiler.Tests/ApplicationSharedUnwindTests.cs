using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Fact]
	public void ApplicationSharedUnwindHelperRestoresAllPhysicalRegistersAndSnapshots()
	{
		var request = new M68kCompilationRequest {
			AssemblyPath = Path.Combine(AppContext.BaseDirectory, "ConsoleIO.dll"),
			EntryPoint = "ConsoleIOExample.Program::Main", Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk, RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = M68kExceptionMode.Full, Hunk = new() { IncludeSymbols = false },
			CodeSizeOptimizations = new() { ShareApplicationUnwindHelper = true }
		};
		var baseline = AmigaM68kCompiler.Compile(request with {
			CodeSizeOptimizations = new() { ShareApplicationUnwindHelper = false }
		});
		var compact = AmigaM68kCompiler.Compile(request);
		Assert.True(compact.Image.Length < baseline.Image.Length);
		var helper = FindSharedUnwindHelper(compact.Code);
		Assert.True(helper >= 0);
		foreach (var mask in new ushort[] { 0, 1, (1 << 1) | (1 << 10),
			(1 << 0) | (1 << 6) | (1 << 9),
			(1 << 0) | (1 << 2) | (1 << 5) | (1 << 6) | (1 << 9) | (1 << 10), 0x7ff })
		foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
		foreach (uint load in new uint[] { 0x10000, 0x20000 })
		foreach (var frameBytes in new[] { 0, 16, 65536 })
		foreach (var initialX in new[] { false, true })
		{
			const uint descriptor = 0x50000, context = 0x51000, frame = 0x52000;
			var bus = LoadApplicationSizeHunk(compact.Image, load, 0x40000);
			var count = System.Numerics.BitOperations.PopCount((uint)mask);
			bus.WriteLong(descriptor, (uint)frameBytes);
			bus.WriteLong(descriptor + 4, (uint)(count * 4));
			bus.WriteLong(descriptor + 8, mask);
			var expected = new uint[11];
			var rank = 0;
			for (var index = 0; index < expected.Length; index++)
			{
				expected[index] = 0x1100_0000u + (uint)index;
				if ((mask & (1 << index)) != 0)
				{
					expected[index] = 0x2200_0000u + (uint)index;
					var savedIndex = count < 3 ? count - 1 - rank : rank;
					bus.WriteLong(frame + (uint)frameBytes + (uint)(savedIndex * 4), expected[index]);
					rank++;
				}
				bus.WriteLong(context + 24 + (uint)(index * 4), 0x1100_0000u + (uint)index);
			}
			Execute(bus, model, load + (uint)helper, maxInstructions: 1_000,
				initialize: state => {
					state.A[0] = descriptor;
					state.A[1] = frame;
					state.D[1] = context;
					for (var index = 0; index < 6; index++) state.D[index + 2] = 0x3300_0000u + (uint)index;
					for (var index = 0; index < 5; index++) state.A[index + 2] = 0x4400_0000u + (uint)index;
					state.StatusRegister = (ushort)((state.StatusRegister & ~0x0010) |
						(initialX ? 0x0010 : 0));
				}, afterReturn: state => {
					for (var index = 0; index < 6; index++) Assert.Equal(expected[index], state.D[index + 2]);
					for (var index = 0; index < 5; index++) Assert.Equal(expected[index + 6], state.A[index + 2]);
					for (var index = 0; index < 11; index++)
						Assert.Equal(expected[index], bus.ReadLong(context + 24 + (uint)(index * 4)));
					Assert.Equal(context, state.D[1]);
					Assert.Equal(StackPointer + 4, state.A[7]);
					Assert.Equal(initialX ? 0x0010 : 0, state.StatusRegister & 0x0010);
				});
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ApplicationSharedUnwindHelperPreservesExceptionAndGcExecution(bool managedPool)
	{
		var request = managedPool
			? CompactGcRequest(typeof(ApplicationSharedUnwindFixtures).FullName + "::ExceptionGcPaths")
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
				CodeSizeOptimizations = new() { ShareApplicationUnwindHelper = enabled,
					PruneApplicationUnwindMetadata = managedPool }
			});
			var helper = FindSharedUnwindHelper(result.Code);
			if (!enabled) Assert.Equal(-1, helper);
			else if (managedPool) Assert.True(helper >= 0);
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (uint load in new uint[] { 0x10000, 0x20000 })
			{
				var helperCalls = 0;
				Assert.Equal(42u, Execute(LoadApplicationSizeHunk(result.Image, load, 0x40000),
					model, load + result.EntryPoint, maxInstructions: 5_000_000,
					beforeInstruction: (cpu, _) => {
						if (helper >= 0 && cpu.State.ProgramCounter == load + (uint)helper) helperCalls++;
					}));
				if (enabled && managedPool) Assert.True(helperCalls > 0);
			}
		}
	}

	[Fact]
	public void ApplicationSharedUnwindHelperIsDisabledForExternalCollector()
	{
		var request = new M68kCompilationRequest {
			AssemblyPath = FixtureAssembly,
			EntryPoint = typeof(ApplicationMetadataFixtures).FullName + "::ExceptionPaths",
			Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application, ExceptionMode = M68kExceptionMode.Full,
			Imports = new Dictionary<string, uint> { [M68kRuntimeImports.GcCollect] = 0x2C00 },
			IncludedExportNames = [], Hunk = new() { IncludeSymbols = false }
		};
		var baseline = M68kCompiler.Compile(request with {
			CodeSizeOptimizations = new() { ShareApplicationUnwindHelper = false }
		});
		var enabled = M68kCompiler.Compile(request with {
			CodeSizeOptimizations = new() { ShareApplicationUnwindHelper = true }
		});
		Assert.Equal(baseline.Image, enabled.Image);
	}

	private static int FindSharedUnwindHelper(byte[] code)
	{
		ReadOnlySpan<byte> signature = [
			0x20, 0x28, 0x00, 0x08, // MOVE.L 8(A0),D0 mask
			0x26, 0x10,             // MOVE.L (A0),D3
			0xD3, 0xC3,             // ADDA.L D3,A1
			0x26, 0x28, 0x00, 0x04, // MOVE.L 4(A0),D3
			0x74, 0x04,             // MOVEQ #4,D2
			0x0C, 0x43, 0x00, 0x0C  // CMPI.W #12,D3
		];
		var offset = code.AsSpan().IndexOf(signature);
		if (offset >= 0) Assert.Equal(-1, code.AsSpan(offset + 1).IndexOf(signature));
		return offset;
	}
}

public static class ApplicationSharedUnwindFixtures
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ExceptionGcPaths()
	{
		var sum = ApplicationMetadataFixtures.ExceptionPaths() +
			CompilerFixtures.PoolCollectTracesCallerFrameEntry() +
			CompilerFixtures.CollectorFrameAnchorRetainsCallerArrayEntry() +
			TypeInitializationRuntimeFixtures.FailureEntry();
		return sum == 4 * 42 ? 42 : 0;
	}
}
