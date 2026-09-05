using System;
using System.Collections.Generic;
using System.Linq;
using Doccer;

namespace Doccer.Tests;

internal static partial class Program
{
    private static void CrossCarrierMultiFamilyPairingRetainsResidueAndReportsSeam()
    {
        var master = new TextMaster("cross-carrier-pairing", 0, ")([])(");
        var batch = PairingBatch(
            master,
            (new TextSpan(0, 1), "close", "round"),
            (new TextSpan(1, 2), "open", "round"),
            (new TextSpan(2, 3), "open", "square"),
            (new TextSpan(3, 4), "close", "square"),
            (new TextSpan(4, 5), "close", "round"),
            (new TextSpan(5, 6), "open", "round"));
        var opens = ClaimSelection.FromPredicate(batch, static record => record.Kind == "open");
        var closes = ClaimSelection.FromPredicate(batch, static record => record.Kind == "close");
        var policy = PairingPolicy.ByKey<string?>(
            "cross-carrier-delimiter-family",
            static record => record.RuleId,
            StringComparer.Ordinal);

        var result = Pairing.Pair(opens, closes, policy);

        True(
            ReferenceEquals(result.OpenInput, opens) &&
            ReferenceEquals(result.CloseInput, closes) &&
            ReferenceEquals(result.MatchEdges.LeftBasis, batch) &&
            ReferenceEquals(result.MatchEdges.RightBasis, batch),
            "cross-carrier pairing retains exact role selections and occurrence bases");
        True(
            result.MatchEdges.Any(edge => edge.LeftOrdinal == 2 && edge.RightOrdinal == 3) &&
            result.MatchEdges.Any(edge => edge.LeftOrdinal == 1 && edge.RightOrdinal == 4),
            "cross-carrier pairing accepts nested square and round families");
        True(
            result.Faults.DanglingCloses.SequenceEqual(new[] { 0 }) &&
            result.Faults.UnclosedOpens.SequenceEqual(new[] { 5 }) &&
            result.Faults.MismatchedPairs.IsEmpty,
            "cross-carrier pairing retains exact dangling and unclosed residue");
        True(
            result.MatchEdges.ProjectLeft().Union(result.Faults.OpenResidue).Equals(opens) &&
            result.MatchEdges.ProjectRight().Union(result.Faults.CloseResidue).Equals(closes),
            "cross-carrier pairing match and residue populations cover both inputs");

        var pairedRegions = result.PairedRegions();
        True(
            pairedRegions.Count == 1 && pairedRegions[0] == new TextSpan(1, 5) &&
            ReferenceEquals(pairedRegions.Master, master),
            "cross-carrier paired-region projection deliberately forgets the nested occurrence identities");
        True(
            !typeof(CandidateRegionGraph).GetMethods()
                .Any(method => method.Name == nameof(CandidateRegionGraph.Create) &&
                    method.GetParameters().Length > 0 &&
                    method.GetParameters()[0].ParameterType == typeof(SpanSet)),
            "cross-carrier pairing geometry has no identity-preserving direct graph composition");
        var compatibleMaster = new TextMaster(master.DocumentId, master.Revision, master.Text);
        var compatibleBatch = PairingBatch(
            compatibleMaster,
            (new TextSpan(0, 1), "close", "round"),
            (new TextSpan(1, 2), "open", "round"),
            (new TextSpan(2, 3), "open", "square"),
            (new TextSpan(3, 4), "close", "square"),
            (new TextSpan(4, 5), "close", "round"),
            (new TextSpan(5, 6), "open", "round"));
        True(master.IsCompatibleWith(compatibleMaster), "cross-carrier pairing replay adversary is text-compatible");
        Throws<InvalidOperationException>(
            () => result.MatchEdges.ComposePairs(ClaimPairView.Identity(compatibleBatch)),
            "cross-carrier pairing relation refuses a compatible recollected middle occurrence basis");

        CrossCarrierAssertSeamPacket(new CrossCarrierSeamPacket(
            "multi-family-pairing",
            "TextMaster; SpanBatch; ClaimSelection(open); ClaimSelection(close); PairingPolicy",
            "PairingResult; ClaimPairView; PairingFaults; SpanSet",
            "one exact TextMaster and one exact SpanBatch shared by both role selections and pair-view endpoints",
            "PairingResult.PairedRegions maps accepted occurrence pairs to normalized SpanSet envelopes and loses nested ordinals",
            "DanglingCloses={0}; UnclosedOpens={5}; MismatchedPairs=empty",
            policy.Name,
            "six non-overlapping token occurrences; one strict-stack pass",
            "bounded reference witness; no production performance claim",
            "portable replay needs source identity, ordered claim rows, exact role ordinals, and the delimiter-family policy definition; F2 is not supplied",
            "paired-region projection cannot preserve endpoints; compatible recollected middle basis is refused by ComposePairs"));
    }

