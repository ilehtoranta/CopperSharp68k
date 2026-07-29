<!--
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
-->

# Polymorphism

This example demonstrates CopperSharp's two supported polymorphic dispatch
paths:

- `Calculation.Apply()` is called through an abstract base-class reference and
  dispatches through the concrete object's vtable.
- `ICalculation.Apply()` is called through an interface reference and
  dispatches through the concrete object's interface map.

Each concrete leaf declares `ICalculation`; the abstract base supplies the
shared virtual contract and inherited state.

The concrete implementation is selected at run time for both reference types.
With no command-line argument the program creates `Addition`; with an argument
it creates `Multiplication`. The abstract base class also owns an `Operand` reference,
showing that managed reference fields inherited by derived classes retain the
correct object layout and tracing metadata.

Build the managed example assembly:

```powershell
dotnet build .\Sdk.Amiga\Examples\Polymorphism\Polymorphism.csproj
```

Compile it to Motorola 68000 assembly:

```powershell
dotnet run --project .\Compiler.Cli -- `
  .\Sdk.Amiga\Examples\Polymorphism\bin\Debug\net10.0\Polymorphism.dll `
  --entry PolymorphismExample.Program::Main `
  --format asm `
  --output .\Sdk.Amiga\Examples\Polymorphism\bin\Polymorphism.s
```

The process returns the sum of the two dispatched calls, so its result is `21`
with no argument (`10 + 11`) and `52` when an argument is present (`24 + 28`).
The example intentionally has no platform imports, which also makes it useful
as a compact compiler regression workload. Producing a linked HUNK executable
also requires the target runtime to supply the managed allocator import used by
the three object allocations.
