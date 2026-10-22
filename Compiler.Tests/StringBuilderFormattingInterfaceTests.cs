/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderFormattingInterfaceTests
{
	[Fact]
	public void FormattingTypeTestsResolveOnlyReleasedInterfaceDefinitions()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
			foreach (var name in new[] { "Provider", "Custom", "Text", "Span" })
			{
				var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::StableFormattingCast" + name);
				var cast = Assert.Single(caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Castclass));
				var target = module.ResolveRuntimeTypeToken((int)cast.Operand!, caller, cast.Offset);
				Assert.Equal(admitted, target.IsInterface);
				if (admitted) Assert.Equal("System.Private.CoreLib", target.ModuleName);
			}
		}
	}

	[Fact]
	public void FormattingContractsRequireExactReleasedDefinitions()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			foreach (var member in StringBuilderFormattingInterfaces.Members)
			{
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out var binding));
				if (admitted)
				{
					Assert.Equal(FrameworkBindingKind.PinnedManagedBody, binding.Kind);
					Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.WritesManagedMemory));
				}
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(
					FrameworkTypeId.Named("Application", member.DeclaringType.FullMetadataName!), member.Name, member.Signature), null, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature), null, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature,
					[FrameworkTypeId.Primitive("System.Int32")]), null, catalog, null, out _));
			}
		}
	}
}

public static partial class CompilerFixtures
{
	private static IFormatProvider StableFormattingCastProvider(object value) => (IFormatProvider)value;
	private static ICustomFormatter StableFormattingCastCustom(object value) => (ICustomFormatter)value;
	private static IFormattable StableFormattingCastText(object value) => (IFormattable)value;
	private static ISpanFormattable StableFormattingCastSpan(object value) => (ISpanFormattable)value;
	private sealed class StableFormattingCallbacks : IFormatProvider, ICustomFormatter, ISpanFormattable
	{
		public int Queries, Formats, Texts, Spans;
		public object? GetFormat(Type? type) { Queries++; GC.Collect(); return type is null ? this : null; }
		public string Format(string? format, object? value, IFormatProvider? provider)
		{
			Formats++; GC.Collect();
			return format == "f" && ReferenceEquals(value, this) && ReferenceEquals(provider, this) ? "CΩ\0\uD800" : "bad";
		}
		public string ToString(string? format, IFormatProvider? provider)
		{
			Texts++; GC.Collect();
			return format == "t" && ReferenceEquals(provider, this) ? "T" : "bad";
		}
		public bool TryFormat(Span<char> destination, out int written, ReadOnlySpan<char> format, IFormatProvider? provider)
		{
			Spans++; GC.Collect(); written = 0;
			if (destination.Length < 3 || format.Length != 0 || !ReferenceEquals(provider, this)) return false;
			destination[0] = 'Ω'; destination[1] = '\0'; destination[2] = '\uD800'; written = 3;
			return true;
		}
	}

	public static int StringBuilderStableFormattingCallbacksEntry()
	{
		object unrelated = "plain";
		if (unrelated is ICustomFormatter) return 4;
		try { StableFormattingCastCustom(unrelated); return 5; }
		catch (InvalidCastException) { GC.Collect(); }
		var value = new StableFormattingCallbacks();
		IFormatProvider provider = value;
		var custom = (ICustomFormatter)provider.GetFormat(null!)!;
		var text = custom.Format("f", value, provider);
		var formatted = ((IFormattable)value).ToString("t", provider);
		Span<char> destination = stackalloc char[4];
		if (!((ISpanFormattable)value).TryFormat(destination, out var written, default, provider) || written != 3) return 1;
		if (destination[0] != 'Ω' || destination[1] != '\0' || destination[2] != '\uD800') return 2;
		GC.Collect();
		var builder = new System.Text.StringBuilder(1).Append(text).Append(formatted);
		return builder.ToString() == "CΩ\0\uD800T" && value.Queries == 1 && value.Formats == 1 && value.Texts == 1 && value.Spans == 1 ? 42 : 3;
	}
}
