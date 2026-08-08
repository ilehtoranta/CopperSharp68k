using CopperSharp.Compiler;
using CopperSharp.Targets.Amiga;

var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
var assemblyPath = Path.Combine(directory, "bin", "Debug", "net10.0", "DOS.dll");
var sdkPath = Path.Combine(directory, "bin", "Debug", "net10.0", "CopperSharp.Sdk.Amiga.dll");
var outputPath = Path.Combine(directory, "DOS.generated.s");

M68kCompilationRequest Request(M68kOutputFormat format) => new()
{
    AssemblyPath = assemblyPath,
    ManagedAssemblyPaths = [sdkPath],
    EntryPoint = "DOSExample.Program::Main",
    Cpu = M68kCpuTarget.M68000,
    OutputFormat = format,
    RuntimeProfile = format == M68kOutputFormat.Hunk
        ? M68kRuntimeProfile.Application
        : M68kRuntimeProfile.Freestanding
};

var assembly = AmigaM68kCompiler.Compile(Request(M68kOutputFormat.Assembly));
File.WriteAllText(outputPath, assembly.Text);
var hunk = AmigaM68kCompiler.Compile(Request(M68kOutputFormat.Hunk));
Console.WriteLine(
    $"Wrote {System.Text.Encoding.UTF8.GetByteCount(assembly.Text)} UTF-8 bytes to '{outputPath}'; encoded HUNK size is {hunk.Image.Length} bytes.");
