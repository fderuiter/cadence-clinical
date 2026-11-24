using Cadence.Apps.Execution.Application.Abstractions;

using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// Scoped <see cref="ICurrentAuditContextAccessor"/> and <see cref="IAuditActorScope"/>. A service account set
/// with <see cref="RunAs"/> wins; otherwise the actor is the authenticated caller's <c>sub</c> claim and the
/// reason comes from the <c>X-Change-Reason</c> header, decoded by <see cref="ChangeReasonHeader"/>. With neither,
/// both properties fail closed rather than fabricating a fallback actor or reason.
/// </summary>
public sealed class AmbientAuditContextAccessor(IHttpContextAccessor httpContextAccessor)
    : ICurrentAuditContextAccessor, IAuditActorScope
{
    private ServiceAccount? _serviceAccount;

    public string UserId =>
        _serviceAccount?.UserId
        ?? NullIfBlank(httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value)
        ?? throw new AuditActorUnresolvedException();

    public string ChangeReason =>
        _serviceAccount?.ChangeReason
        ?? (httpContextAccessor.HttpContext is { } http ? ChangeReasonHeader.Read(http.Request) : null)
        ?? throw new AuditChangeReasonUnresolvedException();

    public void RunAs(ServiceAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _serviceAccount = account;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
