using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace Doccer.Tests;

internal static partial class Program
{
    private const int HarnessProtocolVersion = 1;
    private const string HarnessProtocol = "doccer-test-harness";
    private const string HarnessSuiteId = "doccer.contracts";
    private const int MaximumReceiptLength = 768;

    private static readonly JsonSerializerOptions HarnessJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly IReadOnlyList<HarnessCase> HarnessCases = CreateHarnessCases();

    private static readonly IReadOnlyDictionary<string, HarnessCase> HarnessCasesById =
        HarnessCases.ToDictionary(testCase => testCase.Id, StringComparer.Ordinal);

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return RunAllCases(showDetails: false);
        }

        if (args.Length == 1 && StringComparer.Ordinal.Equals(args[0], "--details"))
        {
            return RunAllCases(showDetails: true);
        }

        return args[0] switch
        {
            "list" => ListCases(args),
            "run" => RunSelectedCase(args),
            "help" or "--help" or "-h" => WriteHelp(),
            _ => UsageError($"Unknown command '{args[0]}'."),
        };
    }

    private static int RunAllCases(bool showDetails)
    {
        _checks = 0;
        HarnessCase? currentCase = null;

        try
        {
            foreach (var testCase in HarnessCases)
            {
                currentCase = testCase;
                testCase.Execute();
            }

            WriteReceipt(
                $"doccer test receipt: status=passed suite={HarnessSuiteId} " +
                $"cases={HarnessCases.Count} checks={_checks}");
            return 0;
        }
        catch (Exception exception)
        {
            WriteReceipt(
                $"doccer test receipt: status=failed suite={HarnessSuiteId} " +
                $"case={currentCase?.Id ?? "unknown"} checks={_checks} " +
                $"error={exception.GetType().Name}: {exception.Message}",
                Console.Error);
            if (showDetails)
            {
                Console.Error.WriteLine(exception);
            }

            return 1;
        }
    }

    private static int ListCases(string[] args)
    {
        var format = "text";
        if (args.Length == 3 && StringComparer.Ordinal.Equals(args[1], "--format"))
        {
            format = args[2];
        }
        else if (args.Length != 1)
        {
            return UsageError("Usage: Doccer.Tests list [--format text|json]");
        }

        if (StringComparer.Ordinal.Equals(format, "json"))
        {
            var document = new
            {
                SchemaVersion = HarnessProtocolVersion,
                Protocol = HarnessProtocol,
                SuiteId = HarnessSuiteId,
                Cases = HarnessCases.Select((testCase, ordinal) => new
                {
                    Ordinal = ordinal + 1,
                    testCase.Id,
                    testCase.DisplayName,
                    Concurrency = ConcurrencyName(testCase.Concurrency),
                }),
            };

            Console.WriteLine(JsonSerializer.Serialize(document, HarnessJsonOptions));
            return 0;
        }

        if (!StringComparer.Ordinal.Equals(format, "text"))
        {
            return UsageError($"Unknown list format '{format}'.");
        }

        Console.WriteLine($"Suite\t{HarnessSuiteId}");
        Console.WriteLine($"Cases\t{HarnessCases.Count}");
        foreach (var testCase in HarnessCases)
        {
            Console.WriteLine($"{testCase.Id}\t{ConcurrencyName(testCase.Concurrency)}\t{testCase.DisplayName}");
        }

        return 0;
    }

    private static int RunSelectedCase(string[] args)
    {
        string? caseId = null;
        var format = "text";
        var formatSpecified = false;
        var showDetails = false;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--case" when index + 1 < args.Length && caseId is null:
                    caseId = args[++index];
                    break;
                case "--format" when index + 1 < args.Length && !formatSpecified:
                    format = args[++index];
                    formatSpecified = true;
                    break;
                case "--details" when !showDetails:
                    showDetails = true;
                    break;
                default:
                    return UsageError($"Unknown, duplicate, or incomplete argument '{args[index]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(caseId))
        {
            return UsageError("Doccer.Tests run requires --case <stable-id>.");
        }

        if (!StringComparer.Ordinal.Equals(format, "text") &&
            !StringComparer.Ordinal.Equals(format, "json"))
        {
            return UsageError($"Unknown run format '{format}'.");
        }

        if (!HarnessCasesById.TryGetValue(caseId, out var testCase))
        {
            return UsageError($"Unknown case '{caseId}'.");
        }

        _checks = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            testCase.Execute();
            stopwatch.Stop();
            WriteCaseResult(testCase, "passed", stopwatch.Elapsed, format, exception: null);
            return 0;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            WriteCaseResult(testCase, "failed", stopwatch.Elapsed, format, exception);
            if (showDetails)
            {
                Console.Error.WriteLine(exception);
            }

            return 1;
        }
    }

    private static void WriteCaseResult(
        HarnessCase testCase,
        string status,
        TimeSpan elapsed,
        string format,
        Exception? exception)
    {
        if (StringComparer.Ordinal.Equals(format, "json"))
        {
            var document = new
            {
                SchemaVersion = HarnessProtocolVersion,
                Protocol = HarnessProtocol,
                SuiteId = HarnessSuiteId,
                CaseId = testCase.Id,
                Status = status,
                CheckCount = _checks,
                ElapsedMilliseconds = Math.Round(elapsed.TotalMilliseconds, 3),
                ErrorType = exception?.GetType().FullName,
                ErrorMessage = exception?.Message,
            };

            Console.WriteLine(JsonSerializer.Serialize(document, HarnessJsonOptions));
            return;
        }

        if (exception is null)
        {
            WriteReceipt(
                $"doccer test receipt: status=passed suite={HarnessSuiteId} " +
                $"case={testCase.Id} checks={_checks}");
        }
        else
        {
            WriteReceipt(
                $"doccer test receipt: status=failed suite={HarnessSuiteId} " +
                $"case={testCase.Id} checks={_checks} " +
                $"error={exception.GetType().Name}: {exception.Message}",
                Console.Error);
        }
    }

    private static int WriteHelp()
    {
        Console.WriteLine(
            """
            Doccer.Tests contract harness

            Usage:
              Doccer.Tests
              Doccer.Tests --details
              Doccer.Tests list [--format text|json]
              Doccer.Tests run --case <stable-id> [--format text|json] [--details]
              Doccer.Tests --help

            No arguments runs the complete catalog serially and emits one receipt.
            --details additionally emits exception detail after a failure receipt.
            """);
        return 0;
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Run 'Doccer.Tests --help' for usage.");
        return 2;
    }

    private static void WriteReceipt(string value, System.IO.TextWriter? writer = null)
    {
        writer ??= Console.Out;
        var singleLine = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (singleLine.Length > MaximumReceiptLength)
        {
            singleLine = singleLine[..(MaximumReceiptLength - 3)] + "...";
        }

        writer.WriteLine(singleLine);
    }

    private static IReadOnlyList<HarnessCase> CreateHarnessCases()
    {
        var cases = new HarnessCase[]
        {
            Parallel(nameof(MasterTopologyIsTotal), MasterTopologyIsTotal),
            Parallel(nameof(TilingReconstructsAndAgreesWithLines), TilingReconstructsAndAgreesWithLines),
            Parallel(nameof(LaminarAdmissionIsDeterministicAndStamped), LaminarAdmissionIsDeterministicAndStamped),
            Parallel(nameof(RunViewsTileTheMasterUnderEveryBreakKey), RunViewsTileTheMasterUnderEveryBreakKey),
            Parallel(nameof(LazySubstrateDefersUntouchedWork), LazySubstrateDefersUntouchedWork),
            Parallel(nameof(FrozenBatchPreservesClaims), FrozenBatchPreservesClaims),
            Parallel(nameof(InternedColumnsRoundTripClaimStrings), InternedColumnsRoundTripClaimStrings),
            Parallel(nameof(SpanSetObeysBooleanLawsAndMasterIdentity), SpanSetObeysBooleanLawsAndMasterIdentity),
            Parallel(nameof(SpanSetRandomizedLawsHold), SpanSetRandomizedLawsHold),
            Parallel(nameof(AllenRelationsAreCompleteAndInvertible), AllenRelationsAreCompleteAndInvertible),
            Parallel(nameof(AllenRelationSetHasAClosedValueSurface), AllenRelationSetHasAClosedValueSurface),
            Parallel(nameof(AllenRelationSetBooleanLawsHoldExhaustively), AllenRelationSetBooleanLawsHoldExhaustively),
            Parallel(nameof(AllenRelationSetConverseAgreesWithTheClassifier), AllenRelationSetConverseAgreesWithTheClassifier),
            Parallel(nameof(AllenClassifierIsJepdOnSixBoundaries), AllenClassifierIsJepdOnSixBoundaries),
            Parallel(nameof(AllenCompositionMatchesIndependentD6Oracle), AllenCompositionMatchesIndependentD6Oracle),
            Parallel(nameof(AllenCompositionLawsHold), AllenCompositionLawsHold),
            Parallel(nameof(AllenCanonicalCompositionIsNotFiniteMasterComposition), AllenCanonicalCompositionIsNotFiniteMasterComposition),
            Parallel(nameof(StructuralValidatorsKeepTheirDistinctInvariants), StructuralValidatorsKeepTheirDistinctInvariants),
            Parallel(nameof(ScopedRegexCollectionCannotBridgeGaps), ScopedRegexCollectionCannotBridgeGaps),
            Parallel(nameof(SuppressionIsAQueryWithIdempotenceAndDuality), SuppressionIsAQueryWithIdempotenceAndDuality),
            Parallel(nameof(DefectiveRuleFailsAtLoadTimeWithoutSideEffects), DefectiveRuleFailsAtLoadTimeWithoutSideEffects),
            Parallel(nameof(ExecutionScopeComposesWithTheCallerRegionSet), ExecutionScopeComposesWithTheCallerRegionSet),
            Parallel(nameof(JsonlInventoryLoadsAndFailsWithProvenance), JsonlInventoryLoadsAndFailsWithProvenance),
            Parallel(nameof(DeclarativeValidationRunsWithoutDomainCode), DeclarativeValidationRunsWithoutDomainCode),
            Parallel(nameof(CollectionCommitsAtomically), CollectionCommitsAtomically),
            Parallel(nameof(UnknownCaptureGroupFailsAtValidation), UnknownCaptureGroupFailsAtValidation),
            Parallel(nameof(UndefinedEnumValuesAreRejected), UndefinedEnumValuesAreRejected),
            Parallel(nameof(EmptySpansHaveSetSemantics), EmptySpansHaveSetSemantics),
            Parallel(nameof(ReferenceJoinRelatesEveryPair), ReferenceJoinRelatesEveryPair),
            Parallel(nameof(ProjectMapsSpansOntoLineRanges), ProjectMapsSpansOntoLineRanges),
            Parallel(nameof(EmitRunsHonorsACustomComparer), EmitRunsHonorsACustomComparer),
            Parallel(nameof(RegexOptionsUnionCultureInvariantAtTheEngineBoundary), RegexOptionsUnionCultureInvariantAtTheEngineBoundary),
            Parallel(nameof(SliceMintsAFragmentLocalChild), SliceMintsAFragmentLocalChild),
            Parallel(nameof(RebaseIsATotalBijection), RebaseIsATotalBijection),
            Parallel(nameof(RebaseCarriesSetsAndBatches), RebaseCarriesSetsAndBatches),
            Parallel(nameof(CollectionCommutesWithRebase), CollectionCommutesWithRebase),
            Parallel(nameof(SlicesCompose), SlicesCompose),
            Parallel(nameof(GroupingByKeyIsADeterministicPartition), GroupingByKeyIsADeterministicPartition),
            Parallel(nameof(ProjectionAndLineGroupsAreStampedTransposes), ProjectionAndLineGroupsAreStampedTransposes),
            Parallel(nameof(LineMembershipIsADeclaredPolicy), LineMembershipIsADeclaredPolicy),
            Parallel(nameof(GapCadenceMeasuresTheTemplateFacts), GapCadenceMeasuresTheTemplateFacts),
            Parallel(nameof(GapCadenceDeclaresItsBasis), GapCadenceDeclaresItsBasis),
            Parallel(nameof(LookupOrderIsAQueryPolicy), LookupOrderIsAQueryPolicy),
            Parallel(nameof(ClaimSelectionIsAnExactBatchValue), ClaimSelectionIsAnExactBatchValue),
            Parallel(nameof(ClaimSelectionBooleanLawsHoldExhaustively), ClaimSelectionBooleanLawsHoldExhaustively),
            Parallel(nameof(ClaimSelectionSeparatesMembershipFromOrderedProjection), ClaimSelectionSeparatesMembershipFromOrderedProjection),
            Parallel(nameof(SelectionPopulationIntegrationsShareOnePath), SelectionPopulationIntegrationsShareOnePath),
            Parallel(nameof(ClaimPairViewIsAnExactBasisStampedRelation), ClaimPairViewIsAnExactBasisStampedRelation),
            Parallel(nameof(ClaimPairViewProjectsSemijoinsAndConverse), ClaimPairViewProjectsSemijoinsAndConverse),
            Parallel(nameof(ClaimPairCompositionMatchesItsIndependentOracleAndWitnesses), ClaimPairCompositionMatchesItsIndependentOracleAndWitnesses),
            Parallel(nameof(ClaimPairCompositionLawsHoldOnBoundedRelations), ClaimPairCompositionLawsHoldOnBoundedRelations),
            Parallel(nameof(ClaimPairAllenAbstractionBridgeIsOneWay), ClaimPairAllenAbstractionBridgeIsOneWay),
            Parallel(nameof(PairingWitnessesTwoDelimiterFamilies), PairingWitnessesTwoDelimiterFamilies),
            Parallel(nameof(PairingFaultResidueIsCompleteAndTopOnly), PairingFaultResidueIsCompleteAndTopOnly),
            Parallel(nameof(PairingRefusesAmbiguousInputsAndRetainsItsStamps), PairingRefusesAmbiguousInputsAndRetainsItsStamps),
            Parallel(nameof(PairingMatchesAnIndependentBoundedStackOracle), PairingMatchesAnIndependentBoundedStackOracle),
            Parallel(nameof(LocatedRelationHasAConcreteBasisAndReferenceAlgebra), LocatedRelationHasAConcreteBasisAndReferenceAlgebra),
            Parallel(nameof(LocatedRelationMatchesBoundedExhaustiveOracles), LocatedRelationMatchesBoundedExhaustiveOracles),
            Parallel(nameof(LocatedRelationRebasesExactlyThroughSlices), LocatedRelationRebasesExactlyThroughSlices),
            Parallel(nameof(CandidateRegionGraphPreservesOccurrenceIdentityUntilProjection), CandidateRegionGraphPreservesOccurrenceIdentityUntilProjection),
            Parallel(nameof(ReachabilityViewKeepsGraphStampAndDiagnostics), ReachabilityViewKeepsGraphStampAndDiagnostics),
            Parallel(nameof(PartitionViewValidatesExactIdentityBearingPaths), PartitionViewValidatesExactIdentityBearingPaths),
            Parallel(nameof(FirstOrdinalSegmentationWitnessesRequiredCases), FirstOrdinalSegmentationWitnessesRequiredCases),
            Parallel(nameof(FirstOrdinalSegmentationMatchesBoundedPathOracle), FirstOrdinalSegmentationMatchesBoundedPathOracle),
            Parallel(nameof(AdditivePathPolicySnapshotsAnExactObjective), AdditivePathPolicySnapshotsAnExactObjective),
            Parallel(nameof(PathSelectionProblemValidatesExactAdmissibility), PathSelectionProblemValidatesExactAdmissibility),
            Parallel(nameof(AdditivePathSelectionRetainsDecisionsAndResiduals), AdditivePathSelectionRetainsDecisionsAndResiduals),
            Parallel(nameof(AdditivePathSelectionMatchesBoundedOptimizerOracle), AdditivePathSelectionMatchesBoundedOptimizerOracle),
            Parallel(nameof(StructuralValidatorsMatchBoundedOracles), StructuralValidatorsMatchBoundedOracles),
            Parallel(nameof(LaminarAdmissionMatchesBoundedOracle), LaminarAdmissionMatchesBoundedOracle),
            Parallel(nameof(NearestContainerProjectionIsExplicit), NearestContainerProjectionIsExplicit),
            Parallel(nameof(HierarchyViewRetainsExplicitDag), HierarchyViewRetainsExplicitDag),
            Parallel(nameof(HierarchyViewMatchesBoundedDagOracle), HierarchyViewMatchesBoundedDagOracle),
            Parallel(nameof(ResolutionMapsSeparateIncidenceFromAggregation), ResolutionMapsSeparateIncidenceFromAggregation),
            Parallel(nameof(ResolutionIncidenceMatchesBoundedEndpointOracle), ResolutionIncidenceMatchesBoundedEndpointOracle),
            Parallel(nameof(FactKeyIsAMasterRelativeSemanticValue), FactKeyIsAMasterRelativeSemanticValue),
            Parallel(nameof(CanonicalFactTableCollapsesAndOrdersProposals), CanonicalFactTableCollapsesAndOrdersProposals),
            Parallel(nameof(CanonicalFactTableEqualityIsProposalOrderIndependent), CanonicalFactTableEqualityIsProposalOrderIndependent),
            Parallel(nameof(FactReferenceIsAnExactTableHandle), FactReferenceIsAnExactTableHandle),
            Parallel(nameof(SupportEdgeIsAnOrderedEvidenceValue), SupportEdgeIsAnOrderedEvidenceValue),
            Parallel(nameof(SupportHypergraphValidatesExactBasesAndRetainsAlternatives), SupportHypergraphValidatesExactBasesAndRetainsAlternatives),
            Parallel(nameof(K5aHierarchyDiamondWitnessSuppliesAncestorSupport), K5aHierarchyDiamondWitnessSuppliesAncestorSupport),
            Parallel(nameof(GroundRuleIsAnOrderedGroundEvidenceValue), GroundRuleIsAnOrderedGroundEvidenceValue),
            Parallel(nameof(SaturationProblemValidatesAndCanonicalizesRules), SaturationProblemValidatesAndCanonicalizesRules),
            Parallel(nameof(FactSaturationHandlesFinitePositiveClosure), FactSaturationHandlesFinitePositiveClosure),
            Parallel(nameof(FactSaturationRetainsCompleteEnabledSupport), FactSaturationRetainsCompleteEnabledSupport),
            Parallel(nameof(FactSaturationRebasesThroughKeyOrderShifts), FactSaturationRebasesThroughKeyOrderShifts),
            Parallel(nameof(FactSaturationIsPermutationIndependent), FactSaturationIsPermutationIndependent),
            Parallel(nameof(K5bHierarchyDiamondSaturatesCanonically), K5bHierarchyDiamondSaturatesCanonically),
            Parallel(nameof(FactSaturationMatchesIndependentBoundedOracle), FactSaturationMatchesIndependentBoundedOracle),
            Parallel(nameof(BooleanVectorIsALogicalSequenceValue), BooleanVectorIsALogicalSequenceValue),
            Parallel(nameof(BooleanVectorAlgebraMatchesIndependentOracle), BooleanVectorAlgebraMatchesIndependentOracle),
            Parallel(nameof(BooleanPrefixParityMatchesIndependentOracle), BooleanPrefixParityMatchesIndependentOracle),
            Parallel(nameof(BooleanVectorLongAndTailCasesMatchOracle), BooleanVectorLongAndTailCasesMatchOracle),
            Parallel(nameof(Utf16UnitMaskEnforcesBasisAndTypedContinuity), Utf16UnitMaskEnforcesBasisAndTypedContinuity),
            Parallel(nameof(Utf16UnitClassificationPropagatesUncertainty), Utf16UnitClassificationPropagatesUncertainty),
            Parallel(nameof(Utf16UnitHarvestIsScalarSafeAndComplete), Utf16UnitHarvestIsScalarSafeAndComplete),
            Parallel(nameof(Utf16ClaimEmissionIsTransactionalAndEvidenceBearing), Utf16ClaimEmissionIsTransactionalAndEvidenceBearing),
            Parallel(nameof(BooleanVectorSupportsATestLocalByteBasis), BooleanVectorSupportsATestLocalByteBasis),
            Parallel(nameof(OriginBasisAndRelationAreExactCanonicalValues), OriginBasisAndRelationAreExactCanonicalValues),
            Parallel(nameof(OriginCompositionMatchesIndependentBooleanMatrixOracle), OriginCompositionMatchesIndependentBooleanMatrixOracle),
            Parallel(nameof(OriginProjectionPreservesMaterialShapeAndSlotIdentity), OriginProjectionPreservesMaterialShapeAndSlotIdentity),
            Parallel(nameof(TextSliceEmbedsAsExactFunctionalOrigin), TextSliceEmbedsAsExactFunctionalOrigin),
            Parallel(nameof(MaterializationConstructionSnapshotsAndRefusesInvalidPlans), MaterializationConstructionSnapshotsAndRefusesInvalidPlans),
            Parallel(nameof(MaterializationCoversRequiredMaterialShapes), MaterializationCoversRequiredMaterialShapes),
            Parallel(nameof(MaterializationPreservesUtf16AtomBoundaries), MaterializationPreservesUtf16AtomBoundaries),
            Parallel(nameof(MaterializationResultRetainsEvidenceAndComposesExactly), MaterializationResultRetainsEvidenceAndComposesExactly),
            Parallel(nameof(MaterializationMatchesIndependentFinitePlanOracle), MaterializationMatchesIndependentFinitePlanOracle),
        };

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var testCase in cases)
        {
            if (string.IsNullOrWhiteSpace(testCase.Id))
            {
                throw new InvalidOperationException("Harness case IDs cannot be empty.");
            }

            if (!ids.Add(testCase.Id))
            {
                throw new InvalidOperationException($"Duplicate harness case ID '{testCase.Id}'.");
            }
        }

        return Array.AsReadOnly(cases);
    }

    private static HarnessCase Parallel(string id, Action execute) =>
        new(id, id, HarnessConcurrency.Parallel, execute);

    private static string ConcurrencyName(HarnessConcurrency concurrency) =>
        concurrency switch
        {
            HarnessConcurrency.Parallel => "parallel",
            HarnessConcurrency.Exclusive => "exclusive",
            _ => throw new ArgumentOutOfRangeException(nameof(concurrency)),
        };
}

internal enum HarnessConcurrency
{
    Parallel,
    Exclusive,
}

internal sealed record HarnessCase(
    string Id,
    string DisplayName,
    HarnessConcurrency Concurrency,
    Action Execute);
