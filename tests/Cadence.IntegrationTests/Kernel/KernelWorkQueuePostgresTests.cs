using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.WorkQueues;
using Cadence.Kernel.References;
using Cadence.Kernel.WorkQueues;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Work queues against PostgreSQL with the production authority evaluator (Canvas D10, ADR-2486): a queue keyed by
/// <c>protocol.approve</c> holds the protocol versions in review that the reader holds that capability on, and loses
/// them when the grant is revoked. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelWorkQueuePostgresTests : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private static readonly Actor Approver = Actor.Person("approver@sponsor.test");
    private static readonly Actor Other = Actor.Person("other@sponsor.test");
    private static readonly Actor Agent = new(ActorKind.AutomationAgent, "agent:review-assistant");

    private readonly KernelPostgresFixture _fixture;
    private readonly KernelProtocolScenario _scenario;

    public KernelWorkQueuePostgresTests(KernelPostgresFixture fixture)
    {
        _fixture = fixture;
        _scenario = new KernelProtocolScenario(fixture);
    }

    public void Dispose()
    {
        if (_fixture.ConnectionString is not null)
        {
            _fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }
    }

    /// <summary>
    /// The approver's queue holds exactly the in-review version of the study their grant covers, due since it was
    /// submitted; a person without the grant and an automation agent with it see nothing (agents may not approve); a
    /// study filter narrows the queue; and revoking the grant empties it.
    /// </summary>
    [Fact(DisplayName = "WorkQueue_FollowsGrants_RevokingTheGrantRemovesItsItems")]
    public async Task WorkQueueFollowsGrantsRevokingTheGrantRemovesItsItems()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var otherStudy = await SecondStudyAsync(tenant, ct);
        var submittedAt = _fixture.Clock.UtcNow;
        var covered = await _scenario.InReviewAsync(tenant, study, ct);
        Tick();
        await _scenario.InReviewAsync(tenant, otherStudy, ct);
        await DefineAsync(tenant, "Protocol approvals", ct);

        Tick();
        var grant = await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Approver, "protocol.approve", null, AuthorityScope.Study(study), _fixture.Clock.UtcNow, null, false, KernelProtocolScenario.Key(), "Approver appointed"), ct);
        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Agent, "protocol.approve", null, AuthorityScope.Tenant(), _fixture.Clock.UtcNow, null, false, KernelProtocolScenario.Key(), "Misconfigured agent"), ct);
        Tick();

        var queue = (await ReadAsync(Approver, tenant, new WorkQueueQuery(), ct)).Should().ContainSingle().Subject;
        queue.Code.Should().Be("protocol_approvals");
        queue.Capability.Should().Be("protocol.approve");
        queue.Truncated.Should().BeFalse();
        var item = queue.Items.Should().ContainSingle().Subject;
        item.Subject.Should().Be(NodeRef.For(covered.ProtocolVersionId, 2), "pinned to the row written by the submission");
        item.SubjectKind.Should().Be(NodeKinds.ProtocolVersion);
        item.Title.Should().StartWith("Protocol version 1 of ");
        item.Since.Should().Be(submittedAt, "an item is due from when the version was submitted");
        item.Masked.Should().BeNull();

        (await ReadAsync(Other, tenant, new WorkQueueQuery(), ct)).Single().Items.Should().BeEmpty("a queue is keyed by a capability, not shared");
        (await ReadAsync(Agent, tenant, new WorkQueueQuery(), ct)).Single().Items.Should().BeEmpty("an automation agent cannot approve (spec §11)");
        (await ReadAsync(Approver, tenant, new WorkQueueQuery(Within: NodeRef.For(otherStudy)), ct)).Single().Items.Should().BeEmpty();
        (await ReadAsync(Approver, tenant, new WorkQueueQuery(Within: NodeRef.For(study)), ct)).Single().Items.Should().ContainSingle();

        await _scenario.OkAsync(new RevokeGrant(tenant, grant.GrantId, _fixture.Clock.UtcNow, KernelProtocolScenario.Key(), "Left the study"), ct);
        Tick();

        (await ReadAsync(Approver, tenant, new WorkQueueQuery(), ct)).Single().Items.Should().BeEmpty("revoking the grant removes its items");
    }

    /// <summary>
    /// Defining a code again adds the next version, which the read uses from then on; earlier versions stay, and an
    /// unregistered source or a verb class other than the kernel command's is refused.
    /// </summary>
    [Fact(DisplayName = "DefineWorkQueue_AddsVersions_AndChecksSourceAndVerbClass")]
    public async Task DefineWorkQueueAddsVersionsAndChecksSourceAndVerbClass()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _) = await _scenario.NewStudyAsync(ct);

        var first = await DefineAsync(tenant, "Protocol approvals", ct);
        var second = await DefineAsync(tenant, "Protocols to approve", ct);

        first.Version.Should().Be(1);
        second.Version.Should().Be(2);
        var queue = (await ReadAsync(Approver, tenant, new WorkQueueQuery(), ct)).Should().ContainSingle().Subject;
        queue.Version.Should().Be(2);
        queue.Title.Should().Be("Protocols to approve");
        queue.Definition.Should().Be(NodeRef.For(second.WorkQueueDefinitionId, 1));
        await using (var db = _fixture.CreateContext(tenant))
        {
            (await db.WorkQueueDefinitions.AsNoTracking().CountAsync(ct)).Should().Be(2, "a definition version never changes");
            (await db.DomainEvents.AsNoTracking().CountAsync(e => e.Type == "WorkQueueDefined", ct)).Should().Be(2);
        }

        (await _fixture.Sender.SendAsync(Define(tenant, "Approvals", source: "etmf.documents_to_file"), cancellationToken: ct)).Error!.Code
            .Should().Be(WorkQueueErrors.SourceUnknown.Code);
        (await _fixture.Sender.SendAsync(Define(tenant, "Approvals", verbClass: VerbClass.Read), cancellationToken: ct)).Error!.Code
            .Should().Be(WorkQueueErrors.CapabilityInvalid.Code, "protocol.approve is an Approve capability");
    }

    /// <summary>A tenant's queues and the versions behind them are invisible from another tenant.</summary>
    [Fact(DisplayName = "WorkQueue_CannotReachAnotherTenant")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task WorkQueueCannotReachAnotherTenant()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (owner, study) = await _scenario.NewStudyAsync(ct);
        var (stranger, _) = await _scenario.NewStudyAsync(ct);
        await _scenario.InReviewAsync(owner, study, ct);
        await DefineAsync(owner, "Protocol approvals", ct);

        (await ReadAsync(Approver, stranger, new WorkQueueQuery(), ct)).Should().BeEmpty();
        var source = new ProtocolVersionsInReviewSource(_fixture.TenantContexts);
        (await source.CandidatesAsync(stranger, null, 10, ct)).Should().BeEmpty();
        (await source.CandidatesAsync(stranger, NodeRef.For(study), 10, ct)).Should().BeEmpty();
        (await source.CandidatesAsync(owner, null, 10, ct)).Should().ContainSingle();
    }

    private async Task<IReadOnlyList<WorkQueue>> ReadAsync(Actor reader, TenantId tenant, WorkQueueQuery query, CancellationToken ct)
    {
        _fixture.CurrentActor.Actor = reader;
        try
        {
            var result = await Queues().ReadAsync(tenant, query, ct);
            result.IsSuccess.Should().BeTrue(result.Error?.Message);
            return result.Value!;
        }
        finally
        {
            _fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }
    }

    private WorkQueueService Queues()
    {
        var evaluator = new AuthorityEvaluator(_fixture.AuthorityStore, new KernelAuthorityOptions(new HashSet<string>()));
        var probe = new KernelAuthorization(evaluator, _fixture.CurrentActor, KernelCommandCatalog.Kernel, _fixture.Clock, NullLogger<KernelAuthorization>.Instance);
        return new WorkQueueService(
            new KernelWorkQueueDefinitions(_fixture.TenantContexts),
            [new ProtocolVersionsInReviewSource(_fixture.TenantContexts)],
            probe,
            _fixture.Visibility,
            _fixture.CurrentActor,
            NullLogger<WorkQueueService>.Instance);
    }

    private Task<WorkQueueDefinitionState> DefineAsync(TenantId tenant, string title, CancellationToken ct) =>
        _scenario.OkAsync(Define(tenant, title), ct);

    private static DefineWorkQueue Define(
        TenantId tenant, string title, string source = KernelWorkItemSources.ProtocolVersionsInReview, VerbClass verbClass = VerbClass.Approve) =>
        new(tenant, WorkQueueDefinitionId.New(), "protocol_approvals", title, "protocol.approve", verbClass, source, KernelProtocolScenario.Key(), "Queue for approvers");

    private async Task<StudyId> SecondStudyAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = _fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Select(s => s.SponsorId).FirstAsync(ct);
        var study = new Study(StudyId.New(), tenant, sponsor, $"STUDY-{Guid.NewGuid():N}"[..16], "Second", "Phase 2", KernelPostgresFixture.ActorId, "Seeded for test");
        await _fixture.SeedAsync(study);
        return study.Id;
    }

    private void Tick() => _fixture.Clock.Advance(TimeSpan.FromSeconds(1));
}
