/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace Amiga.MUI;

// Generated from MorphOS SDK headers: libraries/mui.h and mui/*.h.
public static class Tag
{
	public const uint Done = 0;
}

public static class Aboutbox
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Build = 0xfed1001eu;
	public const uint Credits = 0xfed10001u;
	public const uint LogoData = 0xfed10002u;
	public const uint LogoFallbackMode = 0xfed10003u;
	public const uint LogoFile = 0xfed10004u;
	public const string Name = "Aboutbox.mcc";
	public static class Value
	{
		public static class LogoFallbackMode
		{
			public const int Auto = 1145391360;
			public const int NoLogo = 0;
		}
	}
}

public static class Aboutmui
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Application = 0x80422523u;
	public const string Name = "Aboutmui.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Aboutpage
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Aboutpage.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Application
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Active = 0x804260abu;
	public const uint Author = 0x80424842u;
	public const uint Base = 0x8042e07au;
	public const uint Broker = 0x8042dbceu;
	public const uint BrokerHook = 0x80428f4bu;
	public const uint BrokerPort = 0x8042e0adu;
	public const uint BrokerPri = 0x8042c8d0u;
	public const uint Commands = 0x80428648u;
	public const uint Copyright = 0x8042ef4du;
	public const uint Description = 0x80421fc6u;
	public const uint DiskObject = 0x804235cbu;
	public const uint DoubleStart = 0x80423bc6u;
	public const uint DropObject = 0x80421266u;
	public const uint ForceQuit = 0x804257dfu;
	public const uint HelpFile = 0x804293f4u;
	public const uint Iconified = 0x8042a07fu;
	public const uint IconifyTitle = 0x80422cb8u;
	public const uint Menu = 0x80420e1fu;
	public const uint MenuAction = 0x80428961u;
	public const uint MenuHelp = 0x8042540bu;
	public const uint Menustrip = 0x804252d9u;
	public static class Method
	{
		public const uint AboutMUI = 0x8042d21du;
		public const uint AddInputHandler = 0x8042f099u;
		public const uint BuildSettingsPanel = 0x8042b58fu;
		public const uint CheckRefresh = 0x80424d68u;
		public const uint DefaultConfigItem = 0x8042d934u;
		public const uint GetMenuCheck = 0x8042c0a7u;
		public const uint GetMenuState = 0x8042a58fu;
		public const uint Input = 0x8042d0f5u;
		public const uint InputBuffered = 0x80427e59u;
		public const uint Load = 0x8042f90du;
		public const uint NewInput = 0x80423ba6u;
		public const uint OpenConfigWindow = 0x804299bau;
		public const uint PushMethod = 0x80429ef8u;
		public const uint RemInputHandler = 0x8042e7afu;
		public const uint ReturnID = 0x804276efu;
		public const uint Run = 0x90420103u;
		public const uint Save = 0x804227efu;
		public const uint SetConfigItem = 0x80424a80u;
		public const uint SetMenuCheck = 0x8042a707u;
		public const uint SetMenuState = 0x80428befu;
		public const uint ShowHelp = 0x80426479u;
		public const uint UnpushMethod = 0x804211ddu;
	}
	public const string Name = "Application.mui";
	public const uint RexxHook = 0x80427c42u;
	public const uint RexxMsg = 0x8042fd88u;
	public const uint RexxString = 0x8042d711u;
	public const uint SingleTask = 0x8042a2c8u;
	public const uint Sleep = 0x80425711u;
	public const uint Title = 0x804281b8u;
	public const uint UseCommodities = 0x80425ee5u;
	public const uint UseRexx = 0x80422387u;
	public const uint UseScreenNotify = 0x80420861u;
	public const uint UsedClasses = 0x8042e9a7u;
	public static class Value
	{
		public static class Load
		{
			public const int ENV = 0;
			public const uint ENVARC = 0xffffffffu;
		}
		public static class OCW
		{
			public const int ScreenPage = 2;
		}
		public static class ReturnID
		{
			public const int Quit = -1;
		}
		public static class Save
		{
			public const int ENV = 0;
			public const uint ENVARC = 0xffffffffu;
		}
	}
	public const uint Version = 0x8042b33fu;
	public const uint Window = 0x8042bfe0u;
	public const uint WindowList = 0x80429abeu;
}

public static class Applist
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Applist.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Area
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Area.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Argstring
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Contents = 0x80429456u;
	public const string Name = "Argstring.mui";
	public const uint Template = 0x80422904u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Attribute
{
	public const uint AppMessage = 0x80421955u;
	public const uint ApplicationObject = 0x8042d3eeu;
	public const uint Background = 0x8042545bu;
	public const uint BottomEdge = 0x8042e552u;
	public const uint BuiltinFont = 0x8042256fu;
	public const uint CSSFilePath = 0x804225d8u;
	public const uint ContextMenu = 0x8042b704u;
	public const uint ContextMenuTrigger = 0x8042a2c1u;
	public const uint ControlChar = 0x8042120bu;
	public const uint CustomBackfill = 0x80420a63u;
	public const uint CustomFont = 0x80423fc5u;
	public const uint CycleChain = 0x80421ce7u;
	public const uint Disabled = 0x80423661u;
	public const uint DoubleBuffer = 0x8042a9c7u;
	public const uint DoubleClick = 0x8042f057u;
	public const uint Draggable = 0x80420b6eu;
	public const uint Dropable = 0x8042fbceu;
	public const uint ExportID = 0x8042d76eu;
	public const uint FillArea = 0x804294a3u;
	public const uint FixHeight = 0x8042a92bu;
	public const uint FixHeightTxt = 0x804276f2u;
	public const uint FixWidth = 0x8042a3f1u;
	public const uint FixWidthTxt = 0x8042d044u;
	public const uint Floating = 0x80429753u;
	public const uint Font = 0x8042be50u;
	public const uint Frame = 0x8042ac64u;
	public const uint FrameDynamic = 0x804223c9u;
	public const uint FramePhantomHoriz = 0x8042ed76u;
	public const uint FrameTitle = 0x8042d1c7u;
	public const uint FrameVisible = 0x80426498u;
	public const uint Height = 0x80423237u;
	public const uint HelpLine = 0x8042a825u;
	public const uint HelpNode = 0x80420b85u;
	public const uint HorizDisappear = 0x80429615u;
	public const uint HorizWeight = 0x80426db9u;
	public const uint InnerBottom = 0x8042f2c0u;
	public const uint InnerLeft = 0x804228f8u;
	public const uint InnerRight = 0x804297ffu;
	public const uint InnerTop = 0x80421eb6u;
	public const uint InputMode = 0x8042fb04u;
	public const uint LeftEdge = 0x8042bec6u;
	public const uint MaxHeight = 0x804293e4u;
	public const uint MaxWidth = 0x8042f112u;
	public const uint NoNotify = 0x804237f9u;
	public const uint NoNotifyMethod = 0x80420a74u;
	public const uint ObjectID = 0x8042d76eu;
	public const uint Parent = 0x8042e35fu;
	public const uint Pressed = 0x80423535u;
	public const uint Revision = 0x80427eaau;
	public const uint RightEdge = 0x8042ba82u;
	public const uint Selected = 0x8042654bu;
	public const uint ShortHelp = 0x80428fe3u;
	public const uint ShowMe = 0x80429ba8u;
	public const uint ShowSelState = 0x8042caacu;
	public const uint TextColor = 0x8042dba6u;
	public const uint Timer = 0x80426435u;
	public const uint TopEdge = 0x8042509bu;
	public const uint Unicode = 0x8042e7d0u;
	public const uint UserData = 0x80420313u;
	public const uint Version = 0x80422301u;
	public const uint VertDisappear = 0x8042d12fu;
	public const uint VertWeight = 0x804298d0u;
	public const uint Weight = 0x80421d1fu;
	public const uint Width = 0x8042b59cu;
	public const uint Window = 0x80421591u;
	public const uint WindowObject = 0x8042669eu;
}

public static class Audiocontrols
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Audiocontrols.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Audiomixer
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Audiomixer.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Backgroundadjust
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Backgroundadjust.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Balance
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Balance.mui";
	public const uint Quiet = 0x80427486u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class BetterBalance
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "BetterBalance.mcc";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Bitmap
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Alpha = 0x80423e71u;
	public const uint Height = 0x80421560u;
	public const uint MappingTable = 0x8042e23du;
	public const string Name = "Bitmap.mui";
	public const uint Precision = 0x80420c74u;
	public const uint RemappedBitmap = 0x80423a47u;
	public const uint SourceColors = 0x80425360u;
	public const uint Transparent = 0x80422805u;
	public const uint UseFriend = 0x804239d8u;
	public const uint Value = 0x804279bdu;
	public const uint Width = 0x8042eb3au;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Bodychunk
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Body = 0x8042ca67u;
	public const uint Compression = 0x8042de5fu;
	public const uint Depth = 0x8042c392u;
	public const uint Masking = 0x80423b0eu;
	public const string Name = "Bodychunk.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Boopsi
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Class = 0x80426999u;
	public const uint ClassID = 0x8042bfa3u;
	public const uint MaxHeight = 0x8042757fu;
	public const uint MaxWidth = 0x8042bcb1u;
	public const uint MinHeight = 0x80422c93u;
	public const uint MinWidth = 0x80428fb2u;
	public const string Name = "Boopsi.mui";
	public const uint Object = 0x80420178u;
	public const uint Remember = 0x8042f4bdu;
	public const uint Smart = 0x8042b8d7u;
	public const uint TagDrawInfo = 0x8042bae7u;
	public const uint TagScreen = 0x8042bc71u;
	public const uint TagWindow = 0x8042e11du;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Busy
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public static class Method
	{
		public const uint Move = 0x80020001u;
	}
	public const string Name = "Busy.mcc";
	public const uint ShowHideIH = 0x800200a9u;
	public const uint Speed = 0x80020049u;
	public static class Value
	{
		public static class Speed
		{
			public const int Off = 0;
			public const int User = -1;
		}
	}
}

public static class Calendar
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint BrowserChanged = 0xfde08216u;
	public const uint CalendarDate = 0xfde0820au;
	public const uint CalendarDateBrowser = 0xfde0820fu;
	public const uint Changed = 0xfde08200u;
	public const uint Compact = 0xfde08207u;
	public const uint ContextCalendarDate = 0xfde08215u;
	public const uint ContextEventID = 0xfde08214u;
	public const uint DatabaseName = 0xfde0820bu;
	public const uint Day = 0xfde08201u;
	public const uint DayClick = 0xfde08212u;
	public const uint DayDoubleClick = 0xfde08213u;
	public static class Method
	{
		public const uint GetDaysInMonth = 0xfde08201u;
		public const uint Notification = 0xfde0820bu;
		public const uint ReadSystemDate = 0xfde08200u;
	}
	public const uint Mode = 0xfde08206u;
	public const uint Month = 0xfde08202u;
	public const uint MonthBrowser = 0xfde08210u;
	public const string Name = "Calendar.mcc";
	public const uint NotifyObject = 0xfde08209u;
	public const uint PeekOver = 0xfde08208u;
	public const uint ShowImages = 0xfde0820cu;
	public const uint ShowTimeLine = 0xfde0820du;
	public const uint ShowTitle = 0xfde08204u;
	public const uint ShowWeekdays = 0xfde0820eu;
	public const uint ShowYear = 0xfde08205u;
	public static class Value
	{
		public static class DayClick
		{
			public const int NextMonth = 268435456;
			public const int PreviousMonth = 536870912;
		}
		public static class Mode
		{
			public const int Days = 1;
			public const int Full = 2;
			public const int FullReadOnly = 3;
			public const int MonthYear = 3;
			public const int None = 0;
		}
		public static class NotifyMode
		{
			public const int Click = 0;
			public const int DoubleClick = 1;
		}
	}
	public const uint Year = 0xfde08203u;
	public const uint YearBrowser = 0xfde08211u;
}

