using System;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Validation;

namespace Cadence.Kernel.Domain.Validation;

/// <summary>
/// The decisions people have made about one finding (spec §10.3): investigating it, classifying it, accepting or
/// explaining it with an attestation (K-FIND-2), or reopening it. Detection is not stored here: runs detect and
/// resolve findings, and <see cref="FindingLedger"/> combines the runs with this record into the finding's status.
/// The aggregate is created by the first decision, with the finding's derived identifier.
/// </summary>
public sealed class ValidationFinding : AggregateRoot<ValidationFindingId>
{
    private ValidationFinding() { }

    private ValidationFinding(ValidationFindingId id, TenantId tenantId, StudyId studyId)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = studyId;
    }

    public TenantId TenantId { get; private set; }

    public StudyId StudyId { get; private set; }

    /// <summary>
    /// The latest decision on the finding's status: Investigating, Accepted, Explained, or <see cref="FindingStatus.Open"/>
    /// after a reopen. Null while the finding has only been classified.
    /// </summary>
    public FindingStatus? Decision { get; private set; }

    /// <summary>When <see cref="Decision"/> was made. A run that resolves the finding afterwards ends it.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Who made <see cref="Decision"/>.</summary>
    public string? DecidedBy { get; private set; }

    /// <summary>The acceptance rationale or explanation, for Accepted and Explained.</summary>
    public string? DecisionText { get; private set; }

    /// <summary>The run the Accepted or Explained decision was made against.</summary>
    public ValidationRunId? DecidedOnRun { get; private set; }

    /// <summary>The attestation that authorizes an Accepted or Explained decision.</summary>
    public AttestationId? AttestationId { get; private set; }

    /// <summary>What kind of problem the finding is, once classified. Kept when the finding recurs.</summary>
    public FindingClassification? Classification { get; private set; }

    /// <summary>Starts the record of decisions about a detected finding.</summary>
    public static ValidationFinding Start(ValidationFindingId id, TenantId tenantId, StudyId studyId) => new(id, tenantId, studyId);

    /// <summary>Marks the finding as being investigated. <paramref name="current"/> is its derived status.</summary>
    public KernelError? Investigate(FindingStatus current, string actor, DateTimeOffset at)
    {
        if (current != FindingStatus.Open)
        {
            return ValidationErrors.InvalidTransition.WithMessage($"Only an open finding can be investigated; this one is {current}.");
        }

        Decide(FindingStatus.Investigating, actor, at, text: null, run: null, attestation: null);
        return null;
    }

    /// <summary>Classifies the finding. Allowed whatever its status except Resolved.</summary>
    public KernelError? Classify(FindingStatus current, FindingClassification classification)
    {
        if (!Enum.IsDefined(classification))
        {
            return ValidationErrors.InvalidTransition.WithMessage($"Unknown classification {(int)classification}.");
        }

        if (current == FindingStatus.Resolved)
        {
            return ValidationErrors.InvalidTransition.WithMessage("A resolved finding cannot be classified.");
        }

        Classification = classification;
        return null;
    }

    /// <summary>
    /// Accepts or explains the finding (K-FIND-2). The caller has verified the attestation over
    /// <see cref="FindingDecisionContent.Hash"/> of this decision against <paramref name="run"/>.
    /// </summary>
    public KernelError? Conclude(
        FindingStatus current, FindingStatus decision, string text, ValidationRunId run, AttestationId attestation, string actor, DateTimeOffset at)
    {
        if (decision is not (FindingStatus.Accepted or FindingStatus.Explained))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "A conclusion is Accepted or Explained.");
        }

        if (current is not (FindingStatus.Open or FindingStatus.Investigating))
        {
            return ValidationErrors.InvalidTransition.WithMessage($"Only an open or investigating finding can be {decision.ToString().ToLowerInvariant()}; this one is {current}.");
        }

        if (!FindingText.IsValid(text))
        {
            return ValidationErrors.DecisionTextInvalid;
        }

        Decide(decision, actor, at, text, run, attestation);
        return null;
    }

    /// <summary>Withdraws an investigation, acceptance or explanation.</summary>
    public KernelError? Reopen(FindingStatus current, string actor, DateTimeOffset at)
    {
        if (current is not (FindingStatus.Investigating or FindingStatus.Accepted or FindingStatus.Explained))
        {
            return ValidationErrors.InvalidTransition.WithMessage($"Only an investigating, accepted or explained finding can be reopened; this one is {current}.");
        }

        Decide(FindingStatus.Open, actor, at, text: null, run: null, attestation: null);
        return null;
    }

    private void Decide(FindingStatus decision, string actor, DateTimeOffset at, string? text, ValidationRunId? run, AttestationId? attestation)
    {
        Decision = decision;
        DecidedBy = actor;
        DecidedAt = at.ToUniversalTime();
        DecisionText = text;
        DecidedOnRun = run;
        AttestationId = attestation;
    }
}
