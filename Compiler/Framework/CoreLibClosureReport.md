# Reachable framework closure reports

The current nineteen-root corpus includes UTF-8 transcoding, standard Base64 and hexadecimal codecs plus an end-to-end byte/text round trip. [CoreLibCodecs.md](CoreLibCodecs.md) inventories the 41 exact codec signatures, target adapters and qualification. These additions do not open the Encoding object graph or unlisted CoreLib bodies.

Use `scripts/analyze-framework-closure.ps1` to run the compiler's existing framework analyzer across a fixed set of application roots and rank repeated compatibility gaps. The report is an inventory of reachable blockers, not an authorization to admit unlisted CoreLib bodies. Each result still comes from the exact binding registry, the selected target profile, and the verified implementation pack.

Create a root manifest with schema version 1:

```json
{
  "schemaVersion": 1,
  "roots": [
    {
      "id": "application-string-formatting",
      "assembly": "../artifacts/App.dll",
      "entry": "Example.Program::Main",
      "platform": "amiga",
      "cpu": "68000",
      "runtime": "application",
      "format": "hunk",
      "exports": "none",
      "expectedReturnValue": 42,
      "qualificationTest": "CopperSharp.Compiler.Tests.CompilerExecutionTests.SomeFixtureExecutesOnM68000",
      "qualificationDisplayName": "M68000",
      "frameworkImplementationManifest": "../artifacts/corelib-10.0.9.json",
      "managedAssemblies": ["../artifacts/CopperSharp.Runtime.Managed.dll"]
    }
  ]
}
```

Assembly and manifest paths are resolved relative to the root manifest. Each root is analyzed independently with `--framework-report`; by default only its entry point is a root (`exports: "none"`). Set `exports` to `"all"` or supply `includedExports` when the application relies on exported methods. Use `-FrameworkImplementationManifest` to apply one pinned pack manifest to all roots, or set `frameworkImplementationManifest` on individual roots. Exit code 1 is accepted only when the CLI produced its normal JSON compatibility report, which is expected when the root has unsupported members. Other process failures stop the run. All roots must use one compiler, target, framework contract, and implementation-pack provenance so gaps are comparable.

The repository includes [`corelib-closure-roots.json`](corelib-closure-roots.json), a corpus spanning integer formatting, spans, collections, LINQ, exceptions, StringBuilder, nullable joins, Decimal, floating point, invariant `DateOnly` calendar operations and canonical parsing, and bounded `TimeOnly` arithmetic, round-trip formatting, and canonical invariant parsing, plus deterministic Guid binary values and text round trips, and UTF-8, Base64 and hexadecimal codecs. Each root declares its expected return value and emulator test. DateOnly parsing admits only exact Parse/TryParse call sites with `CultureInfo.InvariantCulture` and canonical `yyyy-MM-dd` input; the DateOnly formatter admits only that literal format and provider. TimeOnly formatting admits only invariant `O`/`o`, and parsing admits only exact Parse/TryParse call sites with `CultureInfo.InvariantCulture`. Other parsing and culture-specific operations remain outside these bounded surfaces. Run the qualification gate from the repository root:

```powershell
./scripts/qualify-corelib-aot.ps1
```

The gate builds the CLI and fixtures in Release, verifies the pinned CoreLib hash and identity, requires all nineteen framework reports to be compatible with zero gaps, compiles every root through the CLI to a valid Hunk image with a compatible report, and runs the mapped M68000 emulator case for each root. Use `-NoBuild` only when both Release outputs already match the current sources. Optional `-CoreLibAssembly` selects the restored runtime-pack input; the pinned hash remains mandatory. `-DotNetWorkingDirectory` controls SDK resolution and defaults to the repository root, where `global.json` pins the SDK. Reports, compatibility JSON, and Hunk images are written under `TestResults/CoreLibAotQualification` by default.

Parser providers are proven at the actual CIL argument slot, rather than by getter proximity. Local copies, invariant branch joins, and loops are supported when all getter uses remain safe parser arguments. Mixed or escaping values are rejected, and reachability and method resolution share the cached proof. The separate four-CPU cached-provider execution test and adversarial admission suite are described in [CompatibilityTesting.md](CompatibilityTesting.md#invariant-parser-provider-flow).

The Guid roots qualify deterministic 16-byte values, explicit byte order and all five standard text formats, using exact pinned constructor/comparison bodies and target adapters for serialization and text. The four-CPU generated and adversarial suites separately compare target checksums with host .NET and protect the associated narrow incoming-argument spill fix. [Deterministic Guid compatibility](CompatibilityTesting.md#deterministic-guid-compatibility) lists the supported signatures and explicit exclusions. The gate records `execution/corelib-execution.trx` and requires exactly nineteen passing emulator cases, so a missing or skipped mapped test cannot silently pass qualification.

`prioritizedGaps` groups exact member identity, disposition, and reason. `rootCount` measures how many corpus roots encounter the gap, `callSiteCount` measures direct reachable call sites reported across those roots, and `examplePaths` retain up to three analyzer paths. The output records the compiler and target package versions plus SHA-256 hashes of the root manifest, CLI assembly, and implementation-pack manifest. Start implementation with a high-reachability gap whose managed/runtime semantics fit the target; keep profile exclusions and host-only runtime services explicitly deferred. Re-run the identical manifest after each change and review the movement of these counts alongside semantic execution tests.
