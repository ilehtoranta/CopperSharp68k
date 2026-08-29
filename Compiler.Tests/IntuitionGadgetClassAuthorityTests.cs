using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class IntuitionGadgetClassAuthorityTests
{
	[Fact]
	public void ConstantsMatchNdk31GadgetClassHeader()
	{
		AssertSequence(0x8003_0000u, new uint[]
		{
			IntuitionGadgetClass.GA_Dummy, IntuitionGadgetClass.GA_Left,
			IntuitionGadgetClass.GA_RelRight, IntuitionGadgetClass.GA_Top,
			IntuitionGadgetClass.GA_RelBottom, IntuitionGadgetClass.GA_Width,
			IntuitionGadgetClass.GA_RelWidth, IntuitionGadgetClass.GA_Height,
			IntuitionGadgetClass.GA_RelHeight, IntuitionGadgetClass.GA_Text,
			IntuitionGadgetClass.GA_Image, IntuitionGadgetClass.GA_Border,
			IntuitionGadgetClass.GA_SelectRender, IntuitionGadgetClass.GA_Highlight,
			IntuitionGadgetClass.GA_Disabled, IntuitionGadgetClass.GA_GZZGadget,
			IntuitionGadgetClass.GA_ID, IntuitionGadgetClass.GA_UserData,
			IntuitionGadgetClass.GA_SpecialInfo, IntuitionGadgetClass.GA_Selected,
			IntuitionGadgetClass.GA_EndGadget, IntuitionGadgetClass.GA_Immediate,
			IntuitionGadgetClass.GA_RelVerify, IntuitionGadgetClass.GA_FollowMouse,
			IntuitionGadgetClass.GA_RightBorder, IntuitionGadgetClass.GA_LeftBorder,
			IntuitionGadgetClass.GA_TopBorder, IntuitionGadgetClass.GA_BottomBorder,
			IntuitionGadgetClass.GA_ToggleSelect, IntuitionGadgetClass.GA_SysGadget,
			IntuitionGadgetClass.GA_SysGType, IntuitionGadgetClass.GA_Previous,
			IntuitionGadgetClass.GA_Next, IntuitionGadgetClass.GA_DrawInfo,
			IntuitionGadgetClass.GA_IntuiText, IntuitionGadgetClass.GA_LabelImage,
			IntuitionGadgetClass.GA_TabCycle, IntuitionGadgetClass.GA_GadgetHelp,
			IntuitionGadgetClass.GA_Bounds, IntuitionGadgetClass.GA_RelSpecial,
		});
		AssertSequence(0x8003_1000u, new uint[]
		{
			IntuitionGadgetClass.PGA_Dummy, IntuitionGadgetClass.PGA_Freedom,
			IntuitionGadgetClass.PGA_Borderless, IntuitionGadgetClass.PGA_HorizPot,
			IntuitionGadgetClass.PGA_HorizBody, IntuitionGadgetClass.PGA_VertPot,
			IntuitionGadgetClass.PGA_VertBody, IntuitionGadgetClass.PGA_Total,
			IntuitionGadgetClass.PGA_Visible, IntuitionGadgetClass.PGA_Top,
			IntuitionGadgetClass.PGA_NewLook,
		});
		AssertSequence(0x8003_2000u, new uint[]
		{
			IntuitionGadgetClass.STRINGA_Dummy,
			IntuitionGadgetClass.STRINGA_MaxChars,
			IntuitionGadgetClass.STRINGA_Buffer,
			IntuitionGadgetClass.STRINGA_UndoBuffer,
			IntuitionGadgetClass.STRINGA_WorkBuffer,
			IntuitionGadgetClass.STRINGA_BufferPos,
			IntuitionGadgetClass.STRINGA_DispPos,
			IntuitionGadgetClass.STRINGA_AltKeyMap,
			IntuitionGadgetClass.STRINGA_Font,
			IntuitionGadgetClass.STRINGA_Pens,
			IntuitionGadgetClass.STRINGA_ActivePens,
			IntuitionGadgetClass.STRINGA_EditHook,
			IntuitionGadgetClass.STRINGA_EditModes,
			IntuitionGadgetClass.STRINGA_ReplaceMode,
			IntuitionGadgetClass.STRINGA_FixedFieldMode,
			IntuitionGadgetClass.STRINGA_NoFilterMode,
			IntuitionGadgetClass.STRINGA_Justification,
			IntuitionGadgetClass.STRINGA_LongVal,
			IntuitionGadgetClass.STRINGA_TextVal,
			IntuitionGadgetClass.STRINGA_ExitHelp,
		});
		AssertSequence(0x8003_8000u, new uint[]
		{
			IntuitionGadgetClass.LAYOUTA_Dummy,
			IntuitionGadgetClass.LAYOUTA_LayoutObj,
			IntuitionGadgetClass.LAYOUTA_Spacing,
			IntuitionGadgetClass.LAYOUTA_Orientation,
		});

		Assert.Equal(128u, IntuitionGadgetClass.SG_DEFAULTMAXCHARS);
		Assert.Equal(new uint[] { 0, 1, 2 }, new uint[]
		{
			IntuitionGadgetClass.LORIENT_NONE,
			IntuitionGadgetClass.LORIENT_HORIZ,
			IntuitionGadgetClass.LORIENT_VERT,
		});
		Assert.Equal(new uint[] { 0, 1, 2, 3, 4, 5, 6 }, new uint[]
		{
			IntuitionGadgetClass.GM_HITTEST, IntuitionGadgetClass.GM_RENDER,
			IntuitionGadgetClass.GM_GOACTIVE, IntuitionGadgetClass.GM_HANDLEINPUT,
			IntuitionGadgetClass.GM_GOINACTIVE, IntuitionGadgetClass.GM_HELPTEST,
			IntuitionGadgetClass.GM_LAYOUT,
		});
		Assert.Equal(uint.MaxValue, IntuitionGadgetClass.GM_Dummy);
		Assert.Equal(4u, IntuitionGadgetClass.GMR_GADGETHIT);
		Assert.Equal(0u, IntuitionGadgetClass.GMR_NOHELPHIT);
		Assert.Equal(uint.MaxValue, IntuitionGadgetClass.GMR_HELPHIT);
		Assert.Equal(0x0001_0000u, IntuitionGadgetClass.GMR_HELPCODE);
		Assert.Equal(new uint[] { 0, 2, 4, 8, 16, 32 }, new uint[]
		{
			IntuitionGadgetClass.GMR_MEACTIVE, IntuitionGadgetClass.GMR_NOREUSE,
			IntuitionGadgetClass.GMR_REUSE, IntuitionGadgetClass.GMR_VERIFY,
			IntuitionGadgetClass.GMR_NEXTACTIVE,
			IntuitionGadgetClass.GMR_PREVACTIVE,
		});
		Assert.Equal(new[] { 2, 1, 0 }, new[]
		{
			IntuitionGadgetClass.GREDRAW_UPDATE,
			IntuitionGadgetClass.GREDRAW_REDRAW,
			IntuitionGadgetClass.GREDRAW_TOGGLE,
		});
	}

	[Fact]
	public void StructuresMatchNdk31Pack2Layouts()
	{
		AssertLayout<GadgetInfoPens>(2, (nameof(GadgetInfoPens.DetailPen), 0),
			(nameof(GadgetInfoPens.BlockPen), 1));
		AssertLayout<GadgetInfo>(58, (nameof(GadgetInfo.gi_Screen), 0),
			(nameof(GadgetInfo.gi_Window), 4), (nameof(GadgetInfo.gi_Requester), 8),
			(nameof(GadgetInfo.gi_RastPort), 12), (nameof(GadgetInfo.gi_Layer), 16),
			(nameof(GadgetInfo.gi_Domain), 20), (nameof(GadgetInfo.gi_Pens), 28),
			(nameof(GadgetInfo.gi_DrInfo), 30), (nameof(GadgetInfo.gi_Reserved), 34));
		AssertLayout<gpHitTest>(12, (nameof(gpHitTest.MethodID), 0),
			(nameof(gpHitTest.gpht_GInfo), 4), (nameof(gpHitTest.gpht_Mouse), 8));
		AssertLayout<gpRender>(16, (nameof(gpRender.MethodID), 0),
			(nameof(gpRender.gpr_GInfo), 4), (nameof(gpRender.gpr_RPort), 8),
			(nameof(gpRender.gpr_Redraw), 12));
		AssertLayout<gpInput>(24, (nameof(gpInput.MethodID), 0),
			(nameof(gpInput.gpi_GInfo), 4), (nameof(gpInput.gpi_IEvent), 8),
			(nameof(gpInput.gpi_Termination), 12), (nameof(gpInput.gpi_Mouse), 16),
			(nameof(gpInput.gpi_TabletData), 20));
		AssertLayout<gpGoInactive>(12, (nameof(gpGoInactive.MethodID), 0),
			(nameof(gpGoInactive.gpgi_GInfo), 4), (nameof(gpGoInactive.gpgi_Abort), 8));
		AssertLayout<gpLayout>(12, (nameof(gpLayout.MethodID), 0),
			(nameof(gpLayout.gpl_GInfo), 4), (nameof(gpLayout.gpl_Initial), 8));
	}

	[Fact]
	public unsafe void GuestCodecsRoundTripEveryFieldInBigEndianMemory()
	{
		var memory = new TestMemory(0x1000u, 512);
		var address = APTR.FromPointer(0x1020u);
		var info = new GadgetInfo
		{
			gi_Screen = P(0x0102_0304), gi_Window = P(0x1112_1314),
			gi_Requester = P(0x2122_2324), gi_RastPort = P(0x3132_3334),
			gi_Layer = P(0x4142_4344),
			gi_Domain = new IBox { Left = -1, Top = 2, Width = 300, Height = -4 },
			gi_Pens = new GadgetInfoPens { DetailPen = 5, BlockPen = 6 },
			gi_DrInfo = P(0x5152_5354),
		};
		for (var index = 0; index < 6; index++)
			info.gi_Reserved[index] = 0x6061_6263u + (uint)index;
		IntuitionGadgetClassGuestCodec.WriteGadgetInfo(ref memory, address, info);
		var actualInfo = IntuitionGadgetClassGuestCodec.ReadGadgetInfo(ref memory, address);
		Assert.Equal(info.gi_Screen, actualInfo.gi_Screen);
		Assert.Equal(info.gi_Window, actualInfo.gi_Window);
		Assert.Equal(info.gi_Requester, actualInfo.gi_Requester);
		Assert.Equal(info.gi_RastPort, actualInfo.gi_RastPort);
		Assert.Equal(info.gi_Layer, actualInfo.gi_Layer);
		Assert.Equal(info.gi_Domain.Left, actualInfo.gi_Domain.Left);
		Assert.Equal(info.gi_Domain.Top, actualInfo.gi_Domain.Top);
		Assert.Equal(info.gi_Domain.Width, actualInfo.gi_Domain.Width);
		Assert.Equal(info.gi_Domain.Height, actualInfo.gi_Domain.Height);
		Assert.Equal(info.gi_Pens.DetailPen, actualInfo.gi_Pens.DetailPen);
		Assert.Equal(info.gi_Pens.BlockPen, actualInfo.gi_Pens.BlockPen);
		Assert.Equal(info.gi_DrInfo, actualInfo.gi_DrInfo);
		for (var index = 0; index < 6; index++)
			Assert.Equal(info.gi_Reserved[index], actualInfo.gi_Reserved[index]);

		var hit = new gpHitTest { MethodID = 1, gpht_GInfo = P(2),
			gpht_Mouse = new Point { X = -3, Y = 4 } };
		IntuitionGadgetClassGuestCodec.WriteHitTest(ref memory, address, hit);
		Assert.Equal(hit, IntuitionGadgetClassGuestCodec.ReadHitTest(ref memory, address));
		var render = new gpRender { MethodID = 5, gpr_GInfo = P(6),
			gpr_RPort = P(7), gpr_Redraw = -8 };
		IntuitionGadgetClassGuestCodec.WriteRender(ref memory, address, render);
		Assert.Equal(render, IntuitionGadgetClassGuestCodec.ReadRender(ref memory, address));
		var input = new gpInput { MethodID = 9, gpi_GInfo = P(10),
			gpi_IEvent = P(11), gpi_Termination = P(12),
			gpi_Mouse = new Point { X = 13, Y = -14 }, gpi_TabletData = P(15) };
		IntuitionGadgetClassGuestCodec.WriteInput(ref memory, address, input);
		Assert.Equal(input, IntuitionGadgetClassGuestCodec.ReadInput(ref memory, address));
		var inactive = new gpGoInactive { MethodID = 16, gpgi_GInfo = P(17),
			gpgi_Abort = 18 };
		IntuitionGadgetClassGuestCodec.WriteGoInactive(ref memory, address, inactive);
		Assert.Equal(inactive,
			IntuitionGadgetClassGuestCodec.ReadGoInactive(ref memory, address));
		var layout = new gpLayout { MethodID = 19, gpl_GInfo = P(20), gpl_Initial = 21 };
		IntuitionGadgetClassGuestCodec.WriteLayout(ref memory, address, layout);
		Assert.Equal(layout, IntuitionGadgetClassGuestCodec.ReadLayout(ref memory, address));
		Assert.Equal("CopperSharp.Sdk.Amiga.Support",
			typeof(IntuitionGadgetClassGuestCodec).Assembly.GetName().Name);
	}

	[Fact]
	public void BuiltInClassAndAttributeConstantsMatchNdk31Headers()
	{
		Assert.Equal(new[]
		{
			"rootclass", "imageclass", "frameiclass", "sysiclass",
			"fillrectclass", "gadgetclass", "propgclass", "strgclass",
			"buttongclass", "frbuttonclass", "groupgclass", "icclass",
			"modelclass", "itexticlass", "pointerclass",
		}, new[]
		{
			IntuitionClassId.Root, IntuitionClassId.Image,
			IntuitionClassId.FrameImage, IntuitionClassId.SystemImage,
			IntuitionClassId.FillRectangle, IntuitionClassId.Gadget,
			IntuitionClassId.ProportionalGadget, IntuitionClassId.StringGadget,
			IntuitionClassId.ButtonGadget, IntuitionClassId.FrameButton,
			IntuitionClassId.GroupGadget, IntuitionClassId.Interconnection,
			IntuitionClassId.Model, IntuitionClassId.IntuiTextImage,
			IntuitionClassId.Pointer,
		});
		AssertSequence(0x8002_0000u, new uint[]
		{
			IntuitionImageClass.IA_Dummy, IntuitionImageClass.IA_Left,
			IntuitionImageClass.IA_Top, IntuitionImageClass.IA_Width,
			IntuitionImageClass.IA_Height, IntuitionImageClass.IA_FGPen,
			IntuitionImageClass.IA_BGPen, IntuitionImageClass.IA_Data,
			IntuitionImageClass.IA_LineWidth, IntuitionImageClass.IA_ShadowPen,
			IntuitionImageClass.IA_HighlightPen, IntuitionImageClass.SYSIA_Size,
			IntuitionImageClass.SYSIA_Depth, IntuitionImageClass.SYSIA_Which,
			IntuitionImageClass.IA_Pens, IntuitionImageClass.IA_Resolution,
			IntuitionImageClass.IA_APattern, IntuitionImageClass.IA_APatSize,
			IntuitionImageClass.IA_Mode, IntuitionImageClass.IA_Font,
			IntuitionImageClass.IA_Outline, IntuitionImageClass.IA_Recessed,
			IntuitionImageClass.IA_DoubleEmboss,
			IntuitionImageClass.IA_EdgesOnly,
			IntuitionImageClass.SYSIA_DrawInfo,
			IntuitionImageClass.SYSIA_ReferenceFont,
			IntuitionImageClass.IA_SupportsDisable,
			IntuitionImageClass.IA_FrameType,
		});
		Assert.Equal(IntuitionImageClass.IA_Pens, IntuitionImageClass.SYSIA_Pens);
		Assert.Equal((short)-1, IntuitionImageClass.CUSTOMIMAGEDEPTH);
		Assert.Equal(new uint[] { 0, 1, 2 }, new uint[]
		{
			IntuitionImageClass.SYSISIZE_MEDRES,
			IntuitionImageClass.SYSISIZE_LOWRES,
			IntuitionImageClass.SYSISIZE_HIRES,
		});
		Assert.Equal(new uint[] { 0, 1, 2, 3, 5, 10, 11, 12, 13, 14, 15, 16, 17 },
			new uint[]
			{
				IntuitionImageClass.DEPTHIMAGE, IntuitionImageClass.ZOOMIMAGE,
				IntuitionImageClass.SIZEIMAGE, IntuitionImageClass.CLOSEIMAGE,
				IntuitionImageClass.SDEPTHIMAGE, IntuitionImageClass.LEFTIMAGE,
				IntuitionImageClass.UPIMAGE, IntuitionImageClass.RIGHTIMAGE,
				IntuitionImageClass.DOWNIMAGE, IntuitionImageClass.CHECKIMAGE,
				IntuitionImageClass.MXIMAGE, IntuitionImageClass.MENUCHECK,
				IntuitionImageClass.AMIGAKEY,
			});
		AssertSequence(0x202u, new uint[]
		{
			IntuitionImageClass.IM_DRAW, IntuitionImageClass.IM_HITTEST,
			IntuitionImageClass.IM_ERASE, IntuitionImageClass.IM_MOVE,
			IntuitionImageClass.IM_DRAWFRAME, IntuitionImageClass.IM_FRAMEBOX,
			IntuitionImageClass.IM_HITFRAME, IntuitionImageClass.IM_ERASEFRAME,
		});
		AssertSequence(0u, new uint[]
		{
			IntuitionImageClass.IDS_NORMAL, IntuitionImageClass.IDS_SELECTED,
			IntuitionImageClass.IDS_DISABLED, IntuitionImageClass.IDS_BUSY,
			IntuitionImageClass.IDS_INDETERMINATE,
			IntuitionImageClass.IDS_INACTIVENORMAL,
			IntuitionImageClass.IDS_INACTIVESELECTED,
			IntuitionImageClass.IDS_INACTIVEDISABLED,
			IntuitionImageClass.IDS_SELECTEDDISABLED,
		});
		Assert.Equal(IntuitionImageClass.IDS_INDETERMINATE,
			IntuitionImageClass.IDS_INDETERMINANT);
		AssertSequence(0x0401u, new uint[]
		{
			IntuitionInterconnectionClass.ICM_Dummy,
			IntuitionInterconnectionClass.ICM_SETLOOP,
			IntuitionInterconnectionClass.ICM_CLEARLOOP,
			IntuitionInterconnectionClass.ICM_CHECKLOOP,
		});
		AssertSequence(0x8004_0000u, new uint[]
		{
			IntuitionInterconnectionClass.ICA_Dummy,
			IntuitionInterconnectionClass.ICA_TARGET,
			IntuitionInterconnectionClass.ICA_MAP,
			IntuitionInterconnectionClass.ICSPECIAL_CODE,
		});
		Assert.Equal(uint.MaxValue, IntuitionInterconnectionClass.ICTARGET_IDCMP);
		AssertSequence(0x8003_9000u, new uint[]
		{
			IntuitionPointerClass.POINTERA_Dummy,
			IntuitionPointerClass.POINTERA_BitMap,
			IntuitionPointerClass.POINTERA_XOffset,
			IntuitionPointerClass.POINTERA_YOffset,
			IntuitionPointerClass.POINTERA_WordWidth,
			IntuitionPointerClass.POINTERA_XResolution,
			IntuitionPointerClass.POINTERA_YResolution,
		});
		Assert.Equal(new uint[] { 0, 1, 2, 3, 4, 5, 6 }, new uint[]
		{
			IntuitionPointerClass.POINTERXRESN_DEFAULT,
			IntuitionPointerClass.POINTERXRESN_140NS,
			IntuitionPointerClass.POINTERXRESN_70NS,
			IntuitionPointerClass.POINTERXRESN_35NS,
			IntuitionPointerClass.POINTERXRESN_SCREENRES,
			IntuitionPointerClass.POINTERXRESN_LORES,
			IntuitionPointerClass.POINTERXRESN_HIRES,
		});
		Assert.Equal(new uint[] { 0, 2, 3, 4, 5 }, new uint[]
		{
			IntuitionPointerClass.POINTERYRESN_DEFAULT,
			IntuitionPointerClass.POINTERYRESN_HIGH,
			IntuitionPointerClass.POINTERYRESN_HIGHASPECT,
			IntuitionPointerClass.POINTERYRESN_SCREENRES,
			IntuitionPointerClass.POINTERYRESN_SCREENRESASPECT,
		});
	}

	[Fact]
	public void ImageMessagesMatchNdk31Pack2Layouts()
	{
		AssertLayout<ImageDimensions>(4, (nameof(ImageDimensions.Width), 0),
			(nameof(ImageDimensions.Height), 2));
		AssertLayout<impFrameBox>(20, (nameof(impFrameBox.MethodID), 0),
			(nameof(impFrameBox.imp_ContentsBox), 4),
			(nameof(impFrameBox.imp_FrameBox), 8),
			(nameof(impFrameBox.imp_DrInfo), 12),
			(nameof(impFrameBox.imp_FrameFlags), 16));
		AssertLayout<impDraw>(24, (nameof(impDraw.MethodID), 0),
			(nameof(impDraw.imp_RPort), 4), (nameof(impDraw.imp_Offset), 8),
			(nameof(impDraw.imp_State), 12), (nameof(impDraw.imp_DrInfo), 16),
			(nameof(impDraw.imp_Dimensions), 20));
		AssertLayout<impErase>(16, (nameof(impErase.MethodID), 0),
			(nameof(impErase.imp_RPort), 4), (nameof(impErase.imp_Offset), 8),
			(nameof(impErase.imp_Dimensions), 12));
		AssertLayout<impHitTest>(12, (nameof(impHitTest.MethodID), 0),
			(nameof(impHitTest.imp_Point), 4),
			(nameof(impHitTest.imp_Dimensions), 8));
	}

	[Fact]
	public void ImageMessageCodecsRoundTripEveryField()
	{
		var memory = new TestMemory(0x2000u, 256);
		var address = P(0x2020u);
		var frame = new impFrameBox { MethodID = 1, imp_ContentsBox = P(2),
			imp_FrameBox = P(3), imp_DrInfo = P(4), imp_FrameFlags = 5 };
		IntuitionImageClassGuestCodec.WriteFrameBox(ref memory, address, frame);
		Assert.Equal(frame,
			IntuitionImageClassGuestCodec.ReadFrameBox(ref memory, address));
		var draw = new impDraw { MethodID = 6, imp_RPort = P(7),
			imp_Offset = new Point { X = -8, Y = 9 }, imp_State = 10,
			imp_DrInfo = P(11),
			imp_Dimensions = new ImageDimensions { Width = 12, Height = -13 } };
		IntuitionImageClassGuestCodec.WriteDraw(ref memory, address, draw);
		Assert.Equal(draw, IntuitionImageClassGuestCodec.ReadDraw(ref memory, address));
		var erase = new impErase { MethodID = 14, imp_RPort = P(15),
			imp_Offset = new Point { X = 16, Y = -17 },
			imp_Dimensions = new ImageDimensions { Width = -18, Height = 19 } };
		IntuitionImageClassGuestCodec.WriteErase(ref memory, address, erase);
		Assert.Equal(erase,
			IntuitionImageClassGuestCodec.ReadErase(ref memory, address));
		var hit = new impHitTest { MethodID = 20,
			imp_Point = new Point { X = -21, Y = 22 },
			imp_Dimensions = new ImageDimensions { Width = 23, Height = -24 } };
		IntuitionImageClassGuestCodec.WriteHitTest(ref memory, address, hit);
		Assert.Equal(hit,
			IntuitionImageClassGuestCodec.ReadHitTest(ref memory, address));
		Assert.Equal("CopperSharp.Sdk.Amiga.Support",
			typeof(IntuitionImageClassGuestCodec).Assembly.GetName().Name);
	}

	[Fact]
	public void ScreenTagsAndErrorsMatchNdk31ScreensHeader()
	{
		AssertSequence(0x8000_0020u, new uint[]
		{
			IntuitionScreenTags.SA_Dummy, IntuitionScreenTags.SA_Left,
			IntuitionScreenTags.SA_Top, IntuitionScreenTags.SA_Width,
			IntuitionScreenTags.SA_Height, IntuitionScreenTags.SA_Depth,
			IntuitionScreenTags.SA_DetailPen, IntuitionScreenTags.SA_BlockPen,
			IntuitionScreenTags.SA_Title, IntuitionScreenTags.SA_Colors,
			IntuitionScreenTags.SA_ErrorCode, IntuitionScreenTags.SA_Font,
			IntuitionScreenTags.SA_SysFont, IntuitionScreenTags.SA_Type,
			IntuitionScreenTags.SA_BitMap, IntuitionScreenTags.SA_PubName,
			IntuitionScreenTags.SA_PubSig, IntuitionScreenTags.SA_PubTask,
			IntuitionScreenTags.SA_DisplayID, IntuitionScreenTags.SA_DClip,
			IntuitionScreenTags.SA_Overscan, IntuitionScreenTags.SA_Obsolete1,
			IntuitionScreenTags.SA_ShowTitle, IntuitionScreenTags.SA_Behind,
			IntuitionScreenTags.SA_Quiet, IntuitionScreenTags.SA_AutoScroll,
			IntuitionScreenTags.SA_Pens, IntuitionScreenTags.SA_FullPalette,
			IntuitionScreenTags.SA_ColorMapEntries, IntuitionScreenTags.SA_Parent,
			IntuitionScreenTags.SA_Draggable, IntuitionScreenTags.SA_Exclusive,
			IntuitionScreenTags.SA_SharePens, IntuitionScreenTags.SA_BackFill,
			IntuitionScreenTags.SA_Interleaved, IntuitionScreenTags.SA_Colors32,
			IntuitionScreenTags.SA_VideoControl, IntuitionScreenTags.SA_FrontChild,
			IntuitionScreenTags.SA_BackChild,
			IntuitionScreenTags.SA_LikeWorkbench,
			IntuitionScreenTags.SA_Reserved,
			IntuitionScreenTags.SA_MinimizeISG,
		});
		Assert.Equal(0x8000_0001u, IntuitionScreenTags.NSTAG_EXT_VPMODE);
		Assert.Equal(-1, IntuitionScreenTags.STDSCREENHEIGHT);
		Assert.Equal(-1, IntuitionScreenTags.STDSCREENWIDTH);
		Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, new[]
		{
			IntuitionScreenTags.OSERR_NOMONITOR,
			IntuitionScreenTags.OSERR_NOCHIPS,
			IntuitionScreenTags.OSERR_NOMEM,
			IntuitionScreenTags.OSERR_NOCHIPMEM,
			IntuitionScreenTags.OSERR_PUBNOTUNIQUE,
			IntuitionScreenTags.OSERR_UNKNOWNMODE,
			IntuitionScreenTags.OSERR_TOODEEP,
			IntuitionScreenTags.OSERR_ATTACHFAIL,
			IntuitionScreenTags.OSERR_NOTAVAILABLE,
		});
	}

	[Fact]
	public void WindowAndPointerTagsMatchNdk31IntuitionHeader()
	{
		AssertSequence(0x8000_0063u, new uint[]
		{
			IntuitionWindowTags.WA_Dummy, IntuitionWindowTags.WA_Left,
			IntuitionWindowTags.WA_Top, IntuitionWindowTags.WA_Width,
			IntuitionWindowTags.WA_Height, IntuitionWindowTags.WA_DetailPen,
			IntuitionWindowTags.WA_BlockPen, IntuitionWindowTags.WA_IDCMP,
			IntuitionWindowTags.WA_Flags, IntuitionWindowTags.WA_Gadgets,
			IntuitionWindowTags.WA_Checkmark, IntuitionWindowTags.WA_Title,
			IntuitionWindowTags.WA_ScreenTitle,
			IntuitionWindowTags.WA_CustomScreen,
			IntuitionWindowTags.WA_SuperBitMap, IntuitionWindowTags.WA_MinWidth,
			IntuitionWindowTags.WA_MinHeight, IntuitionWindowTags.WA_MaxWidth,
			IntuitionWindowTags.WA_MaxHeight, IntuitionWindowTags.WA_InnerWidth,
			IntuitionWindowTags.WA_InnerHeight,
			IntuitionWindowTags.WA_PubScreenName,
			IntuitionWindowTags.WA_PubScreen,
			IntuitionWindowTags.WA_PubScreenFallBack,
			IntuitionWindowTags.WA_WindowName, IntuitionWindowTags.WA_Colors,
			IntuitionWindowTags.WA_Zoom, IntuitionWindowTags.WA_MouseQueue,
			IntuitionWindowTags.WA_BackFill, IntuitionWindowTags.WA_RptQueue,
			IntuitionWindowTags.WA_SizeGadget, IntuitionWindowTags.WA_DragBar,
			IntuitionWindowTags.WA_DepthGadget,
			IntuitionWindowTags.WA_CloseGadget, IntuitionWindowTags.WA_Backdrop,
			IntuitionWindowTags.WA_ReportMouse,
			IntuitionWindowTags.WA_NoCareRefresh,
			IntuitionWindowTags.WA_Borderless, IntuitionWindowTags.WA_Activate,
			IntuitionWindowTags.WA_RMBTrap,
			IntuitionWindowTags.WA_WBenchWindow,
			IntuitionWindowTags.WA_SimpleRefresh,
			IntuitionWindowTags.WA_SmartRefresh,
			IntuitionWindowTags.WA_SizeBRight,
			IntuitionWindowTags.WA_SizeBBottom,
			IntuitionWindowTags.WA_AutoAdjust,
			IntuitionWindowTags.WA_GimmeZeroZero,
			IntuitionWindowTags.WA_MenuHelp,
			IntuitionWindowTags.WA_NewLookMenus,
			IntuitionWindowTags.WA_AmigaKey,
			IntuitionWindowTags.WA_NotifyDepth,
		});
		AssertSequence(0x8000_0097u, new uint[]
		{
			IntuitionWindowTags.WA_Pointer,
			IntuitionWindowTags.WA_BusyPointer,
			IntuitionWindowTags.WA_PointerDelay,
			IntuitionWindowTags.WA_TabletMessages,
			IntuitionWindowTags.WA_HelpGroup,
			IntuitionWindowTags.WA_HelpGroupWindow,
		});
		Assert.Equal(IntuitionWindowTags.WA_Dummy + 0x33u,
			IntuitionWindowTags.WA_Pointer - 1u);
		Assert.Equal(1u, IntuitionWindowTags.HC_GADGETHELP);
	}

	[Fact]
	public void StringHookConstantsAndLayoutsMatchNdk31SgHooksHeader()
	{
		AssertSequence(1u, new uint[]
		{
			IntuitionStringGadgetHooks.EO_NOOP,
			IntuitionStringGadgetHooks.EO_DELBACKWARD,
			IntuitionStringGadgetHooks.EO_DELFORWARD,
			IntuitionStringGadgetHooks.EO_MOVECURSOR,
			IntuitionStringGadgetHooks.EO_ENTER,
			IntuitionStringGadgetHooks.EO_RESET,
			IntuitionStringGadgetHooks.EO_REPLACECHAR,
			IntuitionStringGadgetHooks.EO_INSERTCHAR,
			IntuitionStringGadgetHooks.EO_BADFORMAT,
			IntuitionStringGadgetHooks.EO_BIGCHANGE,
			IntuitionStringGadgetHooks.EO_UNDO,
			IntuitionStringGadgetHooks.EO_CLEAR,
			IntuitionStringGadgetHooks.EO_SPECIAL,
		});
		Assert.Equal(new uint[] { 1, 2, 4, 8, 16, 32, 64, 128 }, new uint[]
		{
			IntuitionStringGadgetHooks.SGM_REPLACE,
			IntuitionStringGadgetHooks.SGM_FIXEDFIELD,
			IntuitionStringGadgetHooks.SGM_NOFILTER,
			IntuitionStringGadgetHooks.SGM_NOCHANGE,
			IntuitionStringGadgetHooks.SGM_NOWORKB,
			IntuitionStringGadgetHooks.SGM_CONTROL,
			IntuitionStringGadgetHooks.SGM_LONGINT,
			IntuitionStringGadgetHooks.SGM_EXITHELP,
		});
		Assert.Equal(new uint[] { 1, 2, 4, 8, 16, 32, 64 }, new uint[]
		{
			IntuitionStringGadgetHooks.SGA_USE,
			IntuitionStringGadgetHooks.SGA_END,
			IntuitionStringGadgetHooks.SGA_BEEP,
			IntuitionStringGadgetHooks.SGA_REUSE,
			IntuitionStringGadgetHooks.SGA_REDISPLAY,
			IntuitionStringGadgetHooks.SGA_NEXTACTIVE,
			IntuitionStringGadgetHooks.SGA_PREVACTIVE,
		});
		Assert.Equal(1u, IntuitionStringGadgetHooks.SGH_KEY);
		Assert.Equal(2u, IntuitionStringGadgetHooks.SGH_CLICK);
		AssertLayout<StringExtend>(36, (nameof(StringExtend.Font), 0),
			(nameof(StringExtend.Pens), 4), (nameof(StringExtend.ActivePens), 6),
			(nameof(StringExtend.InitialModes), 8),
			(nameof(StringExtend.EditHook), 12),
			(nameof(StringExtend.WorkBuffer), 16),
			(nameof(StringExtend.Reserved), 20));
		AssertLayout<SGWork>(44, (nameof(SGWork.Gadget), 0),
			(nameof(SGWork.StringInfo), 4), (nameof(SGWork.WorkBuffer), 8),
			(nameof(SGWork.PrevBuffer), 12), (nameof(SGWork.Modes), 16),
			(nameof(SGWork.IEvent), 20), (nameof(SGWork.Code), 24),
			(nameof(SGWork.BufferPos), 26), (nameof(SGWork.NumChars), 28),
			(nameof(SGWork.Actions), 30), (nameof(SGWork.LongInt), 34),
			(nameof(SGWork.GadgetInfo), 38), (nameof(SGWork.EditOp), 42));
	}

	[Fact]
	public unsafe void StringHookCodecsRoundTripEveryField()
	{
		var memory = new TestMemory(0x3000u, 256);
		var address = P(0x3020u);
		var extend = new StringExtend { Font = P(1), InitialModes = 2,
			EditHook = P(3), WorkBuffer = P(4) };
		extend.Pens[0] = 5;
		extend.Pens[1] = 6;
		extend.ActivePens[0] = 7;
		extend.ActivePens[1] = 8;
		for (var index = 0; index < 4; index++)
			extend.Reserved[index] = 9u + (uint)index;
		IntuitionStringGadgetGuestCodec.WriteExtend(ref memory, address, extend);
		var actualExtend = IntuitionStringGadgetGuestCodec.ReadExtend(ref memory, address);
		Assert.Equal(extend.Font, actualExtend.Font);
		Assert.Equal(extend.InitialModes, actualExtend.InitialModes);
		Assert.Equal(extend.EditHook, actualExtend.EditHook);
		Assert.Equal(extend.WorkBuffer, actualExtend.WorkBuffer);
		for (var index = 0; index < 2; index++)
		{
			Assert.Equal(extend.Pens[index], actualExtend.Pens[index]);
			Assert.Equal(extend.ActivePens[index], actualExtend.ActivePens[index]);
		}
		for (var index = 0; index < 4; index++)
			Assert.Equal(extend.Reserved[index], actualExtend.Reserved[index]);
		var work = new SGWork { Gadget = P(13), StringInfo = P(14),
			WorkBuffer = P(15), PrevBuffer = P(16), Modes = 17,
			IEvent = P(18), Code = 19, BufferPos = -20, NumChars = 21,
			Actions = 22, LongInt = -23, GadgetInfo = P(24), EditOp = 25 };
		IntuitionStringGadgetGuestCodec.WriteWork(ref memory, address, work);
		Assert.Equal(work,
			IntuitionStringGadgetGuestCodec.ReadWork(ref memory, address));
		Assert.Equal("CopperSharp.Sdk.Amiga.Support",
			typeof(IntuitionStringGadgetGuestCodec).Assembly.GetName().Name);
	}

	[Fact]
	public void PreferencesLayoutAndCodecCoverEveryNdk31Byte()
	{
		Assert.Equal(30, IntuitionPreferencesConstants.FILENAME_SIZE);
		Assert.Equal(16, IntuitionPreferencesConstants.DEVNAME_SIZE);
		Assert.Equal(36, IntuitionPreferencesConstants.POINTERSIZE);
		AssertLayout<Preferences>(234,
			(nameof(Preferences.FontHeight), 0), (nameof(Preferences.PrinterPort), 1),
			(nameof(Preferences.BaudRate), 2), (nameof(Preferences.KeyRptSpeed), 4),
			(nameof(Preferences.KeyRptDelay), 12), (nameof(Preferences.DoubleClick), 20),
			(nameof(Preferences.PointerMatrix), 28), (nameof(Preferences.XOffset), 100),
			(nameof(Preferences.YOffset), 101), (nameof(Preferences.color17), 102),
			(nameof(Preferences.color18), 104), (nameof(Preferences.color19), 106),
			(nameof(Preferences.PointerTicks), 108), (nameof(Preferences.color0), 110),
			(nameof(Preferences.color1), 112), (nameof(Preferences.color2), 114),
			(nameof(Preferences.color3), 116), (nameof(Preferences.ViewXOffset), 118),
			(nameof(Preferences.ViewYOffset), 119), (nameof(Preferences.ViewInitX), 120),
			(nameof(Preferences.ViewInitY), 122), (nameof(Preferences.EnableCLI), 124),
			(nameof(Preferences.PrinterType), 128),
			(nameof(Preferences.PrinterFilename), 130),
			(nameof(Preferences.PrintPitch), 160),
			(nameof(Preferences.PrintQuality), 162),
			(nameof(Preferences.PrintSpacing), 164),
			(nameof(Preferences.PrintLeftMargin), 166),
			(nameof(Preferences.PrintRightMargin), 168),
			(nameof(Preferences.PrintImage), 170),
			(nameof(Preferences.PrintAspect), 172),
			(nameof(Preferences.PrintShade), 174),
			(nameof(Preferences.PrintThreshold), 176),
			(nameof(Preferences.PaperSize), 178),
			(nameof(Preferences.PaperLength), 180),
			(nameof(Preferences.PaperType), 182),
			(nameof(Preferences.SerRWBits), 184),
			(nameof(Preferences.SerStopBuf), 185),
			(nameof(Preferences.SerParShk), 186), (nameof(Preferences.LaceWB), 187),
			(nameof(Preferences.Pad), 188), (nameof(Preferences.PrtDevName), 200),
			(nameof(Preferences.DefaultPrtUnit), 216),
			(nameof(Preferences.DefaultSerUnit), 217),
			(nameof(Preferences.RowSizeChange), 218),
			(nameof(Preferences.ColumnSizeChange), 219),
			(nameof(Preferences.PrintFlags), 220),
			(nameof(Preferences.PrintMaxWidth), 222),
			(nameof(Preferences.PrintMaxHeight), 224),
			(nameof(Preferences.PrintDensity), 226),
			(nameof(Preferences.PrintXOffset), 227),
			(nameof(Preferences.wb_Width), 228), (nameof(Preferences.wb_Height), 230),
			(nameof(Preferences.wb_Depth), 232), (nameof(Preferences.ext_size), 233));
		var memory = new TestMemory(0x4000u, 1024);
		var source = P(0x4020u);
		var destination = P(0x4120u);
		for (var offset = 0; offset < (int)Preferences.Size; offset++)
			memory.WriteUInt8(source, offset, unchecked((byte)(offset * 73 + 19)));
		var value = IntuitionPreferencesGuestCodec.Read(ref memory, source);
		IntuitionPreferencesGuestCodec.Write(ref memory, destination, value);
		for (var offset = 0; offset < (int)Preferences.Size; offset++)
			Assert.Equal(memory.ReadUInt8(source, offset),
				memory.ReadUInt8(destination, offset));
		Assert.Equal("CopperSharp.Sdk.Amiga.Support",
			typeof(IntuitionPreferencesGuestCodec).Assembly.GetName().Name);
	}

	[Fact]
	public void PreferenceValuesMatchNdk31PreferencesHeader()
	{
		Assert.Equal(1, IntuitionPreferencesConstants.LACEWB);
		Assert.Equal(1, IntuitionPreferencesConstants.LW_RESERVED);
		Assert.Equal(0x4000, IntuitionPreferencesConstants.SCREEN_DRAG);
		Assert.Equal(0x8000, IntuitionPreferencesConstants.MOUSE_ACCEL);
		Assert.Equal(new byte[] { 0, 1 }, new byte[]
		{
			IntuitionPreferencesConstants.PARALLEL_PRINTER,
			IntuitionPreferencesConstants.SERIAL_PRINTER,
		});
		AssertSequence(0u, new uint[]
		{
			IntuitionPreferencesConstants.BAUD_110,
			IntuitionPreferencesConstants.BAUD_300,
			IntuitionPreferencesConstants.BAUD_1200,
			IntuitionPreferencesConstants.BAUD_2400,
			IntuitionPreferencesConstants.BAUD_4800,
			IntuitionPreferencesConstants.BAUD_9600,
			IntuitionPreferencesConstants.BAUD_19200,
			IntuitionPreferencesConstants.BAUD_MIDI,
		});
		Assert.Equal(new ushort[] { 0, 0x80 }, new ushort[]
		{
			IntuitionPreferencesConstants.FANFOLD,
			IntuitionPreferencesConstants.SINGLE,
		});
		Assert.Equal(new ushort[] { 0, 0x400, 0x800, 0, 0x100, 0, 0x200 },
			new ushort[]
			{
				IntuitionPreferencesConstants.PICA,
				IntuitionPreferencesConstants.ELITE,
				IntuitionPreferencesConstants.FINE,
				IntuitionPreferencesConstants.DRAFT,
				IntuitionPreferencesConstants.LETTER,
				IntuitionPreferencesConstants.SIX_LPI,
				IntuitionPreferencesConstants.EIGHT_LPI,
			});
		Assert.Equal(new ushort[] { 0, 1, 0, 1, 0, 1, 2 }, new ushort[]
		{
			IntuitionPreferencesConstants.IMAGE_POSITIVE,
			IntuitionPreferencesConstants.IMAGE_NEGATIVE,
			IntuitionPreferencesConstants.ASPECT_HORIZ,
			IntuitionPreferencesConstants.ASPECT_VERT,
			IntuitionPreferencesConstants.SHADE_BW,
			IntuitionPreferencesConstants.SHADE_GREYSCALE,
			IntuitionPreferencesConstants.SHADE_COLOR,
		});
		Assert.Equal(new ushort[]
		{
			0, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70,
			0x80, 0x90, 0xA0, 0xB0, 0xC0, 0xD0,
		}, new ushort[]
		{
			IntuitionPreferencesConstants.US_LETTER,
			IntuitionPreferencesConstants.US_LEGAL,
			IntuitionPreferencesConstants.N_TRACTOR,
			IntuitionPreferencesConstants.W_TRACTOR,
			IntuitionPreferencesConstants.CUSTOM,
			IntuitionPreferencesConstants.EURO_A0,
			IntuitionPreferencesConstants.EURO_A1,
			IntuitionPreferencesConstants.EURO_A2,
			IntuitionPreferencesConstants.EURO_A3,
			IntuitionPreferencesConstants.EURO_A4,
			IntuitionPreferencesConstants.EURO_A5,
			IntuitionPreferencesConstants.EURO_A6,
			IntuitionPreferencesConstants.EURO_A7,
			IntuitionPreferencesConstants.EURO_A8,
		});
		AssertSequence(0u, new uint[]
		{
			IntuitionPreferencesConstants.CUSTOM_NAME,
			IntuitionPreferencesConstants.ALPHA_P_101,
			IntuitionPreferencesConstants.BROTHER_15XL,
			IntuitionPreferencesConstants.CBM_MPS1000,
			IntuitionPreferencesConstants.DIAB_630,
			IntuitionPreferencesConstants.DIAB_ADV_D25,
			IntuitionPreferencesConstants.DIAB_C_150,
			IntuitionPreferencesConstants.EPSON,
			IntuitionPreferencesConstants.EPSON_JX_80,
			IntuitionPreferencesConstants.OKIMATE_20,
			IntuitionPreferencesConstants.QUME_LP_20,
			IntuitionPreferencesConstants.HP_LASERJET,
			IntuitionPreferencesConstants.HP_LASERJET_PLUS,
		});
		AssertSequence(0u, new uint[]
		{
			IntuitionPreferencesConstants.SBUF_512,
			IntuitionPreferencesConstants.SBUF_1024,
			IntuitionPreferencesConstants.SBUF_2048,
			IntuitionPreferencesConstants.SBUF_4096,
			IntuitionPreferencesConstants.SBUF_8000,
			IntuitionPreferencesConstants.SBUF_16000,
		});
		Assert.Equal(new byte[] { 0xF0, 0x0F, 0xF0, 0x0F, 0xF0, 0x0F },
			new byte[]
			{
				IntuitionPreferencesConstants.SREAD_BITS,
				IntuitionPreferencesConstants.SWRITE_BITS,
				IntuitionPreferencesConstants.SSTOP_BITS,
				IntuitionPreferencesConstants.SBUFSIZE_BITS,
				IntuitionPreferencesConstants.SPARITY_BITS,
				IntuitionPreferencesConstants.SHSHAKE_BITS,
			});
		AssertSequence(0u, new uint[]
		{
			IntuitionPreferencesConstants.SPARITY_NONE,
			IntuitionPreferencesConstants.SPARITY_EVEN,
			IntuitionPreferencesConstants.SPARITY_ODD,
			IntuitionPreferencesConstants.SPARITY_MARK,
			IntuitionPreferencesConstants.SPARITY_SPACE,
		});
		AssertSequence(0u, new uint[]
		{
			IntuitionPreferencesConstants.SHSHAKE_XON,
			IntuitionPreferencesConstants.SHSHAKE_RTS,
			IntuitionPreferencesConstants.SHSHAKE_NONE,
		});
		Assert.Equal(new ushort[]
		{
			1, 2, 4, 8, 0, 0x10, 0x20, 0x40, 0x80, 0x100,
			0, 0x200, 0x400, 0x800, 0x1000,
		}, new ushort[]
		{
			IntuitionPreferencesConstants.CORRECT_RED,
			IntuitionPreferencesConstants.CORRECT_GREEN,
			IntuitionPreferencesConstants.CORRECT_BLUE,
			IntuitionPreferencesConstants.CENTER_IMAGE,
			IntuitionPreferencesConstants.IGNORE_DIMENSIONS,
			IntuitionPreferencesConstants.BOUNDED_DIMENSIONS,
			IntuitionPreferencesConstants.ABSOLUTE_DIMENSIONS,
			IntuitionPreferencesConstants.PIXEL_DIMENSIONS,
			IntuitionPreferencesConstants.MULTIPLY_DIMENSIONS,
			IntuitionPreferencesConstants.INTEGER_SCALING,
			IntuitionPreferencesConstants.ORDERED_DITHERING,
			IntuitionPreferencesConstants.HALFTONE_DITHERING,
			IntuitionPreferencesConstants.FLOYD_DITHERING,
			IntuitionPreferencesConstants.ANTI_ALIAS,
			IntuitionPreferencesConstants.GREY_SCALE2,
		});
		Assert.Equal(0x0007, IntuitionPreferencesConstants.CORRECT_RGB_MASK);
		Assert.Equal(0x00F0, IntuitionPreferencesConstants.DIMENSIONS_MASK);
		Assert.Equal(0x0600, IntuitionPreferencesConstants.DITHERING_MASK);
	}

	[Fact]
	public void IntuitionBasePublicPrefixMatchesNdk31HeaderByteForByte()
	{
		Assert.Equal(new ushort[] { 2, 0, 1, 10, 2, 0, 1, 8 }, new ushort[]
		{
			IntuitionBaseConstants.DMODECOUNT, IntuitionBaseConstants.HIRESPICK,
			IntuitionBaseConstants.LOWRESPICK, IntuitionBaseConstants.EVENTMAX,
			IntuitionBaseConstants.RESCOUNT, IntuitionBaseConstants.HIRESGADGET,
			IntuitionBaseConstants.LOWRESGADGET,
			IntuitionBaseConstants.GADGETCOUNT,
		});
		AssertSequence(0u, new uint[]
		{
			IntuitionBaseConstants.UPFRONTGADGET,
			IntuitionBaseConstants.DOWNBACKGADGET,
			IntuitionBaseConstants.SIZEGADGET,
			IntuitionBaseConstants.CLOSEGADGET,
			IntuitionBaseConstants.DRAGGADGET,
			IntuitionBaseConstants.SUPFRONTGADGET,
			IntuitionBaseConstants.SDOWNBACKGADGET,
			IntuitionBaseConstants.SDRAGGADGET,
		});
		AssertLayout<IntuitionBase>(80, (nameof(IntuitionBase.LibNode), 0),
			(nameof(IntuitionBase.ViewLord), 34),
			(nameof(IntuitionBase.ActiveWindow), 52),
			(nameof(IntuitionBase.ActiveScreen), 56),
			(nameof(IntuitionBase.FirstScreen), 60),
			(nameof(IntuitionBase.Flags), 64), (nameof(IntuitionBase.MouseY), 68),
			(nameof(IntuitionBase.MouseX), 70), (nameof(IntuitionBase.Seconds), 72),
			(nameof(IntuitionBase.Micros), 76));
		var memory = new TestMemory(0x5000u, 512);
		var source = P(0x5020u);
		var destination = P(0x50A0u);
		for (var offset = 0; offset < (int)IntuitionBase.Size; offset++)
			memory.WriteUInt8(source, offset, unchecked((byte)(offset * 29 + 7)));
		var value = IntuitionBaseGuestCodec.Read(ref memory, source);
		IntuitionBaseGuestCodec.Write(ref memory, destination, value);
		for (var offset = 0; offset < (int)IntuitionBase.Size; offset++)
			Assert.Equal(memory.ReadUInt8(source, offset),
				memory.ReadUInt8(destination, offset));
		Assert.Equal("CopperSharp.Sdk.Amiga.Support",
			typeof(IntuitionBaseGuestCodec).Assembly.GetName().Name);
	}

	private static void AssertSequence(uint first, IReadOnlyList<uint> values)
	{
		for (var index = 0; index < values.Count; index++)
			Assert.Equal(first + (uint)index, values[index]);
	}

	private static void AssertLayout<T>(int size,
		params (string Name, int Offset)[] fields) where T : struct
	{
		Assert.Equal(size, Marshal.SizeOf<T>());
		foreach (var (name, offset) in fields)
			Assert.Equal(offset, Marshal.OffsetOf<T>(name).ToInt32());
	}

	private static APTR P(uint value) => APTR.FromPointer(value);

	private struct TestMemory : IAmigaGuestMemory
	{
		private readonly uint _baseAddress;
		private readonly byte[] _bytes;

		public TestMemory(uint baseAddress, int size)
		{
			_baseAddress = baseAddress;
			_bytes = new byte[size];
		}

		public byte ReadUInt8(APTR address, int offset = 0) =>
			_bytes[Index(address, offset, 1)];
		public ushort ReadUInt16(APTR address, int offset = 0) =>
			BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(Index(address, offset, 2), 2));
		public uint ReadUInt32(APTR address, int offset = 0) =>
			BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(Index(address, offset, 4), 4));
		public void WriteUInt8(APTR address, int offset, byte value) =>
			_bytes[Index(address, offset, 1)] = value;
		public void WriteUInt16(APTR address, int offset, ushort value) =>
			BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan(Index(address, offset, 2), 2), value);
		public void WriteUInt32(APTR address, int offset, uint value) =>
			BinaryPrimitives.WriteUInt32BigEndian(_bytes.AsSpan(Index(address, offset, 4), 4), value);
		public void Clear(APTR address, uint byteCount) =>
			_bytes.AsSpan(Index(address, 0, checked((int)byteCount)), checked((int)byteCount)).Clear();
		public void Copy(APTR source, APTR destination, uint byteCount) =>
			_bytes.AsSpan(Index(source, 0, checked((int)byteCount)), checked((int)byteCount))
				.CopyTo(_bytes.AsSpan(Index(destination, 0, checked((int)byteCount)), checked((int)byteCount)));
		public bool IsMapped(APTR address, uint byteSize) =>
			address.Raw >= _baseAddress && address.Raw - _baseAddress <= (uint)_bytes.Length &&
			byteSize <= (uint)_bytes.Length - (address.Raw - _baseAddress);

		private int Index(APTR address, int offset, int size)
		{
			var raw = checked(address.Raw + (uint)offset);
			if (raw < _baseAddress || raw - _baseAddress > (uint)_bytes.Length ||
				(uint)size > (uint)_bytes.Length - (raw - _baseAddress))
				throw new ArgumentOutOfRangeException(nameof(address));
			return checked((int)(raw - _baseAddress));
		}
	}
}
