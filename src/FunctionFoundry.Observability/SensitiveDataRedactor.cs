namespace FunctionFoundry.Observability;

/// <summary>
/// Options controlling structural sensitive-data redaction.
/// </summary>
public sealed class SensitiveDataRedactorOptions
{
    /// <summary>
    /// Gets or sets the maximum object graph depth to traverse. Defaults to 32.
    /// </summary>
    public int MaxDepth { get; set; } = 32;

    /// <summary>
    /// Gets or sets the maximum number of nodes visited per redaction call. Defaults to 10_000.
    /// </summary>
    public int MaxItems { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets property-name policies evaluated before value inspection.
    /// </summary>
    public IReadOnlyList<SensitivePropertyNamePolicy> PropertyNamePolicies { get; set; } =
        [SensitivePropertyNamePolicy.Default];

    /// <summary>
    /// Gets or sets regex pattern rules applied to string values.
    /// </summary>
    public IReadOnlyList<SensitivePatternRule> PatternRules { get; set; } =
        SensitivePatternRule.DefaultRules;

    /// <summary>
    /// Gets or sets the entropy detector for secret-like string candidates.
    /// </summary>
    public EntropySecretDetector EntropyDetector { get; set; } = new();

    /// <summary>
    /// Gets or sets the fallback replacement strategy for detected secrets. Defaults to <see cref="RedactionReplacementStrategy.Redacted"/>.
    /// </summary>
    public RedactionReplacementStrategy DefaultStrategy { get; set; } = RedactionReplacementStrategy.Redacted;

    /// <summary>
    /// Gets or sets the redaction token used by <see cref="RedactionReplacementStrategy.Redacted"/>.
    /// </summary>
    public string RedactionToken { get; set; } = "[REDACTED]";

    /// <summary>
    /// Validates option ranges and required values.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when limits are not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when required strings are invalid.</exception>
    public void Validate()
    {
        if (MaxDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDepth), "MaxDepth must be positive.");
        }

        if (MaxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxItems), "MaxItems must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(RedactionToken);
        ArgumentNullException.ThrowIfNull(PropertyNamePolicies);
        ArgumentNullException.ThrowIfNull(PatternRules);
        ArgumentNullException.ThrowIfNull(EntropyDetector);
    }
}

/// <summary>
/// Result of a sensitive-data redaction operation.
/// </summary>
/// <param name="Value">Redacted value tree. May be the original shape with replaced leaves.</param>
/// <param name="RedactedFieldCount">Number of fields or values replaced.</param>
/// <param name="VisitedItemCount">Number of nodes visited during traversal.</param>
/// <param name="TruncatedDueToDepth">Whether traversal stopped because <see cref="SensitiveDataRedactorOptions.MaxDepth"/> was reached.</param>
/// <param name="TruncatedDueToItemLimit">Whether traversal stopped because <see cref="SensitiveDataRedactorOptions.MaxItems"/> was reached.</param>
public sealed record RedactionResult(
    object? Value,
    int RedactedFieldCount,
    int VisitedItemCount,
    bool TruncatedDueToDepth,
    bool TruncatedDueToItemLimit);

/// <summary>
/// Redacts sensitive values from nested object graphs without invoking arbitrary user code where avoidable.
/// </summary>
/// <remarks>
/// <para>Traversal supports dictionaries, read-only dictionaries, lists, arrays, tuples, and plain CLR objects via public instance properties.</para>
/// <para>Delegates, tasks, streams, and reflection types are not expanded; they are replaced with unsupported placeholders.</para>
/// <para>Property getters on CLR objects may be invoked during reflection-based traversal. Prefer dictionary-shaped payloads when side effects are a concern.</para>
/// <para>Thread safety: instances are immutable after construction and safe for concurrent use.</para>
/// </remarks>
public sealed class SensitiveDataRedactor
{
    private readonly SensitiveDataRedactorOptions _options;
    private readonly SensitivePatternRule[] _patternRules;

