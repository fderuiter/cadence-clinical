using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Validates JSON, XML, and YAML dependency manifests, lockstep MSBuild and <c>pnpm</c> catalog cohorts,
/// <c>.github/dependabot.yml</c> target/ignore uniqueness and group/ignore guardrails, synchronized
/// <c>PostgresTestImage</c> <c>@sha256</c> digests, and forbidden client-side crypto or runtime test-harness packages
/// across the workspace or staged Git index.
/// </summary>
public static partial class DependencyValidatorService
{
    private const string DependabotRelativePath = ".github/dependabot.yml";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly string[] WorkspaceContainers = ["apps", "packages"];
    private static readonly string[] ForbiddenPackages = ["node-forge", "elliptic", "jose"];
    private static readonly string[] ForbiddenRuntimeTestPackages =
    [
        "axe-core",
        "@axe-core/playwright",
        "vitest",
        "@vue/test-utils",
        "jsdom",
        "fake-indexeddb",
        "msw",
    ];
    private static readonly string[] DependencySections =
    [
        "dependencies",
        "devDependencies",
        "peerDependencies",
        "optionalDependencies",
    ];
    private static readonly string[] RequiredSolutionFiles =
    [
        "Directory.Build.props",
        "Directory.Packages.props",
        ".config/dotnet-tools.json",
        "pnpm-workspace.yaml",
        DependabotRelativePath,
        "packages/testing/src/Persistence/PostgresTestImage.cs",
        "docker/docker-compose.yml",
        ".github/workflows/ci.yml",
        ".github/workflows/production-pipeline.yml",
        "apps/apphost/src/Program.cs",
    ];
    private static readonly string[] RequiredMsBuildCohortProperties =
    [
        "DotNetServicingVersion",
        "AspireHostingVersion",
        "IdentityModelVersion",
        "DotNetAiVersion",
        "OpenTelemetryVersion",
        "OpenTelemetryInstrumentationVersion",
        "MassTransitVersion",
        "FluentValidationVersion",
    ];
    private static readonly (string GroupName, string[] Patterns, string[] ExcludePatterns)[] RequiredNugetGroups =
    [
        (
            "dotnet-10-servicing",
            [
                "Microsoft.AspNetCore.*",
                "Microsoft.EntityFrameworkCore*",
                "Microsoft.Extensions.*",
                "System.Security.Cryptography.*",
                "System.IO.*",
            ],
            ["Microsoft.Extensions.AI*"]
        ),
        ("dotnet-ai", ["Microsoft.Extensions.AI*"], []),
        ("aspire", ["Aspire.*"], []),
        ("identitymodel", ["Microsoft.IdentityModel.*", "System.IdentityModel.*"], []),
        ("opentelemetry", ["OpenTelemetry*"], []),
        ("masstransit", ["MassTransit*"], []),
        ("fluentvalidation", ["FluentValidation*"], []),
        ("dotnet-testing", ["xunit*", "Microsoft.NET.Test.Sdk", "TngTech.ArchUnitNET*"], []),
        ("analyzers", ["Microsoft.CodeAnalysis.*", "SonarAnalyzer.CSharp"], []),
        ("anthropic", ["Anthropic*"], []),
    ];
    private static readonly string[] RequiredNugetMajorIgnores =
    [
        "MassTransit*",
        "Microsoft.AspNetCore.*",
        "Microsoft.EntityFrameworkCore*",
        "Microsoft.Extensions.*",
        "System.Security.Cryptography.*",
        "System.IO.*",
        "dotnet-ef",
        "Aspire.*",
        "Fluid.Core",
        "xunit*",
        "Microsoft.NET.Test.Sdk",
        "TngTech.ArchUnitNET*",
        "Microsoft.CodeAnalysis.*",
        "SonarAnalyzer.CSharp",
        "Anthropic*",
    ];
    private static readonly (string DependencyName, string VersionRange)[] RequiredNugetVersionIgnores =
    [
        ("AWSSDK.*", ">= 4.1.0"),
        ("OpenTelemetry*", ">= 1.21.0"),
        ("SonarAnalyzer.CSharp", ">= 10.7.0"),
    ];
    private static readonly string[] RequiredNpmGroups =
    [
        "typescript-eslint",
        "vitest",
        "playwright",
        "vue-vite",
    ];
    private static readonly string[] RequiredDockerDotnetPatterns =
    [
        "mcr.microsoft.com/dotnet/*",
        "dotnet/*",
    ];
    private static readonly string[] ScePythonIgnoredUpdateTypes =
    [
        "version-update:semver-major",
        "version-update:semver-minor",
    ];
    private static readonly string[] SceRIgnoredUpdateTypes =
    [
        "version-update:semver-major",
        "version-update:semver-minor",
        "version-update:semver-patch",
    ];

    private sealed record RepoSnapshot(string RepoRoot, GitIndexReader? Reader)
    {
        public bool Exists(string relativePath) =>
            Reader is { HasAuthoritativeIndex: true }
                ? Reader.FileExists(relativePath)
                : File.Exists(Path.Combine(RepoRoot, relativePath));

        public bool TryReadBytes(string relativePath, out byte[] bytes, out string? error)
        {
            error = null;
            if (Reader is { HasAuthoritativeIndex: true })
            {
                if (Reader.TryReadBytes(relativePath, out bytes))
                {
                    return true;
                }

                error = $"Could not read staged Git index blob for '{relativePath}'.";
                return false;
            }

            try
            {
                bytes = File.ReadAllBytes(Path.Combine(RepoRoot, relativePath));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                bytes = [];
                error = ex.Message;
                return false;
            }
        }

        public bool TryReadText(string relativePath, out string text, out string? error)
        {
            text = string.Empty;
            if (!TryReadBytes(relativePath, out byte[] bytes, out error))
            {
                return false;
            }

            try
            {
                text = StrictUtf8.GetString(StripBom(bytes));
                return true;
            }
            catch (DecoderFallbackException ex)
            {
                error = $"Invalid UTF-8 byte sequence ({ex.Message}).";
                return false;
            }
        }
    }

