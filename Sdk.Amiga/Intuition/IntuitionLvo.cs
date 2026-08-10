namespace Amiga;

/// <summary>Compile-time Intuition offsets currently consumed by CopperStart.</summary>
public static class IntuitionLvo
{
	public const short CloseScreen = -66, CloseWindow = -72, ModifyIDCMP = -150, OpenScreen = -198, OpenWindow = -204;
	public const short ReportMouse = -234, ScreenToFront = -252, SetWindowTitles = -276, ShowTitle = -282;
	public const short ViewAddress = -294, ViewPortAddress = -300, MakeScreen = -378, RemakeDisplay = -384, RethinkDisplay = -390;
	public const short AllocRemember = -396, FreeRemember = -408, GetScreenData = -426, RefreshGList = -432, AddGList = -438;
	public const short QueryOverscan = -474, OpenScreenTagList = -612;
}
