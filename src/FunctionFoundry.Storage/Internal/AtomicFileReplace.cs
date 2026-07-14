namespace FunctionFoundry.Storage.Internal;

/// <summary>
/// Result of an atomic or fallback file replacement attempt.
/// </summary>
internal enum AtomicReplaceMode
{
    /// <summary>File.Move with overwrite on the same volume.</summary>
    AtomicMove,

    /// <summary>Copy to destination followed by source deletion.</summary>
    CopyAndDelete,
}

internal static class AtomicFileReplace
{
    public static AtomicReplaceMode Replace(string sourcePath, string destinationPath, bool preferAtomicMove)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (preferAtomicMove && CanUseAtomicMove(sourcePath, destinationPath))
        {
            File.Move(sourcePath, destinationPath, overwrite: true);
            return AtomicReplaceMode.AtomicMove;
        }

        string tempDestination = destinationPath + ".ff-tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(sourcePath, tempDestination, overwrite: true);
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempDestination, destinationPath);
            File.Delete(sourcePath);
            return AtomicReplaceMode.CopyAndDelete;
        }
        finally
        {
            if (File.Exists(tempDestination))
            {
                File.Delete(tempDestination);
            }
        }
    }

    private static bool CanUseAtomicMove(string sourcePath, string destinationPath)
    {
        try
        {
            string sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath)) ?? string.Empty;
            string destinationRoot = Path.GetPathRoot(Path.GetFullPath(destinationPath)) ?? string.Empty;
            return string.Equals(sourceRoot, destinationRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
