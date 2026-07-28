<!--
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
-->

# Full exception runtime

`M68kExceptionMode.Full` emits a self-contained 68k exception runtime. Catch,
finally, rethrow, and leave handling do not require host imports.

## Runtime frames

`A5` points to the current hidden runtime frame. Methods with exception regions,
and methods participating in a managed GC runtime, link a frame with this
fixed header:

| Offset | Value |
| ---: | --- |
| 0 | Previous runtime frame |
| 4 | Method descriptor |
| 8 | Stable method frame base |
| 12 | Current exception action |
| 16 | Active exception |
| 20 | Pending unwind or leave action |
| 24 | Normal leave continuation |

Entry and export adapters isolate managed calls from an incoming `A5` chain.
Imports and Amiga library calls that use `A5` temporarily preserve and restore
the runtime-frame pointer.

Method descriptors contain signed frame-base offsets for reference arguments,
reference locals, the active exception, and evaluation-stack scratch roots.
The built-in collector walks the complete `A5` chain and the generated static
root table before sweeping.

## Exceptions

Compiler-generated failures use allocation-free static exception objects:

| Reason | Exception type |
| ---: | --- |
| 0 | Explicit `throw`; `throw null` becomes `NullReferenceException` |
| 1 | `NullReferenceException` |
| 2 | `IndexOutOfRangeException` |
| 3 | `DivideByZeroException` |
| 4 | `OverflowException` |
| 5 | `System.Exception` for annotated external-call failure |
| 6 | `OutOfMemoryException` |

Platform calls using `M68kExternalExceptionPolicy.NonZeroStatus` must report
their status in a data register. A nonzero status raises `System.Exception`.

Type descriptors include a base-descriptor pointer, so catch matching walks the
generated inheritance chain. Catch handlers receive one exception reference on
the evaluation stack. Finally handlers receive no evaluation-stack value.

Normal `leave` and exceptional unwind use the same generated continuation
actions. A finally body is emitted once; `endfinally` resumes its pending action.

The optional `M68kRuntimeImports.UnhandledException` hook receives the exception
in `A0` and the compiler reason in `D0`. If the hook is absent, or if it returns,
the runtime executes `ILLEGAL`.

The linked image exports `__c68k_exception_table`. Its entries point to method
descriptors containing root offsets, method bounds, and exception-region
metadata. Catch entries contain linked type-descriptor addresses rather than raw
metadata tokens.

Filters, fault clauses, stack traces, and managed exception messages are not
implemented.

`M68kExceptionMode.Yolo` emits no exception runtime. It retains non-returning
`ILLEGAL` fault paths and rejects methods containing managed exception regions.
