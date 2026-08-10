/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>
/// Allocation-free handle for the OCS/ECS custom-register block. Read aliases,
/// write aliases, and strobe operations are deliberately separate methods.
/// </summary>
public readonly struct CustomChip
{
	private const uint BaseAddress = 0x00DFF000;
	private const ushort SetClear = 0x8000;

	private const int DmaControlRead = 0x002;
	private const int VerticalPositionRead = 0x004;
	private const int VerticalHorizontalPositionRead = 0x006;
	private const int AudioDiskControlRead = 0x010;
	private const int InterruptEnableRead = 0x01C;
	private const int InterruptRequestRead = 0x01E;
	private const int Copper1Pointer = 0x080;
	private const int Copper1Jump = 0x088;
	private const int DmaControl = 0x096;
	private const int InterruptEnable = 0x09A;
	private const int InterruptRequest = 0x09C;
	private const int AudioDiskControl = 0x09E;

	public static DmaControlFlags ReadDmaControl() =>
		(DmaControlFlags)APTR.ReadUInt16(APTR.FromPointer(BaseAddress), DmaControlRead);

	public static CustomInterruptFlags ReadInterruptEnable() =>
		(CustomInterruptFlags)APTR.ReadUInt16(APTR.FromPointer(BaseAddress), InterruptEnableRead);

	public static CustomInterruptFlags ReadInterruptRequest() =>
		(CustomInterruptFlags)APTR.ReadUInt16(APTR.FromPointer(BaseAddress), InterruptRequestRead);

	public static AudioDiskControlFlags ReadAudioDiskControl() =>
		(AudioDiskControlFlags)APTR.ReadUInt16(APTR.FromPointer(BaseAddress), AudioDiskControlRead);

	public static int ReadBeamLine()
	{
		var registers = APTR.FromPointer(BaseAddress);
		var high = (APTR.ReadUInt16(registers, VerticalPositionRead) & 1) << 8;
		var low = APTR.ReadUInt16(registers, VerticalHorizontalPositionRead) >> 8;
		return high | low;
	}

	public static void SetCopper1Pointer(uint address) =>
		APTR.WriteUInt32(APTR.FromPointer(BaseAddress), Copper1Pointer, address);

	public static void StrobeCopper1() =>
		APTR.WriteUInt16(APTR.FromPointer(BaseAddress), Copper1Jump, 0);

	public static void ClearDma(DmaControlFlags flags) =>
		APTR.WriteUInt16(
			APTR.FromPointer(BaseAddress),
			DmaControl,
			(ushort)(flags & DmaControlFlags.WritableMask));

	public static void SetDma(DmaControlFlags flags) =>
		APTR.WriteUInt16(
			APTR.FromPointer(BaseAddress),
			DmaControl,
			(ushort)(SetClear | (ushort)(flags & DmaControlFlags.WritableMask)));

	public static void ClearInterruptEnable(CustomInterruptFlags flags) =>
		APTR.WriteUInt16(
			APTR.FromPointer(BaseAddress),
			InterruptEnable,
			(ushort)(flags & CustomInterruptFlags.All));

	public static void SetInterruptEnable(CustomInterruptFlags flags) =>
		APTR.WriteUInt16(
			APTR.FromPointer(BaseAddress),
			InterruptEnable,
			(ushort)(SetClear | (ushort)(flags & CustomInterruptFlags.All)));

	public static void ClearInterruptRequest(CustomInterruptFlags flags) =>
		APTR.WriteUInt16(
			APTR.FromPointer(BaseAddress),
			InterruptRequest,
			(ushort)(flags & CustomInterruptFlags.All));

	public static void SetInterruptRequest(CustomInterruptFlags flags) =>
		APTR.WriteUInt16(
			APTR.FromPointer(BaseAddress),
			InterruptRequest,
			(ushort)(SetClear | (ushort)(flags & CustomInterruptFlags.All)));

	public static void ClearAudioDiskControl(AudioDiskControlFlags flags) =>
		APTR.WriteUInt16(
			APTR.FromPointer(BaseAddress),
			AudioDiskControl,
			(ushort)(flags & AudioDiskControlFlags.All));

	public static void SetAudioDiskControl(AudioDiskControlFlags flags) =>
		APTR.WriteUInt16(
			APTR.FromPointer(BaseAddress),
			AudioDiskControl,
			(ushort)(SetClear | (ushort)(flags & AudioDiskControlFlags.All)));
}
