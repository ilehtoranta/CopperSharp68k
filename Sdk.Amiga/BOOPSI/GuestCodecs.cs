/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

namespace Amiga;

public static class BOOPSIGuestCodec
{
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
		cl_Reserved = memory.ReadUInt32(address, BOOPSILayout.Class.Reserved),
		cl_Super = APTR.FromPointer(memory.ReadUInt32(address, BOOPSILayout.Class.Super)),
		cl_ID = APTR.FromPointer(memory.ReadUInt32(address, BOOPSILayout.Class.Id)),
		cl_InstOffset = memory.ReadUInt16(address, BOOPSILayout.Class.InstanceOffset),
		cl_InstSize = memory.ReadUInt16(address, BOOPSILayout.Class.InstanceSize),
		cl_UserData = memory.ReadUInt32(address, BOOPSILayout.Class.UserData),
		cl_SubclassCount = memory.ReadUInt32(address, BOOPSILayout.Class.SubclassCount),
		cl_ObjectCount = memory.ReadUInt32(address, BOOPSILayout.Class.ObjectCount),
		cl_Flags = memory.ReadUInt32(address, BOOPSILayout.Class.Flags),
	};

	public static void WriteClass<TMemory>(ref TMemory memory, APTR address,
		IClass value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.cl_Dispatcher.MinNode.Successor.Raw);
		memory.WriteUInt32(address, 4, value.cl_Dispatcher.MinNode.Predecessor.Raw);
		memory.WriteUInt32(address, 8, value.cl_Dispatcher.Entry.Raw);
		memory.WriteUInt32(address, 12, value.cl_Dispatcher.SubEntry.Raw);
		memory.WriteUInt32(address, 16, value.cl_Dispatcher.Data.Raw);
		memory.WriteUInt32(address, BOOPSILayout.Class.Reserved, value.cl_Reserved);
		memory.WriteUInt32(address, BOOPSILayout.Class.Super, value.cl_Super.Raw);
		memory.WriteUInt32(address, BOOPSILayout.Class.Id, value.cl_ID.Raw);
		memory.WriteUInt16(address, BOOPSILayout.Class.InstanceOffset,
			value.cl_InstOffset);
		memory.WriteUInt16(address, BOOPSILayout.Class.InstanceSize,
			value.cl_InstSize);
		memory.WriteUInt32(address, BOOPSILayout.Class.UserData, value.cl_UserData);
		memory.WriteUInt32(address, BOOPSILayout.Class.SubclassCount,
			value.cl_SubclassCount);
		memory.WriteUInt32(address, BOOPSILayout.Class.ObjectCount,
			value.cl_ObjectCount);
		memory.WriteUInt32(address, BOOPSILayout.Class.Flags, value.cl_Flags);
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

	public static opUpdate ReadOpUpdate<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		opu_AttrList = APTR.FromPointer(memory.ReadUInt32(address, 4)),
		opu_GInfo = APTR.FromPointer(memory.ReadUInt32(address, 8)),
		opu_Flags = memory.ReadUInt32(address, 12),
	};

	public static opGet ReadOpGet<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		opg_AttrID = memory.ReadUInt32(address, 4),
		opg_Storage = APTR.FromPointer(memory.ReadUInt32(address, 8)),
	};
}
