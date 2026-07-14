namespace FunctionFoundry.Storage;

/// <summary>
/// Options for <see cref="ContentAddressedStore"/>.
/// </summary>
/// <param name="ObjectsSubdirectory">Relative subdirectory under the store root where objects are stored.</param>
public sealed record ContentAddressedStoreOptions(string ObjectsSubdirectory = "objects")
{
    /// <summary>Gets a validated copy of these options.</summary>
    public ContentAddressedStoreOptions Validate()
    {
        if (string.IsNullOrWhiteSpace(ObjectsSubdirectory))
        {
            throw new ArgumentException("Objects subdirectory must not be empty.", nameof(ObjectsSubdirectory));
        }

        string normalized = ObjectsSubdirectory.Replace('\\', '/').Trim('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Objects subdirectory must not contain parent traversal.", nameof(ObjectsSubdirectory));
        }

        return this with { ObjectsSubdirectory = normalized };
    }
}
