/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class BulkCopyLoweringTests
{
	private const uint LoadAddress = 0x10000;
	private const uint StackAddress = 0x80000;
	private const uint StackBottom = StackAddress - 32768;
	private const uint ReturnSentinel = 0x1000;
	private const uint ExternalBase = 0x4000;
	private const uint Seed = 0x31;
	private static readonly string FixtureName = typeof(BulkCopyLoweringFixtures).FullName!;

	[Theory]
	[InlineData(32, 1)]
	[InlineData(32, 4)]
	[InlineData(32, 32)]
	[InlineData(32, 64)]
	[InlineData(44, 64)]
	[InlineData(64, 64)]
	[InlineData(64, 128)]
	[InlineData(68, 64)]
	[InlineData(128, 128)]
	[InlineData(172, 128)]
	[InlineData(200, 64)]
	[InlineData(256, 64)]
	[InlineData(512, 64)]
	public void ManagedCopiesPreserveAllBytesScalarArgumentsAndTheCallerAbi(
		int bytes, int minimumBytes)
	{
		var options = ManagedProvider(minimumBytes);
		var compilation = Compile($"Size{bytes}", options);
		if (bytes >= minimumBytes)
			Assert.Single(compilation.Symbols, symbol =>
				symbol.Name == $"{FixtureName}::{nameof(BulkCopyLoweringFixtures.Copy)}");
		foreach (var stackRemainder in new uint[] { 0, 2 })
		{
			var execution = Execute(compilation, options, $"::RoundTrip{bytes}", stackRemainder);
			if (bytes < minimumBytes)
			{
				Assert.Empty(execution.Copies);
				continue;
			}
			// Multiple return/local/argument copies share the option-rooted body.
			Assert.True(execution.Copies.Count >= 2);
			Assert.All(execution.Copies, copy => Assert.Equal((uint)bytes, copy.Count));
			if (stackRemainder == 2)
				Assert.Contains(execution.Copies, copy =>
					(copy.Source & 3) == 2 && (copy.Destination & 3) == 2);
		}
	}

	[Theory]
	[InlineData(32)]
	[InlineData(512)]
	public void MissingProviderRetainsExecutableConventionalCopies(int bytes)
	{
		var compilation = Compile($"Size{bytes}", null);
		Assert.DoesNotContain(compilation.Symbols, symbol =>
			symbol.Name == $"{FixtureName}::{nameof(BulkCopyLoweringFixtures.Copy)}");
		Assert.Empty(Execute(compilation, null, $"::RoundTrip{bytes}", 2).Copies);
	}

	[Fact]
	public void LargeCopiesReduceCodeWithOneSharedProvider()
	{
		var conventional = Compile(nameof(BulkCopyLoweringFixtures.Size512), null);
		var options = ManagedProvider(64);
		var outlined = Compile(nameof(BulkCopyLoweringFixtures.Size512), options);

		Assert.True(outlined.Code.Length < conventional.Code.Length,
			$"Provider code was {outlined.Code.Length} bytes; conventional code was {conventional.Code.Length} bytes.");
		Assert.Single(outlined.Symbols, symbol =>
			symbol.Name == $"{FixtureName}::{nameof(BulkCopyLoweringFixtures.Copy)}");
		Execute(conventional, null, "::RoundTrip512", 2);
		Assert.True(Execute(outlined, options, "::RoundTrip512", 2).Copies.Count >= 2);
	}

	[Fact]
	public void ProviderDependenciesCannotRecursivelyInvokeTheProvider()
	{
		var options = ManagedProvider(32,
			nameof(BulkCopyLoweringFixtures.CopyWithAggregateDependency));
		var compilation = Compile(nameof(BulkCopyLoweringFixtures.Size256), options);

		// The provider's dependency returns and passes a 64-byte value. Execution
		// tracing rejects a second provider entry before the first one returns.
		var execution = Execute(compilation, options, "::RoundTrip256", 2);
		Assert.True(execution.Copies.Count >= 2);
		Assert.All(execution.Copies, copy => Assert.Equal(256u, copy.Count));
	}

	[Theory]
	[InlineData(M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kPeepholeOptimizationMode.Disabled)]
	public void OrdinaryProviderCallKeepsAggregateArgumentHomeAlive(
		M68kPeepholeOptimizationMode peephole)
	{
		var compilation = Compile(nameof(BulkCopyLoweringFixtures.ProviderEntry), null, peephole);
		var expected = Enumerable.Range(0, 256).Select(index => unchecked((byte)(Seed + index * 17))).ToArray();
		foreach (var stackRemainder in new uint[] { 0, 2 })
		{
			// A normal call must preserve the same aggregate dependency without
			// outlining. Its tail-position checker receives a pointer to a frame
			// home, which must remain live until that checker returns.
			var execution = Execute(compilation, null, "::ProviderScenario", stackRemainder, bus =>
			{
				bus.Memory.AsSpan((int)BulkCopyLoweringFixtures.GuestSource - 16, 288).Fill(0x6d);
				expected.CopyTo(bus.Memory.AsSpan((int)BulkCopyLoweringFixtures.GuestSource, 256));
				bus.Memory.AsSpan((int)BulkCopyLoweringFixtures.ProviderDestination - 16, 288).Fill(0xa7);
			});
			Assert.Equal(expected, execution.Bus.Memory.AsSpan(
				(int)BulkCopyLoweringFixtures.GuestSource, 256).ToArray());
			Assert.Equal(expected, execution.Bus.Memory.AsSpan(
				(int)BulkCopyLoweringFixtures.ProviderDestination, 256).ToArray());
			foreach (var (address, sentinel) in new[]
			{
				(BulkCopyLoweringFixtures.GuestSource, (byte)0x6d),
				(BulkCopyLoweringFixtures.ProviderDestination, (byte)0xa7)
			})
			{
				Assert.All(execution.Bus.Memory.AsSpan((int)address - 16, 16).ToArray(),
					value => Assert.Equal(sentinel, value));
				Assert.All(execution.Bus.Memory.AsSpan((int)address + 256, 16).ToArray(),
					value => Assert.Equal(sentinel, value));
			}
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ExternalProviderUsesItsDeclaredRegistersAndPreservesLiveScalars(bool permuted)
	{
		var parameters = permuted
			? new[] { M68kRegister.A1, M68kRegister.A0, M68kRegister.D1 }
			: new[] { M68kRegister.A0, M68kRegister.A1, M68kRegister.D0 };
		var convention = new M68kExternalCallConvention("fixture.bulk-copy",
			M68kExternalBaseSource.Immediate, M68kRegister.A6, -30,
			InitialValue: ExternalBase, ParameterRegisters: parameters)
		{
			ClobberedRegisters =
				[M68kRegister.D0, M68kRegister.D1, M68kRegister.A0, M68kRegister.A1]
		};
		var options = new M68kBulkCopyOptions { MinimumBytes = 64, ExternalCall = convention };
		var compilation = Compile(nameof(BulkCopyLoweringFixtures.Size200), options);

		foreach (var stackRemainder in new uint[] { 0, 2 })
		{
			var execution = Execute(compilation, options, "::RoundTrip200", stackRemainder);
			Assert.True(execution.Copies.Count >= 2);
			Assert.All(execution.Copies, copy => Assert.Equal(200u, copy.Count));
		}
	}

	[Fact]
	public void UnknownOverlappingGuestRangesDoNotUseTheNonoverlappingProvider()
	{
		var options = ManagedProvider(32);
		var compilation = Compile(nameof(BulkCopyLoweringFixtures.PointerEntry), options);
		byte[]? expected = null;
		var execution = Execute(compilation, options, "::PointerScenario", 2, bus =>
		{
			var region = bus.Memory.AsSpan((int)BulkCopyLoweringFixtures.GuestSource - 16, 112);
			for (var index = 0; index < region.Length; index++)
				region[index] = (byte)(index * 13 + 7);
			expected = region.ToArray();
			Array.Copy(expected, 16, expected, 12, 64);
		});

		Assert.Empty(execution.Copies);
		Assert.Equal(expected, execution.Bus.Memory.AsSpan(
			(int)BulkCopyLoweringFixtures.GuestSource - 16, 112).ToArray());
	}

	[Fact]
	public void DynamicStackFrameRetainsConventionalCopiesAndRestoresSp()
	{
		var options = ManagedProvider(32);
		var compilation = Compile(nameof(BulkCopyLoweringFixtures.DynamicEntry), options);
		Assert.Empty(Execute(compilation, options, "::DynamicScenario", 2).Copies);
	}

	[Theory]
	[InlineData(nameof(BulkCopyLoweringFixtures.ExceptionRegionCopy), true)]
	[InlineData(nameof(BulkCopyLoweringFixtures.ReferenceBearingCopy), false)]
	public void ExceptionAndReferenceBearingCopiesKeepTheirOriginalMemorySemantics(
		string methodName, bool exceptionRegion)
	{
		using var module = new CompilationModule(typeof(BulkCopyLoweringFixtures).Assembly.Location);
		var method = module.ResolveEntryPoint($"{FixtureName}::{methodName}");
		var function = CilMachineIrBuilder.Build(method, module);
		var provider = module.ResolveEntryPoint(
			$"{FixtureName}::{nameof(BulkCopyLoweringFixtures.Copy)}");
		var target = new M68kBulkCopyTarget(provider, null,
			[M68kRegister.A0, M68kRegister.A1, M68kRegister.D0],
			M68kRegisterSet.From(M68kRegister.D0, M68kRegister.D1,
				M68kRegister.A0, M68kRegister.A1));
		if (exceptionRegion)
			Assert.True(function.HasExceptionHandlers);
		else
			Assert.False(module.TryGetReferenceFreeStructLayout(method.Signature.ReturnType,
				method.ModuleName, out _));

		Assert.Equal(0, M68kBulkCopyLowering.Run(function, module, target, 32));
		Assert.DoesNotContain(function.Blocks.SelectMany(block => block.Instructions),
			instruction => instruction.Operation == M68kMachineOperation.BulkCopy);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void NonpositiveThresholdIsRejected(int minimumBytes)
	{
		var error = Assert.Throws<M68kCompilationException>(() =>
			Compile(nameof(BulkCopyLoweringFixtures.Size64), ManagedProvider(minimumBytes)));
		Assert.Contains("positive", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ManagedReferenceProviderParametersAreRejected()
	{
		var error = Assert.Throws<M68kCompilationException>(() =>
			Compile(nameof(BulkCopyLoweringFixtures.Size64), ManagedProvider(64,
				nameof(BulkCopyLoweringFixtures.InvalidReferenceProvider))));
		Assert.Contains("32-bit integer or pointer", error.Message);
	}

	private static M68kBulkCopyOptions ManagedProvider(int minimumBytes,
		string method = nameof(BulkCopyLoweringFixtures.Copy)) => new()
	{
		MinimumBytes = minimumBytes,
		ManagedAssemblyName = typeof(BulkCopyLoweringFixtures).Assembly.GetName().Name!,
		ManagedMethod = $"{FixtureName}::{method}"
	};

	private static M68kCompilationResult Compile(string method, M68kBulkCopyOptions? options,
		M68kPeepholeOptimizationMode peephole = M68kPeepholeOptimizationMode.FixedPoint) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(BulkCopyLoweringFixtures).Assembly.Location,
			EntryPoint = $"{FixtureName}::{method}",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = peephole,
			IncludedExportNames = [],
			BulkCopy = options
		});

	private static Execution Execute(M68kCompilationResult compilation,
		M68kBulkCopyOptions? options, string targetFragment, uint stackRemainder,
		Action<TestBus>? initialize = null)
	{
		var bus = new TestBus(0x100000);
		var stack = StackAddress + stackRemainder;
		bus.Memory.AsSpan((int)StackBottom - 32, (int)(stack - StackBottom) + 324).Fill(0xa5);
		compilation.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in compilation.Relocations)
		{
			var address = LoadAddress + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		bus.WriteLong(stack, ReturnSentinel);
		initialize?.Invoke(bus);
		var codeBefore = bus.Memory.AsSpan((int)LoadAddress, compilation.Code.Length).ToArray();
		var lowerGuard = bus.Memory.AsSpan((int)StackBottom - 32, 32).ToArray();
		var upperGuard = bus.Memory.AsSpan((int)stack + 4, 256).ToArray();
		var copies = new List<CopyInvocation>();
		if (options?.ExternalCall is { } external)
		{
			bus.RegisterGateway(unchecked(ExternalBase + (uint)external.Displacement), state =>
			{
				Assert.Equal(ExternalBase, state.A[6]);
				var parameters = external.ParameterRegisters!;
				var copy = ObserveCopy(bus, state, ReadRegister(state, parameters[0]),
					ReadRegister(state, parameters[1]), ReadRegister(state, parameters[2]), stack);
				copies.Add(copy);
				bus.Memory.AsSpan((int)copy.Source, (int)copy.Count).CopyTo(
					bus.Memory.AsSpan((int)copy.Destination, (int)copy.Count));
				state.D[0] = 0xd0d0_d0d0;
				state.D[1] = 0xd1d1_d1d1;
				state.A[0] = 0xa0a0_a0a0;
				state.A[1] = 0xa1a1_a1a1;
			});
		}
		uint? providerAddress = null;
		if (options?.ManagedMethod is { } providerName)
		{
			var providers = compilation.Symbols.Where(symbol => symbol.Name == providerName).ToArray();
			Assert.True(providers.Length <= 1);
			if (providers.Length == 1) providerAddress = LoadAddress + providers[0].Address;
		}
		var target = Assert.Single(compilation.Symbols,
			symbol => symbol.Name.Contains(targetFragment, StringComparison.Ordinal));
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		// A retained callee, not the process entry, owns the normal callee-save ABI.
		cpu.Reset(LoadAddress + target.Address, stack);
		cpu.State.D[0] = Seed;
		for (var register = 2; register <= 7; register++)
			cpu.State.D[register] = 0x1234_5600u + (uint)register;
		for (var register = 2; register <= 6; register++)
			cpu.State.A[register] = 0x50000u + (uint)register * 256;
		uint? providerReturn = null;
		for (var instruction = 0; instruction < 1_000_000 &&
			cpu.State.ProgramCounter != ReturnSentinel; instruction++)
		{
			if (providerReturn == cpu.State.ProgramCounter)
				providerReturn = null;
			if (providerAddress == cpu.State.ProgramCounter)
			{
				Assert.Null(providerReturn);
				var copy = ObserveCopy(bus, cpu.State, cpu.State.A[0], cpu.State.A[1],
					cpu.State.D[0], stack);
				copies.Add(copy);
				providerReturn = copy.ReturnAddress;
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"MC68000 halted at ${cpu.State.ProgramCounter:X8}; opcode ${cpu.State.LastOpcode:X4}.");
		}
		Assert.Equal(ReturnSentinel, cpu.State.ProgramCounter);
		Assert.Equal(42u, cpu.State.D[0]);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		for (var register = 2; register <= 7; register++)
			Assert.Equal(0x1234_5600u + (uint)register, cpu.State.D[register]);
		for (var register = 2; register <= 6; register++)
			Assert.Equal(0x50000u + (uint)register * 256, cpu.State.A[register]);
		Assert.Equal(ReturnSentinel, bus.ReadLong(stack));
		Assert.Equal(codeBefore, bus.Memory.AsSpan((int)LoadAddress, compilation.Code.Length).ToArray());
		Assert.Equal(lowerGuard, bus.Memory.AsSpan((int)StackBottom - 32, 32).ToArray());
		Assert.Equal(upperGuard, bus.Memory.AsSpan((int)stack + 4, 256).ToArray());
		return new Execution(bus, copies);
	}

	private static CopyInvocation ObserveCopy(TestBus bus, M68kCpuState state,
		uint source, uint destination, uint count, uint stack)
	{
		Assert.InRange(count, 1u, 512u);
		Assert.True(source >= StackBottom && (ulong)source + count <= stack);
		Assert.True(destination >= StackBottom && (ulong)destination + count <= stack);
		Assert.True((ulong)source + count <= destination ||
			(ulong)destination + count <= source, "Provider received overlapping ranges.");
		Assert.Equal(0u, source & 1);
		Assert.Equal(0u, destination & 1);
		return new CopyInvocation(source, destination, count, bus.ReadLong(state.A[7]));
	}

	private static uint ReadRegister(M68kCpuState state, M68kRegister register) =>
		register <= M68kRegister.D7 ? state.D[(int)register] : state.A[(int)register - 8];

	private sealed record CopyInvocation(uint Source, uint Destination, uint Count, uint ReturnAddress);
	private sealed record Execution(TestBus Bus, List<CopyInvocation> Copies);
}

public static class BulkCopyLoweringFixtures
{
	public const uint GuestSource = 0x30000;
	public const uint ProviderDestination = 0x31000;
	public struct Bytes32 { public uint A, B, C, D, E, F, G, H; }
	public struct Bytes44 { public Bytes32 First; public uint I, J, K; }
	public struct Bytes64 { public Bytes32 First, Second; }
	public struct Bytes68 { public Bytes64 First; public uint Last; }
	public struct Bytes128 { public Bytes64 First, Second; }
	public struct Bytes172 { public Bytes128 First; public Bytes44 Last; }
	public struct Bytes200 { public Bytes128 First; public Bytes64 Second; public uint A, B; }
	public struct Bytes256 { public Bytes128 First, Second; }
	public struct Bytes512 { public Bytes256 First, Second; }
	public struct ReferenceValue { public object? Reference; public Bytes64 Payload; }

	public static uint Size32() => RoundTrip32(0x31);
	public static uint Size44() => RoundTrip44(0x31);
	public static uint Size64() => RoundTrip64(0x31);
	public static uint Size68() => RoundTrip68(0x31);
	public static uint Size128() => RoundTrip128(0x31);
	public static uint Size172() => RoundTrip172(0x31);
	public static uint Size200() => RoundTrip200(0x31);
	public static uint Size256() => RoundTrip256(0x31);
	public static uint Size512() => RoundTrip512(0x31);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint RoundTrip32(uint seed)
	{
		var first = seed + 0x1122_3344;
		var second = seed ^ 0x5566_7788;
		var third = seed + 0x99aa_bbcc;
		var firstPointer = APTR.FromPointer(0x32000 + seed);
		var secondPointer = APTR.FromPointer(0x33000 + seed);
		Bytes32 original = Create32(seed);
		Bytes32 local = original;
		((byte*)&local)[0] ^= 0x5a;
		var result = Consume32(first, firstPointer, local, second, secondPointer,
			original, third, seed);
		var expected = first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
		return result == expected && Matches((byte*)&original, sizeof(Bytes32), seed, 0) &&
			Matches((byte*)&local, sizeof(Bytes32), seed, 0x5a) ? 42u : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe Bytes32 Create32(uint seed)
	{
		Bytes32 value = default;
		Fill((byte*)&value, sizeof(Bytes32), seed);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint Consume32(uint first, APTR firstPointer, Bytes32 changed,
		uint second, APTR secondPointer, Bytes32 original, uint third, uint seed)
	{
		if (first != seed + 0x1122_3344 || second != (seed ^ 0x5566_7788) ||
			third != seed + 0x99aa_bbcc || firstPointer.Raw != 0x32000 + seed ||
			secondPointer.Raw != 0x33000 + seed ||
			!Matches((byte*)&changed, sizeof(Bytes32), seed, 0x5a) ||
			!Matches((byte*)&original, sizeof(Bytes32), seed, 0)) return 0;
		// The caller's independently live values must survive mutations to these
		// by-value arguments as well as helper-register clobbering during setup.
		((byte*)&changed)[0] ^= 0xff;
		((byte*)&original)[sizeof(Bytes32) - 1] ^= 0xff;
		return first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint RoundTrip44(uint seed)
	{
		var first = seed + 0x1122_3344;
		var second = seed ^ 0x5566_7788;
		var third = seed + 0x99aa_bbcc;
		var firstPointer = APTR.FromPointer(0x32000 + seed);
		var secondPointer = APTR.FromPointer(0x33000 + seed);
		Bytes44 original = Create44(seed);
		Bytes44 local = original;
		((byte*)&local)[0] ^= 0x5a;
		var result = Consume44(first, firstPointer, local, second, secondPointer,
			original, third, seed);
		var expected = first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
		return result == expected && Matches((byte*)&original, sizeof(Bytes44), seed, 0) &&
			Matches((byte*)&local, sizeof(Bytes44), seed, 0x5a) ? 42u : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe Bytes44 Create44(uint seed)
	{
		Bytes44 value = default;
		Fill((byte*)&value, sizeof(Bytes44), seed);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint Consume44(uint first, APTR firstPointer, Bytes44 changed,
		uint second, APTR secondPointer, Bytes44 original, uint third, uint seed)
	{
		if (first != seed + 0x1122_3344 || second != (seed ^ 0x5566_7788) ||
			third != seed + 0x99aa_bbcc || firstPointer.Raw != 0x32000 + seed ||
			secondPointer.Raw != 0x33000 + seed ||
			!Matches((byte*)&changed, sizeof(Bytes44), seed, 0x5a) ||
			!Matches((byte*)&original, sizeof(Bytes44), seed, 0)) return 0;
		// The caller's independently live values must survive mutations to these
		// by-value arguments as well as helper-register clobbering during setup.
		((byte*)&changed)[0] ^= 0xff;
		((byte*)&original)[sizeof(Bytes44) - 1] ^= 0xff;
		return first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint RoundTrip64(uint seed)
	{
		var first = seed + 0x1122_3344;
		var second = seed ^ 0x5566_7788;
		var third = seed + 0x99aa_bbcc;
		var firstPointer = APTR.FromPointer(0x32000 + seed);
		var secondPointer = APTR.FromPointer(0x33000 + seed);
		Bytes64 original = Create64(seed);
		Bytes64 local = original;
		((byte*)&local)[0] ^= 0x5a;
		var result = Consume64(first, firstPointer, local, second, secondPointer,
			original, third, seed);
		var expected = first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
		return result == expected && Matches((byte*)&original, sizeof(Bytes64), seed, 0) &&
			Matches((byte*)&local, sizeof(Bytes64), seed, 0x5a) ? 42u : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe Bytes64 Create64(uint seed)
	{
		Bytes64 value = default;
		Fill((byte*)&value, sizeof(Bytes64), seed);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint Consume64(uint first, APTR firstPointer, Bytes64 changed,
		uint second, APTR secondPointer, Bytes64 original, uint third, uint seed)
	{
		if (first != seed + 0x1122_3344 || second != (seed ^ 0x5566_7788) ||
			third != seed + 0x99aa_bbcc || firstPointer.Raw != 0x32000 + seed ||
			secondPointer.Raw != 0x33000 + seed ||
			!Matches((byte*)&changed, sizeof(Bytes64), seed, 0x5a) ||
			!Matches((byte*)&original, sizeof(Bytes64), seed, 0)) return 0;
		// The caller's independently live values must survive mutations to these
		// by-value arguments as well as helper-register clobbering during setup.
		((byte*)&changed)[0] ^= 0xff;
		((byte*)&original)[sizeof(Bytes64) - 1] ^= 0xff;
		return first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint RoundTrip68(uint seed)
	{
		var first = seed + 0x1122_3344;
		var second = seed ^ 0x5566_7788;
		var third = seed + 0x99aa_bbcc;
		var firstPointer = APTR.FromPointer(0x32000 + seed);
		var secondPointer = APTR.FromPointer(0x33000 + seed);
		Bytes68 original = Create68(seed);
		Bytes68 local = original;
		((byte*)&local)[0] ^= 0x5a;
		var result = Consume68(first, firstPointer, local, second, secondPointer,
			original, third, seed);
		var expected = first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
		return result == expected && Matches((byte*)&original, sizeof(Bytes68), seed, 0) &&
			Matches((byte*)&local, sizeof(Bytes68), seed, 0x5a) ? 42u : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe Bytes68 Create68(uint seed)
	{
		Bytes68 value = default;
		Fill((byte*)&value, sizeof(Bytes68), seed);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint Consume68(uint first, APTR firstPointer, Bytes68 changed,
		uint second, APTR secondPointer, Bytes68 original, uint third, uint seed)
	{
		if (first != seed + 0x1122_3344 || second != (seed ^ 0x5566_7788) ||
			third != seed + 0x99aa_bbcc || firstPointer.Raw != 0x32000 + seed ||
			secondPointer.Raw != 0x33000 + seed ||
			!Matches((byte*)&changed, sizeof(Bytes68), seed, 0x5a) ||
			!Matches((byte*)&original, sizeof(Bytes68), seed, 0)) return 0;
		// The caller's independently live values must survive mutations to these
		// by-value arguments as well as helper-register clobbering during setup.
		((byte*)&changed)[0] ^= 0xff;
		((byte*)&original)[sizeof(Bytes68) - 1] ^= 0xff;
		return first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint RoundTrip128(uint seed)
	{
		var first = seed + 0x1122_3344;
		var second = seed ^ 0x5566_7788;
		var third = seed + 0x99aa_bbcc;
		var firstPointer = APTR.FromPointer(0x32000 + seed);
		var secondPointer = APTR.FromPointer(0x33000 + seed);
		Bytes128 original = Create128(seed);
		Bytes128 local = original;
		((byte*)&local)[0] ^= 0x5a;
		var result = Consume128(first, firstPointer, local, second, secondPointer,
			original, third, seed);
		var expected = first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
		return result == expected && Matches((byte*)&original, sizeof(Bytes128), seed, 0) &&
			Matches((byte*)&local, sizeof(Bytes128), seed, 0x5a) ? 42u : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe Bytes128 Create128(uint seed)
	{
		Bytes128 value = default;
		Fill((byte*)&value, sizeof(Bytes128), seed);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint Consume128(uint first, APTR firstPointer, Bytes128 changed,
		uint second, APTR secondPointer, Bytes128 original, uint third, uint seed)
	{
		if (first != seed + 0x1122_3344 || second != (seed ^ 0x5566_7788) ||
			third != seed + 0x99aa_bbcc || firstPointer.Raw != 0x32000 + seed ||
			secondPointer.Raw != 0x33000 + seed ||
			!Matches((byte*)&changed, sizeof(Bytes128), seed, 0x5a) ||
			!Matches((byte*)&original, sizeof(Bytes128), seed, 0)) return 0;
		// The caller's independently live values must survive mutations to these
		// by-value arguments as well as helper-register clobbering during setup.
		((byte*)&changed)[0] ^= 0xff;
		((byte*)&original)[sizeof(Bytes128) - 1] ^= 0xff;
		return first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint RoundTrip172(uint seed)
	{
		var first = seed + 0x1122_3344;
		var second = seed ^ 0x5566_7788;
		var third = seed + 0x99aa_bbcc;
		var firstPointer = APTR.FromPointer(0x32000 + seed);
		var secondPointer = APTR.FromPointer(0x33000 + seed);
		Bytes172 original = Create172(seed);
		Bytes172 local = original;
		((byte*)&local)[0] ^= 0x5a;
		var result = Consume172(first, firstPointer, local, second, secondPointer,
			original, third, seed);
		var expected = first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
		return result == expected && Matches((byte*)&original, sizeof(Bytes172), seed, 0) &&
			Matches((byte*)&local, sizeof(Bytes172), seed, 0x5a) ? 42u : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe Bytes172 Create172(uint seed)
	{
		Bytes172 value = default;
		Fill((byte*)&value, sizeof(Bytes172), seed);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint Consume172(uint first, APTR firstPointer, Bytes172 changed,
		uint second, APTR secondPointer, Bytes172 original, uint third, uint seed)
	{
		if (first != seed + 0x1122_3344 || second != (seed ^ 0x5566_7788) ||
			third != seed + 0x99aa_bbcc || firstPointer.Raw != 0x32000 + seed ||
			secondPointer.Raw != 0x33000 + seed ||
			!Matches((byte*)&changed, sizeof(Bytes172), seed, 0x5a) ||
			!Matches((byte*)&original, sizeof(Bytes172), seed, 0)) return 0;
		// The caller's independently live values must survive mutations to these
		// by-value arguments as well as helper-register clobbering during setup.
		((byte*)&changed)[0] ^= 0xff;
		((byte*)&original)[sizeof(Bytes172) - 1] ^= 0xff;
		return first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint RoundTrip200(uint seed)
	{
		var first = seed + 0x1122_3344;
		var second = seed ^ 0x5566_7788;
		var third = seed + 0x99aa_bbcc;
		var firstPointer = APTR.FromPointer(0x32000 + seed);
		var secondPointer = APTR.FromPointer(0x33000 + seed);
		Bytes200 original = Create200(seed);
		Bytes200 local = original;
		((byte*)&local)[0] ^= 0x5a;
		var result = Consume200(first, firstPointer, local, second, secondPointer,
			original, third, seed);
		var expected = first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
		return result == expected && Matches((byte*)&original, sizeof(Bytes200), seed, 0) &&
			Matches((byte*)&local, sizeof(Bytes200), seed, 0x5a) ? 42u : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe Bytes200 Create200(uint seed)
	{
		Bytes200 value = default;
		Fill((byte*)&value, sizeof(Bytes200), seed);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint Consume200(uint first, APTR firstPointer, Bytes200 changed,
		uint second, APTR secondPointer, Bytes200 original, uint third, uint seed)
	{
		if (first != seed + 0x1122_3344 || second != (seed ^ 0x5566_7788) ||
			third != seed + 0x99aa_bbcc || firstPointer.Raw != 0x32000 + seed ||
			secondPointer.Raw != 0x33000 + seed ||
			!Matches((byte*)&changed, sizeof(Bytes200), seed, 0x5a) ||
			!Matches((byte*)&original, sizeof(Bytes200), seed, 0)) return 0;
		// The caller's independently live values must survive mutations to these
		// by-value arguments as well as helper-register clobbering during setup.
		((byte*)&changed)[0] ^= 0xff;
		((byte*)&original)[sizeof(Bytes200) - 1] ^= 0xff;
		return first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint RoundTrip256(uint seed)
	{
		var first = seed + 0x1122_3344;
		var second = seed ^ 0x5566_7788;
		var third = seed + 0x99aa_bbcc;
		var firstPointer = APTR.FromPointer(0x32000 + seed);
		var secondPointer = APTR.FromPointer(0x33000 + seed);
		Bytes256 original = Create256(seed);
		Bytes256 local = original;
		((byte*)&local)[0] ^= 0x5a;
		var result = Consume256(first, firstPointer, local, second, secondPointer,
			original, third, seed);
		var expected = first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
		return result == expected && Matches((byte*)&original, sizeof(Bytes256), seed, 0) &&
			Matches((byte*)&local, sizeof(Bytes256), seed, 0x5a) ? 42u : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe Bytes256 Create256(uint seed)
	{
		Bytes256 value = default;
		Fill((byte*)&value, sizeof(Bytes256), seed);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint Consume256(uint first, APTR firstPointer, Bytes256 changed,
		uint second, APTR secondPointer, Bytes256 original, uint third, uint seed)
	{
		if (first != seed + 0x1122_3344 || second != (seed ^ 0x5566_7788) ||
			third != seed + 0x99aa_bbcc || firstPointer.Raw != 0x32000 + seed ||
			secondPointer.Raw != 0x33000 + seed ||
			!Matches((byte*)&changed, sizeof(Bytes256), seed, 0x5a) ||
			!Matches((byte*)&original, sizeof(Bytes256), seed, 0)) return 0;
		// The caller's independently live values must survive mutations to these
		// by-value arguments as well as helper-register clobbering during setup.
		((byte*)&changed)[0] ^= 0xff;
		((byte*)&original)[sizeof(Bytes256) - 1] ^= 0xff;
		return first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint RoundTrip512(uint seed)
	{
		var first = seed + 0x1122_3344;
		var second = seed ^ 0x5566_7788;
		var third = seed + 0x99aa_bbcc;
		var firstPointer = APTR.FromPointer(0x32000 + seed);
		var secondPointer = APTR.FromPointer(0x33000 + seed);
		Bytes512 original = Create512(seed);
		Bytes512 local = original;
		((byte*)&local)[0] ^= 0x5a;
		var result = Consume512(first, firstPointer, local, second, secondPointer,
			original, third, seed);
		var expected = first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
		return result == expected && Matches((byte*)&original, sizeof(Bytes512), seed, 0) &&
			Matches((byte*)&local, sizeof(Bytes512), seed, 0x5a) ? 42u : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe Bytes512 Create512(uint seed)
	{
		Bytes512 value = default;
		Fill((byte*)&value, sizeof(Bytes512), seed);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint Consume512(uint first, APTR firstPointer, Bytes512 changed,
		uint second, APTR secondPointer, Bytes512 original, uint third, uint seed)
	{
		if (first != seed + 0x1122_3344 || second != (seed ^ 0x5566_7788) ||
			third != seed + 0x99aa_bbcc || firstPointer.Raw != 0x32000 + seed ||
			secondPointer.Raw != 0x33000 + seed ||
			!Matches((byte*)&changed, sizeof(Bytes512), seed, 0x5a) ||
			!Matches((byte*)&original, sizeof(Bytes512), seed, 0)) return 0;
		// The caller's independently live values must survive mutations to these
		// by-value arguments as well as helper-register clobbering during setup.
		((byte*)&changed)[0] ^= 0xff;
		((byte*)&original)[sizeof(Bytes512) - 1] ^= 0xff;
		return first ^ second ^ third ^ firstPointer.Raw ^ secondPointer.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Copy(APTR source, APTR destination, uint count)
	{
		for (var offset = 0u; offset < count; offset++)
			APTR.WriteUInt8(destination, unchecked((int)offset),
				APTR.ReadUInt8(source, unchecked((int)offset)));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void CopyWithAggregateDependency(APTR source, APTR destination, uint count)
	{
		var guard = Create64(7);
		if (CheckProviderArgument(guard)) Copy(source, destination, count);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe bool CheckProviderArgument(Bytes64 value) =>
		Matches((byte*)&value, sizeof(Bytes64), 7, 0);

	public static uint ProviderEntry() => ProviderScenario(0x31);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ProviderScenario(uint seed)
	{
		var source = APTR.FromPointer(GuestSource);
		var destination = APTR.FromPointer(ProviderDestination);
		CopyWithAggregateDependency(source, destination, 256);
		for (var offset = 0; offset < 256; offset++)
		{
			var expected = unchecked((byte)(seed + (uint)offset * 17));
			if (APTR.ReadUInt8(source, offset) != expected ||
				APTR.ReadUInt8(destination, offset) != expected) return 0;
		}
		return 42;
	}

	public static void InvalidReferenceProvider(byte[] source, byte[] destination, uint count) { }

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe void Fill(byte* bytes, int count, uint seed)
	{
		for (var offset = 0; offset < count; offset++)
			bytes[offset] = unchecked((byte)(seed + (uint)offset * 17));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe bool Matches(byte* bytes, int count, uint seed, int firstXor)
	{
		for (var offset = 0; offset < count; offset++)
		{
			var expected = unchecked((byte)(seed + (uint)offset * 17));
			if (offset == 0) expected ^= (byte)firstXor;
			if (bytes[offset] != expected) return false;
		}
		return true;
	}

	public static uint PointerEntry() => PointerScenario();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PointerScenario()
	{
		CopyUnknown(APTR.FromPointer(GuestSource), APTR.FromPointer(GuestSource - 4));
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe void CopyUnknown(APTR source, APTR destination) =>
		*(Bytes64*)destination.Raw = *(Bytes64*)source.Raw;

	public static uint DynamicEntry() => DynamicScenario(0x31);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint DynamicScenario(uint seed)
	{
		byte* scratch = stackalloc byte[(int)(seed & 7) + 1];
		scratch[0] = 0x73;
		Bytes64 original = default;
		Fill((byte*)&original, sizeof(Bytes64), seed);
		Bytes64 local = original;
		((byte*)&local)[0] ^= 0x5a;
		return scratch[0] == 0x73 && Matches((byte*)&original, sizeof(Bytes64), seed, 0) &&
			Matches((byte*)&local, sizeof(Bytes64), seed, 0x5a) ? 42u : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe Bytes64 ExceptionRegionCopy(uint seed)
	{
		Bytes64 value = default;
		try
		{
			Fill((byte*)&value, sizeof(Bytes64), seed);
			Bytes64 copy = value;
			((byte*)&copy)[0] ^= 0x5a;
			return copy;
		}
		finally
		{
			APTR.WriteUInt32(APTR.FromPointer(GuestSource), 0, seed);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReferenceValue ReferenceBearingCopy(ReferenceValue value)
	{
		ReferenceValue copy = value;
		copy.Payload.First.A ^= 0x55;
		return copy;
	}
}
