/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

const string pinnedHash = "DC1945DE746F94987EC705A1F27D512ABF72A41A1DA37E414D1951E4E823037A";
const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
var profiles = new Dictionary<string, Profile>(StringComparer.Ordinal)
{
    ["inherited-list"] = new("StringBuilderInheritedGenericListDispatchEntry", "SoftFloat", false, false),
    ["integral-joins"] = new("CoreLibStringBuilderGenericIntegralJoinsEntry", "Disabled", false, false),
    ["enum-joins"] = new("CoreLibStringBuilderGenericEnumJoinsEntry", "Disabled", false, false),
    ["join-callbacks"] = new("CoreLibGenericJoinCallbackContractsEntry", "Disabled", false, false),
    ["join-owners"] = new("CoreLibGenericJoinIteratorOwnershipEntry", "Disabled", false, false),
    ["boxed-join-iterator"] = new("CoreLibGenericJoinBoxedIteratorContractsEntry", "Disabled", false, false),
    ["enum-join-capacity"] = new("CoreLibGenericEnumJoinCapacityContractsEntry", "Disabled", false, false),
    ["floating-allocation"] = new("CoreLibStringBuilderFloatingAllocationContractsEntry", "SoftFloat", true, true),
    ["floating-left-allocation"] = new("CoreLibStringBuilderFloatingLeftAllocationContractsEntry", "SoftFloat", true, false),
    ["custom-floating-enumerable"] = new("CoreLibFloatingCustomEnumerableAllocationEntry", "SoftFloat", true, true, true)
};
if (args.Length == 1 && args[0] == "--list")
{
    Console.WriteLine(JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
var validating = args.Length == 2 && args[0] == "--validate-inputs";
if (!validating && args.Length != 4) throw new ArgumentException("Usage: --list | --validate-inputs BIN | PROFILE BIN OUTPUT FixedPoint|Disabled");
var bin = Path.GetFullPath(args[1]);
var coreLib = Path.Combine(bin, "PinnedCoreLib", "10.0.9", "System.Private.CoreLib.dll");
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
if (Hash(coreLib) != pinnedHash) throw new InvalidOperationException("Pinned CoreLib hash mismatch.");
AssemblyLoadContext.Default.Resolving += (context, name) => File.Exists(Path.Combine(bin, name.Name + ".dll")) ? context.LoadFromAssemblyPath(Path.Combine(bin, name.Name + ".dll")) : null;
var compiler = Assembly.LoadFrom(Path.Combine(bin, "CopperSharp.Compiler.dll"));
var tests = Assembly.LoadFrom(Path.Combine(bin, "CopperSharp.Compiler.Tests.dll"));
var fixture = tests.GetType("CopperSharp.Compiler.Tests.CompilerFixtures")!;
foreach (var profile in profiles.Values)
    if (fixture.GetMethod(profile.Entry) is null) throw new InvalidOperationException("Missing fixture: " + profile.Entry);
var packType = tests.GetType("CopperSharp.Compiler.Tests.FrameworkImplementationPackTests+CoreLibPack")!;
using var pack = (IDisposable)packType.GetMethod("CreateFromAssembly", all)!.Invoke(null, [coreLib, "Microsoft.NETCore.App.Runtime.win-x64", "win-x64", pinnedHash.ToLowerInvariant()])!;
if (validating)
{
    Console.WriteLine(JsonSerializer.Serialize(new { Profiles = profiles.Count, CompilerSha256 = Hash(Path.Combine(bin, "CopperSharp.Compiler.dll")), FixtureSha256 = Hash(tests.Location), CoreLibSha256 = pinnedHash, Status = "Validated fixture entries and fingerprinted pack; no compilation performed" }));
    return;
}
var selected = profiles[args[0]];
var output = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
var optionsType = compiler.GetType("CopperSharp.Compiler.M68kFrameworkImplementationPackOptions")!;
var options = Activator.CreateInstance(optionsType, [packType.GetProperty("ManifestPath")!.GetValue(pack)])!;
optionsType.GetProperty("EnableUnlistedManagedBodies", all)!.SetValue(options, selected.EnableUnlisted);
var requestType = compiler.GetType("CopperSharp.Compiler.M68kCompilationRequest")!;
var request = Activator.CreateInstance(requestType)!;
void Set(string name, object value) => requestType.GetProperty(name)!.SetValue(request, value);
void SetEnum(string name, string value)
{
    var type = requestType.GetProperty(name)!.PropertyType;
    Set(name, Enum.Parse(Nullable.GetUnderlyingType(type) ?? type, value));
}
Set("AssemblyPath", tests.Location);
Set("EntryPoint", "CopperSharp.Compiler.Tests.CompilerFixtures::" + selected.Entry);
// The probe runs outside compiler output, so resolve both original runtime
// assemblies explicitly rather than relying on AppContext.BaseDirectory.
Set("ManagedAssemblyPaths", new string[] { Path.Combine(bin, "CopperSharp.Runtime.Managed.dll"), Path.Combine(bin, "CopperSharp.Runtime.AmigaPal.dll") });
Set("FrameworkImplementationPack", options);
SetEnum("Cpu", "M68000");
SetEnum("OutputFormat", "Hunk");
SetEnum("ExceptionMode", "Full");
SetEnum("FloatingPoint", selected.FloatMode);
SetEnum("PeepholeOptimization", args[3]);
if (selected.ExternalAllocation)
{
    var importType = compiler.GetType("CopperSharp.Compiler.M68kRuntimeImports")!;
    var allocate = (string)importType.GetField("Allocate", all)!.GetRawConstantValue()!;
    SetEnum("MemoryManagement", "ExternalAllocator");
    var imports = new Dictionary<string, uint> { [allocate] = 0x2800, ["fixture.string-builder-allocation-failure"] = 0x2A00, ["fixture.floating-allocation-case"] = 0x2B00 };
    if (selected.CollectionGateway)
        imports[(string)importType.GetField("GcCollect", all)!.GetRawConstantValue()!] = 0x2C00;
    Set("Imports", imports);
    // Match RunFloatingAllocationContracts: retain the default export selection.
}
else
{
    Set("IncludedExportNames", Array.Empty<string>());
    SetEnum("MemoryManagement", "ManagedPoolMarkSweepGc");
    SetEnum("GcSweepStrategy", "EveryAllocation");
    var heapType = compiler.GetType("CopperSharp.Compiler.M68kHeapOptions")!;
    var heap = Activator.CreateInstance(heapType)!;
    heapType.GetProperty("StartAddress")!.SetValue(heap, 0x100000u);
    heapType.GetProperty("Size")!.SetValue(heap, 0x8000u);
    Set("Heap", heap);
}
File.WriteAllText(Path.Combine(output, "request.json"), JsonSerializer.Serialize(request, requestType, new JsonSerializerOptions { WriteIndented = true }));
var inputs = Directory.EnumerateFiles(bin, "*.dll", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => new { Path = Path.GetRelativePath(bin, path), Sha256 = Hash(path) }).ToArray();
File.WriteAllText(Path.Combine(output, "inputs.json"), JsonSerializer.Serialize(inputs, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PID {Environment.ProcessId}: compiling {args[0]} / {args[3]}");
using var process = Process.GetCurrentProcess();
var profiler = compiler.GetType("CopperSharp.Compiler.Backend.M68kOptimizationProfiler");
var session = Environment.GetEnvironmentVariable("COPPERSHARP_PROFILE_OPTIMIZER") == "1" ? profiler!.GetMethod("Start", all)!.Invoke(null, null) : null;
var cpuBefore = process.TotalProcessorTime;
var allocatedBefore = GC.GetTotalAllocatedBytes(true);
var stopwatch = Stopwatch.StartNew();
var result = compiler.GetType("CopperSharp.Compiler.M68kCompiler")!.GetMethod("Compile")!.Invoke(null, [request])!;
stopwatch.Stop();
process.Refresh();
var cpuSeconds = (process.TotalProcessorTime - cpuBefore).TotalSeconds;
var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
if (session is not null)
{
    var snapshot = session.GetType().GetMethod("GetSnapshot", all)!.Invoke(session, null)!;
    File.WriteAllText(Path.Combine(output, "optimizer-profile.json"), JsonSerializer.Serialize(snapshot, snapshot.GetType(), new JsonSerializerOptions { WriteIndented = true }));
    ((IDisposable)session).Dispose();
}
var resultType = result.GetType();
var analysis = resultType.GetProperty("FrameworkAnalysis")!.GetValue(result)!;
if (!(bool)analysis.GetType().GetProperty("IsCompatible")!.GetValue(analysis)!) throw new InvalidOperationException("Incompatible compilation.");
File.WriteAllBytes(Path.Combine(output, "image.hunk"), (byte[])resultType.GetProperty("Image")!.GetValue(result)!);
File.WriteAllText(Path.Combine(output, "image.map"), (string)resultType.GetProperty("Map")!.GetValue(result)!);
File.WriteAllText(Path.Combine(output, "analysis.json"), JsonSerializer.Serialize(analysis, analysis.GetType(), new JsonSerializerOptions { WriteIndented = true }));
var metrics = new { Profile = args[0], selected.Entry, Optimization = args[3], Cpu = "M68000", CompilationSeconds = stopwatch.Elapsed.TotalSeconds, CompilationCpuSeconds = cpuSeconds, AllocatedBytes = allocated, CompilerSha256 = Hash(compiler.Location), FixtureSha256 = Hash(tests.Location), CoreLibSha256 = pinnedHash, HunkSha256 = Hash(Path.Combine(output, "image.hunk")), MapSha256 = Hash(Path.Combine(output, "image.map")), AnalysisSha256 = Hash(Path.Combine(output, "analysis.json")), TimingScope = "Compile invocation only; excludes pack construction, input inventory, output writing and target execution" };
File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(metrics));

internal sealed record Profile(string Entry, string FloatMode, bool ExternalAllocation, bool EnableUnlisted, bool CollectionGateway = false);
