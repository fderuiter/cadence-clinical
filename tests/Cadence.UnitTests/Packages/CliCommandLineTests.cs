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
    [InlineData("doctor", "--auto-fix", "--minimal")]
    [InlineData("doctor", "--linters-only")]
    [InlineData("doctor", "--auto-fix", "--linters-only")]
    [InlineData("check")]
    [InlineData("check", "--base", "origin/release/1.0")]
    [InlineData("check", "--push-before", "0000000000000000000000000000000000000000")]
    [InlineData("check", "--event-name", "workflow_dispatch", "--git-ref", "refs/tags/v1.0.1")]
    [InlineData("check", "gxp-snapshot", "--push-before", "0000000000000000000000000000000000000000", "--event-name", "push", "--git-ref", "refs/heads/release/1.0")]
    [InlineData("check", "deid", "--base", "origin/main")]
    [InlineData("check", "detect-secrets", "--push-before", "1111111111111111111111111111111111111111")]
    [InlineData("check", "--exclude", "api-clients", "--exclude", "vulnerabilities")]
    [InlineData("check", "--fail-fast")]
    [InlineData("check", "--pre-commit", "--fail-fast", "false")]
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
    [InlineData("ci", "--offline")]
    [InlineData("ci", "--include-uncommitted")]
    [InlineData("ci", "--job", "dotnet-quality", "--job", "security")]
    [InlineData("ci", "-j", "lint-and-static", "--include-uncommitted", "--base", "HEAD~1", "--list")]
    [InlineData("ci", "-j", "dotnet-tests", "--allow-skip", "--base", "main", "--list")]
    [InlineData("ci", "-j", "dotnet-tests", "--allow-skip", "--base", "main", "--offline", "--list")]
    [InlineData("ci", "-j", "dotnet-tests", "--allow-missing-tools", "--allow-db-skip", "--base", "main", "--list")]
    [InlineData("ci", "-j", "dotnet-tests", "--allow-missing-tools", "--allow-db-skip", "--base", "main", "--offline", "--list")]
    [InlineData("rulesets")]
    [InlineData("rulesets", "--dry-run")]
    [InlineData("rulesets", "--verify-live")]
    [InlineData("rulesets", "--verify-live", "--profile", "active-actions", "--repo", "fderuiter/cadence-clinical")]
    [InlineData("rulesets", "--profile", "paused-actions")]
    [InlineData("rulesets", "--dry-run", "--profile", "active-actions", "--repo", "fderuiter/cadence-clinical")]
    [InlineData("check", "rulesets")]
    [InlineData("check", "merge-readiness")]
    [InlineData("gxp", "sync", "--dry-run")]
    [InlineData("gxp", "sync", "--results-dir", "TestResults/gxp")]
    [InlineData("gxp", "snapshot", "--commit")]
    [InlineData("gxp", "snapshot", "--results-dir", "TestResults/gxp")]
    [InlineData("gxp", "snapshot", "--commit", "--results-dir", "TestResults/gxp")]
    [InlineData("gxp", "validate")]
    [InlineData("generate-schema-doc", "--validate")]
    [InlineData("pre-commit-openapi")]
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
    [InlineData("db", "migrate")]
    [InlineData("migrations", "add", "ctms", "AddVisitWindow")]
    [InlineData("migrations", "script")]
    [InlineData("migrations", "script", "kernel")]
    [InlineData("migrations", "script", "all", "--output-dir", "TestResults/migrations")]
    [InlineData("migrations", "list")]
    [InlineData("migrations", "list", "intelligence")]
    [InlineData("migrations", "verify")]
    [InlineData("migrations", "verify", "executionmodule")]
    [InlineData("migrations", "apply")]
    [InlineData("migrations", "apply", "ctms")]
    [InlineData("migrations", "apply", "ctms", "--connection", "Host=localhost;Database=cadence")]
    [InlineData("migrations", "apply", "ctms", "-c", "Host=localhost;Database=cadence")]
    [InlineData("migrations", "bundle")]
    [InlineData("migrations", "bundle", "kernel")]
    [InlineData("migrations", "bundle", "all", "--output-dir", "TestResults/migrations")]
    [InlineData("migrations", "optimize")]
    [InlineData("migrations", "optimize", "ctms")]
    [InlineData("migrations", "optimize", "kernel", "--output-dir", "CompiledModels")]
    [InlineData("migrations", "seed")]
    [InlineData("migrations", "seed", "execution")]
    [InlineData("migrations", "seed", "all")]
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
    [InlineData("check", "--event-name")]
    [InlineData("check", "--git-ref")]
    [InlineData("check", "--event-name", "--pre-commit")]
    [InlineData("check", "--git-ref", "--pre-commit")]
    [InlineData("check", "--pre-commit", "--event-name", "workflow_dispatch")]
    [InlineData("check", "--pre-commit", "--git-ref", "refs/heads/main")]
    [InlineData("ci", "--base", "--list")]
    [InlineData("check", "adrs", "--pre-comit")]
    [InlineData("doctor", "--no-such-option")]
    [InlineData("rulesets", "--no-such-option")]
    [InlineData("rulesets", "--dry-run", "--verify-live")]
    [InlineData("rulesets", "--profile")]
    [InlineData("rulesets", "--profile", "--dry-run")]
    [InlineData("rulesets", "--repo", "--dry-run")]
    [InlineData("generate-demo-manifest", "--no-such-option")]
    [InlineData("clean-test-dbs")]
    [InlineData("no-such-command")]
    [InlineData("gxp")]
    [InlineData("gxp", "sync", "--results-dir")]
    [InlineData("gxp", "snapshot", "--results-dir")]
    [InlineData("gxp", "snapshot", "--results-dir", "--commit")]
    [InlineData("migrations")]
    [InlineData("migrations", "add")]
    [InlineData("migrations", "add", "ctms")]
    [InlineData("migrations", "verify", "--no-such-option")]
    [InlineData("migrations", "apply", "--connection")]
    [InlineData("migrations", "apply", "--no-such-option")]
    [InlineData("migrations", "bundle", "--output-dir")]
    [InlineData("migrations", "bundle", "--no-such-option")]
    [InlineData("migrations", "optimize", "--output-dir")]
    [InlineData("migrations", "optimize", "--no-such-option")]
    [InlineData("migrations", "seed", "--no-such-option")]
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
