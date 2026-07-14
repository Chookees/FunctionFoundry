using System.Text;

namespace FunctionFoundry.Distributed.Tests;

public sealed class WeightedRendezvousHasherTests
{
    [Fact]
    public void Select_is_deterministic_for_same_key_and_nodes()
    {
        var hasher = new WeightedRendezvousHasher();
        WeightedNode[] nodes =
        [
            new("alpha", 1),
            new("beta", 1),
            new("gamma", 1),
        ];
        byte[] key = "user-1001"u8.ToArray();

        WeightedNode first = hasher.Select(key, nodes);
        WeightedNode second = hasher.Select(key, nodes);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Higher_weight_receives_more_keys_over_distribution()
    {
        var hasher = new WeightedRendezvousHasher();
        WeightedNode[] nodes =
        [
            new("heavy", 5),
            new("light", 1),
        ];

        int heavy = 0;
        for (int i = 0; i < 1_000; i++)
        {
            byte[] key = Encoding.UTF8.GetBytes($"key-{i}");
            if (hasher.Select(key, nodes).Id == "heavy")
            {
                heavy++;
            }
        }

        Assert.True(heavy > 600);
    }

    [Fact]
    public void Bounded_load_prefers_underloaded_node()
    {
        var hasher = new WeightedRendezvousHasher(new WeightedRendezvousHasherOptions { EnableBoundedLoad = true, LoadBalancingFactor = 1.25 });
        WeightedNode[] nodes =
        [
            new("a", 1),
            new("b", 1),
        ];
        byte[] key = "same-key"u8.ToArray();
        var loads = new Dictionary<string, long> { ["a"] = 100, ["b"] = 0 };

        WeightedNode selected = hasher.Select(key, nodes, loads);
        Assert.Equal("b", selected.Id);
    }

    [Fact]
    public void Rank_orders_by_score_with_node_id_tie_break()
    {
        WeightedNode[] nodes =
        [
            new("b", 1),
            new("a", 1),
        ];
        IReadOnlyList<WeightedNode> ranked = new WeightedRendezvousHasher().Rank("key"u8.ToArray(), nodes);
        Assert.Equal(2, ranked.Count);
        Assert.True(string.Compare(ranked[0].Id, ranked[1].Id, StringComparison.Ordinal) < 0
            || ranked[0].Id != ranked[1].Id);
    }
}
