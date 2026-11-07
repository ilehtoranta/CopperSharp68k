/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class AllocatedAddressSourceArithmeticTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        {
            yield return [model, false, true, 0xFFFF8000u, 0xAABB8001u, 0xAABB0001u, 0x13];
            yield return [model, false, false, 1u, 0x7FFFFFFFu, 0x80000000u, 0x0A];
            yield return [model, true, true, 1u, 0xAABB0000u, 0xAABBFFFFu, 0x19];
            yield return [model, true, false, 1u, 0x80000000u, 0x7FFFFFFFu, 0x02];
            yield return [model, false, true, 1u, 0xAABBFFFFu, 0xAABB0000u, 0x15];
            yield return [model, true, false, uint.MaxValue, uint.MaxValue, 0u, 0x04];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void WordAndLongArithmeticReadsAddressRegisterValuesAndPreservesOtherState(
        M68kCpuModel model, bool subtract, bool word, uint source, uint destination, uint expected, int flags)
    {
        var (emit, assembler) = Emitter();
        emit(subtract ? M68kMachineOperation.Subtract : M68kMachineOperation.Add,
            M68kRegister.A3, M68kRegister.D4, word ? M68kMachineValueWidth.Word : M68kMachineValueWidth.Long);
        var linked = assembler.Link(0x10000, new Dictionary<string, uint>());
        Assert.Equal(2, linked.Bytes.Length);
        var bus = new TestBus(); linked.Bytes.CopyTo(bus.Memory.AsSpan(0x10000));
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x80000); cpu.State.StatusRegister = 0x271F;
        for (var i = 0; i < 8; i++) cpu.State.D[i] = 0x12345600u + (uint)i;
        for (var i = 0; i < 7; i++) cpu.State.A[i] = 0x76543200u + (uint)i;
        cpu.State.A[3] = source; cpu.State.D[4] = destination;
        var data = cpu.State.D.ToArray(); var addresses = cpu.State.A.ToArray();
        cpu.ExecuteInstruction();
        Assert.False(cpu.State.Halted);
        Assert.Equal(0x10002u, cpu.State.ProgramCounter);
        Assert.Equal(expected, cpu.State.D[4]);
        Assert.Equal(flags, cpu.State.StatusRegister & 31);
        Assert.Equal(0x2700, cpu.State.StatusRegister & 0xFFE0);
        Assert.Equal(addresses, cpu.State.A.ToArray());
        for (var i = 0; i < 8; i++) if (i != 4) Assert.Equal(data[i], cpu.State.D[i]);
    }

    [Theory]
    [InlineData(nameof(M68kMachineOperation.Add), nameof(M68kMachineValueWidth.Byte))]
    [InlineData(nameof(M68kMachineOperation.Subtract), nameof(M68kMachineValueWidth.Byte))]
    [InlineData(nameof(M68kMachineOperation.And), nameof(M68kMachineValueWidth.Word))]
    [InlineData(nameof(M68kMachineOperation.Or), nameof(M68kMachineValueWidth.Long))]
    [InlineData(nameof(M68kMachineOperation.Xor), nameof(M68kMachineValueWidth.Long))]
    public void OperationsWithoutAnAddressRegisterSourceEncodingRemainRejected(
        string operation, string width)
    {
        var (emit, _) = Emitter();
        Assert.Throws<InvalidOperationException>(() => emit(Enum.Parse<M68kMachineOperation>(operation),
            M68kRegister.A3, M68kRegister.D4, Enum.Parse<M68kMachineValueWidth>(width)));
    }

    private static (Action<M68kMachineOperation, M68kRegister, M68kRegister, M68kMachineValueWidth>, M68kAssembler) Emitter()
    {
        var generator = new M68kCodeGenerator(null!, new M68kCompilationRequest
        {
            AssemblyPath = typeof(AllocatedAddressSourceArithmeticTests).Assembly.Location,
            EntryPoint = "unused", RuntimeProfile = M68kRuntimeProfile.Rom,
            OutputFormat = M68kOutputFormat.KickstartRom, MemoryManagement = M68kMemoryManagement.None,
        }, []);
        const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
        var emit = typeof(M68kCodeGenerator).GetMethod("EmitAllocatedBinaryInPlace", access)!
            .CreateDelegate<Action<M68kMachineOperation, M68kRegister, M68kRegister, M68kMachineValueWidth>>(generator);
        var assembler = (M68kAssembler)typeof(M68kCodeGenerator).GetField("_assembler", access)!.GetValue(generator)!;
        return (emit, assembler);
    }
}
