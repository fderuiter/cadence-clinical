using System;
using System.IO;
using System.Linq;
using System.Text.Json;

using Cadence.Cli.Services;
using Cadence.Compliance;
using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Commands;

/// <summary>
/// Agent lifecycle hooks (PRD-SYS-053). Each command reads the hook payload as JSON on standard input and exits with
/// 0 to allow, or 2 with the reason on standard error to refuse; both Claude Code and Antigravity treat exit code 2
/// as "blocked, tell the agent why".
/// </summary>
public static class HookCommands
{
    /// <summary>Exit code that tells the agent runtime to block the step and show standard error to the agent.</summary>
    public const int Blocked = 2;

    /// <summary>PreToolUse: refuses destructive git and shell commands and hand edits to generated GxP documents.</summary>
    public static int PreTool(TextReader input, TextWriter error)
    {
        AgentHookGuard.ToolCall call;
        try
        {
            call = AgentHookGuard.ParsePayload(input.ReadToEnd());
        }
        catch (JsonException ex)
        {
            // A payload we cannot read is refused: the guard fails closed.
            error.WriteLine($"cadence hook pre-tool: unreadable hook payload ({ex.Message}).");
            return Blocked;
        }

        string? reason = AgentHookGuard.RefusalReason(call);
        if (reason is null)
        {
            return 0;
        }

        error.WriteLine($"Blocked by the Cadence GxP guardrail: {reason}");
        return Blocked;
    }

    /// <summary>PostToolUse: lints the query style of an edited C# file (strongly typed boolean predicates).</summary>
    public static int PostWrite(TextReader input, TextWriter error)
    {
        AgentHookGuard.ToolCall call;
        try
        {
            call = AgentHookGuard.ParsePayload(input.ReadToEnd());
        }
        catch (JsonException)
        {
            // Nothing to check; a post-write hook never blocks on a payload it cannot read.
            return 0;
        }

        string[] csFiles = [.. call.Paths.Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && File.Exists(p))];
        if (csFiles.Length == 0)
        {
            return 0;
        }

        if (QueryLinterService.LintQueries(csFiles) == 0)
        {
            return 0;
        }

        error.WriteLine($"Query style issues in {string.Join(", ", csFiles)}; see the output above (AGENTS.md pattern 2).");
        return Blocked;
    }

    /// <summary>PreInvocation: prints the GxP working rules the agent must follow.</summary>
    public static int PreInvocation(TextWriter output)
    {
        output.WriteLine(AgentHookGuard.ComplianceReminder);
        return 0;
    }

    /// <summary>Stop: refuses to finish while the traceability matrix is out of date with the requirement tags.</summary>
    public static int Stop(TextWriter error)
    {
        if (RtmGeneratorService.GenerateRtm(draft: true, validateOnly: true) == 0)
        {
            return 0;
        }

        error.WriteLine("The traceability matrix does not match the requirement tags in tests/. Run `pnpm cadence gxp sync` before finishing.");
        return Blocked;
    }
}
