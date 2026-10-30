using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal sealed record Source(string Path, int Line, string Module);
internal sealed record ApiMember(ISymbol Symbol, string Kind, string Signature, string Anchor, XElement Documentation);
internal sealed record ApiType(INamedTypeSymbol Symbol, Source Source, string Kind, string Signature,
    string Href, string? Library, XElement Documentation, IReadOnlyList<ApiMember> Members);

internal static class Catalog
{
    private static IReadOnlyDictionary<string, XElement> documentation = new Dictionary<string, XElement>();
    private static readonly SymbolDisplayFormat SignatureFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility | SymbolDisplayMemberOptions.IncludeModifiers |
            SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeParameters |
            SymbolDisplayMemberOptions.IncludeConstantValue | SymbolDisplayMemberOptions.IncludeExplicitInterface,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName |
            SymbolDisplayParameterOptions.IncludeDefaultValue | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static IReadOnlyList<ApiType> Load(string repository)
    {
        var sources = SourceMap(Path.Combine(repository, "Sdk.Amiga"));
        using var routesJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "website/src/published-routes.json")));
        var routes = routesJson.RootElement.EnumerateArray().Where(e => e.GetProperty("kind").GetString() != "module").ToArray();
        var sdk = typeof(Amiga.Exec).Assembly.Location;
        documentation = XDocument.Load(Path.ChangeExtension(sdk, ".xml")).Root!.Element("members")!.Elements("member")
            .ToDictionary(e => e.Attribute("name")!.Value, StringComparer.Ordinal);
        var reference = MetadataReference.CreateFromFile(sdk);
        var runtime = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => !Path.GetFileName(path).StartsWith("CopperSharp.", StringComparison.Ordinal))
            .Select(path => MetadataReference.CreateFromFile(path));
        var compiler = MetadataReference.CreateFromFile(typeof(CopperSharp.Compiler.M68kRegister).Assembly.Location);
        var compilation = CSharpCompilation.Create("SdkReference", references: runtime.Append(compiler).Append(reference));
        var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
        var exportedTypes = Types(assembly.GlobalNamespace).OrderBy(t => t.ToDisplayString(), StringComparer.Ordinal).ToArray();
        var exportedNames = exportedTypes.Select(t => t.ToDisplayString()).ToHashSet(StringComparer.Ordinal);
        var claimedRoutes = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ApiType>();
        foreach (var type in exportedTypes)
        {
            if (!sources.TryGetValue(Key(type), out var source))
                throw new InvalidOperationException($"No SDK source found for exported type {type}");
            // Some examples are deliberately compiled into the package. They are not SDK API.
            if (source.Path.StartsWith("Examples/", StringComparison.Ordinal)) continue;
            var anchors = new Dictionary<string, int>(StringComparer.Ordinal);
            var members = new List<ApiMember>();
            foreach (var member in type.GetMembers().Where(IsDocumentedMember))
            {
                var kind = MemberKind(member);
                var name = member is IMethodSymbol { MethodKind: MethodKind.Constructor } ? type.Name : member.Name;
                var stem = Slug(kind + "-" + name);
                anchors.TryGetValue(stem, out var count);
                anchors[stem] = ++count;
                var signature = member is IFieldSymbol { IsFixedSizeBuffer: true, Type: IPointerTypeSymbol pointer } buffer
                    ? $"public fixed {pointer.PointedAtType.ToDisplayString(SignatureFormat)} {buffer.Name}[{buffer.FixedSize}]"
                    : member.ToDisplayString(SignatureFormat);
                members.Add(new(member, kind, signature,
                    count == 1 ? stem : $"{stem}-{count}", Documentation(member)));
            }
            var fullName = string.Join(".", ContainingTypes(type).Select(t => t.Name));
            var oldRoute = routes.FirstOrDefault(e => e.GetProperty("type").GetString() == type.ToDisplayString());
            // A number of formerly top-level enums now belong to their library class.
            if (oldRoute.ValueKind == JsonValueKind.Undefined)
            {
                var candidates = routes.Where(e => e.GetProperty("module").GetString() == source.Module &&
                    e.GetProperty("name").GetString() == type.Name &&
                    !exportedNames.Contains(e.GetProperty("type").GetString()!) &&
                    !claimedRoutes.Contains(e.GetProperty("href").GetString()!)).ToArray();
                if (candidates.Length == 1) oldRoute = candidates[0];
            }
            var href = oldRoute.ValueKind == JsonValueKind.Undefined
                ? $"/reference/{Slug(source.Module)}/{Slug(fullName)}" : oldRoute.GetProperty("href").GetString()!;
            claimedRoutes.Add(href);
            result.Add(new(type, source, TypeKind(type), TypeSignature(type),
                href, Library(type), Documentation(type), members));
        }
        var duplicate = result.GroupBy(t => t.Href).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new InvalidOperationException($"Conflicting reference route {duplicate.Key}");
        return result;
    }

    private static Dictionary<string, Source> SourceMap(string sdk)
    {
        var result = new Dictionary<string, Source>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(sdk, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(sdk, file).Replace('\\', '/');
            if (relative.Split('/').Any(p => p is "obj" or "bin")) continue;
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>()
                .Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                var namespaces = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
                    .Reverse().Select(n => n.Name.ToString());
                var names = declaration.AncestorsAndSelf().OfType<MemberDeclarationSyntax>()
                    .Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax).Reverse().Select(MetadataName);
                var ns = string.Join(".", namespaces);
                var key = (ns.Length > 0 ? ns + "." : "") + string.Join("+", names);
                var module = relative.Contains('/') ? relative.Split('/')[0] : "Core";
                result.TryAdd(key, new(relative, tree.GetLineSpan(declaration.Span).StartLinePosition.Line + 1, module));
            }
        }
        return result;
    }

    private static string MetadataName(MemberDeclarationSyntax node)
    {
        var (name, arity) = node switch
        {
            TypeDeclarationSyntax t => (t.Identifier.ValueText, t.TypeParameterList?.Parameters.Count ?? 0),
            EnumDeclarationSyntax e => (e.Identifier.ValueText, 0),
            DelegateDeclarationSyntax d => (d.Identifier.ValueText, d.TypeParameterList?.Parameters.Count ?? 0),
            _ => throw new ArgumentException("Expected a type declaration")
        };
        return arity == 0 ? name : name + "`" + arity;
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol ns) =>
        ns.GetNamespaceMembers().SelectMany(Types).Concat(ns.GetTypeMembers().SelectMany(Types));

    private static IEnumerable<INamedTypeSymbol> Types(INamedTypeSymbol type)
    {
        if (type.DeclaredAccessibility != Accessibility.Public || !type.CanBeReferencedByName ||
            type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.CompilerGeneratedAttribute")) yield break;
        yield return type;
        foreach (var nested in type.GetTypeMembers().SelectMany(Types)) yield return nested;
    }

    private static IEnumerable<INamedTypeSymbol> ContainingTypes(INamedTypeSymbol type) =>
        type.ContainingType is null ? [type] : ContainingTypes(type.ContainingType).Append(type);

    private static string Key(INamedTypeSymbol type) =>
        (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") +
        string.Join("+", ContainingTypes(type).Select(t => t.MetadataName));

    private static bool IsDocumentedMember(ISymbol symbol) => symbol.DeclaredAccessibility == Accessibility.Public &&
        !symbol.IsImplicitlyDeclared && symbol.ContainingType.TypeKind != Microsoft.CodeAnalysis.TypeKind.Delegate &&
        !symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.CompilerGeneratedAttribute") && symbol switch
    {
        IMethodSymbol m => m.ContainingType.TypeKind != Microsoft.CodeAnalysis.TypeKind.Enum &&
            m.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.UserDefinedOperator or MethodKind.Conversion,
        IFieldSymbol f => f.Name != "value__",
        IPropertySymbol => true,
        IEventSymbol => true,
        _ => false
    };

    private static string MemberKind(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } => "constructor",
        IMethodSymbol => "method",
        IFieldSymbol { ContainingType.TypeKind: Microsoft.CodeAnalysis.TypeKind.Enum } => "enum value",
        IFieldSymbol { IsConst: true } => "constant",
        IFieldSymbol => "field",
        IPropertySymbol => "property",
        IEventSymbol => "event",
        _ => throw new ArgumentException("Unsupported member")
    };

    private static string TypeKind(INamedTypeSymbol type) => type.TypeKind switch
    {
        Microsoft.CodeAnalysis.TypeKind.Class => type.IsStatic ? "static class" : "class",
        Microsoft.CodeAnalysis.TypeKind.Struct => type.IsRefLikeType ? "ref struct" : "struct",
        Microsoft.CodeAnalysis.TypeKind.Interface => "interface",
        Microsoft.CodeAnalysis.TypeKind.Enum => "enum",
        Microsoft.CodeAnalysis.TypeKind.Delegate => "delegate",
        _ => throw new ArgumentException("Unsupported type")
    };

    private static string TypeSignature(INamedTypeSymbol type)
    {
        var name = type.ToDisplayString(SignatureFormat);
        if (type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Delegate)
        {
            var invoke = type.DelegateInvokeMethod!;
            return $"public delegate {invoke.ReturnType.ToDisplayString(SignatureFormat)} {name}(" +
                string.Join(", ", invoke.Parameters.Select(p => p.ToDisplayString(SignatureFormat))) + ")";
        }
        var modifiers = type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Class && !type.IsStatic
            ? (type.IsAbstract ? "abstract " : "") + (type.IsSealed ? "sealed " : "") : "";
        var bases = type.Interfaces.Select(i => i.ToDisplayString(SignatureFormat)).ToList();
        if (type.BaseType is { SpecialType: not SpecialType.System_Object } parent &&
            type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Class) bases.Insert(0, parent.ToDisplayString(SignatureFormat));
        if (type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Enum) bases.Add(type.EnumUnderlyingType!.ToDisplayString(SignatureFormat));
        return "public " + modifiers + (type.IsReadOnly ? "readonly " : "") + TypeKind(type) + " " + name +
            (bases.Count > 0 ? " : " + string.Join(", ", bases) : "");
    }

    public static string Slug(string name) => Regex.Replace(
        Regex.Replace(name, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    public static XElement Documentation(ISymbol symbol)
    {
        var id = symbol.GetDocumentationCommentId();
        return id is not null && documentation.TryGetValue(id, out var element) ? element : new XElement("member");
    }

    public static string Text(XElement? element) => element is null ? "" : Regex.Replace(string.Concat(element.Nodes().Select(n => n switch
    {
        XText t => t.Value,
        XElement e when e.Name.LocalName is "see" or "seealso" => (e.Attribute("cref")?.Value ?? "").Replace("T:", ""),
        XElement e when e.Name.LocalName is "paramref" or "typeparamref" => e.Attribute("name")?.Value ?? "",
        XElement e => " " + Text(e) + " ",
        _ => ""
    })), @"\s+", " ").Trim();

    public static string? Library(INamedTypeSymbol? type)
    {
        if (type is null) return null;
        var attribute = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "AmigaLibraryAttribute");
        return attribute?.ConstructorArguments.FirstOrDefault().Value as string ?? Library(type.ContainingType);
    }

    public static string? AttributeValue(ImmutableArray<AttributeData> attributes, string name)
    {
        var attribute = attributes.FirstOrDefault(a => a.AttributeClass?.Name == name);
        if (attribute is null || attribute.ConstructorArguments.IsEmpty) return null;
        var value = attribute.ConstructorArguments[0];
        if (value.Type is INamedTypeSymbol { TypeKind: Microsoft.CodeAnalysis.TypeKind.Enum } type)
            return type.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value.Value))?.Name;
        return Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
