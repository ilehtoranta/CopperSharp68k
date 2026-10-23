# CoreLib byte and text codecs

This milestone admits a bounded, deterministic codec surface from the pinned .NET 10.0.9 CoreLib. It covers 41 **public method signatures**: 3 `System.Text.Unicode.Utf8`, 10 `System.Buffers.Text.Base64`, 12 `System.Convert` Base64, and 16 `System.Convert` hexadecimal signatures. The compiler matches the declaring assembly and type, method name, static signature, parameter and return types, and generic shape. The implementation pack must be the verified pinned input with unlisted managed bodies disabled. Methods outside the table below remain unavailable through this codec surface.

In the table, `RB` means `ReadOnlySpan<byte>`, `WB` means `Span<byte>`, `RC` means `ReadOnlySpan<char>`, `WC` means `Span<char>`, `B[]` means `byte[]`, `C[]` means `char[]`, `I` means `int`, `O` means `out int`, `F` means `bool`, and `Opt` means `Base64FormattingOptions`. Optional source parameters remain part of their full metadata signature even when a C# caller omits them.

| Owner and method | Exact admitted signatures | Count |
| --- | --- | ---: |
| `Utf8.FromUtf16` | `(RC, WB, O, O, F, F) → OperationStatus` | 1 |
| `Utf8.ToUtf16` | `(RB, WC, O, O, F, F) → OperationStatus` | 1 |
| `Utf8.IsValid` | `(RB) → F` | 1 |
| `Base64.GetMaxEncodedToUtf8Length`; `GetMaxDecodedFromUtf8Length` | Each `(I) → I` | 2 |
| `Base64.EncodeToUtf8`; `DecodeFromUtf8` | Each `(RB, WB, O, O, F) → OperationStatus` | 2 |
| `Base64.EncodeToUtf8InPlace` | `(WB, I, O) → OperationStatus` | 1 |
| `Base64.DecodeFromUtf8InPlace` | `(WB, O) → OperationStatus` | 1 |
| `Base64.IsValid` | `(RB) → F`; `(RB, O) → F`; `(RC) → F`; `(RC, O) → F` | 4 |
| `Convert.ToBase64String` | `(B[]) → string`; `(B[], Opt) → string`; `(B[], I, I) → string`; `(B[], I, I, Opt) → string`; `(RB, Opt) → string` | 5 |
| `Convert.ToBase64CharArray` | `(B[], I, I, C[], I) → I`; `(B[], I, I, C[], I, Opt) → I` | 2 |
| `Convert.TryToBase64Chars` | `(RB, WC, O, Opt) → F` | 1 |
| `Convert.FromBase64String` | `(string) → B[]` | 1 |
| `Convert.FromBase64CharArray` | `(C[], I, I) → B[]` | 1 |
| `Convert.TryFromBase64String` | `(string, WB, O) → F` | 1 |
| `Convert.TryFromBase64Chars` | `(RC, WB, O) → F` | 1 |
| `Convert.ToHexString`; `ToHexStringLower` | Each `(B[]) → string`; `(B[], I, I) → string`; `(RB) → string` | 6 |
| `Convert.TryToHexString`; `TryToHexStringLower` | Each `(RB, WC, O) → F`; `(RB, WB, O) → F` | 4 |
| `Convert.FromHexString` | `(string) → B[]`; `(RC) → B[]`; `(RB) → B[]`; `(string, WB, O, O) → OperationStatus`; `(RC, WB, O, O) → OperationStatus`; `(RB, WB, O, O) → OperationStatus` | 6 |
| **Total** | | **41** |

`Utf8` converts scalar Unicode values between UTF-16 and UTF-8. Its two `OperationStatus` methods report only complete input units in the consumed count and complete output units in the written count. `replaceInvalidSequences` selects U+FFFD replacement or `InvalidData`; `isFinalBlock` distinguishes an incomplete trailing sequence (`NeedMoreData`) from final invalid input. A destination too short for the next complete unit returns `DestinationTooSmall` and preserves bytes or characters beyond the reported write count. `IsValid` checks a complete UTF-8 span without replacement.

The standard Base64 methods use the `A–Z`, `a–z`, `0–9`, `+`, `/` alphabet and `=` padding. Streaming encode and decode respect `isFinalBlock`, report consumed and written counts, and return `NeedMoreData`, `DestinationTooSmall`, or `InvalidData` at the same boundaries as the released CoreLib. Decode and validation admit only Base64 whitespace (`space`, tab, carriage return, line feed); `Convert` decode and `TryFrom` follow their own released validation and short-destination behavior, including legacy noncanonical padding accepted by Convert. `Base64FormattingOptions.InsertLineBreaks` emits CRLF after 76 output characters in `Convert` formatting. In-place methods operate within the caller's span and return status and written counts without allocating a replacement buffer. Streaming decode's returned prefix is the result: the host's vectorized implementation can overwrite an additional byte inside the supplied destination beyond `bytesWritten`, so long-input comparisons check the reported prefix, status and counts rather than that unspecified suffix. Guard regions outside the supplied span remain protected.

