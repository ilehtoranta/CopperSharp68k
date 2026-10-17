using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>
/// MorphOS exec/resident.h packed extension. Revision and Tags are valid only
/// when Header.Flags contains Extended. The classic Resident remains 26 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MorphOSResident
{
	public const uint Size = 32;
	public Resident Header;
	public ushort Revision;
	public APTR Tags;
}
