using Copper68k;
using Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeExportAdapterTests
{
    [Theory]
    [InlineData(M68kRuntimeProfile.Freestanding, false)]
    [InlineData(M68kRuntimeProfile.Freestanding, true)]
    [InlineData(M68kRuntimeProfile.Resident, false)]
    [InlineData(M68kRuntimeProfile.Resident, true)]
    public void RegisterCyclesAndOverflowPreserveNativeAbi(M68kRuntimeProfile profile, bool exportIsEntry)
    {
        string exportName = exportIsEntry ? "native.test.entry" : "native.test.mix";
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(NativeExportFixtures).Assembly.Location,
            EntryPoint = typeof(NativeExportFixtures).FullName + "::" + (exportIsEntry ? "EntryMix" : "Empty"),
            ManagedAssemblyPaths = [typeof(APTR).Assembly.Location],
            IncludedExportNames = [exportName],
            RuntimeProfile = profile, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, OutputFormat = M68kOutputFormat.Hunk,
            Hunk = new() { IncludeSymbols = false }
        });
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        foreach (uint seed in new uint[] { 0, 1, 0x80000000, 0xFFFFFFFF })
        foreach (uint stack in new uint[] { 0x80000, 0x80002 })
        {
            var bus = new TestBus();
            var code = ResidentCodeSizeExecutionTests.LoadImage(result.Image, 0x10000);
            code.CopyTo(bus.Memory, 0x10000); bus.WriteLong(stack, 0x1000);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x10000 + result.Symbols.Single(s => s.Name == exportName).Address, stack);
            cpu.State.StatusRegister = 0x2700;
            for (int i = 0; i < 8; i++) cpu.State.D[i] = unchecked(seed + (uint)(i * 17));
            for (int i = 0; i < 7; i++) cpu.State.A[i] = unchecked(seed ^ (uint)(0x1234 + i * 71));
            var d = cpu.State.D.ToArray(); var a = cpu.State.A.ToArray();
            uint[] inputs = [a[0], a[1], d[1], d[0], d[2], a[6]];
            for (int i = 0; i < inputs.Length; i++) bus.WriteLong((uint)(0x3000 + 4 * i), inputs[i]);
            uint expected = NativeExportFixtures.Mix(a[0], a[1], d[1], d[0], d[2], a[6]);
            for (int i = 0; i < 1000 && cpu.State.ProgramCounter != 0x1000; i++) cpu.ExecuteInstruction();
            Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(stack + 4, cpu.State.A[7]);
            Assert.Equal(expected, cpu.State.D[0]);
            Assert.Equal(d.Skip(2), cpu.State.D.Skip(2)); Assert.Equal(a.Skip(2).Take(5), cpu.State.A.Skip(2).Take(5));
            Assert.Equal(code, bus.Memory.AsSpan(0x10000, code.Length).ToArray());
        }
    }
}

public static class NativeExportFixtures
{
    public static void Empty() { }
    [M68kExport("native.test.entry")]
    [return: M68kRegister(M68kRegister.D0)]
    public static uint EntryMix() => Mix(APTR.ReadUInt32(0x3000u, 0), APTR.ReadUInt32(0x3000u, 4),
        APTR.ReadUInt32(0x3000u, 8), APTR.ReadUInt32(0x3000u, 12), APTR.ReadUInt32(0x3000u, 16), APTR.ReadUInt32(0x3000u, 20));
    [M68kExport("native.test.mix")]
    [return: M68kRegister(M68kRegister.D0)]
    public static uint Mix(
        [M68kRegister(M68kRegister.A0)] uint p0,
        [M68kRegister(M68kRegister.A1)] uint p1,
        [M68kRegister(M68kRegister.D1)] uint p2,
        [M68kRegister(M68kRegister.D0)] uint p3,
        [M68kRegister(M68kRegister.D2)] uint p4,
        [M68kRegister(M68kRegister.A6)] uint p5)
        => unchecked(p0 + p1 * 3 + p2 * 5 + p3 * 7 + p4 * 11 + p5 * 13);
}
