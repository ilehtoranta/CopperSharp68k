/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.InteropServices;

namespace Amiga;

public static class TrackDiskDevice
{
	public const string Name = "trackdisk.device";
	public const short Open = -6;
	public const short Close = -12;
	public const short Expunge = -18;
	public const short ExtFunc = -24;
	public const short BeginIO = -30;
	public const short AbortIO = -36;
	public const uint SectorSize = 512;
	public const uint SectorShift = 9;
	public const uint LabelSize = 16;
}

public enum TrackDiskCommand : ushort
{
	Read = 2, Write = 3, Update = 4, Clear = 5, Flush = 8,
	Motor = 9, Seek = 10, Format = 11, Remove = 12, ChangeNumber = 13,
	ChangeState = 14, ProtectionStatus = 15, RawRead = 16, RawWrite = 17,
	GetDriveType = 18, GetNumberOfTracks = 19, AddChangeInterrupt = 20,
	RemoveChangeInterrupt = 21, GetGeometry = 22, Eject = 23,
	Read64 = 24, Write64 = 25, Seek64 = 26, Format64 = 27,
}

[Flags]
public enum TrackDiskIoFlags : byte { None = 0, IndexSync = 1 << 4, WordSync = 1 << 5 }
[Flags]
public enum TrackDiskOpenFlags : byte { None = 0, AllowNon35 = 1 << 0 }
public enum TrackDiskDriveType : uint { Drive35 = 1, Drive325 = 2, Drive35At150Rpm = 3 }
public enum TrackDiskError : byte { NotSpecified = 20, NoSectorHeader = 21, BadSectorPreamble = 22, BadSectorId = 23, BadHeaderChecksum = 24, BadSectorChecksum = 25, TooFewSectors = 26, BadSectorHeader = 27, WriteProtected = 28, DiskChanged = 29, SeekError = 30, NoMemory = 31, BadUnitNumber = 32, BadDriveType = 33, DriveInUse = 34, PostReset = 35 }
public enum DriveGeometryDeviceType : byte { DirectAccess = 0, SequentialAccess = 1, Printer = 2, Processor = 3, Worm = 4, CdRom = 5, Scanner = 6, OpticalDisk = 7, MediumChanger = 8, Communication = 9, Unknown = 31 }
[Flags]
public enum DriveGeometryFlags : byte { None = 0, Removable = 1 << 0 }

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IOExtTD { public const uint Size = 56; public IOStdReq Request; public uint Count; public APTR SectorLabel; }
public static class IOExtTDLayout { public const int Request = 0, Count = 48, SectorLabel = 52; }

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DriveGeometry
{
	public const uint Size = 32;
	public uint SectorSize, TotalSectors, Cylinders, CylinderSectors, Heads, TrackSectors;
	public Exec.MemoryFlags BufferMemoryType;
	public DriveGeometryDeviceType DeviceType;
	public DriveGeometryFlags Flags;
	public ushort Reserved;
}
public static class DriveGeometryLayout
{
	public const int SectorSize = 0, TotalSectors = 4, Cylinders = 8, CylinderSectors = 12,
		Heads = 16, TrackSectors = 20, BufferMemoryType = 24, DeviceType = 28, Flags = 29, Reserved = 30;
}
