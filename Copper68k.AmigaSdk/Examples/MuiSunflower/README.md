# MUI Sunflower

Small `muimaster.library` example for `Copper68k.AmigaSdk`.

The entry point builds this MUI object tree:

```text
Application.mui
  Window.mui
    Group.mui
      Text.mui
      MUIO_Button
```

Build the managed example assembly:

```powershell
dotnet build .\Copper68k.AmigaSdk\Examples\MuiSunflower\MuiSunflower.csproj
```

The SDK models Amiga pointers as `uint` guest addresses. This example therefore
uses two runtime-supplied imports:

- `examples.cstring`: returns the guest address of a NUL-terminated string.
- `examples.u32array1`, `examples.u32array3`, `examples.u32array5`, and
  `examples.u32array13`: return the guest address of a contiguous ULONG array.

Those imports are deliberately local to the example. They keep the MUI binding
itself ABI-pure while still making the tag-list style readable.
