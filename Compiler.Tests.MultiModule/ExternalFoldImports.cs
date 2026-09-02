using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests.MultiModule;

public static class ExternalFoldImports
{
	[M68kImport("fixture.fold-first")]
	[MethodImpl(MethodImplOptions.InternalCall)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int First([M68kRegister(M68kRegister.D0)] int value);

	[M68kImport("fixture.fold-second")]
	[MethodImpl(MethodImplOptions.InternalCall)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Second([M68kRegister(M68kRegister.D0)] int value);
}
