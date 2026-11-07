using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Cilica;
using Cilica.AsmIO;
using Copper68k;
using CopperSharp.Compiler;
using CopperSharp.Compiler.Tests;
using CopperSharp.Targets.Amiga;
using CopperSharp.Tools;

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
void Save(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, jsonOptions));
if (args.Length < 3) throw new ArgumentException("rewrite <input.dll> <output.dll> <default|scalar|portable|restricted> | inspect <input.dll> <output.txt> | bench <fixture.dll> <results-directory> [repetitions] [variants-csv] | example <input.dll> <results-directory> <entry>");
if (args[0] == "inspect")
{
    var context = new AssemblyLoadContext("IL inspection", isCollectible: true);
    var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
    var opcodes = typeof(OpCodes).GetFields().Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
    var lines = new List<string>();
    foreach (var type in assembly.GetTypes().Where(t => t.FullName!.StartsWith("CilicaAudit.Kernels")))
    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        var body = method.GetMethodBody();
        if (body == null) continue;
        lines.Add($"{type.FullName}::{method.Name} locals={body.LocalVariables.Count} initlocals={body.InitLocals}");
        var bytes = body.GetILAsByteArray()!;
        for (int offset = 0; offset < bytes.Length;)
        {
            int start = offset;
            ushort code = bytes[offset++];
            if (code == 0xfe) code = (ushort)(0xfe00 | bytes[offset++]);
            var opcode = opcodes[code];
            int size = opcode.OperandType switch {
                OperandType.InlineNone => 0, OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2, OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + BitConverter.ToInt32(bytes, offset) * 4, _ => 4 };
            string operand = Convert.ToHexString(bytes.AsSpan(offset, size));
            if (opcode.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            {
                try { operand = assembly.ManifestModule.ResolveMember(BitConverter.ToInt32(bytes, offset))!.ToString()!; } catch { }
            }
            lines.Add($"  IL_{start:X4}: {opcode.Name} {operand}");
            offset += size;
        }
    }
    File.WriteAllLines(args[2], lines);
    context.Unload();
    return;
}
if (args[0] == "rewrite")
{
    var timer = Stopwatch.StartNew();
    var logger = new ConsoleLogger { MinLevel = LogLevel.Warn };
    using var resolver = new ModuleResolver(logger);
    resolver.AddSearchPaths([Path.GetDirectoryName(Path.GetFullPath(args[1]))!]);
    resolver.AddTrustedSearchPaths();
    var module = resolver.Load(args[1]);
    bool portable = args[3] == "portable";
    bool restricted = args[3] == "restricted";
    var compilation = new Compilation(module, logger, restricted ? RestrictedPipeline.CreateSettings() : new CompilationSettings {
        VerifyEachPass = true, AssumeLittleEndian = !portable, AllowCrossAssemblyIPO = !portable });
    var pipeline = restricted ? RestrictedPipeline.Create(compilation) : CompilationPipeline.CreateDefault(compilation);
    // At the pinned revision the final segment is LoopVectorizer + cleanup.
    if (args[3] is "scalar" or "portable") pipeline.Pipeline.RemoveAt(pipeline.Pipeline.Count - 1);
    else if (args[3] != "default" && !restricted) throw new ArgumentException("Unknown pipeline");
    // Match the CLI candidate filter: rewrite the main module, allowing the
    // inliner to import dependencies as needed, without optimizing them first.
    var methods = PassManager.GetCandidateMethodsFromIL(module, (_, method) => method.Module == module);
    var result = pipeline.Run(methods);
    timer.Stop();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
    Save(args[2] + ".rewrite.json", new { Pipeline = args[3], result.Succeeded, Seconds = timer.Elapsed.TotalSeconds,
        InputSha256 = Hash(args[1]), Methods = result.Methods.Select(m => new { Method = m.Method.ToString(), Status = m.Status.ToString(), m.FailedPhase, Error = m.Error?.ToString() }) });
    result.ThrowIfFailed(); // Never benchmark a silently partially optimized assembly.
    module.Save(args[2], savePdb: false);
    Console.WriteLine($"{args[3]}: {result.Methods.Count} methods, {timer.Elapsed.TotalSeconds:F3}s, {Hash(args[2])}");
    return;
}

