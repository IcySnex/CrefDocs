using System.Text.Json;

namespace CrefDocs.Rendering;

internal static class RenderedFileWriter
{
    private const string ManifestName = ".crefdocs-manifest.json";

    public static async Task WriteAsync(
        string outputPath,
        IReadOnlyList<RenderedFile> files,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(root);

        var manifestPath = Path.Combine(root, ManifestName);
        foreach (var stalePath in await ReadManifestAsync(manifestPath, cancellationToken))
        {
            var fullPath = ResolveSafePath(root, stalePath);
            if (File.Exists(fullPath) && files.All(file =>
                !string.Equals(file.RelativePath, stalePath, StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(fullPath);
            }
        }

        foreach (var file in files)
        {
            var fullPath = ResolveSafePath(root, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await WriteIfChangedAsync(fullPath, file.Content, cancellationToken);
        }

        var manifest = JsonSerializer.Serialize(
            files.Select(file => file.RelativePath).Order(StringComparer.Ordinal),
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        await WriteIfChangedAsync(manifestPath, manifest, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        return JsonSerializer.Deserialize<string[]>(json) ?? [];
    }

    private static string ResolveSafePath(string root, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Generated path '{relativePath}' escapes the output directory.");
        }

        return fullPath;
    }

    private static async Task WriteIfChangedAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path) && string.Equals(
            await File.ReadAllTextAsync(path, cancellationToken),
            content,
            StringComparison.Ordinal))
        {
            return;
        }

        await File.WriteAllTextAsync(path, content, cancellationToken);
    }
}

