using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private interface IStaticConstrainedAbiProbe
	{
		void Store(uint value);

		static virtual uint Write<TPlatform>(
			ref TPlatform platform, APTR buffer, uint capacity,
			ref uint copied, int code)
			where TPlatform : struct, IStaticConstrainedAbiProbe => 0u;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 4)]
	private struct FourByteStaticConstrainedAbiProbe : IStaticConstrainedAbiProbe
	{
		private uint _value;

		public readonly uint Value => _value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public void Store(uint value) => _value = value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static uint Write<TPlatform>(
			ref TPlatform platform, APTR buffer, uint capacity,
			ref uint copied, int code)
			where TPlatform : struct, IStaticConstrainedAbiProbe
		{
			platform.Store(buffer.Raw ^ capacity);
			copied = unchecked(copied + capacity);
			return unchecked(buffer.Raw + copied + (uint)code);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint InvokeStaticConstrainedAbiProbe<TPlatform>(
		ref TPlatform platform, APTR buffer, uint capacity,
		ref uint copied, int code)
		where TPlatform : struct, IStaticConstrainedAbiProbe =>
		TPlatform.Write(ref platform, buffer, capacity, ref copied, code);

	public static uint StaticConstrainedAbiEntry()
	{
		var platform = new FourByteStaticConstrainedAbiProbe();
		var copied = 0x10u;
		var result = InvokeStaticConstrainedAbiProbe(
			ref platform, APTR.FromPointer(0x3000), 0x1234u,
			ref copied, 0x5678);
		if (platform.Value != 0x2234u) return 1u;
		if (copied != 0x1244u) return 2u;
		if (result != 0x98BCu) return 3u;
		return 42u;
	}
}