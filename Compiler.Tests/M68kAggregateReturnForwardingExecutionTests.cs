using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using Xunit.Abstractions;

namespace CopperSharp.Compiler.Tests;

// These tests require the forwarding pass in the real compiler pipeline;
// result-only checks cannot establish
// that a hidden destination was forwarded or a temporary frame home reclaimed.
public sealed class M68kAggregateReturnForwardingExecutionTests
{
	private readonly ITestOutputHelper _output;
	public M68kAggregateReturnForwardingExecutionTests(ITestOutputHelper output) => _output = output;

	private const int PacketBytes = 80;
	private const uint LoadAddress = 0x10000, StackTop = 0x80000, StackBottom = 0x78000;
	private const uint ReturnSentinel = 0x1000, ResultAddress = 0x30000, AliasAddress = 0x31000;
	private static readonly string FixtureName = typeof(AggregateForwardingExecutionFixture).FullName!;
	private sealed record Scenario(string Method, uint Seed);
	private static readonly Scenario[] Scenarios =
	[
		new("Immediate", 0x31), new("Private", 0x31), new("Repeated", 0x31),
		new("Multiple", 0), new("Multiple", 0x31), new("Mixed", 0), new("Mixed", 0x31),
		new("Discarded", 0x31), new("ByrefLocal", 0x31), new("RawLocal", 0x31),
		new("ApointerLocal", 0x31), new("DirectByref", 0x31), new("DirectRaw", 0x31),
		new("DirectApointer", 0x31), new("DirectNative", 0x31), new("ManyArguments", 0x31),
		new("PrivateWithPlatform", 0x31), new("PrivatePassedByValue", 0x31)
	];

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, false)]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, true)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, false)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, true)]
	public void ForwardedAndFallbackCallsPreserveBytesFrameLifetimesAndTheNativeAbi(
		M68kCpuTarget target, M68kCpuModel model, bool outlineCopies)
	{
		var compilation = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(AggregateForwardingExecutionFixture).Assembly.Location,
			EntryPoint = $"{FixtureName}::Entry",
			IncludedExportNames = [],
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			BulkCopy = outlineCopies ? new M68kBulkCopyOptions
			{
				ManagedAssemblyName = typeof(AggregateForwardingExecutionFixture).Assembly.GetName().Name!,
				ManagedMethod = $"{FixtureName}::Copy",
				MinimumBytes = 64
			} : null
		});
		var forwarding = MapMetrics(compilation.Map, "AGGREGATE-FORWARDING");
		var copies = MapMetrics(compilation.Map, "BULK-COPY");
		Assert.True(forwarding["return-buffers"] > 0);
		Assert.True(forwarding["private-locals"] > 0);
		Assert.Equal(0, copies["unclassified-calls"]);
		Assert.Equal(0, copies["external-providers"]);
		if (outlineCopies)
		{
			Assert.True(copies["return-calls"] > 0);
			Assert.True(copies["local-calls"] > 0);
			Assert.True(copies["argument-calls"] > 0);
			Assert.Equal(copies["calls"] * PacketBytes, copies["static-copy-bytes"]);
			Assert.Equal(1, copies["managed-providers"]);
		}
		else
		{
			Assert.Equal(0, copies["calls"]);
			Assert.Equal(0, copies["managed-providers"]);
		}
		foreach (var line in compilation.Map.Split('\n').Where(static line =>
			line.StartsWith("AGGREGATE-FORWARDING ", StringComparison.Ordinal) ||
			line.StartsWith("BULK-COPY ", StringComparison.Ordinal)))
			_output.WriteLine($"{target}/helper={outlineCopies}: {line.TrimEnd()}");
		foreach (var scenario in Scenarios)
		foreach (var stackRemainder in new uint[] { 0, 2 })
		foreach (var aliasResult in IsDirectAlias(scenario.Method) ? new[] { false, true } : new[] { false })
		{
			Execute(compilation, model, outlineCopies, scenario, stackRemainder, aliasResult);
		}
	}

	private static void Execute(M68kCompilationResult compilation, M68kCpuModel model, bool outlineCopies,
		Scenario scenario, uint remainder, bool aliasResult)
	{
		var context = $"{scenario.Method}/{scenario.Seed}/{model}/helper={outlineCopies}/SP+{remainder}/alias={aliasResult}";
		var stack = StackTop + remainder;
		var output = ResultAddress + remainder;
		var externalAlias = aliasResult ? output : AliasAddress + remainder;
		var bus = new TestBus(0x100000);
		bus.Memory.AsSpan((int)StackBottom - 16, (int)(stack - StackBottom) + 280).Fill(0xa5);
		bus.Memory.AsSpan((int)output - 16, PacketBytes + 32).Fill(0xc7);
		bus.Memory.AsSpan((int)AliasAddress + (int)remainder - 16, PacketBytes + 32).Fill(0x5d);
		bus.Memory.AsSpan((int)AggregateForwardingExecutionFixture.CounterAddress - 16, 36).Fill(0x93);
		bus.WriteLong(AggregateForwardingExecutionFixture.CounterAddress, 0);
		var initialAlias = Pattern(0x23);
		initialAlias.CopyTo(bus.Memory.AsSpan((int)externalAlias));
		if (scenario.Method == "PrivateWithPlatform")
		{
			bus.WriteLong(externalAlias, 7);
			BinaryPrimitives.WriteUInt32BigEndian(initialAlias, 7);
		}
		compilation.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in compilation.Relocations)
		{
			var address = LoadAddress + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		bus.WriteLong(stack, ReturnSentinel);
		bus.WriteLong(stack + 4, output); // incoming hidden return buffer
		var immutableCode = bus.Memory.AsSpan((int)LoadAddress, compilation.Code.Length).ToArray();
		var lowerStackGuard = bus.Memory.AsSpan((int)StackBottom - 16, 16).ToArray();
		var upperStackGuard = bus.Memory.AsSpan((int)stack + 4, 256).ToArray();
		var target = Method(compilation, scenario.Method);
		var watched = new[] { "Make", "MakeWithPlatform", "MakeMany", "RewriteByref", "RewriteRaw", "RewriteApointer", "RewriteNative" }
			.Select(name => (Name: name, Symbol: Method(compilation, name)))
			.GroupBy(static item => item.Symbol.Address)
			.ToDictionary(static group => LoadAddress + group.Key,
				static group => group.Select(static item => item.Name).ToHashSet());
		var provider = outlineCopies ? LoadAddress + Method(compilation, "Copy").Address : (uint?)null;
		var calls = new List<Invocation>();
		var copies = new List<CopyInvocation>();
		CopyInvocation? activeCopy = null;
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		// Entry roots all scenarios; execute the retained helper so ordinary
		// callee-save obligations remain observable at this boundary.
		cpu.Reset(LoadAddress + target.Address, stack);
		cpu.State.D[0] = scenario.Seed;
		cpu.State.A[0] = externalAlias;
		if (scenario.Method == "DirectNative")
		{
			// Native integers use data registers, unlike pointer-typed/APTR args.
			cpu.State.D[0] = externalAlias;
			cpu.State.D[1] = scenario.Seed;
		}
		for (var register = 2; register <= 7; register++) cpu.State.D[register] = 0x12345600u + (uint)register;
		for (var register = 2; register <= 6; register++) cpu.State.A[register] = 0x50000u + (uint)register * 256;
		for (var step = 0; step < 500_000 && cpu.State.ProgramCounter != ReturnSentinel; step++)
		{
			if (activeCopy is { } completed && cpu.State.ProgramCounter == completed.ReturnAddress)
			{
				Assert.True(completed.SourceBytes.SequenceEqual(bus.Memory.AsSpan((int)completed.Destination, PacketBytes).ToArray()), context);
				Assert.Equal(completed.BeforeDestination, bus.ReadLong(completed.Destination - 4));
				Assert.Equal(completed.AfterDestination, bus.ReadLong(completed.Destination + PacketBytes));
				activeCopy = null;
			}
			if (watched.TryGetValue(cpu.State.ProgramCounter, out var names))
			{
				var explicitStackBytes = names.Contains("MakeMany") ? 4u : 0;
				calls.Add(new Invocation(names, cpu.State.A[7],
					bus.ReadLong(cpu.State.A[7] + 4 + explicitStackBytes),
					names.Contains("RewriteNative") ? cpu.State.D[0] : cpu.State.A[0]));
			}
			if (provider == cpu.State.ProgramCounter)
			{
				Assert.Null(activeCopy);
				var source = cpu.State.A[0];
				var destination = cpu.State.A[1];
				Assert.Equal((uint)PacketBytes, cpu.State.D[0]);
				Assert.True((ulong)source + PacketBytes <= destination || (ulong)destination + PacketBytes <= source, context);
				Assert.Equal(0u, (source | destination) & 1);
				activeCopy = new CopyInvocation(source, destination, bus.ReadLong(cpu.State.A[7]),
					bus.Memory.AsSpan((int)source, PacketBytes).ToArray(),
					bus.ReadLong(destination - 4), bus.ReadLong(destination + PacketBytes));
				copies.Add(activeCopy);
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted, $"{context}: halted at ${cpu.State.ProgramCounter:X8}.");
		}
		Assert.Equal(ReturnSentinel, cpu.State.ProgramCounter);
		Assert.Null(activeCopy);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		Assert.True(Expected(scenario).SequenceEqual(bus.Memory.AsSpan((int)output, PacketBytes).ToArray()), context);
		var makeCount = scenario.Method is "Repeated" or "Discarded" or "ByrefLocal" or "RawLocal" or "ApointerLocal" ? 2u : 1u;
		Assert.Equal(makeCount, bus.ReadLong(AggregateForwardingExecutionFixture.CounterAddress));
		for (var register = 2; register <= 7; register++) Assert.Equal(0x12345600u + (uint)register, cpu.State.D[register]);
		for (var register = 2; register <= 6; register++) Assert.Equal(0x50000u + (uint)register * 256, cpu.State.A[register]);
		Assert.Equal(ReturnSentinel, bus.ReadLong(stack));
		Assert.Equal(immutableCode, bus.Memory.AsSpan((int)LoadAddress, compilation.Code.Length).ToArray());
		Assert.Equal(lowerStackGuard, bus.Memory.AsSpan((int)StackBottom - 16, 16).ToArray());
		Assert.Equal(upperStackGuard, bus.Memory.AsSpan((int)stack + 4, 256).ToArray());
		AssertCanaries(bus, output, PacketBytes, 0xc7);
		AssertCanaries(bus, AliasAddress + remainder, PacketBytes, 0x5d);
		AssertCanaries(bus, AggregateForwardingExecutionFixture.CounterAddress, 4, 0x93);
		if (!aliasResult)
		{
			if (IsDirectAlias(scenario.Method)) initialAlias[0] = 0xee;
			Assert.Equal(initialAlias, bus.Memory.AsSpan((int)externalAlias, PacketBytes).ToArray());
		}
		AssertDestinationsAndFrames(scenario, calls, copies, stack, output, outlineCopies, context);
	}

	private static void AssertDestinationsAndFrames(Scenario scenario, List<Invocation> calls,
		List<CopyInvocation> copies, uint stack, uint output, bool outlineCopies, string context)
	{
		var makes = calls.Where(static call => call.Methods.Contains("Make")).ToArray();
		Assert.NotEmpty(makes);
		var directlyReturned = scenario.Method is "Immediate" or "Multiple" || scenario.Method == "Mixed" && scenario.Seed == 0;
		if (directlyReturned)
		{
			Assert.Equal(output, Assert.Single(makes).ReturnBuffer);
		}
		if (scenario.Method is "Private" or "Repeated" or "PrivatePassedByValue")
		{
			Assert.All(makes, call =>
			{
				Assert.NotEqual(output, call.ReturnBuffer);
				AssertFrameAddress(call.ReturnBuffer, stack, context);
			});
			if (outlineCopies)
			{
				var finalCopy = Assert.Single(copies, copy => copy.Destination == output);
				AssertFrameAddress(finalCopy.Source, stack, context);
			}
		}
		if (scenario.Method == "PrivateWithPlatform")
		{
			var platformCall = Assert.Single(calls, static call => call.Methods.Contains("MakeWithPlatform"));
			Assert.Equal(platformCall.ReturnBuffer, Assert.Single(makes).ReturnBuffer);
			AssertFrameAddress(platformCall.ReturnBuffer, stack, context);
		}
		if (scenario.Method == "Discarded")
		{
			Assert.Equal(2, makes.Length);
			Assert.NotEqual(output, makes[0].ReturnBuffer);
			Assert.Equal(output, makes[1].ReturnBuffer);
		}
		if (scenario.Method == "ManyArguments" || scenario.Method == "Mixed" && scenario.Seed != 0)
		{
			var many = Assert.Single(calls, static call => call.Methods.Contains("MakeMany"));
			Assert.NotEqual(output, many.ReturnBuffer);
			Assert.Equal(many.ReturnBuffer, Assert.Single(makes).ReturnBuffer);
			Assert.True(stack - many.StackPointer - 12 >= PacketBytes, context);
		}
		foreach (var alias in calls.Where(call => call.Methods.Any(static name => name.StartsWith("Rewrite", StringComparison.Ordinal))))
		{
			Assert.NotEqual(alias.ArgumentAddress, alias.ReturnBuffer);
		}
		if (IsDirectAlias(scenario.Method))
		{
			Assert.All(makes, call => Assert.NotEqual(output, call.ReturnBuffer));
		}
		if (outlineCopies) Assert.NotEmpty(copies);
		else Assert.Empty(copies);
	}

	private static byte[] Expected(Scenario scenario)
	{
		var seed = scenario.Method switch
		{
			"Multiple" or "Mixed" when scenario.Seed == 0 => 7u,
			"Mixed" or "ManyArguments" => scenario.Seed + 10,
			"Repeated" or "Discarded" => scenario.Seed + 1,
			"ByrefLocal" or "RawLocal" or "ApointerLocal" => scenario.Seed * 2 + 1,
			"DirectByref" or "DirectRaw" or "DirectApointer" or "DirectNative" => scenario.Seed + 0x23,
			"PrivateWithPlatform" => scenario.Seed + 7,
			_ => scenario.Seed
		};
		var bytes = Pattern(seed);
		if (scenario.Method is "Private" or "PrivateWithPlatform")
			BinaryPrimitives.WriteUInt32BigEndian(bytes, BinaryPrimitives.ReadUInt32BigEndian(bytes) ^ 0x01020304);
		if (scenario.Method == "Repeated")
			BinaryPrimitives.WriteUInt32BigEndian(bytes,
				BinaryPrimitives.ReadUInt32BigEndian(bytes) ^ BinaryPrimitives.ReadUInt32BigEndian(Pattern(scenario.Seed)));
		if (scenario.Method == "PrivatePassedByValue")
			BinaryPrimitives.WriteUInt32BigEndian(bytes,
				BinaryPrimitives.ReadUInt32BigEndian(bytes) ^ bytes.Aggregate(0u, static (sum, value) => sum + value));
		return bytes;
	}

	private static byte[] Pattern(uint seed) => Enumerable.Range(0, PacketBytes)
		.Select(index => unchecked((byte)(seed + (uint)index * 13))).ToArray();
	private static IReadOnlyDictionary<string, long> MapMetrics(string map, string name) =>
		Assert.Single(map.Split('\n'), line => line.StartsWith(name + " ", StringComparison.Ordinal))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
			.Select(static token => token.TrimEnd().Split('=', 2))
			.ToDictionary(static parts => parts[0], static parts => long.Parse(parts[1], CultureInfo.InvariantCulture));
	private static bool IsDirectAlias(string name) => name is "DirectByref" or "DirectRaw" or "DirectApointer" or "DirectNative";
	private static M68kSymbol Method(M68kCompilationResult compilation, string name) =>
		Assert.Single(compilation.Symbols, symbol => symbol.Name == $"{FixtureName}::{name}");
	private static void AssertCanaries(TestBus bus, uint address, int size, byte expected)
	{
		Assert.All(bus.Memory.AsSpan((int)address - 16, 16).ToArray(), value => Assert.Equal(expected, value));
		Assert.All(bus.Memory.AsSpan((int)address + size, 16).ToArray(), value => Assert.Equal(expected, value));
	}
	private static void AssertFrameAddress(uint address, uint stack, string context) =>
		Assert.True(address >= StackBottom && (ulong)address + PacketBytes <= stack,
			$"{context}: aggregate home ${address:X8} is outside the caller frame ending at ${stack:X8}.");
	private sealed record Invocation(IReadOnlySet<string> Methods, uint StackPointer, uint ReturnBuffer, uint ArgumentAddress);
	private sealed record CopyInvocation(uint Source, uint Destination, uint ReturnAddress, byte[] SourceBytes,
		uint BeforeDestination, uint AfterDestination);
}