var input = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
var variants = new[] { (Name: "baseline", Path: input), (Name: "default", Path: Path.Combine(Path.GetDirectoryName(input)!, "default", Path.GetFileName(input))),
    (Name: "scalar", Path: Path.Combine(Path.GetDirectoryName(input)!, "scalar", Path.GetFileName(input))),
    (Name: "portable", Path: Path.Combine(Path.GetDirectoryName(input)!, "portable", Path.GetFileName(input))),
    (Name: "restricted", Path: Path.Combine(Path.GetDirectoryName(input)!, "restricted", Path.GetFileName(input))) };
if (args[0] == "bench" && args.Length > 4)
{
    var selected = args[4].Split(',');
    if (selected.Distinct().Count() != selected.Length || selected.Any(name => !variants.Any(v => v.Name == name)))
        throw new ArgumentException("Unknown or duplicate benchmark variant");
    variants = selected.Select(name => variants.Single(v => v.Name == name)).ToArray();
    foreach (var variant in variants)
        if (!File.Exists(variant.Path)) throw new FileNotFoundException($"Missing {variant.Name} input", variant.Path);
}
Save(Path.Combine(output, "environment.json"), new { Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription, ProcessorCount = Environment.ProcessorCount,
    TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    CompilerSha256 = Hash(typeof(M68kCompiler).Assembly.Location), CilicaSha256 = Hash(typeof(Compilation).Assembly.Location),
    Inputs = variants.Where(v => File.Exists(v.Path)).Select(v => new { v.Name, v.Path, Sha256 = Hash(v.Path) }),
    TimingScope = "In-process Compile only, one unrecorded warmup per variant/case/CPU, reversed variant order on odd rounds. Target cycles include cold startup. No preprocessing time included in Compile." });
var entries = args[0] == "example" ? new[] { args[3] } : new[] { "Arithmetic", "CommonExpressions", "Inlining", "StructPromotion", "ScalarReplacement", "Devirtualization", "Loop", "ArrayLoop", "Linq", "LinqRangeSelect", "ListGrowth", "Aliasing", "Volatile", "Finally", "NativeWidth", "SwitchTable", "CheckedOverflow" }.Select(x => "CilicaAudit.Kernels::" + x).ToArray();
var repetitions = args[0] == "example" ? 1 : args.Length > 3 ? int.Parse(args[3]) : 3;
var rows = new List<object>();
foreach (var entry in entries)
foreach (var target in new[] { M68kCpuTarget.M68000, M68kCpuTarget.M68020, M68kCpuTarget.M68040 })
{
    var failed = new HashSet<string>();
    for (int round = -1; round < repetitions; round++)
    foreach (var variant in round % 2 == 1 ? variants.Reverse() : variants)
    {
        if (!File.Exists(variant.Path) || failed.Contains(variant.Name)) continue;
        try
        {
            long allocated = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            var request = new M68kCompilationRequest {
                AssemblyPath = variant.Path, EntryPoint = entry, Cpu = target,
                ManagedAssemblyPaths = Directory.GetFiles(AppContext.BaseDirectory, "CopperSharp*.dll"),
                OutputFormat = M68kOutputFormat.Hunk, IncludedExportNames = [],
                MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
                Heap = new() { StartAddress = 0x100000, Size = 0x40000 },
                ExceptionMode = M68kExceptionMode.Full,
                RuntimeProfile = M68kRuntimeProfile.Application };
            var result = args[0] == "example" ? AmigaM68kCompiler.Compile(request) : M68kCompiler.Compile(request);
            timer.Stop();
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            if (round < 0) continue;
            var stem = $"{entry.Split("::")[1]}-{target}-{variant.Name}-{round}";
            File.WriteAllBytes(Path.Combine(output, stem + ".hunk"), result.Image);
            File.WriteAllText(Path.Combine(output, stem + ".map"), result.Map);
            string name = entry.Split("::")[1];
            int[] inputs = name is "Arithmetic" or "CommonExpressions" or "Inlining" or "StructPromotion" or "ScalarReplacement" or "Devirtualization" or "Aliasing" or "NativeWidth"
                ? [1, 7, 13, 31, -1, int.MinValue, int.MaxValue] : [1, 7, 13, 31];
            var executions = args[0] == "example" ? [] : inputs.Select(value => Execute(result, target, value)).ToArray();
            foreach (var execution in executions)
                if (execution.Result != ReferenceResults.Get(name, execution.Input)) throw new InvalidOperationException($"Incorrect {name}({execution.Input}): {execution.Result}, expected {ReferenceResults.Get(name, execution.Input)}");
            var row = new { Entry = entry, Cpu = target.ToString(), Variant = variant.Name, Round = round,
                ImageBytes = result.Image.Length, LoadedBytes = result.Code.Length, Symbols = result.Symbols.Count,
                CompileMs = timer.Elapsed.TotalMilliseconds, AllocatedBytes = allocated, Executions = executions,
                ImageSha256 = Convert.ToHexString(SHA256.HashData(result.Image)), Error = (string?)null };
            rows.Add(row);
            Console.WriteLine($"{stem}: {result.Code.Length} bytes; {string.Join(", ", executions.Select(x => $"{x.Input}:{x.Result}/{x.Cycles}"))}");
        }
        catch (Exception error)
        {
            failed.Add(variant.Name);
            rows.Add(new { Entry = entry, Cpu = target.ToString(), Variant = variant.Name, Round = round, Error = error.ToString() });
            Console.WriteLine($"FAIL {entry} {target} {variant.Name}: {error.Message}");
        }
        Save(Path.Combine(output, "samples.json"), rows);
    }
}
Save(Path.Combine(output, "samples.json"), rows);

