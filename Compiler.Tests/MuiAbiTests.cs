using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;
using Amiga.MUI;

namespace CopperSharp.Compiler.Tests;

public sealed class MuiAbiTests
{
	[Fact]
	public void CoreStructuresUsePublishedM68kSizesAndOffsets()
	{
		Size<MUI_Command>(36);
		Offset<MUI_Command>(nameof(MUI_Command.mc_Hook), 12);
		Size<MUI_Palette_Entry>(20);
		Size<MUI_InputHandlerValue>(4);
		Size<MUI_InputHandlerNode>(24);
		Offset<MUI_InputHandlerNode>(nameof(MUI_InputHandlerNode.ihn_Object), 8);
		Offset<MUI_InputHandlerNode>(nameof(MUI_InputHandlerNode.ihn_Value), 12);
		Offset<MUI_InputHandlerNode>(nameof(MUI_InputHandlerNode.ihn_Method), 20);
		Size<MUI_EventHandlerNode>(24);
		Offset<MUI_EventHandlerNode>(nameof(MUI_EventHandlerNode.ehn_Priority), 9);
		Offset<MUI_EventHandlerNode>(nameof(MUI_EventHandlerNode.ehn_Object), 12);
		Size<MUI_List_TestPos_Result>(12);
		Size<MUI_RGBColor>(12);
		Size<MUI_GlobalInfo>(8);
		Size<MUI_NotifyData>(28);
		Size<MUI_MinMax>(12);
		Size<MUI_LayoutDimensions>(16);
		Size<MUI_LayoutMsg>(36);
		Offset<MUI_LayoutMsg>(nameof(MUI_LayoutMsg.lm_MinMax), 8);
		Offset<MUI_LayoutMsg>(nameof(MUI_LayoutMsg.lm_Layout), 20);
		Size<MUI_AreaData>(40);
		Offset<MUI_AreaData>(nameof(MUI_AreaData.mad_Box), 24);
		Offset<MUI_AreaData>(nameof(MUI_AreaData.mad_Flags), 36);
		Size<MUI_DragImage>(20);
		Size<MUI_RenderInfo>(28);
		Size<MUI_PenSpec>(32);
		Size<MUI_CustomClass>(28);
		Offset<MUI_CustomClass>(nameof(MUI_CustomClass.mcc_Class), 24);
	}

	[Fact]
	public void GlobalLimitAndUnionSignednessAreExact()
	{
		Assert.Equal(10000, MUIConstants.MUI_MAXMAX);
		var timer = new MUI_InputHandlerValue { ihn_Millis = 0x1234, ihn_Current = 0x5678 };
		Assert.Equal(0x1234_5678u, timer.ihn_Signals);
		var result = new MUI_List_TestPos_Result { entry = -1, column = -2, xoffset = -3, yoffset = -4 };
		Assert.Equal(-1, result.entry);
		Assert.Equal((short)-2, result.column);
	}

	[Fact]
	public void MorphOs320ProfileAdmitsOnlyItsQualifiedVersion()
	{
		Assert.Equal("MorphOs320M68k", MUIProfile.Name);
		Assert.Equal((ushort)20, MUIProfile.MinimumVersion);
		Assert.Equal((ushort)20, MUIProfile.LatestVersion);
		Assert.False(MUIProfile.IsVersionAdmitted(19));
		Assert.True(MUIProfile.IsVersionAdmitted(20));
		Assert.False(MUIProfile.IsVersionAdmitted(21));
	}

