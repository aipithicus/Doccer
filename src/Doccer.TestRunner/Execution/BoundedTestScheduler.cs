using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Doccer.TestRunner;

internal static class BoundedTestScheduler
{
    public static async Task<IReadOnlyList<TResult>> RunAsync<TResult>(
        IReadOnlyList<TestWorkItem> workItems,
        int maxParallel,
        Func<TestWorkItem, CancellationToken, Task<TResult>> executeAsync,
        Func<TestWorkItem, Task<TResult>> notStartedAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItems);
        ArgumentNullException.ThrowIfNull(executeAsync);
        ArgumentNullException.ThrowIfNull(notStartedAsync);
        if (maxParallel < 1 || maxParallel > TestSchedulingContract.MaximumParallelism)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxParallel),
                $"Parallelism must be from 1 through " +
                $"{TestSchedulingContract.MaximumParallelism}.");
        }

        var results = new TResult[workItems.Count];
        var assigned = new bool[workItems.Count];
        for (var index = 0; index < workItems.Count; index++)
        {
            var workItem = workItems[index]
                ?? throw new ArgumentException("A scheduled work item cannot be null.", nameof(workItems));
            if (!Enum.IsDefined(workItem.Concurrency))
            {
                throw new ArgumentException(
                    $"Work item '{workItem.Id}' has an unsupported concurrency posture.",
                    nameof(workItems));
            }
        }

        using var executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var next = 0;
        while (next < workItems.Count)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await AccountForNotStartedAsync(
                        workItems,
                        next,
                        results,
                        assigned,
                        notStartedAsync)
                    .ConfigureAwait(false);
                break;
            }

            if (workItems[next].Concurrency == TestConcurrency.Exclusive)
            {
                results[next] = await executeAsync(
                        workItems[next],
                        executionCancellation.Token)
                    .ConfigureAwait(false);
                assigned[next] = true;
                next++;
                continue;
            }

            var segmentEnd = next + 1;
            while (segmentEnd < workItems.Count &&
                   workItems[segmentEnd].Concurrency == TestConcurrency.Parallel)
            {
                segmentEnd++;
            }

            next = await RunParallelSegmentAsync(
                    workItems,
                    next,
                    segmentEnd,
                    maxParallel,
                    results,
                    assigned,
                    executeAsync,
                    executionCancellation,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        for (var index = 0; index < assigned.Length; index++)
        {
            if (!assigned[index])
            {
                throw new InvalidOperationException(
                    $"Scheduler did not account for work item '{workItems[index].Id}'.");
            }
        }

        return Array.AsReadOnly(results);
    }

    private static async Task<int> RunParallelSegmentAsync<TResult>(
        IReadOnlyList<TestWorkItem> workItems,
        int start,
        int end,
        int maxParallel,
        TResult[] results,
        bool[] assigned,
        Func<TestWorkItem, CancellationToken, Task<TResult>> executeAsync,
        CancellationTokenSource executionCancellation,
        CancellationToken cancellationToken)
    {
        var active = new List<ActiveOperation<TResult>>(maxParallel);
        var next = start;
        try
        {
            while (next < end || active.Count != 0)
            {
                while (next < end &&
                       active.Count < maxParallel &&
                       !cancellationToken.IsCancellationRequested)
                {
                    var task = executeAsync(workItems[next], executionCancellation.Token);
                    active.Add(new ActiveOperation<TResult>(next, task));
                    next++;
                }

                if (active.Count == 0)
                {
                    break;
                }

                var completedTask = await Task.WhenAny(
                        active.ConvertAll(operation => operation.Task))
                    .ConfigureAwait(false);
                var completedIndex = active.FindIndex(
                    operation => ReferenceEquals(operation.Task, completedTask));
                if (completedIndex < 0)
                {
                    throw new InvalidOperationException(
                        "The scheduler could not identify a completed operation.");
                }

                var completed = active[completedIndex];
                active.RemoveAt(completedIndex);
                results[completed.Index] = await completed.Task.ConfigureAwait(false);
                assigned[completed.Index] = true;
            }

            return next;
        }
        catch
        {
            executionCancellation.Cancel();
            await DrainAfterFailureAsync(active).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task AccountForNotStartedAsync<TResult>(
        IReadOnlyList<TestWorkItem> workItems,
        int start,
        TResult[] results,
        bool[] assigned,
        Func<TestWorkItem, Task<TResult>> notStartedAsync)
    {
        for (var index = start; index < workItems.Count; index++)
        {
            results[index] = await notStartedAsync(workItems[index]).ConfigureAwait(false);
            assigned[index] = true;
        }
    }

    private static async Task DrainAfterFailureAsync<TResult>(
        IReadOnlyList<ActiveOperation<TResult>> active)
    {
        foreach (var operation in active)
        {
            try
            {
                await operation.Task.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }

    private sealed record ActiveOperation<TResult>(int Index, Task<TResult> Task);
}
