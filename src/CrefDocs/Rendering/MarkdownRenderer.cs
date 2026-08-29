using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CrefDocs.Snapshot;

namespace CrefDocs.Rendering;

internal sealed class MarkdownRenderer
{
    private static readonly JsonSerializerOptions FrontmatterJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public IReadOnlyList<RenderedFile> Render(ApiSnapshot snapshot, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);

        var routes = RouteMap.Create(snapshot, options);
        var documentation = new DocumentationMarkdown(routes);
        var files = snapshot.Types
            .Select(type => new RenderedFile(
                routes.GetRelativeFilePath(type.Id, options.BaseRoute),
                RenderType(snapshot, type, routes, documentation, options)))
            .ToList();

        files.AddRange(RenderDirectoryIndexes(snapshot, routes, documentation, options));
        return files
            .GroupBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Single())
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static string RenderType(
        ApiSnapshot snapshot,
        ApiType type,
        RouteMap routes,
        DocumentationMarkdown documentation,
        RenderOptions options)
    {
        var builder = new StringBuilder();
        WriteFrontmatter(builder, type.Name, documentation.Render(type.Documentation.Summary));
        builder.Append("# ").AppendLine(type.Name).AppendLine();
        WriteIfPresent(builder, documentation.Render(type.Documentation.Summary));
        builder.Append("- **Type:** ").AppendLine(GetTypeLabel(type.Kind));
        builder.Append("- **Namespace:** ").AppendLine(string.IsNullOrEmpty(type.Namespace) ? "Global" : type.Namespace);
        if (type.BaseType is not null)
        {
            builder.Append("- **Inherits:** ").AppendLine(RenderReference(type.BaseType, routes));
        }

        if (type.Interfaces.Count > 0)
        {
            builder.Append("- **Implements:** ")
                .AppendLine(string.Join(", ", type.Interfaces.Select(reference => RenderReference(reference, routes))));
        }

        builder.AppendLine().AppendLine("```csharp").AppendLine(type.Declaration).AppendLine("```");
        WriteIfPresent(builder, documentation.Render(type.Documentation.Remarks));
        WriteTypeParameters(builder, type.TypeParameters, documentation);

        var groups = type.Members.GroupBy(member => member.Kind).ToDictionary(group => group.Key, group => group.ToArray());
        WriteMembers(builder, "Constructors", groups.GetValueOrDefault(ApiMemberKind.Constructor), routes, documentation);
        WriteMembers(builder, "Properties", Combine(groups, ApiMemberKind.Property, ApiMemberKind.Indexer), routes, documentation);
        WriteMembers(builder, "Methods", groups.GetValueOrDefault(ApiMemberKind.Method), routes, documentation);
        WriteMembers(builder, "Events", groups.GetValueOrDefault(ApiMemberKind.Event), routes, documentation);
        WriteMembers(builder, "Fields", groups.GetValueOrDefault(ApiMemberKind.Field), routes, documentation);
        WriteMembers(builder, "Operators", groups.GetValueOrDefault(ApiMemberKind.Operator), routes, documentation);
        WriteEnumValues(builder, groups.GetValueOrDefault(ApiMemberKind.EnumValue), documentation);

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static IEnumerable<RenderedFile> RenderDirectoryIndexes(
        ApiSnapshot snapshot,
        RouteMap routes,
        DocumentationMarkdown documentation,
        RenderOptions options)
    {
        var directories = snapshot.Types
            .Select(type => RouteMap.GetDirectory(type, options.Structure))
            .SelectMany(GetDirectoryAndParents)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(directory => directory.Count(character => character == '/'))
            .ThenBy(directory => directory, StringComparer.Ordinal)
            .ToArray();

        foreach (var directory in directories)
        {
            if (string.IsNullOrEmpty(directory) && !options.GenerateRootIndex)
            {
                continue;
            }

            var directTypes = snapshot.Types
                .Where(type => string.Equals(
                    RouteMap.GetDirectory(type, options.Structure),
                    directory,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToArray();
            var children = directories
                .Where(candidate => IsDirectChild(directory, candidate))
                .OrderBy(candidate => candidate, StringComparer.Ordinal)
                .ToArray();
            var title = string.IsNullOrEmpty(directory)
                ? snapshot.Package.Id
                : HumanizeSegment(directory.Split('/')[^1]);
            var builder = new StringBuilder();
            WriteFrontmatter(builder, title, $"API reference for {title}.");
            builder.Append("# ").AppendLine(title).AppendLine();
            if (string.IsNullOrEmpty(directory))
            {
                builder.Append("API reference for ").Append(snapshot.Package.Id).Append(' ')
                    .Append(snapshot.Package.Version).AppendLine(".").AppendLine();
            }

            if (children.Length > 0)
            {
                builder.AppendLine("## Sections").AppendLine();
                builder.AppendLine("| Section |")
                    .AppendLine("| ------- |");
                foreach (var child in children)
                {
                    var route = CombineRoute(options.BaseRoute, child);
                    builder.Append("| [").Append(HumanizeSegment(child.Split('/')[^1])).Append("](")
                        .Append(route).AppendLine(") |");
                }

                builder.AppendLine();
            }

            foreach (var group in directTypes.GroupBy(type => GetTypeSection(type.Kind)))
            {
                builder.Append("## ").AppendLine(group.Key).AppendLine();
                builder.Append("| ").Append(group.Key.TrimEnd('s')).AppendLine(" | Summary |")
                    .AppendLine("| ---- | ------- |");
                foreach (var type in group)
                {
                    builder.Append("| [").Append(type.Name).Append("](").Append(routes[type.Id]).Append(") | ")
                        .Append(EscapeTable(documentation.Render(type.Documentation.Summary))).AppendLine(" |");
                }

                builder.AppendLine();
            }

            yield return new RenderedFile(
                string.IsNullOrEmpty(directory) ? "index.md" : $"{directory}/index.md",
                builder.ToString().TrimEnd() + Environment.NewLine);
        }
    }

    private static void WriteMembers(
        StringBuilder builder,
        string heading,
        IReadOnlyList<ApiMember>? members,
        RouteMap routes,
        DocumentationMarkdown documentation)
    {
        if (members is not { Count: > 0 })
        {
            return;
        }

        builder.AppendLine().Append("## ").AppendLine(heading);
        var anchors = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var baseAnchor = Slug.Create(member.Name);
            var occurrence = anchors.GetValueOrDefault(baseAnchor) + 1;
            anchors[baseAnchor] = occurrence;
            var anchor = occurrence == 1 ? baseAnchor : $"{baseAnchor}-{occurrence}";

            builder.AppendLine().Append("<a id=\"").Append(anchor).AppendLine("\"></a>");
            builder.Append("### ").AppendLine(member.Name).AppendLine();
            WriteIfPresent(builder, documentation.Render(member.Documentation.Summary));
            if (member.Type is not null)
            {
                builder.Append("- **Type:** ").AppendLine(RenderReference(member.Type, routes));
            }

            builder.AppendLine().AppendLine("```csharp").AppendLine(member.Declaration).AppendLine("```");
            WriteParameters(builder, member.Parameters, routes, documentation);
            WriteTypeParameters(builder, member.TypeParameters, documentation);
            WriteIfPresent(builder, documentation.Render(member.Documentation.Returns), "**Returns:** ");
            WriteIfPresent(builder, documentation.Render(member.Documentation.Remarks));
            WriteExceptions(builder, member.Exceptions, routes, documentation);
        }
    }

    private static void WriteParameters(
        StringBuilder builder,
        IReadOnlyList<ApiParameter> parameters,
        RouteMap routes,
        DocumentationMarkdown documentation)
    {
        if (parameters.Count == 0)
        {
            return;
        }

        builder.AppendLine().AppendLine("| Parameter | Summary |")
            .AppendLine("| --------- | ------- |");
        foreach (var parameter in parameters)
        {
            var optional = parameter.DefaultValue is null ? string.Empty : "*(optional)* ";
            builder.Append("| ").Append(optional).Append(RenderReference(parameter.Type, routes)).Append(' ')
                .Append('`').Append(parameter.Name).Append("` | ")
                .Append(EscapeTable(documentation.Render(parameter.Description))).AppendLine(" |");
        }
    }

    private static void WriteTypeParameters(
        StringBuilder builder,
        IReadOnlyList<ApiTypeParameter> parameters,
        DocumentationMarkdown documentation)
    {
        if (parameters.Count == 0)
        {
            return;
        }

        builder.AppendLine().AppendLine("| Type parameter | Constraints | Summary |")
            .AppendLine("| -------------- | ----------- | ------- |");
        foreach (var parameter in parameters)
        {
            builder.Append("| `").Append(parameter.Name).Append("` | ")
                .Append(parameter.Constraints is null ? string.Empty : $"`{parameter.Constraints}`")
                .Append(" | ").Append(EscapeTable(documentation.Render(parameter.Description))).AppendLine(" |");
        }
    }

    private static void WriteExceptions(
        StringBuilder builder,
        IReadOnlyList<ApiException> exceptions,
        RouteMap routes,
        DocumentationMarkdown documentation)
    {
        if (exceptions.Count == 0)
        {
            return;
        }

        builder.AppendLine().AppendLine("**Exceptions**").AppendLine();
        foreach (var exception in exceptions)
        {
            builder.Append("- ").Append(RenderReference(exception.Type, routes));
            var description = documentation.Render(exception.Description);
            if (!string.IsNullOrEmpty(description))
            {
                builder.Append(" — ").Append(description);
            }

            builder.AppendLine();
        }
    }

    private static void WriteEnumValues(
        StringBuilder builder,
        IReadOnlyList<ApiMember>? values,
        DocumentationMarkdown documentation)
    {
        if (values is not { Count: > 0 })
        {
            return;
        }

        builder.AppendLine().AppendLine("## Values").AppendLine();
        builder.AppendLine("| Name | Summary |")
            .AppendLine("| ---- | ------- |");
        foreach (var value in values)
        {
            builder.Append("| `").Append(value.Name).Append("` | ")
                .Append(EscapeTable(documentation.Render(value.Documentation.Summary))).AppendLine(" |");
        }
    }

    private static string RenderReference(ApiReference reference, RouteMap routes)
    {
        var display = $"`{reference.DisplayName}`";
        if (routes.TryGetRoute(reference.DocumentationId, out var internalRoute))
        {
            return $"[{display}]({internalRoute})";
        }

        if (reference.DocumentationId?.StartsWith("T:", StringComparison.Ordinal) == true)
        {
            var externalName = reference.DocumentationId[2..]
                .ToLowerInvariant()
                .Replace('`', '-')
                .Replace('+', '.');
            return $"[{display}](https://learn.microsoft.com/dotnet/api/{externalName})";
        }

        return display;
    }

    private static IReadOnlyList<ApiMember> Combine(
        IReadOnlyDictionary<ApiMemberKind, ApiMember[]> groups,
        params ApiMemberKind[] kinds)
    {
        return kinds.SelectMany(kind => groups.GetValueOrDefault(kind) ?? []).ToArray();
    }

    private static IEnumerable<string> GetDirectoryAndParents(string directory)
    {
        yield return directory;
        while (!string.IsNullOrEmpty(directory))
        {
            var separator = directory.LastIndexOf('/');
            directory = separator < 0 ? string.Empty : directory[..separator];
            yield return directory;
        }
    }

    private static bool IsDirectChild(string parent, string candidate)
    {
        if (string.IsNullOrEmpty(candidate) || string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidateParent = candidate.Contains('/') ? candidate[..candidate.LastIndexOf('/')] : string.Empty;
        return string.Equals(parent, candidateParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string CombineRoute(string baseRoute, string relative)
    {
        return $"/{baseRoute.Trim('/')}/{relative}".TrimEnd('/');
    }

    private static string HumanizeSegment(string segment)
    {
        return string.Join(' ', segment.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select((word, index) => index == 0
                ? char.ToUpperInvariant(word[0]) + word[1..]
                : word));
    }

    private static string GetTypeLabel(ApiTypeKind kind) => kind switch
    {
        ApiTypeKind.RecordClass => "Record class",
        ApiTypeKind.RecordStruct => "Record struct",
        _ => kind.ToString(),
    };

    private static string GetTypeSection(ApiTypeKind kind) => kind switch
    {
        ApiTypeKind.Class or ApiTypeKind.RecordClass => "Classes",
        ApiTypeKind.Struct or ApiTypeKind.RecordStruct => "Structs",
        ApiTypeKind.Interface => "Interfaces",
        ApiTypeKind.Enum => "Enums",
        ApiTypeKind.Delegate => "Delegates",
        _ => "Types",
    };

    private static void WriteFrontmatter(StringBuilder builder, string title, string description)
    {
        builder.AppendLine("---")
            .Append("title: ").AppendLine(JsonSerializer.Serialize(title, FrontmatterJsonOptions))
            .Append("description: ").AppendLine(JsonSerializer.Serialize(description, FrontmatterJsonOptions))
            .AppendLine("---")
            .AppendLine();
    }

    private static void WriteIfPresent(StringBuilder builder, string? value, string? prefix = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.AppendLine();
        if (prefix is not null)
        {
            builder.Append(prefix);
        }

        builder.AppendLine(value).AppendLine();
    }

    private static string EscapeTable(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
    }
}
