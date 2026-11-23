using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The kernel API host end to end against PostgreSQL (ADR-2540, ADR-2457): the real <c>Program.cs</c> with its tenant
/// directory, grant-backed visibility and lineage reads, over rows written through the kernel pipeline. Trace, Impact
/// and amendment impact show a coordinator what their grant covers and mask the rest in place, with the same counts
/// for every reader. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelApiHostPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string Crc = "crc@kernel-host.test";
    private const string Stranger = "stranger@kernel-host.test";
    private const string NotPermitted = nameof(MaskReason.NotPermitted);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>One primary objective with its endpoint; the amendment rewords the objective.</summary>
    private static readonly ProtocolDesign Design = ProtocolDesignSamples.MinimalDesign() with
    {
        Objectives = [new Objective("obj.1", ObjectiveLevel.Primary, "Show that the drug lowers systolic blood pressure.")],
        Endpoints = [new Endpoint("ep.1", "obj.1", ObjectiveLevel.Primary, "Change in systolic blood pressure at week 12.")],
    };

    private readonly KernelProtocolScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// A coordinator granted at one study site reads, through the host, the approved version's dependents and an
    /// amendment's impact: the enrollment at their site, the design elements and the version are shown; the enrollment
    /// at the other site keeps its place masked as not permitted. A reader with no grant gets the same counts with
    /// everything masked. The amendment's impact report describes its content, so the reader with no grant is refused it.
    /// </summary>
    [Fact(DisplayName = "HostLineageReads_ShowWhatTheReadersGrantCovers_AndMaskTheRest")]
    public async Task HostLineageReadsShowWhatTheReadersGrantCoversAndMaskTheRest()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var code = $"T-HOST-{Guid.NewGuid():N}"[..17];
        var tenant = TenantId.New();
        await _scenario.OkAsync(new ProvisionTenant(tenant, code, "Host tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.NewStudyAsync(tenant, ct);
        var first = await ApproveFirstAsync(tenant, study, ct);
        var amendment = await ForkRewordedAsync(tenant, first, ct);
        var ownSite = await AddStudySiteAsync(tenant, study, ct);
        var otherSite = await AddStudySiteAsync(tenant, study, ct);
        var ownEnrollment = NodeRef.For(await ScreenAsync(tenant, ownSite, "HOST-001", ct), 1);
        var otherEnrollment = NodeRef.For(await ScreenAsync(tenant, otherSite, "HOST-002", ct), 1);
        NodeRef approved;
        await using (var db = fixture.CreateContext(tenant))
        {
            approved = NodeRef.For(first, await db.ProtocolVersions.AsNoTracking().Where(v => v.Id == first).Select(v => v.VersionIndex).SingleAsync(ct));
        }

        await fixture.SeedAsync(
            Edge(tenant, approved, ownEnrollment, LineageEdgeKind.GovernedBy),
            Edge(tenant, approved, otherEnrollment, LineageEdgeKind.GovernedBy));
        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(Crc), "observation.record", null, AuthorityScope.StudySite(ownSite), Tick(), null, false,
                KernelProtocolScenario.Key(), "Appointed"),
            ct);
        Tick();
        using var crc = Client(code, Crc);
        using var stranger = Client(code, Stranger);

        var dependents = await GetAsync<KernelDependentsResponse>(crc, $"nodes/{approved}/dependents", ct);
        var objects = dependents.Groups.SelectMany(g => g.Objects).ToList();
        objects.Should().ContainSingle(o => o.Ref == ownEnrollment.ToString() && o.Mask == null);
        objects.Should().ContainSingle(o => o.Kind == NodeKinds.Enrollment && o.Ref == null && o.Mask == NotPermitted);
        objects.Should().NotContain(o => o.Ref == otherEnrollment.ToString(), "a masked object never carries its reference");
        objects.Where(o => o.Kind == NodeKinds.ProtocolElement).Should().NotBeEmpty().And.OnlyContain(o => o.Mask == null && o.Ref != null);

        var hidden = await GetAsync<KernelDependentsResponse>(stranger, $"nodes/{approved}/dependents", ct);
        hidden.Total.Should().Be(dependents.Total, "masked objects are still counted (Canvas D2)");
        hidden.Groups.Select(g => (g.Kind, g.Count)).Should().BeEquivalentTo(dependents.Groups.Select(g => (g.Kind, g.Count)));
        hidden.Groups.SelectMany(g => g.Objects).Should().OnlyContain(o => o.Ref == null && o.Mask == NotPermitted);

        var impact = await GetAsync<KernelAmendmentImpactResponse>(crc, $"protocol-versions/{NodeRef.For(amendment)}/impact", ct);
        impact.ChangedElements.Should().BeEquivalentTo([new KernelElementChangeResponse("obj.1", null, "Changed", ["/text"])]);
        var impacted = impact.Dependents.Groups.SelectMany(g => g.Objects).ToList();
        impacted.Should().ContainSingle(o => o.Ref == ownEnrollment.ToString());
        impacted.Should().ContainSingle(o => o.Kind == NodeKinds.Enrollment && o.Mask == NotPermitted);
        impacted.Should().Contain(o => o.Ref == NodeRef.For(ProtocolElementId.For(study, "obj.1"), 1).ToString());
        impact.Schedule.Should().NotBeNull("the design is typed, so the report carries its schedule directives (ADR-2475)");
        using (var refused = await stranger.GetAsync(new Uri($"/api/v1/kernel/protocol-versions/{NodeRef.For(amendment)}/impact", UriKind.Relative), ct))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the report describes a version the stranger may not see (ADR-2475 §4.4)");
        }


        var trace = await GetAsync<KernelLineageResponse>(crc, $"nodes/{ownEnrollment}/lineage", ct);
        trace.Nodes.Should().Contain(n => n.Ref == approved.ToString() && n.Mask == null);
        var otherTrace = await GetAsync<KernelLineageResponse>(crc, $"nodes/{otherEnrollment}/lineage", ct);
        otherTrace.Nodes.Should().ContainSingle(n => n.Distance == 0).Which.Mask.Should().Be(NotPermitted);
        otherTrace.Nodes.Should().Contain(n => n.Ref == approved.ToString() && n.Mask == null, "the version it came from is still the coordinator's to see");
    }

    /// <summary>
    /// The host projects committed events into the activity stream itself and serves them per reader (ADR-2485): a
    /// coordinator granted at one study site reads the study's activity with their own site's screening shown and the
    /// other site's screening masked in place, and a reader with no grant is refused the study's activity.
    /// </summary>
    [Fact(DisplayName = "HostActivity_ProjectsEvents_AndShowsWhatTheReadersGrantCovers")]
    public async Task HostActivityProjectsEventsAndShowsWhatTheReadersGrantCovers()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var code = $"T-ACT-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.OkAsync(new ProvisionTenant(tenant, code, "Activity tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.NewStudyAsync(tenant, ct);
        var ownSite = await AddStudySiteAsync(tenant, study, ct);
        var otherSite = await AddStudySiteAsync(tenant, study, ct);
        var own = NodeRef.For(await ScreenAsync(tenant, ownSite, "ACT-001", ct));
        var other = NodeRef.For(await ScreenAsync(tenant, otherSite, "ACT-002", ct));
        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(Crc), "observation.record", null, AuthorityScope.StudySite(ownSite), Tick(), null, false,
                KernelProtocolScenario.Key(), "Appointed"),
            ct);
        using var crc = Client(code, Crc);
        using var stranger = Client(code, Stranger);

        // The host's projector runs every few seconds; wait until both screenings are in the study's stream.
        KernelActivityPageResponse page;
        var attempts = 0;
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            page = await GetAsync<KernelActivityPageResponse>(crc, $"activity?scope={NodeRef.For(study)}&family=Execution", ct);
        }
        while (page.Entries.Count(e => e.Type == "ParticipantScreened") < 2 && ++attempts < 120);

        var screenings = page.Entries.Where(e => e.Type == "ParticipantScreened").ToList();
        screenings.Should().HaveCount(2).And.OnlyContain(e => e.SubjectKind == NodeKinds.Enrollment && e.Family == "Execution");
        screenings.Should().ContainSingle(e => e.Mask == null && e.Subject != null && NodeRef.Parse(e.Subject).Unversioned() == own);
        screenings.Should().ContainSingle(e => e.Mask == NotPermitted && e.Subject == null);
        page.Entries.Should().NotContain(e => e.Subject != null && NodeRef.Parse(e.Subject).Unversioned() == other);
        page.Entries.Should().BeInDescendingOrder(e => e.Recorded);

        using var refused = await stranger.GetAsync(new Uri($"/api/v1/kernel/activity?scope={NodeRef.For(study)}", UriKind.Relative), ct);
        refused.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Validates that the host runs the kernel's background jobs and delivers committed events to its in-process
    /// consumers (ADR-2601, #5313, #5314): the outbox relay, audit sealing and lineage check are hosted services, and the
    /// relay's publisher is the in-process one.
    /// </summary>
    [Fact(DisplayName = "Host_RunsBackgroundJobs_AndDeliversEventsInProcess")]
    public void HostRunsBackgroundJobsAndDeliversEventsInProcess()
    {
        fixture.SkipIfUnavailable();
        _ = Client("unused", Crc);

        var services = _host!.Services;
        services.GetServices<IHostedService>().Select(s => s.GetType().Name)
            .Should().Contain(["KernelOutboxRelayService", "KernelAuditSealingService", "KernelLineageCheckService"]);
        services.GetRequiredService<IKernelEventPublisher>().GetType().Name.Should().Be("InProcessKernelEventPublisher");
    }

    /// <summary>
    /// Direct enrollment lookup resolves study, site, and subject codes across study sites over the unique
    /// <c>(tenant_id, study_id, subject_code)</c> index, isolates tenants, and returns an identical non-confirming 404
    /// when the study, site, or enrollment is absent, mismatched, or outside the caller's grants (ADR-2471).
    /// </summary>
    [Fact(DisplayName = "HostEnrollmentLookup_ResolvesAcrossSites_IsolatesTenants_AndMasksHiddenAs404")]
    public async Task HostEnrollmentLookupResolvesAcrossSitesIsolatesTenantsAndMasksHiddenAs404()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenantACode = $"T-LKA-{Guid.NewGuid():N}"[..16];
        var tenantBCode = $"T-LKB-{Guid.NewGuid():N}"[..16];
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        await _scenario.OkAsync(new ProvisionTenant(tenantA, tenantACode, "Lookup tenant A", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await _scenario.OkAsync(new ProvisionTenant(tenantB, tenantBCode, "Lookup tenant B", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var (_, studyA) = await _scenario.NewStudyAsync(tenantA, ct);
        var (_, studyB) = await _scenario.NewStudyAsync(tenantB, ct);
        var ownSite = await AddStudySiteAsync(tenantA, studyA, ct);
        var otherSite = await AddStudySiteAsync(tenantA, studyA, ct);
        var tenantBSite = await AddStudySiteAsync(tenantB, studyB, ct);

        var ownEnrollment = await ScreenAsync(tenantA, ownSite, "LKP-001", ct);
        var otherEnrollment = await ScreenAsync(tenantA, otherSite, "LKP-002", ct);
        _ = await ScreenAsync(tenantB, tenantBSite, "LKP-001", ct);

        string studyACode;
        string ownSiteCode;
        string otherSiteCode;
        await using (var db = fixture.CreateContext(tenantA))
        {
            studyACode = await db.Studies.AsNoTracking().Where(s => s.Id == studyA).Select(s => s.StudyCode).SingleAsync(ct);
            ownSiteCode = await (
                    from ss in db.StudySites.AsNoTracking()
                    join s in db.Sites.AsNoTracking() on ss.SiteId equals s.Id
                    where ss.Id == ownSite
                    select s.SiteCode)
                .SingleAsync(ct);
            otherSiteCode = await (
                    from ss in db.StudySites.AsNoTracking()
                    join s in db.Sites.AsNoTracking() on ss.SiteId equals s.Id
                    where ss.Id == otherSite
                    select s.SiteCode)
                .SingleAsync(ct);
        }

        const string dataManager = "dm@kernel-host.test";
        await _scenario.OkAsync(
            new IssueGrant(tenantA, GrantId.New(), Actor.Person(dataManager), "observation.record", null, AuthorityScope.Study(studyA), Tick(), null, false,
                KernelProtocolScenario.Key(), "Study DM appointed"),
            ct);
        await _scenario.OkAsync(
            new IssueGrant(tenantA, GrantId.New(), Actor.Person(Crc), "observation.record", null, AuthorityScope.StudySite(ownSite), Tick(), null, false,
                KernelProtocolScenario.Key(), "Site CRC appointed"),
            ct);
        await _scenario.OkAsync(
            new IssueGrant(tenantB, GrantId.New(), Actor.Person(dataManager), "observation.record", null, AuthorityScope.Study(studyB), Tick(), null, false,
                KernelProtocolScenario.Key(), "Tenant B DM appointed"),
            ct);
        Tick();

        using var dmA = Client(tenantACode, dataManager);
        using var crcA = Client(tenantACode, Crc);
        using var strangerA = Client(tenantACode, Stranger);
        using var dmB = Client(tenantBCode, dataManager);

        // Study-wide DM resolves across both sites without needing site_code, and gets pinned @VersionIndex NodeRefs.
        var resolvedOwn = await GetAsync<KernelEnrollmentLookupResponse>(
            dmA,
            $"enrollments/lookup?study_code={Uri.EscapeDataString(studyACode)}&subject_code=LKP-001",
            ct);
        resolvedOwn.StudyRef.Should().Be(NodeRef.For(studyA, 1).ToString());
        resolvedOwn.StudySiteRef.Should().Be(NodeRef.For(ownSite, 1).ToString());
        resolvedOwn.SiteCode.Should().Be(ownSiteCode);
        resolvedOwn.EnrollmentRef.Should().Be(NodeRef.For(ownEnrollment, 1).ToString());
        resolvedOwn.SubjectCode.Should().Be("LKP-001");

        var resolvedOther = await GetAsync<KernelEnrollmentLookupResponse>(
            dmA,
            $"enrollments/lookup?study_code={Uri.EscapeDataString(studyACode.ToLowerInvariant())}&site_code={Uri.EscapeDataString(otherSiteCode)}&subject_code=LKP-002",
            ct);
        resolvedOther.EnrollmentRef.Should().Be(NodeRef.For(otherEnrollment, 1).ToString());

        // Site CRC resolves their own site's enrollment.
        var crcOwn = await GetAsync<KernelEnrollmentLookupResponse>(
            crcA,
            $"enrollments/lookup?study_code={Uri.EscapeDataString(studyACode)}&subject_code=LKP-001",
            ct);
        crcOwn.EnrollmentRef.Should().Be(NodeRef.For(ownEnrollment, 1).ToString());

        // Absent subject, mismatched site_code, other-site hidden enrollment, ungranted stranger, and cross-tenant lookup
        // all return the exact same non-confirming 404 response.
        using var absentResp = await dmA.GetAsync(
            new Uri($"/api/v1/kernel/enrollments/lookup?study_code={Uri.EscapeDataString(studyACode)}&subject_code=LKP-NONE", UriKind.Relative), ct);
        absentResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var absentJson = JsonDocument.Parse(await absentResp.Content.ReadAsStringAsync(ct));
        var expectedTitle = absentJson.RootElement.GetProperty("title").GetString();
        var expectedDetail = absentJson.RootElement.GetProperty("detail").GetString();

        foreach (var (client, query) in new (HttpClient Client, string Query)[]
        {
            (dmA, $"study_code={Uri.EscapeDataString(studyACode)}&site_code={Uri.EscapeDataString(otherSiteCode)}&subject_code=LKP-001"),
            (crcA, $"study_code={Uri.EscapeDataString(studyACode)}&subject_code=LKP-002"),
            (strangerA, $"study_code={Uri.EscapeDataString(studyACode)}&subject_code=LKP-001"),
            (dmB, $"study_code={Uri.EscapeDataString(studyACode)}&subject_code=LKP-001"),
        })
        {
            using var resp = await client.GetAsync(new Uri($"/api/v1/kernel/enrollments/lookup?{query}", UriKind.Relative), ct);
            resp.StatusCode.Should().Be(HttpStatusCode.NotFound, query);
            var body = await resp.Content.ReadAsStringAsync(ct);
            body.Should().NotContain(NotPermitted).And.NotContain(otherEnrollment.Value.ToString());
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("title").GetString().Should().Be(expectedTitle);
            doc.RootElement.GetProperty("detail").GetString().Should().Be(expectedDetail);
        }
    }

    /// <summary>
    /// K-EXP-2 end-to-end over PostgreSQL: a caller without visibility over another site's enrollment or draft protocol
    /// version receives masked NodeRefs (<c>masked:&lt;kind&gt;:NotPermitted</c>) in <c>can-i</c>, <c>commands</c>, and HTTP
    /// 403 <c>ProblemDetails.explanation</c> responses without leaking hidden UUIDs, while the denial audit entry in
    /// PostgreSQL preserves unredacted evidence.
    /// </summary>
    [Fact(DisplayName = "HostExplanations_MaskRestrictedNodeRefs_WhileKeepingAuditUnredacted")]
    [Cadence.Testing.KernelRule("K-EXP-2")]
    public async Task HostExplanationsMaskRestrictedNodeRefsWhileKeepingAuditUnredacted()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var code = $"T-EXP-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.OkAsync(new ProvisionTenant(tenant, code, "Explanation masking tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.NewStudyAsync(tenant, ct);
        var first = await ApproveFirstAsync(tenant, study, ct);
        var amendment = await ForkRewordedAsync(tenant, first, ct);
        var ownSite = await AddStudySiteAsync(tenant, study, ct);
        var otherSite = await AddStudySiteAsync(tenant, study, ct);
        var otherEnrollment = NodeRef.For(await ScreenAsync(tenant, otherSite, "EXP-002", ct), 1);

        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(Crc), "observation.record", null, AuthorityScope.StudySite(ownSite), Tick(), null, false,
                KernelProtocolScenario.Key(), "Own-site CRC appointed"),
            ct);
        Tick();

        using var stranger = Client(code, Stranger);
        using var canIResp = await stranger.GetAsync(
            new Uri($"/api/v1/kernel/can-i?capability=observation.record&resource={Uri.EscapeDataString(otherEnrollment.ToString())}", UriKind.Relative), ct);
        canIResp.IsSuccessStatusCode.Should().BeTrue();
        var canIBody = await canIResp.Content.ReadAsStringAsync(ct);
        canIBody.Should().NotContain(otherEnrollment.Id.ToString("D"));
        using var canIDoc = JsonDocument.Parse(canIBody);
        canIDoc.RootElement.GetProperty("permitted").GetBoolean().Should().BeFalse();
        canIDoc.RootElement.GetProperty("explanation").GetProperty("subject").GetString()
            .Should().Be($"masked:{NodeKinds.Enrollment}:{NotPermitted}");

        using var cmdsResp = await stranger.GetAsync(
            new Uri($"/api/v1/kernel/nodes/{Uri.EscapeDataString(otherEnrollment.ToString())}/commands?capabilities=observation.record", UriKind.Relative), ct);
        cmdsResp.IsSuccessStatusCode.Should().BeTrue();
        var cmdsBody = await cmdsResp.Content.ReadAsStringAsync(ct);
        cmdsBody.Should().NotContain(otherEnrollment.Id.ToString("D"));
        using var cmdsDoc = JsonDocument.Parse(cmdsBody);
        cmdsDoc.RootElement.GetProperty("subject").GetString()
            .Should().Be($"masked:{NodeKinds.Enrollment}:{NotPermitted}");

        using var denied = await stranger.PostAsJsonAsync(
            new Uri($"/api/v1/kernel/protocol-versions/{NodeRef.For(amendment)}/submit", UriKind.Relative),
            new { idempotency_key = KernelProtocolScenario.Key(), reason = "Unauthorized submit" },
            Json,
            ct);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var deniedBody = await denied.Content.ReadAsStringAsync(ct);
        using var deniedDoc = JsonDocument.Parse(deniedBody);
        deniedDoc.RootElement.TryGetProperty("explanation", out var expEl).Should().BeTrue();
        expEl.GetProperty("producer").GetString().Should().Be("kernel.authorization");

        await using var db = fixture.CreateAdminContext();
        var denialAudit = await db.AuditEntries.AsNoTracking()
            .Where(a => a.TenantId == tenant && a.Action == "SubmitProtocolVersionForReview.Denied")
            .SingleAsync(ct);
        denialAudit.Changes.Should().NotContain("masked:");
    }

    /// <summary>A client signed as <paramref name="userId"/> in <paramref name="tenantCode"/>, on one host per test class.</summary>
    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/kernel/{path}", UriKind.Relative), ct);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private async Task<ProtocolVersionId> ApproveFirstAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(tenant, study, id, ct, ProtocolDesignDocument.Write(Design))).IsSuccess.Should().BeTrue();
        var inReview = await _scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), "Ready"), ct);
        var attestation = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.OkAsync(new ApproveProtocolVersion(tenant, id, attestation, KernelProtocolScenario.Key(), "Approved"), ct);
        return id;
    }

    private async Task<ProtocolVersionId> ForkRewordedAsync(TenantId tenant, ProtocolVersionId parent, CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        var reworded = ProtocolDesignDocument.Write(Design with
        {
            Objectives = [Design.Objectives[0] with { Text = "Show that the drug lowers blood pressure." }],
        });
        await _scenario.OkAsync(new ForkProtocolVersion(tenant, parent, id, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Amendment"), ct);
        await _scenario.OkAsync(new ReviseProtocolDraft(tenant, id, reworded, null, KernelProtocolScenario.Key(), "Reword the objective"), ct);
        return id;
    }

    private async Task<StudySiteId> AddStudySiteAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var site = new Site(SiteId.New(), tenant, sponsor, $"SITE-{suffix}", "Memorial Hospital", "US", KernelPostgresFixture.ActorId, "Seeded for test");
        var studySite = new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, "Seeded for test");
        await fixture.SeedAsync(site, studySite);
        return studySite.Id;
    }

    private async Task<EnrollmentId> ScreenAsync(TenantId tenant, StudySiteId studySite, string code, CancellationToken ct)
    {
        var participant = await _scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented"), ct);
        SubjectCode.TryFrom(code, out var subjectCode);
        var enrollment = await _scenario.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participant, studySite, subjectCode, Tick(), KernelProtocolScenario.Key(), "Screening visit"), ct);
        return enrollment.EnrollmentId;
    }

    private LineageEdgeRecord Edge(TenantId tenant, NodeRef from, NodeRef to, LineageEdgeKind kind) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant,
        FromKind = from.Kind,
        FromId = from.Id,
        FromVersion = from.Version!.Value,
        ToKind = to.Kind,
        ToId = to.Id,
        ToVersion = to.Version!.Value,
        Kind = kind,
        ActivityId = Guid.CreateVersion7(),
        RecordedAt = KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow),
    };
}
