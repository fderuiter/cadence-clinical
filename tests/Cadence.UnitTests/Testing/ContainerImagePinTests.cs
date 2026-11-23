using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Cadence.Cli.Services;
using Cadence.ToolingCore.OpenApi;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Testing;

/// <summary>
/// Keeps container base image pins, non-root <c>USER</c> rules, <c>APP_NAME</c> targets, Hadolint discovery, CLI tool
/// manifests, MSBuild cohort properties and skew targets, <c>pnpm</c> workspace catalog references,
/// <see cref="DependencyValidatorService"/>, and Dependabot rules aligned with <c>global.json</c>,
/// <c>Directory.Build.props</c>, ADR-2233, ADR-2327, and ADR-2527.
/// </summary>
public sealed partial class ContainerImagePinTests
{
    private static readonly string Root = OpenApiToolingService.FindRepoRoot();
    private static readonly string[] PackageDependencySections =
        ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"];

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
    public void DotnetToolsAndDockerfile_AlignDotnetEfWithDotNetServicingVersion()
    {
        var propsDoc = XDocument.Load(Path.Combine(Root, "Directory.Build.props"));
        string servicingVersion = ReadMsBuildProperty(propsDoc, "DotNetServicingVersion");
        servicingVersion.Should().StartWith("10.0.");

        using var toolsJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, ".config", "dotnet-tools.json")));
        var dotnetEf = toolsJson.RootElement.GetProperty("tools").GetProperty("dotnet-ef");
        dotnetEf.GetProperty("version").GetString().Should().Be(servicingVersion,
            ".config/dotnet-tools.json must align dotnet-ef with $(DotNetServicingVersion)");
        dotnetEf.GetProperty("rollForward").GetBoolean().Should().BeFalse(
            "dotnet-ef must disable rollForward for deterministic CLI execution");

        string dockerfile = File.ReadAllText(Path.Combine(Root, "docker", "Dockerfile.dotnet"));
        string dockerfileEfVersion = DotnetEfExactVersion().Match(dockerfile).Groups["version"].Value;
        dockerfileEfVersion.Should().Be(servicingVersion,
            "docker/Dockerfile.dotnet must install the same dotnet-ef version as .config/dotnet-tools.json and $(DotNetServicingVersion)");
    }

    [Fact]
    public void SceDockerfiles_MatchOpenApiAndAdr2327RuntimeBaselines()
    {
        using var interopSpec = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Root, "docs", "openapi", "interop_openapi.json")));
        var sceProperties = interopSpec.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("Interop_ExecutionSandboxConfigDto")
            .GetProperty("properties");

        string expectedPython = sceProperties.GetProperty("python_version").GetProperty("default").GetString()!;
        string expectedR = sceProperties.GetProperty("r_version").GetProperty("default").GetString()!;
        expectedPython.Should().Be("3.14.0", "ADR-2327 and interop_openapi.json lock the GxP Python statistical runtime baseline to 3.14.0");
        expectedR.Should().Be("4.4.2", "interop_openapi.json locks the GxP R statistical runtime baseline to 4.4.2");

        string expectedPythonMajorMinor = string.Join('.', expectedPython.Split('.')[..2]);
        string pythonDockerfile = File.ReadAllText(Path.Combine(Root, "docker", "sce", "python", "Dockerfile"));
        var pythonMatch = ScePythonBaseImage().Match(pythonDockerfile);
        pythonMatch.Success.Should().BeTrue("docker/sce/python/Dockerfile must pin python:<major.minor>-slim@sha256:<digest>");
        pythonMatch.Groups["version"].Value.Should().Be(expectedPythonMajorMinor);

        string rDockerfile = File.ReadAllText(Path.Combine(Root, "docker", "sce", "r", "Dockerfile"));
        var rMatch = SceRBaseImage().Match(rDockerfile);
        rMatch.Success.Should().BeTrue("docker/sce/r/Dockerfile must pin r-base:<version>@sha256:<digest>");
        rMatch.Groups["version"].Value.Should().Be(expectedR);
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
    public void MsBuildPropsAndPackages_EliminateDuplicateCohortLiteralsAndAlignAppHost()
    {
        var propsDoc = XDocument.Load(Path.Combine(Root, "Directory.Build.props"));
        ReadMsBuildProperty(propsDoc, "DotNetServicingVersion").Should().Be("10.0.15");
        ReadMsBuildProperty(propsDoc, "AspireHostingVersion").Should().Be("13.6.5");
        ReadMsBuildProperty(propsDoc, "IdentityModelVersion").Should().Be("8.23.0");
        ReadMsBuildProperty(propsDoc, "DotNetAiVersion").Should().Be("10.11.0");

        var appHostDoc = XDocument.Load(Path.Combine(Root, "apps", "apphost", "src", "Cadence.AppHost.csproj"));
        string? appHostSdkVersion = appHostDoc.Descendants("Sdk")
            .FirstOrDefault(e => e.Attribute("Name")?.Value == "Aspire.AppHost.Sdk")
            ?.Attribute("Version")?.Value;
        appHostSdkVersion.Should().Be("$(AspireHostingVersion)",
            "Cadence.AppHost.csproj must inherit Aspire.AppHost.Sdk version from $(AspireHostingVersion)");

        var packagesDoc = XDocument.Load(Path.Combine(Root, "Directory.Packages.props"));
        var packageVersions = packagesDoc.Descendants("PackageVersion")
            .Select(e => (
                Id: e.Attribute("Include")?.Value ?? string.Empty,
                Version: e.Attribute("Version")?.Value ?? string.Empty))
            .ToList();

        packageVersions.Should().NotBeEmpty();

        foreach (var (id, version) in packageVersions)
        {
            if (id.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal))
            {
                version.Should().Be("$(DotNetAiVersion)", $"{id} must reference $(DotNetAiVersion)");
            }
            else if (id.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) ||
                     id.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                     id.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) ||
                     string.Equals(id, "System.Security.Cryptography.Pkcs", StringComparison.Ordinal))
            {
                version.Should().Be("$(DotNetServicingVersion)", $"{id} must reference $(DotNetServicingVersion)");
            }
            else if (id.StartsWith("Aspire.Hosting.", StringComparison.Ordinal))
            {
                version.Should().Be("$(AspireHostingVersion)", $"{id} must reference $(AspireHostingVersion)");
            }
            else if (id.StartsWith("Microsoft.IdentityModel.", StringComparison.Ordinal) ||
                     id.StartsWith("System.IdentityModel.", StringComparison.Ordinal))
            {
                version.Should().Be("$(IdentityModelVersion)", $"{id} must reference $(IdentityModelVersion)");
            }
        }

        var literalVersions = packageVersions
            .Where(p => !p.Version.StartsWith("$(", StringComparison.Ordinal))
            .Select(p => p.Version)
            .ToList();
        literalVersions.Should().OnlyHaveUniqueItems(
            "shared NuGet cohorts must use MSBuild properties in Directory.Build.props instead of repeating literal versions");
    }

    [Fact]
    public void DirectoryBuildTargets_FailsCompilationOnDotNet11OrSplitIdentityModelOrAspireSkew()
    {
        var (dotNet11Exit, dotNet11Out) = RunSkewProbeBuild(
            """<ResolvedCompileFileDefinitions Include="Microsoft.Extensions.Logging.Abstractions.dll" NuGetPackageId="Microsoft.Extensions.Logging.Abstractions" NuGetPackageVersion="11.0.0" />""");
        dotNet11Exit.Should().NotBe(0, "resolving a .NET 11.0.0 framework package on net10.0 must fail compilation");
        dotNet11Out.Should().Contain("CADDEP001");

        var (identityExit, identityOut) = RunSkewProbeBuild(
            """
            <ResolvedCompileFileDefinitions Include="Microsoft.IdentityModel.Tokens.dll" NuGetPackageId="Microsoft.IdentityModel.Tokens" NuGetPackageVersion="8.23.0" />
            <ResolvedCompileFileDefinitions Include="Microsoft.IdentityModel.Protocols.dll" NuGetPackageId="Microsoft.IdentityModel.Protocols" NuGetPackageVersion="8.19.2" />
            """);
        identityExit.Should().NotBe(0, "splitting IdentityModel package versions must fail compilation");
        identityOut.Should().Contain("CADDEP002");

        var (aspireExit, aspireOut) = RunSkewProbeBuild(
            """<_PackageDependenciesDesignTime Include="Aspire.Hosting.AppHost/13.5.4" Name="Aspire.Hosting.AppHost" />""");
        aspireExit.Should().NotBe(0, "resolving an Aspire package version mismatched with $(AspireHostingVersion) must fail compilation");
        aspireOut.Should().Contain("CADDEP003");
    }

    [Fact]
    public void PnpmWorkspaceCatalog_GovernsAllSharedFrontendPackagesWithoutDuplicateLiterals()
    {
        var catalog = ReadPnpmCatalog(Path.Combine(Root, "pnpm-workspace.yaml"));
        catalog.Should().ContainKeys(
            "@axe-core/playwright",
            "@playwright/test",
            "@typescript-eslint/eslint-plugin",
            "@typescript-eslint/parser",
            "@vitejs/plugin-vue",
            "@vitest/coverage-v8",
            "axe-core",
            "eslint-plugin-vue",
            "pinia",
            "typescript",
            "typescript-eslint",
            "vite",
            "vitest",
            "vue",
            "vue-router");

        catalog["@typescript-eslint/eslint-plugin"].Should().Be("^8.73.0");
        catalog["@typescript-eslint/parser"].Should().Be("^8.73.0");
        catalog["typescript-eslint"].Should().Be("^8.73.0");

        var manifestPaths = DiscoverWorkspacePackageJsonPaths();
        manifestPaths.Should().HaveCountGreaterThanOrEqualTo(5);

        var literalByPackage = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var allRawLiterals = new List<string>();

        foreach (string manifestPath in manifestPaths)
        {
            string relative = Path.GetRelativePath(Root, manifestPath).Replace('\\', '/');
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));

            foreach (string section in PackageDependencySections)
            {
                if (!doc.RootElement.TryGetProperty(section, out var deps))
                {
                    continue;
                }

                foreach (var dep in deps.EnumerateObject())
                {
                    string spec = dep.Value.GetString()!;
                    if (catalog.ContainsKey(dep.Name))
                    {
                        spec.Should().Be("catalog:",
                            $"{relative} ({section}) must reference '{dep.Name}' via 'catalog:' instead of repeating a literal version");
                        continue;
                    }

                    if (spec.StartsWith("workspace:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!literalByPackage.TryGetValue(dep.Name, out var owners))
                    {
                        owners = [];
                        literalByPackage[dep.Name] = owners;
                    }

                    owners.Add(relative);
                    allRawLiterals.Add(spec);
                }
            }
        }

        literalByPackage.Where(kvp => kvp.Value.Count > 1).Should().BeEmpty(
            "every npm package referenced by more than one workspace package.json must live in pnpm-workspace.yaml catalog:");
        allRawLiterals.Should().OnlyHaveUniqueItems(
            "workspace package.json manifests should not duplicate version literals");

        string lockfile = File.ReadAllText(Path.Combine(Root, "pnpm-lock.yaml"));
        lockfile.Should().Contain("catalogs:")
            .And.Contain("'@typescript-eslint/eslint-plugin':")
            .And.Contain("specifier: ^8.73.0");
    }

    [Fact]
    public void Dependabot_CoversAllDockerDirectoriesAndEnforcesGroupsAndRuntimeIgnores()
    {
        string dependabotPath = Path.Combine(Root, ".github", "dependabot.yml");
        string dependabot = File.ReadAllText(dependabotPath);

        dependabot.Should().Contain("package-ecosystem: \"nuget\"")
            .And.Contain("dotnet-10-servicing:")
            .And.Contain("dotnet-ai:")
            .And.Contain("aspire:")
            .And.Contain("identitymodel:")
            .And.Contain("opentelemetry:")
            .And.Contain("masstransit:")
            .And.Contain("fluentvalidation:")
            .And.Contain("ai-providers:")
            .And.Contain("code-analyzers:")
            .And.Contain("dotnet-testing:")
            .And.Contain("nuget-dependencies:")
            .And.Contain("package-ecosystem: \"npm\"")
            .And.Contain("npm-workspace:")
            .And.Contain("npm-workspace-major:")
            .And.Contain("package-ecosystem: \"docker\"")
            .And.Contain("dotnet-containers:")
            .And.Contain("sce-python:")
            .And.Contain("sce-r:")
            .And.Contain("directory: \"/docker\"")
            .And.Contain("directory: \"/docker/sce/python\"")
            .And.Contain("directory: \"/docker/sce/r\"")
            .And.Contain("package-ecosystem: \"docker-compose\"")
            .And.Contain("local-stack:")
            .And.Contain("package-ecosystem: \"github-actions\"")
            .And.Contain("workflow-actions:");

        DotnetMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for mcr.microsoft.com/dotnet/*");
        NugetAspNetMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for Microsoft.AspNetCore.*");
        NugetEfCoreMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for Microsoft.EntityFrameworkCore*");
        NugetDotnetEfMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for dotnet-ef");
        NpmTypesNodeMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for @types/node");
        ScePythonIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore semver-major and semver-minor bumps on python in /docker/sce/python (ADR-2327)");
        SceRIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore semver-major, semver-minor, and semver-patch bumps on r-base in /docker/sce/r");

        var parseErrors = new List<string>();
        var blocks = DependencyValidatorService.ParseDependabotUpdates(dependabotPath, parseErrors);
        parseErrors.Should().BeEmpty();
        blocks.Should().HaveCount(7);
        blocks.Select(b => b.ScheduleDay).Distinct(StringComparer.Ordinal)
            .Should().BeEquivalentTo("monday", "tuesday", "wednesday", "thursday", "friday");
        blocks.Sum(b => b.OpenPullRequestsLimit ?? 0).Should().Be(DependencyValidatorService.MaxTotalOpenPullRequestsLimit);

        var npmBlock = blocks.Single(b => b.PackageEcosystem == "npm" && b.Directory == "/");
        npmBlock.ScheduleDay.Should().Be("wednesday");
        npmBlock.OpenPullRequestsLimit.Should().Be(DependencyValidatorService.MaxNpmOpenPullRequestsLimit);
    }

    [Fact]
    public void Dependabot_EnforcesFullGroupCoverageAcrossAllRepositoryDependencies()
    {
        var parseErrors = new List<string>();
        var blocks = DependencyValidatorService.ParseDependabotUpdates(
            Path.Combine(Root, ".github", "dependabot.yml"),
            parseErrors);
        parseErrors.Should().BeEmpty();

        var nugetBlock = blocks.Single(b => b.PackageEcosystem == "nuget" && b.Directory == "/");
        var nugetPins = XDocument.Load(Path.Combine(Root, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(e => e.Attribute("Include")!.Value)
            .ToList();
        nugetPins.Should().HaveCount(64);

        foreach (string pin in nugetPins.Append("dotnet-ef"))
        {
            DependencyValidatorService.FindMatchingGroup(pin, nugetBlock)
                .Should().NotBeNullOrWhiteSpace($"NuGet dependency '{pin}' must belong to a Dependabot group");
        }

        DependencyValidatorService.FindMatchingGroup("Microsoft.EntityFrameworkCore.Design", nugetBlock).Should().Be("dotnet-10-servicing");
        DependencyValidatorService.FindMatchingGroup("Anthropic", nugetBlock).Should().Be("ai-providers");
        DependencyValidatorService.FindMatchingGroup("ModelContextProtocol", nugetBlock).Should().Be("ai-providers");
        DependencyValidatorService.FindMatchingGroup("Microsoft.CodeAnalysis.NetAnalyzers", nugetBlock).Should().Be("code-analyzers");
        DependencyValidatorService.FindMatchingGroup("SonarAnalyzer.CSharp", nugetBlock).Should().Be("code-analyzers");
        DependencyValidatorService.FindMatchingGroup("xunit.v3", nugetBlock).Should().Be("dotnet-testing");
        DependencyValidatorService.FindMatchingGroup("Polly", nugetBlock).Should().Be("nuget-dependencies");
        DependencyValidatorService.FindMatchingGroup("dotnet-ef", nugetBlock).Should().Be("nuget-dependencies");

        var npmBlock = blocks.Single(b => b.PackageEcosystem == "npm" && b.Directory == "/");
        var npmPackages = CollectAllWorkspaceNpmPackages();
        npmPackages.Should().HaveCount(37);
        foreach (string pkg in npmPackages)
        {
            DependencyValidatorService.FindMatchingGroup(pkg, npmBlock, "minor").Should().Be("npm-workspace");
            DependencyValidatorService.FindMatchingGroup(pkg, npmBlock, "patch").Should().Be("npm-workspace");
            if (!string.Equals(pkg, "@types/node", StringComparison.Ordinal))
            {
                DependencyValidatorService.FindMatchingGroup(pkg, npmBlock, "major").Should().Be("npm-workspace-major");
            }
        }

        var composeBlock = blocks.Single(b => b.PackageEcosystem == "docker-compose" && b.Directory == "/docker");
        var composeImages = ComposeImageLine().Matches(File.ReadAllText(Path.Combine(Root, "docker", "docker-compose.yml")))
            .Select(m => m.Groups["image"].Value.Split(':')[0])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        composeImages.Should().HaveCount(6);
        foreach (string image in composeImages)
        {
            DependencyValidatorService.FindMatchingGroup(image, composeBlock).Should().Be("local-stack");
        }

        var actionsBlock = blocks.Single(b => b.PackageEcosystem == "github-actions" && b.Directory == "/");
        var workflowActions = CollectAllExternalWorkflowActions();
        workflowActions.Should().HaveCount(14);
        foreach (string action in workflowActions)
        {
            DependencyValidatorService.FindMatchingGroup(action, actionsBlock).Should().Be("workflow-actions");
        }
    }

    [Fact]
    public void DependencyValidatorService_FailsClosedOnMalformedJsonAndPassesOnCleanWorkspace()
    {
        DependencyValidatorService.ValidateDependencies(Root).Should().Be(0,
            "HEAD manifests must pass JSON validation, Dependabot batching coverage, and client crypto checks");

        string emptyTemp = CreateTempDir();
        try
        {
            DependencyValidatorService.ValidateDependencies(emptyTemp).Should().Be(0,
                "isolated temporary fixtures without manifests must skip cleanly");
        }
        finally
        {
            Directory.Delete(emptyTemp, recursive: true);
        }

        AssertMalformedManifestFails("package.json", "{ \"devDependencies\": { ");
        AssertMalformedManifestFails("apps/web/package.json", "{ invalid json ");
        AssertMalformedManifestFails("packages/ui/package.json", """{ "dependencies": ["not-an-object"] }""");
        AssertMalformedManifestFails(".config/dotnet-tools.json", "{ \"tools\": ");
        AssertMalformedManifestFails(".config/dotnet-tools.json", """{ "version": 1, "isRoot": true }""");
        AssertMalformedManifestFails("packages/ui/package.json", """{ "dependencies": { "jose": "^5.0.0" } }""");
    }

    [Fact]
    public void DependencyValidatorService_RejectsUngroupedDependenciesSplitNpmAndScheduleViolations()
    {
        // 1. Ungrouped NuGet dependency fails closed when catch-all group is absent.
        AssertDependabotFixtureFails(new Dictionary<string, string>
        {
            ["Directory.Packages.props"] = """
                <Project>
                  <ItemGroup>
                    <PackageVersion Include="Polly" Version="8.6.5" />
                  </ItemGroup>
                </Project>
                """,
            [".github/dependabot.yml"] = BuildMinimalDependabotYaml("nuget", "/", "monday", 1, "dotnet-ai:\n        patterns:\n          - \"Microsoft.Extensions.AI*\""),
        });

        // 2. Ungrouped npm package in pnpm-workspace.yaml catalog fails closed.
        AssertDependabotFixtureFails(new Dictionary<string, string>
        {
            ["package.json"] = """{ "name": "fixture", "private": true }""",
            ["pnpm-workspace.yaml"] = "packages:\n  - packages/*\ncatalog:\n  zod: ^3.24.2\n",
            [".github/dependabot.yml"] = BuildMinimalDependabotYaml("npm", "/", "wednesday", 1, "vitest:\n        patterns:\n          - \"vitest*\""),
        });

        // 3. Splitting npm minor/patch updates across multiple groups fails closed.
        AssertDependabotFixtureFails(new Dictionary<string, string>
        {
            ["package.json"] = """{ "devDependencies": { "vitest": "^4.0.0", "vue": "^3.5.0" } }""",
            [".github/dependabot.yml"] = BuildMinimalDependabotYaml(
                "npm",
                "/",
                "wednesday",
                1,
                "vitest:\n        patterns:\n          - \"vitest*\"\n      vue:\n        patterns:\n          - \"*\""),
        });

        // 4. npm open-pull-requests-limit > 1 fails closed.
        AssertDependabotFixtureFails(new Dictionary<string, string>
        {
            ["package.json"] = """{ "devDependencies": { "vitest": "^4.0.0" } }""",
            [".github/dependabot.yml"] = BuildMinimalDependabotYaml("npm", "/", "wednesday", 2, "npm-workspace:\n        patterns:\n          - \"*\""),
        });

        // 5. Unstaggered multi-block schedules on the same weekday fail closed.
        AssertDependabotFixtureFails(new Dictionary<string, string>
        {
            [".github/dependabot.yml"] = """
                version: 2
                updates:
                  - package-ecosystem: "nuget"
                    directory: "/"
                    schedule:
                      interval: "weekly"
                      day: "monday"
                    open-pull-requests-limit: 1
                    groups:
                      all-nuget:
                        patterns:
                          - "*"
                  - package-ecosystem: "github-actions"
                    directory: "/"
                    schedule:
                      interval: "weekly"
                      day: "monday"
                    open-pull-requests-limit: 1
                    groups:
                      workflow-actions:
                        patterns:
                          - "*"
                """,
        });

        // 6. NuGet open-pull-requests-limit > 3 or total open-pull-requests-limit > 9 fails closed.
        AssertDependabotFixtureFails(new Dictionary<string, string>
        {
            [".github/dependabot.yml"] = BuildMinimalDependabotYaml("nuget", "/", "monday", 4, "all-nuget:\n        patterns:\n          - \"*\""),
        });
        AssertDependabotFixtureFails(new Dictionary<string, string>
        {
            [".github/dependabot.yml"] = string.Join(
                "\n",
                "version: 2",
                "updates:",
                BuildUpdateEntry("nuget", "/", "monday", 3, "g1"),
                BuildUpdateEntry("npm", "/", "wednesday", 1, "g2"),
                BuildUpdateEntry("docker", "/docker", "tuesday", 1, "g3"),
                BuildUpdateEntry("docker", "/docker/a", "tuesday", 1, "g4"),
                BuildUpdateEntry("docker", "/docker/b", "thursday", 1, "g5"),
                BuildUpdateEntry("docker", "/docker/c", "thursday", 1, "g6"),
                BuildUpdateEntry("docker-compose", "/docker", "friday", 1, "g7"),
                BuildUpdateEntry("github-actions", "/", "friday", 1, "g8")),
        });

        // 7. Ungrouped docker-compose image or workflow action fails closed.
        AssertDependabotFixtureFails(new Dictionary<string, string>
        {
            ["docker/docker-compose.yml"] = "services:\n  db:\n    image: postgres:17-alpine@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\n", // pragma: allowlist secret
            [".github/dependabot.yml"] = BuildMinimalDependabotYaml("docker-compose", "/docker", "tuesday", 1, "only-nginx:\n        patterns:\n          - \"nginx*\""),
        });
        AssertDependabotFixtureFails(new Dictionary<string, string>
        {
            [".github/workflows/ci.yml"] = "name: CI\non: push\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6\n", // pragma: allowlist secret
            [".github/dependabot.yml"] = BuildMinimalDependabotYaml("github-actions", "/", "friday", 1, "only-node:\n        patterns:\n          - \"actions/setup-node*\""),
        });
    }

    private static string BuildMinimalDependabotYaml(string ecosystem, string directory, string day, int prLimit, string groupsBody) =>
        $$"""
        version: 2
        updates:
          - package-ecosystem: "{{ecosystem}}"
            directory: "{{directory}}"
            schedule:
              interval: "weekly"
              day: "{{day}}"
            open-pull-requests-limit: {{prLimit}}
            groups:
              {{groupsBody}}
        """;

    private static string BuildUpdateEntry(string ecosystem, string directory, string day, int prLimit, string groupName) =>
        $$"""
          - package-ecosystem: "{{ecosystem}}"
            directory: "{{directory}}"
            schedule:
              interval: "weekly"
              day: "{{day}}"
            open-pull-requests-limit: {{prLimit}}
            groups:
              {{groupName}}:
                patterns:
                  - "*"
        """;

    private static void AssertDependabotFixtureFails(IReadOnlyDictionary<string, string> files)
    {
        string tempDir = CreateTempDir();
        try
        {
            foreach (var (relativePath, content) in files)
            {
                string fullPath = Path.Combine(tempDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, content);
            }

            DependencyValidatorService.ValidateDependencies(tempDir).Should().Be(1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static SortedSet<string> CollectAllWorkspaceNpmPackages()
    {
        var packages = new SortedSet<string>(
            ReadPnpmCatalog(Path.Combine(Root, "pnpm-workspace.yaml")).Keys,
            StringComparer.Ordinal);

        foreach (string manifestPath in DiscoverWorkspacePackageJsonPaths())
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            foreach (string section in PackageDependencySections)
            {
                if (!doc.RootElement.TryGetProperty(section, out var deps) || deps.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var dep in deps.EnumerateObject())
                {
                    string spec = dep.Value.GetString()!;
                    if (!spec.StartsWith("workspace:", StringComparison.Ordinal))
                    {
                        packages.Add(dep.Name);
                    }
                }
            }
        }

        return packages;
    }

    private static SortedSet<string> CollectAllExternalWorkflowActions()
    {
        var actions = new SortedSet<string>(StringComparer.Ordinal);
        string workflowsDir = Path.Combine(Root, ".github", "workflows");
        foreach (string file in Directory.GetFiles(workflowsDir, "*.yml", SearchOption.TopDirectoryOnly))
        {
            foreach (string rawLine in File.ReadAllLines(file))
            {
                string line = rawLine.Split('#')[0];
                var match = WorkflowUsesLine().Match(line);
                if (!match.Success)
                {
                    continue;
                }

                string reference = match.Groups["uses"].Value.Trim('"', '\'');
                if (reference.StartsWith("./", StringComparison.Ordinal) || reference.StartsWith("docker://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                actions.Add(reference.Split('@')[0]);
            }
        }

        return actions;
    }

    private static void AssertMalformedManifestFails(string relativePath, string content)
    {
        string tempDir = CreateTempDir();
        try
        {
            string fullPath = Path.Combine(tempDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);

            DependencyValidatorService.ValidateDependencies(tempDir).Should().Be(1,
                $"DependencyValidatorService must fail closed on invalid manifest '{relativePath}'");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static (int ExitCode, string Output) RunSkewProbeBuild(string injectedItemsXml)
    {
        string tempDir = CreateTempDir();
        try
        {
            string propsPath = Path.Combine(Root, "Directory.Build.props");
            string targetsPath = Path.Combine(Root, "Directory.Build.targets");
            string csproj = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="{propsPath}" />
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                    <RunAnalyzers>false</RunAnalyzers>
                    <GenerateDocumentationFile>false</GenerateDocumentationFile>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Remove="Microsoft.CodeAnalysis.NetAnalyzers" />
                    <PackageReference Remove="SonarAnalyzer.CSharp" />
                    <PackageReference Remove="Microsoft.CodeAnalysis.BannedApiAnalyzers" />
                  </ItemGroup>
                  <Import Project="{targetsPath}" />
                  <Target Name="InjectSkewFixture" BeforeTargets="ValidateDependencyAlignment">
                    <ItemGroup>
                      {injectedItemsXml}
                    </ItemGroup>
                  </Target>
                </Project>
                """;

            File.WriteAllText(Path.Combine(tempDir, "Directory.Build.props"), "<Project />");
            File.WriteAllText(Path.Combine(tempDir, "Directory.Build.targets"), "<Project />");
            File.WriteAllText(Path.Combine(tempDir, "Directory.Packages.props"), "<Project />");
            string projectFile = Path.Combine(tempDir, "SkewProbe.csproj");
            File.WriteAllText(projectFile, csproj);
            File.WriteAllText(Path.Combine(tempDir, "Probe.cs"), "namespace SkewProbe; public static class Probe { }\n");

            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = tempDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("msbuild");
            psi.ArgumentList.Add(projectFile);
            psi.ArgumentList.Add("-t:ValidateDependencyAlignment");
            psi.ArgumentList.Add("-nologo");

            using var process = Process.Start(psi)!;
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout + "\n" + stderr);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cadence-dep-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ReadMsBuildProperty(XDocument doc, string propertyName) =>
        doc.Descendants(propertyName).FirstOrDefault()?.Value
        ?? throw new InvalidOperationException($"Missing MSBuild property '{propertyName}' in Directory.Build.props.");

    private static Dictionary<string, string> ReadPnpmCatalog(string workspaceYamlPath)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inCatalog = false;

        foreach (string line in File.ReadAllLines(workspaceYamlPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                inCatalog = string.Equals(line.Trim(), "catalog:", StringComparison.Ordinal);
                continue;
            }

            if (!inCatalog)
            {
                continue;
            }

            int colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex <= 0)
            {
                continue;
            }

            string key = line[..colonIndex].Trim().Trim('"', '\'');
            string value = line[(colonIndex + 1)..].Trim().Trim('"', '\'');
            entries[key] = value;
        }

        return entries;
    }

    private static List<string> DiscoverWorkspacePackageJsonPaths()
    {
        var result = new List<string> { Path.Combine(Root, "package.json") };
        foreach (string container in new[] { "apps", "packages" })
        {
            foreach (string dir in Directory.GetDirectories(Path.Combine(Root, container)).OrderBy(d => d, StringComparer.Ordinal))
            {
                string pkg = Path.Combine(dir, "package.json");
                if (File.Exists(pkg))
                {
                    result.Add(pkg);
                }
            }
        }

        return result;
    }

    [GeneratedRegex(@"<TargetFramework>net(\d+)\.\d+</TargetFramework>")]
    private static partial Regex TargetFrameworkMajor();

    [GeneratedRegex(@"^FROM mcr\.microsoft\.com/dotnet/(?<repo>sdk|aspnet):(?<major>\d+)\.\d+\.\d+@sha256:(?<digest>[0-9a-f]{64})\b", RegexOptions.Multiline)]
    private static partial Regex DotnetBaseImage();

    [GeneratedRegex(@"dotnet-ef --version (\d+)\.")]
    private static partial Regex DotnetEfVersionMajor();

    [GeneratedRegex(@"dotnet-ef --version (?<version>\d+\.\d+\.\d+)\b")]
    private static partial Regex DotnetEfExactVersion();

    [GeneratedRegex(@"^FROM python:(?<version>\d+\.\d+)-slim@sha256:[0-9a-f]{64}\s*$", RegexOptions.Multiline)]
    private static partial Regex ScePythonBaseImage();

    [GeneratedRegex(@"^FROM r-base:(?<version>\d+\.\d+\.\d+)@sha256:[0-9a-f]{64}\s*$", RegexOptions.Multiline)]
    private static partial Regex SceRBaseImage();

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

    [GeneratedRegex(@"dependency-name:\s*""Microsoft\.AspNetCore\.\*""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major""\]")]
    private static partial Regex NugetAspNetMajorIgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""Microsoft\.EntityFrameworkCore\*""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major""\]")]
    private static partial Regex NugetEfCoreMajorIgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""dotnet-ef""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major""\]")]
    private static partial Regex NugetDotnetEfMajorIgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""@types/node""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major""\]")]
    private static partial Regex NpmTypesNodeMajorIgnoreRule();

    [GeneratedRegex(@"^\s*(?:-\s*)?uses:\s*(?<uses>\S+)")]
    private static partial Regex WorkflowUsesLine();

    [GeneratedRegex(@"dependency-name:\s*""python""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major"",\s*""version-update:semver-minor""\]")]
    private static partial Regex ScePythonIgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""r-base""\s*\r?\n\s*update-types:\s*\[\s*""version-update:semver-major"",\s*""version-update:semver-minor"",\s*""version-update:semver-patch"",?\s*\]")]
    private static partial Regex SceRIgnoreRule();
}
