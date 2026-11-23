using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// End-to-end PostgreSQL integration tests for pre-commit form preview (<c>POST /api/v1/execution/forms/{formId}/preview</c>),
/// post-commit outbox collection edit checks (<c>EvaluateEditChecksOnCollectionChanged</c>), idempotent replay, automatic
/// discrepancy query closure on resolution, and <c>CollectionItem.Admits</c> enforcement on <c>CorrectObservation</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionOutboxEditChecksPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Crc = "crc@edit-checks.test";
    private const string Investigator = "pi@edit-checks.test";
    private const string Monitor = "cra@edit-checks.test";
    private const string DataManager = "dm@edit-checks.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    [Fact(DisplayName = "PreviewSaveReplayAndClear_PreviewsWithoutWrites_OpensIdempotentAutoQuery_AndClosesWhenCleared")]
    [KernelRule("K-RULE-3")]
    [KernelRule("K-FIND-1")]
    public async Task PreviewSaveReplayAndClearPreviewsWithoutWritesOpensIdempotentAutoQueryAndClosesWhenCleared()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, study, _, form) = await SeedStudyWithPinnedRulePackAsync(ct);
        await RelayAsync(ct);

        using var crc = Client(code, Crc);
        using var pi = Client(code, Investigator);
        using var monitor = Client(code, Monitor);

        // Site coordinator cannot sign; Principal Investigator can sign.
        var crcFormView = await SendAsync<FormResponse>(crc, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}", null, null, HttpStatusCode.OK, ct);
        crcFormView.CanSign.Should().BeFalse();
        var piFormView = await SendAsync<FormResponse>(pi, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}", null, null, HttpStatusCode.OK, ct);
        piFormView.CanSign.Should().BeTrue();

        int auditsBeforePreview;
        int runsBeforePreview;
        await using (var db = fixture.CreateAdminContext())
        {
            auditsBeforePreview = await db.ProvenanceActivities.CountAsync(a => a.TenantId == tenant, ct);
            runsBeforePreview = await db.Set<ValidationRun>().CountAsync(r => r.TenantId == tenant, ct);
        }

        // 1. Pre-commit preview returns item-level warnings with zero writes or audit entries.
        var preview = await SendAsync<FormPreviewResponse>(
            crc,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{form:D}/preview",
            reason: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 240m, unit = "mm[Hg]" } },
                    new { item_id = Diastolic, value = new { type = "quantity", value = 85m, unit = "mm[Hg]" } },
                },
            },
            HttpStatusCode.OK,
            ct);
        preview.Warnings.Should().ContainSingle(w => w.RuleId == "VS_SBP_HIGH" && w.ItemId == Systolic);

        await using (var db = fixture.CreateAdminContext())
        {
            (await db.ProvenanceActivities.CountAsync(a => a.TenantId == tenant, ct)).Should().Be(auditsBeforePreview);
            (await db.Set<ValidationRun>().CountAsync(r => r.TenantId == tenant, ct)).Should().Be(runsBeforePreview);
            (await db.Observations.CountAsync(o => o.TenantId == tenant, ct)).Should().Be(0);
        }

        // 2. Saving out-of-range values commits immediately (K-RULE-3).
        var saved = await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{form:D}/values",
            SaveReason,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 240m, unit = "mm[Hg]" }, expected_revision = 0 },
                    new { item_id = Diastolic, value = new { type = "quantity", value = 85m, unit = "mm[Hg]" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        saved.Values.Should().HaveCount(2);
        saved.Form.Should().NotBeNull();
        saved.Form!.FormId.Should().Be(form);
        saved.Form.Version.Should().Be(saved.Version);
        saved.Form.Complete.Should().BeTrue();
        saved.Form.CanSign.Should().BeFalse();
        var sbpObs = saved.Values.Single(v => v.ItemId == Systolic).ObservationId;

        // Outbox consumer evaluates study collections and opens one automated FormQuery.
        await RelayAsync(ct);

        var queriesAfterViolation = await SendAsync<FormQueryResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        var autoQuery = queriesAfterViolation.Should().ContainSingle().Subject;
        autoQuery.Status.Should().Be("open");
        autoQuery.ItemId.Should().Be(Systolic);
        autoQuery.RaisedBy.Should().Be($"svc:{EvaluateEditChecksOnCollectionChanged.Name}");

        // 3. Replaying outbox messages is idempotent: no duplicate validation runs or queries.
        await fixture.ExecuteAdminAsync(
            $"UPDATE kernel.outbox_messages SET dispatched_at = NULL WHERE tenant_id = '{tenant.Value:D}'", ct);
        await RelayAsync(ct);

        var queriesAfterReplay = await SendAsync<FormQueryResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        queriesAfterReplay.Should().ContainSingle(q => q.QueryId == autoQuery.QueryId);

        // Also raise a human monitor query on the same observation to ensure auto-closure leaves human queries open.
        var humanQuery = await SendAsync<FormQueryResponse>(
            monitor,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{form:D}/queries",
            "Manual monitor check",
            new { observation_id = sbpObs, revision = 1, text = "Confirm cuff size used." },
            HttpStatusCode.Created,
            ct);

        // 4. Updating value into valid range resolves the finding and closes only the automated query.
        await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{form:D}/values",
            "Corrected transcription error",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 122m, unit = "mm[Hg]" }, expected_revision = 1 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayAsync(ct);

        var queriesAfterClear = await SendAsync<FormQueryResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        queriesAfterClear.Single(q => q.QueryId == autoQuery.QueryId).Should().Match<FormQueryResponse>(q =>
            q.Status == "closed" && q.Origin == "system");
        queriesAfterClear.Single(q => q.QueryId == humanQuery.QueryId).Should().Match<FormQueryResponse>(q =>
            q.Status == "answered" && q.Origin == "manual" && q.OwnerRole == "monitor" && q.LastAnsweredBy == Crc);

        var findingsResponse = await SendAsync<JsonElement>(
            monitor, HttpMethod.Get, $"/api/v1/kernel/studies/study:{study.Value:D}/validation/findings", null, null, HttpStatusCode.OK, ct);
        findingsResponse.GetProperty("findings").EnumerateArray()
            .Should().ContainSingle(f => f.GetProperty("rule_id").GetString() == "VS_SBP_HIGH" && f.GetProperty("status").GetString() == "Resolved");
    }

    [Fact(DisplayName = "CorrectObservation_OnNotAskedQuantityItem_ExposesExpectedUnitAndRejectsTextValue")]
    public async Task CorrectObservationOnNotAskedQuantityItemExposesExpectedUnitAndRejectsTextValue()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, _, _, enrollment, form) = await SeedStudyWithPinnedRulePackAsync(ct);
        await RelayAsync(ct);

        using var crc = Client(code, Crc);
        using var pi = Client(code, Investigator);
        using var monitor = Client(code, Monitor);

        // Monitor raises a query on blank Systolic item -> stores blank-item FormQuery with ObservationId = null.
        var raised = await SendAsync<FormQueryResponse>(
            monitor,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{form:D}/queries",
            "Blank required item",
            new { item_id = Systolic, text = "Systolic BP is missing." },
            HttpStatusCode.Created,
            ct);
        raised.ObservationId.Should().BeNull();
        raised.Revision.Should().Be(0);

        // Coordinator records a reasoned missing value (NotDone) at expected_revision = 0 -> creates observation at revision 1.
        var recordedMissing = await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{form:D}/values",
            "Systolic cuff unavailable initially",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "missing", reason = "NotDone" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        var systolicObsId = recordedMissing.Values.Single(v => v.ItemId == Systolic).ObservationId;

        var obsDetail = await SendAsync<JsonElement>(
            pi, HttpMethod.Get, $"/api/v1/kernel/observations/observation:{systolicObsId:D}", null, null, HttpStatusCode.OK, ct);
        obsDetail.GetProperty("expected_value_kind").GetString().Should().Be("Quantity");
        obsDetail.GetProperty("expected_unit").GetString().Should().Be("mm[Hg]");
        obsDetail.GetProperty("item_id").GetString().Should().Be(Systolic);

        // Attempting to correct missing-value quantity item with plain text is rejected with 422 K.Collection.ValuesInvalid.
        using (var rejected = await pi.SendAsync(
            Request(
                HttpMethod.Post,
                $"/api/v1/kernel/observations/observation:{systolicObsId:D}/corrections",
                "Attempted text correction",
                new
                {
                    enrollment_ref = $"enrollment:{enrollment.Value:D}",
                    value = new { type = "text", value = "one twenty" },
                    expected_version = 1,
                }),
            ct))
        {
            rejected.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(ct));
            problem.RootElement.GetProperty("code").GetString().Should().Be(CollectionErrors.ValuesInvalid.Code);
        }

        // Correcting with a valid quantity in mm[Hg] succeeds and auto-answers the open manual query on relay.
        var corrected = await SendAsync<JsonElement>(
            pi,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{systolicObsId:D}/corrections",
            "Entered verified systolic BP",
            new
            {
                enrollment_ref = $"enrollment:{enrollment.Value:D}",
                value = new { type = "quantity", value = 124m, unit = "mm[Hg]" },
                expected_version = 1,
            },
            HttpStatusCode.OK,
            ct);
        corrected.GetProperty("revision").GetInt32().Should().Be(2);
        await RelayAsync(ct);

        var queriesAfterStandaloneCorrection = await SendAsync<FormQueryResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        queriesAfterStandaloneCorrection.Single(q => q.QueryId == raised.QueryId).Should().Match<FormQueryResponse>(q =>
            q.Status == "answered" && q.Origin == "manual" && q.OwnerRole == "monitor" && q.LastAnsweredBy == Investigator);

        // Editing the form later preserves the quantity unit without errors.
        var editedForm = await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{form:D}/values",
            "Follow-up cuff check",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 128m, unit = "mm[Hg]" }, expected_revision = 2 },
                },
            },
            HttpStatusCode.OK,
            ct);
        editedForm.Values.Should().ContainSingle(v => v.ItemId == Systolic && v.Revision == 3);
        var reloadedForm = await SendAsync<FormResponse>(
            crc, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}", null, null, HttpStatusCode.OK, ct);
        var sbpAfterEdit = reloadedForm.Items.Single(v => v.ItemId == Systolic);
        sbpAfterEdit.Revision.Should().Be(3);
        (sbpAfterEdit.Value as QuantityValue)!.Unit.Should().Be("mm[Hg]");
    }

    [Fact(DisplayName = "AutoEditCheck_OnBlankItemOfSignedFrozenForm_RaisesBlankQueryWithoutSupersedingSignature")]
    public async Task AutoEditCheckOnBlankItemOfSignedFrozenFormRaisesBlankQueryWithoutSupersedingSignature()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, study, enrollment, form) = await SeedStudyWithPinnedRulePackAsync(ct);
        await RelayAsync(ct);

        using var crc = Client(code, Crc);
        using var pi = Client(code, Investigator);
        using var monitor = Client(code, Monitor);
        using var dm = Client(code, DataManager);

        // Pin a study rule requiring optional Comment when Systolic > 180 (Comment is first variable -> Finding targets blank Comment).
        var commentVar = new VariableExpression(Comment);
        var sbpVar = new VariableExpression(Systolic);
        var blankCommentPack = new RulePack(
            RuleAuthority.Study,
            "Blank Comment Check",
            [
                new Rule(
                    "VS_COMMENT_REQUIRED_HIGH_SBP",
                    1,
                    RuleAuthority.Study,
                    new SourceCitation("Test Validation Plan", "Section 2", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, SpecId),
                    ValidationSeverity.Warning,
                    new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsEmpty, [commentVar]),
                            new FunctionExpression(RuleFunction.IsNotEmpty, [sbpVar]),
                            new ComparisonExpression(ComparisonOperator.Greater, sbpVar, new LiteralExpression(RuleValue.FromNumber(180m))),
                        ]),
                    "Cuff comment is required when systolic BP exceeds 180 mm[Hg].",
                    "Record cuff posture and rest note.",
                    EffectivePeriod.Always),
            ]);
        var blankPackId = LibraryItemId.New();
        var drafted = await _scenario.Protocol.OkAsync(
            new DraftLibraryItem(tenant, blankPackId, LibraryItemKind.RulePack, "vitals-comment-check", blankCommentPack.Title, RulePackJson.ToJson(blankCommentPack), [], KernelProtocolScenario.Key(), "Drafted blank comment check"),
            ct);
        var attestation = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, blankPackId.Value, drafted.ContentHash)),
            ct);
        await _scenario.Protocol.OkAsync(
            new ApproveLibraryItem(tenant, blankPackId, attestation, KernelProtocolScenario.Key(), "Approved blank comment check"),
            ct);

        // Save required Systolic (190) and Diastolic (82) with Comment left blank, sign the form as Investigator, and freeze the form!
        await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{form:D}/values",
            SaveReason,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 190m, unit = "mm[Hg]" }, expected_revision = 0 },
                    new { item_id = Diastolic, value = new { type = "quantity", value = 82m, unit = "mm[Hg]" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayAsync(ct);

        var signedBefore = await SendAsync<FormResponse>(pi, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}", null, null, HttpStatusCode.OK, ct);
        using (var signReq = Request(
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{form:D}/signatures",
            "I approve these vital signs",
            new { meaning = "approved", content_hash = signedBefore.ContentHash }))
        {
            signReq.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(Investigator, FormSignatureEndpoints.SignAction(form), FormSignatureEndpoints.SignSemanticAction));
            using var signRes = await pi.SendAsync(signReq, ct);
            signRes.StatusCode.Should().Be(HttpStatusCode.Created, await signRes.Content.ReadAsStringAsync(ct));
        }

        await _scenario.Protocol.OkAsync(
            await _scenario.Observations.LockAsync(
                tenant, study, NodeRef.For(CollectionInstanceId.FromGuid(form)), ct, LockKind.Frozen),
            ct);

        // Pin the blank-comment rule pack and save a sibling AE form on the same enrollment -> study-wide edit checks evaluate the signed Frozen vitals form!
        await SendAsync<JsonElement>(
            dm,
            HttpMethod.Put,
            $"/api/v1/kernel/studies/study:{study.Value:D}/standards",
            "Pin blank comment rule pack",
            new { rule_pack_refs = new[] { $"library_item:{blankPackId.Value:D}" } },
            HttpStatusCode.OK,
            ct);

        var openedAe = await SendAsync<JsonElement>(
            crc,
            HttpMethod.Post,
            FormEndpoints.RoutePrefix,
            "Open sibling AE form",
            new
            {
                enrollment_id = enrollment.Value,
                collection_spec_id = "cs.ae",
                planned_activity_id = VisitId,
                collected_at = SystemClock.Instance.UtcNow.AddMinutes(-5),
            },
            HttpStatusCode.Created,
            ct);
        var aeFormId = openedAe.GetProperty("form_id").GetGuid();
        await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{aeFormId:D}/values",
            "Save AE note",
            new
            {
                values = new object[]
                {
                    new { item_id = "item.ae_term", value = new { type = "text", value = "Elevated BP" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayAsync(ct);

        // Automated edit check raised a blank-item FormQuery on Comment without mutating the Frozen form or superseding its Part 11 signature!
        var vitalsQueries = await SendAsync<FormQueryResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        vitalsQueries.Should().ContainSingle().Which.Should().Match<FormQueryResponse>(q =>
            q.ItemId == Comment && q.ObservationId == null && q.Revision == 0 && q.Status == "open");

        var signedAfter = await SendAsync<FormResponse>(pi, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}", null, null, HttpStatusCode.OK, ct);
        signedAfter.ContentHash.Should().Be(signedBefore.ContentHash);
        signedAfter.Signatures.Should().ContainSingle().Which.Superseded.Should().BeFalse();
        signedAfter.Items.Single(i => i.ItemId == Comment).ObservationId.Should().BeNull();
    }

    [Fact(DisplayName = "OdmImportApprovePinAndObservationCorrection_CompilesRulePack_AndEvaluatesOnObservationCorrected")]
    public async Task OdmImportApprovePinAndObservationCorrectionCompilesRulePackAndEvaluatesOnObservationCorrected()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, _, study, enrollment, form) = await SeedStudyWithPinnedRulePackAsync(ct);
        await RelayAsync(ct);

        using var crc = Client(code, Crc);
        using var pi = Client(code, Investigator);
        using var monitor = Client(code, Monitor);
        using var dm = Client(code, DataManager);

        const string odmXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" ODMVersion="1.3.2" FileType="Snapshot" FileOID="ODM-EC-1" CreationDateTime="2026-01-01T00:00:00Z">
              <Study OID="S.EC">
                <GlobalVariables>
                  <StudyName>ODM Edit Check Import</StudyName>
                  <StudyDescription>ODM rule compilation</StudyDescription>
                  <ProtocolName>ODM-EC</ProtocolName>
                </GlobalVariables>
                <MetaDataVersion OID="MDV.1" Name="v1">
                  <Protocol>
                    <StudyEventRef StudyEventOID="SE.W12" OrderNumber="1" Mandatory="Yes" />
                  </Protocol>
                  <StudyEventDef OID="SE.W12" Name="Week 12" Repeating="No" Type="Scheduled">
                    <FormRef FormOID="cs.vitals" OrderNumber="1" Mandatory="Yes" />
                    <FormRef FormOID="cs.ae" OrderNumber="2" Mandatory="No" />
                  </StudyEventDef>
                  <FormDef OID="cs.vitals" Name="Vital Signs" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes" />
                  </FormDef>
                  <FormDef OID="cs.ae" Name="Adverse Events" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.AE" OrderNumber="1" Mandatory="No" />
                  </FormDef>
                  <ItemGroupDef OID="IG.VS" Name="Vital Signs" Repeating="No">
                    <ItemRef ItemOID="item.sbp" OrderNumber="1" Mandatory="Yes" />
                    <ItemRef ItemOID="item.dbp" OrderNumber="2" Mandatory="Yes" CollectionExceptionConditionOID="COND.DBP_EXEMPT" />
                  </ItemGroupDef>
                  <ItemGroupDef OID="IG.AE" Name="Adverse Events" Repeating="No">
                    <ItemRef ItemOID="item.ae_term" OrderNumber="1" Mandatory="No" />
                  </ItemGroupDef>
                  <ItemDef OID="item.sbp" Name="Systolic BP" DataType="float">
                    <RangeCheck Comparator="LE" SoftHard="Hard">
                      <CheckValue>210</CheckValue>
                      <ErrorMessage><TranslatedText xml:lang="en">Systolic BP must not exceed 210 mm[Hg].</TranslatedText></ErrorMessage>
                    </RangeCheck>
                  </ItemDef>
                  <ItemDef OID="item.dbp" Name="Diastolic BP" DataType="float" />
                  <ItemDef OID="item.ae_term" Name="Adverse Event Term" DataType="text" />
                  <ConditionDef OID="COND.DBP_EXEMPT" Name="Diastolic exempt when SBP below 60">
                    <Description><TranslatedText xml:lang="en">Diastolic BP is required unless SBP &lt; 60.</TranslatedText></Description>
                    <FormalExpression Context="Cadence">item.sbp &lt; 60</FormalExpression>
                  </ConditionDef>
                  <ConditionDef OID="COND.BP_NEEDS_AE" Name="Elevated SBP requires AE log">
                    <Description><TranslatedText xml:lang="en">Systolic BP above 180 requires an Adverse Event entry.</TranslatedText></Description>
                    <FormalExpression Context="XPath">item.sbp &gt; 180 and cs.ae/IG.AE/item.ae_term = ''</FormalExpression>
                  </ConditionDef>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;

        var imported = await SendAsync<JsonElement>(
            dm,
            HttpMethod.Post,
            $"/api/v1/kernel/studies/study:{study.Value:D}/protocol-imports",
            "Import ODM with RangeCheck and ConditionDef",
            new { format = "odm_xml", content = odmXml, replace_forms = true },
            HttpStatusCode.Created,
            ct);
        var packRef = imported.GetProperty("imported_rule_pack").GetProperty("ref").GetString()!;
        imported.GetProperty("imported_rule_pack").GetProperty("rule_count").GetInt32().Should().Be(3);

        // Save initial in-range values (SBP = 130, DBP = 80).
        var saved = await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{form:D}/values",
            SaveReason,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 130m, unit = "mm[Hg]" }, expected_revision = 0 },
                    new { item_id = Diastolic, value = new { type = "quantity", value = 80m, unit = "mm[Hg]" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        var sbpObs = saved.Values.Single(v => v.ItemId == Systolic).ObservationId;
        await RelayAsync(ct);

        // Approve the imported rule pack with a single-use step-up signature token and pin it on the study.
        var approvePath = $"/api/v1/kernel/library-items/{packRef}/approve";
        var sigToken = TestSecurityContext.CreateSigToken(
            DataManager,
            Cadence.Kernel.Api.KernelValidationEndpoints.ApproveLibraryItemAction(packRef),
            Cadence.Kernel.Api.KernelValidationEndpoints.ApproveLibraryItemSemanticAction);
        using (var approveReq = Request(HttpMethod.Post, approvePath, "Approve imported ODM edit checks", new { }))
        {
            approveReq.Headers.Add(GatewayHeaders.SigToken, sigToken);
            using var approveRes = await dm.SendAsync(approveReq, ct);
            approveRes.StatusCode.Should().Be(HttpStatusCode.OK, await approveRes.Content.ReadAsStringAsync(ct));
        }

        await SendAsync<JsonElement>(
            dm,
            HttpMethod.Put,
            $"/api/v1/kernel/studies/study:{study.Value:D}/standards",
            "Pin imported ODM rule pack",
            new { rule_pack_refs = new[] { packRef } },
            HttpStatusCode.OK,
            ct);

        // Correct observation to 195 mm[Hg] (> 180) via /observations/{ref}/corrections -> ObservationCorrected triggers outbox edit checks!
        await SendAsync<JsonElement>(
            pi,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{sbpObs:D}/corrections",
            "Source worksheet showed 195",
            new
            {
                enrollment_ref = $"enrollment:{enrollment.Value:D}",
                value = new { type = "quantity", value = 195m, unit = "mm[Hg]" },
                expected_version = 1,
            },
            HttpStatusCode.OK,
            ct);
        await RelayAsync(ct);

        var queriesAfterCorrection = await SendAsync<FormQueryResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        var autoQuery = queriesAfterCorrection.Should().ContainSingle(q => q.Status == "open").Subject;
        autoQuery.ItemId.Should().Be(Systolic);

        // Previewing the rule pack over study collection records reflects the active violation without mutating state.
        var packPreview = await SendAsync<JsonElement>(
            dm,
            HttpMethod.Post,
            $"/api/v1/kernel/studies/study:{study.Value:D}/rule-packs/{packRef}/preview",
            "Preview rule pack",
            new { },
            HttpStatusCode.OK,
            ct);
        packPreview.GetProperty("violations").GetArrayLength().Should().Be(1);

        // Saving the sibling cs.ae form with an AE term resolves the cross-form finding on cs.vitals and closes autoQuery!
        var openedAe = await SendAsync<JsonElement>(
            crc,
            HttpMethod.Post,
            FormEndpoints.RoutePrefix,
            "Open AE form for elevated BP",
            new
            {
                enrollment_id = enrollment.Value,
                collection_spec_id = "cs.ae",
                planned_activity_id = VisitId,
                collected_at = SystemClock.Instance.UtcNow.AddMinutes(-5),
            },
            HttpStatusCode.Created,
            ct);
        var aeFormId = openedAe.GetProperty("form_id").GetGuid();
        await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{aeFormId:D}/values",
            "Recorded hypertensive urgency AE",
            new
            {
                values = new object[]
                {
                    new { item_id = "item.ae_term", value = new { type = "text", value = "Transient hypertension" }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayAsync(ct);

        var queriesAfterResolution = await SendAsync<FormQueryResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        queriesAfterResolution.Single(q => q.QueryId == autoQuery.QueryId).Status.Should().Be("closed");
    }

    private async Task<(string Code, TenantId Tenant, StudyId Study, EnrollmentId Enrollment, Guid Form)> SeedStudyWithPinnedRulePackAsync(CancellationToken ct)
    {
        fixture.Clock.Set(SystemClock.Instance.UtcNow.AddHours(-2));
        var code = $"T-EC-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Outbox edit checks tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        await EffectiveVitalsAndAeVersionAsync(tenant, study, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);

        foreach (var (actor, capabilities, scope) in new (Actor, string[], AuthorityScope)[]
        {
            (Actor.Person(Crc), ["collection.open", "collection.record", "execution.query.answer"], AuthorityScope.StudySite(studySite)),
            (Actor.Person(Investigator), ["collection.record", "collection.sign", "observation.correct", "execution.query.answer"], AuthorityScope.StudySite(studySite)),
            (Actor.Person(Monitor), ["execution.query.raise", "execution.query.manage"], AuthorityScope.Study(study)),
            (Actor.Person(DataManager), ["concept.define", "protocol.draft", "protocol.amend", "protocol.revise_draft", "library_item.draft", "library_item.approve", "attestation.sign", "standard.register", "standard.approve", "study.declare_standards"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount("execution-forms"), ["execution.form.open"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount("execution-queries"), ["observation.query", "observation.query.answer", "assertion.retract"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount(EvaluateEditChecksOnCollectionChanged.Name), ["validation_run.record", "execution.query.raise", "execution.query.manage"], AuthorityScope.Tenant()),
        })
        {
            foreach (var capability in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), actor, capability, null, scope, _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Granted for test"),
                    ct);
            }
        }

        // Draft, approve, and pin a RulePack checking Systolic > 220 on cs.vitals.
        var sbpVar = new VariableExpression(Systolic);
        var pack = new RulePack(
            RuleAuthority.Study,
            "Vital Signs Edit Checks",
            [
                new Rule(
                    "VS_SBP_HIGH",
                    1,
                    RuleAuthority.Study,
                    new SourceCitation("Test Validation Plan", "Section 1", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, SpecId),
                    ValidationSeverity.Warning,
                    new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsNotEmpty, [sbpVar]),
                            new ComparisonExpression(ComparisonOperator.Greater, sbpVar, new LiteralExpression(RuleValue.FromNumber(220m))),
                        ]),
                    "Systolic blood pressure exceeds 220 mm[Hg].",
                    "Verify cuff reading against source worksheet.",
                    EffectivePeriod.Always),
            ]);
        var packJson = RulePackJson.ToJson(pack);
        var itemId = LibraryItemId.New();
        var drafted = await _scenario.Protocol.OkAsync(
            new DraftLibraryItem(tenant, itemId, LibraryItemKind.RulePack, "vitals-checks", pack.Title, packJson, [], KernelProtocolScenario.Key(), "Drafted rule pack"),
            ct);
        var attestation = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, itemId.Value, drafted.ContentHash)),
            ct);
        await _scenario.Protocol.OkAsync(
            new ApproveLibraryItem(tenant, itemId, attestation, KernelProtocolScenario.Key(), "Approved rule pack"),
            ct);
        var standardId = StandardVersionId.New();
        var standardPkg = new StandardPackage(
            "CDASHIG",
            "2.3",
            "CDISC CDASHIG v2.3",
            "CDISC",
            "/mdr/cdashig/2-3",
            new DateOnly(2023, 9, 29),
            [
                new DatasetDefinitionData(
                    "VS",
                    "Vital Signs",
                    "Findings",
                    "One record per measurement",
                    1,
                    [
                        new VariableDefinitionData(
                            "VSORRES",
                            "Result in Original Units",
                            1,
                            StandardDataType.Num,
                            "Result Qualifier",
                            VariableCore.Expected,
                            [],
                            [],
                            null,
                            null),
                    ]),
            ],
            [],
            []);
        await _scenario.Protocol.OkAsync(
            new RegisterStandardVersion(tenant, standardId, standardPkg, null, KernelProtocolScenario.Key(), "Registered CDASHIG"),
            ct);
        var standardSummary = (await fixture.Standards.FindStandardVersionAsync(tenant, standardId, ct))!;
        var stdAttestation = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.StandardVersion, standardId.Value, standardSummary.ContentHash)),
            ct);
        await _scenario.Protocol.OkAsync(
            new ApproveStandardVersion(tenant, standardId, stdAttestation, KernelProtocolScenario.Key(), "Approved CDASHIG"),
            ct);

        await _scenario.Protocol.OkAsync(
            new DeclareStudyStandards(tenant, study, StudyStandardsProfileId.New(), [standardId], [], [itemId], KernelProtocolScenario.Key(), "Pinned rule pack"),
            ct);

        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
        var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        return (code, tenant, study, enrollment, opened.CollectionInstanceId.Value);
    }

    private async Task EffectiveVitalsAndAeVersionAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var systolic = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Systolic BP", "Systolic BP.", KernelProtocolScenario.Key(), "Defined"), ct);
        var diastolic = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Diastolic BP", "Diastolic BP.", KernelProtocolScenario.Key(), "Defined"), ct);
        var aeTerm = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.DataElement, "AE Term", "Adverse event term.", KernelProtocolScenario.Key(), "Defined"), ct);

        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Cadence.Kernel.Protocol.Design.Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new Cadence.Kernel.Protocol.Design.PlannedActivity(
                    VisitId,
                    "Week 12 visit",
                    "ep.treatment",
                    [Cadence.Kernel.Protocol.Design.ActivityLocation.Site],
                    [SpecId, "cs.ae"],
                    new Cadence.Kernel.Protocol.Design.ActivityTiming(84, 3, 3)),
            ],
            CollectionSpecs =
            [
                new Cadence.Kernel.Protocol.Design.CollectionSpec(
                    SpecId,
                    "Vital signs",
                    [
                        new Cadence.Kernel.Protocol.Design.CollectionItem(Systolic, systolic, "mm[Hg]", Required: true),
                        new Cadence.Kernel.Protocol.Design.CollectionItem(Diastolic, diastolic, "mm[Hg]", Required: true),
                        new Cadence.Kernel.Protocol.Design.CollectionItem(Comment, systolic, ValueKind: ObservationValueKind.Text),
                    ]),
                new Cadence.Kernel.Protocol.Design.CollectionSpec(
                    "cs.ae",
                    "Adverse Events",
                    [
                        new Cadence.Kernel.Protocol.Design.CollectionItem("item.ae_term", aeTerm, ValueKind: ObservationValueKind.Text),
                    ]),
            ],
        };

        var id = ProtocolVersionId.New();
        var draft = await _scenario.Protocol.CreateDraftAsync(tenant, study, id, ct, Cadence.Kernel.Protocol.Design.ProtocolDesignDocument.Write(design));
        draft.IsSuccess.Should().BeTrue(draft.Error?.Message);
        var inReview = await _scenario.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), "Ready"), ct);
        var signature = await _scenario.Protocol.OkAsync(_scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.Protocol.OkAsync(new ApproveProtocolVersion(tenant, id, signature, KernelProtocolScenario.Key(), "Approved"), ct);

        var effectivity = EffectivityId.New();
        await _scenario.Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivity, id, Cadence.Kernel.Execution.EffectivityScope.Study(), _scenario.Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethicsBytes = System.Text.Encoding.UTF8.GetBytes($"ethics-{Guid.NewGuid():N}");
        var ethicsVersion = ArtifactVersionId.New();
        var ethicsContent = new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://evidence/ethics.pdf");
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(tenant, ArtifactId.New(), ethicsVersion, ArtifactKind.EthicsApproval, study, "Ethics approval", ethicsContent, KernelProtocolScenario.Key(), "Filed"), ct);
        await _scenario.Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivity, Cadence.Kernel.Execution.EffectivityStatus.Effective, _scenario.Tick(), [ethicsVersion], KernelProtocolScenario.Key(), "Effective"), ct);
    }

    private async Task RelayAsync(CancellationToken ct)
    {
        var now = SystemClock.Instance.UtcNow;
        fixture.Clock.Set(now);
        for (var pass = 0; pass < 40; pass++)
        {
            var relayed = await fixture.OutboxRelay.RelayBatchAsync(10_000, ct);
            await using var db = fixture.CreateAdminContext();
            var errors = await db.OutboxMessages
                .Where(m => m.LastError != null)
                .Select(m => m.LastError!)
                .ToListAsync(ct);
            errors.Should().BeEmpty(string.Join(" | ", errors));

            var remaining = await db.OutboxMessages
                .AnyAsync(m => m.DispatchedAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now), ct);
            if (!remaining && !relayed.MoreDue)
            {
                break;
            }

            await Task.Delay(25, ct);
        }
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                services.AddDeclaredKernelErrorsCheck();
                services.RemoveAll<KernelBackgroundOptions>();
                services.AddSingleton(new KernelBackgroundOptions { RelayInterval = TimeSpan.FromHours(1) });
            }));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(method, path, reason, body), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? reason, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
