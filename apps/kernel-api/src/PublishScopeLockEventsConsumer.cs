using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Contracts;
using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Tenancy;

namespace Cadence.Kernel.Host;

/// <summary>
/// Receives replicated scope-lock integration events published out of the Clinical Kernel outbox so in-process hosts,
/// broker relays, and integration tests can observe or forward <see cref="KernelScopeLockApplied"/> and
/// <see cref="KernelScopeLockReleased"/> notifications.
/// </summary>
public interface IScopeLockEventSink
{
    /// <summary>Handles an applied scope lock event emitted by the Clinical Kernel outbox.</summary>
    Task OnScopeLockAppliedAsync(KernelScopeLockApplied notification, CancellationToken cancellationToken);

    /// <summary>Handles a released scope lock event emitted by the Clinical Kernel outbox.</summary>
    Task OnScopeLockReleasedAsync(KernelScopeLockReleased notification, CancellationToken cancellationToken);
}

/// <summary>
/// Default in-memory sink retaining published scope lock notifications for diagnostics and test verification.
/// </summary>
public sealed class InMemoryScopeLockEventSink : IScopeLockEventSink
{
    private readonly ConcurrentQueue<KernelScopeLockApplied> _applied = new();
    private readonly ConcurrentQueue<KernelScopeLockReleased> _released = new();

    /// <summary>Snapshot of applied scope lock events observed by this host.</summary>
    public IReadOnlyList<KernelScopeLockApplied> Applied => [.. _applied];

    /// <summary>Snapshot of released scope lock events observed by this host.</summary>
    public IReadOnlyList<KernelScopeLockReleased> Released => [.. _released];