    private static void CrossCarrierAmbiguousTwoPathGraphRetainsPoliciesAndReportsSeam()
    {
        var master = new TextMaster("cross-carrier-ambiguous-path", 0, "abc");
        var batch = PairBatch(
            master,
            new TextSpan(0, 1),
            new TextSpan(0, 2),
            new TextSpan(1, 3),
            new TextSpan(2, 3));
        var graph = CandidateRegionGraph.Create(ClaimSelection.All(batch), master.Extent);
        var costs = new long[] { 4, 1, 4, 1 };
        var policy = AdditivePathPolicy.Create(
            graph,
            "cross-carrier-two-path-minimum-penalty",
            "penalty-points",
            record => costs[record.Ordinal]);
        var problem = PathSelectionProblem.Create(graph, graph.Candidates, policy);
        var selected = PathSelection.Select(problem);
        var firstOrdinal = Segmentation.FirstOrdinalCompletePath(graph);

        True(
            firstOrdinal.Partition is not null &&
            firstOrdinal.Partition.SequenceEqual(new[] { 0, 2 }) &&
            selected.Partition is not null &&
            selected.Partition.SequenceEqual(new[] { 1, 3 }),
            "cross-carrier ambiguous graph retains two complete paths distinguished by explicit policy");
        True(
            ReferenceEquals(selected.Graph, graph) &&
            ReferenceEquals(selected.Partition!.Graph, graph) &&
            ReferenceEquals(selected.Policy, policy) &&
            ReferenceEquals(graph.Source, batch),
            "cross-carrier ambiguous result retains exact graph, batch, partition, and policy stamps");
        True(
            selected.Score == 2 && selected.ScoreUnit == "penalty-points" &&
            selected.SelectedCandidates.SequenceEqual(new[] { 1, 3 }) &&
            selected.RejectedCandidates.SequenceEqual(new[] { 0, 2 }) &&
            selected.ExcludedCandidates.IsEmpty,
            "cross-carrier ambiguous result exposes score and alternative-path residue");

        var geometry = graph.ToLocatedRelation();
        True(
            geometry.Count == 4 && ReferenceEquals(geometry.Master, master) &&
            geometry.Window == master.Extent,
            "cross-carrier graph projection retains geometry and window while forgetting candidate ordinals");

        var compatibleMaster = new TextMaster(master.DocumentId, master.Revision, master.Text);
        var compatibleBatch = PairBatch(
            compatibleMaster,
            new TextSpan(0, 1),
            new TextSpan(0, 2),
            new TextSpan(1, 3),
            new TextSpan(2, 3));
        True(master.IsCompatibleWith(compatibleMaster), "cross-carrier ambiguous replay adversary is text-compatible");
        Throws<InvalidOperationException>(
            () => PathSelectionProblem.Create(graph, ClaimSelection.All(compatibleBatch), policy),
            "cross-carrier ambiguous path refuses a compatible recollected occurrence basis");

        CrossCarrierAssertSeamPacket(new CrossCarrierSeamPacket(
            "ambiguous-two-path-graph",
            "TextMaster; SpanBatch; ClaimSelection; CandidateRegionGraph; AdditivePathPolicy; PathSelectionProblem",
            "PathSelectionResult; PartitionView; ClaimSelection residues; LocatedRelation projection",
            "exact four-row SpanBatch, exact CandidateRegionGraph value over [0,3), and exact policy graph stamp",
            "CandidateRegionGraph.ToLocatedRelation forgets edge ordinals; PartitionView retains the chosen ordinal path",
            "RejectedCandidates={0,2}; ExcludedCandidates=empty; feasibility residue=none",
            $"{policy.Name}; {policy.Guarantee}; {policy.TieBreak}; {problem.Feasibility}",
            "four candidate occurrences; exactly two complete paths; retained cost table has four entries",
            "bounded reference and dynamic-programming witness; no cross-batch invariance claim",
            "portable replay needs source identity, ordered batch rows, graph window/candidate ordinals, cost table, and tie policy; F2 is not supplied",
            "compatible-recollected-batch: exact ClaimSelection basis mismatch is refused"));
    }