    /// <summary>
    /// Initializes a new instance of the <see cref="SensitiveDataRedactor"/> class.
    /// </summary>
    /// <param name="options">Redaction options. When null, defaults are used.</param>
    public SensitiveDataRedactor(SensitiveDataRedactorOptions? options = null)
    {
        _options = options ?? new SensitiveDataRedactorOptions();
        _options.Validate();
        _patternRules = _options.PatternRules.ToArray();
    }

    /// <summary>
    /// Redacts sensitive values from <paramref name="root"/>.
    /// </summary>
    /// <param name="root">Root object, dictionary, collection, or scalar.</param>
    /// <returns>A <see cref="RedactionResult"/> containing the sanitized graph and counters.</returns>
    public RedactionResult Redact(object? root)
    {
        var context = new RedactionContext(_options, _patternRules);
        object? value = context.RedactValue(root, propertyName: null, depth: 0);
        return new RedactionResult(
            value,
            context.RedactedFieldCount,
            context.VisitedItemCount,
            context.TruncatedDueToDepth,
            context.TruncatedDueToItemLimit);
    }

    private sealed class RedactionContext
    {
        private readonly SensitiveDataRedactorOptions _options;
        private readonly SensitivePatternRule[] _patternRules;
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

        internal RedactionContext(SensitiveDataRedactorOptions options, SensitivePatternRule[] patternRules)
        {
            _options = options;
            _patternRules = patternRules;
        }

        internal int RedactedFieldCount { get; private set; }

        internal int VisitedItemCount { get; private set; }

        internal bool TruncatedDueToDepth { get; private set; }

        internal bool TruncatedDueToItemLimit { get; private set; }

        internal object? RedactValue(object? value, string? propertyName, int depth)
        {
            if (!TryConsumeVisit())
            {
                TruncatedDueToItemLimit = true;
                return "[TRUNCATED]";
            }

            if (depth > _options.MaxDepth)
            {
                TruncatedDueToDepth = true;
                return "[DEPTH_LIMIT]";
            }

            if (value is null)
            {
                return null;
            }

            if (value is string text)
            {
                return RedactString(text, propertyName);
            }

            if (value is char or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                return value;
            }

            if (value is DateTime or DateTimeOffset or DateOnly or TimeOnly or Guid or TimeSpan)
            {
                return value;
            }

            if (IsUnsupportedType(value))
            {
                return "[UNSUPPORTED]";
            }

            if (value is System.Collections.IDictionary dictionary)
            {
                return RedactDictionary(dictionary, depth);
            }

            if (value is System.Collections.IEnumerable enumerable and not string)
            {
                return RedactEnumerable(enumerable, depth);
            }

            return RedactObject(value, depth);
        }

        private object RedactDictionary(System.Collections.IDictionary dictionary, int depth)
        {
            if (!_visited.Add(dictionary))
            {
                return "[CYCLE]";
            }

            var result = new Dictionary<string, object?>(dictionary.Count, StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                string key = Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                object? child = RedactValue(entry.Value, key, depth + 1);
                if (child is not RemovedSentinel)
                {
                    result[key] = child;
                }
            }

            _visited.Remove(dictionary);
            return result;
        }

        private object RedactEnumerable(System.Collections.IEnumerable enumerable, int depth)
        {
            if (!_visited.Add(enumerable))
            {
                return "[CYCLE]";
            }

            var items = new List<object?>();
            foreach (object? item in enumerable)
            {
                items.Add(RedactValue(item, propertyName: null, depth + 1));
                if (TruncatedDueToItemLimit)
                {
                    break;
                }
            }

            _visited.Remove(enumerable);
            return items;
        }

        private object RedactObject(object value, int depth)
        {
            if (!_visited.Add(value))
            {
                return "[CYCLE]";
            }