public static class Calltips
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Layout = 0xfecf1005u;
	public const uint MarginLeft = 0xfecf1008u;
	public const uint MarginTop = 0xfecf1009u;
	public static class Method
	{
		public const uint ParentCleanup = 0xfecf1006u;
		public const uint ParentHide = 0xfecf1008u;
		public const uint ParentSetup = 0xfecf1005u;
		public const uint ParentShow = 0xfecf1007u;
		public const uint ParentWindowArranged = 0xfecf1009u;
		public const uint SetRectangle = 0xfecf1004u;
	}
	public const string Name = "Calltips.mcc";
	public const uint Rectangle = 0xfecf1004u;
	public const uint Source = 0xfecf1006u;
	public static class Value
	{
		public static class Layout
		{
			public const int BelowThenAbove = 2;
			public const int Exact = 0;
			public const int RightThenBelow = 1;
		}
	}
}

public static class Cclist
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Cclist.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Chart
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Colors = 0xfece2000u;
	public const uint Count = 0xfece2001u;
	public const uint GroupSize = 0xfece2002u;
	public const uint InitialMaxValue = 0xfece2004u;
	public const uint InitialMinValue = 0xfece2003u;
	public static class Method
	{
		public const uint ClearGroup = 0xfece1000u;
		public const uint GetGroup = 0xfece1001u;
		public const uint GetMinMax = 0xfece1003u;
		public const uint InsertGroup = 0xfece1002u;
		public const uint RemoveGroup = 0xfece1004u;
		public const uint ReplaceInGroup = 0xfece1005u;
		public const uint SetMax = 0xfece1006u;
	}
	public const string Name = "Chart.mcc";
	public const uint Title = 0xfece2006u;
	public const uint Type = 0xfece2007u;
	public const uint Unit = 0xfece2005u;
}

public static class Clock
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Hour = 0xfecd0030u;
	public static class Method
	{
		public const uint OffsetFromTime = 0xfecd0070u;
	}
	public const uint Minute = 0xfecd0031u;
	public const string Name = "Clock.mcc";
	public const uint Second = 0xfecd0032u;
	public const uint Wrapped = 0xfecd0033u;
}

public static class ColorButton
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Max = 0xaa124322u;
	public const string Name = "ColorButton.mcc";
	public const uint Pen = 0xaa124321u;
	public const uint Pens = 0xaa124320u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class ColorSlider
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "ColorSlider.mcc";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Coloradjust
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint ARGB = 0x804250cau;
	public const uint Alpha = 0x8042a1f1u;
	public const uint Blue = 0x8042b8a3u;
	public const uint Green = 0x804285abu;
	public const uint ModeID = 0x8042ec59u;
	public const string Name = "Coloradjust.mui";
	public const uint RGB = 0x8042f899u;
	public const uint Red = 0x80420eaau;
	public const uint ShowAlpha = 0x8042e102u;
	public const uint XRGB = 0x8042cc13u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Colorfield
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Blue = 0x8042d3b0u;
	public const uint Green = 0x80424466u;
	public const string Name = "Colorfield.mui";
	public const uint Pen = 0x8042713au;
	public const uint RGB = 0x8042677au;
	public const uint Red = 0x804279f6u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Colorring
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Colorring.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Configdata
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Configdata.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Cpumonitor
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint DnetcMode = 0xfed1000fu;
	public const uint FilterTask = 0xfed1000cu;
	public const uint LastValue = 0xfed1000du;
	public static class Method
	{
		public const uint Clone = 0xfed10012u;
		public const uint Reset = 0xfed1000au;
	}
	public const string Name = "Cpumonitor.mcc";
	public const uint QuickMode = 0xfed10011u;
	public const uint TaskEncounter = 0xfed1000eu;
	public static class Value
	{
		public static class FilterTask
		{
			public const int Dnetc = 1;
		}
	}
}

public static class Crawling
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Crawling.mcc";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Cycle
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Active = 0x80421788u;
	public const uint Entries = 0x80420629u;
	public const string Name = "Cycle.mui";
	public static class Value
	{
		public static class Active
		{
			public const int Next = -1;
			public const int Prev = -2;
		}
	}
}

public static class Datamap
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint AutoLock = 0x8042fbe4u;
	public const uint CopyKeys = 0x8042a179u;
	public const uint Count = 0x80427580u;
	public static class Method
	{
		public const uint Clear = 0x8042eebcu;
		public const uint Find = 0x8042d650u;
		public const uint Get = 0x8042c2bau;
		public const uint Iterate = 0x8042fda1u;
		public const uint IterationKey = 0x8042bc15u;
		public const uint Remove = 0x804203d8u;
		public const uint Set = 0x8042b84fu;
	}
	public const string Name = "Datamap.mui";
	public const uint Pool = 0x80424724u;
}

public static class Dataspace
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Count = 0x8042e7eau;
	public static class Method
	{
		public const uint Add = 0x80423366u;
		public const uint Clear = 0x8042b6c9u;
		public const uint Find = 0x8042832cu;
		public const uint Get = 0x8042483fu;
		public const uint Merge = 0x80423e2bu;
		public const uint ReadIFF = 0x80420dfbu;
		public const uint Remove = 0x8042dce1u;
		public const uint WriteIFF = 0x80425e8eu;
	}
	public const string Name = "Dataspace.mui";
	public const uint Pool = 0x80424cf9u;
}

public static class Dirlist
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint AcceptPattern = 0x8042760au;
	public const uint Directory = 0x8042ea41u;
	public const uint DrawersOnly = 0x8042b379u;
	public const uint ExAllType = 0x8042cd7cu;
	public const uint FilesOnly = 0x8042896au;
	public const uint FilterDrawers = 0x80424ad2u;
	public const uint FilterHook = 0x8042ae19u;
	public static class Method
	{
		public const uint ReRead = 0x80422d71u;
		public const uint Rename = 0x8042d336u;
		public const uint SetComment = 0x8042b378u;
		public const uint SetProtection = 0x804202bbu;
	}
	public const uint MultiSelDirs = 0x80428653u;
	public const string Name = "Dirlist.mui";
	public const uint NumBytes = 0x80429e26u;
	public const uint NumBytes64 = 0x80428050u;
	public const uint NumDrawers = 0x80429cb8u;
	public const uint NumFiles = 0x8042a6f0u;
	public const uint Path = 0x80426176u;
	public const uint Pattern = 0x8042c761u;
	public const uint RejectIcons = 0x80424808u;
	public const uint RejectPattern = 0x804259c7u;
	public const uint SortDirs = 0x8042bbb9u;
	public const uint SortHighLow = 0x80421896u;
	public const uint SortType = 0x804228bcu;
	public const uint Status = 0x804240deu;
	public static class Value
	{
		public static class Rename
		{
			public const int Active = -1;
		}
		public static class SetComment
		{
			public const int Active = -1;
		}
		public static class SetProtection
		{
			public const int Active = -1;
		}
		public static class SortDirs
		{
			public const int First = 0;
			public const int Last = 1;
			public const int Mix = 2;
		}
		public static class SortType
		{
			public const int Comment = 3;
			public const int Count = 7;
			public const int Date = 1;
			public const int Flags = 4;
			public const int Name = 0;
			public const int Size = 2;
			public const int Type = 5;
			public const int Used = 6;
		}
		public static class Status
		{
			public const int Invalid = 0;
			public const int Reading = 1;
			public const int Valid = 2;
		}
	}
}

public static class Dtpic
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Alpha = 0x8042b4dbu;
	public const uint DarkenSelState = 0x80423247u;
	public const uint FreeHoriz = 0x8042d360u;
	public const uint FreeVert = 0x80424c12u;
	public const uint LightenOnMouse = 0x8042966au;
	public const uint MinHeight = 0x80423eccu;
	public const uint MinWidth = 0x8042c417u;
	public const uint Name = 0x80423d72u;
	public const string NameClass = "Dtpic.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(NameClass), tags);
}

public static class Error
{
	public const int InvalidWindowObject = 3;
	public const int MissingLibrary = 4;
	public const int NoARexx = 5;
	public const int OK = 0;
	public const int OutOfGfxMemory = 2;
	public const int OutOfMemory = 1;
	public const int SingleTask = 6;
}

public static class FSProtectionBits
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Flags = 0x8042330cu;
	public const string Name = "FSProtectionBits.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Family
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Child = 0x8042c696u;
	public const uint ChildCount = 0x8042b25au;
	public const uint List = 0x80424b9eu;
	public static class Method
	{
		public const uint AddHead = 0x8042e200u;
		public const uint AddTail = 0x8042d752u;
		public const uint DoChildMethods = 0x80429a3cu;
		public const uint GetChild = 0x8042c556u;
		public const uint Insert = 0x80424d34u;
		public const uint Remove = 0x8042f8a9u;
		public const uint Reorder = 0x80426008u;
		public const uint Sort = 0x80421c49u;
		public const uint Transfer = 0x8042c14au;
	}
	public const string Name = "Family.mui";
	public static class Value
	{
		public static class GetChild
		{
			public const int First = 0;
			public const int Iterate = -4;
			public const int Last = -1;
			public const int Next = -2;
			public const int Previous = -3;
		}
	}
}

public static class Filepanel
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint AcceptPattern = 0x80426f3bu;
	public const uint DoMultiSelect = 0x8042fd78u;
	public const uint DoPatterns = 0x80420b3bu;
	public const uint DoSaveMode = 0x80429022u;
	public const uint Drawer = 0x8042e802u;
	public const uint DrawersOnly = 0x80427726u;
	public const uint File = 0x80427acfu;
	public const uint FilterDrawers = 0x804298a1u;
	public const uint FilterFunc = 0x80429c9du;
	public static class Method
	{
		public const uint AddRow = 0x80421d3bu;
	}
	public const string Name = "Filepanel.mui";
	public const uint Pattern = 0x8042c330u;
	public const uint RejectIcons = 0x80423450u;
	public const uint RejectPattern = 0x804281abu;
}

public static class Flag
{
	public static class DRAGEVENT
	{
		public const int FOREIGNDROP = 2;
		public const int MOUSECHANGED = 4;
		public const int REDRAW = 1;
	}
	public static class DRAGIMAGE
	{
		public const int HASMASK = 1;
		public const int NOSHADOWS = 4;
		public const int SOURCEALPHA = 2;
	}
	public static class PowerTerm
	{
		public static class Search
		{
			public const int Continue = 2;
			public static class Direction
			{
				public const int Down = 1;
				public const int Up = 0;
			}
			public const int MakeVisible = 4;
			public const int Mark = 8;
			public const int MarkLine = 32;
			public const int MarkWord = 16;
		}
	}
	public static class Slave
	{
		public static class Delegate
		{
			public const int ForceSlave = 1;
		}
	}
}

