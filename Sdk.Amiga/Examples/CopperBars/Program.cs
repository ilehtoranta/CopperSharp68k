/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using Amiga.Hardware;
using CopperSharp.Compiler;

namespace CopperBarsExample;

public static class Program
{
	private const int GraphicsBaseActiveView = 34;
	private const int GraphicsBaseCopperInit = 38;

	private const ushort CopperEnd1 = 0xFFFF;
	private const ushort CopperEnd2 = 0xFFFE;
	private const ushort WaitMask = 0xFFFE;
	private const ushort CopperVerticalWrapWait = 0xFFDF;
	private const ushort SetClear = 0x8000;
	private const ushort BackgroundColor = 0x001;
	private const int CopperUpdateLine = 280;

	private const int BarCount = 4;
	private const int BarHeight = 8;
	private const int BarSpacing = 24;
	private const int FirstBarWaitOffset = 8;
	private const int CopperBytesPerBar = (BarHeight + 1) * 8;
	private const int CopperInstructionCount = 2 + (BarCount * (BarHeight + 1) * 2) + 4;
	private const uint CopperListSize = CopperInstructionCount * 4;

	[M68kEntryPoint]
	public static int Main()
	{
		var graphicsBase = Exec.OpenLibrary(Graphics.Name, 0);
		if (!graphicsBase.HasValue)
		{
			return 20;
		}

		Graphics.GraphicsLibraryBase = graphicsBase.Value;
		var copperListPointer = Exec.AllocMem(
			CopperListSize,
			Exec.MemoryFlags.Chip | Exec.MemoryFlags.Clear);
		if (copperListPointer == 0)
		{
			Exec.CloseLibrary(Graphics.GraphicsLibraryBase);
			Graphics.GraphicsLibraryBase = APTR.Null;
			return 20;
		}

		WaitForLeftMouseRelease();
		var activeView = APTR.ReadUInt32(Graphics.GraphicsLibraryBase, GraphicsBaseActiveView);
		var systemCopperList = APTR.ReadUInt32(Graphics.GraphicsLibraryBase, GraphicsBaseCopperInit);
		Graphics.LoadView(0);
		Graphics.WaitTOF();
		Graphics.WaitTOF();
		Graphics.OwnBlitter();
		Graphics.WaitBlit();

		Exec.Forbid();
		Exec.Disable();
		RunDemo(APTR.FromPointer(copperListPointer), systemCopperList);
		Exec.Enable();
		Exec.Permit();

		Graphics.DisownBlitter();
		Graphics.LoadView(activeView);
		Graphics.WaitTOF();
		Graphics.WaitTOF();

		Exec.FreeMem(copperListPointer, CopperListSize);
		Exec.CloseLibrary(Graphics.GraphicsLibraryBase);
		Graphics.GraphicsLibraryBase = APTR.Null;
		return 0;
	}

	// This whole method executes with task switching and OS interrupts disabled.
	// It intentionally uses only CopperSharp raw-memory intrinsics and local code.
	private static void RunDemo(APTR copperList, uint systemCopperList)
	{
		var oldDma = CustomChip.ReadDmaControl() & DmaControlFlags.WritableMask;
		var oldInterruptEnable = CustomChip.ReadInterruptEnable() & CustomInterruptFlags.All;
		var oldInterruptRequest = CustomChip.ReadInterruptRequest() & CustomInterruptFlags.All;
		var oldAudioDiskControl = CustomChip.ReadAudioDiskControl() & AudioDiskControlFlags.All;

		CustomChip.ClearInterruptEnable(CustomInterruptFlags.All);
		CustomChip.ClearInterruptRequest(CustomInterruptFlags.All);
		CustomChip.ClearDma(DmaControlFlags.WritableMask);

		BuildCopperList(copperList, 48);
		CustomChip.SetCopper1Pointer(copperList.Raw);
		CustomChip.SetDma(DmaControlFlags.Master | DmaControlFlags.Copper);

		// Keep these initializations after the setup calls. This also makes their
		// first live values explicit in the generated loop register allocation.
		var top = 48;
		var direction = 1;
		while (!LeftMousePressed())
		{
			WaitForCopperUpdateWindow();
			UpdateCopperWaits(copperList, top);

			top += direction;
			if (top >= 140)
			{
				direction = -1;
			}
			else if (top <= 48)
			{
				direction = 1;
			}
		}

		CustomChip.ClearDma(DmaControlFlags.WritableMask);
		CustomChip.SetCopper1Pointer(systemCopperList);
		CustomChip.StrobeCopper1();
		CustomChip.ClearAudioDiskControl(AudioDiskControlFlags.All);
		CustomChip.SetAudioDiskControl(oldAudioDiskControl);
		CustomChip.ClearInterruptRequest(CustomInterruptFlags.All);
		CustomChip.SetInterruptRequest(oldInterruptRequest);
		CustomChip.SetDma(oldDma);
		CustomChip.SetInterruptEnable(oldInterruptEnable);
	}

