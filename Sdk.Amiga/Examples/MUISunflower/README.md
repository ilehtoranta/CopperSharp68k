# MUI Sunflower

Small `muimaster.library` example for `CopperSharp.Sdk.Amiga`.

The entry point builds this MUI object tree:

```text
Application.mui
  Window.mui
    Group.mui
      Text.mui
      MakeObject.Button
```

It attaches a `Window.CloseRequest` notification to
`Application.Method.ReturnID`, opens the window, runs
`Application.Method.Run`, and disposes the application object after the event
loop returns.

Build the managed example assembly:

```powershell
dotnet build .\Sdk.Amiga\Examples\MUISunflower\MUISunflower.csproj
```

The SDK models Amiga pointers as `uint` guest addresses. `CString.FromLiteral`
emits NUL-terminated guest strings, while the `MUI_NewObject()` and
`MUI_MakeObject()` `params uint[]` overloads are compiler-lowered to temporary
68k stack arrays. The example therefore does not need helper imports for
temporary taglists.
