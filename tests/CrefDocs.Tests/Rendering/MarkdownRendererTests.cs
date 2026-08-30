using CrefDocs.Capture;
using CrefDocs.Rendering;
using CrefDocs.Snapshot;

namespace CrefDocs.Tests.Rendering;

public sealed class MarkdownRendererTests
{
    [Fact]
    public void RenderTypeUsesStableMarkdownLayout()
    {
        var snapshot = new ApiSnapshot(
            ApiSnapshot.CurrentSchemaVersion,
            "0.1.0",
            new ApiPackage("Example", "1.0.0", "Example", "net10.0"),
            ApiIndexMetadata.Empty,
            [
                new ApiType(
                    "T:Example.Widget",
                    "Widget",
                    "Example",
                    ApiTypeKind.Class,
                    "public sealed class Widget",
                    "Widget.cs",
                    null,
                    null,
                    [],
                    new ApiDocumentation("An example widget.", null, null, null),
                    [],
                    []),
            ]);

        var files = new MarkdownRenderer().Render(
            snapshot,
            new RenderOptions("unused", "/reference", StructureMode.Flat));
        var page = Assert.Single(files, file => file.RelativePath == "widget.md").Content;

        Assert.Equal(
            """
            ---
            title: "Widget"
            description: "An example widget."
            ---

            # Widget

            An example widget.

            - **Kind:** Class
            - **Namespace:** [Example](/reference)

            ```csharp
            public sealed class Widget
            ```

            """,
            page);
    }

    [Theory]
    [InlineData((int)StructureMode.Flat, "repository-1.md")]
    [InlineData((int)StructureMode.Source, "services/repository-1.md")]
    [InlineData((int)StructureMode.Namespace, "crefdocs/fixture/services/repository-1.md")]
    public async Task RenderUsesSelectedStructure(int structureValue, string expectedPath)
    {
        var structure = (StructureMode)structureValue;
        var files = await RenderFixtureAsync(structure);

        Assert.Contains(files, file => file.RelativePath == expectedPath);
    }