public static class Floattext
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Justify = 0x8042dc03u;
	public static class Method
	{
		public const uint Append = 0x8042a221u;
	}
	public const string Name = "Floattext.mui";
	public const uint SkipChars = 0x80425c7du;
	public const uint TabSize = 0x80427d17u;
	public const uint Text = 0x8042d16au;
}

public static class Fontdisplay
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Fontdisplay.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Fontpanel
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const string Name = "Fontpanel.mui";
	public const uint ShowCollection = 0x804225eau;
	public static class Value
	{
		public static class ShowCollection
		{
			public const int All = -1;
			public const int Bitmap = 2;
			public const int FixedWidth = 1;
			public const int TrueType = 4;
			public const int User = 8;
		}
	}
}

public static class Frameadjust
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Frameadjust.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Framedisplay
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Framedisplay.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Frimagedisplay
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Frimagedisplay.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Gadget
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Gadget.mui";
	public const uint Value = 0x8042ec1au;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Gauge
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Current = 0x8042f0ddu;
	public const uint Divide = 0x8042d8dfu;
	public const uint Horiz = 0x804232ddu;
	public const uint InfoRate = 0x804253c8u;
	public const uint InfoText = 0x8042bf15u;
	public const uint Max = 0x8042bcdbu;
	public const string Name = "Gauge.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Graph
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint DrawBackCurve = 0xfed10007u;
	public const uint Max = 0xfed10006u;
	public const uint MaxEntries = 0xfed10005u;
	public static class Method
	{
		public const uint AddEntry = 0xfed10009u;
		public const uint Clone = 0xfed10012u;
		public const uint Reset = 0xfed1000au;
	}
	public const string Name = "Graph.mcc";
	public const uint SetMax = 0xfed10008u;
}

public static class Group
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint ActivePage = 0x80424199u;
	public const uint Child = 0x804226e6u;
	public const uint ChildCount = 0x80420322u;
	public const uint ChildList = 0x80424748u;
	public const uint Columns = 0x8042f416u;
	public const uint Forward = 0x80421422u;
	public const uint ForwardDepth = 0x80428488u;
	public const uint Horiz = 0x8042536bu;
	public const uint HorizCenter = 0x8042cc64u;
	public const uint HorizSpacing = 0x8042c651u;
	public const uint LayoutHook = 0x8042c3b2u;
	public static class Method
	{
		public const uint AddHead = 0x8042e200u;
		public const uint AddTail = 0x8042d752u;
		public const uint ExitChange = 0x8042d1ccu;
		public const uint ExitChange2 = 0x8042e541u;
		public const uint InitChange = 0x80420887u;
		public const uint MoveMember = 0x8042ff4eu;
		public const uint Remove = 0x8042f8a9u;
		public const uint Reorder = 0x80426c3fu;
		public const uint Sort = 0x80427417u;
	}
	public const string Name = "Group.mui";
	public const uint PageMode = 0x80421a5fu;
	public const uint Rows = 0x8042b68fu;
	public const uint SameHeight = 0x8042037eu;
	public const uint SameSize = 0x80420860u;
	public const uint SameWidth = 0x8042b3ecu;
	public const uint Spacing = 0x8042866du;
	public static class Value
	{
		public static class ActivePage
		{
			public const int Advance = -4;
			public const int First = 0;
			public const int Last = -1;
			public const int Next = -3;
			public const int Prev = -2;
		}
		public static class GetChild
		{
			public const int First = 0;
			public const int Iterate = -4;
			public const int Last = -1;
			public const int Next = -2;
			public const int Previous = -3;
		}
		public static class Spacing
		{
			public const int Default = -100;
		}
	}
	public const uint VertCenter = 0x8042c008u;
	public const uint VertSpacing = 0x8042e1bfu;
}

public static class Hex
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint CursorChanged = 0xfecf4e2eu;
	public const uint CursorSize = 0xfecf4e2au;
	public const uint Editing = 0xfecf4e2fu;
	public const uint File = 0xfecf4e21u;
	public const uint FirstRow = 0xfecf4e28u;
	public const uint HasRedo = 0xfecf4e24u;
	public const uint HasUndo = 0xfecf4e23u;
	public const uint MarkChanged = 0xfecf4e2cu;
	public static class Method
	{
		public const uint AbstractDataChanged = 0xfecf4e37u;
		public const uint AddAnnotation = 0xfecf4e3eu;
		public const uint Apply = 0xfecf4e2cu;
		public const uint BeginEditing = 0xfecf4e61u;
		public const uint Edit = 0xfecf4e62u;
		public const uint GetCursor = 0xfecf4e53u;
		public const uint GetCursorBytes = 0xfecf4e55u;
		public const uint GetLength = 0xfecf4e56u;
		public const uint GetMarked = 0xfecf4e52u;
		public const uint GetMessageForAnnotation = 0xfecf4e41u;
		public const uint GetVisibleOffset = 0xfecf4e5du;
		public const uint HitTest = 0xfecf4e58u;
		public const uint Load = 0xfecf4e34u;
		public const uint LoadAbstract = 0xfecf4e36u;
		public const uint LoadMemory = 0xfecf4e35u;
		public const uint Read = 0xfecf4e21u;
		public const uint ReadData = 0xfecf4e5eu;
		public const uint ReadDataCompleted = 0xfecf4e5fu;
		public const uint ReadDataReply = 0xfecf4e60u;
		public const uint Redo = 0xfecf4e2bu;
		public const uint RemoveAnnotation = 0xfecf4e3fu;
		public const uint RemoveAnnotations = 0xfecf4e40u;
		public const uint SetCursor = 0xfecf4e54u;
		public const uint SetMarked = 0xfecf4e57u;
		public const uint SetVisibleOffset = 0xfecf4e5cu;
		public const uint ShowAnnotation = 0xfecf4e42u;
		public const uint Undo = 0xfecf4e2au;
		public const uint Write = 0xfecf4e22u;
	}
	public const uint Modified = 0xfecf4e25u;
	public const string Name = "Hex.mcc";
	public const uint ReadOnly = 0xfecf4e22u;
	public const uint Rows = 0xfecf4e26u;
	public const uint SelectedAnnotation = 0xfecf4e2du;
	public const uint Sleep = 0xfecf4e2bu;
	public static class Value
	{
		public static class SelectedAnnotation
		{
			public const int None = -1;
		}
	}
	public const uint VisibleRows = 0xfecf4e29u;
}

public static class Hyperlink
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint HoverTip = 0xfed10036u;
	public static class Method
	{
		public const uint Copy = 0xfed10035u;
		public const uint Decode = 0xfed10038u;
		public const uint Encode = 0xfed10037u;
		public const uint Follow = 0xfed10034u;
	}
	public const string Name = "Hyperlink.mcc";
	public const uint SetMax = 0x80424d0au;
	public const uint Text = 0x8042f8dcu;
	public const uint URI = 0xfed10033u;
	public static class Value
	{
		public static class Encode
		{
			public const int Count = 0;
		}
	}
}

public static class Image
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const int ArrowDown = 12;
	public const int ArrowLeft = 13;
	public const int ArrowRight = 14;
	public const int ArrowUp = 11;
	public const int Assign = 29;
	public const int BACKGROUND = 128;
	public const int BARBLOCK = 146;
	public const int BARDETAIL = 147;
	public const uint BuiltinSpec = 0x8042b907u;
	public const int ButtonBack = 2;
	public const int CheckMark = 15;
	public const int Chip = 25;
	public const int Close = 54;
	public const int Count = 55;
	public const int Cycle = 17;
	public const int Disk = 24;
	public const int Drawer = 22;
	public const int FILL = 131;
	public const int FILLBACK = 135;
	public const int FILLBACK2 = 138;
	public const int FILLSHINE = 136;
	public const uint FontMatch = 0x8042815du;
	public const uint FontMatchHeight = 0x80429f26u;
	public const uint FontMatchString = 0x804263c1u;
	public const uint FontMatchWidth = 0x804239bfu;
	public const uint FreeHoriz = 0x8042da84u;
	public const uint FreeVert = 0x8042ea28u;
	public const int GaugeEmpty = 46;
	public const int GaugeFull = 45;
	public const int GroupBack = 35;
	public const int GroupTitle = 52;
	public const int HSHADOWBACK = 140;
	public const int HSHADOWSHADOW = 142;
	public const int HSHINEBACK = 139;
	public const int HSHINESHINE = 141;
	public const int HardDisk = 23;
	public const int ImageButtonBack = 43;
	public const int ImageSelectedBack = 44;
	public const int LASTPAT = 147;
	public const int ListBack = 3;
	public const int ListCursor = 8;
	public const int ListSelCur = 10;
	public const int ListSelect = 9;
	public const int ListTitle = 51;
	public const int MARKBACKGROUND = 145;
	public const int MARKHALFSHINE = 144;
	public const int MARKSHINE = 143;
	public const int Menudisplay = 47;
	public const string Name = "Image.mui";
	public const int Network = 28;
	public const uint OldImage = 0x80424f3du;
	public const int PageBack = 40;
	public const int PopDrawer = 20;
	public const int PopFile = 19;
	public const int PopFont = 42;
	public const int PopUp = 18;
	public const int PopupBack = 6;
	public const int PropBack = 5;
	public const int PropKnob = 21;
	public const int PullOpen = 48;
	public const int RadioButton = 16;
	public const int ReadListBack = 41;
	public const int RegisterBack = 27;
	public const int RegisterTitle = 53;
	public const int RequesterBack = 1;
	public const int SHADOW = 129;
	public const int SHADOWBACK = 132;
	public const int SHADOWFILL = 133;
	public const int SHADOWSHINE = 134;
	public const int SHINE = 130;
	public const int SHINEBACK = 137;
	public const int SelectedBack = 7;
	public const int SliderBack = 36;
	public const int SliderKnob = 37;
	public const uint Spec = 0x804233d5u;
	public const uint State = 0x8042a3adu;
	public const int StringActiveBack = 50;
	public const int StringBack = 49;
	public const int TapeDown = 39;
	public const int TapePause = 32;
	public const int TapePlay = 30;
	public const int TapePlayBack = 31;
	public const int TapeRecord = 34;
	public const int TapeStop = 33;
	public const int TapeUp = 38;
	public const int TextBack = 4;
	public const int Volume = 26;
	public const int WindowBack = 0;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Imageadjust
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Imageadjust.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Imagebrowser
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Imagebrowser.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Imagedisplay
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Imagedisplay.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Imagespace
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Imagespace.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Keyadjust
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint AllowDoubleClick = 0x8042be82u;
	public const uint AllowMouseEvents = 0x8042b61cu;
	public const uint AllowMultipleKeys = 0x8042890bu;
	public const uint AllowTripleClick = 0x8042fd79u;
	public const uint ForceKeyCode = 0x8042fbadu;
	public const uint Key = 0x8042e161u;
	public const string Name = "Keyadjust.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Knob
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Knob.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Lamp
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Blue = 0x85b90006u;
	public const uint Color = 0x85b90002u;
	public const uint ColorType = 0x85b90003u;
	public const uint Green = 0x85b90005u;
	public static class Method
	{
		public const uint SetRGB = 0x85b90008u;
	}
	public const string Name = "Lamp.mcc";
	public const uint PenSpec = 0x85b90007u;
	public const uint Red = 0x85b90004u;
	public const uint Type = 0x85b90001u;
	public static class Value
	{
		public static class Color
		{
			public const int Connecting = 7;
			public const int Error = 3;
			public const int FatalError = 4;
			public const int LoadingData = 10;
			public const int LookingUp = 6;
			public const int Off = 0;
			public const int Ok = 1;
			public const int Processing = 5;
			public const int ReceivingData = 9;
			public const int SavingData = 11;
			public const int SendingData = 8;
			public const int Warning = 2;
		}
		public static class ColorType
		{
			public const int Color = 1;
			public const int PenSpec = 2;
			public const int UserDefined = 0;
		}
		public static class Type
		{
			public const int Big = 3;
			public const int Gigantic = 6;
			public const int Huge = 4;
			public const int Mammoth = 5;
			public const int Medium = 2;
			public const int Small = 1;
			public const int Tiny = 0;
		}
	}
}

