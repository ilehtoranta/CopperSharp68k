namespace Amiga;

/// <summary>Verified offsets for utility.library public ABI records.</summary>
public static class UtilityLayout
{
	public static class Hook
	{
		public const int Size = 20;
		public const int MinNode = 0;
		public const int Entry = 8;
		public const int SubEntry = 12;
		public const int Data = 16;
	}
}
