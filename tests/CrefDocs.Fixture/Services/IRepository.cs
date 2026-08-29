using CrefDocs.Fixture.Models;

namespace CrefDocs.Fixture.Services;

/// <summary>Reads values from a data source.</summary>
/// <typeparam name="T">The type of value stored by the repository.</typeparam>
public interface IRepository<T>
    where T : class
{
    /// <summary>Raised after a value is read.</summary>
    event EventHandler<T>? Read;

    /// <summary>Reads the value identified by <paramref name="id"/>.</summary>
    /// <param name="id">The value identifier.</param>
    /// <returns>The matching value.</returns>
    /// <exception cref="KeyNotFoundException">No value has the supplied identifier.</exception>
    Result<T> Get(string id);

    /// <summary>Reads a value asynchronously.</summary>
    /// <param name="id">The value identifier.</param>
    /// <returns>The matching value once the operation completes.</returns>
    Task<Result<T>> GetAsync(string id);
}
