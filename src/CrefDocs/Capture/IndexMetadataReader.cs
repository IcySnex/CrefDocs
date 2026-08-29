using System.Text.Json;
using CrefDocs.Snapshot;

namespace CrefDocs.Capture;

internal static class IndexMetadataReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<ApiIndexMetadata> ReadAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ApiIndexMetadata.Empty;
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The API reference metadata file does not exist.", fullPath);
        }

        try
        {
            await using var stream = File.OpenRead(fullPath);
            var document = await JsonSerializer.DeserializeAsync<MetadataDocument>(stream, Options, cancellationToken)
                ?? new MetadataDocument();
            return new ApiIndexMetadata(
                Normalize(document.Namespaces, NormalizeNamespaceKey, "namespace"),
                Normalize(document.Sections, NormalizeSectionKey, "section"));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The API reference metadata file is invalid: {exception.Message}", exception);
        }
    }

    private static IReadOnlyList<ApiIndexDescription> Normalize(
        IReadOnlyDictionary<string, string>? entries,
        Func<string, string> normalizeKey,
        string kind)
    {
        if (entries is null)
        {
            return [];
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawKey, rawDescription) in entries)
        {
            var key = normalizeKey(rawKey);
            var description = rawDescription?.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                throw new InvalidDataException($"The {kind} description for '{rawKey}' is empty.");
            }

            if (!normalized.TryAdd(key, description))
            {
                throw new InvalidDataException($"The {kind} metadata key '{key}' is duplicated.");
            }
        }

        return normalized
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ApiIndexDescription(entry.Key, entry.Value))
            .ToArray();
    }

    private static string NormalizeNamespaceKey(string key)
    {
        var normalized = key.Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidDataException("Namespace metadata keys cannot be empty.");
        }

        return normalized;
    }

    private static string NormalizeSectionKey(string key)
    {
        var normalized = key.Trim().Replace('\\', '/').Trim('/');
        if (normalized == ".")
        {
            return string.Empty;
        }

        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
        {
            throw new InvalidDataException($"Section metadata key '{key}' cannot contain '..'.");
        }

        return normalized;
    }

    private sealed record MetadataDocument
    {
        public Dictionary<string, string>? Namespaces { get; init; }

        public Dictionary<string, string>? Sections { get; init; }
    }
}
