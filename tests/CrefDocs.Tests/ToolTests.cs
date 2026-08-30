using CrefDocs.Rendering;
using CrefDocs.Snapshot;

namespace CrefDocs.Tests;

public sealed class ToolTests
{
    [Fact]
    public void ToolAssemblyHasExpectedName()
    {
        Assert.Equal("CrefDocs", typeof(Program).Assembly.GetName().Name);
    }

    [Fact]
    public void DocumentationLinksUseMemberNames()
    {
        var snapshot = new ApiSnapshot(
            ApiSnapshot.CurrentSchemaVersion,
            "test",
            new ApiPackage("Fixture", "1.0.0", "Fixture", "net10.0"),
            ApiIndexMetadata.Empty,
            [Type("T:SkeleKit.Link", "Link"), Type("T:SkeleKit.TextView", "TextView")]);
        var routes = RouteMap.Create(snapshot, new RenderOptions("output", "reference", StructureMode.Flat));
        var documentation = new DocumentationMarkdown(routes);
        const string fragment = "Fires <see cref=\"P:SkeleKit.Link.Command\"/> inside <see cref=\"P:SkeleKit.TextView.Spans\"/>.";

        var markdown = documentation.Render(fragment, "T:SkeleKit.Link");
        var plainText = documentation.RenderPlainText(fragment, "T:SkeleKit.Link");

        Assert.Equal("Fires [`Command`](/reference/link) inside [`TextView.Spans`](/reference/textview).", markdown);
        Assert.Equal("Fires Command inside TextView.Spans.", plainText);
    }

    private static ApiType Type(string id, string name) => new(
        id,
        name,
        "SkeleKit",
        ApiTypeKind.Class,
        $"public class {name}",
        null,
        null,
        null,
        [],
        ApiDocumentation.Empty,
        [],
        []);
}
