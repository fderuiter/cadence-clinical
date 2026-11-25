using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Modules.Execution.Infrastructure;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.Module;

/// <summary>
/// The Execution module's form rules that need no database (ADR-2622): the layout a form opens with, how a form read
/// derives its order and completeness from the kernel record, enrollment form summary read-authority checks, and the
/// application role grant.
/// </summary>
public sealed class ExecutionFormModuleTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A layout that repeats an item, or names an empty or overlong one, is not a form layout.</summary>
    [Theory(DisplayName = "FormInstance_Open_RefusesALayoutThatIsNotOne")]
    [InlineData("sbp", "sbp")]
    [InlineData("sbp", " ")]
    public void FormInstanceOpenRefusesALayoutThatIsNotOne(string first, string second)
    {
        var open = () => Open([first, second]);
        var overlong = () => Open([new string('x', FormInstance.ElementIdMaxLength + 1)]);

        open.Should().Throw<ArgumentException>();
        overlong.Should().Throw<ArgumentException>();
        Open(["sbp", "dbp"]).ItemOrder.Should().Equal("sbp", "dbp");
    }

    /// <summary>
    /// A form read lists the items in the module's layout, then any item the layout does not name; a required item is
    /// missing until it has a value, and a reasoned missing value counts as one.
    /// </summary>
    [Fact(DisplayName = "FormResponse_FollowsTheLayout_AndDerivesCompleteness")]
    public void FormResponseFollowsTheLayoutAndDerivesCompleteness()
    {
        var record = Record(
            [Item("sbp", required: true), Item("dbp", required: true), Item("comment"), Item("pulse", required: true)],
            Value("dbp", new MissingValue(NullFlavor.NotDone, "Cuff unavailable")));

        var response = FormResponse.From(record, ["comment", "dbp", "sbp"]);

        response.Items.Select(i => i.ItemId).Should().Equal("comment", "dbp", "sbp", "pulse");
        response.CollectionTitle.Should().Be("Vital signs");
        response.MissingRequired.Should().Equal("sbp", "pulse");
        response.Complete.Should().BeFalse();
        response.Items.Single(i => i.ItemId == "dbp").Revision.Should().Be(1);
        response.Items.Single(i => i.ItemId == "sbp").Revision.Should().Be(0);

        var complete = FormResponse.From(Record([Item("dbp", required: true)], Value("dbp", new MissingValue(NullFlavor.NotDone))), null);
        complete.Complete.Should().BeTrue();
        complete.MissingRequired.Should().BeEmpty();

        var unasked = FormResponse.From(Record([Item("dbp", required: true)], Value("dbp", new MissingValue(NullFlavor.NotAsked))), null);
        unasked.Complete.Should().BeFalse();
        unasked.MissingRequired.Should().Equal("dbp");
        unasked.Items.Single().Revision.Should().Be(1);

        var optionalBlank = FormResponse.From(Record([Item("comment", required: false), Item("notes", required: false)]), null);
        optionalBlank.MissingRequired.Should().BeEmpty();
        optionalBlank.Complete.Should().BeFalse("a non-empty form with zero recorded values must not report Complete");

        var optionalUnasked = FormResponse.From(
            Record([Item("comment", required: false)], Value("comment", new MissingValue(NullFlavor.NotAsked))),
            null);
        optionalUnasked.Complete.Should().BeFalse("NotAsked does not count as a recorded value");

        var optionalRecorded = FormResponse.From(
            Record([Item("comment", required: false), Item("notes", required: false)], Value("comment", new TextValue("Patient rested 5 min"))),
            null);
        optionalRecorded.Complete.Should().BeTrue();

        var zeroItems = FormResponse.From(Record([]), null);
        zeroItems.Complete.Should().BeTrue();
    }

    /// <summary>
    /// Form items with terminology bindings expose their pinned code-system version, codelist code, and hydrated codelist
    /// options while items pinned to a code-system version without a codelist expose null options.
    /// </summary>
    [Fact(DisplayName = "FormResponse_MapsItemTerminology_AndCodelistOptions")]
    public void FormResponseMapsItemTerminologyAndCodelistOptions()
    {
        var csVersion = CodeSystemVersionId.New();
        var position = new CollectionItem(
            "position",
            ConceptRef.New(),
            Terminology: new TerminologyBinding(csVersion, "C71148"),
            ValueKind: ObservationValueKind.Coded,
            Required: true);
        var llt = new CollectionItem(
            "ae_llt",
            ConceptRef.New(),
            Terminology: new TerminologyBinding(csVersion),
            ValueKind: ObservationValueKind.Coded);
        IReadOnlyDictionary<string, IReadOnlyList<CodelistOptionView>> codelists =
            new Dictionary<string, IReadOnlyList<CodelistOptionView>>(StringComparer.Ordinal)
            {
                ["position"] =
                [
                    new CodelistOptionView("SITTING", "Sitting", "SITTING"),
                    new CodelistOptionView("SUPINE", "Supine", "SUPINE"),
                ],
            };

        var response = FormResponse.From(RecordWithCodelists([position, llt], codelists), null);

        var posItem = response.Items.Single(i => i.ItemId == "position");
        posItem.ValueKind.Should().Be("Coded");
        posItem.CodeSystemVersionId.Should().Be(csVersion.Value);
        posItem.Codelist.Should().Be("C71148");
        posItem.CodelistOptions.Should().Equal(
            new CodelistOptionView("SITTING", "Sitting", "SITTING"),
            new CodelistOptionView("SUPINE", "Supine", "SUPINE"));

        var lltItem = response.Items.Single(i => i.ItemId == "ae_llt");
        lltItem.ValueKind.Should().Be("Coded");
        lltItem.CodeSystemVersionId.Should().Be(csVersion.Value);
        lltItem.Codelist.Should().BeNull();
        lltItem.CodelistOptions.Should().BeNull();

        var blindedResponse = FormResponse.From(
            RecordWithCodelists([position, llt], codelists, Value("position", new CodedValue(csVersion, "SITTING"))),
            null,
            blindedItemIds: new HashSet<string>(StringComparer.Ordinal) { "position" });
        var blindedPos = blindedResponse.Items.Single(i => i.ItemId == "position");
        blindedPos.Value.Should().Be(new MissingValue(NullFlavor.Masked));
        blindedPos.CodeSystemVersionId.Should().BeNull();
        blindedPos.Codelist.Should().BeNull();
        blindedPos.CodelistOptions.Should().BeNull();

        var blindedNotAsked = FormResponse.From(
            RecordWithCodelists([position], codelists, Value("position", new MissingValue(NullFlavor.NotAsked))),
            null,
            blindedItemIds: new HashSet<string>(StringComparer.Ordinal) { "position" });
        blindedNotAsked.Items.Single(i => i.ItemId == "position").Value.Should().Be(new MissingValue(NullFlavor.NotAsked));
        blindedNotAsked.Items.Single(i => i.ItemId == "position").CodelistOptions.Should().BeNull();

        var recordWithBlindedObs = RecordWithCodelists(
            [position],
            codelists,
            Value("position", new CodedValue(csVersion, "SITTING"))) with
        {
            BlindedItemIds = new HashSet<string>(StringComparer.Ordinal) { "position" },
        };
        recordWithBlindedObs.MaskBlinded().ItemCodelists.Should().BeNull();
    }

    /// <summary>
    /// Listing an enrollment's forms validates enrollment_id (400), checks read access on the enrollment before touching
    /// records (403), and returns mapped form summaries when visible (200).
    /// </summary>
    [Fact(DisplayName = "FormEndpoints_ListForEnrollment_EnforcesReadAuthority_AndMapsSummaries")]
    public async Task FormEndpointsListForEnrollmentEnforcesReadAuthorityAndMapsSummaries()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var enrollment = EnrollmentId.New();
        var summary = new CollectionInstanceSummary(
            CollectionInstanceId.New(),
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
            2,
            false,
            3,
            1,
            "Vital signs");
        var http = await ContextWithTenantAsync(tenant);
        var records = new StubCollectionInstanceRecords([summary]);

        var missing = await FormEndpoints.ListForEnrollmentAsync(null, http, records, new StubReadAccess(true), ct);
        missing.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var emptyGuid = await FormEndpoints.ListForEnrollmentAsync(Guid.Empty, http, records, new StubReadAccess(true), ct);
        emptyGuid.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        records.ListCalls.Should().Be(0);

        var access = new StubReadAccess(false);
        var denied = await FormEndpoints.ListForEnrollmentAsync(enrollment.Value, http, records, access, ct);
        denied.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        access.CheckedNode.Should().Be(NodeRef.For(enrollment));
        records.ListCalls.Should().Be(0);

        var allowed = await FormEndpoints.ListForEnrollmentAsync(enrollment.Value, http, records, new StubReadAccess(true), ct);
        var ok = allowed.Result.Should().BeOfType<Ok<IReadOnlyList<FormSummaryResponse>>>().Subject;
        ok.Value.Should().ContainSingle().Which.Should().Match<FormSummaryResponse>(s =>
            s.FormId == summary.CollectionInstanceId.Value
            && s.EnrollmentId == enrollment.Value
            && s.CollectionSpecId == "cs.vitals"
            && s.CollectionTitle == "Vital signs"
            && s.PlannedActivityId == "act.week12"
            && !s.Complete
            && s.ItemCount == 3
            && s.RecordedCount == 1
            && s.Version == 2);
        records.ListCalls.Should().Be(1);
    }

    /// <summary>
    /// The grant gives the role read, insert and update on the module's schema, delete only on the used signature tokens,
    /// and refuses a name that is not a role.
    /// </summary>
    [Fact(DisplayName = "ExecutionModuleDatabase_GrantAppRoleSql_GrantsTheModuleSchema_AndRefusesBadNames")]
    public void ExecutionModuleDatabaseGrantAppRoleSqlGrantsTheModuleSchemaAndRefusesBadNames()
    {
        var sql = ExecutionModuleDatabase.GrantAppRoleSql("cadence_app");
        var injected = () => ExecutionModuleDatabase.GrantAppRoleSql("app\"; DROP SCHEMA execution; --");

        sql.Should().Contain("GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA \"execution\" TO \"cadence_app\"")
            .And.Contain("REVOKE ALL ON \"execution\".\"__ef_migrations_history\" FROM \"cadence_app\"")
            .And.Contain("GRANT DELETE ON \"execution\".\"consumed_signature_tokens\" TO \"cadence_app\"");
        sql.Split('\n').Where(line => line.Contains("DELETE", StringComparison.Ordinal)).Should().ContainSingle();
        injected.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Previewing form values loads the target record once and passes that <see cref="CollectionInstanceRecord"/> directly
    /// to <see cref="ICollectionValidationService.PreviewFormAsync(TenantId, CollectionInstanceRecord, IReadOnlyDictionary{string, ObservationValue?}, DateOnly, CancellationToken)"/>.
    /// </summary>
    [Fact(DisplayName = "FormEndpoints_PreviewAsync_ForwardsLoadedRecordWithoutSecondFindAsync")]
    public async Task FormEndpointsPreviewAsyncForwardsLoadedRecordWithoutSecondFindAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var record = Record([Item("sbp", required: true), Item("dbp", required: true)]);
        var validation = new RecordingValidationService(
        [
            new CollectionPreviewWarning("vitals.pack", "R1", 1, ValidationSeverity.Warning, "cs.vitals", "sbp", "High systolic BP"),
        ]);
        var services = new ServiceCollection()
            .AddSingleton<ICollectionValidationService>(validation)
            .BuildServiceProvider();
        var http = await ContextWithTenantAsync(tenant, services);
        var records = new StubCollectionInstanceRecords([], [record]);
        var request = new PreviewFormValuesRequest([new PreviewFormItemValueRequest("sbp", new QuantityValue(185m, "mm[Hg]"))]);

        var response = await FormEndpoints.PreviewAsync(
            record.CollectionInstanceId.Value, request, http, records, new StubReadAccess(true), new FixedClock(At), ct);

        var ok = response.Result.Should().BeOfType<Ok<FormPreviewResponse>>().Subject;
        ok.Value!.FormId.Should().Be(record.CollectionInstanceId.Value);
        ok.Value.Warnings.Should().ContainSingle().Which.RuleId.Should().Be("R1");
        records.FindCalls.Should().Be(1);
        validation.PreviewRecordCalls.Should().Be(1);
        validation.PreviewByIdCalls.Should().Be(0);
    }

    /// <summary>
    /// Saving form values returns the hydrated <see cref="FormResponse"/> in the same response so callers do not issue a
    /// follow-up <c>GET /forms/{formId}</c> round-trip.
    /// </summary>
    [Fact(DisplayName = "FormEndpoints_SaveAsync_ReturnsHydratedFormResponse")]
    public async Task FormEndpointsSaveAsyncReturnsHydratedFormResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var sbpObs = ObservationId.New();
        var initial = Record([Item("sbp", required: true)]);
        var updated = new CollectionInstanceRecord(
            initial.CollectionInstanceId,
            initial.EnrollmentId,
            initial.ParticipantId,
            initial.StudyId,
            initial.StudySiteId,
            initial.ProtocolVersionId,
            initial.CollectionSpecId,
            initial.PlannedActivityId,
            initial.CollectedAt,
            initial.OpenedBy,
            initial.OpenedAt,
            2,
            initial.Items,
            [new CollectedValueView("sbp", sbpObs, ConceptRef.New(), new QuantityValue(124m, "mm[Hg]"), At, 1, "crc@example.test", At)],
            [],
            [],
            [],
            "hash-v2",
            "Vital signs");
        var validDesign = new ProtocolDesign
        {
            Title = "Vital Signs Protocol",
            CollectionSpecs = [new CollectionSpec("cs.vitals", "Vital signs", initial.Items)],
        };
        var validDesignJson = ProtocolDesignDocument.Write(validDesign);
        var http = await ContextWithTenantAsync(tenant);
        http.Request.Headers[FormEndpoints.IdempotencyKeyHeader] = "idem-save-1";
        var records = new StubCollectionInstanceRecords(
            [],
            [updated],
            headers: new Dictionary<CollectionInstanceId, CollectionInstanceHeader>
            {
                [initial.CollectionInstanceId] = CollectionInstanceHeader.From(initial),
            });
        var sender = new StubCommandSender(
            new CollectedValuesState(
                initial.CollectionInstanceId,
                2,
                [new CollectedValueState("sbp", sbpObs, new QuantityValue(124m, "mm[Hg]"), 1, true)]),
            protocolContentToRead: validDesignJson);
        var request = new SaveFormValuesRequest(
            [new FormValueRequest("sbp", new QuantityValue(124m, "mm[Hg]"), 0, null, null)]);

        int protocolReads;
        Results<Ok<FormSavedResponse>, ProblemHttpResult> response;
        using (var readScope = ProtocolDesignDocument.MeasureReads())
        {
            response = await FormEndpoints.SaveAsync(
                initial.CollectionInstanceId.Value,
                request,
                http,
                records,
                sender,
                new FixedChangeReason("Initial vitals entry"),
                ct,
                contexts: null,
                access: new StubReadAccess(true, canSign: true));
            protocolReads = readScope.Count;
        }

        var ok = response.Result.Should().BeOfType<Ok<FormSavedResponse>>().Subject;
        ok.Value!.Version.Should().Be(2);
        ok.Value.Values.Should().ContainSingle().Which.Revision.Should().Be(1);
        ok.Value.Form.Should().NotBeNull();
        ok.Value.Form!.Version.Should().Be(2);
        ok.Value.Form.Complete.Should().BeTrue();
        ok.Value.Form.CanSign.Should().BeTrue();
        ok.Value.Form.Items.Should().ContainSingle().Which.Revision.Should().Be(1);
        records.FindHeaderCalls.Should().Be(1, "pre-save authorization resolves EnrollmentId via FindHeaderAsync");
        records.FindCalls.Should().Be(1, "only the post-save hydrated FormResponse read calls FindAsync");
        records.LastGoverningDesign.Should().NotBeNull();
        records.LastGoverningDesign!.Title.Should().Be("Vital Signs Protocol");
        protocolReads.Should().Be(1, "SaveAsync parses governing protocol content at most once per request");
    }

    /// <summary>
    /// When <see cref="CollectionInstanceRecord.BlindedItemIds"/> and <see cref="CollectionInstanceRecord.BlindedObservationIds"/>
    /// are precomputed by persistence, blinded <c>GetAsync</c> and <c>SaveAsync</c> redact blinded items and superseded
    /// <c>SignedValues</c> without querying <see cref="IProtocolVersionReader"/> or <see cref="IConceptRegistry"/>.
    /// </summary>
    [Fact(DisplayName = "FormEndpoints_GetAndSaveAsync_ReusePrecomputedBlindingSets_WithoutQueryingProtocolsOrConcepts")]
    public async Task FormEndpointsGetAndSaveAsyncReusePrecomputedBlindingSetsWithoutQueryingProtocolsOrConcepts()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var sbpObs = ObservationId.New();
        var trtObs = ObservationId.New();
        var priorSignedTrtObs = ObservationId.New();
        var items = new[] { Item("sbp", required: true), Item("trt_arm", required: true) };
        var record = new CollectionInstanceRecord(
            CollectionInstanceId.New(),
            EnrollmentId.New(),
            ParticipantId.New(),
            StudyId.New(),
            StudySiteId.New(),
            ProtocolVersionId.New(),
            "cs.day1",
            "act.day1",
            At,
            "crc@example.test",
            At,
            3,
            items,
            [
                new CollectedValueView("sbp", sbpObs, ConceptRef.New(), new QuantityValue(124m, "mm[Hg]"), At, 2, "crc@example.test", At),
                new CollectedValueView("trt_arm", trtObs, ConceptRef.New(), new TextValue("SECRET-ARM-A"), At, 2, "irt@example.test", At),
            ],
            [],
            [],
            [
                new FormAttestationView(
                    AttestationId.New(),
                    AttestationMeaning.Approved,
                    "pi@example.test",
                    At.AddMinutes(-5),
                    "hash-v1",
                    "PI review",
                    Superseded: true,
                    SignedValues:
                    [
                        new SignedValueView("sbp", sbpObs, 1, new QuantityValue(120m, "mm[Hg]")),
                        new SignedValueView("trt_arm", priorSignedTrtObs, 1, new TextValue("SECRET-PRIOR-ARM")),
                    ]),
            ],
            "hash-v2",
            "Day 1 Visit",
            BlindedItemIds: FrozenSet.Create(StringComparer.Ordinal, "trt_arm"),
            BlindedObservationIds: FrozenSet.Create(trtObs));

        var services = new ServiceCollection()
            .AddSingleton<IProtocolVersionReader>(new ThrowingProtocolVersionReader())
            .AddSingleton<IConceptRegistry>(new ThrowingConceptRegistry())
            .BuildServiceProvider();

        var contexts = new InMemoryFormContextFactory($"exec-forms-precomputed-{Guid.NewGuid():N}");
        var getHttp = await ContextWithTenantAsync(tenant, services);
        var getResponse = await FormEndpoints.GetAsync(
            record.CollectionInstanceId.Value,
            getHttp,
            new StubCollectionInstanceRecords([], [record]),
            contexts,
            new StubReadAccess(canSee: true, canSign: true, canUnblind: false),
            ct);

        var got = getResponse.Result.Should().BeOfType<Ok<FormResponse>>().Subject.Value!;
        var maskedItem = got.Items.Single(i => i.ItemId == "trt_arm");
        maskedItem.ObservationId.Should().BeNull();
        maskedItem.RecordedBy.Should().BeNull();
        maskedItem.Value.Should().Be(new MissingValue(NullFlavor.Masked));
        got.Items.Single(i => i.ItemId == "sbp").Value.Should().Be(new QuantityValue(124m, "mm[Hg]"));

        var signedTrt = got.Signatures.Single().SignedValues!.Single(sv => sv.ItemId == "trt_arm");
        signedTrt.ObservationId.Should().Be(Guid.Empty);
        signedTrt.Value.Should().Be(new MissingValue(NullFlavor.Masked));

        var saveHttp = await ContextWithTenantAsync(tenant, services);
        saveHttp.Request.Headers[FormEndpoints.IdempotencyKeyHeader] = "idem-blinded-save";
        var sender = new StubCommandSender(
            new CollectedValuesState(
                record.CollectionInstanceId,
                4,
                [
                    new CollectedValueState("sbp", sbpObs, new QuantityValue(126m, "mm[Hg]"), 3, true),
                    new CollectedValueState("trt_arm", trtObs, new TextValue("SECRET-ARM-A"), 2, false),
                ]));
        var updated = record with
        {
            Version = 4,
            Values =
            [
                new CollectedValueView("sbp", sbpObs, ConceptRef.New(), new QuantityValue(126m, "mm[Hg]"), At, 3, "crc@example.test", At),
                new CollectedValueView("trt_arm", trtObs, ConceptRef.New(), new TextValue("SECRET-ARM-A"), At, 2, "irt@example.test", At),
            ],
        };
        var saveResponse = await FormEndpoints.SaveAsync(
            record.CollectionInstanceId.Value,
            new SaveFormValuesRequest([new FormValueRequest("sbp", new QuantityValue(126m, "mm[Hg]"), 2, null, null)]),
            saveHttp,
            new StubCollectionInstanceRecords([], [record, updated]),
            sender,
            new FixedChangeReason("Updated systolic BP"),
            ct,
            contexts,
            new StubReadAccess(canSee: true, canSign: true, canUnblind: false));

        var saved = saveResponse.Result.Should().BeOfType<Ok<FormSavedResponse>>().Subject.Value!;
        var savedTrt = saved.Values.Single(v => v.ItemId == "trt_arm");
        savedTrt.ObservationId.Should().Be(Guid.Empty);
        savedTrt.Value.Should().Be(new MissingValue(NullFlavor.Masked));
        saved.Form!.Items.Single(i => i.ItemId == "trt_arm").Value.Should().Be(new MissingValue(NullFlavor.Masked));
    }

    /// <summary>
    /// When <see cref="CollectionInstanceRecord.BlindedItemIds"/> is <c>null</c> (in-memory test fixtures), blinded reads
    /// batch study designs and item concepts instead of looping per version or per concept.
    /// </summary>
    [Fact(DisplayName = "FormEndpoints_GetAsync_BatchesProtocolDesignsAndConcepts_WhenBlindingSetsAreNull")]
    public async Task FormEndpointsGetAsyncBatchesProtocolDesignsAndConceptsWhenBlindingSetsAreNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var studyId = StudyId.New();
        var versionId = ProtocolVersionId.New();
        var blindedConcept = new ConceptRef(ConceptId.New(), 1);
        var sbpConcept = new ConceptRef(ConceptId.New(), 1);
        var staleDbpConcept = new ConceptRef(ConceptId.New(), 9);

        var items = new[]
        {
            new CollectionItem("sbp", sbpConcept, Required: true),
            new CollectionItem("dbp", staleDbpConcept, Required: false),
            new CollectionItem("arm_code", blindedConcept, Required: true),
        };
        var trtObs = ObservationId.New();
        var record = new CollectionInstanceRecord(
            CollectionInstanceId.New(),
            EnrollmentId.New(),
            ParticipantId.New(),
            studyId,
            StudySiteId.New(),
            versionId,
            "cs.baseline",
            "act.day1",
            At,
            "crc@example.test",
            At,
            2,
            items,
            [
                new CollectedValueView("sbp", ObservationId.New(), sbpConcept, new QuantityValue(122m, "mm[Hg]"), At, 1, "crc@example.test", At),
                new CollectedValueView("arm_code", trtObs, blindedConcept, new TextValue("ACTIVE-DOSE-200"), At, 1, "irt@example.test", At),
            ],
            [],
            [],
            [],
            "hash-null-sets",
            "Baseline Visit",
            BlindedItemIds: null,
            BlindedObservationIds: null);

        var protocolReader = new CountingProtocolVersionReader(
            studyId,
            versionId,
            new ProtocolDesign
            {
                Title = "Blinded Trial",
                Interventions = [new Intervention("int.1", "Study Drug", blindedConcept)],
                CollectionSpecs = [new CollectionSpec("cs.baseline", "Baseline Visit", items)],
            });
        var conceptRegistry = new CountingConceptRegistry(
            pinned: new Dictionary<ConceptRef, ConceptVersionView>
            {
                [sbpConcept] = new(sbpConcept, ConceptOrigin.Sponsor, "SBP", ConceptKind.BiomedicalConcept, "Systolic BP", "Systolic blood pressure", ConceptStatus.Active, 1, ConceptMappingState.Unmapped, At),
                [blindedConcept] = new(blindedConcept, ConceptOrigin.Sponsor, "ARM", ConceptKind.BiomedicalConcept, "Assigned Arm", "Randomized treatment arm", ConceptStatus.Active, 1, ConceptMappingState.Unmapped, At),
            },
            current: new Dictionary<ConceptId, ConceptVersionView>
            {
                [staleDbpConcept.ConceptId] = new(new ConceptRef(staleDbpConcept.ConceptId, 1), ConceptOrigin.Sponsor, "DBP", ConceptKind.BiomedicalConcept, "Diastolic BP", "Diastolic blood pressure", ConceptStatus.Active, 1, ConceptMappingState.Unmapped, At),
            });

        var services = new ServiceCollection()
            .AddSingleton<IProtocolVersionReader>(protocolReader)
            .AddSingleton<IConceptRegistry>(conceptRegistry)
            .BuildServiceProvider();
        var http = await ContextWithTenantAsync(tenant, services);

        var contexts = new InMemoryFormContextFactory($"exec-forms-fallback-{Guid.NewGuid():N}");
        var response = await FormEndpoints.GetAsync(
            record.CollectionInstanceId.Value,
            http,
            new StubCollectionInstanceRecords([], [record]),
            contexts,
            new StubReadAccess(canSee: true, canSign: false, canUnblind: false),
            ct);

        var got = response.Result.Should().BeOfType<Ok<FormResponse>>().Subject.Value!;
        got.Items.Single(i => i.ItemId == "arm_code").Value.Should().Be(new MissingValue(NullFlavor.Masked));
        got.Items.Single(i => i.ItemId == "sbp").Value.Should().Be(new QuantityValue(122m, "mm[Hg]"));

        protocolReader.GetDesignsForStudyCalls.Should().Be(1);
        protocolReader.SingleGetDesignCalls.Should().Be(0);
        conceptRegistry.ResolveManyCalls.Should().Be(1);
        conceptRegistry.GetCurrentManyCalls.Should().Be(1);
        conceptRegistry.SingleResolveCalls.Should().Be(0);
        conceptRegistry.SingleGetCurrentCalls.Should().Be(0);
    }

    /// <summary>
    /// Scheduled forms expose <c>Early</c>, <c>InWindow</c>, and <c>Late</c> timing fields from
    /// <see cref="ExpectedSchedule.Assess"/> across open and list endpoints, and out-of-window opens emit
    /// <c>VISIT_WINDOW</c> deviation events without emitting deviations for in-window visits.
    /// </summary>
    [Fact(DisplayName = "FormEndpoints_And_OpenFormInstanceHandler_AssessVisitTiming_AndEmitVisitWindowDeviations")]
    public async Task FormEndpointsAndOpenFormInstanceHandlerAssessVisitTimingAndEmitVisitWindowDeviations()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var enrollment = EnrollmentId.New();
        var participant = ParticipantId.New();
        var study = StudyId.New();
        var site = StudySiteId.New();
        var versionId = ProtocolVersionId.New();
        var enrolledAt = new DateTimeOffset(2026, 3, 1, 8, 30, 0, TimeSpan.FromHours(1));

        var design = new ProtocolDesign
        {
            SchemaVersion = 1,
            Title = "Timing Assessment Trial",
            Epochs = [new Epoch("ep.treat", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(
                    "act.week4",
                    "Week 4",
                    "ep.treat",
                    [ActivityLocation.Site],
                    ["cs.vitals"],
                    new ActivityTiming(Day: 28, WindowBefore: 2, WindowAfter: 2)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec("cs.vitals", "Vital signs", [Item("sbp", required: true)]),
            ],
        };
        var versionReader = new StubProtocolVersionReader(versionId, design);
        var sink = new RecordingDeviationSink();
        var earlyCollected = new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.FromHours(1));
        var inWindowCollected = new DateTimeOffset(2026, 3, 28, 10, 0, 0, TimeSpan.FromHours(1));
        var lateCollected = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.FromHours(1));

        var earlyId = CollectionInstanceId.New();
        var inWindowId = CollectionInstanceId.New();
        var lateId = CollectionInstanceId.New();

        CollectionInstanceSummary MakeSummary(CollectionInstanceId id, DateTimeOffset collected) =>
            new(id, enrollment, participant, study, site, versionId, "cs.vitals", "act.week4", collected,
                "crc@example.test", collected, 1, true, 1, 1, "Vital signs", EnrollmentReferenceAt: enrolledAt);

        CollectionInstanceRecord MakeRecord(CollectionInstanceId id, DateTimeOffset collected) =>
            new(id, enrollment, participant, study, site, versionId, "cs.vitals", "act.week4", collected,
                "crc@example.test", collected, 1, [Item("sbp", required: true)], [], [], [], [], null, "Vital signs",
                EnrollmentReferenceAt: enrolledAt);

        var records = new StubCollectionInstanceRecords(
            [MakeSummary(earlyId, earlyCollected), MakeSummary(inWindowId, inWindowCollected), MakeSummary(lateId, lateCollected)],
            [MakeRecord(lateId, lateCollected), MakeRecord(inWindowId, inWindowCollected), MakeRecord(earlyId, earlyCollected)]);

        var services = new ServiceCollection()
            .AddSingleton<IProtocolVersionReader>(versionReader)
            .AddSingleton<ICollectionInstanceRecords>(records)
            .AddSingleton<IVisitWindowDeviationSink>(sink)
            .BuildServiceProvider();
        var http = await ContextWithTenantAsync(tenant, services);
        http.Request.Headers[FormEndpoints.IdempotencyKeyHeader] = "idem-open-late";

        // 1. ListForEnrollment computes Early, InWindow, and Late + VISIT_WINDOW category from ExpectedSchedule.Assess.
        var listResult = await FormEndpoints.ListForEnrollmentAsync(enrollment.Value, http, records, new StubReadAccess(true), ct);
        var listed = listResult.Result.Should().BeOfType<Ok<IReadOnlyList<FormSummaryResponse>>>().Subject.Value!;
        listed.Select(s => s.VisitTiming).Should().Equal("Early", "InWindow", "Late");
        listed.Select(s => s.DeviationCategory).Should().Equal("VISIT_WINDOW", null, "VISIT_WINDOW");
        listed[0].DaysOutsideWindow.Should().BeGreaterThan(0);
        listed[1].DaysOutsideWindow.Should().Be(0);
        listed[2].DaysOutsideWindow.Should().BeGreaterThan(0);
        listed[1].TargetDate.Should().Be(new DateOnly(2026, 3, 28));
        listed[1].WindowStart.Should().Be(new DateOnly(2026, 3, 26));
        listed[1].WindowEnd.Should().Be(new DateOnly(2026, 3, 30));

        // 2. OpenAsync returns VisitTiming on FormStateResponse and notifies IVisitWindowDeviationSink for out-of-window opens.
        var openSender = new StubOpenCommandSender(
            new CollectionInstanceState(lateId, enrollment, versionId, "cs.vitals", "act.week4", lateCollected, 1));
        var openRes = await FormEndpoints.OpenAsync(
            new OpenFormRequest(lateId.Value, enrollment.Value, "cs.vitals", "act.week4", lateCollected),
            http,
            openSender,
            new FixedChangeReason("Open late Week 4 vitals"),
            ct);
        var created = openRes.Result.Should().BeOfType<Created<FormStateResponse>>().Subject.Value!;
        created.VisitTiming.Should().Be("Late");
        created.DeviationCategory.Should().Be("VISIT_WINDOW");
        created.DaysOutsideWindow.Should().Be(6);
        sink.Events.Should().ContainSingle().Which.Should().Match<VisitWindowDeviationDetected>(e =>
            e.FormId == lateId
            && e.Category == "VISIT_WINDOW"
            && e.Timing == "Late"
            && e.DaysOutsideWindow == 6);

        // 3. OpenFormInstanceHandler raises VisitWindowDeviationDetected on out-of-window forms and not on in-window forms.
        var handler = new Cadence.Modules.Execution.Application.OpenFormInstanceHandler(records, versionReader);
        var inWindowWork = new RecordingModuleUnitOfWork();
        var inWindowOutcome = await handler.HandleAsync(
            new Cadence.Modules.Execution.Application.OpenFormInstance(tenant, inWindowId, enrollment, "idem-in", "Open in-window"),
            new StubCommandContext(tenant, inWindowWork),
            ct);
        inWindowOutcome.IsSuccess.Should().BeTrue();
        inWindowWork.RaisedEvents.Should().BeEmpty();

        var earlyWork = new RecordingModuleUnitOfWork();
        var earlyOutcome = await handler.HandleAsync(
            new Cadence.Modules.Execution.Application.OpenFormInstance(tenant, earlyId, enrollment, "idem-early", "Open early"),
            new StubCommandContext(tenant, earlyWork),
            ct);
        earlyOutcome.IsSuccess.Should().BeTrue();
        earlyWork.RaisedEvents.OfType<VisitWindowDeviationDetected>().Should().ContainSingle().Which.Should().Match<VisitWindowDeviationDetected>(e =>
            e.FormId == earlyId
            && e.Category == "VISIT_WINDOW"
            && e.Timing == "Early"
            && e.DaysOutsideWindow == 6);
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

    private static FormInstance Open(string[] layout) =>
        FormInstance.Open(
            CollectionInstanceId.New(), TenantId.New(), EnrollmentId.New(), ParticipantId.New(), StudyId.New(), ProtocolVersionId.New(),
            "cs.vitals", "act.week12", At, layout);

    private static CollectionItem Item(string id, bool required = false) => new(id, ConceptRef.New(), Required: required);

    private static CollectedValueView Value(string item, ObservationValue value) =>
        new(item, ObservationId.New(), ConceptRef.New(), value, At, 1, "crc@example.test", At);

    private static CollectionInstanceRecord Record(CollectionItem[] items, params CollectedValueView[] values) =>
        new(
            CollectionInstanceId.New(), EnrollmentId.New(), ParticipantId.New(), StudyId.New(), StudySiteId.New(), ProtocolVersionId.New(),
            "cs.vitals", "act.week12", At, "crc@example.test", At, 1 + values.Length, items, values, [], [], [], null, "Vital signs");

    private static CollectionInstanceRecord RecordWithCodelists(
        CollectionItem[] items,
        IReadOnlyDictionary<string, IReadOnlyList<CodelistOptionView>> itemCodelists,
        params CollectedValueView[] values) =>
        new(
            CollectionInstanceId.New(), EnrollmentId.New(), ParticipantId.New(), StudyId.New(), StudySiteId.New(), ProtocolVersionId.New(),
            "cs.vitals", "act.week12", At, "crc@example.test", At, 1 + values.Length, items, values, [], [], [], null, "Vital signs",
            ItemCodelists: itemCodelists);

    private sealed class FixedTenantResolver(TenantId tenant) : IKernelTenantResolver
    {
        public ValueTask<KernelTenantResolution> ResolveAsync(ClaimsPrincipal caller, CancellationToken cancellationToken) =>
            ValueTask.FromResult(KernelTenantResolution.Resolved(tenant));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FixedChangeReason(string reason) : IKernelChangeReason
    {
        public string? Read(HttpContext context) => reason;
    }

    private sealed class StubReadAccess(bool canSee, bool canSign = false, bool? canUnblind = null) : IKernelReadAccess
    {
        public NodeRef? CheckedNode { get; private set; }

        public ValueTask<bool> CanSeeAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken)
        {
            CheckedNode = node;
            return ValueTask.FromResult(canSee);
        }

        public ValueTask<bool> CanExerciseAsync(TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Capability.IsUnblinding(capability.Token) ? (canUnblind ?? canSign) : canSign);
    }

    private sealed class InMemoryFormContextFactory(string dbName) : IModuleTenantContextFactory<ExecutionModuleDbContext>
    {
        public ExecutionModuleDbContext Create(TenantId tenant)
        {
            var options = new DbContextOptionsBuilder<ExecutionModuleDbContext>()
                .UseInMemoryDatabase($"{dbName}-{tenant.Value:N}")
                .Options;
            return new ExecutionModuleDbContext(options);
        }
    }

    private sealed class ThrowingProtocolVersionReader : IProtocolVersionReader
    {
        public Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Protocol versions should not be queried when blinding sets are precomputed.");

        public Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Protocol versions should not be queried when blinding sets are precomputed.");

        public Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Protocol versions should not be queried when blinding sets are precomputed.");

        public Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Protocol designs should not be queried when blinding sets are precomputed.");

        public Task<IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign>> GetDesignsForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Protocol designs should not be queried when blinding sets are precomputed.");
    }

    private sealed class ThrowingConceptRegistry : IConceptRegistry
    {
        public Task<ConceptVersionView?> ResolveAsync(TenantId tenant, ConceptRef concept, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Concepts should not be queried when blinding sets are precomputed.");

        public Task<IReadOnlyDictionary<ConceptRef, ConceptVersionView>> ResolveManyAsync(TenantId tenant, IReadOnlyCollection<ConceptRef> concepts, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Concepts should not be queried when blinding sets are precomputed.");

        public Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Concepts should not be queried when blinding sets are precomputed.");

        public Task<IReadOnlyDictionary<ConceptId, ConceptVersionView>> GetCurrentManyAsync(TenantId tenant, IReadOnlyCollection<ConceptId> concepts, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Concepts should not be queried when blinding sets are precomputed.");

        public Task<ConceptVersionView?> FindBiomedicalConceptAsync(TenantId tenant, string cdiscConceptCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CodeSystemVersionView?> FindCodeSystemVersionAsync(TenantId tenant, string codeSystemUri, string version, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CodeSystemEntryView>> LookupAsync(TenantId tenant, Cadence.Kernel.Knowledge.Coding coding, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CodeSystemEntryView>> GetGroupAsync(TenantId tenant, CodeSystemVersionId version, string groupCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ConceptMappingView>> ResolveCodingAsync(TenantId tenant, Cadence.Kernel.Knowledge.Coding coding, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ConceptMappingView>> GetMappingsAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingProtocolVersionReader(
        StudyId studyId,
        ProtocolVersionId versionId,
        ProtocolDesign design) : IProtocolVersionReader
    {
        public int GetDesignsForStudyCalls { get; private set; }
        public int SingleGetDesignCalls { get; private set; }

        public Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProtocolVersionSummary>>([]);

        public Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<ProtocolVersionSummary?>(null);

        public Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<SoaMatrix?>(null);

        public Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
        {
            SingleGetDesignCalls++;
            return Task.FromResult(version == versionId ? design : null);
        }

        public Task<IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign>> GetDesignsForStudyAsync(
            TenantId tenant,
            StudyId study,
            CancellationToken cancellationToken)
        {
            GetDesignsForStudyCalls++;
            IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign> result = study == studyId
                ? new Dictionary<ProtocolVersionId, ProtocolDesign> { [versionId] = design }
                : new Dictionary<ProtocolVersionId, ProtocolDesign>();
            return Task.FromResult(result);
        }
    }

    private sealed class CountingConceptRegistry(
        IReadOnlyDictionary<ConceptRef, ConceptVersionView> pinned,
        IReadOnlyDictionary<ConceptId, ConceptVersionView> current) : IConceptRegistry
    {
        public int SingleResolveCalls { get; private set; }
        public int SingleGetCurrentCalls { get; private set; }
        public int ResolveManyCalls { get; private set; }
        public int GetCurrentManyCalls { get; private set; }

        public Task<ConceptVersionView?> ResolveAsync(TenantId tenant, ConceptRef concept, CancellationToken cancellationToken = default)
        {
            SingleResolveCalls++;
            return Task.FromResult(pinned.GetValueOrDefault(concept));
        }

        public Task<IReadOnlyDictionary<ConceptRef, ConceptVersionView>> ResolveManyAsync(
            TenantId tenant,
            IReadOnlyCollection<ConceptRef> concepts,
            CancellationToken cancellationToken = default)
        {
            ResolveManyCalls++;
            IReadOnlyDictionary<ConceptRef, ConceptVersionView> found = concepts
                .Where(pinned.ContainsKey)
                .Distinct()
                .ToDictionary(c => c, c => pinned[c]);
            return Task.FromResult(found);
        }

        public Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default)
        {
            SingleGetCurrentCalls++;
            return Task.FromResult(current.GetValueOrDefault(concept));
        }

        public Task<IReadOnlyDictionary<ConceptId, ConceptVersionView>> GetCurrentManyAsync(
            TenantId tenant,
            IReadOnlyCollection<ConceptId> concepts,
            CancellationToken cancellationToken = default)
        {
            GetCurrentManyCalls++;
            IReadOnlyDictionary<ConceptId, ConceptVersionView> found = concepts
                .Where(current.ContainsKey)
                .Distinct()
                .ToDictionary(c => c, c => current[c]);
            return Task.FromResult(found);
        }

        public Task<ConceptVersionView?> FindBiomedicalConceptAsync(TenantId tenant, string cdiscConceptCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CodeSystemVersionView?> FindCodeSystemVersionAsync(TenantId tenant, string codeSystemUri, string version, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CodeSystemEntryView>> LookupAsync(TenantId tenant, Cadence.Kernel.Knowledge.Coding coding, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CodeSystemEntryView>> GetGroupAsync(TenantId tenant, CodeSystemVersionId version, string groupCode, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ConceptMappingView>> ResolveCodingAsync(TenantId tenant, Cadence.Kernel.Knowledge.Coding coding, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ConceptMappingView>> GetMappingsAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubCollectionInstanceRecords(
        IReadOnlyList<CollectionInstanceSummary> summaries,
        IReadOnlyList<CollectionInstanceRecord>? findQueue = null,
        IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>? headers = null) : ICollectionInstanceRecords
    {
        private readonly Queue<CollectionInstanceRecord> _records = new(findQueue ?? []);
        public int FindCalls { get; private set; }
        public int FindHeaderCalls { get; private set; }
        public int ListCalls { get; private set; }
        public ProtocolDesign? LastGoverningDesign { get; private set; }

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken) =>
            FindAsync(tenant, instance, governingDesign: null, cancellationToken);

        public Task<CollectionInstanceRecord?> FindAsync(
            TenantId tenant,
            CollectionInstanceId instance,
            ProtocolDesign? governingDesign,
            CancellationToken cancellationToken = default)
        {
            FindCalls++;
            LastGoverningDesign = governingDesign;
            return Task.FromResult(_records.Count > 0 ? _records.Dequeue() : null);
        }

        public Task<CollectionInstanceHeader?> FindHeaderAsync(
            TenantId tenant,
            CollectionInstanceId instance,
            CancellationToken cancellationToken = default)
        {
            FindHeaderCalls++;
            if (headers is not null && headers.TryGetValue(instance, out var header))
            {
                return Task.FromResult<CollectionInstanceHeader?>(header);
            }

            return Task.FromResult(_records.Count > 0 ? CollectionInstanceHeader.From(_records.Peek()) : null);
        }

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
            TenantId tenant,
            EnrollmentId enrollment,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult(summaries);
        }
    }

    private sealed class RecordingValidationService(IReadOnlyList<CollectionPreviewWarning> warnings) : ICollectionValidationService
    {
        public int PreviewByIdCalls { get; private set; }
        public int PreviewRecordCalls { get; private set; }

        public Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
            TenantId tenant,
            CollectionInstanceId formId,
            IReadOnlyDictionary<string, ObservationValue?> draftValues,
            DateOnly asOf,
            CancellationToken cancellationToken = default)
        {
            PreviewByIdCalls++;
            return Task.FromResult(warnings);
        }

        public Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
            TenantId tenant,
            CollectionInstanceRecord target,
            IReadOnlyDictionary<string, ObservationValue?> draftValues,
            DateOnly asOf,
            CancellationToken cancellationToken = default)
        {
            PreviewRecordCalls++;
            return Task.FromResult(warnings);
        }

        public Task<CollectionRulePackPreviewResult> PreviewRulePackAsync(
            TenantId tenant, StudyId studyId, LibraryItemId rulePackId, DateOnly asOf, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CommandResult<CollectionValidationResult>> ValidateStudyCollectionsAsync(
            TenantId tenant, StudyId studyId, DateOnly asOf, DateTimeOffset evaluatedAt, string idempotencyKey, string reasonForChange,
            CommandMetadata? metadata = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubCommandSender(CollectedValuesState saved, string? protocolContentToRead = null) : IKernelCommandSender
    {
        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            if (command is RecordCollectedValues)
            {
                if (protocolContentToRead is not null)
                {
                    _ = ProtocolDesignDocument.Read(protocolContentToRead);
                }

                return Task.FromResult(CommandResult<TResult>.Success((TResult)(object)saved));
            }

            throw new NotSupportedException();
        }
    }

    private sealed class StubOpenCommandSender(CollectionInstanceState state) : IKernelCommandSender
    {
        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default) =>
            command is OpenCollectionInstance
                ? Task.FromResult(CommandResult<TResult>.Success((TResult)(object)state))
                : throw new NotSupportedException();
    }

    private sealed class StubProtocolVersionReader(ProtocolVersionId versionId, ProtocolDesign design) : IProtocolVersionReader
    {
        public Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProtocolVersionSummary>>([]);

        public Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<ProtocolVersionSummary?>(null);

        public Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<SoaMatrix?>(version == versionId ? ScheduleOfActivities.Compile(design) : null);

        public Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult(version == versionId ? design : null);
    }

    private sealed class RecordingDeviationSink : IVisitWindowDeviationSink
    {
        public List<VisitWindowDeviationDetected> Events { get; } = [];

        public ValueTask NotifyAsync(VisitWindowDeviationDetected deviation, CancellationToken cancellationToken = default)
        {
            Events.Add(deviation);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubCommandContext(TenantId tenantId, IKernelUnitOfWork work) : ICommandContext
    {
        public TenantId TenantId => tenantId;
        public Actor Actor => Actor.Person("crc.user");
        public DateTimeOffset RecordedAt => At;
        public IKernelUnitOfWork Work => work;
    }

    private sealed class RecordingModuleUnitOfWork : IKernelUnitOfWork
    {
        public List<AggregateRootBase> Added { get; } = [];
        public List<object> RaisedEvents { get; } = [];

        public Task<TAggregate?> FindAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            Task.FromResult<TAggregate?>(null);

        public Task<bool> ExistsAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            Task.FromResult(false);

        public Task<TAggregate?> ReadAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            Task.FromResult<TAggregate?>(null);

        public Task<IReadOnlyList<TAggregate>> ReadAllAsync<TAggregate>(
            Expression<Func<TAggregate, bool>> filter,
            CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            Task.FromResult<IReadOnlyList<TAggregate>>([]);

        public Task LockAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            Task.CompletedTask;

        public Task LockCollectionAsync(CollectionInstanceId instance, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShareScopesAsync(IReadOnlyList<NodeRef> scopes, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task LockScopeAsync(NodeRef scope, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Add(AggregateRootBase aggregate) => Added.Add(aggregate);

        public void Raise(object domainEvent, DateTimeOffset? occurredValid = null) => RaisedEvents.Add(domainEvent);

        public void LinkInput(NodeRef input, LineageEdgeKind kind, DateTimeOffset? validFrom = null, NodeRef? target = null)
        {
        }

        public void IncludePart(NodeRef part, bool newVersion)
        {
        }

        public void LinkPart(NodeRef from, NodeRef to, LineageEdgeKind kind)
        {
        }
    }
}
