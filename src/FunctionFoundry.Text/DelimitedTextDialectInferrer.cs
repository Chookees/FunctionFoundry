using System.Globalization;
using System.Text;

namespace FunctionFoundry.Text;

/// <summary>
/// Inferred newline style.
/// </summary>
public enum DelimitedTextNewlineStyle
{
    /// <summary>Unix line feed.</summary>
    Lf,

    /// <summary>Carriage return + line feed.</summary>
    CrLf,
}

/// <summary>
/// Evidence supporting a dialect inference.
/// </summary>
/// <param name="Description">Human-readable evidence statement.</param>
/// <param name="Weight">Relative weight in [0,1].</param>
public sealed record DialectInferenceEvidence(string Description, double Weight);

/// <summary>
/// A ranked dialect candidate.
/// </summary>
/// <param name="Delimiter">Field delimiter character.</param>
/// <param name="Quote">Quote character.</param>
/// <param name="Escape">Escape character.</param>
/// <param name="Newline">Inferred newline style.</param>
/// <param name="HasHeader">Header likelihood in [0,1].</param>
/// <param name="Confidence">Overall confidence in [0,1], never 1.0 by design.</param>
/// <param name="Evidence">Supporting evidence entries.</param>
public sealed record DelimitedTextDialectCandidate(
    char Delimiter,
    char Quote,
    char Escape,
    DelimitedTextNewlineStyle Newline,
    double HasHeader,
    double Confidence,
    IReadOnlyList<DialectInferenceEvidence> Evidence);

/// <summary>
/// Options for <see cref="DelimitedTextDialectInferrer"/>.
/// </summary>
/// <param name="MaximumSampleChars">Maximum characters analyzed. Must be positive.</param>
/// <param name="MaximumCandidates">Maximum ranked candidates returned. Must be positive.</param>
public sealed record DelimitedTextDialectInferrerOptions(
    int MaximumSampleChars = 64 * 1024,
    int MaximumCandidates = 5)
{
    /// <summary>
    /// Validates options.
    /// </summary>
    public DelimitedTextDialectInferrerOptions Validate()
    {
        if (MaximumSampleChars <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumSampleChars), "Maximum sample chars must be positive.");
        }

        if (MaximumCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCandidates), "Maximum candidates must be positive.");
        }

        return this;
    }
}

/// <summary>
/// Result of dialect inference.
/// </summary>
/// <param name="Candidates">Ranked candidates ordered by descending confidence.</param>
/// <param name="SampleChars">Number of characters analyzed (may be less than input length).</param>
/// <param name="Truncated">True when input exceeded sample limits.</param>
public sealed record DelimitedTextDialectInferenceResult(
    IReadOnlyList<DelimitedTextDialectCandidate> Candidates,
    int SampleChars,
    bool Truncated);

/// <summary>
/// Infers delimited-text dialect parameters with confidence and evidence.
/// </summary>
public sealed class DelimitedTextDialectInferrer
{
    private static readonly char[] CandidateDelimiters = [',', ';', '\t', '|'];
    private static readonly char[] CandidateQuotes = ['"', '\''];
    private static readonly char[] CandidateEscapes = ['\\', '"'];

    private readonly DelimitedTextDialectInferrerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelimitedTextDialectInferrer"/> class.
    /// </summary>
    public DelimitedTextDialectInferrer(DelimitedTextDialectInferrerOptions? options = null)
    {
        _options = (options ?? new DelimitedTextDialectInferrerOptions()).Validate();
    }

    /// <summary>
    /// Infers dialect candidates from a text sample.
    /// </summary>
    public DelimitedTextDialectInferenceResult Infer(string sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        bool truncated = sample.Length > _options.MaximumSampleChars;
        string analyzed = truncated ? sample[.._options.MaximumSampleChars] : sample;
        if (analyzed.Length == 0)
        {
            throw new ArgumentException("Sample must not be empty.", nameof(sample));
        }

        DelimitedTextNewlineStyle newline = analyzed.Contains("\r\n", StringComparison.Ordinal)
            ? DelimitedTextNewlineStyle.CrLf
            : DelimitedTextNewlineStyle.Lf;

        var candidates = new List<DelimitedTextDialectCandidate>();
        foreach (char delimiter in CandidateDelimiters)
        {
            foreach (char quote in CandidateQuotes)
            {
                foreach (char escape in CandidateEscapes)
                {
                    ScoreDialect(analyzed, delimiter, quote, escape, newline, candidates);
                }
            }
        }

        DelimitedTextDialectCandidate[] ranked = candidates
            .OrderByDescending(static c => c.Confidence)
            .ThenBy(static c => c.Delimiter)
            .ThenBy(static c => c.Quote)
            .Take(_options.MaximumCandidates)
            .ToArray();

        return new DelimitedTextDialectInferenceResult(ranked, analyzed.Length, truncated);
    }

