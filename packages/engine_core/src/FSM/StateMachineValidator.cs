using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Cadence.Core.Domain.Fsm;

/// <summary>
/// Static Graph Topology Validator for State Machine Definitions.
/// @req:PRD-SYS-053
/// </summary>
public static class StateMachineValidator
{
    public static List<string> ValidateStateMachineGraph(StateMachineDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<string>();
        var stateNames = definition.States.Select(s => s.Name).ToHashSet();

        // 1. Verify initial state
        if (!stateNames.Contains(definition.InitialState))
        {
            errors.Add($"Initial state '{definition.InitialState}' is not defined in states list");
        }

        // 2. Verify all transitions reference defined states
        var adj = stateNames.ToDictionary(s => s, _ => new List<string>());
        var reverseAdj = stateNames.ToDictionary(s => s, _ => new List<string>());
        var seenTransitions = new HashSet<string>();

        for (int idx = 0; idx < definition.Transitions.Count; idx++)
        {
            var edge = definition.Transitions[idx];
            if (!stateNames.Contains(edge.FromState))
            {
                errors.Add($"Transition #{idx} references unknown from_state: '{edge.FromState}'");
            }
            if (!stateNames.Contains(edge.ToState))
            {
                errors.Add($"Transition #{idx} references unknown to_state: '{edge.ToState}'");
            }

            if (stateNames.Contains(edge.FromState) && stateNames.Contains(edge.ToState))
            {
                adj[edge.FromState].Add(edge.ToState);
                reverseAdj[edge.ToState].Add(edge.FromState);

                string guardKey = edge.Guard != null ? JsonSerializer.Serialize(edge.Guard) : "null";
                string key = $"{edge.FromState}|{edge.Trigger}|{guardKey}";
                if (seenTransitions.Contains(key))
                {
                    errors.Add($"Non-deterministic duplicate transition from '{edge.FromState}' on trigger '{edge.Trigger}'");
                }
                seenTransitions.Add(key);
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        // 3. Check for unreachable / orphan states (BFS from initial state)
        var visitedForward = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(definition.InitialState);
        visitedForward.Add(definition.InitialState);

        while (queue.Count > 0)
        {
            string curr = queue.Dequeue();
            if (adj.TryGetValue(curr, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (!visitedForward.Contains(neighbor))
                    {
                        visitedForward.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        var unreachable = stateNames.Except(visitedForward).OrderBy(s => s).ToList();
        if (unreachable.Count > 0)
        {
            errors.Add($"Unreachable states detected: [{string.Join(", ", unreachable)}]");
        }

        // 4. Check for dead-end non-terminal states
        var terminalStates = definition.States
            .Where(s => s.StateType == StateType.Terminal || s.StateType == StateType.Error)
            .Select(s => s.Name)
            .ToHashSet();

        foreach (var s in definition.States.Where(s => visitedForward.Contains(s.Name) && s.StateType != StateType.Terminal && s.StateType != StateType.Error && adj[s.Name].Count == 0))
        {
            errors.Add($"Non-terminal state '{s.Name}' has no outgoing transitions (deadlock trap)");
        }

        // 5. Check that at least one terminal state is reachable from reachable states
        if (terminalStates.Count > 0)
        {
            var canReachTerminal = new HashSet<string>(terminalStates);
            var termQueue = new Queue<string>(terminalStates);

            while (termQueue.Count > 0)
            {
                string curr = termQueue.Dequeue();
                if (reverseAdj.TryGetValue(curr, out var preds))
                {
                    foreach (var pred in preds)
                    {
                        if (!canReachTerminal.Contains(pred))
                        {
                            canReachTerminal.Add(pred);
                            termQueue.Enqueue(pred);
                        }
                    }
                }
            }

            var deadlocks = visitedForward.Except(canReachTerminal).OrderBy(s => s).ToList();
            if (deadlocks.Count > 0)
            {
                errors.Add($"Reachable states cannot reach any terminal state: [{string.Join(", ", deadlocks)}]");
            }
        }

        return errors;
    }
}
