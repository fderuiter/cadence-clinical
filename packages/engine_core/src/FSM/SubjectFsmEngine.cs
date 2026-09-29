using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain.Rules;

namespace Cadence.Core.Domain.Fsm;

/// <summary>
/// Generic, declarative Finite State Machine and DAG Lifecycle Engine for clinical subjects.
/// Executes declarative state transitions with dynamic rule expression guards,
/// role-based authorization gates, Part 11 reason-for-change justification,
/// and cryptographic audit ledger sealing.
/// @req:PRD-SYS-053
/// </summary>
public class SubjectFsmEngine
{
    private readonly StateMachineDefinition _definition;
    private readonly Dictionary<string, StateNode> _states;
    private readonly Dictionary<string, List<TransitionEdge>> _transitionsByFromState;
    private readonly RuleEvaluator _ruleEvaluator;

    public StateMachineDefinition Definition => _definition;
    public string MachineId => _definition.MachineId;
    public int VersionIndex => _definition.VersionIndex;
    public string InitialState => _definition.InitialState;

    public SubjectFsmEngine(StateMachineDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _states = definition.States.ToDictionary(s => s.Name, s => s);
        _transitionsByFromState = new Dictionary<string, List<TransitionEdge>>();
        _ruleEvaluator = new RuleEvaluator();

        foreach (var edge in definition.Transitions)
        {
            if (!_transitionsByFromState.TryGetValue(edge.FromState, out var list))
            {
                list = new List<TransitionEdge>();
                _transitionsByFromState[edge.FromState] = list;
            }
            list.Add(edge);
        }
    }

    public bool IsValidState(string state) => _states.ContainsKey(state);

    public StateType? GetStateType(string state) =>
        _states.TryGetValue(state, out var node) ? node.StateType : null;

    public bool IsTerminalState(string state)
    {
        var st = GetStateType(state);
        return st == StateType.Terminal || st == StateType.Error;
    }

    public List<TransitionEdge> GetAvailableTransitions(
        string currentState,
        IReadOnlyDictionary<string, object?>? context = null,
        IEnumerable<string>? roles = null)
    {
        if (!_transitionsByFromState.TryGetValue(currentState, out var candidateEdges))
        {
            return new List<TransitionEdge>();
        }

        var dict = context is Dictionary<string, object?> d ? d : new Dictionary<string, object?>(context ?? new Dictionary<string, object?>());
        var userRolesLower = (roles ?? Array.Empty<string>())
            .Select(r => r.Trim().ToLowerInvariant())
            .ToHashSet();

        var validEdges = new List<TransitionEdge>();
        foreach (var edge in candidateEdges)
        {
            if (edge.AllowedRoles.Count > 0)
            {
                var edgeRolesLower = edge.AllowedRoles
                    .Select(r => r.Trim().ToLowerInvariant())
                    .ToHashSet();

                if (!userRolesLower.Overlaps(edgeRolesLower))
                {
                    continue;
                }
            }

            if (edge.Guard != null)
            {
                try
                {
                    object? result = _ruleEvaluator.Evaluate(edge.Guard, dict);
                    if (result is not bool b || !b)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }
            }

            validEdges.Add(edge);
        }

        return validEdges;
    }

