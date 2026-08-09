using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using CopperSharp.Compiler;
using CopperSharp.Targets.Amiga;
using Hst.Amiga;
using Hst.Amiga.FileSystems.FastFileSystem;
using Hst.Amiga.FileSystems.FastFileSystem.Blocks;
using Hst.Amiga.RigidDiskBlocks;
using AmigaFileMode = Hst.Amiga.FileSystems.FileMode;
using SystemDirectory = System.IO.Directory;
using SystemFile = System.IO.File;

const int AdfSize = 901_120;
const int BootBlockSize = 1_024;
const int SectorSize = 512;
const uint LoadAddress = 0x0007_0000;
const uint StatusAddress = LoadAddress - 16;
const uint EnteredValue = 0x424F_4F54; // "BOOT"
const uint SuccessValue = 0x4353_4850; // "CSHP"
const string SuccessMarker = "COPPERSHARP68K_BOOT_OK";
const string FailureMarker = "COPPERSHARP68K_BOOT_FAIL";

if (args is ["--filesystem", var sourceDirectory, var filesystemOutput, ..])
{
    var filesystemOptions = args.Skip(3).ToArray();
    var volumeName = GetOption(filesystemOptions, "--volume") ?? "CopperSharp68k";
    await CreateFileSystemAdfAsync(
        Path.GetFullPath(sourceDirectory),
        Path.GetFullPath(filesystemOutput),
        volumeName);

    Console.WriteLine($"Created bootable AmigaDOS OFS ADF '{Path.GetFullPath(filesystemOutput)}'.");
    return 0;
}

if (args.Length < 3)
{
    Console.Error.WriteLine(
        "Usage: CopperSharp.Compiler.BootAdf <assembly> <entry> <output.adf> " +
        "[--managed-amiga] [--cpu m68000|m68040] [--fpu disabled|m68040] [--success-value hex]\n" +
        "   or: CopperSharp.Compiler.BootAdf --filesystem <source-directory> <output.adf> [--volume name]");
    return 2;
}

var options = args.Skip(3).ToArray();
var managedAmiga = options.Contains("--managed-amiga", StringComparer.Ordinal);
string? Option(string name)
{
    return GetOption(options, name);
}

var cpu = Option("--cpu")?.ToLowerInvariant() switch
{
    null or "m68000" => M68kCpuTarget.M68000,
    "m68040" => M68kCpuTarget.M68040,
    var value => throw new ArgumentException($"Unsupported CPU '{value}'.")
};
var floatingPoint = Option("--fpu")?.ToLowerInvariant() switch
{
    null or "disabled" => M68kFloatingPointMode.Disabled,
    "m68040" => M68kFloatingPointMode.M68040,
    var value => throw new ArgumentException($"Unsupported FPU '{value}'.")
};
var successValueText = Option("--success-value");
var successValue = successValueText is null
    ? SuccessValue
    : Convert.ToUInt32(successValueText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? successValueText[2..] : successValueText, 16);
var request = new M68kCompilationRequest
{
    AssemblyPath = Path.GetFullPath(args[0]),
    EntryPoint = args[1],
    Cpu = cpu,
    FloatingPoint = floatingPoint,
    OutputFormat = M68kOutputFormat.Hunk,
    RuntimeProfile = managedAmiga ? M68kRuntimeProfile.Application : M68kRuntimeProfile.Freestanding,
    ExceptionMode = M68kExceptionMode.Yolo,
    MemoryManagement = managedAmiga
        ? M68kMemoryManagement.ManagedPoolMarkSweepGc
        : M68kMemoryManagement.None,
    Heap = managedAmiga ? new M68kHeapOptions { Size = 0x0000_2000 } : new M68kHeapOptions()
};
var result = managedAmiga ? AmigaM68kCompiler.Compile(request) : M68kCompiler.Compile(request);

var wrapper = BuildWrapper(result.EntryPoint, successValue);
var codeAddress = checked(LoadAddress + (uint)wrapper.Length);
var code = (byte[])result.Code.Clone();
foreach (var relocation in result.Relocations)
{
    var value = BinaryPrimitives.ReadUInt32BigEndian(code.AsSpan(relocation.Offset, 4));
    BinaryPrimitives.WriteUInt32BigEndian(code.AsSpan(relocation.Offset, 4), checked(value + codeAddress));
}

var payloadLength = checked(wrapper.Length + code.Length);
var transferLength = checked((payloadLength + SectorSize - 1) / SectorSize * SectorSize);
if (BootBlockSize + transferLength > AdfSize - SectorSize)
{
    throw new InvalidOperationException($"Boot payload is too large for the ADF: {payloadLength} bytes.");
}

