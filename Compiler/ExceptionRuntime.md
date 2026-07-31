<!--
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
-->

# Full exception runtime

`M68kExceptionMode.Full` emits a self-contained 68k exception runtime. Catch,
finally, rethrow, and leave handling do not require host imports.

## Table-driven unwinding

Normal execution has no linked exception-frame chain and reserves no frame
pointer. `A5` is available to the register allocator like the other
callee-saved address registers. A method containing handlers reserves three
ordinary stack slots for its active exception, pending action, and leave
continuation; methods that only propagate exceptions have no EH-specific
prologue or epilogue instructions.

Every potentially throwing return PC has a generated 20-byte method-table
entry containing the exact resume PC, method descriptor, exception action,
stack adjustment, and optional GC root map. Dispatch finds the entry for the
return PC, computes the canonical frame base from the suspended A7 value, and
either enters the selected handler or runs the descriptor's unwind thunk.
The thunk restores only the callee-saved registers owned by that method, so
D2-D7/A2-A6 have the same preservation contract on normal and exceptional
returns.

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

The linked image exports aliases `__c68k_exception_table` and
`__c68k_method_table` for the unified table. Method descriptors contain frame
size, callee-save area size, and an unwind-thunk pointer. Catch actions contain
linked type-descriptor addresses rather than raw metadata tokens.

Filters, fault clauses, stack traces, and managed exception messages are not
implemented.

`M68kExceptionMode.Yolo` emits no exception runtime. It retains non-returning
`ILLEGAL` fault paths and rejects methods containing managed exception regions.
