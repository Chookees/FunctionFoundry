using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FunctionFoundry.Data;

/// <summary>
/// Policy for resolving non-conflicting three-way merge changes.
/// </summary>
public enum ThreeWayMergePolicy
{
    /// <summary>Prefer local changes when both sides changed identically.</summary>
    PreferLocal,

    /// <summary>Prefer remote changes when both sides changed identically.</summary>
    PreferRemote,

    /// <summary>Fail when both local and remote changed the same path to different values.</summary>
    FailOnConcurrentEdit,
}

/// <summary>
/// Kind of merge conflict.
/// </summary>
public enum ThreeWayMergeConflictKind
{
    /// <summary>Local and remote both modified the same path differently.</summary>
    ConcurrentEdit,

    /// <summary>One side deleted while the other modified.</summary>
    DeleteModify,

    /// <summary>Type mismatch between merged values.</summary>
    TypeMismatch,
}

/// <summary>
/// A path-level merge conflict.
/// </summary>
/// <param name="Path">JSON Pointer path.</param>
/// <param name="Kind">Conflict kind.</param>
/// <param name="BaseValue">Base JSON value.</param>
/// <param name="LocalValue">Local JSON value.</param>
/// <param name="RemoteValue">Remote JSON value.</param>
public sealed record ThreeWayMergeConflict(
    string Path,
    ThreeWayMergeConflictKind Kind,
    string? BaseValue,
    string? LocalValue,
    string? RemoteValue);

/// <summary>
/// Options for <see cref="ThreeWayMerge"/>.
/// </summary>
/// <param name="Policy">Non-conflict resolution policy.</param>
/// <param name="MaximumNodeVisits">Maximum node visits. Must be positive.</param>
public sealed record ThreeWayMergeOptions(
    ThreeWayMergePolicy Policy = ThreeWayMergePolicy.FailOnConcurrentEdit,
    int MaximumNodeVisits = 100_000)
{
    /// <summary>
    /// Validates options.
    /// </summary>
    public ThreeWayMergeOptions Validate()
    {
        if (MaximumNodeVisits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumNodeVisits), "Maximum node visits must be positive.");
        }

        return this;
    }
}

/// <summary>
/// Result of a three-way merge.
/// </summary>
/// <param name="Merged">Merged JSON when no conflicts, otherwise null.</param>
/// <param name="Conflicts">Deterministic path-ordered conflicts.</param>
/// <param name="NodeVisits">Node visits performed.</param>
public sealed record ThreeWayMergeResult(
    JsonElement? Merged,
    IReadOnlyList<ThreeWayMergeConflict> Conflicts,
    int NodeVisits)
{
    /// <summary>
    /// True when the merge produced conflicts.
    /// </summary>
    public bool HasConflicts => Conflicts.Count > 0;

    /// <summary>
    /// Renders human-readable conflict summary.
    /// </summary>
    public string ToHumanReadable()
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"conflicts={Conflicts.Count}; visits={NodeVisits}; merged={(Merged is not null)}");
        builder.AppendLine();
        foreach (ThreeWayMergeConflict conflict in Conflicts)
        {
            builder.Append(conflict.Kind);
            builder.Append(' ');
            builder.Append(conflict.Path);
            builder.Append(" base=");
            builder.Append(conflict.BaseValue ?? "null");
            builder.Append(" local=");
            builder.Append(conflict.LocalValue ?? "null");
            builder.Append(" remote=");
            builder.AppendLine(conflict.RemoteValue ?? "null");
        }

        return builder.ToString();
    }
}

/// <summary>
/// Performs deterministic three-way merges over JSON trees with explicit path-level conflicts.
/// </summary>
public static class ThreeWayMerge
{
    /// <summary>
    /// Merges base/local/remote UTF-8 JSON documents.
    /// </summary>
    public static ThreeWayMergeResult Merge(ReadOnlySpan<byte> baseJson, ReadOnlySpan<byte> localJson, ReadOnlySpan<byte> remoteJson, ThreeWayMergeOptions? options = null)
    {
        ThreeWayMergeOptions resolved = (options ?? new ThreeWayMergeOptions()).Validate();
        using JsonDocument baseDoc = JsonDocument.Parse(baseJson.ToArray());
        using JsonDocument localDoc = JsonDocument.Parse(localJson.ToArray());
        using JsonDocument remoteDoc = JsonDocument.Parse(remoteJson.ToArray());
        return Merge(baseDoc.RootElement, localDoc.RootElement, remoteDoc.RootElement, resolved);
    }

    /// <summary>
    /// Merges three <see cref="JsonElement"/> trees.
    /// </summary>
    public static ThreeWayMergeResult Merge(JsonElement baseRoot, JsonElement localRoot, JsonElement remoteRoot, ThreeWayMergeOptions? options = null)
    {
        ThreeWayMergeOptions resolved = (options ?? new ThreeWayMergeOptions()).Validate();
        var context = new MergeContext(resolved);
        JsonElement? merged = MergeNodes(baseRoot, localRoot, remoteRoot, string.Empty, context);
        ThreeWayMergeConflict[] orderedConflicts = context.Conflicts
            .OrderBy(static c => c.Path, StringComparer.Ordinal)
            .ThenBy(static c => c.Kind)
            .ToArray();

        if (orderedConflicts.Length > 0)
        {
            return new ThreeWayMergeResult(null, orderedConflicts, context.NodeVisits);
        }

        return new ThreeWayMergeResult(merged, orderedConflicts, context.NodeVisits);
    }

