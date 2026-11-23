using System;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;

namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// Scoped <see cref="ICurrentAuditContextAccessor"/> and <see cref="IAuditActorScope"/>. A service account set
/// with <see cref="RunAs"/> wins; otherwise the actor is the authenticated caller's <c>sub</c> claim and the
/// reason comes from the <c>X-Change-Reason</c> header decoded by <see cref="ChangeReasonHeader"/> (at least
/// <see cref="MinimumChangeReasonLength"/> characters). With neither, reading <see cref="UserId"/> or
/// <see cref="ChangeReason"/> fails closed rather than defaulting to an unattributed placeholder.
/// </summary>
public sealed class AmbientAuditContextAccessor(IHttpContextAccessor httpContextAccessor)
    : ICurrentAuditContextAccessor, IAuditActorScope
{
    public const int MinimumChangeReasonLength = 10;

    private ServiceAccount? _serviceAccount;

    public string UserId =>
        _serviceAccount?.UserId
        ?? Cadence.Database.Audit.AmbientAuditActorScope.Current?.Actor
        ?? NullIfBlank(httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value)
        ?? throw new AuditActorUnresolvedException();

    public string ChangeReason
    {
        get
        {
            if (_serviceAccount is not null)
            {
                return _serviceAccount.ChangeReason;
            }

            if (Cadence.Database.Audit.AmbientAuditActorScope.Current is { } ambient
                && !string.IsNullOrWhiteSpace(ambient.ChangeReason)
                && ambient.ChangeReason.Trim().Length >= MinimumChangeReasonLength)
            {
                return ambient.ChangeReason.Trim();
            }

            string? headerReason = httpContextAccessor.HttpContext is { } http
                ? ChangeReasonHeader.Read(http.Request)
                : null;
            if (!string.IsNullOrWhiteSpace(headerReason)
                && headerReason.Trim().Length >= MinimumChangeReasonLength)
            {
                return headerReason.Trim();
            }

            throw new AuditChangeReasonUnresolvedException();
        }
    }

    public void RunAs(ServiceAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _serviceAccount = account;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
