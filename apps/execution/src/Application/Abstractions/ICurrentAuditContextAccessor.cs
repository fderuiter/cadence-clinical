namespace Cadence.Apps.Execution.Application.Abstractions;

/// <summary>
/// Replaces Python's `contextvars.ContextVar` (`current_user_id`, `current_change_reason`) readers
/// (`01_spec.md` Rule 9, `02_technical_design.md` §4.4) with one consistently-defaulting accessor —
/// closing the legacy's two-idiom split (a `.get()` + `LookupError` catch that only special-cased
/// "absent" vs. a `.get() or "system"` truthy check that happened to treat blank strings as falsy too)
/// by giving every reader exactly one resolution path. Both properties always resolve to a fixed
/// default when ambient context is missing, empty, or whitespace-only, and neither ever throws.
/// @req:PRD-SYS-053
/// </summary>
public interface ICurrentAuditContextAccessor
{
    /// <summary>The acting user's identity. Defaults to <c>"system"</c> when unresolvable.</summary>
    string UserId { get; }

    /// <summary>The caller-supplied reason for change. Defaults to <c>"system_operation"</c> when unresolvable.</summary>
    string ChangeReason { get; }
}
