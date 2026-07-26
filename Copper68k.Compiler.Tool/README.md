# Copper68k.Compiler.Tool

Command-line driver for the optional `Copper68k.Compiler` CIL-to-Motorola 68k
ahead-of-time compiler.

```text
dotnet tool install Copper68k.Compiler.Tool
copper68kc Firmware.dll --entry Firmware.Boot::Main --cpu 68000 \
  --format hunk --output Firmware.hunk
copper68kc Firmware.dll --entry Firmware.Boot::Main \
  --format asm --output Firmware.s
```

Run `copper68kc --help` for ROM layout and fixed-address import options.
