using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Intelligence;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Proposals in the domain (ADR-2489): validation of every field, the content hash an approval signs, the lifecycle
/// with derived expiry, and the model rule for automation agents. <c>KernelProposalPostgresTests</c> covers the
/// handlers, authorization, attestations and lineage.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ProposalTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private static readonly Actor Agent = Actor.AutomationAgent("deviation-triage");

    private static readonly Actor Proposer = Actor.Person("proposer@sponsor.test");

    private static readonly Actor Approver = Actor.Person("approver@sponsor.test");

    private static readonly string ImpactHash = ContentHash.Compute("""{"result":1}""");

    private readonly FakeClock _clock = new();

    [Fact]
    public void AutomationAgent_BuildsAnAgentPrefixedActor()
    {
        var agent = Actor.AutomationAgent("deviation-triage");

        agent.Kind.Should().Be(ActorKind.AutomationAgent);
        agent.Id.Should().Be("agent:deviation-triage");
        agent.Should().Be(new Actor(ActorKind.AutomationAgent, "agent:deviation-triage"));

        FluentActions.Invoking(() => Actor.AutomationAgent("")).Should().Throw<ArgumentException>("a bare agent: prefix names no one");
        FluentActions.Invoking(() => Actor.AutomationAgent(null!)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Actor.Person("agent:impostor")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Commands_DeclareTheirCapabilities()
    {
        var catalog = KernelCommandCatalog.Kernel;

        catalog.Find(typeof(Propose))!.Capability.Should().Be(new Capability("proposal.create", VerbClass.Propose));
        catalog.Find(typeof(Propose))!.ResourceKind.Should().Be(NodeKinds.Study);
        catalog.Find(typeof(ApproveProposal))!.Capability.Should().Be(new Capability("proposal.approve", VerbClass.Approve));
        catalog.Find(typeof(RejectProposal))!.Capability.Should().Be(new Capability("proposal.reject", VerbClass.Approve));
        catalog.Find(typeof(WithdrawProposal))!.Capability.Should().Be(new Capability("proposal.withdraw", VerbClass.Propose));
        catalog.Find(typeof(EscalateProposal))!.Capability.Should().Be(new Capability("proposal.escalate", VerbClass.Approve));
        foreach (var command in new[] { typeof(ApproveProposal), typeof(RejectProposal), typeof(WithdrawProposal), typeof(EscalateProposal) })
        {
            catalog.Find(command)!.ResourceKind.Should().Be(NodeKinds.Proposal);
        }

        NodeKinds.Of<ProposalId>().Should().Be("proposal");
    }

    [Fact]
    public void Capabilities_AreOnlyKernelExecuteOrApproveCommands()
    {
        ProposalCapabilities.IsExecutable("protocol.approve").Should().BeTrue();
        ProposalCapabilities.IsExecutable("effectivity.declare").Should().BeTrue("declaring an effectivity is an Execute command");
        ProposalCapabilities.IsExecutable("protocol.draft").Should().BeFalse("drafting is a Propose command");
        ProposalCapabilities.IsExecutable("proposal.create").Should().BeFalse();
        ProposalCapabilities.IsExecutable("made.up").Should().BeFalse();

        var executable = KernelCommandCatalog.Kernel.Entries
            .Where(e => e.Capability.VerbClass is VerbClass.Execute or VerbClass.Approve)
            .Select(e => e.Capability.Token)
            .ToList();
        executable.Should().NotBeEmpty().And.OnlyContain(t => ProposalCapabilities.IsExecutable(t));
    }

    [Fact]
    public void Create_AcceptsAValidProposalFromAPersonOrAnAgent()
    {
        var byPerson = Create(Command(), Proposer);
        var byAgent = Create(Command() with { ModelId = "claude-opus-5" }, Agent);

        byPerson.Status.Should().Be(ProposalStatus.Open);
        byPerson.ModelId.Should().BeNull();
        byPerson.ProposedBy.Should().Be(Proposer);
        byPerson.ProposedAt.Should().Be(_clock.UtcNow);
        byPerson.ContentHash.Should().Match(h => ContentHash.IsWellFormed(h));
        byPerson.SignableTarget.Should().Be(new AttestationTarget(NodeKinds.Proposal, byPerson.Id.Value, byPerson.ContentHash));
        byAgent.ProposedBy.Should().Be(Agent);
        byAgent.ModelId.Should().Be("claude-opus-5");
    }

    [Theory]
    [InlineData(ActorKind.AutomationAgent, null, "K.Proposal.ModelRequired")]
    [InlineData(ActorKind.AutomationAgent, "", "K.Proposal.ModelRequired")]
    [InlineData(ActorKind.AutomationAgent, "   ", "K.Proposal.ModelRequired")]
    [InlineData(ActorKind.Person, "claude-opus-5", "K.Proposal.ModelNotAllowed")]
    [InlineData(ActorKind.ServiceAccount, "claude-opus-5", "K.Proposal.ModelNotAllowed")]
    public void Create_AnAgentNamesItsModelAndNobodyElseDoes(ActorKind kind, string? modelId, string code)
    {
        var actor = kind switch
        {
            ActorKind.AutomationAgent => Agent,
            ActorKind.ServiceAccount => Actor.ServiceAccount("scheduler"),
            _ => Proposer,
        };

        Error(Command() with { ModelId = modelId }, actor).Code.Should().Be(code);
    }

    [Fact]
    public void Create_RefusesAModelIdLongerThanTheLimit()
    {
        Error(Command() with { ModelId = new string('m', Propose.MaxModelIdLength + 1) }, Agent).Code.Should().Be(ProposalErrors.ModelRequired.Code);
        Create(Command() with { ModelId = new string('m', Propose.MaxModelIdLength) }, Agent).ModelId.Should().HaveLength(Propose.MaxModelIdLength);
    }

    [Fact]
    public void Create_ChecksTheIntent()
    {
        foreach (var intent in new[] { null!, "", "   ", new string('x', Propose.MaxIntentLength + 1) })
        {
            Error(Command() with { Intent = intent }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code, $"intent of length {intent?.Length}");
        }

        Create(Command() with { Intent = "x" }, Proposer).Intent.Should().Be("x");
        Create(Command() with { Intent = new string('x', Propose.MaxIntentLength) }, Proposer).Intent.Should().HaveLength(Propose.MaxIntentLength);
    }

    [Fact]
    public void Create_ChecksTheStudyAndPlanAreNamed()
    {
        Error(Command() with { StudyId = StudyId.Empty }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);
        Error(Command() with { PlanArtifactVersionId = ArtifactVersionId.Empty }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);
    }

    [Fact]
    public void Create_ChecksTheAffectedObjects()
    {
        var node = NodeRef.For(EnrollmentId.New(), 2);
        var tooMany = Enumerable.Range(0, Propose.MaxAffected + 1).Select(_ => NodeRef.For(EnrollmentId.New())).ToList();

        Error(Command() with { Affected = null! }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);
        Error(Command() with { Affected = tooMany }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);
        Error(Command() with { Affected = [node, node] }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);
        Error(Command() with { Affected = [node, null!] }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);

        Create(Command() with { Affected = [] }, Proposer).Affected.Should().BeEmpty();
        Create(Command() with { Affected = tooMany.Take(Propose.MaxAffected).ToList() }, Proposer).Affected.Should().HaveCount(Propose.MaxAffected);
        var unversionedToo = Create(Command() with { Affected = [node, node.Unversioned()] }, Proposer);
        unversionedToo.AffectedRefs().Should().Equal(node, node.Unversioned());
    }

    [Fact]
    public void Create_ChecksTheSimulatedImpact()
    {
        foreach (var bad in new[] { "", "abc", ImpactHash.ToUpperInvariant(), ImpactHash[ContentHash.Prefix.Length..], ImpactHash + "0" })
        {
            Error(Command() with { SimulatedImpact = bad }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code, bad);
        }

        Create(Command() with { SimulatedImpact = null }, Proposer).SimulatedImpact.Should().BeNull();
        Create(Command() with { SimulatedImpact = ImpactHash }, Proposer).SimulatedImpact.Should().Be(ImpactHash);
    }

    [Fact]
    public void Create_ChecksTheRequestedCapabilities()
    {
        var executable = KernelCommandCatalog.Kernel.Entries
            .Where(e => e.Capability.VerbClass is VerbClass.Execute or VerbClass.Approve)
            .Select(e => e.Capability.Token)
            .Distinct()
            .ToList();
        executable.Should().HaveCountGreaterThan(Propose.MaxRequestedCapabilities);

        Error(Command() with { RequestedCapabilities = null! }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);
        Error(Command() with { RequestedCapabilities = [] }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);
        Error(Command() with { RequestedCapabilities = executable.Take(Propose.MaxRequestedCapabilities + 1).ToList() }, Proposer).Code
            .Should().Be(ProposalErrors.Invalid.Code);
        Error(Command() with { RequestedCapabilities = ["protocol.approve", "protocol.approve"] }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);
        foreach (var unknown in new[] { "protocol.draft", "made.up", "Protocol.Approve", "", null! })
        {
            Error(Command() with { RequestedCapabilities = [unknown] }, Proposer).Code.Should().Be(ProposalErrors.CapabilityUnknown.Code, unknown);
        }

        Create(Command() with { RequestedCapabilities = executable.Take(Propose.MaxRequestedCapabilities).ToList() }, Proposer)
            .RequestedCapabilities.Should().HaveCount(Propose.MaxRequestedCapabilities);
    }

    [Fact]
    public void Create_ChecksTheExpiryWindow()
    {
        var now = _clock.UtcNow;

        Error(Command() with { ExpiresAt = now }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);
        Error(Command() with { ExpiresAt = now.AddSeconds(-1) }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);
        Error(Command() with { ExpiresAt = now + Propose.MaxLifetime + TimeSpan.FromSeconds(1) }, Proposer).Code.Should().Be(ProposalErrors.Invalid.Code);

        Create(Command() with { ExpiresAt = now.AddSeconds(1) }, Proposer).ExpiresAt.Should().Be(now.AddSeconds(1));
        Create(Command() with { ExpiresAt = now + Propose.MaxLifetime }, Proposer).ExpiresAt.Should().Be(now + Propose.MaxLifetime);
    }

    [Fact]
    public void ContentHash_IsStableForTheSameContent()
    {
        var command = Command() with { SimulatedImpact = ImpactHash, ModelId = "claude-opus-5" };

        var first = Create(command, Agent);
        var second = Create(command, Agent);

        second.ContentHash.Should().Be(first.ContentHash);
        Proposal.HashOf(first).Should().Be(first.ContentHash);
    }

    [Fact]
    public void ContentHash_ChangesWithEveryContentField()
    {
        var command = Command() with { SimulatedImpact = ImpactHash };
        var baseline = Create(command, Proposer).ContentHash;

        var variants = new Dictionary<string, Proposal>
        {
            ["id"] = Create(command with { ProposalId = ProposalId.New() }, Proposer),
            ["study"] = Create(command with { StudyId = StudyId.New() }, Proposer),
            ["intent"] = Create(command with { Intent = command.Intent + "." }, Proposer),
            ["plan"] = Create(command with { PlanArtifactVersionId = ArtifactVersionId.New() }, Proposer),
            ["affected"] = Create(command with { Affected = [.. command.Affected, NodeRef.For(EnrollmentId.New(), 1)] }, Proposer),
            ["affected order"] = Create(command with { Affected = [.. command.Affected.Reverse()] }, Proposer),
            ["simulated impact"] = Create(command with { SimulatedImpact = null }, Proposer),
            ["capabilities"] = Create(command with { RequestedCapabilities = ["protocol.approve"] }, Proposer),
            ["expiry"] = Create(command with { ExpiresAt = command.ExpiresAt.AddSeconds(1) }, Proposer),
            ["proposer"] = Create(command, Approver),
            ["proposer kind and model"] = Create(command with { ModelId = "claude-opus-5" }, Actor.AutomationAgent("proposer@sponsor.test")),
        };
        _clock.Advance(TimeSpan.FromSeconds(1));
        variants["proposed at"] = Create(command, Proposer);

        variants.Should().AllSatisfy(v => v.Value.ContentHash.Should().NotBe(baseline, v.Key));
        variants.Values.Select(v => v.ContentHash).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ContentHash_ChangesWithTheModel()
    {
        var command = Command() with { ModelId = "claude-opus-5" };

        Create(command with { ModelId = "claude-sonnet-5" }, Agent).ContentHash.Should().NotBe(Create(command, Agent).ContentHash);
    }

    [Fact]
    public void Approve_RecordsTheApproverAndAttestation()
    {
        var proposal = Create(Command(), Agent, "claude-opus-5");
        var attestation = AttestationId.New();
        var now = _clock.Advance(TimeSpan.FromHours(1));

        proposal.Approve(Approver, attestation, now).Should().BeNull();

        proposal.Status.Should().Be(ProposalStatus.Approved);
        proposal.StatusAt(now.AddYears(1)).Should().Be(ProposalStatus.Approved, "a decided proposal never expires");
        proposal.DecidedAt.Should().Be(now);
        proposal.DecidedById.Should().Be(Approver.Id);
        proposal.DecidedByKind.Should().Be(ActorKind.Person);
        proposal.ApprovalAttestationId.Should().Be(attestation);
        proposal.DecisionReason.Should().BeNull();
        proposal.ToState(now).Should().BeEquivalentTo(new ProposalState(
            proposal.Id, proposal.StudyId, ProposalStatus.Approved, proposal.ContentHash, Agent.Id, ActorKind.AutomationAgent, "claude-opus-5",
            proposal.ExpiresAt, now, Approver.Id, attestation, ConfidenceScore: null, ConfidenceTier: null, IsEscalated: false));

        proposal.Approve(Approver, AttestationId.New(), now).Should().Be(ProposalErrors.NotOpen with { Message = "The proposal is already Approved." });
        proposal.Reject(Approver, "No", now)!.Code.Should().Be(ProposalErrors.NotOpen.Code);
        proposal.Withdraw(Agent, "No", now)!.Code.Should().Be(ProposalErrors.NotOpen.Code);
    }

    [Fact]
    public void Approve_RefusesTheProposer()
    {
        var proposal = Create(Command(), Proposer);

        proposal.CheckApproval(Proposer, _clock.UtcNow).Should().Be(ProposalErrors.SelfApproval);
        proposal.Approve(Proposer, AttestationId.New(), _clock.UtcNow).Should().Be(ProposalErrors.SelfApproval);
        proposal.Status.Should().Be(ProposalStatus.Open);
        proposal.CheckApproval(Approver, _clock.UtcNow).Should().BeNull();
    }

    [Fact]
    public void Reject_RecordsTheReason()
    {
        var proposal = Create(Command(), Proposer);
        var now = _clock.Advance(TimeSpan.FromMinutes(5));

        proposal.Reject(Approver, "Out of scope", now).Should().BeNull();

        proposal.Status.Should().Be(ProposalStatus.Rejected);
        proposal.DecidedById.Should().Be(Approver.Id);
        proposal.DecisionReason.Should().Be("Out of scope");
        proposal.ApprovalAttestationId.Should().BeNull();
    }

    [Fact]
    public void Withdraw_IsForTheProposerOnly()
    {
        var proposal = Create(Command() with { ModelId = "claude-opus-5" }, Agent);
        var now = _clock.Advance(TimeSpan.FromMinutes(5));

        proposal.Withdraw(Approver, "Not mine", now).Should().Be(ProposalErrors.NotProposer);
        proposal.Withdraw(Actor.Person("deviation-triage"), "Same name, not an agent", now).Should().Be(ProposalErrors.NotProposer);
        proposal.Status.Should().Be(ProposalStatus.Open);

        proposal.Withdraw(Agent, "Superseded by a better plan", now).Should().BeNull();
        proposal.Status.Should().Be(ProposalStatus.Withdrawn);
        proposal.DecidedById.Should().Be(Agent.Id);
        proposal.DecidedByKind.Should().Be(ActorKind.AutomationAgent);
        proposal.DecisionReason.Should().Be("Superseded by a better plan");
    }

    [Fact]
    public void Expiry_IsDerivedOnRead_AndStopsEveryDecision()
    {
        var proposal = Create(Command(), Proposer);
        var expiresAt = proposal.ExpiresAt;

        proposal.StatusAt(expiresAt.AddTicks(-10)).Should().Be(ProposalStatus.Open);
        proposal.StatusAt(expiresAt).Should().Be(ProposalStatus.Expired);
        proposal.StatusAt(expiresAt.AddDays(1)).Should().Be(ProposalStatus.Expired);
        proposal.Status.Should().Be(ProposalStatus.Open, "expiry is never stored");
        proposal.ToState(expiresAt).Status.Should().Be(ProposalStatus.Expired);

        proposal.CheckOpen(expiresAt.AddTicks(-10)).Should().BeNull();
        proposal.Approve(Approver, AttestationId.New(), expiresAt).Should().Be(ProposalErrors.Expired);
        proposal.Reject(Approver, "Too late", expiresAt).Should().Be(ProposalErrors.Expired);
        proposal.Withdraw(Proposer, "Too late", expiresAt).Should().Be(ProposalErrors.Expired);
        proposal.Status.Should().Be(ProposalStatus.Open);
        proposal.DecidedAt.Should().BeNull();
    }

    [Fact]
    public void ConfidenceScore_ValidatesAndCalculatesTier()
    {
        var high = Create(Command() with { ConfidenceScore = 0.92, ModelId = "claude-opus-5" }, Agent);
        high.ConfidenceScore.Should().Be(0.92);
        high.ConfidenceTier.Should().Be("High");

        var medium = Create(Command() with { ConfidenceScore = 0.78, ModelId = "claude-opus-5" }, Agent);
        medium.ConfidenceScore.Should().Be(0.78);
        medium.ConfidenceTier.Should().Be("Medium");

        var low = Create(Command() with { ConfidenceScore = 0.55, ModelId = "claude-opus-5" }, Agent);
        low.ConfidenceScore.Should().Be(0.55);
        low.ConfidenceTier.Should().Be("Low");

        Error(Command() with { ConfidenceScore = -0.1 }, Proposer).Code.Should().Be(ProposalErrors.InvalidConfidenceScore.Code);
        Error(Command() with { ConfidenceScore = 1.05 }, Proposer).Code.Should().Be(ProposalErrors.InvalidConfidenceScore.Code);
    }

    [Fact]
    public void AutoEscalation_TriggersOnLowConfidenceOrPolicy()
    {
        var highProp = Create(Command() with { ConfidenceScore = 0.88, ModelId = "claude-opus-5" }, Agent);
        highProp.IsEscalated.Should().BeFalse();

        var lowProp = Create(Command() with { ConfidenceScore = 0.60, ModelId = "claude-opus-5" }, Agent);
        lowProp.IsEscalated.Should().BeTrue();
        lowProp.EscalatedTo.Should().Be("Manual Review");
        lowProp.EscalationReason.Should().Contain("Confidence score (0.60) is below threshold (0.75)");
        lowProp.EscalationPolicy.Should().Be("LowConfidenceAutoEscalation");

        var forceProp = Create(Command() with { ConfidenceScore = 0.95, EscalationPolicy = "ForceEscalation", ModelId = "claude-opus-5" }, Agent);
        forceProp.IsEscalated.Should().BeTrue();
        forceProp.EscalationReason.Should().Contain("Policy explicitly requested escalation");
    }

    [Fact]
    public void Escalate_UpdatesEscalationStateAndValidatesInputs()
    {
        var proposal = Create(Command(), Proposer);
        var now = _clock.Advance(TimeSpan.FromMinutes(10));

        proposal.Escalate(Approver, "", "Needs review", now).Should().Be(ProposalErrors.EscalationTargetRequired);

        proposal.Escalate(Approver, "Medical Director", "Complex protocol change", now).Should().BeNull();
        proposal.IsEscalated.Should().BeTrue();
        proposal.EscalatedTo.Should().Be("Medical Director");
        proposal.EscalationReason.Should().Be("Complex protocol change");
        proposal.EscalatedById.Should().Be(Approver.Id);
        proposal.EscalatedByKind.Should().Be(ActorKind.Person);
        proposal.EscalatedAt.Should().Be(now);

        proposal.Escalate(Approver, "Safety Board", "Duplicate escalation", now).Should().Be(ProposalErrors.AlreadyEscalated);
    }

    [Fact]
    public void Escalate_MaintainsContentHash_WhenEscalatedWithCustomPolicy()
    {
        var proposal = Create(Command(), Proposer);
        var initialHash = proposal.ContentHash;
        var now = _clock.Advance(TimeSpan.FromMinutes(10));

        var result = proposal.Escalate(Approver, "Medical Director", "Complex protocol change", now, policy: "CustomEscalationPolicy");

        result.Should().BeNull();
        proposal.EscalationPolicy.Should().Be("CustomEscalationPolicy");
        proposal.ContentHash.Should().Be(initialHash);
        Proposal.HashOf(proposal).Should().Be(proposal.ContentHash);
    }

    private Propose Command() => new(
        Tenant,
        ProposalId.FromGuid(Guid.Parse("0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e")),
        StudyId.FromGuid(Guid.Parse("0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6f")),
        "Close the stale deviation queries at site 101",
        ArtifactVersionId.FromGuid(Guid.Parse("0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d70")),
        [NodeRef.For(EnrollmentId.FromGuid(Guid.Parse("0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d71")), 3), NodeRef.For(StudySiteId.FromGuid(Guid.Parse("0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d72")))],
        SimulatedImpact: null,
        ["protocol.approve", "effectivity.change_status"],
        _clock.UtcNow.AddDays(7),
        ModelId: null,
        "key-1",
        "AI triage suggestion");

    private Proposal Create(Propose command, Actor actor, string? modelId = null)
    {
        if (modelId is not null)
        {
            command = command with { ModelId = modelId };
        }

        var result = Proposal.Create(Tenant, command, actor, _clock.UtcNow, ProposalCapabilities.IsExecutable);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }

    private KernelError Error(Propose command, Actor actor)
    {
        var result = Proposal.Create(Tenant, command, actor, _clock.UtcNow, ProposalCapabilities.IsExecutable);
        result.IsSuccess.Should().BeFalse("the command {0} must be refused", command);
        return result.Error!;
    }
}
