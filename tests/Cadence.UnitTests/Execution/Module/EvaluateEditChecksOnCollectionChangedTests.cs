using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Module;

/// <summary>
/// Unit tests for <see cref="EvaluateEditChecksOnCollectionChanged"/>: handling <see cref="StudyStandardsDeclared"/>,
/// <see cref="FindingAccepted"/>, and <see cref="FindingExplained"/>, closing automated <see cref="FormQuery"/> rows
/// when rule packs are unpinned or findings are dispositioned, and paginating findings past <see cref="FindingQuery.MaxTake"/>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EvaluateEditChecksOnCollectionChangedTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly StudyId Study = StudyId.New();
    private static readonly StudySiteId Site = StudySiteId.New();
    private static readonly ParticipantId Participant = ParticipantId.New();
    private static readonly EnrollmentId Enrollment = EnrollmentId.New();
    private static readonly ProtocolVersionId ProtocolVersion = ProtocolVersionId.New();
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Handles_AcceptsCollectionStandardsAndFindingDecisionEvents_AndSkipsSelfActor")]
    public async Task HandlesAcceptsCollectionStandardsAndFindingDecisionEventsAndSkipsSelfActor()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new OutboxHarness();

        var savedEnv = MakeEnvelope(new CollectedValuesSaved(Tenant, CollectionInstanceId.New(), Enrollment, Study, ProtocolVersion, "cs.vital_signs", 1, []));
        var correctedEnv = MakeEnvelope(new ObservationCorrected(Tenant, ObservationId.New(), Enrollment, 2, 1, new QuantityValue(120m, "mm[Hg]"), "Corrected"));
        var standardsEnv = MakeEnvelope(new StudyStandardsDeclared(Tenant, Study, StudyStandardsProfileId.New(), 2, [], [], []));
        var acceptedEnv = MakeEnvelope(new FindingAccepted(Tenant, ValidationFindingId.New(), ValidationRunId.New(), AttestationId.New()));
        var explainedEnv = MakeEnvelope(new FindingExplained(Tenant, ValidationFindingId.New(), ValidationRunId.New(), AttestationId.New()));

        harness.Consumer.Handles(savedEnv).Should().BeTrue();
        harness.Consumer.Handles(correctedEnv).Should().BeTrue();
        harness.Consumer.Handles(standardsEnv).Should().BeTrue();
        harness.Consumer.Handles(acceptedEnv).Should().BeTrue();
        harness.Consumer.Handles(explainedEnv).Should().BeTrue();

        var selfDelivery = new KernelEventDelivery(
            EvaluateEditChecksOnCollectionChanged.Name,
            savedEnv with { Actor = $"svc:{EvaluateEditChecksOnCollectionChanged.Name}" });
        await harness.Consumer.ConsumeAsync(selfDelivery, ct);
        harness.SentCommands.Should().BeEmpty();
    }

    [Fact(DisplayName = "ConsumeAsync_StudyStandardsDeclared_ClosesAutoQueriesForUnpinnedRulePacks_EvenWhenZeroPacksRemain")]
    public async Task ConsumeAsyncStudyStandardsDeclaredClosesAutoQueriesForUnpinnedRulePacksEvenWhenZeroPacksRemain()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new OutboxHarness();

        var form = harness.SeedForm("cs.vital_signs", "SYSBP");
        var obsId = form.Values.Single().ObservationId;
        var packKeep = LibraryItemId.New();
        var packUnpinned = LibraryItemId.New();
        harness.Standards.RegisterPack(packKeep, "pack-keep");
        harness.Standards.RegisterPack(packUnpinned, "pack-unpinned");

        var keepFinding = harness.SeedFinding("pack-keep", packKeep, "R-KEEP", form.CollectionInstanceId, "SYSBP", FindingStatus.Open);
        var unpinnedFinding = harness.SeedFinding("pack-unpinned", packUnpinned, "R-UNPIN", form.CollectionInstanceId, "SYSBP", FindingStatus.Open);

        var keepAutoQueryId = EvaluateEditChecksOnCollectionChanged.DeriveAutoQueryId(Tenant, keepFinding.Id, 0);
        var unpinnedAutoQueryId = EvaluateEditChecksOnCollectionChanged.DeriveAutoQueryId(Tenant, unpinnedFinding.Id, 0);
        var manualQueryId = FormQueryId.New();

        await harness.SeedQueryAsync(keepAutoQueryId, form.CollectionInstanceId, obsId, FormQueryOrigin.System);
        await harness.SeedQueryAsync(unpinnedAutoQueryId, form.CollectionInstanceId, obsId, FormQueryOrigin.System);
        await harness.SeedQueryAsync(manualQueryId, form.CollectionInstanceId, obsId, FormQueryOrigin.Manual);

        // Case 1: Partial unpin (pack-keep stays pinned; pack-unpinned removed).
        harness.Standards.PinnedIds = [packKeep];
        harness.ValidationResult = CommandResult<CollectionValidationResult>.Success(
            new CollectionValidationResult(new ValidationRunOutcome(ValidationRunId.New(), 1, 0, 0), [keepFinding, unpinnedFinding]));

        var partialEvent = new StudyStandardsDeclared(Tenant, Study, StudyStandardsProfileId.New(), 2, [], [], [packKeep]);
        await harness.Consumer.ConsumeAsync(new KernelEventDelivery(EvaluateEditChecksOnCollectionChanged.Name, MakeEnvelope(partialEvent)), ct);

        harness.SentCommands.OfType<CloseFormQuery>().Select(c => c.QueryId).Should().Equal(unpinnedAutoQueryId);
        harness.SentCommands.Clear();

        // Case 2: All packs unpinned -> ValidateStudyCollectionsAsync returns (RunOutcome: null, Findings: []).
        harness.Standards.PinnedIds = [];
        harness.ValidationResult = CommandResult<CollectionValidationResult>.Success(new CollectionValidationResult(null, []));
        var zeroPacksEvent = new StudyStandardsDeclared(Tenant, Study, StudyStandardsProfileId.New(), 3, [], [], []);
        await harness.Consumer.ConsumeAsync(new KernelEventDelivery(EvaluateEditChecksOnCollectionChanged.Name, MakeEnvelope(zeroPacksEvent)), ct);

        harness.SentCommands.OfType<CloseFormQuery>().Select(c => c.QueryId)
            .Should().Contain(keepAutoQueryId)
            .And.NotContain(manualQueryId, "manual queries are never auto-closed");
    }

    [Fact(DisplayName = "ConsumeAsync_FindingAcceptedAndFindingExplained_CloseActiveAutoQueries")]
    public async Task ConsumeAsyncFindingAcceptedAndFindingExplainedCloseActiveAutoQueries()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new OutboxHarness();

        var form = harness.SeedForm("cs.vital_signs", "SYSBP");
        var obsId = form.Values.Single().ObservationId;
        var packId = LibraryItemId.New();
        var acceptedAttest = AttestationId.New();
        var explainedAttest = AttestationId.New();

        var acceptedFinding = harness.SeedFinding(
            "pack-a", packId, "R-ACCEPT", form.CollectionInstanceId, "SYSBP", FindingStatus.Accepted, acceptedAttest);
        var explainedFinding = harness.SeedFinding(
            "pack-a", packId, "R-EXPLAIN", form.CollectionInstanceId, "SYSBP", FindingStatus.Explained, explainedAttest);

        var acceptedQueryId = EvaluateEditChecksOnCollectionChanged.DeriveAutoQueryId(Tenant, acceptedFinding.Id, 0);
        var explainedQueryId = EvaluateEditChecksOnCollectionChanged.DeriveAutoQueryId(Tenant, explainedFinding.Id, 0);
        await harness.SeedQueryAsync(acceptedQueryId, form.CollectionInstanceId, obsId, FormQueryOrigin.System);
        await harness.SeedQueryAsync(explainedQueryId, form.CollectionInstanceId, obsId, FormQueryOrigin.System);

        var acceptedEnv = MakeEnvelope(new FindingAccepted(Tenant, acceptedFinding.Id, ValidationRunId.New(), acceptedAttest));
        await harness.Consumer.ConsumeAsync(new KernelEventDelivery(EvaluateEditChecksOnCollectionChanged.Name, acceptedEnv), ct);

        var explainedEnv = MakeEnvelope(new FindingExplained(Tenant, explainedFinding.Id, ValidationRunId.New(), explainedAttest));
        await harness.Consumer.ConsumeAsync(new KernelEventDelivery(EvaluateEditChecksOnCollectionChanged.Name, explainedEnv), ct);

        var closed = harness.SentCommands.OfType<CloseFormQuery>().ToList();
        closed.Select(c => c.QueryId).Should().Equal(acceptedQueryId, explainedQueryId);
        closed[0].ReasonForChange.Should().Contain("accepted");
        closed[1].ReasonForChange.Should().Contain("explained");
    }

    [Fact(DisplayName = "ConsumeAsync_PaginatesFindingsPastMaxTake_WhenReconcilingExistingFindings")]
    public async Task ConsumeAsyncPaginatesFindingsPastMaxTakeWhenReconcilingExistingFindings()
    {
        var ct = TestContext.Current.CancellationToken;
        using var harness = new OutboxHarness();

        var dummyFormId = CollectionInstanceId.New();
        var packId = LibraryItemId.New();
        harness.Standards.RegisterPack(packId, "pack-paged");
        harness.Standards.PinnedIds = [packId];

        for (var i = 0; i < 524; i++)
        {
            harness.SeedFinding("pack-paged", packId, $"R-FILL-{i}", dummyFormId, "SYSBP", FindingStatus.Resolved);
        }

        var tailForm = harness.SeedForm("cs.vital_signs", "SYSBP");
        var tailFinding = harness.SeedFinding("pack-paged", packId, "R-TAIL-524", tailForm.CollectionInstanceId, "SYSBP", FindingStatus.Open);

        harness.ValidationResult = CommandResult<CollectionValidationResult>.Failure(KernelErrors.AlreadyExists);

        var savedEnv = MakeEnvelope(new CollectedValuesSaved(
            Tenant, tailForm.CollectionInstanceId, Enrollment, Study, ProtocolVersion, "cs.vital_signs", 2, []));
        await harness.Consumer.ConsumeAsync(new KernelEventDelivery(EvaluateEditChecksOnCollectionChanged.Name, savedEnv), ct);

        harness.Findings.SkipsRequested.Should().Equal(0, 500);
        var expectedTailQueryId = EvaluateEditChecksOnCollectionChanged.DeriveAutoQueryId(Tenant, tailFinding.Id, 0);
        harness.SentCommands.OfType<RaiseFormQuery>().Should().ContainSingle()
            .Which.QueryId.Should().Be(expectedTailQueryId);
    }

    private static EventEnvelope MakeEnvelope<TEvent>(TEvent payload, string actor = "person:crc-1")
        where TEvent : notnull
    {
        var attr = typeof(TEvent).GetCustomAttribute<KernelEventAttribute>()!;
        var element = JsonSerializer.SerializeToElement(payload, WebJson);
        return new EventEnvelope(
            Guid.CreateVersion7(),
            attr.Type,
            attr.SchemaVersion,
            Tenant,
            T0,
            T0,
            actor,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            element,
            NodeRef.For(Study));
    }

    private sealed class OutboxHarness : IDisposable
    {
        private readonly string _dbName = $"exec-outbox-{Guid.NewGuid():N}";

        public OutboxHarness()
        {
            var validation = new StubValidationService(this);
            Consumer = new EvaluateEditChecksOnCollectionChanged(
                validation,
                Forms,
                new StubObservations(),
                Findings,
                new InMemoryExecutionContextFactory(_dbName),
                new RecordingSender(SentCommands),
                Standards);
        }

        public EvaluateEditChecksOnCollectionChanged Consumer { get; }

        public StubForms Forms { get; } = new();

        public PagedFindings Findings { get; } = new();

        public StubStandards Standards { get; } = new();

        public List<ICommand> SentCommands { get; } = [];

        public CommandResult<CollectionValidationResult> ValidationResult { get; set; } =
            CommandResult<CollectionValidationResult>.Success(new CollectionValidationResult(null, []));

        public CollectionInstanceRecord SeedForm(string specId, string itemId)
        {
            var obsId = ObservationId.New();
            var record = new CollectionInstanceRecord(
                CollectionInstanceId.New(),
                Enrollment,
                Participant,
                Study,
                Site,
                ProtocolVersion,
                specId,
                "act.v1",
                T0,
                "crc@site.test",
                T0,
                2,
                [new CollectionItem(itemId, ConceptRef.New(), Required: true)],
                [new CollectedValueView(itemId, obsId, ConceptRef.New(), new QuantityValue(240m, "mm[Hg]"), T0, 1, "crc@site.test", T0)],
                [],
                [],
                [],
                null,
                specId);
            Forms.Records.Add(record);
            return record;
        }

        public ValidationFindingView SeedFinding(
            string packKey,
            LibraryItemId packId,
            string ruleId,
            CollectionInstanceId formId,
            string variable,
            FindingStatus status,
            AttestationId? decisionAttestation = null)
        {
            var runId = ValidationRunId.New();
            var decision = decisionAttestation is { } attest
                ? new FindingDecision(status, "dm@sponsor.test", T0, "Dispositioned", runId, attest)
                : null;
            var view = new ValidationFindingView(
                ValidationFindingId.New(),
                Study,
                ValidationStage.Collection,
                packKey,
                packId,
                ruleId,
                1,
                ValidationSeverity.Warning,
                new FindingTarget(FindingTargetKind.DatasetRecord, "cs.vital_signs", formId.Value.ToString("D"), variable),
                $"Rule {ruleId} discrepancy",
                null,
                [],
                null,
                null,
                null,
                status,
                runId,
                runId,
                status == FindingStatus.Resolved ? runId : null,
                null,
                decision);
            Findings.Items.Add(view);
            return view;
        }

        public async Task SeedQueryAsync(
            FormQueryId queryId,
            CollectionInstanceId formId,
            ObservationId obsId,
            FormQueryOrigin origin = FormQueryOrigin.System)
        {
            await using var db = new InMemoryExecutionContextFactory(_dbName).Create(Tenant);
            using var writeWindow = db.PermitCommandWrites();
            var (query, _) = FormQuery.Raise(
                queryId,
                Tenant,
                formId,
                Enrollment,
                "SYSBP",
                obsId,
                1,
                "Discrepancy",
                AssertionId.New(),
                "svc:execution-edit-checks",
                T0,
                "Seeded query",
                origin);
            db.FormQueries.Add(query);
            await db.SaveChangesAsync();
        }

        public void Dispose()
        {
            using var db = new InMemoryExecutionContextFactory(_dbName).Create(Tenant);
            db.Database.EnsureDeleted();
        }
    }

    private sealed class InMemoryExecutionContextFactory(string dbName) : IModuleTenantContextFactory<ExecutionModuleDbContext>
    {
        public ExecutionModuleDbContext Create(TenantId tenant)
        {
            var options = new DbContextOptionsBuilder<ExecutionModuleDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            return new ExecutionModuleDbContext(options);
        }
    }

    private sealed class StubValidationService(OutboxHarness harness) : ICollectionValidationService
    {
        public Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
            TenantId tenant, CollectionInstanceId formId, IReadOnlyDictionary<string, ObservationValue?> draftValues, DateOnly asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollectionPreviewWarning>>([]);

        public Task<CollectionRulePackPreviewResult> PreviewRulePackAsync(
            TenantId tenant, StudyId studyId, LibraryItemId rulePackId, DateOnly asOf, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CollectionRulePackPreviewResult(0, 0, []));

        public Task<CommandResult<CollectionValidationResult>> ValidateStudyCollectionsAsync(
            TenantId tenant, StudyId studyId, DateOnly asOf, DateTimeOffset evaluatedAt, string idempotencyKey, string reasonForChange, CommandMetadata? metadata = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(harness.ValidationResult);
    }

    private sealed class RecordingSender(List<ICommand> commands) : IKernelCommandSender
    {
        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            commands.Add(command);
            object value = command switch
            {
                RaiseFormQuery raise => FormQueryState.Of(
                    FormQuery.Raise(raise.QueryId, raise.TenantId, raise.FormId, raise.EnrollmentId, "SYSBP", raise.ObservationId, raise.Revision, raise.Text, AssertionId.New(), "svc", T0).Query,
                    1),
                CloseFormQuery close => FormQueryState.Of(
                    FormQuery.Raise(close.QueryId, close.TenantId, CollectionInstanceId.New(), close.EnrollmentId, "SYSBP", ObservationId.New(), 1, "Closed", AssertionId.New(), "svc", T0).Query,
                    2),
                RecordUnaskedCollectionItem unasked => new CollectedValueState(
                    unasked.ItemId,
                    unasked.ObservationId ?? ObservationId.New(),
                    new MissingValue(NullFlavor.NotAsked),
                    1,
                    true),
                _ => throw new NotSupportedException(command.GetType().Name),
            };
            return Task.FromResult(CommandResult<TResult>.Success((TResult)value));
        }
    }

    private sealed class StubForms : ICollectionInstanceRecords
    {
        public List<CollectionInstanceRecord> Records { get; } = [];

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken) =>
            Task.FromResult(Records.FirstOrDefault(r => r.CollectionInstanceId == instance));

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceSummary>>([]);
    }

    private sealed class StubObservations : IObservationRecords
    {
        public Task<ObservationRecord?> FindAsync(TenantId tenant, ObservationId observation, CancellationToken cancellationToken) =>
            Task.FromResult<ObservationRecord?>(null);
    }

    private sealed class PagedFindings : IValidationFindings
    {
        public List<ValidationFindingView> Items { get; } = [];

        public List<int> SkipsRequested { get; } = [];

        public Task<FindingPage> ListAsync(TenantId tenant, FindingQuery query, CancellationToken cancellationToken)
        {
            SkipsRequested.Add(query.Skip);
            var page = Items.Skip(query.Skip).Take(query.Take).ToList();
            return Task.FromResult(new FindingPage(page, Items.Count));
        }

        public Task<ValidationFindingView?> FindAsync(TenantId tenant, ValidationFindingId finding, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(f => f.Id == finding));

        public Task<ValidationRunSummary?> FindRunAsync(TenantId tenant, ValidationRunId run, CancellationToken cancellationToken) =>
            Task.FromResult<ValidationRunSummary?>(null);
    }

    private sealed class StubStandards : IStandardsRegistry
    {
        private readonly Dictionary<LibraryItemId, LibraryItemView> _items = [];

        public IReadOnlyList<LibraryItemId> PinnedIds { get; set; } = [];

        public void RegisterPack(LibraryItemId id, string key)
        {
            using var doc = JsonDocument.Parse("""{"rules":[]}""");
            _items[id] = new LibraryItemView(id, LibraryItemKind.RulePack, key, 1, key, doc.RootElement.Clone(), "sha256:" + new string('b', 64), GovernanceStatus.Approved, [], null, null);
        }

        public Task<StudyStandardsProfileView?> FindStudyStandardsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<StudyStandardsProfileView?>(new StudyStandardsProfileView(StudyStandardsProfileId.New(), study, 1, [], [], PinnedIds));

        public Task<LibraryItemView?> FindLibraryItemAsync(TenantId tenant, LibraryItemId id, CancellationToken cancellationToken) =>
            Task.FromResult(_items.GetValueOrDefault(id));

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemVersionsAsync(TenantId tenant, LibraryItemKind kind, string key, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>([.. _items.Values.Where(i => i.Kind == kind && i.Key == key)]);

        public Task<IReadOnlyList<LibraryItemView>> ListLibraryItemsAsync(TenantId tenant, LibraryItemKind? kind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LibraryItemView>>(kind is null ? [.. _items.Values] : [.. _items.Values.Where(i => i.Kind == kind)]);

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
