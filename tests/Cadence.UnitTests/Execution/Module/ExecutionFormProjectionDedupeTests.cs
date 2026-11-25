using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Module;

/// <summary>
/// Regression tests verifying narrow projections on <see cref="ICollectionInstanceRecords"/>, summary query assertion IDs,
/// bitemporal query pre-filtering/paging before target hydration, kernel blinding set reuse, and deduplicated consumer reads.
/// </summary>
public sealed class ExecutionFormProjectionDedupeTests
{
    private static readonly DateTimeOffset At = new(2026, 4, 10, 9, 0, 0, TimeSpan.Zero); // deid-ignore: fixed test instant

    [Fact(DisplayName = "ICollectionInstanceRecords_DefaultProjections_DelegateAndMaskTitleAndSignedValues")]
    public async Task DefaultProjectionsDelegateAndMaskTitleAndSignedValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var obsId = ObservationId.New();
        var item = new CollectionItem("sbp", ConceptRef.New(), Required: true);
        var record = new CollectionInstanceRecord(
            CollectionInstanceId.New(),
            EnrollmentId.New(),
            ParticipantId.New(),
            StudyId.New(),
            StudySiteId.New(),
            ProtocolVersionId.New(),
            "cs.vitals",
            "act.week12",
            At,
            "crc@example.test",
            At,
            2,
            [item],
            [new CollectedValueView("sbp", obsId, item.Concept, new QuantityValue(120m, "mm[Hg]"), At, 2, "crc@example.test", At)],
            [new LockView(AssertionId.New(), LockKind.Frozen, NodeRef.For(StudyId.New()), AttestationId.New(), At, "dm", "Freeze")],
            [],
            [new FormAttestationView(
                AttestationId.New(),
                AttestationMeaning.Approved,
                "pi@example.test",
                At,
                "hash-1",
                "Signed",
                false,
                [new SignedValueView("sbp", obsId, 1, new TextValue("Treatment Arm A"))])],
            "hash-2",
            "Randomization Arm Assignment");

        // MaskBlinded inspects CollectionTitle and Attestations[].SignedValues even when BlindedItemIds is null and current Values are numeric.
        var masked = record.MaskBlinded();
        masked.Values[0].Value.Should().Be(new MissingValue(NullFlavor.Masked));
        masked.Attestations[0].SignedValues![0].Value.Should().Be(new MissingValue(NullFlavor.Masked));
        masked.BlindedItemIds.Should().Contain("sbp");

        ICollectionInstanceRecords port = new MinimalCollectionInstanceRecords([record]);
        var header = await port.FindHeaderAsync(tenant, record.CollectionInstanceId, ct);
        header.Should().NotBeNull();
        header!.CollectionSpecId.Should().Be("cs.vitals");

        var headers = await port.FindHeadersAsync(tenant, [record.CollectionInstanceId], ct);
        headers.Should().ContainKey(record.CollectionInstanceId);

        var blindedTargets = await port.FindQueryTargetsAsync(
            tenant,
            [record.CollectionInstanceId],
            new HashSet<EnrollmentId>(),
            ct);
        blindedTargets[record.CollectionInstanceId].IsQueryBlinded("sbp", obsId).Should().BeTrue();
        blindedTargets[record.CollectionInstanceId].ObservationRevisions.Should().BeEmpty();

        var scopeState = await port.FindScopeStateAsync(tenant, record.CollectionInstanceId, ct);
        scopeState.Should().NotBeNull();
        scopeState!.Locks.Should().ContainSingle(l => l.Kind == LockKind.Frozen);
        scopeState.Values.Should().ContainSingle(v => v.ObservationId == obsId && v.Revision == 2);

