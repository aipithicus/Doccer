using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Doccer.TestRunner;

internal sealed class TestPlanExpansionValidationException : Exception
{
    public TestPlanExpansionValidationException(string message)
        : base(message)
    {
    }
}

internal sealed record TestPlanExpansion(IReadOnlyList<TestWorkItem> WorkItems);

internal static class TestPlanExpander
{
    public static Task<TestPlanExpansion> ExpandAsync(
        TestPlan plan,
        string repositoryRoot,
        string configuration,
        CancellationToken cancellationToken) =>
        ExpandAsync(
            plan,
            repositoryRoot,
            configuration,
            new BoundedToolProcessRunner(),
            cancellationToken);

    internal static async Task<TestPlanExpansion> ExpandAsync(
        TestPlan plan,
        string repositoryRoot,
        string configuration,
        IToolProcessRunner processRunner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(processRunner);

        var commandExpansion = CommandPlanExpander.Expand(plan);
        var harnessCases = await ExecutableHarnessAdapter.ExpandAsync(
                commandExpansion.DeferredHarnessSources,
                repositoryRoot,
                configuration,
                processRunner,
                cancellationToken)
            .ConfigureAwait(false);
        var workItems = commandExpansion.Commands
            .Concat(harnessCases)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        if (workItems.Length > TestArtifactLayout.MaximumWorkItemCount)
        {
            throw new TestPlanExpansionValidationException(
                $"An expanded test plan cannot contain more than " +
                $"{TestArtifactLayout.MaximumWorkItemCount} work items.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workItem in workItems)
        {
            if (!ids.Add(workItem.Id))
            {
                throw new TestPlanExpansionValidationException(
                    $"Duplicate expanded work-item ID '{workItem.Id}'.");
            }

            if (!Enum.IsDefined(workItem.Concurrency))
            {
                throw new TestPlanExpansionValidationException(
                    $"Work item '{workItem.Id}' has an unsupported concurrency posture.");
            }
        }

        return new TestPlanExpansion(Array.AsReadOnly(workItems));
    }
}
