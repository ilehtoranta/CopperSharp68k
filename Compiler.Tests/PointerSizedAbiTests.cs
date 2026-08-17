using System.Runtime.InteropServices;
using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class PointerSizedAbiTests
{
	public static uint M68kPointerSizedEntry()
	{
		var value = IPTR.FromUInt32(0x1234_5678u);
		var signed = SIPTR.FromBits(0xFFFF_FFFEu);
		var argument = new AmigaVarArg(value);
		var signedArgument = new AmigaVarArg(signed);
		return argument.Raw ^ signedArgument.Raw;
	}

	[Fact]
	public void PointerSizedTypesExposeTheCurrentM68kWidth()
	{
		Assert.Equal(4u, IPTR.M68kSize);
		Assert.Equal(4u, SIPTR.M68kSize);
		Assert.Equal((uint)IntPtr.Size, (uint)Marshal.SizeOf<IPTR>());
		Assert.Equal((uint)IntPtr.Size, (uint)Marshal.SizeOf<SIPTR>());
	}

	[Fact]
	public void IPTRPreservesNativeAndCurrentGuestViews()
	{
		var value = IPTR.FromUInt32(0xA5A5_1234u);

		Assert.Equal((nuint)0xA5A5_1234u, value.Raw);
		Assert.Equal(0xA5A5_1234u, IPTR.ToUInt32(value));
		Assert.Equal(value, (IPTR)0xA5A5_1234u);
		Assert.Equal((nuint)0xA5A5_1234u, (nuint)value);
	}

	[Fact]
	public void SIPTRPreservesSignedAndBitPatternViews()
	{
		var value = SIPTR.FromInt32(-1234);
		var bits = SIPTR.FromBits(0xFFFF_FB2Eu);

		Assert.Equal((nint)(-1234), value.Raw);
		Assert.Equal(-1234, SIPTR.ToInt32(value));
		Assert.Equal(0xFFFF_FB2Eu, SIPTR.ToBits(bits));
		Assert.Equal(-1234, (int)value);
	}

	[Fact]
	public void AmigaVarArgUsesIPTRWithoutChangingTheM68kRawView()
	{
		var value = IPTR.FromUInt32(0x1234_5678u);
		var argument = new AmigaVarArg(value);
		var signed = new AmigaVarArg(SIPTR.FromBits(0xFFFF_FFFEu));

		Assert.Equal(value, argument.Value);
		Assert.Equal(0x1234_5678u, argument.Raw);
		Assert.Equal(0xFFFF_FFFEu, signed.Raw);
	}

	[Fact]
	public void PointerSizedTypesCompileForTheM68000Target()
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(PointerSizedAbiTests).Assembly.Location,
			EntryPoint = $"{typeof(PointerSizedAbiTests).FullName}::M68kPointerSizedEntry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			IncludedExportNames = Array.Empty<string>(),
		});

		Assert.NotNull(result.Text);
		Assert.NotEmpty(result.Code);
	}
}
