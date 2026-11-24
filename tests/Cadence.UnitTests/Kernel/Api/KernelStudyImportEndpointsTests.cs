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
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
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
using Cadence.Testing;
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
            ["organization.register", "study.register", "site.register", "study_site.link", "concept.define", "code_system.register", "code_system.import_version", "protocol.draft", "protocol.revise_draft", "protocol.submit_for_review", "protocol.amend"],
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
    public async Task CreateSiteAndLinkToStudy_RegistersSiteInstitution_EnforcesHeaders_AndListsStudySites()
    {
        var ct = TestContext.Current.CancellationToken;
        using var crcClient = CreateClient(CoordinatorIdentity);
        using var designerClient = CreateClient(DesignerIdentity);

        // 1. Caller without site.register receives 403 Forbidden.
        using (var deniedSiteReq = JsonPost("/api/v1/kernel/sites", new
        {
            site_code = "SITE-01",
            name = "Boston General Hospital",
            country = "US",
            institution_name = "Boston General Health",
        }, idempotencyKey: "site-denied", reason: "Unauthorized site creation"))
        {
            using var deniedRes = await crcClient.SendAsync(deniedSiteReq, ct);
            deniedRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 2. Missing X-Change-Reason and missing Idempotency-Key fail closed with 422.
        using (var noReasonReq = JsonPost("/api/v1/kernel/sites", new
        {
            site_code = "SITE-01",
            name = "Boston General Hospital",
            country = "US",
            institution_name = "Boston General Health",
        }, idempotencyKey: "site-no-reason", reason: null))
        {
            using var noReasonRes = await designerClient.SendAsync(noReasonReq, ct);
            noReasonRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await noReasonRes.Content.ReadAsStringAsync(ct)).Should().Contain("K.Command.ReasonForChangeRequired");
        }

        using (var noIdemReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/kernel/sites")
        {
            Content = JsonContent.Create(new
            {
                site_code = "SITE-01",
                name = "Boston General Hospital",
                country = "US",
                institution_name = "Boston General Health",
            }),
        })
        {
            noIdemReq.Headers.Add(GatewayHeaders.ChangeReason, "Missing idempotency key");
            using var noIdemRes = await designerClient.SendAsync(noIdemReq, ct);
            noIdemRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await noIdemRes.Content.ReadAsStringAsync(ct)).Should().Contain("K.Command.IdempotencyKeyRequired");
        }

        // 3. Register site when institution_name matches existing Sponsor name ("Cadence Pharma") ->
        // disambiguates and registers OrganizationKind.SiteInstitution instead of reusing Sponsor!
        string siteRef;
        string institutionRef;
        using (var createSiteReq = JsonPost("/api/v1/kernel/sites", new
        {
            site_code = "SITE-01",
            name = "Cadence Boston Clinic",
            country = "us",
            institution_name = "Cadence Pharma",
        }, idempotencyKey: "site-create-01", reason: "Register primary Boston site"))
        {
            using var createSiteRes = await designerClient.SendAsync(createSiteReq, ct);
            createSiteRes.StatusCode.Should().Be(HttpStatusCode.Created, await createSiteRes.Content.ReadAsStringAsync(ct));
            var siteDoc = JsonDocument.Parse(await createSiteRes.Content.ReadAsStringAsync(ct)).RootElement;
            siteRef = siteDoc.GetProperty("ref").GetString()!;
            institutionRef = siteDoc.GetProperty("institution_ref").GetString()!;
            siteDoc.GetProperty("code").GetString().Should().Be("SITE-01");
            siteDoc.GetProperty("country").GetString().Should().Be("US");
            siteDoc.GetProperty("institution_code").GetString().Should().Be("CADENCE-PHARMA-SITE");
        }

        var siteInstitutionOrgs = await designerClient.GetFromJsonAsync<JsonElement>("/api/v1/kernel/organizations?kind=SiteInstitution", ct);
        siteInstitutionOrgs.EnumerateArray().Select(o => o.GetProperty("ref").GetString())
            .Should().Contain(institutionRef);

        var allSites = await designerClient.GetFromJsonAsync<JsonElement>("/api/v1/kernel/sites", ct);
        allSites.EnumerateArray().Select(s => s.GetProperty("code").GetString())
            .Should().Contain("SITE-01");

        // 4. Passing a Sponsor NodeRef as institution_ref is rejected with 422.
        using (var wrongKindReq = JsonPost("/api/v1/kernel/sites", new
        {
            site_code = "SITE-02",
            name = "Wrong Org Kind Site",
            country = "US",
            institution_ref = NodeRef.For(_world.DemoSponsorId).ToString(),
        }, idempotencyKey: "site-wrong-kind", reason: "Reject sponsor org ref for site"))
        {
            using var wrongKindRes = await designerClient.SendAsync(wrongKindReq, ct);
            wrongKindRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        // 5. Link SITE-01 to CADENCE-101 -> 201 Created, listed on GET /studies/CADENCE-101/sites for both designer and study-scoped coordinator.
        using (var linkNoReason = JsonPost("/api/v1/kernel/studies/CADENCE-101/sites", new
        {
            site_ref = siteRef,
        }, idempotencyKey: "link-no-reason", reason: null))
        {
            using var linkNoReasonRes = await designerClient.SendAsync(linkNoReason, ct);
            linkNoReasonRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        using (var linkReq = JsonPost("/api/v1/kernel/studies/CADENCE-101/sites", new
        {
            site_code = "SITE-01",
        }, idempotencyKey: "link-site-01", reason: "Link SITE-01 to CADENCE-101"))
        {
            using var linkRes = await designerClient.SendAsync(linkReq, ct);
            linkRes.StatusCode.Should().Be(HttpStatusCode.Created, await linkRes.Content.ReadAsStringAsync(ct));
            var linkDoc = JsonDocument.Parse(await linkRes.Content.ReadAsStringAsync(ct)).RootElement;
            linkDoc.GetProperty("site_code").GetString().Should().Be("SITE-01");
            linkDoc.GetProperty("study_code").GetString().Should().Be("CADENCE-101");
            linkDoc.GetProperty("status").GetString().Should().Be("Selected");
        }

        var linkedSitesForCrc = await crcClient.GetFromJsonAsync<JsonElement>("/api/v1/kernel/studies/CADENCE-101/sites", ct);
        linkedSitesForCrc.EnumerateArray().Select(s => s.GetProperty("site_code").GetString())
            .Should().Equal("SITE-01");
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

    [Fact]
    public async Task LifecycleVersionResolution_HandlesWithdrawnInReviewAndSupersededVersionsWithoutDeadlock()
    {
        var ct = TestContext.Current.CancellationToken;
        _world.GrantRole(
            Actor.Person(DesignerIdentity.UserId),
            "REVIEW_APPROVER",
            ["protocol.return_to_draft"],
            AuthorityScope.Tenant());

        using var designerClient = CreateClient(DesignerIdentity);
        var templates = await designerClient.GetFromJsonAsync<JsonElement>("/api/v1/kernel/protocol-templates", ct);
        var dmTemplateId = templates.EnumerateArray().First(t => t.GetProperty("file_name").GetString() == "DM_XML.xml").GetProperty("id").GetString()!;
        var aeTemplateId = templates.EnumerateArray().First(t => t.GetProperty("file_name").GetString() == "AE_XML.xml").GetProperty("id").GetString()!;

        // 1. Study with Withdrawn v1 -> creates recovery Draft v2 (201 Created).
        var withdrawnV1 = ProtocolVersionId.New();
        var initialDesign = ProtocolDesignDocument.Write(new ProtocolDesign
        {
            Title = "CADENCE-101",
            CollectionSpecs =
            [
                new CollectionSpec(
                    "F.OLD",
                    "Old Form",
                    [new CollectionItem("IT.OLD", new ConceptRef(ConceptId.New(), 1), Required: true, ValueKind: ObservationValueKind.Text, Prompt: "Old")]),
            ],
        });
        _world.Versions.Add(new StoredVersion(
            new ProtocolVersionSummary(withdrawnV1, _world.DemoStudyId, 1, null, ProtocolVersionStatus.Withdrawn, "sha256:v1", null, null, "Withdrawn before activation"),
            initialDesign));

        using (var req = MultipartOdmPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", SampleOdmXml, "vitals.xml", "rec-v2", "Create recovery draft after withdrawn v1"))
        {
            using var res = await designerClient.SendAsync(req, ct);
            res.StatusCode.Should().Be(HttpStatusCode.Created);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            doc.RootElement.GetProperty("version").GetProperty("number").GetInt32().Should().Be(2);
            doc.RootElement.GetProperty("version").GetProperty("status").GetString().Should().Be("Draft");
        }

        // 2. Mark v2 InReview -> importing reopens v2 to Draft (200 OK) and clears content_hash.
        var v2Id = _world.Versions.Single(v => v.Summary.Number == 2).Summary.Id;
        _world.SetVersionStatus(v2Id, ProtocolVersionStatus.InReview, "sha256:inreview");

        using (var req = JsonPost(
            "/api/v1/kernel/studies/CADENCE-101/protocol-imports",
            new { template_ids = new[] { dmTemplateId }, replace_forms = false },
            "reopen-v2",
            "Return in-review v2 to draft and merge DM"))
        {
            using var res = await designerClient.SendAsync(req, ct);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            doc.RootElement.GetProperty("version").GetProperty("number").GetInt32().Should().Be(2);
            doc.RootElement.GetProperty("version").GetProperty("status").GetString().Should().Be("Draft");
            doc.RootElement.GetProperty("version").GetProperty("content_hash").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // 3. Mark v2 Superseded and add Approved v3 -> importing forks Approved v3 into Draft v4.
        _world.SetVersionStatus(v2Id, ProtocolVersionStatus.Superseded, "sha256:v2");
        var v2Content = _world.Versions.Single(v => v.Summary.Id == v2Id).Content;
        var approvedV3 = ProtocolVersionId.New();
        _world.Versions.Add(new StoredVersion(
            new ProtocolVersionSummary(approvedV3, _world.DemoStudyId, 3, v2Id, ProtocolVersionStatus.Approved, "sha256:v3", "Amendment 1", null, null),
            v2Content));

        using (var req = JsonPost(
            "/api/v1/kernel/studies/CADENCE-101/protocol-imports",
            new { template_ids = new[] { aeTemplateId }, replace_forms = false },
            "fork-v4",
            "Fork approved v3 across superseded history"))
        {
            using var res = await designerClient.SendAsync(req, ct);
            res.StatusCode.Should().Be(HttpStatusCode.Created);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            doc.RootElement.GetProperty("version").GetProperty("number").GetInt32().Should().Be(4);
            doc.RootElement.GetProperty("version").GetProperty("parent_version").GetString().Should().StartWith($"protocol_version:{approvedV3}");
        }
    }

    [Fact]
    public async Task MissingReviseDraftOrConceptDefineCapability_Returns403BeforeAnyForkOrConceptCommit()
    {
        var ct = TestContext.Current.CancellationToken;
        var approvedV1 = ProtocolVersionId.New();
        var baseContent = ProtocolDesignDocument.Write(new ProtocolDesign
        {
            Title = "CADENCE-101",
            CollectionSpecs =
            [
                new CollectionSpec(
                    "F.BASE",
                    "Baseline",
                    [new CollectionItem("IT.BASE", new ConceptRef(ConceptId.New(), 1), Required: true, ValueKind: ObservationValueKind.Text, Prompt: "Baseline")]),
            ],
        });
        _world.Versions.Add(new StoredVersion(
            new ProtocolVersionSummary(approvedV1, _world.DemoStudyId, 1, null, ProtocolVersionStatus.Approved, "sha256:v1", null, null, null),
            baseContent));

        // Actor missing protocol.revise_draft (only has protocol.amend + concept.define) -> 403 before fork or concepts.
        var amendOnlyIdentity = TestSecurityContext.Identity("amend.only", "Amend Only");
        _world.GrantRole(
            Actor.Person(amendOnlyIdentity.UserId),
            "AMEND_ONLY",
            ["protocol.amend", "concept.define"],
            AuthorityScope.Tenant());
        using var amendOnlyClient = CreateClient(amendOnlyIdentity);
        using (var req = MultipartOdmPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", SampleOdmXml, "vitals.xml", "no-revise", "Attempt fork without revise_draft"))
        {
            using var res = await amendOnlyClient.SendAsync(req, ct);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _world.Versions.Should().HaveCount(1);
        _world.Concepts.Should().BeEmpty();

        // Actor missing concept.define (has protocol.amend + protocol.revise_draft) -> 403 before fork or concepts.
        var noConceptIdentity = TestSecurityContext.Identity("noconcept.user", "No Concept Define");
        _world.GrantRole(
            Actor.Person(noConceptIdentity.UserId),
            "NO_CONCEPT_DEFINE",
            ["protocol.amend", "protocol.revise_draft"],
            AuthorityScope.Tenant());
        using var noConceptClient = CreateClient(noConceptIdentity);
        using (var req = MultipartOdmPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", SampleOdmXml, "vitals.xml", "no-concept", "Attempt fork without concept.define"))
        {
            using var res = await noConceptClient.SendAsync(req, ct);
            res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _world.Versions.Should().HaveCount(1);
        _world.Concepts.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidImport_CommitsZeroConcepts_AndBiomedicalConceptC25298DeduplicatesAcrossOdmAndUsdm()
    {
        var ct = TestContext.Current.CancellationToken;
        using var designerClient = CreateClient(DesignerIdentity);

        // 1. Invalid ODM referencing an undefined ItemGroupOID alongside an ItemDef with C25298 and a CodeList -> 422 with zero Concept or CodeSystem rows committed.
        const string invalidOdmWithConcept = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.BAD" FileType="Snapshot" ODMVersion="1.3.2">
              <Study OID="S.BAD">
                <GlobalVariables>
                  <StudyName>Invalid ODM</StudyName>
                  <StudyDescription>Invalid</StudyDescription>
                  <ProtocolName>BAD-101</ProtocolName>
                </GlobalVariables>
                <MetaDataVersion OID="MDV.BAD" Name="v1">
                  <FormDef OID="F.OK" Name="Valid First Form" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.OK" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <FormDef OID="F.BAD" Name="Broken Form" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.MISSING" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.OK" Name="Valid Group" Repeating="No">
                    <ItemRef ItemOID="IT.SYS" OrderNumber="1" Mandatory="Yes"/>
                    <ItemRef ItemOID="IT.POS" OrderNumber="2" Mandatory="No"/>
                  </ItemGroupDef>
                  <ItemDef OID="IT.SYS" Name="SYSBP" DataType="integer">
                    <Alias Context="CDASH" Name="extCodeID: C25298"/>
                  </ItemDef>
                  <ItemDef OID="IT.POS" Name="VSPOS" DataType="text">
                    <Question><TranslatedText>Position</TranslatedText></Question>
                    <CodeListRef CodeListOID="CL.POS"/>
                  </ItemDef>
                  <CodeList OID="CL.POS" Name="Position" DataType="text">
                    <CodeListItem CodedValue="SITTING"><Decode><TranslatedText>Sitting</TranslatedText></Decode></CodeListItem>
                  </CodeList>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;

        using (var badReq = MultipartOdmPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", invalidOdmWithConcept, "broken-odm.xml", "bad-odm-c25298", "Attempt broken ODM"))
        {
            using var badRes = await designerClient.SendAsync(badReq, ct);
            badRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        _world.Concepts.Should().BeEmpty();
        _world.CodeSystems.Should().BeEmpty();
        _world.CodeSystemVersions.Should().BeEmpty();
        _world.Versions.Should().BeEmpty();

        // 2. Valid ODM with C25298 defines one CdiscBiomedicalConcept with ExternalCode == "C25298".
        using (var odmReq = MultipartOdmPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", SampleOdmXml, "vitals.xml", "valid-odm-c25298", "Import ODM with C25298"))
        {
            using var odmRes = await designerClient.SendAsync(odmReq, ct);
            odmRes.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var c25298 = _world.Concepts.Should().ContainSingle(c => c.ExternalCode == "C25298").Subject;
        c25298.Origin.Should().Be(ConceptOrigin.CdiscBiomedicalConcept);

        // 3. Subsequent USDM v4 JSON referencing C25298 reuses the exact same CdiscBiomedicalConcept ConceptRef.
        const string usdmWithC25298 = """
            {
              "usdmVersion": "4.0.0",
              "study": {
                "id": "Study_CADENCE_101",
                "name": "CADENCE-101",
                "versions": [
                  {
                    "id": "SV_1",
                    "titles": [
                      { "id": "T_1", "text": "CADENCE-101", "type": { "code": "C207616", "decode": "Official Study Title" } }
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
                            "name": "Home Blood Pressure",
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

        using (var usdmReq = JsonPost(
            "/api/v1/kernel/studies/CADENCE-101/protocol-imports",
            new { format = "usdm_json", file_name = "home-bp.json", content = usdmWithC25298, replace_forms = false },
            "valid-usdm-c25298",
            "Merge USDM referencing C25298"))
        {
            using var usdmRes = await designerClient.SendAsync(usdmReq, ct);
            usdmRes.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await usdmRes.Content.ReadAsStringAsync(ct));
            var forms = doc.RootElement.GetProperty("forms");
            var odmConcept = forms[0].GetProperty("items")[0].GetProperty("concept").GetString();
            var usdmConcept = forms[1].GetProperty("items")[0].GetProperty("concept").GetString();
            usdmConcept.Should().Be(odmConcept).And.Be(c25298.Concept.ToString());
        }

        _world.Concepts.Count(c => c.ExternalCode == "C25298").Should().Be(1);
    }

    [Fact]
    [KernelRule("K-PROT-2")]
    public async Task ForkAndDraftRevision_SetImpactedElementsFromStructuralDiff_AndPersistClassificationAndTransitionPolicy()
    {
        var ct = TestContext.Current.CancellationToken;
        using var designerClient = CreateClient(DesignerIdentity);
        var templates = await designerClient.GetFromJsonAsync<JsonElement>("/api/v1/kernel/protocol-templates", ct);
        var aeTemplateId = templates.EnumerateArray().First(t => t.GetProperty("file_name").GetString() == "AE_XML.xml").GetProperty("id").GetString()!;

        var approvedV1 = ProtocolVersionId.New();
        var v1Content = ProtocolDesignDocument.Write(new ProtocolDesign
        {
            Title = "CADENCE-101",
            Objectives = [new Objective("obj.primary", ObjectiveLevel.Primary, "Primary SBP objective")],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.baseline",
                    "Baseline Assessment",
                    [new CollectionItem("it.base", new ConceptRef(ConceptId.New(), 1), Required: true, ValueKind: ObservationValueKind.Text, Prompt: "Baseline note")]),
            ],
        });
        _world.Versions.Add(new StoredVersion(
            new ProtocolVersionSummary(approvedV1, _world.DemoStudyId, 1, null, ProtocolVersionStatus.Approved, "sha256:v1", null, null, null),
            v1Content));

        // 1. Fork Approved v1 -> Draft v2 with Substantial + RequiresReconsent.
        using (var forkReq = JsonPost(
            "/api/v1/kernel/studies/CADENCE-101/protocol-imports",
            new
            {
                format = "odm_xml",
                file_name = "vitals.xml",
                content = SampleOdmXml,
                replace_forms = false,
                amendment_classification = "Substantial",
                transition_policy = "RequiresReconsent",
            },
            "fork-amend-1",
            "Add Vital Signs form in substantial amendment"))
        {
            using var forkRes = await designerClient.SendAsync(forkReq, ct);
            forkRes.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var v2 = _world.Versions.Single(v => v.Summary.Number == 2);
        var expectedForkDiff = ProtocolStructuralDiff.Compare(v1Content, v2.Content).Value!
            .Select(c => c.ElementId)
            .Where(id => id != ProtocolStructuralDiff.RootId)
            .ToList();
        v2.Amendment.Should().NotBeNull();
        v2.Amendment!.Classification.Should().Be(AmendmentClassification.Substantial);
        v2.Amendment.TransitionPolicy.Should().Be(TransitionPolicy.RequiresReconsent);
        v2.Amendment.ImpactedElements.Should().Equal(expectedForkDiff);

        // 2. Revise Draft v2 by merging bundled AE_XML template with UrgentSafetyMeasure + ByCohort.
        using (var revReq = JsonPost(
            "/api/v1/kernel/studies/CADENCE-101/protocol-imports",
            new
            {
                template_ids = new[] { aeTemplateId },
                replace_forms = false,
                amendment_classification = "UrgentSafetyMeasure",
                transition_policy = "ByCohort",
            },
            "revise-amend-2",
            "Add Adverse Events form under urgent safety measure"))
        {
            using var revRes = await designerClient.SendAsync(revReq, ct);
            revRes.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await revRes.Content.ReadAsStringAsync(ct));
            doc.RootElement.GetProperty("amendment").GetProperty("classification").GetString().Should().Be("UrgentSafetyMeasure");
            doc.RootElement.GetProperty("amendment").GetProperty("transition_policy").GetString().Should().Be("ByCohort");
        }

        var v2Revised = _world.Versions.Single(v => v.Summary.Number == 2);
        var expectedRevDiff = ProtocolStructuralDiff.Compare(v1Content, v2Revised.Content).Value!
            .Select(c => c.ElementId)
            .Where(id => id != ProtocolStructuralDiff.RootId)
            .ToList();
        v2Revised.Amendment!.Classification.Should().Be(AmendmentClassification.UrgentSafetyMeasure);
        v2Revised.Amendment.TransitionPolicy.Should().Be(TransitionPolicy.ByCohort);
        v2Revised.Amendment.ImpactedElements.Should().Equal(expectedRevDiff);
    }

    [Fact]
    public async Task ImportProtocolDraft_RegistersCodeSystemVersionsIdempotently_ForOdmAndUsdmCodelists()
    {
        var ct = TestContext.Current.CancellationToken;
        using var designerClient = CreateClient(DesignerIdentity);

        const string odmWithCodelist = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.CL.1" FileType="Snapshot" ODMVersion="1.3.2" CreationDateTime="2026-02-01T00:00:00Z">
              <Study OID="S.CL">
                <GlobalVariables>
                  <StudyName>Codelist Study</StudyName>
                  <StudyDescription>Test</StudyDescription>
                  <ProtocolName>CL-101</ProtocolName>
                </GlobalVariables>
                <MetaDataVersion OID="MDV.1" Name="v1">
                  <FormDef OID="F.VS" Name="Vital Signs" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.VS" Name="Vital Signs" Repeating="No">
                    <ItemRef ItemOID="IT.SBP" OrderNumber="1" Mandatory="Yes"/>
                    <ItemRef ItemOID="IT.POS" OrderNumber="2" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemDef OID="IT.SBP" Name="SYSBP" DataType="float">
                    <Question><TranslatedText>Systolic Blood Pressure</TranslatedText></Question>
                    <MeasurementUnitRef MeasurementUnitOID="MU.MMHG"/>
                  </ItemDef>
                  <ItemDef OID="IT.POS" Name="VSPOS" DataType="text">
                    <Question><TranslatedText>Position</TranslatedText></Question>
                    <CodeListRef CodeListOID="CL.POS"/>
                  </ItemDef>
                  <CodeList OID="CL.POS" Name="Position" DataType="text">
                    <CodeListItem CodedValue="SITTING"><Decode><TranslatedText>Sitting</TranslatedText></Decode></CodeListItem>
                    <CodeListItem CodedValue="SUPINE"><Decode><TranslatedText>Supine</TranslatedText></Decode></CodeListItem>
                  </CodeList>
                </MetaDataVersion>
                <BasicDefinitions>
                  <MeasurementUnit OID="MU.MMHG" Name="mmHg">
                    <Symbol><TranslatedText>mm[Hg]</TranslatedText></Symbol>
                  </MeasurementUnit>
                </BasicDefinitions>
              </Study>
            </ODM>
            """;

        using (var firstOdm = MultipartOdmPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", odmWithCodelist, "vs_cl.xml", "cl-odm-1", "Import ODM with codelist"))
        {
            using var res = await designerClient.SendAsync(firstOdm, ct);
            res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync(ct));
        }

        _world.CodeSystems.Should().ContainSingle();
        _world.CodeSystemVersions.Should().ContainSingle();
        var firstVersionId = _world.CodeSystemVersions.Single().Id;

        var parsedAfterOdm = Cadence.Kernel.Protocol.Design.ProtocolDesignDocument.Read(_world.Versions.Single().Content).Design!;
        var sbpItem = parsedAfterOdm.CollectionSpecs.Single().Items[0];
        sbpItem.ValueKind.Should().Be(Cadence.Kernel.Evidence.ObservationValueKind.Quantity);
        sbpItem.Unit.Should().Be("mm[Hg]");
        sbpItem.Required.Should().BeTrue();

        var posItem = parsedAfterOdm.CollectionSpecs.Single().Items[1];
        posItem.ValueKind.Should().Be(Cadence.Kernel.Evidence.ObservationValueKind.Coded);
        posItem.Terminology.Should().NotBeNull();
        posItem.Terminology!.CodeSystemVersionId.Should().Be(firstVersionId);

        // Re-importing the same codelist reuses the registered CodeSystemVersion rather than creating duplicates.
        using (var secondOdm = MultipartOdmPost("/api/v1/kernel/studies/CADENCE-101/protocol-imports", odmWithCodelist, "vs_cl.xml", "cl-odm-2", "Re-import same codelist"))
        {
            using var res = await designerClient.SendAsync(secondOdm, ct);
            res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync(ct));
        }

        _world.CodeSystems.Should().ContainSingle();
        _world.CodeSystemVersions.Should().ContainSingle();
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
                services.RemoveAll<IStudyGovernanceReader>();
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

        public List<(CodeSystemId Id, string Uri, LicenseClass License)> CodeSystems { get; } = [];

        public List<CodeSystemVersionView> CodeSystemVersions { get; } = [];

        public List<CodeSystemEntryView> CodeSystemEntries { get; } = [];

        public List<StoredVersion> Versions { get; } = [];

        public Dictionary<string, object> IdempotentResults { get; } = new(StringComparer.Ordinal);

        public void GrantRole(Actor grantee, string code, string[] capabilities, AuthorityScope scope)
        {
            var role = Role.Define(RoleId.New(), _tenant, code, code, capabilities).Value!;
            DefinedRoles.Add(role);
            var grant = Grant.Issue(GrantId.New(), _tenant, grantee, null, role.Id, scope, Epoch, null, false, null, Actor.ServiceAccount("seed"), Epoch).Value!;
            Grants.Add(grant);
        }

        public void MarkVersionApproved(ProtocolVersionId versionId) =>
            SetVersionStatus(versionId, ProtocolVersionStatus.Approved, "sha256:approved");

        public void SetVersionStatus(ProtocolVersionId versionId, ProtocolVersionStatus status, string? contentHash)
        {
            var idx = Versions.FindIndex(v => v.Summary.Id == versionId);
            if (idx >= 0)
            {
                var current = Versions[idx];
                Versions[idx] = new StoredVersion(current.Summary with { Status = status, ContentHash = contentHash }, current.Content, current.Amendment);
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

            if (resource.Kind == NodeKinds.Site)
            {
                var site = Sites.FirstOrDefault(x => x.SiteId == SiteId.FromGuid(resource.Id));
                if (site is not null)
                {
                    return Task.FromResult(new ScopePath(site.InstitutionId, null, site.Country, null));
                }
            }

            if (resource.Kind == NodeKinds.StudySite)
            {
                var ss = StudySites.FirstOrDefault(x => x.StudySiteId == StudySiteId.FromGuid(resource.Id));
                if (ss is not null)
                {
                    var s = Studies.First(x => x.StudyId == ss.StudyId);
                    return Task.FromResult(new ScopePath(s.SponsorId, s.StudyId, ss.Country, ss.StudySiteId));
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

        public Task<SiteDirectoryEntry?> FindSiteByCodeAsync(
            TenantId tenant,
            string siteCode,
            OrganizationId? institutionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Sites.FirstOrDefault(s =>
                string.Equals(s.SiteCode, siteCode, StringComparison.OrdinalIgnoreCase)
                && (institutionId is null || s.InstitutionId == institutionId.Value)));

        public Task<IReadOnlyList<StudySiteDirectoryEntry>> ListStudySitesAsync(
            TenantId tenant,
            StudyId studyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudySiteDirectoryEntry>>(
                [.. StudySites.Where(ss => ss.StudyId == studyId).OrderBy(ss => ss.SiteCode, StringComparer.Ordinal)]);

        public Task<StudySiteDirectoryEntry?> FindStudySiteAsync(
            TenantId tenant,
            StudySiteId studySiteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudySites.FirstOrDefault(ss => ss.StudySiteId == studySiteId));

        public Task<StudySiteDirectoryEntry?> FindStudySiteBySiteAsync(
            TenantId tenant,
            StudyId studyId,
            SiteId siteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StudySites.FirstOrDefault(ss => ss.StudyId == studyId && ss.SiteId == siteId));
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

        public Task<string?> GetContentAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult(Versions.FirstOrDefault(v => v.Summary.Id == version)?.Content);

        public Task<ProtocolAmendment?> GetAmendmentAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
            Task.FromResult(Versions.FirstOrDefault(v => v.Summary.Id == version)?.Amendment);

        public Task<ConceptVersionView?> ResolveAsync(TenantId tenant, ConceptRef concept, CancellationToken cancellationToken = default) =>
            Task.FromResult(Concepts.FirstOrDefault(c => c.Concept == concept));

        public Task<ConceptVersionView?> GetCurrentAsync(TenantId tenant, ConceptId concept, CancellationToken cancellationToken = default) =>
            Task.FromResult(Concepts.FirstOrDefault(c => c.Concept.ConceptId == concept));

        public Task<ConceptVersionView?> FindBiomedicalConceptAsync(TenantId tenant, string cdiscConceptCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Concepts.FirstOrDefault(c => c.Origin == ConceptOrigin.CdiscBiomedicalConcept && c.ExternalCode == cdiscConceptCode));

        public Task<CodeSystemVersionView?> FindCodeSystemVersionAsync(TenantId tenant, string codeSystemUri, string version, CancellationToken cancellationToken = default) =>
            Task.FromResult(CodeSystemVersions.FirstOrDefault(v =>
                string.Equals(v.CodeSystemUri, codeSystemUri, StringComparison.Ordinal)
                && string.Equals(v.Version, version, StringComparison.Ordinal)));

        public Task<IReadOnlyList<CodeSystemEntryView>> LookupAsync(TenantId tenant, Coding coding, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeSystemEntryView>>([
                .. CodeSystemEntries.Where(e =>
                    e.CodeSystemVersionId == coding.CodeSystemVersionId
                    && string.Equals(e.Code, coding.Code, StringComparison.Ordinal)),
            ]);

        public Task<IReadOnlyList<CodeSystemEntryView>> GetGroupAsync(TenantId tenant, CodeSystemVersionId version, string groupCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CodeSystemEntryView>>([
                .. CodeSystemEntries.Where(e =>
                    e.CodeSystemVersionId == version
                    && string.Equals(e.GroupCode, groupCode, StringComparison.Ordinal)),
            ]);

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
                RegisterCodeSystem regCodeSystem => ExecuteRegisterCodeSystem(regCodeSystem),
                ImportCodeSystemVersion importVersion => ExecuteImportCodeSystemVersion(importVersion),
                CreateProtocolDraft createDraft => ExecuteCreateDraft(createDraft),
                ReviseProtocolDraft reviseDraft => ExecuteReviseDraft(reviseDraft),
                ForkProtocolVersion forkVersion => ExecuteForkVersion(forkVersion),
                ImportProtocolDraft importDraft => ExecuteImportProtocolDraft(importDraft),
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
            if (inst is null || inst.Kind != OrganizationKind.SiteInstitution)
            {
                return StudyErrors.InstitutionUnknown;
            }

            world.Sites.Add(new SiteDirectoryEntry(
                cmd.SiteId,
                1,
                inst.OrganizationId,
                inst.VersionIndex,
                inst.Code,
                inst.Name,
                cmd.Code,
                cmd.Name,
                cmd.Country,
                true,
                0));
            return cmd.SiteId;
        }

        private object ExecuteLinkSiteToStudy(LinkSiteToStudy cmd)
        {
            var studyIdx = world.Studies.FindIndex(s => s.StudyId == cmd.StudyId);
            if (studyIdx < 0)
            {
                return StudyErrors.StudyUnknown;
            }

            var siteIdx = world.Sites.FindIndex(s => s.SiteId == cmd.SiteId);
            if (siteIdx < 0)
            {
                return StudyErrors.SiteUnknown;
            }

            if (world.StudySites.Any(ss => ss.StudyId == cmd.StudyId && ss.SiteId == cmd.SiteId))
            {
                return StudyErrors.AlreadyLinked;
            }

            var study = world.Studies[studyIdx];
            var site = world.Sites[siteIdx];
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
                "Selected",
                study.StudyCode,
                study.VersionIndex,
                true));
            world.Studies[studyIdx] = study with { SiteCount = study.SiteCount + 1 };
            world.Sites[siteIdx] = site with { LinkedStudyCount = site.LinkedStudyCount + 1 };
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

        private object ExecuteRegisterCodeSystem(RegisterCodeSystem cmd)
        {
            if (world.CodeSystems.Any(cs => cs.Id == cmd.CodeSystemId || string.Equals(cs.Uri, cmd.Uri, StringComparison.Ordinal)))
            {
                return KernelErrors.AlreadyExists;
            }

            world.CodeSystems.Add((cmd.CodeSystemId, cmd.Uri, cmd.License));
            return cmd.CodeSystemId;
        }

        private object ExecuteImportCodeSystemVersion(ImportCodeSystemVersion cmd)
        {
            var cs = world.CodeSystems.FirstOrDefault(x => x.Id == cmd.CodeSystemId);
            if (cs == default)
            {
                return KernelErrors.NotFound;
            }

            if (world.CodeSystemVersions.Any(v =>
                    v.Id == cmd.CodeSystemVersionId
                    || (v.CodeSystemId == cmd.CodeSystemId && string.Equals(v.Version, cmd.Version, StringComparison.Ordinal))))
            {
                return KernelErrors.AlreadyExists;
            }

            world.CodeSystemVersions.Add(new CodeSystemVersionView(
                cmd.CodeSystemVersionId,
                cmd.CodeSystemId,
                cs.Uri,
                cmd.Version,
                cmd.ReleasedOn,
                cs.License,
                cmd.Package.Sha256,
                cmd.Entries.Count));

            foreach (var entry in cmd.Entries)
            {
                world.CodeSystemEntries.Add(new CodeSystemEntryView(
                    cmd.CodeSystemVersionId,
                    entry.Code,
                    entry.Display,
                    entry.GroupCode,
                    entry.SubmissionValue,
                    entry.Definition,
                    entry.Synonyms ?? []));
            }

            return cmd.CodeSystemVersionId;
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
            world.Versions.Add(new StoredVersion(summary, parent.Content, cmd.Amendment));
            return new ProtocolVersionState(summary.Id, summary.StudyId, summary.Number, ProtocolVersionStatus.Draft, null);
        }

        private object ExecuteImportProtocolDraft(ImportProtocolDraft cmd)
        {
            if (!world.Studies.Any(s => s.StudyId == cmd.StudyId))
            {
                return ProtocolErrors.StudyUnknown;
            }

            if (!ProtocolDesignDocument.Read(cmd.Content).IsValid)
            {
                return ProtocolErrors.ContentInvalid;
            }

            StoredVersion? targetStored;
            int targetIdx = -1;
            switch (cmd.Mode)
            {
                case ProtocolDraftImportMode.CreateDraft:
                    {
                        var studyVersions = world.Versions.Where(v => v.Summary.StudyId == cmd.StudyId).ToList();
                        if (studyVersions.Any(v => v.Summary.Status is not (ProtocolVersionStatus.Withdrawn or ProtocolVersionStatus.Superseded)))
                        {
                            return ProtocolErrors.FirstVersionExists;
                        }

                        if (cmd.Amendment is not null)
                        {
                            return ProtocolErrors.AmendmentNotAllowed;
                        }

                        int nextNum = studyVersions.Count == 0 ? 1 : studyVersions.Max(v => v.Summary.Number) + 1;
                        var summary = new ProtocolVersionSummary(cmd.TargetVersionId, cmd.StudyId, nextNum, null, ProtocolVersionStatus.Draft, null, null, null, null);
                        targetStored = new StoredVersion(summary, cmd.Content, null);
                        break;
                    }

                case ProtocolDraftImportMode.UpdateExisting:
                case ProtocolDraftImportMode.ReopenInReview:
                    {
                        targetIdx = world.Versions.FindIndex(v => v.Summary.Id == cmd.TargetVersionId && v.Summary.StudyId == cmd.StudyId);
                        if (targetIdx < 0)
                        {
                            return KernelErrors.NotFound;
                        }

                        var existing = world.Versions[targetIdx];
                        if (cmd.Mode == ProtocolDraftImportMode.ReopenInReview)
                        {
                            if (existing.Summary.Status is not (ProtocolVersionStatus.InReview or ProtocolVersionStatus.Draft))
                            {
                                return ProtocolErrors.InvalidTransition;
                            }
                        }
                        else if (existing.Summary.Status != ProtocolVersionStatus.Draft)
                        {
                            return ProtocolErrors.InvalidTransition;
                        }

                        if (existing.Summary.ParentVersionId is null && cmd.Amendment is not null)
                        {
                            return ProtocolErrors.AmendmentNotAllowed;
                        }

                        var mergedAmendment = cmd.Amendment ?? existing.Amendment;
                        var updatedSummary = existing.Summary with
                        {
                            Status = ProtocolVersionStatus.Draft,
                            ContentHash = null,
                            AmendmentIntent = mergedAmendment?.Intent ?? existing.Summary.AmendmentIntent,
                        };
                        targetStored = new StoredVersion(updatedSummary, cmd.Content, mergedAmendment);
                        break;
                    }

                case ProtocolDraftImportMode.ForkApproved:
                    {
                        if (cmd.ParentVersionId is not { } parentId)
                        {
                            return ProtocolErrors.AmendmentInvalid;
                        }

                        var parent = world.Versions.FirstOrDefault(v => v.Summary.Id == parentId && v.Summary.StudyId == cmd.StudyId);
                        if (parent is null)
                        {
                            return KernelErrors.NotFound;
                        }

                        if (parent.Summary.Status != ProtocolVersionStatus.Approved)
                        {
                            return ProtocolErrors.InvalidTransition;
                        }

                        if (cmd.Amendment is null
                            || string.IsNullOrWhiteSpace(cmd.Amendment.Intent)
                            || string.IsNullOrWhiteSpace(cmd.Amendment.Rationale)
                            || string.IsNullOrWhiteSpace(cmd.Amendment.SummaryOfChanges)
                            || cmd.Amendment.ImpactedElements is not { Count: > 0 }
                            || !Enum.IsDefined(cmd.Amendment.Classification)
                            || !Enum.IsDefined(cmd.Amendment.TransitionPolicy))
                        {
                            return ProtocolErrors.AmendmentInvalid;
                        }

                        int nextNum = world.Versions.Where(v => v.Summary.StudyId == cmd.StudyId).Max(v => v.Summary.Number) + 1;
                        var forkedSummary = new ProtocolVersionSummary(
                            cmd.TargetVersionId,
                            cmd.StudyId,
                            nextNum,
                            parentId,
                            ProtocolVersionStatus.Draft,
                            null,
                            cmd.Amendment.Intent,
                            null,
                            null);
                        targetStored = new StoredVersion(forkedSummary, cmd.Content, cmd.Amendment);
                        break;
                    }

                default:
                    return ProtocolErrors.InvalidTransition;
            }

            foreach (var staged in cmd.StagedConcepts)
            {
                if (world.Concepts.Any(c => c.Concept.ConceptId == staged.ConceptId))
                {
                    continue;
                }

                if (staged.Origin == ConceptOrigin.CdiscBiomedicalConcept
                    && !string.IsNullOrWhiteSpace(staged.ExternalCode)
                    && world.Concepts.Any(c => c.Origin == ConceptOrigin.CdiscBiomedicalConcept && c.ExternalCode == staged.ExternalCode))
                {
                    continue;
                }

                world.Concepts.Add(new ConceptVersionView(
                    new ConceptRef(staged.ConceptId, 1),
                    staged.Origin,
                    staged.ExternalCode,
                    staged.Kind,
                    staged.PreferredLabel,
                    staged.Definition,
                    ConceptStatus.Active,
                    1,
                    ConceptMappingState.Unmapped,
                    Epoch));
            }

            if (targetIdx >= 0)
            {
                world.Versions[targetIdx] = targetStored;
            }
            else
            {
                world.Versions.Add(targetStored);
            }

            return new ProtocolVersionState(
                targetStored.Summary.Id,
                targetStored.Summary.StudyId,
                targetStored.Summary.Number,
                targetStored.Summary.Status,
                targetStored.Summary.ContentHash);
        }
    }

    private sealed record StoredVersion(ProtocolVersionSummary Summary, string Content, ProtocolAmendment? Amendment = null);
}
