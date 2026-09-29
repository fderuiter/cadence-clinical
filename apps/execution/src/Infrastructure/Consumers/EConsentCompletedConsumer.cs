using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel.Time;

using MassTransit;

using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Execution.Infrastructure.Consumers;

public static partial class EConsentCompletedConsumerLoggingExtensions
{
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Dropped malformed consent event: SubjectId='{SubjectId}', StudyId='{StudyId}'")]
    public static partial void LogMalformedConsentEventDropped(this ILogger logger, string? subjectId, string? studyId);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Processing consent completed event for Subject '{SubjectId}', Study '{StudyId}'")]
    public static partial void LogConsentEventProcessing(this ILogger logger, string subjectId, string studyId);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Information,
        Message = "Auto-enrolled subject '{SubjectId}' for study '{StudyId}' at enrollment index {EnrollmentIndex}")]
    public static partial void LogSubjectAutoEnrolled(this ILogger logger, string subjectId, string studyId, int enrollmentIndex);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Information,
        Message = "Backfilled consent record for subject '{SubjectId}', study '{StudyId}', version index {VersionIndex}")]
    public static partial void LogConsentRecordBackfilled(this ILogger logger, string subjectId, string studyId, int versionIndex);
}

/// <summary>
/// MassTransit replacement for the legacy fire-and-forget Redis pub/sub auto-enrollment subscriber
/// (<c>apps/execution/workers/consent_subscriber.py</c>'s <c>handle_consent_completed_message</c> /
/// <c>_run_subscriber_sync</c> — a <b>MIG-06 hard blocker</b>, not transcribed as a literal
/// background-thread subscriber per <c>01_spec.md</c> Rule 8's explicit redesign mandate). Preserves the
/// legacy's exact idempotency / sequential-index / independent-consent-backfill behavioral contract
/// (SCN-009) while closing MIG-06's documented delivery-durability gap: <c>Program.cs</c> registers this
/// consumer behind MassTransit's EF Core transactional outbox
/// (<c>AddEntityFrameworkOutbox&lt;ExecutionDbContext&gt;</c>), so every write below is safe to redeliver
/// at least once.
///
/// <para>
/// Deliberately has <b>no</b> top-level <c>try</c>/<c>catch</c>: the one documented "swallow and move on"
/// case is the malformed-event silent drop below (an explicit, narrow, early-return check on the
/// caller-controlled message shape) — a genuine infrastructure failure (e.g. a <c>DbUpdateException</c>
/// out of <see cref="ISubjectRepository.SaveChangesAsync"/>) must propagate out of <see cref="Consume"/>
/// so MassTransit's own retry/outbox/fault pipeline handles it, exactly as
/// <c>02_technical_design.md</c> §5.2 and this ticket's Acceptance Criteria require.
/// </para>
/// @req:PRD-SUB-001
/// </summary>
public sealed class EConsentCompletedConsumer(
    ISubjectRepository subjects,
    IConsentRepository consents,
    IAuditLogWriter auditLog,
    IAuditActorScope actorScope,
    IClock clock,
    ILogger<EConsentCompletedConsumer>? logger = null)
    : IConsumer<EConsentCompletedEvent>
{
    public async Task Consume(ConsumeContext<EConsentCompletedEvent> context)
    {
        // Attribute every write in this scope, including the GxPAuditInterceptor's own entries, to the
        // consumer's named service account; there is no anonymous fallback actor (#4767).
        ServiceAccount account = ServiceAccounts.EConsentConsumer;
        actorScope.RunAs(account);

        var msg = context.Message;
        if (string.IsNullOrWhiteSpace(msg.SubjectId) || string.IsNullOrWhiteSpace(msg.StudyId))
        {
            // Malformed event -- dropped silently, matching the legacy warning-log-only behavior exactly
            // (01_spec.md Rule 8). Not a validation failure and not retried: there is no better-formed
            // redelivery of the same malformed payload coming, so treating this as a fault would only ever
            // exhaust MassTransit's retry budget and dead-letter a message the legacy system also discarded.
            if (logger != null)
            {
                logger.LogMalformedConsentEventDropped(msg.SubjectId, msg.StudyId);
            }
            return;
        }

        if (logger != null)
        {
            logger.LogConsentEventProcessing(msg.SubjectId, msg.StudyId);
        }

        var subjectCode = new SubjectCode(msg.SubjectId);
        var studyCode = new StudyCode(msg.StudyId);

        // Idempotency check #1 (subject creation): looked up by domain code alone -- deliberately not the
        // dual id-then-code fallback GetByIdAsync/GetByCodeAsync sequencing an application service would use,
        // since this event only ever carries the domain code. Skip entirely, log-only, no error when found.
        var existing = await subjects.GetByCodeAsync(subjectCode.Value, context.CancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            // Same shared, transaction-scoped, advisory-locked path SubjectApplicationService.CreateAsync
            // (MIG-SUBJ-018) calls for its own enrollment -- 02_technical_design.md §4.2's explicit closing
            // of the "identical race exists independently in both places" finding. ISubjectRepository
            // exposes exactly one index-assignment method, so there is no second, hand-rolled query path
            // this call site could drift onto.
            int nextIndex = await subjects.EnrollNextIndexAsync(studyCode, context.CancellationToken).ConfigureAwait(false);
            SiteCode? siteCode = msg.SiteId is { Length: > 0 } site ? new SiteCode(site) : null;
            var created = ClinicalSubject.Create(subjectCode, studyCode, siteCode, nextIndex,
                encryptedDemographics: null, stratFactors: null);
            created.ReasonForChange = account.ChangeReason;
            await subjects.AddAsync(created, context.CancellationToken).ConfigureAwait(false);
            await auditLog.WriteAsync(nameof(ClinicalSubject), created.Id.ToString(), "INSERT",
                account.UserId, created.ReasonForChange,
                oldValues: null, newValues: null, context.CancellationToken).ConfigureAwait(false);

            if (logger != null)
            {
                logger.LogSubjectAutoEnrolled(msg.SubjectId, msg.StudyId, nextIndex);
            }
        }

        // Idempotency check #2 (consent-cache backfill) -- INDEPENDENT of check #1 above: this runs even
        // when the subject already existed, and is itself skipped only when a matching consent-cache row
        // already exists. The two checks never share state, so "subject exists but consent doesn't" and
        // "both already exist" (pure redelivery) are both handled correctly without coupling one to the other.
        var version = new ProtocolVersionRef(studyCode, msg.VersionTag, msg.VersionIndex);
        var consentExists = await consents.ExistsAsync(subjectCode, studyCode, msg.VersionIndex, context.CancellationToken)
            .ConfigureAwait(false);
        if (!consentExists)
        {
            var consent = SubjectConsent.Create(subjectCode, version, icfSigned: true, icfSignedDate: clock.UtcNow,
                requiresReconsent: false, isPaperOverride: false, clock);
            await consents.AddAsync(consent, context.CancellationToken).ConfigureAwait(false);
            await auditLog.WriteAsync(nameof(SubjectConsent), consent.Id.ToString(), "INSERT",
                account.UserId, account.ChangeReason,
                oldValues: null, newValues: null, context.CancellationToken).ConfigureAwait(false);

            if (logger != null)
            {
                logger.LogConsentRecordBackfilled(msg.SubjectId, msg.StudyId, msg.VersionIndex);
            }
        }

        // One transaction, both writes: ISubjectRepository/IConsentRepository share the same underlying
        // ExecutionDbContext in this slice, so either repository's SaveChangesAsync commits both pending
        // writes atomically -- and, once Program.cs's AddEntityFrameworkOutbox<ExecutionDbContext> wraps this
        // receive endpoint, inside the same transactional outbox MassTransit uses for at-least-once delivery.
        await subjects.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
    }
}
