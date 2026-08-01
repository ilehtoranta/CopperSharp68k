/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.MUI;

// Constants from the public headers of the commonly deployed third-party MUI
// custom classes. Like the built-in MUI declarations, these classes only
// describe object construction and BOOPSI dispatch; the .mcc files remain
// optional runtime components.
public static class TextEditor
{
	private const uint Dummy = 0xad000000u;

	public const string Name = "TextEditor.mcc";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint Contents = Dummy + 0x02u;
	public const uint CursorX = Dummy + 0x04u;
	public const uint CursorY = Dummy + 0x05u;
	public const uint DoubleClickHook = Dummy + 0x06u;
	public const uint TypeAndSpell = Dummy + 0x07u;
	public const uint ExportHook = Dummy + 0x08u;
	public const uint ExportWrap = Dummy + 0x09u;
	public const uint FixedFont = Dummy + 0x0au;
	public const uint Flow = Dummy + 0x0bu;
	public const uint HasChanged = Dummy + 0x0cu;
	public const uint Prop_DeltaFactor = Dummy + 0x0du;
	public const uint ImportHook = Dummy + 0x0eu;
	public const uint InsertMode = Dummy + 0x0fu;
	public const uint ImportWrap = Dummy + 0x10u;
	public const uint KeyBindings = Dummy + 0x11u;
	public const uint UndoAvailable = Dummy + 0x12u;
	public const uint RedoAvailable = Dummy + 0x13u;
	public const uint AreaMarked = Dummy + 0x14u;
	public const uint Prop_Entries = Dummy + 0x15u;
	public const uint Prop_Visible = Dummy + 0x16u;
	public const uint Quiet = Dummy + 0x17u;
	public const uint NumLock = Dummy + 0x18u;
	public const uint ReadOnly = Dummy + 0x19u;
	public const uint Slider = Dummy + 0x1au;
	public const uint InVirtualGroup = Dummy + 0x1bu;
	public const uint StyleBold = Dummy + 0x1cu;
	public const uint StyleItalic = Dummy + 0x1du;
	public const uint StyleUnderline = Dummy + 0x1eu;
	public const uint Prop_First = Dummy + 0x20u;
	public const uint WrapBorder = Dummy + 0x21u;
	public const uint Separator = Dummy + 0x2cu;
	public const uint Pen = Dummy + 0x2eu;
	public const uint ColorMap = Dummy + 0x2fu;
	public const uint MultiColorQuoting = Dummy + 0x31u;
	public const uint Rows = Dummy + 0x32u;
	public const uint Columns = Dummy + 0x33u;
	public const uint AutoClip = Dummy + 0x34u;
	public const uint CursorPosition = Dummy + 0x35u;
	public const uint KeyUpFocus = Dummy + 0x36u;
	public const uint UndoLevels = Dummy + 0x38u;
	public const uint WrapMode = Dummy + 0x39u;
	public const uint ActiveObjectOnClick = Dummy + 0x3au;
	public const uint PasteStyles = Dummy + 0x3bu;
	public const uint PasteColors = Dummy + 0x3cu;
	public const uint ConvertTabs = Dummy + 0x3du;
	public const uint WrapWords = Dummy + 0x3eu;
	public const uint TabSize = Dummy + 0x3fu;
	public const uint Keywords = Dummy + 0x40u;
	public const uint MatchedKeyword = Dummy + 0x41u;
	public const uint CursorIndex = Dummy + 0x42u;
	public const uint RGBMode = Dummy + 0x45u;
	public const uint HorizontalSlider = Dummy + 0x46u;
	public const uint GlobalFlow = Dummy + 0x4au;
	public const uint ContentsChanged = Dummy + 0x4bu;
	public const uint MetaDataChanged = Dummy + 0x4cu;
	public const uint InactiveContents = Dummy + 0x4du;
	public const uint FreeHoriz = Dummy + 0x4fu;
	public const uint FreeVert = Dummy + 0x50u;

