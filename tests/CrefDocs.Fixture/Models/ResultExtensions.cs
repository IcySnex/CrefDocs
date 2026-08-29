namespace CrefDocs.Fixture.Models;

/// <summary>Extensions for reading result values.</summary>
public static class ResultExtensions
{
    /// <typeparam name="T">The result value type.</typeparam>
    /// <param name="result">The result being read.</param>
    extension<T>(Result<T> result)
    {
        /// <summary>Returns the underlying result value.</summary>
        /// <returns>The stored value.</returns>
        public T Unwrap() => result.Value;
    }
}
