using System.Reflection;
using System.Reflection.Emit;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class GuestMemoryWrapperFoldingTests
{
	private const uint HunkLoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;
	private const uint GuestAddress = 0x0000_3000;

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
	public void FoldedReadWriteWidthsPreserveExecutionMemoryAndConditionCodes(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var folded = Compile(target, M68kOutputFormat.Hunk, "EligibleEntry");
		var direct = Compile(target, M68kOutputFormat.Hunk, "DirectEntry");

		var foldedState = Execute(folded, model, 0x15);
		var directState = Execute(direct, model, 0x15);

		Assert.Equal(0x2CB9_F9EFu, foldedState.Result);
		Assert.Equal(directState.Result, foldedState.Result);
		Assert.Equal(directState.ConditionCodes, foldedState.ConditionCodes);
		Assert.Equal(directState.GuestBytes, foldedState.GuestBytes);
		Assert.Equal(
			[0xA5, 0x00, 0x12, 0x34, 0x89, 0xAB, 0xCD, 0xEF],
			foldedState.GuestBytes);
	}

	[Fact]
	public void EveryReadWriteWidthWrapperIsRemovedWithoutInliningAttribute()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"EligibleEntry");

		foreach (var method in new[]
		{
			"ReadUInt8", "ReadUInt16", "ReadUInt32",
			"WriteUInt8", "WriteUInt16", "WriteUInt32"
		})
		{
			Assert.True(
				!result.Symbols.Any(symbol => IsAdapterMethod(symbol, method)),
				$"Retained {method}\n{result.Map}");
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void ConstrainedGenericWrapperFoldingPreservesExecution(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var result = Compile(target, M68kOutputFormat.Hunk, "ConstrainedEntry");

		Assert.Equal(0x1357_9BDFu, Execute(result, model, 0).Result);
		Assert.Contains("inlined-calls=2", result.Map, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldedMachineCallRetainsIntrinsicFaultMemoryAndCcrMetadata()
	{
		using var module = new CompilationModule(Assembly.GetExecutingAssembly().Location);
		var callerMethod = module.ResolveEntryPoint(
			"CopperSharp.Compiler.Tests.GuestMemoryWrapperFoldingFixtures::" +
			"ReadUInt8Caller");
		var callSource = callerMethod.Instructions.Single(instruction =>
			instruction.OpCode == OpCodes.Call);
		var targetMethod = module.ResolveMethodToken(
			(int)callSource.Operand!,
			callerMethod,
			callSource.Offset).Definition!;
		var caller = CilMachineIrBuilder.Build(callerMethod, module);
		var target = CilMachineIrBuilder.Build(
			targetMethod,
			module,
			argumentRegisters:
				[M68kRegister.A0, M68kRegister.A1, M68kRegister.D0]);
		var sourceIntrinsic = Assert.Single(target.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(instruction => IsGuestIntrinsic(instruction, module)));

		var statistics = M68kMachineModuleOptimizer.Run(
			[callerMethod, targetMethod],
			new Dictionary<CilMethodIdentity, M68kMachineFunction>
			{
				[callerMethod.Identity] = caller,
				[targetMethod.Identity] = target
			},
			module,
			M68kCpuTarget.M68000,
			new HashSet<CilMethodIdentity> { callerMethod.Identity });

		Assert.True(
			statistics.InlinedCalls == 1,
			$"Target instance={targetMethod.Signature.Header.IsInstance}, " +
			$"parameters={targetMethod.Signature.ParameterTypes.Length}\n" +
			Describe(target) + "\nCALLER\n" + Describe(caller));
		var foldedIntrinsic = Assert.Single(caller.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(instruction => IsGuestIntrinsic(instruction, module)));
		Assert.Equal(2, foldedIntrinsic.Uses.Length);
		Assert.Equal(sourceIntrinsic.Clobbers, foldedIntrinsic.Clobbers);
		Assert.Equal(sourceIntrinsic.MemoryEffect, foldedIntrinsic.MemoryEffect);
		Assert.Equal(sourceIntrinsic.IsSafepoint, foldedIntrinsic.IsSafepoint);
		Assert.Equal(sourceIntrinsic.MayThrow, foldedIntrinsic.MayThrow);
		Assert.Equal(
			sourceIntrinsic.ProducesConditionCodes,
			foldedIntrinsic.ProducesConditionCodes);
		Assert.Equal(
			sourceIntrinsic.ConsumesConditionCodes,
			foldedIntrinsic.ConsumesConditionCodes);
		Assert.Equal(
			sourceIntrinsic.RequiresLiveCallerFrame,
			foldedIntrinsic.RequiresLiveCallerFrame);
		Assert.Single(foldedIntrinsic.Origin!.InlineSites);
	}

	[Fact]
	public void NullCheckingReferenceReceiverIsNotFolded()
	{
		using var module = new CompilationModule(Assembly.GetExecutingAssembly().Location);
		var callerMethod = module.ResolveEntryPoint(
			"CopperSharp.Compiler.Tests.GuestMemoryWrapperFoldingFixtures::" +
			"ReferenceCaller");
		var callSource = callerMethod.Instructions.Single(instruction =>
			instruction.OpCode == OpCodes.Callvirt);
		var targetMethod = module.ResolveMethodToken(
			(int)callSource.Operand!,
			callerMethod,
			callSource.Offset).Definition!;
		var caller = CilMachineIrBuilder.Build(callerMethod, module);
		var target = CilMachineIrBuilder.Build(targetMethod, module);

		var statistics = M68kMachineModuleOptimizer.Run(
			[callerMethod, targetMethod],
			new Dictionary<CilMethodIdentity, M68kMachineFunction>
			{
				[callerMethod.Identity] = caller,
				[targetMethod.Identity] = target
			},
			module,
			M68kCpuTarget.M68000,
			new HashSet<CilMethodIdentity> { callerMethod.Identity });

		Assert.Equal(0, statistics.InlinedCalls);
		var retainedCall = Assert.Single(caller.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(instruction => instruction.LogicalCall?.ResolvedTargets.Contains(
				targetMethod.Identity) == true));
		Assert.True(retainedCall.LogicalCall!.RequiresNullCheck);
	}

	[Fact]
	public void AlteredMultipleReceiverTouchingAndNoInlineWrappersRemainCalls()
	{
		var result = Compile(
			M68kCpuTarget.M68000,
			M68kOutputFormat.Assembly,
			"NegativeEntry");

		foreach (var method in new[]
		{
			"ReadAdjusted", "ReadTwice", "ReadAndTouchReceiver",
			"ReadAfterMerge", "NoInlineRead"
		})
		{
			Assert.Contains(result.Symbols, symbol =>
				IsAdapterMethod(symbol, method));
		}
	}

	private static bool IsGuestIntrinsic(
		M68kMachineInstruction instruction,
		CompilationModule module)
	{
		if (instruction.Operation != M68kMachineOperation.Call ||
			instruction.Origin is not { SourceInstruction.Operand: int token } origin)
		{
			return false;
		}
		var target = module.ResolveMethodToken(
			token,
			origin.SourceMethod,
			origin.SourceInstruction.Offset);
		return M68kMachineInliningPolicy.IsGuestMemoryIntrinsic(target.ImportName);
	}

	private static string Describe(M68kMachineFunction function) =>
		string.Join("\n", function.Blocks.SelectMany(block =>
			block.Instructions.Prepend(null).Select(instruction => instruction is null
				? $"block {block.Id} pred=[{string.Join(',', block.Predecessors)}] " +
				  $"succ=[{string.Join(',', block.Successors)}]"
				:
				$"{instruction.Id} {instruction.Operation} " +
				$"u=[{string.Join(',', instruction.Uses.Select(value => DescribeValue(function.Values[value])))}] " +
				$"d=[{string.Join(',', instruction.Definitions.Select(value => DescribeValue(function.Values[value])))}] " +
				$"arg={instruction.ArgumentIndex} imm={instruction.Immediate} " +
				$"logical={instruction.LogicalCall?.DispatchKind} " +
				$"la=[{string.Join(',', instruction.LogicalCall?.ArgumentValueIds ?? [])}] " +
				$"lr=[{string.Join(',', instruction.LogicalCall?.ResultValueIds ?? [])}]")));

	private static string DescribeValue(M68kMachineValue value) =>
		$"v{value.Id}:{value.Kind}:{value.Width}:${value.AllowedRegisters.Bits:X4}:" +
		$"{value.PrecoloredRegister}:gc={value.IsGcReference}";

	private static bool IsAdapterMethod(M68kSymbol symbol, string method) =>
		symbol.Name.Contains("GuestMemoryAdapter::", StringComparison.Ordinal) &&
		symbol.Name.Contains($"::{method}", StringComparison.Ordinal);

	private static M68kCompilationResult Compile(
		M68kCpuTarget cpu,
		M68kOutputFormat format,
		string entry) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint =
				$"CopperSharp.Compiler.Tests.GuestMemoryWrapperFoldingFixtures::{entry}",
			Cpu = cpu,
			OutputFormat = format
		});

	private static ExecutionSnapshot Execute(
		M68kCompilationResult result,
		M68kCpuModel model,
		ushort initialConditionCodes)
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
		cpu.State.StatusRegister = (ushort)(0x2000 | (initialConditionCodes & 0x1F));
		for (var instruction = 0; instruction < 10_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				return new ExecutionSnapshot(
					cpu.State.D[0],
					(ushort)(cpu.State.StatusRegister & 0x1F),
					bus.Memory.AsSpan((int)GuestAddress, 8).ToArray());
			}
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

	private sealed record ExecutionSnapshot(
		uint Result,
		ushort ConditionCodes,
		byte[] GuestBytes);
}
