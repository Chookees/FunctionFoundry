namespace FunctionFoundry.Distributed;

/// <summary>
/// Quorum completion status.
/// </summary>
public enum QuorumStatus
{
    /// <summary>A quorum of agreeing successful responses was reached.</summary>
    Succeeded,

    /// <summary>Responses disagreed before a quorum could be formed.</summary>
    Conflict,

    /// <summary>Insufficient successful responses were received.</summary>
    Failed,
}

/// <summary>
/// Policy describing quorum requirements for replica operations.
/// </summary>
/// <param name="RequiredSuccesses">Minimum successful responses required.</param>
/// <param name="TotalParticipants">Total participant count used for cancellation heuristics.</param>
/// <param name="AllowDeterministicTieBreak">Whether conflicts may be resolved by deterministic selection.</param>
public sealed record QuorumPolicy(int RequiredSuccesses, int TotalParticipants, bool AllowDeterministicTieBreak = false)
{
    /// <summary>
    /// Validates policy values.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when counts are invalid.</exception>
    public void Validate()
    {
        if (RequiredSuccesses <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RequiredSuccesses), "RequiredSuccesses must be positive.");
        }

        if (TotalParticipants <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TotalParticipants), "TotalParticipants must be positive.");
        }

        if (RequiredSuccesses > TotalParticipants)
        {
            throw new ArgumentOutOfRangeException(nameof(RequiredSuccesses), "RequiredSuccesses cannot exceed TotalParticipants.");
        }
    }
}

/// <summary>
/// Evidence describing a failed quorum participant.
/// </summary>
/// <param name="ParticipantIndex">Zero-based participant index.</param>
/// <param name="ErrorMessage">Failure message.</param>
public sealed record QuorumFailureEvidence(int ParticipantIndex, string ErrorMessage);

/// <summary>
/// Result produced by <see cref="QuorumResultAggregator{T}"/>.
/// </summary>
/// <param name="Status">Final quorum status.</param>
/// <param name="Value">Agreed value when status is <see cref="QuorumStatus.Succeeded"/>.</param>
/// <param name="SuccessCount">Number of successful responses.</param>
/// <param name="FailureEvidence">Failure details for unsuccessful participants.</param>
/// <param name="WasDeterministicSelectionApplied">Whether a deterministic tie-break selected the final value.</param>
public sealed record QuorumResult<T>(
    QuorumStatus Status,
    T? Value,
    int SuccessCount,
    IReadOnlyList<QuorumFailureEvidence> FailureEvidence,
    bool WasDeterministicSelectionApplied);

/// <summary>
/// Aggregates replica results under a quorum policy with early completion and cancellation.
/// </summary>
/// <remarks>
/// <para>Deterministic tie-breaking is applied only when <see cref="QuorumPolicy.AllowDeterministicTieBreak"/> is enabled and a strict majority of successes share the same canonical value.</para>
/// <para>Cancelled tasks are best-effort; hosts should treat cancellation as an optimization, not a consensus guarantee.</para>
/// </remarks>
public sealed class QuorumResultAggregator<T>
{
    /// <summary>
    /// Aggregates tasks under the supplied quorum policy.
    /// </summary>
    /// <param name="tasks">Participant tasks. Length must equal <see cref="QuorumPolicy.TotalParticipants"/>.</param>
    /// <param name="policy">Quorum policy.</param>
    /// <param name="comparer">Optional equality comparer for agreement checks.</param>
    /// <param name="cancellationToken">Token observing external cancellation.</param>
    /// <returns>Quorum result with status, value, and evidence.</returns>
    public async Task<QuorumResult<T>> AggregateAsync(
        IReadOnlyList<Task<T>> tasks,
        QuorumPolicy policy,
        IEqualityComparer<T>? comparer = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        if (tasks.Count != policy.TotalParticipants)
        {
            throw new ArgumentException("Task count must equal TotalParticipants.", nameof(tasks));
        }

        comparer ??= EqualityComparer<T>.Default;
        using CancellationTokenSource quorumCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        List<QuorumFailureEvidence> failures = new();
        Dictionary<string, (T Value, int Count, List<int> Indexes)> groups = new(StringComparer.Ordinal);
        int successCount = 0;

        Task<(int Index, T? Value, string? Error)>[] observers = new Task<(int Index, T? Value, string? Error)>[tasks.Count];
        for (int i = 0; i < tasks.Count; i++)
        {
            int index = i;
            observers[i] = ObserveAsync(tasks[i], index, quorumCts.Token);
        }

        List<Task<(int Index, T? Value, string? Error)>> pending = observers.ToList();
        while (pending.Count > 0)
        {
            Task<(int Index, T? Value, string? Error)> finished = await Task.WhenAny(pending).ConfigureAwait(false);
            (int index, T? value, string? error) = await finished.ConfigureAwait(false);
            pending.Remove(finished);

            if (error is not null)
            {
                failures.Add(new QuorumFailureEvidence(index, error));
                if (successCount + pending.Count < policy.RequiredSuccesses)
                {
                    await quorumCts.CancelAsync().ConfigureAwait(false);
                    return new QuorumResult<T>(QuorumStatus.Failed, default, successCount, failures, false);
                }

                continue;
            }

            successCount++;
            string key = CanonicalKey(value, comparer);
            if (!groups.TryGetValue(key, out (T Value, int Count, List<int> Indexes) group))
            {
                group = (value!, 0, new List<int>());
            }

            group.Count++;
            group.Indexes.Add(index);
            groups[key] = group;

            if (group.Count >= policy.RequiredSuccesses)
            {
                await quorumCts.CancelAsync().ConfigureAwait(false);
                return new QuorumResult<T>(QuorumStatus.Succeeded, group.Value, successCount, failures, false);
            }
        }

        if (groups.Values.Any(g => g.Count >= policy.RequiredSuccesses))
        {
            (T Value, int Count, List<int> Indexes) winner = groups.Values
                .OrderByDescending(static g => g.Count)
                .ThenBy(g => CanonicalKey(g.Value, comparer), StringComparer.Ordinal)
                .First();
            return new QuorumResult<T>(QuorumStatus.Succeeded, winner.Value, successCount, failures, policy.AllowDeterministicTieBreak);
        }

        if (groups.Values.Count(static g => g.Count > 0) > 1 && successCount >= policy.RequiredSuccesses)
        {
            return new QuorumResult<T>(QuorumStatus.Conflict, default, successCount, failures, false);
        }

        return new QuorumResult<T>(QuorumStatus.Failed, default, successCount, failures, false);
    }

    private static Task<(int Index, T? Value, string? Error)> ObserveAsync(Task<T> task, int index, CancellationToken cancellationToken)
        => task.ContinueWith<(int Index, T? Value, string? Error)>(
            t =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return (index, default, "Cancelled after quorum decision.");
                }

                if (t.IsFaulted)
                {
                    Exception? inner = t.Exception?.InnerExceptions.FirstOrDefault();
                    return (index, default, inner?.Message ?? "Participant failed.");
                }

                if (t.IsCanceled)
                {
                    return (index, default, "Cancelled after quorum decision.");
                }

                return (index, t.Result, null);
            },
            cancellationToken,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static string CanonicalKey(T? value, IEqualityComparer<T> comparer)
    {
        if (value is null)
        {
            return "\0null";
        }

        if (value is IComparable comparable)
        {
            return "\0" + Convert.ToString(comparable, System.Globalization.CultureInfo.InvariantCulture);
        }

        return "\0" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