    private static void CrossCarrierBudgetedChunksRetainAdapterMeasureCostAndReportsSeam()
    {
        const int maximumMeasure = 3;
        var master = new TextMaster("cross-carrier-budgeted-chunks", 0, "abcdef");
        var builder = new SpanBatchBuilder(master);
        builder.Add(new SpanClaim(new TextSpan(0, 2), "chunk", SpanLevel.Character, "adapter", 4, "two"));
        builder.Add(new SpanClaim(new TextSpan(0, 3), "chunk", SpanLevel.Character, "adapter", 1, "three"));
        builder.Add(new SpanClaim(new TextSpan(2, 6), "chunk", SpanLevel.Character, "adapter", 1, "four"));
        builder.Add(new SpanClaim(new TextSpan(2, 5), "chunk", SpanLevel.Character, "adapter", 4, "three"));
        builder.Add(new SpanClaim(new TextSpan(3, 6), "chunk", SpanLevel.Character, "adapter", 1, "three"));
        builder.Add(new SpanClaim(new TextSpan(5, 6), "chunk", SpanLevel.Character, "adapter", 1, "one"));
        builder.Add(new SpanClaim(new TextSpan(0, 6), "chunk", SpanLevel.Character, "adapter", 0, "six"));
        var batch = builder.Freeze();
        var graph = CandidateRegionGraph.Create(ClaimSelection.All(batch), master.Extent);
        var admitted = ClaimSelection.FromPredicate(
            batch,
            record => CrossCarrierChunkMeasure(record) <= maximumMeasure);
        var policy = AdditivePathPolicy.Create(
            graph,
            "cross-carrier-adapter-chunk-cost",
            "penalty-points",
            static record => record.Priority);
        var problem = PathSelectionProblem.Create(graph, admitted, policy);
        var result = PathSelection.Select(problem);

        True(
            admitted.SequenceEqual(new[] { 0, 1, 3, 4, 5 }) &&
            problem.ExcludedCandidates.SequenceEqual(new[] { 2, 6 }),
            "cross-carrier adapter measure separates admitted chunks from explicit resource residue");
        True(
            result.Partition is not null && result.Partition.SequenceEqual(new[] { 1, 4 }) &&
            result.Score == 2 && result.ScoreUnit == "penalty-points",
            "cross-carrier adapter cost selects the minimum-cost complete chunk path");
        True(
            result.SelectedCandidates.SequenceEqual(new[] { 1, 4 }) &&
            result.RejectedCandidates.SequenceEqual(new[] { 0, 3, 5 }) &&
            result.ExcludedCandidates.SequenceEqual(new[] { 2, 6 }),
            "cross-carrier chunk result separates selected, rejected-admissible, and over-budget populations");
        True(
            ReferenceEquals(problem.Graph, graph) && ReferenceEquals(problem.Source, batch) &&
            ReferenceEquals(problem.Policy, policy) && ReferenceEquals(result.Problem, problem),
            "cross-carrier chunk problem retains exact graph, batch, adapter-cost, and result stamps");

        var tooSmall = ClaimSelection.FromPredicate(batch, record => CrossCarrierChunkMeasure(record) <= 1);
        var failed = PathSelection.Select(PathSelectionProblem.Create(graph, tooSmall, policy));
        True(
            !failed.IsComplete && failed.Residual is not null &&
            ReferenceEquals(failed.Residual.Policy, policy) &&
            failed.Residual.CoverageGaps.Count > 0,
            "cross-carrier too-small resource admission returns stamped failed-path evidence");
        var compatibleMaster = new TextMaster(master.DocumentId, master.Revision, master.Text);
        var compatibleBuilder = new SpanBatchBuilder(compatibleMaster);
        foreach (var record in batch)
        {
            compatibleBuilder.Add(record.ToClaim());
        }

        var compatibleBatch = compatibleBuilder.Freeze();
        var compatibleGraph = CandidateRegionGraph.Create(
            ClaimSelection.All(compatibleBatch),
            compatibleMaster.Extent);
        var compatiblePolicy = AdditivePathPolicy.Create(
            compatibleGraph,
            policy.Name,
            policy.Unit,
            static record => record.Priority);
        True(master.IsCompatibleWith(compatibleMaster), "cross-carrier chunk replay adversary is text-compatible");
        Throws<InvalidOperationException>(
            () => PathSelectionProblem.Create(graph, admitted, compatiblePolicy),
            "cross-carrier chunk problem refuses a policy stamped by a compatible recollected graph");

        CrossCarrierAssertSeamPacket(new CrossCarrierSeamPacket(
            "budgeted-flat-chunks",
            "TextMaster; SpanBatch; CandidateRegionGraph; adapter-measured ClaimSelection; AdditivePathPolicy; PathSelectionProblem",
            "PathSelectionResult; PartitionView; selected/rejected/excluded ClaimSelections; PathSelectionResidual",
            "exact seven-row SpanBatch and all-candidate graph over [0,6); every subset remains on that exact batch",
            "CandidateRegionGraph.ToLocatedRelation is available but loses candidate identity; selection partitions retain ordinals",
            "RejectedCandidates={0,3,5}; ExcludedCandidates={2,6}; max-measure=1 produces explicit coverage residual",
            $"measure=cross-carrier-utf16-span-length; objective={policy.Name}; unit={policy.Unit}",
            $"maximumMeasure={maximumMeasure}; seven candidates; additive score bound={batch.Sum(record => record.Priority)}",
            "bounded exact reference selection; adapter measure/cost meanings are not kernel semantics",
            "portable replay needs source/batch identity, measure algorithm/version, threshold, retained cost table, graph window, and tie policy; F2 is not supplied",
            "maximumMeasure=1 returns PathSelectionResidual; compatible recollected graph policy is refused"));
    }

