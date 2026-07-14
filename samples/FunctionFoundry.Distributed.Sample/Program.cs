using FunctionFoundry.Distributed;

WeightedNode[] nodes =
[
    new("node-a", 2),
    new("node-b", 1),
    new("node-c", 1),
];
var hasher = new WeightedRendezvousHasher();
WeightedNode selected = hasher.Select("partition-42"u8.ToArray(), nodes);
Console.WriteLine($"Rendezvous selected: {selected.Id}");

var clock = new VectorClock();
clock.Increment("node-a");
clock.Merge(VectorClock.Deserialize("node-b=2"));
Console.WriteLine($"Vector clock: {clock.Serialize()} relation={clock.CompareTo(new VectorClock())}");

var detector = new PhiAccrualFailureDetector(new SystemClock());
detector.RecordHeartbeat();
Console.WriteLine($"Phi snapshot warming={detector.GetSnapshot().IsWarmingUp}");

var aggregator = new QuorumResultAggregator<string>();
QuorumResult<string> quorum = await aggregator.AggregateAsync(
    [Task.FromResult("ok"), Task.FromResult("ok"), Task.FromResult("late")],
    new QuorumPolicy(2, 3));
Console.WriteLine($"Quorum status={quorum.Status} value={quorum.Value}");
