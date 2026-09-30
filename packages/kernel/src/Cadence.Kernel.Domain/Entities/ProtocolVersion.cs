using System;
using System.Linq;
using System.Text.Json;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// One version of a study's executable protocol (spec §6.2): a JSON document (CDISC USDM is the interchange format)
/// with a per-study <see cref="Number"/>, its parent, the amendment that introduced it, and a lifecycle
/// <c>Draft → InReview → Approved → Superseded | Withdrawn</c>.
/// <list type="bullet">
/// <item>Submitting for review freezes the content and sets <see cref="ContentHash"/> (<see cref="Trust.ContentHash"/>).
/// From then on the content never changes; returning to draft clears the hash.</item>
/// <item>Approval and withdrawal name an attestation bound to that hash; the command handler verifies it.</item>
/// <item>Forking starts a new draft from an approved version and leaves the parent approved: approved versions
/// coexist until Effectivity (#4777) supersedes one.</item>
/// </list>
/// Invalid transitions return <see cref="ProtocolErrors.InvalidTransition"/>; nothing here throws for an expected
/// failure. The database repeats the content freeze and the transition rules in a trigger.
/// </summary>
public class ProtocolVersion : AggregateRoot<ProtocolVersionId>
{
    protected ProtocolVersion() { }

    private ProtocolVersion(
        ProtocolVersionId id,
        TenantId tenantId,
        StudyId studyId,
        int number,
        ProtocolVersionId? parentVersionId,
        string content,
        ProtocolAmendment? amendment)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = studyId;
        Number = number;
        ParentVersionId = parentVersionId;
        Content = content;
        Amendment = amendment;
        Status = ProtocolVersionStatus.Draft;
    }

    public TenantId TenantId { get; private set; }

    public StudyId StudyId { get; private set; }

    /// <summary>1 for the study's first version, then one more than the highest number in the study.</summary>
    public int Number { get; private set; }

    /// <summary>The approved version this one was forked from; null for the first version.</summary>
    public ProtocolVersionId? ParentVersionId { get; private set; }

    public ProtocolVersionStatus Status { get; private set; }

    /// <summary>The protocol document, a JSON object.</summary>
    public string Content { get; private set; } = "{}";

    /// <summary>Hash of the frozen content; set from submission for review onwards, null for a draft.</summary>
    public string? ContentHash { get; private set; }

    /// <summary>The amendment a forked version introduces; null for the first version.</summary>
    public ProtocolAmendment? Amendment { get; private set; }

    public AttestationId? ApprovalAttestationId { get; private set; }

    public AttestationId? WithdrawalAttestationId { get; private set; }

    public string? WithdrawalReason { get; private set; }

    /// <summary>What an attestation about this version signs; null while there is no frozen content.</summary>
    public AttestationTarget? SignableTarget =>
        ContentHash is null ? null : new AttestationTarget(AttestationTarget.ProtocolVersionKind, Id.Value, ContentHash);

    /// <summary>Creates a study's first version, numbered 1, as a draft.</summary>
    public static CommandResult<ProtocolVersion> CreateFirst(ProtocolVersionId id, TenantId tenantId, StudyId studyId, string content)
    {
        if (CheckContent(content) is { } invalid)
        {
            return invalid;
        }

        return CommandResult<ProtocolVersion>.Success(new ProtocolVersion(id, tenantId, studyId, 1, null, content, null));
    }

    /// <summary>
    /// Starts an amended draft from <paramref name="parent"/>, which must be approved and stays approved. The draft
    /// copies the parent's content.
    /// </summary>
    public static CommandResult<ProtocolVersion> Fork(ProtocolVersion parent, ProtocolVersionId id, int number, ProtocolAmendment amendment)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.Status != ProtocolVersionStatus.Approved)
        {
            return ProtocolErrors.InvalidTransition.WithMessage(
                $"Only an approved protocol version can be forked; version {parent.Number} is {parent.Status}.");
        }

        if (number <= parent.Number)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "A fork is numbered after its parent.");
        }

        if (CheckAmendment(amendment) is { } invalid)
        {
            return invalid;
        }

        return CommandResult<ProtocolVersion>.Success(
            new ProtocolVersion(id, parent.TenantId, parent.StudyId, number, parent.Id, parent.Content, amendment));
    }

    /// <summary>Replaces a draft's content and, for a forked draft, optionally its amendment.</summary>
    public KernelError? Revise(string content, ProtocolAmendment? amendment)
    {
        if (Status != ProtocolVersionStatus.Draft)
        {
            return Transition("revised");
        }

        if (CheckContent(content) is { } invalidContent)
        {
            return invalidContent;
        }

        if (amendment is not null)
        {
            if (ParentVersionId is null)
            {
                return ProtocolErrors.AmendmentNotAllowed;
            }

            if (CheckAmendment(amendment) is { } invalidAmendment)
            {
                return invalidAmendment;
            }

            Amendment = amendment;
        }

        Content = content;
        return null;
    }

    /// <summary>Freezes a draft for review and sets its content hash.</summary>
    public KernelError? SubmitForReview()
    {
        if (Status != ProtocolVersionStatus.Draft)
        {
            return Transition("submitted for review");
        }

        ContentHash = Trust.ContentHash.Compute(Content);
        Status = ProtocolVersionStatus.InReview;
        return null;
    }

    /// <summary>Returns a version in review to draft and clears its content hash.</summary>
    public KernelError? ReturnToDraft()
    {
        if (Status != ProtocolVersionStatus.InReview)
        {
            return Transition("returned to draft");
        }

        ContentHash = null;
        Status = ProtocolVersionStatus.Draft;
        return null;
    }

    /// <summary>Approves a version in review. The caller has verified <paramref name="attestation"/>.</summary>
    public KernelError? Approve(AttestationId attestation)
    {
        if (Status != ProtocolVersionStatus.InReview)
        {
            return Transition("approved");
        }

        ApprovalAttestationId = attestation;
        Status = ProtocolVersionStatus.Approved;
        return null;
    }

    /// <summary>Withdraws a version in review or approved; terminal. The caller has verified <paramref name="attestation"/>.</summary>
    public KernelError? Withdraw(AttestationId attestation, string reason)
    {
        if (Status is not (ProtocolVersionStatus.InReview or ProtocolVersionStatus.Approved))
        {
            return Transition("withdrawn");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return KernelErrors.ReasonForChangeRequired;
        }

        WithdrawalAttestationId = attestation;
        WithdrawalReason = reason;
        Status = ProtocolVersionStatus.Withdrawn;
        return null;
    }

    /// <summary>
    /// Marks an approved version superseded once a later version is effective everywhere this one will ever be.
    /// Only Effectivity (#4777) decides that.
    /// </summary>
    public KernelError? Supersede()
    {
        if (Status != ProtocolVersionStatus.Approved)
        {
            return Transition("superseded");
        }

        Status = ProtocolVersionStatus.Superseded;
        return null;
    }

    private KernelError Transition(string change) =>
        ProtocolErrors.InvalidTransition.WithMessage($"Protocol version {Number} is {Status} and cannot be {change}.");

    /// <summary>How many design problems one error message lists.</summary>
    private const int MaxReportedProblems = 10;

    /// <summary>The design this version holds. Content is checked on every change, so it always reads.</summary>
    public ProtocolDesign Design() =>
        ProtocolDesignDocument.Read(Content).Design
        ?? throw new InvalidOperationException($"Protocol version {Number} holds content that is not a protocol design.");

    private static KernelError? CheckContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return ProtocolErrors.ContentRequired;
        }

        // The content is the executable protocol (ADR-2472): it must read as a design, with every reference inside it
        // resolving to an element of the right kind.
        var read = ProtocolDesignDocument.Read(content);
        if (read.IsValid)
        {
            return null;
        }

        var more = read.Problems.Count > MaxReportedProblems ? $" (and {read.Problems.Count - MaxReportedProblems} more)" : string.Empty;
        return ProtocolErrors.ContentInvalid.WithMessage(
            $"{ProtocolErrors.ContentInvalid.Message} {string.Join(" ", read.Problems.Take(MaxReportedProblems))}{more}");
    }

    private static KernelError? CheckAmendment(ProtocolAmendment? amendment)
    {
        var valid = amendment is not null
            && !string.IsNullOrWhiteSpace(amendment.Intent)
            && !string.IsNullOrWhiteSpace(amendment.Rationale)
            && !string.IsNullOrWhiteSpace(amendment.SummaryOfChanges)
            && amendment.ImpactedElements is { Count: > 0 } elements
            && elements.All(e => !string.IsNullOrWhiteSpace(e))
            && Enum.IsDefined(amendment.Classification)
            && Enum.IsDefined(amendment.TransitionPolicy);
        return valid ? null : ProtocolErrors.AmendmentInvalid;
    }
}
