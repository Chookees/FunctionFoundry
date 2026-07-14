using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FunctionFoundry.Data;

/// <summary>
/// Kind of structural tree diff operation.
/// </summary>
public enum StructuralDiffOperationKind
{
    /// <summary>A value was added at a path.</summary>
    Add,

    /// <summary>A value was removed from a path.</summary>
    Remove,

    /// <summary>A value was replaced at a path.</summary>
    Replace,

    /// <summary>An array element moved from one index to another.</summary>
    Move,
}

/// <summary>
/// A single deterministic structural diff operation.
/// </summary>
/// <param name="Kind">Operation kind.</param>
/// <param name="Path">JSON Pointer-style path (RFC 6901).</param>
/// <param name="FromPath">Source path for move operations.</param>
/// <param name="OldValue">Previous value JSON when applicable.</param>
/// <param name="NewValue">New value JSON when applicable.</param>
public sealed record StructuralDiffOperation(
    StructuralDiffOperationKind Kind,
    string Path,
    string? FromPath = null,
    string? OldValue = null,
    string? NewValue = null);

/// <summary>
/// Options for <see cref="StructuralTreeDiff"/>.
/// </summary>
/// <param name="ArrayIdentityProperty">Object property used to match array elements. When null, index-based matching is used.</param>
/// <param name="MaximumNodeVisits">Maximum node visits before aborting. Must be positive.</param>
/// <param name="MaximumDepth">Maximum tree depth to traverse. Must be positive.</param>
public sealed record StructuralTreeDiffOptions(
    string? ArrayIdentityProperty = "id",
    int MaximumNodeVisits = 100_000,
    int MaximumDepth = 128)
{
    /// <summary>
    /// Validates options.
    /// </summary>
    public StructuralTreeDiffOptions Validate()
    {
        if (MaximumNodeVisits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumNodeVisits), "Maximum node visits must be positive.");
        }

        if (MaximumDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumDepth), "Maximum depth must be positive.");
        }

        return this;
    }
}

