using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Core.Domain;

using FluentValidation;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Signed service-to-service webhook payload posted by <c>apps/econsent</c> upon verified initial or amended
/// electronic consent capture so <c>apps/execution</c> keeps subject enrollment and active consent status synchronized.
/// </summary>
public sealed record EconsentWebhookPayload(
    [property: JsonPropertyName("subject_code")] string SubjectCode,
    [property: JsonPropertyName("study_id")] string StudyCode,
    [property: JsonPropertyName("site_id")] string? SiteCode,
    [property: JsonPropertyName("version_tag")] string VersionTag,
    [property: JsonPropertyName("version_index")] int VersionIndex,
    [property: JsonPropertyName("signed_at")] DateTimeOffset SignedAt,
    [property: JsonPropertyName("consent_id")] string? ConsentId = null);

public sealed class EconsentWebhookPayloadValidator : AbstractValidator<EconsentWebhookPayload>
{
    public EconsentWebhookPayloadValidator()
    {
        RuleFor(x => x.SubjectCode).NotEmpty();
        RuleFor(x => x.StudyCode).NotEmpty();
        RuleFor(x => x.VersionTag).NotEmpty();
        RuleFor(x => x.VersionIndex).GreaterThan(0);
        RuleFor(x => x.SignedAt).NotEqual(default(DateTimeOffset));
    }
}

public interface IEconsentWebhookApplicationService
{
    Task<Result<bool>> ApplyConsentSignedAsync(EconsentWebhookPayload payload, CancellationToken ct);
}

public sealed class EconsentWebhookApplicationService(
    ISubjectRepository subjects,
    IConsentRepository consents,
    IAuditLogWriter auditLog,
    IAuditActorScope actorScope) : IEconsentWebhookApplicationService
{
    public async Task<Result<bool>> ApplyConsentSignedAsync(EconsentWebhookPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ct.ThrowIfCancellationRequested();

        ServiceAccount account = ServiceAccounts.EConsentWebhook;
        actorScope.RunAs(account);

        var subjectCode = new SubjectCode(payload.SubjectCode.Trim());
        var studyCode = new StudyCode(payload.StudyCode.Trim());
        SiteCode? siteCode = !string.IsNullOrWhiteSpace(payload.SiteCode)
            ? new SiteCode(payload.SiteCode.Trim())
            : null;
        string versionTag = payload.VersionTag.Trim();

        await subjects.ExecuteInTransactionAsync(async token =>
        {
            var subjectRecord = await subjects.GetByCodeAsync(subjectCode.Value, token).ConfigureAwait(false);
            if (subjectRecord is null)
            {
                int nextIndex = await subjects.EnrollNextIndexAsync(studyCode, token).ConfigureAwait(false);
                var enrolled = ClinicalSubject.Create(
                    subjectCode,
                    studyCode,
                    siteCode,
                    nextIndex,
                    encryptedDemographics: null,
                    stratFactors: null);
                enrolled.ReasonForChange = account.ChangeReason;
                await subjects.AddAsync(enrolled, token).ConfigureAwait(false);
                await auditLog.WriteAsync(
                    nameof(ClinicalSubject),
                    enrolled.Id.ToString(),
                    "INSERT",
                    account.UserId,
                    enrolled.ReasonForChange,
                    oldValues: null,
                    newValues: null,
                    token).ConfigureAwait(false);
            }

            var existingConsent = await consents.GetAsync(subjectCode, studyCode, payload.VersionIndex, token)
                .ConfigureAwait(false);
            SubjectConsent targetConsent;
            if (existingConsent is not null)
            {
                existingConsent.MergeResubmission(
                    versionTag,
                    icfSigned: true,
                    icfSignedDate: payload.SignedAt,
                    requiresReconsent: false,
                    isPaperOverride: false,
                    incomingProtocolVersion: null);
                existingConsent.ReasonForChange = account.ChangeReason;
                targetConsent = existingConsent;
            }
            else
            {
                var versionRef = new ProtocolVersionRef(studyCode, versionTag, payload.VersionIndex);
                targetConsent = SubjectConsent.Create(
                    subjectCode,
                    versionRef,
                    icfSigned: true,
                    icfSignedDate: payload.SignedAt,
                    requiresReconsent: false,
                    isPaperOverride: false);
                targetConsent.ReasonForChange = account.ChangeReason;
                await consents.AddAsync(targetConsent, token).ConfigureAwait(false);
            }

            var staleConsents = await consents.GetOtherRequiringReconsentAsync(
                subjectCode,
                studyCode,
                payload.VersionIndex,
                token).ConfigureAwait(false);
            foreach (var priorConsent in staleConsents)
            {
                priorConsent.ClearReconsentRequirement();
                priorConsent.ReasonForChange = account.ChangeReason;
            }

            await auditLog.WriteAsync(
                nameof(SubjectConsent),
                targetConsent.Id.ToString(),
                "ECONSENT_WEBHOOK_SYNC",
                account.UserId,
                account.ChangeReason,
                oldValues: null,
                newValues: null,
                token).ConfigureAwait(false);

            await subjects.SaveChangesAsync(token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

        return true;
    }
}
