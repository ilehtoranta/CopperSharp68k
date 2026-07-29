/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Amiga;
using CopperSharp.Compiler;

namespace PolymorphismExample;

public static class Program
{
	[M68kEntryPoint]
	public static int Main(int argLength, CONST_STRPTR argText)
	{
		// Select a concrete class at run time, but retain only a base-class
		// reference. This call is dispatched through the object's vtable.
		Calculation calculation = argLength == 0
			? new Addition()
			: new Multiplication();
		var baseResult = calculation.Apply(6);

		// Select through the interface type as well. This call is resolved
		// through the object's interface map and method table.
		ICalculation contract = argLength == 0
			? new Addition()
			: new Multiplication();
		var interfaceResult = contract.Apply(7);

		// The process result makes both calls observable without requiring any
		// platform imports: addition returns 21, multiplication returns 52.
		return baseResult + interfaceResult;
	}
}

public interface ICalculation
{
	int Apply(int value);
}

public abstract class Calculation
{
	// This managed reference lives in the base portion of every derived object.
	// It also makes the example exercise inherited reference-field layout.
	private readonly Operand _operand;

	protected Calculation(int operand)
	{
		_operand = new Operand(operand);
	}

	public abstract int Apply(int value);

	protected int OperandValue => _operand.Value;
}

public sealed class Addition : Calculation, ICalculation
{
	public Addition()
		: base(4)
	{
	}

	public override int Apply(int value) => value + OperandValue;
}

public sealed class Multiplication : Calculation, ICalculation
{
	public Multiplication()
		: base(4)
	{
	}

	public override int Apply(int value) => value * OperandValue;
}

public sealed class Operand
{
	public Operand(int value)
	{
		Value = value;
	}

	public int Value { get; }
}
