namespace CrefDocs.Rendering;

internal sealed record RenderOptions(
    string OutputPath,
    string BaseRoute,
    StructureMode Structure,
    bool GenerateRootIndex = true,
    PageHeaderMode PageHeader = PageHeaderMode.Markdown);

internal enum StructureMode
{
    Flat,
    Source,
    Namespace,
}

internal enum PageHeaderMode
{
    Markdown,
    Frontmatter,
}

internal sealed record RenderedFile(string RelativePath, string Content);
