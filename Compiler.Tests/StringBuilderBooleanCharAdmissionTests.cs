/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderBooleanCharAdmissionTests
{
	[Fact]
	public void BoxedStructIteratorsRegisterTheirReferenceBitmapAndDisposeSlot()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
			using var coreLib = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var owner = coreLib.Reader.TypeDefinitions.Single(handle => coreLib.Reader.GetString(coreLib.Reader.GetTypeDefinition(handle).Name) == "IDisposable");
			var dispose = coreLib.GetMethod(coreLib.Reader.GetTypeDefinition(owner).GetMethods().Single());
			foreach (var element in new[] { new CilType(CilTypeKind.Boolean, 1, "bool"), new CilType(CilTypeKind.Character, 2, "char") })
			{
				var type = new CilType(CilTypeKind.ValueType, 0, $"CopperSharp.Compiler.Tests.CompilerFixtures/StructGenericJoinIterator`1<{element.DisplayName}>", GenericArguments: [element]);
				var layout = module.RegisterBoxedDispatchLayout(type, "CopperSharp.Compiler.Tests");
				Assert.Equal(admitted, layout is not null);
				if (!admitted) continue;
				Assert.Equal(12, layout!.Size);
				Assert.Equal(3u, layout.ReferenceBitmap);
				Assert.Contains(module.GetInterfaceImplementations(dispose), method => method.Name == "Dispose" && method.ConstructedDeclaringType == type);
			}
		}
	}

	[Fact]
	public void PrimitiveEnumerationRequiresExactReleasedJoinDefinitions()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(pack.AssemblyPath,
				managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location], frameworkImplementationPack:
				FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
			var owner = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "StringBuilder");
			var definitions = module.Reader.GetTypeDefinition(owner).GetMethods().Where(handle =>
				module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == "AppendJoinCore")
				.Select(module.GetMethod).Where(method => method.Signature.GenericParameterCount == 1 &&
					method.Signature.ParameterTypes[^1].Kind == CilTypeKind.ManagedReference).ToArray();
			var definition = Assert.Single(definitions);
			foreach (var element in new[] { new CilType(CilTypeKind.Boolean, 1, "bool"), new CilType(CilTypeKind.Character, 2, "char"),
				new CilType(CilTypeKind.SignedInteger, 1, "sbyte"), new CilType(CilTypeKind.UnsignedInteger, 1, "byte"),
				new CilType(CilTypeKind.SignedInteger, 2, "short"), new CilType(CilTypeKind.UnsignedInteger, 2, "ushort"),
				new CilType(CilTypeKind.UnsignedInteger, 4, "uint"), new CilType(CilTypeKind.SignedInteger, 8, "long"),
				new CilType(CilTypeKind.UnsignedInteger, 8, "ulong") })
			{
				var source = definition.Signature.ParameterTypes[^1] with {
					DisplayName = $"System.Collections.Generic.IEnumerable`1<{element.DisplayName}>", GenericArguments = [element] };
				var caller = definition with { MethodTypeArguments = [element], DisplayName = $"System.Text.StringBuilder::AppendJoinCore<{element.DisplayName}>",
					Signature = new System.Reflection.Metadata.MethodSignature<CilType>(definition.Signature.Header,
						definition.Signature.ReturnType, definition.Signature.RequiredParameterCount, definition.Signature.GenericParameterCount,
						[definition.Signature.ParameterTypes[0], definition.Signature.ParameterTypes[1], source]) };
				var calls = caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt)
					.Select(instruction => (instruction, member: module.DescribeFrameworkMethodToken((int)instruction.Operand!, caller, instruction.Offset)))
					.Where(pair => pair.member.Name is "GetEnumerator" or "get_Current" or "MoveNext" or "Dispose").ToArray();
				Assert.Equal(new[] { "Dispose", "GetEnumerator", "MoveNext", "get_Current" }, calls.Select(pair => pair.member.Name).Distinct().Order(StringComparer.Ordinal).ToArray());
				foreach (var (call, member) in calls)
				{
					Assert.Equal(admitted, module.TryCreateExperimentalJoinEnumerationBinding(member, caller, out var binding));
					if (admitted)
					{
						Assert.Contains("ShadowGenericJoin", binding.ShadowMethod!.TypeName);
						Assert.Equal(binding.Target, module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).FrameworkBinding!.Target);
					}
					Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, caller with { ModuleName = "Application" }, out _));
					Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, caller with { DisplayName = "System.Text.StringBuilder::AdjacentJoin" }, out _));
					Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, caller with { MethodTypeArguments = [element with { Size = 3 }] }, out _));
					Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(new(member.DeclaringType, "Adjacent", member.Signature), caller, out _));
				}
			}
		}
	}
}

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableBooleanCharJoinsPreserveBehaviorAndOwners(M68kCpuTarget target, Copper68k.M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, [
			nameof(CompilerFixtures.CoreLibStringBuilderGenericBooleanCharJoinsEntry),
			nameof(CompilerFixtures.CoreLibGenericBooleanCharCallbackContractsEntry),
			nameof(CompilerFixtures.CoreLibGenericBooleanCharOwnershipEntry),
			nameof(CompilerFixtures.CoreLibGenericBooleanCharCapacityEntry)]);
}