var image = new byte[AdfSize];
BuildBootBlock(image.AsSpan(0, BootBlockSize), transferLength);
wrapper.CopyTo(image.AsSpan(BootBlockSize));
code.CopyTo(image.AsSpan(BootBlockSize + wrapper.Length));

var outputPath = Path.GetFullPath(args[2]);
SystemDirectory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
SystemFile.WriteAllBytes(outputPath, image);
Console.WriteLine(
    $"Created {AdfSize}-byte boot ADF: payload={payloadLength}, transfer={transferLength}, " +
    $"entry=${codeAddress + result.EntryPoint:X8}, relocations={result.Relocations.Count}.");
return 0;

static string? GetOption(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
}

static async Task CreateFileSystemAdfAsync(
    string sourceDirectory,
    string outputPath,
    string volumeName)
{
    if (!SystemDirectory.Exists(sourceDirectory))
    {
        throw new DirectoryNotFoundException(sourceDirectory);
    }

    SystemDirectory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    await using var image = new FileStream(
        outputPath,
        FileMode.Create,
        FileAccess.ReadWrite,
        FileShare.Read,
        bufferSize: 4096,
        FileOptions.Asynchronous);
    image.SetLength(FloppyDiskConstants.DoubleDensity.Size);
    await FastFileSystemFormatter.Format(
        image,
        FloppyDiskConstants.DoubleDensity.LowCyl,
        FloppyDiskConstants.DoubleDensity.HighCyl,
        FloppyDiskConstants.DoubleDensity.ReservedBlocks,
        FloppyDiskConstants.DoubleDensity.Heads,
        FloppyDiskConstants.DoubleDensity.Sectors,
        FloppyDiskConstants.BlockSize,
        FloppyDiskConstants.FileSystemBlockSize,
        DosTypeHelper.FormatDosType("DOS0"),
        volumeName);

    var bootBlock = BootBlockBuilder.Build(
        new BootBlock
        {
            DosType = DosTypeHelper.FormatDosType("DOS0"),
            RootBlockOffset = 880
        },
        BootBlockSize);
    image.Position = 0;
    await image.WriteAsync(bootBlock);

    await using var volume = await FastFileSystemVolume.MountAdf(image);
    await CopyDirectoryAsync(volume, sourceDirectory);
    await volume.Flush();
    await RepairOfsRootBlockAsync(image);
    await image.FlushAsync();
}

static async Task RepairOfsRootBlockAsync(Stream image)
{
    // Hst.Amiga uses the root block's extension field for its newer FFS
    // layouts. Classic DOS\0 requires this field to be zero; Kickstart 1.3
    // otherwise rejects an otherwise valid OFS disk as non-bootable.
    var block = new byte[SectorSize];
    image.Position = 880L * SectorSize;
    await image.ReadExactlyAsync(block);
    BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(SectorSize - 8), 0);
    BinaryPrimitives.WriteInt32BigEndian(block.AsSpan(20), 0);
    var sum = 0;
    for (var offset = 0; offset < block.Length; offset += 4)
    {
        sum = unchecked(sum + BinaryPrimitives.ReadInt32BigEndian(block.AsSpan(offset, 4)));
    }
    BinaryPrimitives.WriteInt32BigEndian(block.AsSpan(20), unchecked(-sum));
    image.Position = 880L * SectorSize;
    await image.WriteAsync(block);
}

static async Task CopyDirectoryAsync(
    FastFileSystemVolume volume,
    string sourceDirectory)
{
    foreach (var filePath in SystemDirectory.EnumerateFiles(sourceDirectory).Order(StringComparer.Ordinal))
    {
        var fileName = Path.GetFileName(filePath);
        await using var destination = await volume.OpenFile(
            fileName,
            AmigaFileMode.Write,
            overwrite: true,
            ignoreProtectionBits: true);
        await using var source = SystemFile.OpenRead(filePath);
        await source.CopyToAsync(destination);
    }

    foreach (var directoryPath in SystemDirectory.EnumerateDirectories(sourceDirectory).Order(StringComparer.Ordinal))
    {
        var directoryName = Path.GetFileName(directoryPath);
        var parentPath = await volume.GetCurrentPath();
        await volume.CreateDirectory(directoryName);
        await volume.ChangeDirectory(directoryName);
        await CopyDirectoryAsync(volume, directoryPath);
        await volume.ChangeDirectory(parentPath);
    }
}

