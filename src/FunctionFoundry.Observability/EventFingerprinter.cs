using System.Text;
using System.Text.RegularExpressions;

namespace FunctionFoundry.Observability;

/// <summary>
/// A named contribution to an <see cref="EventFingerprint"/>.
/// </summary>
/// <param name="Name">Component name, such as <c>event.type</c> or <c>exception.stack</c>.</param>
/// <param name="NormalizedValue">Normalized text included in the fingerprint hash.</param>
public sealed record FingerprintComponent(string Name, string NormalizedValue);

/// <summary>
/// Stable fingerprint derived from normalized event data.
/// </summary>
/// <param name="Hash">Lowercase hexadecimal SHA-256 digest.</param>
/// <param name="Components">Explainable normalized components that contributed to the hash.</param>
public sealed record EventFingerprint(string Hash, IReadOnlyList<FingerprintComponent> Components);

/// <summary>
/// Input payload used to compute an <see cref="EventFingerprint"/>.
/// </summary>
/// <param name="EventType">Logical event type or category.</param>
/// <param name="Message">Human-readable message. May contain volatile tokens that will be stripped.</param>
/// <param name="Exception">Optional exception whose stack trace is normalized.</param>
/// <param name="Properties">Optional structured properties included in the fingerprint.</param>
public sealed record EventFingerprintInput(
    string EventType,
    string Message,
    Exception? Exception = null,
    IReadOnlyDictionary<string, string>? Properties = null);

/// <summary>
/// Options controlling event fingerprint normalization.
/// </summary>
public sealed class EventFingerprinterOptions
{
    /// <summary>
    /// Gets or sets whether file paths in stack traces are reduced to file names only. Defaults to <see langword="true"/>.
    /// </summary>
    public bool NormalizeStackFilePaths { get; set; } = true;

    /// <summary>
    /// Gets or sets whether line numbers are removed from stack frames. Defaults to <see langword="true"/>.
    /// </summary>
    public bool RemoveStackLineNumbers { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of inner exceptions to include. Defaults to 8.
    /// </summary>
    public int MaxInnerExceptionDepth { get; set; } = 8;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when limits are invalid.</exception>
    public void Validate()
    {
        if (MaxInnerExceptionDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxInnerExceptionDepth), "MaxInnerExceptionDepth cannot be negative.");
        }
    }
}

/// <summary>
/// Produces stable fingerprints from events by normalizing volatile values while preserving causal exception structure.
/// </summary>
/// <remarks>
/// <para>Volatile tokens such as GUIDs, timestamps, memory addresses, and correlation identifiers are removed or canonicalized.</para>
/// <para>Identical normalized inputs always produce identical <see cref="EventFingerprint.Hash"/> values.</para>
/// <para>Thread safety: instances are immutable after construction and safe for concurrent use.</para>
/// </remarks>
public sealed class EventFingerprinter
{
    private static readonly Regex GuidRegex = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex IsoTimestampRegex = new(
        @"\b\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex UnixTimestampRegex = new(
        @"\b\d{10,13}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex MemoryAddressRegex = new(
        @"\b0x[0-9a-fA-F]+\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex CorrelationTokenRegex = new(
        @"\b(?:cid|trace|span|req|request|correlation)[=:][A-Za-z0-9._-]+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly EventFingerprinterOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventFingerprinter"/> class.
    /// </summary>
    /// <param name="options">Fingerprinting options. When null, defaults are used.</param>
    public EventFingerprinter(EventFingerprinterOptions? options = null)
    {
        _options = options ?? new EventFingerprinterOptions();
        _options.Validate();
    }

    /// <summary>
    /// Computes a stable fingerprint for <paramref name="input"/>.
    /// </summary>
    /// <param name="input">Event payload. Must not be null.</param>
    /// <returns>An <see cref="EventFingerprint"/> with hash and explainable components.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
    public EventFingerprint Fingerprint(EventFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.EventType);
        ArgumentNullException.ThrowIfNull(input.Message);

        var components = new List<FingerprintComponent>(capacity: 8);
        string normalizedType = NormalizeScalar(input.EventType);
        components.Add(new FingerprintComponent("event.type", normalizedType));

        string normalizedMessage = NormalizeVolatileText(input.Message);
        components.Add(new FingerprintComponent("event.message", normalizedMessage));

        if (input.Properties is not null)
        {
            foreach (KeyValuePair<string, string> pair in input.Properties.OrderBy(static p => p.Key, StringComparer.Ordinal))
            {
                string normalizedProperty = NormalizeVolatileText(pair.Value);
                components.Add(new FingerprintComponent($"property.{pair.Key}", normalizedProperty));
            }
        }

        if (input.Exception is not null)
        {
            string normalizedStack = NormalizeException(input.Exception, depth: 0);
            components.Add(new FingerprintComponent("exception.chain", normalizedStack));
        }

        var builder = new StringBuilder();
        foreach (FingerprintComponent component in components)
        {
            builder.Append(component.Name);
            builder.Append('=');
            builder.Append(component.NormalizedValue);
            builder.Append('\n');
        }

        string hash = ObservabilityHashing.Sha256Hex(builder.ToString());
        return new EventFingerprint(hash, components);
    }

