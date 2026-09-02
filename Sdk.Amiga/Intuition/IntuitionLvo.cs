namespace Amiga;

/// <summary>Named intuition.library m68k vector offsets and slot classifiers.</summary>
public static class IntuitionLvo
{
	// Classic public/private/reserved classification is frozen from the
	// Commodore NDK 3.1 intuition_lib.fd. MorphOS 3.20 facts are exposed
	// separately below.
	public const short OpenIntuition = -30;
	public const short Intuition_ = -36;
	public const short AddGadget = -42;
	public const short ClearDMRequest = -48;
	public const short ClearMenuStrip = -54;
	public const short ClearPointer = -60;
	public const short CloseScreen = -66;
	public const short CloseWindow = -72;
	public const short CloseWorkBench = -78;
	public const short CurrentTime = -84;
	public const short DisplayAlert = -90;
	public const short DisplayBeep = -96;
	public const short DoubleClick = -102;
	public const short DrawBorder = -108;
	public const short DrawImage = -114;
	public const short EndRequest = -120;
	public const short GetDefPrefs = -126;
	public const short GetPrefs = -132;
	public const short InitRequester = -138;
	public const short ItemAddress = -144;
	public const short ModifyIDCMP = -150;
	public const short ModifyProp = -156;
	public const short MoveScreen = -162;
	public const short MoveWindow = -168;
	public const short OffGadget = -174;
	public const short OffMenu = -180;
	public const short OnGadget = -186;
	public const short OnMenu = -192;
	public const short OpenScreen = -198;
	public const short OpenWindow = -204;
	public const short OpenWorkBench = -210;
	public const short PrintIText = -216;
	public const short RefreshGadgets = -222;
	public const short RemoveGadget = -228;
	public const short ReportMouse = -234;
	public const short Request = -240;
	public const short ScreenToBack = -246;
	public const short ScreenToFront = -252;
	public const short SetDMRequest = -258;
	public const short SetMenuStrip = -264;
	public const short SetPointer = -270;
	public const short SetWindowTitles = -276;
	public const short ShowTitle = -282;
	public const short SizeWindow = -288;
	public const short ViewAddress = -294;
	public const short ViewPortAddress = -300;
	public const short WindowToBack = -306;
	public const short WindowToFront = -312;
	public const short WindowLimits = -318;
	public const short SetPrefs = -324;
	public const short IntuiTextLength = -330;
	public const short WBenchToBack = -336;
	public const short WBenchToFront = -342;
	public const short AutoRequest = -348;
	public const short BeginRefresh = -354;
	public const short BuildSysRequest = -360;
	public const short EndRefresh = -366;
	public const short FreeSysRequest = -372;
	public const short MakeScreen = -378;
	public const short RemakeDisplay = -384;
	public const short RethinkDisplay = -390;
	public const short AllocRemember = -396;
	// NDK 3.1 and M320 list this under ##public but intentionally omit a
	// documented/callable clib prototype. Inventory it without admitting it.
	public const short AlohaWorkbench = -402;
	public const short FreeRemember = -408;
	public const short LockIBase = -414;
	public const short UnlockIBase = -420;
	public const short GetScreenData = -426;
	public const short RefreshGList = -432;
	public const short AddGList = -438;
	public const short RemoveGList = -444;
	public const short ActivateWindow = -450;
	public const short RefreshWindowFrame = -456;
	public const short ActivateGadget = -462;
	public const short NewModifyProp = -468;
	public const short QueryOverscan = -474;
	public const short MoveWindowInFrontOf = -480;
	public const short ChangeWindowBox = -486;
	public const short SetEditHook = -492;
	public const short SetMouseQueue = -498;
	public const short ZipWindow = -504;
	public const short LockPubScreen = -510;
	public const short UnlockPubScreen = -516;
	public const short LockPubScreenList = -522;
	public const short UnlockPubScreenList = -528;
	public const short NextPubScreen = -534;
	public const short SetDefaultPubScreen = -540;
	public const short SetPubScreenModes = -546;
	public const short PubScreenStatus = -552;
	public const short ObtainGIRPort = -558;
	public const short ReleaseGIRPort = -564;
	public const short GadgetMouse = -570;
	// -576 is intuitionPrivate1 in the NDK 3.1 FD.
	public const short GetDefaultPubScreen = -582;
	public const short EasyRequestArgs = -588;
	public const short BuildEasyRequestArgs = -594;
	public const short SysReqHandler = -600;
	public const short OpenWindowTagList = -606;
	public const short OpenScreenTagList = -612;
	public const short DrawImageState = -618;
	public const short PointInImage = -624;
	public const short EraseImage = -630;
	public const short NewObjectA = -636;
	public const short DisposeObject = -642;
	public const short SetAttrsA = -648;
	public const short GetAttr = -654;
	public const short SetGadgetAttrsA = -660;
	public const short NextObject = -666;
	// -672 is intuitionPrivate2 in the NDK 3.1 FD.
	public const short MakeClass = -678;
	public const short AddClass = -684;
	public const short GetScreenDrawInfo = -690;
	public const short FreeScreenDrawInfo = -696;
	public const short ResetMenuStrip = -702;
	public const short RemoveClass = -708;
	public const short FreeClass = -714;
	// -720 and -726 are intuitionPrivate3/intuitionPrivate4 in the NDK 3.1 FD.
	// -732 through -762 are reserved.
	public const short AllocScreenBuffer = -768;
	public const short FreeScreenBuffer = -774;
	public const short ChangeScreenBuffer = -780;
	public const short ScreenDepth = -786;
	public const short ScreenPosition = -792;
	public const short ScrollWindowRaster = -798;
	public const short LendMenus = -804;
	public const short DoGadgetMethodA = -810;
	public const short SetWindowPointerA = -816;
	public const short TimedDisplayAlert = -822;
	public const short HelpControl = -828;

