namespace Cilica;

public enum MethodCompilationStatus
{
    Unchanged,
    Optimized,
    Unsupported,
    Failed
}

public sealed class MethodCompilationResult(MethodDef method)
{
    public MethodDef Method { get; } = method;

    public MethodCompilationStatus Status { get; internal set; }

    public string? FailedPhase { get; internal set; }

    public Exception? Error { get; internal set; }
}

public sealed class CompilationResult(IReadOnlyList<MethodCompilationResult> methods)
{
    public IReadOnlyList<MethodCompilationResult> Methods { get; } = methods;

    public bool Succeeded => Methods.All(method => method.Status is MethodCompilationStatus.Unchanged or MethodCompilationStatus.Optimized);

    public void ThrowIfFailed()
    {
        if (!Succeeded) {
            throw new CompilationException(this);
        }
    }
}

public sealed class CompilationException(CompilationResult result)
    : InvalidOperationException($"Compilation failed for {result.Methods.Count(method => method.Error != null)} method(s)")
{
    public CompilationResult Result { get; } = result;
}
