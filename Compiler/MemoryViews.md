# Borrowed views of guest memory

Small application value types can describe guest-memory fields without creating
a native object or an accessor call. A typical view stores an `APTR` and a
`ref T` to an application memory adapter. Its properties call the adapter with
named byte offsets; the adapter implements reads and writes using APTR intrinsics.
This keeps host test adapters and native access on the same source path.

Use a `readonly ref struct` and mark its constructor and small accessors with
`MethodImplOptions.AggressiveInlining`. The reference borrows the original
adapter, including mutable state and later replacement of its value. It does
not introduce a private adapter copy. Construction itself need not read memory.

The compiler now resolves closed generic application views across explicitly
supplied managed assemblies. Stateless application structs have addressable
native storage and can serve as constrained interface receivers without enabling
unlisted framework bodies. This does not extend the framework admission policy.

## Lowering

The existing constrained-call and guest-memory-wrapper folding passes expose
direct memory intrinsics. The scalar inliner additionally admits small attributed
value-type accessors and constructors that assign up to three scalar fields.
Readonly scalar argument homes, such as an APTR-valued setter parameter, can be
replaced by their incoming value when their address does not escape.

After inlining, `M68kLocalViewScalarizer` exposes four-byte fields of nonescaping,
reference-free local homes to memory SSA. It preserves copy snapshots and zero
initialization. Calls, returns, phis, indirect access, address comparisons, and
storing a home address prevent this transformation when they expose that home.
Exception regions and dynamic stack allocation remain excluded. Explicit
`NoInlining` boundaries remain calls.

Frame forwarding tracks byte offsets and access sizes. A write invalidates
overlapping cached values, a read preserves any overlapping earlier store, and
taking a home's address escapes every field. Adjacent fields must never alias
merely because they share a local variable index.

Folded memory loads inherit their intrinsic register constraints instead of the
removed wrapper's fixed return register. Integer loads and arithmetic retain data
register requirements; removing a call must not put an XOR or shift operand in
an address register. Pointer payloads can retain their permitted register bank.

Final value-type interface implementations that are reached only by constrained
calls need no separate dispatch root in an image without runtime object/boxed
descriptors or interface dispatch. Surviving calls and function addresses still
retain their targets. Runtime dispatch retains its normal roots.

## Verification

`BorrowedMemoryViewTests` executes direct and borrowed accesses on MC68000 and
MC68020. It checks code size, cycles, instruction counts, absence of calls and
view frame storage, pointer setters, mutable adapter replacement, imported
constructors/accessors, escaping copies, and bitwise register constraints.
`FrameSliceForwardingTests` covers adjacent fields, overlapping accesses, and
whole-home escape. Wrapper-folding tests retain fault, memory, CCR, and
`NoInlining` checks.

The optimization is proven from each compiled body, not from the name of a type
or property. Larger or escaping views can keep their calls or storage. Inspect
the generated map and execute representative native paths before relying on
zero-overhead lowering in a performance-sensitive component.
