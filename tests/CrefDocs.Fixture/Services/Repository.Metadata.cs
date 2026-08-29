namespace CrefDocs.Fixture.Services;

public sealed partial class Repository<T>
{
    /// <summary>A human-readable repository name.</summary>
    /// <remarks>The name is intended for display.</remarks>
    public string? Name { get; init; }
}