static Execution Execute(M68kCompilationResult result, M68kCpuTarget target, int input)
{
    const uint load = 0x10000, stack = 0x300000, sentinel = 0x1000;
    var bus = new TestBus(0x400000);
    result.Code.CopyTo(bus.Memory.AsSpan((int)load));
    foreach (var relocation in result.Relocations) { uint address = load + (uint)relocation.Offset; bus.WriteLong(address, bus.ReadLong(address) + load); }
    bus.WriteLong(stack, sentinel);
    bus.WriteLong(0x4000, (uint)input);
    var model = Enum.Parse<M68kCpuModel>(target.ToString());
    using var cpu = M68kCoreFactory.Default.Create(model, bus);
    cpu.Reset(load + result.EntryPoint, stack);
    for (int instruction = 0; instruction < 5_000_000; instruction++)
    {
        if (cpu.State.ProgramCounter == sentinel)
        {
            if (cpu.State.A[7] != stack + 4) throw new InvalidOperationException("Unbalanced stack");
            return new(input, unchecked((int)cpu.State.D[0]), cpu.State.Cycles, instruction);
        }
        cpu.ExecuteInstruction();
        if (cpu.State.Halted || cpu.State.Stopped) throw new InvalidOperationException($"Stopped at {cpu.State.ProgramCounter:X}");
    }
    throw new InvalidOperationException("Execution budget exceeded");
}
record Execution(int Input, int Result, long Cycles, int Instructions);

static class ReferenceResults
{
    public static int Get(string name, int x) => unchecked(name switch {
        "Arithmetic" => ((x * 8 + x * 2) ^ (x >> 3)) + x % 16,
        "CommonExpressions" => (x * 7 + 11) * (x * 7 + 11) + (x * 7 + 11),
        "Inlining" => x * 3 + ((x * 3) >> 2) + x * 5 + ((x * 5) >> 2),
        "StructPromotion" => (x + 7) * 7, "ScalarReplacement" => (x + 7) * 3,
        "Devirtualization" => (x * 3 + 1) * 2, "Loop" => 3 * x * (x - 1) / 2 + 7 * x,
        "ArrayLoop" => 2016 + 64 * x, "Linq" => 3 * ((x + 1) / 2) * (((x + 1) / 2) - 1),
        "LinqRangeSelect" => 3 * x * (x - 1) / 2, "ListGrowth" => 2 * x - 1,
        "Aliasing" => (x + 3) * 4, "Volatile" => 2 * x + 1,
        "Finally" => x == 13 ? 35 : 6 * x + 1, "NativeWidth" => x & 7,
        "SwitchTable" => (x & 7) switch { 0 => 0x1234, 1 => 0x2345, 2 => 0x3456, 3 => 0x4567, 4 => 0x5678, 5 => 0x6789, 6 => 0x789a, _ => 0x1357 },
        "CheckedOverflow" => x > int.MaxValue / 100_000_000 ? 42 : x * 100_000_000,
        _ => throw new ArgumentException(name) });
}
