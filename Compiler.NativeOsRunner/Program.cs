using System.Globalization;
using System.Text.Json;
using Copper68k;
using CopperSharp.Compiler;
using CopperSharp.Compiler.NativeOsRunner;
using CopperSharp.Compiler.Tests.NativeOsFixture;
using CopperSharp.Targets.Amiga;

try
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("NativeOsRunner --hunk FILE --rom KICKSTART31 [--expect-d0 VALUE] [--arguments TEXT] [--input-file FILE] [--cpu 68000|68020|68040] [--jit] [--max-frames 1500] [--timeout-seconds 120] [--save-adf FILE]\n  --fixture Answer|HighBitAnswer|DosProbe|ArgumentProbe compiles a built-in payload instead of --hunk.\nNative Kickstart boots a host RAM-backed OFS ADF; ROMs are never included.");
        return args.Length == 0 ? 1 : 0;
    }
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--jit") { values.Add(args[i], "true"); continue; }
        if (!args[i].StartsWith("--") || ++i >= args.Length)
            throw new ArgumentException("Options require a value.");
        values.Add(args[i - 1], args[i]);
    }
    string[] allowed = ["--hunk", "--rom", "--fixture", "--expect-d0", "--arguments", "--cpu", "--jit", "--max-frames", "--timeout-seconds", "--save-adf", "--input-file"];
    foreach (var option in values.Keys)
        if (!allowed.Contains(option)) throw new ArgumentException($"Unknown option: {option}");
    if (values.ContainsKey("--hunk") && values.ContainsKey("--fixture"))
        throw new ArgumentException("Choose either --hunk or --fixture.");
    var expected = values.GetValueOrDefault("--expect-d0");
    uint? expectedValue = expected is null ? null : expected.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? uint.Parse(expected[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
        : ParseReturnValue(expected);
    var rom = values.GetValueOrDefault("--rom") ?? Environment.GetEnvironmentVariable("COPPERSHARP_KICKSTART31_ROM")
        ?? throw new ArgumentException("Supply --rom or COPPERSHARP_KICKSTART31_ROM.");
    var model = values.GetValueOrDefault("--cpu", "68000") switch
    {
        "68000" => M68kCpuModel.M68000,
        "68020" => M68kCpuModel.M68020,
        "68040" => M68kCpuModel.M68040,
        _ => throw new ArgumentException("Supported CPUs: 68000, 68020, 68040.")
    };
    if (values.ContainsKey("--jit") && model != M68kCpuModel.M68040)
        throw new ArgumentException("The current engine supports JIT only for 68040.");
    byte[] payload;
    if (values.TryGetValue("--fixture", out var fixture))
    {
        if (fixture is not ("Answer" or "HighBitAnswer" or "DosProbe" or "ArgumentProbe")) throw new ArgumentException("Unknown fixture.");
        payload = AmigaM68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = typeof(NativeOsFixture).Assembly.Location,
            EntryPoint = $"{typeof(NativeOsFixture).FullName}::{fixture}",
            RuntimeProfile = M68kRuntimeProfile.Application,
            ExceptionMode = M68kExceptionMode.Full,
            IncludedExportNames = []
        }).Image;
    }
    else payload = await File.ReadAllBytesAsync(values.GetValueOrDefault("--hunk")
        ?? throw new ArgumentException("Supply --hunk or --fixture."));
    IReadOnlyDictionary<string, byte[]>? inputFiles = null;
    if (values.TryGetValue("--input-file", out var inputPath))
        inputFiles = new Dictionary<string, byte[]> { [Path.GetFileName(inputPath)] = await File.ReadAllBytesAsync(inputPath) };
    var result = await NativeOsSession.RunAsync(payload, await File.ReadAllBytesAsync(rom),
        values.GetValueOrDefault("--arguments", ""), model,
        values.ContainsKey("--jit"),
        int.Parse(values.GetValueOrDefault("--max-frames", "1500"), CultureInfo.InvariantCulture),
        TimeSpan.FromSeconds(int.Parse(values.GetValueOrDefault("--timeout-seconds", "120"), CultureInfo.InvariantCulture)),
        values.GetValueOrDefault("--save-adf"), inputFiles);
    var success = result.StopReason == "ProgramReturned" && result.LaunchStatus == 0 &&
        (!expectedValue.HasValue || result.ReturnValue == expectedValue.Value);
    Console.WriteLine(JsonSerializer.Serialize(new { success, result }));
    return success ? 0 : 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.ToString());
    return 1;
}

static uint ParseReturnValue(string text)
{
    var value = long.Parse(text, CultureInfo.InvariantCulture);
    if (value < int.MinValue || value > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(text));
    return unchecked((uint)value);
}
