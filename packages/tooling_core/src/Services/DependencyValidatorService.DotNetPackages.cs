using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Cadence.ToolingCore.Services;

public static partial class DependencyValidatorService
{
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

    [GeneratedRegex(@"^(?i)(?:xunit(?:$|\.(?!v3(?:$|\.)|analyzers(?:$|\.)|runner\.visualstudio$))|TngTech\.ArchUnitNET\.xUnit$)", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyXunitPackageRegex();

    [GeneratedRegex(@"dotnet-ef\s+--version\s+(?<version>\d+\.\d+\.\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex DockerfileDotnetEfRegex();

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
}
