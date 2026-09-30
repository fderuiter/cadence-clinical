using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Cadence.Kernel.Standards;

namespace Cadence.Kernel.Domain.Standards;

/// <summary>
/// Structural validation, normalization and the content hash of a <see cref="StandardPackage"/>. The hash covers the
/// normalized package (datasets by ordinal then name, variables by ordinal then name, terminology by code system and
/// version, value-level definitions in the given order) serialized canonically, so reordering the input never
/// changes it and any change of content does.
/// </summary>
public static partial class StandardPackageContent
{
    /// <summary>Longest code, version, dataset or variable name the registry stores.</summary>
    public const int MaxNameLength = 64;

    private static readonly JsonSerializerOptions HashOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Every structural problem with the package; empty when it is valid.</summary>
    public static IReadOnlyList<string> Validate(StandardPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var problems = new List<string>();

        RequireName(problems, "Standard code", package.StandardCode);
        RequireName(problems, "Version", package.Version);
        if (string.IsNullOrWhiteSpace(package.Name))
        {
            problems.Add("Name is required.");
        }

        if (string.IsNullOrWhiteSpace(package.Publisher))
        {
            problems.Add("Publisher is required.");
        }

        if (package.Datasets is not { Count: > 0 })
        {
            problems.Add("A standard version needs at least one dataset.");
            return problems;
        }

        var variablesByDataset = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var dataset in package.Datasets)
        {
            RequireName(problems, "Dataset name", dataset.Name);
            if (!variablesByDataset.TryAdd(dataset.Name ?? string.Empty, new HashSet<string>(StringComparer.Ordinal)))
            {
                problems.Add($"Dataset {dataset.Name} appears more than once.");
                continue;
            }

            if (dataset.Variables is not { Count: > 0 })
            {
                problems.Add($"Dataset {dataset.Name} has no variables.");
                continue;
            }

            foreach (var variable in dataset.Variables)
            {
                RequireName(problems, $"Variable name in {dataset.Name}", variable.Name);
                if (!variablesByDataset[dataset.Name!].Add(variable.Name ?? string.Empty))
                {
                    problems.Add($"Variable {dataset.Name}.{variable.Name} appears more than once.");
                }

                CheckCodelists(problems, $"{dataset.Name}.{variable.Name}", variable.Codelists);
                if (variable.ValueList is null)
                {
                    problems.Add($"Variable {dataset.Name}.{variable.Name} needs a value list (empty when none).");
                }
            }
        }

        foreach (var vlm in package.ValueLevelDefinitions ?? [])
        {
            var target = $"{vlm.Dataset}.{vlm.Variable}";
            if (!variablesByDataset.TryGetValue(vlm.Dataset ?? string.Empty, out var variables) || !variables.Contains(vlm.Variable ?? string.Empty))
            {
                problems.Add($"Value-level definition targets {target}, which the package does not define.");
                continue;
            }

            if (vlm.Where is not { Count: > 0 })
            {
                problems.Add($"Value-level definition for {target} has no where clause.");
            }
            else
            {
                foreach (var condition in vlm.Where)
                {
                    if (!variables.Contains(condition.Variable ?? string.Empty))
                    {
                        problems.Add($"Value-level definition for {target} conditions on {condition.Variable}, which {vlm.Dataset} does not define.");
                    }

                    if (condition.Values is not { Count: > 0 } || condition.Values.Any(string.IsNullOrEmpty))
                    {
                        problems.Add($"Value-level definition for {target} has a condition on {condition.Variable} without values.");
                    }
                }
            }

            if (vlm.Length is <= 0)
            {
                problems.Add($"Value-level definition for {target} has a length that is not positive.");
            }

            CheckCodelists(problems, $"value-level definition for {target}", vlm.Codelists);
        }

        foreach (var ct in package.ControlledTerminology ?? [])
        {
            if (string.IsNullOrWhiteSpace(ct.CodeSystem) || string.IsNullOrWhiteSpace(ct.Version))
            {
                problems.Add("A controlled-terminology reference needs a code system and a version.");
            }
        }

        return problems;
    }

    /// <summary>The package in normalized order, with duplicate terminology references removed.</summary>
    public static StandardPackage Normalize(StandardPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return package with
        {
            Datasets = [.. package.Datasets
                .OrderBy(d => d.Ordinal).ThenBy(d => d.Name, StringComparer.Ordinal)
                .Select(d => d with
                {
                    Variables = [.. d.Variables.OrderBy(v => v.Ordinal).ThenBy(v => v.Name, StringComparer.Ordinal)],
                })],
            ValueLevelDefinitions = [.. package.ValueLevelDefinitions ?? []],
            ControlledTerminology = [.. (package.ControlledTerminology ?? []).Distinct()
                .OrderBy(t => t.CodeSystem, StringComparer.Ordinal).ThenBy(t => t.Version, StringComparer.Ordinal)],
        };
    }

    /// <summary>The content hash of a normalized package.</summary>
    public static string Hash(StandardPackage normalized)
    {
        ArgumentNullException.ThrowIfNull(normalized);
        return CanonicalJson.Hash(JsonSerializer.SerializeToElement(normalized, HashOptions));
    }

    private static void RequireName(List<string> problems, string what, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{what} is required.");
        }
        else if (value.Length > MaxNameLength || value.Trim().Length != value.Length)
        {
            problems.Add($"{what} '{value}' must be at most {MaxNameLength} characters with no surrounding spaces.");
        }
    }

    private static void CheckCodelists(List<string> problems, string owner, IReadOnlyList<string>? codelists)
    {
        if (codelists is null)
        {
            problems.Add($"{owner} needs a codelist list (empty when none).");
            return;
        }

        foreach (var code in codelists.Where(c => !CodelistCode().IsMatch(c ?? string.Empty)))
        {
            problems.Add($"{owner} names codelist '{code}', which is not an NCI C-code.");
        }
    }

    [GeneratedRegex("^C[0-9]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodelistCode();
}