        var ruleInputs = await port.ListRuleInputsForEnrollmentAsync(tenant, record.EnrollmentId, ct);
        ruleInputs.Should().ContainSingle(r => r.CollectionInstanceId == record.CollectionInstanceId && r.Values.Count == 1);
    }

    [Fact(DisplayName = "FormEndpoints_ListForEnrollment_MergesSummaryAssertionIdsWithoutListRecordsForEnrollmentAsync")]
    public async Task ListForEnrollmentMergesSummaryAssertionIdsWithoutListRecordsForEnrollmentAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var enrollment = EnrollmentId.New();
        var formId = CollectionInstanceId.New();
        var execOpenAssertion = AssertionId.New();
        var execAnsweredAssertion = AssertionId.New();
        var standaloneKernelOpenAssertion = AssertionId.New();

        var summary = new CollectionInstanceSummary(
            formId,
            enrollment,
            ParticipantId.New(),
            StudyId.New(),
            StudySiteId.New(),
            ProtocolVersionId.New(),
            "cs.vitals",
            "act.week12",
            At,
            "crc@example.test",
            At,
            3,
            Complete: false,
            ItemCount: 2,
            RecordedCount: 2,
            CollectionTitle: "Vital signs",
            OpenQueryCount: 2,
            AnsweredQueryCount: 1,
            ActiveQueryAssertionIds: new HashSet<AssertionId> { execOpenAssertion, execAnsweredAssertion, standaloneKernelOpenAssertion },
            AnsweredQueryAssertionIds: new HashSet<AssertionId> { execAnsweredAssertion });

        await using var sqlite = await SqliteExecutionContextFactory.CreateAsync(ct);
        await using (var db = sqlite.Create(tenant))
        {
            var (execOpen, _) = FormQuery.Raise(
                FormQueryId.New(), tenant, formId, enrollment, "sbp", ObservationId.New(), 1,
                "Check SBP", execOpenAssertion, "cra", At, "SDV");
            var (execAnswered, _) = FormQuery.Raise(
                FormQueryId.New(), tenant, formId, enrollment, "dbp", ObservationId.New(), 1,
                "Check DBP", execAnsweredAssertion, "cra", At, "SDV");
            execAnswered.Answer("Confirmed", AssertionId.New(), "crc", At.AddMinutes(5), "Verified");
            db.FormQueries.AddRange(execOpen, execAnswered);
            using (db.PermitCommandWrites())
            {
                await db.SaveChangesAsync(ct);
            }
        }

        var records = new CountingCollectionInstanceRecords(summaries: [summary], throwOnListRecords: true);
        var http = await ContextWithTenantAsync(tenant);

        var result = await FormEndpoints.ListForEnrollmentAsync(
            enrollment.Value,
            http,
            records,
            new StubReadAccess(canSee: true),
            ct,
            sqlite);

        var ok = result.Result.Should().BeOfType<Ok<IReadOnlyList<FormSummaryResponse>>>().Subject;
        var item = ok.Value!.Should().ContainSingle().Subject;
        item.OpenQueryCount.Should().Be(2, "1 execution open query + 1 un-owned standalone kernel open query");
        item.AnsweredQueryCount.Should().Be(1, "1 execution answered query deduplicated against ActiveQueryAssertionIds");
        records.ListSummariesCalls.Should().Be(1);
        records.ListRecordsCalls.Should().Be(0);
    }

    [Fact(DisplayName = "FormEndpoints_GetAndSave_UseFindHeaderBeforeCommandAndReuseKernelBlindingSetsWithoutExtraDbReads")]
    public async Task GetAndSaveUseFindHeaderBeforeCommandAndReuseKernelBlindingSetsWithoutExtraDbReads()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var obsId = ObservationId.New();
        var item = new CollectionItem("arm", ConceptRef.New(), ValueKind: ObservationValueKind.Text, Required: true);
        var header = new CollectionInstanceHeader(
            CollectionInstanceId.New(),
            EnrollmentId.New(),
            ParticipantId.New(),
            StudyId.New(),
            StudySiteId.New(),
            ProtocolVersionId.New(),
            "cs.dispense",
            "act.day1",
            At,
            At,
            1);
        var postSaveRecord = new CollectionInstanceRecord(
            header.CollectionInstanceId,
            header.EnrollmentId,
            header.ParticipantId,
            header.StudyId,
            header.StudySiteId,
            header.ProtocolVersionId,
            header.CollectionSpecId,
            header.PlannedActivityId,
            At,
            "crc@example.test",
            At,
            2,
            [item],
            [new CollectedValueView("arm", obsId, item.Concept, new TextValue("Recorded"), At, 1, "crc@example.test", At)],
            [],
            [],
            [],
            "hash-v2",
            "Vital signs",
            BlindedItemIds: new HashSet<string>(StringComparer.Ordinal) { "arm" },
            BlindedObservationIds: new HashSet<ObservationId> { obsId });

        // Throwing IProtocolVersionReader and IConceptRegistry prove ResolveBlindedFormTargetsAsync makes 0 DB reads
        // when kernel BlindedItemIds is already populated!
        var throwingProtocols = Substitute.For<IProtocolVersionReader>();
        throwingProtocols.GetDesignAsync(Arg.Any<TenantId>(), Arg.Any<ProtocolVersionId>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProtocolDesign?>>(_ => throw new InvalidOperationException("Protocol reader must not be queried when BlindedItemIds is populated."));
        var throwingConcepts = Substitute.For<IConceptRegistry>();
        throwingConcepts.ResolveAsync(Arg.Any<TenantId>(), Arg.Any<ConceptRef>(), Arg.Any<CancellationToken>())
            .Returns<Task<ConceptVersionView?>>(_ => throw new InvalidOperationException("Concept registry must not be queried when BlindedItemIds is populated."));

        var services = new ServiceCollection()
            .AddSingleton(throwingProtocols)
            .AddSingleton(throwingConcepts)
            .BuildServiceProvider();
        var http = await ContextWithTenantAsync(tenant, services);
        http.Request.Headers[FormEndpoints.IdempotencyKeyHeader] = "idem-save-blinded";

        await using var sqlite = await SqliteExecutionContextFactory.CreateAsync(ct);
        var records = new CountingCollectionInstanceRecords(
            headers: new Dictionary<CollectionInstanceId, CollectionInstanceHeader> { [header.CollectionInstanceId] = header },
            findQueue: [postSaveRecord]);
        var sender = new RecordingCommandSender(
            new CollectedValuesState(
                header.CollectionInstanceId,
                2,
                [new CollectedValueState("arm", obsId, new TextValue("Treatment Arm A"), 1, true)]));

        var saveResult = await FormEndpoints.SaveAsync(
            header.CollectionInstanceId.Value,
            new SaveFormValuesRequest([new FormValueRequest("arm", new TextValue("Treatment Arm A"), 0, null, null)]),
            http,
            records,
            sender,
            new FixedChangeReason("Dispense entry"),
            ct,
            contexts: sqlite,
            access: new StubReadAccess(canSee: true, canUnblind: false));

        var saved = saveResult.Result.Should().BeOfType<Ok<FormSavedResponse>>().Subject.Value!;
        records.FindHeaderCalls.Should().Be(1, "SaveAsync checks form existence/enrollment via FindHeaderAsync before sending command");
        records.FindCalls.Should().Be(1, "SaveAsync hydrates full CollectionInstanceRecord only once after saving");
        saved.Values.Single().Value.Should().Be(new MissingValue(NullFlavor.Masked));
        saved.Form!.Items.Single().Value.Should().Be(new MissingValue(NullFlavor.Masked));
    }

    [Fact(DisplayName = "FormQueryHandlersAndEndpoints_UseHeaderAndScopeState_ReuseScopeContext_AndRefreshOnReplay")]
    public async Task FormQueryHandlersAndEndpointsUseHeaderAndScopeStateReuseScopeContextAndRefreshOnReplay()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var formId = CollectionInstanceId.New();
        var enrollmentId = EnrollmentId.New();
        var studyId = StudyId.New();
        var siteId = StudySiteId.New();
        var obsId = ObservationId.New();
        var header = new CollectionInstanceHeader(
            formId, enrollmentId, ParticipantId.New(), studyId, siteId, ProtocolVersionId.New(), "cs.vitals", "act.week12", At, At, 2);
        var scopeState = new CollectionInstanceScopeState(
            formId,
            enrollmentId,
            studyId,
            siteId,
            header.ProtocolVersionId,
            "cs.vitals",
            "act.week12",
            2,
            1,
            [new CollectionScopeValue("sbp", obsId, new QuantityValue(132m, "mm[Hg]"), 1)],
            []);
        var target = new CollectionInstanceQueryTarget(
            formId,
            enrollmentId,
            studyId,
            siteId,
            header.ProtocolVersionId,
            "cs.vitals",
            "act.week12",
            new Dictionary<ObservationId, int> { [obsId] = 3 },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["sbp"] = 3 });

        var records = new CountingCollectionInstanceRecords(
            headers: new Dictionary<CollectionInstanceId, CollectionInstanceHeader> { [formId] = header },
            scopeStates: new Dictionary<CollectionInstanceId, CollectionInstanceScopeState> { [formId] = scopeState },
            queryTargets: new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget> { [formId] = target },
            throwOnFind: true);

        var navigator = new CountingNavigator(
            studyId,
            siteId,
            enrollmentId,
            siteCode: "017",
            subjectCode: "017-0001");

        // 1. Live RaiseAsync resolves FindHeaderAsync once, RaiseFormQueryHandler reuses ResolvedHeader + calls FindScopeStateAsync once,
        // and RespondAsync reuses state.Scope with 0 FindAsync and 0 FindQueryTargetsAsync calls!
        var work = Substitute.For<IKernelUnitOfWork>();
        work.ExistsAsync<FormQuery>(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(false);
        FormQuery? capturedQuery = null;
        work.When(w => w.Add(Arg.Any<FormQuery>())).Do(call => capturedQuery = call.Arg<FormQuery>());
        var cmdContext = Substitute.For<ICommandContext>();
        cmdContext.TenantId.Returns(tenant);
        cmdContext.Actor.Returns(Actor.Person("cra@example.test"));
        cmdContext.RecordedAt.Returns(At);
        cmdContext.Work.Returns(work);
        var raiseHandler = new RaiseFormQueryHandler(records);

        var sender = new DelegatingQuerySender(async (cmd, token) =>
            await raiseHandler.HandleAsync((RaiseFormQuery)cmd, cmdContext, token));

        var http = await ContextWithTenantAsync(tenant);
        http.Request.Headers[FormEndpoints.IdempotencyKeyHeader] = "idem-raise-1";
        var raiseResponse = await FormQueryEndpoints.RaiseAsync(
            formId.Value,
            new RaiseFormQueryRequest(obsId.Value, 1, "Confirm SBP", null, null, null, null, null),
            http,
            records,
            sender,
            new FixedChangeReason("SDV"),
            new StubReadAccess(canSee: true, canUnblind: true),
            navigator,
            ct);

        var created = raiseResponse.Result.Should().BeOfType<Created<FormQueryResponse>>().Subject.Value!;
        created.CurrentRevision.Should().Be(1);
        created.SiteCode.Should().Be("017");
        created.SubjectCode.Should().Be("017-0001");
        records.FindHeaderCalls.Should().Be(1);
        records.FindScopeStateCalls.Should().Be(1);
        records.FindQueryTargetsCalls.Should().Be(0, "live unblinded response reuses state.Scope");
        records.FindCalls.Should().Be(0);
        navigator.ResolveManyCalls.Should().Be(1);
        navigator.ResolveCalls.Should().Be(0);

        // 2. Idempotent replay (Replayed = true, deserialized Scope = null) refreshes CurrentRevision via FindQueryTargetsAsync!
        var replayedState = JsonSerializer.Deserialize<FormQueryState>(
            JsonSerializer.Serialize(
                FormQueryState.Of(capturedQuery!, 1)));
        var replaySender = new DelegatingQuerySender((_, _) =>
            Task.FromResult(CommandResult<FormQueryState>.Replay(replayedState!)));

        var replayResponse = await FormQueryEndpoints.RaiseAsync(
            formId.Value,
            new RaiseFormQueryRequest(obsId.Value, 1, "Confirm SBP", null, null, null, null, null),
            http,
            records,
            replaySender,
            new FixedChangeReason("SDV"),
            new StubReadAccess(canSee: true, canUnblind: true),
            navigator,
            ct);
        var replayBody = replayResponse.Result.Should().BeOfType<Created<FormQueryResponse>>().Subject.Value!;
        replayBody.CurrentRevision.Should().Be(3, "replayed commands query FindQueryTargetsAsync for fresh current_revision");
        replayBody.ValueChanged.Should().BeTrue();
        records.FindQueryTargetsCalls.Should().Be(1);
        records.FindCalls.Should().Be(0);
    }

    [Fact(DisplayName = "FormQueryEndpoints_ListQueries_PreFiltersAndPagesBitemporalSnapshotsBeforeFindQueryTargetsAsync")]
    public async Task ListQueriesPreFiltersAndPagesBitemporalSnapshotsBeforeFindQueryTargetsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var studyId = StudyId.New();
        var siteA = StudySiteId.New();
        var siteB = StudySiteId.New();
        var enrollmentA = EnrollmentId.New();
        var enrollmentB = EnrollmentId.New();

        await using var sqlite = await SqliteExecutionContextFactory.CreateAsync(ct);
        var headers = new Dictionary<CollectionInstanceId, CollectionInstanceHeader>();
        var targets = new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>();
        var revisionsAsOf = new Dictionary<ObservationId, int>();

        // Seed 6 forms across enrollmentA (site 017) and enrollmentB (site 022); only 3 match status=open & value_changed=true at AsOf,
        // and pagination (Offset=1, Limit=1) should hydrate ONLY the 1 form on that page via FindQueryTargetsAsync!
        var matchingFormIds = new List<CollectionInstanceId>();
        await using (var db = sqlite.Create(tenant))
        {
            for (var i = 0; i < 6; i++)
            {
                var isSiteA = i < 4;
                var enr = isSiteA ? enrollmentA : enrollmentB;
                var site = isSiteA ? siteA : siteB;
                var formId = CollectionInstanceId.New();
                var obsId = ObservationId.New();
                var openedAt = At.AddMinutes(i);
                var (q, _) = FormQuery.Raise(
                    FormQueryId.New(), tenant, formId, enr, "sbp", obsId, 1,
                    $"Query {i}", AssertionId.New(), "cra", openedAt, "SDV");
                q.CreatedAt = openedAt.UtcDateTime;

                // Make i == 0 answered before AsOf so it is filtered out by status=open at AsOf.
                if (i == 0)
                {
                    var ansAt = At.AddMinutes(10);
                    q.Answer("Answered early", AssertionId.New(), "crc", ansAt, "Reply");
                }

                // Make i == 1, 2, 3 have revision 2 (value_changed = true) at AsOf; i == 0, 4, 5 stay at revision 1.
                var revAtAsOf = i is 1 or 2 or 3 ? 2 : 1;
                revisionsAsOf[obsId] = revAtAsOf;
                if (isSiteA && i is 1 or 2 or 3)
                {
                    matchingFormIds.Add(formId);
                }

                var pv = ProtocolVersionId.New();
                headers[formId] = new CollectionInstanceHeader(
                    formId, enr, ParticipantId.New(), studyId, site, pv, "cs.vitals", "act.week12", openedAt, openedAt, 1);
                targets[formId] = new CollectionInstanceQueryTarget(
                    formId,
                    enr,
                    studyId,
                    site,
                    pv,
                    "cs.vitals",
                    "act.week12",
                    new Dictionary<ObservationId, int> { [obsId] = revAtAsOf },
                    new Dictionary<string, int>(StringComparer.Ordinal) { ["sbp"] = revAtAsOf });
                db.FormQueries.Add(q);
            }

            using (db.PermitCommandWrites())
            {
                await db.SaveChangesAsync(ct);
            }
        }

        var http = await ContextWithTenantAsync(tenant);
        var records = new CountingCollectionInstanceRecords(
            headers: headers,
            queryTargets: targets,
            observationRevisions: revisionsAsOf,
            throwOnFind: true);
        var navigator = new CountingNavigator(
            studyId,
            siteA,
            enrollmentA,
            siteCode: "017",
            subjectCode: "017-0001",
            extraEnrollments: [new StudyEnrollmentNode(enrollmentB, "022-0001", siteB, "022")]);
        var access = new StubReadAccess(canSee: true, canUnblind: true);
        var clock = new FakeClock(At.AddHours(2));

        // Matches ordered by OpenedAt descending: i=3, i=2, i=1. Offset=1, Limit=1 selects ONLY i=2!
        var filter = new FormQueryListFilter(
            StudyId: studyId.Value,
            Status: "open",
            Site: "017",
            ValueChanged: true,
            AsOf: At.AddMinutes(30),
            Limit: 1,
            Offset: 1);

        var response = await FormQueryEndpoints.ListQueriesAsync(
            filter,
            http,
            records,
            sqlite,
            access,
            navigator,
            clock,
            ct);

        var page = response.Result.Should().BeOfType<Ok<IReadOnlyList<FormQueryResponse>>>().Subject.Value!;
        page.Should().ContainSingle();
        page[0].FormId.Should().Be(matchingFormIds[1].Value, "ordered matches [1, 2, 3] at offset 1 returns index 1 (form 2)");
        page[0].ValueChanged.Should().BeTrue();
        records.FindCalls.Should().Be(0);
        records.FindQueryTargetsCalls.Should().Be(1);
        records.LastQueriedTargetIds.Should().ContainSingle()
            .Which.Should().Be(matchingFormIds[1], "FindQueryTargetsAsync must only hydrate the 1 paged form, not all 6 forms");
    }

    [Fact(DisplayName = "ConsumersAndValidation_UseStudyIdRuleInputsAndBatchedHeadersWithoutFullFindAsync")]
    public async Task ConsumersAndValidationUseStudyIdRuleInputsAndBatchedHeadersWithoutFullFindAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var studyId = StudyId.New();
        var siteId = StudySiteId.New();
        var enrollmentId = EnrollmentId.New();
        var formId = CollectionInstanceId.New();
        var obsId = ObservationId.New();
        var item = new CollectionItem("sbp", ConceptRef.New(), "mm[Hg]", Required: true);

        // 1. CollectionValidationService.PreviewFormAsync calls ListRuleInputsForEnrollmentAsync (0 ListRecordsForEnrollmentAsync calls).
        var packId = LibraryItemId.New();
        var rulePack = new RulePack(
            RuleAuthority.Sponsor,
            "Vitals Rules",
            [
                new Rule(
                    "R_SBP_HIGH",
                    1,
                    RuleAuthority.Sponsor,
                    new SourceCitation("Sponsor Edit Checks", "1.1", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, "cs.vitals", "sbp"),
                    ValidationSeverity.Warning,
                    new ComparisonExpression(
                        ComparisonOperator.Greater,
                        new VariableExpression("sbp"),
                        new LiteralExpression(RuleValue.FromNumber(180m))),
                    "SBP exceeds 180",
                    null,
                    EffectivePeriod.Always),
            ]);
        var packView = new LibraryItemView(
            packId,
            LibraryItemKind.RulePack,
            "vitals.pack",
            1,
            "Vitals Rules",
            RulePackJson.ToJson(rulePack),
            "hash-1",
            GovernanceStatus.Approved,
            [],
            AttestationId.New(),
            null);
        var standards = Substitute.For<IStandardsRegistry>();
        standards.FindStudyStandardsAsync(tenant, studyId, Arg.Any<CancellationToken>())
            .Returns(new StudyStandardsProfileView(StudyStandardsProfileId.New(), studyId, 1, [], [], [packId]));
        standards.FindLibraryItemAsync(tenant, packId, Arg.Any<CancellationToken>())
            .Returns(packView);
        standards.FindLibraryItemsAsync(tenant, Arg.Any<IReadOnlyCollection<LibraryItemId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<LibraryItemId, LibraryItemView> { [packId] = packView });

        var ruleInput = new CollectionRuleInput(
            formId,
            enrollmentId,
            studyId,
            ProtocolVersionId.New(),
            "cs.vitals",
            At,
            [item],
            [new CollectionRuleValue("sbp", obsId, new QuantityValue(120m, "mm[Hg]"))],
            "Vital signs");
        var targetRecord = new CollectionInstanceRecord(
            formId, enrollmentId, ParticipantId.New(), studyId, siteId, ruleInput.ProtocolVersionId,
            "cs.vitals", "act.week12", At, "crc", At, 1, [item], [], [], [], [], null, "Vital signs");

        var ruleForms = new CountingCollectionInstanceRecords(
            ruleInputs: [ruleInput],
            throwOnListRecords: true,
            throwOnFind: true);
        var validationService = new CollectionValidationService(
            new RulePackLoader(standards),
            standards,
            new DotNetRuleEvaluator(),
            ruleForms,
            Substitute.For<IValidationFindings>());

        var draft = new Dictionary<string, ObservationValue?>(StringComparer.Ordinal)
        {
            ["sbp"] = new QuantityValue(195m, "mm[Hg]"),
        };
        var unblindedWarnings = await validationService.PreviewFormAsync(
            tenant, targetRecord, draft, new DateOnly(2026, 4, 10), unblinded: true, ct);
        var blindedWarnings = await validationService.PreviewFormAsync(
            tenant, targetRecord, draft, new DateOnly(2026, 4, 10), unblinded: false, ct);
        unblindedWarnings.Should().ContainSingle(w => w.RuleId == "R_SBP_HIGH" && w.ItemId == "sbp");
        blindedWarnings.Should().BeEquivalentTo(unblindedWarnings);
        ruleForms.ListRuleInputsCalls.Should().Be(2);
        ruleForms.ListRecordsCalls.Should().Be(0);

        // 2. EvaluateEditChecksOnCollectionChanged uses ObservationRecord.StudyId directly on ObservationCorrected (0 FindAsync calls).
        await using var sqlite = await SqliteExecutionContextFactory.CreateAsync(ct);
        var obsReader = Substitute.For<IObservationRecords>();
        obsReader.FindAsync(tenant, obsId, Arg.Any<CancellationToken>())
            .Returns(new ObservationRecord(
                obsId,
                enrollmentId,
                ParticipantId.New(),
                studyId,
                item.Concept,
                new QuantityValue(195m, "mm[Hg]"),
                At,
                2,
                [],
                [],
                [],
                [],
                CollectionInstanceId: formId,
                ItemId: "sbp"));
        var stubValidation = Substitute.For<ICollectionValidationService>();
        StudyId? validatedStudy = null;
        stubValidation.ValidateStudyCollectionsAsync(
                tenant,
                Arg.Do<StudyId>(s => validatedStudy = s),
                Arg.Any<DateOnly>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CommandMetadata?>(),
                Arg.Any<CancellationToken>())
            .Returns(CommandResult<CollectionValidationResult>.Success(
                new CollectionValidationResult(null, [])));

        var consumer = new EvaluateEditChecksOnCollectionChanged(
            stubValidation,
            ruleForms,
            obsReader,
            Substitute.For<IValidationFindings>(),
            sqlite,
            Substitute.For<IKernelCommandSender>());

        var correctedAttr = typeof(ObservationCorrected).GetCustomAttribute<KernelEventAttribute>()!;
        var correctedPayload = JsonSerializer.SerializeToElement(
            new ObservationCorrected(
                tenant, obsId, enrollmentId, 2, 1, new QuantityValue(195m, "mm[Hg]"), "Corrected"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var envelope = new EventEnvelope(
            Guid.CreateVersion7(),
            correctedAttr.Type,
            correctedAttr.SchemaVersion,
            tenant,
            null,
            At,
            "crc@example.test",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            correctedPayload);
        await consumer.ConsumeAsync(new KernelEventDelivery("edit-check-probe", envelope), ct);
        validatedStudy.Should().Be(studyId, "consumer reads StudyId directly from ObservationRecord without calling forms.FindAsync");
        ruleForms.FindCalls.Should().Be(0);

        // 3. FormQueryEscalationService batches FindHeadersAsync + ResolveManyAsync when an enrollment lacks site metadata.
        var overdueOpenAt = At.AddDays(-15);
        await using (var db = sqlite.Create(tenant))
        {
            var (overdue, _) = FormQuery.Raise(
                FormQueryId.New(), tenant, formId, enrollmentId, "sbp", obsId, 1,
                "Overdue query", AssertionId.New(), "cra", overdueOpenAt, "SDV");
            overdue.Escalate("cra", overdueOpenAt.AddDays(14), "14-day SLA");
            db.FormQueries.Add(overdue);
            using (db.PermitCommandWrites())
            {
                await db.SaveChangesAsync(ct);
            }
        }

        var escalationForms = new CountingCollectionInstanceRecords(
            headers: new Dictionary<CollectionInstanceId, CollectionInstanceHeader>
            {
                [formId] = new CollectionInstanceHeader(
                    formId, enrollmentId, ParticipantId.New(), studyId, siteId, ProtocolVersionId.New(), "cs.vitals", "act.week12", At, At, 1),
            },
            throwOnFind: true);
        var unassignedNavigator = new CountingNavigator(
            studyId,
            siteId,
            enrollmentId,
            siteCode: "017",
            subjectCode: "017-0001",
            directEnrollmentUnderStudy: true);
        var digestSender = new DigestCommandSender();
        var escalation = new FormQueryEscalationService(
            new FakeClock(At),
            escalationForms,
            unassignedNavigator,
            sqlite,
            digestSender);

        var pass = await escalation.RunStudyPassAsync(tenant, studyId, "SLA pass", ct);
        pass.IsSuccess.Should().BeTrue();
        pass.Value!.Digests.Should().NotBeEmpty();
        escalationForms.FindHeadersCalls.Should().Be(1);
        escalationForms.FindCalls.Should().Be(0);
        unassignedNavigator.GetStudyEnrollmentsCalls.Should().Be(1);
        unassignedNavigator.GetChildrenCalls.Should().Be(0);
        unassignedNavigator.ResolveManyCalls.Should().Be(1);

        // 4. Multi-site escalation pass maps all study enrollments in one GetStudyEnrollmentsAsync call without per-site GetChildrenAsync.
        var multiSiteEnrollments = Enumerable.Range(1, 5)
            .Select(i => new StudyEnrollmentNode(
                EnrollmentId.New(),
                $"0{i}-0001",
                StudySiteId.New(),
                $"0{i}"))
            .ToList();
        await using (var db = sqlite.Create(tenant))
        {
            foreach (var node in multiSiteEnrollments)
            {
                var (q, _) = FormQuery.Raise(
                    FormQueryId.New(), tenant, CollectionInstanceId.New(), node.EnrollmentId, "sbp", ObservationId.New(), 1,
                    "Site overdue query", AssertionId.New(), "cra", overdueOpenAt, "SDV");
                q.Escalate("cra", overdueOpenAt.AddDays(14), "14-day SLA");
                db.FormQueries.Add(q);
            }

            using (db.PermitCommandWrites())
            {
                await db.SaveChangesAsync(ct);
            }
        }

        var multiSiteForms = new CountingCollectionInstanceRecords(throwOnFind: true);
        var multiSiteNavigator = new CountingNavigator(
            studyId,
            siteId,
            enrollmentId,
            siteCode: "017",
            subjectCode: "017-0001",
            extraEnrollments: multiSiteEnrollments);
        var multiSiteEscalation = new FormQueryEscalationService(
            new FakeClock(At),
            multiSiteForms,
            multiSiteNavigator,
            sqlite,
            new DigestCommandSender());
        var multiSitePass = await multiSiteEscalation.RunStudyPassAsync(tenant, studyId, "Multi-site SLA pass", ct);
        multiSitePass.IsSuccess.Should().BeTrue();
        multiSitePass.Value!.Digests.Should().HaveCount(7, "1 study-wide digest + 6 site digests (017 + 01..05)");
        multiSiteNavigator.GetStudyEnrollmentsCalls.Should().Be(1);
        multiSiteNavigator.GetChildrenCalls.Should().Be(0);
        multiSiteForms.FindHeadersCalls.Should().Be(0, "all enrollments already carry site metadata from GetStudyEnrollmentsAsync");
        multiSiteForms.FindCalls.Should().Be(0);

        // 5. EvaluateEditChecksOnCollectionChanged pre-hydrates multi-form findings via a single FindManyAsync call.
        var secondFormId = CollectionInstanceId.New();
        var secondObsId = ObservationId.New();
        var record1 = new CollectionInstanceRecord(
            formId,
            enrollmentId,
            ParticipantId.New(),
            studyId,
            siteId,
            ruleInput.ProtocolVersionId,
            "cs.vitals",
            "act.week12",
            At,
            "crc",
            At,
            1,
            [item],
            [new CollectedValueView("sbp", obsId, item.Concept, new QuantityValue(195m, "mm[Hg]"), At, 1, "crc", At)],
            [],
            [],
            [],
            null,
            "Vital signs");
        var record2 = new CollectionInstanceRecord(
            secondFormId,
            enrollmentId,
            ParticipantId.New(),
            studyId,
            siteId,
            ruleInput.ProtocolVersionId,
            "cs.vitals",
            "act.week16",
            At,
            "crc",
            At,
            1,
            [item],
            [new CollectedValueView("sbp", secondObsId, item.Concept, new QuantityValue(198m, "mm[Hg]"), At, 1, "crc", At)],
            [],
            [],
            [],
            null,
            "Vital signs");
        var multiFormRecords = new CountingCollectionInstanceRecords(
            recordsById: new Dictionary<CollectionInstanceId, CollectionInstanceRecord>
            {
                [formId] = record1,
                [secondFormId] = record2,
            },
            throwOnFind: true);
        var multiFindingValidation = Substitute.For<ICollectionValidationService>();
        var runId = ValidationRunId.New();
        multiFindingValidation
            .ValidateStudyCollectionsAsync(
                tenant,
                studyId,
                Arg.Any<DateOnly>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CommandMetadata?>(),
                Arg.Any<CancellationToken>())
            .Returns(CommandResult<CollectionValidationResult>.Success(
                new CollectionValidationResult(
                    null,
                    [
                        new ValidationFindingView(
                            ValidationFindingId.New(),
                            studyId,
                            ValidationStage.Collection,
                            "pack.vitals",
                            packId,
                            "R-SBP-1",
                            1,
                            ValidationSeverity.Warning,
                            new FindingTarget(FindingTargetKind.DatasetRecord, "cs.vitals", formId.Value.ToString("D"), "sbp"),
                            "SBP high on form 1",
                            "<= 180",
                            [],
                            null,
                            null,
                            null,
                            FindingStatus.Open,
                            runId,
                            runId,
                            null,
                            null,
                            null,
                            Enrollment: enrollmentId),
                        new ValidationFindingView(
                            ValidationFindingId.New(),
                            studyId,
                            ValidationStage.Collection,
                            "pack.vitals",
                            packId,
                            "R-SBP-2",
                            1,
                            ValidationSeverity.Warning,
                            new FindingTarget(FindingTargetKind.DatasetRecord, "cs.vitals", secondFormId.Value.ToString("D"), "sbp"),
                            "SBP high on form 2",
                            "<= 180",
                            [],
                            null,
                            null,
                            null,
                            FindingStatus.Open,
                            runId,
                            runId,
                            null,
                            null,
                            null,
                            Enrollment: enrollmentId),
                    ])));
        var raisedAutoQueries = new List<RaiseFormQuery>();
        var multiFormSender = Substitute.For<IKernelCommandSender>();
        multiFormSender
            .SendAsync(Arg.Do<RaiseFormQuery>(raisedAutoQueries.Add), Arg.Any<CommandMetadata?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var cmd = ci.ArgAt<RaiseFormQuery>(0);
                var (q, _) = FormQuery.Raise(
                    cmd.QueryId, cmd.TenantId, cmd.FormId, cmd.EnrollmentId, "sbp", cmd.ObservationId, cmd.Revision,
                    cmd.Text, AssertionId.New(), "svc:execution-edit-checks", At, cmd.ReasonForChange, cmd.Origin, cmd.OwnerRole, cmd.Priority);
                return CommandResult<FormQueryState>.Success(FormQueryState.Of(q, 1));
            });
        var multiFormConsumer = new EvaluateEditChecksOnCollectionChanged(
            multiFindingValidation,
            multiFormRecords,
            obsReader,
            Substitute.For<IValidationFindings>(),
            sqlite,
            multiFormSender);
        var savedAttr = typeof(CollectedValuesSaved).GetCustomAttribute<KernelEventAttribute>()!;
        var savedPayload = JsonSerializer.SerializeToElement(
            new CollectedValuesSaved(
                tenant,
                formId,
                enrollmentId,
                studyId,
                ruleInput.ProtocolVersionId,
                "cs.vitals",
                1,
                [new CollectedValueChange("sbp", obsId, 1, null)]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var savedEnvelope = new EventEnvelope(
            Guid.CreateVersion7(),
            savedAttr.Type,
            savedAttr.SchemaVersion,
            tenant,
            null,
            At,
            "crc@example.test",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            savedPayload);
        await multiFormConsumer.ConsumeAsync(new KernelEventDelivery("multi-form-edit-check", savedEnvelope), ct);
        raisedAutoQueries.Should().HaveCount(2);
        multiFormRecords.FindManyCalls.Should().Be(1);
        multiFormRecords.FindCalls.Should().Be(0);
    }

    private static async Task<HttpContext> ContextWithTenantAsync(TenantId tenant, IServiceProvider? services = null)
    {
        var http = new DefaultHttpContext();
        if (services is not null)
        {
            http.RequestServices = services;
        }

        var filter = new KernelTenantFilter(new FixedTenantResolver(tenant));
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), static _ => ValueTask.FromResult<object?>(null));
        return http;
    }

    private sealed class FixedTenantResolver(TenantId tenant) : IKernelTenantResolver
    {
        public ValueTask<KernelTenantResolution> ResolveAsync(ClaimsPrincipal caller, CancellationToken cancellationToken) =>
            ValueTask.FromResult(KernelTenantResolution.Resolved(tenant));
    }

    private sealed class FixedChangeReason(string reason) : IKernelChangeReason
    {
        public string? Read(HttpContext context) => reason;
    }

    private sealed class MinimalCollectionInstanceRecords(IReadOnlyList<CollectionInstanceRecord> records) : ICollectionInstanceRecords
    {
        private readonly Dictionary<CollectionInstanceId, CollectionInstanceRecord> _byId =
            records.ToDictionary(r => r.CollectionInstanceId);

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken) =>
            Task.FromResult(_byId.GetValueOrDefault(instance));

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
            TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceSummary>>([]);

        public Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(
            TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([.. _byId.Values.Where(r => r.EnrollmentId == enrollment)]);
    }

    private sealed class CountingCollectionInstanceRecords(
        IReadOnlyList<CollectionInstanceSummary>? summaries = null,
        IReadOnlyList<CollectionInstanceRecord>? findQueue = null,
        IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>? recordsById = null,
        IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>? headers = null,
        IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceScopeState>? scopeStates = null,
        IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget>? queryTargets = null,
        IReadOnlyDictionary<ObservationId, int>? observationRevisions = null,
        IReadOnlyList<CollectionRuleInput>? ruleInputs = null,
        bool throwOnFind = false,
        bool throwOnListRecords = false) : ICollectionInstanceRecords
    {
        private readonly Queue<CollectionInstanceRecord> _findQueue = new(findQueue ?? []);
        public int FindCalls { get; private set; }
        public int FindManyCalls { get; private set; }
        public int FindHeaderCalls { get; private set; }
        public int FindHeadersCalls { get; private set; }
        public int FindScopeStateCalls { get; private set; }
        public int FindQueryTargetsCalls { get; private set; }
        public int ListSummariesCalls { get; private set; }
        public int ListRecordsCalls { get; private set; }
        public int ListRuleInputsCalls { get; private set; }
        public IReadOnlyCollection<CollectionInstanceId> LastQueriedTargetIds { get; private set; } = [];

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken)
        {
            FindCalls++;
            if (throwOnFind)
            {
                throw new InvalidOperationException("Full FindAsync must not be called on narrow read paths.");
            }

            if (recordsById is not null && recordsById.TryGetValue(instance, out var mapped))
            {
                return Task.FromResult<CollectionInstanceRecord?>(mapped);
            }

            return Task.FromResult(_findQueue.Count > 0 ? _findQueue.Dequeue() : null);
        }

        public Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>> FindManyAsync(
            TenantId tenant,
            IReadOnlyCollection<CollectionInstanceId> instances,
            CancellationToken cancellationToken)
        {
            FindManyCalls++;
            if (recordsById is null)
            {
                return Task.FromResult<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>>(
                    new Dictionary<CollectionInstanceId, CollectionInstanceRecord>());
            }

            return Task.FromResult<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>>(
                instances.Where(recordsById.ContainsKey).ToDictionary(id => id, id => recordsById[id]));
        }

        public Task<IReadOnlyDictionary<ObservationId, int>> FindObservationRevisionsAsync(
            TenantId tenant,
            IReadOnlyCollection<ObservationId> observations,
            IReadOnlyCollection<CollectionInstanceId>? instances = null,
            CancellationToken cancellationToken = default)
        {
            if (observationRevisions is null)
            {
                return Task.FromResult<IReadOnlyDictionary<ObservationId, int>>(new Dictionary<ObservationId, int>());
            }

            return Task.FromResult<IReadOnlyDictionary<ObservationId, int>>(
                observations.Where(observationRevisions.ContainsKey).ToDictionary(id => id, id => observationRevisions[id]));
        }

        public Task<CollectionInstanceHeader?> FindHeaderAsync(
            TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken = default)
        {
            FindHeaderCalls++;
            return Task.FromResult(headers is not null && headers.TryGetValue(instance, out var h) ? h : null);
        }

        public Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>> FindHeadersAsync(
            TenantId tenant, IReadOnlyCollection<CollectionInstanceId> instances, CancellationToken cancellationToken = default)
        {
            FindHeadersCalls++;
            if (headers is null)
            {
                return Task.FromResult<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>>(
                    new Dictionary<CollectionInstanceId, CollectionInstanceHeader>());
            }

            return Task.FromResult<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>>(
                instances.Where(headers.ContainsKey).ToDictionary(id => id, id => headers[id]));
        }

        public Task<CollectionInstanceScopeState?> FindScopeStateAsync(
            TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken = default)
        {
            FindScopeStateCalls++;
            return Task.FromResult(scopeStates is not null && scopeStates.TryGetValue(instance, out var s) ? s : null);
        }

        public Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget>> FindQueryTargetsAsync(
            TenantId tenant,
            IReadOnlyCollection<CollectionInstanceId> instances,
            IReadOnlySet<EnrollmentId> unblindedEnrollments,
            CancellationToken cancellationToken = default)
        {
            FindQueryTargetsCalls++;
            LastQueriedTargetIds = [.. instances];
            if (queryTargets is null)
            {
                return Task.FromResult<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget>>(
                    new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>());
            }

            return Task.FromResult<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget>>(
                instances.Where(queryTargets.ContainsKey).ToDictionary(id => id, id => queryTargets[id]));
        }

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
            TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken)
        {
            ListSummariesCalls++;
            return Task.FromResult(summaries ?? []);
        }

        public Task<IReadOnlyList<CollectionInstanceRecord>> ListRecordsForEnrollmentAsync(
            TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken)
        {
            ListRecordsCalls++;
            if (throwOnListRecords)
            {
                throw new InvalidOperationException("ListRecordsForEnrollmentAsync must not be called.");
            }

            return Task.FromResult<IReadOnlyList<CollectionInstanceRecord>>([]);
        }

        public Task<IReadOnlyList<CollectionRuleInput>> ListRuleInputsForEnrollmentAsync(
            TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken = default)
        {
            ListRuleInputsCalls++;
            return Task.FromResult(ruleInputs ?? []);
        }
    }

    private sealed class StubReadAccess(bool canSee, bool canUnblind = true, bool canSign = false) : IKernelReadAccess
    {
        public ValueTask<bool> CanSeeAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            ValueTask.FromResult(canSee);

        public ValueTask<IReadOnlySet<NodeRef>> FilterVisibleAsync(
            TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlySet<NodeRef>>(canSee ? nodes.Select(n => n.Unversioned()).ToHashSet() : new HashSet<NodeRef>());

        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(
                canSee
                    ? new Dictionary<NodeRef, MaskReason>()
                    : nodes.ToDictionary(n => n, _ => MaskReason.NotPermitted));

        public ValueTask<bool> CanExerciseAsync(TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(canSign);

        public ValueTask<bool> CanUnblindAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            ValueTask.FromResult(canUnblind);
    }

    private sealed class RecordingCommandSender(CollectedValuesState saved) : IKernelCommandSender
    {
        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command, CommandMetadata? metadata = null, CancellationToken cancellationToken = default) =>
            command is RecordCollectedValues
                ? Task.FromResult(CommandResult<TResult>.Success((TResult)(object)saved))
                : throw new NotSupportedException();
    }

    private sealed class DelegatingQuerySender(Func<object, CancellationToken, Task<CommandResult<FormQueryState>>> handler) : IKernelCommandSender
    {
        public async Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command, CommandMetadata? metadata = null, CancellationToken cancellationToken = default)
        {
            var result = await handler(command, cancellationToken);
            if (!result.IsSuccess)
            {
                return CommandResult<TResult>.Failure(result.Error!);
            }

            return result.Replayed
                ? CommandResult<TResult>.Replay((TResult)(object)result.Value!)
                : CommandResult<TResult>.Success((TResult)(object)result.Value!);
        }
    }

    private sealed class DigestCommandSender : IKernelCommandSender
    {
        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command, CommandMetadata? metadata = null, CancellationToken cancellationToken = default)
        {
            if (command is GenerateFormQueryAgingDigest digest)
            {
                var state = new FormQueryAgingDigestState(
                    digest.DigestId,
                    digest.StudyId,
                    digest.StudySiteId,
                    digest.StudySiteId?.ToString() ?? "study",
                    digest.RecipientRole,
                    digest.DigestDate,
                    At,
                    digest.StudyCode,
                    digest.SiteCode,
                    digest.Entries.Count,
                    digest.Entries);
                return Task.FromResult(CommandResult<TResult>.Success((TResult)(object)state));
            }

            throw new NotSupportedException($"Unexpected command {command.GetType().Name}");
        }
    }

    private sealed class CountingNavigator(
        StudyId studyId,
        StudySiteId siteId,
        EnrollmentId enrollmentId,
        string siteCode,
        string subjectCode,
        IReadOnlyList<StudyEnrollmentNode>? extraEnrollments = null,
        bool directEnrollmentUnderStudy = false) : IKernelNavigator
    {
        private readonly Dictionary<NodeRef, NodeSummary> _nodes = new()
        {
            [NodeRef.For(studyId)] = new NodeSummary(NodeRef.For(studyId), "Study 01", "STUDY-01", "Active"),
            [NodeRef.For(siteId)] = new NodeSummary(NodeRef.For(siteId), $"Site {siteCode}", siteCode, "Active"),
            [NodeRef.For(enrollmentId)] = new NodeSummary(NodeRef.For(enrollmentId), subjectCode, subjectCode, "Enrolled"),
        };

        public int ResolveCalls { get; private set; }
        public int ResolveManyCalls { get; private set; }
        public int GetChildrenCalls { get; private set; }
        public int GetStudyEnrollmentsCalls { get; private set; }

        public Task<NodeSummary?> ResolveAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            return Task.FromResult(_nodes.GetValueOrDefault(node.Unversioned()));
        }

        public Task<IReadOnlyDictionary<NodeRef, NodeSummary>> ResolveManyAsync(
            TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken = default)
        {
            ResolveManyCalls++;
            return Task.FromResult<IReadOnlyDictionary<NodeRef, NodeSummary>>(
                nodes.Select(n => n.Unversioned()).Where(_nodes.ContainsKey).ToDictionary(n => n, n => _nodes[n]));
        }

        public Task<IReadOnlyList<ZoomStep>?> GetZoomPathAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ZoomStep>?>(null);

        public Task<IReadOnlyList<ScopeChild>?> GetChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
        {
            GetChildrenCalls++;
            if (node.Unversioned() == NodeRef.For(studyId))
            {
                if (directEnrollmentUnderStudy)
                {
                    return Task.FromResult<IReadOnlyList<ScopeChild>?>(
                        [new ScopeChild(_nodes[NodeRef.For(enrollmentId)], null)]);
                }

                return Task.FromResult<IReadOnlyList<ScopeChild>?>(
                    [new ScopeChild(_nodes[NodeRef.For(siteId)], 1)]);
            }

            if (node.Unversioned() == NodeRef.For(siteId))
            {
                return Task.FromResult<IReadOnlyList<ScopeChild>?>(
                    [new ScopeChild(_nodes[NodeRef.For(enrollmentId)], null)]);
            }

            return Task.FromResult<IReadOnlyList<ScopeChild>?>([]);
        }

        public Task<IReadOnlyList<StudyEnrollmentNode>?> GetStudyEnrollmentsAsync(
            TenantId tenant, StudyId study, CancellationToken cancellationToken = default)
        {
            GetStudyEnrollmentsCalls++;
            if (study != studyId)
            {
                return Task.FromResult<IReadOnlyList<StudyEnrollmentNode>?>(null);
            }

            var list = new List<StudyEnrollmentNode>
            {
                new(
                    enrollmentId,
                    subjectCode,
                    directEnrollmentUnderStudy ? null : siteId,
                    directEnrollmentUnderStudy ? null : siteCode),
            };
            if (extraEnrollments is not null)
            {
                list.AddRange(extraEnrollments);
            }

            return Task.FromResult<IReadOnlyList<StudyEnrollmentNode>?>(list);
        }
    }

    private sealed class SqliteExecutionContextFactory : IModuleTenantContextFactory<ExecutionModuleDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<ExecutionModuleDbContext> _options;

        private SqliteExecutionContextFactory(SqliteConnection connection, DbContextOptions<ExecutionModuleDbContext> options)
        {
            _connection = connection;
            _options = options;
        }

        public static async Task<SqliteExecutionContextFactory> CreateAsync(CancellationToken ct)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync(ct);
            var options = new DbContextOptionsBuilder<ExecutionModuleDbContext>()
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, SqliteExecutionModelCustomizer>()
                .Options;
            await using var setup = new ExecutionModuleDbContext(options);
            await setup.Database.EnsureCreatedAsync(ct);
            return new SqliteExecutionContextFactory(connection, options);
        }

        public ExecutionModuleDbContext Create(TenantId tenant) => new(_options);

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed class SqliteExecutionModelCustomizer(ModelCustomizerDependencies dependencies)
        : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            var dtoConverter = new DateTimeOffsetToBinaryConverter();
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                    {
                        property.SetValueConverter(dtoConverter);
                    }
                }
            }

            modelBuilder.Entity<FormQueryPeriod>().ComplexProperty<TemporalPeriod>(nameof(IBitemporalEntity.Period), period =>
            {
                period.Property(p => p.Valid)
                    .HasConversion(r => FormatRange(r), s => ParseRange(s))
                    .HasColumnType("TEXT");
                period.Property(p => p.Recorded)
                    .HasConversion(r => FormatRange(r), s => ParseRange(s))
                    .HasColumnType("TEXT");
            });
        }

        private static string FormatRange(TimeRange range) =>
            range.End is { } end
                ? $"{range.Start.ToString("O", CultureInfo.InvariantCulture)}|{end.ToString("O", CultureInfo.InvariantCulture)}"
                : $"{range.Start.ToString("O", CultureInfo.InvariantCulture)}|";

        private static TimeRange ParseRange(string raw)
        {
            var parts = raw.Split('|');
            var start = DateTimeOffset.Parse(parts[0], CultureInfo.InvariantCulture);
            DateTimeOffset? end = parts.Length > 1 && parts[1].Length > 0
                ? DateTimeOffset.Parse(parts[1], CultureInfo.InvariantCulture)
                : null;
            return new TimeRange(start, end);
        }
    }
}
