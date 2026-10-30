/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class ConstrainedStaticCallAbiTests
{
	[Fact]
	public void StaticInterfaceCallPassesAllArgumentsOnMc68000()
	{
		var expected = ConstrainedStaticCallAbiFixture.Entry();
		Assert.Equal(257u, expected);
		var compiled = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(ConstrainedStaticCallAbiFixture).Assembly.Location,
			EntryPoint = typeof(ConstrainedStaticCallAbiFixture).FullName + "::Entry",
			Cpu = M68kCpuTarget.M68000,
			ClrPolicy = M68kClrPolicy.Always,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = M68kPeepholeOptimizationMode.FixedPoint,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			ManagedAssemblyPaths = [typeof(APTR).Assembly.Location]
		});
		const uint load = 0x10000, stack = 0x80000, sentinel = 0x1000;
		var bus = new TestBus();
		compiled.Code.CopyTo(bus.Memory.AsSpan((int)load));
		foreach (var relocation in compiled.Relocations)
		{
			var address = load + unchecked((uint)relocation.Offset);
			bus.WriteLong(address, bus.ReadLong(address) + load);
		}
		bus.WriteLong(stack, sentinel);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load + compiled.EntryPoint, stack);
		for (var i = 0; i < 200000 && cpu.State.ProgramCounter != sentinel; i++)
		{
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted);
		}
		Assert.Equal(sentinel, cpu.State.ProgramCounter);
		Assert.Equal(expected, cpu.State.D[0]);
		Assert.Equal(stack + 4, cpu.State.A[7]);
	}
}

public static class ConstrainedStaticCallAbiFixture
{
	private interface IWriter
	{
		static abstract uint Write(ref Writer writer, APTR buffer, uint capacity,
			ref uint copied, int code);
	}

	private struct Writer : IWriter
	{
		public uint Value;
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static uint Write(ref Writer writer, APTR buffer, uint capacity,
			ref uint copied, int code)
		{
			copied += writer.Value + buffer.Raw + capacity + unchecked((uint)code);
			return copied;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint Invoke<T>(ref Writer writer, APTR buffer, uint capacity,
		ref uint copied, int code) where T : struct, IWriter =>
		T.Write(ref writer, buffer, capacity, ref copied, code);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Entry()
	{
		var writer = new Writer { Value = 31 };
		uint copied = 17;
		var result = Invoke<Writer>(ref writer, APTR.FromPointer(100), 29, ref copied, 40);
		return result + copied - 177;
	}
}
