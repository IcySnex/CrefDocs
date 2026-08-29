using CrefDocs.Capture;
using CrefDocs.Rendering;
using CrefDocs.Snapshot;

namespace CrefDocs.Tests.Rendering;

public sealed class MarkdownRendererTests
{
    [Theory]
    [InlineData((int)StructureMode.Flat, "repository.md")]
    [InlineData((int)StructureMode.Source, "services/repository.md")]
    [InlineData((int)StructureMode.Namespace, "cref-docs/fixture/services/repository.md")]
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

        Assert.Contains(files, file => file.RelativePath == "collections/collection.md");
        Assert.Contains(files, file => file.RelativePath == "collections/collection-2.md");
    }

    [Fact]
    public async Task RenderProducesLinkedMarkdownForTypesAndMembers()
    {
        var files = await RenderFixtureAsync(StructureMode.Source);
        var repository = Assert.Single(files, file => file.RelativePath == "services/repository.md").Content;

        Assert.Contains("# Repository<T>", repository, StringComparison.Ordinal);
        Assert.Contains("[`IRepository<T>`](/reference/services/i-repository)", repository, StringComparison.Ordinal);
        Assert.Contains("[`Result<T>`](/reference/models/result)", repository, StringComparison.Ordinal);
        Assert.Contains("[`string`](https://learn.microsoft.com/dotnet/api/system.string)", repository, StringComparison.Ordinal);
        Assert.Contains("Reads the value identified by `id`.", repository, StringComparison.Ordinal);
        Assert.Contains("## Events", repository, StringComparison.Ordinal);
        Assert.Contains("## Operators", repository, StringComparison.Ordinal);
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
            fixtureRoot));

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
