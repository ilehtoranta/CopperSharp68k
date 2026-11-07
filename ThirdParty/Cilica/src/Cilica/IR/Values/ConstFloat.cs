namespace Cilica.IR;

using System.Globalization;

public class ConstFloat : Const
{
    public double Value { get; private init; }
    public bool IsSingle => ResultType.Kind == TypeKind.Single;
    public bool IsDouble => ResultType.Kind == TypeKind.Double;

    private ConstFloat() { }

    public static ConstFloat CreateS(float value) => Create(PrimType.Single, value);
    public static ConstFloat CreateD(double value) => Create(PrimType.Double, value);

    public static ConstFloat Create(TypeDesc type, double value)
    {
        Ensure.That(type.StackType == StackType.Float);
        return new ConstFloat() { ResultType = type, Value = type.Kind == TypeKind.Single ? (float)value : value };
    }

    public override void Print(PrintContext ctx)
    {
        string str = Value.ToString(CultureInfo.InvariantCulture);
        if (double.IsFinite(Value) && !str.Contains('.') && !str.Contains('E')) str += ".0";
        if (IsSingle) str += "f";

        ctx.Print(str, PrintToner.Number);
    }

    public override bool Equals(Const? other) => other is ConstFloat o &&
        BitConverter.DoubleToInt64Bits(o.Value) == BitConverter.DoubleToInt64Bits(Value) && o.ResultType == ResultType;
    public override int GetHashCode() => BitConverter.DoubleToInt64Bits(Value).GetHashCode();
}
