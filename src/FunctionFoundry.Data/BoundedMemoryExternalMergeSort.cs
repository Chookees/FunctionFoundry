namespace FunctionFoundry.Data;

/// <summary>
/// Performs bounded-memory external merge sort over generic records with temp-run recovery diagnostics.
/// </summary>
/// <typeparam name="T">Record type.</typeparam>
/// <remarks>
/// <para>Algorithm: in-memory run accumulation until memory budget is reached, write sorted runs to disk, k-way merge with a min-heap.</para>
/// <para>Guarantees: respects memory budget per run buffer; temp runs are tracked; successful sorts optionally delete temps.</para>
/// <para>Non-guarantees: does not encrypt temp files; recovery diagnostics do not delete abandoned files.</para>
/// <para>Thread safety: instance methods are not thread-safe.</para>
/// </remarks>
public sealed class BoundedMemoryExternalMergeSort<T>
{
    private readonly IMergeSortRecordCodec<T> _codec;
    private readonly BoundedMemoryExternalMergeSortOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="BoundedMemoryExternalMergeSort{T}"/> class.
    /// </summary>
    /// <param name="codec">Record codec. Must not be null.</param>
    /// <param name="options">Optional sort options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="codec"/> is null.</exception>
    public BoundedMemoryExternalMergeSort(IMergeSortRecordCodec<T> codec, BoundedMemoryExternalMergeSortOptions? options = null)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _options = (options ?? new BoundedMemoryExternalMergeSortOptions()).Validate();
    }

    /// <summary>
    /// Sorts records from <paramref name="source"/> and writes them to <paramref name="output"/>.
    /// </summary>
    /// <param name="source">Input records.</param>
    /// <param name="comparer">Ordering comparer. When null, <see cref="Comparer{T}.Default"/> is used.</param>
    /// <param name="output">Output sink invoked once per sorted record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sort statistics.</returns>
    public async Task<ExternalMergeSortStatistics> SortAsync(
        IAsyncEnumerable<T> source,
        IComparer<T>? comparer,
        Func<T, CancellationToken, ValueTask> output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();

        Comparer<SortEntry> entryComparer = CreateEntryComparer(comparer ?? Comparer<T>.Default);
        string tempDirectory = _options.TempDirectory ?? Path.GetTempPath();
        Directory.CreateDirectory(tempDirectory);

        var runPaths = new List<string>();
        var currentRun = new List<SortEntry>();
        int inputCount = 0;
        long sequence = 0;
        int maxRecordsPerRun = Math.Max(1, _options.MemoryBudgetBytes / Math.Max(1, _codec.MaximumSerializedBytes));

        try
        {
            await foreach (T record in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                currentRun.Add(new SortEntry(record, sequence++));
                inputCount++;
                if (currentRun.Count >= maxRecordsPerRun)
                {
                    runPaths.Add(await FlushRunAsync(currentRun, entryComparer, tempDirectory, cancellationToken).ConfigureAwait(false));
                    currentRun.Clear();
                }
            }

            if (currentRun.Count > 0)
            {
                runPaths.Add(await FlushRunAsync(currentRun, entryComparer, tempDirectory, cancellationToken).ConfigureAwait(false));
                currentRun.Clear();
            }

            if (runPaths.Count == 0)
            {
                return new ExternalMergeSortStatistics(0, 0, 0);
            }

            int peakOpenRuns = runPaths.Count == 1 ? 1 : Math.Min(runPaths.Count, _options.MergeFanIn);
            await KWayMergeToOutputAsync(runPaths, entryComparer, output, cancellationToken).ConfigureAwait(false);
            return new ExternalMergeSortStatistics(inputCount, runPaths.Count, peakOpenRuns);
        }
        finally
        {
            if (_options.DeleteTempRunsOnSuccess)
            {
                foreach (string path in runPaths)
                {
                    TryDelete(path);
                }
            }
        }
    }

    /// <summary>
    /// Scans <paramref name="tempDirectory"/> for abandoned temp runs matching the configured prefix.
    /// </summary>
    /// <param name="tempDirectory">Directory to scan. When null, configured temp directory is used.</param>
    /// <returns>Deterministic diagnostic report.</returns>
    public AbandonedTempRunReport ScanAbandonedTempRuns(string? tempDirectory = null)
    {
        string directory = tempDirectory ?? _options.TempDirectory ?? Path.GetTempPath();
        if (!Directory.Exists(directory))
        {
            return new AbandonedTempRunReport(directory, _options.TempFilePrefix, Array.Empty<AbandonedTempRunDiagnostic>());
        }

        var diagnostics = new List<AbandonedTempRunDiagnostic>();
        foreach (string path in Directory.EnumerateFiles(directory, _options.TempFilePrefix + "*").OrderBy(static p => p, StringComparer.Ordinal))
        {
            FileInfo info = new(path);
            diagnostics.Add(new AbandonedTempRunDiagnostic(path, info.CreationTimeUtc, info.Length));
        }

        return new AbandonedTempRunReport(directory, _options.TempFilePrefix, diagnostics);
    }

    private Comparer<SortEntry> CreateEntryComparer(IComparer<T> comparer)
    {
        return Comparer<SortEntry>.Create((a, b) =>
        {
            int cmp = comparer.Compare(a.Record, b.Record);
            if (cmp != 0)
            {
                return cmp;
            }

            if (!_options.StableSort)
            {
                return 0;
            }

            int runCmp = a.RunIndex.CompareTo(b.RunIndex);
            return runCmp != 0 ? runCmp : a.Sequence.CompareTo(b.Sequence);
        });
    }

    private async Task<string> FlushRunAsync(List<SortEntry> run, Comparer<SortEntry> comparer, string tempDirectory, CancellationToken cancellationToken)
    {
        if (_options.StableSort)
        {
            List<SortEntry> sorted = run.OrderBy(static entry => entry, comparer).ToList();
            run.Clear();
            run.AddRange(sorted);
        }
        else
        {
            run.Sort(comparer);
        }
        string path = Path.Combine(tempDirectory, _options.TempFilePrefix + Guid.NewGuid().ToString("N") + ".run");
        await using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        foreach (SortEntry entry in run)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _codec.WriteAsync(entry.Record, stream, cancellationToken).ConfigureAwait(false);
        }

        return path;
    }

    private async Task KWayMergeToOutputAsync(
        List<string> runPaths,
        Comparer<SortEntry> comparer,
        Func<T, CancellationToken, ValueTask> output,
        CancellationToken cancellationToken)
    {
        if (runPaths.Count == 1)
        {
            await EmitRunAsync(runPaths[0], output, cancellationToken).ConfigureAwait(false);
            return;
        }

        var readers = new RunReader[runPaths.Count];
        for (int i = 0; i < runPaths.Count; i++)
        {
            var stream = new FileStream(runPaths[i], FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            readers[i] = new RunReader(stream, _codec, i);
            await readers[i].AdvanceAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var heap = new PriorityQueue<int, SortEntry>(runPaths.Count, comparer);
            for (int i = 0; i < readers.Length; i++)
            {
                if (readers[i].Current is SortEntry entry)
                {
                    heap.Enqueue(i, entry);
                }
            }

            while (heap.TryDequeue(out int runIndex, out SortEntry best))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await output(best.Record, cancellationToken).ConfigureAwait(false);

                await readers[runIndex].AdvanceAsync(cancellationToken).ConfigureAwait(false);
                if (readers[runIndex].Current is SortEntry next)
                {
                    heap.Enqueue(runIndex, next);
                }
            }
        }
        finally
        {
            foreach (RunReader reader in readers)
            {
                await reader.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task EmitRunAsync(string runPath, Func<T, CancellationToken, ValueTask> output, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(runPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long sequence = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            T? record = await _codec.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                break;
            }

            _ = sequence;
            await output(record, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct SortEntry(T Record, long Sequence, int RunIndex = -1);

    private sealed class RunReader : IAsyncDisposable
    {
        private readonly FileStream _stream;
        private readonly IMergeSortRecordCodec<T> _codec;
        private readonly int _runIndex;
        private long _sequence;

        public RunReader(FileStream stream, IMergeSortRecordCodec<T> codec, int runIndex)
        {
            _stream = stream;
            _codec = codec;
            _runIndex = runIndex;
        }

        public SortEntry? Current { get; private set; }

        public async ValueTask AdvanceAsync(CancellationToken cancellationToken)
        {
            T? record = await _codec.ReadAsync(_stream, cancellationToken).ConfigureAwait(false);
            Current = record is null ? null : new SortEntry(record, _sequence++, _runIndex);
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
