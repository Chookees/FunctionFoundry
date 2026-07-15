namespace FunctionFoundry.Scheduling;

/// <summary>
/// A schedulable task node.
/// </summary>
/// <param name="Id">Unique task identifier.</param>
/// <param name="Duration">Task duration in arbitrary time units.</param>
/// <param name="Dependencies">Task ids that must finish before this task starts.</param>
public sealed record ScheduleTask(string Id, int Duration, IReadOnlyList<string> Dependencies);

/// <summary>
/// Validation error for schedule definitions.
/// </summary>
/// <param name="Message">Error message.</param>
/// <param name="Cycle">Detected cycle path when applicable.</param>
public sealed record ScheduleValidationError(string Message, IReadOnlyList<string>? Cycle = null);

/// <summary>
/// A scheduled task with critical-path metrics.
/// </summary>
/// <param name="TaskId">Task identifier.</param>
/// <param name="Duration">Task duration.</param>
/// <param name="EarlyStart">Early start time.</param>
/// <param name="EarlyFinish">Early finish time.</param>
/// <param name="LateStart">Late start time.</param>
/// <param name="LateFinish">Late finish time.</param>
/// <param name="Slack">Total slack (late start minus early start).</param>
/// <param name="IsCritical">Whether the task lies on the critical path.</param>
public sealed record ScheduledTaskMetrics(
    string TaskId,
    int Duration,
    int EarlyStart,
    int EarlyFinish,
    int LateStart,
    int LateFinish,
    int Slack,
    bool IsCritical);

/// <summary>
/// Result of critical-path scheduling.
/// </summary>
/// <param name="Tasks">Per-task metrics in deterministic task-id order.</param>
/// <param name="CriticalPath">Task ids on the critical path.</param>
/// <param name="ProjectDuration">Overall project duration.</param>
/// <param name="UsedResourceHeuristic">Whether a non-optimal resource heuristic was applied.</param>
/// <param name="IsOptimal">Whether the schedule is proven optimal.</param>
public sealed record CriticalPathScheduleResult(
    IReadOnlyList<ScheduledTaskMetrics> Tasks,
    IReadOnlyList<string> CriticalPath,
    int ProjectDuration,
    bool UsedResourceHeuristic,
    bool IsOptimal);

/// <summary>
/// Options for critical-path scheduling.
/// </summary>
public sealed class CriticalPathSchedulerOptions
{
    /// <summary>
    /// Gets or sets the optional per-task resource demand used by the leveling heuristic.
    /// </summary>
    public IReadOnlyDictionary<string, int>? ResourceDemandByTaskId { get; set; }

    /// <summary>
    /// Gets or sets the resource capacity for the optional leveling heuristic. Defaults to 1.
    /// </summary>
    public int ResourceCapacity { get; set; } = 1;

    /// <summary>
    /// Validates scheduler options.
    /// </summary>
    public void Validate()
    {
        if (ResourceCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ResourceCapacity), "ResourceCapacity must be positive.");
        }
    }
}

/// <summary>
/// Critical-path method scheduler with dependency validation and optional resource leveling heuristic.
/// </summary>
/// <remarks>
/// <para>When resource leveling is enabled, resulting schedules are marked non-optimal because the heuristic may extend task starts beyond CPM limits.</para>
/// </remarks>
public sealed class CriticalPathScheduler
{
    private readonly CriticalPathSchedulerOptions _options;

    /// <summary>
    /// Initializes a new scheduler.
    /// </summary>
    /// <param name="options">Optional scheduler options.</param>
    public CriticalPathScheduler(CriticalPathSchedulerOptions? options = null)
    {
        _options = options ?? new CriticalPathSchedulerOptions();
        _options.Validate();
    }

