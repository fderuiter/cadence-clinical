using System;
using System.Collections.Generic;
using System.Text;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class WorkflowSecurityValidatorService
{
    private enum TopSection
    {
        None,
        On,
        Permissions,
        Jobs,
    }

    private enum JobSection
    {
        None,
        Permissions,
        Steps,
    }

    private enum StepBlockMode
    {
        None,
        If,
        Run,
    }

    private sealed record UsesEntry(int LineNumber, string RawValue);

    private sealed record PermissionEntry(string Scope, string Access, int LineNumber);

    private sealed class WorkflowDispatchInput(string name, int lineNumber)
    {
        public string Name { get; } = name;

        public int LineNumber { get; } = lineNumber;

        public string? Type { get; set; }
    }

    private sealed class ParsedStep
    {
        private readonly StringBuilder _ifBuilder = new();

        public int LineNumber { get; init; } = 1;

        public UsesEntry? Uses { get; set; }

        public string? PersistCredentials { get; set; }

        public (int LineNumber, string Value)? CheckoutRepository { get; set; }

        public (int LineNumber, string Value)? CheckoutRef { get; set; }

        public List<(int LineNumber, string Text)> RunLines { get; } = [];

        public string IfExpression => _ifBuilder.ToString();

        public void AppendIf(string fragment)
        {
            if (fragment.Length == 0)
            {
                return;
            }

            if (_ifBuilder.Length > 0)
            {
                _ifBuilder.Append(' ');
            }

            _ifBuilder.Append(fragment);
        }
    }

    private sealed class ParsedJob(string key, int lineNumber)
    {
        private readonly StringBuilder _ifBuilder = new();

        public string Key { get; } = key;

        public int LineNumber { get; } = lineNumber;

        public int PermissionsLine { get; set; } = lineNumber;

        public string? PermissionsScalar { get; set; }

        public List<PermissionEntry> Permissions { get; } = [];

        public UsesEntry? ReusableWorkflowUses { get; set; }

        public List<ParsedStep> Steps { get; } = [];

        public string IfExpression => _ifBuilder.ToString();

        public void AppendIf(string fragment)
        {
            if (fragment.Length == 0)
            {
                return;
            }

            if (_ifBuilder.Length > 0)
            {
                _ifBuilder.Append(' ');
            }

            _ifBuilder.Append(fragment);
        }
    }

    private sealed class ParsedWorkflow
    {
        public HashSet<string> Triggers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<WorkflowDispatchInput> WorkflowDispatchInputs { get; } = [];

        public bool HasTopLevelPermissions { get; set; }

        public int TopLevelPermissionsLine { get; set; } = 1;

        public string? TopLevelPermissionsScalar { get; set; }

        public List<PermissionEntry> TopLevelPermissions { get; } = [];

        public List<ParsedJob> Jobs { get; } = [];

        public List<UsesEntry> JobUses { get; } = [];

        public List<ParsedStep> Steps { get; } = [];
    }

    private sealed class ParsedAction
    {
        public (int LineNumber, string Value)? DockerImage { get; set; }

        public List<ParsedStep> Steps { get; } = [];
    }

    private sealed class StepTracker
    {
        private int _stepsKeyIndent = -1;
        private int _stepDashIndent = -1;
        private int _stepPropIndent = -1;
        private bool _inWithBlock;
        private StepBlockMode _blockMode;
        private ParsedStep? _current;

        public void Reset(int stepsKeyIndent)
        {
            _stepsKeyIndent = stepsKeyIndent;
            _stepDashIndent = -1;
            _stepPropIndent = -1;
            _inWithBlock = false;
            _blockMode = StepBlockMode.None;
            _current = null;
        }

        public void Flush(List<ParsedStep> steps, List<ParsedStep>? jobSteps = null)
        {
            if (_current is not null)
            {
                steps.Add(_current);
                jobSteps?.Add(_current);
                _current = null;
            }

            _inWithBlock = false;
            _blockMode = StepBlockMode.None;
        }

        public void CaptureBlockLine(string rawLine, string trimmed, int lineNumber)
        {
            if (_current is null)
            {
                return;
            }

            if (_blockMode == StepBlockMode.If && trimmed.Length > 0 && !trimmed.StartsWith('#'))
            {
                _current.AppendIf(YamlPrimitives.StripInlineComment(trimmed));
            }
            else if (_blockMode == StepBlockMode.Run && trimmed.Length > 0 && !trimmed.StartsWith('#'))
            {
                _current.RunLines.Add((lineNumber, rawLine));
            }
        }

        public void EndBlockScalar() => _blockMode = StepBlockMode.None;

        public void ProcessLine(
            string trimmed,
            int indent,
            int lineNumber,
            List<ParsedStep> steps,
            List<ParsedStep>? jobSteps,
            ref int blockParentIndent)
        {
            if (YamlPrimitives.IsSequenceItem(trimmed, out string afterDash) &&
                (_stepDashIndent < 0 ? indent >= _stepsKeyIndent : indent == _stepDashIndent))
            {
                Flush(steps, jobSteps);
                _stepDashIndent = indent;
                _stepPropIndent = indent + 2;
                _current = new ParsedStep { LineNumber = lineNumber };

                if (afterDash.Length > 0)
                {
                    ProcessStepProperty(afterDash, _stepPropIndent, lineNumber, ref blockParentIndent);
                }

                return;
            }

            if (_current is null)
            {
                return;
            }

            if (indent == _stepPropIndent && !trimmed.StartsWith('-'))
            {
                _inWithBlock = false;
                ProcessStepProperty(trimmed, indent, lineNumber, ref blockParentIndent);
                return;
            }

            if (_inWithBlock && indent > _stepPropIndent && YamlPrimitives.TrySplitMapping(trimmed, out string withKey, out string withVal))
            {
                string cleanVal = YamlPrimitives.StripInlineComment(withVal);
                if (YamlPrimitives.IsBlockScalarHeader(cleanVal))
                {
                    blockParentIndent = indent;
                }
                else
                {
                    RecordWithEntry(withKey, UnquoteLiteral(cleanVal), lineNumber);
                }
            }
        }

        private void ProcessStepProperty(
            string propLine,
            int propIndent,
            int lineNumber,
            ref int blockParentIndent)
        {
            if (_current is null || !YamlPrimitives.TrySplitMapping(propLine, out string key, out string rawVal))
            {
                return;
            }

            string cleanVal = YamlPrimitives.StripInlineComment(rawVal);
            if (string.Equals(key, "if", StringComparison.Ordinal))
            {
                if (YamlPrimitives.IsBlockScalarHeader(cleanVal))
                {
                    _blockMode = StepBlockMode.If;
                    blockParentIndent = propIndent;
                }
                else
                {
                    _current.AppendIf(UnquoteLiteral(cleanVal));
                }

                return;
            }

            if (string.Equals(key, "run", StringComparison.Ordinal))
            {
                if (YamlPrimitives.IsBlockScalarHeader(cleanVal))
                {
                    _blockMode = StepBlockMode.Run;
                    blockParentIndent = propIndent;
                }
                else if (cleanVal.Length > 0)
                {
                    _current.RunLines.Add((lineNumber, UnquoteLiteral(rawVal)));
                }

                return;
            }

            if (YamlPrimitives.IsBlockScalarHeader(cleanVal))
            {
                blockParentIndent = propIndent;
                return;
            }

            if (string.Equals(key, "uses", StringComparison.Ordinal))
            {
                _current.Uses = new UsesEntry(lineNumber, rawVal);
            }
            else if (string.Equals(key, "with", StringComparison.Ordinal))
            {
                if (cleanVal.StartsWith('{') && cleanVal.EndsWith('}') && cleanVal.Length > 2)
                {
                    ParseFlowWith(cleanVal, lineNumber);
                }
                else
                {
                    _inWithBlock = true;
                }
            }
        }

        private void ParseFlowWith(string flowText, int lineNumber)
        {
            if (_current is null)
            {
                return;
            }

            foreach (string item in flowText[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (YamlPrimitives.TrySplitMapping(item, out string k, out string v))
                {
                    RecordWithEntry(k, UnquoteLiteral(YamlPrimitives.StripInlineComment(v)), lineNumber);
                }
            }
        }

        private void RecordWithEntry(string key, string value, int lineNumber)
        {
            if (_current is null)
            {
                return;
            }

            if (string.Equals(key, "persist-credentials", StringComparison.OrdinalIgnoreCase))
            {
                _current.PersistCredentials = value;
            }
            else if (string.Equals(key, "repository", StringComparison.OrdinalIgnoreCase))
            {
                _current.CheckoutRepository = (lineNumber, value);
            }
            else if (string.Equals(key, "ref", StringComparison.OrdinalIgnoreCase))
            {
                _current.CheckoutRef = (lineNumber, value);
            }
        }
    }
}
