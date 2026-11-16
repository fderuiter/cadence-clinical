using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Cadence.Core.Domain;

/// <summary>
/// Abstract generic aggregate root enforcing state machine lifecycle rules, 21 CFR Part 11 audit trails,
/// and atomic outbox message generation (PRD-EVT-001, PRD-SYS-001).
/// External orchestrators must not bypass aggregate boundary methods to alter entity status properties.
/// </summary>
public abstract class StatefulAggregateRoot<TId, TState, TTrigger> : AggregateRoot<TId>, IStatefulAggregate
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private readonly List<StateTransitionRecord> _transitionHistory = [];
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<TransactionalOutboxMessage> _outboxMessages = [];

    /// <summary>
    /// Current lifecycle state. The setter is protected to enforce aggregate boundary encapsulation.
    /// </summary>
    public TState Status { get; protected set; }

    /// <summary>
    /// Monotonically appended 21 CFR Part 11 transition audit records.
    /// </summary>
    [NotMapped]
    public IReadOnlyList<StateTransitionRecord> TransitionHistory => _transitionHistory.AsReadOnly();

    /// <summary>
    /// Domain events raised during lifecycle transitions.
    /// </summary>
    [NotMapped]
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Outbox messages queued for transactional dispatch.
    /// </summary>
    [NotMapped]
    public IReadOnlyList<TransactionalOutboxMessage> OutboxMessages => _outboxMessages.AsReadOnly();

    /// <summary>
    /// Explicitly sets status for unit testing purposes.
    /// </summary>
    public void SetStatusForTesting(TState status)
    {
        Status = status;
    }

    /// <summary>
    /// Explicitly records a domain event raised during aggregate business logic.
    /// </summary>
    protected void RecordDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// The explicit transition state machine definition governing this aggregate.
    /// </summary>
    protected abstract StateMachineDefinition<TState, TTrigger> StateMachine { get; }

    protected StatefulAggregateRoot() { }

    protected StatefulAggregateRoot(TId id, TState initialStatus = default) : base(id)
    {
        Status = initialStatus;
    }

    /// <summary>
    /// Evaluates and applies a state transition triggered by <paramref name="trigger"/>.
    /// </summary>
    public Result<TState> Transition(
        TTrigger trigger,
        string userId,
        DateTimeOffset timestamp,
        string reasonForChange,
        string? userRole = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var context = new TransitionContext(userId, userRole, timestamp, reasonForChange, metadata);
        var validationResult = StateMachine.ValidateTransition(Status, trigger, context);
        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        var previousState = Status;
        var nextState = validationResult.Value!;
        ApplyTransition(previousState, nextState, trigger.ToString(), userId, userRole, timestamp, reasonForChange);
        return nextState;
    }

    /// <summary>
    /// Evaluates and applies a direct transition to <paramref name="targetState"/>.
    /// </summary>
    public Result<TState> TransitionTo(
        TState targetState,
        string userId,
        DateTimeOffset timestamp,
        string reasonForChange,
        string? userRole = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var context = new TransitionContext(userId, userRole, timestamp, reasonForChange, metadata);
        var validationResult = StateMachine.ValidateTransitionTo(Status, targetState, context);
        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        var previousState = Status;
        var nextState = validationResult.Value!;
        if (!EqualityComparer<TState>.Default.Equals(previousState, nextState))
        {
            string triggerName = StateMachine.TryGetTriggerFor(previousState, nextState, out var trigger)
                ? trigger.ToString()
                : $"TransitionTo_{nextState}";

            ApplyTransition(previousState, nextState, triggerName, userId, userRole, timestamp, reasonForChange);
        }
        return nextState;
    }

    private void ApplyTransition(
        TState previousState,
        TState nextState,
        string trigger,
        string userId,
        string? userRole,
        DateTimeOffset timestamp,
        string reasonForChange)
    {
        Status = nextState;
        ReasonForChange = reasonForChange;
        VersionIndex++;

        string? predecessorHash = _transitionHistory.Count > 0 ? _transitionHistory[^1].Sha256Hash : null;
        var auditRecord = StateTransitionRecord.Create(
            previousState.ToString(),
            nextState.ToString(),
            trigger,
            timestamp,
            userId,
            userRole,
            reasonForChange,
            predecessorHash);
        _transitionHistory.Add(auditRecord);

        var domainEvent = new DomainStateTransitionEvent(
            Guid.NewGuid(),
            GetType().Name,
            Id?.ToString() ?? string.Empty,
            previousState.ToString(),
            nextState.ToString(),
            trigger,
            timestamp,
            userId,
            reasonForChange);
        _domainEvents.Add(domainEvent);

        var outboxMessage = new TransactionalOutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = domainEvent.EventId,
            EventType = domainEvent.EventType,
            AggregateType = GetType().Name,
            AggregateId = Id?.ToString() ?? string.Empty,
            Payload = JsonSerializer.Serialize(domainEvent),
            CreatedAt = timestamp,
            Attempts = 0
        };
        _outboxMessages.Add(outboxMessage);
    }

    /// <inheritdoc />
    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
        _outboxMessages.Clear();
    }
}
