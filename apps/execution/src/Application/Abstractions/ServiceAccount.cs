namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// A named non-human actor that background work runs as. Its id always carries the <c>svc:</c> prefix so audit
/// entries written by automation are distinguishable from people at a glance.
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
/// </summary>
public static class ServiceAccounts
{
    /// <summary>The eConsent-completed consumer that auto-enrolls subjects and backfills consent records.</summary>
    public static readonly ServiceAccount EConsentConsumer =
        new("svc:econsent-consumer", "Automated consent-completion enrollment");

    /// <summary>The CLI --seed bootstrapper that populates deterministic local development fixture data.</summary>
    public static readonly ServiceAccount DatabaseSeeder =
        new("svc:execution-seeder", "Initial local development seed");
}
