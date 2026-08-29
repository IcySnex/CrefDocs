namespace CrefDocs.Tests;

public sealed class ToolTests
{
    [Fact]
    public void ToolAssemblyHasExpectedName()
    {
        Assert.Equal("CrefDocs", typeof(Program).Assembly.GetName().Name);
    }
}

