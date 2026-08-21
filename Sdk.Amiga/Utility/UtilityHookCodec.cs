namespace Amiga;

/// <summary>Typed big-endian guest-memory access for the public Utility Hook.</summary>
public static class UtilityHookCodec
{
	public const uint Size = Hook.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		(address.Raw & 1u) == 0 && memory.IsMapped(address, Size);

	public static Hook Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		MinNode = new MinNode
		{
			Successor = APTR.FromPointer(memory.ReadUInt32(address,
				UtilityLayout.Hook.MinNodeSuccessor)),
			Predecessor = APTR.FromPointer(memory.ReadUInt32(address,
				UtilityLayout.Hook.MinNodePredecessor)),
		},
		Entry = APTR.FromPointer(memory.ReadUInt32(address,
			UtilityLayout.Hook.Entry)),
		SubEntry = APTR.FromPointer(memory.ReadUInt32(address,
			UtilityLayout.Hook.SubEntry)),
		Data = APTR.FromPointer(memory.ReadUInt32(address,
			UtilityLayout.Hook.Data)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address, Hook value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, UtilityLayout.Hook.MinNodeSuccessor,
			value.MinNode.Successor.Raw);
		memory.WriteUInt32(address, UtilityLayout.Hook.MinNodePredecessor,
			value.MinNode.Predecessor.Raw);
		memory.WriteUInt32(address, UtilityLayout.Hook.Entry, value.Entry.Raw);
		memory.WriteUInt32(address, UtilityLayout.Hook.SubEntry,
			value.SubEntry.Raw);
		memory.WriteUInt32(address, UtilityLayout.Hook.Data, value.Data.Raw);
	}
}
