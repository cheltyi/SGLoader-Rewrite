namespace Marsey.Safety;

/// <summary>
/// Controls how strictly patches are checked against the safety catalog
/// (approved.json / rejected.json) before they are allowed to run.
/// </summary>
public enum PatchSafetyLevel
{
    /// <summary>
    /// Only approved (green) patches may run. Unknown and rejected patches are blocked.
    /// </summary>
    Block = 0,

    /// <summary>
    /// Rejected (red) patches are blocked. Approved and unknown patches may run.
    /// </summary>
    Warn = 1,

    /// <summary>
    /// All patches may run regardless of their verdict.
    /// </summary>
    Pass = 2
}
