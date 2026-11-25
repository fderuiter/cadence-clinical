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
/// <c>Directory.Build.props</c>, ADR-2233, ADR-2327, ADR-2527, and ADR-2528.
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
    public void ScePythonRequirementsAndDockerfile_EnforceHashPinnedLockfileAndPipRemoval()
    {
        string dockerfile = File.ReadAllText(Path.Combine(Root, "docker", "sce", "python", "Dockerfile"));
        dockerfile.Should().Contain(
            "FROM python:3.14-slim@sha256:9aaa474097d094040649f2162a627c0049b408f52138d980916788d80f04802f",
            "ADR-2327 locks the SCE Python 3.14-slim base image digest");

        int installIndex = dockerfile.IndexOf(
            "pip install --no-cache-dir --require-hashes --no-deps -r /tmp/requirements.txt",
            StringComparison.Ordinal);
        int uninstallIndex = dockerfile.IndexOf("pip uninstall -y pip", StringComparison.Ordinal);
        installIndex.Should().BeGreaterThanOrEqualTo(0,
            "docker/sce/python/Dockerfile must install requirements.txt with --require-hashes and --no-deps");
        uninstallIndex.Should().BeGreaterThan(installIndex,
            "docker/sce/python/Dockerfile must uninstall pip after installing hash-verified packages");

        bool parseSucceeded = VulnerabilityAuditorService.TryParseHashedPythonRequirements(
            File.ReadAllText(Path.Combine(Root, "docker", "sce", "python", "requirements.txt")),
            out var packages,
            out string errorMessage);
        parseSucceeded.Should().BeTrue(errorMessage);
        packages.Should().HaveCount(30,
            "docker/sce/python/requirements.txt must pin all 30 resolved direct and transitive SCE Python wheels with --hash=sha256:");
        packages.Should().Contain(p => p.Name == "numpy" && p.Version == "2.5.5");
        packages.Should().Contain(p => p.Name == "scipy" && p.Version == "1.18.1");
        packages.Should().Contain(p => p.Name == "pandas" && p.Version == "3.1.1");
        packages.Should().Contain(p => p.Name == "statsmodels" && p.Version == "0.15.0");
        packages.Should().Contain(p => p.Name == "matplotlib" && p.Version == "3.11.2");
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
        ReadMsBuildProperty(propsDoc, "OpenTelemetryVersion").Should().Be("1.20.1");
        ReadMsBuildProperty(propsDoc, "OpenTelemetryInstrumentationVersion").Should().Be("1.20.2");
        ReadMsBuildProperty(propsDoc, "MassTransitVersion").Should().Be("8.5.12");
        ReadMsBuildProperty(propsDoc, "FluentValidationVersion").Should().Be("12.2.0");

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

        packageVersions.Should().Contain(("TngTech.ArchUnitNET.xUnitV3", "0.13.5"),
            "Cadence.ArchitectureTests must bind ArchUnitNET through TngTech.ArchUnitNET.xUnitV3");
        packageVersions.Should().Contain(("xunit.v3", "4.0.3"));
        packageVersions.Select(p => p.Id).Should().NotContain(
            ["TngTech.ArchUnitNET.xUnit", "xunit", "xunit.assert", "xunit.core"],
            "legacy xunit v2 packages must never coexist with xunit.v3");

        string archAssetsPath = Path.Combine(Root, "tests", "Cadence.ArchitectureTests", "obj", "project.assets.json");
        File.Exists(archAssetsPath).Should().BeTrue("Cadence.ArchitectureTests must be restored before running tests");
        string archAssets = File.ReadAllText(archAssetsPath);
        archAssets.Should().Contain("\"TngTech.ArchUnitNET.xUnitV3/0.13.5\"")
            .And.Contain("\"TngTech.ArchUnitNET/0.13.5\"")
            .And.Contain("\"xunit.v3.assert/4.0.3\"")
            .And.NotContain("\"TngTech.ArchUnitNET.xUnit/")
            .And.NotContain("\"xunit.assert/");

        foreach (string assetsFile in Directory.EnumerateFiles(Path.Combine(Root, "tests"), "project.assets.json", SearchOption.AllDirectories))
        {
            string assetsContent = File.ReadAllText(assetsFile);
            assetsContent.Should().NotContain("\"xunit.assert/",
                $"{Path.GetRelativePath(Root, assetsFile)} must not resolve xunit.assert 2.x");
            assetsContent.Should().NotContain("\"TngTech.ArchUnitNET.xUnit/",
                $"{Path.GetRelativePath(Root, assetsFile)} must not resolve legacy TngTech.ArchUnitNET.xUnit");
        }
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

        var (legacyXunitAssertExit, legacyXunitAssertOut) = RunSkewProbeBuild(
            """<ResolvedCompileFileDefinitions Include="xunit.assert.dll" NuGetPackageId="xunit.assert" NuGetPackageVersion="2.4.1" />""");
        legacyXunitAssertExit.Should().NotBe(0, "resolving xunit.assert 2.x on net10.0 must fail compilation");
        legacyXunitAssertOut.Should().Contain("CADDEP004");

        var (legacyArchUnitExit, legacyArchUnitOut) = RunSkewProbeBuild(
            """<ResolvedCompileFileDefinitions Include="ArchUnitNET.xUnit.dll" NuGetPackageId="TngTech.ArchUnitNET.xUnit" NuGetPackageVersion="0.13.5" />""");
        legacyArchUnitExit.Should().NotBe(0, "resolving legacy TngTech.ArchUnitNET.xUnit must fail compilation");
        legacyArchUnitOut.Should().Contain("CADDEP004");

        var (legacyXunitCoreExit, legacyXunitCoreOut) = RunSkewProbeBuild(
            """<_PackageDependenciesDesignTime Include="xunit.core/2.9.3" Name="xunit.core" />""");
        legacyXunitCoreExit.Should().NotBe(0, "resolving legacy xunit.core 2.x must fail compilation");
        legacyXunitCoreOut.Should().Contain("CADDEP004");

        var (splitXunitV3Exit, splitXunitV3Out) = RunSkewProbeBuild(
            """
            <ResolvedCompileFileDefinitions Include="xunit.v3.core.dll" NuGetPackageId="xunit.v3.core" NuGetPackageVersion="4.0.3" />
            <ResolvedCompileFileDefinitions Include="xunit.v3.assert.dll" NuGetPackageId="xunit.v3.assert" NuGetPackageVersion="4.0.0" />
            """);
        splitXunitV3Exit.Should().NotBe(0, "splitting xunit.v3 package versions must fail compilation");
        splitXunitV3Out.Should().Contain("CADDEP004");

        var (splitArchUnitExit, splitArchUnitOut) = RunSkewProbeBuild(
            """
            <ResolvedCompileFileDefinitions Include="ArchUnitNET.dll" NuGetPackageId="TngTech.ArchUnitNET" NuGetPackageVersion="0.13.5" />
            <ResolvedCompileFileDefinitions Include="ArchUnitNET.xUnitV3.dll" NuGetPackageId="TngTech.ArchUnitNET.xUnitV3" NuGetPackageVersion="0.13.4" />
            """);
        splitArchUnitExit.Should().NotBe(0, "splitting TngTech.ArchUnitNET package versions must fail compilation");
        splitArchUnitOut.Should().Contain("CADDEP004");

        var (alignedXunitV3Exit, alignedXunitV3Out) = RunSkewProbeBuild(
            """
            <ResolvedCompileFileDefinitions Include="xunit.v3.core.dll" NuGetPackageId="xunit.v3.core" NuGetPackageVersion="4.0.3" />
            <ResolvedCompileFileDefinitions Include="xunit.v3.assert.dll" NuGetPackageId="xunit.v3.assert" NuGetPackageVersion="4.0.3" />
            <_PackageDependenciesDesignTime Include="xunit.analyzers/2.3.0" Name="xunit.analyzers" />
            <_PackageDependenciesDesignTime Include="xunit.runner.visualstudio/4.0.1" Name="xunit.runner.visualstudio" />
            <ResolvedCompileFileDefinitions Include="ArchUnitNET.dll" NuGetPackageId="TngTech.ArchUnitNET" NuGetPackageVersion="0.13.5" />
            <ResolvedCompileFileDefinitions Include="ArchUnitNET.xUnitV3.dll" NuGetPackageId="TngTech.ArchUnitNET.xUnitV3" NuGetPackageVersion="0.13.5" />
            """);
        alignedXunitV3Exit.Should().Be(0, $"aligned xunit.v3 and TngTech.ArchUnitNET.xUnitV3 packages must pass: {alignedXunitV3Out}");
    }

    [Fact]
    public void PnpmWorkspaceCatalog_GovernsAllSharedFrontendPackagesWithoutDuplicateLiterals()
    {
        var catalog = ReadPnpmCatalog(Path.Combine(Root, "pnpm-workspace.yaml"));
        catalog.Should().ContainKeys(
            "@axe-core/playwright",
            "@playwright/test",
            "@types/node",
            "@typescript-eslint/eslint-plugin",
            "@typescript-eslint/parser",
            "@vitejs/plugin-vue",
            "@vitest/coverage-v8",
            "axe-core",
            "eslint-plugin-vue",
            "globals",
            "pinia",
            "typescript",
            "typescript-eslint",
            "vite",
            "vitest",
            "vue",
            "vue-router",
            "vue-tsc");

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
            .And.Contain("- \"dotnet-ef\"")
            .And.Contain("dotnet-ai:")
            .And.Contain("aspire:")
            .And.Contain("identitymodel:")
            .And.Contain("opentelemetry:")
            .And.Contain("masstransit:")
            .And.Contain("fluentvalidation:")
            .And.Contain("dotnet-testing:")
            .And.Contain("analyzers:")
            .And.Contain("anthropic:")
            .And.Contain("nuget-other:")
            .And.Contain("package-ecosystem: \"npm\"")
            .And.Contain("typescript-eslint:")
            .And.Contain("vitest:")
            .And.Contain("playwright:")
            .And.Contain("vue-vite:")
            .And.Contain("npm-other:")
            .And.Contain("package-ecosystem: \"docker\"")
            .And.Contain("package-ecosystem: \"pip\"")
            .And.Contain("sce-python-pip:")
            .And.Contain("dotnet-containers:")
            .And.Contain("directory: \"/docker\"")
            .And.Contain("directory: \"/docker/sce/python\"")
            .And.Contain("directory: \"/docker/sce/r\"")
            .And.Contain("package-ecosystem: \"docker-compose\"")
            .And.Contain("compose-images:")
            .And.Contain("package-ecosystem: \"github-actions\"")
            .And.Contain("github-actions:");

        DotnetServicingDotnetEfGroupRule().IsMatch(dependabot).Should().BeTrue(
            "dotnet-10-servicing must group dotnet-ef with $(DotNetServicingVersion)");
        OpenPullRequestsLimitLine().Matches(dependabot)
            .Select(m => int.Parse(m.Groups["limit"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Equal([5, 5, 1, 1, 1, 1, 1, 2],
                "dependabot.yml open-pull-requests-limit values must sum to 17 across nuget, npm, container, pip, and github-actions blocks");

        DotnetMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for mcr.microsoft.com/dotnet/*");
        NugetAspNetMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for Microsoft.AspNetCore.*");
        NugetEfCoreMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for Microsoft.EntityFrameworkCore*");
        NugetDotnetEfMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for dotnet-ef");
        NugetXunitMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for xunit*");
        NugetTestSdkMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for Microsoft.NET.Test.Sdk");
        NugetArchUnitMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for TngTech.ArchUnitNET*");
        NugetCodeAnalysisMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for Microsoft.CodeAnalysis.*");
        NugetSonarMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for SonarAnalyzer.CSharp");
        NugetAnthropicMajorIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore version-update:semver-major for Anthropic*");
        NugetOpenTelemetryNet11IgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore OpenTelemetry >= 1.21.0 while targeting net10.0 (#5944, ADR-2527)");
        ScePythonIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore semver-major and semver-minor bumps on python in /docker/sce/python (ADR-2327)");
        SceRIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore semver-major, semver-minor, and semver-patch bumps on r-base in /docker/sce/r");
        NugetSonarAnalyzerIgnoreRule().IsMatch(dependabot).Should().BeTrue(
            "dependabot.yml must ignore SonarAnalyzer.CSharp >= 10.7.0 until solution-wide analyzer triage (#6030)");
    }

    [Fact]
    public void Dependabot_IgnoresHaveUniqueDependencyVersionAndUpdateTypeCombinations()
    {
        string dependabot = File.ReadAllText(Path.Combine(Root, ".github", "dependabot.yml"));
        ValidateDependabotIgnoreUniqueness(dependabot).Should().BeEmpty(
            "Dependabot rejects duplicate (dependency-name, versions) or (dependency-name, update-types) ignore entries within an updates[] block");

        const string duplicateVersionsFixture = """
            version: 2
            updates:
              - package-ecosystem: "nuget"
                directory: "/"
                ignore:
                  - dependency-name: "SonarAnalyzer.CSharp"
                    versions: [">= 10.7.0"]
                  - dependency-name: "OpenTelemetry*"
                    versions: [">= 1.21.0"]
                  - dependency-name: "SonarAnalyzer.CSharp"
                    versions: [">= 10.7.0"]
            """;

        ValidateDependabotIgnoreUniqueness(duplicateVersionsFixture).Should().ContainSingle()
            .Which.Should().Be(
                "The property '#/updates/0/ignore/2/versions' includes a duplicate. Ignores must have a unique combination of 'dependency-name' and 'versions'");

        const string duplicateUpdateTypesFixture = """
            version: 2
            updates:
              - package-ecosystem: "docker"
                directory: "/docker/sce/r"
                ignore:
                  - dependency-name: "r-base"
                    update-types:
                      [
                        "version-update:semver-major",
                        "version-update:semver-minor",
                      ]
                  - dependency-name: "r-base"
                    update-types: ["version-update:semver-minor"]
            """;

        ValidateDependabotIgnoreUniqueness(duplicateUpdateTypesFixture).Should().ContainSingle()
            .Which.Should().Be(
                "The property '#/updates/0/ignore/1/update-types' includes a duplicate. Ignores must have a unique combination of 'dependency-name' and 'update-types'");

        const string intraEntryDuplicateVersionsFixture = """
            version: 2
            updates:
              - package-ecosystem: "nuget"
                directory: "/"
                ignore:
                  - dependency-name: "SonarAnalyzer.CSharp"
                    versions: [">= 10.7.0", ">= 10.7.0"]
            """;

        ValidateDependabotIgnoreUniqueness(intraEntryDuplicateVersionsFixture).Should().ContainSingle()
            .Which.Should().Be(
                "The property '#/updates/0/ignore/0/versions' includes a duplicate. Ignores must have a unique combination of 'dependency-name' and 'versions'");

        const string duplicateBareDependencyFixture = """
            version: 2
            updates:
              - package-ecosystem: "docker-compose"
                directory: "/docker"
                ignore:
                  - dependency-name: "postgres"
                  - dependency-name: "postgres"
            """;

        ValidateDependabotIgnoreUniqueness(duplicateBareDependencyFixture).Should().ContainSingle()
            .Which.Should().Be(
                "The property '#/updates/0/ignore/1/dependency-name' includes a duplicate.");

        const string duplicateTargetFixture = """
            version: 2
            updates:
              - package-ecosystem: "nuget"
                directory: "/"
              - package-ecosystem: "nuget"
                directory: "/"
            """;

        ValidateDependabotIgnoreUniqueness(duplicateTargetFixture).Should().ContainSingle()
            .Which.Should().Be(
                "'#/updates/1' duplicates package-ecosystem 'nuget' and directory '/'.");

        const string distinctTargetBranchesFixture = """
            version: 2
            updates:
              - package-ecosystem: "nuget"
                directory: "/"
                target-branch: "main"
              - package-ecosystem: "nuget"
                directory: "/"
                target-branch: "release/1.0"
            """;

        ValidateDependabotIgnoreUniqueness(distinctTargetBranchesFixture).Should().BeEmpty(
            "updates targeting distinct target-branch values on the same directory are valid in Dependabot v2");
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
        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            """
            version: 2
            updates:
              - package-ecosystem: "nuget"
                directory: "/"
                ignore:
                  - dependency-name: "SonarAnalyzer.CSharp"
                    versions: [">= 10.7.0"]
                  - dependency-name: "SonarAnalyzer.CSharp"
                    versions: [">= 10.7.0"]
            """);
        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            """
            version: 2
            updates:
              - package-ecosystem: "nuget"
                directory: "/"
                ignore:
                  - dependency-name: "MassTransit*"
                    update-types: ["version-update:semver-major"]
                  - dependency-name: "MassTransit*"
                    update-types:
                      [
                        "version-update:semver-major",
                      ]
            """);
        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            """
            version: 2
            updates:
              - package-ecosystem: "nuget"
                directory: "/"
                ignore:
                  - dependency-name: "SonarAnalyzer.CSharp"
                    versions: [">= 10.7.0", ">= 10.7.0"]
            """);
        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            """
            version: 2
            updates:
              - package-ecosystem: "nuget"
                directory: "/"
              - package-ecosystem: "nuget"
                directory: "/"
            """);
        AssertMalformedManifestFails(
            ".github/dependabot.yaml",
            """
            version: 2
            updates:
              - package-ecosystem: "nuget"
                directory: "/"
                ignore:
                  - dependency-name: "Fluid.Core"
                  - dependency-name: "Fluid.Core"
            """);

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

    [Fact]
    public void DependencyValidatorService_EnforcesMsBuildCohortsDependabotRulesAndPreCommitIndexBlobs()
    {
        AssertMalformedManifestFails("Directory.Build.props", "<Project><PropertyGroup>");
        AssertMalformedManifestFails("Directory.Packages.props", "<Project><ItemGroup>");

        string buildPropsContent = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));
        string packagesPropsContent = File.ReadAllText(Path.Combine(Root, "Directory.Packages.props"));
        string dependabotContent = File.ReadAllText(Path.Combine(Root, ".github", "dependabot.yml"));

        AssertMsBuildCohortMutationFails(
            buildPropsContent,
            packagesPropsContent.Replace(
                """<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="$(DotNetServicingVersion)" />""",
                """<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.15" />""",
                StringComparison.Ordinal));

        AssertMsBuildCohortMutationFails(
            buildPropsContent,
            packagesPropsContent.Replace(
                "TngTech.ArchUnitNET.xUnitV3",
                "TngTech.ArchUnitNET.xUnit",
                StringComparison.Ordinal));

        AssertMsBuildCohortMutationFails(
            buildPropsContent,
            packagesPropsContent.Replace(
                """<PackageVersion Include="xunit.v3" Version="4.0.3" />""",
                """
                <PackageVersion Include="xunit.v3" Version="4.0.3" />
                <PackageVersion Include="xunit.v3.assert" Version="4.0.0" />
                """,
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace("dotnet-testing:", "legacy-testing-group:", StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "- dependency-name: \"Anthropic*\"\n        update-types: [\"version-update:semver-major\"]",
                string.Empty,
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "- dependency-name: \"postgres\"",
                "- dependency-name: \"postgres\"\n        update-types: [\"version-update:semver-major\"]",
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "- dependency-name: \"postgres\"",
                "- dependency-name: \"postgres\"\n      - dependency-name: \"postgres\"",
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent + """

              - package-ecosystem: "github-actions"
                directory: "/"
                schedule:
                  interval: "weekly"
            """);

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace("          - \"dotnet-ef\"\n", string.Empty, StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "      nuget-other:\n        patterns:\n          - \"*\"\n",
                string.Empty,
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "      npm-other:\n        patterns:\n          - \"*\"\n",
                string.Empty,
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "    groups:\n      sce-python-pip:\n        patterns:\n          - \"*\"\n",
                string.Empty,
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "    groups:\n      compose-images:\n        patterns:\n          - \"*\"\n",
                string.Empty,
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "    groups:\n      github-actions:\n        patterns:\n          - \"*\"",
                string.Empty,
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent
                .Replace("      nuget-other:\n        patterns:\n          - \"*\"\n", string.Empty, StringComparison.Ordinal)
                .Replace(
                    "      dotnet-10-servicing:",
                    "      nuget-other:\n        patterns:\n          - \"*\"\n      dotnet-10-servicing:",
                    StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "      typescript-eslint:",
                "      early-catchall:\n        patterns:\n          - \"*\"\n      typescript-eslint:",
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "      nuget-other:\n        patterns:\n          - \"*\"",
                "      nuget-other:\n        patterns:\n          - \"*\"\n        exclude-patterns:\n          - \"Serilog*\"",
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace("    open-pull-requests-limit: 2\n", string.Empty, StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace("    open-pull-requests-limit: 2", "    open-pull-requests-limit: 0", StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace("    open-pull-requests-limit: 2", "    open-pull-requests-limit: 7", StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "package-ecosystem: \"pip\"",
                "package-ecosystem: \"cargo\"",
                StringComparison.Ordinal));

        AssertMalformedManifestFails(
            ".github/dependabot.yml",
            dependabotContent.Replace(
                "  - package-ecosystem: \"pip\"\n    directory: \"/docker/sce/python\"\n    schedule:\n      interval: \"weekly\"",
                "  - package-ecosystem: \"pip\"\n    directory: \"/docker/sce/python\"\n    schedule:\n      interval: \"daily\"",
                StringComparison.Ordinal));
        VerifyPreCommitIndexValidation(buildPropsContent, packagesPropsContent, dependabotContent);
    }

    private static void AssertMsBuildCohortMutationFails(string buildPropsContent, string mutatedPackagesPropsContent)
    {
        string tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Directory.Build.props"), buildPropsContent);
            File.WriteAllText(Path.Combine(tempDir, "Directory.Packages.props"), mutatedPackagesPropsContent);

            DependencyValidatorService.ValidateDependencies(tempDir).Should().Be(1,
                "DependencyValidatorService must fail closed when Directory.Packages.props violates MSBuild cohort or xUnit v3 rules");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void VerifyPreCommitIndexValidation(
        string buildPropsContent,
        string packagesPropsContent,
        string dependabotContent)
    {
        string tempGit = CreateTempDir();
        try
        {
            RunGit(tempGit, "init", "-q", "-b", "main");
            RunGit(tempGit, "config", "user.email", "fixture@example.invalid");
            RunGit(tempGit, "config", "user.name", "Fixture");
            RunGit(tempGit, "config", "commit.gpgsign", "false");

            Directory.CreateDirectory(Path.Combine(tempGit, ".github"));
            File.WriteAllText(Path.Combine(tempGit, "Directory.Build.props"), buildPropsContent);
            File.WriteAllText(Path.Combine(tempGit, "Directory.Packages.props"), packagesPropsContent);
            File.WriteAllText(Path.Combine(tempGit, ".github", "dependabot.yml"), dependabotContent);
            RunGit(tempGit, "add", "Directory.Build.props", "Directory.Packages.props", ".github/dependabot.yml");
            RunGit(tempGit, "commit", "-q", "-m", "baseline manifests");

            string driftedPackages = packagesPropsContent.Replace(
                "TngTech.ArchUnitNET.xUnitV3",
                "TngTech.ArchUnitNET.xUnit",
                StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(tempGit, "Directory.Packages.props"), driftedPackages);
            RunGit(tempGit, "add", "Directory.Packages.props");

            // Restore the working-tree copy to clean content so only the staged Git index blob contains the drift.
            File.WriteAllText(Path.Combine(tempGit, "Directory.Packages.props"), packagesPropsContent);

            using (var reader = GitIndexReader.TryOpen(tempGit)!)
            {
                reader.Should().NotBeNull();
                DependencyValidatorService.ValidateDependencies(
                    tempGit,
                    preCommit: true,
                    indexReader: reader).Should().Be(1,
                    "pre-commit dependency validation must inspect staged Git index blobs even when no C# files are staged and working-tree files are clean");
            }

            RunGit(tempGit, "add", "Directory.Packages.props");

            string duplicateDependabot = dependabotContent.Replace(
                "- dependency-name: \"SonarAnalyzer.CSharp\"\n        versions: [\">= 10.7.0\"]",
                "- dependency-name: \"SonarAnalyzer.CSharp\"\n        versions: [\">= 10.7.0\"]\n      - dependency-name: \"SonarAnalyzer.CSharp\"\n        versions: [\">= 10.7.0\"]",
                StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(tempGit, ".github", "dependabot.yml"), duplicateDependabot);
            RunGit(tempGit, "add", ".github/dependabot.yml");
            File.WriteAllText(Path.Combine(tempGit, ".github", "dependabot.yml"), dependabotContent);

            using (var dependabotReader = GitIndexReader.TryOpen(tempGit)!)
            {
                dependabotReader.Should().NotBeNull();
                DependencyValidatorService.ValidateDependencies(
                    tempGit,
                    preCommit: true,
                    indexReader: dependabotReader).Should().Be(1,
                    "pre-commit dependency validation must fail closed on duplicate ignore rules in staged .github/dependabot.yml blobs");
            }

            string misorderedCatchAllDependabot = dependabotContent
                .Replace("      npm-other:\n        patterns:\n          - \"*\"\n", string.Empty, StringComparison.Ordinal)
                .Replace(
                    "      typescript-eslint:",
                    "      npm-other:\n        patterns:\n          - \"*\"\n      typescript-eslint:",
                    StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(tempGit, ".github", "dependabot.yml"), misorderedCatchAllDependabot);
            RunGit(tempGit, "add", ".github/dependabot.yml");
            File.WriteAllText(Path.Combine(tempGit, ".github", "dependabot.yml"), dependabotContent);

            using (var misorderedReader = GitIndexReader.TryOpen(tempGit)!)
            {
                misorderedReader.Should().NotBeNull();
                DependencyValidatorService.ValidateDependencies(
                    tempGit,
                    preCommit: true,
                    indexReader: misorderedReader).Should().Be(1,
                    "pre-commit dependency validation must reject catch-all '*' groups placed before cohort groups in staged .github/dependabot.yml blobs");
            }

            string excessiveLimitsDependabot = dependabotContent.Replace(
                "    open-pull-requests-limit: 2",
                "    open-pull-requests-limit: 7",
                StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(tempGit, ".github", "dependabot.yml"), excessiveLimitsDependabot);
            RunGit(tempGit, "add", ".github/dependabot.yml");
            File.WriteAllText(Path.Combine(tempGit, ".github", "dependabot.yml"), dependabotContent);

            using (var limitReader = GitIndexReader.TryOpen(tempGit)!)
            {
                limitReader.Should().NotBeNull();
                DependencyValidatorService.ValidateDependencies(
                    tempGit,
                    preCommit: true,
                    indexReader: limitReader).Should().Be(1,
                    "pre-commit dependency validation must fail closed when staged .github/dependabot.yml open-pull-requests-limit sum exceeds 20");
            }

            RunGit(tempGit, "add", ".github/dependabot.yml");
            using var cleanReader = GitIndexReader.TryOpen(tempGit)!;
            cleanReader.Should().NotBeNull();
            DependencyValidatorService.ValidateDependencies(
                tempGit,
                preCommit: true,
                indexReader: cleanReader).Should().Be(0,
                "pre-commit dependency validation must pass when staged manifests satisfy all cohort rules");
        }
        finally
        {
            foreach (var file in new DirectoryInfo(tempGit).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }

            Directory.Delete(tempGit, recursive: true);
        }
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        string stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} failed: {stderr}");
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

    [GeneratedRegex(@"dependency-name:\s*""xunit\*""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major""\]")]
    private static partial Regex NugetXunitMajorIgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""Microsoft\.NET\.Test\.Sdk""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major""\]")]
    private static partial Regex NugetTestSdkMajorIgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""TngTech\.ArchUnitNET\*""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major""\]")]
    private static partial Regex NugetArchUnitMajorIgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""Microsoft\.CodeAnalysis\.\*""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major""\]")]
    private static partial Regex NugetCodeAnalysisMajorIgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""SonarAnalyzer\.CSharp""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major""\]")]
    private static partial Regex NugetSonarMajorIgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""Anthropic\*""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major""\]")]
    private static partial Regex NugetAnthropicMajorIgnoreRule();

    private static IReadOnlyList<string> ValidateDependabotIgnoreUniqueness(string yamlContent) =>
        DependencyValidatorService.ValidateDependabotContent(yamlContent);

    [GeneratedRegex(@"dependency-name:\s*""OpenTelemetry\*""\s*\r?\n\s*versions:\s*\[""\>= 1\.21\.0""\]")]
    private static partial Regex NugetOpenTelemetryNet11IgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""SonarAnalyzer\.CSharp""\s*\r?\n\s*versions:\s*\[""\>= 10\.7\.0""\]")]
    private static partial Regex NugetSonarAnalyzerIgnoreRule();

    [GeneratedRegex(@"dependency-name:\s*""python""\s*\r?\n\s*update-types:\s*\[""version-update:semver-major"",\s*""version-update:semver-minor""\]")]
    private static partial Regex ScePythonIgnoreRule();

    [GeneratedRegex(@"dotnet-10-servicing:\s*\r?\n\s*patterns:(?:\s*\r?\n\s*-\s*""[^""]+"")*\s*\r?\n\s*-\s*""dotnet-ef""")]
    private static partial Regex DotnetServicingDotnetEfGroupRule();

    [GeneratedRegex(@"^\s*open-pull-requests-limit:\s*(?<limit>\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex OpenPullRequestsLimitLine();

    [GeneratedRegex(@"dependency-name:\s*""r-base""\s*\r?\n\s*update-types:\s*\[\s*""version-update:semver-major"",\s*""version-update:semver-minor"",\s*""version-update:semver-patch"",?\s*\]")]
    private static partial Regex SceRIgnoreRule();
}
