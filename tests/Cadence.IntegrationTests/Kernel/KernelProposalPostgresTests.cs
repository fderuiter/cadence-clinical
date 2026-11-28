using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Proposals and automation agents through the production pipeline and authorization against PostgreSQL (ADR-2489,
/// #4783): an agent proposes and only a person approves, with an attestation over the proposal's content hash; the
/// proposer cannot approve; a tampered or expired proposal cannot be approved; only the proposer withdraws; and the
/// proposal sits in its study for lineage, visibility and navigation. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelProposalPostgresTests : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string Reason = KernelProtocolScenario.Reason;
    private const string Model = "claude-opus-5";

    private static readonly Actor Requester = Actor.Person("data-manager@sponsor.test");
    private static readonly Actor AgentIdentity = Actor.AutomationAgent("deviation-triage");
    private static readonly Actor Agent = AgentIdentity.ActingFor(Requester);
    private static readonly Actor Approver = Actor.Person("approver@sponsor.test");
    private static readonly Actor Colleague = Actor.Person("colleague@sponsor.test");

    /// <summary>Tables a proposal, its approval attestation and the pipeline's records may change; nothing else may.</summary>
    private static readonly HashSet<string> ProposalWrites = new(StringComparer.Ordinal)
    {
        "proposals", "attestations", "provenance_activities", "domain_events", "audit_entries", "audit_chain_heads",
        "outbox_messages", "idempotency_records", "lineage_nodes", "lineage_edges", "activity_entries",
    };

    private readonly KernelPostgresFixture _fixture;
    private readonly KernelProtocolScenario _scenario;

    public KernelProposalPostgresTests(KernelPostgresFixture fixture)
    {
        _fixture = fixture;
        _scenario = new KernelProtocolScenario(fixture);
    }

    public void Dispose()
    {
        if (_fixture.ConnectionString is not null)
        {
            _fixture.Authorizer.Inner = null;
            _fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }
    }

    /// <summary>
    /// An automation agent holding <c>proposal.create</c> on a study proposes a change, naming its model and plan; a
    /// person holding <c>proposal.approve</c> signs an Approved attestation over the proposal's content hash and approves
    /// it. The proposal records the agent, the model and the plan, the approval records the person and the attestation
    /// (dual attribution), and nothing else in the kernel changes: the plan, the affected objects and every other table
    /// are as they were (K-SIM-5).
    /// </summary>
    [Fact(DisplayName = "Proposal_AgentProposes_PersonApprovesWithAttestation_NothingElseChanges")]
    [KernelRule("K-SIM-5")]
    [KernelRule("K-PROV-3")]
    public async Task ProposalAgentProposesPersonApprovesWithAttestationNothingElseChanges()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (artifact, plan) = await RegisterPlanAsync(tenant, study, ct);
        var (version, _) = await _scenario.ApprovedAsync(tenant, study, ct);
        await GrantAgentAsync(tenant, "proposal.create", AuthorityScope.Study(study), ct);
        await GrantAsync(tenant, Approver, "proposal.approve", AuthorityScope.Study(study), ct);
        await GrantAsync(tenant, Approver, "attestation.sign", AuthorityScope.Tenant(), ct);
        UseProductionAuthorization();
        var affected = NodeRef.For(version.ProtocolVersionId, 3);
        var before = await SnapshotAsync(ct);
        var proposalId = ProposalId.New();

        var proposed = await As(Agent, Propose(tenant, proposalId, study, plan, [affected, NodeRef.For(study)], model: Model), ct);
        Tick();
        var attestation = await As(Approver, _scenario.AttestCommand(tenant, AttestationMeaning.Approved, Target(proposed)), ct);
        var approved = await As(Approver, new ApproveProposal(tenant, proposalId, attestation, KernelProtocolScenario.Key(), "Plan reviewed"), ct);

        proposed.Status.Should().Be(ProposalStatus.Open);
        proposed.ProposedBy.Should().Be(Agent.Id);
        proposed.ProposedByKind.Should().Be(ActorKind.AutomationAgent);
        proposed.ModelId.Should().Be(Model);
        approved.Should().BeEquivalentTo(proposed with
        {
            Status = ProposalStatus.Approved,
            DecidedAt = _fixture.Clock.UtcNow,
            DecidedBy = Approver.Id,
            ApprovalAttestationId = attestation,
        });

        await using var db = _fixture.CreateContext(tenant);
        var stored = await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == proposalId, ct);
        stored.PlanArtifactVersionId.Should().Be(plan);
        stored.ModelId.Should().Be(Model);
        stored.ProposedById.Should().Be(Agent.Id);
        stored.DecidedById.Should().Be(Approver.Id);
        stored.ApprovalAttestationId.Should().Be(attestation);
        stored.ContentHash.Should().Be(proposed.ContentHash);
        stored.VersionIndex.Should().Be(2);

        var events = await db.DomainEvents.AsNoTracking().Where(e => e.TenantId == tenant && e.Recorded >= before.At)
            .OrderBy(e => e.Recorded).ThenBy(e => e.EventId).Select(e => new { e.Type, e.ActorId }).ToListAsync(ct);
        events.Should().Equal(
            new { Type = "ProposalCreated", ActorId = Agent.Id },
            new { Type = "AttestationRecorded", ActorId = Approver.Id },
            new { Type = "ProposalApproved", ActorId = Approver.Id });
        var activities = await db.ProvenanceActivities.AsNoTracking().Where(a => a.TenantId == tenant && a.RecordedAt >= before.At)
            .OrderBy(a => a.RecordedAt).Select(a => new { a.CommandType, a.ActorId, a.ActorKind, a.Outcome }).ToListAsync(ct);
        activities.Should().Equal(
            new { CommandType = nameof(Propose), ActorId = Agent.Id, ActorKind = ActorKind.AutomationAgent, Outcome = "Committed" },
            new { CommandType = nameof(Attest), ActorId = Approver.Id, ActorKind = ActorKind.Person, Outcome = "Committed" },
            new { CommandType = nameof(ApproveProposal), ActorId = Approver.Id, ActorKind = ActorKind.Person, Outcome = "Committed" });

        var after = await SnapshotAsync(ct);
        after.Tables.Where(t => !ProposalWrites.Contains(t.Key)).Should().BeEquivalentTo(
            before.Tables.Where(t => !ProposalWrites.Contains(t.Key)),
            "approving a proposal does not apply it: the plan, the affected objects and every other table are unchanged");
        (await db.Artifacts.AsNoTracking().SingleAsync(a => a.Id == artifact, ct)).VersionIndex.Should().Be(1);
        (await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == version.ProtocolVersionId, ct)).VersionIndex.Should().Be(3);
    }

    /// <summary>
    /// An automation agent cannot approve or reject a proposal, nor sign the attestation an approval needs, even with a
    /// grant for it: the authorizer denies every Execute and Approve command to an agent (K-AI-1, actor.agent_verb), and
    /// each denial is audited.
    /// </summary>
    [Fact(DisplayName = "Proposal_Agent_CannotApproveRejectOrAttest")]
    [KernelRule("K-AI-1")]
    public async Task ProposalAgentCannotApproveRejectOrAttest()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (_, plan) = await RegisterPlanAsync(tenant, study, ct);
        await GrantAgentAsync(tenant, "proposal.create", AuthorityScope.Study(study), ct);
        foreach (var capability in new[] { "proposal.approve", "proposal.reject", "attestation.sign" })
        {
            await GrantAgentAsync(tenant, capability, AuthorityScope.Tenant(), ct);
        }

        UseProductionAuthorization();
        var proposalId = ProposalId.New();
        var proposed = await As(Agent, Propose(tenant, proposalId, study, plan, [], model: Model), ct);

        var attempts = new ICommand[]
        {
            new ApproveProposal(tenant, proposalId, AttestationId.New(), KernelProtocolScenario.Key(), Reason),
            new RejectProposal(tenant, proposalId, KernelProtocolScenario.Key(), Reason),
            _scenario.AttestCommand(tenant, AttestationMeaning.Approved, Target(proposed)),
        };
        foreach (var attempt in attempts)
        {
            var error = await SendErrorAs(Agent, attempt, ct);
            error.Code.Should().Be(KernelErrors.AuthorizationDenied.Code, attempt.GetType().Name);
            error.Explanation!.Conditions.Should().ContainSingle(c => c.Code == AuthorityEvaluator.AgentVerbCondition && !c.Met, attempt.GetType().Name);
        }

        await using var db = _fixture.CreateContext(tenant);
        (await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == proposalId, ct)).Status.Should().Be(ProposalStatus.Open);
        (await db.DomainEvents.AsNoTracking().CountAsync(e => e.TenantId == tenant && e.Type == "DecisionDenied", ct)).Should().Be(attempts.Length);
    }

    /// <summary>
    /// A person who proposed cannot approve their own proposal, even with a valid Approved attestation (separation of
    /// duties). The model rule holds through the pipeline: an agent must name its model and a person must not.
    /// </summary>
    [Fact(DisplayName = "Proposal_Proposer_CannotApproveOwnProposal")]
    public async Task ProposalProposerCannotApproveOwnProposal()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (_, plan) = await RegisterPlanAsync(tenant, study, ct);
        await GrantAgentAsync(tenant, "proposal.create", AuthorityScope.Study(study), ct);
        foreach (var capability in new[] { "proposal.create", "proposal.approve", "attestation.sign" })
        {
            await GrantAsync(tenant, Approver, capability, AuthorityScope.Tenant(), ct);
        }

        UseProductionAuthorization();
        (await SendErrorAs(Approver, Propose(tenant, ProposalId.New(), study, plan, [], model: Model), ct)).Code
            .Should().Be(ProposalErrors.ModelNotAllowed.Code);
        (await SendErrorAs(Agent, Propose(tenant, ProposalId.New(), study, plan, [], model: null), ct)).Code
            .Should().Be(ProposalErrors.ModelRequired.Code);

        var proposalId = ProposalId.New();
        var proposed = await As(Approver, Propose(tenant, proposalId, study, plan, [], model: null), ct);
        proposed.ModelId.Should().BeNull();
        var attestation = await As(Approver, _scenario.AttestCommand(tenant, AttestationMeaning.Approved, Target(proposed)), ct);

        (await SendErrorAs(Approver, new ApproveProposal(tenant, proposalId, attestation, KernelProtocolScenario.Key(), Reason), ct))
            .Should().Be(ProposalErrors.SelfApproval);

        await using var db = _fixture.CreateContext(tenant);
        (await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == proposalId, ct)).Status.Should().Be(ProposalStatus.Open);
    }

    /// <summary>
    /// An approval must sign this proposal's content: an attestation over another proposal, over a hash the proposal
    /// does not have, or with another meaning is refused, and the database refuses to change a proposal's content even
    /// for the superuser, so the signed hash always matches. Once the proposal expires it can be neither approved nor
    /// rejected, and it reads as expired.
    /// </summary>
    [Fact(DisplayName = "Proposal_TamperedOrExpired_CannotBeApproved")]
    public async Task ProposalTamperedOrExpiredCannotBeApproved()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (_, plan) = await RegisterPlanAsync(tenant, study, ct);
        var target = ProposalId.New();
        var proposed = await _scenario.OkAsync(Propose(tenant, target, study, plan, [], model: null), ct);
        var other = await _scenario.OkAsync(Propose(tenant, ProposalId.New(), study, plan, [], model: null) with { Intent = "Something else" }, ct);
        _fixture.CurrentActor.Actor = Approver;

        var forged = Target(proposed) with { ContentHash = other.ContentHash };
        (await SendErrorAs(Approver, _scenario.AttestCommand(tenant, AttestationMeaning.Approved, forged), ct)).Should().Be(TrustErrors.TargetContentChanged);
        (await SendErrorAs(Approver, _scenario.AttestCommand(tenant, AttestationMeaning.Approved, Target(proposed) with { Id = Guid.NewGuid() }), ct))
            .Should().Be(TrustErrors.TargetUnknown);

        var overOther = await As(Approver, _scenario.AttestCommand(tenant, AttestationMeaning.Approved, Target(other)), ct);
        var reviewedOnly = await As(Approver, _scenario.AttestCommand(tenant, AttestationMeaning.Reviewed, Target(proposed)), ct);
        foreach (var wrong in new[] { overOther, reviewedOnly, AttestationId.New() })
        {
            (await SendErrorAs(Approver, new ApproveProposal(tenant, target, wrong, KernelProtocolScenario.Key(), Reason), ct)).Code
                .Should().Be(ProposalErrors.AttestationRejected.Code);
        }

        foreach (var statement in new[]
        {
            $"UPDATE kernel.proposals SET intent = 'Something else' WHERE id = '{target.Value}'",
            $"UPDATE kernel.proposals SET requested_capabilities = ARRAY['protocol.approve'] WHERE id = '{target.Value}'",
            $"UPDATE kernel.proposals SET content_hash = '{other.ContentHash}' WHERE id = '{target.Value}'",
        })
        {
            var act = () => ExecuteAsAdminAsync(statement, ct);
            (await act.Should().ThrowAsync<PostgresException>(statement)).Which.SqlState.Should().Be(ImmutableColumnsSql.ViolationSqlState);
        }

        var signed = await As(Approver, _scenario.AttestCommand(tenant, AttestationMeaning.Approved, Target(proposed)), ct);
        _fixture.Clock.Advance(proposed.ExpiresAt - _fixture.Clock.UtcNow);

        (await SendErrorAs(Approver, new ApproveProposal(tenant, target, signed, KernelProtocolScenario.Key(), Reason), ct)).Should().Be(ProposalErrors.Expired);
        (await SendErrorAs(Approver, new RejectProposal(tenant, target, KernelProtocolScenario.Key(), Reason), ct)).Should().Be(ProposalErrors.Expired);

        await using var db = _fixture.CreateContext(tenant);
        var stored = await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == target, ct);
        stored.Status.Should().Be(ProposalStatus.Open, "expiry is derived, never stored");
        stored.StatusAt(_fixture.Clock.UtcNow).Should().Be(ProposalStatus.Expired);
    }

    /// <summary>
    /// Only the proposer withdraws a proposal: another person holding <c>proposal.withdraw</c> is refused, the agent that
    /// proposed withdraws it, and a withdrawn proposal can no longer be decided. A person rejects another open proposal
    /// with the command's reason.
    /// </summary>
    [Fact(DisplayName = "Proposal_OnlyProposer_Withdraws")]
    public async Task ProposalOnlyProposerWithdraws()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (_, plan) = await RegisterPlanAsync(tenant, study, ct);
        foreach (var capability in new[] { "proposal.create", "proposal.withdraw" })
        {
            await GrantAgentAsync(tenant, capability, AuthorityScope.Study(study), ct);
        }

        foreach (var capability in new[] { "proposal.withdraw", "proposal.reject" })
        {
            await GrantAsync(tenant, Colleague, capability, AuthorityScope.Study(study), ct);
        }

        UseProductionAuthorization();
        var withdrawn = ProposalId.New();
        var rejected = ProposalId.New();
        await As(Agent, Propose(tenant, withdrawn, study, plan, [], model: Model), ct);
        await As(Agent, Propose(tenant, rejected, study, plan, [], model: Model), ct);
        Tick();

        (await SendErrorAs(Colleague, new WithdrawProposal(tenant, withdrawn, KernelProtocolScenario.Key(), "Not mine to withdraw"), ct))
            .Should().Be(ProposalErrors.NotProposer);
        var state = await As(Agent, new WithdrawProposal(tenant, withdrawn, KernelProtocolScenario.Key(), "A better plan exists"), ct);
        state.Status.Should().Be(ProposalStatus.Withdrawn);
        state.DecidedBy.Should().Be(Agent.Id);
        (await SendErrorAs(Colleague, new RejectProposal(tenant, withdrawn, KernelProtocolScenario.Key(), Reason), ct)).Code
            .Should().Be(ProposalErrors.NotOpen.Code);

        var rejection = await As(Colleague, new RejectProposal(tenant, rejected, KernelProtocolScenario.Key(), "Outside the study's scope"), ct);
        rejection.Status.Should().Be(ProposalStatus.Rejected);

        await using var db = _fixture.CreateContext(tenant);
        (await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == rejected, ct)).DecisionReason.Should().Be("Outside the study's scope");
        var events = await db.DomainEvents.AsNoTracking().Where(e => e.TenantId == tenant && e.Type.StartsWith("Proposal"))
            .OrderBy(e => e.Recorded).ThenBy(e => e.EventId).Select(e => e.Type).ToListAsync(ct);
        events.Should().Equal("ProposalCreated", "ProposalCreated", "ProposalWithdrawn", "ProposalRejected");
    }

    /// <summary>
    /// A proposal names only objects its proposer may see (K-AI-3): a plan or an affected object hidden from the proposer
    /// is refused without saying which, so a proposal never links into scope its proposer cannot read.
    /// </summary>
    [Fact(DisplayName = "Proposal_NamingAnObjectTheProposerMayNotSee_IsRefused")]
    [KernelRule("K-AI-3")]
    public async Task ProposalNamingAnObjectTheProposerMayNotSeeIsRefused()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (_, plan) = await RegisterPlanAsync(tenant, study, ct);
        var (_, hiddenPlan) = await RegisterPlanAsync(tenant, study, ct);
        var hidden = new NodeRef(NodeKinds.Enrollment, Guid.NewGuid(), 1);
        _fixture.Visibility.Masked[hidden.Unversioned()] = MaskReason.NotPermitted;
        _fixture.Visibility.Masked[NodeRef.For(hiddenPlan)] = MaskReason.NotPermitted;
        await GrantAgentAsync(tenant, "proposal.create", AuthorityScope.Study(study), ct);
        UseProductionAuthorization();

        (await SendErrorAs(Agent, Propose(tenant, ProposalId.New(), study, plan, [hidden], model: Model), ct))
            .Should().Be(ProposalErrors.NotVisible);
        (await SendErrorAs(Agent, Propose(tenant, ProposalId.New(), study, hiddenPlan, [], model: Model), ct))
            .Should().Be(ProposalErrors.NotVisible);
        await As(Agent, Propose(tenant, ProposalId.New(), study, plan, [], model: Model), ct);
    }

    /// <summary>
    /// A proposal derives from its plan and is defined by each affected object version in lineage; an unversioned
    /// affected reference is kept on the proposal only. Its scope path is its study's, so a reader with a grant on another
    /// study does not see it while a reader with a grant on its study does (K-AI-3), and navigation places it under the
    /// study.
    /// </summary>
    [Fact(DisplayName = "Proposal_Lineage_AndVisibility_PlaceItInItsStudy")]
    public async Task ProposalLineageAndVisibilityPlaceItInItsStudy()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (_, otherStudy) = await _scenario.NewStudyAsync(tenant, ct);
        var (_, plan) = await RegisterPlanAsync(tenant, study, ct);
        var (_, tenantWidePlan) = await RegisterPlanAsync(tenant, studyId: null, ct);
        var (_, foreignPlan) = await RegisterPlanAsync(tenant, otherStudy, ct);
        var version = await _scenario.InReviewAsync(tenant, study, ct);
        var versioned = NodeRef.For(version.ProtocolVersionId, 2);
        var proposalId = ProposalId.New();

        (await _fixture.Sender.SendAsync(Propose(tenant, ProposalId.New(), study, foreignPlan, [], model: null), cancellationToken: ct)).Error!.Code
            .Should().Be(ProposalErrors.PlanInvalid.Code);
        (await _fixture.Sender.SendAsync(Propose(tenant, ProposalId.New(), study, ArtifactVersionId.New(), [], model: null), cancellationToken: ct)).Error!.Code
            .Should().Be(ProposalErrors.PlanInvalid.Code);
        (await _fixture.Sender.SendAsync(Propose(tenant, ProposalId.New(), StudyId.New(), plan, [], model: null), cancellationToken: ct)).Error
            .Should().Be(ProposalErrors.StudyUnknown);
        await _scenario.OkAsync(Propose(tenant, ProposalId.New(), study, tenantWidePlan, [], model: null), ct);
        await _scenario.OkAsync(Propose(tenant, proposalId, study, plan, [versioned, NodeRef.For(study)], model: null), ct);

        await using (var db = _fixture.CreateContext(tenant))
        {
            var inbound = await db.LineageEdges.AsNoTracking()
                .Where(e => e.ToKind == NodeKinds.Proposal && e.ToId == proposalId.Value)
                .Select(e => new { e.FromKind, e.FromId, e.FromVersion, e.ToVersion, e.Kind })
                .ToListAsync(ct);
            inbound.Should().BeEquivalentTo(new[]
            {
                new { FromKind = NodeKinds.ArtifactVersion, FromId = plan.Value, FromVersion = 1L, ToVersion = 1L, Kind = LineageEdgeKind.DerivedFrom },
                new { FromKind = NodeKinds.ProtocolVersion, FromId = version.ProtocolVersionId.Value, FromVersion = 2L, ToVersion = 1L, Kind = LineageEdgeKind.DefinedBy },
            });
        }

        await GrantAsync(tenant, Approver, "proposal.approve", AuthorityScope.Study(study), ct);
        await GrantAsync(tenant, Colleague, "proposal.approve", AuthorityScope.Study(otherStudy), ct);
        var visibility = new GrantNodeVisibility(_fixture.AuthorityStore, _fixture.Clock);
        var proposalRef = NodeRef.For(proposalId, 1);

        (await visibility.MaskAsync(tenant, Colleague, [proposalRef], ct)).Should().ContainKey(proposalRef);
        (await visibility.MaskAsync(tenant, Approver, [proposalRef], ct)).Should().BeEmpty();

        var path = await _fixture.AuthorityStore.PathOfAsync(tenant, proposalRef, ct);
        path.Study.Should().Be(study);
        var node = await new KernelScopeGraph(_fixture.TenantContexts).FindAsync(tenant, proposalRef, ct);
        node!.ZoomParent.Should().Be(NodeRef.For(study));
        node.Summary.Ref.Should().Be(proposalRef);
        node.Summary.Status.Should().Be(nameof(ProposalStatus.Open));
    }

    /// <summary>
    /// A proposal created with low confidence (&lt; 0.75) or ForceEscalation policy is automatically marked as escalated.
    /// An open proposal can also be explicitly escalated via <see cref="EscalateProposal"/>.
    /// </summary>
    [Fact(DisplayName = "Proposal_ConfidenceAndEscalation_AutoEscalatesAndAllowsManualEscalation")]
    public async Task ProposalConfidenceAndEscalationAutoEscalatesAndAllowsManualEscalation()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (_, plan) = await RegisterPlanAsync(tenant, study, ct);
        await GrantAgentAsync(tenant, "proposal.create", AuthorityScope.Study(study), ct);
        await GrantAsync(tenant, Colleague, "proposal.escalate", AuthorityScope.Study(study), ct);
        UseProductionAuthorization();

        var autoEscalatedId = ProposalId.New();
        var autoEscalated = await As(Agent, Propose(tenant, autoEscalatedId, study, plan, [], model: Model) with { ConfidenceScore = 0.60 }, ct);
        autoEscalated.IsEscalated.Should().BeTrue();
        autoEscalated.ConfidenceTier.Should().Be("Low");
        autoEscalated.EscalatedByKind.Should().Be(ActorKind.AutomationAgent);

        var normalId = ProposalId.New();
        var normal = await As(Agent, Propose(tenant, normalId, study, plan, [], model: Model) with { ConfidenceScore = 0.95 }, ct);
        normal.IsEscalated.Should().BeFalse();
        normal.ConfidenceTier.Should().Be("High");

        var escalated = await As(Colleague, new EscalateProposal(tenant, normalId, "SafetyLead", KernelProtocolScenario.Key(), "Safety concerns raised during review"), ct);
        escalated.IsEscalated.Should().BeTrue();
        escalated.EscalatedById.Should().Be(Colleague.Id);
        escalated.EscalatedByKind.Should().Be(ActorKind.Person);
        escalated.EscalatedTo.Should().Be("SafetyLead");
        escalated.EscalationReason.Should().Be("Safety concerns raised during review");

        await using var db = _fixture.CreateContext(tenant);
        var storedAuto = await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == autoEscalatedId, ct);
        storedAuto.IsEscalated.Should().BeTrue();
        storedAuto.ConfidenceScore.Should().Be(0.60);

        var storedManual = await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == normalId, ct);
        storedManual.IsEscalated.Should().BeTrue();
        storedManual.EscalatedTo.Should().Be("SafetyLead");
        storedManual.EscalationReason.Should().Be("Safety concerns raised during review");
    }

    private void Tick() => _fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private Propose Propose(TenantId tenant, ProposalId id, StudyId study, ArtifactVersionId plan, IReadOnlyList<NodeRef> affected, string? model) =>
        new(
            tenant,
            id,
            study,
            "Close the stale deviation queries at site 101",
            plan,
            affected,
            SimulatedImpact: ContentHash.Compute("""{"simulation":"stale-queries"}"""),
            ["protocol.approve", "effectivity.change_status"],
            _fixture.Clock.UtcNow.AddDays(7),
            model,
            KernelProtocolScenario.Key(),
            "Triage suggestion");

    private static AttestationTarget Target(ProposalState proposal) => new(NodeKinds.Proposal, proposal.ProposalId.Value, proposal.ContentHash);

    private async Task<(ArtifactId Artifact, ArtifactVersionId Version)> RegisterPlanAsync(TenantId tenant, StudyId? studyId, CancellationToken ct)
    {
        var bytes = $"plan-{Guid.NewGuid():N}";
        var artifact = ArtifactId.New();
        var version = ArtifactVersionId.New();
        var content = new ArtifactContent(ContentHash.OfBytes(Encoding.UTF8.GetBytes(bytes)), bytes.Length, "application/json", $"blob://plans/{bytes}");
        await _scenario.OkAsync(
            new RegisterArtifact(tenant, artifact, version, ArtifactKind.Other, studyId, "Proposed plan and reasoning", content, KernelProtocolScenario.Key(), "Plan filed"),
            ct);
        return (artifact, version);
    }

    private async Task GrantAsync(TenantId tenant, Actor grantee, string capability, AuthorityScope scope, CancellationToken ct)
    {
        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), grantee, capability, null, scope, _fixture.Clock.UtcNow, null, false, KernelProtocolScenario.Key(), "Appointed to role"),
            ct);
        Tick();
    }

    /// <summary>Grants the agent a capability, and the person it acts for the same, since the agent is bounded by both (K-AI-6).</summary>
    private async Task GrantAgentAsync(TenantId tenant, string capability, AuthorityScope scope, CancellationToken ct)
    {
        await GrantAsync(tenant, AgentIdentity, capability, scope, ct);
        await GrantAsync(tenant, Requester, capability, scope, ct);
    }

    private void UseProductionAuthorization()
    {
        var evaluator = new AuthorityEvaluator(_fixture.AuthorityStore, new KernelAuthorityOptions(new HashSet<string>()));
        _fixture.Authorizer.Inner = new KernelAuthorization(
            evaluator, _fixture.CurrentActor, KernelCommandCatalog.Kernel, _fixture.Clock, NullLogger<KernelAuthorization>.Instance);
    }

    private async Task<TResult> As<TResult>(Actor actor, ICommand<TResult> command, CancellationToken ct)
    {
        _fixture.CurrentActor.Actor = actor;
        var result = await _fixture.Sender.SendAsync(command, cancellationToken: ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }

    private async Task<KernelError> SendErrorAs(Actor actor, ICommand command, CancellationToken ct)
    {
        _fixture.CurrentActor.Actor = actor;
        var error = command switch
        {
            ICommand<ProposalState> proposal => (await _fixture.Sender.SendAsync(proposal, cancellationToken: ct)).Error,
            ICommand<AttestationId> attest => (await _fixture.Sender.SendAsync(attest, cancellationToken: ct)).Error,
            _ => throw new ArgumentException($"Unexpected command {command.GetType().Name}.", nameof(command)),
        };
        error.Should().NotBeNull($"{command.GetType().Name} must be refused");
        return error!;
    }

    /// <summary>A digest of every kernel table's rows, as the superuser sees them, and the clock time it was taken.</summary>
    private async Task<(DateTimeOffset At, Dictionary<string, string> Tables)> SnapshotAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'kernel' AND table_type = 'BASE TABLE' ORDER BY table_name", connection))
        await using (var reader = await list.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var digest = new NpgsqlCommand(
                $"SELECT count(*)::text || ':' || coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), '') FROM kernel.\"{table}\" t", connection);
            digests[table] = (string)(await digest.ExecuteScalarAsync(ct))!;
        }

        return (_fixture.Clock.UtcNow, digests);
    }

    private async Task ExecuteAsAdminAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
