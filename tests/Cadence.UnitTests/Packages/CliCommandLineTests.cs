using System.Collections.Generic;
using System.CommandLine;
using System.Linq;

using Cadence.Cli;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class CliCommandLineTests
{
    private static readonly RootCommand Root = Cadence.Cli.Program.BuildRootCommand();

    [Theory]
    [InlineData("doctor", "-f")]
    [InlineData("check")]
    [InlineData("check", "adrs", "--pre-commit")]
    [InlineData("check", "markdown", "docs/cli.md", "README.md")]
    [InlineData("test", "--fast")]
    [InlineData("ci")]
    [InlineData("ci", "--job", "style", "--job", "security")]
    [InlineData("ci", "-j", "dotnet-tests", "--allow-skip", "--base", "main", "--list")]
    [InlineData("gxp", "sync", "--dry-run", "--commit")]
    [InlineData("gxp", "validate")]
    [InlineData("generate-schema-doc", "--validate")]
    [InlineData("generate-rtm", "--validate", "--draft")]
    [InlineData("settings", "export", "--check")]
    [InlineData("scaffold", "adr", "Adopt a thing")]
    [InlineData("db", "reset")]
    [InlineData("wizard", "onboarding")]
    public void Parse_AcceptsEachDocumentedInvocation(params string[] args)
    {
        var result = Root.Parse(args);

        result.Errors.Should().BeEmpty();
        result.CommandResult.Command.Action.Should().NotBeNull("every leaf command runs something");
    }

    [Theory]
    [InlineData("scaffold", "adr")]
    [InlineData("check", "--no-such-option")]
    [InlineData("check", "adrs", "--pre-comit")]
    [InlineData("doctor", "--no-such-option")]
    [InlineData("no-such-command")]
    [InlineData("gxp")]
    public void Parse_RejectsMissingArgumentsUnknownInputAndBareBranches(params string[] args)
    {
        Root.Parse(args).Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void EveryLeafCommand_HasAnActionAndADescription()
    {
        var leaves = Descendants(Root).Where(c => c.Subcommands.Count == 0).ToList();

        leaves.Should().HaveCountGreaterThan(40);
        leaves.Should().OnlyContain(c => c.Action != null && !string.IsNullOrWhiteSpace(c.Description));
    }

    private static IEnumerable<Command> Descendants(Command command) =>
        command.Subcommands.SelectMany(c => Descendants(c).Prepend(c));
}
