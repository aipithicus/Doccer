using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Doccer;

namespace Doccer.Tests;

internal static partial class Program
{
    private const int EngineWarmupCount = 3;
    private const int EngineRepetitionCount = 9;
    private const string EngineReportProtocol = "doccer-workload-baseline";

    private static readonly JsonSerializerOptions EngineJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static void EngineWorkloadManifestIsBoundedAndDifferential()
    {
        var workloads = CreateEngineWorkloads();
        var expectedIds = new[]
        {
            "selection-enumeration-dense",
            "selection-enumeration-sparse",
            "relation-validation-dense-all-pairs",
            "graph-path-dense-alternatives",
            "graph-path-sparse-unit-chain",
            "fact-saturation-sparse-chain",
            "support-enumeration-dense-alternatives",
            "hierarchy-adjacency-sparse-tree",
            "boolean-vector-dense-prefix-parity",
            "origin-composition-dense-many-to-many",
            "materialization-copy-heavy-reordered",
        };

        True(
            workloads.Select(workload => workload.Id).SequenceEqual(expectedIds),
            "engine workload manifest fixes the named workload order");
        Equal(
            workloads.Count,
            workloads.Select(workload => workload.Id).Distinct(StringComparer.Ordinal).Count(),
            "engine workload IDs are unique");
        True(
            workloads.Any(workload => workload.Density == "dense") &&
            workloads.Any(workload => workload.Density == "sparse"),
            "engine workload manifest includes named dense and sparse postures");
        True(
            new[] { "selection", "validation", "graph/path", "fact/support", "adjacency", "vector", "origin", "materialization" }
                .All(category => workloads.Any(workload => workload.Category == category)),
            "engine workload manifest covers every required carrier workload family");
        True(
            workloads.All(workload =>
                workload.Parameters.Count > 0 &&
                workload.ScalePosture.Contains("reference", StringComparison.OrdinalIgnoreCase)),
            "engine workloads declare parameters and reference-only scale posture");

        foreach (var workload in workloads)
        {
            var expected = workload.Reference();
            var observed = workload.Execute();
            Equal(expected, observed, $"engine workload {workload.Id} agrees with its independent checksum");
        }
    }

