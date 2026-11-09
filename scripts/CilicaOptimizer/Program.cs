using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Cilica;
using Cilica.AsmIO;
using CopperSharp.Tools;

try
{
    if (args.Length < 2 || (args.Length - 2) % 2 != 0)
        throw new ArgumentException("Usage: CilicaOptimizer <input.dll> <output.dll> [--resolver <directory>]... [--inlining on|off]");
    var input = Path.GetFullPath(args[0]);
    var output = Path.GetFullPath(args[1]);
    var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    if (string.Equals(input, output, comparison))
        throw new ArgumentException("Choose a separate output file; in-place rewriting is disabled.");
    if (!string.Equals(Path.GetFileName(input), Path.GetFileName(output), comparison))
        throw new ArgumentException("Preserve the DLL filename in a separate output directory so dependency resolution stays consistent.");
    var searchPaths = new List<string> { Path.GetDirectoryName(input)! };
    var enableInlining = true;
    for (int i = 2; i < args.Length; i += 2)
    {
        switch (args[i])
        {
            case "--resolver":
                searchPaths.Add(Path.GetFullPath(args[i + 1]));
                break;
            case "--inlining":
                enableInlining = args[i + 1] switch
                {
                    "on" => true,
                    "off" => false,
                    _ => throw new ArgumentException("--inlining must be either 'on' or 'off'.")
                };
                break;
            default:
                throw new ArgumentException($"Unknown option: {args[i]}");
        }
    }

    var timer = Stopwatch.StartNew();
    var logger = new ConsoleLogger { MinLevel = LogLevel.Warn };
    using var resolver = new ModuleResolver(logger);
    resolver.AddSearchPaths(searchPaths);
    resolver.AddTrustedSearchPaths();
    var module = resolver.Load(input);
    var settings = RestrictedPipeline.CreateSettings(enableInlining);
    var compilation = new Compilation(module, logger, settings);
    var methods = PassManager.GetCandidateMethodsFromIL(module, (_, method) => method.Module == module);
    var result = RestrictedPipeline.Create(compilation).Run(methods);
    result.ThrowIfFailed();

    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var stagedOutput = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        module.Save(stagedOutput, savePdb: false);
        File.Move(stagedOutput, output, overwrite: true);
    }
    finally
    {
        if (File.Exists(stagedOutput)) File.Delete(stagedOutput);
    }
    timer.Stop();
    File.WriteAllText(output + ".cilica.json", JsonSerializer.Serialize(new {
        Pipeline = "restricted", CilicaRevision = RestrictedPipeline.SourceRevision,
        settings.AssumeLittleEndian, settings.AllowCrossAssemblyIPO, settings.VerifyEachPass,
        settings.EnableInlining,
        EnableScalarReplacement = settings.EnableScalarReplacement ?? settings.EnableInlining,
        Input = input, Output = output,
        InputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
        OutputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),
        Seconds = timer.Elapsed.TotalSeconds,
        Methods = result.Methods.Select(m => new { Method = m.Method.ToString(), Status = m.Status.ToString() })
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Restricted CILica: {result.Methods.Count} methods, {timer.Elapsed.TotalSeconds:F3}s; {output}");
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
return 0;
