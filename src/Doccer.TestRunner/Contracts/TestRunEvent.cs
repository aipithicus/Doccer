using System;
using System.Linq;

namespace Doccer.TestRunner;

internal enum TestRunEventKind
{
    RunStarted,
    ItemStarted,
    ItemFinished,
    CancellationRequested,
    RunFinished,
}

internal enum TestItemLifecycle
{
    Planned,
    Running,
    Terminal,
}

internal sealed record TestRunEvent(
    int SchemaVersion,
    string Protocol,
    string RunId,
    long Sequence,
    DateTimeOffset OccurredAtUtc,
    TestRunEventKind Kind,
    string? WorkItemId,
    TestExecutionStatus? Status)
{
    public static TestRunEvent Create(
        string runId,
        long sequence,
        DateTimeOffset occurredAtUtc,
        TestRunEventKind kind,
        string? workItemId = null,
        TestExecutionStatus? status = null)
    {
        var itemEvent = new TestRunEvent(
            TestRunnerProtocol.SchemaVersion,
            TestRunnerProtocol.Event,
            runId,
            sequence,
            occurredAtUtc,
            kind,
            workItemId,
            status);
        itemEvent.Validate();
        return itemEvent;
    }

    public string ToJsonLine() => TestRunnerJson.SerializeCompact(this);

    internal void Validate()
    {
        if (SchemaVersion != TestRunnerProtocol.SchemaVersion ||
            !string.Equals(Protocol, TestRunnerProtocol.Event, StringComparison.Ordinal))
        {
            throw new ArgumentException("The event has an unsupported evidence contract.");
        }

        RequireIdentity(RunId, nameof(RunId));
        if (Sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Sequence), "Event sequence starts at one.");
        }

        TestRunPlanSnapshot.RequireUtc(OccurredAtUtc, nameof(OccurredAtUtc));
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind));
        }

        if (Status is { } terminalStatus && !Enum.IsDefined(terminalStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(Status));
        }

        var isItemEvent = Kind is TestRunEventKind.ItemStarted or TestRunEventKind.ItemFinished;
        if (isItemEvent)
        {
            RequireIdentity(WorkItemId, nameof(WorkItemId));
        }
        else if (WorkItemId is not null)
        {
            throw new ArgumentException("Run-level events cannot name a work item.", nameof(WorkItemId));
        }

        if (Kind == TestRunEventKind.ItemFinished && Status is null)
        {
            throw new ArgumentException("An item-finished event requires a terminal status.", nameof(Status));
        }

        if (Kind != TestRunEventKind.ItemFinished && Status is not null)
        {
            throw new ArgumentException("Only an item-finished event can carry status.", nameof(Status));
        }
    }

    private static void RequireIdentity(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException("A stable nonblank identity is required.", parameterName);
        }
    }
}

internal static class TestItemLifecycleTransitions
{
    public static TestItemLifecycle Apply(TestItemLifecycle current, TestRunEvent itemEvent)
    {
        ArgumentNullException.ThrowIfNull(itemEvent);
        itemEvent.Validate();
        if (itemEvent.Kind == TestRunEventKind.ItemStarted)
        {
            if (current != TestItemLifecycle.Planned)
            {
                throw new InvalidOperationException(
                    $"Cannot start an item from lifecycle state '{current}'.");
            }

            return TestItemLifecycle.Running;
        }

        if (itemEvent.Kind != TestRunEventKind.ItemFinished || itemEvent.Status is null)
        {
            throw new ArgumentException("An item lifecycle consumes only item events.", nameof(itemEvent));
        }

        var allowed = itemEvent.Status switch
        {
            TestExecutionStatus.NotStarted => current == TestItemLifecycle.Planned,
            TestExecutionStatus.InfrastructureError =>
                current is TestItemLifecycle.Planned or TestItemLifecycle.Running,
            TestExecutionStatus.Passed or
            TestExecutionStatus.Failed or
            TestExecutionStatus.Canceled or
            TestExecutionStatus.TimedOut => current == TestItemLifecycle.Running,
            _ => false,
        };
        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Status '{itemEvent.Status}' cannot finish lifecycle state '{current}'.");
        }

        return TestItemLifecycle.Terminal;
    }
}