    public (bool IsAllowed, string? Reason, TransitionEdge? MatchingEdge) CanTransition(
        string currentState,
        string trigger,
        IReadOnlyDictionary<string, object?>? context = null,
        IEnumerable<string>? roles = null)
    {
        if (!IsValidState(currentState))
        {
            return (false, $"Invalid current state: '{currentState}'", null);
        }

        if (!_transitionsByFromState.TryGetValue(currentState, out var candidateEdges) || candidateEdges.Count == 0)
        {
            return (false, $"No transition defined from state '{currentState}' on trigger '{trigger}'", null);
        }

        var matchingEdges = candidateEdges.Where(e => e.Trigger == trigger).ToList();
        if (matchingEdges.Count == 0)
        {
            return (false, $"No transition defined from state '{currentState}' on trigger '{trigger}'", null);
        }

        var dict = context is Dictionary<string, object?> d ? d : new Dictionary<string, object?>(context ?? new Dictionary<string, object?>());
        var userRolesLower = (roles ?? Array.Empty<string>())
            .Select(r => r.Trim().ToLowerInvariant())
            .ToHashSet();

        string? lastFailureReason = null;
        TransitionEdge? lastEdge = null;

        foreach (var edge in matchingEdges)
        {
            lastEdge = edge;

            // 1. Role verification
            if (edge.AllowedRoles.Count > 0)
            {
                var edgeRolesLower = edge.AllowedRoles
                    .Select(r => r.Trim().ToLowerInvariant())
                    .ToHashSet();

                if (!userRolesLower.Overlaps(edgeRolesLower))
                {
                    string joinedUserRoles = string.Join(", ", roles ?? Array.Empty<string>());
                    string joinedAllowedRoles = string.Join(", ", edge.AllowedRoles);
                    lastFailureReason = $"User roles [{joinedUserRoles}] not authorized for transition '{edge.Trigger}' (requires [{joinedAllowedRoles}])";
                    continue;
                }
            }

            // 2. Guard expression verification
            if (edge.Guard != null)
            {
                try
                {
                    object? result = _ruleEvaluator.Evaluate(edge.Guard, dict);
                    if (result is not bool b || !b)
                    {
                        lastFailureReason = $"Guard condition failed for transition to '{edge.ToState}'";
                        continue;
                    }
                }
                catch (Exception exc)
                {
                    lastFailureReason = $"Guard evaluation error for transition to '{edge.ToState}': {exc.Message}";
                    continue;
                }
            }

            return (true, null, edge);
        }

        return (false, lastFailureReason ?? "Transition conditions not satisfied", lastEdge);
    }

    public TransitionResult Transition(TransitionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string currState = context.CurrentState;
        string trigger = context.Trigger;

        var (isAllowed, reason, edge) = CanTransition(
            currentState: currState,
            trigger: trigger,
            context: context.Payload,
            roles: context.UserRoles
        );

        if (!isAllowed || edge == null)
        {
            return new TransitionResult(
                Success: false,
                FromState: currState,
                ToState: currState,
                Trigger: trigger,
                ErrorMessage: reason ?? "Transition rejected"
            );
        }

        // Enforce 21 CFR Part 11 reason-for-change validation
        if (edge.RequiresReasonForChange &&
            (string.IsNullOrWhiteSpace(context.ReasonForChange) ||
             context.ReasonForChange.Trim().Length < edge.MinReasonLength))
        {
            return new TransitionResult(
                Success: false,
                FromState: currState,
                ToState: currState,
                Trigger: trigger,
                ErrorMessage: $"GxP compliance requires a valid reason_for_change (minimum {edge.MinReasonLength} characters, got '{context.ReasonForChange}')"
            );
        }

        // Enforce electronic signature requirement
        if (edge.RequiresSignature && string.IsNullOrWhiteSpace(context.SignatureToken))
        {
            return new TransitionResult(
                Success: false,
                FromState: currState,
                ToState: currState,
                Trigger: trigger,
                ErrorMessage: "Transition requires a valid 21 CFR Part 11 electronic signature token"
            );
        }

        // Build sealed Part 11 audit envelope
        var auditEnv = new Part11AuditEnvelope(
            machineId: MachineId,
            machineVersion: VersionIndex,
            fromState: currState,
            toState: edge.ToState,
            trigger: trigger,
            userId: context.UserId,
            userRoles: context.UserRoles,
            reasonForChange: context.ReasonForChange ?? "State transition execution",
            signatureVerified: !string.IsNullOrWhiteSpace(context.SignatureToken)
        );

        return new TransitionResult(
            Success: true,
            FromState: currState,
            ToState: edge.ToState,
            Trigger: trigger,
            ErrorMessage: null,
            AuditEnvelope: auditEnv,
            ExecutedActionHooks: edge.ActionHooks
        );
    }
}
