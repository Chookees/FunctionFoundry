namespace FunctionFoundry.Resilience;

/// <summary>
/// Persisted checkpoint state for a batch run.
/// </summary>
/// <param name="RunId">Stable run identifier.</param>
/// <param name="CompletedItemIds">Item identifiers completed successfully.</param>
/// <param name="FailedItemIds">Item identifiers that failed.</param>
/// <param name="LastUpdatedUtc">UTC timestamp of the last checkpoint write.</param>
public sealed record BatchCheckpointState(
    string RunId,
    IReadOnlyList<string> CompletedItemIds,
    IReadOnlyList<string> FailedItemIds,
    DateTimeOffset LastUpdatedUtc);

/// <summary>
/// Outcome for a single batch item.
/// </summary>
/// <param name="ItemId">Deterministic item identifier.</param>
/// <param name="Succeeded">Whether processing succeeded.</param>
/// <param name="ErrorMessage">Optional error message.</param>
public sealed record BatchItemResult(string ItemId, bool Succeeded, string? ErrorMessage = null);

/// <summary>
/// Aggregate result of a checkpointed batch execution.
/// </summary>
/// <param name="RunId">Run identifier.</param>
/// <param name="Completed">Successfully completed items in this invocation.</param>
/// <param name="Failed">Failed items in this invocation.</param>
/// <param name="SkippedAsCompleted">Items skipped because they were already completed in a prior checkpoint.</param>
/// <param name="ResumedFromCheckpoint">Whether execution resumed from persisted state.</param>
public sealed record BatchExecutionResult(
    string RunId,
    IReadOnlyList<BatchItemResult> Completed,
    IReadOnlyList<BatchItemResult> Failed,
    IReadOnlyList<string> SkippedAsCompleted,
    bool ResumedFromCheckpoint);

/// <summary>
/// Options for <see cref="CheckpointedBatchExecutor"/>.
/// </summary>
public sealed class CheckpointedBatchExecutorOptions
{
    /// <summary>
    /// Gets or sets the maximum number of concurrently processed items. Defaults to 4.
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Gets or sets whether callers must mark operations as idempotent. Defaults to <see langword="true"/>.
    /// </summary>
    public bool RequireExplicitIdempotency { get; set; } = true;

    /// <summary>
    /// Validates option ranges.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when numeric options are out of range.</exception>
    public void Validate()
    {
        if (MaxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrency), "MaxConcurrency must be positive.");
        }
    }
}

/// <summary>
/// Persists batch checkpoints for resumable execution.
/// </summary>
public interface ICheckpointStore
{
    /// <summary>
    /// Loads checkpoint state for a run.
    /// </summary>
    /// <param name="runId">Run identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Checkpoint state or <see langword="null"/> when none exists.</returns>
    Task<BatchCheckpointState?> LoadAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists checkpoint state for a run.
    /// </summary>
    /// <param name="state">Checkpoint state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(BatchCheckpointState state, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory checkpoint store for tests and ephemeral runs.
/// </summary>
public sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly Dictionary<string, BatchCheckpointState> _states = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<BatchCheckpointState?> LoadAsync(string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        _states.TryGetValue(runId, out BatchCheckpointState? state);
        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public Task SaveAsync(BatchCheckpointState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.RunId);
        _states[state.RunId] = state;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Executes batch work with bounded concurrency, checkpoint persistence, and explicit partial-failure handling.
/// </summary>
public sealed class CheckpointedBatchExecutor
{
    private readonly ICheckpointStore _store;
    private readonly CheckpointedBatchExecutorOptions _options;

    /// <summary>
    /// Initializes a new executor.
    /// </summary>
    /// <param name="store">Checkpoint persistence abstraction.</param>
    /// <param name="options">Executor options.</param>
    public CheckpointedBatchExecutor(ICheckpointStore store, CheckpointedBatchExecutorOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? new CheckpointedBatchExecutorOptions();
        _options.Validate();
    }

    /// <summary>
    /// Gets the configured options.
    /// </summary>
    public CheckpointedBatchExecutorOptions Options => _options;

    /// <summary>
    /// Executes batch items with checkpointing. Each item is attempted at most once per run unless it previously failed.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="runId">Stable run identifier.</param>
    /// <param name="items">Items to process.</param>
    /// <param name="itemIdSelector">Deterministic item id selector.</param>
    /// <param name="processAsync">Item processor.</param>
    /// <param name="isIdempotent">Whether item processing is idempotent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregate execution result.</returns>
    public async Task<BatchExecutionResult> ExecuteAsync<TItem>(
        string runId,
        IReadOnlyList<TItem> items,
        Func<TItem, string> itemIdSelector,
        Func<TItem, CancellationToken, Task> processAsync,
        bool isIdempotent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(itemIdSelector);
        ArgumentNullException.ThrowIfNull(processAsync);
        if (_options.RequireExplicitIdempotency && !isIdempotent)
        {
            throw new InvalidOperationException("Batch processing requires idempotent handlers when checkpointing is enabled.");
        }

        BatchCheckpointState? existing = await _store.LoadAsync(runId, cancellationToken).ConfigureAwait(false);
        HashSet<string> completed = existing?.CompletedItemIds.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> failed = existing?.FailedItemIds.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        bool resumed = existing is not null;

        List<BatchItemResult> completedNow = [];
        List<BatchItemResult> failedNow = [];
        List<string> skipped = [];

        List<(string Id, TItem Item)> pending = [];
        foreach (TItem item in items)
        {
            string id = itemIdSelector(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (completed.Contains(id))
            {
                skipped.Add(id);
                continue;
            }

            if (!failed.Contains(id))
            {
                pending.Add((id, item));
            }
            else
            {
                pending.Add((id, item));
            }
        }

        using SemaphoreSlim gate = new(_options.MaxConcurrency, _options.MaxConcurrency);
        List<Task> workers = [];
        object sync = new();

        foreach ((string id, TItem item) in pending)
        {
            workers.Add(ProcessOneAsync(id, item));
        }

        await Task.WhenAll(workers).ConfigureAwait(false);

        BatchCheckpointState checkpoint = new(
            runId,
            completed.Order(StringComparer.Ordinal).ToArray(),
            failed.Order(StringComparer.Ordinal).ToArray(),
            DateTimeOffset.UtcNow);
        await _store.SaveAsync(checkpoint, cancellationToken).ConfigureAwait(false);

        return new BatchExecutionResult(
            runId,
            completedNow,
            failedNow,
            skipped,
            resumed);

        async Task ProcessOneAsync(string id, TItem item)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await processAsync(item, cancellationToken).ConfigureAwait(false);
                lock (sync)
                {
                    completed.Add(id);
                    failed.Remove(id);
                    completedNow.Add(new BatchItemResult(id, true));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException ex)
            {
                RecordFailure(ex);
            }
            catch (IOException ex)
            {
                RecordFailure(ex);
            }
            catch (TimeoutException ex)
            {
                RecordFailure(ex);
            }
            catch (HttpRequestException ex)
            {
                RecordFailure(ex);
            }
            finally
            {
                gate.Release();
            }

            void RecordFailure(Exception ex)
            {
                lock (sync)
                {
                    failed.Add(id);
                    failedNow.Add(new BatchItemResult(id, false, ex.Message));
                }
            }
        }
    }
}
