using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Core.Synthetic;

public static class ProtocolQualitySentinel
{
    private static readonly HashSet<string> AllowedOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        ">=", "<=", "==", "!=", ">", "<", "CONTAINS", "BETWEEN", "IN"
    };

    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "INCLUSION", "EXCLUSION"
    };

    public static CriterionValidationResult ValidateCriteria(IEnumerable<EligibilityCriterion> criteria)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (criteria == null || !criteria.Any())
        {
            errors.Add("Eligibility criteria list cannot be empty.");
            return new CriterionValidationResult(false, errors, warnings);
        }

        var criterionList = criteria.ToList();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in criterionList)
        {
            if (string.IsNullOrWhiteSpace(c.Id))
            {
                errors.Add("Criterion ID cannot be empty.");
            }
            else if (!seenIds.Add(c.Id))
            {
                errors.Add($"Duplicate criterion ID detected: '{c.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(c.Name))
            {
                errors.Add($"Criterion '{c.Id}' must have a valid Name.");
            }

            if (string.IsNullOrWhiteSpace(c.Category) || !AllowedCategories.Contains(c.Category))
            {
                errors.Add($"Criterion '{c.Id}' category '{c.Category}' is invalid. Allowed values: INCLUSION, EXCLUSION.");
            }

            if (string.IsNullOrWhiteSpace(c.VariableName))
            {
                errors.Add($"Criterion '{c.Id}' must specify a target VariableName.");
            }

            if (string.IsNullOrWhiteSpace(c.Operator) || !AllowedOperators.Contains(c.Operator))
            {
                errors.Add($"Criterion '{c.Id}' operator '{c.Operator}' is invalid or unsupported.");
            }

            if (string.Equals(c.Operator, "BETWEEN", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(c.Value) || string.IsNullOrWhiteSpace(c.HighValue))
                {
                    errors.Add($"Criterion '{c.Id}' with operator BETWEEN requires both Value and HighValue bounds.");
                }
            }
            else if (string.IsNullOrWhiteSpace(c.Value))
            {
                warnings.Add($"Criterion '{c.Id}' has an empty Value. Will evaluate against default null semantics.");
            }
        }

        return new CriterionValidationResult(errors.Count == 0, errors, warnings);
    }
}
