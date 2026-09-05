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
    private const int A0WarmupCount = 3;
    private const int A0RepetitionCount = 9;
    private const string A0ReportProtocol = "doccer-a0-baseline";

    private static readonly JsonSerializerOptions A0JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static void A0WorkloadManifestIsBoundedAndDifferential()
    {
        var workloads = CreateA0Workloads();
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
            "A0 manifest fixes the named workload order");
        Equal(
            workloads.Count,
            workloads.Select(workload => workload.Id).Distinct(StringComparer.Ordinal).Count(),
            "A0 workload IDs are unique");
        True(
            workloads.Any(workload => workload.Density == "dense") &&
            workloads.Any(workload => workload.Density == "sparse"),
            "A0 manifest includes named dense and sparse postures");
        True(
            new[] { "selection", "validation", "graph/path", "fact/support", "adjacency", "vector", "origin", "materialization" }
                .All(category => workloads.Any(workload => workload.Category == category)),
            "A0 manifest covers every required carrier workload family");
        True(
            workloads.All(workload =>
                workload.Parameters.Count > 0 &&
                workload.ScalePosture.Contains("reference", StringComparison.OrdinalIgnoreCase)),
            "A0 workloads declare parameters and reference-only scale posture");

        foreach (var workload in workloads)
        {
            var expected = workload.Reference();
            var observed = workload.Execute();
            Equal(expected, observed, $"A0 {workload.Id} agrees with its independent checksum");
        }
    }

    private static int RunA0Measurements(string[] args)
    {
#if DEBUG
        return UsageError("A0 measurement requires a Release build (-c Release).");
#else
        if (args.Length != 3 || !StringComparer.Ordinal.Equals(args[1], "--output"))
        {
            return UsageError("Usage: Doccer.Tests measure-a0 --output <build-relative-json-path>.");
        }

        try
        {
            var repositoryRoot = A0FindRepositoryRoot();
            var outputPath = A0ResolveOutputPath(repositoryRoot, args[2]);
            var workloads = CreateA0Workloads();
            var measurements = new List<A0Measurement>(workloads.Count);
            foreach (var workload in workloads)
            {
                measurements.Add(A0Measure(workload));
            }

            var relativeOutput = Path.GetRelativePath(repositoryRoot, outputPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var processorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            if (string.IsNullOrWhiteSpace(processorIdentifier))
            {
                processorIdentifier = RuntimeInformation.ProcessArchitecture.ToString();
            }

            var report = new A0Report(
                SchemaVersion: 1,
                Protocol: A0ReportProtocol,
                RecordedAtUtc: DateTimeOffset.UtcNow,
                Configuration: "Release",
                Runtime: new A0RuntimeStamp(
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.RuntimeIdentifier,
                    RuntimeInformation.OSDescription,
                    RuntimeInformation.ProcessArchitecture.ToString(),
                    processorIdentifier,
                    Environment.ProcessorCount,
                    Stopwatch.Frequency),
                Policy: new A0MeasurementPolicy(
                    A0WarmupCount,
                    A0RepetitionCount,
                    "median",
                    "GC.GetAllocatedBytesForCurrentThread delta",
                    "every warmup and measured result must equal the independent reference checksum"),
                Measurements: measurements.AsReadOnly(),
                Qualification: "Mechanics-grade observations for these named workloads only; not a population, scientific, comparative, or release-gate performance claim.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(report, A0JsonOptions) + Environment.NewLine);
            WriteReceipt(
                $"doccer a0 receipt: status=passed workloads={measurements.Count} " +
                $"warmups={A0WarmupCount} repetitions={A0RepetitionCount} output={relativeOutput}");
            return 0;
        }
        catch (Exception exception)
        {
            WriteReceipt(
                $"doccer a0 receipt: status=failed error={exception.GetType().Name}: {exception.Message}",
                Console.Error);
            return 1;
        }
#endif
    }

    private static A0Measurement A0Measure(A0Workload workload)
    {
        var expected = workload.Reference();
        for (var warmup = 0; warmup < A0WarmupCount; warmup++)
        {
            var observed = workload.Execute();
            if (observed != expected)
            {
                throw new InvalidOperationException(
                    $"A0 workload '{workload.Id}' warmup {warmup} returned {observed}, expected {expected}.");
            }
        }

        var elapsedNanoseconds = new long[A0RepetitionCount];
        var allocatedBytes = new long[A0RepetitionCount];
        long lastObserved = 0;
        for (var repetition = 0; repetition < A0RepetitionCount; repetition++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            lastObserved = workload.Execute();
            var stopped = Stopwatch.GetTimestamp();
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            if (lastObserved != expected)
            {
                throw new InvalidOperationException(
                    $"A0 workload '{workload.Id}' repetition {repetition} returned {lastObserved}, expected {expected}.");
            }

            elapsedNanoseconds[repetition] = checked((long)Math.Round(
                (stopped - started) * (1_000_000_000d / Stopwatch.Frequency),
                MidpointRounding.AwayFromZero));
            allocatedBytes[repetition] = checked(allocatedAfter - allocatedBefore);
        }

        return new A0Measurement(
            workload.Id,
            workload.Category,
            workload.Density,
            workload.OperationScope,
            workload.Parameters,
            workload.ScalePosture,
            elapsedNanoseconds,
            A0Median(elapsedNanoseconds),
            allocatedBytes,
            A0Median(allocatedBytes),
            expected,
            lastObserved,
            DifferentialPassed: true);
    }

    private static long A0Median(long[] samples)
    {
        var ordered = (long[])samples.Clone();
        Array.Sort(ordered);
        return ordered[ordered.Length / 2];
    }

    private static string A0FindRepositoryRoot()
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

    private static string A0ResolveOutputPath(string repositoryRoot, string suppliedPath)
    {
        if (string.IsNullOrWhiteSpace(suppliedPath))
        {
            throw new ArgumentException("An A0 output path is required.", nameof(suppliedPath));
        }

        var outputPath = Path.GetFullPath(
            Path.IsPathFullyQualified(suppliedPath)
                ? suppliedPath
                : Path.Combine(repositoryRoot, suppliedPath));
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(outputPath), ".json"))
        {
            throw new ArgumentException("The A0 output path must end in .json.", nameof(suppliedPath));
        }

        var buildRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "build"));
        var relative = Path.GetRelativePath(buildRoot, outputPath);
        if (Path.IsPathFullyQualified(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A0 output must stay beneath the repository build directory.",
                nameof(suppliedPath));
        }

        return outputPath;
    }

    private static IReadOnlyList<A0Workload> CreateA0Workloads()
    {
        var workloads = new List<A0Workload>
        {
            A0SelectionWorkload(dense: true),
            A0SelectionWorkload(dense: false),
            A0ValidationWorkload(),
        };
        workloads.AddRange(A0GraphPathWorkloads());
        workloads.Add(A0SaturationWorkload());
        workloads.Add(A0SupportEnumerationWorkload());
        workloads.Add(A0HierarchyAdjacencyWorkload());
        workloads.Add(A0BooleanVectorWorkload());
        workloads.Add(A0OriginCompositionWorkload());
        workloads.Add(A0MaterializationWorkload());
        return workloads.AsReadOnly();
    }

    private static A0Workload A0SelectionWorkload(bool dense)
    {
        const int claimCount = 4096;
        var master = new TextMaster(
            dense ? "a0-selection-dense" : "a0-selection-sparse",
            0,
            new string('x', claimCount));
        var builder = new SpanBatchBuilder(master);
        for (var ordinal = 0; ordinal < claimCount; ordinal++)
        {
            builder.Add(new SpanClaim(
                new TextSpan(ordinal, ordinal + 1),
                "unit",
                SpanLevel.Character,
                "a0"));
        }

        var batch = builder.Freeze();
        var expectedOrdinals = dense
            ? Enumerable.Range(0, claimCount).ToArray()
            : Enumerable.Range(0, claimCount).Where(ordinal => ordinal % 257 == 0).ToArray();
        var selection = ClaimSelection.Create(batch, expectedOrdinals);
        return new A0Workload(
            dense ? "selection-enumeration-dense" : "selection-enumeration-sparse",
            "selection",
            dense ? "dense" : "sparse",
            "enumerate one prebuilt ClaimSelection and fold its ascending exact-batch ordinals",
            A0Parameters(
                ("basisClaims", claimCount.ToString(CultureInfo.InvariantCulture)),
                ("selectedClaims", expectedOrdinals.Length.ToString(CultureInfo.InvariantCulture)),
                ("selectionConstruction", "outside measured scope")),
            "reference implementation on one fixed synthetic basis; no comparative claim",
            () => A0OrdinalChecksum(selection),
            () => A0OrdinalChecksum(expectedOrdinals));
    }

    private static A0Workload A0ValidationWorkload()
    {
        const int leftCount = 64;
        const int rightCount = 64;
        var master = new TextMaster(
            "a0-validation-dense",
            0,
            new string('x', leftCount + rightCount));
        var builder = new SpanBatchBuilder(master);
        for (var ordinal = 0; ordinal < leftCount; ordinal++)
        {
            builder.Add(new SpanClaim(
                new TextSpan(ordinal, ordinal + 1),
                "left",
                SpanLevel.Character,
                "a0"));
        }

        for (var ordinal = 0; ordinal < rightCount; ordinal++)
        {
            var start = leftCount + ordinal;
            builder.Add(new SpanClaim(
                new TextSpan(start, start + 1),
                "right",
                SpanLevel.Character,
                "a0"));
        }

        var batch = builder.Freeze();
        var requirement = new RelationRequirement(
            "a0-no-left-before-right",
            "left",
            "right",
            AllenRelationSet.Singleton(AllenRelation.Before),
            minimumMatches: 0,
            maximumMatches: 0);
        return new A0Workload(
            "relation-validation-dense-all-pairs",
            "validation",
            "dense",
            "run declarative relation validation over fixed left/right populations and fold emitted violations",
            A0Parameters(
                ("leftClaims", leftCount.ToString(CultureInfo.InvariantCulture)),
                ("rightClaims", rightCount.ToString(CultureInfo.InvariantCulture)),
                ("candidatePairs", (leftCount * rightCount).ToString(CultureInfo.InvariantCulture))),
            "quadratic reference validator on one named finite workload; no general throughput claim",
            () => A0ValidationChecksum(DoccerValidation.ValidateRelations(batch, new[] { requirement })),
            () => A0ExpectedValidationChecksum(leftCount));
    }

    private static IReadOnlyList<A0Workload> A0GraphPathWorkloads()
    {
        const int windowLength = 256;
        var master = new TextMaster("a0-graph-path", 0, new string('x', windowLength));
        var builder = new SpanBatchBuilder(master);
        var unitOrdinals = new List<int>();
        var fourUnitOrdinals = new Dictionary<int, int>();
        for (var start = 0; start < windowLength; start++)
        {
            unitOrdinals.Add(builder.Add(new SpanClaim(
                new TextSpan(start, start + 1),
                "edge",
                SpanLevel.Character,
                "a0")));
            if (start + 2 <= windowLength)
            {
                builder.Add(new SpanClaim(
                    new TextSpan(start, start + 2),
                    "edge",
                    SpanLevel.Character,
                    "a0"));
            }

            if (start + 4 <= windowLength)
            {
                fourUnitOrdinals.Add(start, builder.Add(new SpanClaim(
                    new TextSpan(start, start + 4),
                    "edge",
                    SpanLevel.Character,
                    "a0")));
            }
        }

        var batch = builder.Freeze();
        var graph = CandidateRegionGraph.Create(ClaimSelection.All(batch), master.Extent);
        var policy = AdditivePathPolicy.Create(
            graph,
            "a0-one-per-edge",
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
            new A0Workload(
                "graph-path-dense-alternatives",
                "graph/path",
                "dense",
                "select one minimum-additive complete path from a prebuilt graph containing length-1, length-2, and length-4 alternatives",
                A0Parameters(
                    ("windowUtf16Units", windowLength.ToString(CultureInfo.InvariantCulture)),
                    ("candidateEdges", graph.Count.ToString(CultureInfo.InvariantCulture)),
                    ("admissibleEdges", graph.Count.ToString(CultureInfo.InvariantCulture))),
                "reference dynamic-programming path selection on one finite DAG; no optimizer comparison",
                () => A0PathChecksum(PathSelection.Select(denseProblem)),
                () => A0ExpectedPathChecksum(
                    denseExpected.Length,
                    denseExpected,
                    graph.Count - denseExpected.Length,
                    excludedCount: 0)),
            new A0Workload(
                "graph-path-sparse-unit-chain",
                "graph/path",
                "sparse",
                "select the sole complete unit-edge path from a sparse admissible subset of the same exact graph",
                A0Parameters(
                    ("windowUtf16Units", windowLength.ToString(CultureInfo.InvariantCulture)),
                    ("candidateEdges", graph.Count.ToString(CultureInfo.InvariantCulture)),
                    ("admissibleEdges", sparse.Count.ToString(CultureInfo.InvariantCulture))),
                "reference dynamic-programming path selection on one sparse admissible subset; no optimizer comparison",
                () => A0PathChecksum(PathSelection.Select(sparseProblem)),
                () => A0ExpectedPathChecksum(
                    unitOrdinals.Count,
                    unitOrdinals,
                    rejectedCount: 0,
                    excludedCount: graph.Count - unitOrdinals.Count)),
        });
    }

    private static A0Workload A0SaturationWorkload()
    {
        const int factCount = 128;
        var master = new TextMaster("a0-saturation", 0, "x");
        var facts = Enumerable.Range(0, factCount)
            .Select(index => new FactKey(
                "a0",
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
                "a0-chain-step",
                new[] { facts[index - 1] },
                new[] { index.ToString("D3", CultureInfo.InvariantCulture) },
                Array.Empty<int>());
        }

        var problem = SaturationProblem.Create(initial, rules);
        return new A0Workload(
            "fact-saturation-sparse-chain",
            "fact/support",
            "sparse",
            "saturate a prebuilt positive ground-rule chain and verify every expected semantic fact",
            A0Parameters(
                ("initialFacts", "1"),
                ("groundRules", rules.Length.ToString(CultureInfo.InvariantCulture)),
                ("expectedFacts", factCount.ToString(CultureInfo.InvariantCulture))),
            "reference finite positive saturation on one sparse chain; no incremental-backend claim",
            () => A0SaturationChecksum(FactSaturation.Saturate(problem), facts),
            () => A0Mix(A0Mix(A0Mix(A0Seed, factCount), rules.Length), factCount));
    }

    private static A0Workload A0SupportEnumerationWorkload()
    {
        const int alternativeCount = 256;
        var master = new TextMaster("a0-support", 0, "x");
        var conclusion = new FactKey(
            "a0",
            "conclusion",
            Array.Empty<TextSpan>(),
            Array.Empty<string>());
        var premise = new FactKey(
            "a0",
            "premise",
            Array.Empty<TextSpan>(),
            Array.Empty<string>());
        var facts = CanonicalFactTable.Create(master, new[] { conclusion, premise });
        if (!facts.TryGetOrdinal(conclusion, out var conclusionOrdinal) ||
            !facts.TryGetOrdinal(premise, out var premiseOrdinal))
        {
            throw new InvalidOperationException("A0 support fixture could not resolve its facts.");
        }
        var occurrences = new SpanBatchBuilder(master).Freeze();
        var edges = new SupportEdge[alternativeCount];
        for (var index = 0; index < alternativeCount; index++)
        {
            edges[index] = new SupportEdge(
                conclusionOrdinal,
                "a0-alternative",
                new[] { premiseOrdinal },
                new[] { index.ToString("D3", CultureInfo.InvariantCulture) },
                Array.Empty<int>());
        }

        var graph = SupportHypergraph.Create(facts, occurrences, edges);
        return new A0Workload(
            "support-enumeration-dense-alternatives",
            "fact/support",
            "dense",
            "enumerate all alternative supports for one conclusion from a prebuilt exact support graph",
            A0Parameters(
                ("facts", facts.Count.ToString(CultureInfo.InvariantCulture)),
                ("supportEdges", graph.Count.ToString(CultureInfo.InvariantCulture)),
                ("supportsForConclusion", alternativeCount.ToString(CultureInfo.InvariantCulture))),
            "reference linear support query on one dense alternative set; no packed-storage claim",
            () => A0SupportChecksum(graph.SupportsOf(conclusionOrdinal)),
            () => A0Mix(
                A0Mix(A0Seed, alternativeCount),
                ((long)alternativeCount * (alternativeCount - 1)) / 2));
    }

    private static A0Workload A0HierarchyAdjacencyWorkload()
    {
        const int nodeCount = 512;
        var master = new TextMaster("a0-hierarchy", 0, new string('x', nodeCount));
        var builder = new SpanBatchBuilder(master);
        for (var ordinal = 0; ordinal < nodeCount; ordinal++)
        {
            builder.Add(new SpanClaim(
                new TextSpan(ordinal, ordinal + 1),
                "node",
                SpanLevel.Character,
                "a0"));
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
            edges[child - 1] = new HierarchyEdge(child, parent, "a0-binary-tree");
            parents[child].Add(parent);
            children[parent].Add(child);
        }

        var hierarchy = HierarchyView.Create(
            ClaimSelection.All(batch),
            master.Extent,
            HierarchyPolicy.Explicit("a0-binary-tree"),
            edges);
        return new A0Workload(
            "hierarchy-adjacency-sparse-tree",
            "adjacency",
            "sparse",
            "query direct parents and children for every node of one prebuilt binary-tree hierarchy",
            A0Parameters(
                ("nodes", nodeCount.ToString(CultureInfo.InvariantCulture)),
                ("edges", edges.Length.ToString(CultureInfo.InvariantCulture)),
                ("queries", (nodeCount * 2).ToString(CultureInfo.InvariantCulture))),
            "reference linear adjacency queries on one sparse tree; no index-speedup claim",
            () => A0HierarchyChecksum(hierarchy),
            () => A0HierarchyReferenceChecksum(parents, children));
    }

    private static A0Workload A0BooleanVectorWorkload()
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
        return new A0Workload(
            "boolean-vector-dense-prefix-parity",
            "vector",
            "dense",
            "compute inclusive prefix parity with carry-in and enumerate the resulting set ordinals",
            A0Parameters(
                ("logicalBits", length.ToString(CultureInfo.InvariantCulture)),
                ("inputPopulation", setOrdinals.Count.ToString(CultureInfo.InvariantCulture)),
                ("carryIn", "true")),
            "portable reference vector backend on one dense logical value; no SIMD or SWAR claim",
            () => A0BooleanResultChecksum(vector.PrefixParity(carryIn: true)),
            () => A0BooleanReferenceChecksum(expected, expectedCarry));
    }

    private static A0Workload A0OriginCompositionWorkload()
    {
        const int outputAtoms = 128;
        var outputBasis = K8SingletonBasis(
            "output",
            new TextMaster("a0-origin-output", 0, new string('o', outputAtoms)));
        var middleBasis = K8SingletonBasis(
            "middle",
            new TextMaster("a0-origin-middle", 0, new string('m', outputAtoms * 2)));
        var sourceBasis = K8SingletonBasis(
            "source",
            new TextMaster("a0-origin-source", 0, new string('s', outputAtoms * 4)));
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
        return new A0Workload(
            "origin-composition-dense-many-to-many",
            "origin",
            "dense",
            "compose two prebuilt exact-basis many-to-many origin relations and fold canonical edges",
            A0Parameters(
                ("outputAtoms", outputAtoms.ToString(CultureInfo.InvariantCulture)),
                ("firstEdges", first.Count.ToString(CultureInfo.InvariantCulture)),
                ("secondEdges", second.Count.ToString(CultureInfo.InvariantCulture)),
                ("expectedComposedEdges", (outputAtoms * 4).ToString(CultureInfo.InvariantCulture))),
            "reference relational composition on one bounded dense shape; no indexed-origin claim",
            () => A0OriginChecksum(first.ComposeOrigins(second)),
            () => A0OriginReferenceChecksum(outputAtoms));
    }

    private static A0Workload A0MaterializationWorkload()
    {
        const int sourceLength = 4096;
        const int blockLength = 16;
        const int passes = 2;
        var sourceCharacters = new char[sourceLength];
        for (var ordinal = 0; ordinal < sourceCharacters.Length; ordinal++)
        {
            sourceCharacters[ordinal] = (char)('a' + (ordinal % 26));
        }

        var source = new TextMaster("a0-materialization-source", 0, new string(sourceCharacters));
        var sourceBasis = K8SingletonBasis("source", source);
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
            new MaterializationTarget("a0-materialization-output", 0, "reordered-copy"),
            pieces);
        var expectedText = expected.ToString();
        return new A0Workload(
            "materialization-copy-heavy-reordered",
            "materialization",
            "dense",
            "materialize a prebuilt plan containing two reverse-order passes over fixed copy blocks and fold output/evidence",
            A0Parameters(
                ("sourceUtf16Units", sourceLength.ToString(CultureInfo.InvariantCulture)),
                ("pieceCount", pieces.Count.ToString(CultureInfo.InvariantCulture)),
                ("blockUtf16Units", blockLength.ToString(CultureInfo.InvariantCulture)),
                ("expectedOutputUtf16Units", expectedText.Length.ToString(CultureInfo.InvariantCulture))),
            "reference exact-plan materialization on one copy-heavy workload; no throughput or alternative-backend claim",
            () => A0MaterializationChecksum(RewriteMaterialization.Materialize(plan)),
            () => A0ExpectedMaterializationChecksum(
                expectedText,
                pieces.Count,
                expectedText.Length,
                unusedRegionCount: 0));
    }

    private static long A0OrdinalChecksum(IEnumerable<int> ordinals)
    {
        var checksum = A0Seed;
        var count = 0;
        foreach (var ordinal in ordinals)
        {
            checksum = A0Mix(checksum, ordinal);
            count++;
        }

        return A0Mix(checksum, count);
    }

    private static long A0ValidationChecksum(IReadOnlyList<ValidationIssue> issues)
    {
        var checksum = A0Mix(A0Seed, issues.Count);
        foreach (var issue in issues)
        {
            checksum = A0Mix(checksum, issue.LeftOrdinal ?? -1);
            checksum = A0Mix(checksum, issue.RightOrdinal ?? -1);
        }

        return checksum;
    }

    private static long A0ExpectedValidationChecksum(int issueCount)
    {
        var checksum = A0Mix(A0Seed, issueCount);
        for (var leftOrdinal = 0; leftOrdinal < issueCount; leftOrdinal++)
        {
            checksum = A0Mix(checksum, leftOrdinal);
            checksum = A0Mix(checksum, -1);
        }

        return checksum;
    }

    private static long A0PathChecksum(PathSelectionResult result) =>
        A0ExpectedPathChecksum(
            result.Score ?? -1,
            result.SelectedCandidates,
            result.RejectedCandidates.Count,
            result.ExcludedCandidates.Count);

    private static long A0ExpectedPathChecksum(
        long score,
        IEnumerable<int> selectedOrdinals,
        int rejectedCount,
        int excludedCount)
    {
        var checksum = A0Mix(A0Seed, score);
        var selectedCount = 0;
        foreach (var ordinal in selectedOrdinals)
        {
            checksum = A0Mix(checksum, ordinal);
            selectedCount++;
        }

        checksum = A0Mix(checksum, selectedCount);
        checksum = A0Mix(checksum, rejectedCount);
        return A0Mix(checksum, excludedCount);
    }

    private static long A0SaturationChecksum(SaturationResult result, IReadOnlyList<FactKey> expectedFacts)
    {
        var found = 0;
        foreach (var fact in expectedFacts)
        {
            if (result.Facts.TryGetOrdinal(fact, out _))
            {
                found++;
            }
        }

        return A0Mix(A0Mix(A0Mix(A0Seed, result.Facts.Count), result.Graph.Count), found);
    }

    private static long A0SupportChecksum(IReadOnlyList<SupportEdge> supports)
    {
        long parameterSum = 0;
        foreach (var support in supports)
        {
            parameterSum += int.Parse(
                support.Parameters[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture);
        }

        return A0Mix(A0Mix(A0Seed, supports.Count), parameterSum);
    }

    private static long A0HierarchyChecksum(HierarchyView hierarchy)
    {
        var checksum = A0Seed;
        for (var ordinal = 0; ordinal < hierarchy.Nodes.Basis.Count; ordinal++)
        {
            checksum = A0Mix(checksum, ordinal);
            foreach (var parent in hierarchy.ParentsOf(ordinal))
            {
                checksum = A0Mix(checksum, parent);
            }

            checksum = A0Mix(checksum, -1);
            foreach (var child in hierarchy.ChildrenOf(ordinal))
            {
                checksum = A0Mix(checksum, child);
            }

            checksum = A0Mix(checksum, -2);
        }

        return checksum;
    }

    private static long A0HierarchyReferenceChecksum(
        IReadOnlyList<int>[] parents,
        IReadOnlyList<int>[] children)
    {
        var checksum = A0Seed;
        for (var ordinal = 0; ordinal < parents.Length; ordinal++)
        {
            checksum = A0Mix(checksum, ordinal);
            foreach (var parent in parents[ordinal])
            {
                checksum = A0Mix(checksum, parent);
            }

            checksum = A0Mix(checksum, -1);
            foreach (var child in children[ordinal])
            {
                checksum = A0Mix(checksum, child);
            }

            checksum = A0Mix(checksum, -2);
        }

        return checksum;
    }

    private static long A0BooleanResultChecksum(BooleanPrefixParityResult result)
    {
        var checksum = A0OrdinalChecksum(result.Vector);
        return A0Mix(checksum, result.CarryOut ? 1 : 0);
    }

    private static long A0BooleanReferenceChecksum(bool[] values, bool carryOut)
    {
        var checksum = A0Seed;
        var count = 0;
        for (var ordinal = 0; ordinal < values.Length; ordinal++)
        {
            if (!values[ordinal])
            {
                continue;
            }

            checksum = A0Mix(checksum, ordinal);
            count++;
        }

        checksum = A0Mix(checksum, count);
        return A0Mix(checksum, carryOut ? 1 : 0);
    }

    private static long A0OriginChecksum(OriginRelation relation)
    {
        var checksum = A0Mix(A0Seed, relation.Count);
        foreach (var edge in relation)
        {
            checksum = A0Mix(checksum, edge.Output.AtomOrdinal);
            checksum = A0Mix(checksum, edge.Source.AtomOrdinal);
        }

        checksum = A0Mix(checksum, relation.IsFunctional ? 1 : 0);
        checksum = A0Mix(checksum, relation.IsTotal ? 1 : 0);
        return A0Mix(checksum, relation.IsInjective ? 1 : 0);
    }

    private static long A0OriginReferenceChecksum(int outputAtoms)
    {
        var checksum = A0Mix(A0Seed, outputAtoms * 4);
        for (var output = 0; output < outputAtoms; output++)
        {
            for (var source = output * 4; source < (output + 1) * 4; source++)
            {
                checksum = A0Mix(checksum, output);
                checksum = A0Mix(checksum, source);
            }
        }

        checksum = A0Mix(checksum, 0);
        checksum = A0Mix(checksum, 1);
        return A0Mix(checksum, 1);
    }

    private static long A0MaterializationChecksum(MaterializationResult result) =>
        A0ExpectedMaterializationChecksum(
            result.OutputMaster.Text,
            result.Pieces.Count,
            result.Origins.Count,
            result.UnusedSources.Sum(region => region.Count));

    private static long A0ExpectedMaterializationChecksum(
        string output,
        int pieceCount,
        int originCount,
        int unusedRegionCount)
    {
        var checksum = A0Mix(A0Seed, output.Length);
        foreach (var character in output)
        {
            checksum = A0Mix(checksum, character);
        }

        checksum = A0Mix(checksum, pieceCount);
        checksum = A0Mix(checksum, originCount);
        return A0Mix(checksum, unusedRegionCount);
    }

    private static IReadOnlyDictionary<string, string> A0Parameters(
        params (string Name, string Value)[] values)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            parameters.Add(value.Name, value.Value);
        }

        return parameters;
    }

    private const long A0Seed = 1469598103934665603L;

    private static long A0Mix(long checksum, long value) =>
        unchecked((checksum ^ value) * 1099511628211L);

    private sealed record A0Workload(
        string Id,
        string Category,
        string Density,
        string OperationScope,
        IReadOnlyDictionary<string, string> Parameters,
        string ScalePosture,
        Func<long> Execute,
        Func<long> Reference);

    private sealed record A0Report(
        int SchemaVersion,
        string Protocol,
        DateTimeOffset RecordedAtUtc,
        string Configuration,
        A0RuntimeStamp Runtime,
        A0MeasurementPolicy Policy,
        IReadOnlyList<A0Measurement> Measurements,
        string Qualification);

    private sealed record A0RuntimeStamp(
        string FrameworkDescription,
        string RuntimeIdentifier,
        string OSDescription,
        string ProcessArchitecture,
        string ProcessorIdentifier,
        int LogicalProcessorCount,
        long StopwatchFrequency);

    private sealed record A0MeasurementPolicy(
        int WarmupCount,
        int RepetitionCount,
        string ElapsedStatistic,
        string AllocatedBytesMeasurement,
        string DifferentialPolicy);

    private sealed record A0Measurement(
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