	[Fact]
	public void BigEndianCodecsRoundTripFoundationalRecords()
	{
		var memory = new Memory(512);
		var minAddress = APTR.FromPointer(16);
		var minMax = new MUI_MinMax
		{
			MinWidth = -1, MinHeight = 2, MaxWidth = 10000,
			MaxHeight = 9999, DefWidth = 320, DefHeight = 200,
		};
		MUI_MinMaxCodec.Write(ref memory, minAddress, minMax);
		Assert.Equal((ushort)0xFFFF, memory.ReadUInt16(minAddress, 0));
		Assert.Equal((ushort)10000, memory.ReadUInt16(minAddress, 4));
		Assert.True(MUI_MinMaxCodec.TryRead(ref memory, minAddress, out var actualMin));
		Assert.Equal(minMax.MinWidth, actualMin.MinWidth);
		Assert.Equal(minMax.DefHeight, actualMin.DefHeight);

		var layoutAddress = APTR.FromPointer(64);
		var layout = new MUI_LayoutMsg
		{
			lm_Type = 2,
			lm_Children = APTR.FromPointer(0x1020_3040),
			lm_MinMax = minMax,
			lm_Layout = new MUI_LayoutDimensions
			{
				Width = -640, Height = 480,
				priv5 = 0xAABB_CCDD, priv6 = 0x1122_3344,
			},
		};
		MUI_LayoutMsgCodec.Write(ref memory, layoutAddress, layout);
		Assert.True(MUI_LayoutMsgCodec.TryRead(ref memory, layoutAddress, out var actualLayout));
		Assert.Equal(layout.lm_Children.Raw, actualLayout.lm_Children.Raw);
		Assert.Equal(-640, actualLayout.lm_Layout.Width);
		Assert.Equal(0xAABB_CCDDu, actualLayout.lm_Layout.priv5);

		var classAddress = APTR.FromPointer(128);
		var customClass = new MUI_CustomClass
		{
			mcc_UserData = 1, mcc_UtilityBase = 2, mcc_DOSBase = 3,
			mcc_GfxBase = 4, mcc_IntuitionBase = 5, mcc_Super = 6,
			mcc_Class = 7,
		};
		MUI_CustomClassCodec.Write(ref memory, classAddress, customClass);
		Assert.True(MUI_CustomClassCodec.TryRead(ref memory, classAddress, out var actualClass));
		Assert.Equal(7u, actualClass.mcc_Class.Raw);

		var renderAddress = APTR.FromPointer(192);
		var render = new MUI_RenderInfo
		{
			mri_WindowObject = 10, mri_Screen = 11, mri_DrawInfo = 12,
			mri_Pens = 13, mri_Window = 14, mri_RastPort = 15,
			mri_Flags = 0x8000_001Fu,
		};
		MUI_RenderInfoCodec.Write(ref memory, renderAddress, render);
		Assert.True(MUI_RenderInfoCodec.TryRead(ref memory, renderAddress, out var actualRender));
		Assert.Equal(render.mri_RastPort.Raw, actualRender.mri_RastPort.Raw);
		Assert.Equal(render.mri_Flags, actualRender.mri_Flags);
	}

	[Fact]
	public void GenericMessageFieldCodecCoversEveryGeneratedFieldWidth()
	{
		var memory = new Memory(64);
		var address = APTR.FromPointer(8);
		Assert.True(MUIMessageFieldCodec.TryWriteUInt32(ref memory, address, 0, 0x8123_4567));
		Assert.True(MUIMessageFieldCodec.TryWriteInt32(ref memory, address, 4, -123456));
		Assert.True(MUIMessageFieldCodec.TryWriteUInt16(ref memory, address, 8, 0xABCD));
		Assert.True(MUIMessageFieldCodec.TryWriteUInt8(ref memory, address, 10, 0xEF));
		Assert.True(MUIMessageFieldCodec.TryWritePointer(ref memory, address, 12,
			APTR.FromPointer(0x1020_3040)));
		Assert.True(MUIMessageFieldCodec.TryReadUInt32(ref memory, address, 0, out var unsignedValue));
		Assert.True(MUIMessageFieldCodec.TryReadInt32(ref memory, address, 4, out var signedValue));
		Assert.True(MUIMessageFieldCodec.TryReadUInt16(ref memory, address, 8, out var wordValue));
		Assert.True(MUIMessageFieldCodec.TryReadUInt8(ref memory, address, 10, out var byteValue));
		Assert.True(MUIMessageFieldCodec.TryReadPointer(ref memory, address, 12, out var pointerValue));
		Assert.Equal(0x8123_4567u, unsignedValue);
		Assert.Equal(-123456, signedValue);
		Assert.Equal((ushort)0xABCD, wordValue);
		Assert.Equal((byte)0xEF, byteValue);
		Assert.Equal(0x1020_3040u, pointerValue.Raw);
		Assert.False(MUIMessageFieldCodec.TryReadUInt32(ref memory, APTR.Null, 0, out _));
		Assert.False(MUIMessageFieldCodec.TryReadUInt16(ref memory, address, 1, out _));
		Assert.False(MUIMessageFieldCodec.TryReadUInt32(ref memory, address, 54, out _));
	}

