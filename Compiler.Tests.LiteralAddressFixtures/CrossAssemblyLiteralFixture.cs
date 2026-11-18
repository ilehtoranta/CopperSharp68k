using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Compiler.Tests;

public static class CrossAssemblyLiteralFixture
{
	public static void Empty() { }

	[M68kExport("literal-address.cross-assembly")]
	public static uint Entry() =>
		(uint)APTR.ReadUInt8(LiteralAddressFixture.Address(), 0) +
		APTR.ReadUInt8(LiteralAddressFixture.Address(), 3);

	public static uint CStringPoolingEntry()
	{
		var shared = CString.ToUInt32(CString.FromLiteral("A\u00E9\0Z"));
		var different = CString.ToUInt32(CString.FromLiteral("A\u00E9\0z"));
		return shared == LiteralAddressFixture.PooledCStringAddress() && shared != different ? shared : 0;
	}
}
