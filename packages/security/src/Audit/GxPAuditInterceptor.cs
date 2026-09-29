using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cadence.Security.Audit;

/// <summary>
/// Entity Framework Core SaveChangesInterceptor that automatically generates 21 CFR Part 11
/// tamper-evident SHA-256 Merkle chain audit entries on entity persistence.
/// @req:PRD-SYS-053
/// </summary>
public class GxPAuditInterceptor : SaveChangesInterceptor
{
    private readonly AuditDigestEngine _auditEngine;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly string _serviceName;

    public GxPAuditInterceptor(
        AuditDigestEngine? auditEngine = null,
        IHttpContextAccessor? httpContextAccessor = null,
        string serviceName = "cadence_service")
    {
        _auditEngine = auditEngine ?? new AuditDigestEngine();
        _httpContextAccessor = httpContextAccessor;
        _serviceName = serviceName;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context != null)
        {
            RecordAuditLogs(eventData.Context);
        }
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null)
        {
            RecordAuditLogs(eventData.Context);
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void RecordAuditLogs(DbContext context)
    {
        string userId = "system";
        string tenantId = "tenant_default";
        string changeReason = "Database SaveChanges mutation";

        if (_httpContextAccessor?.HttpContext?.Items["Principal"] is Rbac.Principal principal)
        {
            if (!string.IsNullOrWhiteSpace(principal.UserId)) userId = principal.UserId;
            if (!string.IsNullOrWhiteSpace(principal.SponsorId)) tenantId = principal.SponsorId;
            if (!string.IsNullOrWhiteSpace(principal.ChangeReason)) changeReason = principal.ChangeReason;
        }

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            string entityName = entry.Entity.GetType().Name;
            string actionType = entry.State switch
            {
                EntityState.Added => "CREATE",
                EntityState.Modified => "UPDATE",
                EntityState.Deleted => "DELETE",
                _ => "PERSIST"
            };

            var primaryKey = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
            string entityId = primaryKey?.CurrentValue?.ToString() ?? Guid.NewGuid().ToString();

            var details = new Dictionary<string, object?>();
            foreach (var prop in entry.Properties)
            {
                if (prop.IsModified || entry.State == EntityState.Added)
                {
                    details[prop.Metadata.Name] = prop.CurrentValue;
                }
            }

            var payload = new AuditLogPayload
            {
                ServiceName = _serviceName,
                ActionType = actionType,
                EntityName = entityName,
                EntityId = entityId,
                UserId = userId,
                TenantId = tenantId,
                ReasonForChange = changeReason,
                Details = details
            };

            _auditEngine.LogEvent(payload);
        }
    }
}
