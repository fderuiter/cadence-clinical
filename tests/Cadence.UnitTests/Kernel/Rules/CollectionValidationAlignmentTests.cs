using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Rules;

/// <summary>
/// Collection validation alignment, longitudinal <c>prev.*</c> checks, UTC temporal expression comparisons,
/// protocol-version rule-pack filtering, and finding pagination (PRD-QRY-003, PRD-QRY-004, K-RULE-4, K-TIME-8).
/// </summary>
[Trait("Category", "Unit")]
public sealed class CollectionValidationAlignmentTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly StudyId Study = StudyId.New();
    private static readonly StudySiteId Site = StudySiteId.New();
    private static readonly ParticipantId Participant = ParticipantId.New();
    private static readonly EnrollmentId Enrollment = EnrollmentId.New();
    private static readonly DateOnly AsOf = new(2026, 3, 15);
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Theory(DisplayName = "Expression_Iso8601TemporalComparisons_OrderAndEquateInUtc")]
    [KernelRule("K-RULE-4")]
    [InlineData("""{"op":"==","args":["2026-03-01T23:30:00-02:00","2026-03-02T01:30:00Z"]}""", true)]
    [InlineData("""{"op":"<","args":["2026-03-01T23:30:00-02:00","2026-03-02T01:30:00Z"]}""", false)]
    [InlineData("""{"op":">","args":["2026-03-01T23:30:00-05:00","2026-03-02T02:00:00Z"]}""", true)]
    [InlineData("""{"op":"<","args":["2026-03-02T02:00:00+03:00","2026-03-02T00:30:00Z"]}""", true)]
    [InlineData("""{"op":"==","args":["2026-03-02","2026-03-02T00:00:00+00:00"]}""", true)]
    [InlineData("""{"op":">","args":["2026-03-02","2026-03-01T23:59:59Z"]}""", true)]
    [InlineData("""{"op":">","args":["2026-03-02T10:00:00.250Z","2026-03-02T10:00:00.050Z"]}""", true)]
    [InlineData("""{"op":">","args":["VIS-02","VIS-01"]}""", true)]
    [InlineData("""{"op":"<","args":["2026-13-45","2026-99-99"]}""", true)]
    public void ExpressionIso8601TemporalComparisonsOrderAndEquateInUtc(string json, bool expected)
    {
        using var doc = JsonDocument.Parse(json);
        var expr = RuleExpressionJson.Parse(doc.RootElement);
        var record = new RuleRecord("AE", "row-1", new Dictionary<string, RuleValue>(StringComparer.Ordinal));

        RuleExpressionCompiler.Evaluate(expr, record).Should().Be(RuleValue.FromBoolean(expected));
    }

    [Fact(DisplayName = "CrossFormAlignment_BindsSameScheduledVisit_AndPrefersPopulatedNearestUnscheduledLog")]
    [GxPRequirement("PRD-QRY-003")]
    public async Task CrossFormAlignmentBindsSameScheduledVisitAndPrefersPopulatedNearestUnscheduledLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var pv1 = ProtocolVersionId.New();

        // Visit 1 physical exam has clinically significant finding ("Y"); Visit 1 vitals has normal SYSBP = 118.
        var peV1 = CreateForm(
            pv1,
            "cs.physical_exam",
            plannedActivityId: "act.visit1",
            collectedAt: T0,
            items: [Item("item.pe_clin_sig")],
            values: [Val("item.pe_clin_sig", new CodedValue(CodeSystemVersionId.New(), "Y"))]);
        var vsV1 = CreateForm(
            pv1,
            "cs.vital_signs",
            plannedActivityId: "act.visit1",
            collectedAt: T0.AddMinutes(5),
            items: [Item("item.sysbp")],
            values: [Val("item.sysbp", new QuantityValue(118m, "mm[Hg]"))]);

        // Visit 2 vitals has high SYSBP = 235; must NOT bleed into Visit 1's cross-form check.
        var vsV2 = CreateForm(
            pv1,
            "cs.vital_signs",
            plannedActivityId: "act.visit2",
            collectedAt: T0.AddDays(14),
            items: [Item("item.sysbp")],
            values: [Val("item.sysbp", new QuantityValue(235m, "mm[Hg]"))]);

        // Two unscheduled AE logs: one populated near Visit 1, and a second blank AE log opened later.
        var aePopulatedNearV1 = CreateForm(
            pv1,
            "cs.adverse_event",
            plannedActivityId: null,
            collectedAt: T0.AddMinutes(20),
            items: [Item("item.ae_term")],
            values: [Val("item.ae_term", new TextValue("Syncope"))]);
        var aeLaterPopulated = CreateForm(
            pv1,
            "cs.adverse_event",
            plannedActivityId: null,
            collectedAt: T0.AddDays(30),
            items: [Item("item.ae_term")],
            values: [Val("item.ae_term", new TextValue("Headache"))]);
        var aeSecondBlank = CreateForm(
            pv1,
            "cs.adverse_event",
            plannedActivityId: null,
            collectedAt: T0.AddMinutes(10),
            items: [Item("item.ae_term")],
            values: []);

        var packId = LibraryItemId.New();
        const string packJson = """
            {
              "authority": "Study",
              "title": "Visit alignment checks",
              "rules": [
                {
                  "id": "PE-CLIN-SIG-AE",
                  "version": 1,
                  "citation": { "document": "Protocol CAD-101", "section": "6.1" },
                  "severity": "Error",
                  "applicability": { "stage": "Collection", "dataset": "cs.physical_exam" },
                  "condition": {
                    "op": "and",
                    "args": [
                      { "op": "==", "args": [{ "var": "item.pe_clin_sig" }, "Y"] },
                      { "op": "is_empty", "args": [{ "var": "cs.adverse_event.item.ae_term" }] }
                    ]
                  },
                  "explanation": "Clinically significant PE abnormality requires an Adverse Event record."
                },
                {
                  "id": "PE-VISIT-SYSBP-BLEED",
                  "version": 1,
                  "citation": { "document": "Protocol CAD-101", "section": "6.2" },
                  "severity": "Warning",
                  "applicability": { "stage": "Collection", "dataset": "cs.physical_exam" },
                  "condition": { "op": ">", "args": [{ "var": "cs.vital_signs.item.sysbp" }, 180] },
                  "explanation": "Same-visit systolic BP exceeded 180."
                },
                {
                  "id": "PE-NEAREST-AE-TERM",
                  "version": 1,
                  "citation": { "document": "Protocol CAD-101", "section": "6.3" },
                  "severity": "Notice",
                  "applicability": { "stage": "Collection", "dataset": "cs.physical_exam" },
                  "condition": { "op": "!=", "args": [{ "var": "cs.adverse_event.item.ae_term" }, "Syncope"] },
                  "explanation": "Expected nearest AE term Syncope."
                }
              ]
            }
            """;

        var harness = new ValidationHarness(
            [peV1, vsV1, vsV2, aePopulatedNearV1, aeLaterPopulated, aeSecondBlank],
            [CreateLibraryPack(packId, "visit-alignment-pack", packJson, governingVersions: [])]);

        var preview = await harness.Service.PreviewRulePackAsync(Tenant, Study, packId, AsOf, ct);

        preview.FormsEvaluated.Should().Be(6);
        preview.Violations.Should().BeEmpty(
            "PE-CLIN-SIG-AE must bind the populated AE log over the blank AE log, nearest AE must be Syncope, and Visit 2 vitals must not bleed into Visit 1");
    }

    [Fact(DisplayName = "RepeatVisitChecks_BindPreviousVisitValuesChronologically")]
    [GxPRequirement("PRD-QRY-004")]
    public async Task RepeatVisitChecksBindPreviousVisitValuesChronologically()
    {
        var ct = TestContext.Current.CancellationToken;
        var pv1 = ProtocolVersionId.New();

        var visit1 = CreateForm(
            pv1,
            "cs.vital_signs",
            plannedActivityId: "act.v1",
            collectedAt: T0,
            items: [Item("item.weight")],
            values: [Val("item.weight", new QuantityValue(80m, "kg"))]);
        var visit2 = CreateForm(
            pv1,
            "cs.vital_signs",
            plannedActivityId: "act.v2",
            collectedAt: T0.AddDays(7),
            items: [Item("item.weight")],
            values: [Val("item.weight", new QuantityValue(83m, "kg"))]);
        var visit3 = CreateForm(
            pv1,
            "cs.vital_signs",
            plannedActivityId: "act.v3",
            collectedAt: T0.AddDays(14),
            items: [Item("item.weight")],
            values: [Val("item.weight", new QuantityValue(96m, "kg"))]);

        var packId = LibraryItemId.New();
        const string packJson = """
            {
              "authority": "Study",
              "title": "Longitudinal weight checks",
              "rules": [
                {
                  "id": "WT-DELTA-GT-10",
                  "version": 1,
                  "citation": { "document": "Protocol CAD-101", "section": "6.4" },
                  "severity": "Warning",
                  "applicability": { "stage": "Collection", "dataset": "cs.vital_signs" },
                  "condition": {
                    "op": "and",
                    "args": [
                      { "op": "is_not_empty", "args": [{ "var": "prev.weight" }] },
                      { "op": "==", "args": [{ "var": "prev.item.weight" }, { "var": "prev.cs.vital_signs.item.weight" }] },
                      { "op": ">", "args": [{ "op": "-", "args": [{ "var": "item.weight" }, { "var": "prev.weight" }] }, 10] }
                    ]
                  },
                  "explanation": "Weight increased by more than 10 kg since the previous visit."
                }
              ]
            }
            """;

        // Deliberately store out of chronological order to verify sorting by CollectedAt/OpenedAt/Id.
        var harness = new ValidationHarness(
            [visit3, visit1, visit2],
            [CreateLibraryPack(packId, "longitudinal-weight-pack", packJson, governingVersions: [])]);

        var preview = await harness.Service.PreviewRulePackAsync(Tenant, Study, packId, AsOf, ct);

        preview.Violations.Should().ContainSingle().Which.Should().Match<CollectionRulePackPreviewViolation>(v =>
            v.FormId == visit3.CollectionInstanceId
            && v.RuleId == "WT-DELTA-GT-10"
            && v.ItemId == "item.weight");
    }

    [Fact(DisplayName = "ProtocolVersionFiltering_EvaluatesOnlyGoverningOrUnlinkedRulePacks")]
    [KernelRule("K-TIME-8")]
    public async Task ProtocolVersionFilteringEvaluatesOnlyGoverningOrUnlinkedRulePacks()
    {
        var ct = TestContext.Current.CancellationToken;
        var pv1 = ProtocolVersionId.New();
        var pv2 = ProtocolVersionId.New();

        var formV1 = CreateForm(
            pv1,
            "cs.vital_signs",
            plannedActivityId: "act.v1",
            collectedAt: T0,
            items: [Item("item.sysbp")],
            values: [Val("item.sysbp", new QuantityValue(210m, "mm[Hg]"))]);
        var formV2 = CreateForm(
            pv2,
            "cs.vital_signs",
            plannedActivityId: "act.v1",
            collectedAt: T0.AddDays(1),
            items: [Item("item.sysbp")],
            values: [Val("item.sysbp", new QuantityValue(210m, "mm[Hg]"))]);

        var packPv1Id = LibraryItemId.New();
        var packPv2Id = LibraryItemId.New();
        var packSharedId = LibraryItemId.New();

        static string MakePack(string key, string ruleId) => $$"""
            {
              "authority": "Study",
              "title": "{{key}}",
              "rules": [
                {
                  "id": "{{ruleId}}",
                  "version": 1,
                  "citation": { "document": "Protocol CAD-101", "section": "6.5" },
                  "severity": "Warning",
                  "applicability": { "stage": "Collection", "dataset": "cs.vital_signs" },
                  "condition": { "op": ">", "args": [{ "var": "item.sysbp" }, 200] },
                  "explanation": "SYSBP > 200"
                }
              ]
            }
            """;

        var harness = new ValidationHarness(
            [formV1, formV2],
            [
                CreateLibraryPack(packPv1Id, "pack-v1", MakePack("pack-v1", "R-V1"), [pv1]),
                CreateLibraryPack(packPv2Id, "pack-v2", MakePack("pack-v2", "R-V2"), [pv2]),
                CreateLibraryPack(packSharedId, "pack-shared", MakePack("pack-shared", "R-SHARED"), []),
            ]);

        var formV1Warnings = await harness.Service.PreviewFormAsync(
            Tenant,
            formV1.CollectionInstanceId,
            new Dictionary<string, ObservationValue?>(),
            AsOf,
            ct);
        formV1Warnings.Select(w => w.RuleId).Should().BeEquivalentTo("R-V1", "R-SHARED");

        var packPv2Preview = await harness.Service.PreviewRulePackAsync(Tenant, Study, packPv2Id, AsOf, ct);
        packPv2Preview.FormsEvaluated.Should().Be(1);
        packPv2Preview.Violations.Should().ContainSingle().Which.FormId.Should().Be(formV2.CollectionInstanceId);

        var runResult = await harness.Service.ValidateStudyCollectionsAsync(
            Tenant, Study, AsOf, T0.AddHours(1), "run-pv-filter", "Test protocol version filtering", cancellationToken: ct);
        runResult.IsSuccess.Should().BeTrue();
        var recordedRun = harness.RecordedRuns.Should().ContainSingle().Subject;
        recordedRun.Detections.Select(d => (d.RuleId, d.Target.Key)).Should().BeEquivalentTo([
            ("R-V1", formV1.CollectionInstanceId.Value.ToString("D")),
            ("R-V2", formV2.CollectionInstanceId.Value.ToString("D")),
            ("R-SHARED", formV1.CollectionInstanceId.Value.ToString("D")),
            ("R-SHARED", formV2.CollectionInstanceId.Value.ToString("D")),
        ]);
    }

    [Fact(DisplayName = "ValidateStudyCollections_PaginatesPastFindingQueryMaxTake")]
    public async Task ValidateStudyCollectionsPaginatesPastFindingQueryMaxTake()
    {
        var ct = TestContext.Current.CancellationToken;
        var pv1 = ProtocolVersionId.New();
        var form = CreateForm(
            pv1,
            "cs.vital_signs",
            plannedActivityId: "act.v1",
            collectedAt: T0,
            items: [Item("item.sysbp")],
            values: [Val("item.sysbp", new QuantityValue(120m, "mm[Hg]"))]);
        var packId = LibraryItemId.New();
        const string packJson = """
            {
              "authority": "Study",
              "title": "Pagination checks",
              "rules": [
                {
                  "id": "R-PAGE",
                  "version": 1,
                  "citation": { "document": "Protocol CAD-101", "section": "6.6" },
                  "severity": "Warning",
                  "applicability": { "stage": "Collection", "dataset": "cs.vital_signs" },
                  "condition": { "op": ">", "args": [{ "var": "item.sysbp" }, 250] },
                  "explanation": "SYSBP > 250"
                }
              ]
            }
            """;

        var runId = ValidationRunId.New();
        var seededFindings = Enumerable.Range(0, 520)
            .Select(i => new ValidationFindingView(
                ValidationFindingId.New(),
                Study,
                ValidationStage.Collection,
                "pack-page",
                packId,
                $"R-{i}",
                1,
                ValidationSeverity.Warning,
                new FindingTarget(FindingTargetKind.DatasetRecord, i == 515 ? "cs.legacy_tail_dataset" : "cs.vital_signs", $"row-{i}", "item.sysbp"),
                "Prior finding",
                null,
                [],
                null,
                null,
                null,
                FindingStatus.Open,
                runId,
                runId,
                null,
                null,
                null))
            .ToList();

        var harness = new ValidationHarness(
            [form],
            [CreateLibraryPack(packId, "pack-page", packJson, [])],
            seededFindings);

        var outcome = await harness.Service.ValidateStudyCollectionsAsync(
            Tenant, Study, AsOf, T0.AddHours(1), "run-paginate", "Verify pagination past 500", cancellationToken: ct);

        outcome.IsSuccess.Should().BeTrue();
        outcome.Value!.Findings.Should().HaveCount(520);
        harness.FindingsStore.SkipsRequested.Should().Equal(0, 500, 0, 500);
        harness.RecordedRuns.Single().Datasets.Should().Contain("cs.legacy_tail_dataset");
    }

    private static CollectionItem Item(string id) => new(id, ConceptRef.New(), Required: false);

    private static CollectedValueView Val(string itemId, ObservationValue value) =>
        new(itemId, ObservationId.New(), ConceptRef.New(), value, T0, 1, "crc@site.test", T0);

    private static CollectionInstanceRecord CreateForm(
        ProtocolVersionId protocolVersionId,
        string specId,
        string? plannedActivityId,
        DateTimeOffset? collectedAt,
        CollectionItem[] items,
        CollectedValueView[] values) =>
        new(
            CollectionInstanceId.New(),
            Enrollment,
            Participant,
            Study,
            Site,
            protocolVersionId,
            specId,
            plannedActivityId,
            collectedAt ?? T0,
            "crc@site.test",
            collectedAt ?? T0,
            1 + values.Length,
            items,
            values,
            [],
            [],
            [],
            null,
            specId);

    private static LibraryItemView CreateLibraryPack(
        LibraryItemId id,
        string key,
        string json,
        IReadOnlyList<ProtocolVersionId> governingVersions)
    {
        using var doc = JsonDocument.Parse(json);
        return new LibraryItemView(
            id,
            LibraryItemKind.RulePack,
            key,
            1,
            key,
            doc.RootElement.Clone(),
            "sha256:" + new string('a', 64),
            GovernanceStatus.Approved,
            [],
            null,
            null)
        {
            GoverningProtocolVersions = governingVersions,
        };
    }

    private sealed class ValidationHarness
    {
        public ValidationHarness(
            IReadOnlyList<CollectionInstanceRecord> forms,
            IReadOnlyList<LibraryItemView> packs,
            IReadOnlyList<ValidationFindingView>? findings = null)
        {
            var registry = new StubStandardsRegistry(packs);
            var collections = new StubCollections(forms);
            FindingsStore = new PagedFindingsStore(findings ?? []);
            var sender = new RecordingCommandSender(RecordedRuns);
            Service = new CollectionValidationService(
                new RulePackLoader(registry),
                registry,
                new DotNetRuleEvaluator(),
                collections,
                FindingsStore,
                sender,
                new FakeClock(T0.AddDays(60)));
        }

        public CollectionValidationService Service { get; }

        public PagedFindingsStore FindingsStore { get; }

        public List<RecordValidationRun> RecordedRuns { get; } = [];
    }

    private sealed class RecordingCommandSender(List<RecordValidationRun> runs) : IKernelCommandSender
    {
        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            if (command is RecordValidationRun run)
            {
                runs.Add(run);
                var outcome = new ValidationRunOutcome(run.RunId, run.Detections.Count, 0, 0);
                return Task.FromResult(CommandResult<TResult>.Success((TResult)(object)outcome));
            }

            throw new NotSupportedException(command.GetType().Name);
        }
    }

    private sealed class StubCollections(IReadOnlyList<CollectionInstanceRecord> records) : ICollectionInstanceRecords
    {
        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken) =>
            Task.FromResult(records.FirstOrDefault(r => r.CollectionInstanceId == instance));

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceSummary>>([]);

        public Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([.. records.Where(r => r.EnrollmentId == enrollment)]);

        public Task<IReadOnlyList<CollectionInstanceRecord>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([.. records.Where(r => r.StudyId == study)]);
    }

    private sealed class PagedFindingsStore(IReadOnlyList<ValidationFindingView> findings) : IValidationFindings
    {
        public List<int> SkipsRequested { get; } = [];

        public Task<FindingPage> ListAsync(TenantId tenant, FindingQuery query, CancellationToken cancellationToken)
        {
            SkipsRequested.Add(query.Skip);
            var page = findings.Skip(query.Skip).Take(query.Take).ToList();
            return Task.FromResult(new FindingPage(page, findings.Count));
        }

        public Task<ValidationFindingView?> FindAsync(TenantId tenant, ValidationFindingId finding, CancellationToken cancellationToken) =>
            Task.FromResult(findings.FirstOrDefault(f => f.Id == finding));

        public Task<ValidationRunSummary?> FindRunAsync(TenantId tenant, ValidationRunId run, CancellationToken cancellationToken) =>
            Task.FromResult<ValidationRunSummary?>(null);
    }

    private sealed class StubStandardsRegistry(IReadOnlyList<LibraryItemView> packs) : IStandardsRegistry
    {
        public Task<StudyStandardsProfileView?> FindStudyStandardsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<StudyStandardsProfileView?>(new StudyStandardsProfileView(
                StudyStandardsProfileId.New(),
                study,
                1,
                [],
                [],
                [.. packs.Select(p => p.Id)]));

        public Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken) =>
            Task.FromResult(packs.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemVersionsAsync(TenantId tenant, LibraryItemKind kind, string key, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>([.. packs.Where(p => p.Kind == kind && p.Key == key)]);

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemsAsync(TenantId tenant, LibraryItemKind? kind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>(kind is null ? packs : [.. packs.Where(p => p.Kind == kind)]);

        public Task<IReadOnlyList<StandardVersionSummary>> ListStandardVersionsAsync(TenantId tenant, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StandardVersionSummary>>([]);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, string standardCode, string version, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardPackage?> GetStandardPackageAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult<StandardPackage?>(null);
    }
}