	// Selected MorphOS m68k declaration surface. Runtime admission belongs to
	// the consuming library profile. Unlisted slots in this range
	// remain unclaimed until their ABI and semantic disposition are verified.
	public const short GetSkinInfoAttrA = -918;
	public const short GetDrawInfoAttr = -936;
	public const short WindowAction = -942;
	public const short TransparencyControl = -948;
	public const short ScrollWindowRasterNoFill = -954;
	public const short GetMonitorList = -966;
	public const short FreeMonitorList = -972;
	public const short ScreenbarControlA = -978;
	public const short GetMonitorModesList = -996;
	public const short FreeMonitorModesList = -1002;
	public const short GetMonitorMode = -1008;

	/// <summary>
	/// Returns the private-slot classification from Commodore NDK 3.1.
	/// Do not use this method for the MorphOS profile.
	/// </summary>
	public static bool IsClassicPrivate(short lvo) =>
		lvo is -576 or -672 or -720 or -726;

	/// <summary>Returns the reserved-slot classification from Commodore NDK 3.1.</summary>
	public static bool IsClassicReserved(short lvo) =>
		lvo == -732 || lvo == -738 || lvo == -744 || lvo == -750 ||
		lvo == -756 || lvo == -762;

	/// <summary>Returns whether NDK 3.1 marks a public call intentionally undocumented.</summary>
	public static bool IsClassicUndocumentedPublic(short lvo) =>
		lvo is OpenIntuition or Intuition_ or AlohaWorkbench;

	/// <summary>
	/// Returns whether <paramref name="lvo"/> is one of the private MorphOS 3.20
	/// slots between the classic tail and the last declared extension vector.
	/// </summary>
	public static bool IsEnhancedPrivate(short lvo) =>
		lvo is -834 or -840 or -846 or -852 or -858 or -864 or -870 or
			-876 or -882 or -888 or -894 or -900 or -906 or -912 or
			-924 or -930 or -960 or -984 or -990;

	/// <summary>Returns whether M320 marks the physical slot private.</summary>
	public static bool IsMorphOsPrivate(short lvo) =>
		lvo is -576 or -672 or -720 or -726 or -732 or -738 or -744 or
			-750 or -756 or -762 || IsEnhancedPrivate(lvo);
}

/// <summary>Published intuition.library ABI and profile facts.</summary>
public static class IntuitionAbiConstants
{
	public const ushort UnverifiedVersion = 0;
	public const ushort ClassicV40 = 40;
	public const ushort MorphOsV50 = 50;
	public const ushort MorphOsV51 = 51;
	public const ushort MorphOsV60 = 60;
	public const bool EnhancedVectorVersionsVerified = true;
	public const short MorphOsFdFirstLvo = IntuitionLvo.OpenIntuition;
	public const short MorphOsFdLastLvo = IntuitionLvo.GetMonitorMode;
	public const int MorphOsFdSlotCount = 164;
	public const int MorphOsFdPublicVectorCount = 135;
	public const int MorphOsFdPrivateSlotCount = 29;
	public const int MorphOsPrototypeVectorCount = 134;

	public const short ClassicFirstLvo = IntuitionLvo.OpenIntuition;
	public const short ClassicLastLvo = IntuitionLvo.HelpControl;
	public const int ClassicSlotCount = 134;
	/// <summary>Unique classic callable SDK declarations.</summary>
	public const int ClassicVectorCount = 123;
	public const int ClassicDeclaredVectorCount = ClassicVectorCount;
	/// <summary>Public names in the NDK 3.1 FD, including undocumented calls.</summary>
	public const int ClassicPublicVectorCount = 124;
	public const int ClassicUndocumentedPublicVectorCount = 3;
	public const int ClassicPrivateSlotCount = 4;
	public const int ClassicReservedCount = 6;

	public const short EnhancedFirstLvo = -834;
	public const short EnhancedFirstDeclaredLvo = IntuitionLvo.GetSkinInfoAttrA;
	public const short EnhancedLastLvo = IntuitionLvo.GetMonitorMode;
	public const int EnhancedSlotCount = 30;
	public const int EnhancedVectorCount = 11;
	public const int EnhancedPrivateSlotCount = 19;
}

/// <summary>
/// Minimum library major version for each selected MorphOS extension vector.
/// Values are derived from the official intuition.library source history and
/// generated autodoc distributed for MorphOS 3.20.
/// </summary>
public static class IntuitionVectorVersion
{
	public const ushort GetSkinInfoAttrA = IntuitionAbiConstants.MorphOsV50;
	public const ushort GetDrawInfoAttr = IntuitionAbiConstants.MorphOsV50;
	public const ushort WindowAction = IntuitionAbiConstants.MorphOsV50;
	public const ushort TransparencyControl = IntuitionAbiConstants.MorphOsV50;
	public const ushort ScrollWindowRasterNoFill = IntuitionAbiConstants.MorphOsV50;
	public const ushort GetMonitorList = IntuitionAbiConstants.MorphOsV50;
	public const ushort FreeMonitorList = IntuitionAbiConstants.MorphOsV50;
	public const ushort ScreenbarControlA = IntuitionAbiConstants.MorphOsV50;
	public const ushort GetMonitorModesList = IntuitionAbiConstants.MorphOsV60;
	public const ushort FreeMonitorModesList = IntuitionAbiConstants.MorphOsV60;
	public const ushort GetMonitorMode = IntuitionAbiConstants.MorphOsV60;
}
