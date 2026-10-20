using System.Threading.Tasks;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Endpoints.Filters;

/// <summary>
/// Enforces the mandatory <c>X-Change-Reason</c> request header (21 CFR Part 11 change-justification
/// string) ahead of mutating subject routes. Short-circuits with a <c>400</c> problem response when the
/// header is absent, present-but-whitespace-only, or a malformed RFC 8187 value (ADR-2495), otherwise
/// forwards to <c>next</c> unchanged. Handlers read the decoded reason with <see cref="ChangeReasonHeader.Read"/>.
/// <para>
/// An explicitly-empty header (<c>X-Change-Reason: ""</c>) is rejected exactly like an absent one rather
/// than silently accepted — this is <c>01_spec.md</c> §3's truthiness-trap guidance (generalized from the
/// already-mitigated <c>if not site_id:</c> row) applied to this header: "present but empty" must not
/// take the same free pass a caller might expect from a bare presence check.
/// </para>
/// <para>
/// Per <c>02_technical_design.md</c> §3.3, this filter is applied to <c>PATCH .../state</c>,
/// <c>PUT .../demographics</c>, and <c>DELETE .../demographics</c> (route registration is
/// <c>MIG-SUBJ-023</c>'s job, not this ticket's). The latter two routes read the header downstream via a
/// null-forgiving <c>ChangeReasonHeader.Read(request)!</c> — this filter's short-circuit
/// guarantee (the endpoint delegate is never invoked unless the header is present and non-whitespace) is
/// what makes that null-forgiving read provably safe rather than an unguarded runtime risk.
/// </para>
/// Legacy Python source being replaced: the <c>X-Change-Reason</c> header presence checks preceding
/// <c>update_subject_state_endpoint</c>/<c>update_subject_demographics_endpoint</c>/
/// <c>delete_subject_demographics_endpoint</c> in <c>apps/execution/presentation/routers/subjects.py</c>.
/// </summary>
public sealed class ChangeReasonHeaderFilter : IEndpointFilter
{
    private const string HeaderName = "X-Change-Reason";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var headerValue = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (!ChangeReasonHeader.TryDecode(headerValue, out string? reason))
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"{HeaderName} must be printable ASCII or RFC 8187 \"UTF-8''\" followed by percent-encoded UTF-8."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"{HeaderName} header is required."));
        }

        return next(context);
    }
}
