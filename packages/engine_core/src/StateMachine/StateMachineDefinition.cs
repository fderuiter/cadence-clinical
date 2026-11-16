using System;
using System.Collections.Generic;

namespace Cadence.Core.Domain;

/// <summary>
/// Internal transition edge definition in a state machine.
/// </summary>
public sealed record StateTransitionRule<TState, TTrigger>(
    TState From,
    TTrigger Trigger,
    TState To,
    Func<TransitionContext, bool>? Guard = null,
    string? GuardFailureMessage = null)
    where TState : struct, Enum
    where TTrigger : struct, Enum;

/// <summary>
/// Immutable, declarative state machine definition validating lifecycle transitions.
/// Executes synchronously in memory without external dependencies (PRD-SYS-001).
/// </summary>
public sealed class StateMachineDefinition<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private readonly Dictionary<(TState From, TTrigger Trigger), StateTransitionRule<TState, TTrigger>> _byTrigger = new();
    private readonly Dictionary<(TState From, TState To), StateTransitionRule<TState, TTrigger>> _byTarget = new();
    private readonly HashSet<TState> _terminalStates = new();
    private readonly Dictionary<(TState From, TState To), string> _customInvalidTransitionErrors = new();

    public TState InitialState { get; }

    internal StateMachineDefinition(
        TState initialState,
        IEnumerable<StateTransitionRule<TState, TTrigger>> rules,
        IEnumerable<TState> terminalStates,
        IEnumerable<KeyValuePair<(TState From, TState To), string>> customErrors)
    {
        InitialState = initialState;
        foreach (var rule in rules)
        {
            _byTrigger[(rule.From, rule.Trigger)] = rule;
            _byTarget[(rule.From, rule.To)] = rule;
        }
        foreach (var term in terminalStates)
        {
            _terminalStates.Add(term);
        }
        foreach (var err in customErrors)
        {
            _customInvalidTransitionErrors[err.Key] = err.Value;
        }
    }

    /// <summary>Checks whether a given state is terminal.</summary>
    public bool IsTerminal(TState state) => _terminalStates.Contains(state);

    /// <summary>Checks whether a trigger can be fired from the current state.</summary>
    public bool CanFire(TState currentState, TTrigger trigger, TransitionContext? context = null)
    {
        if (IsTerminal(currentState)) return false;
        if (!_byTrigger.TryGetValue((currentState, trigger), out var rule)) return false;
        return rule.Guard == null || rule.Guard(context ?? new TransitionContext("system"));
    }

    /// <summary>Checks whether a direct transition to targetState is allowed.</summary>
    public bool CanTransitionTo(TState currentState, TState targetState, TransitionContext? context = null)
    {
        if (EqualityComparer<TState>.Default.Equals(currentState, targetState)) return true;
        if (IsTerminal(currentState)) return false;
        if (!_byTarget.TryGetValue((currentState, targetState), out var rule)) return false;
        return rule.Guard == null || rule.Guard(context ?? new TransitionContext("system"));
    }

    /// <summary>Returns next state for a valid trigger, or a structured domain error.</summary>
    public Result<TState> ValidateTransition(TState currentState, TTrigger trigger, TransitionContext? context = null)
    {
        if (IsTerminal(currentState))
        {
            return DomainError.Validation(
                "ILLEGAL_STATE_TRANSITION",
                $"State '{currentState}' is terminal and cannot transition to any subsequent state.");
        }

        if (!_byTrigger.TryGetValue((currentState, trigger), out var rule))
        {
            return DomainError.Validation(
                "INVALID_STATE_TRANSITION",
                $"No transition configured from state '{currentState}' on trigger '{trigger}'.");
        }

        var ctx = context ?? new TransitionContext("system");
        if (rule.Guard != null && !rule.Guard(ctx))
        {
            return DomainError.Validation(
                "STATE_TRANSITION_GUARD_FAILED",
                rule.GuardFailureMessage ?? $"Transition from '{currentState}' on trigger '{trigger}' blocked by guard condition.");
        }

        return rule.To;
    }

    /// <summary>Returns target state if transition is allowed, or a structured domain error.</summary>
    public Result<TState> ValidateTransitionTo(TState currentState, TState targetState, TransitionContext? context = null)
    {
        if (EqualityComparer<TState>.Default.Equals(currentState, targetState))
        {
            return currentState; // same-state no-op
        }

        if (IsTerminal(currentState))
        {
            return DomainError.Validation(
                "ILLEGAL_STATE_TRANSITION",
                $"State '{currentState}' is terminal and cannot transition to any subsequent state.");
        }

        if (!_byTarget.TryGetValue((currentState, targetState), out var rule))
        {
            if (_customInvalidTransitionErrors.TryGetValue((currentState, targetState), out string? customDetail))
            {
                return DomainError.Validation("INVALID_STATE_TRANSITION", customDetail);
            }

            return DomainError.Validation(
                "INVALID_STATE_TRANSITION",
                $"Transition from '{currentState}' to '{targetState}' is not allowed.");
        }

        var ctx = context ?? new TransitionContext("system");
        if (rule.Guard != null && !rule.Guard(ctx))
        {
            return DomainError.Validation(
                "STATE_TRANSITION_GUARD_FAILED",
                rule.GuardFailureMessage ?? $"Transition from '{currentState}' to '{targetState}' blocked by guard condition.");
        }

        return targetState;
    }

    /// <summary>Returns trigger for moving from currentState to targetState, if configured.</summary>
    public bool TryGetTriggerFor(TState currentState, TState targetState, out TTrigger trigger)
    {
        if (_byTarget.TryGetValue((currentState, targetState), out var rule))
        {
            trigger = rule.Trigger;
            return true;
        }

        trigger = default;
        return false;
    }
}

