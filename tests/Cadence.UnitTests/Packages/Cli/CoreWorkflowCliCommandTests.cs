using Xunit;

namespace Cadence.UnitTests.Packages.Cli;

public sealed class CoreWorkflowCliCommandTests
{
    [Theory]
    [InlineData("doctor", "-f")]
    [InlineData("check")]
    [InlineData("check", "--base", "origin/release/1.0")]
    [InlineData("check", "--push-before", "0000000000000000000000000000000000000000")]
    [InlineData("check", "deid", "--base", "origin/main")]
    [InlineData("check", "detect-secrets", "--push-before", "1111111111111111111111111111111111111111")]
    [InlineData("check", "--exclude", "api-clients", "--exclude", "vulnerabilities")]
    [InlineData("check", "--fail-fast")]
    [InlineData("check", "--pre-commit", "--fail-fast", "false")]
    [InlineData("check", "adrs", "--pre-commit")]
    [InlineData("check", "markdown", "docs/cli.md", "README.md")]
    [InlineData("check", "rulesets")]
    [InlineData("check", "merge-readiness")]
    [InlineData("check", "demo-manifest")]
    [InlineData("fix")]
    [InlineData("fix", "--full")]
    [InlineData("fix", "--openapi")]
    [InlineData("fix", "--full", "--openapi")]
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
    [InlineData("rulesets", "--profile", "paused-actions")]
    [InlineData("rulesets", "--dry-run", "--profile", "active-actions", "--repo", "fderuiter/cadence-clinical")]
    public void Parse_AcceptsDocumentedCoreWorkflowInvocation(params string[] args) =>
        CliCommandLineTests.AssertParses(args);

    [Theory]
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
    [InlineData("rulesets", "--profile")]
    [InlineData("rulesets", "--profile", "--dry-run")]
    [InlineData("rulesets", "--repo", "--dry-run")]
    [InlineData("no-such-command")]
    public void Parse_RejectsInvalidCoreWorkflowInvocation(params string[] args) =>
        CliCommandLineTests.AssertRejects(args);
}