	public static class Method
	{
		public const uint HandleError = Dummy + 0x1fu;
		public const uint AddKeyBindings = Dummy + 0x22u;
		public const uint ARexxCmd = Dummy + 0x23u;
		public const uint ClearText = Dummy + 0x24u;
		public const uint ExportText = Dummy + 0x25u;
		public const uint InsertText = Dummy + 0x26u;
		public const uint MacroBegin = Dummy + 0x27u;
		public const uint MacroEnd = Dummy + 0x28u;
		public const uint MacroExecute = Dummy + 0x29u;
		public const uint Replace = Dummy + 0x2au;
		public const uint Search = Dummy + 0x2bu;
		public const uint MarkText = Dummy + 0x2cu;
		public const uint QueryKeyAction = Dummy + 0x2du;
		public const uint SetBlock = Dummy + 0x2eu;
		public const uint BlockInfo = Dummy + 0x30u;
		public const uint ExportBlock = Dummy + 0x37u;
		public const uint CursorXYToIndex = Dummy + 0x43u;
		public const uint IndexToCursorXY = Dummy + 0x44u;
		public const uint Redraw = Dummy + 0x45u;
		public const uint TestPos = Dummy + 0x4eu;
	}

	public static class Value
	{
		public static class ExportHook
		{
			public const uint Plain = 0;
			public const uint EMail = 1;
			public const uint NoStyle = 2;
		}

		public static class Flow
		{
			public const uint Left = 0;
			public const uint Center = 1;
			public const uint Right = 2;
			public const uint Justified = 3;
		}

		public static class ImportHook
		{
			public const uint Plain = 0;
			public const uint EMail = 2;
			public const uint MIME = 3;
			public const uint MIMEQuoted = 4;
		}

		public static class InsertText
		{
			public const uint Cursor = 0;
			public const uint Top = 1;
			public const uint Bottom = 2;
		}

		public static class WrapMode
		{
			public const uint NoWrap = 0;
			public const uint SoftWrap = 1;
			public const uint HardWrap = 2;
		}

		public static class SearchFlags
		{
			public const uint FromTop = 1u << 0;
			public const uint Next = 1u << 1;
			public const uint CaseSensitive = 1u << 2;
			public const uint DOSPattern = 1u << 3;
			public const uint Backwards = 1u << 4;
		}

		public static class ExportBlockFlags
		{
			public const uint FullLines = 1u << 0;
			public const uint TakeBlock = 1u << 1;
		}

		public static class SetBlockFlags
		{
			public const uint Color = 1u << 0;
			public const uint StyleBold = 1u << 1;
			public const uint StyleItalic = 1u << 2;
			public const uint StyleUnderline = 1u << 3;
			public const uint Flow = 1u << 4;
		}
	}
}

public static class TheBar
{
	private const uint TagBase = 0xf76b022cu;