/// <summary>
/// Fluent builder for declarative <see cref="StateMachineDefinition{TState, TTrigger}"/>.
/// </summary>
public sealed class StateMachineBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    private TState _initialState;
    private readonly List<StateTransitionRule<TState, TTrigger>> _rules = [];
    private readonly HashSet<TState> _terminalStates = [];
    private readonly Dictionary<(TState From, TState To), string> _customErrors = [];

    public StateMachineBuilder(TState initialState = default)
    {
        _initialState = initialState;
    }

    public StateMachineBuilder<TState, TTrigger> WithInitialState(TState initialState)
    {
        _initialState = initialState;
        return this;
    }

    public StateMachineBuilder<TState, TTrigger> WithTerminalStates(params TState[] terminalStates)
    {
        foreach (var s in terminalStates)
        {
            _terminalStates.Add(s);
        }
        return this;
    }

    public StateMachineBuilder<TState, TTrigger> OnInvalidTransition(TState from, TState to, string actionableErrorMessage)
    {
        _customErrors[(from, to)] = actionableErrorMessage;
        return this;
    }

    public StateConfigurator In(TState from) => new(this, from);

    public StateMachineBuilder<TState, TTrigger> Permit(TState from, TTrigger trigger, TState to)
    {
        _rules.Add(new StateTransitionRule<TState, TTrigger>(from, trigger, to));
        return this;
    }

    public StateMachineBuilder<TState, TTrigger> PermitIf(
        TState from,
        TTrigger trigger,
        TState to,
        Func<TransitionContext, bool> guard,
        string? guardFailureMessage = null)
    {
        _rules.Add(new StateTransitionRule<TState, TTrigger>(from, trigger, to, guard, guardFailureMessage));
        return this;
    }

    public StateMachineDefinition<TState, TTrigger> Build() =>
        new(_initialState, _rules, _terminalStates, _customErrors);

    public sealed class StateConfigurator(StateMachineBuilder<TState, TTrigger> builder, TState from)
    {
        public StateConfigurator Permit(TTrigger trigger, TState to)
        {
            builder.Permit(from, trigger, to);
            return this;
        }

        public StateConfigurator PermitIf(
            TTrigger trigger,
            TState to,
            Func<TransitionContext, bool> guard,
            string? guardFailureMessage = null)
        {
            builder.PermitIf(from, trigger, to, guard, guardFailureMessage);
            return this;
        }

        public StateConfigurator In(TState nextFrom) => builder.In(nextFrom);

        public StateMachineBuilder<TState, TTrigger> OnInvalidTransition(TState fromState, TState toState, string actionableErrorMessage) =>
            builder.OnInvalidTransition(fromState, toState, actionableErrorMessage);

        public StateMachineBuilder<TState, TTrigger> WithTerminalStates(params TState[] terminalStates) =>
            builder.WithTerminalStates(terminalStates);

        public StateMachineBuilder<TState, TTrigger> And() => builder;
        public StateMachineDefinition<TState, TTrigger> Build() => builder.Build();
    }
}
