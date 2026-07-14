namespace FunctionFoundry.Storage;

/// <summary>
/// A single action recorded in a deterministic recovery report.
/// </summary>
/// <param name="Path">Normalized relative path affected by recovery.</param>
/// <param name="Action">Recovery action name (for example, <c>rollback-staging</c> or <c>complete-commit</c>).</param>
/// <param name="Detail">Optional human-readable detail sorted deterministically by the host.</param>
public sealed record RecoveryReportEntry(string Path, string Action, string Detail);

/// <summary>
/// Deterministic report produced by <see cref="TransactionalFileSetRecovery"/>.
/// </summary>
/// <param name="TransactionId">Recovered transaction identifier.</param>
/// <param name="FinalStatus">Final journal status after recovery.</param>
/// <param name="Entries">Ordered recovery actions.</param>
public sealed record RecoveryReport(
    string TransactionId,
    string FinalStatus,
    IReadOnlyList<RecoveryReportEntry> Entries)
{
    /// <summary>Gets a stable text rendering used by tests and operators.</summary>
    public string ToDeterministicText()
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(TransactionId);
        builder.Append('|');
        builder.Append(FinalStatus);
        foreach (RecoveryReportEntry entry in Entries)
        {
            builder.Append('\n');
            builder.Append(entry.Path);
            builder.Append('|');
            builder.Append(entry.Action);
            builder.Append('|');
            builder.Append(entry.Detail);
        }

        return builder.ToString();
    }
}
