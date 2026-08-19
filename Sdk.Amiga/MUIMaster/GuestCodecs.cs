/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

namespace Amiga.MUI;

internal static class MUIAddress
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address, uint size)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 1) == 0 &&
		address.Raw <= uint.MaxValue - size && memory.IsMapped(address, size);
}

public static class MUI_MinMaxCodec
{
	public const uint Size = MUI_MinMax.Size;

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out MUI_MinMax value) where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!MUIAddress.IsMapped(ref memory, address, Size)) return false;
		value.MinWidth = unchecked((short)memory.ReadUInt16(address, 0));
		value.MinHeight = unchecked((short)memory.ReadUInt16(address, 2));
		value.MaxWidth = unchecked((short)memory.ReadUInt16(address, 4));
		value.MaxHeight = unchecked((short)memory.ReadUInt16(address, 6));
		value.DefWidth = unchecked((short)memory.ReadUInt16(address, 8));
		value.DefHeight = unchecked((short)memory.ReadUInt16(address, 10));
		return true;
	}

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		MUI_MinMax value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, unchecked((ushort)value.MinWidth));
		memory.WriteUInt16(address, 2, unchecked((ushort)value.MinHeight));
		memory.WriteUInt16(address, 4, unchecked((ushort)value.MaxWidth));
		memory.WriteUInt16(address, 6, unchecked((ushort)value.MaxHeight));
		memory.WriteUInt16(address, 8, unchecked((ushort)value.DefWidth));
		memory.WriteUInt16(address, 10, unchecked((ushort)value.DefHeight));
	}
}

public static class MUI_NotifyDataCodec
{
	public const uint Size = MUI_NotifyData.Size;

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out MUI_NotifyData value) where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!MUIAddress.IsMapped(ref memory, address, Size)) return false;
		value.mnd_GlobalInfo = APTR.FromPointer(memory.ReadUInt32(address, 0));
		value.mnd_UserData = memory.ReadUInt32(address, 4);
		value.mnd_ObjectID = memory.ReadUInt32(address, 8);
		value.priv1 = memory.ReadUInt32(address, 12);
		value.priv2 = memory.ReadUInt32(address, 16);
		value.priv3 = memory.ReadUInt32(address, 20);
		value.priv4 = memory.ReadUInt32(address, 24);
		return true;
	}

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		MUI_NotifyData value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.mnd_GlobalInfo.Raw);
		memory.WriteUInt32(address, 4, value.mnd_UserData);
		memory.WriteUInt32(address, 8, value.mnd_ObjectID);
		memory.WriteUInt32(address, 12, value.priv1);
		memory.WriteUInt32(address, 16, value.priv2);
		memory.WriteUInt32(address, 20, value.priv3);
		memory.WriteUInt32(address, 24, value.priv4);
	}
}

public static class MUI_LayoutMsgCodec
{
	public const uint Size = MUI_LayoutMsg.Size;

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out MUI_LayoutMsg value) where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!MUIAddress.IsMapped(ref memory, address, Size)) return false;
		value.lm_Type = memory.ReadUInt32(address, 0);
		value.lm_Children = APTR.FromPointer(memory.ReadUInt32(address, 4));
		value.lm_MinMax.MinWidth = unchecked((short)memory.ReadUInt16(address, 8));
		value.lm_MinMax.MinHeight = unchecked((short)memory.ReadUInt16(address, 10));
		value.lm_MinMax.MaxWidth = unchecked((short)memory.ReadUInt16(address, 12));
		value.lm_MinMax.MaxHeight = unchecked((short)memory.ReadUInt16(address, 14));
		value.lm_MinMax.DefWidth = unchecked((short)memory.ReadUInt16(address, 16));
		value.lm_MinMax.DefHeight = unchecked((short)memory.ReadUInt16(address, 18));
		value.lm_Layout.Width = unchecked((int)memory.ReadUInt32(address, 20));
		value.lm_Layout.Height = unchecked((int)memory.ReadUInt32(address, 24));
		value.lm_Layout.priv5 = memory.ReadUInt32(address, 28);
		value.lm_Layout.priv6 = memory.ReadUInt32(address, 32);
		return true;
	}

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		MUI_LayoutMsg value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.lm_Type);
		memory.WriteUInt32(address, 4, value.lm_Children.Raw);
		memory.WriteUInt16(address, 8, unchecked((ushort)value.lm_MinMax.MinWidth));
		memory.WriteUInt16(address, 10, unchecked((ushort)value.lm_MinMax.MinHeight));
		memory.WriteUInt16(address, 12, unchecked((ushort)value.lm_MinMax.MaxWidth));
		memory.WriteUInt16(address, 14, unchecked((ushort)value.lm_MinMax.MaxHeight));
		memory.WriteUInt16(address, 16, unchecked((ushort)value.lm_MinMax.DefWidth));
		memory.WriteUInt16(address, 18, unchecked((ushort)value.lm_MinMax.DefHeight));
		memory.WriteUInt32(address, 20, unchecked((uint)value.lm_Layout.Width));
		memory.WriteUInt32(address, 24, unchecked((uint)value.lm_Layout.Height));
		memory.WriteUInt32(address, 28, value.lm_Layout.priv5);
		memory.WriteUInt32(address, 32, value.lm_Layout.priv6);
	}
}

