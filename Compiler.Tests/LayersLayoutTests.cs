using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class LayersLayoutTests
{
	private sealed record ParameterSpec(Type Type, M68kRegister Register);

	private sealed record VectorSpec(
		string Name,
		short Lvo,
		ushort MinimumVersion,
		Type ReturnType,
		ParameterSpec[] Parameters);

	private static readonly VectorSpec[] VectorSpecs =
	{
		V(nameof(Layers.InitLayers), LayersLvo.InitLayers, 40, typeof(void), P<APTR>(M68kRegister.A0)),
		Create(nameof(Layers.CreateUpfrontLayer), LayersLvo.CreateUpfrontLayer, 40, false),
		Create(nameof(Layers.CreateBehindLayer), LayersLvo.CreateBehindLayer, 40, false),
		V(nameof(Layers.UpfrontLayer), LayersLvo.UpfrontLayer, 40, typeof(int), P<int>(M68kRegister.A0), P<APTR>(M68kRegister.A1)),
		V(nameof(Layers.BehindLayer), LayersLvo.BehindLayer, 40, typeof(int), P<int>(M68kRegister.A0), P<APTR>(M68kRegister.A1)),
		DummyLayerDelta(nameof(Layers.MoveLayer), LayersLvo.MoveLayer, typeof(int)),
		DummyLayerDelta(nameof(Layers.SizeLayer), LayersLvo.SizeLayer, typeof(int)),
		DummyLayerDelta(nameof(Layers.ScrollLayer), LayersLvo.ScrollLayer, typeof(void)),
		V(nameof(Layers.BeginUpdate), LayersLvo.BeginUpdate, 40, typeof(int), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.EndUpdate), LayersLvo.EndUpdate, 40, typeof(void), P<APTR>(M68kRegister.A0), P<uint>(M68kRegister.D0)),
		V(nameof(Layers.DeleteLayer), LayersLvo.DeleteLayer, 40, typeof(int), P<int>(M68kRegister.A0), P<APTR>(M68kRegister.A1)),
		V(nameof(Layers.LockLayer), LayersLvo.LockLayer, 40, typeof(void), P<int>(M68kRegister.A0), P<APTR>(M68kRegister.A1)),
		V(nameof(Layers.UnlockLayer), LayersLvo.UnlockLayer, 40, typeof(void), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.LockLayers), LayersLvo.LockLayers, 40, typeof(void), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.UnlockLayers), LayersLvo.UnlockLayers, 40, typeof(void), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.LockLayerInfo), LayersLvo.LockLayerInfo, 40, typeof(void), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.SwapBitsRastPortClipRect), LayersLvo.SwapBitsRastPortClipRect, 40, typeof(void), P<APTR>(M68kRegister.A0), P<APTR>(M68kRegister.A1)),
		V(nameof(Layers.WhichLayer), LayersLvo.WhichLayer, 40, typeof(APTR), P<APTR>(M68kRegister.A0), P<int>(M68kRegister.D0), P<int>(M68kRegister.D1)),
		V(nameof(Layers.UnlockLayerInfo), LayersLvo.UnlockLayerInfo, 40, typeof(void), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.NewLayerInfo), LayersLvo.NewLayerInfo, 40, typeof(APTR)),
		V(nameof(Layers.DisposeLayerInfo), LayersLvo.DisposeLayerInfo, 40, typeof(void), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.FattenLayerInfo), LayersLvo.FattenLayerInfo, 40, typeof(int), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.ThinLayerInfo), LayersLvo.ThinLayerInfo, 40, typeof(void), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.MoveLayerInFrontOf), LayersLvo.MoveLayerInFrontOf, 40, typeof(int), P<APTR>(M68kRegister.A0), P<APTR>(M68kRegister.A1)),
		V(nameof(Layers.InstallClipRegion), LayersLvo.InstallClipRegion, 40, typeof(APTR), P<APTR>(M68kRegister.A0), P<APTR>(M68kRegister.A1)),
		LayerMoveSize(nameof(Layers.MoveSizeLayer), LayersLvo.MoveSizeLayer),
		Create(nameof(Layers.CreateUpfrontHookLayer), LayersLvo.CreateUpfrontHookLayer, 40, true),
		Create(nameof(Layers.CreateBehindHookLayer), LayersLvo.CreateBehindHookLayer, 40, true),
		V(nameof(Layers.InstallLayerHook), LayersLvo.InstallLayerHook, 40, typeof(APTR), P<APTR>(M68kRegister.A0), P<APTR>(M68kRegister.A1)),
		V(nameof(Layers.InstallLayerInfoHook), LayersLvo.InstallLayerInfoHook, 40, typeof(APTR), P<APTR>(M68kRegister.A0), P<APTR>(M68kRegister.A1)),
		V(nameof(Layers.SortLayerCR), LayersLvo.SortLayerCR, 40, typeof(void), P<APTR>(M68kRegister.A0), P<int>(M68kRegister.D0), P<int>(M68kRegister.D1)),
		V(nameof(Layers.DoHookClipRects), LayersLvo.DoHookClipRects, 40, typeof(void), P<APTR>(M68kRegister.A0), P<APTR>(M68kRegister.A1), P<APTR>(M68kRegister.A2)),
		Create(nameof(Layers.CreateUpfrontLayerTagList), LayersLvo.CreateUpfrontLayerTagList, 50, false),
		Create(nameof(Layers.CreateBehindLayerTagList), LayersLvo.CreateBehindLayerTagList, 50, false),
		V(nameof(Layers.WhichLayerBehindLayer), LayersLvo.WhichLayerBehindLayer, 52, typeof(APTR), P<APTR>(M68kRegister.A0), P<int>(M68kRegister.D0), P<int>(M68kRegister.D1)),
		V(nameof(Layers.IsLayerVisible), LayersLvo.IsLayerVisible, 52, typeof(int), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.RenderLayerInfoTagList), LayersLvo.RenderLayerInfoTagList, 52, typeof(int), P<APTR>(M68kRegister.A0), P<APTR>(M68kRegister.A1)),
		V(nameof(Layers.LockLayerUpdates), LayersLvo.LockLayerUpdates, 52, typeof(void), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.UnlockLayerUpdates), LayersLvo.UnlockLayerUpdates, 52, typeof(void), P<APTR>(M68kRegister.A0)),
		V(nameof(Layers.IsVisibleInLayer), LayersLvo.IsVisibleInLayer, 52, typeof(int), P<APTR>(M68kRegister.A0), P<int>(M68kRegister.D0), P<int>(M68kRegister.D1), P<int>(M68kRegister.D2), P<int>(M68kRegister.D3)),
		V(nameof(Layers.IsLayerHitable), LayersLvo.IsLayerHitable, 52, typeof(int), P<APTR>(M68kRegister.A0)),
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallIsVisibleInLayer() =>
		Layers.IsVisibleInLayer((APTR)0x0000_2000u, -1, 2, 31, 42);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallCreateUpfrontLayer() =>
		Layers.CreateUpfrontLayer(
			(APTR)0x0000_2000u,
			(APTR)0x0000_3000u,
			-2,
			3,
			319,
			255,
			LayerCreationFlags.Smart | LayerCreationFlags.Backdrop,
			APTR.Null);

	[Theory]
	[InlineData(typeof(Amiga.Layer), 160u)]
	[InlineData(typeof(ClipRect), 36u)]
	[InlineData(typeof(LayerInfo), 102u)]
	[InlineData(typeof(NewLayerHook), 28u)]
	[InlineData(typeof(LayerBackfillMessage), 20u)]
	[InlineData(typeof(LayerInfoBackfillMessage), 12u)]
	[InlineData(typeof(RastPort), 100u)]
	[InlineData(typeof(BitMap), 40u)]
	[InlineData(typeof(Rectangle), 8u)]
	[InlineData(typeof(RegionRectangle), 16u)]
	[InlineData(typeof(Region), 12u)]
	public void PublicStructureSizesMatchPublishedPack2Abi(Type type, uint size)
	{
		Assert.Equal((int)size, Marshal.SizeOf(type));
		Assert.Equal(size, (uint)type.GetField("Size")!.GetRawConstantValue()!);
		Assert.Equal(2, type.StructLayoutAttribute!.Pack);
	}

	[Theory]
	[InlineData(typeof(Amiga.Layer), typeof(LayersLayout.Layer))]
	[InlineData(typeof(ClipRect), typeof(LayersLayout.ClipRect))]
	[InlineData(typeof(LayerInfo), typeof(LayersLayout.LayerInfo))]
	[InlineData(typeof(NewLayerHook), typeof(LayersLayout.NewLayerHook))]
	[InlineData(typeof(LayerBackfillMessage), typeof(LayersLayout.LayerBackfillMessage))]
	[InlineData(typeof(LayerInfoBackfillMessage), typeof(LayersLayout.LayerInfoBackfillMessage))]
	[InlineData(typeof(BitMap), typeof(GraphicsLayout.BitMap))]
	[InlineData(typeof(Rectangle), typeof(GraphicsLayout.Rectangle))]
	[InlineData(typeof(RegionRectangle), typeof(GraphicsLayout.RegionRectangle))]
	[InlineData(typeof(Region), typeof(GraphicsLayout.Region))]
	public void PublicLayoutConstantsCoverEveryPublishedField(Type structureType, Type layoutType)
	{
		var fields = structureType.GetFields(BindingFlags.Public | BindingFlags.Instance);
		var constants = layoutType.GetFields(BindingFlags.Public | BindingFlags.Static)
			.ToDictionary(field => field.Name);

		Assert.Equal(fields.Length + 1, constants.Count);
		Assert.Equal(Marshal.SizeOf(structureType), (int)constants["Size"].GetRawConstantValue()!);
		foreach (var field in fields)
		{
			Assert.True(constants.TryGetValue(field.Name, out var constant),
				$"Missing {layoutType.FullName}.{field.Name}");
			Assert.True(constant!.IsLiteral);
			Assert.Equal(
				Marshal.OffsetOf(structureType, field.Name).ToInt32(),
				(int)constant.GetRawConstantValue()!);
		}
	}

	[Fact]
	public void PublishedFieldOffsetsRemainExact()
	{
		AssertOffsets<Amiga.Layer>(
			(nameof(Amiga.Layer.Front), 0), (nameof(Amiga.Layer.Back), 4),
			(nameof(Amiga.Layer.ClipRect), 8), (nameof(Amiga.Layer.RastPort), 12),
			(nameof(Amiga.Layer.Bounds), 16), (nameof(Amiga.Layer.Reserved), 24),
			(nameof(Amiga.Layer.Priority), 28), (nameof(Amiga.Layer.Flags), 30),
			(nameof(Amiga.Layer.SuperBitMap), 32), (nameof(Amiga.Layer.SuperClipRect), 36),
			(nameof(Amiga.Layer.Window), 40), (nameof(Amiga.Layer.ScrollX), 44),
			(nameof(Amiga.Layer.ScrollY), 46), (nameof(Amiga.Layer.ClipRectWork), 48),
			(nameof(Amiga.Layer.ClipRectWork2), 52), (nameof(Amiga.Layer.NewClipRect), 56),
			(nameof(Amiga.Layer.SuperSaveClipRects), 60), (nameof(Amiga.Layer.ClipRects), 64),
			(nameof(Amiga.Layer.LayerInfo), 68), (nameof(Amiga.Layer.Lock), 72),
			(nameof(Amiga.Layer.BackFill), 118), (nameof(Amiga.Layer.Reserved1), 122),
			(nameof(Amiga.Layer.ClipRegion), 126), (nameof(Amiga.Layer.SaveClipRects), 130),
			(nameof(Amiga.Layer.Width), 134), (nameof(Amiga.Layer.Height), 136),
			(nameof(Amiga.Layer.Reserved2), 138), (nameof(Amiga.Layer.DamageList), 156));
		AssertOffsets<ClipRect>(
			(nameof(ClipRect.Next), 0), (nameof(ClipRect.Previous), 4),
			(nameof(ClipRect.ObscuringLayer), 8), (nameof(ClipRect.BitMap), 12),
			(nameof(ClipRect.Bounds), 16), (nameof(ClipRect.ReservedPointer1), 24),
			(nameof(ClipRect.ReservedPointer2), 28), (nameof(ClipRect.Reserved), 32));
		AssertOffsets<LayerInfo>(
			(nameof(LayerInfo.TopLayer), 0), (nameof(LayerInfo.CheckLayer), 4),
			(nameof(LayerInfo.Obscured), 8), (nameof(LayerInfo.FreeClipRects), 12),
			(nameof(LayerInfo.PrivateReserve1), 16), (nameof(LayerInfo.PrivateReserve2), 20),
			(nameof(LayerInfo.Lock), 24), (nameof(LayerInfo.GraphicsSemaphoreHead), 70),
			(nameof(LayerInfo.PrivateReserve3), 82), (nameof(LayerInfo.PrivateReserve4), 84),
			(nameof(LayerInfo.Flags), 88), (nameof(LayerInfo.FattenCount), 90),
			(nameof(LayerInfo.LockLayersCount), 91), (nameof(LayerInfo.PrivateReserve5), 92),
			(nameof(LayerInfo.BlankHook), 94), (nameof(LayerInfo.Extra), 98));
		AssertOffsets<NewLayerHook>(
			(nameof(NewLayerHook.MinNode), 0), (nameof(NewLayerHook.Entry), 8),
			(nameof(NewLayerHook.SubEntry), 12), (nameof(NewLayerHook.Data), 16),
			(nameof(NewLayerHook.TransparentRegionHook), 20),
			(nameof(NewLayerHook.TransparentRegion), 24));
		AssertOffsets<LayerBackfillMessage>(
			(nameof(LayerBackfillMessage.Layer), 0),
			(nameof(LayerBackfillMessage.Bounds), 4),
			(nameof(LayerBackfillMessage.OffsetX), 12),
			(nameof(LayerBackfillMessage.OffsetY), 16));
		AssertOffsets<LayerInfoBackfillMessage>(
			(nameof(LayerInfoBackfillMessage.Undefined), 0),
			(nameof(LayerInfoBackfillMessage.Bounds), 4));
		AssertOffsets<BitMap>(
			(nameof(BitMap.BytesPerRow), 0), (nameof(BitMap.Rows), 2),
			(nameof(BitMap.Flags), 4), (nameof(BitMap.Depth), 5),
			(nameof(BitMap.Plane0), 8), (nameof(BitMap.Plane1), 12),
			(nameof(BitMap.Plane2), 16), (nameof(BitMap.Plane3), 20),
			(nameof(BitMap.Plane4), 24), (nameof(BitMap.Plane5), 28),
			(nameof(BitMap.Plane6), 32), (nameof(BitMap.Plane7), 36));
		AssertOffsets<Rectangle>(
			(nameof(Rectangle.MinX), 0), (nameof(Rectangle.MinY), 2),
			(nameof(Rectangle.MaxX), 4), (nameof(Rectangle.MaxY), 6));
		AssertOffsets<RegionRectangle>(
			(nameof(RegionRectangle.Successor), 0),
			(nameof(RegionRectangle.Predecessor), 4),
			(nameof(RegionRectangle.Bounds), 8));
		AssertOffsets<Region>(
			(nameof(Region.Bounds), 0), (nameof(Region.RegionRectangle), 8));
	}

	[Fact]
	public void RastPortLayerTraversalLayoutRemainsClassicAbi()
	{
		Assert.Equal(100, GraphicsLayout.RastPort.Size);
		Assert.Equal(100, Marshal.SizeOf<RastPort>());
		Assert.Equal(2, typeof(RastPort).StructLayoutAttribute!.Pack);
		Assert.Equal(0, GraphicsLayout.RastPort.Layer);
		Assert.Equal(
			GraphicsLayout.RastPort.Layer,
			Marshal.OffsetOf<RastPort>(nameof(RastPort.Layer)).ToInt32());
		Assert.Equal(4, GraphicsLayout.RastPort.BitMap);
		Assert.Equal(
			GraphicsLayout.RastPort.BitMap,
			Marshal.OffsetOf<RastPort>(nameof(RastPort.BitMap)).ToInt32());
		AssertField<RastPort>(nameof(RastPort.Layer), typeof(APTR));
		AssertField<RastPort>(nameof(RastPort.BitMap), typeof(APTR));
	}

	[Fact]
	public void PublicFieldSignednessAndTypesMatchHeaders()
	{
		AssertField<Amiga.Layer>(nameof(Amiga.Layer.Priority), typeof(ushort));
		AssertField<Amiga.Layer>(nameof(Amiga.Layer.Flags), typeof(LayerFlags));
		AssertField<Amiga.Layer>(nameof(Amiga.Layer.ScrollX), typeof(short));
		AssertField<Amiga.Layer>(nameof(Amiga.Layer.ScrollY), typeof(short));
		AssertField<Amiga.Layer>(nameof(Amiga.Layer.Reserved1), typeof(uint));
		AssertField<Amiga.Layer>(nameof(Amiga.Layer.Width), typeof(short));
		AssertField<Amiga.Layer>(nameof(Amiga.Layer.Height), typeof(short));
		AssertField<ClipRect>(nameof(ClipRect.Reserved), typeof(int));
		AssertField<LayerInfo>(nameof(LayerInfo.PrivateReserve1), typeof(int));
		AssertField<LayerInfo>(nameof(LayerInfo.PrivateReserve2), typeof(int));
		AssertField<LayerInfo>(nameof(LayerInfo.PrivateReserve3), typeof(short));
		AssertField<LayerInfo>(nameof(LayerInfo.Flags), typeof(LayerInfoFlags));
		AssertField<LayerInfo>(nameof(LayerInfo.FattenCount), typeof(sbyte));
		AssertField<LayerInfo>(nameof(LayerInfo.LockLayersCount), typeof(sbyte));
		AssertField<LayerInfo>(nameof(LayerInfo.PrivateReserve5), typeof(short));
		AssertField<LayerBackfillMessage>(nameof(LayerBackfillMessage.OffsetX), typeof(int));
		AssertField<LayerBackfillMessage>(nameof(LayerBackfillMessage.OffsetY), typeof(int));
		AssertField<LayerInfoBackfillMessage>(nameof(LayerInfoBackfillMessage.Undefined), typeof(uint));
		AssertField<Rectangle>(nameof(Rectangle.MinX), typeof(short));
		AssertField<Rectangle>(nameof(Rectangle.MinY), typeof(short));
		AssertField<Rectangle>(nameof(Rectangle.MaxX), typeof(short));
		AssertField<Rectangle>(nameof(Rectangle.MaxY), typeof(short));
		AssertField<RegionRectangle>(nameof(RegionRectangle.Successor), typeof(APTR));
		AssertField<RegionRectangle>(nameof(RegionRectangle.Predecessor), typeof(APTR));
		AssertField<Region>(nameof(Region.RegionRectangle), typeof(APTR));
		Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(LayerFlags)));
		Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(LayerInfoFlags)));
		Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(LayerCreationFlags)));
		Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(ClipRectFlags)));
		Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(ClipRectPositionFlags)));
		Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(LayerCreationTag)));
		Assert.Equal(typeof(uint), Enum.GetUnderlyingType(typeof(LayerRenderTag)));
	}

	[Fact]
	public void ConditionalMorphOsClipRectTailIsExplicitButNotInDefaultProfile()
	{
		Assert.Equal(36, LayersLayout.ExtendedClipRect.PublicPrefixSize);
		Assert.Equal(36, LayersLayout.ExtendedClipRect.Flags);
		Assert.Equal(40, LayersLayout.ExtendedClipRect.Size);
		Assert.Null(typeof(ClipRect).GetField("Flags", BindingFlags.Public | BindingFlags.Instance));
	}

	[Fact]
	public void ClassicAndMorphOsConstantsMatchPublishedValues()
	{
		Assert.Equal(0x0000, (int)LayerCreationFlags.None);
		Assert.Equal(0x0001, (int)LayerCreationFlags.Simple);
		Assert.Equal(0x0002, (int)LayerCreationFlags.Smart);
		Assert.Equal(0x0004, (int)LayerCreationFlags.Super);
		Assert.Equal(0x0040, (int)LayerCreationFlags.Backdrop);
		Assert.Equal(0x0000, (ushort)LayerFlags.None);
		Assert.Equal(0x0001, (ushort)LayerFlags.Simple);
		Assert.Equal(0x0002, (ushort)LayerFlags.Smart);
		Assert.Equal(0x0004, (ushort)LayerFlags.Super);
		Assert.Equal(0x0010, (ushort)LayerFlags.Updating);
		Assert.Equal(0x0040, (ushort)LayerFlags.Backdrop);
		Assert.Equal(0x0080, (ushort)LayerFlags.Refresh);
		Assert.Equal(0x0100, (ushort)LayerFlags.ClipRectsLost);
		Assert.Equal(0x0200, (ushort)LayerFlags.InternalRefresh);
		Assert.Equal(0x0400, (ushort)LayerFlags.InternalRefresh2);
		Assert.Equal(0x0000, (ushort)LayerInfoFlags.None);
		Assert.Equal(0x0001, (ushort)LayerInfoFlags.NewLayerInfoCalled);
		Assert.Equal(0x0000, (int)ClipRectFlags.None);
		Assert.Equal(0x0001, (int)ClipRectFlags.NeedsNoConcealedRasters);
		Assert.Equal(0x0002, (int)ClipRectFlags.NeedsNoLayerBlitDamage);
		Assert.Equal(0x0000, (int)ClipRectPositionFlags.None);
		Assert.Equal(0x0001, (int)ClipRectPositionFlags.LessX);
		Assert.Equal(0x0002, (int)ClipRectPositionFlags.LessY);
		Assert.Equal(0x0004, (int)ClipRectPositionFlags.GreaterX);
		Assert.Equal(0x0008, (int)ClipRectPositionFlags.GreaterY);

		Assert.Equal(0u, LayerBackfillHook.Backfill);
		Assert.Equal(1u, LayerBackfillHook.NoBackfill);
		Assert.Equal(2u, LayerBackfillHook.NeverBackfill);
		Assert.Equal(0x8000_0400u, (uint)LayerCreationTag.Dummy);
		Assert.Equal(0x8000_0401u, (uint)LayerCreationTag.BackfillHook);
		Assert.Equal(0x8000_0402u, (uint)LayerCreationTag.TransparentRegion);
		Assert.Equal(0x8000_0403u, (uint)LayerCreationTag.TransparentHook);
		Assert.Equal(0x8000_0404u, (uint)LayerCreationTag.WindowPointer);
		Assert.Equal(0x8000_0405u, (uint)LayerCreationTag.SuperBitMap);
		Assert.Equal(0x8000_047Eu, (uint)LayerRenderTag.Dummy);
		Assert.Equal(0x8000_047Fu, (uint)LayerRenderTag.DestinationRastPort);
		Assert.Equal(0x8000_0480u, (uint)LayerRenderTag.DestinationBitMap);
		Assert.Equal(0x8000_0481u, (uint)LayerRenderTag.DestinationBounds);
		Assert.Equal(0x8000_0482u, (uint)LayerRenderTag.LayerInfoBounds);
		Assert.Equal(0x8000_0483u, (uint)LayerRenderTag.Erase);
		Assert.Equal(0x8000_0484u, (uint)LayerRenderTag.RenderList);
		Assert.Equal(0x8000_0485u, (uint)LayerRenderTag.IgnoreList);
		Assert.Equal(0x8000_0486u, (uint)LayerRenderTag.ApplyOpacityMultiplier);
		Assert.Equal((ushort)40, LayersAbiConstants.ClassicV40);
		Assert.Equal((ushort)50, LayersAbiConstants.MorphOsV50);
		Assert.Equal((ushort)52, LayersAbiConstants.MorphOsV52);
		Assert.Equal((ushort)52, LayersAbiConstants.NeverBackfillMinimumVersion);
		Assert.Equal((ushort)22, LayersAbiConstants.NeverBackfillRevision);
		Assert.Equal(0x4359_4252u, LayersAbiConstants.NewLayerHookDataMagic);
	}

	[Fact]
	public void EveryVectorHasExactLvoSignatureRegistersAndVersion()
	{
		var methods = typeof(Layers).GetMethods(
			BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
			.Where(method => method.GetCustomAttribute<AmigaLvoAttribute>() is not null)
			.ToDictionary(method => method.Name);

		Assert.Equal(LayersAbiConstants.ClassicVectorCount +
			LayersAbiConstants.MorphOsM68kExtensionCount, VectorSpecs.Length);
		Assert.Equal(VectorSpecs.Length, methods.Count);

		foreach (var spec in VectorSpecs)
		{
			Assert.True(methods.TryGetValue(spec.Name, out var method), spec.Name);
			Assert.Equal(spec.Lvo, method!.GetCustomAttribute<AmigaLvoAttribute>()!.Offset);
			Assert.Equal(spec.ReturnType, method.ReturnType);

			var lvo = typeof(LayersLvo).GetField(spec.Name, BindingFlags.Public | BindingFlags.Static);
			Assert.NotNull(lvo);
			Assert.True(lvo!.IsLiteral);
			Assert.Equal(spec.Lvo, (short)lvo.GetRawConstantValue()!);

			var parameters = method.GetParameters();
			Assert.Equal(spec.Parameters.Length, parameters.Length);
			for (var i = 0; i < parameters.Length; i++)
			{
				Assert.Equal(spec.Parameters[i].Type, parameters[i].ParameterType);
				Assert.Equal(spec.Parameters[i].Register,
					parameters[i].GetCustomAttribute<M68kRegisterAttribute>()?.Register);
			}

			if (spec.ReturnType == typeof(void))
			{
				Assert.Null(method.ReturnParameter.GetCustomAttribute<M68kRegisterAttribute>());
			}
			else
			{
				Assert.Equal(M68kRegister.D0,
					method.ReturnParameter.GetCustomAttribute<M68kRegisterAttribute>()?.Register);
			}

			if (spec.MinimumVersion > LayersAbiConstants.ClassicV40)
			{
				var version = typeof(LayersVectorVersion).GetField(spec.Name,
					BindingFlags.Public | BindingFlags.Static);
				Assert.NotNull(version);
				Assert.Equal(spec.MinimumVersion, (ushort)version!.GetRawConstantValue()!);
			}
		}
	}

	[Fact]
	public void ClassicProfileIsContiguousAndMorphOsReservedGapsStayUnclaimed()
	{
		var classic = VectorSpecs.Where(spec => spec.MinimumVersion == 40)
			.Select(spec => (int)spec.Lvo).OrderDescending().ToArray();
		var extensions = VectorSpecs.Where(spec => spec.MinimumVersion > 40)
			.Select(spec => (int)spec.Lvo).OrderDescending().ToArray();

		Assert.Equal(Enumerable.Range(0, 32).Select(index => -30 - index * 6), classic);
		Assert.Equal(new[] { -234, -240, -252, -258, -282, -288, -294, -300, -306 }, extensions);
		Assert.DoesNotContain(VectorSpecs, spec => spec.Lvo is -222 or -228 or -246 or -264 or -270 or -276);
	}

	[Fact]
	public void PublishedFunctionNamesMapToExactClassicAndMorphOsSlots()
	{
		string[] classicNames =
		{
			"InitLayers", "CreateUpfrontLayer", "CreateBehindLayer", "UpfrontLayer",
			"BehindLayer", "MoveLayer", "SizeLayer", "ScrollLayer", "BeginUpdate",
			"EndUpdate", "DeleteLayer", "LockLayer", "UnlockLayer", "LockLayers",
			"UnlockLayers", "LockLayerInfo", "SwapBitsRastPortClipRect", "WhichLayer",
			"UnlockLayerInfo", "NewLayerInfo", "DisposeLayerInfo", "FattenLayerInfo",
			"ThinLayerInfo", "MoveLayerInFrontOf", "InstallClipRegion", "MoveSizeLayer",
			"CreateUpfrontHookLayer", "CreateBehindHookLayer", "InstallLayerHook",
			"InstallLayerInfoHook", "SortLayerCR", "DoHookClipRects",
		};

		Assert.Equal(32, classicNames.Length);
		for (var index = 0; index < classicNames.Length; index++)
		{
			var spec = Assert.Single(VectorSpecs, spec => spec.Name == classicNames[index]);
			Assert.Equal(-30 - index * 6, spec.Lvo);
		}

		var morphOs = new Dictionary<string, short>
		{
			[nameof(Layers.CreateUpfrontLayerTagList)] = -234,
			[nameof(Layers.CreateBehindLayerTagList)] = -240,
			[nameof(Layers.WhichLayerBehindLayer)] = -252,
			[nameof(Layers.IsLayerVisible)] = -258,
			[nameof(Layers.RenderLayerInfoTagList)] = -282,
			[nameof(Layers.LockLayerUpdates)] = -288,
			[nameof(Layers.UnlockLayerUpdates)] = -294,
			[nameof(Layers.IsVisibleInLayer)] = -300,
			[nameof(Layers.IsLayerHitable)] = -306,
		};

		foreach (var (name, lvo) in morphOs)
		{
			Assert.Equal(lvo, Assert.Single(VectorSpecs, spec => spec.Name == name).Lvo);
		}
	}

	[Fact]
	public void OfficialMorphOsPpcWrappersMapOneToOneToM68kManifest()
	{
		string[] officialPpcWrappers =
		{
			"WhichLayer", "CreateBehindLayerTagList", "RenderLayerInfoTagList",
			"CreateBehindHookLayer", "UpfrontLayer", "SizeLayer", "WhichLayerBehindLayer",
			"NewLayerInfo", "IsVisibleInLayer", "UnlockLayerUpdates", "FattenLayerInfo",
			"SwapBitsRastPortClipRect", "DoHookClipRects", "UnlockLayers", "UnlockLayer",
			"MoveSizeLayer", "LockLayers", "CreateUpfrontLayer", "LockLayer", "BeginUpdate",
			"LockLayerUpdates", "EndUpdate", "InitLayers", "SortLayerCR",
			"CreateUpfrontLayerTagList", "DeleteLayer", "MoveLayer", "IsLayerVisible",
			"LockLayerInfo", "IsLayerHitable", "InstallClipRegion", "DisposeLayerInfo",
			"ScrollLayer", "InstallLayerInfoHook", "UnlockLayerInfo", "MoveLayerInFrontOf",
			"CreateBehindLayer", "BehindLayer", "InstallLayerHook", "CreateUpfrontHookLayer",
			"ThinLayerInfo",
		};

		Assert.Equal(41, officialPpcWrappers.Length);
		Assert.Equal(
			officialPpcWrappers.Order(StringComparer.Ordinal),
			VectorSpecs.Select(spec => spec.Name).Order(StringComparer.Ordinal));
		var wrapper = typeof(Layers).GetMethod(nameof(Layers.RenderLayerInfoTags));
		Assert.NotNull(wrapper);
		Assert.Null(wrapper!.GetCustomAttribute<AmigaLvoAttribute>());
		Assert.Equal(typeof(int), wrapper.ReturnType);
		Assert.Equal(new[] { typeof(APTR), typeof(APTR) },
			wrapper.GetParameters().Select(parameter => parameter.ParameterType));
	}

	[Fact]
	public void LayersLibraryMetadataAndBaseRemainExplicit()
	{
		var library = typeof(Layers).GetCustomAttribute<AmigaLibraryAttribute>();
		Assert.NotNull(library);
		Assert.Equal("layers.library", library!.Name);
		Assert.Equal(AmigaLibraryBasePolicy.Manual, library.BasePolicy);
		Assert.Equal("_LayersLibraryBase", AmigaLibraryBaseSymbols.For(Layers.Name));
	}

	[Fact]
	public void MorphOsTypedFrameLowersThroughPublishedVector()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(LayersLayoutTests).FullName}::{nameof(CallIsVisibleInLayer)}",
			OutputFormat = M68kOutputFormat.Assembly,
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				[Layers.Name] = 0x0000_4200,
			},
		});

		Assert.Contains("_LayersLibraryBase", result.Text, StringComparison.Ordinal);
		Assert.Contains("-300(a6)", result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ClassicTypedCreationFrameLowersThroughPublishedVector()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(LayersLayoutTests).FullName}::{nameof(CallCreateUpfrontLayer)}",
			OutputFormat = M68kOutputFormat.Assembly,
		}, new AmigaCompilationOptions
		{
			LibraryBases = new Dictionary<string, uint>
			{
				[Layers.Name] = 0x0000_4200,
			},
		});

		Assert.Contains("_LayersLibraryBase", result.Text, StringComparison.Ordinal);
		Assert.Contains("-36(a6)", result.Text, StringComparison.Ordinal);
	}

	private static VectorSpec Create(string name, short lvo, ushort version, bool hook)
	{
		var tail = hook
			? new[] { P<APTR>(M68kRegister.A3), P<APTR>(M68kRegister.A2) }
			: new[] { P<APTR>(M68kRegister.A2) };
		return V(name, lvo, version, typeof(APTR),
			new[]
			{
				P<APTR>(M68kRegister.A0), P<APTR>(M68kRegister.A1),
				P<int>(M68kRegister.D0), P<int>(M68kRegister.D1),
				P<int>(M68kRegister.D2), P<int>(M68kRegister.D3),
				P<LayerCreationFlags>(M68kRegister.D4),
			}.Concat(tail).ToArray());
	}

	private static VectorSpec DummyLayerDelta(string name, short lvo, Type returnType) =>
		V(name, lvo, 40, returnType,
			P<int>(M68kRegister.A0), P<APTR>(M68kRegister.A1),
			P<int>(M68kRegister.D0), P<int>(M68kRegister.D1));

	private static VectorSpec LayerMoveSize(string name, short lvo) =>
		V(name, lvo, 40, typeof(int), P<APTR>(M68kRegister.A0),
			P<int>(M68kRegister.D0), P<int>(M68kRegister.D1),
			P<int>(M68kRegister.D2), P<int>(M68kRegister.D3));

	private static VectorSpec V(
		string name,
		short lvo,
		ushort version,
		Type returnType,
		params ParameterSpec[] parameters) =>
		new(name, lvo, version, returnType, parameters);

	private static ParameterSpec P<T>(M68kRegister register) => new(typeof(T), register);

	private static void AssertField<T>(string name, Type type) =>
		Assert.Equal(type, typeof(T).GetField(name)!.FieldType);

	private static void AssertOffsets<T>(params (string Name, int Offset)[] expected)
	{
		foreach (var (name, offset) in expected)
		{
			Assert.Equal(offset, Marshal.OffsetOf<T>(name).ToInt32());
		}
	}
}
