using Xunit;

namespace Cadence.UnitTests.Packages.Cli;

public sealed class ComplianceAndWorkspaceCliCommandTests
{
    [Theory]
    [InlineData("gxp", "sync", "--dry-run")]
    [InlineData("gxp", "sync", "--results-dir", "TestResults/gxp")]
    [InlineData("gxp", "snapshot", "--commit")]
    [InlineData("gxp", "snapshot", "--results-dir", "TestResults/gxp")]
    [InlineData("gxp", "snapshot", "--commit", "--results-dir", "TestResults/gxp")]
    [InlineData("gxp", "validate")]
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
    [InlineData("wizard", "onboarding")]
    public void Parse_AcceptsDocumentedComplianceAndWorkspaceInvocation(params string[] args) =>
        CliCommandLineTests.AssertParses(args);

    [Theory]
    [InlineData("scaffold", "adr")]
    [InlineData("scaffold", "post")]
    [InlineData("clean-test-dbs")]
    [InlineData("gxp")]
    [InlineData("gxp", "sync", "--results-dir")]
    [InlineData("gxp", "snapshot", "--results-dir")]
    [InlineData("gxp", "snapshot", "--results-dir", "--commit")]
    [InlineData("migrations")]
    [InlineData("migrations", "add")]
    [InlineData("migrations", "add", "ctms")]
    [InlineData("migrations", "verify", "--no-such-option")]
    public void Parse_RejectsInvalidComplianceAndWorkspaceInvocation(params string[] args) =>
        CliCommandLineTests.AssertRejects(args);
}
