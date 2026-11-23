using Xunit;

namespace Cadence.UnitTests.Packages.Cli;

public sealed class ValidationAndGeneratorCliCommandTests
{
    [Theory]
    [InlineData("clean-secrets-baseline")]
    [InlineData("clean-secrets-baseline", "--check")]
    [InlineData("generate-schema-doc", "--validate")]
    [InlineData("generate-demo-manifest")]
    [InlineData("generate-demo-manifest", "--check")]
    [InlineData("generate-rtm", "--validate", "--draft")]
    [InlineData("settings", "export", "--check")]
    public void Parse_AcceptsDocumentedValidationAndGeneratorInvocation(params string[] args) =>
        CliCommandLineTests.AssertParses(args);

    [Theory]
    [InlineData("generate-demo-manifest", "--no-such-option")]
    public void Parse_RejectsInvalidValidationAndGeneratorInvocation(params string[] args) =>
        CliCommandLineTests.AssertRejects(args);
}
