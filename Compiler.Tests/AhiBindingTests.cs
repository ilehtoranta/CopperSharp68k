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

public sealed class AhiBindingTests
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static APTR CallAllocAudio() => AHI.AllocAudioA(0x0000_4200u,
		0x0000_4300u);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallSetSound()
	{
		AHI.SetSound(0x0000_4200u, 0, 1, 2, 256, 0x0000_4300u,
			(uint)AhiSetFlags.Immediate);
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallPlay()
	{
		AHI.PlayA(0x0000_4200u, 0x0000_4300u, 0x0000_4400u);
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallLoadModeFile() => AHI.LoadModeFile(0x0000_4200u,
		"DEVS:AHI");

	[Fact]
	public void AhiReceivesItsDeviceBaseFromTheCaller()
	{
		var attribute = typeof(AHI).GetCustomAttribute<AmigaLibraryAttribute>();

		Assert.NotNull(attribute);
		Assert.Equal(AHI.Name, attribute.Name);
		Assert.Equal(AmigaLibraryBasePolicy.CallerProvided, attribute.BasePolicy);
		Assert.DoesNotContain(typeof(AHI).GetProperties(), property =>
			property.Name.Contains("Base", StringComparison.Ordinal));
	}

	public static IEnumerable<object[]> PublicVectors =>
	[
		Vector(nameof(AHI.AllocAudioA), -42, [M68kRegister.A6, M68kRegister.A1], M68kRegister.D0),
		Vector(nameof(AHI.FreeAudio), -48, [M68kRegister.A6, M68kRegister.A2]),
		Vector(nameof(AHI.KillAudio), -54, [M68kRegister.A6]),
		Vector(nameof(AHI.ControlAudioA), -60, [M68kRegister.A6, M68kRegister.A2, M68kRegister.A1], M68kRegister.D0),
		Vector(nameof(AHI.SetVol), -66, [M68kRegister.A6, M68kRegister.D0, M68kRegister.D1, M68kRegister.D2, M68kRegister.A2, M68kRegister.D3]),
		Vector(nameof(AHI.SetFreq), -72, [M68kRegister.A6, M68kRegister.D0, M68kRegister.D1, M68kRegister.A2, M68kRegister.D2]),
		Vector(nameof(AHI.SetSound), -78, [M68kRegister.A6, M68kRegister.D0, M68kRegister.D1, M68kRegister.D2, M68kRegister.D3, M68kRegister.A2, M68kRegister.D4]),
		Vector(nameof(AHI.SetEffect), -84, [M68kRegister.A6, M68kRegister.A0, M68kRegister.A2], M68kRegister.D0),
		Vector(nameof(AHI.LoadSound), -90, [M68kRegister.A6, M68kRegister.D0, M68kRegister.D1, M68kRegister.A0, M68kRegister.A2], M68kRegister.D0),
		Vector(nameof(AHI.UnloadSound), -96, [M68kRegister.A6, M68kRegister.D0, M68kRegister.A2]),
		Vector(nameof(AHI.NextAudioId), -102, [M68kRegister.A6, M68kRegister.D0], M68kRegister.D0),
		Vector(nameof(AHI.GetAudioAttrsA), -108, [M68kRegister.A6, M68kRegister.D0, M68kRegister.A2, M68kRegister.A1], M68kRegister.D0),
		Vector(nameof(AHI.BestAudioIdA), -114, [M68kRegister.A6, M68kRegister.A1], M68kRegister.D0),
		Vector(nameof(AHI.AllocAudioRequestA), -120, [M68kRegister.A6, M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(AHI.AudioRequestA), -126, [M68kRegister.A6, M68kRegister.A0, M68kRegister.A1], M68kRegister.D0),
		Vector(nameof(AHI.FreeAudioRequest), -132, [M68kRegister.A6, M68kRegister.A0]),
		Vector(nameof(AHI.PlayA), -138, [M68kRegister.A6, M68kRegister.A2, M68kRegister.A1]),
		Vector(nameof(AHI.SampleFrameSize), -144, [M68kRegister.A6, M68kRegister.D0], M68kRegister.D0),
		Vector(nameof(AHI.AddAudioMode), -150, [M68kRegister.A6, M68kRegister.A0], M68kRegister.D0),
		Vector(nameof(AHI.RemoveAudioMode), -156, [M68kRegister.A6, M68kRegister.D0], M68kRegister.D0),
		Vector(nameof(AHI.LoadModeFile), -162, [M68kRegister.A6, M68kRegister.A0], M68kRegister.D0),
	];

	[Theory]
	[MemberData(nameof(PublicVectors))]
	public void AhiVectorsUsePublishedM68kAbi(string methodName, int lvo,
		M68kRegister[] parameters, M68kRegister? result)
	{
		var method = typeof(AHI).GetMethod(methodName,
			BindingFlags.Public | BindingFlags.Static)!;

		Assert.Equal(lvo, method.GetCustomAttribute<AmigaLvoAttribute>()?.Offset);
		Assert.Equal(parameters, method.GetParameters().Select(parameter =>
			parameter.GetCustomAttribute<M68kRegisterAttribute>()!.Register));
		Assert.Equal(result,
			method.ReturnParameter.GetCustomAttribute<M68kRegisterAttribute>()?.Register);
	}

	[Theory]
	[InlineData(nameof(CallAllocAudio), "-42(a6)")]
	[InlineData(nameof(CallSetSound), "-78(a6)")]
	[InlineData(nameof(CallPlay), "-138(a6)")]
	[InlineData(nameof(CallLoadModeFile), "-162(a6)")]
	public void AhiCallsLowerThroughTheCallerProvidedDeviceBase(string methodName,
		string vector)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint = $"{typeof(AhiBindingTests).FullName}::{methodName}",
			OutputFormat = M68kOutputFormat.Assembly
		});

		Assert.Contains(vector, result.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void AhiPublicPrefixesHavePublishedLayoutsAndCodecs()
	{
		Assert.Equal((int)AHIAudioCtrl.Size, Marshal.SizeOf<AHIAudioCtrl>());
		Assert.Equal((int)AHISoundMessage.Size, Marshal.SizeOf<AHISoundMessage>());
		Assert.Equal((int)AHIRecordMessage.Size, Marshal.SizeOf<AHIRecordMessage>());
		Assert.Equal((int)AHISampleInfo.Size, Marshal.SizeOf<AHISampleInfo>());
		Assert.Equal((int)AHIAudioModeRequester.Size,
			Marshal.SizeOf<AHIAudioModeRequester>());
		Assert.Equal(AHILayout.AudioCtrl.UserData,
			Marshal.OffsetOf<AHIAudioCtrl>(nameof(AHIAudioCtrl.UserData)).ToInt32());
		Assert.Equal(AHILayout.SoundMessage.Channel,
			Marshal.OffsetOf<AHISoundMessage>(nameof(AHISoundMessage.Channel)).ToInt32());
		Assert.Equal(AHILayout.RecordMessage.Type,
			Marshal.OffsetOf<AHIRecordMessage>(nameof(AHIRecordMessage.Type)).ToInt32());
		Assert.Equal(AHILayout.RecordMessage.Buffer,
			Marshal.OffsetOf<AHIRecordMessage>(nameof(AHIRecordMessage.Buffer)).ToInt32());
		Assert.Equal(AHILayout.RecordMessage.Length,
			Marshal.OffsetOf<AHIRecordMessage>(nameof(AHIRecordMessage.Length)).ToInt32());
		Assert.Equal(AHILayout.SampleInfo.Type,
			Marshal.OffsetOf<AHISampleInfo>(nameof(AHISampleInfo.Type)).ToInt32());
		Assert.Equal(AHILayout.SampleInfo.Address,
			Marshal.OffsetOf<AHISampleInfo>(nameof(AHISampleInfo.Address)).ToInt32());
		Assert.Equal(AHILayout.SampleInfo.Length,
			Marshal.OffsetOf<AHISampleInfo>(nameof(AHISampleInfo.Length)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.AudioId,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.AudioId)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.MixFrequency,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.MixFrequency)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.LeftEdge,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.LeftEdge)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.TopEdge,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.TopEdge)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.Width,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.Width)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.Height,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.Height)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.InfoOpened,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.InfoOpened)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.InfoLeftEdge,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.InfoLeftEdge)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.InfoTopEdge,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.InfoTopEdge)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.InfoWidth,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.InfoWidth)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.InfoHeight,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.InfoHeight)).ToInt32());
		Assert.Equal(AHILayout.AudioModeRequester.UserData,
			Marshal.OffsetOf<AHIAudioModeRequester>(nameof(AHIAudioModeRequester.UserData)).ToInt32());

		var memory = new Memory(160);
		var control = APTR.FromPointer(8);
		var requester = APTR.FromPointer(32);
		var sample = APTR.FromPointer(72);
		var sound = APTR.FromPointer(96);
		var record = APTR.FromPointer(104);
		AHIAudioCtrlCodec.Write(ref memory, control, new AHIAudioCtrl
		{
			UserData = APTR.FromPointer(0x1122_3344),
		});
		AHIAudioModeRequesterCodec.Write(ref memory, requester,
			new AHIAudioModeRequester
			{
				AudioId = 7, MixFrequency = 44_100, LeftEdge = -4, TopEdge = 5,
				Width = 320, Height = 200, InfoOpened = 1, InfoLeftEdge = -8,
				InfoTopEdge = 9, InfoWidth = 160, InfoHeight = 100,
				UserData = APTR.FromPointer(0x5566_7788),
			});
		AHISampleInfoCodec.Write(ref memory, sample, new AHISampleInfo
		{
			Type = (uint)AhiSampleType.Stereo16Signed,
			Address = APTR.FromPointer(0x99AA_BBCC), Length = 123,
		});
		AHISoundMessageCodec.Write(ref memory, sound, new AHISoundMessage { Channel = 2 });
		AHIRecordMessageCodec.Write(ref memory, record, new AHIRecordMessage
		{
			Type = (uint)AhiSampleType.Mono16Signed,
			Buffer = APTR.FromPointer(0xDDEE_F001), Length = 456,
		});

		Assert.Equal(0x1122_3344u, AHIAudioCtrlCodec.Read(ref memory, control).UserData.Raw);
		var actualRequester = AHIAudioModeRequesterCodec.Read(ref memory, requester);
		Assert.Equal(44_100u, actualRequester.MixFrequency);
		Assert.Equal((short)-8, actualRequester.InfoLeftEdge);
		Assert.Equal(0x5566_7788u, actualRequester.UserData.Raw);
		var actualSample = AHISampleInfoCodec.Read(ref memory, sample);
		Assert.Equal(0x99AA_BBCCu, actualSample.Address.Raw);
		Assert.Equal(123u, actualSample.Length);
		Assert.Equal((ushort)2, AHISoundMessageCodec.Read(ref memory, sound).Channel);
		var actualRecord = AHIRecordMessageCodec.Read(ref memory, record);
		Assert.Equal(0xDDEE_F001u, actualRecord.Buffer.Raw);
		Assert.Equal(456u, actualRecord.Length);
	}

	private static object[] Vector(string methodName, int lvo,
		M68kRegister[] parameters, M68kRegister? result = null) =>
		[methodName, lvo, parameters, result];

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