public static class Levelmeter
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Label = 0x80420dd5u;
	public const string Name = "Levelmeter.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class List
{
	[global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = false)]
	public sealed class DisplayCallbackAttribute : global::System.Attribute
	{
		public DisplayCallbackAttribute(string? name = null) => Name = name;

		public string? Name { get; }
	}

	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Active = 0x8042391cu;
	public const uint AdjustHeight = 0x8042850du;
	public const uint AdjustWidth = 0x8042354au;
	public const uint AgainClick = 0x804214c2u;
	public const uint AutoLineHeight = 0x8042bc08u;
	public const uint AutoVisible = 0x8042a445u;
	public const uint ClickColumn = 0x8042d1b3u;
	public const uint ColumnOrder = 0x9d5100f6u;
	public const uint CompareHook = 0x80425c14u;
	public const uint ConstructHook = 0x8042894fu;
	public const uint DefClickColumn = 0x8042b296u;
	public const uint DestructHook = 0x804297ceu;
	public const uint DisplayHook = 0x8042b4d5u;
	public const uint DoubleClick = 0x80424635u;
	public const uint DragSortable = 0x80426099u;
	public const uint DragType = 0x80425cd3u;
	public const uint DropMark = 0x8042aba6u;
	public const uint Editable = 0x8042f9b9u;
	public const uint Entries = 0x80421654u;
	public const uint First = 0x804238d4u;
	public const uint Format = 0x80423c0au;
	public const uint HScrollerVisibility = 0x804280a6u;
	public const uint HideColumn = 0x80428052u;
	public const uint Input = 0x8042682du;
	public const uint InsertPosition = 0x8042d0cdu;
	public const uint LineHeight = 0x80425880u;
	public const uint MaxColumns = 0x8042a98bu;
	public static class Method
	{
		public const uint Clear = 0x8042ad89u;
		public const uint Compare = 0x80421b68u;
		public const uint Construct = 0x8042d662u;
		public const uint CreateEditObject = 0x804219aeu;
		public const uint CreateImage = 0x80429804u;
		public const uint DeleteImage = 0x80420f58u;
		public const uint Destruct = 0x80427d51u;
		public const uint Display = 0x80425377u;
		public const uint Edit = 0x8042843du;
		public const uint EditDone = 0x80423ab3u;
		public const uint EndEdit = 0x804203eeu;
		public const uint Exchange = 0x8042468cu;
		public const uint GetEntry = 0x804280ecu;
		public const uint Insert = 0x80426c87u;
		public const uint InsertSingle = 0x804254d5u;
		public const uint Jump = 0x8042baabu;
		public const uint Move = 0x804253c2u;
		public const uint NextSelected = 0x80425f17u;
		public const uint Redraw = 0x80427993u;
		public const uint Remove = 0x8042647eu;
		public const uint Select = 0x804252d8u;
		public const uint Sort = 0x80422275u;
		public const uint SortEntries = 0x80429e32u;
		public const uint TestPos = 0x80425f48u;
	}
	public const uint MinLineHeight = 0x8042d1c3u;
	public const uint MultiSelect = 0x80427e08u;
	public const uint MultiTestHook = 0x8042c2c6u;
	public const string Name = "List.mui";
	public const uint Pool = 0x80423431u;
	public const uint PoolPuddleSize = 0x8042a4ebu;
	public const uint PoolThreshSize = 0x8042c48cu;
	public const uint Quiet = 0x8042d8c7u;
	public const uint ScrollerPos = 0x8042b1b4u;
	public const uint SelectChange = 0x8042178fu;
	public const uint ShowColumn = 0x8042c840u;
	public const uint ShowDropMarks = 0x8042c6f3u;
	public const uint SortColumn = 0x8042cafbu;
	public const uint SourceArray = 0x8042c0a0u;
	public const uint Stripes = 0x8042a308u;
	public const uint Title = 0x80423e66u;
	public const uint TitleArray = 0x80427d95u;
	public const uint TitleClick = 0x80422fd9u;
	public const uint TopPixel = 0x80429df3u;
	public const uint TotalPixel = 0x8042a8f5u;
	public static class Value
	{
		public static class Active
		{
			public const int Bottom = -3;
			public const int Down = -5;
			public const int Off = -1;
			public const int PageDown = -7;
			public const int PageUp = -6;
			public const int Top = -2;
			public const int Up = -4;
		}
		public static class CompareHook
		{
			public const int String = -1;
			public const int StringArray = -2;
		}
		public static class ConstructHook
		{
			public const int String = -1;
			public const int StringArray = -2;
		}
		public static class DestructHook
		{
			public const int String = -1;
			public const int StringArray = -2;
		}
		public static class DisplayHook
		{
			public const int String = -1;
			public const int StringArray = -2;
		}
		public static class DragType
		{
			public const int Immediate = 1;
			public const int None = 0;
		}
		public static class Edit
		{
			public const int Active = -1;
		}
		public static class EditEntry
		{
			public const int Active = -1;
		}
		public static class EndEdit
		{
			public const int Abort = 1;
			public const int Done = 0;
			public const int Down = 5;
			public const int Next = 3;
			public const int Prev = 2;
			public const int Up = 4;
		}
		public static class Exchange
		{
			public const int Active = -1;
			public const int Bottom = -2;
			public const int Next = -3;
			public const int Previous = -4;
			public const int Top = 0;
		}
		public static class GetEntry
		{
			public const int Active = -1;
		}
		public static class HScrollerVisibility
		{
			public const int Always = 1;
			public const int Auto = 0;
			public const int Never = 2;
		}
		public static class Insert
		{
			public const int Active = -1;
			public const int Bottom = -3;
			public const int Sorted = -2;
			public const int Top = 0;
		}
		public static class Jump
		{
			public const int Active = -1;
			public const int Bottom = -2;
			public const int Down = -3;
			public const int Top = 0;
			public const int Up = -4;
		}
		public static class Move
		{
			public const int Active = -1;
			public const int Bottom = -2;
			public const int Next = -3;
			public const int Previous = -4;
			public const int Top = 0;
		}
		public static class MultiSelect
		{
			public const int Always = 3;
			public const int Default = 1;
			public const int None = 0;
			public const int Shifted = 2;
		}
		public static class NextSelected
		{
			public const int End = -1;
			public const int Start = -1;
		}
		public static class Redraw
		{
			public const int Active = -1;
			public const int All = -2;
			public const int Entry = -3;
		}
		public static class Remove
		{
			public const int Active = -1;
			public const int First = 0;
			public const int Last = -2;
			public const int Selected = -3;
		}
		public static class ScrollerPos
		{
			public const int Default = 0;
			public const int Left = 1;
			public const int None = 3;
			public const int Right = 2;
		}
		public static class Select
		{
			public const int Active = -1;
			public const int All = -2;
			public const int Ask = 3;
			public const int Off = 0;
			public const int On = 1;
			public const int Toggle = 2;
		}
	}
	public const uint Visible = 0x8042191fu;
	public const uint VisiblePixel = 0x804273e9u;
}

