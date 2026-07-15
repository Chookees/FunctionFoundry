namespace FunctionFoundry.Distributed.Tests;

public sealed class WeightedRendezvousExtraTests
{
    [Fact]
    public void Empty_membership_throws()
    {
        var hasher = new WeightedRendezvousHasher();
        Assert.ThrowsAny<ArgumentException>(() => hasher.Select("k"u8, []));
    }

    [Fact]
    public void Same_key_selects_same_node_deterministically()
    {
        var hasher = new WeightedRendezvousHasher();
        WeightedNode[] nodes =
        [
            new("n1", 1),
            new("n2", 1),
            new("n3", 1),
        ];
        WeightedNode a = hasher.Select("user-42"u8, nodes);
        WeightedNode b = hasher.Select("user-42"u8, nodes);
        Assert.Equal(a.Id, b.Id);
    }
}
