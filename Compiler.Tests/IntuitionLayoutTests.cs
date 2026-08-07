using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class IntuitionLayoutTests
{
	[Theory]
	[InlineData(typeof(NewWindow), 48u)]
	[InlineData(typeof(ExtNewWindow), 52u)]
	[InlineData(typeof(Window), 136u)]
	[InlineData(typeof(NewScreen), 32u)]
	[InlineData(typeof(ExtNewScreen), 36u)]
	[InlineData(typeof(Screen), 346u)]
	[InlineData(typeof(Gadget), 44u)]
	[InlineData(typeof(ExtGadget), 56u)]
	[InlineData(typeof(BoolInfo), 10u)]
	[InlineData(typeof(PropInfo), 22u)]
	[InlineData(typeof(StringInfo), 36u)]
	[InlineData(typeof(IntuiMessage), 52u)]
	[InlineData(typeof(ExtIntuiMessage), 56u)]
	[InlineData(typeof(IntuiText), 20u)]
	[InlineData(typeof(Image), 20u)]
	[InlineData(typeof(Border), 16u)]
	[InlineData(typeof(Menu), 30u)]
	[InlineData(typeof(MenuItem), 34u)]
	[InlineData(typeof(Requester), 112u)]
	[InlineData(typeof(EasyStruct), 20u)]
	[InlineData(typeof(Remember), 12u)]
	[InlineData(typeof(DrawInfo), 50u)]
	[InlineData(typeof(ColorSpec), 8u)]
	[InlineData(typeof(PubScreenNode), 30u)]
	[InlineData(typeof(ScreenBuffer), 8u)]
	[InlineData(typeof(IBox), 8u)]
	[InlineData(typeof(TabletData), 24u)]
	[InlineData(typeof(TabletHookData), 16u)]
	[InlineData(typeof(Message), 20u)]
	[InlineData(typeof(MinList), 12u)]
	[InlineData(typeof(SemaphoreRequest), 12u)]
	[InlineData(typeof(SignalSemaphore), 46u)]
	[InlineData(typeof(LayerInfo), 102u)]
	[InlineData(typeof(BitMap), 40u)]
	[InlineData(typeof(ViewPort), 40u)]
	[InlineData(typeof(RastPort), 100u)]
	[InlineData(typeof(TextAttr), 8u)]
	public void StructuresMatchClassicM68kSizes(Type type, uint expectedSize)
	{
		Assert.Equal(expectedSize, (uint)Marshal.SizeOf(type));
		Assert.Equal(expectedSize, (uint)(uint)type.GetField("Size")!.GetValue(null)!);
	}

	[Theory]
	[InlineData(typeof(NewWindow), nameof(NewWindow.IDCMPFlags), 10)]
	[InlineData(typeof(NewWindow), nameof(NewWindow.FirstGadget), 18)]
	[InlineData(typeof(Window), nameof(Window.Flags), 24)]
	[InlineData(typeof(Window), nameof(Window.MenuStrip), 28)]
	[InlineData(typeof(Window), nameof(Window.IDCMPFlags), 82)]
	[InlineData(typeof(Window), nameof(Window.MoreFlags), 132)]
	[InlineData(typeof(NewScreen), nameof(NewScreen.Font), 16)]
	[InlineData(typeof(Screen), nameof(Screen.Font), 40)]
	[InlineData(typeof(Screen), nameof(Screen.ViewPort), 44)]
	[InlineData(typeof(Screen), nameof(Screen.RastPort), 84)]
	[InlineData(typeof(Screen), nameof(Screen.BitMap), 184)]
	[InlineData(typeof(Screen), nameof(Screen.LayerInfo), 224)]
	[InlineData(typeof(Gadget), nameof(Gadget.GadgetRender), 18)]
	[InlineData(typeof(Gadget), nameof(Gadget.UserData), 40)]
	[InlineData(typeof(IntuiMessage), nameof(IntuiMessage.Class), 20)]
	[InlineData(typeof(IntuiMessage), nameof(IntuiMessage.IDCMPWindow), 44)]
	[InlineData(typeof(IntuiText), nameof(IntuiText.LeftEdge), 4)]
	[InlineData(typeof(MenuItem), nameof(MenuItem.SubItem), 28)]
	[InlineData(typeof(Requester), nameof(Requester.Layer), 32)]
	public void KeyFieldsMatchClassicM68kOffsets(Type type, string fieldName, int expectedOffset)
	{
		Assert.Equal(expectedOffset, Marshal.OffsetOf(type, fieldName).ToInt32());
	}

	[Fact]
	public void AllDeclaredFieldsMatchClassicM68kOffsets()
	{
		AssertOffsets<NewWindow>(
			(nameof(NewWindow.LeftEdge), 0), (nameof(NewWindow.TopEdge), 2),
			(nameof(NewWindow.Width), 4), (nameof(NewWindow.Height), 6),
			(nameof(NewWindow.DetailPen), 8), (nameof(NewWindow.BlockPen), 9),
			(nameof(NewWindow.IDCMPFlags), 10), (nameof(NewWindow.Flags), 14),
			(nameof(NewWindow.FirstGadget), 18), (nameof(NewWindow.CheckMark), 22),
			(nameof(NewWindow.Title), 26), (nameof(NewWindow.Screen), 30),
			(nameof(NewWindow.BitMap), 34), (nameof(NewWindow.MinWidth), 38),
			(nameof(NewWindow.MinHeight), 40), (nameof(NewWindow.MaxWidth), 42),
			(nameof(NewWindow.MaxHeight), 44), (nameof(NewWindow.Type), 46));
		AssertOffsets<ExtNewWindow>((nameof(ExtNewWindow.Extension), 48));
		AssertOffsets<Window>(
			(nameof(Window.NextWindow), 0), (nameof(Window.LeftEdge), 4),
			(nameof(Window.TopEdge), 6), (nameof(Window.Width), 8),
			(nameof(Window.Height), 10), (nameof(Window.MouseY), 12),
			(nameof(Window.MouseX), 14), (nameof(Window.MinWidth), 16),
			(nameof(Window.MinHeight), 18), (nameof(Window.MaxWidth), 20),
			(nameof(Window.MaxHeight), 22), (nameof(Window.Flags), 24),
			(nameof(Window.MenuStrip), 28), (nameof(Window.Title), 32),
			(nameof(Window.FirstRequest), 36), (nameof(Window.DMRequest), 40),
			(nameof(Window.RequesterCount), 44), (nameof(Window.Screen), 46),
			(nameof(Window.RastPort), 50), (nameof(Window.BorderLeft), 54),
			(nameof(Window.BorderTop), 55), (nameof(Window.BorderRight), 56),
			(nameof(Window.BorderBottom), 57), (nameof(Window.BorderRastPort), 58),
			(nameof(Window.FirstGadget), 62), (nameof(Window.Parent), 66),
			(nameof(Window.Descendant), 70), (nameof(Window.Pointer), 74),
			(nameof(Window.PointerHeight), 78), (nameof(Window.PointerWidth), 79),
			(nameof(Window.XOffset), 80), (nameof(Window.YOffset), 81),
			(nameof(Window.IDCMPFlags), 82), (nameof(Window.UserPort), 86),
			(nameof(Window.WindowPort), 90), (nameof(Window.MessageKey), 94),
			(nameof(Window.DetailPen), 98), (nameof(Window.BlockPen), 99),
			(nameof(Window.CheckMark), 100), (nameof(Window.ScreenTitle), 104),
			(nameof(Window.GzzMouseX), 108), (nameof(Window.GzzMouseY), 110),
			(nameof(Window.GzzWidth), 112), (nameof(Window.GzzHeight), 114),
			(nameof(Window.ExtData), 116), (nameof(Window.UserData), 120),
			(nameof(Window.Layer), 124), (nameof(Window.Font), 128),
			(nameof(Window.MoreFlags), 132));
		AssertOffsets<NewScreen>(
			(nameof(NewScreen.LeftEdge), 0), (nameof(NewScreen.TopEdge), 2),
			(nameof(NewScreen.Width), 4), (nameof(NewScreen.Height), 6),
			(nameof(NewScreen.Depth), 8), (nameof(NewScreen.DetailPen), 10),
			(nameof(NewScreen.BlockPen), 11), (nameof(NewScreen.ViewModes), 12),
			(nameof(NewScreen.Type), 14), (nameof(NewScreen.Font), 16),
			(nameof(NewScreen.DefaultTitle), 20), (nameof(NewScreen.Gadgets), 24),
			(nameof(NewScreen.CustomBitMap), 28));
		AssertOffsets<ExtNewScreen>((nameof(ExtNewScreen.Extension), 32));
		AssertOffsets<Screen>(
			(nameof(Screen.NextScreen), 0), (nameof(Screen.FirstWindow), 4),
			(nameof(Screen.LeftEdge), 8), (nameof(Screen.TopEdge), 10),
			(nameof(Screen.Width), 12), (nameof(Screen.Height), 14),
			(nameof(Screen.MouseY), 16), (nameof(Screen.MouseX), 18),
			(nameof(Screen.Flags), 20), (nameof(Screen.Title), 22),
			(nameof(Screen.DefaultTitle), 26), (nameof(Screen.BarHeight), 30),
			(nameof(Screen.BarVBorder), 31), (nameof(Screen.BarHBorder), 32),
			(nameof(Screen.MenuVBorder), 33), (nameof(Screen.MenuHBorder), 34),
			(nameof(Screen.WindowBorderTop), 35), (nameof(Screen.WindowBorderLeft), 36),
			(nameof(Screen.WindowBorderRight), 37), (nameof(Screen.WindowBorderBottom), 38),
			(nameof(Screen.Font), 40), (nameof(Screen.ViewPort), 44),
			(nameof(Screen.RastPort), 84), (nameof(Screen.BitMap), 184),
			(nameof(Screen.LayerInfo), 224), (nameof(Screen.FirstGadget), 326),
			(nameof(Screen.DetailPen), 330), (nameof(Screen.BlockPen), 331),
			(nameof(Screen.SaveColor0), 332), (nameof(Screen.BarLayer), 334),
			(nameof(Screen.ExtData), 338), (nameof(Screen.UserData), 342));
		AssertOffsets<Gadget>(
			(nameof(Gadget.NextGadget), 0), (nameof(Gadget.LeftEdge), 4),
			(nameof(Gadget.TopEdge), 6), (nameof(Gadget.Width), 8),
			(nameof(Gadget.Height), 10), (nameof(Gadget.Flags), 12),
			(nameof(Gadget.Activation), 14), (nameof(Gadget.GadgetType), 16),
			(nameof(Gadget.GadgetRender), 18), (nameof(Gadget.SelectRender), 22),
			(nameof(Gadget.GadgetText), 26), (nameof(Gadget.MutualExclude), 30),
			(nameof(Gadget.SpecialInfo), 34), (nameof(Gadget.GadgetID), 38),
			(nameof(Gadget.UserData), 40));
		AssertOffsets<ExtGadget>((nameof(ExtGadget.MoreFlags), 44),
			(nameof(ExtGadget.BoundsLeftEdge), 48), (nameof(ExtGadget.BoundsTopEdge), 50),
			(nameof(ExtGadget.BoundsWidth), 52), (nameof(ExtGadget.BoundsHeight), 54));
		AssertOffsets<PropInfo>(
			(nameof(PropInfo.Flags), 0), (nameof(PropInfo.HorizPot), 2),
			(nameof(PropInfo.VertPot), 4), (nameof(PropInfo.HorizBody), 6),
			(nameof(PropInfo.VertBody), 8), (nameof(PropInfo.ContainerWidth), 10),
			(nameof(PropInfo.ContainerHeight), 12), (nameof(PropInfo.HorizontalPotResolution), 14),
			(nameof(PropInfo.VerticalPotResolution), 16), (nameof(PropInfo.LeftBorder), 18),
			(nameof(PropInfo.TopBorder), 20));
		AssertOffsets<StringInfo>(
			(nameof(StringInfo.Buffer), 0), (nameof(StringInfo.UndoBuffer), 4),
			(nameof(StringInfo.BufferPosition), 8), (nameof(StringInfo.MaxChars), 10),
			(nameof(StringInfo.DisplayPosition), 12), (nameof(StringInfo.UndoPosition), 14),
			(nameof(StringInfo.NumberOfChars), 16), (nameof(StringInfo.DisplayCount), 18),
			(nameof(StringInfo.ContainerLeft), 20), (nameof(StringInfo.ContainerTop), 22),
			(nameof(StringInfo.Extension), 24), (nameof(StringInfo.LongInt), 28),
			(nameof(StringInfo.AlternateKeyMap), 32));
		AssertOffsets<IntuiMessage>(
			(nameof(IntuiMessage.ExecMessage), 0), (nameof(IntuiMessage.Class), 20),
			(nameof(IntuiMessage.Code), 24), (nameof(IntuiMessage.Qualifier), 26),
			(nameof(IntuiMessage.IAddress), 28), (nameof(IntuiMessage.MouseX), 32),
			(nameof(IntuiMessage.MouseY), 34), (nameof(IntuiMessage.Seconds), 36),
			(nameof(IntuiMessage.Micros), 40), (nameof(IntuiMessage.IDCMPWindow), 44),
			(nameof(IntuiMessage.SpecialLink), 48));
		AssertOffsets<IntuiText>(
			(nameof(IntuiText.FrontPen), 0), (nameof(IntuiText.BackPen), 1),
			(nameof(IntuiText.DrawMode), 2), (nameof(IntuiText.LeftEdge), 4),
			(nameof(IntuiText.TopEdge), 6), (nameof(IntuiText.Font), 8),
			(nameof(IntuiText.Text), 12), (nameof(IntuiText.NextText), 16));
		AssertOffsets<Border>(
			(nameof(Border.LeftEdge), 0), (nameof(Border.TopEdge), 2),
			(nameof(Border.FrontPen), 4), (nameof(Border.BackPen), 5),
			(nameof(Border.DrawMode), 6), (nameof(Border.Count), 7),
			(nameof(Border.XY), 8), (nameof(Border.NextBorder), 12));
		AssertOffsets<Image>(
			(nameof(Image.LeftEdge), 0), (nameof(Image.TopEdge), 2),
			(nameof(Image.Width), 4), (nameof(Image.Height), 6),
			(nameof(Image.Depth), 8), (nameof(Image.ImageData), 10),
			(nameof(Image.PlanePick), 14), (nameof(Image.PlaneOnOff), 15),
			(nameof(Image.NextImage), 16));
		AssertOffsets<Menu>(
			(nameof(Menu.NextMenu), 0), (nameof(Menu.LeftEdge), 4),
			(nameof(Menu.TopEdge), 6), (nameof(Menu.Width), 8),
			(nameof(Menu.Height), 10), (nameof(Menu.Flags), 12),
			(nameof(Menu.MenuName), 14), (nameof(Menu.FirstItem), 18),
			(nameof(Menu.JazzX), 22), (nameof(Menu.JazzY), 24),
			(nameof(Menu.BeatX), 26), (nameof(Menu.BeatY), 28));
		AssertOffsets<MenuItem>(
			(nameof(MenuItem.NextItem), 0), (nameof(MenuItem.LeftEdge), 4),
			(nameof(MenuItem.TopEdge), 6), (nameof(MenuItem.Width), 8),
			(nameof(MenuItem.Height), 10), (nameof(MenuItem.Flags), 12),
			(nameof(MenuItem.MutualExclude), 14), (nameof(MenuItem.ItemFill), 18),
			(nameof(MenuItem.SelectFill), 22), (nameof(MenuItem.Command), 26),
			(nameof(MenuItem.SubItem), 28), (nameof(MenuItem.NextSelect), 32));
		AssertOffsets<Requester>(
			(nameof(Requester.OlderRequest), 0), (nameof(Requester.LeftEdge), 4),
			(nameof(Requester.TopEdge), 6), (nameof(Requester.Width), 8),
			(nameof(Requester.Height), 10), (nameof(Requester.RelativeLeft), 12),
			(nameof(Requester.RelativeTop), 14), (nameof(Requester.Gadget), 16),
			(nameof(Requester.Border), 20), (nameof(Requester.Text), 24),
			(nameof(Requester.Flags), 28), (nameof(Requester.BackFill), 30),
			(nameof(Requester.Layer), 32), (nameof(Requester.ImageBitMap), 68),
			(nameof(Requester.Window), 72), (nameof(Requester.Image), 76));
		AssertOffsets<DrawInfo>(
			(nameof(DrawInfo.Version), 0), (nameof(DrawInfo.NumberOfPens), 2),
			(nameof(DrawInfo.Pens), 4), (nameof(DrawInfo.Font), 8),
			(nameof(DrawInfo.Depth), 12), (nameof(DrawInfo.ResolutionX), 14),
			(nameof(DrawInfo.ResolutionY), 16), (nameof(DrawInfo.Flags), 18),
			(nameof(DrawInfo.CheckMark), 22), (nameof(DrawInfo.AmigaKey), 26),
			(nameof(DrawInfo.Reserved), 30));
		AssertOffsets<PubScreenNode>(
			(nameof(PubScreenNode.Node), 0), (nameof(PubScreenNode.Screen), 14),
			(nameof(PubScreenNode.Flags), 18), (nameof(PubScreenNode.SizeInBytes), 20),
			(nameof(PubScreenNode.VisitorCount), 22), (nameof(PubScreenNode.SignalTask), 24),
			(nameof(PubScreenNode.SignalBit), 28));
		AssertOffsets<Message>((nameof(Message.Node), 0), (nameof(Message.ReplyPort), 14),
			(nameof(Message.Length), 18));
		AssertOffsets<SignalSemaphore>((nameof(SignalSemaphore.Link), 0),
			(nameof(SignalSemaphore.NestCount), 14), (nameof(SignalSemaphore.WaitQueue), 16),
			(nameof(SignalSemaphore.MultipleLink), 28), (nameof(SignalSemaphore.Owner), 40),
			(nameof(SignalSemaphore.QueueCount), 44));
		AssertOffsets<LayerInfo>((nameof(LayerInfo.TopLayer), 0),
			(nameof(LayerInfo.CheckLayer), 4), (nameof(LayerInfo.Obscured), 8),
			(nameof(LayerInfo.FreeClipRects), 12), (nameof(LayerInfo.PrivateReserve1), 16),
			(nameof(LayerInfo.PrivateReserve2), 20), (nameof(LayerInfo.Lock), 24),
			(nameof(LayerInfo.GraphicsSemaphoreHead), 70), (nameof(LayerInfo.PrivateReserve3), 82),
			(nameof(LayerInfo.PrivateReserve4), 84), (nameof(LayerInfo.Flags), 88),
			(nameof(LayerInfo.FattenCount), 90), (nameof(LayerInfo.LockLayersCount), 91),
			(nameof(LayerInfo.PrivateReserve5), 92), (nameof(LayerInfo.BlankHook), 94),
			(nameof(LayerInfo.Extra), 98));
		AssertOffsets<BitMap>((nameof(BitMap.BytesPerRow), 0), (nameof(BitMap.Rows), 2),
			(nameof(BitMap.Flags), 4), (nameof(BitMap.Depth), 5), (nameof(BitMap.Plane0), 8),
			(nameof(BitMap.Plane7), 36));
		AssertOffsets<ViewPort>((nameof(ViewPort.Next), 0), (nameof(ViewPort.ColorMap), 4),
			(nameof(ViewPort.DisplayInstructions), 8), (nameof(ViewPort.SpriteInstructions), 12),
			(nameof(ViewPort.ColorInstructions), 16), (nameof(ViewPort.UserCopperInstructions), 20),
			(nameof(ViewPort.DisplayWidth), 24), (nameof(ViewPort.DisplayHeight), 26),
			(nameof(ViewPort.DisplayXOffset), 28), (nameof(ViewPort.DisplayYOffset), 30),
			(nameof(ViewPort.Modes), 32), (nameof(ViewPort.SpritePriorities), 34),
			(nameof(ViewPort.ExtendedModes), 35), (nameof(ViewPort.RasInfo), 36));
		AssertOffsets<TextAttr>((nameof(TextAttr.Name), 0), (nameof(TextAttr.YSize), 4),
			(nameof(TextAttr.Style), 6), (nameof(TextAttr.Flags), 7));
		AssertOffsets<ExtIntuiMessage>((nameof(ExtIntuiMessage.IntuiMessage), 0),
			(nameof(ExtIntuiMessage.TabletData), 52));
		AssertOffsets<IBox>((nameof(IBox.Left), 0), (nameof(IBox.Top), 2),
			(nameof(IBox.Width), 4), (nameof(IBox.Height), 6));
		AssertOffsets<TabletData>((nameof(TabletData.XFraction), 0),
			(nameof(TabletData.YFraction), 2), (nameof(TabletData.TabletX), 4),
			(nameof(TabletData.TabletY), 8), (nameof(TabletData.RangeX), 12),
			(nameof(TabletData.RangeY), 16), (nameof(TabletData.TagList), 20));
		AssertOffsets<TabletHookData>((nameof(TabletHookData.Screen), 0),
			(nameof(TabletHookData.Width), 4), (nameof(TabletHookData.Height), 8),
			(nameof(TabletHookData.ScreenChanged), 12));
		AssertOffsets<BoolInfo>((nameof(BoolInfo.Flags), 0), (nameof(BoolInfo.Mask), 2),
			(nameof(BoolInfo.Reserved), 6));
		AssertOffsets<ColorSpec>((nameof(ColorSpec.ColorIndex), 0),
			(nameof(ColorSpec.Red), 2), (nameof(ColorSpec.Green), 4),
			(nameof(ColorSpec.Blue), 6));
		AssertOffsets<ScreenBuffer>((nameof(ScreenBuffer.BitMap), 0),
			(nameof(ScreenBuffer.DoubleBufferInfo), 4));
		AssertOffsets<Remember>((nameof(Remember.NextRemember), 0),
			(nameof(Remember.RememberSize), 4), (nameof(Remember.Memory), 8));
		AssertOffsets<EasyStruct>((nameof(EasyStruct.StructureSize), 0),
			(nameof(EasyStruct.Flags), 4), (nameof(EasyStruct.Title), 8),
			(nameof(EasyStruct.TextFormat), 12), (nameof(EasyStruct.GadgetFormat), 16));
		AssertOffsets<MinList>((nameof(MinList.Head), 0), (nameof(MinList.Tail), 4),
			(nameof(MinList.TailPred), 8));
		AssertOffsets<SemaphoreRequest>((nameof(SemaphoreRequest.Link), 0),
			(nameof(SemaphoreRequest.Waiter), 8));
		AssertOffsets<RastPort>(
			(nameof(RastPort.Layer), 0), (nameof(RastPort.BitMap), 4),
			(nameof(RastPort.AreaPattern), 8), (nameof(RastPort.TemporaryRaster), 12),
			(nameof(RastPort.AreaInfo), 16), (nameof(RastPort.GelsInfo), 20),
			(nameof(RastPort.Mask), 24), (nameof(RastPort.ForegroundPen), 25),
			(nameof(RastPort.BackgroundPen), 26), (nameof(RastPort.AreaOutlinePen), 27),
			(nameof(RastPort.DrawMode), 28), (nameof(RastPort.AreaPatternSize), 29),
			(nameof(RastPort.LinePatternCount), 30), ("_padding", 31),
			(nameof(RastPort.Flags), 32), (nameof(RastPort.LinePattern), 34),
			(nameof(RastPort.CurrentX), 36), (nameof(RastPort.CurrentY), 38),
			(nameof(RastPort.Minterms), 40), (nameof(RastPort.PenWidth), 48),
			(nameof(RastPort.PenHeight), 50), (nameof(RastPort.Font), 52),
			(nameof(RastPort.AlgorithmicStyle), 56), (nameof(RastPort.TextFlags), 57),
			(nameof(RastPort.TextHeight), 58), (nameof(RastPort.TextWidth), 60),
			(nameof(RastPort.TextBaseline), 62), (nameof(RastPort.TextSpacing), 64),
			(nameof(RastPort.User), 66), (nameof(RastPort.LongReserved), 70),
			(nameof(RastPort.WordReserved), 78), (nameof(RastPort.Reserved), 92));
		AssertOffsets<BitMap>((nameof(BitMap.BytesPerRow), 0), (nameof(BitMap.Rows), 2),
			(nameof(BitMap.Flags), 4), (nameof(BitMap.Depth), 5), ("_padding", 6),
			(nameof(BitMap.Plane0), 8), (nameof(BitMap.Plane1), 12),
			(nameof(BitMap.Plane2), 16), (nameof(BitMap.Plane3), 20),
			(nameof(BitMap.Plane4), 24), (nameof(BitMap.Plane5), 28),
			(nameof(BitMap.Plane6), 32), (nameof(BitMap.Plane7), 36));
		AssertOffsets<Requester>(("_padding", 31),
			(nameof(Requester.RequesterPadding1), 36), (nameof(Requester.ImageBitMap), 68),
			(nameof(Requester.Window), 72), (nameof(Requester.Image), 76),
			(nameof(Requester.RequesterPadding2), 80));
		AssertOffsets<Node>((nameof(Node.Successor), 0), (nameof(Node.Predecessor), 4),
			(nameof(Node.Type), 8), (nameof(Node.Priority), 9), (nameof(Node.Name), 10));
		AssertOffsets<MinNode>((nameof(MinNode.Successor), 0),
			(nameof(MinNode.Predecessor), 4));
	}

	private static void AssertOffsets<T>(params (string Name, int Offset)[] expected)
	{
		foreach (var (name, offset) in expected)
		{
			Assert.Equal(offset, Marshal.OffsetOf<T>(name).ToInt32());
		}
	}

	[Fact]
	public void ClassicFlagValuesRemainUnchanged()
	{
		Assert.Equal(0x0000_0200u, (uint)IDCMPFlags.CloseWindow);
		Assert.Equal(0x0000_0400u, (uint)WindowFlags.GimmeZeroZero);
		Assert.Equal(0x8000u, (ushort)GadgetFlags.Extended);
		Assert.Equal(0x4000u, (ushort)GadgetActivationFlags.ActiveGadget);
		Assert.Equal(0x0003u, (ushort)GadgetType.ProportionalGadget);
		Assert.Equal(0x1000u, (ushort)ScreenFlags.Extended);
		Assert.Equal(0x2000u, (ushort)MenuItemFlags.Highlighted);
		Assert.Equal(0x8000u, (ushort)RequesterFlags.DeferRefresh);
		Assert.Equal(0x0001u, (ushort)BoolInfoFlags.Mask);
		Assert.Equal(0x0010u, (byte)BitMapFlags.MinimumPlanes);
		Assert.Equal(0x0020u, (ushort)RastPortFlags.NoCrossFill);
		Assert.Equal(0x0400u, (ushort)LayerFlags.InternalRefresh2);
	}
}
