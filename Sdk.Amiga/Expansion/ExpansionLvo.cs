namespace Amiga;

public static class ExpansionLvo
{
	public const short AddConfigDev = -30, AddBootNode = -36, AllocBoardMem = -42, AllocConfigDev = -48;
	public const short AllocExpansionMem = -54, ConfigBoard = -60, ConfigChain = -66, FindConfigDev = -72;
	public const short FreeBoardMem = -78, FreeConfigDev = -84, FreeExpansionMem = -90, ReadExpansionByte = -96;
	public const short ReadExpansionRom = -102, RemConfigDev = -108, WriteExpansionByte = -114;
	public const short ObtainConfigBinding = -120, ReleaseConfigBinding = -126, SetCurrentBinding = -132;
	public const short GetCurrentBinding = -138, MakeDosNode = -144, AddDosNode = -150;
}
