using Cadence.Kernel.Authorization;
using Cadence.Kernel.Protocol;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Creates the first protocol version of a study as a draft, numbered 1 (spec §6.2). Later versions are forked from an
/// approved one with <see cref="ForkProtocolVersion"/>.
/// </summary>
/// <remarks>
/// <para><c>Content</c>: The protocol document as JSON (CDISC USDM is the interchange format).</para>
/// </remarks>
[KernelCapability("protocol.draft", VerbClass.Propose, Resource = nameof(CreateProtocolDraft.StudyId))]
public sealed record CreateProtocolDraft(
    TenantId TenantId,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    string Content,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProtocolVersionState>;

/// <summary>Replaces a draft's content, and for a forked draft optionally its amendment.</summary>
/// <remarks>
/// <para><c>Amendment</c>: The replacement amendment of a forked draft; null keeps the current one.</para>
/// </remarks>
[KernelCapability("protocol.revise_draft", VerbClass.Propose, Resource = nameof(ReviseProtocolDraft.ProtocolVersionId))]
public sealed record ReviseProtocolDraft(
    TenantId TenantId,
    ProtocolVersionId ProtocolVersionId,
    string Content,
    ProtocolAmendment? Amendment,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProtocolVersionState>;

/// <summary>Freezes a draft for review and computes its content hash, which approval attestations bind to.</summary>
[KernelCapability("protocol.submit_for_review", VerbClass.Propose, Resource = nameof(SubmitProtocolVersionForReview.ProtocolVersionId))]
public sealed record SubmitProtocolVersionForReview(
    TenantId TenantId,
    ProtocolVersionId ProtocolVersionId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProtocolVersionState>;

/// <summary>Returns a version in review to draft so it can be revised; its content hash is cleared.</summary>
[KernelCapability("protocol.return_to_draft", VerbClass.Approve, Resource = nameof(ReturnProtocolVersionToDraft.ProtocolVersionId))]
public sealed record ReturnProtocolVersionToDraft(
    TenantId TenantId,
    ProtocolVersionId ProtocolVersionId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProtocolVersionState>;

/// <summary>
/// Approves a version in review. <c>AttestationId</c> must name an attestation with meaning
/// <c>Approved</c> bound to this version's content hash.
/// </summary>
[KernelCapability("protocol.approve", VerbClass.Approve, Resource = nameof(ApproveProtocolVersion.ProtocolVersionId))]
public sealed record ApproveProtocolVersion(
    TenantId TenantId,
    ProtocolVersionId ProtocolVersionId,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProtocolVersionState>;

/// <summary>
/// Withdraws a version in review or approved; terminal. The reason for change is the withdrawal reason, and
/// <c>AttestationId</c> must name an attestation with meaning <c>Withdrawn</c> bound to the version's
/// content hash.
/// </summary>
[KernelCapability("protocol.withdraw", VerbClass.Approve, Resource = nameof(WithdrawProtocolVersion.ProtocolVersionId))]
public sealed record WithdrawProtocolVersion(
    TenantId TenantId,
    ProtocolVersionId ProtocolVersionId,
    AttestationId AttestationId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProtocolVersionState>;

/// <summary>
/// Starts an amended draft from an approved version. The draft copies the parent's content, takes the study's next
/// number and carries <c>Amendment</c>. The parent stays approved (spec §6.2).
/// </summary>
[KernelCapability("protocol.amend", VerbClass.Propose, Resource = nameof(ForkProtocolVersion.ParentVersionId))]
public sealed record ForkProtocolVersion(
    TenantId TenantId,
    ProtocolVersionId ParentVersionId,
    ProtocolVersionId ProtocolVersionId,
    ProtocolAmendment Amendment,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<ProtocolVersionState>;

/// <summary>A protocol version's identity and lifecycle position after a command.</summary>
/// <remarks>
/// <para><c>ContentHash</c>: Set while the version is in review and afterwards; null for a draft.</para>
/// </remarks>
public sealed record ProtocolVersionState(
    ProtocolVersionId ProtocolVersionId,
    StudyId StudyId,
    int Number,
    ProtocolVersionStatus Status,
    string? ContentHash);

/// <summary>Errors raised by the protocol commands (spec §6.2).</summary>
public static class ProtocolErrors
{
    public static readonly KernelError ContentRequired =
        new("K.Protocol.ContentRequired", 422, "The protocol content is required.");

    public static readonly KernelError ContentInvalid =
        new("K.Protocol.ContentInvalid", 422, "The protocol content must be a protocol design document (ADR-2472).");

    public static readonly KernelError ReferenceUnknown =
        new("K.Protocol.ReferenceUnknown", 422, "The protocol design refers to a concept version, code-system version or artifact the tenant does not have.");

    public static readonly KernelError StudyUnknown =
        new("K.Protocol.StudyUnknown", 404, "The study does not exist.");

    public static readonly KernelError FirstVersionExists =
        new("K.Protocol.FirstVersionExists", 409, "The study already has a protocol version; fork an approved version instead.");

    public static readonly KernelError InvalidTransition =
        new("K.Protocol.InvalidTransition", 409, "The protocol version's status does not allow this change.");

    public static readonly KernelError AmendmentInvalid =
        new("K.Protocol.AmendmentInvalid", 422, "The amendment needs an intent, a rationale, a summary of changes and at least one impacted element.");

    public static readonly KernelError AmendmentNotAllowed =
        new("K.Protocol.AmendmentNotAllowed", 422, "Only a forked protocol version carries an amendment.");

    public static readonly KernelError AttestationRejected =
        new("K.Protocol.AttestationRejected", 422, "The attestation does not approve or withdraw this exact content.");
}
