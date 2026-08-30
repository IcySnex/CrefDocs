using System.Net;
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
        var description = documentation.Render(type.Documentation.Summary, type.Id);
        WriteFrontmatter(
            builder,
            type.Name,
            documentation.RenderPlainText(type.Documentation.Summary, type.Id),
            description,
            options.PageHeader);
        WritePageHeader(builder, EscapeMarkdownText(type.Name), description, options.PageHeader);
        WriteRemarks(builder, documentation.Render(type.Documentation.Remarks, type.Id));
        builder.Append("- **Kind:** ").AppendLine(GetTypeLabel(type.Kind));
        var namespaceName = string.IsNullOrEmpty(type.Namespace) ? "Global" : type.Namespace;
        builder.Append("- **Namespace:** [").Append(namespaceName).Append("](")
            .Append(RouteMap.GetNamespaceRoute(type.Namespace, options)).AppendLine(")");
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
        WriteTypeParameters(builder, type.TypeParameters, routes, documentation, type.Id);

        var groups = type.Members.GroupBy(member => member.Kind).ToDictionary(group => group.Key, group => group.ToArray());
        WriteConstructors(builder, type, groups.GetValueOrDefault(ApiMemberKind.Constructor), routes, documentation);
        WriteMembers(builder, "Properties", Combine(groups, ApiMemberKind.Property, ApiMemberKind.Indexer), routes, documentation, type.Id);
        WriteMembers(builder, "Methods", groups.GetValueOrDefault(ApiMemberKind.Method), routes, documentation, type.Id);
        WriteMembers(builder, "Events", groups.GetValueOrDefault(ApiMemberKind.Event), routes, documentation, type.Id);
        WriteMembers(builder, "Fields", groups.GetValueOrDefault(ApiMemberKind.Field), routes, documentation, type.Id);
        WriteMembers(builder, "Operators", groups.GetValueOrDefault(ApiMemberKind.Operator), routes, documentation, type.Id);
        WriteEnumValues(builder, groups.GetValueOrDefault(ApiMemberKind.EnumValue), documentation, type.Id);

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
            var indexKey = GetIndexKey(snapshot, directory, options.Structure);
            var title = options.Structure == StructureMode.Namespace && indexKey is not null
                ? indexKey
                : string.IsNullOrEmpty(directory)
                    ? snapshot.Package.Id
                    : GetDirectoryDisplayName(snapshot, directory, options.Structure);
            var description = GetIndexDescription(snapshot, indexKey, options.Structure);
            var frontmatterDescription = description ?? $"API reference for {title}.";
            var markdownDescription = description ?? (string.IsNullOrEmpty(directory)
                ? $"API reference for {snapshot.Package.Id} {snapshot.Package.Version}."
                : null);
            var builder = new StringBuilder();
            WriteFrontmatter(builder, title, frontmatterDescription, markdownDescription, options.PageHeader);
            WritePageHeader(builder, title, markdownDescription, options.PageHeader);

            WriteIndexIdentity(builder, snapshot, directory, indexKey, options);

            if (children.Length > 0)
            {
                var childKind = options.Structure == StructureMode.Namespace ? "Namespace" : "Section";
                EnsureBlankLine(builder);
                builder.Append("## ").Append(childKind).AppendLine("s").AppendLine();
                builder.Append("| ").Append(childKind).AppendLine(" | Summary |")
                    .AppendLine("| --------- | ------- |");
                foreach (var child in children)
                {
                    var route = CombineRoute(options.BaseRoute, child);
                    var childKey = GetIndexKey(snapshot, child, options.Structure);
                    var childTitle = options.Structure == StructureMode.Namespace && childKey is not null
                        ? childKey
                        : GetDirectoryDisplayName(snapshot, child, options.Structure);
                    var childDescription = GetIndexDescription(snapshot, childKey, options.Structure) ?? string.Empty;
                    builder.Append("| [").Append(childTitle).Append("](")
                        .Append(route).Append(") | ")
                        .Append(EscapeTable(childDescription)).AppendLine(" |");
                }

                builder.AppendLine();
            }

            foreach (var group in directTypes.GroupBy(type => GetTypeSection(type.Kind)))
            {
                EnsureBlankLine(builder);
                builder.Append("## ").AppendLine(group.Key).AppendLine();
                builder.Append("| ").Append(GetTypeColumnLabel(group.Key)).AppendLine(" | Summary |")
                    .AppendLine("| ---- | ------- |");
                foreach (var type in group)
                {
                    builder.Append("| [").Append(EscapeMarkdownText(type.Name)).Append("](").Append(routes[type.Id]).Append(") | ")
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
        DocumentationMarkdown documentation,
        string currentTypeId)
    {
        if (members is not { Count: > 0 })
        {
            return;
        }

        EnsureBlankLine(builder);
        builder.Append("## ").AppendLine(heading);
        var anchors = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var baseAnchor = Slug.Create(member.Name);
            var occurrence = anchors.GetValueOrDefault(baseAnchor) + 1;
            anchors[baseAnchor] = occurrence;
            var anchor = occurrence == 1 ? baseAnchor : $"{baseAnchor}-{occurrence}";

            EnsureBlankLine(builder);
            builder.Append("<a id=\"").Append(anchor).AppendLine("\"></a>");
            builder.Append("### ").AppendLine(EscapeMarkdownText(member.Name)).AppendLine();
            WriteIfPresent(builder, documentation.Render(member.Documentation.Summary, currentTypeId));
            WriteRemarks(builder, documentation.Render(member.Documentation.Remarks, currentTypeId));
            if (member.Type is not null && member.Kind is not ApiMemberKind.Method and not ApiMemberKind.Operator)
            {
                EnsureBlankLine(builder);
                builder.Append("**Type:** ").AppendLine(RenderReference(member.Type, routes));
            }

            EnsureBlankLine(builder);
            builder.AppendLine("```csharp").AppendLine(FormatMemberDeclaration(member.Declaration)).AppendLine("```");
            WriteParameters(builder, member.Parameters, routes, documentation, currentTypeId);
            WriteTypeParameters(builder, member.TypeParameters, routes, documentation, currentTypeId);
            if (member.Kind is ApiMemberKind.Method or ApiMemberKind.Operator)
            {
                WriteReturns(builder, member, routes, documentation, currentTypeId);
            }

            WriteExceptions(builder, member.Exceptions, routes, documentation, currentTypeId);
        }
    }

    private static void WriteConstructors(
        StringBuilder builder,
        ApiType type,
        IReadOnlyList<ApiMember>? constructors,
        RouteMap routes,
        DocumentationMarkdown documentation)
    {
        if (constructors is not { Count: > 0 })
        {
            return;
        }

        EnsureBlankLine(builder);
        builder.AppendLine("## Constructors");
        var anchor = Slug.Create(type.Name);
        for (var index = 0; index < constructors.Count; index++)
        {
            var constructor = constructors[index];
            EnsureBlankLine(builder);
            builder.Append("<a id=\"").Append(anchor);
            if (index > 0)
            {
                builder.Append('-').Append(index + 1);
            }

            builder.AppendLine("\"></a>");
            if (constructor.IsPrimaryConstructor)
            {
                var reference = RenderReference(new ApiReference(type.Name, type.Id, []), routes);
                WriteIfPresent(builder, $"Initializes a new instance of the {reference} {GetTypeNoun(type.Kind)}.");
            }
            else
            {
                WriteIfPresent(builder, documentation.Render(constructor.Documentation.Summary, type.Id));
                WriteRemarks(builder, documentation.Render(constructor.Documentation.Remarks, type.Id));
            }

            EnsureBlankLine(builder);
            builder.AppendLine("```csharp")
                .AppendLine(FormatMemberDeclaration(constructor.Declaration))
                .AppendLine("```");
            WriteParameters(builder, constructor.Parameters, routes, documentation, type.Id);
            WriteTypeParameters(builder, constructor.TypeParameters, routes, documentation, type.Id);
            WriteExceptions(builder, constructor.Exceptions, routes, documentation, type.Id);
        }
    }

    private static void WriteParameters(
        StringBuilder builder,
        IReadOnlyList<ApiParameter> parameters,
        RouteMap routes,
        DocumentationMarkdown documentation,
        string currentTypeId)
    {
        if (parameters.Count == 0)
        {
            return;
        }

        EnsureBlankLine(builder);
        builder.AppendLine("| Parameter | Summary |")
            .AppendLine("| --------- | ------- |");
        foreach (var parameter in parameters)
        {
            var optional = parameter.DefaultValue is null ? string.Empty : "*(optional)* ";
            builder.Append("| ").Append(optional).Append(RenderReference(parameter.Type, routes)).Append(' ')
                .Append('`').Append(parameter.Name).Append("` | ")
                .Append(EscapeTable(documentation.Render(parameter.Description, currentTypeId))).AppendLine(" |");
        }
    }

    private static void WriteTypeParameters(
        StringBuilder builder,
        IReadOnlyList<ApiTypeParameter> parameters,
        RouteMap routes,
        DocumentationMarkdown documentation,
        string currentTypeId)
    {
        if (parameters.Count == 0)
        {
            return;
        }

        EnsureBlankLine(builder);
        builder.AppendLine("| Type parameter | Summary |")
            .AppendLine("| -------------- | ------- |");
        foreach (var parameter in parameters)
        {
            builder.Append("| ");
            if (parameter.TypeConstraints.Count > 0)
            {
                builder.Append(string.Join(", ", parameter.TypeConstraints.Select(constraint =>
                    RenderReference(constraint, routes)))).Append(' ');
            }
            else if (!string.IsNullOrWhiteSpace(parameter.KeywordConstraints))
            {
                builder.Append("*(").Append(parameter.KeywordConstraints).Append(")* ");
            }

            builder.Append('`').Append(parameter.Name).Append("` | ")
                .Append(EscapeTable(documentation.Render(parameter.Description, currentTypeId))).AppendLine(" |");
        }
    }

    private static void WriteExceptions(
        StringBuilder builder,
        IReadOnlyList<ApiException> exceptions,
        RouteMap routes,
        DocumentationMarkdown documentation,
        string currentTypeId)
    {
        if (exceptions.Count == 0)
        {
            return;
        }

        EnsureBlankLine(builder);
        builder.AppendLine("**Exceptions**").AppendLine();
        foreach (var exception in exceptions)
        {
            builder.Append("- ").Append(RenderReference(exception.Type, routes));
            var description = documentation.Render(exception.Description, currentTypeId);
            if (!string.IsNullOrEmpty(description))
            {
                builder.Append(": ").Append(description);
            }

            builder.AppendLine();
        }
    }

    private static void WriteReturns(
        StringBuilder builder,
        ApiMember member,
        RouteMap routes,
        DocumentationMarkdown documentation,
        string currentTypeId)
    {
        var description = documentation.Render(member.Documentation.Returns, currentTypeId);
        var hasReturnType = member.Type is not null &&
            !string.Equals(member.Type.DocumentationId, "T:System.Void", StringComparison.Ordinal);
        if (!hasReturnType)
        {
            return;
        }

        EnsureBlankLine(builder);
        builder.AppendLine("**Returns**").AppendLine();
        builder.Append("- ").Append(RenderReference(member.Type!, routes));

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.Append(": ").Append(description);
        }

        builder.AppendLine();
    }

    private static void WriteEnumValues(
        StringBuilder builder,
        IReadOnlyList<ApiMember>? values,
        DocumentationMarkdown documentation,
        string currentTypeId)
    {
        if (values is not { Count: > 0 })
        {
            return;
        }

        EnsureBlankLine(builder);
        builder.AppendLine("## Values").AppendLine();
        builder.AppendLine("| Name | Summary |")
            .AppendLine("| ---- | ------- |");
        foreach (var value in values)
        {
            builder.Append("| `").Append(value.Name).Append("` | ")
                .Append(EscapeTable(documentation.Render(value.Documentation.Summary, currentTypeId))).AppendLine(" |");
        }
    }

    private static string RenderReference(ApiReference reference, RouteMap routes)
    {
        var builder = new StringBuilder("<code>");
        if (reference.Components.Count == 0)
        {
            builder.Append(RenderReferenceComponent(reference.DisplayName, reference.DocumentationId, routes));
            return builder.Append("</code>").ToString();
        }

        var offset = 0;
        foreach (var component in reference.Components.OrderBy(component => component.Start))
        {
            if (component.Start < offset || component.Start + component.Length > reference.DisplayName.Length)
            {
                continue;
            }

            builder.Append(WebUtility.HtmlEncode(reference.DisplayName[offset..component.Start]));
            builder.Append(RenderReferenceComponent(
                reference.DisplayName.Substring(component.Start, component.Length),
                component.DocumentationId,
                routes));
            offset = component.Start + component.Length;
        }

        builder.Append(WebUtility.HtmlEncode(reference.DisplayName[offset..]));
        return builder.Append("</code>").ToString();
    }

    private static string RenderReferenceComponent(string text, string? documentationId, RouteMap routes)
    {
        if (routes.TryGetRoute(documentationId, out var internalRoute))
        {
            return $"<a href=\"{WebUtility.HtmlEncode(internalRoute)}\">{WebUtility.HtmlEncode(text)}</a>";
        }

        if (documentationId?.StartsWith("T:", StringComparison.Ordinal) == true)
        {
            var externalName = documentationId[2..]
                .ToLowerInvariant()
                .Replace('`', '-')
                .Replace('+', '.');
            var route = $"https://learn.microsoft.com/dotnet/api/{externalName}";
            return $"<a href=\"{WebUtility.HtmlEncode(route)}\">{WebUtility.HtmlEncode(text)}</a>";
        }

        return WebUtility.HtmlEncode(text);
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

    private static string GetDirectoryDisplayName(
        ApiSnapshot snapshot,
        string directory,
        StructureMode structure)
    {
        var depth = directory.Count(character => character == '/');
        var type = FindRepresentativeType(snapshot, directory, structure);
        if (type is null)
        {
            return HumanizeSegment(directory.Split('/')[^1]);
        }

        var segments = structure switch
        {
            StructureMode.Namespace => type.Namespace.Split('.', StringSplitOptions.RemoveEmptyEntries),
            StructureMode.Source => (Path.GetDirectoryName(type.SourcePath?.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty)
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries),
            _ => [],
        };
        return depth < segments.Length
            ? segments[depth]
            : HumanizeSegment(directory.Split('/')[^1]);
    }

    private static string? GetIndexKey(
        ApiSnapshot snapshot,
        string directory,
        StructureMode structure)
    {
        if (structure == StructureMode.Flat)
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(directory))
        {
            return structure == StructureMode.Source ? string.Empty : null;
        }

        var type = FindRepresentativeType(snapshot, directory, structure);
        if (type is null)
        {
            return null;
        }

        var count = directory.Count(character => character == '/') + 1;
        var segments = structure switch
        {
            StructureMode.Namespace => type.Namespace.Split('.', StringSplitOptions.RemoveEmptyEntries),
            StructureMode.Source => (Path.GetDirectoryName(type.SourcePath?.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty)
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries),
            _ => [],
        };
        var separator = structure == StructureMode.Namespace ? "." : "/";
        return string.Join(separator, segments.Take(count));
    }

    private static string? GetIndexDescription(
        ApiSnapshot snapshot,
        string? key,
        StructureMode structure)
    {
        if (key is null)
        {
            return null;
        }

        var descriptions = structure == StructureMode.Namespace
            ? snapshot.IndexMetadata.Namespaces
            : snapshot.IndexMetadata.Sections;
        return descriptions.FirstOrDefault(entry =>
            string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))?.Description;
    }

    private static void WriteIndexIdentity(
        StringBuilder builder,
        ApiSnapshot snapshot,
        string directory,
        string? indexKey,
        RenderOptions options)
    {
        if (string.IsNullOrEmpty(directory))
        {
            EnsureBlankLine(builder);
            builder.AppendLine("- **Kind:** Package");
            WritePackageMetadata(builder, snapshot.Package);
            return;
        }

        if (options.Structure == StructureMode.Namespace && indexKey is not null)
        {
            EnsureBlankLine(builder);
            builder.AppendLine("- **Kind:** Namespace");
            var parent = GetParentDirectory(directory);
            if (!string.IsNullOrEmpty(parent))
            {
                var parentKey = GetIndexKey(snapshot, parent, options.Structure)!;
                builder.Append("- **Namespace:** [").Append(parentKey).Append("](")
                    .Append(CombineRoute(options.BaseRoute, parent)).AppendLine(")");
            }

            return;
        }

        if (options.Structure == StructureMode.Source)
        {
            EnsureBlankLine(builder);
            builder.AppendLine("- **Kind:** Section");
            var parent = GetParentDirectory(directory);
            var parentTitle = string.IsNullOrEmpty(parent)
                ? snapshot.Package.Id
                : GetDirectoryDisplayName(snapshot, parent, options.Structure);
            builder.Append("- **Section:** [")
                .Append(parentTitle).Append("](")
                .Append(CombineRoute(options.BaseRoute, parent)).AppendLine(")");
        }
    }

    private static void WritePackageMetadata(
        StringBuilder builder,
        ApiPackage package)
    {
        builder.Append("- **Version:** ").AppendLine(package.Version);
        builder.Append("- **Target Framework:** ").AppendLine(package.TargetFramework);
    }

    private static ApiType? FindRepresentativeType(
        ApiSnapshot snapshot,
        string directory,
        StructureMode structure)
    {
        return snapshot.Types
            .Where(candidate => RouteMap.GetDirectory(candidate, structure) == directory ||
                RouteMap.GetDirectory(candidate, structure).StartsWith(directory + '/', StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.SourcePath, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string GetParentDirectory(string directory)
    {
        var separator = directory.LastIndexOf('/');
        return separator < 0 ? string.Empty : directory[..separator];
    }

    private static string GetTypeLabel(ApiTypeKind kind) => kind switch
    {
        ApiTypeKind.RecordClass => "Record class",
        ApiTypeKind.RecordStruct => "Record struct",
        _ => kind.ToString(),
    };

    private static string GetTypeNoun(ApiTypeKind kind) => kind switch
    {
        ApiTypeKind.Class or ApiTypeKind.RecordClass => "class",
        ApiTypeKind.Struct or ApiTypeKind.RecordStruct => "struct",
        _ => "type",
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

    private static string GetTypeColumnLabel(string section) => section switch
    {
        "Classes" => "Class",
        "Structs" => "Struct",
        "Interfaces" => "Interface",
        "Enums" => "Enum",
        "Delegates" => "Delegate",
        _ => "Type",
    };

    private static void WriteFrontmatter(
        StringBuilder builder,
        string title,
        string description,
        string? descriptionMarkdown,
        PageHeaderMode pageHeader)
    {
        builder.AppendLine("---")
            .Append("title: ").AppendLine(JsonSerializer.Serialize(title, FrontmatterJsonOptions))
            .Append("description: ").AppendLine(JsonSerializer.Serialize(description, FrontmatterJsonOptions));
        if (pageHeader is PageHeaderMode.Frontmatter)
        {
            builder.Append("markdown: ")
                .AppendLine(JsonSerializer.Serialize(descriptionMarkdown ?? description, FrontmatterJsonOptions))
                .AppendLine("docs: true");
        }

        builder.AppendLine("---").AppendLine();
    }

    private static void WritePageHeader(
        StringBuilder builder,
        string title,
        string? description,
        PageHeaderMode pageHeader)
    {
        if (pageHeader is PageHeaderMode.Frontmatter)
        {
            return;
        }

        builder.Append("# ").AppendLine(title).AppendLine();
        WriteIfPresent(builder, description);
    }

    private static void WriteIfPresent(StringBuilder builder, string? value, string? prefix = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        EnsureBlankLine(builder);
        if (prefix is not null)
        {
            builder.Append(prefix);
        }

        builder.AppendLine(value).AppendLine();
    }

    private static void WriteRemarks(StringBuilder builder, string? remarks)
    {
        if (string.IsNullOrWhiteSpace(remarks))
        {
            return;
        }

        EnsureBlankLine(builder);
        foreach (var line in remarks.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            builder.Append('>');
            if (line.Length > 0)
            {
                builder.Append(' ').Append(line);
            }

            builder.AppendLine();
        }

        builder.AppendLine();
    }

    private static string FormatMemberDeclaration(string declaration)
    {
        var lines = declaration.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var formatted = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            FormatDeclarationLine(line, formatted);
        }

        return string.Join(Environment.NewLine, formatted);
    }

    private static void FormatDeclarationLine(string line, ICollection<string> output)
    {
        var open = line.IndexOf('(');
        if (open < 0)
        {
            output.Add(line);
            return;
        }

        var close = FindMatchingParenthesis(line, open);
        if (close < 0)
        {
            output.Add(line);
            return;
        }

        var parameters = SplitTopLevel(line[(open + 1)..close]);
        if (parameters.Count == 0)
        {
            output.Add(line);
            return;
        }

        var indentation = line[..(line.Length - line.TrimStart().Length)];
        output.Add(line[..(open + 1)]);
        for (var index = 0; index < parameters.Count; index++)
        {
            var suffix = index < parameters.Count - 1 ? "," : line[close..];
            output.Add($"{indentation}  {parameters[index]}{suffix}");
        }
    }

    private static int FindMatchingParenthesis(string value, int open)
    {
        var depth = 0;
        for (var index = open; index < value.Length; index++)
        {
            depth += value[index] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0,
            };
            if (depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitTopLevel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var parts = new List<string>();
        var start = 0;
        var round = 0;
        var square = 0;
        var curly = 0;
        var angle = 0;
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (value[index] == '\\')
                {
                    escaped = true;
                }
                else if (value[index] == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (value[index])
            {
                case '\"':
                case '\'':
                    quote = value[index];
                    break;
                case '(':
                    round++;
                    break;
                case ')':
                    round--;
                    break;
                case '[':
                    square++;
                    break;
                case ']':
                    square--;
                    break;
                case '{':
                    curly++;
                    break;
                case '}':
                    curly--;
                    break;
                case '<':
                    angle++;
                    break;
                case '>':
                    angle--;
                    break;
                case ',' when round == 0 && square == 0 && curly == 0 && angle == 0:
                    parts.Add(value[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        parts.Add(value[start..].Trim());
        return parts;
    }

    private static void EnsureBlankLine(StringBuilder builder)
    {
        var lineBreaks = 0;
        for (var index = builder.Length - 1; index >= 0; index--)
        {
            if (builder[index] == '\n')
            {
                lineBreaks++;
            }
            else if (builder[index] != '\r')
            {
                break;
            }
        }

        while (lineBreaks++ < 2)
        {
            builder.AppendLine();
        }
    }

    private static string EscapeTable(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);
    }

    private static string EscapeMarkdownText(string value)
    {
        return value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
