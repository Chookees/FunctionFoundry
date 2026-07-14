using System.Text.RegularExpressions;

namespace FunctionFoundry.Observability;

/// <summary>
/// Describes how property names are evaluated for sensitive-data redaction.
/// </summary>
/// <param name="Names">Property names or fragments to match. Must not be null or empty.</param>
/// <param name="MatchMode">Matching mode applied to each name. Defaults to <see cref="PropertyNameMatchMode.Contains"/>.</param>
/// <param name="Strategy">Replacement strategy when a name matches. Defaults to <see cref="RedactionReplacementStrategy.Redacted"/>.</param>
public sealed record SensitivePropertyNamePolicy(
    IReadOnlyCollection<string> Names,
    PropertyNameMatchMode MatchMode = PropertyNameMatchMode.Contains,
    RedactionReplacementStrategy Strategy = RedactionReplacementStrategy.Redacted)
{
    /// <summary>
    /// Gets the default property-name policy covering common secret field names.
    /// </summary>
    public static SensitivePropertyNamePolicy Default { get; } = new(
        [
            "password",
            "passwd",
            "secret",
            "token",
            "apikey",
            "api_key",
            "authorization",
            "auth",
            "credential",
            "private_key",
            "access_key",
            "refresh_token",
            "session",
            "ssn",
            "credit_card",
            "card_number",
        ],
        PropertyNameMatchMode.Contains,
        RedactionReplacementStrategy.Redacted);

    /// <summary>
    /// Determines whether <paramref name="propertyName"/> matches this policy.
    /// </summary>
    /// <param name="propertyName">Property or dictionary key name.</param>
    /// <returns><see langword="true"/> when the name matches; otherwise <see langword="false"/>.</returns>
    public bool Matches(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        foreach (string name in Names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (MatchMode switch
            {
                PropertyNameMatchMode.Exact => propertyName.Equals(name, StringComparison.OrdinalIgnoreCase),
                PropertyNameMatchMode.Contains => propertyName.Contains(name, StringComparison.OrdinalIgnoreCase),
                PropertyNameMatchMode.Suffix => propertyName.EndsWith(name, StringComparison.OrdinalIgnoreCase),
                _ => false,
            })
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A regex-based value pattern that triggers redaction when matched.
/// </summary>
/// <param name="Pattern">Regular expression evaluated with a one-second timeout. Must not be null or empty.</param>
/// <param name="Strategy">Replacement strategy when the pattern matches. Defaults to <see cref="RedactionReplacementStrategy.Redacted"/>.</param>
/// <param name="Options">Regex options. <see cref="RegexOptions.Compiled"/> is added automatically.</param>
public sealed record SensitivePatternRule(
    string Pattern,
    RedactionReplacementStrategy Strategy = RedactionReplacementStrategy.Redacted,
    RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets default pattern rules for common secret-bearing value shapes.
    /// </summary>
    public static IReadOnlyList<SensitivePatternRule> DefaultRules { get; } =
    [
        new(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RedactionReplacementStrategy.Redacted),
        new(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RedactionReplacementStrategy.Hashed),
        new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RedactionReplacementStrategy.Masked),
        new(@"\b(?:\d[ -]*?){13,16}\b", RedactionReplacementStrategy.Redacted),
        new(@"\bAKIA[0-9A-Z]{16}\b", RedactionReplacementStrategy.Hashed),
    ];

    internal Regex Compiled { get; } = new(
        Pattern,
        Options | RegexOptions.Compiled,
        MatchTimeout);

    /// <summary>
    /// Determines whether <paramref name="value"/> matches this rule.
    /// </summary>
    /// <param name="value">Candidate string value.</param>
    /// <returns><see langword="true"/> when the pattern matches; otherwise <see langword="false"/>.</returns>
    public bool IsMatch(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Compiled.IsMatch(value);
    }
}

/// <summary>
/// Detects high-entropy string candidates that likely represent secrets.
/// </summary>
public sealed class EntropySecretDetector
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntropySecretDetector"/> class.
    /// </summary>
    /// <param name="entropyThreshold">
    /// Minimum Shannon entropy (bits per character) required for detection. Typical secrets use 3.5 or higher.
    /// </param>
    /// <param name="minimumLength">Minimum string length considered. Defaults to 16.</param>
    public EntropySecretDetector(double entropyThreshold = 3.5, int minimumLength = 16)
    {
        if (entropyThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entropyThreshold), "Entropy threshold must be positive.");
        }

        if (minimumLength < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLength), "Minimum length must be at least 8.");
        }

        EntropyThreshold = entropyThreshold;
        MinimumLength = minimumLength;
    }

    /// <summary>
    /// Gets the entropy threshold in bits per character.
    /// </summary>
    public double EntropyThreshold { get; }

    /// <summary>
    /// Gets the minimum candidate string length.
    /// </summary>
    public int MinimumLength { get; }

    /// <summary>
    /// Determines whether <paramref name="value"/> is a likely secret based on entropy and charset shape.
    /// </summary>
    /// <param name="value">Candidate string.</param>
    /// <returns><see langword="true"/> when the value appears secret-like; otherwise <see langword="false"/>.</returns>
    public bool IsLikelySecret(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length < MinimumLength)
        {
            return false;
        }

        if (!LooksLikeSecretAlphabet(value))
        {
            return false;
        }

        return ComputeShannonEntropyBitsPerChar(value) >= EntropyThreshold;
    }

    private static bool LooksLikeSecretAlphabet(string value)
    {
        int classified = 0;
        bool hasLower = false;
        bool hasUpper = false;
        bool hasDigit = false;
        bool hasSymbol = false;
        foreach (char ch in value)
        {
            if (char.IsAsciiLetterLower(ch))
            {
                hasLower = true;
                classified++;
            }
            else if (char.IsAsciiLetterUpper(ch))
            {
                hasUpper = true;
                classified++;
            }
            else if (char.IsAsciiDigit(ch))
            {
                hasDigit = true;
                classified++;
            }
            else if (ch is '+' or '/' or '=' or '-' or '_' or '.')
            {
                hasSymbol = true;
                classified++;
            }
            else
            {
                return false;
            }
        }

        if (classified != value.Length)
        {
            return false;
        }

        int kinds = (hasLower ? 1 : 0) + (hasUpper ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSymbol ? 1 : 0);
        return kinds >= 2;
    }

    private static double ComputeShannonEntropyBitsPerChar(string value)
    {
        Span<int> counts = stackalloc int[256];
        int total = 0;
        foreach (char ch in value)
        {
            if (ch > 255)
            {
                return 0;
            }

            counts[ch]++;
            total++;
        }

        double entropy = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            double probability = counts[i] / (double)total;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }
}
