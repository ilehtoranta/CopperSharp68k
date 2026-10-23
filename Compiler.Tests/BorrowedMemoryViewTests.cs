using System.Reflection;
using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Compiler.Tests.MultiModule;

namespace CopperSharp.Compiler.Tests;

public sealed class BorrowedMemoryViewTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void BorrowedViewHasDirectCodeSizeCyclesAndNoCallsOrFrame(M68kCpuTarget target, M68kCpuModel model)
    {
        var direct = Compile(target, "Direct");
        var view = Compile(target, "Borrowed");
        Assert.Equal(direct.Code.Length, view.Code.Length);
        Assert.Single(view.Symbols);
        Assert.DoesNotContain("FRAME-SLOT", view.Map);
        foreach (var count in new[] { 0u, 17u, uint.MaxValue })
        {
            var state = Execute(view, model, count);
            var baseline = Execute(direct, model, count);
            Assert.True(state.Cycles <= baseline.Cycles,
                $"View cycles {state.Cycles} exceed direct cycles {baseline.Cycles}.");
            Assert.True(state.Instructions <= baseline.Instructions);
            Assert.Equal(0, state.Calls);
            Assert.Equal(unchecked((count + 1) * 512 + 0x1000u), state.Result);
            Assert.Equal(unchecked(count + 1), state.Count);
            Assert.Equal(64u, state.Bulk);
        }
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void PointerValuedSettersHaveNoArgumentHomeOrCopyOverhead(M68kCpuTarget target, M68kCpuModel model)
    {
        var direct = Compile(target, "DirectPointers");
        var view = Compile(target, "BorrowedPointers");
        Assert.True(view.Code.Length <= direct.Code.Length);
        Assert.Single(view.Symbols);
        Assert.DoesNotContain("FRAME-SLOT", view.Map);
        var baseline = Execute(direct, model, 17);
        var state = Execute(view, model, 17);
        Assert.Equal(baseline.Result, state.Result);
        Assert.Equal(baseline.Count, state.Count);
        Assert.Equal(baseline.Bulk, state.Bulk);
        Assert.True(state.Cycles <= baseline.Cycles);
        Assert.True(state.Instructions <= baseline.Instructions);
        Assert.Equal(0, state.Calls);
        Assert.Equal(0x3002u + 288, state.Order);
        Assert.Equal(0x3002u + 3360, state.Data);
        Assert.Equal(state.Order + state.Data, state.Result);
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void ScalarMemoryAndPromotedFieldValuesRetainBitwiseOperandConstraints(M68kCpuTarget target, M68kCpuModel model)
    {
        Assert.Equal(17u ^ 128 ^ 512 ^ 0x5000 ^ 0x4000,
            Execute(Compile(target, "Checksum"), model, 17).Result);
        Assert.Equal(17u ^ 512,
            Execute(Compile(target, "LocalFieldXor"), model, 17).Result);
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void EscapingViewKeepsStorageAndIndependentSnapshot(M68kCpuTarget target, M68kCpuModel model)
    {
        var result = Compile(target, "Escaping");
        Assert.Equal(145u, Execute(result, model, 17).Result);
        Assert.Contains("FRAME-SLOT", result.Map);
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void BorrowedPlatformObservesReplacementAndMutatesOriginal(M68kCpuTarget target, M68kCpuModel model)
    {
        var result = Execute(Compile(target, "Stateful"), model, 17);
        Assert.Equal(52u, result.Result); // 17 + 34 + one read on the replaced platform
    }

    [Fact]
    public void NoInliningAccessorRetainsItsCall()
    {
        var result = Compile(M68kCpuTarget.M68000, "NoInline");
        Assert.Contains(result.Symbols, s => s.Name.Contains("::get_Pinned", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void ImportedClosedGenericViewResolvesAndInlinesItsConstructorAndAccessors(M68kCpuTarget target, M68kCpuModel model)
    {
        var result = Compile(target, "External");
        Assert.Single(result.Symbols);
        Assert.DoesNotContain("FRAME-SLOT", result.Map);
        var state = Execute(result, model, 17);
        Assert.Equal(18u, state.Result);
        Assert.Equal(18u, state.Count);
        Assert.Equal(0, state.Calls);
    }

    private static M68kCompilationResult Compile(M68kCpuTarget target, string entry) =>
        M68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = Assembly.GetExecutingAssembly().Location,
            ManagedAssemblyPaths = [typeof(ExternalBorrowedView<>).Assembly.Location],
            EntryPoint = $"CopperSharp.Compiler.Tests.BorrowedMemoryViewFixtures::{entry}",
            Cpu = target,
            ExceptionMode = M68kExceptionMode.Yolo,
            MemoryManagement = M68kMemoryManagement.None,
            ClrPolicy = M68kClrPolicy.Always,
            RomSizeOptimizations = new M68kRomSizeOptions(),
            IncludedExportNames = []
        });

    private static (uint Result, uint Count, uint Bulk, long Cycles, int Instructions, int Calls, uint Order, uint Data) Execute(M68kCompilationResult result, M68kCpuModel model, uint count)
    {
        var bus = new TestBus();
        result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
        foreach (var relocation in result.Relocations)
            bus.WriteLong(0x10000u + (uint)relocation.Offset, bus.ReadLong(0x10000u + (uint)relocation.Offset) + 0x10000u);
        bus.WriteLong(0x800, 0x3002);
        bus.WriteLong(0x3002, count);
        bus.WriteLong(0x3006, 128);
        bus.WriteLong(0x300A, 512);
        bus.WriteLong(0x301A, 0x5000);
        bus.WriteLong(0x301E, 0x4000);
        bus.WriteLong(0x80000, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000 + result.EntryPoint, 0x80000);
        int step = 0, calls = 0;
        for (; step < 10000 && cpu.State.ProgramCounter != 0x1000; step++)
        {
            var opcode = bus.ReadWord(cpu.State.ProgramCounter);
            if ((opcode & 0xFF00) == 0x6100 || (opcode & 0xFFC0) == 0x4E80) calls++;
            cpu.ExecuteInstruction();
            Assert.False(cpu.State.Halted);
        }
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        Assert.Equal(0x80004u, cpu.State.A[7]);
        return (cpu.State.D[0], bus.ReadLong(0x3002), bus.ReadLong(0x3016), cpu.State.Cycles, step, calls,
            bus.ReadLong(0x301A), bus.ReadLong(0x301E));
    }
}

public interface IViewMemory
{
    uint Read(APTR address, int offset);
    void Write(APTR address, int offset, uint value);
}

public struct EmptyViewMemory : IViewMemory, IExternalViewMemory
{
    public uint Read(APTR address, int offset) => APTR.ReadUInt32(address, offset);
    public void Write(APTR address, int offset, uint value) => APTR.WriteUInt32(address, offset, value);
}

public struct StatefulViewMemory : IViewMemory
{
    public uint Reads;
    public uint Read(APTR address, int offset) { Reads++; return APTR.ReadUInt32(address, offset); }
    public void Write(APTR address, int offset, uint value) => APTR.WriteUInt32(address, offset, value);
}

public readonly ref struct BorrowedView<T> where T : struct, IViewMemory
{
    private readonly ref T memory;
    private readonly APTR address;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BorrowedView(ref T memory, APTR address) { this.memory = ref memory; this.address = address; }
    public uint Count { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => memory.Read(address, 0); [MethodImpl(MethodImplOptions.AggressiveInlining)] set => memory.Write(address, 0, value); }
    public uint Capacity { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => memory.Read(address, 4); }
    public uint BlockBytes { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => memory.Read(address, 8); }
    public uint Bulk { [MethodImpl(MethodImplOptions.AggressiveInlining)] set => memory.Write(address, 20, value); }
    public uint Order { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => memory.Read(address, 24); }
    public uint Data { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => memory.Read(address, 28); }
    public APTR OrderPointer { [MethodImpl(MethodImplOptions.AggressiveInlining)] set => memory.Write(address, 24, value.Raw); }
    public APTR DataPointer { [MethodImpl(MethodImplOptions.AggressiveInlining)] set => memory.Write(address, 28, value.Raw); }
}

public readonly struct NativeView
{
    private readonly APTR address;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NativeView(APTR address) { this.address = address; }
    public uint Count { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => APTR.ReadUInt32(address, 0); [MethodImpl(MethodImplOptions.AggressiveInlining)] set => APTR.WriteUInt32(address, 0, value); }
    public uint Capacity { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => APTR.ReadUInt32(address, 4); }
    public uint BlockBytes { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => APTR.ReadUInt32(address, 8); }
    public uint Bulk { [MethodImpl(MethodImplOptions.AggressiveInlining)] set => APTR.WriteUInt32(address, 20, value); }
    public uint Order { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => APTR.ReadUInt32(address, 24); }
    public uint Data { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => APTR.ReadUInt32(address, 28); }
    public uint Pinned { [MethodImpl(MethodImplOptions.NoInlining)] get => APTR.ReadUInt32(address, 0); }
}

public readonly struct ScalarPair
{
    private readonly uint first;
    private readonly uint second;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ScalarPair(uint first, uint second) { this.first = first; this.second = second; }
    public uint First { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => first; }
    public uint Second { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => second; }
}

public static class BorrowedMemoryViewFixtures
{
    public static uint Checksum()
    {
        var address = APTR.FromPointer(APTR.ReadUInt32(APTR.FromPointer(0x800), 0));
        uint sum = 0;
        for (int offset = 0; offset < 32; offset += 4)
            sum ^= APTR.ReadUInt32(address, offset);
        return sum;
    }
    public static uint LocalFieldXor()
    {
        var address = APTR.FromPointer(APTR.ReadUInt32(APTR.FromPointer(0x800), 0));
        var pair = new ScalarPair(APTR.ReadUInt32(address, 0), APTR.ReadUInt32(address, 8));
        return pair.First ^ pair.Second;
    }
    public static uint External()
    {
        var memory = new EmptyViewMemory();
        var view = new ExternalBorrowedView<EmptyViewMemory>(ref memory,
            APTR.FromPointer(APTR.ReadUInt32(APTR.FromPointer(0x800), 0)));
        view.Count++;
        return view.Count;
    }
    public static uint Direct()
    {
        var address = APTR.FromPointer(APTR.ReadUInt32(APTR.FromPointer(0x800), 0));
        APTR.WriteUInt32(address, 0, APTR.ReadUInt32(address, 0) + 1);
        APTR.WriteUInt32(address, 20, APTR.ReadUInt32(address, 4) >> 1);
        return APTR.ReadUInt32(address, 0) * APTR.ReadUInt32(address, 8) + APTR.ReadUInt32(address, 24) - APTR.ReadUInt32(address, 28);
    }
    public static uint Native()
    {
        var view = new NativeView(APTR.FromPointer(APTR.ReadUInt32(APTR.FromPointer(0x800), 0)));
        view.Count++;
        view.Bulk = view.Capacity >> 1;
        return view.Count * view.BlockBytes + view.Order - view.Data;
    }
    public static uint Borrowed()
    {
        var memory = new EmptyViewMemory();
        var view = new BorrowedView<EmptyViewMemory>(ref memory, APTR.FromPointer(APTR.ReadUInt32(APTR.FromPointer(0x800), 0)));
        view.Count++;
        view.Bulk = view.Capacity >> 1;
        return view.Count * view.BlockBytes + view.Order - view.Data;
    }
    public static uint DirectPointers()
    {
        var address = APTR.FromPointer(APTR.ReadUInt32(APTR.FromPointer(0x800), 0));
        APTR.WriteUInt32(address, 24, address.Raw + 288);
        APTR.WriteUInt32(address, 28, address.Raw + 3360);
        return APTR.ReadUInt32(address, 24) + APTR.ReadUInt32(address, 28);
    }
    public static uint BorrowedPointers()
    {
        var memory = new EmptyViewMemory();
        var address = APTR.FromPointer(APTR.ReadUInt32(APTR.FromPointer(0x800), 0));
        var view = new BorrowedView<EmptyViewMemory>(ref memory, address);
        view.OrderPointer = APTR.FromPointer(address.Raw + 288);
        view.DataPointer = APTR.FromPointer(address.Raw + 3360);
        return view.Order + view.Data;
    }
    public static uint Stateful()
    {
        var memory = new StatefulViewMemory { Reads = 1 };
        var view = new BorrowedView<StatefulViewMemory>(ref memory, APTR.FromPointer(APTR.ReadUInt32(APTR.FromPointer(0x800), 0)));
        memory = new StatefulViewMemory { Reads = 34 };
        var count = view.Count;
        return count + memory.Reads;
    }
    public static uint NoInline() => new NativeView(APTR.FromPointer(APTR.ReadUInt32(APTR.FromPointer(0x800), 0))).Pinned;
    public static uint Escaping()
    {
        var view = new NativeView(APTR.FromPointer(APTR.ReadUInt32(APTR.FromPointer(0x800), 0)));
        var snapshot = view;
        Retarget(ref view);
        return snapshot.Count + view.Count;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Retarget(ref NativeView view) => view = new NativeView(APTR.FromPointer(0x3006));
}