static void BuildBootBlock(Span<byte> block, int transferLength)
{
    "DOS\0"u8.CopyTo(block);
    BinaryPrimitives.WriteUInt32BigEndian(block[8..], 880);
    var code = new byte[]
    {
        0x4B, 0xF9, 0x00, 0x07, 0x00, 0x00,             // lea $70000,a5
        0x23, 0x7C, 0, 0, 0, 0, 0x00, 0x24,             // move.l #length,$24(a1)
        0x23, 0x4D, 0x00, 0x28,                         // move.l a5,$28(a1)
        0x23, 0x7C, 0, 0, 0x04, 0x00, 0x00, 0x2C,       // move.l #1024,$2c(a1)
        0x4E, 0xAE, 0xFE, 0x38,                         // jsr -456(a6) ; DoIO
        0x23, 0x7C, 0, 0, 0, 0, 0x00, 0x24,             // move.l #0,$24(a1)
        0x33, 0x7C, 0, 9, 0x00, 0x1C,                   // move.w #9,$1c(a1) ; motor off
        0x4E, 0xAE, 0xFE, 0x38,                         // jsr -456(a6)
        0x4E, 0xF9, 0x00, 0x07, 0x00, 0x00              // jmp $70000
    };
    BinaryPrimitives.WriteUInt32BigEndian(code.AsSpan(8, 4), (uint)transferLength);
    code.CopyTo(block[12..]);

    uint sum = 0;
    for (var offset = 0; offset < BootBlockSize; offset += 4)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(block[offset..]);
        var next = sum + value;
        if (next < sum) next++;
        sum = next;
    }
    BinaryPrimitives.WriteUInt32BigEndian(block[4..], ~sum);
}

static byte[] BuildWrapper(uint entryOffset, uint successValue)
{
    var bytes = new List<byte>();
    var labels = new Dictionary<string, int>(StringComparer.Ordinal);
    var patches = new List<(int Offset, string Label)>();

    void Word(ushort value) { bytes.Add((byte)(value >> 8)); bytes.Add((byte)value); }
    void Long(uint value) { Word((ushort)(value >> 16)); Word((ushort)value); }
    void Label(string name) => labels.Add(name, bytes.Count);
    void Branch(ushort opcode, string label) { Word(opcode); patches.Add((bytes.Count, label)); Word(0); }
    void LeaPc(string label) { Word(0x41FA); patches.Add((bytes.Count, label)); Word(0); }

    Word(0x33FC); Word(0x000F); Long(0x00DF_F180);     // blue: wrapper entered
    Word(0x23FC); Long(EnteredValue); Long(StatusAddress);
    Word(0x2F09);                                      // move.l a1,-(sp)
    Word(0x2F0E);                                      // move.l a6,-(sp)
    Word(0x4EB9);
    var entryAddressPatch = bytes.Count;
    Long(checked(LoadAddress + entryOffset));           // patched after size is known
    Word(0x2C5F);                                      // move.l (sp)+,a6
    Word(0x225F);                                      // move.l (sp)+,a1
    Word(0x23C0); Long(StatusAddress);                  // expose returned d0 to headless runners
    Word(0x0C80); Long(successValue);                  // cmpi.l #expected,d0
    Branch(0x6700, "success");                        // beq.w success
    Word(0x33FC); Word(0x0F00); Long(0x00DF_F180);     // red failure screen
    LeaPc("failureMarker");
    Branch(0x6000, "write");
    Label("success");
    Word(0x33FC); Word(0x00F0); Long(0x00DF_F180);     // green success screen
    LeaPc("successMarker");
    Label("write");
    Word(0x237C); Long(SectorSize); Word(0x0024);       // io_Length
    Word(0x2348); Word(0x0028);                        // io_Data = a0
    Word(0x237C); Long(AdfSize - SectorSize); Word(0x002C); // io_Offset
    Word(0x337C); Word(0x0003); Word(0x001C);           // CMD_WRITE
    Word(0x4EAE); Word(0xFE38);                        // DoIO
    Word(0x60FE);                                      // bra.s *
    Label("successMarker");
    bytes.AddRange(PaddedMarker(SuccessMarker));
    Label("failureMarker");
    bytes.AddRange(PaddedMarker(FailureMarker));

    if ((bytes.Count & 1) != 0) bytes.Add(0);
    var wrapperSize = bytes.Count;
    var entryAddress = checked(LoadAddress + (uint)wrapperSize + entryOffset);
    BinaryPrimitives.WriteUInt32BigEndian(CollectionsMarshal.AsSpan(bytes).Slice(entryAddressPatch, 4), entryAddress);
    foreach (var patch in patches)
    {
        var displacement = checked(labels[patch.Label] - (patch.Offset + 2));
        BinaryPrimitives.WriteInt16BigEndian(CollectionsMarshal.AsSpan(bytes).Slice(patch.Offset, 2), checked((short)displacement));
    }
    return bytes.ToArray();

    static byte[] PaddedMarker(string marker)
    {
        var data = new byte[SectorSize];
        Encoding.ASCII.GetBytes(marker).CopyTo(data, 0);
        return data;
    }
}
