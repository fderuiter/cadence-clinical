using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Cadence.Kernel.Explanations;

namespace Cadence.Kernel.Commands;

/// <summary>
/// A registered kernel error (K-ERR-1): a stable <see cref="Code"/> of the form <c>K.&lt;Area&gt;.&lt;Name&gt;</c>, the
/// HTTP status it maps to, and a human-readable message. Codes are never reused or renamed.
/// </summary>
public sealed record KernelError(string Code, int HttpStatus, string Message)
{
    /// <summary>
    /// Why the error happened, from the component that decided it (ADR-2482). The UI renders this; the code is for
    /// support (UX-9). Null when the producer has nothing beyond the message.
    /// </summary>
    public Explanation? Explanation { get; init; }

    /// <summary>
    /// The invalid fields and why, keyed by the command property's name, when the command failed validation
    /// (<see cref="KernelErrors.CommandInvalid"/>); null otherwise. Rendered as the <c>errors</c> member of the problem
    /// details.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    /// <summary>The same registered error with a message specific to this occurrence.</summary>
    public KernelError WithMessage(string message) => this with { Message = message };
}

/// <summary>
/// The checked-in registry of kernel error codes (spec §5.4, K-ERR-1). Defaults: authorization 403, not found 404,
/// idempotency or concurrency conflict 409, rule violation 422, lock 423.
/// </summary>
public static class KernelErrors
{
    public static readonly KernelError ReasonForChangeRequired =
        new("K.Command.ReasonForChangeRequired", 422, "A reason for change is required for every kernel mutation.");

    public static readonly KernelError IdempotencyKeyRequired =
        new("K.Command.IdempotencyKeyRequired", 422, "An idempotency key is required for every kernel command.");

    public static readonly KernelError TenantRequired =
        new("K.Command.TenantRequired", 422, "The command does not name a tenant.");

    public static readonly KernelError IdempotencyConflict =
        new("K.Command.IdempotencyConflict", 409, "The idempotency key was already used with a different payload.");

    public static readonly KernelError CommandInvalid =
        new("K.Command.Invalid", 422, "The command is not valid.");

    public static readonly KernelError HandlerMissing =
        new("K.Command.HandlerMissing", 500, "No handler is registered for the command.");

    public static readonly KernelError ActorUnresolved =
        new("K.Actor.Unresolved", 401, "No accountable actor could be resolved for the command (K-TX-5).");

    public static readonly KernelError AuthorizationDenied =
        new("K.Authorization.Denied", 403, "The actor is not authorized to perform this command.");

    public static readonly KernelError SubscriptionExpired =
        new("K.Subscription.Expired", 403, "The workspace trial subscription has expired; regulated reads, exports, electronic signatures, and audit trails remain available.");

    public static readonly KernelError ConcurrencyConflict =
        new("K.Aggregate.ConcurrencyConflict", 409, "The aggregate was changed by another command; reload and retry.");

    public static readonly KernelError NotFound =
        new("K.Aggregate.NotFound", 404, "The aggregate does not exist.");

    public static readonly KernelError AlreadyExists =
        new("K.Aggregate.AlreadyExists", 409, "An aggregate with the same identity already exists.");

    /// <summary>
    /// The errors the command pipeline itself can return for any command, before or after its handler runs: a missing
    /// tenant, idempotency key or reason for change, an unresolved actor, a failed validation, a denied authorization,
    /// an expired trial subscription, a reused idempotency key and a commit-time concurrency conflict. A command's
    /// declared errors (<see cref="RaisesKernelErrorsAttribute"/>) add to these.
    /// </summary>
    public static IReadOnlyList<KernelError> CommandPipeline { get; } =
    [
        TenantRequired,
        IdempotencyKeyRequired,
        ReasonForChangeRequired,
        ActorUnresolved,
        CommandInvalid,
        AuthorizationDenied,
        SubscriptionExpired,
        IdempotencyConflict,
        ConcurrencyConflict,
    ];

    /// <summary>
    /// Every error registered anywhere in the kernel contracts (public static <see cref="KernelError"/> fields), for the
    /// uniqueness test and for documentation.
    /// </summary>
    public static IReadOnlyList<KernelError> All { get; } = typeof(KernelErrors).Assembly.GetTypes()
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Where(f => f.FieldType == typeof(KernelError))
        .Select(f => (KernelError)f.GetValue(null)!)
        .ToArray();
}
