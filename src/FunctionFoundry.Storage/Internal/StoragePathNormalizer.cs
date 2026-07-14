namespace FunctionFoundry.Storage.Internal;

internal static class StoragePathNormalizer
{
    public static string NormalizeRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Relative paths must not be rooted.", nameof(relativePath));
        }

        string normalized = relativePath.Replace('\\', '/');
        if (normalized.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Paths must not contain null characters.", nameof(relativePath));
        }

        if (normalized is "." or "./")
        {
            throw new ArgumentException("Relative paths must not be empty or root.", nameof(relativePath));
        }

        var segments = new List<string>();
        foreach (ReadOnlySpan<char> segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment.SequenceEqual("."))
            {
                continue;
            }

            if (segment.SequenceEqual(".."))
            {
                if (segments.Count == 0)
                {
                    throw new ArgumentException("Relative paths must not escape the root.", nameof(relativePath));
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment.ToString());
        }

        if (segments.Count == 0)
        {
            throw new ArgumentException("Relative paths must not be empty or root.", nameof(relativePath));
        }

        return string.Join('/', segments);
    }

    public static string NormalizeForComparison(string normalizedPath, bool ignoreCase)
    {
        return ignoreCase
            ? normalizedPath.ToUpperInvariant()
            : normalizedPath;
    }

    public static string CombineTargetPath(string targetDirectory, string normalizedRelativePath)
    {
        string[] segments = normalizedRelativePath.Split('/');
        string path = targetDirectory;
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    public static void EnsureWithinRoot(string rootDirectory, string candidatePath)
    {
        string fullRoot = Path.GetFullPath(rootDirectory);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        string fullCandidate = Path.GetFullPath(candidatePath);
        if (!fullCandidate.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolved path escapes the configured root directory.");
        }
    }
}
