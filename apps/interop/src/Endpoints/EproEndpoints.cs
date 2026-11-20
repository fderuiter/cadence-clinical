using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Interop.Endpoints;

/// <summary>
/// Participant ePRO / eCOA Minimal API endpoints serving the Subject Portal offline-first workflows:
/// assignments, instruments, compliance, notifications, notification acknowledgement, single diary
/// submission, and batched offline queue reconciliation with sequence idempotency, conflict archival
/// (<see cref="EproSubmissionDefeated"/>), stale-version quarantine (<see cref="EproSubmissionQuarantine"/>),
/// and canonical EDC/Kernel observation forwarding (<c>K-TRUTH-1</c>).
/// @req:PRD-SYS-001
/// </summary>
public static class EproEndpoints
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<string> PrivilegedClinicalRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleNormalization.ROLE_SYSADMIN,
        RoleNormalization.ROLE_SPONSOR_DM,
        RoleNormalization.ROLE_SPONSOR_MM,
        RoleNormalization.ROLE_INVESTIGATOR,
        RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR,
        RoleNormalization.ROLE_AUTHORIZED_ER_PHYSICIAN,
        RoleNormalization.ROLE_LEAD_INVESTIGATOR,
        RoleNormalization.ROLE_CRC,
        RoleNormalization.ROLE_CRA_CANONICAL,
        "monitor",
        RoleNormalization.ROLE_AUDITOR_CANONICAL,
    };

    public static IEndpointRouteBuilder MapEproEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/interop")
            .WithTags("Participant ePRO & eCOA");

        group.MapGet("/assignments/subject/{subjectId}", ListSubjectAssignmentsAsync)
            .WithName("ListSubjectAssignments")
            .RequirePermission("ecoa_schedule:read")
            .Produces<IReadOnlyList<EproAssignmentDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/subjects/{subjectId}/instruments", ListSubjectInstrumentsAsync)
            .WithName("ListSubjectInstruments")
            .RequirePermission("ecoa_diary:read")
            .Produces<IReadOnlyList<EproInstrumentDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/instruments/{id}", GetInstrumentByIdAsync)
            .WithName("GetEproInstrument")
            .RequirePermission("ecoa_diary:read")
            .Produces<EproInstrumentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/subjects/{subjectId}/compliance", GetSubjectComplianceAsync)
            .WithName("GetSubjectCompliance")
            .RequirePermission("ecoa_submission:read")
            .Produces<EproComplianceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/subjects/{subjectId}/notifications", ListSubjectNotificationsAsync)
            .WithName("ListSubjectNotifications")
            .RequirePermission("ecoa_notification:read")
            .Produces<IReadOnlyList<EproNotificationDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/notifications/{notificationId}/acknowledge", AcknowledgeNotificationAsync)
            .WithName("AcknowledgeSubjectNotification")
            .RequirePermission("ecoa_notification:acknowledge")
            .Produces<EproNotificationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/epro/submit", SubmitEproDiaryAsync)
            .WithName("SubmitEproDiary")
            .RequirePermission("ecoa_submission:create")
            .Produces<EproReconciliationResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/epro/sync", SyncEproBatchAsync)
            .WithName("SyncEproBatch")
            .RequirePermission("ecoa_submission:create")
            .Produces<EproSyncBatchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public static async Task<IResult> ListSubjectAssignmentsAsync(
        string subjectId,
        HttpContext httpContext,
        InteropDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        if (await EnforceSubjectOwnershipAsync(httpContext, subjectId, ct).ConfigureAwait(false) is { } forbidden)
        {
            return forbidden;
        }

        var now = clock.UtcNow;
        var rawAssignments = await db.SubjectAssignments.AsNoTracking()
            .Where(a => a.SubjectId == subjectId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var assignments = rawAssignments
            .OrderBy(a => a.DueAt)
            .ToList();

        if (assignments.Count == 0)
        {
            return Results.Ok(Array.Empty<EproAssignmentDto>());
        }

        var instrumentIds = assignments.Select(a => a.InstrumentId).Distinct(StringComparer.Ordinal).ToList();
        var instruments = await db.Instruments.AsNoTracking()
            .Where(i => instrumentIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        var latestSubmissions = await LoadLatestSubmissionsByDiaryAsync(db, subjectId, ct).ConfigureAwait(false);
        var dtos = assignments
            .Select(a => MapAssignmentDto(a, instruments, latestSubmissions, now))
            .ToList();

        return Results.Ok(dtos);
    }

    public static async Task<IResult> ListSubjectInstrumentsAsync(
        string subjectId,
        HttpContext httpContext,
        InteropDbContext db,
        CancellationToken ct)
    {
        if (await EnforceSubjectOwnershipAsync(httpContext, subjectId, ct).ConfigureAwait(false) is { } forbidden)
        {
            return forbidden;
        }

        var assignedIds = await db.SubjectAssignments.AsNoTracking()
            .Where(a => a.SubjectId == subjectId)
            .Select(a => a.InstrumentId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (assignedIds.Count == 0)
        {
            return Results.Ok(Array.Empty<EproInstrumentDto>());
        }

        var instruments = await db.Instruments.AsNoTracking()
            .Where(i => assignedIds.Contains(i.Id))
            .OrderBy(i => i.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Results.Ok(instruments.Select(MapInstrumentDto).ToList());
    }

    public static async Task<IResult> GetInstrumentByIdAsync(
        string id,
        HttpContext httpContext,
        InteropDbContext db,
        CancellationToken ct)
    {
        var (isSubjectRestricted, subjectCode) = await ResolveSubjectRestrictionAsync(httpContext, ct).ConfigureAwait(false);
        if (isSubjectRestricted && string.IsNullOrWhiteSpace(subjectCode))
        {
            return ForbiddenCrossSubjectResult();
        }

        string? requestedSubjectId = httpContext.Request.Query["subject_id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(requestedSubjectId)
            && await EnforceSubjectOwnershipAsync(httpContext, requestedSubjectId, ct).ConfigureAwait(false) is { } queryForbidden)
        {
            return queryForbidden;
        }

        var instrument = await db.Instruments.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            .ConfigureAwait(false);

        if (instrument is null)
        {
            return Results.Problem($"Instrument '{id}' was not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(MapInstrumentDto(instrument));
    }

    public static async Task<IResult> GetSubjectComplianceAsync(
        string subjectId,
        HttpContext httpContext,
        InteropDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        if (await EnforceSubjectOwnershipAsync(httpContext, subjectId, ct).ConfigureAwait(false) is { } forbidden)
        {
            return forbidden;
        }

        var now = clock.UtcNow;
        var rawAssignments = await db.SubjectAssignments.AsNoTracking()
            .Where(a => a.SubjectId == subjectId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var assignments = rawAssignments
            .OrderByDescending(a => a.DueAt)
            .ToList();

        var instrumentIds = assignments.Select(a => a.InstrumentId).Distinct(StringComparer.Ordinal).ToList();
        var instruments = instrumentIds.Count == 0
            ? new Dictionary<string, Instrument>(StringComparer.Ordinal)
            : await db.Instruments.AsNoTracking()
                .Where(i => instrumentIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, StringComparer.Ordinal, ct)
                .ConfigureAwait(false);

        var latestSubmissions = await LoadLatestSubmissionsByDiaryAsync(db, subjectId, ct).ConfigureAwait(false);

        var mapped = assignments
            .Select(a => MapAssignmentDto(a, instruments, latestSubmissions, now))
            .ToList();

        int completedCount = mapped.Count(x => x.Status == "COMPLETED");
        int overdueCount = mapped.Count(x => x.Status == "OVERDUE");
        int pendingCount = Math.Max(0, mapped.Count - completedCount - overdueCount);
        double rate = mapped.Count > 0
            ? Math.Round((double)completedCount / mapped.Count * 100.0, 2)
            : 0.0;

        var history = mapped
            .Select(x => new EproComplianceAssignmentItem(
                x.Id,
                x.InstrumentId,
                x.InstrumentName,
                x.DueAt,
                x.EndDate,
                x.SubmittedAt,
                x.Status))
            .ToList();

        return Results.Ok(new EproComplianceResponse(
            subjectId,
            rate,
            completedCount,
            pendingCount,
            overdueCount,
            mapped.Count,
            history));
    }

    public static async Task<IResult> ListSubjectNotificationsAsync(
        string subjectId,
        HttpContext httpContext,
        InteropDbContext db,
        CancellationToken ct)
    {
        if (await EnforceSubjectOwnershipAsync(httpContext, subjectId, ct).ConfigureAwait(false) is { } forbidden)
        {
            return forbidden;
        }

        var notifications = await db.SubjectNotifications.AsNoTracking()
            .Where(n => n.SubjectId == subjectId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Results.Ok(notifications.OrderByDescending(n => n.DueAt).Select(MapNotificationDto).ToList());
    }

    public static async Task<IResult> AcknowledgeNotificationAsync(
        string notificationId,
        HttpContext httpContext,
        InteropDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var (isSubjectRestricted, callerSubjectCode) = await ResolveSubjectRestrictionAsync(httpContext, ct).ConfigureAwait(false);
        if (isSubjectRestricted)
        {
            if (string.IsNullOrWhiteSpace(callerSubjectCode))
            {
                return ForbiddenCrossSubjectResult();
            }

            string? querySubjectId = httpContext.Request.Query["subject_id"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(querySubjectId)
                && !string.Equals(callerSubjectCode, querySubjectId, StringComparison.Ordinal))
            {
                return ForbiddenCrossSubjectResult();
            }
        }

        EproAcknowledgeNotificationRequest? request = null;
        if (httpContext.Request.Body.CanRead)
        {
            using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
            string rawBody = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                try
                {
                    request = JsonSerializer.Deserialize<EproAcknowledgeNotificationRequest>(rawBody, RequestJsonOptions);
                }
                catch (JsonException)
                {
                    return Results.Problem("Invalid JSON payload for notification acknowledgement.", statusCode: StatusCodes.Status400BadRequest);
                }
            }
        }

        if (isSubjectRestricted
            && !string.IsNullOrWhiteSpace(request?.SubjectId)
            && !string.Equals(callerSubjectCode, request.SubjectId, StringComparison.Ordinal))
        {
            return ForbiddenCrossSubjectResult();
        }

        string? changeReason = ResolveChangeReason(httpContext);
        if (string.IsNullOrWhiteSpace(changeReason))
        {
            return MissingChangeReasonResult();
        }

        var notification = await db.SubjectNotifications
            .FirstOrDefaultAsync(n => n.Id == notificationId, ct)
            .ConfigureAwait(false);

        if (notification is null)
        {
            return Results.Problem($"Notification '{notificationId}' was not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (await EnforceSubjectOwnershipAsync(httpContext, notification.SubjectId, ct).ConfigureAwait(false) is { } ownerForbidden)
        {
            return ownerForbidden;
        }

        var now = clock.UtcNow;
        var (actorId, actorRole, _) = GetCallerContext(httpContext);

        notification.IsRead = true;
        notification.ReadAt ??= now;
        notification.DeliveryStatus = "ACKNOWLEDGED";
        notification.ReasonForChange = changeReason;
        notification.VersionIndex += 1;

        db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = now,
            UserId = actorId,
            UserRole = actorRole,
            Action = "ACKNOWLEDGE_NOTIFICATION",
            Details = $"Notification '{notification.Id}' acknowledged for participant '{notification.SubjectId}'.",
            ChangeReason = changeReason,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(MapNotificationDto(notification));
    }

    public static async Task<IResult> SubmitEproDiaryAsync(
        EproSubmitRequest? request,
        HttpContext httpContext,
        InteropDbContext db,
        ICanonicalObservationForwarder forwarder,
        IClock clock,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SubjectId) || string.IsNullOrWhiteSpace(request.DiaryId))
        {
            return Results.Problem("subject_id and diary_id are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await EnforceSubjectOwnershipAsync(httpContext, request.SubjectId, ct).ConfigureAwait(false) is { } forbidden)
        {
            return forbidden;
        }

        string? changeReason = ResolveChangeReason(httpContext);
        if (string.IsNullOrWhiteSpace(changeReason))
        {
            return MissingChangeReasonResult();
        }

        var outcome = await ReconcileSingleSubmissionAsync(
            request,
            httpContext,
            db,
            forwarder,
            clock,
            changeReason,
            seenSequenceKeysInCurrentBatch: new HashSet<string>(StringComparer.Ordinal),
            ct).ConfigureAwait(false);

        if (outcome.ValidationFailure is { } validationFailure)
        {
            return validationFailure;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(outcome.Result!);
    }

    public static async Task<IResult> SyncEproBatchAsync(
        EproSyncBatchRequest? request,
        HttpContext httpContext,
        InteropDbContext db,
        ICanonicalObservationForwarder forwarder,
        IClock clock,
        CancellationToken ct)
    {
        if (request?.Submissions is null || request.Submissions.Count == 0)
        {
            return Results.Problem("submissions must contain at least one ePRO diary payload.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await EnforceSubjectBatchOwnershipAsync(httpContext, request.Submissions, ct).ConfigureAwait(false) is { } forbidden)
        {
            return forbidden;
        }

        string? changeReason = ResolveChangeReason(httpContext);
        if (string.IsNullOrWhiteSpace(changeReason))
        {
            return MissingChangeReasonResult();
        }

        // Validate every item in the batch before staging changes so a schema/range error triggers atomic 422 batch
        // rejection (allowing the Subject Portal's isolateSubmissionsIndividually fallback to run cleanly).
        foreach (var item in request.Submissions)
        {
            if (string.IsNullOrWhiteSpace(item.SubjectId) || string.IsNullOrWhiteSpace(item.DiaryId))
            {
                return Results.Problem("Each submission requires non-empty subject_id and diary_id.", statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            if (item.Answers.ValueKind != JsonValueKind.Object)
            {
                return Results.Problem($"Submission for diary '{item.DiaryId}' must contain an answers object.", statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var instrument = await db.Instruments.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == item.DiaryId, ct)
                .ConfigureAwait(false);

            if (instrument is null)
            {
                return Results.Problem($"Unknown instrument '{item.DiaryId}'.", statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            int submittedVersion = item.VersionIndex ?? 1;
            if (submittedVersion == instrument.VersionIndex
                && ValidateAnswersAgainstInstrument(instrument.ItemsJson, item.Answers) is { } validationError)
            {
                return Results.Problem(validationError, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        }

        var results = new List<EproReconciliationResultDto>(request.Submissions.Count);
        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in request.Submissions)
        {
            var outcome = await ReconcileSingleSubmissionAsync(
                item,
                httpContext,
                db,
                forwarder,
                clock,
                changeReason,
                seenInBatch,
                ct).ConfigureAwait(false);

            if (outcome.ValidationFailure is { } validationFailure)
            {
                return validationFailure;
            }

            results.Add(outcome.Result!);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new EproSyncBatchResponse("success", results));
    }

    private static async Task<ReconcileItemOutcome> ReconcileSingleSubmissionAsync(
        EproSubmitRequest item,
        HttpContext httpContext,
        InteropDbContext db,
        ICanonicalObservationForwarder forwarder,
        IClock clock,
        string changeReason,
        HashSet<string> seenSequenceKeysInCurrentBatch,
        CancellationToken ct)
    {
        if (item.Answers.ValueKind != JsonValueKind.Object)
        {
            return new ReconcileItemOutcome(
                null,
                Results.Problem($"Submission for diary '{item.DiaryId}' must contain an answers object.", statusCode: StatusCodes.Status422UnprocessableEntity));
        }

        var instrument = await db.Instruments.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == item.DiaryId, ct)
            .ConfigureAwait(false);

        if (instrument is null)
        {
            return new ReconcileItemOutcome(
                null,
                Results.Problem($"Unknown instrument '{item.DiaryId}'.", statusCode: StatusCodes.Status422UnprocessableEntity));
        }

        var now = clock.UtcNow;
        var deviceTimestamp = item.DeviceTimestamp ?? now;
        int submittedVersion = item.VersionIndex ?? 1;
        var (actorId, actorRole, actorRoles) = GetCallerContext(httpContext);
        string studyCode = ResolveCallerStudyCode(httpContext);
        string tenantCode = ResolveCallerTenantCode(httpContext);
        string answersJson = item.Answers.GetRawText();
        string markersJson = SerializeMarkers(item.OfflineSyncMarkers);

        // 1. Sequence idempotency check across prior submissions, quarantines, defeated records, and the current batch.
        if (TryBuildSequenceKey(item.SubjectId, item.OfflineSyncMarkers, out string sequenceKey, out string clientId, out long sequenceNumber))
        {
            if (!seenSequenceKeysInCurrentBatch.Add(sequenceKey))
            {
                db.InteropAuditLogs.Add(new InteropAuditLog
                {
                    Timestamp = now,
                    UserId = actorId,
                    UserRole = actorRole,
                    Action = "EPRO_DUPLICATE_SEQUENCE_IGNORED",
                    Details = $"Duplicate in-batch sequence {sequenceNumber} from client '{clientId}' for participant '{item.SubjectId}' ignored.",
                    ChangeReason = changeReason,
                });

                return new ReconcileItemOutcome(
                    new EproReconciliationResultDto(
                        Id: null,
                        SubjectId: item.SubjectId,
                        DiaryId: item.DiaryId,
                        Status: "SYNCED",
                        Answers: CloneJsonElement(item.Answers),
                        OfflineSyncMarkers: item.OfflineSyncMarkers,
                        DuplicateSequenceIgnored: true,
                        CanonicalObservationsCount: 0),
                    null);
            }

            var priorMatchingSubmission = await FindSubmissionBySequenceMarkerAsync(db, item.SubjectId, clientId, sequenceNumber, ct)
                .ConfigureAwait(false);
            if (priorMatchingSubmission is not null)
            {
                db.InteropAuditLogs.Add(new InteropAuditLog
                {
                    Timestamp = now,
                    UserId = actorId,
                    UserRole = actorRole,
                    Action = "EPRO_DUPLICATE_SEQUENCE_IGNORED",
                    Details = $"Idempotent replay of sequence {sequenceNumber} from client '{clientId}' for submission '{priorMatchingSubmission.Id}'.",
                    ChangeReason = changeReason,
                });

                return new ReconcileItemOutcome(
                    new EproReconciliationResultDto(
                        Id: priorMatchingSubmission.Id,
                        SubjectId: item.SubjectId,
                        DiaryId: item.DiaryId,
                        Status: "SYNCED",
                        Answers: ParseJsonElementOrEmptyObject(priorMatchingSubmission.AnswersJson),
                        OfflineSyncMarkers: item.OfflineSyncMarkers,
                        DuplicateSequenceIgnored: true,
                        CanonicalObservationsCount: 0),
                    null);
            }
        }

        // 2. Stale instrument version quarantine (EproSubmissionQuarantine + coordinator ClinicalQuery).
        if (submittedVersion != instrument.VersionIndex)
        {
            string mismatchMessage = $"INSTRUMENT_VERSION_MISMATCH: submitted version {submittedVersion.ToString(CultureInfo.InvariantCulture)} does not match active version {instrument.VersionIndex.ToString(CultureInfo.InvariantCulture)}.";
            var quarantine = new EproSubmissionQuarantine
            {
                SubjectId = item.SubjectId,
                DiaryId = item.DiaryId,
                DeviceTimestamp = deviceTimestamp,
                AnswersJson = answersJson,
                OriginalAnswersJson = answersJson,
                OfflineSyncMarkersJson = markersJson,
                ValidationErrorsJson = JsonSerializer.Serialize(new[] { mismatchMessage }),
                Status = "QUARANTINED",
                TriageHistoryJson = "[]",
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.EproSubmissionQuarantines.Add(quarantine);
            db.ClinicalQueries.Add(new ClinicalQuery
            {
                StudyId = studyCode,
                SubjectId = item.SubjectId,
                Domain = "QS",
                TestCode = item.DiaryId,
                Status = "OPEN",
                Explanation = mismatchMessage,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.InteropAuditLogs.Add(new InteropAuditLog
            {
                Timestamp = now,
                UserId = actorId,
                UserRole = actorRole,
                Action = "EPRO_SUBMISSION_QUARANTINED",
                Details = $"Quarantined stale-version submission '{quarantine.Id}' for diary '{item.DiaryId}' ({mismatchMessage}).",
                ChangeReason = changeReason,
            });

            return new ReconcileItemOutcome(
                new EproReconciliationResultDto(
                    Id: quarantine.Id,
                    SubjectId: item.SubjectId,
                    DiaryId: item.DiaryId,
                    Status: "QUARANTINED",
                    Answers: CloneJsonElement(item.Answers),
                    OfflineSyncMarkers: item.OfflineSyncMarkers,
                    DuplicateSequenceIgnored: false,
                    CanonicalObservationsCount: 0),
                null);
        }

        // 3. Item-level validation for active version submissions.
        if (ValidateAnswersAgainstInstrument(instrument.ItemsJson, item.Answers) is { } itemValidationError)
        {
            return new ReconcileItemOutcome(
                null,
                Results.Problem(itemValidationError, statusCode: StatusCodes.Status422UnprocessableEntity));
        }

        // 4. Reconcile against any existing active submission for (SubjectId, DiaryId), including tracked intra-batch items.
        var persistedExisting = await db.EproSubmissions
            .Where(s => s.SubjectId == item.SubjectId && s.DiaryId == item.DiaryId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var existing = persistedExisting
            .Concat(db.EproSubmissions.Local.Where(s => s.SubjectId == item.SubjectId && s.DiaryId == item.DiaryId))
            .DistinctBy(s => s.Id, StringComparer.Ordinal)
            .OrderByDescending(s => s.ServerTimestamp)
            .FirstOrDefault();

        string strategy = NormalizeConflictStrategy(item.OfflineSyncMarkers?.ConflictStrategy);
        string resolvedStatus;
        string submissionId;
        JsonElement resolvedAnswers;

        if (existing is null)
        {
            var created = new EproSubmission
            {
                SubjectId = item.SubjectId,
                DiaryId = item.DiaryId,
                DeviceTimestamp = deviceTimestamp,
                ServerTimestamp = now,
                AnswersJson = answersJson,
                OfflineSyncMarkersJson = markersJson,
                SyncStatus = "CREATED",
                CreatedAt = now,
                CreatedBy = actorId,
                ReasonForChange = changeReason,
                VersionIndex = submittedVersion,
            };

            db.EproSubmissions.Add(created);
            resolvedStatus = "CREATED";
            submissionId = created.Id;
            resolvedAnswers = CloneJsonElement(item.Answers);
        }
        else if (strategy == "SERVER_WINS")
        {
            db.EproSubmissionDefeateds.Add(new EproSubmissionDefeated
            {
                SubjectId = item.SubjectId,
                DiaryId = item.DiaryId,
                DeviceTimestamp = deviceTimestamp,
                AnswersJson = answersJson,
                OfflineSyncMarkersJson = markersJson,
                Status = "DEFEATED_BY_SERVER_WINS",
                CreatedAt = now,
            });

            resolvedStatus = "IGNORED_SERVER_WINS";
            submissionId = existing.Id;
            resolvedAnswers = ParseJsonElementOrEmptyObject(existing.AnswersJson);

            db.InteropAuditLogs.Add(new InteropAuditLog
            {
                Timestamp = now,
                UserId = actorId,
                UserRole = actorRole,
                Action = "EPRO_CONFLICT_SERVER_WINS",
                Details = $"Archived client conflict payload for diary '{item.DiaryId}'; preserved server submission '{existing.Id}'.",
                ChangeReason = changeReason,
            });

            return new ReconcileItemOutcome(
                new EproReconciliationResultDto(
                    Id: submissionId,
                    SubjectId: item.SubjectId,
                    DiaryId: item.DiaryId,
                    Status: resolvedStatus,
                    Answers: resolvedAnswers,
                    OfflineSyncMarkers: item.OfflineSyncMarkers,
                    DuplicateSequenceIgnored: false,
                    CanonicalObservationsCount: 0),
                null);
        }
        else if (strategy == "MERGE")
        {
            db.EproSubmissionDefeateds.Add(new EproSubmissionDefeated
            {
                SubjectId = existing.SubjectId,
                DiaryId = existing.DiaryId,
                DeviceTimestamp = existing.DeviceTimestamp,
                AnswersJson = existing.AnswersJson,
                OfflineSyncMarkersJson = existing.OfflineSyncMarkersJson,
                Status = "DEFEATED_BY_MERGE",
                CreatedAt = now,
            });

            resolvedAnswers = MergeAnswerObjects(existing.AnswersJson, item.Answers);
            existing.AnswersJson = resolvedAnswers.GetRawText();
            existing.OfflineSyncMarkersJson = markersJson;
            existing.DeviceTimestamp = deviceTimestamp;
            existing.ServerTimestamp = now;
            existing.SyncStatus = "MERGED";
            existing.ReasonForChange = changeReason;
            existing.VersionIndex = submittedVersion;

            resolvedStatus = "MERGED";
            submissionId = existing.Id;
        }
        else
        {
            // CLIENT_WINS default strategy: archive superseded server record into EproSubmissionDefeated.
            db.EproSubmissionDefeateds.Add(new EproSubmissionDefeated
            {
                SubjectId = existing.SubjectId,
                DiaryId = existing.DiaryId,
                DeviceTimestamp = existing.DeviceTimestamp,
                AnswersJson = existing.AnswersJson,
                OfflineSyncMarkersJson = existing.OfflineSyncMarkersJson,
                Status = "DEFEATED_BY_CLIENT_WINS",
                CreatedAt = now,
            });

            existing.AnswersJson = answersJson;
            existing.OfflineSyncMarkersJson = markersJson;
            existing.DeviceTimestamp = deviceTimestamp;
            existing.ServerTimestamp = now;
            existing.SyncStatus = "UPDATED_CLIENT_WINS";
            existing.ReasonForChange = changeReason;
            existing.VersionIndex = submittedVersion;

            resolvedStatus = "UPDATED_CLIENT_WINS";
            submissionId = existing.Id;
            resolvedAnswers = CloneJsonElement(item.Answers);
        }

        // 5. Mark matching active SubjectAssignment completed in InteropDbContext when a diary resolves.
        var matchingAssignments = await db.SubjectAssignments
            .Where(a => a.SubjectId == item.SubjectId && a.InstrumentId == item.DiaryId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var assignment in matchingAssignments)
        {
            assignment.VersionIndex = Math.Max(2, assignment.VersionIndex + 1);
            assignment.ReasonForChange = changeReason;
        }

        // 6. Structural missing-required-item check -> open ClinicalQuery if optional/blank required concepts exist.
        OpenMissingDataQueriesIfNeeded(db, studyCode, item.SubjectId, item.DiaryId, instrument.ItemsJson, resolvedAnswers, now);

        // 7. Forward resolved diary answers into canonical EDC/Kernel observations per K-TRUTH-1.
        var receipt = await forwarder.ForwardAsync(
            new CanonicalObservationForwardingContext(
                SubmissionId: submissionId,
                SubjectCode: item.SubjectId,
                StudyCode: studyCode,
                DiaryId: item.DiaryId,
                VersionIndex: submittedVersion,
                ObservedAt: deviceTimestamp,
                ResolvedAnswers: resolvedAnswers,
                InstrumentItemsJson: instrument.ItemsJson,
                EnrollmentRef: item.EnrollmentRef,
                TenantCode: tenantCode,
                ActorUserId: actorId,
                ActorRoles: actorRoles,
                ChangeReason: changeReason),
            ct).ConfigureAwait(false);

        db.InteropAuditLogs.Add(new InteropAuditLog
        {
            Timestamp = now,
            UserId = actorId,
            UserRole = actorRole,
            Action = $"EPRO_{resolvedStatus}",
            Details = $"Reconciled diary '{item.DiaryId}' ({resolvedStatus}) for participant '{item.SubjectId}' with {receipt.Envelopes.Count.ToString(CultureInfo.InvariantCulture)} canonical observation(s).",
            ChangeReason = changeReason,
        });

        return new ReconcileItemOutcome(
            new EproReconciliationResultDto(
                Id: submissionId,
                SubjectId: item.SubjectId,
                DiaryId: item.DiaryId,
                Status: resolvedStatus,
                Answers: resolvedAnswers,
                OfflineSyncMarkers: item.OfflineSyncMarkers,
                DuplicateSequenceIgnored: false,
                CanonicalObservationsCount: receipt.Envelopes.Count),
            null);
    }

    private const string CachedResolvedSubjectCodeKey = "Interop.ResolvedSubjectCode";

    private static async Task<IResult?> EnforceSubjectOwnershipAsync(
        HttpContext httpContext,
        string? targetSubjectId,
        CancellationToken ct)
    {
        var (isRestricted, callerSubjectCode) = await ResolveSubjectRestrictionAsync(httpContext, ct).ConfigureAwait(false);
        if (!isRestricted)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(callerSubjectCode)
            || string.IsNullOrWhiteSpace(targetSubjectId)
            || !string.Equals(callerSubjectCode, targetSubjectId, StringComparison.Ordinal))
        {
            return ForbiddenCrossSubjectResult();
        }

        return null;
    }

    private static async Task<IResult?> EnforceSubjectBatchOwnershipAsync(
        HttpContext httpContext,
        IReadOnlyList<EproSubmitRequest> submissions,
        CancellationToken ct)
    {
        var (isRestricted, callerSubjectCode) = await ResolveSubjectRestrictionAsync(httpContext, ct).ConfigureAwait(false);
        if (!isRestricted)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(callerSubjectCode))
        {
            return ForbiddenCrossSubjectResult();
        }

        if (submissions.Select(sub => sub.SubjectId).Any(subjectId =>
                string.IsNullOrWhiteSpace(subjectId)
                || !string.Equals(callerSubjectCode, subjectId, StringComparison.Ordinal)))
        {
            return ForbiddenCrossSubjectResult();
        }

        return null;
    }

    private static async Task<(bool IsRestricted, string? CallerSubjectCode)> ResolveSubjectRestrictionAsync(
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!TryGetSubjectRestrictedCallerCode(httpContext, out string? callerSubjectCode))
        {
            return (false, null);
        }

        if (!string.IsNullOrWhiteSpace(callerSubjectCode))
        {
            return (true, callerSubjectCode);
        }

        if (httpContext.Items.TryGetValue(CachedResolvedSubjectCodeKey, out object? cachedObj))
        {
            return (true, cachedObj as string);
        }

        var resolver = httpContext.RequestServices.GetService<IActivationBindingResolver>();
        if (resolver is not null)
        {
            var (actorUserId, _, actorRoles) = GetCallerContext(httpContext);
            string tenantCode = ResolveCallerTenantCode(httpContext);
            string studyCode = ResolveCallerStudyCode(httpContext);
            string? resolved = await resolver.ResolveSubjectCodeAsync(
                actorUserId,
                actorRoles,
                tenantCode,
                string.IsNullOrWhiteSpace(studyCode) ? [] : [studyCode],
                ct).ConfigureAwait(false);

            httpContext.Items[CachedResolvedSubjectCodeKey] = resolved;
            return (true, resolved);
        }

        return (true, null);
    }

    private static bool TryGetSubjectRestrictedCallerCode(HttpContext httpContext, out string? callerSubjectCode)
    {
        if (httpContext.Items.TryGetValue("Principal", out object? rawPrincipal) && rawPrincipal is Principal principal)
        {
            bool hasPrivilegedRole = principal.Roles.Any(PrivilegedClinicalRoles.Contains);
            bool hasSubjectRole = principal.Roles.Contains(RoleNormalization.ROLE_SUBJECT);
            if (hasSubjectRole && !hasPrivilegedRole)
            {
                callerSubjectCode = principal.SubjectCode
                    ?? httpContext.User.FindFirst(CadenceClaimTypes.SubjectCode)?.Value;
                return true;
            }

            callerSubjectCode = null;
            return false;
        }

        var rawRoles = httpContext.User.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .Concat(httpContext.User.FindAll("role").Select(c => c.Value))
            .ToArray();
        var normalized = RoleNormalization.GetNormalizedRoles(rawRoles);
        if (normalized.Contains(RoleNormalization.ROLE_SUBJECT) && !normalized.Any(PrivilegedClinicalRoles.Contains))
        {
            callerSubjectCode = httpContext.User.FindFirst(CadenceClaimTypes.SubjectCode)?.Value;
            return true;
        }

        callerSubjectCode = null;
        return false;
    }

    private static IResult ForbiddenCrossSubjectResult() =>
        Results.Problem(
            "Subjects may only read or mutate their own assigned SubjectCode.",
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult MissingChangeReasonResult() =>
        Results.Problem(
            "A valid non-empty X-Change-Reason header is required for ePRO mutations.",
            statusCode: StatusCodes.Status400BadRequest);

    private static string? ResolveChangeReason(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue("Principal", out object? rawPrincipal)
            && rawPrincipal is Principal principal
            && !string.IsNullOrWhiteSpace(principal.ChangeReason))
        {
            return principal.ChangeReason.Trim();
        }

        string? headerReason = httpContext.Request.Headers[GatewayHeaders.ChangeReason].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerReason))
        {
            return ChangeReasonHeader.TryDecode(headerReason, out string? decoded) && !string.IsNullOrWhiteSpace(decoded)
                ? decoded.Trim()
                : headerReason.Trim();
        }

        return null;
    }

    private static (string UserId, string PrimaryRole, IReadOnlyList<string> Roles) GetCallerContext(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue("Principal", out object? rawPrincipal) && rawPrincipal is Principal principal)
        {
            var rolesList = principal.Roles.OrderBy(r => r, StringComparer.Ordinal).ToList();
            return (
                string.IsNullOrWhiteSpace(principal.UserId) ? "svc:interop-epro" : principal.UserId,
                rolesList.FirstOrDefault() ?? RoleNormalization.ROLE_SUBJECT,
                rolesList);
        }

        string userId = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "svc:interop-epro";
        var roles = httpContext.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return (userId, roles.FirstOrDefault() ?? RoleNormalization.ROLE_SUBJECT, roles);
    }

    private static string ResolveCallerStudyCode(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue("Principal", out object? rawPrincipal)
            && rawPrincipal is Principal principal
            && principal.AssignedStudies.FirstOrDefault(s => !string.Equals(s, "*", StringComparison.Ordinal)) is { } study)
        {
            return study;
        }

        return httpContext.User.FindAll(CadenceClaimTypes.StudyIds)
            .Select(c => c.Value)
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, "*", StringComparison.Ordinal))
            ?? "CADENCE-101";
    }

    private static string ResolveCallerTenantCode(HttpContext httpContext)
    {
        string? claimTenant = httpContext.User.FindFirst(CadenceClaimTypes.TenantId)?.Value;
        if (!string.IsNullOrWhiteSpace(claimTenant))
        {
            return claimTenant;
        }

        string? headerTenant = httpContext.Request.Headers[GatewayHeaders.TenantId].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(headerTenant) ? headerTenant : "cadence-local";
    }

    private static async Task<Dictionary<string, EproSubmission>> LoadLatestSubmissionsByDiaryAsync(
        InteropDbContext db,
        string subjectId,
        CancellationToken ct)
    {
        var submissions = await db.EproSubmissions.AsNoTracking()
            .Where(s => s.SubjectId == subjectId && s.SyncStatus != "QUARANTINED")
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dict = new Dictionary<string, EproSubmission>(StringComparer.Ordinal);
        foreach (var sub in submissions.OrderByDescending(s => s.ServerTimestamp))
        {
            dict.TryAdd(sub.DiaryId, sub);
        }

        return dict;
    }

    private static EproAssignmentDto MapAssignmentDto(
        SubjectAssignment assignment,
        IReadOnlyDictionary<string, Instrument> instruments,
        IReadOnlyDictionary<string, EproSubmission> latestSubmissions,
        DateTimeOffset now)
    {
        instruments.TryGetValue(assignment.InstrumentId, out var instrument);
        bool hasSubmission = latestSubmissions.TryGetValue(assignment.InstrumentId, out var submission)
            && submission.DeviceTimestamp >= assignment.StartDate.AddDays(-7);
        bool isCompleted = hasSubmission || assignment.VersionIndex > 1;
        bool isOverdue = !isCompleted && assignment.DueAt is { } due && due < now;

        string status;
        if (isCompleted)
        {
            status = "COMPLETED";
        }
        else if (isOverdue)
        {
            status = "OVERDUE";
        }
        else
        {
            status = "PENDING";
        }

        return new EproAssignmentDto(
            assignment.Id,
            assignment.SubjectId,
            assignment.InstrumentId,
            instrument?.Name ?? assignment.InstrumentId,
            assignment.StartDate,
            assignment.EndDate,
            assignment.RecurrencePattern,
            assignment.DueAt,
            hasSubmission ? submission!.DeviceTimestamp : null,
            status,
            assignment.VersionIndex);
    }

    private static EproInstrumentDto MapInstrumentDto(Instrument instrument) =>
        new(
            instrument.Id,
            instrument.Name,
            instrument.Description,
            ParseJsonElementOrEmptyObject(instrument.ItemsJson),
            ParseJsonElementOrEmptyObject(instrument.ResponseTypesJson),
            ParseJsonElementOrEmptyObject(instrument.ScoringMetadataJson),
            instrument.VersionIndex);

    private static EproNotificationDto MapNotificationDto(SubjectNotification notification)
    {
        string message = !string.IsNullOrWhiteSpace(notification.ReasonForChange)
            ? notification.ReasonForChange
            : $"Scheduled trial survey reminder (channel: {notification.Channel})";

        return new EproNotificationDto(
            notification.Id,
            notification.SubjectId,
            notification.AssignmentId,
            message,
            message,
            notification.DueAt,
            string.IsNullOrWhiteSpace(notification.Channel) ? "IN_APP" : notification.Channel,
            notification.DeliveryStatus,
            notification.IsRead,
            notification.ReadAt,
            notification.VersionIndex);
    }

    private static bool TryBuildSequenceKey(
        string subjectId,
        EproOfflineSyncMarkersDto? markers,
        out string key,
        out string clientId,
        out long sequenceNumber)
    {
        if (markers is not null
            && !string.IsNullOrWhiteSpace(markers.ClientId)
            && markers.SequenceNumber is { } seq)
        {
            clientId = markers.ClientId.Trim();
            sequenceNumber = seq;
            key = $"{subjectId}::{clientId}::{seq.ToString(CultureInfo.InvariantCulture)}";
            return true;
        }

        key = string.Empty;
        clientId = string.Empty;
        sequenceNumber = 0;
        return false;
    }

    private static async Task<EproSubmission?> FindSubmissionBySequenceMarkerAsync(
        InteropDbContext db,
        string subjectId,
        string clientId,
        long sequenceNumber,
        CancellationToken ct)
    {
        var candidates = await db.EproSubmissions.AsNoTracking()
            .Where(s => s.SubjectId == subjectId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return candidates
            .Concat(db.EproSubmissions.Local.Where(s => s.SubjectId == subjectId))
            .DistinctBy(s => s.Id, StringComparer.Ordinal)
            .FirstOrDefault(candidate => MatchesSequenceMarker(candidate.OfflineSyncMarkersJson, clientId, sequenceNumber));
    }

    private static bool MatchesSequenceMarker(string markersJson, string clientId, long sequenceNumber)
    {
        if (string.IsNullOrWhiteSpace(markersJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(markersJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string? cid = doc.RootElement.TryGetProperty("client_id", out var cidEl) && cidEl.ValueKind == JsonValueKind.String
                ? cidEl.GetString()
                : null;
            long? seq = doc.RootElement.TryGetProperty("sequence_number", out var seqEl) && seqEl.TryGetInt64(out long parsedSeq)
                ? parsedSeq
                : null;

            return string.Equals(cid, clientId, StringComparison.Ordinal) && seq == sequenceNumber;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ValidateAnswersAgainstInstrument(string? itemsJson, JsonElement answers)
    {
        if (string.IsNullOrWhiteSpace(itemsJson) || answers.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(itemsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var itemProp in doc.RootElement.EnumerateObject())
            {
                if (itemProp.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!answers.TryGetProperty(itemProp.Name, out var answerVal)
                    || answerVal.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    continue;
                }

                string? rawString = answerVal.ValueKind == JsonValueKind.String ? answerVal.GetString()?.Trim() : null;
                if (answerVal.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(rawString))
                {
                    continue;
                }

                string? itemType = itemProp.Value.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                    ? typeEl.GetString()
                    : null;

                if (itemType is "numeric" or "number" or "visual_analog" or "vas" or "slider" or "range")
                {
                    decimal numericVal;
                    if (answerVal.ValueKind == JsonValueKind.Number && answerVal.TryGetDecimal(out decimal n))
                    {
                        numericVal = n;
                    }
                    else if (rawString is not null
                             && decimal.TryParse(rawString, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
                    {
                        numericVal = parsed;
                    }
                    else
                    {
                        return $"Field '{itemProp.Name}' must be a valid number.";
                    }

                    if (itemProp.Value.TryGetProperty("min", out var minEl)
                        && minEl.TryGetDecimal(out decimal min)
                        && numericVal < min)
                    {
                        return $"Field '{itemProp.Name}' value {numericVal.ToString(CultureInfo.InvariantCulture)} is out of range (min {min.ToString(CultureInfo.InvariantCulture)}).";
                    }

                    if (itemProp.Value.TryGetProperty("max", out var maxEl)
                        && maxEl.TryGetDecimal(out decimal max)
                        && numericVal > max)
                    {
                        return $"Field '{itemProp.Name}' value {numericVal.ToString(CultureInfo.InvariantCulture)} is out of range (max {max.ToString(CultureInfo.InvariantCulture)}).";
                    }
                }
                else if (itemType == "choice_single"
                         && itemProp.Value.TryGetProperty("options", out var optionsEl)
                         && optionsEl.ValueKind == JsonValueKind.Array
                         && rawString is not null)
                {
                    var allowed = optionsEl.EnumerateArray()
                        .Where(o => o.ValueKind == JsonValueKind.String)
                        .Select(o => o.GetString()!)
                        .ToList();

                    if (allowed.Count > 0 && !allowed.Contains(rawString, StringComparer.Ordinal))
                    {
                        return $"Field '{itemProp.Name}' value '{rawString}' is not a permitted option.";
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static void OpenMissingDataQueriesIfNeeded(
        InteropDbContext db,
        string studyCode,
        string subjectId,
        string diaryId,
        string? itemsJson,
        JsonElement resolvedAnswers,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(itemsJson) || resolvedAnswers.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(itemsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var itemProp in doc.RootElement.EnumerateObject())
            {
                bool isRequired = itemProp.Value.ValueKind == JsonValueKind.Object
                    && itemProp.Value.TryGetProperty("required", out var reqEl)
                    && reqEl.ValueKind == JsonValueKind.True;

                if (!isRequired)
                {
                    continue;
                }

                bool hasAnswer = resolvedAnswers.TryGetProperty(itemProp.Name, out var val)
                    && val.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                    && !(val.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(val.GetString()));

                if (!hasAnswer)
                {
                    db.ClinicalQueries.Add(new ClinicalQuery
                    {
                        StudyId = studyCode,
                        SubjectId = subjectId,
                        Domain = "QS",
                        TestCode = $"{diaryId}.{itemProp.Name}",
                        Status = "OPEN",
                        Explanation = $"Required diary item '{itemProp.Name}' was omitted in ePRO submission.",
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed item definitions.
        }
    }

    private static string NormalizeConflictStrategy(string? strategy) =>
        (strategy ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "SERVER_WINS" => "SERVER_WINS",
            "MERGE" => "MERGE",
            _ => "CLIENT_WINS",
        };

    private static JsonElement MergeAnswerObjects(string existingJson, JsonElement incoming)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var existingElement = ParseJsonElementOrEmptyObject(existingJson);
        if (existingElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in existingElement.EnumerateObject())
            {
                merged[prop.Name] = prop.Value.Clone();
            }
        }

        if (incoming.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in incoming.EnumerateObject())
            {
                bool isBlank = prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    || (prop.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(prop.Value.GetString()));
                if (!isBlank || !merged.ContainsKey(prop.Name))
                {
                    merged[prop.Name] = prop.Value.Clone();
                }
            }
        }

        string serialized = JsonSerializer.Serialize(merged);
        using var doc = JsonDocument.Parse(serialized);
        return doc.RootElement.Clone();
    }

    private static string SerializeMarkers(EproOfflineSyncMarkersDto? markers) =>
        markers is null ? "{}" : JsonSerializer.Serialize(markers);

    private static JsonElement ParseJsonElementOrEmptyObject(string? json)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Fall back to empty JSON object below.
            }
        }

        using var fallback = JsonDocument.Parse("{}");
        return fallback.RootElement.Clone();
    }

    private static JsonElement CloneJsonElement(JsonElement element)
    {
        using var doc = JsonDocument.Parse(element.GetRawText());
        return doc.RootElement.Clone();
    }

    private sealed record ReconcileItemOutcome(
        EproReconciliationResultDto? Result,
        IResult? ValidationFailure);
}

public sealed record EproAssignmentDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("instrument_id")] string InstrumentId,
    [property: JsonPropertyName("instrument_name")] string InstrumentName,
    [property: JsonPropertyName("start_date")] DateTimeOffset StartDate,
    [property: JsonPropertyName("end_date")] DateTimeOffset EndDate,
    [property: JsonPropertyName("recurrence_pattern")] string? RecurrencePattern,
    [property: JsonPropertyName("due_at")] DateTimeOffset? DueAt,
    [property: JsonPropertyName("submitted_at")] DateTimeOffset? SubmittedAt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version_index")] int VersionIndex);

public sealed record EproInstrumentDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("items")] JsonElement Items,
    [property: JsonPropertyName("response_types")] JsonElement ResponseTypes,
    [property: JsonPropertyName("scoring_metadata")] JsonElement ScoringMetadata,
    [property: JsonPropertyName("version_index")] int VersionIndex);

public sealed record EproComplianceAssignmentItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("instrument_id")] string InstrumentId,
    [property: JsonPropertyName("instrument_name")] string InstrumentName,
    [property: JsonPropertyName("due_at")] DateTimeOffset? DueAt,
    [property: JsonPropertyName("end_date")] DateTimeOffset EndDate,
    [property: JsonPropertyName("submitted_at")] DateTimeOffset? SubmittedAt,
    [property: JsonPropertyName("status")] string Status);

public sealed record EproComplianceResponse(
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("compliance_rate")] double ComplianceRate,
    [property: JsonPropertyName("completed_count")] int CompletedCount,
    [property: JsonPropertyName("pending_count")] int PendingCount,
    [property: JsonPropertyName("overdue_count")] int OverdueCount,
    [property: JsonPropertyName("total_count")] int TotalCount,
    [property: JsonPropertyName("assignments")] IReadOnlyList<EproComplianceAssignmentItem> Assignments);

public sealed record EproNotificationDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("assignment_id")] string? AssignmentId,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("message_content")] string MessageContent,
    [property: JsonPropertyName("due_at")] DateTimeOffset DueAt,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("delivery_status")] string DeliveryStatus,
    [property: JsonPropertyName("is_read")] bool IsRead,
    [property: JsonPropertyName("read_at")] DateTimeOffset? ReadAt,
    [property: JsonPropertyName("version_index")] int VersionIndex);

public sealed record EproAcknowledgeNotificationRequest(
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange = null,
    [property: JsonPropertyName("subject_id")] string? SubjectId = null);

public sealed record EproOfflineSyncMarkersDto(
    [property: JsonPropertyName("sequence_number")] long? SequenceNumber = null,
    [property: JsonPropertyName("client_id")] string? ClientId = null,
    [property: JsonPropertyName("conflict_strategy")] string? ConflictStrategy = null);

public sealed record EproSubmitRequest(
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("diary_id")] string DiaryId,
    [property: JsonPropertyName("version_index")] int? VersionIndex,
    [property: JsonPropertyName("device_timestamp")] DateTimeOffset? DeviceTimestamp,
    [property: JsonPropertyName("answers")] JsonElement Answers,
    [property: JsonPropertyName("offline_sync_markers")] EproOfflineSyncMarkersDto? OfflineSyncMarkers = null,
    [property: JsonPropertyName("enrollment_ref")] string? EnrollmentRef = null);

public sealed record EproSyncBatchRequest(
    [property: JsonPropertyName("submissions")] IReadOnlyList<EproSubmitRequest> Submissions);

public sealed record EproReconciliationResultDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("diary_id")] string DiaryId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("answers")] JsonElement Answers,
    [property: JsonPropertyName("offline_sync_markers")] EproOfflineSyncMarkersDto? OfflineSyncMarkers,
    [property: JsonPropertyName("duplicate_sequence_ignored")] bool DuplicateSequenceIgnored = false,
    [property: JsonPropertyName("canonical_observations_count")] int CanonicalObservationsCount = 0);

public sealed record EproSyncBatchResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("results")] IReadOnlyList<EproReconciliationResultDto> Results);
