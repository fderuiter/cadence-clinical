using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Protocol.Design;

namespace Cadence.Kernel.Protocol;

/// <summary>
/// Read-only access to a study's protocol versions (spec §6.2) for hosts that show them, such as the Canvas. Reads
/// run under the tenant's Row-Level Security, so another tenant's version is never returned. The reader does not
/// authorize the actor: the host checks the read with <c>CanI</c> first. Content is not returned, only its Schedule of
/// Activities rendering (ADR-2475).
/// </summary>
public interface IProtocolVersionReader
{
    /// <summary>The study's protocol versions, newest (highest number) first; empty when the study has none.</summary>
    Task<IReadOnlyList<ProtocolVersionSummary>> ListForStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken);

    /// <summary>One protocol version, or null when the tenant has no such version.</summary>
    Task<ProtocolVersionSummary?> GetAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken);

    /// <summary>
    /// The Schedule of Activities compiled from one version's design (ADR-2475), or null when the tenant has no such
    /// version or its content is not a typed protocol design (ADR-2472).
    /// </summary>
    Task<SoaMatrix?> GetScheduleOfActivitiesAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken);

    /// <summary>
    /// The typed <see cref="ProtocolDesign"/> stored on one version, or null when the tenant has no such version or its
    /// content is not a typed protocol design.
    /// </summary>
    Task<ProtocolDesign?> GetDesignAsync(TenantId tenant, ProtocolVersionId version, CancellationToken cancellationToken) =>
        Task.FromResult<ProtocolDesign?>(null);

    /// <summary>
    /// Reads one protocol version's summary together with its typed <see cref="ProtocolDesign"/> in a single query.
    /// </summary>
    async Task<(ProtocolVersionSummary? Version, ProtocolDesign? Design)> GetWithDesignAsync(
        TenantId tenant,
        ProtocolVersionId version,
        CancellationToken cancellationToken)
    {
        var summary = await GetAsync(tenant, version, cancellationToken).ConfigureAwait(false);
        if (summary is null)
        {
            return (null, null);
        }

        var design = await GetDesignAsync(tenant, version, cancellationToken).ConfigureAwait(false);
        return (summary, design);
    }
}

/// <summary>A protocol version without its content.</summary>
/// <param name="Id">The version.</param>
/// <param name="StudyId">The study it belongs to.</param>
/// <param name="Number">Its number in the study, from 1.</param>
/// <param name="ParentVersionId">The version it was forked from, or null for the first.</param>
/// <param name="Status">Its lifecycle status.</param>
/// <param name="ContentHash">The content hash, set once the version is submitted for review.</param>
/// <param name="AmendmentIntent">What the amendment sets out to change, or null for the first version.</param>
/// <param name="ApprovalAttestationId">The attestation that approved it, once approved.</param>
/// <param name="WithdrawalReason">Why it was withdrawn, once withdrawn.</param>
public sealed record ProtocolVersionSummary(
    ProtocolVersionId Id,
    StudyId StudyId,
    int Number,
    ProtocolVersionId? ParentVersionId,
    ProtocolVersionStatus Status,
    string? ContentHash,
    string? AmendmentIntent,
    AttestationId? ApprovalAttestationId,
    string? WithdrawalReason);
