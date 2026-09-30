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

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The kernel API host end to end against PostgreSQL (ADR-2540, ADR-2457): the real <c>Program.cs</c> with its tenant
/// directory, grant-backed visibility and lineage reads, over rows written through the kernel pipeline. Trace, Impact
/// and amendment impact show a coordinator what their grant covers and mask the rest in place, with the same counts
/// for every reader. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
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
