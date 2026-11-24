using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Database.Audit;

/// <summary>
/// Validated non-interactive service account identifier (<c>svc:*</c>) used when background workers,
/// MassTransit consumers, or controlled startup seeders write <c>IGxPAuditable</c> records outside an
/// interactive HTTP request.
/// @req:SRS-SYS-001
/// </summary>
public readonly record struct AuditServiceAccount
{
    /// <summary>Required prefix for all automated service accounts.</summary>
    public const string Prefix = "svc:";

    /// <summary>Minimum trimmed length for an automated change justification.</summary>
    public const int MinReasonLength = 10;

    /// <summary>Disallowed placeholder reason that hides the real trigger of an automated write.</summary>
    public const string DisallowedDefaultReason = "explicit_change_reason_required";

    /// <summary>Gets the canonical <c>svc:*</c> identifier.</summary>
    public string Value { get; }

    /// <summary>Initializes and validates a <c>svc:*</c> service account identifier.</summary>
    public AuditServiceAccount(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith(Prefix, StringComparison.Ordinal)
            || value.Length <= Prefix.Length)
        {
            throw new ArgumentException(
                $"Automated GxP audit service accounts must start with '{Prefix}' followed by a non-empty name.",
                nameof(value));
        }

        Value = value.Trim();
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Scoped and async-local service-account attribution scope for background workers, message consumers,
/// and controlled seeders that persist GxP-audited entities outside an authenticated user HTTP request.
/// @req:SRS-SYS-001
/// </summary>
public interface IAuditActorScope
{
    /// <summary>Gets the active <c>svc:*</c> actor identifier in the current async execution flow, if any.</summary>
    string? CurrentActor { get; }

    /// <summary>Gets the active change justification in the current async execution flow, if any.</summary>
    string? CurrentChangeReason { get; }

    /// <summary>Begins an explicit <c>svc:*</c> attribution scope and restores the prior scope on disposal.</summary>
    IDisposable Begin(string serviceAccountId, string changeReason);

    /// <summary>Executes <paramref name="operation"/> inside an explicit <c>svc:*</c> attribution scope.</summary>
    Task RunAsAsync(
        string serviceAccountId,
        string changeReason,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    /// <summary>Executes <paramref name="operation"/> inside an explicit <c>svc:*</c> attribution scope and returns its result.</summary>
    Task<T> RunAsAsync<T>(
        string serviceAccountId,
        string changeReason,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="AsyncLocal{T}"/>-backed <see cref="IAuditActorScope"/> implementation.
/// </summary>
public sealed class AmbientAuditActorScope : IAuditActorScope
{
    private static readonly AsyncLocal<(string Actor, string ChangeReason)?> Ambient = new();

    /// <summary>Gets the singleton instance suitable for static callers or DI resolution.</summary>
    public static AmbientAuditActorScope Instance { get; } = new();

    /// <summary>Gets the active <c>(Actor, ChangeReason)</c> tuple in the current async flow, if any.</summary>
    public static (string Actor, string ChangeReason)? Current => Ambient.Value;

    /// <inheritdoc />
    public string? CurrentActor => Ambient.Value?.Actor;

    /// <inheritdoc />
    public string? CurrentChangeReason => Ambient.Value?.ChangeReason;

    /// <summary>Static convenience entry point equivalent to <see cref="Begin"/>.</summary>
    public static IDisposable BeginScope(string serviceAccountId, string changeReason) =>
        Instance.Begin(serviceAccountId, changeReason);

    /// <inheritdoc />
    public IDisposable Begin(string serviceAccountId, string changeReason)
    {
        var account = new AuditServiceAccount(serviceAccountId);
        string normalizedReason = ValidateReason(changeReason);

        var previous = Ambient.Value;
        Ambient.Value = (account.Value, normalizedReason);
        return new RestoreHandle(previous);
    }

    /// <inheritdoc />
    public async Task RunAsAsync(
        string serviceAccountId,
        string changeReason,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var _ = Begin(serviceAccountId, changeReason);
        await operation(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<T> RunAsAsync<T>(
        string serviceAccountId,
        string changeReason,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var _ = Begin(serviceAccountId, changeReason);
        return await operation(cancellationToken).ConfigureAwait(false);
    }

    private static string ValidateReason(string changeReason)
    {
        if (string.IsNullOrWhiteSpace(changeReason))
        {
            throw new ArgumentException("Automated GxP audit scope requires a non-empty change reason.", nameof(changeReason));
        }

        string trimmed = changeReason.Trim();
        if (trimmed.Length < AuditServiceAccount.MinReasonLength)
        {
            throw new ArgumentException(
                $"Automated GxP audit change reason must be at least {AuditServiceAccount.MinReasonLength} characters.",
                nameof(changeReason));
        }

        if (string.Equals(trimmed, AuditServiceAccount.DisallowedDefaultReason, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Automated GxP audit change reason cannot use generic placeholder '{AuditServiceAccount.DisallowedDefaultReason}'.",
                nameof(changeReason));
        }

        return trimmed;
    }

    private sealed class RestoreHandle((string Actor, string ChangeReason)? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Ambient.Value = previous;
            }
        }
    }
}
