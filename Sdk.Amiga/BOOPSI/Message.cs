/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static partial class BOOPSI
{
	public struct Message
	{
		public uint MethodID;

		public static APTR AddressOf(ref Message message) =>
			throw new System.NotSupportedException(
				"BOOPSI.Message.AddressOf is lowered by CopperSharp.");
	}
}
