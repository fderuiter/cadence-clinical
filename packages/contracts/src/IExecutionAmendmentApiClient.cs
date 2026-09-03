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
/// Execution service, currently reached via
/// os.getenv("EXECUTION_SERVICE_URL") in designer_routes.py's protocol-
/// amendment publish flow (see docs/migration/phase0/03_migration_plan.md,
/// MIG-03). Implemented by the Execution service, consumed by Designer. This
/// is a contract/DTO surface only — request signing, retry/rollback, and
/// serving logic remain Phase 1 (Slice 01/Slice 02) implementation work.
/// @req:PRD-SYS-053
/// </summary>
public interface IExecutionAmendmentApiClient
{
    Task<Result<PublishAmendmentResponseDto>> PublishAmendmentAsync(
        PublishAmendmentRequestDto request, CancellationToken ct = default);
}
