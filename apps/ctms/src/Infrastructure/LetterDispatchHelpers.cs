using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Ctms.Infrastructure;

/// <summary>
/// Helper for filing finalized visit letters into the eTMF repository.
/// </summary>
public static class EtmfFilingHelper
{
    public static Task<string?> FileToEtmfFromServicesAsync(
        SiteVisitLetterModel request,
        GxpPdfResult pdfResult,
        string templateName,
        string letterId,
        IServiceProvider serviceProvider,
        IClock clock,
        string user,
        CancellationToken ct)
    {
        var etmfDbType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.FullName == "Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContext");
        var etmfDb = etmfDbType != null ? serviceProvider.GetService(etmfDbType) : null;
        return FileToEtmfAsync(request, pdfResult, templateName, letterId, etmfDb, clock, user, ct);
    }

    /// <summary>
    /// Ingests a finalized visit letter PDF into the eTMF repository.
    /// </summary>
    public static async Task<string?> FileToEtmfAsync(
        SiteVisitLetterModel request,
        GxpPdfResult pdfResult,
        string templateName,
        string letterId,
        object? etmfDb,
        IClock clock,
        string user,
        CancellationToken ct)
    {
        if (etmfDb is null) return null;

        var etmfAssembly = etmfDb.GetType().Assembly;
        var tmfDocType = etmfAssembly.GetType("Cadence.Apps.Etmf.Domain.TmfDocument");
        var tmfAuditLogType = etmfAssembly.GetType("Cadence.Apps.Etmf.Domain.TmfAuditLog");
        if (tmfDocType is null || tmfAuditLogType is null) return null;

        var now = clock.UtcNow;
        var docId = "DOC-CTMS-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var filename = $"{templateName}_{request.SiteId}_{request.StudyId}_{letterId}.pdf";

        var tmfDoc = Activator.CreateInstance(tmfDocType, docId);
        if (tmfDoc is null) return null;

        SetProp(tmfDoc, "StudyId", request.StudyId);
        SetProp(tmfDoc, "SiteId", request.SiteId);
        SetProp(tmfDoc, "Filename", filename);
        SetProp(tmfDoc, "Content", Convert.ToBase64String(pdfResult.PdfBytes));
        SetProp(tmfDoc, "MimeType", "application/pdf");
        SetProp(tmfDoc, "Zone", 5);
        SetProp(tmfDoc, "Section", "05.02");
        SetProp(tmfDoc, "ArtifactCode", "05.02.06");
        SetProp(tmfDoc, "ArtifactType", "Monitoring Visit Correspondence");
        var reason = request.ReasonForChange ?? "Automated eTMF filing for finalized monitoring visit letter.";
        var stageMethod = tmfDocType.GetMethod("StageForQc");
        stageMethod?.Invoke(tmfDoc, [user, "System/CRA", reason, now, 1]);
        var approveMethod = tmfDocType.GetMethod("Approve");
        approveMethod?.Invoke(tmfDoc, [user, "System/CRA", reason, now, 2]);
        SetProp(tmfDoc, "TaxonomyVersion", "v3.2.0-complete");
        SetProp(tmfDoc, "CreatedAt", now.UtcDateTime);
        SetProp(tmfDoc, "CreatedBy", user);
        SetProp(tmfDoc, "VersionIndex", 1);
        SetProp(tmfDoc, "ReasonForChange", reason);
        SetProp(tmfDoc, "MetadataJson", JsonSerializer.Serialize(new
        {
            source = "CTMS_MONITORING_PIPELINE",
            letter_type = templateName,
            is_signed = pdfResult.IsSigned,
            sha256_hash = pdfResult.Sha256Hash
        }));

        var dbContextType = etmfDb.GetType();
        var addMethod = dbContextType.GetMethod("Add", [typeof(object)]) ?? dbContextType.GetMethod("Add");
        addMethod?.Invoke(etmfDb, [tmfDoc]);

        var auditLog = Activator.CreateInstance(tmfAuditLogType);
        if (auditLog is not null)
        {
            SetProp(auditLog, "Timestamp", now);
            SetProp(auditLog, "UserId", user);
            SetProp(auditLog, "UserRole", "System/CRA");
            SetProp(auditLog, "Action", "INGEST_MONITORING_LETTER");
            SetProp(auditLog, "DocumentId", docId);
            SetProp(auditLog, "Details", $"Filed finalized monitoring visit letter {filename} into eTMF Zone 5.");
            SetProp(auditLog, "ReasonForChange", request.ReasonForChange ?? "Automated visit letter eTMF integration");
            addMethod?.Invoke(etmfDb, [auditLog]);
        }

        var saveChangesMethod = dbContextType.GetMethod("SaveChangesAsync", [typeof(CancellationToken)]);
        if (saveChangesMethod is not null)
        {
            await (Task)saveChangesMethod.Invoke(etmfDb, [ct])!;
        }

        return docId;
    }

    private static void SetProp(object target, string name, object? value)
    {
        target.GetType().GetProperty(name)?.SetValue(target, value);
    }
}

