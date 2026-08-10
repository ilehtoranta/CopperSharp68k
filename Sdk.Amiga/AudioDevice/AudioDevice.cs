using System.Runtime.InteropServices;

namespace Amiga;

public static class AudioDevice
{
	public const string Name = "audio.device";
	public const int HardwareChannels = 4;
}

public enum AudioCommand : ushort
{
	Reset = 1, Read = 2, Write = 3, Start = 6, Stop = 7, Flush = 8,
	Free = 9, SetPrecedence = 10, Finish = 11, PeriodVolume = 12,
	Lock = 13, WaitCycle = 14, Allocate = 32
}

[Flags]
public enum AudioIoFlags : byte
{
	PeriodVolume = 1 << 4, SyncCycle = 1 << 5, NoWait = 1 << 6, WriteMessage = 1 << 7
}

public enum AudioIoError : sbyte
{
	NoAllocation = -10, AllocationFailed = -11, ChannelStolen = -12
}

[StructLayout(LayoutKind.Sequential, Pack = 2, Size = Size)]
public struct IOAudio
{
	public const int Size = 76;
	public IORequest Request;
	public short AllocationKey;
	public APTR Data;
	public uint Length;
	public ushort Period;
	public ushort Volume;
	public ushort Cycles;
	public Message WriteMessage;
}

public static class IOAudioLayout
{
	public const int Request = 0, AllocationKey = 40, Data = 42, Length = 46,
		Period = 50, Volume = 52, Cycles = 54, WriteMessage = 56;
}
