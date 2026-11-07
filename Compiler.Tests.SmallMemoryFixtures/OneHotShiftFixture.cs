using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class OneHotShiftFixture
{
	public static void Empty() { }
	[M68kInternalRegisterAbi, M68kExport("onehot.mask")]
	public static uint Mask([M68kRegister(M68kRegister.D0)] int count) => 1u << count;
	[M68kInternalRegisterAbi, M68kExport("onehot.signed")]
	public static uint Signed([M68kRegister(M68kRegister.D0)] int count) => unchecked((uint)(1 << count));
	[M68kInternalRegisterAbi, M68kExport("onehot.byte")]
	public static uint Byte([M68kRegister(M68kRegister.D0)] int count) => 1u << unchecked((byte)count);
	[M68kInternalRegisterAbi, M68kExport("onehot.sbyte")]
	public static uint SignedByte([M68kRegister(M68kRegister.D0)] int count) => 1u << unchecked((sbyte)count);
	[M68kInternalRegisterAbi, M68kExport("onehot.byte-load")]
	public static uint ByteLoad([M68kRegister(M68kRegister.A6)] APTR source) => 1u << APTR.ReadUInt8(source, 0);
	private struct CallContext { public uint Value; }
	[M68kExport("onehot.pressure")]
	public static uint Pressure([M68kRegister(M68kRegister.A0)] APTR source)
	{
		var context = default(CallContext);
		var record = APTR.FromPointer(APTR.ReadUInt32(source, 0));
		APTR.WriteUInt8(record, 0, 0x7F);
		return Consume(ref context, APTR.FromPointer(APTR.ReadUInt32(source, 8)), APTR.ReadUInt32(record, 4));
	}
	[M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
	private static uint Consume([M68kRegister(M68kRegister.A0)] ref CallContext context,
		[M68kRegister(M68kRegister.A1)] APTR pointer, [M68kRegister(M68kRegister.D0)] uint value)
	{
		context.Value = value;
		APTR.WriteUInt32(pointer, 0, value);
		return value;
	}
	[M68kInternalRegisterAbi, M68kExport("onehot.dispatch")]
	public static uint Dispatch([M68kRegister(M68kRegister.A6)] APTR source)
	{
		Invoke(APTR.FromPointer(APTR.ReadUInt32(source, 8)),
			APTR.FromPointer(APTR.ReadUInt32(source, 0)), 1u << APTR.ReadUInt8(source, 4));
		return APTR.ReadUInt32(source, 16);
	}
	[AmigaIndirectCall(M68kRegister.A0, preservesNonvolatileInputs: true)]
	private static extern void Invoke([M68kRegister(M68kRegister.A0)] APTR entry,
		[M68kRegister(M68kRegister.A1)] APTR pointer, [M68kRegister(M68kRegister.D0)] uint mask);
	[M68kInternalRegisterAbi, M68kExport("onehot.masked-live")]
	public static uint MaskedLive([M68kRegister(M68kRegister.D0)] int count)
	{
		var bounded = count & 31;
		return (1u << bounded) ^ unchecked((uint)bounded);
	}
	[M68kInternalRegisterAbi, M68kExport("onehot.masked-seven")]
	public static uint MaskedSeven([M68kRegister(M68kRegister.D0)] int count) => 1u << (count & 7);
	[M68kInternalRegisterAbi, M68kExport("onehot.live")]
	public static uint Live([M68kRegister(M68kRegister.D0)] int count)
	{
		var value = 1u << count;
		return value ^ unchecked((uint)count);
	}
	[M68kInternalRegisterAbi, M68kExport("onehot.branch")]
	public static uint Branch([M68kRegister(M68kRegister.D0)] int count)
	{
		var value = 1u << count;
		if ((int)value < 0) return 0x12345678;
		if (value == 0) return 0xABCDEF01;
		return value + 3;
	}
	[M68kInternalRegisterAbi, M68kExport("onehot.shared")]
	public static uint Shared([M68kRegister(M68kRegister.D0)] int count)
	{
		var initial = 1u;
		var value = initial << count;
		return value + initial;
	}
	[M68kInternalRegisterAbi, M68kExport("onehot.other-left")]
	public static uint OtherLeft([M68kRegister(M68kRegister.D0)] int count) => 2u << count;
	[M68kInternalRegisterAbi, M68kExport("onehot.right")]
	public static uint Right([M68kRegister(M68kRegister.D0)] int count) => 1u >> count;
	[M68kInternalRegisterAbi, M68kExport("onehot.wide")]
	public static void Wide([M68kRegister(M68kRegister.D0)] int count, [M68kRegister(M68kRegister.A6)] APTR output)
	{
		var value = 1ul << count;
		APTR.WriteUInt32(output, 0, (uint)(value >> 32));
		APTR.WriteUInt32(output, 4, (uint)value);
	}
}
