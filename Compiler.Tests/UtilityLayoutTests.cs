using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class UtilityLayoutTests
{
	[Fact]
	public void HookUsesPublishedUtilityOffsets()
	{
		Assert.Equal(20, Unsafe.SizeOf<Hook>());
		Assert.Equal(UtilityLayout.Hook.Size, Unsafe.SizeOf<Hook>());
		Assert.Equal(UtilityLayout.Hook.MinNode,
			Marshal.OffsetOf<Hook>(nameof(Hook.MinNode)).ToInt32());
		Assert.Equal(UtilityLayout.Hook.Entry,
			Marshal.OffsetOf<Hook>(nameof(Hook.Entry)).ToInt32());
		Assert.Equal(UtilityLayout.Hook.SubEntry,
			Marshal.OffsetOf<Hook>(nameof(Hook.SubEntry)).ToInt32());
		Assert.Equal(UtilityLayout.Hook.Data,
			Marshal.OffsetOf<Hook>(nameof(Hook.Data)).ToInt32());
	}
}