    /// <summary>
    /// Validates tasks and returns structural errors including dependency cycles.
    /// </summary>
    /// <param name="tasks">Tasks to validate.</param>
    /// <returns>Validation errors; empty when valid.</returns>
    public IReadOnlyList<ScheduleValidationError> Validate(IReadOnlyList<ScheduleTask> tasks)
    {
        _ = _options;
        ArgumentNullException.ThrowIfNull(tasks);
        List<ScheduleValidationError> errors = new();
        Dictionary<string, ScheduleTask> byId = new(StringComparer.Ordinal);
        foreach (ScheduleTask task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                errors.Add(new ScheduleValidationError("Task id must not be null or empty."));
                continue;
            }

            if (task.Duration < 0)
            {
                errors.Add(new ScheduleValidationError($"Task '{task.Id}' duration must be non-negative."));
            }

            if (!byId.TryAdd(task.Id, task))
            {
                errors.Add(new ScheduleValidationError($"Duplicate task id '{task.Id}'."));
            }
        }

        foreach (ScheduleTask task in tasks)
        {
            foreach (string dependency in task.Dependencies)
            {
                if (!byId.ContainsKey(dependency))
                {
                    errors.Add(new ScheduleValidationError($"Task '{task.Id}' depends on unknown task '{dependency}'."));
                }
            }
        }

        // Cycle detection requires a closed graph; skip it when unknown dependencies already invalidate structure.
        if (errors.Count == 0)
        {
            IReadOnlyList<string>? cycle = DetectCycle(tasks);
            if (cycle is not null)
            {
                errors.Add(new ScheduleValidationError("Dependency cycle detected.", cycle));
            }
        }

