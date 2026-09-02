using System.Reflection;
using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class PointerIdentityInliningTests
{
	[Fact]
	public void PointerConversionIsAnSsaCopyWithoutCallEffects()
	{
		using var module = new CompilationModule(Assembly.GetExecutingAssembly().Location);
		var method = module.ResolveEntryPoint(
			$"{typeof(PointerIdentityFixtures).FullName}::Pointer");
		var function = CilMachineIrBuilder.Build(method, module);
		var instructions = function.Blocks.SelectMany(block => block.Instructions).ToArray();

		Assert.DoesNotContain(instructions, instruction =>
			instruction.Operation == M68kMachineOperation.Call);
		Assert.All(instructions, instruction =>
		{
			Assert.Equal(M68kMachineMemoryEffect.None, instruction.MemoryEffect);
			Assert.False(instruction.MayThrow);
			Assert.False(instruction.IsSafepoint);
			Assert.Null(instruction.LogicalCall);
		});
		M68kMachineIrVerifier.Verify(function);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void PlainPointerHelperInlinesWhileExplicitRootsRemain(bool retainHelper)
	{
		using var module = new CompilationModule(Assembly.GetExecutingAssembly().Location);
		var entry = module.ResolveEntryPoint(
			$"{typeof(PointerIdentityFixtures).FullName}::RoundTrip");
		var helper = module.ResolveEntryPoint(
			$"{typeof(PointerIdentityFixtures).FullName}::Pointer");
		var caller = CilMachineIrBuilder.Build(entry, module);
		var target = CilMachineIrBuilder.Build(helper, module);
		var roots = new HashSet<CilMethodIdentity> { entry.Identity };
		if (retainHelper)
			roots.Add(helper.Identity);

		var statistics = M68kMachineModuleOptimizer.Run(
			[entry, helper],
			new Dictionary<CilMethodIdentity, M68kMachineFunction>
			{
				[entry.Identity] = caller,
				[helper.Identity] = target
			},
			module,
			M68kCpuTarget.M68000,
			roots);

		Assert.Equal(1, statistics.InlinedCalls);
		Assert.DoesNotContain(caller.Blocks.SelectMany(block => block.Instructions),
			instruction => instruction.LogicalCall?.ResolvedTargets.Contains(helper.Identity) == true);
		Assert.Equal(retainHelper, statistics.RetainedMethodIdentities.Contains(helper.Identity));
		M68kMachineIrVerifier.Verify(caller);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void FoldedHelperUsesCanonicalBodyAndPreservesRequestedInliningPolicy(bool noInlining)
	{
		using var module = new CompilationModule(Assembly.GetExecutingAssembly().Location);
		var type = typeof(PointerIdentityFixtures).FullName;
		var entry = module.ResolveEntryPoint($"{type}::" +
			(noInlining ? nameof(PointerIdentityFixtures.NoInlineAliasRoundTrip) :
				nameof(PointerIdentityFixtures.AliasRoundTrip)));
		var helper = module.ResolveEntryPoint($"{type}::Pointer");
		var alias = module.ResolveEntryPoint($"{type}::" +
			(noInlining ? "NoInlinePointer" : nameof(PointerIdentityFixtures.PointerAlias)));
		var caller = CilMachineIrBuilder.Build(entry, module);
		var target = CilMachineIrBuilder.Build(helper, module);
		var statistics = M68kMachineModuleOptimizer.Run(
			[entry, helper, alias],
			new Dictionary<CilMethodIdentity, M68kMachineFunction>
			{
				[entry.Identity] = caller,
				[helper.Identity] = target
			},
			module,
			M68kCpuTarget.M68000,
			new HashSet<CilMethodIdentity> { entry.Identity },
			foldedMethodAliases: new Dictionary<CilMethodIdentity, CilMethod>
			{
				[alias.Identity] = helper
			});

		Assert.Equal(noInlining ? 0 : 1, statistics.InlinedCalls);
		Assert.Equal(noInlining, caller.Blocks.SelectMany(block => block.Instructions)
			.Any(instruction => instruction.LogicalCall?.ResolvedTargets.Contains(alias.Identity) == true));
		Assert.Equal(noInlining, statistics.RetainedMethodIdentities.Contains(helper.Identity));
		M68kMachineIrVerifier.Verify(caller);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	[InlineData(M68kCpuTarget.M68060, M68kCpuModel.M68040)]
	public void CallsToBothIdenticalPointerHelpersInline(M68kCpuTarget target, M68kCpuModel model)
	{
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled,
			M68kPeepholeOptimizationMode.FixedPoint })
		{
			var result = Compile(nameof(PointerIdentityFixtures.BothAliasesEntry), target, mode);
			var helpers = result.Symbols.Where(symbol =>
				symbol.Name.EndsWith("::Pointer", StringComparison.Ordinal) ||
				symbol.Name.EndsWith("::PointerAlias", StringComparison.Ordinal)).ToArray();
			Assert.Equal(2, helpers.Length);
			Assert.Equal(helpers[0].Address, helpers[1].Address);
			foreach (var input in new uint[] { 0, 1, 0x8000_0000, 0xffff_ffff, 0xdead_beef })
			{
				var actual = Execute(result, model, input, pc =>
					Assert.NotEqual(0x10000u + helpers[0].Address, pc));
				Assert.Equal(unchecked(input + (input ^ 0x1234_5678)), actual);
			}
		}
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68060, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.FixedPoint)]
	public void InlinedPointerPreservesAllBitsAndTheStack(
		M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode peephole)
	{
		var result = Compile(nameof(PointerIdentityFixtures.RoundTripEntry), target, peephole);
		Assert.DoesNotContain(result.Symbols, symbol => symbol.Name.EndsWith("::Pointer", StringComparison.Ordinal));
		foreach (var value in new uint[] { 0, 1, 0x7fff_ffff, 0x8000_0000, 0xffff_ffff, 0x1234_5678 })
			Assert.Equal(value, Execute(result, model, value));
	}

	[Fact]
	public void NoInliningStillKeepsTheHelperCall()
	{
		var result = Compile(nameof(PointerIdentityFixtures.NoInlineRoundTripEntry));
		Assert.Contains(result.Symbols, symbol =>
			symbol.Name.EndsWith("::NoInlinePointer", StringComparison.Ordinal));
		Assert.Equal(0xdead_beefu, Execute(result, M68kCpuModel.M68000, 0xdead_beef));
	}

	[Fact]
	public void PointerReadStillReadsGuestMemory()
	{
		var result = Compile(nameof(PointerIdentityFixtures.ReadEntry));
		Assert.Equal(0x8765_4321u, Execute(result, M68kCpuModel.M68000, 0x4000));
	}

	[Fact]
	public void AddressTakenPointerHelperRemainsRelocatable()
	{
		var result = Compile(nameof(PointerIdentityFixtures.PointerAddress));
		var helper = Assert.Single(result.Symbols, symbol =>
			symbol.Name.EndsWith("::Pointer", StringComparison.Ordinal));
		Assert.Equal(0x10000 + helper.Address,
			Execute(result, M68kCpuModel.M68000, 0));
	}

	[Fact]
	public void InlinedPointerReadPreservesEmbeddedDisplacementMetadata()
	{
		var result = Compile(nameof(PointerIdentityFixtures.ReadDisplacementEntry));
		Assert.DoesNotContain(result.Symbols, symbol =>
			symbol.Name.EndsWith("::ReadWithDisplacement", StringComparison.Ordinal));
		Assert.Equal(0x89ab_cdefu, Execute(result, M68kCpuModel.M68000, 0x4000));
	}

	private static M68kCompilationResult Compile(
		string entry, M68kCpuTarget target = M68kCpuTarget.M68000,
		M68kPeepholeOptimizationMode peephole = M68kPeepholeOptimizationMode.FixedPoint) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(PointerIdentityFixtures).FullName}::{entry}",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Hunk,
			PeepholeOptimization = peephole,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			IncludedExportNames = []
		});

	private static uint Execute(M68kCompilationResult result, M68kCpuModel model, uint input,
		Action<uint>? beforeInstruction = null)
	{
		const uint load = 0x10000, stack = 0x80000, sentinel = 0x1000;
		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)load));
		foreach (var relocation in result.Relocations)
		{
			var address = load + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + load);
		}
		bus.WriteLong(stack, sentinel);
		bus.WriteLong(0x4000, 0x8765_4321);
		bus.WriteLong(0x4004, input);
		bus.WriteLong(0x400c, 0x89ab_cdef);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(load + result.EntryPoint, stack);
		for (var step = 0; step < 1000 && cpu.State.ProgramCounter != sentinel; step++)
		{
			beforeInstruction?.Invoke(cpu.State.ProgramCounter);
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted);
		}
		Assert.Equal(sentinel, cpu.State.ProgramCounter);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		return cpu.State.D[0];
	}
}