    [Fact]
    public async Task RenderDisambiguatesGenericTypesWithTheSameName()
    {
        var files = await RenderFixtureAsync(StructureMode.Source);

        Assert.Contains(files, file => file.RelativePath == "collections/collection-1.md");
        var grouped = Assert.Single(files, file => file.RelativePath == "collections/collection-2.md").Content;
        Assert.Contains(
            "| <code><a href=\"/reference/services/irepository-1\">IRepository</a>&lt;TItem&gt;</code> `TGroup` |",
            grouped,
            StringComparison.Ordinal);
        Assert.DoesNotContain("*(new())* `TGroup`", grouped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderPreservesDirectoryCasingInVisibleIndexTitles()
    {
        var files = await RenderFixtureAsync(StructureMode.Namespace);
        var index = Assert.Single(files, file => file.RelativePath == "crefdocs/index.md").Content;
        var collections = Assert.Single(
            files,
            file => file.RelativePath == "crefdocs/fixture/collections/index.md").Content;

        Assert.Contains("# CrefDocs", index, StringComparison.Ordinal);
        Assert.Contains("- **Kind:** Namespace", index, StringComparison.Ordinal);
        Assert.Contains("| [CrefDocs.Fixture](/reference/crefdocs/fixture) | Public API fixtures used to verify CrefDocs. |", index, StringComparison.Ordinal);
        Assert.Contains("| Class | Summary |", collections, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderUsesSourceSectionDescriptionsAndParents()
    {
        var files = await RenderFixtureAsync(StructureMode.Source);
        var root = Assert.Single(files, file => file.RelativePath == "index.md").Content;
        var services = Assert.Single(files, file => file.RelativePath == "services/index.md").Content;

        Assert.Contains("Public API fixtures organized by source folder.", root, StringComparison.Ordinal);
        Assert.Contains("- **Kind:** Section", root, StringComparison.Ordinal);
        Assert.Contains("| [Services](/reference/services) | Repository service fixtures. |", root, StringComparison.Ordinal);
        Assert.Contains("description: \"Repository service fixtures.\"", services, StringComparison.Ordinal);
        Assert.Contains("- **Section:** [CrefDocs.Fixture](/reference)", services, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderProducesLinkedMarkdownForTypesAndMembers()
    {
        var files = await RenderFixtureAsync(StructureMode.Source);
        var repository = Assert.Single(files, file => file.RelativePath == "services/repository-1.md").Content;

        Assert.Contains("# Repository&lt;T&gt;", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("IInternalRepositoryMarker", repository, StringComparison.Ordinal);
        Assert.Contains("description: \"An in-memory IRepository<T>.\"", repository, StringComparison.Ordinal);
        Assert.Contains("[`IRepository<T>`](/reference/services/irepository-1)", repository, StringComparison.Ordinal);
        Assert.Contains("<code><a href=\"/reference/models/result-1\">Result</a>&lt;T&gt;</code>", repository, StringComparison.Ordinal);
        Assert.Contains("<code><a href=\"https://learn.microsoft.com/dotnet/api/system.string\">string</a></code>", repository, StringComparison.Ordinal);
        Assert.Contains("Reads the value identified by `id`.", repository, StringComparison.Ordinal);
        Assert.Contains("> Repository instances keep their values in memory.", repository, StringComparison.Ordinal);
        Assert.Contains("> The lookup is performed synchronously.", repository, StringComparison.Ordinal);
        Assert.Contains("> The name is intended for display.", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("Is Read Only", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("- **Type:** [`", repository, StringComparison.Ordinal);
        Assert.Contains("**Type:** <code><a href=\"https://learn.microsoft.com/dotnet/api/system.string\">string</a>?</code>", repository, StringComparison.Ordinal);
        Assert.Contains("public Result<T> Get(\n  string id)", repository, StringComparison.Ordinal);
        var returns = "**Returns**\n\n- <code><a href=\"/reference/models/result-1\">Result</a>&lt;T&gt;</code>: The matching value.";
        Assert.Contains(returns, repository, StringComparison.Ordinal);
        Assert.True(repository.IndexOf(returns, StringComparison.Ordinal) > repository.IndexOf("public Result<T> Get(", StringComparison.Ordinal));
        Assert.Contains("**Returns**\n\n- <code><a href=\"https://learn.microsoft.com/dotnet/api/system.threading.tasks.task-1\">Task</a>&lt;<a href=\"/reference/models/result-1\">Result</a>&lt;T&gt;&gt;</code>: The matching value once the operation completes.", repository, StringComparison.Ordinal);
        Assert.Contains("- <code><a href=\"https://learn.microsoft.com/dotnet/api/system.collections.generic.keynotfoundexception\">KeyNotFoundException</a></code>: No value has the supplied identifier.", repository, StringComparison.Ordinal);
        Assert.Contains("| *(class)* `T` | The type of value stored by the repository. |", repository, StringComparison.Ordinal);
        Assert.Contains("## Events", repository, StringComparison.Ordinal);
        Assert.Contains("## Operators", repository, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderDocumentsPrimaryConstructorsWithoutRepeatingTheTypeHeading()
    {
        var files = await RenderFixtureAsync(StructureMode.Source);
        var result = Assert.Single(files, file => file.RelativePath == "models/result-1.md").Content;

        Assert.Contains("# Result&lt;T&gt;", result, StringComparison.Ordinal);
        Assert.Contains("## Constructors", result, StringComparison.Ordinal);
        Assert.Contains("Initializes a new instance of the <code><a href=\"/reference/models/result-1\">Result&lt;T&gt;</a></code> struct.", result, StringComparison.Ordinal);
        Assert.DoesNotContain("### Result", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriterRemovesOnlyPreviouslyGeneratedFiles()
    {
        var output = Path.Combine(Path.GetTempPath(), $"crefdocs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        var manualPath = Path.Combine(output, "manual.md");
        await File.WriteAllTextAsync(manualPath, "manual");

        try
        {
            await RenderedFileWriter.WriteAsync(output, [new RenderedFile("old.md", "old")]);
            await RenderedFileWriter.WriteAsync(output, [new RenderedFile("new.md", "new")]);

            Assert.False(File.Exists(Path.Combine(output, "old.md")));
            Assert.True(File.Exists(Path.Combine(output, "new.md")));
            Assert.True(File.Exists(manualPath));
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static async Task<IReadOnlyList<RenderedFile>> RenderFixtureAsync(StructureMode structure)
    {
        var repositoryRoot = FindRepositoryRoot();
        var fixtureRoot = Path.Combine(repositoryRoot, "tests/CrefDocs.Fixture");
        var snapshot = await new ProjectSnapshotCapture().CaptureAsync(new CaptureOptions(
            Path.Combine(fixtureRoot, "CrefDocs.Fixture.csproj"),
            "net10.0",
            "CrefDocs.Fixture",
            "1.0.0",
            fixtureRoot,
            MetadataPath: Path.Combine(fixtureRoot, "api-reference.json")));

        return new MarkdownRenderer().Render(
            snapshot,
            new RenderOptions("unused", "/reference", structure));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CrefDocs.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the CrefDocs repository root.");
    }
}
