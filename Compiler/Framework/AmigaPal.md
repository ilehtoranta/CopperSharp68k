# Amiga platform abstraction layer

## Purpose

The Amiga PAL implements the operating-system-dependent members admitted by the .NET 10 profile. It is a narrow runtime boundary between public framework semantics and AmigaOS calls.

`Runtime.AmigaPal` is separate from both the compiler's Amiga output backend and the public `Sdk.Amiga` declarations. Framework implementations depend on PAL operations, not directly on SDK library bindings. This keeps the core profile portable and makes output-profile ownership explicit.

## General rules

- Every PAL entry is selected by an exact framework binding.
- Parameters and results retain managed semantics at the public boundary.
- Native strings use explicit validated Latin-1 conversion where a C string is required.
- Native handles, buffers, messages, locks, and devices have a named owner and deterministic cleanup.
- Application output may cache process-lifetime resources lazily; freestanding and ROM-oriented output uses scoped or statically provisioned resources.
- Unused PAL feature groups contribute no code, data, handles, or startup work.
- Platform failures map to the documented official exception identity rather than raw Amiga error codes.

## Console

The admitted console surface includes selected `Write` and `WriteLine` overloads for strings, characters, booleans, and signed or unsigned integers, plus `Read` and `ReadLine`.

Output is sent through DOS gateways. Managed string output carries an explicit byte count after Latin-1 validation, so embedded zero characters are written rather than treated as terminators. Integer formatting uses a packed target-side conversion path and does not allocate a managed intermediate string.

Application output lazily acquires and caches the required DOS state. Freestanding output uses the resources supplied by its host profile. All error paths clean up resources acquired by the current operation before throwing.

Console input uses a native line buffer owned by the application PAL. The current bounded buffer is 128 bytes. Line parsing recognizes LF, CRLF, and lone CR; EOF state is cached consistently across `Read` and `ReadLine`. `Read` consumes decoded characters without a managed allocation, while `ReadLine` allocates the returned managed UTF-16 string.

Stream replacement, redirection, arbitrary encodings, and the full `Console.In`/`Out`/`Error` object model are not implied by this surface.

## Filesystem

The admitted surface includes selected operations corresponding to:

- `File.Exists`, `File.Delete`, `File.GetAttributes`, and `File.SetAttributes`;
- `Directory.Exists`, `Directory.Delete`, and `Directory.Move`.

Paths cross the PAL through a scoped validated C-string buffer. Application mode lazily opens the DOS library; scoped profiles release it after the operation.

Existence and kind checks use Amiga locks and examination data. Locks are released before returning or throwing. Operations distinguish a missing path, wrong path kind, protection failure, allocation failure, and other I/O errors so the public exception behavior remains stable.

`File.Delete` treats an absent file as success. Directory deletion reports a missing directory and rejects file paths. `Directory.Move` uses the Amiga rename operation and therefore guarantees same-volume rename semantics only; it is not a cross-volume copy-and-delete implementation.

Attribute mapping covers the admitted .NET flags such as `Directory`, `ReparsePoint`, `ReadOnly`, and `Archive`. `SetAttributes` preserves Amiga protection bits that the public operation does not own. Defined but unrepresentable flags are handled according to the binding contract; undefined flag bits produce `ArgumentException`.

Directory creation, recursive deletion, general enumeration, file streams, and cross-volume moves require additional explicit bindings and are currently outside this PAL contract.

## Environment

The admitted environment values are static platform facts:

- `Environment.NewLine` is the managed LF string;
- `Environment.ProcessorCount` is 1.

They require no Amiga library and must not be folded from the build host's environment.

## Clock and `Stopwatch`

The monotonic clock uses `timer.device` `ReadEClock`. The PAL returns the raw high/low counter value and the device-reported frequency. `Stopwatch.GetTimestamp`, `Frequency`, `IsHighResolution`, and the admitted instance state machine are built on that pair without host-time substitution.

Application output lazily creates a message port and I/O request, opens the device once, and closes it during PAL shutdown. Freestanding profiles use scoped or caller-provided storage. Partially initialized resources are unwound in reverse order on failure.

Members that convert elapsed ticks to a complete `TimeSpan` surface are not admitted until their scaling, overflow, and type dependencies are implemented explicitly.

## Feature groups

PAL dependencies are recorded independently in map and provenance output:

- `amiga-console`;
- `amiga-console-input`;
- `amiga-filesystem`;
- `amiga-environment`;
- `amiga-clock`.

This granularity is part of the pay-for-play contract. For example, using `Environment.NewLine` must not open DOS, and using console output must not pull in console input or `timer.device`.

## Error and cleanup discipline

PAL code follows a single ownership rule: the layer that successfully acquires a native resource owns it until ownership is explicitly transferred. Cleanup is idempotent where shutdown paths may be repeated. A managed exception is created only after native cleanup required by that path has completed.

PAL calls that can block or allocate are declared as such in the binding registry so safepoint and liveness analysis remains correct.
