/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

using CopperSharp.Compiler;

[M68kStackAlignment(4)]
[M68kUninitializedStorage]
public unsafe struct FileInfoBlock
{
	public const int SizeInBytes = 260;
	public const int FileNameOffset = 8;
	public const int CommentOffset = 144;
	public const int DirEntryTypeOffset = 4;
	public const int ProtectionOffset = 116;
	public const int SizeOffset = 124;
	public const int DateDaysOffset = 132;
	public const int DateMinuteOffset = 136;
	public const int DateTickOffset = 140;

	public static APTR AddressOf(ref FileInfoBlock fileInfoBlock) =>
		throw new System.NotSupportedException(
			"FileInfoBlock.AddressOf is lowered by CopperSharp.");

	public static CString FileName(uint fileInfoBlock) =>
		CString.FromPointer(fileInfoBlock + FileNameOffset);

	public static CString Comment(uint fileInfoBlock) =>
		CString.FromPointer(fileInfoBlock + CommentOffset);

	public static int GetDirEntryType(uint fileInfoBlock) =>
		ReadInt32(fileInfoBlock, DirEntryTypeOffset);

	public static int GetSize(uint fileInfoBlock) =>
		ReadInt32(fileInfoBlock, SizeOffset);

	public static int GetProtection(uint fileInfoBlock) =>
		ReadInt32(fileInfoBlock, ProtectionOffset);

	public static int GetDateDays(uint fileInfoBlock) =>
		ReadInt32(fileInfoBlock, DateDaysOffset);

	public static int GetDateMinute(uint fileInfoBlock) =>
		ReadInt32(fileInfoBlock, DateMinuteOffset);

	public static int GetDateTick(uint fileInfoBlock) =>
		ReadInt32(fileInfoBlock, DateTickOffset);

	private static int ReadInt32(uint fileInfoBlock, int offset) =>
		(int)APTR.ReadUInt32(APTR.FromPointer(fileInfoBlock), offset);

	public int DiskKey;
	public int DirEntryType;
	public fixed byte FileNameBuffer[108];
	public int Protection;
	public int EntryType;
	public int Size;
	public int NumBlocks;
	public int DateDays;
	public int DateMinute;
	public int DateTick;
	public fixed byte CommentBuffer[80];
	public uint Owner;
	public uint ReservedLong000;
	public uint ReservedLong001;
	public uint ReservedLong002;
	public uint ReservedLong003;
	public uint ReservedLong004;
	public uint ReservedLong005;
	public uint ReservedLong006;
	public uint ReservedLong007;
}
