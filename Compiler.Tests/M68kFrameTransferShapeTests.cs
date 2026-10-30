/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kFrameTransferShapeTests
{
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly FieldInfo BufferField = typeof(M68kAssembler).GetField("_buffer", PrivateInstance)!;
	private static readonly FieldInfo AddressIndex = typeof(M68kPeepholeOptimizer)
		.GetField("_addressEntryBoundaryIndex", PrivateInstance)!;
	private static readonly FieldInfo RoundAddresses = typeof(M68kPeepholeOptimizer)
		.GetField("_addressFixupOffsets", PrivateInstance)!;

	public static IEnumerable<object[]> IncompatibleShapes()
	{
		yield return ["nop", new ushort[] { 0x4E71 }];
		yield return ["moveq", new ushort[] { 0x7001 }];
		yield return ["data-register move", new ushort[] { 0x2003 }];
		yield return ["different stored register", new ushort[] { 0x2F43, 12 }];
		yield return ["different store displacement", new ushort[] { 0x2F42, 16 }];
		yield return ["word store", new ushort[] { 0x3F42, 12 }];
		yield return ["word reload", new ushort[] { 0x306F, 12 }];
		yield return ["different reload base", new ushort[] { 0x206E, 12 }];
		yield return ["different reload displacement", new ushort[] { 0x206F, 16 }];
		yield return ["different addressing mode", new ushort[] { 0x2057 }];
		yield return ["different copied data register", new ushort[] { 0x2043 }];
		yield return ["different copied address register", new ushort[] { 0x204B }];
		yield return ["unrelated indirect reload", new ushort[] { 0x2050 }];
		yield return ["immediate address load", new ushort[] { 0x207C, 0, 0x8000 }];
		yield return ["reload into stack pointer", new ushort[] { 0x2E6F, 12 }];
		yield return ["copy into stack pointer", new ushort[] { 0x2E42 }];
	}

	[Theory]
	[MemberData(nameof(IncompatibleShapes))]
	public void IncompatibleShapesDoNotBuildAddressBoundaryIndex(string scenario, ushort[] next)
	{
		var (assembler, buffer) = Create([0x2F42, 12], next);
		var bytes = buffer.Bytes.ToArray();
		var labels = buffer.Labels.ToArray();
		var addresses = buffer.Addresses.ToArray();
		var dataStart = buffer.DataStartOffset;

		var result = Cleanup(assembler, buffer);

		Assert.False(result.Changed, scenario);
		Assert.Null(result.BoundaryIndex);
		Assert.Equal(bytes, buffer.Bytes);
		Assert.Equal(labels, buffer.Labels.ToArray());
		Assert.Equal(addresses, buffer.Addresses);
		Assert.Equal(dataStart, buffer.DataStartOffset);
	}

	public static IEnumerable<object[]> EligibleShapes()
	{
		yield return ["duplicate displaced store through A7", new ushort[] { 0x2F42, 12 }, new ushort[] { 0x2F42, 12 }, Array.Empty<ushort>(), new ushort[] { 0x2F42, 12, 0x4E75 }];
		yield return ["duplicate indirect store through A7", new ushort[] { 0x2E82 }, new ushort[] { 0x2E82 }, Array.Empty<ushort>(), new ushort[] { 0x2E82, 0x4E75 }];
		yield return ["displaced frame reload", new ushort[] { 0x2F42, 12 }, new ushort[] { 0x206F, 12 }, Array.Empty<ushort>(), new ushort[] { 0x2F42, 12, 0x2042, 0x4E75 }];
		yield return ["indirect frame reload", new ushort[] { 0x2E82 }, new ushort[] { 0x2057 }, Array.Empty<ushort>(), new ushort[] { 0x2E82, 0x2042, 0x4E75 }];
		yield return ["displaced register-copy chain", new ushort[] { 0x2F42, 12 }, new ushort[] { 0x2042 }, new ushort[] { 0x2F48, 12, 0x2F48, 12 }, new ushort[] { 0x2F42, 12, 0x2042, 0x4E75 }];
		yield return ["indirect register-copy chain", new ushort[] { 0x2E82 }, new ushort[] { 0x2042 }, new ushort[] { 0x2E88, 0x2E88 }, new ushort[] { 0x2E82, 0x2042, 0x4E75 }];
		yield return ["address-register-copy chain", new ushort[] { 0x2F4A, 12 }, new ushort[] { 0x204A }, new ushort[] { 0x2F48, 12 }, new ushort[] { 0x2F4A, 12, 0x204A, 0x4E75 }];
	}

	[Theory]
	[MemberData(nameof(EligibleShapes))]
	public void EligibleShapesKeepTheSameRewritesAndCheckBoundaries(
		string scenario, ushort[] store, ushort[] next, ushort[] trailing, ushort[] expected)
	{
		var (assembler, buffer) = Create(store, next, trailing);
		var before = buffer.Bytes.Take(buffer.DataStartOffset!.Value).ToArray();

		var result = Cleanup(assembler, buffer);

		Assert.True(result.Changed, scenario);
		Assert.NotNull(result.BoundaryIndex);
		Assert.Equal(expected.SelectMany(word => new[] { (byte)(word >> 8), (byte)word }),
			buffer.Bytes.Take(buffer.DataStartOffset!.Value));
		Assert.Equal(buffer.DataStartOffset.Value, Assert.Single(buffer.Addresses).Offset);
		Assert.Equal(0, buffer.Labels["outside:target"]);
		var after = buffer.Bytes.Take(buffer.DataStartOffset.Value).ToArray();
		foreach (var value in new uint[] { 0, 1, 0x7FFF_FFFF, 0x8000_0000, 0xFFFF_FFFF })
		foreach (var flags in new ushort[] { 0, 0x1F })
			Assert.Equal(ExecuteTransfer(before, value, flags), ExecuteTransfer(after, value, flags));
	}

	[Fact]
	public void CompatibleRegisterCopyWithoutFollowingStoresRemainsUnchanged()
	{
		var (assembler, buffer) = Create([0x2F42, 12], [0x2042]);
		var bytes = buffer.Bytes.ToArray();

		var result = Cleanup(assembler, buffer);

		Assert.False(result.Changed);
		Assert.NotNull(result.BoundaryIndex);
		Assert.Equal(bytes, buffer.Bytes);
	}

	[Theory]
	[InlineData("named entry")]
	[InlineData("source effect")]
	[InlineData("reload effect")]
	[InlineData("positive addend span")]
	[InlineData("negative addend span")]
	[InlineData("global alias entry")]
	[InlineData("unknown base")]
	public void EligibleShapesRetainEntrySpanEffectAndAliasGuards(string guard)
	{
		var (assembler, buffer) = guard == "unknown base"
			? Create([0x2542, 12], [0x206A, 12])
			: Create([0x2F42, 12], [0x206F, 12]);
		switch (guard)
		{
			case "named entry": buffer.Labels.Add("generated:unwind-entry", 4); break;
			case "source effect":
			case "reload effect":
				var offset = guard == "source effect" ? 0 : 4;
				var instruction = assembler.GetExecutableInstructionStream().Single(item => item.Offset == offset);
				assembler.SetInstructionEffects(offset, M68kInstructionDataflow.GetEffects(instruction) with
					{ IsBarrier = true, CanRemoveWhenOutputsDead = false });
				break;
			case "positive addend span": buffer.Addresses[0] = buffer.Addresses[0] with { Addend = 8 }; break;
			case "negative addend span":
				buffer.Labels["outside:target"] = 12;
				buffer.Addresses[0] = buffer.Addresses[0] with { Addend = -12 };
				break;
			case "global alias entry":
				buffer.Labels["outside:target"] = 4;
				assembler.SetAnalysisScope(null, "missing:end", []);
				break;
		}
		var bytes = buffer.Bytes.ToArray();
		var addresses = buffer.Addresses.ToArray();

		var result = Cleanup(assembler, buffer);

		Assert.False(result.Changed, guard);
		Assert.NotNull(result.BoundaryIndex);
		Assert.Equal(bytes, buffer.Bytes);
		Assert.Equal(addresses, buffer.Addresses);
	}

	private static (M68kAssembler Assembler, M68kAssemblyBuffer Buffer) Create(
		ushort[] store, ushort[] next, ushort[]? trailing = null)
	{
		var assembler = new M68kAssembler();
		foreach (var word in store.Concat(next).Concat(trailing ?? []).Append((ushort)0x4E75)) assembler.EmitWord(word);
		assembler.MarkDataStart();
		var buffer = (M68kAssemblyBuffer)BufferField.GetValue(assembler)!;
		buffer.Labels.Add("outside:target", 0);
		assembler.EmitAddress("outside:target");
		return (assembler, buffer);
	}

	private static (bool Changed, object? BoundaryIndex) Cleanup(M68kAssembler assembler, M68kAssemblyBuffer buffer)
	{
		var optimizer = new M68kPeepholeOptimizer(assembler, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			RoundAddresses.SetValue(optimizer, buffer.Addresses.Select(address => address.Offset).ToHashSet());
			var dataflow = M68kInstructionDataflow.Analyze(assembler);
			var cleanup = typeof(M68kPeepholeOptimizer).GetMethod("TryCleanupFrameMemoryTransfers", PrivateInstance)!
				.CreateDelegate<Func<M68kInstructionDataflow, bool>>(optimizer);
			return (cleanup(dataflow), AddressIndex.GetValue(optimizer));
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	private static (uint A0, uint D2, uint A2, uint Stack, uint AtStack, uint AtDisplacement, ushort Flags)
		ExecuteTransfer(byte[] code, uint value, ushort flags)
	{
		const uint load = 0x10000, stack = 0x80000;
		var bus = new TestBus();
		code.CopyTo(bus.Memory.AsSpan((int)load));
		bus.WriteLong(stack, 0x12345678);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load, stack);
		cpu.State.D[2] = value;
		cpu.State.A[2] = ~value;
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		// Stop before RTS: an indirect store may legitimately overwrite (SP).
		var end = load + (uint)code.Length - 2;
		for (var step = 0; step < 8 && cpu.State.ProgramCounter != end; step++) cpu.ExecuteInstruction();
		Assert.Equal(end, cpu.State.ProgramCounter);
		return (cpu.State.A[0], cpu.State.D[2], cpu.State.A[2], cpu.State.A[7],
			bus.ReadLong(stack), bus.ReadLong(stack + 12), (ushort)(cpu.State.StatusRegister & 0x1F));
	}
}
