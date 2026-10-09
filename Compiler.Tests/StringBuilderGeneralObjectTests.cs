/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderGeneralObjectTests
{
	[Fact]
	public void GeneralObjectCallsRequireExactVirtualSlotAndReleasedInput()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
			var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CallStableGeneralObject");
			var call = caller.Instructions.Single(instruction => instruction.OpCode == OpCodes.Callvirt);
			var member = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
			Assert.Equal(admitted, module.TryCreatePinnedGeneralObjectToStringBinding(member, caller, call.Offset, out _));
			Assert.False(module.TryCreatePinnedGeneralObjectToStringBinding(member, null, call.Offset, out _));
			Assert.False(module.TryCreatePinnedGeneralObjectToStringBinding(member, caller, -1, out _));
			Assert.False(module.TryCreatePinnedGeneralObjectToStringBinding(new(member.DeclaringType, "Unlisted", member.Signature), caller, call.Offset, out _));
			var direct = caller with { Instructions = caller.Instructions.Select(instruction => instruction with { OpCode = OpCodes.Call }).ToArray() };
			Assert.False(module.TryCreatePinnedGeneralObjectToStringBinding(member, direct, call.Offset, out _));
		}
	}
	[Fact]
	public void DefaultNamesInspectInheritanceWithoutRequiringInstanceLayouts()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		foreach (var (name, expected) in new[] {
			("GeneralDefaultObject", true), ("GeneralHiddenObject", true), ("GeneralHiddenVirtualObject", true),
			("GeneralObjectDerived", false), ("GeneralObjectInherited", false), ("GeneralRejectedObject", false) })
		{
			var type = new CilType(CilTypeKind.ManagedReference, 4, "CopperSharp.Compiler.Tests.CompilerFixtures/" + name);
			Assert.Equal(expected, module.HasDefaultObjectToString(module.ResolveRuntimeTypeIdentity(type, module.AssemblyName)));
		}
		foreach (var name in new[] { "System.Exception", "System.Decimal", "System.Enum" })
		{
			var target = module.ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, name), "System.Private.CoreLib");
			Assert.False(module.HasDefaultObjectToString(target));
		}
		var large = module.ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, "System.Globalization.DateTimeFormatInfo"), "System.Private.CoreLib");
		foreach (var (name, expected) in new[] { ("DefaultWideNameValue", true), ("JoinWideValue", false) })
		{
			var type = new CilType(CilTypeKind.ValueType, 8, "CopperSharp.Compiler.Tests.CompilerFixtures/" + name);
			Assert.Equal(expected, module.HasDefaultObjectToString(module.ResolveRuntimeTypeIdentity(type, module.AssemblyName)));
		}
		Assert.True(module.HasDefaultObjectToString(large));
		var unknown = module.ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ManagedReference, 4, "Missing.ApplicationType"), module.AssemblyName);
		Assert.False(module.HasDefaultObjectToString(unknown));
	}
	[Fact]
	public void GeneralObjectFormattingMatchesHostAndHasCompatibleGraph()
	{
		Assert.Equal(42, CompilerFixtures.StringBuilderStableGeneralObjectsEntry());
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableGeneralObjectsEntry",
			IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-general-object-analysis.json"), System.Text.Json.JsonSerializer.Serialize(result));
		Assert.True(result.IsCompatible, string.Join("\n", result.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported).Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}
}

public static partial class CompilerFixtures
{
	private class GeneralObjectBase { public override string ToString() { GC.Collect(); return "base"; } }
	private sealed class GeneralObjectDerived : GeneralObjectBase { public override string ToString() { GC.Collect(); return "derived"; } }
	private sealed class GeneralObjectInherited : GeneralObjectBase;
	private sealed class GeneralDefaultObject;
	private sealed class GeneralHiddenObject { public new string ToString() => "hidden"; }
	private class GeneralHiddenVirtualObject { public new virtual string ToString() => "hidden virtual"; }
	private sealed class GeneralRejectedObject : CopperSharp.Runtime.ShadowObject
	{
		public override string ToString() => throw new NotSupportedException();
	}
	private sealed class GeneralGenericObject<T> { public override string ToString() { GC.Collect(); return "generic"; } }
	private static string CallStableGeneralObject(object value) => value.ToString()!;
	public static int StringBuilderStableGeneralObjectsEntry()
	{
		object[] values = [new GeneralObjectDerived(), new GeneralObjectInherited(), new GeneralDefaultObject(), new GeneralHiddenObject(),
			new GeneralGenericObject<int>(), new System.Text.StringBuilder(1).Append('G'), 42, "A\0\u03a9\ud800", new object()];
		string[] expected = ["derived", "base", "CopperSharp.Compiler.Tests.CompilerFixtures+GeneralDefaultObject", "CopperSharp.Compiler.Tests.CompilerFixtures+GeneralHiddenObject",
			"generic", "G", "42", "A\0\u03a9\ud800", "System.Object"];
		var builder = new System.Text.StringBuilder(1);
		for (var index = 0; index < values.Length; index++)
		{
			GC.Collect();
			var text = CallStableGeneralObject(values[index]);
			if (text != expected[index]) return 10 + index;
			builder.Clear().Append(values[index]); GC.Collect();
			if (builder.ToString() != expected[index]) return 30 + index;
		}
		builder.Clear().AppendJoin('|', values); GC.Collect();
		var snapshot = builder.ToString();
		builder.Clear().AppendJoin("|", new ReadOnlySpan<object>(values)); GC.Collect();
		if (builder.ToString() != snapshot) return 50;
		try { CallStableGeneralObject(null!); return 51; }
		catch (NullReferenceException) { GC.Collect(); }
		if (builder.ToString() != snapshot) return 52;
		object rejected = new GeneralRejectedObject();
		try { CallStableGeneralObject(rejected); return 53; }
		catch (NotSupportedException) { GC.Collect(); }
		builder.Clear().Append("seed");
		try { builder.Append(rejected); return 54; }
		catch (NotSupportedException) { GC.Collect(); }
		if (builder.ToString() != "seed") return 55;
		builder.Append((object)42);
		return builder.ToString() == "seed42" ? 42 : 56;
	}
}
