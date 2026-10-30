using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

if (args.Length != 5)
    throw new ArgumentException("Usage: ReferenceGenerator <repository> <output> <base-path> <origin> <source-revision>");
var repository = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var basePath = args[2];
var origin = args[3];
var sourceRevision = args[4];
var types = Catalog.Load(repository);
var shell = File.ReadAllText(Path.Combine(repository, "website/src/reference-shell.html"));
var version = typeof(Amiga.Exec).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
var modules = types.GroupBy(t => t.Source.Module).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
var entries = new List<SearchEntry>();
var paths = new List<string>();

foreach (var module in modules)
{
    var moduleHref = "/reference/" + Catalog.Slug(module.Key);
    entries.Add(new(module.Key, "module", module.Key, null,
        $"{module.Count()} types · {module.Sum(t => t.Members.Count)} members", null, null, moduleHref));
    var content = new StringBuilder($"<article class=\"reference-document\">{Breadcrumb(module.Key)}" +
        $"<header class=\"reference-document-header\"><p class=\"section-kicker\">SDK MODULE</p><h1>{H(module.Key)}</h1>" +
        $"<p>{module.Count()} public types · {module.Sum(t => t.Members.Count)} declared members</p></header>" +
        "<div class=\"reference-module-grid\">");
    foreach (var type in module)
    {
        content.Append($"<a class=\"reference-module-card\" href=\"{Url(type.Href)}\"><span>{H(type.Symbol.Name)}</span>" +
            $"<p>{H(type.Kind)}</p><p>{H(Catalog.Text(type.Documentation.Element("summary")))}</p></a>");
    }
    content.Append("</div></article>");
    Write(moduleHref, module.Key, $"Public {module.Key} types in CopperSharp.Sdk.Amiga.", content.ToString());
}

foreach (var type in types)
{
    var fullName = type.Symbol.ToDisplayString();
    var summary = Catalog.Text(type.Documentation.Element("summary"));
    entries.Add(new(type.Symbol.Name, type.Kind, type.Source.Module, fullName, type.Signature, summary, type.Library, type.Href));
    var source = "https://github.com/ilehtoranta/CopperSharp68k/blob/" + sourceRevision + "/Sdk.Amiga/" +
        string.Join("/", type.Source.Path.Split('/').Select(Uri.EscapeDataString)) + "#L" + type.Source.Line;
    var content = new StringBuilder($"<article class=\"reference-document\">{Breadcrumb(type.Source.Module, type.Symbol.Name)}" +
        "<header class=\"reference-document-header reference-type-header\"><div class=\"reference-type-labels\">" +
        $"<span class=\"reference-kind\">{H(type.Kind)}</span><code>{H(type.Library)}</code></div>" +
        $"<h1>{H(type.Symbol.Name)}</h1><p class=\"reference-full-name\">{H(fullName)}</p>" +
        $"<pre class=\"reference-signature\"><code>{H(type.Signature)}</code></pre>{Documentation(type.Documentation)}" +
        $"<div class=\"reference-source-row\"><span>{type.Members.Count} declared members</span>" +
        $"<a href=\"{H(source)}\">View source ↗</a></div></header>" +
        "<div class=\"reference-type-layout\"><aside class=\"reference-member-index\" aria-label=\"Members on this page\"><span>MEMBERS</span>");
    foreach (var member in type.Members)
        content.Append($"<a href=\"#{member.Anchor}\">{H(MemberName(member.Symbol))}</a>");
    content.Append("</aside><div>");
    foreach (var group in type.Members.GroupBy(m => m.Kind))
    {
        content.Append($"<section class=\"reference-member-section\"><h2>{H(group.Key)} declarations</h2>");
        foreach (var member in group)
        {
            var name = MemberName(member.Symbol);
            entries.Add(new(name, member.Kind, type.Source.Module, fullName, member.Signature,
                Catalog.Text(member.Documentation.Element("summary")), type.Library, type.Href + "#" + member.Anchor));
            content.Append($"<article class=\"reference-member\" id=\"{member.Anchor}\"><header>" +
                $"<span class=\"reference-kind\">{H(member.Kind)}</span><a href=\"#{member.Anchor}\" aria-label=\"Link to {H(name)}\">#</a></header>" +
                $"<h3>{H(name)}</h3><pre><code>{H(member.Signature)}</code></pre>");
            var lvo = Catalog.AttributeValue(member.Symbol.GetAttributes(), "AmigaLvoAttribute");
            var registers = new List<string>();
            if (member.Symbol is IMethodSymbol method)
            {
                foreach (var parameter in method.Parameters)
                {
                    var register = Catalog.AttributeValue(parameter.GetAttributes(), "M68kRegisterAttribute");
                    if (register is not null) registers.Add(parameter.Name + ": " + register);
                }
                var returned = Catalog.AttributeValue(method.GetReturnTypeAttributes(), "M68kRegisterAttribute");
                if (returned is not null) registers.Add("return: " + returned);
            }
            if (lvo is not null || registers.Count > 0)
            {
                content.Append("<dl class=\"reference-abi\">");
                if (lvo is not null) content.Append($"<div><dt>LVO</dt><dd>{H(lvo)}</dd></div>");
                if (registers.Count > 0) content.Append($"<div><dt>Registers</dt><dd>{H(string.Join(" · ", registers))}</dd></div>");
                content.Append("</dl>");
            }
            content.Append(Documentation(member.Documentation));
            content.Append("</article>");
        }
        content.Append("</section>");
    }
    content.Append("</div></div></article>");
    Write(type.Href, type.Symbol.Name, summary.Length > 0 ? summary : fullName + " API reference.", content.ToString());
}

