# StopwatchBenchmark

Portable `System.Diagnostics.Stopwatch` benchmark for CopperSharp68k on
AmigaOS. It calculates CRC-32 over a deterministic 4 KiB byte stream, then
prints the checksum, elapsed timer ticks, timer frequency, and timer resolution.
The source uses only .NET APIs; the Amiga platform layer supplies the supported
`Stopwatch` and `Console` implementations during AOT compilation.

CopperSharp currently supports the static `Stopwatch.GetTimestamp()`,
`Stopwatch.Frequency`, and `Stopwatch.IsHighResolution` timing surface. The
example retains the low 32 bits of each timestamp, giving an unambiguous
elapsed interval for benchmarks shorter than one full 32-bit timer wrap.

Build the managed example assembly:

```powershell
dotnet build .\Sdk.Amiga\Examples\StopwatchBenchmark\StopwatchBenchmark.csproj
```

Compile it as an Amiga HUNK executable:

```powershell
dotnet run --project .\Compiler.Cli -- `
  .\Sdk.Amiga\Examples\StopwatchBenchmark\bin\Debug\net10.0\StopwatchBenchmark.dll `
  --entry StopwatchBenchmarkExample.Program::Main `
  --output .\Sdk.Amiga\Examples\StopwatchBenchmark\StopwatchBenchmark `
  --platform amiga --cpu 68000 --format hunk --runtime application --exceptions full
```

The CRC-32 should be `548B6D54` (printed as decimal `1418423636`). Divide
elapsed ticks by ticks per second to convert the measurement to seconds.
