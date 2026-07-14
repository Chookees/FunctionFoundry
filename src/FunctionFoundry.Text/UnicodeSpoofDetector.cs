using System.Globalization;
using System.Text;

namespace FunctionFoundry.Text;

/// <summary>
/// Risk category for Unicode spoof findings.
/// </summary>
public enum UnicodeSpoofRiskKind
{
    /// <summary>Mixed scripts in a single token.</summary>
    MixedScript,

    /// <summary>Confusable characters relative to the built-in skeleton map.</summary>
    ConfusableCharacter,

    /// <summary>Invisible or format-control characters.</summary>
    InvisibleOrControl,

    /// <summary>Normalization form differs from canonical NFC.</summary>
    NonCanonicalNormalization,
}

/// <summary>
/// A single explainable spoof risk finding.
/// </summary>
/// <param name="Kind">Risk category.</param>
/// <param name="CodePoint">Affected Unicode code point.</param>
/// <param name="Offset">Zero-based UTF-16 offset in the input string.</param>
/// <param name="Explanation">Human-readable explanation without intent claims.</param>
public sealed record UnicodeSpoofFinding(
    UnicodeSpoofRiskKind Kind,
    int CodePoint,
    int Offset,
    string Explanation);

/// <summary>
/// Options for <see cref="UnicodeSpoofDetector"/>.
/// </summary>
/// <param name="MaximumInputChars">Maximum input length. Must be positive.</param>
/// <param name="CheckNormalization">When true, reports non-NFC text.</param>
public sealed record UnicodeSpoofDetectorOptions(
    int MaximumInputChars = 64 * 1024,
    bool CheckNormalization = true)
{
    /// <summary>
    /// Validates options.
    /// </summary>
    public UnicodeSpoofDetectorOptions Validate()
    {
        if (MaximumInputChars <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumInputChars), "Maximum input chars must be positive.");
        }

        return this;
    }
}

/// <summary>
/// Result of Unicode spoof analysis.
/// </summary>
/// <param name="Findings">Deterministic findings ordered by offset, kind, code point.</param>
/// <param name="Skeleton">Confusable skeleton using built-in map and lowercase ASCII folding.</param>
/// <param name="DataVersion">Confusables dataset version used.</param>
public sealed record UnicodeSpoofAnalysisResult(
    IReadOnlyList<UnicodeSpoofFinding> Findings,
    string Skeleton,
    string DataVersion);

/// <summary>
/// Detects observable Unicode spoofing risks using normalization, confusables, and script analysis.
/// </summary>
/// <remarks>
/// <para>Does not claim malicious intent. Reports structural properties only.</para>
/// </remarks>
public sealed class UnicodeSpoofDetector
{
    private readonly UnicodeSpoofDetectorOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnicodeSpoofDetector"/> class.
    /// </summary>
    public UnicodeSpoofDetector(UnicodeSpoofDetectorOptions? options = null)
    {
        _options = (options ?? new UnicodeSpoofDetectorOptions()).Validate();
    }

    /// <summary>
    /// Analyzes <paramref name="input"/> for spoofing risks.
    /// </summary>
    public UnicodeSpoofAnalysisResult Analyze(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length > _options.MaximumInputChars)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Input exceeds configured maximum length.");
        }

        var findings = new List<UnicodeSpoofFinding>();
        if (_options.CheckNormalization && input != input.Normalize(NormalizationForm.FormC))
        {
            findings.Add(new UnicodeSpoofFinding(
                UnicodeSpoofRiskKind.NonCanonicalNormalization,
                -1,
                0,
                "Text is not in Unicode normalization form C (NFC)."));
        }

        var skeleton = new StringBuilder(input.Length);
        int offset = 0;
        UnicodeCategory? scriptCategory = null;
        bool mixedScript = false;
        foreach (Rune rune in input.EnumerateRunes())
        {
            if (IsInvisibleOrControl(rune))
            {
                findings.Add(new UnicodeSpoofFinding(
                    UnicodeSpoofRiskKind.InvisibleOrControl,
                    rune.Value,
                    offset,
                    string.Create(CultureInfo.InvariantCulture, $"Code point U+{rune.Value:X4} is an invisible or format-control character.")));
            }

            if (ConfusablesData.TryMapCodePoint(rune.Value, out int mapped) && mapped != rune.Value)
            {
                findings.Add(new UnicodeSpoofFinding(
                    UnicodeSpoofRiskKind.ConfusableCharacter,
                    rune.Value,
                    offset,
                    string.Create(CultureInfo.InvariantCulture, $"Code point U+{rune.Value:X4} maps to skeleton U+{mapped:X4} in {ConfusablesData.DataVersion}.")));
                skeleton.Append(char.ToUpperInvariant((char)mapped));
            }
            else
            {
                skeleton.Append(rune.ToString().ToUpperInvariant());
            }

            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (IsScriptCategory(category))
            {
                if (scriptCategory is not null && scriptCategory != category)
                {
                    mixedScript = true;
                }

                scriptCategory ??= category;
            }

            offset += rune.Utf16SequenceLength;
        }

        if (mixedScript)
        {
            findings.Add(new UnicodeSpoofFinding(
                UnicodeSpoofRiskKind.MixedScript,
                -1,
                0,
                "Token mixes multiple script categories."));
        }

        UnicodeSpoofFinding[] ordered = findings
            .OrderBy(static f => f.Offset)
            .ThenBy(static f => f.Kind)
            .ThenBy(static f => f.CodePoint)
            .ToArray();

        return new UnicodeSpoofAnalysisResult(ordered, skeleton.ToString(), ConfusablesData.DataVersion);
    }

    private static bool IsInvisibleOrControl(Rune rune) =>
        rune.Value is 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format;

    private static bool IsScriptCategory(UnicodeCategory category) =>
        category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.OtherLetter;
}
