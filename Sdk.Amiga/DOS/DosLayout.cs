/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Published byte offsets for guest-memory AmigaDOS structures.</summary>
public static class DosLayout
{
	public static class ExAllControl
	{
		public const int Entries = 0, LastKey = 4, MatchString = 8,
			MatchFunction = 12;
		public const int Size = 16;
	}

	public static class DosAttrBuffer
	{
		public const int Pointer = 0, Length = 4, Size = 8;
	}

	public static class DateStamp
	{
		public const int Days = 0, Minutes = 4, Ticks = 8;
		public const int Size = 12;
	}

	public static class PosixDateStamp
	{
		public const int Seconds = 0, Nanoseconds = 8;
		public const int Size = 12;
	}

	public static class CSource
	{
		public const int Buffer = 0, Length = 4, CurrentCharacter = 8;
		public const int Size = 12;
	}

	public static class RDArgs
	{
		public const int Source = 0, AllocationList = 12, Buffer = 16,
			BufferSize = 20, ExtendedHelp = 24, Flags = 28;
		public const int Size = 32;
	}

	public static class RecordLock
	{
		public const int File = 0, Offset = 4, Length = 8, Mode = 12;
		public const int Size = 16;
	}

	public static class RecordLock64
	{
		public const int File = 0, Offset = 4, Length = 12, Mode = 20;
		public const int Size = 24;
	}

	public static class FileHandle
	{
		public const int Link = 0, Port = 4, Type = 8, Buffer = 12, Position = 16;
		public const int End = 20, Functions = 24, Function2 = 28, Function3 = 32;
		public const int Arguments = 36, Argument2 = 40;
		public const int Size = 44;
	}

	public static class DosPacket
	{
		public const int Link = 0, Port = 4, Type = 8, Result1 = 12, Result2 = 16;
		public const int Argument1 = 20, Argument2 = 24, Argument3 = 28, Argument4 = 32;
		public const int Argument5 = 36, Argument6 = 40, Argument7 = 44;
		public const int Size = 48;
	}

	public static class StandardPacket
	{
		public const int Message = 0, Packet = 20;
		public const int Size = 68;
	}

	public static class FileLock
	{
		public const int Link = 0, Key = 4, Access = 8, Task = 12, Volume = 16;
		public const int Size = 20;
	}

	public static class CommandLineInterface
	{
		public const int Result2 = 0, CurrentDirectoryName = 4, CommandDirectory = 8;
		public const int ReturnCode = 12, CommandName = 16, FailLevel = 20, Prompt = 24;
		public const int StandardInput = 28, CurrentInput = 32, CommandFile = 36;
		public const int Interactive = 40, Background = 44, CurrentOutput = 48;
		public const int DefaultStack = 52, StandardOutput = 56, Module = 60;
		public const int Size = 64;
	}

	public static class RootNode
	{
		public const int TaskArray = 0, ConsoleSegment = 4, Time = 8,
			RestartSegment = 20, Info = 24, FileHandlerSegment = 28,
			CliList = 32, BootProcess = 44, ShellSegment = 48, Flags = 52;
		public const int Size = 56;
	}

	public static class DosInfo
	{
		public const int ResidentList = 0, DeviceInfo = 4, Devices = 8,
			Handlers = 12, NetworkHandler = 16, DeviceLock = 20,
			EntryLock = 66, DeleteLock = 112;
		public const int Size = 158;
	}

	public static class Process
	{
		public const int Task = 0, MessagePort = 92, Padding = 126,
			SegmentList = 128, StackSize = 132, GlobalVector = 136,
			TaskNumber = 140, StackBase = 144, Result2 = 148,
			CurrentDirectory = 152, CurrentInput = 156, CurrentOutput = 160,
			ConsoleTask = 164, FileSystemTask = 168,
			CommandLineInterface = 172, ReturnAddress = 176, PacketWait = 180,
			WindowPointer = 184, HomeDirectory = 188, Flags = 192,
			ExitCode = 196, ExitData = 200, Arguments = 204,
			LocalVariables = 208, ShellPrivate = 220, CurrentError = 224;
		public const int Size = 228;
	}

	public static class DosList
	{
		public const int Next = 0, Type = 4, Task = 8, Lock = 12, Misc = 16, Name = 40;
		public const int Size = 44;
	}

	public static class DevProc
	{
		public const int Port = 0, Lock = 4, Flags = 8, DeviceNode = 12;
		public const int Size = 16;
	}

	public static class AssignList
	{
		public const int Next = 0, Lock = 4;
		public const int Size = 8;
	}
}