            Type type = value.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                object? key = type.GetProperty("Key")?.GetValue(value);
                object? pairValue = type.GetProperty("Value")?.GetValue(value);
                string keyName = Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                return new Dictionary<string, object?>(1, StringComparer.Ordinal)
                {
                    [keyName] = RedactValue(pairValue, keyName, depth + 1),
                };
            }

            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (System.Reflection.PropertyInfo property in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (!property.CanRead)
                {
                    continue;
                }

                object? propertyValue;
                try
                {
                    propertyValue = property.GetValue(value);
                }
                catch (System.Reflection.TargetInvocationException)
                {
                    result[property.Name] = "[GETTER_FAILED]";
                    continue;
                }

                object? redacted = RedactValue(propertyValue, property.Name, depth + 1);
                if (redacted is not RemovedSentinel)
                {
                    result[property.Name] = redacted;
                }
            }

            _visited.Remove(value);
            return result;
        }

        private object? RedactString(string value, string? propertyName)
        {
            RedactionReplacementStrategy strategy = ResolveStrategy(value, propertyName);
            if (strategy == RedactionReplacementStrategy.Removed)
            {
                RedactedFieldCount++;
                return RemovedSentinel.Instance;
            }

            if (!ShouldRedact(value, propertyName))
            {
                return value;
            }

            RedactedFieldCount++;
            return ApplyStrategy(value, strategy);
        }

        private bool ShouldRedact(string value, string? propertyName)
        {
            if (propertyName is not null && MatchesPropertyPolicy(propertyName, out _))
            {
                return true;
            }

            foreach (SensitivePatternRule rule in _patternRules)
            {
                if (rule.IsMatch(value))
                {
                    return true;
                }
            }

            return _options.EntropyDetector.IsLikelySecret(value);
        }

        private RedactionReplacementStrategy ResolveStrategy(string value, string? propertyName)
        {
            if (propertyName is not null && MatchesPropertyPolicy(propertyName, out RedactionReplacementStrategy propertyStrategy))
            {
                return propertyStrategy;
            }

            foreach (SensitivePatternRule rule in _patternRules)
            {
                if (rule.IsMatch(value))
                {
                    return rule.Strategy;
                }
            }

            if (_options.EntropyDetector.IsLikelySecret(value))
            {
                return _options.DefaultStrategy;
            }

            return _options.DefaultStrategy;
        }

        private bool MatchesPropertyPolicy(string propertyName, out RedactionReplacementStrategy strategy)
        {
            foreach (SensitivePropertyNamePolicy policy in _options.PropertyNamePolicies)
            {
                if (policy.Matches(propertyName))
                {
                    strategy = policy.Strategy;
                    return true;
                }
            }

            strategy = _options.DefaultStrategy;
            return false;
        }

        private object ApplyStrategy(string value, RedactionReplacementStrategy strategy) =>
            strategy switch
            {
                RedactionReplacementStrategy.Masked => Mask(value),
                RedactionReplacementStrategy.Hashed => $"sha256:{ObservabilityHashing.Sha256Hex(value)[..16]}",
                RedactionReplacementStrategy.Removed => RemovedSentinel.Instance,
                _ => _options.RedactionToken,
            };

        private static string Mask(string value)
        {
            if (value.Length <= 2)
            {
                return new string('*', value.Length);
            }

            return $"{value[0]}{new string('*', Math.Min(value.Length - 2, 8))}{value[^1]}";
        }

        private bool TryConsumeVisit()
        {
            if (VisitedItemCount >= _options.MaxItems)
            {
                return false;
            }

            VisitedItemCount++;
            return true;
        }

        private static bool IsUnsupportedType(object value) =>
            value is Delegate or System.IO.Stream or System.Threading.Tasks.Task or System.Reflection.Assembly or System.Reflection.Module or IntPtr or UIntPtr;
    }

    private sealed class RemovedSentinel
    {
        internal static readonly RemovedSentinel Instance = new();

        private RemovedSentinel()
        {
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
