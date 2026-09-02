using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class ValueTypeConstructionNoHeapTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;

	public static TheoryData<M68kCpuTarget, M68kCpuModel,
		M68kPeepholeOptimizationMode, M68kRuntimeProfile> ExecutionCases
	{
		get
		{
			var cases = new TheoryData<M68kCpuTarget, M68kCpuModel,
				M68kPeepholeOptimizationMode, M68kRuntimeProfile>();
			foreach (var (target, model) in new[]
			{
				(M68kCpuTarget.M68000, M68kCpuModel.M68000),
				(M68kCpuTarget.M68020, M68kCpuModel.M68020),
				(M68kCpuTarget.M68040, M68kCpuModel.M68040)
			})
			foreach (var mode in new[]
			{
				M68kPeepholeOptimizationMode.FixedPoint,
				M68kPeepholeOptimizationMode.Disabled
			})
			foreach (var profile in new[]
			{
				M68kRuntimeProfile.Freestanding,
				M68kRuntimeProfile.Resident
			})
				cases.Add(target, model, mode, profile);
			return cases;
		}
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void ReadonlyAggregateNewobjStoredThroughOutExecutesWithoutHeap(
		M68kCpuTarget target, M68kCpuModel model,
		M68kPeepholeOptimizationMode mode, M68kRuntimeProfile profile)
	{
		using (var module = new CompilationModule(typeof(ValueTypeConstructionNoHeapFixture).Assembly.Location))
		{
			var capture = module.ResolveEntryPoint(
				$"{typeof(ValueTypeConstructionNoHeapFixture).FullName}::Capture");
			Assert.Contains(capture.Instructions, instruction =>
				instruction.OpCode == OpCodes.Newobj &&
				module.ResolveMethodToken((int)instruction.Operand!, capture,
					instruction.Offset).Definition is { } constructor &&
				module.IsValueTypeConstructor(constructor) &&
				!module.IsTransparentScalarConstructor(constructor));
		}

		Assert.Equal(42u, ValueTypeConstructionNoHeapFixture.Entry());
		var result = AmigaM68kCompiler.Compile(Request(
			nameof(ValueTypeConstructionNoHeapFixture.Entry), target, mode, profile));
		Assert.Empty(result.FrameworkAnalysis.ManagedAllocationSites);
		Assert.Equal(M68kMemoryManagement.None, result.NativeCompatibility.MemoryManagement);
		Assert.Empty(result.NativeCompatibility.RuntimeFeatures);
		Assert.Empty(result.NativeCompatibility.RuntimeHelpers);
		Assert.Empty(result.NativeCompatibility.ExternalNativeTargets);

		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = LoadAddress + unchecked((uint)relocation.Offset);
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		var loadedImage = bus.Memory.AsSpan((int)LoadAddress, result.Code.Length).ToArray();
		bus.WriteLong(StackPointer, ReturnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(LoadAddress + result.EntryPoint, StackPointer);
		for (var instruction = 0; instruction < 20_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				Assert.Equal(StackPointer + 4, cpu.State.A[7]);
				Assert.Equal(42u, cpu.State.D[0]);
				Assert.Equal(loadedImage,
					bus.Memory.AsSpan((int)LoadAddress, result.Code.Length).ToArray());
				return;
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"{model}/{mode}/{profile} halted at ${cpu.State.ProgramCounter:X8}.");
		}
		Assert.Fail($"{model}/{mode}/{profile} did not return.");
	}

	[Theory]
	[InlineData(nameof(ValueTypeConstructionNoHeapFixture.DirectClass), "::DirectClass")]
	[InlineData(nameof(ValueTypeConstructionNoHeapFixture.DirectArray), "::DirectArray")]
	[InlineData(nameof(ValueTypeConstructionNoHeapFixture.NestedClass), "ClassAllocatingValue::.ctor")]
	[InlineData(nameof(ValueTypeConstructionNoHeapFixture.NestedArray), "ArrayAllocatingValue::.ctor")]
	public void RealAllocationsStillRequireHeapIncludingInsideValueConstructors(
		string entry, string diagnosticMethod)
	{
		foreach (var profile in new[]
		{
			M68kRuntimeProfile.Freestanding,
			M68kRuntimeProfile.Resident
		})
		{
			var error = Assert.Throws<M68kCompilationException>(() =>
				AmigaM68kCompiler.Compile(Request(entry, M68kCpuTarget.M68000,
					M68kPeepholeOptimizationMode.FixedPoint, profile)));
			Assert.Equal(M68kDiagnosticIds.StaticAnalysis, error.DiagnosticId);
			Assert.Contains("managed allocation requires a managed heap", error.Message,
				StringComparison.OrdinalIgnoreCase);
			Assert.Contains(diagnosticMethod, error.Method, StringComparison.Ordinal);
			Assert.NotNull(error.IlOffset);
		}
	}

	private static M68kCompilationRequest Request(string entry, M68kCpuTarget target,
		M68kPeepholeOptimizationMode mode, M68kRuntimeProfile profile) => new()
	{
		AssemblyPath = typeof(ValueTypeConstructionNoHeapFixture).Assembly.Location,
		EntryPoint = $"{typeof(ValueTypeConstructionNoHeapFixture).FullName}::{entry}",
		Cpu = target,
		OutputFormat = M68kOutputFormat.Hunk,
		RuntimeProfile = profile,
		MemoryManagement = M68kMemoryManagement.None,
		ExceptionMode = M68kExceptionMode.Yolo,
		PeepholeOptimization = mode,
		IncludedExportNames = []
	};
}

