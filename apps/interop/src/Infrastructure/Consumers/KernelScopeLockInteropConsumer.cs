using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application;
using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Contracts;
using Cadence.Database.Audit;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Interop.Infrastructure.Consumers;

/// <summary>
/// Replicates study-wide kernel database lock and release events into <see cref="InteropDbContext"/>,
/// revoking both replicated study lock certificates and any outstanding SCE database-lock authorization tokens
/// when a study lock is released.
/// </summary>
public sealed class KernelScopeLockInteropConsumer(
    InteropDbContext db,
    IInteropUnitOfWork unitOfWork,
    ITenantScope? tenantScope = null,
    IClock? clock = null,
    ILogger<KernelScopeLockInteropConsumer>? logger = null)
    : IConsumer<KernelScopeLockApplied>, IConsumer<KernelScopeLockReleased>
{
    private const string Actor = "svc:interop-lock-consumer";
    private readonly IClock _clock = clock ?? SystemClock.Instance;

    public async Task Consume(ConsumeContext<KernelScopeLockApplied> context)
    {
        var msg = context.Message;
        if (!string.Equals(msg.ScopeKind, "study", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(msg.TenantCode)
            || string.IsNullOrWhiteSpace(msg.LockId))
        {
            return;
        }

        using var _ = BindTenant(msg.TenantCode);
        using var audit = AmbientAuditActorScope.BeginScope(Actor, $"Replicate kernel study lock {msg.LockId}");

        string normalizedLockId = KernelLockCertificateVerifier.NormalizePrefix(msg.LockId, "assertion:");
        string normalizedStudyRef = KernelLockCertificateVerifier.NormalizePrefix(msg.StudyRef, "study:");
        string? certJson = msg.Certificate is not null ? JsonSerializer.Serialize(msg.Certificate) : null;

        var existing = await db.ReplicatedStudyLocks
            .FirstOrDefaultAsync(
                l => l.LockId == normalizedLockId || l.LockId == msg.LockId,
                context.CancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.ReplicatedStudyLocks.Add(new ReplicatedStudyLock
            {
                LockId = normalizedLockId,
                StudyRef = normalizedStudyRef,
                StudyCode = msg.StudyCode,
                ScopeKind = msg.ScopeKind,
                LockKind = msg.LockKind,
                Milestone = msg.Milestone,
                IsActive = true,
                CertificateId = msg.Certificate?.CertificateId,
                CertificateFingerprint = msg.Certificate?.FingerprintSha256,
                CertificateJson = certJson,
                AppliedBy = msg.AppliedBy,
                AppliedAt = msg.AppliedAt,
                CreatedAt = _clock.UtcNow,
                CreatedBy = Actor,
                ReasonForChange = $"Kernel study lock {msg.LockId} replicated"
            });
        }
        else if (existing.RevokedAt is null)
        {
            existing.StudyRef = normalizedStudyRef;
            existing.StudyCode = msg.StudyCode;
            existing.LockKind = msg.LockKind;
            existing.Milestone = msg.Milestone;
            existing.IsActive = true;
            existing.CertificateId = msg.Certificate?.CertificateId ?? existing.CertificateId;
            existing.CertificateFingerprint = msg.Certificate?.FingerprintSha256 ?? existing.CertificateFingerprint;
            existing.CertificateJson = certJson ?? existing.CertificateJson;
            existing.ReasonForChange = $"Kernel study lock {msg.LockId} refreshed";
        }

        await unitOfWork.CommitAsync(context.CancellationToken).ConfigureAwait(false);
        logger?.LogInformation("Replicated kernel study lock {LockId} for study {StudyCode} into SCE.", normalizedLockId, msg.StudyCode);
    }

    public async Task Consume(ConsumeContext<KernelScopeLockReleased> context)
    {
        var msg = context.Message;
        if (!string.Equals(msg.ScopeKind, "study", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(msg.TenantCode)
            || string.IsNullOrWhiteSpace(msg.LockId))
        {
            return;
        }

        using var _ = BindTenant(msg.TenantCode);
        using var audit = AmbientAuditActorScope.BeginScope(Actor, $"Revoke released kernel study lock {msg.LockId}");

        string normalizedLockId = KernelLockCertificateVerifier.NormalizePrefix(msg.LockId, "assertion:");
        string normalizedStudyRef = KernelLockCertificateVerifier.NormalizePrefix(msg.StudyRef, "study:");

        var existingLocks = await db.ReplicatedStudyLocks
            .Where(l => l.LockId == normalizedLockId
                || l.LockId == msg.LockId
                || (msg.RevokedCertificateId != null && l.CertificateId == msg.RevokedCertificateId))
            .ToListAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (existingLocks.Count == 0)
        {
            db.ReplicatedStudyLocks.Add(new ReplicatedStudyLock
            {
                LockId = normalizedLockId,
                StudyRef = normalizedStudyRef,
                StudyCode = msg.StudyCode,
                ScopeKind = msg.ScopeKind,
                LockKind = msg.LockKind,
                Milestone = "Revoked",
                IsActive = false,
                CertificateId = msg.RevokedCertificateId,
                AppliedBy = msg.ReleasedBy,
                AppliedAt = msg.ReleasedAt,
                RevokedAt = msg.ReleasedAt,
                RevokedBy = msg.ReleasedBy,
                RevocationReason = msg.ReleaseReason,
                CreatedAt = _clock.UtcNow,
                CreatedBy = Actor,
                ReasonForChange = msg.ReleaseReason
            });
        }
        else
        {
            foreach (var studyLock in existingLocks)
            {
                studyLock.IsActive = false;
                studyLock.RevokedAt = msg.ReleasedAt;
                studyLock.RevokedBy = msg.ReleasedBy;
                studyLock.RevocationReason = msg.ReleaseReason;
                studyLock.CertificateId ??= msg.RevokedCertificateId;
                studyLock.ReasonForChange = msg.ReleaseReason;
            }
        }

        var activeTokens = await db.DatabaseLockAuthTokens
            .Where(t => !t.IsRevoked && (
                t.LockId == normalizedLockId
                || t.LockId == msg.LockId
                || (msg.RevokedCertificateId != null && t.CertificateId == msg.RevokedCertificateId)))
            .ToListAsync(context.CancellationToken)
            .ConfigureAwait(false);

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = msg.ReleasedAt;
            token.RevokedBy = msg.ReleasedBy;
            token.RevocationReason = $"Revoked by kernel lock release: {msg.ReleaseReason}";
            token.ReasonForChange = $"Revoked by kernel lock release: {msg.ReleaseReason}";
        }

        await unitOfWork.CommitAsync(context.CancellationToken).ConfigureAwait(false);
        logger?.LogInformation(
            "Revoked replicated study lock {LockId} and {TokenCount} SCE lock tokens for study {StudyCode}.",
            normalizedLockId,
            activeTokens.Count,
            msg.StudyCode);
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