    private static void CrossCarrierFixedMacroSubstitutionComposesOriginsAndReportsSeam()
    {
        var root = new TextMaster("cross-carrier-fixed-macro-root", 3, "say: @!");
        var slice = TextSlice.Create(root, new TextSpan(5, 7));
        var rootBasis = CrossCarrierSingletonBasis("root", root);
        var sliceBasis = CrossCarrierSingletonBasis("macro-body", slice.Child);
        var sliceOrigins = OriginRelation.FromTextSlice(slice, sliceBasis, rootBasis);
        var macroPlan = RewritePlan.Create(
            sliceBasis,
            new MaterializationTarget("cross-carrier-fixed-macro-output", 4, "expanded"),
            new[]
            {
                OutputPiece.OriginMapped(
                    "hi",
                    new[]
                    {
                        new PieceOrigin(0, new OriginAtom(0, 0)),
                        new PieceOrigin(1, new OriginAtom(0, 0)),
                    }),
                OutputPiece.Copy(0, new TextSpan(1, 2)),
            });
        var materialized = RewriteMaterialization.Materialize(macroPlan);
        var composed = materialized.Origins.ComposeOrigins(sliceOrigins);

        Equal("hi!", materialized.OutputMaster.Text, "cross-carrier fixed macro substitution produces a new master");
        True(
            ReferenceEquals(materialized.Plan, macroPlan) &&
            ReferenceEquals(materialized.Origins.SourceBasis, sliceBasis) &&
            ReferenceEquals(composed.OutputBasis, materialized.OutputBasis) &&
            ReferenceEquals(composed.SourceBasis, rootBasis),
            "cross-carrier fixed macro substitution retains every exact plan and origin basis stamp");
        True(
            CrossCarrierHasOrigin(composed, 0, 0, 0, 5) &&
            CrossCarrierHasOrigin(composed, 0, 1, 0, 5) &&
            CrossCarrierHasOrigin(composed, 0, 2, 0, 6) &&
            composed.Count == 3,
            "cross-carrier composed origins trace expanded and copied atoms to the root document");
        True(
            materialized.UnusedSources.Count == 1 && materialized.UnusedSources[0].Count == 0,
            "cross-carrier macro materialization accounts for all slice-source material");

        var scopeResidue = SpanSet.Whole(root).Subtract(
            SpanSet.Create(root, new[] { slice.Window }));
        True(
            scopeResidue.SequenceEqual(new[] { new TextSpan(0, 5) }),
            "cross-carrier macro recipe names root material outside the selected slice as scope residue");
        var projection = composed.ProjectSources(0, materialized.OutputMaster.Extent);
        True(
            projection.Count == 1 && projection[0].SequenceEqual(new[] { new TextSpan(5, 7) }),
            "cross-carrier origin projection recovers the used root region while forgetting atom-edge multiplicity");

        var valueIdenticalSliceBasis = OriginBasis.Create(sliceBasis.Slots);
        Throws<InvalidOperationException>(
            () => OriginRelation.Identity(valueIdenticalSliceBasis).ComposeOrigins(sliceOrigins),
            "cross-carrier fixed macro composition refuses a value-identical middle-basis clone");

        CrossCarrierAssertSeamPacket(new CrossCarrierSeamPacket(
            "fixed-macro-substitution",
            "root TextMaster; TextSlice; slice/root OriginBasis values; RewritePlan with OriginMapped and Copy pieces",
            "MaterializationResult; new output TextMaster; stage OriginRelation; composed root OriginRelation; SpanSet projection",
            "exact slice child basis is both plan source and slice-origin output; materialization output basis becomes composed output",
            "OriginRelation.ProjectSources normalizes output-atom edges to one root SpanSet and forgets multiplicity",
            "MaterializationResult.UnusedSources=empty; root scope outside slice=[0,5)",
            "fixed table @ -> hi; left-to-right two-piece RewritePlan; exact MaterializationTarget identity",
            "one fixed substitution, two positive pieces, one relational composition",
            "bounded exact recipe; no general macro language or recursive engine policy",
            "portable replay needs root content identity, slice window, basis tags, macro table, ordered plan pieces, target identity, and origin edges; F2 is not supplied",
            "value-identical-slice-basis-clone: OriginRelation.ComposeOrigins requires the exact shared object"));
    }

