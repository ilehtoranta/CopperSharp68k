/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

namespace Amiga.MUI;

public static class MUIValueHelpers
{
	public static uint MUIV_PushMethod_Delay(uint millis)
	{
		uint shifted = millis << 8;
		return shifted < 0x0fff_fff0u ? shifted : 0x0fff_fff0u;
	}

	public static int MUIV_Window_AltHeight_MinMax(int p) => 0 - p;
	public static int MUIV_Window_AltHeight_Visible(int p) => -100 - p;
	public static int MUIV_Window_AltHeight_Screen(int p) => -200 - p;
	public static int MUIV_Window_AltTopEdge_Delta(int p) => -3 - p;
	public static int MUIV_Window_AltWidth_MinMax(int p) => 0 - p;
	public static int MUIV_Window_AltWidth_Visible(int p) => -100 - p;
	public static int MUIV_Window_AltWidth_Screen(int p) => -200 - p;
	public static int MUIV_Window_Height_MinMax(int p) => 0 - p;
	public static int MUIV_Window_Height_Visible(int p) => -100 - p;
	public static int MUIV_Window_Height_Screen(int p) => -200 - p;
	public static int MUIV_Window_LeftEdge_Right(int n) => -1000 - n;
	public static int MUIV_Window_TopEdge_Delta(int p) => -3 - p;
	public static int MUIV_Window_TopEdge_Bottom(int n) => -1000 - n;
	public static int MUIV_Window_Width_MinMax(int p) => 0 - p;
	public static int MUIV_Window_Width_Visible(int p) => -100 - p;
	public static int MUIV_Window_Width_Screen(int p) => -200 - p;
	public static int MUIV_Group_Spacing_Percent(int p) => -p;
	public static int MUIV_Lamp_Type_Size(int px) => px < 5 ? 0 : px - 5;
}
