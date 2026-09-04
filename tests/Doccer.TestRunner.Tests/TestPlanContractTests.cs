using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doccer.TestRunner.Tests;

internal static partial class Program
{
    private static readonly string ContractRepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "contract-repository"));

    private static void PlanSchemaAndCommandExpansionAreDeterministic()
    {
        var plan = TestPlanParser.Parse(ValidPlanJson, ContractRepositoryRoot);

        Equal(1, plan.SchemaVersion, "plan schema version");
        Equal("doccer-test-plan", plan.Protocol, "plan protocol");
        Equal(3, plan.Entries.Count, "plan entry count");
        Equal(
            "tests/Doccer.Tests/Doccer.Tests.csproj",
            plan.Entries.Single(entry => entry.Kind == TestPlanEntryKind.ExecutableHarness).Project,
            "harness project normalization");

        var expansion = CommandPlanExpander.Expand(plan);
        var commands = expansion.Commands;
        SequenceEqual(
            new[] { "a.command", "z.command" },
            commands.Select(item => item.Id).ToArray(),
            "command stable ordering");
        Equal(TestWorkItemKind.Command, commands[0].Kind, "expanded command kind");
        Equal(TestConcurrency.Parallel, commands[0].Concurrency, "parallel posture");
        Equal(TestConcurrency.Exclusive, commands[1].Concurrency, "exclusive posture");
        Equal("tool path.dll", commands[1].Arguments[1], "argument vector preserves spaces");
        Equal("one", commands[1].Environment["ALPHA"], "environment value");
        Equal(1, expansion.DeferredHarnessSources.Count, "deferred harness source count");
        Equal(
            "a.harness",
            expansion.DeferredHarnessSources[0].Id,
            "deferred harness source identity");

        Throws<NotSupportedException>(
            () => ((ICollection<string>)commands[1].Arguments).Add("mutation"),
            "argument vector is frozen");
        Throws<NotSupportedException>(
            () => ((IDictionary<string, string>)commands[1].Environment).Add("BETA", "two"),
            "environment is frozen");

        var serialized = TestPlanParser.Serialize(plan);
        Contains("\"kind\": \"executable_harness\"", serialized, "harness kind serialization");
        Contains("\"concurrency\": \"exclusive\"", serialized, "concurrency serialization");
        var roundTrip = TestPlanParser.Parse(serialized, ContractRepositoryRoot);
        Equal(plan.Entries.Count, roundTrip.Entries.Count, "plan JSON round trip");
    }

    private static void PlanValidationRejectsAmbiguityAndEscapes()
    {
        RejectsPlan(
            ValidPlanJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal),
            "Unsupported test plan schema version",
            "schema version rejection");
        RejectsPlan(
            ValidPlanJson.Replace("\"protocol\": \"doccer-test-plan\"", "\"protocol\": \"other\"", StringComparison.Ordinal),
            "protocol must be",
            "protocol rejection");
        RejectsPlan(
            ValidPlanJson.Replace("\"kind\": \"command\"", "\"kind\": \"Command\"", StringComparison.Ordinal),
            "kind must be",
            "noncanonical kind rejection");
        RejectsPlan(
            ValidPlanJson.Replace("\"displayName\": \"Same:name\"", "\"displayName\": \"Same:name\", \"mystery\": true", StringComparison.Ordinal),
            "could not be mapped",
            "unknown property rejection");
        RejectsPlan(
            ValidPlanJson.Replace("\"id\": \"z.command\"", "\"id\": \"a.command\"", StringComparison.Ordinal),
            "Duplicate plan entry ID",
            "duplicate ID rejection");
        RejectsPlan(
            ValidPlanJson.Replace("\"environment\": { \"ALPHA\": \"one\" }", "\"environment\": { \"TEMP\": \"outside\" }", StringComparison.Ordinal),
            "reserved environment",
            "reserved environment rejection");
        RejectsPlan(
            ValidPlanJson.Replace("{ \"ALPHA\": \"one\" }", "{ \"ALPHA\": \"one\", \"alpha\": \"two\" }", StringComparison.Ordinal),
            "differ only by case",
            "case-colliding environment rejection");
        RejectsPlan(
            ValidPlanJson.Replace("\"workingDirectory\": \"tools\"", "\"workingDirectory\": \"../outside\"", StringComparison.Ordinal),
            "must remain beneath",
            "working-directory escape rejection");
        RejectsPlan(
            ValidPlanJson.Replace(",\n      \"concurrency\": \"parallel\"", string.Empty, StringComparison.Ordinal),
            "must declare 'concurrency'",
            "missing command concurrency rejection");
        RejectsPlan(
            ValidPlanJson.Replace("\"id\": \"a.command\"", "\"id\": \"a.command\", \"id\": \"duplicate.property\"", StringComparison.Ordinal),
            "Duplicate JSON property",
            "duplicate JSON property rejection");
        RejectsPlan(
            ValidPlanJson.Replace("\"group\": \"contracts\"", "\"group\": \"contracts\", \"concurrency\": \"parallel\"", StringComparison.Ordinal),
            "receives concurrency from its case catalog",
            "harness concurrency override rejection");
    }

    private static void RejectsPlan(string json, string expectedMessage, string name)
    {
        var exception = Throws<TestPlanValidationException>(
            () => TestPlanParser.Parse(json, ContractRepositoryRoot),
            name);
        Contains(expectedMessage, exception.Message, $"{name} message");
    }

    private const string ValidPlanJson =
        """
        {
          "schemaVersion": 1,
          "protocol": "doccer-test-plan",
          "entries": [
            {
              "id": "z.command",
              "displayName": "Same/name",
              "kind": "command",
              "executable": "dotnet",
              "arguments": [ "exec", "tool path.dll", "--value=a b" ],
              "workingDirectory": ".",
              "environment": { "ALPHA": "one" },
              "timeoutMilliseconds": 5000,
              "group": "infrastructure",
              "concurrency": "exclusive"
            },
            {
              "id": "a.harness",
              "displayName": "Doccer contracts",
              "kind": "executable_harness",
              "project": "tests/Doccer.Tests/Doccer.Tests.csproj",
              "arguments": [],
              "workingDirectory": ".",
              "environment": {},
              "timeoutMilliseconds": 30000,
              "group": "contracts"
            },
            {
              "id": "a.command",
              "displayName": "Same:name",
              "kind": "command",
              "executable": "tools/check.exe",
              "arguments": [],
              "workingDirectory": "tools",
              "environment": {},
              "concurrency": "parallel"
            }
          ]
        }
        """;
}