    private static void ScoreDialect(
        string sample,
        char delimiter,
        char quote,
        char escape,
        DelimitedTextNewlineStyle newline,
        List<DelimitedTextDialectCandidate> output)
    {
        string[] lines = sample.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return;
        }

        var evidence = new List<DialectInferenceEvidence>();
        int consistentRows = 0;
        int? expectedColumns = null;
        int delimiterCount = 0;
        int quotedFieldCount = 0;
        foreach (string line in lines)
        {
            List<string> fields = ParseLine(line, delimiter, quote, escape);
            delimiterCount += fields.Count - 1;
            if (fields.Any(static f => f.Length >= 2 && f.StartsWith('"') && f.EndsWith('"')))
            {
                quotedFieldCount++;
            }

            if (expectedColumns is null)
            {
                expectedColumns = fields.Count;
                consistentRows++;
            }
            else if (fields.Count == expectedColumns)
            {
                consistentRows++;
            }
        }

        double columnConsistency = lines.Length == 0 ? 0 : (double)consistentRows / lines.Length;
        evidence.Add(new DialectInferenceEvidence(
            string.Create(CultureInfo.InvariantCulture, $"{consistentRows}/{lines.Length} rows share column count {expectedColumns}."),
            columnConsistency));

        double delimiterDensity = delimiterCount / (double)Math.Max(1, lines.Length);
        evidence.Add(new DialectInferenceEvidence(
            string.Create(CultureInfo.InvariantCulture, $"Average delimiters per row: {delimiterDensity:F2}."),
            Math.Min(1.0, delimiterDensity / 10.0)));

        double quoteSignal = quotedFieldCount / (double)Math.Max(1, lines.Length);
        if (quote != '"')
        {
            quoteSignal *= 0.5;
        }

        evidence.Add(new DialectInferenceEvidence(
            string.Create(CultureInfo.InvariantCulture, $"{quotedFieldCount} rows contain quoted fields."),
            quoteSignal));

        double headerLikelihood = InferHeaderLikelihood(lines, delimiter, quote, escape);
        evidence.Add(new DialectInferenceEvidence(
            "First row looks textual/non-numeric relative to following rows.",
            headerLikelihood));

        double confidence = (columnConsistency * 0.45) + (Math.Min(1.0, delimiterDensity / 8.0) * 0.35) + (quoteSignal * 0.1) + (headerLikelihood * 0.1);
        confidence = Math.Min(0.95, confidence);
        if (lines.Length < 2)
        {
            confidence = Math.Min(confidence, 0.5);
            evidence.Add(new DialectInferenceEvidence("Sample has fewer than two rows; confidence capped.", 0.2));
        }

        output.Add(new DelimitedTextDialectCandidate(delimiter, quote, escape, newline, headerLikelihood, confidence, evidence));
    }

    private static double InferHeaderLikelihood(string[] lines, char delimiter, char quote, char escape)
    {
        if (lines.Length < 2)
        {
            return 0.2;
        }

        List<string> first = ParseLine(lines[0], delimiter, quote, escape);
        List<string> second = ParseLine(lines[1], delimiter, quote, escape);
        int textual = first.Count(field => !double.TryParse(field, NumberStyles.Any, CultureInfo.InvariantCulture, out _));
        int secondNumeric = second.Count(field => double.TryParse(field, NumberStyles.Any, CultureInfo.InvariantCulture, out _));
        if (textual == first.Count && secondNumeric > 0)
        {
            return 0.8;
        }

        return 0.3;
    }

    private static List<string> ParseLine(string line, char delimiter, char quote, char escape)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuotes)
            {
                if (ch == escape && i + 1 < line.Length)
                {
                    current.Append(line[++i]);
                    continue;
                }

                if (ch == quote)
                {
                    inQuotes = false;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (ch == quote)
            {
                inQuotes = true;
                continue;
            }

            if (ch == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        fields.Add(current.ToString());
        return fields;
    }
}
