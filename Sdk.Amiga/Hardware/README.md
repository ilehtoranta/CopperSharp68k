# Amiga hardware register catalog

This namespace is the low-level, allocation-free hardware layer. It does not
open libraries, arbitrate with AmigaOS, probe expansion cards, or make register
access safe. Applications must take ownership of each affected subsystem.

## Shared chips

- `CustomReadRegister`, `CustomWriteRegister`, `CustomPointerRegister`, and
  `CustomStrobeRegister` cover the documented 16-bit custom-register aperture.
- `CustomReservedRegister` names the three published ECS placeholders that are
  absent on OCS, ECS, and AGA, without making them writeable.
- `CustomRegisterCatalog` distinguishes OCS, ECS, and AGA availability.
- `CiaRegister` covers all sixteen MOS 8520 registers for both `CiaA` and
  `CiaB`, including the Amiga 0x100-byte register spacing.
- Typed set/clear flag enums preserve DMACON, INTENA/INTREQ, ADKCON, and CIA
  interrupt semantics.

## Model-specific chips

| Hardware | SDK type | Built-in systems |
| --- | --- | --- |
| A2000-style RTC | `RealTimeClock2000` | A2000 family, A500 Plus, CDTV |
| A3000-style RTC | `RealTimeClock3000` | A3000 and A4000 families |
| Gayle/PCMCIA | `Gayle` | A600, A1200 |
| Gayle-compatible PATA | `GayleIde` | A600, A1200, A4000 family |
| Fat Gary/Ramsey resource window | `MotherboardResources` | A3000/A4000 family |
| CDTV DMAC/CD controller | `CdtvDmac` | CDTV |
| Akiko | `Akiko` | CD32 |

`AmigaHardware.GetFeatures()` reports the standard built-in configuration.
Socketed custom chips, third-party accelerators, Zorro cards, and board
revisions can change the actual hardware. Use Expansion/Autoconfig discovery
before touching add-on hardware and use `HardwareBus` only with a verified
physical address and access width.

The Gayle register definitions are known to be partly undocumented. Their
names and layouts follow the long-standing system software interface, but code
must not infer their presence from an address responding on the bus.