	private static void BuildCopperList(APTR list, int top)
	{
		var offset = 0;
		WriteCopperInstruction(list, ref offset, CustomRegister.BitplaneControl0, 0);
		WriteCopperInstruction(list, ref offset, CustomRegister.Color00, BackgroundColor);

		for (var bar = 0; bar < BarCount; bar++)
		{
			var y = top + (bar * BarSpacing);
			for (var line = 0; line < BarHeight; line++)
			{
				WriteCopperWait(list, ref offset, y + line);
				WriteCopperInstruction(list, ref offset, CustomRegister.Color00, BarColor(bar, line));
			}

			WriteCopperWait(list, ref offset, y + BarHeight);
			WriteCopperInstruction(list, ref offset, CustomRegister.Color00, BackgroundColor);
		}

		// The Copper compares only eight vertical-position bits. Cross physical
		// line 255 before waiting for the low eight bits of PAL line 280.
		WriteCopperInstruction(list, ref offset, CopperVerticalWrapWait, WaitMask);
		WriteCopperWait(list, ref offset, CopperUpdateLine);
		WriteCopperInstruction(
			list,
			ref offset,
			CustomRegister.InterruptRequest,
			(ushort)(SetClear | (ushort)CustomInterruptFlags.Copper));
		WriteCopperInstruction(list, ref offset, CopperEnd1, CopperEnd2);
	}

	private static void UpdateCopperWaits(APTR list, int top)
	{
		var offset = FirstBarWaitOffset;
		var y = top;
		for (var bar = 0; bar < BarCount; bar++)
		{
			UpdateBarWaits(list, offset, y);
			offset += CopperBytesPerBar;
			y += BarSpacing;
		}
	}

	private static void UpdateBarWaits(APTR list, int offset, int y)
	{
		for (var line = 0; line <= BarHeight; line++)
		{
			APTR.WriteUInt16(list, offset + (line * 8), CopperWait(y + line));
		}
	}

	private static ushort CopperWait(int y)
	{
		return (ushort)((y << 8) | 1);
	}

	private static void WriteCopperWait(APTR list, ref int offset, int y)
	{
		WriteCopperInstruction(list, ref offset, CopperWait(y), WaitMask);
	}

	private static void WriteCopperInstruction(APTR list, ref int offset, ushort first, ushort second)
	{
		APTR.WriteUInt16(list, offset, first);
		APTR.WriteUInt16(list, offset + 2, second);
		offset += 4;
	}

	private static ushort BarColor(int bar, int line)
	{
		var level = 15;
		if (line == 0 || line == 7)
		{
			level = 3;
		}
		else if (line == 1 || line == 6)
		{
			level = 7;
		}
		else if (line == 2 || line == 5)
		{
			level = 11;
		}

		if (bar == 0)
		{
			return (ushort)(level << 8);
		}
		if (bar == 1)
		{
			return (ushort)(level * 0x110);
		}
		if (bar == 2)
		{
			return (ushort)(level << 4);
		}

		return (ushort)level;
	}

	private static void WaitForCopperUpdateWindow()
	{
		while ((CustomChip.ReadInterruptRequest() & CustomInterruptFlags.Copper) == 0)
		{
		}
		CustomChip.ClearInterruptRequest(CustomInterruptFlags.Copper);
	}

	private static void WaitForLeftMouseRelease()
	{
		while (LeftMousePressed())
		{
		}
	}

	private static bool LeftMousePressed()
	{
		return CiaA.IsLeftMouseButtonPressed();
	}
}
