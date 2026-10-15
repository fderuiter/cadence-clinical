using System.Collections.Generic;

using Cadence.Core.Domain;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Single source of truth for <see cref="DomainError"/> -&gt; HTTP status mapping (`02_technical_design.md`
/// §3.4): the five default <see cref="ErrorType"/> -&gt; status mappings, plus the named-<c>Code</c>
/// overrides that deviate from their own <see cref="ErrorType"/>'s default (<c>LOCKED_FACTOR_MUTATION</c> -&gt;
/// 422 despite <c>Type = Conflict</c>, whose default would be 409; <c>INVALID_STATE_TRANSITION</c> -&gt; 422
/// despite <c>Type = Validation</c>, whose default would be 400, because PRD-SUB-001 requires a forbidden subject
/// transition to answer 422 Unprocessable Entity (the Python reference answered 400);
/// <c>RECONSENT_REQUIRED</c> -&gt; 423 despite <c>Type = Forbidden</c>, whose default would be 403). <see cref="GetStatusCode"/> backs
/// `DomainErrorExtensions.ToProblemResult()`, the primary mechanism call sites use directly;
/// <see cref="ApplyKnownCodeStatus"/> backs `Program.cs`'s `IProblemDetailsService.CustomizeProblemDetails`
/// defense-in-depth registration (`01_spec.md` §5's "no global not-found handler exists in the legacy system"
/// gap). No other call site in this slice should hardcode a <see cref="DomainError"/> status-code literal —
/// this class is deliberately the one seam where this slice's `Domain/Subjects` folder depends on
/// `Microsoft.AspNetCore.Http` (for the `StatusCodes`/`ProblemDetails` types only), per MIG-SUBJ-010.
/// @req:PRD-SUB-001
/// </summary>
public static class DomainErrorHttpMapping
{
    private static readonly IReadOnlyDictionary<ErrorType, int> DefaultStatusByType = new Dictionary<ErrorType, int>
    {
        [ErrorType.Validation] = StatusCodes.Status400BadRequest,
        [ErrorType.NotFound] = StatusCodes.Status404NotFound,
        [ErrorType.Conflict] = StatusCodes.Status409Conflict,
        [ErrorType.Forbidden] = StatusCodes.Status403Forbidden,
        [ErrorType.Unauthorized] = StatusCodes.Status401Unauthorized,
        [ErrorType.Failure] = StatusCodes.Status500InternalServerError,
    };

    /// <summary>
    /// Codes whose status is pinned explicitly rather than derived from <see cref="ErrorType"/>.
    /// <c>SUBJECT_NOT_FOUND</c> is listed even though it agrees with its own <c>Type = NotFound</c> default
    /// (404): stating it here — not only relying on the <c>Type</c>-based branch — is what lets
    /// <see cref="ApplyKnownCodeStatus"/> recognize it from a bare wire-level <c>code</c> string alone, with
    /// no <see cref="DomainError.Type"/> available to consult. <c>LOCKED_FACTOR_MUTATION</c>,
    /// <c>INVALID_STATE_TRANSITION</c> and <c>RECONSENT_REQUIRED</c> are the subject overrides: each deviates from
    /// what its own <see cref="ErrorType"/> would otherwise produce (409, 400 and 403, respectively).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> StatusByCode = new Dictionary<string, int>
    {
        ["SUBJECT_NOT_FOUND"] = StatusCodes.Status404NotFound,
        ["LOCKED_FACTOR_MUTATION"] = StatusCodes.Status422UnprocessableEntity,
        ["INVALID_STATE_TRANSITION"] = StatusCodes.Status422UnprocessableEntity,
        ["RECONSENT_REQUIRED"] = StatusCodes.Status423Locked,
        ["OBSERVATION_NOT_FOUND"] = StatusCodes.Status404NotFound,
        ["FORM_NOT_FOUND"] = StatusCodes.Status404NotFound,
        ["FORM_LOCKED"] = StatusCodes.Status423Locked,
        ["QUERY_NOT_FOUND"] = StatusCodes.Status404NotFound,
        ["ACTIVE_QUERY_EXISTS"] = StatusCodes.Status409Conflict,
        ["UNAUTHORIZED_QUERY_ROLE"] = StatusCodes.Status403Forbidden,
        ["UNAUTHORIZED_SIGNER_ROLE"] = StatusCodes.Status403Forbidden,
        ["INVALID_HMAC_SIGNATURE"] = StatusCodes.Status400BadRequest,
        ["TRIAL_LOCKED"] = StatusCodes.Status423Locked,
        ["CONCURRENCY_CONFLICT"] = StatusCodes.Status409Conflict,
        ["OVERRIDE_RATIONALE_REQUIRED"] = StatusCodes.Status422UnprocessableEntity,
    };

    /// <summary>
    /// Resolves the canonical HTTP status for <paramref name="error"/>: a named-<c>Code</c> override when one
    /// exists, otherwise the default mapping for its <see cref="DomainError.Type"/>. An <see cref="ErrorType"/>
    /// this table has no entry for (there is none today) falls back to 500 rather than throwing.
    /// </summary>
    public static int GetStatusCode(DomainError error) =>
        StatusByCode.TryGetValue(error.Code, out var overrideStatus)
            ? overrideStatus
            : DefaultStatusByType.GetValueOrDefault(error.Type, StatusCodes.Status500InternalServerError);

    /// <summary>
    /// Defense-in-depth safety net for `Program.cs`'s global `CustomizeProblemDetails` registration: when
    /// <paramref name="problemDetails"/> already carries a recognized <c>code</c> extension (set by
    /// `DomainErrorExtensions.ToProblemResult()`, or by any future call site that produces
    /// <see cref="ProblemDetails"/> some other way), its <see cref="ProblemDetails.Status"/> is forced back
    /// onto this mapping's canonical value. A <see cref="ProblemDetails"/> with no recognized <c>code</c> —
    /// most importantly, the generic 500 ASP.NET Core writes for an exception that never carried a
    /// <see cref="DomainError"/> at all — is left completely untouched, so this can never reproduce raise
    /// site #19's blanket downgrade-to-400 bug (`02_technical_design.md` §3.4, `01_spec.md` §5 raise site #19).
    /// </summary>
    public static void ApplyKnownCodeStatus(ProblemDetails problemDetails)
    {
        if (problemDetails.Extensions.TryGetValue("code", out var codeValue)
            && codeValue is string code
            && StatusByCode.TryGetValue(code, out var canonicalStatus))
        {
            problemDetails.Status = canonicalStatus;
        }
    }
}
