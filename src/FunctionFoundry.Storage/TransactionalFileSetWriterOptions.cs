namespace FunctionFoundry.Storage;

/// <summary>
/// Options for <see cref="TransactionalFileSetWriter"/>.
/// </summary>
/// <param name="StagingRootDirectory">Root directory for transaction staging folders. Created when missing.</param>
/// <param name="PreferAtomicReplace">When true, same-volume <see cref="File.Move(string,string,bool)"/> is attempted before copy-and-delete fallback.</param>
/// <param name="JournalFileName">Journal file name inside each staging directory.</param>
public sealed record TransactionalFileSetWriterOptions(
    string? StagingRootDirectory = null,
    bool PreferAtomicReplace = true,
    string JournalFileName = "journal.json")
{
    /// <summary>Gets a validated copy of these options.</summary>
    public TransactionalFileSetWriterOptions Validate()
    {
        if (string.IsNullOrWhiteSpace(JournalFileName))
        {
            throw new ArgumentException("Journal file name must not be empty.", nameof(JournalFileName));
        }

        return this;
    }
}
