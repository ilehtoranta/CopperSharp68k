/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class AsyncIoBindingTests
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static APTR CallOpenAsync() => AsyncIO.OpenAsync("RAM:asyncio-test",
		AsyncOpenMode.Read, 8192);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallReadAsync() => AsyncIO.ReadAsync(0x0000_4200u,
		0x0000_4300u, 128);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallSeekAsync() => AsyncIO.SeekAsync(0x0000_4200u, 32,
		AsyncSeekMode.Current);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static STRPTR CallFGetsLenAsync() => AsyncIO.FGetsLenAsync(0x0000_4200u,
		0x0000_4300u, 128, 0x0000_4400u);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallPeekAsync() => AsyncIO.PeekAsync(0x0000_4200u,
		0x0000_4300u, 128);

	[Fact]
	public void AsyncIoUsesAManualLibraryBase()
	{
		var attribute = typeof(AsyncIO).GetCustomAttribute<AmigaLibraryAttribute>();
		var property = typeof(AsyncIO).GetProperty(nameof(AsyncIO.AsyncIOLibraryBase),
			BindingFlags.Public | BindingFlags.Static);

		Assert.NotNull(attribute);
		Assert.Equal(AsyncIO.Name, attribute.Name);
		Assert.Equal(AmigaLibraryBasePolicy.Manual, attribute.BasePolicy);
		Assert.NotNull(property);
		Assert.Equal(typeof(APTR), property.PropertyType);
	}

	public static IEnumerable<object[]> PublicVectors =>
	[
		Vector(nameof(AsyncIO.OpenAsync), -30, [M68kRegister.A0, M68kRegister.D0, M68kRegister.D1]),
		Vector(nameof(AsyncIO.OpenAsyncFromFH), -36, [M68kRegister.A0, M68kRegister.D0, M68kRegister.D1]),
		Vector(nameof(AsyncIO.CloseAsync), -42, [M68kRegister.A0]),
		Vector(nameof(AsyncIO.SeekAsync), -48, [M68kRegister.A0, M68kRegister.D0, M68kRegister.D1]),
		Vector(nameof(AsyncIO.ReadAsync), -54, [M68kRegister.A0, M68kRegister.A1, M68kRegister.D0]),
		Vector(nameof(AsyncIO.WriteAsync), -60, [M68kRegister.A0, M68kRegister.A1, M68kRegister.D0]),
		Vector(nameof(AsyncIO.ReadCharAsync), -66, [M68kRegister.A0]),
		Vector(nameof(AsyncIO.WriteCharAsync), -72, [M68kRegister.A0, M68kRegister.D0]),
		Vector(nameof(AsyncIO.ReadLineAsync), -78, [M68kRegister.A0, M68kRegister.A1, M68kRegister.D0]),
		Vector(nameof(AsyncIO.WriteLineAsync), -84, [M68kRegister.A0, M68kRegister.A1]),
		Vector(nameof(AsyncIO.FGetsAsync), -90, [M68kRegister.A0, M68kRegister.A1, M68kRegister.D0]),
		Vector(nameof(AsyncIO.FGetsLenAsync), -96, [M68kRegister.A0, M68kRegister.A1, M68kRegister.D0, M68kRegister.A2]),
		Vector(nameof(AsyncIO.PeekAsync), -102, [M68kRegister.A0, M68kRegister.A1, M68kRegister.D0]),
	];

	[Theory]
	[MemberData(nameof(PublicVectors))]
	public void AsyncIoVectorsUsePublishedM68kAbi(string methodName, int lvo,
		M68kRegister[] parameters)
	{
		var method = typeof(AsyncIO).GetMethod(methodName,
			BindingFlags.Public | BindingFlags.Static)!;

		Assert.Equal(lvo, method.GetCustomAttribute<AmigaLvoAttribute>()?.Offset);
		Assert.Equal(parameters, method.GetParameters().Select(parameter =>
			parameter.GetCustomAttribute<M68kRegisterAttribute>()!.Register));
		Assert.Equal(M68kRegister.D0,
			method.ReturnParameter.GetCustomAttribute<M68kRegisterAttribute>()?.Register);
	}

	[Theory]
	[InlineData(nameof(CallOpenAsync), "-30(a6)")]
	[InlineData(nameof(CallReadAsync), "-54(a6)")]
	[InlineData(nameof(CallSeekAsync), "-48(a6)")]
	[InlineData(nameof(CallFGetsLenAsync), "-96(a6)")]
	[InlineData(nameof(CallPeekAsync), "-102(a6)")]
	public void AsyncIoCallsLowerThroughTheManualLibraryBase(string methodName,
		string vector)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(AsyncIoBindingTests).FullName}::{methodName}",
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Contains(vector, result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void AsyncFileUsesThePublishedSharedLibraryLayoutAndInspector()
	{
		Assert.Equal(154, Marshal.SizeOf<AsyncFile>());
		Assert.Equal(AsyncFile.Size, Marshal.SizeOf<AsyncFile>());
		AssertOffset(nameof(AsyncFile.File), AsyncIOLayout.AsyncFile.File);
		AssertOffset(nameof(AsyncFile.BlockSize), AsyncIOLayout.AsyncFile.BlockSize);
		AssertOffset(nameof(AsyncFile.Handler), AsyncIOLayout.AsyncFile.Handler);
		AssertOffset(nameof(AsyncFile.Offset), AsyncIOLayout.AsyncFile.Offset);
		AssertOffset(nameof(AsyncFile.BytesLeft), AsyncIOLayout.AsyncFile.BytesLeft);
		AssertOffset(nameof(AsyncFile.BufferSize), AsyncIOLayout.AsyncFile.BufferSize);
		AssertOffset(nameof(AsyncFile.Buffer0), AsyncIOLayout.AsyncFile.Buffer0);
		AssertOffset(nameof(AsyncFile.Buffer1), AsyncIOLayout.AsyncFile.Buffer1);
		AssertOffset(nameof(AsyncFile.Packet), AsyncIOLayout.AsyncFile.Packet);
		AssertOffset(nameof(AsyncFile.PacketPort), AsyncIOLayout.AsyncFile.PacketPort);
		AssertOffset(nameof(AsyncFile.CurrentBuffer), AsyncIOLayout.AsyncFile.CurrentBuffer);
		AssertOffset(nameof(AsyncFile.SeekOffset), AsyncIOLayout.AsyncFile.SeekOffset);
		AssertOffset(nameof(AsyncFile.PacketPending), AsyncIOLayout.AsyncFile.PacketPending);
		AssertOffset(nameof(AsyncFile.ReadMode), AsyncIOLayout.AsyncFile.ReadMode);
		AssertOffset(nameof(AsyncFile.CloseFileHandle), AsyncIOLayout.AsyncFile.CloseFileHandle);
		AssertOffset(nameof(AsyncFile.SeekPastEndOfFile), AsyncIOLayout.AsyncFile.SeekPastEndOfFile);
		AssertOffset(nameof(AsyncFile.LastResult1), AsyncIOLayout.AsyncFile.LastResult1);
		AssertOffset(nameof(AsyncFile.LastBytesLeft), AsyncIOLayout.AsyncFile.LastBytesLeft);

		var memory = new Memory(256);
		var address = APTR.FromPointer(8);
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.File, 0x1234_5678);
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.BlockSize, 512);
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.Handler, 0x1111_2222);
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.Offset, 0x3333_4444);
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.BytesLeft,
			unchecked((uint)-24));
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.BufferSize, 8192);
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.Buffer0, 0x5555_6666);
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.Buffer1, 0x7777_8888);
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.CurrentBuffer, 1);
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.SeekOffset, 64);
		memory.WriteUInt8(address, AsyncIOLayout.AsyncFile.PacketPending, 1);
		memory.WriteUInt8(address, AsyncIOLayout.AsyncFile.ReadMode, 1);
		memory.WriteUInt8(address, AsyncIOLayout.AsyncFile.CloseFileHandle, 0);
		memory.WriteUInt8(address, AsyncIOLayout.AsyncFile.SeekPastEndOfFile, 1);
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.LastResult1, 42);
		memory.WriteUInt32(address, AsyncIOLayout.AsyncFile.LastBytesLeft, 7);

		Assert.True(AsyncFileCodec.IsMapped(ref memory, address));
		Assert.Equal(0x1234_5678u, AsyncFileCodec.ReadFile(ref memory, address).Raw);
		Assert.Equal(512u, AsyncFileCodec.ReadBlockSize(ref memory, address));
		Assert.Equal(0x1111_2222u, AsyncFileCodec.ReadHandler(ref memory, address).Raw);
		Assert.Equal(0x3333_4444u, AsyncFileCodec.ReadOffset(ref memory, address).Raw);
		Assert.Equal(-24, AsyncFileCodec.ReadBytesLeft(ref memory, address));
		Assert.Equal(8192u, AsyncFileCodec.ReadBufferSize(ref memory, address));
		Assert.Equal(0x5555_6666u, AsyncFileCodec.ReadBuffer(ref memory, address, 0).Raw);
		Assert.Equal(0x7777_8888u, AsyncFileCodec.ReadBuffer(ref memory, address, 1).Raw);
		Assert.Equal(address.Raw + AsyncIOLayout.AsyncFile.Packet,
			AsyncFileCodec.PacketAddress(address).Raw);
		Assert.Equal(address.Raw + AsyncIOLayout.AsyncFile.PacketPort,
			AsyncFileCodec.PacketPortAddress(address).Raw);
		Assert.Equal(1u, AsyncFileCodec.ReadCurrentBuffer(ref memory, address));
		Assert.Equal(64u, AsyncFileCodec.ReadSeekOffset(ref memory, address));
		Assert.Equal((byte)1, AsyncFileCodec.ReadPacketPending(ref memory, address));
		Assert.Equal((byte)1, AsyncFileCodec.ReadReadMode(ref memory, address));
		Assert.Equal((byte)0, AsyncFileCodec.ReadCloseFileHandle(ref memory, address));
		Assert.Equal((byte)1, AsyncFileCodec.ReadSeekPastEndOfFile(ref memory, address));
		Assert.Equal(42u, AsyncFileCodec.ReadLastResult1(ref memory, address));
		Assert.Equal(7u, AsyncFileCodec.ReadLastBytesLeft(ref memory, address));
	}

	private static object[] Vector(string methodName, int lvo,
		M68kRegister[] parameters) => [methodName, lvo, parameters];

	private static void AssertOffset(string field, int expected) => Assert.Equal(expected,
		Marshal.OffsetOf<AsyncFile>(field).ToInt32());

	private struct Memory : IAmigaGuestMemory
	{
		private readonly byte[] _bytes;
		internal Memory(int size) => _bytes = new byte[size];
		public byte ReadUInt8(APTR address, int offset = 0) =>
			_bytes[checked((int)address.Raw + offset)];
		public ushort ReadUInt16(APTR address, int offset = 0)
		{
			var index = checked((int)address.Raw + offset);
			return (ushort)((_bytes[index] << 8) | _bytes[index + 1]);
		}
		public uint ReadUInt32(APTR address, int offset = 0)
		{
			var index = checked((int)address.Raw + offset);
			return ((uint)_bytes[index] << 24) | ((uint)_bytes[index + 1] << 16) |
				((uint)_bytes[index + 2] << 8) | _bytes[index + 3];
		}
		public void WriteUInt8(APTR address, int offset, byte value) =>
			_bytes[checked((int)address.Raw + offset)] = value;
		public void WriteUInt16(APTR address, int offset, ushort value)
		{
			var index = checked((int)address.Raw + offset);
			_bytes[index] = (byte)(value >> 8);
			_bytes[index + 1] = (byte)value;
		}
		public void WriteUInt32(APTR address, int offset, uint value)
		{
			var index = checked((int)address.Raw + offset);
			_bytes[index] = (byte)(value >> 24);
			_bytes[index + 1] = (byte)(value >> 16);
			_bytes[index + 2] = (byte)(value >> 8);
			_bytes[index + 3] = (byte)value;
		}
		public void Clear(APTR address, uint byteCount) => Array.Clear(_bytes,
			checked((int)address.Raw), checked((int)byteCount));
		public void Copy(APTR source, APTR destination, uint byteCount) => Array.Copy(
			_bytes, checked((int)source.Raw), _bytes, checked((int)destination.Raw),
			checked((int)byteCount));
		public bool IsMapped(APTR address, uint byteSize) => address.Raw != 0 &&
			address.Raw <= (uint)_bytes.Length && byteSize <= (uint)_bytes.Length - address.Raw;
	}
}
