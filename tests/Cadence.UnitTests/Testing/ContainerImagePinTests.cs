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
        List<string> expectedCiTargets = [.. composeApps, "sce-python", "sce-r"];
        ciTargets.Should().Equal(expectedCiTargets);
        ci.Should().Contain("docker compose -f docker/docker-compose.yml config -q")
            .And.Contain("docker/setup-buildx-action@")
            .And.Contain("docker/build-push-action@")
            .And.Contain("cache-from: type=gha")
            .And.Contain("cache-to: type=gha,mode=max")
            .And.Contain("output: \"trivy-results-${{ matrix.target }}.sarif\"")
            .And.Contain("category: \"trivy-${{ matrix.target }}\"");

        var ciDotnetApps = CiMatrixAppName().Matches(ci).Select(m => m.Groups["app"].Value).ToList();
        ciDotnetApps.Should().Equal(composeApps);

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
        ReadMsBuildProperty(propsDoc, "DotNetAiVersion").Should().Be("10.11.1");

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
        string dependabot = File.ReadAllText(Path.Combine(Root, ".github", "dependabot.yml"));

        dependabot.Should().Contain("package-ecosystem: \"nuget\"")
            .And.Contain("dotnet-10-servicing:")
            .And.Contain("dotnet-ai:")
            .And.Contain("aspire:")
            .And.Contain("identitymodel:")
            .And.Contain("package-ecosystem: \"npm\"")
            .And.Contain("typescript-eslint:")
            .And.Contain("vitest:")
            .And.Contain("playwright:")
            .And.Contain("vue-vite:")
            .And.Contain("package-ecosystem: \"docker\"")
            .And.Contain("dotnet-containers:")
            .And.Contain("directory: \"/docker\"")
            .And.Contain("directory: \"/docker/sce/python\"")
            .And.Contain("directory: \"/docker/sce/r\"")
            .And.Contain("package-ecosystem: \"docker-compose\"");

        DotnetMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for mcr.microsoft.com/dotnet/*");
        NugetAspNetMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for Microsoft.AspNetCore.*");
        NugetEfCoreMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for Microsoft.EntityFrameworkCore*");
        NugetDotnetEfMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for dotnet-ef");
        NugetOpenTelemetryNet11IgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore OpenTelemetry >= 1.21.0 while targeting net10.0 (#5944, ADR-2527)");
        ScePythonIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore semver-major and semver-minor bumps on python in /docker/sce/python (ADR-2327)");
        SceRIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore semver-major, semver-minor, and semver-patch bumps on r-base in /docker/sce/r");

        ValidateDependabotUniqueness(dependabot).Should().BeEmpty(
            "dependabot.yml must not contain duplicate ecosystem/directory entries or duplicate (dependency-name, versions/update-types) ignore rules");
    }

    [Fact]
    public void DependabotUniquenessValidator_RejectsDuplicateVersionsUpdateTypesAndUnconditionalIgnores()
    {
        const string duplicateVersionsYaml = """
            version: 2
            updates:
              - package-ecosystem: "nuget"
                directory: "/"
                schedule:
                  interval: "weekly"
                ignore:
                  - dependency-name: "SonarAnalyzer.CSharp"
                    versions: [">= 10.7.0"]
                  - dependency-name: "OpenTelemetry*"
                    versions: [">= 1.21.0"]
                  - dependency-name: "SonarAnalyzer.CSharp"
                    versions: [">= 10.7.0"]
            """;
        ValidateDependabotUniqueness(duplicateVersionsYaml)
            .Should().ContainSingle()
            .Which.Should().Contain("#/updates/0/ignore/2/versions");

        const string duplicateUpdateTypesYaml = """
            version: 2
            updates:
              - package-ecosystem: "npm"
                directory: "/"
                schedule:
                  interval: "weekly"
                ignore:
                  - dependency-name: "@types/node"
                    update-types: ["version-update:semver-major"]
                  - dependency-name: "@types/node"
                    update-types:
                      [
                        "version-update:semver-major",
                      ]
            """;
        ValidateDependabotUniqueness(duplicateUpdateTypesYaml)
            .Should().ContainSingle()
            .Which.Should().Contain("#/updates/0/ignore/1/update-types");

        const string duplicateUnconditionalYaml = """
            version: 2
            updates:
              - package-ecosystem: "nuget"
                directory: "/"
                schedule:
                  interval: "weekly"
                ignore:
                  - dependency-name: "Fluid.Core"
                  - dependency-name: "Fluid.Core"
            """;
        ValidateDependabotUniqueness(duplicateUnconditionalYaml)
            .Should().ContainSingle()
            .Which.Should().Contain("#/updates/0/ignore/1/dependency-name");
    }

    [Fact]
    public void DependencyValidatorService_FailsClosedOnMalformedJsonAndPassesOnCleanWorkspace()
    {
        DependencyValidatorService.ValidateDependencies(Root).Should().Be(0,
            "HEAD manifests must pass JSON validation and client crypto checks");

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

        foreach (string testPackage in new[]
        {
            "axe-core",
            "@axe-core/playwright",
            "vitest",
            "@vue/test-utils",
            "jsdom",
            "fake-indexeddb",
            "msw",
        })
        {
            AssertMalformedManifestFails(
                "packages/ui/package.json",
                $$"""{ "dependencies": { "{{testPackage}}": "catalog:" } }""");
        }

        string cleanDevDepsDir = CreateTempDir();
        try
        {
            string pkgPath = Path.Combine(cleanDevDepsDir, "packages/ui/package.json");
            Directory.CreateDirectory(Path.GetDirectoryName(pkgPath)!);
            File.WriteAllText(
                pkgPath,
                """
                {
                  "devDependencies": {
                    "axe-core": "catalog:",
                    "@axe-core/playwright": "catalog:",
                    "vitest": "catalog:",
                    "@vue/test-utils": "catalog:",
                    "jsdom": "catalog:",
                    "fake-indexeddb": "catalog:",
                    "msw": "catalog:"
                  }
                }
                """);
            DependencyValidatorService.ValidateDependencies(cleanDevDepsDir).Should().Be(0,
                "test frameworks and assertion libraries are valid inside devDependencies");
        }
        finally
        {
            Directory.Delete(cleanDevDepsDir, recursive: true);
        }
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

    private static List<string> ValidateDependabotUniqueness(string dependabotYaml)
    {
        var updates = ParseDependabotUpdates(dependabotYaml);
        var errors = new List<string>();
        var seenEcosystemTargets = new HashSet<string>(StringComparer.Ordinal);

        for (int u = 0; u < updates.Count; u++)
        {
            var update = updates[u];
            string targetKey = $"{update.Ecosystem}|{update.Directory}|{update.TargetBranch}";
            if (!seenEcosystemTargets.Add(targetKey))
            {
                errors.Add($"The property '#/updates/{u}' duplicates ecosystem/directory target '{targetKey}'.");
            }

            ValidateDependabotIgnoreRules(u, update.Ignores, errors);
        }

        return errors;
    }

    private static void ValidateDependabotIgnoreRules(
        int updateIndex,
        List<DependabotIgnoreEntry> ignores,
        List<string> errors)
    {
        var seenVersions = new HashSet<string>(StringComparer.Ordinal);
        var seenUpdateTypes = new HashSet<string>(StringComparer.Ordinal);
        var seenUnconditional = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < ignores.Count; i++)
        {
            var rule = ignores[i];
            if (string.IsNullOrWhiteSpace(rule.DependencyName))
            {
                errors.Add($"The property '#/updates/{updateIndex}/ignore/{i}/dependency-name' must be non-empty.");
                continue;
            }

            if (rule.Versions is { Count: > 0 })
            {
                CheckIgnoreConstraintList(
                    updateIndex,
                    i,
                    "versions",
                    rule.DependencyName,
                    rule.Versions,
                    seenVersions,
                    errors);
            }

            if (rule.UpdateTypes is { Count: > 0 })
            {
                CheckIgnoreConstraintList(
                    updateIndex,
                    i,
                    "update-types",
                    rule.DependencyName,
                    rule.UpdateTypes,
                    seenUpdateTypes,
                    errors);
            }

            if (rule.Versions is null && rule.UpdateTypes is null && !seenUnconditional.Add(rule.DependencyName))
            {
                errors.Add(
                    $"The property '#/updates/{updateIndex}/ignore/{i}/dependency-name' includes a duplicate ('{rule.DependencyName}').");
            }
        }
    }

    private static void CheckIgnoreConstraintList(
        int updateIndex,
        int ignoreIndex,
        string propertyName,
        string dependencyName,
        List<string> items,
        HashSet<string> seenCombinations,
        List<string> errors)
    {
        foreach (string item in items)
        {
            string comboKey = $"{dependencyName}|{item}";
            if (!seenCombinations.Add(comboKey))
            {
                errors.Add(
                    $"The property '#/updates/{updateIndex}/ignore/{ignoreIndex}/{propertyName}' includes a duplicate. " +
                    $"Ignores must have a unique combination of 'dependency-name' and '{propertyName}' ('{dependencyName}': '{item}').");
                break;
            }
        }
    }

    private static List<DependabotUpdateEntry> ParseDependabotUpdates(string yaml)
    {
        var updates = new List<DependabotUpdateEntry>();
        string[] lines = yaml.ReplaceLineEndings("\n").Split('\n');
        bool inUpdates = false;
        bool inIgnore = false;
        DependabotUpdateEntry? currentUpdate = null;
        DependabotIgnoreEntry? currentIgnore = null;

        for (int idx = 0; idx < lines.Length; idx++)
        {
            string raw = lines[idx];
            string trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            int indent = raw.Length - raw.TrimStart(' ').Length;
            if (indent == 0)
            {
                inUpdates = string.Equals(trimmed, "updates:", StringComparison.Ordinal);
                inIgnore = false;
                currentUpdate = null;
                currentIgnore = null;
                continue;
            }

            if (!inUpdates)
            {
                continue;
            }

            if (indent == 2 && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                currentUpdate = new DependabotUpdateEntry();
                updates.Add(currentUpdate);
                inIgnore = false;
                currentIgnore = null;
                ApplyUpdateProperty(currentUpdate, trimmed[2..].Trim(), ref inIgnore);
                continue;
            }

            if (currentUpdate is null)
            {
                continue;
            }

            if (indent == 4)
            {
                currentIgnore = null;
                ApplyUpdateProperty(currentUpdate, trimmed, ref inIgnore);
                continue;
            }

            if (!inIgnore)
            {
                continue;
            }

            if (indent == 6 && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                currentIgnore = new DependabotIgnoreEntry();
                currentUpdate.Ignores.Add(currentIgnore);
                ApplyIgnoreProperty(currentIgnore, lines, ref idx, trimmed[2..].Trim());
                continue;
            }

            if (indent == 8 && currentIgnore is not null)
            {
                ApplyIgnoreProperty(currentIgnore, lines, ref idx, trimmed);
            }
        }

        return updates;
    }

    private static void ApplyUpdateProperty(DependabotUpdateEntry update, string propLine, ref bool inIgnore)
    {
        if (string.Equals(propLine, "ignore:", StringComparison.Ordinal))
        {
            inIgnore = true;
            return;
        }

        inIgnore = false;
        if (!TrySplitYamlScalarProperty(propLine, out string key, out string value))
        {
            return;
        }

        switch (key)
        {
            case "package-ecosystem":
                update.Ecosystem = value;
                break;
            case "directory":
                update.Directory = value;
                break;
            case "target-branch":
                update.TargetBranch = value;
                break;
        }
    }

    private static void ApplyIgnoreProperty(
        DependabotIgnoreEntry ignore,
        string[] lines,
        ref int idx,
        string propLine)
    {
        int colon = propLine.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return;
        }

        string key = propLine[..colon].Trim();
        string rest = propLine[(colon + 1)..].Trim();
        if (string.Equals(key, "dependency-name", StringComparison.Ordinal))
        {
            ignore.DependencyName = UnquoteYamlScalar(rest);
            return;
        }

        if (key is "versions" or "update-types")
        {
            var values = ReadYamlStringArray(lines, ref idx, rest);
            if (string.Equals(key, "versions", StringComparison.Ordinal))
            {
                ignore.Versions = values;
            }
            else
            {
                ignore.UpdateTypes = values;
            }
        }
    }

    private static List<string> ReadYamlStringArray(string[] lines, ref int idx, string inlineRest)
    {
        string buffer = inlineRest;
        if (buffer.Length == 0 && idx + 1 < lines.Length && lines[idx + 1].TrimStart().StartsWith('['))
        {
            idx++;
            buffer = lines[idx].Trim();
        }

        if (buffer.StartsWith('['))
        {
            while (!buffer.Contains(']', StringComparison.Ordinal) && idx + 1 < lines.Length)
            {
                idx++;
                string next = lines[idx].Trim();
                if (!next.StartsWith('#'))
                {
                    buffer += " " + next;
                }
            }

            int open = buffer.IndexOf('[', StringComparison.Ordinal);
            int close = buffer.LastIndexOf(']');
            string inner = close > open ? buffer[(open + 1)..close] : "";
            return inner
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(UnquoteYamlScalar)
                .Where(s => s.Length > 0)
                .ToList();
        }

        var items = new List<string>();
        while (idx + 1 < lines.Length)
        {
            string peek = lines[idx + 1];
            string peekTrimmed = peek.Trim();
            if (peekTrimmed.Length == 0 || peekTrimmed.StartsWith('#'))
            {
                idx++;
                continue;
            }

            int peekIndent = peek.Length - peek.TrimStart(' ').Length;
            if (peekIndent < 8 || !peekTrimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                break;
            }

            idx++;
            items.Add(UnquoteYamlScalar(peekTrimmed[2..].Trim()));
        }

        return items;
    }

    private static bool TrySplitYamlScalarProperty(string line, out string key, out string value)
    {
        int colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            key = "";
            value = "";
            return false;
        }

        key = line[..colon].Trim();
        value = UnquoteYamlScalar(line[(colon + 1)..].Trim());
        return true;
    }

    private static string UnquoteYamlScalar(string raw)
    {
        if (raw.Length >= 2 &&
            ((raw[0] == '"' && raw[^1] == '"') || (raw[0] == '\'' && raw[^1] == '\'')))
        {
            return raw[1..^1];
        }

        int commentIdx = raw.IndexOf(" #", StringComparison.Ordinal);
        return (commentIdx >= 0 ? raw[..commentIdx] : raw).Trim();
    }

    private sealed class DependabotUpdateEntry
    {
        public string Ecosystem { get; set; } = "";
        public string Directory { get; set; } = "";
        public string TargetBranch { get; set; } = "";
        public List<DependabotIgnoreEntry> Ignores { get; } = [];
    }

    private sealed class DependabotIgnoreEntry
    {
        public string DependencyName { get; set; } = "";
        public List<string>? Versions { get; set; }
        public List<string>? UpdateTypes { get; set; }
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

    [GeneratedRegex(@"dependency-name:\s*""OpenTelemetry\*""\s*\r?\n\s*versions:\s*\[""\>= 1\.21\.0""\]")]
    private static partial Regex NugetOpenTelemetryNet11IgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""python""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major"",\s*""version-update:semver-minor""\]")]
    private static partial Regex ScePythonIgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""r-base""\s*\r?\n\s*update-types:\s*\[\s*""version-update:semver-major"",\s*""version-update:semver-minor"",\s*""version-update:semver-patch"",?\s*\]")]
    private static partial Regex SceRIgnoreRule();
}
