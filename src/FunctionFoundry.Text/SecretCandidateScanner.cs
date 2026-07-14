using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FunctionFoundry.Text;

/// <summary>
/// Secret finding category.
/// </summary>
public enum SecretCandidateKind
{
    /// <summary>High-entropy token.</summary>
    HighEntropyToken,

    /// <summary>Known pattern match (for example API key prefix).</summary>
    PatternMatch,
}

/// <summary>
/// A secret candidate finding with redacted preview by default.
/// </summary>
/// <param name="Kind">Finding kind.</param>
/// <param name="Line">One-based line number.</param>
/// <param name="Column">One-based column number.</param>
/// <param name="Confidence">Confidence score in [0,1].</param>
/// <param name="RedactedPreview">Redacted token preview.</param>
/// <param name="PatternName">Pattern name when <see cref="Kind"/> is <see cref="SecretCandidateKind.PatternMatch"/>.</param>
public sealed record SecretCandidateFinding(
    SecretCandidateKind Kind,
    int Line,
    int Column,
    double Confidence,
    string RedactedPreview,
    string? PatternName = null);

/// <summary>
/// Options for <see cref="SecretCandidateScanner"/>.
/// </summary>
/// <param name="MinimumEntropyBits">Minimum Shannon entropy for high-entropy tokens.</param>
/// <param name="MinimumTokenLength">Minimum token length to consider.</param>
/// <param name="MaximumTokenLength">Maximum token length to consider.</param>
/// <param name="MaximumInputChars">Maximum total input characters per scan.</param>
/// <param name="IncludePatternMatches">When true, evaluates built-in patterns.</param>
public sealed record SecretCandidateScannerOptions(
    double MinimumEntropyBits = 3.5,
    int MinimumTokenLength = 16,
    int MaximumTokenLength = 256,
    int MaximumInputChars = 1024 * 1024,
    bool IncludePatternMatches = true)
{
    /// <summary>
    /// Validates options.
    /// </summary>
    public SecretCandidateScannerOptions Validate()
    {
        if (MinimumEntropyBits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumEntropyBits), "Minimum entropy must be non-negative.");
        }

        if (MinimumTokenLength <= 0 || MaximumTokenLength < MinimumTokenLength)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumTokenLength), "Token length bounds are invalid.");
        }

        if (MaximumInputChars <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumInputChars), "Maximum input chars must be positive.");
        }

        return this;
    }
}

/// <summary>
/// Scans text for secret candidates using entropy and pattern heuristics.
/// </summary>
public sealed class SecretCandidateScanner
{
    private static readonly (string Name, Regex Pattern, double Confidence)[] BuiltInPatterns =
    [
        ("aws_access_key", new Regex(@"AKIA[0-9A-Z]{16}", RegexOptions.CultureInvariant), 0.9),
        ("github_pat", new Regex(@"ghp_[A-Za-z0-9_]{20,}", RegexOptions.CultureInvariant), 0.85),
        ("generic_api_key", new Regex(@"(?i)(api[_-]?key|secret|token)\s*[:=]\s*['""]?([A-Za-z0-9_\-]{16,})", RegexOptions.CultureInvariant), 0.75),
    ];

    private readonly SecretCandidateScannerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretCandidateScanner"/> class.
    /// </summary>
    public SecretCandidateScanner(SecretCandidateScannerOptions? options = null)
    {
        _options = (options ?? new SecretCandidateScannerOptions()).Validate();
    }

    /// <summary>
    /// Scans a complete string.
    /// </summary>
    public IReadOnlyList<SecretCandidateFinding> Scan(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length > _options.MaximumInputChars)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Input exceeds configured maximum length.");
        }

        var findings = new List<SecretCandidateFinding>();
        ScanLines(input, findings);
        return findings
            .OrderBy(static f => f.Line)
            .ThenBy(static f => f.Column)
            .ThenBy(static f => f.Kind)
            .ToArray();
    }

    /// <summary>
    /// Scans a text stream line-by-line with bounded memory.
    /// </summary>
    public async Task<IReadOnlyList<SecretCandidateFinding>> ScanAsync(Stream input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        using StreamReader reader = new(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var findings = new List<SecretCandidateFinding>();
        int lineNumber = 0;
        int totalChars = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            lineNumber++;
            totalChars += line.Length;
            if (totalChars > _options.MaximumInputChars)
            {
                throw new ArgumentOutOfRangeException(nameof(input), "Input exceeds configured maximum length.");
            }

            ScanLine(line, lineNumber, findings);
        }

        return findings
            .OrderBy(static f => f.Line)
            .ThenBy(static f => f.Column)
            .ThenBy(static f => f.Kind)
            .ToArray();
    }

    private void ScanLines(string input, List<SecretCandidateFinding> findings)
    {
        int lineNumber = 0;
        foreach (string line in input.Split('\n'))
        {
            lineNumber++;
            string normalized = line.TrimEnd('\r');
            ScanLine(normalized, lineNumber, findings);
        }
    }

    private void ScanLine(string line, int lineNumber, List<SecretCandidateFinding> findings)
    {
        if (_options.IncludePatternMatches)
        {
            foreach ((string name, Regex pattern, double confidence) in BuiltInPatterns)
            {
                foreach (Match match in pattern.Matches(line))
                {
                    string token = match.Groups.Count > 2 ? match.Groups[2].Value : match.Value;
                    if (IsLikelyFalsePositive(token))
                    {
                        continue;
                    }

                    findings.Add(new SecretCandidateFinding(
                        SecretCandidateKind.PatternMatch,
                        lineNumber,
                        match.Index + 1,
                        confidence,
                        Redact(token),
                        name));
                }
            }
        }

        foreach (Match match in Regex.Matches(line, @"[A-Za-z0-9_\-+/=]{8,}", RegexOptions.CultureInvariant))
        {
            string token = match.Value;
            if (token.Length < _options.MinimumTokenLength || token.Length > _options.MaximumTokenLength)
            {
                continue;
            }

            double entropy = ComputeShannonEntropy(token);
            if (entropy < _options.MinimumEntropyBits)
            {
                continue;
            }

            if (IsLikelyFalsePositive(token))
            {
                continue;
            }

            findings.Add(new SecretCandidateFinding(
                SecretCandidateKind.HighEntropyToken,
                lineNumber,
                match.Index + 1,
                Math.Min(1.0, entropy / 6.0),
                Redact(token)));
        }
    }

    private static bool IsLikelyFalsePositive(string token)
    {
        if (token.All(c => c == token[0]))
        {
            return true;
        }

        if (token.Contains("example", StringComparison.OrdinalIgnoreCase)
            || token.Contains("dummy", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static double ComputeShannonEntropy(string token)
    {
        Span<int> counts = stackalloc int[256];
        foreach (char ch in token)
        {
            if (ch < 256)
            {
                counts[ch]++;
            }
        }

        double entropy = 0;
        double length = token.Length;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            double probability = counts[i] / length;
            entropy -= probability * Math.Log(probability, 2);
        }

        return entropy;
    }

    private static string Redact(string token)
    {
        if (token.Length <= 4)
        {
            return "****";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{token[..2]}…{token[^2..]} ({token.Length} chars)");
    }
}
