using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Cli.Services;
using Cadence.Testing.Persistence;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Testing;

/// <summary>
/// The database tests, CI and the docker compose stack run one digest-pinned PostgreSQL major (#5438, ADR-2533), so the
/// evidence qualifies the version developers run.
/// </summary>
public sealed partial class PostgresVersionPinTests
{
    private static readonly string Root = PreCommitOpenApiService.FindRepoRoot();

    [Fact]
    public void ComposeAndCi_RunTheTestImage()
    {
        string compose = File.ReadAllText(Path.Combine(Root, "docker", "docker-compose.yml"));
        string ci = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "ci.yml"));
        string productionPipeline = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "production-pipeline.yml"));

        PostgresImages().Matches(compose).Select(m => m.Value).Should().Equal(PostgresTestImage.Image);
        PostgresImages().Matches(ci).Select(m => m.Value).Should().Equal(PostgresTestImage.Image, PostgresTestImage.Image);
        PostgresImages().Matches(productionPipeline).Select(m => m.Value).Should().Equal(PostgresTestImage.Image);
        PostgresTestImage.Image.Should().StartWith($"postgres:{PostgresTestImage.Major}-").And.Contain("@sha256:");
    }

    [Fact]
    public void AppHost_RunsTheTestImage()
    {
        string appHost = File.ReadAllText(Path.Combine(Root, "apps", "apphost", "src", "Program.cs"));
        string[] parts = PostgresTestImage.Image["postgres:".Length..].Split("@sha256:");

        appHost.Should().Contain($".WithImageTag(\"{parts[0]}\")").And.Contain($".WithImageSHA256(\"{parts[1]}\")");
    }

    [Fact]
    public void LocalCi_ProbesForTheSameMajor() =>
        LocalCiEnvironment.PostgresMajor.Should().Be(PostgresTestImage.Major);

    [Fact]
    public void NoTestFixture_NamesItsOwnPostgresImage()
    {
        var literals = Directory.EnumerateFiles(Path.Combine(Root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", System.StringComparison.Ordinal)
                && !path.EndsWith(nameof(PostgresVersionPinTests) + ".cs", System.StringComparison.Ordinal))
            .Where(path => PostgresImageLiteral().IsMatch(File.ReadAllText(path)));

        literals.Should().BeEmpty("every fixture starts PostgresTestImage.Image");
    }

    [Theory]
    [InlineData(180001, true)]
    [InlineData(180010, true)]
    [InlineData(160015, false)]
    [InlineData(170000, false)]
    public void Mismatch_RefusesAServerOfAnotherMajor(int serverVersionNumber, bool usable) =>
        (PostgresTestImage.Mismatch(serverVersionNumber) is null).Should().Be(usable);

    [GeneratedRegex(@"postgres:[0-9][^\s""']*")]
    private static partial Regex PostgresImages();

    [GeneratedRegex(@"""postgres:[0-9]")]
    private static partial Regex PostgresImageLiteral();
}
