# Copper68k.AmigaSdk

Amiga ABI declarations for code compiled by `Copper68k.Compiler.Amiga`.

The initial reference surface includes:

- `Amiga.Exec.OpenLibrary`, LVO -552: name in A1, minimum version in D0,
  library base returned in D0.
- `Amiga.Dos.Open`, LVO -30: name in D1, access mode in D2, file handle
  returned in D0.

Pointer arguments are represented as 32-bit guest addresses.

Kickstart 3.1 library folders are present as ABI declaration stubs. Most
libraries currently expose only their Amiga library name constant until their
LVO declarations are added.

`Amiga.Dos` includes the non-variadic AmigaOS 3.x and MorphOS m68k ABI vector
surface. C varargs convenience wrappers, such as `Printf()` and `SystemTags()`,
are intentionally represented by their underlying vector/tag-list forms.

`Amiga.Intuition` follows the same convention. Variadic convenience wrappers,
such as `NewObject()`, `SetAttrs()`, and `EasyRequest()`, are represented by
their underlying `*A`/argument-array vector forms.

`Amiga.Graphics` is generated from the m68k ABI vector surface. Header macros
without standalone library vectors, such as `AreaCircle()` and `SetOPen()`, are
represented by the underlying vector calls they expand to.

`Amiga.Layers` exposes the m68k ABI vector surface, including MorphOS tag-list
and visibility helpers where those are present in the MorphOS ABI headers.

`Amiga.MuiMaster` exposes the MorphOS MUI `muimaster.library` vector surface.
The MUI stdarg conveniences (`MUI_NewObject()`, `MUI_MakeObject()`,
`MUI_Request()`, `MUI_RequestObject()`, `MUI_AllocAslRequestTags()`, and
`MUI_AslRequestTags()`) are represented as wrappers over the corresponding
`A`/tag-list entry points. Their final argument is the guest address of the
already-built tag or parameter array, matching the MorphOS inline stdarg macros'
lowering to the real library calls.

See `Examples/MuiSunflower` for a minimal MUI object tree with an application,
window, group, text object, and button.
