# CopperSharp.Sdk.Amiga

Amiga ABI declarations for code compiled by `CopperSharp.Targets.Amiga`.

## ABI integer and pointer types

The SDK keeps fixed-width 68k guest values separate from pointer-sized API
values:

- `uint` and `int` represent fixed 32-bit guest fields.
- `APTR`, `BPTR`, `STRPTR`, and `CONST_STRPTR` represent the corresponding
  Amiga pointer encodings. `WSTRPTR` and `CONST_WSTRPTR` represent MorphOS
  native-endian UCS-4/UTF-32 string pointers (`WCHAR` is a 32-bit `int`), not
  UTF-16 strings.
- `IPTR` and `SIPTR` represent MorphOS-style unsigned and signed
  pointer-sized scalar values. They are currently emitted as 32-bit values by
  the CopperSharp 68k compiler and are reserved for API arguments, return
  values, and varargs—not fixed-width guest records.

`AmigaVarArg` carries an `IPTR` value while retaining its `Raw` 32-bit view for
the current 68k ABI. A future target can widen the native integer without
changing the semantic API type.

## Start a project

Install the compiler tool and project templates:

```text
dotnet tool install --global CopperSharp.Compiler.Cli --version 0.1.0-preview.1
dotnet new install CopperSharp.Templates::0.1.0-preview.1
```

Create and build a minimal Amiga program:

```text
dotnet new amiga -n HelloAmiga
cd HelloAmiga
dotnet build
```

The generated project references this SDK and `CopperSharp.Compiler`. Its build
produces a managed assembly followed by a Motorola 68k HUNK executable. The
sample opens `dos.library`, writes a line, and closes the library before
returning.

The initial reference surface includes:

- `Amiga.Exec.OpenLibrary`, LVO -552: name in A1, minimum version in D0,
  library base returned in D0, or `null` when the library cannot be opened.
- `Amiga.DOS.Open`, LVO -30: name in D1, access mode in D2, file handle
  returned in D0.

Pointer arguments are represented as 32-bit guest addresses.

## Hardware registers

`Amiga.Hardware` contains typed, allocation-free register façades alongside the
OS subsystem namespaces. Its model catalog covers the Commodore systems from
the Amiga 1000 through the Amiga 4000T, plus CDTV and CD32. The shared register
surface includes the complete OCS/ECS/AGA custom-chip map and both MOS 8520
CIAs. Model-specific types cover the A2000- and A3000-style clocks, Gayle and
PCMCIA, A600/A1200 and A4000 IDE layouts, A3000/A4000 motherboard resources,
CDTV DMAC and CD32 Akiko.

`CustomChip` separates CPU-readable aliases, ordinary writes, pointer pairs,
write strobes, and set/clear operations. Use `CustomRegisterCatalog` to test
whether an ECS- or AGA-only register exists for the selected chipset. Register
bit fields retain their hardware width. Operations are static because the
current AOT profile supports scalar locals only; the structs carry no runtime
state. `HardwareBus` is the width-explicit escape hatch for expansion hardware
and controller revisions that have no common motherboard layout.

Use `CustomRegister` selectors when constructing Copper instructions. Direct
custom-chip access remains the caller's responsibility: own or disable the
affected OS resources before taking over the machine, and restore saved DMA,
interrupt, audio/disk, Copper, and display state before returning.

`AmigaHardware.GetFeatures()` describes built-in motherboard hardware only.
Expansion cards and user-installed Agnus, Denise, or accelerator upgrades must
be detected separately; the machine name alone cannot identify them safely.

`APTR` represents untyped byte-addressed Amiga pointers. `BPTR` represents DOS
BCPL pointers: the raw value passed to DOS is the byte address shifted right by
two, and `BPTR.Address` converts it back to an `APTR`. Use `APTR.Null`,
`BPTR.Null`, `IsNull`, and `IsNotNull` for pointer-null semantics instead of
raw numeric zero.

Manual library wrappers expose a lowered `<Wrapper>LibraryBase` property, such
as `DOS.DOSLibraryBase` or `Graphics.GraphicsLibraryBase`. Check the nullable
`APTR?` returned by `Exec.OpenLibrary()` and assign its non-null `Value` before
calling manual vectors. Clear or replace the base with `APTR.Null` during
cleanup as needed. These properties map directly to the same writable base
slots used by the generated LVO calls; they are not managed backing fields.