    /// <inheritdoc />
    public Task OnScopeLockAppliedAsync(KernelScopeLockApplied notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _applied.Enqueue(notification);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnScopeLockReleasedAsync(KernelScopeLockReleased notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _released.Enqueue(notification);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Kernel outbox consumer that translates committed <see cref="LockApplied"/> and <see cref="LockReleased"/> domain
/// events into <see cref="KernelScopeLockApplied"/> and <see cref="KernelScopeLockReleased"/> integration contracts
/// (including certificate revocation on <see cref="LockReleased"/>).
/// </summary>
internal sealed class PublishScopeLockEventsConsumer(
    ILockRecords locks,
    IStudyDirectory studies,
    ITenantDirectory tenants,
    IEnumerable<IScopeLockEventSink> sinks) : IKernelEventConsumer
{
    public const string Name = "kernel-scope-locks";

    private static readonly string LockAppliedType =
        typeof(LockApplied).GetCustomAttribute<KernelEventAttribute>()!.Type;

    private static readonly string LockReleasedType =
        typeof(LockReleased).GetCustomAttribute<KernelEventAttribute>()!.Type;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
    };

    public bool Handles(EventEnvelope envelope) =>
        envelope is not null
        && (string.Equals(envelope.Type, LockAppliedType, StringComparison.Ordinal)
            || string.Equals(envelope.Type, LockReleasedType, StringComparison.Ordinal));

    public async Task ConsumeAsync(KernelEventDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var envelope = delivery.Envelope;
        if (string.Equals(envelope.Type, LockAppliedType, StringComparison.Ordinal))
        {
            var applied = envelope.Payload.Deserialize<LockApplied>(JsonOptions)
                ?? throw new KernelOutboxPoisonMessageException("LockApplied payload deserialized to null.");
            var notification = await BuildAppliedNotificationAsync(envelope, applied, cancellationToken).ConfigureAwait(false);
            foreach (var sink in sinks)
            {
                await sink.OnScopeLockAppliedAsync(notification, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (string.Equals(envelope.Type, LockReleasedType, StringComparison.Ordinal))
        {
            var released = envelope.Payload.Deserialize<LockReleased>(JsonOptions)
                ?? throw new KernelOutboxPoisonMessageException("LockReleased payload deserialized to null.");
            var notification = await BuildReleasedNotificationAsync(envelope, released, cancellationToken).ConfigureAwait(false);
            foreach (var sink in sinks)
            {
                await sink.OnScopeLockReleasedAsync(notification, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<KernelScopeLockApplied> BuildAppliedNotificationAsync(
        EventEnvelope envelope,
        LockApplied applied,
        CancellationToken cancellationToken)
    {
        var tenantCode = await ResolveTenantCodeAsync(applied.TenantId, cancellationToken).ConfigureAwait(false);
        var (studyCode, siteCode, subjectCode, _) = await ResolveScopeContextAsync(
            applied.TenantId,
            applied.StudyId,
            applied.LockId,
            cancellationToken).ConfigureAwait(false);

        DatabaseLockCertificateContract? certContract = null;
        if (applied.Certificate is { } cert)
        {
            certContract = new DatabaseLockCertificateContract(
                cert.CertificateId,
                string.IsNullOrWhiteSpace(cert.TenantId) ? tenantCode : cert.TenantId,
                applied.StudyId.Value.ToString("D"),
                string.IsNullOrWhiteSpace(cert.StudyCode) ? studyCode : cert.StudyCode,
                applied.LockId.Value.ToString("D"),
                cert.Milestone.ToString(),
                cert.AttestationId,
                cert.SignedBy,
                cert.IssuedAt,
                cert.KeyId,
                cert.Algorithm,
                cert.PayloadSha256,
                cert.FingerprintSha256,
                cert.SignatureBase64);
        }

        return new KernelScopeLockApplied
        {
            TenantCode = tenantCode,
            LockId = applied.LockId.Value.ToString("D"),
            StudyRef = applied.StudyId.Value.ToString("D"),
            StudyCode = studyCode,
            SiteCode = siteCode,
            SubjectCode = subjectCode,
            ScopeKind = applied.Scope.Kind,
            ScopeRef = applied.Scope.Unversioned().ToString(),
            LockKind = applied.Kind.ToString(),
            Milestone = applied.Milestone.ToString(),
            AppliedBy = envelope.Actor,
            AppliedAt = envelope.Recorded,
            Certificate = certContract,
        };
    }

    private async Task<KernelScopeLockReleased> BuildReleasedNotificationAsync(
        EventEnvelope envelope,
        LockReleased released,
        CancellationToken cancellationToken)
    {
        var tenantCode = await ResolveTenantCodeAsync(released.TenantId, cancellationToken).ConfigureAwait(false);
        var (studyCode, siteCode, subjectCode, releaseReason) = await ResolveScopeContextAsync(
            released.TenantId,
            released.StudyId,
            released.LockId,
            cancellationToken).ConfigureAwait(false);

        return new KernelScopeLockReleased
        {
            TenantCode = tenantCode,
            ReleaseAssertionId = released.AssertionId.Value.ToString("D"),
            LockId = released.LockId.Value.ToString("D"),
            StudyRef = released.StudyId.Value.ToString("D"),
            StudyCode = studyCode,
            SiteCode = siteCode,
            SubjectCode = subjectCode,
            ScopeKind = released.Scope.Kind,
            ScopeRef = released.Scope.Unversioned().ToString(),
            LockKind = released.Kind.ToString(),
            ReleasedBy = envelope.Actor,
            ReleasedAt = envelope.Recorded,
            ReleaseReason = releaseReason ?? "Scope lock released",
            RevokedCertificateId = released.RevokedCertificateId,
        };
    }

    private async Task<string> ResolveTenantCodeAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var code = await tenants.FindCodeAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(code) ? code : tenantId.Value.ToString("D");
    }

    private async Task<(string StudyCode, string? SiteCode, string? SubjectCode, string? ReleaseReason)> ResolveScopeContextAsync(
        TenantId tenantId,
        StudyId studyId,
        AssertionId lockId,
        CancellationToken cancellationToken)
    {
        var study = await studies.FindStudyAsync(tenantId, studyId, cancellationToken).ConfigureAwait(false);
        var studyCode = study?.StudyCode ?? studyId.Value.ToString("D");

        var lockEntry = await locks.FindAsync(tenantId, lockId, cancellationToken).ConfigureAwait(false);
        string? siteCode = null;
        if (lockEntry?.StudySiteId is { } studySiteId)
        {
            var studySite = await studies.FindStudySiteAsync(tenantId, studySiteId, cancellationToken).ConfigureAwait(false);
            siteCode = studySite?.SiteCode ?? studySiteId.Value.ToString("D");
        }

        return (studyCode, siteCode, lockEntry?.SubjectCode, lockEntry?.ReleaseReason);
    }
}