    private sealed class DependabotGroup
    {
        public HashSet<string> Patterns { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ExcludePatterns { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ParsedDependabotConfig
    {
        public string? Version { get; set; }

        public List<DependabotUpdateEntry> Updates { get; } = [];
    }

    private sealed class DependabotUpdateEntry
    {
        public string? PackageEcosystem { get; set; }

        public string? Directory { get; set; }

        public List<string> Directories { get; } = [];

        public string? TargetBranch { get; set; }

        public Dictionary<string, DependabotGroup> Groups { get; } = new(StringComparer.Ordinal);

        public List<DependabotIgnoreEntry> Ignores { get; } = [];
    }

    private sealed class DependabotIgnoreEntry
    {
        public string? DependencyName { get; set; }

        public bool HasVersions { get; set; }

        public List<string> Versions { get; } = [];

        public bool HasUpdateTypes { get; set; }

        public List<string> UpdateTypes { get; } = [];
    }

    public static int ValidateDependencies(
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating dependency manifests, MSBuild/pnpm cohorts, Dependabot rules, and PostgreSQL digest parity...");

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            var snapshot = new RepoSnapshot(repoRoot, reader);
            var packageManifests = DiscoverPackageManifests(snapshot);
            bool hasSolution = snapshot.Exists("Cadence.sln");
            bool hasPnpmWorkspace = snapshot.Exists("pnpm-workspace.yaml");
            bool hasDotnetTools = snapshot.Exists(".config/dotnet-tools.json");
            bool hasBuildProps = snapshot.Exists("Directory.Build.props");
            bool hasPackagesProps = snapshot.Exists("Directory.Packages.props");
            bool hasDependabot = snapshot.Exists(DependabotRelativePath);
            bool hasPostgresImageSource = snapshot.Exists("packages/testing/src/Persistence/PostgresTestImage.cs");
            bool hasDockerCompose = snapshot.Exists("docker/docker-compose.yml");

            if (!hasSolution &&
                packageManifests.Count == 0 &&
                !hasPnpmWorkspace &&
                !hasDotnetTools &&
                !hasBuildProps &&
                !hasPackagesProps &&
                !hasDependabot &&
                !hasPostgresImageSource &&
                !hasDockerCompose)
            {
                Console.WriteLine("✔ No dependency manifests found, skipping.");
                return 0;
            }

            int errors = 0;
            if (hasSolution)
            {
                errors += ValidateRequiredSolutionFiles(snapshot);
            }

            Dictionary<string, string>? catalog = null;
            if (hasPnpmWorkspace)
            {
                errors += ValidatePnpmWorkspaceCatalog(snapshot, out catalog);
            }

            errors += ValidateWorkspacePackageManifests(snapshot, packageManifests, catalog);

            Dictionary<string, string>? msBuildProps = null;
            if (hasBuildProps)
            {
                errors += ValidateDirectoryBuildProps(snapshot, out msBuildProps);
            }

            if (hasPackagesProps)
            {
                errors += ValidateDirectoryPackagesProps(snapshot, msBuildProps);
            }

            if (hasDotnetTools)
            {
                errors += ValidateDotnetToolsManifest(snapshot, msBuildProps);
            }

            if (hasDependabot)
            {
                errors += ValidateDependabotConfig(snapshot);
            }

            if (hasPostgresImageSource || hasDockerCompose)
            {
                errors += ValidatePostgresDigestParity(snapshot, hasSolution);
            }

            if (errors > 0)
            {
                Console.WriteLine($"\n[ERROR] Found {errors} dependency manifest, cohort, Dependabot, or container digest violation(s).");
                return 1;
            }

            Console.WriteLine("✔ Dependency manifests, cohort pins, Dependabot rules, and PostgreSQL digest checks passed cleanly.");
            return 0;
        }
    }

    private static int ValidateRequiredSolutionFiles(RepoSnapshot snapshot)
    {
        int errors = 0;
        foreach (string requiredPath in RequiredSolutionFiles)
        {
            if (!snapshot.Exists(requiredPath))
            {
                Console.WriteLine($"[ERROR] Missing required dependency manifest or guardrail file '{requiredPath}'.");
                errors++;
            }
        }

        return errors;
    }

    private static List<string> DiscoverPackageManifests(RepoSnapshot snapshot)
    {
        var manifests = new List<string>();
        if (snapshot.Exists("package.json"))
        {
            manifests.Add("package.json");
        }

        if (snapshot.Reader is { HasAuthoritativeIndex: true })
        {
            var indexedChildren = snapshot.Reader.Paths
                .Where(IsChildWorkspacePackageJson)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
            manifests.AddRange(indexedChildren);
            return manifests;
        }

        foreach (string container in WorkspaceContainers)
        {
            string containerPath = Path.Combine(snapshot.RepoRoot, container);
            if (!Directory.Exists(containerPath))
            {
                continue;
            }

            foreach (string childDir in Directory.GetDirectories(containerPath).OrderBy(d => d, StringComparer.Ordinal))
            {
                string childName = Path.GetFileName(childDir);
                string relPkg = $"{container}/{childName}/package.json";
                if (snapshot.Exists(relPkg))
                {
                    manifests.Add(relPkg);
                }
            }
        }

        return manifests;
    }

    private static bool IsChildWorkspacePackageJson(string relativePath)
    {
        string[] parts = relativePath.Split('/');
        return parts.Length == 3 &&
               (string.Equals(parts[0], "apps", StringComparison.Ordinal) ||
                string.Equals(parts[0], "packages", StringComparison.Ordinal)) &&
               string.Equals(parts[2], "package.json", StringComparison.Ordinal);
    }

    private static int ValidatePnpmWorkspaceCatalog(
        RepoSnapshot snapshot,
        out Dictionary<string, string>? catalog)
    {
        const string relativePath = "pnpm-workspace.yaml";
        catalog = null;

        if (!snapshot.TryReadBytes(relativePath, out byte[] bytes, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {readError}");
            return 1;
        }

        var yamlErrors = YamlSyntaxValidatorService.ValidateBytes(bytes, relativePath);
        if (yamlErrors.Count > 0)
        {
            foreach (string err in yamlErrors)
            {
                Console.WriteLine($"[ERROR] {err}");
            }

            return yamlErrors.Count;
        }

        string text = StrictUtf8.GetString(StripBom(bytes));
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inCatalog = false;
        int errors = 0;

        foreach (string rawLine in GitIndexReader.SplitLines(text))
        {
            string trimmed = StripYamlLineComment(rawLine).Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!char.IsWhiteSpace(rawLine[0]))
            {
                inCatalog = string.Equals(trimmed, "catalog:", StringComparison.Ordinal);
                continue;
            }

            if (!inCatalog || !TrySplitMapping(trimmed, out string key, out string rawVal))
            {
                continue;
            }

            string value = UnquoteYamlScalar(rawVal);
            if (key.Length == 0 || value.Length == 0)
            {
                Console.WriteLine($"[ERROR] {relativePath} catalog entry '{rawLine.Trim()}' must specify a non-empty package name and version.");
                errors++;
                continue;
            }

            parsed[key] = value;
        }

        if (parsed.Count == 0)
        {
            Console.WriteLine($"[ERROR] {relativePath} must define a non-empty 'catalog:' section for shared frontend packages (ADR-2527).");
            errors++;
        }

        catalog = parsed;
        return errors;
    }

    private static int ValidateWorkspacePackageManifests(
        RepoSnapshot snapshot,
        IReadOnlyList<string> packageManifests,
        Dictionary<string, string>? catalog)
    {
        int errors = 0;
        var literalOwnersByPackage = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (string relativePath in packageManifests)
        {
            errors += ValidateSinglePackageManifest(snapshot, relativePath, catalog, literalOwnersByPackage);
        }

        if (catalog is not null)
        {
            foreach (var (pkgName, owners) in literalOwnersByPackage.Where(kv => kv.Value.Count > 1))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] Shared npm package '{pkgName}' uses literal versions across multiple manifests ({string.Join(", ", owners)}); move it to 'catalog:' in pnpm-workspace.yaml.");
                errors++;
            }
        }

        return errors;
    }

    private static int ValidateSinglePackageManifest(
        RepoSnapshot snapshot,
        string relativePath,
        Dictionary<string, string>? catalog,
        Dictionary<string, List<string>> literalOwnersByPackage)
    {
        if (!snapshot.TryReadText(relativePath, out string json, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {readError}");
            return 1;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine($"[ERROR] {relativePath} root element must be a JSON object.");
                return 1;
            }

            int errors = 0;
            foreach (string section in DependencySections)
            {
                if (!root.TryGetProperty(section, out var depsObj))
                {
                    continue;
                }

                if (depsObj.ValueKind != JsonValueKind.Object)
                {
                    Console.WriteLine($"[ERROR] {relativePath} property '{section}' must be a JSON object.");
                    errors++;
                    continue;
                }

                errors += ValidatePackageDependencySection(relativePath, section, depsObj, catalog, literalOwnersByPackage);
            }

            return errors;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[ERROR] Failed to parse {relativePath}: {ex.Message}");
            return 1;
        }
    }

