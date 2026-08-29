using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrefDocs.Snapshot;

internal static class SnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
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
}