	public const string Name = "TheBar.mcc";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint MinVer = TagBase + 10u;
	public const uint Buttons = TagBase + 11u;
	public const uint Images = TagBase + 12u;
	public const uint Pics = TagBase + 13u;
	public const uint PicsDrawer = TagBase + 14u;
	public const uint ViewMode = TagBase + 15u;
	public const uint Borderless = TagBase + 16u;
	public const uint Raised = TagBase + 17u;
	public const uint Sunny = TagBase + 18u;
	public const uint Scaled = TagBase + 19u;
	public const uint SpacerIndex = TagBase + 20u;
	public const uint Strip = TagBase + 21u;
	public const uint StripBrush = TagBase + 22u;
	public const uint EnableKeys = TagBase + 23u;
	public const uint TextOnly = TagBase + 24u;
	public const uint LabelPos = TagBase + 25u;
	public const uint BarPos = TagBase + 26u;
	public const uint DragBar = TagBase + 27u;
	public const uint Frame = TagBase + 28u;
	public const uint Limbo = TagBase + 29u;
	public const uint Active = TagBase + 30u;
	public const uint Columns = TagBase + 31u;
	public const uint Rows = TagBase + 32u;
	public const uint FreeHoriz = TagBase + 33u;
	public const uint FreeVert = TagBase + 34u;
	public const uint Free = TagBase + 35u;
	public const uint BarSpacer = TagBase + 36u;
	public const uint RemoveSpacers = TagBase + 37u;
	public const uint SelImages = TagBase + 39u;
	public const uint DisImages = TagBase + 40u;
	public const uint SelPics = TagBase + 41u;
	public const uint DisPics = TagBase + 42u;
	public const uint SelStrip = TagBase + 43u;
	public const uint DisStrip = TagBase + 44u;
	public const uint SelStripBrush = TagBase + 45u;
	public const uint DisStripBrush = TagBase + 46u;
	public const uint StripRows = TagBase + 47u;
	public const uint StripCols = TagBase + 48u;
	public const uint StripHSpace = TagBase + 49u;
	public const uint StripVSpace = TagBase + 50u;
	public const uint HorizSpacing = TagBase + 51u;
	public const uint VertSpacing = TagBase + 52u;
	public const uint BarSpacerSpacing = TagBase + 53u;
	public const uint HorizInnerSpacing = TagBase + 54u;
	public const uint TopInnerSpacing = TagBase + 55u;
	public const uint BottomInnerSpacing = TagBase + 56u;
	public const uint LeftBarFrameSpacing = TagBase + 57u;
	public const uint RightBarFrameSpacing = TagBase + 58u;
	public const uint TopBarFrameSpacing = TagBase + 59u;
	public const uint BottomBarFrameSpacing = TagBase + 60u;
	public const uint HorizTextGfxSpacing = TagBase + 61u;
	public const uint VertTextGfxSpacing = TagBase + 62u;
	public const uint Precision = TagBase + 63u;
	public const uint Scale = TagBase + 65u;
	public const uint DisMode = TagBase + 66u;
	public const uint SpecialSelect = TagBase + 67u;
	public const uint TextOverUseShine = TagBase + 68u;
	public const uint IgnoreSelImages = TagBase + 69u;
	public const uint IgnoreDisImages = TagBase + 70u;
	public const uint DontMove = TagBase + 71u;
	public const uint MouseOver = TagBase + 72u;
	public const uint NtRaiseActive = TagBase + 73u;
	public const uint SpacersSize = TagBase + 74u;
	public const uint Appearance = TagBase + 75u;
	public const uint IgnoreAppearance = TagBase + 76u;
	public const uint HoveredButton = TagBase + 77u;

	public static class Method
	{
		public const uint Rebuild = TagBase + 0u;
		public const uint DeActivate = TagBase + 2u;
		public const uint AddButton = TagBase + 3u;
		public const uint AddSpacer = TagBase + 4u;
		public const uint GetObject = TagBase + 5u;
		public const uint DoOnButton = TagBase + 6u;
		public const uint SetAttr = TagBase + 7u;
		public const uint GetAttr = TagBase + 8u;
		public const uint Clear = TagBase + 9u;
		public const uint Sort = TagBase + 10u;
		public const uint Remove = TagBase + 11u;
		public const uint GetDragImage = TagBase + 12u;
		public const uint Notify = TagBase + 13u;
		public const uint KillNotify = TagBase + 14u;
		public const uint NoNotifySetAttr = TagBase + 15u;
	}

	public static class Attr
	{
		public const uint Hide = TagBase;
		public const uint Sleep = TagBase + 1u;
		public const uint Disabled = TagBase + 2u;
		public const uint Selected = TagBase + 3u;
	}

	public static class Value
	{
		public const uint Qualifier = 0x49893135u;
		public const uint SkipPic = 0xffffffffu;
		public const uint End = 0xffffffffu;
		public const uint BarSpacer = 0xfffffffeu;
		public const uint ButtonSpacer = 0xfffffffdu;
		public const uint ImageSpacer = 0xfffffffcu;

		public static class ViewMode
		{
			public const uint TextGfx = 0;
			public const uint Gfx = 1;
			public const uint Text = 2;
			public const uint Last = 3;
		}

		public static class LabelPos
		{
			public const uint Bottom = 0;
			public const uint Top = 1;
			public const uint Right = 2;
			public const uint Left = 3;
			public const uint Last = 4;
		}

