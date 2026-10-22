/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderFormattingPrimitiveTests
{
	[Fact]
	public void FormattingPrimitivesRequireExactSignaturesAndOwnedCallers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var constructor = StringBuilderFrameworkSurface.Members.First(member => member.Name == ".ctor" && member.Signature.ParameterTypes.Length == 0);
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.StringBuilder"), constructor.Name, constructor.Signature);
		var text = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Boolean"), "ToString",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []));
		var digit = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Char"), "IsAsciiDigit",
			new FrameworkMethodSignatureId(0, 0, 1, FrameworkTypeId.Primitive("System.Boolean"), [FrameworkTypeId.Primitive("System.Char")]));
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);

		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			foreach (var member in new[] { text, digit })
			{
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out var binding));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, constructor, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature), null, catalog, caller, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")]), null, catalog, caller, out _));
				if (admitted)
				{
					Assert.Equal(FrameworkEffects.None, binding.EffectSummary.Effects);
					using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
					var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type =>
						module.Reader.GetString(type.Namespace) + "." + module.Reader.GetString(type.Name) == member.DeclaringType.FullMetadataName);
					var method = type.GetMethods().Where(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == member.Name)
						.Select(module.GetMethod).Single(method => method.Signature.ParameterTypes.Length == member.Signature.RequiredParameterCount);
					if (member.Name == "ToString")
						Assert.DoesNotContain(method.Instructions, instruction => instruction.OpCode.FlowControl == System.Reflection.Emit.FlowControl.Call);
					else
					{
						var call = Assert.Single(method.Instructions.Where(instruction => instruction.OpCode.FlowControl == System.Reflection.Emit.FlowControl.Call));
						var helper = module.DescribeFrameworkMethodToken((int)call.Operand!, method, call.Offset);
						var declaredCaller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Char"), member.Name, member.Signature);
						Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(helper, null, catalog, declaredCaller, out var range));
						Assert.Equal("IsBetween", range.Member.Name);
						Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(helper, null, catalog, caller, out _));
						Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(helper, null, catalog, member, out _));
						var body = module.ResolveMethodToken((int)call.Operand!, method, call.Offset).Definition!;
						Assert.DoesNotContain(body.Instructions, instruction => instruction.OpCode.FlowControl == System.Reflection.Emit.FlowControl.Call);
					}
				}
			}
		}
	}
}
