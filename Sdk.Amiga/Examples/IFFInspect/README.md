<!--
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
-->

# IFFInspect

`IFFInspect` opens a file through `dos.library` and validates its complete IFF
structure with `iffparse.library`.

The example is also a compiler regression workload. It uses a transparent
`IFFHandle` wrapper, nullable `BPTR` values, nested `try`/`finally` cleanup,
a custom managed exception with fields, typed `catch`, and several Amiga
library register conventions.

Build:

```powershell
dotnet build .\Sdk.Amiga\Examples\IFFInspect\IFFInspect.csproj
```

The program expects an IFF filename in its command-line arguments. A missing
file, allocation failure, malformed IFF stream, or parser error throws
`IFFInspectException`; `Main` catches it and returns `DOS.RETURN_ERROR` after
printing a diagnostic. Allocation failure is promoted to `DOS.RETURN_FAIL`.
