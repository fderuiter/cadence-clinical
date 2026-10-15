using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.References;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Kernel navigation over the real kernel tables (ADR-2481, Canvas PX-1 and PX-2): summaries, the zoom path through a
/// study site's country, children with counts, and no reads across tenants. Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelNavigationPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Integration test";

    /// <summary>
    /// A study site resolves to its site's name and code at its current version, zooms portfolio → study → country →
    /// study site, and appears under its study grouped by country.
    /// </summary>
    [Fact(DisplayName = "Navigation_StudySite_SummaryPathAndChildren")]
    public async Task NavigationStudySiteSummaryPathAndChildren()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedTenantAsync("NAVA");
        var navigator = new KernelNavigator(new KernelScopeGraph(fixture.TenantContexts));

        var summary = await navigator.ResolveAsync(world.Tenant, NodeRef.For(world.StudySite), ct);
        summary.Should().Be(new NodeSummary(NodeRef.For(world.StudySite, 1), "NAVA Site", "NAVA-S1", "Active"));

        var path = await navigator.GetZoomPathAsync(world.Tenant, NodeRef.For(world.StudySite), ct);
        path!.Select(s => (s.Level, s.Label)).Should().Equal(
            (NodeKinds.Tenant, "NAVA"),
            (NodeKinds.Study, "NAVA-301"),
            (ZoomLevels.Country, "DE"),
            (NodeKinds.StudySite, "NAVA-S1"));

        var studyChildren = await navigator.GetChildrenAsync(world.Tenant, NodeRef.For(world.Study), ct);
        studyChildren.Should().ContainSingle().Which.Should().Be(new ScopeChild(summary!, ChildCount: 0, Group: "DE"));

        var tenantChildren = await navigator.GetChildrenAsync(world.Tenant, NodeRef.For(world.Tenant), ct);
        tenantChildren!.Select(c => (c.Node.Ref.Kind, c.Node.Code, c.ChildCount)).Should().Equal(
            (NodeKinds.Study, "NAVA-301", 1),
            (NodeKinds.Organization, "NAVA-ORG", 1));

        var sitePath = await navigator.GetZoomPathAsync(world.Tenant, NodeRef.For(world.Site), ct);
        sitePath!.Select(s => s.Level).Should().Equal(NodeKinds.Tenant, NodeKinds.Organization, NodeKinds.Site);
    }

    /// <summary>
    /// A participant appears in a study as its enrollment: it zooms portfolio → study → country → study site →
    /// enrollment under its subject code, and a study site's children are its enrollments, counted on the study.
    /// </summary>
    [Fact(DisplayName = "Navigation_EnrollmentSitsUnderItsStudySite")]
    public async Task NavigationEnrollmentSitsUnderItsStudySite()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (tenant, study) = await scenario.NewStudyAsync(ct);
        await using (var db = fixture.CreateContext(tenant))
        {
            var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
            var site = new Site(SiteId.New(), tenant, sponsor, $"NAVE-{Guid.NewGuid():N}"[..20], "Enrolling Clinic", "FR", KernelPostgresFixture.ActorId, Reason);
            await fixture.SeedAsync(site, new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, Reason));
        }

        StudySiteId studySite;
        await using (var db = fixture.CreateContext(tenant))
        {
            studySite = await db.StudySites.AsNoTracking().Where(s => s.StudyId == study).Select(s => s.Id).SingleAsync(ct);
        }

        var enrollments = new List<EnrollmentId>();
        foreach (var code in new[] { "FR-002", "FR-001" })
        {
            var participant = await scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), Reason), ct);
            SubjectCode.TryFrom(code, out var subjectCode).Should().BeTrue();
            var state = await scenario.OkAsync(
                new ScreenParticipant(tenant, EnrollmentId.New(), participant, studySite, subjectCode, fixture.Clock.UtcNow.AddMinutes(-1), KernelProtocolScenario.Key(), Reason),
                ct);
            enrollments.Add(state.EnrollmentId);
        }

        var navigator = new KernelNavigator(new KernelScopeGraph(fixture.TenantContexts));

        var path = await navigator.GetZoomPathAsync(tenant, NodeRef.For(enrollments[0]), ct);
        var siteChildren = await navigator.GetChildrenAsync(tenant, NodeRef.For(studySite), ct);
        var studyChildren = await navigator.GetChildrenAsync(tenant, NodeRef.For(study), ct);

        path!.Select(s => s.Level).Should().Equal(NodeKinds.Tenant, NodeKinds.Study, ZoomLevels.Country, NodeKinds.StudySite, NodeKinds.Enrollment);
        path![^1].Label.Should().Be("FR-002");
        siteChildren!.Select(c => (c.Node.Title, c.Node.Status, c.ChildCount)).Should().Equal(
            ("FR-001", "Screening", (int?)null),
            ("FR-002", "Screening", (int?)null));
        studyChildren.Should().ContainSingle().Which.ChildCount.Should().Be(2);
    }

    /// <summary>
    /// Navigation is tenant-bound: another tenant's study, study site and tenant root resolve to nothing, and a
    /// tenant's children never include another tenant's objects.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "Navigation_NeverCrossesTenants")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task NavigationNeverCrossesTenants()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var mine = await SeedTenantAsync("NAVB");
        var theirs = await SeedTenantAsync("NAVC");
        var navigator = new KernelNavigator(new KernelScopeGraph(fixture.TenantContexts));

        (await navigator.ResolveAsync(mine.Tenant, NodeRef.For(theirs.Study), ct)).Should().BeNull();
        (await navigator.GetZoomPathAsync(mine.Tenant, NodeRef.For(theirs.StudySite), ct)).Should().BeNull();
        (await navigator.ResolveAsync(mine.Tenant, NodeRef.For(theirs.Tenant), ct)).Should().BeNull();
        var children = await navigator.GetChildrenAsync(mine.Tenant, NodeRef.For(mine.Tenant), ct);
        children!.Select(c => c.Node.Ref.Id).Should().NotContain([theirs.Study.Value, theirs.Organization.Value]);
    }

    /// <summary>
    /// Every event a command raises names the aggregate it changed as a NodeRef pinned to the new version, in the
    /// outbox envelope that integrations and the activity stream read.
    /// </summary>
    [Fact(DisplayName = "Events_CarryTheChangedAggregateAsNodeRef")]
    public async Task EventsCarryTheChangedAggregateAsNodeRef()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var organization = OrganizationId.New();

        (await fixture.Sender.SendAsync(new ProvisionTenant(tenant, "NAVD", "NAVD Pharma", Guid.NewGuid().ToString(), Reason), cancellationToken: ct))
            .IsSuccess.Should().BeTrue();
        (await fixture.Sender.SendAsync(
            new RegisterOrganization(tenant, organization, "NAVD-ORG", "NAVD University", OrganizationKind.Sponsor, Guid.NewGuid().ToString(), Reason),
            cancellationToken: ct)).IsSuccess.Should().BeTrue();

        await using var db = fixture.CreateContext(tenant);
        var subjects = (await db.OutboxMessages.AsNoTracking()
                .Where(o => o.TenantId == tenant)
                .OrderBy(o => o.EnqueuedAt).ThenBy(o => o.Id)
                .Select(o => o.Envelope)
                .ToListAsync(ct))
            .Select(json => JsonSerializer.Deserialize<EventEnvelope>(json, JsonSerializerOptions.Web)!.Subject)
            .ToList();

        subjects.Should().Equal(NodeRef.For(tenant, 1), NodeRef.For(organization, 1));
    }

    private async Task<SeededTenant> SeedTenantAsync(string code)
    {
        var tenant = TenantId.New();
        var organization = OrganizationId.New();
        var site = SiteId.New();
        var study = StudyId.New();
        var studySite = StudySiteId.New();
        var actor = KernelPostgresFixture.ActorId;
        await fixture.SeedAsync(
            new Tenant(tenant, code, $"{code} Pharma", actor, Reason),
            new Organization(organization, tenant, $"{code}-ORG", $"{code} University", OrganizationKind.SiteInstitution, actor, Reason),
            new Site(site, tenant, organization, $"{code}-S1", $"{code} Site", "DE", actor, Reason),
            new Study(study, tenant, organization, $"{code}-301", $"{code} Phase 3", "Phase 3", actor, Reason),
            new StudySite(studySite, tenant, study, site, "Active", actor, Reason));
        return new SeededTenant(tenant, organization, site, study, studySite);
    }

    private sealed record SeededTenant(TenantId Tenant, OrganizationId Organization, SiteId Site, StudyId Study, StudySiteId StudySite);
}
