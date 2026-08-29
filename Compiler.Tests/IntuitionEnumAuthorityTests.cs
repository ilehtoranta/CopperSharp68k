using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class IntuitionEnumAuthorityTests
{
	[Fact]
	public void IdcmpAndWindowValuesAreExhaustiveC31Authority()
	{
		AssertValues(new ulong[]
		{
			0, 0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80, 0x100,
			0x200, 0x400, 0x800, 0x1000, 0x2000, 0x4000, 0x8000,
			0x10000, 0x20000, 0x40000, 0x80000, 0x100000, 0x200000,
			0x400000, 0x800000, 0x1000000, 0x2000000, 0x4000000,
			0x80000000,
		}, new[]
		{
			IDCMPFlags.None, IDCMPFlags.SizeVerify, IDCMPFlags.NewSize,
			IDCMPFlags.RefreshWindow, IDCMPFlags.MouseButtons, IDCMPFlags.MouseMove,
			IDCMPFlags.GadgetDown, IDCMPFlags.GadgetUp, IDCMPFlags.RequesterSet,
			IDCMPFlags.MenuPick, IDCMPFlags.CloseWindow, IDCMPFlags.RawKey,
			IDCMPFlags.RequesterVerify, IDCMPFlags.RequesterClear,
			IDCMPFlags.MenuVerify, IDCMPFlags.NewPrefs, IDCMPFlags.DiskInserted,
			IDCMPFlags.DiskRemoved, IDCMPFlags.WorkbenchMessage,
			IDCMPFlags.ActiveWindow, IDCMPFlags.InactiveWindow, IDCMPFlags.DeltaMove,
			IDCMPFlags.VanillaKey, IDCMPFlags.IntuiTicks, IDCMPFlags.IDCMPUpdate,
			IDCMPFlags.MenuHelp, IDCMPFlags.ChangeWindow, IDCMPFlags.GadgetHelp,
			IDCMPFlags.LonelyMessage,
		});
		AssertValues(new ulong[] { 0, 1, 1, 2, 3, 4, 1, 2 }, new[]
		{
			IDCMPCode.WindowMoveSize, IDCMPCode.WindowDepth, IDCMPCode.MenuHot,
			IDCMPCode.MenuCancel, IDCMPCode.MenuWaiting,
			IDCMPCode.VerificationAbort, IDCMPCode.WorkbenchOpen,
			IDCMPCode.WorkbenchClose,
		});
		AssertValues(new ulong[]
		{
			0, 1, 2, 4, 8, 0x10, 0x20, 0xC0, 0, 0x40, 0x80, 0xC0,
			0x100, 0x200, 0x400, 0x800, 0x1000, 0x2000, 0x4000, 0x8000,
			0x10000, 0x20000, 0x40000, 0x200000, 0x1000000, 0x2000000,
			0x4000000, 0x8000000, 0x10000000, 0x20000000,
		}, new[]
		{
			WindowFlags.None, WindowFlags.SizeGadget, WindowFlags.DragBar,
			WindowFlags.DepthGadget, WindowFlags.CloseGadget,
			WindowFlags.SizeBright, WindowFlags.SizeBottom, WindowFlags.RefreshMask,
			WindowFlags.SmartRefresh, WindowFlags.SimpleRefresh,
			WindowFlags.SuperBitmap, WindowFlags.OtherRefresh, WindowFlags.Backdrop,
			WindowFlags.ReportMouse, WindowFlags.GimmeZeroZero,
			WindowFlags.Borderless, WindowFlags.Activate, WindowFlags.WindowActive,
			WindowFlags.InRequest, WindowFlags.MenuState, WindowFlags.RmbTrap,
			WindowFlags.NoCareRefresh, WindowFlags.NewWindowExtended,
			WindowFlags.NewLookMenus, WindowFlags.WindowRefresh,
			WindowFlags.WorkbenchWindow, WindowFlags.WindowTicked,
			WindowFlags.Visitor, WindowFlags.Zoomed, WindowFlags.HasZoom,
		});
	}

	[Fact]
	public void GadgetValuesAreExhaustiveC31Authority()
	{
		AssertValues(new ulong[]
		{
			0, 3, 0, 1, 2, 3, 4, 8, 0x10, 0x20, 0x40, 0x80, 0x100,
			0x200, 0x400, 0x800, 0x3000, 0, 0x1000, 0x2000, 0x4000, 0x8000,
		}, new[]
		{
			GadgetFlags.None, GadgetFlags.HighlightMask,
			GadgetFlags.HighlightComplement, GadgetFlags.HighlightBox,
			GadgetFlags.HighlightImage, GadgetFlags.HighlightNone,
			GadgetFlags.GadgetImage, GadgetFlags.RelativeBottom,
			GadgetFlags.RelativeRight, GadgetFlags.RelativeWidth,
			GadgetFlags.RelativeHeight, GadgetFlags.Selected, GadgetFlags.Disabled,
			GadgetFlags.TabCycle, GadgetFlags.StringExtend,
			GadgetFlags.ImageDisable, GadgetFlags.LabelMask,
			GadgetFlags.LabelIntuiText, GadgetFlags.LabelString,
			GadgetFlags.LabelImage, GadgetFlags.RelativeSpecial,
			GadgetFlags.Extended,
		});
		AssertValues(new ulong[]
		{
			0, 1, 2, 4, 8, 0x10, 0x20, 0x40, 0x80, 0x100, 0x200,
			0x400, 0x800, 0x1000, 0x2000, 0x2000, 0x4000, 0x8000,
		}, new[]
		{
			GadgetActivationFlags.None, GadgetActivationFlags.RelativeVerify,
			GadgetActivationFlags.Immediate, GadgetActivationFlags.EndGadget,
			GadgetActivationFlags.FollowMouse, GadgetActivationFlags.RightBorder,
			GadgetActivationFlags.LeftBorder, GadgetActivationFlags.TopBorder,
			GadgetActivationFlags.BottomBorder,
			GadgetActivationFlags.ToggleSelect, GadgetActivationFlags.StringCenter,
			GadgetActivationFlags.StringRight, GadgetActivationFlags.LongInteger,
			GadgetActivationFlags.AlternateKeyMap,
			GadgetActivationFlags.StringExtend,
			GadgetActivationFlags.BooleanExtend,
			GadgetActivationFlags.ActiveGadget,
			GadgetActivationFlags.BorderSniff,
		});
		AssertValues(new ulong[]
		{
			0, 0xFC00, 0x4000, 0x2000, 0x1000, 0x8000, 0xF0, 0x10,
			0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x40, 0x50,
			0x60, 0x70, 7, 1, 2, 3, 4, 5,
		}, new[]
		{
			GadgetType.None, GadgetType.GadgetTypeMask, GadgetType.ScreenGadget,
			GadgetType.GzzGadget, GadgetType.RequesterGadget,
			GadgetType.SystemGadget, GadgetType.SystemTypeMask, GadgetType.Sizing,
			GadgetType.WindowDragging, GadgetType.ScreenDragging,
			GadgetType.WindowDepth, GadgetType.ScreenDepth, GadgetType.WindowZoom,
			GadgetType.ScreenUnused, GadgetType.Close, GadgetType.WindowToFront,
			GadgetType.ScreenToFront, GadgetType.WindowToBack,
			GadgetType.ScreenToBack, GadgetType.GadgetClassMask,
			GadgetType.BooleanGadget, GadgetType.Gadget0002,
			GadgetType.ProportionalGadget, GadgetType.StringGadget,
			GadgetType.CustomGadget,
		});
		AssertValues(new ulong[] { 0, 1, 2, 4 }, new[]
		{
			GadgetMoreFlags.None, GadgetMoreFlags.Bounds,
			GadgetMoreFlags.GadgetHelp, GadgetMoreFlags.ScrollRaster,
		});
		AssertValues(new ulong[] { 0, 1 }, new[]
		{
			BoolInfoFlags.None, BoolInfoFlags.Mask,
		});
		AssertValues(new ulong[] { 0, 1, 2, 4, 8, 0x10, 0x100 }, new[]
		{
			PropInfoFlags.None, PropInfoFlags.AutoKnob, PropInfoFlags.FreeHorizontal,
			PropInfoFlags.FreeVertical, PropInfoFlags.Borderless,
			PropInfoFlags.NewLook, PropInfoFlags.KnobHit,
		});
	}

	[Fact]
	public void MenuAndRequesterValuesAreExhaustiveC31Authority()
	{
		AssertValues(new ulong[] { 0, 1, 0x100 }, new[]
		{
			MenuFlags.None, MenuFlags.Enabled, MenuFlags.Drawn,
		});
		AssertValues(new ulong[]
		{
			0, 1, 2, 4, 8, 0x10, 0xC0, 0, 0x40, 0x80, 0xC0,
			0x100, 0x1000, 0x2000, 0x4000,
		}, new[]
		{
			MenuItemFlags.None, MenuItemFlags.CheckIt, MenuItemFlags.ItemText,
			MenuItemFlags.CommandSequence, MenuItemFlags.MenuToggle,
			MenuItemFlags.Enabled, MenuItemFlags.HighlightMask,
			MenuItemFlags.HighlightImage, MenuItemFlags.HighlightComplement,
			MenuItemFlags.HighlightBox, MenuItemFlags.HighlightNone,
			MenuItemFlags.Checked, MenuItemFlags.Drawn, MenuItemFlags.Highlighted,
			MenuItemFlags.MenuToggled,
		});
		AssertValues(new ulong[]
		{
			0, 1, 2, 4, 0x10, 0x20, 0x40, 0x1000, 0x2000, 0x4000, 0x8000,
		}, new[]
		{
			RequesterFlags.None, RequesterFlags.PointerRelative,
			RequesterFlags.Predrawn, RequesterFlags.Noisy,
			RequesterFlags.SimpleRefresh, RequesterFlags.UseRequesterImage,
			RequesterFlags.NoBackFill, RequesterFlags.OffWindow,
			RequesterFlags.Active, RequesterFlags.System,
			RequesterFlags.DeferRefresh,
		});
	}

	[Fact]
	public void ScreenValuesAreExhaustiveC31Authority()
	{
		AssertValues(new ulong[]
		{
			0, 0xF, 1, 2, 0xF, 0x10, 0x20, 0x40, 0x80, 0x100,
			0x200, 0x400, 0x1000, 0x4000,
		}, new[]
		{
			ScreenFlags.None, ScreenFlags.ScreenTypeMask, ScreenFlags.Workbench,
			ScreenFlags.Public, ScreenFlags.Custom, ScreenFlags.ShowTitle,
			ScreenFlags.Beeping, ScreenFlags.CustomBitmap, ScreenFlags.Behind,
			ScreenFlags.Quiet, ScreenFlags.HighResolution, ScreenFlags.PensShared,
			ScreenFlags.Extended, ScreenFlags.AutoScroll,
		});
		AssertValues(new ulong[] { 1, 2, 0xF }, new[]
		{
			ScreenType.Workbench, ScreenType.Public, ScreenType.Custom,
		});
		AssertValues(new ulong[]
		{
			0, 2, 4, 8, 0x20, 0x40, 0x80, 0x100, 0x400, 0x800,
			0x1000, 0x2000, 0x4000, 0x8000,
		}, new[]
		{
			ScreenViewModes.None, ScreenViewModes.GenlockVideo,
			ScreenViewModes.Interlace, ScreenViewModes.DoubleScan,
			ScreenViewModes.SuperHighResolution, ScreenViewModes.PlayfieldB,
			ScreenViewModes.ExtraHalfBrite, ScreenViewModes.GenlockAudio,
			ScreenViewModes.DualPlayfield, ScreenViewModes.HoldAndModify,
			ScreenViewModes.ExtendedMode, ScreenViewModes.ViewportHidden,
			ScreenViewModes.Sprites, ScreenViewModes.HighResolution,
		});
		AssertValues(new ulong[] { 1, 2, 3, 4 }, new[]
		{
			OverscanType.Text, OverscanType.Standard, OverscanType.Maximum,
			OverscanType.Video,
		});
		AssertValues(new ulong[] { 0, 1, 2 }, new[]
		{
			PublicScreenModes.None, PublicScreenModes.Shanghai,
			PublicScreenModes.PopPublicScreen,
		});
		AssertValues(new ulong[] { 0, 1 }, new[]
		{
			PublicScreenFlags.None, PublicScreenFlags.Private,
		});
		AssertValues(new ulong[] { 0, 1, 2 }, new[]
		{
			ScreenDepthMode.ToFront, ScreenDepthMode.ToBack,
			ScreenDepthMode.InFamily,
		});
		AssertValues(new ulong[] { 0, 1, 2, 4 }, new[]
		{
			ScreenPositionMode.Relative, ScreenPositionMode.Absolute,
			ScreenPositionMode.MakeVisible, ScreenPositionMode.ForcedDrag,
		});
	}

	[Fact]
	public void DrawInfoAndDrawingValuesAreExhaustiveC31Authority()
	{
		AssertValues(new ulong[] { 0, 1 }, new[]
		{
			DrawInfoFlags.None, DrawInfoFlags.NewLook,
		});
		AssertValues(new ulong[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 },
			new[]
			{
				DrawInfoPen.Detail, DrawInfoPen.Block, DrawInfoPen.Text,
				DrawInfoPen.Shine, DrawInfoPen.Shadow, DrawInfoPen.Fill,
				DrawInfoPen.FillText, DrawInfoPen.Background,
				DrawInfoPen.HighlightText, DrawInfoPen.BarDetail,
				DrawInfoPen.BarBlock, DrawInfoPen.BarTrim,
				DrawInfoPen.NumberOfPens,
			});
		AssertValues(new ulong[] { 0, 1, 2, 4 }, new[]
		{
			DrawMode.Jam1, DrawMode.Jam2, DrawMode.Complement,
			DrawMode.InverseVideo,
		});
	}

	[Fact]
	public void ClassicSentinelsAndLimitsMatchC31Authority()
	{
		Assert.Equal((ushort)0x001F, IntuitionConstants.NoMenu);
		Assert.Equal((ushort)0x003F, IntuitionConstants.NoItem);
		Assert.Equal((ushort)0x001F, IntuitionConstants.NoSub);
		Assert.Equal(ushort.MaxValue, IntuitionConstants.MenuNull);
		Assert.Equal(ushort.MaxValue, IntuitionConstants.MaxBody);
		Assert.Equal(ushort.MaxValue, IntuitionConstants.MaxPot);
		Assert.Equal((ushort)6, IntuitionConstants.KnobHorizontalMinimum);
		Assert.Equal((ushort)4, IntuitionConstants.KnobVerticalMinimum);
		Assert.Equal((ushort)5, IntuitionConstants.DefaultMouseQueue);
		Assert.Equal((short)-1, IntuitionConstants.StandardScreenHeight);
		Assert.Equal((short)-1, IntuitionConstants.StandardScreenWidth);
		Assert.Equal((ushort)139, IntuitionConstants.MaxPublicScreenName);
	}

	[Fact]
	public void EmbeddedGraphicsAndLayersFlagsAreExhaustiveDependencyAuthority()
	{
		AssertValues(new ulong[] { 0, 1, 2, 4, 8, 0x10 }, new[]
		{
			BitMapFlags.None, BitMapFlags.Clear, BitMapFlags.Displayable,
			BitMapFlags.Interleaved, BitMapFlags.Standard,
			BitMapFlags.MinimumPlanes,
		});
		AssertValues(new ulong[] { 0, 1, 2, 4, 8, 0x20 }, new[]
		{
			RastPortFlags.None, RastPortFlags.FirstDot, RastPortFlags.OneDot,
			RastPortFlags.DoubleBuffered, RastPortFlags.AreaOutline,
			RastPortFlags.NoCrossFill,
		});
		AssertValues(new ulong[]
		{
			0, 1, 2, 4, 0x10, 0x40, 0x80, 0x100, 0x200, 0x400,
		}, new[]
		{
			LayerFlags.None, LayerFlags.Simple, LayerFlags.Smart, LayerFlags.Super,
			LayerFlags.Updating, LayerFlags.Backdrop, LayerFlags.Refresh,
			LayerFlags.ClipRectsLost, LayerFlags.InternalRefresh,
			LayerFlags.InternalRefresh2,
		});
	}

	[Fact]
	public void ObsoleteCompatibilityAliasSurfaceMatchesC31HeaderCount()
	{
		var fields = typeof(IntuitionObsolete).GetFields(
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.Static);
		Assert.Equal(185, fields.Length);
		Assert.All(fields, field => Assert.True(field.IsLiteral));
		Assert.Equal(GadgetFlags.HighlightMask, IntuitionObsolete.GADGHIGHBITS);
		Assert.Equal(GadgetActivationFlags.None, IntuitionObsolete.STRINGLEFT);
		Assert.Equal(GadgetType.GadgetClassMask, IntuitionObsolete.GTYPEMASK);
		Assert.Equal(IDCMPFlags.LonelyMessage, IntuitionObsolete.LONELYMESSAGE);
		Assert.Equal(WindowFlags.HasZoom, IntuitionObsolete.HASZOOM);
		Assert.Equal(IntuitionGadgetClass.GA_LabelImage,
			IntuitionObsolete.GA_LABELIMAGE);
		Assert.Equal(IntuitionImageClass.IA_HighlightPen,
			IntuitionObsolete.IA_HIGHLIGHTPEN);
		Assert.Equal(DrawInfoPen.NumberOfPens, IntuitionObsolete.numDrIPens);
	}

	private static void AssertValues<T>(ulong[] expected, T[] actual)
		where T : struct, Enum
	{
		Assert.Equal(Enum.GetNames<T>().Length, actual.Length);
		Assert.Equal(expected.Length, actual.Length);
		for (var index = 0; index < expected.Length; index++)
			Assert.Equal(expected[index], Convert.ToUInt64(actual[index]));
	}
}
