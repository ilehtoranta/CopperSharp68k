namespace Copper68k.AmigaSdk;

public enum AmigaLibraryBasePolicy
{
	ExecBase,
	Cached,
	Provided
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method)]
public sealed class AmigaLibraryAttribute : Attribute
{
	public AmigaLibraryAttribute(
		string name,
		AmigaLibraryBasePolicy basePolicy = AmigaLibraryBasePolicy.Cached)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
		BasePolicy = basePolicy;
	}

	public string Name { get; }

	public AmigaLibraryBasePolicy BasePolicy { get; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class AmigaLvoAttribute : Attribute
{
	public AmigaLvoAttribute(int offset)
	{
		Offset = offset;
	}

	public int Offset { get; }
}
