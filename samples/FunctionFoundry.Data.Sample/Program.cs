using FunctionFoundry.Data;

string tempDir = Path.Combine(Path.GetTempPath(), "ff-data-sample-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDir);
var sorter = new BoundedMemoryExternalMergeSort<string>(
    new Utf8StringMergeSortCodec(),
    new BoundedMemoryExternalMergeSortOptions(MemoryBudgetBytes: 256, TempDirectory: tempDir));

List<string> sorted = [];
ExternalMergeSortStatistics stats = await sorter.SortAsync(
    CreateInput(["z", "a", "m", "a"]),
    StringComparer.Ordinal,
    (record, _) =>
    {
        sorted.Add(record);
        return ValueTask.CompletedTask;
    });
Console.WriteLine($"Sorted: {string.Join(", ", sorted)}; runs={stats.RunCount}");

StructuralTreeDiffResult diff = StructuralTreeDiff.Diff(
    """{"user":"alice","roles":["admin"]}"""u8,
    """{"user":"alice","roles":["admin","reader"]}"""u8);
Console.WriteLine(diff.ToHumanReadable());

ThreeWayMergeResult merge = ThreeWayMerge.Merge(
    """{"version":1,"name":"base"}"""u8,
    """{"version":2,"name":"local"}"""u8,
    """{"version":2,"name":"remote"}"""u8);
Console.WriteLine(merge.ToHumanReadable());

var left = new[] { new TemporalRecord<string, string>("tenant", new TemporalInterval<string>("2020-01-01", "2025-01-01"), "plan-a") };
var right = new[] { new TemporalRecord<string, string>("tenant", new TemporalInterval<string>("2024-01-01", "2026-01-01"), "plan-b") };
TemporalIntervalJoinResult<string, string, string> join = TemporalIntervalJoin.Join(left, right);
Console.WriteLine($"Interval matches: {join.Matches.Count}");

static async IAsyncEnumerable<string> CreateInput(IEnumerable<string> values)
{
    foreach (string value in values)
    {
        yield return value;
        await Task.Yield();
    }
}
