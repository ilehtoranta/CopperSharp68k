using System.Buffers.Binary;
using System.Text;
using CopperSharp.Compiler.BootAdf;
using CopperSharp.Compiler.NativeOsRunner;

namespace CopperSharp.Compiler.NativeOsRunner.Tests;

public sealed class NativeOsProtocolTests
{
    [Theory]
    [InlineData("../INPUT")]
    [InlineData("C:INPUT")]
    [InlineData("result.bin")]
    [InlineData("STDOUT.TXT")]
    [InlineData("PROGRAM.")]
    [InlineData("S")]
    public async Task InputFilesCannotEscapeStagingOrReplaceRunnerFiles(string name)
    {
        var rom = new byte[524288];
        BinaryPrimitives.WriteUInt16BigEndian(rom.AsSpan(12), 37);
        await Assert.ThrowsAsync<ArgumentException>(() => NativeOsSession.RunAsync(
            [0, 0, 3, 0xF3], rom, "", Copper68k.M68kCpuModel.M68000, false, 1,
            TimeSpan.FromSeconds(1), inputFiles: new Dictionary<string, byte[]> { [name] = [] }));
    }

    [Fact]
    public async Task InputFilesRejectCaseInsensitiveDuplicates()
    {
        var rom = new byte[524288];
        BinaryPrimitives.WriteUInt16BigEndian(rom.AsSpan(12), 37);
        await Assert.ThrowsAsync<ArgumentException>(() => NativeOsSession.RunAsync(
            [0, 0, 3, 0xF3], rom, "", Copper68k.M68kCpuModel.M68000, false, 1,
            TimeSpan.FromSeconds(1), inputFiles: new Dictionary<string, byte[]> { ["Input.bin"] = [], ["INPUT.BIN"] = [] }));
    }

    [Fact]
    public void CompletionRequiresMatchingRunAndPreservesAllReturnBits()
    {
        var id = new string('a', 32);
        var report = new byte[48];
        BinaryPrimitives.WriteUInt32BigEndian(report, 0x43534f53);
        BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(4), 1);
        Encoding.ASCII.GetBytes(id).CopyTo(report, 8);
        BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(44), 0xdeadbeef);
        Assert.True(NativeOsSession.TryReadResult(report, id, out var status, out var answer));
        Assert.Equal(0u, status);
        Assert.Equal(0xdeadbeefu, answer);
        Assert.False(NativeOsSession.TryReadResult(report, new string('b', 32), out _, out _));
        Assert.False(NativeOsSession.TryReadResult(report[..47], id, out _, out _));
        report[7] = 2;
        Assert.False(NativeOsSession.TryReadResult(report, id, out _, out _));
    }

    [Fact]
    public async Task RamAdfContainsNativeBootBlockAndRoundTripsBinaryPayload()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CopperSharpNativeOsTests"));
        var stage = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(stage, "S"));
        try
        {
            var payload = Enumerable.Range(0, 1500).Select(i => (byte)i).ToArray();
            await File.WriteAllBytesAsync(Path.Combine(stage, "PROGRAM"), payload);
            await File.WriteAllTextAsync(Path.Combine(stage, "S", "startup-sequence"), "LAUNCH\n");
            var image = await FileSystemAdf.CreateAsync(stage, "NativeTest");
            Assert.Equal(901120, image.Length);
            Assert.Equal("DOS\0"u8.ToArray(), image[..4]);
            Assert.Equal(880u, BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(8)));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(880 * 512 + 504)));
            var sum = 0u;
            for (var offset = 880 * 512; offset < 881 * 512; offset += 4)
                sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(offset)));
            Assert.Equal(0u, sum);
            Assert.Equal(payload, await NativeOsSession.ReadDiskFileAsync(image, "PROGRAM"));
            Assert.Null(await NativeOsSession.ReadDiskFileAsync(image, "RESULT.BIN"));
            using var stream = new MemoryStream(image);
            await using var volume = await Hst.Amiga.FileSystems.FastFileSystem.FastFileSystemVolume.MountAdf(stream);
            await volume.ChangeDirectory("S");
            await using var startup = await volume.OpenFile("startup-sequence", Hst.Amiga.FileSystems.FileMode.Read);
            using var reader = new StreamReader(startup);
            Assert.Equal("LAUNCH\n", await reader.ReadToEndAsync());
        }
        finally
        {
            Assert.StartsWith(root + Path.DirectorySeparatorChar, Path.GetFullPath(stage));
            Directory.Delete(stage, true);
        }
    }
}
