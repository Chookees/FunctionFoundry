using System.Text;

namespace FunctionFoundry.Text;

/// <summary>
/// Options for <see cref="HomoglyphNormalizer"/>.
/// </summary>
/// <param name="MaximumInputChars">Maximum accepted input length.</param>
public sealed record HomoglyphNormalizerOptions(int MaximumInputChars = 64 * 1024)
{
    /// <summary>
    /// Validates options.
    /// </summary>
    public HomoglyphNormalizerOptions Validate()
    {
        if (MaximumInputChars <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumInputChars), "Maximum input chars must be positive.");
        }

        return this;
    }
}

/// <summary>
/// Result of homoglyph skeleton normalization.
/// </summary>
/// <param name="Skeleton">NFC text after confusable folding.</param>
/// <param name="Substitutions">Number of code points replaced via the confusables map.</param>
public sealed record HomoglyphNormalizationResult(string Skeleton, int Substitutions);

/// <summary>
/// Folds known confusable code points to ASCII skeleton forms for comparison.
/// </summary>
/// <remarks>
/// <para>Uses the compact built-in <see cref="ConfusablesData"/> subset (not the full UCD confusables table).</para>
/// <para>Does not claim intent detection; hosts should treat output as a comparison aid.</para>
/// </remarks>
public sealed class HomoglyphNormalizer
{
    private readonly HomoglyphNormalizerOptions _options;

    /// <summary>
    /// Initializes a new normalizer.
    /// </summary>
    /// <param name="options">Optional limits.</param>
    public HomoglyphNormalizer(HomoglyphNormalizerOptions? options = null)
    {
        _options = (options ?? new HomoglyphNormalizerOptions()).Validate();
    }

    /// <summary>
    /// Normalizes <paramref name="input"/> to an NFC skeleton string.
    /// </summary>
    /// <param name="input">Input text.</param>
    /// <returns>Skeleton text and substitution count.</returns>
    public HomoglyphNormalizationResult NormalizeToSkeleton(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length > _options.MaximumInputChars)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Input exceeds configured maximum length.");
        }

        string nfc = input.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(nfc.Length);
        int substitutions = 0;
        for (int i = 0; i < nfc.Length;)
        {
            int codePoint = char.ConvertToUtf32(nfc, i);
            int width = char.IsSurrogatePair(nfc, i) ? 2 : 1;
            if (ConfusablesData.TryMapCodePoint(codePoint, out int mapped))
            {
                builder.Append(char.ConvertFromUtf32(mapped));
                substitutions++;
            }
            else
            {
                builder.Append(nfc, i, width);
            }

            i += width;
        }

        string skeleton = builder.ToString().Normalize(NormalizationForm.FormC);
        return new HomoglyphNormalizationResult(skeleton, substitutions);
    }
}