    private static JsonElement? MergeNodes(JsonElement baseValue, JsonElement localValue, JsonElement remoteValue, string path, MergeContext context)
    {
        if (!context.Visit())
        {
            return null;
        }

        bool localChanged = !JsonElement.DeepEquals(baseValue, localValue);
        bool remoteChanged = !JsonElement.DeepEquals(baseValue, remoteValue);

        if (!localChanged && !remoteChanged)
        {
            return Clone(baseValue);
        }

        if (localChanged && !remoteChanged)
        {
            return Clone(localValue);
        }

        if (!localChanged && remoteChanged)
        {
            return Clone(remoteValue);
        }

        if (JsonElement.DeepEquals(localValue, remoteValue))
        {
            return resolvedPolicyClone(localValue, remoteValue, context.Options.Policy);
        }

        if (baseValue.ValueKind == JsonValueKind.Undefined || baseValue.ValueKind == JsonValueKind.Null)
        {
            if (localValue.ValueKind != remoteValue.ValueKind)
            {
                context.Conflicts.Add(new ThreeWayMergeConflict(path, ThreeWayMergeConflictKind.TypeMismatch, Raw(baseValue), Raw(localValue), Raw(remoteValue)));
                return null;
            }
        }

        if (localValue.ValueKind != remoteValue.ValueKind)
        {
            context.Conflicts.Add(new ThreeWayMergeConflict(path, ThreeWayMergeConflictKind.TypeMismatch, Raw(baseValue), Raw(localValue), Raw(remoteValue)));
            return null;
        }

        switch (localValue.ValueKind)
        {
            case JsonValueKind.Object:
                return MergeObjects(baseValue, localValue, remoteValue, path, context);
            case JsonValueKind.Array:
                if (!JsonElement.DeepEquals(localValue, remoteValue))
                {
                    context.Conflicts.Add(new ThreeWayMergeConflict(path, ThreeWayMergeConflictKind.ConcurrentEdit, Raw(baseValue), Raw(localValue), Raw(remoteValue)));
                }

                return Clone(localValue);
            default:
                if (context.Options.Policy == ThreeWayMergePolicy.FailOnConcurrentEdit)
                {
                    context.Conflicts.Add(new ThreeWayMergeConflict(path, ThreeWayMergeConflictKind.ConcurrentEdit, Raw(baseValue), Raw(localValue), Raw(remoteValue)));
                    return null;
                }

                return resolvedPolicyClone(localValue, remoteValue, context.Options.Policy);
        }
    }

    private static JsonElement MergeObjects(JsonElement baseValue, JsonElement localValue, JsonElement remoteValue, string path, MergeContext context)
    {
        var names = baseValue.EnumerateObject().Select(static p => p.Name)
            .Concat(localValue.EnumerateObject().Select(static p => p.Name))
            .Concat(remoteValue.EnumerateObject().Select(static p => p.Name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static n => n, StringComparer.Ordinal)
            .ToArray();

        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream);
        writer.WriteStartObject();
        foreach (string name in names)
        {
            bool inBase = TryGet(baseValue, name, out JsonElement baseChild);
            bool inLocal = TryGet(localValue, name, out JsonElement localChild);
            bool inRemote = TryGet(remoteValue, name, out JsonElement remoteChild);
            string childPath = string.IsNullOrEmpty(path) ? "/" + Escape(name) : path + "/" + Escape(name);

            if (!inBase && inLocal && inRemote)
            {
                JsonElement? mergedChild = MergeNodes(default, localChild, remoteChild, childPath, context);
                if (mergedChild is JsonElement child)
                {
                    writer.WritePropertyName(name);
                    child.WriteTo(writer);
                }

                continue;
            }

            if (inBase && !inLocal && inRemote)
            {
                context.Conflicts.Add(new ThreeWayMergeConflict(childPath, ThreeWayMergeConflictKind.DeleteModify, Raw(baseChild), null, Raw(remoteChild)));
                continue;
            }

            if (inBase && inLocal && !inRemote)
            {
                context.Conflicts.Add(new ThreeWayMergeConflict(childPath, ThreeWayMergeConflictKind.DeleteModify, Raw(baseChild), Raw(localChild), null));
                continue;
            }

            if (inBase && inLocal && inRemote)
            {
                JsonElement? mergedChild = MergeNodes(baseChild, localChild, remoteChild, childPath, context);
                if (mergedChild is JsonElement child)
                {
                    writer.WritePropertyName(name);
                    child.WriteTo(writer);
                }
            }
            else if (inLocal)
            {
                writer.WritePropertyName(name);
                localChild.WriteTo(writer);
            }
            else if (inRemote)
            {
                writer.WritePropertyName(name);
                remoteChild.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
        writer.Flush();
        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static JsonElement? resolvedPolicyClone(JsonElement local, JsonElement remote, ThreeWayMergePolicy policy) =>
        policy == ThreeWayMergePolicy.PreferRemote ? Clone(remote) : Clone(local);

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static JsonElement Clone(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
        {
            using JsonDocument doc = JsonDocument.Parse("null");
            return doc.RootElement.Clone();
        }

        using JsonDocument cloneDoc = JsonDocument.Parse(element.GetRawText());
        return cloneDoc.RootElement.Clone();
    }

    private static string? Raw(JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined ? null : element.GetRawText();

    private static string Escape(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private sealed class MergeContext(ThreeWayMergeOptions options)
    {
        public ThreeWayMergeOptions Options { get; } = options;

        public List<ThreeWayMergeConflict> Conflicts { get; } = [];

        public int NodeVisits { get; private set; }

        public bool Visit()
        {
            NodeVisits++;
            return NodeVisits <= Options.MaximumNodeVisits;
        }
    }
}