var home = new StringBuilder("<section class=\"reference-intro\" aria-labelledby=\"reference-title\">" +
    "<p class=\"section-kicker\">COPPERSHARP.SDK.AMIGA</p><div class=\"reference-title-row\"><div>" +
    "<h1 id=\"reference-title\">SDK<br/><em>reference.</em></h1>" +
    $"<p class=\"reference-lede\">Public types and members generated from CopperSharp.Sdk.Amiga {H(version)}. " +
    "Descriptions come from the package XML documentation.</p></div>" +
    $"<dl class=\"reference-stats\" aria-label=\"Reference coverage\"><div><dt>Modules</dt><dd>{modules.Length}</dd></div>" +
    $"<div><dt>Types</dt><dd>{types.Count}</dd></div><div><dt>Members</dt><dd>{types.Sum(t => t.Members.Count)}</dd></div></dl></div>" +
    "<form class=\"reference-search\" role=\"search\" onsubmit=\"return false\"><label for=\"sdk-search\">Search the SDK</label>" +
    "<div class=\"reference-search-controls\"><input id=\"sdk-search\" type=\"search\" placeholder=\"Name, signature, module, or library\"/>" +
    "<select id=\"sdk-kind-filter\" aria-label=\"Declaration kind\"><option value=\"\">All declarations</option>" +
    "<option value=\"type\">Types</option><option value=\"method\">Methods</option><option value=\"constructor\">Constructors</option>" +
    "<option value=\"property\">Properties</option><option value=\"field\">Fields and constants</option><option value=\"event\">Events</option></select></div>" +
    "<p id=\"sdk-search-status\" role=\"status\">Type a name, signature, module, or library.</p></form></section>" +
    "<section aria-label=\"SDK search results\"><div id=\"sdk-search-results\" class=\"reference-results\" hidden></div></section>" +
    "<section id=\"sdk-module-browser\" class=\"reference-modules\" aria-labelledby=\"module-heading\">" +
    "<div class=\"reference-section-heading\"><p class=\"section-kicker\">MODULES</p><h2 id=\"module-heading\">Browse the package surface.</h2></div>" +
    "<div class=\"reference-module-grid\">");
foreach (var module in modules)
    home.Append($"<a class=\"reference-module-card\" href=\"{Url("/reference/" + Catalog.Slug(module.Key))}\"><span>{H(module.Key)}</span>" +
        $"<dl><div><dt>Types</dt><dd>{module.Count()}</dd></div><div><dt>Members</dt><dd>{module.Sum(t => t.Members.Count)}</dd></div></dl>" +
        "<i aria-hidden=\"true\">→</i></a>");
home.Append("</div></section>");
Write("/reference", "SDK reference", "Searchable public CopperSharp Amiga SDK API reference.", home.ToString(), search: true);
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
File.WriteAllText(Path.Combine(output, "reference-index.json"), JsonSerializer.Serialize(entries, jsonOptions) + "\n", new UTF8Encoding(false));
File.WriteAllText(Path.Combine(output, "reference-paths.json"), JsonSerializer.Serialize(paths, jsonOptions) + "\n", new UTF8Encoding(false));
Console.WriteLine($"Generated {modules.Length} modules, {types.Count} types, {types.Sum(t => t.Members.Count)} members.");

string Url(string route) => H(basePath + route);
static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
static string MemberName(ISymbol symbol) => symbol is IMethodSymbol { MethodKind: MethodKind.Constructor }
    ? symbol.ContainingType.Name : symbol.Name;

string Breadcrumb(string module, string? type = null) => "<nav class=\"reference-breadcrumbs\" aria-label=\"Breadcrumb\">" +
    $"<a href=\"{Url("/reference")}\">SDK reference</a><span>/</span>" +
    $"<a href=\"{Url("/reference/" + Catalog.Slug(module))}\">{H(module)}</a>" +
    (type is null ? "" : $"<span>/</span><span aria-current=\"page\">{H(type)}</span>") + "</nav>";

static string Documentation(System.Xml.Linq.XElement documentation)
{
    var result = new StringBuilder();
    foreach (var name in new[] { "summary", "remarks", "returns", "value" })
    {
        var text = Catalog.Text(documentation.Element(name));
        if (text.Length > 0) result.Append($"<p class=\"reference-description\">{(name is "returns" or "value" ? H(name) + ": " : "")}{H(text)}</p>");
    }
    var parameters = documentation.Elements("param").Concat(documentation.Elements("typeparam")).ToArray();
    if (parameters.Length > 0)
    {
        result.Append("<div class=\"reference-parameters\"><h4>Parameters</h4><dl>");
        foreach (var parameter in parameters)
            result.Append($"<div><dt><code>{H(parameter.Attribute("name")?.Value)}</code></dt><dd>{H(Catalog.Text(parameter))}</dd></div>");
        result.Append("</dl></div>");
    }
    return result.ToString();
}

void Write(string route, string title, string description, string content, bool search = false)
{
    var html = shell.Replace("{{TITLE}}", H(title), StringComparison.Ordinal)
        .Replace("{{DESCRIPTION}}", H(description), StringComparison.Ordinal)
        .Replace("{{CANONICAL}}", H(origin + basePath + route), StringComparison.Ordinal)
        .Replace("{{BASE}}", H(basePath), StringComparison.Ordinal)
        .Replace("{{CONTENT}}", content, StringComparison.Ordinal)
        .Replace("{{SCRIPT}}", search ? $"<script src=\"{Url("/reference-search.js")}\" defer></script>" : "", StringComparison.Ordinal);
    var directory = Path.Combine(output, route.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "index.html"), html, new UTF8Encoding(false));
    paths.Add(route);
}

internal sealed record SearchEntry(string Name, string Kind, string Module, string? Type,
    string Signature, string? Summary, string? Library, string Href);