Wrappers with `AmigaLibraryBasePolicy.CallerProvided` instead receive their
already-opened library or device base as the single parameter assigned to
`A6`. These calls create no writable global base slot. `TimerDevice.ReadEClock`
uses this form because `Exec.OpenDevice()` returns the applicable base through
the request's `IORequest.Device` field.

`Amiga.BsdSocket` exposes the `bsdsocket.library` vector surface. The binding is
explicitly manual: open `bsdsocket.library` with `Exec.OpenLibrary()` only after
the TCP/IP stack is online, assign the returned base to
`BsdSocket.BsdSocketLibraryBase`, and close it during cleanup. Programs that
start without networking must not open or call this library.

Kickstart 3.1 library folders are present as ABI declaration stubs. Most
libraries currently expose only their Amiga library name constant until their
LVO declarations are added.

`Amiga.DOS` includes the non-variadic AmigaOS 3.x and MorphOS m68k ABI vector
surface. C varargs convenience wrappers, such as `Printf()` and `SystemTags()`,
are intentionally represented by their underlying vector/tag-list forms.
DOS 64-bit file-position and record-lock calls use 68k register pairs: a
`long`/`ulong` parameter annotated with `D2` occupies `D2/D3`, and a 64-bit
return annotated with `D0` is returned in `D0/D1`.

`Amiga.Intuition` follows the same convention. Variadic convenience wrappers,
such as `NewObject()`, `SetAttrs()`, and `EasyRequest()`, are represented by
their underlying `*A`/argument-array vector forms.

`Amiga.BOOPSI` exposes generic BOOPSI method-dispatch helpers. MorphOS provides
these as alib/direct-dispatch helpers rather than Intuition library vectors, so
the SDK declares runtime-resolved imports for `DoMethodA()`,
`DoSuperMethodA()`, and `CoerceMethodA()`. Callers pass the guest address of the
method message whose first ULONG is the method ID.
`BOOPSI.DoMethod(obj, methodId, ...)` overloads for zero to three method
arguments, plus `BOOPSI.DoMethod(obj, params uint[])` for wider messages, are
compiler-lowered conveniences. They build the temporary BOOPSI message on the
68k stack and call `DoMethodA()`; prebuilt messages can still use `DoMethodA()`
directly.

`Amiga.Graphics` is generated from the m68k ABI vector surface. Header macros
without standalone library vectors, such as `AreaCircle()` and `SetOPen()`, are
represented by the underlying vector calls they expand to.

`Amiga.Layers` exposes the m68k ABI vector surface, including MorphOS tag-list
and visibility helpers where those are present in the MorphOS ABI headers.

`Amiga.ASL` exposes the asl.library m68k ABI vector surface. Stdarg
conveniences (`AllocAslRequestTags()` and `AslRequestTags()`) are represented
as wrappers over the underlying tag-list vectors. The MorphOS m68k ABI
request lifecycle helpers (`AbortAslRequest()` and `ActivateAslRequest()`) are
included and marked in code.

`Amiga.CyberGraphics` exposes the cybergraphics.library m68k ABI vector
surface through V52. Stdarg conveniences are represented by the underlying
`*TagList` vectors, with wrapper aliases for the corresponding `*Tags` forms.
MorphOS m68k ABI alpha/composition extensions from V43, V50, V51, and V52 are
included and marked in code.

`Amiga.LowLevel`, `Amiga.Nonvolatile`, and `Amiga.Realtime` expose their m68k
ABI vector surfaces. LowLevel includes the MorphOS m68k
`SetJoyPortAttrsA()` extension and tag-list wrappers. Realtime follows the
same convention for player attribute stdarg conveniences by exposing the
underlying `A` calls plus wrapper aliases.

`Amiga.Commodities`, `Amiga.GadTools`, and `Amiga.Keymap` expose their m68k
ABI vector surfaces. GadTools stdarg conveniences are represented by the
underlying `A` calls plus wrapper aliases. Keymap includes MorphOS m68k
UCS4/codepage extensions and marks them in code.

`Amiga.Bullet` and `Amiga.Diskfont` expose their m68k ABI vector surfaces,
including tag-list `A` calls and wrapper aliases for their stdarg convenience
forms.

`Amiga.Utility` exposes the utility.library m68k ABI vector surface through
`GetUniqueID()`. The MorphOS m68k named-object vectors are included and
marked in code. The stdarg `AllocNamedObject()` convenience is represented by
the underlying `AllocNamedObjectA()` vector plus a wrapper alias.

