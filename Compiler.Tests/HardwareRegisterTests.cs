using Amiga.Hardware;

namespace CopperSharp.Compiler.Tests;

public sealed class HardwareRegisterTests
{
	[Fact]
	public void CustomRegisterMapsCoverOcsEcsAndAgaAperture()
	{
		Assert.Equal(35, Enum.GetNames<CustomReadRegister>().Length);
		Assert.Equal(205, Enum.GetNames<CustomWriteRegister>().Length);
		Assert.Equal(27, Enum.GetNames<CustomPointerRegister>().Length);
		Assert.Equal(6, Enum.GetNames<CustomStrobeRegister>().Length);
		Assert.Equal(3, Enum.GetNames<CustomReservedRegister>().Length);

		Assert.All(Enum.GetValues<CustomReadRegister>(), AssertCustomOffset);
		Assert.All(Enum.GetValues<CustomWriteRegister>(), AssertCustomOffset);
		Assert.All(Enum.GetValues<CustomPointerRegister>(), AssertCustomOffset);
		Assert.All(Enum.GetValues<CustomStrobeRegister>(), AssertCustomOffset);
		Assert.All(Enum.GetValues<CustomReservedRegister>(), AssertCustomOffset);
	}

	[Fact]
	public void ChipsetSpecificRegistersReportCorrectAvailability()
	{
		Assert.Equal(
			AmigaChipsetSupport.All,
			CustomRegisterCatalog.GetSupport(CustomWriteRegister.Color31));
		Assert.Equal(
			AmigaChipsetSupport.EcsAndAga,
			CustomRegisterCatalog.GetSupport(CustomWriteRegister.BeamControl));
		Assert.Equal(
			AmigaChipsetSupport.Aga,
			CustomRegisterCatalog.GetSupport(CustomWriteRegister.FetchMode));
		Assert.Equal(
			AmigaChipsetSupport.Aga,
			CustomRegisterCatalog.GetSupport(CustomPointerRegister.Bitplane8));
	}

	[Fact]
	public void CiaRegisterMapContainsEveryMos8520Register()
	{
		Assert.Equal(16, Enum.GetNames<CiaRegister>().Length);
		Assert.Equal(0, (byte)CiaRegister.PortA);
		Assert.Equal(0x0D, (byte)CiaRegister.InterruptControl);
		Assert.Equal(0x0F, (byte)CiaRegister.ControlB);
	}

	[Theory]
	[InlineData(AmigaModel.Amiga1000, AmigaChipset.Ocs)]
	[InlineData(AmigaModel.Amiga600, AmigaChipset.Ecs)]
	[InlineData(AmigaModel.Amiga1200, AmigaChipset.Aga)]
	[InlineData(AmigaModel.Amiga4000, AmigaChipset.Aga)]
	public void ModelsHaveExpectedDefaultChipset(AmigaModel model, AmigaChipset expected)
	{
		Assert.Equal(expected, AmigaHardware.GetDefaultChipset(model));
	}

	[Fact]
	public void ModelSpecificFeaturesAreNotAdvertisedUniversally()
	{
		var a1000 = AmigaHardware.GetFeatures(AmigaModel.Amiga1000);
		var a1200 = AmigaHardware.GetFeatures(AmigaModel.Amiga1200);
		var a4000t = AmigaHardware.GetFeatures(AmigaModel.Amiga4000T);
		var cd32 = AmigaHardware.GetFeatures(AmigaModel.Cd32);

		Assert.False(a1000.HasFlag(AmigaHardwareFeatures.Gayle));
		Assert.True(a1200.HasFlag(AmigaHardwareFeatures.Gayle));
		Assert.True(a4000t.HasFlag(AmigaHardwareFeatures.A4000TScsi));
		Assert.True(cd32.HasFlag(AmigaHardwareFeatures.Akiko));
	}

	[Fact]
	public void ModelSpecificRegisterAddressesMatchPhysicalBusLayout()
	{
		Assert.Equal(0x00DA0000u, GayleIde.A600A1200BaseAddress);
		Assert.Equal(0x00DD2020u, GayleIde.A4000BaseAddress);
		Assert.Equal(0x00DD3020u, GayleIde.A4000InterruptAddress);
		Assert.Equal(0x3000, (ushort)GayleRegister.CardConfiguration);
		Assert.Equal(0x0043, (ushort)MotherboardResourceRegister.RamseyRevision);
		Assert.Equal(0x38, (byte)AkikoRegister.ChunkyToPlanar);
	}

	private static void AssertCustomOffset<T>(T register) where T : struct, Enum
	{
		var offset = Convert.ToUInt16(register);
		Assert.InRange(offset, (ushort)0, (ushort)0x1FC);
		Assert.Equal(0, offset & 1);
	}
}