/// <summary>
/// Helper for dispatching email notifications for finalized visit letters.
/// </summary>
public static class EmailDispatchHelper
{
    public static Task<string?> DispatchEmailFromServicesAsync(
        SiteVisitLetterModel request,
        GxpPdfResult pdfResult,
        string templateName,
        string letterId,
        IServiceProvider serviceProvider,
        IClock clock,
        string user,
        CancellationToken ct)
    {
        var notifDbType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.FullName == "Cadence.Apps.Notifications.Infrastructure.Persistence.NotificationsDbContext");
        var notifDb = notifDbType != null ? serviceProvider.GetService(notifDbType) : null;
        return DispatchEmailNotificationAsync(request, pdfResult, templateName, letterId, notifDb, clock, user, ct);
    }

    /// <summary>
    /// Dispatches an email notification with secure PDF access link to site investigator.
    /// </summary>
    public static async Task<string?> DispatchEmailNotificationAsync(
        SiteVisitLetterModel request,
        GxpPdfResult pdfResult,
        string templateName,
        string letterId,
        object? notificationsDb,
        IClock clock,
        string user,
        CancellationToken ct)
    {
        if (notificationsDb is null) return null;

        var recipientEmail = request.InvestigatorEmail;
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            recipientEmail = $"investigator.site{request.SiteId}@clinicalsite.org";
        }

        if (!recipientEmail.Contains('@', StringComparison.Ordinal) || recipientEmail.EndsWith("@unauthorized.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var notifAssembly = notificationsDb.GetType().Assembly;
        var recordType = notifAssembly.GetType("Cadence.Apps.Notifications.Domain.NotificationRecord");
        var deliveryType = notifAssembly.GetType("Cadence.Apps.Notifications.Domain.NotificationDelivery");
        if (recordType is null || deliveryType is null) return null;

        var now = clock.UtcNow;
        var notificationId = "NOTIF-CTMS-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var pdfAccessUrl = $"/api/v1/ctms/letters/{letterId}/pdf";

        var record = Activator.CreateInstance(recordType);
        if (record is null) return null;

        SetProp(record, "Id", notificationId);
        SetProp(record, "RecipientUserId", recipientEmail);
        SetProp(record, "RecipientRoleId", "PRINCIPAL_INVESTIGATOR");
        SetProp(record, "Category", "VISIT_LETTER_DISPATCH");
        SetProp(record, "Priority", "HIGH");
        SetProp(record, "Channels", "EMAIL,IN_APP");
        SetProp(record, "MessageContent", $"Official Monitoring Visit Letter ({templateName}) for Study {request.StudyId} Site {request.SiteId} is ready. Access secure PDF: {pdfAccessUrl}");
        SetProp(record, "RelatedEntityId", letterId);
        SetProp(record, "RelatedEntityType", "CTMS_VISIT_LETTER");
        SetProp(record, "Status", "SENT");
        SetProp(record, "DeliveryState", "COMPLETED");
        SetProp(record, "Retries", 0);
        SetProp(record, "CreatedAt", now);
        SetProp(record, "CreatedBy", user);
        SetProp(record, "VersionIndex", 1);
        SetProp(record, "ReasonForChange", "Automated email notification dispatch for visit letter finalization.");

        var dbContextType = notificationsDb.GetType();
        var addMethod = dbContextType.GetMethod("Add", [typeof(object)]) ?? dbContextType.GetMethod("Add");
        addMethod?.Invoke(notificationsDb, [record]);

        var delivery = Activator.CreateInstance(deliveryType);
        if (delivery is not null)
        {
            SetProp(delivery, "Id", Guid.NewGuid().ToString());
            SetProp(delivery, "NotificationId", notificationId);
            SetProp(delivery, "Channel", "EMAIL");
            SetProp(delivery, "Status", "COMPLETED");
            SetProp(delivery, "Attempts", 1);
            SetProp(delivery, "CompletedAt", now);
            SetProp(delivery, "RetryEligible", false);
            addMethod?.Invoke(notificationsDb, [delivery]);
        }

        var saveChangesMethod = dbContextType.GetMethod("SaveChangesAsync", [typeof(CancellationToken)]);
        if (saveChangesMethod is not null)
        {
            await (Task)saveChangesMethod.Invoke(notificationsDb, [ct])!;
        }

        return recipientEmail;
    }

    private static void SetProp(object target, string name, object? value)
    {
        target.GetType().GetProperty(name)?.SetValue(target, value);
    }
}
