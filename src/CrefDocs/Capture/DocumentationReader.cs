using System.Xml.Linq;
using CrefDocs.Snapshot;
using Microsoft.CodeAnalysis;

namespace CrefDocs.Capture;

internal sealed class DocumentationReader
{
    public DocumentationResult Read(ISymbol symbol, CancellationToken cancellationToken)
    {
        var own = Parse(symbol.GetDocumentationCommentXml(
            expandIncludes: true,
            cancellationToken: cancellationToken));

        if (!own.HasInheritDoc)
        {
            return own;
        }

        var inheritedSymbol = FindInheritedSymbol(symbol);
        if (inheritedSymbol is null)
        {
            return own;
        }

        var inherited = Read(inheritedSymbol, cancellationToken);
        return own.FillMissingFrom(inherited);
    }

    private static DocumentationResult Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return DocumentationResult.Empty;
        }

        var root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        var parameters = root.Elements("param")
            .Where(element => element.Attribute("name") is not null)
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                ReadContent,
                StringComparer.Ordinal);
        var typeParameters = root.Elements("typeparam")
            .Where(element => element.Attribute("name") is not null)
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                ReadContent,
                StringComparer.Ordinal);
        var exceptions = root.Elements("exception")
            .Select(element => new DocumentationException(
                element.Attribute("cref")?.Value,
                ReadContent(element)))
            .ToArray();

        return new DocumentationResult(
            new ApiDocumentation(
                ReadElement(root, "summary"),
                ReadElement(root, "remarks"),
                ReadElement(root, "returns") ?? ReadElement(root, "value"),
                ReadElement(root, "example")),
            parameters,
            typeParameters,
            exceptions,
            root.Descendants("inheritdoc").Any());
    }

    private static ISymbol? FindInheritedSymbol(ISymbol symbol)
    {
        ISymbol? overridden = symbol switch
        {
            IMethodSymbol method => method.OverriddenMethod,
            IPropertySymbol property => property.OverriddenProperty,
            IEventSymbol @event => @event.OverriddenEvent,
            INamedTypeSymbol type => type.BaseType,
            _ => null,
        };

        if (overridden is not null)
        {
            return overridden;
        }

        if (symbol.ContainingType is null)
        {
            return null;
        }

        foreach (var @interface in symbol.ContainingType.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers())
            {
                if (SymbolEqualityComparer.Default.Equals(
                    symbol.ContainingType.FindImplementationForInterfaceMember(member),
                    symbol))
                {
                    return member;
                }
            }
        }

        return null;
    }

    private static string? ReadElement(XElement root, string name)
    {
        var element = root.Element(name);
        return element is null ? null : ReadContent(element);
    }

    private static string? ReadContent(XElement element)
    {
        var content = string.Concat(element.Nodes().Select(
            node => node.ToString(SaveOptions.DisableFormatting))).Trim();
        return string.IsNullOrEmpty(content) ? null : content;
    }
}

internal sealed record DocumentationResult(
    ApiDocumentation Documentation,
    IReadOnlyDictionary<string, string?> Parameters,
    IReadOnlyDictionary<string, string?> TypeParameters,
    IReadOnlyList<DocumentationException> Exceptions,
    bool HasInheritDoc)
{
    public static DocumentationResult Empty { get; } = new(
        ApiDocumentation.Empty,
        new Dictionary<string, string?>(),
        new Dictionary<string, string?>(),
        [],
        false);

    public DocumentationResult FillMissingFrom(DocumentationResult inherited)
    {
        return new DocumentationResult(
            new ApiDocumentation(
                Documentation.Summary ?? inherited.Documentation.Summary,
                Documentation.Remarks ?? inherited.Documentation.Remarks,
                Documentation.Returns ?? inherited.Documentation.Returns,
                Documentation.Example ?? inherited.Documentation.Example),
            Merge(inherited.Parameters, Parameters),
            Merge(inherited.TypeParameters, TypeParameters),
            Exceptions.Count == 0 ? inherited.Exceptions : Exceptions,
            false);
    }

    private static IReadOnlyDictionary<string, string?> Merge(
        IReadOnlyDictionary<string, string?> inherited,
        IReadOnlyDictionary<string, string?> own)
    {
        var result = new Dictionary<string, string?>(inherited, StringComparer.Ordinal);
        foreach (var entry in own)
        {
            result[entry.Key] = entry.Value;
        }

        return result;
    }
}

internal sealed record DocumentationException(string? DocumentationId, string? Description);