public static class Listtree
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Active = 0x80020020u;
	public const uint CloseHook = 0x80020033u;
	public const uint ConstructHook = 0x80020016u;
	public const uint DestructHook = 0x80020017u;
	public const uint DisplayHook = 0x80020018u;
	public const uint DoubleClick = 0x8002000du;
	public const uint DragDropSort = 0x80020031u;
	public const uint DuplicateNodeName = 0x8002003du;
	public const uint EmptyNodes = 0x80020030u;
	public const uint Format = 0x80020014u;
	public static class Method
	{
		public const uint Close = 0x8002001fu;
		public const uint Exchange = 0x80020008u;
		public const uint FindName = 0x8002003cu;
		public const uint GetEntry = 0x8002002bu;
		public const uint GetNr = 0x8002000eu;
		public const uint Insert = 0x80020011u;
		public const uint Move = 0x80020009u;
		public const uint Open = 0x8002001eu;
		public const uint Remove = 0x80020012u;
		public const uint Rename = 0x8002000cu;
		public const uint SetDropMark = 0x8002004cu;
		public const uint Sort = 0x80020029u;
		public const uint TestPos = 0x8002004bu;
	}
	public const uint MultiSelect = 0x800200c3u;
	public const uint NList = 0x800200c4u;
	public const string Name = "Listtree.mcc";
	public const uint OpenHook = 0x80020032u;
	public const uint Quiet = 0x8002000au;
	public const uint SortHook = 0x80020010u;
	public const uint Title = 0x80020015u;
	public const uint TreeColumn = 0x80020013u;
	public static class Value
	{
		public static class Active
		{
			public const int Off = 0;
		}
		public static class Close
		{
			public static class Flags
			{
				public const int Nr = 32768;
				public const int Visible = 16384;
			}
			public static class ListNode
			{
				public const int Active = -2;
				public const int Parent = -1;
				public const int Root = 0;
			}
			public static class TreeNode
			{
				public const int Active = -2;
				public const int All = -3;
				public const int Head = 0;
				public const int Tail = -1;
			}
		}
		public static class ConstructHook
		{
			public const int String = -1;
		}
		public static class DestructHook
		{
			public const int String = -1;
		}
		public static class DisplayHook
		{
			public const int Default = -1;
		}
		public static class DoubleClick
		{
			public const int All = -2;
			public const int Off = -1;
			public const int Tree = -3;
		}
		public static class Exchange
		{
			public static class ListNode1
			{
				public const int Active = -2;
				public const int Root = 0;
			}
			public static class ListNode2
			{
				public const int Active = -2;
				public const int Root = 0;
			}
			public static class TreeNode1
			{
				public const int Active = -2;
				public const int Head = 0;
				public const int Tail = -1;
			}
			public static class TreeNode2
			{
				public const int Active = -2;
				public const int Down = -6;
				public const int Head = 0;
				public const int Tail = -1;
				public const int Up = -5;
			}
		}
		public static class FindName
		{
			public static class Flags
			{
				public const int SameLevel = 32768;
				public const int Visible = 16384;
			}
			public static class ListNode
			{
				public const int Active = -2;
				public const int Root = 0;
			}
		}
		public static class GetEntry
		{
			public static class Flags
			{
				public const int SameLevel = 32768;
				public const int Visible = 16384;
			}
			public static class ListNode
			{
				public const int Active = -2;
				public const int Root = 0;
			}
			public static class Position
			{
				public const int Active = -2;
				public const int Head = 0;
				public const int Next = -3;
				public const int Parent = -5;
				public const int Previous = -4;
				public const int Tail = -1;
			}
		}
		public static class GetNr
		{
			public static class Flags
			{
				public const int CountAll = 32768;
				public const int CountLevel = 16384;
				public const int CountList = 8192;
				public const int ListEmpty = 4096;
			}
			public static class TreeNode
			{
				public const int Active = -2;
			}
		}
		public static class Insert
		{
			public static class Flags
			{
				public const int Active = 8192;
				public const int NextNode = 4096;
				public const int Nr = 32768;
				public const int Visible = 16384;
			}
			public static class ListNode
			{
				public const int Active = -2;
				public const int Root = 0;
			}
			public static class PrevNode
			{
				public const int Active = -2;
				public const int Head = 0;
				public const int Sorted = -4;
				public const int Tail = -1;
			}
		}
		public static class Move
		{
			public static class Flags
			{
				public const int Nr = 32768;
				public const int Visible = 16384;
			}
			public static class NewListNode
			{
				public const int Active = -2;
				public const int Root = 0;
			}
			public static class NewTreeNode
			{
				public const int Active = -2;
				public const int Head = 0;
				public const int Sorted = -4;
				public const int Tail = -1;
			}
			public static class OldListNode
			{
				public const int Active = -2;
				public const int Root = 0;
			}
			public static class OldTreeNode
			{
				public const int Active = -2;
				public const int Head = 0;
				public const int Tail = -1;
			}
		}
		public static class Open
		{
			public static class Flags
			{
				public const int Nr = 32768;
				public const int Visible = 16384;
			}
			public static class ListNode
			{
				public const int Active = -2;
				public const int Parent = -1;
				public const int Root = 0;
			}
			public static class TreeNode
			{
				public const int Active = -2;
				public const int All = -3;
				public const int Head = 0;
				public const int Tail = -1;
			}
		}
		public static class Remove
		{
			public static class Flags
			{
				public const int Nr = 32768;
				public const int Visible = 16384;
			}
			public static class ListNode
			{
				public const int Active = -2;
				public const int Root = 0;
			}
			public static class TreeNode
			{
				public const int Active = -2;
				public const int All = -3;
				public const int Head = 0;
				public const int Tail = -1;
			}
		}
		public static class Rename
		{
			public static class Flags
			{
				public const int NoRefresh = 512;
				public const int User = 256;
			}
			public static class TreeNode
			{
				public const int Active = -2;
			}
		}
		public static class SetDropMark
		{
			public static class Entry
			{
				public const int None = -1;
			}
			public static class Values
			{
				public const int Above = 1;
				public const int Below = 2;
				public const int None = 0;
				public const int Onto = 3;
				public const int Sorted = 4;
			}
		}
		public static class Sort
		{
			public static class Flags
			{
				public const int Nr = 32768;
				public const int Visible = 16384;
			}
			public static class ListNode
			{
				public const int Active = -2;
				public const int Root = 0;
			}
		}
		public static class SortHook
		{
			public const int Head = 0;
			public const int LeavesBottom = -4;
			public const int LeavesMixed = -3;
			public const int LeavesTop = -2;
			public const int Tail = -1;
		}
		public static class TestPos
		{
			public static class Result
			{
				public static class Flags
				{
					public const int Above = 1;
					public const int Below = 2;
					public const int None = 0;
					public const int Onto = 3;
					public const int Sorted = 4;
				}
			}
		}
	}
}

public static class Listview
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint AgainClick = 0x804214c2u;
	public const uint ClickColumn = 0x8042d1b3u;
	public const uint DefClickColumn = 0x8042b296u;
	public const uint DoubleClick = 0x80424635u;
	public const uint DragType = 0x80425cd3u;
	public const uint Input = 0x8042682du;
	public const uint List = 0x8042bcceu;
	public const uint MultiSelect = 0x80427e08u;
	public const string Name = "Listview.mui";
	public const uint ScrollerPos = 0x8042b1b4u;
	public const uint SelectChange = 0x8042178fu;
	public static class Value
	{
		public static class DragType
		{
			public const int Immediate = 1;
			public const int None = 0;
		}
		public static class MultiSelect
		{
			public const int Always = 3;
			public const int Default = 1;
			public const int None = 0;
			public const int Shifted = 2;
		}
		public static class ScrollerPos
		{
			public const int Default = 0;
			public const int Left = 1;
			public const int None = 3;
			public const int Right = 2;
		}
	}
}

public static class Login
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Login.mcc";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class MakeObject
{
	public const int BarTitle = 15;
	public const int Button = 2;
	public const int Checkmark = 3;
	public const int Cycle = 4;
	public const int HBar = 11;
	public const int HSpace = 9;
	public static class Label
	{
		public const int Value = 1;
		public const int Centered = 2048;
		public const int DontCopy = 16384;
		public const int DoubleFrame = 512;
		public const int FreeVert = 4096;
		public const int LeftAligned = 1024;
		public const int SingleFrame = 256;
		public const int Tiny = 8192;
	}
	public static class Menuitem
	{
		public const int Value = 14;
		public const int CopyStrings = 1073741824;
	}
	public static class MenustripNM
	{
		public const int Value = 13;
		public const int CommandKeyCheck = 1;
	}
	public const int NumericButton = 16;
	public const int PopButton = 8;
	public const int Radio = 5;
	public const int Slider = 6;
	public const int String = 7;
	public const int VBar = 12;
	public const int VSpace = 10;
}

public static class Mccprefs
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public static class Method
	{
		public const uint ConfigToGadgets = 0x80427043u;
		public const uint GadgetsToConfig = 0x80425242u;
		public const uint RegisterGadget = 0x80424828u;
	}
	public const string Name = "Mccprefs.mui";
}

public static class Menu
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint CopyStrings = 0x8042dbe2u;
	public const uint Enabled = 0x8042ed48u;
	public const string Name = "Menu.mui";
	public const uint Title = 0x8042a0e3u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Menubar
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Menubar.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Menudisplay
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Menudisplay.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Menuitem
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Checked = 0x8042562au;
	public const uint Checkit = 0x80425aceu;
	public const uint CommandString = 0x8042b9ccu;
	public const uint CopyStrings = 0x8042dc1bu;
	public const uint Enabled = 0x8042ae0fu;
	public const uint Exclude = 0x80420bc6u;
	public const string Name = "Menuitem.mui";
	public const uint Object = 0x80424b21u;
	public const uint Shortcut = 0x80422030u;
	public const uint Title = 0x804218beu;
	public const uint Toggle = 0x80424d5cu;
	public const uint Trigger = 0x80426f32u;
	public static class Value
	{
		public static class Shortcut
		{
			public const int Check = -1;
		}
	}
}

public static class Menustrip
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint CaseSensitive = 0x8042d718u;
	public const uint Enabled = 0x8042815bu;
	public static class Method
	{
		public const uint ExitChange = 0x8042ce4du;
		public const uint InitChange = 0x8042dcd9u;
		public const uint Popup = 0x80420e76u;
		public const uint WillOpen = 0x804230e9u;
	}
	public const string Name = "Menustrip.mui";
}

public static class Method
{
	public const uint AskMinMax = 0x80423874u;
	public const uint Backfill = 0x80428d73u;
	public const uint BoopsiQuery = 0x80427157u;
	public const uint CallHook = 0x8042b96bu;
	public const uint CheckShortHelp = 0x80423c79u;
	public const uint Cleanup = 0x8042d985u;
	public const uint CloseCustomFont = 0x8042b27cu;
	public const uint ContextMenuAdd = 0x8042df9eu;
	public const uint ContextMenuBuild = 0x80429d2eu;
	public const uint ContextMenuChoice = 0x80420f0eu;
	public const uint CreateBubble = 0x80421c41u;
	public const uint CreateDragImage = 0x8042eb6fu;
	public const uint CreateShortHelp = 0x80428e93u;
	public const uint DeleteBubble = 0x804211afu;
	public const uint DeleteDragImage = 0x80423037u;
	public const uint DeleteShortHelp = 0x8042d35au;
	public const uint DoDrag = 0x804216bbu;
	public const uint DragBegin = 0x8042c03au;
	public const uint DragDrop = 0x8042c555u;
	public const uint DragEvent = 0x8042b774u;
	public const uint DragFinish = 0x804251f0u;
	public const uint DragQuery = 0x80420261u;
	public const uint DragReport = 0x8042edadu;
	public const uint Draw = 0x80426f3fu;
	public const uint DrawBackground = 0x804238cau;
	public const uint ExitResize = 0x80428431u;
	public const uint Export = 0x80420f1cu;
	public const uint FindObject = 0x8042038fu;
	public const uint FindUData = 0x8042c196u;
	public const uint GetConfigItem = 0x80423edbu;
	public const uint GetUData = 0x8042ed0cu;
	public const uint GoActive = 0x8042491au;
	public const uint GoInactive = 0x80422c0cu;
	public const uint HandleEvent = 0x80426d66u;
	public const uint HandleInput = 0x80422a1au;
	public const uint Hide = 0x8042f20fu;
	public const uint Import = 0x8042d012u;
	public const uint InitResize = 0x804292bdu;
	public const uint KillNotify = 0x8042d240u;
	public const uint KillNotifyObj = 0x8042b145u;
	public const uint Layout = 0x8042845bu;
	public const uint MultiSet = 0x8042d356u;
	public const uint NoNotifySet = 0x8042216fu;
	public const uint OpenCustomFont = 0x8042f3dcu;
	public const uint Relayout = 0x8042b381u;
	public const uint Set = 0x8042549au;
	public const uint SetAsString = 0x80422590u;
	public const uint SetUData = 0x8042c920u;
	public const uint SetUDataOnce = 0x8042ca19u;
	public const uint Setup = 0x80428354u;
	public const uint Show = 0x8042cc84u;
	public const uint TextDim = 0x80422ad7u;
	public const uint UpdateConfig = 0x8042b0a9u;
	public const uint WriteLong = 0x80428d86u;
	public const uint WriteString = 0x80424bf4u;
}

public static class Notify
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Method = 0x8042c9cbu;
	public const string Name = "Notify.mui";
	public static class Value
	{
		public const int Application = 3;
		public const int Parent = 4;
		public const int ParentParent = 5;
		public const int ParentParentParent = 6;
		public const int Self = 1;
		public const int Window = 2;
	}
}

public static class Numeric
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint CheckAllSizes = 0x80421594u;
	public const uint Default = 0x804263e8u;
	public const uint Format = 0x804263e9u;
	public const uint Max = 0x8042d78au;
	public static class Method
	{
		public const uint Decrease = 0x804243a7u;
		public const uint Increase = 0x80426ecdu;
		public const uint ScaleToValue = 0x8042032cu;
		public const uint SetDefault = 0x8042ab0au;
		public const uint Stringify = 0x80424891u;
		public const uint ValueToScale = 0x80423e4fu;
	}
	public const uint Min = 0x8042e404u;
	public const string Name = "Numeric.mui";
	public const uint RevLeftRight = 0x804294a7u;
	public const uint RevUpDown = 0x804252ddu;
	public const uint Reverse = 0x8042f2a0u;
	public const uint Value = 0x8042ae3au;
}