public static class ValueTypeConstructionNoHeapFixture
{
	public static uint Entry()
	{
		Capture(false, 205, out var value);
		if (value.IsCaptured || value.Value != 0)
			return 1;
		Capture(true, -101, out value);
		if (!value.IsCaptured || value.Value != -101)
			return 2;
		Capture(true, 0, out value);
		if (!value.IsCaptured || value.Value != 0)
			return 3;
		Capture(true, 205, out value);
		if (!value.IsCaptured || value.Value != 205)
			return 4;
		Capture(false, -101, out value);
		return !value.IsCaptured && value.Value == 0 ? 42u : 5u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Capture(bool capture, int rawValue, out CapturedValue value)
	{
		value = default;
		if (capture)
			value = new CapturedValue(rawValue);
	}

	public readonly struct CapturedValue
	{
		private readonly uint _captured;
		private readonly int _value;

		public CapturedValue(int value)
		{
			_captured = 1;
			_value = value;
		}

		public bool IsCaptured => _captured != 0;
		public int Value => _value;
	}

	public static int DirectClass() => new ReferenceValue(42).Value;
	public static int DirectArray() => new int[42].Length;
	public static int NestedClass()
	{
		WriteClass(out var value, 42);
		return value.Value;
	}
	public static int NestedArray()
	{
		WriteArray(out var value, 42);
		return value.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteClass(out ClassAllocatingValue value, int number) =>
		value = new ClassAllocatingValue(number);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteArray(out ArrayAllocatingValue value, int number) =>
		value = new ArrayAllocatingValue(number);

	private readonly struct ClassAllocatingValue
	{
		private readonly int _marker;
		private readonly int _value;
		public ClassAllocatingValue(int value)
		{
			_marker = 0;
			_value = new ReferenceValue(value).Value;
		}
		public int Value => _marker + _value;
	}

	private readonly struct ArrayAllocatingValue
	{
		private readonly int _marker;
		private readonly int _value;
		public ArrayAllocatingValue(int value)
		{
			_marker = 0;
			_value = new int[value].Length;
		}
		public int Value => _marker + _value;
	}

	private sealed class ReferenceValue
	{
		public ReferenceValue(int value) => Value = value;
		public int Value { get; }
	}
}
