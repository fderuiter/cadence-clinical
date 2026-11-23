using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.Cli.Services;

/// <summary>One parsed step from a GitHub Actions workflow YAML file.</summary>
/// <param name="Name">The step display name (or step key fallback when unnamed).</param>
/// <param name="If">The step's <c>if:</c> expression, when declared.</param>
/// <param name="Run">The step's <c>run:</c> script or mapped <see cref="WorkflowYamlParser.MappedUsesCommands"/> command.</param>
/// <param name="Uses">The unpinned action slug (<c>owner/repo</c>) when the step uses <c>uses:</c>.</param>
public sealed record WorkflowStep(string Name, string? If, string? Run, string? Uses = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyEnvironment =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The step's <c>id:</c> identifier, when declared.</summary>
    public string? Id { get; init; }

    /// <summary>Key-value entries declared under the step's <c>env:</c> block.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = EmptyEnvironment;
}

/// <summary>One parsed job from a GitHub Actions workflow YAML file.</summary>
/// <param name="Key">The YAML job identifier under <c>jobs:</c>.</param>
/// <param name="Name">The job's <c>name:</c> display label (defaults to <paramref name="Key"/> when omitted).</param>
/// <param name="If">The job's <c>if:</c> expression, folded to a single line when declared.</param>
/// <param name="Needs">Job keys listed under <c>needs:</c>, in declaration order.</param>
/// <param name="Steps">Steps declared under <c>steps:</c>, in declaration order.</param>
public sealed record WorkflowJob(
    string Key,
    string Name,
    string? If,
    IReadOnlyList<string> Needs,
    IReadOnlyList<WorkflowStep> Steps)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyDictionary =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Expressions declared under the job's <c>outputs:</c> mapping.</summary>
    public IReadOnlyDictionary<string, string> Outputs { get; init; } = EmptyDictionary;

    /// <summary>Key-value entries declared under the job's <c>env:</c> mapping.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = EmptyDictionary;
}