        return errors;
    }

    /// <summary>
    /// Schedules tasks using the critical-path method.
    /// </summary>
    /// <param name="tasks">Acyclic task graph.</param>
    /// <returns>Schedule metrics and critical path.</returns>
    /// <exception cref="InvalidOperationException">Thrown when validation fails.</exception>
    public CriticalPathScheduleResult Schedule(IReadOnlyList<ScheduleTask> tasks)
    {
        IReadOnlyList<ScheduleValidationError> errors = Validate(tasks);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(errors[0].Message);
        }

        Dictionary<string, ScheduleTask> byId = tasks.ToDictionary(static t => t.Id, static t => t, StringComparer.Ordinal);
        List<string> topologicalOrder = TopologicalSort(byId);
        Dictionary<string, int> earlyStart = new(StringComparer.Ordinal);
        Dictionary<string, int> earlyFinish = new(StringComparer.Ordinal);
        foreach (string id in topologicalOrder)
        {
            ScheduleTask task = byId[id];
            int es = task.Dependencies.Count == 0
                ? 0
                : task.Dependencies.Max(dep => earlyFinish[dep]);
            earlyStart[id] = es;
            earlyFinish[id] = es + task.Duration;
        }

        int projectDuration = earlyFinish.Values.DefaultIfEmpty(0).Max();
        Dictionary<string, int> lateFinish = new(StringComparer.Ordinal);
        Dictionary<string, int> lateStart = new(StringComparer.Ordinal);
        foreach (string id in topologicalOrder.AsEnumerable().Reverse())
        {
            ScheduleTask task = byId[id];
            List<string> successors = byId.Values
                .Where(t => t.Dependencies.Contains(id, StringComparer.Ordinal))
                .Select(static t => t.Id)
                .ToList();
            int lf = successors.Count == 0 ? projectDuration : successors.Min(dep => lateStart[dep]);
            lateFinish[id] = lf;
            lateStart[id] = lf - task.Duration;
        }

        bool usedHeuristic = false;
        if (_options.ResourceDemandByTaskId is not null && _options.ResourceDemandByTaskId.Count > 0)
        {
            usedHeuristic = ApplyResourceHeuristic(byId, earlyStart, earlyFinish, ref projectDuration);
        }

        List<ScheduledTaskMetrics> metrics = new();
        List<string> criticalPath = new();
        foreach (string id in byId.Keys.OrderBy(static k => k, StringComparer.Ordinal))
        {
            int slack = lateStart[id] - earlyStart[id];
            bool critical = slack == 0;
            if (critical)
            {
                criticalPath.Add(id);
            }

            metrics.Add(new ScheduledTaskMetrics(
                id,
                byId[id].Duration,
                earlyStart[id],
                earlyFinish[id],
                lateStart[id],
                lateFinish[id],
                slack,
                critical));
        }

        return new CriticalPathScheduleResult(metrics, criticalPath, projectDuration, usedHeuristic, !usedHeuristic);
    }

    private static List<string> TopologicalSort(Dictionary<string, ScheduleTask> byId)
    {
        Dictionary<string, int> incoming = byId.ToDictionary(
            static kv => kv.Key,
            static kv => kv.Value.Dependencies.Count,
            StringComparer.Ordinal);

        SortedSet<string> ready = new(byId.Keys.Where(id => incoming[id] == 0), StringComparer.Ordinal);
        List<string> order = new(byId.Count);
        while (ready.Count > 0)
        {
            string id = ready.Min!;
            ready.Remove(id);
            order.Add(id);
            foreach (ScheduleTask task in byId.Values.Where(t => t.Dependencies.Contains(id, StringComparer.Ordinal)))
            {
                incoming[task.Id]--;
                if (incoming[task.Id] == 0)
                {
                    ready.Add(task.Id);
                }
            }
        }

        if (order.Count != byId.Count)
        {
            throw new InvalidOperationException("Task graph contains a cycle.");
        }

        return order;
    }

    private static IReadOnlyList<string>? DetectCycle(IReadOnlyList<ScheduleTask> tasks)
    {
        Dictionary<string, ScheduleTask> byId = tasks.ToDictionary(static t => t.Id, static t => t, StringComparer.Ordinal);
        HashSet<string> visiting = new(StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal);
        List<string> path = new();

        foreach (string id in byId.Keys.OrderBy(static k => k, StringComparer.Ordinal))
        {
            if (visited.Contains(id))
            {
                continue;
            }

            IReadOnlyList<string>? cycle = Dfs(id, byId, visiting, visited, path);
            if (cycle is not null)
            {
                return cycle;
            }
        }

        return null;
    }

    private static IReadOnlyList<string>? Dfs(
        string id,
        Dictionary<string, ScheduleTask> byId,
        HashSet<string> visiting,
        HashSet<string> visited,
        List<string> path)
    {
        visiting.Add(id);
        path.Add(id);
        foreach (string dependency in byId[id].Dependencies.OrderBy(static d => d, StringComparer.Ordinal))
        {
            if (!visited.Contains(dependency))
            {
                if (visiting.Contains(dependency))
                {
                    int start = path.IndexOf(dependency);
                    List<string> cycle = path.Skip(start).Append(dependency).ToList();
                    return cycle;
                }

                IReadOnlyList<string>? found = Dfs(dependency, byId, visiting, visited, path);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        visiting.Remove(id);
        visited.Add(id);
        path.RemoveAt(path.Count - 1);
        return null;
    }

    private bool ApplyResourceHeuristic(
        Dictionary<string, ScheduleTask> byId,
        Dictionary<string, int> earlyStart,
        Dictionary<string, int> earlyFinish,
        ref int projectDuration)
    {
        bool changed = false;
        List<string> order = byId.Keys.OrderBy(static k => k, StringComparer.Ordinal).ToList();
        foreach (string id in order)
        {
            if (!_options.ResourceDemandByTaskId!.TryGetValue(id, out int demand) || demand <= _options.ResourceCapacity)
            {
                continue;
            }

            int delay = demand - _options.ResourceCapacity;
            earlyStart[id] += delay;
            earlyFinish[id] += delay;
            changed = true;
        }

        projectDuration = earlyFinish.Values.Max();
        return changed;
    }
}
