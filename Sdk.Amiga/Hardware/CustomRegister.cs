/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>Custom-register selectors used in Copper MOVE instructions.</summary>
public static class CustomRegister
{
	public const ushort BitplaneControl0 = (ushort)CustomWriteRegister.BitplaneControl0;
	public const ushort InterruptRequest = (ushort)CustomWriteRegister.InterruptRequest;
	public const ushort Color00 = (ushort)CustomWriteRegister.Color00;
}
