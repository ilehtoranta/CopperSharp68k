/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class VolatileExecutionTests
{
	public static IEnumerable<object[]> Targets()
	{
		foreach (var cpu in new[] { M68kCpuTarget.M68000, M68kCpuTarget.M68020, M68kCpuTarget.M68040, M68kCpuTarget.M68060 })
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Bounded })
			yield return [cpu, mode];
	}

	[Theory]
	[MemberData(nameof(Targets))]
	public void StaticReadsObserveChangingValuesAndZeroStoresDoNotReadMemory(M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		var result = Compile(nameof(VolatileFixtures.StaticReads), target, mode);
		var address = FieldAddress(result, nameof(VolatileFixtures.Flag));
		var bus = new ObservedBus();
		bus.ReadScript[address] = n => (uint)n;
		Assert.Equal(12u, Execute(result, target, bus));
		Assert.Equal(new[] { $"W4:{address:X}:0", $"R4:{address:X}:1", $"R4:{address:X}:2" }, bus.Events);
	}

	[Theory]
	[MemberData(nameof(Targets))]
	public void InstanceReadsKeepEveryAccessToTheObjectField(M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		var result = Compile(nameof(VolatileFixtures.InstanceReads), target, mode, heap: true);
		var bus = new ObservedBus { DiscoverMarker = 0x13579BDF };
		Assert.Equal(12u, Execute(result, target, bus));
		Assert.NotNull(bus.DiscoveredAddress);
		var address = bus.DiscoveredAddress!.Value;
		Assert.Equal(new[] { $"W4:{address:X}:13579BDF", $"R4:{address:X}:1", $"R4:{address:X}:2" }, bus.Events);
	}

	[Theory]
	[MemberData(nameof(Targets))]
	public void VolatileReferenceReadsAreNotCommoned(M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		var result = Compile(nameof(VolatileFixtures.ReferenceReads), target, mode);
		var address = FieldAddress(result, nameof(VolatileFixtures.Reference));
		var bus = new ObservedBus();
		bus.ReadScript[address] = n => (uint)(0x7000 + n * 0x100);
		Assert.Equal(0u, Execute(result, target, bus));
		Assert.Equal(3, bus.Events.Count);
		Assert.Equal(2, bus.ReadCounts[address]);
	}

	[Theory]
	[MemberData(nameof(Targets))]
	public void ReleaseStoresPublishEveryPrecedingOrdinaryStore(M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		foreach (var entry in new[] { nameof(VolatileFixtures.ReleaseStores), nameof(VolatileFixtures.ReleaseThroughCalls) })
		{
			var result = Compile(entry, target, mode);
			var gate = FieldAddress(result, nameof(VolatileFixtures.Flag));
			var payload = FieldAddress(result, nameof(VolatileFixtures.Payload));
			var bus = new ObservedBus();
			var observed = new List<uint>();
			bus.Watch.Add(gate); bus.Watch.Add(payload);
			bus.AfterWrite = (address, _) => { if (address == gate) observed.Add(bus.Memory.ReadLong(payload)); };
			Assert.Equal(42u, Execute(result, target, bus));
			Assert.Equal(new uint[] { 11, 22, 33 }, observed);
			Assert.Equal(6, bus.Events.Count);
		}
	}

	[Theory]
	[MemberData(nameof(Targets))]
	public void AcquireReadInvalidatesOrdinaryMemoryFacts(M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		foreach (var entry in new[] { nameof(VolatileFixtures.AcquireReads), nameof(VolatileFixtures.AcquireThroughCalls) })
		{
			var result = Compile(entry, target, mode);
			var gate = FieldAddress(result, nameof(VolatileFixtures.Flag));
			var payload = FieldAddress(result, nameof(VolatileFixtures.Payload));
			var bus = new ObservedBus();
			bus.ReadScript[gate] = _ => { bus.Memory.WriteLong(payload, 2); return 7; };
			bus.Watch.Add(payload);
			Assert.Equal(12u, Execute(result, target, bus));
			Assert.Equal(1, bus.ReadCounts[gate]);
			Assert.Contains($"R4:{payload:X}:2", bus.Events);
		}
	}

	[Theory]
	[MemberData(nameof(Targets))]
	public void VolatileAddressTakenLocalStaysInMemory(M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		var result = CompilePath(CreateIndirectFixture("local", 4), "VolatileIndirect::Entry", target, mode);
		var bus = new ObservedBus { DiscoverMarker = 0x13579BDF, DiscoverOnStack = true };
		Assert.Equal(12u, Execute(result, target, bus));
		Assert.NotNull(bus.DiscoveredAddress);
		Assert.Equal(2, bus.ReadCounts[bus.DiscoveredAddress!.Value]);
		Assert.Equal(3, bus.Events.Count);
	}

	[Theory]
	[MemberData(nameof(Targets))]
	public void StaticAddressVolatileAccessesKeepTheirEffectsWithoutRedundantNullChecks(M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		foreach (string operation in new[] { "static", "static-clobber", "static-other", "static-call", "static-branch" })
		{
			var result = CompilePath(CreateIndirectFixture(operation, 4), "VolatileIndirect::Entry", target, mode,
				output: M68kOutputFormat.Assembly);
			var relocation = result.Relocations.First(r => r.Target == "static:04000001");
			uint address = 0x10000 + BinaryPrimitives.ReadUInt32BigEndian(result.Code.AsSpan(relocation.Offset, 4));
			foreach (uint condition in new uint[] { 0, 1 })
			{
				var bus = new ObservedBus();
				bus.ReadScript[address] = n => (uint)n;
				bus.ReadScript[0x5000] = _ => condition;
				bus.ReadScript[0x6000] = _ => 77;
				bus.ReadScript[0x7000] = _ => 88;
				Assert.Equal(12u, Execute(result, target, bus));
				Assert.Equal(new[] { $"W4:{address:X}:0", $"R4:{address:X}:1", $"R4:{address:X}:2" },
					bus.Events.Where(e => e.Contains($":{address:X}:")));
			}
			Assert.DoesNotContain("allocated_nonnull", result.Text!, StringComparison.Ordinal);
			Assert.True(result.Text!.Split('\n').Count(line => line.Trim() == "nop") >= 6);
			if (operation == "static")
				Assert.Single(result.Text.Split('\n').Where(line => line.Contains("#C68K_static_003A04000001,")));
		}
	}

	[Theory]
	[MemberData(nameof(Targets))]
	public void IndirectReadsWritesAndPollingPreserveWidthCountAndOrder(M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		foreach (int width in new[] { 1, 2, 4 })
		foreach (string operation in new[] { "read", "discard", "write", "poll" })
		{
			var fixture = CreateIndirectFixture(operation, width);
			var result = CompilePath(fixture, "VolatileIndirect::Entry", target, mode);
			var bus = new ObservedBus();
			const uint address = 0x5000;
			if (operation == "write") bus.Watch.Add(address);
			else bus.ReadScript[address] = operation == "poll" ? n => n < 4 ? 1u : 0u : n => (uint)n;
			Assert.Equal(operation == "read" ? 12u : operation == "discard" ? 2u : 42u, Execute(result, target, bus));
			if (operation == "write")
				Assert.Equal(new[] { $"W{width}:5000:0", $"W{width}:5000:0", $"W{width}:5000:7", $"W{width}:5000:0" }, bus.Events);
			else
			{
				Assert.Equal(operation == "poll" ? 4 : 2, bus.ReadCounts[address]);
				Assert.All(bus.Events, e => Assert.StartsWith($"R{width}:5000:", e));
			}
		}
	}

	[Theory]
	[MemberData(nameof(Targets))]
	public void WideScalarAndTypedIndirectAccessesKeepTheirMemoryOperations(M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		var wideRead = CompilePath(CreateIndirectFixture("read", 8), "VolatileIndirect::Entry", target, mode);
		var bus = new ObservedBus();
		bus.Watch.Add(0x5000); bus.ReadScript[0x5004] = n => (uint)n;
		Assert.Equal(12u, Execute(wideRead, target, bus));
		Assert.Equal(2, bus.ReadCounts[0x5004]);
		Assert.Equal(2, bus.Events.Count(e => e == "R4:5000:0"));
		var wideWrite = CompilePath(CreateIndirectFixture("write", 8), "VolatileIndirect::Entry", target, mode);
		bus = new ObservedBus(); bus.Watch.Add(0x5000); bus.Watch.Add(0x5004);
		Assert.Equal(42u, Execute(wideWrite, target, bus));
		Assert.Equal(new[] { "W4:5000:0", "W4:5004:0", "W4:5000:0", "W4:5004:0", "W4:5000:0", "W4:5004:7", "W4:5000:0", "W4:5004:0" }, bus.Events);
		var typedRead = CompilePath(CreateIndirectFixture("typed-read", 4), "VolatileIndirect::Entry", target, mode);
		bus = new ObservedBus(); bus.ReadScript[0x5000] = n => (uint)n;
		Assert.Equal(12u, Execute(typedRead, target, bus));
		Assert.Equal(2, bus.ReadCounts[0x5000]);
		var typedWrite = CompilePath(CreateIndirectFixture("typed-write", 4), "VolatileIndirect::Entry", target, mode);
		bus = new ObservedBus(); bus.Watch.Add(0x5000);
		Assert.Equal(42u, Execute(typedWrite, target, bus));
		Assert.Equal(new[] { "W4:5000:0", "W4:5000:0", "W4:5000:7", "W4:5000:0" }, bus.Events);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kFloatingPointMode.SoftFloat, M68kCpuModel.M68000)]
	// Copper68k executes the common 68882/040 instruction subset on its 040 core.
	[InlineData(M68kCpuTarget.M68020, M68kFloatingPointMode.M68882, M68kCpuModel.M68040)]
	[InlineData(M68kCpuTarget.M68040, M68kFloatingPointMode.M68040, M68kCpuModel.M68040)]
	[InlineData(M68kCpuTarget.M68060, M68kFloatingPointMode.M68040, M68kCpuModel.M68040)]
	public void FloatingPointVolatileReadsKeepTheirBitPatterns(M68kCpuTarget target, M68kFloatingPointMode floating, M68kCpuModel model)
	{
		var result = CompilePath(typeof(VolatileFixtures).Assembly.Location,
			typeof(VolatileFixtures).FullName + "::FloatReads", target, M68kPeepholeOptimizationMode.FixedPoint, floating: floating);
		var address = FieldAddress(result, nameof(VolatileFixtures.Single));
		var bus = new ObservedBus();
		bus.ReadScript[address] = n => n == 1 ? 0x3F800000u : 0x40000000u;
		Assert.Equal(0x7F800000u, Execute(result, target, bus, model));
		Assert.Equal(2, bus.ReadCounts[address]);
	}

	[Fact]
	public void VolatilePrefixIsAttachedToItsMemoryInstructionAndMirEffect()
	{
		using var module = new CompilationModule(typeof(VolatileFixtures).Assembly.Location);
		var method = module.ResolveEntryPoint(typeof(VolatileFixtures).FullName + "::StaticReads");
		Assert.Equal(3, method.Instructions.Count(i => i.IsVolatile));
		Assert.DoesNotContain(method.Instructions, i => i.OpCode == OpCodes.Volatile);
		var function = CilMachineIrBuilder.Build(method, module);
		Assert.Equal(3, function.Blocks.SelectMany(b => b.Instructions).Count(i => (i.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0));
	}

	[Theory]
	[InlineData(M68kFloatingPointMode.Disabled)]
	[InlineData(M68kFloatingPointMode.M68040)]
	public void SynchronizationSurvivesFinalAssemblyEmission(M68kFloatingPointMode floating)
	{
		var result = CompilePath(typeof(VolatileFixtures).Assembly.Location,
			typeof(VolatileFixtures).FullName + "::StaticReads", M68kCpuTarget.M68040,
			M68kPeepholeOptimizationMode.FixedPoint, floating: floating, output: M68kOutputFormat.Assembly);
		var instructions = result.Text!.Split('\n').Select(line => line.Trim()).ToArray();
		Assert.True(instructions.Count(line => line == "nop") >= 6);
		Assert.Equal(floating == M68kFloatingPointMode.Disabled ? 0 : 6, instructions.Count(line => line == "fnop"));
	}

	[Theory]
	[InlineData("FE13")]
	[InlineData("FE13FE132A")]
	[InlineData("FE13172A")]
	public void MalformedPrefixesAreRejected(string bytes)
	{
		var error = Assert.Throws<M68kCompilationException>(() => CilInstructionDecoder.Decode(Convert.FromHexString(bytes), "BadPrefix"));
		Assert.Equal(M68kDiagnosticIds.InvalidMetadata, error.DiagnosticId);
	}

	[Theory]
	[InlineData("FE13FE17")]
	[InlineData("FE13FE18")]
	[InlineData("FE13FE1201")]
	public void UnsupportedBulkAndUnalignedVolatileAccessesAreRejected(string bytes)
	{
		var error = Assert.Throws<M68kCompilationException>(() => CilInstructionDecoder.Decode(Convert.FromHexString(bytes), "UnsupportedPrefix"));
		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, error.DiagnosticId);
	}

	[Fact]
	public void BranchCannotBypassTheVolatilePrefix()
	{
		var error = Assert.Throws<M68kCompilationException>(() => CompilePath(CreateIndirectFixture("branch", 4),
			"VolatileIndirect::Entry", M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.FixedPoint));
		Assert.Equal(M68kDiagnosticIds.InvalidMetadata, error.DiagnosticId);
	}

	[Fact]
	public void AggregateVolatileAccessIsRejectedExplicitly()
	{
		var error = Assert.Throws<M68kCompilationException>(() => CompilePath(CreateIndirectFixture("aggregate", 4),
			"VolatileIndirect::Entry", M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.FixedPoint));
		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, error.DiagnosticId);
		Assert.Contains("aggregate", error.Message, StringComparison.Ordinal);
	}

	private static M68kCompilationResult Compile(string entry, M68kCpuTarget cpu, M68kPeepholeOptimizationMode mode, bool heap = false) =>
		CompilePath(typeof(VolatileFixtures).Assembly.Location, typeof(VolatileFixtures).FullName + "::" + entry, cpu, mode, heap);

	private static M68kCompilationResult CompilePath(string path, string entry, M68kCpuTarget cpu, M68kPeepholeOptimizationMode mode,
		bool heap = false, M68kFloatingPointMode floating = M68kFloatingPointMode.Disabled,
		M68kOutputFormat output = M68kOutputFormat.Hunk) => M68kCompiler.Compile(new() {
		AssemblyPath = path, EntryPoint = entry, IncludedExportNames = [], Cpu = cpu,
		ManagedAssemblyPaths = [typeof(M68kRuntime).Assembly.Location, typeof(CopperSharp.Runtime.ManagedPool).Assembly.Location],
		OutputFormat = output, RuntimeProfile = M68kRuntimeProfile.Application,
		MemoryManagement = heap ? M68kMemoryManagement.ManagedPoolMarkSweepGc : M68kMemoryManagement.None,
		ExceptionMode = M68kExceptionMode.Full, FloatingPoint = floating,
		PeepholeOptimization = mode, ClrPolicy = M68kClrPolicy.Always,
		CodeSizeOptimizations = new() { CompactApplicationStatics = false }, Heap = new() { StartAddress = 0x100000, Size = 0x8000 }
	});

	private static uint FieldAddress(M68kCompilationResult result, string name)
	{
		var field = typeof(VolatileFixtures).GetField(name, BindingFlags.Static | BindingFlags.Public)!;
		var relocation = result.Relocations.First(r => r.Target == $"static:{field.MetadataToken:X8}");
		return 0x10000 + BinaryPrimitives.ReadUInt32BigEndian(result.Code.AsSpan(relocation.Offset, 4));
	}

	private static uint Execute(M68kCompilationResult result, M68kCpuTarget target, ObservedBus bus, M68kCpuModel? model = null)
	{
		result.Code.CopyTo(bus.Memory.Memory.AsSpan(0x10000));
		foreach (var relocation in result.Relocations)
		{
			uint at = 0x10000 + (uint)relocation.Offset;
			bus.Memory.WriteLong(at, bus.Memory.ReadLong(at) + 0x10000);
		}
		bus.Memory.WriteLong(0x200000, 0x1000);
		using var cpu = M68kCoreFactory.Default.Create(model ?? (target == M68kCpuTarget.M68060 ? M68kCpuModel.M68040 : Enum.Parse<M68kCpuModel>(target.ToString())), bus);
		cpu.Reset(0x10000 + result.EntryPoint, 0x200000);
		for (int n = 0; n < 1_000_000; n++)
		{
			if (cpu.State.ProgramCounter == 0x1000)
			{
				Assert.Equal(0x200004u, cpu.State.A[7]);
				return cpu.State.D[0];
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted || cpu.State.Stopped, $"{target} stopped at {cpu.State.ProgramCounter:X8}");
		}
		throw new InvalidOperationException("Volatile execution/polling exceeded the instruction budget.");
	}

	private static string CreateIndirectFixture(string operation, int width)
	{
		string staticOperation = operation;
		bool typed = operation.StartsWith("typed-", StringComparison.Ordinal);
		if (typed) operation = operation[6..];
		var directory = Path.Combine(AppContext.BaseDirectory, "VolatileProbes", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("VolatileIndirect"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("VolatileIndirect");
		var type = module.DefineType("VolatileIndirect", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var method = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = method.GetILGenerator();
		var field = operation.StartsWith("static", StringComparison.Ordinal) ? type.DefineField("Value", typeof(int), FieldAttributes.Public | FieldAttributes.Static) : null;
		var otherField = staticOperation == "static-other" ? type.DefineField("Other", typeof(int), FieldAttributes.Public | FieldAttributes.Static) : null;
		if (field is not null)
		{
			operation = "read";
			il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Volatile); il.Emit(OpCodes.Stsfld, field);
		}
		bool local = operation == "local";
		if (local)
		{
			operation = "read";
			il.DeclareLocal(typeof(int)); il.Emit(OpCodes.Ldc_I4, 0x13579BDF); il.Emit(OpCodes.Stloc_0);
		}
		void Address() {
			if (field is not null) il.Emit(OpCodes.Ldsflda, field);
			else if (local) il.Emit(OpCodes.Ldloca_S, (byte)0);
			else { il.Emit(OpCodes.Ldc_I4, 0x5000); il.Emit(OpCodes.Conv_U); }
		}
		void Read() {
			Address(); il.Emit(OpCodes.Volatile);
			if (typed) il.Emit(OpCodes.Ldobj, typeof(int));
			else il.Emit(width == 1 ? OpCodes.Ldind_U1 : width == 2 ? OpCodes.Ldind_U2 : width == 8 ? OpCodes.Ldind_I8 : OpCodes.Ldind_I4);
			if (width == 8) il.Emit(OpCodes.Conv_I4);
		}
		void ReadForeign(int address)
		{
			il.Emit(OpCodes.Ldc_I4, address); il.Emit(OpCodes.Conv_U);
			il.Emit(OpCodes.Volatile); il.Emit(OpCodes.Ldind_I4); il.Emit(OpCodes.Pop);
		}
		if (operation == "branch")
		{
			Address(); var interior = il.DefineLabel(); il.Emit(OpCodes.Br, interior);
			il.Emit(OpCodes.Volatile); il.MarkLabel(interior); il.Emit(OpCodes.Ldind_I4);
		}
		else if (operation == "aggregate")
		{
			var aggregate = module.DefineType("Aggregate", TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed, typeof(ValueType));
			aggregate.DefineField("First", typeof(int), FieldAttributes.Public);
			aggregate.DefineField("Second", typeof(int), FieldAttributes.Public);
			aggregate.CreateType(); Address(); il.Emit(OpCodes.Volatile); il.Emit(OpCodes.Ldobj, aggregate);
			il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldc_I4, 42);
		}
		else if (operation == "write")
		{
			foreach (int value in new[] { 0, 0, 7, 0 })
			{
				Address();
				if (width == 8) il.Emit(OpCodes.Ldc_I8, (long)value); else il.Emit(OpCodes.Ldc_I4, value);
				il.Emit(OpCodes.Volatile);
				if (typed) il.Emit(OpCodes.Stobj, typeof(int));
				else il.Emit(width == 1 ? OpCodes.Stind_I1 : width == 2 ? OpCodes.Stind_I2 : width == 8 ? OpCodes.Stind_I8 : OpCodes.Stind_I4);
			}
			il.Emit(OpCodes.Ldc_I4, 42);
		}
		else if (operation == "poll")
		{
			var loop = il.DefineLabel(); il.MarkLabel(loop); Read(); il.Emit(OpCodes.Brtrue, loop); il.Emit(OpCodes.Ldc_I4, 42);
		}
		else
		{
			Read();
			if (operation == "discard") il.Emit(OpCodes.Pop);
			else { il.Emit(OpCodes.Ldc_I4, 10); il.Emit(OpCodes.Mul); }
			if (staticOperation == "static-clobber") ReadForeign(0x6000);
			else if (otherField is not null)
			{
				il.Emit(OpCodes.Ldsflda, otherField); il.Emit(OpCodes.Volatile); il.Emit(OpCodes.Ldind_I4); il.Emit(OpCodes.Pop);
			}
			else if (staticOperation == "static-call")
			{
				var helper = type.DefineMethod("Clobber", MethodAttributes.Private | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
				helper.SetImplementationFlags(MethodImplAttributes.NoInlining);
				var helperIl = helper.GetILGenerator();
				helperIl.Emit(OpCodes.Ldc_I4, 0x6000); helperIl.Emit(OpCodes.Conv_U);
				helperIl.Emit(OpCodes.Volatile); helperIl.Emit(OpCodes.Ldind_I4); helperIl.Emit(OpCodes.Pop); helperIl.Emit(OpCodes.Ret);
				il.Emit(OpCodes.Call, helper);
			}
			else if (staticOperation == "static-branch")
			{
				var taken = il.DefineLabel(); var join = il.DefineLabel();
				il.Emit(OpCodes.Ldc_I4, 0x5000); il.Emit(OpCodes.Conv_U); il.Emit(OpCodes.Ldind_I4);
				il.Emit(OpCodes.Brtrue, taken); ReadForeign(0x6000); il.Emit(OpCodes.Br, join);
				il.MarkLabel(taken); ReadForeign(0x7000); il.MarkLabel(join);
			}
			Read(); if (operation == "read") il.Emit(OpCodes.Add);
		}
		il.Emit(OpCodes.Ret); type.CreateType();
		var path = Path.Combine(directory, "VolatileIndirect.dll"); assembly.Save(path); return path;
	}

	private sealed class ObservedBus : IM68kBus
	{
		public TestBus Memory { get; } = new();
		public Dictionary<uint, Func<int, uint>> ReadScript { get; } = new();
		public Dictionary<uint, int> ReadCounts { get; } = new();
		public HashSet<uint> Watch { get; } = new();
		public List<string> Events { get; } = new();
		public Action<uint, uint>? AfterWrite { get; set; }
		public uint? DiscoverMarker { get; init; }
		public bool DiscoverOnStack { get; init; }
		public uint? DiscoveredAddress { get; private set; }
		private uint Read(uint address, int width)
		{
			uint value = ReadScript.TryGetValue(address, out var script)
				? script(ReadCounts[address] = ReadCounts.GetValueOrDefault(address) + 1)
				: width == 1 ? Memory.Memory[address] : width == 2 ? Memory.ReadWord(address) : Memory.ReadLong(address);
			if (ReadScript.ContainsKey(address) || Watch.Contains(address)) Events.Add($"R{width}:{address:X}:{value:X}");
			return value;
		}
		private void Write(uint address, uint value, int width)
		{
			if (DiscoverMarker == value && DiscoveredAddress is null &&
				(DiscoverOnStack ? address >= 0x1F0000 && address < 0x200000 : address >= 0x100000 && address < 0x108000))
			{
				DiscoveredAddress = address; ReadScript[address] = n => (uint)n;
			}
			if (ReadScript.ContainsKey(address) || Watch.Contains(address)) Events.Add($"W{width}:{address:X}:{value:X}");
			if (width == 1) Memory.Memory[address] = (byte)value;
			else if (width == 2) Memory.WriteWord(address, (ushort)value);
			else Memory.WriteLong(address, value);
			AfterWrite?.Invoke(address, value);
		}
		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind) =>
			kind == M68kBusAccessKind.CpuInstructionFetch ? Memory.Memory[address] : (byte)Read(address, 1);
		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind) =>
			kind == M68kBusAccessKind.CpuInstructionFetch ? Memory.ReadWord(address) : (ushort)Read(address, 2);
		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind) =>
			kind == M68kBusAccessKind.CpuInstructionFetch ? Memory.ReadLong(address) : Read(address, 4);
		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind) => Write(address, value, 1);
		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind) => Write(address, value, 2);
		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind) => Write(address, value, 4);
		public void ResetExternalDevices(long cycle) { }
	}
}

