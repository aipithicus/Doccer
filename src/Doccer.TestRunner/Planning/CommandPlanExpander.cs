using System;
using System.Collections.Generic;
using System.Linq;

namespace Doccer.TestRunner;

internal static class CommandPlanExpander
{
    public static CommandPlanExpansion Expand(TestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var workItems = plan.Entries
            .Where(entry => entry.Kind == TestPlanEntryKind.Command)
            .Select(entry => new TestWorkItem(
                entry.Id,
                entry.DisplayName,
                TestWorkItemKind.Command,
                entry.Id,
                SourceProject: null,
                entry.Executable!,
                entry.Arguments,
                entry.WorkingDirectory,
                entry.Environment,
                entry.TimeoutMilliseconds,
                entry.Group,
                entry.Concurrency!.Value))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        var deferredHarnessSources = plan.Entries
            .Where(entry => entry.Kind == TestPlanEntryKind.ExecutableHarness)
            .OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();

        return new CommandPlanExpansion(
            Array.AsReadOnly(workItems),
            Array.AsReadOnly(deferredHarnessSources));
    }
}

internal sealed record CommandPlanExpansion(
    IReadOnlyList<TestWorkItem> Commands,
    IReadOnlyList<TestPlanEntry> DeferredHarnessSources);
