using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Database.Temporal;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Ctms.Application.Services;

public interface IDoaQueryAndExportService
{
    Task<IResult> GetSiteDoaLogAsync(
        string siteId,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null);

    Task<IResult> ExportSiteDoaPdfAsync(
        string siteId,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null);
}

public sealed class DoaQueryAndExportService(
    CtmsDbContext db,
    ICtmsUnitOfWork unitOfWork,
    ICtmsSiteScopeService siteScope,
    IClock clock) : IDoaQueryAndExportService
{
    public async Task<IResult> GetSiteDoaLogAsync(
        string siteId,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null,
        DateTimeOffset? validAt = null,
        DateTimeOffset? knownAt = null)
    {
        var scope = await siteScope.ResolveDoaSiteScopeAsync(httpContext, siteId, studyId, ct);
        if (scope.Error is not null)
        {
            return scope.Error;
        }

        string trimmedSite = siteId.Trim();
        var authorizedStudies = scope.AuthorizedStudies;

        List<DelegationResponseDto> delegatedStaff;
        if (validAt.HasValue || knownAt.HasValue)
        {
            DateTimeOffset effectiveValidAt = validAt ?? knownAt!.Value;
            DateTimeOffset effectiveKnownAt = knownAt ?? validAt!.Value;
            var periods = (await db.CtmsDelegationPeriods.AsNoTracking()
                .Where(p => p.SiteCode == trimmedSite && authorizedStudies.Contains(p.StudyCode))
                .QueryAsOfAsync(db, effectiveValidAt, effectiveKnownAt, ct))
                .OrderBy(p => p.StartDate, StringComparer.Ordinal)
                .ToList();

            var versionById = await db.CtmsDelegations.AsNoTracking()
                .Where(d => d.SiteId == trimmedSite && authorizedStudies.Contains(d.StudyId))
                .ToDictionaryAsync(d => d.Id, d => d.VersionIndex, StringComparer.Ordinal, ct);

            delegatedStaff = periods
                .Select(p => new DelegationResponseDto(
                    RecordId: p.DelegationId,
                    Site: p.SiteCode,
                    StaffUserId: p.StaffUserId,
                    TaskCodes: DoaMutationService.ParseTaskCodes(p.TaskCodes),
                    StartDate: p.StartDate,
                    EndDate: p.EndDate,
                    IsActive: p.IsActive,
                    SignedOff: p.SignedOff,
                    ReasonForChange: p.ReasonForChange,
                    CreatedBy: p.CreatedBy,
                    VersionIndex: versionById.GetValueOrDefault(p.DelegationId, 1L)))
                .ToList();
        }
        else
        {
            var delegations = await db.CtmsDelegations.AsNoTracking()
                .Where(d => d.SiteId == trimmedSite && authorizedStudies.Contains(d.StudyId))
                .OrderBy(d => d.StartDate)
                .ToListAsync(ct);
            delegatedStaff = delegations.Select(DoaMutationService.ToResponseDto).ToList();
        }

        var audits = await db.CtmsAuditLogs.AsNoTracking()
            .Where(a => a.SiteId == trimmedSite
                && a.StudyId != null
                && authorizedStudies.Contains(a.StudyId)
                && (a.Action.StartsWith("DOA_") || a.Action.Contains("DELEGAT")))
            .Where(a => !knownAt.HasValue || a.Timestamp <= knownAt.Value)
            .OrderBy(a => a.Timestamp)
            .ToListAsync(ct);

        var gate = await db.CtmsSiteGreenlightGates.AsNoTracking()
            .Where(g => g.SiteId == trimmedSite && authorizedStudies.Contains(g.StudyId))
            .OrderBy(g => g.StudyId)
            .FirstOrDefaultAsync(ct);

        string piName = !string.IsNullOrWhiteSpace(gate?.GreenlightCertifiedBy)
            ? gate.GreenlightCertifiedBy
            : "Principal Investigator";

        var response = new DoaLogResponseDto(
            Site: trimmedSite,
            PiName: piName,
            DelegatedStaff: delegatedStaff,
            AuditHistory: audits.Select(ToAuditDto).ToList());

        return TypedResults.Ok(response);
    }

    public async Task<IResult> ExportSiteDoaPdfAsync(
        string siteId,
        HttpContext httpContext,
        CancellationToken ct,
        string? studyId = null)
    {
        var scope = await siteScope.ResolveDoaSiteScopeAsync(httpContext, siteId, studyId, ct);
        if (scope.Error is not null)
        {
            return scope.Error;
        }

        string trimmedSite = siteId.Trim();
        var authorizedStudies = scope.AuthorizedStudies;

        var delegations = await db.CtmsDelegations.AsNoTracking()
            .Where(d => d.SiteId == trimmedSite && authorizedStudies.Contains(d.StudyId))
            .OrderBy(d => d.StartDate)
            .ToListAsync(ct);

        var audits = await db.CtmsAuditLogs.AsNoTracking()
            .Where(a => a.SiteId == trimmedSite
                && a.StudyId != null
                && authorizedStudies.Contains(a.StudyId)
                && (a.Action.StartsWith("DOA_") || a.Action.Contains("DELEGAT")))
            .OrderBy(a => a.Timestamp)
            .ToListAsync(ct);

        var gate = await db.CtmsSiteGreenlightGates.AsNoTracking()
            .Where(g => g.SiteId == trimmedSite && authorizedStudies.Contains(g.StudyId))
            .OrderBy(g => g.StudyId)
            .FirstOrDefaultAsync(ct);

        string piName = !string.IsNullOrWhiteSpace(gate?.GreenlightCertifiedBy)
            ? gate.GreenlightCertifiedBy
            : "Principal Investigator";

        var (user, role) = CallerScope.ResolveActor(httpContext, "investigator");
        DateTimeOffset now = clock.UtcNow;

        var pdfBuilder = new StringBuilder();
        pdfBuilder.AppendLine("%PDF-1.7");
        pdfBuilder.AppendLine("% Cadence Clinical GxP PDF Document - Delegation of Authority Log");
        pdfBuilder.AppendLine($"% Site: {siteId} | Principal Investigator: {piName}");
        pdfBuilder.AppendLine($"% Generated: {now:O} | Exported By: {user} ({role})");
        pdfBuilder.AppendLine();
        pdfBuilder.AppendLine("================================================================================");
        pdfBuilder.AppendLine("               DELEGATION OF AUTHORITY (DOA) COMPLIANCE LOG                     ");
        pdfBuilder.AppendLine("================================================================================");
        pdfBuilder.AppendLine($"Site ID: {siteId}");
        pdfBuilder.AppendLine($"Principal Investigator: {piName}");
        pdfBuilder.AppendLine($"Export Timestamp: {now:u}");
        pdfBuilder.AppendLine($"Total Delegations: {delegations.Count}");
        pdfBuilder.AppendLine("--------------------------------------------------------------------------------");
        pdfBuilder.AppendLine("DELEGATED SITE PERSONNEL:");
        foreach (var d in delegations)
        {
            var taskList = DoaMutationService.ParseTaskCodes(d.TaskCodes);
            pdfBuilder.AppendLine($"- Staff User ID : {d.StaffUserId}");
            pdfBuilder.AppendLine($"  Record ID     : {d.Id}");
            pdfBuilder.AppendLine($"  Start Date    : {d.StartDate}");
            pdfBuilder.AppendLine($"  End Date      : {d.EndDate ?? "N/A (Active)"}");
            pdfBuilder.AppendLine($"  Active Status : {(d.IsActive ? "ACTIVE" : "REVOKED")}");
            pdfBuilder.AppendLine($"  PI Signed Off : {(d.SignedOff ? "YES" : "PENDING")}");
            pdfBuilder.AppendLine($"  Task Codes    : {string.Join(", ", taskList)}");
            pdfBuilder.AppendLine($"  Justification : {d.ReasonForChange}");
            pdfBuilder.AppendLine();
        }

        pdfBuilder.AppendLine("--------------------------------------------------------------------------------");
        pdfBuilder.AppendLine("IMMUTABLE AUDIT TRAIL HISTORY:");
        foreach (var a in audits)
        {
            pdfBuilder.AppendLine($"[{a.Timestamp:O}] [{a.Action}] User: {a.UserId} ({a.UserRole}) - {a.Details} (Reason: {a.ReasonForChange})");
        }

        pdfBuilder.AppendLine("================================================================================");
        pdfBuilder.AppendLine("%%EOF");

        byte[] rawPdfBytes = Encoding.UTF8.GetBytes(pdfBuilder.ToString().TrimEnd());

        var (cert, key) = Pkcs7Signer.GenerateSigningCredentials(user, clock);
        var signer = new Pkcs7Signer(cert, key);
        byte[] signedPdfBytes = signer.SignPdf(rawPdfBytes);

        string hash = Convert.ToHexStringLower(SHA256.HashData(signedPdfBytes));

        string? resolvedExportStudyId = null;
        if (authorizedStudies.Count == 1)
        {
            resolvedExportStudyId = authorizedStudies.First();
        }
        else if (!string.IsNullOrWhiteSpace(studyId))
        {
            resolvedExportStudyId = studyId.Trim();
        }

        var auditEntry = new CtmsAuditLog
        {
            Timestamp = now,
            UserId = user,
            UserRole = role,
            Action = "DOA_EXPORT_PDF",
            Details = $"Exported GxP Delegation of Authority PDF for site {trimmedSite} with {delegations.Count} records. SHA-256: {hash}",
            StudyId = resolvedExportStudyId,
            SiteId = trimmedSite,
            ReasonForChange = "Export GxP Delegation of Authority PDF",
            CreatedAt = now.UtcDateTime,
            CreatedBy = user,
            VersionIndex = 1,
        };
        db.CtmsAuditLogs.Add(auditEntry);
        await unitOfWork.CommitAsync(ct);

        return Results.File(signedPdfBytes, "application/pdf", $"DOA_Log_{trimmedSite}.pdf");
    }

    public static DoaAuditHistoryItemDto ToAuditDto(CtmsAuditLog a) =>
        new(
            Id: a.Id,
            Timestamp: a.Timestamp,
            UserId: a.UserId,
            UserRole: a.UserRole,
            Action: a.Action,
            Details: a.Details);
}
