using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Kernel;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Kernel.Tenancy;
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
    IClock clock,
    ICtmsKernelTrustClient? kernelTrust = null) : IDoaQueryAndExportService
{
    private readonly ICtmsKernelTrustClient _kernelTrust = kernelTrust ?? new InMemoryCtmsKernelTrustClient(clock);

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
        var (_, role) = CallerScope.ResolveActor(httpContext, "investigator");
        var (tenantId, tenantCode, _) = await DoaMutationService.ResolveCallerContextAsync(httpContext, role, ct);

        var delegatedStaff = await ResolveSiteDelegationsAsync(
            tenantId,
            tenantCode,
            trimmedSite,
            authorizedStudies,
            validAt,
            knownAt,
            ct);

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
            Site: new KernelSiteCode(trimmedSite),
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
        var (user, role) = CallerScope.ResolveActor(httpContext, "investigator");
        var (tenantId, tenantCode, _) = await DoaMutationService.ResolveCallerContextAsync(httpContext, role, ct);

        var delegations = await ResolveSiteDelegationsAsync(
            tenantId,
            tenantCode,
            trimmedSite,
            authorizedStudies,
            validAt: null,
            knownAt: null,
            ct);

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
            pdfBuilder.AppendLine($"- Staff User ID : {d.StaffUserId}");
            pdfBuilder.AppendLine($"  Record ID     : {d.RecordId}");
            if (d.KernelGrantId is { } kernelGrantId)
            {
                pdfBuilder.AppendLine($"  Kernel Grant  : grant:{kernelGrantId:D}");
            }

            pdfBuilder.AppendLine($"  Start Date    : {d.StartDate}");
            pdfBuilder.AppendLine($"  End Date      : {d.EndDate ?? "N/A (Active)"}");
            pdfBuilder.AppendLine($"  Active Status : {(d.IsActive ? "ACTIVE" : "REVOKED")}");
            pdfBuilder.AppendLine($"  PI Signed Off : {(d.SignedOff ? "YES" : "PENDING")}");
            pdfBuilder.AppendLine($"  Task Codes    : {string.Join(", ", d.TaskCodes)}");
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

    private async Task<List<DelegationResponseDto>> ResolveSiteDelegationsAsync(
        Cadence.Kernel.TenantId tenantId,
        string? tenantCode,
        string trimmedSite,
        HashSet<string> authorizedStudies,
        DateTimeOffset? validAt,
        DateTimeOffset? knownAt,
        CancellationToken ct)
    {
        var siteCode = new KernelSiteCode(trimmedSite);
        var siteEndorsements = await db.CtmsDoaTaskEndorsements.AsNoTracking()
            .Where(d => d.SiteId == siteCode)
            .ToListAsync(ct);

        var endorsements = siteEndorsements
            .Where(d => authorizedStudies.Contains(d.StudyId.Raw))
            .OrderBy(d => d.StartDate, StringComparer.Ordinal)
            .ToList();

        var kernelGrantsById = new Dictionary<Guid, CtmsKernelGrantRecord>();
        foreach (string study in authorizedStudies.OrderBy(s => s, StringComparer.Ordinal))
        {
            var studyGrants = await _kernelTrust.ListSiteGrantsAsync(
                tenantId,
                tenantCode,
                study,
                trimmedSite,
                validAt,
                knownAt,
                includeInactive: true,
                ct);
            foreach (var grant in studyGrants)
            {
                kernelGrantsById[grant.GrantId] = grant;
            }
        }

        bool pointInTime = validAt.HasValue || knownAt.HasValue;
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset effectiveValidAt = validAt ?? knownAt ?? now;
        DateTimeOffset effectiveKnownAt = knownAt ?? validAt ?? now;

        var pairedGrantIds = new HashSet<Guid>();
        var results = new List<DelegationResponseDto>();

        foreach (var d in endorsements)
        {
            if (d.KernelGrantId is { } kgId && kgId != Guid.Empty)
            {
                pairedGrantIds.Add(kgId);
            }

            if (!pointInTime)
            {
                if (d.KernelGrantId is { } activeKgId && kernelGrantsById.TryGetValue(activeKgId, out var currentGrant))
                {
                    results.Add(new DelegationResponseDto(
                        RecordId: d.Id,
                        Site: d.SiteId,
                        StaffUserId: d.StaffUserId,
                        TaskCodes: DoaMutationService.ParseTaskCodes(d.TaskCodes),
                        StartDate: d.StartDate,
                        EndDate: currentGrant.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? d.EndDate,
                        IsActive: currentGrant.InEffect && d.IsActive,
                        SignedOff: true,
                        ReasonForChange: d.ReasonForChange,
                        CreatedBy: d.CreatedBy,
                        VersionIndex: d.VersionIndex,
                        KernelGrantId: activeKgId));
                }
                else
                {
                    results.Add(DoaMutationService.ToResponseDto(d));
                }

                continue;
            }

            DateTimeOffset createdOffset = new(DateTime.SpecifyKind(d.CreatedAt, DateTimeKind.Utc));
            if (createdOffset > effectiveKnownAt)
            {
                continue;
            }

            DateTimeOffset startOffset = DoaMutationService.ParseDelegationStartDate(d.StartDate, createdOffset);
            if (startOffset > effectiveValidAt)
            {
                continue;
            }

            bool signedOffKnown = d.SignedOff && (!d.SignedOffAt.HasValue || d.SignedOffAt.Value <= effectiveKnownAt);
            if (signedOffKnown && d.KernelGrantId is { } bitemporalKgId && kernelGrantsById.TryGetValue(bitemporalKgId, out var bitemporalGrant))
            {
                results.Add(new DelegationResponseDto(
                    RecordId: d.Id,
                    Site: d.SiteId,
                    StaffUserId: d.StaffUserId,
                    TaskCodes: DoaMutationService.ParseTaskCodes(d.TaskCodes),
                    StartDate: d.StartDate,
                    EndDate: bitemporalGrant.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    IsActive: bitemporalGrant.InEffect,
                    SignedOff: true,
                    ReasonForChange: d.ReasonForChange,
                    CreatedBy: d.CreatedBy,
                    VersionIndex: d.VersionIndex,
                    KernelGrantId: bitemporalKgId));
                continue;
            }

            bool revokedKnown = (!d.IsActive && !d.RevokedAt.HasValue)
                || (d.RevokedAt.HasValue && d.RevokedAt.Value <= effectiveKnownAt && d.RevokedAt.Value <= effectiveValidAt);

            results.Add(new DelegationResponseDto(
                RecordId: d.Id,
                Site: d.SiteId,
                StaffUserId: d.StaffUserId,
                TaskCodes: DoaMutationService.ParseTaskCodes(d.TaskCodes),
                StartDate: d.StartDate,
                EndDate: revokedKnown ? (d.EndDate ?? d.RevokedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null,
                IsActive: !revokedKnown,
                SignedOff: signedOffKnown,
                ReasonForChange: d.ReasonForChange,
                CreatedBy: d.CreatedBy,
                VersionIndex: d.VersionIndex,
                KernelGrantId: signedOffKnown ? d.KernelGrantId : null));
        }

        foreach (var grant in kernelGrantsById.Values.Where(g => !pairedGrantIds.Contains(g.GrantId)))
        {
            results.Add(new DelegationResponseDto(
                RecordId: grant.GrantId.ToString("D"),
                Site: siteCode,
                StaffUserId: grant.GranteeId,
                TaskCodes: grant.Capabilities.Count > 0 ? grant.Capabilities.ToList() : [grant.RoleCode ?? "SITE_GRANT"],
                StartDate: grant.ValidFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate: grant.ValidTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                IsActive: grant.InEffect,
                SignedOff: true,
                ReasonForChange: $"Canonical Kernel grant ({grant.RoleCode ?? "capability"})",
                CreatedBy: grant.IssuedBy,
                VersionIndex: Math.Max(1L, grant.Periods.Count),
                KernelGrantId: grant.GrantId));
        }

        return results
            .OrderBy(r => r.StartDate, StringComparer.Ordinal)
            .ThenBy(r => r.RecordId, StringComparer.Ordinal)
            .ToList();
    }
}