Hexadecimal encoding uses uppercase `A–F` by default and lowercase `a–f` in the `Lower` overloads. Its `TryTo` methods first check destination capacity; on failure they write nothing and report zero. Allocating `FromHexString` accepts even-length ASCII hex digits only, returns an empty array for empty input, and throws `FormatException` for odd length or invalid characters. Destination `FromHexString` overloads decode whole pairs, report the released invalid-nibble offset and number of completed bytes, and return `NeedMoreData` for an unpaired final nibble when the destination has room. When both nibbles are invalid, the low-nibble check takes precedence (`GG` reports consumed 1). If destination capacity ends earlier, `DestinationTooSmall` takes precedence. Both UTF-16 and UTF-8 input variants preserve bytes outside the reported write range.

The compiler maps these signatures to scalar methods in `ShadowUtf8`, `ShadowBase64`, `ShadowConvertBase64`, and `ShadowConvertHex`. This avoids host vector instructions, raw pointer helpers, and implicit host endianness. The two allocating `Convert` formatters construct a string through their own exact `CreateString(ReadOnlySpan<char>)` helper; the compiler admits that string constructor only for those verified runtime callers. Other application string-constructor call sites gain no admission from this rule.

The Base64 adapters retain released validation exceptions without opening arbitrary exception construction. The binder accepts `ArgumentNullException(string)` and `ArgumentOutOfRangeException(string)` only from an exact admitted public Base64 shadow method or `ShadowConvertBase64.CheckRange(int,int,int)`. It accepts `FormatException()` only from that adapter's private `FromChars(ReadOnlySpan<char>)`, `OutOfMemoryException()` only from `Required(int,bool)` when encoded size cannot fit, and `ArgumentException(string,string)` only from `CheckOptions(Base64FormattingOptions)` for an invalid option. These dependencies require the full caller and constructor signatures; other callers and overloads gain no admission.

Hex validation likewise preserves argument parameter names and released validation order. Its null constructor dependency is bounded to admitted public hex callers and the exact `EncodeArray` / `EncodeArraySlice` helpers; its range constructor dependency is bounded to `EncodeArraySlice` and `Encode`. Null array validation precedes ranges, negative `length` precedes `offset`, and string-null overloads distinguish `s` from `source`. Constructor and helper signature mutations are rejected by admission tests.

The boundary deliberately excludes `System.Text.Encoding` object graphs and custom fallback objects, `System.Buffers.Text.Base64Url`, UTF-8 interpolation or general text formatting, and other `Convert` operations. It also excludes Base64 or UTF-8 APIs introduced after .NET 10, including .NET 11 additions. No unlisted managed CoreLib body is opened to support the codecs.

