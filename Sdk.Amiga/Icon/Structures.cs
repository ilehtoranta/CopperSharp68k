/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IconIdentifyMessage
{
	public const uint Size = 36;

	public APTR SysBase;
	public APTR DosBase;
	public APTR UtilityBase;
	public APTR IconBase;
	public BPTR FileLock;
	public BPTR ParentLock;
	public APTR FileInfoBlock;
	public BPTR FileHandle;
	public APTR Tags;
}
