using CrefDocs.Fixture.Models;

namespace CrefDocs.Fixture.Services;

/// <summary>An in-memory <see cref="IRepository{T}"/>.</summary>
/// <typeparam name="T">The type of value stored by the repository.</typeparam>
public sealed partial class Repository<T> : IRepository<T>
    where T : class
{
    /// <inheritdoc/>
    public event EventHandler<T>? Read;

    /// <inheritdoc/>
    public Result<T> Get(string id) => throw new KeyNotFoundException(id);

    /// <summary>Returns the supplied value.</summary>
    public static implicit operator Repository<T>(T value) => new();

    private void OnRead(T value) => Read?.Invoke(this, value);
}

