/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// CIL compares promoted integers. A narrow CMP is valid only when both
/// operands fit its domain, and unsigned narrow domains need unsigned flags.
/// </summary>
internal static class M68kComparisonPromotion
{
	public static void Run(M68kMachineFunction function)
	{
		var constants = function.Blocks.SelectMany(b => b.Instructions)
			.Where(i => i.Operation == M68kMachineOperation.Constant && i.Definitions.Length == 1 && i.ConstantValue is not null)
			.ToDictionary(i => i.Definitions[0], i => i.ConstantValue!.Value);
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (instruction.Uses.Length != 2 || !(instruction.Operation == M68kMachineOperation.Compare ||
					instruction.Operation == M68kMachineOperation.ConditionalBranch &&
					instruction.BranchCondition is null or { SourceKind: M68kMachineConditionSourceKind.Compare })) continue;
				var left = function.Values[instruction.Uses[0]];
				var right = function.Values[instruction.Uses[1]];
				if (!IsNarrow(left) && !IsNarrow(right)) continue;
				if (left.Width == M68kMachineValueWidth.LongPair || right.Width == M68kMachineValueWidth.LongPair ||
					left.Kind is CilStackValueKind.Float32 or CilStackValueKind.Float64 ||
					right.Kind is CilStackValueKind.Float32 or CilStackValueKind.Float64) continue;
				if (IsNarrow(left) && FitsDomain(right, left, constants))
				{
					if (IsUnsigned(left.Kind))
						block.Instructions[index] = instruction with
						{
							SourceInstruction = Unsigned(instruction.SourceInstruction),
							BranchCondition = instruction.BranchCondition is { } branch ? branch with
							{
								Condition = Unsigned(branch.Condition),
								ProducerInstruction = Unsigned(branch.ProducerInstruction)
							} : null
						};
					continue;
				}
				var uses = instruction.Uses.ToArray();
				for (var operand = 0; operand < uses.Length; operand++)
				{
					var value = function.Values[uses[operand]];
					if (!IsNarrow(value)) continue;
					var promoted = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data);
					var op = value.Kind switch
					{
						CilStackValueKind.SignedByte => OpCodes.Conv_I1,
						CilStackValueKind.SignedWord => OpCodes.Conv_I2,
						CilStackValueKind.UnsignedWord => OpCodes.Conv_U2,
						_ => OpCodes.Conv_U1
					};
					block.Instructions.Insert(index++, function.CreateInstruction(M68kMachineOperation.Convert,
						instruction.IlOffset, uses: [value.Id], definitions: [promoted.Id],
						sourceInstruction: new CilInstruction(instruction.IlOffset, op, null, instruction.IlOffset)));
					uses[operand] = promoted.Id;
				}
				block.Instructions[index] = instruction with { Uses = uses.ToImmutableArray() };
			}
		}
	}

	private static bool IsNarrow(M68kMachineValue value) => value.Width is M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word;
	private static bool IsUnsigned(CilStackValueKind kind) => kind is CilStackValueKind.UnsignedByte or CilStackValueKind.BooleanByte or CilStackValueKind.UnsignedWord;
	private static bool FitsDomain(M68kMachineValue right, M68kMachineValue left, Dictionary<int, M68kMachineConstant> constants)
	{
		if (right.Width == left.Width && right.Kind == left.Kind) return true;
		if (!constants.TryGetValue(right.Id, out var constant) || !constant.TryGetIntegral(out var number)) return false;
		return left.Kind switch
		{
			CilStackValueKind.SignedByte => number is >= sbyte.MinValue and <= sbyte.MaxValue,
			CilStackValueKind.SignedWord => number is >= short.MinValue and <= short.MaxValue,
			CilStackValueKind.UnsignedWord => number is >= 0 and <= ushort.MaxValue,
			_ => number is >= 0 and <= byte.MaxValue
		};
	}

	private static M68kCondition Unsigned(M68kCondition condition) => condition switch
	{
		M68kCondition.GreaterThan => M68kCondition.Higher,
		M68kCondition.GreaterOrEqual => M68kCondition.CarryClear,
		M68kCondition.LessThan => M68kCondition.CarrySet,
		M68kCondition.LessOrEqual => M68kCondition.LowerOrSame,
		_ => condition
	};
	private static CilInstruction? Unsigned(CilInstruction? source)
	{
		if (source is null) return null;
		var op = source.OpCode;
		var replacement = op == OpCodes.Cgt ? OpCodes.Cgt_Un : op == OpCodes.Clt ? OpCodes.Clt_Un :
			op == OpCodes.Bgt ? OpCodes.Bgt_Un : op == OpCodes.Bgt_S ? OpCodes.Bgt_Un_S :
			op == OpCodes.Bge ? OpCodes.Bge_Un : op == OpCodes.Bge_S ? OpCodes.Bge_Un_S :
			op == OpCodes.Blt ? OpCodes.Blt_Un : op == OpCodes.Blt_S ? OpCodes.Blt_Un_S :
			op == OpCodes.Ble ? OpCodes.Ble_Un : op == OpCodes.Ble_S ? OpCodes.Ble_Un_S : op;
		return replacement == op ? source : source with { OpCode = replacement };
	}
}
