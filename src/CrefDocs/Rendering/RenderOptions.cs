namespace CrefDocs.Rendering;

internal sealed record RenderOptions(
    string OutputPath,
    string BaseRoute,
    StructureMode Structure,
    bool GenerateRootIndex = true);

internal enum StructureMode
{
    Flat,
    Source,
    Namespace,
}

internal sealed record RenderedFile(string RelativePath, string Content);

