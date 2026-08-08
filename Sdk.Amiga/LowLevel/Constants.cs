/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static class LowLevelConstants
{
	public const uint TagUser = 0x80000000u;
	public const uint SetJoyPortAttributesTagBase = TagUser + 0x00c00100u;
	public const uint SetJoyPortType = SetJoyPortAttributesTagBase + 1;
	public const uint SetJoyPortReinitialize = SetJoyPortAttributesTagBase + 2;
	public const uint SystemControlTagBase = TagUser + 0x00c00000u;
	public const uint SystemControlTakeOver = SystemControlTagBase;
	public const uint SystemControlKillRequest = SystemControlTagBase + 1;
	public const uint SystemControlCdReboot = SystemControlTagBase + 2;
	public const uint SystemControlStopInput = SystemControlTagBase + 3;
	public const uint SystemControlAddCreateKeys = SystemControlTagBase + 4;
	public const uint SystemControlRemoveCreateKeys = SystemControlTagBase + 5;

	public const uint JoyPortTypeMask = 15u << 28;
	public const uint JoystickButtonMask = (1u << 23) | (1u << 22) | (1u << 21) |
		(1u << 20) | (1u << 19) | (1u << 18) | (1u << 17);
	public const uint JoystickDirectionMask = 0x0f;
	public const uint MouseHorizontalMask = 0xff;
	public const uint MouseVerticalMask = 0xff00;
	public const uint MouseMask = MouseHorizontalMask | MouseVerticalMask;

	public const uint CdRebootOff = 0;
	public const uint CdRebootOn = 1;
	public const uint CdRebootDefault = 2;

	public const ushort Port0ButtonBlue = 0x72;
	public const ushort Port0ButtonRed = 0x78;
	public const ushort Port0ButtonYellow = 0x77;
	public const ushort Port0ButtonGreen = 0x76;
	public const ushort Port0ButtonForward = 0x75;
	public const ushort Port0ButtonReverse = 0x74;
	public const ushort Port0ButtonPlay = 0x73;
	public const ushort Port0JoyUp = 0x79;
	public const ushort Port0JoyDown = 0x7a;
	public const ushort Port0JoyLeft = 0x7c;
	public const ushort Port0JoyRight = 0x7b;

	public const ushort Port1ButtonBlue = 0x172;
	public const ushort Port1ButtonRed = 0x178;
	public const ushort Port1ButtonYellow = 0x177;
	public const ushort Port1ButtonGreen = 0x176;
	public const ushort Port1ButtonForward = 0x175;
	public const ushort Port1ButtonReverse = 0x174;
	public const ushort Port1ButtonPlay = 0x173;
	public const ushort Port1JoyUp = 0x179;
	public const ushort Port1JoyDown = 0x17a;
	public const ushort Port1JoyLeft = 0x17c;
	public const ushort Port1JoyRight = 0x17b;

	public const ushort Port2ButtonBlue = 0x272;
	public const ushort Port2ButtonRed = 0x278;
	public const ushort Port2ButtonYellow = 0x277;
	public const ushort Port2ButtonGreen = 0x276;
	public const ushort Port2ButtonForward = 0x275;
	public const ushort Port2ButtonReverse = 0x274;
	public const ushort Port2ButtonPlay = 0x273;
	public const ushort Port2JoyUp = 0x279;
	public const ushort Port2JoyDown = 0x27a;
	public const ushort Port2JoyLeft = 0x27c;
	public const ushort Port2JoyRight = 0x27b;

	public const ushort Port3ButtonBlue = 0x372;
	public const ushort Port3ButtonRed = 0x378;
	public const ushort Port3ButtonYellow = 0x377;
	public const ushort Port3ButtonGreen = 0x376;
	public const ushort Port3ButtonForward = 0x375;
	public const ushort Port3ButtonReverse = 0x374;
	public const ushort Port3ButtonPlay = 0x373;
	public const ushort Port3JoyUp = 0x379;
	public const ushort Port3JoyDown = 0x37a;
	public const ushort Port3JoyLeft = 0x37c;
	public const ushort Port3JoyRight = 0x37b;
}
