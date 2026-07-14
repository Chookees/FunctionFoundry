namespace FunctionFoundry.Storage;

/// <summary>
/// Describes how symbolic links are handled when building Merkle directory snapshots.
/// </summary>
public enum SymlinkPolicy
{
    /// <summary>Skip symbolic links entirely.</summary>
    Skip,

    /// <summary>Record the link target path without following it.</summary>
    RecordTarget,

    /// <summary>Follow symbolic links to files and directories with cycle protection.</summary>
    Follow,
}
