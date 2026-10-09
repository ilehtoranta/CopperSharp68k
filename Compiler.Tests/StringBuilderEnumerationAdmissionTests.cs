/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderEnumerationAdmissionTests
{
	[Fact]
	public void ObjectDispatchExceptionRequiresTheExactRuntimeAdapterAndReleasedInput()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var text = FrameworkTypeId.Primitive("System.String");
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowObjectJoinDispatch"),
			"ToString", new FrameworkMethodSignatureId(0x20, 0, 0, text, []));
		var member = StringBuilderExceptionSurface.Members.Single(member => member.DeclaringType.MetadataName == "System.NotSupportedException" && member.Signature.ParameterTypes.Length == 1);
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			Assert.Equal(admitted, Admit(caller));
			Assert.False(Admit(null));
			Assert.False(Admit(new(caller.DeclaringType, "Unlisted", caller.Signature)));
			Assert.False(Admit(new(FrameworkTypeId.Named("Application", caller.DeclaringType.FullMetadataName!), caller.Name, caller.Signature)));
			Assert.False(Admit(new(caller.DeclaringType, caller.Name, caller.Signature, [text])));
			bool Admit(FrameworkMemberId? source) => FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, source, out _);
		}
	}

	[Fact]
	public void JoinSubstitutionsRequireAuditedCallersAndElementArguments()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var calls = new List<(CilMethod Caller, int Offset, FrameworkMemberId Member)>();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
			if (admitted)
			foreach (var name in new[] { "AppendCustomStringJoin", "AppendCustomInt32Join", "AppendCustomObjectJoin", "AppendStableDecimalArrayJoin" })
			{
				var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::" + name);
				foreach (var appendCall in entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt))
				{
					var append = module.ResolveMethodToken((int)appendCall.Operand!, entry, appendCall.Offset).Definition!;
					var coreCall = append.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call &&
						module.DescribeFrameworkMethodToken((int)instruction.Operand!, append, instruction.Offset).Name == "AppendJoinCore");
					var caller = module.ResolveMethodToken((int)coreCall.Operand!, append, coreCall.Offset).Definition!;
					foreach (var call in caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt))
						calls.Add((caller, call.Offset, module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset)));
				}
			}
			Assert.NotEmpty(calls);
			foreach (var (caller, offset, member) in calls)
			{
				if (member.Name is "MoveNext" or "Dispose" or "GetEnumerator" or "get_Current")
				{
					Assert.Equal(admitted, module.TryCreateExperimentalJoinEnumerationBinding(member, caller, out var binding));
					if (admitted) Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
					Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, caller with { ModuleName = "Application" }, out _));
					Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, null, out _));
					Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, caller with { MethodTypeArguments = [] }, out _));
					Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(new(member.DeclaringType, "Unlisted", member.Signature), caller, out _));
				}
				else if (member.Name == "ToString")
				{
					if (caller.MethodTypeArguments[0].DisplayName == "object")
					{
						Assert.Equal(admitted, module.TryCreateExperimentalObjectJoinTextBinding(member, caller, out var binding));
						if (admitted) Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow));
						Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, null, out _));
						Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, caller with { ModuleName = "Application" }, out _));
						Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(member, caller with { MethodTypeArguments = [] }, out _));
						Assert.False(module.TryCreateExperimentalObjectJoinTextBinding(new(member.DeclaringType, "Unlisted", member.Signature), caller, out _));
						continue;
					}
					Assert.Equal(admitted, module.TryCreatePinnedIntegralToStringBinding(member, caller, offset, out _, out var implementation));
					if (admitted) Assert.Equal(caller.MethodTypeArguments[0].DisplayName switch {
						"string" => "System.String::ToString", "System.Decimal" => "System.Decimal::ToString", _ => "System.Int32::ToString"
					}, implementation.DisplayName);
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, caller, -1, out _, out _));
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, caller with { ModuleName = "Application" }, offset, out _, out _));
					// Exact floating joins are admitted; a mismatched precision size is not.
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, caller with { MethodTypeArguments = [new CilType(CilTypeKind.FloatingPoint, 4, "double")] }, offset, out _, out _));
				}
			}
		}
	}

	[Fact]
	public void EnumerationContractsRequireExactReleasedDefinitions()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			foreach (var member in StringBuilderEnumerationInterfaces.Members)
			{
				Assert.Equal(admitted, Admit(member, out var binding));
				if (admitted) Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate |
					FrameworkEffects.MayCollect | FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory));
				Assert.False(Admit(new(FrameworkTypeId.Named("Application", member.DeclaringType.FullMetadataName!), member.Name, member.Signature), out _));
				Assert.False(Admit(new(member.DeclaringType, "Unlisted", member.Signature), out _));
				Assert.False(Admit(new(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")]), out _));
			}
			bool Admit(FrameworkMemberId member, out FrameworkBinding binding) =>
				FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out binding);
		}
	}

	[Theory]
	[InlineData("CoreLibStringCustomCallbackContractsEntry")]
	[InlineData("CoreLibInt32CustomCallbackContractsEntry")]
	[InlineData("StringBuilderStableArrayJoinEntry")]
	[InlineData("CoreLibObjectCustomCallbackContractsEntry")]
	[InlineData("StringBuilderStableObjectTextEntry")]
	[InlineData("StringBuilderStableDecimalArrayJoinEntry")]
	[InlineData("CoreLibDecimalCustomCallbackContractsEntry")]
	[InlineData("CoreLibDecimalCustomIteratorOwnershipEntry")]
	public void CallbackGraphsAreCompatibleWithoutUnlistedBodies(string entry)
	{
		Assert.Equal(42, (int)typeof(CompilerFixtures).GetMethod(entry)!.Invoke(null, null)!);
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::" + entry,
			IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-enumeration-" + entry + ".json"),
			System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
		Assert.True(result.IsCompatible, string.Join("\n", result.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
			.Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}
}

public static partial class CompilerFixtures
{
	public static int StringBuilderStableObjectTextEntry()
	{
		var state = new ObjectJoinCallbackState(0);
		object value = new CustomEnumeratedObjectValue(state, "A\0\u03a9\ud800", true);
		var builder = new System.Text.StringBuilder(1);
		if (builder.Append(value) != builder || builder.Insert(0, value) != builder) return 1;
		GC.Collect();
		var snapshot = builder.ToString();
		if (snapshot != "A\0\u03a9\ud800A\0\u03a9\ud800" || state.Formats != 2) return 2;
		object?[] values = [value, null, 42];
		builder.Clear().AppendJoin('|', values);
		GC.Collect();
		if (builder.ToString() != "A\0\u03a9\ud800||42" || state.Formats != 3) return 3;
		builder.Clear().AppendJoin("::", new ReadOnlySpan<object?>(values));
		GC.Collect();
		return builder.ToString() == "A\0\u03a9\ud800::::42" && state.Formats == 4 &&
			snapshot == "A\0\u03a9\ud800A\0\u03a9\ud800" ? 42 : 4;
	}

	public static int StringBuilderStableArrayJoinEntry()
	{
		var strings = new string?[3]; strings[0] = "A\0Ω"; strings[2] = "\ud800";
		var integers = new int[2]; integers[0] = int.MinValue; integers[1] = int.MaxValue;
		var builder = new System.Text.StringBuilder(1);
		builder.AppendJoin('|', (IEnumerable<string?>)strings);
		GC.Collect();
		var snapshot = builder.ToString();
		if (snapshot != "A\0Ω||\ud800") return 1;
		builder.Clear().AppendJoin("::", (IEnumerable<int>)integers);
		GC.Collect();
		if (builder.ToString() != "-2147483648::2147483647" || snapshot != "A\0Ω||\ud800") return 2;
		builder.Clear().Append('x').AppendJoin('|', (IEnumerable<string>)new string[0]);
		try { builder.AppendJoin('|', (IEnumerable<string>)null!); return 3; }
		catch (ArgumentNullException) { GC.Collect(); }
		return builder.ToString() == "x" ? 42 : 4;
	}
}
