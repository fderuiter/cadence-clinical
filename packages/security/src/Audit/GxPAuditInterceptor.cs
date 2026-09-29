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
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        // No anonymous fallback actor: a write with no authenticated principal fails instead of being
        // recorded against "system" (21 CFR 11.10(e)).
        var principal = _httpContextAccessor?.HttpContext?.Items["Principal"] as Rbac.Principal;
        if (string.IsNullOrWhiteSpace(principal?.UserId))
        {
            throw new InvalidOperationException(
                "No audit actor: the request has no authenticated principal, so the change cannot be attributed.");
        }

        string userId = principal.UserId;
        string tenantId = string.IsNullOrWhiteSpace(principal.SponsorId) ? "tenant_default" : principal.SponsorId;
        string changeReason = string.IsNullOrWhiteSpace(principal.ChangeReason) ? "Database SaveChanges mutation" : principal.ChangeReason;

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