public static class AggregateForwardingExecutionFixture
{
	public const uint CounterAddress = 0x32000;
	public struct Packet
	{
		public uint Word00, Word01, Word02, Word03, Word04, Word05, Word06, Word07, Word08, Word09;
		public uint Word10, Word11, Word12, Word13, Word14, Word15, Word16, Word17, Word18, Word19;
	}
	public struct Platform { public uint Value; }

	public static unsafe uint Entry() => SelectScenario(*(uint*)0x32100);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint SelectScenario(uint scenario) => scenario switch
	{
		0 => Sum(Immediate(0x31)),
		1 => Sum(Private(0x31)),
		2 => Sum(Repeated(0x31)),
		3 => Sum(Multiple(scenario)),
		4 => Sum(Mixed(scenario)),
		5 => Sum(Discarded(0x31)),
		6 => Sum(ByrefLocal(0x31)),
		7 => Sum(RawLocal(0x31)),
		8 => Sum(ApointerLocal(0x31)),
		9 => Sum(DirectByref(ref *(Packet*)0x31000, 0x31)),
		10 => Sum(DirectRaw((Packet*)0x31000, 0x31)),
		11 => Sum(DirectApointer(APTR.FromPointer(0x31000), 0x31)),
		12 => Sum(DirectNative(0x31000, 0x31)),
		13 => Sum(ManyArguments(0x31)),
		14 => Sum(PrivateWithPlatform(ref *(Platform*)0x31000, 0x31)),
		_ => Sum(PrivatePassedByValue(0x31))
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe Packet Make(uint seed)
	{
		Packet packet = default;
		var bytes = (byte*)&packet;
		for (uint index = 0; index < 80; index++) bytes[index] = unchecked((byte)(seed + index * 13));
		(*(uint*)CounterAddress)++;
		return packet;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe void Copy(byte* source, byte* destination, uint count)
	{
		for (uint index = 0; index < count; index++) destination[index] = source[index];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet Immediate(uint seed) => Make(seed);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet Private(uint seed)
	{
		var packet = Make(seed);
		packet.Word00 ^= 0x01020304;
		return packet;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet Repeated(uint seed)
	{
		var packet = Make(seed);
		var first = packet.Word00;
		packet = Make(seed + 1);
		packet.Word00 ^= first;
		return packet;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet Multiple(uint seed)
	{
		if (seed == 0) return Make(7);
		return Make(seed);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet Mixed(uint seed)
	{
		if (seed == 0) return Make(7);
		return MakeMany(seed, 1, 2, 3, 4);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet Discarded(uint seed)
	{
		Make(seed);
		return Make(seed + 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet MakeMany(uint first, uint second, uint third, uint fourth, uint fifth) =>
		Make(first + second + third + fourth + fifth);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet ManyArguments(uint seed) => MakeMany(seed, 1, 2, 3, 4);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe Packet RewriteByref(ref Packet previous, uint seed)
	{
		uint combined;
		fixed (Packet* pointer = &previous)
		{
			combined = seed + *(byte*)pointer;
			*(byte*)pointer = 0xee;
		}
		return Make(combined);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe Packet RewriteRaw(Packet* previous, uint seed)
	{
		var combined = seed + *(byte*)previous;
		*(byte*)previous = 0xee;
		return Make(combined);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet RewriteApointer(APTR previous, uint seed)
	{
		var combined = seed + APTR.ReadUInt8(previous, 0);
		APTR.WriteUInt8(previous, 0, 0xee);
		return Make(combined);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe Packet RewriteNative(nuint previous, uint seed)
	{
		var combined = seed + *(byte*)previous;
		*(byte*)previous = 0xee;
		return Make(combined);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet ByrefLocal(uint seed)
	{
		var packet = Make(seed);
		packet = RewriteByref(ref packet, seed + 1);
		return packet;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe Packet RawLocal(uint seed)
	{
		var packet = Make(seed);
		packet = RewriteRaw(&packet, seed + 1);
		return packet;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe Packet ApointerLocal(uint seed)
	{
		var packet = Make(seed);
		packet = RewriteApointer(APTR.FromPointer((uint)&packet), seed + 1);
		return packet;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet DirectByref(ref Packet previous, uint seed) => RewriteByref(ref previous, seed);
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe Packet DirectRaw(Packet* previous, uint seed) => RewriteRaw(previous, seed);
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet DirectApointer(APTR previous, uint seed) => RewriteApointer(previous, seed);
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet DirectNative(nuint previous, uint seed) => RewriteNative(previous, seed);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet MakeWithPlatform(ref Platform platform, uint seed) => Make(platform.Value + seed);
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet PrivateWithPlatform(ref Platform platform, uint seed)
	{
		var packet = MakeWithPlatform(ref platform, seed);
		packet.Word00 ^= 0x01020304;
		return packet;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint Sum(Packet value)
	{
		var bytes = (byte*)&value;
		uint sum = 0;
		for (uint index = 0; index < 80; index++) sum += bytes[index];
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet PrivatePassedByValue(uint seed)
	{
		var packet = Make(seed);
		packet.Word00 ^= Sum(packet);
		return packet;
	}
}
