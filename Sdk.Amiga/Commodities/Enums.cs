/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum CommoditiesObjectType : int
{
	Invalid = 0,
	Filter = 1,
	TypeFilter = 2,
	Send = 3,
	Signal = 4,
	Translate = 5,
	Broker = 6,
	Debug = 7,
	Custom = 8,
	Zero = 9,
}

[System.Flags]
public enum CommoditiesMessageType : int
{
	InputEvent = 1 << 5,
	Command = 1 << 6,
}

public enum CommoditiesBrokerUniqueness : short
{
	Duplicate = 0,
	Unique = 1,
	Notify = 2,
}

[System.Flags]
public enum CommoditiesBrokerFlags : short
{
	ShowHide = 4,
}

[System.Flags]
public enum InputXpressionSynonyms : ushort
{
	Shift = 1,
	Caps = 2,
	Alt = 4,
}

public enum CommoditiesError : int
{
	Ok = 0,
	SystemError = 1,
	Duplicate = 2,
	Version = 3,
}

[System.Flags]
public enum CommoditiesObjectError : int
{
	Null = 1,
	NullAttach = 2,
	BadFilter = 4,
	BadType = 8,
}
