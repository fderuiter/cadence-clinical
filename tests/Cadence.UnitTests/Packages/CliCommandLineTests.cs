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
    [InlineData("check", "--base", "origin/release/1.0")]
    [InlineData("check", "--push-before", "0000000000000000000000000000000000000000")]
    [InlineData("check", "deid", "--base", "origin/main")]
    [InlineData("check", "detect-secrets", "--push-before", "1111111111111111111111111111111111111111")]
    [InlineData("check", "--exclude", "api-clients", "--exclude", "vulnerabilities")]
    [InlineData("check", "adrs", "--pre-commit")]
    [InlineData("check", "markdown", "docs/cli.md", "README.md")]
    [InlineData("fix")]
    [InlineData("fix", "--full")]
    [InlineData("fix", "--openapi")]
    [InlineData("fix", "--full", "--openapi")]
    [InlineData("clean-secrets-baseline")]
    [InlineData("clean-secrets-baseline", "--check")]
    [InlineData("test", "--fast")]
    [InlineData("ci")]
    [InlineData("ci", "--job", "dotnet-quality", "--job", "security")]
    [InlineData("ci", "-j", "dotnet-tests", "--allow-skip", "--base", "main", "--list")]
    [InlineData("ci", "-j", "dotnet-tests", "--allow-missing-tools", "--allow-db-skip", "--base", "main", "--list")]
    [InlineData("rulesets")]
    [InlineData("rulesets", "--dry-run")]
    [InlineData("rulesets", "--dry-run", "--repo", "fderuiter/cadence-clinical")]
    [InlineData("check", "rulesets")]
    [InlineData("gxp", "sync", "--dry-run")]
    [InlineData("gxp", "sync", "--results-dir", "TestResults/gxp")]
    [InlineData("gxp", "snapshot", "--commit")]
    [InlineData("gxp", "snapshot", "--results-dir", "TestResults/gxp")]
    [InlineData("gxp", "snapshot", "--commit", "--results-dir", "TestResults/gxp")]
    [InlineData("gxp", "validate")]
    [InlineData("generate-schema-doc", "--validate")]
    [InlineData("generate-demo-manifest")]
    [InlineData("generate-demo-manifest", "--check")]
    [InlineData("check", "demo-manifest")]
    [InlineData("generate-rtm", "--validate", "--draft")]
    [InlineData("settings", "export", "--check")]
    [InlineData("scaffold", "adr", "Adopt a thing")]
    [InlineData("scaffold", "post", "Ship Kernel Phase 3")]
    [InlineData("scaffold", "post", "Ship Kernel Phase 3", "--author", "fderuiter,reviewer", "--tag", "kernel", "--tag", "architecture", "--summary", "Short excerpt", "--publish")]
    [InlineData("scaffold", "post", "Ship Kernel Phase 3", "--author=@fderuiter", "-t", "kernel")]
    [InlineData("db", "reset")]
    [InlineData("db", "reset", "--allow-offline")]
    [InlineData("db", "migrate")]
    [InlineData("db", "migrate", "all")]
    [InlineData("db", "migrate", "execution")]
    [InlineData("db", "add", "execution", "AddVisitWindowIndex")]
    [InlineData("db", "script")]
    [InlineData("db", "script", "kernel")]
    [InlineData("db", "script", "all", "--output-dir", "TestResults/migrations")]
    [InlineData("db", "bundle", "execution-module")]
    [InlineData("db", "list", "intelligence")]
    [InlineData("db", "optimize", "ctms")]
    [InlineData("db", "seed")]
    [InlineData("db", "seed", "execution")]
    [InlineData("migrations", "add", "ctms", "AddVisitWindow")]
    [InlineData("migrations", "script")]
    [InlineData("migrations", "script", "kernel")]
    [InlineData("migrations", "script", "all", "--output-dir", "TestResults/migrations")]
    [InlineData("migrations", "list")]
    [InlineData("migrations", "list", "intelligence")]
    [InlineData("migrations", "verify")]
    [InlineData("migrations", "verify", "executionmodule")]
    [InlineData("wizard", "onboarding")]
    public void Parse_AcceptsEachDocumentedInvocation(params string[] args)
    {
        var result = Root.Parse(args);

        result.Errors.Should().BeEmpty();
        result.CommandResult.Command.Action.Should().NotBeNull("every leaf command runs something");
    }

    [Theory]
    [InlineData("scaffold", "adr")]
    [InlineData("scaffold", "post")]
    [InlineData("check", "--no-such-option")]
    [InlineData("check", "--exclude", "--pre-commit")]
    [InlineData("check", "--base")]
    [InlineData("check", "--base", "--pre-commit")]
    [InlineData("check", "--push-before")]
    [InlineData("check", "--base", "origin/main", "--push-before", "0000000000000000000000000000000000000000")]
    [InlineData("check", "--pre-commit", "--base", "origin/main")]
    [InlineData("check", "--pre-commit", "--push-before", "0000000000000000000000000000000000000000")]
    [InlineData("ci", "--base", "--list")]
    [InlineData("check", "adrs", "--pre-comit")]
    [InlineData("doctor", "--no-such-option")]
    [InlineData("rulesets", "--no-such-option")]
    [InlineData("generate-demo-manifest", "--no-such-option")]
    [InlineData("no-such-command")]
    [InlineData("gxp")]
    [InlineData("gxp", "sync", "--results-dir")]
    [InlineData("gxp", "snapshot", "--results-dir")]
    [InlineData("gxp", "snapshot", "--results-dir", "--commit")]
    [InlineData("db")]
    [InlineData("db", "add")]
    [InlineData("db", "add", "execution")]
    [InlineData("db", "add", "all", "InitialCreate")]
    [InlineData("db", "script", "--output-dir")]
    [InlineData("db", "migrate", "--no-such-option")]
    [InlineData("db", "reset", "--no-such-option")]
    [InlineData("migrations")]
    [InlineData("migrations", "add")]
    [InlineData("migrations", "add", "ctms")]
    [InlineData("migrations", "verify", "--no-such-option")]
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
