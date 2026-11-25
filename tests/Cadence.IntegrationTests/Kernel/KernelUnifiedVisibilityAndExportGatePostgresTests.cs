using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Cadence.Ddf;
using Cadence.IntegrationTests.Execution;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Modules.Intelligence.Mcp;
using Cadence.Modules.Intelligence.Tools;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// PostgreSQL regression tests for unified <c>enrollment.unblind</c> and site-visibility masking across kernel
/// observations, Execution eCRF forms/previews/queries/auto-queries, validation findings and RulePack previews,
/// Intelligence MCP tools (<c>get_node</c>, <c>explain</c>), bitemporal <c>as_of(valid_at, known_at)</c> export
/// reconstruction, blinded side-channel elimination, and <c>[BaselineKernelCommand]</c> export capability gating.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelUnifiedVisibilityAndExportGatePostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string TreatmentItem = "item.treatment_arm";
    private const string BlindedArmText = "Investigational Arm Zeta";
    private const string BlindedQueryPrompt = "Confirm bottle code for Investigational Arm Zeta";
    private const string BlindedQueryAnswer = "Verified vial label Investigational Arm Zeta";
    private const string BlindedSignReason = "Signed confidential Investigational Arm Zeta record";

    private const string SiteACrc = "crc.site-a@unified-vis.test";
    private const string TenantMonitor = "cra.tenant@unified-vis.test";
    private const string BlindedStat = "stat.blinded@unified-vis.test";
    private const string UnblindedPharm = "pharm.unblinded@unified-vis.test";
    private const string ReadOnlyReviewer = "reviewer.no-export@unified-vis.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// Even a caller holding a tenant-wide grant without <c>enrollment.unblind</c> is refused blinded
    /// <c>GET /api/v1/kernel/observations/{ref}</c> with 403, receives masked form values/revisions/signatures/assertions
    /// on <c>GET /api/v1/execution/forms/{formId}</c>, receives masked warnings on <c>POST /forms/{formId}/preview</c>,
    /// receives masked query text/messages across query endpoints, and <c>EvaluateEditChecksOnCollectionChanged</c>
    /// suppresses automated discrepancy queries on blinded items while an unblinded caller sees full clinical values.
    /// </summary>
    [Fact(DisplayName = "ObservationExecutionFormsPreviewsQueriesAndAutoQueries_EnforceEnrollmentUnblindEvenForTenantCaller")]
    [KernelRule("K-AUTH-4")]
    [KernelRule("K-RULE-3")]
    [GxPRequirement("PRD-MDR-006")]
    public async Task ObservationExecutionFormsPreviewsQueriesAndAutoQueriesEnforceEnrollmentUnblindEvenForTenantCaller()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedWorldAsync(ct);
        await RelayAsync(ct);

        using var crc = Client(world.Code, SiteACrc);
        using var tenantMonitor = Client(world.Code, TenantMonitor);
        using var unblindedPharm = Client(world.Code, UnblindedPharm);

        // Save normal Systolic (122 mm[Hg], in range) AND blinded TreatmentItem = BlindedArmText (triggers ARM_CHECK).
        var saved = await SendJsonAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/values",
            "Initial screening and kit entry",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 122m, unit = "mm[Hg]" }, expected_revision = 0 },
                    new { item_id = TreatmentItem, value = new { type = "text", value = BlindedArmText }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        saved.Values.Single(v => v.ItemId == TreatmentItem).ObservationId.Should().Be(Guid.Empty);
        var pharmFormAfterSave = await SendJsonAsync<JsonElement>(
            unblindedPharm, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}", null, null, HttpStatusCode.OK, ct);
        var blindedObsId = new ObservationId(pharmFormAfterSave.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("item_id").GetString() == TreatmentItem)
            .GetProperty("observation_id").GetGuid());
        var blindedObsRef = NodeRef.For(blindedObsId).ToString();

        // Outbox consumer evaluates ARM_CHECK: records the finding, yet opens ZERO automated FormQuery on blinded item!
        await RelayAsync(ct);

        var autoQueries = await SendJsonAsync<FormQueryResponse[]>(
            unblindedPharm, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/queries", null, null, HttpStatusCode.OK, ct);
        autoQueries.Should().BeEmpty("automated queries must never open on blinded randomization items");

        // 1. Direct observation read: TenantMonitor (Tenant() scope, no enrollment.unblind) gets 403; UnblindedPharm gets 200.
        using (var refusedObs = await tenantMonitor.GetAsync(new Uri($"/api/v1/kernel/observations/{blindedObsRef}", UriKind.Relative), ct))
        {
            refusedObs.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var unblindedObsText = await GetRawAsync(unblindedPharm, $"/api/v1/kernel/observations/{blindedObsRef}", HttpStatusCode.OK, ct);
        unblindedObsText.Should().Contain(BlindedArmText);

        // 2. Unblinded pharmacist signs the form and raises + coordinator answers a manual query on the blinded item.
        var pharmFormBeforeSign = await SendJsonAsync<JsonElement>(
            unblindedPharm, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}", null, null, HttpStatusCode.OK, ct);
        var formHash = pharmFormBeforeSign.GetProperty("content_hash").GetString()!;

        using (var signReq = new HttpRequestMessage(HttpMethod.Post, new Uri($"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/signatures", UriKind.Relative)))
        {
            signReq.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
            signReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(BlindedSignReason));
            signReq.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(UnblindedPharm, FormSignatureEndpoints.SignAction(world.FormA.Value), FormSignatureEndpoints.SignSemanticAction));
            signReq.Content = JsonContent.Create(new { meaning = "approved", content_hash = formHash });
            using var signRes = await unblindedPharm.SendAsync(signReq, ct);
            signRes.StatusCode.Should().Be(HttpStatusCode.Created, await signRes.Content.ReadAsStringAsync(ct));
        }

        var raisedQuery = await SendJsonAsync<FormQueryResponse>(
            unblindedPharm,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/queries",
            "Unblinded pharmacy check",
            new { observation_id = blindedObsId.Value, revision = 1, text = BlindedQueryPrompt },
            HttpStatusCode.Created,
            ct);
        await RelayAsync(ct);

        await SendJsonAsync<FormQueryResponse>(
            unblindedPharm,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/queries/{raisedQuery.QueryId:D}/answer",
            "Unblinded pharmacy confirmation",
            new { text = BlindedQueryAnswer },
            HttpStatusCode.OK,
            ct);
        await RelayAsync(ct);

        // 3. GET /api/v1/execution/forms/{formId}: TenantMonitor gets NullFlavor.Masked, null content_hash, masked signatures & assertions.
        var monitorFormRaw = await GetRawAsync(tenantMonitor, $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}", HttpStatusCode.OK, ct);
        monitorFormRaw.Should().NotContain(BlindedArmText).And.NotContain(BlindedQueryPrompt).And.NotContain(BlindedQueryAnswer);
        using (var monitorFormDoc = JsonDocument.Parse(monitorFormRaw))
        {
            var root = monitorFormDoc.RootElement;
            root.GetProperty("content_hash").ValueKind.Should().Be(JsonValueKind.Null);
            var armItem = root.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("item_id").GetString() == TreatmentItem);
            armItem.GetProperty("value").GetProperty("reason").GetString()!.ToLowerInvariant().Should().Be("masked");
            var maskedSig = root.GetProperty("signatures").EnumerateArray().Single();
            maskedSig.GetProperty("superseded").GetBoolean().Should().BeFalse();
            maskedSig.GetProperty("content_hash").GetString().Should().Be("masked");
            maskedSig.GetProperty("reason").GetString().Should().Be("masked");
            var signedValues = maskedSig.GetProperty("signed_values");
            signedValues.ValueKind.Should().Be(JsonValueKind.Array, "CollectionInstance.ContentHashOf must verify raw values before masking SignedValues");
            var maskedSignedArm = signedValues.EnumerateArray()
                .Single(v => v.GetProperty("item_id").GetString() == TreatmentItem);
            maskedSignedArm.GetProperty("observation_id").GetGuid().Should().Be(Guid.Empty);
            maskedSignedArm.GetProperty("value").GetProperty("reason").GetString()!.ToLowerInvariant().Should().Be("masked");
            root.GetProperty("assertions").EnumerateArray().Should().OnlyContain(a => a.GetProperty("text").GetString() == "masked");
        }

        var monitorFormListRaw = await GetRawAsync(tenantMonitor, $"{FormEndpoints.RoutePrefix}?enrollment_id={world.EnrollmentA.Value:D}", HttpStatusCode.OK, ct);
        monitorFormListRaw.Should().NotContain(BlindedArmText).And.NotContain(BlindedSignReason);
        using (var monitorListDoc = JsonDocument.Parse(monitorFormListRaw))
        {
            monitorListDoc.RootElement.EnumerateArray().Single().GetProperty("signed").GetBoolean()
                .Should().BeTrue("form summary must remain Signed=true when ContentHash is verified prior to masking");
        }

        var pharmFormRaw = await GetRawAsync(unblindedPharm, $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}", HttpStatusCode.OK, ct);
        pharmFormRaw.Should().Contain(BlindedArmText).And.Contain(BlindedQueryPrompt).And.Contain(BlindedQueryAnswer);

        // 4. POST /api/v1/execution/forms/{formA}/preview: persisted blinded value is replaced with RuleValue.Null for TenantMonitor
        //    (preventing rule-expression oracle probing while still evaluating unblinded Systolic = 245), whereas UnblindedPharm sees both!
        var monitorPreviewRaw = await SendRawAsync(
            tenantMonitor,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/preview",
            reason: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 245m, unit = "mm[Hg]" } },
                },
            },
            HttpStatusCode.OK,
            ct);
        monitorPreviewRaw.Should().NotContain(BlindedArmText);
        var monitorPreview = JsonSerializer.Deserialize<FormPreviewResponse>(monitorPreviewRaw, Json)!;
        monitorPreview.Warnings.Select(w => w.RuleId).Should().Equal("VS_SBP_HIGH");

        var pharmPreviewRaw = await SendRawAsync(
            unblindedPharm,
            HttpMethod.Post,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/preview",
            reason: null,
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 245m, unit = "mm[Hg]" } },
                },
            },
            HttpStatusCode.OK,
            ct);
        pharmPreviewRaw.Should().Contain(BlindedArmText);
        var pharmPreview = JsonSerializer.Deserialize<FormPreviewResponse>(pharmPreviewRaw, Json)!;
        pharmPreview.Warnings.Select(w => w.RuleId).Should().BeEquivalentTo("VS_SBP_HIGH", "ARM_CHECK");

        // 5. Query endpoints (/forms/{id}/queries, ?enrollment_id=, ?study_id=, and bitemporal as_of): TenantMonitor sees masked text, messages, reasons, and actors.
        var queryAsOf = Uri.EscapeDataString(SystemClock.Instance.UtcNow.AddSeconds(5).ToString("O"));
        foreach (var queryUrl in new[]
        {
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/queries",
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/queries?as_of={queryAsOf}",
            $"{FormEndpoints.RoutePrefix}/queries?enrollment_id={world.EnrollmentA.Value:D}",
            $"{FormEndpoints.RoutePrefix}/queries?enrollment_id={world.EnrollmentA.Value:D}&as_of={queryAsOf}",
            $"{FormEndpoints.RoutePrefix}/queries?study_id={world.Study.Value:D}",
            $"{FormEndpoints.RoutePrefix}/queries?study_id={world.Study.Value:D}&as_of={queryAsOf}",
        })
        {
            var maskedQueriesRaw = await GetRawAsync(tenantMonitor, queryUrl, HttpStatusCode.OK, ct);
            maskedQueriesRaw.Should()
                .NotContain(BlindedArmText)
                .And.NotContain(BlindedQueryPrompt)
                .And.NotContain(BlindedQueryAnswer)
                .And.NotContain("Unblinded pharmacy check")
                .And.NotContain("Unblinded pharmacy confirmation");
            var maskedQueries = JsonSerializer.Deserialize<FormQueryResponse[]>(maskedQueriesRaw, Json)!;
            var q = maskedQueries.Should().ContainSingle().Subject;
            q.Text.Should().Be("masked");
            q.RaisedBy.Should().Be("masked");
            q.LastAnsweredBy.Should().Be("masked");
            q.Messages.Should().NotBeEmpty().And.OnlyContain(m => m.Text == "masked" && m.By == "masked" && m.ReasonForChange == "masked");
        }

        var pharmQueriesRaw = await GetRawAsync(unblindedPharm, $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/queries", HttpStatusCode.OK, ct);
        pharmQueriesRaw.Should().Contain(BlindedQueryPrompt).And.Contain(BlindedQueryAnswer);
    }

    /// <summary>
    /// Validation finding list/detail routes, <c>POST /studies/{studyRef}/rule-packs/{itemRef}/preview</c>, and
    /// Intelligence MCP tools (<c>get_node</c>, <c>explain</c>) enforce per-site visibility and <c>enrollment.unblind</c>
    /// redaction consistently.
    /// </summary>
    [Fact(DisplayName = "ValidationFindingsRulePackPreviewAndMcpTools_EnforceSiteVisibilityAndBlinding")]
    [KernelRule("K-AUTH-4")]
    [KernelRule("K-FIND-1")]
    [KernelRule("K-AI-3")]
    [GxPRequirement("PRD-CRF-013")]
    [GxPRequirement("PRD-MDR-006")]
    public async Task ValidationFindingsRulePackPreviewAndMcpToolsEnforceSiteVisibilityAndBlinding()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedWorldAsync(ct);
        await RelayAsync(ct);

        using var siteACrc = Client(world.Code, SiteACrc);
        using var tenantMonitor = Client(world.Code, TenantMonitor);
        using var unblindedPharm = Client(world.Code, UnblindedPharm);

        // Site A: save violating Systolic (245) + violating blinded arm (BlindedArmText).
        var savedA = await SendJsonAsync<FormSavedResponse>(
            siteACrc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/values",
            "Site A out-of-range vitals and kit",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 245m, unit = "mm[Hg]" }, expected_revision = 0 },
                    new { item_id = TreatmentItem, value = new { type = "text", value = BlindedArmText }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        savedA.Values.Single(v => v.ItemId == TreatmentItem).ObservationId.Should().Be(Guid.Empty);
        var pharmFormAAfterSave = await SendJsonAsync<JsonElement>(
            unblindedPharm, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}", null, null, HttpStatusCode.OK, ct);
        var blindedObsARef = NodeRef.For(new ObservationId(pharmFormAAfterSave.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("item_id").GetString() == TreatmentItem)
            .GetProperty("observation_id").GetGuid())).ToString();

        // Site B: save violating Systolic (252) directly via command pipeline.
        var savedB = await _scenario.Protocol.OkAsync(
            Save(
                new Form(new KernelObservationScenario.Subject(world.Tenant, world.Study, world.SiteB, world.EnrollmentB), world.Version, world.FormB),
                Value(Systolic, new QuantityValue(252m, "mm[Hg]"))),
            ct);
        var siteBObsRef = NodeRef.For(savedB.Values.Single().ObservationId).ToString();

        await RelayAsync(ct);

        var studyRef = NodeRef.For(world.Study).ToString();
        var rulePackRef = NodeRef.For(world.RulePackId).ToString();

        // 1. UnblindedPharmacist sees all 3 findings (2 on Site A + 1 on Site B) with unmasked BlindedArmText.
        var pharmFindingsRaw = await GetRawAsync(unblindedPharm, $"/api/v1/kernel/studies/{studyRef}/validation/findings", HttpStatusCode.OK, ct);
        pharmFindingsRaw.Should().Contain(BlindedArmText).And.Contain("252");
        using var pharmFindingsDoc = JsonDocument.Parse(pharmFindingsRaw);
        pharmFindingsDoc.RootElement.GetProperty("total").GetInt32().Should().Be(3);
        var allFindings = pharmFindingsDoc.RootElement.GetProperty("findings").EnumerateArray().ToList();
        var blindedFindingRef = allFindings.Single(f => f.GetProperty("rule_id").GetString() == "ARM_CHECK").GetProperty("ref").GetString()!;
        var siteBFindingRef = allFindings.Single(f => f.GetProperty("record_key").GetString()!.Contains(world.FormB.Value.ToString("D"), StringComparison.Ordinal)).GetProperty("ref").GetString()!;

        // 2. SiteACrc sees ONLY Site A's non-blinded finding (total == 1), zero Site B findings, and blinded ARM_CHECK is withheld!
        var crcFindingsRaw = await GetRawAsync(siteACrc, $"/api/v1/kernel/studies/{studyRef}/validation/findings", HttpStatusCode.OK, ct);
        crcFindingsRaw.Should().NotContain("252").And.NotContain(siteBFindingRef).And.NotContain(BlindedArmText).And.NotContain("ARM_CHECK");
        using (var crcFindingsDoc = JsonDocument.Parse(crcFindingsRaw))
        {
            crcFindingsDoc.RootElement.GetProperty("total").GetInt32().Should().Be(1);
            crcFindingsDoc.RootElement.GetProperty("findings").EnumerateArray()
                .Single().GetProperty("rule_id").GetString().Should().Be("VS_SBP_HIGH");
        }

        // Cross-site GET /validation/findings/{siteBFindingRef} returns 403 Forbidden for SiteACrc.
        using (var crossSiteDetail = await siteACrc.GetAsync(new Uri($"/api/v1/kernel/validation/findings/{siteBFindingRef}", UriKind.Relative), ct))
        {
            crossSiteDetail.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // TenantMonitor (Tenant() scope, no enrollment.unblind) is refused 403 Forbidden on blindedFindingRef.
        using (var monitorBlindedDetail = await tenantMonitor.GetAsync(new Uri($"/api/v1/kernel/validation/findings/{blindedFindingRef}", UriKind.Relative), ct))
        {
            monitorBlindedDetail.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var monitorDetailBody = await monitorBlindedDetail.Content.ReadAsStringAsync(ct);
            monitorDetailBody.Should().NotContain(BlindedArmText);
        }

        // 3. RulePack preview: SiteACrc evaluates only Site A's form, sees VS_SBP_HIGH, and withholds blinded ARM_CHECK
        //    while UnblindedPharm evaluates both forms and sees all three violations unmasked.
        var crcPreviewRaw = await SendRawAsync(
            siteACrc, HttpMethod.Post, $"/api/v1/kernel/studies/{studyRef}/rule-packs/{rulePackRef}/preview", null, null, HttpStatusCode.OK, ct);
        crcPreviewRaw.Should().Contain(world.FormA.Value.ToString("D"))
            .And.NotContain(world.FormB.Value.ToString("D"))
            .And.NotContain(BlindedArmText);
        using (var crcPreviewDoc = JsonDocument.Parse(crcPreviewRaw))
        {
            crcPreviewDoc.RootElement.GetProperty("forms_evaluated").GetInt32().Should().Be(1);
            crcPreviewDoc.RootElement.GetProperty("violation_count").GetInt32().Should().Be(1);
        }

        var pharmPreviewRaw = await SendRawAsync(
            unblindedPharm, HttpMethod.Post, $"/api/v1/kernel/studies/{studyRef}/rule-packs/{rulePackRef}/preview", null, null, HttpStatusCode.OK, ct);
        pharmPreviewRaw.Should().Contain(world.FormA.Value.ToString("D"))
            .And.Contain(world.FormB.Value.ToString("D"))
            .And.Contain(BlindedArmText);
        using (var pharmPreviewDoc = JsonDocument.Parse(pharmPreviewRaw))
        {
            pharmPreviewDoc.RootElement.GetProperty("forms_evaluated").GetInt32().Should().Be(2);
            pharmPreviewDoc.RootElement.GetProperty("violation_count").GetInt32().Should().Be(3);
        }

        // 4. Intelligence MCP tools (/api/v1/intelligence/mcp):
        //    - SiteACrc cannot get_node on Site B observation or explain Site B finding (isError == true).
        var crcGetSiteBObs = await McpCallAsync(siteACrc, KernelToolNames.GetNode, new { node = siteBObsRef }, ct);
        crcGetSiteBObs.GetProperty("isError").GetBoolean().Should().BeTrue();

        var crcExplainSiteBFinding = await McpCallAsync(siteACrc, KernelToolNames.Explain, new { node = siteBFindingRef }, ct);
        crcExplainSiteBFinding.GetProperty("isError").GetBoolean().Should().BeTrue();

        //    - TenantMonitor (Tenant() scope, no enrollment.unblind) cannot get_node on blinded observation or explain blindedFindingRef (isError == true),
        //      while list_children on EnrollmentA masks the blinded observation child in place!
        var monitorGetBlindedObs = await McpCallAsync(tenantMonitor, KernelToolNames.GetNode, new { node = blindedObsARef }, ct);
        monitorGetBlindedObs.GetProperty("isError").GetBoolean().Should().BeTrue();
        monitorGetBlindedObs.ToString().Should().NotContain(BlindedArmText);

        var monitorListFormChildren = await McpCallAsync(tenantMonitor, KernelToolNames.ListChildren, new { node = NodeRef.For(world.FormA).ToString() }, ct);
        monitorListFormChildren.GetProperty("isError").GetBoolean().Should().BeFalse();
        var listChildrenText = monitorListFormChildren.ToString();
        listChildrenText.Should().Contain("\"masked\":\"blinded\"").And.NotContain(BlindedArmText).And.NotContain(blindedObsARef);

        var monitorExplainBlinded = await McpCallAsync(tenantMonitor, KernelToolNames.Explain, new { node = blindedFindingRef }, ct);
        monitorExplainBlinded.GetProperty("isError").GetBoolean().Should().BeTrue();
        monitorExplainBlinded.ToString().Should().NotContain(BlindedArmText);

        //    - UnblindedPharmacist sees BlindedArmText in both get_node and explain.
        var pharmGetBlindedObs = await McpCallAsync(unblindedPharm, KernelToolNames.GetNode, new { node = blindedObsARef }, ct);
        pharmGetBlindedObs.GetProperty("isError").GetBoolean().Should().BeFalse();
        pharmGetBlindedObs.ToString().Should().Contain(BlindedArmText);

        var pharmExplainBlinded = await McpCallAsync(unblindedPharm, KernelToolNames.Explain, new { node = blindedFindingRef }, ct);
        pharmExplainBlinded.GetProperty("isError").GetBoolean().Should().BeFalse();
        pharmExplainBlinded.ToString().Should().Contain(BlindedArmText);
    }

    /// <summary>
    /// Historical <c>as_of</c> study exports and casebooks reconstruct protocol metadata at <c>(asOf, asOf)</c> without
    /// leaking post-<c>as_of</c> amendments or draft edits, and blinded exports eliminate every clinical, provenance,
    /// signature, and audit-trail side channel while preserving unblinded exports for callers with <c>enrollment.unblind</c>.
    /// </summary>
    [Fact(DisplayName = "BitemporalExportsAndCasebooks_ExcludePostKnownAtProtocolEdits_AndEliminateBlindedSideChannels")]
    [KernelRule("K-TRUTH-1")]
    [KernelRule("K-AUTH-4")]
    [GxPRequirement("PRD-CDR-006")]
    [GxPRequirement("PRD-MDR-006")]
    public async Task BitemporalExportsAndCasebooksExcludePostKnownAtProtocolEditsAndEliminateBlindedSideChannels()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedWorldAsync(ct, declareStandards: false);
        await RelayAsync(ct);

        using var crc = Client(world.Code, SiteACrc);
        using var blindedStat = Client(world.Code, BlindedStat);
        using var unblindedPharm = Client(world.Code, UnblindedPharm);

        var saved = await SendJsonAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/values",
            "Recorded baseline vitals and randomization arm",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 118.50m, unit = "mm[Hg]" }, expected_revision = 0 },
                    new { item_id = TreatmentItem, value = new { type = "text", value = BlindedArmText }, expected_revision = 0 },
                },
            },
            HttpStatusCode.OK,
            ct);
        saved.Values.Single(v => v.ItemId == TreatmentItem).ObservationId.Should().Be(Guid.Empty);
        var pharmFormForExport = await SendJsonAsync<JsonElement>(
            unblindedPharm, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}", null, null, HttpStatusCode.OK, ct);
        var blindedObsId = new ObservationId(pharmFormForExport.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("item_id").GetString() == TreatmentItem)
            .GetProperty("observation_id").GetGuid());
        var blindedObsUuid = blindedObsId.Value.ToString("D");

        // Verify blinded observation and sign formA while discrepancy-free, then raise + answer a blinded pharmacy query and freeze before tCut.
        Tick();
        await _scenario.Protocol.OkAsync(
            new VerifyObservation(world.Tenant, AssertionId.New(), world.EnrollmentA, blindedObsId, 1, KernelProtocolScenario.Key(), "Verified kit against pharmacy log"),
            ct);

        var pharmForm = await SendJsonAsync<JsonElement>(
            unblindedPharm, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}", null, null, HttpStatusCode.OK, ct);
        using (var signReq = new HttpRequestMessage(HttpMethod.Post, new Uri($"{FormEndpoints.RoutePrefix}/{world.FormA.Value:D}/signatures", UriKind.Relative)))
        {
            signReq.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
            signReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(BlindedSignReason));
            signReq.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(UnblindedPharm, FormSignatureEndpoints.SignAction(world.FormA.Value), FormSignatureEndpoints.SignSemanticAction));
            signReq.Content = JsonContent.Create(new { meaning = "approved", content_hash = pharmForm.GetProperty("content_hash").GetString() });
            using var signRes = await unblindedPharm.SendAsync(signReq, ct);
            signRes.StatusCode.Should().Be(HttpStatusCode.Created, await signRes.Content.ReadAsStringAsync(ct));
        }

        Tick();
        var queryAssertion = await _scenario.Protocol.OkAsync(
            new RaiseQuery(world.Tenant, AssertionId.New(), world.EnrollmentA, blindedObsId, BlindedQueryPrompt, KernelProtocolScenario.Key(), "Pharmacy query"),
            ct);
        await _scenario.Protocol.OkAsync(
            new AnswerQuery(world.Tenant, AssertionId.New(), world.EnrollmentA, queryAssertion.AssertionId, BlindedQueryAnswer, KernelProtocolScenario.Key(), "Pharmacy answer"),
            ct);

        // Also freeze the blinded observation directly before tCut via POST /api/v1/kernel/locks (while its query remains open, per LockReadinessEvaluator) so both blinded lock attestations and blinded form signatures are exercised.
        using (var lockReq = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/kernel/locks", UriKind.Relative)))
        {
            lockReq.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
            lockReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(BlindedSignReason));
            lockReq.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(UnblindedPharm, KernelLockEndpoints.ApplyLockAction, KernelLockEndpoints.ApplyLockSemanticAction));
            lockReq.Content = JsonContent.Create(new
            {
                study_ref = NodeRef.For(world.Study).ToString(),
                scope_ref = NodeRef.For(blindedObsId).ToString(),
                kind = "Frozen",
                meaning = "Approved",
            });
            using var lockRes = await unblindedPharm.SendAsync(lockReq, ct);
            lockRes.StatusCode.Should().Be(HttpStatusCode.Created, await lockRes.Content.ReadAsStringAsync(ct));
        }

        await Task.Delay(20, ct);
        var tCut = SystemClock.Instance.UtcNow;
        await Task.Delay(20, ct);
        fixture.Clock.Set(SystemClock.Instance.UtcNow);

        // Strictly AFTER tCut: fork, revise with a future item, approve, and make effective with a retroactive valid_at!
        const string futureItemMarker = "item.post_cut_amendment_marker";
        var markerConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(world.Tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.DataElement, "Post-cut marker", "Post-cut marker.", KernelProtocolScenario.Key(), "Defined post-cut"),
            ct);
        var amendedDesign = world.Design with
        {
            CollectionSpecs =
            [
                new CollectionSpec(
                    SpecId,
                    "Vital signs amended after cut",
                    [
                        .. world.Design.CollectionSpecs.Single().Items,
                        new CollectionItem(futureItemMarker, markerConcept, ValueKind: ObservationValueKind.Text),
                    ]),
            ],
        };

        var v2Id = ProtocolVersionId.New();
        var amendment = new ProtocolAmendment(
            "Add post-cut marker",
            "Test bitemporal known_at isolation",
            AmendmentClassification.NonSubstantial,
            "Added future item marker",
            [SpecId, futureItemMarker],
            TransitionPolicy.Automatic);
        await _scenario.Protocol.OkAsync(new ForkProtocolVersion(world.Tenant, world.Version, v2Id, amendment, KernelProtocolScenario.Key(), "Forked v2 after cut"), ct);
        await _scenario.Protocol.OkAsync(new ReviseProtocolDraft(world.Tenant, v2Id, ProtocolDesignDocument.Write(amendedDesign), null, KernelProtocolScenario.Key(), "Revised v2 after cut"), ct);
        var v2Review = await _scenario.Protocol.OkAsync(new SubmitProtocolVersionForReview(world.Tenant, v2Id, KernelProtocolScenario.Key(), "Submitted v2"), ct);
        var (_, _, v2SimReviewAttest) = await _scenario.Protocol.SimulateAndAttestReviewAsync(world.Tenant, world.Study, v2Id, ct);
        var v2Attest = await _scenario.Protocol.OkAsync(_scenario.Protocol.AttestCommand(world.Tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(v2Review)), ct);
        await _scenario.Protocol.OkAsync(new ApproveProtocolVersion(world.Tenant, v2Id, v2Attest, KernelProtocolScenario.Key(), "Approved v2", v2SimReviewAttest), ct);
        var v2Eff = EffectivityId.New();
        // Retroactive EffectiveAt (before tCut!), recorded strictly AFTER tCut: known_at = tCut must NOT see v2!
        var retroactiveValidAt = tCut.AddHours(-1);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(2));
        await _scenario.Protocol.OkAsync(new DeclareEffectivity(world.Tenant, v2Eff, v2Id, EffectivityScope.Study(), retroactiveValidAt, KernelProtocolScenario.Key(), "Declared retroactive v2"), ct);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(2));
        await _scenario.Protocol.OkAsync(new ChangeEffectivityStatus(world.Tenant, v2Eff, EffectivityStatus.Effective, retroactiveValidAt.AddMinutes(5), [world.EthicsArtifactVersion], KernelProtocolScenario.Key(), "Effective v2"), ct);
        await Task.Delay(20, ct);

        var studyRef = NodeRef.For(world.Study).ToString();
        var enrollmentARef = NodeRef.For(world.EnrollmentA).ToString();
        var asOfParam = Uri.EscapeDataString(tCut.ToString("O"));
        var asOfAfterV2Param = Uri.EscapeDataString(fixture.Clock.UtcNow.AddSeconds(5).ToString("O"));

        // 1. Historical ODM XML and casebook at as_of=tCut exclude futureItemMarker; snapshot after v2 includes it!
        var historicalOdm = await GetRawAsync(unblindedPharm, $"/api/v1/kernel/studies/{studyRef}/exports?format=odm_xml&as_of={asOfParam}", HttpStatusCode.OK, ct);
        historicalOdm.Should().NotContain(futureItemMarker).And.NotContain("Vital signs amended after cut");

        var latestOdm = await GetRawAsync(unblindedPharm, $"/api/v1/kernel/studies/{studyRef}/exports?format=odm_xml&as_of={asOfAfterV2Param}", HttpStatusCode.OK, ct);
        latestOdm.Should().Contain(futureItemMarker);

        // 2. BlindedStatistician exports (ODM XML + JSON/ODM/HTML/PDF casebooks) strip all blinded side channels.
        var statOdm = await GetRawAsync(blindedStat, $"/api/v1/kernel/studies/{studyRef}/exports?format=odm_xml", HttpStatusCode.OK, ct);
        AssertNoBlindedLeak(statOdm, blindedObsUuid);
        XNamespace odmNs = "http://www.cdisc.org/ns/odm/v1.3";
        var statOdmDoc = XDocument.Parse(statOdm);
        var blindedFormSig = statOdmDoc.Descendants(odmNs + "FormData").Descendants(odmNs + "Signature").Single();
        blindedFormSig.Element(odmNs + "UserRef")!.Attribute("UserOID")!.Value.Should().Be("masked");
        var blindedSigOid = blindedFormSig.Element(odmNs + "SignatureRef")!.Attribute("SignatureOID")!.Value;
        var blindedSigDef = statOdmDoc.Descendants(odmNs + "SignatureDef").Single(sd => sd.Attribute("OID")?.Value == blindedSigOid);
        blindedSigDef.Elements(odmNs + "LegalReason").Should().BeEmpty("blinded ODM signatures must omit LegalReason");

        foreach (var format in new[] { "json", "odm_xml", "html", "pdf" })
        {
            var statCasebookRaw = await GetRawAsync(blindedStat, $"/api/v1/kernel/enrollments/{enrollmentARef}/casebook?format={format}", HttpStatusCode.OK, ct);
            AssertNoBlindedLeak(statCasebookRaw, blindedObsUuid);

            if (format == "json")
            {
                var cb = JsonSerializer.Deserialize<KernelCasebookResponse>(statCasebookRaw, Json)!;
                cb.Observations.Single(o => o.Masked).Ref.Should().Be("masked");
                cb.Forms.Single().ContentHash.Should().BeNull("form content hash must be redacted when a member item is blinded");
                cb.Queries.Should().HaveCount(2);
                cb.Queries.Should().OnlyContain(q => q.Ref == "masked" && q.TargetRef == "masked" && q.Text == "masked" && q.AssertedBy == null);

                var maskedVerif = cb.Verifications.Should().ContainSingle().Subject;
                maskedVerif.Ref.Should().Be("masked");
                maskedVerif.ObservationRef.Should().Be("masked");
                maskedVerif.VerifiedBy.Should().BeNull();

                cb.Attestations.Should().HaveCount(2);
                var maskedLockAttest = cb.Attestations.Single(a => a.Ref == "masked");
                maskedLockAttest.SignerId.Should().Be("masked");
                maskedLockAttest.Reason.Should().Be("masked");
                maskedLockAttest.TargetRef.Should().Be("masked");
                maskedLockAttest.TargetContentHash.Should().Be("masked");

                var maskedFormAttest = cb.Attestations.Single(a => a.TargetRef == NodeRef.For(world.FormA).ToString());
                maskedFormAttest.SignerId.Should().Be("masked");
                maskedFormAttest.Reason.Should().Be("masked");
                maskedFormAttest.TargetContentHash.Should().Be("masked");

                cb.AuditTrail.Where(a => a.Subject == "masked").Should().NotBeEmpty()
                    .And.OnlyContain(a => a.ActorId == "masked" && a.ReasonForChange == "masked");
            }
            else if (format == "odm_xml")
            {
                // AC 7: Blinded casebook ODM XML includes Study, AdminData/SignatureDef, nested ClinicalData hierarchy, and passes OdmDesignImporter cleanly.
                await AssertValidCasebookOdmXmlAsync(statCasebookRaw, expectSignature: true, ct);
            }
        }

        // 3. UnblindedPharmacist sees unmasked values, ObservationRef, LegalReason, and AuditTrail details, and casebook ODM XML passes OdmDesignImporter cleanly.
        var pharmCasebookRaw = await GetRawAsync(unblindedPharm, $"/api/v1/kernel/enrollments/{enrollmentARef}/casebook?format=json", HttpStatusCode.OK, ct);
        pharmCasebookRaw.Should().Contain(BlindedArmText).And.Contain(BlindedQueryPrompt).And.Contain(BlindedQueryAnswer).And.Contain(BlindedSignReason).And.Contain(blindedObsUuid);
        var pharmCasebookOdm = await GetRawAsync(unblindedPharm, $"/api/v1/kernel/enrollments/{enrollmentARef}/casebook?format=odm_xml", HttpStatusCode.OK, ct);
        pharmCasebookOdm.Should().Contain(BlindedArmText).And.Contain(BlindedSignReason);
        await AssertValidCasebookOdmXmlAsync(pharmCasebookOdm, expectSignature: true, ct);
        var unblindedOdmDoc = XDocument.Parse(latestOdm);
        var unblindedSigDef = unblindedOdmDoc.Descendants(odmNs + "SignatureDef").Single(sd => sd.Attribute("OID")?.Value == blindedSigOid);
        unblindedSigDef.Element(odmNs + "LegalReason")!.Value.Should().Be(BlindedSignReason);
    }

    /// <summary>
    /// <c>ExportStudySnapshot</c> (<c>study.export</c>) and <c>ExportEnrollmentCasebook</c> (<c>enrollment.export</c>)
    /// deny unauthorized callers with 403 and record a denied <c>ProvenanceActivity</c> + Merkle <c>AuditEntry</c>,
    /// register immutable <c>Dataset</c> <c>Artifact</c>s and blinded/unblinded disclosure audit entries when permitted,
    /// and remain permitted as <c>[BaselineKernelCommand]</c>s when a tenant's commercial <c>Subscription</c> is expired.
    /// </summary>
    [Fact(DisplayName = "ExportCommands_EnforceCapabilityGates_AuditDeniedAndPermittedExports_AndRemainBaselineWhenSubscriptionExpires")]
    [KernelRule("K-AUTH-2")]
    [KernelRule("K-PROV-1")]
    [GxPRequirement("PRD-CDR-006")]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-SYS-192")]
    public async Task ExportCommandsEnforceCapabilityGatesAuditDeniedAndPermittedExportsAndRemainBaselineWhenSubscriptionExpires()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedWorldAsync(ct);
        await RelayAsync(ct);

        var studyRef = NodeRef.For(world.Study).ToString();
        var enrollmentARef = NodeRef.For(world.EnrollmentA).ToString();

        // Record a blinded arm observation on EnrollmentA so exports distinguish blinded vs unblinded disclosure.
        await _scenario.Protocol.OkAsync(
            Save(
                new Form(new KernelObservationScenario.Subject(world.Tenant, world.Study, world.SiteA, world.EnrollmentA), world.Version, world.FormA),
                Value(Systolic, new QuantityValue(120m, "mm[Hg]")),
                Value(TreatmentItem, new TextValue(BlindedArmText))),
            ct);
        await RelayAsync(ct);

        using var reviewerNoExport = Client(world.Code, ReadOnlyReviewer);
        using var blindedStat = Client(world.Code, BlindedStat);
        using var unblindedPharm = Client(world.Code, UnblindedPharm);

        int deniedAuditsBefore;
        int datasetArtifactsBefore;
        await using (var db = fixture.CreateAdminContext())
        {
            deniedAuditsBefore = await db.AuditEntries.CountAsync(a => a.TenantId == world.Tenant && a.Action == "Denied", ct);
            datasetArtifactsBefore = await db.Set<Artifact>().CountAsync(a => a.TenantId == world.Tenant && a.Kind == ArtifactKind.Dataset, ct);
        }

        // 1. ReadOnlyReviewer can see the study (holds observation.verify on Study), yet lacks study.export & enrollment.export:
        //    both routes fail closed with 403 K.Authorization.Denied and append Denied Merkle AuditEntries!
        using (var deniedStudyExport = await reviewerNoExport.GetAsync(new Uri($"/api/v1/kernel/studies/{studyRef}/exports?format=csv", UriKind.Relative), ct))
        {
            deniedStudyExport.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            using var problem = JsonDocument.Parse(await deniedStudyExport.Content.ReadAsStringAsync(ct));
            problem.RootElement.GetProperty("code").GetString().Should().Be(KernelErrors.AuthorizationDenied.Code);
        }

        using (var deniedCasebook = await reviewerNoExport.GetAsync(new Uri($"/api/v1/kernel/enrollments/{enrollmentARef}/casebook?format=json", UriKind.Relative), ct))
        {
            deniedCasebook.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            using var problem = JsonDocument.Parse(await deniedCasebook.Content.ReadAsStringAsync(ct));
            problem.RootElement.GetProperty("code").GetString().Should().Be(KernelErrors.AuthorizationDenied.Code);
        }

        await using (var db = fixture.CreateAdminContext())
        {
            (await db.AuditEntries.CountAsync(a => a.TenantId == world.Tenant && a.Action == "Denied", ct))
                .Should().Be(deniedAuditsBefore + 2, "each unauthorized export attempt must write a Denied audit entry");
            var deniedBiostatLogs = await db.BiostatExportAuditLogs.AsNoTracking()
                .Where(l => l.TenantId == world.Tenant && l.Outcome == "Denied")
                .ToListAsync(ct);
            deniedBiostatLogs.Should().HaveCount(2, "each denied export command must also write a Denied BiostatExportAuditLog row");
            (await db.Set<Artifact>().CountAsync(a => a.TenantId == world.Tenant && a.Kind == ArtifactKind.Dataset, ct))
                .Should().Be(datasetArtifactsBefore, "unauthorized exports must not register Dataset artifacts");
        }

        // 2. Permitted exports by BlindedStat vs UnblindedPharm register Dataset artifacts, events, and disclosure audit entries.
        await GetRawAsync(blindedStat, $"/api/v1/kernel/studies/{studyRef}/exports?format=csv", HttpStatusCode.OK, ct);
        await GetRawAsync(unblindedPharm, $"/api/v1/kernel/enrollments/{enrollmentARef}/casebook?format=json", HttpStatusCode.OK, ct);

        await using (var db = fixture.CreateAdminContext())
        {
            (await db.Set<Artifact>().CountAsync(a => a.TenantId == world.Tenant && a.Kind == ArtifactKind.Dataset, ct))
                .Should().Be(datasetArtifactsBefore + 2);
            (await db.DomainEvents.CountAsync(e => e.TenantId == world.Tenant && e.Type == "StudySnapshotExported", ct))
                .Should().Be(1);
            (await db.DomainEvents.CountAsync(e => e.TenantId == world.Tenant && e.Type == "EnrollmentCasebookExported", ct))
                .Should().Be(1);

            var exportAudits = await db.AuditEntries.AsNoTracking()
                .Where(a => a.TenantId == world.Tenant && a.AggregateType == nameof(Artifact) && a.ReasonForChange.Contains("disclosure="))
                .OrderBy(a => a.Sequence)
                .ToListAsync(ct);
            exportAudits.Should().HaveCount(2);
            exportAudits[0].ActorId.Should().Be(BlindedStat);
            exportAudits[0].ReasonForChange.Should().Contain("disclosure=blinded");
            exportAudits[1].ActorId.Should().Be(UnblindedPharm);
            exportAudits[1].ReasonForChange.Should().Contain("disclosure=unblinded");

            var committedBiostatLogs = await db.BiostatExportAuditLogs.AsNoTracking()
                .Where(l => l.TenantId == world.Tenant && l.Outcome == "Committed")
                .OrderBy(l => l.Timestamp)
                .ToListAsync(ct);
            committedBiostatLogs.Should().HaveCount(2);
            committedBiostatLogs[0].Action.Should().Be("EXPORT_STUDY_SNAPSHOT");
            committedBiostatLogs[0].UserId.Should().Be(BlindedStat);
            committedBiostatLogs[0].Format.Should().Be("csv");
            committedBiostatLogs[0].LockFilter.Should().Be("all");
            committedBiostatLogs[0].IsUnblinded.Should().BeFalse();
            committedBiostatLogs[0].Sha256Hash.Should().HaveLength(64);

            committedBiostatLogs[1].Action.Should().Be("EXPORT_ENROLLMENT_CASEBOOK");
            committedBiostatLogs[1].UserId.Should().Be(UnblindedPharm);
            committedBiostatLogs[1].EnrollmentId.Should().Be(world.EnrollmentA);
            committedBiostatLogs[1].Format.Should().Be("json");
            committedBiostatLogs[1].IsUnblinded.Should().BeTrue();
            committedBiostatLogs[1].Sha256Hash.Should().HaveLength(64);

            // InsertOnlySql triggers forbid UPDATE and DELETE on kernel.biostat_export_audit_logs even for admin connections!
            var targetId = committedBiostatLogs[0].Id;
            await FluentActions.Invoking(() => db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE kernel.biostat_export_audit_logs SET outcome = 'Tampered' WHERE id = {targetId}", ct))
                .Should().ThrowAsync<Npgsql.PostgresException>();
            await FluentActions.Invoking(() => db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM kernel.biostat_export_audit_logs WHERE id = {targetId}", ct))
                .Should().ThrowAsync<Npgsql.PostgresException>();
        }

        // 3. Provision an Expired commercial Subscription: non-baseline writes fail with 403 K.Subscription.Expired,
        //    whereas [BaselineKernelCommand] study & casebook exports continue to succeed and record Dataset artifacts!
        await _scenario.Protocol.OkAsync(
            new ProvisionSubscription(
                world.Tenant,
                SubscriptionId.New(),
                CommercialTier.Complete,
                SubscriptionStatus.Expired,
                null,
                KernelProtocolScenario.Key(),
                "Simulated expired trial subscription"),
            ct);

        using (var blockedWrite = await unblindedPharm.SendAsync(
            BuildJsonRequest(
                HttpMethod.Post,
                "/api/v1/kernel/observations",
                "Attempted write under expired subscription",
                new
                {
                    observation_id = ObservationId.New().Value,
                    enrollment_ref = enrollmentARef,
                    concept = $"{world.SystolicConcept.ConceptId.Value:D}@{world.SystolicConcept.Version}",
                    value = new { type = "quantity", value = 120m, unit = "mm[Hg]" },
                    observed_at = Tick(),
                }),
            ct))
        {
            blockedWrite.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            using var problem = JsonDocument.Parse(await blockedWrite.Content.ReadAsStringAsync(ct));
            problem.RootElement.GetProperty("code").GetString().Should().Be(KernelErrors.SubscriptionExpired.Code);
        }

        await GetRawAsync(blindedStat, $"/api/v1/kernel/studies/{studyRef}/exports?format=dataset_json", HttpStatusCode.OK, ct);
        await GetRawAsync(unblindedPharm, $"/api/v1/kernel/enrollments/{enrollmentARef}/casebook?format=odm_xml", HttpStatusCode.OK, ct);

        await using (var db = fixture.CreateAdminContext())
        {
            (await db.Set<Artifact>().CountAsync(a => a.TenantId == world.Tenant && a.Kind == ArtifactKind.Dataset, ct))
                .Should().Be(datasetArtifactsBefore + 4);
        }
    }

    private static void AssertNoBlindedLeak(string payload, string blindedObsUuid)
    {
        foreach (var secret in new[] { BlindedArmText, BlindedQueryPrompt, BlindedQueryAnswer, BlindedSignReason, blindedObsUuid })
        {
            payload.Should().NotContain(secret);
        }
    }

    private static async Task AssertValidCasebookOdmXmlAsync(string odmXml, bool expectSignature, CancellationToken ct)
    {
        XNamespace odmNs = OdmDesignImporter.OdmNamespace;
        var doc = XDocument.Parse(odmXml);
        doc.Root!.Name.Should().Be(odmNs + "ODM");
        var study = doc.Root.Element(odmNs + "Study");
        study.Should().NotBeNull();
        study!.Element(odmNs + "GlobalVariables").Should().NotBeNull();
        var mdv = study.Element(odmNs + "MetaDataVersion");
        mdv.Should().NotBeNull();
        mdv!.Elements(odmNs + "StudyEventDef").Should().NotBeEmpty();
        mdv.Elements(odmNs + "FormDef").Should().ContainSingle(e => e.Attribute("OID")!.Value == $"FORM.{SpecId}");
        mdv.Elements(odmNs + "ItemGroupDef").Should().ContainSingle(e => e.Attribute("OID")!.Value == $"IG.{SpecId}");
        mdv.Elements(odmNs + "ItemDef").Should().NotBeEmpty();

        var clinical = doc.Root.Element(odmNs + "ClinicalData");
        clinical.Should().NotBeNull();
        var subject = clinical!.Element(odmNs + "SubjectData");
        subject.Should().NotBeNull();
        var studyEvent = subject!.Elements(odmNs + "StudyEventData").FirstOrDefault();
        studyEvent.Should().NotBeNull();
        var formData = studyEvent!.Elements(odmNs + "FormData").FirstOrDefault();
        formData.Should().NotBeNull();
        var itemGroup = formData!.Elements(odmNs + "ItemGroupData").FirstOrDefault();
        itemGroup.Should().NotBeNull();
        itemGroup!.Elements(odmNs + "ItemData").Should().NotBeEmpty();

        if (expectSignature)
        {
            var admin = doc.Root.Element(odmNs + "AdminData");
            admin.Should().NotBeNull();
            admin!.Elements(odmNs + "SignatureDef").Should().NotBeEmpty();
            formData.Elements(odmNs + "Signature").Should().NotBeEmpty();
        }

        var importResult = await OdmDesignImporter.ImportAsync(
            odmXml,
            (_, _) => ValueTask.FromResult(new ProtocolConceptResolution(new ConceptRef(ConceptId.New(), 1), UsedFallbackSponsorConcept: false)),
            cancellationToken: ct);
        importResult.Problems.Should().BeEmpty();
        importResult.IsSuccess.Should().BeTrue();
        importResult.CollectionSpecs.Should().ContainSingle(cs => cs.Name.Contains("Vital signs", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SeededWorld> SeedWorldAsync(CancellationToken ct, bool declareStandards = true)
    {
        fixture.Clock.Set(SystemClock.Instance.UtcNow.AddHours(-3));
        var code = $"T-UV-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Unified visibility and export gate tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);

        var systolicConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Systolic BP", "Systolic BP.", KernelProtocolScenario.Key(), "Defined"), ct);
        var diastolicConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Diastolic BP", "Diastolic BP.", KernelProtocolScenario.Key(), "Defined"), ct);
        var treatmentConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(
                tenant,
                ConceptId.New(),
                ConceptOrigin.Sponsor,
                null,
                ConceptKind.BiomedicalConcept,
                "Randomized arm (randomization_treatment_assignment)",
                "Randomized arm allocation.",
                KernelProtocolScenario.Key(),
                "Defined"),
            ct);

        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(
                    VisitId,
                    "Baseline visit",
                    "ep.treatment",
                    [ActivityLocation.Site],
                    [SpecId],
                    new ActivityTiming(1, 0, 2)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    SpecId,
                    "Vital signs",
                    [
                        new CollectionItem(Systolic, systolicConcept, "mm[Hg]", Required: true),
                        new CollectionItem(Diastolic, diastolicConcept, "mm[Hg]", Required: false),
                        new CollectionItem(TreatmentItem, treatmentConcept, ValueKind: ObservationValueKind.Text, Required: false),
                    ]),
            ],
        };

        var versionId = ProtocolVersionId.New();
        var draft = await _scenario.Protocol.CreateDraftAsync(tenant, study, versionId, ct, ProtocolDesignDocument.Write(design));
        draft.IsSuccess.Should().BeTrue(draft.Error?.Message);
        var inReview = await _scenario.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, versionId, KernelProtocolScenario.Key(), "Ready"), ct);
        var signature = await _scenario.Protocol.OkAsync(_scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.Protocol.OkAsync(new ApproveProtocolVersion(tenant, versionId, signature, KernelProtocolScenario.Key(), "Approved"), ct);

        var effectivity = EffectivityId.New();
        await _scenario.Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivity, versionId, EffectivityScope.Study(), Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethicsBytes = Encoding.UTF8.GetBytes($"ethics-{Guid.NewGuid():N}");
        var ethicsVersion = ArtifactVersionId.New();
        var ethicsContent = new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://evidence/ethics.pdf");
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(tenant, ArtifactId.New(), ethicsVersion, ArtifactKind.EthicsApproval, study, "Ethics approval", ethicsContent, KernelProtocolScenario.Key(), "Filed"), ct);
        await _scenario.Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, Tick(), [ethicsVersion], KernelProtocolScenario.Key(), "Effective"), ct);

        StudySiteId siteA;
        StudySiteId siteB;
        await using (var db = fixture.CreateContext(tenant))
        {
            var studyRow = await db.Studies.AsNoTracking().SingleAsync(s => s.Id == study, ct);
            var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var rawSiteA = new Site(SiteId.New(), tenant, studyRow.SponsorId, $"SITE-A-{suffix}", "Site Alpha", "US", KernelPostgresFixture.ActorId, "Seeded Site A");
            var rawSiteB = new Site(SiteId.New(), tenant, studyRow.SponsorId, $"SITE-B-{suffix}", "Site Beta", "US", KernelPostgresFixture.ActorId, "Seeded Site B");
            var linkA = new StudySite(StudySiteId.New(), tenant, study, rawSiteA.Id, "Active", KernelPostgresFixture.ActorId, "Seeded Site A");
            var linkB = new StudySite(StudySiteId.New(), tenant, study, rawSiteB.Id, "Active", KernelPostgresFixture.ActorId, "Seeded Site B");
            await fixture.SeedAsync(rawSiteA, rawSiteB, linkA, linkB);
            siteA = linkA.Id;
            siteB = linkB.Id;
        }

        var participantA = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented A"), ct);
        SubjectCode.TryFrom("SUBJ-A", out var subjA);
        var screenedA = await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participantA, siteA, subjA, Tick(), KernelProtocolScenario.Key(), "Screened A"), ct);

        var participantB = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented B"), ct);
        SubjectCode.TryFrom("SUBJ-B", out var subjB);
        var screenedB = await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participantB, siteB, subjB, Tick(), KernelProtocolScenario.Key(), "Screened B"), ct);

        // Pin a RulePack with BOTH a normal Systolic check (VS_SBP_HIGH) and a blinded arm check (ARM_CHECK).
        var sbpVar = new VariableExpression(Systolic);
        var armVar = new VariableExpression(TreatmentItem);
        var pack = new RulePack(
            RuleAuthority.Study,
            "Unified Visibility Edit Checks",
            [
                new Rule(
                    "VS_SBP_HIGH",
                    1,
                    RuleAuthority.Study,
                    new SourceCitation("Validation Plan", "Section 1", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, SpecId),
                    ValidationSeverity.Warning,
                    new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsNotEmpty, [sbpVar]),
                            new ComparisonExpression(ComparisonOperator.Greater, sbpVar, new LiteralExpression(RuleValue.FromNumber(220m))),
                        ]),
                    "Systolic blood pressure exceeds 220 mm[Hg].",
                    "Confirm cuff measurement.",
                    EffectivePeriod.Always),
                new Rule(
                    "ARM_CHECK",
                    1,
                    RuleAuthority.Study,
                    new SourceCitation("Pharmacy Plan", "Section 2", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, SpecId),
                    ValidationSeverity.Warning,
                    new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsNotEmpty, [armVar]),
                            new ComparisonExpression(ComparisonOperator.Equal, armVar, new LiteralExpression(RuleValue.FromText(BlindedArmText))),
                        ]),
                    $"Assigned arm matched {BlindedArmText}.",
                    "Verify kit reconciliation.",
                    EffectivePeriod.Always),
            ]);
        var rulePackId = LibraryItemId.New();
        var draftedPack = await _scenario.Protocol.OkAsync(
            new DraftLibraryItem(tenant, rulePackId, LibraryItemKind.RulePack, "unified-vis-checks", pack.Title, RulePackJson.ToJson(pack), [], KernelProtocolScenario.Key(), "Drafted rule pack"),
            ct);
        var packAttest = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, rulePackId.Value, draftedPack.ContentHash)),
            ct);
        await _scenario.Protocol.OkAsync(new ApproveLibraryItem(tenant, rulePackId, packAttest, KernelProtocolScenario.Key(), "Approved rule pack"), ct);

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
                        new VariableDefinitionData("VSORRES", "Result in Original Units", 1, StandardDataType.Num, "Result Qualifier", VariableCore.Expected, [], [], null, null),
                    ]),
            ],
            [],
            []);
        await _scenario.Protocol.OkAsync(new RegisterStandardVersion(tenant, standardId, standardPkg, null, KernelProtocolScenario.Key(), "Registered CDASHIG"), ct);
        var stdSummary = (await fixture.Standards.FindStandardVersionAsync(tenant, standardId, ct))!;
        var stdAttest = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.StandardVersion, standardId.Value, stdSummary.ContentHash)),
            ct);
        await _scenario.Protocol.OkAsync(new ApproveStandardVersion(tenant, standardId, stdAttest, KernelProtocolScenario.Key(), "Approved CDASHIG"), ct);
        if (declareStandards)
        {
            await _scenario.Protocol.OkAsync(
                new DeclareStudyStandards(tenant, study, StudyStandardsProfileId.New(), [standardId], [], [rulePackId], KernelProtocolScenario.Key(), "Pinned rule pack"),
                ct);
        }

        var openedA = await _scenario.Protocol.OkAsync(
            Open(new KernelObservationScenario.Subject(tenant, study, siteA, screenedA.EnrollmentId), CollectionInstanceId.New(), Tick()), ct);
        var openedB = await _scenario.Protocol.OkAsync(
            Open(new KernelObservationScenario.Subject(tenant, study, siteB, screenedB.EnrollmentId), CollectionInstanceId.New(), Tick()), ct);

        foreach (var (actor, capabilities, scope) in new (Actor, string[], AuthorityScope)[]
        {
            (Actor.Person(SiteACrc), ["collection.open", "collection.record", "execution.query.answer"], AuthorityScope.StudySite(siteA)),
            (Actor.Person(TenantMonitor), ["observation.verify", "execution.query.raise", "execution.query.manage", "collection.record_unasked"], AuthorityScope.Tenant()),
            (Actor.Person(BlindedStat), ["observation.verify", "study.export", "enrollment.export"], AuthorityScope.Study(study)),
            (Actor.Person(UnblindedPharm), ["observation.verify", "observation.record", "collection.sign", "lock.apply", "attestation.sign", "execution.query.raise", "execution.query.answer", "execution.query.manage", "study.export", "enrollment.export", "enrollment.unblind"], AuthorityScope.Study(study)),
            (Actor.Person(ReadOnlyReviewer), ["observation.verify"], AuthorityScope.Study(study)),
            (Actor.ServiceAccount("execution-forms"), ["execution.form.open"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount("execution-queries"), ["observation.query", "observation.query.answer", "assertion.retract"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount(AlertMonitorOnVerifiedObservationCorrected.Name), ["execution.query.manage"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount(EvaluateEditChecksOnCollectionChanged.Name), ["validation_run.record", "collection.record_unasked", "execution.query.raise", "execution.query.manage"], AuthorityScope.Tenant()),
            (Actor.AutomationAgent(IntelligenceMcp.AgentName), ["observation.verify", "enrollment.unblind"], AuthorityScope.Tenant()),
        })
        {
            foreach (var cap in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), actor, cap, null, scope, Tick(), null, false, KernelProtocolScenario.Key(), "Test grant"),
                    ct);
            }
        }

        // Attestation signing checks Tenant-scoped or Target-scoped attestation.sign; issue attestation.sign at Tenant for UnblindedPharm as well.
        await _scenario.Protocol.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(UnblindedPharm), "attestation.sign", null, AuthorityScope.Tenant(), Tick(), null, false, KernelProtocolScenario.Key(), "Test attestation grant"),
            ct);

        return new SeededWorld(
            code,
            tenant,
            study,
            versionId,
            design,
            ethicsVersion,
            rulePackId,
            systolicConcept,
            siteA,
            siteB,
            screenedA.EnrollmentId,
            screenedB.EnrollmentId,
            openedA.CollectionInstanceId,
            openedB.CollectionInstanceId);
    }

    private async Task RelayAsync(CancellationToken ct)
    {
        fixture.Clock.Set(SystemClock.Instance.UtcNow);
        for (var pass = 0; pass < 4; pass++)
        {
            var relayed = await fixture.OutboxRelay.RelayBatchAsync(500, ct);
            if (relayed.Failed > 0)
            {
                await using var db = fixture.CreateAdminContext();
                var errors = await db.OutboxMessages
                    .Where(m => m.LastError != null)
                    .Select(m => m.LastError!)
                    .ToListAsync(ct);
                relayed.Failed.Should().Be(0, string.Join(" | ", errors));
            }

            if (relayed.Dispatched == 0 && !relayed.MoreDue)
            {
                break;
            }
        }
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddDeclaredKernelErrorsCheck();
            }));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<JsonElement> McpCallAsync(HttpClient client, string toolName, object arguments, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(IntelligenceMcp.Route, UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = toolName, arguments } }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? body.Split('\n').Last(l => l.StartsWith("data:", StringComparison.Ordinal))["data:".Length..]
            : body;
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("error", out var rpcError).Should().BeFalse(rpcError.ToString());
        return doc.RootElement.GetProperty("result").Clone();
    }

    private static async Task<string> GetRawAsync(HttpClient client, string path, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return text;
    }

    private static async Task<string> SendRawAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? reason,
        object? body,
        HttpStatusCode expected,
        CancellationToken ct)
    {
        using var request = BuildJsonRequest(method, path, reason, body);
        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return text;
    }

    private static async Task<T> SendJsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        string? reason,
        object? body,
        HttpStatusCode expected,
        CancellationToken ct)
    {
        var text = await SendRawAsync(client, method, path, reason, body, expected, ct);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static HttpRequestMessage BuildJsonRequest(HttpMethod method, string path, string? reason, object? body)
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

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private sealed record SeededWorld(
        string Code,
        TenantId Tenant,
        StudyId Study,
        ProtocolVersionId Version,
        ProtocolDesign Design,
        ArtifactVersionId EthicsArtifactVersion,
        LibraryItemId RulePackId,
        ConceptRef SystolicConcept,
        StudySiteId SiteA,
        StudySiteId SiteB,
        EnrollmentId EnrollmentA,
        EnrollmentId EnrollmentB,
        CollectionInstanceId FormA,
        CollectionInstanceId FormB);
}
