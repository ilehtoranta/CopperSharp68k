/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

namespace Amiga;

public static class BOOPSIGuestCodec
{
	public const uint MethodMessageSize = 4;
	public const uint OpGetStorageSize = 4;

	public static uint ReadMethodId<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt32(address, 0);

	public static void WriteMethodId<TMemory>(ref TMemory memory, APTR address,
		uint methodId) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, 0, methodId);

	public static IClass ReadClass<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		cl_Dispatcher = new Hook
		{
			MinNode = new MinNode
			{
				Successor = APTR.FromPointer(memory.ReadUInt32(address, 0)),
				Predecessor = APTR.FromPointer(memory.ReadUInt32(address, 4)),
			},
			Entry = APTR.FromPointer(memory.ReadUInt32(address, 8)),
			SubEntry = APTR.FromPointer(memory.ReadUInt32(address, 12)),
			Data = APTR.FromPointer(memory.ReadUInt32(address, 16)),
		},
		cl_Reserved = memory.ReadUInt32(address, 20),
		cl_Super = APTR.FromPointer(memory.ReadUInt32(address, 24)),
		cl_ID = APTR.FromPointer(memory.ReadUInt32(address, 28)),
		cl_InstOffset = memory.ReadUInt16(address, 32),
		cl_InstSize = memory.ReadUInt16(address, 34),
		cl_UserData = memory.ReadUInt32(address, 36),
		cl_SubclassCount = memory.ReadUInt32(address, 40),
		cl_ObjectCount = memory.ReadUInt32(address, 44),
		cl_Flags = memory.ReadUInt32(address, 48),
	};

	public static void WriteClass<TMemory>(ref TMemory memory, APTR address,
		IClass value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.cl_Dispatcher.MinNode.Successor.Raw);
		memory.WriteUInt32(address, 4, value.cl_Dispatcher.MinNode.Predecessor.Raw);
		memory.WriteUInt32(address, 8, value.cl_Dispatcher.Entry.Raw);
		memory.WriteUInt32(address, 12, value.cl_Dispatcher.SubEntry.Raw);
		memory.WriteUInt32(address, 16, value.cl_Dispatcher.Data.Raw);
		memory.WriteUInt32(address, 20, value.cl_Reserved);
		memory.WriteUInt32(address, 24, value.cl_Super.Raw);
		memory.WriteUInt32(address, 28, value.cl_ID.Raw);
		memory.WriteUInt16(address, 32, value.cl_InstOffset);
		memory.WriteUInt16(address, 34, value.cl_InstSize);
		memory.WriteUInt32(address, 36, value.cl_UserData);
		memory.WriteUInt32(address, 40, value.cl_SubclassCount);
		memory.WriteUInt32(address, 44, value.cl_ObjectCount);
		memory.WriteUInt32(address, 48, value.cl_Flags);
	}

	public static _Object ReadObjectHeader<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		o_Node = new MinNode
		{
			Successor = APTR.FromPointer(memory.ReadUInt32(address, 0)),
			Predecessor = APTR.FromPointer(memory.ReadUInt32(address, 4)),
		},
		o_Class = APTR.FromPointer(memory.ReadUInt32(address, 8)),
	};

	public static void WriteObjectHeader<TMemory>(ref TMemory memory,
		APTR address, _Object value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.o_Node.Successor.Raw);
		memory.WriteUInt32(address, 4, value.o_Node.Predecessor.Raw);
		memory.WriteUInt32(address, 8, value.o_Class.Raw);
	}

	public static opSet ReadOpSet<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		ops_AttrList = APTR.FromPointer(memory.ReadUInt32(address, 4)),
		ops_GInfo = APTR.FromPointer(memory.ReadUInt32(address, 8)),
	};

	public static void WriteOpSet<TMemory>(ref TMemory memory, APTR address,
		opSet value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.ops_AttrList.Raw);
		memory.WriteUInt32(address, 8, value.ops_GInfo.Raw);
	}

	public static opUpdate ReadOpUpdate<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		opu_AttrList = APTR.FromPointer(memory.ReadUInt32(address, 4)),
		opu_GInfo = APTR.FromPointer(memory.ReadUInt32(address, 8)),
		opu_Flags = memory.ReadUInt32(address, 12),
	};

	public static void WriteOpUpdate<TMemory>(ref TMemory memory, APTR address,
		opUpdate value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.opu_AttrList.Raw);
		memory.WriteUInt32(address, 8, value.opu_GInfo.Raw);
		memory.WriteUInt32(address, 12, value.opu_Flags);
	}

	public static opGet ReadOpGet<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		opg_AttrID = memory.ReadUInt32(address, 4),
		opg_Storage = APTR.FromPointer(memory.ReadUInt32(address, 8)),
	};

	public static void WriteOpGet<TMemory>(ref TMemory memory, APTR address,
		opGet value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.opg_AttrID);
		memory.WriteUInt32(address, 8, value.opg_Storage.Raw);
	}

	public static opAddTail ReadOpAddTail<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		opat_List = APTR.FromPointer(memory.ReadUInt32(address, 4)),
	};

	public static void WriteOpAddTail<TMemory>(ref TMemory memory, APTR address,
		opAddTail value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.opat_List.Raw);
	}

	public static opMember ReadOpMember<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		opam_Object = APTR.FromPointer(memory.ReadUInt32(address, 4)),
	};

	public static void WriteOpMember<TMemory>(ref TMemory memory, APTR address,
		opMember value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.opam_Object.Raw);
	}
}
