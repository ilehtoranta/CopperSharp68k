/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderNumericProviderTests
{
	[Fact]
	public void NumberFormatProviderEntryUsesTheExactReleasedBody()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Globalization.NumberFormatInfo"), "GetInstance",
			new FrameworkMethodSignatureId(0, 0, 1, FrameworkTypeId.Named("System.Runtime", "System.Globalization.NumberFormatInfo"),
				[FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider")]));
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out var binding));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature), null, catalog, null, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature,
				[FrameworkTypeId.Primitive("System.Int32")]), null, catalog, null, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(
				FrameworkTypeId.Named("Application", member.DeclaringType.FullMetadataName!), member.Name, member.Signature), null, catalog, null, out _));
			if (!admitted) return;
			Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var body = module.ResolveEntryPoint("System.Globalization.NumberFormatInfo::GetInstance");
			File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "stringbuilder-number-provider-body.txt"), body.Instructions.Select(instruction =>
				instruction.OpCode.FlowControl == System.Reflection.Emit.FlowControl.Call
					? $"{instruction.Offset:X4} {instruction.OpCode} {module.DescribeFrameworkMethodToken((int)instruction.Operand!, body, instruction.Offset).DeclaringType.FullMetadataName}::{module.DescribeFrameworkMethodToken((int)instruction.Operand!, body, instruction.Offset).Name}"
					: $"{instruction.Offset:X4} {instruction.OpCode} {instruction.Operand}"));
			foreach (var helperName in new[] { "<GetInstance>g__GetProviderNonNull|58_0", "get_CurrentInfo" })
			{
				var helper = module.ResolveEntryPoint("System.Globalization.NumberFormatInfo::" + helperName);
				var helperMember = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(helper.Handle), body, 0);
				var caller = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(body.Handle), body, 0);
				Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(helperMember, null, catalog, caller, out _));
				if (helperName.StartsWith('<'))
				{
					Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(helperMember, null, catalog, null, out _));
					Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(helperMember, null, catalog, member, out _));
				}
				foreach (var token in helper.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ldtoken))
				{
					var type = module.ResolveTypeToken((int)token.Operand!, helper, token.Offset);
					Assert.True(module.IsPinnedNumberFormatTypeToken(helper, type));
					Assert.False(module.IsPinnedNumberFormatTypeToken(body, type));
					Assert.False(module.IsPinnedNumberFormatTypeToken(helper, new CilType(CilTypeKind.ManagedReference, 4, "System.Object")));
				}
				File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "stringbuilder-number-provider-" + (helperName.StartsWith('<') ? "resolver" : "current") + ".txt"),
					helper.Instructions.Select(instruction => instruction.OpCode.FlowControl == System.Reflection.Emit.FlowControl.Call
						? $"{instruction.Offset:X4} {instruction.OpCode} {module.DescribeFrameworkMethodToken((int)instruction.Operand!, helper, instruction.Offset).DeclaringType.FullMetadataName}::{module.DescribeFrameworkMethodToken((int)instruction.Operand!, helper, instruction.Offset).Name}"
						: $"{instruction.Offset:X4} {instruction.OpCode} {instruction.Operand}"));
			}
		}
	}
}

public static partial class CompilerFixtures
{
	private sealed class StableNumericProvider : IFormatProvider
	{
		public object? Result;
		public int Queries;
		public Type? RequestedType;
		public object? GetFormat(Type? type) { Queries++; RequestedType = type; GC.Collect(); return Result; }
	}

	public static int StringBuilderStableNumericProviderEntry()
	{
		var expected = System.Globalization.CultureInfo.CurrentCulture.NumberFormat;
		var actual = System.Globalization.NumberFormatInfo.GetInstance(null);
		GC.Collect();
		if (!ReferenceEquals(expected, actual)) return 1;
		if (!ReferenceEquals(System.Globalization.NumberFormatInfo.GetInstance(expected), expected)) return 2;
		var invariant = System.Globalization.CultureInfo.InvariantCulture;
		if (!ReferenceEquals(System.Globalization.NumberFormatInfo.GetInstance(invariant), invariant.NumberFormat)) return 3;
		var provider = new StableNumericProvider { Result = expected };
		if (!ReferenceEquals(System.Globalization.NumberFormatInfo.GetInstance(provider), expected) || provider.Queries != 1 || provider.RequestedType is null) return 4;
		var requested = provider.RequestedType;
		provider.Result = "wrong type";
		GC.Collect();
		if (!ReferenceEquals(System.Globalization.NumberFormatInfo.GetInstance(provider), expected) || provider.Queries != 2 ||
			!ReferenceEquals(provider.RequestedType, requested)) return 5;
		provider.Result = null;
		return ReferenceEquals(System.Globalization.NumberFormatInfo.GetInstance(provider), expected) && provider.Queries == 3 ? 42 : 6;
	}
}
