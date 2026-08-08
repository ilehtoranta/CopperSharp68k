/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum WorkbenchObjectType : ushort
{
	Disk = 1,
	Drawer = 2,
	Tool = 3,
	Project = 4,
	Garbage = 5,
	Device = 6,
	Kick = 7,
	AppIcon = 8,
}

public enum DrawerViewMode : ushort
{
	ByDefault = 0,
	ByIcon = 1,
	ByName = 2,
	ByDate = 3,
	BySize = 4,
	ByType = 5,
}

public enum WorkbenchSetupCleanupState : int
{
	TryCleanup = 0,
	Cleanup = 1,
	Setup = 2,
}

public enum AppMessageType : ushort
{
	AppWindow = 7,
	AppIcon = 8,
	AppMenuItem = 9,
	AppWindowZone = 10,
}

public enum AppWindowDropZoneAction : int
{
	Enter = 0,
	Leave = 1,
}

public enum IconSelectionAction : int
{
	Unselect = 0,
	Select = 1,
	Ignore = 2,
	Stop = 3,
}

public enum WorkbenchCopyAction : int
{
	Begin = 0,
	Copy = 1,
	End = 2,
}

public enum WorkbenchDeleteAction : int
{
	BeginDiscard = 0,
	BeginEmptyTrash = 1,
	DeleteContents = 3,
	DeleteObject = 4,
	End = 5,
}

public enum WorkbenchTextInputAction : int
{
	Rename = 0,
	RelabelVolume = 1,
	NewDrawer = 2,
	Execute = 3,
}

public enum WorkbenchConstants : int
{
	DiskMagic = unchecked((int)0xe310),
	DiskVersion = 1,
	DiskRevision = 1,
	NoIconPosition = unchecked((int)0x80000000),
}
