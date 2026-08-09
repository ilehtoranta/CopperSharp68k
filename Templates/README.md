# CopperSharp.Templates

Project templates for C# programs compiled to Motorola 68k Amiga output.

## Install

Install the templates. The generated project restores its compiler through the
`CopperSharp.Sdk` project SDK, so a global compiler tool is optional:

```text
dotnet new install CopperSharp.Templates::0.1.0-preview.1
```

## Create an Amiga program

```text
dotnet new amiga -n HelloAmiga
cd HelloAmiga
dotnet publish -r amiga-m68k
```

Publish writes `HelloAmiga.hunk`, its symbol/provenance map, and its exact
framework compatibility report under the standard publish directory. The
default target is MC68000. Select another
supported CPU when creating the project:

```text
dotnet new amiga -n HelloAmiga --cpu 68040
```

The generated project contains ordinary C# source, explicit package
references, the CopperSharp entry point, and the `amiga-m68k` publish RID.
