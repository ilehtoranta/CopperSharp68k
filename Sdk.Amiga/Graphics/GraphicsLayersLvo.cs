namespace Amiga;

/// <summary>
/// graphics.library vector identities that participate in Layers clipping,
/// locking, or super-bitmap integration.
/// </summary>
public static class GraphicsLayersLvo
{
	public const short BltTemplate = -36;
	public const short ClearEOL = -42;
	public const short ClearScreen = -48;
	public const short Text = -60;
	public const short DrawEllipse = -180;
	public const short AreaEllipse = -186;
	public const short SetRast = -234;
	public const short Draw = -246;
	public const short AreaMove = -252;
	public const short AreaDraw = -258;
	public const short AreaEnd = -264;
	public const short RectFill = -306;
	public const short BltPattern = -312;
	public const short ReadPixel = -318;
	public const short WritePixel = -324;
	public const short Flood = -330;
	public const short PolyDraw = -336;
	public const short ScrollRaster = -396;
	public const short LockLayerRom = -432;
	public const short UnlockLayerRom = -438;
	public const short SyncSBitMap = -444;
	public const short CopySBitMap = -450;
	public const short ClipBlit = -552;
	public const short BltBitMapRastPort = -606;
	public const short BltMaskBitMapRastPort = -636;
	public const short AttemptLockLayerRom = -654;
	public const short ReadPixelLine8 = -768;
	public const short WritePixelLine8 = -774;
	public const short ReadPixelArray8 = -780;
	public const short WritePixelArray8 = -786;
	public const short EraseRect = -810;
	public const short ScrollRasterBF = -1002;
	public const short GetRPAttrsA = -1044;
	public const short WriteChunkyPixels = -1056;
}
