namespace Cilica.AsmIO;

/// <summary> Explicit transformations allowed when rewriting a managed image. </summary>
[Flags]
public enum ModuleWriteOptions
{
    None = 0,
    /// <summary> Retain the public key and reserve an empty strong-name signature. </summary>
    PublicSign = 1 << 0,
    /// <summary> Remove Authenticode and strong-name signing, including the assembly public key. </summary>
    StripSignatures = 1 << 1,
    /// <summary> Discard ReadyToRun code and emit retained IL for JIT compilation. </summary>
    StripReadyToRun = 1 << 2
}
