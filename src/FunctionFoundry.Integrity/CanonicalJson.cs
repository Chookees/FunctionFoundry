using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FunctionFoundry.Integrity;

/// <summary>
/// Documents the FunctionFoundry canonical JSON profile and provides deterministic serialization.
/// </summary>
/// <remarks>
/// <para>Profile identifier: <c>FunctionFoundry.CanonicalJson/v1</c>.</para>
/// <para>Rules:</para>
/// <list type="bullet">
/// <item><description>Objects: property names sorted by UTF-8 byte lexicographic order; duplicate names are rejected.</description></item>
/// <item><description>Arrays: element order is preserved.</description></item>
/// <item><description>Strings: minimal JSON escaping; control characters U+0000–U+001F and U+007F–U+009F use <c>\u00XX</c> lowercase hex; other valid UTF-8 is emitted literally.</description></item>
/// <item><description>Numbers: <c>NaN</c>, <c>Infinity</c>, and <c>-Infinity</c> are rejected; integers render without a fractional part; non-integers use the shortest decimal form with no trailing zeros and no leading zeros except a single <c>0</c> before the decimal point.</description></item>
/// <item><description>Booleans and null: lowercase <c>true</c>, <c>false</c>, and <c>null</c>.</description></item>
/// <item><description>Whitespace: insignificant whitespace is not permitted in input and is never emitted in output.</description></item>
/// <item><description>Output encoding: UTF-8 without BOM.</description></item>
/// </list>
/// <para>Thread safety: static methods are thread-safe.</para>
/// </remarks>
public static class CanonicalJson
{
    /// <summary>
    /// Gets the profile identifier string for this canonical JSON implementation.
    /// </summary>
    public const string ProfileId = "FunctionFoundry.CanonicalJson/v1";

