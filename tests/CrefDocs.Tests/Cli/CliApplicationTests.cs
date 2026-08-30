using CrefDocs.Cli;

namespace CrefDocs.Tests.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task HelpDescribesSnapshotAndStructureOptions()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await new CliApplication(output, error).RunAsync(["--help"]);

        Assert.Equal(0, exitCode);
        Assert.Contains("crefdocs capture", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("namespace, source, or flat", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--page-header", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("--metadata", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task UnknownOptionsAreRejected()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await new CliApplication(output, error).RunAsync([
            "render",
            "--snapshot", "crefdocs.json",
            "--output", "reference",
            "--surprise",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown option '--surprise'", error.ToString(), StringComparison.Ordinal);
    }
}
