using System;

namespace Doccer.TestRunner.Tests;

internal static partial class Program
{
    private static void EventContractAndLifecycleTransitionsAreExact()
    {
        var started = TestRunEvent.Create(
            "event-run",
            sequence: 1,
            ContractRequestedAt,
            TestRunEventKind.ItemStarted,
            workItemId: "case.one");
        Equal("doccer-test-event", started.Protocol, "event protocol");
        Contains("\"kind\":\"item_started\"", started.ToJsonLine(), "event kind JSON");
        True(!started.ToJsonLine().Contains('\n'), "event JSONL record is one line");

        var running = TestItemLifecycleTransitions.Apply(TestItemLifecycle.Planned, started);
        Equal(TestItemLifecycle.Running, running, "planned item starts");
        var passed = TestRunEvent.Create(
            "event-run",
            sequence: 2,
            ContractRequestedAt.AddSeconds(1),
            TestRunEventKind.ItemFinished,
            workItemId: "case.one",
            status: TestExecutionStatus.Passed);
        Equal(
            TestItemLifecycle.Terminal,
            TestItemLifecycleTransitions.Apply(running, passed),
            "running item finishes");

        var notStarted = TestRunEvent.Create(
            "event-run",
            sequence: 3,
            ContractRequestedAt.AddSeconds(1),
            TestRunEventKind.ItemFinished,
            workItemId: "case.two",
            status: TestExecutionStatus.NotStarted);
        Equal(
            TestItemLifecycle.Terminal,
            TestItemLifecycleTransitions.Apply(TestItemLifecycle.Planned, notStarted),
            "planned item can be accounted not-started");

        var infrastructure = TestRunEvent.Create(
            "event-run",
            sequence: 4,
            ContractRequestedAt.AddSeconds(1),
            TestRunEventKind.ItemFinished,
            workItemId: "case.three",
            status: TestExecutionStatus.InfrastructureError);
        Equal(
            TestItemLifecycle.Terminal,
            TestItemLifecycleTransitions.Apply(TestItemLifecycle.Planned, infrastructure),
            "launch failure can finish a planned item");

        Throws<InvalidOperationException>(
            () => TestItemLifecycleTransitions.Apply(TestItemLifecycle.Planned, passed),
            "passed item must have started");
        Throws<InvalidOperationException>(
            () => TestItemLifecycleTransitions.Apply(TestItemLifecycle.Running, started),
            "item cannot start twice");
        Throws<InvalidOperationException>(
            () => TestItemLifecycleTransitions.Apply(TestItemLifecycle.Running, notStarted),
            "running item cannot become not-started");
        Throws<ArgumentException>(
            () => TestItemLifecycleTransitions.Apply(
                TestItemLifecycle.Planned,
                started with { Protocol = "other-event" }),
            "mutated event contract rejection");
        Throws<ArgumentException>(
            () => TestRunEvent.Create(
                "event-run",
                sequence: 5,
                ContractRequestedAt,
                TestRunEventKind.RunStarted,
                workItemId: "case.one"),
            "run event item identity rejection");
        Throws<ArgumentException>(
            () => TestRunEvent.Create(
                "event-run",
                sequence: 5,
                ContractRequestedAt,
                TestRunEventKind.ItemFinished,
                workItemId: "case.one"),
            "finished event status requirement");
        Throws<ArgumentOutOfRangeException>(
            () => TestRunEvent.Create(
                "event-run",
                sequence: 0,
                ContractRequestedAt,
                TestRunEventKind.RunStarted),
            "event sequence lower bound");
        Throws<ArgumentException>(
            () => TestRunEvent.Create(
                "event-run",
                sequence: 5,
                ContractRequestedAt.ToOffset(TimeSpan.FromHours(1)),
                TestRunEventKind.RunStarted),
            "event UTC requirement");
    }
}
