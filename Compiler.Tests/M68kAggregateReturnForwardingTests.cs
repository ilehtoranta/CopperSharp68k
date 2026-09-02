using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kAggregateReturnForwardingTests
{
	[Fact]
	public void ImmediateReturnUsesIncomingBufferAndReclaimsTemporary()
	{
		using var fixture = Build(nameof(AggregateReturnForwardingFixture.ImmediateReturn));
		var before = Instructions(fixture.Function);
		var call = Assert.Single(before, static instruction => instruction.Operation == M68kMachineOperation.Call);
		var temporary = Assert.Single(fixture.Function.LocalHomes,
			item => item.Key >= fixture.Method.Locals.Length);
		var resultValues = call.LogicalCall!.ResultValueIds.ToArray();

		var result = M68kAggregateReturnForwarding.Run(fixture.Function, fixture.Module);

		Assert.Equal(1, result.ReturnBuffersForwarded);
		Assert.Equal(0, result.LocalsForwarded);
		Assert.Equal(1, result.TemporaryHomesRemoved);
		Assert.Equal(80, result.TemporaryBytesRemoved);
		Assert.DoesNotContain(temporary.Key, fixture.Function.LocalHomes.Keys);
		Assert.Empty(fixture.Function.ReusableAggregateReturnHomes);
		var after = Instructions(fixture.Function);
		Assert.Single(after, static instruction => instruction.Operation == M68kMachineOperation.ReturnBufferAddress);
		var returned = Assert.Single(after, static instruction => instruction.Operation == M68kMachineOperation.Return);
		Assert.True(returned.ReturnBufferWritten);
		Assert.Empty(returned.Uses);
		var forwardedCall = Assert.Single(after, instruction => instruction.Id == call.Id);
		Assert.Same(call.SourceInstruction, forwardedCall.SourceInstruction);
		Assert.Same(call.Origin, forwardedCall.Origin);
		Assert.Equal(call.MayThrow, forwardedCall.MayThrow);
		Assert.Equal(call.IsSafepoint, forwardedCall.IsSafepoint);
		Assert.Equal(call.Clobbers, forwardedCall.Clobbers);
		Assert.Empty(forwardedCall.LogicalCall!.ResultValueIds);
		Assert.All(resultValues, value => Assert.DoesNotContain(value, fixture.Function.Values.Keys));
		Assert.DoesNotContain(after.SelectMany(static instruction => instruction.ExactMemoryAccesses),
			access => access.Object.Kind == M68kMemoryObjectKind.FrameSlot &&
				access.Object.Identity == temporary.Key.ToString(System.Globalization.CultureInfo.InvariantCulture));
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Theory]
	[InlineData(nameof(AggregateReturnForwardingFixture.PrivateLocal))]
	[InlineData(nameof(AggregateReturnForwardingFixture.PrivateLocalWithPlatform))]
	public void PrivateLocalBecomesHiddenReturnDestination(string entry)
	{
		using var fixture = Build(entry);
		var destination = Assert.Single(Instructions(fixture.Function), instruction =>
			instruction.Operation == M68kMachineOperation.LocalStore &&
			instruction.ArgumentIndex is { } index && index < fixture.Method.Locals.Length &&
			fixture.Function.LocalHomes[index].Size == 80);
		var originalOrigins = Instructions(fixture.Function).ToDictionary(static instruction => instruction.Id, static instruction => instruction.Origin);

		var result = M68kAggregateReturnForwarding.Run(fixture.Function, fixture.Module);

		Assert.Equal(0, result.ReturnBuffersForwarded);
		Assert.Equal(1, result.LocalsForwarded);
		Assert.Equal(80, result.TemporaryBytesRemoved);
		Assert.DoesNotContain(Instructions(fixture.Function), instruction => instruction.Id == destination.Id);
		Assert.True(fixture.Function.LocalHomes.ContainsKey(destination.ArgumentIndex!.Value));
		var hidden = HiddenBufferArgument(fixture.Function);
		Assert.Equal(destination.ArgumentIndex, hidden.Address.ArgumentIndex);
		var access = Assert.Single(hidden.Address.ExactMemoryAccesses);
		Assert.Equal(M68kExactMemoryAccessKind.Address, access.Kind);
		Assert.Equal(80, access.Object.Size);
		Assert.Equal(destination.ArgumentIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), access.Object.Identity);
		Assert.All(Instructions(fixture.Function), instruction =>
			Assert.Equal(originalOrigins[instruction.Id], instruction.Origin));
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Fact]
	public void LaterByValueAggregateArgumentIsNotAnAddressEscape()
	{
		using var fixture = Build(nameof(AggregateReturnForwardingFixture.PrivateLocalPassedByValue));
		var outgoingSnapshot = Assert.Single(Instructions(fixture.Function), instruction =>
			instruction.Operation == M68kMachineOperation.OutgoingArgumentPush && instruction.ArgumentIndex == 80);
		var consumingCall = Assert.Single(Instructions(fixture.Function), instruction =>
			instruction.Operation == M68kMachineOperation.Call &&
			instruction.LogicalCall!.ArgumentValueIds.Contains(outgoingSnapshot.Uses[0]));
		Assert.DoesNotContain(outgoingSnapshot.Uses[0], consumingCall.Uses);

		var result = M68kAggregateReturnForwarding.Run(fixture.Function, fixture.Module);

		Assert.Equal(1, result.LocalsForwarded);
		Assert.Contains(Instructions(fixture.Function), instruction => instruction == outgoingSnapshot);
		Assert.Contains(Instructions(fixture.Function), instruction => instruction == consumingCall);
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Fact]
	public void LaterProvenReadOnlyReferenceIsNotAnAddressEscape()
	{
		using var fixture = Build(nameof(AggregateReturnForwardingFixture.PrivateLocalPassedReadOnly));
		var summaries = BuildSummaries(
			fixture.Module,
			nameof(AggregateReturnForwardingFixture.ConsumeReadOnly));
		var target = fixture.Module.ResolveEntryPoint(
			$"{typeof(AggregateReturnForwardingFixture).FullName}::" +
			nameof(AggregateReturnForwardingFixture.ConsumeReadOnly));
		Assert.Equal(
			M68kParameterMemoryEffect.Read,
			summaries[target.Identity].EffectForParameter(0));
		Assert.NotEqual(
			ParameterAttributes.None,
			target.ParameterFlags[0] & ParameterAttributes.In);

		var result = M68kAggregateReturnForwarding.Run(
			fixture.Function,
			fixture.Module,
			summaries);

		Assert.True(result.LocalsForwarded == 1, Dump(fixture.Function));
		Assert.Equal(80, result.TemporaryBytesRemoved);
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Fact]
	public void ReadOnlyReferenceRequiresClosedWorldEffectProof()
	{
		using var fixture = Build(nameof(AggregateReturnForwardingFixture.PrivateLocalPassedReadOnly));
		var before = Instructions(fixture.Function);

		var result = M68kAggregateReturnForwarding.Run(
			fixture.Function,
			fixture.Module);

		Assert.False(result.Changed);
		Assert.Equal(before, Instructions(fixture.Function));
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Fact]
	public void WritableReferenceKeepsThePrivateLocalSnapshot()
	{
		using var fixture = Build(nameof(AggregateReturnForwardingFixture.PrivateLocalPassedWritable));
		var summaries = BuildSummaries(
			fixture.Module,
			nameof(AggregateReturnForwardingFixture.ConsumeWritable));
		var before = Instructions(fixture.Function);

		var result = M68kAggregateReturnForwarding.Run(
			fixture.Function,
			fixture.Module,
			summaries);

		Assert.False(result.Changed);
		Assert.Equal(before, Instructions(fixture.Function));
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Fact]
	public void ProducerReadOnlyAliasKeepsItsInputSnapshot()
	{
		using var fixture = Build(nameof(AggregateReturnForwardingFixture.SameCallReadOnlyAlias));
		var summaries = BuildSummaries(
			fixture.Module,
			nameof(AggregateReturnForwardingFixture.MakeFromReadOnly));

		var result = M68kAggregateReturnForwarding.Run(
			fixture.Function,
			fixture.Module,
			summaries);

		Assert.Equal(1, result.LocalsForwarded);
		Assert.Single(Instructions(fixture.Function), instruction =>
			instruction.Operation == M68kMachineOperation.LocalStore &&
			instruction.ArgumentIndex is { } index &&
			index < fixture.Method.Locals.Length &&
			fixture.Function.LocalHomes[index].Size == 80);
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Fact]
	public void RepeatedPrivateAssignmentsReleaseBothCallTemporaries()
	{
		using var fixture = Build(nameof(AggregateReturnForwardingFixture.RepeatedPrivateAssignments));

		var result = M68kAggregateReturnForwarding.Run(fixture.Function, fixture.Module);

		Assert.Equal(2, result.LocalsForwarded);
		Assert.Equal(2, result.TemporaryHomesRemoved);
		Assert.Equal(160, result.TemporaryBytesRemoved);
		Assert.DoesNotContain(Instructions(fixture.Function), static instruction =>
			instruction.Operation == M68kMachineOperation.LocalStore);
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Fact]
	public void BothReturnExitsCanShareAndThenReleaseOneTemporary()
	{
		using var fixture = Build(nameof(AggregateReturnForwardingFixture.MultipleReturns));
		var sharedTemporary = Assert.Single(fixture.Function.ReusableAggregateReturnHomes);

		var result = M68kAggregateReturnForwarding.Run(fixture.Function, fixture.Module);

		Assert.True(result.ReturnBuffersForwarded == 2, $"{result}{Environment.NewLine}{Dump(fixture.Function)}");
		Assert.Equal(1, result.TemporaryHomesRemoved);
		Assert.Equal(80, result.TemporaryBytesRemoved);
		Assert.DoesNotContain(sharedTemporary.Value, fixture.Function.LocalHomes.Keys);
		Assert.Empty(fixture.Function.ReusableAggregateReturnHomes);
		var returns = Instructions(fixture.Function).Where(static instruction => instruction.Operation == M68kMachineOperation.Return).ToArray();
		Assert.Equal(2, returns.Length);
		Assert.All(returns, static instruction => Assert.True(instruction.ReturnBufferWritten));
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Fact]
	public void UnforwardedExitKeepsSharedTemporaryAndOriginalReturnCopy()
	{
		using var fixture = Build(nameof(AggregateReturnForwardingFixture.MixedReturns));
		var sharedTemporary = Assert.Single(fixture.Function.ReusableAggregateReturnHomes);

		var result = M68kAggregateReturnForwarding.Run(fixture.Function, fixture.Module);

		Assert.True(result.ReturnBuffersForwarded == 1, $"{result}{Environment.NewLine}{Dump(fixture.Function)}");
		Assert.Equal(0, result.TemporaryHomesRemoved);
		Assert.Contains(sharedTemporary.Value, fixture.Function.LocalHomes.Keys);
		var returns = Instructions(fixture.Function).Where(static instruction => instruction.Operation == M68kMachineOperation.Return).ToArray();
		Assert.Single(returns, static instruction => instruction.ReturnBufferWritten);
		Assert.Single(returns, static instruction => !instruction.ReturnBufferWritten && instruction.Uses.Length == 1);
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Theory]
	[InlineData(nameof(AggregateReturnForwardingFixture.SameCallByrefAlias))]
	[InlineData(nameof(AggregateReturnForwardingFixture.PreviouslyEscapedLocal))]
	[InlineData(nameof(AggregateReturnForwardingFixture.ImmediateByrefReturn))]
	[InlineData(nameof(AggregateReturnForwardingFixture.ImmediateRawPointerReturn))]
	[InlineData(nameof(AggregateReturnForwardingFixture.ImmediateNativePointerReturn))]
	[InlineData(nameof(AggregateReturnForwardingFixture.ImmediateApointerReturn))]
	[InlineData(nameof(AggregateReturnForwardingFixture.SameCallRawPointerAlias))]
	[InlineData(nameof(AggregateReturnForwardingFixture.ManyArgumentsReturn))]
	[InlineData(nameof(AggregateReturnForwardingFixture.ExceptionObservedLocal))]
	public void UnprovenDestinationsKeepSnapshotAndMetadataUnchanged(string entry)
	{
		using var fixture = Build(entry);
		var before = Instructions(fixture.Function);
		var homes = fixture.Function.LocalHomes.ToArray();
		var values = fixture.Function.Values.ToArray();

		var result = M68kAggregateReturnForwarding.Run(fixture.Function, fixture.Module);

		Assert.False(result.Changed);
		Assert.Equal(before, Instructions(fixture.Function));
		Assert.Equal(homes, fixture.Function.LocalHomes.ToArray());
		Assert.Equal(values, fixture.Function.Values.ToArray());
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Fact]
	public void VolatileMachineMemoryEffectKeepsThePrivateLocalSnapshot()
	{
		using var fixture = Build(nameof(AggregateReturnForwardingFixture.PrivateLocal));
		var block = Assert.Single(fixture.Function.Blocks, candidate => candidate.Instructions.Any(
			static instruction => instruction.Operation == M68kMachineOperation.Load &&
				instruction.SourceInstruction?.OpCode == OpCodes.Ldfld));
		var index = block.Instructions.FindIndex(static instruction => instruction.Operation == M68kMachineOperation.Load &&
			instruction.SourceInstruction?.OpCode == OpCodes.Ldfld);
		var load = block.Instructions[index];
		// The frontend currently rejects the volatile. CIL prefix. Add its
		// conservative memory effect to a real field-load instruction to test
		// this machine-IR boundary without fabricating any CIL or metadata token.
		block.Instructions[index] = load with { MemoryEffect = load.MemoryEffect | M68kMachineMemoryEffect.Volatile };
		var before = Instructions(fixture.Function);

		var result = M68kAggregateReturnForwarding.Run(fixture.Function, fixture.Module);

		Assert.False(result.Changed);
		Assert.Equal(before, Instructions(fixture.Function));
		M68kMachineIrVerifier.Verify(fixture.Function);
	}

	[Fact]
	public void StatisticsExcludeForwardingInMethodsRemovedByFinalReachability()
	{
		using var discarded = Build(nameof(AggregateReturnForwardingFixture.UnreachableForwarding));
		var module = discarded.Module;
		var fixtureName = typeof(AggregateReturnForwardingFixture).FullName;
		var entry = module.ResolveEntryPoint($"{fixtureName}::StatisticsEntry");
		var make = module.ResolveEntryPoint($"{fixtureName}::Make");
		var methods = new[] { entry, discarded.Method, make };
		var functions = methods.ToDictionary(static method => method.Identity,
			method => method.Identity == discarded.Method.Identity
				? discarded.Function : CilMachineIrBuilder.Build(method, module));
		var forwarding = new Dictionary<CilMethodIdentity, M68kAggregateReturnForwardingStatistics>();
		var result = M68kMachineModuleOptimizer.Run(methods, functions, module, M68kCpuTarget.M68000,
			roots: new HashSet<CilMethodIdentity> { entry.Identity }, beforeRetention: () =>
			{
				foreach (var (identity, function) in functions)
					forwarding[identity] = M68kAggregateReturnForwarding.Run(function, module);
			});

		// The optional method was transformed before the final graph excluded
		// it. Its reclaimed home must not inflate the retained-program report.
		Assert.Equal(1, forwarding[discarded.Method.Identity].ReturnBuffersForwarded);
		Assert.DoesNotContain(discarded.Method.Identity, result.RetainedMethodIdentities);
		var reported = M68kCodeGenerator.WithAggregateCopyStatistics(result, functions, forwarding);
		Assert.Equal(M68kAggregateReturnForwardingStatistics.Empty, reported.AggregateReturnForwarding);
		Assert.Equal(M68kBulkCopyStatistics.Empty, reported.BulkCopies);
	}

	private static (M68kMachineInstruction Push, M68kMachineInstruction Address) HiddenBufferArgument(M68kMachineFunction function)
	{
		var instructions = Instructions(function);
		var push = Assert.Single(instructions, instruction =>
			instruction.Operation == M68kMachineOperation.OutgoingArgumentPush && instruction.ArgumentIndex == 4);
		var address = Assert.Single(instructions, instruction => instruction.Definitions.Contains(push.Uses[0]));
		Assert.Equal(M68kMachineOperation.LocalAddress, address.Operation);
		return (push, address);
	}

	private static M68kMachineInstruction[] Instructions(M68kMachineFunction function) =>
		function.Blocks.SelectMany(static block => block.Instructions).ToArray();

	private static string Dump(M68kMachineFunction function) => string.Join(
		Environment.NewLine,
		new[]
		{
			$"locals={function.SourceMethod?.Locals.Length} homes=[{string.Join(',', function.LocalHomes.Keys)}] " +
			$"reusable=[{string.Join(',', function.ReusableAggregateReturnHomes.Values)}]"
		}.Concat(function.Blocks.SelectMany(block => new[]
		{
			$"block {block.Id} predecessors=[{string.Join(',', block.Predecessors)}] successors=[{string.Join(',', block.Successors)}]"
		}.Concat(block.Instructions.Select(instruction =>
			$"{instruction.Id} {instruction.Operation} arg={instruction.ArgumentIndex} " +
			$"uses=[{string.Join(',', instruction.Uses)}] " +
			$"defs=[{string.Join(',', instruction.Definitions)}] " +
			$"exact=[{string.Join(',', instruction.ExactMemoryAccesses.Select(static access => access.Kind))}] " +
			$"arguments=[{string.Join(',', instruction.LogicalCall?.ArgumentValueIds ?? [])}] " +
			$"results=[{string.Join(',', instruction.LogicalCall?.ResultValueIds ?? [])}]")))));

	private static IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary>
		BuildSummaries(CompilationModule module, params string[] entries)
	{
		var fixtureName = typeof(AggregateReturnForwardingFixture).FullName;
		var methods = entries.Select(entry =>
			module.ResolveEntryPoint($"{fixtureName}::{entry}")).ToArray();
		var functions = methods.ToDictionary(
			static method => method.Identity,
			method => BuildFunction(method, module));
		return M68kMethodMemorySummaryAnalyzer.Compute(methods, functions, module);
	}

	private static M68kMachineFunction BuildFunction(
		CilMethod method,
		CompilationModule module)
	{
		var function = CilMachineIrBuilder.Build(
			method,
			module,
			hasRuntimeFrame: method.ExceptionRegions.Count != 0);
		M68kExactMemoryAnnotator.AnnotateFrameAndArgumentAccesses(function);
		M68kMemoryPromotionPass.Run(function, new M68kMemoryPromotionContext(
			method,
			module,
			new Dictionary<CilMethodIdentity, M68kMethodMemorySummary>(),
			new HashSet<M68kMemoryObject>(),
			new Dictionary<int, M68kHeapOwnerFacts>(),
			function,
			FrameAndArgumentOnly: true));
		M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);
		return function;
	}

	private static Fixture Build(string entry)
	{
		var module = new CompilationModule(typeof(AggregateReturnForwardingFixture).Assembly.Location);
		try
		{
			var method = module.ResolveEntryPoint($"{typeof(AggregateReturnForwardingFixture).FullName}::{entry}");
			var function = BuildFunction(method, module);
			return new(module, method, function);
		}
		catch
		{
			module.Dispose();
			throw;
		}
	}

	private sealed record Fixture(CompilationModule Module, CilMethod Method, M68kMachineFunction Function) : IDisposable
	{
		public void Dispose() => Module.Dispose();
	}
}

