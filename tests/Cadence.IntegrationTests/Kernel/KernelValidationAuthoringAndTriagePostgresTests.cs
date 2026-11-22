using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Execution;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// PostgreSQL integration tests for in-place rule pack authoring (<c>POST /api/v1/kernel/studies/{studyRef}/rule-packs</c>),
/// library item deprecation (<c>POST /api/v1/kernel/library-items/{itemRef}/deprecate</c>), post-commit validation runs
/// (<c>POST /api/v1/kernel/studies/{studyRef}/validation/runs</c>), and all five <c>FindingDecisionHandler</c>
/// triage routes with <see cref="FindingDecisionContent.Hash"/> step-up signature binding.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelValidationAuthoringAndTriagePostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Designer = "designer@validation-canvas.test";
    private const string Crc = "crc@validation-canvas.test";
    private const string Monitor = "cra@validation-canvas.test";
    private const string DataManager = "dm@validation-canvas.test";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    [Fact(DisplayName = "DraftApprovePinRunAndTriage_EnforcesKRule2KFind3AndSignedFindingDecisionHash")]
    [KernelRule("K-RULE-1")]
    [KernelRule("K-RULE-2")]
    [KernelRule("K-FIND-3")]
    public async Task DraftApprovePinRunAndTriageEnforcesKRule2KFind3AndSignedFindingDecisionHash()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenantCode, studyId, formId) = await SeedStudyAndGrantsAsync(ct);
        var studyRef = NodeRef.For(studyId).ToString();

        using var designer = Client(tenantCode, Designer);
        using var crc = Client(tenantCode, Crc);
        using var monitor = Client(tenantCode, Monitor);
        using var dm = Client(tenantCode, DataManager);

        // 1. Missing X-Change-Reason or Idempotency-Key fails closed with 422.
        var validPackPayload = BuildStudyRulePackPayload("Study Data Validation Plan", 220);
        using (var missingHeaders = await designer.PostAsJsonAsync(
            $"/api/v1/kernel/studies/{studyRef}/rule-packs",
            new { key = "canvas-bp-checks", title = "Canvas BP Edit Checks", content = validPackPayload },
            ct))
        {
            missingHeaders.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        // 2. K-RULE-2: Proprietary validator citations ("Pinnacle 21") are rejected.
        var forbiddenCitationPayload = BuildStudyRulePackPayload("Pinnacle 21 Enterprise Rule Catalog", 220);
        using (var kRule2Rejected = await designer.SendAsync(
            BuildKernelRequest(
                HttpMethod.Post,
                $"/api/v1/kernel/studies/{studyRef}/rule-packs",
                "Attempt proprietary citation",
                sigToken: null,
                new { key = "canvas-bp-checks", title = "Canvas BP Edit Checks", content = forbiddenCitationPayload }),
            ct))
        {
            kRule2Rejected.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await kRule2Rejected.Content.ReadAsStringAsync(ct)).Should().Contain("K-RULE-2");
        }

        // 3. Draft v1, approve v1, draft v2 revision, approve v2, pin v2, and deprecate unpinned v1.
        var draftedV1 = await SendKernelAsync<KernelRulePackResponse>(
            designer,
            HttpMethod.Post,
            $"/api/v1/kernel/studies/{studyRef}/rule-packs",
            "Draft initial study BP checks",
            sigToken: null,
            new { key = "canvas-bp-checks", title = "Canvas BP Edit Checks", content = validPackPayload },
            HttpStatusCode.Created,
            ct);
        draftedV1.Version.Should().Be(1);
        draftedV1.Status.Should().Be("Draft");

        var approveV1Token = TestSecurityContext.CreateSigToken(
            Designer,
            KernelValidationEndpoints.ApproveLibraryItemAction(draftedV1.Ref),
            KernelValidationEndpoints.ApproveLibraryItemSemanticAction);
        var approvedV1 = await SendKernelAsync<KernelRulePackResponse>(
            designer,
            HttpMethod.Post,
            $"/api/v1/kernel/library-items/{draftedV1.Ref}/approve",
            "Approve v1 rule pack",
            approveV1Token,
            new { meaning = "Approved" },
            HttpStatusCode.OK,
            ct);
        approvedV1.Status.Should().Be("Approved");

        var draftedV2 = await SendKernelAsync<KernelRulePackResponse>(
            designer,
            HttpMethod.Post,
            $"/api/v1/kernel/studies/{studyRef}/rule-packs",
            "Revise BP threshold to 210 mm[Hg]",
            sigToken: null,
            new
            {
                key = "canvas-bp-checks",
                title = "Canvas BP Edit Checks v2",
                content = BuildStudyRulePackPayload("Study Data Validation Plan", 210),
            },
            HttpStatusCode.Created,
            ct);
        draftedV2.Version.Should().Be(2);

        var approveV2Token = TestSecurityContext.CreateSigToken(
            Designer,
            KernelValidationEndpoints.ApproveLibraryItemAction(draftedV2.Ref),
            KernelValidationEndpoints.ApproveLibraryItemSemanticAction);
        var approvedV2 = await SendKernelAsync<KernelRulePackResponse>(
            designer,
            HttpMethod.Post,
            $"/api/v1/kernel/library-items/{draftedV2.Ref}/approve",
            "Approve v2 rule pack",
            approveV2Token,
            new { meaning = "Approved" },
            HttpStatusCode.OK,
            ct);
        approvedV2.Status.Should().Be("Approved");

        await SendKernelAsync<KernelStudyRulePacksResponse>(
            designer,
            HttpMethod.Put,
            $"/api/v1/kernel/studies/{studyRef}/standards",
            "Pin v2 rule pack on study",
            sigToken: null,
            new { rule_pack_refs = new[] { approvedV2.Ref } },
            HttpStatusCode.OK,
            ct);

        var deprecateV1Token = TestSecurityContext.CreateSigToken(
            Designer,
            KernelValidationEndpoints.DeprecateLibraryItemAction(approvedV1.Ref),
            KernelValidationEndpoints.DeprecateLibraryItemSemanticAction);
        var deprecatedV1 = await SendKernelAsync<KernelRulePackResponse>(
            designer,
            HttpMethod.Post,
            $"/api/v1/kernel/library-items/{approvedV1.Ref}/deprecate",
            "Deprecate superseded v1 rule pack",
            deprecateV1Token,
            new { meaning = "Responsibility" },
            HttpStatusCode.OK,
            ct);
        deprecatedV1.Status.Should().Be("Deprecated");

        // 4. K-FIND-3: Recording an out-of-range clinical observation succeeds unblocked, then validation run detects it.
        using (var saveObs = await crc.SendAsync(
            BuildKernelRequest(
                HttpMethod.Put,
                $"{FormEndpoints.RoutePrefix}/{formId:D}/values",
                "Initial vitals entry",
                sigToken: null,
                new
                {
                    values = new object[]
                    {
                        new { item_id = Systolic, value = new { type = "quantity", value = 232m, unit = "mm[Hg]" }, expected_revision = 0 },
                        new { item_id = Diastolic, value = new { type = "quantity", value = 84m, unit = "mm[Hg]" }, expected_revision = 0 },
                    },
                }),
            ct))
        {
            saveObs.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var runResponse = await SendKernelAsync<KernelStudyValidationRunResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/studies/{studyRef}/validation/runs",
            "Run post-commit collection checks",
            sigToken: null,
            new { },
            HttpStatusCode.OK,
            ct);
        runResponse.Detected.Should().Be(1);
        runResponse.New.Should().Be(1);

        var openFinding = runResponse.Findings.Should().ContainSingle().Subject;
        openFinding.RuleId.Should().Be("VS_SBP_CANVAS_MAX");
        openFinding.Status.Should().Be("Open");
        openFinding.Blocks.Should().BeFalse();

        var parsedFindingNode = NodeRef.Parse(openFinding.Ref);
        var findingId = ValidationFindingId.FromGuid(parsedFindingNode.Id);
        var parsedRunNode = NodeRef.Parse(openFinding.LastDetectedRunRef);
        var lastRunId = ValidationRunId.FromGuid(parsedRunNode.Id);

        // 5. Exercise all 5 FindingDecisionHandler routes with step-up hash binding on Accept and Explain.
        var investigating = await SendKernelAsync<KernelValidationFindingResponse>(
            dm,
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{openFinding.Ref}/investigate",
            "Begin data manager review",
            sigToken: null,
            body: null,
            HttpStatusCode.OK,
            ct);
        investigating.Status.Should().Be("Investigating");

        var classified = await SendKernelAsync<KernelValidationFindingResponse>(
            dm,
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{openFinding.Ref}/classify",
            "Classify discrepancy root cause",
            sigToken: null,
            new { classification = "DataIssue" },
            HttpStatusCode.OK,
            ct);
        classified.Classification.Should().Be("DataIssue");

        const string acceptRationale = "Verified baseline hypertensive reading on source chart.";
        var unhashedAcceptToken = TestSecurityContext.CreateSigToken(
            Monitor,
            KernelValidationEndpoints.AcceptFindingAction(openFinding.Ref),
            KernelValidationEndpoints.AcceptFindingSemanticAction,
            batchId: "sha256:0000000000000000000000000000000000000000000000000000000000000000");
        using (var wrongBatchAccept = await monitor.SendAsync(
            BuildKernelRequest(
                HttpMethod.Post,
                $"/api/v1/kernel/validation/findings/{openFinding.Ref}/accept",
                "Attempt mismatched hash token",
                unhashedAcceptToken,
                new { rationale = acceptRationale, last_detected_run_ref = openFinding.LastDetectedRunRef }),
            ct))
        {
            wrongBatchAccept.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var acceptHash = FindingDecisionContent.Hash(findingId, lastRunId, FindingStatus.Accepted, acceptRationale);
        var validAcceptToken = TestSecurityContext.CreateSigToken(
            Monitor,
            KernelValidationEndpoints.AcceptFindingAction(openFinding.Ref),
            KernelValidationEndpoints.AcceptFindingSemanticAction,
            batchId: acceptHash);
        var accepted = await SendKernelAsync<KernelValidationFindingResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{openFinding.Ref}/accept",
            "Accept finding after source verification",
            validAcceptToken,
            new { rationale = acceptRationale, last_detected_run_ref = openFinding.LastDetectedRunRef },
            HttpStatusCode.OK,
            ct);
        accepted.Status.Should().Be("Accepted");
        accepted.DecisionText.Should().Be(acceptRationale);

        var reopened = await SendKernelAsync<KernelValidationFindingResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{openFinding.Ref}/reopen",
            "Reopen to record clinical explanation",
            sigToken: null,
            body: null,
            HttpStatusCode.OK,
            ct);
        reopened.Status.Should().Be("Open");

        const string clinicalExplanation = "Transient elevation immediately post-venipuncture.";
        var explainHash = FindingDecisionContent.Hash(findingId, lastRunId, FindingStatus.Explained, clinicalExplanation);
        var validExplainToken = TestSecurityContext.CreateSigToken(
            DataManager,
            KernelValidationEndpoints.ExplainFindingAction(openFinding.Ref),
            KernelValidationEndpoints.ExplainFindingSemanticAction,
            batchId: explainHash);
        var explained = await SendKernelAsync<KernelValidationFindingResponse>(
            dm,
            HttpMethod.Post,
            $"/api/v1/kernel/validation/findings/{openFinding.Ref}/explain",
            "Sign clinical explanation",
            validExplainToken,
            new { explanation = clinicalExplanation, last_detected_run_ref = openFinding.LastDetectedRunRef },
            HttpStatusCode.OK,
            ct);
        explained.Status.Should().Be("Explained");
        explained.DecisionText.Should().Be(clinicalExplanation);

        var explainedPage = await SendKernelAsync<KernelValidationFindingPageResponse>(
            monitor,
            HttpMethod.Get,
            $"/api/v1/kernel/studies/{studyRef}/validation/findings?status=Explained",
            reason: null,
            sigToken: null,
            body: null,
            HttpStatusCode.OK,
            ct);
        explainedPage.Findings.Should().ContainSingle(f => f.Ref == openFinding.Ref && f.Status == "Explained");
    }

    private static object BuildStudyRulePackPayload(string citationDocument, int maxSystolic) =>
        new
        {
            authority = "Study",
            title = "Canvas BP Edit Checks",
            rules = new object[]
            {
                new
                {
                    id = "VS_SBP_CANVAS_MAX",
                    version = 1,
                    authority = "Study",
                    citation = new { document = citationDocument, section = "Section 4.1" },
                    applicability = new { stage = "Collection", dataset = SpecId },
                    severity = "Warning",
                    condition = new
                    {
                        op = ">",
                        args = new object[] { new { @var = Systolic }, maxSystolic },
                    },
                    explanation = $"Systolic BP exceeds {maxSystolic} mm[Hg].",
                    remediation = "Confirm measurement against vital signs worksheet.",
                },
            },
        };

    private async Task<(string Code, StudyId Study, Guid Form)> SeedStudyAndGrantsAsync(CancellationToken ct)
    {
        fixture.Clock.Set(SystemClock.Instance.UtcNow.AddHours(-1));
        var code = $"T-VC-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Validation canvas tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        await _scenario.EffectiveVersionAsync(tenant, study, ct);

        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);

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
                    "DOM1",
                    "Domain One",
                    "Findings",
                    "One per visit",
                    1,
                    [
                        new VariableDefinitionData(
                            "VAR1",
                            "Primary Value",
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
        await _scenario.Protocol.OkAsync(new RegisterStandardVersion(tenant, standardId, standardPkg, null, KernelProtocolScenario.Key(), "Register standard"), ct);
        var stdView = (await fixture.Standards.FindStandardVersionAsync(tenant, standardId, ct))!;
        var stdAttest = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.StandardVersion, standardId.Value, stdView.ContentHash)), ct);
        await _scenario.Protocol.OkAsync(new ApproveStandardVersion(tenant, standardId, stdAttest, KernelProtocolScenario.Key(), "Approve standard"), ct);
        await _scenario.Protocol.OkAsync(
            new DeclareStudyStandards(tenant, study, StudyStandardsProfileId.New(), [standardId], [], [], KernelProtocolScenario.Key(), "Initial standards"), ct);

        foreach (var (actor, capabilities, scope) in new (Actor, string[], AuthorityScope)[]
        {
            (Actor.Person(Designer), ["study.view", "library_item.draft", "library_item.approve", "library_item.deprecate", "study.declare_standards", "attestation.sign"], AuthorityScope.Tenant()),
            (Actor.Person(Monitor), ["attestation.sign"], AuthorityScope.Tenant()),
            (Actor.Person(DataManager), ["attestation.sign"], AuthorityScope.Tenant()),
            (Actor.Person(Crc), ["study.view", "collection.open", "collection.record"], AuthorityScope.StudySite(studySite)),
            (Actor.Person(Monitor), ["study.view", "validation_run.record", "validation_finding.accept", "validation_finding.reopen"], AuthorityScope.Study(study)),
            (Actor.Person(DataManager), ["study.view", "validation_finding.investigate", "validation_finding.classify", "validation_finding.explain"], AuthorityScope.Study(study)),
        })
        {
            foreach (var cap in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), actor, cap, null, scope, _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Test grant"),
                    ct);
            }
        }

        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
        var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        return (code, study, opened.CollectionInstanceId.Value);
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services => services.AddDeclaredKernelErrorsCheck()));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> SendKernelAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        string? reason,
        string? sigToken,
        object? body,
        HttpStatusCode expected,
        CancellationToken ct)
    {
        using var response = await client.SendAsync(BuildKernelRequest(method, path, reason, sigToken, body), ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, payload);
        return JsonSerializer.Deserialize<T>(payload, WebJson)!;
    }

    private static HttpRequestMessage BuildKernelRequest(
        HttpMethod method,
        string path,
        string? reason,
        string? sigToken,
        object? body)
    {
        var req = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (method != HttpMethod.Get)
        {
            req.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        }

        if (reason is not null)
        {
            req.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (sigToken is not null)
        {
            req.Headers.Add(GatewayHeaders.SigToken, sigToken);
        }

        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        return req;
    }
}
