using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class AmigaExternalCallClobberTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;
	private const uint ExecBase = 0x0000_4000;
	private const uint Process = 0x0000_5000;
	private const uint Port = Process + 92;
	private const uint Message = 0x0000_6000;
	private static readonly M68kRegister[] VolatileRegisters =
		[M68kRegister.D0, M68kRegister.D1, M68kRegister.A0, M68kRegister.A1];

	[Theory]
	[InlineData(AmigaLibraryBasePolicy.ExecBase)]
	[InlineData(AmigaLibraryBasePolicy.Manual)]
	[InlineData(AmigaLibraryBasePolicy.Provided)]
	[InlineData(AmigaLibraryBasePolicy.CallerProvided)]
	public void EverySupportedAmigaBasePolicyDeclaresTheSameScratchRegisters(
		AmigaLibraryBasePolicy policy)
	{
		var method = new M68kExternalMethod(
			"fixture", "fixture::Call", "fixture", "Call", true,
			[new(typeof(AmigaLibraryAttribute).FullName!, ["fixture.library", (int)policy])],
			[new(typeof(AmigaLvoAttribute).FullName!, [-30])],
			policy == AmigaLibraryBasePolicy.CallerProvided
				? [[new(typeof(M68kRegisterAttribute).FullName!, [(int)M68kRegister.A6])]]
				: [],
			[]);
		var resolver = new AmigaExternalCallResolver(new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint> { ["fixture.library"] = ExecBase }
		});

		Assert.True(resolver.TryResolve(method, out var convention));
		Assert.Equal(VolatileRegisters, convention.ClobberedRegisters);
	}

	[Fact]
	public void AmigaIndirectCallsAlsoDeclareTheScratchRegisters()
	{
		var method = new M68kExternalMethod(
			"fixture", "fixture::Call", "fixture", "Call", true, [],
			[new(typeof(AmigaIndirectCallAttribute).FullName!, [(int)M68kRegister.A3])],
			[[new(typeof(M68kRegisterAttribute).FullName!, [(int)M68kRegister.A3])]],
			[]);

		Assert.True(new AmigaExternalCallResolver().TryResolve(method, out var convention));
		Assert.Equal(VolatileRegisters, convention.ClobberedRegisters);
	}

	[Fact]
	public void GenericResolverWithoutAdditionalClobbersKeepsItsExistingRegisterMask()
	{
		using var module = new CompilationModule(
			typeof(AmigaExternalCallClobberFixture).Assembly.Location,
			[new GenericResolver(null)]);
		var entry = module.ResolveEntryPoint(
			$"{typeof(AmigaExternalCallClobberFixture).FullName}::GenericEntry");
		var function = CilMachineIrBuilder.Build(entry, module);
		var call = Assert.Single(function.Blocks.SelectMany(block => block.Instructions)
			.Where(instruction => instruction.Operation == M68kMachineOperation.Call));

		Assert.Equal(M68kRegisterSet.From(M68kRegister.D0, M68kRegister.A6), call.Clobbers);
	}

	[Theory]
	[InlineData((M68kRegister)(-1))]
	[InlineData((M68kRegister)15)]
	[InlineData((M68kRegister)256)]
	public void UnsupportedAdditionalClobberRegistersAreRejected(M68kRegister register)
	{
		var error = Assert.Throws<M68kCompilationException>(() =>
			M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = typeof(AmigaExternalCallClobberFixture).Assembly.Location,
				EntryPoint = $"{typeof(AmigaExternalCallClobberFixture).FullName}::GenericEntry",
				IncludedExportNames = [],
				ExternalCallResolvers = [new GenericResolver([register])]
			}));

		Assert.Contains("External clobbered registers must use D0-D7 or A0-A6.", error.Message);
	}

	public static TheoryData<
		M68kCpuTarget,
		M68kCpuModel,
		M68kPeepholeOptimizationMode,
		M68kRuntimeProfile> ExecutionCases
	{
		get
		{
			var cases = new TheoryData<
				M68kCpuTarget,
				M68kCpuModel,
				M68kPeepholeOptimizationMode,
				M68kRuntimeProfile>();
			foreach (var (target, model) in new[]
			{
				(M68kCpuTarget.M68000, M68kCpuModel.M68000),
				(M68kCpuTarget.M68020, M68kCpuModel.M68020),
				(M68kCpuTarget.M68040, M68kCpuModel.M68040)
			})
			{
				foreach (var mode in new[]
				{
					M68kPeepholeOptimizationMode.FixedPoint,
					M68kPeepholeOptimizationMode.Disabled
				})
				{
					cases.Add(target, model, mode, M68kRuntimeProfile.Application);
					cases.Add(target, model, mode, M68kRuntimeProfile.Resident);
				}
			}
			return cases;
		}
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void WaitPortPreservesPortForGetMsgAcrossUnusedVolatileRegisters(
		M68kCpuTarget target,
		M68kCpuModel model,
		M68kPeepholeOptimizationMode mode,
		M68kRuntimeProfile profile)
	{
		var result = Compile(nameof(AmigaExternalCallClobberFixture.WaitThenGet),
			target, mode, profile);
		var bus = CreateBus(result);
		var events = new List<string>();
		bus.RegisterGateway(ExecBase - 294, state =>
		{
			Assert.Equal(ExecBase, state.A[6]);
			Assert.Equal(0u, state.A[1]);
			events.Add("FindTask");
			ClobberVolatileRegisters(state, Process);
		});
		bus.RegisterGateway(ExecBase - 384, state =>
		{
			Assert.Equal(ExecBase, state.A[6]);
			Assert.Equal(Port, state.A[0]);
			events.Add("WaitPort");
			// D1 and A1 are scratch even though neither is an argument/result.
			ClobberVolatileRegisters(state, Message);
		});
		bus.RegisterGateway(ExecBase - 372, state =>
		{
			Assert.Equal(ExecBase, state.A[6]);
			Assert.Equal(Port, state.A[0]);
			events.Add("GetMsg");
			ClobberVolatileRegisters(state, Message);
		});

		Assert.Equal(Message, Execute(result, bus, model));
		Assert.Equal(["FindTask", "WaitPort", "GetMsg"], events);
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void VoidNoArgumentCallPreservesMessageForReplyAndReturn(
		M68kCpuTarget target,
		M68kCpuModel model,
		M68kPeepholeOptimizationMode mode,
		M68kRuntimeProfile profile)
	{
		var result = Compile(nameof(AmigaExternalCallClobberFixture.ForbidThenReply),
			target, mode, profile);
		var bus = CreateBus(result);
		var events = new List<string>();
		bus.RegisterGateway(ExecBase - 294, state =>
		{
			Assert.Equal(ExecBase, state.A[6]);
			Assert.Equal(0u, state.A[1]);
			events.Add("FindTask");
			ClobberVolatileRegisters(state, Message);
		});
		bus.RegisterGateway(ExecBase - 132, state =>
		{
			Assert.Equal(ExecBase, state.A[6]);
			events.Add("Forbid");
			// All four scratch registers may change despite the empty signature.
			ClobberVolatileRegisters(state, 0xD0D0_D0D0);
		});
		bus.RegisterGateway(ExecBase - 378, state =>
		{
			Assert.Equal(ExecBase, state.A[6]);
			Assert.Equal(Message, state.A[1]);
			events.Add("ReplyMsg");
			ClobberVolatileRegisters(state, 0xD0D0_D0D0);
		});

		Assert.Equal(Message, Execute(result, bus, model));
		Assert.Equal(["FindTask", "Forbid", "ReplyMsg"], events);
	}

	private static M68kCompilationResult Compile(
		string method,
		M68kCpuTarget target,
		M68kPeepholeOptimizationMode mode,
		M68kRuntimeProfile profile) =>
		AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(AmigaExternalCallClobberFixture).Assembly.Location,
			EntryPoint = $"{typeof(AmigaExternalCallClobberFixture).FullName}::{method}",
			IncludedExportNames = [],
			Cpu = target,
			OutputFormat = M68kOutputFormat.Hunk,
			PeepholeOptimization = mode,
			RuntimeProfile = profile
		});

	private static TestBus CreateBus(M68kCompilationResult result)
	{
		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = LoadAddress + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		bus.WriteLong(4, ExecBase);
		bus.WriteLong(StackPointer, ReturnSentinel);
		return bus;
	}

	private static uint Execute(
		M68kCompilationResult result,
		TestBus bus,
		M68kCpuModel model)
	{
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(LoadAddress + result.EntryPoint, StackPointer);
		cpu.State.A[0] = Message;
		for (var instruction = 0; instruction < 20_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				Assert.Equal(StackPointer + 4, cpu.State.A[7]);
				return cpu.State.D[0];
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"{model} halted at ${cpu.State.ProgramCounter:X8}.");
		}
		throw new Xunit.Sdk.XunitException(
			$"{model} did not return; PC=${cpu.State.ProgramCounter:X8}.");
	}

	private static void ClobberVolatileRegisters(M68kCpuState state, uint result)
	{
		state.D[0] = result;
		state.D[1] = 0xD1D1_D1D1;
		state.A[0] = 0xA0A0_A0A0;
		state.A[1] = 0xA1A1_A1A1;
	}

	private sealed class GenericResolver(IReadOnlyList<M68kRegister>? clobbers)
		: IM68kExternalCallResolver
	{
		public bool TryResolve(M68kExternalMethod method, out M68kExternalCallConvention convention)
		{
			if (method.DisplayName != $"{typeof(AmigaExternalCallClobberFixture).FullName}::GenericVector")
			{
				convention = null!;
				return false;
			}
			convention = new M68kExternalCallConvention(
				"fixture.generic", M68kExternalBaseSource.Immediate,
				M68kRegister.A6, -30, InitialValue: ExecBase, ParameterRegisters: [])
			{
				ClobberedRegisters = clobbers
			};
			return true;
		}
	}
}

public static class AmigaExternalCallClobberFixture
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint WaitThenGet()
	{
		var process = Exec.FindTask(CString.FromPointer(0));
		var port = APTR.FromPointer(process.Raw + 92);
		Exec.WaitPort(port);
		return Exec.GetMsg(port).Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ForbidThenReply() =>
		Reply(Exec.FindTask(CString.FromPointer(0)));

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint Reply(APTR message)
	{
		Exec.Forbid();
		Exec.ReplyMsg(message);
		return message.Raw;
	}

	public static uint GenericEntry() => GenericVector();

	[MethodImpl(MethodImplOptions.InternalCall)]
	public static extern uint GenericVector();
}
