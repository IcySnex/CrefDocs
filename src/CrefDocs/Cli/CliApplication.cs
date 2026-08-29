using CrefDocs.Capture;
using CrefDocs.Rendering;
using CrefDocs.Snapshot;

namespace CrefDocs.Cli;

internal sealed class CliApplication(TextWriter output, TextWriter error)
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"] or ["-h"])
            {
                await output.WriteLineAsync(HelpText);
                return 0;
            }

            if (args is ["--version"])
            {
                await output.WriteLineAsync(GetToolVersion());
                return 0;
            }

            var command = args[0];
            var options = CliArguments.Parse(args.Skip(1));
            switch (command)
            {
                case "capture":
                    await CaptureAsync(options, cancellationToken);
                    return 0;
                case "render":
                    await RenderAsync(options, cancellationToken);
                    return 0;
                case "generate":
                    await GenerateAsync(options, cancellationToken);
                    return 0;
                default:
                    throw new CliException($"Unknown command '{command}'.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("CrefDocs was cancelled.");
            return 2;
        }
        catch (Exception exception) when (exception is CliException or IOException or InvalidOperationException)
        {
            await error.WriteLineAsync($"error: {exception.Message}");
            return 1;
        }
    }

    private async Task CaptureAsync(CliArguments args, CancellationToken cancellationToken)
    {
        args.EnsureOnly(
            "project", "framework", "package", "version", "source-root", "configuration", "metadata", "output");
        var snapshot = await CaptureProjectAsync(args, cancellationToken);
        var path = args.Required("output");
        await SnapshotSerializer.WriteAsync(snapshot, path, cancellationToken);
        await output.WriteLineAsync($"Captured {snapshot.Types.Count} public types to {Path.GetFullPath(path)}.");
    }

    private async Task RenderAsync(CliArguments args, CancellationToken cancellationToken)
    {
        args.EnsureOnly("snapshot", "output", "structure", "base-route", "no-root-index");
        var snapshot = await SnapshotSerializer.ReadAsync(args.Required("snapshot"), cancellationToken);
        await RenderSnapshotAsync(snapshot, args, cancellationToken);
    }

    private async Task GenerateAsync(CliArguments args, CancellationToken cancellationToken)
    {
        args.EnsureOnly(
            "project", "framework", "package", "version", "source-root", "configuration",
            "metadata", "output", "snapshot-output", "structure", "base-route", "no-root-index");
        var snapshot = await CaptureProjectAsync(args, cancellationToken);
        var snapshotOutput = args.Optional("snapshot-output");
        if (snapshotOutput is not null)
        {
            await SnapshotSerializer.WriteAsync(snapshot, snapshotOutput, cancellationToken);
        }

        await RenderSnapshotAsync(snapshot, args, cancellationToken);
    }

    private static Task<ApiSnapshot> CaptureProjectAsync(
        CliArguments args,
        CancellationToken cancellationToken)
    {
        return new ProjectSnapshotCapture().CaptureAsync(
            new CaptureOptions(
                args.Required("project"),
                args.Required("framework"),
                args.Required("package"),
                args.Required("version"),
                args.Optional("source-root"),
                args.Optional("configuration", "Release"),
                args.Optional("metadata")),
            cancellationToken);
    }

    private async Task RenderSnapshotAsync(
        ApiSnapshot snapshot,
        CliArguments args,
        CancellationToken cancellationToken)
    {
        var outputPath = args.Required("output");
        var structure = ParseStructure(args.Optional("structure", "namespace"));
        var options = new RenderOptions(
            outputPath,
            args.Optional("base-route", "/reference"),
            structure,
            !args.Flag("no-root-index"));
        var files = new MarkdownRenderer().Render(snapshot, options);
        await RenderedFileWriter.WriteAsync(outputPath, files, cancellationToken);
        await output.WriteLineAsync($"Rendered {files.Count} Markdown files to {Path.GetFullPath(outputPath)}.");
    }

    private static StructureMode ParseStructure(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "flat" => StructureMode.Flat,
            "source" => StructureMode.Source,
            "namespace" => StructureMode.Namespace,
            _ => throw new CliException("Option '--structure' must be flat, source, or namespace."),
        };
    }

    private static string GetToolVersion()
    {
        return typeof(CliApplication).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private const string HelpText =
        """
        CrefDocs generates linked Markdown API reference documentation for .NET projects.

        Commands:
          crefdocs capture  Capture a released API into crefdocs.json.
          crefdocs render   Render a crefdocs.json snapshot as Markdown.
          crefdocs generate Capture and render a local project in one step.

        Capture options:
          --project <path>         Project file to evaluate.
          --framework <tfm>       Target framework to capture.
          --package <id>          Package identifier stored in the snapshot.
          --version <version>     Released package version stored in the snapshot.
          --source-root <path>    Root used for source-relative paths. Defaults to the project directory.
          --configuration <name>  Build configuration. Defaults to Release.
          --metadata <path>       Optional API index descriptions to embed in the snapshot.
          --output <path>         Snapshot file for capture, Markdown directory for generate.

        Render options:
          --snapshot <path>       crefdocs.json to render.
          --output <directory>    Markdown output directory.
          --structure <mode>      namespace, source, or flat. Defaults to namespace.
          --base-route <route>    Documentation route root. Defaults to /reference.
          --no-root-index         Leave the root index page to the documentation project.

        Generate additionally accepts --snapshot-output <path>.
        """;
}
