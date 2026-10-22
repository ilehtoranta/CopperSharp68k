/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class NumericLoweringTests
{
	[Theory]
	[InlineData(M68kFloatingPointMode.SoftFloat)]
	[InlineData(M68kFloatingPointMode.M68882)]
	[InlineData(M68kFloatingPointMode.M68040)]
	public void UnsignedSingleConversionRoundsDirectlyAndRetainsOriginalOffsets(M68kFloatingPointMode mode)
	{
		var assembly = CreateProbe();
		using var module = new CompilationModule(assembly,
			managedAssemblyPaths: [typeof(FloatingPointConversions).Assembly.Location], floatingPoint: mode);
		var original = module.ResolveEntryPoint("NumericConversionProbe::UInt64ToSingle");
		var lowered = module.ApplyTargetRuntimeOverride(original);
		var source = Assert.Single(original.Instructions, instruction => instruction.OpCode == OpCodes.Conv_R_Un);
		var call = Assert.Single(lowered.Instructions, instruction => instruction.OpCode == OpCodes.Call);
		Assert.Equal(source.Offset, call.Offset);
		Assert.Equal(source.NextOffset, call.NextOffset);
		Assert.Equal(0, call.Operand);
		Assert.Equal(OpCodes.Nop, lowered.Instructions.Single(instruction => instruction.Offset == source.NextOffset).OpCode);
		var helper = module.ResolveMethodToken(0, lowered, call.Offset).Definition!;
		Assert.Equal("CopperSharp.Runtime.FloatingPointConversions::UInt64ToSingle", helper.DisplayName);
		Assert.Equal("ulong", Assert.Single(helper.Signature.ParameterTypes).DisplayName);
		Assert.Equal("float", helper.Signature.ReturnType.DisplayName);
		Assert.Equal("UInt64ToSingle", module.DescribeMethodToken(0, lowered, call.Offset)!.Name);
		Assert.Equal("UInt64ToSingle", module.DescribeFrameworkMethodToken(0, lowered, call.Offset).Name);
		Assert.Same(lowered, module.ApplyTargetRuntimeOverride(original));
		var foreign = module.ResolveEntryPoint("NumericConversionProbe::Int64ToSingle");
		Assert.Throws<M68kCompilationException>(() => module.ResolveMethodToken(0, foreign, call.Offset));
		Assert.Throws<M68kCompilationException>(() => module.ResolveMethodToken(0, lowered, call.Offset + 1));
	}

	[Fact]
	public void SharedNarrowingBranchTargetKeepsTheIntermediateDoubleConversion()
	{
		var assembly = CreateProbe();
		using var module = new CompilationModule(assembly,
			managedAssemblyPaths: [typeof(FloatingPointConversions).Assembly.Location], floatingPoint: M68kFloatingPointMode.SoftFloat);
		var original = module.ResolveEntryPoint("NumericConversionProbe::BranchedUInt64ToSingle");
		var lowered = module.ApplyTargetRuntimeOverride(original);
		var helpers = lowered.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken(0, lowered, instruction.Offset).Definition!.Name).ToArray();
		Assert.Equal(new[] { "UInt64ToDouble", "DoubleToSingle" }, helpers);
		Assert.DoesNotContain(lowered.Instructions, instruction => instruction.OpCode == OpCodes.Nop);
	}

	[Fact]
	public void FloatingToIntegerConversionCannotSilentlyUseIntegerBitTruncationWhenDisabled()
	{
		var assembly = CreateProbe();
		using var module = new CompilationModule(assembly);
		var original = module.ResolveEntryPoint("NumericConversionProbe::DisabledEntry");
		Assert.Same(original, module.ApplyTargetRuntimeOverride(original));
		var error = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(new M68kCompilationRequest {
			AssemblyPath = assembly, EntryPoint = "NumericConversionProbe::DisabledEntry",
			Cpu = M68kCpuTarget.M68000, RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo
		}));
		Assert.Contains("Floating numeric conversions require an enabled floating-point mode", error.Message);
	}

	[Theory]
	[InlineData("NegateSingle", "float")]
	[InlineData("NegateDouble", "double")]
	public void FloatingNegationUsesAnExactPrecisionHelper(string entry, string precision)
	{
		using var module = new CompilationModule(CreateProbe(),
			managedAssemblyPaths: [typeof(FloatingPointArithmetic).Assembly.Location], floatingPoint: M68kFloatingPointMode.SoftFloat);
		var original = module.ResolveEntryPoint("NumericConversionProbe::" + entry);
		var lowered = module.ApplyTargetRuntimeOverride(original);
		var negate = Assert.Single(original.Instructions, instruction => instruction.OpCode == OpCodes.Neg);
		var call = Assert.Single(lowered.Instructions, instruction => instruction.OpCode == OpCodes.Call);
		Assert.Equal(negate.Offset, call.Offset);
		Assert.Equal(negate.NextOffset, call.NextOffset);
		var helper = module.ResolveMethodToken(0, lowered, call.Offset).Definition!;
		Assert.Equal("CopperSharp.Runtime.FloatingPointArithmetic::" + entry, helper.DisplayName);
		Assert.Equal(precision, helper.Signature.ReturnType.DisplayName);
		Assert.Equal(precision, Assert.Single(helper.Signature.ParameterTypes).DisplayName);
	}

	[Fact]
	public void NumericLoweringLeavesIntegerNegationAndDisabledFloatingNegationIntact()
	{
		var assembly = CreateProbe();
		using var enabled = new CompilationModule(assembly,
			managedAssemblyPaths: [typeof(FloatingPointArithmetic).Assembly.Location], floatingPoint: M68kFloatingPointMode.SoftFloat);
		var integer = enabled.ResolveEntryPoint("NumericConversionProbe::NegateInt64");
		Assert.Contains(enabled.ApplyTargetRuntimeOverride(integer).Instructions, instruction => instruction.OpCode == OpCodes.Neg);
		using var disabled = new CompilationModule(assembly);
		var floating = disabled.ResolveEntryPoint("NumericConversionProbe::NegateDouble");
		Assert.Same(floating, disabled.ApplyTargetRuntimeOverride(floating));
	}

	[Theory]
	[InlineData("AddSingle", "float")]
	[InlineData("SubtractSingle", "float")]
	[InlineData("MultiplySingle", "float")]
	[InlineData("DivideSingle", "float")]
	[InlineData("RemainderSingle", "float")]
	[InlineData("AddDouble", "double")]
	[InlineData("SubtractDouble", "double")]
	[InlineData("MultiplyDouble", "double")]
	[InlineData("DivideDouble", "double")]
	[InlineData("RemainderDouble", "double")]
	public void FloatingBinaryOperationsKeepOffsetsAndSelectExactPrecisionSignatures(string name, string precision)
	{
		var directory = Path.Combine(Path.GetDirectoryName(typeof(NumericLoweringTests).Assembly.Location)!, "ArithmeticLoweringProbes", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		using var module = new CompilationModule(FloatingPointArithmeticFixtureBuilder.Create(directory),
			managedAssemblyPaths: [typeof(FloatingPointArithmetic).Assembly.Location], floatingPoint: M68kFloatingPointMode.SoftFloat);
		var original = module.ResolveEntryPoint("FloatingPointArithmeticProbe::" + name);
		var lowered = module.ApplyTargetRuntimeOverride(original);
		var arithmetic = original.Instructions[2];
		var call = Assert.Single(lowered.Instructions, instruction => instruction.OpCode == OpCodes.Call);
		Assert.Equal(arithmetic.Offset, call.Offset); Assert.Equal(arithmetic.NextOffset, call.NextOffset);
		var helper = module.ResolveMethodToken(0, lowered, call.Offset).Definition!;
		Assert.Equal("CopperSharp.Runtime.FloatingPointArithmetic::" + name, helper.DisplayName);
		Assert.Equal(precision, helper.Signature.ReturnType.DisplayName);
		Assert.Equal(new[] { precision, precision }, helper.Signature.ParameterTypes.Select(type => type.DisplayName));
	}

	public static IEnumerable<object[]> FloatingComparisonCases() => FloatingPointComparisonFixtureBuilder.Operations()
		.SelectMany(operation => new[] { "Single", "Double" }.Select(precision => new object[] { operation.Name, operation.Op, precision }));

	[Theory]
	[MemberData(nameof(FloatingComparisonCases))]
	public void FloatingComparisonsKeepBranchTargetsAndUseBooleanPrecisionHelpers(string name, OpCode op, string precision)
	{
		using var module = CreateComparisonModule();
		var original = module.ResolveEntryPoint("FloatingPointComparisonProbe::" + name + precision);
		var lowered = module.ApplyTargetRuntimeOverride(original);
		var comparison = Assert.Single(original.Instructions, instruction => instruction.OpCode == op);
		var call = Assert.Single(lowered.Instructions, instruction => instruction.OpCode == OpCodes.Call);
		Assert.Equal(comparison.Offset, call.Offset);
		var helper = module.ResolveMethodToken(0, lowered, call.Offset).Definition!;
		Assert.Equal("bool", helper.Signature.ReturnType.DisplayName);
		Assert.Equal(new[] { precision == "Single" ? "float" : "double", precision == "Single" ? "float" : "double" }, helper.Signature.ParameterTypes.Select(type => type.DisplayName));
		if (op.FlowControl == FlowControl.Cond_Branch)
		{
			var branch = Assert.Single(lowered.Instructions, instruction => instruction.OpCode == OpCodes.Brtrue);
			Assert.Equal(comparison.Offset + 1, branch.Offset); Assert.Equal(branch.Offset, call.NextOffset);
			Assert.Equal(comparison.NextOffset, branch.NextOffset); Assert.Equal(comparison.Operand, branch.Operand);
			Assert.Equal(original.Instructions.Count + 1, lowered.Instructions.Count);
		}
		else Assert.Equal(comparison.NextOffset, call.NextOffset);
	}

	[Fact]
	public void FloatingBranchExpansionPreservesAllOriginalExceptionRegionBoundaries()
	{
		using var module = CreateComparisonModule();
		var original = module.ResolveEntryPoint("FloatingPointComparisonProbe::ProtectedComparison");
		var lowered = module.ApplyTargetRuntimeOverride(original);
		Assert.NotEmpty(original.ExceptionRegions);
		Assert.Equal(original.ExceptionRegions, lowered.ExceptionRegions);
		Assert.Single(lowered.Instructions, instruction => instruction.OpCode == OpCodes.Call);
		Assert.Single(lowered.Instructions, instruction => instruction.OpCode == OpCodes.Brtrue);
	}

	[Theory]
	[InlineData(M68kFloatingPointMode.M68882)]
	[InlineData(M68kFloatingPointMode.M68040)]
	public void HardwareModesRetainNativeArithmeticAndLowerUnorderedComparisons(M68kFloatingPointMode mode)
	{
		var directory = Path.Combine(Path.GetDirectoryName(typeof(NumericLoweringTests).Assembly.Location)!, "HardwareNumericLoweringProbes", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		using var arithmetic = new CompilationModule(FloatingPointArithmeticFixtureBuilder.Create(directory),
			managedAssemblyPaths: [typeof(FloatingPointArithmetic).Assembly.Location], floatingPoint: mode);
		foreach (var name in new[] { "Add", "Subtract", "Multiply", "Divide", "Remainder" })
		foreach (var precision in new[] { "Single", "Double" })
		{
			var original = arithmetic.ResolveEntryPoint("FloatingPointArithmeticProbe::" + name + precision);
			Assert.Equal(original.Instructions, arithmetic.ApplyTargetRuntimeOverride(original).Instructions);
		}
		using var comparison = CreateComparisonModule(mode);
		foreach (var name in new[] { "ceq", "clt_un", "bge_un_s" })
		foreach (var precision in new[] { "Single", "Double" })
		{
			var original = comparison.ResolveEntryPoint("FloatingPointComparisonProbe::" + name + precision);
			var lowered = comparison.ApplyTargetRuntimeOverride(original);
			var call = Assert.Single(lowered.Instructions, instruction => instruction.OpCode == OpCodes.Call);
			Assert.StartsWith("CopperSharp.Runtime.FloatingPointArithmetic::", comparison.ResolveMethodToken(0, lowered, call.Offset).Definition!.DisplayName);
		}
	}

	private static CompilationModule CreateComparisonModule(M68kFloatingPointMode mode = M68kFloatingPointMode.SoftFloat)
	{
		var directory = Path.Combine(Path.GetDirectoryName(typeof(NumericLoweringTests).Assembly.Location)!, "ComparisonLoweringProbes", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		return new CompilationModule(FloatingPointComparisonFixtureBuilder.Create(directory),
			managedAssemblyPaths: [typeof(FloatingPointArithmetic).Assembly.Location], floatingPoint: mode);
	}

	private static string CreateProbe()
	{
		var directory = Path.Combine(Path.GetDirectoryName(typeof(NumericLoweringTests).Assembly.Location)!, "NumericLoweringProbes", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		return NumericConversionFixtureBuilder.Create(directory);
	}
}