    /// <summary>
    /// Canonicalizes UTF-8 JSON bytes to the profile output.
    /// </summary>
    /// <param name="utf8Json">Input JSON encoded as UTF-8. Must not contain a BOM.</param>
    /// <returns>Canonical UTF-8 JSON bytes without BOM.</returns>
    /// <exception cref="ArgumentException">Thrown when input is empty, contains a BOM, or is not valid canonicalizable JSON.</exception>
    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("JSON input must not be empty.", nameof(utf8Json));
        }

        if (utf8Json.Length >= 3 && utf8Json[0] == 0xEF && utf8Json[1] == 0xBB && utf8Json[2] == 0xBF)
        {
            throw new ArgumentException("UTF-8 BOM is not permitted.", nameof(utf8Json));
        }

        JsonModel root;
        try
        {
            root = JsonModel.Parse(utf8Json);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(ex.Message, nameof(utf8Json), ex);
        }

        return root.WriteCanonical();
    }

    /// <summary>
    /// Canonicalizes a JSON string to UTF-8 profile output.
    /// </summary>
    /// <param name="json">Input JSON text.</param>
    /// <returns>Canonical UTF-8 JSON bytes without BOM.</returns>
    /// <exception cref="ArgumentException">Thrown when input is invalid.</exception>
    public static byte[] Canonicalize(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        return Canonicalize(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Canonicalizes JSON from an input stream to an output stream using bounded buffering.
    /// </summary>
    /// <param name="input">Readable stream containing UTF-8 JSON. Must not contain a BOM.</param>
    /// <param name="output">Writable stream that receives canonical UTF-8 JSON.</param>
    /// <param name="bufferSize">Copy buffer size. Must be positive.</param>
    /// <exception cref="ArgumentNullException">Thrown when a stream is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bufferSize"/> is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when JSON is invalid.</exception>
    public static void Canonicalize(Stream input, Stream output, int bufferSize = 64 * 1024)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (bufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be positive.");
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            using var ms = new MemoryStream();
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                ms.Write(buffer, 0, read);
            }

            byte[] canonical = Canonicalize(ms.GetBuffer().AsSpan(0, (int)ms.Length));
            output.Write(canonical, 0, canonical.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Attempts to canonicalize UTF-8 JSON without throwing for parse failures.
    /// </summary>
    /// <param name="utf8Json">Input JSON encoded as UTF-8.</param>
    /// <param name="canonical">When successful, receives canonical UTF-8 output.</param>
    /// <param name="error">When unsuccessful, receives a descriptive error message.</param>
    /// <returns><see langword="true"/> when canonicalization succeeds; otherwise <see langword="false"/>.</returns>
    public static bool TryCanonicalize(ReadOnlySpan<byte> utf8Json, out byte[]? canonical, out string? error)
    {
        canonical = null;
        error = null;
        try
        {
            canonical = Canonicalize(utf8Json);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private abstract class JsonModel
    {
        public abstract void WriteCanonical(Utf8JsonWriter writer);

        public byte[] WriteCanonical()
        {
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, SkipValidation = true });
            WriteCanonical(writer);
            writer.Flush();
            return stream.ToArray();
        }

        public static JsonModel Parse(ReadOnlySpan<byte> utf8Json)
        {
            var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });

            if (!reader.Read())
            {
                throw new ArgumentException("JSON input is empty.", nameof(utf8Json));
            }

            JsonModel root = ReadValue(ref reader);
            if (reader.Read())
            {
                throw new ArgumentException("JSON input contains trailing data.", nameof(utf8Json));
            }

            return root;
        }

        private static JsonModel ReadValue(ref Utf8JsonReader reader)
        {
            return reader.TokenType switch
            {
                JsonTokenType.StartObject => ReadObject(ref reader),
                JsonTokenType.StartArray => ReadArray(ref reader),
                JsonTokenType.String => new JsonStringModel(reader.GetString() ?? string.Empty),
                JsonTokenType.Number => new JsonNumberModel(reader.ValueSpan),
                JsonTokenType.True => new JsonBoolModel(true),
                JsonTokenType.False => new JsonBoolModel(false),
                JsonTokenType.Null => JsonNullModel.Instance,
                _ => throw new ArgumentException($"Unexpected JSON token '{reader.TokenType}'."),
            };
        }

        private static JsonObjectModel ReadObject(ref Utf8JsonReader reader)
        {
            var properties = new List<(byte[] NameUtf8, JsonModel Value)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new ArgumentException("Malformed JSON object.");
                }

                ReadOnlySpan<byte> nameSpan = reader.ValueSpan;
                string name = reader.GetString() ?? string.Empty;
                if (!seen.Add(name))
                {
                    throw new ArgumentException($"Duplicate property name '{name}'.");
                }

                if (!reader.Read())
                {
                    throw new ArgumentException("Unexpected end of JSON object.");
                }

                JsonModel value = ReadValue(ref reader);
                properties.Add((nameSpan.ToArray(), value));
            }

            properties.Sort(static (left, right) => CompareUtf8(left.NameUtf8, right.NameUtf8));
            return new JsonObjectModel(properties);
        }

        private static JsonArrayModel ReadArray(ref Utf8JsonReader reader)
        {
            var items = new List<JsonModel>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                items.Add(ReadValue(ref reader));
            }

            return new JsonArrayModel(items);
        }

        private static int CompareUtf8(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            int min = Math.Min(left.Length, right.Length);
            for (int i = 0; i < min; i++)
            {
                int diff = left[i] - right[i];
                if (diff != 0)
                {
                    return diff;
                }
            }

            return left.Length - right.Length;
        }
    }

    private sealed class JsonNullModel : JsonModel
    {
        public static JsonNullModel Instance { get; } = new();

        public override void WriteCanonical(Utf8JsonWriter writer) => writer.WriteNullValue();
    }

    private sealed class JsonBoolModel(bool value) : JsonModel
    {
        public override void WriteCanonical(Utf8JsonWriter writer) => writer.WriteBooleanValue(value);
    }

    private sealed class JsonStringModel(string value) : JsonModel
    {
        public override void WriteCanonical(Utf8JsonWriter writer) => writer.WriteRawValue(EncodeString(value));

        private static string EncodeString(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    default:
                        if (character <= '\u001f' || (character >= '\u007f' && character <= '\u009f'))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }
    }

    private sealed class JsonNumberModel(ReadOnlySpan<byte> rawToken) : JsonModel
    {
        private readonly string _canonical = CanonicalizeNumberToken(rawToken);

        public override void WriteCanonical(Utf8JsonWriter writer) => writer.WriteRawValue(_canonical);

        private static string CanonicalizeNumberToken(ReadOnlySpan<byte> rawToken)
        {
            string token = Encoding.UTF8.GetString(rawToken);
            if (token.Length == 0)
            {
                throw new ArgumentException("Number token is empty.");
            }

            if (string.Equals(token, "NaN", StringComparison.Ordinal) ||
                string.Equals(token, "Infinity", StringComparison.Ordinal) ||
                string.Equals(token, "-Infinity", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Number token '{token}' is not permitted.");
            }

            if (token[0] == '+' || (token[0] == '0' && token.Length > 1 && token[1] != '.'))
            {
                throw new ArgumentException($"Number token '{token}' is not in canonical form.");
            }

            if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integral))
            {
                return integral.ToString(CultureInfo.InvariantCulture);
            }

            if (decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal decimalValue))
            {
                return FormatDecimal(decimalValue);
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue))
            {
                return FormatDouble(doubleValue);
            }

            throw new ArgumentException($"Number token '{token}' is not a valid JSON number.");
        }

        private static string FormatDecimal(decimal value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            if (!text.Contains('.', StringComparison.Ordinal))
            {
                return text;
            }

            text = text.TrimEnd('0').TrimEnd('.');
            return text.Length == 0 ? "0" : text;
        }

        private static string FormatDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("Non-finite numbers are not permitted.");
            }

            if (Math.Abs(value) <= 9007199254740991d && Math.Abs(value - Math.Truncate(value)) < 1e-12)
            {
                return ((long)Math.Truncate(value)).ToString(CultureInfo.InvariantCulture);
            }

            string text = value.ToString("G17", CultureInfo.InvariantCulture);
            if (text.Contains('E', StringComparison.OrdinalIgnoreCase))
            {
                return text.ToUpperInvariant();
            }

            if (!text.Contains('.', StringComparison.Ordinal))
            {
                return text;
            }

            text = text.TrimEnd('0').TrimEnd('.');
            return text.Length == 0 ? "0" : text;
        }
    }

    private sealed class JsonArrayModel(IReadOnlyList<JsonModel> items) : JsonModel
    {
        public override void WriteCanonical(Utf8JsonWriter writer)
        {
            writer.WriteStartArray();
            foreach (JsonModel item in items)
            {
                item.WriteCanonical(writer);
            }

            writer.WriteEndArray();
        }
    }

    private sealed class JsonObjectModel(IReadOnlyList<(byte[] NameUtf8, JsonModel Value)> properties) : JsonModel
    {
        public override void WriteCanonical(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            foreach ((byte[] nameUtf8, JsonModel value) in properties)
            {
                writer.WritePropertyName(Encoding.UTF8.GetString(nameUtf8));
                value.WriteCanonical(writer);
            }

            writer.WriteEndObject();
        }
    }
}
