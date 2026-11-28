using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

namespace Cadence.Cli.Services;

public static partial class RulesetService
{
    [GeneratedRegex(@"^  ([A-Za-z0-9_-]+):\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JobHeaderRegex();

    [GeneratedRegex(@"^    ([A-Za-z0-9_-]+):(.*)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JobPropertyRegex();

    [GeneratedRegex(@"^([A-Za-z0-9_]+):\s*(.+)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StepEnvEntryRegex();

    [GeneratedRegex(@"\bpull_request\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PullRequestTriggerRegex();

    [GeneratedRegex(@"\bmerge_group\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MergeGroupTriggerRegex();

    private static List<WorkflowJobInfo> ParseWorkflowJobs(string workflowFile, string[] lines)
    {
        var prTrigger = ParseOnPullRequest(lines);
        bool onMergeGroup = ParseWorkflowTrigger(lines, MergeGroupTriggerRegex());
        var jobs = new List<WorkflowJobInfo>();

        int jobsStart = Array.FindIndex(lines, l => l.TrimEnd() == "jobs:");
        if (jobsStart < 0)
        {
            return jobs;
        }

        int i = jobsStart + 1;
        while (i < lines.Length)
        {
            string line = lines[i];
            if (line.Length > 0 && line[0] != ' ' && line[0] != '#')
            {
                break;
            }

            var jobMatch = JobHeaderRegex().Match(line);
            if (!jobMatch.Success)
            {
                i++;
                continue;
            }

            string jobKey = jobMatch.Groups[1].Value;
            i++;
            int jobEnd = i;
            while (jobEnd < lines.Length)
            {
                string next = lines[jobEnd];
                if ((next.Length > 0 && next[0] != ' ' && next[0] != '#') || JobHeaderRegex().IsMatch(next))
                {
                    break;
                }

                jobEnd++;
            }

            jobs.Add(BuildJobInfo(workflowFile, prTrigger, onMergeGroup, jobKey, lines[i..jobEnd]));
            i = jobEnd;
        }

        return jobs;
    }

    private static bool ParseWorkflowTrigger(string[] lines, Regex triggerPattern)
    {
        bool inOnBlock = false;
        foreach (string raw in lines)
        {
            string line = YamlPrimitives.StripLineComment(raw);
            if (line == "jobs:")
            {
                break;
            }

            if (line.StartsWith("on:", StringComparison.Ordinal))
            {
                inOnBlock = true;
                if (triggerPattern.IsMatch(line))
                {
                    return true;
                }

                continue;
            }

            if (inOnBlock)
            {
                if (line.Length > 0 && line[0] != ' ')
                {
                    inOnBlock = false;
                }
                else if (triggerPattern.IsMatch(line))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static PullRequestTriggerInfo ParseOnPullRequest(string[] lines)
    {
        bool inOnBlock = false;
        bool onPullRequest = false;
        bool hasExplicitTypes = false;
        var types = new HashSet<string>(StringComparer.Ordinal);
        int pullRequestIndent = -1;
        int typesIndent = -1;

        foreach (string raw in lines)
        {
            string line = YamlPrimitives.StripLineComment(raw);
            if (line == "jobs:")
            {
                break;
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            int indent = YamlPrimitives.CountLeadingSpaces(line);
            if (line.StartsWith("on:", StringComparison.Ordinal))
            {
                inOnBlock = true;
                pullRequestIndent = -1;
                typesIndent = -1;
                if (PullRequestTriggerRegex().IsMatch(line))
                {
                    onPullRequest = true;
                }

                continue;
            }

            if (!inOnBlock)
            {
                continue;
            }

            if (indent == 0)
            {
                inOnBlock = false;
                pullRequestIndent = -1;
                typesIndent = -1;
                continue;
            }

            if (typesIndent >= 0)
            {
                if (indent > typesIndent)
                {
                    AddTriggerTypesFromLine(trimmed, types);
                    continue;
                }

                typesIndent = -1;
            }

            if (pullRequestIndent >= 0)
            {
                if (indent <= pullRequestIndent)
                {
                    pullRequestIndent = -1;
                }
                else if (trimmed.StartsWith("types:", StringComparison.Ordinal))
                {
                    hasExplicitTypes = true;
                    string afterTypes = trimmed["types:".Length..].Trim();
                    if (afterTypes.Length > 0 && afterTypes != "[")
                    {
                        AddTriggerTypesFromLine(afterTypes, types);
                        if (afterTypes.StartsWith('[') && !afterTypes.EndsWith(']'))
                        {
                            typesIndent = indent;
                        }
                    }
                    else
                    {
                        typesIndent = indent;
                    }

                    continue;
                }
            }

            if (trimmed.StartsWith("pull_request:", StringComparison.Ordinal))
            {
                onPullRequest = true;
                pullRequestIndent = indent;
            }
            else if (PullRequestTriggerRegex().IsMatch(trimmed) && pullRequestIndent < 0)
            {
                onPullRequest = true;
            }
        }

        return new PullRequestTriggerInfo(onPullRequest, hasExplicitTypes, types);
    }

    private static void AddTriggerTypesFromLine(string text, HashSet<string> types)
    {
        if (YamlPrimitives.IsSequenceItem(text, out string itemText))
        {
            string single = YamlPrimitives.UnquoteScalar(itemText);
            if (single.Length > 0)
            {
                types.Add(single);
            }

            return;
        }

        string cleaned = text.Trim().TrimStart('[').TrimEnd(']');
        foreach (string item in YamlPrimitives.ParseFlowStringArray(cleaned, decodeCommonEscapes: false))
        {
            types.Add(item);
        }
    }

    private static WorkflowJobInfo BuildJobInfo(
        string workflowFile,
        PullRequestTriggerInfo prTrigger,
        bool onMergeGroup,
        string jobKey,
        string[] bodyLines)
    {
        string? name = null;
        string? ifCond = null;
        string? uses = null;
        bool hasMatrix = false;
        var needs = new List<string>();
        var stepEnv = new Dictionary<string, string>(StringComparer.Ordinal);
        var stepRuns = new List<string>();

        int i = 0;
        while (i < bodyLines.Length)
        {
            string line = bodyLines[i];
            if (line.StartsWith("      matrix:", StringComparison.Ordinal))
            {
                hasMatrix = true;
                i++;
                continue;
            }

            if (line.TrimEnd() == "    steps:")
            {
                ParseStepsBlock(bodyLines[(i + 1)..], stepEnv, stepRuns);
                break;
            }

            var propMatch = JobPropertyRegex().Match(line);
            if (!propMatch.Success)
            {
                i++;
                continue;
            }

            string prop = propMatch.Groups[1].Value;
            string rest = YamlPrimitives.StripInlineComment(propMatch.Groups[2].Value);
            i++;

            if (prop == "name")
            {
                name = YamlPrimitives.UnquoteScalar(rest);
            }
            else if (prop == "uses")
            {
                uses = YamlPrimitives.UnquoteScalar(rest);
            }
            else if (prop == "if")
            {
                ifCond = ReadScalarOrBlock(rest, bodyLines, ref i, parentIndent: 4);
            }
            else if (prop == "needs")
            {
                ReadNeedsList(rest, bodyLines, ref i, needs);
            }
        }

        return new WorkflowJobInfo(
            workflowFile,
            prTrigger,
            onMergeGroup,
            jobKey,
            string.IsNullOrWhiteSpace(name) ? jobKey : name,
            ifCond,
            uses,
            hasMatrix,
            needs,
            stepEnv,
            string.Join('\n', stepRuns));
    }

    private static void ParseStepsBlock(
        string[] stepLines,
        Dictionary<string, string> stepEnv,
        List<string> stepRuns)
    {
        int i = 0;
        int stepPropIndent = 8;
        int envIndent = -1;
        while (i < stepLines.Length)
        {
            string line = stepLines[i];
            if (line.Trim().Length == 0)
            {
                i++;
                continue;
            }

            int indent = YamlPrimitives.CountLeadingSpaces(line);
            string trimmedStart = line[indent..];
            string stepPropText;
            if (trimmedStart.StartsWith("- ", StringComparison.Ordinal))
            {
                stepPropIndent = indent + 2;
                envIndent = -1;
                stepPropText = trimmedStart[2..];
            }
            else
            {
                if (envIndent >= 0)
                {
                    if (indent > envIndent)
                    {
                        var envMatch = StepEnvEntryRegex().Match(trimmedStart);
                        if (envMatch.Success)
                        {
                            stepEnv[envMatch.Groups[1].Value] = envMatch.Groups[2].Value.Trim();
                        }

                        i++;
                        continue;
                    }

                    envIndent = -1;
                }

                if (indent != stepPropIndent)
                {
                    i++;
                    continue;
                }

                stepPropText = trimmedStart;
            }

            if (stepPropText.TrimEnd() == "env:")
            {
                envIndent = stepPropIndent;
                i++;
                continue;
            }

            if (stepPropText.StartsWith("run:", StringComparison.Ordinal))
            {
                string inline = stepPropText["run:".Length..].Trim();
                i++;
                stepRuns.Add(ReadScalarOrBlock(inline, stepLines, ref i, parentIndent: stepPropIndent));
                continue;
            }

            i++;
        }
    }

    private static string ReadScalarOrBlock(string inlineValue, string[] lines, ref int index, int parentIndent)
    {
        if (!YamlPrimitives.TryParseBlockHeader(inlineValue, out char style, out char chomp, out int indentHint))
        {
            return YamlPrimitives.UnquoteScalar(inlineValue);
        }

        var (blockText, nextIndex) = YamlPrimitives.ConsumeBlockScalar(
            lines,
            index,
            lines.Length,
            parentIndent,
            style,
            chomp,
            indentHint);
        index = nextIndex;
        return blockText.Trim();
    }

    private static void ReadNeedsList(string inlineValue, string[] lines, ref int index, List<string> needs)
    {
        if (inlineValue.StartsWith('[') && inlineValue.EndsWith(']'))
        {
            AddCommaSeparatedNeeds(inlineValue[1..^1], needs);
            return;
        }

        if (inlineValue.Length > 0 && inlineValue != "[")
        {
            needs.Add(YamlPrimitives.UnquoteScalar(inlineValue));
            return;
        }

        var buffer = new StringBuilder(inlineValue);
        while (index < lines.Length)
        {
            string raw = YamlPrimitives.StripLineComment(lines[index]);
            if (raw.Trim().Length == 0)
            {
                index++;
                continue;
            }

            if (!raw.StartsWith("      ", StringComparison.Ordinal) && !raw.StartsWith("    - ", StringComparison.Ordinal))
            {
                break;
            }

            string trimmed = raw.Trim();
            index++;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                needs.Add(YamlPrimitives.UnquoteScalar(trimmed[2..]));
                continue;
            }

            buffer.Append(' ').Append(trimmed);
            if (trimmed.EndsWith(']'))
            {
                break;
            }
        }

        string combined = buffer.ToString().Trim().TrimStart('[').TrimEnd(']');
        AddCommaSeparatedNeeds(combined, needs);
    }

    private static void AddCommaSeparatedNeeds(string csv, List<string> needs)
    {
        needs.AddRange(YamlPrimitives.ParseFlowStringArray(csv, decodeCommonEscapes: false));
    }
}