public static class NumericList
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Current = 0xaa123220u;
	public const string Name = "NumericList.mcc";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class NumericString
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "NumericString.mcc";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Numericbutton
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Numericbutton.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Objectmap
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint AutoLock = 0x8042e65fu;
	public const uint CopyKeys = 0x8042b964u;
	public static class Method
	{
		public const uint Clear = 0x80422ee5u;
		public const uint Find = 0x80426506u;
		public const uint Iterate = 0x804262bcu;
		public const uint IterationKey = 0x8042d7ffu;
		public const uint Remove = 0x8042f649u;
		public const uint Set = 0x80421ec5u;
	}
	public const string Name = "Objectmap.mui";
	public const uint Pool = 0x80422ed3u;
}

public static class Palette
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Entries = 0x8042a3d8u;
	public const uint Groupable = 0x80423e67u;
	public const string Name = "Palette.mui";
	public const uint Names = 0x8042c3a2u;
	public static class Value
	{
		public static class Entry
		{
			public const int End = -1;
		}
	}
}

public static class Panel
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public static class Method
	{
		public const uint Run = 0x8042d789u;
	}
	public const string Name = "Panel.mui";
}

public static class Penadjust
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Penadjust.mui";
	public const uint PSIMode = 0x80421cbbu;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Pendisplay
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint ARGB = 0x804278d0u;
	public static class Method
	{
		public const uint SetColormap = 0x80426c80u;
		public const uint SetMUIPen = 0x8042039du;
		public const uint SetRGB = 0x8042c131u;
	}
	public const string Name = "Pendisplay.mui";
	public const uint Pen = 0x8042a748u;
	public const uint RGBcolor = 0x8042a1a9u;
	public const uint Reference = 0x8042dc24u;
	public const uint Spec = 0x8042a204u;
	public const uint XRGB = 0x8042de8au;
}

public static class Piano
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint KeyColor = 0xfed90001u;
	public static class Method
	{
		public const uint KeyClear = 0xfed90002u;
		public const uint KeyEvent = 0xfed90001u;
	}
	public const string Name = "Piano.mcc";
	public const uint OctaveBeginAt = 0xfed90004u;
	public const uint Octaves = 0xfed90003u;
	public const uint ReadOnly = 0xfed90000u;
	public static class Value
	{
		public const int KeyColor = -2;
		public static class KeyEvent
		{
			public const int Down = 1;
			public const int Up = 0;
		}
	}
}

public static class Popasl
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Active = 0x80421b37u;
	public const uint MUIFontStyles = 0x8042897fu;
	public const string Name = "Popasl.mui";
	public const uint StartHook = 0x8042b703u;
	public const uint StopHook = 0x8042d8d2u;
	public const uint Type = 0x8042df3du;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Popcolor
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Popcolor.mui";
	public const uint ShowAlpha = 0x8042e102u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Popframe
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Popframe.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Popfrimage
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Popfrimage.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Popimage
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Popimage.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Poplist
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Array = 0x8042084cu;
	public const string Name = "Poplist.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Popmenu
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Popmenu.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Popobject
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Follow = 0x80424cb5u;
	public const uint Light = 0x8042a5a3u;
	public const string Name = "Popobject.mui";
	public const uint ObjStrHook = 0x8042db44u;
	public const uint Object = 0x804293e3u;
	public const uint StrObjHook = 0x8042fbe1u;
	public const uint Volatile = 0x804252ecu;
	public const uint WindowHook = 0x8042f194u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Poppen
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Poppen.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Popscreen
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Popscreen.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Popstring
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Button = 0x8042d0b9u;
	public const uint CloseHook = 0x804256bfu;
	public static class Method
	{
		public const uint Close = 0x8042dc52u;
		public const uint Open = 0x804258bau;
	}
	public const string Name = "Popstring.mui";
	public const uint OpenHook = 0x80429d00u;
	public const uint String = 0x804239eau;
	public const uint Toggle = 0x80422b7au;
}

public static class PowerTerm
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint CRasCRLF = 0xfecf0829u;
	public const uint ClickX = 0xfecf0825u;
	public const uint ClickY = 0xfecf0826u;
	public const uint CursorX = 0xfecf0822u;
	public const uint CursorY = 0xfecf0823u;
	public const uint DELasBS = 0xfecf082eu;
	public const uint DestructiveBS = 0xfecf082du;
	public const uint EatAllInput = 0xfecf0831u;
	public const uint EightBit = 0xfecf0828u;
	public const uint Emulation = 0xfecf081fu;
	public const uint FontHeight = 0xfecf0804u;
	public const uint FontWidth = 0xfecf0803u;
	public const uint Height = 0xfecf0821u;
	public const uint IconTitle = 0xfecf0807u;
	public const uint LFasCRLF = 0xfecf082bu;
	public const uint LocalAlt = 0xfecf080du;
	public static class Method
	{
		public const uint AddOut = 0xfecf0805u;
		public const uint Duplicate = 0xfecf0810u;
		public const uint FlushReview = 0xfecf081au;
		public const uint OutFlush = 0xfecf0813u;
		public const uint PasteFromClip = 0xfecf080bu;
		public const uint Reset = 0xfecf0814u;
		public const uint SavePlain = 0xfecf0815u;
		public const uint SavePlainFH = 0xfecf0818u;
		public const uint SaveStyle = 0xfecf0816u;
		public const uint SaveStyleFH = 0xfecf0819u;
		public const uint Scroll = 0xfecf081bu;
		public const uint Search = 0xfecf080cu;
		public const uint Select = 0xfecf0832u;
		public const uint SelectToClip = 0xfecf080au;
		public const uint SetAbsXY = 0xfecf0817u;
		public const uint Write = 0xfecf0812u;
		public const uint WriteUnicode = 0xfecf0809u;
	}
	public const uint MouseTracking = 0xfecf0808u;
	public const string Name = "PowerTerm.mcc";
	public const uint OutEnable = 0xfecf0811u;
	public const uint OutLen = 0xfecf081eu;
	public const uint OutPtr = 0xfecf081du;
	public const uint Resizable = 0xfecf0827u;
	public const uint ResizableHistory = 0xfecf0824u;
	public const uint SaveSettings = 0xfecf080eu;
	public const uint Scroller = 0xfecf081cu;
	public const uint SwapDELBS = 0xfecf082au;
	public const uint TabSize = 0xfecf082fu;
	public const uint TextMarking = 0xfecf0830u;
	public const uint UTFEnable = 0xfecf080fu;
	public const uint UnixPaths = 0xfecf0812u;
	public static class Value
	{
		public static class Emulation
		{
			public const int ANSI = 0;
			public const int Amiga = 4;
			public const int TTY = 2;
			public const int VT100 = 1;
			public const int XTerm = 3;
		}
		public static class LocalAlt
		{
			public const int Both = 0;
			public const int Left = 1;
			public const int None = 3;
			public const int Right = 2;
		}
		public static class Scroll
		{
			public const int End = 3;
			public const int Home = 2;
			public const int Normal = 0;
			public const int Page = 1;
		}
		public static class Search
		{
			public const int ASCII = 0;
			public const int U16BE = 2;
			public const int U16LE = 3;
			public const int U32BE = 4;
			public const int U32LE = 5;
			public const int UTF8 = 1;
		}
		public static class Select
		{
			public const int All = 1;
			public const int None = 0;
		}
		public static class WriteUnicode
		{
			public const int U16BE = 2;
			public const int U16LE = 3;
			public const int U32BE = 4;
			public const int U32LE = 5;
			public const int UTF8 = 1;
		}
	}
	public const uint Width = 0xfecf0820u;
	public const uint WindowTitle = 0xfecf0806u;
	public const uint Wrap = 0xfecf082cu;
	public const uint _8Bit = 0xfecf0828u;
}

public static class Process
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(NameClass), tags);

	public const uint AutoLaunch = 0x80428855u;
	public static class Method
	{
		public const uint Kill = 0x804264cfu;
		public const uint Launch = 0x80425df7u;
		public const uint Process = 0x804230aau;
		public const uint Signal = 0x8042e791u;
	}
	public const uint Name = 0x8042732bu;
	public const string NameClass = "Process.mui";
	public const uint Priority = 0x80422a54u;
	public const uint SourceClass = 0x8042cf8bu;
	public const uint SourceObject = 0x804212a2u;
	public const uint StackSize = 0x804230d0u;
	public const uint Task = 0x8042b123u;
}

public static class Prop
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint DeltaFactor = 0x80427c5eu;
	public const uint Entries = 0x8042fbdbu;
	public const uint First = 0x8042d4b2u;
	public const uint Horiz = 0x8042f4f3u;
	public static class Method
	{
		public const uint Decrease = 0x80420dd1u;
		public const uint Increase = 0x8042cac0u;
	}
	public const string Name = "Prop.mui";
	public const uint Slider = 0x80429c3au;
	public const uint UseWinBorder = 0x8042deeeu;
	public static class Value
	{
		public static class UseWinBorder
		{
			public const int Bottom = 3;
			public const int Left = 1;
			public const int None = 0;
			public const int Right = 2;
		}
	}
	public const uint Visible = 0x8042fea6u;
}

public static class Pubscreen
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Pubscreen.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Pubscreenadjust
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Pubscreenadjust.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Pubscreenlist
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Pubscreenlist.mui";
	public const uint Selection = 0x8042fe58u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Pubscreenpanel
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Pubscreenpanel.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Radio
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Active = 0x80429b41u;
	public const uint Entries = 0x8042b6a1u;
	public const string Name = "Radio.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Rawimage
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Data = 0xfed10014u;
	public const string Name = "Rawimage.mcc";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Rectangle
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint BarTitle = 0x80426689u;
	public const uint HBar = 0x8042c943u;
	public const string Name = "Rectangle.mui";
	public const uint VBar = 0x80422204u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Register
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Frame = 0x8042349bu;
	public const string Name = "Register.mui";
	public const uint Titles = 0x804297ecu;
	public static class Value
	{
		public static class Titles
		{
			public const int Frame = -2;
			public const int UData = -1;
		}
	}
}

public static class Rootgrp
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Rootgrp.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Scale
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Horiz = 0x8042919au;
	public const string Name = "Scale.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Scintilla
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint ActiveEditor = 0xfecca001u;
	public const uint ClipStripANSI = 0xfecca022u;
	public const uint ClipUnit = 0xfecca020u;
	public const uint LexerChanged = 0xfecca00au;
	public static class Method
	{
		public const uint AutoComplete = 0xfecca004u;
		public const uint AutoDoc = 0xfecca006u;
		public const uint BindWithProjectIndex = 0xfecca023u;
		public const uint CallTip = 0xfecca005u;
		public const uint ContextMenuAdd = 0xfecca024u;
		public const uint Definition = 0xfecca008u;
		public const uint Include = 0xfecca007u;
		public const uint RexxCommand = 0xfecca021u;
	}
	public const string Name = "Scintilla.mcc";
	public const uint Notify = 0xfecca003u;
	public const uint dummy = 0xfecca000u;
}

