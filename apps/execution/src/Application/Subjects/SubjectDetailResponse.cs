using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Wire shape for <c>GET /api/v1/execution/subjects/{subject_id}</c> (`01_spec.md` §1.2). The RTSM
/// blinding redaction rule (`treatment_group`/`randomization_seed`/`investigational_product_id` nulled for a
/// blinded caller role) is caller-side business logic for whichever application service constructs
/// this record (MIG-SUBJ-019's Detail read), not a concern of the DTO shape itself.
/// Legacy Python source being replaced: `apps/execution/presentation/dtos.py`'s
/// <c>SubjectDetailResponse</c> Pydantic model.
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectDetailResponse(
    SubjectId Id,
    SubjectCode SubjectId,
    StudyCode StudyId,
    string Status,
    SiteCode? SiteId,
    string ActiveProtocolVersion,
    int EnrollmentIndex,
    string? TreatmentGroup,
    string? RandomizationSeed,
    string? InvestigationalProductId,
    string? EnrollmentRef = null);
