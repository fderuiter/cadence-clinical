using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Contracts;

/// <summary>
/// A single activity entry inside a protocol-amendment snapshot.
/// </summary>
public record AmendmentActivityDto(string Id, string? Name, string Description = "");

/// <summary>
/// Baseline or amended snapshot of a study version's activities, built by
/// Designer from its study projection
/// (apps/designer/presentation/routers/designer_routes.py:make_snapshot_from_projection).
/// </summary>
public record AmendmentSnapshotDto(string Version, IReadOnlyList<AmendmentActivityDto> Activities);

/// <summary>
/// Request body Designer POSTs to Execution's
/// /api/v1/execution/amendments/publish endpoint during the protocol-amendment
/// publish flow (apps/designer/presentation/routers/designer_routes.py:2618).
/// </summary>
public record PublishAmendmentRequestDto(
    string StudyId,
    string VersionNumber,
    string? Description,
    AmendmentSnapshotDto BaselineSnapshot,
    AmendmentSnapshotDto AmendedSnapshot);

/// <summary>
/// Execution's response to a protocol-amendment publish request. A failed or
/// timed-out call is the trigger for Designer's compensating rollback of the
/// study version to DRAFT (designer_routes.py, same publish flow) — that
/// rollback/retry behavior is Phase 1 implementation, not part of this
/// contract surface.
/// </summary>
public record PublishAmendmentResponseDto(string? AmendmentId, string? SummaryOfChanges);

/// <summary>
/// Frozen HTTP contract for the Designer -&gt; Execution edge of the MIG-03
/// same-tier cycle: POST /api/v1/execution/amendments/publish on the
/// Execution service. It was defined for the Python Designer's protocol-
/// amendment publish flow (docs/migration/phase0/03_migration_plan.md, MIG-03);
/// no C# service implements or consumes it yet. This is a contract/DTO surface
/// only: request signing, retry, rollback and serving are not built.
/// @req:PRD-SYS-053
/// </summary>
public interface IExecutionAmendmentApiClient
{
    /// <summary>Publishes a protocol amendment to Execution.</summary>
    /// <param name="request">The amendment to publish.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>Execution's acknowledgement, or a failure result.</returns>
    Task<Result<PublishAmendmentResponseDto>> PublishAmendmentAsync(
        PublishAmendmentRequestDto request, CancellationToken ct = default);
}