public static class Screenmodepanel
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Screenmodepanel.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Screenspace
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Screenspace.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Scrmodelist
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Scrmodelist.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Scrollbar
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const string Name = "Scrollbar.mui";
	public const uint Type = 0x8042fb6bu;
	public static class Value
	{
		public static class Type
		{
			public const int Bottom = 1;
			public const int Default = 0;
			public const int None = 4;
			public const int Sym = 3;
			public const int Top = 2;
		}
	}
}

public static class Scrollgroup
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint AutoBars = 0x8042f50eu;
	public const uint Contents = 0x80421261u;
	public const uint FreeHoriz = 0x804292f3u;
	public const uint FreeVert = 0x804224f2u;
	public const uint HorizBar = 0x8042b63du;
	public const string Name = "Scrollgroup.mui";
	public const uint NoHorizBar = 0x8042cab1u;
	public const uint NoVertBar = 0x804264c3u;
	public const uint UseWinBorder = 0x804284c1u;
	public const uint VertBar = 0x8042cdc0u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Selectgroup
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Active = 0x80421788u;
	public const string Name = "Selectgroup.mui";
	public static class Value
	{
		public static class Active
		{
			public const int Next = -1;
			public const int Prev = -2;
		}
	}
}

public static class Semaphore
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public static class Method
	{
		public const uint Attempt = 0x80426ce2u;
		public const uint AttemptShared = 0x80422551u;
		public const uint Obtain = 0x804276f0u;
		public const uint ObtainShared = 0x8042ea02u;
		public const uint Release = 0x80421f2du;
	}
	public const string Name = "Semaphore.mui";
}

public static class Settings
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Settings.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Settingsgroup
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public static class Method
	{
		public const uint ConfigToGadgets = 0x80427043u;
		public const uint GadgetsToConfig = 0x80425242u;
	}
	public const string Name = "Settingsgroup.mui";
}

public static class Slave
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Application = 0x80427767u;
	public const uint Class = 0x80420f8cu;
	public static class Method
	{
		public const uint Cleanup = 0x80425e72u;
		public const uint Dispatch = 0x8042361fu;
		public const uint Error = 0x8042e544u;
		public const uint Setup = 0x80429faau;
		public const uint SignalsReceived = 0x8042d21au;
	}
	public const string Name = "Slave.mui";
	public const uint Object = 0x804202abu;
}

public static class Slider
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Horiz = 0x8042fad1u;
	public const uint Level = 0x8042ae3au;
	public const uint Max = 0x8042d78au;
	public const uint Min = 0x8042e404u;
	public const string Name = "Slider.mui";
	public const uint Quiet = 0x80420b26u;
	public const uint Reverse = 0x8042f2a0u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class String
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Accept = 0x8042e3e1u;
	public const uint Acknowledge = 0x8042026cu;
	public const uint AdvanceOnCR = 0x804226deu;
	public const uint AttachedList = 0x80420fd2u;
	public const uint BufferPos = 0x80428b6cu;
	public const uint Contents = 0x80428ffdu;
	public const uint DisplayPos = 0x8042ccbfu;
	public const uint EditHook = 0x80424c33u;
	public const uint Editable = 0x8042c94bu;
	public const uint Format = 0x80427484u;
	public const uint Integer = 0x80426e8au;
	public const uint Integer64 = 0x80424820u;
	public const uint LonelyEditHook = 0x80421569u;
	public const uint MaxLen = 0x80424984u;
	public const uint Multiline = 0x8042d18bu;
	public const string Name = "String.mui";
	public const uint Placeholder = 0x8042ae65u;
	public const uint Reject = 0x8042179cu;
	public const uint ScrollHeight = 0x8042be8bu;
	public const uint ScrollLeft = 0x8042bd0du;
	public const uint ScrollTop = 0x8042f4e5u;
	public const uint ScrollVisibleHeight = 0x8042791eu;
	public const uint ScrollVisibleWidth = 0x8042d280u;
	public const uint ScrollWidth = 0x80420fb5u;
	public const uint Secret = 0x80428769u;
	public const uint SpellChecking = 0x804266c6u;
	public static class Value
	{
		public static class Format
		{
			public const int Center = 1;
			public const int Left = 0;
			public const int Right = 2;
		}
	}
}

public static class Stringscroll
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint HorizBar = 0x8042e049u;
	public const string Name = "Stringscroll.mui";
	public const uint NoInput = 0x8042b2f3u;
	public const uint SetMin = 0x8042cbbbu;
	public const uint SetVMin = 0x80420115u;
	public const uint String = 0x804256a2u;
	public const uint UseWinBorder = 0x80422a61u;
	public const uint VertBar = 0x804232f8u;
	public const uint VertScrollerOnly = 0x8042873bu;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Switchitem
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public static class Method
	{
		public const uint Create = 0x80421ef8u;
	}
	public const string Name = "Switchitem.mui";
}

public static class Switchpanel
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Current = 0x80427e97u;
	public static class Method
	{
		public const uint Create = 0x80427bb4u;
	}
	public const string Name = "Switchpanel.mui";
	public static class Value
	{
		public static class Current
		{
			public const int First = -1;
			public const int Last = -2;
			public const int Next = -4;
			public const int NextGroup = -6;
			public const int Prev = -3;
			public const int PrevGroup = -5;
		}
	}
}

public static class Text
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Contents = 0x8042f8dcu;
	public const uint ControlChar = 0x8042e6d0u;
	public const uint Copy = 0x80427727u;
	public const uint HiChar = 0x804218ffu;
	public const uint Marking = 0x8042f780u;
	public const uint Method = 0x8042ee70u;
	public const string Name = "Text.mui";
	public const uint PreParse = 0x8042566du;
	public const uint SetMax = 0x80424d0au;
	public const uint SetMin = 0x80424e10u;
	public const uint SetVMax = 0x80420d8bu;
	public const uint Shorten = 0x80428bbdu;
	public const uint Shortened = 0x80425a86u;
	public static class Value
	{
		public static class Shorten
		{
			public const int Cutoff = 1;
			public const int Hide = 2;
			public const int Nothing = 0;
		}
	}
}

public static class TextCode
{
	public const string B = "\033b";
	public const string C = "\033c";
	public const string I = "\033i";
	public const string L = "\033l";
	public const string N = "\033n";
	public const string PH = "\0338";
	public const string PT = "\u00da";
	public const string R = "\033r";
	public const string U = "\033u";
}

public static class Textinput
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint AcceptChars = 0x851b078cu;
	public const uint Acknowledge = 0x851b0783u;
	public const uint AdvanceOnCR = 0x851b0780u;
	public const uint AttachedList = 0x851b078fu;
	public const uint AutoExpand = 0x851b0779u;
	public const uint Blinkrate = 0x851b077eu;
	public const uint Bookmark1 = 0x851b07a5u;
	public const uint Bookmark2 = 0x851b07a6u;
	public const uint Bookmark3 = 0x851b07a7u;
	public const uint Changed = 0x851b078eu;
	public const uint Contents = 0x851b077au;
	public const uint CursorPos = 0x851b0791u;
	public const uint CursorSize = 0x851b07a8u;
	public const uint Cursorstyle = 0x851b077fu;
	public const uint DefaultPopup = 0x851b0787u;
	public const uint Editable = 0x851b0794u;
	public const uint Font = 0x851b07aau;
	public const uint Format = 0x851b07a0u;
	public const uint HandleURLHook = 0x851b07a2u;
	public const uint Integer = 0x851b0784u;
	public const uint IsNumeric = 0x851b0789u;
	public const uint IsOld = 0x851b0796u;
	public const uint Lines = 0x851b0793u;
	public const uint MarkEnd = 0x851b0798u;
	public const uint MarkStart = 0x851b0797u;
	public const uint MaxLen = 0x851b0777u;
	public const uint MaxLines = 0x851b0778u;
	public const uint MaxVal = 0x851b078bu;
	public static class Method
	{
		public const uint Acknowledge = 0x851b0726u;
		public const uint AppendText = 0x851b0721u;
		public const uint DoBS = 0x851b0738u;
		public const uint DoBSSOL = 0x851b0739u;
		public const uint DoBSWord = 0x851b073du;
		public const uint DoBottom = 0x851b0730u;
		public const uint DoCopy = 0x851b071fu;
		public const uint DoCopyCut = 0x851b0755u;
		public const uint DoCut = 0x851b071eu;
		public const uint DoCutLine = 0x851b0754u;
		public const uint DoDecrementDec = 0x851b0748u;
		public const uint DoDel = 0x851b0736u;
		public const uint DoDelEOL = 0x851b0737u;
		public const uint DoDelLine = 0x851b071bu;
		public const uint DoDelWord = 0x851b073eu;
		public const uint DoDown = 0x851b072cu;
		public const uint DoGotoBookmark1 = 0x851b0751u;
		public const uint DoGotoBookmark2 = 0x851b0752u;
		public const uint DoGotoBookmark3 = 0x851b0753u;
		public const uint DoIncrementDec = 0x851b0747u;
		public const uint DoInsertFile = 0x851b073fu;
		public const uint DoLeft = 0x851b0729u;
		public const uint DoLineEnd = 0x851b072eu;
		public const uint DoLineStart = 0x851b072du;
		public const uint DoMarkAll = 0x851b071du;
		public const uint DoMarkStart = 0x851b071cu;
		public const uint DoNextGadget = 0x851b074du;
		public const uint DoNextWord = 0x851b0735u;
		public const uint DoPageDown = 0x851b0732u;
		public const uint DoPageUp = 0x851b0731u;
		public const uint DoPaste = 0x851b0720u;
		public const uint DoPopup = 0x851b0733u;
		public const uint DoPrevWord = 0x851b0734u;
		public const uint DoRedo = 0x851b074bu;
		public const uint DoRevert = 0x851b071au;
		public const uint DoRight = 0x851b072au;
		public const uint DoSetBookmark1 = 0x851b074eu;
		public const uint DoSetBookmark2 = 0x851b074fu;
		public const uint DoSetBookmark3 = 0x851b0750u;
		public const uint DoTab = 0x851b074cu;
		public const uint DoToggleCase = 0x851b0745u;
		public const uint DoToggleCaseEOW = 0x851b0746u;
		public const uint DoToggleWordwrap = 0x851b0725u;
		public const uint DoTop = 0x851b072fu;
		public const uint DoUndo = 0x851b074au;
		public const uint DoUp = 0x851b072bu;
		public const uint DoubleClick = 0x851b073cu;
		public const uint ExternalEdit = 0x851b0713u;
		public const uint HandleChar = 0x851b0741u;
		public const uint HandleURL = 0x851b0742u;
		public const uint InsertFromFile = 0x851b0740u;
		public const uint InsertText = 0x851b0728u;
		public const uint LoadFromFile = 0x851b0718u;
		public const uint SaveToFile = 0x851b0717u;
		public const uint TranslateEvent = 0x851b0727u;
	}
	public const uint MinVal = 0x851b078au;
	public const uint MinVersion = 0x851b0785u;
	public const uint MinimumWidth = 0x851b07aeu;
	public const uint Multiline = 0x851b0776u;
	public const string Name = "Textinput.mcc";
	public const uint NoCopy = 0x851b07adu;
	public const uint NoExtraSpacing = 0x851b07b0u;
	public const uint NoInput = 0x851b079au;
	public const uint PreParse = 0x851b079fu;
	public const uint ProhibitParse = 0x851b07acu;
	public const uint Quiet = 0x851b0782u;
	public const uint RejectChars = 0x851b078du;
	public const uint RemainActive = 0x851b0790u;
	public const uint ResetMarkOnCursor = 0x851b07afu;
	public const uint Secret = 0x851b0792u;
	public const uint SetMax = 0x851b079cu;
	public const uint SetMin = 0x851b079bu;
	public const uint SetVMax = 0x851b079du;
	public const uint SetVMin = 0x851b07a1u;
	public const uint Styles = 0x851b079eu;
	public const uint SuggestParse = 0x851b07abu;
	public const uint TabLen = 0x851b07a4u;
	public const uint Tabs = 0x851b07a3u;
	public const uint TmpExtension = 0x851b0781u;
	public const uint TopLine = 0x851b07a9u;
	public static class Value
	{
		public static class Font
		{
			public const int Fixed = 1;
			public const int Normal = 0;
		}
		public static class Format
		{
			public const int Center = 1;
			public const int Centre = 1;
			public const int Left = 0;
			public const int Right = 2;
		}
		public const int NoMark = -1;
		public static class ParseB
		{
			public const int Misspell = 1;
			public const int URL = 0;
		}
		public static class ParseF
		{
			public const int Misspell = 2;
			public const int URL = 1;
		}
		public static class Styles
		{
			public const int Email = 3;
			public const int HTML = 4;
			public const int IRC = 2;
			public const int MUI = 1;
			public const int None = 0;
		}
		public static class Tabs
		{
			public const int Disk = 2;
			public const int Ignore = 0;
			public const int Spaces = 1;
			public const int Value = 3;
		}
	}
	public const uint WordWrap = 0x851b0788u;
}

