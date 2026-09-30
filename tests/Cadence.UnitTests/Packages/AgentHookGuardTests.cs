using System.IO;

using Cadence.Cli.Commands;
using Cadence.Cli.Services;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>The agent lifecycle hooks (<c>cadence hook</c>) and the guard behind them.</summary>
public class AgentHookGuardTests
{
    /// <summary>
    /// The pre-tool guardrail refuses destructive git and shell commands.
    /// </summary>
    [Theory]
    [GxPRequirement("PRD-SYS-053")]
    [InlineData("git push --force origin main")]
    [InlineData("git push -f origin HEAD:main")]
    [InlineData("git push --force-with-lease origin master")]
    [InlineData("git push origin --delete main")]
    [InlineData("git push origin :main")]
    [InlineData("git branch -D main")]
    [InlineData("git commit -m wip --no-verify")]
    [InlineData("git filter-branch --tree-filter 'rm x' HEAD")]
    [InlineData("rm -rf /")]
    [InlineData("rm -rf ~")]
    [InlineData("cd /repo && rm -fr .")]
    [InlineData("rm --recursive --force *")]
    [InlineData("git clean -fdx")]
    [InlineData("sed -i 's/Fail/Pass/' docs/SDLC/Requirements_Traceability_Matrix.md")]
    [InlineData("echo ok > docs/SDLC/IQ_OQ_PQ_Execution_Report.md")]
    [InlineData("cp /tmp/rtm.md docs/SDLC/Requirements_Traceability_Matrix.md")]
    public void PreTool_RefusesDestructiveCommands(string command)
    {
        AgentHookGuard.CommandRefusalReason(command).Should().NotBeNull();
    }

    [Theory]
    [InlineData("git push -u origin claude/hygiene-epic")]
    [InlineData("git push --force-with-lease origin claude/hygiene-epic")]
    [InlineData("git reset --hard origin/main")]
    [InlineData("rm -rf bin obj")]
    [InlineData("rm -rf ./tests/Cadence.UnitTests/bin")]
    [InlineData("git clean -fd tests/")]
    [InlineData("git diff docs/SDLC/Requirements_Traceability_Matrix.md > /tmp/rtm.diff")]
    [InlineData("grep -n PRD-SYS-001 docs/SDLC/Requirements_Traceability_Matrix.md")]
    [InlineData("git add docs/SDLC/IQ_OQ_PQ_Execution_Report.md")]
    [InlineData("dotnet test Cadence.sln 2>&1 | tail -5")]
    public void PreTool_AllowsEverydayCommands(string command)
    {
        AgentHookGuard.CommandRefusalReason(command).Should().BeNull();
    }

    /// <summary>
    /// The pre-tool guardrail refuses a write tool that targets a generated GxP document, whatever the runtime calls
    /// the path argument.
    /// </summary>
    [Theory]
    [GxPRequirement("PRD-SYS-053")]
    [InlineData("""{"tool_name":"Edit","tool_input":{"file_path":"/repo/docs/SDLC/Requirements_Traceability_Matrix.md"}}""")]
    [InlineData("""{"toolName":"write_to_file","arguments":{"TargetFile":"docs/SDLC/IQ_OQ_PQ_Execution_Report.md"}}""")]
    [InlineData("""{"tool_input":{"file_path":"docs/SDLC/runs/2026-09-30/report.md"}}""")]
    [InlineData("""{"tool_name":"Bash","tool_input":{"command":"git push --force origin main"}}""")]
    public void PreTool_RefusesPayloads(string payload)
    {
        var error = new StringWriter();

        int exit = HookCommands.PreTool(new StringReader(payload), error);

        exit.Should().Be(HookCommands.Blocked);
        error.ToString().Should().Contain("Blocked by the Cadence GxP guardrail");
    }

    [Theory]
    [InlineData("""{"tool_name":"Edit","tool_input":{"file_path":"/repo/docs/SDLC/01_Product_Requirements_Document_PRD.md"}}""")]
    [InlineData("""{"tool_name":"Bash","tool_input":{"command":"dotnet build Cadence.sln"}}""")]
    [InlineData("")]
    public void PreTool_AllowsOrdinaryPayloads(string payload)
    {
        HookCommands.PreTool(new StringReader(payload), new StringWriter()).Should().Be(0);
    }

    [Fact]
    public void PreTool_FailsClosedOnAnUnreadablePayload()
    {
        HookCommands.PreTool(new StringReader("{not json"), new StringWriter()).Should().Be(HookCommands.Blocked);
    }

    [Fact]
    public void PreInvocation_PrintsTheWorkingRules()
    {
        var output = new StringWriter();

        HookCommands.PreInvocation(output).Should().Be(0);

        output.ToString().Should().Contain("21 CFR Part 11").And.Contain("IClock").And.Contain("gxp sync");
    }

    [Fact]
    public void PostWrite_IgnoresPayloadsWithoutACSharpFile()
    {
        HookCommands.PostWrite(new StringReader("""{"tool_input":{"file_path":"README.md"}}"""), new StringWriter()).Should().Be(0);
    }

    [Theory]
    [InlineData("hook", "pre-tool")]
    [InlineData("hook", "post-write")]
    [InlineData("hook", "pre-invocation")]
    [InlineData("hook", "stop")]
    public void Parse_AcceptsEachHook(params string[] args)
    {
        var result = Cadence.Cli.Program.BuildRootCommand().Parse(args);

        result.Errors.Should().BeEmpty();
        result.CommandResult.Command.Action.Should().NotBeNull();
    }
}
