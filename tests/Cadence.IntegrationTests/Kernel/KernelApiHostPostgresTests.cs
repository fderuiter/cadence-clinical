using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Application.Knowledge.Terminology;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.TestSupport.Kernel.Knowledge;
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
    /// Every registered <see cref="NodeKinds.All"/> kind resolves through <c>GET /api/v1/kernel/nodes/{nodeRef}</c> and
    /// <c>/path</c> for seeded records in the active tenant; pseudonymous <c>person</c> and <c>participant</c> summaries
    /// expose zero direct identifiers and require an active <c>TenantMembership</c>; site-scoped callers receive
    /// <c>mask: "NotPermitted"</c> (<c>200 OK</c>) instead of <c>404</c> for <c>qualification</c>, <c>subscription</c>
    /// and <c>product_entitlement</c>, while reading <c>form_version_snapshot</c>, <c>study_standards_profile</c> and
    /// <c>validation_run</c> unmasked; collection findings resolve zoom paths through <c>collection_instance</c>; and
    /// findings targeting absent intermediate rows fall back cleanly without throwing.
    /// </summary>
    [Fact(DisplayName = "HostNodeReads_ResolveAll38NodeKinds_EnforceMaskingAndCollectionFindingPaths_AndFallBackWhenParentMissing")]
    [GxPRequirement("PRD-CRF-013")]
    [KernelRule("K-ID-7")]
    [KernelRule("K-IDN-1")]
    public async Task HostNodeReadsResolveAll38NodeKindsEnforceMaskingAndCollectionFindingPathsAndFallBackWhenParentMissing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await new KernelSignedTargetsScenario(fixture).CreateAsync(ct);
        var tenant = world.Tenant;

        string tenantCode;
        OrganizationId sponsor;
        SiteId site;
        string siteCountry;
        ParticipantId participant;
        EffectivityId studyEffectivity;
        StudyStandardsProfileId standardsProfile;
        ConceptId concept;
        ValidationRunId validationRun;
        Guid protocolElementGuid;
        SubscriptionId subscription;
        ProductEntitlementId productEntitlement;

        await using (var db = fixture.CreateContext(tenant))
        {
            tenantCode = await db.Tenants.AsNoTracking().Where(t => t.Id == tenant).Select(t => t.Code).SingleAsync(ct);
            sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == world.Study).Select(s => s.SponsorId).SingleAsync(ct);
            site = await db.StudySites.AsNoTracking().Where(ss => ss.Id == world.StudySite).Select(ss => ss.SiteId).SingleAsync(ct);
            siteCountry = await db.Sites.AsNoTracking().Where(s => s.Id == site).Select(s => s.Country).SingleAsync(ct);
            participant = await db.Enrollments.AsNoTracking().Where(e => e.Id == world.Enrollment).Select(e => e.ParticipantId).SingleAsync(ct);
            studyEffectivity = await db.Effectivities.AsNoTracking().Where(e => e.TenantId == tenant && e.StudyId == world.Study).Select(e => e.Id).FirstAsync(ct);
            standardsProfile = await db.Set<StudyStandardsProfile>().AsNoTracking().Where(p => p.TenantId == tenant && p.StudyId == world.Study).Select(p => p.Id).SingleAsync(ct);
            concept = await db.Concepts.AsNoTracking().Where(c => c.TenantId == tenant).Select(c => c.Id).FirstAsync(ct);
            validationRun = await db.Set<ValidationRun>().AsNoTracking().Where(r => r.TenantId == tenant && r.StudyId == world.Study).Select(r => r.Id).FirstAsync(ct);
            protocolElementGuid = await db.LineageNodes.AsNoTracking()
                .Where(n => n.TenantId == tenant && n.Kind == NodeKinds.ProtocolElement)
                .Select(n => n.NodeId)
                .FirstAsync(ct);
            subscription = await db.Subscriptions.AsNoTracking().Where(s => s.TenantId == tenant).Select(s => s.Id).FirstOrDefaultAsync(ct);
            productEntitlement = default;
        }

        if (subscription.IsEmpty)
        {
            subscription = await _scenario.OkAsync(
                new ProvisionSubscription(
                    tenant,
                    SubscriptionId.New(),
                    CommercialTier.Complete,
                    SubscriptionStatus.Active,
                    null,
                    KernelProtocolScenario.Key(),
                    KernelProtocolScenario.Reason),
                ct);
        }

        await using (var db = fixture.CreateContext(tenant))
        {
            productEntitlement = await db.ProductEntitlements.AsNoTracking()
                .Where(e => e.TenantId == tenant && e.SubscriptionId == subscription)
                .Select(e => e.Id)
                .FirstAsync(ct);
        }

        // Seed remaining NodeKinds through the command pipeline / kernel store:
        var memberPerson = await _scenario.OkAsync(
            new RegisterPerson(tenant, PersonId.New(), KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var tenantMembership = await _scenario.OkAsync(
            new RegisterTenantMembership(tenant, TenantMembershipId.New(), memberPerson, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var nonMemberPerson = await _scenario.OkAsync(
            new RegisterPerson(tenant, PersonId.New(), KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var roleState = await _scenario.OkAsync(
            new DefineRole(tenant, RoleId.New(), "SITE_CRC", "Site Coordinator", ["observation.record"], KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        const string SponsorAdmin = "admin@all-kinds.test";
        const string SiteCrc = "site-crc@all-kinds.test";
        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(SponsorAdmin), "observation.record", null, AuthorityScope.Tenant(), Tick(), null, false,
                KernelProtocolScenario.Key(), "Tenant-wide reader"),
            ct);
        var siteGrant = await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(SiteCrc), null, roleState.RoleId, AuthorityScope.StudySite(world.StudySite), Tick(), null, false,
                KernelProtocolScenario.Key(), "Site-scoped reader"),
            ct);
        var countryGrant = await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person("monitor@all-kinds.test"), "observation.record", null, AuthorityScope.StudyCountry(world.Study, siteCountry), Tick(), null, false,
                KernelProtocolScenario.Key(), "Country-scoped reader"),
            ct);

        var trainingBytes = Encoding.UTF8.GetBytes("gcp-certificate");
        var trainingArtifactVersion = ArtifactVersionId.New();
        await _scenario.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                trainingArtifactVersion,
                ArtifactKind.TrainingRecord,
                null,
                "GCP Certificate",
                new ArtifactContent(ContentHash.OfBytes(trainingBytes), trainingBytes.Length, "application/pdf", "blob://training/gcp"),
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);
        var qualificationState = await _scenario.OkAsync(
            new RecordQualification(
                tenant,
                QualificationId.New(),
                Actor.Person(SiteCrc),
                QualificationKind.Training,
                "gcp_training",
                null,
                trainingArtifactVersion,
                Tick(),
                null,
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);
        var qualificationRequirementState = await _scenario.OkAsync(
            new DefineQualificationRequirement(
                tenant,
                QualificationRequirementId.New(),
                "observation.record",
                "gcp_training",
                null,
                AuthorityScope.StudySite(world.StudySite),
                Tick(),
                null,
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);

        var countryEffectivity = EffectivityId.New();
        await _scenario.OkAsync(
            new DeclareEffectivity(
                tenant,
                countryEffectivity,
                world.ProtocolVersion,
                EffectivityScope.InCountry(siteCountry),
                Tick(),
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);

        var codeSystem = await _scenario.OkAsync(
            new RegisterCodeSystem(
                tenant,
                CodeSystemId.New(),
                KnownCodeSystems.SdtmCt,
                "CDISC SDTM Controlled Terminology",
                "CDISC",
                LicenseClass.Open,
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);
        var codeSystemVersionLoad = await new TerminologyPackageLoader(fixture.Sender).LoadAsync(
            new TerminologyRelease(
                tenant,
                codeSystem,
                CodeSystemVersionId.New(),
                "2024-09-27",
                new DateOnly(2024, 9, 27),
                "terminology.txt",
                "https://evs.nci.nih.gov/ftp1/CDISC/",
                TerminologyFixtures.Sha256(TerminologyFixtures.CdiscCt),
                KernelProtocolScenario.Reason),
            TerminologyFixtures.CdiscCt,
            new CdiscControlledTerminologyParser("sdtmct"),
            ct);
        codeSystemVersionLoad.IsSuccess.Should().BeTrue(codeSystemVersionLoad.Error?.Message);
        var codeSystemVersion = codeSystemVersionLoad.Value;

        var conceptMapping = await _scenario.OkAsync(
            new MapConcept(
                tenant,
                ConceptMappingId.New(),
                new ConceptRef(concept, 1),
                new Coding(codeSystemVersion, "C62122"),
                MappingRelation.Exact,
                "CDISC BC mapping",
                true,
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);

        var workQueueState = await _scenario.OkAsync(
            new DefineWorkQueue(
                tenant,
                WorkQueueDefinitionId.New(),
                "protocol_approvals",
                "Protocol approvals",
                "protocol.approve",
                VerbClass.Approve,
                KernelWorkItemSources.ProtocolVersionsInReview,
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);

        var formSnapshotId = FormVersionSnapshotId.New();
        var formQueryId = FormQueryId.New();
        var storedNow = KernelTimestamps.ToStoredPrecision(Tick());
        await fixture.SeedAsync(
            new FormVersionSnapshot(
                formSnapshotId,
                tenant,
                world.Study,
                world.ProtocolVersion,
                "1.0",
                "VS",
                ContentHash.Compute("{\"form\":\"VS\"}"),
                "<h:html/>",
                "{}",
                null,
                storedNow),
            new LineageNodeRecord
            {
                TenantId = tenant,
                Kind = NodeKinds.FormQuery,
                NodeId = formQueryId.Value,
                Version = 1L,
                Derived = false,
                ActivityId = Guid.CreateVersion7(),
                RecordedAt = storedNow,
            },
            Edge(tenant, NodeRef.For(world.Enrollment, 1), NodeRef.For(formQueryId, 1), LineageEdgeKind.GovernedBy));

        // Record collection, observation, and orphan-dataset-UUID validation findings:
        var missingFormGuid = Guid.CreateVersion7();
        var collectionRunAt = Tick();
        await _scenario.OkAsync(
            new RecordValidationRun(
                tenant,
                ValidationRunId.New(),
                world.Study,
                ValidationStage.Collection,
                [world.LibraryItem],
                new DateOnly(2026, 9, 30),
                null,
                null,
                null,
                [],
                ["VITALS"],
                collectionRunAt.AddSeconds(-1),
                collectionRunAt,
                [
                    new ValidationDetection(
                        "signed-targets-pack",
                        "R1",
                        1,
                        ValidationSeverity.Error,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "VITALS", world.CollectionInstance.Value.ToString("D"), "systolic"),
                        "Collection form finding.",
                        null,
                        [new ObservedVariable("SYS", "88")]),
                    new ValidationDetection(
                        "signed-targets-pack",
                        "R2",
                        1,
                        ValidationSeverity.Warning,
                        new FindingTarget(FindingTargetKind.Observation, "VITALS", NodeRef.For(world.FormObservation).ToString()),
                        "Observation finding.",
                        null,
                        [new ObservedVariable("SYS", "88")]),
                    new ValidationDetection(
                        "signed-targets-pack",
                        "R1",
                        1,
                        ValidationSeverity.Error,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "VITALS", missingFormGuid.ToString("D"), "systolic"),
                        "Orphan dataset row finding.",
                        null,
                        [new ObservedVariable("SYS", "85")]),
                ],
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);

        var collectionStageFindings = (await fixture.Findings.ListAsync(tenant, new FindingQuery(world.Study) { Stage = ValidationStage.Collection }, ct)).Findings;
        var collectionFinding = collectionStageFindings.Single(f => f.Target.Key == world.CollectionInstance.Value.ToString("D")).Id;
        var observationFinding = collectionStageFindings.Single(f => f.Target.Kind == FindingTargetKind.Observation).Id;
        var orphanDatasetFinding = collectionStageFindings.Single(f => f.Target.Key == missingFormGuid.ToString("D")).Id;

        Tick();
        using var adminClient = Client(tenantCode, SponsorAdmin);
        using var crcClient = Client(tenantCode, SiteCrc);

        // AC 1: Every kind in NodeKinds.All resolves through GET /api/v1/kernel/nodes/{nodeRef} and /path:
        var seededNodes = new Dictionary<string, NodeRef>(StringComparer.Ordinal)
        {
            [NodeKinds.Tenant] = NodeRef.For(tenant),
            [NodeKinds.Organization] = NodeRef.For(sponsor),
            [NodeKinds.Person] = NodeRef.For(memberPerson),
            [NodeKinds.TenantMembership] = NodeRef.For(tenantMembership),
            [NodeKinds.Site] = NodeRef.For(site),
            [NodeKinds.Study] = NodeRef.For(world.Study),
            [NodeKinds.StudySite] = NodeRef.For(world.StudySite),
            [NodeKinds.Participant] = NodeRef.For(participant),
            [NodeKinds.Enrollment] = NodeRef.For(world.Enrollment),
            [NodeKinds.ProtocolVersion] = NodeRef.For(world.ProtocolVersion),
            [NodeKinds.ProtocolElement] = new NodeRef(NodeKinds.ProtocolElement, protocolElementGuid),
            [NodeKinds.Observation] = NodeRef.For(world.FormObservation),
            [NodeKinds.Assertion] = NodeRef.For(world.VerificationAssertion),
            [NodeKinds.CollectionInstance] = NodeRef.For(world.CollectionInstance),
            [NodeKinds.Attestation] = NodeRef.For(world.EnrollmentAttestation),
            [NodeKinds.Artifact] = NodeRef.For(world.StudyArtifact),
            [NodeKinds.ArtifactVersion] = NodeRef.For(world.StudyArtifactVersion),
            [NodeKinds.Role] = NodeRef.For(roleState.RoleId),
            [NodeKinds.Grant] = NodeRef.For(siteGrant.GrantId),
            [NodeKinds.Qualification] = NodeRef.For(qualificationState.QualificationId),
            [NodeKinds.QualificationRequirement] = NodeRef.For(qualificationRequirementState.RequirementId),
            [NodeKinds.Effectivity] = NodeRef.For(studyEffectivity),
            [NodeKinds.CodeSystem] = NodeRef.For(codeSystem),
            [NodeKinds.CodeSystemVersion] = NodeRef.For(codeSystemVersion),
            [NodeKinds.Concept] = NodeRef.For(concept),
            [NodeKinds.ConceptMapping] = NodeRef.For(conceptMapping),
            [NodeKinds.StandardVersion] = NodeRef.For(world.StandardVersion),
            [NodeKinds.LibraryItem] = NodeRef.For(world.LibraryItem),
            [NodeKinds.StudyStandardsProfile] = NodeRef.For(standardsProfile),
            [NodeKinds.WorkQueueDefinition] = NodeRef.For(workQueueState.WorkQueueDefinitionId),
            [NodeKinds.ValidationRun] = NodeRef.For(validationRun),
            [NodeKinds.ValidationFinding] = NodeRef.For(collectionFinding),
            [NodeKinds.Proposal] = NodeRef.For(world.Proposal),
            [NodeKinds.FormVersionSnapshot] = NodeRef.For(formSnapshotId),
            [NodeKinds.FormQuery] = NodeRef.For(formQueryId),
            [NodeKinds.Subscription] = NodeRef.For(subscription),
            [NodeKinds.ProductEntitlement] = NodeRef.For(productEntitlement),
            [NodeKinds.TaskConfiguration] = NodeRef.For(world.AttestedTaskConfiguration),
        };

        seededNodes.Keys.Should().BeEquivalentTo(NodeKinds.All, "every registered NodeKinds.All entry must be exercised");

        var batchedQuery = string.Join("&", seededNodes.Values.Select(r => $"ref={Uri.EscapeDataString(r.ToString())}"));
        var batchedAdmin = await GetAsync<KernelBatchNodesResponse>(adminClient, $"nodes?{batchedQuery}", ct);
        batchedAdmin.Nodes.Keys.Should().BeEquivalentTo(seededNodes.Values.Select(r => r.ToString()), "batch GET /nodes must resolve all 38 NodeKinds.All");

        foreach (var (kind, nodeRef) in seededNodes)
        {
            var resolved = await GetAsync<KernelNodeResponse>(adminClient, $"nodes/{nodeRef}", ct);
            resolved.Kind.Should().Be(kind);
            resolved.Mask.Should().BeNull($"{kind} should resolve unmasked for a tenant-wide reader");
            resolved.Ref.Should().NotBeNullOrWhiteSpace();
            resolved.Title.Should().NotBeNullOrWhiteSpace();
            batchedAdmin.Nodes[nodeRef.ToString()].Should().BeEquivalentTo(resolved, $"batch GET /nodes summary for {kind} must match GET /nodes/{{nodeRef}}");

            var zoom = await GetAsync<IReadOnlyList<KernelZoomStepResponse>>(adminClient, $"nodes/{nodeRef}/path", ct);
            zoom.Should().NotBeEmpty();
            zoom[0].Level.Should().Be(NodeKinds.Tenant, $"{kind} zoom path must start at tenant");
            zoom[^1].Level.Should().Be(kind, $"{kind} zoom path must end at {kind}");
        }

        var batchedCrc = await GetAsync<KernelBatchNodesResponse>(crcClient, $"nodes?{batchedQuery}", ct);
        foreach (var maskedKindRef in new[]
        {
            NodeRef.For(qualificationState.QualificationId),
            NodeRef.For(subscription),
            NodeRef.For(productEntitlement),
        })
        {
            var maskedItem = batchedCrc.Nodes[maskedKindRef.ToString()];
            maskedItem.Kind.Should().Be(maskedKindRef.Kind);
            maskedItem.Mask.Should().Be(NotPermitted);
            maskedItem.Ref.Should().BeNull();
        }

        // Req 2 & Req 3: Pseudonymous person/participant without PII, and person requires active TenantMembership:
        var resolvedPerson = await GetAsync<KernelNodeResponse>(adminClient, $"nodes/{NodeRef.For(memberPerson)}", ct);
        resolvedPerson.Title.Should().Be("Person");
        resolvedPerson.Code.Should().BeNull();

        var resolvedParticipant = await GetAsync<KernelNodeResponse>(adminClient, $"nodes/{NodeRef.For(participant)}", ct);
        resolvedParticipant.Title.Should().Be("Participant");
        resolvedParticipant.Code.Should().BeNull();

        using (var nonMemberResp = await adminClient.GetAsync(new Uri($"/api/v1/kernel/nodes/{NodeRef.For(nonMemberPerson)}", UriKind.Relative), ct))
        {
            nonMemberResp.StatusCode.Should().Be(HttpStatusCode.NotFound, "person without an active TenantMembership in the caller's tenant must not resolve");
        }

        // Req 4: Scoped parents for grant, qualification_requirement, and effectivity (including country grouping):
        var siteGrantPath = await GetAsync<IReadOnlyList<KernelZoomStepResponse>>(adminClient, $"nodes/{NodeRef.For(siteGrant.GrantId)}/path", ct);
        siteGrantPath.Select(s => s.Level).Should().Equal(NodeKinds.Tenant, NodeKinds.Study, ZoomLevels.Country, NodeKinds.StudySite, NodeKinds.Grant);

        var countryGrantPath = await GetAsync<IReadOnlyList<KernelZoomStepResponse>>(adminClient, $"nodes/{NodeRef.For(countryGrant.GrantId)}/path", ct);
        countryGrantPath.Select(s => s.Level).Should().Equal(NodeKinds.Tenant, NodeKinds.Study, ZoomLevels.Country, NodeKinds.Grant);

        var siteReqPath = await GetAsync<IReadOnlyList<KernelZoomStepResponse>>(adminClient, $"nodes/{NodeRef.For(qualificationRequirementState.RequirementId)}/path", ct);
        siteReqPath.Select(s => s.Level).Should().Equal(NodeKinds.Tenant, NodeKinds.Study, ZoomLevels.Country, NodeKinds.StudySite, NodeKinds.QualificationRequirement);

        var countryEffPath = await GetAsync<IReadOnlyList<KernelZoomStepResponse>>(adminClient, $"nodes/{NodeRef.For(countryEffectivity)}/path", ct);
        countryEffPath.Select(s => s.Level).Should().Equal(NodeKinds.Tenant, NodeKinds.Study, ZoomLevels.Country, NodeKinds.Effectivity);

        // AC 2 & Req 5: Site-scoped callers receive mask: "NotPermitted" (200 OK) instead of 404 for qualification, subscription, and product_entitlement:
        foreach (var tenantWideNode in new[]
        {
            NodeRef.For(qualificationState.QualificationId),
            NodeRef.For(subscription),
            NodeRef.For(productEntitlement),
        })
        {
            var masked = await GetAsync<KernelNodeResponse>(crcClient, $"nodes/{tenantWideNode}", ct);
            masked.Kind.Should().Be(tenantWideNode.Kind);
            masked.Mask.Should().Be(NotPermitted, $"{tenantWideNode.Kind} must return 200 OK with mask NotPermitted for site-scoped callers");
            masked.Ref.Should().BeNull();
            masked.Title.Should().BeNull();
            masked.Code.Should().BeNull();
        }

        // Req 6: Study/site readers resolve form_version_snapshot, study_standards_profile, and validation_run unmasked:
        foreach (var studyScopedNode in new[]
        {
            NodeRef.For(formSnapshotId),
            NodeRef.For(standardsProfile),
            NodeRef.For(validationRun),
        })
        {
            var visible = await GetAsync<KernelNodeResponse>(crcClient, $"nodes/{studyScopedNode}", ct);
            visible.Mask.Should().BeNull($"{studyScopedNode.Kind} sits on its study scope path and is visible to study/site readers");
            visible.Ref.Should().NotBeNullOrWhiteSpace();
        }

        // AC 3 & Req 7: Collection finding zoom path includes tenant -> study -> country -> study_site -> enrollment -> collection_instance -> validation_finding:
        var collectionFindingPath = await GetAsync<IReadOnlyList<KernelZoomStepResponse>>(crcClient, $"nodes/{NodeRef.For(collectionFinding)}/path", ct);
        collectionFindingPath.Select(s => s.Level).Should().Equal(
            NodeKinds.Tenant,
            NodeKinds.Study,
            ZoomLevels.Country,
            NodeKinds.StudySite,
            NodeKinds.Enrollment,
            NodeKinds.CollectionInstance,
            NodeKinds.ValidationFinding);
        collectionFindingPath.Should().OnlyContain(s => s.Mask == null);

        var observationFindingPath = await GetAsync<IReadOnlyList<KernelZoomStepResponse>>(crcClient, $"nodes/{NodeRef.For(observationFinding)}/path", ct);
        observationFindingPath.Select(s => s.Level).Should().Equal(
            NodeKinds.Tenant,
            NodeKinds.Study,
            ZoomLevels.Country,
            NodeKinds.StudySite,
            NodeKinds.Enrollment,
            NodeKinds.CollectionInstance,
            NodeKinds.Observation,
            NodeKinds.ValidationFinding);

        // AC 4 & Req 8: Missing intermediate parent row falls back to study -> tenant without throwing InvalidOperationException:
        var orphanFindingPath = await GetAsync<IReadOnlyList<KernelZoomStepResponse>>(adminClient, $"nodes/{NodeRef.For(orphanDatasetFinding)}/path", ct);
        orphanFindingPath.Select(s => s.Level).Should().Equal(
            NodeKinds.Tenant,
            NodeKinds.Study,
            NodeKinds.ValidationFinding);
    }

    /// <summary>A client signed as <paramref name="userId"/> in <paramref name="tenantCode"/>, on one host per test class.</summary>
    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
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
