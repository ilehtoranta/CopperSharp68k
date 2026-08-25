using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

[AmigaLibrary(Name, AmigaLibraryBasePolicy.CallerProvided)]
public static class ConsoleDevice
{
	public const string Name = "console.device";
	public const short Open = -6, Close = -12, Expunge = -18, ExtFunc = -24,
		BeginIO = -30, AbortIO = -36, CDInputHandler = -42, RawKeyConvert = -48;
	public const int DefaultColumns = 80, DefaultRows = 25;

	/// <summary>Calls the MorphOS <c>RawKeyConvert</c> device vector.</summary>
	[AmigaLvo(RawKeyConvert)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ConvertRawKey(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A0)] APTR inputEvent,
		[M68kRegister(M68kRegister.A1)] APTR buffer,
		[M68kRegister(M68kRegister.D1)] int length,
		[M68kRegister(M68kRegister.A2)] APTR keyMap);
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
