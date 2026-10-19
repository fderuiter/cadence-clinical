using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Registering studies, sites and study sites through the command pipeline against PostgreSQL (#5485): each command
/// writes its aggregate with its event and audit entry, and refuses duplicates and unknown references. Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelStudyRegistrationPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>
    /// A study, a site and the link between them are written by the pipeline with the caller in their audit fields, one
    /// event each and one audit entry each naming the caller; the new study site starts Selected and takes screenings.
    /// </summary>
    [Fact(DisplayName = "StudySiteAndLink_AreRegisteredThroughThePipeline_WithEventsAndAudit")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task StudySiteAndLinkAreRegisteredThroughThePipelineWithEventsAndAudit()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, sponsor) = await NewSponsorAsync(ct);
        var institution = await RegisterOrganizationAsync(tenant, OrganizationKind.SiteInstitution, ct);

        var study = await _scenario.OkAsync(new RegisterStudy(tenant, StudyId.New(), sponsor, "CARDIO-301", "Heart failure outcomes", "Phase 3", KernelProtocolScenario.Key(), "Study start-up"), ct);
        var site = await _scenario.OkAsync(new RegisterSite(tenant, SiteId.New(), institution, "SITE-101", "Memorial Hospital", "US", KernelProtocolScenario.Key(), "Site selection"), ct);
        var studySite = await _scenario.OkAsync(new LinkSiteToStudy(tenant, StudySiteId.New(), study, site, KernelProtocolScenario.Key(), "Site selection"), ct);
        var participant = await _scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented"), ct);
        SubjectCode.TryFrom("101-001", out var code);
        await _scenario.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participant, studySite, code, fixture.Clock.UtcNow, KernelProtocolScenario.Key(), "Screening visit"), ct);

        await using var db = fixture.CreateContext(tenant);
        var studyRow = await db.Studies.AsNoTracking().SingleAsync(s => s.Id == study, ct);
        (studyRow.SponsorId, studyRow.StudyCode, studyRow.Title, studyRow.Phase, studyRow.CreatedBy, studyRow.ReasonForChange)
            .Should().Be((sponsor, "CARDIO-301", "Heart failure outcomes", "Phase 3", KernelPostgresFixture.ActorId, "Study start-up"));
        var siteRow = await db.Sites.AsNoTracking().SingleAsync(s => s.Id == site, ct);
        (siteRow.InstitutionId, siteRow.SiteCode, siteRow.Name, siteRow.Country).Should().Be((institution, "SITE-101", "Memorial Hospital", "US"));
        var link = await db.StudySites.AsNoTracking().SingleAsync(s => s.Id == studySite, ct);
        (link.StudyId, link.SiteId, link.Status).Should().Be((study, site, "Selected"));

        var events = await db.DomainEvents.AsNoTracking().Where(e => e.TenantId == tenant).OrderBy(e => e.Recorded).ThenBy(e => e.EventId).Select(e => e.Type).ToListAsync(ct);
        events.Should().ContainInOrder("StudyRegistered", "SiteRegistered", "SiteLinkedToStudy", "ParticipantScreened");
        var audited = await db.AuditEntries.AsNoTracking()
            .Where(a => a.TenantId == tenant && a.ActorId == KernelPostgresFixture.ActorId)
            .Select(a => a.AggregateId)
            .ToListAsync(ct);
        audited.Should().Contain([study.ToString(), site.ToString(), studySite.ToString()]);
    }

    /// <summary>
    /// The commands refuse a blank code or title, a malformed country, an unknown sponsor, institution, study or site,
    /// a study code already used in the tenant, a site code already used by the institution, a second link of the same
    /// site to the study, and a reused id.
    /// </summary>
    [Fact(DisplayName = "StudyCommands_RefuseInvalidUnknownAndDuplicate")]
    public async Task StudyCommandsRefuseInvalidUnknownAndDuplicate()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, sponsor) = await NewSponsorAsync(ct);
        var institution = await RegisterOrganizationAsync(tenant, OrganizationKind.SiteInstitution, ct);
        var study = await _scenario.OkAsync(Study(tenant, StudyId.New(), sponsor, "ONC-101"), ct);
        var site = await _scenario.OkAsync(Site(tenant, SiteId.New(), institution, "SITE-201", "DE"), ct);
        var studySite = await _scenario.OkAsync(Link(tenant, StudySiteId.New(), study, site), ct);

        (await SendAsync(Study(tenant, StudyId.New(), sponsor, " "), ct)).Error.Should().Be(StudyErrors.CodeRequired);
        (await SendAsync(Study(tenant, StudyId.New(), sponsor, "ONC-102") with { Title = "" }, ct)).Error.Should().Be(StudyErrors.TitleRequired);
        (await SendAsync(Study(tenant, StudyId.New(), OrganizationId.New(), "ONC-103"), ct)).Error.Should().Be(StudyErrors.SponsorUnknown);
        (await SendAsync(Study(tenant, StudyId.New(), sponsor, "ONC-101"), ct)).Error.Should().Be(StudyErrors.StudyCodeTaken);
        (await SendAsync(Study(tenant, study, sponsor, "ONC-104"), ct)).Error.Should().Be(KernelErrors.AlreadyExists);

        (await SendAsync(Site(tenant, SiteId.New(), institution, "SITE-202", "Germany"), ct)).Error.Should().Be(StudyErrors.CountryInvalid);
        (await SendAsync(Site(tenant, SiteId.New(), institution, "SITE-202", "de"), ct)).Error.Should().Be(StudyErrors.CountryInvalid);
        (await SendAsync(Site(tenant, SiteId.New(), institution, "SITE-202", "DE") with { Name = " " }, ct)).Error.Should().Be(StudyErrors.NameRequired);
        (await SendAsync(Site(tenant, SiteId.New(), OrganizationId.New(), "SITE-202", "DE"), ct)).Error.Should().Be(StudyErrors.InstitutionUnknown);
        (await SendAsync(Site(tenant, SiteId.New(), institution, "SITE-201", "DE"), ct)).Error.Should().Be(StudyErrors.SiteCodeTaken);
        await _scenario.OkAsync(Site(tenant, SiteId.New(), sponsor, "SITE-201", "DE"), ct);

        (await SendAsync(Link(tenant, StudySiteId.New(), StudyId.New(), site), ct)).Error.Should().Be(StudyErrors.StudyUnknown);
        (await SendAsync(Link(tenant, StudySiteId.New(), study, SiteId.New()), ct)).Error.Should().Be(StudyErrors.SiteUnknown);
        (await SendAsync(Link(tenant, StudySiteId.New(), study, site), ct)).Error.Should().Be(StudyErrors.AlreadyLinked);
        (await SendAsync(Link(tenant, studySite, study, site), ct)).Error.Should().Be(KernelErrors.AlreadyExists);
    }

    /// <summary>A study code is unique per tenant, not globally: another tenant may reuse it.</summary>
    [Fact(DisplayName = "StudyCode_IsUniquePerTenant")]
    public async Task StudyCodeIsUniquePerTenant()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (first, firstSponsor) = await NewSponsorAsync(ct);
        var (second, secondSponsor) = await NewSponsorAsync(ct);

        await _scenario.OkAsync(Study(first, StudyId.New(), firstSponsor, "SHARED-001"), ct);
        await _scenario.OkAsync(Study(second, StudyId.New(), secondSponsor, "SHARED-001"), ct);
        (await SendAsync(Study(first, StudyId.New(), secondSponsor, "OTHER-001"), ct)).Error.Should().Be(StudyErrors.SponsorUnknown, "another tenant's sponsor is not visible");
    }

    private static RegisterStudy Study(TenantId tenant, StudyId id, OrganizationId sponsor, string code) =>
        new(tenant, id, sponsor, code, "A study", "Phase 2", KernelProtocolScenario.Key(), "Study start-up");

    private static RegisterSite Site(TenantId tenant, SiteId id, OrganizationId institution, string code, string country) =>
        new(tenant, id, institution, code, "A hospital", country, KernelProtocolScenario.Key(), "Site selection");

    private static LinkSiteToStudy Link(TenantId tenant, StudySiteId id, StudyId study, SiteId site) =>
        new(tenant, id, study, site, KernelProtocolScenario.Key(), "Site selection");

    private Task<CommandResult<TResult>> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct) =>
        fixture.Sender.SendAsync(command, cancellationToken: ct);

    private async Task<(TenantId Tenant, OrganizationId Sponsor)> NewSponsorAsync(CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenant = TenantId.New();
        await _scenario.OkAsync(new ProvisionTenant(tenant, $"T-{suffix}", $"Tenant {suffix}", $"tenant-{suffix}", KernelProtocolScenario.Reason), ct);
        return (tenant, await RegisterOrganizationAsync(tenant, OrganizationKind.Sponsor, ct));
    }

    private async Task<OrganizationId> RegisterOrganizationAsync(TenantId tenant, OrganizationKind kind, CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        return await _scenario.OkAsync(
            new RegisterOrganization(tenant, OrganizationId.New(), $"ORG-{suffix}", $"Organization {suffix}", kind, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
    }
}