`Amiga.MathFfp`, `Amiga.MathTrans`, `Amiga.MathIeeeSingBas`,
`Amiga.MathIeeeSingTrans`, `Amiga.MathIeeeDoubBas`, and
`Amiga.MathIeeeDoubTrans` expose their m68k ABI vector surfaces. Raw single
precision values are represented as `uint`; raw IEEE double values are
represented as `ulong` register-pair values.

`Amiga.Icon` exposes the icon.library m68k ABI vector surface, including the
V44 tag-list vectors (`DupDiskObjectA()`, `IconControlA()`,
`DrawIconStateA()`, `GetIconRectangleA()`, `GetIconTagList()`,
`PutIconTagList()`, and `LayoutIconA()`) as used by MorphOS' m68k-compatible
icon.library ABI. Variadic conveniences are represented by the underlying
`A`/tag-list entry points.

`Amiga.AmigaGuide`, `Amiga.Expansion`, and `Amiga.Locale` expose their m68k
ABI vector surfaces. Locale and AmigaGuide MorphOS direct function-pointer
helpers without register metadata are intentionally omitted; stdarg forms map
to the underlying `A`/tag-list vectors.

`Amiga.Datatypes`, `Amiga.IffParse`, `Amiga.RexxSysLib`, and
`Amiga.Workbench` expose their public m68k ABI vector surfaces. MorphOS m68k
ABI extensions with explicit register mappings are included and marked in
code. Workbench direct function-pointer helpers without register metadata are
documented but intentionally omitted, as are older Workbench LVO-table entries
without current public prototype/register documentation. Datatypes and
Workbench stdarg conveniences are represented by their underlying
`A`/tag-list vectors plus wrapper aliases.

`Amiga.RexxSupport` and `Amiga.Version` expose their library name metadata.
`rexxsupport.library` is an ARexx external function library loaded through
ADDLIB/rxlib rather than a public C-style vector surface, and `version.library`
does not publish callable SDK vectors. No MorphOS ppcinline m68k register
mapping is published for either library.

`Amiga.MUIMaster` exposes the MorphOS MUI `muimaster.library` vector surface.
`MUI_NewObject(CString, params uint[])` and
`MUI_MakeObject(int, params uint[])` are compiler-lowered: the inline `params`
values are written to the 68k stack, the matching pointer register receives
that stack address, and the real `A` library vector is called without
allocating a managed array. The pointer-style `A`/tag-list entry points remain
available for prebuilt guest taglists. The other MUI stdarg conveniences
(`MUI_Request()`, `MUI_RequestObject()`, `MUI_AllocAslRequestTags()`, and
`MUI_AslRequestTags()`) are currently represented as wrappers over the
corresponding `A`/tag-list entry points; their final argument is the guest
address of the already-built tag or parameter array.
`Amiga.MUI.MUIObject`, `ApplicationObject`, and `WindowObject` are thin typed
wrappers around raw MUI object pointers. They expose `Raw`, `DoMethod()`,
`SetAttrs()`, `Dispose()`, and class-specific `New()` factories while keeping
taglists and method messages as guest pointers.

The optional `AmiSSLMaster`, `XadMaster`, `XpkMaster`, and `CAMD` bindings are
manual-base libraries. Open and close their libraries from the program when
the corresponding service is available; the SDK does not add them to startup
opening. `AmiSSLMaster` is the bootstrap interface for selecting the versioned
AmiSSL API, while XAD and XPK delegate archive/compression work to their
installed clients or sublibraries. `CAMD` provides the shared MIDI framework.

`Amiga.MUI.TextEditor`, `TheBar`, and `BetterString` expose the public class
names, attributes, methods, and common value constants for the corresponding
optional `.mcc` files. They use the same `New(tags)` and `Do(object, message)`
pattern as the built-in MUI declarations.

See `Examples/DOS` for a minimal manual `dos.library` open/print/cleanup
program. See `Examples/MUISunflower` for a minimal MUI object tree with an
application, window, group, text object, and button. See
`Examples/IFFInspect` for DOS-backed IFF parsing with typed exceptions and
nested deterministic cleanup. See `Examples/FileStats` for a YOLO-style DOS
file report that mixes byte, word, and longword arithmetic. See
`Examples/StopwatchBenchmark` for a portable .NET prime-number benchmark using
`System.Diagnostics.Stopwatch` and `System.Console`. See `Examples/CopperBars`
for a PAL OCS system-takeover demo whose moving Copper effect performs no OS
calls and exits on the left mouse button.
