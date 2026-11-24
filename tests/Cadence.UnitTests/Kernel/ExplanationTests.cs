using System;
using System.Linq;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The explanation model (ADR-2482, spec §5.5): authorization decisions carry every evaluated condition with its
/// evidence, explain themselves as a checklist, and the explanation follows the evidence.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ExplanationTests
{
    private static readonly NodeRef Participant = NodeRef.For(ParticipantId.New());
    private static readonly NodeRef Delegation = NodeRef.For(AttestationId.New(), 1);
    private static readonly NodeRef Training = NodeRef.For(AttestationId.New(), 2);

    /// <summary>
    /// A denial explains itself with every condition the evaluator checked, met and unmet, keeps each condition's
    /// evidence, and turns each unmet condition into a next action.
    /// </summary>
    [Fact(DisplayName = "Denial_ExplainsMetAndUnmetConditions")]
    [KernelRule("K-AUTH-6")]
    [KernelRule("K-EXP-1")]
    public void DenialExplainsMetAndUnmetConditions()
    {
        var decision = AuthorizationDecision.Deny(
            "Protocol v3 training is not current.",
            EvaluatedCondition.Satisfied("delegation.active", "Delegated eligibility signing at this study site", Delegation),
            EvaluatedCondition.Unsatisfied("qualification.current", "Complete protocol v3 training", Training));

        var explanation = decision.Explain("Sign eligibility", Participant);

        explanation.Subject.Should().Be(Participant);
        explanation.Claim.Should().Be("Sign eligibility is not permitted.");
        explanation.Producer.Should().Be(AuthorizationDecision.ExplanationProducer);
        explanation.Conditions.Select(c => (c.Code, c.Met)).Should().Equal(("delegation.active", true), ("qualification.current", false));
        explanation.UnmetConditions.Should().ContainSingle().Which.Evidence.Should().Equal(Training);
        explanation.Steps.Should().ContainSingle().Which.Should().Be(
            new ExplanationStep("Protocol v3 training is not current.", [Delegation, Training]));
        explanation.NextActions.Should().Equal(
            new NextAction("Complete protocol v3 training", Capability: "qualification.record", Target: Training));
    }

    /// <summary>
    /// Unmet grant, qualification, and readiness conditions map their remedial capability and first evidence NodeRef
    /// (falling back to Subject) onto each NextAction so UI surfaces can render clickable resolution targets.
    /// </summary>
    [Fact(DisplayName = "Explain_PopulatesNextActionTargetAndCapabilityFromUnmetConditionEvidence")]
    [KernelRule("K-AUTH-6")]
    [KernelRule("K-EXP-1")]
    public void ExplainPopulatesNextActionTargetAndCapabilityFromUnmetConditionEvidence()
    {
        var requirement = NodeRef.For(QualificationRequirementId.New());
        var decision = AuthorizationDecision.Deny(
            "Missing grant and qualification.",
            EvaluatedCondition.Unsatisfied("authority.grant", "Hold observation.record for this study site"),
            EvaluatedCondition.Unsatisfied("authority.qualification", "Obtain gcp", requirement));

        var explanation = decision.Explain("Record observation", Participant);

        explanation.NextActions.Should().Equal(
            new NextAction("Hold observation.record for this study site", Capability: "grant.issue", Target: Participant),
            new NextAction("Obtain gcp", Capability: "qualification.record", Target: requirement));
    }

    /// <summary>
    /// K-EXP-2: restricted or blinded NodeRefs in an explanation are replaced in place with masked:&lt;kind&gt;:&lt;Reason&gt;
    /// without dropping conditions, steps, or evidence entries and without leaking hidden UUIDs.
    /// </summary>
    [Fact(DisplayName = "ExplanationMasking_ReplacesHiddenNodeRefsInPlaceWithoutDroppingItems")]
    [KernelRule("K-EXP-2")]
    public void ExplanationMaskingReplacesHiddenNodeRefsInPlaceWithoutDroppingItems()
    {
        var hiddenSite = NodeRef.For(StudySiteId.New());
        var blindedAllocation = NodeRef.For(AssertionId.New());
        var explanation = new Explanation(
            Claim: $"Action on {hiddenSite} is blocked by {blindedAllocation}.",
            Producer: "test.rule",
            Subject: hiddenSite,
            Steps: [new ExplanationStep($"Blocked at {hiddenSite}.", [hiddenSite, Training, blindedAllocation])],
            Conditions:
            [
                EvaluatedCondition.Satisfied("delegation.active", "Delegated", Delegation),
                EvaluatedCondition.Unsatisfied("lock.unlocked", $"Release {blindedAllocation}", blindedAllocation),
            ],
            NextActions:
            [
                new NextAction($"Release lock {blindedAllocation}", Capability: "lock.release", Target: blindedAllocation),
            ]);

        var masks = new System.Collections.Generic.Dictionary<NodeRef, MaskReason>
        {
            [hiddenSite] = MaskReason.NotPermitted,
            [blindedAllocation] = MaskReason.Blinded,
        };

        var wire = ProblemExplanation.From(explanation, masks);
        var json = System.Text.Json.JsonSerializer.Serialize(wire);

        json.Should().NotContain(hiddenSite.Id.ToString("D"));
        json.Should().NotContain(blindedAllocation.Id.ToString("D"));
        wire.Subject.Should().Be($"masked:{NodeKinds.StudySite}:{MaskReason.NotPermitted}");
        wire.Steps.Should().ContainSingle().Which.Evidence.Should().Equal(
            $"masked:{NodeKinds.StudySite}:{MaskReason.NotPermitted}",
            Training.ToString(),
            $"masked:{NodeKinds.Assertion}:{MaskReason.Blinded}");
        wire.Conditions.Should().HaveCount(2);
        wire.Conditions[1].Evidence.Should().Equal($"masked:{NodeKinds.Assertion}:{MaskReason.Blinded}");
        wire.NextActions.Should().ContainSingle().Which.Target.Should().Be($"masked:{NodeKinds.Assertion}:{MaskReason.Blinded}");
    }

    /// <summary>A permit explains itself too, with no unmet conditions and nothing to do.</summary>
    [Fact(DisplayName = "Permit_ExplainsWithoutNextActions")]
    public void PermitExplainsWithoutNextActions()
    {
        var explanation = AuthorizationDecision
            .Permit("Delegated and trained.", EvaluatedCondition.Satisfied("delegation.active", "Delegated", Delegation))
            .Explain("Sign eligibility");

        explanation.Claim.Should().Be("Sign eligibility is permitted.");
        explanation.Subject.Should().BeNull();
        explanation.UnmetConditions.Should().BeEmpty();
        explanation.NextActions.Should().BeEmpty();
    }

    /// <summary>
    /// K-EXP-3: the explanation is a function of the evidence. The same evidence gives an equal explanation; when the
    /// training record changes (expired, then renewed as a new version), the unmet conditions and the evidence change
    /// with it.
    /// </summary>
    [Fact(DisplayName = "Explanation_FollowsTheEvidence")]
    [KernelRule("K-EXP-3")]
    public void ExplanationFollowsTheEvidence()
    {
        var expired = new TrainingRecord(Training, ValidUntil: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var renewed = new TrainingRecord(Training.AtVersion(3), ValidUntil: new DateTimeOffset(2027, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var at = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        var before = EligibilitySigning(expired, at).Explain("Sign eligibility", Participant);
        var again = EligibilitySigning(expired, at).Explain("Sign eligibility", Participant);
        var after = EligibilitySigning(renewed, at).Explain("Sign eligibility", Participant);

        again.Should().Be(before);
        before.UnmetConditions.Select(c => c.Code).Should().Equal("qualification.current");
        after.UnmetConditions.Should().BeEmpty();
        after.Should().NotBe(before);
        after.Conditions.Single(c => c.Code == "qualification.current").Evidence.Should().Equal(Training.AtVersion(3));
    }

    /// <summary>Explaining needs an action name; an empty one is a programming error.</summary>
    [Fact(DisplayName = "Explain_RequiresAnAction")]
    public void ExplainRequiresAnAction() =>
        FluentActions.Invoking(() => AuthorizationDecision.Permit().Explain(" ")).Should().Throw<ArgumentException>();

    /// <summary>A stand-in for the rule owner (#4781 will own it): delegation plus current training.</summary>
    private static AuthorizationDecision EligibilitySigning(TrainingRecord training, DateTimeOffset at)
    {
        var delegated = EvaluatedCondition.Satisfied("delegation.active", "Delegated eligibility signing", Delegation);
        return training.ValidUntil > at
            ? AuthorizationDecision.Permit("Delegated and trained.", delegated,
                EvaluatedCondition.Satisfied("qualification.current", "Protocol v3 training is current", training.Record))
            : AuthorizationDecision.Deny("Protocol v3 training is not current.", delegated,
                EvaluatedCondition.Unsatisfied("qualification.current", "Complete protocol v3 training", training.Record));
    }

    private sealed record TrainingRecord(NodeRef Record, DateTimeOffset ValidUntil);
}
