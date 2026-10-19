/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class DecimalFormattingTests
{
	[Fact]
	public void DecimalStorageRequiresOptInAndVerifiedLogicalFields()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		using var ordinary = Open(pack, false);
		var type = new CilType(CilTypeKind.ValueType, 0, "System.Decimal");
		Assert.True(module.IsSupportedStructType(type));
		Assert.True(module.TryGetReferenceFreeStructLayout(type, "System.Private.CoreLib", out var layout));
		Assert.Equal(16, layout.Size);
		Assert.Equal(0u, layout.ReferenceBitmap);
		Assert.True(module.IsExperimentalDecimalFormattingLayout(layout));
		Assert.False(module.IsExperimentalDecimalFormattingLayout(layout with { Size = 8 }));
		Assert.False(module.IsExperimentalDecimalFormattingLayout(layout with { ReferenceBitmap = 1 }));
		Assert.False(module.IsExperimentalDecimalFormattingLayout(layout with { FieldOffsets = new Dictionary<System.Reflection.Metadata.FieldDefinitionHandle, int>() }));
		foreach (var (name, fieldType, offset) in new[] { ("_flags", "int", 0), ("_hi32", "uint", 4), ("_lo64", "ulong", 8) })
		{
			var field = module.ResolveManagedField("System.Private.CoreLib", "System.Decimal", name);
			Assert.Equal(fieldType, field.Type.DisplayName);
			Assert.Equal(offset, layout.FieldOffsets[field.Handle]);
		}
		Assert.False(ordinary.IsSupportedStructType(type));
		Assert.False(ordinary.TryGetReferenceFreeStructLayout(type, "System.Private.CoreLib", out _));
	}

	[Fact]
	public void BoxedDecimalJoinDispatchUsesTheVerifiedOverride()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(ShadowDecimalFormatting).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }));
		var caller = module.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinText::ToString");
		var call = Assert.Single(caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt));
		var declaration = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
		Assert.Empty(module.GetObjectJoinDispatchEntries(declaration));
		var layout = module.RegisterBoxedDispatchLayout(new CilType(CilTypeKind.ValueType, 0, "System.Decimal"), "System.Private.CoreLib");
		Assert.NotNull(layout);
		var entry = Assert.Single(module.GetObjectJoinDispatchEntries(declaration));
		Assert.Equal(layout.Identity, entry.Layout.Identity);
		Assert.Equal("System.Decimal::ToString", entry.Method.DisplayName);
		Assert.Equal("System.Private.CoreLib", entry.Method.ModuleName);
		Assert.Equal(entry.Method, module.TryGetVirtualImplementation(layout, declaration));
	}

	[Fact]
	public void DecimalJoinEnumeratorStoresReferencesSeparatelyFromTheSixteenByteCurrent()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		var factory = module.ResolveEntryPoint("CopperSharp.Runtime.ShadowDecimalJoinEnumeration::GetEnumerator");
		var layout = module.GetRuntimeTypeLayout(module.ResolveRuntimeTypeIdentity(factory.Signature.ReturnType, factory.ModuleName));
		// The delegated enumerator added after the inline decimal is also a collector root.
		Assert.Equal(44, layout.Size);
		Assert.Equal(259u, layout.ReferenceBitmap);
		Assert.Equal(new[] { 8, 12, 16, 20, 24, 40 }, layout.FieldOffsets.Values.Order());
	}

	[Fact]
	public void DecimalArrayEnumeratorCopiesEveryWordAndClearsItsCurrentOnExhaustionAndDispose()
	{
		var values = new[] { new decimal(0, 0, 0, true, 3), new decimal(1, 0, 0, false, 28), new decimal(-1, -1, -1, true, 28) };
		var enumerator = ShadowDecimalJoinEnumeration.GetEnumerator(values);
		Assert.Equal(decimal.GetBits(default), decimal.GetBits(enumerator.Current));
		foreach (var value in values)
		{
			Assert.True(enumerator.MoveNext());
			Assert.Equal(decimal.GetBits(value), decimal.GetBits(enumerator.Current));
		}
		Assert.False(enumerator.MoveNext());
		Assert.Equal(decimal.GetBits(default), decimal.GetBits(enumerator.Current));
		enumerator.Dispose();
		Assert.False(enumerator.MoveNext());
		Assert.Equal(decimal.GetBits(default), decimal.GetBits(enumerator.Current));
		var interrupted = ShadowDecimalJoinEnumeration.GetEnumerator(values);
		Assert.True(interrupted.MoveNext());
		interrupted.Dispose();
		Assert.False(interrupted.MoveNext());
		Assert.Equal(decimal.GetBits(default), decimal.GetBits(interrupted.Current));
	}

	[Fact]
	public void FacadeDecimalBoxTokenRetainsItsValueTypeKind()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(ShadowDecimalFormatting).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }));
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderDecimalSmokeEntry");
		var box = Assert.Single(caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Box));
		var type = module.ResolveTypeToken((int)box.Operand!, caller, box.Offset);
		Assert.Equal(CilTypeKind.ValueType, type.Kind);
		Assert.Equal("System.Decimal", type.DisplayName);
		Assert.True(module.IsSupportedStructType(type));
	}

	[Fact]
	public void DecimalRendererShimsResolveOnlyWithExperimentalOptIn()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		using var ordinary = Open(pack, false);
		foreach (var entry in new[] { "FormatDecimal", "TryFormatDecimal" })
		{
			var caller = module.ResolveEntryPoint("CopperSharp.Runtime.ShadowDecimalFormatting::" + entry);
			var raw = ordinary.ResolveEntryPoint("CopperSharp.Runtime.ShadowDecimalFormatting::" + entry);
			foreach (var (shim, corelib) in new[] { ("ParseFormatSpecifier", "ParseFormatSpecifier"), ("RenderCustom", "NumberToStringFormat<char>"), ("RenderStandard", "NumberToString<char>") })
			{
				var call = caller.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call && module.DescribeMethodTokenName((int)instruction.Operand!, caller, instruction.Offset) == shim);
				var target = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset);
				Assert.Equal("System.Number::" + corelib, target.Definition!.DisplayName);
				Assert.Equal("System.Private.CoreLib", target.Definition.ModuleName);
				Assert.Equal(FrameworkTypeInitializerPolicy.TargetOwned, target.FrameworkBinding!.TypeInitializerPolicy);
				Assert.True(target.FrameworkBinding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
				Assert.Equal("CopperSharp.Runtime.ShadowDecimalFormatting::" + shim,
					ordinary.ResolveMethodToken((int)call.Operand!, raw, call.Offset).Definition!.DisplayName);
			}
		}
	}

	[Fact]
	public void DecimalDigitExtractionPreservesEveryScaleSignAndSignificandWord()
	{
		var info = new NumberFormatInfo { NegativeSign = "minus", NumberDecimalSeparator = ":", NumberGroupSeparator = "_", CurrencySymbol = "coins" };
		foreach (var words in new[] { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { 12300, 0, 0 }, new[] { -1, -1, -1 }, new[] { unchecked((int)0x89abcdef), 0x12345678, 0x76543210 } })
		foreach (var negative in new[] { false, true })
		for (byte scale = 0; scale <= 28; scale++)
		foreach (var format in new[] { "", "G", "G0", "G5", "R", "F2", "N1", "E3", "P0", "C3", "0.000", "#,##0.##;[neg]#,##0.##;zero", "0.00E+00" })
		{
			var value = new decimal(words[0], words[1], words[2], negative, scale);
			var expected = value.ToString(format, info);
			Assert.Equal(expected, ShadowDecimalFormatting.FormatDecimal(value, format, info));
			var destination = new char[expected.Length + 2];
			Array.Fill(destination, '~');
			Assert.True(ShadowDecimalFormatting.TryFormatDecimal(value, format, info, destination.AsSpan(1, expected.Length), out var written));
			Assert.Equal(expected.Length, written);
			Assert.Equal(expected, new string(destination, 1, written));
			Assert.Equal('~', destination[0]);
			Assert.Equal('~', destination[^1]);
			if (expected.Length == 0) continue;
			Array.Fill(destination, '~');
			Assert.False(ShadowDecimalFormatting.TryFormatDecimal(value, format, info, destination.AsSpan(1, expected.Length - 1), out written));
			Assert.Equal(0, written);
			Assert.All(destination, character => Assert.Equal('~', character));
		}
	}

	private static CompilationModule Open(FrameworkImplementationPackTests.CoreLibPack pack, bool enabled) =>
		new(typeof(ShadowDecimalFormatting).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = enabled }));
}