	[Fact]
	public void CodecsRejectNullMisalignedAndTruncatedPointers()
	{
		var memory = new Memory(64);
		Assert.False(MUI_MinMaxCodec.TryRead(ref memory, APTR.Null, out _));
		Assert.False(MUI_MinMaxCodec.TryRead(ref memory, APTR.FromPointer(3), out _));
		Assert.False(MUI_RenderInfoCodec.TryRead(ref memory, APTR.FromPointer(48), out _));
	}

	private static void Size<T>(int expected) where T : struct =>
		Assert.Equal(expected, Unsafe.SizeOf<T>());

	private static void Offset<T>(string field, int expected) where T : struct =>
		Assert.Equal(expected, Marshal.OffsetOf<T>(field).ToInt32());

	private struct Memory : IAmigaGuestMemory
	{
		private readonly byte[] _bytes;

		internal Memory(int size) => _bytes = new byte[size];

		public byte ReadUInt8(APTR address, int offset = 0) =>
			_bytes[Index(address, offset, 1)];

		public ushort ReadUInt16(APTR address, int offset = 0)
		{
			var index = Index(address, offset, 2);
			return (ushort)((_bytes[index] << 8) | _bytes[index + 1]);
		}

		public uint ReadUInt32(APTR address, int offset = 0)
		{
			var index = Index(address, offset, 4);
			return ((uint)_bytes[index] << 24) | ((uint)_bytes[index + 1] << 16) |
				((uint)_bytes[index + 2] << 8) | _bytes[index + 3];
		}

		public void WriteUInt8(APTR address, int offset, byte value) =>
			_bytes[Index(address, offset, 1)] = value;

		public void WriteUInt16(APTR address, int offset, ushort value)
		{
			var index = Index(address, offset, 2);
			_bytes[index] = (byte)(value >> 8);
			_bytes[index + 1] = (byte)value;
		}

		public void WriteUInt32(APTR address, int offset, uint value)
		{
			var index = Index(address, offset, 4);
			_bytes[index] = (byte)(value >> 24);
			_bytes[index + 1] = (byte)(value >> 16);
			_bytes[index + 2] = (byte)(value >> 8);
			_bytes[index + 3] = (byte)value;
		}

		public void Clear(APTR address, uint byteCount) =>
			Array.Clear(_bytes, Index(address, 0, checked((int)byteCount)),
				checked((int)byteCount));

		public void Copy(APTR source, APTR destination, uint byteCount) =>
			Array.Copy(_bytes, Index(source, 0, checked((int)byteCount)), _bytes,
				Index(destination, 0, checked((int)byteCount)), checked((int)byteCount));

		public bool IsMapped(APTR address, uint byteSize) => address.Raw != 0 &&
			address.Raw <= int.MaxValue && byteSize <= int.MaxValue &&
			address.Raw <= (uint)_bytes.Length &&
			byteSize <= (uint)_bytes.Length - address.Raw;

		private int Index(APTR address, int offset, int size)
		{
			if (offset < 0 || size < 0) throw new ArgumentOutOfRangeException();
			var value = checked(address.Raw + (uint)offset);
			if (value > int.MaxValue || size > _bytes.Length - (int)value)
				throw new ArgumentOutOfRangeException();
			return (int)value;
		}
	}
}
