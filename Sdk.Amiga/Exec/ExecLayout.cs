/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Compile-time byte offsets for guest-resident Exec structures.</summary>
public static class ExecLayout
{
	public static class Node
	{
		public const int Successor = 0;
		public const int Predecessor = 4;
		public const int Type = 8;
		public const int Priority = 9;
		public const int Name = 10;
	}

	public static class List
	{
		public const int Head = 0;
		public const int Tail = 4;
		public const int TailPred = 8;
		public const int Type = 12;
		public const int Padding = 13;
	}

	public static class Library
	{
		public const int Node = 0;
		public const int Flags = 14;
		public const int Padding = 15;
		public const int NegativeSize = 16;
		public const int PositiveSize = 18;
		public const int Version = 20;
		public const int Revision = 22;
		public const int IdString = 24;
		public const int Checksum = 28;
		public const int OpenCount = 32;
	}

	public static class Device
	{
		public const int Library = 0;
	}

	public static class Message
	{
		public const int Node = 0;
		public const int ReplyPort = 14;
		public const int Length = 18;
	}

	public static class MsgPort
	{
		public const int Node = 0;
		public const int Flags = 14;
		public const int SignalBit = 15;
		public const int SignalTask = 16;
		public const int MessageList = 20;
	}

	public static class Unit
	{
		public const int MessagePort = 0;
		public const int Flags = 34;
		public const int Padding = 35;
		public const int OpenCount = 36;
	}

	public static class Interrupt
	{
		public const int Node = 0;
		public const int Data = 14;
		public const int Code = 18;
	}

	public static class IntVector
	{
		public const int Data = 0;
		public const int Code = 4;
		public const int Node = 8;
	}

	public static class SoftIntList
	{
		public const int List = 0;
		public const int Padding = 14;
	}

	public static class MemChunk
	{
		public const int Next = 0;
		public const int Bytes = 4;
	}

	public static class MemHeader
	{
		public const int Node = 0;
		public const int Attributes = 14;
		public const int First = 16;
		public const int Lower = 20;
		public const int Upper = 24;
		public const int Free = 28;
	}

	public static class MemEntry
	{
		public const int AddressOrRequirements = 0;
		public const int Length = 4;
	}

	public static class MemList
	{
		public const int Node = 0;
		public const int NumberOfEntries = 14;
		public const int FirstEntry = 16;
	}

	public static class MemHandlerData
	{
		public const int RequestSize = 0;
		public const int RequestFlags = 4;
		public const int Flags = 8;
	}

	public static class Task
	{
		public const int Node = 0;
		public const int Flags = 14;
		public const int State = 15;
		public const int IDNestCount = 16;
		public const int TaskDisableNestCount = 17;
		public const int SignalAllocated = 18;
		public const int SignalWait = 22;
		public const int SignalReceived = 26;
		public const int SignalException = 30;
		public const int TrapAllocated = 34;
		public const int TrapEnabled = 36;
		public const int ExceptionData = 38;
		public const int ExceptionCode = 42;
		public const int TrapData = 46;
		public const int TrapCode = 50;
		public const int StackPointer = 54;
		public const int StackLower = 58;
		public const int StackUpper = 62;
		public const int Switch = 66;
		public const int Launch = 70;
		public const int MemoryEntries = 74;
		public const int UserData = 88;
	}

	public static class StackSwapStruct
	{
		public const int Lower = 0;
		public const int Upper = 4;
		public const int Pointer = 8;
	}

	public static class IORequest
	{
		public const int Message = 0;
		public const int Device = 20;
		public const int Unit = 24;
		public const int Command = 28;
		public const int Flags = 30;
		public const int Error = 31;
	}

	public static class IOStdReq
	{
		public const int Message = 0;
		public const int Device = 20;
		public const int Unit = 24;
		public const int Command = 28;
		public const int Flags = 30;
		public const int Error = 31;
		public const int Actual = 32;
		public const int Length = 36;
		public const int Data = 40;
		public const int Offset = 44;
	}

	public static class Resident
	{
		public const int MatchWord = 0;
		public const int MatchTag = 2;
		public const int EndSkip = 6;
		public const int Flags = 10;
		public const int Version = 11;
		public const int Type = 12;
		public const int Priority = 13;
		public const int Name = 14;
		public const int IdString = 18;
		public const int Init = 22;
	}

	public static class MinList
	{
		public const int Head = 0;
		public const int Tail = 4;
		public const int TailPred = 8;
	}

	public static class SemaphoreRequest
	{
		public const int Link = 0;
		public const int Waiter = 8;
	}

	public static class SignalSemaphore
	{
		public const int Link = 0;
		public const int NestCount = 14;
		public const int WaitQueue = 16;
		public const int MultipleLink = 28;
		public const int Owner = 40;
		public const int QueueCount = 44;
	}

	public static class SemaphoreMessage
	{
		public const int Message = 0;
		public const int Semaphore = 20;
	}

	public static class Semaphore
	{
		public const int MessagePort = 0;
		public const int Bids = 34;
	}

	public static class ExecBase
	{
		public const int LibNode = 0;
		public const int SoftVer = 34;
		public const int IntVector0 = 84;
		public const int IntVector15 = 264;
		public const int ThisTask = 276;
		public const int IDNestCount = 294;
		public const int TaskDisableNestCount = 295;
		public const int ResModules = 300;
		public const int MemList = 322;
		public const int ResourceList = 336;
		public const int DeviceList = 350;
		public const int InterruptList = 364;
		public const int LibraryList = 378;
		public const int PortList = 392;
		public const int TaskReady = 406;
		public const int TaskWait = 420;
		public const int SoftInt0 = 434;
		public const int SoftInt4 = 498;
		public const int LastAlert = 514;
		public const int SemaphoreList = 532;
		public const int KickMemPtr = 546;
		public const int KickTagPtr = 550;
		public const int KickCheckSum = 554;
		public const int ExPad0 = 558;
		public const int ExReserved1 = 580;
		public const int ExMmuLock = 600;
		public const int ExReserved2 = 604;
		public const int ExMemHandlers = 616;
		public const int ExMemHandler = 628;
	}
}
