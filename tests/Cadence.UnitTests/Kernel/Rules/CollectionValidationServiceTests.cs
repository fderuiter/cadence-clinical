using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Rules;

/// <summary>
/// Verifies <see cref="CollectionValidationService"/> cross-form sibling precedence, finding deduplication (K-FIND-1),
/// treatment-blinding masking (K-AUTH-4), and linear memory scaling per enrollment.
/// </summary>
public sealed class CollectionValidationServiceTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly StudyId Study = StudyId.New();
    private static readonly StudySiteId Site = StudySiteId.New();
    private static readonly ProtocolVersionId ProtocolVersion = ProtocolVersionId.New();
    private static readonly DateOnly AsOf = new(2026, 9, 30);
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "CollectionValidationService_PreservesCrossFormPrecedenceAcrossPreviewAndValidation")]
    [Trait("Category", "Unit")]
    public async Task CollectionValidationServicePreservesCrossFormPrecedenceAcrossPreviewAndValidation()
    {
        var enrollment = EnrollmentId.New();
        var participant = ParticipantId.New();

        // Two instances of "vitals" (older and newer), two instances of "labs" (older and newer), plus "demog".
        var vitalsOlder = Form(
            enrollment,
            participant,
            "vitals",
            BaseTime.AddMinutes(1),
            ("sbp", new QuantityValue(150m, "mm[Hg]")),
            ("shared", new TextValue("from-vitals-older")),
            ("null_target", null));
        var vitalsNewer = Form(
            enrollment,
            participant,
            "vitals",
            BaseTime.AddMinutes(5),
            ("sbp", new QuantityValue(110m, "mm[Hg]")),
            ("shared", new TextValue("from-vitals-newer")),
            ("null_target", new TextValue("non-null-on-other-vitals")));
        var labsOlder = Form(
            enrollment,
            participant,
            "labs",
            BaseTime.AddMinutes(2),
            ("potassium", new QuantityValue(4.0m, "mmol/L")),
            ("shared", new TextValue("from-labs-older")),
            ("null_target", new TextValue("from-labs-older")));
        var labsNewer = Form(
            enrollment,
            participant,
            "labs",
            BaseTime.AddMinutes(6),
            ("potassium", new QuantityValue(6.2m, "mmol/L")),
            ("shared", new TextValue("from-labs-newer")),
            ("null_target", new TextValue("from-labs-newer")));
        var demog = Form(
            enrollment,
            participant,
            "demog",
            BaseTime.AddMinutes(3),
            ("cohort", new TextValue("A")),
            ("shared", new TextValue("from-demog")));

        IReadOnlyList<CollectionInstanceRecord> forms = [vitalsOlder, labsOlder, demog, vitalsNewer, labsNewer];

        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Cross-form precedence pack",
            [
                // Fires on vitalsOlder only when vitals.sbp uses target's own 150 (not vitalsNewer's 110) AND latest labs.potassium is 6.2.
                MakeRule(
                    "CF-OWN-AND-LATEST-SIBLING",
                    "vitals",
                    And(
                        Eq(Var("vitals.sbp"), Num(150m)),
                        Eq(Var("potassium"), Num(6.2m)),
                        Eq(Var("labs.potassium"), Num(6.2m)))),
                // Must NEVER fire on vitalsOlder because target's own null_target (even though null) shadows labs.null_target!
                MakeRule(
                    "CF-NULL-TARGET-SHADOWS-SIBLING",
                    "vitals",
                    IsNotEmpty(Var("null_target"))),
                // Fires on demog when unqualified "shared" resolves to first-seen sibling spec ("vitals" -> vitalsNewer = "from-vitals-newer").
                MakeRule(
                    "CF-FIRST-SIBLING-SPEC-WINS",
                    "demog",
                    Eq(Var("shared"), Txt("from-vitals-newer"))),
            ]);

        var evaluator = new DotNetRuleEvaluator();
        var context = new RuleEvaluationContext(ValidationStage.Collection, AsOf);

        // Baseline: static BuildRuleRecord for every form in the enrollment.
        var expectedViolations = evaluator.Evaluate(
            pack,
            context,
            forms.Select(f => CollectionValidationService.BuildRuleRecord(f, forms, draftOverlay: null)));

        var packId = LibraryItemId.New();
        var registry = new StubStandardsRegistry(Study, packId, "sponsor.crossform", pack);
        var collectionStore = new StubCollectionRecords(forms);
        var findingStore = new StubValidationFindings();
        var commandSender = new CapturingCommandSender();
        var service = new CollectionValidationService(
            new RulePackLoader(registry),
            registry,
            evaluator,
            collectionStore,
            findingStore,
            commandSender);

        var previewPack = await service.PreviewRulePackAsync(
            Tenant,
            Study,
            packId,
            AsOf,
            TestContext.Current.CancellationToken);
        previewPack.Violations.Select(v => (Key: v.FormId.Value.ToString("D"), v.RuleId))
            .Should().Equal(expectedViolations.Select(v => (v.RecordKey, v.RuleId)));

        var previewOlderVitals = await service.PreviewFormAsync(
            Tenant,
            vitalsOlder,
            new Dictionary<string, ObservationValue?>(StringComparer.Ordinal),
            AsOf,
            TestContext.Current.CancellationToken);
        previewOlderVitals.Select(w => w.RuleId).Should().Equal("CF-OWN-AND-LATEST-SIBLING");

        var validationResult = await service.ValidateStudyCollectionsAsync(
            Tenant,
            Study,
            AsOf,
            BaseTime.AddHours(1),
            "idem-1",
            "Cross-form validation check",
            cancellationToken: TestContext.Current.CancellationToken);
        validationResult.IsSuccess.Should().BeTrue();
        commandSender.LastRun.Should().NotBeNull();
        commandSender.LastRun!.Detections.Select(d => (d.Target.Key, d.RuleId))
            .Should().Equal(expectedViolations.Select(v => (v.RecordKey, v.RuleId)));
    }

    [Fact(DisplayName = "CollectionValidationService_BlindedPreviewMasksBlindedSiblingAndTargetVariables")]
    [Trait("Category", "Unit")]
    public async Task CollectionValidationServiceBlindedPreviewMasksBlindedSiblingAndTargetVariables()
    {
        var enrollment = EnrollmentId.New();
        var participant = ParticipantId.New();

        var randomizationForm = Form(
            enrollment,
            participant,
            "randomization",
            BaseTime.AddMinutes(1),
            ("treatment_assignment", new TextValue("ACTIVE_DRUG")));
        var vitalsForm = Form(
            enrollment,
            participant,
            "vitals",
            BaseTime.AddMinutes(2),
            ("sbp", new QuantityValue(135m, "mm[Hg]")));

        IReadOnlyList<CollectionInstanceRecord> forms = [randomizationForm, vitalsForm];
        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Blinding check pack",
            [
                MakeRule(
                    "BLIND-CROSS-FORM",
                    "vitals",
                    And(
                        Eq(Var("sbp"), Num(135m)),
                        Eq(Var("randomization.treatment_assignment"), Txt("ACTIVE_DRUG")))),
            ]);

        var packId = LibraryItemId.New();
        var registry = new StubStandardsRegistry(Study, packId, "sponsor.blinding", pack);
        var collectionStore = new StubCollectionRecords(forms);
        var service = new CollectionValidationService(
            new RulePackLoader(registry),
            registry,
            new DotNetRuleEvaluator(),
            collectionStore,
            new StubValidationFindings());

        var blindedWarnings = await service.PreviewFormAsync(
            Tenant,
            vitalsForm,
            new Dictionary<string, ObservationValue?>(StringComparer.Ordinal),
            AsOf,
            unblinded: false,
            TestContext.Current.CancellationToken);
        blindedWarnings.Should().BeEmpty("blinded callers must not infer treatment assignment via cross-form edit checks (K-AUTH-4)");

        var unblindedWarnings = await service.PreviewFormAsync(
            Tenant,
            vitalsForm,
            new Dictionary<string, ObservationValue?>(StringComparer.Ordinal),
            AsOf,
            unblinded: true,
            TestContext.Current.CancellationToken);
        unblindedWarnings.Select(w => w.RuleId).Should().Equal("BLIND-CROSS-FORM");

        var blindedPackPreview = await service.PreviewRulePackAsync(
            Tenant,
            Study,
            packId,
            AsOf,
            Actor.Person("blinded.cra@cadence.test"),
            unblindedEnrollments: new HashSet<EnrollmentId>(),
            TestContext.Current.CancellationToken);
        blindedPackPreview.Violations.Should().BeEmpty();

        var unblindedPackPreview = await service.PreviewRulePackAsync(
            Tenant,
            Study,
            packId,
            AsOf,
            Actor.Person("unblinded.pharmacist@cadence.test"),
            unblindedEnrollments: new HashSet<EnrollmentId> { enrollment },
            TestContext.Current.CancellationToken);
        unblindedPackPreview.Violations.Select(v => v.RuleId).Should().Equal("BLIND-CROSS-FORM");
    }

    [Fact(DisplayName = "CollectionValidationService_ScalesLinearlyWithFormsPerEnrollment")]
    [Trait("Category", "Unit")]
    public async Task CollectionValidationServiceScalesLinearlyWithFormsPerEnrollment()
    {
        var pack = new RulePack(
            RuleAuthority.Sponsor,
            "Linear scaling pack",
            [
                MakeRule(
                    "CROSS-FORM-CHECK",
                    "spec_0",
                    And(
                        Eq(Var("item_0"), Num(999m)),
                        Eq(Var("spec_1.item_1"), Num(1m)))),
            ]);
        var packId = LibraryItemId.New();
        var registry = new StubStandardsRegistry(Study, packId, "sponsor.scaling", pack);
        var evaluator = new DotNetRuleEvaluator();

        // Warm up JIT and pack compilation first.
        await RunStudyValidationAllocationAsync(registry, evaluator, enrollments: 2, formsPerEnrollment: 5);

        var smallAllocated = await RunStudyValidationAllocationAsync(registry, evaluator, enrollments: 20, formsPerEnrollment: 10);
        var fourTimesFormsAllocated = await RunStudyValidationAllocationAsync(registry, evaluator, enrollments: 20, formsPerEnrollment: 40);

        // O(M) sibling indexing scales ~4x when forms per enrollment grow 4x (10 -> 40), whereas O(M^2) scaled ~16x.
        var growthRatio = (double)fourTimesFormsAllocated / smallAllocated;
        growthRatio.Should().BeLessThan(6.0, "pre-indexing sibling forms once per enrollment must scale linearly O(M) rather than quadratically O(M^2)");
    }

    private static async Task<long> RunStudyValidationAllocationAsync(
        StubStandardsRegistry registry,
        DotNetRuleEvaluator evaluator,
        int enrollments,
        int formsPerEnrollment)
    {
        var forms = new List<CollectionInstanceRecord>(enrollments * formsPerEnrollment);
        for (var e = 0; e < enrollments; e++)
        {
            var enrollment = EnrollmentId.New();
            var participant = ParticipantId.New();
            for (var f = 0; f < formsPerEnrollment; f++)
            {
                var specId = string.Create(CultureInfo.InvariantCulture, $"spec_{f}");
                forms.Add(Form(
                    enrollment,
                    participant,
                    specId,
                    BaseTime.AddMinutes(f),
                    ("item_0", new QuantityValue(120m, "1")),
                    ("item_1", new QuantityValue(1m, "1")),
                    ("item_2", new TextValue("ok")),
                    ("item_3", new BooleanValue(true))));
            }
        }

        var service = new CollectionValidationService(
            new RulePackLoader(registry),
            registry,
            evaluator,
            new StubCollectionRecords(forms),
            new StubValidationFindings(),
            new CapturingCommandSender());

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = await service.ValidateStudyCollectionsAsync(
            Tenant,
            Study,
            AsOf,
            BaseTime.AddHours(2),
            "idem-scale",
            "Scaling measurement",
            cancellationToken: TestContext.Current.CancellationToken);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        result.IsSuccess.Should().BeTrue();
        return allocated;
    }

    private static CollectionInstanceRecord Form(
        EnrollmentId enrollment,
        ParticipantId participant,
        string specId,
        DateTimeOffset openedAt,
        params (string ItemId, ObservationValue? Value)[] items)
    {
        var specItems = new List<CollectionItem>(items.Length);
        var values = new List<CollectedValueView>(items.Length);
        foreach (var (itemId, value) in items)
        {
            var concept = new ConceptRef(ConceptId.New(), 1);
            specItems.Add(new CollectionItem(itemId, concept));
            if (value is not null)
            {
                values.Add(new CollectedValueView(
                    itemId,
                    ObservationId.New(),
                    concept,
                    value,
                    openedAt,
                    Revision: 1,
                    RecordedBy: "coord@cadence.test",
                    RecordedAt: openedAt));
            }
        }

        return new CollectionInstanceRecord(
            CollectionInstanceId.New(),
            enrollment,
            participant,
            Study,
            Site,
            ProtocolVersion,
            specId,
            PlannedActivityId: null,
            CollectedAt: openedAt,
            OpenedBy: "coord@cadence.test",
            OpenedAt: openedAt,
            Version: 1,
            specItems,
            values,
            Locks: [],
            Assertions: [],
            Attestations: [],
            ContentHash: null);
    }

    private static Rule MakeRule(string id, string dataset, RuleExpression condition) =>
        new(
            id,
            Version: 1,
            RuleAuthority.Sponsor,
            new SourceCitation("Sponsor Edit Check Specification", "§1", null),
            new RuleApplicability(ValidationStage.Collection, null, null, null, dataset),
            ValidationSeverity.Warning,
            condition,
            $"Rule {id} triggered.",
            "Verify recorded values.",
            EffectivePeriod.Always);

    private static VariableExpression Var(string name) => new(name);

    private static LiteralExpression Num(decimal value) => new(RuleValue.FromNumber(value));

    private static LiteralExpression Txt(string value) => new(RuleValue.FromText(value));

    private static ComparisonExpression Eq(RuleExpression left, RuleExpression right) =>
        new(ComparisonOperator.Equal, left, right);

    private static LogicalExpression And(params RuleExpression[] items) =>
        new(LogicalOperator.And, items);

    private static FunctionExpression IsNotEmpty(RuleExpression arg) =>
        new(RuleFunction.IsNotEmpty, [arg]);

    private sealed class StubStandardsRegistry : IStandardsRegistry
    {
        private readonly StudyId _study;
        private readonly LibraryItemView _item;
        private readonly StudyStandardsProfileView _profile;

        public StubStandardsRegistry(StudyId study, LibraryItemId itemId, string key, RulePack pack)
        {
            _study = study;
            _item = new LibraryItemView(
                itemId,
                LibraryItemKind.RulePack,
                key,
                1,
                pack.Title,
                RulePackJson.ToJson(pack),
                new string('c', 64),
                GovernanceStatus.Approved,
                [],
                AttestationId.New(),
                null);
            _profile = new StudyStandardsProfileView(StudyStandardsProfileId.New(), study, 1, [], [], [itemId]);
        }

        public Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken) =>
            Task.FromResult(id == _item.Id ? _item : null);

        public Task<StudyStandardsProfileView?> FindStudyStandardsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult(study == _study ? _profile : null);

        public Task<IReadOnlyList<StandardVersionSummary>> ListStandardVersionsAsync(TenantId tenant, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StandardVersionSummary>>([]);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardVersionSummary?> FindStandardVersionAsync(TenantId tenant, string standardCode, string version, CancellationToken cancellationToken) =>
            Task.FromResult<StandardVersionSummary?>(null);

        public Task<StandardPackage?> GetStandardPackageAsync(TenantId tenant, StandardVersionId id, CancellationToken cancellationToken) =>
            Task.FromResult<StandardPackage?>(null);

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemVersionsAsync(TenantId tenant, LibraryItemKind kind, string key, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>([]);
    }

    private sealed class StubCollectionRecords(IReadOnlyList<CollectionInstanceRecord> records) : ICollectionInstanceRecords
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

    private sealed class StubValidationFindings : IValidationFindings
    {
        public Task<ValidationFindingView?> FindAsync(TenantId tenant, ValidationFindingId finding, CancellationToken cancellationToken) =>
            Task.FromResult<ValidationFindingView?>(null);

        public Task<FindingPage> ListAsync(TenantId tenant, FindingQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new FindingPage([], 0));

        public Task<ValidationRunSummary?> FindRunAsync(TenantId tenant, ValidationRunId run, CancellationToken cancellationToken) =>
            Task.FromResult<ValidationRunSummary?>(null);
    }

    private sealed class CapturingCommandSender : IKernelCommandSender
    {
        public RecordValidationRun? LastRun { get; private set; }

        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            if (command is RecordValidationRun run)
            {
                LastRun = run;
                var outcome = new ValidationRunOutcome(run.RunId, run.Detections.Count, run.Detections.Count, 0);
                return Task.FromResult(CommandResult<TResult>.Success((TResult)(object)outcome));
            }

            throw new NotSupportedException($"Unexpected command {command.GetType().Name}");
        }
    }
}
