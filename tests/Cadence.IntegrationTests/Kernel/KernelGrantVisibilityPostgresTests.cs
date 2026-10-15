using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.References;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Grant-backed visibility over the kernel's own scope paths against PostgreSQL (ADR-2457, #4781): grants issued
/// through the pipeline decide which studies, study sites, sites, participants, enrollments, protocol versions and
/// effectivities a reader sees. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelGrantVisibilityPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private static readonly Actor Crc = Actor.Person("crc@visibility.test");
    private static readonly Actor Monitor = Actor.Person("monitor@visibility.test");
    private static readonly Actor Stranger = Actor.Person("stranger@visibility.test");

    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>
    /// Validates the whole rule set on real rows: a coordinator granted at one US study site sees that site, its hospital,
    /// its participant and enrollment, and the study, sponsor, protocol version and US effectivity above it, but not a
    /// second site or another study. A monitor granted for the study in the US sees both US sites. A person with no grant
    /// sees nothing, and revoking the coordinator's grant hides everything again.
    /// </summary>
    [Fact(DisplayName = "GrantVisibility_FollowsGrantScopes_OverKernelScopePaths")]
    public async Task GrantVisibilityFollowsGrantScopesOverKernelScopePaths()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (version, _) = await _scenario.ApprovedAsync(tenant, study, ct);
        var otherStudy = await NewStudyInTenantAsync(tenant, ct);
        var (boston, hospital) = await AddStudySiteAsync(tenant, study, "US", ct);
        var (chicago, _) = await AddStudySiteAsync(tenant, study, "US", ct);
        var (berlin, _) = await AddStudySiteAsync(tenant, otherStudy, "DE", ct);
        var (participant, enrollment) = await ScreenAsync(tenant, boston, "VIS-001", ct);
        var (otherParticipant, otherEnrollment) = await ScreenAsync(tenant, chicago, "VIS-002", ct);
        var usEffectivity = EffectivityId.New();
        await _scenario.OkAsync(
            new DeclareEffectivity(tenant, usEffectivity, version.ProtocolVersionId, EffectivityScope.InCountry("US"), Tick(), KernelProtocolScenario.Key(), "Filing"), ct);
        OrganizationId sponsor;
        await using (var db = fixture.CreateContext(tenant))
        {
            sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
        }

        var crcGrant = await IssueAsync(tenant, Crc, AuthorityScope.StudySite(boston), ct);
        await IssueAsync(tenant, Monitor, AuthorityScope.StudyCountry(study, "US"), ct);
        Tick();

        NodeRef[] nodes =
        [
            NodeRef.For(study), NodeRef.For(otherStudy), NodeRef.For(sponsor), NodeRef.For(boston), NodeRef.For(chicago), NodeRef.For(berlin),
            NodeRef.For(hospital), NodeRef.For(participant), NodeRef.For(otherParticipant), NodeRef.For(enrollment), NodeRef.For(otherEnrollment),
            NodeRef.For(version.ProtocolVersionId), NodeRef.For(usEffectivity), NodeRef.For(tenant),
        ];
        var visibility = new GrantNodeVisibility(fixture.AuthorityStore, fixture.Clock);

        (await visibility.MaskAsync(tenant, Crc, nodes, ct)).Keys.Should().BeEquivalentTo(
            [NodeRef.For(otherStudy), NodeRef.For(chicago), NodeRef.For(berlin), NodeRef.For(otherParticipant), NodeRef.For(otherEnrollment)]);
        (await visibility.MaskAsync(tenant, Monitor, nodes, ct)).Keys.Should().BeEquivalentTo(
            [NodeRef.For(otherStudy), NodeRef.For(berlin)]);
        (await visibility.MaskAsync(tenant, Stranger, nodes, ct)).Should().HaveCount(nodes.Length)
            .And.AllSatisfy(m => m.Value.Should().Be(MaskReason.NotPermitted));

        await _scenario.OkAsync(new RevokeGrant(tenant, crcGrant, Tick(), KernelProtocolScenario.Key(), "Left the site"), ct);
        Tick();
        (await visibility.MaskAsync(tenant, Crc, nodes, ct)).Should().HaveCount(nodes.Length);
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private async Task<GrantId> IssueAsync(TenantId tenant, Actor grantee, AuthorityScope scope, CancellationToken ct)
    {
        var grant = GrantId.New();
        await _scenario.OkAsync(
            new IssueGrant(tenant, grant, grantee, "observation.record", null, scope, fixture.Clock.UtcNow, null, false, KernelProtocolScenario.Key(), "Appointed"), ct);
        return grant;
    }

    private async Task<(ParticipantId Participant, EnrollmentId Enrollment)> ScreenAsync(TenantId tenant, StudySiteId studySite, string code, CancellationToken ct)
    {
        var participant = await _scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented"), ct);
        SubjectCode.TryFrom(code, out var subjectCode);
        var enrollment = await _scenario.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participant, studySite, subjectCode, Tick(), KernelProtocolScenario.Key(), "Screening visit"), ct);
        return (participant, enrollment.EnrollmentId);
    }

    private async Task<StudyId> NewStudyInTenantAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Select(s => s.SponsorId).FirstAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var study = new Study(StudyId.New(), tenant, sponsor, $"STUDY-{suffix}", "Second study", "Phase 2", KernelPostgresFixture.ActorId, "Seeded for test");
        await fixture.SeedAsync(study);
        return study.Id;
    }

    private async Task<(StudySiteId StudySite, SiteId Site)> AddStudySiteAsync(TenantId tenant, StudyId study, string country, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var site = new Site(SiteId.New(), tenant, sponsor, $"SITE-{suffix}", "Memorial Hospital", country, KernelPostgresFixture.ActorId, "Seeded for test");
        var studySite = new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, "Seeded for test");
        await fixture.SeedAsync(site, studySite);
        return (studySite.Id, site.Id);
    }
}
