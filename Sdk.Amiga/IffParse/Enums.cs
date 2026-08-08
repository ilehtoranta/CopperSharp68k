/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum IffFlags : uint
{
	Read = 0,
	Write = 1,
	ForwardSeek = 1u << 1,
	RandomSeek = 1u << 2,
}

public enum IffParseMode : int
{
	Scan = 0,
	Step = 1,
	RawStep = 2,
}

public enum IffStreamCommand : int
{
	Init = 0,
	Cleanup = 1,
	Read = 2,
	Write = 3,
	Seek = 4,
	Entry = 5,
	Exit = 6,
	PurgeLocalContextItem = 7,
}

public enum IffLocalItemScope : int
{
	Root = 1,
	Top = 2,
	Property = 3,
}

public enum IffConstants : uint
{
	Form = 0x464f524d,
	List = 0x4c495354,
	Cat = 0x43415420,
	Prop = 0x50524f50,
	Null = 0x20202020,
	PropertyLocalItem = 0x70726f70,
	CollectionLocalItem = 0x636f6c6c,
	EntryHandlerLocalItem = 0x656e6864,
	ExitHandlerLocalItem = 0x65786864,
}