    private static int RunWorkloadMeasurements(string[] args)
    {
#if DEBUG
        return UsageError("Workload measurement requires a Release build (-c Release).");
#else
        if (args.Length != 3 || !StringComparer.Ordinal.Equals(args[1], "--output"))
        {
            return UsageError("Usage: Doccer.Tests measure-workloads --output <build-relative-json-path>.");
        }

        try
        {
            var repositoryRoot = EngineFindRepositoryRoot();
            var outputPath = EngineResolveOutputPath(repositoryRoot, args[2]);
            var workloads = CreateEngineWorkloads();
            var measurements = new List<EngineMeasurement>(workloads.Count);
            foreach (var workload in workloads)
            {
                measurements.Add(EngineMeasure(workload));
            }

            var relativeOutput = Path.GetRelativePath(repositoryRoot, outputPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var processorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            if (string.IsNullOrWhiteSpace(processorIdentifier))
            {
                processorIdentifier = RuntimeInformation.ProcessArchitecture.ToString();
            }

            var report = new EngineReport(
                SchemaVersion: 1,
                Protocol: EngineReportProtocol,
                RecordedAtUtc: DateTimeOffset.UtcNow,
                Configuration: "Release",
                Runtime: new EngineRuntimeStamp(
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.RuntimeIdentifier,
                    RuntimeInformation.OSDescription,
                    RuntimeInformation.ProcessArchitecture.ToString(),
                    processorIdentifier,
                    Environment.ProcessorCount,
                    Stopwatch.Frequency),
                Policy: new EngineMeasurementPolicy(
                    EngineWarmupCount,
                    EngineRepetitionCount,
                    "median",
                    "GC.GetAllocatedBytesForCurrentThread delta",
                    "every warmup and measured result must equal the independent reference checksum"),
                Measurements: measurements.AsReadOnly(),
                Qualification: "Mechanics-grade observations for these named workloads only; not a population, scientific, comparative, or release-gate performance claim.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(report, EngineJsonOptions) + Environment.NewLine);
            WriteReceipt(
                $"doccer workload receipt: status=passed workloads={measurements.Count} " +
                $"warmups={EngineWarmupCount} repetitions={EngineRepetitionCount} output={relativeOutput}");
            return 0;
        }
        catch (Exception exception)
        {
            WriteReceipt(
                $"doccer workload receipt: status=failed error={exception.GetType().Name}: {exception.Message}",
                Console.Error);
            return 1;
        }
#endif
    }

    private static EngineMeasurement EngineMeasure(EngineWorkload workload)
    {
        var expected = workload.Reference();
        for (var warmup = 0; warmup < EngineWarmupCount; warmup++)
        {
            var observed = workload.Execute();
            if (observed != expected)
            {
                throw new InvalidOperationException(
                    $"Engine workload '{workload.Id}' warmup {warmup} returned {observed}, expected {expected}.");
            }
        }

        var elapsedNanoseconds = new long[EngineRepetitionCount];
        var allocatedBytes = new long[EngineRepetitionCount];
        long lastObserved = 0;
        for (var repetition = 0; repetition < EngineRepetitionCount; repetition++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            lastObserved = workload.Execute();
            var stopped = Stopwatch.GetTimestamp();
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            if (lastObserved != expected)
            {
                throw new InvalidOperationException(
                    $"Engine workload '{workload.Id}' repetition {repetition} returned {lastObserved}, expected {expected}.");
            }

            elapsedNanoseconds[repetition] = checked((long)Math.Round(
                (stopped - started) * (1_000_000_000d / Stopwatch.Frequency),
                MidpointRounding.AwayFromZero));
            allocatedBytes[repetition] = checked(allocatedAfter - allocatedBefore);
        }

        return new EngineMeasurement(
            workload.Id,
            workload.Category,
            workload.Density,
            workload.OperationScope,
            workload.Parameters,
            workload.ScalePosture,
            elapsedNanoseconds,
            EngineMedian(elapsedNanoseconds),
            allocatedBytes,
            EngineMedian(allocatedBytes),
            expected,
            lastObserved,
            DifferentialPassed: true);
    }

    private static long EngineMedian(long[] samples)
    {
        var ordered = (long[])samples.Clone();
        Array.Sort(ordered);
        return ordered[ordered.Length / 2];
    }

    private static string EngineFindRepositoryRoot()
    {
        static string? FindFrom(string start)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Doccer.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }

        return FindFrom(Environment.CurrentDirectory) ??
            FindFrom(AppContext.BaseDirectory) ??
            throw new InvalidOperationException("Could not locate the Doccer repository root.");
    }

    private static string EngineResolveOutputPath(string repositoryRoot, string suppliedPath)
    {
        if (string.IsNullOrWhiteSpace(suppliedPath))
        {
            throw new ArgumentException("A workload output path is required.", nameof(suppliedPath));
        }

        var outputPath = Path.GetFullPath(
            Path.IsPathFullyQualified(suppliedPath)
                ? suppliedPath
                : Path.Combine(repositoryRoot, suppliedPath));
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(outputPath), ".json"))
        {
            throw new ArgumentException("The workload output path must end in .json.", nameof(suppliedPath));
        }

