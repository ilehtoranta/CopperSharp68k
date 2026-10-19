/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderPublicSurfaceTests
{
	[Fact]
	public void EveryPublicOverloadAndPropertyAccessorHasAnExplicitFixtureUse()
	{
		var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
		var methods = typeof(StringBuilder).GetMethods(flags);
		var constructors = typeof(StringBuilder).GetConstructors(flags);
		var properties = typeof(StringBuilder).GetProperties(flags);
		Assert.Equal(96, methods.Count(method => !method.IsSpecialName) + constructors.Length);
		Assert.Equal(new[] { "Capacity", "Chars", "Length", "MaxCapacity" }, properties.Select(property => property.Name).Order().ToArray());
		MethodBase[] surface = [.. constructors, .. methods];
		Assert.Equal(103, surface.Length);
		var calls = surface.ToDictionary(method => method.MetadataToken, _ => new SortedSet<string>(StringComparer.Ordinal));
		foreach (var type in FixtureTypes(typeof(CompilerFixtures)))
		foreach (var caller in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
		{
			foreach (var callee in CalledMethods(caller))
				if (callee.DeclaringType == typeof(StringBuilder) && calls.TryGetValue(callee.MetadataToken, out var users))
					users.Add(type.FullName + "::" + caller.Name);
		}
		var rows = surface.OrderBy(method => method.Name, StringComparer.Ordinal).ThenBy(method => method.ToString(), StringComparer.Ordinal)
			.Select(method => "| `" + method + "` | " + string.Join("<br>", calls[method.MetadataToken].Select(caller => "`" + caller + "`")) + " |");
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-public-surface.md"),
			"# StringBuilder public call inventory\n\nRuntime: " + typeof(StringBuilder).Assembly.GetName().Version +
			"; module: " + typeof(StringBuilder).Module.ModuleVersionId +
			". This records direct fixture IL calls and verified StringBuilder-local virtual dispatch, not successful native execution or supported-profile admission.\n\n" +
			"96 constructor/method signatures and seven accessors for four properties.\n\n| Public signature | Fixture callers |\n| --- | --- |\n" + string.Join("\n", rows) + "\n");
		var missing = surface.Where(method => calls[method.MetadataToken].Count == 0).Select(method => method.ToString()).ToArray();
		Assert.True(missing.Length == 0, "Missing StringBuilder fixture uses:\n" + string.Join("\n", missing));
	}

	[Fact]
	public void VirtualSlotClassificationRequiresTheExactStringBuilderReceiverLocal()
	{
		var general = typeof(StringBuilderPublicSurfaceTests).GetMethod(nameof(OrdinaryObjectSlot), BindingFlags.NonPublic | BindingFlags.Static)!;
		Assert.Contains(CalledMethods(general), method => method.DeclaringType == typeof(object) && method.Name == nameof(ToString));
		Assert.DoesNotContain(CalledMethods(general), method => method.DeclaringType == typeof(StringBuilder));
		var builder = typeof(CompilerFixtures).GetMethod(nameof(CompilerFixtures.CoreLibStringBuilderToStringEntry))!;
		Assert.Equal(3, CalledMethods(builder).Count(method => method.DeclaringType == typeof(StringBuilder) && method.Name == nameof(ToString)));
	}

	[Fact]
	public void UnifiedAcceptanceEntryExplicitlyUsesEveryPublicSignatureAndAccessor()
	{
		var entry = typeof(CompilerFixtures).GetMethod(nameof(CompilerFixtures.CoreLibStringBuilderPublicAcceptanceEntry))!;
		var calls = CalledMethods(entry).Where(method => method.DeclaringType == typeof(StringBuilder)).Select(method => method.MetadataToken).ToHashSet();
		var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
		MethodBase[] surface = [.. typeof(StringBuilder).GetConstructors(flags), .. typeof(StringBuilder).GetMethods(flags)];
		Assert.Equal(103, surface.Length);
		Assert.All(surface, method => Assert.True(calls.Contains(method.MetadataToken), "Unified acceptance entry omits " + method));
	}

	private static string OrdinaryObjectSlot(object value) => value.ToString()!;

	private static IEnumerable<Type> FixtureTypes(Type root)
	{
		yield return root;
		foreach (var nested in root.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
		foreach (var type in FixtureTypes(nested)) yield return type;
	}

	private static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
		.Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
		.ToDictionary(opcode => unchecked((ushort)opcode.Value));

	private static IEnumerable<MethodBase> CalledMethods(MethodInfo caller)
	{
		var bytes = caller.GetMethodBody()?.GetILAsByteArray();
		if (bytes is null) yield break;
		OpCode previous = default;
		var previousOperand = 0;
		for (var offset = 0; offset < bytes.Length;)
		{
			ushort opcode = bytes[offset++];
			if (opcode == 0xFE) opcode = (ushort)(0xFE00 | bytes[offset++]);
			var operation = Opcodes[opcode];
			if (operation.OperandType == OperandType.InlineMethod)
			{
				var token = BitConverter.ToInt32(bytes, offset);
				var callee = caller.Module.ResolveMethod(token, caller.DeclaringType!.GetGenericArguments(), caller.GetGenericArguments())!;
				// C# emits the Object virtual slot for StringBuilder.ToString().
				// Count it only when the immediately loaded local has the exact
				// StringBuilder type, rather than counting arbitrary Object calls.
				if (operation == OpCodes.Callvirt && callee == typeof(object).GetMethod(nameof(ToString), Type.EmptyTypes))
				{
					var local = previous == OpCodes.Ldloc_0 ? 0 : previous == OpCodes.Ldloc_1 ? 1 :
						previous == OpCodes.Ldloc_2 ? 2 : previous == OpCodes.Ldloc_3 ? 3 :
						previous == OpCodes.Ldloc_S ? bytes[previousOperand] : previous == OpCodes.Ldloc ? BitConverter.ToUInt16(bytes, previousOperand) : -1;
					if (local >= 0 && caller.GetMethodBody()!.LocalVariables[local].LocalType == typeof(StringBuilder))
						callee = typeof(StringBuilder).GetMethod(nameof(ToString), Type.EmptyTypes)!;
				}
				if (operation == OpCodes.Call || operation == OpCodes.Callvirt || operation == OpCodes.Newobj) yield return callee;
			}
			previous = operation;
			previousOperand = offset;
			offset += operation.OperandType switch
			{
				OperandType.InlineNone => 0,
				OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
				OperandType.InlineVar => 2,
				OperandType.InlineI8 or OperandType.InlineR => 8,
				OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
				_ => 4
			};
		}
	}
}
