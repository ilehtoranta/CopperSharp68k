using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class TrivialValueTypeConstructorInliningTests
{
	private const uint HunkLoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;

	public static TheoryData<M68kCpuTarget, M68kCpuModel> CpuTargets =>
		new()
		{
			{ M68kCpuTarget.M68000, M68kCpuModel.M68000 },
			{ M68kCpuTarget.M68020, M68kCpuModel.M68020 },
			{ M68kCpuTarget.M68040, M68kCpuModel.M68040 },
			{ M68kCpuTarget.M68060, M68kCpuModel.M68040 }
		};

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void TrivialValueTypeConstructorExecutesWithoutRetainedCall(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(target, M68kOutputFormat.Hunk, "EligibleEntry");

		Assert.Equal(42u, Execute(result, model));
		Assert.DoesNotContain(result.Symbols, IsEligibleConstructor);
	}

	[Fact]
	public void TrivialValueTypeConstructorIsStoredLocallyWithoutCallAssembly()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"EligibleEntry");

		Assert.DoesNotContain(result.Symbols, IsEligibleConstructor);
		Assert.Matches("\\tmove\\.l\\td[0-7],\\(a[0-6]\\)", result.Text!);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void MachineIrReplacesDirectAndConstrainedTrivialConstructorCalls(
		bool constrained)
	{
		using var module = new CompilationModule(
			Assembly.GetExecutingAssembly().Location);
		var entry = module.ResolveEntryPoint(
			"CopperSharp.Compiler.Tests.TrivialValueTypeConstructorFixtures::EligibleEntry");
		var constructor = entry.Instructions
			.Where(instruction => instruction.OpCode ==
				System.Reflection.Emit.OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken(
				(int)instruction.Operand!, entry, instruction.Offset).Definition)
			.Single(method => method?.Name == ".ctor")!;
		var caller = CilMachineIrBuilder.Build(entry, module);
		var target = CilMachineIrBuilder.Build(
			constructor,
			module,
			argumentRegisters: [M68kRegister.A0, M68kRegister.D0]);
		if (constrained)
		{
			var callBlock = caller.Blocks.Single(block => block.Instructions.Any(
				instruction => instruction.LogicalCall?.ResolvedTargets.Contains(
					constructor.Identity) == true));
			var callIndex = callBlock.Instructions.FindIndex(instruction =>
				instruction.LogicalCall?.ResolvedTargets.Contains(
					constructor.Identity) == true);
			var call = callBlock.Instructions[callIndex];
			callBlock.Instructions[callIndex] = call with
			{
				LogicalCall = call.LogicalCall! with
				{
					DispatchKind = M68kMachineCallDispatchKind.Constrained
				}
			};
		}

		var statistics = M68kMachineModuleOptimizer.Run(
			[entry, constructor],
			new Dictionary<CilMethodIdentity, M68kMachineFunction>
			{
				[entry.Identity] = caller,
				[constructor.Identity] = target
			},
			module,
			M68kCpuTarget.M68000,
			new HashSet<CilMethodIdentity> { entry.Identity });

		var callerInstructions = caller.Blocks
			.SelectMany(static block => block.Instructions)
			.ToArray();
		Assert.Equal(1, statistics.InlinedCalls);
		Assert.DoesNotContain(callerInstructions, instruction =>
			instruction.LogicalCall?.ResolvedTargets.Contains(
				constructor.Identity) == true);
		var clonedStore = Assert.Single(callerInstructions.Where(static instruction =>
			instruction.Operation == M68kMachineOperation.Store &&
			instruction.SourceInstruction?.OpCode ==
				System.Reflection.Emit.OpCodes.Stfld));
		var sourceStore = Assert.Single(target.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction =>
				instruction.Operation == M68kMachineOperation.Store));
		Assert.Equal(sourceStore.Clobbers, clonedStore.Clobbers);
		Assert.Equal(sourceStore.MemoryEffect, clonedStore.MemoryEffect);
		Assert.Equal(sourceStore.IsSafepoint, clonedStore.IsSafepoint);
		Assert.Equal(sourceStore.MayThrow, clonedStore.MayThrow);
		Assert.Equal(
			sourceStore.ProducesConditionCodes,
			clonedStore.ProducesConditionCodes);
		Assert.Equal(
			sourceStore.ConsumesConditionCodes,
			clonedStore.ConsumesConditionCodes);
		Assert.Single(clonedStore.Origin!.InlineSites);
	}

	[Fact]
	public void NontrivialThrowingAndReferenceConstructorsRemainCalls()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"RejectedEntry",
			new Dictionary<string, uint>
			{
				[M68kRuntimeImports.Allocate] = 0x0000_2800
			});

		Assert.Contains(result.Symbols, symbol => IsConstructorOf(symbol, "NontrivialValue"));
		Assert.Contains(result.Symbols, symbol => IsConstructorOf(symbol, "ThrowingValue"));
		Assert.Contains(result.Symbols, symbol => IsConstructorOf(symbol, "ReferenceValue"));
	}

	private static bool IsEligibleConstructor(M68kSymbol symbol) =>
		IsConstructorOf(symbol, "TrivialValue");

	private static bool IsConstructorOf(M68kSymbol symbol, string typeName) =>
		symbol.Name.Contains(typeName, StringComparison.Ordinal) &&
		symbol.Name.Contains("::.ctor", StringComparison.Ordinal);

	private static M68kCompilationResult Compile(
		M68kCpuTarget cpu,
		M68kOutputFormat format,
		string entry,
		IReadOnlyDictionary<string, uint>? imports = null) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint =
				$"CopperSharp.Compiler.Tests.TrivialValueTypeConstructorFixtures::{entry}",
			Cpu = cpu,
			OutputFormat = format,
			Imports = imports ?? new Dictionary<string, uint>()
		});

	private static uint Execute(M68kCompilationResult result, M68kCpuModel model)
	{
		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)HunkLoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = HunkLoadAddress + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + HunkLoadAddress);
		}
		bus.WriteLong(StackPointer, ReturnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(HunkLoadAddress + result.EntryPoint, StackPointer);
		for (var instruction = 0; instruction < 10_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
				return cpu.State.D[0];
			cpu.ExecuteInstruction();
			if (cpu.State.Halted)
			{
				throw new Xunit.Sdk.XunitException(
					$"{model} halted at ${cpu.State.ProgramCounter:X8}, " +
					$"last opcode ${cpu.State.LastOpcode:X4}.");
			}
		}
		throw new Xunit.Sdk.XunitException(
			$"{model} did not return after 10000 instructions; " +
			$"PC=${cpu.State.ProgramCounter:X8}.");
	}
}
