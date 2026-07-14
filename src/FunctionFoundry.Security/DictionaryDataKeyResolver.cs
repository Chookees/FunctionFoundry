namespace FunctionFoundry.Security;

/// <summary>
/// In-memory key resolver suitable for tests and carefully controlled hosts.
/// </summary>
public sealed class DictionaryDataKeyResolver : IDataKeyResolver
{
    private readonly Dictionary<string, byte[]> _keys;

    /// <summary>
    /// Initializes a new instance of the <see cref="DictionaryDataKeyResolver"/> class.
    /// </summary>
    /// <param name="keys">Map of key identifier to raw key bytes. Keys are copied.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keys"/> is null.</exception>
    public DictionaryDataKeyResolver(IReadOnlyDictionary<string, byte[]> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, byte[]> pair in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            _keys[pair.Key] = pair.Value.ToArray();
        }
    }

    /// <inheritdoc />
    public bool TryResolve(string keyId, out byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (_keys.TryGetValue(keyId, out byte[]? stored))
        {
            key = stored.ToArray();
            return true;
        }

        key = [];
        return false;
    }
}
