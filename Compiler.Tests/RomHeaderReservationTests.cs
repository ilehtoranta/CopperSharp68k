using System.Buffers.Binary;
using Copper68k;
using CopperSharp.Compiler.Output;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class RomHeaderReservationTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(32)]
    public void ReservedDescriptorWordsDoNotOverwriteLinkedCode(int headerBytes)
    {
        const uint origin = 0x100000;
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(IntrinsicPhiFixture).Assembly.Location,
            EntryPoint = typeof(IntrinsicPhiFixture).FullName + "::Empty",
            IncludedExportNames = ["intrinsic.phi.load"], Cpu = M68kCpuTarget.M68000,
            OutputFormat = M68kOutputFormat.KickstartRom, RuntimeProfile = M68kRuntimeProfile.Rom,
            MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
            Rom = new() { Size = 262144, BaseAddress = origin, HeaderBytes = headerBytes }
        });
        Assert.All(result.Symbols, symbol => Assert.True(symbol.Address >= origin + headerBytes));
        Assert.Equal(result.Code, result.Image.AsSpan(headerBytes, result.Code.Length).ToArray());
        Assert.All(result.Image.AsSpan(8, headerBytes - 8).ToArray(), value => Assert.Equal(0xFF, value));
        Assert.Equal(uint.MaxValue, KickstartRomWriter.ComputeEndAroundCarrySum(result.Image));
        if (headerBytes > 8) BinaryPrimitives.WriteUInt32BigEndian(result.Image.AsSpan(8), 0xDEADBEEF);
        var bus = new TestBus(0x200000); result.Image.CopyTo(bus.Memory, (int)origin);
        bus.WriteLong(0x30000, 0x30100); bus.WriteLong(0x30100, 0x30200); bus.WriteLong(0x80000, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
        cpu.Reset(result.Symbols.Single(s => s.Name == "intrinsic.phi.load").Address, 0x80000);
        cpu.State.A[0] = 0x30000;
        for (var n = 0; n < 1000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(0x30100u, cpu.State.D[0]);
        Assert.Equal(0x30200u, bus.ReadLong(0x30000)); Assert.Equal(0x30000u, bus.ReadLong(0x30204));
        var capacity = result.Image.Length - 20 - headerBytes;
        Assert.Throws<M68kCompilationException>(() => KickstartRomWriter.Write(new byte[capacity + 1], origin, new() {
            Size = result.Image.Length, BaseAddress = origin, HeaderBytes = headerBytes
        }));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(262128)]
    public void InvalidReservationIsRejected(int headerBytes) =>
        Assert.Throws<M68kCompilationException>(() => KickstartRomWriter.GetBaseAddress(new() { HeaderBytes = headerBytes, Size = 262144 }));
}