        var buildRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "build"));
        var relative = Path.GetRelativePath(buildRoot, outputPath);
        if (Path.IsPathFullyQualified(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Workload output must stay beneath the repository build directory.",
                nameof(suppliedPath));
        }

        return outputPath;
    }

    private static IReadOnlyList<EngineWorkload> CreateEngineWorkloads()
    {
        var workloads = new List<EngineWorkload>
        {
            EngineSelectionWorkload(dense: true),
            EngineSelectionWorkload(dense: false),
            EngineValidationWorkload(),
        };
        workloads.AddRange(EngineGraphPathWorkloads());
        workloads.Add(EngineSaturationWorkload());
        workloads.Add(EngineSupportEnumerationWorkload());
        workloads.Add(EngineHierarchyAdjacencyWorkload());
        workloads.Add(EngineBooleanVectorWorkload());
        workloads.Add(EngineOriginCompositionWorkload());
        workloads.Add(EngineMaterializationWorkload());
        return workloads.AsReadOnly();
    }

    private static EngineWorkload EngineSelectionWorkload(bool dense)
    {
        const int claimCount = 4096;
        var master = new TextMaster(
            dense ? "workload-selection-dense" : "workload-selection-sparse",
            0,
            new string('x', claimCount));
        var builder = new SpanBatchBuilder(master);
        for (var ordinal = 0; ordinal < claimCount; ordinal++)
        {
            builder.Add(new SpanClaim(
                new TextSpan(ordinal, ordinal + 1),
                "unit",
                SpanLevel.Character,
                "workload"));
        }

        var batch = builder.Freeze();
        var expectedOrdinals = dense
            ? Enumerable.Range(0, claimCount).ToArray()
            : Enumerable.Range(0, claimCount).Where(ordinal => ordinal % 257 == 0).ToArray();
        var selection = ClaimSelection.Create(batch, expectedOrdinals);
        return new EngineWorkload(
            dense ? "selection-enumeration-dense" : "selection-enumeration-sparse",
            "selection",
            dense ? "dense" : "sparse",
            "enumerate one prebuilt ClaimSelection and fold its ascending exact-batch ordinals",
            EngineParameters(
                ("basisClaims", claimCount.ToString(CultureInfo.InvariantCulture)),
                ("selectedClaims", expectedOrdinals.Length.ToString(CultureInfo.InvariantCulture)),
                ("selectionConstruction", "outside measured scope")),
            "reference implementation on one fixed synthetic basis; no comparative claim",
            () => EngineOrdinalChecksum(selection),
            () => EngineOrdinalChecksum(expectedOrdinals));
    }

    private static EngineWorkload EngineValidationWorkload()
    {
        const int leftCount = 64;
        const int rightCount = 64;
        var master = new TextMaster(
            "workload-validation-dense",
            0,
            new string('x', leftCount + rightCount));
        var builder = new SpanBatchBuilder(master);
        for (var ordinal = 0; ordinal < leftCount; ordinal++)
        {
            builder.Add(new SpanClaim(
                new TextSpan(ordinal, ordinal + 1),
                "left",
                SpanLevel.Character,
                "workload"));
        }

        for (var ordinal = 0; ordinal < rightCount; ordinal++)
        {
            var start = leftCount + ordinal;
            builder.Add(new SpanClaim(
                new TextSpan(start, start + 1),
                "right",
                SpanLevel.Character,
                "workload"));
        }

        var batch = builder.Freeze();
        var requirement = new RelationRequirement(
            "workload-no-left-before-right",
            "left",
            "right",
            AllenRelationSet.Singleton(AllenRelation.Before),
            minimumMatches: 0,
            maximumMatches: 0);
        return new EngineWorkload(
            "relation-validation-dense-all-pairs",
            "validation",
            "dense",
            "run declarative relation validation over fixed left/right populations and fold emitted violations",
            EngineParameters(
                ("leftClaims", leftCount.ToString(CultureInfo.InvariantCulture)),
                ("rightClaims", rightCount.ToString(CultureInfo.InvariantCulture)),
                ("candidatePairs", (leftCount * rightCount).ToString(CultureInfo.InvariantCulture))),
            "quadratic reference validator on one named finite workload; no general throughput claim",
            () => EngineValidationChecksum(DoccerValidation.ValidateRelations(batch, new[] { requirement })),
            () => EngineExpectedValidationChecksum(leftCount));
    }

    private static IReadOnlyList<EngineWorkload> EngineGraphPathWorkloads()
    {
        const int windowLength = 256;
        var master = new TextMaster("workload-graph-path", 0, new string('x', windowLength));
        var builder = new SpanBatchBuilder(master);
        var unitOrdinals = new List<int>();
        var fourUnitOrdinals = new Dictionary<int, int>();
        for (var start = 0; start < windowLength; start++)
        {
            unitOrdinals.Add(builder.Add(new SpanClaim(
                new TextSpan(start, start + 1),
                "edge",
                SpanLevel.Character,
                "workload")));
            if (start + 2 <= windowLength)
            {
                builder.Add(new SpanClaim(
                    new TextSpan(start, start + 2),
                    "edge",
                    SpanLevel.Character,
                    "workload"));
            }

            if (start + 4 <= windowLength)
            {
                fourUnitOrdinals.Add(start, builder.Add(new SpanClaim(
                    new TextSpan(start, start + 4),
                    "edge",
                    SpanLevel.Character,
                    "workload")));
            }
        }

        var batch = builder.Freeze();
        var graph = CandidateRegionGraph.Create(ClaimSelection.All(batch), master.Extent);
        var policy = AdditivePathPolicy.Create(
            graph,
            "workload-one-per-edge",
            "edge-count",
            static _ => 1L);
        var denseExpected = Enumerable.Range(0, windowLength / 4)
            .Select(index => fourUnitOrdinals[index * 4])
            .ToArray();
        var denseProblem = PathSelectionProblem.Create(graph, graph.Candidates, policy);
        var sparse = ClaimSelection.Create(batch, unitOrdinals);
        var sparseProblem = PathSelectionProblem.Create(graph, sparse, policy);

        return Array.AsReadOnly(new[]
        {
            new EngineWorkload(
                "graph-path-dense-alternatives",
                "graph/path",
                "dense",
                "select one minimum-additive complete path from a prebuilt graph containing length-1, length-2, and length-4 alternatives",
                EngineParameters(
                    ("windowUtf16Units", windowLength.ToString(CultureInfo.InvariantCulture)),
                    ("candidateEdges", graph.Count.ToString(CultureInfo.InvariantCulture)),
                    ("admissibleEdges", graph.Count.ToString(CultureInfo.InvariantCulture))),
                "reference dynamic-programming path selection on one finite DAG; no optimizer comparison",
                () => EnginePathChecksum(PathSelection.Select(denseProblem)),
                () => EngineExpectedPathChecksum(
                    denseExpected.Length,
                    denseExpected,
                    graph.Count - denseExpected.Length,
                    excludedCount: 0)),
            new EngineWorkload(
                "graph-path-sparse-unit-chain",
                "graph/path",
                "sparse",
                "select the sole complete unit-edge path from a sparse admissible subset of the same exact graph",
                EngineParameters(
                    ("windowUtf16Units", windowLength.ToString(CultureInfo.InvariantCulture)),
                    ("candidateEdges", graph.Count.ToString(CultureInfo.InvariantCulture)),
                    ("admissibleEdges", sparse.Count.ToString(CultureInfo.InvariantCulture))),
                "reference dynamic-programming path selection on one sparse admissible subset; no optimizer comparison",
                () => EnginePathChecksum(PathSelection.Select(sparseProblem)),
                () => EngineExpectedPathChecksum(
                    unitOrdinals.Count,
                    unitOrdinals,
                    rejectedCount: 0,
                    excludedCount: graph.Count - unitOrdinals.Count)),
        });
    }

    private static EngineWorkload EngineSaturationWorkload()
    {
        const int factCount = 128;
        var master = new TextMaster("workload-saturation", 0, "x");
        var facts = Enumerable.Range(0, factCount)
            .Select(index => new FactKey(
                "workload",
                "chain",
                Array.Empty<TextSpan>(),
                new[] { index.ToString("D3", CultureInfo.InvariantCulture) }))
            .ToArray();
        var initialFacts = CanonicalFactTable.Create(master, new[] { facts[0] });
        var occurrences = new SpanBatchBuilder(master).Freeze();
        var initial = SupportHypergraph.Create(
            initialFacts,
            occurrences,
            Array.Empty<SupportEdge>());
        var rules = new GroundRule[factCount - 1];
        for (var index = 1; index < factCount; index++)
        {
            rules[index - 1] = new GroundRule(
                facts[index],
                "workload-chain-step",
                new[] { facts[index - 1] },
                new[] { index.ToString("D3", CultureInfo.InvariantCulture) },
                Array.Empty<int>());
        }

        var problem = SaturationProblem.Create(initial, rules);
        return new EngineWorkload(
            "fact-saturation-sparse-chain",
            "fact/support",
            "sparse",
            "saturate a prebuilt positive ground-rule chain and verify every expected semantic fact",
            EngineParameters(
                ("initialFacts", "1"),
                ("groundRules", rules.Length.ToString(CultureInfo.InvariantCulture)),
                ("expectedFacts", factCount.ToString(CultureInfo.InvariantCulture))),
            "reference finite positive saturation on one sparse chain; no incremental-backend claim",
            () => EngineSaturationChecksum(FactSaturation.Saturate(problem), facts),
            () => EngineMix(EngineMix(EngineMix(EngineSeed, factCount), rules.Length), factCount));
    }

    private static EngineWorkload EngineSupportEnumerationWorkload()
    {
        const int alternativeCount = 256;
        var master = new TextMaster("workload-support", 0, "x");
        var conclusion = new FactKey(
            "workload",
            "conclusion",
            Array.Empty<TextSpan>(),
            Array.Empty<string>());
        var premise = new FactKey(
            "workload",
            "premise",
            Array.Empty<TextSpan>(),
            Array.Empty<string>());
        var facts = CanonicalFactTable.Create(master, new[] { conclusion, premise });
        if (!facts.TryGetOrdinal(conclusion, out var conclusionOrdinal) ||
            !facts.TryGetOrdinal(premise, out var premiseOrdinal))
        {
            throw new InvalidOperationException("Engine support fixture could not resolve its facts.");
        }
        var occurrences = new SpanBatchBuilder(master).Freeze();
        var edges = new SupportEdge[alternativeCount];
        for (var index = 0; index < alternativeCount; index++)
        {
            edges[index] = new SupportEdge(
                conclusionOrdinal,
                "workload-alternative",
                new[] { premiseOrdinal },
                new[] { index.ToString("D3", CultureInfo.InvariantCulture) },
                Array.Empty<int>());
        }

        var graph = SupportHypergraph.Create(facts, occurrences, edges);
        return new EngineWorkload(
            "support-enumeration-dense-alternatives",
            "fact/support",
            "dense",
            "enumerate all alternative supports for one conclusion from a prebuilt exact support graph",
            EngineParameters(
                ("facts", facts.Count.ToString(CultureInfo.InvariantCulture)),
                ("supportEdges", graph.Count.ToString(CultureInfo.InvariantCulture)),
                ("supportsForConclusion", alternativeCount.ToString(CultureInfo.InvariantCulture))),
            "reference linear support query on one dense alternative set; no packed-storage claim",
            () => EngineSupportChecksum(graph.SupportsOf(conclusionOrdinal)),
            () => EngineMix(
                EngineMix(EngineSeed, alternativeCount),
                ((long)alternativeCount * (alternativeCount - 1)) / 2));
    }

    private static EngineWorkload EngineHierarchyAdjacencyWorkload()
    {
        const int nodeCount = 512;
        var master = new TextMaster("workload-hierarchy", 0, new string('x', nodeCount));
        var builder = new SpanBatchBuilder(master);
        for (var ordinal = 0; ordinal < nodeCount; ordinal++)
        {
            builder.Add(new SpanClaim(
                new TextSpan(ordinal, ordinal + 1),
                "node",
                SpanLevel.Character,
                "workload"));
        }

        var batch = builder.Freeze();
        var edges = new HierarchyEdge[nodeCount - 1];
        var parents = new List<int>[nodeCount];
        var children = new List<int>[nodeCount];
        for (var ordinal = 0; ordinal < nodeCount; ordinal++)
        {
            parents[ordinal] = new List<int>();
            children[ordinal] = new List<int>();
        }

        for (var child = 1; child < nodeCount; child++)
        {
            var parent = (child - 1) / 2;
            edges[child - 1] = new HierarchyEdge(child, parent, "workload-binary-tree");
            parents[child].Add(parent);
            children[parent].Add(child);
        }

        var hierarchy = HierarchyView.Create(
            ClaimSelection.All(batch),
            master.Extent,
            HierarchyPolicy.Explicit("workload-binary-tree"),
            edges);
        return new EngineWorkload(
            "hierarchy-adjacency-sparse-tree",
            "adjacency",
            "sparse",
            "query direct parents and children for every node of one prebuilt binary-tree hierarchy",
            EngineParameters(
                ("nodes", nodeCount.ToString(CultureInfo.InvariantCulture)),
                ("edges", edges.Length.ToString(CultureInfo.InvariantCulture)),
                ("queries", (nodeCount * 2).ToString(CultureInfo.InvariantCulture))),
            "reference linear adjacency queries on one sparse tree; no index-speedup claim",
            () => EngineHierarchyChecksum(hierarchy),
            () => EngineHierarchyReferenceChecksum(parents, children));
    }

    private static EngineWorkload EngineBooleanVectorWorkload()
    {
        const int length = 8192;
        var input = new bool[length];
        var setOrdinals = new List<int>();
        for (var ordinal = 0; ordinal < length; ordinal++)
        {
            input[ordinal] = ordinal % 3 != 0;
            if (input[ordinal])
            {
                setOrdinals.Add(ordinal);
            }
        }

        var vector = BooleanVector.Create(length, setOrdinals);
        var expected = new bool[length];
        var state = true;
        for (var ordinal = 0; ordinal < length; ordinal++)
        {
            state ^= input[ordinal];
            expected[ordinal] = state;
        }

        var expectedCarry = state;
        return new EngineWorkload(
            "boolean-vector-dense-prefix-parity",
            "vector",
            "dense",
            "compute inclusive prefix parity with carry-in and enumerate the resulting set ordinals",
            EngineParameters(
                ("logicalBits", length.ToString(CultureInfo.InvariantCulture)),
                ("inputPopulation", setOrdinals.Count.ToString(CultureInfo.InvariantCulture)),
                ("carryIn", "true")),
            "portable reference vector backend on one dense logical value; no SIMD or SWAR claim",
            () => EngineBooleanResultChecksum(vector.PrefixParity(carryIn: true)),
            () => EngineBooleanReferenceChecksum(expected, expectedCarry));
    }

    private static EngineWorkload EngineOriginCompositionWorkload()
    {
        const int outputAtoms = 128;
        var outputBasis = EngineSingletonBasis(
            "output",
            new TextMaster("workload-origin-output", 0, new string('o', outputAtoms)));
        var middleBasis = EngineSingletonBasis(
            "middle",
            new TextMaster("workload-origin-middle", 0, new string('m', outputAtoms * 2)));
        var sourceBasis = EngineSingletonBasis(
            "source",
            new TextMaster("workload-origin-source", 0, new string('s', outputAtoms * 4)));
        var firstEdges = new List<OriginEdge>(outputAtoms * 2);
        for (var output = 0; output < outputAtoms; output++)
        {
            firstEdges.Add(new OriginEdge(
                new OriginAtom(0, output),
                new OriginAtom(0, output * 2)));
            firstEdges.Add(new OriginEdge(
                new OriginAtom(0, output),
                new OriginAtom(0, (output * 2) + 1)));
        }

        var secondEdges = new List<OriginEdge>(outputAtoms * 4);
        for (var middle = 0; middle < outputAtoms * 2; middle++)
        {
            secondEdges.Add(new OriginEdge(
                new OriginAtom(0, middle),
                new OriginAtom(0, middle * 2)));
            secondEdges.Add(new OriginEdge(
                new OriginAtom(0, middle),
                new OriginAtom(0, (middle * 2) + 1)));
        }

        var first = OriginRelation.Create(outputBasis, middleBasis, firstEdges);
        var second = OriginRelation.Create(middleBasis, sourceBasis, secondEdges);
        return new EngineWorkload(
            "origin-composition-dense-many-to-many",
            "origin",
            "dense",
            "compose two prebuilt exact-basis many-to-many origin relations and fold canonical edges",
            EngineParameters(
                ("outputAtoms", outputAtoms.ToString(CultureInfo.InvariantCulture)),
                ("firstEdges", first.Count.ToString(CultureInfo.InvariantCulture)),
                ("secondEdges", second.Count.ToString(CultureInfo.InvariantCulture)),
                ("expectedComposedEdges", (outputAtoms * 4).ToString(CultureInfo.InvariantCulture))),
            "reference relational composition on one bounded dense shape; no indexed-origin claim",
            () => EngineOriginChecksum(first.ComposeOrigins(second)),
            () => EngineOriginReferenceChecksum(outputAtoms));
    }

    private static EngineWorkload EngineMaterializationWorkload()
    {
        const int sourceLength = 4096;
        const int blockLength = 16;
        const int passes = 2;
        var sourceCharacters = new char[sourceLength];
        for (var ordinal = 0; ordinal < sourceCharacters.Length; ordinal++)
        {
            sourceCharacters[ordinal] = (char)('a' + (ordinal % 26));
        }

        var source = new TextMaster("workload-materialization-source", 0, new string(sourceCharacters));
        var sourceBasis = EngineSingletonBasis("source", source);
        var pieces = new List<OutputPiece>();
        var expected = new StringBuilder(sourceLength * passes);
        var blockCount = sourceLength / blockLength;
        for (var pass = 0; pass < passes; pass++)
        {
            for (var block = blockCount - 1; block >= 0; block--)
            {
                var span = new TextSpan(block * blockLength, (block + 1) * blockLength);
                pieces.Add(OutputPiece.Copy(0, span));
                expected.Append(source.Slice(span));
            }
        }

        var plan = RewritePlan.Create(
            sourceBasis,
            new MaterializationTarget("workload-materialization-output", 0, "reordered-copy"),
            pieces);
        var expectedText = expected.ToString();
        return new EngineWorkload(
            "materialization-copy-heavy-reordered",
            "materialization",
            "dense",
            "materialize a prebuilt plan containing two reverse-order passes over fixed copy blocks and fold output/evidence",
            EngineParameters(
                ("sourceUtf16Units", sourceLength.ToString(CultureInfo.InvariantCulture)),
                ("pieceCount", pieces.Count.ToString(CultureInfo.InvariantCulture)),
                ("blockUtf16Units", blockLength.ToString(CultureInfo.InvariantCulture)),
                ("expectedOutputUtf16Units", expectedText.Length.ToString(CultureInfo.InvariantCulture))),
            "reference exact-plan materialization on one copy-heavy workload; no throughput or alternative-backend claim",
            () => EngineMaterializationChecksum(RewriteMaterialization.Materialize(plan)),
            () => EngineExpectedMaterializationChecksum(
                expectedText,
                pieces.Count,
                expectedText.Length,
                unusedRegionCount: 0));
    }

    private static long EngineOrdinalChecksum(IEnumerable<int> ordinals)
    {
        var checksum = EngineSeed;
        var count = 0;
        foreach (var ordinal in ordinals)
        {
            checksum = EngineMix(checksum, ordinal);
            count++;
        }

        return EngineMix(checksum, count);
    }

    private static long EngineValidationChecksum(IReadOnlyList<ValidationIssue> issues)
    {
        var checksum = EngineMix(EngineSeed, issues.Count);
        foreach (var issue in issues)
        {
            checksum = EngineMix(checksum, issue.LeftOrdinal ?? -1);
            checksum = EngineMix(checksum, issue.RightOrdinal ?? -1);
        }

        return checksum;
    }

    private static long EngineExpectedValidationChecksum(int issueCount)
    {
        var checksum = EngineMix(EngineSeed, issueCount);
        for (var leftOrdinal = 0; leftOrdinal < issueCount; leftOrdinal++)
        {
            checksum = EngineMix(checksum, leftOrdinal);
            checksum = EngineMix(checksum, -1);
        }

        return checksum;
    }

    private static long EnginePathChecksum(PathSelectionResult result) =>
        EngineExpectedPathChecksum(
            result.Score ?? -1,
            result.SelectedCandidates,
            result.RejectedCandidates.Count,
            result.ExcludedCandidates.Count);

    private static long EngineExpectedPathChecksum(
        long score,
        IEnumerable<int> selectedOrdinals,
        int rejectedCount,
        int excludedCount)
    {
        var checksum = EngineMix(EngineSeed, score);
        var selectedCount = 0;
        foreach (var ordinal in selectedOrdinals)
        {
            checksum = EngineMix(checksum, ordinal);
            selectedCount++;
        }

        checksum = EngineMix(checksum, selectedCount);
        checksum = EngineMix(checksum, rejectedCount);
        return EngineMix(checksum, excludedCount);
    }

    private static long EngineSaturationChecksum(SaturationResult result, IReadOnlyList<FactKey> expectedFacts)
    {
        var found = 0;
        foreach (var fact in expectedFacts)
        {
            if (result.Facts.TryGetOrdinal(fact, out _))
            {
                found++;
            }
        }

        return EngineMix(EngineMix(EngineMix(EngineSeed, result.Facts.Count), result.Graph.Count), found);
    }

    private static long EngineSupportChecksum(IReadOnlyList<SupportEdge> supports)
    {
        long parameterSum = 0;
        foreach (var support in supports)
        {
            parameterSum += int.Parse(
                support.Parameters[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture);
        }

        return EngineMix(EngineMix(EngineSeed, supports.Count), parameterSum);
    }

    private static long EngineHierarchyChecksum(HierarchyView hierarchy)
    {
        var checksum = EngineSeed;
        for (var ordinal = 0; ordinal < hierarchy.Nodes.Basis.Count; ordinal++)
        {
            checksum = EngineMix(checksum, ordinal);
            foreach (var parent in hierarchy.ParentsOf(ordinal))
            {
                checksum = EngineMix(checksum, parent);
            }

            checksum = EngineMix(checksum, -1);
            foreach (var child in hierarchy.ChildrenOf(ordinal))
            {
                checksum = EngineMix(checksum, child);
            }

            checksum = EngineMix(checksum, -2);
        }

        return checksum;
    }

    private static long EngineHierarchyReferenceChecksum(
        IReadOnlyList<int>[] parents,
        IReadOnlyList<int>[] children)
    {
        var checksum = EngineSeed;
        for (var ordinal = 0; ordinal < parents.Length; ordinal++)
        {
            checksum = EngineMix(checksum, ordinal);
            foreach (var parent in parents[ordinal])
            {
                checksum = EngineMix(checksum, parent);
            }

            checksum = EngineMix(checksum, -1);
            foreach (var child in children[ordinal])
            {
                checksum = EngineMix(checksum, child);
            }

            checksum = EngineMix(checksum, -2);
        }

        return checksum;
    }

    private static long EngineBooleanResultChecksum(BooleanPrefixParityResult result)
    {
        var checksum = EngineOrdinalChecksum(result.Vector);
        return EngineMix(checksum, result.CarryOut ? 1 : 0);
    }

    private static long EngineBooleanReferenceChecksum(bool[] values, bool carryOut)
    {
        var checksum = EngineSeed;
        var count = 0;
        for (var ordinal = 0; ordinal < values.Length; ordinal++)
        {
            if (!values[ordinal])
            {
                continue;
            }

            checksum = EngineMix(checksum, ordinal);
            count++;
        }

        checksum = EngineMix(checksum, count);
        return EngineMix(checksum, carryOut ? 1 : 0);
    }

    private static long EngineOriginChecksum(OriginRelation relation)
    {
        var checksum = EngineMix(EngineSeed, relation.Count);
        foreach (var edge in relation)
        {
            checksum = EngineMix(checksum, edge.Output.AtomOrdinal);
            checksum = EngineMix(checksum, edge.Source.AtomOrdinal);
        }

        checksum = EngineMix(checksum, relation.IsFunctional ? 1 : 0);
        checksum = EngineMix(checksum, relation.IsTotal ? 1 : 0);
        return EngineMix(checksum, relation.IsInjective ? 1 : 0);
    }

    private static long EngineOriginReferenceChecksum(int outputAtoms)
    {
        var checksum = EngineMix(EngineSeed, outputAtoms * 4);
        for (var output = 0; output < outputAtoms; output++)
        {
            for (var source = output * 4; source < (output + 1) * 4; source++)
            {
                checksum = EngineMix(checksum, output);
                checksum = EngineMix(checksum, source);
            }
        }

        checksum = EngineMix(checksum, 0);
        checksum = EngineMix(checksum, 1);
        return EngineMix(checksum, 1);
    }

    private static long EngineMaterializationChecksum(MaterializationResult result) =>
        EngineExpectedMaterializationChecksum(
            result.OutputMaster.Text,
            result.Pieces.Count,
            result.Origins.Count,
            result.UnusedSources.Sum(region => region.Count));

    private static long EngineExpectedMaterializationChecksum(
        string output,
        int pieceCount,
        int originCount,
        int unusedRegionCount)
    {
        var checksum = EngineMix(EngineSeed, output.Length);
        foreach (var character in output)
        {
            checksum = EngineMix(checksum, character);
        }

        checksum = EngineMix(checksum, pieceCount);
        checksum = EngineMix(checksum, originCount);
        return EngineMix(checksum, unusedRegionCount);
    }

    private static IReadOnlyDictionary<string, string> EngineParameters(
        params (string Name, string Value)[] values)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            parameters.Add(value.Name, value.Value);
        }

        return parameters;
    }

    private static OriginBasis EngineSingletonBasis(string tag, TextMaster master) =>
        OriginBasis.Create(new[] { new OriginSlot(tag, master) });

    private const long EngineSeed = 1469598103934665603L;

    private static long EngineMix(long checksum, long value) =>
        unchecked((checksum ^ value) * 1099511628211L);

    private sealed record EngineWorkload(
        string Id,
        string Category,
        string Density,
        string OperationScope,
        IReadOnlyDictionary<string, string> Parameters,
        string ScalePosture,
        Func<long> Execute,
        Func<long> Reference);

    private sealed record EngineReport(
        int SchemaVersion,
        string Protocol,
        DateTimeOffset RecordedAtUtc,
        string Configuration,
        EngineRuntimeStamp Runtime,
        EngineMeasurementPolicy Policy,
        IReadOnlyList<EngineMeasurement> Measurements,
        string Qualification);

    private sealed record EngineRuntimeStamp(
        string FrameworkDescription,
        string RuntimeIdentifier,
        string OSDescription,
        string ProcessArchitecture,
        string ProcessorIdentifier,
        int LogicalProcessorCount,
        long StopwatchFrequency);

    private sealed record EngineMeasurementPolicy(
        int WarmupCount,
        int RepetitionCount,
        string ElapsedStatistic,
        string AllocatedBytesMeasurement,
        string DifferentialPolicy);

    private sealed record EngineMeasurement(
        string Id,
        string Category,
        string Density,
        string OperationScope,
        IReadOnlyDictionary<string, string> Parameters,
        string ScalePosture,
        IReadOnlyList<long> ElapsedNanosecondsSamples,
        long MedianElapsedNanoseconds,
        IReadOnlyList<long> AllocatedBytesSamples,
        long MedianAllocatedBytes,
        long ExpectedChecksum,
        long ObservedChecksum,
        bool DifferentialPassed);
}
