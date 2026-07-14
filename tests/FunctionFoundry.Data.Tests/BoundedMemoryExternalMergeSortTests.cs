using System.Text;
using System.Text.Json;

namespace FunctionFoundry.Data.Tests;

public sealed class BoundedMemoryExternalMergeSortTests
{
    [Fact]
    public async Task Sorts_records_within_memory_budget()
    {
        var codec = new Utf8StringMergeSortCodec(maximumUtf8Bytes: 64);
        string tempDir = Path.Combine(Path.GetTempPath(), "ff-data-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sorter = new BoundedMemoryExternalMergeSort<string>(
            codec,
            new BoundedMemoryExternalMergeSortOptions(MemoryBudgetBytes: 128, TempDirectory: tempDir, MergeFanIn: 2));

        string[] input = ["delta", "alpha", "charlie", "bravo", "alpha"];
        var output = new List<string>();
        ExternalMergeSortStatistics stats = await sorter.SortAsync(
            ToAsync(input),
            StringComparer.Ordinal,
            (record, _) =>
            {
                output.Add(record);
                return ValueTask.CompletedTask;
            });

        Assert.Equal(["alpha", "alpha", "bravo", "charlie", "delta"], output);
        Assert.Equal(input.Length, stats.InputCount);
        Assert.True(stats.RunCount >= 1);
        Assert.Empty(sorter.ScanAbandonedTempRuns(tempDir).Runs);
    }

    [Fact]
    public async Task Stable_sort_preserves_equal_order()
    {
        var codec = new Utf8StringMergeSortCodec(maximumUtf8Bytes: 16);
        var sorter = new BoundedMemoryExternalMergeSort<string>(
            codec,
            new BoundedMemoryExternalMergeSortOptions(MemoryBudgetBytes: 64, StableSort: true));

        string[] input = ["b|1", "a|2", "b|3"];
        var comparer = Comparer<string>.Create(static (left, right) => left[0].CompareTo(right[0]));
        var output = new List<int>();
        await sorter.SortAsync(
            ToAsync(input),
            comparer,
            (record, _) =>
            {
                output.Add(int.Parse(record.AsSpan(2), System.Globalization.CultureInfo.InvariantCulture));
                return ValueTask.CompletedTask;
            });

        Assert.Equal([2, 1, 3], output);
    }

    [Fact]
    public async Task Cancellation_stops_sort()
    {
        var codec = new Utf8StringMergeSortCodec();
        var sorter = new BoundedMemoryExternalMergeSort<string>(codec);
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sorter.SortAsync(ToAsync(["a"]), StringComparer.Ordinal, (_, _) => ValueTask.CompletedTask, cts.Token));
    }

    [Fact]
    public void Abandoned_temp_scan_is_deterministic()
    {
        var codec = new Utf8StringMergeSortCodec();
        string tempDir = Path.Combine(Path.GetTempPath(), "ff-data-abandon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string pathA = Path.Combine(tempDir, "ff-merge-left.run");
        string pathB = Path.Combine(tempDir, "ff-merge-right.run");
        File.WriteAllText(pathB, "b");
        File.WriteAllText(pathA, "a");

        var sorter = new BoundedMemoryExternalMergeSort<string>(
            codec,
            new BoundedMemoryExternalMergeSortOptions(TempDirectory: tempDir, DeleteTempRunsOnSuccess: false));

        AbandonedTempRunReport report = sorter.ScanAbandonedTempRuns(tempDir);
        Assert.Equal(2, report.Runs.Count);
        Assert.True(report.Runs[0].FilePath.CompareTo(report.Runs[1].FilePath, StringComparison.Ordinal) < 0);
    }

    private static async IAsyncEnumerable<string> ToAsync(IEnumerable<string> source)
    {
        foreach (string item in source)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
