using Cadence.Apps.Execution.Application.Abstractions;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// `IHttpContextAccessor`-backed <see cref="ICurrentAuditContextAccessor"/> (MIG-SUBJ-017,
/// `02_technical_design.md` §4.4). <see cref="UserId"/> reads the `"sub"` claim off
/// <c>HttpContext.User</c>; <see cref="ChangeReason"/> reads the `X-Change-Reason` request header. Both
/// share the single <see cref="Resolve"/> fallback path — a missing, empty, or whitespace-only value
/// resolves to the same default as a fully-absent one, closing Rule 9's documented two-idiom split onto
/// one consistent falsy-check rather than a `null`-only check that would treat <c>""</c> differently.
/// @req:PRD-SYS-053
/// </summary>
public sealed class HttpCurrentAuditContextAccessor(IHttpContextAccessor httpContextAccessor)
    : ICurrentAuditContextAccessor
{
    private const string DefaultUserId = "system";
    private const string DefaultChangeReason = "system_operation";

    public string UserId =>
        Resolve(httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value, DefaultUserId);

    public string ChangeReason =>
        Resolve(httpContextAccessor.HttpContext?.Request.Headers["X-Change-Reason"].FirstOrDefault(), DefaultChangeReason);

    /// <summary>
    /// The one resolution path both properties share: <paramref name="value"/> is used verbatim when it
    /// carries any non-whitespace content, otherwise <paramref name="defaultValue"/> is returned — the
    /// same outcome whether the ambient value was never set, set to <c>""</c>, or set to whitespace only.
    /// </summary>
    private static string Resolve(string? value, string defaultValue) =>
        string.IsNullOrWhiteSpace(value) ? defaultValue : value;
}
