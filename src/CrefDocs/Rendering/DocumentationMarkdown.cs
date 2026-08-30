using System.Text;
using System.Xml.Linq;

namespace CrefDocs.Rendering;

internal sealed class DocumentationMarkdown(RouteMap routes)
{
    public string Render(string? fragment, string? currentTypeId = null)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return string.Empty;
        }

        var root = XElement.Parse($"<root>{fragment}</root>", LoadOptions.PreserveWhitespace);
        var builder = new StringBuilder();
        foreach (var node in root.Nodes())
        {
            RenderNode(node, builder, currentTypeId);
        }

        return NormalizeWhitespace(builder.ToString());
    }

    public string RenderPlainText(string? fragment, string? currentTypeId = null)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return string.Empty;
        }

        var root = XElement.Parse($"<root>{fragment}</root>", LoadOptions.PreserveWhitespace);
        var builder = new StringBuilder();
        foreach (var node in root.Nodes())
        {
            RenderPlainTextNode(node, builder, currentTypeId);
        }

        return NormalizeWhitespace(builder.ToString()).Replace('\n', ' ');
    }

    private void RenderNode(XNode node, StringBuilder builder, string? currentTypeId)
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
                RenderSee(element, builder, currentTypeId);
                break;
            case "paramref":
            case "typeparamref":
                builder.Append('`').Append(element.Attribute("name")?.Value).Append('`');
                break;
            case "c":
                builder.Append('`').Append(element.Value.Trim()).Append('`');
                break;
            case "para":
                AppendChildren(element, builder, currentTypeId);
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
                RenderList(element, builder, currentTypeId);
                break;
            default:
                AppendChildren(element, builder, currentTypeId);
                break;
        }
    }

    private void RenderPlainTextNode(XNode node, StringBuilder builder, string? currentTypeId)
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
                var languageKeyword = element.Attribute("langword")?.Value;
                if (!string.IsNullOrWhiteSpace(languageKeyword))
                {
                    builder.Append(languageKeyword);
                    break;
                }

                var href = element.Attribute("href")?.Value;
                if (!string.IsNullOrWhiteSpace(href))
                {
                    builder.Append(string.IsNullOrWhiteSpace(element.Value) ? href : element.Value.Trim());
                    break;
                }

                var documentationId = element.Attribute("cref")?.Value;
                builder.Append(string.IsNullOrWhiteSpace(element.Value)
                    ? GetCrefDisplayName(documentationId, currentTypeId)
                    : element.Value.Trim());
                break;
            case "paramref":
            case "typeparamref":
                builder.Append(element.Attribute("name")?.Value);
                break;
            case "c":
            case "code":
                builder.Append(element.Value.Trim());
                break;
            case "para":
                foreach (var child in element.Nodes())
                {
                    RenderPlainTextNode(child, builder, currentTypeId);
                }

                builder.Append(' ');
                break;
            case "br":
                builder.Append(' ');
                break;
            case "list":
                foreach (var item in element.Elements("item"))
                {
                    RenderPlainTextNode(item.Element("description") ?? item, builder, currentTypeId);
                    builder.Append(' ');
                }

                break;
            default:
                foreach (var child in element.Nodes())
                {
                    RenderPlainTextNode(child, builder, currentTypeId);
                }

                break;
        }
    }

    private void RenderSee(XElement element, StringBuilder builder, string? currentTypeId)
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
        var display = string.IsNullOrWhiteSpace(element.Value)
            ? GetCrefDisplayName(documentationId, currentTypeId)
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

    private void RenderList(XElement list, StringBuilder builder, string? currentTypeId)
    {
        var numbered = string.Equals(list.Attribute("type")?.Value, "number", StringComparison.OrdinalIgnoreCase);
        var index = 1;
        builder.AppendLine();
        foreach (var item in list.Elements("item"))
        {
            builder.Append(numbered ? $"{index++}. " : "- ");
            var content = item.Element("description") ?? item;
            AppendChildren(content, builder, currentTypeId);
            builder.AppendLine();
        }
    }

    private void AppendChildren(XElement element, StringBuilder builder, string? currentTypeId)
    {
        foreach (var child in element.Nodes())
        {
            RenderNode(child, builder, currentTypeId);
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

    private string GetCrefDisplayName(string? documentationId, string? currentTypeId)
    {
        if (string.IsNullOrWhiteSpace(documentationId))
        {
            return "unknown";
        }

        var declaringTypeId = GetDeclaringTypeId(documentationId);
        if (documentationId.StartsWith("T:", StringComparison.Ordinal))
        {
            return routes.TryGetDisplayName(documentationId, out var typeName)
                ? typeName
                : GetSimpleName(documentationId[2..]);
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
        var memberName = separator < 0 ? body : body[(separator + 1)..];
        if (string.Equals(memberName, "#ctor", StringComparison.Ordinal))
        {
            return routes.TryGetDisplayName(declaringTypeId, out var constructorType)
                ? constructorType
                : GetSimpleName(declaringTypeId?[2..] ?? body);
        }

        if (string.Equals(declaringTypeId, currentTypeId, StringComparison.Ordinal))
        {
            return memberName;
        }

        var declaringTypeName = routes.TryGetDisplayName(declaringTypeId, out var internalType)
            ? internalType
            : GetSimpleName(declaringTypeId?[2..] ?? string.Empty);
        return string.IsNullOrEmpty(declaringTypeName)
            ? memberName
            : $"{declaringTypeName}.{memberName}";
    }

    private static string GetSimpleName(string name)
    {
        var separator = Math.Max(name.LastIndexOf('.'), name.LastIndexOf('+'));
        var simple = separator < 0 ? name : name[(separator + 1)..];
        var arity = simple.IndexOf('`');
        return arity < 0 ? simple : simple[..arity];
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
