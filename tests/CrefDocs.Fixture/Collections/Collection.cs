using CrefDocs.Fixture.Services;

namespace CrefDocs.Fixture.Collections;

/// <summary>A collection of <typeparamref name="TItem"/> values.</summary>
public sealed class Collection<TItem>;

/// <summary>A grouped collection of <typeparamref name="TItem"/> values.</summary>
public sealed class Collection<TItem, TGroup>
    where TItem : class
    where TGroup : IRepository<TItem>, new();
