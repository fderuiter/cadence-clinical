using System;
using System.Collections.Generic;
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
using Cadence.Kernel.Activity;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Intelligence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Search;
using Cadence.Kernel.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.UnitTests.Kernel.Api;

/// <summary>
/// HTTP endpoint tests for <c>KernelStudyImportEndpoints</c> hosted in the real <c>Cadence.Kernel.Host.Program</c>:
/// visible study listing, self-service study creation, idempotent retries, signed reason-for-change enforcement,
/// authorization checks, synchronous ODM v1.3.2 XML / USDM v4 JSON / bundled CDISC template import into Draft status,
/// 10 MB payload limits, and DTD/XXE blocking.
/// </summary>
[Trait("Category", "Unit")]
public sealed class KernelStudyImportEndpointsTests : IDisposable
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly GatewayIdentity DesignerIdentity = TestSecurityContext.Identity("designer.lead", "Study Designer");
    private static readonly GatewayIdentity CoordinatorIdentity = TestSecurityContext.Identity("crc.site101", "Clinical Research Coordinator");
    private static readonly GatewayIdentity StrangerIdentity = TestSecurityContext.Identity("stranger.user", "Viewer");

    private const string SampleOdmXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.TEST.1" FileType="Snapshot" ODMVersion="1.3.2" CreationDateTime="2026-02-01T00:00:00Z" Originator="Sponsor EDC">
          <Study OID="S.CADENCE">
            <GlobalVariables>
              <StudyName>Cardiovascular Outcomes Protocol</StudyName>
              <StudyDescription>Demo</StudyDescription>
              <ProtocolName>CADENCE-101</ProtocolName>
            </GlobalVariables>
            <MetaDataVersion OID="MDV.1" Name="Draft 1">
              <FormDef OID="F.VS" Name="Vital Signs" Repeating="No">
                <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes"/>
              </FormDef>
              <ItemGroupDef OID="IG.VS" Name="Vital Signs Measurements" Repeating="No">
                <ItemRef ItemOID="IT.DIA" OrderNumber="2" Mandatory="No"/>
                <ItemRef ItemOID="IT.SYS" OrderNumber="1" Mandatory="Yes"/>
              </ItemGroupDef>
              <ItemDef OID="IT.SYS" Name="SYSBP" DataType="integer">
                <Alias Context="prompt" Name="Systolic Blood Pressure"/>
                <Alias Context="CDASH" Name="extCodeID: C25298"/>
              </ItemDef>
              <ItemDef OID="IT.DIA" Name="DIABP" DataType="integer">
                <Alias Context="prompt" Name="Diastolic Blood Pressure"/>
              </ItemDef>
            </MetaDataVersion>
          </Study>
        </ODM>
        """;

    private readonly InMemoryImportWorld _world = new(Tenant);
    private readonly List<WebApplicationFactory<Cadence.Kernel.Host.Program>> _factories = [];

    public KernelStudyImportEndpointsTests()
    {
        _world.GrantRole(
            Actor.Person(DesignerIdentity.UserId),
            "DESIGNER",
            ["organization.register", "study.register", "concept.define", "protocol.draft", "protocol.revise_draft", "protocol.submit_for_review", "protocol.amend"],
            AuthorityScope.Tenant());

        _world.GrantRole(
            Actor.Person(CoordinatorIdentity.UserId),
            "CRC",
            ["observation.record"],
            AuthorityScope.Study(_world.DemoStudyId));
    }

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    [Fact]
    public async Task ListStudies_FiltersByCallerVisibility_AndExposesDemoStudy()
    {
        var ct = TestContext.Current.CancellationToken;
        var otherStudy = StudyId.New();
        _world.Studies.Add(new StudyDirectoryEntry(
            otherStudy, 1, "HIDDEN-999", "Unassigned Oncology Study", "Phase 1",
            _world.DemoSponsorId, 1, "CADENCE-PHARMA", "Cadence Pharma", 0));

        using var designerClient = CreateClient(DesignerIdentity);
        var designerStudies = await designerClient.GetFromJsonAsync<JsonElement>("/api/v1/kernel/studies", ct);
        designerStudies.EnumerateArray().Select(s => s.GetProperty("code").GetString())
            .Should().Contain(["CADENCE-101", "HIDDEN-999"]);

        using var coordinatorClient = CreateClient(CoordinatorIdentity);
        var coordinatorStudies = await coordinatorClient.GetFromJsonAsync<JsonElement>("/api/v1/kernel/studies", ct);
        coordinatorStudies.EnumerateArray().Select(s => s.GetProperty("code").GetString())
            .Should().Equal("CADENCE-101");

        using var strangerClient = CreateClient(StrangerIdentity);
        var strangerStudies = await strangerClient.GetFromJsonAsync<JsonElement>("/api/v1/kernel/studies", ct);
        strangerStudies.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task CreateStudy_EnforcesPermissionsReasonAndIdempotency_AndAppearsInStudyList()
    {
        var ct = TestContext.Current.CancellationToken;

        // 1. Coordinator lacks study.register -> 403 Forbidden.
        using var crcClient = CreateClient(CoordinatorIdentity);
        using (var deniedReq = JsonPost("/api/v1/kernel/studies", new
        {
            study_code = "CARDIO-202",
            title = "Phase 2 Heart Failure Study",
            phase = "Phase 2",
            sponsor_name = "Acme Therapeutics",
        }, idempotencyKey: "study-create-crc", reason: "Attempt unauthorized creation"))
        {
            using var deniedRes = await crcClient.SendAsync(deniedReq, ct);
            deniedRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 2. Designer without X-Change-Reason -> 422 K.Command.ReasonForChangeRequired.
        using var designerClient = CreateClient(DesignerIdentity);
        using (var noReasonReq = JsonPost("/api/v1/kernel/studies", new
        {
            study_code = "CARDIO-202",
            title = "Phase 2 Heart Failure Study",
            phase = "Phase 2",
            sponsor_name = "Acme Therapeutics",
        }, idempotencyKey: "study-create-no-reason", reason: null))
        {
            using var noReasonRes = await designerClient.SendAsync(noReasonReq, ct);
            noReasonRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await noReasonRes.Content.ReadAsStringAsync(ct)).Should().Contain("K.Command.ReasonForChangeRequired");
        }

        // 3. Designer with signed X-Change-Reason and Idempotency-Key -> 201 Created, and replay returns identical study without duplicate.
        string createdRef;
        using (var createReq = JsonPost("/api/v1/kernel/studies", new
        {
            study_code = "CARDIO-202",
            title = "Phase 2 Heart Failure Study",
            phase = "Phase 2",
            sponsor_name = "Acme Therapeutics",
        }, idempotencyKey: "study-create-202", reason: "Register CARDIO-202 for Phase 2 protocol authoring"))
        {
            using var createRes = await designerClient.SendAsync(createReq, ct);
            createRes.StatusCode.Should().Be(HttpStatusCode.Created, await createRes.Content.ReadAsStringAsync(ct));
            var doc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync(ct)).RootElement;
            createdRef = doc.GetProperty("ref").GetString()!;
            doc.GetProperty("code").GetString().Should().Be("CARDIO-202");
            doc.GetProperty("sponsor_name").GetString().Should().Be("Acme Therapeutics");
        }

        int countAfterFirst = _world.Studies.Count;
        using (var replayReq = JsonPost("/api/v1/kernel/studies", new
        {
            study_code = "CARDIO-202",
            title = "Phase 2 Heart Failure Study",
            phase = "Phase 2",
            sponsor_name = "Acme Therapeutics",
        }, idempotencyKey: "study-create-202", reason: "Register CARDIO-202 for Phase 2 protocol authoring"))
        {
            using var replayRes = await designerClient.SendAsync(replayReq, ct);
            replayRes.StatusCode.Should().Be(HttpStatusCode.Created);
            var replayDoc = JsonDocument.Parse(await replayRes.Content.ReadAsStringAsync(ct)).RootElement;
            replayDoc.GetProperty("ref").GetString().Should().Be(createdRef);
        }

        _world.Studies.Count.Should().Be(countAfterFirst, "duplicate Idempotency-Key must not create duplicate studies");
    }

    [Fact]
    public async Task ImportProtocolDraft_OdmMultipartTemplatesAndApprovedFork_AlwaysCreatesOrRevisesDraftIdempotently()
    {
        var ct = TestContext.Current.CancellationToken;
        using var designerClient = CreateClient(DesignerIdentity);
        using var crcClient = CreateClient(CoordinatorIdentity);

        // Bundled templates endpoint returns all 36 CDISC Library templates.
        var templates = await designerClient.GetFromJsonAsync<JsonElement>("/api/v1/kernel/protocol-templates", ct);
        templates.GetArrayLength().Should().Be(36);
        var dmTemplateId = templates.EnumerateArray()
            .First(t => t.GetProperty("file_name").GetString() == "DM_XML.xml")
            .GetProperty("id").GetString()!;

        // Coordinator is denied protocol import before any concept is defined.
        using (var crcImport = MultipartOdmPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", SampleOdmXml, "vs.xml", "crc-import-1", "Unauthorized import"))
        {
            using var crcRes = await crcClient.SendAsync(crcImport, ct);
            crcRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            _world.Concepts.Should().BeEmpty();
        }

        // Designer uploads ODM v1.3.2 XML -> 201 Created in Draft status with preserved order, mandatory flags, and import notes.
        string versionRef;
        using (var firstImport = MultipartOdmPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", SampleOdmXml, "vs.xml", "odm-import-1", "Initial ODM import"))
        {
            using var res = await designerClient.SendAsync(firstImport, ct);
            res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync(ct));
            var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement;
            versionRef = body.GetProperty("version").GetProperty("ref").GetString()!;
            body.GetProperty("version").GetProperty("status").GetString().Should().Be("Draft");
            body.GetProperty("version").GetProperty("number").GetInt32().Should().Be(1);
            body.GetProperty("form_count").GetInt32().Should().Be(1);
            var form = body.GetProperty("forms")[0];
            form.GetProperty("name").GetString().Should().Be("Vital Signs");
            form.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("prompt").GetString())
                .Should().Equal("Systolic Blood Pressure", "Diastolic Blood Pressure");
            form.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("required").GetBoolean())
                .Should().Equal(true, false);
            body.GetProperty("import_notes").GetArrayLength().Should().BeGreaterThan(0);
        }

        // Duplicate request with same Idempotency-Key returns same Draft v1 without creating v2.
        using (var replayImport = MultipartOdmPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", SampleOdmXml, "vs.xml", "odm-import-1", "Initial ODM import"))
        {
            using var replayRes = await designerClient.SendAsync(replayImport, ct);
            replayRes.StatusCode.Should().Be(HttpStatusCode.Created, await replayRes.Content.ReadAsStringAsync(ct));
            var replayBody = JsonDocument.Parse(await replayRes.Content.ReadAsStringAsync(ct)).RootElement;
            replayBody.GetProperty("version").GetProperty("ref").GetString().Should().Be(versionRef);
            _world.Versions.Should().ContainSingle();
        }

        // Adding a bundled CDISC template to the existing Draft revises v1 in place (200 OK) with 2 forms.
        using (var addTemplate = JsonPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", new
        {
            template_ids = new[] { dmTemplateId },
        }, idempotencyKey: "tpl-import-2", reason: "Add CDISC Demographics eCRF"))
        {
            using var tplRes = await designerClient.SendAsync(addTemplate, ct);
            tplRes.StatusCode.Should().Be(HttpStatusCode.OK, await tplRes.Content.ReadAsStringAsync(ct));
            var tplBody = JsonDocument.Parse(await tplRes.Content.ReadAsStringAsync(ct)).RootElement;
            tplBody.GetProperty("version").GetProperty("status").GetString().Should().Be("Draft");
            tplBody.GetProperty("form_count").GetInt32().Should().Be(2);
        }

        // Design summary GET endpoint returns the viewable forms, items, and notes.
        var summary = await designerClient.GetFromJsonAsync<JsonElement>(
            $"/api/v1/kernel/protocol-versions/{Uri.EscapeDataString(versionRef)}/design-summary", ct);
        summary.GetProperty("form_count").GetInt32().Should().Be(2);

        // Once v1 is Approved, importing creates a forked v2 in Draft status (never bypassing review/approval).
        _world.MarkVersionApproved(_world.Versions.Single().Summary.Id);
        await AssertForkedDraftCreatedAsync(designerClient, dmTemplateId, ct);
    }

    private static async Task AssertForkedDraftCreatedAsync(HttpClient designerClient, string dmTemplateId, CancellationToken ct)
    {
        using var forkImport = JsonPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", new
        {
            template_ids = new[] { dmTemplateId },
        }, idempotencyKey: "fork-import-3", reason: "Import amendment draft");
        using var forkRes = await designerClient.SendAsync(forkImport, ct);
        forkRes.StatusCode.Should().Be(HttpStatusCode.Created, await forkRes.Content.ReadAsStringAsync(ct));
        var forkBody = JsonDocument.Parse(await forkRes.Content.ReadAsStringAsync(ct)).RootElement;
        forkBody.GetProperty("version").GetProperty("number").GetInt32().Should().Be(2);
        forkBody.GetProperty("version").GetProperty("status").GetString().Should().Be("Draft");
    }

    [Fact]
    public async Task ImportProtocolDraft_RejectsOversizedXxeAndMalformedPayloadsWithProblemDetails()
    {
        var ct = TestContext.Current.CancellationToken;
        using var designerClient = CreateClient(DesignerIdentity);

        // > 10 MB upload -> 413 Payload Too Large.
        var hugeBytes = new byte[10 * 1024 * 1024 + 128];
        Array.Fill(hugeBytes, (byte)'a');
        using (var hugeReq = MultipartBytesPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", hugeBytes, "huge.xml", "huge-1", "Test limit"))
        {
            using var hugeRes = await designerClient.SendAsync(hugeReq, ct);
            hugeRes.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
            hugeRes.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        }

        // XXE / DOCTYPE upload -> 422 ProblemDetails.
        const string xxe = "<?xml version=\"1.0\"?><!DOCTYPE ODM [<!ENTITY xxe SYSTEM \"file:///etc/hostname\">]><ODM>&xxe;</ODM>";
        using (var xxeReq = MultipartOdmPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", xxe, "xxe.xml", "xxe-1", "Test XXE"))
        {
            using var xxeRes = await designerClient.SendAsync(xxeReq, ct);
            xxeRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await xxeRes.Content.ReadAsStringAsync(ct)).Should().Contain("DTD");
        }

        // Malformed JSON upload -> 422 ProblemDetails, zero 500s or 404s.
        var badJson = Encoding.UTF8.GetBytes("{not valid json");
        using var badReq = MultipartBytesPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", badJson, "broken.json", "bad-json-1", "Test broken JSON");
        using var badRes = await designerClient.SendAsync(badReq, ct);
        badRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        badRes.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    private HttpClient CreateClient(GatewayIdentity identity)
    {
        var factory = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestGatewaySecret().UseTestBiometricLivenessKey().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITenantDirectory>();
                services.AddSingleton<ITenantDirectory>(_world);
                services.RemoveAll<IAuthorityStore>();
                services.AddSingleton<IAuthorityStore>(_world);
                services.RemoveAll<IStudyDirectory>();
                services.AddSingleton<IStudyDirectory>(_world);
                services.RemoveAll<IProtocolVersionReader>();
                services.AddSingleton<IProtocolVersionReader>(_world);
                services.RemoveAll<IConceptRegistry>();
                services.AddSingleton<IConceptRegistry>(_world);
                services.RemoveAll<IKernelCommandSender>();
                services.AddScoped<IKernelCommandSender>(sp =>
                    new InMemoryImportCommandSender(_world, sp.GetRequiredService<ICurrentActor>(), sp.GetRequiredService<IAuthorizer>()));

                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    services.Remove(hosted);
                }
            });
        });
        _factories.Add(factory);
        return factory.CreateSignedClient(identity);
    }

    private static HttpRequestMessage JsonPost(string path, object payload, string idempotencyKey, string? reason)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        if (reason is not null)
        {
            req.Headers.Add(GatewayHeaders.ChangeReason, reason);
        }

        return req;
    }

    private static HttpRequestMessage MultipartOdmPost(string path, string xml, string fileName, string idempotencyKey, string? reason) =>
        MultipartBytesPost(path, Encoding.UTF8.GetBytes(xml), fileName, idempotencyKey, reason);

    private static HttpRequestMessage MultipartBytesPost(string path, byte[] bytes, string fileName, string idempotencyKey, string? reason)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".json", StringComparison.Ordinal) ? "application/json" : "application/xml");
        multipart.Add(fileContent, "file", fileName);

        var req = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = multipart,
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        if (reason is not null)
        {
            req.Headers.Add(GatewayHeaders.ChangeReason, reason);
        }

        return req;
    }

    private sealed class InMemoryImportWorld :
        ITenantDirectory,
        IAuthorityStore,
        IStudyDirectory,
        IProtocolVersionReader,
        IConceptRegistry
    {
        private readonly TenantId _tenant;

        public InMemoryImportWorld(TenantId tenant)
        {
            _tenant = tenant;
            DemoSponsorId = OrganizationId.New();
            DemoStudyId = StudyId.New();
            Organizations.Add(new OrganizationDirectoryEntry(DemoSponsorId, 1, "CADENCE-PHARMA", "Cadence Pharma (demo sponsor)", OrganizationKind.Sponsor));
            Studies.Add(new StudyDirectoryEntry(DemoStudyId, 1, "CADENCE-101", "Cadence demo study: cardiovascular outcomes", "Phase 3", DemoSponsorId, 1, "CADENCE-PHARMA", "Cadence Pharma (demo sponsor)", 2));
        }

        public OrganizationId DemoSponsorId { get; }

        public StudyId DemoStudyId { get; }

        public List<Grant> Grants { get; } = [];

        public List<Role> DefinedRoles { get; } = [];

        public List<OrganizationDirectoryEntry> Organizations { get; } = [];

        public List<StudyDirectoryEntry> Studies { get; } = [];

        public List<SiteDirectoryEntry> Sites { get; } = [];

        public List<StudySiteDirectoryEntry> StudySites { get; } = [];

        public List<ConceptVersionView> Concepts { get; } = [];

        public List<StoredVersion> Versions { get; } = [];

        public Dictionary<string, object> IdempotentResults { get; } = new(StringComparer.Ordinal);

        public void GrantRole(Actor grantee, string code, string[] capabilities, AuthorityScope scope)
        {
            var role = Role.Define(RoleId.New(), _tenant, code, code, capabilities).Value!;
            DefinedRoles.Add(role);
            var grant = Grant.Issue(GrantId.New(), _tenant, grantee, null, role.Id, scope, Epoch, null, false, null, Actor.ServiceAccount("seed"), Epoch).Value!;
            Grants.Add(grant);
        }

        public void MarkVersionApproved(ProtocolVersionId versionId)
        {
            var idx = Versions.FindIndex(v => v.Summary.Id == versionId);
            if (idx >= 0)
            {
                var current = Versions[idx];
                Versions[idx] = new StoredVersion(current.Summary with { Status = ProtocolVersionStatus.Approved, ContentHash = "sha256:approved" }, current.Content);
            }
        }

        public Task<TenantId?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(code == TestSecurityContext.TenantCode ? (TenantId?)_tenant : null);

        public Task<IReadOnlyList<Grant>> GrantsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Grant>>([.. Grants.Where(g => g.Grantee == actor)]);

        public Task<Grant?> FindGrantAsync(TenantId tenant, GrantId grant, CancellationToken cancellationToken) =>
            Task.FromResult(Grants.SingleOrDefault(g => g.Id == grant));

        public Task<IReadOnlyList<Role>> RolesAsync(TenantId tenant, IReadOnlyCollection<RoleId> roles, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Role>>([.. DefinedRoles.Where(r => roles.Contains(r.Id))]);

        public Task<ScopePath> PathOfAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken)
        {
            if (resource.Kind == NodeKinds.Study)
            {
                var s = Studies.FirstOrDefault(x => x.StudyId == StudyId.FromGuid(resource.Id));
                if (s is not null)
                {
                    return Task.FromResult(new ScopePath(s.SponsorId, s.StudyId, null, null));
                }
            }

            if (resource.Kind == NodeKinds.ProtocolVersion)
            {
                var v = Versions.FirstOrDefault(x => x.Summary.Id == ProtocolVersionId.FromGuid(resource.Id));
                if (v is not null)
                {
                    var s = Studies.First(x => x.StudyId == v.Summary.StudyId);
                    return Task.FromResult(new ScopePath(s.SponsorId, s.StudyId, null, null));
                }
            }

            return Task.FromResult(ScopePath.TenantWide);
        }

        public Task<IReadOnlyList<ScopePath>> StudySitePathsAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScopePath>>([]);

        public Task<IReadOnlyList<Qualification>> QualificationsHeldAsync(TenantId tenant, Actor actor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Qualification>>([]);

        public Task<IReadOnlyList<QualificationRequirement>> QualificationRequirementsAsync(TenantId tenant, string capability, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QualificationRequirement>>([]);

        public Task<IReadOnlyList<StudyDirectoryEntry>> ListStudiesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyDirectoryEntry>>([.. Studies.OrderBy(s => s.StudyCode, StringComparer.Ordinal)]);

        public Task<StudyDirectoryEntry?> FindStudyAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Studies.FirstOrDefault(s => s.StudyId == studyId));

        public Task<StudyDirectoryEntry?> FindStudyByCodeAsync(TenantId tenant, string studyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Studies.FirstOrDefault(s => string.Equals(s.StudyCode, studyCode, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<OrganizationDirectoryEntry>> ListOrganizationsAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationDirectoryEntry>>([.. Organizations.OrderBy(o => o.Code, StringComparer.Ordinal)]);

        public Task<OrganizationDirectoryEntry?> FindOrganizationAsync(TenantId tenant, OrganizationId organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Organizations.FirstOrDefault(o => o.OrganizationId == organizationId));

        public Task<OrganizationDirectoryEntry?> FindOrganizationByCodeAsync(TenantId tenant, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(Organizations.FirstOrDefault(o => string.Equals(o.Code, code, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<SiteDirectoryEntry>> ListSitesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SiteDirectoryEntry>>([.. Sites.OrderBy(s => s.SiteCode, StringComparer.Ordinal)]);

        public Task<SiteDirectoryEntry?> FindSiteAsync(TenantId tenant, SiteId siteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sites.FirstOrDefault(s => s.SiteId == siteId));

        public Task<SiteDirectoryEntry?> FindSiteByCodeAsync(TenantId tenant, string siteCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sites.FirstOrDefault(s => string.Equals(s.SiteCode, siteCode, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<StudySiteDirectoryEntry>> ListStudySitesAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudySiteDirectoryEntry>>([.. StudySites.Where(ss => ss.StudyId == studyId).OrderBy(ss => ss.SiteCode, StringComparer.Ordinal)]);

        public Task<StudySiteDirectoryEntry?> FindStudySiteAsync(TenantId tenant, StudySiteId studySiteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(StudySites.FirstOrDefault(ss => ss.StudySiteId == studySiteId));

        public Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProtocolVersionSummary>>([.. Versions.Where(v => v.Summary.StudyId == study).OrderByDescending(v => v.Summary.Number).Select(v => v.Summary)]);

        public Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult(Versions.FirstOrDefault(v => v.Summary.Id == version)?.Summary);

        public Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult<SoaMatrix?>(null);

        public Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken)
        {
            var found = Versions.FirstOrDefault(v => v.Summary.Id == version);
            if (found is null)
            {
                return Task.FromResult<ProtocolDesign?>(null);
            }

            var parsed = ProtocolDesignDocument.Read(found.Content);
            return Task.FromResult(parsed.IsValid ? parsed.Design : null);
        }

        public Task<ConceptVersionView?> ResolveAsync(TenantId tenant, ConceptRef concept, CancellationToken cancellationToken = default) =>
            Task.FromResult(Concepts.FirstOrDefault(c => c.Concept == concept));

        public Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default) =>
            Task.FromResult(Concepts.FirstOrDefault(c => c.Concept.ConceptId == concept));

        public Task<ConceptVersionView?> FindBiomedicalConceptAsync(TenantId tenant, string cdiscConceptCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Concepts.FirstOrDefault(c => c.Origin == ConceptOrigin.CdiscBiomedicalConcept && c.ExternalCode == cdiscConceptCode));

        public Task<CodeSystemVersionView?> FindCodeSystemVersionAsync(TenantId tenant, string codeSystemUri, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult<CodeSystemVersionView?>(null);

        public Task<IReadOnlyList<CodeSystemEntryView>> LookupAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeSystemEntryView>>([]);

        public Task<IReadOnlyList<CodeSystemEntryView>> GetGroupAsync(TenantId tenant, CodeSystemVersionId version, string groupCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeSystemEntryView>>([]);

        public Task<IReadOnlyList<ConceptMappingView>> ResolveCodingAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConceptMappingView>>([]);

        public Task<IReadOnlyList<ConceptMappingView>> GetMappingsAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConceptMappingView>>([]);
    }

    private sealed class InMemoryImportCommandSender(
        InMemoryImportWorld world,
        ICurrentActor currentActor,
        IAuthorizer authorizer) : IKernelCommandSender
    {
        public async Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                return CommandResult<TResult>.Failure(KernelErrors.IdempotencyKeyRequired);
            }

            if (string.IsNullOrWhiteSpace(command.ReasonForChange))
            {
                return CommandResult<TResult>.Failure(KernelErrors.ReasonForChangeRequired);
            }

            if (currentActor.Actor is not { } actor)
            {
                return CommandResult<TResult>.Failure(KernelErrors.ActorUnresolved);
            }

            var auth = await authorizer.AuthorizeAsync(actor, command, cancellationToken).ConfigureAwait(false);
            if (!auth.Permitted)
            {
                return CommandResult<TResult>.Failure(KernelErrors.AuthorizationDenied);
            }

            if (world.IdempotentResults.TryGetValue(command.IdempotencyKey, out var cached))
            {
                return CommandResult<TResult>.Success((TResult)cached);
            }

            object outcome = command switch
            {
                RegisterOrganization regOrg => ExecuteRegisterOrg(regOrg),
                RegisterStudy regStudy => ExecuteRegisterStudy(regStudy),
                RegisterSite regSite => ExecuteRegisterSite(regSite),
                LinkSiteToStudy linkSite => ExecuteLinkSiteToStudy(linkSite),
                DefineConcept defConcept => ExecuteDefineConcept(defConcept),
                CreateProtocolDraft createDraft => ExecuteCreateDraft(createDraft),
                ReviseProtocolDraft reviseDraft => ExecuteReviseDraft(reviseDraft),
                ForkProtocolVersion forkVersion => ExecuteForkVersion(forkVersion),
                _ => throw new NotSupportedException($"Unhandled command {command.GetType().Name}."),
            };

            if (outcome is KernelError err)
            {
                return CommandResult<TResult>.Failure(err);
            }

            world.IdempotentResults[command.IdempotencyKey] = outcome;
            return CommandResult<TResult>.Success((TResult)outcome);
        }

        private object ExecuteRegisterOrg(RegisterOrganization cmd)
        {
            if (world.Organizations.Any(o => string.Equals(o.Code, cmd.Code, StringComparison.OrdinalIgnoreCase)))
            {
                return KernelErrors.AlreadyExists;
            }

            world.Organizations.Add(new OrganizationDirectoryEntry(cmd.OrganizationId, 1, cmd.Code, cmd.Name, cmd.Kind));
            return cmd.OrganizationId;
        }

        private object ExecuteRegisterStudy(RegisterStudy cmd)
        {
            if (world.Studies.Any(s => string.Equals(s.StudyCode, cmd.Code, StringComparison.OrdinalIgnoreCase)))
            {
                return StudyErrors.StudyCodeTaken;
            }

            var sponsor = world.Organizations.FirstOrDefault(o => o.OrganizationId == cmd.SponsorId);
            if (sponsor is null)
            {
                return StudyErrors.SponsorUnknown;
            }

            world.Studies.Add(new StudyDirectoryEntry(cmd.StudyId, 1, cmd.Code, cmd.Title, cmd.Phase, sponsor.OrganizationId, sponsor.VersionIndex, sponsor.Code, sponsor.Name, 0));
            return cmd.StudyId;
        }

        private object ExecuteRegisterSite(RegisterSite cmd)
        {
            if (world.Sites.Any(s => string.Equals(s.SiteCode, cmd.Code, StringComparison.OrdinalIgnoreCase)))
            {
                return StudyErrors.SiteCodeTaken;
            }

            var inst = world.Organizations.FirstOrDefault(o => o.OrganizationId == cmd.InstitutionId);
            if (inst is null)
            {
                return StudyErrors.InstitutionUnknown;
            }

            if (inst.Kind != OrganizationKind.SiteInstitution)
            {
                return StudyErrors.InstitutionUnknown;
            }

            world.Sites.Add(new SiteDirectoryEntry(cmd.SiteId, 1, inst.OrganizationId, inst.VersionIndex, inst.Code, inst.Name, cmd.Code, cmd.Name, cmd.Country));
            return cmd.SiteId;
        }

        private object ExecuteLinkSiteToStudy(LinkSiteToStudy cmd)
        {
            var study = world.Studies.FirstOrDefault(s => s.StudyId == cmd.StudyId);
            if (study is null)
            {
                return StudyErrors.StudyUnknown;
            }

            var site = world.Sites.FirstOrDefault(s => s.SiteId == cmd.SiteId);
            if (site is null)
            {
                return StudyErrors.SiteUnknown;
            }

            if (world.StudySites.Any(ss => ss.StudyId == cmd.StudyId && ss.SiteId == cmd.SiteId))
            {
                return StudyErrors.AlreadyLinked;
            }

            world.StudySites.Add(new StudySiteDirectoryEntry(
                cmd.StudySiteId,
                1,
                study.StudyId,
                site.SiteId,
                site.VersionIndex,
                site.SiteCode,
                site.Name,
                site.Country,
                site.InstitutionId,
                site.InstitutionVersionIndex,
                site.InstitutionCode,
                site.InstitutionName,
                "Selected"));
            return cmd.StudySiteId;
        }

        private object ExecuteDefineConcept(DefineConcept cmd)
        {
            var conceptRef = new ConceptRef(cmd.ConceptId, 1);
            world.Concepts.Add(new ConceptVersionView(
                conceptRef,
                cmd.Origin,
                cmd.ExternalCode,
                cmd.Kind,
                cmd.PreferredLabel,
                cmd.Definition,
                ConceptStatus.Active,
                1,
                ConceptMappingState.Unmapped,
                Epoch));
            return conceptRef;
        }

        private object ExecuteCreateDraft(CreateProtocolDraft cmd)
        {
            if (world.Versions.Any(v => v.Summary.StudyId == cmd.StudyId))
            {
                return ProtocolErrors.FirstVersionExists;
            }

            var summary = new ProtocolVersionSummary(cmd.ProtocolVersionId, cmd.StudyId, 1, null, ProtocolVersionStatus.Draft, null, null, null, null);
            world.Versions.Add(new StoredVersion(summary, cmd.Content));
            return new ProtocolVersionState(cmd.ProtocolVersionId, cmd.StudyId, 1, ProtocolVersionStatus.Draft, null);
        }

        private object ExecuteReviseDraft(ReviseProtocolDraft cmd)
        {
            var idx = world.Versions.FindIndex(v => v.Summary.Id == cmd.ProtocolVersionId);
            if (idx < 0)
            {
                return KernelErrors.NotFound;
            }

            var existing = world.Versions[idx];
            if (existing.Summary.Status != ProtocolVersionStatus.Draft)
            {
                return ProtocolErrors.InvalidTransition;
            }

            var updatedSummary = cmd.Amendment is not null
                ? existing.Summary with { AmendmentIntent = cmd.Amendment.Intent }
                : existing.Summary;
            world.Versions[idx] = new StoredVersion(updatedSummary, cmd.Content);
            return new ProtocolVersionState(updatedSummary.Id, updatedSummary.StudyId, updatedSummary.Number, updatedSummary.Status, updatedSummary.ContentHash);
        }

        private object ExecuteForkVersion(ForkProtocolVersion cmd)
        {
            var parent = world.Versions.FirstOrDefault(v => v.Summary.Id == cmd.ParentVersionId);
            if (parent is null)
            {
                return KernelErrors.NotFound;
            }

            int nextNum = world.Versions.Where(v => v.Summary.StudyId == parent.Summary.StudyId).Max(v => v.Summary.Number) + 1;
            var summary = new ProtocolVersionSummary(cmd.ProtocolVersionId, parent.Summary.StudyId, nextNum, parent.Summary.Id, ProtocolVersionStatus.Draft, null, cmd.Amendment.Intent, null, null);
            world.Versions.Add(new StoredVersion(summary, parent.Content));
            return new ProtocolVersionState(summary.Id, summary.StudyId, summary.Number, ProtocolVersionStatus.Draft, null);
        }
    }

    private sealed record StoredVersion(ProtocolVersionSummary Summary, string Content);
}
