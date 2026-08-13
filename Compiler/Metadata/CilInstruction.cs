/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Metadata;

internal sealed record CilInstruction(
	int Offset,
	OpCode OpCode,
	object? Operand,
	int NextOffset,
	int? ConstrainedTypeToken = null)
{
	public int Size => NextOffset - Offset;
}

internal static class CilInstructionDecoder
{
	private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue = CreateOpCodeMap();

	public static IReadOnlyList<CilInstruction> Decode(ReadOnlySpan<byte> il, string methodName)
	{
		var result = new List<CilInstruction>();
		var offset = 0;
		int? constrainedTypeToken = null;
		var constrainedOffset = -1;
		while (offset < il.Length)
		{
			var instructionOffset = offset;
			ushort value = il[offset++];
			if (value == 0xFE)
			{
				EnsureAvailable(il, offset, 1, methodName, instructionOffset);
				value = (ushort)(0xFE00 | il[offset++]);
			}

			if (!OpCodesByValue.TryGetValue(value, out var opCode))
			{
				throw InvalidIl(methodName, instructionOffset, $"Unknown CIL opcode 0x{value:X4}.");
			}

			object? operand = opCode.OperandType switch
			{
				OperandType.InlineNone => null,
				OperandType.ShortInlineI => ReadSByte(il, ref offset, methodName, instructionOffset),
				OperandType.InlineI => ReadInt32(il, ref offset, methodName, instructionOffset),
				OperandType.InlineI8 => ReadInt64(il, ref offset, methodName, instructionOffset),
				OperandType.ShortInlineR => ReadSingle(il, ref offset, methodName, instructionOffset),
				OperandType.InlineR => ReadDouble(il, ref offset, methodName, instructionOffset),
				OperandType.ShortInlineVar => ReadByte(il, ref offset, methodName, instructionOffset),
				OperandType.InlineVar => ReadUInt16(il, ref offset, methodName, instructionOffset),
				OperandType.ShortInlineBrTarget => ReadShortBranchTarget(il, ref offset, methodName, instructionOffset),
				OperandType.InlineBrTarget => ReadBranchTarget(il, ref offset, methodName, instructionOffset),
				OperandType.InlineSwitch => ReadSwitchTargets(il, ref offset, methodName, instructionOffset),
				OperandType.InlineField or
					OperandType.InlineMethod or
					OperandType.InlineSig or
					OperandType.InlineString or
					OperandType.InlineTok or
					OperandType.InlineType =>
					ReadInt32(il, ref offset, methodName, instructionOffset),
				_ => throw InvalidIl(
					methodName,
					instructionOffset,
					$"Unsupported operand encoding {opCode.OperandType}.")
			};

			if (opCode == OpCodes.Constrained)
			{
				if (constrainedTypeToken is not null)
				{
					throw InvalidIl(
						methodName,
						instructionOffset,
						"A constrained. prefix cannot follow another constrained. prefix.");
				}
				constrainedTypeToken = (int)operand!;
				constrainedOffset = instructionOffset;
				continue;
			}

			if (constrainedTypeToken is { } typeToken)
			{
				if (opCode != OpCodes.Callvirt && opCode != OpCodes.Call)
				{
					throw InvalidIl(
						methodName,
						instructionOffset,
						$"A constrained. prefix must be followed by call or callvirt, not '{opCode.Name}'.");
				}
				result.Add(new CilInstruction(
					constrainedOffset,
					opCode,
					operand,
					offset,
					typeToken));
				constrainedTypeToken = null;
				constrainedOffset = -1;
				continue;
			}

			result.Add(new CilInstruction(instructionOffset, opCode, operand, offset));
		}
		if (constrainedTypeToken is not null)
		{
			throw InvalidIl(
				methodName,
				constrainedOffset,
				"A constrained. prefix at the end of a method has no following call or callvirt.");
		}

		return result;
	}

