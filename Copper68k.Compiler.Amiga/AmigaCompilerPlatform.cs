using System.Collections.ObjectModel;
using Copper68k.AmigaSdk;

namespace Copper68k.Compiler.Amiga;

public sealed record AmigaCompilationOptions
{
	public IReadOnlyDictionary<string, uint> LibraryBases { get; init; } =
		new ReadOnlyDictionary<string, uint>(new Dictionary<string, uint>());
}

public static class AmigaLibraryBaseSymbols
{
	public static string For(string libraryName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(libraryName);
		return $"__c68k_amiga_library_base:{libraryName}";
	}
}

public sealed class AmigaExternalCallResolver : IM68kExternalCallResolver
{
	private static readonly string LibraryAttributeName = typeof(AmigaLibraryAttribute).FullName!;
	private static readonly string LvoAttributeName = typeof(AmigaLvoAttribute).FullName!;
	private static readonly string RegisterAttributeName = typeof(M68kRegisterAttribute).FullName!;
	private readonly AmigaCompilationOptions _options;

	public AmigaExternalCallResolver(AmigaCompilationOptions? options = null)
	{
		_options = options ?? new AmigaCompilationOptions();
	}

	public bool TryResolve(
		M68kExternalMethod method,
		out M68kExternalCallConvention convention)
	{
		var methodLibrary = Find(method.MethodAttributes, LibraryAttributeName);
		var typeLibrary = Find(method.DeclaringTypeAttributes, LibraryAttributeName);
		var lvo = Find(method.MethodAttributes, LvoAttributeName);
		if (methodLibrary is null && typeLibrary is null && lvo is null)
		{
			convention = null!;
			return false;
		}
		if (lvo is null)
		{
			throw Invalid(method, "[AmigaLibrary] requires [AmigaLvo] on an external method.");
		}
		var libraryAttribute = methodLibrary ?? typeLibrary ??
			throw Invalid(method, "[AmigaLvo] requires [AmigaLibrary] on the method or declaring type.");
		if (!method.IsStatic)
		{
			throw Unsupported(method, "Amiga library vector declarations must be static.");
		}

		var (name, policy) = DecodeLibrary(method, libraryAttribute);
		var offset = DecodeLvo(method, lvo);
		var parameterRegisters = DecodeParameterRegisters(method);
		var returnRegister = DecodeReturnRegister(method);
		convention = CreateConvention(
			method,
			name,
			policy,
			offset,
			parameterRegisters,
			returnRegister);
		return true;
	}

	private M68kExternalCallConvention CreateConvention(
		M68kExternalMethod method,
		string name,
		AmigaLibraryBasePolicy policy,
		short offset,
		IReadOnlyList<M68kRegister>? parameterRegisters = null,
		M68kRegister returnRegister = M68kRegister.D0) =>
		policy switch
		{
			AmigaLibraryBasePolicy.ExecBase => new M68kExternalCallConvention(
				name,
				M68kExternalBaseSource.CachedPointer,
				M68kRegister.A6,
				offset,
				CacheRegister: M68kRegister.A5,
				SourceAddress: 4,
				ParameterRegisters: parameterRegisters,
				ReturnRegister: returnRegister),
			AmigaLibraryBasePolicy.Cached => new M68kExternalCallConvention(
				name,
				M68kExternalBaseSource.WritableSlot,
				M68kRegister.A6,
				offset,
				InitialValue: _options.LibraryBases.TryGetValue(name, out var cached) ? cached : 0,
				SlotSymbol: AmigaLibraryBaseSymbols.For(name),
				ParameterRegisters: parameterRegisters,
				ReturnRegister: returnRegister),
			AmigaLibraryBasePolicy.Provided => new M68kExternalCallConvention(
				name,
				M68kExternalBaseSource.Immediate,
				M68kRegister.A6,
				offset,
				InitialValue: GetProvidedBase(method, name),
				ParameterRegisters: parameterRegisters,
				ReturnRegister: returnRegister),
			_ => throw Invalid(method, $"Unknown Amiga library base policy {policy}.")
		};

