using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class RemainingLibraryLayoutTests
{
	[Theory]
	[InlineData(typeof(KeyMap), 32u)]
	[InlineData(typeof(KeyMapNode), 46u)]
	[InlineData(typeof(ExtendedKeyMapNode), 60u)]
	[InlineData(typeof(KeyMapResource), 28u)]
	[InlineData(typeof(UCS4ConversionTable), 8u)]
	[InlineData(typeof(UCS4CharsetCode), 8u)]
	[InlineData(typeof(UCS4CharsetConversionTable), 4u)]
	[InlineData(typeof(FileHandle), 44u)]
	[InlineData(typeof(CopList), 38u)]
	[InlineData(typeof(AmigaGuideHost), 40u)]
	[InlineData(typeof(AppIconRenderMessage), 28u)]
	[InlineData(typeof(MidiMessage), 8u)]
	[InlineData(typeof(SysExFilter), 4u)]
	[InlineData(typeof(MidiCluster), 48u)]
	[InlineData(typeof(MidiLink), 56u)]
	[InlineData(typeof(MidiNode), 72u)]
	[InlineData(typeof(CamdLinkMessage), 8u)]
	[InlineData(typeof(ClusterNotifyNode), 16u)]
	[InlineData(typeof(CyberModeNode), 60u)]
	[InlineData(typeof(CDrawMsg), 26u)]
	[InlineData(typeof(KeyQuery), 6u)]
	[InlineData(typeof(NVInfo), 8u)]
	[InlineData(typeof(NVEntry), 20u)]
	[InlineData(typeof(SockAddr), 16u)]
	[InlineData(typeof(IfReq), 32u)]
	[InlineData(typeof(IfAliasReq), 64u)]
	[InlineData(typeof(IfConf), 8u)]
	[InlineData(typeof(RouteMetrics), 36u)]
	[InlineData(typeof(RouteStat), 10u)]
	[InlineData(typeof(RouteEntry), 48u)]
	[InlineData(typeof(RouteMessageHeader), 70u)]
	[InlineData(typeof(RouteControlBlock), 16u)]
	public void NewStructuresMatchDeclaredM68kSizes(Type type, uint expectedSize)
	{
		Assert.Equal(expectedSize, (uint)Marshal.SizeOf(type));
		Assert.Equal(expectedSize, (uint)type.GetField("Size")!.GetValue(null)!);
	}

	[Fact]
	public void KeymapFieldsMatchPublicHeaderOffsets()
	{
		AssertOffsets<KeyMap>(
			(nameof(KeyMap.LowKeyMapTypes), 0), (nameof(KeyMap.LowKeyMap), 4),
			(nameof(KeyMap.LowCapsable), 8), (nameof(KeyMap.LowRepeatable), 12),
			(nameof(KeyMap.HighKeyMapTypes), 16), (nameof(KeyMap.HighKeyMap), 20),
			(nameof(KeyMap.HighCapsable), 24), (nameof(KeyMap.HighRepeatable), 28));
		AssertOffsets<KeyMapNode>((nameof(KeyMapNode.Node), 0), (nameof(KeyMapNode.KeyMap), 14));
		AssertOffsets<ExtendedKeyMapNode>((nameof(ExtendedKeyMapNode.Node), 0),
			(nameof(ExtendedKeyMapNode.NodePadding), 14), (nameof(ExtendedKeyMapNode.KeyMap), 16),
			(nameof(ExtendedKeyMapNode.SegmentList), 48), (nameof(ExtendedKeyMapNode.Resident), 52),
			(nameof(ExtendedKeyMapNode.Future), 56));
		AssertOffsets<KeyMapResource>((nameof(KeyMapResource.Node), 0), (nameof(KeyMapResource.KeyMaps), 14));
		AssertOffsets<UCS4ConversionTable>((nameof(UCS4ConversionTable.FirstChar), 0),
			(nameof(UCS4ConversionTable.LastChar), 2), (nameof(UCS4ConversionTable.ConversionTable), 4));
		AssertOffsets<UCS4CharsetConversionTable>((nameof(UCS4CharsetConversionTable.Mapping), 0));
	}

	[Fact]
	public void CamdFieldsMatchPublicHeaderOffsets()
	{
		AssertOffsets<MidiCluster>((nameof(MidiCluster.Node), 0), (nameof(MidiCluster.Participants), 14),
			(nameof(MidiCluster.Receivers), 16), (nameof(MidiCluster.Senders), 30),
			(nameof(MidiCluster.PublicParticipants), 44), (nameof(MidiCluster.Flags), 46));
		AssertOffsets<MidiLink>((nameof(MidiLink.Node), 0), (nameof(MidiLink.Padding), 14),
			(nameof(MidiLink.OwnerNode), 16), (nameof(MidiLink.MidiNode), 24),
			(nameof(MidiLink.Location), 28), (nameof(MidiLink.ClusterComment), 32),
			(nameof(MidiLink.Flags), 36), (nameof(MidiLink.PortId), 37),
			(nameof(MidiLink.ChannelMask), 38), (nameof(MidiLink.EventTypeMask), 40),
			(nameof(MidiLink.SysExFilter), 44), (nameof(MidiLink.ParserData), 48),
			(nameof(MidiLink.UserData), 52));
		AssertOffsets<MidiNode>((nameof(MidiNode.Node), 0), (nameof(MidiNode.ClientType), 14),
			(nameof(MidiNode.Image), 16), (nameof(MidiNode.OutLinks), 20),
			(nameof(MidiNode.InLinks), 32), (nameof(MidiNode.SignalTask), 44),
			(nameof(MidiNode.ReceiveHook), 48), (nameof(MidiNode.ParticipantHook), 52),
			(nameof(MidiNode.ReceiveSignalBit), 56), (nameof(MidiNode.ParticipantSignalBit), 57),
			(nameof(MidiNode.ErrorFilter), 58), (nameof(MidiNode.Alignment), 59),
			(nameof(MidiNode.TimeStamp), 60), (nameof(MidiNode.MessageQueueSize), 64),
			(nameof(MidiNode.SysExQueueSize), 68));
		AssertOffsets<ClusterNotifyNode>((nameof(ClusterNotifyNode.Node), 0),
			(nameof(ClusterNotifyNode.Task), 8), (nameof(ClusterNotifyNode.SignalBit), 12));
	}

	[Fact]
	public void CyberGraphxAndInputFieldsMatchPublicHeaderOffsets()
	{
		AssertOffsets<CyberModeNode>((nameof(CyberModeNode.Node), 0),
			(nameof(CyberModeNode.ModeText), 14), (nameof(CyberModeNode.DisplayId), 46),
			(nameof(CyberModeNode.Width), 50), (nameof(CyberModeNode.Height), 52),
			(nameof(CyberModeNode.Depth), 54), (nameof(CyberModeNode.DisplayTagList), 56));
		AssertOffsets<CDrawMsg>((nameof(CDrawMsg.Memory), 0), (nameof(CDrawMsg.OffsetX), 4),
			(nameof(CDrawMsg.OffsetY), 8), (nameof(CDrawMsg.XSize), 12),
			(nameof(CDrawMsg.YSize), 16), (nameof(CDrawMsg.BytesPerRow), 20),
			(nameof(CDrawMsg.BytesPerPixel), 22), (nameof(CDrawMsg.ColorModel), 24));
		AssertOffsets<KeyQuery>((nameof(KeyQuery.KeyCode), 0), (nameof(KeyQuery.Pressed), 2));
		AssertOffsets<NVInfo>((nameof(NVInfo.MaximumStorage), 0), (nameof(NVInfo.FreeStorage), 4));
		AssertOffsets<NVEntry>((nameof(NVEntry.Node), 0), (nameof(NVEntry.Name), 8),
			(nameof(NVEntry.SizeInBytes), 12), (nameof(NVEntry.Protection), 16));
	}

	[Fact]
	public void NetworkInterfaceAndRouteFieldsMatchPublicHeaders()
	{
		AssertOffsets<SockAddr>((nameof(SockAddr.Length), 0), (nameof(SockAddr.Family), 1),
			(nameof(SockAddr.Data), 2));
		AssertOffsets<IfReq>((nameof(IfReq.Name), 0), (nameof(IfReq.RequestData), 16));
		AssertOffsets<IfAliasReq>((nameof(IfAliasReq.Name), 0),
			(nameof(IfAliasReq.Address), 16), (nameof(IfAliasReq.BroadcastAddress), 32),
			(nameof(IfAliasReq.Mask), 48));
		AssertOffsets<IfConf>((nameof(IfConf.Length), 0), (nameof(IfConf.Request), 4));
		AssertOffsets<RouteMetrics>((nameof(RouteMetrics.Locks), 0), (nameof(RouteMetrics.Mtu), 4),
			(nameof(RouteMetrics.HopCount), 8), (nameof(RouteMetrics.Expire), 12),
			(nameof(RouteMetrics.ReceivePipe), 16), (nameof(RouteMetrics.SendPipe), 20),
			(nameof(RouteMetrics.SlowStartThreshold), 24), (nameof(RouteMetrics.RoundTripTime), 28),
			(nameof(RouteMetrics.RoundTripVariance), 32));
		AssertOffsets<RouteEntry>((nameof(RouteEntry.Hash), 0), (nameof(RouteEntry.Destination), 4),
			(nameof(RouteEntry.Gateway), 20), (nameof(RouteEntry.Flags), 36),
			(nameof(RouteEntry.ReferenceCount), 38), (nameof(RouteEntry.Use), 40),
			(nameof(RouteEntry.Interface), 44));
		AssertOffsets<RouteMessageHeader>((nameof(RouteMessageHeader.MessageLength), 0),
			(nameof(RouteMessageHeader.Version), 2), (nameof(RouteMessageHeader.Type), 3),
			(nameof(RouteMessageHeader.Index), 4), (nameof(RouteMessageHeader.ProcessId), 6),
			(nameof(RouteMessageHeader.Addresses), 10), (nameof(RouteMessageHeader.Sequence), 14),
			(nameof(RouteMessageHeader.Error), 18), (nameof(RouteMessageHeader.Flags), 22),
			(nameof(RouteMessageHeader.Use), 26), (nameof(RouteMessageHeader.Initializers), 30),
			(nameof(RouteMessageHeader.Metrics), 34));
	}

	[Fact]
	public void NewEnumValuesPreserveSourceBitPatterns()
	{
		Assert.Equal(0x80000000u, BsdSocketConstants.TagUser);
		Assert.Equal(0x8000, (ushort)InterfaceFlags.Multicast);
		Assert.Equal(0x8000, (ushort)RouteFlags.Protocol1);
		Assert.Equal(0x4000, (ushort)InterfaceFlags.Sana);
		Assert.Equal(0x00007fffu, (uint)MidiEventMask.All);
		Assert.Equal(0x00800000u, (uint)LowLevelJoystickButtons.Blue);
		Assert.Equal(0x80000000u, (uint)NonvolatileEntryFlags.ApplicationName);
		Assert.Equal(0x0du, (uint)CyberPixelFormat.Rgba32);
		Assert.Equal(0x20u, (byte)KeyMapType.Dead);
		Assert.Equal((uint)CyberPixelFormat.Rgb15X, (uint)CyberPixelFormat.Bgr15);
		Assert.False(Attribute.IsDefined(typeof(DataTypeFlags), typeof(FlagsAttribute)));
		Assert.False(Attribute.IsDefined(typeof(DataTypeToolFlags), typeof(FlagsAttribute)));
		Assert.Equal(0x80040014u, (uint)CyberModeRequestTag.WindowTitle);
		Assert.Equal(0x85231026u, (uint)CyberProcessPixelTag.GradientSymmetricCenter);
		Assert.Equal(0x100u, (uint)CyberExtendedBitmapFlags.ThreeDTarget);
		Assert.Equal(0x78u, (uint)MidiController.Maximum);
		Assert.Equal(0x7fu, (uint)MidiChannelMode.PolyMode);
		Assert.Equal(0x40u, (uint)CamdErrorFlags.SysExTooBig);
		Assert.Equal(0xf7u, (byte)MidiStatus.EndOfExclusive);
		Assert.Equal(0x8000004eu, (uint)MidiLinkTag.Name);
		Assert.Equal(0x8000004du, (uint)MidiNodeTag.ErrorCode);
		Assert.Equal(3u, (uint)LowLevelJoyPortAttributeType.Joystick);
		Assert.Equal(0x80c00102u, LowLevelConstants.SetJoyPortReinitialize);
		Assert.Equal(0x7bu, LowLevelConstants.Port0JoyRight);
		Assert.Equal(16, BsdNetworkConstants.InterfaceNameSize);
	}

	[Fact]
	public void AuditedClassicRecordsMatchCanonicalHeaderOffsets()
	{
		AssertOffsets<FileHandle>(
			(nameof(FileHandle.Link), 0), (nameof(FileHandle.Port), 4),
			(nameof(FileHandle.Type), 8), (nameof(FileHandle.Buffer), 12),
			(nameof(FileHandle.Position), 16), (nameof(FileHandle.End), 20),
			(nameof(FileHandle.Functions), 24), (nameof(FileHandle.Function2), 28),
			(nameof(FileHandle.Function3), 32), (nameof(FileHandle.Arguments), 36),
			(nameof(FileHandle.Argument2), 40));
		AssertOffsets<CopList>(
			(nameof(CopList.Next), 0), (nameof(CopList.SystemCopy), 4),
			(nameof(CopList.ViewPort), 8), (nameof(CopList.Instructions), 12),
			(nameof(CopList.InstructionPointer), 16), (nameof(CopList.LongFrameStart), 20),
			(nameof(CopList.ShortFrameStart), 24), (nameof(CopList.Count), 28),
			(nameof(CopList.MaxCount), 30), (nameof(CopList.YOffset), 32),
			(nameof(CopList.ShortLongRepeat), 34), (nameof(CopList.Flags), 36));
		AssertOffsets<AmigaGuideHost>(
			(nameof(AmigaGuideHost.Dispatcher), 0), (nameof(AmigaGuideHost.Reserved), 20),
			(nameof(AmigaGuideHost.Flags), 24), (nameof(AmigaGuideHost.UseCount), 28),
			(nameof(AmigaGuideHost.SystemData), 32), (nameof(AmigaGuideHost.UserData), 36));
		AssertOffsets<AppIconRenderMessage>(
			(nameof(AppIconRenderMessage.RastPort), 0), (nameof(AppIconRenderMessage.Icon), 4),
			(nameof(AppIconRenderMessage.Label), 8), (nameof(AppIconRenderMessage.Tags), 12),
			(nameof(AppIconRenderMessage.Left), 16), (nameof(AppIconRenderMessage.Top), 18),
			(nameof(AppIconRenderMessage.Width), 20), (nameof(AppIconRenderMessage.Height), 22),
			(nameof(AppIconRenderMessage.State), 24));
	}

	private static void AssertOffsets<T>(params (string Name, int Offset)[] expected)
	{
		foreach (var (name, offset) in expected)
		{
			Assert.Equal(offset, Marshal.OffsetOf<T>(name).ToInt32());
		}
	}

	[Fact]
	public void AuditedStringFieldsUseGuestStringWrappers()
	{
		Assert.Equal(typeof(STRPTR), typeof(WBArg).GetField(nameof(WBArg.Name))!.FieldType);
		Assert.Equal(typeof(STRPTR), typeof(WBStartup).GetField(nameof(WBStartup.ToolWindow))!.FieldType);
		Assert.Equal(typeof(STRPTR), typeof(CurrentBinding).GetField(nameof(CurrentBinding.FileName))!.FieldType);
		Assert.Equal(typeof(STRPTR), typeof(CurrentBinding).GetField(nameof(CurrentBinding.ProductString))!.FieldType);
	}


	[Fact]
	public void EveryPublicAbiStructureMatchesItsSizeConstant()
	{
		var assembly = typeof(APTR).Assembly;
		var structures = assembly.GetTypes()
			.Where(type => type.Namespace == "Amiga" && type.IsValueType && !type.IsEnum)
			.Where(type => type.GetField("Size") is { IsLiteral: true, IsStatic: true })
			.OrderBy(type => type.FullName)
			.ToArray();

		Assert.NotEmpty(structures);
		var failures = new List<string>();
		foreach (var type in structures)
		{
			var layout = type.StructLayoutAttribute;
			if (layout?.Pack != 2)
			{
				failures.Add($"{type.FullName}: expected Pack=2, actual Pack={layout?.Pack}");
			}
			var declaredSize = Convert.ToUInt32(
				type.GetField("Size")!.GetRawConstantValue(),
				System.Globalization.CultureInfo.InvariantCulture);
			var actualSize = (uint)Marshal.SizeOf(type);
			if (declaredSize != actualSize)
			{
				failures.Add($"{type.FullName}: declared Size={declaredSize}, Marshal.SizeOf={actualSize}");
			}
		}

		Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
	}

}
