namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// A named non-human actor that background work runs as. Its id always carries the <c>svc:</c> prefix so audit
/// entries written by automation are distinguishable from people at a glance.
/// @req:PRD-SYS-053
/// </summary>
public sealed record ServiceAccount
{
    public const string IdPrefix = "svc:";

    public ServiceAccount(string userId, string changeReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeReason);
        if (!userId.StartsWith(IdPrefix, StringComparison.Ordinal) || userId.Length == IdPrefix.Length)
        {
            throw new ArgumentException($"Service account ids must start with '{IdPrefix}' and name the account.", nameof(userId));
        }

        UserId = userId;
        ChangeReason = changeReason;
    }

    public string UserId { get; }

    public string ChangeReason { get; }
}

/// <summary>
/// The execution service's declared service accounts. Adding background work means adding its account here.
/// @req:PRD-SYS-053
/// </summary>
public static class ServiceAccounts
{
    /// <summary>The eConsent-completed consumer that auto-enrolls subjects and backfills consent records.</summary>
    public static readonly ServiceAccount EConsentConsumer =
        new("svc:econsent-consumer", "Automated consent-completion enrollment");

    /// <summary>The rule engine and query dispatch worker that issues auto-queries and resolves queries upon data correction.</summary>
    public static readonly ServiceAccount AutoQueryEngine =
        new("svc:auto-query-engine", "Automated query dispatch and resolution");
}
