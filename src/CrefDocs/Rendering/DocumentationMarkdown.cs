using System.Text;
using System.Xml.Linq;

namespace CrefDocs.Rendering;

internal sealed class DocumentationMarkdown(RouteMap routes)
{
    public string Render(string? fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return string.Empty;
        }

        var root = XElement.Parse($"<root>{fragment}</root>", LoadOptions.PreserveWhitespace);
        var builder = new StringBuilder();
        foreach (var node in root.Nodes())
        {
            RenderNode(node, builder);
        }

        return NormalizeWhitespace(builder.ToString());
    }

    private void RenderNode(XNode node, StringBuilder builder)
    {
        if (node is XText text)
        {
            builder.Append(text.Value);
            return;
        }

        if (node is not XElement element)
        {
            return;
        }

        switch (element.Name.LocalName)
        {
            case "see":
                RenderSee(element, builder);
                break;
            case "paramref":
            case "typeparamref":
                builder.Append('`').Append(element.Attribute("name")?.Value).Append('`');
                break;
            case "c":
                builder.Append('`').Append(element.Value.Trim()).Append('`');
                break;
            case "para":
                AppendChildren(element, builder);
                builder.AppendLine().AppendLine();
                break;
            case "br":
                builder.AppendLine();
                break;
            case "code":
                builder.AppendLine().AppendLine("```csharp");
                builder.AppendLine(element.Value.Trim());
                builder.AppendLine("```").AppendLine();
                break;
            case "list":
                RenderList(element, builder);
                break;
            default:
                AppendChildren(element, builder);
                break;
        }
    }

    private void RenderSee(XElement element, StringBuilder builder)
    {
        var languageKeyword = element.Attribute("langword")?.Value;
        if (!string.IsNullOrWhiteSpace(languageKeyword))
        {
            builder.Append('`').Append(languageKeyword).Append('`');
            return;
        }

        var href = element.Attribute("href")?.Value;
        if (!string.IsNullOrWhiteSpace(href))
        {
            var label = string.IsNullOrWhiteSpace(element.Value) ? href : element.Value.Trim();
            builder.Append('[').Append(label).Append("](").Append(href).Append(')');
            return;
        }

        var documentationId = element.Attribute("cref")?.Value;
        var declaringTypeId = GetDeclaringTypeId(documentationId);
        var display = string.IsNullOrWhiteSpace(element.Value)
            ? routes.TryGetDisplayName(declaringTypeId, out var internalName)
                ? internalName
                : GetCrefDisplayName(documentationId)
            : element.Value.Trim();
        var route = GetReferenceRoute(documentationId);
        if (route is null)
        {
            builder.Append('`').Append(display).Append('`');
        }
        else
        {
            builder.Append("[`").Append(display).Append("`](").Append(route).Append(')');
        }
    }

    private void RenderList(XElement list, StringBuilder builder)
    {
        var numbered = string.Equals(list.Attribute("type")?.Value, "number", StringComparison.OrdinalIgnoreCase);
        var index = 1;
        builder.AppendLine();
        foreach (var item in list.Elements("item"))
        {
            builder.Append(numbered ? $"{index++}. " : "- ");
            var content = item.Element("description") ?? item;
            AppendChildren(content, builder);
            builder.AppendLine();
        }
    }

    private void AppendChildren(XElement element, StringBuilder builder)
    {
        foreach (var child in element.Nodes())
        {
            RenderNode(child, builder);
        }
    }

    private string? GetReferenceRoute(string? documentationId)
    {
        if (routes.TryGetRoute(GetDeclaringTypeId(documentationId), out var internalRoute))
        {
            return internalRoute;
        }

        var typeName = GetExternalTypeName(documentationId);
        return typeName is null
            ? null
            : $"https://learn.microsoft.com/dotnet/api/{typeName.ToLowerInvariant().Replace('`', '-').Replace('+', '.')}";
    }

    private static string? GetDeclaringTypeId(string? documentationId)
    {
        if (string.IsNullOrWhiteSpace(documentationId))
        {
            return null;
        }

        if (documentationId.StartsWith("T:", StringComparison.Ordinal))
        {
            return documentationId;
        }

        if (documentationId.Length < 3 || documentationId[1] != ':')
        {
            return null;
        }

        var body = documentationId[2..];
        var parameters = body.IndexOf('(');
        if (parameters >= 0)
        {
            body = body[..parameters];
        }

        var memberSeparator = body.LastIndexOf('.');
        return memberSeparator < 0 ? null : $"T:{body[..memberSeparator]}";
    }

    private static string? GetExternalTypeName(string? documentationId)
    {
        var declaringType = GetDeclaringTypeId(documentationId);
        return declaringType is null ? null : declaringType[2..];
    }

    private static string GetCrefDisplayName(string? documentationId)
    {
        if (string.IsNullOrWhiteSpace(documentationId))
        {
            return "unknown";
        }

        var body = documentationId.Length > 2 && documentationId[1] == ':'
            ? documentationId[2..]
            : documentationId;
        var parameters = body.IndexOf('(');
        if (parameters >= 0)
        {
            body = body[..parameters];
        }

        var separator = body.LastIndexOf('.');
        return (separator < 0 ? body : body[(separator + 1)..]).Replace("#ctor", "constructor", StringComparison.Ordinal);
    }

    private static string NormalizeWhitespace(string value)
    {
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var normalized = lines
            .Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .ToArray();
        return string.Join('\n', normalized).Trim();
    }
}
