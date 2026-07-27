# DOS

Small `dos.library` example for `CopperSharp.Sdk.Amiga`.

The entry point receives the native Amiga startup arguments in `d0/a0`, opens
`dos.library` manually through `exec.library`, stores the returned base in the
SDK's manual DOS base slot, and lists a directory using Kickstart 1.3 style
`Lock()`, `Examine()`, and `ExNext()` calls.

If an argument is supplied, it is used as the path. With no argument, the
example locks the current directory. The `FileInfoBlock` backing storage is
stack allocated with longword-alignment slack because Amiga only guarantees
word-aligned stack addresses.

Build the managed example assembly:

```powershell
dotnet build .\Sdk.Amiga\Examples\DOS\DOS.csproj
```

`DOS.Printf()` is a CopperSharp-lowered convenience over the real
`DOS.VPrintf()` vector. Its inline `params uint[]` values are written to a
temporary 68k stack array, and the argument-array register receives that stack
address.
