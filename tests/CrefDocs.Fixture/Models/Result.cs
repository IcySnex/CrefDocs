namespace CrefDocs.Fixture.Models;

/// <summary>A result produced by an operation.</summary>
/// <typeparam name="T">The type of the result value.</typeparam>
/// <param name="Value">The result value.</param>
public readonly record struct Result<T>(T Value);

