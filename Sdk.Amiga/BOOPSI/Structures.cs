/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.InteropServices;

namespace Amiga;

public static partial class BOOPSI
{
	public const uint OM_Dummy = 0x100;
	public const uint OM_NEW = 0x101;
	public const uint OM_DISPOSE = 0x102;
	public const uint OM_SET = 0x103;
	public const uint OM_GET = 0x104;
	public const uint OM_ADDTAIL = 0x105;
	public const uint OM_REMOVE = 0x106;
	public const uint OM_NOTIFY = 0x107;
	public const uint OM_UPDATE = 0x108;
	public const uint OM_ADDMEMBER = 0x109;
	public const uint OM_REMMEMBER = 0x10A;
	public const uint OPUF_INTERIM = 1;
	public const uint CLF_INLIST = 1;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IClass
{
	public const uint Size = 52;

	public Hook cl_Dispatcher;
	public uint cl_Reserved;
	public APTR cl_Super;
	public APTR cl_ID;
	public ushort cl_InstOffset;
	public ushort cl_InstSize;
	public uint cl_UserData;
	public uint cl_SubclassCount;
	public uint cl_ObjectCount;
	public uint cl_Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct _Object
{
	public const uint Size = 12;

	public MinNode o_Node;
	public APTR o_Class;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct opSet
{
	public const uint Size = 12;

	public uint MethodID;
	public APTR ops_AttrList;
	public APTR ops_GInfo;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct opUpdate
{
	public const uint Size = 16;

	public uint MethodID;
	public APTR opu_AttrList;
	public APTR opu_GInfo;
	public uint opu_Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct opGet
{
	public const uint Size = 12;

	public uint MethodID;
	public uint opg_AttrID;
	public APTR opg_Storage;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct opAddTail
{
	public const uint Size = 8;

	public uint MethodID;
	public APTR opat_List;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct opMember
{
	public const uint Size = 8;

	public uint MethodID;
	public APTR opam_Object;
}

public static class BOOPSILayout
{
	public static class Class
	{
		public const int Size = 52;
		public const int Dispatcher = 0;
		public const int Reserved = 20;
		public const int Super = 24;
		public const int Id = 28;
		public const int InstanceOffset = 32;
		public const int InstanceSize = 34;
		public const int UserData = 36;
		public const int SubclassCount = 40;
		public const int ObjectCount = 44;
		public const int Flags = 48;
	}

	public static class ObjectHeader
	{
		public const int Size = 12;
		public const int Node = 0;
		public const int Class = 8;
	}
}
