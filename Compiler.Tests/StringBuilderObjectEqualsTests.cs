/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using Xunit;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderObjectEqualsTests
{
	[Fact]
	public void BindingRequiresVerifiedExactReceiverSignatureAndUnambiguousStack()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);

		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
			foreach (var name in new[] { "StringBuilderObjectEquals", "UnknownReceiverObjectEquals" })
			{
				var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::" + name);
				var call = caller.Instructions.Single(instruction => instruction.OpCode == OpCodes.Callvirt);
				var member = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
				Assert.Equal(admitted && name == "StringBuilderObjectEquals",
					module.TryCreatePinnedStringBuilderObjectEqualsBinding(member, caller, call.Offset, out var binding));
				if (binding is not null)
				{
					Assert.Equal("intrinsic:object-reference-equals", binding.Target);
					Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow));
				}
				Assert.False(module.TryCreatePinnedStringBuilderObjectEqualsBinding(member, null, call.Offset, out _));
				Assert.False(module.TryCreatePinnedStringBuilderObjectEqualsBinding(member, caller, -1, out _));
				Assert.False(module.TryCreatePinnedStringBuilderObjectEqualsBinding(new(member.DeclaringType, "Unlisted", member.Signature), caller, call.Offset, out _));
				var direct = caller with { Instructions = caller.Instructions.Select(instruction => instruction.Offset == call.Offset ? instruction with { OpCode = OpCodes.Call } : instruction).ToArray() };
				Assert.False(module.TryCreatePinnedStringBuilderObjectEqualsBinding(member, direct, call.Offset, out _));
				var ingress = caller with { Instructions = caller.Instructions.Prepend(new CilInstruction(-1, OpCodes.Br, call.Offset, 0)).ToArray() };
				Assert.False(module.TryCreatePinnedStringBuilderObjectEqualsBinding(member, ingress, call.Offset, out _));
			}
		}
	}
}

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderObjectEqualityPreservesNullReceiverException(M68kCpuTarget target, Copper68k.M68kCpuModel model)
	{
		Assert.Equal(42, CompilerFixtures.StringBuilderObjectEqualsNullReceiverEntry());
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = FixtureAssembly,
				EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderObjectEqualsNullReceiverEntry",
				Cpu = target, OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc, GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions { StartAddress = 0x100000, Size = 0x8000 },
				FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }
			});
			Assert.Equal(42u, ExecuteHunk(result, model));
			Assert.DoesNotContain(result.Symbols, static symbol => symbol.Name == "System.Globalization.CultureInfo::Equals");
		}
	}
}
