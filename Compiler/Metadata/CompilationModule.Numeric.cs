/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Metadata;

internal sealed partial class CompilationModule
{
	private readonly M68kFloatingPointMode _floatingPointMode;
	private readonly Dictionary<CilMethodIdentity, CilMethod> _numericMethods = new();
	private readonly Dictionary<(CilMethodIdentity Caller, int Offset), MethodReference> _numericCalls = new();

	private bool TryGetNumericCall(int token, CilMethod caller, int offset, out MethodReference reference)
	{
		// Token zero is reserved for compiler-created calls. No external metadata
		// token can select a numeric helper without this caller-and-offset registration.
		reference = null!;
		return token == 0 && _root._numericCalls.TryGetValue((caller.Identity, offset), out reference!);
	}

	private CilMethod LowerNumericOperations(CilMethod method)
	{
		if (!ReferenceEquals(this, _root)) return _root.LowerNumericOperations(method);
		if (method.IsImport || method.HasDeferredBody) return method;
		if (_numericMethods.TryGetValue(method.Identity, out var previous)) return previous;
		if (!method.Instructions.Any(instruction => IsIntegerDivision(instruction.OpCode) ||
			(_floatingPointMode != M68kFloatingPointMode.Disabled && (instruction.OpCode == OpCodes.Neg || IsFloatingBinary(instruction.OpCode) || FloatingComparisonName(instruction.OpCode) is not null ||
			 instruction.OpCode.Name?.StartsWith("conv.", StringComparison.Ordinal) == true))))
			return method;
		var states = CilStackAnalyzer.AnalyzeTypes(method, this, CilOptimizer.Optimize(method, this));
		var instructions = method.Instructions.ToArray();
		var comparisonBranches = new Dictionary<int, CilInstruction>();
		var branchTargets = instructions.SelectMany(instruction => instruction.Operand switch {
			int target when instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch => [target],
			int[] targets when instruction.OpCode == OpCodes.Switch => targets,
			_ => Array.Empty<int>()
		}).Concat(method.ExceptionRegions.SelectMany(region => new[] { region.HandlerOffset, region.TryOffset, region.HandlerEnd, region.TryEnd, region.FilterOffset })).ToHashSet();
		for (var index = 0; index < instructions.Length; index++)
		{
			var instruction = instructions[index];
			if (IsIntegerDivision(instruction.OpCode) && states.TryGetValue(instruction.Offset, out var arithmeticStack) &&
				!arithmeticStack.IsEmpty && arithmeticStack[^1] == CilStackValueKind.Int64)
			{
				var unsigned = instruction.OpCode == OpCodes.Div_Un || instruction.OpCode == OpCodes.Rem_Un;
				var remainder = instruction.OpCode == OpCodes.Rem || instruction.OpCode == OpCodes.Rem_Un;
				var type = IntegralType(true, unsigned);
				var divisionSignature = new MethodSignature<CilType>(new SignatureHeader(0), type, 2, 0, ImmutableArray.Create(type, type));
				var divisionName = (remainder ? "Remainder" : "Divide") + (unsigned ? "Unsigned" : "Signed");
				RegisterNumericCall(method, instruction, "Integer64Arithmetic", divisionName, divisionSignature);
				instructions[index] = instruction with { OpCode = OpCodes.Call, Operand = 0 };
				continue;
			}
			if (_floatingPointMode != M68kFloatingPointMode.Disabled && FloatingComparisonName(instruction.OpCode) is { } comparisonName &&
				states.TryGetValue(instruction.Offset, out var comparisonStack) && !comparisonStack.IsEmpty &&
				comparisonStack[^1] is CilStackValueKind.Float32 or CilStackValueKind.Float64)
			{
				var single = comparisonStack[^1] == CilStackValueKind.Float32;
				var words = single ? 1 : 2;
				if (comparisonStack.Length < words * 2 || comparisonStack[^(words + 1)] != comparisonStack[^1])
					throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedInstruction,
						"Floating comparison operands require matching precisions.", method.DisplayName, instruction.Offset);
				var type = FloatingType(single);
				var comparisonSignature = new MethodSignature<CilType>(new SignatureHeader(0), new CilType(CilTypeKind.Boolean, 1, "bool"), 2, 0, ImmutableArray.Create(type, type));
				RegisterNumericCall(method, instruction, "FloatingPointArithmetic", comparisonName + (single ? "Single" : "Double"), comparisonSignature);
				var call = instruction with { OpCode = OpCodes.Call, Operand = 0 };
				if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
				{
					// Every two-operand branch occupies at least two original IL bytes.
					// Use its operand byte for a synthetic Boolean branch. Existing branch
					// targets and EH boundaries still enter the registered call at the
					// original instruction offset, and fall through at its original end.
					var branchOffset = instruction.Offset + 1;
					comparisonBranches.Add(instruction.Offset, instruction with { Offset = branchOffset, OpCode = OpCodes.Brtrue });
					call = call with { NextOffset = branchOffset };
				}
				instructions[index] = call;
				continue;
			}
			if (_floatingPointMode == M68kFloatingPointMode.SoftFloat && IsFloatingBinary(instruction.OpCode) &&
				states.TryGetValue(instruction.Offset, out var binaryStack) && !binaryStack.IsEmpty &&
				binaryStack[^1] is CilStackValueKind.Float32 or CilStackValueKind.Float64)
			{
				var single = binaryStack[^1] == CilStackValueKind.Float32;
				var words = single ? 1 : 2;
				if (binaryStack.Length < words * 2 || binaryStack[^(words + 1)] != binaryStack[^1])
					throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedInstruction,
						"Floating binary operands require matching precisions.", method.DisplayName, instruction.Offset);
				var type = FloatingType(single);
				var binarySignature = new MethodSignature<CilType>(new SignatureHeader(0), type, 2, 0, ImmutableArray.Create(type, type));
				var binaryName = instruction.OpCode == OpCodes.Add ? "Add" : instruction.OpCode == OpCodes.Sub ? "Subtract" :
					instruction.OpCode == OpCodes.Mul ? "Multiply" : instruction.OpCode == OpCodes.Div ? "Divide" : "Remainder";
				RegisterNumericCall(method, instruction, "FloatingPointArithmetic", binaryName + (single ? "Single" : "Double"), binarySignature);
				instructions[index] = instruction with { OpCode = OpCodes.Call, Operand = 0 };
				continue;
			}
			if (_floatingPointMode != M68kFloatingPointMode.Disabled && instruction.OpCode == OpCodes.Neg &&
				states.TryGetValue(instruction.Offset, out var unaryStack) && !unaryStack.IsEmpty &&
				unaryStack[^1] is CilStackValueKind.Float32 or CilStackValueKind.Float64)
			{
				var single = unaryStack[^1] == CilStackValueKind.Float32;
				var type = FloatingType(single);
				var unarySignature = new MethodSignature<CilType>(new SignatureHeader(0), type, 1, 0, ImmutableArray.Create(type));
				RegisterNumericCall(method, instruction, "FloatingPointArithmetic", single ? "NegateSingle" : "NegateDouble", unarySignature);
				instructions[index] = instruction with { OpCode = OpCodes.Call, Operand = 0 };
				continue;
			}
			if (_floatingPointMode == M68kFloatingPointMode.Disabled || instruction.OpCode.Name?.StartsWith("conv.", StringComparison.Ordinal) != true ||
				!states.TryGetValue(instruction.Offset, out var stack) || stack.IsEmpty) continue;
			var source = stack[^1];
			var op = instruction.OpCode;
			var target = op;
			if (op == OpCodes.Conv_R_Un && index + 1 < instructions.Length &&
				instructions[index + 1].Offset == instruction.NextOffset &&
				!branchTargets.Contains(instructions[index + 1].Offset) &&
				(instructions[index + 1].OpCode == OpCodes.Conv_R4 || instructions[index + 1].OpCode == OpCodes.Conv_R8))
			{
				// An unsigned integer-to-Single conversion must round directly to
				// binary32; an intermediate rounded Double could change a halfway case.
				target = instructions[index + 1].OpCode;
				instructions[index + 1] = instructions[index + 1] with { OpCode = OpCodes.Nop, Operand = null };
			}
			var floatingSource = source is CilStackValueKind.Float32 or CilStackValueKind.Float64;
			string? name = null;
			CilType? input = null; CilType? output = null;
			if (op == OpCodes.Conv_R4 || op == OpCodes.Conv_R8 || op == OpCodes.Conv_R_Un)
			{
				var single = target == OpCodes.Conv_R4;
				output = FloatingType(single);
				if (floatingSource && op != OpCodes.Conv_R_Un)
				{
					input = FloatingType(source == CilStackValueKind.Float32);
					name = (source == CilStackValueKind.Float32 ? "Single" : "Double") + "To" + (single ? "Single" : "Double");
				}
				else if (!floatingSource && source is (CilStackValueKind.Int64 or CilStackValueKind.Int32 or
					CilStackValueKind.SignedByte or CilStackValueKind.UnsignedByte or CilStackValueKind.SignedWord or
					CilStackValueKind.UnsignedWord or CilStackValueKind.BooleanByte))
				{
					var unsigned = op == OpCodes.Conv_R_Un;
					var wide = source == CilStackValueKind.Int64;
					input = IntegralType(wide, unsigned);
					name = (unsigned ? "UInt" : "Int") + (wide ? "64" : "32") + "To" + (single ? "Single" : "Double");
				}
			}
			else if (floatingSource && (op == OpCodes.Conv_I4 || op == OpCodes.Conv_U4 || op == OpCodes.Conv_I ||
				op == OpCodes.Conv_U || op == OpCodes.Conv_I8 || op == OpCodes.Conv_U8))
			{
				var wide = op == OpCodes.Conv_I8 || op == OpCodes.Conv_U8;
				var unsigned = op == OpCodes.Conv_U4 || op == OpCodes.Conv_U || op == OpCodes.Conv_U8;
				input = FloatingType(source == CilStackValueKind.Float32);
				output = IntegralType(wide, unsigned);
				name = (source == CilStackValueKind.Float32 ? "Single" : "Double") + "To" + (unsigned ? "UInt" : "Int") + (wide ? "64" : "32");
			}
			else if (!floatingSource) continue;
			if (name is null)
				throw new M68kCompilationException(M68kDiagnosticIds.UnsupportedInstruction,
					$"Numeric conversion '{op.Name}' from '{source}' is not supported.", method.DisplayName, instruction.Offset);
			var signature = new MethodSignature<CilType>(new SignatureHeader(0), output!, 1, 0, ImmutableArray.Create(input!));
			RegisterNumericCall(method, instruction, "FloatingPointConversions", name, signature);
			instructions[index] = instruction with { OpCode = OpCodes.Call, Operand = 0 };
		}
		var result = method with { Instructions = instructions.SelectMany(instruction => comparisonBranches.TryGetValue(instruction.Offset, out var branch)
			? new[] { instruction, branch } : new[] { instruction }).ToArray() };
		_numericMethods[method.Identity] = result;
		return result;
	}

	private void RegisterNumericCall(CilMethod method, CilInstruction instruction, string typeName, string name, MethodSignature<CilType> signature)
	{
		var helper = TryResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime." + typeName, name, signature)
			?? throw new M68kCompilationException(M68kDiagnosticIds.InvalidInput,
				$"Numeric operation requires CopperSharp.Runtime.Managed {typeName}::{name}.", method.DisplayName, instruction.Offset);
		_numericCalls[(method.Identity, instruction.Offset)] = MethodReference.ForDefinition(helper);
	}

	private static string? FloatingComparisonName(OpCode op) => op.Name switch {
		"ceq" or "beq" or "beq.s" => "Equal",
		"bne.un" or "bne.un.s" => "NotEqualOrUnordered",
		"cgt" or "bgt" or "bgt.s" => "Greater",
		"clt" or "blt" or "blt.s" => "Less",
		"bge" or "bge.s" => "GreaterOrEqual",
		"ble" or "ble.s" => "LessOrEqual",
		"cgt.un" or "bgt.un" or "bgt.un.s" => "GreaterOrUnordered",
		"clt.un" or "blt.un" or "blt.un.s" => "LessOrUnordered",
		"bge.un" or "bge.un.s" => "GreaterOrEqualOrUnordered",
		"ble.un" or "ble.un.s" => "LessOrEqualOrUnordered",
		_ => null
	};

	private static bool IsFloatingBinary(OpCode op) => op == OpCodes.Add || op == OpCodes.Sub || op == OpCodes.Mul || op == OpCodes.Div || op == OpCodes.Rem;

	private static bool IsIntegerDivision(OpCode op) => op == OpCodes.Div || op == OpCodes.Div_Un || op == OpCodes.Rem || op == OpCodes.Rem_Un;

	private static CilType FloatingType(bool single) => new(CilTypeKind.FloatingPoint, single ? 4 : 8, single ? "float" : "double");
	private static CilType IntegralType(bool wide, bool unsigned) => new(unsigned ? CilTypeKind.UnsignedInteger : CilTypeKind.SignedInteger,
		wide ? 8 : 4, unsigned ? wide ? "ulong" : "uint" : wide ? "long" : "int");
}
