/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderNewLineAdmissionTests
{
	[Fact]
	public void ReleasedNewLineWrappersAndHandlerConsumersUseTheTargetAdapter()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath);
		var methods = module.Reader.MethodDefinitions.Where(handle => {
			var method = module.Reader.GetMethodDefinition(handle);
			return module.Reader.GetString(method.Name) == "AppendLine" &&
				module.Reader.GetString(module.Reader.GetTypeDefinition(method.GetDeclaringType()).Name) == "StringBuilder";
		}).Select(module.GetMethod).ToArray();
		Assert.Equal(4, methods.Length);
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "stringbuilder-newline-body-audit.txt"), methods.SelectMany(method =>
			new[] { method.DisplayName }.Concat(method.Instructions.Select(instruction => instruction.OpCode == OpCodes.Call
				? module.DescribeFrameworkMethodToken((int)instruction.Operand!, method, instruction.Offset).DisplayName
				: instruction.OpCode == OpCodes.Ldstr ? module.GetUserString((int)instruction.Operand!, method, instruction.Offset).Replace("\r", "\\r").Replace("\n", "\\n") : instruction.ToString()))));
		foreach (var method in methods)
		{
			var member = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(method.Handle), method, -1);
			var handler = member.Signature.ParameterTypes.Any(type => type.Kind == FrameworkTypeKind.ByReference);
			if (handler)
			{
				var call = Assert.Single(method.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call));
				member = module.DescribeFrameworkMethodToken((int)call.Operand!, method, call.Offset);
				Assert.Equal("AppendLine", member.Name);
				Assert.Empty(member.Signature.ParameterTypes);
			}
			Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, null, out var binding));
			Assert.Equal("shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowStringBuilder::AppendLine", binding.Target);
			Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new(member.DeclaringType, "AdjacentAppendLine", member.Signature), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(new(member.DeclaringType, "AdjacentAppendLine", member.Signature), catalog, null, out _));
		}
		pack.Replace("packVersion", "10.0.10");
		var unaudited = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		foreach (var method in methods)
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(
				module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(method.Handle), method, -1), unaudited, null, out _));
	}
}

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableNewLinesUseThePlatformValue(CopperSharp.Compiler.M68kCpuTarget target, Copper68k.M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, [nameof(CompilerFixtures.StringBuilderNewLineConsumersEntry)]);
}

public static partial class CompilerFixtures
{
	public static int StringBuilderNewLineConsumersEntry()
	{
		for (var capacity = 1; capacity <= 32; capacity *= 32)
		{
			var builder = new System.Text.StringBuilder(capacity).Append("seed");
			var snapshot = builder.ToString();
			if (!ReferenceEquals(builder, builder.AppendLine()) || builder.ToString() != "seed" + Environment.NewLine) return 1;
			builder.Clear();
			if (!ReferenceEquals(builder, builder.AppendLine("text")) || builder.ToString() != "text" + Environment.NewLine) return 2;
			builder.Clear();
			if (!ReferenceEquals(builder, builder.AppendLine((string?)null)) || builder.ToString() != Environment.NewLine) return 3;
			builder.Clear().Append("seed");
			var handler = new System.Text.StringBuilder.AppendInterpolatedStringHandler(4, 0, builder);
			handler.AppendLiteral("text");
			if (!ReferenceEquals(builder, builder.AppendLine(ref handler)) || builder.ToString() != "seedtext" + Environment.NewLine) return 4;
			builder.Clear().Append("seed");
			handler = new System.Text.StringBuilder.AppendInterpolatedStringHandler(4, 0, builder, null);
			handler.AppendLiteral("text");
			if (!ReferenceEquals(builder, builder.AppendLine(null, ref handler)) || builder.ToString() != "seedtext" + Environment.NewLine) return 5;
			System.GC.Collect();
			if (snapshot != "seed" || builder.ToString() != "seedtext" + Environment.NewLine) return 6;
		}
		return 42;
	}
}
