/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum SysExFilterMode : byte
{
	Off = 0x00,
	OneByte = 0x00,
	ThreeByte = 0x04,
}

public enum MidiController : byte
{
	Bank = 0x00,
	ModWheel = 0x01,
	Breath = 0x02,
	Foot = 0x04,
	PortamentoTime = 0x05,
	DataEntry = 0x06,
	Volume = 0x07,
	Balance = 0x08,
	Pan = 0x0a,
	Expression = 0x0b,
	General1 = 0x10,
	General2 = 0x11,
	General3 = 0x12,
	General4 = 0x13,
	Sustain = 0x40,
	Portamento = 0x41,
	Sustenuto = 0x42,
	SoftPedal = 0x43,
	Hold2 = 0x45,
	General5 = 0x50,
	General6 = 0x51,
	General7 = 0x52,
	General8 = 0x53,
	ExternalEffectsDepth = 0x5b,
	TremoloDepth = 0x5c,
	ChorusDepth = 0x5d,
	CelesteDepth = 0x5e,
	PhaserDepth = 0x5f,
	DataIncrement = 0x60,
	DataDecrement = 0x61,
	NrpnLsb = 0x62,
	NrpnMsb = 0x63,
	RpnLsb = 0x64,
	RpnMsb = 0x65,
	Maximum = 0x78,
}

public enum MidiChannelMode : byte
{
	Minimum = 0x79,
	ResetControllers = 0x79,
	LocalControl = 0x7a,
	AllNotesOff = 0x7b,
	OmniModeOff = 0x7c,
	OmniModeOn = 0x7d,
	MonoMode = 0x7e,
	PolyMode = 0x7f,
}

public enum MidiRegisteredParameter : ushort
{
	PitchBendSensitivity = 0x0000,
	FineTune = 0x0001,
	CoarseTune = 0x0002,
}

public enum MidiTimeCodeQuarterFrame : byte
{
	FrameLow = 0x00,
	FrameHigh = 0x10,
	SecondLow = 0x20,
	SecondHigh = 0x30,
	MinuteLow = 0x40,
	MinuteHigh = 0x50,
	HourLow = 0x60,
	HourHigh = 0x70,
}

[System.Flags]
public enum MidiTimeCodeMasks : byte
{
	QuarterFrameType = 0x70,
	QuarterFrameData = 0x0f,
	HourType = 0x60,
	Hour = 0x1f,
}

public enum MidiTimeCodeType : byte
{
	TwentyFourFrames = 0x00,
	TwentyFiveFrames = 0x20,
	ThirtyDropFrames = 0x40,
	ThirtyNonDropFrames = 0x60,
}

public enum MidiManufacturer : byte
{
	XAmerica = 0x00,
	Sequential = 0x01,
	Idp = 0x02,
	OctavePlateau = 0x03,
	Moog = 0x04,
	Passport = 0x05,
	Lexicon = 0x06,
	Kurzweil = 0x07,
	Fender = 0x08,
	Gulbransen = 0x09,
	Akg = 0x0a,
	Voyce = 0x0b,
	Waveframe = 0x0c,
	Ada = 0x0d,
	Garfield = 0x0e,
	Ensoniq = 0x0f,
	Oberheim = 0x10,
	Apple = 0x11,
	GreyMatter = 0x12,
	PalmTree = 0x14,
	JlCooper = 0x15,
	Lowrey = 0x16,
	AdamsSmith = 0x17,
	Emu = 0x18,
	Harmony = 0x19,
	Art = 0x1a,
	Baldwin = 0x1b,
	Eventide = 0x1c,
	Inventronics = 0x1d,
	Clarity = 0x1f,
	Siel = 0x21,
	Synthaxe = 0x22,
	Hohner = 0x24,
	Twister = 0x25,
	Solton = 0x26,
	Jellinghaus = 0x27,
	Southworth = 0x28,
	Ppg = 0x29,
	Jen = 0x2a,
	Ssl = 0x2b,
	AudioVeritrieb = 0x2c,
	Elka = 0x2f,
	Dynacord = 0x30,
	Clavia = 0x33,
	Soundcraft = 0x39,
	Kawai = 0x40,
	Roland = 0x41,
	Korg = 0x42,
	Yamaha = 0x43,
	Casio = 0x44,
	Kamiya = 0x46,
	Akai = 0x47,
	JapanVictor = 0x48,
	Mesosha = 0x49,
	Unc = 0x7d,
	Unrt = 0x7e,
	Urt = 0x7f,
}

public enum MidiExtendedManufacturer : uint
{
	DigitalMusic = 0x000007,
	Iota = 0x000008,
	Artisyn = 0x00000a,
	Ivl = 0x00000b,
	SouthernMusic = 0x00000c,
	LakeButler = 0x00000d,
	Dod = 0x000010,
	PerfectFret = 0x000014,
	Kat = 0x000015,
	Opcode = 0x000016,
	Rane = 0x000017,
	SpatialSound = 0x000018,
	Kmx = 0x000019,
	Brenell = 0x00001a,
	Peavey = 0x00001b,
	ThreeSixty = 0x00001c,
	Axxes = 0x000020,
	Cae = 0x000026,
	Cannon = 0x00002b,
	BlueSkyLogic = 0x00002e,
	Voce = 0x000031,
}

[System.Flags]
public enum CamdErrorFlags : uint
{
	MessageError = 1u << 0,
	BufferFull = 1u << 1,
	SysExFull = 1u << 2,
	ParseMemory = 1u << 3,
	ReceiveError = 1u << 4,
	ReceiveOverflow = 1u << 5,
	SysExTooBig = 1u << 6,
}

public static class CamdConstants
{
	public const byte StatusBits = 0xf0;
	public const byte ChannelBits = 0x0f;
	public const byte SystemStatus = 0xf0;
	public const byte RealtimeStatus = 0xf8;
	public const byte MinimumControllerMode = 0x79;
	public const byte MiddleC = 60;
	public const byte DefaultVelocity = 64;
	public const ushort PitchBendCenter = 0x2000;
	public const byte ClocksPerQuarterNote = 24;
	public const byte ClocksPerSixteenthNote = 6;
	public const byte MidiControllerCenter = 64;
	public const uint MaximumCamdErrorFlags = 0x7f;

	public static byte HighMidiByte(ushort word) => (byte)((word >> 7) & 0x7f);
	public static byte LowMidiByte(ushort word) => (byte)(word & 0x7f);
	public static ushort MidiWord(byte high, byte low) => (ushort)(((high & 0x7f) << 7) | (low & 0x7f));
	public static uint MakeExtendedManufacturerId(byte id0, byte id1, byte id2) =>
		(uint)(id0 << 16 | id1 << 8 | id2);
	public static uint PackSysExFilter0() => 0;
	public static uint PackSysExFilter1(byte id1) => (uint)(1 << 24 | id1 << 16);
	public static uint PackSysExFilter2(byte id1, byte id2) => (uint)(2 << 24 | id1 << 16 | id2 << 8);
	public static uint PackSysExFilter3(byte id1, byte id2, byte id3) =>
		(uint)(3 << 24 | id1 << 16 | id2 << 8 | id3);
}
