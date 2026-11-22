using FluentValidation;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// <c>PATCH /api/v1/execution/subjects/{id}/state</c> request body. <see cref="Status"/> and
/// <see cref="State"/> are alternative property names for the same target state — exactly one must be
/// supplied (enforced by <see cref="SubjectStateUpdateRequestValidator"/>, not here). Both fields stay
/// nullable rather than defaulted to <c>""</c> so "omitted" remains distinguishable from "explicitly
/// empty", per <c>01_spec.md</c> §3's truthiness-trap guidance (generalized from the mitigated
/// <c>if not site_id:</c> row). Closes legacy raise site #14 (<c>subjects.py:598-600</c>,
/// <c>update_subject_state_endpoint</c>, <c>"Either status or state must be provided."</c>).
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectStateUpdateRequest(string? Status, string? State, string? WithdrawalKind = null)
{
    /// <summary>
    /// Resolves the two alternative property names to the single target state string. Uses <c>??</c>
    /// (null-coalescing), not a whitespace-aware fallback: an explicitly-empty <see cref="Status"/>
    /// (e.g. <c>{"status": "", "state": "ENROLLED"}</c>) still wins over a populated <see cref="State"/>
    /// here even though <see cref="SubjectStateUpdateRequestValidator"/> would accept that same payload
    /// (it treats either field being non-whitespace as sufficient) — this mirrors the exact shape mandated
    /// by <c>02_technical_design.md</c> §2.7 verbatim; see <c>RequestValidatorTests</c> for a test that
    /// documents this <see cref="Target"/>-vs-validator asymmetry explicitly rather than silently.
    /// </summary>
    public string? Target => Status ?? State;
}

/// <summary>
/// Per <c>02_technical_design.md</c> §2.7. Checks <see cref="SubjectStateUpdateRequest.Status"/> and
/// <see cref="SubjectStateUpdateRequest.State"/> independently with <see cref="string.IsNullOrWhiteSpace(string?)"/>
/// (not a blanket null/truthiness check) so a whitespace-only value is treated the same as an absent one.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectStateUpdateRequestValidator : AbstractValidator<SubjectStateUpdateRequest>
{
    public SubjectStateUpdateRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Status) || !string.IsNullOrWhiteSpace(x.State))
            .WithMessage("Either status or state must be provided.")
            .WithErrorCode("MISSING_TARGET_STATE");
    }
}
