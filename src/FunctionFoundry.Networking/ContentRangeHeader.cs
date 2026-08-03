using System.Globalization;
using System.Text.RegularExpressions;

namespace FunctionFoundry.Networking;

/// <summary>
/// Parsed HTTP <c>Content-Range</c> header values for byte ranges.
/// </summary>
/// <param name="Start">Inclusive start offset when present.</param>
/// <param name="End">Inclusive end offset when present.</param>
/// <param name="TotalLength">Total object length when known; <see langword="null"/> when reported as <c>*</c>.</param>
/// <param name="IsLengthUnknown">Whether the total length was reported as <c>*</c>.</param>
public sealed record ContentRangeInfo(long? Start, long? End, long? TotalLength, bool IsLengthUnknown);

/// <summary>
/// Parses and formats HTTP <c>Content-Range</c> byte-range headers.
/// </summary>
public static partial class ContentRangeHeader
{
    /// <summary>
    /// Attempts to parse a <c>Content-Range</c> header value.
    /// </summary>
    /// <param name="headerValue">Header value such as <c>bytes 0-99/1234</c>.</param>
    /// <param name="info">Parsed info when successful.</param>
    /// <returns><see langword="true"/> when parsing succeeds.</returns>
    public static bool TryParse(string headerValue, out ContentRangeInfo info)
    {
        info = default!;
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        Match match = ContentRangeRegex().Match(headerValue.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (match.Groups["star"].Success)
        {
            if (!TryParseTotal(match.Groups["total"].Value, out long? total, out bool unknown))
            {
                return false;
            }

            info = new ContentRangeInfo(null, null, total, unknown);
            return true;
        }

        if (!long.TryParse(match.Groups["start"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long start)
            || !long.TryParse(match.Groups["end"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long end)
            || start < 0
            || end < start)
        {
            return false;
        }

        if (!TryParseTotal(match.Groups["total"].Value, out long? totalLength, out bool lengthUnknown))
        {
            return false;
        }

        if (totalLength is long known && end >= known)
        {
            return false;
        }

        info = new ContentRangeInfo(start, end, totalLength, lengthUnknown);
        return true;
    }

    /// <summary>
    /// Formats a byte content-range header value.
    /// </summary>
    public static string Format(long start, long end, long? totalLength)
    {
        if (start < 0 || end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "Range bounds are invalid.");
        }

        string total = totalLength is null ? "*" : totalLength.Value.ToString(CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"bytes {start}-{end}/{total}");
    }

    private static bool TryParseTotal(string text, out long? total, out bool unknown)
    {
        unknown = false;
        total = null;
        if (text == "*")
        {
            unknown = true;
            return true;
        }

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value < 0)
        {
            return false;
        }

        total = value;
        return true;
    }

    [GeneratedRegex(@"^bytes\s+(?:(?<star>\*)|(?<start>\d+)-(?<end>\d+))/(?<total>\d+|\*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ContentRangeRegex();
}
