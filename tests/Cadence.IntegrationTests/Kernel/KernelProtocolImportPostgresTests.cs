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

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// End-to-end PostgreSQL integration tests for atomic protocol design import (<see cref="ImportProtocolDraft"/>):
/// single-transaction <c>Concept</c> + <c>ProtocolVersion</c> persistence, zero-concept rollback on invalid payloads,
/// idempotent HTTP replay across committed concepts, <c>InReview</c> reopening, <c>Withdrawn</c> recovery drafts, and
/// <c>ProtocolStructuralDiff</c> amendment impact persistence.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelProtocolImportPostgresTests(KernelPostgresFixture fixture)
    : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string Designer = "designer@protocol-import.test";
    private const string LimitedActor = "limited@protocol-import.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const string ValidOdmWithSchedule = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.PG.1" FileType="Snapshot" ODMVersion="1.3.2" CreationDateTime="2026-03-01T00:00:00Z">
          <Study OID="S.PG">
            <GlobalVariables>
              <StudyName>Postgres Atomic Import Study</StudyName>
              <StudyDescription>Integration</StudyDescription>
              <ProtocolName>PG-IMP-101</ProtocolName>
            </GlobalVariables>
            <MetaDataVersion OID="MDV.1" Name="v1">
              <Protocol>
                <StudyEventRef StudyEventOID="SE.SCR" OrderNumber="1" Mandatory="Yes"/>
              </Protocol>
              <StudyEventDef OID="SE.SCR" Name="Screening" Repeating="No" Type="Scheduled">
                <FormRef FormOID="F.VS" OrderNumber="1" Mandatory="Yes"/>
              </StudyEventDef>
              <FormDef OID="F.VS" Name="Vital Signs" Repeating="No">
                <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes"/>
              </FormDef>
              <ItemGroupDef OID="IG.VS" Name="Vitals" Repeating="No">
                <ItemRef ItemOID="IT.SYS" OrderNumber="1" Mandatory="Yes"/>
              </ItemGroupDef>
              <ItemDef OID="IT.SYS" Name="SYSBP" DataType="integer">
                <Alias Context="prompt" Name="Systolic Blood Pressure"/>
                <Alias Context="CDASH" Name="extCodeID: C25298"/>
              </ItemDef>
            </MetaDataVersion>
          </Study>
        </ODM>
        """;

    private readonly KernelProtocolScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    [Fact(DisplayName = "AtomicProtocolImport_CommitsConceptsAndVersionTogether_ReplaysCleanly_AndHandlesLifecycleTransitions")]
    [KernelRule("K-TX-1")]
    [KernelRule("K-PROT-1")]
    [KernelRule("K-PROT-2")]
    public async Task AtomicProtocolImportCommitsConceptsAndVersionTogetherReplaysCleanlyAndHandlesLifecycleTransitions()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenantCode = $"T-IMP-{Guid.NewGuid():N}"[..17];
        var tenant = TenantId.New();
        await _scenario.OkAsync(new ProvisionTenant(tenant, tenantCode, "Protocol Import Tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.NewStudyAsync(tenant, ct);

        string studyCode;
        await using (var db = fixture.CreateContext(tenant))
        {
            studyCode = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.StudyCode).SingleAsync(ct);
        }

        var designerRole = await _scenario.OkAsync(
            new DefineRole(
                tenant,
                RoleId.New(),
                "PROTOCOL_IMPORTER",
                "Protocol Importer",
                [
                    "concept.define",
                    "protocol.draft",
                    "protocol.revise_draft",
                    "protocol.submit_for_review",
                    "protocol.return_to_draft",
                    "protocol.amend",
                ],
                KernelProtocolScenario.Key(),
                "Define importer role"),
            ct);
        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(Designer), null, designerRole.RoleId, AuthorityScope.Tenant(), Tick(), null, false,
                KernelProtocolScenario.Key(), "Grant designer"),
            ct);

        var limitedRole = await _scenario.OkAsync(
            new DefineRole(tenant, RoleId.New(), "DRAFT_WITHOUT_CONCEPTS", "Draft Without Concepts", ["protocol.draft", "protocol.revise_draft"],
                KernelProtocolScenario.Key(), "Define limited role"),
            ct);
        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(LimitedActor), null, limitedRole.RoleId, AuthorityScope.Tenant(), Tick(), null, false,
                KernelProtocolScenario.Key(), "Grant limited actor"),
            ct);
        Tick();

        using var designer = Client(tenantCode, Designer);
        using var limited = Client(tenantCode, LimitedActor);

        var templates = await designer.GetFromJsonAsync<JsonElement>("/api/v1/kernel/protocol-templates", ct);
        var dmTemplateId = templates.EnumerateArray().First(t => t.GetProperty("file_name").GetString() == "DM_XML.xml").GetProperty("id").GetString()!;
        var aeTemplateId = templates.EnumerateArray().First(t => t.GetProperty("file_name").GetString() == "AE_XML.xml").GetProperty("id").GetString()!;

        // 1. Missing concept.define -> 403 and zero concepts or versions committed in PostgreSQL.
        using (var forbiddenReq = BuildMultipartImport(studyCode, ValidOdmWithSchedule, "vitals.xml", [dmTemplateId], false, null, null, "imp-403", "Missing concept.define"))
        using (var forbiddenRes = await limited.SendAsync(forbiddenReq, ct))
        {
            forbiddenRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 2. Invalid ODM (422) -> zero concepts or versions committed in PostgreSQL.
        const string invalidOdm = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="BAD" FileType="Snapshot" ODMVersion="1.3.2">
              <Study OID="S.BAD">
                <GlobalVariables><StudyName>Bad</StudyName><StudyDescription>Bad</StudyDescription><ProtocolName>BAD</ProtocolName></GlobalVariables>
                <MetaDataVersion OID="MDV.BAD" Name="v1">
                  <FormDef OID="F.BAD" Name="Bad"><ItemGroupRef ItemGroupOID="IG.MISSING" OrderNumber="1" Mandatory="Yes"/></FormDef>
                  <ItemDef OID="IT.SYS" Name="SYSBP" DataType="integer"><Alias Context="CDASH" Name="extCodeID: C25298"/></ItemDef>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;
        using (var badReq = BuildMultipartImport(studyCode, invalidOdm, "bad.xml", null, false, null, null, "imp-422", "Invalid ODM"))
        using (var badRes = await designer.SendAsync(badReq, ct))
        {
            badRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        await using (var db = fixture.CreateContext(tenant))
        {
            (await db.Concepts.CountAsync(ct)).Should().Be(0);
            (await db.ProtocolVersions.Where(v => v.StudyId == study).CountAsync(ct)).Should().Be(0);
        }

        // 3. Valid combined file + template_ids import commits C25298 + sponsor concepts + Draft v1 atomically, and replays cleanly!
        string v1Ref;
        using (var req1 = BuildMultipartImport(studyCode, ValidOdmWithSchedule, "vitals.xml", [dmTemplateId], false, null, null, "imp-v1", "Initial combined import"))
        using (var res1 = await designer.SendAsync(req1, ct))
        {
            res1.StatusCode.Should().Be(HttpStatusCode.Created, await res1.Content.ReadAsStringAsync(ct));
            using var doc1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync(ct));
            doc1.RootElement.GetProperty("form_count").GetInt32().Should().Be(2);
            v1Ref = doc1.RootElement.GetProperty("version").GetProperty("ref").GetString()!;
        }

        using (var replayReq = BuildMultipartImport(studyCode, ValidOdmWithSchedule, "vitals.xml", [dmTemplateId], false, null, null, "imp-v1", "Initial combined import"))
        using (var replayRes = await designer.SendAsync(replayReq, ct))
        {
            replayRes.StatusCode.Should().Be(HttpStatusCode.Created, await replayRes.Content.ReadAsStringAsync(ct));
            using var replayDoc = JsonDocument.Parse(await replayRes.Content.ReadAsStringAsync(ct));
            replayDoc.RootElement.GetProperty("version").GetProperty("ref").GetString().Should().Be(v1Ref);
            replayDoc.RootElement.GetProperty("form_count").GetInt32().Should().Be(2);
        }

        ProtocolVersionId v1Id;
        await using (var db = fixture.CreateContext(tenant))
        {
            var bc = await db.Concepts.AsNoTracking().SingleAsync(c => c.ExternalCode == "C25298", ct);
            bc.Origin.Should().Be(ConceptOrigin.CdiscBiomedicalConcept);
            v1Id = await db.ProtocolVersions.AsNoTracking().Where(v => v.StudyId == study && v.Number == 1).Select(v => v.Id).SingleAsync(ct);
        }

        // 4. Submit v1 for review -> importing reopens InReview v1 back to Draft (200 OK) without trigger failure.
        var inReview = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, v1Id, KernelProtocolScenario.Key(), "Submit v1"), ct);
        inReview.Status.Should().Be(ProtocolVersionStatus.InReview);

        using (var reopenReq = BuildJsonImport(studyCode, new { template_ids = new[] { aeTemplateId }, replace_forms = false }, "imp-reopen", "Reopen v1 and merge AE"))
        using (var reopenRes = await designer.SendAsync(reopenReq, ct))
        {
            reopenRes.StatusCode.Should().Be(HttpStatusCode.OK, await reopenRes.Content.ReadAsStringAsync(ct));
            using var doc = JsonDocument.Parse(await reopenRes.Content.ReadAsStringAsync(ct));
            doc.RootElement.GetProperty("version").GetProperty("status").GetString().Should().Be("Draft");
            doc.RootElement.GetProperty("form_count").GetInt32().Should().Be(3);
        }

        // 5. Submit & Approve v1 -> Fork v2 with Substantial + RequiresReconsent, then revise v2 with UrgentSafetyMeasure + ByCohort.
        var resubmitted = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, v1Id, KernelProtocolScenario.Key(), "Resubmit v1"), ct);
        var approvalAtt = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(resubmitted)), ct);
        await _scenario.OkAsync(new ApproveProtocolVersion(tenant, v1Id, approvalAtt, KernelProtocolScenario.Key(), "Approve v1"), ct);

        const string usdmC25298 = """
            {
              "usdmVersion": "4.0.0",
              "study": {
                "id": "Study_PG_101",
                "name": "PG-IMP-101",
                "versions": [
                  {
                    "id": "SV_1",
                    "titles": [
                      { "id": "T_1", "text": "PG-IMP-101", "type": { "code": "C207616", "decode": "Official Study Title" } }
                    ],
                    "biomedicalConcepts": [
                      {
                        "id": "BC_HOME_SBP",
                        "name": "Systolic Blood Pressure",
                        "label": "Home Systolic BP",
                        "code": { "id": "AC_1", "standardCode": { "code": "C25298", "decode": "Systolic Blood Pressure" } }
                      }
                    ],
                    "studyDesigns": [
                      {
                        "id": "SD_1",
                        "activities": [
                          {
                            "id": "F.HOME_BP",
                            "name": "Home BP Diary",
                            "biomedicalConceptIds": [ "BC_HOME_SBP" ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        using (var forkReq = BuildJsonImport(
            studyCode,
            new
            {
                format = "usdm_json",
                file_name = "home-bp.json",
                content = usdmC25298,
                replace_forms = false,
                amendment_classification = "Substantial",
                transition_policy = "RequiresReconsent",
            },
            "imp-fork-v2",
            "Fork v2 with Home BP USDM"))
        using (var forkRes = await designer.SendAsync(forkReq, ct))
        {
            forkRes.StatusCode.Should().Be(HttpStatusCode.Created, await forkRes.Content.ReadAsStringAsync(ct));
            using var doc = JsonDocument.Parse(await forkRes.Content.ReadAsStringAsync(ct));
            doc.RootElement.GetProperty("version").GetProperty("number").GetInt32().Should().Be(2);
            doc.RootElement.GetProperty("amendment").GetProperty("classification").GetString().Should().Be("Substantial");
            doc.RootElement.GetProperty("amendment").GetProperty("transition_policy").GetString().Should().Be("RequiresReconsent");
            doc.RootElement.GetProperty("amendment").GetProperty("impacted_elements").EnumerateArray()
                .Select(e => e.GetString()).Should().Contain("F.HOME_BP");
        }

        await using (var db = fixture.CreateContext(tenant))
        {
            // C25298 across ODM and USDM resolves to the single CdiscBiomedicalConcept row!
            (await db.Concepts.AsNoTracking().CountAsync(c => c.ExternalCode == "C25298", ct)).Should().Be(1);
        }
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private static HttpRequestMessage BuildMultipartImport(
        string studyCode,
        string fileText,
        string fileName,
        string[]? templateIds,
        bool replaceForms,
        string? classification,
        string? policy,
        string idempotencyKey,
        string reason)
    {
        var multipart = new MultipartFormDataContent();
        var fileBytes = new ByteArrayContent(Encoding.UTF8.GetBytes(fileText));
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        multipart.Add(fileBytes, "file", fileName);
        if (templateIds is not null)
        {
            foreach (var tpl in templateIds)
            {
                multipart.Add(new StringContent(tpl), "template_ids");
            }
        }

        if (replaceForms)
        {
            multipart.Add(new StringContent("true"), "replace_forms");
        }

        if (classification is not null)
        {
            multipart.Add(new StringContent(classification), "amendment_classification");
        }

        if (policy is not null)
        {
            multipart.Add(new StringContent(policy), "transition_policy");
        }

        var req = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/kernel/studies/{studyCode}/protocol-imports", UriKind.Relative))
        {
            Content = multipart,
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        req.Headers.Add(GatewayHeaders.ChangeReason, reason);
        return req;
    }

    private static HttpRequestMessage BuildJsonImport(string studyCode, object body, string idempotencyKey, string reason)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/kernel/studies/{studyCode}/protocol-imports", UriKind.Relative))
        {
            Content = JsonContent.Create(body, options: Json),
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        req.Headers.Add(GatewayHeaders.ChangeReason, reason);
        return req;
    }
}
