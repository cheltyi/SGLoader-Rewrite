namespace Marsey.Safety;

/// <summary>
/// Result of checking a patch's hash against the safety catalog.
/// </summary>
public enum PatchVerdict
{
    /// <summary>The hash is listed in approved.json.</summary>
    Approved,

    /// <summary>The hash is in neither list.</summary>
    Unknown,

    /// <summary>The hash is listed in rejected.json.</summary>
    Rejected
}
