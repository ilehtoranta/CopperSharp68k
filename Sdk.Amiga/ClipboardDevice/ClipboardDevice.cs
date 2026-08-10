using System.Runtime.InteropServices;

namespace Amiga;

public static class ClipboardDevice
{
	public const string Name = "clipboard.device";
	public const short Open = -6, Close = -12, Expunge = -18, ExtFunc = -24, BeginIO = -30, AbortIO = -36;
	public const uint PrimaryClip = 0;
}

public enum ClipboardCommand : ushort
{
	Reset = 1, Read = 2, Write = 3, Update = 4, Clear = 5, Stop = 6, Start = 7, Flush = 8,
	Post = 9, CurrentReadId = 10, CurrentWriteId = 11, ChangeHook = 12
}

public enum ClipboardError : byte { ObsoleteId = 1 }

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ClipboardUnitPartial { public const uint Size = 18; public Node Node; public uint UnitNumber; }

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IOClipReq
{
	public const uint Size = 52;
	public IOStdReq Request;
	public int ClipId;
}
public static class IOClipReqLayout { public const int Request = 0, Offset = 44, ClipId = 48; }

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct SatisfyMessage { public const uint Size = 26; public Message Message; public ushort Unit; public int ClipId; }
public static class SatisfyMessageLayout { public const int Message = 0, Unit = 20, ClipId = 22; }

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ClipHookMessage { public const uint Size = 12; public uint Type; public int ChangeCommand; public int ClipId; }
public static class ClipHookMessageLayout { public const int Type = 0, ChangeCommand = 4, ClipId = 8; }
