using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.Cli.Services;
using Cadence.ToolingCore.OpenApi;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Testing;

/// <summary>
/// Keeps container base image pins, non-root <c>USER</c> rules, <c>APP_NAME</c> targets, Hadolint discovery, and
/// Dependabot rules aligned with <c>global.json</c>, <c>Directory.Build.props</c>, and ADR-2233.
/// </summary>
public sealed partial class ContainerImagePinTests
{
    private static readonly string Root = OpenApiToolingService.FindRepoRoot();

    [Fact]
    public void DotnetDockerfile_MatchesGlobalJsonAndDirectoryBuildPropsMajor()
    {
        using var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "global.json")));
        string sdkVersion = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
        string expectedMajor = sdkVersion.Split('.')[0];

        string buildProps = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));
        string tfmMajor = TargetFrameworkMajor().Match(buildProps).Groups[1].Value;
        tfmMajor.Should().Be(expectedMajor, "Directory.Build.props and global.json target the same .NET major (ADR-2233)");

        string dockerfile = File.ReadAllText(Path.Combine(Root, "docker", "Dockerfile.dotnet"));
        var dotnetStages = DotnetBaseImage().Matches(dockerfile);
        dotnetStages.Should().HaveCount(2, "Dockerfile.dotnet defines the SDK dev stage and the ASP.NET runtime stage");

        foreach (Match match in dotnetStages)
        {
            match.Groups["major"].Value.Should().Be(expectedMajor,
                $"mcr.microsoft.com/dotnet/{match.Groups["repo"].Value} must stay on .NET {expectedMajor}");
            match.Groups["digest"].Value.Should().HaveLength(64);
        }

        DotnetEfVersionMajor().Match(dockerfile).Groups[1].Value.Should().Be(expectedMajor);
    }

    [Fact]
    public void EveryDockerfileAndComposeImage_IsPinnedBySha256Digest()
    {
        var dockerfiles = LocalCiEnvironment.DiscoverFiles(
            Root,
            new CiStep("lint-and-static", "Run Hadolint on Dockerfiles") { Files = CiFileSource.Dockerfiles },
            "origin/main");

        dockerfiles.Should().Equal(
            "./docker/Dockerfile.dotnet",
            "./docker/sce/python/Dockerfile",
            "./docker/sce/r/Dockerfile");

        foreach (string relativePath in dockerfiles)
        {
            string fullPath = Path.Combine(Root, relativePath.TrimStart('.', '/'));
            var stages = new HashSet<string>(StringComparer.Ordinal);

            foreach (string rawLine in File.ReadAllLines(fullPath))
            {
                var fromMatch = FromInstruction().Match(rawLine);
                if (!fromMatch.Success)
                {
                    continue;
                }

                string imageRef = fromMatch.Groups["image"].Value;
                if (!stages.Contains(imageRef))
                {
                    Sha256PinnedImage().IsMatch(imageRef).Should().BeTrue(
                        $"external base image '{imageRef}' in {relativePath} must pin both a tag and @sha256 digest");
                }

                if (fromMatch.Groups["alias"].Success)
                {
                    stages.Add(fromMatch.Groups["alias"].Value);
                }
            }
        }

        string compose = File.ReadAllText(Path.Combine(Root, "docker", "docker-compose.yml"));
        var composeImages = ComposeImageLine().Matches(compose).Select(m => m.Groups["image"].Value).ToList();
        composeImages.Should().NotBeEmpty();
        composeImages.Should().AllSatisfy(image =>
            Sha256PinnedImage().IsMatch(image).Should().BeTrue(
                $"compose image '{image}' must pin both a tag and @sha256 digest"));
    }

    [Fact]
    public void EveryRuntimeAndSceDockerfile_RunsAsNonRootUser()
    {
        foreach (string relativePath in new[]
        {
            "docker/Dockerfile.dotnet",
            "docker/sce/python/Dockerfile",
            "docker/sce/r/Dockerfile",
        })
        {
            string content = File.ReadAllText(Path.Combine(Root, relativePath));
            string finalStage = content[(content.LastIndexOf("\nFROM ", StringComparison.Ordinal) + 1)..];
            var users = UserInstruction().Matches(finalStage).Select(m => m.Groups["user"].Value).ToList();

            users.Should().NotBeEmpty($"{relativePath} final stage must declare a non-root USER");
            string activeUser = users[^1].Split(':')[0];
            activeUser.Should().NotBe("root").And.NotBe("0");

            if (relativePath.StartsWith("docker/sce/", StringComparison.Ordinal))
            {
                finalStage.Should().Contain($"chown -R {activeUser}:{activeUser} /workspace",
                    $"{relativePath} must grant /workspace ownership to '{activeUser}'");
            }
        }
    }

    [Fact]
    public void ComposeAndCiAppNameTargets_ResolveToExistingServiceProjects()
    {
        string compose = File.ReadAllText(Path.Combine(Root, "docker", "docker-compose.yml"));
        ComposeDotnetBuilds().Matches(compose).Should().HaveCount(19,
            "docker-compose.yml builds all 18 .NET services plus dotnet-dev from docker/Dockerfile.dotnet");

        var composeApps = ComposeAppName().Matches(compose).Select(m => m.Groups["app"].Value).ToList();
        composeApps.Should().HaveCount(18).And.OnlyHaveUniqueItems();

        string ci = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "ci.yml"));
        var ciTargets = CiMatrixTarget().Matches(ci).Select(m => m.Groups["target"].Value).ToList();
        ciTargets.Should().Equal("gateway", "kernel-api", "sce-python", "sce-r");
        ci.Should().Contain("output: \"trivy-results-${{ matrix.target }}.sarif\"")
            .And.Contain("category: \"trivy-${{ matrix.target }}\"");

        var ciDotnetApps = CiMatrixAppName().Matches(ci).Select(m => m.Groups["app"].Value).ToList();
        ciDotnetApps.Should().Equal("gateway", "kernel-api");

        foreach (string appName in composeApps.Concat(ciDotnetApps).Distinct(StringComparer.Ordinal))
        {
            string projectDir = Path.Combine(Root, "apps", appName, "src");
            Directory.Exists(projectDir).Should().BeTrue($"APP_NAME '{appName}' must resolve to apps/{appName}/src");
            Directory.GetFiles(projectDir, "*.csproj").Should().ContainSingle(
                $"apps/{appName}/src must contain a single .csproj for docker/Dockerfile.dotnet");
        }
    }

    [Fact]
    public void Dependabot_CoversAllDockerDirectoriesAndBlocksDotnetMajorBumps()
    {
        string dependabot = File.ReadAllText(Path.Combine(Root, ".github", "dependabot.yml"));

        dependabot.Should().Contain("package-ecosystem: \"docker\"")
            .And.Contain("directory: \"/docker\"")
            .And.Contain("directory: \"/docker/sce/python\"")
            .And.Contain("directory: \"/docker/sce/r\"")
            .And.Contain("package-ecosystem: \"docker-compose\"");

        DotnetMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for mcr.microsoft.com/dotnet/*");
    }

    [GeneratedRegex(@"<TargetFramework>net(\d+)\.\d+</TargetFramework>")]
    private static partial Regex TargetFrameworkMajor();

    [GeneratedRegex(@"^FROM mcr\.microsoft\.com/dotnet/(?<repo>sdk|aspnet):(?<major>\d+)\.\d+\.\d+@sha256:(?<digest>[0-9a-f]{64})\b", RegexOptions.Multiline)]
    private static partial Regex DotnetBaseImage();

    [GeneratedRegex(@"dotnet-ef --version (\d+)\.")]
    private static partial Regex DotnetEfVersionMajor();

    [GeneratedRegex(@"^\s*FROM\s+(?:--platform=\S+\s+)?(?<image>\S+)(?:\s+AS\s+(?<alias>\S+))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FromInstruction();

    [GeneratedRegex(@"^[^\s:@]+:[^\s@]+@sha256:[0-9a-f]{64}$")]
    private static partial Regex Sha256PinnedImage();

    [GeneratedRegex(@"^\s*image:\s*(?<image>\S+)", RegexOptions.Multiline)]
    private static partial Regex ComposeImageLine();

    [GeneratedRegex(@"^\s*USER\s+(?<user>\S+)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex UserInstruction();

    [GeneratedRegex(@"^\s*dockerfile:\s*docker/Dockerfile\.dotnet\s*$", RegexOptions.Multiline)]
    private static partial Regex ComposeDotnetBuilds();

    [GeneratedRegex(@"^\s*APP_NAME:\s*(?<app>[a-z0-9-]+)\s*$", RegexOptions.Multiline)]
    private static partial Regex ComposeAppName();

    [GeneratedRegex(@"^\s*-\s*target:\s*(?<target>[a-z0-9-]+)\s*$", RegexOptions.Multiline)]
    private static partial Regex CiMatrixTarget();

    [GeneratedRegex(@"^\s*app_name:\s*(?<app>[a-z0-9-]+)\s*$", RegexOptions.Multiline)]
    private static partial Regex CiMatrixAppName();

    [GeneratedRegex(@"dependency-name:\s*""mcr\.microsoft\.com/dotnet/\*""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major""\]")]
    private static partial Regex DotnetMajorIgnoreRule();
}
