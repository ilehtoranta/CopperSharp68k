using System.Runtime.CompilerServices;
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Fact]
	public void ApplicationStaticPlacementPreservesMutationExceptionsAndGcRoots()
	{
		foreach (var compact in new[] { false, true })
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = typeof(ApplicationStaticAddressFixtures).Assembly.Location,
				EntryPoint = typeof(ApplicationStaticAddressFixtures).FullName + "::Entry",
				Cpu = M68kCpuTarget.M68000,
				OutputFormat = M68kOutputFormat.Hunk,
				RuntimeProfile = M68kRuntimeProfile.Application,
				ExceptionMode = M68kExceptionMode.Full,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new() { StartAddress = 0x60000, Size = 0x8000 },
				Imports = new Dictionary<string, uint>
				{
					[M68kRuntimeImports.Allocate] = 0x2800
				},
				IncludedExportNames = [],
				Hunk = new() { IncludeSymbols = false },
				CodeSizeOptimizations = new()
				{
					CompactApplicationStatics = compact
				}
			});
			Assert.Equal(M68kExceptionMode.Full, result.NativeCompatibility.ExceptionMode);
			Assert.Equal(M68kMemoryManagement.ManagedPoolMarkSweepGc,
				result.NativeCompatibility.MemoryManagement);
			foreach (var model in new[]
				{ M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (var codeBase in new uint[] { 0x10000, 0x20000 })
			{
				const uint bssBase = 0x40000;
				var bus = LoadApplicationSizeHunk(result.Image, codeBase, bssBase);
				bus.WriteLong(0x68000, 0x5A17C0DE);
				Assert.Equal(126u, Execute(bus, model, codeBase + result.EntryPoint,
					maxInstructions: 5_000_000,
					afterReturn: state => Assert.Equal(StackPointer + 4, state.A[7])));
				Assert.Equal(0x5A17C0DEu, bus.ReadLong(0x68000));
			}
		}
	}
}

public static class ApplicationStaticAddressFixtures
{
	private static int _hot;
	private static int _zero;
	private static int[]? _root;
	private static int _payload;
	private static int _caught;
	private static int _finally;
	private static int _cold0;
	private static int _cold1;
	private static int _cold2;
	private static int _cold3;
	private static int _cold4;
	private static int _cold5;
	private static int _cold6;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Entry()
	{
		_hot = 7;
		_zero = 0;
		InitializeColdFields();
		_root = new int[4];
		_root[0] = 13;
		var before = _hot;
		try
		{
			ChangeHotField();
			M68kRuntime.Collect();
			var transient = new int[4];
			transient[0] = 17;
			_payload = transient[0];
			_payload += 84 / _zero;
		}
		catch (DivideByZeroException)
		{
			_caught = 1;
		}
		finally
		{
			_finally = _hot + 3;
		}

		var after = _hot;
		return unchecked((uint)(before + after + _hot + _root[0] +
			_payload + _caught + _finally + SumColdFields()));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ChangeHotField() => _hot = 19;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void InitializeColdFields()
	{
		_cold0 = 1;
		_cold1 = 2;
		_cold2 = 3;
		_cold3 = 4;
		_cold4 = 5;
		_cold5 = 6;
		_cold6 = 7;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int SumColdFields() => unchecked(_cold0 + _cold1 +
		_cold2 + _cold3 + _cold4 + _cold5 + _cold6);
}