public static class VolatileFixtures
{
	public static volatile int Flag;
	public static int Payload;
	public static volatile object? Reference;
	public static volatile float Single;
	public sealed class Holder { public volatile int Value; }
	public static int StaticReads() { Flag = 0; return Flag * 10 + Flag; }
	public static int InstanceReads() { var holder = new Holder(); holder.Value = 0x13579BDF; return holder.Value * 10 + holder.Value; }
#pragma warning disable CS1718
	public static int ReferenceReads() { Reference = null; return Reference == Reference ? 1 : 0; }
#pragma warning restore CS1718
	public static int ReleaseStores() { Payload = 11; Flag = 1; Payload = 22; Flag = 2; Payload = 33; Flag = 3; return 42; }
	public static int ReleaseThroughCalls() { Payload = 11; Publish(1); Payload = 22; Publish(2); Payload = 33; Publish(3); return 42; }
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Publish(int value) { Flag = value; }
	public static int AcquireReads() { Payload = 1; Flag = 0; int before = Payload; _ = Flag; return before * 10 + Payload; }
	public static int AcquireThroughCalls() { Payload = 1; Flag = 0; int before = Payload; _ = ReadFlag(); return before * 10 + Payload; }
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadFlag() => Flag;
	public static unsafe uint FloatReads() { Single = 123.5f; float first = Single, second = Single; return *(uint*)&first ^ *(uint*)&second; }
}
