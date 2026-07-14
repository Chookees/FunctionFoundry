using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FunctionFoundry.Text;

/// <summary>
/// Options for <see cref="NearDuplicateTextIndex"/>.
/// </summary>
/// <param name="ShingleSize">Word-shingle width. Must be positive.</param>
/// <param name="HashCount">Number of MinHash signatures. Must be positive.</param>
/// <param name="BandCount">LSH band count. Must divide <paramref name="HashCount"/> evenly.</param>
/// <param name="Seed">Deterministic seed for hash coefficients.</param>
/// <param name="SimilarityThreshold">Jaccard threshold for candidate generation in [0,1].</param>
/// <param name="MaximumDocumentChars">Maximum characters per document.</param>
public sealed record NearDuplicateTextIndexOptions(
    int ShingleSize = 3,
    int HashCount = 32,
    int BandCount = 16,
    int Seed = 0x46_46_54_58,
    double SimilarityThreshold = 0.6,
    int MaximumDocumentChars = 64 * 1024)
{
    /// <summary>
    /// Validates options.
    /// </summary>
    public NearDuplicateTextIndexOptions Validate()
    {
        if (ShingleSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ShingleSize), "Shingle size must be positive.");
        }

        if (HashCount <= 0 || BandCount <= 0 || HashCount % BandCount != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(HashCount), "HashCount must be a positive multiple of BandCount.");
        }

        if (SimilarityThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(SimilarityThreshold), "Similarity threshold must be in [0,1].");
        }

        if (MaximumDocumentChars <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumDocumentChars), "Maximum document chars must be positive.");
        }

        return this;
    }
}

/// <summary>
/// A near-duplicate candidate pair with verified Jaccard estimate.
/// </summary>
/// <param name="DocumentIdA">First document identifier.</param>
/// <param name="DocumentIdB">Second document identifier.</param>
/// <param name="EstimatedSimilarity">Estimated Jaccard similarity after verification.</param>
public sealed record NearDuplicateCandidate(string DocumentIdA, string DocumentIdB, double EstimatedSimilarity);

/// <summary>
/// Incremental MinHash LSH index for near-duplicate text detection.
/// </summary>
/// <remarks>
/// <para>Memory scales with O(documents * hashCount + buckets). Accuracy improves with higher <see cref="NearDuplicateTextIndexOptions.HashCount"/>.</para>
/// <para>Uses word shingles over lowercase invariant words.</para>
/// </remarks>
public sealed class NearDuplicateTextIndex
{
    private readonly NearDuplicateTextIndexOptions _options;
    private readonly uint[] _coefficients;
    private readonly Dictionary<string, uint[]> _signatures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _buckets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _shingles = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="NearDuplicateTextIndex"/> class.
    /// </summary>
    public NearDuplicateTextIndex(NearDuplicateTextIndexOptions? options = null)
    {
        _options = (options ?? new NearDuplicateTextIndexOptions()).Validate();
        _coefficients = CreateCoefficients(_options.HashCount, _options.Seed);
    }

    /// <summary>
    /// Gets the number of indexed documents.
    /// </summary>
    public int DocumentCount => _signatures.Count;

    /// <summary>
    /// Adds or replaces a document in the index.
    /// </summary>
    public void Upsert(string documentId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > _options.MaximumDocumentChars)
        {
            throw new ArgumentOutOfRangeException(nameof(text), "Document exceeds configured maximum length.");
        }

        if (_signatures.ContainsKey(documentId))
        {
            Remove(documentId);
        }

