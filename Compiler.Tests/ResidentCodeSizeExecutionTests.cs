using System.Buffers.Binary;
using Copper68k;
using CopperSharp.Targets.Amiga;
using Amiga;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public sealed class ResidentCodeSizeExecutionTests
{
    private static readonly string[] Passes = ["ElideUnusedRegisterArguments", "ReuseIncomingArgumentHomes",
        "ForwardReadOnlyAggregateLocals", "InlineSingleUseMethods", "ClusterInternalCalls",
        "ShareIdenticalMethods", "ShareReturnSequences", "Combined"];

    public static IEnumerable<object[]> Cases()
    {
        foreach (var pass in Passes)
        foreach (var scenario in new[] { "Unused/Scalar", "Unused/Effects", "Unused/Forward",
            "Unused/Aggregate", "Unused/UsedPointer", "Incoming/Read", "Incoming/Mutate",
            "Incoming/Twice", "Incoming/HiddenReturn", "ReadOnly/Safe",
            "ReadOnly/ProducerAlias", "ReadOnly/Writable", "Local/Entry", "Local/IdentityEntry" })
        foreach (var peephole in new[] { M68kPeepholeOptimizationMode.Disabled,
            M68kPeepholeOptimizationMode.FixedPoint })
            yield return [pass, scenario, peephole];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ResidentHunksPreserveAliasesRegistersStackAndRelocation(
        string pass, string scenario, M68kPeepholeOptimizationMode peephole)
    {
        var parts = scenario.Split('/');
        var type = parts[0] switch {
            "Unused" => typeof(UnusedRegisterArgumentFixtures),
            "Incoming" => typeof(IncomingArgumentHomeFixtures),
            "ReadOnly" => typeof(ReadOnlyAggregateReturnForwardingFixtures),
            _ => typeof(ResidentSizeFixtures)
        };
        var entry = type.FullName + "::" + (parts[0] == "Local" ? parts[1] : "Entry" + parts[1]);
        var request = new M68kCompilationRequest {
            AssemblyPath = type.Assembly.Location, EntryPoint = entry,
            Cpu = M68kCpuTarget.M68000, RuntimeProfile = M68kRuntimeProfile.Resident,
            OutputFormat = M68kOutputFormat.Hunk, ExceptionMode = M68kExceptionMode.Yolo,
            MemoryManagement = M68kMemoryManagement.None, PeepholeOptimization = peephole,
            IncludedExportNames = parts[1] == "IdentityEntry" ? ["resident.size.first", "resident.size.second"] : [],
            Hunk = new() { IncludeSymbols = false }
        };
        var before = AmigaM68kCompiler.Compile(request);
        var after = AmigaM68kCompiler.Compile(request with { CodeSizeOptimizations = Policy(pass) });
        Assert.Contains("CODE-SIZE mode=general", after.Map);
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
        foreach (var load in new uint[] { 0x10000, 0x20000 })
        foreach (var input in new uint[] { 0, 41, 0xffffffff })
        foreach (var residue in new uint[] { 0, 2 })
        {
            var a = Execute(before, model, load, input, residue, parts[0] == "Local");
            var b = Execute(after, model, load, input, residue, parts[0] == "Local");
            Assert.Equal(a.Value, b.Value);
            Assert.Equal(a.Guest, b.Guest);
            Assert.Equal(a.Registers, b.Registers);
            if (parts[0] == "Local") Assert.Equal(42u, b.Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResidentDynamicStackRemainsRejected(bool enabled)
    {
        var error = Assert.Throws<M68kCompilationException>(() => AmigaM68kCompiler.Compile(
            new M68kCompilationRequest {
                AssemblyPath = typeof(IncomingArgumentHomeFixtures).Assembly.Location,
                EntryPoint = typeof(IncomingArgumentHomeFixtures).FullName + "::EntryDynamic",
                RuntimeProfile = M68kRuntimeProfile.Resident, OutputFormat = M68kOutputFormat.Hunk,
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                IncludedExportNames = [], CodeSizeOptimizations = enabled ? new() : null
            }));
        Assert.Contains("does not support localloc", error.Message);
    }

    private static M68kCodeSizeOptions Policy(string pass) => new() {
        ElideUnusedRegisterArguments = pass is "Combined" or "ElideUnusedRegisterArguments",
        ReuseIncomingArgumentHomes = pass is "Combined" or "ReuseIncomingArgumentHomes",
        ForwardReadOnlyAggregateLocals = pass is "Combined" or "ForwardReadOnlyAggregateLocals",
        InlineSingleUseMethods = pass is "Combined" or "InlineSingleUseMethods",
        ClusterInternalCalls = pass is "Combined" or "ClusterInternalCalls",
        ShareIdenticalMethods = pass is "Combined" or "ShareIdenticalMethods",
        ShareReturnSequences = pass is "Combined" or "ShareReturnSequences"
    };

    [Fact]
    public void PoliciesAreExclusiveAndLegacyResidentBehaviorIsUnchanged()
    {
        var request = new M68kCompilationRequest {
            AssemblyPath = typeof(ResidentSizeFixtures).Assembly.Location,
            EntryPoint = typeof(ResidentSizeFixtures).FullName + "::Entry",
            Cpu = M68kCpuTarget.M68000, RuntimeProfile = M68kRuntimeProfile.Resident,
            OutputFormat = M68kOutputFormat.Hunk, ExceptionMode = M68kExceptionMode.Yolo,
            MemoryManagement = M68kMemoryManagement.None, IncludedExportNames = []
        };
        Assert.Equal(AmigaM68kCompiler.Compile(request).Image,
            AmigaM68kCompiler.Compile(request with { RomSizeOptimizations = new() }).Image);
        Assert.Throws<M68kCompilationException>(() => AmigaM68kCompiler.Compile(request with {
            RomSizeOptimizations = new(), CodeSizeOptimizations = new()
        }));
        foreach (var cpu in new[] { M68kCpuTarget.M68020, M68kCpuTarget.M68040 })
            Assert.Equal(AmigaM68kCompiler.Compile(request with { Cpu = cpu }).Image,
                AmigaM68kCompiler.Compile(request with { Cpu = cpu, CodeSizeOptimizations = new() }).Image);
    }

    // Exercise the serialized HUNK relocations, rather than relocating the
    // compiler's raw code buffer. These fixtures intentionally have one hunk.
    private static byte[] Load(M68kCompilationResult result, uint address)
        => LoadImage(result.Image, address);

    internal static byte[] LoadImage(byte[] image, uint address)
    {
        var offset = 0;
        uint Long() { var value = BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(offset, 4)); offset += 4; return value; }
        Assert.Equal(0x3f3u, Long()); Assert.Equal(0u, Long());
        Assert.Equal(1u, Long()); Assert.Equal(0u, Long()); Assert.Equal(0u, Long());
        var bytes = checked((int)(Long() * 4));
        Assert.Equal(0x3e9u, Long()); var length = checked((int)(Long() * 4));
        var code = new byte[bytes]; image.AsSpan(offset, length).CopyTo(code); offset += length;
        var record = Long();
        if (record == 0x3ec)
        {
            for (var count = Long(); count != 0; count = Long())
            {
                Assert.Equal(0u, Long());
                for (var i = 0u; i < count; i++) {
                    var field = checked((int)Long()); Assert.Equal(0, field % 2);
                    Assert.InRange(field, 0, code.Length - 4);
                    var target = BinaryPrimitives.ReadUInt32BigEndian(code.AsSpan(field, 4));
                    Assert.InRange(target, 0u, (uint)code.Length - 1);
                    BinaryPrimitives.WriteUInt32BigEndian(code.AsSpan(field, 4), target + address);
                }
            }
            record = Long();
        }
        Assert.Equal(0x3f2u, record); Assert.Equal(image.Length, offset);
        return code;
    }

    private sealed record Observation(uint Value, byte[] Guest, uint[] Registers);
    private static Observation Execute(M68kCompilationResult result, M68kCpuModel model,
        uint load, uint input, uint residue, bool recursiveRoot)
    {
        var bus = new TestBus(0x100000); var code = Load(result, load);
        code.CopyTo(bus.Memory.AsSpan((int)load));
        bus.WriteLong(0x40000, input); bus.WriteLong(0x40004, input & 1); bus.WriteLong(0x40008, 0);
        var stack = 0x80000u + residue; bus.WriteLong(stack, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(load + result.EntryPoint, stack);
        for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xdada0000u + (uint)i;
        for (var i = 2; i < 7; i++) cpu.State.A[i] = 0xa0a00000u + (uint)i;
        cpu.State.StatusRegister = 0x2700;
        for (var step = 0; step < 30000 && cpu.State.ProgramCounter != 0x1000; step++) {
            cpu.ExecuteInstruction(); Assert.False(cpu.State.Halted);
        }
        Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(stack + 4, cpu.State.A[7]);
        // This private-ABI recursive root clobbers data registers even with the policy disabled.
        // Compare its exact register observation against that control; all other
        // fixture entries and reserved registers retain their normal assertions.
        for (var i = recursiveRoot ? 8 : 2; i < 8; i++) Assert.Equal(0xdada0000u + (uint)i, cpu.State.D[i]);
        for (var i = 2; i < 7; i++) Assert.Equal(0xa0a00000u + (uint)i, cpu.State.A[i]);
        Assert.Equal(code, bus.Memory.AsSpan((int)load, code.Length).ToArray());
        return new(cpu.State.D[0], bus.Memory.AsSpan(0x40000, 24).ToArray(),
            cpu.State.D.Skip(2).Concat(cpu.State.A.Skip(2).Take(5)).ToArray());
    }
}

public static unsafe class ResidentSizeFixtures
{
    public static uint IdentityEntry() {
        var first = APTR.ToUInt32(APTR.ExportAddress("resident.size.first"));
        var second = APTR.ToUInt32(APTR.ExportAddress("resident.size.second"));
        var input = *(uint*)0x40000;
        return first != second && ExportFirst(input) == ExportSecond(input) ? 42u : 0u;
    }
    [M68kExport("resident.size.first")]
    [return: M68kRegister(M68kRegister.D0)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static uint ExportFirst([M68kRegister(M68kRegister.D0)] uint input) =>
        input == 0 ? 7u : input ^ 0x12345678;
    [M68kExport("resident.size.second")]
    [return: M68kRegister(M68kRegister.D0)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static uint ExportSecond([M68kRegister(M68kRegister.D0)] uint input) =>
        input == 0 ? 7u : input ^ 0x12345678;
    public static uint Entry() {
        var seed = *(uint*)0x40000;
        var a = First(seed); var b = Second(seed);
        return a == b && Once(seed) == seed + 7 && Recurse(3) == 6 ? 42u : 0u;
    }
    private static uint First(uint input) => input == 0 ? 7u : input ^ 0x12345678;
    private static uint Second(uint input) => input == 0 ? 7u : input ^ 0x12345678;
    private static uint Once(uint input) => input + 7;
    private static uint Recurse(uint input) => input == 0 ? 0 : input + Recurse(input - 1);
}