    private string NormalizeException(Exception exception, int depth)
    {
        if (depth > _options.MaxInnerExceptionDepth)
        {
            return "[INNER_LIMIT]";
        }

        string type = exception.GetType().FullName ?? exception.GetType().Name;
        string message = NormalizeVolatileText(exception.Message ?? string.Empty);
        string stack = NormalizeStackTrace(exception.StackTrace);
        string current = $"{type}|{message}|{stack}";
        if (exception.InnerException is null)
        {
            return current;
        }

        return current + "->" + NormalizeException(exception.InnerException, depth + 1);
    }

    private string NormalizeStackTrace(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return string.Empty;
        }

        string[] lines = stackTrace.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var normalized = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            string frame = line;
            if (_options.RemoveStackLineNumbers)
            {
                int lineIndex = frame.IndexOf(":line ", StringComparison.OrdinalIgnoreCase);
                if (lineIndex >= 0)
                {
                    frame = frame[..lineIndex];
                }
            }

            if (_options.NormalizeStackFilePaths)
            {
                int inIndex = frame.IndexOf(" in ", StringComparison.Ordinal);
                if (inIndex >= 0)
                {
                    string prefix = frame[..inIndex];
                    string pathPart = frame[(inIndex + 4)..].Trim();
                    string fileName = ExtractFileName(pathPart);
                    frame = $"{prefix} in {fileName}";
                }
            }

            normalized.Add(NormalizeVolatileText(frame));
        }

        return string.Join(";", normalized);
    }

    private static string ExtractFileName(string pathPart)
    {
        int colon = pathPart.LastIndexOf(':');
        if (colon > 0)
        {
            pathPart = pathPart[..colon];
        }

        int slash = Math.Max(pathPart.LastIndexOf('/'), pathPart.LastIndexOf('\\'));
        return slash >= 0 ? pathPart[(slash + 1)..] : pathPart;
    }

    private static string NormalizeScalar(string value) =>
        CollapseWhitespace(value.Trim());

    private static string NormalizeVolatileText(string value)
    {
        string text = value;
        text = GuidRegex.Replace(text, string.Empty);
        text = IsoTimestampRegex.Replace(text, string.Empty);
        text = MemoryAddressRegex.Replace(text, string.Empty);
        text = CorrelationTokenRegex.Replace(text, string.Empty);
        text = UnixTimestampRegex.Replace(text, string.Empty);
        return CollapseWhitespace(text.Trim());
    }

    private static string CollapseWhitespace(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        bool previousWhitespace = false;
        foreach (char ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }
            }
            else
            {
                builder.Append(ch);
                previousWhitespace = false;
            }
        }

        return builder.ToString();
    }
}
