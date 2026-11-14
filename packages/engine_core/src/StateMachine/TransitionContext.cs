using System;
using System.Collections.Generic;

namespace Cadence.Core.Domain;

/// <summary>
/// Runtime context provided during state machine transition evaluation.
/// Evaluates synchronously in memory without network or system clock access (K-TIME-3).
/// </summary>
public sealed record TransitionContext(
    string ActorId,
    string? UserRole = null,
    DateTimeOffset? Timestamp = null,
    string? ReasonForChange = null,
    IReadOnlyDictionary<string, object?>? Payload = null);
