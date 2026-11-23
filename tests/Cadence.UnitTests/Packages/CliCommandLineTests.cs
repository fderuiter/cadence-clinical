using System.Collections.Generic;
using System.CommandLine;
using System.Linq;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Structural invariants across the full <see cref="Cadence.Cli.Program.BuildRootCommand"/> tree; domain-specific CLI
/// invocation tests live in disjoint slice files under <c>Packages/Cli/</c>.
/// </summary>
public class CliCommandLineTests
{
    internal static readonly RootCommand Root = Cadence.Cli.Program.BuildRootCommand();

    internal static void AssertParses(params string[] args)
    {
        var result = Root.Parse(args);

        result.Errors.Should().BeEmpty();
        result.CommandResult.Command.Action.Should().NotBeNull("every leaf command runs something");
    }

    internal static void AssertRejects(params string[] args)
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
