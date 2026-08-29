namespace CrefDocs.Fixture;

/// <summary>The state of an operation.</summary>
public enum Status
{
    /// <summary>The operation has not started.</summary>
    Pending,

    /// <summary>The operation completed.</summary>
    Complete,
}

/// <summary>Handles a change in <see cref="Status"/>.</summary>
/// <param name="status">The new status.</param>
public delegate void StatusChanged(Status status);

