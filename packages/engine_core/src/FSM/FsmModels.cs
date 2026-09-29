using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Core.Domain.Rules;

namespace Cadence.Core.Domain.Fsm;

/// <summary>
/// State type classification in lifecycle graph.
/// @req:PRD-SYS-053
/// </summary>
public enum StateType
{
    Initial,
    Normal,
    Terminal,
    Error
}

/// <summary>
/// Represents a state vertex in the lifecycle graph.
/// @req:PRD-SYS-053
/// </summary>
public record StateNode(
    string Name,
    StateType StateType = StateType.Normal,
    string? Label = null,
    string? Description = null,
    IReadOnlyDictionary<string, object?>? Metadata = null
);

/// <summary>
/// Represents a directed transition edge between states.
/// @req:PRD-SYS-053
/// </summary>
public record TransitionEdge(
    string Trigger,
    string FromState,
    string ToState,
    ExpressionNode? Guard = null,
    IReadOnlyList<string>? AllowedRoles = null,
    bool RequiresSignature = false,
    bool RequiresReasonForChange = true,
    int MinReasonLength = 10,
    IReadOnlyList<string>? ActionHooks = null,
    string? Description = null
)
{
    public IReadOnlyList<string> AllowedRoles { get; init; } = AllowedRoles ?? Array.Empty<string>();
    public IReadOnlyList<string> ActionHooks { get; init; } = ActionHooks ?? Array.Empty<string>();
}

/// <summary>
/// Immutable 21 CFR Part 11 cryptographic audit record for state transitions.
/// @req:PRD-SYS-053
/// </summary>
public record Part11AuditEnvelope
{
    public string EventId { get; init; }
    public string MachineId { get; init; }
    public int MachineVersion { get; init; }
    public string FromState { get; init; }
    public string ToState { get; init; }
    public string Trigger { get; init; }
    public string Timestamp { get; init; }
    public string UserId { get; init; }
    public IReadOnlyList<string> UserRoles { get; init; }
    public string ReasonForChange { get; init; }
    public bool SignatureVerified { get; init; }
    public string Sha256Digest { get; init; }

    public Part11AuditEnvelope(
        string machineId,
        int machineVersion,
        string fromState,
        string toState,
        string trigger,
        string userId,
        IReadOnlyList<string>? userRoles,
        string reasonForChange,
        bool signatureVerified = false,
        string? eventId = null,
        string? timestamp = null,
        string? sha256Digest = null)
    {
        EventId = eventId ?? $"evt_{Guid.NewGuid().ToString("N")[..12]}";
        MachineId = machineId;
        MachineVersion = machineVersion;
        FromState = fromState;
        ToState = toState;
        Trigger = trigger;
        Timestamp = timestamp ?? DateTime.UtcNow.ToString("o");
        UserId = userId;
        UserRoles = (userRoles ?? Array.Empty<string>()).OrderBy(r => r).ToList();
        ReasonForChange = reasonForChange;
        SignatureVerified = signatureVerified;

        if (!string.IsNullOrEmpty(sha256Digest))
        {
            Sha256Digest = sha256Digest;
        }
        else
        {
            var payload = new SortedDictionary<string, object>
            {
                { "event_id", EventId },
                { "from_state", FromState },
                { "machine_id", MachineId },
                { "machine_version", MachineVersion },
                { "reason_for_change", ReasonForChange },
                { "signature_verified", SignatureVerified },
                { "timestamp", Timestamp },
                { "to_state", ToState },
                { "trigger", Trigger },
                { "user_id", UserId },
                { "user_roles", UserRoles }
            };

            string canonical = JsonSerializer.Serialize(payload);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            Sha256Digest = Convert.ToHexStringLower(hash);
        }
    }
}

/// <summary>
/// Runtime context payload supplied when requesting a state transition.
/// @req:PRD-SYS-053
/// </summary>
public record TransitionContext(
    string CurrentState,
    string Trigger,
    string UserId = "system",
    IReadOnlyList<string>? UserRoles = null,
    string? ReasonForChange = null,
    string? SignatureToken = null,
    IReadOnlyDictionary<string, object?>? Payload = null,
    DateTime? AsOf = null
)
{
    public IReadOnlyList<string> UserRoles { get; init; } = UserRoles ?? Array.Empty<string>();
    public IReadOnlyDictionary<string, object?> Payload { get; init; } = Payload ?? new Dictionary<string, object?>();
}

/// <summary>
/// Outcome of a state transition attempt.
/// @req:PRD-SYS-053
/// </summary>
public record TransitionResult(
    bool Success,
    string FromState,
    string ToState,
    string Trigger,
    string? ErrorMessage = null,
    Part11AuditEnvelope? AuditEnvelope = null,
    IReadOnlyList<string>? ExecutedActionHooks = null
)
{
    public IReadOnlyList<string> ExecutedActionHooks { get; init; } = ExecutedActionHooks ?? Array.Empty<string>();
}

/// <summary>
/// Declarative specification defining a complete FSM lifecycle graph.
/// @req:PRD-SYS-053
/// </summary>
public record StateMachineDefinition
{
    public string MachineId { get; init; }
    public string Name { get; init; }
    public int VersionIndex { get; init; }
    public IReadOnlyList<StateNode> States { get; init; }
    public IReadOnlyList<TransitionEdge> Transitions { get; init; }
    public string InitialState { get; init; }
    public string? Sha256Checksum { get; init; }
    public bool IsActive { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; }
    public string ReasonForChange { get; init; }

    public StateMachineDefinition(
        string machineId,
        string name,
        IReadOnlyList<StateNode> states,
        IReadOnlyList<TransitionEdge> transitions,
        string initialState,
        int versionIndex = 1,
        string? sha256Checksum = null,
        bool isActive = true,
        bool isDeleted = false,
        DateTime? createdAt = null,
        string createdBy = "system",
        string reasonForChange = "Initial state machine authoring")
    {
        MachineId = machineId;
        Name = name;
        VersionIndex = versionIndex;
        States = states ?? Array.Empty<StateNode>();
        Transitions = transitions ?? Array.Empty<TransitionEdge>();
        InitialState = initialState;
        IsActive = isActive;
        IsDeleted = isDeleted;
        CreatedAt = createdAt ?? DateTime.UtcNow;
        CreatedBy = createdBy;
        ReasonForChange = reasonForChange;

        var stateNames = States.Select(s => s.Name).ToHashSet();
        if (!stateNames.Contains(InitialState))
        {
            throw new ArgumentException($"Initial state '{InitialState}' does not exist in state list");
        }

        if (!string.IsNullOrEmpty(sha256Checksum))
        {
            Sha256Checksum = sha256Checksum;
        }
        else
        {
            var payload = new
            {
                initial_state = InitialState,
                machine_id = MachineId,
                name = Name,
                states = States,
                transitions = Transitions,
                version_index = VersionIndex
            };
            string canonical = JsonSerializer.Serialize(payload);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            Sha256Checksum = Convert.ToHexStringLower(hash);
        }
    }
}
