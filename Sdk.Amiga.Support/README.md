# CopperSharp.Sdk.Amiga.Support

Host-side support for tools, emulators, and tests that inspect an Amiga 68k
address space.

This package contains the guest-memory abstraction, big-endian structure
codecs, and C-string helpers. Programs compiled for Amiga normally do not need
this package; they should reference `CopperSharp.Sdk.Amiga` and use its typed
ABI declarations directly.