/// <summary>
/// Indentation-aware, Native AOT-safe parser for two-space-indented GitHub Actions workflow YAML files such as
/// <c>.github/workflows/ci.yml</c>. Uses source-generated regular expressions only (zero reflection).
/// </summary>
public static partial class WorkflowYamlParser
{
    /// <summary>
    /// GitHub Actions <c>uses:</c> slugs whose invocation maps directly to a local CLI binary in <see cref="CiPlan"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MappedUsesCommands { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rhysd/actionlint"] = "actionlint",
            ["docker/build-push-action"] = "docker build",
            ["aquasecurity/trivy-action"] = "trivy image",
        };

    private enum JobMappingSection
    {
        None,
        Outputs,
        Env,
    }

    /// <summary>Parses jobs, dependencies, conditions, outputs, environment maps, and steps from workflow YAML lines.</summary>
    public static List<WorkflowJob> Parse(string[] lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var jobs = new List<WorkflowJob>();
        bool inJobs = false;
        string? key = null, name = null, jobIf = null;
        IReadOnlyList<string> needs = [];
        var jobOutputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var jobEnv = new Dictionary<string, string>(StringComparer.Ordinal);
        var steps = new List<WorkflowStep>();

        string? stepName = null, stepId = null, stepIf = null, stepRun = null, stepUses = null;
        var stepEnv = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inStep = false;
        bool inStepEnv = false;
        JobMappingSection jobSection = JobMappingSection.None;

        void FlushStep()
        {
            if (inStep)
            {
                string? effectiveName = stepName ?? (stepRun is not null || stepUses is not null ? (stepId ?? stepUses ?? "unnamed") : null);
                if (effectiveName is not null)
                {
                    steps.Add(new WorkflowStep(effectiveName, stepIf, stepRun, stepUses)
                    {
                        Id = stepId,
                        Environment = stepEnv.Count == 0
                            ? new Dictionary<string, string>(StringComparer.Ordinal)
                            : new Dictionary<string, string>(stepEnv, StringComparer.Ordinal),
                    });
                }
            }

            inStep = false;
            inStepEnv = false;
            stepName = stepId = stepIf = stepUses = stepRun = null;
            stepEnv.Clear();
        }

        void FlushJob()
        {
            FlushStep();
            if (key is not null)
            {
                jobs.Add(new WorkflowJob(key, name ?? key, jobIf, needs, [.. steps])
                {
                    Outputs = jobOutputs.Count == 0
                        ? new Dictionary<string, string>(StringComparer.Ordinal)
                        : new Dictionary<string, string>(jobOutputs, StringComparer.Ordinal),
                    Environment = jobEnv.Count == 0
                        ? new Dictionary<string, string>(StringComparer.Ordinal)
                        : new Dictionary<string, string>(jobEnv, StringComparer.Ordinal),
                });
            }

            key = name = jobIf = null;
            needs = [];
            jobOutputs.Clear();
            jobEnv.Clear();
            steps.Clear();
            jobSection = JobMappingSection.None;
        }

        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i++];
            if (line == "jobs:")
            {
                inJobs = true;
                continue;
            }

            if (!inJobs)
            {
                continue;
            }

            if (line.Length > 0 && line[0] != ' ' && line[0] != '#')
            {
                FlushJob();
                inJobs = false;
                continue;
            }

            if (TryMatch(JobKeyRegex(), line, out string jobKey))
            {
                FlushJob();
                key = jobKey;
                continue;
            }

            if (key is null)
            {
                continue;
            }

            if (TryMatch(StepDashRegex(), line, out string firstKey))
            {
                FlushStep();
                jobSection = JobMappingSection.None;
                inStep = true;
                ParseStepInlineField(firstKey, lines, ref i, ref stepName, ref stepId, ref stepIf, ref stepUses, ref stepRun, ref inStepEnv);
                continue;
            }

            if (!inStep)
            {
                if (TryMatch(JobPropertyRegex(), line, out string propName, out string propValue))
                {
                    jobSection = JobMappingSection.None;
                    switch (propName)
                    {
                        case "name":
                            name = Unquote(propValue);
                            break;
                        case "if":
                            jobIf = ParseScalarOrFolded(propValue, lines, ref i, "      ");
                            break;
                        case "needs":
                            needs = ParseNeeds(propValue, lines, ref i);
                            break;
                        case "outputs":
                            jobSection = JobMappingSection.Outputs;
                            break;
                        case "env":
                            jobSection = JobMappingSection.Env;
                            break;
                    }

                    continue;
                }

                if (jobSection != JobMappingSection.None && TryMatch(JobSubEntryRegex(), line, out string subKey, out string subVal))
                {
                    string cleaned = Unquote(StripUnquotedInlineComment(subVal));
                    if (jobSection == JobMappingSection.Outputs)
                    {
                        jobOutputs[subKey] = cleaned;
                    }
                    else if (jobSection == JobMappingSection.Env)
                    {
                        jobEnv[subKey] = cleaned;
                    }
                }

                continue;
            }

            if (inStepEnv)
            {
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                if (TryMatch(StepEnvEntryRegex(), line, out string envKey, out string envVal))
                {
                    stepEnv[envKey] = Unquote(StripUnquotedInlineComment(envVal));
                    continue;
                }

                inStepEnv = false;
            }

            if (TryMatch(StepPropertyRegex(), line, out string stepProp, out string stepVal))
            {
                ParseStepProperty(stepProp, stepVal, lines, ref i, ref stepName, ref stepId, ref stepIf, ref stepUses, ref stepRun, ref inStepEnv);
            }
        }

        FlushJob();
        return jobs;
    }

    private static void ParseStepInlineField(
        string firstKey,
        string[] lines,
        ref int index,
        ref string? stepName,
        ref string? stepId,
        ref string? stepIf,
        ref string? stepUses,
        ref string? stepRun,
        ref bool inStepEnv)
    {
        if (TryMatch(InlinePropertyRegex(), firstKey, out string prop, out string val))
        {
            ParseStepProperty(prop, val, lines, ref index, ref stepName, ref stepId, ref stepIf, ref stepUses, ref stepRun, ref inStepEnv);
        }
    }

    private static void ParseStepProperty(
        string prop,
        string val,
        string[] lines,
        ref int index,
        ref string? stepName,
        ref string? stepId,
        ref string? stepIf,
        ref string? stepUses,
        ref string? stepRun,
        ref bool inStepEnv)
    {
        switch (prop)
        {
            case "name":
                stepName = Unquote(val);
                break;
            case "id":
                stepId = Unquote(StripUnquotedInlineComment(val));
                break;
            case "if":
                stepIf = ParseScalarOrFolded(val, lines, ref index, "          ");
                break;
            case "uses":
                if (TryMatch(UsesSlugRegex(), val, out string action))
                {
                    stepUses = action;
                    if (MappedUsesCommands.TryGetValue(action, out string? mappedRun))
                    {
                        stepRun = mappedRun;
                    }
                }

                break;
            case "env":
                inStepEnv = true;
                break;
            case "run":
                stepRun = ParseRunValue(val, lines, ref index);
                break;
        }
    }

    private static string ParseRunValue(string rawRun, string[] lines, ref int index)
    {
        string indicator = StripUnquotedInlineComment(rawRun).Trim();
        if (indicator is "|" or "|-" or "|+")
        {
            var block = new List<string>();
            while (index < lines.Length && (lines[index].Trim().Length == 0 || lines[index].StartsWith("          ", StringComparison.Ordinal)))
            {
                block.Add(lines[index].Trim());
                index++;
            }

            return string.Join('\n', block);
        }

        if (indicator is ">" or ">-")
        {
            return ParseScalarOrFolded(indicator, lines, ref index, "          ");
        }

        return rawRun;
    }

    private static string ParseScalarOrFolded(string head, string[] lines, ref int index, string indent)
    {
        string trimmed = StripUnquotedInlineComment(head).Trim();
        if (trimmed is not ">-" and not ">" and not "|" and not "|-")
        {
            return head;
        }

        var parts = new List<string>();
        while (index < lines.Length && (lines[index].Trim().Length == 0 || lines[index].StartsWith(indent, StringComparison.Ordinal)))
        {
            string part = lines[index++].Trim();
            if (part.Length > 0)
            {
                parts.Add(part);
            }
        }

        return string.Join(' ', parts);
    }

    private static IReadOnlyList<string> ParseNeeds(string rawNeeds, string[] lines, ref int index)
    {
        string value = StripUnquotedInlineComment(rawNeeds).Trim();
        if (value.Length == 0 && index < lines.Length && lines[index].TrimStart().StartsWith('['))
        {
            value = StripUnquotedInlineComment(lines[index++]).Trim();
        }

        if (value.StartsWith('['))
        {
            var buffer = new List<string> { value };
            while (!value.Contains(']') && index < lines.Length)
            {
                value = StripUnquotedInlineComment(lines[index++]).Trim();
                buffer.Add(value);
            }

            string joined = string.Join(' ', buffer);
            int open = joined.IndexOf('[');
            int close = joined.LastIndexOf(']');
            string inner = open >= 0 && close > open ? joined[(open + 1)..close] : joined.Trim('[', ']');
            return [.. inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote)];
        }

        if (value.Length == 0)
        {
            var items = new List<string>();
            while (index < lines.Length && TryMatch(NeedsListItemRegex(), lines[index], out string item))
            {
                items.Add(Unquote(StripUnquotedInlineComment(item)));
                index++;
            }

            return items;
        }

        return [Unquote(value)];
    }

    private static string StripUnquotedInlineComment(string line)
    {
        bool inSingle = false;
        bool inDouble = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
            else if (c == '"' && !inSingle && (i == 0 || line[i - 1] != '\\'))
            {
                inDouble = !inDouble;
            }
            else if (c == '#' && !inSingle && !inDouble && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                return line[..i];
            }
        }

        return line;
    }

    private static bool TryMatch(Regex regex, string input, out string group1)
    {
        Match match = regex.Match(input);
        group1 = match.Success ? match.Groups[1].Value : "";
        return match.Success;
    }

    private static bool TryMatch(Regex regex, string input, out string group1, out string group2)
    {
        Match match = regex.Match(input);
        if (!match.Success)
        {
            group1 = "";
            group2 = "";
            return false;
        }

        group1 = match.Groups[1].Value;
        group2 = match.Groups[2].Value;
        return true;
    }

    private static string Unquote(string value) => value.Trim().Trim('"', '\'');

    [GeneratedRegex(@"^  ([A-Za-z0-9_-]+):\s*$")]
    private static partial Regex JobKeyRegex();

    [GeneratedRegex(@"^    ([A-Za-z0-9_-]+):\s*(.*)$")]
    private static partial Regex JobPropertyRegex();

    [GeneratedRegex(@"^      ([A-Za-z0-9_-]+):\s*(.*)$")]
    private static partial Regex JobSubEntryRegex();

    [GeneratedRegex(@"^      -\s*(.+)$")]
    private static partial Regex NeedsListItemRegex();

    [GeneratedRegex(@"^      - (.*)$")]
    private static partial Regex StepDashRegex();

    [GeneratedRegex(@"^        ([A-Za-z0-9_-]+):\s*(.*)$")]
    private static partial Regex StepPropertyRegex();

    [GeneratedRegex(@"^([A-Za-z0-9_-]+):\s*(.*)$")]
    private static partial Regex InlinePropertyRegex();

    [GeneratedRegex(@"^          ([A-Za-z0-9_]+):\s*(.*)$")]
    private static partial Regex StepEnvEntryRegex();

    [GeneratedRegex(@"^\s*([^\s@#]+)")]
    private static partial Regex UsesSlugRegex();
}
