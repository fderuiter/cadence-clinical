using System;
using System.Collections.Frozen;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Forms.Errors;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Forms;

/// <summary>
/// Aggregate root representing an eCRF form submission page as the auditable unit of PI sign-off.
/// Enforces strict 21 CFR Part 11 locking upon approval and audited unlock reversion.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-002
/// @req:PRD-SYS-053
/// @req:SRS-EDC-002
/// @req:SRS-EDC-003
/// </summary>
public sealed class FormSubmission : AggregateRoot<FormSubmissionId>
{
    public static readonly FrozenSet<string> ValidSigningReasons = new[]
    {
        "I attest that this data is accurate and complete.",
        "PI approval and sign-off.",
        "Review and confirmation.",
        "DATA_RECORDING",
        "DATA_ENTRY_COMPLETED",
        "PI_REVIEW",
        "PI_SIGN_OFF",
        "COMPLIANCE_ATTESTATION",
        "I approve the accuracy and completeness of this casebook.",
        "I approve the accuracy and completeness of this form.",
        "I attest that I have reviewed and approved these clinical data.",
        "Sign-off"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public StudyCode StudyId { get; private set; }
    public SiteCode? SiteId { get; private set; }
    public SubjectCode SubjectId { get; private set; }
    public string? VisitId { get; private set; }
    public FormId FormId { get; private set; }
    public FormSubmissionStatus Status { get; private set; }
    public string? ProtocolVersion { get; private set; }
    public string? Payload { get; private set; }
    public SignatureManifest? SignatureManifest { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsReadonly { get; private set; }
    public string? ClonedFromId { get; private set; }

    private FormSubmission() { } // EF Core

    public static Result<FormSubmission> Initialize(
        StudyCode studyId,
        SubjectCode subjectId,
        FormId formId,
        SiteCode? siteId = null,
        string? visitId = null,
        string? protocolVersion = null,
        string? initialPayload = null)
    {
        var submission = new FormSubmission
        {
            Id = FormSubmissionId.New(),
            StudyId = studyId,
            SubjectId = subjectId,
            FormId = formId,
            SiteId = siteId,
            VisitId = visitId,
            ProtocolVersion = protocolVersion ?? "1.0.0",
            Payload = initialPayload,
            Status = FormSubmissionStatus.Draft,
            IsActive = true,
            IsReadonly = false,
            SignatureManifest = null,
            ClonedFromId = null
        };

        return submission;
    }

    public Result<bool> UpdatePayload(string newPayload, string reasonForChange)
    {
        if (!IsActive)
        {
            return FormErrors.InactiveForm(Id);
        }

        if (IsReadonly || Status == FormSubmissionStatus.Approved)
        {
            return FormErrors.FormReadonly(Id);
        }

        Payload = newPayload;
        ReasonForChange = reasonForChange;
        return true;
    }

    public Result<bool> Complete(IReadOnlyList<DomainError>? blockingErrors = null)
    {
        if (!IsActive)
        {
            return FormErrors.InactiveForm(Id);
        }

        if (IsReadonly || Status == FormSubmissionStatus.Approved)
        {
            return FormErrors.FormReadonly(Id);
        }

        if (Status != FormSubmissionStatus.Draft)
        {
            return FormErrors.InvalidStatusTransition(Status.ToString(), FormSubmissionStatus.Completed.ToString());
        }

        if (blockingErrors != null && blockingErrors.Count > 0)
        {
            return FormErrors.FatalEditChecksPresent($"Form completion blocked: {blockingErrors.Count} fatal edit check violation(s) present.");
        }

        Status = FormSubmissionStatus.Completed;
        return true;
    }

    public Result<bool> Approve(SignatureManifest manifest)
    {
        if (!IsActive)
        {
            return FormErrors.InactiveForm(Id);
        }

        if (IsReadonly || Status == FormSubmissionStatus.Approved)
        {
            return FormErrors.FormReadonly(Id);
        }

        if (Status != FormSubmissionStatus.Completed)
        {
            return FormErrors.InvalidStatusTransition(Status.ToString(), FormSubmissionStatus.Approved.ToString());
        }

        if (manifest is null)
        {
            return FormErrors.SignatureRequired();
        }

        if (string.IsNullOrWhiteSpace(manifest.SigningReason) ||
            !ValidSigningReasons.Contains(manifest.SigningReason.Trim()))
        {
            return FormErrors.InvalidSigningReason(manifest?.SigningReason ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(manifest.PayloadHash))
        {
            return FormErrors.InvalidPayloadHash();
        }

        Status = FormSubmissionStatus.Approved;
        IsReadonly = true;
        SignatureManifest = manifest;
        return true;
    }

    /// <summary>
    /// Strict 21 CFR Part 11 Unlock: revokes electronic signature and reverts to DRAFT with mandatory ReasonForChange.
    /// </summary>
    public Result<bool> Unlock(string unlockedBy, string reasonForChange)
    {
        if (Status != FormSubmissionStatus.Approved)
        {
            return FormErrors.NotApproved();
        }

        if (string.IsNullOrWhiteSpace(reasonForChange))
        {
            return FormErrors.UnlockReasonRequired();
        }

        Status = FormSubmissionStatus.Draft;
        IsReadonly = false;
        SignatureManifest = null;
        ReasonForChange = reasonForChange;
        return true;
    }

    public Result<bool> Reopen(string reasonForChange)
    {
        if (!IsActive)
        {
            return FormErrors.InactiveForm(Id);
        }

        if (IsReadonly || Status == FormSubmissionStatus.Approved)
        {
            return FormErrors.FormReadonly(Id);
        }

        if (Status != FormSubmissionStatus.Completed)
        {
            return FormErrors.InvalidStatusTransition(Status.ToString(), FormSubmissionStatus.Draft.ToString());
        }

        Status = FormSubmissionStatus.Draft;
        ReasonForChange = reasonForChange;
        return true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
