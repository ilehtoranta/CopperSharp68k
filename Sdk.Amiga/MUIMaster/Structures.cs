/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.InteropServices;

namespace Amiga.MUI;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct MUI_Command
{
	public const uint Size = 36;
	public APTR mc_Name;
	public APTR mc_Template;
	public int mc_Parameters;
	public APTR mc_Hook;
	public fixed int mc_Reserved[5];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_Palette_Entry
{
	public const uint Size = 20;
	public int mpe_ID;
	public uint mpe_Red;
	public uint mpe_Green;
	public uint mpe_Blue;
	public int mpe_Group;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_InputHandlerValue
{
	public uint ihn_Signals;

	public ushort ihn_Millis
	{
		readonly get => (ushort)(ihn_Signals >> 16);
		set => ihn_Signals = (ihn_Signals & 0x0000_FFFFu) | ((uint)value << 16);
	}

	public ushort ihn_Current
	{
		readonly get => (ushort)ihn_Signals;
		set => ihn_Signals = (ihn_Signals & 0xFFFF_0000u) | value;
	}
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_InputHandlerNode
{
	public const uint Size = 24;
	public MinNode ihn_Node;
	public APTR ihn_Object;
	public MUI_InputHandlerValue ihn_Value;
	public uint ihn_Flags;
	public uint ihn_Method;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_EventHandlerNode
{
	public const uint Size = 24;
	public MinNode ehn_Node;
	public sbyte ehn_Reserved;
	public sbyte ehn_Priority;
	public ushort ehn_Flags;
	public APTR ehn_Object;
	public APTR ehn_Class;
	public uint ehn_Events;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_List_TestPos_Result
{
	public const uint Size = 12;
	public int entry;
	public short column;
	public ushort flags;
	public short xoffset;
	public short yoffset;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_RGBColor
{
	public const uint Size = 12;
	public uint red;
	public uint green;
	public uint blue;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_GlobalInfo
{
	public const uint Size = 8;
	public uint priv0;
	public APTR mgi_ApplicationObject;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_NotifyData
{
	public const uint Size = 28;
	public APTR mnd_GlobalInfo;
	public uint mnd_UserData;
	public uint mnd_ObjectID;
	public uint priv1;
	public uint priv2;
	public uint priv3;
	public uint priv4;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_MinMax
{
	public const uint Size = 12;
	public short MinWidth;
	public short MinHeight;
	public short MaxWidth;
	public short MaxHeight;
	public short DefWidth;
	public short DefHeight;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_LayoutDimensions
{
	public const uint Size = 16;
	public int Width;
	public int Height;
	public uint priv5;
	public uint priv6;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_LayoutMsg
{
	public const uint Size = 36;
	public uint lm_Type;
	public APTR lm_Children;
	public MUI_MinMax lm_MinMax;
	public MUI_LayoutDimensions lm_Layout;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_AreaData
{
	public const uint Size = 40;
	public APTR mad_RenderInfo;
	public uint priv7;
	public APTR mad_Font;
	public MUI_MinMax mad_MinMax;
	public IBox mad_Box;
	public sbyte mad_addleft;
	public sbyte mad_addtop;
	public sbyte mad_subwidth;
	public sbyte mad_subheight;
	public uint mad_Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_DragImage
{
	public const uint Size = 20;
	public APTR bm;
	public short width;
	public short height;
	public short touchx;
	public short touchy;
	public uint flags;
	public APTR mask;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_RenderInfo
{
	public const uint Size = 28;
	public APTR mri_WindowObject;
	public APTR mri_Screen;
	public APTR mri_DrawInfo;
	public APTR mri_Pens;
	public APTR mri_Window;
	public APTR mri_RastPort;
	public uint mri_Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct MUI_PenSpec
{
	public const uint Size = 32;
	public fixed byte buf[32];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MUI_CustomClass
{
	public const uint Size = 28;
	public APTR mcc_UserData;
	public APTR mcc_UtilityBase;
	public APTR mcc_DOSBase;
	public APTR mcc_GfxBase;
	public APTR mcc_IntuitionBase;
	public APTR mcc_Super;
	public APTR mcc_Class;
}
