using CrefDocs.Snapshot;

namespace CrefDocs.Rendering;

internal sealed class RouteMap
{
    private readonly Dictionary<string, string> _routes;
    private readonly Dictionary<string, string> _displayNames;

    private RouteMap(Dictionary<string, string> routes, Dictionary<string, string> displayNames)
    {
        _routes = routes;
        _displayNames = displayNames;
    }

    public static RouteMap Create(ApiSnapshot snapshot, RenderOptions options)
    {
        var candidates = snapshot.Types.Select(type => new RouteCandidate(
            type,
            GetDirectory(type, options.Structure),
            Slug.Create(GetSimpleTypeName(type.Name)),
            GetGenericArity(type.Name))).ToArray();
        var routes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in candidates.GroupBy(
            candidate => $"{candidate.Directory}/{candidate.BaseSlug}",
            StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderBy(candidate => candidate.GenericArity)
                .ThenBy(candidate => candidate.Type.Id, StringComparer.Ordinal)
                .ToArray();

            for (var index = 0; index < ordered.Length; index++)
            {
                var candidate = ordered[index];
                var suffix = index == 0
                    ? string.Empty
                    : candidate.GenericArity > 0
                        ? $"-{candidate.GenericArity}"
                        : $"-{index + 1}";
                var relative = Join(candidate.Directory, candidate.BaseSlug + suffix);
                routes.Add(candidate.Type.Id, CombineRoute(options.BaseRoute, relative));
            }
        }

        return new RouteMap(
            routes,
            snapshot.Types.ToDictionary(type => type.Id, type => type.Name, StringComparer.Ordinal));
    }

    public string this[string documentationId] => _routes[documentationId];

    public bool TryGetRoute(string? documentationId, out string route)
    {
        if (documentationId is not null && _routes.TryGetValue(documentationId, out var found))
        {
            route = found;
            return true;
        }

        route = string.Empty;
        return false;
    }

    public bool TryGetDisplayName(string? documentationId, out string displayName)
    {
        if (documentationId is not null && _displayNames.TryGetValue(documentationId, out var found))
        {
            displayName = found;
            return true;
        }

        displayName = string.Empty;
        return false;
    }

    public string GetRelativeFilePath(string documentationId, string baseRoute)
    {
        var route = this[documentationId];
        var normalizedBase = "/" + baseRoute.Trim('/');
        var relative = route.StartsWith(normalizedBase + "/", StringComparison.Ordinal)
            ? route[(normalizedBase.Length + 1)..]
            : route.TrimStart('/');
        return relative + ".md";
    }

    internal static string GetNamespaceRoute(string @namespace, RenderOptions options)
    {
        var relative = options.Structure == StructureMode.Namespace
            ? string.Join('/', @namespace
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(Slug.Create))
            : string.Empty;
        return CombineRoute(options.BaseRoute, relative);
    }

    internal static string GetDirectory(ApiType type, StructureMode structure)
    {
        return structure switch
        {
            StructureMode.Flat => string.Empty,
            StructureMode.Namespace => string.Join('/', type.Namespace
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(Slug.Create)),
            StructureMode.Source => GetSourceDirectory(type),
            _ => throw new ArgumentOutOfRangeException(nameof(structure)),
        };
    }

    private static string GetSourceDirectory(ApiType type)
    {
        if (string.IsNullOrWhiteSpace(type.SourcePath))
        {
            return string.Empty;
        }

        var directory = Path.GetDirectoryName(type.SourcePath.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(directory) || directory == ".")
        {
            return string.Empty;
        }

        return string.Join('/', directory
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Select(Slug.Create));
    }

    private static string GetSimpleTypeName(string name)
    {
        var generic = name.IndexOf('<');
        return generic < 0 ? name : name[..generic];
    }

    private static int GetGenericArity(string name)
    {
        var open = name.IndexOf('<');
        var close = name.LastIndexOf('>');
        if (open < 0 || close <= open)
        {
            return 0;
        }

        return name[(open + 1)..close].Count(character => character == ',') + 1;
    }

    private static string Join(string directory, string name)
    {
        return string.IsNullOrEmpty(directory) ? name : $"{directory}/{name}";
    }

    private static string CombineRoute(string baseRoute, string relative)
    {
        var root = "/" + baseRoute.Trim('/');
        return string.IsNullOrEmpty(relative) ? root : $"{root}/{relative}";
    }

    private sealed record RouteCandidate(ApiType Type, string Directory, string BaseSlug, int GenericArity);
}
