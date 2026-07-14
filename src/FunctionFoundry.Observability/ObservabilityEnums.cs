namespace FunctionFoundry.Observability;

/// <summary>
/// Severity levels used by adaptive sampling and burst prioritization.
/// </summary>
public enum EventSeverity
{
    /// <summary>Verbose diagnostic detail.</summary>
    Verbose = 0,

    /// <summary>Informational lifecycle events.</summary>
    Information = 1,

    /// <summary>Abnormal but recoverable conditions.</summary>
    Warning = 2,

    /// <summary>Operation failures requiring attention.</summary>
    Error = 3,

    /// <summary>Severe failures with user or data impact.</summary>
    Critical = 4,

    /// <summary>Unrecoverable failures.</summary>
    Fatal = 5,
}

/// <summary>
/// Strategy applied when sensitive data is detected.
/// </summary>
public enum RedactionReplacementStrategy
{
    /// <summary>Replace with a fixed redaction token.</summary>
    Redacted = 0,

    /// <summary>Mask interior characters while preserving edge hints.</summary>
    Masked = 1,

    /// <summary>Replace with a deterministic SHA-256 prefix.</summary>
    Hashed = 2,

    /// <summary>Omit the value entirely.</summary>
    Removed = 3,
}

/// <summary>
/// How a property name is matched against sensitive-name policies.
/// </summary>
public enum PropertyNameMatchMode
{
    /// <summary>Case-insensitive exact equality.</summary>
    Exact = 0,

    /// <summary>Case-insensitive substring containment.</summary>
    Contains = 1,

    /// <summary>Case-insensitive suffix.</summary>
    Suffix = 2,
}
