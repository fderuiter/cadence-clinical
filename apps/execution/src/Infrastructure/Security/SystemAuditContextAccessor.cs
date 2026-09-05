using Cadence.Apps.Execution.Application.Abstractions;

namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// Fixed-identity <see cref="ICurrentAuditContextAccessor"/> for contexts with no ambient
/// <c>HttpContext</c> — namely <c>EConsentCompletedConsumer</c> (MIG-SUBJ-021), registered
/// scoped-per-consume via MassTransit's own DI scope (`02_technical_design.md` §4.4). Reproduces the
/// legacy Redis subscriber's hardcoded `user_id="system-pubsub"` identity exactly: both values are
/// compile-time constants, never configurable and never influenced by ambient state — there is no
/// constructor dependency on <c>IHttpContextAccessor</c> at all, so no ambient <c>HttpContext</c> could
/// ever change the outcome.
/// @req:PRD-SYS-053
/// </summary>
public sealed class SystemAuditContextAccessor : ICurrentAuditContextAccessor
{
    /// <summary>The fixed pubsub-consumer identity, matching the legacy subscriber's hardcoded value exactly.</summary>
    public const string SystemUserId = "system-pubsub";

    /// <summary>The fixed pubsub-consumer change reason, matching the legacy subscriber's hardcoded value exactly.</summary>
    public const string SystemChangeReason = "Automated consent-completion enrollment";

    public string UserId => SystemUserId;

    public string ChangeReason => SystemChangeReason;
}
