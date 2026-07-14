using FunctionFoundry.Resilience;

using var controller = new AdaptiveConcurrencyController();
AcquireAttemptResult permit = await controller.TryAcquireAsync(TimeSpan.FromSeconds(1));
controller.Release(permit.Permit, new OperationFeedback(true, TimeSpan.FromMilliseconds(12)));
Console.WriteLine($"Concurrency limit: {controller.GetSnapshot().CurrentConcurrencyLimit}");

var hedged = new HedgedExecution();
string value = await hedged.ExecuteAsync(_ => Task.FromResult("hedged"), isIdempotent: true);
Console.WriteLine($"Hedged result: {value}");

using var budget = ExecutionBudget.Create(TimeSpan.FromSeconds(30));
if (budget.TryReserve(TimeSpan.FromSeconds(5), out BudgetReservation reservation))
{
    budget.Release(reservation, TimeSpan.FromSeconds(2));
    Console.WriteLine($"Budget remaining: {budget.GetSnapshot().Remaining.TotalSeconds:F1}s");
}

var batch = new CheckpointedBatchExecutor(new InMemoryCheckpointStore());
BatchExecutionResult batchResult = await batch.ExecuteAsync(
    "demo-run",
    DemoItems.Value,
    static n => $"item-{n}",
    (_, _) => Task.CompletedTask,
    isIdempotent: true);
Console.WriteLine($"Batch completed: {batchResult.Completed.Count}, skipped: {batchResult.SkippedAsCompleted.Count}");

file static class DemoItems
{
    public static readonly int[] Value = [1, 2, 3];
}
