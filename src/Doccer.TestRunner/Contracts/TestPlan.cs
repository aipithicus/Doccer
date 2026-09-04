using System.Collections.Generic;

namespace Doccer.TestRunner;

internal enum TestPlanEntryKind
{
    Command,
    ExecutableHarness,
}

internal enum TestWorkItemKind
{
    Command,
    HarnessCase,
}

internal enum TestConcurrency
{
    Parallel,
    Exclusive,
}

internal sealed record TestPlan(
    int SchemaVersion,
    string Protocol,
    IReadOnlyList<TestPlanEntry> Entries);

internal sealed record TestPlanEntry(
    string Id,
    string DisplayName,
    TestPlanEntryKind Kind,
    string? Project,
    string? Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    int? TimeoutMilliseconds,
    string? Group,
    TestConcurrency? Concurrency);

internal sealed record TestWorkItem(
    string Id,
    string DisplayName,
    TestWorkItemKind Kind,
    string SourceId,
    string? SourceProject,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    int? TimeoutMilliseconds,
    string? Group,
    TestConcurrency Concurrency);
