using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Application;
using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Database.Audit;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Econsent.Infrastructure.Consumers;

/// <summary>
/// Replicates Clinical Kernel scope locks (<c>study</c>, <c>study_site</c>, <c>enrollment</c>) into
/// <see cref="EconsentDbContext"/> so consent capture and template publishing enforce lock posture locally
/// without runtime HTTP calls or spending single-use biometric liveness tokens.
/// </summary>
public sealed class KernelScopeLockEconsentConsumer(
    EconsentDbContext dbContext,
    IEconsentUnitOfWork unitOfWork,
    ITenantScope? tenantScope = null,
    IClock? clock = null,
    ILogger<KernelScopeLockEconsentConsumer>? logger = null)
    : IConsumer<KernelScopeLockApplied>, IConsumer<KernelScopeLockReleased>
{
    private const string Actor = "svc:econsent-lock-consumer";

    private static readonly HashSet<string> SupportedScopeKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "study",
        "study_site",
        "enrollment",
    };

    private readonly IClock _clock = clock ?? SystemClock.Instance;

    public async Task Consume(ConsumeContext<KernelScopeLockApplied> context)
    {
        var msg = context.Message;
        if (string.IsNullOrWhiteSpace(msg.TenantCode)
            || string.IsNullOrWhiteSpace(msg.LockId)
            || !SupportedScopeKinds.Contains(msg.ScopeKind))
        {
            return;
        }

        using var _ = BindTenant(msg.TenantCode);
        using var audit = AmbientAuditActorScope.BeginScope(Actor, $"Replicate kernel scope lock {msg.LockId}");

        string normalizedLockId = StripPrefix(msg.LockId, "assertion:");
        string normalizedStudyRef = StripPrefix(msg.StudyRef, "study:");

        var existing = await dbContext.ReplicatedScopeLocks
            .FirstOrDefaultAsync(
                l => l.LockId == normalizedLockId || l.LockId == msg.LockId,
                context.CancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            dbContext.ReplicatedScopeLocks.Add(new ReplicatedScopeLock
            {
                LockId = normalizedLockId,
                StudyRef = normalizedStudyRef,
                StudyCode = msg.StudyCode,
                SiteCode = msg.SiteCode,
                SubjectCode = msg.SubjectCode,
                ScopeKind = msg.ScopeKind.ToLowerInvariant(),
                ScopeRef = msg.ScopeRef,
                LockKind = msg.LockKind,
                Milestone = msg.Milestone,
                IsActive = true,
                AppliedAt = msg.AppliedAt,
                AppliedBy = msg.AppliedBy,
                CreatedAt = _clock.UtcNow,
                CreatedBy = Actor,
                ReasonForChange = $"Replicated kernel {msg.ScopeKind} {msg.LockKind} lock {normalizedLockId}"
            });
        }
        else if (existing.ReleasedAt is null)
        {
            existing.StudyRef = normalizedStudyRef;
            existing.StudyCode = msg.StudyCode;
            existing.SiteCode = msg.SiteCode ?? existing.SiteCode;
            existing.SubjectCode = msg.SubjectCode ?? existing.SubjectCode;
            existing.ScopeKind = msg.ScopeKind.ToLowerInvariant();
            existing.ScopeRef = msg.ScopeRef;
            existing.LockKind = msg.LockKind;
            existing.Milestone = msg.Milestone;
            existing.IsActive = true;
            existing.ReasonForChange = $"Refreshed kernel {msg.ScopeKind} {msg.LockKind} lock {normalizedLockId}";
        }

        await unitOfWork.CommitAsync(context.CancellationToken).ConfigureAwait(false);
        logger?.LogInformation("Replicated kernel scope lock {LockId} ({ScopeKind}) into eConsent.", normalizedLockId, msg.ScopeKind);
    }

    public async Task Consume(ConsumeContext<KernelScopeLockReleased> context)
    {
        var msg = context.Message;
        if (string.IsNullOrWhiteSpace(msg.TenantCode)
            || string.IsNullOrWhiteSpace(msg.LockId)
            || !SupportedScopeKinds.Contains(msg.ScopeKind))
        {
            return;
        }

        using var _ = BindTenant(msg.TenantCode);
        using var audit = AmbientAuditActorScope.BeginScope(Actor, $"Release replicated kernel scope lock {msg.LockId}");

        string normalizedLockId = StripPrefix(msg.LockId, "assertion:");
        string normalizedStudyRef = StripPrefix(msg.StudyRef, "study:");

        var existingLocks = await dbContext.ReplicatedScopeLocks
            .Where(l => l.LockId == normalizedLockId || l.LockId == msg.LockId)
            .ToListAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (existingLocks.Count == 0)
        {
            dbContext.ReplicatedScopeLocks.Add(new ReplicatedScopeLock
            {
                LockId = normalizedLockId,
                StudyRef = normalizedStudyRef,
                StudyCode = msg.StudyCode,
                SiteCode = msg.SiteCode,
                SubjectCode = msg.SubjectCode,
                ScopeKind = msg.ScopeKind.ToLowerInvariant(),
                ScopeRef = msg.ScopeRef,
                LockKind = msg.LockKind,
                Milestone = "Released",
                IsActive = false,
                AppliedAt = msg.ReleasedAt,
                AppliedBy = msg.ReleasedBy,
                ReleasedAt = msg.ReleasedAt,
                ReleasedBy = msg.ReleasedBy,
                ReleaseReason = msg.ReleaseReason,
                CreatedAt = _clock.UtcNow,
                CreatedBy = Actor,
                ReasonForChange = msg.ReleaseReason
            });
        }
        else
        {
            foreach (var scopeLock in existingLocks)
            {
                scopeLock.IsActive = false;
                scopeLock.ReleasedAt = msg.ReleasedAt;
                scopeLock.ReleasedBy = msg.ReleasedBy;
                scopeLock.ReleaseReason = msg.ReleaseReason;
                scopeLock.ReasonForChange = msg.ReleaseReason;
            }
        }

        await unitOfWork.CommitAsync(context.CancellationToken).ConfigureAwait(false);
        logger?.LogInformation("Released replicated kernel scope lock {LockId} in eConsent.", normalizedLockId);
    }

    internal static string StripPrefix(string? raw, string prefix)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string trimmed = raw.Trim();
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..]
            : trimmed;
    }

    private IDisposable? BindTenant(string tenantCode)
    {
        if (tenantScope is null)
        {
            return null;
        }

        return Guid.TryParse(tenantCode.Trim(), out Guid tenantGuid) && tenantGuid != Guid.Empty
            ? tenantScope.Bind(tenantGuid)
            : tenantScope.Bind(tenantCode);
    }
}
