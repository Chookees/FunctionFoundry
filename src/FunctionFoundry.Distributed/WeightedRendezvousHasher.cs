using FunctionFoundry.Distributed.Internal;

namespace FunctionFoundry.Distributed;

/// <summary>
/// A weighted node participating in rendezvous selection.
/// </summary>
/// <param name="Id">Stable node identifier used in hashing. Must not be null or empty.</param>
/// <param name="Weight">Positive selection weight. Higher weights receive proportionally more keys.</param>
public sealed record WeightedNode(string Id, double Weight);

/// <summary>
/// Options controlling weighted rendezvous hashing and optional bounded-load enforcement.
/// </summary>
public sealed class WeightedRendezvousHasherOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether bounded-load selection is enabled. Defaults to <see langword="false"/>.
    /// </summary>
    public bool EnableBoundedLoad { get; set; }

    /// <summary>
    /// Gets or sets the load-balancing factor applied when bounded load is enabled. Must be at least 1.0. Defaults to 1.25.
    /// </summary>
    public double LoadBalancingFactor { get; set; } = 1.25;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="LoadBalancingFactor"/> is less than 1.</exception>
    public void Validate()
    {
        if (LoadBalancingFactor < 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(LoadBalancingFactor), "LoadBalancingFactor must be at least 1.0.");
        }
    }
}

/// <summary>
/// Selects nodes using weighted rendezvous hashing for minimal remapping when membership changes.
/// </summary>
/// <remarks>
/// <para>Scores are computed deterministically from SHA-256(key || nodeId) and node weight.</para>
/// <para>When bounded load is enabled, the highest-scoring nodes are considered in order until one is below its capacity.</para>
/// <para>Thread safety: instance methods are thread-safe when callers do not mutate shared node lists concurrently.</para>
/// </remarks>
public sealed class WeightedRendezvousHasher
{
    private readonly WeightedRendezvousHasherOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeightedRendezvousHasher"/> class.
    /// </summary>
    /// <param name="options">Optional hashing options. When null, defaults are used.</param>
    public WeightedRendezvousHasher(WeightedRendezvousHasherOptions? options = null)
    {
        _options = options ?? new WeightedRendezvousHasherOptions();
        _options.Validate();
    }

    /// <summary>
    /// Selects the highest-scoring node for the supplied key.
    /// </summary>
    /// <param name="key">Routing key bytes. May be empty.</param>
    /// <param name="nodes">Candidate nodes with positive weights. Must contain at least one node.</param>
    /// <param name="loads">Optional per-node current loads keyed by node id. Required when bounded load is enabled.</param>
    /// <returns>The selected node.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="nodes"/> is empty or contains invalid entries.</exception>
    public WeightedNode Select(ReadOnlySpan<byte> key, IReadOnlyList<WeightedNode> nodes, IReadOnlyDictionary<string, long>? loads = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count == 0)
        {
            throw new ArgumentException("At least one node is required.", nameof(nodes));
        }

        ValidateNodes(nodes);
        IReadOnlyList<ScoredNode> ranked = RankNodes(key, nodes);

        if (!_options.EnableBoundedLoad)
        {
            return ranked[0].Node;
        }

        loads ??= new Dictionary<string, long>();
        long totalLoad = 0;
        foreach (WeightedNode node in nodes)
        {
            totalLoad += loads.TryGetValue(node.Id, out long load) ? load : 0;
        }

        double averageLoad = (double)totalLoad / nodes.Count;
        double capacityLimit = Math.Ceiling(averageLoad * _options.LoadBalancingFactor);

        foreach (ScoredNode scored in ranked)
        {
            long nodeLoad = loads.TryGetValue(scored.Node.Id, out long load) ? load : 0;
            if (nodeLoad < capacityLimit)
            {
                return scored.Node;
            }
        }

        return ranked[0].Node;
    }

    /// <summary>
    /// Ranks all nodes by rendezvous score in descending deterministic order.
    /// </summary>
    /// <param name="key">Routing key bytes. May be empty.</param>
    /// <param name="nodes">Candidate nodes with positive weights.</param>
    /// <returns>Nodes ordered from highest to lowest score. Ties break on node id ordinal.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="nodes"/> is empty or contains invalid entries.</exception>
    public IReadOnlyList<WeightedNode> Rank(ReadOnlySpan<byte> key, IReadOnlyList<WeightedNode> nodes)
    {
        _ = _options;
        ArgumentNullException.ThrowIfNull(nodes);
        if (nodes.Count == 0)
        {
            throw new ArgumentException("At least one node is required.", nameof(nodes));
        }

        ValidateNodes(nodes);
        return RankNodes(key, nodes).Select(static s => s.Node).ToArray();
    }

    /// <summary>
    /// Computes the deterministic rendezvous score for a node and key.
    /// </summary>
    /// <param name="key">Routing key bytes.</param>
    /// <param name="node">Weighted node. Must have a positive weight.</param>
    /// <returns>Score where higher values are preferred.</returns>
    public static double ComputeScore(ReadOnlySpan<byte> key, WeightedNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (string.IsNullOrWhiteSpace(node.Id))
        {
            throw new ArgumentException("Node id must not be null or empty.", nameof(node));
        }

        if (node.Weight <= 0 || double.IsNaN(node.Weight) || double.IsInfinity(node.Weight))
        {
            throw new ArgumentException("Node weight must be a positive finite number.", nameof(node));
        }

        ulong hash = DeterministicHash.Hash64(key, node.Id);
        double unit = (hash + 1) / (double)(ulong.MaxValue + 1.0);
        return unit * node.Weight;
    }

    private static void ValidateNodes(IReadOnlyList<WeightedNode> nodes)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (WeightedNode node in nodes)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                throw new ArgumentException("Node id must not be null or empty.", nameof(nodes));
            }

            if (node.Weight <= 0 || double.IsNaN(node.Weight) || double.IsInfinity(node.Weight))
            {
                throw new ArgumentException("All node weights must be positive finite numbers.", nameof(nodes));
            }

            if (!seen.Add(node.Id))
            {
                throw new ArgumentException("Duplicate node ids are not allowed.", nameof(nodes));
            }
        }
    }

    private static List<ScoredNode> RankNodes(ReadOnlySpan<byte> key, IReadOnlyList<WeightedNode> nodes)
    {
        List<ScoredNode> scored = new(nodes.Count);
        foreach (WeightedNode node in nodes)
        {
            scored.Add(new ScoredNode(node, ComputeScore(key, node)));
        }

        scored.Sort(static (a, b) =>
        {
            int cmp = b.Score.CompareTo(a.Score);
            return cmp != 0 ? cmp : string.Compare(a.Node.Id, b.Node.Id, StringComparison.Ordinal);
        });

        return scored;
    }

    private sealed record ScoredNode(WeightedNode Node, double Score);
}
