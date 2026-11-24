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

using Cadence.Ddf;
using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// End-to-end PostgreSQL host tests verifying that USDM v4 and all 36 CDISC Library eCRF templates import item schemas
/// and pinned terminology bindings that pass <c>SubmitProtocolVersionForReview</c>, enforce HTTP 422 on observation
/// <c>ValueKind</c>/<c>Unit</c>/<c>CodeSystemVersionId</c> mismatches, keep non-empty forms with zero recorded values
/// incomplete in both form and casebook responses, and persist <c>FormVersionSnapshot</c> + <c>FormVersionPublished</c>
/// upon protocol approval.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProtocolImportValidationAndSnapshotPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Designer = "designer@protocol-import.test";
    private const string Crc = "crc@protocol-import.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    [Fact(DisplayName = "ProtocolImport_UsdmV4AndAll36Templates_PassReview_Enforce422Schemas_EnforceEmptyCompleteness_AndPersistFormSnapshot")]
    public async Task ProtocolImportUsdmV4AndAll36TemplatesPassReviewEnforce422SchemasEnforceEmptyCompletenessAndPersistFormSnapshot()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var tenantCode = $"T-IMP-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, tenantCode, "Protocol import & snapshot tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var (_, usdmStudy) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var (_, cdiscStudy) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, usdmStudy, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);

        foreach (var cap in new[] { "concept.define", "code_system.register", "code_system.import_version", "protocol.draft", "protocol.revise" })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(Designer), cap, null, AuthorityScope.Tenant(), _scenario.Tick(), null, false,
                    KernelProtocolScenario.Key(), "Designer grant"),
                ct);
        }

        foreach (var cap in new[] { "collection.open", "collection.record" })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(Crc), cap, null, AuthorityScope.StudySite(studySite), _scenario.Tick(), null, false,
                    KernelProtocolScenario.Key(), "Coordinator grant"),
                ct);
        }

        _scenario.Tick();

        using var designerClient = Client(tenantCode, Designer);
        using var crcClient = Client(tenantCode, Crc);

        // 1. Import USDM v4 Wrapper JSON with Quantity+UCUM unit, Coded responseCodes, Boolean, DateTime, and an all-optional activity.
        const string usdmV4Wrapper = """
            {
              "usdmVersion": "4.0.0",
              "study": {
                "id": "STUDY_IMP_V4",
                "name": "IMP-V4-101",
                "versions": [
                  {
                    "id": "SV_1",
                    "versionIdentifier": "1.0",
                    "titles": [
                      { "id": "T_1", "text": "Imported USDM v4 Clinical Study", "type": { "code": "C207616", "decode": "Official Study Title" } }
                    ],
                    "biomedicalConcepts": [
                      {
                        "id": "BC_SBP",
                        "name": "Systolic Blood Pressure",
                        "code": { "standardCode": { "code": "C25298", "decode": "Systolic Blood Pressure" } },
                        "properties": [
                          { "id": "P_SBP_VAL", "name": "VSORRES", "datatype": "decimal", "isRequired": true, "isEnabled": true },
                          {
                            "id": "P_SBP_UNIT",
                            "name": "VSORRESU",
                            "datatype": "string",
                            "isRequired": true,
                            "isEnabled": true,
                            "responseCodes": [
                              { "id": "RC_MMHG", "isEnabled": true, "code": { "code": "C49670", "decode": "Millimeter of Mercury" } }
                            ]
                          }
                        ]
                      },
                      {
                        "id": "BC_POS",
                        "name": "Body Position",
                        "code": { "standardCode": { "code": "C62164", "decode": "Body Position" } },
                        "properties": [
                          {
                            "id": "P_POS",
                            "name": "VSPOS",
                            "datatype": "string",
                            "isRequired": true,
                            "isEnabled": true,
                            "responseCodes": [
                              { "id": "RC_SIT", "isEnabled": true, "code": { "code": "SITTING", "decode": "Sitting" } },
                              { "id": "RC_SUP", "isEnabled": true, "code": { "code": "SUPINE", "decode": "Supine" } }
                            ]
                          }
                        ]
                      },
                      {
                        "id": "BC_FAST",
                        "name": "Fasting Status",
                        "code": { "standardCode": { "code": "C17056", "decode": "Fasting" } },
                        "properties": [
                          { "id": "P_FAST", "name": "VSFAST", "datatype": "boolean", "isRequired": false, "isEnabled": true }
                        ]
                      },
                      {
                        "id": "BC_NOTE",
                        "name": "Investigator Comment",
                        "code": { "standardCode": { "code": "C25393", "decode": "Comment" } },
                        "properties": [
                          { "id": "P_NOTE", "name": "COVAL", "datatype": "string", "isRequired": false, "isEnabled": true }
                        ]
                      }
                    ],
                    "studyDesigns": [
                      {
                        "id": "SD_1",
                        "epochs": [ { "id": "EP_1", "name": "Screening" } ],
                        "encounters": [ { "id": "ENC_1", "name": "Screening Visit" } ],
                        "activities": [
                          { "id": "ACT_VS", "name": "Vital Signs", "biomedicalConceptIds": [ "BC_SBP", "BC_POS", "BC_FAST" ] },
                          { "id": "ACT_NOTES", "name": "Optional Clinical Notes", "biomedicalConceptIds": [ "BC_NOTE" ] }
                        ],
                        "scheduleTimelines": [
                          {
                            "id": "TL_1",
                            "mainTimeline": true,
                            "instances": [
                              {
                                "id": "SAI_1",
                                "instanceType": "ScheduledActivityInstance",
                                "epochId": "EP_1",
                                "encounterId": "ENC_1",
                                "activityIds": [ "ACT_VS", "ACT_NOTES" ]
                              }
                            ],
                            "timings": [
                              { "id": "TIM_1", "type": { "code": "C201358", "decode": "Fixed Reference" }, "value": "P1D", "relativeFromScheduledInstanceId": "SAI_1" }
                            ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        string usdmStudyCode;
        string cdiscStudyCode;
        await using (var db = fixture.CreateContext(tenant))
        {
            usdmStudyCode = (await db.Studies.AsNoTracking().SingleAsync(s => s.Id == usdmStudy, ct)).StudyCode;
            cdiscStudyCode = (await db.Studies.AsNoTracking().SingleAsync(s => s.Id == cdiscStudy, ct)).StudyCode;
        }

        ProtocolVersionId usdmVersionId;
        using (var multipart = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(usdmV4Wrapper));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            multipart.Add(fileContent, "file", "study_v4.json");
            using var importReq = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/kernel/studies/{usdmStudyCode}/protocol-imports", UriKind.Relative))
            {
                Content = multipart,
            };
            importReq.Headers.Add("Idempotency-Key", KernelProtocolScenario.Key());
            importReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Import USDM v4 protocol"));
            using var importRes = await designerClient.SendAsync(importReq, ct);
            var importBody = await importRes.Content.ReadAsStringAsync(ct);
            importRes.StatusCode.Should().Be(HttpStatusCode.Created, importBody);
            using var doc = JsonDocument.Parse(importBody);
            var versionRef = NodeRef.Parse(doc.RootElement.GetProperty("version").GetProperty("ref").GetString()!);
            usdmVersionId = ProtocolVersionId.FromGuid(versionRef.Id);
        }

        // Also import all 36 bundled CDISC Library templates into cdiscStudy and verify SubmitProtocolVersionForReview passes UnknownReferenceAsync!
        var allTemplateIds = new CdiscLibraryTemplateCatalog().ListTemplates().Select(t => t.Id).ToArray();
        allTemplateIds.Should().HaveCount(36);
        ProtocolVersionId cdiscVersionId;
        using (var tplReq = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/kernel/studies/{cdiscStudyCode}/protocol-imports", UriKind.Relative))
        {
            Content = JsonContent.Create(new { template_ids = allTemplateIds }),
        })
        {
            tplReq.Headers.Add("Idempotency-Key", KernelProtocolScenario.Key());
            tplReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Import all 36 CDISC eCRF templates"));
            using var tplRes = await designerClient.SendAsync(tplReq, ct);
            var tplBody = await tplRes.Content.ReadAsStringAsync(ct);
            tplRes.StatusCode.Should().Be(HttpStatusCode.Created, tplBody);
            using var doc = JsonDocument.Parse(tplBody);
            var versionRef = NodeRef.Parse(doc.RootElement.GetProperty("version").GetProperty("ref").GetString()!);
            cdiscVersionId = ProtocolVersionId.FromGuid(versionRef.Id);
        }

        var cdiscInReview = await _scenario.Protocol.OkAsync(
            new SubmitProtocolVersionForReview(tenant, cdiscVersionId, KernelProtocolScenario.Key(), "Submit 36-template protocol"), ct);
        cdiscInReview.Status.Should().Be(ProtocolVersionStatus.InReview);

        // 2. Submit, attest, and approve the USDM v4 protocol version -> verify FormVersionSnapshot and FormVersionPublished!
        var usdmInReview = await _scenario.Protocol.OkAsync(
            new SubmitProtocolVersionForReview(tenant, usdmVersionId, KernelProtocolScenario.Key(), "Submit USDM v4 protocol"), ct);
        var approvalSig = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(usdmInReview)), ct);

        _scenario.Tick();
        var expectedApprovalRecordedAt = KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow);
        await _scenario.Protocol.OkAsync(
            new ApproveProtocolVersion(tenant, usdmVersionId, approvalSig, KernelProtocolScenario.Key(), "Approve USDM v4 protocol"), ct);

        Guid positionCodeSystemVersionId;
        await using (var db = fixture.CreateContext(tenant))
        {
            var storedProtocol = await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == usdmVersionId, ct);
            var design = ProtocolDesignDocument.Read(storedProtocol.Content).Design!;
            var vsSpec = design.CollectionSpecs.Single(s => s.Id == "ACT_VS");
            var posItem = vsSpec.Items.Single(i => i.Id == "ACT_VS.BC_POS");
            posItem.Terminology.Should().NotBeNull();
            positionCodeSystemVersionId = posItem.Terminology!.CodeSystemVersionId.Value;

            var snapshot = await db.FormVersionSnapshots.AsNoTracking().SingleAsync(s => s.ProtocolVersionId == usdmVersionId, ct);
            snapshot.OpenRosaXml.Should().NotBeNullOrWhiteSpace().And.Contain("<h:html");
            snapshot.DdfIr.Should().NotBeNullOrWhiteSpace().And.Contain("ACT_VS.BC_SBP");
            snapshot.OdmXml.Should().NotBeNullOrWhiteSpace().And.Contain("<ODM");
            ContentHash.IsWellFormed(snapshot.FormHash).Should().BeTrue();
            snapshot.RecordedAt.Should().Be(expectedApprovalRecordedAt);

            var eventTypes = await db.DomainEvents.AsNoTracking()
                .Where(e => e.TenantId == tenant)
                .Select(e => e.Type)
                .ToListAsync(ct);
            eventTypes.Should().Contain("FormVersionPublished");
        }

        (await fixture.LineageOrphanCheck.FindOrphansAsync(tenant, 100, ct)).Should().BeEmpty();
        (await fixture.AuditChainVerifier.VerifyAsync(tenant, ct)).IsIntact.Should().BeTrue();

        // Make the approved USDM v4 version effective so forms can be opened under it.
        var effectivityId = EffectivityId.New();
        await _scenario.Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivityId, usdmVersionId, EffectivityScope.Study(), _scenario.Tick(), KernelProtocolScenario.Key(), "Declare study effectivity"), ct);
        var ethicsBytes = Encoding.UTF8.GetBytes("ethics-approval");
        var ethicsVersion = ArtifactVersionId.New();
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                ethicsVersion,
                ArtifactKind.EthicsApproval,
                usdmStudy,
                "IRB Approval",
                new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://irb/usdm-v4"),
                KernelProtocolScenario.Key(),
                "Register IRB approval"),
            ct);
        await _scenario.Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivityId, EffectivityStatus.Effective, _scenario.Tick(), [ethicsVersion], KernelProtocolScenario.Key(), "Activate protocol"),
            ct);

        var collectedAt = _scenario.Tick();

        // 3. Open both ACT_VS (has required items) and ACT_NOTES (all optional items) with zero recorded values:
        //    both FormResponse and casebook FormSummaryResponse must return Complete == false!
        var vsOpened = await SendFormAsync<FormStateResponse>(crcClient, HttpMethod.Post, string.Empty, "Open Vital Signs", new
        {
            enrollment_id = enrollment.Value,
            collection_spec_id = "ACT_VS",
            planned_activity_id = "SAI_1",
            collected_at = collectedAt,
        }, HttpStatusCode.Created, ct);

        var notesOpened = await SendFormAsync<FormStateResponse>(crcClient, HttpMethod.Post, string.Empty, "Open Optional Notes", new
        {
            enrollment_id = enrollment.Value,
            collection_spec_id = "ACT_NOTES",
            planned_activity_id = "SAI_1",
            collected_at = collectedAt,
        }, HttpStatusCode.Created, ct);

        var emptyNotesForm = await SendFormAsync<FormResponse>(crcClient, HttpMethod.Get, $"/{notesOpened.FormId:D}", null, null, HttpStatusCode.OK, ct);
        emptyNotesForm.MissingRequired.Should().BeEmpty();
        emptyNotesForm.Complete.Should().BeFalse("an opened non-empty form with zero recorded values must be incomplete");

        var initialSummaries = await SendFormAsync<FormSummaryResponse[]>(crcClient, HttpMethod.Get, $"?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);
        initialSummaries.Single(s => s.FormId == vsOpened.FormId).Complete.Should().BeFalse();
        var emptyNotesSummary = initialSummaries.Single(s => s.FormId == notesOpened.FormId);
        emptyNotesSummary.MissingRequiredCount.Should().Be(0);
        emptyNotesSummary.RecordedCount.Should().Be(0);
        emptyNotesSummary.Complete.Should().BeFalse();

        // Recording one optional value on ACT_NOTES marks it Complete in both form and casebook responses.
        await SendFormAsync<FormSavedResponse>(crcClient, HttpMethod.Put, $"/{notesOpened.FormId:D}/values", "Record note", new
        {
            values = new[]
            {
                new { item_id = "ACT_NOTES.BC_NOTE", value = (object)new { type = "text", value = "Baseline assessment unremarkable" }, expected_revision = 0 },
            },
        }, HttpStatusCode.OK, ct);
        (await SendFormAsync<FormResponse>(crcClient, HttpMethod.Get, $"/{notesOpened.FormId:D}", null, null, HttpStatusCode.OK, ct)).Complete.Should().BeTrue();
        (await SendFormAsync<FormSummaryResponse[]>(crcClient, HttpMethod.Get, $"?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct))
            .Single(s => s.FormId == notesOpened.FormId).Complete.Should().BeTrue();

        // 4. RecordCollectedValuesHandler returns HTTP 422 when observations violate ValueKind, Unit, or CodeSystemVersionId/codelist.
        // 4a. ValueKind violation: text instead of quantity on ACT_VS.BC_SBP
        using (var wrongKind = await crcClient.SendAsync(FormRequest(HttpMethod.Put, $"/{vsOpened.FormId:D}/values", "Wrong ValueKind", new
        {
            values = new[]
            {
                new { item_id = "ACT_VS.BC_SBP", value = (object)new { type = "text", value = "120" }, expected_revision = 0 },
            },
        }), ct))
        {
            wrongKind.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(wrongKind, ct)).Should().Be(CollectionErrors.ValuesInvalid.Code);
        }

        // 4b. Unit violation: kPa instead of mm[Hg] on ACT_VS.BC_SBP
        using (var wrongUnit = await crcClient.SendAsync(FormRequest(HttpMethod.Put, $"/{vsOpened.FormId:D}/values", "Wrong Unit", new
        {
            values = new[]
            {
                new { item_id = "ACT_VS.BC_SBP", value = (object)new { type = "quantity", value = 120m, unit = "kPa" }, expected_revision = 0 },
            },
        }), ct))
        {
            wrongUnit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(wrongUnit, ct)).Should().Be(CollectionErrors.ValuesInvalid.Code);
        }

        // 4c. CodeSystemVersionId violation: unpinned CodeSystemVersionId on ACT_VS.BC_POS
        using (var wrongCsVersion = await crcClient.SendAsync(FormRequest(HttpMethod.Put, $"/{vsOpened.FormId:D}/values", "Wrong CodeSystemVersionId", new
        {
            values = new[]
            {
                new
                {
                    item_id = "ACT_VS.BC_POS",
                    value = (object)new { type = "coded", code_system_version_id = Guid.CreateVersion7(), code = "SITTING" },
                    expected_revision = 0,
                },
            },
        }), ct))
        {
            wrongCsVersion.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(wrongCsVersion, ct)).Should().Be(CollectionErrors.ValuesInvalid.Code);
        }

        // 4d. Codelist membership violation: unknown code in pinned CodeSystemVersionId on ACT_VS.BC_POS
        using (var wrongCode = await crcClient.SendAsync(FormRequest(HttpMethod.Put, $"/{vsOpened.FormId:D}/values", "Unknown code", new
        {
            values = new[]
            {
                new
                {
                    item_id = "ACT_VS.BC_POS",
                    value = (object)new { type = "coded", code_system_version_id = positionCodeSystemVersionId, code = "INVERTED" },
                    expected_revision = 0,
                },
            },
        }), ct))
        {
            wrongCode.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(wrongCode, ct)).Should().Be(CollectionErrors.ValuesInvalid.Code);
        }

        // 4e. Valid observations matching ValueKind, Unit, and CodeSystemVersionId save with 200 OK and complete the form!
        await SendFormAsync<FormSavedResponse>(crcClient, HttpMethod.Put, $"/{vsOpened.FormId:D}/values", "Record valid vitals", new
        {
            values = new object[]
            {
                new { item_id = "ACT_VS.BC_SBP", value = (object)new { type = "quantity", value = 122m, unit = "mm[Hg]" }, expected_revision = 0 },
                new
                {
                    item_id = "ACT_VS.BC_POS",
                    value = (object)new { type = "coded", code_system_version_id = positionCodeSystemVersionId, code = "SITTING" },
                    expected_revision = 0,
                },
                new { item_id = "ACT_VS.BC_FAST", value = (object)new { type = "boolean", value = true }, expected_revision = 0 },
            },
        }, HttpStatusCode.OK, ct);

        var completedVs = await SendFormAsync<FormResponse>(crcClient, HttpMethod.Get, $"/{vsOpened.FormId:D}", null, null, HttpStatusCode.OK, ct);
        completedVs.Complete.Should().BeTrue();
        completedVs.MissingRequired.Should().BeEmpty();
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                services.AddSingleton<IClock>(fixture.Clock);
                services.AddDeclaredKernelErrorsCheck();
            }));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> SendFormAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(FormRequest(method, path, reason, body), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static HttpRequestMessage FormRequest(HttpMethod method, string path, string? reason, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri($"{FormEndpoints.RoutePrefix}{path}", UriKind.Relative));
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

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