	private uint GetProvidedBase(M68kExternalMethod method, string name) =>
		_options.LibraryBases.TryGetValue(name, out var address)
			? address
			: throw new M68kCompilationException(
				M68kDiagnosticIds.UnresolvedImport,
				$"No base address was supplied for Amiga library '{name}'.",
				method.DisplayName);

	private static (string Name, AmigaLibraryBasePolicy Policy) DecodeLibrary(
		M68kExternalMethod method,
		M68kMetadataAttribute attribute)
	{
		if (attribute.FixedArguments.Count is < 1 or > 2 ||
			attribute.FixedArguments[0] is not string name ||
			string.IsNullOrWhiteSpace(name))
		{
			throw Invalid(method, "[AmigaLibrary] must contain a non-empty library name.");
		}
		var value = attribute.FixedArguments.Count == 1
			? (int)AmigaLibraryBasePolicy.Cached
			: attribute.FixedArguments[1] as int?;
		if (value is null || !Enum.IsDefined(typeof(AmigaLibraryBasePolicy), value.Value))
		{
			throw Invalid(method, "[AmigaLibrary] contains an invalid base policy.");
		}
		return (name, (AmigaLibraryBasePolicy)value.Value);
	}

	private static short DecodeLvo(
		M68kExternalMethod method,
		M68kMetadataAttribute attribute)
	{
		if (attribute.FixedArguments.Count != 1 || attribute.FixedArguments[0] is not int offset)
		{
			throw Invalid(method, "[AmigaLvo] must contain one signed byte offset.");
		}
		if (offset >= 0 || offset < short.MinValue || (offset & 1) != 0)
		{
			throw Invalid(method, $"[AmigaLvo] offset {offset} must be a negative, word-aligned signed 16-bit displacement.");
		}
		return (short)offset;
	}

	private static M68kMetadataAttribute? Find(
		IReadOnlyList<M68kMetadataAttribute> attributes,
		string typeName) =>
		attributes.FirstOrDefault(attribute =>
			string.Equals(attribute.TypeName, typeName, StringComparison.Ordinal));

	private static IReadOnlyList<M68kRegister>? DecodeParameterRegisters(
		M68kExternalMethod method)
	{
		if (method.ParameterAttributes.Count == 0)
		{
			return null;
		}
		var result = new M68kRegister[method.ParameterAttributes.Count];
		for (var index = 0; index < result.Length; index++)
		{
			result[index] = DecodeRegister(
				method,
				method.ParameterAttributes[index],
				$"parameter {index}") ??
				throw Unsupported(method, $"Amiga call parameter {index} requires [M68kRegister].");
		}
		return result;
	}

	private static M68kRegister DecodeReturnRegister(M68kExternalMethod method) =>
		DecodeRegister(method, method.ReturnAttributes, "return value") ?? M68kRegister.D0;

	private static M68kRegister? DecodeRegister(
		M68kExternalMethod method,
		IReadOnlyList<M68kMetadataAttribute> attributes,
		string role)
	{
		var attribute = Find(attributes, RegisterAttributeName);
		if (attribute is null)
		{
			return null;
		}
		if (attribute.FixedArguments.Count != 1 ||
			attribute.FixedArguments[0] is not int value ||
			!Enum.IsDefined(typeof(M68kRegister), value))
		{
			throw Invalid(method, $"Invalid [M68kRegister] on {role}.");
		}
		return (M68kRegister)value;
	}

	private static M68kCompilationException Invalid(M68kExternalMethod method, string message) =>
		new(M68kDiagnosticIds.InvalidMetadata, message, method.DisplayName);

	private static M68kCompilationException Unsupported(M68kExternalMethod method, string message) =>
		new(M68kDiagnosticIds.UnsupportedSignature, message, method.DisplayName);
}

public static class AmigaM68kCompiler
{
	public static M68kCompilationResult Compile(
		M68kCompilationRequest request,
		AmigaCompilationOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(request);
		return M68kCompiler.Compile(request with
		{
			ExternalCallResolvers =
			[
				new AmigaExternalCallResolver(options)
			]
		});
	}
}
