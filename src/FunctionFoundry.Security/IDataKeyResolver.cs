namespace FunctionFoundry.Security;

/// <summary>
/// Resolves cryptographic key material by key identifier.
/// </summary>
public interface IDataKeyResolver
{
    /// <summary>
    /// Attempts to resolve a data-encryption key for the specified identifier.
    /// </summary>
    /// <param name="keyId">The key identifier embedded in a ciphertext envelope. Must not be null.</param>
    /// <param name="key">When this method returns <see langword="true"/>, receives a copy of the key bytes owned by the caller.</param>
    /// <returns><see langword="true"/> when the key was found; otherwise <see langword="false"/>.</returns>
    bool TryResolve(string keyId, out byte[] key);
}