/// <summary>
/// Result of a structural tree diff.
/// </summary>
/// <param name="Operations">Deterministically ordered operations.</param>
/// <param name="NodeVisits">Number of node visits performed.</param>
/// <param name="LimitReached">True when complexity limits stopped the diff early.</param>
public sealed record StructuralTreeDiffResult(
    IReadOnlyList<StructuralDiffOperation> Operations,
    int NodeVisits,
    bool LimitReached)
{
    /// <summary>
    /// Renders a human-readable summary of operations.
    /// </summary>
    public string ToHumanReadable()
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"operations={Operations.Count}; visits={NodeVisits}; limitReached={LimitReached}");
        builder.AppendLine();
        foreach (StructuralDiffOperation operation in Operations)
        {
            builder.Append(operation.Kind);
            builder.Append(' ');
            builder.Append(operation.Path);
            if (operation.FromPath is not null)
            {
                builder.Append(" from ");
                builder.Append(operation.FromPath);
            }

            if (operation.OldValue is not null)
            {
                builder.Append(" old=");
                builder.Append(operation.OldValue);
            }

            if (operation.NewValue is not null)
            {
                builder.Append(" new=");
                builder.Append(operation.NewValue);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders machine-readable canonical JSON for the diff result.
    /// </summary>
    public byte[] ToCanonicalUtf8Json()
    {
        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream);
        writer.WriteStartObject();
        writer.WriteNumber("nodeVisits", NodeVisits);
        writer.WriteBoolean("limitReached", LimitReached);
        writer.WriteStartArray("operations");
        foreach (StructuralDiffOperation operation in Operations)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", operation.Kind.ToString());
            writer.WriteString("path", operation.Path);
            if (operation.FromPath is not null)
            {
                writer.WriteString("fromPath", operation.FromPath);
            }

            if (operation.OldValue is not null)
            {
                writer.WriteString("oldValue", operation.OldValue);
            }

            if (operation.NewValue is not null)
            {
                writer.WriteString("newValue", operation.NewValue);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToArray();
    }
}

/// <summary>
/// Computes deterministic structural diffs between JSON trees.
/// </summary>
/// <remarks>
/// <para>Supports objects, arrays, and scalars with optional array identity keys.</para>
/// <para>Operations are ordered by path, then kind, then from-path.</para>
/// </remarks>
public static class StructuralTreeDiff
{
    /// <summary>
    /// Diffs <paramref name="left"/> against <paramref name="right"/> using UTF-8 JSON inputs.
    /// </summary>
    public static StructuralTreeDiffResult Diff(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, StructuralTreeDiffOptions? options = null)
    {
        StructuralTreeDiffOptions resolved = (options ?? new StructuralTreeDiffOptions()).Validate();
        using JsonDocument leftDoc = JsonDocument.Parse(left.ToArray());
        using JsonDocument rightDoc = JsonDocument.Parse(right.ToArray());
        return Diff(leftDoc.RootElement, rightDoc.RootElement, resolved);
    }

    /// <summary>
    /// Diffs two <see cref="JsonElement"/> trees.
    /// </summary>
    public static StructuralTreeDiffResult Diff(JsonElement left, JsonElement right, StructuralTreeDiffOptions? options = null)
    {
        StructuralTreeDiffOptions resolved = (options ?? new StructuralTreeDiffOptions()).Validate();
        var context = new DiffContext(resolved);
        DiffNodes(left, right, string.Empty, 0, context);
        IReadOnlyList<StructuralDiffOperation> ordered = context.Operations
            .OrderBy(static op => op.Path, StringComparer.Ordinal)
            .ThenBy(static op => op.Kind)
            .ThenBy(static op => op.FromPath, StringComparer.Ordinal)
            .ToArray();
        return new StructuralTreeDiffResult(ordered, context.NodeVisits, context.LimitReached);
    }

    private static void DiffNodes(JsonElement left, JsonElement right, string path, int depth, DiffContext context)
    {
        if (!context.Visit())
        {
            return;
        }

        if (depth > context.Options.MaximumDepth)
        {
            context.LimitReached = true;
            return;
        }

        if (left.ValueKind != right.ValueKind)
        {
            context.Operations.Add(new StructuralDiffOperation(
                StructuralDiffOperationKind.Replace,
                path,
                OldValue: SerializeCompact(left),
                NewValue: SerializeCompact(right)));
            return;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                DiffObjects(left, right, path, depth, context);
                break;
            case JsonValueKind.Array:
                DiffArrays(left, right, path, depth, context);
                break;
            default:
                if (!JsonElement.DeepEquals(left, right))
                {
                    context.Operations.Add(new StructuralDiffOperation(
                        StructuralDiffOperationKind.Replace,
                        path,
                        OldValue: SerializeCompact(left),
                        NewValue: SerializeCompact(right)));
                }

                break;
        }
    }

    private static void DiffObjects(JsonElement left, JsonElement right, string path, int depth, DiffContext context)
    {
        var leftProps = left.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal).ToArray();
        var rightProps = right.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal).ToArray();
        int li = 0;
        int ri = 0;
        while (li < leftProps.Length && ri < rightProps.Length)
        {
            int cmp = string.Compare(leftProps[li].Name, rightProps[ri].Name, StringComparison.Ordinal);
            if (cmp == 0)
            {
                string childPath = AppendPath(path, leftProps[li].Name);
                DiffNodes(leftProps[li].Value, rightProps[ri].Value, childPath, depth + 1, context);
                li++;
                ri++;
            }
            else if (cmp < 0)
            {
                string childPath = AppendPath(path, leftProps[li].Name);
                context.Operations.Add(new StructuralDiffOperation(
                    StructuralDiffOperationKind.Remove,
                    childPath,
                    OldValue: SerializeCompact(leftProps[li].Value)));
                li++;
            }
            else
            {
                string childPath = AppendPath(path, rightProps[ri].Name);
                context.Operations.Add(new StructuralDiffOperation(
                    StructuralDiffOperationKind.Add,
                    childPath,
                    NewValue: SerializeCompact(rightProps[ri].Value)));
                ri++;
            }
        }

        while (li < leftProps.Length)
        {
            string childPath = AppendPath(path, leftProps[li].Name);
            context.Operations.Add(new StructuralDiffOperation(
                StructuralDiffOperationKind.Remove,
                childPath,
                OldValue: SerializeCompact(leftProps[li].Value)));
            li++;
        }

        while (ri < rightProps.Length)
        {
            string childPath = AppendPath(path, rightProps[ri].Name);
            context.Operations.Add(new StructuralDiffOperation(
                StructuralDiffOperationKind.Add,
                childPath,
                NewValue: SerializeCompact(rightProps[ri].Value)));
            ri++;
        }
    }

    private static void DiffArrays(JsonElement left, JsonElement right, string path, int depth, DiffContext context)
    {
        string? identity = context.Options.ArrayIdentityProperty;
        if (identity is not null && left.EnumerateArray().All(e => e.ValueKind == JsonValueKind.Object)
            && right.EnumerateArray().All(e => e.ValueKind == JsonValueKind.Object))
        {
            DiffIdentityArrays(left, right, path, depth, identity, context);
            return;
        }

        int max = Math.Max(left.GetArrayLength(), right.GetArrayLength());
        for (int i = 0; i < max; i++)
        {
            string indexPath = AppendIndex(path, i);
            bool hasLeft = i < left.GetArrayLength();
            bool hasRight = i < right.GetArrayLength();
            if (hasLeft && hasRight)
            {
                DiffNodes(left[i], right[i], indexPath, depth + 1, context);
            }
            else if (hasLeft)
            {
                context.Operations.Add(new StructuralDiffOperation(
                    StructuralDiffOperationKind.Remove,
                    indexPath,
                    OldValue: SerializeCompact(left[i])));
            }
            else
            {
                context.Operations.Add(new StructuralDiffOperation(
                    StructuralDiffOperationKind.Add,
                    indexPath,
                    NewValue: SerializeCompact(right[i])));
            }
        }
    }

    private static void DiffIdentityArrays(JsonElement left, JsonElement right, string path, int depth, string identity, DiffContext context)
    {
        var leftMap = left.EnumerateArray()
            .Select((element, index) => (Id: GetIdentity(element, identity, index), Index: index, Element: element))
            .ToDictionary(static x => x.Id, static x => x, StringComparer.Ordinal);
        var rightMap = right.EnumerateArray()
            .Select((element, index) => (Id: GetIdentity(element, identity, index), Index: index, Element: element))
            .ToDictionary(static x => x.Id, static x => x, StringComparer.Ordinal);

        foreach (string id in leftMap.Keys.Union(rightMap.Keys, StringComparer.Ordinal).OrderBy(static x => x, StringComparer.Ordinal))
        {
            bool inLeft = leftMap.TryGetValue(id, out var leftEntry);
            bool inRight = rightMap.TryGetValue(id, out var rightEntry);
            string entryPath = AppendPath(path, id);
            if (inLeft && inRight)
            {
                if (leftEntry.Index != rightEntry.Index)
                {
                    context.Operations.Add(new StructuralDiffOperation(
                        StructuralDiffOperationKind.Move,
                        AppendIndex(path, rightEntry.Index),
                        FromPath: AppendIndex(path, leftEntry.Index)));
                }

                DiffNodes(leftEntry.Element, rightEntry.Element, entryPath, depth + 1, context);
            }
            else if (inLeft)
            {
                context.Operations.Add(new StructuralDiffOperation(
                    StructuralDiffOperationKind.Remove,
                    entryPath,
                    OldValue: SerializeCompact(leftEntry.Element)));
            }
            else
            {
                context.Operations.Add(new StructuralDiffOperation(
                    StructuralDiffOperationKind.Add,
                    entryPath,
                    NewValue: SerializeCompact(rightEntry.Element)));
            }
        }
    }

    private static string GetIdentity(JsonElement element, string property, int fallbackIndex)
    {
        if (element.TryGetProperty(property, out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            return idElement.GetString() ?? fallbackIndex.ToString(CultureInfo.InvariantCulture);
        }

        return fallbackIndex.ToString(CultureInfo.InvariantCulture);
    }

    private static string AppendPath(string path, string segment)
    {
        string escaped = segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
        return string.IsNullOrEmpty(path) ? "/" + escaped : path + "/" + escaped;
    }

    private static string AppendIndex(string path, int index) =>
        string.IsNullOrEmpty(path) ? "/" + index.ToString(CultureInfo.InvariantCulture) : path + "/" + index.ToString(CultureInfo.InvariantCulture);

    private static string SerializeCompact(JsonElement element) => element.GetRawText();

    private sealed class DiffContext(StructuralTreeDiffOptions options)
    {
        public StructuralTreeDiffOptions Options { get; } = options;

        public List<StructuralDiffOperation> Operations { get; } = [];

        public int NodeVisits { get; private set; }

        public bool LimitReached { get; set; }

        public bool Visit()
        {
            NodeVisits++;
            if (NodeVisits > Options.MaximumNodeVisits)
            {
                LimitReached = true;
                return false;
            }

            return true;
        }
    }
}