    private static void CrossCarrierRecursiveExpansionStopsAtResourceBoundaryAndReportsSeam()
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["A"] = "x${B}",
            ["B"] = "y${C}",
            ["C"] = "z",
        };
        var root = new TextMaster("cross-carrier-recursive-root", 0, "${A}");
        var rootBasis = CrossCarrierSingletonBasis("root", root);
        var depthPolicy = new CrossCarrierExpansionPolicy(
            "cross-carrier-leftmost-document-macro",
            MaxDepth: 2,
            MaxOutputUtf16Units: 32);
        var bounded = CrossCarrierExpandDocument(root, rootBasis, definitions, depthPolicy);

        True(
            bounded.Stop == CrossCarrierExpansionStop.DepthLimit &&
            bounded.DepthUsed == 2 && bounded.Materializations.Count == 2 &&
            bounded.OutputMaster.Text == "xy${C}",
            "cross-carrier recursive adapter stops before a third expansion at its exact depth limit");
        True(
            ReferenceEquals(bounded.OutputBasis, bounded.ComposedOrigins.OutputBasis) &&
            ReferenceEquals(rootBasis, bounded.ComposedOrigins.SourceBasis) &&
            bounded.ComposedOrigins.IsTotal,
            "cross-carrier recursive adapter reuses exact stage bases for total root-origin composition");
        True(
            ReferenceEquals(bounded.UnexpandedMacros.Basis.Master, bounded.OutputMaster) &&
            bounded.UnexpandedMacros.Count == 1 &&
            bounded.UnexpandedMacros.Single() == 0 &&
            bounded.UnexpandedMacros.Basis[0].Span == new TextSpan(2, 6) &&
            bounded.UnexpandedMacros.Basis[0].RuleId == "C",
            "cross-carrier depth residue retains the exact output occurrence basis and unresolved macro name");

        var complete = CrossCarrierExpandDocument(
            root,
            rootBasis,
            definitions,
            depthPolicy with { MaxDepth = 3 });
        True(
            complete.Stop == CrossCarrierExpansionStop.Completed && complete.DepthUsed == 3 &&
            complete.OutputMaster.Text == "xyz" && complete.UnexpandedMacros.IsEmpty,
            "cross-carrier recursion completes only when the external policy grants the third step");

        var outputBounded = CrossCarrierExpandDocument(
            root,
            rootBasis,
            definitions,
            depthPolicy with { MaxDepth = 8, MaxOutputUtf16Units = 4 });
        True(
            outputBounded.Stop == CrossCarrierExpansionStop.OutputUnitLimit &&
            outputBounded.DepthUsed == 0 && outputBounded.OutputMaster.Text == "${A}" &&
            outputBounded.UnexpandedMacros.Count == 1,
            "cross-carrier recursive adapter returns unresolved residue before exceeding its output resource");

        var firstStage = bounded.Materializations[0];
        var clonedFirstStageBasis = OriginBasis.Create(firstStage.OutputBasis.Slots);
        Throws<InvalidOperationException>(
            () => OriginRelation.Identity(clonedFirstStageBasis).ComposeOrigins(firstStage.Origins),
            "cross-carrier recursive stage composition refuses a value-identical middle-basis clone");

        CrossCarrierAssertSeamPacket(new CrossCarrierSeamPacket(
            "resource-bounded-recursive-expansion",
            "TextMaster; document-supplied definition table; test-local cross-carrier expansion policy; per-stage RewritePlan and OriginBasis",
            "ordered MaterializationResult stages; final TextMaster; composed root OriginRelation; unresolved-macro ClaimSelection",
            "each stage reuses the preceding MaterializationResult.OutputBasis exactly; residue uses an exact final-output SpanBatch",
            "composed OriginRelation.ProjectSources may forget atom-edge multiplicity; no stage or unresolved occurrence is projected away internally",
            "depth-limited run retains ${C}; output-limited run retains ${A}; complete run has empty residue",
            $"{depthPolicy.Name}; leftmost occurrence; definitions supplied by document adapter",
            $"MaxDepth={depthPolicy.MaxDepth}; MaxOutputUtf16Units={depthPolicy.MaxOutputUtf16Units}; DepthUsed={bounded.DepthUsed}",
            "bounded adapter orchestration over exact kernel calls; no recursive-expansion performance or language claim",
            "portable replay needs root identity, ordered definition bytes, parser/policy version, resource limits, stage targets, basis tags, and every origin relation; F2 is not supplied",
            "depth/output exhaustion returns typed test-recipe residue; cloned stage bases are refused by exact origin composition"));
    }

    private static int CrossCarrierChunkMeasure(SpanRecord record) => record.Span.Length;

    private static OriginBasis CrossCarrierSingletonBasis(string tag, TextMaster master) =>
        OriginBasis.Create(new[] { new OriginSlot(tag, master) });

    private static bool CrossCarrierHasOrigin(
        OriginRelation relation,
        int outputSlot,
        int outputAtom,
        int sourceSlot,
        int sourceAtom) =>
        relation.Any(edge =>
            edge.Output == new OriginAtom(outputSlot, outputAtom) &&
            edge.Source == new OriginAtom(sourceSlot, sourceAtom));

    private static void CrossCarrierAssertSeamPacket(CrossCarrierSeamPacket packet)
    {
        var fields = new[]
        {
            packet.WitnessId,
            packet.InputSorts,
            packet.ResultSorts,
            packet.ExactBases,
            packet.IdentityForgettingProjections,
            packet.Residue,
            packet.PolicyStamp,
            packet.ResourceStamp,
            packet.ScalePosture,
            packet.PortabilityRequirements,
            packet.FailedComposition,
        };
        True(fields.All(static field => !string.IsNullOrWhiteSpace(field)),
            $"{packet.WitnessId} seam packet fills every cross-carrier closure field");
        True(packet.ScalePosture.Contains("bounded", StringComparison.OrdinalIgnoreCase),
            $"{packet.WitnessId} declares bounded scale posture");
        True(packet.PortabilityRequirements.Contains("F2", StringComparison.Ordinal),
            $"{packet.WitnessId} distinguishes portability requirements from current replay");
    }

    private static CrossCarrierRecursiveExpansionOutcome CrossCarrierExpandDocument(
        TextMaster root,
        OriginBasis rootBasis,
        IReadOnlyDictionary<string, string> definitions,
        CrossCarrierExpansionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(rootBasis);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.MaxDepth < 0 || policy.MaxOutputUtf16Units < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        if (rootBasis.Count != 1 || !ReferenceEquals(rootBasis[0].Master, root))
        {
            throw new InvalidOperationException(
                "cross-carrier recursive expansion requires one exact root basis slot.");
        }

        var currentMaster = root;
        var currentBasis = rootBasis;
        var currentToRoot = OriginRelation.Identity(rootBasis);
        var materializations = new List<MaterializationResult>();
        var depth = 0;
        var stop = CrossCarrierExpansionStop.Completed;

        while (true)
        {
            var macros = CrossCarrierFindMacroOccurrences(currentMaster.Text);
            if (macros.Count == 0)
            {
                stop = CrossCarrierExpansionStop.Completed;
                break;
            }

            var next = macros[0];
            if (!definitions.TryGetValue(next.Name, out var replacement))
            {
                stop = CrossCarrierExpansionStop.UnknownMacro;
                break;
            }

            if (depth >= policy.MaxDepth)
            {
                stop = CrossCarrierExpansionStop.DepthLimit;
                break;
            }

            var projectedLength = checked(currentMaster.Length - next.Span.Length + replacement.Length);
            if (projectedLength > policy.MaxOutputUtf16Units)
            {
                stop = CrossCarrierExpansionStop.OutputUnitLimit;
                break;
            }

            var pieces = new List<OutputPiece>();
            if (next.Span.Start > 0)
            {
                pieces.Add(OutputPiece.Copy(0, new TextSpan(0, next.Span.Start)));
            }

            pieces.Add(CrossCarrierMappedReplacement(currentMaster, next.Span, replacement));
            if (next.Span.End < currentMaster.Length)
            {
                pieces.Add(OutputPiece.Copy(0, new TextSpan(next.Span.End, currentMaster.Length)));
            }

            var plan = RewritePlan.Create(
                currentBasis,
                new MaterializationTarget(
                    $"{root.DocumentId}:expansion:{depth + 1}",
                    root.Revision,
                    $"expanded-{depth + 1}"),
                pieces);
            var materialized = RewriteMaterialization.Materialize(plan);
            currentToRoot = materialized.Origins.ComposeOrigins(currentToRoot);
            materializations.Add(materialized);
            currentMaster = materialized.OutputMaster;
            currentBasis = materialized.OutputBasis;
            depth++;
        }

        var residueBuilder = new SpanBatchBuilder(currentMaster);
        foreach (var macro in CrossCarrierFindMacroOccurrences(currentMaster.Text))
        {
            residueBuilder.Add(new SpanClaim(
                macro.Span,
                "unexpanded-macro",
                SpanLevel.Character,
                policy.Name,
                RuleId: macro.Name));
        }

        var residueBasis = residueBuilder.Freeze();
        return new CrossCarrierRecursiveExpansionOutcome(
            policy,
            stop,
            depth,
            currentMaster,
            currentBasis,
            currentToRoot,
            materializations.AsReadOnly(),
            ClaimSelection.All(residueBasis));
    }

    private static OutputPiece CrossCarrierMappedReplacement(
        TextMaster source,
        TextSpan macroSpan,
        string replacement)
    {
        var sourceAtoms = new List<int>();
        for (var ordinal = 0; ordinal < source.Topology.AtomCount; ordinal++)
        {
            if (macroSpan.Contains(source.Topology.Atoms[ordinal].Span))
            {
                sourceAtoms.Add(ordinal);
            }
        }

        var replacementMaster = new TextMaster("cross-carrier-replacement", 0, replacement);
        var origins = new List<PieceOrigin>();
        for (var outputAtom = 0; outputAtom < replacementMaster.Topology.AtomCount; outputAtom++)
        {
            foreach (var sourceAtom in sourceAtoms)
            {
                origins.Add(new PieceOrigin(outputAtom, new OriginAtom(0, sourceAtom)));
            }
        }

        return OutputPiece.OriginMapped(replacement, origins);
    }

    private static IReadOnlyList<CrossCarrierMacroOccurrence> CrossCarrierFindMacroOccurrences(string text)
    {
        var occurrences = new List<CrossCarrierMacroOccurrence>();
        var cursor = 0;
        while (cursor < text.Length)
        {
            var start = text.IndexOf("${", cursor, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            var end = text.IndexOf('}', start + 2);
            if (end < 0)
            {
                break;
            }

            var name = text.Substring(start + 2, end - start - 2);
            if (name.Length > 0)
            {
                occurrences.Add(new CrossCarrierMacroOccurrence(name, new TextSpan(start, end + 1)));
            }

            cursor = end + 1;
        }

        return occurrences.AsReadOnly();
    }

    private sealed record CrossCarrierSeamPacket(
        string WitnessId,
        string InputSorts,
        string ResultSorts,
        string ExactBases,
        string IdentityForgettingProjections,
        string Residue,
        string PolicyStamp,
        string ResourceStamp,
        string ScalePosture,
        string PortabilityRequirements,
        string FailedComposition);

    private sealed record CrossCarrierExpansionPolicy(
        string Name,
        int MaxDepth,
        int MaxOutputUtf16Units);

    private enum CrossCarrierExpansionStop
    {
        Completed,
        DepthLimit,
        OutputUnitLimit,
        UnknownMacro,
    }

    private readonly record struct CrossCarrierMacroOccurrence(string Name, TextSpan Span);

    private sealed record CrossCarrierRecursiveExpansionOutcome(
        CrossCarrierExpansionPolicy Policy,
        CrossCarrierExpansionStop Stop,
        int DepthUsed,
        TextMaster OutputMaster,
        OriginBasis OutputBasis,
        OriginRelation ComposedOrigins,
        IReadOnlyList<MaterializationResult> Materializations,
        ClaimSelection UnexpandedMacros);
}