Reference behavior is pinned to the released [Convert hexadecimal and Base64 source](https://github.com/dotnet/runtime/blob/v10.0.9/src/libraries/System.Private.CoreLib/src/System/Convert.cs), [hex digit decoder](https://github.com/dotnet/runtime/blob/v10.0.9/src/libraries/Common/src/System/HexConverter.cs), [UTF-8 transcoder](https://github.com/dotnet/runtime/blob/v10.0.9/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8.cs), [Base64 encoder](https://github.com/dotnet/runtime/blob/v10.0.9/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/Base64Encoder.cs), [decoder](https://github.com/dotnet/runtime/blob/v10.0.9/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/Base64Decoder.cs), and [validator](https://github.com/dotnet/runtime/blob/v10.0.9/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/Base64Validator.cs). The hash-verified win-x64 implementation input is `Microsoft.NETCore.App.Runtime.win-x64` 10.0.9, SHA-256 `dc1945de746f94987ec705a1f27d512abf72a41a1da37e414d1951e4e823037a`, as recorded in [CompatibilityTesting.md](CompatibilityTesting.md). The available host runner uses SDK 10.0.401 and .NET 10.0.12; host differential results therefore compare observable behavior on that runner while admission and compilation still use the pinned 10.0.9 metadata and CIL.

The pre-integration checkout based on commit `daab9bc` completed qualification on **2026-10-23**. Its final Release selection passed **450/450 tests**, with no skips or failures, in `TestResults/CoreLibCodecQualification/codec-final-regressions.trx`. It includes host differential tests for generated valid and malformed input, capacity and guard boundaries, validation exceptions and parameter names, and the compiler optimizer, register-allocation, frame-transfer, numeric, Guid and parser-provider regressions. Every admitted public codec signature is invoked by the native fixtures.

The selection contains **64 native execution cases**: 16 surface/pipeline cases across the four CPU targets, and 48 corpus cases covering UTF-8, Base64, hex, malformed Base64, retained-input streaming retries, and promoted comparisons on every CPU with both fixed-point and disabled peephole modes. Allocating paths use a 32 KiB managed heap with physical collection at every allocation. M68060 compilation uses the suite's existing M68040 emulator model; this does not qualify physical M68060 timing. The three compiler fixes required by these tests—comparison promotion, alignment after byte blobs, and complete phi spill stores—are described in [CompatibilityTesting.md](CompatibilityTesting.md#corelib-byte-and-text-codecs).

An independent, package-free host oracle compiled the final fixture sources on .NET 10.0.12. All five entry checks returned 42. Its actual results are recorded in `TestResults/CoreLibCodecQualification/host-oracle/host-oracle.json`; the following checksums also matched every corresponding native CPU/mode case:

| Corpus | Host/native checksum |
| --- | --- |
| UTF-8 generated | `A63B47C4` |
| Base64 generated, including in-place lengths 0–75 | `AB1E1E34` |
| Hex generated | `0C733167` |
| Base64 malformed inputs | `045E1F44` |
| Streaming retries | `19F5074C` |
| Promoted narrow comparisons | `F1F8A995` |

The expanded gate passed **19/19 compatible closure roots with zero gaps**, produced **19 valid Hunk images with compatible compiled reports**, and passed **19/19 mapped M68000 emulator cases** without skips. Its fresh reports, images, maps and execution TRX are under `TestResults/CoreLibCodecQualification/full-gate`. The four new roots are UTF-8, Base64, hex, and a Unicode → UTF-8 → Base64 → hex round trip that retains live values across collection. The gate can be rerun from the repository root with:

```powershell
./scripts/qualify-corelib-aot.ps1 -DotNetWorkingDirectory .. -OutputDirectory TestResults/CoreLibCodecQualification/full-gate
```

The tracked [`run-corelib-codec-host-oracle.ps1`](../../scripts/run-corelib-codec-host-oracle.ps1) generates a package-free .NET 10 console project that links the six current fixture sources, checks the expected host results, and writes `host-oracle.json`. The tracked [`audit-corelib-codec-qualification.ps1`](../../scripts/audit-corelib-codec-qualification.ps1) writes `qualification-audit.json` under its qualification directory. For the historical output directory, rerun both after generating its regression TRX and `full-gate` outputs:

```powershell
./scripts/run-corelib-codec-host-oracle.ps1 -DotNetWorkingDirectory ..
./scripts/audit-corelib-codec-qualification.ps1 -QualificationDirectory TestResults/CoreLibCodecQualification -RegressionTrx TestResults/CoreLibCodecQualification/codec-final-regressions.trx
```

For a fresh integration run, use its own directory and pass the actual final regression TRX name:

```powershell
./scripts/run-corelib-codec-host-oracle.ps1 -OutputDirectory TestResults/CoreLibCommitQualification/host-oracle -DotNetWorkingDirectory ..
./scripts/audit-corelib-codec-qualification.ps1 -QualificationDirectory TestResults/CoreLibCommitQualification -RegressionTrx TestResults/CoreLibCommitQualification/corelib-commit-final-regressions.trx
```

The audit resolves `host-oracle/host-oracle.json` and `full-gate` beneath the selected qualification directory. It records SHA-256 hashes for the final compiler, runtime, test and CLI assemblies, the pinned implementation input, source files, closure manifest, host oracle, TRX reports and every Hunk/map/compatibility report. It verifies exact native case selection, artifact freshness, dependency-copy identity and word alignment of emitted type descriptors in all 19 maps. Earlier diagnostic reports are superseded by the historical final results above.

## Integration qualification on current main

The self-contained CoreLib stack was integrated onto main parent `d6ebcc5` and qualified separately on **2026-10-23** with SDK 10.0.401 and host .NET 10.0.12. This integration preserves that parent's code-size, runtime descriptor, constructed-type indexing and frame changes. It includes the required DateOnly, TimeOnly, invariant-provider and deterministic Guid surfaces together with the codec additions. The ordered-enumeration unsupported path uses the runtime's private throw intrinsic; no public exception-constructor admission is added for that guard.

`TestResults/CoreLibCommitQualification/corelib-commit-final-regressions.trx` passes **511/511 tests with zero skips**, including the complete 64-case codec/comparison native matrix and the calendar, Guid, provider, optimizer, allocation and frame regressions. The independent host oracle reproduces all five entry results and six checksums above. The gate in `TestResults/CoreLibCommitQualification/full-gate` passes **19/19 compatible roots with zero gaps**, generates 19 compatible Hunk images, and passes **19/19 mapped M68000 execution cases with zero skips**. The tracked audit passes for these exact artifacts and confirms that all **2,543 descriptor addresses** across the 19 maps are word-aligned.

`TestResults/CoreLibCommitQualification/qualification-audit.json` records the complete source and artifact inventory for this integration. Its Release assembly SHA-256 hashes are:

| Assembly | SHA-256 |
| --- | --- |
| Compiler | `28a1f54845bb6161d6f9f60c384432896f74c2d5f3aa591e41b397958d69aa56` |
| Runtime.Managed | `0c46b8496ed7ae44e2466a2feb256fac6d65729a0b7c14a6ca89813fd88a5bd0` |
| Compiler.Tests | `15b164b77d99d7b8f37e6880df54130992440e652a4359f5628f0e3bbc93982b` |
| Compiler.Cli | `a3d1a6e016fe8e6f14ddc38b86b2e4111b6e66f131bc1cdf36bcee5cbf79623a` |

These selected runs do not repeat the complete historical StringBuilder acceptance inventory or native AmigaOS integration matrix.