public static class AggregateReturnForwardingFixture
{
	[StructLayout(LayoutKind.Sequential)]
	public struct Packet
	{
		public uint First;
		public uint Word01;
		public uint Word02;
		public uint Word03;
		public uint Word04;
		public uint Word05;
		public uint Word06;
		public uint Word07;
		public uint Word08;
		public uint Word09;
		public uint Word10;
		public uint Word11;
		public uint Word12;
		public uint Word13;
		public uint Word14;
		public uint Word15;
		public uint Word16;
		public uint Word17;
		public uint Word18;
		public uint Last;
	}

	public struct Platform
	{
		public uint Value;
	}

	private static nuint _escaped;
	private static uint _observed;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet Make(uint seed) => new() { First = seed, Last = 1 };

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet MakeWithPlatform(ref Platform platform, uint seed) => Make(platform.Value + seed);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet MakeAliased(ref Packet previous, uint seed)
	{
		previous.First = seed;
		return Make(previous.Last);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe Packet MakeRaw(Packet* previous, uint seed)
	{
		previous->First = seed;
		return Make(previous->Last);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe Packet MakeNative(nuint previous, uint seed) => MakeRaw((Packet*)previous, seed);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet MakeApointer(APTR previous, uint seed) => Make(APTR.ReadUInt32(previous, 0) + seed);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet MakeMany(uint a, uint b, uint c, uint d, uint e) => Make(a + b + c + d + e);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Consume(Packet value) => value.First + value.Last;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ConsumeReadOnly(in Packet value) => value.First + value.Last;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ConsumeWritable(ref Packet value)
	{
		value.Word01++;
		return value.First + value.Last;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet MakeFromReadOnly(in Packet value, uint seed) =>
		Make(value.First + seed);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe void Capture(ref Packet value)
	{
		fixed (Packet* address = &value)
		{
			_escaped = (nuint)address;
		}
	}

	public static Packet ImmediateReturn(uint seed) => Make(seed);

	public static uint StatisticsEntry() => 11;
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Packet UnreachableForwarding(uint seed) => Make(seed);

	public static uint PrivateLocal(uint seed)
	{
		var packet = Make(seed);
		return packet.First + packet.Last;
	}

	public static uint PrivateLocalWithPlatform(ref Platform platform, uint seed)
	{
		var packet = MakeWithPlatform(ref platform, seed);
		return packet.First + packet.Last;
	}

	public static uint PrivateLocalPassedByValue(uint seed)
	{
		var packet = Make(seed);
		var first = packet.First;
		return first + Consume(packet);
	}

	public static uint PrivateLocalPassedReadOnly(uint seed)
	{
		var packet = Make(seed);
		var first = packet.First;
		return first + ConsumeReadOnly(in packet);
	}

	public static uint PrivateLocalPassedWritable(uint seed)
	{
		var packet = Make(seed);
		var first = packet.First;
		return first + ConsumeWritable(ref packet);
	}

	public static uint SameCallReadOnlyAlias(uint seed)
	{
		var packet = Make(seed);
		packet = MakeFromReadOnly(in packet, seed + 1);
		return packet.First + packet.Last;
	}

	public static uint RepeatedPrivateAssignments(uint seed)
	{
		var packet = Make(seed);
		var first = packet.First;
		packet = Make(seed + 1);
		return first + packet.Last;
	}

	public static Packet MultipleReturns(uint seed)
	{
		if (seed == 0) return Make(7);
		return Make(seed);
	}

	public static Packet MixedReturns(uint seed)
	{
		if (seed == 0) return Make(7);
		return MakeMany(seed, 1, 2, 3, 4);
	}

	public static uint SameCallByrefAlias(uint seed)
	{
		var packet = Make(seed);
		packet = MakeAliased(ref packet, seed + 1);
		return packet.First + packet.Last;
	}

	public static uint PreviouslyEscapedLocal(uint seed)
	{
		var packet = Make(seed);
		Capture(ref packet);
		packet = Make(seed + 1);
		return packet.First + packet.Last;
	}

	public static Packet ImmediateByrefReturn(ref Platform platform, uint seed) => MakeWithPlatform(ref platform, seed);

	public static unsafe Packet ImmediateRawPointerReturn(Packet* previous, uint seed) => MakeRaw(previous, seed);

	public static Packet ImmediateNativePointerReturn(nuint previous, uint seed) => MakeNative(previous, seed);

	public static Packet ImmediateApointerReturn(APTR previous, uint seed) => MakeApointer(previous, seed);

	public static unsafe uint SameCallRawPointerAlias(uint seed)
	{
		var packet = Make(seed);
		packet = MakeRaw(&packet, seed + 1);
		return packet.First + packet.Last;
	}

	public static Packet ManyArgumentsReturn(uint seed) => MakeMany(seed, 1, 2, 3, 4);

	public static uint ExceptionObservedLocal(uint seed)
	{
		Packet packet = default;
		try
		{
			packet = Make(seed);
			return packet.First;
		}
		finally
		{
			_observed = packet.Last;
		}
	}

}
