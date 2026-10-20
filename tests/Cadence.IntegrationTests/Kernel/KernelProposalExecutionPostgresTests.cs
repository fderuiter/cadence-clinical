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
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The K-AI-2 execution link and requester-scoped agents (ADR-2632, #4783) through the production pipeline and
/// authorization against PostgreSQL: an agent proposes for one person, another person approves with an attestation and
/// executes the proposal's command as themselves, and the kernel links proposal → approval → activity → events. Skipped
/// unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelProposalExecutionPostgresTests : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string Reason = KernelProtocolScenario.Reason;
    private const string Model = "claude-opus-5";

    private static readonly Actor Requester = Actor.Person("data-manager@sponsor.test");
    private static readonly Actor AgentIdentity = Actor.AutomationAgent("protocol-reviewer");
    private static readonly Actor Agent = AgentIdentity.ActingFor(Requester);
    private static readonly Actor Approver = Actor.Person("study-lead@sponsor.test");
    private static readonly Actor Colleague = Actor.Person("colleague@sponsor.test");
    private static readonly Actor Setup = Actor.Person(KernelPostgresFixture.ActorId);

    private readonly KernelPostgresFixture _fixture;
    private readonly KernelProtocolScenario _scenario;

    public KernelProposalExecutionPostgresTests(KernelPostgresFixture fixture)
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
    /// An agent acting for the data manager proposes approving a protocol version; the study lead approves the proposal
    /// with an attestation and then approves the protocol version as themselves, naming the proposal. The command's
    /// provenance activity names the proposal, a <c>ProposalExecuted</c> event in the same activity names the agent,
    /// model, requester, approver and approval attestation, and lineage records the protocol version as executed under
    /// the approved proposal version (K-AI-2). The proposal records the person the agent acted for (K-AI-6).
    /// </summary>
    [Fact(DisplayName = "ProposalExecution_ApproverExecutes_LinksProposalApprovalActivityAndEvents")]
    [GxPRequirement("PRD-SYS-051")]
    [KernelRule("K-AI-2")]
    public async Task ProposalExecutionApproverExecutesLinksProposalApprovalActivityAndEvents()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var version = await _scenario.InReviewAsync(tenant, study, ct);
        await SetUpAsync(tenant, study, ct);
        var proposalId = ProposalId.New();
        var approval = await ApprovedProposalAsync(tenant, study, proposalId, ["protocol.approve"], ct);
        var signed = await As(Approver, _scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(version)), ct);
        Tick();

        var approved = await As(Approver, new ApproveProtocolVersion(tenant, version.ProtocolVersionId, signed, KernelProtocolScenario.Key(), Reason), ct, proposalId);

        approved.Status.Should().Be(ProtocolVersionStatus.Approved);
        await using var db = _fixture.CreateContext(tenant);
        var proposal = await db.Proposals.AsNoTracking().SingleAsync(p => p.Id == proposalId, ct);
        proposal.RequestedById.Should().Be(Requester.Id);
        var activity = await db.ProvenanceActivities.AsNoTracking()
            .SingleAsync(a => a.TenantId == tenant && a.ProposalId == proposalId.Value, ct);
        activity.CommandType.Should().Be(nameof(ApproveProtocolVersion));
        activity.ActorId.Should().Be(Approver.Id);
        activity.ActorKind.Should().Be(ActorKind.Person);
        activity.RequestedBy.Should().BeNull("the person executes as themselves");
        var proposed = await db.ProvenanceActivities.AsNoTracking()
            .SingleAsync(a => a.TenantId == tenant && a.CommandType == nameof(Propose) && a.ActorId == Agent.Id, ct);
        proposed.RequestedBy.Should().Be(Requester.Id, "the agent's activity names the person it acted for");

        var events = await db.DomainEvents.AsNoTracking().Where(e => e.ActivityId == activity.Id).OrderBy(e => e.EventId).ToListAsync(ct);
        events.Select(e => e.Type).Should().Contain(["ProtocolVersionApproved", "ProposalExecuted"]);
        var executed = events.Single(e => e.Type == "ProposalExecuted").Payload;
        executed.Should().Contain(proposalId.Value.ToString()).And.Contain(Agent.Id).And.Contain(Model).And.Contain(Requester.Id)
            .And.Contain(Approver.Id).And.Contain(approval.Value.ToString()).And.Contain("protocol.approve");

        var edge = await db.LineageEdges.AsNoTracking()
            .SingleAsync(e => e.Kind == LineageEdgeKind.ExecutedUnder && e.FromId == proposalId.Value, ct);
        edge.FromKind.Should().Be(NodeKinds.Proposal);
        edge.FromVersion.Should().Be(2, "the approved proposal is its second version");
        edge.ToKind.Should().Be(NodeKinds.ProtocolVersion);
        edge.ToId.Should().Be(version.ProtocolVersionId.Value);
    }

    /// <summary>
    /// A command runs under a proposal only when the proposal exists, is approved and unexpired, was approved by the
    /// acting person, requested the command's capability, and the command acts in the proposal's study (K-AI-2). The
    /// proposal never widens authority: the approver still needs the capability. Nothing is written for a refusal.
    /// </summary>
    [Fact(DisplayName = "ProposalExecution_RefusesEveryUnlinkedExecution")]
    [GxPRequirement("PRD-SYS-051")]
    [KernelRule("K-AI-2")]
    public async Task ProposalExecutionRefusesEveryUnlinkedExecution()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (_, otherStudy) = await _scenario.NewStudyAsync(tenant, ct);
        await SetUpAsync(tenant, study, ct);
        await GrantAsync(tenant, Approver, "protocol.approve", AuthorityScope.Study(otherStudy), ct);
        await GrantAsync(tenant, Colleague, "protocol.approve", AuthorityScope.Study(study), ct);
        await GrantAsync(tenant, Colleague, "attestation.sign", AuthorityScope.Tenant(), ct);

        var approvedId = ProposalId.New();
        await ApprovedProposalAsync(tenant, study, approvedId, ["protocol.approve"], ct);
        var otherCapability = ProposalId.New();
        await ApprovedProposalAsync(tenant, study, otherCapability, ["effectivity.change_status"], ct);
        var open = ProposalId.New();
        await As(Agent, Propose(tenant, open, study, await RegisterPlanAsync(tenant, study, ct), ["protocol.approve"]), ct);

        var here = await InReviewSignedAsync(tenant, study, ct);
        var there = await InReviewSignedAsync(tenant, otherStudy, ct);

        (await ExecuteAsync(tenant, here, Approver, ProposalId.New(), ct)).Should().Be(ProposalErrors.ExecutionUnknown);
        (await ExecuteAsync(tenant, here, Approver, open, ct)).Code.Should().Be(ProposalErrors.NotApproved.Code);
        (await ExecuteAsync(tenant, here, Colleague, approvedId, ct)).Should().Be(ProposalErrors.NotApprover);
        (await ExecuteAsync(tenant, here, Approver, otherCapability, ct)).Code.Should().Be(ProposalErrors.CapabilityNotRequested.Code);
        (await ExecuteAsync(tenant, there, Approver, approvedId, ct)).Should().Be(ProposalErrors.OutsideStudy);

        _fixture.CurrentActor.Actor = Setup;
        _fixture.Authorizer.Inner = null;
        await _scenario.OkAsync(
            new RevokeGrant(tenant, (await GrantsOfAsync(tenant, Approver, "protocol.approve", study, ct)).Single(), _fixture.Clock.UtcNow, KernelProtocolScenario.Key(), "Rotated off the study"),
            ct);
        Tick();
        UseProductionAuthorization();
        (await ExecuteAsync(tenant, here, Approver, approvedId, ct)).Code.Should().Be(KernelErrors.AuthorizationDenied.Code, "a proposal never widens authority");

        await GrantAsync(tenant, Approver, "protocol.approve", AuthorityScope.Study(study), ct);
        _fixture.Clock.Advance(TimeSpan.FromDays(8));
        (await ExecuteAsync(tenant, here, Approver, approvedId, ct)).Should().Be(ProposalErrors.ExecutionExpired);

        await using var db = _fixture.CreateContext(tenant);
        (await db.ProvenanceActivities.AsNoTracking().CountAsync(a => a.TenantId == tenant && a.ProposalId != null, ct)).Should().Be(0);
        (await db.DomainEvents.AsNoTracking().CountAsync(e => e.TenantId == tenant && e.Type == "ProposalExecuted", ct)).Should().Be(0);
    }

    /// <summary>
    /// An agent sees only what both it and the person it acts for may see, and nothing when it acts for no one (K-AI-6):
    /// acting for a person with a grant on another study it does not see this study's proposal, though its own grant
    /// covers the study. It cannot propose for nobody, and a grant cannot name an agent acting for someone.
    /// </summary>
    [Fact(DisplayName = "Agent_ReadsAndProposes_OnlyWithinTheRequestingPersonsScope")]
    [GxPRequirement("PRD-SYS-051")]
    [KernelRule("K-AI-6")]
    public async Task AgentReadsAndProposesOnlyWithinTheRequestingPersonsScope()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (_, otherStudy) = await _scenario.NewStudyAsync(tenant, ct);
        await SetUpAsync(tenant, study, ct);
        await GrantAsync(tenant, Colleague, "proposal.create", AuthorityScope.Study(otherStudy), ct);
        var proposalId = ProposalId.New();
        var plan = await RegisterPlanAsync(tenant, study, ct);
        await As(Agent, Propose(tenant, proposalId, study, plan, ["protocol.approve"]), ct);

        var visibility = new GrantNodeVisibility(_fixture.AuthorityStore, _fixture.Clock);
        var proposal = NodeRef.For(proposalId, 1);
        (await visibility.MaskAsync(tenant, Agent, [proposal], ct)).Should().BeEmpty();
        (await visibility.MaskAsync(tenant, AgentIdentity, [proposal], ct)).Should().ContainKey(proposal, "an agent acting for no one sees nothing");
        (await visibility.MaskAsync(tenant, AgentIdentity.ActingFor(Colleague), [proposal], ct)).Should().ContainKey(proposal);

        var alone = await SendErrorAs(AgentIdentity, Propose(tenant, ProposalId.New(), study, plan, ["protocol.approve"]), ct);
        alone.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
        alone.Explanation!.Conditions.Should().Contain(c => c.Code == AuthorityEvaluator.RequesterCondition && !c.Met);
        (await SendErrorAs(AgentIdentity.ActingFor(Colleague), Propose(tenant, ProposalId.New(), study, plan, ["protocol.approve"]), ct)).Code
            .Should().Be(KernelErrors.AuthorizationDenied.Code, "the colleague may not propose in this study");

        _fixture.CurrentActor.Actor = Setup;
        _fixture.Authorizer.Inner = null;
        var grant = await _fixture.Sender.SendAsync(
            new IssueGrant(tenant, GrantId.New(), Agent, "proposal.create", null, AuthorityScope.Study(study), _fixture.Clock.UtcNow, null, false, KernelProtocolScenario.Key(), "Appointed"),
            cancellationToken: ct);
        grant.Error!.Code.Should().Be(AuthorityErrors.GrantTargetInvalid.Code);
    }

    private void Tick() => _fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    /// <summary>
    /// The agent and the person it acts for may propose in the study; the approver may approve proposals, sign and
    /// approve protocol versions there. Production authorization from then on.
    /// </summary>
    private async Task SetUpAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        await GrantAsync(tenant, AgentIdentity, "proposal.create", AuthorityScope.Study(study), ct);
        await GrantAsync(tenant, Requester, "proposal.create", AuthorityScope.Study(study), ct);
        await GrantAsync(tenant, Approver, "proposal.approve", AuthorityScope.Study(study), ct);
        await GrantAsync(tenant, Approver, "protocol.approve", AuthorityScope.Study(study), ct);
        await GrantAsync(tenant, Approver, "attestation.sign", AuthorityScope.Tenant(), ct);
        UseProductionAuthorization();
    }

    /// <summary>The agent proposes; the approver signs and approves the proposal. Returns the approval attestation.</summary>
    private async Task<AttestationId> ApprovedProposalAsync(TenantId tenant, StudyId study, ProposalId id, IReadOnlyList<string> capabilities, CancellationToken ct)
    {
        var plan = await RegisterPlanAsync(tenant, study, ct);
        var proposed = await As(Agent, Propose(tenant, id, study, plan, capabilities), ct);
        Tick();
        var attestation = await As(Approver, _scenario.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.Proposal, id.Value, proposed.ContentHash)), ct);
        await As(Approver, new ApproveProposal(tenant, id, attestation, KernelProtocolScenario.Key(), "Plan reviewed"), ct);
        Tick();
        return attestation;
    }

    /// <summary>A protocol version of <paramref name="study"/> in review, with the approver's Approved attestation over it.</summary>
    private async Task<(ProtocolVersionId Version, AttestationId Signed)> InReviewSignedAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var previous = (_fixture.CurrentActor.Actor, _fixture.Authorizer.Inner);
        _fixture.CurrentActor.Actor = Setup;
        _fixture.Authorizer.Inner = null;
        var version = await _scenario.InReviewAsync(tenant, study, ct);
        (_fixture.CurrentActor.Actor, _fixture.Authorizer.Inner) = previous;
        var signed = await As(Approver, _scenario.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(version)), ct);
        return (version.ProtocolVersionId, signed);
    }

    /// <summary>Approves the protocol version as <paramref name="actor"/> under the proposal; returns the refusal.</summary>
    private async Task<KernelError> ExecuteAsync(
        TenantId tenant, (ProtocolVersionId Version, AttestationId Signed) target, Actor actor, ProposalId proposal, CancellationToken ct)
    {
        _fixture.CurrentActor.Actor = actor;
        var result = await _fixture.Sender.SendAsync(
            new ApproveProtocolVersion(tenant, target.Version, target.Signed, KernelProtocolScenario.Key(), Reason),
            new CommandMetadata(Guid.CreateVersion7(), Proposal: proposal),
            ct);
        result.IsSuccess.Should().BeFalse();
        return result.Error!;
    }

    private Propose Propose(TenantId tenant, ProposalId id, StudyId study, ArtifactVersionId plan, IReadOnlyList<string> capabilities) =>
        new(
            tenant,
            id,
            study,
            "Approve the reviewed protocol version",
            plan,
            [],
            SimulatedImpact: null,
            capabilities,
            _fixture.Clock.UtcNow.AddDays(7),
            Model,
            KernelProtocolScenario.Key(),
            "Review complete");

    private async Task<ArtifactVersionId> RegisterPlanAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var previous = (_fixture.CurrentActor.Actor, _fixture.Authorizer.Inner);
        _fixture.CurrentActor.Actor = Setup;
        _fixture.Authorizer.Inner = null;
        var bytes = $"plan-{Guid.NewGuid():N}";
        var version = ArtifactVersionId.New();
        var content = new ArtifactContent(ContentHash.OfBytes(Encoding.UTF8.GetBytes(bytes)), bytes.Length, "application/json", $"blob://plans/{bytes}");
        await _scenario.OkAsync(
            new RegisterArtifact(tenant, ArtifactId.New(), version, ArtifactKind.Other, study, "Proposed plan and reasoning", content, KernelProtocolScenario.Key(), "Plan filed"),
            ct);
        (_fixture.CurrentActor.Actor, _fixture.Authorizer.Inner) = previous;
        return version;
    }

    private async Task GrantAsync(TenantId tenant, Actor grantee, string capability, AuthorityScope scope, CancellationToken ct)
    {
        var previous = (_fixture.CurrentActor.Actor, _fixture.Authorizer.Inner);
        _fixture.CurrentActor.Actor = Setup;
        _fixture.Authorizer.Inner = null;
        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), grantee, capability, null, scope, _fixture.Clock.UtcNow, null, false, KernelProtocolScenario.Key(), "Appointed"),
            ct);
        (_fixture.CurrentActor.Actor, _fixture.Authorizer.Inner) = previous;
        Tick();
    }

    private async Task<IReadOnlyList<GrantId>> GrantsOfAsync(TenantId tenant, Actor grantee, string capability, StudyId study, CancellationToken ct)
    {
        await using var db = _fixture.CreateContext(tenant);
        return await db.Grants.AsNoTracking()
            .Where(g => g.GranteeId == grantee.Id && g.Capability == capability && g.ScopeTargetId == study.Value)
            .Select(g => g.Id)
            .ToListAsync(ct);
    }

    private void UseProductionAuthorization()
    {
        var evaluator = new AuthorityEvaluator(_fixture.AuthorityStore, new KernelAuthorityOptions(new HashSet<string>()));
        _fixture.Authorizer.Inner = new KernelAuthorization(
            evaluator, _fixture.CurrentActor, KernelCommandCatalog.Kernel, _fixture.Clock, NullLogger<KernelAuthorization>.Instance);
    }

    private async Task<TResult> As<TResult>(Actor actor, ICommand<TResult> command, CancellationToken ct, ProposalId? proposal = null)
    {
        _fixture.CurrentActor.Actor = actor;
        var result = await _fixture.Sender.SendAsync(command, new CommandMetadata(Guid.CreateVersion7(), Proposal: proposal), ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }

    private async Task<KernelError> SendErrorAs(Actor actor, ICommand<ProposalState> command, CancellationToken ct)
    {
        _fixture.CurrentActor.Actor = actor;
        var error = (await _fixture.Sender.SendAsync(command, cancellationToken: ct)).Error;
        error.Should().NotBeNull($"{command.GetType().Name} must be refused");
        return error!;
    }
}
