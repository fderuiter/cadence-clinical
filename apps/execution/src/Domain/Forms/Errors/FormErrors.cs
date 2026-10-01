using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Forms.Errors;

/// <summary>
/// Domain error factory for form submissions and 21 CFR Part 11 electronic sign-off.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:PRD-SYS-053
/// @req:SRS-EDC-002
/// @req:SRS-EDC-003
/// </summary>
public static class FormErrors
{
    public static DomainError NotFound(string id) =>
        DomainError.NotFound("FORM_NOT_FOUND", $"Form submission '{id}' was not found.");

    public static DomainError FormReadonly(FormSubmissionId id) =>
        DomainError.Validation("FORM_LOCKED", $"Form submission '{id}' is approved/read-only and cannot be modified without an explicit unlock.")
            with
        { Type = ErrorType.Conflict };

    public static DomainError InactiveForm(FormSubmissionId id) =>
        DomainError.Validation("FORM_INACTIVE", $"Form submission '{id}' is inactive and cannot be modified.");

    public static DomainError InvalidStatusTransition(string from, string to) =>
        DomainError.Validation("INVALID_FORM_TRANSITION", $"Form submission transition from {from} to {to} is forbidden.");

    public static DomainError FatalEditChecksPresent(string message) =>
        DomainError.Validation("FATAL_EDIT_CHECKS_PRESENT", message);

    public static DomainError InvalidSigningReason(string reason) =>
        DomainError.Validation("INVALID_SIGNING_REASON", $"Signing reason '{reason}' is not recognized under 21 CFR Part 11 policy.");

    public static DomainError InvalidPayloadHash() =>
        DomainError.Validation("INVALID_PAYLOAD_HASH", "Signature payload hash does not match current canonical form payload.");

    public static DomainError SignatureRequired() =>
        DomainError.Validation("SIGNATURE_MANIFEST_REQUIRED", "A valid 21 CFR Part 11 signature manifest is required for form approval.");

    public static DomainError UnauthorizedSignerRole(string role) =>
        DomainError.Forbidden("UNAUTHORIZED_SIGNER_ROLE", $"Role '{role}' is not authorized for Principal Investigator signature sign-off.");

    public static DomainError SignerIdentityMismatch(string claimedSignerId) =>
        DomainError.Forbidden("SIGNER_IDENTITY_MISMATCH", $"Signature manifest names signer '{claimedSignerId}', who is not the authenticated caller.");

    public static DomainError ReauthenticationRequired(string? message = null) =>
        DomainError.Unauthorized("REAUTHENTICATION_REQUIRED", message ?? "21 CFR Part 11 mandate: Re-authentication is required.");

    public static DomainError UnlockReasonRequired() =>
        DomainError.Validation("UNLOCK_REASON_REQUIRED", "A documented GxP ReasonForChange is strictly required to unlock an approved form.");

    public static DomainError NotApproved() =>
        DomainError.Validation("FORM_NOT_APPROVED", "Form submission is not in APPROVED status; cannot unlock.");

    public static DomainError TrialLocked(string message) =>
        DomainError.Forbidden("TRIAL_LOCKED", message);

    public static DomainError ProtocolVersionMismatch(string explicitVersion, string activeVersion) =>
        DomainError.Validation(
            "PROTOCOL_VERSION_MISMATCH",
            $"Submitted protocol version '{explicitVersion}' does not match subject active protocol version '{activeVersion}'.");
}
