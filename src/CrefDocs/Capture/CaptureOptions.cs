namespace CrefDocs.Capture;

internal sealed record CaptureOptions(
    string ProjectPath,
    string TargetFramework,
    string PackageId,
    string PackageVersion,
    string? SourceRoot = null,
    string Configuration = "Release",
    string? MetadataPath = null);