        HashSet<string> shingleSet = BuildShingles(text);
        uint[] signature = ComputeSignature(shingleSet);
        _signatures[documentId] = signature;
        _shingles[documentId] = shingleSet;
        AddToBuckets(documentId, signature);
    }

    /// <summary>
    /// Removes a document from the index.
    /// </summary>
    public bool Remove(string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (!_signatures.TryGetValue(documentId, out uint[]? signature))
        {
            return false;
        }

        RemoveFromBuckets(documentId, signature);
        _signatures.Remove(documentId);
        _shingles.Remove(documentId);
        return true;
    }

    /// <summary>
    /// Queries for near-duplicate candidates for <paramref name="documentId"/>.
    /// </summary>
    public IReadOnlyList<NearDuplicateCandidate> Query(string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (!_signatures.TryGetValue(documentId, out uint[]? signature) || !_shingles.TryGetValue(documentId, out HashSet<string>? sourceShingles))
        {
            throw new KeyNotFoundException($"Document '{documentId}' is not indexed.");
        }

        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        int rowsPerBand = _options.HashCount / _options.BandCount;
        for (int band = 0; band < _options.BandCount; band++)
        {
            string bucketKey = CreateBucketKey(band, signature.AsSpan(band * rowsPerBand, rowsPerBand));
            if (_buckets.TryGetValue(bucketKey, out HashSet<string>? members))
            {
                foreach (string member in members)
                {
                    if (!string.Equals(member, documentId, StringComparison.Ordinal))
                    {
                        candidateIds.Add(member);
                    }
                }
            }
        }

        var results = new List<NearDuplicateCandidate>();
        foreach (string candidateId in candidateIds.OrderBy(static id => id, StringComparer.Ordinal))
        {
            if (!_shingles.TryGetValue(candidateId, out HashSet<string>? candidateShingles))
            {
                continue;
            }

            double similarity = EstimateJaccard(sourceShingles, candidateShingles);
            if (similarity >= _options.SimilarityThreshold)
            {
                string a = string.Compare(documentId, candidateId, StringComparison.Ordinal) < 0 ? documentId : candidateId;
                string b = string.Equals(a, documentId, StringComparison.Ordinal) ? candidateId : documentId;
                results.Add(new NearDuplicateCandidate(a, b, similarity));
            }
        }

        return results
            .OrderByDescending(static r => r.EstimatedSimilarity)
            .ThenBy(static r => r.DocumentIdA, StringComparer.Ordinal)
            .ThenBy(static r => r.DocumentIdB, StringComparer.Ordinal)
            .ToArray();
    }

    private HashSet<string> BuildShingles(string text)
    {
        string[] words = RegexSplitWords(text);
        var shingles = new HashSet<string>(StringComparer.Ordinal);
        if (words.Length < _options.ShingleSize)
        {
            return shingles;
        }

        for (int i = 0; i <= words.Length - _options.ShingleSize; i++)
        {
            string shingle = string.Join(' ', words.AsSpan(i, _options.ShingleSize));
            shingles.Add(shingle);
        }

        return shingles;
    }

    private static string[] RegexSplitWords(string text) =>
        text.ToUpperInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private uint[] ComputeSignature(HashSet<string> shingles)
    {
        uint[] signature = new uint[_options.HashCount];
        Array.Fill(signature, uint.MaxValue);
        foreach (string shingle in shingles)
        {
            uint hash = Hash32(shingle);
            for (int i = 0; i < _options.HashCount; i++)
            {
                uint combined = unchecked((uint)((hash * _coefficients[i]) ^ (hash >> (i % 16))));
                if (combined < signature[i])
                {
                    signature[i] = combined;
                }
            }
        }

        return signature;
    }

    private void AddToBuckets(string documentId, uint[] signature)
    {
        int rowsPerBand = _options.HashCount / _options.BandCount;
        for (int band = 0; band < _options.BandCount; band++)
        {
            string bucketKey = CreateBucketKey(band, signature.AsSpan(band * rowsPerBand, rowsPerBand));
            if (!_buckets.TryGetValue(bucketKey, out HashSet<string>? members))
            {
                members = new HashSet<string>(StringComparer.Ordinal);
                _buckets[bucketKey] = members;
            }

            members.Add(documentId);
        }
    }

    private void RemoveFromBuckets(string documentId, uint[] signature)
    {
        int rowsPerBand = _options.HashCount / _options.BandCount;
        for (int band = 0; band < _options.BandCount; band++)
        {
            string bucketKey = CreateBucketKey(band, signature.AsSpan(band * rowsPerBand, rowsPerBand));
            if (_buckets.TryGetValue(bucketKey, out HashSet<string>? members))
            {
                members.Remove(documentId);
                if (members.Count == 0)
                {
                    _buckets.Remove(bucketKey);
                }
            }
        }
    }

    private static string CreateBucketKey(int band, ReadOnlySpan<uint> rows) =>
        string.Create(CultureInfo.InvariantCulture, $"{band}:{string.Join(',', rows.ToArray())}");

    private static double EstimateJaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
        {
            return 1.0;
        }

        int intersection = 0;
        foreach (string item in a)
        {
            if (b.Contains(item))
            {
                intersection++;
            }
        }

        int union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static uint[] CreateCoefficients(int count, int seed)
    {
        uint[] coefficients = new uint[count];
        Span<byte> buffer = stackalloc byte[4];
        for (int i = 0; i < count; i++)
        {
            int mixed = HashCode.Combine(seed, i);
            BinaryPrimitives.WriteInt32LittleEndian(buffer, mixed);
            coefficients[i] = BitConverter.ToUInt32(buffer) | 1u;
        }

        return coefficients;
    }

    private static uint Hash32(string value)
    {
        Span<byte> bytes = stackalloc byte[Encoding.UTF8.GetByteCount(value)];
        Encoding.UTF8.GetBytes(value, bytes);
        Span<byte> hash = stackalloc byte[32];
        SHA256.TryHashData(bytes, hash, out _);
        return BitConverter.ToUInt32(hash);
    }
}