    private static int ValidatePackageDependencySection(
        string relativePath,
        string section,
        JsonElement depsObj,
        Dictionary<string, string>? catalog,
        Dictionary<string, List<string>> literalOwnersByPackage)
    {
        int errors = 0;

        foreach (var entry in depsObj.EnumerateObject())
        {
            string? spec = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
            if (string.IsNullOrWhiteSpace(spec))
            {
                Console.WriteLine($"[ERROR] {relativePath} dependency '{entry.Name}' in '{section}' must specify a non-empty version string.");
                errors++;
                continue;
            }

            if (catalog is not null && catalog.ContainsKey(entry.Name) && !string.Equals(spec, "catalog:", StringComparison.Ordinal))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} ({section}) must reference '{entry.Name}' via \"catalog:\" instead of literal '{spec}'.");
                errors++;
            }
            else if (catalog is not null &&
                     !string.Equals(spec, "catalog:", StringComparison.Ordinal) &&
                     !spec.StartsWith("workspace:", StringComparison.Ordinal))
            {
                if (!literalOwnersByPackage.TryGetValue(entry.Name, out var owners))
                {
                    owners = [];
                    literalOwnersByPackage[entry.Name] = owners;
                }

                if (!owners.Contains(relativePath, StringComparer.Ordinal))
                {
                    owners.Add(relativePath);
                }
            }
        }

        foreach (string forbidden in ForbiddenPackages)
        {
            if (depsObj.TryGetProperty(forbidden, out _))
            {
                Console.WriteLine($"[SECURITY VIOLATION] Forbidden client-side crypto package '{forbidden}' detected in {relativePath} ({section}).");
                errors++;
            }
        }

        if (string.Equals(section, "dependencies", StringComparison.Ordinal))
        {
            foreach (string testPkg in ForbiddenRuntimeTestPackages)
            {
                if (depsObj.TryGetProperty(testPkg, out _))
                {
                    Console.WriteLine($"[DEPENDENCY VIOLATION] Test-only package '{testPkg}' is forbidden in runtime 'dependencies' of {relativePath}; move it to 'devDependencies'.");
                    errors++;
                }
            }
        }

        return errors;
    }

    private static int ValidateDirectoryBuildProps(
        RepoSnapshot snapshot,
        out Dictionary<string, string>? properties)
    {
        const string relativePath = "Directory.Build.props";
        properties = null;

        if (!TryLoadXmlDocument(snapshot, relativePath, out var doc))
        {
            return 1;
        }

        var props = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in doc.Root!.Elements("PropertyGroup").SelectMany(pg => pg.Elements()))
        {
            if (element.Attribute("Condition") is null)
            {
                props[element.Name.LocalName] = element.Value.Trim();
            }
        }

        int errors = 0;
        if (!props.TryGetValue("TargetFramework", out string? tfm) || !string.Equals(tfm, "net10.0", StringComparison.Ordinal))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} must keep <TargetFramework>net10.0</TargetFramework> (ADR-2233).");
            errors++;
        }

        foreach (string requiredProp in RequiredMsBuildCohortProperties)
        {
            if (!props.TryGetValue(requiredProp, out string? val) ||
                string.IsNullOrWhiteSpace(val) ||
                val.Contains("$(", StringComparison.Ordinal))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} must define a concrete non-empty <{requiredProp}> cohort version (ADR-2527).");
                errors++;
            }
        }

        errors += ValidateMsBuildCohortVersionBounds(relativePath, props);
        errors += ValidateAppHostSdkAlignment(snapshot);
        errors += ValidateDockerfileDotnetEfAlignment(snapshot, props);

        properties = props;
        return errors;
    }

    private static int ValidateMsBuildCohortVersionBounds(string relativePath, Dictionary<string, string> props)
    {
        int errors = 0;

        if (props.TryGetValue("DotNetServicingVersion", out string? servicing) &&
            !string.IsNullOrWhiteSpace(servicing) &&
            !servicing.StartsWith("10.0.", StringComparison.Ordinal))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} <DotNetServicingVersion> ('{servicing}') must stay on the 10.0.x servicing line for net10.0.");
            errors++;
        }

        if (props.TryGetValue("DotNetAiVersion", out string? aiVersion) &&
            !string.IsNullOrWhiteSpace(aiVersion) &&
            !aiVersion.StartsWith("10.", StringComparison.Ordinal))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} <DotNetAiVersion> ('{aiVersion}') must stay on 10.x while targeting net10.0.");
            errors++;
        }

        if (props.TryGetValue("MassTransitVersion", out string? mtVersion) &&
            !string.IsNullOrWhiteSpace(mtVersion) &&
            !mtVersion.StartsWith("8.", StringComparison.Ordinal))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} <MassTransitVersion> ('{mtVersion}') must stay on Apache-2.0 8.x (ADR-2524).");
            errors++;
        }

        foreach (string otelProp in new[] { "OpenTelemetryVersion", "OpenTelemetryInstrumentationVersion" })
        {
            if (props.TryGetValue(otelProp, out string? otelVal) && IsVersionAtLeast(otelVal, 1, 21))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} <{otelProp}> ('{otelVal}') must stay < 1.21.0 while targeting net10.0 (#5944, ADR-2527).");
                errors++;
            }
        }

        return errors;
    }

    private static int ValidateAppHostSdkAlignment(RepoSnapshot snapshot)
    {
        const string appHostPath = "apps/apphost/src/Cadence.AppHost.csproj";
        if (!snapshot.Exists(appHostPath))
        {
            return 0;
        }

        if (!TryLoadXmlDocument(snapshot, appHostPath, out var doc))
        {
            return 1;
        }

        string? sdkVersion = doc.Descendants("Sdk")
            .FirstOrDefault(e => string.Equals(e.Attribute("Name")?.Value, "Aspire.AppHost.Sdk", StringComparison.Ordinal))
            ?.Attribute("Version")?.Value;

        if (!string.Equals(sdkVersion, "$(AspireHostingVersion)", StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {appHostPath} must set <Sdk Name=\"Aspire.AppHost.Sdk\" Version=\"$(AspireHostingVersion)\" /> (found '{sdkVersion ?? "<missing>"}').");
            return 1;
        }

        return 0;
    }

    private static int ValidateDockerfileDotnetEfAlignment(RepoSnapshot snapshot, Dictionary<string, string> props)
    {
        const string dockerfilePath = "docker/Dockerfile.dotnet";
        if (!snapshot.Exists(dockerfilePath) ||
            !props.TryGetValue("DotNetServicingVersion", out string? servicingVersion) ||
            string.IsNullOrWhiteSpace(servicingVersion))
        {
            return 0;
        }

        if (!snapshot.TryReadText(dockerfilePath, out string dockerfile, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {dockerfilePath}: {readError}");
            return 1;
        }

        var match = DockerfileDotnetEfRegex().Match(dockerfile);
        if (!match.Success || !string.Equals(match.Groups["version"].Value, servicingVersion, StringComparison.Ordinal))
        {
            string actual = match.Success ? match.Groups["version"].Value : "<missing>";
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {dockerfilePath} dotnet-ef version ('{actual}') must match $(DotNetServicingVersion) ('{servicingVersion}') in Directory.Build.props.");
            return 1;
        }

        return 0;
    }

    private static int ValidateDirectoryPackagesProps(
        RepoSnapshot snapshot,
        Dictionary<string, string>? msBuildProps)
    {
        const string relativePath = "Directory.Packages.props";
        if (!TryLoadXmlDocument(snapshot, relativePath, out var doc))
        {
            return 1;
        }

        int errors = ValidateCentralPackageManagementFlags(relativePath, doc);
        var packageElements = doc.Descendants("PackageVersion").ToList();
        if (packageElements.Count == 0)
        {
            Console.WriteLine($"[ERROR] {relativePath} must declare <PackageVersion> pins.");
            return errors + 1;
        }

        var seenPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var literalOwnersByVersion = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var xunitV3Versions = new HashSet<string>(StringComparer.Ordinal);
        var archUnitVersions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in packageElements)
        {
            string id = element.Attribute("Include")?.Value?.Trim() ?? string.Empty;
            string version = element.Attribute("Version")?.Value?.Trim() ?? string.Empty;
            if (id.Length == 0 || version.Length == 0)
            {
                Console.WriteLine($"[ERROR] {relativePath} contains a <PackageVersion> element with an empty Include or Version attribute.");
                errors++;
                continue;
            }

            if (!seenPackageIds.Add(id))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} declares duplicate <PackageVersion Include=\"{id}\" />.");
                errors++;
            }

            errors += ValidateSingleNugetPin(relativePath, id, version, msBuildProps, literalOwnersByVersion);
            TrackTestCohortVersion(id, version, msBuildProps, xunitV3Versions, archUnitVersions);
        }

        if (xunitV3Versions.Count > 1)
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} splits xunit.v3 cohort across multiple versions [{string.Join(", ", xunitV3Versions)}] (CADDEP004).");
            errors++;
        }

        if (archUnitVersions.Count > 1)
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} splits TngTech.ArchUnitNET cohort across multiple versions [{string.Join(", ", archUnitVersions)}] (CADDEP004).");
            errors++;
        }

        foreach (var (literalVersion, packageIds) in literalOwnersByVersion.Where(kv => kv.Value.Count > 1))
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} repeats literal version '{literalVersion}' across [{string.Join(", ", packageIds)}]; extract a shared cohort property in Directory.Build.props.");
            errors++;
        }

        return errors;
    }

    private static void TrackTestCohortVersion(
        string id,
        string version,
        Dictionary<string, string>? msBuildProps,
        HashSet<string> xunitV3Versions,
        HashSet<string> archUnitVersions)
    {
        string resolved = version;
        if (version.StartsWith("$(", StringComparison.Ordinal) &&
            version.EndsWith(')') &&
            msBuildProps is not null &&
            msBuildProps.TryGetValue(version[2..^1], out string? propVal) &&
            !string.IsNullOrWhiteSpace(propVal))
        {
            resolved = propVal;
        }

        if (string.Equals(id, "xunit.v3", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("xunit.v3.", StringComparison.OrdinalIgnoreCase))
        {
            xunitV3Versions.Add(resolved);
        }
        else if (string.Equals(id, "TngTech.ArchUnitNET", StringComparison.OrdinalIgnoreCase) ||
                 id.StartsWith("TngTech.ArchUnitNET.", StringComparison.OrdinalIgnoreCase))
        {
            archUnitVersions.Add(resolved);
        }
    }

    private static int ValidateCentralPackageManagementFlags(string relativePath, XDocument doc)
    {
        int errors = 0;
        string? manageCentrally = doc.Descendants("ManagePackageVersionsCentrally").FirstOrDefault()?.Value.Trim();
        string? transitivePinning = doc.Descendants("CentralPackageTransitivePinningEnabled").FirstOrDefault()?.Value.Trim();

        if (!string.Equals(manageCentrally, "true", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} must keep <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>.");
            errors++;
        }

        if (!string.Equals(transitivePinning, "true", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} must keep <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>.");
            errors++;
        }

        return errors;
    }

    private static int ValidateSingleNugetPin(
        string relativePath,
        string id,
        string version,
        Dictionary<string, string>? msBuildProps,
        Dictionary<string, List<string>> literalOwnersByVersion)
    {
        int errors = 0;

        if (LegacyXunitPackageRegex().IsMatch(id))
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} pins legacy xUnit v2 package '{id}'; migrate to 'xunit.v3' and 'TngTech.ArchUnitNET.xUnitV3' (CADDEP004).");
            errors++;
        }

        errors += ValidateNugetCohortIndirection(relativePath, id, version);

        if (version.StartsWith("$(", StringComparison.Ordinal) && version.EndsWith(')'))
        {
            string propName = version[2..^1];
            if (propName.Length == 0 || propName.Contains('$'))
            {
                Console.WriteLine($"[ERROR] {relativePath} package '{id}' uses malformed MSBuild property expression '{version}'.");
                errors++;
            }
            else if (msBuildProps is not null &&
                     (!msBuildProps.TryGetValue(propName, out string? resolved) || string.IsNullOrWhiteSpace(resolved)))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} package '{id}' references '$({propName})', which is not defined in Directory.Build.props.");
                errors++;
            }
        }
        else
        {
            errors += ValidateNugetLiteralVersionBounds(relativePath, id, version);
            if (!literalOwnersByVersion.TryGetValue(version, out var owners))
            {
                owners = [];
                literalOwnersByVersion[version] = owners;
            }

            owners.Add(id);
        }

        return errors;
    }

    private static int ValidateNugetCohortIndirection(string relativePath, string id, string version)
    {
        if (string.Equals(id, "OpenTelemetry.Exporter.Prometheus.AspNetCore", StringComparison.Ordinal))
        {
            return 0;
        }

        if (id.StartsWith("OpenTelemetry", StringComparison.Ordinal))
        {
            if (!string.Equals(version, "$(OpenTelemetryVersion)", StringComparison.Ordinal) &&
                !string.Equals(version, "$(OpenTelemetryInstrumentationVersion)", StringComparison.Ordinal))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} package '{id}' must reference $(OpenTelemetryVersion) or $(OpenTelemetryInstrumentationVersion) instead of '{version}'.");
                return 1;
            }

            return 0;
        }

        string? expectedProperty = ResolveExpectedNugetCohortProperty(id);
        if (expectedProperty is not null && !string.Equals(version, expectedProperty, StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} package '{id}' must reference '{expectedProperty}' instead of '{version}' (ADR-2527).");
            return 1;
        }

        return 0;
    }

    private static string? ResolveExpectedNugetCohortProperty(string packageId)
    {
        if (string.Equals(packageId, "Microsoft.Extensions.AI", StringComparison.Ordinal) ||
            packageId.StartsWith("Microsoft.Extensions.AI.", StringComparison.Ordinal))
        {
            return "$(DotNetAiVersion)";
        }

        if (packageId.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) ||
            string.Equals(packageId, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            packageId.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) ||
            packageId.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) ||
            packageId.StartsWith("System.Security.Cryptography.", StringComparison.Ordinal) ||
            packageId.StartsWith("System.IO.", StringComparison.Ordinal))
        {
            return "$(DotNetServicingVersion)";
        }

        if (packageId.StartsWith("Aspire.", StringComparison.Ordinal))
        {
            return "$(AspireHostingVersion)";
        }

        if (packageId.StartsWith("Microsoft.IdentityModel.", StringComparison.Ordinal) ||
            packageId.StartsWith("System.IdentityModel.", StringComparison.Ordinal))
        {
            return "$(IdentityModelVersion)";
        }

        if (string.Equals(packageId, "MassTransit", StringComparison.Ordinal) ||
            packageId.StartsWith("MassTransit.", StringComparison.Ordinal))
        {
            return "$(MassTransitVersion)";
        }

        if (string.Equals(packageId, "FluentValidation", StringComparison.Ordinal) ||
            packageId.StartsWith("FluentValidation.", StringComparison.Ordinal))
        {
            return "$(FluentValidationVersion)";
        }

        return null;
    }

    private static int ValidateNugetLiteralVersionBounds(string relativePath, string id, string version)
    {
        if (id.StartsWith("AWSSDK.", StringComparison.Ordinal) && IsVersionAtLeast(version, 4, 1))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} package '{id}' ('{version}') must stay < 4.1.0 while targeting net10.0.");
            return 1;
        }

        if (string.Equals(id, "SonarAnalyzer.CSharp", StringComparison.Ordinal) && IsVersionAtLeast(version, 10, 7))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} package '{id}' ('{version}') must stay < 10.7.0 until baselined.");
            return 1;
        }

        if (string.Equals(id, "Microsoft.CodeAnalysis.NetAnalyzers", StringComparison.Ordinal) && IsVersionAtLeast(version, 11, 0))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} package '{id}' ('{version}') must stay < 11.0.0 while targeting net10.0.");
            return 1;
        }

        if (string.Equals(id, "Fluid.Core", StringComparison.Ordinal) && IsVersionAtLeast(version, 3, 0))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} package '{id}' ('{version}') must stay on 2.x until FluidTemplateRenderer migrates.");
            return 1;
        }

        return 0;
    }

    private static int ValidateDotnetToolsManifest(
        RepoSnapshot snapshot,
        Dictionary<string, string>? msBuildProps)
    {
        const string relativePath = ".config/dotnet-tools.json";
        if (!snapshot.TryReadText(relativePath, out string json, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {readError}");
            return 1;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine($"[ERROR] {relativePath} root element must be a JSON object.");
                return 1;
            }

            if (!root.TryGetProperty("tools", out var toolsObj) || toolsObj.ValueKind != JsonValueKind.Object)
            {
                Console.WriteLine($"[ERROR] {relativePath} must contain a 'tools' JSON object.");
                return 1;
            }

            int errors = 0;
            foreach (var tool in toolsObj.EnumerateObject())
            {
                if (tool.Value.ValueKind != JsonValueKind.Object ||
                    !tool.Value.TryGetProperty("version", out var versionProp) ||
                    versionProp.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(versionProp.GetString()))
                {
                    Console.WriteLine($"[ERROR] {relativePath} tool '{tool.Name}' must define a non-empty string 'version'.");
                    errors++;
                    continue;
                }

                if (string.Equals(tool.Name, "dotnet-ef", StringComparison.Ordinal))
                {
                    errors += ValidateDotnetEfToolEntry(relativePath, tool.Value, versionProp.GetString()!, msBuildProps);
                }
            }

            return errors;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[ERROR] Failed to parse {relativePath}: {ex.Message}");
            return 1;
        }
    }

    private static int ValidateDotnetEfToolEntry(
        string relativePath,
        JsonElement toolObj,
        string version,
        Dictionary<string, string>? msBuildProps)
    {
        int errors = 0;
        if (!toolObj.TryGetProperty("rollForward", out var rollForward) || rollForward.ValueKind != JsonValueKind.False)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} tool 'dotnet-ef' must set \"rollForward\": false.");
            errors++;
        }

        if (msBuildProps is not null &&
            msBuildProps.TryGetValue("DotNetServicingVersion", out string? servicingVersion) &&
            !string.IsNullOrWhiteSpace(servicingVersion) &&
            !string.Equals(version, servicingVersion, StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} tool 'dotnet-ef' version ('{version}') must match $(DotNetServicingVersion) ('{servicingVersion}') in Directory.Build.props.");
            errors++;
        }

        return errors;
    }

    private static int ValidateDependabotConfig(RepoSnapshot snapshot)
    {
        if (!snapshot.TryReadBytes(DependabotRelativePath, out byte[] bytes, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {DependabotRelativePath}: {readError}");
            return 1;
        }

        var yamlErrors = YamlSyntaxValidatorService.ValidateBytes(bytes, DependabotRelativePath);
        if (yamlErrors.Count > 0)
        {
            foreach (string err in yamlErrors)
            {
                Console.WriteLine($"[ERROR] {err}");
            }

            return yamlErrors.Count;
        }

        string content = StrictUtf8.GetString(StripBom(bytes));
        var parseErrors = new List<string>();
        var parsed = ParseDependabotConfig(content, parseErrors);
        if (parseErrors.Count > 0)
        {
            foreach (string parseError in parseErrors)
            {
                Console.WriteLine($"[ERROR] {DependabotRelativePath}: {parseError}");
            }

            return parseErrors.Count;
        }

        int errors = 0;
        if (!string.Equals(parsed.Version, "2", StringComparison.Ordinal))
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: Top-level 'version' must be 2.");
            errors++;
        }

        if (parsed.Updates.Count == 0)
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: 'updates' must define at least one package-ecosystem entry.");
            return errors + 1;
        }

        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        for (int u = 0; u < parsed.Updates.Count; u++)
        {
            errors += ValidateDependabotUpdateEntry(parsed.Updates[u], u, seenTargets);
        }

        errors += ValidateNugetDependabotBlock(DependabotRelativePath, FindDependabotBlock(parsed.Updates, "nuget", "/"));
        errors += ValidateNpmDependabotBlock(DependabotRelativePath, FindDependabotBlock(parsed.Updates, "npm", "/"));
        errors += ValidateDockerDotnetDependabotBlock(DependabotRelativePath, FindDependabotBlock(parsed.Updates, "docker", "/docker"));
        errors += ValidateDockerSceDependabotBlock(DependabotRelativePath, FindDependabotBlock(parsed.Updates, "docker", "/docker/sce/python"), "/docker/sce/python", "python", ScePythonIgnoredUpdateTypes);
        errors += ValidateDockerSceDependabotBlock(DependabotRelativePath, FindDependabotBlock(parsed.Updates, "docker", "/docker/sce/r"), "/docker/sce/r", "r-base", SceRIgnoredUpdateTypes);
        errors += ValidateDockerComposeDependabotBlock(DependabotRelativePath, FindDependabotBlock(parsed.Updates, "docker-compose", "/docker"));

        return errors;
    }

    private static int ValidateDependabotUpdateEntry(
        DependabotUpdateEntry update,
        int updateIndex,
        HashSet<string> seenTargets)
    {
        int errors = 0;
        if (string.IsNullOrWhiteSpace(update.PackageEcosystem))
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: '#/updates/{updateIndex}' is missing a non-empty 'package-ecosystem'.");
            errors++;
        }

        var directories = ResolveUpdateDirectories(update);
        if (directories.Count == 0)
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: '#/updates/{updateIndex}' must specify 'directory' or 'directories'.");
            errors++;
        }

        string targetBranch = update.TargetBranch ?? string.Empty;
        foreach (string dir in directories)
        {
            string targetKey = $"{update.PackageEcosystem}\0{dir}\0{targetBranch}";
            if (!seenTargets.Add(targetKey))
            {
                Console.WriteLine($"[DEPENDABOT VIOLATION] {DependabotRelativePath}: '#/updates/{updateIndex}' duplicates package-ecosystem '{update.PackageEcosystem}' and directory '{dir}'.");
                errors++;
            }
        }

        var seenVersionIgnores = new HashSet<string>(StringComparer.Ordinal);
        var seenUpdateTypeIgnores = new HashSet<string>(StringComparer.Ordinal);
        var seenBareIgnores = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < update.Ignores.Count; i++)
        {
            errors += ValidateDependabotIgnoreEntry(
                update.Ignores[i],
                updateIndex,
                i,
                seenVersionIgnores,
                seenUpdateTypeIgnores,
                seenBareIgnores);
        }

        return errors;
    }

    private static List<string> ResolveUpdateDirectories(DependabotUpdateEntry update)
    {
        if (update.Directories.Count > 0)
        {
            return update.Directories;
        }

        if (!string.IsNullOrWhiteSpace(update.Directory))
        {
            return [update.Directory!];
        }

        return [];
    }

    private static int ValidateDependabotIgnoreEntry(
        DependabotIgnoreEntry ignore,
        int updateIndex,
        int ignoreIndex,
        HashSet<string> seenVersionIgnores,
        HashSet<string> seenUpdateTypeIgnores,
        HashSet<string> seenBareIgnores)
    {
        if (string.IsNullOrWhiteSpace(ignore.DependencyName))
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: '#/updates/{updateIndex}/ignore/{ignoreIndex}' is missing a non-empty 'dependency-name'.");
            return 1;
        }

        int errors = 0;
        string depName = ignore.DependencyName!;

        if (ignore.HasVersions)
        {
            errors += ValidateIgnoreListUniqueness(
                depName,
                ignore.Versions,
                propertyName: "versions",
                updateIndex,
                ignoreIndex,
                seenVersionIgnores);
        }

        if (ignore.HasUpdateTypes)
        {
            errors += ValidateIgnoreListUniqueness(
                depName,
                ignore.UpdateTypes,
                propertyName: "update-types",
                updateIndex,
                ignoreIndex,
                seenUpdateTypeIgnores);
        }

        if (!ignore.HasVersions && !ignore.HasUpdateTypes && !seenBareIgnores.Add(depName))
        {
            Console.WriteLine(
                $"[DEPENDABOT VIOLATION] {DependabotRelativePath}: The property '#/updates/{updateIndex}/ignore/{ignoreIndex}/dependency-name' includes a duplicate ignore for '{depName}'.");
            errors++;
        }

        return errors;
    }

    private static int ValidateIgnoreListUniqueness(
        string depName,
        List<string> values,
        string propertyName,
        int updateIndex,
        int ignoreIndex,
        HashSet<string> seenCombinations)
    {
        if (values.Count == 0)
        {
            Console.WriteLine($"[ERROR] {DependabotRelativePath}: '#/updates/{updateIndex}/ignore/{ignoreIndex}/{propertyName}' must not be empty.");
            return 1;
        }

        int errors = 0;
        var uniqueItems = new HashSet<string>(values, StringComparer.Ordinal);
        if (uniqueItems.Count != values.Count)
        {
            Console.WriteLine(
                $"[DEPENDABOT VIOLATION] {DependabotRelativePath}: The property '#/updates/{updateIndex}/ignore/{ignoreIndex}/{propertyName}' contains duplicate values.");
            errors++;
        }

        bool reportedOverlap = false;
        foreach (string item in uniqueItems)
        {
            string itemKey = $"{depName}\0{item}";
            if (!seenCombinations.Add(itemKey) && !reportedOverlap)
            {
                Console.WriteLine(
                    $"[DEPENDABOT VIOLATION] {DependabotRelativePath}: The property '#/updates/{updateIndex}/ignore/{ignoreIndex}/{propertyName}' includes a duplicate. Ignores must have a unique combination of 'dependency-name' and '{propertyName}'.");
                errors++;
                reportedOverlap = true;
            }
        }

        return errors;
    }

    private static DependabotUpdateEntry? FindDependabotBlock(
        IReadOnlyList<DependabotUpdateEntry> blocks,
        string ecosystem,
        string directory) =>
        blocks.FirstOrDefault(b =>
            string.Equals(b.PackageEcosystem, ecosystem, StringComparison.Ordinal) &&
            ResolveUpdateDirectories(b).Contains(directory, StringComparer.Ordinal));

    private static int ValidateNugetDependabotBlock(string relativePath, DependabotUpdateEntry? block)
    {
        if (block is null)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} is missing the 'nuget' update block for directory '/'.");
            return 1;
        }

        int errors = 0;
        foreach (var (groupName, patterns, excludePatterns) in RequiredNugetGroups)
        {
            if (!block.Groups.TryGetValue(groupName, out var group))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (nuget '/') is missing lockstep group '{groupName}'.");
                errors++;
                continue;
            }

            foreach (string pattern in patterns.Where(p => !group.Patterns.Contains(p)))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (nuget group '{groupName}') must include pattern '{pattern}'.");
                errors++;
            }

            foreach (string excluded in excludePatterns.Where(p => !group.ExcludePatterns.Contains(p)))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (nuget group '{groupName}') must exclude pattern '{excluded}'.");
                errors++;
            }
        }

        foreach (string dep in RequiredNugetMajorIgnores)
        {
            if (!HasIgnoreUpdateType(block, dep, "version-update:semver-major"))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} (nuget '/') must ignore 'version-update:semver-major' for '{dep}'.");
                errors++;
            }
        }

        foreach (var (dep, range) in RequiredNugetVersionIgnores)
        {
            if (!block.Ignores.Any(i =>
                    string.Equals(i.DependencyName, dep, StringComparison.Ordinal) &&
                    !i.HasUpdateTypes &&
                    i.Versions.Contains(range, StringComparer.Ordinal)))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} (nuget '/') must ignore versions ['{range}'] for '{dep}'.");
                errors++;
            }
        }

        return errors;
    }

    private static int ValidateNpmDependabotBlock(string relativePath, DependabotUpdateEntry? block)
    {
        if (block is null)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} is missing the 'npm' update block for directory '/'.");
            return 1;
        }

        int errors = 0;
        foreach (string groupName in RequiredNpmGroups)
        {
            if (!block.Groups.TryGetValue(groupName, out var group) || group.Patterns.Count == 0)
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (npm '/') is missing lockstep group '{groupName}'.");
                errors++;
            }
        }

        if (block.Groups.TryGetValue("vue-vite", out var vueViteGroup) && !vueViteGroup.Patterns.Contains("vue-tsc"))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (npm group 'vue-vite') must include pattern 'vue-tsc'.");
            errors++;
        }

        if (!HasIgnoreUpdateType(block, "@types/node", "version-update:semver-major"))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (npm '/') must ignore 'version-update:semver-major' for '@types/node'.");
            errors++;
        }

        return errors;
    }

    private static int ValidateDockerDotnetDependabotBlock(string relativePath, DependabotUpdateEntry? block)
    {
        if (block is null)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} is missing the 'docker' update block for directory '/docker'.");
            return 1;
        }

        int errors = 0;
        if (!block.Groups.TryGetValue("dotnet-containers", out var group))
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (docker '/docker') is missing lockstep group 'dotnet-containers'.");
            errors++;
        }
        else
        {
            foreach (string pattern in RequiredDockerDotnetPatterns.Where(p => !group.Patterns.Contains(p)))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (docker group 'dotnet-containers') must include pattern '{pattern}'.");
                errors++;
            }
        }

        foreach (string dep in RequiredDockerDotnetPatterns)
        {
            if (!HasIgnoreUpdateType(block, dep, "version-update:semver-major"))
            {
                Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} (docker '/docker') must ignore 'version-update:semver-major' for '{dep}'.");
                errors++;
            }
        }

        return errors;
    }

    private static int ValidateDockerSceDependabotBlock(
        string relativePath,
        DependabotUpdateEntry? block,
        string directory,
        string dependencyName,
        string[] requiredUpdateTypes)
    {
        if (block is null)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} is missing the 'docker' update block for directory '{directory}'.");
            return 1;
        }

        int errors = 0;
        foreach (string updateType in requiredUpdateTypes)
        {
            if (!HasIgnoreUpdateType(block, dependencyName, updateType))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {relativePath} (docker '{directory}') must ignore '{updateType}' for '{dependencyName}'.");
                errors++;
            }
        }

        return errors;
    }

    private static int ValidateDockerComposeDependabotBlock(string relativePath, DependabotUpdateEntry? block)
    {
        if (block is null)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {relativePath} is missing the 'docker-compose' update block for directory '/docker'.");
            return 1;
        }

        bool blocksAllPostgresUpdates = block.Ignores.Any(i =>
            string.Equals(i.DependencyName, "postgres", StringComparison.Ordinal) &&
            !i.HasUpdateTypes &&
            (i.Versions.Count == 0 || i.Versions.Contains(">= 0", StringComparer.Ordinal)));

        if (!blocksAllPostgresUpdates)
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} (docker-compose '/docker') must ignore 'postgres' without 'update-types' scoping so standalone compose @sha256 digest bumps cannot drift from PostgresTestImage.cs (ADR-2533).");
            return 1;
        }

        return 0;
    }

    private static bool HasIgnoreUpdateType(DependabotUpdateEntry block, string dependencyName, string updateType) =>
        block.Ignores.Any(i =>
            string.Equals(i.DependencyName, dependencyName, StringComparison.Ordinal) &&
            !i.HasVersions &&
            i.UpdateTypes.Contains(updateType, StringComparer.Ordinal));

    private static ParsedDependabotConfig ParseDependabotConfig(string content, List<string> parseErrors)
    {
        string[] rawLines = content.ReplaceLineEndings("\n").Split('\n');
        string[] lines = new string[rawLines.Length];
        for (int i = 0; i < rawLines.Length; i++)
        {
            lines[i] = StripYamlLineComment(rawLines[i]);
        }

        var config = new ParsedDependabotConfig();
        bool inUpdates = false;
        int updatesIndent = -1;
        int updateItemIndent = -1;
        DependabotUpdateEntry? currentUpdate = null;

        bool inGroups = false;
        int groupsKeyIndent = -1;
        int groupItemIndent = -1;
        DependabotGroup? currentGroup = null;

        bool inIgnore = false;
        int ignoreKeyIndent = -1;
        int ignoreItemIndent = -1;
        DependabotIgnoreEntry? currentIgnore = null;

        int lineIdx = 0;
        while (lineIdx < lines.Length)
        {
            string line = lines[lineIdx];
            if (string.IsNullOrWhiteSpace(line))
            {
                lineIdx++;
                continue;
            }

            int indent = CountLeadingSpaces(line);
            string trimmed = line[indent..].TrimEnd();
            if (trimmed is "---" or "...")
            {
                lineIdx++;
                continue;
            }

            if (indent == 0)
            {
                inUpdates = false;
                inGroups = false;
                inIgnore = false;
                currentUpdate = null;
                currentGroup = null;
                currentIgnore = null;

                if (TrySplitMapping(trimmed, out string rootKey, out string rootVal))
                {
                    if (string.Equals(rootKey, "version", StringComparison.Ordinal))
                    {
                        config.Version = UnquoteYamlScalar(rootVal);
                    }
                    else if (string.Equals(rootKey, "updates", StringComparison.Ordinal))
                    {
                        inUpdates = true;
                        updatesIndent = 0;
                        updateItemIndent = -1;
                    }
                }

                lineIdx++;
                continue;
            }

            if (!inUpdates || indent <= updatesIndent)
            {
                lineIdx++;
                continue;
            }

            if (IsSequenceItem(trimmed, out string afterUpdateDash) &&
                (updateItemIndent == -1 || indent == updateItemIndent))
            {
                updateItemIndent = indent;
                currentUpdate = new DependabotUpdateEntry();
                config.Updates.Add(currentUpdate);
                inGroups = false;
                groupsKeyIndent = -1;
                groupItemIndent = -1;
                currentGroup = null;
                inIgnore = false;
                ignoreKeyIndent = -1;
                ignoreItemIndent = -1;
                currentIgnore = null;

                if (afterUpdateDash.Length > 0)
                {
                    int virtualIndent = indent + (trimmed.Length - afterUpdateDash.Length);
                    ApplyUpdatePropertyLine(
                        lines,
                        ref lineIdx,
                        virtualIndent,
                        afterUpdateDash,
                        currentUpdate,
                        ref inGroups,
                        ref groupsKeyIndent,
                        ref groupItemIndent,
                        ref currentGroup,
                        ref inIgnore,
                        ref ignoreKeyIndent,
                        ref ignoreItemIndent,
                        parseErrors);
                }

                lineIdx++;
                continue;
            }

            if (currentUpdate is null)
            {
                lineIdx++;
                continue;
            }

            if (inGroups && indent > groupsKeyIndent)
            {
                ApplyGroupSubsectionLine(
                    lines,
                    ref lineIdx,
                    indent,
                    trimmed,
                    currentUpdate,
                    ref groupItemIndent,
                    ref currentGroup,
                    parseErrors);
                lineIdx++;
                continue;
            }

            if (inGroups && indent <= groupsKeyIndent)
            {
                inGroups = false;
                currentGroup = null;
            }

            if (inIgnore && indent > ignoreKeyIndent)
            {
                if (IsSequenceItem(trimmed, out string afterIgnoreDash) &&
                    (ignoreItemIndent == -1 || indent == ignoreItemIndent))
                {
                    ignoreItemIndent = indent;
                    currentIgnore = new DependabotIgnoreEntry();
                    currentUpdate.Ignores.Add(currentIgnore);

                    if (afterIgnoreDash.Length > 0)
                    {
                        int virtualIndent = indent + (trimmed.Length - afterIgnoreDash.Length);
                        ApplyIgnorePropertyLine(lines, ref lineIdx, virtualIndent, afterIgnoreDash, currentIgnore, parseErrors);
                    }

                    lineIdx++;
                    continue;
                }

                if (currentIgnore is not null && (ignoreItemIndent == -1 || indent > ignoreItemIndent))
                {
                    ApplyIgnorePropertyLine(lines, ref lineIdx, indent, trimmed, currentIgnore, parseErrors);
                }

                lineIdx++;
                continue;
            }

            if (inIgnore && indent <= ignoreKeyIndent)
            {
                inIgnore = false;
                currentIgnore = null;
            }

            if (indent > updateItemIndent)
            {
                ApplyUpdatePropertyLine(
                    lines,
                    ref lineIdx,
                    indent,
                    trimmed,
                    currentUpdate,
                    ref inGroups,
                    ref groupsKeyIndent,
                    ref groupItemIndent,
                    ref currentGroup,
                    ref inIgnore,
                    ref ignoreKeyIndent,
                    ref ignoreItemIndent,
                    parseErrors);
            }

            lineIdx++;
        }

        return config;
    }

    private static void ApplyGroupSubsectionLine(
        string[] lines,
        ref int lineIdx,
        int indent,
        string trimmedLine,
        DependabotUpdateEntry update,
        ref int groupItemIndent,
        ref DependabotGroup? currentGroup,
        List<string> parseErrors)
    {
        if (!TrySplitMapping(trimmedLine, out string key, out string val))
        {
            return;
        }

        if (groupItemIndent == -1 || indent == groupItemIndent)
        {
            groupItemIndent = indent;
            if (!update.Groups.TryGetValue(key, out currentGroup))
            {
                currentGroup = new DependabotGroup();
                update.Groups[key] = currentGroup;
            }

            return;
        }

        if (currentGroup is null || indent <= groupItemIndent)
        {
            return;
        }

        switch (key)
        {
            case "patterns":
                foreach (string item in ReadStringListValue(lines, ref lineIdx, indent, val, parseErrors))
                {
                    currentGroup.Patterns.Add(item);
                }

                break;
            case "exclude-patterns":
                foreach (string item in ReadStringListValue(lines, ref lineIdx, indent, val, parseErrors))
                {
                    currentGroup.ExcludePatterns.Add(item);
                }

                break;
        }
    }

    private static void ApplyUpdatePropertyLine(
        string[] lines,
        ref int lineIdx,
        int propIndent,
        string trimmedLine,
        DependabotUpdateEntry update,
        ref bool inGroups,
        ref int groupsKeyIndent,
        ref int groupItemIndent,
        ref DependabotGroup? currentGroup,
        ref bool inIgnore,
        ref int ignoreKeyIndent,
        ref int ignoreItemIndent,
        List<string> parseErrors)
    {
        if (!TrySplitMapping(trimmedLine, out string key, out string val))
        {
            return;
        }

        switch (key)
        {
            case "package-ecosystem":
                update.PackageEcosystem = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "directory":
                update.Directory = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "directories":
                update.Directories.AddRange(ReadStringListValue(lines, ref lineIdx, propIndent, val, parseErrors));
                break;
            case "target-branch":
                update.TargetBranch = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "groups":
                inGroups = true;
                groupsKeyIndent = propIndent;
                groupItemIndent = -1;
                currentGroup = null;
                inIgnore = false;
                break;
            case "ignore":
                inIgnore = true;
                ignoreKeyIndent = propIndent;
                ignoreItemIndent = -1;
                inGroups = false;
                currentGroup = null;
                break;
        }
    }

    private static void ApplyIgnorePropertyLine(
        string[] lines,
        ref int lineIdx,
        int propIndent,
        string trimmedLine,
        DependabotIgnoreEntry ignore,
        List<string> parseErrors)
    {
        if (!TrySplitMapping(trimmedLine, out string key, out string val))
        {
            return;
        }

        switch (key)
        {
            case "dependency-name":
                ignore.DependencyName = ReadScalarValue(lines, ref lineIdx, propIndent, val);
                break;
            case "versions":
                ignore.HasVersions = true;
                ignore.Versions.AddRange(ReadStringListValue(lines, ref lineIdx, propIndent, val, parseErrors));
                break;
            case "update-types":
                ignore.HasUpdateTypes = true;
                ignore.UpdateTypes.AddRange(ReadStringListValue(lines, ref lineIdx, propIndent, val, parseErrors));
                break;
        }
    }

    private static string ReadScalarValue(string[] lines, ref int lineIdx, int propIndent, string inlineVal)
    {
        if (!string.IsNullOrWhiteSpace(inlineVal))
        {
            return UnquoteYamlScalar(inlineVal);
        }

        int next = PeekNextContentLine(lines, lineIdx + 1);
        if (next >= 0)
        {
            int nextIndent = CountLeadingSpaces(lines[next]);
            string nextTrimmed = lines[next][nextIndent..].Trim();
            if (nextIndent > propIndent && !nextTrimmed.StartsWith('-') && !TrySplitMapping(nextTrimmed, out _, out _))
            {
                lineIdx = next;
                return UnquoteYamlScalar(nextTrimmed);
            }
        }

        return string.Empty;
    }

    private static List<string> ReadStringListValue(
        string[] lines,
        ref int lineIdx,
        int propIndent,
        string inlineVal,
        List<string> parseErrors)
    {
        string trimmedInline = inlineVal.Trim();
        if (trimmedInline.StartsWith('['))
        {
            return ParseFlowStringArray(lines, ref lineIdx, trimmedInline, parseErrors);
        }

        if (trimmedInline.Length > 0)
        {
            parseErrors.Add($"Expected YAML sequence on line {lineIdx + 1}, found scalar '{trimmedInline}'.");
            return [];
        }

        int next = PeekNextContentLine(lines, lineIdx + 1);
        if (next < 0)
        {
            return [];
        }

        int nextIndent = CountLeadingSpaces(lines[next]);
        if (nextIndent <= propIndent)
        {
            return [];
        }

        string nextTrimmed = lines[next][nextIndent..].Trim();
        if (nextTrimmed.StartsWith('['))
        {
            lineIdx = next;
            return ParseFlowStringArray(lines, ref lineIdx, nextTrimmed, parseErrors);
        }

        var items = new List<string>();
        int seqIndent = -1;
        while (true)
        {
            int peek = PeekNextContentLine(lines, lineIdx + 1);
            if (peek < 0)
            {
                break;
            }

            int peekIndent = CountLeadingSpaces(lines[peek]);
            if (peekIndent <= propIndent)
            {
                break;
            }

            string peekTrimmed = lines[peek][peekIndent..].TrimEnd();
            if (!IsSequenceItem(peekTrimmed, out string itemText))
            {
                break;
            }

            if (seqIndent == -1)
            {
                seqIndent = peekIndent;
            }
            else if (peekIndent != seqIndent)
            {
                break;
            }

            lineIdx = peek;
            items.Add(UnquoteYamlScalar(itemText));
        }

        return items;
    }

    private static List<string> ParseFlowStringArray(
        string[] lines,
        ref int lineIdx,
        string initialSegment,
        List<string> parseErrors)
    {
        var combined = new StringBuilder(initialSegment);
        while (!HasBalancedClosingBracket(combined.ToString()))
        {
            if (lineIdx + 1 >= lines.Length)
            {
                parseErrors.Add("Unclosed flow sequence '[' in dependabot configuration.");
                return [];
            }

            lineIdx++;
            combined.Append(' ').Append(lines[lineIdx].Trim());
        }

        string full = combined.ToString().Trim();
        int closeIdx = FindMatchingCloseBracket(full);
        if (closeIdx < 0)
        {
            parseErrors.Add("Malformed flow sequence '[' in dependabot configuration.");
            return [];
        }

        string inner = full[1..closeIdx];
        var result = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';

        int i = 0;
        while (i < inner.Length)
        {
            char c = inner[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < inner.Length)
                {
                    current.Append(c).Append(inner[i + 1]);
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < inner.Length && inner[i + 1] == '\'')
                {
                    current.Append("''");
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    quote = '\0';
                }

                current.Append(c);
                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                current.Append(c);
                i++;
                continue;
            }

            if (c == ',')
            {
                string part = current.ToString().Trim();
                if (part.Length > 0)
                {
                    result.Add(UnquoteYamlScalar(part));
                }

                current.Clear();
                i++;
                continue;
            }

            current.Append(c);
            i++;
        }

        string tail = current.ToString().Trim();
        if (tail.Length > 0)
        {
            result.Add(UnquoteYamlScalar(tail));
        }

        return result;
    }

    private static bool HasBalancedClosingBracket(string text) => FindMatchingCloseBracket(text) >= 0;

    private static int FindMatchingCloseBracket(string text)
    {
        int depth = 0;
        char quote = '\0';
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < text.Length)
                {
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    quote = '\0';
                }

                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }

            i++;
        }

        return -1;
    }

    private static int PeekNextContentLine(string[] lines, int startIdx)
    {
        for (int i = startIdx; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSequenceItem(string trimmed, out string remainder)
    {
        if (string.Equals(trimmed, "-", StringComparison.Ordinal))
        {
            remainder = string.Empty;
            return true;
        }

        if (trimmed.Length > 1 && trimmed[0] == '-' && (trimmed[1] == ' ' || trimmed[1] == '\t'))
        {
            remainder = trimmed[1..].TrimStart(' ', '\t');
            return true;
        }

        remainder = string.Empty;
        return false;
    }

    private static bool TrySplitMapping(string segment, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        char quote = '\0';
        int flowDepth = 0;

        int i = 0;
        while (i < segment.Length)
        {
            char c = segment[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < segment.Length)
                {
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < segment.Length && segment[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    quote = '\0';
                }

                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c is '[' or '{')
            {
                flowDepth++;
            }
            else if (c is ']' or '}' && flowDepth > 0)
            {
                flowDepth--;
            }
            else if (flowDepth == 0 && c == ':' && (i + 1 == segment.Length || segment[i + 1] is ' ' or '\t'))
            {
                key = UnquoteYamlScalar(segment[..i].Trim());
                value = segment[(i + 1)..].Trim();
                return key.Length > 0;
            }

            i++;
        }

        return false;
    }

    private static string StripYamlLineComment(string line)
    {
        char quote = '\0';
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\' && i + 1 < line.Length)
                {
                    i += 2;
                    continue;
                }

                if (quote == '\'' && c == '\'' && i + 1 < line.Length && line[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == quote)
                {
                    quote = '\0';
                }

                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                i++;
                continue;
            }

            if (c == '#' && (i == 0 || line[i - 1] is ' ' or '\t'))
            {
                return line[..i].TrimEnd();
            }

            i++;
        }

        return line.TrimEnd();
    }

    private static string UnquoteYamlScalar(string raw)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return trimmed;
    }

    private static int CountLeadingSpaces(string line)
    {
        int count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static int ValidatePostgresDigestParity(RepoSnapshot snapshot, bool hasSolution)
    {
        const string sourcePath = "packages/testing/src/Persistence/PostgresTestImage.cs";
        const string composePath = "docker/docker-compose.yml";
        int errors = 0;

        if (snapshot.Exists(composePath))
        {
            errors += ValidateComposeImageDigests(snapshot, composePath);
        }

        if (!snapshot.Exists(sourcePath))
        {
            if (hasSolution || snapshot.Exists(composePath))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] Missing '{sourcePath}' required to verify PostgreSQL @sha256 image parity (ADR-2533).");
                return errors + 1;
            }

            return errors;
        }

        if (!TryReadCanonicalPostgresImage(snapshot, sourcePath, out string expectedImage, out string expectedTag, out string expectedDigest))
        {
            return errors + 1;
        }

        errors += ValidatePostgresImageInFile(snapshot, composePath, expectedImage, minMatches: 1, required: hasSolution);
        errors += ValidatePostgresImageInFile(snapshot, ".github/workflows/ci.yml", expectedImage, minMatches: 2, required: hasSolution);
        errors += ValidatePostgresImageInFile(snapshot, ".github/workflows/production-pipeline.yml", expectedImage, minMatches: 1, required: hasSolution);
        errors += ValidatePostgresImageInAppHost(snapshot, "apps/apphost/src/Program.cs", expectedTag, expectedDigest, required: hasSolution);
        return errors;
    }

    private static int ValidateComposeImageDigests(RepoSnapshot snapshot, string composePath)
    {
        if (!snapshot.TryReadBytes(composePath, out byte[] bytes, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {composePath}: {readError}");
            return 1;
        }

        var yamlErrors = YamlSyntaxValidatorService.ValidateBytes(bytes, composePath);
        if (yamlErrors.Count > 0)
        {
            foreach (string err in yamlErrors)
            {
                Console.WriteLine($"[ERROR] {err}");
            }

            return yamlErrors.Count;
        }

        string compose = StrictUtf8.GetString(StripBom(bytes));
        int errors = 0;
        foreach (Match match in ComposeImageLineRegex().Matches(compose))
        {
            string image = UnquoteYamlScalar(match.Groups["image"].Value);
            if (!Sha256PinnedImageRegex().IsMatch(image))
            {
                Console.WriteLine(
                    $"[DEPENDENCY VIOLATION] {composePath} image '{image}' must pin both a tag and a 64-hex @sha256 digest.");
                errors++;
            }
        }

        return errors;
    }

    private static bool TryReadCanonicalPostgresImage(
        RepoSnapshot snapshot,
        string sourcePath,
        out string expectedImage,
        out string expectedTag,
        out string expectedDigest)
    {
        expectedImage = string.Empty;
        expectedTag = string.Empty;
        expectedDigest = string.Empty;

        if (!snapshot.TryReadText(sourcePath, out string source, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {sourcePath}: {readError}");
            return false;
        }

        var majorMatch = PostgresMajorConstRegex().Match(source);
        var imageMatch = PostgresImageConstRegex().Match(source);
        if (!majorMatch.Success || !imageMatch.Success)
        {
            Console.WriteLine($"[DEPENDENCY VIOLATION] {sourcePath} must declare 'Major' and digest-pinned 'Image' constants.");
            return false;
        }

        string major = majorMatch.Groups["major"].Value;
        expectedImage = imageMatch.Groups["image"].Value;
        var parsed = PostgresPinnedImageRegex().Match(expectedImage);
        if (!parsed.Success || !string.Equals(parsed.Groups["major"].Value, major, StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {sourcePath} Image ('{expectedImage}') must match 'postgres:{major}-<variant>@sha256:<64hex>' (ADR-2533).");
            return false;
        }

        expectedTag = parsed.Groups["tag"].Value;
        expectedDigest = parsed.Groups["digest"].Value;
        return true;
    }

    private static int ValidatePostgresImageInFile(
        RepoSnapshot snapshot,
        string relativePath,
        string expectedImage,
        int minMatches,
        bool required)
    {
        if (!snapshot.Exists(relativePath))
        {
            if (!required)
            {
                return 0;
            }

            Console.WriteLine($"[DEPENDENCY VIOLATION] Missing '{relativePath}' required for PostgresTestImage.Image parity.");
            return 1;
        }

        if (!snapshot.TryReadText(relativePath, out string content, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {readError}");
            return 1;
        }

        var matches = PostgresReferenceRegex().Matches(content).Select(m => m.Value).ToList();
        if (matches.Count < minMatches)
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} must contain at least {minMatches} PostgreSQL image pin(s) matching '{expectedImage}' (found {matches.Count}).");
            return 1;
        }

        int errors = 0;
        foreach (string actual in matches.Where(m => !string.Equals(m, expectedImage, StringComparison.Ordinal)))
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} PostgreSQL image '{actual}' drifts from PostgresTestImage.Image ('{expectedImage}'); synchronize PostgresTestImage.cs, docker-compose.yml, CI workflows, and AppHost together (ADR-2533).");
            errors++;
        }

        return errors;
    }

    private static int ValidatePostgresImageInAppHost(
        RepoSnapshot snapshot,
        string relativePath,
        string expectedTag,
        string expectedDigest,
        bool required)
    {
        if (!snapshot.Exists(relativePath))
        {
            if (!required)
            {
                return 0;
            }

            Console.WriteLine($"[DEPENDENCY VIOLATION] Missing '{relativePath}' required for PostgresTestImage.Image parity.");
            return 1;
        }

        if (!snapshot.TryReadText(relativePath, out string appHost, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {readError}");
            return 1;
        }

        bool hasTag = appHost.Contains($".WithImageTag(\"{expectedTag}\")", StringComparison.Ordinal);
        bool hasDigest = appHost.Contains($".WithImageSHA256(\"{expectedDigest}\")", StringComparison.Ordinal);
        if (!hasTag || !hasDigest)
        {
            Console.WriteLine(
                $"[DEPENDENCY VIOLATION] {relativePath} must pin PostgreSQL with .WithImageTag(\"{expectedTag}\") and .WithImageSHA256(\"{expectedDigest}\") to match PostgresTestImage.Image (ADR-2533).");
            return 1;
        }

        return 0;
    }

    private static bool TryLoadXmlDocument(RepoSnapshot snapshot, string relativePath, out XDocument doc)
    {
        doc = null!;
        if (!snapshot.TryReadText(relativePath, out string xml, out string? readError))
        {
            Console.WriteLine($"[ERROR] Failed to read {relativePath}: {readError}");
            return false;
        }

        try
        {
            doc = XDocument.Parse(xml, LoadOptions.SetLineInfo);
            if (doc.Root is null || !string.Equals(doc.Root.Name.LocalName, "Project", StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] {relativePath} root XML element must be <Project>.");
                return false;
            }

            return true;
        }
        catch (XmlException ex)
        {
            Console.WriteLine($"[ERROR] Failed to parse XML in {relativePath}: {ex.Message}");
            return false;
        }
    }

    private static bool IsVersionAtLeast(string? rawVersion, int minMajor, int minMinor)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        string core = rawVersion.Split('-')[0];
        string[] parts = core.Split('.');
        if (parts.Length < 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
        {
            return false;
        }

        return major > minMajor || (major == minMajor && minor >= minMinor);
    }

    private static ReadOnlySpan<byte> StripBom(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        return span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF
            ? span[3..]
            : span;
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current)
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        return Directory.GetCurrentDirectory();
    }

    [GeneratedRegex(@"^(?i)(?:xunit(?:$|\.(?!v3(?:$|\.)|analyzers(?:$|\.)|runner\.visualstudio$))|TngTech\.ArchUnitNET\.xUnit$)", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyXunitPackageRegex();

    [GeneratedRegex(@"dotnet-ef\s+--version\s+(?<version>\d+\.\d+\.\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex DockerfileDotnetEfRegex();

    [GeneratedRegex(@"^\s*image:\s*(?<image>\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ComposeImageLineRegex();

    [GeneratedRegex(@"^[^\s:@]+:[^\s@]+@sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256PinnedImageRegex();

    [GeneratedRegex(@"public\s+const\s+int\s+Major\s*=\s*(?<major>\d+)\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex PostgresMajorConstRegex();

    [GeneratedRegex(@"public\s+const\s+string\s+Image\s*=\s*""(?<image>[^""]+)""\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex PostgresImageConstRegex();

    [GeneratedRegex(@"^postgres:(?<tag>(?<major>\d+)-[^\s@""']+)@sha256:(?<digest>[0-9a-f]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex PostgresPinnedImageRegex();

    [GeneratedRegex(@"postgres:[0-9][^\s""']*", RegexOptions.CultureInvariant)]
    private static partial Regex PostgresReferenceRegex();
}
