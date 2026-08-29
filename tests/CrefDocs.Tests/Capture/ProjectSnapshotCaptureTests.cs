using CrefDocs.Capture;
using CrefDocs.Snapshot;

namespace CrefDocs.Tests.Capture;

public sealed class ProjectSnapshotCaptureTests
{
    [Fact]
    public async Task CaptureIncludesPublicGenericTypesAndDocumentation()
    {
        var snapshot = await CaptureFixtureAsync();

        var repository = Assert.Single(snapshot.Types, type => type.Id == "T:CrefDocs.Fixture.Services.Repository`1");
        Assert.Equal(ApiTypeKind.Class, repository.Kind);
        Assert.Equal("Services/Repository.cs", repository.SourcePath);
        Assert.Equal("An in-memory <see cref=\"T:CrefDocs.Fixture.Services.IRepository`1\" />.", repository.Documentation.Summary);
        Assert.Equal("class", Assert.Single(repository.TypeParameters).Constraints);

        var get = Assert.Single(repository.Members, member => member.Name == "Get");
        Assert.Equal("Reads the value identified by <paramref name=\"id\" />.", get.Documentation.Summary);
        Assert.Equal("The value identifier.", Assert.Single(get.Parameters).Description);
        Assert.Equal("The matching value.", get.Documentation.Returns);
        Assert.Equal("No value has the supplied identifier.", Assert.Single(get.Exceptions).Description);
    }

    [Fact]
    public async Task CaptureIncludesRecordsEnumsDelegatesEventsAndOperators()
    {
        var snapshot = await CaptureFixtureAsync();

        Assert.Contains(snapshot.Types, type => type.Kind == ApiTypeKind.RecordStruct);
        Assert.Contains(snapshot.Types, type => type.Kind == ApiTypeKind.Enum);
        Assert.Contains(snapshot.Types, type => type.Kind == ApiTypeKind.Delegate);

        var repository = Assert.Single(snapshot.Types, type => type.Id == "T:CrefDocs.Fixture.Services.Repository`1");
        Assert.Contains(repository.Members, member => member.Kind == ApiMemberKind.Event);
        Assert.Contains(repository.Members, member => member.Kind == ApiMemberKind.Operator);
        var name = Assert.Single(repository.Members, member => member.Name == "Name" && member.Kind == ApiMemberKind.Property);
        Assert.False(name.IsReadOnly);

        var result = Assert.Single(snapshot.Types, type => type.Id == "T:CrefDocs.Fixture.Models.Result`1");
        Assert.True(Assert.Single(result.Members, member => member.Kind == ApiMemberKind.Constructor).IsPrimaryConstructor);
        Assert.True(Assert.Single(repository.Members, member => member.Name == "ReadCount").IsReadOnly);
    }

    [Fact]
    public async Task CaptureFlattensExtensionBlocksIntoTheirPublicContainer()
    {
        var snapshot = await CaptureFixtureAsync();

        var extensions = Assert.Single(snapshot.Types, type => type.Name == "ResultExtensions");
        var unwrap = Assert.Single(extensions.Members);
        Assert.Equal("Unwrap", unwrap.Name);
        Assert.Contains("extension<T>(Result<T> result)", unwrap.Declaration, StringComparison.Ordinal);
        Assert.Equal("result", Assert.Single(unwrap.Parameters).Name);
        Assert.Equal("The result being read.", Assert.Single(unwrap.Parameters).Description);
        Assert.Equal("T", Assert.Single(unwrap.TypeParameters).Name);
    }

    private static Task<ApiSnapshot> CaptureFixtureAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        return new ProjectSnapshotCapture().CaptureAsync(new CaptureOptions(
            Path.Combine(repositoryRoot, "tests/CrefDocs.Fixture/CrefDocs.Fixture.csproj"),
            "net10.0",
            "CrefDocs.Fixture",
            "1.0.0",
            Path.Combine(repositoryRoot, "tests/CrefDocs.Fixture")));
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
