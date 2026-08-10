namespace Amiga;

public static class ConsoleDevice
{
	public const string Name = "console.device";
	public const short Open = -6, Close = -12, Expunge = -18, ExtFunc = -24,
		BeginIO = -30, AbortIO = -36, CDInputHandler = -42, RawKeyConvert = -48;
	public const int DefaultColumns = 80, DefaultRows = 25;
}

public enum ConsoleCommand : ushort
{
	Reset = 1, Read = 2, Write = 3, Update = 4, Clear = 5, Stop = 6, Start = 7, Flush = 8,
	AskKeyMap = 9, SetKeyMap = 10, AskDefaultKeyMap = 11, SetDefaultKeyMap = 12
}

public enum ConsoleUnit : int
{
	Library = -1, Standard = 0, CharacterMap = 1, SnipMap = 3
}

[Flags]
public enum ConsoleOpenFlags : uint
{
	Default = 0, NoDrawOnNewSize = 1
}
