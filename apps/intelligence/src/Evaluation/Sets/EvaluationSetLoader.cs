using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.Kernel.Trust;

namespace Cadence.Modules.Intelligence.Evaluation.Sets;

/// <summary>The outcome of loading a set file: the set and its identity, or every reason it was refused.</summary>
/// <param name="Set">The set, when it loaded without errors.</param>
/// <param name="Ref">Its identity, with the content hash of the file's canonical JSON.</param>
/// <param name="Errors">Every rule the file broke; empty when it loaded.</param>
public sealed record EvaluationSetLoadResult(EvaluationSet? Set, EvaluationSetRef? Ref, IReadOnlyList<string> Errors)
{
    /// <summary>True when the set loaded.</summary>
    public bool Succeeded => Set is not null && Errors.Count == 0;
}

/// <summary>
/// Reads and validates evaluation set files (ADR-2634 §4.1). A set is refused, never partly used, when a case id
/// repeats, a reviewed case lacks de-identification provenance, an adversarial case lacks its category, a prompt
/// injection case has no canary, or a required citation names a node the case does not provide.
/// </summary>
public static partial class EvaluationSetLoader
{
    /// <summary>Parses and validates a set file's text.</summary>
    public static EvaluationSetLoadResult Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        EvaluationSet? set;
        string hash;
        try
        {
            hash = ContentHash.Compute(json);
            set = JsonSerializer.Deserialize<EvaluationSet>(json, EvaluationJson.Options);
        }
        catch (JsonException ex)
        {
            return new(null, null, [$"The set is not valid JSON for the evaluation set schema: {ex.Message}"]);
        }

        if (set is null)
        {
            return new(null, null, ["The set file is empty."]);
        }

        var errors = Validate(set);
        return errors.Count == 0
            ? new(set, new EvaluationSetRef(set.SetId, set.Version, hash), [])
            : new(null, null, errors);
    }

    /// <summary>
    /// Loads a set and checks it is exactly the version a task configuration pins: same id, version and content hash.
    /// </summary>
    public static EvaluationSetLoadResult LoadPinned(string json, EvaluationSetRef pinned)
    {
        ArgumentNullException.ThrowIfNull(pinned);
        var result = Load(json);
        if (!result.Succeeded)
        {
            return result;
        }

        return result.Ref == pinned
            ? result
            : new(null, null, [$"The set file is {Describe(result.Ref!)}, but the configuration pins {Describe(pinned)}."]);
    }

    /// <summary>Every rule <paramref name="set"/> breaks.</summary>
    public static IReadOnlyList<string> Validate(EvaluationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var errors = new List<string>();
        if (!KebabId().IsMatch(set.SetId))
        {
            errors.Add($"Set id '{set.SetId}' must be lower-case kebab-case.");
        }

        if (set.Version < 1)
        {
            errors.Add("Set version must be 1 or more.");
        }

        if (string.IsNullOrWhiteSpace(set.Task))
        {
            errors.Add("The set must name its task.");
        }

        if (set.Cases.Count == 0)
        {
            errors.Add("The set has no cases.");
        }

        foreach (var duplicate in set.Cases.GroupBy(c => c.CaseId, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add($"Case id '{duplicate.Key}' appears {duplicate.Count()} times.");
        }

        foreach (var c in set.Cases)
        {
            ValidateCase(c, errors);
        }

        return errors;
    }

    private static void ValidateCase(EvaluationCase c, List<string> errors)
    {
        var at = $"Case '{c.CaseId}'";
        if (string.IsNullOrWhiteSpace(c.CaseId))
        {
            errors.Add("A case has no id.");
        }

        if (string.IsNullOrWhiteSpace(c.Request))
        {
            errors.Add($"{at} has no request.");
        }

        switch (c.Kind)
        {
            case CaseKind.Reviewed:
                if (c.Provenance.Deidentification is null)
                {
                    errors.Add($"{at} is a reviewed case without a de-identification method.");
                }

                if (string.IsNullOrWhiteSpace(c.Provenance.ReviewedBy))
                {
                    errors.Add($"{at} is a reviewed case without a reviewer.");
                }

                if (!DateOnly.TryParseExact(c.Provenance.ReviewedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    errors.Add($"{at} needs a review date (yyyy-MM-dd).");
                }

                if (c.Category is not null)
                {
                    errors.Add($"{at} is a reviewed case but names an adversarial category.");
                }

                break;
            case CaseKind.Adversarial:
                if (c.Category is null)
                {
                    errors.Add($"{at} is adversarial without a category.");
                }
                else if (c.Category == AdversarialCategory.PromptInjection && c.Expectation.Forbidden.Count == 0)
                {
                    errors.Add($"{at} is a prompt injection case without a forbidden canary.");
                }

                break;
        }

        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodeRef in c.Context.Select(n => n.Ref))
        {
            if (string.IsNullOrWhiteSpace(nodeRef) || !nodeRef.Contains(':', StringComparison.Ordinal))
            {
                errors.Add($"{at} has a context node without a kind:id reference.");
            }
            else if (!refs.Add(nodeRef))
            {
                errors.Add($"{at} lists context node '{nodeRef}' twice.");
            }
        }

        foreach (var cite in c.Expectation.RequiredCitations.Where(r => !refs.Contains(r)))
        {
            errors.Add($"{at} requires citation '{cite}', which is not in its context.");
        }

        if (c.Expectation.Outcome != ExpectedOutcome.Propose && c.Expectation.Fields.Count > 0)
        {
            errors.Add($"{at} expects the agent to {c.Expectation.Outcome.ToString().ToLowerInvariant()} but also expects output fields.");
        }

        foreach (var f in c.Expectation.Fields)
        {
            if (f.Match == FieldMatch.Numeric && !double.TryParse(f.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                errors.Add($"{at} field '{f.Name}' is numeric but '{f.Value}' is not a number.");
            }

            if (f.Tolerance < 0)
            {
                errors.Add($"{at} field '{f.Name}' has a negative tolerance.");
            }
        }

        if (c.Expectation.AllowedFields is { Count: > 0 } allowed)
        {
            foreach (var f in c.Expectation.Fields.Where(f => !allowed.Contains(f.Name, StringComparer.Ordinal)))
            {
                errors.Add($"{at} expects field '{f.Name}', which its allowed fields do not include.");
            }
        }
    }

    private static string Describe(EvaluationSetRef r) => $"{r.SetId} v{r.Version} ({r.ContentHash})";

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KebabId();
}
