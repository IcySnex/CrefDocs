using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace CrefDocs.Snapshot;

internal static class SnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter<ApiTypeKind>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<ApiMemberKind>(JsonNamingPolicy.CamelCase),
        },
    };

    public static string Serialize(ApiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, Options) + Environment.NewLine;
    }

    public static ApiSnapshot Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var snapshot = JsonSerializer.Deserialize<ApiSnapshot>(json, Options)
            ?? throw new InvalidDataException("The snapshot is empty.");

        if (snapshot.SchemaVersion != ApiSnapshot.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported CrefDocs schema version {snapshot.SchemaVersion}. Expected {ApiSnapshot.CurrentSchemaVersion}.");
        }

        return snapshot;
    }

    public static async Task WriteAsync(
        ApiSnapshot snapshot,
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var json = Serialize(snapshot);
        if (File.Exists(fullPath) && string.Equals(
            await File.ReadAllTextAsync(fullPath, cancellationToken),
            json,
            StringComparison.Ordinal))
        {
            return;
        }

        var temporaryPath = fullPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, fullPath, overwrite: true);
    }

    public static async Task<ApiSnapshot> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The CrefDocs snapshot does not exist.", fullPath);
        }

        return Deserialize(await File.ReadAllTextAsync(fullPath, cancellationToken));
    }
}