public static class PointerIdentityFixtures
{
	public static APTR Pointer(uint value) => APTR.FromPointer(value);
	public static APTR PointerAlias(uint value) => APTR.FromPointer(value);
	public static uint RoundTrip(uint value) => APTR.ToUInt32(Pointer(value));
	public static uint AliasRoundTrip(uint value) => APTR.ToUInt32(PointerAlias(value));
	public static uint NoInlineAliasRoundTrip(uint value) => APTR.ToUInt32(NoInlinePointer(value));
	public static uint BothAliasesEntry()
	{
		var input = ReadInput();
		return unchecked(APTR.ToUInt32(Pointer(input)) +
			APTR.ToUInt32(PointerAlias(input ^ 0x1234_5678)));
	}
	public static uint RoundTripEntry() => RoundTrip(ReadInput());
	public static uint NoInlineRoundTripEntry() => NoInlinePointer(ReadInput()).Raw;
	public static uint ReadEntry() => APTR.ReadUInt32(Pointer(ReadInput()), 0);
	public static uint ReadDisplacementEntry() => ReadWithDisplacement(ReadInput());
	public static unsafe uint PointerAddress() =>
		unchecked((uint)(nuint)(delegate*<uint, APTR>)&Pointer);
	private static uint ReadInput() => APTR.ReadUInt32(APTR.FromPointer(0x4004), 0);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static APTR NoInlinePointer(uint value) => APTR.FromPointer(value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static uint ReadWithDisplacement(uint address) =>
		APTR.ReadUInt32(APTR.FromPointer(address), 12);
}