		public static class BarPos
		{
			public const uint Left = 0;
			public const uint Center = 1;
			public const uint Right = 2;
			public const uint Up = Left;
			public const uint Down = Right;
			public const uint Last = 3;
		}

		public static class RemoveSpacers
		{
			public const uint Bar = 1u << 0;
			public const uint Button = 1u << 1;
			public const uint Image = 1u << 2;
			public const uint All = Bar | Button | Image;
		}

		public static class Precision
		{
			public const uint GUI = 0;
			public const uint Icon = 1;
			public const uint Image = 2;
			public const uint Exact = 3;
			public const uint Last = 4;
		}

		public static class DisMode
		{
			public const uint Shape = 0;
			public const uint Grid = 1;
			public const uint FullGrid = 2;
			public const uint Sunny = 3;
			public const uint Blend = 4;
			public const uint BlendGrey = 5;
			public const uint Last = 6;
		}

		public static class SpacersSize
		{
			public const uint Quarter = 0;
			public const uint Half = 1;
			public const uint One = 2;
			public const uint None = 3;
			public const uint OnePoint = 4;
			public const uint TwoPoint = 5;
			public const uint Last = 6;
			public const uint PointsFlag = 0x40u;
		}

		public static class ButtonFlags
		{
			public const uint NoClick = 1u << 0;
			public const uint Immediate = 1u << 1;
			public const uint Toggle = 1u << 2;
			public const uint Disabled = 1u << 3;
			public const uint Selected = 1u << 4;
			public const uint Sleep = 1u << 5;
			public const uint Hide = 1u << 6;
		}

		public static class Appearance
		{
			public const uint Borderless = 1u << 0;
			public const uint Raised = 1u << 1;
			public const uint Sunny = 1u << 2;
			public const uint Scaled = 1u << 3;
			public const uint BarSpacer = 1u << 4;
			public const uint EnableKeys = 1u << 5;
		}
	}
}

public static class BetterString
{
	public const string Name = "BetterString.mcc";

	public static uint New(uint tags) =>
		global::Amiga.MUIMaster.MUI_NewObjectTags(global::Amiga.CString.FromLiteral(Name), tags);

	public static uint Do(uint obj, uint message) =>
		global::Amiga.BOOPSI.DoMethodA(obj, message);

	public const uint SelectSize = 0xad001001u;
	public const uint StayActive = 0xad001003u;
	public const uint Columns = 0xad001005u;
	public const uint NoInput = 0xad001007u;
	public const uint KeyUpFocus = 0xad001008u;
	public const uint KeyDownFocus = 0xad001009u;
	public const uint InactiveContents = 0xad00100au;
	public const uint NoShortcuts = 0xad00100cu;
	public const uint SelectOnActive = 0xad00100du;
	public const uint NoNotify = 0xad00100eu;

	public static class Method
	{
		public const uint Insert = 0xad001002u;
		public const uint ClearSelected = 0xad001004u;
		public const uint FileNameStart = 0xad001006u;
		public const uint DoAction = 0xad00100bu;
	}

	public static class Value
	{
		public const uint InsertStartOfString = 0;
		public const uint InsertEndOfString = 0xfffffffeu;
		public const uint InsertBufferPos = 0xffffffffu;
		public const int FileNameStartVolume = -1;

		public static class DoAction
		{
			public const uint Cut = 1;
			public const uint Copy = 2;
			public const uint Paste = 3;
			public const uint SelectAll = 4;
			public const uint SelectNone = 5;
			public const uint Undo = 6;
			public const uint Redo = 7;
			public const uint Revert = 8;
			public const uint ToggleCase = 9;
			public const uint ToggleCaseWord = 10;
			public const uint IncreaseNum = 11;
			public const uint DecreaseNum = 12;
			public const uint HexToDec = 13;
			public const uint DecToHex = 14;
			public const uint NextFileComp = 15;
			public const uint PrevFileComp = 16;
			public const uint Delete = 17;
		}
	}
}
