/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Gadget
{
	public const uint Size = 44;

	public APTR NextGadget;
	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public GadgetFlags Flags;
	public GadgetActivationFlags Activation;
	public GadgetType GadgetType;
	public APTR GadgetRender;
	public APTR SelectRender;
	public APTR GadgetText;
	public int MutualExclude;
	public APTR SpecialInfo;
	public ushort GadgetID;
	public APTR UserData;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ExtGadget
{
	public const uint Size = 56;

	public APTR NextGadget;
	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public GadgetFlags Flags;
	public GadgetActivationFlags Activation;
	public GadgetType GadgetType;
	public APTR GadgetRender;
	public APTR SelectRender;
	public APTR GadgetText;
	public int MutualExclude;
	public APTR SpecialInfo;
	public ushort GadgetID;
	public APTR UserData;
	public GadgetMoreFlags MoreFlags;
	public short BoundsLeftEdge;
	public short BoundsTopEdge;
	public short BoundsWidth;
	public short BoundsHeight;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct BoolInfo
{
	public const uint Size = 10;

	public BoolInfoFlags Flags;
	public APTR Mask;
	public int Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct PropInfo
{
	public const uint Size = 22;

	public PropInfoFlags Flags;
	public ushort HorizPot;
	public ushort VertPot;
	public ushort HorizBody;
	public ushort VertBody;
	public ushort ContainerWidth;
	public ushort ContainerHeight;
	public ushort HorizontalPotResolution;
	public ushort VerticalPotResolution;
	public ushort LeftBorder;
	public ushort TopBorder;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct StringInfo
{
	public const uint Size = 36;

	public STRPTR Buffer;
	public STRPTR UndoBuffer;
	public short BufferPosition;
	public short MaxChars;
	public short DisplayPosition;
	public short UndoPosition;
	public short NumberOfChars;
	public short DisplayCount;
	public short ContainerLeft;
	public short ContainerTop;
	public APTR Extension;
	public int LongInt;
	public APTR AlternateKeyMap;
}
