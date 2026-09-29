using Cadence.Apps.Execution.Application.Abstractions;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// Scoped <see cref="ICurrentAuditContextAccessor"/> and <see cref="IAuditActorScope"/>. A service account set
/// with <see cref="RunAs"/> wins; otherwise the actor is the authenticated caller's <c>sub</c> claim and the
/// reason comes from the <c>X-Change-Reason</c> header. With neither, <see cref="UserId"/> throws rather than
/// defaulting to an anonymous identity.
/// @req:PRD-SYS-053
/// </summary>
public sealed class AmbientAuditContextAccessor(IHttpContextAccessor httpContextAccessor)
    : ICurrentAuditContextAccessor, IAuditActorScope
{
    private const string DefaultChangeReason = "system_operation";

    private ServiceAccount? _serviceAccount;

    public string UserId =>
        _serviceAccount?.UserId
        ?? NullIfBlank(httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value)
        ?? throw new AuditActorUnresolvedException();

    public string ChangeReason =>
        _serviceAccount?.ChangeReason
        ?? NullIfBlank(httpContextAccessor.HttpContext?.Request.Headers["X-Change-Reason"].FirstOrDefault())
        ?? DefaultChangeReason;

    public void RunAs(ServiceAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _serviceAccount = account;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
