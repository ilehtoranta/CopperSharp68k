using System.Buffers.Binary;
using Hst.Amiga;
using Hst.Amiga.FileSystems.FastFileSystem;
using Hst.Amiga.FileSystems.FastFileSystem.Blocks;
using Hst.Amiga.RigidDiskBlocks;
using AmigaFileMode = Hst.Amiga.FileSystems.FileMode;
using SystemDirectory = System.IO.Directory;
using SystemFile = System.IO.File;

namespace CopperSharp.Compiler.BootAdf;

public static class FileSystemAdf
{
    private const int BootBlockSize = 1024;
    private const int SectorSize = 512;

    public static async Task<byte[]> CreateAsync(
        string sourceDirectory,
        string volumeName)
    {
        if (!SystemDirectory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException(sourceDirectory);
        }

        using var image = new MemoryStream();
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
        return image.ToArray();
    }

    private static async Task RepairOfsRootBlockAsync(Stream image)
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

    private static async Task CopyDirectoryAsync(
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

}
