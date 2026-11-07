namespace Cilica.IR;

public class UnaryInst : Instruction
{
    public UnaryOp Op { get => _op; set => SetProperty(ref _op, value); }

    private UnaryOp _op;

    public Value Value {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public override string InstName => Op.ToString().ToLower();

    public UnaryInst(UnaryOp op, Value value)
        : base(value)
    {
        Op = op;
        ResultType = GetResultType(value.ResultType);
    }

    internal static TypeDesc GetResultType(TypeDesc operandType)
        => operandType.StackType == StackType.Int && operandType.Kind.Size() < 4 ? PrimType.Int32 : operandType;

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public enum UnaryOp
{
    Neg, // int/long/nint
    Not, // int/long/nint
    FNeg // float/double
}
