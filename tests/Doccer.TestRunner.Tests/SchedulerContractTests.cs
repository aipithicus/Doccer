using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Doccer.TestRunner.Tests;

internal static partial class Program
{
    private static void BoundedSchedulerEnforcesParallelAndExclusivePhases()
    {
        var items = new[]
        {
            CreateSchedulerItem("a.parallel", TestConcurrency.Parallel),
            CreateSchedulerItem("b.parallel", TestConcurrency.Parallel),
            CreateSchedulerItem("c.parallel", TestConcurrency.Parallel),
            CreateSchedulerItem("d.exclusive", TestConcurrency.Exclusive),
            CreateSchedulerItem("e.parallel", TestConcurrency.Parallel),
            CreateSchedulerItem("f.parallel", TestConcurrency.Parallel),
        };
        var sync = new object();
        var trace = new List<string>();
        var active = 0;
        var maximumActive = 0;
        var exclusiveActive = false;
        string? violation = null;

        async Task<string> ExecuteAsync(TestWorkItem item, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                if (item.Concurrency == TestConcurrency.Exclusive)
                {
                    if (active != 0 || exclusiveActive)
                    {
                        violation ??= $"Exclusive item '{item.Id}' overlapped another item.";
                    }

                    exclusiveActive = true;
                }
                else if (exclusiveActive)
                {
                    violation ??= $"Parallel item '{item.Id}' overlapped an exclusive item.";
                }

                active++;
                maximumActive = Math.Max(maximumActive, active);
                trace.Add($"start:{item.Id}");
            }

            try
            {
                await Task.Delay(
                        item.Concurrency == TestConcurrency.Exclusive ? 20 : 60,
                        cancellationToken)
                    .ConfigureAwait(false);
                return item.Id;
            }
            finally
            {
                lock (sync)
                {
                    trace.Add($"finish:{item.Id}");
                    active--;
                    if (item.Concurrency == TestConcurrency.Exclusive)
                    {
                        exclusiveActive = false;
                    }
                }
            }
        }

        var results = BoundedTestScheduler.RunAsync(
                items,
                maxParallel: 2,
                ExecuteAsync,
                item => Task.FromResult($"not-started:{item.Id}"),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        SequenceEqual(
            items.Select(item => item.Id).ToArray(),
            results,
            "scheduler returns frozen plan order");
        Equal(2, maximumActive, "scheduler parallel bound reached but not exceeded");
        Equal<string?>(null, violation, "scheduler exclusivity invariant");
        var exclusiveStart = trace.IndexOf("start:d.exclusive");
        True(
            new[] { "a.parallel", "b.parallel", "c.parallel" }
                .All(id => trace.IndexOf($"finish:{id}") < exclusiveStart),
            "exclusive waits for the preceding parallel phase to drain");
        var exclusiveFinish = trace.IndexOf("finish:d.exclusive");
        True(
            new[] { "e.parallel", "f.parallel" }
                .All(id => trace.IndexOf($"start:{id}") > exclusiveFinish),
            "parallel admission resumes only after exclusive completion");
        Throws<ArgumentOutOfRangeException>(
            () => BoundedTestScheduler.RunAsync(
                    items,
                    maxParallel: 0,
                    ExecuteAsync,
                    item => Task.FromResult(item.Id),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult(),
            "scheduler zero parallelism rejection");
    }

    private static void BoundedSchedulerStopsAdmissionAndAccountsForQueuedItems()
    {
        var items = Enumerable.Range(1, 5)
            .Select(index => CreateSchedulerItem($"p{index}", TestConcurrency.Parallel))
            .ToArray();
        using var cancellation = new CancellationTokenSource();
        var sync = new object();
        var started = new List<string>();
        var skipped = new List<string>();
        var twoStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> ExecuteAsync(TestWorkItem item, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                started.Add(item.Id);
                if (started.Count == 2)
                {
                    twoStarted.TrySetResult(true);
                }
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            return $"canceled:{item.Id}";
        }

        Task<string> NotStartedAsync(TestWorkItem item)
        {
            lock (sync)
            {
                skipped.Add(item.Id);
            }

            return Task.FromResult($"not-started:{item.Id}");
        }

        var scheduler = BoundedTestScheduler.RunAsync(
            items,
            maxParallel: 2,
            ExecuteAsync,
            NotStartedAsync,
            cancellation.Token);
        twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        cancellation.Cancel();
        var results = scheduler.GetAwaiter().GetResult();

        SequenceEqual(new[] { "p1", "p2" }, started, "scheduler started item prefix");
        SequenceEqual(new[] { "p3", "p4", "p5" }, skipped, "scheduler queued item accounting");
        SequenceEqual(
            new[]
            {
                "canceled:p1",
                "canceled:p2",
                "not-started:p3",
                "not-started:p4",
                "not-started:p5",
            },
            results,
            "scheduler cancellation result order");
    }

    private static TestWorkItem CreateSchedulerItem(
        string id,
        TestConcurrency concurrency)
    {
        return new TestWorkItem(
            id,
            id,
            TestWorkItemKind.Command,
            id,
            SourceProject: null,
            "dotnet",
            Array.Empty<string>(),
            ".",
            new Dictionary<string, string>(),
            TimeoutMilliseconds: null,
            Group: null,
            concurrency);
    }
}
