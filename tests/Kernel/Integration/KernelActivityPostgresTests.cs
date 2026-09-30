using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Activity;
using Cadence.Kernel.Application.Activity;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Activity;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.References;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Kernel.Integration;

/// <summary>
/// The activity stream against PostgreSQL (ADR-2485): the projector turns outbox envelopes into activity entries
/// scoped by the subject's zoom path, idempotently and without touching the outbox; reads filter by scope, family,
/// actor and time, page by cursor, stay inside the tenant, and mask entries the reader may not see. Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelActivityPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private static readonly string[] StudyEvents =
    [
        "ProtocolVersionDrafted", "ProtocolVersionSubmittedForReview", "AttestationRecorded", "ProtocolVersionApproved",
    ];

    /// <summary>
    /// A study's activity holds its protocol events and the attestation that signed its protocol version, not the
    /// tenant's own events; the tenant's activity holds all of them. Projecting again writes nothing, and the outbox
    /// the projector reads is left exactly as it was.
    /// </summary>
    [Fact(DisplayName = "Projection_ScopesByZoomPath_AndIsIdempotent")]
    public async Task ProjectionScopesByZoomPathAndIsIdempotent()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (tenant, study) = await scenario.NewStudyAsync(ct);
        var (version, _) = await scenario.ApprovedAsync(tenant, study, ct);
        var outboxBefore = await OutboxAsync(tenant, ct);

        var written = await Projector().ProjectAsync(tenant, ct);
        var again = await Projector().ProjectAsync(tenant, ct);

        written.Should().Be(outboxBefore.Count);
        again.Should().Be(0);
        (await OutboxAsync(tenant, ct)).Should().BeEquivalentTo(outboxBefore);

        var everything = await ReadAsync(tenant, new ActivityQuery(), ct);
        var inStudy = await ReadAsync(tenant, new ActivityQuery(Scope: NodeRef.For(study)), ct);
        everything.Entries.Select(e => e.Type).Should().Contain(["TenantProvisioned", "OrganizationRegistered", .. StudyEvents]);
        inStudy.Entries.Select(e => e.Type).Should().BeEquivalentTo(StudyEvents);
        inStudy.Entries.Should().BeInDescendingOrder(e => e.Recorded);
        inStudy.Entries.Single(e => e.Type == "ProtocolVersionApproved").Subject.Should()
            .Be(NodeRef.For(version.ProtocolVersionId, 3), "the entry names the version the event produced");
        (await ReadAsync(tenant, new ActivityQuery(Scope: NodeRef.For(version.ProtocolVersionId, 1)), ct)).Entries
            .Should().HaveCount(StudyEvents.Length, "a scope is matched whatever version it names");
    }

    /// <summary>Family, actor and time filters narrow the stream, and combine.</summary>
    [Fact(DisplayName = "Read_FiltersByFamilyActorAndTime")]
    public async Task ReadFiltersByFamilyActorAndTime()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (tenant, study) = await scenario.NewStudyAsync(ct);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var afterSetup = fixture.Clock.UtcNow;
        await scenario.ApprovedAsync(tenant, study, ct);
        await Projector().ProjectAsync(tenant, ct);

        var trust = await ReadAsync(tenant, new ActivityQuery(Families: [EventFamily.Trust]), ct);
        var byActor = await ReadAsync(tenant, new ActivityQuery(ActorId: KernelPostgresFixture.ActorId), ct);
        var byStranger = await ReadAsync(tenant, new ActivityQuery(ActorId: "someone-else@cadence.test"), ct);
        var recent = await ReadAsync(tenant, new ActivityQuery(From: afterSetup), ct);

        trust.Entries.Should().ContainSingle().Which.Should().Match<ActivityEntry>(e =>
            e.Type == "AttestationRecorded" && e.Family == EventFamily.Trust && e.SubjectKind == NodeKinds.Attestation);
        byActor.Entries.Should().NotBeEmpty().And.OnlyContain(e => e.ActorId == KernelPostgresFixture.ActorId);
        byStranger.Entries.Should().BeEmpty();
        recent.Entries.Select(e => e.Type).Should().BeEquivalentTo(StudyEvents);
    }

    /// <summary>Following the cursor page by page returns every entry exactly once, in the same order as one read.</summary>
    [Fact(DisplayName = "Read_CursorPagesThroughEveryEntryOnce")]
    public async Task ReadCursorPagesThroughEveryEntryOnce()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (tenant, study) = await scenario.NewStudyAsync(ct);
        await scenario.ApprovedAsync(tenant, study, ct);
        foreach (var code in new[] { "CRO", "LAB", "IRB" })
        {
            await scenario.OkAsync(new RegisterOrganization(tenant, OrganizationId.New(), $"{code}-{Guid.NewGuid():N}"[..20], code, OrganizationKind.Vendor, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        }

        await Projector().ProjectAsync(tenant, ct);

        var all = (await ReadAsync(tenant, new ActivityQuery(Limit: ActivityQuery.MaxLimit), ct)).Entries;
        var paged = new List<ActivityEntry>();
        ActivityCursor? cursor = null;
        do
        {
            var page = await ReadAsync(tenant, new ActivityQuery(Before: cursor, Limit: 2), ct);
            page.Entries.Should().HaveCountLessThanOrEqualTo(2);
            paged.AddRange(page.Entries);
            cursor = page.Next;
        }
        while (cursor is not null);

        all.Should().HaveCount(9, "two tenant events, four protocol events and three organizations");
        paged.Select(e => e.EventId).Should().Equal(all.Select(e => e.EventId));
    }

    /// <summary>
    /// An event recorded before the newest projected entry, as when a transaction commits after a later one, is still
    /// projected on the next pass: the projector looks back before its high-water mark.
    /// </summary>
    [Fact(DisplayName = "Projection_PicksUpEventsThatCommitLate")]
    public async Task ProjectionPicksUpEventsThatCommitLate()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (tenant, _) = await scenario.NewStudyAsync(ct);
        await Projector().ProjectAsync(tenant, ct);
        var now = fixture.Clock.UtcNow;

        fixture.Clock.Set(now.AddMinutes(-2));
        var late = OrganizationId.New();
        await scenario.OkAsync(new RegisterOrganization(tenant, late, $"LATE-{late.Value:N}"[..20], "Late CRO", OrganizationKind.Vendor, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        fixture.Clock.Set(now);

        (await Projector().ProjectAsync(tenant, ct)).Should().Be(1);
        (await ReadAsync(tenant, new ActivityQuery(Scope: NodeRef.For(late)), ct)).Entries.Should().ContainSingle()
            .Which.Recorded.Should().Be(now.AddMinutes(-2));
    }

    /// <summary>
    /// A participant's screening sits in its study's and study site's activity through its enrollment, while the
    /// participant's registration, which belongs to no single study, is in the tenant's activity only.
    /// </summary>
    [Fact(DisplayName = "Projection_ScopesEnrollmentEventsUnderTheirStudySite")]
    public async Task ProjectionScopesEnrollmentEventsUnderTheirStudySite()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (tenant, study) = await scenario.NewStudyAsync(ct);
        StudySiteId studySite;
        await using (var db = fixture.CreateContext(tenant))
        {
            var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
            var site = new Site(SiteId.New(), tenant, sponsor, $"ACT-{Guid.NewGuid():N}"[..20], "Activity Clinic", "NL", KernelPostgresFixture.ActorId, KernelProtocolScenario.Reason);
            var link = new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, KernelProtocolScenario.Reason);
            await fixture.SeedAsync(site, link);
            studySite = link.Id;
        }

        var participant = await scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        SubjectCode.TryFrom("NL-001", out var code).Should().BeTrue();
        await scenario.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participant, studySite, code, fixture.Clock.UtcNow.AddMinutes(-1), KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);
        await Projector().ProjectAsync(tenant, ct);

        var inSite = await ReadAsync(tenant, new ActivityQuery(Scope: NodeRef.For(studySite)), ct);
        var inStudy = await ReadAsync(tenant, new ActivityQuery(Scope: NodeRef.For(study), Families: [EventFamily.Execution]), ct);
        var inTenant = await ReadAsync(tenant, new ActivityQuery(Families: [EventFamily.Execution]), ct);

        inSite.Entries.Select(e => e.Type).Should().Equal("ParticipantScreened");
        inSite.Entries[0].SubjectKind.Should().Be(NodeKinds.Enrollment);
        inStudy.Entries.Select(e => e.Type).Should().Equal("ParticipantScreened");
        inTenant.Entries.Select(e => e.Type).Should().Contain(["ParticipantRegistered", "ParticipantScreened"]);
    }

    /// <summary>
    /// A reader for whom the protocol version is blinded sees its entries in place, with the reason and without the
    /// reference; a reader with no mask sees the reference. The stream never shortens.
    /// </summary>
    [Fact(DisplayName = "Read_MasksEntriesTheReaderMayNotSee")]
    public async Task ReadMasksEntriesTheReaderMayNotSee()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (tenant, study) = await scenario.NewStudyAsync(ct);
        var (version, _) = await scenario.ApprovedAsync(tenant, study, ct);
        await Projector().ProjectAsync(tenant, ct);
        var hidden = NodeRef.For(version.ProtocolVersionId);

        var blinded = await ReadAsync(tenant, new ActivityQuery(Scope: NodeRef.For(study)), ct, new MaskKind(hidden, MaskReason.Blinded));
        var open = await ReadAsync(tenant, new ActivityQuery(Scope: NodeRef.For(study)), ct);

        blinded.Entries.Select(e => e.EventId).Should().Equal(open.Entries.Select(e => e.EventId));
        blinded.Entries.Where(e => e.SubjectKind == NodeKinds.ProtocolVersion).Should().HaveCount(3)
            .And.OnlyContain(e => e.Subject == null && e.Masked == MaskReason.Blinded);
        blinded.Entries.Single(e => e.SubjectKind == NodeKinds.Attestation).Masked.Should().BeNull();
        open.Entries.Should().OnlyContain(e => e.Subject != null && e.Masked == null);
    }

    /// <summary>
    /// Two tenants' activity never mixes: each tenant's stream holds only its own events, and a scope naming another
    /// tenant's study returns nothing, because every read runs under the tenant's Row-Level Security.
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "Read_NeverReturnsAnotherTenantsActivity")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task ReadNeverReturnsAnotherTenantsActivity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (first, firstStudy) = await scenario.NewStudyAsync(ct);
        var (second, secondStudy) = await scenario.NewStudyAsync(ct);
        await scenario.ApprovedAsync(first, firstStudy, ct);
        await scenario.ApprovedAsync(second, secondStudy, ct);
        await Projector().ProjectAllAsync(ct);

        var firstEvents = (await OutboxAsync(first, ct)).Select(o => o.EventId);
        var secondEvents = (await OutboxAsync(second, ct)).Select(o => o.EventId);
        var firstStream = await ReadAsync(first, new ActivityQuery(Limit: ActivityQuery.MaxLimit), ct);
        var secondStream = await ReadAsync(second, new ActivityQuery(Limit: ActivityQuery.MaxLimit), ct);
        var crossScope = await ReadAsync(first, new ActivityQuery(Scope: NodeRef.For(secondStudy)), ct);

        firstStream.Entries.Select(e => e.EventId).Should().BeEquivalentTo(firstEvents);
        secondStream.Entries.Select(e => e.EventId).Should().BeEquivalentTo(secondEvents);
        crossScope.Entries.Should().BeEmpty();
    }

    private KernelActivityProjector Projector() =>
        new(fixture.TenantContexts, new KernelNavigator(new KernelScopeGraph(fixture.TenantContexts)), new KernelBackgroundOptions());

    private Task<ActivityPage> ReadAsync(TenantId tenant, ActivityQuery query, CancellationToken ct, INodeVisibility? visibility = null) =>
        new ActivityStream(new KernelActivityStore(fixture.TenantContexts), visibility ?? new MaskKind(null, MaskReason.NotPermitted), fixture.CurrentActor)
            .ReadAsync(tenant, query, ct);

    private async Task<List<(Guid EventId, string Envelope, DateTimeOffset? DispatchedAt, int Attempts)>> OutboxAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var rows = await db.OutboxMessages.AsNoTracking().Where(o => o.TenantId == tenant).ToListAsync(ct);
        return [.. rows.Select(o => (o.EventId, o.Envelope, o.DispatchedAt, o.Attempts))];
    }

    /// <summary>Masks every version of one object, as D1 requires, and nothing else.</summary>
    private sealed class MaskKind(NodeRef? node, MaskReason reason) : INodeVisibility
    {
        public ValueTask<IReadOnlyDictionary<NodeRef, MaskReason>> MaskAsync(
            TenantId tenant, Actor reader, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
        {
            var masked = nodes.Where(n => node is not null && n.Unversioned() == node.Unversioned()).ToDictionary(n => n, _ => reason);
            return ValueTask.FromResult<IReadOnlyDictionary<NodeRef, MaskReason>>(masked);
        }
    }
}