public static class Textinputscroll
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint HorizBar = 0x851b07b2u;
	public const string Name = "Textinputscroll.mcc";
	public const uint UseWinBorder = 0x851b0795u;
	public const uint VertBar = 0x851b07b1u;
	public const uint VertScrollerOnly = 0x851b0799u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Title
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Clickable = 0x80425959u;
	public const uint Closable = 0x80420402u;
	public const uint EventHandlerPriority = 0x804286bcu;
	public static class Method
	{
		public const uint Close = 0x8042303au;
		public const uint FindPage = 0x80423d0du;
		public const uint New = 0x804247a6u;
	}
	public const string Name = "Title.mui";
	public const uint Newable = 0x80424145u;
	public const uint OnLastClose = 0x804253cfu;
	public const uint Position = 0x804273a3u;
	public const uint Sortable = 0x804211f1u;
	public static class Value
	{
		public static class EventHandlerPriority
		{
			public const int Default = 0;
		}
		public static class OnLastClose
		{
			public const int Remove = 0;
			public const int WindowAction = 1;
		}
		public static class Position
		{
			public const int Bottom = 1;
			public const int Left = 2;
			public const int Right = 3;
			public const int Top = 0;
		}
	}
}

public static class Transition
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "Transition.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Urltext
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Active = 1u;
	public const uint DoOpenURL = 1u;
	public const uint DoVisitedPen = 1u;
	public const uint FallBack = 1u;
	public static class Method
	{
		public const uint Copy = 0xfed10035u;
		public const uint OpenURL = 0xfed10034u;
	}
	public const string Name = "Hyperlink.mcc";
	public const uint NoMenu = 1u;
	public const uint NoOpenURLPrefs = 1u;
	public const uint SetMax = 0x80424d0au;
	public const uint Text = 0x8042f8dcu;
	public const uint Underline = 1u;
	public const uint Url = 0xfed10033u;
	public const uint Visited = 1u;
}

public static class VGraphics
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const string Name = "VGraphics.mcc";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Value
{
	public static class BuiltinFont
	{
		public const int Big = -6;
		public const int Bubble = -12;
		public const int Button = -7;
		public const int Count = 14;
		public const int Fixed = -4;
		public const int Gauge = -9;
		public const int Huge = -13;
		public const int Inherit = 0;
		public const int Last = -13;
		public const int List = -2;
		public const int Menu = -10;
		public const int Normal = -1;
		public const int Slider = -8;
		public const int Tab = -11;
		public const int Tiny = -3;
		public const int Title = -5;
	}
	public static class ContextMenuBuild
	{
		public const uint Default = 0xffffffffu;
	}
	public static class CreateBubble
	{
		public const int DontHidePointer = 1;
	}
	public static class DoDrag
	{
		public const int Async = 1;
	}
	public static class DragQuery
	{
		public const int Accept = 1;
		public const int Refuse = 0;
	}
	public static class DragReport
	{
		public const int Abort = 0;
		public const int Continue = 1;
		public const int Lock = 2;
		public const int Refresh = 3;
	}
	public const int EveryTime = 1233727793;
	public static class Font
	{
		public const int Big = -6;
		public const int Bubble = -12;
		public const int Button = -7;
		public const int Count = 14;
		public const int Fixed = -4;
		public const int Gauge = -9;
		public const int Huge = -13;
		public const int Inherit = 0;
		public const int Last = -13;
		public const int List = -2;
		public const int Menu = -10;
		public const int Normal = -1;
		public const int Slider = -8;
		public const int Tab = -11;
		public const int Tiny = -3;
		public const int Title = -5;
	}
	public static class Frame
	{
		public const int Button = 1;
		public const int Count = 24;
		public const int Gauge = 8;
		public const int GaugeInner = 14;
		public const int Group = 9;
		public const int GroupTitle = 22;
		public const int ImageButton = 2;
		public const int InputList = 6;
		public const int Menudisplay = 15;
		public const int MenudisplayMenu = 16;
		public const int None = 0;
		public const int Page = 20;
		public const int PopUp = 10;
		public const int Prop = 7;
		public const int PropKnob = 17;
		public const int ReadList = 5;
		public const int Register = 21;
		public const int RegisterTitle = 23;
		public const int Requester = 19;
		public const int Slider = 12;
		public const int SliderKnob = 13;
		public const int String = 4;
		public const int Text = 3;
		public const int Virtual = 11;
		public const int Window = 18;
	}
	public static class InputMode
	{
		public const int Immediate = 2;
		public const int None = 0;
		public const int RelVerify = 1;
		public const int Toggle = 3;
	}
	public const int NotTriggerValue = 1233727795;
	public const int TriggerValue = 1233727793;
}

public static class Virtgroup
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Height = 0x80423038u;
	public const uint Input = 0x80427f7eu;
	public const uint Left = 0x80429371u;
	public const string Name = "Virtgroup.mui";
	public const uint Top = 0x80425200u;
	public const uint TryFit = 0x80429427u;
	public const uint Width = 0x80427c49u;

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Volumelist
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint ExampleMode = 0x804246a5u;
	public const string Name = "Volumelist.mui";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);
}

public static class Window
{
	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public const uint Activate = 0x80428d2fu;
	public const uint ActiveObject = 0x80427925u;
	public const uint AltHeight = 0x8042cce3u;
	public const uint AltLeftEdge = 0x80422d65u;
	public const uint AltTopEdge = 0x8042e99bu;
	public const uint AltWidth = 0x804260f4u;
	public const uint AppWindow = 0x804280cfu;
	public const uint Backdrop = 0x8042c0bbu;
	public const uint Borderless = 0x80429b79u;
	public const uint CloseGadget = 0x8042a110u;
	public const uint CloseRequest = 0x8042e86eu;
	public const uint DefaultObject = 0x804294d7u;
	public const uint DepthGadget = 0x80421923u;
	public const uint DisableKeys = 0x80424c36u;
	public const uint DragBar = 0x8042045du;
	public const uint FancyDrawing = 0x8042bd0eu;
	public const uint HasAlpha = 0x8042e632u;
	public const uint Height = 0x80425846u;
	public const uint ID = 0x804201bdu;
	public const uint InputEvent = 0x804247d8u;
	public const uint IsSubWindow = 0x8042b5aau;
	public const uint LeftEdge = 0x80426c65u;
	public const uint Menu = 0x8042db94u;
	public const uint MenuAction = 0x80427521u;
	public const uint Menustrip = 0x8042855eu;
	public static class Method
	{
		public const uint AddEventHandler = 0x804203b7u;
		public const uint Cleanup = 0x8042ab26u;
		public const uint GetMenuCheck = 0x80420414u;
		public const uint GetMenuState = 0x80420d2fu;
		public const uint RemEventHandler = 0x8042679eu;
		public const uint ScreenToBack = 0x8042913du;
		public const uint ScreenToFront = 0x804227a4u;
		public const uint SetCycleChain = 0x80426510u;
		public const uint SetMenuCheck = 0x80422243u;
		public const uint SetMenuState = 0x80422b5eu;
		public const uint Setup = 0x8042c34cu;
		public const uint Snapshot = 0x8042945eu;
		public const uint ToBack = 0x8042152eu;
		public const uint ToFront = 0x8042554fu;
	}
	public const uint MouseObject = 0x8042bf9bu;
	public const string Name = "Window.mui";
	public const uint NeedsMouseObject = 0x8042372au;
	public const uint NoMenus = 0x80429df5u;
	public const uint Opacity = 0x80429617u;
	public const uint Open = 0x80428aa0u;
	public const uint PanelWindow = 0x80429528u;
	public const uint PublicScreen = 0x804278e4u;
	public const uint RefWindow = 0x804201f4u;
	public const uint RootObject = 0x8042cba5u;
	public const uint Screen = 0x8042df4fu;
	public const uint ScreenTitle = 0x804234b0u;
	public const uint SizeGadget = 0x8042e33du;
	public const uint SizeRight = 0x80424780u;
	public const uint Sleep = 0x8042e7dbu;
	public const uint TabletMessages = 0x804217b7u;
	public const uint Title = 0x8042ad3du;
	public const uint TopEdge = 0x80427c66u;
	public const uint UseBottomBorderScroller = 0x80424e79u;
	public const uint UseLeftBorderScroller = 0x8042433eu;
	public const uint UseRightBorderScroller = 0x8042c05eu;
	public static class Value
	{
		public const uint IntuitionWindow = 0x80426a42u;
		public static class ActiveObject
		{
			public const int Down = -6;
			public const int Left = -3;
			public const int Next = -1;
			public const int None = 0;
			public const int Prev = -2;
			public const int Right = -4;
			public const int Up = -5;
		}
		public static class AltHeight
		{
			public const int Scaled = -1000;
		}
		public static class AltLeftEdge
		{
			public const int Centered = -1;
			public const int Moused = -2;
			public const int NoChange = -1000;
		}
		public static class AltTopEdge
		{
			public const int Centered = -1;
			public const int Moused = -2;
			public const int NoChange = -1000;
		}
		public static class AltWidth
		{
			public const int Scaled = -1000;
		}
		public static class Height
		{
			public const int Default = -1001;
			public const int Scaled = -1000;
		}
		public static class LeftEdge
		{
			public const int Centered = -1;
			public const int Moused = -2;
		}
		public static class Menu
		{
			public const int NoMenu = -1;
		}
		public static class TopEdge
		{
			public const int Centered = -1;
			public const int Moused = -2;
		}
		public static class Width
		{
			public const int Default = -1001;
			public const int Scaled = -1000;
		}
	}
	public const uint VisibleOnMaximize = 0x8042acfdu;
	public const uint Width = 0x8042dcaeu;
}
