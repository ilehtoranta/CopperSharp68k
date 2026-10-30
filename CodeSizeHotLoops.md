# Narrow values and register transport in resident hot loops

Search's feasible-color allocator removed spills but exposed avoidable byte
normalization and register transport. The frozen shipping and MorphOS command
assemblies are qualified independently in CopperOS's `search-hotloops-20261018`
receipts. No command source or command option is changed.

## Value facts

`MOVE.B` to a data register preserves the upper 24 bits for every legal source,
including memory. Retain those known upper bits without making assumptions about
the loaded byte. This lets the existing narrow-boolean rewrite replace
`SEQ; EXT.W; EXT.L; NEG.L` with `SEQ; NEG.B` when the result is provably canonical.
The analysis also invalidates every explicitly declared native-call clobber,
including nonstandard clobbers outside D0-D1/A0-A1.

## Opt-in transport cleanup

The existing `RemoveRedundantTransport` selection additionally supports:

- Forwarding a full register copy into one comparison or arithmetic source,
  across at most eight contiguous register-only instructions. The source must
  remain unchanged, the copied flags must be overwritten before observation,
  and the temporary must be dead afterward.
- A bounded, bit-sensitive CFG proof for a temporary whose upper bits remain
  conservatively live through later byte/word writes. Every actual observation
  rejects removal until the copied bits have been overwritten. The proof has a
  256-state bound and rejects unresolved exits and unknown uses.
- Inspecting an unannotated internal leaf call to prove it neither reads nor
  writes that register. The separate 64-instruction proof rejects opaque
  instructions, non-return annotations, nested calls and saves of that register.
  RTS ABI annotations are handled by checking actual consumers in the caller. Unknown
  calls retain their complete ABI uses.
- Removing zero operand staging, or replacing `OR.L #known-zero-register,Dn`
  with `TST.L Dn` when its flags remain live. X is preserved in either form.

These rewrites use the existing eligible MC68000 generated-code ranges and
policy gates. They do not change defaults or relax resident reserved registers,
exception, GC, stack or alias exclusions. Read-only unreferenced markers can be
retargeted; referenced entries, relocation destinations and label-plus-addend
spans inside a shortened region remain barriers. The first copy's entry can
move to its replacement because its observable behavior is retained.

## Regression coverage

`M68kHotLoopTransportTests` executes before/after images on 68000, 68020 and 68040
with all 32 initial CCR combinations, including X. It compares all registers,
final flags, stack restoration, memory contents, and guest access widths/order.
The observation instructions declare their real effects, and the return makes
all registers and final CCR observable; dead ABI outputs are not used as an
equivalence oracle. Positive and negative fixtures cover partial writes,
branches, loops, live upper bits, leaf calls, explicit call clobbers, entry
labels, relocation addends, memory barriers, policy-off and unsupported CPUs.
