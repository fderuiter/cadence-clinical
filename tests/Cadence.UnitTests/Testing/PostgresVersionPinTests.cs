using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Cli.Services;
using Cadence.Testing.Persistence;
using Cadence.ToolingCore.OpenApi;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Testing;

/// <summary>
/// The database tests, CI and the docker compose stack run one digest-pinned PostgreSQL major (#5438, ADR-2533, ADR-2539), so the
/// evidence qualifies the version developers run.
/// </summary>
public sealed partial class PostgresVersionPinTests
{
    private static readonly string Root = OpenApiToolingService.FindRepoRoot();

    private static readonly string[] ParityFiles =
    [
        "packages/testing/src/Persistence/PostgresTestImage.cs",
        "docker/docker-compose.yml",
        ".github/workflows/ci.yml",
        ".github/workflows/production-pipeline.yml",
        "apps/apphost/src/Program.cs",
    ];

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
    public void Dependabot_BlocksStandaloneComposePostgresDigestBumps()
    {
        string dependabot = File.ReadAllText(Path.Combine(Root, ".github", "dependabot.yml"));
        Match composeSection = ComposeDependabotSection().Match(dependabot);
        composeSection.Success.Should().BeTrue("dependabot.yml must declare a docker-compose ecosystem block for /docker");

        string block = composeSection.Value;
        UnconditionalPostgresIgnore().IsMatch(block).Should().BeTrue(
            "docker-compose /docker must ignore postgres unconditionally without update-types so standalone digest updates cannot drift from PostgresTestImage.cs");
        ScopedPostgresIgnore().IsMatch(block).Should().BeFalse(
            "scoping postgres ignore to update-types or versions permits standalone @sha256 digest bumps in docker/docker-compose.yml");
    }

    [Fact]
    public void DependencyValidatorService_RejectsPostgresImageOrDigestDriftAcrossComposeCiAndAppHost()
    {
        string[] parts = PostgresTestImage.Image["postgres:".Length..].Split("@sha256:");
        string expectedTag = parts[0];
        string expectedDigest = parts[1];
        string driftedDigest = (expectedDigest[0] == '0' ? '1' : '0') + expectedDigest[1..];
        string driftedImage = $"postgres:{expectedTag}@sha256:{driftedDigest}";

        RunParityProbe(null, string.Empty, string.Empty).Should().Be(0,
            "matching PostgresTestImage.Image references across compose, CI workflows, and AppHost must pass");

        RunParityProbe("docker/docker-compose.yml", PostgresTestImage.Image, driftedImage).Should().Be(1,
            "drifting the postgres @sha256 digest in docker/docker-compose.yml must fail DependencyValidatorService");

        RunParityProbe(".github/workflows/ci.yml", PostgresTestImage.Image, driftedImage, replaceFirstOnly: true).Should().Be(1,
            "drifting a postgres service container @sha256 digest in .github/workflows/ci.yml must fail DependencyValidatorService");

        RunParityProbe(".github/workflows/production-pipeline.yml", PostgresTestImage.Image, driftedImage).Should().Be(1,
            "drifting the postgres service container @sha256 digest in .github/workflows/production-pipeline.yml must fail DependencyValidatorService");

        RunParityProbe("apps/apphost/src/Program.cs", $".WithImageSHA256(\"{expectedDigest}\")", $".WithImageSHA256(\"{driftedDigest}\")")
            .Should().Be(1, "drifting WithImageSHA256 in apps/apphost/src/Program.cs must fail DependencyValidatorService");

        RunParityProbe("apps/apphost/src/Program.cs", $".WithImageTag(\"{expectedTag}\")", ".WithImageTag(\"18-bookworm\")")
            .Should().Be(1, "drifting WithImageTag in apps/apphost/src/Program.cs must fail DependencyValidatorService");
    }

    [Fact]
    public void LocalCi_ProbesForTheSameMajor() =>
        LocalCiEnvironment.PostgresMajor.Should().Be(PostgresTestImage.Major);

    [Fact]
    public void NoTestFixture_NamesItsOwnPostgresImage()
    {
        var literals = Directory.EnumerateFiles(Path.Combine(Root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.EndsWith(nameof(PostgresVersionPinTests) + ".cs", StringComparison.Ordinal))
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

    private static int RunParityProbe(
        string? mutatedRelativePath,
        string originalSubstring,
        string replacementSubstring,
        bool replaceFirstOnly = false)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"cadence-pg-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            foreach (string relativePath in ParityFiles)
            {
                string sourcePath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                string destPath = Path.Combine(tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                string content = File.ReadAllText(sourcePath);

                if (string.Equals(relativePath, mutatedRelativePath, StringComparison.Ordinal))
                {
                    if (replaceFirstOnly)
                    {
                        int idx = content.IndexOf(originalSubstring, StringComparison.Ordinal);
                        idx.Should().BeGreaterThanOrEqualTo(0);
                        content = string.Concat(
                            content.AsSpan(0, idx),
                            replacementSubstring,
                            content.AsSpan(idx + originalSubstring.Length));
                    }
                    else
                    {
                        content = content.Replace(originalSubstring, replacementSubstring, StringComparison.Ordinal);
                    }
                }

                File.WriteAllText(destPath, content);
            }

            return DependencyValidatorService.ValidateDependencies(tempDir);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [GeneratedRegex(@"postgres:[0-9][^\s""']*")]
    private static partial Regex PostgresImages();

    [GeneratedRegex(@"""postgres:[0-9]")]
    private static partial Regex PostgresImageLiteral();

    [GeneratedRegex(@"- package-ecosystem:\s*""docker-compose""[\s\S]*?(?=\r?\n  - package-ecosystem:|\z)")]
    private static partial Regex ComposeDependabotSection();

    [GeneratedRegex(@"-\s*dependency-name:\s*""postgres""\s*(?:\r?\n\s*(?:#.*)?)*(?:\r?\n\s*-\s*dependency-name:|\r?\n  - |\z)")]
    private static partial Regex UnconditionalPostgresIgnore();

    [GeneratedRegex(@"-\s*dependency-name:\s*""postgres""\s*\r?\n\s*(?:update-types|versions):")]
    private static partial Regex ScopedPostgresIgnore();
}
