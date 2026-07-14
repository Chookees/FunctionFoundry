namespace FunctionFoundry.Security;

/// <summary>
/// Describes a single payload that requires key rotation.
/// </summary>
/// <param name="PayloadId">Caller-supplied payload identifier. Must not be null.</param>
/// <param name="CurrentKeyId">Key identifier currently wrapping the payload.</param>
/// <param name="TargetKeyId">Key identifier that should wrap the payload after rotation.</param>
/// <param name="FormatVersion">Envelope format version observed in metadata.</param>
public sealed record RotationAction(
    string PayloadId,
    string CurrentKeyId,
    string TargetKeyId,
    byte FormatVersion);

/// <summary>
/// Deterministic plan enumerating payloads that still need re-encryption.
/// </summary>
/// <param name="TargetKeyId">Destination key for rotation.</param>
/// <param name="Actions">Ordered actions. Never implies silent deletion of old payloads.</param>
/// <param name="AlreadyCurrentCount">Count of payloads already under the target key.</param>
public sealed record KeyRotationPlan(
    string TargetKeyId,
    IReadOnlyList<RotationAction> Actions,
    int AlreadyCurrentCount);

/// <summary>
/// Analyzes envelope metadata and produces a deterministic key-rotation plan.
/// </summary>
/// <remarks>
/// <para>This planner never mutates ciphertext and never destroys old data.</para>
/// <para>Determinism: actions are ordered by payload id using ordinal comparison.</para>
/// <para>Thread safety: static methods are thread-safe.</para>
/// </remarks>
public static class KeyRotationPlanner
{
    /// <summary>
    /// Builds a rotation plan for envelopes that are not yet encrypted under <paramref name="targetKeyId"/>.
    /// </summary>
    /// <param name="payloads">Map of payload identifier to envelope metadata. Must not be null.</param>
    /// <param name="targetKeyId">Desired key identifier after rotation. Must not be null or empty.</param>
    /// <returns>A plan listing only payloads that require re-encryption.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="payloads"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="targetKeyId"/> is invalid.</exception>
    /// <example>
    /// <code>
    /// var plan = KeyRotationPlanner.CreatePlan(metadataByPayloadId, targetKeyId: "k2");
    /// foreach (RotationAction action in plan.Actions)
    /// {
    ///     // decrypt with action.CurrentKeyId and encrypt with action.TargetKeyId
    /// }
    /// </code>
    /// </example>
    public static KeyRotationPlan CreatePlan(
        IReadOnlyDictionary<string, EnvelopeMetadata> payloads,
        string targetKeyId)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKeyId);

        var actions = new List<RotationAction>();
        int alreadyCurrent = 0;
        foreach (KeyValuePair<string, EnvelopeMetadata> pair in payloads.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            if (string.Equals(pair.Value.KeyId, targetKeyId, StringComparison.Ordinal))
            {
                alreadyCurrent++;
                continue;
            }

            actions.Add(new RotationAction(pair.Key, pair.Value.KeyId, targetKeyId, pair.Value.FormatVersion));
        }

        return new KeyRotationPlan(targetKeyId, actions, alreadyCurrent);
    }
}
