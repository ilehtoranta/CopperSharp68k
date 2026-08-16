/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using Amiga.MUI;
using Xunit;

namespace CopperSharp.Compiler.Tests;

public sealed class MuiValueHelperTests
{
	[Fact]
	public void PushMethodDelayUsesExactCUnsignedShiftAndClampSemantics()
	{
		Assert.Equal(0u, MUIValueHelpers.MUIV_PushMethod_Delay(0));
		Assert.Equal(0x0001_2300u, MUIValueHelpers.MUIV_PushMethod_Delay(0x123));
		Assert.Equal(0x0fff_fff0u, MUIValueHelpers.MUIV_PushMethod_Delay(0x0010_0000));
		Assert.Equal(0u, MUIValueHelpers.MUIV_PushMethod_Delay(0x0100_0000));
	}

	[Fact]
	public void WindowDimensionHelpersUseExactSignedEncodings()
	{
		Assert.Equal(-7, MUIValueHelpers.MUIV_Window_AltHeight_MinMax(7));
		Assert.Equal(-107, MUIValueHelpers.MUIV_Window_AltHeight_Visible(7));
		Assert.Equal(-207, MUIValueHelpers.MUIV_Window_AltHeight_Screen(7));
		Assert.Equal(-7, MUIValueHelpers.MUIV_Window_AltTopEdge_Delta(4));
		Assert.Equal(-7, MUIValueHelpers.MUIV_Window_AltWidth_MinMax(7));
		Assert.Equal(-107, MUIValueHelpers.MUIV_Window_AltWidth_Visible(7));
		Assert.Equal(-207, MUIValueHelpers.MUIV_Window_AltWidth_Screen(7));
		Assert.Equal(-7, MUIValueHelpers.MUIV_Window_Height_MinMax(7));
		Assert.Equal(-107, MUIValueHelpers.MUIV_Window_Height_Visible(7));
		Assert.Equal(-207, MUIValueHelpers.MUIV_Window_Height_Screen(7));
		Assert.Equal(-1007, MUIValueHelpers.MUIV_Window_LeftEdge_Right(7));
		Assert.Equal(-7, MUIValueHelpers.MUIV_Window_TopEdge_Delta(4));
		Assert.Equal(-1007, MUIValueHelpers.MUIV_Window_TopEdge_Bottom(7));
		Assert.Equal(-7, MUIValueHelpers.MUIV_Window_Width_MinMax(7));
		Assert.Equal(-107, MUIValueHelpers.MUIV_Window_Width_Visible(7));
		Assert.Equal(-207, MUIValueHelpers.MUIV_Window_Width_Screen(7));
	}

	[Fact]
	public void GroupAndLampHelpersPreserveBoundarySemantics()
	{
		Assert.Equal(-37, MUIValueHelpers.MUIV_Group_Spacing_Percent(37));
		Assert.Equal(0, MUIValueHelpers.MUIV_Lamp_Type_Size(4));
		Assert.Equal(0, MUIValueHelpers.MUIV_Lamp_Type_Size(5));
		Assert.Equal(11, MUIValueHelpers.MUIV_Lamp_Type_Size(16));
	}
}