	private static IReadOnlyDictionary<ushort, OpCode> CreateOpCodeMap()
	{
		var result = new Dictionary<ushort, OpCode>();
		foreach (var field in typeof(OpCodes).GetFields(
			System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
		{
			if (field.GetValue(null) is OpCode opCode)
			{
				result[unchecked((ushort)opCode.Value)] = opCode;
			}
		}

		return result;
	}

	private static byte ReadByte(
		ReadOnlySpan<byte> source,
		ref int offset,
		string method,
		int instructionOffset)
	{
		EnsureAvailable(source, offset, 1, method, instructionOffset);
		return source[offset++];
	}

	private static sbyte ReadSByte(
		ReadOnlySpan<byte> source,
		ref int offset,
		string method,
		int instructionOffset) =>
		unchecked((sbyte)ReadByte(source, ref offset, method, instructionOffset));

	private static ushort ReadUInt16(
		ReadOnlySpan<byte> source,
		ref int offset,
		string method,
		int instructionOffset)
	{
		EnsureAvailable(source, offset, 2, method, instructionOffset);
		var value = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
		offset += 2;
		return value;
	}

	private static int ReadInt32(
		ReadOnlySpan<byte> source,
		ref int offset,
		string method,
		int instructionOffset)
	{
		EnsureAvailable(source, offset, 4, method, instructionOffset);
		var value = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
		offset += 4;
		return value;
	}

	private static long ReadInt64(
		ReadOnlySpan<byte> source,
		ref int offset,
		string method,
		int instructionOffset)
	{
		EnsureAvailable(source, offset, 8, method, instructionOffset);
		var value = BinaryPrimitives.ReadInt64LittleEndian(source[offset..]);
		offset += 8;
		return value;
	}

	private static float ReadSingle(
		ReadOnlySpan<byte> source,
		ref int offset,
		string method,
		int instructionOffset) =>
		BitConverter.Int32BitsToSingle(ReadInt32(source, ref offset, method, instructionOffset));

	private static double ReadDouble(
		ReadOnlySpan<byte> source,
		ref int offset,
		string method,
		int instructionOffset) =>
		BitConverter.Int64BitsToDouble(ReadInt64(source, ref offset, method, instructionOffset));

	private static int ReadShortBranchTarget(
		ReadOnlySpan<byte> source,
		ref int offset,
		string method,
		int instructionOffset)
	{
		var delta = ReadSByte(source, ref offset, method, instructionOffset);
		return checked(offset + delta);
	}

	private static int ReadBranchTarget(
		ReadOnlySpan<byte> source,
		ref int offset,
		string method,
		int instructionOffset)
	{
		var delta = ReadInt32(source, ref offset, method, instructionOffset);
		return checked(offset + delta);
	}

	private static int[] ReadSwitchTargets(
		ReadOnlySpan<byte> source,
		ref int offset,
		string method,
		int instructionOffset)
	{
		var count = ReadInt32(source, ref offset, method, instructionOffset);
		if (count < 0 || count > (source.Length - offset) / 4)
		{
			throw InvalidIl(method, instructionOffset, "Invalid switch target count.");
		}

		var deltas = new int[count];
		for (var i = 0; i < deltas.Length; i++)
		{
			deltas[i] = ReadInt32(source, ref offset, method, instructionOffset);
		}

		var baseOffset = offset;
		for (var i = 0; i < deltas.Length; i++)
		{
			deltas[i] = checked(baseOffset + deltas[i]);
		}

		return deltas;
	}

	private static void EnsureAvailable(
		ReadOnlySpan<byte> source,
		int offset,
		int count,
		string method,
		int instructionOffset)
	{
		if (offset < 0 || count < 0 || offset > source.Length - count)
		{
			throw InvalidIl(method, instructionOffset, "CIL instruction operand extends past the method body.");
		}
	}

	private static M68kCompilationException InvalidIl(string method, int offset, string message) =>
		new(M68kDiagnosticIds.InvalidMetadata, message, method, offset);
}
