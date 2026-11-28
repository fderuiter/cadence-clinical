using System;
using System.Collections.Generic;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class WorkflowSecurityValidatorService
{
    private static ParsedWorkflow ParseWorkflowFile(string[] lines)
    {
        var workflow = new ParsedWorkflow();
        TopSection topSection = TopSection.None;
        JobSection jobSection = JobSection.None;
        ParsedJob? currentJob = null;
        var stepTracker = new StepTracker();

        var onTracker = new OnSectionTracker();
        int jobsChildIndent = -1;
        int jobPropIndent = -1;
        int blockParentIndent = -1;
        bool captureBlockIntoJobIf = false;

        for (int idx = 0; idx < lines.Length; idx++)
        {
            int lineNumber = idx + 1;
            string rawLine = lines[idx];
            string trimmed = rawLine.Trim();
            int indent = YamlPrimitives.CountLeadingSpaces(rawLine);

            if (blockParentIndent >= 0)
            {
                if (trimmed.Length == 0 || indent > blockParentIndent)
                {
                    if (captureBlockIntoJobIf && currentJob is not null && trimmed.Length > 0 && !trimmed.StartsWith('#'))
                    {
                        currentJob.AppendIf(YamlPrimitives.StripInlineComment(trimmed));
                    }
                    else
                    {
                        stepTracker.CaptureBlockLine(rawLine, trimmed, lineNumber);
                    }

                    continue;
                }

                blockParentIndent = -1;
                captureBlockIntoJobIf = false;
                stepTracker.EndBlockScalar();
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (indent == 0 && !trimmed.StartsWith('-'))
            {
                stepTracker.Flush(workflow.Steps, currentJob?.Steps);
                currentJob = null;
                jobSection = JobSection.None;
                onTracker.Reset();
                topSection = ParseTopLevelLine(trimmed, lineNumber, workflow, ref blockParentIndent);
                continue;
            }

            switch (topSection)
            {
                case TopSection.On:
                    onTracker.ProcessLine(trimmed, indent, lineNumber, workflow, ref blockParentIndent);
                    break;

                case TopSection.Permissions:
                    ParsePermissionMappingLine(trimmed, lineNumber, workflow.TopLevelPermissions);
                    break;

                case TopSection.Jobs:
                    ParseJobsSectionLine(
                        trimmed,
                        indent,
                        lineNumber,
                        workflow,
                        stepTracker,
                        ref jobsChildIndent,
                        ref jobPropIndent,
                        ref currentJob,
                        ref jobSection,
                        ref blockParentIndent,
                        ref captureBlockIntoJobIf);
                    break;
            }
        }

        stepTracker.Flush(workflow.Steps, currentJob?.Steps);
        return workflow;
    }

    private static TopSection ParseTopLevelLine(
        string trimmed,
        int lineNumber,
        ParsedWorkflow workflow,
        ref int blockParentIndent)
    {
        if (!YamlPrimitives.TrySplitMapping(trimmed, out string key, out string rawVal))
        {
            return TopSection.None;
        }

        string cleanVal = YamlPrimitives.StripInlineComment(rawVal);
        if (YamlPrimitives.IsBlockScalarHeader(cleanVal))
        {
            blockParentIndent = 0;
            return TopSection.None;
        }

        if (string.Equals(key, "on", StringComparison.Ordinal))
        {
            if (cleanVal.Length > 0)
            {
                ExtractInlineTriggers(cleanVal, workflow.Triggers);
                return TopSection.None;
            }

            return TopSection.On;
        }

        if (string.Equals(key, "permissions", StringComparison.Ordinal))
        {
            workflow.HasTopLevelPermissions = true;
            workflow.TopLevelPermissionsLine = lineNumber;
            if (cleanVal.Length > 0)
            {
                if (cleanVal.StartsWith('{') && cleanVal.EndsWith('}') && cleanVal.Length > 2)
                {
                    ParseFlowPermissions(cleanVal, lineNumber, workflow.TopLevelPermissions);
                }
                else
                {
                    workflow.TopLevelPermissionsScalar = UnquoteLiteral(cleanVal);
                }

                return TopSection.None;
            }

            return TopSection.Permissions;
        }

        return string.Equals(key, "jobs", StringComparison.Ordinal) ? TopSection.Jobs : TopSection.None;
    }

    private sealed class OnSectionTracker
    {
        private int _eventIndent = -1;
        private int _dispatchChildIndent = -1;
        private int _inputNameIndent = -1;
        private bool _inWorkflowDispatch;
        private bool _inDispatchInputs;
        private WorkflowDispatchInput? _currentInput;

        public void Reset()
        {
            _eventIndent = -1;
            _dispatchChildIndent = -1;
            _inputNameIndent = -1;
            _inWorkflowDispatch = false;
            _inDispatchInputs = false;
            _currentInput = null;
        }

        public void ProcessLine(
            string trimmed,
            int indent,
            int lineNumber,
            ParsedWorkflow workflow,
            ref int blockParentIndent)
        {
            if (_eventIndent < 0 && trimmed.StartsWith('-'))
            {
                string item = UnquoteLiteral(YamlPrimitives.StripInlineComment(trimmed[1..].Trim()));
                if (item.Length > 0)
                {
                    workflow.Triggers.Add(item);
                }

                return;
            }

            if (!trimmed.StartsWith('-') && (_eventIndent < 0 || indent == _eventIndent))
            {
                _eventIndent = indent;
                _dispatchChildIndent = -1;
                _inputNameIndent = -1;
                _inDispatchInputs = false;
                _currentInput = null;
                if (YamlPrimitives.TrySplitMapping(trimmed, out string eventName, out string eventVal))
                {
                    workflow.Triggers.Add(eventName);
                    _inWorkflowDispatch = string.Equals(eventName, "workflow_dispatch", StringComparison.OrdinalIgnoreCase);
                    if (YamlPrimitives.IsBlockScalarHeader(YamlPrimitives.StripInlineComment(eventVal)))
                    {
                        blockParentIndent = indent;
                    }
                }

                return;
            }

            if (!_inWorkflowDispatch || trimmed.StartsWith('-') || !YamlPrimitives.TrySplitMapping(trimmed, out string key, out string rawVal))
            {
                return;
            }

            string cleanVal = YamlPrimitives.StripInlineComment(rawVal);
            if (YamlPrimitives.IsBlockScalarHeader(cleanVal))
            {
                blockParentIndent = indent;
                return;
            }

            if (_dispatchChildIndent < 0 || indent == _dispatchChildIndent)
            {
                _dispatchChildIndent = indent;
                _inputNameIndent = -1;
                _currentInput = null;
                _inDispatchInputs = string.Equals(key, "inputs", StringComparison.Ordinal);
                return;
            }

            if (!_inDispatchInputs || indent <= _dispatchChildIndent)
            {
                return;
            }

            if (_inputNameIndent < 0 || indent == _inputNameIndent)
            {
                _inputNameIndent = indent;
                _currentInput = new WorkflowDispatchInput(key, lineNumber);
                workflow.WorkflowDispatchInputs.Add(_currentInput);
                return;
            }

            if (_currentInput is not null && indent > _inputNameIndent && string.Equals(key, "type", StringComparison.Ordinal))
            {
                _currentInput.Type = UnquoteLiteral(cleanVal);
            }
        }
    }

    private static void ParseJobsSectionLine(
        string trimmed,
        int indent,
        int lineNumber,
        ParsedWorkflow workflow,
        StepTracker stepTracker,
        ref int jobsChildIndent,
        ref int jobPropIndent,
        ref ParsedJob? currentJob,
        ref JobSection jobSection,
        ref int blockParentIndent,
        ref bool captureBlockIntoJobIf)
    {
        if (!trimmed.StartsWith('-') && (jobsChildIndent < 0 || indent == jobsChildIndent))
        {
            if (YamlPrimitives.TrySplitMapping(trimmed, out string jobKey, out _))
            {
                jobsChildIndent = indent;
                jobPropIndent = -1;
                stepTracker.Flush(workflow.Steps, currentJob?.Steps);
                currentJob = new ParsedJob(jobKey, lineNumber);
                workflow.Jobs.Add(currentJob);
                jobSection = JobSection.None;
            }

            return;
        }

        if (currentJob is null)
        {
            return;
        }

        if (!trimmed.StartsWith('-') && indent > jobsChildIndent && (jobPropIndent < 0 || indent == jobPropIndent))
        {
            jobPropIndent = indent;
            stepTracker.Flush(workflow.Steps, currentJob.Steps);
            jobSection = ParseJobPropertyLine(
                trimmed,
                indent,
                lineNumber,
                currentJob,
                workflow,
                stepTracker,
                ref blockParentIndent,
                ref captureBlockIntoJobIf);
            return;
        }

        if (jobSection == JobSection.Permissions && indent > jobPropIndent)
        {
            ParsePermissionMappingLine(trimmed, lineNumber, currentJob.Permissions);
        }
        else if (jobSection == JobSection.Steps)
        {
            stepTracker.ProcessLine(trimmed, indent, lineNumber, workflow.Steps, currentJob.Steps, ref blockParentIndent);
        }
    }

    private static JobSection ParseJobPropertyLine(
        string trimmed,
        int indent,
        int lineNumber,
        ParsedJob currentJob,
        ParsedWorkflow workflow,
        StepTracker stepTracker,
        ref int blockParentIndent,
        ref bool captureBlockIntoJobIf)
    {
        if (!YamlPrimitives.TrySplitMapping(trimmed, out string propKey, out string propRawVal))
        {
            return JobSection.None;
        }

        string cleanVal = YamlPrimitives.StripInlineComment(propRawVal);
        if (string.Equals(propKey, "if", StringComparison.Ordinal))
        {
            if (YamlPrimitives.IsBlockScalarHeader(cleanVal))
            {
                blockParentIndent = indent;
                captureBlockIntoJobIf = true;
            }
            else
            {
                currentJob.AppendIf(UnquoteLiteral(cleanVal));
            }

            return JobSection.None;
        }

        if (YamlPrimitives.IsBlockScalarHeader(cleanVal))
        {
            blockParentIndent = indent;
            return JobSection.None;
        }

        if (string.Equals(propKey, "uses", StringComparison.Ordinal))
        {
            var usesEntry = new UsesEntry(lineNumber, propRawVal);
            currentJob.ReusableWorkflowUses = usesEntry;
            workflow.JobUses.Add(usesEntry);
            return JobSection.None;
        }

        if (string.Equals(propKey, "permissions", StringComparison.Ordinal))
        {
            currentJob.PermissionsLine = lineNumber;
            if (cleanVal.Length > 0)
            {
                if (cleanVal.StartsWith('{') && cleanVal.EndsWith('}') && cleanVal.Length > 2)
                {
                    ParseFlowPermissions(cleanVal, lineNumber, currentJob.Permissions);
                }
                else
                {
                    currentJob.PermissionsScalar = UnquoteLiteral(cleanVal);
                }

                return JobSection.None;
            }

            return JobSection.Permissions;
        }

        if (string.Equals(propKey, "steps", StringComparison.Ordinal))
        {
            stepTracker.Reset(indent);
            return JobSection.Steps;
        }

        return JobSection.None;
    }

    private static ParsedAction ParseActionFile(string[] lines)
    {
        var action = new ParsedAction();
        bool inRuns = false;
        bool inSteps = false;
        int runsPropIndent = -1;
        int blockParentIndent = -1;
        var stepTracker = new StepTracker();

        for (int idx = 0; idx < lines.Length; idx++)
        {
            int lineNumber = idx + 1;
            string rawLine = lines[idx];
            string trimmed = rawLine.Trim();
            int indent = YamlPrimitives.CountLeadingSpaces(rawLine);

            if (blockParentIndent >= 0)
            {
                if (trimmed.Length == 0 || indent > blockParentIndent)
                {
                    stepTracker.CaptureBlockLine(rawLine, trimmed, lineNumber);
                    continue;
                }

                blockParentIndent = -1;
                stepTracker.EndBlockScalar();
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (indent == 0 && !trimmed.StartsWith('-'))
            {
                stepTracker.Flush(action.Steps, null);
                inSteps = false;
                if (YamlPrimitives.TrySplitMapping(trimmed, out string topKey, out string topVal))
                {
                    if (YamlPrimitives.IsBlockScalarHeader(YamlPrimitives.StripInlineComment(topVal)))
                    {
                        blockParentIndent = 0;
                    }

                    inRuns = string.Equals(topKey, "runs", StringComparison.Ordinal);
                }
                else
                {
                    inRuns = false;
                }

                continue;
            }

            if (!inRuns)
            {
                continue;
            }

            if (!trimmed.StartsWith('-') && (runsPropIndent < 0 || indent == runsPropIndent))
            {
                runsPropIndent = indent;
                stepTracker.Flush(action.Steps, null);
                inSteps = false;

                if (YamlPrimitives.TrySplitMapping(trimmed, out string runsKey, out string runsVal))
                {
                    string cleanVal = UnquoteLiteral(YamlPrimitives.StripInlineComment(runsVal));
                    if (YamlPrimitives.IsBlockScalarHeader(cleanVal))
                    {
                        blockParentIndent = indent;
                    }
                    else if (string.Equals(runsKey, "image", StringComparison.Ordinal) &&
                             cleanVal.StartsWith("docker://", StringComparison.OrdinalIgnoreCase))
                    {
                        action.DockerImage = (lineNumber, cleanVal);
                    }
                    else if (string.Equals(runsKey, "steps", StringComparison.Ordinal))
                    {
                        stepTracker.Reset(indent);
                        inSteps = true;
                    }
                }

                continue;
            }

            if (inSteps)
            {
                stepTracker.ProcessLine(trimmed, indent, lineNumber, action.Steps, jobSteps: null, ref blockParentIndent);
            }
        }

        stepTracker.Flush(action.Steps, null);
        return action;
    }

    private static void ExtractInlineTriggers(string cleanVal, HashSet<string> triggers)
    {
        foreach (string name in YamlPrimitives.ParseFlowStringArray(cleanVal, decodeCommonEscapes: false))
        {
            if (name.Length > 0)
            {
                triggers.Add(name);
            }
        }
    }

    private static void ParseFlowPermissions(string flowText, int lineNumber, List<PermissionEntry> target)
    {
        string inner = flowText[1..^1];
        foreach (string item in inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ParsePermissionMappingLine(item, lineNumber, target);
        }
    }

    private static void ParsePermissionMappingLine(string trimmed, int lineNumber, List<PermissionEntry> target)
    {
        if (YamlPrimitives.TrySplitMapping(trimmed, out string scope, out string rawAccess))
        {
            string access = UnquoteLiteral(YamlPrimitives.StripInlineComment(rawAccess));
            if (access.Length > 0)
            {
                target.Add(new PermissionEntry(scope, access, lineNumber));
            }
        }
    }

    private static string UnquoteLiteral(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }
}
