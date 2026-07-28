/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public unsafe struct FileInfoBlock
{
	public const int SizeInBytes = 260;
	public const int AlignedStackStorageLongs = 66;
	public const int FileNameOffset = 8;
	public const int CommentOffset = 144;
	public const int DirEntryTypeOffset = 4;
	public const int SizeOffset = 124;
	public const int DateDaysOffset = 132;
	public const int DateMinuteOffset = 136;
	public const int DateTickOffset = 140;

	public static CString FileName(uint fileInfoBlock) =>
		CString.FromPointer(fileInfoBlock + FileNameOffset);

	public static CString Comment(uint fileInfoBlock) =>
		CString.FromPointer(fileInfoBlock + CommentOffset);

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
