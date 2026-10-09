namespace CopperSharp.Compiler.Tests;

// Skip only absent opt-in configuration. Configured paths that are broken must fail.
internal static class NativeOsIntegrationConfiguration
{
    public static string? SkipReason
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COPPERSHARP_KICKSTART31_ROM")))
                return "Set COPPERSHARP_KICKSTART31_ROM to run native OS integration tests.";
            return null;
        }
    }
}

public sealed class NativeOsIntegrationTheoryAttribute : TheoryAttribute
{
    public NativeOsIntegrationTheoryAttribute() => Skip = NativeOsIntegrationConfiguration.SkipReason;
}

public sealed class NativeOsIntegrationFactAttribute : FactAttribute
{
    public NativeOsIntegrationFactAttribute() => Skip = NativeOsIntegrationConfiguration.SkipReason;
}
