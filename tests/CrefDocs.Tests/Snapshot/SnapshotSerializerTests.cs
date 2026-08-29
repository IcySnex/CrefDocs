using CrefDocs.Snapshot;

namespace CrefDocs.Tests.Snapshot;

public sealed class SnapshotSerializerTests
{
    [Fact]
    public void SerializeProducesStableReadableJson()
    {
        var snapshot = CreateSnapshot();

        var json = SnapshotSerializer.Serialize(snapshot);

        Assert.Equal(
            """
            {
              "schemaVersion": 1,
              "toolVersion": "0.1.0",
              "package": {
                "id": "Example",
                "version": "1.2.3",
                "assemblyName": "Example",
                "targetFramework": "net10.0"
              },
              "types": [
                {
                  "id": "T:Example.Widget",
                  "name": "Widget",
                  "namespace": "Example",
                  "kind": "class",
                  "declaration": "public sealed class Widget",
                  "sourcePath": "Widgets/Widget.cs",
                  "containingTypeId": null,
                  "baseType": {
                    "displayName": "object",
                    "documentationId": "T:System.Object"
                  },
                  "interfaces": [],
                  "documentation": {
                    "summary": "An example widget.",
                    "remarks": null,
                    "returns": null,
                    "example": null
                  },
                  "typeParameters": [],
                  "members": []
                }
              ]
            }

            """,
            json);
    }

    [Fact]
    public void DeserializeRejectsUnknownSchemaVersion()
    {
        var json = SnapshotSerializer.Serialize(CreateSnapshot())
            .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99", StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() => SnapshotSerializer.Deserialize(json));

        Assert.Contains("schema version 99", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotRoundTripsWithoutLoss()
    {
        var snapshot = CreateSnapshot();

        var restored = SnapshotSerializer.Deserialize(SnapshotSerializer.Serialize(snapshot));

        Assert.Equal(SnapshotSerializer.Serialize(snapshot), SnapshotSerializer.Serialize(restored));
    }

    private static ApiSnapshot CreateSnapshot()
    {
        return new ApiSnapshot(
            ApiSnapshot.CurrentSchemaVersion,
            "0.1.0",
            new ApiPackage("Example", "1.2.3", "Example", "net10.0"),
            [
                new ApiType(
                    "T:Example.Widget",
                    "Widget",
                    "Example",
                    ApiTypeKind.Class,
                    "public sealed class Widget",
                    "Widgets/Widget.cs",
                    null,
                    new ApiReference("object", "T:System.Object"),
                    [],
                    new ApiDocumentation("An example widget.", null, null, null),
                    [],
                    []),
            ]);
    }
}
