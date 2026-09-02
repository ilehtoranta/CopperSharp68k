/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kFrameClearRunsExecutionTests
{
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	private const uint Load = 0x10000, Stack = 0x80000, Sentinel = 0x1000, Observer = 0x22000;
	internal sealed record Shape(string Name, int[] Homes);
	internal sealed record Pressure(string Name, bool Arguments, M68kRegister[] Saved, M68kRegister? Reserved = null);
	internal static readonly Shape[] Shapes =
	[
		new("two-33", [33, -1, 33]),
		new("setup-boundary", [1, -1, 13, -1, 1]),
		new("mixed", [2, -1, 16, -2, 3, -1, 20, -1, 2]),
		new("small-runs", [8, -1, 8]),
		new("preserved-boundary", [17, -1, 17]),
		new("one-zero-register", [47, -1, 1]),
		new("long-preserved", [66, -1, 1]),
		new("quick-counter", [512, -1, 1]),
		new("long-counter", [516, -1, 2]),
		new("single-contiguous", [17]),
		new("only-small", [1, -1, 1, -1, 1])
	];
	internal static readonly Pressure[] Pressures =
	[
		new("free", false, []),
		new("all-incoming", true, []),
		new("saved-address", true, [M68kRegister.A2]),
		new("one-saved-data", true, [M68kRegister.D2]),
		new("saved-scratch", true, [M68kRegister.D2, M68kRegister.D3, M68kRegister.A2]),
		new("reserved-a0", false, [], M68kRegister.A0),
		new("reserved-a1", false, [], M68kRegister.A1)
	];

	public static IEnumerable<object[]> Cases()
	{
		foreach (var shape in Shapes)
		foreach (var pressure in Pressures.Where(item => item.Reserved is null))
		foreach (var target in new[] { M68kCpuTarget.M68000, M68kCpuTarget.M68020 })
		foreach (var clr in new[] { M68kClrPolicy.Auto, M68kClrPolicy.Always })
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
			yield return [shape.Name, pressure.Name, target, clr, mode, false];
		foreach (var pressure in Pressures.Where(item => item.Reserved is not null))
		foreach (var target in new[] { M68kCpuTarget.M68000, M68kCpuTarget.M68020 })
		foreach (var clr in new[] { M68kClrPolicy.Auto, M68kClrPolicy.Always })
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
			yield return ["two-33", pressure.Name, target, clr, mode, false];
		foreach (var target in new[] { M68kCpuTarget.M68000, M68kCpuTarget.M68020 })
			yield return ["two-33", "all-incoming", target, M68kClrPolicy.Auto, M68kPeepholeOptimizationMode.Disabled, true];
	}

	[Theory]
	[MemberData(nameof(Cases))]
	public void InitializationPreservesHolesArgumentsAndStack(
		string shape, string pressure, M68kCpuTarget target, M68kClrPolicy clr,
		M68kPeepholeOptimizationMode mode, bool dynamicFrame)
	{
		foreach (var remainder in new uint[] { 0, 2 })
			Measure(shape, pressure, target, clr, mode, dynamicFrame, remainder);
	}

	internal sealed record Measurement(string Case, string Cpu, string Clr, string Mode,
		bool Dynamic, uint StackRemainder, int InitializedBytes, int FrameBytes, int CodeBytes,
		int InitializerBytes, long Cycles, int LoopCount, string FrameSHA256, string CodeSHA256,
		string MethodCilSHA256, string AssemblyText);

	internal static Measurement Measure(string shapeName, string pressureName, M68kCpuTarget target,
		M68kClrPolicy clr, M68kPeepholeOptimizationMode mode, bool dynamicFrame, uint remainder)
	{
		var shape = Shapes.Single(item => item.Name == shapeName);
		var pressure = Pressures.Single(item => item.Name == pressureName);
		var context = $"{shapeName}/{pressureName}/{target}/{clr}/{mode}/dynamic={dynamicFrame}/SP+{remainder}";
		using var module = new CompilationModule(typeof(FrameClearRunMetadataFixtures).Assembly.Location);
		var method = module.ResolveEntryPoint($"{typeof(FrameClearRunMetadataFixtures).FullName}::" +
			(pressure.Arguments ? nameof(FrameClearRunMetadataFixtures.Arguments) : nameof(FrameClearRunMetadataFixtures.NoArguments)));
		Assert.True(method.InitializeLocals); // Real C# method metadata, never a fabricated token.
		var request = new M68kCompilationRequest
		{
			AssemblyPath = typeof(FrameClearRunMetadataFixtures).Assembly.Location, Cpu = target,
			ClrPolicy = clr, OutputFormat = M68kOutputFormat.Assembly, RuntimeProfile = M68kRuntimeProfile.Freestanding,
			ExceptionMode = M68kExceptionMode.Yolo, MemoryManagement = M68kMemoryManagement.None, IncludedExportNames = []
		};
		var generator = new M68kCodeGenerator(module, request, []);
		var assembler = (M68kAssembler)typeof(M68kCodeGenerator).GetField("_assembler", PrivateInstance)!.GetValue(generator)!;
		var abi = Invoke(generator, "GetInternalCallAbi", method)!;
		// This is an allocated-frame emission test. Private home layouts are
		// deliberately controlled so holes cannot disappear through promotion.
		// Actual CIL supplies InitializeLocals and the six-argument calling ABI.
		var function = new M68kMachineFunction("frame-clear-slice", 0, method)
		{
			HasDynamicStackAllocation = dynamicFrame
		};
		if (pressure.Reserved is { } reserved)
			function.ReservedRegisters = function.ReservedRegisters.Add(reserved);
		var entry = new M68kMachineBlock(0, 0);
		function.Blocks.Add(entry);
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return,
			method.Instructions[^1].Offset, sourceInstruction: method.Instructions[^1]));
		for (var index = 0; index < shape.Homes.Length; index++)
			function.LocalHomes.Add(index, new(index, Math.Abs(shape.Homes[index]) * 4, false, shape.Homes[index] > 0));
		var allocated = M68kRegisterAllocatorPipeline.Run(function);
		var argumentOffsets = new Dictionary<int, int>();
		var localBytes = shape.Homes.Sum(count => Math.Abs(count) * 4);
		if (pressure.Arguments)
			for (var index = 0; index < 6; index++)
			{
				function.ArgumentHomes.Add(index, new(index, 4, false));
				argumentOffsets.Add(index, localBytes + index * 4);
			}
		var saved = pressure.Saved.Concat(dynamicFrame ? new[] { M68kRegister.A5 } : [])
			.Distinct().Order().ToArray();
		allocated = allocated with { Frame = allocated.Frame with
		{
			ArgumentHomeOffsets = argumentOffsets, CalleeSavedRegisters = saved,
			FrameBytes = localBytes + argumentOffsets.Count * 4
		} };
		typeof(M68kCodeGenerator).GetField("_emittingAllocatedFunction", PrivateInstance)!.SetValue(generator, allocated);
		assembler.Mark("method:frame-clear-slice");
		Invoke(generator, "EmitAllocatedCalleeSaves", saved);
		Invoke(generator, "EmitAllocateFrame", allocated.Frame.FrameBytes);
		if (dynamicFrame) assembler.EmitWord(0x2A4F); // MOVEA.L A7,A5 fixed anchor.
		var initializerStart = assembler.Offset;
		Invoke(generator, "EmitAllocatedFrameHomeInitialization", method, abi, allocated,
			saved.Length * 4 + allocated.Frame.FrameBytes);
		var initializerBytes = assembler.Offset - initializerStart;
		assembler.EmitJsr("observer", external: true);
		// A frame read after the opaque observer prevents tail-call conversion.
		assembler.EmitWord(0x23EF); // MOVE.L d16(A7),absolute.L
		assembler.EmitWord(0);
		assembler.EmitLong(0x30000);
		Invoke(generator, "EmitAllocatedCalleeRestores", saved, allocated.Frame.FrameBytes);
		assembler.EmitWord(0x4E75);
		assembler.Mark("method:frame-clear-slice:end");
		if (mode != M68kPeepholeOptimizationMode.Disabled)
			assembler.OptimizeForCpu(target, clr, peepholeOptimization: mode);
		var linked = assembler.Link(Load, new Dictionary<string, uint> { ["observer"] = Observer });
		var assembly = assembler.RenderAssembly(target);
		var bus = new TestBus(0x100000);
		var stack = Stack + remainder;
		var frame = stack - (uint)(saved.Length * 4 + allocated.Frame.FrameBytes);
		bus.Memory.AsSpan((int)frame - 64, (int)(stack - frame) + 160).Fill(0xA5);
		linked.Bytes.CopyTo(bus.Memory.AsSpan((int)Load));
		bus.WriteWord(Observer, 0x4E75);
		bus.WriteLong(stack, Sentinel);
		uint[] arguments = [0x10203040, 0x50607080, 0x90A0B0C0, 0xD0E0F001, 0x23456789, 0xABCDEF10];
		bus.WriteLong(stack + 4, arguments[4]);
		bus.WriteLong(stack + 8, arguments[5]);
		using var cpu = M68kCoreFactory.Default.Create(target == M68kCpuTarget.M68000 ? M68kCpuModel.M68000 : M68kCpuModel.M68020, bus);
		cpu.Reset(Load, stack);
		for (var index = 0; index < 8; index++) cpu.State.D[index] = 0x11220000u + (uint)index;
		for (var index = 0; index < 7; index++) cpu.State.A[index] = 0x44550000u + (uint)index;
		if (pressure.Arguments)
		{
			cpu.State.D[0] = arguments[0]; cpu.State.D[1] = arguments[1];
			cpu.State.A[0] = arguments[2]; cpu.State.A[1] = arguments[3];
		}
		var incomingData = cpu.State.D.ToArray();
		var incomingAddress = cpu.State.A.ToArray();
		var observed = false;
		string? frameHash = null;
		for (var step = 0; step < 200_000 && cpu.State.ProgramCounter != Sentinel; step++)
		{
			if (cpu.State.ProgramCounter == Observer)
			{
				Assert.False(observed);
				observed = true;
				Assert.Equal(frame - 4, cpu.State.A[7]);
				if (pressure.Reserved is { } observedReserved)
					Assert.Equal(incomingAddress[(int)observedReserved - (int)M68kRegister.A0],
						cpu.State.A[(int)observedReserved - (int)M68kRegister.A0]);
				var offset = 0;
				foreach (var home in shape.Homes)
				{
					var bytes = Math.Abs(home) * 4;
					var expected = home > 0 ? (byte)0 : (byte)0xA5;
					Assert.All(bus.Memory.AsSpan((int)frame + offset, bytes).ToArray(), value => Assert.Equal(expected, value));
					offset += bytes;
				}
				if (pressure.Arguments)
					for (var index = 0; index < 6; index++)
						Assert.Equal(arguments[index], bus.ReadLong(frame + (uint)argumentOffsets[index]));
				frameHash = Hash(bus.Memory.AsSpan((int)frame, allocated.Frame.FrameBytes).ToArray());
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted, context + ": CPU halted.");
		}
		Assert.True(observed, context + ": observer was not reached.");
		Assert.Equal(Sentinel, cpu.State.ProgramCounter);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		if (pressure.Reserved is { } finalReserved)
			Assert.Equal(incomingAddress[(int)finalReserved - (int)M68kRegister.A0],
				cpu.State.A[(int)finalReserved - (int)M68kRegister.A0]);
		for (var index = pressure.Arguments ? 0 : 2; index < 8; index++) Assert.Equal(incomingData[index], cpu.State.D[index]);
		for (var index = pressure.Arguments ? 0 : 2; index < 7; index++) Assert.Equal(incomingAddress[index], cpu.State.A[index]);
		Assert.Equal(arguments[4], bus.ReadLong(stack + 4));
		Assert.Equal(arguments[5], bus.ReadLong(stack + 8));
		Assert.All(bus.Memory.AsSpan((int)frame - 64, 48).ToArray(), value => Assert.Equal(0xA5, value));
		Assert.All(bus.Memory.AsSpan((int)stack + 12, 64).ToArray(), value => Assert.Equal(0xA5, value));
		Assert.Equal(linked.Bytes, bus.Memory.AsSpan((int)Load, linked.Bytes.Length).ToArray());
		var cilText = string.Join(";", method.Instructions.Select(instruction =>
			$"{instruction.Offset}:{instruction.OpCode.Value}:{instruction.Operand}"));
		return new(context, target.ToString(), clr.ToString(), mode.ToString(), dynamicFrame, remainder,
			shape.Homes.Where(count => count > 0).Sum() * 4, allocated.Frame.FrameBytes, linked.Bytes.Length,
			initializerBytes, cpu.State.Cycles,
			assembler.Labels.Keys.Count(name => name.Contains("frame-zero-loop", StringComparison.Ordinal)),
			frameHash!, Hash(linked.Bytes), Hash(System.Text.Encoding.UTF8.GetBytes(cilText)), assembly);
	}

	private static object? Invoke(M68kCodeGenerator generator, string name, params object[] arguments) =>
		typeof(M68kCodeGenerator).GetMethod(name, PrivateInstance)!.Invoke(generator, arguments);
	private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

public static class FrameClearRunMetadataFixtures
{
	public struct Block { public uint A, B, C, D; }
	private static uint _sink;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Touch(ref Block block) => _sink = block.A;
	public static void NoArguments() { Block block = default; Touch(ref block); }
	public static void Arguments(uint a, uint b, uint c, uint d, uint e, uint f)
	{
		Block block = default; Touch(ref block); _sink = a + b + c + d + e + f;
	}
}
