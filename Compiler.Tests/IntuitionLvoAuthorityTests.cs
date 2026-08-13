using System.Reflection;
using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class IntuitionLvoAuthorityTests
{
	private const int VectorStride = 6;

	[Fact]
	public void PublishedAbiFactsDescribeTheClassicAndSelectedEnhancedDeclarations()
	{
		Assert.Equal((ushort)40, IntuitionAbiConstants.ClassicV40);
		Assert.Equal((ushort)50, IntuitionAbiConstants.MorphOsV50);
		Assert.Equal((ushort)51, IntuitionAbiConstants.MorphOsV51);
		Assert.Equal((ushort)0, IntuitionAbiConstants.UnverifiedVersion);
		Assert.False(IntuitionAbiConstants.EnhancedVectorVersionsVerified);
		Assert.Equal((short)-30, IntuitionAbiConstants.MorphOsFdFirstLvo);
		Assert.Equal((short)-1008, IntuitionAbiConstants.MorphOsFdLastLvo);
		Assert.Equal(164, IntuitionAbiConstants.MorphOsFdSlotCount);
		Assert.Equal(135, IntuitionAbiConstants.MorphOsFdPublicVectorCount);
		Assert.Equal(29, IntuitionAbiConstants.MorphOsFdPrivateSlotCount);
		Assert.Equal(134, IntuitionAbiConstants.MorphOsPrototypeVectorCount);

		Assert.Equal((short)-30, IntuitionAbiConstants.ClassicFirstLvo);
		Assert.Equal((short)-828, IntuitionAbiConstants.ClassicLastLvo);
		Assert.Equal(134, IntuitionAbiConstants.ClassicSlotCount);
		Assert.Equal(123, IntuitionAbiConstants.ClassicVectorCount);
		Assert.Equal(123, IntuitionAbiConstants.ClassicDeclaredVectorCount);
		Assert.Equal(124, IntuitionAbiConstants.ClassicPublicVectorCount);
		Assert.Equal(3, IntuitionAbiConstants.ClassicUndocumentedPublicVectorCount);
		Assert.Equal(4, IntuitionAbiConstants.ClassicPrivateSlotCount);
		Assert.Equal(6, IntuitionAbiConstants.ClassicReservedCount);

		Assert.Equal((short)-834, IntuitionAbiConstants.EnhancedFirstLvo);
		Assert.Equal(IntuitionLvo.GetSkinInfoAttrA,
			IntuitionAbiConstants.EnhancedFirstDeclaredLvo);
		Assert.Equal((short)-1008, IntuitionAbiConstants.EnhancedLastLvo);
		Assert.Equal(30, IntuitionAbiConstants.EnhancedSlotCount);
		Assert.Equal(11, IntuitionAbiConstants.EnhancedVectorCount);
		Assert.Equal(19, IntuitionAbiConstants.EnhancedPrivateSlotCount);
	}

	[Fact]
	public void ClassicRangePartitionsEverySlotIntoPublicPrivateOrReserved()
	{
		var declaredVectors = VectorConstants()
			.Where(pair => pair.Name != nameof(IntuitionLvo.AlohaWorkbench))
			.Where(pair => IsInRange(pair.Value,
				IntuitionAbiConstants.ClassicFirstLvo,
				IntuitionAbiConstants.ClassicLastLvo))
			.Select(pair => pair.Value)
			.ToHashSet();
		var publicVectors = VectorConstants()
			.Where(pair => IsInRange(pair.Value,
				IntuitionAbiConstants.ClassicFirstLvo,
				IntuitionAbiConstants.ClassicLastLvo))
			.Select(pair => pair.Value)
			.ToHashSet();
		var slots = Slots(
			IntuitionAbiConstants.ClassicFirstLvo,
			IntuitionAbiConstants.ClassicLastLvo,
			IntuitionAbiConstants.ClassicSlotCount);
		var privateSlots = slots.Where(IntuitionLvo.IsClassicPrivate).ToArray();
		var reserved = slots.Where(IntuitionLvo.IsClassicReserved).ToArray();

		Assert.Equal(IntuitionAbiConstants.ClassicDeclaredVectorCount, declaredVectors.Count);
		Assert.Equal(IntuitionAbiConstants.ClassicPublicVectorCount, publicVectors.Count);
		Assert.Equal(IntuitionAbiConstants.ClassicPrivateSlotCount, privateSlots.Length);
		Assert.Equal(IntuitionAbiConstants.ClassicReservedCount, reserved.Length);
		Assert.DoesNotContain(declaredVectors, IntuitionLvo.IsClassicPrivate);
		Assert.Equal(IntuitionAbiConstants.ClassicUndocumentedPublicVectorCount,
			publicVectors.Count(IntuitionLvo.IsClassicUndocumentedPublic));
		Assert.True(IntuitionLvo.IsClassicUndocumentedPublic(IntuitionLvo.OpenIntuition));
		Assert.True(IntuitionLvo.IsClassicUndocumentedPublic(IntuitionLvo.Intuition_));
		Assert.True(IntuitionLvo.IsClassicUndocumentedPublic(IntuitionLvo.AlohaWorkbench));
		Assert.All(slots, lvo =>
		{
			var classifications = (publicVectors.Contains(lvo) ? 1 : 0) +
				(IntuitionLvo.IsClassicPrivate(lvo) ? 1 : 0) +
				(IntuitionLvo.IsClassicReserved(lvo) ? 1 : 0);
			Assert.Equal(1, classifications);
		});
		Assert.Equal(IntuitionAbiConstants.ClassicLastLvo, slots[^1]);
	}

	[Fact]
	public void EnhancedRangePartitionsEveryMorphOsFdSlotIntoPublicOrPrivate()
	{
		var vectors = VectorConstants()
			.Where(pair => IsInRange(pair.Value,
				IntuitionAbiConstants.EnhancedFirstLvo,
				IntuitionAbiConstants.EnhancedLastLvo))
			.Select(pair => pair.Value)
			.ToHashSet();
		var slots = Slots(
			IntuitionAbiConstants.EnhancedFirstLvo,
			IntuitionAbiConstants.EnhancedLastLvo,
			IntuitionAbiConstants.EnhancedSlotCount);
		var privateSlots = slots.Where(IntuitionLvo.IsEnhancedPrivate).ToArray();

		Assert.Equal(IntuitionAbiConstants.EnhancedVectorCount, vectors.Count);
		Assert.Equal(IntuitionAbiConstants.EnhancedPrivateSlotCount, privateSlots.Length);
		Assert.All(slots, lvo => Assert.True(
			vectors.Contains(lvo) ^ IntuitionLvo.IsEnhancedPrivate(lvo),
			$"Enhanced slot {lvo} must be exactly one public vector or private FD slot."));
		Assert.Equal(IntuitionAbiConstants.EnhancedLastLvo, slots[^1]);
		Assert.False(IntuitionLvo.IsEnhancedPrivate(IntuitionLvo.GetSkinInfoAttrA));
	}

	[Fact]
	public void MorphOsFdRangePartitionsAllPublicNamesAndPrivateSlots()
	{
		var publicVectors = VectorConstants().Select(pair => pair.Value).ToHashSet();
		var slots = Slots(IntuitionAbiConstants.MorphOsFdFirstLvo,
			IntuitionAbiConstants.MorphOsFdLastLvo,
			IntuitionAbiConstants.MorphOsFdSlotCount);
		var privateSlots = slots.Where(IntuitionLvo.IsMorphOsPrivate).ToArray();

		Assert.Equal(IntuitionAbiConstants.MorphOsFdPublicVectorCount,
			publicVectors.Count);
		Assert.Equal(IntuitionAbiConstants.MorphOsFdPrivateSlotCount,
			privateSlots.Length);
		Assert.All(slots, lvo => Assert.True(
			publicVectors.Contains(lvo) ^ IntuitionLvo.IsMorphOsPrivate(lvo),
			$"M320 slot {lvo} must be exactly one public name or private slot."));
		Assert.Contains(IntuitionLvo.AlohaWorkbench, publicVectors);
	}

	[Fact]
	public void EveryIntuitionDeclarationUsesItsPublishedNamedLvo()
	{
		var constants = VectorConstants()
			.Where(pair => pair.Name != nameof(IntuitionLvo.AlohaWorkbench))
			.ToDictionary(pair => pair.Name);
		var methods = typeof(Intuition).GetMethods(
			BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
			.Where(method => !method.IsSpecialName)
			.ToArray();

		Assert.Equal(
			IntuitionAbiConstants.ClassicVectorCount +
			IntuitionAbiConstants.EnhancedVectorCount + 1,
			methods.Length);
		Assert.Equal(
			IntuitionAbiConstants.ClassicVectorCount +
			IntuitionAbiConstants.EnhancedVectorCount,
			constants.Count);
		Assert.DoesNotContain(methods,
			method => method.Name == nameof(IntuitionLvo.AlohaWorkbench));

		foreach (var method in methods)
		{
			var constantName = method.Name == nameof(Intuition.NewObject)
				? nameof(Intuition.NewObjectA)
				: method.Name;
			Assert.True(constants.TryGetValue(constantName, out var vector),
				$"Missing IntuitionLvo.{constantName} for Intuition.{method.Name}.");

			var attribute = method.GetCustomAttribute<AmigaLvoAttribute>();
			Assert.NotNull(attribute);
			Assert.Equal((int)vector.Value, attribute!.Offset);
		}

		foreach (var vector in constants.Values)
		{
			var method = Assert.Single(methods, candidate => candidate.Name == vector.Name);
			Assert.Equal((int)vector.Value,
				method.GetCustomAttribute<AmigaLvoAttribute>()!.Offset);
		}

		Assert.Equal(IntuitionLvo.NewObjectA,
			(short)typeof(Intuition).GetMethod(nameof(Intuition.NewObject))!
				.GetCustomAttribute<AmigaLvoAttribute>()!.Offset);
	}

	[Fact]
	public void EnhancedVersionFactsArePresentAndExplicitlyUnverified()
	{
		var enhancedNames = VectorConstants()
			.Where(pair => IsInRange(pair.Value,
				IntuitionAbiConstants.EnhancedFirstLvo,
				IntuitionAbiConstants.EnhancedLastLvo))
			.Select(pair => pair.Name)
			.OrderBy(name => name)
			.ToArray();
		var versions = typeof(IntuitionVectorVersion)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.IsLiteral && field.FieldType == typeof(ushort))
			.OrderBy(field => field.Name)
			.ToArray();

		Assert.Equal(IntuitionAbiConstants.EnhancedVectorCount, versions.Length);
		Assert.Equal(enhancedNames, versions.Select(field => field.Name));
		Assert.All(versions, field => Assert.Equal(
			IntuitionAbiConstants.UnverifiedVersion,
			(ushort)field.GetRawConstantValue()!));
	}

	[Fact]
	public void BuildSysRequestUsesPublishedAddressAndDataRegisters()
	{
		var method = typeof(Intuition).GetMethod(nameof(Intuition.BuildSysRequest))!;
		var registers = method.GetParameters()
			.Select(parameter =>
				parameter.GetCustomAttribute<M68kRegisterAttribute>()?.Register)
			.ToArray();

		Assert.Equal(new M68kRegister?[]
		{
			M68kRegister.A0,
			M68kRegister.A1,
			M68kRegister.A2,
			M68kRegister.A3,
			M68kRegister.D0,
			M68kRegister.D1,
			M68kRegister.D2,
		}, registers);
	}

	private static (string Name, short Value)[] VectorConstants() =>
		typeof(IntuitionLvo)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.IsLiteral && field.FieldType == typeof(short))
			.Select(field => (field.Name, (short)field.GetRawConstantValue()!))
			.ToArray();

	private static short[] Slots(short first, short last, int count)
	{
		var slots = Enumerable.Range(0, count)
			.Select(index => checked((short)(first - index * VectorStride)))
			.ToArray();
		Assert.Equal(last, slots[^1]);
		return slots;
	}

	private static bool IsInRange(short value, short first, short last) =>
		value <= first && value >= last;
}