public static class MUI_RenderInfoCodec
{
	public const uint Size = MUI_RenderInfo.Size;

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out MUI_RenderInfo value) where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!MUIAddress.IsMapped(ref memory, address, Size)) return false;
		value.mri_WindowObject = APTR.FromPointer(memory.ReadUInt32(address, 0));
		value.mri_Screen = APTR.FromPointer(memory.ReadUInt32(address, 4));
		value.mri_DrawInfo = APTR.FromPointer(memory.ReadUInt32(address, 8));
		value.mri_Pens = APTR.FromPointer(memory.ReadUInt32(address, 12));
		value.mri_Window = APTR.FromPointer(memory.ReadUInt32(address, 16));
		value.mri_RastPort = APTR.FromPointer(memory.ReadUInt32(address, 20));
		value.mri_Flags = memory.ReadUInt32(address, 24);
		return true;
	}

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		MUI_RenderInfo value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.mri_WindowObject.Raw);
		memory.WriteUInt32(address, 4, value.mri_Screen.Raw);
		memory.WriteUInt32(address, 8, value.mri_DrawInfo.Raw);
		memory.WriteUInt32(address, 12, value.mri_Pens.Raw);
		memory.WriteUInt32(address, 16, value.mri_Window.Raw);
		memory.WriteUInt32(address, 20, value.mri_RastPort.Raw);
		memory.WriteUInt32(address, 24, value.mri_Flags);
	}
}

public static class MUI_CustomClassCodec
{
	public const uint Size = MUI_CustomClass.Size;

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out MUI_CustomClass value) where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!MUIAddress.IsMapped(ref memory, address, Size)) return false;
		value.mcc_UserData = APTR.FromPointer(memory.ReadUInt32(address, 0));
		value.mcc_UtilityBase = APTR.FromPointer(memory.ReadUInt32(address, 4));
		value.mcc_DOSBase = APTR.FromPointer(memory.ReadUInt32(address, 8));
		value.mcc_GfxBase = APTR.FromPointer(memory.ReadUInt32(address, 12));
		value.mcc_IntuitionBase = APTR.FromPointer(memory.ReadUInt32(address, 16));
		value.mcc_Super = APTR.FromPointer(memory.ReadUInt32(address, 20));
		value.mcc_Class = APTR.FromPointer(memory.ReadUInt32(address, 24));
		return true;
	}

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		MUI_CustomClass value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.mcc_UserData.Raw);
		memory.WriteUInt32(address, 4, value.mcc_UtilityBase.Raw);
		memory.WriteUInt32(address, 8, value.mcc_DOSBase.Raw);
		memory.WriteUInt32(address, 12, value.mcc_GfxBase.Raw);
		memory.WriteUInt32(address, 16, value.mcc_IntuitionBase.Raw);
		memory.WriteUInt32(address, 20, value.mcc_Super.Raw);
		memory.WriteUInt32(address, 24, value.mcc_Class.Raw);
	}
}

public static class MUI_PenSpecCodec
{
	public const uint Size = MUI_PenSpec.Size;

	public static unsafe bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out MUI_PenSpec value) where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!MUIAddress.IsMapped(ref memory, address, Size)) return false;
		for (var index = 0; index < Size; index++)
			value.buf[index] = memory.ReadUInt8(address, index);
		return true;
	}

	public static unsafe void Write<TMemory>(ref TMemory memory, APTR address,
		MUI_PenSpec value) where TMemory : struct, IAmigaGuestMemory
	{
		for (var index = 0; index < Size; index++)
			memory.WriteUInt8(address, index, value.buf[index]);
	}
}
